// ReactorSim.Core — simulation engine.
// Fixed tick cadence with a subsystem barrier. Single-threaded: the three phases
// (kinetics, fluids, ambient) run sequentially each tick, which keeps runs deterministic.

using System;
using System.Collections.Generic;

namespace ReactorSim
{
    // Thrown by EnqueueIntent when an intent's class is not allowed in the current session.
    public class IntentRejectedException : InvalidOperationException
    {
        public IntentRejectedException(string message) : base(message) { }
    }

    // One applied (or failed) intent, recorded by Tick at application time (KTD12).
    public sealed class IntentLogEntry
    {
        public long SessionTick;   // monotonic; preset loads do not reset it
        public long Seq;           // monotonic across the session
        public PlantIntent Intent;
        public IntentClass Class;
        public bool Rejected;      // Apply threw or refused
        public string Error = "";
    }

    public class Simulation
    {
        public Plant Plant = new Plant();

        // The sim clock (KTD1): time scale, pause, auto-slow, skip-ahead, casualty freeze.
        public SimClock Clock { get; private set; }

        public Simulation() : this(new Tuning()) { }

        public Simulation(Tuning tuning)
        {
            Clock = new SimClock(tuning);
        }

        // Interactive sessions use the profile's short tick (default 1 s) so a queued control applies
        // within one real second even at the auto-slow scale. Runner scenarios and missions never call this.
        public void ApplyInteractiveTick()
        {
            TicksPerGameMinute = Math.Max(1, (int)Math.Round(SecondsPerGameMinute / Clock.Tuning.InteractiveTickSeconds));
        }

        public int TickCount { get; private set; }
        public double SimSeconds { get; private set; }
        // 12 ticks per game-minute = 5 s of sim time per tick. Chosen from integrator and UI needs: the kinetics solver
        // sub-steps internally at 0.02 s (so the tick length does not affect kinetics accuracy), the slowest thermal lump
        // has a time constant of ~1000 s (>> 5 s), and 5 s divides the 60 s game-minute and the 1 s interactive tick evenly.
        public int TicksPerGameMinute = 12;
        public double SecondsPerGameMinute = 60.0; // sim-time seconds per game-minute

        public double TickDt { get { return SecondsPerGameMinute / TicksPerGameMinute; } }
        public double GameMinutes { get { return SimSeconds / SecondsPerGameMinute; } }

        // Real-time pacing for the console host: 250 ms of wall clock per 5 s tick (20x real time), so a 12-tick
        // game-minute takes 3 s.
        public double RealMsPerTick = 250.0;
        public double RealMsPerGameMinute = 3000.0;

        // Thread-safe intent queue so UI, HTTP server, and network peers can enqueue intents safely.
        private readonly object _intentLock = new object();
        private readonly Queue<PlantIntent> _pendingIntents = new Queue<PlantIntent>();

        // Dev intents (presets, ...) are rejected at the boundary while this is off.
        public volatile bool DevMode;

        // Monotonic tick counter for the whole session: LoadScenarioPreset/RestoreSnapshot do not touch it.
        public long SessionTick { get; private set; }

        private readonly object _intentLogLock = new object();
        private readonly List<IntentLogEntry> _intentLog = new List<IntentLogEntry>();
        private long _intentSeq;

        // Telemetry ring buffer for HTTP / MCP / UI inspection.
        private readonly object _telemetryLock = new object();
        private readonly List<string> _telemetryHistory = new List<string>(256);
        public int MaxTelemetryHistory = 200;

        // Optional per-tick hook (after the plant step and clock observation); Runner timing scenarios use it.
        public Action<Simulation> OnTick;

        // Optional diagnostics hooks (U9).
        public SimLogger Logger;
        public TelemetryRecorder Recorder;

        // Rejects non-finite values at the boundary: Physics.Clamp maps NaN to the lower
        // bound, so a NaN rod target would otherwise mean "withdraw fully".
        public void EnqueueIntent(PlantIntent intent)
        {
            if (intent == null) return;
            if (double.IsNaN(intent.Value) || double.IsInfinity(intent.Value))
                throw new ArgumentException("intent " + intent.Kind + " has non-finite value " + intent.Value, nameof(intent));
            if (intent.Class == IntentClass.Dev && !DevMode)
                throw new IntentRejectedException("intent " + intent.Kind + " is a Dev intent and dev mode is off");
            lock (_intentLock)
            {
                _pendingIntents.Enqueue(intent);
            }
        }

        public List<PlantIntent> DrainPendingIntents()
        {
            var drained = new List<PlantIntent>();
            lock (_intentLock)
            {
                while (_pendingIntents.Count > 0)
                    drained.Add(_pendingIntents.Dequeue());
            }
            return drained;
        }

        public void Tick()
        {
            var appliedIntents = DrainPendingIntents();
            for (int i = 0; i < appliedIntents.Count; i++)
                ApplyLogged(appliedIntents[i]);

            Plant.Step(TickDt);
            SimSeconds += TickDt;
            TickCount++;
            SessionTick++;

            Plant.Assistant.Evaluate(Plant, GameMinutes);
            Plant.StepDispatch(TickDt, Clock.EffectiveScale);
            Plant.AssertFinite();
            Clock.Observe(Plant, SimSeconds);

            string line = Plant.Telemetry(GameMinutes);
            lock (_telemetryLock)
            {
                _telemetryHistory.Add(line);
                if (_telemetryHistory.Count > MaxTelemetryHistory)
                    _telemetryHistory.RemoveAt(0);
            }

            OnTick?.Invoke(this);

            Logger?.LogTick(this, appliedIntents);
            Recorder?.RecordTick(this);
        }

        // Applies one intent now and records it in the intent log (KTD12).
        private void ApplyLogged(PlantIntent intent)
        {
            var entry = new IntentLogEntry
            {
                SessionTick = SessionTick,
                Intent = intent,
                Class = intent.Class
            };
            try
            {
                if (SimClock.IsClockKind(intent.Kind))
                {
                    if (!Clock.Apply(intent, SimSeconds))
                    {
                        entry.Rejected = true;
                        entry.Error = "not applied";
                    }
                }
                else if (intent.Kind == IntentKind.LoadPreset)
                {
                    LoadScenarioPreset(intent.Target);
                }
                else if (!Plant.Apply(intent))
                {
                    entry.Rejected = true;
                    entry.Error = "not applied";
                }
            }
            catch (Exception ex)
            {
                entry.Rejected = true;
                entry.Error = ex.Message;
            }
            lock (_intentLogLock)
            {
                entry.Seq = _intentSeq++;
                _intentLog.Add(entry);
            }
        }

        // While the clock is halted (paused or casualty-frozen) no tick drains the queue, so SCRAM, alarm acknowledge, clock
        // intents and Dev intents are pulled out and applied now; every other intent stays queued in order.
        /// <summary>
        /// True for intents applied at once while the clock is halted (paused or skipping): SCRAM, alarm acknowledge,
        /// clock control and Dev intents. Everything else waits in the queue for the clock to run.
        /// </summary>
        public static bool AppliesWhileHalted(IntentKind kind, IntentClass cls)
        {
            return kind == IntentKind.Scram || kind == IntentKind.AcknowledgeAlarms || SimClock.IsClockKind(kind)
                || cls == IntentClass.Dev;
        }

        private void ApplyImmediateWhileHalted()
        {
            List<PlantIntent> now = null;
            lock (_intentLock)
            {
                int n = _pendingIntents.Count;
                for (int i = 0; i < n; i++)
                {
                    var it = _pendingIntents.Dequeue();
                    if (AppliesWhileHalted(it.Kind, it.Class))
                    {
                        if (now == null) now = new List<PlantIntent>();
                        now.Add(it);
                    }
                    else
                    {
                        _pendingIntents.Enqueue(it);
                    }
                }
            }
            if (now == null) return;
            for (int i = 0; i < now.Count; i++) ApplyLogged(now[i]);
        }

        // Converts real seconds into whole ticks at the clock's effective scale, carrying the remainder.
        // Returns the ticks run. Paused/frozen: no ticks (SCRAM, clock and Dev intents still apply).
        // At most Tuning.MaxTicksPerAdvance ticks run per call; time beyond that is dropped.
        public int Advance(double realSeconds)
        {
            if (double.IsNaN(realSeconds) || double.IsInfinity(realSeconds) || realSeconds < 0.0) realSeconds = 0.0;
            Clock.ElapseReal(realSeconds);
            if (Clock.Halted) ApplyImmediateWhileHalted();

            int ticks = 0;
            double remaining = realSeconds;
            int budget = Clock.Tuning.MaxTicksPerAdvance;
            while (!Clock.Halted && ticks < budget)
            {
                double scale = Clock.EffectiveScale;
                double needReal = (TickDt - Clock.Accumulator) / scale;
                if (remaining + 1e-12 < needReal)
                {
                    Clock.Accumulator += remaining * scale;
                    remaining = 0.0;
                    break;
                }
                remaining = Math.Max(0.0, remaining - needReal);
                Clock.Accumulator = 0.0;
                Tick();
                ticks++;
            }
            if (ticks >= budget) Clock.Accumulator = Math.Min(Clock.Accumulator, TickDt * 0.999999);
            Clock.InterpolationFraction = Math.Min(1.0, Clock.Accumulator / TickDt);
            return ticks;
        }

        public List<IntentLogEntry> GetIntentLogSnapshot()
        {
            lock (_intentLogLock)
            {
                return new List<IntentLogEntry>(_intentLog);
            }
        }

        public List<string> GetTelemetryHistorySnapshot()
        {
            lock (_telemetryLock)
            {
                return new List<string>(_telemetryHistory);
            }
        }

        public PlantSnapshot CaptureSnapshot()
        {
            return Plant.CaptureState(SimSeconds);
        }

        public void RestoreSnapshot(PlantSnapshot snap)
        {
            if (snap == null) return;
            SimSeconds = snap.SimSeconds;
            TickCount = (int)Math.Round(SimSeconds / TickDt);
            Plant.RestoreState(snap);
            Clock.CancelSkip("snapshot restored");
            Clock.ResetObservation();
            Plant.Assistant.Evaluate(Plant, GameMinutes);
        }

        public void LoadScenarioPreset(string presetName)
        {
            Plant.LoadPreset(presetName);
            Plant.ResetDispatch();
            Clock.CancelSkip("preset loaded");
            Clock.ResetObservation();
            SimSeconds = 0.0;
            TickCount = 0;
            Plant.Assistant.Evaluate(Plant, GameMinutes);
        }

        public void RunGameMinutes(double minutes, Action<Simulation> onMinute)
        {
            int ticks = (int)Math.Round(minutes * TicksPerGameMinute);
            for (int i = 0; i < ticks; i++)
            {
                Tick();
                if (onMinute != null && (i + 1) % TicksPerGameMinute == 0)
                    onMinute(this);
            }
        }
    }
}
