#!/usr/bin/env bash
# Pulls a Windows playtest's logs back for review (read-only on the remote; nothing is written there).
#   REACTOR_SIM_WIN_HOST=<ssh host> bash scripts/fetch-playtest-logs.sh [--host H] [--latest N] [--dest DIR]
# Copies, from %USERPROFILE%\AppData\LocalLow\duketopceo\ReactorSim\ on the host (Windows OpenSSH, cmd.exe shell):
#   Player.log, Player-prev.log, and the sessions\ folders (the latest N, default all)
# into out/playtest/<local timestamp>/ (or --dest DIR).
# Exit 0: everything expected was fetched. Exit 1: something was missing (named on stderr; what exists is still
# copied). Exit 2: host unreachable / bad usage. company/product come from ProjectSettings (duketopceo / ReactorSim).
set -uo pipefail

HOST="${REACTOR_SIM_WIN_HOST:-}"
LATEST=0
DEST=""
COMPANY="duketopceo"
PRODUCT="ReactorSim"
SSH_TIMEOUT=25
SCP_TIMEOUT=300

usage() { sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'; }

while [ $# -gt 0 ]; do
  case "$1" in
    --host|--latest|--dest) [ $# -ge 2 ] || { echo "$1 needs a value" >&2; exit 2; } ;;
  esac
  case "$1" in
    --host) HOST="${2:-}"; shift 2 ;;
    --latest) LATEST="${2:-}"; shift 2 ;;
    --dest) DEST="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done
case "$LATEST" in ''|*[!0-9]*) echo "--latest needs a non-negative integer, got '$LATEST'" >&2; exit 2 ;; esac
[ -n "$HOST" ] || { echo "set REACTOR_SIM_WIN_HOST or pass --host" >&2; exit 2; }

cd "$(dirname "$0")/.."
[ -n "$DEST" ] || DEST="out/playtest/$(date +%Y%m%d-%H%M%S)"

SSH_OPTS=(-o BatchMode=yes -o ConnectTimeout=8 -o LogLevel=ERROR)
rssh() { timeout "$SSH_TIMEOUT" ssh "${SSH_OPTS[@]}" "$HOST" "$@" 2>/dev/null | tr -d '\r'; return "${PIPESTATUS[0]}"; }

# 1. reachable? and where is the profile?
profile="$(rssh 'echo %USERPROFILE%')"; rc=$?
profile="${profile%%$'\n'*}"
if [ "$rc" -ne 0 ] || [ -z "$profile" ] || [ "$profile" = '%USERPROFILE%' ]; then
  echo "FAIL: host '$HOST' is unreachable (ssh exit $rc; 124 = timed out). Nothing fetched." >&2
  exit 2
fi
winbase="$profile\\AppData\\LocalLow\\$COMPANY\\$PRODUCT"
scpbase="${winbase//\\//}"   # C:/path/... works for Windows OpenSSH scp
echo "host: $HOST   remote: $winbase"
echo "dest: $DEST"

remote_exists() { # $1 = windows path, $2 = file|dir
  local probe
  if [ "$2" = dir ]; then probe="if exist \"$1\\*\" (echo YES) else (echo NO)"; else probe="if exist \"$1\" (echo YES) else (echo NO)"; fi
  [ "$(rssh "$probe")" = "YES" ]
}

mkdir -p "$DEST"
missing=()
fetched=()

# 2. logs
for f in Player.log Player-prev.log; do
  if remote_exists "$winbase\\$f" file; then
    if timeout "$SCP_TIMEOUT" scp -q "${SSH_OPTS[@]}" "$HOST:$scpbase/$f" "$DEST/$f"; then
      fetched+=("$f ($(wc -c < "$DEST/$f") bytes)")
    else
      missing+=("$f (scp failed)")
    fi
  elif [ "$f" = Player.log ]; then
    missing+=("$f")
  else
    echo "note: no $f yet (Unity writes it from the second launch on)" >&2
  fi
done

# 3. sessions (names sort chronologically: yyyyMMdd-HHmmss[-n])
if remote_exists "$winbase\\sessions" dir; then
  mapfile -t all < <(rssh "dir /b /ad \"$winbase\\sessions\"" | sed '/^$/d' | LC_ALL=C sort)
  if [ "${#all[@]}" -eq 0 ]; then
    missing+=("sessions\\ (folder is empty)")
  else
    pick=("${all[@]}")
    if [ "$LATEST" -gt 0 ] && [ "${#all[@]}" -gt "$LATEST" ]; then pick=("${all[@]: -$LATEST}"); fi
    mkdir -p "$DEST/sessions"
    for s in "${pick[@]}"; do
      if timeout "$SCP_TIMEOUT" scp -q -r "${SSH_OPTS[@]}" "$HOST:$scpbase/sessions/$s" "$DEST/sessions/"; then
        fetched+=("sessions/$s ($(find "$DEST/sessions/$s" -type f | wc -l) files)")
      else
        missing+=("sessions\\$s (scp failed)")
      fi
    done
  fi
else
  missing+=("sessions\\")
fi

# 4. screenshots (optional: -smoke runs with a graphics device write them); newest 6
if remote_exists "$winbase\\shots" dir; then
  mapfile -t shots < <(rssh "dir /b /a-d \"$winbase\\shots\\*.png\"" | sed '/^$/d' | LC_ALL=C sort | tail -n 6)
  if [ "${#shots[@]}" -gt 0 ]; then
    mkdir -p "$DEST/shots"
    n=0
    for p in "${shots[@]}"; do
      timeout "$SCP_TIMEOUT" scp -q "${SSH_OPTS[@]}" "$HOST:$scpbase/shots/$p" "$DEST/shots/" && n=$((n + 1))
    done
    fetched+=("shots/ ($n png)")
  fi
fi

echo
echo "fetched ${#fetched[@]} item(s) into $DEST:"
for x in "${fetched[@]:-}"; do [ -n "$x" ] && echo "  + $x"; done
if [ "${#missing[@]}" -gt 0 ]; then
  echo "MISSING on $HOST under $winbase:" >&2
  for x in "${missing[@]}"; do echo "  - $x" >&2; done
  exit 1
fi
exit 0
