using ReactorSim;

/// <summary>
/// U11 start state for both Sandbox and Missions: cold shutdown, primary pumps OFF, assistant hints on
/// (Advisory tier), mission M1 selected with step 1 ("establish primary circulation") live.
/// UnityEngine-free so ReactorSim.Core.Tests can run it. Uses only the Player-safe setup surface (the
/// preset load is the host's, not an intent); no DevMode flip.
/// </summary>
public static class SandboxStart
{
    public static void Apply(Simulation sim)
    {
        sim.LoadScenarioPreset("cold-shutdown");
        // The preset carries primary flow 1.0 and its load latched mission step 1 as done: zero the flow and
        // re-select M1 so "start the pumps" is the live first step and first hint.
        sim.Plant.Thermal.PrimaryFlowFrac = 0.0;
        sim.Plant.Assistant.Tier = AssistTier.Advisory;
        sim.Plant.Assistant.SelectMission(0, sim.Plant);
        sim.Plant.Assistant.Evaluate(sim.Plant, 0.0);
    }
}
