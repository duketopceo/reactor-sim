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
