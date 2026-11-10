# ReactorSim — Repo Index

Original PWR plant simulator: deterministic C# core, Unity 6 front-end (3D
control room), Blender-generated assets, co-op first. Read [AGENTS.md](AGENTS.md)
and [docs/TESTING.md](docs/TESTING.md) first.

## Projects

| Path | What |
|---|---|
| [ReactorSim.Core/](ReactorSim.Core) | Sim library, netstandard2.1, no engine/netcode deps. `Sim.cs` tick loop + intent queue, `Plant.cs` state root (serializable = save/sync boundary), `Intents.cs` typed control pipeline, `Snapshot.cs` JSON save/restore, `Core.cs` point kinetics + Xe/I poisons + decay heat, `Thermal.cs` fuel/primary/SG/pressurizer thermal-hydraulics, `Physics.cs` every constant + IAPWS-IF97 saturation, `BalanceOfPlant/` turbine-generator, grid, condenser, hotwell/feed, cooling tower and latched trips (auto-load governor by default). Host plumbing: `Api/HttpApiServer.cs` (JSON API), `Assistant/AssistantEngine.cs` (advisories, procedures, tiers, missions), `Diagnostics/` (JSONL logger, CSV recorder, golden compare). |
| [ReactorSim.Runner/](ReactorSim.Runner) | Console harness, net8.0: U1–U10 scenario suite (`BopScenarios.cs` = U10, `StartupScenarios.cs` = `startup-grid`), `--verify` checks (`Verification.cs`), intent scripts, golden replay, `--serve`. |
| [ReactorSim.Core.Tests/](ReactorSim.Core.Tests) | xUnit, net8.0: intent JSON for every kind + boundary rejection, BOP sync/trip semantics, snapshot round trip + bit-identical resume, IF97 points, golden-compare columns. `dotnet test ReactorSim.Core.Tests`. |
| [ReactorSim.McpBridge/](ReactorSim.McpBridge) | stdio MCP bridge, net8.0, proxying the HTTP API for agents. Config: [tools/mcp-bridge-config.json](tools/mcp-bridge-config.json). |
| [ReactorSim.Unity/](ReactorSim.Unity) | Unity 6000.6.7f1. `PlantBridge` drives the sim; `View3D/` binds the Blender control room, CRT screens, plant equipment and audio; `Missions/` mission HUD; `Net/` co-op (TCP now, Photon Fusion stub). Core DLL synced into `Assets/Plugins/ReactorSim/`. |
| [Blender/](Blender) | Procedural .blend generators, FBX export, trim-sheet bake, sync into Unity. See [Blender/README.md](Blender/README.md). |

## Commands

| Task | Command |
|---|---|
| Full verification | `bash scripts/ci.sh` (add `UNITY_EXE=...` for the Unity checks) |
| Build | `dotnet build` |
| Scenario suite | `dotnet run --project ReactorSim.Runner -- --scenario all` (includes `startup-grid`; list in [docs/TESTING.md](docs/TESTING.md)) |
| Verification checks | `dotnet run --project ReactorSim.Runner -- --verify` (`--list` to enumerate) |
| Baseline transient | `bash scripts/run.sh` |
| Live HTTP API | `dotnet run --project ReactorSim.Runner -- --serve 7770` |
| Core → Unity | `bash scripts/sync-core-to-unity.sh` (also records `core-source.sha256`; `ci.sh` fails if stale) |
| Windows build | `"<Unity.exe>" -batchmode -nographics -projectPath ReactorSim.Unity -executeMethod BuildScript.BuildWindows -buildTarget StandaloneWindows64 -quit -logFile out/unity-build.log` → `out/build/` |
| Blender bake + sync | see [Blender/README.md](Blender/README.md) |

Runner targets net8.0; on machines with only newer runtimes set
`DOTNET_ROLL_FORWARD=LatestMajor` (`ci.sh` does).

## Docs

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — layering, tick structure, host-authoritative co-op
- [docs/TESTING.md](docs/TESTING.md) — determinism contract, scenario and check inventory, CI
- [docs/STARTUP.md](docs/STARTUP.md) — cold shutdown to power on the grid: the 13-step checklist, why each step comes where it does, measured game minutes, pace guidance
- [docs/PLAYTEST.md](docs/PLAYTEST.md) — launching the Windows build, the playtest checklist, controls, and fetching session logs
- [docs/testing/simulation-verification.md](docs/testing/simulation-verification.md) — `--verify` design, fixtures, known defects
- [docs/ASSISTANT.md](docs/ASSISTANT.md) — advisor, procedure and assist-tier design
- [docs/design/](docs/design) — plant and asset design
- [docs/development/observability.md](docs/development/observability.md) — logging and telemetry
- [docs/TOOLING.md](docs/TOOLING.md) — scripts, prerequisites and open-source physics references

## Entry points

- `ReactorSim.Core/Sim.cs` → `Simulation.Tick()` (5 s sim time per tick)
- `ReactorSim.Runner/Program.cs` → `Program.Main`
- `ReactorSim.Unity/Assets/Scripts/PlantBridge.cs` → Unity-side sim driver
