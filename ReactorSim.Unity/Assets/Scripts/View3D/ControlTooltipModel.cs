using System;
using System.Globalization;
using System.Text;
using ReactorSim.Instruments;

// Pure logic (no UnityEngine): what the hover tooltip says about a control or an indicator (U17, R18): label, live value
// with units, setpoint where one exists, limits and a one-line description. Linked into ReactorSim.Core.Tests.
// The runtime has no CSV: scripts/inventory-rollup.py writes StreamingAssets/inventory-runtime.json from inventory.csv
// (id, label, short label, units, range, description, kit, role) and ControlTooltip loads it once.
namespace ReactorSim.Controls
{
    // JsonUtility-compatible (public fields, [Serializable]).
    [Serializable]
    public class RuntimeRow
    {
        public string id = "";
        public string label = "";
        public string shortLabel = "";
        public string units = "";
        public string description = "";
        public string range = "";
        public string kit = "";
        public string role = "";
    }

    [Serializable]
    public class RuntimeInventory
    {
        public RuntimeRow[] rows = new RuntimeRow[0];

        public RuntimeRow Find(string id)
        {
            if (rows == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < rows.Length; i++)
                if (rows[i] != null && rows[i].id == id) return rows[i];
            return null;
        }
    }

    public sealed class TooltipText
    {
        public string Title = "";
        public string Value = "";       // "now 1.5 ppm/s" (empty when nothing readable)
        public string Setpoint = "";    // "set 2 ppm/s [queued]" (empty when it matches the value or none exists)
        public string Limits = "";      // "range 0 - 5 ppm/s"
        public string Description = "";
        public string Note = "";        // out of service, too far, guard state

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.Append(Title);
            Line(sb, Value);
            Line(sb, Setpoint);
            Line(sb, Limits);
            Line(sb, Description);
            Line(sb, Note);
            return sb.ToString();
        }

        private static void Line(StringBuilder sb, string text)
        {
            if (!string.IsNullOrEmpty(text)) sb.Append('\n').Append(text);
        }
    }

    public static class ControlTooltipModel
    {
        /// <summary>A control (dial or button). pendingTarget is NaN when nothing is queued; stateText overrides the value line for buttons.</summary>
        public static TooltipText ForControl(RuntimeRow row, ControlSpec spec, PlantSnapshot s, string fallbackTitle,
            double pendingTarget, bool queued, string stateText, string note)
        {
            var t = new TooltipText();
            t.Title = TitleOf(row, fallbackTitle);
            string units = row != null ? row.units : "";
            if (spec != null && spec.Style == ControlStyle.Button)
            {
                t.Value = string.IsNullOrEmpty(stateText) ? "" : stateText;
            }
            else if (spec != null)
            {
                double cur = ControlValues.Current(spec, s);
                if (!double.IsNaN(cur)) t.Value = "now " + Fmt(cur, units, spec.Discrete);
                double target = !double.IsNaN(pendingTarget) ? pendingTarget : ControlValues.Commanded(spec, s);
                if (!double.IsNaN(target) && (!double.IsNaN(pendingTarget) || double.IsNaN(cur) || Math.Abs(target - cur) > 0.005))
                    t.Setpoint = "set " + Fmt(target, units, spec.Discrete) + (queued ? "  [queued]" : "");
                t.Limits = units == TierUnits ? "range " + TierName(spec.Min) + " - " + TierName(spec.Max) : "range " + Num(spec.Min) + " - " + Num(spec.Max) + UnitSuffix(units);
            }
            t.Description = row != null ? row.description : "";
            t.Note = note ?? "";
            return t;
        }

        /// <summary>An indicator (gauge, bar or counter) read through its gauge selector.</summary>
        public static TooltipText ForIndicator(RuntimeRow row, string selector, int index, PlantSnapshot s, string fallbackTitle, string note)
        {
            var t = new TooltipText();
            t.Title = TitleOf(row, fallbackTitle);
            var e = GaugeSources.Find(selector);
            string units = row != null && !string.IsNullOrEmpty(row.units) ? row.units : (e != null ? e.Units : "");
            if (e != null && s != null)
            {
                double v = e.Select(s, index);
                if (!double.IsNaN(v)) t.Value = "now " + Fmt(v, units, false);
            }
            double lo, hi;
            if (row != null && TryRange(row.range, out lo, out hi))
                t.Limits = units == TierUnits ? "range " + TierName(lo) + " - " + TierName(hi) : "range " + Num(lo) + " - " + Num(hi) + UnitSuffix(units);
            t.Description = row != null ? row.description : "";
            t.Note = note ?? "";
            return t;
        }

        // ---- feedback for a click that does nothing (R6) and the live drag readout (R4) ----

        /// <summary>Reason codes logged with a rejected press.</summary>
        public const string RejectIndicator = "indicator", RejectOutOfService = "out_of_service",
            RejectOutOfRange = "out_of_range", RejectNoSpec = "no_spec", RejectNoBinder = "no_binder";

        /// <summary>
        /// The control row that operates an indicator row: its id is the indicator's id plus "_ctl" or a leading part of it
        /// (cooling_water_ctl for cooling_water_flow, pzr_level_ctl for pzr_level). Null when no control matches.
        /// </summary>
        public static RuntimeRow RelatedControl(RuntimeInventory inv, string indicatorId)
        {
            if (inv == null || inv.rows == null || string.IsNullOrEmpty(indicatorId)) return null;
            for (int i = 0; i < inv.rows.Length; i++)
            {
                var r = inv.rows[i];
                if (r == null || r.role != "control" || r.id == null || !r.id.EndsWith("_ctl", StringComparison.Ordinal)) continue;
                string stem = r.id.Substring(0, r.id.Length - 4);
                if (stem.Length > 0 && (indicatorId == stem || indicatorId.StartsWith(stem + "_", StringComparison.Ordinal))) return r;
            }
            return null;
        }

        /// <summary>One line shown when a gauge or lamp is clicked: it is read, not operated.</summary>
        public static string IndicatorNote(RuntimeRow relatedControl)
        {
            return relatedControl == null || string.IsNullOrEmpty(relatedControl.label)
                ? "Indicator. Right-click to pin it to the tablet."
                : "Indicator. Right-click to pin. Operate it with the " + relatedControl.label + " knob";
        }

        /// <summary>Drag readout: label, the value being set with its unit, and the change since the press.</summary>
        public static string DragReadout(string label, double value, double startValue, string units, bool integer)
        {
            if (units == TierUnits) { integer = true; value = Math.Round(value); startValue = Math.Round(startValue); }   // steps, not fractions of a tier
            double delta = value - startValue;
            string sign = delta > 0.0 ? "+" : "";
            return (label ?? "") + "\n" + Fmt(value, units, integer) + "   (" + sign + (integer ? Num(Math.Round(delta)) : ReactorSim.Ui.ReadoutFormat.Adaptive(delta)) + ")";
        }

        /// <summary>A placed but unbound row.</summary>
        public static TooltipText ForOutOfService(RuntimeRow row, string fallbackTitle, string reason)
        {
            var t = new TooltipText();
            t.Title = TitleOf(row, fallbackTitle);
            t.Description = row != null ? row.description : "";
            t.Note = "Out of service" + (string.IsNullOrEmpty(reason) ? "" : ": " + reason);
            return t;
        }

        public static bool TryRange(string range, out double lo, out double hi)
        {
            lo = hi = 0.0;
            if (string.IsNullOrEmpty(range)) return false;
            int i = range.IndexOf("..", StringComparison.Ordinal);
            if (i <= 0) return false;
            return double.TryParse(range.Substring(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out lo)
                && double.TryParse(range.Substring(i + 2), NumberStyles.Float, CultureInfo.InvariantCulture, out hi);
        }

        private static string TitleOf(RuntimeRow row, string fallback)
        {
            return row != null && !string.IsNullOrEmpty(row.label) ? row.label : (fallback ?? "");
        }

        private static string UnitSuffix(string units)
        {
            return string.IsNullOrEmpty(units) ? "" : " " + units;
        }

        // A "tier" reading is the assistant level: it reads as a name ("Advisory"), never "0.62 tier".
        private const string TierUnits = "tier";
        private static string TierName(double v)
        {
            if (double.IsNaN(v)) return "--";
            int max = Enum.GetValues(typeof(AssistTier)).Length - 1;
            return ((AssistTier)Math.Max(0, Math.Min(max, (int)Math.Round(v)))).ToString();
        }

        private static string Fmt(double v, string units, bool integer)
        {
            if (units == TierUnits && !double.IsNaN(v)) return TierName(v);
            return (integer ? Num(Math.Round(v)) : ReactorSim.Ui.ReadoutFormat.Adaptive(v)) + UnitSuffix(units);
        }

        // Up to 2 decimals, or 3 significant digits in exponent form for very large and very small values.
        public static string Num(double v)
        {
            if (double.IsNaN(v)) return "--";
            double a = Math.Abs(v);
            if (a != 0.0 && (a >= 1e6 || a < 1e-3)) return v.ToString("0.##e+0", CultureInfo.InvariantCulture);
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
