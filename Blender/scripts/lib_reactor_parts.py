"""Original game-scale PWR geometry, batched by material before export."""
import math
import os

import bpy
from mathutils import Vector

from lib_common import add_empty, add_mesh, save_blend


class Geometry:
    def __init__(self):
        self.vertices = []
        self.faces = []
        self.smooth = []

    def face(self, points, smooth=False):
        start = len(self.vertices)
        self.vertices.extend(points)
        self.faces.append(tuple(range(start, start + len(points))))
        self.smooth.append(smooth)

    def box(self, center, size):
        x, y, z = center
        a, b, c = (v / 2 for v in size)
        v = [(x + dx * a, y + dy * b, z + dz * c)
             for dx, dy, dz in ((-1,-1,-1), (1,-1,-1), (1,1,-1), (-1,1,-1),
                               (-1,-1,1), (1,-1,1), (1,1,1), (-1,1,1))]
        for face in ((3,2,1,0), (0,1,5,4), (1,2,6,5), (2,3,7,6), (3,0,4,7), (4,5,6,7)):
            self.face([v[i] for i in face])

    def tube(self, start, end, radius, segments=12, caps=True, tip_radius=None):
        a, b = Vector(start), Vector(end)
        axis = (b - a).normalized()
        tangent = axis.cross(Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((0, 1, 0))).normalized()
        other = axis.cross(tangent)
        rings = []
        for center, r in ((a, radius), (b, radius if tip_radius is None else tip_radius)):
            rings.append([center + r * (math.cos(i * math.tau / segments) * tangent +
                                       math.sin(i * math.tau / segments) * other)
                          for i in range(segments)])
        for i in range(segments):
            j = (i + 1) % segments
            self.face([rings[0][i], rings[0][j], rings[1][j], rings[1][i]], True)
        if caps:
            self.face(list(reversed(rings[0])))
            self.face(rings[1])

    def ring(self, z, radius, height, thickness, segments=48, opening=0.0):
        start = -math.pi / 2 + opening / 2
        span = math.tau - opening
        for i in range(segments):
            a, b = start + span * i / segments, start + span * (i + 1) / segments
            outer = [(radius * math.cos(t), radius * math.sin(t), h)
                     for h in (z, z + height) for t in (a, b)]
            inner = [((radius - thickness) * math.cos(t), (radius - thickness) * math.sin(t), h)
                     for h in (z, z + height) for t in (a, b)]
            self.face([outer[0], outer[1], outer[3], outer[2]], True)
            self.face([inner[1], inner[0], inner[2], inner[3]], True)
            self.face([inner[0], inner[1], outer[1], outer[0]])
            self.face([outer[2], outer[3], inner[3], inner[2]])
            if opening and i == 0:
                self.face([outer[0], outer[2], inner[2], inner[0]])
            if opening and i == segments - 1:
                self.face([outer[3], outer[1], inner[1], inner[3]])

    def mesh(self, name, material, parent=None):
        # Share coincident vertices so smooth tube faces interpolate normals around the shaft.
        vertices, indices, remap = [], {}, []
        for point in self.vertices:
            key = tuple(round(float(v), 6) for v in point)
            if key not in indices:
                indices[key] = len(vertices)
                vertices.append(key)
            remap.append(indices[key])
        faces = [tuple(remap[i] for i in face) for face in self.faces]
        obj = add_mesh(name, vertices, faces, material, parent=parent)
        for polygon, smooth in zip(obj.data.polygons, self.smooth):
            polygon.use_smooth = smooth
        # A simple repeatable planar trim mapping; no packed absolute texture paths.
        uv = obj.data.uv_layers.new(name="TrimUV")
        for polygon in obj.data.polygons:
            for loop in polygon.loop_indices:
                point = obj.data.vertices[obj.data.loops[loop].vertex_index].co
                uv.data[loop].uv = (point.x + point.y, point.z)
        return obj


def empty(name, location=(0, 0, 0), parent=None):
    return add_empty(name, location, parent=parent)


def lods(geometry, name, material, parent=None, binding=""):
    obj = geometry.mesh(name + "_LOD0", material, parent)
    obj["rs_bind"] = binding
    # Decimation happens once at authoring, not at load time.
    for level, ratio in ((1, 0.4), (2, 0.15)):
        copy = obj.copy()
        copy.data = obj.data.copy()
        copy.name = name + "_LOD" + str(level)
        bpy.context.scene.collection.objects.link(copy)
        bpy.context.view_layer.objects.active = copy
        modifier = copy.modifiers.new("GameLOD", "DECIMATE")
        modifier.ratio = ratio
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


def assembly(materials, index=0, offset=(0, 0, 0), detailed=True):
    root = empty("RS_VES_FuelAssembly_%02d_PIV" % index, offset)
    rods, frame = Geometry(), Geometry()
    count = 11 if detailed else 6
    pitch = 0.027 if detailed else 0.05
    for x in range(count):
        for y in range(count):
            if detailed and x % 3 == 2 and y % 3 == 2:
                continue  # guide-tube positions
            u, v = (x - (count - 1) / 2) * pitch, (y - (count - 1) / 2) * pitch
            rods.tube((u, v, 0.18), (u, v, 3.85), 0.009 if detailed else 0.013,
                      8 if detailed else 5, caps=False)
    # Open spacer-grid strips, nozzle flow slots and four support feet.
    for z in ((0.3, 0.85, 1.4, 1.95, 2.5, 3.05, 3.65) if detailed else (0.3, 1.95, 3.65)):
        for i in range(count + 1):
            d = (i - count / 2) * pitch
            frame.box((d, 0, z), (0.004, count * pitch, 0.055))
            frame.box((0, d, z), (count * pitch, 0.004, 0.055))
        if detailed:
            # Bowed outer spacer tabs catch grazing highlights without a unique mesh per cell.
            edge = count * pitch / 2
            for i in range(count):
                d = (i - (count - 1) / 2) * pitch
                for sign in (-1, 1):
                    frame.face([(d - 0.009, sign * edge, z - 0.02),
                                (d + 0.009, sign * edge, z - 0.02),
                                (d, sign * (edge + 0.006), z)])
                    frame.face([(d, sign * (edge + 0.006), z),
                                (d + 0.009, sign * edge, z + 0.02),
                                (d - 0.009, sign * edge, z + 0.02)])
    for z in (0.12, 3.95):
        for i in range(5):
            frame.box(((i - 2) * 0.065, 0, z), (0.021, 0.33, 0.045))
        for x in (-0.15, 0.15):
            frame.box((x, 0, z), (0.018, 0.34, 0.07))
        for y in (-0.15, 0.15):
            frame.box((0, y, z), (0.34, 0.018, 0.07))
    for x in (-0.125, 0.125):
        for y in (-0.125, 0.125):
            frame.tube((x, y, 0), (x, y, 0.18), 0.025, 6)
    frame.tube((-0.10, 0, 4.02), (-0.10, 0, 4.14), 0.016, 8)
    frame.tube((0.10, 0, 4.02), (0.10, 0, 4.14), 0.016, 8)
    frame.tube((-0.10, 0, 4.14), (0.10, 0, 4.14), 0.016, 8)
    mesh = lods(rods, "RS_VES_FuelRodLattice_%02d" % index, materials["M_BrushedSteel"], root)
    mesh["rodlet_count"] = count * count - (9 if detailed else 0)
    lods(frame, "RS_VES_SpacerNozzle_%02d" % index, materials["M_BrushedSteel"], root)
    return root


def finish(name):
    for obj in bpy.data.objects:
        obj["rs_id"] = obj.name
        obj["rs_bind"] = obj.get("rs_bind", "")
        obj["rs_status"] = "C" if obj["rs_bind"] else "S"
        obj["rs_priority"] = "P0" if obj["rs_bind"] else "P1"
        obj["rs_lod"] = int(obj.name[-1]) if "_LOD" in obj.name else 0
        obj["rs_license"] = "Original-project-asset"
        obj["rs_author"] = "ReactorSim"
        obj["rs_version"] = "1"
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1
    # Avoid generating local backup files on every build.
    bpy.context.preferences.filepaths.save_version = 0
    save_blend(os.path.join(os.path.dirname(os.path.dirname(__file__)), name + ".blend"))
    print(name.upper() + "_SAVED", len(bpy.data.objects))
