// Primary pressure protection scenarios (--scenario pressure): the high-pressure reactor trip, the automatic PORV with its
// close hysteresis, manual PORV use, the safety valve bound, and a save/restore taken while the PORV is lifted. Setpoints
// and their sources are in Physics.cs. Every case prints its peak pressure so the run is a before/after record.

using System;
using System.Globalization;

namespace ReactorSim
{
    internal static class PressureScenarios
    {
        private static string F(double v) { return v.ToString("F1", CultureInfo.InvariantCulture); }

        private static Simulation FullPower()
        {
            var sim = new Simulation { DevMode = true }; // LoadPreset is a Dev intent
            sim.EnqueueIntent(PlantIntent.LoadPreset("full-power", "pressure"));
            sim.Tick();
            return sim;
        }

        private static void Need(bool ok, string what)
        {
            if (!ok) throw new InvalidOperationException("pressure scenario: " + what);
        }

        public static void Verify()
        {
            // 1. Loss of sink with the heaters full: pressure climbs, the PORV lifts, the high-pressure trip latches.
            var sim = FullPower();
            sim.EnqueueIntent(PlantIntent.SetHeaters(1.0, "pressure"));
            sim.EnqueueIntent(PlantIntent.SetSteamDemand(0.0, "pressure"));
            double max = 0; bool porv = false; double tripAt = -1;
            for (int i = 0; i < 12 * sim.TicksPerGameMinute; i++)
            {
                sim.Tick();
                var s = sim.CaptureSnapshot();
                max = Math.Max(max, s.PrimaryPressureBar); porv |= s.ReliefOpen;
                if (s.Scrammed && tripAt < 0) tripAt = sim.GameMinutes;
            }
            Console.WriteLine("  [pressure] loss-of-sink heaters-full  maxPpri=" + F(max) + " porv=" + (porv ? 1 : 0) + " trip@min=" + F(tripAt) + " cause=" + sim.Plant.LastScramCause);
            Need(sim.Plant.Scrammed && sim.Plant.LastScramCause == "high primary pressure", "loss of sink must trip on high primary pressure, got " + sim.Plant.LastScramCause);
            Need(porv, "the PORV must lift before or at the trip");
            Need(max >= Physics.TripHighPrimaryPressureBar && max < Physics.SafetyValveOpenBar, "peak pressure " + F(max) + " must reach the trip setpoint and stay under the safety valve setpoint");

            // 2. Heater excursion with the sink intact: the PORV cycles on its hysteresis, the plant never trips, pressure stays bounded.
            sim = FullPower();
            sim.EnqueueIntent(PlantIntent.SetHeaters(1.0, "pressure"));
            max = 0; int lifts = 0; bool prev = false;
            for (int i = 0; i < 10 * sim.TicksPerGameMinute; i++)
            {
                sim.Tick();
                var s = sim.CaptureSnapshot();
                max = Math.Max(max, s.PrimaryPressureBar);
                if (s.ReliefAutoOpen && !prev) lifts++;
                prev = s.ReliefAutoOpen;
                Need(!s.ReliefAutoOpen || s.PrimaryPressureBar > Physics.PorvCloseBar - 3.0, "PORV open at " + F(s.PrimaryPressureBar) + " bar, well under its reset");
            }
            Console.WriteLine("  [pressure] heater excursion           maxPpri=" + F(max) + " porvLifts=" + lifts + " scram=" + (sim.Plant.Scrammed ? 1 : 0));
            Need(!sim.Plant.Scrammed, "a heater excursion the PORV can handle must not trip the plant");
            Need(lifts >= 1, "the PORV must lift on the heater excursion");
            Need(max < Physics.TripHighPrimaryPressureBar, "peak " + F(max) + " must stay under the trip setpoint");

            // 3. Manual PORV at normal pressure: opens on demand, depressurises, closes on demand, and the pressure comes back.
            sim = FullPower();
            double p0 = sim.CaptureSnapshot().PrimaryPressureBar;
            sim.EnqueueIntent(PlantIntent.SetReliefValve(true, "pressure"));
            sim.RunGameMinutes(1.0, null);
            var open = sim.CaptureSnapshot();
            sim.EnqueueIntent(PlantIntent.SetReliefValve(false, "pressure"));
            sim.RunGameMinutes(2.0, null);
            var closed = sim.CaptureSnapshot();
            Console.WriteLine("  [pressure] manual PORV                " + F(p0) + " -> " + F(open.PrimaryPressureBar) + " open (manual=" + (open.ReliefManualOpen ? 1 : 0) + " auto=" + (open.ReliefAutoOpen ? 1 : 0) + ") -> " + F(closed.PrimaryPressureBar) + " closed");
            Need(open.ReliefOpen && open.ReliefManualOpen && !open.ReliefAutoOpen, "manual open must show as manual only");
            Need(open.PrimaryPressureBar < p0 - 5.0, "manual PORV must depressurise");
            Need(!closed.ReliefOpen && closed.PrimaryPressureBar > open.PrimaryPressureBar + 5.0 && !closed.Scrammed, "closing the PORV must let pressure recover without a trip");

            // 4. A save taken while the PORV is lifted resumes to the same state (the valve latch is part of the snapshot).
            sim = FullPower();
            sim.EnqueueIntent(PlantIntent.SetHeaters(1.0, "pressure"));
            for (int i = 0; i < 12 * sim.TicksPerGameMinute && !sim.CaptureSnapshot().ReliefAutoOpen; i++) sim.Tick();
            Need(sim.CaptureSnapshot().ReliefAutoOpen, "PORV never lifted for the save/restore case");
            string json = sim.Plant.CaptureState(sim.SimSeconds).ToJson();
            var restored = new Simulation { DevMode = true };
            restored.Plant.RestoreState(PlantSnapshot.FromJson(json));
            for (int i = 0; i < 2 * sim.TicksPerGameMinute; i++) { sim.Tick(); restored.Tick(); }
            double dp = Math.Abs(sim.Plant.Thermal.PrimaryPressureBar - restored.Plant.Thermal.PrimaryPressureBar);
            Console.WriteLine("  [pressure] save/restore while lifted  |dP|=" + dp.ToString("G3", CultureInfo.InvariantCulture) + " bar, relief " + sim.Plant.Thermal.ReliefOpen + "/" + restored.Plant.Thermal.ReliefOpen);
            Need(dp < 0.5 && sim.Plant.Thermal.ReliefAutoOpen == restored.Plant.Thermal.ReliefAutoOpen, "restored plant diverged from the original after a save while the PORV was lifted");
        }
    }
}
