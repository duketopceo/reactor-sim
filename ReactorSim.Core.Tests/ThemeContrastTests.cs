using System.Collections.Generic;
using System.Linq;
using ReactorSim.Ui;
using Xunit;

namespace ReactorSim.Tests
{
    // Startup-window U1: the UI palette is fixed and its contrast is measured, not hoped for.
    // Pure: Theme has no UnityEngine reference, so this runs on Linux.
    public class ThemeContrastTests
    {
        private static double Required(PairKind k) { return k == PairKind.Body ? 4.5 : 3.0; }

        [Fact]
        public void EveryDeclaredPair_MeetsItsRequiredRatio_OnBlackAndWhiteBackdrops()
        {
            var failures = new List<string>();
            foreach (var p in Theme.Pairs)
            {
                var fg = Theme.Token(p.Fg);
                var bg = Theme.Token(p.Bg);
                foreach (double backdrop in new[] { 0.0, 1.0 })
                {
                    var surface = Theme.CompositeOver(bg, new Rgba(backdrop, backdrop, backdrop));
                    double ratio = Theme.Contrast(fg, surface);
                    if (ratio < Required(p.Kind))
                        failures.Add($"{p.Name}: {p.Fg} on {p.Bg} over backdrop {backdrop} = {ratio:F2} < {Required(p.Kind)}");
                }
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void ContrastFunction_BlackOnWhiteIs21_AndIdenticalIs1()
        {
            Assert.Equal(21.0, Theme.Contrast(new Rgba(0, 0, 0), new Rgba(1, 1, 1)), 3);
            Assert.Equal(21.0, Theme.Contrast(new Rgba(1, 1, 1), new Rgba(0, 0, 0)), 3);
            Assert.Equal(1.0, Theme.Contrast(new Rgba(0.4, 0.5, 0.6), new Rgba(0.4, 0.5, 0.6)), 6);
        }

        [Fact]
        public void ContrastFunction_ReportsMidGreyOnMidGreyAsFailing()
        {
            double ratio = Theme.Contrast(new Rgba(0.5, 0.5, 0.5), new Rgba(0.45, 0.45, 0.45));
            Assert.True(ratio < 3.0);
        }

        [Fact]
        public void SemiTransparentBacking_PassesOverWhiteAndOverBlack()
        {
            var backing = Theme.Token("SurfaceTooltip");
            Assert.True(backing.A < 1.0, "tooltip backing should carry opacity");
            var text = Theme.Token("TextBody");
            foreach (double backdrop in new[] { 0.0, 1.0 })
            {
                var composed = Theme.CompositeOver(backing, new Rgba(backdrop, backdrop, backdrop));
                Assert.True(Theme.Contrast(text, composed) >= 4.5, "backdrop " + backdrop);
            }
        }

        [Fact]
        public void TooFaintBacking_FailsOverWhite()
        {
            var faint = new Rgba(0.02, 0.04, 0.06, 0.2);
            var composed = Theme.CompositeOver(faint, new Rgba(1, 1, 1));
            Assert.True(Theme.Contrast(Theme.Token("TextBody"), composed) < 4.5);
        }

        [Fact]
        public void PairTokens_AllExist_AndSurfacesAreDistinct()
        {
            foreach (var p in Theme.Pairs)
            {
                Assert.True(Theme.Tokens.ContainsKey(p.Fg), "missing token " + p.Fg);
                Assert.True(Theme.Tokens.ContainsKey(p.Bg), "missing token " + p.Bg);
            }
            var surfaces = Theme.Tokens.Where(kv => kv.Key.StartsWith("Surface")).ToList();
            Assert.True(surfaces.Count >= 5);
            for (int i = 0; i < surfaces.Count; i++)
                for (int j = i + 1; j < surfaces.Count; j++)
                    Assert.False(surfaces[i].Value.Equals(surfaces[j].Value), surfaces[i].Key + " == " + surfaces[j].Key);
        }

        [Fact]
        public void LargeTaggedPair_NeedsALargeFontSize()
        {
            foreach (var p in Theme.Pairs.Where(x => x.Kind == PairKind.Large))
                Assert.True(Theme.IsLargeText(p.FontSize, p.Bold), p.Name + " is tagged large but its size is not");
            Assert.False(Theme.IsLargeText(Theme.FontCaption, false));
            Assert.True(Theme.IsLargeText(Theme.FontHeading, false));
            Assert.True(Theme.IsLargeText(14, true));
            Assert.False(Theme.IsLargeText(13, true));
        }

        [Fact]
        public void TextOverTheScene_SitsOnABackingAndIsNotSmallerThanTheMinimum()
        {
            var scene = Theme.Pairs.Where(p => p.OverScene).ToList();
            Assert.NotEmpty(scene);
            foreach (var p in scene)
            {
                Assert.True(p.Bg == "SurfaceTooltip" || p.Bg == "SurfacePanel", p.Name + " has no backing panel token");
                Assert.True(p.FontSize >= Theme.MinSceneFontSize, p.Name);
            }
        }

        [Fact]
        public void CautionAndCritical_AreDistinguishable()
        {
            double a = Theme.Hue(Theme.Token("StatusCaution"));
            double b = Theme.Hue(Theme.Token("StatusCritical"));
            double d = System.Math.Abs(a - b);
            if (d > 180) d = 360 - d;
            Assert.True(d >= 20, "hue gap " + d);
        }

        // ---- playtest-2 U5: type ramp, spacing scale, uGUI canvas rules, new surfaces ----

        [Fact]
        public void TypeRamp_IsAscending_AndEveryRoleMapsToItsToken()
        {
            Assert.Equal(Theme.FontCaption, Theme.RampSize(TextRole.Caption));
            Assert.Equal(Theme.FontBody, Theme.RampSize(TextRole.Body));
            Assert.Equal(Theme.FontHeading, Theme.RampSize(TextRole.Heading));
            Assert.Equal(Theme.FontNumeric, Theme.RampSize(TextRole.Numeric));
            Assert.Equal(Theme.FontTitle, Theme.RampSize(TextRole.Title));
            Assert.Equal(Theme.FontDisplay, Theme.RampSize(TextRole.Display));
            for (int i = 1; i < Theme.TypeRamp.Count; i++)
                Assert.True(Theme.TypeRamp[i] > Theme.TypeRamp[i - 1], "ramp step " + i);
            Assert.True(Theme.FontCaption >= Theme.MinSceneFontSize);
        }

        [Fact]
        public void SpacingScale_Is_4_8_12_16_24()
        {
            Assert.Equal(new[] { 4, 8, 12, 16, 24 }, Theme.SpacingScale.ToArray());
        }

        [Fact]
        public void ScaledFont_MultipliesTheRamp_WithAClamp()
        {
            Assert.Equal(20, Theme.ScaledFont(20, 1.0));
            Assert.Equal(30, Theme.ScaledFont(20, 1.5));
            Assert.Equal(30, Theme.ScaledFont(20, 3.0));       // clamped at the ceiling
            Assert.Equal(15, Theme.ScaledFont(20, 0.1));       // clamped at the floor (0.75)
            Assert.Equal(20, Theme.ScaledFont(20, double.NaN));
            Assert.Equal(8, Theme.ScaledFont(4, 1.0));         // never below 8
        }

        [Fact]
        public void Canvas_ScalesByHeightFromAStatedReference()
        {
            Assert.Equal(1920, Theme.ReferenceWidth);
            Assert.Equal(1080, Theme.ReferenceHeight);
            Assert.Equal(1920.0, Theme.CanvasWidth(1600.0, 900.0), 6);      // 16:9 window
            Assert.Equal(2580.0, Theme.CanvasWidth(3440.0, 1440.0), 6);     // ultrawide
            Assert.Equal(1920.0, Theme.CanvasWidth(100.0, 0.0), 6);
        }

        [Fact]
        public void PanelWidth_GrowsWithTextScale_ButStaysOnScreen()
        {
            Assert.Equal(700.0, Theme.PanelWidth(700.0, 1.0, 1920.0), 6);
            Assert.Equal(1050.0, Theme.PanelWidth(700.0, 1.5, 1920.0), 6);
            Assert.Equal(1000.0 * Theme.PanelMaxWidthFraction, Theme.PanelWidth(1200.0, 1.5, 1000.0), 6);
            Assert.Equal(1.0, Theme.FitScale(900.0), 6);
            double tall = Theme.FitScale(2000.0);
            Assert.True(tall < 1.0);
            Assert.Equal(Theme.ReferenceHeight * Theme.PanelMaxHeightFraction, 2000.0 * tall, 6);
        }

        [Fact]
        public void NewPanelSurfaces_HaveDeclaredPairs()
        {
            foreach (var bg in new[] { "SurfaceHeader" })
                Assert.Contains(Theme.Pairs, p => p.Bg == bg && p.Kind == PairKind.Body);
            Assert.Contains(Theme.Pairs, p => p.Fg == "TextHeading" && p.Bg == "SurfaceHeader" && p.Kind == PairKind.Large);
            foreach (var bg in new[] { "SurfacePanel", "SurfaceRow", "SurfaceHeader", "SurfaceTooltip", "ButtonOff" })
                Assert.Contains(Theme.Pairs, p => p.Fg == "FocusRing" && p.Bg == bg && p.Kind == PairKind.Boundary);
        }

        [Fact]
        public void ADeliberatelyLowContrastPair_IsCaughtByTheSameCheck()
        {
            // The fixture a contributor might add by mistake: a dark button face on the header band.
            var bad = new ContrastPair { Name = "fixture", Fg = "ButtonOff", Bg = "SurfaceHeader", Kind = PairKind.Body, FontSize = Theme.FontBody };
            double ratio = Theme.Contrast(Theme.Token(bad.Fg), Theme.CompositeOver(Theme.Token(bad.Bg), new Rgba(0, 0, 0)));
            Assert.True(ratio < 4.5, "the fixture must fail the body bar, ratio " + ratio);
        }

        [Fact]
        public void Theme_CoversTheTokenGroupsTheMigrationNeeds()
        {
            foreach (var name in new[] { "SurfaceScreen", "SurfacePanel", "SurfaceRow", "SurfacePaper", "SurfaceTooltip",
                "TextBody", "TextDim", "TextHeading", "TextInkOnPaper", "StatusNormal", "StatusCaution", "StatusCritical",
                "StatusOk", "ButtonOff", "ButtonOn", "ButtonBorder", "CrtPhosphor" })
                Assert.True(Theme.Tokens.ContainsKey(name), name);
            Assert.True(Theme.FontCaption < Theme.FontBody && Theme.FontBody < Theme.FontHeading);
        }
    }
}
