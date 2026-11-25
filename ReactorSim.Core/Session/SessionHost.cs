// ReactorSim.Core - host side of the shared-crew session contract (Phase 3, U3 and U4, KTD1-KTD13).
// A pure state machine: no sockets, threads, clocks or entropy of its own. The transport calls the four inputs below
// with its monotonic seconds, sends what DrainOutbox returns, and reads DrainEvents for diagnostics.
//
// WIRE SHAPES. Every frame is one UTF-8 JSON line (transport adds and strips the "\n"):
//   {"v":1,"t":<type>,"seq":<int>,"sid":<session id>,"body":{...}}
// "sid" may be absent only on hello. Every host frame carries the session id. Host frames use one host-wide strictly
// increasing "seq"; clients number their own frames from 1, strictly increasing, and the host keeps the last value per
// peer across reconnects (the hello counts: a client's first frame after hello must use a higher number).
//
//   hello      (client -> host, no sid needed)
//     {"schema":<int, client's world schema>, "joinCode":"123456", "name":"<display name>",
//      "token":"<32 lowercase hex, optional: present means resume>"}
//     With a token the envelope "sid" should be the session id the token came from (a mismatch gives SessionUnknown) and
//     joinCode/name are ignored. Without a token joinCode and name are required.
//   welcome    (host -> client) always the first frame on a connection that joined or resumed
//     {"hostId":"<16 hex>", "sid":"<32 hex>", "peerId":"p1", "token":"<32 hex>", "graceSeconds":30,
//      "world":<world body>}
//   reject     (host -> client) a refusal that identifies what it refused
//     {"code":<SessionErrorCode int>, "name":"<enum name>", "seq":<seq of the refused frame, 0 when none>}
//     Fatal refusals are sent with close-after: the transport flushes the frame, then closes. BadJoinCode on a hello,
//     NotJoined for a crew_update before joining, and per-message crew_update refusals (InvalidCrewState,
//     RateLimited) leave the connection open: the client keeps waiting or carries on.
//   world      (host -> client, every 0.25 s to Active peers)
//     the WorldSnapshot.Encode body: {"schema","sessionTick","snapshotSeq","clockPaused","clockScale","plant","crew":[...]}
//     Apply only when snapshotSeq is newer than the last one applied; the Welcome's world counts.
//   intent     (client -> host)
//     {"kind":"<IntentKind name>", "target":"<string, optional, max 64 chars>", "value":<number>, "atMinute":<number, optional>}
//     Any "issuer" claim is ignored: the host records the peer id.
//   intent_result (host -> client, one for every checked intent, including Duplicate and NotJoined)
//     {"seq":<seq of the intent>, "code":<int, 0 = queued by the host>, "accepted":<bool>}
//     "Accepted" means queued, not applied; an apply-time rejection shows in a later world.
//   crew_update (client -> host)
//     {"loc":"<location id>", "x":<n>, "y":<n>, "z":<n>, "yaw":<n>, "pose":"standing"|"seated", "name":"<optional>"}
//     Success has no reply: the new entry appears in the next world. Refusal is a non-fatal reject with the update's seq.
//   heartbeat  (client -> host) {} - liveness only, no reply. The host sends no heartbeat; world frames prove it is alive.
//   goodbye    (client -> host) {} - frees the slot at once. The host closes the connection, no reply.
//
// The host itself has no crew entry: the crew list holds one entry per remote peer (Active or Stale). A new peer starts at
// location "unplaced" at the origin, standing, with the name from its hello.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ReactorSim.Session
{
    public enum PeerState
    {
        Active = 0,
        Stale = 1,
        Closed = 2
    }

    public enum SessionEventKind
    {
        PeerJoined,
        PeerResumed,
        PeerStale,
        // The peer's slot was freed: goodbye, a violation, or the grace window expired. Code says why (None for goodbye).
        PeerLeft,
        ConnectionRefused,
        IntentQueued,
        IntentRefused,
        CrewRefused
    }

    public sealed class SessionEvent
    {
        public SessionEventKind Kind;
        public int ConnectionId;
        public string PeerId;
        public SessionErrorCode Code;
        // Sequence number of the refused or queued frame, 0 when not applicable.
        public long Seq;
    }

    public sealed class OutboxEntry
    {
        public int ConnectionId;
        // The frame without its newline; the transport appends "\n". Null means "close only, no frame".
        public string Frame;
        // The transport flushes the frame, then closes the connection.
        public bool CloseAfter;
        internal bool IsWorld;
    }

    public sealed class SessionPeer
    {
        public string Id { get; internal set; }
        public PeerState State { get; internal set; }
        // -1 when the peer has no live connection (Stale or Closed).
        public int ConnectionId { get; internal set; }
        public long LastSeq { get; internal set; }
        public string DisplayName { get { return Crew.DisplayName; } }

        internal byte[] Token;
        internal CrewMember Crew;
        internal double LastRx;
        internal double StaleSince;
        internal double LastWorldAt;
        internal TokenBucket IntentBucket;
        internal TokenBucket CrewBucket;
    }

    public sealed class SessionHost
    {
        // Original design choice: location of a peer that has not sent a crew update yet; valid under the default rule.
        public const string InitialLocationId = "unplaced";
        // Original design choice: closed-peer records kept so their tokens answer TokenExpired; older ones answer TokenInvalid.
        private const int MaxClosedPeerRecords = 64;
        private const double TimerEpsilon = 1e-9;
        private const double FrameWindowSeconds = 1.0;

        private sealed class Conn
        {
            public int Id;
            public SessionFrameReader Reader;
            public double OpenedAt;
            public bool Handshaking = true;
            public SessionPeer Peer;
            public int BadCodes;
            public double WindowStart;
            public int WindowCount;
        }

        private readonly Simulation _sim;
        private readonly Func<string, bool> _locationValidator;
        private readonly List<Conn> _conns = new List<Conn>();
        private readonly List<SessionPeer> _peers = new List<SessionPeer>();
        private readonly IReadOnlyList<SessionPeer> _peersView;
        private readonly List<OutboxEntry> _outbox = new List<OutboxEntry>();
        private readonly List<SessionEvent> _events = new List<SessionEvent>();
        private double _now;
        private bool _started;
        private long _hostSeq;
        private long _snapshotSeq;
        private int _peerCounter;

        public SessionHost(Simulation sim, ISessionEntropy entropy, Func<string, bool> locationValidator = null)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            if (entropy == null) throw new ArgumentNullException(nameof(entropy));
            _sim = sim;
            _locationValidator = locationValidator;
            _peersView = _peers.AsReadOnly();
            _entropy = entropy;

            var hostId = new byte[8];
            var sid = new byte[16];
            var code = new byte[4];
            entropy.Fill(hostId);
            entropy.Fill(sid);
            entropy.Fill(code);
            HostId = SessionIds.ToHex(hostId);
            SessionId = SessionIds.ToHex(sid);
            JoinCode = SessionIds.JoinCodeFrom(code);
        }

        private readonly ISessionEntropy _entropy;

        public string HostId { get; private set; }
        public string SessionId { get; private set; }
        public string JoinCode { get; private set; }

        // Only the host sets this, per session; it defaults to off (KTD10 step 6).
        public bool AllowRemoteClock { get; set; }

        public IReadOnlyList<SessionPeer> Peers { get { return _peersView; } }
        public int ConnectionCount { get { return _conns.Count; } }

        // Clones of the crew entries for every peer that holds a slot (Active or Stale), in join order.
        public List<CrewMember> GetCrew()
        {
            var crew = new List<CrewMember>();
            for (int i = 0; i < _peers.Count; i++)
            {
                if (_peers[i].State != PeerState.Closed)
                    crew.Add(_peers[i].Crew.Clone());
            }
            return crew;
        }

        public List<OutboxEntry> DrainOutbox()
        {
            var drained = new List<OutboxEntry>(_outbox);
            _outbox.Clear();
            return drained;
        }

        public List<SessionEvent> DrainEvents()
        {
            var drained = new List<SessionEvent>(_events);
            _events.Clear();
            return drained;
        }

        // ---- inputs (KTD13) ----

        public void OnConnected(int connectionId, double nowSeconds)
        {
            Advance(nowSeconds);
            ExpireGrace();
            if (Find(connectionId) != null)
                return;

            var conn = new Conn
            {
                Id = connectionId,
                Reader = new SessionFrameReader(SessionProtocol.ControlFrameMaxBytes),
                OpenedAt = _now,
                WindowStart = _now
            };
            if (CountHandshaking() >= SessionProtocol.MaxHandshakingConnections)
            {
                QueueReject(connectionId, SessionErrorCode.TooManyConnections, 0, true);
                RaiseRefused(connectionId, SessionErrorCode.TooManyConnections);
                return;
            }
            _conns.Add(conn);
        }

        public void OnBytes(int connectionId, byte[] data, int offset, int count, double nowSeconds)
        {
            Advance(nowSeconds);
            ExpireGrace();
            Conn c = Find(connectionId);
            if (c == null)
                return;

            List<SessionFrame> frames = c.Reader.Feed(data, offset, count);
            for (int i = 0; i < frames.Count; i++)
            {
                if (!_conns.Contains(c))
                    break;
                HandleFrame(c, frames[i]);
            }
        }

        public void OnDisconnected(int connectionId, double nowSeconds)
        {
            Advance(nowSeconds);
            ExpireGrace();
            Conn c = Find(connectionId);
            if (c == null)
                return;
            _conns.Remove(c);
            SessionPeer peer = c.Peer;
            if (peer != null && peer.State == PeerState.Active && peer.ConnectionId == c.Id)
                MarkStale(peer);
        }

        public void Tick(double nowSeconds)
        {
            Advance(nowSeconds);
            ExpireGrace();

            for (int i = 0; i < _peers.Count; i++)
            {
                SessionPeer peer = _peers[i];
                if (peer.State == PeerState.Active && _now - peer.LastRx >= SessionProtocol.StaleAfterSilenceSeconds - TimerEpsilon)
                {
                    Conn c = Find(peer.ConnectionId);
                    if (c != null)
                    {
                        CloseSilently(c);
                    }
                    MarkStale(peer);
                }
            }

            var snapshot = new List<Conn>(_conns);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Conn c = snapshot[i];
                if (c.Handshaking && _now - c.OpenedAt >= SessionProtocol.HandshakeTimeoutSeconds - TimerEpsilon)
                    Refuse(c, SessionErrorCode.HandshakeTimeout, 0);
            }

            BroadcastWorlds();
        }

        // ---- time ----

        // A value earlier than the last one counts as no time passing (KTD2); a non-finite value is ignored.
        private void Advance(double now)
        {
            if (!Physics.IsFinite(now))
                return;
            if (!_started || now > _now)
            {
                _now = now;
                _started = true;
            }
        }

        private void ExpireGrace()
        {
            for (int i = 0; i < _peers.Count; i++)
            {
                SessionPeer peer = _peers[i];
                if (peer.State == PeerState.Stale && _now - peer.StaleSince >= SessionProtocol.GraceSeconds - TimerEpsilon)
                    ClosePeer(peer, SessionErrorCode.TokenExpired);
            }
        }

        private void MarkStale(SessionPeer peer)
        {
            peer.State = PeerState.Stale;
            peer.ConnectionId = -1;
            peer.StaleSince = _now;
            AddEvent(SessionEventKind.PeerStale, -1, peer.Id, SessionErrorCode.None, 0);
        }

        // Frees the slot for good: the token now answers TokenExpired.
        private void ClosePeer(SessionPeer peer, SessionErrorCode reason)
        {
            if (peer.State == PeerState.Closed)
                return;
            peer.State = PeerState.Closed;
            peer.ConnectionId = -1;
            AddEvent(SessionEventKind.PeerLeft, -1, peer.Id, reason, 0);
            TrimClosedPeers();
        }

        private void TrimClosedPeers()
        {
            int closed = 0;
            for (int i = 0; i < _peers.Count; i++)
                if (_peers[i].State == PeerState.Closed) closed++;
            for (int i = 0; i < _peers.Count && closed > MaxClosedPeerRecords; )
            {
                if (_peers[i].State == PeerState.Closed)
                {
                    _peers.RemoveAt(i);
                    closed--;
                }
                else
                {
                    i++;
                }
            }
        }

        // ---- frame handling ----

        private void HandleFrame(Conn c, SessionFrame frame)
        {
            if (frame.IsFault)
            {
                Refuse(c, frame.Fault, 0);
                return;
            }

            if (_now - c.WindowStart >= FrameWindowSeconds)
            {
                c.WindowStart = _now;
                c.WindowCount = 0;
            }
            c.WindowCount++;
            if (c.WindowCount > SessionProtocol.MaxFramesPerSecond)
            {
                Refuse(c, SessionErrorCode.RateLimited, 0);
                return;
            }

            SessionEnvelopeResult decoded = SessionEnvelope.Decode(frame.Line);
            if (!decoded.Ok)
            {
                Refuse(c, decoded.Error, 0);
                return;
            }

            if (c.Handshaking)
                HandleHandshaking(c, decoded.Envelope);
            else
                HandleActive(c, decoded.Envelope);
        }

        private void HandleHandshaking(Conn c, SessionEnvelope env)
        {
            switch (env.T)
            {
                case SessionProtocol.TypeHello:
                    HandleHello(c, env);
                    return;
                case SessionProtocol.TypeIntent:
                    QueueIntentResult(c.Id, env.Seq, SessionErrorCode.NotJoined);
                    AddEvent(SessionEventKind.IntentRefused, c.Id, null, SessionErrorCode.NotJoined, env.Seq);
                    return;
                case SessionProtocol.TypeCrewUpdate:
                    QueueReject(c.Id, SessionErrorCode.NotJoined, env.Seq, false);
                    AddEvent(SessionEventKind.CrewRefused, c.Id, null, SessionErrorCode.NotJoined, env.Seq);
                    return;
                case SessionProtocol.TypeHeartbeat:
                    return;
                case SessionProtocol.TypeGoodbye:
                    CloseSilently(c);
                    return;
                default:
                    Refuse(c, SessionErrorCode.ProtocolViolation, env.Seq);
                    return;
            }
        }

        private void HandleActive(Conn c, SessionEnvelope env)
        {
            SessionPeer peer = c.Peer;
            peer.LastRx = _now;

            if (env.T == SessionProtocol.TypeHello)
            {
                Refuse(c, SessionErrorCode.ProtocolViolation, env.Seq);
                return;
            }
            if (env.Sid != SessionId)
            {
                Refuse(c, SessionErrorCode.SessionUnknown, env.Seq);
                return;
            }

            if (env.T == SessionProtocol.TypeGoodbye)
            {
                CloseSilently(c);
                ClosePeer(peer, SessionErrorCode.None);
                return;
            }
            if (env.T != SessionProtocol.TypeIntent && env.T != SessionProtocol.TypeCrewUpdate && env.T != SessionProtocol.TypeHeartbeat)
            {
                Refuse(c, SessionErrorCode.ProtocolViolation, env.Seq);
                return;
            }

            if (peer.LastSeq >= SessionProtocol.MaxWireInteger)
            {
                Refuse(c, SessionErrorCode.ProtocolViolation, env.Seq);
                return;
            }
            bool fresh = env.Seq > peer.LastSeq;
            if (fresh)
                peer.LastSeq = env.Seq;

            switch (env.T)
            {
                case SessionProtocol.TypeIntent:
                    HandleIntent(c, peer, env, fresh);
                    return;
                case SessionProtocol.TypeCrewUpdate:
                    if (fresh)
                        HandleCrewUpdate(c, peer, env);
                    return;
                default:
                    return;
            }
        }

        // ---- hello (U3) ----

        private void HandleHello(Conn c, SessionEnvelope env)
        {
            JsonValue body = env.Body;
            long clientSchema;
            if (!SessionJson.TryWholeNumber(body.Get("schema"), out clientSchema))
            {
                Refuse(c, SessionErrorCode.ProtocolViolation, env.Seq);
                return;
            }
            if (clientSchema < SessionProtocol.WorldSchemaVersion)
            {
                Refuse(c, SessionErrorCode.UnsupportedSchema, env.Seq);
                return;
            }

            JsonValue tokenValue = body.Get("token");
            if (tokenValue != null)
            {
                if (tokenValue.Kind != JsonKind.String)
                {
                    Refuse(c, SessionErrorCode.ProtocolViolation, env.Seq);
                    return;
                }
                HandleResume(c, env, tokenValue.Text);
                return;
            }

            JsonValue codeValue = body.Get("joinCode");
            JsonValue nameValue = body.Get("name");
            if (codeValue == null || codeValue.Kind != JsonKind.String || nameValue == null || nameValue.Kind != JsonKind.String)
            {
                Refuse(c, SessionErrorCode.ProtocolViolation, env.Seq);
                return;
            }
            if (codeValue.Text != JoinCode)
            {
                c.BadCodes++;
                if (c.BadCodes >= SessionProtocol.MaxBadJoinCodes)
                {
                    Refuse(c, SessionErrorCode.BadJoinCode, env.Seq);
                }
                else
                {
                    QueueReject(c.Id, SessionErrorCode.BadJoinCode, env.Seq, false);
                    RaiseRefused(c.Id, SessionErrorCode.BadJoinCode);
                }
                return;
            }
            if (HasSlotTaken())
            {
                Refuse(c, SessionErrorCode.SessionFull, env.Seq);
                return;
            }

            var crew = new CrewMember
            {
                PeerId = "p" + (_peerCounter + 1).ToString(CultureInfo.InvariantCulture),
                DisplayName = nameValue.Text.Trim(),
                LocationId = InitialLocationId,
                Pose = CrewPose.Standing
            };
            if (WorldSnapshot.ValidateCrewMember(crew, null) != SessionErrorCode.None)
            {
                Refuse(c, SessionErrorCode.InvalidCrewState, env.Seq);
                return;
            }

            _peerCounter++;
            var token = new byte[SessionProtocol.TokenBytes];
            _entropy.Fill(token);
            var peer = new SessionPeer
            {
                Id = crew.PeerId,
                State = PeerState.Active,
                ConnectionId = c.Id,
                LastSeq = env.Seq,
                Token = token,
                Crew = crew,
                LastRx = _now,
                LastWorldAt = _now,
                IntentBucket = new TokenBucket(SessionProtocol.IntentRatePerSecond, SessionProtocol.IntentBurst, _now),
                CrewBucket = new TokenBucket(SessionProtocol.CrewRatePerSecond, SessionProtocol.CrewBurst, _now)
            };
            _peers.Add(peer);
            Bind(c, peer);
            QueueWelcome(c, peer);
            AddEvent(SessionEventKind.PeerJoined, c.Id, peer.Id, SessionErrorCode.None, env.Seq);
        }

        private void HandleResume(Conn c, SessionEnvelope env, string tokenText)
        {
            if (env.Sid != null && env.Sid != SessionId)
            {
                Refuse(c, SessionErrorCode.SessionUnknown, env.Seq);
                return;
            }
            byte[] token;
            if (!SessionIds.TryParseHex(tokenText, SessionProtocol.TokenBytes, out token))
            {
                Refuse(c, SessionErrorCode.TokenInvalid, env.Seq);
                return;
            }

            SessionPeer match = null;
            for (int i = 0; i < _peers.Count; i++)
            {
                if (SessionIds.FixedTimeEquals(token, _peers[i].Token))
                    match = _peers[i];
            }
            if (match == null)
            {
                Refuse(c, SessionErrorCode.TokenInvalid, env.Seq);
                return;
            }
            if (match.State == PeerState.Closed)
            {
                Refuse(c, SessionErrorCode.TokenExpired, env.Seq);
                return;
            }

            if (match.State == PeerState.Active)
            {
                Conn old = Find(match.ConnectionId);
                if (old != null)
                {
                    _conns.Remove(old);
                    QueueReject(old.Id, SessionErrorCode.Superseded, 0, true);
                    RaiseRefused(old.Id, SessionErrorCode.Superseded);
                }
            }

            match.State = PeerState.Active;
            match.LastRx = _now;
            match.LastWorldAt = _now;
            if (env.Seq > match.LastSeq)
                match.LastSeq = env.Seq;
            Bind(c, match);
            QueueWelcome(c, match);
            AddEvent(SessionEventKind.PeerResumed, c.Id, match.Id, SessionErrorCode.None, env.Seq);
        }

        private bool HasSlotTaken()
        {
            for (int i = 0; i < _peers.Count; i++)
                if (_peers[i].State != PeerState.Closed) return true;
            return false;
        }

        private static void Bind(Conn c, SessionPeer peer)
        {
            c.Handshaking = false;
            c.Peer = peer;
            peer.ConnectionId = c.Id;
        }

        // ---- intents (U4, KTD10) ----

        private void HandleIntent(Conn c, SessionPeer peer, SessionEnvelope env, bool fresh)
        {
            SessionErrorCode code = Admit(peer, env, fresh);
            QueueIntentResult(c.Id, env.Seq, code);
            AddEvent(code == SessionErrorCode.None ? SessionEventKind.IntentQueued : SessionEventKind.IntentRefused, c.Id, peer.Id, code, env.Seq);
        }

        // Steps 1 (peer Active) is guaranteed by the caller; this runs steps 2-8 in order.
        private SessionErrorCode Admit(SessionPeer peer, SessionEnvelope env, bool fresh)
        {
            if (!fresh)
                return SessionErrorCode.Duplicate;

            SessionIntentResult decoded = SessionEnvelope.DecodeIntentBody(env.Body);
            if (!decoded.Ok)
                return decoded.Error;
            PlantIntent intent = decoded.Intent;

            if (!peer.IntentBucket.TryTake(_now))
                return SessionErrorCode.RateLimited;

            if (PlantIntent.ClassOf(intent.Kind) != IntentClass.Player)
                return SessionErrorCode.NotPermitted;

            if (SimClock.IsClockKind(intent.Kind) && !AllowRemoteClock)
                return SessionErrorCode.NotPermitted;

            return EnqueueForPeer(peer.Id, intent);
        }

        // Steps 7 and 8: the peer id overwrites any claimed issuer, then the simulation validates and queues.
        internal SessionErrorCode EnqueueForPeer(string peerId, PlantIntent intent)
        {
            intent.Issuer = peerId;
            try
            {
                _sim.EnqueueIntent(intent);
                return SessionErrorCode.None;
            }
            catch (IntentRejectedException)
            {
                return SessionErrorCode.NotPermitted;
            }
            catch (ArgumentException)
            {
                return SessionErrorCode.NonFiniteValue;
            }
        }

        // ---- crew updates (U4, KTD11) ----

        private void HandleCrewUpdate(Conn c, SessionPeer peer, SessionEnvelope env)
        {
            SessionErrorCode code;
            if (!peer.CrewBucket.TryTake(_now))
            {
                code = SessionErrorCode.RateLimited;
            }
            else
            {
                CrewMember candidate = BuildCandidate(peer.Crew, env.Body);
                if (candidate == null || WorldSnapshot.ValidateCrewMember(candidate, _locationValidator) != SessionErrorCode.None)
                {
                    code = SessionErrorCode.InvalidCrewState;
                }
                else
                {
                    candidate.DisplayName = candidate.DisplayName.Trim();
                    peer.Crew = candidate;
                    return;
                }
            }
            QueueReject(c.Id, code, env.Seq, false);
            AddEvent(SessionEventKind.CrewRefused, c.Id, peer.Id, code, env.Seq);
        }

        // Null when a field is missing or has the wrong type; value rules are checked by ValidateCrewMember.
        private static CrewMember BuildCandidate(CrewMember current, JsonValue body)
        {
            JsonValue name = body.Get("name");
            var candidate = new CrewMember { PeerId = current.PeerId };
            if (!WorldSnapshot.TryReadCrewFields(body, candidate))
                return null;
            if (name != null && name.Kind != JsonKind.String)
                return null;
            candidate.DisplayName = name != null ? name.Text : current.DisplayName;
            return candidate;
        }

        // ---- output ----

        private Conn Find(int id)
        {
            for (int i = 0; i < _conns.Count; i++)
                if (_conns[i].Id == id) return _conns[i];
            return null;
        }

        private int CountHandshaking()
        {
            int n = 0;
            for (int i = 0; i < _conns.Count; i++)
                if (_conns[i].Handshaking) n++;
            return n;
        }

        private void CloseSilently(Conn c)
        {
            _conns.Remove(c);
            Queue(c.Id, null, true, false);
        }

        private void AddEvent(SessionEventKind kind, int connectionId, string peerId, SessionErrorCode code, long seq)
        {
            _events.Add(new SessionEvent { Kind = kind, ConnectionId = connectionId, PeerId = peerId, Code = code, Seq = seq });
        }

        private void Queue(int connectionId, string frame, bool closeAfter, bool isWorld)
        {
            _outbox.Add(new OutboxEntry { ConnectionId = connectionId, Frame = frame, CloseAfter = closeAfter, IsWorld = isWorld });
        }

        private void QueueReject(int connectionId, SessionErrorCode code, long refSeq, bool closeAfter)
        {
            var sb = new StringBuilder(64);
            sb.Append("{\"code\":");
            SessionJson.AppendInteger(sb, (int)code);
            sb.Append(",\"name\":");
            SessionJson.AppendString(sb, code.ToString());
            sb.Append(",\"seq\":");
            SessionJson.AppendInteger(sb, refSeq);
            sb.Append('}');
            Queue(connectionId, SessionEnvelope.Encode(SessionProtocol.TypeReject, ++_hostSeq, SessionId, sb.ToString()), closeAfter, false);
        }

        private void QueueIntentResult(int connectionId, long seq, SessionErrorCode code)
        {
            var sb = new StringBuilder(64);
            sb.Append("{\"seq\":");
            SessionJson.AppendInteger(sb, seq);
            sb.Append(",\"code\":");
            SessionJson.AppendInteger(sb, (int)code);
            sb.Append(",\"accepted\":").Append(code == SessionErrorCode.None ? "true" : "false").Append('}');
            Queue(connectionId, SessionEnvelope.Encode(SessionProtocol.TypeIntentResult, ++_hostSeq, SessionId, sb.ToString()), false, false);
        }

        private void QueueWelcome(Conn c, SessionPeer peer)
        {
            WorldSnapshot world = WorldSnapshot.Capture(_sim, ++_snapshotSeq, GetCrew());
            var sb = new StringBuilder(4096);
            sb.Append("{\"hostId\":");
            SessionJson.AppendString(sb, HostId);
            sb.Append(",\"sid\":");
            SessionJson.AppendString(sb, SessionId);
            sb.Append(",\"peerId\":");
            SessionJson.AppendString(sb, peer.Id);
            sb.Append(",\"token\":");
            SessionJson.AppendString(sb, SessionIds.ToHex(peer.Token));
            sb.Append(",\"graceSeconds\":");
            SessionJson.AppendNumber(sb, SessionProtocol.GraceSeconds);
            sb.Append(",\"world\":").Append(world.Encode()).Append('}');
            Queue(c.Id, SessionEnvelope.Encode(SessionProtocol.TypeWelcome, ++_hostSeq, SessionId, sb.ToString()), false, false);
        }

        // Captures one snapshot for every peer that is due and replaces any unsent World on its connection.
        private void BroadcastWorlds()
        {
            var due = new List<SessionPeer>();
            for (int i = 0; i < _peers.Count; i++)
            {
                SessionPeer p = _peers[i];
                if (p.State == PeerState.Active && p.ConnectionId >= 0
                    && _now - p.LastWorldAt >= SessionProtocol.WorldIntervalSeconds - TimerEpsilon)
                    due.Add(p);
            }
            if (due.Count == 0)
                return;

            string body = WorldSnapshot.Capture(_sim, ++_snapshotSeq, GetCrew()).Encode();
            for (int i = 0; i < due.Count; i++)
            {
                SessionPeer p = due[i];
                p.LastWorldAt = _now;
                for (int j = _outbox.Count - 1; j >= 0; j--)
                {
                    if (_outbox[j].IsWorld && _outbox[j].ConnectionId == p.ConnectionId)
                        _outbox.RemoveAt(j);
                }
                Queue(p.ConnectionId, SessionEnvelope.Encode(SessionProtocol.TypeWorld, ++_hostSeq, SessionId, body), false, true);
            }
        }

        // Fatal refusal: a reject frame with close-after, then the connection and its peer binding are dropped.
        private void Refuse(Conn c, SessionErrorCode code, long refSeq)
        {
            QueueReject(c.Id, code, refSeq, true);
            RaiseRefused(c.Id, code);
            _conns.Remove(c);
            if (c.Peer != null && c.Peer.ConnectionId == c.Id)
                ClosePeer(c.Peer, code);
        }

        private void RaiseRefused(int connectionId, SessionErrorCode code)
        {
            AddEvent(SessionEventKind.ConnectionRefused, connectionId, null, code, 0);
        }
    }
}
