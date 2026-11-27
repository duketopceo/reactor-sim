using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ReactorSim
{
    public enum LedgerRefusal { None, InvalidSubjectId, InvalidArea, NonFiniteCoordinate, OutOfBounds, TooManySubjects }

    public enum LedgerLoadReason { None, Parse, MissingSchema, UnsupportedSchema, UnknownKey, WrongType, NonFinite, Negative, OutOfBounds, DuplicateSubject, TooManySubjects, InvalidSubjectId }

    /// <summary>Outcome of <see cref="ExposureLedger.FromJson"/>: a ledger, or null with the reason it was refused.</summary>
    public sealed class LedgerLoadResult
    {
        public ExposureLedger Ledger { get; private set; }
        public LedgerLoadReason Reason { get; private set; }
        public bool Ok { get { return Reason == LedgerLoadReason.None; } }

        internal LedgerLoadResult(ExposureLedger ledger, LedgerLoadReason reason) { Ledger = ledger; Reason = reason; }
    }

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

        internal ExposureSubject(string id, double doseMSv, double peakRateMSvPerH, double unavailableSeconds)
        {
            Id = id; DoseMSv = doseMSv; PeakRateMSvPerH = peakRateMSvPerH; UnavailableSeconds = unavailableSeconds;
        }

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

        /// <summary>Versioned document (KTD9). Subjects are in ordinal ID order; locations are not saved.</summary>
        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"schema\":").Append(SchemaVersion).Append(",\"subjects\":[");
            for (int i = 0; i < _subjects.Count; i++)
            {
                var s = _subjects[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":\"").Append(PlantIntent.EscapeJson(s.Id)).Append('"');
                sb.Append(",\"doseMSv\":").Append(Num(s.DoseMSv));
                sb.Append(",\"peakRateMSvPerH\":").Append(Num(s.PeakRateMSvPerH));
                sb.Append(",\"unavailableSeconds\":").Append(Num(s.UnavailableSeconds));
                sb.Append('}');
            }
            return sb.Append("]}").ToString();
        }

        /// <summary>
        /// Strict load (KTD9, KTD10). Null, empty or whitespace input is an empty ledger. Any bad input returns a reason and
        /// no ledger; this never throws for input content.
        /// </summary>
        public static LedgerLoadResult FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new LedgerLoadResult(new ExposureLedger(), LedgerLoadReason.None);
            object doc;
            try { doc = new MiniJson(json, "exposure ledger").ParseDocument(); }
            catch (JsonParseException) { return Refuse(LedgerLoadReason.Parse); }

            var root = doc as Dictionary<string, object>;
            if (root == null) return Refuse(LedgerLoadReason.WrongType);
            object schema;
            if (!root.TryGetValue("schema", out schema)) return Refuse(LedgerLoadReason.MissingSchema);
            if (!(schema is double)) return Refuse(LedgerLoadReason.WrongType);
            if ((double)schema != SchemaVersion) return Refuse(LedgerLoadReason.UnsupportedSchema);
            foreach (var key in root.Keys)
                if (key != "schema" && key != "subjects") return Refuse(LedgerLoadReason.UnknownKey);
            object subjectsObj;
            if (!root.TryGetValue("subjects", out subjectsObj)) return Refuse(LedgerLoadReason.WrongType);
            var list = subjectsObj as List<object>;
            if (list == null) return Refuse(LedgerLoadReason.WrongType);
            if (list.Count > Physics.RadMaxSubjects) return Refuse(LedgerLoadReason.TooManySubjects);

            var ledger = new ExposureLedger();
            foreach (var item in list)
            {
                ExposureSubject subject;
                var reason = ReadSubject(item as Dictionary<string, object>, out subject);
                if (reason != LedgerLoadReason.None) return Refuse(reason);
                int at = ledger.Find(subject.Id);
                if (at >= 0) return Refuse(LedgerLoadReason.DuplicateSubject);
                ledger._subjects.Insert(~at, subject);
            }
            return new LedgerLoadResult(ledger, LedgerLoadReason.None);
        }

        internal void ReplaceContentsFrom(ExposureLedger other)
        {
            if (other == null) throw new ArgumentNullException("other");
            if (ReferenceEquals(other, this)) return;
            _subjects.Clear();
            _subjects.AddRange(other._subjects);
        }

        private const int SchemaVersion = 1;

        private static LedgerLoadResult Refuse(LedgerLoadReason reason) { return new LedgerLoadResult(null, reason); }

        private static string Num(double v) { return v.ToString("G17", CultureInfo.InvariantCulture); }

        private static LedgerLoadReason ReadSubject(Dictionary<string, object> o, out ExposureSubject subject)
        {
            subject = null;
            if (o == null) return LedgerLoadReason.WrongType;
            foreach (var key in o.Keys)
                if (key != "id" && key != "doseMSv" && key != "peakRateMSvPerH" && key != "unavailableSeconds") return LedgerLoadReason.UnknownKey;
            object idObj, dose, peak, unavail;
            if (!o.TryGetValue("id", out idObj) || !o.TryGetValue("doseMSv", out dose)
                || !o.TryGetValue("peakRateMSvPerH", out peak) || !o.TryGetValue("unavailableSeconds", out unavail))
                return LedgerLoadReason.WrongType;
            var id = idObj as string;
            if (id == null || !(dose is double) || !(peak is double) || !(unavail is double)) return LedgerLoadReason.WrongType;
            double d = (double)dose, p = (double)peak, u = (double)unavail;
            if (!Physics.IsFinite(d) || !Physics.IsFinite(p) || !Physics.IsFinite(u)) return LedgerLoadReason.NonFinite;
            if (d < 0.0 || p < 0.0 || u < 0.0) return LedgerLoadReason.Negative;
            if (d > Physics.RadMaxDoseMSv || p > Physics.RadMaxRateMSvPerH || u > Physics.RadMaxDoseMSv) return LedgerLoadReason.OutOfBounds;
            if (!ValidText(id)) return LedgerLoadReason.InvalidSubjectId;
            subject = new ExposureSubject(id, d, p, u);
            return LedgerLoadReason.None;
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
