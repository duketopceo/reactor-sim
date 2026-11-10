using System;

// Pure logic (no UnityEngine) behind HallVisibility: extracts the six frustum planes from a view-projection matrix and
// tests an axis-aligned box against them. Linked into ReactorSim.Core.Tests.
namespace ReactorSim
{
    public static class HallVisibilityMath
    {
        /// <summary>
        /// Six inward-facing, normalised planes (a, b, c, d per plane, 24 numbers: left, right, bottom, top, near, far) from a
        /// row-major 4x4 view-projection matrix that maps to GL-style clip space (-w..w on every axis). A point is inside a
        /// plane when a*x + b*y + c*z + d is at least 0.
        /// </summary>
        public static double[] PlanesFromViewProjection(double[] m)
        {
            if (m == null || m.Length != 16) throw new ArgumentException("a 4x4 matrix needs 16 numbers");
            var p = new double[24];
            for (int i = 0; i < 6; i++)
            {
                int axis = i / 2;                        // 0 = x, 1 = y, 2 = z
                double sign = (i % 2 == 0) ? 1.0 : -1.0; // left/bottom/near add, right/top/far subtract
                double a = m[12] + sign * m[axis * 4 + 0];
                double b = m[13] + sign * m[axis * 4 + 1];
                double c = m[14] + sign * m[axis * 4 + 2];
                double d = m[15] + sign * m[axis * 4 + 3];
                double len = Math.Sqrt(a * a + b * b + c * c);
                if (len < 1e-12) len = 1.0;
                p[i * 4 + 0] = a / len;
                p[i * 4 + 1] = b / len;
                p[i * 4 + 2] = c / len;
                p[i * 4 + 3] = d / len;
            }
            return p;
        }

        /// <summary>True when any part of the box can be inside the frustum (a conservative test: it never hides a visible box).</summary>
        public static bool AabbVisible(double[] planes, double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            if (planes == null || planes.Length != 24) return true;   // no information: stay visible
            for (int i = 0; i < 6; i++)
            {
                double a = planes[i * 4], b = planes[i * 4 + 1], c = planes[i * 4 + 2], d = planes[i * 4 + 3];
                // the box corner furthest along the plane normal; if even that is behind the plane, the box is outside
                double x = a >= 0.0 ? maxX : minX;
                double y = b >= 0.0 ? maxY : minY;
                double z = c >= 0.0 ? maxZ : minZ;
                if (a * x + b * y + c * z + d < 0.0) return false;
            }
            return true;
        }
    }
}
