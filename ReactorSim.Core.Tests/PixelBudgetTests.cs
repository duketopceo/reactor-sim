using System;
using ReactorSim.Instruments;
using Xunit;

namespace ReactorSim.Tests
{
    // Playtest-2 U4 (KTD4, R11): the pixel budget is arithmetic, so it is pinned here and judged in captures later.
    public class PixelBudgetTests
    {
        [Fact]
        public void PixelsPerMetre_MatchesPinholeGeometry()
        {
            // 900 px window, 90 degree fov: half-height at 1 m is 1 m, so 450 px per metre.
            Assert.Equal(450.0, PixelBudget.PixelsPerMetre(900.0, 90.0, 1.0), 6);
            Assert.Equal(225.0, PixelBudget.PixelsPerMetre(900.0, 90.0, 2.0), 6);
            Assert.Equal(0.0, PixelBudget.PixelsPerMetre(900.0, 90.0, 0.0));
            Assert.Equal(0.0, PixelBudget.PixelsPerMetre(0.0, 70.0, 2.0));
            Assert.Equal(0.0, PixelBudget.PixelsPerMetre(900.0, 180.0, 2.0));
        }

        [Fact]
        public void PlaytestArithmetic_SeatViewGivesFewPixelsPerCharacter()
        {
            // 0.4 m wide screen, 2 m away, 70 degree fov, 900 px: the hypothesis in the plan (about 130 px wide).
            double ppm = PixelBudget.PixelsPerMetre(900.0, 70.0, 2.0);
            Assert.InRange(0.4 * ppm, 120.0, 140.0);
        }

        [Fact]
        public void Oblique_ShrinksPixelDensity()
        {
            double straight = PixelBudget.PixelsPerMetre(900.0, 70.0, 2.0);
            double angled = PixelBudget.PixelsPerMetreOblique(900.0, 70.0, 2.0, 60.0);
            Assert.Equal(straight * 0.5, angled, 6);
        }

        [Fact]
        public void EmAndGlyphHeight_AreInverse()
        {
            double ppm = 321.0;
            double em = PixelBudget.EmForGlyphPx(PixelBudget.BodyGlyphMinPx, ppm);
            Assert.Equal(PixelBudget.BodyGlyphMinPx, PixelBudget.GlyphHeightPx(em, ppm), 9);
            Assert.True(double.IsPositiveInfinity(PixelBudget.EmForGlyphPx(10.0, 0.0)));
        }

        [Fact]
        public void Solve_ReturnsColumnsAndRows_ThatMeetTheGlyphFloor()
        {
            // Big face close up: plenty of room.
            var close = PixelBudget.Solve(0.4, 0.3, 2000.0);
            Assert.True(close.Fits);
            Assert.True(close.Columns >= 28 && close.Rows >= 10, close.Columns + "x" + close.Rows);
            Assert.Equal(PixelBudget.BodyGlyphMinPx, close.GlyphPx, 6);
            // Same face far away: fewer columns and rows.
            var far = PixelBudget.Solve(0.4, 0.3, 300.0);
            Assert.True(far.Columns < close.Columns && far.Rows < close.Rows);
        }

        [Fact]
        public void Solve_ReturnsZeroRows_WhenNothingFits()
        {
            var none = PixelBudget.Solve(0.4, 0.3, 20.0);   // a few pixels per centimetre: not even one glyph row
            Assert.False(none.Fits);
            Assert.Equal(0, none.Rows);
            Assert.Equal(0, none.Columns);
            Assert.False(PixelBudget.Solve(0.0, 0.3, 500.0).Fits);
            Assert.False(PixelBudget.Solve(0.4, 0.3, 0.0).Fits);
        }

        [Fact]
        public void PageMeetsBudget_RisesWithDensity_AndFailsFromTheSeat()
        {
            const int cols = 28; const int rows = 10;
            double seat = PixelBudget.PixelsPerMetre(900.0, 70.0, 2.0);
            Assert.False(PixelBudget.PageMeetsBudget(0.4, 0.3, cols, rows, seat), "seat view should need focus");
            double focus = PixelBudget.PixelsPerMetre(900.0, 35.0, PixelBudget.FocusDistance(0.4, 0.3, 35.0, 16.0 / 9.0));
            Assert.True(PixelBudget.PageMeetsBudget(0.4, 0.3, cols, rows, focus), "focused view must meet the body floor");
            Assert.True(PixelBudget.PageMeetsBudget(0.4, 0.3, cols, rows, focus, PixelBudget.KeyGlyphMinPx));
        }

        [Fact]
        public void MaxDistanceForPage_IsWhereTheFloorIsJustMet()
        {
            double d = PixelBudget.MaxDistanceForPage(0.4, 0.3, 28, 10, 900.0, 70.0);
            Assert.True(d > 0.0);
            double ppm = PixelBudget.PixelsPerMetre(900.0, 70.0, d);
            double em = PixelBudget.EmToFillFace(0.4, 0.3, 28, 10);
            Assert.Equal(PixelBudget.BodyGlyphMinPx, PixelBudget.GlyphHeightPx(em, ppm), 6);
            Assert.Equal(0.0, PixelBudget.MaxDistanceForPage(0.4, 0.3, 0, 10, 900.0, 70.0));
        }

        [Fact]
        public void EmToFillFace_IsLimitedByTheTighterAxis()
        {
            double wide = PixelBudget.EmToFillFace(1.0, 0.1, 10, 4);
            double tall = PixelBudget.EmToFillFace(0.1, 1.0, 10, 4);
            Assert.Equal(0.1 * 0.92 / (4 * PixelBudget.LineHeightEm), wide, 9);   // height binds
            Assert.Equal(0.1 * 0.92 / (10 * PixelBudget.CharAdvanceEm), tall, 9); // width binds
            Assert.Equal(0.0, PixelBudget.EmToFillFace(1.0, 1.0, 0, 4));
        }

        [Fact]
        public void FocusDistance_FitsTheLimitingAxis()
        {
            double d = PixelBudget.FocusDistance(0.4, 0.3, 40.0, 16.0 / 9.0, 0.9);
            double tan = Math.Tan(20.0 * Math.PI / 180.0);
            // Height binds for a 4:3 face in a 16:9 window.
            Assert.Equal(0.3 / (2.0 * tan * 0.9), d, 9);
            // A very wide face is width-limited.
            double dw = PixelBudget.FocusDistance(3.0, 0.3, 40.0, 16.0 / 9.0, 0.9);
            Assert.Equal(3.0 / (2.0 * tan * (16.0 / 9.0) * 0.9), dw, 9);
            Assert.Equal(0.0, PixelBudget.FocusDistance(0.4, 0.3, 0.0, 1.0));
        }
    }
}
