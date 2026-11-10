#!/usr/bin/env bash
# Runs the in-player capture mode (-capture) on the Windows build machine and pulls the PNGs and captures.json back.
#   REACTOR_SIM_WIN_HOST=<ssh host> REACTOR_SIM_WIN_REPO='C:\path\to\worktree' \
#     bash scripts/capture-playtest.sh [--host H] [--repo WINPATH] [--sizes 1600x900,1920x1080] [--poses a,b,c] [--dest DIR] [--timeout SECONDS]
# The player is started inside the logged-in interactive desktop session (a scheduled task with interactive logon): a process
# started straight from an ssh session has no desktop and no GPU, so it cannot render a screenshot. The build is the one
# BuildScript made at <repo>\out\build\ReactorSim.exe, in a separate worktree, never the owner's checkout. One player at a
# time; nothing is killed. Output lands in out/capture/<timestamp>/<WxH>/ (PNGs + captures.json), then grade it with
# scripts/check-captures.sh.
# Fallback with no ssh: run the same flag locally, e.g.  ReactorSim.exe -capture C:\some\dir -size 1600x900
# Exit 0: every size produced captures.json and was fetched. Exit 1: a size timed out or something was missing.
# Exit 2: host unreachable / bad usage.
set -uo pipefail

HOST="${REACTOR_SIM_WIN_HOST:-}"
REPO="${REACTOR_SIM_WIN_REPO:-}"
SIZES="1600x900,1920x1080"
POSES=""
DEST=""
TIMEOUT=300
TASK="ReactorSimCapture"
SSH_TIMEOUT=25
SCP_TIMEOUT=300

usage() { sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'; }

while [ $# -gt 0 ]; do
  case "$1" in
    --host|--repo|--sizes|--poses|--dest|--timeout) [ $# -ge 2 ] || { echo "$1 needs a value" >&2; exit 2; } ;;
  esac
  case "$1" in
    --host) HOST="$2"; shift 2 ;;
    --repo) REPO="$2"; shift 2 ;;
    --sizes) SIZES="$2"; shift 2 ;;
    --poses) POSES="$2"; shift 2 ;;
    --dest) DEST="$2"; shift 2 ;;
    --timeout) TIMEOUT="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done
case "$TIMEOUT" in ''|*[!0-9]*) echo "--timeout needs a whole number of seconds, got '$TIMEOUT'" >&2; exit 2 ;; esac
[ -n "$HOST" ] || { echo "set REACTOR_SIM_WIN_HOST or pass --host" >&2; exit 2; }
[ -n "$REPO" ] || { echo "set REACTOR_SIM_WIN_REPO (the Windows path of the build worktree) or pass --repo" >&2; exit 2; }
IFS=',' read -r -a size_list <<< "$SIZES"
for sz in "${size_list[@]}"; do
  case "$sz" in [0-9]*x[0-9]*) ;; *) echo "bad size '$sz' (want 1600x900)" >&2; exit 2 ;; esac
  case "$sz" in *[!0-9x]*) echo "bad size '$sz' (want 1600x900)" >&2; exit 2 ;; esac
done
case "$POSES" in *[!a-z,]*) echo "bad --poses '$POSES' (comma-separated pose names)" >&2; exit 2 ;; esac

cd "$(dirname "$0")/.."
STAMP="$(date -u +%Y%m%d-%H%M%S)"
[ -n "$DEST" ] || DEST="out/capture/$STAMP"

SSH_OPTS=(-o BatchMode=yes -o ConnectTimeout=8 -o LogLevel=ERROR)
rssh() { timeout "$SSH_TIMEOUT" ssh "${SSH_OPTS[@]}" "$HOST" "$@" 2>/dev/null | tr -d '\r'; return "${PIPESTATUS[0]}"; }

who="$(rssh 'echo %USERNAME%')"; rc=$?
who="${who%%$'\n'*}"
if [ "$rc" -ne 0 ] || [ -z "$who" ] || [ "$who" = '%USERNAME%' ]; then
  echo "FAIL: host '$HOST' is unreachable (ssh exit $rc; 124 = timed out). Nothing run." >&2
  exit 2
fi

REPO="${REPO%\\}"
exe="$REPO\\out\\build\\ReactorSim.exe"
remote_out="$REPO\\out\\capture\\$STAMP"
scp_out="${remote_out//\\//}"
remote_exists() { [ "$(rssh "if exist \"$1\" (echo YES) else (echo NO)")" = "YES" ]; }

remote_exists "$exe" || { echo "FAIL: no build at $exe on the host (run BuildScript.BuildWindows there first)" >&2; exit 2; }
echo "host: $HOST   build: $exe"
echo "remote out: $remote_out   local dest: $DEST"

rssh "mkdir \"$remote_out\"" >/dev/null
mkdir -p "$DEST"
failed=()

for sz in "${size_list[@]}"; do
  args="-capture $remote_out -size $sz"
  [ -n "$POSES" ] && args="$args -poses $POSES"
  echo "== $sz: starting the player in the interactive session"
  rssh "schtasks /Delete /TN $TASK /F" >/dev/null
  if ! rssh "schtasks /Create /TN $TASK /TR \"\\\"$exe\\\" $args\" /SC ONCE /ST 00:00 /RU $who /IT /F" >/dev/null; then
    failed+=("$sz (could not create the scheduled task)"); continue
  fi
  if ! rssh "schtasks /Run /TN $TASK" >/dev/null; then
    failed+=("$sz (could not start the scheduled task)"); rssh "schtasks /Delete /TN $TASK /F" >/dev/null; continue
  fi
  waited=0
  until remote_exists "$remote_out\\$sz\\captures.json"; do
    if [ "$waited" -ge "$TIMEOUT" ]; then break; fi
    sleep 5; waited=$((waited + 5))
  done
  rssh "schtasks /Delete /TN $TASK /F" >/dev/null
  if remote_exists "$remote_out\\$sz\\captures.json"; then
    echo "   $sz: captures.json written after ${waited}s"
  else
    failed+=("$sz (no captures.json within ${TIMEOUT}s)")
  fi
done

if timeout "$SCP_TIMEOUT" scp -q -r "${SSH_OPTS[@]}" "$HOST:$scp_out/." "$DEST/"; then
  echo "fetched into $DEST:"
  find "$DEST" -type f | sort | sed 's/^/  + /'
else
  failed+=("fetch (scp failed)")
fi

if [ "${#failed[@]}" -gt 0 ]; then
  echo "PROBLEMS:" >&2
  for x in "${failed[@]}"; do echo "  - $x" >&2; done
  exit 1
fi
echo "grade it with:  bash scripts/check-captures.sh $DEST"
exit 0
