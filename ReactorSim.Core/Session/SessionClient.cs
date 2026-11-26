// ReactorSim.Core - client side of the shared-crew session contract (Phase 3, U5, KTD1, KTD2, KTD8, KTD9, KTD13, KTD14).
// A pure state machine for ONE host connection: no sockets, threads, clocks or entropy. It speaks exactly the wire
// shapes documented at the top of SessionHost.cs. The transport drives it with its monotonic seconds:
//   Connect            the user asked to join; the client queues a Connect output and enters Joining.
//   OnConnected        the transport opened the connection the last Connect output asked for; the client sends its hello.
//   OnBytes            bytes arrived on the current connection.
//   OnDisconnected     the current connection closed, or an open attempt failed.
//   Tick               time passed; timers (handshake, heartbeat, silence, reconnect backoff) run here.
// and it sends what DrainOutbox returns, then reads DrainEvents for the UI.
//
// Outputs (ClientOutput): Connect = close any current connection, open a fresh one, then call OnConnected (or
// OnDisconnected when it cannot open); Send = write the frame plus "\n", and when CloseAfter is set flush then close;
// Close = close the connection now.
//
// Policy notes (all measured on the injected clock; a value earlier than the last one counts as no time passing):
// - Handshake timeout: 5 s from the Connect request, restarted when the connection opens and on RetryJoin. A connection
//   that never opens is therefore also bounded.
// - BadJoinCode is the one non-fatal refusal while Joining: the client raises Refused and stays Joining so the user can
//   call RetryJoin with a corrected code on the same connection. If the host closes after the fifth bad code, the client
//   fails with that code; if nothing happens it fails with HandshakeTimeout.
// - Reconnect attempts go out 1, 2, 4, 8 and 8 s after the previous one (cumulative 1, 3, 7, 15, 23 s after the loss);
//   after the last attempt the client waits the final 8 s, so it fails as HostLost 31 s after the loss at the latest.
//   One attempt is started per Tick call.
// - The host-seq check (KTD9) and the session-id check apply only while Active. Before a Welcome the client cannot
//   know the new host's numbering, and a Reject from a restarted host carries the new session id by design.
// - A malformed host frame, an oversize frame or an undecodable world is a ProtocolError failure: TCP does not corrupt
//   frames, so a host that sends them is broken, and the stream cannot be resynchronized (KTD4).

using System;
using System.Collections.Generic;
using System.Text;

namespace ReactorSim.Session
{
    public enum ClientState
    {
        Idle = 0,
        Joining = 1,
        Active = 2,
        Reconnecting = 3,
        Failed = 4
    }

    public enum ClientOutputKind
    {
        Connect = 0,
        Send = 1,
        Close = 2
    }

    public sealed class ClientOutput
    {
        public ClientOutputKind Kind;
        // The frame without its newline; null for Connect and Close.
        public string Frame;
        // Send only: flush the frame, then close the connection.
        public bool CloseAfter;
    }

    public enum ClientEventKind
    {
        Joined,
        // A refusal the client survives: BadJoinCode while Joining, or a per-message reject while Active (Seq names it).
        Refused,
        Reconnecting,
        Resumed,
        Failed,
        // Code None means the host queued the intent; any other code is the refusal. Seq 0 means refused locally.
        IntentResult,
        WorldApplied
    }

    public enum ClientFailure
    {
        None = 0,
        Refused = 1,
        HandshakeTimeout = 2,
        HostLost = 3,
        SessionLost = 4,
        Superseded = 5,
        ProtocolError = 6
    }

    public sealed class ClientEvent
    {
        public ClientEventKind Kind;
        public SessionErrorCode Code;
        public ClientFailure Failure;
        public string PeerId;
        public long Seq;
        public long SnapshotSeq;
        public bool Accepted;
    }

    public sealed class SessionClient
    {
        private const double TimerEpsilon = 1e-9;
        // Original design choice: how many intent results and unanswered intents are remembered.
        private const int IntentMemory = 1024;

        private readonly Simulation _sim;
        private readonly string _displayName;
        private string _joinCode;
        private readonly List<ClientOutput> _outbox = new List<ClientOutput>();
        private readonly List<ClientEvent> _events = new List<ClientEvent>();
        private readonly HashSet<long> _pendingIntents = new HashSet<long>();
        private readonly Dictionary<long, SessionErrorCode> _results = new Dictionary<long, SessionErrorCode>();
        private List<CrewMember> _crew = new List<CrewMember>();

        private SessionFrameReader _reader;
        private ClientState _state = ClientState.Idle;
        private double _now;
        private bool _started;
        private bool _connected;
        private bool _awaitingOpen;
        private long _nextSeq = 1;
        private long _lastHostSeq;
        private long _lastSnapshotSeq = -1;
        private double _lastRx;
        private double _lastHeartbeat;
        private double _handshakeDeadline;
        private double _reconnectStart;
        private int _attemptsMade;
        private SessionErrorCode _lastJoinReject;

        public SessionClient(Simulation sim, string displayName, string joinCode)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            if (displayName == null) throw new ArgumentNullException(nameof(displayName));
            if (joinCode == null) throw new ArgumentNullException(nameof(joinCode));
            _sim = sim;
            _displayName = displayName;
            _joinCode = joinCode;
        }

        // The client's mirror of the host plant; it is restored from World frames and never ticked by the client (KTD14).
        public Simulation Sim { get { return _sim; } }
        public ClientState State { get { return _state; } }
        public string PeerId { get; private set; }
        public string SessionId { get; private set; }
        public string Token { get; private set; }
        public double GraceSeconds { get; private set; }
        // snapshotSeq of the newest applied world; -1 before the first one.
        public long LastSnapshotSeq { get { return _lastSnapshotSeq; } }
        public IReadOnlyList<CrewMember> Crew { get { return _crew; } }
        public SessionErrorCode FailureCode { get; private set; }
        public ClientFailure FailureReason { get; private set; }

        public List<ClientOutput> DrainOutbox()
        {
            var drained = new List<ClientOutput>(_outbox);
            _outbox.Clear();
            return drained;
        }

        public List<ClientEvent> DrainEvents()
        {
            var drained = new List<ClientEvent>(_events);
            _events.Clear();
            return drained;
        }

        // The first result the host reported for an intent this client sent; later results for it are ignored.
        public bool TryGetIntentResult(long seq, out SessionErrorCode code)
        {
            return _results.TryGetValue(seq, out code);
        }

        // ---- user inputs ----

        // Idle -> Joining. Returns false (and does nothing) from any other state.
        public bool Connect(double nowSeconds)
        {
            Advance(nowSeconds);
            if (_state != ClientState.Idle)
                return false;
            _state = ClientState.Joining;
            _lastHostSeq = 0;
            _lastSnapshotSeq = -1;
            _crew = new List<CrewMember>();
            _lastJoinReject = SessionErrorCode.None;
            _handshakeDeadline = _now + SessionProtocol.HandshakeTimeoutSeconds;
            RequestConnection();
            return true;
        }

        // While Joining, replaces the join code (after a BadJoinCode) and sends a fresh hello on the open connection.
        public void RetryJoin(string joinCode, double nowSeconds)
        {
            if (joinCode == null) throw new ArgumentNullException(nameof(joinCode));
            Advance(nowSeconds);
            if (_state != ClientState.Joining)
                return;
            _joinCode = joinCode;
            _lastJoinReject = SessionErrorCode.None;
            _handshakeDeadline = _now + SessionProtocol.HandshakeTimeoutSeconds;
            if (_connected)
                SendHello(false);
        }

        // Returns the sequence number used, or 0 when refused locally (an IntentResult event with Seq 0 says why).
        public long SendIntent(PlantIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            if (_state != ClientState.Active)
                return RefuseLocally(SessionErrorCode.NotJoined);
            if (!Enum.IsDefined(typeof(IntentKind), intent.Kind)
                || (intent.Target != null && intent.Target.Length > SessionProtocol.IntentStringMaxChars))
                return RefuseLocally(SessionErrorCode.InvalidIntent);
            if (!Physics.IsFinite(intent.Value) || !Physics.IsFinite(intent.AtMinute))
                return RefuseLocally(SessionErrorCode.NonFiniteValue);

            var sb = new StringBuilder(96);
            sb.Append("{\"kind\":");
            SessionJson.AppendString(sb, intent.Kind.ToString());
            sb.Append(",\"target\":");
            SessionJson.AppendString(sb, intent.Target ?? "");
            sb.Append(",\"value\":");
            SessionJson.AppendNumber(sb, intent.Value);
            sb.Append(",\"atMinute\":");
            SessionJson.AppendNumber(sb, intent.AtMinute);
            sb.Append('}');

            long seq = _nextSeq++;
            _pendingIntents.Add(seq);
            SendFrame(SessionProtocol.TypeIntent, seq, SessionId, sb.ToString(), false);
            PruneIntentMemory();
            return seq;
        }

        // Returns the sequence number used, or 0 when refused locally. The host validates the location rule; the client
        // only refuses what it cannot encode.
        public long SendCrewUpdate(CrewMember update)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            if (_state != ClientState.Active)
                return RefuseLocally(SessionErrorCode.NotJoined);
            if (update.LocationId == null || !Physics.IsFinite(update.X) || !Physics.IsFinite(update.Y) || !Physics.IsFinite(update.Z) || !Physics.IsFinite(update.YawDeg))
                return RefuseLocally(SessionErrorCode.InvalidCrewState);

            var sb = new StringBuilder(128);
            sb.Append("{\"loc\":");
            SessionJson.AppendString(sb, update.LocationId);
            sb.Append(",\"x\":");
            SessionJson.AppendNumber(sb, update.X);
            sb.Append(",\"y\":");
            SessionJson.AppendNumber(sb, update.Y);
            sb.Append(",\"z\":");
            SessionJson.AppendNumber(sb, update.Z);
            sb.Append(",\"yaw\":");
            SessionJson.AppendNumber(sb, update.YawDeg);
            sb.Append(",\"pose\":");
            SessionJson.AppendString(sb, CrewPoseNames.ToWire(update.Pose));
            if (!string.IsNullOrEmpty(update.DisplayName))
            {
                sb.Append(",\"name\":");
                SessionJson.AppendString(sb, update.DisplayName);
            }
            sb.Append('}');

            long seq = _nextSeq++;
            SendFrame(SessionProtocol.TypeCrewUpdate, seq, SessionId, sb.ToString(), false);
            return seq;
        }

        // Active: sends Goodbye (the host frees the slot at once) and returns to Idle. Joining or Reconnecting: drops the
        // connection without a Goodbye, because none can be delivered. Failed: same as AcknowledgeFailure.
        public void Leave(double nowSeconds)
        {
            Advance(nowSeconds);
            switch (_state)
            {
                case ClientState.Idle:
                    return;
                case ClientState.Active:
                    SendFrame(SessionProtocol.TypeGoodbye, _nextSeq++, SessionId, "{}", true);
                    break;
                case ClientState.Joining:
                case ClientState.Reconnecting:
                    _outbox.Add(new ClientOutput { Kind = ClientOutputKind.Close });
                    break;
            }
            ResetToIdle();
        }

        public void AcknowledgeFailure()
        {
            if (_state == ClientState.Failed)
                ResetToIdle();
        }

        // ---- transport inputs ----

        public void OnConnected(double nowSeconds)
        {
            Advance(nowSeconds);
            if (!_awaitingOpen || (_state != ClientState.Joining && _state != ClientState.Reconnecting))
                return;
            _awaitingOpen = false;
            _connected = true;
            _reader = new SessionFrameReader(SessionProtocol.StateFrameMaxBytes);
            if (_state == ClientState.Joining)
            {
                _handshakeDeadline = _now + SessionProtocol.HandshakeTimeoutSeconds;
                SendHello(false);
            }
            else
            {
                SendHello(true);
            }
        }

        public void OnBytes(byte[] data, int offset, int count, double nowSeconds)
        {
            Advance(nowSeconds);
            if (!_connected || _reader == null)
                return;
            if (_state != ClientState.Joining && _state != ClientState.Active && _state != ClientState.Reconnecting)
                return;

            List<SessionFrame> frames = _reader.Feed(data, offset, count);
            SessionFrameReader reader = _reader;
            for (int i = 0; i < frames.Count; i++)
            {
                if (!_connected || _reader != reader)
                    break;
                ProcessFrame(frames[i]);
            }
        }

        public void OnDisconnected(double nowSeconds)
        {
            Advance(nowSeconds);
            switch (_state)
            {
                case ClientState.Joining:
                    if (_lastJoinReject == SessionErrorCode.BadJoinCode)
                        Fail(ClientFailure.Refused, SessionErrorCode.BadJoinCode, false);
                    else
                        Fail(ClientFailure.HostLost, SessionErrorCode.None, false);
                    return;
                case ClientState.Active:
                    EnterReconnecting(false);
                    return;
                case ClientState.Reconnecting:
                    _connected = false;
                    _awaitingOpen = false;
                    return;
                default:
                    return;
            }
        }

        public void Tick(double nowSeconds)
        {
            Advance(nowSeconds);
            switch (_state)
            {
                case ClientState.Joining:
                    if (_now >= _handshakeDeadline - TimerEpsilon)
                        Fail(ClientFailure.HandshakeTimeout, SessionErrorCode.HandshakeTimeout, true);
                    return;
                case ClientState.Active:
                    TickActive();
                    return;
                case ClientState.Reconnecting:
                    TickReconnecting();
                    return;
                default:
                    return;
            }
        }

        // ---- timers ----

        private void TickActive()
        {
            if (_now - _lastRx >= SessionProtocol.StaleAfterSilenceSeconds - TimerEpsilon)
            {
                EnterReconnecting(true);
                return;
            }
            if (_now - _lastHeartbeat >= SessionProtocol.ClientHeartbeatSeconds - TimerEpsilon)
            {
                _lastHeartbeat = _now;
                SendFrame(SessionProtocol.TypeHeartbeat, _nextSeq++, SessionId, "{}", false);
            }
        }

        private void TickReconnecting()
        {
            double[] backoff = SessionProtocol.ReconnectBackoffSeconds;
            if (_attemptsMade < backoff.Length)
            {
                if (_now >= AttemptTime(_attemptsMade) - TimerEpsilon)
                {
                    _attemptsMade++;
                    RequestConnection();
                }
                return;
            }
            double failAt = AttemptTime(backoff.Length - 1) + backoff[backoff.Length - 1];
            if (_now >= failAt - TimerEpsilon)
                Fail(ClientFailure.HostLost, SessionErrorCode.None, true);
        }

        private double AttemptTime(int index)
        {
            double t = _reconnectStart;
            for (int i = 0; i <= index; i++)
                t += SessionProtocol.ReconnectBackoffSeconds[i];
            return t;
        }

        // ---- frame handling ----

        private void ProcessFrame(SessionFrame frame)
        {
            if (frame.IsFault)
            {
                Fail(ClientFailure.ProtocolError, frame.Fault, true);
                return;
            }
            SessionEnvelopeResult decoded = SessionEnvelope.Decode(frame.Line);
            if (!decoded.Ok)
            {
                Fail(ClientFailure.ProtocolError, decoded.Error, true);
                return;
            }
            SessionEnvelope env = decoded.Envelope;
            _lastRx = _now;

            if (_state == ClientState.Active)
            {
                if (env.Sid != SessionId)
                    return;
                if (env.Seq <= _lastHostSeq)
                    return;
                _lastHostSeq = env.Seq;
            }

            switch (env.T)
            {
                case SessionProtocol.TypeWelcome:
                    HandleWelcome(env);
                    return;
                case SessionProtocol.TypeReject:
                    HandleReject(env);
                    return;
                case SessionProtocol.TypeWorld:
                    HandleWorld(env);
                    return;
                case SessionProtocol.TypeIntentResult:
                    HandleIntentResult(env);
                    return;
                default:
                    return;
            }
        }

        private void HandleWelcome(SessionEnvelope env)
        {
            if (_state != ClientState.Joining && _state != ClientState.Reconnecting)
                return;
            JsonValue body = env.Body;
            JsonValue peer = body.Get("peerId"), token = body.Get("token"), grace = body.Get("graceSeconds");
            byte[] tokenBytes;
            if (peer == null || peer.Kind != JsonKind.String || peer.Text.Length == 0
                || token == null || token.Kind != JsonKind.String
                || !SessionIds.TryParseHex(token.Text, SessionProtocol.TokenBytes, out tokenBytes))
            {
                Fail(ClientFailure.ProtocolError, SessionErrorCode.ProtocolViolation, true);
                return;
            }
            WorldSnapshotDecodeResult world = WorldSnapshot.Decode(body.Get("world"), SessionProtocol.WorldSchemaVersion);
            if (!world.Ok)
            {
                Fail(ClientFailure.ProtocolError, world.Error, true);
                return;
            }

            bool resume = _state == ClientState.Reconnecting;
            if (resume && env.Sid != SessionId)
            {
                Fail(ClientFailure.SessionLost, SessionErrorCode.SessionUnknown, true);
                return;
            }
            if (resume && peer.Text != PeerId)
            {
                Fail(ClientFailure.ProtocolError, SessionErrorCode.ProtocolViolation, true);
                return;
            }

            PeerId = peer.Text;
            SessionId = env.Sid;
            Token = token.Text;
            GraceSeconds = grace != null && grace.Kind == JsonKind.Number && Physics.IsFinite(grace.Number) && grace.Number > 0
                ? grace.Number
                : SessionProtocol.GraceSeconds;
            _state = ClientState.Active;
            _lastHostSeq = env.Seq;
            _lastRx = _now;
            _lastHeartbeat = _now;
            ApplyWorld(world.World);
            _events.Add(new ClientEvent
            {
                Kind = resume ? ClientEventKind.Resumed : ClientEventKind.Joined,
                PeerId = PeerId,
                SnapshotSeq = _lastSnapshotSeq
            });
        }

        private void HandleWorld(SessionEnvelope env)
        {
            if (_state != ClientState.Active)
                return;
            WorldSnapshotDecodeResult world = WorldSnapshot.Decode(env.Body, SessionProtocol.WorldSchemaVersion);
            if (!world.Ok)
            {
                Fail(ClientFailure.ProtocolError, world.Error, true);
                return;
            }
            ApplyWorld(world.World);
        }

        // KTD14: only a snapshot newer than the last applied one reaches the Simulation.
        private void ApplyWorld(WorldSnapshot world)
        {
            if (world.SnapshotSeq <= _lastSnapshotSeq)
                return;
            world.Restore(_sim);
            _lastSnapshotSeq = world.SnapshotSeq;
            _crew = world.Crew;
            _events.Add(new ClientEvent { Kind = ClientEventKind.WorldApplied, SnapshotSeq = world.SnapshotSeq });
        }

        private void HandleReject(SessionEnvelope env)
        {
            SessionErrorCode code = SessionErrorCode.ProtocolViolation;
            long refSeq = 0;
            long raw;
            if (SessionJson.TryWholeNumber(env.Body.Get("code"), out raw) && raw >= 0 && raw <= int.MaxValue
                && Enum.IsDefined(typeof(SessionErrorCode), (int)raw))
                code = (SessionErrorCode)(int)raw;
            // ConnectionLost is client-local (KTD18): a reject carrying it from the wire is dropped whole.
            if (code == SessionErrorCode.ConnectionLost)
                return;
            if (SessionJson.TryWholeNumber(env.Body.Get("seq"), out raw))
                refSeq = raw;

            switch (_state)
            {
                case ClientState.Joining:
                    if (code == SessionErrorCode.BadJoinCode)
                    {
                        _lastJoinReject = code;
                        _events.Add(new ClientEvent { Kind = ClientEventKind.Refused, Code = code, Seq = refSeq });
                    }
                    else
                    {
                        Fail(ClientFailure.Refused, code, true);
                    }
                    return;
                case ClientState.Reconnecting:
                    if (code == SessionErrorCode.TokenExpired || code == SessionErrorCode.SessionUnknown || code == SessionErrorCode.TokenInvalid)
                    {
                        Fail(ClientFailure.SessionLost, code, true);
                    }
                    else if (code == SessionErrorCode.Superseded)
                    {
                        Fail(ClientFailure.Superseded, code, true);
                    }
                    else
                    {
                        _events.Add(new ClientEvent { Kind = ClientEventKind.Refused, Code = code, Seq = refSeq });
                        _outbox.Add(new ClientOutput { Kind = ClientOutputKind.Close });
                        _connected = false;
                        _awaitingOpen = false;
                    }
                    return;
                case ClientState.Active:
                    if (code == SessionErrorCode.Superseded)
                        Fail(ClientFailure.Superseded, code, true);
                    else
                        _events.Add(new ClientEvent { Kind = ClientEventKind.Refused, Code = code, Seq = refSeq });
                    return;
                default:
                    return;
            }
        }

        private void HandleIntentResult(SessionEnvelope env)
        {
            if (_state != ClientState.Active)
                return;
            long seq, rawCode;
            if (!SessionJson.TryWholeNumber(env.Body.Get("seq"), out seq) || !SessionJson.TryWholeNumber(env.Body.Get("code"), out rawCode)
                || rawCode < 0 || rawCode > int.MaxValue || !Enum.IsDefined(typeof(SessionErrorCode), (int)rawCode)
                || rawCode == (long)SessionErrorCode.ConnectionLost)
                return;
            // Only the first result for a sequence number this client sent counts; a later Duplicate finds it not pending.
            if (!_pendingIntents.Remove(seq))
                return;
            RecordIntentResult(seq, (SessionErrorCode)(int)rawCode);
        }

        // ---- helpers ----

        // KTD18: a lost connection can no longer deliver host results, so every pending intent gets one ConnectionLost
        // result (ascending sequence order) before the Reconnecting or Failed event. Outcome unknown, not refused.
        private void ResolvePendingIntents()
        {
            if (_pendingIntents.Count == 0)
                return;
            var seqs = new List<long>(_pendingIntents);
            seqs.Sort();
            _pendingIntents.Clear();
            for (int i = 0; i < seqs.Count; i++)
                RecordIntentResult(seqs[i], SessionErrorCode.ConnectionLost);
        }

        // Stores the first result for a sequence number and raises its IntentResult event.
        private void RecordIntentResult(long seq, SessionErrorCode code)
        {
            if (!_results.ContainsKey(seq))
                _results[seq] = code;
            _events.Add(new ClientEvent
            {
                Kind = ClientEventKind.IntentResult,
                Seq = seq,
                Code = code,
                Accepted = code == SessionErrorCode.None
            });
        }

        private void EnterReconnecting(bool closeConnection)
        {
            ResolvePendingIntents();
            _state = ClientState.Reconnecting;
            _connected = false;
            _awaitingOpen = false;
            _attemptsMade = 0;
            _reconnectStart = _now;
            if (closeConnection)
                _outbox.Add(new ClientOutput { Kind = ClientOutputKind.Close });
            _events.Add(new ClientEvent { Kind = ClientEventKind.Reconnecting, PeerId = PeerId });
        }

        private void RequestConnection()
        {
            _connected = false;
            _awaitingOpen = true;
            _outbox.Add(new ClientOutput { Kind = ClientOutputKind.Connect });
        }

        private void Fail(ClientFailure reason, SessionErrorCode code, bool closeConnection)
        {
            ResolvePendingIntents();
            _state = ClientState.Failed;
            _connected = false;
            _awaitingOpen = false;
            FailureReason = reason;
            FailureCode = code;
            if (closeConnection)
                _outbox.Add(new ClientOutput { Kind = ClientOutputKind.Close });
            _events.Add(new ClientEvent { Kind = ClientEventKind.Failed, Failure = reason, Code = code });
        }

        private void ResetToIdle()
        {
            _state = ClientState.Idle;
            _connected = false;
            _awaitingOpen = false;
            _reader = null;
            PeerId = null;
            SessionId = null;
            Token = null;
            GraceSeconds = 0;
            FailureReason = ClientFailure.None;
            FailureCode = SessionErrorCode.None;
            _pendingIntents.Clear();
            _results.Clear();
        }

        private void SendHello(bool resume)
        {
            var sb = new StringBuilder(128);
            sb.Append("{\"schema\":");
            SessionJson.AppendInteger(sb, SessionProtocol.WorldSchemaVersion);
            if (resume)
            {
                sb.Append(",\"token\":");
                SessionJson.AppendString(sb, Token);
            }
            else
            {
                sb.Append(",\"joinCode\":");
                SessionJson.AppendString(sb, _joinCode);
                sb.Append(",\"name\":");
                SessionJson.AppendString(sb, _displayName);
            }
            sb.Append('}');
            SendFrame(SessionProtocol.TypeHello, _nextSeq++, resume ? SessionId : null, sb.ToString(), false);
        }

        private void SendFrame(string type, long seq, string sid, string bodyJson, bool closeAfter)
        {
            _outbox.Add(new ClientOutput
            {
                Kind = ClientOutputKind.Send,
                Frame = SessionEnvelope.Encode(type, seq, sid, bodyJson),
                CloseAfter = closeAfter
            });
        }

        private long RefuseLocally(SessionErrorCode code)
        {
            _events.Add(new ClientEvent { Kind = ClientEventKind.IntentResult, Seq = 0, Code = code, Accepted = false });
            return 0;
        }

        private void PruneIntentMemory()
        {
            if (_results.Count <= IntentMemory && _pendingIntents.Count <= IntentMemory)
                return;
            long floor = _nextSeq - IntentMemory;
            var stale = new List<long>();
            foreach (long k in _results.Keys)
                if (k < floor) stale.Add(k);
            for (int i = 0; i < stale.Count; i++)
                _results.Remove(stale[i]);
            _pendingIntents.RemoveWhere(k => k < floor);
        }

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
    }
}
