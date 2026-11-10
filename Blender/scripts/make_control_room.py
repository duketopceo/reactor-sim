# ReactorSim — main control room generator (U6; replaces the hall-scale blockout)
# Regenerable: blender -b --python-exit-code 1 --python Blender/scripts/make_control_room.py
# Builds Blender/ControlRoom.blend from design/control-room/{layout.json,inventory.csv,kits.json}:
#   - shell (floor, four walls, ceiling, lit fixtures + lights, door) and COL_* collision boxes
#   - the horseshoe: per panel a console body (sloped bench + vertical face, a flat facet tangent to the arc)
#   - every inventory row placed with lib_kits.build_kit at its slot, root CTL_<row id>, pivot named from the row
#     (room_layout.plan_row: INT_/BIND_/CRT_; needs_core_model rows and needs_mapping controls out of service)
#   - signposting: K21 section signs, K22 panel title plates, bench mimic stripes, floor pads and bench lines
#   - chairs at SEAT_A1/SEAT_A2, anchors (SPAWN_A1, SEAT_*, STAND_*) and cameras for render_room.py
# Deterministic: same inputs, same object names and transforms. Raises on a row with no slot, an unknown kit or a
# slot outside its panel. Ends with ROOM_OK rows=<n> oos=<n> ints=<n> gauges=<n>.
# Layout maths and pivot naming live in room_layout.py (pure Python; `python3 -B Blender/scripts/room_layout.py`).
import bpy
import math
import os
import sys
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lib_common import reset_scene, get_or_create_collection, add_box, add_cylinder, add_empty, add_mesh, make_mat, save_blend
import lib_kits
from lib_kits import build_kit
import room_layout as RL


def mark_collision(obj):
    """COL_* boxes are collision proxies: wire in the viewport, never rendered."""
    obj.display_type = "WIRE"
    obj.hide_render = True
    return obj


def look_at(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()


def make_camera(name, parent, pos, target=None, fov=None, ortho_scale=None):
    data = bpy.data.cameras.new(name)
    data.clip_start = 0.05
    data.clip_end = 200.0
    if ortho_scale is not None:
        data.type = "ORTHO"
        data.ortho_scale = ortho_scale
        data.sensor_fit = "VERTICAL"   # the scale spans the room depth; the 16:9 frame then covers its width too
    else:
        data.lens_unit = "FOV"
        data.sensor_fit = "HORIZONTAL"
        data.angle = math.radians(fov)
    cam = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(cam)
    cam.parent = parent
    cam.location = pos
    if target is not None:
        look_at(cam, target)
    else:
        cam.rotation_euler = (0.0, 0.0, 0.0)  # straight down -Z
    return cam


def build():
    layout, kits, rows = RL.load_layout(), RL.load_kits(), RL.load_inventory()
    plans = {r["id"]: RL.plan_row(r, kits) for r in rows}
    placements = RL.place_rows(layout, kits, rows)   # raises on a missing slot, unknown kit/panel, or a cell outside its panel
    fits = RL.label_fit(layout, kits, rows, placements)   # per-row engraved cap height (KTD8) and plate fit
    panels = RL.build_panels(layout)
    bg = RL.bench_geometry(layout)

    reset_scene()
    M = lib_kits.mats()
    room_cfg = layout["room"]
    W, D, H, T = room_cfg["width"], room_cfg["depth"], room_cfg["ceiling"], room_cfg["wall_thickness"]
    arc_cx, arc_cy = layout["arc"]["center"]

    m_light = make_mat("M_LightPanel", (0.90, 0.90, 0.85), emission_color=(1.0, 0.97, 0.90), emission_strength=4.0)
    m_line = make_mat("M_FloorLine", (0.85, 0.65, 0.0), roughness=0.6, emission_color=(0.85, 0.65, 0.0), emission_strength=0.3)
    # bench tops step darker and warmer than the panel faces so the two read apart (room material contrast, PR5)
    m_bench_top = make_mat("M_BenchTop", (0.30, 0.26, 0.21), metallic=0.05, roughness=0.70)
    m_wall_stripe = make_mat("M_WallStripe", (0.62, 0.45, 0.12), roughness=0.60)
    m_pad_neutral = make_mat("M_PadNeutral", (0.45, 0.45, 0.47), roughness=0.6)
    m_mimic = {}
    for sec in layout["sections"]:
        rgb = RL.hex_to_linear(sec["mimic_colour"])
        m_mimic[sec["id"]] = make_mat("M_Mimic_" + sec["id"], rgb, roughness=0.5, emission_color=rgb, emission_strength=0.4)

    col_room = get_or_create_collection("Room")
    col_board = get_or_create_collection("Board")
    col_sign = get_or_create_collection("Signage")
    col_furn = get_or_create_collection("Furniture")
    col_anchor = get_or_create_collection("Anchors")
    col_col = get_or_create_collection("Collision")

    root = bpy.data.objects.new("ControlRoom", None)
    bpy.context.scene.collection.objects.link(root)

    # ---- shell ----------------------------------------------------------------------------------------------
    # Interior is x 0..W, y 0..D, z 0..H; walls, floor and ceiling sit outside it.
    add_box("Floor", (W / 2, D / 2, -0.05), (W + 2 * T, D + 2 * T, 0.10), M["M_FloorDark"], col_room, parent=root)
    add_box("Ceiling", (W / 2, D / 2, H + 0.05), (W + 2 * T, D + 2 * T, 0.10), M["M_PanelSlate"], col_room, parent=root)["ceiling"] = 1
    walls = [("South", (W / 2, -T / 2, H / 2), (W + 2 * T, T, H)),
             ("North", (W / 2, D + T / 2, H / 2), (W + 2 * T, T, H)),
             ("West", (-T / 2, D / 2, H / 2), (T, D, H)),
             ("East", (W + T / 2, D / 2, H / 2), (T, D, H))]
    for n, loc, dim in walls:
        add_box("Wall_" + n, loc, dim, M["M_PanelSlate"], col_room, parent=root)
    mark_collision(add_box("COL_Floor", (W / 2, D / 2, -0.25), (W + 2 * T, D + 2 * T, 0.50), None, col_col, parent=root))
    for n, loc, dim in walls:
        mark_collision(add_box("COL_Wall_" + n, loc, dim, None, col_col, parent=root))

    # ceiling fixtures: emissive panels plus point lights that export to Unity
    fixtures = [(x, y) for y in (2.5, 6.5) for x in (2.5, 6.0, 9.5)]
    for i, (fx, fy) in enumerate(fixtures):
        add_box("Fixture_%d" % i, (fx, fy, H - 0.02), (1.6, 0.5, 0.04), m_light, col_room, parent=root)["ceiling"] = 1
        ld = bpy.data.lights.new("RoomLight_%d" % i, "POINT")
        ld.energy = 350.0
        ld.color = (1.0, 0.96, 0.88)
        lo = bpy.data.objects.new("RoomLight_%d" % i, ld)
        col_room.objects.link(lo)
        lo.parent = root
        lo.location = (fx, fy, H - 0.4)
        lo["ceiling"] = 1

    # door in the south wall (the horseshoe opens toward it): frame, leaf, push bar
    dx = W / 2
    add_box("Door_FrameL", (dx - 0.55, 0.04, 1.05), (0.10, 0.08, 2.10), M["M_BrushedSteel"], col_room, parent=root)
    add_box("Door_FrameR", (dx + 0.55, 0.04, 1.05), (0.10, 0.08, 2.10), M["M_BrushedSteel"], col_room, parent=root)
    add_box("Door_FrameTop", (dx, 0.04, 2.15), (1.20, 0.08, 0.10), M["M_BrushedSteel"], col_room, parent=root)
    add_box("Door_Leaf", (dx, 0.045, 1.05), (1.00, 0.05, 2.10), M["M_ConsoleBeige"], col_room, parent=root)
    add_box("Door_PushBar", (dx, 0.085, 1.0), (0.70, 0.03, 0.04), M["M_BrushedSteel"], col_room, parent=root)

    # ---- south-wall detail beside the door (the viewing window takes the other side): a primary-loop mimic panel with
    # a title plate, a cable conduit run with drops, and a chair-rail stripe. Original layout, plain boxes and cylinders.
    wx0, wx1 = 0.6, dx - 0.9
    wall_y = 0.0
    add_box("WallStripe", ((wx0 + wx1) / 2, wall_y + 0.012, 1.05), (wx1 - wx0, 0.024, 0.07), m_wall_stripe, col_room, parent=root)
    add_box("WallBase", ((wx0 + wx1) / 2, wall_y + 0.02, 0.06), (wx1 - wx0, 0.04, 0.12), M["M_DarkRubber"], col_room, parent=root)
    mx, mz, mw, mh = (wx0 + wx1) / 2, 1.75, 1.7, 0.95
    add_box("WallMimic_Plate", (mx, wall_y + 0.025, mz), (mw, 0.05, mh), M["M_PanelSlate"], col_sign, parent=root, bevel=0.01)
    add_box("WallMimic_Frame_T", (mx, wall_y + 0.055, mz + mh / 2), (mw + 0.06, 0.03, 0.04), M["M_BrushedSteel"], col_sign, parent=root)
    add_box("WallMimic_Frame_B", (mx, wall_y + 0.055, mz - mh / 2), (mw + 0.06, 0.03, 0.04), M["M_BrushedSteel"], col_sign, parent=root)
    add_box("WallMimic_Frame_L", (mx - mw / 2, wall_y + 0.055, mz), (0.04, 0.03, mh), M["M_BrushedSteel"], col_sign, parent=root)
    add_box("WallMimic_Frame_R", (mx + mw / 2, wall_y + 0.055, mz), (0.04, 0.03, mh), M["M_BrushedSteel"], col_sign, parent=root)
    sec_ids = [sec["id"] for sec in layout["sections"]]
    mat_a = m_mimic[sec_ids[0]]
    mat_b = m_mimic[sec_ids[min(1, len(sec_ids) - 1)]]
    # primary loop: vessel block, hot leg, steam generator block, cold leg back (boxes on the plate face)
    fy = wall_y + 0.056
    add_box("WallMimic_Vessel", (mx - 0.55, fy, mz - 0.05), (0.26, 0.012, 0.42), mat_a, col_sign, parent=root)
    add_box("WallMimic_SG", (mx + 0.50, fy, mz + 0.05), (0.26, 0.012, 0.52), mat_b, col_sign, parent=root)
    add_box("WallMimic_HotLeg", (mx - 0.02, fy, mz + 0.22), (0.80, 0.012, 0.035), mat_a, col_sign, parent=root)
    add_box("WallMimic_ColdLeg", (mx - 0.02, fy, mz - 0.26), (0.80, 0.012, 0.035), mat_b, col_sign, parent=root)
    add_box("WallMimic_Pump", (mx - 0.02, fy, mz - 0.26), (0.10, 0.014, 0.10), M["M_BrushedSteel"], col_sign, parent=root)
    for i in range(4):
        add_box("WallMimic_Lamp_%d" % i, (mx - 0.60 + i * 0.12, fy, mz + mh / 2 - 0.10), (0.05, 0.012, 0.05),
                m_pad_neutral if i % 2 else m_mimic[sec_ids[0]], col_sign, parent=root)
    sign = build_kit("K22", "wallsign_mimic", label="PRIMARY LOOP", parent=root, mount="vertical",
                     location=(mx, wall_y + 0.06, mz + mh / 2 + 0.22))
    sign.name = "WALLSIGN_PrimaryLoop"
    # conduit: a run near the ceiling and two drops, with clamps
    cz = H - 0.35
    add_cylinder("Conduit_Run", ((wx0 + wx1) / 2, wall_y + 0.05, cz), 0.035, wx1 - wx0, M["M_BrushedSteel"], col_room, parent=root,
                 vertices=16, rotation=(0.0, math.radians(90.0), 0.0))
    for i, cxp in enumerate((wx0 + 0.25, wx1 - 0.25)):
        add_cylinder("Conduit_Drop_%d" % i, (cxp, wall_y + 0.05, cz / 2 + 0.06), 0.035, cz - 0.12, M["M_BrushedSteel"], col_room,
                     parent=root, vertices=16)
        add_box("Conduit_Elbow_%d" % i, (cxp, wall_y + 0.05, cz), (0.09, 0.09, 0.09), M["M_BrushedSteel"], col_room, parent=root)
    for i in range(5):
        add_box("Conduit_Clamp_%d" % i, (wx0 + 0.9 + i * ((wx1 - wx0 - 1.8) / 4), wall_y + 0.05, cz), (0.05, 0.09, 0.09),
                M["M_DarkRubber"], col_room, parent=root)

    # ---- horseshoe: console bodies, collision, mimic stripes, floor lines -------------------------------------
    frames = {}
    profile = RL.body_profile(layout)
    top = RL.panel_top(layout)
    for p in panels:
        pe = add_empty("Panel_" + p.name, (p.cx, p.cy, 0.0), col_board, root)
        pe.rotation_euler = (0.0, 0.0, p.yaw)
        frames[p.key] = pe
        console = add_mesh("Console_" + p.name, RL.prism_verts(profile, p.width), RL.prism_faces(len(profile)),
                           M["M_ConsoleBeige"], col_board, parent=pe)
        console.data.materials.append(m_bench_top)
        # prism side face i joins profile points i and i+1 (faces 0 and 1 are the end caps): the sloped bench is the edge
        # from the front lip (hd, front_h) to the panel foot (0, back_h), profile points 3 -> 4.
        console.data.polygons[2 + 3].material_index = 1
        # collision: the vertical body, and the bench as a box under its front-edge height
        mark_collision(add_box("COL_Panel_" + p.name, (0, -RL.BACK_DEPTH / 2, top / 2), (p.width, RL.BACK_DEPTH, top), None, col_col, parent=pe))
        mark_collision(add_box("COL_Bench_" + p.name, (0, bg["hd"] / 2, bg["front_h"] / 2), (p.width, bg["hd"], bg["front_h"]), None, col_col, parent=pe))
        # mimic stripe along the bench front apron, in the section colour
        add_box("Mimic_" + p.name, (0, bg["hd"] + 0.003, bg["front_h"] - 0.07), (p.width - 0.02, 0.006, 0.04),
                m_mimic[p.section], col_sign, parent=pe)
        # yellow "do not cross while operating" line 0.35 m in front of the bench edge
        add_box("BenchLine_" + p.name, (0, bg["hd"] + 0.35, 0.003), (p.width, 0.05, 0.006), m_line, col_sign, parent=pe)

    # ---- inventory rows ---------------------------------------------------------------------------------------
    for r in rows:
        pl, plan = placements[r["id"]], plans[r["id"]]
        kit_root = build_kit(r["kit"], r["id"], pivot_name=plan["pivot"], label=RL.display_label(r), parent=frames[pl.panel.key],
                             location=(pl.lx, pl.ly, pl.lz), rotation=(math.radians(pl.pitch_deg), 0.0, 0.0),
                             out_of_service=plan["oos"], mount=pl.mount, label_size=RL.label_text_size(fits[r["id"]]))
        kit_root["short_label"] = r["short_label"]
        kit_root["label"] = r["label"]   # the full name stays the tooltip text; the plate engraves short_label

    # ---- signposting ------------------------------------------------------------------------------------------
    for p in panels:   # K22 title plate on each panel header (title_plate_z is the plate's lower edge)
        plate_h = kits["kits"]["K22"]["face_h"]
        pr = build_kit("K22", "title_" + p.name, label=p.title, parent=frames[p.key],
                       location=(0.0, 0.0, layout["vertical"]["title_plate_z"] + plate_h / 2), mount="vertical")
        pr.name = "TITLE_" + p.name
    sign_h = kits["kits"]["K21"]["face_h"]
    for sid, text, x, y, z, yaw in RL.sign_specs(layout):   # K21 section signs hung over each section
        sr = build_kit("K21", "sign_" + sid, label=text, parent=root, location=(x, y, z), rotation=(0.0, 0.0, yaw), mount="overhead")
        sr.name = "SIGN_" + sid
        lx, ly = math.cos(yaw), math.sin(yaw)   # local +X (the sign's left) in world xy
        for sx in (-0.5, 0.5):
            rod_top = H
            rod_bot = z + sign_h / 2
            add_box("SignHanger_%s_%d" % (sid, 0 if sx < 0 else 1), (x + sx * lx, y + sx * ly, (rod_top + rod_bot) / 2),
                    (0.02, 0.02, rod_top - rod_bot), M["M_BrushedSteel"], col_sign, parent=root)

    # ---- anchors, floor pads, chairs --------------------------------------------------------------------------
    sec_colour = {s["id"]: m_mimic[s["id"]] for s in layout["sections"]}
    for name, x, y, heading, eye, kind in RL.anchor_specs(layout):
        a = add_empty(name, (x, y, 0.0), col_anchor, root)
        a.rotation_euler = (0.0, 0.0, -math.radians(heading))   # +Y (Unity forward) = the way the operator faces
        a.empty_display_type = "ARROWS"
        a.empty_display_size = 0.3
        a["eye_height"] = eye
        a["heading_deg"] = heading
        if name.startswith("STAND_"):
            sec = name[len("STAND_"):]
            mat = sec_colour.get(sec, m_pad_neutral)
            add_cylinder("Pad_" + name, (x, y, 0.005), 0.22, 0.01, mat, col_sign, parent=root, vertices=32)
    for sid, anchor in (("A1", "SEAT_A1"), ("A2", "SEAT_A2")):
        op = layout["operating_points"][anchor]
        yaw = math.pi - math.radians(op["at_deg"])   # the chair's operator faces -Y: its +Y is away from the board
        ch = build_kit("K15", "chair_" + sid, parent=root, location=(op["pos"][0], op["pos"][1], 0.0), rotation=(0.0, 0.0, yaw))
        ch.name = "CHAIR_" + sid

    # ---- cameras ----------------------------------------------------------------------------------------------
    cams = {}
    for c in RL.camera_specs(layout):
        cams[c["name"]] = make_camera(c["name"], root, c["pos"], target=c["target"], fov=c["fov"], ortho_scale=c["ortho_scale"])
    bpy.context.scene.camera = cams["CAM_Seat_A1"]

    bpy.context.view_layer.update()
    self_check(rows, plans, layout, panels)

    out = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "ControlRoom.blend")
    save_blend(out)
    print("CONTROLROOM_SAVED", out)
    print("CONTROLROOM_OBJECTS", len(bpy.data.objects), "COLLECTIONS", len(bpy.data.collections))


def self_check(rows, plans, layout, panels):
    problems = []
    roots = {o.name: o for o in bpy.data.objects if o.type == "EMPTY" and o.name.startswith("CTL_") and o.get("ctl_id")}
    want = {"CTL_" + r["id"] for r in rows}
    if len(roots) != len(rows):
        problems.append("CTL_ roots %d != inventory rows %d" % (len(roots), len(rows)))
    for n in sorted(want - set(roots)):
        problems.append("missing " + n)
    for n in sorted(set(roots) - want):
        problems.append("unexpected " + n)
    oos = ints = gauges = 0
    for r in rows:
        root = roots.get("CTL_" + r["id"])
        if root is None:
            continue
        plan = plans[r["id"]]
        names = [c.name for c in root.children_recursive]
        has_int = any(n.startswith("INT_") for n in names)
        has_tag = any(n.endswith("_OosTag") for n in names)
        if plan["oos"]:
            oos += 1
            if not has_tag:
                problems.append("%s: out of service but no OOS tag" % r["id"])
            if has_int or any(n.startswith(("BIND_", "CRT_")) for n in names):
                problems.append("%s: out of service but carries a bound pivot" % r["id"])
        else:
            if has_tag:
                problems.append("%s: in service but tagged out of service" % r["id"])
            mover = root.get("mover_obj", "")
            if not (mover == plan["pivot"] or (mover.startswith(plan["pivot"] + ".") and mover[len(plan["pivot"]) + 1:].isdigit())):
                problems.append("%s: pivot %r != expected %r" % (r["id"], mover, plan["pivot"]))
            if plan["pivot"].startswith("INT_"):
                ints += 1
            elif plan["pivot"].startswith(("BIND_GAUGE_", "BIND_BAR_")):
                gauges += 1
        if r["readiness"] == "needs_core_model" and (not plan["oos"] or has_int):
            problems.append("%s: needs_core_model must be tagged with no INT_ pivot" % r["id"])
    n_col = sum(1 for o in bpy.data.objects if o.name.startswith("COL_"))
    if n_col != 5 + 2 * len(panels):
        problems.append("collision meshes %d != %d" % (n_col, 5 + 2 * len(panels)))
    for need in ["SPAWN_A1", "SEAT_A1", "SEAT_A2", "STAND_Center", "CAM_Seat_A1", "CAM_Stand_Center", "CAM_Top"] + \
                ["STAND_" + s["id"] for s in layout["sections"]]:
        if need not in bpy.data.objects:
            problems.append("missing " + need)
    if problems:
        for p in problems:
            print("ROOM_FAIL", p)
        raise RuntimeError("control room self-check failed: %d problem(s); first: %s" % (len(problems), problems[0]))
    print("ROOM_OK rows=%d oos=%d ints=%d gauges=%d" % (len(rows), oos, ints, gauges))


if __name__ == "__main__":
    build()
