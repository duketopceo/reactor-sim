// ReactorSim.Core — plant-manager and colleague messages (U15).
// Message text, sender, trigger name and cooldown are data (StreamingAssets/messages.json). A trigger is a NAME
// that keys a C# predicate over the plant state (no expression evaluator). A message fires once when its
// trigger turns true and is not repeated within its cooldown. Fired messages land in a short inbox.
// Loading follows the Tuning pattern: missing file -> empty engine, malformed or unknown names -> a named error.
// State (timers, per-message edge memory, inbox) is plain fields so it can ride a future snapshot.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ReactorSim
{
    public class MessageException : Exception
    {
        public MessageException(string message) : base(message) { }
    }

    public sealed class MessageDef
    {
        public string Id = "";
        public string Sender = "";
        public string Text = "";
        public string Trigger = "";
        public double CooldownSeconds;
    }

    public sealed class FiredMessage
    {
        public long Seq;
        public string Id = "";
        public string Sender = "";
        public string Text = "";
        public double AtSeconds;   // message-engine clock
    }

    // Everything a trigger predicate may look at. Timers are maintained by MessageEngine.Step.
    public sealed class MessageContext
    {
        public Plant Plant;
        public Dispatcher Dispatcher;
        public double TimeScale = 1.0;
        public double IdleZeroPowerSeconds;   // continuous seconds at ~zero power, not scrammed
        public double SecondsSinceSync;       // since the breaker last closed (large if never)
        public double SecondsSinceResolved;   // since the dispatcher last scored a request (large if never)
        public bool LastResolvedMet;
    }

    public sealed class MessageEngine
    {
        public const double Never = 1.0e12;

        // Named predicate table: every trigger name in messages.json must be a key here.
        public static readonly Dictionary<string, Func<MessageContext, bool>> Predicates =
            new Dictionary<string, Func<MessageContext, bool>>
            {
                { "boron-high", c => c.Plant.Core.Power > 0.05 && c.Plant.Chemistry.BoronPpm > 600.0 },
                { "boron-low", c => c.Plant.Core.Power > 0.05 && c.Plant.Chemistry.BoronPpm < 5.0 },
                { "xenon-pit", c => c.Plant.Core.Xenon135 > 1.5 && c.Plant.Core.Power < 0.05 },
                { "xenon-rising", c => c.Plant.Core.Xenon135 > 1.3 && c.Plant.Core.Power >= 0.05 },
                { "scram", c => c.Plant.Scrammed },
                { "bop-trip", c => c.Plant.Bop.TripMask != 0 },
                { "overspeed", c => c.Plant.Bop.Trip(BopTripKind.Overspeed).Active },
                { "out-of-sync", c => c.Plant.Bop.Trip(BopTripKind.OutOfSync).Active },
                { "sg-low-level", c => c.Plant.Bop.Trip(BopTripKind.SgLowLevel).Active },
                { "low-vacuum", c => c.Plant.Bop.CondenserPressureBar > Physics.CondenserLowVacuumWarnBar },
                { "grid-frequency-off", c => c.Plant.Bop.Turbine.Synced && Math.Abs(c.Plant.Bop.GridFrequencyHz - Physics.GridNominalFrequencyHz) > 0.5 },
                { "sync-success", c => c.SecondsSinceSync < 10.0 },
                { "request-incoming", c => c.Dispatcher.Current != null && c.Dispatcher.Current.Status == RequestStatus.Pending },
                { "request-met", c => c.SecondsSinceResolved < 10.0 && c.LastResolvedMet },
                { "request-missed", c => c.SecondsSinceResolved < 10.0 && !c.LastResolvedMet },
                { "request-streak", c => c.Dispatcher.Streak >= 3 },
                { "idle-zero-power", c => c.IdleZeroPowerSeconds > 900.0 },
                { "high-power", c => c.Plant.Core.Power > 1.05 },
                { "fast-time", c => c.TimeScale >= 30.0 },
            };

        public sealed class MessageState
        {
            public string Id = "";
            public bool Prev;
            public double LastFired = -Never;
        }

        public readonly List<MessageDef> Defs = new List<MessageDef>();
        // Trigger delegates resolved once in SetDefs, parallel to Defs (null = unknown trigger: Step still throws on lookup).
        private readonly List<Func<MessageContext, bool>> _resolved = new List<Func<MessageContext, bool>>();
        private readonly MessageContext _ctx = new MessageContext();
        public int InboxLimit = 50;

        // ---- serialisable state ----
        public double Clock;
        public bool Primed;
        public bool PrevSynced;
        public int PrevResolved;
        public double SecondsSinceSync = Never;
        public double SecondsSinceResolved = Never;
        public bool LastResolvedMet;
        public double IdleZeroPowerSeconds;
        public long NextSeq = 1;
        public readonly List<MessageState> States = new List<MessageState>();
        public readonly List<FiredMessage> Inbox = new List<FiredMessage>();

        public MessageEngine() { }

        public MessageEngine(IEnumerable<MessageDef> defs) { SetDefs(defs); }

        public void SetDefs(IEnumerable<MessageDef> defs)
        {
            Defs.Clear();
            States.Clear();
            _resolved.Clear();
            foreach (var d in defs)
            {
                Defs.Add(d);
                Func<MessageContext, bool> pred;
                Predicates.TryGetValue(d.Trigger ?? "", out pred);
                _resolved.Add(pred);
                States.Add(new MessageState { Id = d.Id });
            }
        }

        public void ResetState()
        {
            Clock = 0.0;
            Primed = false;
            PrevSynced = false;
            PrevResolved = 0;
            SecondsSinceSync = Never;
            SecondsSinceResolved = Never;
            LastResolvedMet = false;
            IdleZeroPowerSeconds = 0.0;
            NextSeq = 1;
            Inbox.Clear();
            for (int i = 0; i < States.Count; i++) { States[i].Prev = false; States[i].LastFired = -Never; }
        }

        // Advances timers and fires any message whose trigger just turned true outside its cooldown.
        public void Step(Plant plant, Dispatcher dispatcher, double dt, double timeScale)
        {
            if (!(dt > 0.0)) return;
            Clock += dt;
            bool synced = plant.Bop.Turbine.Synced;
            int resolved = dispatcher.MetCount + dispatcher.MissedCount;

            bool firstLook = !Primed;
            if (firstLook)
            {
                // First look: record the starting state so a loaded preset does not announce itself (the per-message
                // edge memory is seeded below without firing).
                Primed = true;
                PrevSynced = synced;
                PrevResolved = resolved;
            }
            if (synced && !PrevSynced) SecondsSinceSync = 0.0;
            else if (SecondsSinceSync < Never) SecondsSinceSync += dt;
            PrevSynced = synced;
            if (resolved != PrevResolved)
            {
                SecondsSinceResolved = 0.0;
                LastResolvedMet = dispatcher.History.Count > 0 && dispatcher.History[dispatcher.History.Count - 1].Status == RequestStatus.Met;
                PrevResolved = resolved;
            }
            else if (SecondsSinceResolved < Never) SecondsSinceResolved += dt;

            if (plant.Core.Power < 0.01 && !plant.Scrammed) IdleZeroPowerSeconds += dt;
            else IdleZeroPowerSeconds = 0.0;

            var ctx = _ctx;
            ctx.Plant = plant;
            ctx.Dispatcher = dispatcher;
            ctx.TimeScale = timeScale;
            ctx.IdleZeroPowerSeconds = IdleZeroPowerSeconds;
            ctx.SecondsSinceSync = SecondsSinceSync;
            ctx.SecondsSinceResolved = SecondsSinceResolved;
            ctx.LastResolvedMet = LastResolvedMet;

            for (int i = 0; i < Defs.Count; i++)
            {
                var def = Defs[i];
                var st = States[i];
                var pred = i < _resolved.Count ? _resolved[i] : null;
                bool now = (pred ?? Predicates[def.Trigger])(ctx);
                if (!firstLook && now && !st.Prev && Clock - st.LastFired >= def.CooldownSeconds)
                {
                    st.LastFired = Clock;
                    Inbox.Add(new FiredMessage { Seq = NextSeq++, Id = def.Id, Sender = def.Sender, Text = def.Text, AtSeconds = Clock });
                    while (Inbox.Count > InboxLimit) Inbox.RemoveAt(0);
                }
                st.Prev = now;
            }
        }

        // ---- loading ----

        public static MessageEngine LoadFromFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new MessageEngine();
            return Parse(File.ReadAllText(path));
        }

        public static MessageEngine Parse(string json)
        {
            if (json == null) throw new MessageException("messages file is empty");
            var r = new Reader(json);
            var defs = new List<MessageDef>();
            var ids = new HashSet<string>();
            r.Expect('{');
            r.Ws();
            if (r.Peek() == '}') throw new MessageException("messages file needs a \"messages\" array");
            bool sawMessages = false;
            while (true)
            {
                string key = r.ReadString();
                r.Expect(':');
                if (key != "messages") throw new MessageException("unknown top-level key \"" + key + "\"");
                if (sawMessages) throw new MessageException("duplicate key \"messages\"");
                sawMessages = true;
                r.Expect('[');
                r.Ws();
                if (r.Peek() == ']') r.Pos++;
                else
                {
                    while (true)
                    {
                        var def = ReadDef(r);
                        if (def.Id.Length == 0) throw new MessageException("message " + (defs.Count + 1) + " has no id");
                        if (!ids.Add(def.Id)) throw new MessageException("duplicate message id \"" + def.Id + "\"");
                        if (def.Sender.Length == 0) throw new MessageException("message \"" + def.Id + "\" has no sender");
                        if (def.Text.Length == 0) throw new MessageException("message \"" + def.Id + "\" has no text");
                        if (!Predicates.ContainsKey(def.Trigger))
                            throw new MessageException("message \"" + def.Id + "\" has unknown trigger \"" + def.Trigger + "\"");
                        if (!(def.CooldownSeconds >= 0.0)) throw new MessageException("message \"" + def.Id + "\" has a negative cooldown");
                        defs.Add(def);
                        r.Ws();
                        if (r.Peek() == ',') { r.Pos++; continue; }
                        r.Expect(']');
                        break;
                    }
                }
                r.Ws();
                if (r.Peek() == ',') { r.Pos++; continue; }
                r.Expect('}');
                break;
            }
            r.Ws();
            if (r.Pos != json.Length) throw new MessageException("unexpected text after the messages object");
            if (!sawMessages) throw new MessageException("messages file needs a \"messages\" array");
            return new MessageEngine(defs);
        }

        private static MessageDef ReadDef(Reader r)
        {
            var d = new MessageDef();
            var seen = new HashSet<string>();
            r.Expect('{');
            while (true)
            {
                string key = r.ReadString();
                r.Expect(':');
                if (!seen.Add(key)) throw new MessageException("duplicate message key \"" + key + "\"");
                switch (key)
                {
                    case "id": d.Id = r.ReadString(); break;
                    case "sender": d.Sender = r.ReadString(); break;
                    case "text": d.Text = r.ReadString(); break;
                    case "trigger": d.Trigger = r.ReadString(); break;
                    case "cooldownSeconds": d.CooldownSeconds = r.ReadNumber(key); break;
                    default: throw new MessageException("unknown message key \"" + key + "\"");
                }
                r.Ws();
                if (r.Peek() == ',') { r.Pos++; continue; }
                r.Expect('}');
                break;
            }
            return d;
        }

        // Minimal JSON reader for this one shape: objects, arrays, strings (with escapes) and numbers.
        private sealed class Reader
        {
            private readonly string _s;
            public int Pos;
            public Reader(string s) { _s = s; }

            public void Ws() { while (Pos < _s.Length && char.IsWhiteSpace(_s[Pos])) Pos++; }
            public char Peek() { Ws(); return Pos < _s.Length ? _s[Pos] : '\0'; }

            public void Expect(char c)
            {
                Ws();
                if (Pos >= _s.Length || _s[Pos] != c)
                    throw new MessageException("expected '" + c + "' at offset " + Pos);
                Pos++;
            }

            public double ReadNumber(string key)
            {
                Ws();
                int start = Pos;
                while (Pos < _s.Length && _s[Pos] != ',' && _s[Pos] != '}' && !char.IsWhiteSpace(_s[Pos])) Pos++;
                string raw = _s.Substring(start, Pos - start);
                double v;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || double.IsNaN(v) || double.IsInfinity(v))
                    throw new MessageException("value for \"" + key + "\" is not a finite number: " + raw);
                return v;
            }

            public string ReadString()
            {
                Ws();
                if (Pos >= _s.Length || _s[Pos] != '"') throw new MessageException("expected a quoted string at offset " + Pos);
                Pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (Pos >= _s.Length) throw new MessageException("unterminated string");
                    char ch = _s[Pos++];
                    if (ch == '"') break;
                    if (ch != '\\') { sb.Append(ch); continue; }
                    if (Pos >= _s.Length) throw new MessageException("unterminated escape");
                    char e = _s[Pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (Pos + 4 > _s.Length) throw new MessageException("bad \\u escape");
                            sb.Append((char)int.Parse(_s.Substring(Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            Pos += 4;
                            break;
                        default: throw new MessageException("unsupported escape \\" + e);
                    }
                }
                return sb.ToString();
            }
        }
    }
}
