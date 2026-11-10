using System;

// Pure lighting maths (no UnityEngine) for the reactor hall, so "no blown highlights, no black walls, surfaces tell apart" is a
// number checked on any box. It estimates the display value (0..1, before tone mapping) of a diffuse surface lit by one point light
// the way Unity's built-in forward renderer does to first order: albedo (linear) x light intensity x distance falloff, plus emission.
// It is an estimate for choosing colours and intensities; the render is the final judge.
namespace ReactorSim
{
    public static class HallExposure
    {
        /// <summary>Highest estimated display value allowed for a lit surface (above this it reads as a blown highlight).</summary>
        public const double BlownCeiling = 0.92;
        /// <summary>Lowest estimated display value allowed for a hall surface that should still read (above a black wall).</summary>
        public const double BlackFloor = 0.05;
        /// <summary>Smallest ratio between the display values of two surfaces that must tell apart (for example water against steel).</summary>
        public const double MinContrast = 1.5;

        /// <summary>sRGB channel (0..1) to linear.</summary>
        public static double ToLinear(double c)
        {
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        /// <summary>Linear 0..1 to sRGB.</summary>
        public static double ToSrgb(double c)
        {
            c = Math.Max(0.0, c);
            return c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
        }

        /// <summary>Relative luminance (Rec. 709 weights) of an sRGB colour, linear 0..1.</summary>
        public static double Luminance(double r, double g, double b)
        {
            return 0.2126 * ToLinear(r) + 0.7152 * ToLinear(g) + 0.0722 * ToLinear(b);
        }

        /// <summary>Point-light falloff: close to 1/(1+25x^2) for x = distance / range, faded to zero at the range.</summary>
        public static double PointFalloff(double distance, double range)
        {
            if (range <= 0.0) return 0.0;
            double x = distance / range;
            if (x >= 1.0) return 0.0;
            return (1.0 / (1.0 + 25.0 * x * x)) * (1.0 - x * x);
        }

        /// <summary>Display value (sRGB, 0..1) of a linear total, clamped the way an 8-bit target without tone mapping clamps it.</summary>
        public static double Display(double linearTotal)
        {
            return Math.Min(1.0, ToSrgb(Math.Min(1.0, linearTotal)));
        }

        /// <summary>Ratio of the lighter to the darker of two display values (at least 1).</summary>
        public static double Contrast(double a, double b)
        {
            double hi = Math.Max(a, b), lo = Math.Max(1e-4, Math.Min(a, b));
            return hi / lo;
        }
    }

    /// <summary>
    /// The hall's colours and light settings (sRGB 0..1 triples, metres), shared by HallBuilder and the tests. Warm high-bay lamps over
    /// cool water and rack glow; concrete and deck in warm greys, stainless and painted steel separated in value and hue.
    /// </summary>
    public static class HallPalette
    {
        public static readonly float[] Concrete = { 0.50f, 0.47f, 0.43f };
        public static readonly float[] Deck = { 0.36f, 0.35f, 0.34f };
        public static readonly float[] PoolTile = { 0.42f, 0.60f, 0.64f };
        public static readonly float[] Stainless = { 0.80f, 0.82f, 0.84f };
        public static readonly float[] ContainmentPaint = { 0.66f, 0.64f, 0.58f };
        public static readonly float[] CranePaint = { 0.86f, 0.62f, 0.10f };
        public static readonly float[] DarkSteel = { 0.26f, 0.28f, 0.31f };
        public static readonly float[] HighBayColour = { 1.00f, 0.88f, 0.72f };
        public const float HighBayIntensity = 1.1f;
        public const float HighBayRange = 34f;
        public const float HighBayHeightAboveDeck = 6.0f;
        /// <summary>Linear emission added to concrete so a far wall never goes black under a falling-off light.</summary>
        public const float ConcreteEmission = 0.04f;
        public static readonly float[] ShallowWater = { 0.05f, 0.42f, 0.52f };
        public static readonly float[] DeepWater = { 0.01f, 0.12f, 0.30f };

        public static double AlbedoLuminance(float[] c) { return HallExposure.Luminance(c[0], c[1], c[2]); }
        public static double HighBayLuminance() { return HallExposure.Luminance(HighBayColour[0], HighBayColour[1], HighBayColour[2]); }
    }
}
