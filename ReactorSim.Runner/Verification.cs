// ReactorSim verification checks (determinism, numerical, edge-case contract).
// CLI: ReactorSim.Runner --verify [--check <id-prefix>] [--verbose] | --list
// Design and caveats: docs/testing/simulation-verification.md
//
// Every check drives the real ReactorSim.Core types. A check returns null when the
// documented contract holds, or a message naming the state field(s) that violate it.
// Checks marked KnownDefect are expected to violate the contract on the current
// code: they print XFAIL (not counted as failures) and turn into a hard failure
// (XPASS) the day the defect is fixed, so the marker cannot rot silently.
// Passing here is NOT physical validation; see the doc.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ReactorSim
{
    internal static class Verification
    {
        private sealed class Check
        {
            public string Id;
            public string Title;
            public string Kind;            // determinism | numerical | edge | scram
            public string KnownDefect;     // null => must pass; else defect id
            public Func<string> Body;      // null => contract satisfied
        }

        private static readonly List<Check> Checks = new List<Check>();

        // Features absent on master: listed so "not tested" is never mistaken for "passing".
        private static readonly string[][] Deferred =
        {
            new[] { "DEF-SAVE",   "Save/load round trip, corruption, version migration", "no serializer exists on master; Verification.Capture is a test-only canonical view, not a save format" },
            new[] { "DEF-PRNG",   "PRNG stream restoration", "ReactorSim.Core contains no random source; add when sensor noise/faults land" },
            new[] { "DEF-CMD",    "Simultaneous command ordering and duplicate semantics", "no command queue on master (intent API lives in open PR #1)" },
            new[] { "DEF-TRIP",   "Trip hysteresis/latching and threshold chatter", "no automatic protection logic in Core; only manual Scram()" },
            new[] { "DEF-SENSOR", "Sensor bias/dropout/disagreement", "no sensor layer; every reading is process truth" },
            new[] { "DEF-CTRL",   "Controller saturation / anti-windup", "no in-Core controllers; Runner TrimPower is test-harness logic" },
            new[] { "DEF-PUMP",   "Pump trip / coastdown, flow reversal, stuck valves", "PrimaryFlowFrac is a bare input; no pump/valve/network model" },
            new[] { "DEF-TANK",   "Zero/full tank, mass conservation residual", "no mass inventory state; levels are inputs or clamped scalars" },
            new[] { "DEF-SOLVER", "Solver nonconvergence reporting", "no implicit/iterative solver; kinetics uses substepped exponential, thermal uses explicit Euler" },
            new[] { "DEF-PAUSE",  "Pause / time acceleration", "Simulation has no pause state; acceleration is just more Tick() calls (covered by DET-03)" },
            new[] { "DEF-RENDER", "Render-independence", "no render layer in Core; Unity PlantBridge not exercised by the console harness" },
            new[] { "DEF-PERF",   "Performance budgets", "no timing gates defined yet; wall-clock is deliberately not asserted here" },
        };

        static Verification()
        {
            // ---- Determinism / reproducibility (same build, same inputs) ----
            Add("DET-01", "determinism", "Repeat-run equivalence of full state, every tick", null, DetRepeat);
            Add("DET-02", "determinism", "Interleaved runs equal isolated runs (no shared mutable state)", null, DetInterleave);
            Add("DET-03", "determinism", "Chunked run (10x1 min) equals single run (10 min)", null, DetChunking);
            Add("DET-04", "determinism", "State hash is sensitive: one-tick input change and hidden state are visible", null, DetSensitivity);
            // ---- Startup checklist (U4) ----
            Add("STARTUP-01", "scenario", "Startup checklist: deterministic cold-start-to-grid run, SCRAM unticks from the rod step, playtest order leaves step 1", null, StartupScenarios.Check);
            // ---- Numerical verification ----
            Add("NUM-01", "numerical", "Delayed-neutron and decay-heat data sanity (sums, ordering, units)", null, NumData);
            Add("NUM-02", "numerical", "Analytic: subcritical multiplication P=S/|rho| and precursor equilibrium (dt=0.1 s)", null, NumSubcritical);
            Add("NUM-03", "numerical", "Kinetics result is tick-dt independent (internal 0.02 s substeps) vs a fine-dt reference", null, NumConvergence);
            Add("NUM-04", "numerical", "Production tick (dt=5 s) reaches the same subcritical steady state", null, NumProductionTick);
            Add("NUM-05", "numerical", "Thermal energy balance closes (no SG, then with SG sink), explicit boundaries", null, NumEnergy);
            Add("NUM-06", "numerical", "Control-rod integral worth: endpoints, monotonic, symmetric S-curve", null, NumRodWorth);
            Add("NUM-07", "numerical", "Saturation pressure vs IAPWS-IF97 verification points; inverse round trip", null, NumSaturation);
            Add("NUM-08", "numerical", "Decay-heat equilibrium share and heat accounting (informational values printed)", null, NumDecayEquilibrium);
            Add("DOM-01", "numerical", "Saturation property call outside its domain is reported, not silently clamped", "KD-02", DomainSaturation);
            // ---- SCRAM / decay heat on the real plant ----
            Add("SCR-01", "scram", "SCRAM from power: fission collapses, rods in, decay heat bounded and monotone", null, ScramBehavior);
            Add("SCR-02", "scram", "SCRAM latch inhibits rod withdrawal until ResetScram()", "KD-03", ScramLatch);
            // ---- Input / timestep edge cases ----
            Add("EDGE-01", "edge", "dt = 0 advances no integrated state (derived ledger terms may refresh)", null, EdgeZeroDt);
            Add("EDGE-02", "edge", "Non-finite dt is detected by AssertFinite (not silently absorbed)", null, EdgeNonFiniteDtDetected);
            Add("EDGE-03", "edge", "Negative, NaN and Infinity dt are rejected at the Step boundary", null, EdgeBadDtRejected);
            Add("EDGE-04", "edge", "Non-finite command (SetAllRods(NaN)) is rejected at the command boundary", null, EdgeNanCommandRejected);
            Add("EDGE-05", "edge", "NaN command that is accepted is at least detected by AssertFinite", null, EdgeNanCommandDetected);
            Add("EDGE-06", "edge", "Out-of-range bounded commands saturate with documented semantics", null, EdgeSaturation);
            Add("EDGE-07", "edge", "Less primary flow never lowers primary temperature (flow 0 finite)", null, EdgeFlow);
            Add("EDGE-08", "edge", "Loss of heat sink at power stays finite and does not sit on a model clamp", "KD-06", EdgeLossOfSink);
        }

        private static void Add(string id, string kind, string title, string knownDefect, Func<string> body)
        {
            Checks.Add(new Check { Id = id, Kind = kind, Title = title, KnownDefect = knownDefect, Body = body });
        }

        // ------------------------------------------------------------------
        // Runner
        // ------------------------------------------------------------------

        public static int Run(bool listOnly, string filter, bool verbose)
        {
            if (listOnly)
            {
                Console.WriteLine("Verification checks (run: dotnet run --project ReactorSim.Runner -- --verify):");
                foreach (var c in Checks)
                    Console.WriteLine("  " + c.Id.PadRight(8) + c.Kind.PadRight(12) +
                                      (c.KnownDefect == null ? "expect-pass  " : "known-defect " + c.KnownDefect + " ") + c.Title);
                Console.WriteLine("Deferred (NOT tested, NOT passing):");
                foreach (var d in Deferred)
                    Console.WriteLine("  " + d[0].PadRight(10) + d[1] + " -- " + d[2]);
                return 0;
            }

            VerboseValues = verbose;
            int pass = 0, xfail = 0, fail = 0, ran = 0;
            foreach (var c in Checks)
            {
                if (filter != null && !c.Id.StartsWith(filter, StringComparison.OrdinalIgnoreCase)) continue;
                ran++;
                Physics.ResetSanitizeHits();
                string result;
                try { result = c.Body(); }
                catch (Exception ex) { result = "unexpected exception " + ex.GetType().Name + ": " + ex.Message; }
                Physics.ResetSanitizeHits();

                string status;
                if (c.KnownDefect == null)
                {
                    status = result == null ? "PASS " : "FAIL ";
                    if (result == null) pass++; else fail++;
                }
                else if (result != null) { status = "XFAIL"; xfail++; }
                else { status = "XPASS"; fail++; result = "known defect " + c.KnownDefect + " no longer reproduces; remove the marker and update the docs"; }

                Console.WriteLine(status + " " + c.Id.PadRight(8) + c.Title);
                if (result != null && (status != "PASS " || verbose))
                    Console.WriteLine("       " + (c.KnownDefect != null && status == "XFAIL" ? "[" + c.KnownDefect + "] " : "") + result);
            }

            Console.WriteLine();
            Console.WriteLine("=== Verification summary ===");
            Console.WriteLine("checks run: " + ran + "  pass: " + pass + "  xfail (known defects): " + xfail +
                              "  fail: " + fail + "  deferred (not run): " + Deferred.Length);
            Console.WriteLine(fail == 0
                ? "OK: no unexpected failures. XFAIL = documented defect reproduced, see docs/testing/simulation-verification.md."
                : "FAILED: " + fail + " unexpected result(s).");
            Console.WriteLine("Scope: same-build determinism, numerical verification and edge-case contract only; this is not physical validation.");
            return fail == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // Canonical state view + hash
        // ------------------------------------------------------------------

        private static string D(double v)
        {
            // Exact IEEE-754 bit pattern: equality means bitwise equality, no rounding forgiveness.
            return BitConverter.DoubleToInt64Bits(v).ToString("X16", CultureInfo.InvariantCulture);
        }

        internal static List<KeyValuePair<string, string>> Capture(Simulation sim)
        {
            var l = new List<KeyValuePair<string, string>>();
            Action<string, string> a = (k, v) => l.Add(new KeyValuePair<string, string>(k, v));
            var p = sim.Plant;
            var c = p.Core;
            var t = p.Thermal;
            var ch = p.Chemistry;
            a("Sim.SimSeconds", D(sim.SimSeconds));
            a("Sim.TicksPerGameMinute", sim.TicksPerGameMinute.ToString(CultureInfo.InvariantCulture));
            a("Plant.Mode", p.Mode.ToString());
            a("Plant.Scrammed", p.Scrammed ? "1" : "0");
            a("Plant.RatedThermalKW", D(p.RatedThermalKW));
            a("Core.Power", D(c.Power));
            for (int g = 0; g < c.PrecursorGroupCount; g++) a("Core._precursors[" + g + "]", D(c.GetPrecursor(g)));
            a("Core.Iodine135", D(c.Iodine135));
            a("Core.Xenon135", D(c.Xenon135));
            for (int g = 0; g < c.DecayGroupCount; g++) a("Core._decayStore[" + g + "]", D(c.GetDecayStore(g)));
            a("Core.DecayHeatPower", D(c.DecayHeatPower));
            a("Core.RodReactivityPcm", D(c.RodReactivityPcm));
            a("Core.BoronReactivityPcm", D(c.BoronReactivityPcm));
            a("Core.DopplerPcm", D(c.DopplerPcm));
            a("Core.ModeratorPcm", D(c.ModeratorPcm));
            a("Core.XenonPcm", D(c.XenonPcm));
            a("Core.IodinePcm", D(c.IodinePcm));
            a("Core.BaseReactivityPcm", D(c.BaseReactivityPcm));
            for (int i = 0; i < p.Banks.Length; i++)
            {
                var b = p.Banks[i];
                string n = "Banks[" + i + "].";
                a(n + "Insertion", D(b.Insertion));
                a(n + "InsertionTarget", D(b.InsertionTarget));
                a(n + "SpeedPerSec", D(b.SpeedPerSec));
                a(n + "Integrity", D(b.Integrity));
                a(n + "Burnup", D(b.Burnup));
            }
            a("Chemistry.BoronPpm", D(ch.BoronPpm));
            a("Chemistry.Ph", D(ch.Ph));
            a("Chemistry.BorateRate", D(ch.BorateRate));
            a("Chemistry.DiluteRate", D(ch.DiluteRate));
            a("Chemistry.NaohRate", D(ch.NaohRate));
            a("Chemistry.IonExchangeRate", D(ch.IonExchangeRate));
            a("Thermal.FuelTempC", D(t.FuelTempC));
            a("Thermal.PrimaryTempC", D(t.PrimaryTempC));
            a("Thermal.PrimaryPressureBar", D(t.PrimaryPressureBar));
            a("Thermal.PrimaryLevelFrac", D(t.PrimaryLevelFrac));
            a("Thermal.PrimaryFlowFrac", D(t.PrimaryFlowFrac));
            a("Thermal.SecondaryPressureBar", D(t.SecondaryPressureBar));
            a("Thermal.SteamDemandFrac", D(t.SteamDemandFrac));
            a("Thermal.SecondaryLevelFrac", D(t.SecondaryLevelFrac));
            a("Thermal.HeaterPowerFrac", D(t.HeaterPowerFrac));
            a("Thermal.SprayFrac", D(t.SprayFrac));
            a("Thermal.ReliefOpen", t.ReliefOpen ? "1" : "0");
            a("Thermal.SteamLevelFrac", D(t.SteamLevelFrac));
            // Process-wide mutable state that influences results.
            a("Physics.PoisonTimeWarp", D(Physics.PoisonTimeWarp));
            return l;
        }

        internal static string Hash(List<KeyValuePair<string, string>> state)
        {
            var sb = new StringBuilder();
            foreach (var kv in state) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder();
                foreach (byte x in h) hex.Append(x.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        private static string FirstDiff(List<KeyValuePair<string, string>> x, List<KeyValuePair<string, string>> y)
        {
            if (x.Count != y.Count) return "field count " + x.Count + " vs " + y.Count;
            for (int i = 0; i < x.Count; i++)
                if (x[i].Value != y[i].Value) return x[i].Key + " (" + x[i].Value + " vs " + y[i].Value + ")";
            return null;
        }

        // ------------------------------------------------------------------
        // Deterministic scripted input: (tick -> command), applied before the tick.
        // ------------------------------------------------------------------

        private static void Script(Simulation sim, int tick, int scramTick, int perturbTick)
        {
            var p = sim.Plant;
            switch (tick)
            {
                case 0:
                    p.Chemistry.DiluteRate = 1.5; p.Thermal.PrimaryFlowFrac = 1.0;
                    for (int i = 0; i < 3; i++) p.Banks[i].InsertionTarget = 0.0;
                    break;
                case 80: p.Banks[3].InsertionTarget = 0.35; break;
                case 140:
                    p.Chemistry.DiluteRate = 0.0; p.Banks[3].InsertionTarget = 0.12;
                    p.Thermal.SteamDemandFrac = 0.1; p.Thermal.HeaterPowerFrac = 1.0;
                    break;
                case 220: p.Banks[3].InsertionTarget = 0.2; p.Thermal.HeaterPowerFrac = 0.2; break;
            }
            if (tick == perturbTick) p.Banks[3].InsertionTarget = 0.21;   // one-tick-later/different input
            if (tick == scramTick) p.Scram();
        }

        private static List<string> RunHashes(int ticks, int scramTick, int perturbTick, out List<KeyValuePair<string, string>> last)
        {
            var sim = new Simulation();
            var hashes = new List<string>();
            for (int i = 0; i < ticks; i++)
            {
                Script(sim, i, scramTick, perturbTick);
                sim.Tick();
                hashes.Add(Hash(Capture(sim)));
            }
            last = Capture(sim);
            return hashes;
        }

        private static string DetRepeat()
        {
            List<KeyValuePair<string, string>> a, b;
            var ha = RunHashes(300, 260, -1, out a);
            var hb = RunHashes(300, 260, -1, out b);
            for (int i = 0; i < ha.Count; i++)
                if (ha[i] != hb[i])
                    return "tick " + (i + 1) + " hash differs; first field: " + FirstDiff(a, b);
            return null;
        }

        private static string DetInterleave()
        {
            var iso = new Simulation(); var x = new Simulation(); var y = new Simulation();
            for (int i = 0; i < 200; i++)
            {
                Script(iso, i, 180, -1);
                iso.Tick();
            }
            for (int i = 0; i < 200; i++)
            {
                Script(x, i, 180, -1);
                Script(y, i, 100, -1);          // different scenario running between x's ticks
                x.Tick(); y.Tick();
            }
            string d = FirstDiff(Capture(iso), Capture(x));
            return d == null ? null : "interleaving changed result; first field: " + d;
        }

        private static string DetChunking()
        {
            var one = new Simulation(); var many = new Simulation();
            one.Plant.Chemistry.DiluteRate = 1.5; many.Plant.Chemistry.DiluteRate = 1.5;
            one.Plant.Thermal.PrimaryFlowFrac = 1.0; many.Plant.Thermal.PrimaryFlowFrac = 1.0;
            for (int i = 0; i < 3; i++) { one.Plant.Banks[i].InsertionTarget = 0; many.Plant.Banks[i].InsertionTarget = 0; }
            one.RunGameMinutes(10, null);
            for (int m = 0; m < 10; m++) many.RunGameMinutes(1, null);
            string d = FirstDiff(Capture(one), Capture(many));
            return d == null ? null : "chunking changed result; first field: " + d;
        }

        private static string DetSensitivity()
        {
            List<KeyValuePair<string, string>> a, b, c;
            var ha = RunHashes(300, -1, -1, out a);
            var hb = RunHashes(300, -1, 250, out b);
            if (ha[299] == hb[299]) return "late input change left the final hash unchanged (hash insensitive)";
            if (ha[249] != hb[249]) return "hash differs before the perturbation tick";
            var hc = RunHashes(300, 260, -1, out c);
            if (ha[299] == hc[299]) return "SCRAM did not change the final hash";
            // Hidden state must be part of the hash view.
            bool pre = false, dec = false;
            foreach (var kv in a) { if (kv.Key == "Core._precursors[5]") pre = true; if (kv.Key == "Core._decayStore[2]") dec = true; }
            if (!pre || !dec) return "canonical view omits hidden precursor or decay-store fields";
            return null;
        }

        // ------------------------------------------------------------------
        // Numerical verification
        // ------------------------------------------------------------------

        private static string NumData()
        {
            double sum = 0;
            foreach (double f in Physics.BetaFraction) sum += f;
            if (Math.Abs(sum - 1.0) > 1e-9) return "Physics.BetaFraction sums to " + sum.ToString("R", CultureInfo.InvariantCulture) + ", expected 1";
            for (int g = 0; g < 6; g++)
            {
                if (!(Physics.Lambda[g] > 0)) return "Physics.Lambda[" + g + "] not positive";
                if (g > 0 && !(Physics.Lambda[g] > Physics.Lambda[g - 1])) return "Physics.Lambda not strictly ascending at " + g;
            }
            double beta = Physics.BetaTotal, gen = Physics.PromptGenTime;   // copies: consts would be folded
            if (!(beta > 0.003 && beta < 0.01)) return "Physics.BetaTotal outside plausible thermal-fission range";
            if (!(gen > 0)) return "Physics.PromptGenTime not positive";
            // Poison data: half-lives from decay constants (s -> h).
            double tI = Math.Log(2) / Physics.LambdaIodine / 3600, tXe = Math.Log(2) / Physics.LambdaXenon / 3600;
            if (Math.Abs(tI - 6.7) > 0.2) return "I-135 half-life implied " + tI.ToString("F2") + " h";
            if (Math.Abs(tXe - 9.2) > 0.2) return "Xe-135 half-life implied " + tXe.ToString("F2") + " h";
            return null;
        }

        // Drive ReactorCore directly with a fixed total reactivity (all ledger terms zero except Excess).
        private static ReactorCore FixedRhoCore(double rhoPcm)
        {
            var c = new ReactorCore();
            c.BaseReactivityPcm = rhoPcm;
            return c;
        }

        private static ReactorCore RunKinetics(double rhoPcm, double dt, double seconds)
        {
            var c = FixedRhoCore(rhoPcm);
            int n = (int)Math.Round(seconds / dt);
            for (int i = 0; i < n; i++) c.StepKinetics(dt);
            return c;
        }

        private static string NumSubcritical()
        {
            const double rhoPcm = -1000.0;
            double expect = ReactorCore.SourcePower / (-rhoPcm * Physics.PcmPerUnit);
            var c = RunKinetics(rhoPcm, 0.1, 3000);
            double relP = Math.Abs(c.Power - expect) / expect;
            if (relP > 0.01) return "Core.Power=" + c.Power.ToString("E4", CultureInfo.InvariantCulture) + " expected " + expect.ToString("E4", CultureInfo.InvariantCulture) + " (rel err " + relP.ToString("P2", CultureInfo.InvariantCulture) + ")";
            for (int g = 0; g < 6; g++)
            {
                double ceq = Physics.BetaTotal * Physics.BetaFraction[g] * c.Power / (Physics.PromptGenTime * Physics.Lambda[g]);
                double rel = Math.Abs(c.GetPrecursor(g) - ceq) / ceq;
                if (rel > 0.01) return "Core._precursors[" + g + "]=" + c.GetPrecursor(g).ToString("E4", CultureInfo.InvariantCulture) + " expected " + ceq.ToString("E4", CultureInfo.InvariantCulture);
            }
            return null;
        }

        private static string NumConvergence()
        {
            // ReactorCore.StepKinetics substeps at 0.02 s internally, so the result at t=120 s
            // must not depend on the caller's dt: every dt agrees with the fine reference.
            double reference = RunKinetics(-1000, 0.0125, 120).Power;
            foreach (double dt in new[] { 0.4, 0.2, 0.1, 5.0, 6.0 })
            {
                double e = Math.Abs(RunKinetics(-1000, dt, 120).Power - reference) / reference;
                if (VerboseValues) Console.WriteLine("       NUM-03 dt=" + dt.ToString(CultureInfo.InvariantCulture) + " rel.err=" + e.ToString("E2", CultureInfo.InvariantCulture));
                if (e > 1e-4)
                    return "Core.Power at t=120 s with dt=" + dt.ToString(CultureInfo.InvariantCulture) + " differs from the dt=0.0125 reference by " + e.ToString("E2", CultureInfo.InvariantCulture);
            }
            return null;
        }

        private static bool VerboseValues { get; set; }

        private static string NumProductionTick()
        {
            var sim = new Simulation();
            double dt = sim.TickDt;                         // 5 s
            double expect = ReactorCore.SourcePower / (1000.0 * Physics.PcmPerUnit);
            var c = RunKinetics(-1000, dt, 3000);
            double rel = Math.Abs(c.Power - expect) / expect;
            if (rel > 0.05)
                return "at Simulation.TickDt=" + dt.ToString("F1", CultureInfo.InvariantCulture) + " s Core.Power=" + c.Power.ToString("E4", CultureInfo.InvariantCulture) +
                       " vs analytic " + expect.ToString("E4", CultureInfo.InvariantCulture) + " (" + rel.ToString("P0", CultureInfo.InvariantCulture) +
                       " off); Core._precursors[5]=" + c.GetPrecursor(5).ToString("E2", CultureInfo.InvariantCulture) +
                       " (explicit Euler needs lambda*dt<2; lambda[5]*dt=" + (Physics.Lambda[5] * dt).ToString("F1", CultureInfo.InvariantCulture) + ")";
            return null;
        }

        private static string NumEnergy()
        {
            // Case 1: no steam demand, pumps off, from cold. dE = (Q - qAmb - qSg) dt exactly
            // (fuel<->primary flux cancels). The SG still sinks heat by natural circulation
            // (15 % effectiveness) once the primary passes the secondary saturation temperature.
            var th = new ThermalHydraulics();
            double dt = 6.0, q = 50000.0;
            for (int i = 0; i < 40; i++)
            {
                double tf0 = th.FuelTempC, tp0 = th.PrimaryTempC;
                double qSg0 = SgSinkKw(th);
                th.Step(dt, q);
                double dE = th.FuelMassCp * (th.FuelTempC - tf0) + th.PrimaryMassCp * (th.PrimaryTempC - tp0);
                double qAmb0 = th.AmbientLossKWK * Math.Max(0, tp0 - 25.0);
                double expect = (q - qAmb0 - qSg0) * dt;
                if (Math.Abs(dE - expect) > 1e-9 * (q + qAmb0 + qSg0) * dt)
                    return "step " + i + ": dE(Thermal.FuelTempC,Thermal.PrimaryTempC)=" + dE.ToString("R", CultureInfo.InvariantCulture) + " kJ expected " + expect.ToString("R", CultureInfo.InvariantCulture) + " kJ";
            }

            // Case 2: SG sink. Oracle recomputes the sink from public pre-step state using the
            // ThermalHydraulics model (flow/tube-coverage effectiveness, sink at SecondaryTempC).
            // The pressurizer heater only biases pressure, so it adds no energy here.
            var t2 = new ThermalHydraulics { SteamDemandFrac = 0.6, PrimaryFlowFrac = 1.0, HeaterPowerFrac = 0.5 };
            int sgSteps = 0;
            for (int i = 0; i < 600; i++)
            {
                double tf0 = t2.FuelTempC, tp0 = t2.PrimaryTempC;
                double qSg = SgSinkKw(t2);
                double qHeat = 90000.0;
                t2.Step(dt, qHeat);
                if (t2.PrimaryTempC > Physics.PrimaryTempMaxC - 0.01 || t2.FuelTempC > Physics.FuelTempMaxC - 0.01)
                    return "model clamp reached at step " + i + " (energy accounting excludes clamps); lower the test heat";
                if (qSg > 0) sgSteps++;
                double qAmb = t2.AmbientLossKWK * Math.Max(0, tp0 - 25.0);
                double dE = t2.FuelMassCp * (t2.FuelTempC - tf0) + t2.PrimaryMassCp * (t2.PrimaryTempC - tp0);
                double expect = (qHeat - qSg - qAmb) * dt;
                double scale = (qHeat + qSg + qAmb) * dt;     // throughput, so cancellation noise is not flagged
                if (Math.Abs(dE - expect) > 1e-9 * scale)
                    return "SG case step " + i + ": dE=" + dE.ToString("R", CultureInfo.InvariantCulture) + " kJ expected " + expect.ToString("R", CultureInfo.InvariantCulture) + " kJ";
            }
            if (sgSteps < 10) return "SG branch exercised only " + sgSteps + " step(s); test did not cover the sink";
            return null;
        }

        // Oracle for the primary->SG sink, recomputed from public pre-step state using the
        // ThermalHydraulics model (flow + tube-coverage effectiveness, no reverse transfer).
        private static double SgSinkKw(ThermalHydraulics t)
        {
            double coverage = Physics.Clamp(t.SecondaryLevelFrac / 0.25, 0.1, 1.0);
            double sgEff = (0.15 + 0.85 * Physics.Clamp(t.PrimaryFlowFrac, 0, 1)) * coverage;
            return Math.Max(0, t.UaPrimaryToSg * sgEff * (t.PrimaryTempC - t.SecondaryTempC));
        }

        private static string NumRodWorth()
        {
            var b = new ControlRodBank("T", 18, -3000);
            if (Math.Abs(b.ReactivityPcmAt(0)) > 1e-12) return "worth at insertion 0 is " + b.ReactivityPcmAt(0);
            if (Math.Abs(b.ReactivityPcmAt(1) - b.WorthPcm) > 1e-9) return "worth at insertion 1 is " + b.ReactivityPcmAt(1) + ", expected " + b.WorthPcm;
            double prev = 0;
            for (int i = 1; i <= 100; i++)
            {
                double w = b.ReactivityPcmAt(i / 100.0);
                if (w > prev + 1e-12) return "integral worth not monotone at insertion " + (i / 100.0);   // worth is negative, more insertion = more negative
                prev = w;
            }
            if (Math.Abs(b.ReactivityPcmAt(0.5) - 0.5 * b.WorthPcm) > 1e-9) return "S-curve not symmetric about mid-travel";
            return null;
        }

        private static string NumSaturation()
        {
            // Fixture provenance: IAPWS-IF97 saturation-pressure verification points
            // (T in K -> p in bar); values cross-checked with the python 'iapws' 1.5.5
            // package on 2026-11-07. Tolerance 0.5 % because Physics uses a Wagner-form fit.
            double[][] pts =
            {
                new[] { 300.0, 0.03536589413 },
                new[] { 373.15, 1.014179779 },
                new[] { 500.0, 26.38897756 },
                new[] { 573.15, 85.87708329 },
                new[] { 600.0, 123.4431458 },
                new[] { 640.0, 202.6594471 },
            };
            foreach (var pt in pts)
            {
                double got = Physics.SaturationPressureBar(pt[0]);
                double rel = Math.Abs(got - pt[1]) / pt[1];
                if (rel > 0.005)
                    return "Physics.SaturationPressureBar(" + pt[0].ToString(CultureInfo.InvariantCulture) + " K)=" + got.ToString("G6", CultureInfo.InvariantCulture) + " bar, reference " + pt[1].ToString("G6", CultureInfo.InvariantCulture);
                double back = Physics.SaturationTempK(got);
                if (Math.Abs(back - pt[0]) > 0.05)
                    return "SaturationTempK round trip at " + pt[0] + " K returned " + back.ToString("F3", CultureInfo.InvariantCulture);
            }
            double last = 0;
            for (double tk = 280; tk <= 645; tk += 5)
            {
                double ps = Physics.SaturationPressureBar(tk);
                if (!(ps > last)) return "saturation pressure not strictly increasing at " + tk + " K";
                last = ps;
            }
            return null;
        }

        private static string NumDecayEquilibrium()
        {
            // Raise power with a temporary +100 pcm step, then hold total reactivity at 0 so
            // power is quasi-constant while the slow decay groups equilibrate.
            var c = FixedRhoCore(100);
            double dt = 0.1;
            for (int i = 0; i < 20000 && c.Power < 1.0; i++) c.StepKinetics(dt);
            if (c.Power < 1.0) return "could not raise Core.Power to 1 for the fixture";
            c.BaseReactivityPcm = 0;
            for (int i = 0; i < 60000; i++) { c.StepKinetics(dt); c.StepDecayHeat(dt); }
            double ratio = c.DecayHeatPower / c.Power;
            double sumF = 0.031 + 0.022 + 0.010;       // Core._decayGroups (private) equilibrium shares
            if (Math.Abs(ratio - sumF) / sumF > 0.05)
                return "Core.DecayHeatPower/Core.Power=" + ratio.ToString("F4", CultureInfo.InvariantCulture) + " expected ~" + sumF.ToString("F3", CultureInfo.InvariantCulture);
            if (Math.Abs(c.TotalHeatPower - (Physics.PromptHeatFraction * c.Power + c.DecayHeatPower)) > 1e-15) return "TotalHeatPower != PromptHeatFraction * Power + DecayHeatPower";
            if (VerboseValues)
                Console.WriteLine("       NUM-08 P=" + c.Power.ToString("F4", CultureInfo.InvariantCulture) + " DH/P=" + ratio.ToString("F4", CultureInfo.InvariantCulture) + " TotalHeat/P=" + (c.TotalHeatPower / c.Power).ToString("F4", CultureInfo.InvariantCulture));
            return null;
        }

        private static string DomainSaturation()
        {
            double above = Physics.SaturationPressureBar(700.0);   // above the critical temperature (647.096 K)
            double high = Physics.SaturationTempK(500.0);          // above critical pressure (220.64 bar)
            var problems = new List<string>();
            if (Physics.IsFinite(above)) problems.Add("SaturationPressureBar(700 K) returned " + above.ToString("F2", CultureInfo.InvariantCulture) + " bar with no fault");
            if (Physics.IsFinite(high)) problems.Add("SaturationTempK(500 bar) returned " + high.ToString("F2", CultureInfo.InvariantCulture) + " K with no fault");
            return problems.Count == 0 ? null : string.Join("; ", problems.ToArray());
        }

        // ------------------------------------------------------------------
        // SCRAM / decay heat
        // ------------------------------------------------------------------

        private static string ScramBehavior()
        {
            var sim = PlantDriver.BringToPower(0.7, 0.75);
            var p = sim.Plant;
            double pBefore = p.Core.Power, dhBefore = p.Core.DecayHeatPower;
            if (pBefore < 0.2) return "precondition failed: Core.Power=" + pBefore;
            p.Scram();
            if (!(p.Core.Power <= 1.0e-4 + 1e-15)) return "Core.Power after Scram()=" + p.Core.Power.ToString("E3", CultureInfo.InvariantCulture);
            if (!p.Scrammed) return "Plant.Scrammed not set";
            sim.Tick();
            foreach (var b in p.Banks)
                if (b.Insertion < 1.0 - 1e-12) return "Banks[" + b.Name + "].Insertion=" + b.Insertion + " after one production tick of scram drive";
            if (!(p.Core.TotalReactivityPcm < 0)) return "Core.TotalReactivityPcm=" + p.Core.TotalReactivityPcm + " not negative after SCRAM";
            double dh0 = p.Core.DecayHeatPower;
            if (!(dh0 > 0)) return "Core.DecayHeatPower not positive after SCRAM";
            double sumF = 0.063;
            if (dh0 > sumF * Math.Max(pBefore, 0.0) * 1.02 + 1e-9 && dh0 > dhBefore * 1.02)
                return "Core.DecayHeatPower " + dh0.ToString("E3", CultureInfo.InvariantCulture) + " exceeds 6.3% of pre-SCRAM power (" + (sumF * pBefore).ToString("E3", CultureInfo.InvariantCulture) + ")";
            double prev = dh0;
            for (int i = 0; i < 100; i++)
            {
                sim.Tick();
                p.AssertFinite("SCR-01:tick" + i);
                double dh = p.Core.DecayHeatPower;
                if (dh > prev * (1 + 1e-9) + 1e-15) return "Core.DecayHeatPower rose at tick " + i + ": " + prev.ToString("E4", CultureInfo.InvariantCulture) + " -> " + dh.ToString("E4", CultureInfo.InvariantCulture);
                if (!(dh > 0)) return "Core.DecayHeatPower vanished at tick " + i;
                if (!p.Scrammed) return "Plant.Scrammed unlatched without ResetScram() at tick " + i;
                prev = dh;
            }
            if (!(prev < dh0)) return "decay heat did not decline";
            // Total heat at 10 game minutes must still be dominated by decay heat (> fission).
            if (!(p.Core.DecayHeatPower > p.Core.Power)) return "after 10 min Core.DecayHeatPower <= Core.Power: decay heat should dominate";
            return null;
        }

        private static string ScramLatch()
        {
            var sim = new Simulation();
            var p = sim.Plant;
            p.Scram();
            p.SetAllRods(0.0);                      // operator asks for withdrawal while tripped
            sim.Tick();
            double min = 1.0;
            foreach (var b in p.Banks) min = Math.Min(min, b.Insertion);
            return min < 1.0 - 1e-12
                ? "with Plant.Scrammed=true, Plant.SetAllRods(0) withdrew rods: min Banks[*].Insertion=" + min.ToString("F3", CultureInfo.InvariantCulture) + " after one tick at scram drive speed"
                : null;
        }

        // ------------------------------------------------------------------
        // Edge cases
        // ------------------------------------------------------------------

        private static Simulation MidRun()
        {
            var sim = new Simulation();
            for (int i = 0; i < 150; i++) { Script(sim, i, -1, -1); sim.Tick(); }
            return sim;
        }

        private static string EdgeZeroDt()
        {
            var sim = MidRun();
            var before = Capture(sim);
            sim.Plant.Step(0.0);
            // Contract: dt = 0 advances no dynamic (integrated) state. Derived reactivity-ledger
            // terms are recomputed from current temperatures on every Step, so they may refresh
            // (operator-split lag of one tick; see docs/testing/simulation-verification.md).
            var derived = new HashSet<string> { "Core.RodReactivityPcm", "Core.BoronReactivityPcm", "Core.DopplerPcm",
                "Core.ModeratorPcm", "Core.XenonPcm", "Core.IodinePcm" };
            var after = Capture(sim);
            for (int i = 0; i < before.Count; i++)
                if (before[i].Value != after[i].Value && !derived.Contains(before[i].Key))
                    return "Plant.Step(0) changed integrated state " + before[i].Key + " (" + before[i].Value + " vs " + after[i].Value + ")";
            return null;
        }

        private static string EdgeNonFiniteDtDetected()
        {
            foreach (double dt in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var sim = MidRun();
                Physics.ResetSanitizeHits();
                try { sim.Plant.Step(dt); } catch (Exception) { Physics.ResetSanitizeHits(); continue; }   // rejecting is also acceptable
                try { sim.Plant.AssertFinite("EDGE-02"); }
                catch (InvalidOperationException) { Physics.ResetSanitizeHits(); continue; }
                return "Plant.Step(" + dt + ") produced state that AssertFinite accepted";
            }
            return null;
        }

        private static string EdgeBadDtRejected()
        {
            // Contract: dt must be finite and > 0 (dt == 0 is a documented no-op, see EDGE-01).
            var accepted = new List<string>();
            foreach (double dt in new[] { -6.0, double.NaN, double.PositiveInfinity })
            {
                var sim = MidRun();
                Physics.ResetSanitizeHits();
                var before = Capture(sim);
                bool threw = false;
                try { sim.Plant.Step(dt); } catch (ArgumentException) { threw = true; }
                if (threw)
                {
                    string d = FirstDiff(before, Capture(sim));
                    if (d != null) accepted.Add("dt=" + dt + " threw but mutated " + d);
                    continue;
                }
                var after = Capture(sim);
                string first = FirstDiff(before, after);
                string insertion = "Banks[3].Insertion=" + sim.Plant.Banks[3].Insertion.ToString("F3", CultureInfo.InvariantCulture);
                accepted.Add("dt=" + dt.ToString(CultureInfo.InvariantCulture) + " accepted (first changed field " + first + "; " + insertion + "; SanitizeHits=" + Physics.SanitizeHits + ")");
            }
            Physics.ResetSanitizeHits();
            return accepted.Count == 0 ? null : string.Join("; ", accepted.ToArray());
        }

        private static string EdgeNanCommandRejected()
        {
            var p = new Plant();
            try { p.SetAllRods(double.NaN); }
            catch (ArgumentException) { return null; }
            double t = p.Banks[0].InsertionTarget;
            return double.IsNaN(t)
                ? "Plant.SetAllRods(NaN) accepted: Banks[0].InsertionTarget=NaN (Physics.Clamp passes NaN through)"
                : null;
        }

        private static string EdgeNanCommandDetected()
        {
            var sim = new Simulation();
            // Rejecting at the command boundary (EDGE-04) is the strongest form of detection.
            try { sim.Plant.SetAllRods(double.NaN); }
            catch (ArgumentException) { return null; }
            Physics.ResetSanitizeHits();
            try
            {
                sim.Tick();
                sim.Plant.AssertFinite("EDGE-05");
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            return "NaN rod command propagated and AssertFinite stayed silent";
        }

        private static string EdgeSaturation()
        {
            var p = new Plant();
            p.SetAllRods(1.5);
            foreach (var b in p.Banks) if (b.InsertionTarget != 1.0) return "SetAllRods(1.5) -> Banks[*].InsertionTarget=" + b.InsertionTarget;
            p.SetAllRods(-0.5);
            foreach (var b in p.Banks) if (b.InsertionTarget != 0.0) return "SetAllRods(-0.5) -> Banks[*].InsertionTarget=" + b.InsertionTarget;
            // Thermal.SteamDemandFrac is clamped inside Step: 5.0 must behave exactly like 1.0.
            var a = new Simulation(); var c = new Simulation();
            a.Plant.Thermal.SteamDemandFrac = 5.0; c.Plant.Thermal.SteamDemandFrac = 1.0;
            for (int i = 0; i < 50; i++) { a.Tick(); c.Tick(); }
            double dT = Math.Abs(a.Plant.Thermal.SecondaryPressureBar - c.Plant.Thermal.SecondaryPressureBar)
                      + Math.Abs(a.Plant.Thermal.PrimaryTempC - c.Plant.Thermal.PrimaryTempC);
            if (dT > 1e-12) return "SteamDemandFrac 5.0 differs from 1.0 by " + dT;
            return null;
        }

        private static string EdgeFlow()
        {
            var lo = new ThermalHydraulics { SteamDemandFrac = 1.0, PrimaryFlowFrac = 0.0 };
            var hi = new ThermalHydraulics { SteamDemandFrac = 1.0, PrimaryFlowFrac = 1.0 };
            for (int i = 0; i < 400; i++)
            {
                lo.Step(6.0, 90000.0); hi.Step(6.0, 90000.0);
                if (!Physics.IsFinite(lo.PrimaryTempC) || !Physics.IsFinite(lo.SecondaryPressureBar)) return "non-finite state at zero flow, step " + i;
                if (lo.PrimaryTempC < hi.PrimaryTempC - 1e-9)
                    return "Thermal.PrimaryTempC lower with flow 0 (" + lo.PrimaryTempC.ToString("F2", CultureInfo.InvariantCulture) + ") than flow 1 (" + hi.PrimaryTempC.ToString("F2", CultureInfo.InvariantCulture) + ") at step " + i;
            }
            return null;
        }

        private static string EdgeLossOfSink()
        {
            var sim = PlantDriver.BringToPower(0.5, 0.5);
            var p = sim.Plant;
            p.Thermal.SteamDemandFrac = 0.0;
            p.Thermal.PrimaryFlowFrac = 0.0;
            double tpMax = 0, tfMax = 0, pMax = 0;
            for (int i = 0; i < 600; i++)
            {
                sim.Tick();
                tpMax = Math.Max(tpMax, p.Thermal.PrimaryTempC);
                tfMax = Math.Max(tfMax, p.Thermal.FuelTempC);
                pMax = Math.Max(pMax, p.Thermal.PrimaryPressureBar);
            }
            if (Physics.SanitizeHits > 0) return "Sanitize replaced non-finite values during loss of sink";
            string tail = " (Thermal.PrimaryTempC max " + tpMax.ToString("F1", CultureInfo.InvariantCulture) + " C, FuelTempC max " + tfMax.ToString("F1", CultureInfo.InvariantCulture) + " C, PrimaryPressureBar max " + pMax.ToString("F1", CultureInfo.InvariantCulture) + " bar)";
            if (tpMax >= Physics.PrimaryTempMaxC - 0.01) return "Thermal.PrimaryTempC pinned at clamp " + Physics.PrimaryTempMaxC + " C with no model-limit fault" + tail;
            if (tfMax >= Physics.FuelTempMaxC - 0.01) return "Thermal.FuelTempC pinned at clamp with no model-limit fault" + tail;
            if (pMax >= Physics.PressureMaxBar - 0.01) return "Thermal.PrimaryPressureBar pinned at clamp with no model-limit fault" + tail;
            return null;
        }
    }
}
