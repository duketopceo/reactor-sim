#!/usr/bin/env bash
# Self-test for the hall budget gate in scripts/check-captures.sh: builds fake capture runs and checks the verdicts, including
# that a deliberately over-budget run FAILS (a gate that cannot fail proves nothing). Needs python3 only; runs on any box.
#   bash scripts/test-check-captures.sh      exit 0 = every case behaved
set -uo pipefail
cd "$(dirname "$0")/.."
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
failures=0

# mk <dir> <gallery p95> <gallery p99> <gallery draws> <away p95>
mk() {
  local d="$1/1600x900"
  mkdir -p "$d"
  head -c 30000 /dev/zero > "$d/01-gallery.png"
  head -c 30000 /dev/zero > "$d/02-gallery-away.png"
  cat > "$d/captures.json" <<EOF
{"captures":[
 {"pose":"gallery","label":"gallery","file":"01-gallery.png","frameMsP95":$2,"frameMsP99":$3,"drawCalls":$4},
 {"pose":"away","label":"gallery-away","file":"02-gallery-away.png","frameMsP95":$5,"frameMsP99":$5,"drawCalls":50}
]}
EOF
}

expect() {   # expect <want exit> <label> <args...>
  local want="$1" label="$2"; shift 2
  bash scripts/check-captures.sh "$@" > "$tmp/out.txt" 2>&1
  local got=$?
  if [ "$got" -eq "$want" ]; then echo "ok   $label (exit $got)"; else echo "FAIL $label: wanted exit $want, got $got"; sed 's/^/     /' "$tmp/out.txt"; failures=$((failures + 1)); fi
}

mk "$tmp/base" 4.0 5.0 100 3.0
mk "$tmp/inside" 6.5 8.0 180 3.5          # +2.5 ms, +80 draws, p99 8, away +0.5
mk "$tmp/slow" 9.0 10.0 150 3.0           # +5 ms p95
mk "$tmp/draws" 5.0 6.0 260 3.0           # +160 draws
mk "$tmp/p99" 5.0 14.0 150 3.0            # p99 over 12 ms
mk "$tmp/away" 5.0 6.0 150 4.6            # +1.6 ms while out of view
mk "$tmp/nodraw" 5.0 6.0 -1 3.0           # draw counter unavailable

expect 0 "inside the budget passes" "$tmp/inside" --baseline "$tmp/base" --budget-only
expect 1 "p95 over budget fails" "$tmp/slow" --baseline "$tmp/base" --budget-only
expect 1 "draw calls over budget fail" "$tmp/draws" --baseline "$tmp/base" --budget-only
expect 1 "gallery p99 over 12 ms fails" "$tmp/p99" --baseline "$tmp/base" --budget-only
expect 1 "out-of-view cost over 1 ms fails" "$tmp/away" --baseline "$tmp/base" --budget-only
expect 1 "unavailable draw counter fails" "$tmp/nodraw" --baseline "$tmp/base" --budget-only
mkdir -p "$tmp/nop95/1600x900"
head -c 30000 /dev/zero > "$tmp/nop95/1600x900/g.png"
cat > "$tmp/nop95/1600x900/captures.json" <<EOF
{"captures":[
 {"pose":"gallery","label":"gallery","file":"g.png","drawCalls":100},
 {"pose":"away","label":"gallery-away","file":"g.png","drawCalls":50}
]}
EOF
expect 1 "missing frame-time percentiles fail instead of passing" "$tmp/nop95" --baseline "$tmp/base" --budget-only
expect 2 "an empty --only list is a usage error" "$tmp/inside" --only ""
expect 2 "budget-only without a baseline is a usage error" "$tmp/inside" --budget-only

# rack glow cases: mkglow <dir> <off> <idle> <mid> <full>
mkglow() {
  local d="$1/1600x900"
  mkdir -p "$d"
  head -c 30000 /dev/zero > "$d/g.png"
  cat > "$d/captures.json" <<EOF
{"captures":[
 {"pose":"glowoff","label":"glowoff","file":"g.png","frameMsP95":5,"frameMsP99":6,"drawCalls":100,"cropMean":$2},
 {"pose":"glowidle","label":"glowidle","file":"g.png","frameMsP95":5,"frameMsP99":6,"drawCalls":100,"cropMean":$3},
 {"pose":"glowmid","label":"glowmid","file":"g.png","frameMsP95":5,"frameMsP99":6,"drawCalls":100,"cropMean":$4},
 {"pose":"glowfull","label":"glowfull","file":"g.png","frameMsP95":5,"frameMsP99":6,"drawCalls":100,"cropMean":$5}
]}
EOF
}
mkglow "$tmp/glowok" 0.100 0.104 0.120 0.160       # gains 0.004 / 0.020 / 0.060
mkglow "$tmp/glowflat" 0.100 0.101 0.102 0.103     # full gain only 0.003
mkglow "$tmp/glowweak" 0.100 0.120 0.130 0.150     # full gain 0.05 but idle gain 0.02: under 4x
mkglow "$tmp/glowbackwards" 0.100 0.104 0.090 0.160
GLOWPOSES=glowoff,glowidle,glowmid,glowfull
expect 0 "rack glow that rises from off passes" "$tmp/glowok" --only $GLOWPOSES
expect 1 "rack glow that barely changes fails" "$tmp/glowflat" --only $GLOWPOSES
expect 1 "rack glow under four times the idle gain fails" "$tmp/glowweak" --only $GLOWPOSES
expect 1 "rack glow that is not monotonic fails" "$tmp/glowbackwards" --only $GLOWPOSES

[ "$failures" -eq 0 ] && echo "check-captures budget self-test: PASS" || echo "check-captures budget self-test: FAILED ($failures)"
exit "$failures"
