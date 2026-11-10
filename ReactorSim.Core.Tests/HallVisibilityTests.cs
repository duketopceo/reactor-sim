using System;
using Xunit;

namespace ReactorSim.Core.Tests
{
    // Frustum gate maths (KTD9): the hall root is on when the window opening is inside the camera frustum, off when it is not.
    // The camera here is built by hand: at the origin looking down +z with a 90 degree field of view, square aspect, near 0.1,
    // far 100, in GL clip space, so every expected answer can be worked out on paper.
    public class HallVisibilityTests
    {
        private static double[] Projection(double near, double far)
        {
            // row-major perspective, f = 1 / tan(45 deg) = 1, looking down +z (clip w = z)
            double a = (far + near) / (far - near), b = -2.0 * far * near / (far - near);
            return new double[]
            {
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, a, b,
                0, 0, 1, 0,
            };
        }

        private static double[] Planes() { return HallVisibilityMath.PlanesFromViewProjection(Projection(0.1, 100.0)); }

        [Fact]
        public void BoxInFrontOfTheCameraIsVisible()
        {
            Assert.True(HallVisibilityMath.AabbVisible(Planes(), -1, -1, 5, 1, 1, 7));
        }

        [Fact]
        public void BoxBehindTheCameraIsHidden()
        {
            Assert.False(HallVisibilityMath.AabbVisible(Planes(), -1, -1, -7, 1, 1, -5));
        }

        [Fact]
        public void BoxFarToTheSideIsHiddenAndANearbyOneIsNot()
        {
            Assert.False(HallVisibilityMath.AabbVisible(Planes(), 20, -1, 5, 22, 1, 7));   // 90 degree fov: |x| > z is outside
            Assert.True(HallVisibilityMath.AabbVisible(Planes(), 3, -1, 5, 4, 1, 7));
        }

        [Fact]
        public void BoxBeyondTheFarPlaneIsHidden()
        {
            Assert.False(HallVisibilityMath.AabbVisible(Planes(), -1, -1, 150, 1, 1, 160));
        }

        [Fact]
        public void BoxStraddlingAnEdgeIsVisible()
        {
            // crosses the left plane (x = -z) at z = 5: part of it is inside
            Assert.True(HallVisibilityMath.AabbVisible(Planes(), -8, -1, 5, -4, 1, 6));
        }

        [Fact]
        public void BoxAboveAndBelowTheViewIsHidden()
        {
            Assert.False(HallVisibilityMath.AabbVisible(Planes(), -1, 30, 5, 1, 32, 7));
            Assert.False(HallVisibilityMath.AabbVisible(Planes(), -1, -32, 5, 1, -30, 7));
        }

        [Fact]
        public void MissingPlanesLeaveTheHallOn()
        {
            Assert.True(HallVisibilityMath.AabbVisible(null, 0, 0, 0, 1, 1, 1));
            Assert.True(HallVisibilityMath.AabbVisible(new double[3], 0, 0, 0, 1, 1, 1));
        }

        [Fact]
        public void BadMatrixSizeIsRejected()
        {
            Assert.Throws<ArgumentException>(() => HallVisibilityMath.PlanesFromViewProjection(new double[9]));
            Assert.Throws<ArgumentException>(() => HallVisibilityMath.PlanesFromViewProjection(null));
        }

        [Fact]
        public void PlanesAreNormalised()
        {
            double[] p = Planes();
            for (int i = 0; i < 6; i++)
            {
                double len = Math.Sqrt(p[i * 4] * p[i * 4] + p[i * 4 + 1] * p[i * 4 + 1] + p[i * 4 + 2] * p[i * 4 + 2]);
                Assert.Equal(1.0, len, 9);
            }
        }
    }
}
