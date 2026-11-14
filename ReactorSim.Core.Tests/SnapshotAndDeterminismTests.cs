using System;
using System.Collections.Generic;
using Xunit;

namespace ReactorSim.Tests
{
    public class SnapshotAndDeterminismTests
    {
        private static Simulation Startup()
        {
            var sim = new Simulation();
            sim.EnqueueIntent(PlantIntent.SetDilute(2.5));
            sim.EnqueueIntent(PlantIntent.SetPrimaryFlow(1.0));
            sim.EnqueueIntent(PlantIntent.SetRodBank("D", 0.0));
            sim.EnqueueIntent(PlantIntent.SetRodBank("C", 0.0));
            sim.EnqueueIntent(PlantIntent.SetRodBank("B", 0.0));
            sim.EnqueueIntent(PlantIntent.SetRodBank("A", 0.4));
            sim.EnqueueIntent(PlantIntent.SetSteamDemand(0.2));
            return sim;
        }

        private static void Ticks(Simulation sim, int n) { for (int i = 0; i < n; i++) sim.Tick(); }

        [Fact]
        public void IdenticalIntentSequences_AreBitIdentical()
        {
            var a = Startup(); var b = Startup();
            Ticks(a, 300); Ticks(b, 300);
            Assert.Equal(a.Plant.CaptureState(a.SimSeconds).ToJson(), b.Plant.CaptureState(b.SimSeconds).ToJson());
        }

        [Fact]
        public void MeasuredFlowsAreCapturedAndRestoredWithoutConfusingCommandsForOutput()
        {
            var plant = new Plant();
            plant.LoadPreset("full-power");
            plant.Apply(PlantIntent.SetSteamDump(0.3));
            for (int i = 0; i < 5; i++) plant.Step(1.0);
            var captured = plant.CaptureState();
            Assert.Equal(plant.Thermal.FeedwaterDeliveredFrac, captured.FeedwaterDeliveredFrac);
            Assert.Equal(plant.Thermal.DumpSteamKW, captured.DumpSteamKW);
            var restored = new Plant();
            restored.RestoreState(PlantSnapshot.FromJson(captured.ToJson()));
            var roundTrip = restored.CaptureState();
            Assert.Equal(captured.FeedwaterDeliveredFrac, roundTrip.FeedwaterDeliveredFrac);
            Assert.Equal(captured.DumpSteamKW, roundTrip.DumpSteamKW);
            Assert.Equal(captured.TurbineSteamKW, roundTrip.TurbineSteamKW);
        }

        [Fact]
        public void SnapshotJson_RoundTripsFreshPlant()
        {
            var p = new Plant();
            string json = p.CaptureState().ToJson();
            var restored = new Plant();
            restored.RestoreState(PlantSnapshot.FromJson(json));
            // Restore recomputes the ledger, so a 0 can come back as -0 (e.g. xenonPcm = -k * 0).
            // Numerically equal; normalize signed zero before comparing text.
            Assert.Equal(NoNegZero(json), NoNegZero(restored.CaptureState().ToJson()));
        }

        private static string NoNegZero(string json)
        {
            return System.Text.RegularExpressions.Regex.Replace(json, @"(?<=[:\[,])-0(?=[,\]}])", "0");
        }

        [Fact]
        public void SnapshotJson_RoundTripsBopFields()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            p.Apply(PlantIntent.SetTurbineAutoLoad(false));
            p.Apply(PlantIntent.SetGridDemand(0.42));
            p.Apply(PlantIntent.SetSteamDump(0.3));
            p.Apply(PlantIntent.SetVacuumPump(0.7));
            p.Apply(PlantIntent.SetCoolingWater(0.8));
            for (int i = 0; i < 5; i++) p.Step(6.0);
            var s = PlantSnapshot.FromJson(p.CaptureState().ToJson());
            Assert.False(s.BopAutoLoad);
            Assert.Equal(p.Bop.GridDemandFrac, s.GridDemandFrac);
            Assert.Equal(0.3, s.SteamDumpFrac);
            Assert.Equal(0.7, s.VacuumPumpFrac);
            Assert.Equal(0.8, s.CoolingWaterFrac);
            Assert.Equal(p.Bop.Turbine.Rpm, s.TurbineRpm);
            Assert.Equal(p.Bop.CondenserPressureBar, s.CondenserPressureBar);
            Assert.Equal(p.Bop.HotwellInventoryFrac, s.HotwellInventoryFrac);
            Assert.Equal(p.Bop.TripMask, s.BopTripMask);
        }

        [Fact]
        public void PreBopSnapshot_RestoresWithBopDefaults()
        {
            // A snapshot written before the balance of plant existed has none of its keys.
            var p = new Plant();
            p.RestoreState(PlantSnapshot.FromJson("{\"power\":0.5,\"primaryTempC\":290}"));
            Assert.True(p.Bop.AutoLoad);
            Assert.Equal(1.0, p.Bop.VacuumPumpFrac);
            Assert.Equal(1.0, p.Bop.CoolingWaterFrac);
            Assert.Equal(0, p.Bop.TripMask);
            Assert.True(p.Thermal.TurbineStopValveOpen);
        }

        [Fact]
        public void Restore_ResumesBitIdentically()
        {
            var a = Startup();
            Ticks(a, 200);
            string mid = a.Plant.CaptureState(a.SimSeconds).ToJson();
            Ticks(a, 200);

            var b = new Simulation();
            b.Plant.RestoreState(PlantSnapshot.FromJson(mid));
            Ticks(b, 200);

            // Compare plant state only (SimSeconds restarts on the second simulation).
            var sa = a.Plant.CaptureState(0); var sb = b.Plant.CaptureState(0);
            Assert.Equal(sa.ToJson(), sb.ToJson());
        }

        [Fact]
        public void SnapshotFromJson_EmptyIsNull()
        {
            Assert.Null(PlantSnapshot.FromJson(""));
            Assert.Null(PlantSnapshot.FromJson("  "));
        }
    }
}
