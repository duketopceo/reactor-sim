# ReactorSim — headless Workbench renders of the control room (U2, KTD7). A layout/scale aid; it never
# satisfies a gate (the Unity RoomRender does).
# Usage: blender -b --python-exit-code 1 --python Blender/scripts/render_room.py [-- --blend <path> --prefix <name>]
# Opens Blender/ControlRoom.blend (or --blend) and renders every CAM_* camera at 1600x900 to
# <repo>/out/render/<prefix>-<name>.png (prefix defaults to "blender"):
#   seat (CAM_Seat_A1), stand (CAM_Stand_Center), top (CAM_Top), and any other CAM_* present, named from the camera:
#   CAM_Stand_Center_L / _R -> stand-l / stand-r (the board is wider than one frame), CAM_Foo_Bar -> foo_bar
# Cameras named CAM_* in the .blend are used as authored. When NONE exist fixed viewpoints that frame the blend are
# used instead. If some exist and one of the three required ones is missing, the script fails naming it.
# CAM_Top hides every object tagged with the custom property "ceiling" (ceiling slab, fixtures, lights).
import bpy
import os
import sys
from mathutils import Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
BLEND_DIR = os.path.dirname(SCRIPT_DIR)
REPO_ROOT = os.path.dirname(BLEND_DIR)
BLEND = os.path.join(BLEND_DIR, "ControlRoom.blend")
OUT_DIR = os.path.join(REPO_ROOT, "out", "render")
WIDTH, HEIGHT = 1600, 900

PREFIX = "blender"

ANCHORS = [("seat", "CAM_Seat_A1"), ("stand", "CAM_Stand_Center"), ("top", "CAM_Top")]


def view_key(name):
    for key, n in ANCHORS:
        if n == name:
            return key
    if name.startswith("CAM_Stand_Center_"):
        return "stand-" + name[len("CAM_Stand_Center_"):].lower()
    return name[len("CAM_"):].lower()


def all_cam_names():
    """The three required cameras first (seat, stand, top), then every other CAM_* in name order."""
    base = [n for _, n in ANCHORS]
    extra = sorted(o.name for o in bpy.data.objects
                   if o.name.startswith("CAM_") and o.name not in base and o.type in ("CAMERA", "EMPTY"))
    return base + extra


def fail(msg):
    print("RENDER_ROOM_FAIL", msg)
    sys.exit(1)


def parse_args():
    """Optional `-- --blend <path> --prefix <name>`; a relative --blend resolves against cwd, then the repo root."""
    global BLEND, PREFIX
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    i = 0
    while i < len(argv):
        if argv[i] == "--blend" and i + 1 < len(argv):
            path = argv[i + 1]
            if not os.path.isabs(path) and not os.path.exists(path):
                path = os.path.join(REPO_ROOT, path)
            BLEND = os.path.abspath(path)
            i += 2
        elif argv[i] == "--prefix" and i + 1 < len(argv):
            PREFIX = argv[i + 1]
            i += 2
        else:
            fail("unknown argument: " + argv[i])


def look_at(obj, target):
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def make_camera(name, location, target=None, ortho_scale=None):
    data = bpy.data.cameras.new(name)
    data.clip_start = 0.05
    data.clip_end = 500.0
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
        cam.rotation_euler = (0.0, 0.0, 0.0)  # looking straight down -Z
    return cam


def room_bounds():
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    found = False
    for o in bpy.data.objects:
        if o.type != "MESH":
            continue
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector((min(lo[i], w[i]) for i in range(3)))
            hi = Vector((max(hi[i], w[i]) for i in range(3)))
            found = True
    if not found:
        fail("no mesh objects in " + BLEND)
    return lo, hi


def fallback_camera(key, lo, hi):
    cx = (lo.x + hi.x) / 2.0
    cy = (lo.y + hi.y) / 2.0
    wall = Vector((cx, lo.y + 1.0, lo.z + (hi.z - lo.z) * 0.4))  # toward the mimic wall
    chair = bpy.data.objects.get("Station_A1_Reactor_Chair")
    if key == "seat":
        loc = chair.matrix_world.translation + Vector((0, 0, 0.6)) if chair else Vector((cx, cy, 1.2))
        return make_camera("CAM_Seat_A1", loc, target=wall)
    if key == "stand":
        return make_camera("CAM_Stand_Center", (cx, cy, 1.7), target=wall)
    span_x = hi.x - lo.x
    span_y = hi.y - lo.y
    scale = max(span_x, span_y * WIDTH / HEIGHT) * 1.05
    return make_camera("CAM_Top", (cx, cy, hi.z + 5.0), ortho_scale=scale)


def anchor_camera(name):
    obj = bpy.data.objects.get(name)
    if obj is None:
        return None
    if obj.type == "CAMERA":
        return obj
    # an empty marks the pose: put a camera there
    cam = make_camera(name + "_cam", (0, 0, 0))
    cam.matrix_world = obj.matrix_world.copy()
    return cam


def main():
    parse_args()
    if not os.path.exists(BLEND):
        fail(f"{BLEND} not found")
    bpy.ops.wm.open_mainfile(filepath=BLEND)
    os.makedirs(OUT_DIR, exist_ok=True)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x = WIDTH
    scene.render.resolution_y = HEIGHT
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"

    present = [n for _, n in ANCHORS if bpy.data.objects.get(n) is not None]
    missing = [n for _, n in ANCHORS if n not in present]
    if present and missing:
        fail("missing camera anchor(s): " + ", ".join(missing))
    use_fallback = not present
    if use_fallback:
        print("RENDER_ROOM_NOTE no CAM_* anchors in the blend; using fixed viewpoints")
        lo, hi = room_bounds()

    views = ANCHORS if use_fallback else [(view_key(n), n) for n in all_cam_names()]
    for key, name in views:
        cam = fallback_camera(key, lo, hi) if use_fallback else anchor_camera(name)
        scene.camera = cam
        for o in bpy.data.objects:
            if o.get("ceiling"):
                o.hide_render = (name == "CAM_Top")
        path = os.path.join(OUT_DIR, f"{PREFIX}-{key}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        if not os.path.exists(path) or os.path.getsize(path) < 1024:
            fail(f"render {key} wrote no usable image: {path}")
        print("RENDER_ROOM_OK", path, os.path.getsize(path))
    print("RENDER_ROOM_DONE")


main()
