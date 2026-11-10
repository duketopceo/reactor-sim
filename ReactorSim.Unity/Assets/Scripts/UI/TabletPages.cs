using System;
using System.Collections.Generic;
using System.Globalization;
using ReactorSim.Controls;
using ReactorSim.Instruments;
using ReactorSim.Ui;

// Pure logic (no UnityEngine): the page models behind the operator tablet (U14, R14). OperatorTablet draws them.
// Linked into ReactorSim.Core.Tests.
namespace ReactorSim.Tablet
{
    // ---- Pins ------------------------------------------------------------------------------------------------

    /// <summary>One pinned indicator: an inventory row plus the gauge selector (GaugeSources.Table) it reads.</summary>
    public sealed class PinRow
    {
        public string RowId = "";
        public string Label = "";
        public string Selector = "";
        public int Index = -1;

        public double Read(PlantSnapshot s)
        {
            var e = GaugeSources.Find(Selector);
            return e != null && s != null ? e.Select(s, Index) : double.NaN;
        }

        public string Units
        {
            get { var e = GaugeSources.Find(Selector); return e != null ? e.Units : ""; }
        }

        public string Format(PlantSnapshot s)
        {
            double v = Read(s);
            if (!ReadoutFormat.IsFinite(v)) return ReadoutFormat.Dash;
            double a = Math.Abs(v);
            string u = Units;
            if (a >= 1e5 || (a > 0.0 && a < 0.01))
            {
                string sci = v.ToString("0.##e+0", CultureInfo.InvariantCulture);
                return u.Length > 0 ? sci + " " + u : sci;
            }
            return ReadoutFormat.Value(v, a >= 100.0 ? 0 : (a >= 10.0 ? 1 : 2), u);
        }
    }

    /// <summary>The pinned readouts, oldest first. Capped; a pin beyond the cap evicts the oldest.</summary>
    public sealed class PinBoard
    {
        public const int Cap = 8;

        private readonly List<PinRow> rows = new List<PinRow>();

        public IList<PinRow> Rows { get { return rows; } }
        public int Count { get { return rows.Count; } }
        /// <summary>Row id evicted by the last Pin call, or empty.</summary>
        public string LastEvicted { get; private set; }

        public PinBoard() { LastEvicted = ""; }

        public bool Contains(string rowId) { return IndexOf(rowId) >= 0; }

        private int IndexOf(string rowId)
        {
            for (int i = 0; i < rows.Count; i++)
                if (string.Equals(rows[i].RowId, rowId, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>Adds the row once. Returns false (and changes nothing) when it is already pinned.</summary>
        public bool Pin(PinRow row)
        {
            LastEvicted = "";
            if (row == null || string.IsNullOrEmpty(row.RowId) || Contains(row.RowId)) return false;
            if (rows.Count >= Cap)
            {
                LastEvicted = rows[0].RowId;
                rows.RemoveAt(0);
            }
            rows.Add(row);
            return true;
        }

        public bool Unpin(string rowId)
        {
            int i = IndexOf(rowId);
            if (i < 0) return false;
            rows.RemoveAt(i);
            return true;
        }

        /// <summary>Right-click behaviour: unpins when pinned, pins otherwise. Returns true when now pinned.</summary>
        public bool Toggle(PinRow row)
        {
            if (row != null && Unpin(row.RowId)) return false;
            Pin(row);
            return true;
        }
    }

    // ---- Alarms ----------------------------------------------------------------------------------------------

    public enum AlarmLevel { Critical = 0, Warning = 1 }

    public sealed class AlarmEntry
    {
        public string Name = "";
        public string Label = "";
        public AlarmLevel Severity;
        /// <summary>Sim seconds when the alarm was first seen active (since it last cleared).</summary>
        public double RaisedAtSimSeconds;
        /// <summary>Inventory row id of the annunciator tile, or empty when unknown.</summary>
        public string RowId = "";
    }

    /// <summary>
    /// Active alarms for the tablet. The alarm set is the lamp table minus status lamps (red-steady / green-steady
    /// style, and the Pause / MODE / CRITICAL information tiles). Red-flash lamps are Critical, amber are Warning.
    /// </summary>
    public sealed class AlarmBoard
    {
        // Alarm lamp names in lamp-table order (fixed at startup), so Update neither re-filters nor allocates an iterator.
        private static readonly string[] AlarmNames = new List<string>(LampPredicates.AlarmNames).ToArray();

        private readonly Dictionary<string, double> raised = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> rowIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<AlarmEntry> active = new List<AlarmEntry>();

        /// <summary>Active alarms, severity first, then first-raised time, then name.</summary>
        public IList<AlarmEntry> Active { get { return active; } }

        /// <summary>Tells the board which inventory row carries the tile for a lamp name.</summary>
        public void SetRowId(string lampName, string rowId) { rowIds[lampName] = rowId; }

        public static bool IsAlarm(string lampName) { return LampPredicates.IsAlarm(lampName); }

        public static string Humanize(string lampName)
        {
            return lampName.Replace('_', ' ');
        }

        public void Update(PlantSnapshot s, LampContext ctx)
        {
            active.Clear();
            if (s == null) return;
            for (int n = 0; n < AlarmNames.Length; n++)
            {
                string name = AlarmNames[n];
                if (!LampPredicates.Evaluate(name, s, ctx)) { raised.Remove(name); continue; }
                double t;
                if (!raised.TryGetValue(name, out t)) { t = s.SimSeconds; raised[name] = t; }
                string row;
                rowIds.TryGetValue(name, out row);
                active.Add(new AlarmEntry
                {
                    Name = name,
                    Label = Humanize(name),
                    Severity = LampPredicates.Style(name) == LampStyle.RedFlash ? AlarmLevel.Critical : AlarmLevel.Warning,
                    RaisedAtSimSeconds = t,
                    RowId = row ?? ""
                });
            }
            active.Sort(Compare);
        }

        private static int Compare(AlarmEntry a, AlarmEntry b)
        {
            int c = a.Severity.CompareTo(b.Severity);
            if (c != 0) return c;
            c = a.RaisedAtSimSeconds.CompareTo(b.RaisedAtSimSeconds);
            return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
        }
    }

    // ---- Trends ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Rolling samples for the tablet's trends page (same shape as the CRT TrendBuffer: one sample per distinct
    /// sim time, fixed capacity) over the four series the tablet shows.
    /// </summary>
    public sealed class TabletTrends
    {
        public enum Series { Power, Pressure, Temperature, GridFrequency }

        public static readonly string[] Titles = { "Power", "Pressure", "Temperature", "Grid frequency" };
        public static readonly string[] UnitNames = { "MWt", "bar", "degC", "Hz" };

        private readonly double[][] data = new double[4][];
        private readonly int capacity;
        private int head;
        private double lastSim;
        private bool hasLast;

        public int Count { get; private set; }

        public TabletTrends(int capacity)
        {
            this.capacity = Math.Max(2, capacity);
            for (int i = 0; i < 4; i++) data[i] = new double[this.capacity];
        }

        public void Push(PlantSnapshot s)
        {
            if (s == null) return;
            if (hasLast && s.SimSeconds == lastSim) return;
            hasLast = true;
            lastSim = s.SimSeconds;
            data[(int)Series.Power][head] = s.Power * ReactorSim.Physics.RatedThermalMWt;
            data[(int)Series.Pressure][head] = s.PrimaryPressureBar;
            data[(int)Series.Temperature][head] = s.PrimaryTempC;
            data[(int)Series.GridFrequency][head] = s.GridFrequencyHz;
            head = (head + 1) % capacity;
            if (Count < capacity) Count++;
        }

        /// <summary>Sample `i` counted from the oldest (0) to the newest (Count - 1).</summary>
        public double At(Series series, int i)
        {
            int start = (head - Count + capacity * 2) % capacity;
            return data[(int)series][(start + i) % capacity];
        }

        public double Latest(Series series) { return Count > 0 ? At(series, Count - 1) : double.NaN; }

        /// <summary>Last `width` samples scaled 0..1 to their own min and max (0.5 when flat); shorter while history is short.</summary>
        public double[] Normalized(Series series, int width)
        {
            int n = Math.Min(width, Count);
            var o = new double[n];
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = Count - n; i < Count; i++)
            {
                double v = At(series, i);
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            for (int i = 0; i < n; i++)
                o[i] = hi > lo ? (At(series, Count - n + i) - lo) / (hi - lo) : 0.5;
            return o;
        }
    }

    // ---- Phone: dispatcher and messages (U15) ----------------------------------------------------------------

    /// <summary>
    /// Dispatcher view of the phone: the pending or active load request (countdown, delivered-so-far against the
    /// target) and the met/missed history, newest first. Refresh copies plain values from Core's Dispatcher.
    /// </summary>
    public sealed class PhonePage
    {
        public bool HasRequest;
        public RequestStatus Status;
        public int RequestId;
        public string Caller = "";
        public double TargetMw;
        public double SecondsToStart;          // > 0 while the window has not opened (sim seconds)
        public double SecondsToEnd;            // to the end of the window
        public double WindowElapsedFraction;   // 0..1 of the window gone
        public double AverageSoFarMw;          // delivered MW-seconds / window seconds elapsed
        public double DeliveredFraction;       // AverageSoFarMw / TargetMw, 0..1 (capped for the bar)
        public int Met, Missed, Streak;
        public readonly List<LoadRequest> History = new List<LoadRequest>();

        private int lastIssuedId;
        private double lastClock;

        /// <summary>Copies the dispatcher's state. Returns the request issued since the last call (once), else null.</summary>
        public LoadRequest Refresh(Dispatcher d)
        {
            HasRequest = false;
            Status = RequestStatus.Pending;
            RequestId = 0;
            Caller = "";
            TargetMw = SecondsToStart = SecondsToEnd = WindowElapsedFraction = AverageSoFarMw = DeliveredFraction = 0.0;
            History.Clear();
            if (d == null) return null;

            if (d.Clock < lastClock) lastIssuedId = 0; // a preset reload restarted the dispatcher
            lastClock = d.Clock;

            Met = d.MetCount;
            Missed = d.MissedCount;
            Streak = d.Streak;
            for (int i = d.History.Count - 1; i >= 0; i--) History.Add(d.History[i]);

            var r = d.Current;
            if (r == null) return null;
            HasRequest = true;
            Status = r.Status;
            RequestId = r.Id;
            Caller = r.Caller;
            TargetMw = r.TargetMw;
            SecondsToStart = Math.Max(0.0, r.WindowStart - d.Clock);
            SecondsToEnd = Math.Max(0.0, r.WindowEnd - d.Clock);
            double dur = Math.Max(1e-9, r.DurationSeconds);
            double elapsed = Math.Min(dur, Math.Max(0.0, d.Clock - r.WindowStart));
            WindowElapsedFraction = elapsed / dur;
            AverageSoFarMw = elapsed > 0.0 ? r.DeliveredMwSeconds / elapsed : 0.0;
            DeliveredFraction = r.TargetMw > 0.0 ? Math.Min(1.0, AverageSoFarMw / r.TargetMw) : 0.0;

            if (r.Id != lastIssuedId)
            {
                lastIssuedId = r.Id;
                return r;
            }
            return null;
        }

        /// <summary>m:ss (or h:mm:ss) of sim time.</summary>
        public static string FormatCountdown(double seconds)
        {
            int t = (int)Math.Max(0.0, Math.Round(seconds));
            if (t >= 3600) return (t / 3600).ToString(CultureInfo.InvariantCulture) + ":" + ((t / 60) % 60).ToString("00", CultureInfo.InvariantCulture)
                + ":" + (t % 60).ToString("00", CultureInfo.InvariantCulture);
            return (t / 60).ToString(CultureInfo.InvariantCulture) + ":" + (t % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The plant-manager inbox, newest first, with per-message read state (kept here, not in Core).</summary>
    public sealed class MessagesPage
    {
        public readonly List<FiredMessage> Items = new List<FiredMessage>();
        public int Unread { get; private set; }

        private readonly HashSet<long> read = new HashSet<long>();
        private long lastSeenSeq;
        private FiredMessage lastSeenRef;
        private bool primed;

        // What the last full copy saw: the inbox only ever grows at the end (or is cleared / trimmed from the front,
        // which changes its count or newest message), so an identical (engine, count, newest) means Items is current.
        private MessageEngine copiedFrom;
        private int copiedCount = -1;
        private FiredMessage copiedNewest;

        /// <summary>Copies the inbox. Returns the newest message that arrived since the last call, else null.</summary>
        public FiredMessage Refresh(MessageEngine e)
        {
            if (e == null)
            {
                Items.Clear();
                Unread = 0;
                copiedFrom = null;
                return null;
            }

            int count = e.Inbox.Count;
            FiredMessage newestMsg = count > 0 ? e.Inbox[count - 1] : null;
            if (ReferenceEquals(e, copiedFrom) && count == copiedCount && ReferenceEquals(newestMsg, copiedNewest))
                return null; // nothing arrived, nothing was reset: Items and Unread are already what a full copy gives

            Items.Clear();
            Unread = 0;
            long newest = newestMsg != null ? newestMsg.Seq : 0;
            // The inbox was reset (sequence restarted): the newest seen message is gone or is a different object.
            if (lastSeenSeq > 0 && (newest < lastSeenSeq || !InboxHasSeen(e)))
            {
                read.Clear();
                lastSeenSeq = 0;
                lastSeenRef = null;
            }

            FiredMessage arrived = null;
            for (int i = e.Inbox.Count - 1; i >= 0; i--)
            {
                var m = e.Inbox[i];
                Items.Add(m);
                if (!read.Contains(m.Seq)) Unread++;
                if (primed && m.Seq > lastSeenSeq && arrived == null) arrived = m;
            }
            if (newest > lastSeenSeq) { lastSeenSeq = newest; lastSeenRef = newestMsg; }
            primed = true;
            copiedFrom = e;
            copiedCount = count;
            copiedNewest = newestMsg;
            return arrived;
        }

        private bool InboxHasSeen(MessageEngine e)
        {
            for (int i = 0; i < e.Inbox.Count; i++)
            {
                var m = e.Inbox[i];
                if (m.Seq == lastSeenSeq && ReferenceEquals(m, lastSeenRef)) return true;
            }
            return false;
        }

        public bool IsUnread(FiredMessage m) { return m != null && !read.Contains(m.Seq); }

        public void MarkRead(long seq)
        {
            if (read.Add(seq) && Unread > 0 && Items.Exists(m => m.Seq == seq)) Unread--;
        }

        public void MarkAllRead()
        {
            foreach (var m in Items) read.Add(m.Seq);
            Unread = 0;
        }

        /// <summary>Game-time stamp of a message: time since the plant started, e.g. T+12:34.</summary>
        public static string FormatStamp(double atSeconds)
        {
            return "T+" + PhonePage.FormatCountdown(atSeconds);
        }
    }

    // ---- Clock -----------------------------------------------------------------------------------------------

    public enum ClockAction { PauseResume, Slower, Faster, SkipAhead }

    /// <summary>
    /// The tablet's clock buttons are the room's pace / pause / skip buttons: each action uses the spec parsed from
    /// the room button's own pivot name and builds its intent through ControlIntentFactory.BuildButton.
    /// </summary>
    public static class ClockActions
    {
        public const string Issuer = "tablet";

        // Pivot names of the K19 buttons the scene generator places (inventory rows clock_*_btn).
        public static string PivotName(ClockAction a)
        {
            switch (a)
            {
                case ClockAction.PauseResume: return "INT_BTN_PauseClock_Clock_1";
                case ClockAction.Slower: return "INT_BTN_SetTimeScale_Clock_0.5";
                case ClockAction.Faster: return "INT_BTN_SetTimeScale_Clock_2";
                default: return "INT_BTN_SkipAhead_Clock_600";
            }
        }

        public static ControlSpec Spec(ClockAction a)
        {
            return PivotNameParser.Parse(PivotName(a)).Spec;
        }

        public static string Label(ClockAction a)
        {
            switch (a)
            {
                case ClockAction.PauseResume: return "Pause / Resume";
                case ClockAction.Slower: return "Slower";
                case ClockAction.Faster: return "Faster";
                default: return "Skip 10 min";
            }
        }

        /// <summary>`paused` is the clock's present state, `scale` its requested scale (pace buttons multiply it).</summary>
        public static PlantIntent Build(ClockAction a, bool paused, double scale)
        {
            return ControlIntentFactory.BuildButton(Spec(a), paused, scale, Issuer);
        }
    }
}
