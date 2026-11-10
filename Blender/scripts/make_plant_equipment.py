# ReactorSim Blender — Reactor Plant Equipment P01-P08 (U12)
import bpy
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lib_common import reset_scene, get_or_create_collection, add_box, add_cylinder, add_empty, link_to, save_blend
from lib_materials import build_materials

def build():
    reset_scene()
    mats = build_materials()
    M_STEEL = mats["M_BrushedSteel"]
    M_PANEL = mats["M_PanelSlate"]
    M_AMBER = mats["M_AmberGlow"]
    M_GREEN = mats["M_GreenGlow"]
    M_RED = mats["M_ScramRed"]
    M_CHER = mats["M_CherenkovGlow"]
    M_GLASS = mats["M_ContainmentGlass"]
    plant = get_or_create_collection("Plant")

    # P01 Reactor Pressure Vessel
    rpv = add_cylinder("ReactorPressureVessel", (0, 0, 3), 2.5, 6, M_STEEL, plant, vertices=48)
    head = add_cylinder("RPV_HeadDome", (0, 0, 6.5), 2.5, 1.0, M_STEEL, plant, vertices=48)
    flange = add_cylinder("RPV_HeadFlange", (0, 0, 6.0), 2.7, 0.4, M_STEEL, plant, vertices=48)
    for i in range(4):
        ang = i * 1.5708
        x = 1.2 * math.cos(ang) if (math := __import__("math")) else 0
        y = 1.2 * __import__("math").sin(ang)
        add_cylinder(f"BIND_BAR_BankInsertion_{i}_0_1", (x, y, 7.2), 0.08, 1.4, M_AMBER, plant, vertices=12)
    core_glow = add_cylinder("CoreGlow_Mesh", (0, 0, 2.5), 1.2, 2.0, M_CHER, plant, vertices=24)
    add_box("RPV_ViewWindow", (0, 2.5, 3.5), (2.0, 0.1, 1.2), M_GLASS, plant)
    add_box("RPV_SupportSkirt", (0, 0, 0.2), (4.0, 4.0, 0.4), M_PANEL, plant)

    # P02 Pressurizer
    add_cylinder("Pressurizer_Vessel", (4, 0, 3), 1.2, 4, M_STEEL, plant, vertices=32)
    add_cylinder("PZR_HeaterBoots", (4, 0, 1.2), 0.9, 0.4, M_RED, plant, vertices=24)
    sight = add_box("BIND_BAR_PrimaryLevelFrac_0_1", (5.25, 0, 3), (0.08, 0.08, 2.0), M_AMBER, plant)
    add_cylinder("PZR_SprayNozzle", (4, 0, 5.2), 0.15, 0.3, M_STEEL, plant, vertices=16)
    add_cylinder("PZR_PORV_Pipe", (4, -1.5, 5.5), 0.12, 2.0, M_STEEL, plant, vertices=16)
    porv = add_box("VFX_PORV_SteamJet_Anchor", (4, -2.5, 5.5), (0.2, 0.2, 0.2), None, plant)
    porv.name = "VFX_PORV_SteamJet_Anchor"

    # P03 Two Steam Generators
    for idx, dx in enumerate((-4, 1)):
        prefix = f"SteamGenerator_{idx+1}"
        add_cylinder(prefix, (dx, 5, 3), 1.1, 5, M_STEEL, plant, vertices=32)
        add_cylinder(f"{prefix}_Head", (dx, 5, 5.8), 1.1, 0.6, M_STEEL, plant, vertices=32)
        add_box(f"BIND_BAR_SecondaryLevelFrac_{idx}_0_1", (dx + 1.15, 5, 3), (0.08, 0.08, 2.5), M_AMBER, plant)
        add_cylinder(f"{prefix}_MainSteamLine", (dx, 6.5, 5.5), 0.12, 3.0, M_STEEL, plant, vertices=16)
        add_box(f"VFX_SGRelief_SteamJet_Anchor_{idx+1}", (dx, 7.5, 5.5), (0.2, 0.2, 0.2), None, plant)

    # P04 Coolant Pump
    add_cylinder("ReactorCoolantPump_1", (0, -3, 1.2), 0.7, 1.8, M_STEEL, plant, vertices=24)
    fw_pivot = add_empty("Flywheel_PrimaryFlow", (0, -3, 2.4), plant)
    add_cylinder("Flywheel", (0, 0, 0), 0.5, 0.2, M_AMBER, plant, parent=fw_pivot, vertices=24)

    # P05 Primary loop piping
    add_box("PrimaryLoop_HotLeg", (0, 2, 3), (0.5, 5, 0.5), M_STEEL, plant)
    add_box("PrimaryLoop_ColdLeg", (0, -1, 3), (0.5, 5, 0.5), M_STEEL, plant)
    add_box("PrimaryLoop_SurgeLine", (2, 0, 4), (0.3, 0.3, 4), M_STEEL, plant)
    add_box("PrimaryLoop_Crossover", (0, 0, 2), (4, 0.4, 0.4), M_STEEL, plant)
    add_cylinder("Flange_HotLeg_1", (0, 3, 3), 0.35, 0.15, M_STEEL, plant, vertices=16)

    # P06 Chemical dosing skid
    add_cylinder("ChemTank_BoricAcid", (-7, 0, 1), 0.6, 1.8, M_STEEL, plant, vertices=20)
    add_cylinder("ChemTank_NaOH", (-8.5, 0, 1), 0.6, 1.8, M_STEEL, plant, vertices=20)
    add_cylinder("Demineralizer_Column", (-7, -1.5, 1.5), 0.5, 2.8, M_STEEL, plant, vertices=20)
    add_box("ChemMeteringPump", (-7.75, -1.5, 0.4), (0.5, 0.4, 0.5), M_PANEL, plant)

    # P07 Turbine deck
    add_box("Turbine_Casing_HP", (8, 5, 1.5), (3.0, 1.2, 1.0), M_STEEL, plant)
    add_cylinder("Turbine_Shaft", (8, 5, 1.5), 0.2, 4.0, M_STEEL, plant, vertices=16, rotation=(0, 1.5708, 0))
    add_box("Generator_Stator", (11, 5, 1.5), (2.0, 1.4, 1.4), M_PANEL, plant)
    add_box("Condenser_Box", (8, 8, 1), (3.0, 1.5, 1.2), M_PANEL, plant)

    # P08 AO droid dock (Phase 2 stub)
    add_box("Bot_AO_Chassis", (6, -6, 0.5), (0.6, 0.5, 1.0), M_PANEL, plant)
    add_box("Bot_AO_Head", (6, -6, 1.2), (0.35, 0.30, 0.3), M_STEEL, plant)
    add_box("Bot_AO_ChargeLED", (6, -5.7, 0.9), (0.1, 0.05, 0.1), M_GREEN, plant)
    add_box("Bot_AO_DockPad", (6, -6, 0.05), (0.9, 0.9, 0.1), M_PANEL, plant)

    out = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "PlantEquipment.blend")
    save_blend(out)
    print("PLANTEQUIP_SAVED", out)
    print("PLANTEQUIP_OBJECTS", len(bpy.data.objects))

if __name__ == "__main__":
    import math
    build()
