using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;
using ReactorSim;
using ReactorSim.Controls;
using ReactorSim.Instruments;

namespace ReactorSim.Core.Tests
{
    // design/control-room/inventory.csv is the single list of every control, readout, alarm and page.
    // These tests keep its readiness column honest against the code that binds it today.
    public class InventoryTests
    {
        private static readonly string[] Levels = { "area", "system", "subsystem", "function", "device" };

        // Kinds no pivot should ever carry (dev/preset plumbing, not player controls).
        private static readonly string[] ExcludedKinds = { "LoadPreset", "SetTimeScaleDev" };

        private sealed class Row
        {
            public Dictionary<string, string> F = new Dictionary<string, string>();
            public int Line;
            public string this[string k] { get { return F.ContainsKey(k) ? F[k] : ""; } }
            public string Id { get { return this["id"]; } }
            public override string ToString() { return Id + " (line " + Line + ")"; }
        }

        private static string BaseDir { get { return AppContext.BaseDirectory; } }

        private static List<Row> Load()
        {
            string path = Path.Combine(BaseDir, "design", "control-room", "inventory.csv");
            Assert.True(File.Exists(path), "inventory.csv not copied to test output: " + path);
            var records = ReactorSim.Csv.CsvReader.Parse(File.ReadAllText(path, Encoding.UTF8));
            Assert.True(records.Count > 1, "inventory.csv has no data rows");
            var header = records[0];
            var rows = new List<Row>();
            for (int i = 1; i < records.Count; i++)
            {
                var rec = records[i];
                var row = new Row { Line = i + 1 };
                for (int c = 0; c < header.Count; c++)
                    row.F[header[c]] = c < rec.Count ? rec[c] : "";
                rows.Add(row);
            }
            return rows;
        }

        // ---- binding helpers -------------------------------------------------------------------------

        private static bool Prefixed(Row r, string prefix) { return r["binding"].StartsWith(prefix, StringComparison.Ordinal); }

        private static string[] BindingParts(Row r)
        {
            // "intent:Kind[:Target]" / "gauge:Name[:index]" -> everything after the prefix, split on ':'
            string b = r["binding"];
            return b.Substring(b.IndexOf(':') + 1).Split(':');
        }

        // KTD2: the pivot name a placed control would carry, built from the row alone.
        private static string ControlPivotName(Row r)
        {
            var p = BindingParts(r);
            string kind = p[0];
            string target = p.Length > 1 ? p[1] : "";
            string range = r["range_or_positions"];
            int dots = range.IndexOf("..", StringComparison.Ordinal);
            var sb = new StringBuilder();
            if (dots > 0)
            {
                sb.Append("INT_DIAL_").Append(kind).Append('_');
                if (target.Length > 0) sb.Append(target).Append('_');
                sb.Append(range.Substring(0, dots)).Append('_').Append(range.Substring(dots + 2));
            }
            else
            {
                sb.Append("INT_BTN_").Append(kind).Append('_');
                if (target.Length > 0) sb.Append(target).Append('_');
                sb.Append(range);
            }
            return sb.ToString();
        }

        // The gauge pivot name a placed instrument would carry (BAR when the binding has an index).
        private static string GaugePivotName(Row r)
        {
            var p = BindingParts(r);
            string range = r["range_or_positions"];
            int dots = range.IndexOf("..", StringComparison.Ordinal);
            string min = range.Substring(0, dots), max = range.Substring(dots + 2);
            return p.Length > 1
                ? "BIND_BAR_" + p[0] + "_" + p[1] + "_" + min + "_" + max
                : "BIND_GAUGE_" + p[0] + "_" + min + "_" + max;
        }

        private static IEnumerable<Row> Intents(List<Row> rows)
        {
            return rows.Where(r => Prefixed(r, "intent:"));
        }

        // ---- tests -----------------------------------------------------------------------------------

        [Fact]
        public void Ids_AreUnique_AndCategoryLevelsFilled()
        {
            var rows = Load();
            var seen = new HashSet<string>();
            foreach (var r in rows)
            {
                Assert.True(r.Id.Length > 0, "empty id on " + r);
                Assert.True(seen.Add(r.Id), "duplicate id " + r);
                foreach (var lv in Levels)
                    Assert.True(r[lv].Trim().Length > 0, r + " has empty '" + lv + "'");
                Assert.Contains(r["role"], new[] { "control", "indicator", "alarm", "page" });
                Assert.Contains(r["readiness"], new[] { "bound", "needs_mapping", "needs_core_model" });
                Assert.Contains(r["phase"], new[] { "room", "backend", "electrical", "plumbing" });
                Assert.Contains(r["overview"], new[] { "y", "n" });
            }
        }

        [Fact]
        public void LabelColumns_Exist_ShortLabelFilled_AndLabelsHaveNoTypeNotes()
        {
            string path = Path.Combine(BaseDir, "design", "control-room", "inventory.csv");
            var header = ReactorSim.Csv.CsvReader.Parse(File.ReadAllText(path, Encoding.UTF8))[0];
            Assert.Contains("short_label", header);
            Assert.Contains("type_note", header);
            foreach (var r in Load())
            {
                Assert.True(r["short_label"].Trim().Length > 0, r + " has an empty short_label");
                Assert.DoesNotContain("[type note", r["label"]);
                Assert.DoesNotContain("[type note", r["short_label"]);
                Assert.DoesNotContain("[type note", r["type_note"]);
            }
        }

        [Fact]
        public void OperatingPoints_FaceTheBoard()
        {
            // facing_deg is the heading the operator faces (toward the board): it equals at_deg, not at_deg + 180.
            var ops = LoadJson("layout.json").GetProperty("operating_points");
            foreach (var p in ops.EnumerateObject())
            {
                if (p.Name == "STAND_Center") continue;
                double diff = (p.Value.GetProperty("facing_deg").GetDouble() - p.Value.GetProperty("at_deg").GetDouble() + 540.0) % 360.0 - 180.0;
                Assert.True(Math.Abs(diff) < 0.01, p.Name + ": facing_deg must equal at_deg");
            }
        }

        [Fact]
        public void BindingPrefix_MatchesRoleAndReadiness()
        {
            foreach (var r in Load())
            {
                if (r["readiness"] == "needs_core_model")
                    Assert.True(Prefixed(r, "core:"), r + ": needs_core_model rows bind 'core:<name>'");
                else
                {
                    string want = r["role"] == "control" ? "intent:" : r["role"] == "page" ? "crt:"
                        : r["role"] == "alarm" ? "lamp:" : null;
                    if (want != null)
                        Assert.True(Prefixed(r, want), r + ": " + r["role"] + " rows bind '" + want + "...'");
                    else
                        Assert.True(Prefixed(r, "gauge:") || Prefixed(r, "lamp:"), r + ": indicator binds gauge: or lamp:");
                }
                Assert.True(r["binding"].IndexOf(':') > 0 && r["binding"].IndexOf(':') < r["binding"].Length - 1, r + ": bad binding");
            }
        }

        [Fact]
        public void PairedIndicators_PointAtRealRows()
        {
            var rows = Load();
            var ids = new HashSet<string>(rows.Select(r => r.Id));
            foreach (var r in rows.Where(r => r["paired_indicator"].Length > 0))
                Assert.True(ids.Contains(r["paired_indicator"]), r + ": paired_indicator '" + r["paired_indicator"] + "' is not a row id");
        }

        // Bound controls with no indicator in the inventory (U9 round trip): they still run the intent-log and value
        // checks, but have no pivot to watch. Mirrors RoundTripPlan.Exclusions in Assets/Editor/RoundTripCheck.cs
        // (BoundControls_PairedIndicatorOrExcluded keeps the two in step).
        private static readonly Dictionary<string, string> PairExclusions = new Dictionary<string, string>
        {
            { "clock_slow_btn", "no inventory indicator shows the time scale" },
            { "clock_fast_btn", "no inventory indicator shows the time scale" },
            { "assist_tier_ctl", "effect is text on the CRT_AssistantTerminal page, no pivot" },
            { "mission_select_ctl", "effect is text on the CRT_AssistantTerminal page, no pivot" },
        };

        [Fact]
        public void BoundControls_PairedIndicatorOrExcluded()
        {
            var rows = Load();
            var byId = rows.ToDictionary(r => r.Id);
            var bound = rows.Where(r => r["role"] == "control" && r["readiness"] == "bound").ToList();
            foreach (var r in bound)
            {
                string paired = r["paired_indicator"];
                if (paired.Length == 0)
                {
                    Assert.True(PairExclusions.ContainsKey(r.Id), r + ": bound control has no paired_indicator and is not on the exclusion list");
                    continue;
                }
                Assert.False(PairExclusions.ContainsKey(r.Id), r + ": has a paired_indicator but is on the exclusion list");
                var ind = byId[paired];
                Assert.True(ind["role"] == "indicator" || ind["role"] == "alarm", r + ": paired " + paired + " is a " + ind["role"] + ", not an indicator or alarm");
                Assert.True(ind["readiness"] == "bound", r + ": paired indicator " + paired + " is " + ind["readiness"] + ", not bound");
                Assert.True(Prefixed(ind, "gauge:") || Prefixed(ind, "lamp:"), r + ": paired " + paired + " binds " + ind["binding"] + " (needs gauge: or lamp: to have a pivot)");
            }
            foreach (var kv in PairExclusions)
            {
                Assert.True(kv.Value.Length > 0, kv.Key + ": exclusion needs a reason");
                Assert.True(bound.Any(r => r.Id == kv.Key), kv.Key + ": excluded but not a bound control row");
            }

            // RoundTripCheck.cs carries the same list between its EXCLUSIONS markers.
            string src = File.ReadAllText(TestRepo.Find("ReactorSim.Unity", "Assets", "Editor", "RoundTripCheck.cs"));
            int a = src.IndexOf("// ---- EXCLUSIONS BEGIN", StringComparison.Ordinal);
            int b = src.IndexOf("// ---- EXCLUSIONS END", StringComparison.Ordinal);
            Assert.True(a >= 0 && b > a, "RoundTripCheck.cs lost its EXCLUSIONS markers");
            var inCode = new SortedSet<string>(
                System.Text.RegularExpressions.Regex.Matches(src.Substring(a, b - a), "\\{\\s*\"([a-z0-9_]+)\"\\s*,\\s*\"")
                    .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value));
            Assert.Equal(new SortedSet<string>(PairExclusions.Keys), inCode);
        }

        [Fact]
        public void IntentControls_GenerateValidPivotNames()
        {
            // Every intent: row (any readiness) must produce a name PivotNameParser accepts, so the
            // pivot Blender will carry is well formed before any mapping exists.
            foreach (var r in Intents(Load()))
            {
                string name = ControlPivotName(r);
                var res = PivotNameParser.Parse(name);
                Assert.True(res.Status == ParseStatus.Ok, r + ": " + name + " -> " + res.Status + " " + res.Error);
            }
        }

        [Fact]
        public void BoundControls_ResolveThroughControlIntentFactory()
        {
            int n = 0;
            foreach (var r in Intents(Load()).Where(r => r["readiness"] == "bound"))
            {
                string name = ControlPivotName(r);
                var res = PivotNameParser.Parse(name);
                Assert.True(res.Ok, r + ": " + name + " -> " + res.Error);
                Assert.True(ControlIntentFactory.IsSupported(res.Spec), r + ": bound but " + name + " is not supported by ControlIntentFactory");
                n++;
            }
            Assert.True(n > 0, "no bound control rows");
        }

        [Fact]
        public void NeedsMappingControls_AreNotYetSupported()
        {
            // Readiness cannot go stale: once the factory handles a kind, its rows must be marked bound.
            foreach (var r in Intents(Load()).Where(r => r["readiness"] == "needs_mapping"))
            {
                var res = PivotNameParser.Parse(ControlPivotName(r));
                Assert.True(res.Ok, r + ": " + res.Error);
                Assert.False(ControlIntentFactory.IsSupported(res.Spec), r + ": marked needs_mapping but ControlIntentFactory now supports it; mark it bound");
            }
        }

        [Fact]
        public void BoundIndicators_ResolveThroughGaugeSources()
        {
            int n = 0;
            foreach (var r in Load().Where(r => r["role"] == "indicator" && Prefixed(r, "gauge:") && r["readiness"] == "bound"))
            {
                GaugeSource src;
                string name = GaugePivotName(r);
                Assert.True(GaugeSources.TryResolve(name, out src), r + ": bound but " + name + " does not resolve in GaugeSources");
                n++;
            }
            Assert.True(n > 0, "no bound gauge rows");
        }

        [Fact]
        public void NeedsMappingIndicators_DoNotYetResolve()
        {
            foreach (var r in Load().Where(r => Prefixed(r, "gauge:") && r["readiness"] == "needs_mapping"))
            {
                GaugeSource src;
                string name = GaugePivotName(r);
                Assert.False(GaugeSources.TryResolve(name, out src), r + ": marked needs_mapping but " + name + " now resolves; mark it bound");
            }
        }

        [Fact]
        public void GaugeRows_UnitsMatchTheSelectorTable()
        {
            foreach (var r in Load().Where(r => r["role"] == "indicator" && Prefixed(r, "gauge:") && r["readiness"] == "bound"))
            {
                GaugeSource src;
                Assert.True(GaugeSources.TryResolve(GaugePivotName(r), out src), r.ToString());
                Assert.True(r["units"] == src.Units, r + ": inventory units '" + r["units"] + "' but selector " + src.Name + " reports '" + src.Units + "'");
            }
        }

        [Fact]
        public void Lamps_BoundIffLampPredicatesHasThem_AndEveryPredicateHasARow()
        {
            var rowLamps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Load().Where(r => Prefixed(r, "lamp:") && r["readiness"] != "needs_core_model"))
            {
                string lamp = BindingParts(r)[0];
                rowLamps.Add(lamp);
                bool has = LampPredicates.Has(lamp);
                if (r["readiness"] == "bound") Assert.True(has, r + ": bound but LampPredicates has no " + lamp);
                else Assert.False(has, r + ": needs_mapping but LampPredicates now has " + lamp + "; mark it bound");
            }
            foreach (var name in LampPredicates.Names)
                Assert.True(rowLamps.Contains(name), "LampPredicates has " + name + " but no inventory lamp: row names it");
        }

        [Fact]
        public void Pages_BoundIffKnown()
        {
            foreach (var r in Load().Where(r => Prefixed(r, "crt:") && r["readiness"] != "needs_core_model"))
            {
                string page = BindingParts(r)[0];
                bool known = CrtPages.IsKnownPage(page);
                if (r["readiness"] == "bound") Assert.True(known, r + ": bound but " + page + " is not in CrtPages.KnownPages");
                else Assert.False(known, r + ": needs_mapping but " + page + " is now a known page; mark it bound");
            }
        }

        [Fact]
        public void EveryPlayerIntentKind_HasARow_OrIsExcluded()
        {
            var used = new HashSet<string>(Intents(Load()).Select(r => BindingParts(r)[0]), StringComparer.OrdinalIgnoreCase);
            foreach (var k in Enum.GetNames(typeof(IntentKind)))
            {
                if (ExcludedKinds.Contains(k)) continue;
                Assert.True(used.Contains(k), "IntentKind." + k + " has no inventory row (add one or list it in ExcludedKinds)");
            }
            foreach (var k in ExcludedKinds)
                Assert.False(used.Contains(k), k + " is excluded but has a row");
        }

        [Fact]
        public void EveryExistingCrtPage_HasARow()
        {
            var pages = new HashSet<string>(Load().Where(r => Prefixed(r, "crt:")).Select(r => BindingParts(r)[0]));
            foreach (var p in CrtPages.KnownPages)
                Assert.True(pages.Contains(p), p + " has no inventory row");
        }

        // ---- U4: physical types, kits, placement and room layout -------------------------------------

        // KTD5 physical types and the kits each may use (kit ids come from design/control-room/kits.json).
        private static readonly Dictionary<string, string[]> TypeKits = new Dictionary<string, string[]>
        {
            { "rotary_knob", new[] { "K08" } },
            { "setpoint_slider", new[] { "K12" } },
            { "stepped_knob", new[] { "K09" } },
            { "pushbutton", new[] { "K19", "K06" } },
            { "guarded_toggle", new[] { "K11" } },
            { "lever", new[] { "K07" } },
            { "handwheel", new[] { "K10" } },
            { "circular_gauge", new[] { "K01", "K01L", "K03" } },
            { "edgewise_meter", new[] { "K02" } },
            { "counter", new[] { "K04" } },
            { "annunciator_tile", new[] { "K05" } },
            { "status_lamp", new[] { "K20" } },
            { "crt_page", new[] { "K13" } },
        };

        // Kits an overview=y row may use (read from STAND_Center at about 4.5 m or more).
        private static readonly string[] OverviewKits = { "K01L", "K05", "K04", "K21" };

        private sealed class Kit
        {
            public string Id, Role;
            public double W, H;
            public string[] Mount;
        }

        private static JsonElement LoadJson(string name)
        {
            string path = Path.Combine(BaseDir, "design", "control-room", name);
            Assert.True(File.Exists(path), name + " not copied to test output: " + path);
            return JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8)).RootElement;
        }

        private static Dictionary<string, Kit> LoadKits()
        {
            var kits = new Dictionary<string, Kit>();
            foreach (var p in LoadJson("kits.json").GetProperty("kits").EnumerateObject())
                kits[p.Name] = new Kit
                {
                    Id = p.Name,
                    Role = p.Value.GetProperty("role").GetString(),
                    W = p.Value.GetProperty("face_w").GetDouble(),
                    H = p.Value.GetProperty("face_h").GetDouble(),
                    Mount = p.Value.GetProperty("mount").EnumerateArray().Select(e => e.GetString()).ToArray(),
                };
            return kits;
        }

        private static double D(JsonElement e, params string[] path)
        {
            foreach (var k in path) e = e.GetProperty(k);
            return e.GetDouble();
        }

        private struct Slot { public string Surface; public int Row, Col; }

        private static Slot ParseSlot(Row r)
        {
            var p = r["slot"].Split(':');
            Assert.True(p.Length == 3, r + ": slot '" + r["slot"] + "' is not <surface>:<row>:<col>");
            int row, col;
            bool okRow = int.TryParse(p[1], out row), okCol = int.TryParse(p[2], out col);
            Assert.True(okRow && okCol && row >= 0 && col >= 0, r + ": bad slot numbers '" + r["slot"] + "'");
            Assert.Contains(p[0], new[] { "bench", "vert" });
            return new Slot { Surface = p[0], Row = row, Col = col };
        }

        private static string SectionOf(JsonElement layout, Row r)
        {
            // Longest matching path prefix of system/subsystem/function/device wins.
            string path = r["system"] + "/" + r["subsystem"] + "/" + r["function"] + "/" + r["device"];
            string best = null, section = null;
            foreach (var p in layout.GetProperty("system_sections").EnumerateObject())
            {
                if (p.Name.StartsWith("_")) continue;
                if ((path == p.Name || path.StartsWith(p.Name + "/", StringComparison.Ordinal)) && (best == null || p.Name.Length > best.Length))
                { best = p.Name; section = p.Value.GetString(); }
            }
            return section;
        }

        // Cell size of a placed kit: kit face plus its label plate. Annunciator tiles carry their own text.
        private static void CellSize(JsonElement layout, Kit k, out double w, out double h)
        {
            var g = layout.GetProperty("grid");
            bool plate = !g.GetProperty("no_label_plate_kits").EnumerateArray().Any(e => e.GetString() == k.Id);
            w = plate ? Math.Max(k.W, g.GetProperty("min_plate_width").GetDouble()) : k.W;
            h = plate ? k.H + D(layout, "label_plate", "height") + D(layout, "label_plate", "gap") : k.H;
        }

        [Fact]
        public void EveryRow_HasKtd5Type_AndAKitThatExists_WithMatchingRole()
        {
            var kits = LoadKits();
            foreach (var r in Load())
            {
                Assert.True(TypeKits.ContainsKey(r["physical_type"]), r + ": physical_type '" + r["physical_type"] + "' is not a KTD5 type");
                Assert.True(kits.ContainsKey(r["kit"]), r + ": kit '" + r["kit"] + "' is not in kits.json");
                Assert.Contains(r["kit"], TypeKits[r["physical_type"]]);
                Assert.True(kits[r["kit"]].Role == r["role"], r + ": row role " + r["role"] + " but kit " + r["kit"] + " role is " + kits[r["kit"]].Role);
            }
        }

        [Fact]
        public void EveryKit_AllowsTheRowsSurface_AndOnlyControlsSitOnTheBench()
        {
            var kits = LoadKits();
            foreach (var r in Load())
            {
                var s = ParseSlot(r);
                string mount = s.Surface == "bench" ? "bench" : "vertical";
                Assert.True(kits[r["kit"]].Mount.Contains(mount), r + ": kit " + r["kit"] + " cannot mount on " + s.Surface);
                // Reach (KTD8): controls live on the bench, except the generator breaker switch (R4 puts it beside the synchroscope).
                if (r["role"] == "control" && r.Id != "gen_breaker_sw")
                    Assert.True(s.Surface == "bench", r + ": control not on the bench");
                // Readouts go on the vertical panel; only counters (K04) and lamps (K20) may sit on the bench beside their controls.
                if (r["role"] != "control" && r["kit"] != "K04" && r["kit"] != "K20")
                    Assert.True(s.Surface == "vert", r + ": " + r["role"] + " must be on the vertical panel");
            }
        }

        [Fact]
        public void NoTwoRowsShareASlot()
        {
            var seen = new Dictionary<string, Row>();
            foreach (var r in Load())
            {
                string key = r["section"] + "/" + r["panel"] + "/" + r["slot"];
                Assert.True(r["section"].Length > 0 && r["panel"].Length > 0 && r["slot"].Length > 0, r + ": section, panel or slot empty");
                Row other;
                Assert.False(seen.TryGetValue(key, out other), r + " shares slot " + key + " with " + (other == null ? "" : other.ToString()));
                seen[key] = r;
            }
        }

        [Fact]
        public void EveryPanel_FitsItsGrid()
        {
            var layout = LoadJson("layout.json");
            var kits = LoadKits();
            var rows = Load();
            double gap = D(LoadJson("kits.json"), "spacing", "min_gap");
            double margin = D(layout, "grid", "edge_margin");
            double plateW = D(layout, "title_plate", "width");
            double benchH = D(layout, "bench", "depth");
            int annRows = (int)D(layout, "vertical", "annunciator_rows");
            double annBand = D(layout, "vertical", "usable_to") - D(layout, "vertical", "annunciator_from");
            double instBand = D(layout, "vertical", "annunciator_from") - D(layout, "vertical", "usable_from");

            var widths = new Dictionary<string, double>();
            foreach (var sec in layout.GetProperty("sections").EnumerateArray())
                foreach (var p in sec.GetProperty("panels").EnumerateArray())
                    widths[sec.GetProperty("id").GetString() + "/" + p.GetProperty("id").GetString()] = p.GetProperty("width").GetDouble();

            foreach (var grp in rows.GroupBy(r => r["section"] + "/" + r["panel"]))
            {
                Assert.True(widths.ContainsKey(grp.Key), "panel " + grp.Key + " is used by rows but missing from layout.json");
                double pw = widths[grp.Key];
                Assert.True(pw >= plateW + 2 * margin - 1e-9, grp.Key + ": narrower than its title plate");
                foreach (var surf in grp.GroupBy(r => ParseSlot(r).Surface))
                {
                    double annH = 0, instH = 0, benchUsed = 0;
                    int instRows = 0, benchRows = 0;
                    foreach (var line in surf.GroupBy(r => ParseSlot(r).Row).OrderBy(g => g.Key))
                    {
                        var cells = line.OrderBy(r => ParseSlot(r).Col).ToList();
                        for (int c = 0; c < cells.Count; c++)
                            Assert.True(ParseSlot(cells[c]).Col == c, grp.Key + " " + surf.Key + " row " + line.Key + ": columns are not contiguous from 0");
                        double rowW = 0, rowH = 0;
                        for (int c = 0; c < cells.Count; c++)
                        {
                            double w, h;
                            CellSize(layout, kits[cells[c]["kit"]], out w, out h);
                            rowW += w;
                            rowH = Math.Max(rowH, h);
                            if (c > 0) rowW += (cells[c]["kit"] == "K05" && cells[c - 1]["kit"] == "K05") ? 0 : gap;
                        }
                        Assert.True(rowW + 2 * margin <= pw + 1e-9, grp.Key + " " + surf.Key + " row " + line.Key + ": needs " + (rowW + 2 * margin).ToString("F3") + " m, panel is " + pw.ToString("F3"));
                        if (surf.Key == "bench") { benchUsed += rowH + (benchRows++ > 0 ? gap : 0); }
                        else if (line.Key < annRows)
                        {
                            Assert.True(cells.All(c => c["kit"] == "K05"), grp.Key + ": vert row " + line.Key + " is in the annunciator band but holds non-tile kits");
                            annH += rowH;
                        }
                        else
                        {
                            Assert.True(cells.All(c => c["kit"] != "K05"), grp.Key + ": annunciator tile below the annunciator band");
                            instH += rowH + (instRows++ > 0 ? gap : 0);
                        }
                    }
                    if (surf.Key == "bench") Assert.True(benchUsed <= benchH + 1e-9, grp.Key + ": bench rows need " + benchUsed.ToString("F3") + " m, bench is " + benchH);
                    else
                    {
                        Assert.True(annH <= annBand + 1e-9, grp.Key + ": annunciator rows need " + annH.ToString("F3") + " m, band is " + annBand.ToString("F3"));
                        Assert.True(instH <= instBand + 1e-9, grp.Key + ": instrument rows need " + instH.ToString("F3") + " m, band is " + instBand.ToString("F3"));
                    }
                }
            }
        }

        [Fact]
        public void EachSection_HoldsOnlyItsOwnSystems()
        {
            var layout = LoadJson("layout.json");
            var order = new[] { "reactor", "primary", "safety", "steam", "turbine", "electrical" };
            Assert.Equal(order, layout.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("id").GetString()).ToArray());
            foreach (var r in Load())
            {
                string want = SectionOf(layout, r);
                Assert.True(want != null, r + ": no system_sections entry covers " + r["system"] + "/" + r["subsystem"] + "/" + r["function"] + "/" + r["device"]);
                Assert.True(r["section"] == want, r + ": sits in section '" + r["section"] + "' but its system belongs to '" + want + "'");
            }
            // every panel in layout.json is used
            var used = new HashSet<string>(Load().Select(r => r["section"] + "/" + r["panel"]));
            foreach (var sec in layout.GetProperty("sections").EnumerateArray())
                foreach (var p in sec.GetProperty("panels").EnumerateArray())
                    Assert.Contains(sec.GetProperty("id").GetString() + "/" + p.GetProperty("id").GetString(), used);
        }

        [Fact]
        public void OverviewRows_UseOverviewCapableKits()
        {
            foreach (var r in Load().Where(r => r["overview"] == "y"))
                Assert.Contains(r["kit"], OverviewKits);
            foreach (var r in Load().Where(r => r["overview"] == "y" && r["role"] == "indicator"))
                Assert.True(r["kit"] == "K01L", r + ": overview gauges use K01L");
        }

        [Fact]
        public void SyncPanel_FollowsR4()
        {
            var rows = Load().ToDictionary(r => r.Id);
            var eye = new[] { "synchroscope", "sync_incoming_volts", "sync_running_volts", "sync_incoming_hz", "sync_running_hz", "gen_breaker_sw" };
            foreach (var id in eye)
            {
                Assert.True(rows[id]["panel"] == "sync" && rows[id]["section"] == "electrical", id + " must be in electrical/sync");
                Assert.True(ParseSlot(rows[id]).Surface == "vert", id + " must be on the vertical panel");
            }
            foreach (var id in new[] { "governor_raise", "governor_lower", "voltage_raise", "voltage_lower" })
            {
                Assert.True(rows[id]["panel"] == "sync", id + " must be in electrical/sync");
                Assert.True(ParseSlot(rows[id]).Surface == "bench", id + " must be on the bench below the sync meters");
            }
        }

        [Fact]
        public void Layout_HasRoomArcPointsCamerasAndChecks()
        {
            var layout = LoadJson("layout.json");
            Assert.True(layout.TryGetProperty("_doc", out _));
            Assert.Equal(3.2, D(layout, "room", "ceiling"), 6);
            double radius = D(layout, "arc", "radius"), span = D(layout, "arc", "span_deg");
            Assert.True(span >= 120.0 && span <= 180.0, "arc span " + span + " deg is not a horseshoe between 120 and 180 degrees");
            double cx = layout.GetProperty("arc").GetProperty("center")[0].GetDouble(), cy = layout.GetProperty("arc").GetProperty("center")[1].GetDouble();
            double rw = D(layout, "room", "width"), rd = D(layout, "room", "depth");

            // sections tile the arc in order, every section's panels fit its arc length
            double prev = D(layout, "arc", "start_deg");
            foreach (var sec in layout.GetProperty("sections").EnumerateArray())
            {
                double a0 = sec.GetProperty("start_deg").GetDouble(), a1 = sec.GetProperty("end_deg").GetDouble();
                Assert.True(Math.Abs(a0 - prev) < 1e-6, sec.GetProperty("id").GetString() + ": section does not start where the previous one ends");
                prev = a1;
                double arcLen = radius * (a1 - a0) * Math.PI / 180.0;
                double sum = sec.GetProperty("panels").EnumerateArray().Sum(p => p.GetProperty("width").GetDouble());
                Assert.True(sum <= arcLen + 1e-6, sec.GetProperty("id").GetString() + ": panels need " + sum.ToString("F3") + " m but the section arc is " + arcLen.ToString("F3"));
            }
            Assert.True(Math.Abs(prev - D(layout, "arc", "end_deg")) < 1e-6, "sections do not end at arc end_deg");

            // every operating point is inside the shell
            var ops = layout.GetProperty("operating_points");
            foreach (var name in new[] { "STAND_reactor", "STAND_primary", "STAND_safety", "STAND_steam", "STAND_turbine", "STAND_electrical", "STAND_Center", "SEAT_A1", "SEAT_A2", "SPAWN_A1" })
            {
                Assert.True(ops.TryGetProperty(name, out var op), "missing operating point " + name);
                double x = op.GetProperty("pos")[0].GetDouble(), y = op.GetProperty("pos")[1].GetDouble();
                Assert.True(x > 0 && x < rw && y > 0 && y < rd, name + " is outside the room");
            }
            foreach (var name in new[] { "CAM_Seat_A1", "CAM_Stand_Center", "CAM_Top" })
                Assert.True(layout.GetProperty("cameras").TryGetProperty(name, out _), "missing camera " + name);

            // the whole board (plus the panel depth behind it) is inside the shell
            foreach (double deg in new[] { D(layout, "arc", "start_deg"), 0.0, D(layout, "arc", "end_deg") })
            {
                double x = cx + (radius + 0.45) * Math.Sin(deg * Math.PI / 180), y = cy + (radius + 0.45) * Math.Cos(deg * Math.PI / 180);
                Assert.True(x > 0 && x < rw && y > 0 && y < rd, "board at " + deg + " deg is outside the room");
            }

            Assert.Equal(0.7, D(layout, "checks", "reach_m"), 6);
            Assert.Equal(45.0, D(layout, "checks", "min_view_angle_deg"), 6);
            Assert.Equal(0.9, D(layout, "checks", "min_text_deg"), 6);
            Assert.Equal(2.0, D(layout, "interaction_range_m"), 6);
            Assert.True(layout.GetProperty("checks").GetProperty("viewpoints").TryGetProperty("overview", out _));
        }

        [Fact]
        public void Panels_AreWithinReach_AndViewableFromTheirOperatingPoint()
        {
            var layout = LoadJson("layout.json");
            double radius = D(layout, "arc", "radius");
            double reach = D(layout, "checks", "reach_m"), minAngle = D(layout, "checks", "min_view_angle_deg");
            double horizBench = D(layout, "bench", "depth") * Math.Cos(D(layout, "bench", "slope_deg") * Math.PI / 180);
            Assert.True(horizBench <= reach, "bench is deeper than the reach threshold");
            double cx = layout.GetProperty("arc").GetProperty("center")[0].GetDouble(), cy = layout.GetProperty("arc").GetProperty("center")[1].GetDouble();
            foreach (var sec in layout.GetProperty("sections").EnumerateArray())
            {
                string id = sec.GetProperty("id").GetString();
                var op = layout.GetProperty("operating_points").GetProperty("STAND_" + id).GetProperty("pos");
                double ox = op[0].GetDouble(), oy = op[1].GetDouble();
                double a = sec.GetProperty("start_deg").GetDouble();
                foreach (var p in sec.GetProperty("panels").EnumerateArray())
                {
                    double w = p.GetProperty("width").GetDouble(), da = w / radius * 180 / Math.PI;
                    foreach (double deg in new[] { a, a + da / 2, a + da })
                    {
                        double rad = deg * Math.PI / 180;
                        double px = cx + radius * Math.Sin(rad), py = cy + radius * Math.Cos(rad);
                        double nx = -Math.Sin(rad), ny = -Math.Cos(rad);       // panel normal, toward the centre
                        double sx = ox - px, sy = oy - py, len = Math.Sqrt(sx * sx + sy * sy);
                        double fromNormal = Math.Acos(Math.Max(-1, Math.Min(1, (sx * nx + sy * ny) / len))) * 180 / Math.PI;
                        Assert.True(90 - fromNormal >= minAngle - 1e-6, id + "/" + p.GetProperty("id").GetString() + ": " + (90 - fromNormal).ToString("F1") + " deg to the display plane from STAND_" + id);
                    }
                    a += da;
                }
            }
        }

        // ---- U17: descriptions, runtime json, mode range ------------------------------------------------

        [Fact]
        public void EveryRow_HasADescription()
        {
            foreach (var r in Load())
                Assert.False(string.IsNullOrWhiteSpace(r["description"]), r + " has no description (the hover tooltip needs one)");
        }

        [Fact]
        public void OperatingModeDial_RangeMatchesWhatThePlantAccepts()
        {
            // Plant.Apply accepts modes 0..2 (Shutdown, Nominal, Maximum): a wider dial would send values the plant ignores.
            int top = Enum.GetValues(typeof(OperatingMode)).Length - 1;
            var rows = Load().Where(r => r["binding"].StartsWith("intent:OperatingMode")).ToList();
            Assert.NotEmpty(rows);
            foreach (var r in rows)
                Assert.Equal("0.." + top, r["range_or_positions"]);
        }

        [Fact]
        public void RuntimeInventoryJson_MatchesTheCsv()
        {
            // StreamingAssets/inventory-runtime.json is generated by scripts/inventory-rollup.py; a stale copy fails here.
            string path = Path.Combine(BaseDir, "StreamingAssets", "inventory-runtime.json");
            Assert.True(File.Exists(path), "inventory-runtime.json not copied to test output");
            var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var json = doc.RootElement.GetProperty("rows").EnumerateArray().ToList();
            var rows = Load();
            Assert.Equal(rows.Count, json.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var j = json[i];
                var r = rows[i];
                Assert.Equal(r.Id, j.GetProperty("id").GetString());
                Assert.Equal(r["label"], j.GetProperty("label").GetString());
                Assert.Equal(r["short_label"], j.GetProperty("shortLabel").GetString());
                Assert.Equal(r["units"], j.GetProperty("units").GetString());
                Assert.Equal(r["range_or_positions"], j.GetProperty("range").GetString());
                Assert.Equal(r["description"].Trim(), j.GetProperty("description").GetString());
                Assert.Equal(r["kit"], j.GetProperty("kit").GetString());
                Assert.Equal(r["role"], j.GetProperty("role").GetString());
            }
        }

        [Fact]
        public void AcknowledgeButton_IsABoundSafetyPushbuttonPairedWithAnAnnunciator()
        {
            var r = Load().Single(x => x.Id == "alarm_ack_btn");
            Assert.Equal("intent:AcknowledgeAlarms:Alarms", r["binding"]);
            Assert.Equal("bound", r["readiness"]);
            Assert.Equal("safety", r["section"]);
            Assert.Equal("pushbutton", r["physical_type"]);
            Assert.Equal("annunciator_tile", Load().Single(x => x.Id == r["paired_indicator"])["physical_type"]);
        }
    }
}
