# ReactorSim Blender — parametrized instrument kits (U5)
#
# One builder per kit id in design/control-room/kits.json. Every builder has the same signature
#   build_kit(kit_id, ctl_id, pivot_name=None, label=None, parent=None, location=(0,0,0),
#             rotation=(0,0,0), out_of_service=False, mount="vertical")
# and returns a root empty named CTL_<ctl_id> that holds the kit geometry, the moving pivot carrying
# `pivot_name` (INT_/BIND_/CRT_ grammar, KTD2; duplicates get Blender's .001 which the parser strips), a K22-style
# label plate (real text geometry) and, when out_of_service, a K23 tag. An out-of-service kit's moving part is
# named OOS_Part_<ctl_id>, never INT_/BIND_/CRT_, so the runtime binds nothing (KTD6).
#
# KIT FRAME (the contract with the runtime binder, see ControlRoom3DBinder.cs):
#   origin   centre of the kit's face on the panel surface
#   X        across the face     Z   up the face     +Y   the face normal, toward the operator (kit stands proud)
# FBX export is axis_forward=-Z / axis_up=Y, i.e. Blender (x, y, z) -> Unity (x, z, y):
#   Blender Y (face normal)  == Unity local Z (forward)   Blender Z (up the face) == Unity local Y
# so the binder's "rotate about local Z" is a rotation about Blender Y. Needles and knob pointers are therefore long
# along Blender Z (perpendicular to the sweep axis) and the pivots carry identity local rotation. Unity's
# positive Euler Z reads clockwise to an operator on the +Y side, which is Blender rotation_euler.y = -angle.
import bpy
import math
import os
import sys
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lib_common import add_box, add_cylinder, add_empty, get_or_create_collection, link_to, ANIM
from lib_materials import build_materials
import room_layout as RL  # pure Python (no bpy): the design data loaders and the tile-label wrap

_CATALOGUE = RL.load_kits()

# kit id -> {"face_w", "face_h", "depth"} in metres (the footprint contract; U4's fit test reads the same json).
FOOTPRINTS = {
    kid: {"face_w": k["face_w"], "face_h": k["face_h"], "depth": k["depth"]}
    for kid, k in _CATALOGUE["kits"].items()
}
_PIVOT_GRAMMAR = {kid: k["pivot"] for kid, k in _CATALOGUE["kits"].items()}
LABEL_PLATE = _CATALOGUE["label_plate"]  # {"height", "gap"}


# layout.json grid.min_plate_width: a label plate is as wide as its kit, but never narrower than this.
# A missing or malformed layout.json fails here, loudly, rather than falling back to a guessed width.
MIN_PLATE_W = float(RL.load_layout()["grid"]["min_plate_width"])

# Label cap height ~0.7 * size, so 0.02 gives ~14 mm numerals (KTD8: 0.9 deg at 0.9 m).
LABEL_TEXT_SIZE = 0.02
LABEL_EXTRUDE = 0.0015
TEXT_ROT = (math.pi / 2, 0.0, math.pi)  # text reads upright and unmirrored for an operator on the +Y side
FOOTPRINT_TOL = 0.002

SELF_LABELED = ("K21", "K22", "K23")   # the kit IS the text/tag: no extra plate
FURNITURE = ("K14", "K15", "K18")      # room furniture: no footprint contract, no plate, no tag
NO_PLATE = ("K05",)                    # annunciator tile: the text is on the lens, no plate (layout.json no_label_plate_kits)

# ---------------------------------------------------------------------------------------------------------------
# Pose contract (KTD3): Blender authors every kit at its REST pose and records the motion envelope as root custom
# props so the Unity binder and the Blender checks read one documented contract. Fractions 0..1 map rest->active in
# ControlRoom3DBinder / the U3 pure pose helper; nothing here animates. Axes are kit-frame (see header): +Y is the
# face normal toward the operator, +Z is up the face, X is across the face.
#   pose_kind      needle      circular gauge: pivot rotates about +Y, sweep pose_sweep_lo..hi degrees
#                  bar         edgewise gauge: pivot slides along +Z by pose_travel_m centred on rest
#                  rotary_face knob/selector/wheel: pivot rotates about +Y by pose_sweep_lo..hi degrees
#                  rotary_across rod-bank lever: pivot pitches about X by pose_sweep_lo..hi degrees
#                  press        pushbutton/plunger/toggle bat: pivot travels -Y by pose_press_m on press
#                  guarded      like press, plus an auxiliary PART_Guard cover that arms before the press
#                  scram_cover  like press, plus an auxiliary PART_ScramCover that lifts before the press
#                  counter     K04 digits: runtime text, pivot carries no moving mesh
#                  lamp/beacon emissive material driven at runtime, no transform motion
#                  screen      CRT: runtime RenderTexture face, no transform motion
#                  static      no live mover
# Auxiliary covers (PART_Guard / PART_ScramCover) are empties parented to the kit root at the cover's hinge edge;
# U3 rotates them open on arm. Their rest pose is identity, same as every other authored pose.
POSE_CONTRACT = {
    "K01":  {"pose_kind": "needle",       "pose_sweep_lo": -135.0, "pose_sweep_hi": 135.0},
    "K01L": {"pose_kind": "needle",       "pose_sweep_lo": -135.0, "pose_sweep_hi": 135.0},
    "K02":  {"pose_kind": "bar"},                                                     # travel_m set per row
    "K03":  {"pose_kind": "needle",       "pose_sweep_lo": -135.0, "pose_sweep_hi": 135.0},
    "K04":  {"pose_kind": "counter"},
    "K05":  {"pose_kind": "lamp"},
    "K06":  {"pose_kind": "scram_cover", "pose_press_m": 0.025, "pose_aux_deg": -100.0},
    "K07":  {"pose_kind": "rotary_across", "pose_sweep_lo": -40.0, "pose_sweep_hi": 40.0},
    "K08":  {"pose_kind": "rotary_face",  "pose_sweep_lo": -150.0, "pose_sweep_hi": 150.0},
    "K09":  {"pose_kind": "rotary_face",  "pose_sweep_lo": -150.0, "pose_sweep_hi": 150.0},
    "K10":  {"pose_kind": "rotary_face",  "pose_sweep_lo": -150.0, "pose_sweep_hi": 150.0},
    "K11":  {"pose_kind": "guarded",      "pose_press_m": 0.025, "pose_aux_deg": -100.0},
    "K12":  {"pose_kind": "rotary_face",  "pose_sweep_lo": -150.0, "pose_sweep_hi": 150.0},
    "K13":  {"pose_kind": "screen"},
    "K16":  {"pose_kind": "static"},
    "K17":  {"pose_kind": "beacon"},
    "K19":  {"pose_kind": "press",        "pose_press_m": 0.025},
    "K20":  {"pose_kind": "lamp"},
    "K21":  {"pose_kind": "static"},
    "K22":  {"pose_kind": "static"},
    "K23":  {"pose_kind": "static"},
}

BUILDERS = {}

# ---------------------------------------------------------------------------------------------------------------
_MATS = None


def mats():
    """Materials are built once per scene; a scene reset frees them, so rebuild when the cache went stale."""
    global _MATS
    try:
        fresh = _MATS is not None and bpy.data.materials.get("M_PanelSlate") is _MATS["M_PanelSlate"]
    except ReferenceError:
        fresh = False
    if not fresh:
        _MATS = build_materials()
    return _MATS


def lerp(a, b, t):
    return a + (b - a) * t


class Ctx:
    def __init__(self, kid, ctl_id, root, mover, col, label, label_size=None):
        self.kid, self.ctl_id, self.root, self.mover, self.col, self.label = kid, ctl_id, root, mover, col, label
        self.label_size = label_size  # FONT size for plate / tile text; None = LABEL_TEXT_SIZE (plate) or 0.012 (tile)
        self.m = mats()
        fp = FOOTPRINTS.get(kid)
        self.fw = fp["face_w"] if fp else 0.0
        self.fh = fp["face_h"] if fp else 0.0
        self.depth = fp["depth"] if fp else 0.0

    def nm(self, n):
        return "%s_%s" % (self.ctl_id, n)

    def box(self, n, center, dims, mat, parent=None, raw=False):
        return add_box(n if raw else self.nm(n), center, dims, mat, self.col, parent or self.root)

    def cyl(self, n, center, radius, length, mat, parent=None, axis="Y", verts=24, raw=False):
        rot = (math.pi / 2, 0, 0) if axis == "Y" else (0, 0, 0)
        return add_cylinder(n if raw else self.nm(n), center, radius, length, mat, self.col,
                            parent or self.root, vertices=verts, rotation=rot)

    def base(self, mat, thickness, n="Base"):
        """Exact-footprint backing plate: face_w x face_h, y 0..thickness."""
        return self.box(n, (0, thickness / 2.0, 0), (self.fw, thickness, self.fh), mat)

    def pivot(self, center):
        """The moving pivot: an empty carrying the binder name, identity local rotation."""
        p = add_empty(self.mover, center, self.col, self.root)
        self.root["mover_obj"] = p.name
        return p

    def aux_pivot(self, n, center):
        """A named auxiliary hinge pivot (PART_Guard / PART_ScramCover): identity rotation, not a binder name."""
        return add_empty(self.nm(n), center, self.col, self.root)

    def set_mover(self, obj):
        """For kits whose NAMED part is the mesh itself (lamp tile, CRT screen, klaxon dome)."""
        self.root["mover_obj"] = obj.name
        return obj

    def ticks(self, parent, center_xz, radius, fracs, y, length, mat, sweep=(-135.0, 135.0), width=0.004):
        cx, cz = center_xz
        for i, f in enumerate(fracs):
            th = math.radians(lerp(sweep[0], sweep[1], f))
            b = self.box("Tick%d" % i, (cx - radius * math.sin(th), y, cz + radius * math.cos(th)),
                         (width, 0.003, length), mat, parent)
            b.rotation_euler = (0, -th, 0)


def make_text(col, name, text, size, mat, parent, location, max_width=None, extrude=LABEL_EXTRUDE, max_height=None):
    """Text as real mesh geometry (no ops): FONT curve -> evaluated mesh. Returns the mesh object, reading toward +Y.
    The text is measured from its vertices and, if wider than max_width or taller than max_height, scaled down
    uniformly (baked into the mesh, so obj.dimensions stays true) until it fits. obj['fit_scale'] records the factor."""
    curve = bpy.data.curves.new(name + "_crv", "FONT")
    curve.body = text
    curve.size = size
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    curve.extrude = extrude
    tmp = bpy.data.objects.new(name + "_tmp", curve)
    bpy.context.scene.collection.objects.link(tmp)
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg))
    bpy.data.objects.remove(tmp)
    bpy.data.curves.remove(curve)
    if len(mesh.vertices) == 0:
        raise RuntimeError("make_text produced no geometry for %r" % text)
    xs = [v.co.x for v in mesh.vertices]
    ys = [v.co.y for v in mesh.vertices]
    w, h = max(xs) - min(xs), max(ys) - min(ys)
    k = 1.0
    if max_width is not None and w > max_width:
        k = min(k, max_width / w)
    if max_height is not None and h > max_height:
        k = min(k, max_height / h)
    if k < 1.0:
        mesh.transform(Matrix.Diagonal((k, k, 1.0, 1.0)))
        mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    col.objects.link(obj)
    obj.data.materials.append(mat)
    obj.parent = parent
    obj.location = location
    obj.rotation_euler = TEXT_ROT
    obj["aux"] = 1
    obj["fit_scale"] = k
    return obj


def _label_plate(ctx, text, mount):
    """Plate below the kit (on the bench: in front of it, toward the operator, so the kit cannot hide the text), as wide
    as the kit but at least MIN_PLATE_W (the grid cell width); the text scales down to fit the plate, it never widens it."""
    h, gap = LABEL_PLATE["height"], LABEL_PLATE["gap"]
    t = 0.01  # K22 depth
    w = max(ctx.fw, MIN_PLATE_W)
    z = -(ctx.fh / 2.0 + gap + h / 2.0)
    plate = ctx.box("LabelPlate", (0, t / 2.0, z), (w, t, h), ctx.m["M_PanelSlate"])
    plate["aux"] = 1
    make_text(ctx.col, ctx.nm("LabelText"), text, ctx.label_size or LABEL_TEXT_SIZE, ctx.m["M_GaugeFace"], ctx.root,
              (0, t + LABEL_EXTRUDE, z), max_width=w - 0.008, max_height=h - 0.006)
    ctx.root["label_w"] = w
    ctx.root["label_z"] = z


def _oos_tag(ctx):
    y = ctx.depth + 0.004
    tag = ctx.box("OosTag", (0, y, 0), (0.08, 0.005, 0.05), ctx.m["M_ScramRed"])
    tag["aux"] = 1
    make_text(ctx.col, ctx.nm("OosText"), "OUT OF\nSERVICE", 0.012, ctx.m["M_GaugeFace"], ctx.root,
              (0, y + 0.0025 + 0.0005, 0), max_width=0.07, extrude=0.0005)


def _register(kid):
    def deco(body):
        def builder(ctl_id, pivot_name=None, label=None, parent=None, location=(0, 0, 0), rotation=(0, 0, 0),
                    out_of_service=False, mount="vertical", label_size=None):
            root_name = "CTL_" + ctl_id
            if root_name in bpy.data.objects:
                raise ValueError("duplicate kit root " + root_name)
            grammar = _PIVOT_GRAMMAR.get(kid, "none")
            if pivot_name and not out_of_service and grammar != "none":
                if not pivot_name.startswith(grammar + "_"):
                    raise ValueError("kit %s pivot must start with %s_ (got %s)" % (kid, grammar, pivot_name))
            if out_of_service:
                mover = "OOS_Part_" + ctl_id
            else:
                mover = pivot_name or ("Part_" + ctl_id)
            col = get_or_create_collection("Kits")
            root = add_empty(root_name, location, col, parent)
            root.rotation_euler = rotation
            root["kit_id"] = kid
            root["ctl_id"] = ctl_id
            root["out_of_service"] = bool(out_of_service)
            if label:
                root["label"] = label
            ctx = Ctx(kid, ctl_id, root, mover, col, label, label_size)
            body(ctx)
            pose = POSE_CONTRACT.get(kid)
            if pose:
                for k, v in pose.items():
                    root[k] = v
            if kid not in SELF_LABELED and kid not in FURNITURE:
                if label and kid not in NO_PLATE:
                    _label_plate(ctx, label, mount)
                if out_of_service:
                    _oos_tag(ctx)
            return root
        builder.__name__ = "build_" + kid
        BUILDERS[kid] = builder
        return builder
    return deco


def build_kit(kit_id, ctl_id, pivot_name=None, label=None, parent=None, location=(0, 0, 0), rotation=(0, 0, 0),
              out_of_service=False, mount="vertical", label_size=None):
    if kit_id not in BUILDERS:
        raise KeyError("unknown kit id " + kit_id)
    return BUILDERS[kit_id](ctl_id, pivot_name, label, parent, location, rotation, out_of_service, mount, label_size)


# ---------------------------------------------------------------------------------------------------------------
# Gauges and indicators

def _gauge_circular(ctx, s):
    m = ctx.m
    sy = ctx.depth / 0.08
    ctx.base(m["M_PanelSlate"], 0.03 * sy)
    # bezel ring: a dark disc proud of the slate, the square faceplate and dial face sit inside it
    ctx.cyl("Bezel", (0, 0.031 * sy, 0), 0.106 * s, 0.008 * sy, m["M_BezelBlack"], verts=32)
    ctx.box("Faceplate", (0, 0.032 * sy, 0), (0.19 * s, 0.004, 0.19 * s), m["M_GaugeFace"])
    ctx.cyl("DialFace", (0, 0.0345 * sy, 0), 0.0965 * s, 0.0015, m["M_GaugeFace"], verts=32)
    ctx.ticks(ctx.root, (0, 0), 0.082 * s, [i / 10.0 for i in range(11)], 0.0355 * sy, 0.014 * s, m["M_DarkRubber"])
    ctx.ticks(ctx.root, (0, 0), 0.068 * s, [0.8], 0.0365 * sy, 0.016 * s, m["M_AmberGlow"], width=0.008 * s)
    ctx.ticks(ctx.root, (0, 0), 0.068 * s, [0.92], 0.0365 * sy, 0.016 * s, m["M_ScramRed"], width=0.008 * s)
    ctx.cyl("Glass", (0, 0.0375 * sy, 0), 0.097 * s, 0.0012, m["M_ContainmentGlass"], verts=32)
    for k in range(4):  # bezel screws at the diagonals
        a = math.radians(45.0 + 90.0 * k)
        ctx.cyl("Screw%d" % k, (0.100 * s * math.sin(a), 0.0345 * sy, 0.100 * s * math.cos(a)),
                0.0035, 0.003, m["M_BrushedSteel"], verts=10)
    piv = ctx.pivot((0, 0.045 * sy, 0))
    # long along Z: the binder turns the pivot about local Z (Blender Y), so the needle visibly sweeps
    ctx.box("Needle", (0, 0, 0.035 * s), (0.010 * s, 0.008, 0.100 * s), m["M_GaugeNeedle"], piv)
    ctx.box("NeedleTail", (0, 0, -0.028 * s), (0.008 * s, 0.006, 0.030 * s), m["M_GaugeNeedle"], piv)
    ctx.cyl("Hub", (0, 0.048 * sy, 0), 0.014 * s, 0.014, m["M_BrushedSteel"], verts=20)
    ctx.cyl("HubCap", (0, 0.0555 * sy, 0), 0.006 * s, 0.003, m["M_BezelBlack"], verts=16)


@_register("K01")
def _k01(ctx):
    _gauge_circular(ctx, 1.0)


@_register("K01L")
def _k01l(ctx):
    _gauge_circular(ctx, 0.45 / 0.22)


@_register("K02")
def _k02(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.03)
    # bezel channel around the strip: side rails and end caps in bezel black
    for sx in (-1, 1):
        ctx.box("Rail%d" % sx, (sx * 0.055, 0.032, 0), (0.010, 0.004, 0.30), m["M_BezelBlack"])
    for sz in (-1, 1):
        ctx.box("EndCap%d" % sz, (0, 0.032, sz * 0.153), (0.12, 0.004, 0.010), m["M_BezelBlack"])
    ctx.box("Strip", (0, 0.032, 0), (0.10, 0.004, 0.30), m["M_GaugeFace"])
    ctx.box("Glass", (0, 0.040, 0), (0.096, 0.0012, 0.296), m["M_ContainmentGlass"])
    for i in range(11):
        ctx.box("Tick%d" % i, (-0.04, 0.037, -0.14 + i * 0.028), (0.03, 0.003, 0.003), m["M_DarkRubber"])
    ctx.box("TargetBug", (0, 0.037, 0.06), (0.10, 0.004, 0.012), m["M_AmberGlow"])
    piv = ctx.pivot((0, 0.045, 0))  # rest = mid travel: the binder slides local Y by Travel*frac around RestY-Travel/2
    ctx.box("PointerBar", (0, 0, 0), (0.09, 0.008, 0.014), m["M_GaugeNeedle"], piv)
    ctx.box("PointerTip", (0.048, 0, 0), (0.014, 0.006, 0.010), m["M_GaugeNeedle"], piv)
    bank = "BankInsertion" in ctx.mover
    ctx.root["travel_m"] = 0.30 if bank else 0.18  # mirrors the binder (ControlRoom3DBinder.BindGauges)


@_register("K03")
def _k03(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.03)
    # bezel frame around the dual dial face
    for sx in (-1, 1):
        ctx.box("Rail%d" % sx, (sx * 0.120, 0.032, 0), (0.008, 0.004, 0.17), m["M_BezelBlack"])
    for sz in (-1, 1):
        ctx.box("Cap%d" % sz, (0, 0.032, sz * 0.093), (0.24, 0.004, 0.008), m["M_BezelBlack"])
    ctx.box("Faceplate", (0, 0.032, 0), (0.23, 0.004, 0.17), m["M_GaugeFace"])
    ctx.box("Glass", (0, 0.038, 0), (0.222, 0.0012, 0.162), m["M_ContainmentGlass"])
    ctx.ticks(ctx.root, (0, -0.025), 0.062, [i / 8.0 for i in range(9)], 0.0355, 0.012, m["M_DarkRubber"])
    ctx.box("Chip_Doppler", (-0.10, 0.0365, 0.07), (0.03, 0.004, 0.012), m["M_GreenGlow"])
    ctx.box("Chip_Xe", (0.10, 0.0365, 0.07), (0.03, 0.004, 0.012), m["M_AmberGlow"])
    piv = ctx.pivot((0, 0.045, -0.025))
    ctx.box("Needle", (0, 0, 0.035), (0.010, 0.008, 0.085), m["M_GaugeNeedle"], piv)
    ctx.box("NeedleTail", (0, 0, -0.022), (0.008, 0.006, 0.026), m["M_GaugeNeedle"], piv)
    ctx.cyl("Hub", (0, 0.048, -0.025), 0.012, 0.014, m["M_BrushedSteel"], verts=20)
    ctx.cyl("HubCap", (0, 0.0555, -0.025), 0.005, 0.003, m["M_BezelBlack"], verts=16)


@_register("K04")
def _k04(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.03)
    ctx.box("Bezel", (0, 0.031, 0), (0.28, 0.004, 0.10), m["M_BezelBlack"])
    for i in range(5):
        ctx.box("Digit%d" % i, (-0.10 + i * 0.05, 0.034, 0), (0.04, 0.004, 0.07), m["M_CrtScreen"])
    ctx.box("DigitGlass", (0, 0.0365, 0), (0.25, 0.0012, 0.085), m["M_ContainmentGlass"])
    ctx.pivot((0, 0.045, 0))  # BIND_GAUGE pivot with no child: the binder's rotation has nothing to move


@_register("K05")
def _k05(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.02)
    ctx.box("Bezel", (0, 0.023, 0), (0.076, 0.006, 0.056), m["M_BezelBlack"])
    lens = ctx.box("Lens", (0, 0.026, 0), (0.07, 0.008, 0.05), m["M_AnnunciatorEmissive"], raw=False)
    lens.name = ctx.mover  # the binder reads the Renderer on the BIND_LAMP_* object itself
    ctx.set_mover(lens)
    if ctx.label:  # a tile carries its own text on the lens (no label plate), scaled to fit the lens
        make_text(ctx.col, ctx.nm("TileText"), RL.wrap_tile_label(ctx.label, 5 if ctx.label_size else 9), ctx.label_size or 0.012, m["M_GaugeFace"], ctx.root,
                  (0, 0.031, 0), max_width=0.064, max_height=0.044, extrude=0.0005)


@_register("K20")
def _k20(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.008)
    ctx.cyl("Bezel", (0, 0.019, 0), 0.0195, 0.003, m["M_BezelBlack"], verts=24)
    ctx.cyl("Housing", (0, 0.016, 0), 0.019, 0.012, m["M_BrushedSteel"], verts=24)
    lens = ctx.cyl("Lens", (0, 0.025, 0), 0.015, 0.008, m["M_AnnunciatorEmissive"], verts=24)
    lens.name = ctx.mover
    ctx.set_mover(lens)


# ---------------------------------------------------------------------------------------------------------------
# Controls (the binder rotates INT_DIAL about local Z / rod-bank dials about local X, presses INT_BTN along local -Z)

@_register("K06")
def _k06(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.025)
    ctx.cyl("Collar", (0, 0.031, 0), 0.040, 0.012, m["M_BrushedSteel"])
    ctx.cyl("CollarTrim", (0, 0.0365, 0), 0.036, 0.003, m["M_BezelBlack"], verts=24)
    piv = ctx.pivot((0, 0.037, 0))
    ctx.cyl("Plunger", (0, 0.009, 0), 0.028, 0.022, m["M_ScramRed"], piv)
    ctx.cyl("PlungerRing", (0, 0.0105, 0), 0.030, 0.004, m["M_BezelBlack"], piv, verts=24)
    # SCRAM cover: a red hood over the plunger on a bottom hinge; U3 flips it open when armed.
    # Stack stays inside the 0.06 m depth contract: plunger top ~0.0495, cover top ~0.0595.
    cover = ctx.aux_pivot("PART_ScramCover", (0, 0.0505, -0.038))
    ctx.box("Hinge", (0, 0, 0), (0.070, 0.010, 0.010), m["M_BrushedSteel"], cover)
    ctx.cyl("Cover", (0, 0.0045, 0.038), 0.042, 0.008, m["M_ScramRed"], cover, verts=24)
    ctx.cyl("CoverRim", (0, 0.008, 0.038), 0.044, 0.002, m["M_BezelBlack"], cover, verts=24)


@_register("K07")
def _k07(ctx):
    m = ctx.m
    ctx.base(m["M_ConsoleBeige"], 0.03)
    # lever slot: dark channel with steel side rails and detent notches
    ctx.box("Slot", (0, 0.032, 0), (0.016, 0.004, 0.14), m["M_DarkRubber"])
    for sx in (-1, 1):
        ctx.box("Rail%d" % sx, (sx * 0.0135, 0.033, 0), (0.003, 0.002, 0.14), m["M_BrushedSteel"])
    for i, z in enumerate((-0.05, -0.025, 0.0, 0.025, 0.05)):
        ctx.box("Detent%d" % i, (0, 0.0345, z), (0.010, 0.002, 0.006), m["M_BezelBlack"])
    ctx.cyl("PivotBoss", (0, 0.034, 0), 0.011, 0.012, m["M_BrushedSteel"], axis="Y", verts=16)
    piv = ctx.pivot((0, 0.02, 0))  # rod-bank dials rotate about local X (the T-handle pitches)
    ctx.box("Shaft", (0, 0.017, 0), (0.016, 0.034, 0.016), m["M_BrushedSteel"], piv)
    ctx.box("Handle", (0, 0.034, 0), (0.07, 0.012, 0.016), m["M_ScramRed"], piv)
    for i, x in enumerate((-0.022, 0.0, 0.022)):  # grip ridges across the T-handle
        ctx.box("Grip%d" % i, (x, 0.034, 0), (0.006, 0.012, 0.018), m["M_DarkRubber"], piv)


@_register("K08")
def _k08(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.02)
    ctx.cyl("Bezel", (0, 0.021, 0), 0.050, 0.004, m["M_BezelBlack"], verts=32)
    ctx.ticks(ctx.root, (0, 0), 0.047, [i / 8.0 for i in range(9)], 0.0235, 0.008, m["M_GaugeFace"],
              sweep=ANIM["knob_sweep_z"], width=0.004)
    ctx.box("Index", (0, 0.024, 0.050), (0.006, 0.004, 0.008), m["M_GaugeNeedle"])  # fixed pointer at 12 o'clock
    piv = ctx.pivot((0, 0.02, 0))
    ctx.cyl("Skirt", (0, 0.006, 0), 0.034, 0.012, m["M_BrushedSteel"], piv, verts=32)
    ctx.cyl("Knob", (0, 0.026, 0), 0.026, 0.040, m["M_BezelBlack"], piv, verts=32)
    ctx.cyl("KnobCap", (0, 0.0475, 0), 0.020, 0.005, m["M_BrushedSteel"], piv, verts=24)
    ctx.box("Pointer", (0, 0.0505, 0.016), (0.008, 0.004, 0.024), m["M_GaugeNeedle"], piv)


@_register("K09")
def _k09(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.02)
    ctx.cyl("Bezel", (0, 0.021, 0), 0.055, 0.004, m["M_BezelBlack"], verts=32)
    ctx.ticks(ctx.root, (0, 0), 0.05, [i / 3.0 for i in range(4)], 0.0235, 0.010, m["M_GaugeFace"],
              sweep=ANIM["knob_sweep_z"], width=0.005)
    for i, f in enumerate((0.0, 1.0 / 3.0, 2.0 / 3.0, 1.0)):  # detent marks at each selector stop
        th = math.radians(lerp(ANIM["knob_sweep_z"][0], ANIM["knob_sweep_z"][1], f))
        mk = ctx.box("Detent%d" % i, (-0.052 * math.sin(th), 0.0235, 0.052 * math.cos(th)),
                     (0.006, 0.003, 0.010), m["M_AmberGlow"])
        mk.rotation_euler = (0, -th, 0)
    piv = ctx.pivot((0, 0.02, 0))
    ctx.box("Grip", (0, 0.022, 0.022), (0.024, 0.040, 0.050), m["M_ScramRed"], piv)
    ctx.box("GripIndex", (0, 0.043, 0.040), (0.008, 0.004, 0.014), m["M_GaugeFace"], piv)
    ctx.cyl("Hub", (0, 0.030, 0), 0.020, 0.020, m["M_BrushedSteel"], piv, verts=16)
    ctx.cyl("HubCap", (0, 0.041, 0), 0.012, 0.004, m["M_BezelBlack"], piv, verts=16)


@_register("K10")
def _k10(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.01, "Flange")
    ctx.box("Pedestal", (0, 0.035, 0), (0.06, 0.05, 0.06), m["M_PanelSlate"])
    ctx.cyl("BackPlate", (0, 0.048, 0), 0.118, 0.004, m["M_BezelBlack"], verts=40)
    ctx.ticks(ctx.root, (0, 0), 0.105, [i / 8.0 for i in range(9)], 0.0505, 0.010, m["M_GaugeFace"],
              sweep=ANIM["knob_sweep_z"], width=0.004)
    ctx.box("Index", (0, 0.0515, 0.110), (0.008, 0.003, 0.012), m["M_GaugeNeedle"])
    piv = ctx.pivot((0, 0.07, 0))
    bpy.ops.mesh.primitive_torus_add(major_radius=0.10, minor_radius=0.012, major_segments=48, minor_segments=12,
                                     location=(0, 0, 0), rotation=(math.pi / 2, 0, 0))
    rim = bpy.context.active_object
    rim.name = ctx.nm("Rim")
    rim.data.materials.append(m["M_BrushedSteel"])
    rim.parent = piv
    link_to(rim, ctx.col)
    ctx.box("SpokeH", (0, 0, 0), (0.20, 0.010, 0.012), m["M_BrushedSteel"], piv)
    ctx.box("SpokeV", (0, 0, 0), (0.012, 0.010, 0.20), m["M_BrushedSteel"], piv)
    ctx.cyl("Hub", (0, 0.005, 0), 0.025, 0.030, m["M_BrushedSteel"], piv)
    ctx.cyl("HubCap", (0, 0.021, 0), 0.016, 0.004, m["M_BezelBlack"], piv, verts=20)
    for sx, sz in ((0.10, 0.0), (-0.10, 0.0), (0.0, -0.10)):  # grips on the three free spoke ends
        ctx.cyl("Grip%d_%d" % (int(sx * 100), int(sz * 100)), (sx, 0.020, sz), 0.012, 0.040,
                m["M_ScramRed"], piv, verts=16)
    ctx.cyl("Grip", (0, 0.020, 0.10), 0.012, 0.040, m["M_ScramRed"], piv, verts=16)


@_register("K11")
def _k11(ctx):
    m = ctx.m
    ctx.box("Base", (0, 0.015, -0.015), (0.07, 0.03, 0.07), m["M_PanelSlate"])
    ctx.box("Lamp_Red", (-0.017, 0.034, 0.04), (0.02, 0.008, 0.02), m["M_ScramRed"])
    ctx.box("Lamp_Green", (0.017, 0.034, 0.04), (0.02, 0.008, 0.02), m["M_GreenGlow"])
    piv = ctx.pivot((0, 0.03, -0.015))
    ctx.box("Bat", (0, 0.015, 0), (0.012, 0.030, 0.012), m["M_BrushedSteel"], piv)
    ctx.box("BatTip", (0, 0.031, 0), (0.016, 0.008, 0.016), m["M_ScramRed"], piv)
    # guarded toggle: a flip-up red cover over the bat on a bottom hinge; U3 flips it open when armed.
    # Children are hinge-local: the hood sits above the hinge line, its free edge toward +Z (up-face).
    guard = ctx.aux_pivot("PART_Guard", (0, 0.036, -0.045))
    ctx.box("GuardHinge", (0, 0, 0), (0.064, 0.006, 0.006), m["M_BrushedSteel"], guard)
    ctx.box("GuardTop", (0, 0.032, 0.040), (0.062, 0.005, 0.080), m["M_ScramRed"], guard)
    for sx in (-1, 1):
        ctx.box("GuardCheek%d" % sx, (sx * 0.029, 0.008, 0.040), (0.005, 0.030, 0.080), m["M_ScramRed"], guard)
    ctx.box("GuardLip", (0, 0.018, 0.076), (0.062, 0.016, 0.005), m["M_ScramRed"], guard)


@_register("K12")
def _k12(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.03)
    ctx.box("Slot", (0, 0.032, 0), (0.014, 0.004, 0.20), m["M_DarkRubber"])
    ctx.box("Bar_Actual", (0.037, 0.032, 0), (0.012, 0.004, 0.18), m["M_GreenGlow"])
    ctx.box("Bar_Demand", (-0.037, 0.032, 0), (0.012, 0.004, 0.18), m["M_AmberGlow"])
    # scale ticks beside the slot and end stops
    for i in range(5):
        ctx.box("Tick%d" % i, (0.026, 0.034, -0.08 + i * 0.04), (0.008, 0.002, 0.003), m["M_DarkRubber"])
    for sz in (-1, 1):
        ctx.box("Stop%d" % sz, (0, 0.034, sz * 0.102), (0.014, 0.006, 0.008), m["M_BezelBlack"])
    # NOTE: the binder rotates INT_DIAL pivots, so the round thumb turns in place; the pivot sits at track mid.
    piv = ctx.pivot((0, 0.04, 0))
    ctx.cyl("Thumb", (0, 0.010, 0), 0.030, 0.020, m["M_BrushedSteel"], piv, verts=24)
    ctx.cyl("ThumbGrip", (0, 0.021, 0), 0.020, 0.006, m["M_BezelBlack"], piv, verts=20)
    ctx.box("ThumbMark", (0, 0.025, 0.016), (0.006, 0.004, 0.024), m["M_GaugeNeedle"], piv)


@_register("K19")
def _k19(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.02)
    ctx.cyl("Bezel", (0, 0.024, 0), 0.029, 0.004, m["M_BezelBlack"], verts=24)
    ctx.cyl("Ring", (0, 0.027, 0), 0.026, 0.010, m["M_BrushedSteel"], verts=24)
    piv = ctx.pivot((0, 0.02, 0))
    ctx.cyl("Cap", (0, 0.010, 0), 0.020, 0.020, m["M_GaugeFace"], piv, verts=24)
    ctx.cyl("CapDome", (0, 0.021, 0), 0.012, 0.004, m["M_BezelBlack"], piv, verts=20)


# ---------------------------------------------------------------------------------------------------------------
# CRT, mimic, klaxon, signs

@_register("K13")
def _k13(ctx):
    m = ctx.m
    # Deep console CRT: housing, picture-frame bezel, recessed emissive face, vents, corner screws.
    # The Screen mesh keeps its ~0.32 x 0.24 active face; CrtScreenRenderer targets it by name + thinnest axis,
    # so nothing else here may carry the CRT_ name or the M_CrtScreen material, and no glass may cover the face
    # (the text plane sits 3 mm proud of it).
    ctx.box("Housing", (0, 0.10, 0), (0.36, 0.20, 0.28), m["M_PanelSlate"])
    ctx.box("HousingLip", (0, 0.205, 0), (0.37, 0.01, 0.29), m["M_BezelBlack"])  # shadow gap under the bezel
    # Bezel frame: four rails out to the 0.40 x 0.30 face edges around the screen opening, front face at y = 0.29.
    for sz, nm in ((1, "BezelTop"), (-1, "BezelBottom")):
        ctx.box(nm, (0, 0.26, sz * 0.14), (0.40, 0.06, 0.02), m["M_BezelBlack"])
    for sx, nm in ((1, "BezelLeft"), (-1, "BezelRight")):
        ctx.box(nm, (sx * 0.19, 0.26, 0), (0.02, 0.06, 0.26), m["M_BezelBlack"])
    # Screen recessed ~20 mm behind the bezel face.
    scr = ctx.box("Screen", (0, 0.266, 0), (0.32, 0.008, 0.234), m["M_CrtScreen"])
    scr.name = ctx.mover
    ctx.set_mover(scr)
    # Vent slots on both housing side faces (the front is fully occupied by lip + bezel).
    for sx in (-1, 1):
        for i in range(3):
            ctx.box("Vent%d_%d" % (sx, i), (sx * 0.181, 0.10, -0.06 + i * 0.06),
                    (0.004, 0.06, 0.010), m["M_DarkRubber"])
    # Corner screws on the bezel face.
    for sx in (-1, 1):
        for sz in (-1, 1):
            ctx.cyl("Screw%d_%d" % (sx, sz), (sx * 0.185, 0.291, sz * 0.14), 0.006, 0.004,
                    m["M_BrushedSteel"], axis="Y", verts=12)
    ctx.box("PowerLED", (0.16, 0.291, -0.14), (0.012, 0.004, 0.012), m["M_GreenGlow"])


@_register("K16")
def _k16(ctx):
    m = ctx.m
    ctx.box("Trace", (0, 0.003, 0), (0.30, 0.006, 0.02), m["M_BrushedSteel"])
    ctx.box("Chevrons", (0, 0.007, 0), (0.26, 0.002, 0.006), m["M_AmberGlow"])


@_register("K17")
def _k17(ctx):
    m = ctx.m
    ctx.box("MountPlate", (0, 0.02, -0.085), (0.15, 0.04, 0.03), m["M_BrushedSteel"])
    ctx.cyl("BaseFlange", (0, 0.075, -0.068), 0.072, 0.008, m["M_BezelBlack"], axis="Z", verts=24)
    dome = ctx.cyl("Dome", (0, 0.075, 0.015), 0.06, 0.17, m["M_ScramRed"], axis="Z", verts=24)
    dome.name = ctx.mover  # AlarmFeedback matches the Renderer named BIND_KLAXON_Beacon_*
    ctx.set_mover(dome)
    for k in range(4):  # beacon cage ribs: vertical strips on the dome's XY cross-section, long along Z
        a = math.radians(45.0 + 90.0 * k)
        rib = ctx.box("Rib%d" % k, (0.062 * math.sin(a), 0.075 + 0.062 * math.cos(a), 0.015),
                      (0.005, 0.012, 0.160), m["M_BezelBlack"])
        rib.rotation_euler = (0, 0, -a)
    ctx.cyl("EndCap", (0, 0.075, -0.066), 0.030, 0.008, m["M_BezelBlack"], axis="Z", verts=20)


@_register("K21")
def _k21(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.03)
    # sign housing: bezel rails around the lit face, end caps at both extremes
    for sx in (-1, 1):
        ctx.box("Rail%d" % sx, (sx * (ctx.fw / 2.0 - 0.012), 0.032, 0), (0.024, 0.004, ctx.fh - 0.02), m["M_BezelBlack"])
    for sz in (-1, 1):
        ctx.box("Cap%d" % sz, (0, 0.032, sz * (ctx.fh / 2.0 - 0.012)), (ctx.fw - 0.024, 0.004, 0.024), m["M_BezelBlack"])
    make_text(ctx.col, ctx.nm("SignText"), ctx.label or "SECTION", 0.12, m["M_AmberGlow"], ctx.root,
              (0, 0.03 + LABEL_EXTRUDE, 0), max_width=ctx.fw - 0.06, max_height=ctx.fh - 0.05)


@_register("K22")
def _k22(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.01)
    make_text(ctx.col, ctx.nm("PlateText"), ctx.label or "LABEL", 0.03, m["M_GaugeFace"], ctx.root,
              (0, 0.01 + LABEL_EXTRUDE, 0), max_width=ctx.fw - 0.02, max_height=ctx.fh - 0.01)


@_register("K23")
def _k23(ctx):
    m = ctx.m
    ctx.base(m["M_ScramRed"], 0.005)
    make_text(ctx.col, ctx.nm("TagText"), "OUT OF\nSERVICE", 0.012, m["M_GaugeFace"], ctx.root,
              (0, 0.005 + 0.0005, 0), max_width=0.07, extrude=0.0005)


# ---------------------------------------------------------------------------------------------------------------
# Room furniture (no footprint contract; the U6 room generator places chairs and console sections)

@_register("K14")
def _k14(ctx):
    m = ctx.m
    ctx.box("LowerCabinet", (0, 0, 0.35), (0.60, 0.55, 0.70), m["M_ConsoleBeige"])
    lower = ctx.box("AngledLowerDeck", (0, 0.10, 0.72), (0.60, 0.25, 0.04), m["M_ConsoleBeige"])
    lower.rotation_euler = (math.radians(-18), 0, 0)
    upper = ctx.box("UpperInstrumentHood", (0, -0.08, 1.10), (0.60, 0.20, 0.40), m["M_ConsoleBeige"])
    upper.rotation_euler = (math.radians(72), 0, 0)
    ctx.box("KickPlate", (0, 0.28, 0.05), (0.58, 0.02, 0.10), m["M_PanelSlate"])


@_register("K15")
def _k15(ctx):
    m = ctx.m
    ctx.cyl("FiveStarBase", (0, 0, 0.05), 0.30, 0.03, m["M_PanelSlate"], axis="Z", verts=5)
    ctx.cyl("GasCylinder", (0, 0, 0.35), 0.035, 0.55, m["M_BrushedSteel"], axis="Z", verts=12)
    seat = add_empty(ctx.nm("ChairSeat_Pivot"), (0, 0, 0.65), ctx.col, ctx.root)
    ctx.box("SeatPan", (0, 0, 0), (0.48, 0.45, 0.06), m["M_PanelSlate"], seat)
    # operator faces -Y (toward the panel whose normal is +Y), so the backrest is on +Y
    ctx.box("Backrest", (0, 0.22, 0.18), (0.48, 0.06, 0.38), m["M_PanelSlate"], seat)


@_register("K18")
def _k18(ctx):
    m = ctx.m
    ctx.box("BinderBody", (0, 0, 0.6), (0.22, 0.02, 0.30), m["M_PanelSlate"])
    ctx.box("MetalClip", (0, 0.01, 0.72), (0.10, 0.02, 0.03), m["M_BrushedSteel"])
    ctx.box("PageQuad", (0, 0.012, 0.60), (0.19, 0.005, 0.26), m["M_GaugeFace"])


# ---------------------------------------------------------------------------------------------------------------
# Helpers for the gallery / checks

def get_pivot(root):
    return bpy.data.objects[root["mover_obj"]]


def rotate_needle(root, frac):
    """Pose a gauge pivot like ControlRoom3DBinder.UpdateGauges: Unity Euler(0,0,lerp(-135,135,frac)) about local Z
    (clockwise to the operator) == Blender rotation_euler.y = -angle."""
    lo, hi = ANIM["gauge_sweep_z"]
    get_pivot(root).rotation_euler = (0.0, -math.radians(lerp(lo, hi, frac)), 0.0)


def local_bbox(root, include_aux=False):
    """(lo, hi) Vectors of the mesh bounds under root, in root-local space (X across, Y proud, Z up)."""
    bpy.context.view_layer.update()
    inv = root.matrix_world.inverted()
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in root.children_recursive:
        if o.type != "MESH" or (not include_aux and o.get("aux")):
            continue
        mw = inv @ o.matrix_world
        for c in o.bound_box:
            v = mw @ Vector(c)
            lo = Vector((min(lo[i], v[i]) for i in range(3)))
            hi = Vector((max(hi[i], v[i]) for i in range(3)))
    return lo, hi


def check_footprint(root):
    """Returns (ok, w, h, d, expected_dict) measured on the kit itself (label plate and OOS tag excluded).
    ok requires the face_w x face_h window AND that the kit stays inside its declared depth (y 0..depth)."""
    kid = root["kit_id"]
    exp = FOOTPRINTS[kid]
    lo, hi = local_bbox(root)
    w, h, d = hi.x - lo.x, hi.z - lo.z, hi.y - min(lo.y, 0.0)
    ok = (abs(w - exp["face_w"]) <= FOOTPRINT_TOL and abs(h - exp["face_h"]) <= FOOTPRINT_TOL
          and hi.y <= exp["depth"] + FOOTPRINT_TOL and lo.y >= -FOOTPRINT_TOL)
    return ok, w, h, d, exp
