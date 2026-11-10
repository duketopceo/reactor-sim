#!/usr/bin/env python3
"""Roll up design/control-room/inventory.csv.

Prints the tree area > system > subsystem > function > device with counts per
node, then totals by readiness, role, physical_type, kit, phase, section and
section/panel. Writes the same
text to design/control-room/rollup.md, and the runtime subset the game loads for hover tooltips (id, label, short label,
units, range, description, kit, role) to ReactorSim.Unity/Assets/StreamingAssets/inventory-runtime.json.
Exits non-zero on a malformed CSV (missing column, empty category level or description, duplicate id).

Usage: python3 scripts/inventory-rollup.py [inventory.csv] [rollup.md] [inventory-runtime.json]
"""
import csv
import json
import os
import sys
from collections import Counter

COLUMNS = ("id area system subsystem function device role binding range_or_positions units "
           "physical_type kit readiness phase section panel slot label short_label type_note overview paired_indicator "
           "description").split()
LEVELS = ["area", "system", "subsystem", "function", "device"]
ROLES = {"control", "indicator", "alarm", "page"}
READINESS = ["bound", "needs_mapping", "needs_core_model"]
PHASES = ["room", "backend", "electrical", "plumbing"]
SECTIONS = ["reactor", "primary", "safety", "steam", "turbine", "electrical"]


def load(path):
    problems = []
    with open(path, newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        missing = [c for c in COLUMNS if c not in (reader.fieldnames or [])]
        if missing:
            return [], ["missing column(s): " + ", ".join(missing)]
        rows = list(reader)
    seen = {}
    for n, r in enumerate(rows, start=2):
        rid = (r["id"] or "").strip()
        if not rid:
            problems.append("line %d: empty id" % n)
        elif rid in seen:
            problems.append("line %d: duplicate id '%s' (first on line %d)" % (n, rid, seen[rid]))
        else:
            seen[rid] = n
        for lv in LEVELS:
            if not (r[lv] or "").strip():
                problems.append("line %d (%s): empty category level '%s'" % (n, rid, lv))
        if not (r["short_label"] or "").strip():
            problems.append("line %d (%s): empty short_label" % (n, rid))
        if not (r["description"] or "").strip():
            problems.append("line %d (%s): empty description (the hover tooltip needs one)" % (n, rid))
        if "[type note" in (r["label"] or "") or "[type note" in (r["short_label"] or ""):
            problems.append("line %d (%s): type note inside a label (use the type_note column)" % (n, rid))
        if r["role"] not in ROLES:
            problems.append("line %d (%s): bad role '%s'" % (n, rid, r["role"]))
        if r["readiness"] not in READINESS:
            problems.append("line %d (%s): bad readiness '%s'" % (n, rid, r["readiness"]))
        if r["phase"] not in PHASES:
            problems.append("line %d (%s): bad phase '%s'" % (n, rid, r["phase"]))
    return rows, problems


def tree_lines(rows):
    tree = {}
    for r in rows:
        node = tree
        for lv in LEVELS:
            node = node.setdefault(r[lv], {"__n": 0, "__kids": {}})
            node["__n"] += 1
            node = node["__kids"]
    lines = []

    def walk(kids, depth):
        for name in kids:
            lines.append("%s%s (%d)" % ("  " * depth, name, kids[name]["__n"]))
            walk(kids[name]["__kids"], depth + 1)

    walk(tree, 0)
    return lines


def totals(title, counter, order=None):
    lines = ["", title]
    keys = list(order) if order else sorted(counter)
    for k in keys:
        lines.append("  %-28s %d" % (k, counter.get(k, 0)))
    if order:
        for k in sorted(set(counter) - set(order)):
            lines.append("  %-28s %d" % (k, counter[k]))
    return lines


def render(rows):
    out = ["Inventory rollup: %d rows" % len(rows), "", "Tree (area > system > subsystem > function > device)", ""]
    out += tree_lines(rows)
    out += totals("By readiness", Counter(r["readiness"] for r in rows), READINESS)
    out += totals("By role", Counter(r["role"] for r in rows), sorted(ROLES))
    ptype = Counter((r["physical_type"] or "(unassigned)") for r in rows)
    out += totals("By physical_type", ptype)
    out += totals("By kit", Counter((r["kit"] or "(unassigned)") for r in rows))
    out += totals("By phase", Counter(r["phase"] for r in rows), PHASES)
    out += totals("By section", Counter((r["section"] or "(unplaced)") for r in rows), SECTIONS)
    out += totals("By section/panel", Counter((r["section"] or "(unplaced)") + "/" + (r["panel"] or "-") for r in rows))
    return "\n".join(out) + "\n"


def runtime_json(rows):
    """The runtime subset, one row per line so a diff shows per-device changes. Field names match RuntimeRow in
    Assets/Scripts/View3D/ControlTooltipModel.cs (Unity JsonUtility)."""
    keys = [("id", "id"), ("label", "label"), ("shortLabel", "short_label"), ("units", "units"),
            ("range", "range_or_positions"), ("description", "description"), ("kit", "kit"), ("role", "role")]
    lines = []
    for r in rows:
        obj = {k: (r[c] or "").strip() for k, c in keys}
        lines.append("  " + json.dumps(obj, ensure_ascii=True))
    return '{"rows":[\n' + ",\n".join(lines) + "\n]}\n"


def main(argv):
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    src = argv[1] if len(argv) > 1 else os.path.join(root, "design", "control-room", "inventory.csv")
    dst = argv[2] if len(argv) > 2 else os.path.join(root, "design", "control-room", "rollup.md")
    rt = argv[3] if len(argv) > 3 else os.path.join(root, "ReactorSim.Unity", "Assets", "StreamingAssets", "inventory-runtime.json")
    rows, problems = load(src)
    if problems:
        for p in problems:
            print("ERROR: " + p, file=sys.stderr)
        return 1
    text = render(rows)
    sys.stdout.write(text)
    with open(dst, "w", encoding="utf-8", newline="\n") as f:
        f.write("<!-- generated by scripts/inventory-rollup.py; do not edit -->\n\n")
        f.write("```\n" + text + "```\n")
    with open(rt, "w", encoding="utf-8", newline="\n") as f:
        f.write(runtime_json(rows))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
