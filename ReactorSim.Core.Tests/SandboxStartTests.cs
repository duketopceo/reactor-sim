using Xunit;

namespace ReactorSim.Tests
{
    // U11: Sandbox start = cold shutdown, pumps off, hints on, first hint/step is "start the pumps".
    public class SandboxStartTests
    {
        [Fact]
        public void ColdShutdown_PumpsOff_FirstStepIsStartPumps_HintsOn()
        {
            var sim = new Simulation(new Tuning());
            SandboxStart.Apply(sim);

            Assert.Equal(0.0, sim.Plant.Thermal.PrimaryFlowFrac);
            Assert.False(sim.DevMode);
            Assert.Equal(AssistTier.Advisory, sim.Plant.Assistant.Tier);
            var m = sim.Plant.Assistant.CurrentMission;
            Assert.Equal(0, m.Index);
            Assert.Equal(0, m.CurrentStepIndex);
            Assert.StartsWith("1.", m.Steps[0].Title);
            Assert.False(m.Steps[0].Completed);
            Assert.Equal(0, sim.TickCount);
        }

        [Fact]
        public void PumpsStayOffUntilThePlayerStartsThem()
        {
            var sim = new Simulation(new Tuning());
            SandboxStart.Apply(sim);
            for (int i = 0; i < 30; i++) sim.Tick();
            Assert.Equal(0.0, sim.Plant.Thermal.PrimaryFlowFrac);
            Assert.Equal(0, sim.Plant.Assistant.CurrentMission.CurrentStepIndex);

            sim.EnqueueIntent(PlantIntent.SetPrimaryFlow(1.0, "player"));
            sim.Tick();
            Assert.True(sim.Plant.Thermal.PrimaryFlowFrac > 0.8);
            Assert.True(sim.Plant.Assistant.CurrentMission.CurrentStepIndex >= 1);
        }
    }
}
