using System;
using Xunit;
using ReactorSim;
using ReactorSim.Instruments;

namespace ReactorSim.Core.Tests
{
    // U8: every lamp is false just short of its threshold and true just past it; the formerly dead tiles can light.
    public class LampPredicatesTests
    {
        private static bool On(string lamp, PlantSnapshot s)
        {
            return LampPredicates.Evaluate(lamp, s, LampContext.Default);
        }

        // A healthy snapshot: no lamp except the ones that are healthy-state status lamps.
        private static PlantSnapshot Healthy()
        {
            var s = new PlantSnapshot();
            s.Power = 0.5; s.PeriodSeconds = 99999.0; s.FuelTempC = 600; s.PrimaryPressureBar = 155;
            s.PrimarySatMarginC = 40; s.SecondaryPressureBar = 60; s.SecondaryLevelFrac = 0.6;
            s.PrimaryFlowFrac = 1.0; s.XenonPcm = 0; s.BoronPpm = 1500; s.PrimaryTempC = 300;
            s.PrimaryLevelFrac = 0.6; s.HeaterPowerFrac = 0.25; s.SprayFrac = 0; s.FeedwaterFlowFrac = 1.0;
            s.CoolingWaterFrac = 1.0; s.Ph = 7.2; s.BankIntegrity = new double[] { 1, 1, 1, 1 };
            s.CondenserPressureBar = 0.5; s.HotwellInventoryFrac = 0.7; s.GridFrequencyHz = 50.0;
            s.GeneratorSynced = true; s.Mode = "Nominal";
            return s;
        }

        [Fact]
        public void HealthySnapshot_LightsNoAlarm()
        {
            var s = Healthy();
            foreach (var n in LampPredicates.Names)
            {
                if (n == "GEN_BREAKER_CLOSED") { Assert.True(On(n, s)); continue; }
                Assert.False(On(n, s), n + " lit on a healthy snapshot");
            }
        }

        public static readonly object[][] Thresholds =
        {
            // lamp, mutator that sets the value just short (off) / just past (on) the threshold
            new object[] { "HIGH_FLUX", 1.09, 1.0901, "Power" },
            new object[] { "FUEL_TEMP_HI", 1200.0, 1200.01, "FuelTempC" },
            new object[] { "PRI_PRESS_HI", 165.0, 165.01, "PrimaryPressureBar" },
            new object[] { "PRI_PRESS_LO", 100.0, 99.99, "PrimaryPressureBar" },
            new object[] { "SUBCOOL_LO", 15.0, 14.99, "PrimarySatMarginC" },
            new object[] { "SG_PRESS_HI", 80.0, 80.01, "SecondaryPressureBar" },
            new object[] { "SG_LVL_LO", 0.25, 0.2499, "SecondaryLevelFrac" },
            new object[] { "RCP_LOW_FLOW", 0.25, 0.2499, "PrimaryFlowFrac" },
            new object[] { "FLOW_LO", 0.4, 0.3999, "PrimaryFlowFrac" },
            new object[] { "XENON_PIT", -800.0, -800.01, "XenonPcm" },
            new object[] { "BORON_LOW", 400.0, 399.99, "BoronPpm" },
            new object[] { "TEMP_HI", 330.0, 330.01, "PrimaryTempC" },
            new object[] { "LEVEL_LO", 0.4, 0.3999, "PrimaryLevelFrac" },
            new object[] { "HEATER_OFF", 0.0011, 0.001, "HeaterPowerFrac" },
            new object[] { "SPRAY_FAULT", 0.99, 0.9901, "SprayFrac" },
            new object[] { "COOLING_LOSS", 0.3, 0.2999, "CoolingWaterFrac" },
            new object[] { "COND_VAC_WARN", 1.3, 1.3001, "CondenserPressureBar" },
            new object[] { "HOTWELL_LOW", 0.3, 0.2999, "HotwellInventoryFrac" },
            new object[] { "PH_DRIFT", 6.0, 5.9999, "Ph" },
            new object[] { "PH_DRIFT", 8.2, 8.2001, "Ph" },
            new object[] { "GRID_FREQ_OFF", 50.5, 50.5001, "GridFrequencyHz" },
            new object[] { "GRID_FREQ_OFF", 49.5, 49.4999, "GridFrequencyHz" },
        };

        private static void Set(PlantSnapshot s, string field, double v)
        {
            var f = typeof(PlantSnapshot).GetField(field);
            Assert.NotNull(f);
            f.SetValue(s, v);
        }

        [Fact]
        public void EachNumericLamp_FalseJustBefore_TrueJustPast()
        {
            foreach (var t in Thresholds)
            {
                string lamp = (string)t[0];
                var off = Healthy(); Set(off, (string)t[3], (double)t[1]);
                var on = Healthy(); Set(on, (string)t[3], (double)t[2]);
                Assert.False(On(lamp, off), lamp + " lit at its threshold value " + t[1]);
                Assert.True(On(lamp, on), lamp + " not lit just past threshold " + t[2]);
            }
        }

        [Fact]
        public void RodWear_LightsWhenAnyBankIntegrityDropsBelowThreshold()
        {
            var s = Healthy();
            s.BankIntegrity = new double[] { 1, 0.8, 1, 1 };
            Assert.False(On("ROD_WEAR", s));
            s.BankIntegrity = new double[] { 1, 0.7999, 1, 1 };
            Assert.True(On("ROD_WEAR", s));
        }

        [Fact]
        public void ShortPeriod_UsesTheContextPeriod()
        {
            var s = Healthy();
            var ctx = new LampContext { ShortPeriodSeconds = 60.0 };
            s.PeriodSeconds = 60.0;
            Assert.False(LampPredicates.Evaluate("SHORT_PERIOD", s, ctx));
            s.PeriodSeconds = 59.9;
            Assert.True(LampPredicates.Evaluate("SHORT_PERIOD", s, ctx));
            s.PeriodSeconds = -5.0;
            Assert.False(LampPredicates.Evaluate("SHORT_PERIOD", s, ctx));
        }

        [Fact]
        public void FeedFail_NeedsBothLowFlowAndPower()
        {
            var s = Healthy();
            s.FeedwaterFlowFrac = 0.0499; s.Power = 0.10;
            Assert.False(On("FEED_FAIL", s));
            s.Power = 0.11;
            Assert.True(On("FEED_FAIL", s));
            s.FeedwaterFlowFrac = 0.05;
            Assert.False(On("FEED_FAIL", s));
        }

        [Fact]
        public void Mode_LightsWhenTheModeDiffersFromThePreviousSnapshot()
        {
            var prev = Healthy(); prev.Mode = "Shutdown";
            var cur = Healthy(); cur.Mode = "Nominal";
            var ctx = new LampContext { ShortPeriodSeconds = 60.0, Previous = prev };
            Assert.True(LampPredicates.Evaluate("MODE", cur, ctx));
            ctx.Previous = cur;
            Assert.False(LampPredicates.Evaluate("MODE", cur, ctx));
            ctx.Previous = null;
            Assert.False(LampPredicates.Evaluate("MODE", cur, ctx));
        }

        [Fact]
        public void BopTripLamps_FollowTheirMaskBit()
        {
            string[] lamps = { "TURB_OVERSPEED", "TURB_UNDERSPEED", "COND_LOW_VACUUM", "SG_LOW_LEVEL_TRIP", "GEN_OUT_OF_SYNC" };
            for (int i = 0; i < lamps.Length; i++)
            {
                var s = Healthy();
                s.BopTripMask = 0;
                Assert.False(On(lamps[i], s), lamps[i]);
                s.BopTripMask = 1 << i;
                Assert.True(On(lamps[i], s), lamps[i]);
                for (int j = 0; j < lamps.Length; j++)
                    if (j != i) Assert.False(On(lamps[j], s), lamps[j] + " lit by bit " + i);
            }
        }

        [Fact]
        public void BopTripBits_MatchBalanceOfPlantOrder()
        {
            // Bit i of BopTripMask is BopTripKind i; the lamp table above relies on that order.
            Assert.Equal(0, (int)BopTripKind.Overspeed);
            Assert.Equal(1, (int)BopTripKind.Underspeed);
            Assert.Equal(2, (int)BopTripKind.LowVacuum);
            Assert.Equal(3, (int)BopTripKind.SgLowLevel);
            Assert.Equal(4, (int)BopTripKind.OutOfSync);
        }

        [Fact]
        public void StatusLamps_TurbineTripped_Critical_BreakerPair()
        {
            var s = Healthy();
            Assert.False(On("TURBINE_TRIPPED", s));
            s.TurbineTripped = true;
            Assert.True(On("TURBINE_TRIPPED", s));
            Assert.False(On("CRITICAL", s));
            s.IsCritical = true;
            Assert.True(On("CRITICAL", s));
            s.GeneratorSynced = true;
            Assert.True(On("GEN_BREAKER_CLOSED", s)); Assert.False(On("GEN_BREAKER_OPEN", s));
            s.GeneratorSynced = false;
            Assert.False(On("GEN_BREAKER_CLOSED", s)); Assert.True(On("GEN_BREAKER_OPEN", s));
            Assert.Equal(LampStyle.RedSteady, LampPredicates.Style("GEN_BREAKER_CLOSED"));
            Assert.Equal(LampStyle.GreenSteady, LampPredicates.Style("GEN_BREAKER_OPEN"));
        }

        [Fact]
        public void PauseLamp_FollowsTheClock()
        {
            var s = Healthy();
            Assert.False(LampPredicates.Evaluate("Pause", s, new LampContext { ClockPausedOrFrozen = false }));
            Assert.True(LampPredicates.Evaluate("Pause", s, new LampContext { ClockPausedOrFrozen = true }));
        }

        [Fact]
        public void EveryPredicate_ReturnsWithoutThrowing_OnRealSnapshots()
        {
            foreach (var preset in new[] { "cold-shutdown", "full-power" })
            {
                var sim = new Simulation(new Tuning());
                sim.LoadScenarioPreset(preset);
                for (int i = 0; i < 5; i++) sim.Tick();
                var s = sim.CaptureSnapshot();
                foreach (var n in LampPredicates.Names) On(n, s);
            }
        }

        [Fact]
        public void UnknownLamp_IsOff()
        {
            Assert.False(LampPredicates.Has("NOPE"));
            Assert.False(On("NOPE", Healthy()));
        }
    }
}
