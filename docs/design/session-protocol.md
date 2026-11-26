# Shared-crew session protocol

This document publishes the LAN session contract for two operators sharing one plant (Phase 3). The decisions behind it live in the [Phase 3 plan](../plans/2026-11-14-ce-phase-3-shared-crew-plan.md); this page states what the code does and cites the plan's KTDs instead of repeating their rationale.

The contract is implemented as pure state machines in `ReactorSim.Core/Session/`:

- `SessionHost` and `SessionClient` hold the session state.
- `SessionFrameReader`, `SessionJson` and `SessionEnvelope` handle framing and decoding.
- `WorldSnapshot` carries plant and crew state.

The Unity transport (a later slice) owns sockets and feeds these machines. The contract has no socket, thread, clock or random source of its own (KTD1, KTD2).

## Trust boundary

- **LAN only.** The session is for a trusted local network. It is not safe on the internet, and no internet play is claimed (R10).
- **The join code is not authentication.** The 6-digit code stops a player joining the wrong session by mistake.
- **Tokens travel in cleartext.** Anyone on the same network segment can read a reconnect token and take that operator's seat.
- **The host is trusted.** A malicious host is out of scope: the client decodes envelopes strictly but restores the plant body through the tolerant `PlantSnapshot.FromJson` (KTD14).
- **Untrusted client input is bounded.**
  - Every client byte passes the frame reader and the strict JSON parser before it can affect the plant.
  - Every intent passes the admission order below.
  - Bad input never crashes the host and never changes the plant (R4).

## Transport duties

The transport must:

1. Feed raw received bytes to `OnBytes`. Never pre-read a whole line: the frame reader enforces the size limit while bytes arrive, so an unbounded line is never buffered (KTD4).
2. Report connection open and close, and call `Tick` regularly with a monotonic seconds value. Feed all pending bytes before calling `Tick`, so a host stall does not mark a live peer stale.
3. Send every outbox frame followed by `\n`, and close the connection after any entry marked close-after. An entry with no frame means close only.
4. Supply a cryptographic random source through `ISessionEntropy`. Tests use a fixed source in the test project; production must never use it.
5. Keep connection ids unique for the host lifetime. `OnDisconnected` drops undrained output for that id, but a reused id could still receive output queued for its predecessor.
6. Run the machines on one thread. They are not thread-safe.

## Frames and envelope

Every frame is one UTF-8 JSON object on one line:

```json
{"v":1,"t":"intent","seq":7,"sid":"<32 hex>","body":{}}
```

| Field | Rule |
|---|---|
| `v` | Protocol version. It must equal `1`; otherwise `UnsupportedProtocol` (KTD5). |
| `t` | Message type, from the table below. An unknown type is a `ProtocolViolation`. |
| `seq` | The sender's own frame number: an integer from 1 up to 2^53-1, strictly increasing (KTD9). |
| `sid` | Session ID. Absent only on `hello`; every host frame carries it. |
| `body` | An object. If missing, it is treated as empty. |

The strict parser rejects duplicate keys, nesting deeper than 8, trailing data, bad escapes and non-finite numbers. Unknown fields are ignored. Numbers are written with invariant culture and `G17` round-trip precision.

## Message types

| Type | Direction | Body |
|---|---|---|
| `hello` | client to host | `schema`, `joinCode`, `name`; optional `token` to resume (then the envelope `sid` names the old session) |
| `welcome` | host to client | `hostId`, `sid`, `peerId`, `token`, `graceSeconds`, `world`; always the first frame on a joined or resumed connection |
| `reject` | host to client | `code` (integer), `name`, `seq` of the refused frame (0 when none) |
| `world` | host to client | world snapshot: `schema`, `sessionTick`, `snapshotSeq`, `clockPaused`, `clockScale`, `plant`, `crew` |
| `intent` | client to host | `kind` (an `IntentKind` name), optional `target` (at most 64 characters), `value`, optional `atMinute`; any `issuer` is ignored |
| `intent_result` | host to client | `seq`, `code` (0 means queued), `accepted` |
| `crew_update` | client to host | `loc`, `x`, `y`, `z`, `yaw`, `pose` (`standing` or `seated`), optional `name` |
| `heartbeat` | client to host | empty; liveness only |
| `goodbye` | client to host | empty; frees the slot at once |

"Accepted" means the host confirmed the intent queued, not applied. An intent queued while the clock is halted waits. A rejection at apply time shows in a later `world`, not as a result (KTD10).

`accepted` false is a refusal for every code except `ConnectionLost` (20). `ConnectionLost` means the connection dropped with the intent in flight and the outcome is unknown: the host may have queued it. The caller reads the next `world` for the outcome. A host refusal reason for that intent is lost. The client never resends an intent on its own; a caller that wants the action again sends a new intent. The client ignores code 20 when it arrives from the wire (KTD18).

## Host handling

| Message | Handshaking connection | Active peer |
|---|---|---|
| Valid `hello`, new peer | `welcome` | `ProtocolViolation`, close |
| `hello` with the token of a stale peer | `welcome`, resume the peer | `ProtocolViolation`, close |
| `hello` with the token of an active peer | `welcome`; the old connection closes with `Superseded` | `ProtocolViolation`, close |
| `hello` with a wrong join code | `reject BadJoinCode`, stays open; the fifth closes | `ProtocolViolation`, close |
| `hello` failing another check | `reject` with that code, close | `ProtocolViolation`, close |
| A new connection opens while 2 are handshaking | the oldest gets `reject TooManyConnections`, close | none; active peers are never evicted |
| `intent` | `reject NotJoined` | admission, below |
| `crew_update` | `reject NotJoined` | validation; a refusal is a non-fatal `reject` |
| `heartbeat` | dropped | liveness only |
| `goodbye` | close | close, free the slot |
| Any frame with a protocol version other than `1` | `UnsupportedProtocol`, close | `UnsupportedProtocol`, close |
| Unknown type or bad JSON | `ProtocolViolation`, close | `ProtocolViolation`, close |
| Over-limit frame | `FrameTooLarge`, close | `FrameTooLarge`, close |
| More than 100 frames in one second | `RateLimited`, close | `RateLimited`, close |

A new connection first expires handshakes past the 5 s timeout. If two connections are still handshaking, it then evicts the oldest with `TooManyConnections`. Trade-off: a LAN peer that opens more than 2 connections per 5 s can evict a newcomer before its `hello`. This sits inside the trusted-LAN boundary (R10). An evicted joining client ends in failed with `TooManyConnections` and joins again. A reconnecting client retries on its backoff.

A host-initiated close of an active peer frees its slot, so its token can no longer resume. Only a transport disconnect or 5 s of silence makes a peer stale.

## Intent admission

The host checks each intent in this order and answers every checked intent with an `intent_result` (KTD10):

1. The peer is active; otherwise `NotJoined`.
2. The sequence number is fresh; otherwise `Duplicate`.
3. The body decodes to a defined `IntentKind` with bounded strings; otherwise `InvalidIntent`.
4. The intent rate limiter has a token; otherwise `RateLimited`.
5. The intent is player-class; otherwise `NotPermitted`.
6. A clock intent passes only when the host sets `AllowRemoteClock` (off by default); otherwise `NotPermitted`.
7. The host overwrites the issuer with the peer ID.
8. `Simulation.EnqueueIntent` runs; a non-finite value maps to `NonFiniteValue`, and a dev-only intent maps to `NotPermitted`.

Every other player-class intent is open to the remote operator in this slice, including mission, assist tier, operating mode and SCRAM reset. A later roles slice narrows this.

## Limits and timers

All values are original design choices for a two-player LAN game (KTD4, KTD6, KTD8).

| Limit | Value |
|---|---|
| Control frame | 4096 bytes, newline not counted |
| State frame (`welcome`, `world`) | 262144 bytes |
| Worst-case world snapshot budget | 65536 bytes |
| JSON nesting depth | 8 |
| Intent target and other intent strings | 64 characters |
| Display name | 1-24 printable characters |
| Location ID | 1-64 characters of `[a-z0-9._-]` |
| Remote peers | 1 |
| Concurrent handshaking connections | 2; a new one evicts the oldest (`TooManyConnections`) |
| Bad join codes per connection | 5 |
| Intent rate | 10 per second, burst 20 |
| Crew update rate | 20 per second, burst 40 |
| Frames per connection | 100 per second |
| Unsent `world` frames per connection | 1 (a newer one replaces it) |
| Pending intents per client | 1024; a further intent is refused locally with `RateLimited` |

| Timer | Value |
|---|---|
| Handshake timeout | 5 s |
| Client heartbeat | every 1 s |
| Silence before a peer turns stale | 5 s |
| Grace window, from turning stale | 30 s |
| `world` interval | 0.25 s, also while paused |
| Client reconnect backoff | 1, 2, 4, 8, 8 s between attempts, then failed |

## Versions

- The protocol version must match exactly.
- The host refuses a client whose world schema is older than its own, with `UnsupportedSchema`.
- The client refuses a world whose schema is newer than its own.
- Both versions start at 1.
- `PlantSnapshot` has no version field. Older plant fields restore through its tolerant defaults (KTD5, KTD15).

## State ownership

- **Plant state** lives on `Plant` and below and travels as the `plant` member of the world snapshot. The host is the only authority.
- **Crew state** is session state. It holds one entry per remote peer: peer ID, display name, location ID, position, yaw and pose. It lives in the world snapshot, not on `Plant`, and is the sync and save boundary for player state (KTD11).
- **Location IDs** are opaque strings. The host checks them with an injected validator, so the session does not depend on a particular route layout. A client decoding a world snapshot checks only the structural bound (1-64 characters, no control characters).
- **The client is a mirror.** It does not tick its simulation between worlds, so tick-derived displays such as trend history are not reproduced on the client yet (KTD14).
- **Clock state** (pause, scale) and the session tick travel in the world snapshot and are restored on the client.

## Error codes

| Code | Value | Meaning |
|---|---|---|
| `None` | 0 | Accepted or no error |
| `UnsupportedProtocol` | 1 | Protocol version differs |
| `UnsupportedSchema` | 2 | World schema not supported |
| `BadJoinCode` | 3 | Wrong join code |
| `SessionFull` | 4 | The remote slot is taken, including by a stale peer within its grace window |
| `HandshakeTimeout` | 5 | No valid `hello` (host) or `welcome` (client) within 5 s |
| `TokenInvalid` | 6 | Unknown reconnect token |
| `TokenExpired` | 7 | The token's peer left or its grace window ended |
| `SessionUnknown` | 8 | The token or frame names another session |
| `Superseded` | 9 | A newer connection resumed this peer |
| `ProtocolViolation` | 10 | Malformed frame, unknown type or bad sequence |
| `FrameTooLarge` | 11 | Frame over its limit |
| `RateLimited` | 12 | Intent, crew-update or frame rate exceeded |
| `TooManyConnections` | 13 | Too many connections handshaking at once; sent to the oldest handshaking connection when a new one opens |
| `NotJoined` | 14 | Message before the join completed |
| `Duplicate` | 15 | Repeated or lower sequence number |
| `InvalidIntent` | 16 | Intent body is malformed or names an undefined kind |
| `NotPermitted` | 17 | Intent class or clock rule refuses it |
| `NonFiniteValue` | 18 | Intent value is not finite |
| `InvalidCrewState` | 19 | Crew update breaks a crew rule |
| `ConnectionLost` | 20 | Client-local: a connection dropped with the intent in flight; outcome unknown. Never sent by the host. |

## Known limits

- Unity wiring, join UI, avatars, roles and internet transport are not built yet.
- No two-machine playtest has been run against this contract.
- The client does not tick its simulation between worlds, so tick-derived displays such as trends are not reproduced on the client yet.
- A compliant client sends at most about 90 frames in one window: bursts plus refill within the window give 29 intents, 59 crew updates and 1 heartbeat.
- The frame-rate cap uses fixed one-second windows, so a burst that spans a window boundary can briefly carry about twice the cap.
