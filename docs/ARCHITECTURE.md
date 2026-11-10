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

Host-authoritative co-op:

- Host runs `Simulation` at the tick cadence; owns truth.
- Clients send intents: `SetRodTarget(bank, x)`, `SetDoseRate(kind, r)`,
  `SetSteamDemand(x)`, `Scram()` — as RPCs.
- Host broadcasts state snapshots (power, temps, pressures, ρ components,
  alarm flags) at ~4 Hz; clients interpolate for gauges.
- Late join = full snapshot.

Transport options, in order of preference for two-player testing:
1. **Mirror + kcp2k** — free, offline-LAN/direct-IP capable, MIT.
2. **FizzySteamworks** transport on Mirror — Steam lobby/P2P once we have an appid.
3. **Photon Fusion** — cloud relay, zero NAT hassle, free tier covers 2 players.

The core is transport-agnostic: `Plant` exposes serializable state and an
intent queue. No netcode types inside `ReactorSim.Core`.

## Crew split (2-player design)

- Operator A (reactor): rods, boron/chemistry, core monitoring
- Operator B (balance of plant): pumps, SG, turbine/steam demand, cooling

Shared alarm space; either may SCRAM. This is where the reference co-op is
thin and where we can genuinely be better.

## Hard rules

- `ReactorSim.Core` never references Unity/netcode types.
- All state that must sync or save lives on `Plant` and below — serializable.
- Constants centralized in `Physics.cs`; tuning tables become data files, not
  scattered literals.
- Every subsystem is independently testable via the console runner.
