"""Inspect saved source assets; --write-manifest updates the import sidecar."""
import csv
import io
import math
import os
import re
import sys

import bpy


NAMES = ("ReactorCore", "VesselInternals", "RotatingEquipment")
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
FIELDS = ("rs_id", "family", "status", "rs_bind", "priority", "lod", "collider",
          "material", "pivot", "est_tris", "license", "author", "asset", "object_path")
REQUIRED = ("rs_id", "rs_bind", "rs_status", "rs_priority", "rs_lod",
            "rs_license", "rs_author", "rs_version")
BINDINGS = {"plant.core", "plant.primary.pumpA", "plant.turb.speed",
            *("plant.core.rods.bank" + letter for letter in "ABCD")}


def object_path(obj):
    names = [obj.name]
    while obj.parent is not None:
        obj = obj.parent
        names.append(obj.name)
    return "/".join(reversed(names))


def inspect():
    rows, ids, total, core_triangles = [], set(), 0, 0
    for name in NAMES:
        source = os.path.join(ROOT, "Blender", name + ".blend")
        bpy.ops.wm.open_mainfile(filepath=source)
        tris, rodlets = 0, 0
        for obj in sorted(bpy.data.objects, key=object_path):
            assert obj.type in ("EMPTY", "MESH"), "Unexpected export type: " + obj.name
            assert all(key in obj for key in REQUIRED), "Missing metadata: " + obj.name
            assert obj["rs_id"] not in ids, "Duplicate identity: " + obj.name
            ids.add(obj["rs_id"])
            assert not re.search(r"\.\d{3}$", obj.name), "Automatic suffix: " + obj.name
            assert obj["rs_bind"] in BINDINGS or not obj["rs_bind"], "Unknown binding"
            assert all(math.isfinite(v) for v in (*obj.location, *obj.scale)), "Non-finite transform"
            assert all(abs(v - 1) < 1e-5 for v in obj.scale), "Unapplied scale: " + obj.name
            count = 0
            if obj.type == "MESH":
                obj.data.calc_loop_triangles()
                count = len(obj.data.loop_triangles)
                tris += count
                if obj.name.endswith("_LOD0"):
                    rodlets += obj.get("rodlet_count", 0)
            rows.append(dict(zip(FIELDS, (
                obj["rs_id"], obj.name.split("_")[1], obj["rs_status"], obj["rs_bind"],
                obj["rs_priority"], obj["rs_lod"], "none",
                "|".join(m.name for m in obj.data.materials) if obj.type == "MESH" else "",
                obj.parent.name if obj.parent else "", count,
                obj["rs_license"], obj["rs_author"], name, object_path(obj),
            ))))
        if name == "ReactorCore":
            assert rodlets >= 100, "Hero lattice lacks its rodlets"
            for letter in "ABCD":
                assert bpy.data.objects.get("ROD_BANK_" + letter + "_PIV"), "Missing bank " + letter
            hero = sum(int(row["est_tris"]) for row in rows
                       if row["asset"] == name and row["object_path"].startswith("RS_VES_FuelAssembly_00_PIV/")
                       and row["lod"] == 0)
            assert hero <= 6000, "Hero assembly exceeds 6k LOD0 triangles"
        if name in ("ReactorCore", "VesselInternals"):
            core_triangles += tris
        total += tris
        print("ASSET_COUNTS", name, "objects", len(bpy.data.objects), "resident_tris", tris)
    print("REACTOR_ASSETS_TOTAL", total)
    assert core_triangles <= 40000, "Core and internals exceed resident triangle allocation"
    for filename in ("export_unity.py", "sync-blender-to-unity.sh", "sync-blender-to-unity.ps1"):
        with open(os.path.join(ROOT, "Blender", "scripts", filename), encoding="utf-8") as handle:
            text = handle.read()
        assert all(name in text for name in NAMES), "Export/sync inventory drift: " + filename
    buffer = io.StringIO(newline="")
    writer = csv.DictWriter(buffer, fieldnames=FIELDS, lineterminator="\n")
    writer.writeheader()
    writer.writerows(rows)
    manifest = os.path.join(ROOT, "assets", "manifest.csv")
    if "--write-manifest" in sys.argv:
        os.makedirs(os.path.dirname(manifest), exist_ok=True)
        with open(manifest, "w", encoding="utf-8", newline="") as handle:
            handle.write(buffer.getvalue())
    else:
        with open(manifest, encoding="utf-8", newline="") as handle:
            assert handle.read() == buffer.getvalue(), "Stale manifest; regenerate it"
    print("REACTOR_ASSETS_OK", len(rows), "manifest rows")


if __name__ == "__main__":
    inspect()
