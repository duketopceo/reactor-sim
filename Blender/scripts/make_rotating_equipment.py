import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lib_common import reset_scene
from lib_materials import build_materials
from lib_reactor_parts import Geometry, empty, finish, lods


def build():
    reset_scene()
    mats = build_materials()
    pump, plate = Geometry(), Geometry()
    shoulder = 7.3
    # Shoulder pump in the integral module: motor fins, bolted service flange, volute throat.
    for i in range(4):
        a = math.radians(-129 + 26 * i)
        x, y = 1.69 * math.cos(a), 1.69 * math.sin(a)
        pump.tube((x, y, shoulder + 2.9), (x, y, shoulder + 5.3), 0.29, 24)
        for j in range(12):
            b = math.tau * j / 12
            dx, dy = 0.30 * math.cos(b), 0.30 * math.sin(b)
            pump.box((x + dx, y + dy, shoulder + 4.15), (0.045, 0.045, 1.65))
        for z in (2.95, 5.15):
            pump.tube((x, y, shoulder + z), (x, y, shoulder + z + 0.10), 0.36, 24)
            for j in range(8):
                b = math.tau * j / 8
                dx, dy = 0.32 * math.cos(b), 0.32 * math.sin(b)
                plate.tube((x + dx, y + dy, shoulder + z + 0.10),
                           (x + dx, y + dy, shoulder + z + 0.15), 0.018, 6)
        pivot = empty("Flywheel_PrimaryFlow_%02d_PIV" % i, (x, y, shoulder + 5.4))
        pivot["rs_bind"] = "plant.primary.pumpA"
        empty("RS_ROD_RotorAxis_Pump%02d_PIV" % i, (0, 0, 1), pivot)
        wheel = Geometry()
        wheel.ring(0, 0.23, 0.055, 0.035, 24)
        wheel.tube((0, 0, 0), (0, 0, 0.08), 0.045, 12)
        for j in range(6):
            b = math.tau * j / 6
            wheel.tube((0, 0, 0.025), (0.22 * math.cos(b), 0.22 * math.sin(b), 0.025),
                       0.015, 6)
        lods(wheel, "RS_PRI_Flywheel_%02d" % i, mats["M_BrushedSteel"], pivot)
        casing = Geometry()
        casing.ring(0, 0.39, 0.18, 0.045, 24, math.radians(120))
        casing_root = empty("RS_PRI_PumpCasing_%02d_PIV" % i, (x, y, shoulder + 2.7))
        lods(casing, "RS_PRI_PumpCasing_%02d" % i, mats["M_PanelSlate"], casing_root)
        impeller = empty("Flywheel_PrimaryFlow_Impeller%02d_PIV" % i, parent=casing_root)
        impeller["rs_bind"] = "plant.primary.pumpA"
        empty("RS_ROD_RotorAxis_Impeller%02d_PIV" % i, (0, 0, 1), impeller)
        vanes = Geometry()
        vanes.tube((0, 0, 0), (0, 0, 0.18), 0.055, 12)
        for j in range(8):
            b = math.tau * j / 8
            vanes.tube((0.055 * math.cos(b), 0.055 * math.sin(b), 0.06),
                       (0.30 * math.cos(b + 0.25), 0.30 * math.sin(b + 0.25), 0.10),
                       0.025, 5)
        lods(vanes, "RS_PRI_Impeller_%02d" % i, mats["M_BrushedSteel"], impeller)
    lods(pump, "RS_PRI_PumpMotor_01", mats["M_PanelSlate"])
    lods(plate, "RS_PRI_ServiceBolts_01", mats["M_BrushedSteel"])
    # Exposed U-tube bundle, inside the vessel above the core.
    tubes, sheets = Geometry(), Geometry()
    for x in range(-3, 4):
        for y in range(-2, 3):
            u, v = x * 0.12, y * 0.12 + 0.52
            tubes.tube((u, v, 5.2), (u, v, 7.4), 0.022, 6, caps=False)
            tubes.tube((u, v - 0.06, 5.2), (u, v - 0.06, 7.4), 0.022, 6, caps=False)
            tubes.tube((u, v, 7.4), (u, v - 0.06, 7.4), 0.022, 6)
    for z in (5.15, 6.0, 6.9):
        sheets.box((0, 0.49, z), (0.95, 0.75, 0.045))
    lods(tubes, "RS_SGN_TubeBundle_01", mats["M_BrushedSteel"])
    lods(sheets, "RS_SGN_TubeSheet_01", mats["M_ConsoleBeige"])
    # Small labelled display cutaway, not a second turbine hall.
    rotor_root = empty("RS_TUR_Display_01_PIV", (3.1, 0, 0.7))
    pivot = empty("ROTOR_Turbine_PIV", parent=rotor_root)
    pivot["rs_bind"] = "plant.turb.speed"
    empty("RS_ROD_RotorAxis_Turbine_PIV", (0, 0, 1), pivot)
    rotor = Geometry()
    rotor.tube((0, 0, -0.5), (0, 0, 0.5), 0.07, 12)
    for z in (-0.3, 0, 0.3):
        rotor.ring(z, 0.32, 0.045, 0.12, 24)
        for i in range(16):
            a = math.tau * i / 16
            rotor.tube((0.14 * math.cos(a), 0.14 * math.sin(a), z),
                       (0.34 * math.cos(a + 0.12), 0.34 * math.sin(a + 0.12), z + 0.04),
                       0.017, 4)
    lods(rotor, "RS_TUR_RotorStub_01", mats["M_BrushedSteel"], pivot)
    finish("RotatingEquipment")


if __name__ == "__main__":
    build()
