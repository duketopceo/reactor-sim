#!/usr/bin/env bash
# Grades a fetched capture run (scripts/capture-playtest.sh, or a local -capture run) against fixed thresholds.
#   bash scripts/check-captures.sh <dir> [--allow-skip pose,pose] [--only pose,pose] [--baseline <dir> [--budget-only]]
# <dir> holds <WxH>/captures.json and the PNGs next to it. The thresholds live here, not in the build, so the build cannot grade
# itself. With --baseline <dir> (a capture run of the same poses before the hall change) it also grades the hall's runtime
# budget (KTD9): the gallery and gallery-away captures must both exist in both runs. Exit 0: every check passed. 
# --budget-only grades only the gallery and gallery-away poses (a run made with -poses gallery,away). --only a,b requires only
# those poses (for example glowoff,glowidle,glowfull). When glow poses are present, the rack glow must rise from off through idle and mid to full power.
# Exit 1: a check failed (each failure is named). Exit 2: bad usage or nothing to grade.
set -uo pipefail

# ---- fixed thresholds ----
GLYPH_MIN_PX=10        # CRT body text: projected glyph height at the captured size (R11)
CONTRAST_MIN=4.5       # CRT text vs its background, measured on the rendered pixels (WCAG AA body text)
PNG_MIN_BYTES=20000    # a real 1600x900 frame is far above this; a blank or failed capture is not
# hall runtime budget (KTD9), only graded with --baseline
BUDGET_P95_MS=4          # gallery pose: p95 frame time may rise at most this much over the baseline
BUDGET_DRAWS=120         # gallery pose: draw calls may rise at most this much over the baseline
BUDGET_GALLERY_P99_MS=12 # gallery pose: p99 frame time, absolute (at least 120 fps)
BUDGET_AWAY_MS=1         # gallery-away pose (window out of view): p95 may rise at most this much
# rack glow (U7): mean rendered brightness over the racks, measured against a glowoff frame (no glow at all)
GLOW_FULL_OVER_FLOOR_MIN=4    # (full - off) must be at least this multiple of (idle - off): "a set multiple of the floor"
GLOW_FULL_DELTA_MIN=0.01      # and at least this much absolute mean luminance (0..1), so a flat render cannot pass
REQUIRED_POSES="seat stand crt tooltip rejected drag checklist pause tablet clipboard menu"

DIR=""
BASELINE=""
BUDGET_ONLY=""
ONLY=""
ALLOW_SKIP=""
while [ $# -gt 0 ]; do
  case "$1" in
    --budget-only) BUDGET_ONLY=1; shift ;;
    --only) [ $# -ge 2 ] || { echo "--only needs a pose list" >&2; exit 2; }; ONLY="${2//,/ }"; shift 2 ;;
    --baseline) [ $# -ge 2 ] || { echo "--baseline needs a directory" >&2; exit 2; }; BASELINE="$2"; shift 2 ;;
    --allow-skip) [ $# -ge 2 ] || { echo "--allow-skip needs a value" >&2; exit 2; }; ALLOW_SKIP="$2"; shift 2 ;;
    -h|--help) sed -n '2,6p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    -*) echo "unknown option: $1" >&2; exit 2 ;;
    *) [ -z "$DIR" ] || { echo "one directory only" >&2; exit 2; }; DIR="$1"; shift ;;
  esac
done
[ -n "$DIR" ] && [ -d "$DIR" ] || { echo "usage: check-captures.sh <dir> [--allow-skip pose,pose] [--baseline <dir>]" >&2; exit 2; }
[ -z "$BUDGET_ONLY" ] || [ -n "$BASELINE" ] || { echo "--budget-only needs --baseline" >&2; exit 2; }
[ -z "$BUDGET_ONLY" ] || REQUIRED_POSES="gallery away"
[ -z "$ONLY" ] || REQUIRED_POSES="$ONLY"
[ -z "$BASELINE" ] || [ -d "$BASELINE" ] || { echo "baseline directory not found: $BASELINE" >&2; exit 2; }

mapfile -t jsons < <(find "$DIR" -maxdepth 3 -name captures.json | LC_ALL=C sort)
[ "${#jsons[@]}" -gt 0 ] || { echo "no captures.json under $DIR" >&2; exit 2; }

fail=0
for j in "${jsons[@]}"; do
  echo "== $j"
  GLYPH_MIN_PX="$GLYPH_MIN_PX" CONTRAST_MIN="$CONTRAST_MIN" PNG_MIN_BYTES="$PNG_MIN_BYTES" \
    REQUIRED_POSES="$REQUIRED_POSES" ALLOW_SKIP="$ALLOW_SKIP" GLOW_MULT="$GLOW_FULL_OVER_FLOOR_MIN" GLOW_DELTA="$GLOW_FULL_DELTA_MIN" python3 -I - "$j" <<'PY'
import json, os, sys

path = sys.argv[1]
base = os.path.dirname(path)
glyph_min = float(os.environ["GLYPH_MIN_PX"])
contrast_min = float(os.environ["CONTRAST_MIN"])
png_min = int(os.environ["PNG_MIN_BYTES"])
required = os.environ["REQUIRED_POSES"].split()
allow_skip = [p for p in os.environ["ALLOW_SKIP"].split(",") if p]

try:
    data = json.load(open(path))
except Exception as ex:
    print("FAIL cannot read %s: %s" % (path, ex))
    sys.exit(1)

problems = []
seen = set()
for c in data.get("captures", []):
    pose = c.get("pose", "?")
    if "skipped" in c:
        if pose not in allow_skip:
            problems.append("%s skipped: %s" % (pose, c["skipped"]))
        continue
    seen.add(pose)
    label = c.get("label", pose)
    f = os.path.join(base, c.get("file", ""))
    if not os.path.isfile(f) or os.path.getsize(f) < png_min:
        problems.append("%s: PNG missing or too small (%s)" % (label, c.get("file")))
    if pose == "crt" and "glyphHeightPx" in c:
        if c["glyphHeightPx"] < glyph_min:
            problems.append("%s: glyph %.1f px < %g px" % (label, c["glyphHeightPx"], glyph_min))
        if c.get("contrast", 99.0) < contrast_min:
            problems.append("%s: contrast %.2f < %g" % (label, c["contrast"], contrast_min))
    line = "  %-28s p95 %6.2f ms  p99 %6.2f ms  draws %s" % (label, c.get("frameMsP95", 0), c.get("frameMsP99", 0), c.get("drawCalls", "?"))
    if "glyphHeightPx" in c:
        line += "  glyph %.1f px  contrast %.2f" % (c["glyphHeightPx"], c.get("contrast", 0))
    print(line)

glow = {c.get("pose"): c for c in data.get("captures", []) if str(c.get("pose", "")).startswith("glow") and "skipped" not in c}
if glow:
    mult, delta_min = float(os.environ["GLOW_MULT"]), float(os.environ["GLOW_DELTA"])
    off, idle, mid, full = (glow.get(k, {}).get("cropMean") for k in ("glowoff", "glowidle", "glowmid", "glowfull"))
    if None in (off, idle, full):
        problems.append("rack glow: needs glowoff, glowidle and glowfull captures with a cropMean")
    else:
        d_idle, d_full = idle - off, full - off
        d_mid = (mid - off) if mid is not None else None
        print("  rack glow mean brightness over off:  idle %+.4f  mid %s  full %+.4f  (full/idle %.1f, need >= %g and full >= %g)" % (
            d_idle, ("%+.4f" % d_mid) if d_mid is not None else "n/a", d_full, d_full / max(d_idle, 1e-6), mult, delta_min))
        if d_full < delta_min:
            problems.append("rack glow: full-power brightness is only %+.4f over the glow-off frame (need %g)" % (d_full, delta_min))
        if d_full < mult * max(d_idle, 0.0):
            problems.append("rack glow: full-power gain %+.4f is under %g x the idle gain %+.4f" % (d_full, mult, d_idle))
        if d_idle < -1e-6:
            problems.append("rack glow: idle is darker than the glow-off frame (%+.4f)" % d_idle)
        if d_mid is not None and not (d_idle - 1e-6 <= d_mid <= d_full + 1e-6):
            problems.append("rack glow: brightness is not monotonic idle %+.4f mid %+.4f full %+.4f" % (d_idle, d_mid, d_full))

for pose in required:
    if pose not in seen and pose not in allow_skip and not any(p.startswith(pose + " skipped") for p in problems):
        problems.append("%s: no capture" % pose)

if problems:
    for p in problems:
        print("FAIL " + p)
    sys.exit(1)
print("PASS")
PY
  rc=$?
  [ "$rc" -eq 0 ] || fail=1
done

if [ -n "$BASELINE" ]; then
  echo "== hall runtime budget against $BASELINE"
  BUDGET_P95_MS="$BUDGET_P95_MS" BUDGET_DRAWS="$BUDGET_DRAWS" BUDGET_GALLERY_P99_MS="$BUDGET_GALLERY_P99_MS" \
    BUDGET_AWAY_MS="$BUDGET_AWAY_MS" python3 -I - "$BASELINE" "$DIR" <<'PY'
import glob, json, os, sys

base_dir, new_dir = sys.argv[1], sys.argv[2]
lim = {k: float(os.environ[k]) for k in ("BUDGET_P95_MS", "BUDGET_DRAWS", "BUDGET_GALLERY_P99_MS", "BUDGET_AWAY_MS")}

def load(d):
    out = {}
    for j in sorted(glob.glob(os.path.join(d, "**", "captures.json"), recursive=True)):
        size = os.path.basename(os.path.dirname(j))
        data = json.load(open(j))
        for c in data.get("captures", []):
            if "skipped" not in c:
                out[(size, c.get("pose"))] = c
    return out

base, new = load(base_dir), load(new_dir)
sizes = sorted({s for (s, _p) in new})
problems = []
if not sizes:
    problems.append("no captures to grade in %s" % new_dir)
for size in sizes:
    for pose in ("gallery", "away"):
        for name, d in (("baseline", base), ("new", new)):
            if (size, pose) not in d:
                problems.append("%s %s: no %s capture" % (size, pose, name))
    if any(p.startswith(size) for p in problems):
        continue
    bg, ng = base[(size, "gallery")], new[(size, "gallery")]
    ba, na = base[(size, "away")], new[(size, "away")]
    d95 = ng.get("frameMsP95", 0) - bg.get("frameMsP95", 0)
    dda = ng.get("drawCalls", -1) - bg.get("drawCalls", -1)
    dav = na.get("frameMsP95", 0) - ba.get("frameMsP95", 0)
    print("  %s gallery p95 %+.2f ms (limit %g)  draws %+d (limit %g)  p99 %.2f ms (limit %g)  away p95 %+.2f ms (limit %g)" % (
        size, d95, lim["BUDGET_P95_MS"], dda, lim["BUDGET_DRAWS"], ng.get("frameMsP99", 0), lim["BUDGET_GALLERY_P99_MS"], dav, lim["BUDGET_AWAY_MS"]))
    if ng.get("drawCalls", -1) < 0 or bg.get("drawCalls", -1) < 0:
        problems.append("%s: draw-call counter unavailable, the draw budget cannot be graded" % size)
    elif dda > lim["BUDGET_DRAWS"]:
        problems.append("%s: draw calls rose by %d > %g" % (size, dda, lim["BUDGET_DRAWS"]))
    if d95 > lim["BUDGET_P95_MS"]:
        problems.append("%s: gallery p95 rose by %.2f ms > %g" % (size, d95, lim["BUDGET_P95_MS"]))
    if ng.get("frameMsP99", 0) > lim["BUDGET_GALLERY_P99_MS"]:
        problems.append("%s: gallery p99 %.2f ms > %g" % (size, ng.get("frameMsP99", 0), lim["BUDGET_GALLERY_P99_MS"]))
    if dav > lim["BUDGET_AWAY_MS"]:
        problems.append("%s: out-of-view p95 rose by %.2f ms > %g" % (size, dav, lim["BUDGET_AWAY_MS"]))
if problems:
    for p in problems:
        print("FAIL " + p)
    sys.exit(1)
print("PASS")
PY
  [ "$?" -eq 0 ] || fail=1
fi

[ "$fail" -eq 0 ] && echo "ALL PASS" || echo "FAILED"
exit "$fail"
