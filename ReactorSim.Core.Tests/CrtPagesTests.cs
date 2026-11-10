using System;
using System.Collections.Generic;
using System.Linq;
using ReactorSim.Instruments;
using Xunit;

namespace ReactorSim.Tests
{
    // Playtest-2 U4: every screen page fits the fixed column and row budget at idle, at power and in alarm,
    // and an alarm is a glyph plus a word, not colour alone. Pure, runs on Linux.
    public class CrtPagesTests
    {
        private static PlantSnapshot Idle() { return new PlantSnapshot(); }

        private static PlantSnapshot AtPower()
        {
            return new PlantSnapshot
            {
                Power = 1.0, TotalReactivityPcm = 12.0, FuelTempC = 612.0, PrimaryTempC = 302.3, PrimaryPressureBar = 155.0,
                PrimaryLevelFrac = 0.55, PrimaryFlowFrac = 1.0, BoronPpm = 1200.0, Ph = 7.2, TurbineRpm = 3600.0,
                GeneratorKw = 50000.0, GeneratorSynced = true, GridFrequencyHz = 50.0, SecondaryPressureBar = 60.0,
                SecondaryLevelFrac = 0.5, SimSeconds = 1234.0, IsCritical = true,
                BankInsertion = new[] { 1.0, 1.0, 1.0, 1.0 },
            };
        }

        // Longest value each field can print: many digits, negative sign, a long unit string.
        private static PlantSnapshot Extreme()
        {
            var s = AtPower();
            s.Power = 62.4;                       // 9984 MWt
            s.TotalReactivityPcm = -99999.0;
            s.FuelTempC = -1234.5;
            s.PrimaryPressureBar = 1234.5;
            s.PrimarySatMarginC = -1234.5;
            s.TurbineRpm = 99999.0;
            s.GeneratorKw = 9999999.0;
            s.BoronPpm = 99999.0;
            s.XenonPcm = -99999.0;
            s.IodinePcm = -99999.0;
            s.PeriodSeconds = 9999.0;
            s.SecondaryPressureBar = 999.9;
            s.Mode = "PowerOperation";
            s.AssistTier = 3;
            s.ActiveMissionIndex = 12;
            return s;
        }

        private static PlantSnapshot Alarm()
        {
            var s = AtPower();
            s.Scrammed = true;
            s.ReliefOpen = true;
            s.SecondaryReliefOpen = true;
            s.TurbineTripped = true;
            return s;
        }

        private static IEnumerable<PlantSnapshot> States() { return new[] { Idle(), AtPower(), Extreme(), Alarm() }; }

        private static readonly string[] LongTerminal =
        {
            "[ 123.4m OPS] boron dilution in progress while the primary loop is far from equilibrium",
            "[ 124.0m OPS] a second very long advisory line that must be cut, not wrapped",
            "[ 125.0m OPS] third", "[ 126.0m OPS] fourth", "[ 127.0m OPS] fifth", "[ 128.0m OPS] sixth",
        };

        private static CrtPage Page(string name, PlantSnapshot s, ClockState clock)
        {
            var trends = new TrendBuffer(40);
            for (int i = 0; i < 40; i++) { s.SimSeconds = i + 1; trends.Push(s); }
            return CrtPages.BuildPage(name, s, clock, trends, LongTerminal);
        }

        private static ClockState LongClock()
        {
            return new ClockState { AutoSlow = true, AutoSlowReason = "xenon transient at the limit", EffectiveScale = 0.25, Scale = 10 };
        }

        [Fact]
        public void EveryPage_FitsColumnsAndRowBudget_InEveryState()
        {
            foreach (var name in CrtPages.KnownPages)
                foreach (var s in States())
                    foreach (var clock in new[] { new ClockState { Scale = 1.0 }, LongClock(), new ClockState { Paused = true, Scale = 10.0 } })
                    {
                        var p = Page(name, s, clock);
                        foreach (var l in p.Lines)
                            Assert.True(l.Text.Length * (l.Key ? CrtPages.KeyScale : 1.0) <= CrtPages.Columns + 1e-9,
                                name + ": row too wide (" + l.Text.Length + "): " + l.Text);
                        Assert.True(p.RowCost <= CrtPages.RowBudget + 1e-9, name + " rows " + p.RowCost);
                        Assert.True(p.Lines.Count(l => l.Key) <= 2, name + " has too many big readouts");
                    }
        }

        [Fact]
        public void HeaderBand_LeadsEveryPage_WithPowerAndReactivity()
        {
            foreach (var name in CrtPages.KnownPages)
            {
                var p = Page(name, AtPower(), new ClockState { Scale = 1.0 });
                Assert.StartsWith("PWR 160 MWt", p.Lines[0].Text);
                Assert.Contains("RHO 12 pcm", p.Lines[0].Text);
                Assert.False(p.Lines[0].Key);
            }
        }

        [Fact]
        public void StatusRow_IsSecondRowAfterTitle_AndAlarmHasGlyphAndWord()
        {
            foreach (var name in CrtPages.KnownPages)
            {
                var ok = Page(name, AtPower(), new ClockState { Scale = 1.0 });
                Assert.StartsWith("[OK]", ok.Lines[2].Text);
                var bad = Page(name, Alarm(), new ClockState { Scale = 1.0 });
                Assert.StartsWith("[XX] SCRAM", bad.Lines[2].Text);
            }
            var relief = AtPower(); relief.ReliefOpen = true;
            Assert.Contains("[!!] PORV OPEN", CrtPages.Status(relief));
            var trip = AtPower(); trip.TurbineTripped = true;
            Assert.Contains("[!!] TURBINE TRIP", CrtPages.Status(trip));
        }

        [Fact]
        public void KeyReadings_CarryTheirUnits()
        {
            var core = Page("CRT_A1_Core", AtPower(), new ClockState { Scale = 1.0 });
            Assert.Contains(core.Lines, l => l.Key && l.Text.Contains("°C"));
            Assert.Contains(core.Lines, l => l.Key && l.Text.Contains("bar"));
            var chem = Page("CRT_A1_Chem", AtPower(), new ClockState { Scale = 1.0 });
            Assert.Contains(chem.Lines, l => l.Key && l.Text.Contains("1200 ppm"));
            var turb = Page("CRT_A2_Turbine", AtPower(), new ClockState { Scale = 1.0 });
            Assert.Contains(turb.Lines, l => l.Key && l.Text == "3600 rpm");
            Assert.Contains(turb.Lines, l => l.Key && l.Text == "50.0 MWe");
        }

        [Fact]
        public void Text_UsesSizeMarkupOnlyForKeyRows_AndNeverLeaksRichTextFromData()
        {
            var p = CrtPages.BuildPage("CRT_AssistantTerminal", AtPower(), new ClockState { Scale = 1.0 }, new TrendBuffer(4),
                new[] { "<b>bold</b> <color=red>x" });
            string text = p.ToText();
            Assert.DoesNotContain("<b>", text);
            Assert.DoesNotContain("<color", text);
            var core = CrtPages.Build("CRT_A1_Core", AtPower(), new ClockState { Scale = 1.0 }, new TrendBuffer(4), new string[0]);
            Assert.Contains("<size=180%>", core);
            Assert.Equal(core.Split('\n').Length, Page("CRT_A1_Core", AtPower(), new ClockState { Scale = 1.0 }).Lines.Count);
        }

        [Fact]
        public void Fit_CutsWithEllipsis_AndKeepsShortText()
        {
            Assert.Equal("short", CrtPages.Fit("short", 10));
            Assert.Equal("abcdefg..", CrtPages.Fit("abcdefghijkl", 9));
            Assert.Equal("a b", CrtPages.Fit("a\nb", 9));
            Assert.Equal("", CrtPages.Fit(null, 5));
        }

        [Fact]
        public void MissingSnapshotArrays_DoNotThrow_AndShowDashes()
        {
            var s = Idle();
            s.BankInsertion = null;
            string text = CrtPages.Build("CRT_A1_Core", s, new ClockState { Scale = 1.0 }, null, null);
            Assert.Contains("A--", text);
        }

        [Fact]
        public void UnknownScreen_StillBuildsAHeaderAndStatus()
        {
            var p = CrtPages.BuildPage("CRT_Nope", AtPower(), new ClockState { Scale = 1.0 }, null, null);
            Assert.True(p.Lines.Count >= 3);
            Assert.True(p.RowCost <= CrtPages.RowBudget);
        }
    }
}
