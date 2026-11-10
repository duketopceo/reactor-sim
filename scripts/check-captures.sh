#!/usr/bin/env bash
# Grades a fetched capture run (scripts/capture-playtest.sh, or a local -capture run) against fixed thresholds.
#   bash scripts/check-captures.sh <dir> [--allow-skip pose,pose]
# <dir> holds <WxH>/captures.json and the PNGs next to it. The thresholds live here, not in the build, so the build cannot grade
# itself. Exit 0: every check passed. Exit 1: a check failed (each failure is named). Exit 2: bad usage or nothing to grade.
set -uo pipefail

# ---- fixed thresholds ----
GLYPH_MIN_PX=10        # CRT body text: projected glyph height at the captured size (R11)
CONTRAST_MIN=4.5       # CRT text vs its background, measured on the rendered pixels (WCAG AA body text)
PNG_MIN_BYTES=20000    # a real 1600x900 frame is far above this; a blank or failed capture is not
REQUIRED_POSES="seat stand crt tooltip rejected drag checklist pause tablet clipboard menu"

DIR=""
ALLOW_SKIP=""
while [ $# -gt 0 ]; do
  case "$1" in
    --allow-skip) [ $# -ge 2 ] || { echo "--allow-skip needs a value" >&2; exit 2; }; ALLOW_SKIP="$2"; shift 2 ;;
    -h|--help) sed -n '2,6p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    -*) echo "unknown option: $1" >&2; exit 2 ;;
    *) [ -z "$DIR" ] || { echo "one directory only" >&2; exit 2; }; DIR="$1"; shift ;;
  esac
done
[ -n "$DIR" ] && [ -d "$DIR" ] || { echo "usage: check-captures.sh <dir> [--allow-skip pose,pose]" >&2; exit 2; }

mapfile -t jsons < <(find "$DIR" -maxdepth 3 -name captures.json | LC_ALL=C sort)
[ "${#jsons[@]}" -gt 0 ] || { echo "no captures.json under $DIR" >&2; exit 2; }

fail=0
for j in "${jsons[@]}"; do
  echo "== $j"
  GLYPH_MIN_PX="$GLYPH_MIN_PX" CONTRAST_MIN="$CONTRAST_MIN" PNG_MIN_BYTES="$PNG_MIN_BYTES" \
    REQUIRED_POSES="$REQUIRED_POSES" ALLOW_SKIP="$ALLOW_SKIP" python3 -I - "$j" <<'PY'
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

[ "$fail" -eq 0 ] && echo "ALL PASS" || echo "FAILED"
exit "$fail"
