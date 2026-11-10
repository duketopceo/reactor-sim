using System.Collections.Generic;
using System.Globalization;

// Pure logic (no UnityEngine): the display model behind the procedure clipboard (U16, R16).
// ProcedureClipboard draws it. Linked into ReactorSim.Core.Tests.
namespace ReactorSim.Clipboard
{
    /// <summary>One checklist line: a tick box derived from the procedure state, never from a click.</summary>
    public sealed class ClipboardLine
    {
        public int Number;       // 1-based step number
        public string Text = "";
        public string Phase = "";      // optional step metadata from procedures.json ("" when absent)
        public string Control = "";
        public string Target = "";
        public string LiveValue = "";  // reading for the CURRENT step only ("" for every other line)
        public bool Ticked;
        public bool Current;
        public string Mark = "[ ]";   // "[x]" done, "[>]" the step to do now, "[ ]" later
    }

    /// <summary>What the always-on startup panel shows: the current step, or the completion state.</summary>
    public sealed class StartupHudModel
    {
        public bool Complete;
        public string Phase = "";
        public int StepNumber;     // 1-based current step (StepCount once complete)
        public int StepCount;
        public string Action = ""; // the step sentence, or "On the grid" once complete
        public string Control = "";
        public string Target = "";
        public string LiveValue = "";
        public string Reading = ""; // live value against its target: "37.5 % (target 80 % or more)"
        public bool Ticked;         // true only once the whole procedure is complete
        public string Mark = "[ ]";
        public string[] HighlightRows = new string[0];
    }

    public static class ClipboardView
    {
        /// <summary>Previous-step marker for "no earlier look", so the first frame reports no regression.</summary>
        public const int NoPrevious = -2;

        public static StartupHudModel Hud(Procedure procedure, ProcedureState state, PlantSnapshot snap)
        {
            var m = new StartupHudModel { StepCount = procedure.Steps.Count };
            if (state.Complete)
            {
                m.Complete = true;
                m.StepNumber = m.StepCount;
                m.Action = "On the grid";
                m.Ticked = true;
                m.Mark = "[x]";
                return m;
            }
            var step = state.Current;
            m.StepNumber = state.CurrentIndex + 1;
            m.Phase = step.Phase ?? "";
            m.Action = step.Text;
            m.Control = step.Control ?? "";
            m.Target = step.Target ?? "";
            m.LiveValue = snap != null ? LiveValue(step, snap) : "";
            m.Reading = Reading(m.LiveValue, m.Target);
            m.Mark = "[>]";
            m.HighlightRows = state.HighlightedRows;
            return m;
        }

        /// <summary>Live value shown against its target; either half alone when the other is empty.</summary>
        public static string Reading(string live, string target)
        {
            bool hasLive = !string.IsNullOrEmpty(live), hasTarget = !string.IsNullOrEmpty(target);
            if (hasLive && hasTarget) return live + " (target " + target + ")";
            if (hasLive) return live;
            return hasTarget ? "target " + target : "";
        }

        /// <summary>
        /// A caution line naming the step that unticked when the current step moves backward (SCRAM, trip), else null.
        /// previousIndex is the earlier ProcedureState.CurrentIndex (-1 = complete), or NoPrevious.
        /// </summary>
        public static string RegressionNote(Procedure procedure, int previousIndex, ProcedureState state)
        {
            if (previousIndex == NoPrevious || state.Complete) return null;
            int before = previousIndex < 0 ? procedure.Steps.Count : previousIndex;
            int now = state.CurrentIndex;
            if (now >= before) return null;
            return "Back to step " + (now + 1) + ": " + procedure.Steps[now].Text + " (no longer met)";
        }

        public static List<ClipboardLine> Lines(Procedure procedure, ProcedureState state)
        {
            return Lines(procedure, state, null);
        }

        /// <summary>Lines with the current step's live reading filled in when a snapshot is given.</summary>
        public static List<ClipboardLine> Lines(Procedure procedure, ProcedureState state, PlantSnapshot snap)
        {
            var lines = new List<ClipboardLine>();
            int current = state.CurrentIndex;
            for (int i = 0; i < procedure.Steps.Count; i++)
            {
                bool ticked = state.Ticked[i];
                bool isCurrent = i == current;
                lines.Add(new ClipboardLine
                {
                    Number = i + 1,
                    Text = procedure.Steps[i].Text,
                    Phase = procedure.Steps[i].Phase ?? "",
                    Control = procedure.Steps[i].Control ?? "",
                    Target = procedure.Steps[i].Target ?? "",
                    LiveValue = isCurrent && snap != null ? LiveValue(procedure.Steps[i], snap) : "",
                    Ticked = ticked,
                    Current = isCurrent,
                    Mark = ticked ? "[x]" : (isCurrent ? "[>]" : "[ ]"),
                });
            }
            return lines;
        }

        /// <summary>
        /// The live reading the step's predicate watches, with its unit, formatted with the invariant culture
        /// so the text never depends on the player's locale ("37.5 %", "1347 ppm", "0.52 bar").
        /// </summary>
        public static string LiveValue(ProcedureStep step, PlantSnapshot s)
        {
            var ic = CultureInfo.InvariantCulture;
            switch (step.Predicate)
            {
                case "primary_flow_at_least": return Pct(s.PrimaryFlowFrac);
                case "heater_power_at_least": return Pct(s.HeaterPowerFrac);
                case "primary_pressure_at_least": return s.PrimaryPressureBar.ToString("0", ic) + " bar";
                case "primary_temp_at_least": return s.PrimaryTempC.ToString("0", ic) + " C";
                case "boron_at_most": return s.BoronPpm.ToString("0", ic) + " ppm";
                case "shutdown_banks_out":
                    return "D " + Pct(s.BankInsertion[0]) + " C " + Pct(s.BankInsertion[1]) + " B " + Pct(s.BankInsertion[2]);
                case "bank_a_insertion_at_most": return Pct(s.BankInsertion[3]);
                case "critical_power_at_least": return (s.IsCritical ? "critical " : "subcritical ") + Pct(s.Power);
                case "power_at_least": return Pct(s.Power);
                case "steam_demand_at_least": return Pct(s.SteamDemandFrac);
                case "turbine_rpm_at_least": return s.TurbineRpm.ToString("0", ic) + " rpm";
                case "condenser_pressure_at_most": return s.CondenserPressureBar.ToString("0.00", ic) + " bar";
                case "turbine_auto_load_on": return s.BopAutoLoad ? "auto" : "manual";
                case "generator_synced": return s.GeneratorSynced ? "closed" : "open";
                case "grid_output_at_least": return Pct(s.GridOutputFrac);
                default: return "";
            }
        }

        private static string Pct(double frac)
        {
            return (frac * 100.0).ToString("0.#", CultureInfo.InvariantCulture) + " %";
        }

        public static string Header(Procedure procedure, ProcedureState state)
        {
            int done = 0;
            foreach (bool t in state.Ticked) if (t) done++;
            string title = string.IsNullOrEmpty(procedure.Title) ? procedure.Id : procedure.Title;
            return state.Complete ? title + "  complete" : title + "  " + done + "/" + procedure.Steps.Count;
        }

        /// <summary>Inventory row ids the "Show me" outline marks: exactly the current step's rows (none once complete).</summary>
        public static string[] HighlightRows(ProcedureState state)
        {
            return state.HighlightedRows;
        }
    }
}
