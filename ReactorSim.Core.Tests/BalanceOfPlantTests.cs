using Xunit;

namespace ReactorSim.Tests
{
    public class BalanceOfPlantTests
    {
        private static Plant FullPower()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            return p;
        }

        private static void Step(Plant p, int ticks, double dt = 6.0)
        {
            for (int i = 0; i < ticks; i++) p.Step(dt);
        }

        [Fact]
        public void FreshPlant_DefaultsToAutoLoad_ColdRotor_NoTrips()
        {
            var b = new Plant().Bop;
            Assert.True(b.AutoLoad);
            Assert.Equal(0.0, b.Turbine.Rpm);
            Assert.False(b.Turbine.Synced);
            Assert.Equal(0, b.TripMask);
            Assert.Null(b.FirstActiveTrip);
        }

        [Fact]
        public void FullPowerPreset_GeneratesInAutoLoad()
        {
            var p = FullPower();
            Step(p, 20);
            Assert.True(p.Bop.Turbine.Synced);
            Assert.Equal(Physics.TurbineRatedRpm, p.Bop.Turbine.Rpm);
            Assert.InRange(p.Bop.Turbine.GeneratorKw, 40000.0, 65000.0);
            Assert.Equal(0, p.Bop.TripMask);
        }

        [Fact]
        public void SyncClose_RefusedOutsideBand()
        {
            var b = new Plant().Bop;
            b.Turbine.Rpm = Physics.TurbineRatedRpm - Physics.GeneratorSyncRpmBand - 1.0;
            Assert.False(b.SetBreaker(true));
            Assert.False(b.Turbine.Synced);
        }

        [Fact]
        public void SyncClose_InsideBand_Closes()
        {
            var b = new Plant().Bop;
            b.Turbine.Rpm = Physics.TurbineRatedRpm + Physics.GeneratorSyncRpmBand - 1.0;
            Assert.True(b.SetBreaker(true));
            Assert.True(b.Turbine.Synced);
        }

        [Fact]
        public void SyncClose_RefusedWhileTripped_AndResetLeavesBreakerOpen()
        {
            var p = FullPower();
            p.Bop.Turbine.Trip();
            Assert.False(p.Bop.SetBreaker(true));
            p.Apply(PlantIntent.ResetBopTrips());
            Assert.False(p.Bop.Turbine.Tripped);
            Assert.False(p.Bop.Turbine.Synced);
            Assert.True(p.Bop.SetBreaker(true)); // still at rated speed: closes again
        }

        [Fact]
        public void OverspeedTrip_ShutsStopValve_AndLatchesEvidence()
        {
            var p = FullPower();
            p.Apply(PlantIntent.SetTurbineAutoLoad(false));
            p.Apply(PlantIntent.SetGeneratorBreaker(false));     // load rejection with steam on
            Step(p, 10);
            var t = p.Bop.Trip(BopTripKind.Overspeed);
            Assert.True(t.Active);
            Assert.True(t.Measured >= Physics.TurbineOverspeedTripRpm);
            Assert.False(p.Thermal.TurbineStopValveOpen);
            Assert.Equal(0.0, p.Thermal.TurbineSteamKW);
            Assert.Equal(1 << (int)BopTripKind.Overspeed, p.Bop.TripMask & (1 << (int)BopTripKind.Overspeed));
        }

        [Fact]
        public void UnderspeedTrip_OnlyArmedInManualWhileSynced()
        {
            var auto = FullPower();
            auto.Bop.Turbine.Rpm = Physics.TurbineUnderspeedTripRpm - 10.0;
            auto.Bop.Turbine.Synced = true;
            Step(auto, 1);
            Assert.False(auto.Bop.Trip(BopTripKind.Underspeed).Active);

            var manual = FullPower();
            manual.Apply(PlantIntent.SetTurbineAutoLoad(false));
            manual.Apply(PlantIntent.SetSteamDemand(0.0)); // no shaft power: the rotor keeps slowing
            manual.Bop.Turbine.Rpm = Physics.TurbineUnderspeedTripRpm - 10.0;
            manual.Bop.Turbine.Synced = true;
            Step(manual, 1);
            Assert.True(manual.Bop.Trip(BopTripKind.Underspeed).Active);

            var unsynced = FullPower();
            unsynced.Apply(PlantIntent.SetTurbineAutoLoad(false));
            unsynced.Bop.Turbine.Synced = false;
            unsynced.Bop.Turbine.Rpm = 100.0;
            unsynced.Apply(PlantIntent.SetSteamDemand(0.0));
            Step(unsynced, 1);
            Assert.False(unsynced.Bop.Trip(BopTripKind.Underspeed).Active);
        }

        [Fact]
        public void LossOfCoolingWater_TripsOnLowVacuum()
        {
            var p = FullPower();
            p.Apply(PlantIntent.SetCoolingWater(0.0));
            Step(p, 30);
            Assert.True(p.Bop.Trip(BopTripKind.LowVacuum).Active);
            Assert.Equal(BopTripKind.LowVacuum, p.Bop.FirstActiveTrip.Kind);
            // After the stop valve shuts the pumps pull the shell back down; the latch keeps the evidence.
            Assert.True(p.Bop.Trip(BopTripKind.LowVacuum).Measured >= Physics.CondenserLowVacuumTripBar);
            Assert.Equal(0.0, p.Thermal.TurbineSteamKW);
        }

        [Fact]
        public void UnsyncedGenerator_NoOutput_NominalFrequency()
        {
            var p = new Plant();
            Step(p, 5);
            Assert.False(p.Bop.Turbine.Synced);
            Assert.Equal(0.0, p.Bop.GridOutputFrac);
            Assert.Equal(Physics.GridNominalFrequencyHz, p.Bop.GridFrequencyHz);
        }

        [Fact]
        public void ManualUndergeneration_SagsFrequency()
        {
            var p = FullPower();
            Step(p, 2);
            p.Apply(PlantIntent.SetTurbineAutoLoad(false));
            p.Apply(PlantIntent.SetGridDemand(1.0));
            p.Apply(PlantIntent.SetSteamDemand(0.3));
            Step(p, 1);
            Assert.True(p.Bop.GridOutputFrac < 1.0);
            Assert.True(p.Bop.GridFrequencyHz < Physics.GridNominalFrequencyHz);
        }

        [Fact]
        public void Rpm_ClampsAtSpeedCeiling()
        {
            var p = new Plant();
            p.Bop.Turbine.Rpm = Physics.TurbineSpeedCeilingRpm * 2.0;
            Step(p, 1);
            Assert.True(p.Bop.Turbine.Rpm <= Physics.TurbineSpeedCeilingRpm);
        }

        [Fact]
        public void SteamDump_CarriesHeatToCondenser()
        {
            var p = FullPower();
            p.Apply(PlantIntent.SetSteamDump(0.5));
            Step(p, 1);
            Assert.True(p.Thermal.DumpSteamKW > 0.0);
            Assert.True(p.Bop.CondenserExhaustFrac > p.Thermal.TurbineSteamKW / p.RatedThermalKW);
        }

        [Fact]
        public void HotwellStarvation_LimitsFeedwater()
        {
            var p = FullPower();
            p.Bop.HotwellInventoryFrac = 0.0;
            p.Apply(PlantIntent.SetCoolingWater(0.0)); // no condensate return
            Step(p, 1);
            Assert.True(p.Thermal.FeedwaterAvailabilityFrac < 1.0);
        }
    }
}
