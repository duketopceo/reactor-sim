using System;

namespace ReactorSim
{
    public enum DoseUnavailableReason { None, UnknownArea, NoLocation, NonFiniteInput, OutOfBounds, MissingBarrier }

    /// <summary>Posting levels of 10 CFR 20.1003, ordered by severity. Unavailable is not a level: it means no usable rate (R5).</summary>
    public enum PostingLevel { Unavailable = -1, None = 0, RadiationArea = 1, HighRadiationArea = 2, VeryHighRadiationArea = 3 }

    /// <summary>A dose rate in mSv/h that is meaningful only when Available; otherwise NaN with a reason (R5).</summary>
    public struct DoseReading
    {
        public readonly bool Available;
        public readonly double RateMSvPerH;
        public readonly DoseUnavailableReason Reason;

        private DoseReading(bool available, double rate, DoseUnavailableReason reason)
        {
            Available = available; RateMSvPerH = rate; Reason = reason;
        }

        public static DoseReading Of(double rateMSvPerH) { return new DoseReading(true, rateMSvPerH, DoseUnavailableReason.None); }
        public static DoseReading Unavailable(DoseUnavailableReason reason) { return new DoseReading(false, double.NaN, reason); }
    }

    /// <summary>
    /// The dose rate at a point: natural background plus, for each source, strength times shielding attenuation over (r^2 + r0^2),
    /// clamped to the rate bound (R3, R6, KTD4, KTD5, KTD10). Stateless and engine-free; reads the plant, never changes it.
    /// </summary>
    public static class DoseField
    {
        public static DoseReading Evaluate(Plant plant, string areaId, double x, double y, double h)
        {
            return Evaluate(SourceRegister.Default, RadiationAreas.DefaultBarriers, RadiationPlantState.FromPlant(plant), areaId, x, y, h);
        }

        public static DoseReading Evaluate(SourceRegister register, BarrierTable barriers, RadiationPlantState state, string areaId, double x, double y, double h)
        {
            if (register == null) throw new ArgumentNullException("register");
            if (barriers == null) throw new ArgumentNullException("barriers");
            if (!RadiationAreas.IsKnown(areaId)) return DoseReading.Unavailable(DoseUnavailableReason.UnknownArea);
            if (!InBounds(x) || !InBounds(y) || !InBounds(h)) return DoseReading.Unavailable(DoseUnavailableReason.OutOfBounds);
            if (!state.IsFinite) return DoseReading.Unavailable(DoseUnavailableReason.NonFiniteInput);

            double rate = Physics.RadBackgroundMSvPerH;
            double r0Sq = Physics.RadSoftenRadiusM * Physics.RadSoftenRadiusM;
            var sources = register.Sources;
            for (int i = 0; i < sources.Count; i++)
            {
                var src = sources[i];
                double strength = src.Strength(state);
                if (!Physics.IsFinite(strength) || strength < 0.0) return DoseReading.Unavailable(DoseUnavailableReason.NonFiniteInput);
                Slab[] slabs;
                if (!barriers.TryGet(src.AreaId, areaId, out slabs)) return DoseReading.Unavailable(DoseUnavailableReason.MissingBarrier);
                double dx = x - src.X, dy = y - src.Y, dh = h - src.H;
                rate += strength * BarrierTable.Attenuation(slabs) / (dx * dx + dy * dy + dh * dh + r0Sq);
            }
            if (!Physics.IsFinite(rate)) return DoseReading.Unavailable(DoseUnavailableReason.NonFiniteInput);
            return DoseReading.Of(Math.Min(rate, Physics.RadMaxRateMSvPerH));
        }

        internal static bool InBounds(double v)
        {
            return Physics.IsFinite(v) && Math.Abs(v) <= Physics.RadMaxCoordinateM;
        }

        /// <summary>Posting level of a rate, with the "greater than" wording of 10 CFR 20.1003. A non-finite or negative rate is Unavailable.</summary>
        public static PostingLevel Classify(double rateMSvPerH)
        {
            if (!Physics.IsFinite(rateMSvPerH) || rateMSvPerH < 0.0) return PostingLevel.Unavailable;
            if (rateMSvPerH > Physics.RadVeryHighAreaThresholdMSvPerH) return PostingLevel.VeryHighRadiationArea;
            if (rateMSvPerH > Physics.RadHighAreaThresholdMSvPerH) return PostingLevel.HighRadiationArea;
            if (rateMSvPerH > Physics.RadAreaThresholdMSvPerH) return PostingLevel.RadiationArea;
            return PostingLevel.None;
        }

        public static PostingLevel Classify(DoseReading reading)
        {
            return reading.Available ? Classify(reading.RateMSvPerH) : PostingLevel.Unavailable;
        }

        /// <summary>An area's posting level: the classification of the rate at the area's reference point (R10). No consequence is applied.</summary>
        public static PostingLevel ClassifyArea(Plant plant, string areaId)
        {
            RadPoint p;
            if (!RadiationAreas.TryGetReferencePoint(areaId, out p)) return PostingLevel.Unavailable;
            return Classify(Evaluate(plant, areaId, p.X, p.Y, p.H));
        }
    }
}
