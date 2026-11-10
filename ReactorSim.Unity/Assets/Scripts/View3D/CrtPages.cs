using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Pure logic (no UnityEngine): the text shown on each CRT_* screen. Every page starts with the same
// power / reactivity header line so any screen proves the plant is live. Linked into ReactorSim.Core.Tests.
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

    public static class CrtPages
    {
        public static readonly string[] KnownPages =
        {
            "CRT_A1_Core", "CRT_A1_Chem", "CRT_A1_Log", "CRT_A2_Primary", "CRT_A2_Secondary",
            "CRT_A2_Turbine", "CRT_AssistantTerminal", "CRT_MimicStatusBoard",
        };

        public static bool IsKnownPage(string screenName)
        {
            return Array.IndexOf(KnownPages, screenName) >= 0;
        }

        private static string F(double v, string fmt)
        {
            return v.ToString(fmt, CultureInfo.InvariantCulture);
        }

        private static string Header(PlantSnapshot s)
        {
            return "PWR " + F(s.Power * ReactorSim.Physics.RatedThermalMWt, "F0") + " MWt   RHO " + F(s.TotalReactivityPcm, "F0") + " pcm";
        }

        public static string Build(string screenName, PlantSnapshot s, ClockState clock, TrendBuffer trends,
            IList<string> terminalLines)
        {
            var sb = new StringBuilder(256);
            sb.Append(Header(s)).Append('\n');
            switch (screenName)
            {
                case "CRT_A1_Core":
                    sb.Append("CLOCK ").Append(ClockReadout.Format(clock)).Append('\n');
                    sb.Append("MODE ").Append(s.Mode).Append(s.Scrammed ? "  ** SCRAM **" : (s.IsCritical ? "  CRITICAL" : "  SUBCRITICAL")).Append('\n');
                    sb.Append("PERIOD ").Append(PeriodText(s.PeriodSeconds)).Append('\n');
                    sb.Append("T_FUEL ").Append(F(s.FuelTempC, "F0")).Append(" C   T_PRI ").Append(F(s.PrimaryTempC, "F0")).Append(" C\n");
                    sb.Append("P_PRI ").Append(F(s.PrimaryPressureBar, "F1")).Append(" bar\n");
                    sb.Append("RODS ");
                    for (int i = 0; i < 4; i++)
                    {
                        double ins = s.BankInsertion != null && i < s.BankInsertion.Length ? s.BankInsertion[i] : double.NaN;
                        sb.Append((char)('A' + i)).Append(' ').Append(double.IsNaN(ins) ? "--" : F(ins * 100.0, "F0")).Append("%  ");
                    }
                    break;
                case "CRT_A1_Chem":
                    sb.Append("BORON ").Append(F(s.BoronPpm, "F0")).Append(" ppm\n");
                    sb.Append("pH ").Append(F(s.Ph, "F2")).Append('\n');
                    sb.Append("XENON ").Append(F(s.XenonPcm, "F0")).Append(" pcm   IODINE ").Append(F(s.IodinePcm, "F0")).Append(" pcm\n");
                    sb.Append("DILUTE ").Append(F(s.DiluteRate, "F2")).Append("  BORATE ").Append(F(s.BorateRate, "F2"));
                    break;
                case "CRT_A1_Log":
                    sb.Append("TRENDS (oldest -> newest)\n");
                    sb.Append("PWR  [").Append(trends != null ? trends.Sparkline(TrendBuffer.Series.Power, 32) : "").Append("]\n");
                    sb.Append("FUEL [").Append(trends != null ? trends.Sparkline(TrendBuffer.Series.FuelTemp, 32) : "").Append("]\n");
                    sb.Append("PRI  [").Append(trends != null ? trends.Sparkline(TrendBuffer.Series.Pressure, 32) : "").Append("]\n");
                    sb.Append("RHO  [").Append(trends != null ? trends.Sparkline(TrendBuffer.Series.Reactivity, 32) : "").Append("]\n");
                    sb.Append("T+").Append(F(s.SimSeconds, "F0")).Append(" s");
                    break;
                case "CRT_A2_Primary":
                    sb.Append("P_PRI ").Append(F(s.PrimaryPressureBar, "F1")).Append(" bar\n");
                    sb.Append("T_PRI ").Append(F(s.PrimaryTempC, "F1")).Append(" C\n");
                    sb.Append("SUBCOOL ").Append(F(s.PrimarySatMarginC, "F1")).Append(" C\n");
                    sb.Append("LEVEL ").Append(F(s.PrimaryLevelFrac * 100.0, "F0")).Append("%   FLOW ").Append(F(s.PrimaryFlowFrac * 100.0, "F0")).Append("%\n");
                    sb.Append("HEATERS ").Append(F(s.HeaterPowerFrac * 100.0, "F0")).Append("%  SPRAY ").Append(F(s.SprayFrac * 100.0, "F0")).Append("%");
                    if (s.ReliefOpen) sb.Append("\n!! PORV OPEN");
                    break;
                case "CRT_A2_Secondary":
                    sb.Append("P_SEC ").Append(F(s.SecondaryPressureBar, "F1")).Append(" bar\n");
                    sb.Append("SG_LVL ").Append(F(s.SecondaryLevelFrac * 100.0, "F0")).Append("%\n");
                    sb.Append("STEAM ").Append(F(s.SteamDemandFrac * 100.0, "F0")).Append("%  DUMP ").Append(F(s.SteamDumpFrac * 100.0, "F0")).Append("%\n");
                    sb.Append("FEEDWATER ").Append(F(s.FeedwaterFlowFrac * 100.0, "F0")).Append("%");
                    if (s.SecondaryReliefOpen) sb.Append("\n!! SG RELIEF OPEN");
                    break;
                case "CRT_A2_Turbine":
                    sb.Append("TURBINE ").Append(F(s.TurbineRpm, "F0")).Append(" rpm").Append(s.TurbineTripped ? "  ** TRIPPED **" : "").Append('\n');
                    sb.Append("GEN ").Append(F(s.GeneratorKw / 1000.0, "F1")).Append(" MWe  ").Append(s.GeneratorSynced ? "ON GRID" : "OFF GRID").Append('\n');
                    sb.Append("GRID ").Append(F(s.GridFrequencyHz, "F2")).Append(" Hz\n");
                    sb.Append("CONDENSER ").Append(F(s.CondenserPressureBar, "F2")).Append(" bar");
                    break;
                case "CRT_AssistantTerminal":
                    sb.Append("ASSISTANT TIER ").Append(s.AssistTier).Append("   MISSION ").Append(s.ActiveMissionIndex).Append('\n');
                    if (terminalLines == null || terminalLines.Count == 0)
                        sb.Append("> all quiet. suspiciously quiet.");
                    else
                        for (int i = 0; i < terminalLines.Count; i++)
                            sb.Append("> ").Append(terminalLines[i]).Append('\n');
                    break;
                case "CRT_MimicStatusBoard":
                    sb.Append(s.Scrammed ? "!!! SCRAM !!!" : (s.IsCritical ? "REACTOR CRITICAL" : "REACTOR SHUTDOWN"));
                    sb.Append("   CLOCK ").Append(ClockReadout.Format(clock));
                    break;
                default:
                    sb.Append("SCRAM ").Append(s.Scrammed);
                    break;
            }
            return sb.ToString();
        }

        private static string PeriodText(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0.0 || seconds > 9999.0) return "STABLE";
            return F(seconds, "F0") + " s";
        }
    }
}
