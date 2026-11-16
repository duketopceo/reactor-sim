using System;
using Xunit;

namespace ReactorSim.Tests
{
    // Regression tests for the three simulation defects documented as verification XFAILs
    // (DOM-01, SCR-02, EDGE-08; docs/testing/simulation-verification.md).
    public class SaturationDomainTests
    {
        [Theory]
        [InlineData(700.0)]
        [InlineData(273.0)]
        [InlineData(0.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void SaturationPressure_OutsideDomain_IsReported(double tK)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Physics.SaturationPressureBar(tK));
            double p;
            Assert.False(Physics.TrySaturationPressureBar(tK, out p));
            Assert.True(double.IsNaN(p));
        }

        [Theory]
        [InlineData(500.0)]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        [InlineData(double.NegativeInfinity)]
        public void SaturationTemp_OutsideDomain_IsReported(double pBar)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Physics.SaturationTempK(pBar));
            double t;
            Assert.False(Physics.TrySaturationTempK(pBar, out t));
            Assert.True(double.IsNaN(t));
        }

        [Fact]
        public void Saturation_DomainEndpoints_StillAnswer()
        {
            Assert.Equal(Physics.CriticalPressureBar, Physics.SaturationPressureBar(Physics.CriticalTempK), 3);
            Assert.True(Physics.SaturationPressureBar(Physics.TripleTempK) > 0.006);
            Assert.Equal(Physics.CriticalTempK, Physics.SaturationTempK(Physics.CriticalPressureBar), 3);
        }

        [Fact]
        public void PlantThermalBounds_StayInsideTheSaturationDomain()
        {
            // The plant's hard bounds (T 20..365 C, p 1..200 bar, secondary 1..90 bar) are inside the IF97
            // domain, so Thermal never feeds the strict functions an out-of-range value.
            double x;
            Assert.True(Physics.TrySaturationPressureBar(20.0 + 273.15, out x));
            Assert.True(Physics.TrySaturationPressureBar(365.0 + 273.15, out x));
            Assert.True(Physics.TrySaturationTempK(1.0, out x));
            Assert.True(Physics.TrySaturationTempK(200.0, out x));
            var t = new ThermalHydraulics();
            t.RestoreState(2000, 1000, 5000, 1, 1, 500, 0, 0.6, 1, 0.5, 0, false, 0);
            Assert.True(Physics.IsFinite(t.SecondaryTempC) && Physics.IsFinite(t.PrimarySatMarginC));
        }
    }

    public class ScramLatchTests
    {
        private static Plant Scrammed()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            p.Scram();
            return p;
        }

        [Fact]
        public void SetAllRods_WhileScrammed_DoesNotWithdraw()
        {
            var p = Scrammed();
            p.SetAllRods(0.0);
            Assert.All(p.Banks, b => Assert.Equal(1.0, b.InsertionTarget));
            for (int i = 0; i < 10; i++) p.Step(5.0);
            Assert.All(p.Banks, b => Assert.Equal(1.0, b.Insertion, 9));
            Assert.True(p.Scrammed);
        }

        [Fact]
        public void SetAllRods_StillValidatesInput_WhileScrammed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Scrammed().SetAllRods(double.NaN));
        }

        [Fact]
        public void ResetScram_ReleasesTheLatch()
        {
            var p = Scrammed();
            p.ResetScram();
            p.SetAllRods(0.25);
            Assert.All(p.Banks, b => Assert.Equal(0.25, b.InsertionTarget));
            Assert.All(p.Banks, b => Assert.Equal(Physics.RodDriveSpeedPerSec, b.SpeedPerSec));
        }

        [Fact]
        public void Intents_WhileScrammed_CannotWithdraw()
        {
            var p = Scrammed();
            p.Apply(PlantIntent.SetAllRods(0.0));
            p.Apply(new PlantIntent(IntentKind.RodBankTarget, "A", 0.0, "local"));
            Assert.All(p.Banks, b => Assert.Equal(1.0, b.InsertionTarget));
        }

        [Fact]
        public void Latch_SurvivesSnapshotRoundTrip()
        {
            var p = Scrammed();
            var restored = new Plant();
            restored.RestoreState(PlantSnapshot.FromJson(p.CaptureState().ToJson()));
            Assert.True(restored.Scrammed);
            restored.SetAllRods(0.0);
            Assert.All(restored.Banks, b => Assert.Equal(1.0, b.InsertionTarget));
        }
    }

    public class ReactorProtectionTests
    {
        private static void HotFuel(Plant p)
        {
            p.Thermal.RestoreState(Physics.TripHighFuelTempC + 50.0, 305.0, 152.0, 1.0, 1.0, 60.0, 0.96, 0.6, 1.0, 0.75, 0.0, false, 0.0);
        }

        [Fact]
        public void LossOfSink_AtPower_TripsBeforeAnyModelBound()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            p.Thermal.SteamDemandFrac = 0.0;
            p.Thermal.PrimaryFlowFrac = 0.0;
            double tpMax = 0, tfMax = 0;
            for (int i = 0; i < 600; i++)
            {
                p.Step(5.0);
                tpMax = Math.Max(tpMax, p.Thermal.PrimaryTempC);
                tfMax = Math.Max(tfMax, p.Thermal.FuelTempC);
            }
            Assert.True(p.Scrammed);
            Assert.Equal("high primary temperature", p.LastScramCause);
            Assert.True(tpMax < Physics.PrimaryTempMaxC - 1.0, "primary max " + tpMax);
            Assert.True(tfMax < Physics.FuelTempMaxC - 1.0, "fuel max " + tfMax);
            Assert.All(p.Banks, b => Assert.Equal(1.0, b.Insertion, 6));
            Assert.True(p.Core.Power < 0.05);
        }

        [Fact]
        public void HighFuelTemperature_Trips()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            HotFuel(p);
            p.Step(0.2);
            Assert.True(p.Scrammed);
            Assert.Equal("high fuel temperature", p.LastScramCause);
        }

        [Fact]
        public void NormalFullPower_DoesNotTrip()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            for (int i = 0; i < 120; i++) p.Step(5.0);
            Assert.False(p.Scrammed);
            Assert.Null(p.LastScramCause);
        }

        [Fact]
        public void ManualScram_RecordsOperatorCause()
        {
            var p = new Plant();
            p.Scram();
            Assert.Equal("operator", p.LastScramCause);
        }

        [Fact]
        public void ProtectiveTrip_StaysLatched_AgainstWithdrawal()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            HotFuel(p);
            p.Step(0.2);
            Assert.True(p.Scrammed);
            p.SetAllRods(0.0);
            p.Step(5.0);
            Assert.True(p.Scrammed);
            Assert.All(p.Banks, b => Assert.Equal(1.0, b.InsertionTarget));
        }
    }
}
