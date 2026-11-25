# Architecture

## Layering

```
Presentation   ReactorSim.Unity (later: 3D plant or 2D control-room desk)
     |
Netcode        host-authoritative state sync + input RPCs (Mirror or Fusion)
     |
Simulation     ReactorSim.Core — pure C#, deterministic fixed-tick, no Unity deps
```

The sim core is the product. Everything else is a view and a transport.

## Simulation engine

`Simulation.Tick()` — fixed 5 s sim-time per tick, 12 ticks/game-minute
(chosen from the integrator and UI needs, see `Simulation.TicksPerGameMinute`; real-time pacing knobs live on
`Simulation` for later wall-clock sync).

Per tick (`Plant.Step`):
1. Rod banks step toward targets (1%/s drive, 50%/s scram)
2. Chemistry (boron dose/dilute, pH drift, ion exchange)
3. Reactivity ledger: rods + boron + Doppler + moderator + Xe + I
4. Point kinetics (6 delayed groups, analytic substep)
5. Poisons (I-135 -> Xe-135 ODEs, time-warped to game scale)
6. Decay heat (3 lumped groups)
7. Thermal-hydraulics (fuel -> primary -> SG -> secondary; pressurizer)

## Multiplayer

Host-authoritative co-op on a trusted LAN, specified in
[docs/design/session-protocol.md](design/session-protocol.md):

- The host runs `Simulation` at the tick cadence and owns truth.
- `ReactorSim.Core/Session/` holds the transport-agnostic contract:
  `SessionHost` and `SessionClient` are pure state machines with injected
  time and entropy. They hold no sockets or threads, so they test without a
  network.
- A client joins with a versioned `hello` and receives a `welcome` carrying
  the full world snapshot (plant plus crew) before anything else. That
  snapshot is the late-join state.
- Clients send intents. The host admits them in a fixed order, stamps the
  peer as issuer, and answers each with a typed result.
- The host sends a world snapshot every 0.25 s. The client applies only
  newer snapshots and does not run its own simulation between them.
- A dropped client resumes with its token within a 30 s grace window.

The Unity transport (`CoopSessionManager`) still uses the older line protocol
until the engine slice wires it to the contract. Internet transport is a
later decision.

## Crew split (2-player design)

- Operator A (reactor): rods, boron/chemistry, core monitoring
- Operator B (balance of plant): pumps, SG, turbine/steam demand, cooling

Shared alarm space; either may SCRAM. This is where the reference co-op is
thin and where we can genuinely be better.

## Hard rules

- `ReactorSim.Core` never references Unity/netcode types.
- All plant state that must sync or save lives on `Plant` and below — serializable.
  Crew (player) state is session state: it lives in the versioned world snapshot
  (`ReactorSim.Core/Session/WorldSnapshot.cs`) that wraps the plant snapshot.
- Constants centralized in `Physics.cs`; tuning tables become data files, not
  scattered literals.
- Every subsystem is independently testable via the console runner.
