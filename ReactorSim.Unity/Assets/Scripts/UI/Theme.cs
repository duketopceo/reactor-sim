using System;
using System.Collections.Generic;

// Pure (no UnityEngine): the one fixed UI palette, its font sizes and the WCAG contrast math.
// ThemeColors adapts it to UnityEngine.Color. Linked into ReactorSim.Core.Tests, where
// ThemeContrastTests measures every declared pair. Values are 0..1 sRGB.
namespace ReactorSim.Ui
{
    /// <summary>Plain RGBA value, no engine type.</summary>
    public struct Rgba : IEquatable<Rgba>
    {
        public double R, G, B, A;
        public Rgba(double r, double g, double b, double a = 1.0) { R = r; G = g; B = b; A = a; }
        public bool Equals(Rgba o) { return R == o.R && G == o.G && B == o.B && A == o.A; }
        public override bool Equals(object o) { return o is Rgba r && Equals(r); }
        public override int GetHashCode() { return (R.GetHashCode() * 31 + G.GetHashCode()) * 31 + B.GetHashCode() ^ A.GetHashCode(); }
    }

    /// <summary>Body text needs 4.5:1. Large text and UI component boundaries need 3:1.</summary>
    public enum PairKind { Body, Large, Boundary }

    /// <summary>One legal foreground-on-background combination, by token name.</summary>
    public sealed class ContrastPair
    {
        public string Name;
        public string Fg;
        public string Bg;
        public PairKind Kind;
        public int FontSize;       // pt; 0 for boundaries
        public bool Bold;
        public bool OverScene;     // drawn over the 3D scene, so Bg must be a backing token
    }

    /// <summary>The type ramp roles. Numeric is the monospace face for values with units.</summary>
    public enum TextRole { Display, Title, Heading, Body, Caption, Numeric }

    public static class Theme
    {
        // ---- surfaces ----
        public static readonly Rgba SurfaceScreen = new Rgba(0.03, 0.07, 0.09);
        public static readonly Rgba SurfacePanel = new Rgba(0.07, 0.11, 0.14);
        public static readonly Rgba SurfaceRow = new Rgba(0.09, 0.15, 0.19);
        public static readonly Rgba SurfacePaper = new Rgba(0.93, 0.92, 0.86);
        public static readonly Rgba SurfaceTooltip = new Rgba(0.02, 0.04, 0.06, 0.94);
        public static readonly Rgba SurfaceHeader = new Rgba(0.11, 0.19, 0.25);      // header band of a panel

        // ---- text roles ----
        public static readonly Rgba TextBody = new Rgba(0.90, 0.96, 1.00);
        public static readonly Rgba TextDim = new Rgba(0.68, 0.78, 0.85);
        public static readonly Rgba TextHeading = new Rgba(0.97, 0.99, 1.00);
        public static readonly Rgba TextInkOnPaper = new Rgba(0.10, 0.10, 0.12);
        public static readonly Rgba TextPaperCurrent = new Rgba(0.50, 0.20, 0.00);
        public static readonly Rgba TextPaperDone = new Rgba(0.15, 0.32, 0.15);

        // ---- status (always shown with a glyph or label where they carry meaning) ----
        // Original scene-emission cues, not instrument alarm colours or blackbody radiation.
        public static readonly Rgba CoreGlowCold = new Rgba(0.13, 0.35, 0.85);
        public static readonly Rgba CoreGlowHot = new Rgba(0.24, 0.72, 1.00);
        public static readonly Rgba StatusNormal = new Rgba(0.55, 0.85, 1.00);
        public static readonly Rgba StatusOk = new Rgba(0.60, 1.00, 0.80);
        public static readonly Rgba StatusCaution = new Rgba(1.00, 0.75, 0.25);
        public static readonly Rgba StatusCritical = new Rgba(1.00, 0.50, 0.45);

        // ---- buttons and boundaries ----
        public static readonly Rgba ButtonOff = new Rgba(0.16, 0.22, 0.28);
        public static readonly Rgba ButtonOn = new Rgba(0.18, 0.40, 0.52);
        public static readonly Rgba ButtonBorder = new Rgba(0.45, 0.60, 0.70);
        public static readonly Rgba FillCritical = new Rgba(0.55, 0.14, 0.14);       // progress fill that is behind target; text sits on it
        public static readonly Rgba ButtonHover = new Rgba(0.22, 0.38, 0.50);
        public static readonly Rgba ButtonPressed = new Rgba(0.14, 0.30, 0.40);
        public static readonly Rgba FocusRing = new Rgba(0.95, 0.85, 0.45);          // keyboard/gamepad focus outline on a control
        public static readonly Rgba Divider = new Rgba(0.30, 0.42, 0.50);            // hairline between panel sections; decorative

        // ---- scene-level (not text surfaces) ----
        public static readonly Rgba ScrimDim = new Rgba(0.0, 0.0, 0.0, 0.6);          // dims the scene behind a menu panel
        public static readonly Rgba CameraBackground = new Rgba(0.05, 0.07, 0.09);    // solid clear color behind the scene
        public static readonly Rgba DeviceBezel = new Rgba(0.04, 0.045, 0.05);        // tablet body around its screen; no text on it
        public static readonly Rgba HighlightPulse = new Rgba(1.0, 0.82, 0.10);       // "Show me" outline; decorative, no text on it

        // ---- CRT ----
        public static readonly Rgba CrtPhosphor = new Rgba(0.45, 1.00, 0.60);

        // ---- font sizes (pt) ----
        public const int FontCaption = 16;
        public const int FontBody = 20;
        public const int FontHeading = 26;
        public const int FontNumeric = 28;         // monospace readouts (values with units)
        public const int FontTitle = 36;           // panel and page titles
        public const int FontDisplay = 56;         // main-menu wordmark, pause banner
        public const int LargeTextMin = 18;        // WCAG large: 18 pt or more
        public const int LargeBoldTextMin = 14;    // or 14 pt bold or more
        public const int MinSceneFontSize = 16;    // smallest text drawn over the 3D scene

        // ---- type ramp (display, title, heading, body, caption, numeric), ascending by role ----
        public static readonly IReadOnlyList<int> TypeRamp = new[] { FontCaption, FontBody, FontHeading, FontNumeric, FontTitle, FontDisplay };

        public static int RampSize(TextRole role)
        {
            switch (role)
            {
                case TextRole.Display: return FontDisplay;
                case TextRole.Title: return FontTitle;
                case TextRole.Heading: return FontHeading;
                case TextRole.Numeric: return FontNumeric;
                case TextRole.Caption: return FontCaption;
                default: return FontBody;
            }
        }

        // ---- spacing scale (reference pixels): 4-8-12-16-24 ----
        public const int Space1 = 4;
        public const int Space2 = 8;
        public const int Space3 = 12;
        public const int Space4 = 16;
        public const int Space5 = 24;
        public static readonly IReadOnlyList<int> SpacingScale = new[] { Space1, Space2, Space3, Space4, Space5 };

        // ---- uGUI canvas: scales with screen HEIGHT from this reference; text scale multiplies the ramp, clamped ----
        public const int ReferenceWidth = 1920;
        public const int ReferenceHeight = 1080;
        public const double TextScaleFloor = 0.75;
        public const double TextScaleCeiling = 1.5;
        public const double PanelMaxWidthFraction = 0.94;   // a panel never exceeds this share of the canvas width
        public const double PanelMaxHeightFraction = 0.96;  // nor this share of the canvas height (it shrinks to fit)

        /// <summary>Uniform scale (at most 1) that keeps content of the given height inside the canvas height.</summary>
        public static double FitScale(double contentHeight)
        {
            double limit = ReferenceHeight * PanelMaxHeightFraction;
            return contentHeight <= limit || contentHeight <= 0.0 ? 1.0 : limit / contentHeight;
        }

        /// <summary>A ramp size (reference px) times the user text scale, scale clamped, at least 8.</summary>
        public static int ScaledFont(int size, double textScale)
        {
            double k = Math.Max(TextScaleFloor, Math.Min(TextScaleCeiling, double.IsNaN(textScale) ? 1.0 : textScale));
            return Math.Max(8, (int)Math.Round(size * k));
        }

        /// <summary>Panel width in reference px: the design width grown with text scale, kept on screen.</summary>
        public static double PanelWidth(double designWidth, double textScale, double canvasWidth)
        {
            double k = Math.Max(TextScaleFloor, Math.Min(TextScaleCeiling, double.IsNaN(textScale) ? 1.0 : textScale));
            return Math.Min(designWidth * k, canvasWidth * PanelMaxWidthFraction);
        }

        /// <summary>Canvas width in reference px for a screen aspect, with the canvas scaled by height.</summary>
        public static double CanvasWidth(double screenWidthPx, double screenHeightPx)
        {
            return screenHeightPx <= 0.0 ? ReferenceWidth : ReferenceHeight * (screenWidthPx / screenHeightPx);
        }

        public static readonly IReadOnlyDictionary<string, Rgba> Tokens = new Dictionary<string, Rgba>
        {
            { "SurfaceScreen", SurfaceScreen }, { "SurfacePanel", SurfacePanel }, { "SurfaceRow", SurfaceRow },
            { "SurfacePaper", SurfacePaper }, { "SurfaceTooltip", SurfaceTooltip }, { "SurfaceHeader", SurfaceHeader },
            { "TextBody", TextBody }, { "TextDim", TextDim }, { "TextHeading", TextHeading },
            { "TextInkOnPaper", TextInkOnPaper }, { "TextPaperCurrent", TextPaperCurrent }, { "TextPaperDone", TextPaperDone },
            { "StatusNormal", StatusNormal }, { "StatusOk", StatusOk },
            { "StatusCaution", StatusCaution }, { "StatusCritical", StatusCritical },
            { "ButtonOff", ButtonOff }, { "ButtonOn", ButtonOn }, { "ButtonBorder", ButtonBorder },
            { "FillCritical", FillCritical }, { "ButtonHover", ButtonHover }, { "ButtonPressed", ButtonPressed },
            { "FocusRing", FocusRing }, { "Divider", Divider },
            { "ScrimDim", ScrimDim }, { "CameraBackground", CameraBackground }, { "HighlightPulse", HighlightPulse }, { "DeviceBezel", DeviceBezel },
            { "CrtPhosphor", CrtPhosphor },
        };

        public static Rgba Token(string name) { return Tokens[name]; }

        public static readonly IReadOnlyList<ContrastPair> Pairs = BuildPairs();

        private static List<ContrastPair> BuildPairs()
        {
            var l = new List<ContrastPair>();
            foreach (var bg in new[] { "SurfaceScreen", "SurfacePanel", "SurfaceRow" })
                foreach (var fg in new[] { "TextBody", "TextDim", "StatusNormal", "StatusOk", "StatusCaution", "StatusCritical" })
                    l.Add(Pair(fg + " on " + bg, fg, bg, PairKind.Body, FontBody));
            l.Add(Pair("CRT phosphor", "CrtPhosphor", "SurfaceScreen", PairKind.Body, FontBody));
            l.Add(Pair("Heading on panel", "TextHeading", "SurfacePanel", PairKind.Large, FontHeading));
            l.Add(Pair("Heading on screen", "TextHeading", "SurfaceScreen", PairKind.Large, FontHeading));
            l.Add(Pair("Button off", "TextBody", "ButtonOff", PairKind.Body, FontBody));
            l.Add(Pair("Button on", "TextBody", "ButtonOn", PairKind.Body, FontBody));
            l.Add(Pair("Button hover", "TextBody", "ButtonHover", PairKind.Body, FontBody));
            l.Add(Pair("Button pressed", "TextBody", "ButtonPressed", PairKind.Body, FontBody));
            l.Add(Pair("Text on critical fill", "TextBody", "FillCritical", PairKind.Body, FontBody));
            // status and dim text on a button-off surface (alarm rows, read messages), body text on a lit tab
            foreach (var fg in new[] { "TextDim", "StatusOk", "StatusCaution", "StatusCritical" })
                l.Add(Pair(fg + " on button off", fg, "ButtonOff", PairKind.Body, FontBody));
            // progress marker line drawn over the track and its fills
            foreach (var bg in new[] { "ButtonOff", "ButtonOn", "FillCritical" })
                l.Add(Pair("Heading marker on " + bg, "TextHeading", bg, PairKind.Boundary, 0));
            // controls drawn on a button-off track: slider fill and handle, checkbox tick, bar-chart bars
            foreach (var fg in new[] { "StatusNormal", "StatusOk", "TextHeading" })
                l.Add(Pair(fg + " on control track", fg, "ButtonOff", PairKind.Boundary, 0));
            // dark text on a bright status fill (badges)
            foreach (var bg in new[] { "StatusOk", "StatusCaution", "StatusCritical", "StatusNormal" })
                l.Add(Pair("Screen ink on " + bg, "SurfaceScreen", bg, PairKind.Body, FontCaption));
            foreach (var fg in new[] { "TextInkOnPaper", "TextPaperCurrent", "TextPaperDone" })
                l.Add(Pair(fg + " on paper", fg, "SurfacePaper", PairKind.Body, FontBody));
            foreach (var fg in new[] { "TextBody", "TextDim", "StatusOk", "StatusCaution", "StatusCritical" })
                l.Add(Pair(fg + " on tooltip", fg, "SurfaceTooltip", PairKind.Body, FontCaption, overScene: true));
            l.Add(Pair("Scene body on panel", "TextBody", "SurfacePanel", PairKind.Body, FontBody, overScene: true));
            foreach (var bg in new[] { "SurfaceScreen", "SurfacePanel", "SurfaceRow", "SurfaceTooltip" })
                l.Add(Pair("Button border on " + bg, "ButtonBorder", bg, PairKind.Boundary, 0));
            // panel header band (uGUI panels): title, body and dim text sit on it
            l.Add(Pair("Heading on header band", "TextHeading", "SurfaceHeader", PairKind.Large, FontTitle));
            foreach (var fg in new[] { "TextBody", "TextDim", "StatusOk", "StatusCaution" })
                l.Add(Pair(fg + " on header band", fg, "SurfaceHeader", PairKind.Body, FontBody));
            // focus ring around a control, over every panel surface
            foreach (var bg in new[] { "SurfacePanel", "SurfaceRow", "SurfaceHeader", "SurfaceTooltip", "ButtonOff" })
                l.Add(Pair("Focus ring on " + bg, "FocusRing", bg, PairKind.Boundary, 0));
            // big monospace numerics and the title on the tooltip/panel backings
            l.Add(Pair("Numeric on panel", "StatusOk", "SurfacePanel", PairKind.Large, FontNumeric));
            l.Add(Pair("Title on tooltip", "TextHeading", "SurfaceTooltip", PairKind.Large, FontTitle, overScene: true));
            return l;
        }

        private static ContrastPair Pair(string name, string fg, string bg, PairKind kind, int size, bool overScene = false)
        {
            return new ContrastPair { Name = name, Fg = fg, Bg = bg, Kind = kind, FontSize = size, OverScene = overScene };
        }

        // ---- WCAG math ----

        public static bool IsLargeText(int fontSize, bool bold)
        {
            return fontSize >= LargeTextMin || (bold && fontSize >= LargeBoldTextMin);
        }

        private static double Linear(double c)
        {
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        public static double Luminance(Rgba c)
        {
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }

        /// <summary>WCAG contrast ratio, 1..21. Alpha is ignored: composite first with CompositeOver.</summary>
        public static double Contrast(Rgba a, Rgba b)
        {
            double la = Luminance(a), lb = Luminance(b);
            double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        /// <summary>Source-over: the token's alpha blended onto an opaque backdrop. Result is opaque.</summary>
        public static Rgba CompositeOver(Rgba top, Rgba backdrop)
        {
            double a = top.A;
            return new Rgba(top.R * a + backdrop.R * (1 - a), top.G * a + backdrop.G * (1 - a), top.B * a + backdrop.B * (1 - a));
        }

        /// <summary>Hue in degrees, 0..360.</summary>
        public static double Hue(Rgba c)
        {
            double max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
            double d = max - min;
            if (d <= 0) return 0;
            double h;
            if (max == c.R) h = ((c.G - c.B) / d) % 6;
            else if (max == c.G) h = (c.B - c.R) / d + 2;
            else h = (c.R - c.G) / d + 4;
            h *= 60;
            return h < 0 ? h + 360 : h;
        }
    }
}
