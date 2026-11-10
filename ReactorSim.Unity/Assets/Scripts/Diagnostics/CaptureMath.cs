using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Pure logic (no UnityEngine) behind the capture mode and the input telemetry: pixel projection of a screen's text,
// contrast measured on rendered pixels, capture argument parsing, frame-time percentiles and the input log line format.
// Linked into ReactorSim.Core.Tests. The thresholds that grade a capture live in scripts/check-captures.sh, not here.
namespace ReactorSim
{
    public static class CaptureMath
    {
        /// <summary>Glyph height as a multiple of the character cell width of a monospace face (cell width is about 0.6 em, cap height about 0.7 em).</summary>
        public const double GlyphToCellRatio = 0.7 / 0.6;

        /// <summary>Pixels per metre on a face at distance (m), for a vertical field of view (degrees) and window height (px).</summary>
        public static double PixelsPerMetre(double windowHeightPx, double verticalFovDeg, double distanceM)
        {
            double half = Math.Tan(verticalFovDeg * Math.PI / 360.0);
            if (!(windowHeightPx > 0.0) || !(distanceM > 0.0) || !(half > 0.0)) return 0.0;
            return windowHeightPx / (2.0 * distanceM * half);
        }

        /// <summary>On-screen width (px) of a character cell when `columns` fixed-width columns span a screen of widthM metres.</summary>
        public static double CellWidthPx(double screenWidthM, int columns, double windowHeightPx, double verticalFovDeg, double distanceM)
        {
            if (columns <= 0) return 0.0;
            return screenWidthM * PixelsPerMetre(windowHeightPx, verticalFovDeg, distanceM) / columns;
        }

        /// <summary>Projected glyph (cap) height in pixels for the same set-up.</summary>
        public static double GlyphHeightPx(double screenWidthM, int columns, double windowHeightPx, double verticalFovDeg, double distanceM)
        {
            return CellWidthPx(screenWidthM, columns, windowHeightPx, verticalFovDeg, distanceM) * GlyphToCellRatio;
        }

        // ---- contrast measured on the rendered pixels ----

        /// <summary>WCAG 2 relative luminance of an sRGB colour given as 0..255 channels.</summary>
        public static double Luminance(byte r, byte g, byte b)
        {
            return 0.2126 * Lin(r / 255.0) + 0.7152 * Lin(g / 255.0) + 0.0722 * Lin(b / 255.0);
        }

        private static double Lin(double c)
        {
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        /// <summary>
        /// Contrast ratio inside a crop of an RGBA32 image (4 bytes per pixel, rows of imageWidth pixels): brightest 5 percent of the
        /// pixels against the darkest 5 percent, so antialiasing and a stray pixel do not decide it. 1 for a flat crop.
        /// </summary>
        public static double CropContrast(byte[] rgba, int imageWidth, int x, int y, int w, int h)
        {
            if (rgba == null || imageWidth <= 0 || w <= 0 || h <= 0 || x < 0 || y < 0 || x + w > imageWidth) return 1.0;
            int rows = rgba.Length / 4 / imageWidth;
            if (y + h > rows) return 1.0;
            var lum = new List<double>(w * h);
            for (int j = y; j < y + h; j++)
                for (int i = x; i < x + w; i++)
                {
                    int p = (j * imageWidth + i) * 4;
                    lum.Add(Luminance(rgba[p], rgba[p + 1], rgba[p + 2]));
                }
            lum.Sort();
            int n = lum.Count;
            int k = Math.Max(1, n / 20);
            double lo = 0.0, hi = 0.0;
            for (int i = 0; i < k; i++) { lo += lum[i]; hi += lum[n - 1 - i]; }
            lo /= k; hi /= k;
            return (hi + 0.05) / (lo + 0.05);
        }

        // ---- frame-time percentiles ----

        /// <summary>Nearest-rank percentile (p in 0..100) of the values; 0 for an empty set.</summary>
        public static double Percentile(IList<double> values, double p)
        {
            if (values == null || values.Count == 0) return 0.0;
            var sorted = new List<double>(values);
            sorted.Sort();
            double q = Math.Min(100.0, Math.Max(0.0, p));
            int rank = (int)Math.Ceiling(q / 100.0 * sorted.Count);
            return sorted[Math.Min(sorted.Count - 1, Math.Max(0, rank - 1))];
        }

        // ---- capture arguments ----

        /// <summary>Poses the capture run can show, in the default order. "crt" expands to every CRT screen.</summary>
        public static readonly string[] KnownPoses =
        {
            "menu", "seat", "stand", "crt", "tooltip", "rejected", "drag", "checklist", "pause", "tablet", "clipboard", "gallery", "away"
        };

        /// <summary>Parses "1600x900". Throws FormatException with the offending text on anything else.</summary>
        public static void ParseSize(string text, out int width, out int height)
        {
            width = height = 0;
            string[] parts = (text ?? "").Split('x');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out width)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out height)
                || width < 320 || height < 200 || width > 7680 || height > 4320)
                throw new FormatException("capture size must look like 1600x900 (320x200 up to 7680x4320), got \"" + text + "\"");
        }

        /// <summary>Parses a comma-separated pose list. Throws FormatException naming an unknown pose and listing the known ones.</summary>
        public static List<string> ParsePoses(string text)
        {
            var list = new List<string>();
            foreach (string raw in (text ?? "").Split(','))
            {
                string name = raw.Trim().ToLowerInvariant();
                if (name.Length == 0) continue;
                if (Array.IndexOf(KnownPoses, name) < 0)
                    throw new FormatException("unknown capture pose \"" + name + "\"; known: " + string.Join(", ", KnownPoses));
                list.Add(name);
            }
            if (list.Count == 0) throw new FormatException("capture pose list is empty; known: " + string.Join(", ", KnownPoses));
            return list;
        }
    }

    /// <summary>One parsed line of the input log.</summary>
    public sealed class InputLogEvent
    {
        public double Time;
        public string Name = "";
        public readonly Dictionary<string, string> Fields = new Dictionary<string, string>();
    }

    /// <summary>
    /// The input telemetry line format (one JSON object per line: "t" seconds, "ev" event name, then string fields). The writer is
    /// used by InputLog in the player; the parser exists so tests prove every lifecycle line reads back.
    /// </summary>
    public static class InputLogFormat
    {
        public const string Hover = "hover", Press = "press", PressRejected = "press_rejected", DragStart = "drag_start",
            DragMove = "drag_move", DragCommit = "drag_commit", DragCancel = "drag_cancel", Walk = "walk";

        /// <summary>Formats a number the way the log writes it (invariant, up to 4 decimals).</summary>
        public static string Num(double v)
        {
            return double.IsNaN(v) || double.IsInfinity(v) ? "nan" : v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>keyValues alternate key, value, key, value.</summary>
        public static string Format(double time, string name, params string[] keyValues)
        {
            var sb = new StringBuilder(96);
            sb.Append("{\"t\":").Append(time.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append(",\"ev\":\"").Append(Esc(name)).Append('"');
            for (int i = 0; i + 1 < (keyValues == null ? 0 : keyValues.Length); i += 2)
                sb.Append(",\"").Append(Esc(keyValues[i])).Append("\":\"").Append(Esc(keyValues[i + 1])).Append('"');
            sb.Append('}');
            return sb.ToString();
        }

        public static bool TryParse(string line, out InputLogEvent e)
        {
            e = null;
            if (string.IsNullOrEmpty(line)) return false;
            int i = 0;
            SkipSpace(line, ref i);
            if (i >= line.Length || line[i] != '{') return false;
            i++;
            var ev = new InputLogEvent();
            bool hasT = false, hasEv = false;
            while (true)
            {
                SkipSpace(line, ref i);
                if (i < line.Length && line[i] == '}') break;
                string key;
                if (!ReadString(line, ref i, out key)) return false;
                SkipSpace(line, ref i);
                if (i >= line.Length || line[i] != ':') return false;
                i++;
                SkipSpace(line, ref i);
                if (i < line.Length && line[i] == '"')
                {
                    string val;
                    if (!ReadString(line, ref i, out val)) return false;
                    if (key == "ev") { ev.Name = val; hasEv = true; } else ev.Fields[key] = val;
                }
                else
                {
                    int start = i;
                    while (i < line.Length && line[i] != ',' && line[i] != '}') i++;
                    double num;
                    if (key != "t" || !double.TryParse(line.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out num)) return false;
                    ev.Time = num;
                    hasT = true;
                }
                SkipSpace(line, ref i);
                if (i < line.Length && line[i] == ',') { i++; continue; }
                if (i < line.Length && line[i] == '}') break;
                return false;
            }
            if (!hasT || !hasEv) return false;
            e = ev;
            return true;
        }

        private static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static bool ReadString(string s, ref int i, out string value)
        {
            value = null;
            if (i >= s.Length || s[i] != '"') return false;
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') { value = sb.ToString(); return true; }
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) return false;
                char n = s[i++];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        int code;
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) return false;
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: sb.Append(n); break;
                }
            }
            return false;
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
