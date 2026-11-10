using System;

// Pure logic (no UnityEngine): needle/bar smoothing between the last two sim snapshots.
namespace ReactorSim.Instruments
{
    public static class NeedleMath
    {
        // Blend from the previous tick's value to the latest one by the clock's fraction toward the next tick.
        // NaN on either side falls back to the other so a missing sample never freezes the needle.
        public static double Interpolate(double previous, double current, double fraction)
        {
            if (double.IsNaN(previous)) return current;
            if (double.IsNaN(current)) return previous;
            if (double.IsNaN(fraction) || fraction < 0.0) fraction = 0.0;
            if (fraction > 1.0) fraction = 1.0;
            return previous + (current - previous) * fraction;
        }

        public static double Fraction(double value, double min, double max)
        {
            if (!(max > min) || double.IsNaN(value)) return 0.0;
            double f = (value - min) / (max - min);
            return f < 0.0 ? 0.0 : (f > 1.0 ? 1.0 : f);
        }
    }
}
