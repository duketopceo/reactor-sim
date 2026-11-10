using Xunit;

namespace ReactorSim.Core.Tests
{
    // Hall lighting and colour choices (PR4): nothing blown out, no black wall, neighbouring surfaces tell apart. The numbers are a
    // first-order estimate (see HallExposure); the render is the final judge, these keep the choices from drifting.
    public class HallExposureTests
    {
        // distance from a surface to each of the two high-bay lamps (metres), measured from the builder's light positions
        private struct Sample { public string name; public float[] albedo; public double d1, d2; public double emission; }

        private static Sample[] Samples()
        {
            return new[]
            {
                new Sample { name = "stainless vessel head under the lamps", albedo = HallPalette.Stainless, d1 = 7.0, d2 = 9.0 },
                new Sample { name = "containment paint", albedo = HallPalette.ContainmentPaint, d1 = 8.0, d2 = 9.5 },
                new Sample { name = "crane girder in safety yellow", albedo = HallPalette.CranePaint, d1 = 6.0, d2 = 9.0 },
                new Sample { name = "hall deck near the window", albedo = HallPalette.Deck, d1 = 11.0, d2 = 13.0 },
                new Sample { name = "far concrete wall", albedo = HallPalette.Concrete, d1 = 15.0, d2 = 15.0, emission = HallPalette.ConcreteEmission },
                new Sample { name = "pool tile at the rack ledge", albedo = HallPalette.PoolTile, d1 = 16.0, d2 = 17.0 },
            };
        }

        private static double Linear(Sample s)
        {
            double k = HallPalette.HighBayLuminance();
            double a = HallExposure.Luminance(s.albedo[0], s.albedo[1], s.albedo[2]);
            return a * HallPalette.HighBayIntensity * k * (HallExposure.PointFalloff(s.d1, HallPalette.HighBayRange) + HallExposure.PointFalloff(s.d2, HallPalette.HighBayRange)) + s.emission;
        }

        [Fact]
        public void NoSampleSurfaceIsBlownOut()
        {
            double ceiling = HallExposure.ToLinear(HallExposure.BlownCeiling);
            foreach (Sample s in Samples()) Assert.True(Linear(s) <= ceiling, $"{s.name}: linear {Linear(s):0.000} above {ceiling:0.000}");
        }

        [Fact]
        public void NoSampleSurfaceIsBlack()
        {
            double floor = HallExposure.ToLinear(HallExposure.BlackFloor);
            foreach (Sample s in Samples()) Assert.True(Linear(s) >= floor, $"{s.name}: linear {Linear(s):0.000} below {floor:0.000}");
        }

        [Fact]
        public void FarWallNeedsItsEmissionFloorUnderTheFallingOffLamps()
        {
            var wall = Samples()[4];
            double without = wall.emission;
            wall.emission = 0.0;
            Assert.True(Linear(wall) < Linear(Samples()[4]));
            Assert.True(without > 0.0);
        }

        [Fact]
        public void WarmLampsAreWarmAndTheWaterIsCool()
        {
            Assert.True(HallPalette.HighBayColour[0] > HallPalette.HighBayColour[1] && HallPalette.HighBayColour[1] > HallPalette.HighBayColour[2]);
            Assert.True(HallPalette.ShallowWater[2] > HallPalette.ShallowWater[0] && HallPalette.DeepWater[2] > HallPalette.DeepWater[0]);
            Assert.True(HallPalette.AlbedoLuminance(HallPalette.DeepWater) < HallPalette.AlbedoLuminance(HallPalette.ShallowWater));
        }

        [Fact]
        public void SurfacesThatMeetTellApartInValue()
        {
            var s = Samples();
            double stainless = HallExposure.Display(Linear(s[0])), paint = HallExposure.Display(Linear(s[1])), yellow = HallExposure.Display(Linear(s[2]));
            double deck = HallExposure.Display(Linear(s[3])), wall = HallExposure.Display(Linear(s[4]));
            Assert.True(HallExposure.Contrast(stainless, deck) >= HallExposure.MinContrast, "stainless against deck");
            Assert.True(HallExposure.Contrast(paint, wall) >= 1.2, "containment against wall");
            Assert.True(HallExposure.Contrast(yellow, wall) >= 1.2, "crane against wall");
            // the water reads as a different colour family from steel, not only darker: blue-dominant against near-neutral
            Assert.True(HallPalette.ShallowWater[2] - HallPalette.ShallowWater[0] > 0.3f);
            Assert.True(System.Math.Abs(HallPalette.Stainless[2] - HallPalette.Stainless[0]) < 0.1f);
        }

        [Fact]
        public void FalloffIsMonotoneAndZeroAtRange()
        {
            Assert.Equal(1.0, HallExposure.PointFalloff(0.0, 10.0), 6);
            Assert.True(HallExposure.PointFalloff(2.0, 10.0) > HallExposure.PointFalloff(5.0, 10.0));
            Assert.Equal(0.0, HallExposure.PointFalloff(10.0, 10.0), 6);
            Assert.Equal(0.0, HallExposure.PointFalloff(4.0, 0.0), 6);
        }

        [Fact]
        public void SrgbRoundTripsAndLuminanceOfWhiteIsOne()
        {
            Assert.Equal(0.5, HallExposure.ToSrgb(HallExposure.ToLinear(0.5)), 6);
            Assert.Equal(1.0, HallExposure.Luminance(1, 1, 1), 4);
        }
    }
}
