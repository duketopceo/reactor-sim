// U4 startup scenario: a scripted operator walks the shipped cold-start-to-grid procedure from the
// sandbox start (SandboxStart.Apply), one step at a time. Each step has a policy keyed by step id that
// issues only Player-class intents through EnqueueIntent. The procedure itself decides when a step is
// done (Procedure.Evaluate over the live snapshot), so the targets in procedures.json and the policies
// here are checked against each other. Every intent is logged so a script can be exported later.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ReactorSim
{
    internal sealed class StartupRun
    {
        public Simulation Sim;
        public Procedure Procedure;
        public double[] TickMinute;            // game minute each step first ticked (-1 = never)
        public List<string> IntentLog = new List<string>();
        public string StateHash;
        public bool Failed;
        public string Failure = "";
        public double TotalMinutes { get { return Sim.GameMinutes; } }
    }

    internal static class StartupScenarios
    {
        internal static bool Verbose;
        internal const double StepCapMinutes = 45.0;   // per-step game-minute cap
        private const int PolicyEveryTicks = 3;         // operator acts every 3 ticks (0.3 game minute)

        private static string F(double v) { return v.ToString("G4", CultureInfo.InvariantCulture); }

        internal static string FindProcedures()
        {
            string rel = Path.Combine("ReactorSim.Unity", "Assets", "StreamingAssets", "procedures.json");
            foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var d = new DirectoryInfo(start);
                while (d != null)
                {
                    string c = Path.Combine(d.FullName, rel);
                    if (File.Exists(c)) return c;
                    d = d.Parent;
                }
            }
            throw new InvalidOperationException("procedures.json not found (run from inside the repo)");
        }

        internal static Procedure LoadProcedure()
        {
            foreach (var p in ProcedureLoader.LoadFromFile(FindProcedures()))
                if (p.Id == "cold-start-to-grid") return p;
            throw new InvalidOperationException("procedures.json has no cold-start-to-grid procedure");
        }

        internal sealed class Op
        {
            public Simulation Sim;
            public StartupRun Run;
            private readonly Dictionary<string, double> _last = new Dictionary<string, double>();

            // Only a changed setting is issued, so the intent log reads as a script, not a per-tick stream.
            private void Set(string key, double v, Func<double, PlantIntent> make)
            {
                double prev;
                if (_last.TryGetValue(key, out prev) && Math.Abs(prev - v) < 1e-9) return;
                _last[key] = v;
                var i = make(v);
                Sim.EnqueueIntent(i);
                Run.IntentLog.Add("m=" + Sim.GameMinutes.ToString("F1", CultureInfo.InvariantCulture) + " " + i.Kind + " " + i.Target + " " + F(i.Value));
            }
            public void Flow(double v) { Set("flow", v, x => PlantIntent.SetPrimaryFlow(x)); }
            public void Heaters(double v) { Set("heat", v, x => PlantIntent.SetHeaters(x)); }
            public void Steam(double v) { Set("steam", Math.Round(Physics.Clamp(v, 0, 1), 3), x => PlantIntent.SetSteamDemand(x)); }
            public void Dilute(double v) { Set("dil", v, x => PlantIntent.SetDilute(x)); }
            public void Borate(double v) { Set("bor", v, x => PlantIntent.SetBorate(x)); }
            public void Bank(int i, double v) { Set("bank" + i, Math.Round(Physics.Clamp(v, 0, 1), 3), x => PlantIntent.SetRodBank(i, x)); }
            public void Vacuum(double v) { Set("vac", v, x => PlantIntent.SetVacuumPump(x)); }
            public void Cooling(double v) { Set("cw", v, x => PlantIntent.SetCoolingWater(x)); }
            public void Auto(bool a) { Set("auto", a ? 1 : 0, x => PlantIntent.SetTurbineAutoLoad(x > 0.5)); }
            public void Grid(double v) { Set("grid", v, x => PlantIntent.SetGridDemand(x)); }
            public void Breaker(bool c) { Set("brk", c ? 1 : 0, x => PlantIntent.SetGeneratorBreaker(x > 0.5)); }
        }

        // Bank A trim toward a power setpoint: small steps, wide deadband (Xe/Doppler make hard trims limit-cycle).
        private static void TrimA(Op op, PlantSnapshot s, double targetPower, double step)
        {
            double band = Math.Max(0.01, 0.12 * targetPower);
            double err = targetPower - s.Power;
            double a = s.BankTarget[3];
            if (err > band) a -= step;
            else if (err < -band) a += step;
            op.Bank(3, Math.Max(0.0, a));
            // Rods alone run out of range as the primary heats (negative temperature feedback): boron carries the rest.
            if (err > 2 * band && s.BankTarget[3] <= 0.02 && s.BoronPpm > 20) op.Dilute(2.5);
            else op.Dilute(0.0);
        }

        // Operator policy for one step. Issues what this step asks for and nothing later.
        private static void Policy(string id, Op op, PlantSnapshot s)
        {
            switch (id)
            {
                case "start-rcps":
                    op.Flow(1.0);
                    break;
                case "pressurize":
                    op.Flow(1.0); op.Heaters(1.0);
                    break;
                case "draw-vacuum":
                    op.Vacuum(1.0); op.Cooling(1.0);
                    break;
                case "pull-shutdown-banks":
                    op.Bank(0, 0); op.Bank(1, 0); op.Bank(2, 0);
                    break;
                case "dilute-boron":
                    op.Dilute(s.BoronPpm > 1260 ? 2.5 : 0.8);
                    break;
                case "withdraw-bank-a":
                    op.Dilute(0);
                    op.Bank(3, 0.5);
                    break;
                case "reach-critical":
                    if (s.Power < 0.02) op.Bank(3, s.BankTarget[3] - 0.01);
                    break;
                case "heat-up":
                    op.Steam(0.05);
                    TrimA(op, s, 0.15, 0.004);
                    break;
                case "auto-load":
                    op.Auto(true);
                    TrimA(op, s, 0.15, 0.004);
                    break;
                case "roll-turbine":
                    // Throttle opens in small steps; the governor (auto-load) closes the breaker when the rotor reaches the band.
                    op.Steam(Math.Min(0.25, s.SteamDemandFrac + 0.006));
                    TrimA(op, s, 0.25, 0.006);
                    break;
                case "close-breaker":
                    op.Steam(Math.Min(0.25, s.SteamDemandFrac + 0.002));
                    TrimA(op, s, 0.25, 0.006);
                    op.Breaker(true);
                    break;
                case "raise-power":
                    op.Steam(Math.Min(0.45, s.SteamDemandFrac + 0.006)); // throttle opens in small steps, rods and boron follow
                    TrimA(op, s, 0.45, 0.008);
                    break;
                case "take-load":
                    op.Steam(Math.Min(0.45, s.SteamDemandFrac + 0.003));
                    TrimA(op, s, 0.45, 0.008);
                    op.Grid(0.25);
                    break;
                default:
                    throw new InvalidOperationException("no operator policy for step \"" + id + "\"");
            }
        }

        internal static string Live(PlantSnapshot s)
        {
            return "P=" + F(s.Power) + " crit=" + (s.IsCritical ? 1 : 0) + " boron=" + F(s.BoronPpm) + " A=" + F(s.BankInsertion[3])
                + " Tpri=" + F(s.PrimaryTempC) + " Ppri=" + F(s.PrimaryPressureBar) + " flow=" + F(s.PrimaryFlowFrac)
                + " cond=" + F(s.CondenserPressureBar) + " steam=" + F(s.SteamDemandFrac) + " rpm=" + F(s.TurbineRpm)
                + " sync=" + (s.GeneratorSynced ? 1 : 0) + " grid=" + F(s.GridOutputFrac) + " rho=" + F(s.TotalReactivityPcm) + "pcm";
        }

        // Runs the procedure from the sandbox start with the step policies.
        internal static StartupRun Run(Procedure proc, Action<string> log = null, Func<Simulation, ProcedureState, bool> stopWhen = null)
        {
            var sim = new Simulation();
            SandboxStart.Apply(sim);
            var run = new StartupRun { Sim = sim, Procedure = proc, TickMinute = new double[proc.Steps.Count] };
            for (int i = 0; i < run.TickMinute.Length; i++) run.TickMinute[i] = -1.0;
            var op = new Op { Sim = sim, Run = run };
            int ticks = 0;
            int lastStep = -1; double stepStart = 0.0;
            int maxTicks = (int)(StepCapMinutes * proc.Steps.Count * sim.TicksPerGameMinute) + 10;
            while (ticks++ < maxTicks)
            {
                var snap = sim.CaptureSnapshot();
                var st = proc.Evaluate(snap);
                for (int i = 0; i < st.Ticked.Length; i++)
                    if (st.Ticked[i] && run.TickMinute[i] < 0.0)
                    {
                        run.TickMinute[i] = sim.GameMinutes;
                        log?.Invoke("  [" + F(sim.GameMinutes) + " min] ticked " + (i + 1) + " " + proc.Steps[i].Id + "  " + Live(snap));
                    }
                int cur = st.CurrentIndex;
                if (cur < 0) break;
                if (stopWhen != null && stopWhen(sim, st)) return run;
                if (cur != lastStep) { lastStep = cur; stepStart = sim.GameMinutes; }
                if (sim.GameMinutes - stepStart > StepCapMinutes)
                    return Fail(run, "step " + (cur + 1) + " \"" + proc.Steps[cur].Id + "\" not ticked within " + F(StepCapMinutes) + " game min; " + Live(snap));
                if (snap.Scrammed) return Fail(run, "SCRAM during step \"" + proc.Steps[cur].Id + "\"; " + Live(snap));
                if (snap.BopTripMask != 0) return Fail(run, "BOP trip (mask " + snap.BopTripMask + ") during step \"" + proc.Steps[cur].Id + "\"; " + Live(snap));
                if ((ticks - 1) % PolicyEveryTicks == 0) Policy(proc.Steps[cur].Id, op, snap);
                sim.Tick();
                sim.Plant.AssertFinite("startup:" + proc.Steps[cur].Id + ":t=" + sim.GameMinutes.ToString("F1", CultureInfo.InvariantCulture));
                if (Verbose && ticks % 10 == 0) log?.Invoke("    " + F(sim.GameMinutes) + " [" + proc.Steps[cur].Id + "] " + Live(sim.CaptureSnapshot()));
            }
            var fin = sim.CaptureSnapshot();
            if (stopWhen != null) return Fail(run, "stop condition never met");
            // Order: the prefix rule makes first-tick minutes non-decreasing; assert it explicitly.
            for (int i = 1; i < run.TickMinute.Length; i++)
                if (run.TickMinute[i] >= 0.0 && run.TickMinute[i] < run.TickMinute[i - 1])
                    return Fail(run, "step " + proc.Steps[i].Id + " ticked before " + proc.Steps[i - 1].Id);
            var fs = proc.Evaluate(fin);
            if (!fs.Complete) return Fail(run, "procedure not complete at end; " + Live(fin));
            if (!fin.GeneratorSynced) return Fail(run, "generator not synced at end; " + Live(fin));
            double target = proc.Steps[proc.Steps.Count - 1].Value;
            if (fin.GridOutputFrac < target) return Fail(run, "grid output " + F(fin.GridOutputFrac) + " below procedure target " + F(target));
            if (fin.Scrammed || fin.BopTripMask != 0) return Fail(run, "scram/trip at end");
            run.StateHash = Verification.Hash(Verification.Capture(sim));
            return run;
        }

        // --verify STARTUP-01. Returns null on pass, else a failure message.
        internal static string Check()
        {
            var proc = LoadProcedure();
            // 1. Determinism: two runs from the same start tick every step at the same minute and end in the same state.
            var a = Run(proc); var b = Run(proc);
            if (a.Failed) return "run A failed: " + a.Failure;
            if (b.Failed) return "run B failed: " + b.Failure;
            for (int i = 0; i < a.TickMinute.Length; i++)
                if (a.TickMinute[i] != b.TickMinute[i]) return "step " + proc.Steps[i].Id + " ticked at " + F(a.TickMinute[i]) + " vs " + F(b.TickMinute[i]);
            if (a.IntentLog.Count != b.IntentLog.Count) return "intent count " + a.IntentLog.Count + " vs " + b.IntentLog.Count;
            if (a.StateHash != b.StateHash) return "final state hash differs between runs: " + a.StateHash + " vs " + b.StateHash;

            int rod = IndexOf(proc, "pull-shutdown-banks");
            int crit = IndexOf(proc, "reach-critical");
            int heat = IndexOf(proc, "heat-up");

            // 2. A SCRAM from power unticks the rod step and everything after it, and leaves the lineup ticked.
            var up = Run(proc, null, (sim, st) => st.CurrentIndex > heat && sim.Plant.Core.Power > 0.1);
            if (up.Failed) return "run to power failed: " + up.Failure;
            var sim2 = up.Sim;
            sim2.EnqueueIntent(PlantIntent.TriggerScram());
            sim2.RunGameMinutes(3.0, s => s.Plant.AssertFinite("STARTUP-01:scram"));
            var after = proc.Evaluate(sim2.CaptureSnapshot());
            for (int i = 0; i < proc.Steps.Count; i++)
            {
                bool want = i < rod;
                if (after.Ticked[i] != want)
                    return "after SCRAM step " + (i + 1) + " " + proc.Steps[i].Id + " ticked=" + after.Ticked[i] + ", expected " + want + "; " + Live(sim2.CaptureSnapshot());
            }
            if (after.CurrentIndex != rod) return "after SCRAM current step is " + after.CurrentIndex + ", expected the rod step " + rod;

            // 3. Playtest order (session 20261108-125042): the turbine throttle opened 0.05 -> 0.11 first, nothing else.
            //    The checklist must still point at step 1 and the plant must stay subcritical after the same game minutes.
            var sim3 = new Simulation();
            SandboxStart.Apply(sim3);
            sim3.EnqueueIntent(PlantIntent.SetSteamDemand(0.05));
            sim3.RunGameMinutes(1.0, null);
            sim3.EnqueueIntent(PlantIntent.SetSteamDemand(0.11));
            sim3.RunGameMinutes(Math.Max(1.0, a.TickMinute[crit] - 1.0), s => s.Plant.AssertFinite("STARTUP-01:playtest"));
            var snap3 = sim3.CaptureSnapshot();
            var st3 = proc.Evaluate(snap3);
            if (st3.CurrentIndex != 0) return "playtest order: current step is " + (st3.CurrentIndex + 1) + ", expected step 1; " + Live(snap3);
            if (snap3.IsCritical || snap3.Power >= 0.02) return "playtest order left the plant critical/powered; " + Live(snap3);
            return null;
        }

        private static int IndexOf(Procedure proc, string id)
        {
            for (int i = 0; i < proc.Steps.Count; i++) if (proc.Steps[i].Id == id) return i;
            throw new InvalidOperationException("procedure has no step \"" + id + "\"");
        }

        private static StartupRun Fail(StartupRun run, string msg)
        {
            run.Failed = true; run.Failure = msg;
            return run;
        }

        // --scenario startup-grid
        public static void VerifyStartupToGrid()
        {
            var proc = LoadProcedure();
            var run = Run(proc, Verbose ? (Action<string>)Console.WriteLine : null);
            if (run.Failed) throw new InvalidOperationException(run.Failure);
            Console.WriteLine();
            Console.WriteLine("  [startup-grid] " + proc.Steps.Count + " steps, " + run.IntentLog.Count + " intents, total " + F(run.TotalMinutes) + " game min");
            double prev = 0.0;
            for (int i = 0; i < proc.Steps.Count; i++)
            {
                Console.WriteLine("    step " + (i + 1).ToString("D2") + " " + proc.Steps[i].Id.PadRight(20) + " ticked at " + F(run.TickMinute[i]) + " min (+" + F(run.TickMinute[i] - prev) + ")");
                prev = run.TickMinute[i];
            }
            Console.WriteLine("    state hash " + run.StateHash);
        }
    }
}
