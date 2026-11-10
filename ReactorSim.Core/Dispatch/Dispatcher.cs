// ReactorSim.Core — grid dispatcher (U15).
// Phones in load requests ("give us 40 MW for ten minutes") once the generator has been synced,
// then scores each one by the AVERAGE delivered generator output over its window.
// Deterministic: all randomness comes from a serialisable splitmix64 state; time is the dispatcher's own
// accumulated sim seconds (it does not depend on Simulation.SimSeconds, which a preset load resets).
// All state is plain fields and lists of plain classes, so it can ride a future save or netcode snapshot.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ReactorSim
{
    public enum RequestStatus { Pending = 0, Active = 1, Met = 2, Missed = 3 }

    public sealed class LoadRequest
    {
        public int Id;
        public double TargetMw;
        public double IssuedAt;          // dispatcher clock, s
        public double WindowStart;
        public double WindowEnd;
        public string Caller = "";
        public RequestStatus Status;
        public double DeliveredMwSeconds; // integral of delivered MW over the part of the window elapsed so far
        public double AverageMw;          // final average over the whole window (set when resolved)

        public double DurationSeconds { get { return WindowEnd - WindowStart; } }
    }

    // Gameplay tuning for the dispatcher (data, not physics). Defaults are the shipped play values.
    public sealed class DispatcherConfig
    {
        public double ToleranceFrac = 0.02;        // met when average >= target * (1 - tolerance)
        public double FirstRequestDelaySeconds = 90.0;   // after the first sync
        public double MinGapSeconds = 120.0;       // quiet time between a resolved request and the next call
        public double MaxGapSeconds = 360.0;
        public double MinLeadSeconds = 60.0;       // notice between the call and the window opening
        public double MaxLeadSeconds = 120.0;
        public double MinTargetMw = 10.0;
        public double MaxTargetMw = 45.0;
        public double TargetStepMw = 5.0;
        public double MinWindowSeconds = 300.0;
        public double MaxWindowSeconds = 600.0;
        public double WindowStepSeconds = 60.0;
        public int HistoryLimit = 8;
    }

    public sealed class Dispatcher
    {
        public DispatcherConfig Config = new DispatcherConfig();

        // ---- serialisable state ----
        public ulong RngState;
        public double Clock;                 // accumulated sim seconds since construction / Reset
        public bool EverSynced;
        public double NextRequestAt = -1.0;  // dispatcher clock for the next call; < 0 = not scheduled
        public int NextId = 1;
        public LoadRequest Current;          // pending or active request, null when none
        public readonly List<LoadRequest> History = new List<LoadRequest>();
        public int MetCount;
        public int MissedCount;
        public int Streak;                   // consecutive met requests

        public ulong InitialSeed;            // seed given at construction; a preset reload restarts from it

        public Dispatcher() : this(1UL) { }
        public Dispatcher(ulong seed) { InitialSeed = seed; RngState = seed; }

        public void Reset(ulong seed)
        {
            RngState = seed;
            Clock = 0.0;
            EverSynced = false;
            NextRequestAt = -1.0;
            NextId = 1;
            Current = null;
            History.Clear();
            MetCount = 0;
            MissedCount = 0;
            Streak = 0;
        }

        // splitmix64: tiny, well mixed, and the whole state is one ulong.
        private ulong NextU64()
        {
            unchecked
            {
                RngState += 0x9E3779B97F4A7C15UL;
                ulong z = RngState;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        private double NextUnit() { return (NextU64() >> 11) * (1.0 / 9007199254740992.0); }

        private double Pick(double min, double max, double step)
        {
            int n = (int)Math.Floor((max - min) / step + 1e-9) + 1;
            int k = (int)(NextUnit() * n);
            if (k >= n) k = n - 1;
            return min + k * step;
        }

        private double Between(double min, double max) { return min + NextUnit() * (max - min); }

        // Issues a request now. Public so tests and scripted scenarios can set one up directly.
        public LoadRequest Issue(double targetMw, double windowStart, double windowEnd, string caller)
        {
            var r = new LoadRequest
            {
                Id = NextId++,
                TargetMw = targetMw,
                IssuedAt = Clock,
                WindowStart = windowStart,
                WindowEnd = windowEnd,
                Caller = caller ?? "",
                Status = RequestStatus.Pending
            };
            Current = r;
            NextRequestAt = -1.0;
            return r;
        }

        // Advances the dispatcher by dt sim seconds. deliveredMw is the generator's output over that step
        // (0 while the breaker is open); synced is the breaker state.
        public void Step(double dt, double deliveredMw, bool synced)
        {
            if (!(dt > 0.0)) return;
            if (double.IsNaN(deliveredMw) || deliveredMw < 0.0) deliveredMw = 0.0;
            double t0 = Clock;
            Clock += dt;

            if (synced && !EverSynced)
            {
                EverSynced = true;
                NextRequestAt = Clock + Config.FirstRequestDelaySeconds;
            }

            var r = Current;
            if (r != null)
            {
                if (r.Status == RequestStatus.Pending && Clock > r.WindowStart) r.Status = RequestStatus.Active;
                if (r.Status == RequestStatus.Active)
                {
                    // Credit only the part of this step inside the window.
                    double a = Math.Max(t0, r.WindowStart);
                    double b = Math.Min(Clock, r.WindowEnd);
                    if (b > a) r.DeliveredMwSeconds += deliveredMw * (b - a);
                    if (Clock >= r.WindowEnd) Resolve(r);
                }
                return;
            }

            if (EverSynced && NextRequestAt >= 0.0 && Clock >= NextRequestAt) IssueRandom();
        }

        private void Resolve(LoadRequest r)
        {
            double dur = Math.Max(1e-9, r.DurationSeconds);
            r.AverageMw = r.DeliveredMwSeconds / dur;
            bool met = r.AverageMw >= r.TargetMw * (1.0 - Config.ToleranceFrac);
            r.Status = met ? RequestStatus.Met : RequestStatus.Missed;
            if (met) { MetCount++; Streak++; } else { MissedCount++; Streak = 0; }
            History.Add(r);
            while (History.Count > Config.HistoryLimit) History.RemoveAt(0);
            Current = null;
            NextRequestAt = Clock + Between(Config.MinGapSeconds, Config.MaxGapSeconds);
        }

        private void IssueRandom()
        {
            double target = Pick(Config.MinTargetMw, Config.MaxTargetMw, Config.TargetStepMw);
            double lead = Between(Config.MinLeadSeconds, Config.MaxLeadSeconds);
            double dur = Pick(Config.MinWindowSeconds, Config.MaxWindowSeconds, Config.WindowStepSeconds);
            double start = Clock + lead;
            Issue(target, start, start + dur, CallerLine(target, lead, dur));
        }

        private string CallerLine(double target, double lead, double dur)
        {
            string[] lines =
            {
                "Grid control. We need {0} MW for {1} minutes, starting in {2} s.",
                "Dispatch here. Cold snap, everyone is on the kettle. {0} MW for {1} minutes, in {2} s.",
                "Evening, it's the grid. Could you do {0} MW for {1} minutes? We start in {2} s.",
                "Big match on the telly. {0} MW for {1} minutes please, from {2} s out.",
                "Load forecast busted again. {0} MW for {1} minutes, window opens in {2} s."
            };
            string line = lines[(int)(NextU64() % (ulong)lines.Length)];
            return string.Format(CultureInfo.InvariantCulture, line,
                target.ToString("0", CultureInfo.InvariantCulture),
                (dur / 60.0).ToString("0", CultureInfo.InvariantCulture),
                lead.ToString("0", CultureInfo.InvariantCulture));
        }

        // Canonical text of the full state, for determinism checks and debugging.
        public string StateString()
        {
            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendFormat(ic, "rng={0};clock={1:G17};synced={2};next={3:G17};id={4};met={5};missed={6};streak={7}",
                RngState, Clock, EverSynced, NextRequestAt, NextId, MetCount, MissedCount, Streak);
            if (Current != null) { sb.Append(";cur="); AppendRequest(sb, Current); }
            foreach (var h in History) { sb.Append(";h="); AppendRequest(sb, h); }
            return sb.ToString();
        }

        private static void AppendRequest(StringBuilder sb, LoadRequest r)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0}|{1:G17}|{2:G17}|{3:G17}|{4:G17}|{5}|{6:G17}|{7:G17}|{8}",
                r.Id, r.TargetMw, r.IssuedAt, r.WindowStart, r.WindowEnd, (int)r.Status, r.DeliveredMwSeconds, r.AverageMw, r.Caller);
        }
    }
}
