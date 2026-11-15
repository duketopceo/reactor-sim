// ReactorSim.Core — operator procedures (U16, R16).
// A procedure is an ordered list of steps. Each step names the inventory rows to highlight and a
// completion condition: a NAMED predicate over a PlantSnapshot (table below, no expression
// evaluator) with an optional numeric parameter from data. State is derived from the snapshot
// alone, so it is pure and deterministic and a click can never tick a step:
//   step i is ticked  iff  its predicate holds  and every earlier step is ticked.
// The current step is the first unticked one. A ticked step therefore unticks as soon as its
// condition stops holding (and everything after it with it), which sends the operator back.

using System;
using System.Collections.Generic;

namespace ReactorSim
{
    public class ProcedureException : Exception
    {
        public ProcedureException(string message) : base(message) { }
    }

    public class ProcedureStep
    {
        public string Id;
        public string Text;
        // Optional presentation fields (empty when absent): which startup phase the step belongs to,
        // the control the operator uses, and the target value in words. Never used for ticking.
        public string Phase = "";
        public string Control = "";
        public string Target = "";
        public string[] Rows = new string[0];   // inventory row ids to highlight
        public string Predicate;                 // key into ProcedurePredicates
        public double Value;                     // predicate parameter (ignored by parameterless predicates)
        public double Value2;                    // optional second parameter ("value2" in data, 0 when absent); a band's lower bound
    }

    public class Procedure
    {
        public string Id;
        public string Title;
        public List<ProcedureStep> Steps = new List<ProcedureStep>();

        public ProcedureState Evaluate(PlantSnapshot snap)
        {
            var ticked = new bool[Steps.Count];
            bool prefix = true;
            for (int i = 0; i < Steps.Count; i++)
            {
                prefix = prefix && ProcedurePredicates.Evaluate(Steps[i].Predicate, Steps[i].Value, Steps[i].Value2, snap);
                ticked[i] = prefix;
            }
            return new ProcedureState(this, ticked);
        }
    }

    public class ProcedureState
    {
        private readonly Procedure _procedure;
        public bool[] Ticked { get; private set; }

        internal ProcedureState(Procedure procedure, bool[] ticked)
        {
            _procedure = procedure;
            Ticked = ticked;
        }

        // Index of the first unticked step, or -1 when every step is ticked.
        public int CurrentIndex
        {
            get
            {
                for (int i = 0; i < Ticked.Length; i++) if (!Ticked[i]) return i;
                return -1;
            }
        }

        public bool Complete { get { return CurrentIndex < 0; } }

        public ProcedureStep Current { get { int i = CurrentIndex; return i < 0 ? null : _procedure.Steps[i]; } }

        // Rows of the current step; empty once the procedure is complete.
        public string[] HighlightedRows { get { var c = Current; return c == null ? new string[0] : c.Rows; } }
    }

    public static class ProcedurePredicates
    {
        // Bank A insertion at or below which the boron band's lower bound no longer applies (original design
        // choice: the step 6 target, "bank A about half").
        internal const double BankAReleaseInsertion = 0.5;

        private static readonly Dictionary<string, Func<PlantSnapshot, double, double, bool>> Table =
            new Dictionary<string, Func<PlantSnapshot, double, double, bool>>
            {
                { "primary_flow_at_least",      (s, v, v2) => s.PrimaryFlowFrac >= v },
                { "heater_power_at_least",      (s, v, v2) => s.HeaterPowerFrac >= v },
                { "primary_pressure_at_least",  (s, v, v2) => s.PrimaryPressureBar >= v },
                { "primary_temp_at_least",      (s, v, v2) => s.PrimaryTempC >= v },
                { "boron_at_most",              (s, v, v2) => s.BoronPpm <= v },
                // Band for the approach: at or below value (ppm) and, until bank A has been pulled to half
                // or beyond, at or above value2. Diluting past the band while bank A is still in would put
                // the critical bank A position before the half-way mark (see docs/STARTUP.md); once bank A
                // is at half or less the lower bound is released, because later steps dilute on purpose.
                { "boron_band_until_bank_a_pulled", (s, v, v2) => s.BoronPpm <= v && (s.BoronPpm >= v2 || s.BankInsertion[3] <= BankAReleaseInsertion) },
                { "shutdown_banks_out",         (s, v, v2) => s.BankInsertion[0] < v && s.BankInsertion[1] < v && s.BankInsertion[2] < v },
                { "bank_a_insertion_at_most",   (s, v, v2) => s.BankInsertion[3] <= v },
                { "critical_power_at_least",    (s, v, v2) => s.IsCritical && s.Power >= v },
                { "power_at_least",             (s, v, v2) => s.Power >= v },
                { "steam_demand_at_least",      (s, v, v2) => s.SteamDemandFrac >= v },
                { "turbine_rpm_at_least",       (s, v, v2) => s.TurbineRpm >= v },
                { "condenser_pressure_at_most", (s, v, v2) => s.CondenserPressureBar <= v },
                { "turbine_auto_load_on",       (s, v, v2) => s.BopAutoLoad },
                { "generator_synced",           (s, v, v2) => s.GeneratorSynced },
                { "grid_output_at_least",       (s, v, v2) => s.GridOutputFrac >= v },
            };

        public static IEnumerable<string> Names { get { return Table.Keys; } }

        public static bool Exists(string name) { return name != null && Table.ContainsKey(name); }

        public static bool Evaluate(string name, double value, PlantSnapshot snap)
        {
            return Evaluate(name, value, 0.0, snap);
        }

        public static bool Evaluate(string name, double value, double value2, PlantSnapshot snap)
        {
            Func<PlantSnapshot, double, double, bool> f;
            if (!Table.TryGetValue(name ?? "", out f))
                throw new ProcedureException("unknown procedure predicate \"" + name + "\"");
            return f(snap, value, value2);
        }
    }
}
