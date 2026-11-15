import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lib_common import reset_scene
from lib_materials import build_materials
from lib_reactor_parts import Geometry, assembly, empty, finish, lods


def build():
    reset_scene()
    mats = build_materials()
    assembly(mats, offset=(0, -0.37, 0))
    for bank in range(4):
        letter = chr(65 + bank)
        x = -0.09 if bank % 2 == 0 else 0.09
        y = -0.37 + (-0.09 if bank < 2 else 0.09)
        mount = empty("RS_ROD_Bank%s_PIV" % letter, (x, y, 0))
        empty("RS_ROD_Bank%s_Withdrawn_PIV" % letter, (0, 0, 4.15), mount)
        empty("RS_ROD_Bank%s_Inserted_PIV" % letter, (0, 0, 0.25), mount)
        pivot = empty("ROD_BANK_%s_PIV" % letter, (0, 0, 4.15), mount)
        pivot["rs_bind"] = "plant.core.rods.bank" + letter
        rod = Geometry()
        # Cluster-style absorber fingers, a spider hub and the long drive shaft.
        for dx, dy in ((-0.025,-0.025), (0.025,-0.025), (-0.025,0.025), (0.025,0.025)):
            rod.tube((dx, dy, 0), (dx, dy, 3.6), 0.007, 8)
            rod.tube((dx, dy, -0.025), (dx, dy, 0), 0.002, 8, tip_radius=0.007)
        rod.box((0, 0, 3.63), (0.08, 0.08, 0.04))
        rod.tube((0, 0, 3.65), (0, 0, 7.8), 0.013, 12)
        lods(rod, "RS_ROD_AbsorberCluster_%s" % letter, mats["M_DarkRubber"], pivot)
        drive = Geometry()
        drive.tube((0, 0, 7.85), (0, 0, 9.15), 0.07, 24)
        drive.tube((0, 0, 9.15), (0, 0, 12.05), 0.035, 16)
        for z in (9.2, 10.1, 11.05, 12.0):
            drive.tube((0, 0, z), (0, 0, z + 0.025), 0.042, 16)
        for z in (7.85, 8.05, 8.55, 9.05):
            drive.tube((0, 0, z), (0, 0, z + 0.04), 0.105, 24)
            for i in range(6):
                a = math.tau * i / 6
                drive.tube((0.085 * math.cos(a), 0.085 * math.sin(a), z + 0.04),
                           (0.085 * math.cos(a), 0.085 * math.sin(a), z + 0.06), 0.012, 6)
        lods(drive, "RS_ROD_CRDMHousing_%s" % letter, mats["M_BrushedSteel"], mount)
        scale = Geometry()
        scale.box((0, -0.076, 8.6), (0.027, 0.005, 0.75))
        for i in range(16):
            scale.box((0, -0.080, 8.24 + i * 0.045),
                      (0.021 if i % 5 == 0 else 0.012, 0.004, 0.003))
        lods(scale, "RS_ROD_PositionScale_%s" % letter, mats["M_PanelSlate"], mount)
    finish("ReactorCore")


if __name__ == "__main__":
    build()
