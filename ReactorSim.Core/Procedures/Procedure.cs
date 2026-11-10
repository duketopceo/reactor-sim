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
                prefix = prefix && ProcedurePredicates.Evaluate(Steps[i].Predicate, Steps[i].Value, snap);
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
        private static readonly Dictionary<string, Func<PlantSnapshot, double, bool>> Table =
            new Dictionary<string, Func<PlantSnapshot, double, bool>>
            {
                { "primary_flow_at_least",      (s, v) => s.PrimaryFlowFrac >= v },
                { "heater_power_at_least",      (s, v) => s.HeaterPowerFrac >= v },
                { "primary_pressure_at_least",  (s, v) => s.PrimaryPressureBar >= v },
                { "primary_temp_at_least",      (s, v) => s.PrimaryTempC >= v },
                { "boron_at_most",              (s, v) => s.BoronPpm <= v },
                { "shutdown_banks_out",         (s, v) => s.BankInsertion[0] < v && s.BankInsertion[1] < v && s.BankInsertion[2] < v },
                { "bank_a_insertion_at_most",   (s, v) => s.BankInsertion[3] <= v },
                { "critical_power_at_least",    (s, v) => s.IsCritical && s.Power >= v },
                { "power_at_least",             (s, v) => s.Power >= v },
                { "steam_demand_at_least",      (s, v) => s.SteamDemandFrac >= v },
                { "turbine_rpm_at_least",       (s, v) => s.TurbineRpm >= v },
                { "condenser_pressure_at_most", (s, v) => s.CondenserPressureBar <= v },
                { "turbine_auto_load_on",       (s, v) => s.BopAutoLoad },
                { "generator_synced",           (s, v) => s.GeneratorSynced },
                { "grid_output_at_least",       (s, v) => s.GridOutputFrac >= v },
            };

        public static IEnumerable<string> Names { get { return Table.Keys; } }

        public static bool Exists(string name) { return name != null && Table.ContainsKey(name); }

        public static bool Evaluate(string name, double value, PlantSnapshot snap)
        {
            Func<PlantSnapshot, double, bool> f;
            if (!Table.TryGetValue(name ?? "", out f))
                throw new ProcedureException("unknown procedure predicate \"" + name + "\"");
            return f(snap, value);
        }
    }
}
