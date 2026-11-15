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
        public const float EndCenterY = -0.395f;       // the end numerals' baseline row, under the end ticks
        public const float EndMinCenterX = 0.25f;       // end numeral centres sit at least this far left and right of the hub
        public const float EndGapFrac = 0.03f;          // clear space between the two end numerals, at least
        public const float CharAdvanceFrac = 0.6f;      // monospace advance as a share of the font size
        public const float NeedleRestLengthFrac = 0.44f, NeedleHalfWidthFrac = 0.03f;   // pessimistic needle (tip beyond the ticks)

        // The kit's bezel frame: its clear inner half-width as a share of the face width. Same number as
        // design/control-room/kits.json circular_bezel_inner_frac (GaugeScaleTests pins them together), which the Blender
        // builder turns into the rails. Every numeral must stay inside it by at least BezelClearanceFrac.
        public const float BezelInnerHalfFrac = 0.4545f;
        public const float BezelClearanceFrac = 0.012f;

        /// <summary>The middle numeral: above the hub at the needle's own angle for its fraction (straight up at 0.5).</summary>
        public static FaceBox MidNumeralBox(string text)
        {
            return TextBox(text, 0f, MidRadiusFrac, MidFontFrac);
        }

        /// <summary>True when the box lies inside the bezel frame with the clearance to spare.</summary>
        public static bool InsideBezel(FaceBox b)
        {
            float lim = BezelInnerHalfFrac - BezelClearanceFrac;
            return Math.Abs(b.X) + b.W / 2f <= lim && Math.Abs(b.Y) + b.H / 2f <= lim;
        }

        // ---- edgewise bars (K02), in metres on the kit face ----
        public const float BarFaceWidthM = 0.14f, BarFaceHeightM = 0.34f;
        public const float BarEndGapM = 0.022f, BarEndInsetM = 0.03f;
        public const float BarStripInnerHalfWidthM = 0.050f;   // inside the side rails
        public const float BarStripInnerHalfHeightM = 0.148f;  // inside the end caps

        /// <summary>Where the minimum and maximum numerals stand on a bar: heights above the pointer's centre of travel
        /// (<see cref="Lo"/>, <see cref="Hi"/>), a shift toward the face's left (<see cref="Side"/>, metres, positive = left), and the font size.</summary>
        public struct BarLayout
        {
            public float Lo, Hi, Side, Size;
        }

        public static BarLayout BarLabels(float travelM)
        {
            float limit = BarFaceHeightM * 0.5f - 0.02f;
            float lo = -travelM * 0.5f - BarEndGapM, hi = travelM * 0.5f + BarEndGapM, side = 0f;
            // A long strip leaves no room past its ends: the numerals then stand inside the strip, a little in from its
            // ends so the limit markers there do not sit on them, and beside the pointer's travel.
            if (lo < -limit || hi > limit)
            {
                lo = Math.Max(-limit, lo) + BarEndInsetM;
                hi = Math.Min(limit, hi) - BarEndInsetM;
                side = BarFaceWidthM * 0.14f;
            }
            return new BarLayout { Lo = lo, Hi = hi, Side = side, Size = BarFaceWidthM * 0.15f };
        }

        /// <summary>Metres, centred on the kit face centre (X across, Y up): the numeral's box.</summary>
        public static FaceBox BarNumeralBox(string text, float yM, BarLayout layout)
        {
            return TextBox(text, -layout.Side, yM, layout.Size);
        }

        /// <summary>The longest numeral (characters, at most 6) that still fits between the strip's rails with a 4 mm margin.</summary>
        public static int BarMaxChars(BarLayout layout)
        {
            float room = 2f * (BarStripInnerHalfWidthM - 0.004f - layout.Side);
            return Math.Min(6, (int)Math.Floor(room / (CharAdvanceFrac * layout.Size) + 1e-4f));
        }

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
