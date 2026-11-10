using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ReactorSim.Ui;

// Pure logic (no UnityEngine): the text shown on each CRT_* screen. Every page is a fixed grid (Columns x RowBudget):
// header band (power / reactivity, so any screen proves the plant is live), a title and clock row, a status row
// (glyph plus word), then big key readings and supporting lines in priority order. Linked into ReactorSim.Core.Tests.
namespace ReactorSim.Instruments
{
    // Rolling window of recent samples for the trends page; one sample per distinct sim time.
    public sealed class TrendBuffer
    {
        public enum Series { Power, FuelTemp, Pressure, Reactivity }

        private readonly double[][] data;
        private readonly int capacity;
        private int head;
        private double lastSim;
        private bool hasLast;

        public int Count { get; private set; }

        public TrendBuffer(int capacity)
        {
            this.capacity = Math.Max(2, capacity);
            data = new double[4][];
            for (int i = 0; i < 4; i++) data[i] = new double[this.capacity];
        }

        public void Push(PlantSnapshot s)
        {
            if (s == null) return;
            if (hasLast && s.SimSeconds == lastSim) return;
            hasLast = true;
            lastSim = s.SimSeconds;
            data[(int)Series.Power][head] = s.Power;
            data[(int)Series.FuelTemp][head] = s.FuelTempC;
            data[(int)Series.Pressure][head] = s.PrimaryPressureBar;
            data[(int)Series.Reactivity][head] = s.TotalReactivityPcm;
            head = (head + 1) % capacity;
            if (Count < capacity) Count++;
        }

        private double At(Series series, int ageFromOldest)
        {
            int start = (head - Count + capacity * 2) % capacity;
            return data[(int)series][(start + ageFromOldest) % capacity];
        }

        // ASCII sparkline of the last `width` samples, left-padded with spaces while history is short.
        public string Sparkline(Series series, int width)
        {
            const string ramp = " .:-=+*#%@";
            int n = Math.Min(width, Count);
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = Count - n; i < Count; i++)
            {
                double v = At(series, i);
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            var sb = new StringBuilder(width);
            sb.Append(' ', width - n);
            for (int i = Count - n; i < Count; i++)
            {
                double v = At(series, i);
                int level = hi > lo ? (int)Math.Round((v - lo) / (hi - lo) * (ramp.Length - 2)) + 1 : 1;
                sb.Append(ramp[Math.Max(1, Math.Min(ramp.Length - 1, level))]);
            }
            return sb.ToString();
        }

        public double Latest(Series series)
        {
            return Count > 0 ? At(series, Count - 1) : double.NaN;
        }
    }


    /// <summary>One row of a screen page. Key rows are drawn KeyScale times larger (big numerics).</summary>
    public sealed class CrtLine
    {
        public string Text;
        public bool Key;
        public CrtLine(string text, bool key) { Text = text; Key = key; }
    }

    /// <summary>A page: header band, title row, body rows, status row. Pure data; ToText makes the TextMeshPro string.</summary>
    public sealed class CrtPage
    {
        public readonly List<CrtLine> Lines = new List<CrtLine>();

        /// <summary>Rows consumed: a key row costs KeyScale rows.</summary>
        public double RowCost
        {
            get
            {
                double c = 0.0;
                foreach (var l in Lines) c += l.Key ? CrtPages.KeyScale : 1.0;
                return c;
            }
        }

        /// <summary>Widest row in character cells; a key row's characters are KeyScale cells wide.</summary>
        public double MaxCellWidth
        {
            get
            {
                double w = 0.0;
                foreach (var l in Lines) w = Math.Max(w, l.Text.Length * (l.Key ? CrtPages.KeyScale : 1.0));
                return w;
            }
        }

        public string ToText()
        {
            var sb = new StringBuilder(256);
            for (int i = 0; i < Lines.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                var l = Lines[i];
                if (l.Key) sb.Append("<size=").Append((int)Math.Round(CrtPages.KeyScale * 100.0)).Append("%>").Append(l.Text).Append("</size>");
                else sb.Append(l.Text);
            }
            return sb.ToString();
        }
    }

    public static class CrtPages
    {
        /// <summary>Character cells per row. Sized so the widest row at the default window leaves glyphs readable
        /// from a focused view; see PixelBudget.</summary>
        public const int Columns = 28;
        /// <summary>Row budget of a page, counting a key row as KeyScale rows.</summary>
        public const double RowBudget = 10.0;
        /// <summary>Big-numeric rows are this much larger than body rows (18 px key vs 10 px body).</summary>
        public const double KeyScale = 1.8;

        public static readonly string[] KnownPages =
        {
            "CRT_A1_Core", "CRT_A1_Chem", "CRT_A1_Log", "CRT_A2_Primary", "CRT_A2_Secondary",
            "CRT_A2_Turbine", "CRT_AssistantTerminal", "CRT_MimicStatusBoard",
        };

        public static bool IsKnownPage(string screenName)
        {
            return Array.IndexOf(KnownPages, screenName) >= 0;
        }

        // ---- row helpers ----

        /// <summary>Shortens to width characters, ending in ".." when it had to cut.</summary>
        public static string Fit(string text, int width)
        {
            if (text == null) return "";
            text = text.Replace('\n', ' ').Replace('<', '(').Replace('>', ')');
            if (text.Length <= width) return text;
            return width <= 2 ? text.Substring(0, Math.Max(0, width)) : text.Substring(0, width - 2) + "..";
        }

        private static string Pad(string left, string right, int width)
        {
            right = Fit(right, Math.Max(0, width - left.Length - 1));
            return left + new string(' ', Math.Max(1, width - left.Length - right.Length)) + right;
        }

        private static string Num(double v, int decimals) { return ReadoutFormat.Number(v, decimals); }

        private static void Body(CrtPage p, string text) { p.Lines.Add(new CrtLine(Fit(text, Columns), false)); }

        // A key row must fit in Columns / KeyScale characters.
        private static void Key(CrtPage p, string text)
        {
            p.Lines.Add(new CrtLine(Fit(text, (int)Math.Floor(Columns / KeyScale)), true));
        }

        // Same header on every page so any screen proves the plant is live.
        private static string Header(PlantSnapshot s)
        {
            return Fit("PWR " + ReadoutFormat.PowerMWt(s.Power * ReactorSim.Physics.RatedThermalMWt)
                + "  RHO " + ReadoutFormat.ReactivityPcm(s.TotalReactivityPcm), Columns);
        }

        // Alarm and status first: a glyph and a word, never colour alone.
        public static string Status(PlantSnapshot s)
        {
            if (s.Scrammed) return "[XX] SCRAM";
            if (s.ReliefOpen) return "[!!] PORV OPEN";
            if (s.SecondaryReliefOpen) return "[!!] SG RELIEF OPEN";
            if (s.TurbineTripped) return "[!!] TURBINE TRIP";
            return "[OK] NO TRIP OR RELIEF";
        }

        private static string Period(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0.0 || seconds > 9999.0) return "STABLE";
            return ReadoutFormat.Value(seconds, 0, ReadoutFormat.Seconds);
        }

        private static string Rod(PlantSnapshot s, int i)
        {
            double ins = s.BankInsertion != null && i < s.BankInsertion.Length ? s.BankInsertion[i] : double.NaN;
            return (char)('A' + i) + ReadoutFormat.Number(ins * 100.0, 0);
        }

        private static CrtPage Begin(string title, PlantSnapshot s, ClockState clock)
        {
            var p = new CrtPage();
            p.Lines.Add(new CrtLine(Header(s), false));
            p.Lines.Add(new CrtLine(Pad(title, ClockReadout.Format(clock), Columns), false));
            p.Lines.Add(new CrtLine(Status(s), false));
            return p;
        }

        /// <summary>The structured page: priority order is status, primary variable, supporting values.</summary>
        public static CrtPage BuildPage(string screenName, PlantSnapshot s, ClockState clock, TrendBuffer trends,
            IList<string> terminalLines)
        {
            CrtPage p;
            switch (screenName)
            {
                case "CRT_A1_Core":
                    p = Begin("REACTOR CORE", s, clock);
                    Key(p, "FUEL " + ReadoutFormat.TemperatureC(s.FuelTempC, 0));
                    Key(p, "PRI " + ReadoutFormat.PressureBar(s.PrimaryPressureBar));
                    Body(p, "PERIOD " + Period(s.PeriodSeconds));
                    Body(p, "ROD% " + Rod(s, 0) + " " + Rod(s, 1) + " " + Rod(s, 2) + " " + Rod(s, 3));
                    Body(p, "MODE " + s.Mode + (s.Scrammed ? "" : (s.IsCritical ? "  CRITICAL" : "  SUBCRITICAL")));
                    break;
                case "CRT_A1_Chem":
                    p = Begin("CHEMISTRY", s, clock);
                    Key(p, "BORON " + ReadoutFormat.BoronPpm(s.BoronPpm));
                    Key(p, "pH " + Num(s.Ph, 2));
                    Body(p, "XENON " + ReadoutFormat.ReactivityPcm(s.XenonPcm));
                    Body(p, "IODINE " + ReadoutFormat.ReactivityPcm(s.IodinePcm));
                    Body(p, "DILUTE " + Num(s.DiluteRate, 2) + "  BORATE " + Num(s.BorateRate, 2));
                    break;
                case "CRT_A1_Log":
                    p = Begin("TRENDS", s, clock);
                    const int spark = Columns - 5;
                    Body(p, "PWR  " + (trends != null ? trends.Sparkline(TrendBuffer.Series.Power, spark) : ""));
                    Body(p, "FUEL " + (trends != null ? trends.Sparkline(TrendBuffer.Series.FuelTemp, spark) : ""));
                    Body(p, "PRI  " + (trends != null ? trends.Sparkline(TrendBuffer.Series.Pressure, spark) : ""));
                    Body(p, "RHO  " + (trends != null ? trends.Sparkline(TrendBuffer.Series.Reactivity, spark) : ""));
                    Body(p, "OLDEST > NEWEST  T+" + ReadoutFormat.Value(s.SimSeconds, 0, ReadoutFormat.Seconds));
                    break;
                case "CRT_A2_Primary":
                    p = Begin("PRIMARY", s, clock);
                    Key(p, "PRI " + ReadoutFormat.PressureBar(s.PrimaryPressureBar));
                    Key(p, "TEMP " + ReadoutFormat.TemperatureC(s.PrimaryTempC));
                    Body(p, "SUBCOOL " + ReadoutFormat.TemperatureC(s.PrimarySatMarginC));
                    Body(p, "LEVEL " + ReadoutFormat.Fraction(s.PrimaryLevelFrac) + "  FLOW " + ReadoutFormat.Fraction(s.PrimaryFlowFrac));
                    Body(p, "HEAT " + ReadoutFormat.Fraction(s.HeaterPowerFrac) + "  SPRAY " + ReadoutFormat.Fraction(s.SprayFrac));
                    break;
                case "CRT_A2_Secondary":
                    p = Begin("SECONDARY", s, clock);
                    Key(p, "SEC " + ReadoutFormat.PressureBar(s.SecondaryPressureBar));
                    Key(p, "SG " + ReadoutFormat.Fraction(s.SecondaryLevelFrac));
                    Body(p, "STEAM " + ReadoutFormat.Fraction(s.SteamDemandFrac) + "  DUMP " + ReadoutFormat.Fraction(s.SteamDumpFrac));
                    Body(p, "FEEDWATER " + ReadoutFormat.Fraction(s.FeedwaterFlowFrac));
                    break;
                case "CRT_A2_Turbine":
                    p = Begin("TURBINE", s, clock);
                    Key(p, ReadoutFormat.TurbineRpm(s.TurbineRpm));
                    Key(p, ReadoutFormat.PowerMWe(s.GeneratorKw / 1000.0));
                    Body(p, s.GeneratorSynced ? "ON GRID" : "OFF GRID");
                    Body(p, "GRID " + ReadoutFormat.FrequencyHz(s.GridFrequencyHz));
                    Body(p, "CONDENSER " + ReadoutFormat.Value(s.CondenserPressureBar, 2, ReadoutFormat.Bar));
                    break;
                case "CRT_AssistantTerminal":
                    p = Begin("ASSISTANT", s, clock);
                    Body(p, "ASSIST LEVEL " + s.AssistTier);
                    if (terminalLines == null || terminalLines.Count == 0)
                        Body(p, "- ALL QUIET");
                    else
                    {
                        // A repeated advisory prints once: the screen shows what changed, not how often it was said.
                        string previous = null;
                        for (int i = Math.Max(0, terminalLines.Count - 5); i < terminalLines.Count; i++)
                        {
                            if (terminalLines[i] == previous) continue;
                            previous = terminalLines[i];
                            Body(p, "- " + terminalLines[i]);
                        }
                    }
                    break;
                case "CRT_MimicStatusBoard":
                    p = Begin("STATUS", s, clock);
                    Key(p, s.Scrammed ? "SCRAM" : (s.IsCritical ? "CRITICAL" : "SHUTDOWN"));
                    Body(p, "CLOCK " + ClockReadout.Format(clock));
                    break;
                default:
                    // A screen with no page: standby content only, no live plant rows. The out-of-service tag sits over the
                    // middle of the face, so the text stays in the upper and lower thirds with blank rows between.
                    p = new CrtPage();
                    Key(p, "STANDBY");
                    Body(p, "NO SIGNAL");
                    Body(p, "");
                    Body(p, "");
                    Body(p, "");
                    Body(p, "");
                    Body(p, "NOT ASSIGNED");
                    Body(p, "SEE CORE, PRIMARY, STATUS");
                    break;
            }
            return p;
        }

        public static string Build(string screenName, PlantSnapshot s, ClockState clock, TrendBuffer trends,
            IList<string> terminalLines)
        {
            return BuildPage(screenName, s, clock, trends, terminalLines).ToText();
        }
    }
}
