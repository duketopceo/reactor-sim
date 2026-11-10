using System;
using System.Collections.Generic;
using Xunit;
using Row = ReactorSim.TelemetryRecorder.TelemetryRow;

namespace ReactorSim.Tests
{
    public class PhysicsTests
    {
        // IAPWS-IF97 saturation verification values (T in K -> p in bar).
        [Theory]
        [InlineData(300.0, 0.03536589413)]
        [InlineData(373.15, 1.014179779)]
        [InlineData(500.0, 26.38897756)]
        [InlineData(600.0, 123.4431458)]
        public void SaturationPressure_MatchesIf97(double tK, double pBar)
        {
            Assert.InRange(Physics.SaturationPressureBar(tK), pBar * (1 - 1e-6), pBar * (1 + 1e-6));
        }

        [Theory]
        [InlineData(0.1)]
        [InlineData(1.0)]
        [InlineData(60.0)]
        [InlineData(155.0)]
        public void SaturationTemp_InvertsSaturationPressure(double pBar)
        {
            Assert.InRange(Physics.SaturationPressureBar(Physics.SaturationTempK(pBar)), pBar * (1 - 1e-6), pBar * (1 + 1e-6));
        }

        [Fact]
        public void Saturation_OutsideDomain_StaysFinite_ClampedToCriticalPoint()
        {
            Assert.Equal(Physics.CriticalPressureBar, Physics.SaturationPressureBar(900.0), 6);
            Assert.Equal(Physics.CriticalTempK, Physics.SaturationTempK(1000.0), 6);
            Assert.True(Physics.IsFinite(Physics.SaturationPressureBar(double.NaN)));
        }

        [Fact]
        public void Clamp_MapsNaNToLowerBound()
        {
            // Documented and the reason rod commands are rejected at the boundary.
            Assert.Equal(0.0, Physics.Clamp(double.NaN, 0.0, 1.0));
            Assert.Equal(1.0, Physics.Clamp(7.0, 0.0, 1.0));
        }

        [Fact]
        public void PlantStep_RejectsBadDt_WithoutMutating()
        {
            var p = new Plant();
            string before = p.CaptureState().ToJson();
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Step(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Step(double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Step(-1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Step(Plant.MaxStepSeconds + 1.0));
            Assert.Equal(before, p.CaptureState().ToJson());
        }

        [Fact]
        public void TotalHeat_IsPromptFractionPlusDecay()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            Assert.Equal(Physics.PromptHeatFraction * p.Core.Power + p.Core.DecayHeatPower, p.Core.TotalHeatPower, 12);
        }

        [Fact]
        public void Scram_DrivesRodsIn_AndPowerFalls()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            p.Scram();
            for (int i = 0; i < 20; i++) p.Step(6.0);
            Assert.All(p.Banks, b => Assert.Equal(1.0, b.Insertion, 6));
            Assert.True(p.Core.Power < 0.05);
        }
    }

    public class GoldenCompareTests
    {
        private static List<Row> Rows(int n)
        {
            var rows = new List<Row>();
            for (int i = 0; i < n; i++)
                rows.Add(new Row { Tick = i, GameMinutes = i * 0.1, PowerFraction = 0.5, ReactivityPcm = 1, FuelTempC = 300,
                                   PrimaryTempC = 290, PrimaryPressureBar = 150, SgPressureBar = 60, XenonConc = 1, BoronPpm = 800 });
            return rows;
        }

        [Fact]
        public void Identical_Passes()
        {
            Assert.True(TelemetryRecorder.VerifyAgainstGolden(Rows(5), Rows(5), 1e-6, out _));
        }

        [Fact]
        public void RowCountMismatch_Fails()
        {
            Assert.False(TelemetryRecorder.VerifyAgainstGolden(Rows(4), Rows(5), 1e-6, out var msg));
            Assert.Contains("Row count", msg);
        }

        [Fact]
        public void TickMismatch_Fails()
        {
            var a = Rows(3); var r = a[1]; r.Tick = 99; a[1] = r;
            Assert.False(TelemetryRecorder.VerifyAgainstGolden(a, Rows(3), 1e-6, out _));
        }

        public static TheoryData<string> Columns() => new TheoryData<string>
        {
            "GameMinutes", "PowerFraction", "ReactivityPcm", "FuelTempC", "PrimaryTempC",
            "PrimaryPressureBar", "SgPressureBar", "XenonConc", "BoronPpm",
        };

        [Theory]
        [MemberData(nameof(Columns))]
        public void DivergenceInAnyColumn_Fails(string column)
        {
            var a = Rows(3); object boxed = a[2];
            var f = typeof(Row).GetField(column);
            f.SetValue(boxed, (double)f.GetValue(boxed) + 50.0);
            a[2] = (Row)boxed;
            Assert.False(TelemetryRecorder.VerifyAgainstGolden(a, Rows(3), 1e-6, out _), column + " divergence was ignored");
        }

        [Fact]
        public void Csv_RoundTrips()
        {
            var r = Rows(1)[0];
            Assert.Equal(r.ToCsv(), Row.FromCsv(r.ToCsv()).ToCsv());
        }
    }
}
