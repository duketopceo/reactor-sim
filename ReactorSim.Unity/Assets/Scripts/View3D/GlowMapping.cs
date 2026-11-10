using System;

// Pure logic (no UnityEngine): normalised reactor power -> Cherenkov glow level for the reactor hall.
// Square-root curve so low power already reads, with a dim floor so a shut-down plant is not pitch black.
// The inputs are power FRACTIONS (1.0 = rated), never watts, so a change to the rated power flows through untouched.
// The spent-fuel rack glow follows plant power as a labelled visual cue: real spent-fuel Cherenkov light comes from fission
// products decaying, not from current reactor power, so this scaling is for the player's benefit, not a physics claim.
namespace ReactorSim.Instruments
{
    public static class GlowMapping
    {
        /// <summary>Glow level at zero (or invalid) power.</summary>
        public const double Floor = 0.08;
        /// <summary>Glow level at rated power and above.</summary>
        public const double Max = 1.0;

        /// <summary>Rack emission multiplier at zero power (a visible, dim cue).</summary>
        public const double RackEmissionFloor = 0.25;
        /// <summary>Rack emission multiplier at rated power and above.</summary>
        public const double RackEmissionMax = 2.6;
        /// <summary>Haze cone opacity at zero power.</summary>
        public const double HazeFloor = 0.02;
        /// <summary>Haze cone opacity at rated power and above.</summary>
        public const double HazeMax = 0.22;

        /// <summary>Power is normalised (1.0 = rated). Result is always finite and within [Floor, Max].</summary>
        public static double Glow(double power)
        {
            if (double.IsNaN(power) || power <= 0.0) return Floor;
            if (power >= 1.0) return Max;
            return Floor + (Max - Floor) * Math.Sqrt(power);
        }

        /// <summary>The glow rescaled so the floor is 0 and the maximum is 1.</summary>
        public static double Level(double power)
        {
            return (Glow(power) - Floor) / (Max - Floor);
        }

        /// <summary>Emission multiplier for the rack cells: RackEmissionFloor at zero power up to RackEmissionMax at rated power.</summary>
        public static double RackEmission(double powerFraction)
        {
            return RackEmissionFloor + (RackEmissionMax - RackEmissionFloor) * Level(powerFraction);
        }

        /// <summary>Opacity of the water-haze cones above the racks.</summary>
        public static double Haze(double powerFraction)
        {
            return HazeFloor + (HazeMax - HazeFloor) * Level(powerFraction);
        }

        /// <summary>Scale (0.15 to 1.5) applied to a rack point light's base intensity; the light count is capped by the builder.</summary>
        public static double LightScale(double powerFraction)
        {
            return 0.15 + 1.35 * Glow(powerFraction);
        }
    }
}
