using System;
using System.Collections.Generic;

namespace ReactorSim
{
    public enum StrengthRule
    {
        /// <summary>A = operating share per unit Power (zero once scrammed), B = residual share per unit DecayHeatPower.</summary>
        CoreOperatingAndResidual,
        /// <summary>A per unit of Power times PrimaryFlowFrac (zero once scrammed); B unused.</summary>
        PrimaryN16,
        /// <summary>A, independent of the plant; B unused.</summary>
        Constant
    }

    /// <summary>The plant inputs the radiation model reads, copied from the live plant so tests can supply any value (including NaN).</summary>
    public struct RadiationPlantState
    {
        public readonly double Power, DecayHeatPower, PrimaryFlowFrac;
        public readonly bool Scrammed;

        public RadiationPlantState(double power, double decayHeatPower, double primaryFlowFrac, bool scrammed)
        {
            Power = power; DecayHeatPower = decayHeatPower; PrimaryFlowFrac = primaryFlowFrac; Scrammed = scrammed;
        }

        public static RadiationPlantState FromPlant(Plant plant)
        {
            if (plant == null) throw new ArgumentNullException("plant");
            return new RadiationPlantState(plant.Core.Power, plant.Core.DecayHeatPower, plant.Thermal.PrimaryFlowFrac, plant.Scrammed);
        }

        public bool IsFinite
        {
            get { return Physics.IsFinite(Power) && Physics.IsFinite(DecayHeatPower) && Physics.IsFinite(PrimaryFlowFrac); }
        }
    }

    /// <summary>A fixed radiation source: a stable ID, an area, a position in the route frame and a strength rule (R1).</summary>
    public sealed class RadSource
    {
        public readonly string Id, AreaId;
        public readonly double X, Y, H;
        public readonly StrengthRule Rule;
        public readonly double CoefficientA, CoefficientB;

        public RadSource(string id, string areaId, double x, double y, double h, StrengthRule rule, double coefficientA, double coefficientB)
        {
            Id = id; AreaId = areaId; X = x; Y = y; H = h; Rule = rule; CoefficientA = coefficientA; CoefficientB = coefficientB;
        }

        /// <summary>
        /// Strength s of s / (r^2 + r0^2) in mSv/h * m^2 for the given plant state. Pure and stateless (KTD1): operating terms follow
        /// power and flow directly and drop to zero while the plant is scrammed (gameplay simplification of the N-16 half-life);
        /// the residual term follows DecayHeatPower, which Plant already decays.
        /// </summary>
        public double Strength(RadiationPlantState s)
        {
            switch (Rule)
            {
                case StrengthRule.CoreOperatingAndResidual:
                    return (s.Scrammed ? 0.0 : CoefficientA * s.Power) + CoefficientB * s.DecayHeatPower;
                case StrengthRule.PrimaryN16:
                    return s.Scrammed ? 0.0 : CoefficientA * s.Power * s.PrimaryFlowFrac;
                default:
                    return CoefficientA;
            }
        }
    }

    /// <summary>The fixed list of sources (R1). Order is fixed and is the summation order, so results are bit-reproducible.</summary>
    public sealed class SourceRegister
    {
        public const string CoreId = "rad.core";
        public const string PrimaryN16Id = "rad.primary_n16";
        public const string CorrosionId = "rad.corrosion";

        private readonly List<RadSource> _sources;

        public SourceRegister(IEnumerable<RadSource> sources)
        {
            if (sources == null) throw new ArgumentNullException("sources");
            _sources = new List<RadSource>(sources);
        }

        public IReadOnlyList<RadSource> Sources { get { return _sources; } }

        public RadSource Find(string id)
        {
            if (id == null) return null;
            foreach (var s in _sources) if (string.Equals(s.Id, id, StringComparison.Ordinal)) return s;
            return null;
        }

        private static readonly SourceRegister Shipped = new SourceRegister(new[]
        {
            // The core under the pool water, at the fuel array.
            new RadSource(CoreId, RadiationAreas.ReactorPoolId, Physics.RadModuleXM, Physics.RadModuleYM, Physics.RadCoreHM,
                StrengthRule.CoreOperatingAndResidual, Physics.RadCoreOperatingMSvPerHM2PerUnit, Physics.RadCoreResidualMSvPerHM2PerUnit),
            // The primary loop where it rises above the water, over the module.
            new RadSource(PrimaryN16Id, RadiationAreas.ReactorHallId, Physics.RadModuleXM, Physics.RadModuleYM, Physics.RadPipingHM,
                StrengthRule.PrimaryN16, Physics.RadN16MSvPerHM2PerUnit, 0.0),
            // Activated corrosion products on the hall piping.
            new RadSource(CorrosionId, RadiationAreas.ReactorHallId, Physics.RadModuleXM + Physics.RadCorrosionOffsetXM, Physics.RadModuleYM, Physics.RadPipingHM,
                StrengthRule.Constant, Physics.RadCorrosionMSvPerHM2, 0.0)
        });

        public static SourceRegister Default { get { return Shipped; } }

        /// <summary>Problems in the register against a barrier table: duplicate or empty IDs, unknown areas, bad numbers and any source-area/subject-area pair the table lacks. Empty when sound.</summary>
        public List<string> Validate(BarrierTable barriers)
        {
            if (barriers == null) throw new ArgumentNullException("barriers");
            var bad = new List<string>();
            var seen = new List<string>();
            foreach (var s in _sources)
            {
                if (string.IsNullOrEmpty(s.Id)) { bad.Add("a source has an empty id"); continue; }
                if (seen.Contains(s.Id)) bad.Add("duplicate source id '" + s.Id + "'");
                seen.Add(s.Id);
                if (!RadiationAreas.IsKnown(s.AreaId)) { bad.Add("source '" + s.Id + "' is in unknown area '" + s.AreaId + "'"); continue; }
                if (!Physics.IsFinite(s.X) || !Physics.IsFinite(s.Y) || !Physics.IsFinite(s.H)) bad.Add("source '" + s.Id + "' has a non-finite position");
                if (!Physics.IsFinite(s.CoefficientA) || !Physics.IsFinite(s.CoefficientB) || s.CoefficientA < 0.0 || s.CoefficientB < 0.0)
                    bad.Add("source '" + s.Id + "' has a negative or non-finite strength coefficient");
                foreach (string subject in RadiationAreas.AllAreaIds)
                {
                    Slab[] slabs;
                    if (!barriers.TryGet(s.AreaId, subject, out slabs))
                        bad.Add("barrier table is missing the pair " + s.AreaId + " -> " + subject);
                }
            }
            return bad;
        }
    }
}
