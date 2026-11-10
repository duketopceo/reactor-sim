// U10 balance-of-plant scenarios: auto-load generation at power, the manual turbine/generator
// choreography (load rejection -> overspeed trip -> reset -> re-sync), and loss of condenser
// cooling (low-vacuum trip -> stop valve shut -> heat goes to relief). Invariants, not goldens.

using System;
using System.Globalization;

namespace ReactorSim
{
    internal static class BopScenarios
    {
        private static void AssertTrue(bool cond, string msg)
        {
            if (!cond) throw new InvalidOperationException(msg);
        }

        private static string F(double v) { return v.ToString("G4", CultureInfo.InvariantCulture); }

        private static Simulation FullPower()
        {
            var sim = new Simulation { DevMode = true }; // LoadPreset is a Dev intent
            sim.EnqueueIntent(PlantIntent.LoadPreset("full-power", "u10"));
            sim.Tick();
            return sim;
        }

        private static void Run(Simulation sim, double minutes, string tag)
        {
            sim.RunGameMinutes(minutes, s => s.Plant.AssertFinite(tag));
        }

        // U10.1: at full power the governor holds 3000 rpm and exports ~50 MWe with no trips;
        // condenser at vacuum, hotwell and SG level steady.
        public static void VerifyAutoLoadAtPower()
        {
            var sim = FullPower();
            Run(sim, 10, "U10.1");
            var p = sim.Plant; var b = p.Bop;
            AssertTrue(b.AutoLoad, "auto-load should be the default");
            AssertTrue(b.TripMask == 0, "unexpected BOP trip: " + (b.FirstActiveTrip != null ? b.FirstActiveTrip.Code : "?"));
            AssertTrue(b.Turbine.Synced && Math.Abs(b.Turbine.Rpm - Physics.TurbineRatedRpm) < 1e-9, "turbine not synced at rated speed: rpm=" + F(b.Turbine.Rpm));
            double mwe = b.Turbine.GeneratorKw / 1000.0;
            AssertTrue(mwe > 40.0 && mwe < 65.0, "generator output " + F(mwe) + " MWe outside 40..65 at ~96% steam");
            double eff = b.Turbine.GeneratorKw / (p.Core.TotalHeatPower * p.RatedThermalKW);
            AssertTrue(eff > 0.25 && eff < 0.34, "net plant efficiency " + F(eff) + " outside 0.25..0.34");
            AssertTrue(b.CondenserPressureBar < 0.2, "condenser not at vacuum: " + F(b.CondenserPressureBar) + " bar");
            AssertTrue(b.HotwellInventoryFrac > 0.5 && b.HotwellInventoryFrac < 0.9, "hotwell drifted: " + F(b.HotwellInventoryFrac));
            AssertTrue(Math.Abs(p.Thermal.SecondaryLevelFrac - 0.6) < 0.05, "SG level drifted: " + F(p.Thermal.SecondaryLevelFrac));
            AssertTrue(Math.Abs(b.GridFrequencyHz - Physics.GridNominalFrequencyHz) < 1e-9, "auto-load frequency off nominal: " + F(b.GridFrequencyHz));
        }

        // U10.2: manual mode. Bumpless transfer at the current output; opening the breaker under
        // load rejects it, the rotor runs to the overspeed trip and the stop valve shuts while the
        // dump carries the heat. Once the rotor coasts below the trip line the operator resets,
        // governs the throttle into the sync band, closes the breaker and takes 50 % load.
        public static void VerifyManualChoreography()
        {
            var sim = FullPower();
            var p = sim.Plant; var b = p.Bop;
            Run(sim, 2, "U10.2:auto");
            sim.EnqueueIntent(PlantIntent.SetGridDemand(b.GridOutputFrac, "u10"));
            sim.EnqueueIntent(PlantIntent.SetTurbineAutoLoad(false, "u10"));
            Run(sim, 2, "U10.2:manual");
            AssertTrue(b.Turbine.Synced && b.TripMask == 0, "bumpless manual transfer should stay synced without trips; rpm=" + F(b.Turbine.Rpm));

            // Load rejection.
            sim.EnqueueIntent(PlantIntent.SetGeneratorBreaker(false, "u10"));
            sim.EnqueueIntent(PlantIntent.SetSteamDump(0.6, "u10"));
            Run(sim, 1, "U10.2:reject");
            var over = b.Trip(BopTripKind.Overspeed);
            AssertTrue(over.Active, "load rejection with steam on should overspeed-trip; rpm=" + F(b.Turbine.Rpm));
            AssertTrue(over.Measured >= Physics.TurbineOverspeedTripRpm, "trip should record the measured speed, got " + F(over.Measured));
            AssertTrue(!p.Thermal.TurbineStopValveOpen && p.Thermal.TurbineSteamKW == 0.0, "stop valve should be shut after the trip");
            AssertTrue(p.Thermal.DumpSteamKW > 0.0, "steam dump should carry the heat after the trip");
            AssertTrue(!b.SetBreaker(true), "closing the breaker on a tripped turbine must be refused");

            // Coast below the trip line, then reset with the throttle closed.
            for (int i = 0; i < 300 && b.Turbine.Rpm >= Physics.TurbineRatedRpm + 100.0; i++) sim.Tick();
            AssertTrue(b.Turbine.Rpm < Physics.TurbineRatedRpm + 100.0, "rotor did not coast down: rpm=" + F(b.Turbine.Rpm));
            sim.EnqueueIntent(PlantIntent.SetSteamDemand(0.0, "u10"));
            sim.EnqueueIntent(PlantIntent.SetGridDemand(0.0, "u10"));
            sim.EnqueueIntent(PlantIntent.ResetBopTrips("u10"));
            sim.Tick();
            AssertTrue(!b.Turbine.Tripped && b.TripMask == 0 && p.Thermal.TurbineStopValveOpen, "reset should clear the latch and reopen the stop valve");

            // Spin into the band and sync.
            int inBand = 0;
            for (int m = 0; m < 60 && inBand < 3; m++)
            {
                Govern(sim, "U10.2:spin");
                inBand = b.Turbine.InSyncBand ? inBand + 1 : 0;
            }
            AssertTrue(inBand >= 3, "operator could not hold the unloaded rotor in the sync band; rpm=" + F(b.Turbine.Rpm));
            sim.EnqueueIntent(PlantIntent.SetGeneratorBreaker(true, "u10"));
            sim.Tick();
            AssertTrue(b.Turbine.Synced, "breaker should close inside the band");

            // Ramp to 50 % load at 2.5 %/min with the throttle leading (feedforward + trim), then hold.
            for (int m = 0; m < 30; m++)
            {
                double demand = Math.Min(0.5, 0.025 * (m + 1));
                sim.EnqueueIntent(PlantIntent.SetGridDemand(demand, "u10"));
                Govern(sim, "U10.2:load", demand);
            }
            AssertTrue(b.TripMask == 0, "trip while loading: " + (b.FirstActiveTrip != null ? b.FirstActiveTrip.Code : "?"));
            AssertTrue(b.Turbine.Synced, "lost the breaker while loading");
            AssertTrue(Math.Abs(b.GridOutputFrac - 0.5) <= Physics.GridToleranceFrac, "output should track 50 % demand, got " + F(b.GridOutputFrac));
            AssertTrue(b.GridInTolerance, "grid should report in tolerance");
        }

        // One game minute of a player acting as the governor. Unloaded: trim the throttle toward
        // 3000 rpm. Loaded: set the throttle for (load + windage) from the current steam-per-valve
        // ratio, then trim on the speed error.
        private static void Govern(Simulation sim, string tag, double demandFrac = 0.0)
        {
            var p = sim.Plant; var th = p.Thermal; var t = p.Bop.Turbine;
            double err = (Physics.TurbineRatedRpm - t.Rpm) / Physics.TurbineRatedRpm;
            double sd = th.SteamDemandFrac + 0.15 * err;
            if (demandFrac > 0.0 && th.SteamDemandFrac > 1e-3 && th.TurbineSteamKW > 0.0)
            {
                double steamPerValve = th.TurbineSteamKW / th.SteamDemandFrac;
                double needMech = demandFrac * Physics.GeneratorRatedKw + Physics.TurbineWindageKwAtRated;
                sd = needMech / (Physics.TurbineMechEfficiency * t.VacuumEfficiencyFrac * steamPerValve) + 0.15 * err;
            }
            sim.EnqueueIntent(PlantIntent.SetSteamDemand(Physics.Clamp(sd, 0.0, 1.0), "u10"));
            Run(sim, 1, tag);
        }

        // U10.3: losing circulating water at power: condenser backpressure climbs to the
        // low-vacuum trip, the stop valve shuts, and with no condenser the SG vents via relief.
        public static void VerifyLossOfCondenserCooling()
        {
            var sim = FullPower();
            var p = sim.Plant; var b = p.Bop;
            sim.EnqueueIntent(PlantIntent.SetCoolingWater(0.0, "u10"));
            bool reliefSeen = false;
            double maxCondenser = 0;
            sim.RunGameMinutes(6, s =>
            {
                s.Plant.AssertFinite("U10.3");
                maxCondenser = Math.Max(maxCondenser, s.Plant.Bop.CondenserPressureBar);
                reliefSeen |= s.Plant.Thermal.SecondaryReliefOpen;
            });
            AssertTrue(b.Trip(BopTripKind.LowVacuum).Active, "loss of cooling water should trip on low vacuum; max condenser " + F(maxCondenser) + " bar");
            AssertTrue(b.FirstActiveTrip.Kind == BopTripKind.LowVacuum, "first trip should be low vacuum, got " + b.FirstActiveTrip.Code);
            AssertTrue(p.Thermal.TurbineSteamKW == 0.0, "stop valve should be shut");
            AssertTrue(reliefSeen, "with the condenser lost the SG should lift its relief valves");
            AssertTrue(b.Turbine.GeneratorKw == 0.0, "no generation after the trip");

            // Snapshot round trip keeps the latched trip and its evidence.
            var snap = PlantSnapshot.FromJson(p.CaptureState(sim.SimSeconds).ToJson());
            var restored = new Plant();
            restored.RestoreState(snap);
            AssertTrue(restored.Bop.Trip(BopTripKind.LowVacuum).Active, "snapshot lost the low-vacuum trip");
            AssertTrue(Math.Abs(restored.Bop.Trip(BopTripKind.LowVacuum).Measured - b.Trip(BopTripKind.LowVacuum).Measured) < 1e-12, "snapshot lost the trip evidence");
            AssertTrue(!restored.Thermal.TurbineStopValveOpen, "restored plant should keep the stop valve shut");
        }

        // U2: cold shutdown -> criticality -> held power -> breaker close, timed in real seconds the way
        // SimClock would spend them: every tick costs TickDt / EffectiveScale real seconds, where the scale
        // is the profile default and drops to the auto-slow scale on a short period, a trip or a SCRAM.
        // The scripted operator (PlantDriver.BringToPower) runs the plant in the default auto-load mode, so the
        // governor closes the breaker when the rotor reaches the sync band; this scenario times that close.
        public static void VerifyColdToSyncTiming()
        {
            double real = 0.0, slowReal = 0.0, realAtSync = -1.0;
            int slowTicks = 0, ticks = 0;
            string slowWhy = "";
            var sim = PlantDriver.BringToPower(0.5, 0.5, s =>
            {
                double scaleNow = s.Clock.EffectiveScale;
                s.OnTick = t =>
                {
                    double dtReal = t.TickDt / scaleNow;
                    real += dtReal;
                    ticks++;
                    if (t.Clock.AutoSlow) { slowReal += dtReal; slowTicks++; slowWhy = t.Clock.AutoSlowReason; }
                    if (realAtSync < 0.0 && t.Plant.Bop.Turbine.Synced) realAtSync = real;
                    t.Clock.ElapseReal(dtReal);
                    scaleNow = t.Clock.EffectiveScale;
                };
            });
            var b = sim.Plant.Bop;
            AssertTrue(b.TripMask == 0, "BOP tripped on the way to sync: " + (b.FirstActiveTrip != null ? b.FirstActiveTrip.Code : "?"));
            AssertTrue(b.Turbine.Synced && b.Turbine.GeneratorKw > 0.0, "breaker never closed: rpm=" + F(b.Turbine.Rpm));
            double minutes = Math.Max(real, realAtSync) / 60.0;
            Console.WriteLine("  [cold-to-sync] held power " + F(sim.Plant.Core.Power) + " and breaker closed (first at real " + F(realAtSync / 60.0)
                + " min) by game minute " + F(sim.GameMinutes) + "; real-time equivalent " + F(minutes) + " min at default scale "
                + F(sim.Clock.Tuning.DefaultScale) + "x, auto-slow " + F(sim.Clock.Tuning.AutoSlowScale) + "x for " + F(slowReal / 60.0)
                + " min (" + slowTicks + "/" + ticks + " ticks, last reason: " + slowWhy + ")");
            AssertTrue(minutes <= 15.0, "cold-to-sync takes " + F(minutes) + " real minutes at the default profile (limit 15)");
        }
    }
}
