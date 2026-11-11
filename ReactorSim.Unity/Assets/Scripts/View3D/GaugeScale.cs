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

        // ---- circular dial layout, in shares of the face width with the hub at the origin, +Y up, +X right ----

        public const float MidFontFrac = 0.085f, EndFontFrac = 0.09f, UnitFontFrac = 0.07f;
        public const float MidRadiusFrac = 0.235f;      // the middle numeral's distance above the hub
        public const float UnitDropFrac = 0.17f;        // the unit label's distance below the hub
        public const float EndCenterY = -0.40f;        // the end numerals' baseline row, under the end ticks
        public const float EndMinCenterX = 0.25f;       // end numeral centres sit at least this far left and right of the hub
        public const float EndGapFrac = 0.03f;          // clear space between the two end numerals, at least
        public const float CharAdvanceFrac = 0.6f;      // monospace advance as a share of the font size
        public const float NeedleRestLengthFrac = 0.44f, NeedleHalfWidthFrac = 0.03f;   // pessimistic needle (tip beyond the ticks)

        /// <summary>An axis-aligned text box: centre and size.</summary>
        public struct FaceBox
        {
            public float X, Y, W, H;
            public FaceBox(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
        }

        public static FaceBox TextBox(string text, float cx, float cy, float fontFrac)
        {
            int n = text == null ? 0 : text.Length;
            return new FaceBox(cx, cy, n * CharAdvanceFrac * fontFrac, fontFrac);
        }

        /// <summary>The minimum (left) or maximum (right) numeral: low in the bottom gap under the end ticks, well below where the
        /// needle's tip rests at its end stop, and never closer to the hub axis than the other end numeral.</summary>
        public static FaceBox EndNumeralBox(string text, bool isMax)
        {
            float w = (text == null ? 0 : text.Length) * CharAdvanceFrac * EndFontFrac;
            float cx = Math.Max(EndMinCenterX, w / 2f + EndGapFrac / 2f);
            return TextBox(text, isMax ? cx : -cx, EndCenterY, EndFontFrac);
        }

        public static FaceBox UnitBox(string unit)
        {
            return TextBox(unit, 0f, -UnitDropFrac, UnitFontFrac);
        }

        public static bool Overlaps(FaceBox a, FaceBox b)
        {
            return Math.Abs(a.X - b.X) < (a.W + b.W) / 2f && Math.Abs(a.Y - b.Y) < (a.H + b.H) / 2f;
        }

        /// <summary>True when the needle at the given angle from up (degrees, positive to the right) touches the box.</summary>
        public static bool NeedleTouches(FaceBox box, float angleDeg)
        {
            double rad = angleDeg * Math.PI / 180.0;
            for (float t = 0f; t <= NeedleRestLengthFrac; t += 0.005f)
            {
                float px = (float)Math.Sin(rad) * t, py = (float)Math.Cos(rad) * t;
                if (Math.Abs(px - box.X) <= box.W / 2f + NeedleHalfWidthFrac && Math.Abs(py - box.Y) <= box.H / 2f + NeedleHalfWidthFrac)
                    return true;
            }
            return false;
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
