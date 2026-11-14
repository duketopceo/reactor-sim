"""Generate an original, board-scale whole-plant overview; all state sockets are neutral."""
import math
import os
import sys
import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lib_common import reset_scene, get_or_create_collection, add_box, add_empty, make_mat, save_blend
from lib_materials import build_materials


def build():
    reset_scene()
    m = build_materials()
    col = get_or_create_collection("PlantOverview")
    root = add_empty("PlantOverview", (0, 0, 0), col)
    primary = make_mat("M_OverviewPrimary", (0.70, 0.25, 0.09), metallic=0.25)
    steam = make_mat("M_OverviewSteam", (0.80, 0.66, 0.27), metallic=0.25)
    water = make_mat("M_OverviewWater", (0.12, 0.40, 0.68), metallic=0.25)
    electric = make_mat("M_OverviewElectric", (0.64, 0.42, 0.72), metallic=0.25)
    chem = make_mat("M_OverviewChemistry", (0.28, 0.60, 0.40), metallic=0.25)

    def box(name, x, z, w, h, mat, y=-0.12, depth=0.08):
        return add_box(name, (x, y, z), (w, depth, h), mat, col, parent=root, bevel=0.008)

    def label(name, value, x, z, size=0.075):
        curve = bpy.data.curves.new(name, "FONT")
        curve.body, curve.size, curve.align_x = value, size, "CENTER"
        obj = bpy.data.objects.new(name, curve)
        col.objects.link(obj)
        obj.location, obj.rotation_euler, obj.parent = (x, -0.34, z), (math.pi / 2, 0, 0), root
        obj.data.materials.append(m["M_GaugeFace"])
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.convert(target="MESH")
        obj.select_set(False)

    def pipe(name, points, mat):
        for i, ((x1, z1), (x2, z2)) in enumerate(zip(points, points[1:])):
            assert x1 == x2 or z1 == z2, name
            box("Pipe_%s_%02d" % (name, i), (x1+x2)/2, (z1+z2)/2,
                max(abs(x2-x1), 0.055), max(abs(z2-z1), 0.055), mat, -0.14, 0.07)

    def node(name, title, x, z, w, h, material=None):
        box("System_" + name, x, z, w, h, material or m["M_BrushedSteel"], -0.20, 0.18)
        box("Status_" + name, x - w/2 - 0.10, z + h/2, 0.08, 0.08, m["M_DarkRubber"], -0.31, 0.04)
        add_empty("LabelSocket_" + name, (x, -0.36, z - h/2 - 0.12), col, parent=root)
        label("Legend_" + name, title, x, z - h/2 - 0.11, 0.065)

    box("Board_Back", 0, 1.85, 7.6, 3.7, m["M_PanelSlate"], 0, 0.12)
    for x in (-3.84, 3.84):
        box("Frame_Left" if x < 0 else "Frame_Right", x, 1.85, 0.07, 3.8, m["M_BrushedSteel"])
    for z in (-0.03, 3.73):
        box("Frame_Bottom" if z < 1 else "Frame_Top", 0, z, 7.75, 0.07, m["M_BrushedSteel"])
    label("Title", "WHOLE PLANT OVERVIEW", 0, 3.45, 0.15)

    # Every route ends at a component or another route. Color denotes circuit only.
    pipe("PrimaryHot", [(-2.35,2.15),(-1.45,2.15)], primary)
    pipe("PrimaryCold", [(-1.15,1.45),(-1.15,0.72),(-1.80,0.72),(-2.65,0.72),(-2.65,1.33)], primary)
    pipe("Surge", [(-2.0,2.15),(-2.0,2.55)], primary)
    pipe("Chemistry", [(-3.05,0.62),(-2.65,0.62),(-2.65,0.72)], chem)
    pipe("SteamHeader", [(-0.85,2.15),(-0.55,2.15)], steam)
    pipe("Steam", [(-0.55,2.15),(-0.05,2.15)], steam)
    # The bypass carries dump steam directly to the condenser, independently of the turbine.
    pipe("SteamDump", [(-0.55,2.15),(-0.55,1.15),(-0.05,1.15),(-0.05,1.10)], steam)
    pipe("TurbineExhaust", [(0.28,1.83),(0.28,1.10)], steam)
    pipe("Feedwater", [(-0.06,0.95),(-0.70,0.95),(-0.70,1.65),(-0.85,1.65)], water)
    pipe("CoolingSupply", [(0.62,0.80),(1.35,0.80),(1.35,0.50),(2.35,0.50),(2.35,0.65)], water)
    pipe("CoolingReturn", [(0.62,1.02),(1.55,1.02),(2.12,1.02)], water)
    pipe("Shaft", [(0.60,2.15),(1.0,2.15)], m["M_BrushedSteel"])
    pipe("Grid", [(1.50,2.15),(2.35,2.15)], electric)
    pipe("Safety", [(-3.10,2.80),(-3.10,2.15),(-2.95,2.15)], chem)

    node("Reactor", "REACTOR / FUEL", -2.65, 1.85, 0.60, 1.04)
    box("Core_Fuel", -2.65, 1.75, 0.35, 0.48, m["M_CherenkovGlow"], -0.31, 0.04)
    for i in range(4):
        box("RodBank_" + "ABCD"[i], -2.78 + i*0.085, 2.30, 0.025, 0.40, m["M_BrushedSteel"], -0.32, 0.04)
    node("Pressurizer", "PRESSURIZER", -2.0, 2.82, 0.35, 0.54)
    node("PrimaryPump", "PRIMARY PUMP", -1.80, 0.72, 0.35, 0.30)
    node("SteamGenerator", "STEAM GENERATOR", -1.15, 1.90, 0.60, 0.90)
    for i in range(4):
        box("SG_Tube_%02d" % i, -1.32+i*0.11, 1.90, 0.025, 0.65, m["M_PanelSlate"], -0.31, 0.025)
    node("Turbine", "TURBINE", 0.28, 2.15, 0.65, 0.64)
    for i in range(4):
        box("Turbine_Rib_%02d" % i, 0.08+i*0.13, 2.15, 0.025, 0.50, m["M_PanelSlate"], -0.31, 0.025)
    node("Generator", "GENERATOR", 1.25, 2.15, 0.50, 0.50, m["M_ConsoleBeige"])
    node("Grid", "GRID", 2.63, 2.15, 0.56, 0.50, m["M_ConsoleBeige"])
    node("Condenser", "CONDENSER", 0.28, 0.95, 0.68, 0.30)
    node("FeedPump", "FEED PUMP", -0.45, 0.95, 0.30, 0.26)
    node("Cooling", "COOLING", 2.35, 0.85, 0.46, 0.40, m["M_ConsoleBeige"])
    node("Chemistry", "BORON / MAKEUP", -3.25, 0.62, 0.40, 0.44)
    node("Safety", "SAFETY", -3.10, 2.98, 0.42, 0.36, m["M_ConsoleBeige"])
    label("Legend_Circuits", "PRIMARY     STEAM     FEED / COOLING     ELECTRICAL     CHEM / SAFETY", 0, 0.13, 0.075)
    add_empty("MetricsSocket", (0, -0.36, 3.20), col, parent=root)
    output = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "PlantOverview.blend")
    save_blend(output)
    print("PLANT_OVERVIEW_SAVED", output)


if __name__ == "__main__":
    build()
