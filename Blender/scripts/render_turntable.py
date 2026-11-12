"""Neutral-lit material preview. Open a blend, then -- output-dir [distance]."""
import math
import os
import sys

import bpy
from mathutils import Vector


def render():
    args = sys.argv[sys.argv.index("--") + 1:]
    output = os.path.abspath(args[0])
    os.makedirs(output, exist_ok=True)
    for obj in bpy.data.objects:
        if "_LOD1" in obj.name or "_LOD2" in obj.name:
            obj.hide_render = True
    objects = list(bpy.data.objects)
    if len(args) > 2:
        root = bpy.data.objects[args[2]]
        objects = [root, *root.children_recursive]
    points = [obj.matrix_world @ Vector(v) for obj in objects
              if obj.type == "MESH" and not obj.hide_render for v in obj.bound_box]
    if not points:
        raise ValueError("No renderable meshes")
    lo = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    hi = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    center = (lo + hi) * 0.5
    size = max(hi - lo)
    if len(args) > 3 and args[3] == "top":
        size = min(size, 0.9)
        center.z = hi.z - 0.25
    distance = float(args[1]) if len(args) > 1 else size * 1.65
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 960
    scene.render.resolution_y = 960
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.color = (0.16, 0.16, 0.16)
    scene.view_settings.view_transform = "AgX"
    for index, (offset, power, color) in enumerate((
        ((-1.2, -1.0, 1.4), 1800, (0.83, 0.91, 1.0)),
        ((1.0, -0.3, 0.6), 1100, (1.0, 0.87, 0.69)),
        ((0.0, 1.0, 1.1), 2400, (0.65, 0.80, 1.0)),
    )):
        data = bpy.data.lights.new("PreviewKey" + str(index), "AREA")
        data.energy = power * max(0.1, size / 4) ** 2
        data.shape = "DISK"
        data.size = size * 0.65
        data.color = color
        obj = bpy.data.objects.new(data.name, data)
        scene.collection.objects.link(obj)
        obj.location = center + Vector(offset) * size
        obj.rotation_euler = (center - obj.location).to_track_quat("-Z", "Y").to_euler()
    data = bpy.data.cameras.new("PreviewCamera")
    data.lens = 52
    camera = bpy.data.objects.new("PreviewCamera", data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    for frame, angle in enumerate((-65, -25, 25, 155)):
        a = math.radians(angle)
        camera.location = center + Vector((math.sin(a), -math.cos(a), 0.3)) * distance
        camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(output, "view_%02d.png" % frame)
        bpy.ops.render.render(write_still=True)
    print("TURNTABLE_SAVED", output)


if __name__ == "__main__":
    render()
