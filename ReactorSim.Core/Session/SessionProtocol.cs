// ReactorSim.Core - shared-crew session contract: protocol constants and error codes (Phase 3, U1).
// Pure data: no sockets, threads, clocks or entropy live in this namespace.

namespace ReactorSim.Session
{
    // Every numeric limit below is an original design choice for this simulator's LAN co-op session,
    // not a value taken from any external protocol or simulator (see the Phase 3 plan, KTD3-KTD10).
    public static class SessionProtocol
    {
        // Original design choice: exact-match protocol version.
        public const int ProtocolVersion = 1;
        // Original design choice: world snapshot schema version; a client accepts schemas up to its own.
        public const int WorldSchemaVersion = 1;

        // Original design choice: control frames (everything except Welcome and World) stay small.
        public const int ControlFrameMaxBytes = 4096;
        // Original design choice: Welcome and World frames, host to client only.
        public const int StateFrameMaxBytes = 262144;
        // Original design choice: worst-case world snapshot, a quarter of the state frame limit for headroom.
        public const int WorldSnapshotBudgetBytes = 65536;

        // Original design choice: time a connection may stay in Handshaking.
        public const double HandshakeTimeoutSeconds = 5;
        // Original design choice: client heartbeat interval.
        public const double ClientHeartbeatSeconds = 1;
        // Original design choice: silence before a peer turns Stale.
        public const double StaleAfterSilenceSeconds = 5;
        // Original design choice: grace window from the Stale transition before the slot is freed.
        public const double GraceSeconds = 30;
        // Original design choice: client reconnect backoff, then Failed. Treat as read-only.
        public static readonly double[] ReconnectBackoffSeconds = { 1, 2, 4, 8, 8 };
        // Original design choice: host World broadcast interval.
        public const double WorldIntervalSeconds = 0.25;

        // Original design choice: intent token bucket refill rate.
        public const int IntentRatePerSecond = 10;
        // Original design choice: intent token bucket burst size.
        public const int IntentBurst = 20;
        // Original design choice: crew update token bucket refill rate.
        public const int CrewRatePerSecond = 20;
        // Original design choice: crew update token bucket burst size.
        public const int CrewBurst = 40;
        // Original design choice: frames per second of any type before a connection is closed RateLimited.
        public const int MaxFramesPerSecond = 100;
        // Original design choice: simultaneous connections in Handshaking.
        public const int MaxHandshakingConnections = 2;
        // Original design choice: wrong join codes on one connection before it is closed.
        public const int MaxBadJoinCodes = 5;

        // Original design choice: join code length; it prevents joining the wrong session, it is not authentication.
        public const int JoinCodeDigits = 6;
        // Original design choice: reconnect token size in bytes.
        public const int TokenBytes = 16;
        // Original design choice: display name length bound.
        public const int DisplayNameMaxChars = 24;
        // Original design choice: location identifier length bound.
        public const int LocationIdMaxChars = 64;
        // Original design choice: maximum JSON nesting depth, with the root counting as depth 1.
        public const int MaxJsonDepth = 8;
        // Original design choice: intent target string bound; real targets are short names such as "Turbine", so 64 is generous.
        public const int IntentStringMaxChars = 64;

        // JSON numbers are doubles, so wire integers (v, seq) stay within the exactly representable range 2^53 - 1.
        public const long MaxWireInteger = 9007199254740991L;

        // Issuer placeholder on a decoded intent; the host overwrites it with the peer ID (KTD10 step 7).
        public const string UnassignedIssuer = "session-unassigned";

        public const string TypeHello = "hello";
        public const string TypeWelcome = "welcome";
        public const string TypeReject = "reject";
        public const string TypeWorld = "world";
        public const string TypeIntent = "intent";
        public const string TypeIntentResult = "intent_result";
        public const string TypeCrewUpdate = "crew_update";
        public const string TypeHeartbeat = "heartbeat";
        public const string TypeGoodbye = "goodbye";

        public static bool IsKnownType(string t)
        {
            switch (t)
            {
                case TypeHello:
                case TypeWelcome:
                case TypeReject:
                case TypeWorld:
                case TypeIntent:
                case TypeIntentResult:
                case TypeCrewUpdate:
                case TypeHeartbeat:
                case TypeGoodbye:
                    return true;
                default:
                    return false;
            }
        }
    }

    // Integer values are part of the wire contract and are pinned by a test: never renumber, only append.
    public enum SessionErrorCode
    {
        None = 0,
        UnsupportedProtocol = 1,
        UnsupportedSchema = 2,
        BadJoinCode = 3,
        SessionFull = 4,
        HandshakeTimeout = 5,
        TokenInvalid = 6,
        TokenExpired = 7,
        SessionUnknown = 8,
        Superseded = 9,
        ProtocolViolation = 10,
        FrameTooLarge = 11,
        RateLimited = 12,
        TooManyConnections = 13,
        NotJoined = 14,
        Duplicate = 15,
        InvalidIntent = 16,
        NotPermitted = 17,
        NonFiniteValue = 18,
        InvalidCrewState = 19,
        // Client-local: the client raises it when a connection drops with an intent in flight; the host never sends it.
        ConnectionLost = 20
    }
}
