// Scripted operator used by the --verify checks: cold shutdown -> critical -> held
// near a target power with steam matched. Drives Plant fields directly (verification
// probes internal state, not the intent pipeline) and asserts finite state each tick.

using System;

namespace ReactorSim
{
    internal static class PlantDriver
    {
        internal static bool Verbose;

        internal static void Run(Simulation sim, double minutes, string tag)
        {
            sim.RunGameMinutes(minutes, s =>
            {
                s.Plant.AssertFinite(tag + ":t=" + s.GameMinutes.ToString("F1"));
                if (Verbose)
                    Console.WriteLine(s.Plant.Telemetry(s.GameMinutes));
            });
        }

        internal static Simulation BringToPower(double target, double steam, Action<Simulation> configure = null)
        {
            var sim = new Simulation();
            configure?.Invoke(sim);
            var p = sim.Plant;
            p.Chemistry.DiluteRate = 1.5;
            p.Thermal.PrimaryFlowFrac = 1.0;
            p.Thermal.SteamDemandFrac = 0.0;
            p.Thermal.HeaterPowerFrac = 1.0; // primary heat-up before high power

            for (int i = 0; i < 3; i++)
                p.Banks[i].InsertionTarget = 0.0; // shutdown banks out
            Run(sim, 8, "bring:SD");

            // Approach criticality while heaters warm the primary toward the
            // hot band so Doppler doesn't swing wildly on a cold core.
            p.Banks[3].InsertionTarget = 0.35;
            Run(sim, 6, "bring:A");
            p.Chemistry.DiluteRate = 0.0;
            p.Banks[3].InsertionTarget = 0.12;
            Run(sim, 5, "bring:crit");

            // Nuclear + heater heat-up at low steam demand.
            HoldNearPower(sim, Math.Min(0.2, Math.Max(0.08, target * 0.25)), steam: 0.05, minutes: 20, tag: "bring:heatup");
            p.Thermal.HeaterPowerFrac = 0.2;

            // Climb to target with steam matched to power.
            HoldNearPower(sim, target, steam, minutes: 25, tag: "bring:hold");
            return sim;
        }

        private static void HoldNearPower(Simulation sim, double targetPower, double steam, int minutes, string tag)
        {
            var p = sim.Plant;
            p.Thermal.SteamDemandFrac = steam;
            p.Thermal.PrimaryFlowFrac = 1.0;
            p.Chemistry.DiluteRate = 0;
            for (int i = 0; i < minutes; i++)
            {
                TrimPower(sim, targetPower, steam);
                Run(sim, 1, tag);
            }
        }

        private static void TrimPower(Simulation sim, double targetPower, double steam)
        {
            var p = sim.Plant;
            // Match steam to target so secondary demand doesn't fight the rods.
            p.Thermal.SteamDemandFrac = Physics.Clamp(steam, 0, 1);
            if (p.Scrammed) return;

            double err = targetPower - p.Core.Power;
            // Very small rod steps + wide deadband: Xe/Doppler make aggressive
            // trim limit-cycle between source and the 300% ceiling.
            double a = p.Banks[3].InsertionTarget;
            if (err > 0.08) a -= 0.008;
            else if (err < -0.08) a += 0.008;

            if (err > 0.2 && a <= 0.02 && p.Chemistry.BoronPpm > 20)
                p.Chemistry.DiluteRate = 0.25;
            else if (err < -0.25 && p.Chemistry.BoronPpm < 1200)
                p.Chemistry.BorateRate = 0.25;
            else
            {
                p.Chemistry.DiluteRate = 0;
                p.Chemistry.BorateRate = 0;
            }
            p.Banks[3].InsertionTarget = Physics.Clamp(a, 0, 1);
        }
    }
}
