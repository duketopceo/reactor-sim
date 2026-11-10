# ReactorSim Blender — shared geometry/pivot/naming helpers (U10)
# All generators import this module. Run headless with:
#   blender -b --python Blender/scripts/lib_common.py
import bpy
import math
import re

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def scrub_user_paths():
    """Reset every file-browser directory kept in the saved UI.

    A saved .blend carries the UI, including each File Browser area's last directory. In a fresh headless
    session that is the operating system's per-user documents folder, which would embed a local user path in a
    committed file. Pointing it at the blend-relative root keeps the generated files free of machine paths.
    """
    for screen in bpy.data.screens:
        for area in screen.areas:
            for space in area.spaces:
                params = getattr(space, "params", None)
                if params is None:
                    continue
                # The RNA setter only writes up to the terminator, so a short value leaves the previous (longer)
                # path in the bytes after it. Overwrite the whole region first, then set the real value.
                params.directory = b"/" * 400
                params.directory = b"//"
                params.filename = ""


def save_blend(path):
    """Save the scene to *path* with relative remapping and no per-user paths in the UI."""
    scrub_user_paths()
    bpy.ops.wm.save_as_mainfile(filepath=path, relative_remap=True)


def get_or_create_collection(name):
    for c in bpy.data.collections:
        if c.name == name:
            return c
    c = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(c)
    return c

def link_to(obj, collection):
    collection.objects.link(obj)
    for c in list(obj.users_collection):
        if c is not collection:
            c.objects.unlink(obj)

def make_mat(name, rgb, metallic=0.0, roughness=0.5, emission_color=None, emission_strength=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission_color is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission_color, 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission_strength
    return m

def add_box(name, location, dimensions, material=None, collection=None, parent=None, bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 2
    if material:
        obj.data.materials.append(material)
    if parent:
        obj.parent = parent
    if collection:
        link_to(obj, collection)
    return obj

def add_cylinder(name, location, radius, depth, material=None, collection=None, parent=None, vertices=32, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(radius=radius, depth=depth, location=location, vertices=vertices, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    if material:
        obj.data.materials.append(material)
    if parent:
        obj.parent = parent
    if collection:
        link_to(obj, collection)
    return obj

def add_mesh(name, verts, faces, material=None, collection=None, parent=None):
    """Mesh from raw vertices/faces (no operators). Faces must wind counter-clockwise seen from outside."""
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    if material:
        obj.data.materials.append(material)
    if parent:
        obj.parent = parent
    if collection:
        link_to(obj, collection)
    return obj

def add_empty(name, location, collection=None, parent=None):
    obj = bpy.data.objects.new(name, None)
    obj.location = location
    bpy.context.scene.collection.objects.link(obj)
    if parent:
        obj.parent = parent
    if collection:
        link_to(obj, collection)
    return obj

def set_origin(obj, mode, location=(0, 0, 0)):
    """Set mesh origin to a specific point (mode='ORIGIN_GEOMETRY' style not used here).
    For pivot-bearing parts, we create an Empty at the pivot and parent the mesh."""
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.origin_set(type=mode)
    obj.select_set(False)

def add_pivot_empty(name, obj, pivot_local):
    """Create an Empty at pivot_local relative to obj and parent obj to it, so Unity can rotate the Empty."""
    # Parent to world first so empty sits correctly
    world_pivot = obj.matrix_world @ pivot_local
    empty = add_empty(name, world_pivot)
    obj.parent = empty
    # Keep world transform: parent_inverse = empty world matrix
    obj.parent_type = 'OBJECT'
    obj.parent_inverse = empty.matrix_world.inverted()
    return empty

def add_collider_mesh(name, obj, create=True):
    """Naming helper: collider proxy meshes are named COL_*; Unity postprocessor swaps them to colliders."""
    obj.name = name
    return obj

# --- Naming contract validators (BTD3) ---
BIND_RE = re.compile(r"^BIND_(GAUGE|BAR|LAMP)_[A-Za-z0-9_]+$")
INT_RE = re.compile(r"^INT_(BTN|DIAL)_[A-Za-z0-9_]+$")
CRT_RE = re.compile(r"^CRT_[A-Za-z0-9_]+$")
COL_RE = re.compile(r"^COL_[A-Za-z0-9_]+$")

def validate_bind_name(name):
    for rx in (BIND_RE, INT_RE, CRT_RE, COL_RE):
        if rx.match(name):
            return True
    return name in ("ControlRoom",) or name.startswith("Kit_") or name.startswith("Mimic_") or name.startswith("Station_") or name.startswith("Reactor") or name.startswith("Pressurizer") or name.startswith("Steam") or not (name.startswith("BIND") or name.startswith("INT") or name.startswith("CRT") or name.startswith("COL"))

# --- Pivot presets (BTD2) ---
# Gauge needle: pivot at shaft center; mesh child offset so needle points +Y at 0.0
def pivot_center():
    return (0, 0, 0)

def pivot_bottom():
    return (0, 0, 0)  # caller positions mesh with +Y up, origin at 0%

def pivot_bezel():
    return (0, 0, 0)

# Standard normalized animation ranges (mirrored in Unity ControlRoom3DBinder)
ANIM = {
    "gauge_sweep_z": (-135.0, 135.0),   # degrees, local Z
    "bar_travel_y": (0.0, 0.18),        # meters, local +Y
    "rod_bar_travel_y": (0.0, 0.10),
    "knob_sweep_z": (-150.0, 150.0),    # degrees, local Z
    "lever_sweep_x": (-40.0, 40.0),     # degrees, local X
    "plunger_z": (0.0, -0.025),         # meters, local -Z
    "hinge_x": (0.0, -100.0),           # degrees, local X
}

if __name__ == "__main__":
    reset_scene()
    col = get_or_create_collection("Test")
    m = make_mat("M_Test", (0.5, 0.5, 0.5), roughness=0.6)
    obj = add_box("TestBox", (0, 0, 0), (1, 1, 1), m, col)
    assert validate_bind_name("BIND_GAUGE_FuelTempC_0_1400")
    assert validate_bind_name("INT_BTN_Scram_Core_1")
    assert validate_bind_name("CRT_A1_Core")
    assert validate_bind_name("COL_Desk_A1")
    print("LIB_COMMON_OK")
