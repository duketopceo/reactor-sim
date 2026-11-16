// Phase 1 U5 scenario matrix (R13). Each case is a scripted run that records one PlantSnapshot per tick,
// so the presentation models (wall map, sound mix, lamps, alarm priority, hall steam) can be fed the same
// states a player would see. ReactorSim.Core.Tests/PresentationParityTests.cs consumes Generate(); the
// --scenario parity check below holds each case to the condition it was written to reach, so a scenario
// that silently stops reaching its state fails here instead of making the parity test vacuous.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace ReactorSim
{
    public sealed class ParityCase
    {
        public string Name;
        public List<PlantSnapshot> Frames = new List<PlantSnapshot>();
        /// <summary>Index of the first frame taken after an AcknowledgeAlarms intent was queued (-1 = none).</summary>
        public int AckFrame = -1;
    }

    public static class ParityScenarios
    {
        public const string SteadyLoad = "steady-load";
        public const string FlowLoss = "flow-loss";
        public const string HighPressure = "high-pressure";
        public const string FeedStarvation = "feed-starvation";
        public const string Scram = "scram";
        public const string TurbineTripBypass = "turbine-trip-bypass";
        public const string BypassWhileTripped = "bypass-while-tripped";
        public const string ColdStartup = "cold-startup-to-grid";

        public static readonly string[] Names =
        {
            ColdStartup, SteadyLoad, FlowLoss, HighPressure, Scram, TurbineTripBypass, FeedStarvation, BypassWhileTripped
        };

        private static string F(double v) { return v.ToString("G4", CultureInfo.InvariantCulture); }

        private static Simulation FullPower()
        {
            var sim = new Simulation { DevMode = true }; // LoadPreset is a Dev intent
            sim.EnqueueIntent(PlantIntent.LoadPreset("full-power", "u5"));
            sim.Tick();
            return sim;
        }

        private static void Record(ParityCase c, Simulation sim, double minutes)
        {
            int ticks = (int)Math.Round(minutes * sim.TicksPerGameMinute);
            for (int i = 0; i < ticks; i++)
            {
                sim.Tick();
                sim.Plant.AssertFinite(c.Name + ":t=" + sim.GameMinutes.ToString("F1", CultureInfo.InvariantCulture));
                c.Frames.Add(sim.CaptureSnapshot());
            }
        }

        public static ParityCase Generate(string name)
        {
            var c = new ParityCase { Name = name };
            if (name == ColdStartup)
            {
                var proc = StartupScenarios.LoadProcedure();
                var run = StartupScenarios.Run(proc, null, null, c.Frames.Add);
                if (run.Failed) throw new InvalidOperationException("cold startup did not reach grid: " + run.Failure);
                return c;
            }

            var sim = FullPower();
            c.Frames.Add(sim.CaptureSnapshot());
            switch (name)
            {
                case SteadyLoad:
                    Record(c, sim, 10);
                    break;
                case FlowLoss:
                    Record(c, sim, 2);
                    sim.EnqueueIntent(PlantIntent.SetPrimaryFlow(0.0, "u5"));
                    Record(c, sim, 6);
                    break;
                case HighPressure:
                    // Heaters full, spray off, secondary heat sink starved of steam demand: primary pressure climbs, the automatic PORV
                    // lifts at Physics.PorvOpenBar, and the high-pressure reactor trip latches at Physics.TripHighPrimaryPressureBar.
                    // Run until the trip (or 12 game minutes), then three minutes of aftermath.
                    Record(c, sim, 2);
                    sim.EnqueueIntent(PlantIntent.SetSpray(0.0, "u5"));
                    sim.EnqueueIntent(PlantIntent.SetHeaters(1.0, "u5"));
                    sim.EnqueueIntent(PlantIntent.SetSteamDemand(0.0, "u5"));
                    for (int i = 0; i < 12 * sim.TicksPerGameMinute && !c.Frames[c.Frames.Count - 1].Scrammed; i++)
                        Record(c, sim, 1.0 / sim.TicksPerGameMinute);
                    Record(c, sim, 3);
                    break;
                case FeedStarvation:
                    Record(c, sim, 2);
                    sim.EnqueueIntent(PlantIntent.SetFeedwater(0.0, "u5"));
                    // Until the steam generator low-level trip latches (or 30 game minutes), then a minute of aftermath.
                    for (int i = 0; i < 30 * sim.TicksPerGameMinute && c.Frames[c.Frames.Count - 1].BopTripMask == 0; i++)
                        Record(c, sim, 1.0 / sim.TicksPerGameMinute);
                    Record(c, sim, 1);
                    break;
                case Scram:
                    Record(c, sim, 2);
                    sim.EnqueueIntent(PlantIntent.TriggerScram("u5"));
                    Record(c, sim, 1);
                    sim.EnqueueIntent(PlantIntent.AcknowledgeAlarms("u5"));
                    c.AckFrame = c.Frames.Count;
                    Record(c, sim, 1);
                    break;
                case TurbineTripBypass:
                    // Load rejection with steam on: overspeed trip, stop valve shut, the dump carries the heat (as U10.2).
                    Record(c, sim, 2);
                    sim.EnqueueIntent(PlantIntent.SetGridDemand(sim.Plant.Bop.GridOutputFrac, "u5"));
                    sim.EnqueueIntent(PlantIntent.SetTurbineAutoLoad(false, "u5"));
                    Record(c, sim, 2);
                    sim.EnqueueIntent(PlantIntent.SetGeneratorBreaker(false, "u5"));
                    sim.EnqueueIntent(PlantIntent.SetSteamDump(0.6, "u5"));
                    Record(c, sim, 2);
                    break;
                case BypassWhileTripped:
                    // The same turbine trip, then a SCRAM on top while the dump is still open, then an acknowledge.
                    Record(c, sim, 2);
                    sim.EnqueueIntent(PlantIntent.SetGridDemand(sim.Plant.Bop.GridOutputFrac, "u5"));
                    sim.EnqueueIntent(PlantIntent.SetTurbineAutoLoad(false, "u5"));
                    Record(c, sim, 2);
                    sim.EnqueueIntent(PlantIntent.SetGeneratorBreaker(false, "u5"));
                    sim.EnqueueIntent(PlantIntent.SetSteamDump(0.6, "u5"));
                    Record(c, sim, 1);
                    sim.EnqueueIntent(PlantIntent.TriggerScram("u5"));
                    Record(c, sim, 1);
                    sim.EnqueueIntent(PlantIntent.AcknowledgeAlarms("u5"));
                    c.AckFrame = c.Frames.Count;
                    Record(c, sim, 1);
                    break;
                default:
                    throw new ArgumentException("unknown parity scenario " + name);
            }
            return c;
        }

        // --scenario parity: every case reaches the state it is named for, and prints a one-line summary.
        public static void Verify()
        {
            foreach (string n in Names)
            {
                var c = Generate(n);
                var last = c.Frames[c.Frames.Count - 1];
                bool relief = false, secRelief = false, scram = false, bopTrip = false, turbTrip = false;
                double maxDump = 0, maxP = 0, maxTurb = 0, minFlow = 1, minFeed = 1, minSgLevel = 1;
                foreach (var f in c.Frames)
                {
                    relief |= f.ReliefOpen; secRelief |= f.SecondaryReliefOpen; scram |= f.Scrammed;
                    bopTrip |= f.BopTripMask != 0; turbTrip |= f.TurbineTripped;
                    maxDump = Math.Max(maxDump, f.DumpSteamKW); maxP = Math.Max(maxP, f.PrimaryPressureBar);
                    maxTurb = Math.Max(maxTurb, f.TurbineSteamKW);
                    minFlow = Math.Min(minFlow, f.PrimaryFlowFrac); minFeed = Math.Min(minFeed, f.FeedwaterFlowFrac);
                    minSgLevel = Math.Min(minSgLevel, f.SecondaryLevelFrac);
                }
                Console.WriteLine("  [parity] " + n.PadRight(22) + c.Frames.Count.ToString().PadLeft(5) + " frames  scram=" + (scram ? 1 : 0)
                    + " turbTrip=" + (turbTrip ? 1 : 0) + " bopTrip=" + (bopTrip ? 1 : 0) + " porv=" + (relief ? 1 : 0) + " sgRelief=" + (secRelief ? 1 : 0)
                    + " maxPpri=" + F(maxP) + " maxDumpKW=" + F(maxDump) + " maxTurbKW=" + F(maxTurb) + " minFlow=" + F(minFlow)
                    + " minFeed=" + F(minFeed) + " minSgLvl=" + F(minSgLevel));
                switch (n)
                {
                    case ColdStartup:
                        Need(last.GeneratorSynced && !scram && !bopTrip, n, "must reach grid with no SCRAM and no BOP trip");
                        break;
                    case SteadyLoad:
                        Need(!scram && !bopTrip && last.GeneratorSynced && maxDump <= 0.0, n, "steady load must hold the grid with no trip and no bypass");
                        break;
                    case FlowLoss:
                        Need(minFlow < 0.25, n, "primary flow must fall under the low-flow lamp setpoint");
                        break;
                    case HighPressure:
                        Need(maxP > LampPressureHi && relief && scram && maxP < Physics.SafetyValveOpenBar, n, "primary pressure must pass the high-pressure lamp setpoint, the PORV must lift and the high-pressure trip must latch below the safety valve setpoint");
                        break;
                    case FeedStarvation:
                        Need(minFeed < 0.05, n, "feedwater flow must fall under the feed-fail setpoint");
                        break;
                    case Scram:
                        Need(scram && c.AckFrame > 0, n, "must SCRAM and acknowledge");
                        break;
                    case TurbineTripBypass:
                        Need(last.TurbineTripped && last.TurbineSteamKW == 0.0 && last.DumpSteamKW > 0.0, n, "must end tripped with dump steam and no turbine steam");
                        break;
                    case BypassWhileTripped:
                        Need(last.TurbineTripped && last.Scrammed && last.DumpSteamKW > 0.0 && last.TurbineSteamKW == 0.0, n, "must end tripped and scrammed with dump steam still flowing");
                        break;
                }
            }
        }

        private const double LampPressureHi = 165.0; // LampPredicates.PriPressHiBar (kept literal: Runner does not link the Unity models)

        private static void Need(bool ok, string name, string what)
        {
            if (!ok) throw new InvalidOperationException("parity scenario " + name + ": " + what);
        }
    }
}
