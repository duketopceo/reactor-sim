#!/usr/bin/env bash
# Local CI — the verification contract:
#   Clean-room scan + scanner self-test -> Core DLL freshness -> Release build (0 warnings) -> xUnit tests -> Runner --scenario all -> Runner --verify
#   -> [UNITY_EXE set] Unity batchmode scene build + trim-sheet binding check.
# Any failure exits nonzero. Runs on Linux (dotnet 8+) and Git Bash on Windows.
#   bash scripts/ci.sh
#   UNITY_EXE="/c/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Unity.exe" bash scripts/ci.sh
#   (UNITY_EXE also runs RoundTripCheck.Run: play mode, no graphics; RoundTripCheck.RunNegative is run by hand.)
#   ROOM_RENDER=1 UNITY_EXE=... bash scripts/ci.sh   # + play-mode room render (needs a GPU session; writes out/render/unity-*.png)
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$(pwd)"

export PATH="/c/Program Files/dotnet:$PATH"   # Windows dev boxes; harmless elsewhere
export DOTNET_ROLL_FORWARD=LatestMajor          # net8.0 targets run on newer runtimes
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

say() { printf '\n=== %s ===\n' "$*"; }
runner() { dotnet run --project ReactorSim.Runner -c Release --no-build -- "$@"; }

say "Clean-room scan (no third-party simulator references; ASCII, UTF-16, separated and compressed content, paths)"
bash scripts/clean_room_check.sh
say "Clean-room scanner self-test (negative controls)"
bash scripts/clean_room_check.sh --self-test

say "Core DLL freshness (Unity plugin vs ReactorSim.Core sources)"
recorded="ReactorSim.Unity/Assets/Plugins/ReactorSim/core-source.sha256"
if [ ! -f "$recorded" ] || [ "$(bash scripts/core-source-hash.sh)" != "$(tr -d '\r\n' < "$recorded")" ]; then
  echo "Core sources differ from the DLL synced into Unity (or $recorded is missing)." >&2
  echo "Run: bash scripts/sync-core-to-unity.sh   then commit the updated Plugins/ReactorSim files." >&2
  exit 1
fi

say "dotnet build (Release, warnings as errors)"
dotnet build reactor-sim.sln -c Release -warnaserror

say "unit tests (xUnit)"
dotnet test ReactorSim.Core.Tests -c Release --no-build --nologo

say "scenario suite (U1-U10)"
runner --scenario all

say "verification checks (determinism / numerics / edge cases)"
runner --verify

if [ -n "${UNITY_EXE:-}" ]; then
  project="$ROOT/ReactorSim.Unity"
  log_dir="$ROOT/out"; mkdir -p "$log_dir"
  if command -v cygpath >/dev/null 2>&1; then project="$(cygpath -w "$project")"; fi
  unity() {
    local method="$1" log="$log_dir/unity-$1.log"
    "$UNITY_EXE" -batchmode -nographics -projectPath "$project" -executeMethod "$method" -quit -logFile "$log" \
      || { echo "Unity $method failed (log: $log)" >&2; return 1; }
    if grep -q 'error CS' "$log"; then echo "Unity compile errors (log: $log)" >&2; return 1; fi
  }
  # Same as unity() but WITH graphics and WITHOUT -quit: the render finishes asynchronously in play mode and the
  # method calls EditorApplication.Exit itself.
  unity_gfx() {
    local method="$1" log="$log_dir/unity-$1.log"
    "$UNITY_EXE" -batchmode -projectPath "$project" -executeMethod "$method" -logFile "$log" \
      || { echo "Unity $method failed (log: $log)" >&2; return 1; }
    if grep -q 'error CS' "$log"; then echo "Unity compile errors (log: $log)" >&2; return 1; fi
  }
  # Play-mode checks that render nothing: WITHOUT -quit (the method exits the editor itself once play mode ends) and
  # WITH -nographics. If play mode misbehaves headless on the box, drop -nographics here.
  unity_play() {
    local method="$1" log="$log_dir/unity-$1.log"
    "$UNITY_EXE" -batchmode -nographics -projectPath "$project" -executeMethod "$method" -logFile "$log" \
      || { echo "Unity $method failed (log: $log)" >&2; return 1; }
    if grep -q 'error CS' "$log"; then echo "Unity compile errors (log: $log)" >&2; return 1; fi
  }
  say "Unity: Phase-2 scene build"
  unity ControlRoomSceneBuilder.BuildPhase2Scene
  say "Unity: trim-sheet binding"
  unity BlenderAssetPostprocessor.VerifyTrimSheets
  say "Unity: control map"
  unity ControlMapCheck.Run
  say "Unity: instruments"
  unity InstrumentCheck.Run
  say "Unity: layout (reach, view angle, occlusion)"
  unity LayoutCheck.Run
  say "Unity: round trip (every bound control, play mode)"
  unity_play RoundTripCheck.Run
  if [ "${ROOM_RENDER:-}" = "1" ]; then
    say "Unity: room render (play mode, with graphics)"
    unity_gfx RoomRender.Run
  fi
else
  say "Unity checks skipped (set UNITY_EXE to run them)"
fi

say "CI PASSED"
