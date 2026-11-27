using Xunit;

namespace ReactorSim.Tests
{
    internal static class RadiationTestPlants
    {
        public static Plant AtPower()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            return p;
        }

        public static Plant Cold()
        {
            var p = new Plant();
            p.LoadPreset("cold-shutdown");
            return p;
        }

        public static Plant Tripped(int steps = 5)
        {
            var p = AtPower();
            p.Apply(PlantIntent.TriggerScram());
            for (int i = 0; i < steps; i++) p.Step(1.0);
            return p;
        }

        public static Simulation FullPowerSim(double scale)
        {
            var sim = new Simulation(new Tuning { DefaultScale = scale }) { DevMode = true };
            sim.LoadScenarioPreset("full-power");
            return sim;
        }

        public static void PutOnDeck(Simulation sim, string id = "op1")
        {
            RadPoint pt;
            Assert.True(RadiationAreas.TryGetReferencePoint(RadiationAreas.ReactorDeckId, out pt));
            Assert.Equal(LedgerRefusal.None, sim.Exposure.SetLocation(id, RadiationAreas.ReactorDeckId, pt.X, pt.Y, pt.H));
        }

        public static Simulation FullPowerSimOnDeck(double scale, string id = "op1")
        {
            var sim = FullPowerSim(scale);
            PutOnDeck(sim, id);
            return sim;
        }
    }
}
