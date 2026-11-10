# ReactorSim Blender — trim-sheet bake (BTD4) + annunciator atlas
# Headless: blender -b --python Blender/scripts/bake_trim_sheets.py [-- --res 1024]
#
# For each of the 8 master PBR materials (first .blend that defines it wins), bakes
# with Cycles onto a dedicated UV'd plane so the result is exactly that material:
#   T_<mat>_BaseColor.png         albedo, via Base Color -> Emission -> EMIT (sRGB)
#   T_<mat>_MetallicSmoothness.png  R = metallic, A = 1 - roughness (Unity Standard)
#   T_<mat>_Emission.png          EMIT pass, only for materials with emission > 0
# plus AnnunciatorAtlas_24.png (6x4 OFF-state tiles). Output: Blender/exports/Textures/.
# Blends are opened read-only in memory and never saved.
import os
import struct
import sys
import zlib

import bpy
import numpy as np

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
BLEND_DIR = os.path.dirname(SCRIPT_DIR)
EXPORT_DIR = os.path.join(BLEND_DIR, "exports", "Textures")

BLENDS = ["ControlRoom.blend", "PanelKits.blend", "PlantEquipment.blend"]
MATERIALS = [
    "M_PanelSlate", "M_ConsoleBeige", "M_BrushedSteel", "M_DarkRubber",
    "M_CrtScreen", "M_GaugeFace", "M_AnnunciatorEmissive", "M_ContainmentGlass",
]

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
BAKE_RES = int(args[args.index("--res") + 1]) if "--res" in args else 1024


def principled(mat):
    for n in mat.node_tree.nodes:
        if n.type == "BSDF_PRINCIPLED":
            return n
    return None


def scalar_input(bsdf, name, default):
    sock = bsdf.inputs.get(name) if bsdf else None
    if sock is None:
        return default
    if sock.is_linked:
        print(f"BAKE_WARN {name} is node-driven; using its default_value for packing")
    return float(sock.default_value)


def setup_scene():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 4  # flat shading inputs: a few samples is exact
    scene.render.bake.margin = 0
    return scene


def make_bake_plane(mat):
    bpy.ops.mesh.primitive_plane_add(size=2.0, location=(0, 0, -1000))
    plane = bpy.context.active_object
    plane.name = f"__bake_{mat.name}"
    plane.data.materials.clear()
    plane.data.materials.append(mat)
    for o in bpy.context.selected_objects:
        o.select_set(False)
    plane.select_set(True)
    bpy.context.view_layer.objects.active = plane
    return plane


def bake_pass(mat, bake_type, colorspace, pass_filter=None):
    img = bpy.data.images.new(f"__bake_{mat.name}_{bake_type}", BAKE_RES, BAKE_RES, alpha=True, float_buffer=False)
    img.colorspace_settings.name = colorspace
    nodes = mat.node_tree.nodes
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = img
    nodes.active = tex
    kwargs = {"type": bake_type, "use_clear": True}
    if pass_filter:
        kwargs["pass_filter"] = pass_filter
    try:
        bpy.ops.object.bake(**kwargs)
    finally:
        nodes.remove(tex)
    px = np.empty(BAKE_RES * BAKE_RES * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    return img, px.reshape(-1, 4)


def bake_base_color(mat, bsdf):
    """Exact albedo: the DIFFUSE/COLOR pass is base * (1 - metallic), which blackens metals.
    Temporarily route Base Color through an Emission shader and bake EMIT instead."""
    nt = mat.node_tree
    out = next((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output), None)
    if out is None:
        out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    surface = out.inputs["Surface"]
    prev = surface.links[0].from_socket if surface.is_linked else None
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.inputs["Strength"].default_value = 1.0
    base = bsdf.inputs["Base Color"]
    if base.is_linked:
        nt.links.new(base.links[0].from_socket, emit.inputs["Color"])
    else:
        emit.inputs["Color"].default_value = base.default_value
    nt.links.new(emit.outputs["Emission"], surface)
    try:
        return bake_pass(mat, "EMIT", "sRGB")
    finally:
        nt.nodes.remove(emit)
        if prev is not None:
            nt.links.new(prev, surface)


def save_png(name, rgba, colorspace):
    img = bpy.data.images.new(name, BAKE_RES, BAKE_RES, alpha=True, float_buffer=False)
    img.colorspace_settings.name = colorspace
    img.pixels.foreach_set(np.ascontiguousarray(rgba, dtype=np.float32).ravel())
    out = os.path.join(EXPORT_DIR, name + ".png")
    img.filepath_raw = out
    img.file_format = "PNG"
    img.save()
    return out


def bake_material(mat):
    bsdf = principled(mat)
    if bsdf is None:
        print(f"BAKE_FAIL {mat.name}: no Principled BSDF")
        return False
    make_bake_plane(mat)

    _, color = bake_base_color(mat, bsdf)
    color[:, 3] = 1.0
    save_png(f"T_{mat.name}_BaseColor", color, "sRGB")

    _, rough = bake_pass(mat, "ROUGHNESS", "Non-Color")
    metallic = scalar_input(bsdf, "Metallic", 0.0)
    ms = np.zeros_like(rough)
    ms[:, 0] = metallic
    ms[:, 1] = metallic
    ms[:, 2] = metallic
    ms[:, 3] = 1.0 - rough[:, 0]
    save_png(f"T_{mat.name}_MetallicSmoothness", ms, "Non-Color")

    strength = scalar_input(bsdf, "Emission Strength", 0.0)
    if strength > 0.0:
        _, emit = bake_pass(mat, "EMIT", "sRGB")
        emit[:, :3] = np.clip(emit[:, :3], 0.0, 1.0)  # LDR map; Unity scales via _EmissionColor
        emit[:, 3] = 1.0
        save_png(f"T_{mat.name}_Emission", emit, "sRGB")

    print(f"BAKE_OK {mat.name} metallic={metallic:.2f} emission={strength:.2f}")
    return True


def make_annunciator_atlas():
    """24-tile OFF-state annunciator atlas (6x4). Unity drives per-tile _EmissionColor."""
    cols, rows, tile = 6, 4, 128
    W, H = cols * tile, rows * tile
    data = bytearray()
    for y in range(H):
        data.append(0)
        for x in range(W):
            tx, ty = x % tile, y % tile
            inside = 4 < tx < tile - 4 and 4 < ty < tile - 4
            if inside:
                data += bytes((16, 16, 18, 255)) if (x // tile + (y // tile) * cols) % 2 == 0 else bytes((22, 22, 26, 255))
            else:
                data += bytes((8, 10, 14, 255))

    def chunk(tag, payload):
        c = struct.pack(">I", len(payload)) + tag + payload
        return c + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(data), 9))
    png += chunk(b"IEND", b"")
    out = os.path.join(EXPORT_DIR, "AnnunciatorAtlas_24.png")
    with open(out, "wb") as f:
        f.write(png)
    print(f"ATLAS_OK {out} (24 tiles {cols}x{rows})")


def main():
    os.makedirs(EXPORT_DIR, exist_ok=True)
    remaining = list(MATERIALS)
    for blend in BLENDS:
        path = os.path.join(BLEND_DIR, blend)
        if not remaining:
            break
        if not os.path.exists(path):
            print(f"BAKE_SKIP blend missing: {path}")
            continue
        bpy.ops.wm.open_mainfile(filepath=path)
        setup_scene()
        for name in list(remaining):
            mat = bpy.data.materials.get(name)
            if mat is None:
                continue
            if bake_material(mat):
                remaining.remove(name)
    if remaining:
        # Masters with no users are dropped when a .blend is saved (e.g. M_DarkRubber);
        # rebuild them from the canonical definitions in lib_materials.
        sys.path.insert(0, SCRIPT_DIR)
        from lib_common import reset_scene
        from lib_materials import build_materials
        reset_scene()
        setup_scene()
        built = build_materials()
        for name in list(remaining):
            print(f"BAKE_FALLBACK {name}: not in any .blend, using lib_materials")
            if bake_material(built[name]):
                remaining.remove(name)
    make_annunciator_atlas()
    baked = len(MATERIALS) - len(remaining)
    print(f"TRIM_SHEETS_BAKED {baked}/{len(MATERIALS)} materials @ {BAKE_RES}px")
    if remaining:
        print(f"BAKE_MISSING {', '.join(remaining)}")
        sys.exit(1)


if __name__ == "__main__":
    main()
