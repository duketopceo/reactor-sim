using System.Linq;
using ReactorSim.Instruments;
using Xunit;

namespace ReactorSim.Core.Tests
{
    public class GaugeScaleTests
    {
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
            Assert.Equal(new[] { "-8000", "-3500", "1000" }, GaugeScale.Marks(-8000, 1000, "pcm").Select(x => x.Text).ToArray());
        }

        [Fact]
        public void BigValues_UseKAndM_AndNeverPrintNegativeZero()
        {
            Assert.Equal("320M", GaugeScale.Tick(3.2e8, 3.2e8));
            Assert.Equal("30k", GaugeScale.Tick(30000, 30000));
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
