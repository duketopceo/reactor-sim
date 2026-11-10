using System;
using System.Collections.Generic;
using System.Linq;
using ReactorSim.Instruments;
using Xunit;

namespace ReactorSim.Tests
{
    // Visual-polish U2: the page grid is the same on every screen (header, title and clock, status, then readings),
    // titles read as titles, and no page prints placeholder or debug text. Pure, runs on Linux.
    public class CrtPageLayoutTests
    {
        private static readonly string[] Forbidden = { "NaN", "Infinity", "null", "TODO", "debug", "(U8)", "SCREEN", "undefined" };

        private static PlantSnapshot Cold() { return new PlantSnapshot(); }

        private static PlantSnapshot Running()
        {
            return new PlantSnapshot
            {
                Power = 0.62, TotalReactivityPcm = 4.0, FuelTempC = 540.0, PrimaryTempC = 291.0, PrimaryPressureBar = 155.0,
                PrimaryLevelFrac = 0.55, PrimaryFlowFrac = 1.0, BoronPpm = 900.0, Ph = 7.1, TurbineRpm = 3000.0,
                GeneratorKw = 30000.0, GeneratorSynced = true, GridFrequencyHz = 50.0, SecondaryPressureBar = 58.0,
                SecondaryLevelFrac = 0.5, SimSeconds = 3600.0, IsCritical = true, BankInsertion = new[] { 1.0, 1.0, 0.6, 0.4 },
            };
        }

        private static PlantSnapshot Scrammed()
        {
            var s = Running();
            s.Scrammed = true;
            s.TurbineTripped = true;
            return s;
        }

        private static IEnumerable<PlantSnapshot> States() { return new[] { Cold(), Running(), Scrammed() }; }

        private static CrtPage Build(string name, PlantSnapshot s)
        {
            var trends = new TrendBuffer(40);
            for (int i = 0; i < 40; i++) { s.SimSeconds += 5; trends.Push(s); }
            return CrtPages.BuildPage(name, s, new ClockState { Scale = 1.0 }, trends, new[] { "[ 1.0m OPS] check pumps" });
        }

        [Fact]
        public void EveryPage_StartsWithHeaderTitleAndStatus_InThatOrder()
        {
            foreach (var name in CrtPages.KnownPages)
                foreach (var s in States())
                {
                    var p = Build(name, s);
                    Assert.True(p.Lines.Count >= 4, name + " has no body");
                    Assert.StartsWith("PWR ", p.Lines[0].Text);
                    Assert.Contains("RHO ", p.Lines[0].Text);
                    Assert.True(p.Lines[1].Text.Length > 0 && char.IsUpper(p.Lines[1].Text[0]), name + " title row");
                    Assert.Matches(@"^\[(OK|!!|XX)\] [A-Z]", p.Lines[2].Text);
                    Assert.False(p.Lines[0].Key || p.Lines[1].Key || p.Lines[2].Key, name + ": only readings may be big");
                }
        }

        [Fact]
        public void TitleRow_IsAnUppercaseTitleWithTheClockAtTheRightEdge()
        {
            foreach (var name in CrtPages.KnownPages)
            {
                var row = Build(name, Running()).Lines[1].Text;
                Assert.Equal(CrtPages.Columns, row.Length);
                string title = row.Substring(0, row.IndexOf("  ", StringComparison.Ordinal));
                Assert.Equal(title.ToUpperInvariant(), title);
                Assert.True(row.TrimEnd().Length > title.Length, name + ": clock missing from title row '" + row + "'");
            }
        }

        [Fact]
        public void NoPage_PrintsPlaceholderOrDebugText()
        {
            foreach (var name in CrtPages.KnownPages)
                foreach (var s in States())
                {
                    string text = Build(name, s).ToText();
                    foreach (var bad in Forbidden)
                        Assert.False(text.Contains(bad), name + " contains '" + bad + "':\n" + text);
                }
        }

        [Fact]
        public void KeyReadings_AlwaysCarryAUnitOrAName()
        {
            foreach (var name in CrtPages.KnownPages)
                foreach (var l in Build(name, Running()).Lines.Where(l => l.Key))
                    Assert.Matches(@"[A-Za-z%°]", l.Text);
        }

        [Fact]
        public void RowsNeverEndInTrailingSpaces()
        {
            foreach (var name in CrtPages.KnownPages)
                foreach (var s in States())
                    foreach (var l in Build(name, s).Lines)
                    {
                        Assert.Equal(l.Text.TrimEnd(), l.Text);
                    }
        }

        [Fact]
        public void AssistantPage_ListsPlainBullets_NotTagsOrMarkupArtifacts()
        {
            var trends = new TrendBuffer(10);
            var quiet = CrtPages.BuildPage("CRT_AssistantTerminal", Running(), new ClockState { Scale = 1.0 }, trends, new string[0]);
            Assert.Contains(quiet.Lines, l => l.Text == "- ALL QUIET");
            var busy = CrtPages.BuildPage("CRT_AssistantTerminal", Running(), new ClockState { Scale = 1.0 }, trends,
                new[] { "Start coolant pumps", "Raise heater power" });
            string text = busy.ToText();
            Assert.Contains("- Start coolant pumps", text);
            Assert.DoesNotContain(") ", text.Replace("RELIEF)", ""));   // a '>' bullet used to be escaped into ')'
            Assert.DoesNotContain("PROC", text);
            Assert.DoesNotContain("TIER", text);
            Assert.DoesNotContain("MISSION ", text);
        }

        [Fact]
        public void AssistantPage_PrintsARepeatedAdvisoryOnce()
        {
            var page = CrtPages.BuildPage("CRT_AssistantTerminal", Running(), new ClockState { Scale = 1.0 }, new TrendBuffer(10),
                new[] { "Start pumps", "Start pumps", "Raise heaters", "Start pumps" });
            Assert.Equal(3, page.Lines.Count(l => l.Text.StartsWith("- ")));
        }
    }
}
