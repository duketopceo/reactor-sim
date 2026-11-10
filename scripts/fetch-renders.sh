#!/usr/bin/env bash
# Pulls the headless room renders back for review (read-only on the remote; nothing is written there).
#   REACTOR_SIM_WIN_HOST=<ssh host> REACTOR_SIM_WIN_REPO='C:\path\to\reactor-sim' bash scripts/fetch-renders.sh [--host H] [--remote-repo P] [--dest DIR]
# Copies <remote repo>\out\render\*.png (blender-*.png from Blender/scripts/render_room.py, unity-*.png from
# RoomRender.Run) on the host (Windows OpenSSH, cmd.exe shell) into out/render/<local timestamp>/ (or --dest DIR).
# Exit 0: PNGs fetched. Exit 1: nothing found / a copy failed (named on stderr). Exit 2: host unreachable / bad usage.
set -uo pipefail

HOST="${REACTOR_SIM_WIN_HOST:-}"
REMOTE_REPO="${REACTOR_SIM_WIN_REPO:-}"
DEST=""
SSH_TIMEOUT=25
SCP_TIMEOUT=300

usage() { sed -n '2,7p' "$0" | sed 's/^# \{0,1\}//'; }

while [ $# -gt 0 ]; do
  case "$1" in
    --host|--remote-repo|--dest) [ $# -ge 2 ] || { echo "$1 needs a value" >&2; exit 2; } ;;
  esac
  case "$1" in
    --host) HOST="${2:-}"; shift 2 ;;
    --remote-repo) REMOTE_REPO="${2:-}"; shift 2 ;;
    --dest) DEST="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done
[ -n "$HOST" ] || { echo "set REACTOR_SIM_WIN_HOST or pass --host" >&2; exit 2; }
[ -n "$REMOTE_REPO" ] || { echo "set REACTOR_SIM_WIN_REPO or pass --remote-repo" >&2; exit 2; }

cd "$(dirname "$0")/.."
[ -n "$DEST" ] || DEST="out/render/$(date +%Y%m%d-%H%M%S)"

SSH_OPTS=(-o BatchMode=yes -o ConnectTimeout=8 -o LogLevel=ERROR)
rssh() { timeout "$SSH_TIMEOUT" ssh "${SSH_OPTS[@]}" "$HOST" "$@" 2>/dev/null | tr -d '\r'; return "${PIPESTATUS[0]}"; }

# 1. reachable?
probe="$(rssh 'echo REACHABLE')"; rc=$?
if [ "$rc" -ne 0 ] || [ "${probe%%$'\n'*}" != "REACHABLE" ]; then
  echo "FAIL: host '$HOST' is unreachable (ssh exit $rc; 124 = timed out). Nothing fetched." >&2
  exit 2
fi
winbase="${REMOTE_REPO%\\}\\out\\render"
scpbase="${winbase//\\//}"   # C:/path/... works for Windows OpenSSH scp
echo "host: $HOST   remote: $winbase"
echo "dest: $DEST"

# 2. list and copy
mapfile -t pngs < <(rssh "dir /b /a-d \"$winbase\\*.png\"" | sed '/^$/d' | LC_ALL=C sort)
if [ "${#pngs[@]}" -eq 0 ]; then
  echo "MISSING on $HOST: no PNGs under $winbase" >&2
  exit 1
fi

mkdir -p "$DEST"
fetched=()
failed=()
for p in "${pngs[@]}"; do
  if timeout "$SCP_TIMEOUT" scp -q "${SSH_OPTS[@]}" "$HOST:$scpbase/$p" "$DEST/$p"; then
    fetched+=("$p ($(wc -c < "$DEST/$p") bytes)")
  else
    failed+=("$p (scp failed)")
  fi
done

echo
echo "fetched ${#fetched[@]} image(s) into $DEST:"
for x in "${fetched[@]:-}"; do [ -n "$x" ] && echo "  + $x"; done
if [ "${#failed[@]}" -gt 0 ]; then
  echo "FAILED on $HOST under $winbase:" >&2
  for x in "${failed[@]}"; do echo "  - $x" >&2; done
  exit 1
fi
exit 0
