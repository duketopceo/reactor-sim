using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ReactorSim.Tests
{
    // U16: procedure clipboards. Pure snapshot-derived state; data in procedures.json.
    // Repo files come from TestRepo.Find (REACTORSIM_REPO_ROOT, else walk up from the test dll or this source tree).
    public class ProcedureTests
    {
        private static List<Procedure> Shipped()
        {
            return ProcedureLoader.LoadFromFile(TestRepo.Find("ReactorSim.Unity", "Assets", "StreamingAssets", "procedures.json"));
        }

        private static Procedure Procedure(string id)
        {
            return Shipped().Single(p => p.Id == id);
        }

        private static HashSet<string> InventoryIds()
        {
            var ids = new HashSet<string>();
            foreach (var line in File.ReadLines(TestRepo.Find("design", "control-room", "inventory.csv")).Skip(1))
            {
                if (line.Length == 0) continue;
                ids.Add(line.Substring(0, line.IndexOf(',')));
            }
            return ids;
        }

        [Fact]
        public void ShippedProcedures_Parse_WithOneColdStartToGridProcedure()
        {
            var ids = Shipped().Select(p => p.Id).ToArray();
            Assert.Equal(new[] { "cold-start-to-grid" }, ids);
            Assert.All(Shipped(), p => Assert.NotEmpty(p.Steps));
        }

        [Fact]
        public void ColdStartToGrid_EveryStepNamesControlAndTarget_AndPhasesRunAToD()
        {
            var proc = Procedure("cold-start-to-grid");
            Assert.InRange(proc.Steps.Count, 12, 14);
            Assert.All(proc.Steps, s =>
            {
                Assert.False(string.IsNullOrWhiteSpace(s.Control), s.Id + " has no control");
                Assert.False(string.IsNullOrWhiteSpace(s.Target), s.Id + " has no target");
                Assert.False(string.IsNullOrWhiteSpace(s.Phase), s.Id + " has no phase");
            });
            var letters = proc.Steps.Select(s => s.Phase[0]).ToArray();
            Assert.Equal(letters.OrderBy(c => c).ToArray(), letters);          // non-decreasing
            Assert.Equal(new[] { 'A', 'B', 'C', 'D' }, letters.Distinct().ToArray());
        }

        [Fact]
        public void ColdStartToGrid_BoronAndBankATargetsAreTheCorrectedOnes()
        {
            var proc = Procedure("cold-start-to-grid");
            Assert.Equal(1200.0, proc.Steps.Single(s => s.Predicate == "boron_at_most").Value);
            Assert.Equal(0.5, proc.Steps.Single(s => s.Predicate == "bank_a_insertion_at_most").Value);
            // Vacuum and heaters (lineup) come before any rod movement.
            int rods = proc.Steps.FindIndex(s => s.Predicate == "shutdown_banks_out");
            Assert.True(proc.Steps.FindIndex(s => s.Predicate == "condenser_pressure_at_most") < rods);
            Assert.True(proc.Steps.FindIndex(s => s.Predicate == "primary_pressure_at_least") < rods);
        }

        [Fact]
        public void SandboxColdStart_StepOneIsCurrent_AndNothingIsTicked()
        {
            var sim = new Simulation();
            SandboxStart.Apply(sim);
            var st = Procedure("cold-start-to-grid").Evaluate(sim.CaptureSnapshot());
            Assert.Equal(0, st.CurrentIndex);
            Assert.DoesNotContain(true, st.Ticked);
        }

        [Fact]
        public void TurbineAutoLoadPredicate_TracksTheFlag()
        {
            Assert.True(ProcedurePredicates.Exists("turbine_auto_load_on"));
            Assert.True(ProcedurePredicates.Evaluate("turbine_auto_load_on", 0, new PlantSnapshot { BopAutoLoad = true }));
            Assert.False(ProcedurePredicates.Evaluate("turbine_auto_load_on", 0, new PlantSnapshot { BopAutoLoad = false }));
        }

        [Fact]
        public void Loader_AcceptsOptionalPhaseControlTarget_AndStepsWithoutThem()
        {
            string with = "{\"procedures\":[{\"id\":\"p\",\"steps\":[{\"id\":\"a\",\"text\":\"t\",\"phase\":\"A. X\",\"control\":\"Knob\",\"target\":\"1 bar\",\"rows\":[],\"when\":\"power_at_least\",\"value\":1}]}]}";
            var s = ProcedureLoader.Parse(with)[0].Steps[0];
            Assert.Equal("A. X", s.Phase); Assert.Equal("Knob", s.Control); Assert.Equal("1 bar", s.Target);
            string without = with.Replace("\"phase\":\"A. X\",", "").Replace("\"control\":\"Knob\",", "").Replace("\"target\":\"1 bar\",", "");
            var t = ProcedureLoader.Parse(without)[0].Steps[0];
            Assert.Equal("", t.Phase); Assert.Equal("", t.Control); Assert.Equal("", t.Target);
            Assert.Throws<ProcedureException>(() => ProcedureLoader.Parse(with.Replace("\"target\":\"1 bar\"", "\"target\":5")));
        }

        [Fact]
        public void Loader_RejectsDuplicateStepId()
        {
            string st = "{\"id\":\"a\",\"text\":\"t\",\"rows\":[],\"when\":\"power_at_least\",\"value\":1}";
            Assert.Throws<ProcedureException>(() => ProcedureLoader.Parse("{\"procedures\":[{\"id\":\"p\",\"steps\":[" + st + "," + st + "]}]}"));
        }

        [Fact]
        public void AllBanksInserted_UntickRodStepAndEverythingAfter()
        {
            var proc = Procedure("cold-start-to-grid");
            int rods = proc.Steps.FindIndex(s => s.Predicate == "shutdown_banks_out");
            var snap = new PlantSnapshot
            {
                PrimaryFlowFrac = 1.0, HeaterPowerFrac = 1.0, PrimaryPressureBar = 155, CondenserPressureBar = 0.08,
                BankInsertion = new[] { 0.0, 0.0, 0.0, 0.3 }, BoronPpm = 900, IsCritical = true, Power = 0.5,
                PrimaryTempC = 290, SteamDemandFrac = 0.5, BopAutoLoad = true, TurbineRpm = 3000, GeneratorSynced = true, GridOutputFrac = 0.5,
            };
            Assert.True(proc.Evaluate(snap).Complete);
            snap.BankInsertion = new[] { 1.0, 1.0, 1.0, 1.0 };
            var st = proc.Evaluate(snap);
            Assert.Equal(rods, st.CurrentIndex);
            for (int i = rods; i < st.Ticked.Length; i++) Assert.False(st.Ticked[i]);
        }

        [Fact]
        public void EveryRowIdInEveryProcedure_ExistsInInventory()
        {
            var inventory = InventoryIds();
            Assert.True(inventory.Count > 100);
            foreach (var p in Shipped())
                foreach (var s in p.Steps)
                {
                    Assert.NotEmpty(s.Rows);
                    foreach (var row in s.Rows)
                        Assert.True(inventory.Contains(row), p.Id + "/" + s.Id + " names unknown inventory row \"" + row + "\"");
                }
        }

        [Fact]
        public void EveryPredicateInProceduresJson_Exists()
        {
            foreach (var p in Shipped())
                foreach (var s in p.Steps)
                    Assert.True(ProcedurePredicates.Exists(s.Predicate), p.Id + "/" + s.Id + " uses unknown predicate " + s.Predicate);
        }

        [Fact]
        public void Loader_RejectsUnknownPredicateKeyAndDuplicates()
        {
            string step = "{\"id\":\"a\",\"text\":\"t\",\"rows\":[],\"when\":\"{0}\",\"value\":1}";
            string Doc(string when) { return "{\"procedures\":[{\"id\":\"p\",\"steps\":[" + step.Replace("{0}", when) + "]}]}"; }
            Assert.Single(ProcedureLoader.Parse(Doc("power_at_least")));
            Assert.Throws<ProcedureException>(() => ProcedureLoader.Parse(Doc("no_such_predicate")));
            Assert.Throws<ProcedureException>(() => ProcedureLoader.Parse(Doc("power_at_least").Replace("\"value\":1", "\"vlaue\":1")));
            Assert.Throws<ProcedureException>(() => ProcedureLoader.Parse("{\"procedures\":[{\"id\":\"p\",\"steps\":[]}]}"));
        }

        [Fact]
        public void StartCoolantPumpsStep_TicksAtFlowThreshold_AndUnticksWhenFlowDrops()
        {
            var proc = Procedure("cold-start-to-grid");
            var step = proc.Steps[0];
            Assert.Equal("start-rcps", step.Id);
            Assert.Equal("primary_flow_at_least", step.Predicate);

            var snap = new PlantSnapshot { PrimaryFlowFrac = 0.0 };
            Assert.False(proc.Evaluate(snap).Ticked[0]);
            Assert.Equal(0, proc.Evaluate(snap).CurrentIndex);

            snap.PrimaryFlowFrac = step.Value - 0.01;
            Assert.False(proc.Evaluate(snap).Ticked[0]);

            snap.PrimaryFlowFrac = step.Value;
            var up = proc.Evaluate(snap);
            Assert.True(up.Ticked[0]);
            Assert.Equal(1, up.CurrentIndex);   // next step is current; the next step has not completed

            snap.PrimaryFlowFrac = 0.2;          // flow drops before the next step completes
            var down = proc.Evaluate(snap);
            Assert.False(down.Ticked[0]);
            Assert.Equal(0, down.CurrentIndex);
        }

        [Fact]
        public void LaterStep_NeverTicksWhileAnEarlierStepIsOpen()
        {
            var proc = Procedure("cold-start-to-grid");
            // Pressure is up but the pumps are not running: step 2 must not tick or become current.
            var snap = new PlantSnapshot { PrimaryFlowFrac = 0.0, PrimaryPressureBar = 150 };
            var st = proc.Evaluate(snap);
            Assert.False(st.Ticked[0]);
            Assert.False(st.Ticked[1]);
            Assert.Equal(0, st.CurrentIndex);
        }

        [Fact]
        public void HighlightedRows_AreExactlyTheCurrentStepsRows()
        {
            foreach (var proc in Shipped())
            {
                var snap = new PlantSnapshot();   // cold plant: step 0 is current
                var st = proc.Evaluate(snap);
                if (st.Complete) continue;
                Assert.Equal(proc.Steps[st.CurrentIndex].Rows, st.HighlightedRows);
            }

            var cold = Procedure("cold-start-to-grid");
            var s = new PlantSnapshot { PrimaryFlowFrac = 1.0 };
            var state = cold.Evaluate(s);
            Assert.Equal(1, state.CurrentIndex);
            Assert.Equal(new[] { "pzr_heater_ctl", "pzr_heater_pwr", "primary_pressure" }, state.HighlightedRows);
            Assert.Equal(cold.Steps[1].Rows, state.HighlightedRows);
        }

        [Fact]
        public void CompleteProcedure_HighlightsNothing()
        {
            var cold = Procedure("cold-start-to-grid");
            var s = new PlantSnapshot
            {
                PrimaryFlowFrac = 1.0, HeaterPowerFrac = 1.0, PrimaryPressureBar = 155, CondenserPressureBar = 0.08,
                BankInsertion = new[] { 0.0, 0.0, 0.0, 0.3 }, BoronPpm = 900, IsCritical = true, Power = 0.5,
                PrimaryTempC = 290, SteamDemandFrac = 0.5, BopAutoLoad = true, TurbineRpm = 3000, GeneratorSynced = true, GridOutputFrac = 0.5,
            };
            var st = cold.Evaluate(s);
            Assert.True(st.Complete);
            Assert.Equal(-1, st.CurrentIndex);
            Assert.Empty(st.HighlightedRows);
        }

        [Fact]
        public void ColdToSyncIntentSequence_TicksColdStartToGridToCompletion()
        {
            // Same operator sequence as ReactorSim.Runner PlantDriver.BringToPower(0.5, 0.5), sent as intents.
            var sim = new Simulation();
            var procs = Shipped();
            var everComplete = new HashSet<string>();
            var cold = procs.Single(p => p.Id == "cold-start-to-grid");
            Assert.False(cold.Evaluate(sim.CaptureSnapshot()).Complete);   // a cold plant starts un-ticked

            Action<PlantIntent> send = i => sim.EnqueueIntent(i);
            Action<double> run = minutes => sim.RunGameMinutes(minutes, s =>
            {
                var snap = s.CaptureSnapshot();
                foreach (var p in procs) if (p.Evaluate(snap).Complete) everComplete.Add(p.Id);
            });
            Action<double, double, int> hold = (target, steam, minutes) =>
            {
                for (int m = 0; m < minutes; m++)
                {
                    var s = sim.CaptureSnapshot();
                    send(PlantIntent.SetSteamDemand(steam));
                    double err = target - s.Power;
                    double a = s.BankTarget[3];
                    if (err > 0.08) a -= 0.008; else if (err < -0.08) a += 0.008;
                    if (err > 0.2 && a <= 0.02 && s.BoronPpm > 20) send(PlantIntent.SetDilute(0.25));
                    else if (err < -0.25 && s.BoronPpm < 1200) { send(PlantIntent.SetDilute(0.0)); send(PlantIntent.SetBorate(0.25)); }
                    else { send(PlantIntent.SetDilute(0.0)); send(PlantIntent.SetBorate(0.0)); }
                    send(PlantIntent.SetRodBank(3, Math.Max(0.0, Math.Min(1.0, a))));
                    run(1);
                }
            };

            send(PlantIntent.SetDilute(1.5)); send(PlantIntent.SetPrimaryFlow(1.0));
            send(PlantIntent.SetSteamDemand(0.0)); send(PlantIntent.SetHeaters(1.0));
            run(3);
            for (int i = 0; i < 3; i++) send(PlantIntent.SetRodBank(i, 0.0));
            run(8);
            send(PlantIntent.SetRodBank(3, 0.35)); run(6);
            send(PlantIntent.SetDilute(0.0)); send(PlantIntent.SetRodBank(3, 0.12)); run(5);
            hold(0.2, 0.05, 20);
            send(PlantIntent.SetHeaters(0.2));
            hold(0.5, 0.5, 25);

            var end = sim.CaptureSnapshot();
            Assert.True(end.GeneratorSynced, "driver never synced the generator");
            Assert.True(cold.Evaluate(end).Complete, "cold-start-to-grid not complete at end of run");
            Assert.Contains("cold-start-to-grid", everComplete);
        }
    }
}
