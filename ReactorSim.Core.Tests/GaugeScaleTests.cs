using System.Linq;
using ReactorSim.Instruments;
using Xunit;

namespace ReactorSim.Core.Tests
{
    public class GaugeScaleTests
    {
        // Every end numeral a dial can print, from the inventory's ranges (k and M notation, decimals, signs).
        private static readonly string[] Samples =
            { "0", "100", "200", "-750", "-2.5k", "1k", "4.0", "10.0", "-3k", "-1.5k", "-8k", "-999", "999", "700", "1.4k", "6.5", "8.5", "320M", "30k", "-4k" };
        private static readonly string[] Units = { "MWt", "pcm", "\u00b0C", "%", "pH", "s", "bar", "ppm", "kV", "Hz" };

        [Fact]
        public void EndNumerals_AreSeparate_ClearOfTheUnit_AndOfTheNeedleAtItsEndStops()
        {
            foreach (var lo in Samples)
                foreach (var hi in Samples)
                {
                    var a = GaugeScale.EndNumeralBox(lo, false);
                    var b = GaugeScale.EndNumeralBox(hi, true);
                    Assert.False(GaugeScale.Overlaps(a, b), lo + " vs " + hi);
                    Assert.True(a.X < 0f && b.X > 0f);
                    foreach (var box in new[] { a, b })
                    {
                        Assert.False(GaugeScale.NeedleTouches(box, -135f), box.W + " covered by the needle at its minimum");
                        Assert.False(GaugeScale.NeedleTouches(box, 135f), box.W + " covered by the needle at its maximum");
                        Assert.True(System.Math.Abs(box.X) + box.W / 2f <= 0.44f && System.Math.Abs(box.Y) + box.H / 2f <= 0.45f, "inside the face");
                    }
                    foreach (var unit in Units)
                    {
                        var u = GaugeScale.UnitBox(unit);
                        Assert.False(GaugeScale.Overlaps(a, u) || GaugeScale.Overlaps(b, u), lo + "/" + hi + " vs unit " + unit);
                    }
                }
        }

        [Fact]
        public void NeedleTouches_DetectsAnOverlapOnTheRestLine()
        {
            // a box on the needle's own line at the minimum must be reported (the original defect)
            var onLine = new GaugeScale.FaceBox(-0.166f, -0.166f, 0.1f, 0.085f);
            Assert.True(GaugeScale.NeedleTouches(onLine, -135f));
            Assert.False(GaugeScale.NeedleTouches(onLine, 135f));
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
