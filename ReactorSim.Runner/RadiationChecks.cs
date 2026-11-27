// Phase 4 verification checks for the radiation model (RAD-01, RAD-02). Kept apart from Verification.cs so the
// check logic can be driven with injected state by tests. Each method returns null when the contract holds,
// or a message naming what broke.

using System;
using System.Globalization;
using System.Text;

namespace ReactorSim
{
    internal static class RadiationChecks
    {
        private const int ScaleCheckTicks = 120;
        private const double HighScale = 60.0;

        private static string F(double v) { return v.ToString("G6", CultureInfo.InvariantCulture); }

        /// <summary>RAD-01 body: every area's reference point reads available, finite and at least background at rated power, after a trip and cold.</summary>
        internal static string AllAreasAvailable()
        {
            var full = new Plant(); full.LoadPreset("full-power");
            var tripped = new Plant(); tripped.LoadPreset("full-power");
            tripped.Apply(PlantIntent.TriggerScram());
            for (int i = 0; i < 5; i++) tripped.Step(1.0);
            var cold = new Plant(); cold.LoadPreset("cold-shutdown");

            var problems = new StringBuilder();
            Append(problems, AreaRates("rated power", SourceRegister.Default, RadiationAreas.DefaultBarriers, RadiationPlantState.FromPlant(full)));
            Append(problems, AreaRates("after trip", SourceRegister.Default, RadiationAreas.DefaultBarriers, RadiationPlantState.FromPlant(tripped)));
            Append(problems, AreaRates("cold", SourceRegister.Default, RadiationAreas.DefaultBarriers, RadiationPlantState.FromPlant(cold)));
            return problems.Length == 0 ? null : problems.ToString();
        }

        /// <summary>Checks one plant state against a register and barrier table: a message per area that is unavailable, non-finite or below background; null when all pass.</summary>
        internal static string AreaRates(string label, SourceRegister register, BarrierTable barriers, RadiationPlantState state)
        {
            var problems = new StringBuilder();
            foreach (string area in RadiationAreas.AllAreaIds)
            {
                RadPoint p;
                if (!RadiationAreas.TryGetReferencePoint(area, out p))
                {
                    problems.Append(label).Append(": ").Append(area).Append(" has no reference point; ");
                    continue;
                }
                DoseReading r = DoseField.Evaluate(register, barriers, state, area, p.X, p.Y, p.H);
                if (!r.Available)
                    problems.Append(label).Append(": ").Append(area).Append(" unavailable (").Append(r.Reason).Append("); ");
                else if (!Physics.IsFinite(r.RateMSvPerH))
                    problems.Append(label).Append(": ").Append(area).Append(" rate not finite; ");
                else if (r.RateMSvPerH < Physics.RadBackgroundMSvPerH)
                    problems.Append(label).Append(": ").Append(area).Append(" rate ").Append(F(r.RateMSvPerH)).Append(" below background; ");
            }
            return problems.Length == 0 ? null : problems.ToString();
        }

        /// <summary>RAD-02 body: the same subject location and tick count give identical dose at 1x and at a higher scale.</summary>
        internal static string DoseInvariantToTimeScale()
        {
            Simulation slow = MakeSim(1.0), fast = MakeSim(HighScale);
            for (int i = 0; i < ScaleCheckTicks; i++) slow.Advance(slow.TickDt);
            int fastTicks = fast.Advance(ScaleCheckTicks * fast.TickDt / HighScale + 1e-6);
            if (slow.TickCount != ScaleCheckTicks) return "1x run reached " + slow.TickCount + " ticks, expected " + ScaleCheckTicks;
            if (fastTicks != ScaleCheckTicks) return HighScale.ToString("G", CultureInfo.InvariantCulture) + "x run reached " + fastTicks + " ticks, expected " + ScaleCheckTicks;
            ExposureSubject a, b;
            if (!slow.Exposure.TryGet("op", out a) || !fast.Exposure.TryGet("op", out b)) return "subject missing from the ledger";
            if (!(a.DoseMSv > 0.0)) return "1x dose is not positive: " + F(a.DoseMSv);
            if (a.DoseMSv != b.DoseMSv) return "dose differs by time scale: 1x " + F(a.DoseMSv) + " vs fast " + F(b.DoseMSv) + " mSv";
            if (a.UnavailableSeconds != b.UnavailableSeconds) return "unavailable time differs by time scale";
            return null;
        }

        private static Simulation MakeSim(double scale)
        {
            var sim = new Simulation(new Tuning { DefaultScale = scale }) { DevMode = true };
            sim.LoadScenarioPreset("full-power");
            RadPoint pt;
            if (!RadiationAreas.TryGetReferencePoint(RadiationAreas.ReactorDeckId, out pt)) throw new InvalidOperationException("no reference point for the reactor deck");
            LedgerRefusal refusal = sim.Exposure.SetLocation("op", RadiationAreas.ReactorDeckId, pt.X, pt.Y, pt.H);
            if (refusal != LedgerRefusal.None) throw new InvalidOperationException("subject location refused: " + refusal);
            return sim;
        }

        private static void Append(StringBuilder sb, string message)
        {
            if (message != null) sb.Append(message);
        }
    }
}
