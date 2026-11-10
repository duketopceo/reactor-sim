// Pure logic (no UnityEngine): the snapshot-array helpers shared by GaugeSources, LampPredicates and ControlValues.
// Every helper returns NaN for a null or empty array (or an out-of-range index). Linked into ReactorSim.Core.Tests.
namespace ReactorSim.Instruments
{
    internal static class SnapshotMath
    {
        /// <summary>a[i], or NaN when the array is null or i is out of range.</summary>
        public static double Elem(double[] a, int i)
        {
            return a != null && i >= 0 && i < a.Length ? a[i] : double.NaN;
        }

        public static double Min(double[] a)
        {
            if (a == null || a.Length == 0) return double.NaN;
            double m = a[0];
            for (int i = 1; i < a.Length; i++) if (a[i] < m) m = a[i];
            return m;
        }

        public static double Mean(double[] a)
        {
            if (a == null || a.Length == 0) return double.NaN;
            double t = 0.0;
            for (int i = 0; i < a.Length; i++) t += a[i];
            return t / a.Length;
        }
    }
}
