using System;

// Pure (no UnityEngine): how many pixels a glyph gets on screen. A CRT page is laid out in a fixed grid of columns
// and rows sized to this budget, and a screen that cannot meet it from the seat is read through ScreenFocus instead.
// Linked into ReactorSim.Core.Tests. Units: metres, degrees, pixels. A glyph's height is its cap height.
namespace ReactorSim.Instruments
{
    /// <summary>The grid that fits on a screen face at a given glyph size. Rows or Columns of 0 means nothing fits.</summary>
    public struct PageFit
    {
        public double EmMetres;       // font size on the screen face
        public int Columns;
        public int Rows;
        public double GlyphPx;        // projected cap height at the pose that was solved for
        public bool Fits { get { return Columns > 0 && Rows > 0; } }
    }

    public static class PixelBudget
    {
        /// <summary>Body glyphs are at least this tall on screen (px) at the reference window.</summary>
        public const double BodyGlyphMinPx = 10.0;
        /// <summary>Key readouts are at least this tall (px).</summary>
        public const double KeyGlyphMinPx = 18.0;
        public const double ReferenceWidthPx = 1600.0;
        public const double ReferenceHeightPx = 900.0;

        /// <summary>Cap height as a fraction of the font size (em) for the shipped faces (about 0.70).</summary>
        public const double CapHeightEm = 0.70;
        /// <summary>Advance width of one monospace character in em (IBM Plex Mono: 0.6).</summary>
        public const double CharAdvanceEm = 0.60;
        /// <summary>Line pitch in em.</summary>
        public const double LineHeightEm = 1.25;

        /// <summary>Screen pixels covered by one metre of a flat surface facing the camera at this distance.</summary>
        public static double PixelsPerMetre(double windowHeightPx, double verticalFovDeg, double distanceM)
        {
            if (windowHeightPx <= 0.0 || verticalFovDeg <= 0.0 || verticalFovDeg >= 179.0 || distanceM <= 0.0) return 0.0;
            double half = Math.Tan(verticalFovDeg * 0.5 * Math.PI / 180.0);
            return windowHeightPx / (2.0 * distanceM * half);
        }

        /// <summary>Same, for a surface turned obliqueDeg away from facing the camera (the squeezed axis shrinks by cos).</summary>
        public static double PixelsPerMetreOblique(double windowHeightPx, double verticalFovDeg, double distanceM, double obliqueDeg)
        {
            double c = Math.Cos(Math.Max(0.0, Math.Min(89.0, obliqueDeg)) * Math.PI / 180.0);
            return PixelsPerMetre(windowHeightPx, verticalFovDeg, distanceM) * c;
        }

        /// <summary>Projected cap height in pixels of text set at emMetres.</summary>
        public static double GlyphHeightPx(double emMetres, double pixelsPerMetre)
        {
            return Math.Max(0.0, emMetres) * CapHeightEm * Math.Max(0.0, pixelsPerMetre);
        }

        /// <summary>Font size (metres) whose projected cap height is exactly glyphPx.</summary>
        public static double EmForGlyphPx(double glyphPx, double pixelsPerMetre)
        {
            if (pixelsPerMetre <= 0.0) return double.PositiveInfinity;
            return glyphPx / (CapHeightEm * pixelsPerMetre);
        }

        /// <summary>
        /// The most columns and rows that fit on a face of faceWidthM x faceHeightM when body text must project to
        /// at least minGlyphPx. marginFrac is the unused share of each axis. Zero columns/rows when nothing fits.
        /// </summary>
        public static PageFit Solve(double faceWidthM, double faceHeightM, double pixelsPerMetre,
            double minGlyphPx = BodyGlyphMinPx, double marginFrac = 0.08)
        {
            var fit = new PageFit();
            if (faceWidthM <= 0.0 || faceHeightM <= 0.0 || pixelsPerMetre <= 0.0) return fit;
            double em = EmForGlyphPx(minGlyphPx, pixelsPerMetre);
            double usableW = faceWidthM * (1.0 - marginFrac);
            double usableH = faceHeightM * (1.0 - marginFrac);
            fit.EmMetres = em;
            fit.GlyphPx = GlyphHeightPx(em, pixelsPerMetre);
            fit.Columns = (int)Math.Floor(usableW / (CharAdvanceEm * em));
            fit.Rows = (int)Math.Floor(usableH / (LineHeightEm * em));
            if (fit.Columns <= 0 || fit.Rows <= 0) { fit.Columns = 0; fit.Rows = 0; }
            return fit;
        }

        /// <summary>
        /// Largest font size (metres) that lets a grid of columns x rows fill the face, so the page is as big as its
        /// content allows. Independent of the viewer: the budget then asks whether that size is readable.
        /// </summary>
        public static double EmToFillFace(double faceWidthM, double faceHeightM, int columns, int rows, double marginFrac = 0.08)
        {
            if (faceWidthM <= 0.0 || faceHeightM <= 0.0 || columns <= 0 || rows <= 0) return 0.0;
            double byWidth = faceWidthM * (1.0 - marginFrac) / (columns * CharAdvanceEm);
            double byHeight = faceHeightM * (1.0 - marginFrac) / (rows * LineHeightEm);
            return Math.Min(byWidth, byHeight);
        }

        /// <summary>True when a columns x rows page filling the face still meets minGlyphPx at this pixel density.</summary>
        public static bool PageMeetsBudget(double faceWidthM, double faceHeightM, int columns, int rows,
            double pixelsPerMetre, double minGlyphPx = BodyGlyphMinPx)
        {
            double em = EmToFillFace(faceWidthM, faceHeightM, columns, rows);
            return em > 0.0 && GlyphHeightPx(em, pixelsPerMetre) >= minGlyphPx;
        }

        /// <summary>Largest distance (metres) at which the page still meets minGlyphPx; 0 when it cannot at any distance.</summary>
        public static double MaxDistanceForPage(double faceWidthM, double faceHeightM, int columns, int rows,
            double windowHeightPx, double verticalFovDeg, double minGlyphPx = BodyGlyphMinPx)
        {
            double em = EmToFillFace(faceWidthM, faceHeightM, columns, rows);
            if (em <= 0.0) return 0.0;
            double perMetreOneM = PixelsPerMetre(windowHeightPx, verticalFovDeg, 1.0);   // px per metre at 1 m
            if (perMetreOneM <= 0.0) return 0.0;
            // glyphPx(d) = em * cap * perMetreOneM / d  >=  minGlyphPx
            return em * CapHeightEm * perMetreOneM / minGlyphPx;
        }

        /// <summary>
        /// Camera distance from the face that makes it fill fillFrac of the window along its limiting axis
        /// (height or width, whichever binds) at the given vertical field of view and window aspect.
        /// </summary>
        public static double FocusDistance(double faceWidthM, double faceHeightM, double verticalFovDeg,
            double windowAspect, double fillFrac = 0.9)
        {
            if (faceWidthM <= 0.0 || faceHeightM <= 0.0 || verticalFovDeg <= 0.0 || verticalFovDeg >= 179.0
                || windowAspect <= 0.0 || fillFrac <= 0.0) return 0.0;
            double tanV = Math.Tan(verticalFovDeg * 0.5 * Math.PI / 180.0);
            double byHeight = faceHeightM / (2.0 * tanV * fillFrac);
            double byWidth = faceWidthM / (2.0 * tanV * windowAspect * fillFrac);
            return Math.Max(byHeight, byWidth);
        }
    }
}
