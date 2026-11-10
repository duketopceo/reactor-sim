using System;
using Xunit;
using ReactorSim.Instruments;

namespace ReactorSim.Core.Tests
{
    // Pure power -> Cherenkov glow mapping (U6). Source lives under ReactorSim.Unity/Assets/Scripts/View3D and is linked in.
    public class GlowMappingTests
    {
        [Fact]
        public void ZeroPowerIsTheFloorAndFullPowerIsTheMax()
        {
            Assert.Equal(GlowMapping.Floor, GlowMapping.Glow(0.0), 12);
            Assert.Equal(GlowMapping.Max, GlowMapping.Glow(1.0), 12);
            Assert.True(GlowMapping.Floor > 0.0, "a shut-down core is dim, not pitch black");
            Assert.True(GlowMapping.Floor < GlowMapping.Max);
        }

        [Fact]
        public void IsMonotonicInPower()
        {
            double prev = GlowMapping.Glow(0.0);
            for (int i = 1; i <= 1000; i++)
            {
                double g = GlowMapping.Glow(i / 1000.0);
                Assert.True(g >= prev, "glow dropped at power " + i / 1000.0);
                prev = g;
            }
            Assert.True(GlowMapping.Glow(0.01) > GlowMapping.Glow(0.0));
        }

        [Fact]
        public void NeverBelowFloorOrAboveMax()
        {
            foreach (double p in new[] { -5.0, -1e-9, 0.0, 0.3, 1.0, 1.0000001, 7.0, double.PositiveInfinity, double.NegativeInfinity })
            {
                double g = GlowMapping.Glow(p);
                Assert.True(g >= GlowMapping.Floor && g <= GlowMapping.Max, p + " -> " + g);
            }
        }

        [Fact]
        public void NonFiniteAndOutOfRangeInputsAreClamped()
        {
            Assert.Equal(GlowMapping.Floor, GlowMapping.Glow(double.NaN), 12);
            Assert.Equal(GlowMapping.Floor, GlowMapping.Glow(-3.0), 12);
            Assert.Equal(GlowMapping.Max, GlowMapping.Glow(2.5), 12);
            Assert.False(double.IsNaN(GlowMapping.Glow(double.NaN)));
            Assert.False(double.IsInfinity(GlowMapping.Glow(double.PositiveInfinity)));
        }

        // ---- rack glow (U7) ----

        [Fact]
        public void RackEmissionAtZeroPowerIsTheNonzeroFloor()
        {
            Assert.Equal(GlowMapping.RackEmissionFloor, GlowMapping.RackEmission(0.0), 12);
            Assert.True(GlowMapping.RackEmission(0.0) > 0.0, "an idle plant still shows a dim cue");
            Assert.Equal(GlowMapping.RackEmissionFloor, GlowMapping.RackEmission(double.NaN), 12);
            Assert.Equal(GlowMapping.RackEmissionFloor, GlowMapping.RackEmission(-4.0), 12);
        }

        [Fact]
        public void RackEmissionRisesMonotonicallyAndSaturatesAtOne()
        {
            double prev = GlowMapping.RackEmission(0.0);
            for (int i = 1; i <= 1000; i++)
            {
                double e = GlowMapping.RackEmission(i / 1000.0);
                Assert.True(e >= prev, "rack emission dropped at fraction " + i / 1000.0);
                prev = e;
            }
            Assert.Equal(GlowMapping.RackEmissionMax, GlowMapping.RackEmission(1.0), 12);
            Assert.Equal(GlowMapping.RackEmissionMax, GlowMapping.RackEmission(3.0), 12);
            Assert.Equal(GlowMapping.RackEmissionMax, GlowMapping.RackEmission(double.PositiveInfinity), 12);
        }

        [Fact]
        public void FullPowerRackEmissionIsAtLeastFourTimesTheFloor()
        {
            Assert.True(GlowMapping.RackEmission(1.0) >= 4.0 * GlowMapping.RackEmission(0.0));
        }

        [Fact]
        public void GlowTakesAFractionSoADifferentRatedPowerGivesTheSameOutput()
        {
            // 600 MW of 1200 MW rated and 150 MW of 300 MW rated are the same fraction, so the same glow
            double a = 600.0 / 1200.0, b = 150.0 / 300.0;
            Assert.Equal(GlowMapping.RackEmission(a), GlowMapping.RackEmission(b), 12);
            Assert.Equal(GlowMapping.Haze(a), GlowMapping.Haze(b), 12);
            Assert.Equal(GlowMapping.LightScale(a), GlowMapping.LightScale(b), 12);
        }

        [Fact]
        public void HazeAndLightScaleStayInRangeAndRise()
        {
            Assert.Equal(GlowMapping.HazeFloor, GlowMapping.Haze(0.0), 12);
            Assert.Equal(GlowMapping.HazeMax, GlowMapping.Haze(1.0), 12);
            Assert.True(GlowMapping.Haze(0.3) > GlowMapping.Haze(0.0) && GlowMapping.Haze(0.3) < GlowMapping.Haze(1.0));
            Assert.InRange(GlowMapping.LightScale(0.0), 0.15, 1.5);
            Assert.InRange(GlowMapping.LightScale(1.0), 0.15, 1.5);
            Assert.True(GlowMapping.LightScale(1.0) > GlowMapping.LightScale(0.0));
        }

        [Fact]
        public void LevelIsZeroAtTheFloorAndOneAtTheMax()
        {
            Assert.Equal(0.0, GlowMapping.Level(0.0), 12);
            Assert.Equal(1.0, GlowMapping.Level(1.0), 12);
            Assert.InRange(GlowMapping.Level(0.25), 0.0, 1.0);
        }
    }
}
