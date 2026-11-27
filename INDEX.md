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
| [design/control-room/](design/control-room) | Control-room data: `inventory.csv` (one row per control, readout, alarm and CRT page), `kits.json` (instrument kit catalog), `layout.json` (panel arc), generated `rollup.md`. See its [README](design/control-room/README.md). |
| [assets/](assets) | `manifest.csv`: sidecar inventory of the reactor assets (ids, bindings, LOD, triangle estimates, license), checked by `Blender/scripts/check_reactor_assets.py` and `AssetManifestCheck`. |
| [scripts/](scripts) | Repo automation, listed below. |
| [tools/](tools) | Developer tools outside the build: [mcp-bridge-config.json](tools/mcp-bridge-config.json), [voice/](tools/voice) (assistant voice-line renderer, see its [README](tools/voice/README.md)). |
| [.github/workflows/](.github/workflows) | `ci.yml`: runs `scripts/ci.sh` on every push to `master` and every PR (Unity checks stay local). |

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

## Scripts

| Script | Purpose |
|---|---|
| [scripts/ci.sh](scripts/ci.sh) | The verification contract: clean-room scan, Core DLL freshness, 0-warning build, tests, `--scenario all`, `--verify`, optional Unity batchmode checks |
| [scripts/clean_room_check.sh](scripts/clean_room_check.sh) | Fails on a banned term or per-user path in any tracked file; `--self-test` runs negative controls |
| [scripts/run.sh](scripts/run.sh) | Builds and runs the console harness, arguments passed through |
| [scripts/sync-core-to-unity.sh](scripts/sync-core-to-unity.sh) | Builds the Core and copies the DLL into Unity Plugins, recording `core-source.sha256` |
| [scripts/core-source-hash.sh](scripts/core-source-hash.sh) | sha256 over the Core sources; shared by the sync script and `ci.sh` |
| [scripts/scene-signature.sh](scripts/scene-signature.sh) | Normalised form of a Unity scene for the scene drift guard |
| [scripts/capture-playtest.sh](scripts/capture-playtest.sh) | Runs the player's in-game capture mode on the Windows box and pulls PNGs and `captures.json` |
| [scripts/check-captures.sh](scripts/check-captures.sh) | Grades a capture run against fixed thresholds (glyph height, CRT contrast, budgets, rack glow) |
| [scripts/test-check-captures.sh](scripts/test-check-captures.sh) | Self-test for the capture grader (a deliberately failing run must fail) |
| [scripts/fetch-renders.sh](scripts/fetch-renders.sh) | Pulls headless room renders back from the Windows box |
| [scripts/fetch-playtest-logs.sh](scripts/fetch-playtest-logs.sh) | Pulls a Windows playtest's logs back for review |
| [scripts/inventory-rollup.py](scripts/inventory-rollup.py) | Regenerates `design/control-room/rollup.md` and the runtime inventory from `inventory.csv` |

Blender generators and helpers live in [Blender/scripts/](Blender/scripts), each with a header comment: `make_*.py`
build one .blend, `export_unity.py` and `sync-blender-to-unity.{sh,ps1}` export and copy the FBX files, `bake_trim_sheets.py`
bakes textures, `check_reactor_assets.py` verifies the reactor assets, `render_room.py` and `render_turntable.py` are
Workbench review aids. Unity batchmode entry points (builders, checks, renders) are in
[ReactorSim.Unity/Assets/Editor/](ReactorSim.Unity/Assets/Editor) and listed in [docs/TESTING.md](docs/TESTING.md).

## Plans

[docs/plans/](docs/plans) holds the execution plans; each carries a `status:` in its frontmatter (`completed`,
`in-progress` or `planned`). The CE phase plans (1 to 8) are indexed with their dependencies in [ROADMAP.md](ROADMAP.md).

| Plan | Status |
|---|---|
| [Playtest 2 controls, readability, hall](docs/plans/2026-11-09-2010-feat-playtest2-controls-readability-hall-plan.md) | completed |
| [Visual polish pass](docs/plans/2026-11-10-feat-visual-polish-pass-plan.md) | completed |
| [Hall lighting and window](docs/plans/2026-11-10-0100-feat-hall-lighting-window-plan.md) | completed |
| [Final visual pass](docs/plans/2026-11-10-feat-final-visual-pass-plan.md) | completed |
| [Window framing and README media](docs/plans/2026-11-10-feat-window-framing-readme-plan.md) | completed |
| [Blender reactor assets brief](docs/plans/2026-11-11-blender-reactor-assets-brief.md) | completed (original brief) |
| [Living reactor core assets](docs/plans/2026-11-11-1950-feat-living-reactor-core-assets-plan.md) | completed (PR #11) |
| [Control instruments upgrade](docs/plans/2026-11-12-0000-feat-control-instruments-upgrade-plan.md) | in-progress |
| [Whole-plant status wall](docs/plans/2026-11-13-feat-whole-plant-status-wall-plan.md) | in-progress |
| [Live plant counterparts](docs/plans/2026-11-14-feat-live-plant-counterparts-plan.md) | in-progress |
| CE phase 1 to 8 | phase 1 in-progress, 2 to 8 planned |

## Docs

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — layering, tick structure, host-authoritative co-op
- [docs/TESTING.md](docs/TESTING.md) — determinism contract, scenario and check inventory, CI
- [ROADMAP.md](ROADMAP.md) — where the project is headed: next, later and ongoing work
- [docs/STARTUP.md](docs/STARTUP.md) — cold shutdown to power on the grid: the 13-step checklist, why each step comes where it does, measured game minutes, pace guidance
- [docs/PLAYTEST.md](docs/PLAYTEST.md) — launching the Windows build, the playtest checklist, controls, and fetching session logs
- [docs/testing/simulation-verification.md](docs/testing/simulation-verification.md) — `--verify` design, fixtures, known defects
- [docs/ASSISTANT.md](docs/ASSISTANT.md) — advisor, procedure and assist-tier design
- [docs/design/](docs/design) — plant and asset design: [main-control-room.md](docs/design/main-control-room.md), [plant-and-assets.md](docs/design/plant-and-assets.md), [reactor-core-assets.md](docs/design/reactor-core-assets.md), [plant-counterparts-inventory.md](docs/design/plant-counterparts-inventory.md) (source audit of live bindings), [session-protocol.md](docs/design/session-protocol.md) (LAN co-op session contract)
- [docs/media/](docs/media) — README screenshots (control room standing and top-down, hall window standing and seated)
- [README.md](README.md) is the project front page; [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) lists bundled third-party material
- [docs/development/observability.md](docs/development/observability.md) — logging and telemetry
- [docs/TOOLING.md](docs/TOOLING.md) — scripts, prerequisites and open-source physics references

## Entry points

- `ReactorSim.Core/Sim.cs` → `Simulation.Tick()` (5 s sim time per tick)
- `ReactorSim.Runner/Program.cs` → `Program.Main`
- `ReactorSim.Unity/Assets/Scripts/PlantBridge.cs` → Unity-side sim driver
