// ReactorSim.Core — unified PlantIntent pipeline (U2).
// Every plant mutation (UI controls, network RPCs, HTTP/MCP agent commands,
// assistant supervised/autopilot actions, replay scripts) flows through PlantIntent.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ReactorSim
{
    public enum IntentKind
    {
        RodBankTarget,
        AllRodsTarget,
        BorateRate,
        DiluteRate,
        NaohRate,
        IonExchangeRate,
        SteamDemand,
        PrimaryFlow,
        PrimaryLevel,
        FeedwaterFlow,
        HeaterPower,
        Spray,
        ReliefValve,
        Scram,
        ResetScram,
        OperatingMode,
        AssistTier,
        SelectMission,
        LoadPreset,
        // Balance of plant (appended: kinds serialize by name)
        TurbineAutoLoad,
        SteamDump,
        GeneratorBreaker,
        GridDemand,
        VacuumPump,
        CoolingWater,
        ResetBopTrips,
        // Sim clock (appended: kinds serialize by name)
        SetTimeScale,
        SetTimeScaleDev,
        PauseClock,
        ResumeClock,
        SkipAhead,
        // Operator quality of life (appended: kinds serialize by name)
        AcknowledgeAlarms
    }

    // KTD2: who may issue an intent. Player and Chaos run in the sandbox; Dev needs Simulation.DevMode.
    public enum IntentClass
    {
        Player,
        Chaos,
        Dev
    }

    public enum AssistTier
    {
        Off = 0,
        Advisory = 1,
        Supervised = 2,
        Autopilot = 3
    }

    [Serializable]
    public class PlantIntent
    {
        public IntentKind Kind;
        public string Target = "";
        public double Value;
        public string Issuer = "local";
        public double AtMinute = -1.0; // optional timestamp for scripted replays

        public PlantIntent() { }

        public IntentClass Class { get { return ClassOf(Kind); } }

        // Explicit per-kind mapping; an IntentKind without a case throws so a new kind cannot ship unclassified.
        public static IntentClass ClassOf(IntentKind kind)
        {
            switch (kind)
            {
                case IntentKind.LoadPreset:
                case IntentKind.SetTimeScaleDev:
                    return IntentClass.Dev;
                case IntentKind.RodBankTarget:
                case IntentKind.AllRodsTarget:
                case IntentKind.BorateRate:
                case IntentKind.DiluteRate:
                case IntentKind.NaohRate:
                case IntentKind.IonExchangeRate:
                case IntentKind.SteamDemand:
                case IntentKind.PrimaryFlow:
                case IntentKind.PrimaryLevel:
                case IntentKind.FeedwaterFlow:
                case IntentKind.HeaterPower:
                case IntentKind.Spray:
                case IntentKind.ReliefValve:
                case IntentKind.Scram:
                case IntentKind.ResetScram:
                case IntentKind.OperatingMode:
                case IntentKind.AssistTier:
                case IntentKind.SelectMission:
                case IntentKind.TurbineAutoLoad:
                case IntentKind.SteamDump:
                case IntentKind.GeneratorBreaker:
                case IntentKind.GridDemand:
                case IntentKind.VacuumPump:
                case IntentKind.CoolingWater:
                case IntentKind.ResetBopTrips:
                case IntentKind.SetTimeScale:
                case IntentKind.PauseClock:
                case IntentKind.ResumeClock:
                case IntentKind.SkipAhead:
                case IntentKind.AcknowledgeAlarms:
                    return IntentClass.Player;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "IntentKind has no IntentClass mapping");
            }
        }

        public PlantIntent(IntentKind kind, string target, double value, string issuer = "local", double atMinute = -1.0)
        {
            Kind = kind;
            Target = target ?? "";
            Value = value;
            Issuer = string.IsNullOrEmpty(issuer) ? "local" : issuer;
            AtMinute = atMinute;
        }

        public static PlantIntent SetRodBank(string bankNameOrIndex, double insertionTarget, string issuer = "local")
        {
            return new PlantIntent(IntentKind.RodBankTarget, bankNameOrIndex, insertionTarget, issuer);
        }

        public static PlantIntent SetRodBank(int bankIndex, double insertionTarget, string issuer = "local")
        {
            return new PlantIntent(IntentKind.RodBankTarget, bankIndex.ToString(CultureInfo.InvariantCulture), insertionTarget, issuer);
        }

        public static PlantIntent SetAllRods(double insertionTarget, string issuer = "local")
        {
            return new PlantIntent(IntentKind.AllRodsTarget, "ALL", insertionTarget, issuer);
        }

        public static PlantIntent SetBorate(double ppmPerSec, string issuer = "local")
        {
            return new PlantIntent(IntentKind.BorateRate, "Boron", ppmPerSec, issuer);
        }

        public static PlantIntent SetDilute(double ppmPerSec, string issuer = "local")
        {
            return new PlantIntent(IntentKind.DiluteRate, "Boron", ppmPerSec, issuer);
        }

        public static PlantIntent SetNaoh(double rate, string issuer = "local")
        {
            return new PlantIntent(IntentKind.NaohRate, "pH", rate, issuer);
        }

        public static PlantIntent SetIonExchange(double rateFrac, string issuer = "local")
        {
            return new PlantIntent(IntentKind.IonExchangeRate, "IonEx", rateFrac, issuer);
        }

        public static PlantIntent SetSteamDemand(double frac, string issuer = "local")
        {
            return new PlantIntent(IntentKind.SteamDemand, "Turbine", frac, issuer);
        }

        public static PlantIntent SetPrimaryFlow(double frac, string issuer = "local")
        {
            return new PlantIntent(IntentKind.PrimaryFlow, "RCP", frac, issuer);
        }

        public static PlantIntent SetFeedwater(double frac, string issuer = "local")
        {
            return new PlantIntent(IntentKind.FeedwaterFlow, "SGFeed", frac, issuer);
        }

        public static PlantIntent SetHeaters(double frac, string issuer = "local")
        {
            return new PlantIntent(IntentKind.HeaterPower, "PZR", frac, issuer);
        }

        public static PlantIntent SetSpray(double frac, string issuer = "local")
        {
            return new PlantIntent(IntentKind.Spray, "PZR", frac, issuer);
        }

        public static PlantIntent SetReliefValve(bool open, string issuer = "local")
        {
            return new PlantIntent(IntentKind.ReliefValve, "PORV", open ? 1.0 : 0.0, issuer);
        }

        public static PlantIntent SetTurbineAutoLoad(bool auto, string issuer = "local")
        {
            return new PlantIntent(IntentKind.TurbineAutoLoad, "Turbine", auto ? 1.0 : 0.0, issuer);
        }

        public static PlantIntent SetSteamDump(double frac, string issuer = "local")
        {
            return new PlantIntent(IntentKind.SteamDump, "Dump", frac, issuer);
        }

        public static PlantIntent SetGeneratorBreaker(bool close, string issuer = "local")
        {
            return new PlantIntent(IntentKind.GeneratorBreaker, "Generator", close ? 1.0 : 0.0, issuer);
        }

        public static PlantIntent SetGridDemand(double frac, string issuer = "local")
        {
            return new PlantIntent(IntentKind.GridDemand, "Grid", frac, issuer);
        }

        public static PlantIntent SetVacuumPump(double frac, string issuer = "local")
        {
            return new PlantIntent(IntentKind.VacuumPump, "Condenser", frac, issuer);
        }

        public static PlantIntent SetCoolingWater(double frac, string issuer = "local")
        {
            return new PlantIntent(IntentKind.CoolingWater, "Tower", frac, issuer);
        }

        public static PlantIntent ResetBopTrips(string issuer = "local")
        {
            return new PlantIntent(IntentKind.ResetBopTrips, "BOP", 0.0, issuer);
        }

        // Acknowledge / silence: every alarm raised so far stops flashing (the tiles hold steady until they clear).
        public static PlantIntent AcknowledgeAlarms(string issuer = "local")
        {
            return new PlantIntent(IntentKind.AcknowledgeAlarms, "Alarms", 0.0, issuer);
        }

        // Clock intents. Player scale requests are clamped to the profile's player range; the Dev
        // variant may reach the profile's dev ceiling.
        public static PlantIntent SetTimeScale(double scale, string issuer = "local")
        {
            return new PlantIntent(IntentKind.SetTimeScale, "Clock", scale, issuer);
        }

        public static PlantIntent SetTimeScaleDev(double scale, string issuer = "local")
        {
            return new PlantIntent(IntentKind.SetTimeScaleDev, "Clock", scale, issuer);
        }

        public static PlantIntent PauseClock(string issuer = "local")
        {
            return new PlantIntent(IntentKind.PauseClock, "Clock", 0.0, issuer);
        }

        public static PlantIntent ResumeClock(string issuer = "local")
        {
            return new PlantIntent(IntentKind.ResumeClock, "Clock", 0.0, issuer);
        }

        // Fast-forward at the top of the player range for up to spanSimSeconds, stopping early on a SCRAM,
        // any BOP trip or (when untilXenonPeak) once xenon passes its post-SCRAM peak.
        public static PlantIntent SkipAhead(double spanSimSeconds, string issuer = "local", bool untilXenonPeak = false)
        {
            return new PlantIntent(IntentKind.SkipAhead, untilXenonPeak ? "xenon-peak" : "Clock", spanSimSeconds, issuer);
        }

        public static PlantIntent TriggerScram(string issuer = "local")
        {
            return new PlantIntent(IntentKind.Scram, "Core", 1.0, issuer);
        }

        public static PlantIntent ClearScram(string issuer = "local")
        {
            return new PlantIntent(IntentKind.ResetScram, "Core", 0.0, issuer);
        }

        public static PlantIntent SetAssist(AssistTier tier, string issuer = "local")
        {
            return new PlantIntent(IntentKind.AssistTier, "Assistant", (double)(int)tier, issuer);
        }

        public static PlantIntent SetMission(int missionIndex, string issuer = "local")
        {
            return new PlantIntent(IntentKind.SelectMission, "Mission", missionIndex, issuer);
        }

        public static PlantIntent LoadPreset(string presetName, string issuer = "local")
        {
            return new PlantIntent(IntentKind.LoadPreset, presetName ?? "cold-shutdown", 0.0, issuer);
        }

        public string ToJson()
        {
            var sb = new StringBuilder(128);
            sb.Append("{\"kind\":\"").Append(Kind.ToString()).Append("\"");
            sb.Append(",\"target\":\"").Append(EscapeJson(Target)).Append("\"");
            sb.Append(",\"value\":").Append(Value.ToString("G17", CultureInfo.InvariantCulture));
            sb.Append(",\"issuer\":\"").Append(EscapeJson(Issuer)).Append("\"");
            if (AtMinute >= 0.0)
                sb.Append(",\"atMinute\":").Append(AtMinute.ToString("G17", CultureInfo.InvariantCulture));
            sb.Append("}");
            return sb.ToString();
        }

        public static PlantIntent FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            string kindStr = ExtractJsonString(json, "kind");
            if (string.IsNullOrEmpty(kindStr))
                kindStr = ExtractJsonString(json, "Kind");
            if (string.IsNullOrEmpty(kindStr))
                return null;

            // Names only: Enum.TryParse also accepts numbers ("999") and flag lists ("A,B").
            IntentKind kind;
            if (!Enum.TryParse(kindStr, true, out kind) || !Enum.IsDefined(typeof(IntentKind), kind)
                || kindStr.IndexOf(',') >= 0 || char.IsDigit(kindStr.Trim()[0]) || kindStr.Trim()[0] == '-')
                return null;

            string target = ExtractJsonString(json, "target") ?? ExtractJsonString(json, "Target") ?? "";
            string issuer = ExtractJsonString(json, "issuer") ?? ExtractJsonString(json, "Issuer") ?? "remote";
            double val = ExtractJsonDouble(json, "value", ExtractJsonDouble(json, "Value", 0.0));
            double atMin = ExtractJsonDouble(json, "atMinute", ExtractJsonDouble(json, "AtMinute", -1.0));

            return new PlantIntent(kind, target, val, issuer, atMin);
        }

        public static List<PlantIntent> ParseListFromJson(string json)
        {
            var list = new List<PlantIntent>();
            if (string.IsNullOrWhiteSpace(json))
                return list;

            int depth = 0;
            int objStart = -1;
            bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\'))
                    inStr = !inStr;
                if (inStr) continue;

                if (c == '{')
                {
                    if (depth == 0) objStart = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        string sub = json.Substring(objStart, i - objStart + 1);
                        var intent = FromJson(sub);
                        if (intent != null) list.Add(intent);
                        objStart = -1;
                    }
                }
            }
            return list;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "[{0}] {1}({2}) = {3:F2}", Issuer, Kind, Target, Value);
        }

        public static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        internal static string ExtractJsonString(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            int colon = json.IndexOf(':', idx + pattern.Length);
            if (colon < 0) return null;
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return null;
            var sb = new StringBuilder();
            for (int i = q1 + 1; i < json.Length; i++)
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    if (next == 'n') sb.Append('\n');
                    else if (next == 'r') sb.Append('\r');
                    else if (next == 't') sb.Append('\t');
                    else sb.Append(next);
                    i++;
                }
                else if (json[i] == '"')
                {
                    return sb.ToString();
                }
                else
                {
                    sb.Append(json[i]);
                }
            }
            return null;
        }

        internal static double ExtractJsonDouble(string json, string key, double fallback)
        {
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return fallback;
            int colon = json.IndexOf(':', idx + pattern.Length);
            if (colon < 0) return fallback;
            int start = colon + 1;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t' || json[start] == '\r' || json[start] == '\n'))
                start++;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-' || json[end] == '+' || json[end] == '.' || json[end] == 'e' || json[end] == 'E'))
                end++;
            if (end <= start) return fallback;
            double v;
            if (double.TryParse(json.Substring(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return fallback;
        }

        internal static bool ExtractJsonBool(string json, string key, bool fallback)
        {
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return fallback;
            int colon = json.IndexOf(':', idx + pattern.Length);
            if (colon < 0) return fallback;
            int start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start]))
                start++;
            if (start + 4 <= json.Length && string.Compare(json, start, "true", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
                return true;
            if (start + 5 <= json.Length && string.Compare(json, start, "false", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
                return false;
            return fallback;
        }

        internal static double[] ExtractJsonDoubleArray(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            int b1 = json.IndexOf('[', idx + pattern.Length);
            if (b1 < 0) return null;
            int b2 = json.IndexOf(']', b1 + 1);
            if (b2 < 0) return null;
            string inner = json.Substring(b1 + 1, b2 - b1 - 1);
            string[] parts = inner.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var arr = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                double v;
                double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
                arr[i] = v;
            }
            return arr;
        }
    }
}
