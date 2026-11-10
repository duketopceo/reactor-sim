using ReactorSim.Ui;
using Xunit;

namespace ReactorSim.Tests
{
    // Playtest-2 U5 (R14): one formatter attaches the unit and fixed decimals. Pure, runs on Linux.
    public class ReadoutFormatTests
    {
        [Fact]
        public void Adaptive_CapsDecimalsByMagnitude_AndTrimsZeros()
        {
            Assert.Equal("-7250", ReadoutFormat.Adaptive(-7250.26));
            Assert.Equal("123.5", ReadoutFormat.Adaptive(123.46));
            Assert.Equal("1.5", ReadoutFormat.Adaptive(1.5));
            Assert.Equal("0.25", ReadoutFormat.Adaptive(0.25));
            Assert.Equal("0", ReadoutFormat.Adaptive(-0.0));
            Assert.Equal("-1e-4", ReadoutFormat.Adaptive(-0.0001));
            Assert.Equal("--", ReadoutFormat.Adaptive(double.NaN));
            Assert.Equal("--", ReadoutFormat.Adaptive(double.PositiveInfinity));
            Assert.Equal("1000", ReadoutFormat.Adaptive(999.96));
            Assert.Equal("10", ReadoutFormat.Adaptive(9.996));
            Assert.Equal("2.5e-5", ReadoutFormat.Adaptive(0.000025));
            Assert.Equal("1e+7", ReadoutFormat.Adaptive(1e7));
        }

        [Fact]
        public void Number_IsInvariantUnderACommaDecimalCulture()
        {
            var saved = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = ReactorSim.Core.Tests.CaptureMathTests.CommaCulture();
                Assert.Equal("1234.5", ReadoutFormat.Number(1234.5, 1));
                Assert.Equal("-0.25", ReadoutFormat.Number(-0.25, 2));
                Assert.Equal("155.0 bar", ReadoutFormat.PressureBar(155.0));
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = saved; }
        }

        [Fact]
        public void Units_AttachWithFixedDecimals()
        {
            Assert.Equal("155.0 bar", ReadoutFormat.PressureBar(155.0));
            Assert.Equal("42%", ReadoutFormat.Fraction(0.42));
            Assert.Equal("42.5%", ReadoutFormat.Fraction(0.425, 1));
            Assert.Equal("302.3 °C", ReadoutFormat.TemperatureC(302.26));
            Assert.Equal("1200 ppm", ReadoutFormat.BoronPpm(1200.2));
            Assert.Equal("3600 rpm", ReadoutFormat.TurbineRpm(3600.4));
            Assert.Equal("80 MWt", ReadoutFormat.PowerMWt(79.6));
            Assert.Equal("49.9 MWe", ReadoutFormat.PowerMWe(49.94));
            Assert.Equal("50.00 Hz", ReadoutFormat.FrequencyHz(50.0));
            Assert.Equal("-567 pcm", ReadoutFormat.ReactivityPcm(-567.2));
        }

        [Fact]
        public void DecimalsAreFixed_NotTrimmed()
        {
            Assert.Equal("3.0 bar", ReadoutFormat.Value(3.0, 1, ReadoutFormat.Bar));
            Assert.Equal("0.00 Hz", ReadoutFormat.Value(0.0, ReadoutFormat.Hertz));
            Assert.Equal("7", ReadoutFormat.Number(7.0, 0));
        }

        [Fact]
        public void NaNAndInfinity_RenderAsDash_AndNoteSaysWhy()
        {
            Assert.Equal("--", ReadoutFormat.Value(double.NaN, 1, "bar"));
            Assert.Equal("--", ReadoutFormat.Value(double.PositiveInfinity, 1, "bar"));
            Assert.Equal("--", ReadoutFormat.Number(double.NegativeInfinity, 2));
            Assert.Equal("no reading", ReadoutFormat.Note(double.NaN));
            Assert.Equal("off scale", ReadoutFormat.Note(double.PositiveInfinity));
            Assert.Equal("", ReadoutFormat.Note(1.0));
            Assert.Equal("-- (no reading)", ReadoutFormat.ValueNoted(double.NaN, 1, "bar"));
            Assert.Equal("-- (off scale)", ReadoutFormat.ValueNoted(double.NegativeInfinity, 1, "bar"));
        }

        [Fact]
        public void NegativeValues_KeepSignAndUnit_ButNeverNegativeZero()
        {
            Assert.Equal("-12.5 °C", ReadoutFormat.TemperatureC(-12.5));
            Assert.Equal("0.0 bar", ReadoutFormat.PressureBar(-0.04));
            Assert.Equal("0 pcm", ReadoutFormat.ReactivityPcm(-0.3));
            Assert.Equal("-5%", ReadoutFormat.Fraction(-0.05));
        }

        [Fact]
        public void Delta_IsSigned_AndZeroIsUnsigned()
        {
            Assert.Equal("+3.2 bar", ReadoutFormat.Delta(3.2, 1, "bar"));
            Assert.Equal("-1.0 bar", ReadoutFormat.Delta(-1.0, 1, "bar"));
            Assert.Equal("0.0 bar", ReadoutFormat.Delta(0.0, 1, "bar"));
            Assert.Equal("0.0 bar", ReadoutFormat.Delta(0.01, 1, "bar"));
            Assert.Equal("--", ReadoutFormat.Delta(double.NaN, 1, "bar"));
            Assert.Equal("+4%", ReadoutFormat.Delta(4.0, 0, "%"));
        }

        [Fact]
        public void Output_IsCultureInvariant()
        {
            var old = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.Equal("1.5 bar", ReadoutFormat.Value(1.5, 1, "bar"));
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = old; }
        }

        [Fact]
        public void DecimalsFor_KnownUnitsAndFallback()
        {
            Assert.Equal(0, ReadoutFormat.DecimalsFor("ppm"));
            Assert.Equal(2, ReadoutFormat.DecimalsFor("Hz"));
            Assert.Equal(1, ReadoutFormat.DecimalsFor("furlongs"));
            Assert.Equal(1, ReadoutFormat.DecimalsFor(null));
        }
    }
}
