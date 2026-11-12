# ReactorSim Blender — 8 shared PBR materials (U10)
import bpy
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lib_common import make_mat, reset_scene

def build_materials():
    materials = {}
    # 1. Slate panel housing
    materials["M_PanelSlate"] = make_mat("M_PanelSlate", (0.09, 0.12, 0.16), metallic=0.25, roughness=0.55)
    # 2. Console beige-grey (MIL-STD console finish)
    materials["M_ConsoleBeige"] = make_mat("M_ConsoleBeige", (0.62, 0.60, 0.54), metallic=0.10, roughness=0.65)
    # 3. Brushed steel / vessel steel
    materials["M_BrushedSteel"] = make_mat("M_BrushedSteel", (0.55, 0.58, 0.62), metallic=0.85, roughness=0.35)
    # 4. Dark rubber / operator surface
    materials["M_DarkRubber"] = make_mat("M_DarkRubber", (0.07, 0.07, 0.08), metallic=0.0, roughness=0.90)
    # 5. CRT phosphor screen (emissive-ready for RenderTexture blit)
    materials["M_CrtScreen"] = make_mat("M_CrtScreen", (0.02, 0.04, 0.05), metallic=0.0, roughness=0.20, emission_color=(0.10, 0.85, 0.95), emission_strength=1.2)
    # 6. Gauge faceplate (matte white-ish for analog dials)
    materials["M_GaugeFace"] = make_mat("M_GaugeFace", (0.85, 0.86, 0.82), metallic=0.05, roughness=0.40)
    # 7. Dynamic annunciator emissive (unity uses MaterialPropertyBlock on _EmissionColor)
    materials["M_AnnunciatorEmissive"] = make_mat("M_AnnunciatorEmissive", (0.05, 0.05, 0.05), metallic=0.0, roughness=0.30, emission_color=(0.95, 0.20, 0.20), emission_strength=0.0)
    # 8. Tinted containment observation glass
    m_glass = make_mat("M_ContainmentGlass", (0.55, 0.75, 0.85), metallic=0.10, roughness=0.05)
    m_glass.surface_render_method = 'DITHERED'
    materials["M_ContainmentGlass"] = m_glass
    # Extra named palette entries used by kit builders
    materials["M_AmberGlow"] = make_mat("M_AmberGlow", (0.10, 0.08, 0.02), emission_color=(0.95, 0.68, 0.12), emission_strength=1.0)
    materials["M_GreenGlow"] = make_mat("M_GreenGlow", (0.02, 0.08, 0.03), emission_color=(0.16, 0.78, 0.42), emission_strength=1.0)
    materials["M_CherenkovGlow"] = make_mat("M_CherenkovGlow", (0.01, 0.03, 0.08), emission_color=(0.18, 0.55, 1.00), emission_strength=2.0)
    materials["M_ScramRed"] = make_mat("M_ScramRed", (0.45, 0.05, 0.05), metallic=0.20, roughness=0.35)
    materials["M_FloorDark"] = make_mat("M_FloorDark", (0.10, 0.11, 0.13), metallic=0.05, roughness=0.55)
    materials["M_GaugeNeedle"] = make_mat("M_GaugeNeedle", (0.75, 0.10, 0.10), metallic=0.0, roughness=0.40)
    # 9. Painted bezel / instrument trim (dials, CRT bezels, switch collars)
    materials["M_BezelBlack"] = make_mat("M_BezelBlack", (0.04, 0.05, 0.06), metallic=0.75, roughness=0.40)
    # Assert all 8 core materials exist
    required = ["M_PanelSlate", "M_ConsoleBeige", "M_BrushedSteel", "M_DarkRubber", "M_CrtScreen", "M_GaugeFace", "M_AnnunciatorEmissive", "M_ContainmentGlass"]
    for r in required:
        assert r in materials, f"Missing material {r}"
    print(f"LIB_MATERIALS_OK {len(materials)} materials")
    return materials

if __name__ == "__main__":
    reset_scene()
    build_materials()
