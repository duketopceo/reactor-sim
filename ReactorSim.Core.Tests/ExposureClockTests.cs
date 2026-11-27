using Xunit;
using static ReactorSim.Tests.RadiationTestPlants;

namespace ReactorSim.Tests
{
    // Phase 4 U2: ledger integration with Simulation.Tick, pause, warp, preset load and restore (R8, R9, R12, KTD8).
    public class ExposureClockTests
    {
        private static ExposureSubject Get(Simulation sim, string id = "op1")
        {
            ExposureSubject s;
            Assert.True(sim.Exposure.TryGet(id, out s));
            return s;
        }

        [Fact]
        public void SameTicks_At1xAnd60x_GiveIdenticalDose()
        {
            const int n = 100;
            var slow = FullPowerSim(1.0); PutOnDeck(slow);
            var fast = FullPowerSim(60.0); PutOnDeck(fast);
            for (int i = 0; i < n; i++) slow.Advance(slow.TickDt);
            Assert.Equal(n, slow.TickCount);
            int ticks = fast.Advance(n * fast.TickDt / 60.0 + 1e-6);
            Assert.Equal(n, ticks);
            Assert.Equal(Get(slow).DoseMSv, Get(fast).DoseMSv);
            Assert.True(Get(slow).DoseMSv > 0.0);
            Assert.Equal(Get(slow).UnavailableSeconds, Get(fast).UnavailableSeconds);
        }

        [Fact]
        public void EachTick_AddsRateTimesTickDtInHours()
        {
            var sim = FullPowerSim(1.0); PutOnDeck(sim);
            sim.Tick();
            RadPoint pt;
            RadiationAreas.TryGetReferencePoint(RadiationAreas.ReactorDeckId, out pt);
            double rate = DoseField.Evaluate(sim.Plant, RadiationAreas.ReactorDeckId, pt.X, pt.Y, pt.H).RateMSvPerH;
            Assert.Equal(rate * sim.TickDt / 3600.0, Get(sim).DoseMSv, 12);
        }

        [Fact]
        public void PausedAdvance_AddsNoDose_AndNoUnavailableTime()
        {
            var sim = FullPowerSim(6.0); PutOnDeck(sim); sim.Exposure.SetLocation("lost", "nowhere", 0, 0, 0);
            sim.Advance(2.0);
            sim.EnqueueIntent(PlantIntent.PauseClock("t"));
            sim.Advance(1.0);
            Assert.True(sim.Clock.Paused);
            double dose = Get(sim).DoseMSv, lost = Get(sim, "lost").UnavailableSeconds;
            Assert.True(dose > 0.0);
            sim.Advance(1000.0);
            Assert.Equal(dose, Get(sim).DoseMSv);
            Assert.Equal(lost, Get(sim, "lost").UnavailableSeconds);
        }

        [Fact]
        public void PerTickDose_FallsAfterScram_InTheSimulation()
        {
            var sim = FullPowerSim(1.0); PutOnDeck(sim);
            sim.Tick();
            double first = Get(sim).DoseMSv;
            sim.EnqueueIntent(PlantIntent.TriggerScram("t"));
            for (int i = 0; i < 6; i++) sim.Tick();
            double before = Get(sim).DoseMSv;
            sim.Tick();
            Assert.True(Get(sim).DoseMSv - before < first);
        }

        [Fact]
        public void LoadScenarioPreset_EmptiesTheLedger()
        {
            var sim = FullPowerSim(1.0); PutOnDeck(sim);
            sim.Tick();
            sim.LoadScenarioPreset("cold-shutdown");
            Assert.Equal(0, sim.Exposure.Count);
        }

        [Fact]
        public void RestoreSnapshot_KeepsTheLedger()
        {
            var sim = FullPowerSim(1.0); PutOnDeck(sim);
            var snap = sim.CaptureSnapshot();
            sim.Tick(); sim.Tick();
            double dose = Get(sim).DoseMSv;
            sim.RestoreSnapshot(snap);
            Assert.Equal(1, sim.Exposure.Count);
            Assert.Equal(dose, Get(sim).DoseMSv);
        }

        [Fact]
        public void WithNoSubjects_StateAndTelemetryAreUnchanged()
        {
            var a = FullPowerSim(1.0);
            var b = FullPowerSim(1.0);
            for (int i = 0; i < 30; i++) { a.Tick(); b.Tick(); }
            Assert.Equal(0, a.Exposure.Count);
            Assert.Equal(b.CaptureSnapshot().ToJson(), a.CaptureSnapshot().ToJson());
            Assert.Equal(b.GetTelemetryHistorySnapshot(), a.GetTelemetryHistorySnapshot());
        }

        [Fact]
        public void Subjects_DoNotChangePlantStateOrTelemetry()
        {
            var a = FullPowerSim(1.0);
            var b = FullPowerSim(1.0); PutOnDeck(b); b.Exposure.SetLocation("lost", "nowhere", 0, 0, 0);
            for (int i = 0; i < 30; i++) { a.Tick(); b.Tick(); }
            Assert.Equal(a.CaptureSnapshot().ToJson(), b.CaptureSnapshot().ToJson());
            Assert.Equal(a.GetTelemetryHistorySnapshot(), b.GetTelemetryHistorySnapshot());
        }

        [Fact]
        public void TwoIdenticalSimulations_GiveIdenticalDose()
        {
            var a = FullPowerSim(10.0); PutOnDeck(a);
            var b = FullPowerSim(10.0); PutOnDeck(b);
            for (int i = 0; i < 40; i++) { a.Advance(0.7); b.Advance(0.7); }
            Assert.Equal(Get(a).DoseMSv, Get(b).DoseMSv);
            Assert.Equal(a.TickCount, b.TickCount);
        }
    }
}
