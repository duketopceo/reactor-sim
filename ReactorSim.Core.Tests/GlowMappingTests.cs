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
    }
}
