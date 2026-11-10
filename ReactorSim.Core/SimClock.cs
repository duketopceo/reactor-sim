// ReactorSim.Core — the sim clock (KTD1). Owns time scale, pause, auto-slow, skip-ahead and the
// casualty freeze. Simulation.Advance converts real seconds into whole ticks using this state;
// hosts (Unity, tests, replays) only supply real seconds. Physics equations are untouched:
// only the number of ticks run per real second changes.

using System;
using System.Collections.Generic;

namespace ReactorSim
{
    public sealed class SimClock
    {
        public Tuning Tuning { get; private set; }

        // Requested scale (sim-seconds per real second), set by the profile default or a clock intent.
        public double Scale { get; private set; }
        public bool Paused { get; private set; }

        // Casualty freeze hook: a host (U4) calls Freeze/Unfreeze; Core only honors it.
        public bool Frozen { get; private set; }
        public string FrozenReason { get; private set; } = "";

        // Auto-slow: engaged by a short reactor period, a BOP trip or a SCRAM; held for a real-time interval.
        private double _holdRealSeconds;
        private bool _periodShort;
        public bool AutoSlow { get { return _periodShort || _holdRealSeconds > 0.0; } }
        public string AutoSlowReason { get; private set; } = "";

        // Skip-ahead: runs at the top of the player range until a stop condition.
        public bool Skipping { get; private set; }
        public string SkipStopReason { get; private set; } = "";
        private double _skipUntilSimSeconds;
        private bool _skipUntilXenonPeak;

        // Sim-seconds accumulated toward the next tick.
        internal double Accumulator;

        // Last-seen plant state for edge detection.
        private bool _prevScrammed;
        private int _prevTripMask;
        private double _prevXenon;
        private bool _observed;
        private readonly HashSet<string> _prevAlarms = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _alarmScratch = new HashSet<string>(StringComparer.Ordinal);

        public SimClock(Tuning tuning)
        {
            Tuning = tuning ?? new Tuning();
            Scale = Tuning.DefaultScale;
        }

        public bool Halted { get { return Paused || Frozen; } }

        public double EffectiveScale
        {
            get
            {
                if (Skipping) return Tuning.MaxScale;
                if (AutoSlow) return Math.Min(Scale, Tuning.AutoSlowScale);
                return Scale;
            }
        }

        // Fraction (0..1) of the way to the next tick, for interpolating instruments between ticks.
        public double InterpolationFraction { get; internal set; }

        public void Freeze(string reason)
        {
            Frozen = true;
            FrozenReason = reason ?? "";
        }

        public void Unfreeze()
        {
            Frozen = false;
            FrozenReason = "";
        }

        internal void CancelSkip(string reason)
        {
            if (!Skipping) return;
            Skipping = false;
            SkipStopReason = reason;
        }

        public void ElapseReal(double realSeconds)
        {
            if (_holdRealSeconds > 0.0)
            {
                _holdRealSeconds = Math.Max(0.0, _holdRealSeconds - realSeconds);
                if (!AutoSlow) AutoSlowReason = "";
            }
        }

        internal static bool IsClockKind(IntentKind kind)
        {
            return kind == IntentKind.SetTimeScale || kind == IntentKind.SetTimeScaleDev
                || kind == IntentKind.PauseClock || kind == IntentKind.ResumeClock || kind == IntentKind.SkipAhead;
        }

        // Applies a clock intent; returns false when the intent is refused. Caller checks IsClockKind first.
        internal bool Apply(PlantIntent intent, double simSeconds)
        {
            switch (intent.Kind)
            {
                case IntentKind.SetTimeScale:
                    Scale = Clamp(intent.Value, Tuning.MinScale, Tuning.MaxScale);
                    return true;
                case IntentKind.SetTimeScaleDev:
                    Scale = Clamp(intent.Value, Tuning.MinScale, Tuning.DevMaxScale);
                    return true;
                case IntentKind.PauseClock:
                    Paused = true;
                    CancelSkip("paused");
                    return true;
                case IntentKind.ResumeClock:
                    Paused = false;
                    return true;
                case IntentKind.SkipAhead:
                    if (!(intent.Value > 0.0)) return false;
                    Skipping = true;
                    SkipStopReason = "";
                    _skipUntilSimSeconds = simSeconds + intent.Value;
                    _skipUntilXenonPeak = string.Equals(intent.Target, "xenon-peak", StringComparison.OrdinalIgnoreCase);
                    return true;
                default:
                    return false;
            }
        }

        // Called after every tick: edge-detects SCRAM / BOP trips / short period / xenon peak.
        internal void Observe(Plant plant, double simSeconds)
        {
            bool scrammed = plant.Scrammed;
            int mask = plant.Bop.TripMask;
            double xenon = plant.Core.Xenon135;
            bool scramEdge = _observed && scrammed && !_prevScrammed;
            bool tripEdge = _observed && (mask & ~_prevTripMask) != 0;

            if (scramEdge) Trigger("scram");
            if (tripEdge) Trigger("bop trip");

            double period = plant.Core.PeriodSeconds;
            _periodShort = period > 0.0 && period < Tuning.AutoSlowPeriodSeconds;
            if (_periodShort)
            {
                Trigger("short period");
            }

            if (!AutoSlow) AutoSlowReason = "";

            // New Caution-or-worse alarm codes since the last tick (skip-ahead stops on any alarm).
            string newAlarm = null;
            _alarmScratch.Clear();
            foreach (var a in plant.Assistant.ActiveAlarms)
            {
                if (a.Severity < AlarmSeverity.Caution) continue;
                // A skip "until the xenon peak" is a request to sit through the xenon transient and its alarms.
                if (_skipUntilXenonPeak && Skipping && a.Code.StartsWith("XENON", StringComparison.Ordinal)) continue;
                _alarmScratch.Add(a.Code);
                if (newAlarm == null && _observed && !_prevAlarms.Contains(a.Code)) newAlarm = a.Code;
            }
            _prevAlarms.Clear();
            _prevAlarms.UnionWith(_alarmScratch);

            if (Skipping)
            {
                if (scramEdge) CancelSkip("scram");
                else if (tripEdge) CancelSkip("bop trip");
                else if (_periodShort) CancelSkip("short period");
                else if (newAlarm != null) CancelSkip("alarm " + newAlarm);
                else if (_skipUntilXenonPeak && scrammed && _observed && xenon < _prevXenon) CancelSkip("xenon peak passed");
                else if (simSeconds >= _skipUntilSimSeconds - 1e-9) CancelSkip("span elapsed");
            }

            _prevScrammed = scrammed;
            _prevTripMask = mask;
            _prevXenon = xenon;
            _observed = true;
        }

        /// <summary>Forget edge-detection history (preset load, snapshot restore) so the next tick is not an edge.</summary>
        internal void ResetObservation()
        {
            _observed = false;
            _prevAlarms.Clear();
        }

        private void Trigger(string reason)
        {
            _holdRealSeconds = Math.Max(_holdRealSeconds, Tuning.AutoSlowHoldSeconds);
            AutoSlowReason = reason;
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
