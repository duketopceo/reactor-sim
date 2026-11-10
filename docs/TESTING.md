# Testing & Determinism Contract

This file is normative. Multiplayer replay, save/load, golden regression, and
the agent surface all depend on the rules below holding at all times.

## Determinism rules (ReactorSim.Core)

`ReactorSim.Core` is a pure function of (initial state, intent sequence). It
must never read:

- **Wall-clock or timers** — no `DateTime`, `DateTimeOffset`, `Stopwatch`,
  `Environment.TickCount`, `Task.Delay`, or thread timing. Sim time advances
  only through `Simulation.Tick()` → `Plant.Step(TickDt)`.
- **Randomness** — no `System.Random`, `Guid.NewGuid`, or any RNG. If
  stochastic behavior is ever needed, a seeded PRNG lives on `Plant` state and
  serializes with it.
- **Ambient OS state** — no `Environment.*` variables, filesystem reads,
  network access, or locale-dependent formatting inside the sim.
- **Unordered iteration that affects results** — no behavior dependent on
  `Dictionary`/`HashSet` enumeration order, `object.GetHashCode`, or reference
  identity. Iterate fixed arrays/lists, or sort by a stable key.
- **Floating-point shortcuts** — same operations in the same order every tick;
  no `float`/`double` mixing that varies by platform, no `Math.Pow` tricks that
  differ across runtimes where a documented alternative exists.

Consequence: an identical intent sequence replayed from an identical snapshot
produces **bit-identical** telemetry. Tests and CI may rely on this.

Scope note: the `ReactorSim.Core` assembly also carries host plumbing,
`Api/HttpApiServer.cs` (HttpListener) and `Diagnostics/` (JSONL/CSV writers).
They sit outside the sim step: the server only enqueues intents and reads
snapshots, and the writers only observe ticks. Nothing reachable from
`Simulation.Tick()` may depend on them.

## Hermetic tests

Every test must pass with: no network, no wall-clock dependence, no
environment-variable or machine-path dependence, no reliance on test ordering
or parallelism settings, and no state leaked between tests. A test that cannot
run in a sandboxed CI container is broken by definition.

## Layout

- `ReactorSim.Core.Tests/` — xUnit unit tests for contracts that are cheaper to pin at unit
  level: intent JSON for every kind and boundary rejection, BOP sync/trip semantics, snapshot
  round trip and bit-identical resume, IF97 saturation points, golden-compare column coverage.
  Core grants it `InternalsVisibleTo` so tests can set up equipment states directly.
- `ReactorSim.Runner/Program.cs` — the scenario suite (U1–U10; U10 in `BopScenarios.cs`; the startup scenario in `StartupScenarios.cs`, which links `SandboxStart.cs`). Each scenario is
  a function with physics-invariant assertions; `--scenario all` runs them all.
- `ReactorSim.Runner/Verification.cs` — determinism, numerical and edge-case
  checks (`--verify`). Design, fixtures and known defects:
  [testing/simulation-verification.md](testing/simulation-verification.md).
  A check marked with a known defect prints XFAIL and turns into a hard failure
  (XPASS) once the defect is fixed, so the marker cannot rot.
- `ReactorSim.Unity/Assets/Editor/` — batchmode entry points:
  `ControlRoomSceneBuilder.BuildPhase2Scene` (scene hierarchy builds, 0 `error CS`)
  and `BlenderAssetPostprocessor.VerifyTrimSheets` (baked textures bound to the
  FBX materials).

## Runner entry points

| Command | What it checks |
|---|---|
| `--scenario all` | All of the below |
| `--scenario startup` | U1.1 cold shutdown → criticality → stable low power |
| `--scenario excursion` | U1.2 +300 pcm step self-limits via Doppler (AE1) |
| `--scenario sustained` | U1.3 100 % power equilibrium |
| `--scenario scram` | U1.4 SCRAM collapses prompt power to the decay-heat floor |
| `--scenario xenon-pit` | U1.5 post-SCRAM xenon pit blocks restart, then decays (AE2) |
| `--scenario loss-of-sink` | U1.6 loss of secondary heat sink causal chain (AE3) |
| `--scenario soak` | U1.7 60-minute finite-state and bounds soak |
| `--scenario snapshot` | U2 intent pipeline parity and snapshot JSON round trip |
| `--scenario http` | U3 in-process HTTP API smoke test |
| `--scenario mcp` | U4 MCP bridge stdio JSON-RPC end to end (needs the bridge built) |
| `--scenario missions` | U5/U8 assistant tiers and the three-mission arc |
| `--scenario replay` | U9 JSONL logging and golden CSV replay |
| `--scenario bop-auto` | U10.1 auto-load governor at power: synced at 3000 rpm, 40–65 MWe, vacuum held |
| `--scenario bop-manual` | U10.2 manual: load rejection → overspeed trip → reset → spin-up → sync → 50 % load |
| `--scenario bop-vacuum` | U10.3 loss of circulating water → low-vacuum trip → stop valve shut → SG relief; snapshot keeps the trip |
| `--scenario bop` | All U10 |
| `--scenario startup-grid` | U4 operator policy walks `cold-start-to-grid` (13 steps, `procedures.json`) from `SandboxStart.Apply` with Player intents only: every step ticks in order, generator synced, grid output at the procedure target, no SCRAM or BOP trip, finite state each tick. Prints per-step game minutes and the final state hash; a per-step 45-minute cap fails naming the step and live values |
| `--verify [--check <id>] [-v]` / `--list` | Determinism / numerics / edge-case contract. `STARTUP-01`: the startup run twice (same step minutes, intents, state hash); a SCRAM from power unticks the rod step and later but keeps the lineup ticked; the playtest order (throttle 0.05 to 0.11 first) leaves step 1 current and the plant subcritical |

Dev artifacts: `--log <file.jsonl>` (one deterministic JSON object per tick),
`--record <file.csv>` (per-tick telemetry), `--script <intents.json>`,
`--replay <intents.json> --golden <file.csv>`, `--serve [port]` (live HTTP API).

## Assertion style

- Prefer **invariants over exact values**: finite (no NaN/Inf), bounded
  (pressure < relief, temperatures inside the model's envelope), sign/shape
  (Doppler drives ρ negative on excursion, Xe rises after SCRAM then decays,
  decay heat trends to a floor). Floats drift across SDK versions and
  platforms; physics invariants do not.
- Use exact/tight-tolerance assertions only where the math is closed-form
  (e.g., serialization round-trips, unit conversions, clamp behavior).
- Golden replay compares with a small documented tolerance per field; a golden
  update is a deliberate, reviewable act, never auto-blessed in CI.

## CI

`bash scripts/ci.sh` is the verification contract: Release build with warnings as
errors, `--scenario all`, `--verify`, and (with `UNITY_EXE` set) the two Unity
batchmode checks. It must be green before work is called done.
`.github/workflows/ci.yml` runs the same script on every push to `master` and every PR, on
GitHub-hosted `ubuntu-latest`. Unity checks need a machine with Unity installed and stay local.

`ci.sh` also fails early when the Core DLL synced into Unity is stale: it recomputes
`scripts/core-source-hash.sh` (sha256 over `ReactorSim.Core` `*.cs`/`*.csproj`) and compares it with
`ReactorSim.Unity/Assets/Plugins/ReactorSim/core-source.sha256`, written by
`bash scripts/sync-core-to-unity.sh`. After any Core change, run that script and commit the result.

### Windows player build

Made on the Windows Unity box, not in CI (`ci.sh` only verifies):

```
"<Unity.exe>" -batchmode -nographics -projectPath ReactorSim.Unity -executeMethod BuildScript.BuildWindows -buildTarget StandaloneWindows64 -quit -logFile out/unity-build.log
```

Output is `out/build/ReactorSim.exe` (repo root, git-ignored) with `bundleVersion` stamped
`<version>+<UTC yyyyMMdd.HHmm>`. The exit code is non-zero on failure; the log has a
`[BuildWindows] OK/FAIL` line.

### Room renders (headless)

Images of the control room for layout/scale review; run on the Windows box, fetched back to Linux.
Both write 1600x900 PNGs for three viewpoints (`CAM_Seat_A1`, `CAM_Stand_Center`, `CAM_Top`) into the repo's
git-ignored `out/render/`. When none of those anchors exist yet, fixed viewpoints that frame the current hall
are used; when only some exist, the run fails naming the missing one.

```
# Blender Workbench (fast layout aid; never satisfies a gate) -> out/render/blender-{seat,stand,top}.png
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --python-exit-code 1 --python Blender/scripts/render_room.py

# Unity play-mode render (the one the checks trust) -> out/render/unity-{seat,stand,top}.png
# Needs a GPU session: no -nographics, and no -quit (RoomRender exits the editor itself, 0 ok / 1 fail).
"<Unity.exe>" -batchmode -projectPath ReactorSim.Unity -executeMethod RoomRender.Run -logFile out/unity-RoomRender.log
#   or as a CI step: ROOM_RENDER=1 UNITY_EXE=... bash scripts/ci.sh

# Back on Linux: copy out/render/*.png from the host into out/render/<timestamp>/
REACTOR_SIM_WIN_HOST=my-windows-box REACTOR_SIM_WIN_REPO='C:\path\to\reactor-sim' bash scripts/fetch-renders.sh [--dest DIR]
```

`RoomRender` logs `[RoomRender] OK <path> <bytes>` per image or `[RoomRender] FAIL ...`, and fails on an empty,
tiny (under 1 KB) or single-flat-colour image. `fetch-renders.sh` exits 0 fetched, 1 nothing found, 2 host
unreachable or bad usage (same contract as `fetch-playtest-logs.sh`).
