using System;
using System.Collections.Generic;
using System.Globalization;

// Pure (no UnityEngine): the numerals printed on a circular dial (minimum, middle, maximum) and its unit label, so a
// value can be read off the scale. The dial sweep itself is GaugeGeometry.NeedleDeg.
namespace ReactorSim.Instruments
{
    public struct ScaleMark
    {
        public float Frac;      // 0 = minimum, 1 = maximum, along the needle sweep
        public string Text;
        public ScaleMark(float frac, string text) { Frac = frac; Text = text; }
    }

    public static class GaugeScale
    {
        /// <summary>The unit as printed on a dial: fractions read as percent, "degC" as the degree sign.</summary>
        public static string UnitLabel(string units)
        {
            if (string.IsNullOrEmpty(units)) return "";
            switch (units)
            {
                case "frac": return "%";
                case "degC": return "°C";
                case "deg": return "°";
                default: return units;
            }
        }

        /// <summary>Where a numeral stands, as (angle from "up" in degrees, radius as a share of the face width). The middle
        /// numeral keeps the needle's own angle (the needle only crosses it mid-scale); the end numerals sit past the sweep
        /// ends, low in the bottom gap, so the needle at rest never covers the minimum.</summary>
        public static void NumeralSpot(float frac, float sweepMinDeg, float sweepMaxDeg, out float angleDeg, out float radiusFrac)
        {
            const float EndPushDeg = 28f;
            if (frac <= 0f) { angleDeg = sweepMinDeg - EndPushDeg; radiusFrac = 0.32f; }
            else if (frac >= 1f) { angleDeg = sweepMaxDeg + EndPushDeg; radiusFrac = 0.32f; }
            else { angleDeg = sweepMinDeg + (sweepMaxDeg - sweepMinDeg) * frac; radiusFrac = 0.235f; }
        }

        /// <summary>Three marks: minimum, middle, maximum. Fractions are shown 0 to 100.</summary>
        public static IList<ScaleMark> Marks(double min, double max, string units)
        {
            double scale = units == "frac" ? 100.0 : 1.0;
            double lo = min * scale, hi = max * scale, mid = (lo + hi) / 2.0;
            return new[] { new ScaleMark(0f, Tick(lo, hi - lo)), new ScaleMark(0.5f, Tick(mid, hi - lo)), new ScaleMark(1f, Tick(hi, hi - lo)) };
        }

        /// <summary>A compact numeral: whole numbers for wide scales, a decimal or two for narrow ones, k and M for big ones.</summary>
        public static string Tick(double v, double span)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "--";
            double a = Math.Abs(v);
            if (a >= 1e6) return Trim(v / 1e6) + "M";
            if (a >= 1e3) return Trim(v / 1e3) + "k";
            int decimals = Math.Abs(span) >= 10.0 ? 0 : (Math.Abs(span) >= 1.0 ? 1 : 2);
            string s = v.ToString("F" + decimals, CultureInfo.InvariantCulture);
            return s.Trim('-', '0', '.').Length == 0 ? s.TrimStart('-') : s;
        }

        private static string Trim(double v)
        {
            return v.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
