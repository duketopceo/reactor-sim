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

# exact-boundary values: every limit is "at most" / "at least", so a value exactly on it passes and a hair past it fails
mk "$tmp/b_p95" 8.0 8.0 100 3.0           # +4.0 ms p95 exactly (limit 4)
mk "$tmp/b_p95x" 8.01 8.01 100 3.0        # +4.01
mk "$tmp/b_draws" 5.0 6.0 220 3.0         # +120 draws exactly (limit 120)
mk "$tmp/b_drawsx" 5.0 6.0 221 3.0        # +121
mk "$tmp/b_p99" 5.0 12.0 100 3.0          # p99 exactly 12 ms
mk "$tmp/b_p99x" 5.0 12.01 100 3.0
mk "$tmp/b_away" 5.0 6.0 100 4.0          # away +1.0 ms exactly (limit 1)
mk "$tmp/b_awayx" 5.0 6.0 100 4.01
expect 0 "p95 exactly on the limit passes" "$tmp/b_p95" --baseline "$tmp/base" --budget-only
expect 1 "p95 just over the limit fails" "$tmp/b_p95x" --baseline "$tmp/base" --budget-only
expect 0 "draw calls exactly on the limit pass" "$tmp/b_draws" --baseline "$tmp/base" --budget-only
expect 1 "draw calls just over the limit fail" "$tmp/b_drawsx" --baseline "$tmp/base" --budget-only
expect 0 "p99 exactly 12 ms passes" "$tmp/b_p99" --baseline "$tmp/base" --budget-only
expect 1 "p99 just over 12 ms fails" "$tmp/b_p99x" --baseline "$tmp/base" --budget-only
expect 0 "out-of-view cost exactly 1 ms passes" "$tmp/b_away" --baseline "$tmp/base" --budget-only
expect 1 "out-of-view cost just over 1 ms fails" "$tmp/b_awayx" --baseline "$tmp/base" --budget-only

# a baseline that lacks one of the two poses cannot be graded: that is a failure, never a pass
for missing in gallery away; do
  d="$tmp/base_no_$missing/1600x900"; mkdir -p "$d"
  head -c 30000 /dev/zero > "$d/g.png"
  if [ "$missing" = gallery ]; then
    echo '{"captures":[{"pose":"away","label":"gallery-away","file":"g.png","frameMsP95":3,"frameMsP99":3,"drawCalls":50}]}' > "$d/captures.json"
  else
    echo '{"captures":[{"pose":"gallery","label":"gallery","file":"g.png","frameMsP95":4,"frameMsP99":5,"drawCalls":100}]}' > "$d/captures.json"
  fi
  expect 1 "a baseline missing $missing fails" "$tmp/inside" --baseline "$tmp/base_no_$missing" --budget-only
done

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

# boundary: gains exact in binary floating point. idle +2^-8, full +2^-6 is exactly 4 x idle and above the 0.01 floor.
mkglow "$tmp/glowedge" 0.25 0.25390625 0.2578125 0.265625
mkglow "$tmp/glowedgex" 0.25 0.25390625 0.2578125 0.2656   # full a hair below 4 x idle
mkglow "$tmp/glowdelta" 0.5 0.5 0.505 0.51         # full gain 0.01 over off (idle gain 0)
mkglow "$tmp/glowdeltax" 0.5 0.5 0.505 0.509       # full gain 0.009
expect 0 "full gain exactly 4 x idle passes" "$tmp/glowedge" --only $GLOWPOSES
expect 1 "full gain just under 4 x idle fails" "$tmp/glowedgex" --only $GLOWPOSES
expect 0 "full gain on the 0.01 floor passes" "$tmp/glowdelta" --only $GLOWPOSES
expect 1 "full gain just under 0.01 fails" "$tmp/glowdeltax" --only $GLOWPOSES

# a glow check that is only partly graded must not pass quietly
mkdir -p "$tmp/glowskipoff/1600x900"
head -c 30000 /dev/zero > "$tmp/glowskipoff/1600x900/g.png"
cat > "$tmp/glowskipoff/1600x900/captures.json" <<EOF
{"captures":[
 {"pose":"glowoff","skipped":"no capture"},
 {"pose":"glowidle","label":"glowidle","file":"g.png","cropMean":0.104},
 {"pose":"glowmid","label":"glowmid","file":"g.png","cropMean":0.120},
 {"pose":"glowfull","label":"glowfull","file":"g.png","cropMean":0.160}
]}
EOF
expect 1 "--allow-skip glowoff cannot waive the reference frame" "$tmp/glowskipoff" --only $GLOWPOSES --allow-skip glowoff
mkdir -p "$tmp/glowallskipped/1600x900"
echo '{"captures":[{"pose":"glowoff","skipped":"x"},{"pose":"glowidle","skipped":"x"},{"pose":"glowmid","skipped":"x"},{"pose":"glowfull","skipped":"x"}]}' > "$tmp/glowallskipped/1600x900/captures.json"
expect 1 "every glow pose skipped leaves the check ungraded and fails" "$tmp/glowallskipped" --only $GLOWPOSES --allow-skip $GLOWPOSES
mkdir -p "$tmp/glownomid/1600x900"
head -c 30000 /dev/zero > "$tmp/glownomid/1600x900/g.png"
cat > "$tmp/glownomid/1600x900/captures.json" <<EOF
{"captures":[
 {"pose":"glowoff","label":"glowoff","file":"g.png","cropMean":0.100},
 {"pose":"glowidle","label":"glowidle","file":"g.png","cropMean":0.104},
 {"pose":"glowfull","label":"glowfull","file":"g.png","cropMean":0.160}
]}
EOF
expect 1 "a missing glowmid is not silently skipped" "$tmp/glownomid" --only glowoff,glowidle,glowfull
expect 0 "glowmid waived on purpose passes" "$tmp/glownomid" --only glowoff,glowidle,glowfull --allow-skip glowmid
waived_out="$(bash scripts/check-captures.sh "$tmp/glownomid" --only glowoff,glowidle,glowfull --allow-skip glowmid 2>&1)"
case "$waived_out" in *"NOT graded"*) echo "ok   the waived glowmid check says it was not graded" ;; *) echo "FAIL the waived glowmid check did not say it was ungraded"; failures=$((failures + 1)) ;; esac

[ "$failures" -eq 0 ] && echo "check-captures budget self-test: PASS" || echo "check-captures budget self-test: FAILED ($failures)"
exit "$failures"
