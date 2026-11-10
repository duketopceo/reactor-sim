# ReactorSim control room (U6): layout math and pivot naming, pure Python (no bpy import).
#
# make_control_room.py (Blender) builds geometry from the numbers this module computes. Keeping the maths here lets
# it run anywhere:   python3 -B Blender/scripts/room_layout.py     (self-check over design/control-room/*)
#
# PANEL FRAME. Every panel is a flat facet tangent to the board arc at the panel's mid angle t (layout.json: angle
# measured from +y toward +x, point = centre + r*(sin t, cos t)). The panel frame is a Blender empty at the face
# centre on the floor with yaw = pi - t, so in panel-local coordinates
#     +X = the operator's LEFT along the face,  +Y = toward the operator (the face normal),  +Z = up
# which is exactly the kit frame of lib_kits (kit X across the face, +Y toward the operator, Z up the face): a kit
# parented to its panel frame needs only a pitch about X (bench slope, annunciator tilt).
# Slot columns grow toward the operator's RIGHT, so panel-local x = -(u - width/2) where u is measured from the
# left edge of the panel.
import csv
import json
import math
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(os.path.dirname(HERE))
DESIGN = os.path.join(REPO_ROOT, "design", "control-room")

EPS = 1e-9
BACK_DEPTH = 0.40        # how far a console body extends behind the vertical face
PANEL_TOP_EXTRA = 0.10   # body top above the title plate's lower edge
TOE_KICK = 0.06
SIGN_HANG_RADIUS_BACK = 0.25   # signs hang this far in front of the vertical face radius
SIGN_PLATE_DROP = 0.0


def load_json(name):
    with open(os.path.join(DESIGN, name), "r", encoding="utf-8") as f:
        return json.load(f)


def load_layout():
    return load_json("layout.json")


def load_kits():
    return load_json("kits.json")


def load_inventory(path=None):
    path = path or os.path.join(DESIGN, "inventory.csv")
    with open(path, "r", encoding="utf-8-sig", newline="") as f:
        rows = list(csv.DictReader(f))
    if not rows:
        raise ValueError("inventory.csv has no rows")
    seen = set()
    for r in rows:
        if r["id"] in seen:
            raise ValueError("duplicate inventory id " + r["id"])
        seen.add(r["id"])
    return rows


def display_label(row):
    """Engraved text: the row's terse `short_label` (the long `label` stays the full name for tooltips)."""
    return row["short_label"].strip()


def r6(x):
    v = round(x, 6)
    return 0.0 if v == 0 else v


# ---------------------------------------------------------------------------------------------------------------
# Pivot names (mirror of ReactorSim.Core.Tests/InventoryTests.ControlPivotName / GaugePivotName)

def binding_parts(row):
    b = row["binding"]
    return b[b.index(":") + 1:].split(":")


def split_range(rng):
    i = rng.find("..")
    if i > 0:
        return rng[:i], rng[i + 2:]
    return None


def control_pivot_name(row):
    p = binding_parts(row)
    kind = p[0]
    target = p[1] if len(p) > 1 else ""
    rng = row["range_or_positions"]
    mm = split_range(rng)
    if mm:
        s = "INT_DIAL_" + kind + "_"
        if target:
            s += target + "_"
        return s + mm[0] + "_" + mm[1]
    s = "INT_BTN_" + kind + "_"
    if target:
        s += target + "_"
    return s + rng


def gauge_pivot_name(row, kit_grammar):
    """BIND_BAR for K02 (edgewise) whether or not the binding has an index (the index is optional in the parser);
    BIND_GAUGE otherwise. The InventoryTests rule (index present -> BAR) agrees on every row that has an index."""
    p = binding_parts(row)
    mm = split_range(row["range_or_positions"])
    if mm is None:
        raise ValueError("%s: gauge row needs a min..max range, got %r" % (row["id"], row["range_or_positions"]))
    if kit_grammar == "BIND_BAR":
        mid = "_".join(p[:2]) if len(p) > 1 else p[0]
        return "BIND_BAR_%s_%s_%s" % (mid, mm[0], mm[1])
    if len(p) > 1:
        raise ValueError("%s: indexed gauge binding on a %s kit; only BIND_BAR kits take an index" % (row["id"], kit_grammar))
    return "BIND_GAUGE_%s_%s_%s" % (p[0], mm[0], mm[1])


def is_number(s):
    try:
        v = float(s)
    except ValueError:
        return False
    return not (math.isnan(v) or math.isinf(v))


def validate_pivot_name(name):
    """Mirror of PivotNameParser (INT_) and GaugeSources.TryResolve parsing (BIND_GAUGE_/BIND_BAR_). Returns an error
    string or None. Intent-kind and selector existence are checked separately (see selfcheck, which reads the C#)."""
    if name.startswith("INT_"):
        tok = name.split("_")
        if len(tok) < 4:
            return "too few fields"
        if tok[1] == "DIAL":
            if len(tok) < 5:
                return "dial needs <min>_<max>"
            if not (is_number(tok[-2]) and is_number(tok[-1])):
                return "dial range is not numeric"
            if not float(tok[-2]) < float(tok[-1]):
                return "dial min must be below max"
        elif tok[1] == "BTN":
            if not is_number(tok[-1]):
                return "button value is not numeric"
        else:
            return "style must be DIAL or BTN"
        return None
    if name.startswith("BIND_GAUGE_") or name.startswith("BIND_BAR_"):
        rest = name[len("BIND_GAUGE_"):] if name.startswith("BIND_GAUGE_") else name[len("BIND_BAR_"):]
        tok = rest.split("_")
        if len(tok) < 3:
            return "too few fields"
        if not (is_number(tok[-2]) and is_number(tok[-1]) and float(tok[-2]) < float(tok[-1])):
            return "range is not numeric / min >= max"
        return None
    if name.startswith("BIND_LAMP_"):
        return None if re.match(r"^BIND_LAMP_[A-Za-z0-9_]+$", name) else "bad lamp name"
    if name.startswith("CRT_"):
        return None if re.match(r"^CRT_[A-Za-z0-9_]+$", name) else "bad CRT name"
    return "unknown pivot grammar"


def plan_row(row, kits):
    """-> dict(pivot=<name or None>, oos=<bool>, why=<str>). KTD6: needs_core_model is placed out of service with no
    INT_/BIND_/CRT_ pivot. needs_mapping controls are out of service too: InventoryTests guarantees the factory does
    not support them yet, so an INT_ pivot would be inert and misleading. needs_mapping indicators, lamps and pages
    keep their BIND_/CRT_ names so U8's mapping lights them without touching the room."""
    kit = kits["kits"].get(row["kit"])
    if kit is None:
        raise KeyError("%s: unknown kit %r" % (row["id"], row["kit"]))
    ready = row["readiness"]
    if ready == "needs_core_model":
        return {"pivot": None, "oos": True, "why": "needs_core_model"}
    if ready not in ("bound", "needs_mapping"):
        raise ValueError("%s: unknown readiness %r" % (row["id"], ready))
    role = row["role"]
    if role == "control" and ready == "needs_mapping":
        return {"pivot": None, "oos": True, "why": "needs_mapping control: factory rejects it"}
    prefix = row["binding"].split(":", 1)[0]
    if role == "control" and prefix == "intent":
        name = control_pivot_name(row)
    elif role == "indicator" and prefix == "gauge":
        name = gauge_pivot_name(row, kit["pivot"])
    elif prefix == "lamp" and role in ("alarm", "indicator"):
        name = "BIND_LAMP_" + binding_parts(row)[0]
    elif role == "page" and prefix == "crt":
        name = binding_parts(row)[0]
    else:
        raise ValueError("%s: cannot make a pivot for role=%s binding=%s readiness=%s" % (row["id"], role, row["binding"], ready))
    err = validate_pivot_name(name)
    if err:
        raise ValueError("%s: pivot %s invalid: %s" % (row["id"], name, err))
    grammar = kit["pivot"]
    if not name.startswith(grammar + "_"):
        raise ValueError("%s: pivot %s does not fit kit %s grammar %s_" % (row["id"], name, row["kit"], grammar))
    return {"pivot": name, "oos": False, "why": "bound" if ready == "bound" else "needs_mapping"}


# ---------------------------------------------------------------------------------------------------------------
# Panels

class Panel(object):
    def __init__(self, section, pid, title, width, start_deg, mid_deg, cx, cy, yaw, colour):
        self.section, self.id, self.title, self.width = section, pid, title, width
        self.start_deg, self.mid_deg, self.cx, self.cy, self.yaw, self.colour = start_deg, mid_deg, cx, cy, yaw, colour

    @property
    def key(self):
        return (self.section, self.id)

    @property
    def name(self):
        return "%s_%s" % (self.section, self.id)


def build_panels(layout):
    arc = layout["arc"]
    R = arc["radius"]
    cx0, cy0 = arc["center"]
    panels = []
    for sec in layout["sections"]:
        a = sec["start_deg"]
        for p in sec["panels"]:
            da = math.degrees(p["width"] / R)
            mid = a + da / 2.0
            t = math.radians(mid)
            panels.append(Panel(sec["id"], p["id"], p["title"], p["width"], a, mid,
                                r6(cx0 + R * math.sin(t)), r6(cy0 + R * math.cos(t)),
                                r6(math.pi - t), sec["mimic_colour"]))
            a += da
    return panels


def panel_to_world(panel, lx, ly, lz):
    c, s = math.cos(panel.yaw), math.sin(panel.yaw)
    return (panel.cx + c * lx - s * ly, panel.cy + s * lx + c * ly, lz)


def bench_geometry(layout):
    b = layout["bench"]
    sl = math.radians(b["slope_deg"])
    return {
        "front_h": b["front_edge_height"],
        "slope": sl,
        "depth": b["depth"],
        "hd": b["depth"] * math.cos(sl),               # horizontal depth in front of the face
        "back_h": b["front_edge_height"] + b["depth"] * math.sin(sl),
    }


def panel_top(layout):
    return layout["vertical"]["title_plate_z"] + PANEL_TOP_EXTRA


def body_profile(layout):
    """Console body profile in the (y, z) plane of the panel frame, counter-clockwise seen from +x."""
    bg = bench_geometry(layout)
    hd, fh, bh, top = bg["hd"], bg["front_h"], bg["back_h"], panel_top(layout)
    return [(hd - TOE_KICK, 0.0), (hd - TOE_KICK, 0.10), (hd, 0.10), (hd, fh), (0.0, bh),
            (0.0, top), (-BACK_DEPTH, top), (-BACK_DEPTH, 0.0)]


def prism_faces(n):
    """Faces for a prism over an n-point profile. Vertices: 0..n-1 at x=+w/2 (A), n..2n-1 at x=-w/2 (B)."""
    faces = [list(range(n)), [n + i for i in reversed(range(n))]]
    for i in range(n):
        j = (i + 1) % n
        faces.append([i, n + i, n + j, j])
    return faces


def prism_verts(profile, width):
    hw = width / 2.0
    return [(hw, y, z) for (y, z) in profile] + [(-hw, y, z) for (y, z) in profile]


def signed_volume(verts, faces):
    """Positive when faces wind counter-clockwise seen from outside (outward normals). Fan-triangulated."""
    vol = 0.0
    for f in faces:
        a = verts[f[0]]
        for k in range(1, len(f) - 1):
            b, c = verts[f[k]], verts[f[k + 1]]
            vol += (a[0] * (b[1] * c[2] - b[2] * c[1]) - a[1] * (b[0] * c[2] - b[2] * c[0]) + a[2] * (b[0] * c[1] - b[1] * c[0])) / 6.0
    return vol


# ---------------------------------------------------------------------------------------------------------------
# Slot grid -> panel-local poses

def parse_slot(row):
    p = row["slot"].split(":")
    if len(p) != 3 or p[0] not in ("bench", "vert"):
        raise ValueError("%s: slot %r is not <bench|vert>:<row>:<col>" % (row["id"], row["slot"]))
    try:
        r, c = int(p[1]), int(p[2])
    except ValueError:
        raise ValueError("%s: bad slot numbers %r" % (row["id"], row["slot"]))
    if r < 0 or c < 0:
        raise ValueError("%s: negative slot number %r" % (row["id"], row["slot"]))
    return p[0], r, c


def cell_size(layout, kits, kit_id):
    k = kits["kits"][kit_id]
    g = layout["grid"]
    if kit_id in g["no_label_plate_kits"]:
        return k["face_w"], k["face_h"]
    lp = layout["label_plate"]
    return max(k["face_w"], g["min_plate_width"]), k["face_h"] + lp["height"] + lp["gap"]


class Placement(object):
    """Panel-local pose of one inventory row's kit, plus its cell extents for the fit checks."""
    def __init__(self, row, panel, surface, lx, ly, lz, pitch_deg, u0, u1, v0, v1):
        self.row, self.panel, self.surface = row, panel, surface
        self.lx, self.ly, self.lz, self.pitch_deg = lx, ly, lz, pitch_deg
        self.u0, self.u1, self.v0, self.v1 = u0, u1, v0, v1   # u from the left edge; v = z (vert) or slope distance s (bench)

    @property
    def mount(self):
        return "bench" if self.surface == "bench" else "vertical"

    def world(self):
        return panel_to_world(self.panel, self.lx, self.ly, self.lz)


def place_rows(layout, kits, rows):
    """-> {row id: Placement}. Raises ValueError/KeyError on a row with no slot, an unknown kit or panel, or a cell
    that does not fit inside its panel (same accounting as InventoryTests.EveryPanel_FitsItsGrid)."""
    panels = {p.key: p for p in build_panels(layout)}
    gap = kits["spacing"]["min_gap"]
    margin = layout["grid"]["edge_margin"]
    ann_rows = int(layout["vertical"]["annunciator_rows"])
    ann_top = layout["vertical"]["usable_to"]
    ann_floor = layout["vertical"]["annunciator_from"]
    inst_floor = layout["vertical"]["usable_from"]
    tilt = layout["vertical"]["annunciator_tilt_deg"]
    bg = bench_geometry(layout)

    groups = {}
    order = []
    for r in rows:
        if not r["slot"]:
            raise ValueError("%s: no slot" % r["id"])
        if r["kit"] not in kits["kits"]:
            raise KeyError("%s: unknown kit %r" % (r["id"], r["kit"]))
        key = (r["section"], r["panel"])
        if key not in panels:
            raise KeyError("%s: panel %s/%s is not in layout.json" % (r["id"], r["section"], r["panel"]))
        surf, ro, co = parse_slot(r)
        gk = (key, surf)
        if gk not in groups:
            groups[gk] = {}
            order.append(gk)
        groups[gk].setdefault(ro, []).append((co, r))

    out = {}
    for gk in order:
        key, surf = gk
        panel = panels[key]
        pw = panel.width
        lines = groups[gk]
        if surf == "bench":
            cursor = 0.0
            first = True
        else:
            ann_cursor = ann_top
            inst_cursor = ann_floor
            inst_first = True
        for ro in sorted(lines):
            cells = sorted(lines[ro], key=lambda t: t[0])
            for i, (co, r) in enumerate(cells):
                if co != i:
                    raise ValueError("%s/%s %s row %d: columns are not contiguous from 0 (%s)" % (key[0], key[1], surf, ro, r["id"]))
            sizes = [cell_size(layout, kits, r["kit"]) for _, r in cells]
            row_w = 0.0
            for i, (w, h) in enumerate(sizes):
                row_w += w
                if i > 0:
                    touching = cells[i][1]["kit"] == "K05" and cells[i - 1][1]["kit"] == "K05"
                    row_w += 0.0 if touching else gap
            row_h = max(h for _, h in sizes)
            if row_w + 2 * margin > pw + EPS:
                raise ValueError("%s/%s %s row %d: needs %.3f m, panel is %.3f m" % (key[0], key[1], surf, ro, row_w + 2 * margin, pw))
            if surf == "bench":
                if not first:
                    cursor += gap
                first = False
                v_lo, v_hi = cursor, cursor + row_h
                cursor = v_hi
                if v_hi > bg["depth"] + EPS:
                    raise ValueError("%s/%s bench row %d ends %.3f m along the slope, bench is %.3f m deep" % (key[0], key[1], ro, v_hi, bg["depth"]))
            elif ro < ann_rows:
                for _, r in cells:
                    if r["kit"] != "K05":
                        raise ValueError("%s: non-tile kit %s in the annunciator band" % (r["id"], r["kit"]))
                v_hi, v_lo = ann_cursor, ann_cursor - row_h
                ann_cursor = v_lo
                if v_lo < ann_floor - EPS:
                    raise ValueError("%s/%s annunciator row %d bottom %.3f is below the band (%.3f)" % (key[0], key[1], ro, v_lo, ann_floor))
            else:
                for _, r in cells:
                    if r["kit"] == "K05":
                        raise ValueError("%s: annunciator tile below the annunciator band" % r["id"])
                if not inst_first:
                    inst_cursor -= gap
                inst_first = False
                v_hi, v_lo = inst_cursor, inst_cursor - row_h
                inst_cursor = v_lo
                if v_lo < inst_floor - EPS:
                    raise ValueError("%s/%s instrument row %d bottom %.3f is below %.3f" % (key[0], key[1], ro, v_lo, inst_floor))
            u = (pw - row_w) / 2.0
            for i, ((co, r), (w, h)) in enumerate(zip(cells, sizes)):
                if i > 0:
                    touching = cells[i][1]["kit"] == "K05" and cells[i - 1][1]["kit"] == "K05"
                    u += 0.0 if touching else gap
                u0, u1 = u, u + w
                uc = u0 + w / 2.0
                kit = kits["kits"][r["kit"]]
                fh = kit["face_h"]
                lx = -(uc - pw / 2.0)
                if surf == "bench":
                    s = v_lo + fh / 2.0                      # near-aligned: label plate sits behind (up-slope of) the kit
                    ly = bg["hd"] - s * math.cos(bg["slope"])
                    lz = bg["front_h"] + s * math.sin(bg["slope"])
                    pitch = 90.0 - layout["bench"]["slope_deg"]
                    cell = (v_lo, v_hi)
                else:
                    lz = v_hi - fh / 2.0                     # top-aligned: label plate hangs below the kit
                    ly = 0.0
                    pitch = -tilt if ro < ann_rows else 0.0
                    cell = (v_lo, v_hi)
                out[r["id"]] = Placement(r, panel, surf, r6(lx), r6(ly), r6(lz), pitch, r6(u0), r6(u1), r6(cell[0]), r6(cell[1]))
                u += w
    missing = [r["id"] for r in rows if r["id"] not in out]
    if missing:
        raise ValueError("rows without a placement: %s" % missing)
    return out


# ---------------------------------------------------------------------------------------------------------------
# Anchors, signs, cameras

def anchor_specs(layout):
    """[(name, x, y, heading_deg, eye_height, kind)]. Heading is the direction the operator FACES (toward the board),
    measured from +y toward +x. layout.json's facing_deg is the same heading (it equals at_deg: the operator looks
    outward along the radius to the board); the selfcheck asserts the two agree."""
    out = []
    ops = layout["operating_points"]
    names = ["SPAWN_A1", "SEAT_A1", "SEAT_A2"] + ["STAND_" + s["id"] for s in layout["sections"]] + ["STAND_Center"]
    for n in names:
        op = ops[n]
        out.append((n, op["pos"][0], op["pos"][1], op["at_deg"], op["eye_height"], n.split("_")[0]))
    return out


def sign_specs(layout):
    """[(section id, text, x, y, z, yaw)] for the K21 section signs, hung just in front of the board."""
    arc = layout["arc"]
    R = arc["radius"]
    cx0, cy0 = arc["center"]
    out = []
    for sec in layout["sections"]:
        sg = sec["sign"]
        t = math.radians(sg["centre_deg"])
        r = R - SIGN_HANG_RADIUS_BACK
        out.append((sec["id"], sg["text"], r6(cx0 + r * math.sin(t)), r6(cy0 + r * math.cos(t)), sg["height"], r6(math.pi - t)))
    return out


def camera_specs(layout):
    """[dict(name, pos, target|None, fov, ortho_scale|None)]. Targets are world points the camera looks at."""
    cams = layout["cameras"]
    out = []
    c = cams["CAM_Seat_A1"]
    la = c["look_at"]
    out.append({"name": "CAM_Seat_A1", "pos": (c["pos"][0], c["pos"][1], c["height"]), "target": (la["x"], la["y"], la["z"]),
                "fov": c["fov_deg"], "ortho_scale": None})
    c = cams["CAM_Stand_Center"]
    heads = sorted(c["headings_deg"])
    if len(heads) != 3 or heads[1] != 0:
        raise ValueError("CAM_Stand_Center headings_deg must be [-a, 0, +a], got %r" % heads)
    for h, suffix in zip(heads, ("_L", "", "_R")):
        th = math.radians(h)
        pos = (c["pos"][0], c["pos"][1], c["height"])
        out.append({"name": "CAM_Stand_Center" + suffix, "pos": pos,
                    "target": (pos[0] + math.sin(th), pos[1] + math.cos(th), pos[2]), "fov": c["fov_deg"], "ortho_scale": None})
    c = cams["CAM_Top"]
    out.append({"name": "CAM_Top", "pos": (c["pos"][0], c["pos"][1], c["height"]), "target": None, "fov": None,
                "ortho_scale": c["ortho_scale"]})
    return out


def hex_to_linear(h):
    """'#rrggbb' sRGB -> linear (r, g, b) for Principled base colour."""
    h = h.lstrip("#")
    out = []
    for i in (0, 2, 4):
        v = int(h[i:i + 2], 16) / 255.0
        out.append(v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4)
    return tuple(out)


# ---------------------------------------------------------------------------------------------------------------
# Engraved-label fit (KTD8): a label must subtend >= checks.min_text_deg from the row's operating point.
#
# Text height H is the cap height. Required H = distance(eye at STAND_<section>, label centre) * tan(min_text_deg).
# Glyph advance is modelled as GLYPH_ADV_PER_H * H per character (average for the generator's font).
# Plate labels are one line on a plate max(kit face_w, grid.min_plate_width) wide (minus PLATE_TEXT_PAD), and at most
# plate height - PLATE_TEXT_VPAD tall. Annunciator tiles (K05) carry their short_label on the lens: one line, or two
# lines split at a space (wrap_tile_label), each line <= TILE_TEXT_W wide with line cap <= TILE_LINE_MAX_H.
CAP_PER_SIZE = 0.72        # Blender FONT curve: cap height / size
GLYPH_ADV_PER_H = 0.6      # average glyph advance as a fraction of the cap height
PLATE_TEXT_PAD = 0.008     # lib_kits._label_plate: max_width = plate width - 0.008
PLATE_TEXT_VPAD = 0.006    # lib_kits._label_plate: max_height = plate height - 0.006
TILE_TEXT_W = 0.064        # lib_kits._k05: lens text max_width
TILE_LINE_MAX_H = 0.018    # two lines in the 0.044 m lens text box
TILE_ONE_LINE_MAX = 5      # a tile label of this many chars or fewer stays on one line


def wrap_tile_label(text, limit=TILE_ONE_LINE_MAX):
    """Tile label split (lib_kits uses this function): at the space nearest the middle when longer than `limit`."""
    if len(text) <= limit or " " not in text:
        return text
    mid = len(text) / 2.0
    cut = min((i for i, c in enumerate(text) if c == " "), key=lambda i: abs(i - mid))
    return text[:cut] + "\n" + text[cut + 1:]


def label_fit(layout, kits, rows, placements):
    """-> {row id: dict(kind, h_req, h, chars, n_max, width, avail, fits, height_limited)}.
    h is the engraved cap height: the required height, capped by what the plate/lens can carry. `height_limited` is
    True when that cap is below h_req (the KTD8 angle is out of reach for the plate height); `fits` is the width test
    at h (so make_text does not shrink the text)."""
    t = math.tan(math.radians(layout["checks"]["min_text_deg"]))
    lp = layout["label_plate"]
    bg = bench_geometry(layout)
    no_plate = set(layout["grid"]["no_label_plate_kits"])
    out = {}
    for r in rows:
        pl = placements[r["id"]]
        kit = kits["kits"][r["kit"]]
        op = layout["operating_points"]["STAND_" + r["section"]]
        text = display_label(r)
        if r["kit"] in no_plate:
            wx, wy, wz = pl.world()
            h_cap = TILE_LINE_MAX_H
            avail = TILE_TEXT_W
            chars = max(len(x) for x in wrap_tile_label(text).split("\n"))
            kind = "tile"
        else:
            off = kit["face_h"] / 2.0 + lp["gap"] + lp["height"] / 2.0
            if pl.surface == "bench":
                wx, wy, wz = panel_to_world(pl.panel, pl.lx, pl.ly - off * math.cos(bg["slope"]), pl.lz + off * math.sin(bg["slope"]))
            else:
                wx, wy, wz = panel_to_world(pl.panel, pl.lx, pl.ly, pl.lz - off)
            h_cap = lp["height"] - PLATE_TEXT_VPAD
            avail = max(kit["face_w"], layout["grid"]["min_plate_width"]) - PLATE_TEXT_PAD
            chars = len(text)
            kind = "plate"
        d = math.sqrt((wx - op["pos"][0]) ** 2 + (wy - op["pos"][1]) ** 2 + (wz - op["eye_height"]) ** 2)
        h_req = d * t
        h = min(h_req, h_cap)
        width = chars * GLYPH_ADV_PER_H * h
        out[r["id"]] = {"kind": kind, "h_req": h_req, "h": h, "chars": chars, "width": width, "avail": avail,
                        "n_max": int(avail / (GLYPH_ADV_PER_H * h) + 1e-9), "fits": width <= avail + 1e-9,
                        "height_limited": h_req > h_cap + 1e-9}
    return out


def label_text_size(fit):
    """Blender FONT size that gives cap height fit['h'] (what make_control_room passes to build_kit)."""
    return fit["h"] / CAP_PER_SIZE


# ---------------------------------------------------------------------------------------------------------------
# Self-check

def _csharp_names():
    """Optional: intent kinds, known CRT pages and gauge selectors read from the repo's C# (skipped if absent)."""
    kinds, pages, selectors = None, None, None
    for dirpath, _dirs, files in os.walk(os.path.join(REPO_ROOT, "ReactorSim.Core")):
        for fn in files:
            if fn.endswith(".cs"):
                txt = open(os.path.join(dirpath, fn), encoding="utf-8").read()
                m = re.search(r"enum\s+IntentKind\s*\{([^}]*)\}", txt)
                if m:
                    kinds = set(re.findall(r"[A-Za-z_]\w*", re.sub(r"//[^\n]*", "", m.group(1))))
    v3 = os.path.join(REPO_ROOT, "ReactorSim.Unity", "Assets", "Scripts", "View3D")
    try:
        txt = open(os.path.join(v3, "CrtPages.cs"), encoding="utf-8").read()
        m = re.search(r"KnownPages\s*=\s*\{([^}]*)\}", txt)
        pages = set(re.findall(r'"(CRT_\w+)"', m.group(1))) if m else None
        txt = open(os.path.join(v3, "GaugeSources.cs"), encoding="utf-8").read()
        selectors = set(re.findall(r'\bE\("(\w+)"', txt)) | set(re.findall(r'new SelectorEntry\("(\w+)"', txt))
    except IOError:
        pass
    return kinds, pages, selectors


def selfcheck(verbose=True):
    layout, kits, rows = load_layout(), load_kits(), load_inventory()
    errors = []
    placements = place_rows(layout, kits, rows)
    panels = build_panels(layout)
    pmap = {p.key: p for p in panels}
    W, D = layout["room"]["width"], layout["room"]["depth"]

    # 1. every row maps to a position inside its panel (place_rows raised otherwise); re-assert independently
    margin = layout["grid"]["edge_margin"]
    for rid, pl in placements.items():
        pw = pl.panel.width
        if pl.u0 < margin - 1e-6 or pl.u1 > pw - margin + 1e-6:
            errors.append("%s: cell u %.3f..%.3f outside panel width %.3f" % (rid, pl.u0, pl.u1, pw))
        wx, wy, wz = pl.world()
        if not (0 < wx < W and 0 < wy < D and 0 < wz < layout["room"]["ceiling"]):
            errors.append("%s: world position %.2f,%.2f,%.2f outside the shell" % (rid, wx, wy, wz))
    # no overlapping cells within a panel surface
    by_surface = {}
    for pl in placements.values():
        by_surface.setdefault((pl.panel.key, pl.surface), []).append(pl)
    for k, pls in by_surface.items():
        for i in range(len(pls)):
            for j in range(i + 1, len(pls)):
                a, b = pls[i], pls[j]
                if a.u0 < b.u1 - 1e-6 and b.u0 < a.u1 - 1e-6 and a.v0 < b.v1 - 1e-6 and b.v0 < a.v1 - 1e-6:
                    errors.append("%s overlaps %s on %s" % (a.row["id"], b.row["id"], k))

    # 1b. engraved labels: every row has a short_label that fits its plate (tile lens) at the engraved text height,
    # so make_text never shrinks it; labels carry no type note (those live in type_note)
    fits = label_fit(layout, kits, rows, placements)
    for r in rows:
        if not r["short_label"].strip():
            errors.append("%s: empty short_label" % r["id"])
            continue
        if "[type note" in r["label"] or "[type note" in r["short_label"]:
            errors.append("%s: type note inside label (use the type_note column)" % r["id"])
        f = fits[r["id"]]
        if not f["fits"]:
            errors.append("%s: short_label %r is %d chars, %s fits %d at %.1f mm cap height (%.0f mm needed for %.1f deg; %.0f of %.0f mm wide)"
                          % (r["id"], r["short_label"], f["chars"], "tile line" if f["kind"] == "tile" else "plate", f["n_max"],
                             f["h"] * 1000, f["h_req"] * 1000, layout["checks"]["min_text_deg"], f["width"] * 1000, f["avail"] * 1000))
    limited = [(rid, f) for rid, f in fits.items() if f["height_limited"]]

    # 2. pivot names valid against the documented grammar (+ the C# when present)
    kinds, pages, selectors = _csharp_names()
    n_int = n_gauge = n_oos = 0
    names = {}
    for r in rows:
        plan = plan_row(r, kits)
        if plan["oos"]:
            n_oos += 1
            if plan["pivot"] is not None:
                errors.append("%s: out of service but has a pivot" % r["id"])
            if r["readiness"] == "needs_core_model" and plan["pivot"] is not None:
                errors.append("%s: needs_core_model must have no pivot" % r["id"])
            continue
        nm = plan["pivot"]
        names.setdefault(nm, []).append(r["id"])
        if nm.startswith("INT_"):
            n_int += 1
            if kinds is not None and nm.split("_")[2].lower() not in {k.lower() for k in kinds}:
                errors.append("%s: %s has unknown intent kind" % (r["id"], nm))
        elif nm.startswith("BIND_GAUGE_") or nm.startswith("BIND_BAR_"):
            n_gauge += 1
            if selectors is not None and r["readiness"] == "bound":
                tok = nm.split("_", 2)[2].split("_")
                body = tok[:-2]
                if nm.startswith("BIND_BAR_") and len(body) >= 2 and body[-1].isdigit():
                    body = body[:-1]
                if "_".join(body) not in selectors:
                    errors.append("%s: bound but %s has no GaugeSources selector" % (r["id"], nm))
        elif nm.startswith("CRT_") and pages is not None and r["readiness"] == "bound" and nm not in pages:
            errors.append("%s: bound but %s is not in CrtPages.KnownPages" % (r["id"], nm))
    dup_crt = [n for n, ids in names.items() if n.startswith("CRT_") and len(ids) > 1]
    if dup_crt:
        errors.append("duplicate CRT pivot names: %s" % dup_crt)

    # 3. body geometry: winding, inside the shell, and the operating points clear of the bench
    prof = body_profile(layout)
    for p in panels:
        verts, faces = prism_verts(prof, p.width), prism_faces(len(prof))
        if signed_volume(verts, faces) <= 0:
            errors.append("%s: body prism winds inward" % p.name)
        for lx in (-p.width / 2.0, p.width / 2.0):
            for ly in (-BACK_DEPTH, bench_geometry(layout)["hd"]):
                x, y, _ = panel_to_world(p, lx, ly, 0)
                if not (0.05 < x < W - 0.05 and 0.05 < y < D - 0.05):
                    errors.append("%s: body corner (%.2f, %.2f) outside the shell" % (p.name, x, y))
    for name, x, y, heading, eye, _k in anchor_specs(layout):
        if not (0 < x < W and 0 < y < D):
            errors.append("%s outside the shell" % name)
    for name, op in layout["operating_points"].items():
        if "facing_deg" in op and name != "STAND_Center":
            if abs((op["facing_deg"] - op["at_deg"] + 180.0) % 360.0 - 180.0) > 0.01:
                errors.append("%s: facing_deg %.3f is not at_deg %.3f (an operator faces the board)" % (name, op["facing_deg"], op["at_deg"]))
    seat = layout["cameras"]["CAM_Seat_A1"]
    sp = layout["operating_points"]["SEAT_A1"]
    look = math.degrees(math.atan2(seat["look_at"]["x"] - seat["pos"][0], seat["look_at"]["y"] - seat["pos"][1]))
    if abs((look - sp["facing_deg"] + 180.0) % 360.0 - 180.0) > 0.5:
        errors.append("SEAT_A1 facing_deg %.2f disagrees with CAM_Seat_A1 look_at heading %.2f" % (sp["facing_deg"], look))

    # 4. kit frame / panel frame orientation: a vert kit at the panel centre faces the arc centre
    for p in panels:
        yx, yy, _ = panel_to_world(p, 0, 1, 0)
        ox, oy = yx - p.cx, yy - p.cy
        tx, ty = layout["arc"]["center"][0] - p.cx, layout["arc"]["center"][1] - p.cy
        n = math.hypot(tx, ty)
        if abs(ox - tx / n) > 1e-4 or abs(oy - ty / n) > 1e-4:
            errors.append("%s: +Y does not point at the arc centre" % p.name)
    cams = camera_specs(layout)
    if len(rows) != len(placements):
        errors.append("placement count != row count")

    if verbose:
        print("rows=%d oos=%d ints=%d gauges=%d panels=%d cameras=%s" % (len(rows), n_oos, n_int, n_gauge, len(panels), [c["name"] for c in cams]))
        if kinds is None:
            print("note: C# intent kinds not found; kind check skipped")
        hs = [f["h"] for f in fits.values()]
        print("labels: %d rows, engraved cap height %.1f-%.1f mm, widest label uses %.0f%% of its plate" %
              (len(fits), min(hs) * 1000, max(hs) * 1000, 100 * max(f["width"] / f["avail"] for f in fits.values())))
        if limited:
            worst = max(limited, key=lambda t: t[1]["h_req"] - t[1]["h"])
            print("note: %d rows need more cap height for %.1f deg than the plate/lens carries (worst %s: %.1f mm needed, %.1f mm engraved)"
                  % (len(limited), layout["checks"]["min_text_deg"], worst[0], worst[1]["h_req"] * 1000, worst[1]["h"] * 1000))
        dups = {n: ids for n, ids in names.items() if len(ids) > 1}
        print("duplicate pivot names (Blender adds .001, parser strips it): %d" % len(dups))
    if errors:
        for e in errors:
            print("SELFCHECK_FAIL", e)
        raise SystemExit(1)
    print("ROOM_LAYOUT_OK")
    return placements


if __name__ == "__main__":
    selfcheck()
