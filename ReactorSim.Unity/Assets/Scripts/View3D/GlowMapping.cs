using System;

// Pure logic (no UnityEngine): normalised reactor power -> Cherenkov glow level for the reactor hall (U6).
// Square-root curve so low power already reads, with a dim floor so a shut-down core is not pitch black.
namespace ReactorSim.Instruments
{
    public static class GlowMapping
    {
        /// <summary>Glow level at zero (or invalid) power.</summary>
        public const double Floor = 0.08;
        /// <summary>Glow level at rated power and above.</summary>
        public const double Max = 1.0;

        /// <summary>Power is normalised (1.0 = rated). Result is always finite and within [Floor, Max].</summary>
        public static double Glow(double power)
        {
            if (double.IsNaN(power) || power <= 0.0) return Floor;
            if (power >= 1.0) return Max;
            return Floor + (Max - Floor) * Math.Sqrt(power);
        }
    }
}
