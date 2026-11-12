import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lib_common import reset_scene
from lib_materials import build_materials
from lib_reactor_parts import Geometry, assembly, finish, lods


def build():
    reset_scene()
    mats = build_materials()
    index = 1
    # Reserve the window-facing centre for the detailed assembly, not a simplified occluder.
    for x, y in ((-1,-1), (0,0), (1,-1), (-1,0), (1,0), (-1,1), (0,1), (1,1)):
        assembly(mats, index, (x * 0.37, y * 0.37, 0), detailed=False)
        index += 1
    barrel, trim, glow = Geometry(), Geometry(), Geometry()
    opening = math.radians(120)
    barrel.ring(-0.15, 0.95, 4.5, 0.045, 48, opening)
    barrel.ring(-0.3, 1.14, 4.9, 0.055, 48, opening)
    for z in (-0.25, 0.15, 4.1, 4.45):
        trim.ring(z, 1.17, 0.10, 0.25, 48, opening)
        for i in range(16):
            a = math.radians(-30 + i * 15)
            trim.tube((1.04 * math.cos(a), 1.04 * math.sin(a), z + 0.1),
                      (1.04 * math.cos(a), 1.04 * math.sin(a), z + 0.15), 0.025, 6)
    # A radial baffle/support grid with open coolant paths.
    for i in range(-3, 4):
        trim.box((i * 0.21, 0, -0.08), (0.025, 1.55, 0.12))
        trim.box((0, i * 0.21, -0.08), (1.55, 0.025, 0.12))
    for i in range(9):
        x, y = (i % 3 - 1) * 0.37, (i // 3 - 1) * 0.37
        glow.box((x, y, 0.1), (0.31, 0.31, 0.025))
    lods(barrel, "RS_VES_CoreBarrel_01", mats["M_BrushedSteel"])
    lods(trim, "RS_VES_ThermalShield_01", mats["M_PanelSlate"])
    lods(glow, "RS_VES_CoreGlow_01", mats["M_CherenkovGlow"], binding="plant.core")
    finish("VesselInternals")


if __name__ == "__main__":
    build()
