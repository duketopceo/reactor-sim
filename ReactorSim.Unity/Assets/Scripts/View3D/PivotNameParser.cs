using System;
using System.Globalization;

// Pure logic (no UnityEngine): parses the INT_* pivot naming contract into a ControlSpec.
// Linked into ReactorSim.Core.Tests, so keep it free of Unity types.
//
//   INT_DIAL_<Kind>_[<Target>_]<min>_<max>   continuous drag control (Target optional)
//   INT_BTN_<Kind>_<Target>_<value>          push button / toggle
//
// <Kind> is matched case-insensitively against IntentKind (Blender's "NaOHRate" is IntentKind.NaohRate).
// Names may carry Blender's duplicate suffix (".001") and visual children (_Knob/_Pointer/_Control).
namespace ReactorSim.Controls
{
    public enum ControlStyle { Dial, Button }

    public enum ParseStatus
    {
        Ok,
        NotControl,   // not an INT_* name at all
        VisualChild,  // INT_*_Knob / _Pointer / _Control: a mesh under a control pivot, not a control
        Malformed     // INT_* name that breaks the grammar; always reported
    }

    public sealed class ControlSpec
    {
        public string PivotName = "";
        public string BaseName = "";
        public ControlStyle Style;
        public IntentKind Kind;
        public string Target = "";
        public double Min;
        public double Max = 1.0;
        public double ButtonValue = 1.0;
        public bool Discrete;

        public bool IsRodBank { get { return Kind == IntentKind.RodBankTarget; } }

        public int RodBank
        {
            get
            {
                int idx;
                return IsRodBank && int.TryParse(Target, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) ? idx : -1;
            }
        }
    }

    public sealed class PivotParseResult
    {
        public ParseStatus Status;
        public ControlSpec Spec;
        public string Error = "";
        public bool Ok { get { return Status == ParseStatus.Ok; } }
    }

    public static class PivotNameParser
    {
        private static readonly string[] VisualSuffixes = { "_Knob", "_Pointer", "_Control" };

        public static PivotParseResult Parse(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith("INT_", StringComparison.Ordinal))
                return new PivotParseResult { Status = ParseStatus.NotControl };

            string baseName = StripDuplicateSuffix(name);
            for (int i = 0; i < VisualSuffixes.Length; i++)
                if (baseName.EndsWith(VisualSuffixes[i], StringComparison.Ordinal))
                    return new PivotParseResult { Status = ParseStatus.VisualChild };

            string[] tok = baseName.Split('_');
            // tok[0] = "INT", tok[1] = style, tok[2] = kind, ...
            if (tok.Length < 4) return Fail(name, "too few fields");

            ControlStyle style;
            if (tok[1] == "DIAL") style = ControlStyle.Dial;
            else if (tok[1] == "BTN") style = ControlStyle.Button;
            else return Fail(name, "style must be DIAL or BTN");

            IntentKind kind;
            if (!TryKind(tok[2], out kind)) return Fail(name, "unknown intent kind '" + tok[2] + "'");

            var spec = new ControlSpec { PivotName = name, BaseName = baseName, Style = style, Kind = kind };

            if (style == ControlStyle.Dial)
            {
                double min, max;
                if (tok.Length < 5) return Fail(name, "dial needs <min>_<max>");
                if (!TryNum(tok[tok.Length - 2], out min) || !TryNum(tok[tok.Length - 1], out max))
                    return Fail(name, "dial range is not numeric");
                if (!(min < max)) return Fail(name, "dial min must be below max");
                spec.Min = min;
                spec.Max = max;
                // Fields between the kind and the range are the Target; none means the intent's default.
                int targetCount = tok.Length - 2 - 3;
                if (targetCount > 0)
                    spec.Target = string.Join("_", tok, 3, targetCount);
                else
                    spec.Target = DefaultTarget(kind);
                spec.Discrete = kind == IntentKind.AssistTier || kind == IntentKind.OperatingMode
                    || kind == IntentKind.SelectMission;
            }
            else
            {
                double val;
                if (!TryNum(tok[tok.Length - 1], out val)) return Fail(name, "button value is not numeric");
                spec.ButtonValue = val;
                int targetCount = tok.Length - 1 - 3;
                spec.Target = targetCount > 0 ? string.Join("_", tok, 3, targetCount) : DefaultTarget(kind);
            }
            return new PivotParseResult { Status = ParseStatus.Ok, Spec = spec };
        }

        // Target the matching PlantIntent factory uses when the pivot name has none.
        public static string DefaultTarget(IntentKind kind)
        {
            switch (kind)
            {
                case IntentKind.RodBankTarget: return "0";
                case IntentKind.AllRodsTarget: return "ALL";
                case IntentKind.BorateRate:
                case IntentKind.DiluteRate: return "Boron";
                case IntentKind.NaohRate:
                case IntentKind.IonExchangeRate: return "pH";
                case IntentKind.SteamDemand: return "Turbine";
                case IntentKind.PrimaryFlow: return "RCP";
                case IntentKind.PrimaryLevel:
                case IntentKind.HeaterPower:
                case IntentKind.Spray: return "PZR";
                case IntentKind.FeedwaterFlow: return "SGFeed";
                case IntentKind.SteamDump: return "Dump";
                case IntentKind.TurbineAutoLoad: return "Turbine";
                case IntentKind.GeneratorBreaker: return "Generator";
                case IntentKind.GridDemand: return "Grid";
                case IntentKind.VacuumPump: return "Condenser";
                case IntentKind.CoolingWater: return "Tower";
                case IntentKind.ResetBopTrips: return "BOP";
                case IntentKind.AcknowledgeAlarms: return "Alarms";
                case IntentKind.ReliefValve: return "PORV";
                case IntentKind.Scram:
                case IntentKind.ResetScram:
                case IntentKind.OperatingMode: return "Core";
                case IntentKind.AssistTier: return "Assistant";
                case IntentKind.SelectMission: return "Mission";
                case IntentKind.SetTimeScale:
                case IntentKind.PauseClock:
                case IntentKind.ResumeClock:
                case IntentKind.SkipAhead: return "Clock";
                default: return "";
            }
        }

        private static string StripDuplicateSuffix(string name)
        {
            // Blender appends ".001", ".002" ... to duplicate object names. "1.2" inside a range is not one.
            int n = name.Length;
            if (n > 4 && name[n - 4] == '.' && char.IsDigit(name[n - 3]) && char.IsDigit(name[n - 2]) && char.IsDigit(name[n - 1])
                && name[n - 3] == '0')
                return name.Substring(0, n - 4);
            return name;
        }

        private static bool TryKind(string token, out IntentKind kind)
        {
            // Names only: Enum.TryParse would also accept numbers.
            foreach (var n in Enum.GetNames(typeof(IntentKind)))
            {
                if (string.Equals(n, token, StringComparison.OrdinalIgnoreCase))
                {
                    kind = (IntentKind)Enum.Parse(typeof(IntentKind), n);
                    return true;
                }
            }
            kind = IntentKind.RodBankTarget;
            return false;
        }

        private static bool TryNum(string s, out double v)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                && !double.IsNaN(v) && !double.IsInfinity(v);
        }

        private static PivotParseResult Fail(string name, string why)
        {
            return new PivotParseResult { Status = ParseStatus.Malformed, Error = "malformed control pivot '" + name + "': " + why };
        }
    }
}
