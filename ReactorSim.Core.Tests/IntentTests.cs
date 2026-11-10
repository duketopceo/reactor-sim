using System;
using System.Linq;
using Xunit;

namespace ReactorSim.Tests
{
    public class IntentTests
    {
        public static TheoryData<IntentKind> AllKinds()
        {
            var d = new TheoryData<IntentKind>();
            foreach (IntentKind k in Enum.GetValues(typeof(IntentKind))) d.Add(k);
            return d;
        }

        [Theory]
        [MemberData(nameof(AllKinds))]
        public void IntentJson_RoundTripsEveryKind(IntentKind kind)
        {
            var original = new PlantIntent(kind, "T\"x\\y", 0.123456789012345, "tester", 12.5);
            var back = PlantIntent.FromJson(original.ToJson());
            Assert.NotNull(back);
            Assert.Equal(kind, back.Kind);
            Assert.Equal(original.Target, back.Target);
            Assert.Equal(original.Value, back.Value);
            Assert.Equal("tester", back.Issuer);
            Assert.Equal(12.5, back.AtMinute);
        }

        [Fact]
        public void IntentJson_UsesKindNamesOnTheWire()
        {
            Assert.Contains("\"kind\":\"GeneratorBreaker\"", PlantIntent.SetGeneratorBreaker(true).ToJson());
        }

        [Theory]
        [InlineData("{\"kind\":\"999\",\"value\":1}")]
        [InlineData("{\"kind\":\"-1\",\"value\":1}")]
        [InlineData("{\"kind\":\"Scram,ResetScram\",\"value\":1}")]
        [InlineData("{\"kind\":\"NotAKind\",\"value\":1}")]
        [InlineData("{\"value\":1}")]
        [InlineData("   ")]
        public void IntentJson_RejectsUnknownOrNumericKinds(string json)
        {
            Assert.Null(PlantIntent.FromJson(json));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void EnqueueIntent_RejectsNonFiniteValues(double v)
        {
            var sim = new Simulation();
            Assert.Throws<ArgumentException>(() => sim.EnqueueIntent(PlantIntent.SetRodBank("A", v)));
            Assert.Throws<ArgumentException>(() => sim.EnqueueIntent(PlantIntent.SetGridDemand(v)));
        }

        [Fact]
        public void SetAllRods_RejectsNaN_InsteadOfWithdrawing()
        {
            var p = new Plant();
            Assert.Throws<ArgumentOutOfRangeException>(() => p.SetAllRods(double.NaN));
            Assert.All(p.Banks, b => Assert.Equal(1.0, b.InsertionTarget));
        }

        [Fact]
        public void Apply_ClampsOperatorFractions()
        {
            var p = new Plant();
            p.Apply(PlantIntent.SetSteamDemand(5));
            p.Apply(PlantIntent.SetFeedwater(-3));
            p.Apply(PlantIntent.SetSteamDump(2));
            p.Apply(PlantIntent.SetGridDemand(9));
            p.Apply(PlantIntent.SetVacuumPump(-1));
            p.Apply(PlantIntent.SetCoolingWater(4));
            Assert.Equal(1.2, p.Thermal.SteamDemandFrac);
            Assert.Equal(0.0, p.Thermal.FeedwaterFlowFrac);
            Assert.Equal(1.0, p.Thermal.SteamDumpFrac);
            Assert.Equal(1.5, p.Bop.GridDemandFrac);
            Assert.Equal(0.0, p.Bop.VacuumPumpFrac);
            Assert.Equal(1.0, p.Bop.CoolingWaterFrac);
        }

        [Fact]
        public void Apply_UnknownBank_ReturnsFalse()
        {
            Assert.False(new Plant().Apply(PlantIntent.SetRodBank("Z", 0.5)));
        }

        [Fact]
        public void Apply_RodCommandsIgnoredWhileScrammed()
        {
            var p = new Plant();
            p.Scram();
            p.Apply(PlantIntent.SetRodBank("A", 0.0));
            Assert.Equal(1.0, p.Banks[3].InsertionTarget);
        }

        [Fact]
        public void Apply_TurbineAutoLoadToggles()
        {
            var p = new Plant();
            Assert.True(p.Bop.AutoLoad);
            p.Apply(PlantIntent.SetTurbineAutoLoad(false));
            Assert.False(p.Bop.AutoLoad);
        }

        // ---- U1: intent classes, dev gate, intent log ----

        [Theory]
        [MemberData(nameof(AllKinds))]
        public void EveryIntentKind_HasAClass(IntentKind kind)
        {
            // ClassOf throws for an unmapped kind, so adding an enum member without a class fails here.
            var cls = PlantIntent.ClassOf(kind);
            Assert.Equal(kind == IntentKind.LoadPreset || kind == IntentKind.SetTimeScaleDev ? IntentClass.Dev : IntentClass.Player, cls);
        }

        [Fact]
        public void LoadPreset_WithDevModeOff_ThrowsAndQueuesNothing()
        {
            var sim = new Simulation();
            Assert.False(sim.DevMode);
            Assert.Throws<IntentRejectedException>(() => sim.EnqueueIntent(PlantIntent.LoadPreset("full-power")));
            Assert.Empty(sim.DrainPendingIntents());
        }

        [Fact]
        public void LoadPreset_WithDevModeOn_Applies()
        {
            var sim = new Simulation { DevMode = true };
            sim.EnqueueIntent(PlantIntent.LoadPreset("full-power"));
            sim.Tick();
            Assert.True(sim.Plant.Core.Power > 0.9);
        }

        [Fact]
        public void IntentLog_RecordsAppliedIntentsInOrder_AndSessionTickSurvivesPresetLoad()
        {
            var sim = new Simulation { DevMode = true };
            sim.Tick();
            sim.Tick();
            sim.EnqueueIntent(PlantIntent.SetSteamDemand(0.3, "a"));
            sim.EnqueueIntent(PlantIntent.SetGridDemand(0.2, "b"));
            Assert.Empty(sim.GetIntentLogSnapshot()); // logged when applied, not when queued
            sim.Tick();                                // session tick 2
            sim.EnqueueIntent(PlantIntent.LoadPreset("hot-standby", "dev"));
            sim.Tick();                                // session tick 3; TickCount resets to 1
            sim.Tick();
            sim.EnqueueIntent(PlantIntent.SetSteamDemand(0.1, "c"));
            sim.Tick();                                // session tick 5

            var log = sim.GetIntentLogSnapshot();
            Assert.Equal(4, log.Count);
            Assert.Equal(new[] { "a", "b", "dev", "c" }, log.Select(e => e.Intent.Issuer).ToArray());
            Assert.Equal(new long[] { 2, 2, 3, 5 }, log.Select(e => e.SessionTick).ToArray());
            Assert.Equal(new long[] { 0, 1, 2, 3 }, log.Select(e => e.Seq).ToArray());
            Assert.Equal(IntentClass.Dev, log[2].Class);
            Assert.All(log, e => Assert.False(e.Rejected));
            Assert.Equal(6, sim.SessionTick);
            Assert.True(sim.TickCount < sim.SessionTick); // preset load reset TickCount only
        }

        [Fact]
        public void IntentThrowingInTick_IsLoggedRejected_AndTickLoopContinues()
        {
            var sim = new Simulation();
            var bad = PlantIntent.SetAllRods(0.0, "bad");
            sim.EnqueueIntent(bad);
            bad.Value = double.NaN; // mutated after the boundary check; Plant.SetAllRods throws
            sim.EnqueueIntent(PlantIntent.SetSteamDemand(0.4, "good"));
            sim.Tick();
            sim.Tick();

            var log = sim.GetIntentLogSnapshot();
            Assert.Equal(2, log.Count);
            Assert.True(log[0].Rejected);
            Assert.False(string.IsNullOrEmpty(log[0].Error));
            Assert.False(log[1].Rejected);
            Assert.Equal(0.4, sim.Plant.Thermal.SteamDemandFrac);
            Assert.Equal(2, sim.TickCount);
        }

        [Fact]
        public void WhileScrammed_RodWithdrawalIntents_LeaveTargetsInserted()
        {
            var sim = new Simulation();
            sim.EnqueueIntent(PlantIntent.TriggerScram("t"));
            sim.Tick();
            sim.EnqueueIntent(PlantIntent.SetAllRods(0.0, "http"));
            sim.EnqueueIntent(PlantIntent.SetRodBank("A", 0.0, "coop"));
            sim.Tick();
            Assert.All(sim.Plant.Banks, b => Assert.Equal(1.0, b.InsertionTarget));
        }

        // ---- U17: AcknowledgeAlarms ----

        [Fact]
        public void AcknowledgeAlarms_IsAppendedLast_AndSerializesByName()
        {
            var kinds = (IntentKind[])Enum.GetValues(typeof(IntentKind));
            Assert.Equal(IntentKind.AcknowledgeAlarms, kinds[kinds.Length - 1]);
            Assert.Contains("\"kind\":\"AcknowledgeAlarms\"", PlantIntent.AcknowledgeAlarms("x").ToJson());
            var back = PlantIntent.FromJson(PlantIntent.AcknowledgeAlarms("x").ToJson());
            Assert.Equal(IntentKind.AcknowledgeAlarms, back.Kind);
            Assert.Equal(IntentClass.Player, back.Class);
        }

        [Fact]
        public void AcknowledgeAlarms_CountsInThePlant_AndSurvivesTheSnapshotRoundTrip()
        {
            var sim = new Simulation();
            Assert.Equal(0, sim.Plant.AlarmAckSeq);
            sim.EnqueueIntent(PlantIntent.AcknowledgeAlarms());
            sim.EnqueueIntent(PlantIntent.AcknowledgeAlarms());
            sim.Tick();
            Assert.Equal(2, sim.Plant.AlarmAckSeq);
            var snap = sim.Plant.CaptureState(1.0);
            Assert.Equal(2, snap.AlarmAckSeq);
            var back = PlantSnapshot.FromJson(snap.ToJson());
            Assert.Equal(2, back.AlarmAckSeq);
            var other = new Plant();
            other.RestoreState(back);
            Assert.Equal(2, other.AlarmAckSeq);
            Assert.Equal(0, PlantSnapshot.FromJson("{\"simSeconds\":1}").AlarmAckSeq);   // snapshots written before the field existed
        }

        [Fact]
        public void AcknowledgeAlarms_AppliesWhileTheClockIsPaused()
        {
            var sim = new Simulation();
            sim.EnqueueIntent(PlantIntent.PauseClock("t"));
            sim.Advance(1.0);
            Assert.True(sim.Clock.Halted);
            sim.EnqueueIntent(PlantIntent.AcknowledgeAlarms("t"));
            sim.Advance(0.1);
            Assert.Equal(1, sim.Plant.AlarmAckSeq);
        }
    }
}
