# Export Blender scenes -> Unity-ready FBX (run headless or GUI inside Blender)
# Usage: blender -b --python Blender/scripts/export_unity.py [optional -- <blend-name>]
# Exports ControlRoom, PanelKits, PlantEquipment to Blender/exports/*.fbx
# Axis: -Z Forward / Y Up (Unity native), meters 1:1 (BTD1/BTD2).
import bpy
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scrub_fbx_paths import scrub

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
BLEND_DIR = os.path.dirname(SCRIPT_DIR)
EXPORT_DIR = os.path.join(BLEND_DIR, "exports")
os.makedirs(EXPORT_DIR, exist_ok=True)

BLENDS = ["ControlRoom", "PanelKits", "PlantEquipment", "PlantOverview"]
REACTOR_BLENDS = ["ReactorCore", "VesselInternals", "RotatingEquipment"]
BLENDS += REACTOR_BLENDS

# Optional CLI filter: -- ControlRoom PanelKits
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
if args:
    unknown = set(args) - set(BLENDS)
    if unknown:
        raise ValueError("Unknown asset names: " + ", ".join(sorted(unknown)))
    BLENDS = args

for name in BLENDS:
    blend_path = os.path.join(BLEND_DIR, name + ".blend")
    if not os.path.exists(blend_path):
        raise FileNotFoundError(blend_path)
    bpy.ops.wm.open_mainfile(filepath=blend_path)
    out = os.path.join(EXPORT_DIR, name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=out,
        use_selection=False,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        object_types={"EMPTY", "MESH"} if name in REACTOR_BLENDS else {"EMPTY", "MESH", "LIGHT", "CAMERA"},
        mesh_smooth_type="FACE",
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        use_custom_props=True,
        use_triangles=True,
        bake_anim=False,
    )
    scrub(out)
    print("EXPORTED", out)
print("EXPORT_ALL_DONE")
