using System;
using System.Collections.Generic;

namespace ReactorSim
{
    public enum LedgerRefusal { None, InvalidSubjectId, InvalidArea, NonFiniteCoordinate, OutOfBounds, TooManySubjects }

    /// <summary>One operator's exposure record: accumulated dose, peak rate, time without a usable reading and the last reported location (R7, R9).</summary>
    public sealed class ExposureSubject
    {
        public string Id { get; private set; }
        public double DoseMSv { get; private set; }
        public double PeakRateMSvPerH { get; private set; }
        public double UnavailableSeconds { get; private set; }
        public bool HasLocation { get; private set; }
        public string AreaId { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public double H { get; private set; }

        internal ExposureSubject(string id) { Id = id; }

        internal void SetLocation(string areaId, double x, double y, double h)
        {
            HasLocation = true; AreaId = areaId; X = x; Y = y; H = h;
        }

        internal void ClearLocation()
        {
            HasLocation = false; AreaId = null; X = 0.0; Y = 0.0; H = 0.0;
        }

        internal void AddUnavailable(double dt)
        {
            UnavailableSeconds = Math.Min(UnavailableSeconds + dt, Physics.RadMaxDoseMSv);
        }

        internal void AddDose(double rateMSvPerH, double dt)
        {
            DoseMSv = Math.Min(DoseMSv + rateMSvPerH * dt / 3600.0, Physics.RadMaxDoseMSv);
            if (rateMSvPerH > PeakRateMSvPerH) PeakRateMSvPerH = rateMSvPerH;
        }
    }

    /// <summary>
    /// Per-operator dose accumulation, separate from the plant (KTD7). Subjects are kept sorted by ordinal ID so iteration is
    /// deterministic. Step integrates on the caller's tick dt, so pause and warp need no special handling (KTD8). Bounds follow KTD10.
    /// </summary>
    public sealed class ExposureLedger
    {
        private readonly List<ExposureSubject> _subjects = new List<ExposureSubject>();

        public int Count { get { return _subjects.Count; } }

        public IReadOnlyList<ExposureSubject> Subjects { get { return _subjects; } }

        public bool TryGet(string subjectId, out ExposureSubject subject)
        {
            subject = null;
            if (subjectId == null) return false;
            int i = Find(subjectId);
            if (i < 0) return false;
            subject = _subjects[i];
            return true;
        }

        /// <summary>
        /// Records where a subject is, registering the subject if new. An area the model does not know is accepted: its reading is
        /// unavailable (R5). A refusal changes nothing.
        /// </summary>
        public LedgerRefusal SetLocation(string subjectId, string areaId, double x, double y, double h)
        {
            if (!ValidText(subjectId)) return LedgerRefusal.InvalidSubjectId;
            if (!ValidText(areaId)) return LedgerRefusal.InvalidArea;
            if (!Physics.IsFinite(x) || !Physics.IsFinite(y) || !Physics.IsFinite(h)) return LedgerRefusal.NonFiniteCoordinate;
            double max = Physics.RadMaxCoordinateM;
            if (Math.Abs(x) > max || Math.Abs(y) > max || Math.Abs(h) > max) return LedgerRefusal.OutOfBounds;

            int i = Find(subjectId);
            if (i < 0)
            {
                if (_subjects.Count >= Physics.RadMaxSubjects) return LedgerRefusal.TooManySubjects;
                i = ~i;
                _subjects.Insert(i, new ExposureSubject(subjectId));
            }
            _subjects[i].SetLocation(areaId, x, y, h);
            return LedgerRefusal.None;
        }

        /// <summary>Marks a subject as having no known location. Returns false if the subject is not registered.</summary>
        public bool ClearLocation(string subjectId)
        {
            ExposureSubject s;
            if (!TryGet(subjectId, out s)) return false;
            s.ClearLocation();
            return true;
        }

        public void Clear()
        {
            _subjects.Clear();
        }

        public void Step(Plant plant, double dtSeconds)
        {
            if (plant == null) throw new ArgumentNullException("plant");
            if (double.IsNaN(dtSeconds) || dtSeconds < 0.0 || dtSeconds > Plant.MaxStepSeconds)
                throw new ArgumentOutOfRangeException("dtSeconds", dtSeconds, "ExposureLedger.Step dt must be finite, >= 0 and <= " + Plant.MaxStepSeconds + " s");
            for (int i = 0; i < _subjects.Count; i++)
            {
                var s = _subjects[i];
                if (!s.HasLocation) { s.AddUnavailable(dtSeconds); continue; }
                var reading = DoseField.Evaluate(plant, s.AreaId, s.X, s.Y, s.H);
                if (reading.Available) s.AddDose(reading.RateMSvPerH, dtSeconds);
                else s.AddUnavailable(dtSeconds);
            }
        }

        private int Find(string id)
        {
            int lo = 0, hi = _subjects.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int c = string.CompareOrdinal(_subjects[mid].Id, id);
                if (c == 0) return mid;
                if (c < 0) lo = mid + 1; else hi = mid - 1;
            }
            return ~lo;
        }

        private static bool ValidText(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > Physics.RadMaxSubjectIdLength) return false;
            for (int i = 0; i < s.Length; i++)
                if (char.IsControl(s[i])) return false;
            return true;
        }
    }
}
