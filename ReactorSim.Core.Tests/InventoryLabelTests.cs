using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace ReactorSim.Core.Tests
{
    // Naming rules for the engraved plates and tooltips: design/control-room/inventory.csv is the one source of labels.
    public class InventoryLabelTests
    {
        public const int MaxShortLabel = 16;
        public const int MaxLabel = 40;

        private sealed class Entry
        {
            public string Id, Label, ShortLabel, Panel, Role;
            public override string ToString() { return Id + " (" + Label + ")"; }
        }

        private static List<Entry> Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "design", "control-room", "inventory.csv");
            Assert.True(File.Exists(path), "inventory.csv not copied to test output: " + path);
            var records = ReactorSim.Csv.CsvReader.Parse(File.ReadAllText(path, Encoding.UTF8));
            var header = records[0];
            int Col(string n) { int i = header.IndexOf(n); Assert.True(i >= 0, "missing column " + n); return i; }
            int id = Col("id"), label = Col("label"), shortLabel = Col("short_label"), panel = Col("panel"), role = Col("role");
            var list = new List<Entry>();
            for (int i = 1; i < records.Count; i++)
            {
                var r = records[i];
                list.Add(new Entry { Id = r[id], Label = r[label], ShortLabel = r[shortLabel], Panel = r[panel], Role = r[role] });
            }
            return list;
        }

        [Fact]
        public void Labels_AreUniqueAcrossTheInventory()
        {
            var dupes = Load().GroupBy(e => e.Label.Trim().ToLowerInvariant()).Where(g => g.Count() > 1)
                .Select(g => g.Key + " <- " + string.Join(", ", g.Select(e => e.Id))).ToList();
            Assert.True(dupes.Count == 0, "duplicate labels: " + string.Join("; ", dupes));
        }

        [Fact]
        public void ShortLabels_AreUniqueWithinAPanel()
        {
            var dupes = Load().GroupBy(e => e.Panel + "|" + e.ShortLabel.Trim().ToUpperInvariant()).Where(g => g.Count() > 1)
                .Select(g => g.Key + " <- " + string.Join(", ", g.Select(e => e.Id))).ToList();
            Assert.True(dupes.Count == 0, "duplicate short labels in a panel: " + string.Join("; ", dupes));
        }

        [Fact]
        public void ShortLabels_AreUppercase_AndFitThePlate()
        {
            foreach (var e in Load())
            {
                string s = e.ShortLabel;
                Assert.True(s.Length > 0 && s.Length <= MaxShortLabel, e + ": short label '" + s + "' is " + s.Length + " characters, budget " + MaxShortLabel);
                Assert.True(s == s.ToUpperInvariant(), e + ": short label '" + s + "' is not uppercase");
                Assert.True(s == s.Trim() && !s.Contains("  "), e + ": short label has stray spaces");
            }
        }

        [Fact]
        public void Labels_AreTidySentenceCase()
        {
            foreach (var e in Load())
            {
                string l = e.Label;
                Assert.True(l.Length > 0 && l.Length <= MaxLabel, e + ": label length " + l.Length);
                Assert.True(l == l.Trim() && !l.Contains("  "), e + ": stray spaces in label");
                Assert.False(l.EndsWith(".") || l.EndsWith(":"), e + ": label ends in punctuation");
                Assert.True(char.IsUpper(l[0]) || char.IsDigit(l[0]) || l.StartsWith("pH "), e + ": label should start with a capital");
                Assert.DoesNotContain("TODO", l, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("tbd", l, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
