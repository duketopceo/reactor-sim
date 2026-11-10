using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ReactorSim.Core.Tests
{
    // R5: UI scripts take text and panel colors from Theme/ThemeColors, never literals. This scans the covered scripts
    // for color literals outside an explicit allowlist (emissive lamps, light colors, prop geometry, particle material).
    public class ThemeLiteralScanTests
    {
        private static readonly Regex Literal = new Regex(
            @"new\s+Color(32)?\s*\(|\bColor32\s*\(|\bColor\.(white|black|gray|grey|red|green|yellow|cyan|magenta|blue)\b|<color=#|""#[0-9A-Fa-f]{6}""",
            RegexOptions.Compiled);

        private sealed class Allow
        {
            public string File;      // path relative to Scripts/
            public string LineRx;    // null = whole file
            public string Why;
            public Allow(string file, string lineRx, string why) { File = file; LineRx = lineRx; Why = why; }
        }

        // Every entry must still match a literal line in an existing file (AllowlistEntriesStillMatch).
        private static readonly Allow[] Allowlist =
        {
            new Allow("UI/Theme.cs", null, "the palette itself"),
            new Allow("UI/ThemeColors.cs", null, "Unity adapter; 1x1 white tint texture and Rgba conversion"),
            new Allow("View3D/AlarmFeedback.cs", null, "alarm emission and room light colors"),
            new Allow("View3D/CounterDisplay.cs", null, "seven-segment emission, a physical display"),
            new Allow("View3D/PlantEquipmentBinder.cs", null, "particle/steam material"),
            new Allow("View3D/ControlRoom3DBinder.cs", @"Lamp(Off|Amber|Red|Green)\b|\bfc\b|SetLampRaw|emission", "lamp tile colors and emission"),
            new Allow("UI/ProcedureClipboard.cs", @"Part\(""", "clipboard prop geometry (board, paper, clip)"),
            new Allow("UI/OperatorTablet.cs", @"var color = new Color", "world-space pointer arrow geometry"),
        };

        // Playtest-2 U5 (KTD6, R15): files already moved to uGUI + TextMeshPro. They may not use IMGUI text surfaces, the
        // legacy font, or raw numeric font sizes; sizes come from the Theme type ramp (TextRole / Theme.RampSize).
        // Extend this list as each surface migrates (ControlTooltip, OperatorController hint and the drag readout are done).
        private static readonly string[] MigratedFiles =
        {
            "UI/RuntimeUi.cs", "UI/StartupChecklistHud.cs", "UI/PauseMenu.cs", "UI/MainMenu.cs", "UI/OperatorTablet.cs",
            "UI/TabletPages.cs", "UI/ProcedureClipboard.cs", "UI/ReadoutFormat.cs",
            "View3D/CrtScreenRenderer.cs", "View3D/CrtPages.cs", "View3D/ScreenFocus.cs", "View3D/PixelBudget.cs",
            "View3D/ControlTooltip.cs", "View3D/OperatorController.cs", "View3D/ControlRoom3DBinder.cs",
        };

        private static readonly Regex Imgui = new Regex(
            @"\bOnGUI\b|\bGUI\.(Label|Box|Button|Toggle|DrawTexture|TextField)\b|\bGUILayout\b|\bGUIStyle\b|\bGUIContent\b|\bLegacyRuntime\b|\bGUI\.matrix\b",
            RegexOptions.Compiled);

        // fontSize = 24, fontSizeMin = 0.05f, and an integer size argument right before a TextAnchor argument.
        private static readonly Regex RawFontSize = new Regex(
            @"\bfontSize(Min|Max)?\s*=\s*[0-9.]|\b[0-9]+\s*,\s*TextAnchor\.|\bScaled\(\s*[0-9]",
            RegexOptions.Compiled);

        /// <summary>Violations of the migrated-file rules for source text under a Scripts-relative name.</summary>
        private static List<string> ScanMigrated(string rel, string text, string[] migrated)
        {
            var hits = new List<string>();
            if (Array.IndexOf(migrated, rel) < 0) return hits;
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string code = StripComment(lines[i]);
                if (Imgui.IsMatch(code)) hits.Add(rel + ":" + (i + 1) + ": IMGUI/legacy text: " + code.Trim());
                if (RawFontSize.IsMatch(code)) hits.Add(rel + ":" + (i + 1) + ": raw font size (use the type ramp): " + code.Trim());
            }
            return hits;
        }

        private static readonly string[] ScanDirs = { "UI", "View3D", "Missions" };
        private static readonly string[] ScanFiles = { "PlantBridge.cs" };

        private static string ScriptsDir()
        {
            string theme = TestRepo.Find("ReactorSim.Unity", "Assets", "Scripts", "UI", "Theme.cs");
            return Path.GetDirectoryName(Path.GetDirectoryName(theme));
        }

        private static string Norm(string p) { return p.Replace('\\', '/'); }

        private static IEnumerable<string> Covered(string scripts)
        {
            foreach (var d in ScanDirs)
            {
                string dir = Path.Combine(scripts, d);
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)) yield return f;
            }
            foreach (var f in ScanFiles) yield return Path.Combine(scripts, f);
        }

        private static string StripComment(string line)
        {
            int i = line.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? line.Substring(0, i) : line;
        }

        /// <summary>Violations as "file:line: text", for source text under a Scripts-relative name.</summary>
        private static List<string> Scan(string rel, string text, Allow[] allow)
        {
            var hits = new List<string>();
            var rules = allow.Where(a => a.File == rel).ToList();
            if (rules.Any(r => r.LineRx == null)) return hits;
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string code = StripComment(lines[i]);
                if (!Literal.IsMatch(code)) continue;
                if (rules.Any(r => Regex.IsMatch(code, r.LineRx))) continue;
                hits.Add(rel + ":" + (i + 1) + ": " + code.Trim());
            }
            return hits;
        }

        [Fact]
        public void CoveredScriptsHaveNoColorLiterals()
        {
            string scripts = ScriptsDir();
            var all = new List<string>();
            int files = 0;
            foreach (var f in Covered(scripts))
            {
                Assert.True(File.Exists(f), "covered file missing: " + f);
                files++;
                string rel = Norm(Path.GetRelativePath(scripts, f));
                all.AddRange(Scan(rel, File.ReadAllText(f), Allowlist));
            }
            Assert.True(files > 10, "scan covered too few files: " + files);
            Assert.True(all.Count == 0, "color literals outside the allowlist (use Theme/ThemeColors):\n" + string.Join("\n", all));
        }

        [Fact]
        public void MigratedFilesUseNoImguiTextAndNoRawFontSizes()
        {
            string scripts = ScriptsDir();
            var all = new List<string>();
            foreach (var rel in MigratedFiles)
            {
                string path = Path.Combine(scripts, rel);
                Assert.True(File.Exists(path), "migrated file missing: " + rel);
                all.AddRange(ScanMigrated(rel, File.ReadAllText(path), MigratedFiles));
            }
            Assert.True(all.Count == 0, "migrated files must use uGUI + TMP and the type ramp:\n" + string.Join("\n", all));
        }

        [Fact]
        public void MigratedScanFlagsSyntheticOnGuiLabelAndRawSizes()
        {
            var one = new[] { "UI/Fake.cs" };
            Assert.Single(ScanMigrated("UI/Fake.cs", "private void OnGUI() { }", one));
            Assert.Single(ScanMigrated("UI/Fake.cs", "GUI.Label(r, \"x\");", one));
            Assert.Single(ScanMigrated("UI/Fake.cs", "GUILayout.Label(\"x\");", one));
            Assert.Single(ScanMigrated("UI/Fake.cs", "var s = new GUIStyle();", one));
            Assert.Single(ScanMigrated("UI/Fake.cs", "t.fontSize = 24;", one));
            Assert.Single(ScanMigrated("UI/Fake.cs", "t.fontSizeMin = 0.05f;", one));
            Assert.Single(ScanMigrated("UI/Fake.cs", "RuntimeUi.MakeLabel(p, \"x\", 22, TextAnchor.MiddleLeft, c);", one));
            Assert.Single(ScanMigrated("UI/Fake.cs", "int n = UserSettings.Scaled(20);", one));
            // clean code, comments and files outside the list pass
            Assert.Empty(ScanMigrated("UI/Fake.cs", "t.fontSize = FontSize(role); // was fontSize = 24", one));
            Assert.Empty(ScanMigrated("UI/Fake.cs", "RuntimeUi.MakeLabel(p, \"x\", TextRole.Body, TextAnchor.MiddleLeft, c);", one));
            Assert.Empty(ScanMigrated("UI/Other.cs", "private void OnGUI() { GUI.Label(r, \"x\"); }", one));
        }

        [Fact]
        public void ScanFlagsSyntheticLiteral()
        {
            Assert.Single(Scan("UI/Fake.cs", "var c = new Color(0.5f, 0.5f, 0.5f);", Allowlist));
            Assert.Single(Scan("UI/Fake.cs", "x.color = Color.white;", Allowlist));
            Assert.Single(Scan("UI/Fake.cs", "var c = new Color32(1, 2, 3, 4);", Allowlist));
            Assert.Single(Scan("UI/Fake.cs", "s = \"<color=#7CF8A0>ok</color>\";", Allowlist));
            Assert.Single(Scan("UI/Fake.cs", "string c = \"#FF6B6B\";", Allowlist));
            Assert.Empty(Scan("UI/Fake.cs", "var c = ThemeColors.TextBody; // new Color(1,1,1)", Allowlist));
            Assert.Empty(Scan("UI/Theme.cs", "new Color(0.5f, 0.5f, 0.5f)", Allowlist));
        }

        [Fact]
        public void AllowlistEntriesStillMatch()
        {
            string scripts = ScriptsDir();
            foreach (var a in Allowlist)
            {
                string path = Path.Combine(scripts, a.File);
                Assert.True(File.Exists(path), "allowlist file missing: " + a.File);
                if (a.LineRx == null) continue;
                bool matched = File.ReadAllLines(path).Select(StripComment)
                    .Any(l => Literal.IsMatch(l) && Regex.IsMatch(l, a.LineRx));
                Assert.True(matched, "allowlist entry matches no literal line (remove it): " + a.File + " /" + a.LineRx + "/");
            }
        }
    }
}
