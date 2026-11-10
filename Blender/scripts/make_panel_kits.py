# ReactorSim Blender — instrument kit gallery (U5; was the K01-K18 blockout of U11)
# Regenerable: blender -b --python-exit-code 1 --python Blender/scripts/make_panel_kits.py
# Builds every kit from lib_kits side by side on one wall (all facing +Y, which is the operator side), with:
#   - K01 three times, its needle posed at 0, 0.5 and 1.0 of range exactly as ControlRoom3DBinder poses it
#   - one out-of-service example (K01 with the K23 tag, no INT_/BIND_ name)
#   - cameras CAM_Seat_A1 (front), CAM_Stand_Center (oblique), CAM_Top (top-down) for render_room.py
# then self-checks each kit's footprint against design/control-room/kits.json (KIT_OK / KIT_FAIL) and gallery overlap,
# raising on any failure before PanelKits.blend is written.
import bpy
import math
import os
import sys
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lib_common import reset_scene, save_blend
import lib_kits
from lib_kits import FOOTPRINTS, build_kit, check_footprint, local_bbox, rotate_needle

GAP = 0.12          # clear gap between neighbours in the gallery
MAX_ROW_W = 3.6     # shelf-pack width

# (kit id, ctl id, pivot name, label, out_of_service, needle fraction or None, new row first)
GALLERY = [
    ("K01", "K01_lo", "BIND_GAUGE_FuelTempC_0_1400", "FUEL TEMP 0.0", False, 0.0, False),
    ("K01", "K01_mid", "BIND_GAUGE_FuelTempC_0_1400", "FUEL TEMP 0.5", False, 0.5, False),
    ("K01", "K01_hi", "BIND_GAUGE_FuelTempC_0_1400", "FUEL TEMP 1.0", False, 1.0, False),
    ("K01", "K01_oos", None, "SG2 LEVEL", True, None, False),
    ("K01L", "K01L_a", "BIND_GAUGE_CorePowerMWt_0_200", "CORE POWER MWT", False, None, False),
    ("K02", "K02_a", "BIND_BAR_BankInsertion_0_0_1", "BANK A", False, None, False),
    ("K03", "K03_a", "BIND_GAUGE_TotalReactivityPcm_-2500_1000", "REACTIVITY PCM", False, None, False),
    ("K04", "K04_a", "BIND_GAUGE_CorePowerMWt_0_200", "POWER MWT", False, None, False),
    ("K05", "K05_a", "BIND_LAMP_SCRAM", "SCRAM", False, None, False),
    ("K06", "K06_a", "INT_BTN_Scram_Core_1", "SCRAM", False, None, False),
    ("K07", "K07_a", "INT_DIAL_RodBankTarget_0_0_1", "BANK A", False, None, False),
    ("K08", "K08_a", "INT_DIAL_BorateRate_Boron_0_5", "BORATE", False, None, False),
    ("K09", "K09_a", "INT_DIAL_AssistTier_Assistant_0_3", "ASSIST", False, None, False),
    ("K10", "K10_a", "INT_DIAL_SteamDemand_Turbine_0_1.2", "THROTTLE", False, None, False),
    ("K11", "K11_a", "INT_BTN_ReliefValve_PORV_1", "PORV", False, None, False),
    ("K12", "K12_a", "INT_DIAL_PrimaryFlow_RCP_0_1.2", "RCP FLOW", False, None, False),
    ("K13", "K13_a", "CRT_A1_Core", "CORE PAGE", False, None, False),
    ("K16", "K16_a", None, "PRIMARY", False, None, False),
    ("K17", "K17_a", "BIND_KLAXON_Beacon_L", "KLAXON", False, None, False),
    ("K19", "K19_a", "INT_BTN_ResetScram_Core_0", "RESET", False, None, False),
    ("K20", "K20_a", "BIND_LAMP_Pause", "PAUSED", False, None, False),
    ("K21", "K21_a", None, "REACTOR AND RODS", False, None, False),
    ("K22", "K22_a", None, "PRIMARY LOOP", False, None, False),
    ("K23", "K23_a", None, None, False, None, False),
    ("K14", "K14_a", None, None, False, None, True),
    ("K15", "K15_a", None, None, False, None, False),
    ("K18", "K18_a", None, None, False, None, False),
]


def look_at(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()


def make_camera(name, location, target=None, ortho_scale=None):
    data = bpy.data.cameras.new(name)
    data.clip_start = 0.05
    data.clip_end = 200.0
    data.lens = 24.0
    if ortho_scale is not None:
        data.type = "ORTHO"
        data.ortho_scale = ortho_scale
    cam = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = location
    if target is not None:
        look_at(cam, target)
    else:
        cam.rotation_euler = (0.0, 0.0, 0.0)  # straight down -Z
    return cam


def build():
    reset_scene()
    roots = []
    for kid, cid, piv, label, oos, frac, _ in GALLERY:
        roots.append((build_kit(kid, cid, pivot_name=piv, label=label, out_of_service=oos), frac))

    # 1. footprint self-check, measured before any needle is posed
    failures = []
    for root, _ in roots:
        kid = root["kit_id"]
        if kid not in FOOTPRINTS:
            continue  # furniture (K14/K15/K18) has no footprint contract
        ok, w, h, d, exp = check_footprint(root)
        if ok:
            print("KIT_OK %s %.4f %.4f %s" % (kid, w, h, root.name))
        else:
            msg = "KIT_FAIL %s expected %.4fx%.4f got %.4fx%.4f %s" % (kid, exp["face_w"], exp["face_h"], w, h, root.name)
            print(msg)
            failures.append(msg)

    # 2. shelf-pack the gallery (viewer-right = world -X for a viewer on +Y, so u grows toward -X)
    items = []
    for (root, _), row in zip(roots, GALLERY):
        lo, hi = local_bbox(root, include_aux=True)
        items.append((root, hi.x - lo.x, hi.z - lo.z, (lo.x + hi.x) / 2.0, (lo.z + hi.z) / 2.0, row[6]))
    placed = []  # (root, u_center, drop_center, w, h, cx, cz)
    u, top, row_h, widest = 0.0, 0.0, 0.0, 0.0
    for root, w, h, cx, cz, new_row in items:
        if (u > 0.0 and u + w > MAX_ROW_W) or (new_row and u > 0.0):
            top += row_h + GAP
            u, row_h = 0.0, 0.0
        placed.append((root, u + w / 2.0, top + h / 2.0, w, h, cx, cz))
        u += w + GAP
        row_h = max(row_h, h)
        widest = max(widest, u - GAP)
    total_h = top + row_h
    z0 = total_h + 0.3
    for root, uc, drop, w, h, cx, cz in placed:
        root.location = (-uc - cx, 0.0, z0 - drop - cz)

    # 3. no overlap, by measured bounds including label plates
    boxes = []
    bpy.context.view_layer.update()
    for root, *_ in placed:
        lo, hi = local_bbox(root, include_aux=True)
        t = root.location
        boxes.append((root.name, t.x + lo.x, t.x + hi.x, t.z + lo.z, t.z + hi.z))
    for i in range(len(boxes)):
        for j in range(i + 1, len(boxes)):
            a, b = boxes[i], boxes[j]
            if a[1] < b[2] and b[1] < a[2] and a[3] < b[4] and b[3] < a[4]:
                msg = "GALLERY_OVERLAP %s %s" % (a[0], b[0])
                print(msg)
                failures.append(msg)

    if failures:
        raise RuntimeError("panel kit gallery failed: %d problem(s): %s" % (len(failures), "; ".join(failures)))

    # 4. pose the three K01 needles the way the binder does
    for root, frac in roots:
        if frac is not None:
            rotate_needle(root, frac)

    # 5. cameras for render_room.py --blend Blender/PanelKits.blend --prefix kits
    cx = -widest / 2.0
    cz = z0 - total_h / 2.0
    dist = 1.05 * max(widest / 1.5, total_h * (16.0 / 9.0) / 1.5) + 0.4
    seat = make_camera("CAM_Seat_A1", (cx, dist, cz), target=(cx, 0.0, cz))
    make_camera("CAM_Stand_Center", (cx + 0.55 * dist, 0.95 * dist, cz + 0.25 * dist), target=(cx, 0.0, cz))
    make_camera("CAM_Top", (cx, 0.15, z0 + 3.0), ortho_scale=widest * 1.05 + 0.2)
    bpy.context.scene.camera = seat

    out = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "PanelKits.blend")
    save_blend(out)
    print("PANELKITS_SAVED", out)
    print("PANELKITS_OBJECTS", len(bpy.data.objects), "COLLECTIONS", len(bpy.data.collections))


if __name__ == "__main__":
    build()
