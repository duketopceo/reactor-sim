# Contributor and Agent Guide

Reactor Sim is an original PWR plant simulator. Everything here is original work; do not add code, assets or data
taken from other simulators (`scripts/clean_room_check.sh` enforces a denylist in CI).

## Build and verify

```bash
bash scripts/ci.sh                       # the contract: clean-room scan, 0-warning build, tests, --scenario all, --verify
UNITY_EXE=/path/to/Unity bash scripts/ci.sh   # also runs the Unity batchmode checks
dotnet run --project ReactorSim.Runner   # scripted transient with telemetry
```

After any `ReactorSim.Core` change run `bash scripts/sync-core-to-unity.sh`, or Unity keeps running the old DLL in
`Assets/Plugins/ReactorSim/` (and `ci.sh` fails its freshness check). Repo map: [INDEX.md](INDEX.md); testing
rules: [docs/TESTING.md](docs/TESTING.md).

`ReactorSim.Core` targets netstandard2.1 (Unity-compatible); keep it free of engine and netcode dependencies.
`ReactorSim.Runner` is the test harness: add scenarios there to exercise physics changes and watch the telemetry
(power reaches equilibrium, feedback drives reactivity to zero, decay heat after SCRAM, xenon and iodine build
during power operation).

## Conventions

- English identifiers.
- Physics constants live in `Physics.cs`. A constant is either a cited physical value (name the public source in
  a comment) or labelled as an original gameplay design choice with a one-line rationale. Gameplay tuning that is
  not physics belongs in data files.
- Serializable state lives on `Plant` and below: it is the save and network boundary.
- Golden files are only regenerated through the documented path in [docs/TESTING.md](docs/TESTING.md).
- See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/ASSISTANT.md](docs/ASSISTANT.md).
