using System.Linq;
using ReactorSim.Instruments;
using Xunit;

namespace ReactorSim.Core.Tests
{
    public class GaugeScaleTests
    {
        [Fact]
        public void EndNumerals_SitPastTheSweepEnds_SoTheNeedleNeverCoversThem()
        {
            float a, r;
            GaugeScale.NumeralSpot(0f, -135f, 135f, out a, out r);
            Assert.Equal(-157f, a);
            Assert.Equal(0.30f, r);
            float lo = a;
            GaugeScale.NumeralSpot(1f, -135f, 135f, out a, out r);
            Assert.True(a > 135f + 20f);
            Assert.Equal(-lo, a);
            GaugeScale.NumeralSpot(0.5f, -135f, 135f, out a, out r);
            Assert.Equal(0f, a);
            Assert.Equal(0.235f, r);
            GaugeScale.NumeralSpot(0.25f, -135f, 135f, out a, out r);
            Assert.Equal(-67.5f, a);
        }

        [Fact]
        public void Marks_AreMinMiddleMax_AlongTheSweep()
        {
            var m = GaugeScale.Marks(0.0, 200.0, "MWt");
            Assert.Equal(new[] { 0f, 0.5f, 1f }, m.Select(x => x.Frac).ToArray());
            Assert.Equal(new[] { "0", "100", "200" }, m.Select(x => x.Text).ToArray());
        }

        [Fact]
        public void Fractions_ReadAsPercent()
        {
            var m = GaugeScale.Marks(0.0, 1.0, "frac");
            Assert.Equal(new[] { "0", "50", "100" }, m.Select(x => x.Text).ToArray());
            Assert.Equal("%", GaugeScale.UnitLabel("frac"));
        }

        [Fact]
        public void NarrowScales_KeepADecimal_AndNegativeMinimumsKeepTheirSign()
        {
            Assert.Equal(new[] { "6.5", "7.5", "8.5" }, GaugeScale.Marks(6.5, 8.5, "pH").Select(x => x.Text).ToArray());
            Assert.Equal(new[] { "-8k", "-3.5k", "1k" }, GaugeScale.Marks(-8000, 1000, "pcm").Select(x => x.Text).ToArray());
        }

        [Fact]
        public void BigValues_UseKAndM_AndNeverPrintNegativeZero()
        {
            Assert.Equal("320M", GaugeScale.Tick(3.2e8, 3.2e8));
            Assert.Equal("30k", GaugeScale.Tick(30000, 30000));
            Assert.Equal("2.5k", GaugeScale.Tick(2500, 2500));
            Assert.Equal("999", GaugeScale.Tick(999, 2500));
            Assert.Equal("0", GaugeScale.Tick(-0.0001, 200));
            Assert.Equal("--", GaugeScale.Tick(double.NaN, 1));
        }

        [Fact]
        public void UnitLabels_AreShortAndPrintable()
        {
            Assert.Equal("°C", GaugeScale.UnitLabel("degC"));
            Assert.Equal("bar", GaugeScale.UnitLabel("bar"));
            Assert.Equal("", GaugeScale.UnitLabel(null));
        }
    }
}
