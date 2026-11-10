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
    ctx.box("Faceplate", (0, 0.032 * sy, 0), (0.19 * s, 0.004, 0.19 * s), m["M_GaugeFace"])
    ctx.ticks(ctx.root, (0, 0), 0.082 * s, [i / 10.0 for i in range(11)], 0.035 * sy, 0.014 * s, m["M_DarkRubber"])
    ctx.ticks(ctx.root, (0, 0), 0.068 * s, [0.8], 0.036 * sy, 0.016 * s, m["M_AmberGlow"], width=0.008 * s)
    ctx.ticks(ctx.root, (0, 0), 0.068 * s, [0.92], 0.036 * sy, 0.016 * s, m["M_ScramRed"], width=0.008 * s)
    piv = ctx.pivot((0, 0.045 * sy, 0))
    # long along Z: the binder turns the pivot about local Z (Blender Y), so the needle visibly sweeps
    ctx.box("Needle", (0, 0, 0.035 * s), (0.010 * s, 0.008, 0.100 * s), m["M_GaugeNeedle"], piv)
    ctx.cyl("Hub", (0, 0.048 * sy, 0), 0.014 * s, 0.014, m["M_BrushedSteel"], verts=20)


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
    ctx.box("Strip", (0, 0.032, 0), (0.10, 0.004, 0.30), m["M_GaugeFace"])
    for i in range(11):
        ctx.box("Tick%d" % i, (-0.04, 0.035, -0.14 + i * 0.028), (0.03, 0.003, 0.003), m["M_DarkRubber"])
    ctx.box("TargetBug", (0, 0.036, 0.06), (0.10, 0.004, 0.012), m["M_AmberGlow"])
    piv = ctx.pivot((0, 0.045, 0))  # rest = mid travel: the binder slides local Y by Travel*frac around RestY-Travel/2
    ctx.box("PointerBar", (0, 0, 0), (0.09, 0.008, 0.014), m["M_GaugeNeedle"], piv)
    bank = "BankInsertion" in ctx.mover
    ctx.root["travel_m"] = 0.30 if bank else 0.18  # mirrors the binder (ControlRoom3DBinder.BindGauges)


@_register("K03")
def _k03(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.03)
    ctx.box("Faceplate", (0, 0.032, 0), (0.23, 0.004, 0.17), m["M_GaugeFace"])
    ctx.ticks(ctx.root, (0, -0.025), 0.062, [i / 8.0 for i in range(9)], 0.035, 0.012, m["M_DarkRubber"])
    ctx.box("Chip_Doppler", (-0.10, 0.036, 0.07), (0.03, 0.004, 0.012), m["M_GreenGlow"])
    ctx.box("Chip_Xe", (0.10, 0.036, 0.07), (0.03, 0.004, 0.012), m["M_AmberGlow"])
    piv = ctx.pivot((0, 0.045, -0.025))
    ctx.box("Needle", (0, 0, 0.035), (0.010, 0.008, 0.085), m["M_GaugeNeedle"], piv)
    ctx.cyl("Hub", (0, 0.048, -0.025), 0.012, 0.014, m["M_BrushedSteel"], verts=20)


@_register("K04")
def _k04(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.03)
    for i in range(5):
        ctx.box("Digit%d" % i, (-0.10 + i * 0.05, 0.034, 0), (0.04, 0.004, 0.07), m["M_CrtScreen"])
    ctx.pivot((0, 0.045, 0))  # BIND_GAUGE pivot with no child: the binder's rotation has nothing to move


@_register("K05")
def _k05(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.02)
    lens = ctx.box("Lens", (0, 0.024, 0), (0.07, 0.008, 0.05), m["M_AnnunciatorEmissive"], raw=False)
    lens.name = ctx.mover  # the binder reads the Renderer on the BIND_LAMP_* object itself
    ctx.set_mover(lens)
    if ctx.label:  # a tile carries its own text on the lens (no label plate), scaled to fit the lens
        make_text(ctx.col, ctx.nm("TileText"), RL.wrap_tile_label(ctx.label, 5 if ctx.label_size else 9), ctx.label_size or 0.012, m["M_GaugeFace"], ctx.root,
                  (0, 0.028 + 0.0005, 0), max_width=0.064, max_height=0.044, extrude=0.0005)


@_register("K20")
def _k20(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.008)
    ctx.cyl("Housing", (0, 0.014, 0), 0.019, 0.012, m["M_BrushedSteel"], verts=24)
    lens = ctx.cyl("Lens", (0, 0.023, 0), 0.015, 0.008, m["M_AnnunciatorEmissive"], verts=24)
    lens.name = ctx.mover
    ctx.set_mover(lens)


# ---------------------------------------------------------------------------------------------------------------
# Controls (the binder rotates INT_DIAL about local Z / rod-bank dials about local X, presses INT_BTN along local -Z)

@_register("K06")
def _k06(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.025)
    ctx.cyl("Collar", (0, 0.031, 0), 0.040, 0.012, m["M_BrushedSteel"])
    piv = ctx.pivot((0, 0.037, 0))
    ctx.cyl("Plunger", (0, 0.015, 0), 0.028, 0.030, m["M_ScramRed"], piv)


@_register("K07")
def _k07(ctx):
    m = ctx.m
    ctx.base(m["M_ConsoleBeige"], 0.03)
    ctx.box("Slot", (0, 0.032, 0), (0.016, 0.004, 0.14), m["M_DarkRubber"])
    piv = ctx.pivot((0, 0.02, 0))  # rod-bank dials rotate about local X (the T-handle pitches)
    ctx.box("Shaft", (0, 0.017, 0), (0.016, 0.034, 0.016), m["M_BrushedSteel"], piv)
    ctx.box("Handle", (0, 0.034, 0), (0.07, 0.012, 0.016), m["M_ScramRed"], piv)


@_register("K08")
def _k08(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.02)
    ctx.ticks(ctx.root, (0, 0), 0.047, [i / 8.0 for i in range(9)], 0.022, 0.008, m["M_GaugeFace"],
              sweep=ANIM["knob_sweep_z"], width=0.004)
    piv = ctx.pivot((0, 0.02, 0))
    ctx.cyl("Skirt", (0, 0.006, 0), 0.034, 0.012, m["M_BrushedSteel"], piv)
    ctx.cyl("Knob", (0, 0.026, 0), 0.026, 0.040, m["M_BrushedSteel"], piv)
    ctx.box("Pointer", (0, 0.047, 0.016), (0.008, 0.004, 0.024), m["M_GaugeNeedle"], piv)


@_register("K09")
def _k09(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.02)
    ctx.ticks(ctx.root, (0, 0), 0.05, [i / 3.0 for i in range(4)], 0.022, 0.010, m["M_GaugeFace"],
              sweep=ANIM["knob_sweep_z"], width=0.005)
    piv = ctx.pivot((0, 0.02, 0))
    ctx.box("Grip", (0, 0.022, 0.022), (0.024, 0.040, 0.050), m["M_ScramRed"], piv)
    ctx.cyl("Hub", (0, 0.030, 0), 0.020, 0.020, m["M_BrushedSteel"], piv, verts=16)


@_register("K10")
def _k10(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.01, "Flange")
    ctx.box("Pedestal", (0, 0.035, 0), (0.06, 0.05, 0.06), m["M_PanelSlate"])
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
    ctx.cyl("Grip", (0, 0.020, 0.10), 0.012, 0.040, m["M_ScramRed"], piv, verts=16)


@_register("K11")
def _k11(ctx):
    m = ctx.m
    ctx.box("Base", (0, 0.015, -0.015), (0.07, 0.03, 0.07), m["M_PanelSlate"])
    ctx.box("Lamp_Red", (-0.017, 0.034, 0.04), (0.02, 0.008, 0.02), m["M_ScramRed"])
    ctx.box("Lamp_Green", (0.017, 0.034, 0.04), (0.02, 0.008, 0.02), m["M_GreenGlow"])
    piv = ctx.pivot((0, 0.03, -0.015))
    ctx.box("Bat", (0, 0.015, 0), (0.012, 0.030, 0.012), m["M_BrushedSteel"], piv)
    for sx in (-1, 1):
        ctx.box("GuardPost%d" % sx, (sx * 0.026, 0.05, -0.015), (0.006, 0.04, 0.04), m["M_ScramRed"])
    ctx.box("GuardBar", (0, 0.068, -0.015), (0.058, 0.008, 0.008), m["M_ScramRed"])


@_register("K12")
def _k12(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.03)
    ctx.box("Slot", (0, 0.032, 0), (0.014, 0.004, 0.20), m["M_DarkRubber"])
    ctx.box("Bar_Actual", (0.037, 0.032, 0), (0.012, 0.004, 0.18), m["M_GreenGlow"])
    ctx.box("Bar_Demand", (-0.037, 0.032, 0), (0.012, 0.004, 0.18), m["M_AmberGlow"])
    # NOTE: the binder rotates INT_DIAL pivots, so the round thumb turns in place; the pivot sits at track mid.
    piv = ctx.pivot((0, 0.04, 0))
    ctx.cyl("Thumb", (0, 0.010, 0), 0.030, 0.020, m["M_BrushedSteel"], piv)
    ctx.box("ThumbMark", (0, 0.021, 0.016), (0.006, 0.004, 0.024), m["M_GaugeNeedle"], piv)


@_register("K19")
def _k19(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.02)
    ctx.cyl("Ring", (0, 0.025, 0), 0.026, 0.010, m["M_BrushedSteel"])
    piv = ctx.pivot((0, 0.02, 0))
    ctx.cyl("Cap", (0, 0.010, 0), 0.020, 0.020, m["M_GaugeFace"], piv)


# ---------------------------------------------------------------------------------------------------------------
# CRT, mimic, klaxon, signs

@_register("K13")
def _k13(ctx):
    m = ctx.m
    ctx.box("Housing", (0, 0.12, 0), (0.34, 0.24, 0.26), m["M_PanelSlate"])
    ctx.box("Bezel", (0, 0.265, 0), (0.40, 0.05, 0.30), m["M_PanelSlate"])
    # CrtScreenRenderer lays text on the thinnest bounds axis (Y here) of the CRT_* mesh
    scr = ctx.box("Screen", (0, 0.292, 0), (0.32, 0.006, 0.24), m["M_CrtScreen"])
    scr.name = ctx.mover
    ctx.set_mover(scr)
    ctx.box("PowerLED", (0.17, 0.293, -0.135), (0.012, 0.006, 0.012), m["M_GreenGlow"])


@_register("K16")
def _k16(ctx):
    m = ctx.m
    ctx.box("Trace", (0, 0.003, 0), (0.30, 0.006, 0.02), m["M_BrushedSteel"])
    ctx.box("Chevrons", (0, 0.007, 0), (0.26, 0.002, 0.006), m["M_AmberGlow"])


@_register("K17")
def _k17(ctx):
    m = ctx.m
    ctx.box("MountPlate", (0, 0.02, -0.085), (0.15, 0.04, 0.03), m["M_BrushedSteel"])
    dome = ctx.cyl("Dome", (0, 0.075, 0.015), 0.06, 0.17, m["M_ScramRed"], axis="Z", verts=24)
    dome.name = ctx.mover  # AlarmFeedback matches the Renderer named BIND_KLAXON_Beacon_*
    ctx.set_mover(dome)


@_register("K21")
def _k21(ctx):
    m = ctx.m
    ctx.base(m["M_PanelSlate"], 0.03)
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
    """Returns (ok, w, h, d, expected_dict) measured on the kit itself (label plate and OOS tag excluded)."""
    kid = root["kit_id"]
    exp = FOOTPRINTS[kid]
    lo, hi = local_bbox(root)
    w, h, d = hi.x - lo.x, hi.z - lo.z, hi.y
    ok = abs(w - exp["face_w"]) <= FOOTPRINT_TOL and abs(h - exp["face_h"]) <= FOOTPRINT_TOL
    return ok, w, h, d, exp
