using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

// Phase 2 import pipeline for Blender FBX: BTD2 axis -Z/Y up, 1BU=1m, materials
// auto-named M_* map by name. Baked trim sheets (Blender/scripts/bake_trim_sheets.py,
// synced to Assets/Blender/Textures/) are bound onto the matching imported materials:
//   T_<mat>_BaseColor -> _MainTex, T_<mat>_MetallicSmoothness -> _MetallicGlossMap,
//   T_<mat>_Emission -> _EmissionMap. Missing textures leave the FBX constants in place.
public class BlenderAssetPostprocessor : AssetPostprocessor
{
    const string ModelDir = "Assets/Blender/";
    const string TextureDir = "Assets/Blender/Textures/";

    static readonly string[] MasterMaterials =
    {
        "M_PanelSlate", "M_ConsoleBeige", "M_BrushedSteel", "M_DarkRubber",
        "M_CrtScreen", "M_GaugeFace", "M_AnnunciatorEmissive", "M_ContainmentGlass",
    };

    static readonly string[] MapSuffixes = { "_BaseColor", "_MetallicSmoothness", "_Emission" };

    static string TexPath(string material, string suffix) => TextureDir + "T_" + material + suffix + ".png";

    public void OnPreprocessModel()
    {
        // 1 Blender unit = 1 meter; FBX already authored so scale factor stays 1.
        // COL_* proxy meshes are swapped to colliders on prefab build (ControlRoomSceneBuilder
        // adds DiegeticInteractable + BoxCollider to INT_*); here we only enforce import flags.
        ModelImporter importer = assetImporter as ModelImporter;
        if (importer == null) return;
        importer.globalScale = 1.0f;
        importer.useFileScale = false;
        // colliders added selectively by ControlRoomSceneBuilder on INT_* (no ModelImporter flag here)
        importer.importAnimation = false;
        importer.importBlendShapes = false;

        if (!assetPath.StartsWith(ModelDir) || assetPath.StartsWith(TextureDir)) return;
        Debug.Log($"[BlenderAssetPostprocessor] preprocess model {assetPath}");
        // Re-import the FBX when a baked map changes, and make sure maps exist first.
        foreach (var mat in MasterMaterials)
            foreach (var suffix in MapSuffixes)
                context.DependsOnArtifact(TexPath(mat, suffix));
    }

    public void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(TextureDir)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 1024;
        // Metallic/smoothness is data, not color; the atlas and albedo/emission are sRGB.
        importer.sRGBTexture = !assetPath.EndsWith("_MetallicSmoothness.png");
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
    }

    // ImportViaMaterialDescription (this project's FBX mode) builds materials here.
    public void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
    {
        BindTrimSheet(material, description.materialName);
    }

    public void OnPostprocessMaterial(Material material)
    {
        BindTrimSheet(material, material.name);
    }

    void BindTrimSheet(Material material, string name)
    {
        if (!assetPath.StartsWith(ModelDir) || assetPath.StartsWith(TextureDir)) return;
        if (System.Array.IndexOf(MasterMaterials, name) < 0) return;

        var baseColor = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath(name, "_BaseColor"));
        if (baseColor != null)
        {
            material.SetTexture("_MainTex", baseColor);
            // The map carries the albedo; keep the tint neutral but preserve alpha (glass).
            material.color = new Color(1f, 1f, 1f, material.color.a);
        }

        var metalSmooth = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath(name, "_MetallicSmoothness"));
        if (metalSmooth != null && material.HasProperty("_MetallicGlossMap"))
        {
            material.SetTexture("_MetallicGlossMap", metalSmooth);
            material.SetFloat("_GlossMapScale", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f); // metallic map alpha
            material.EnableKeyword("_METALLICGLOSSMAP");
        }

        var emission = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath(name, "_Emission"));
        if (emission != null && material.HasProperty("_EmissionMap"))
        {
            // The map carries the hue (LDR); keep the imported HDR intensity on _EmissionColor.
            float intensity = Mathf.Max(1f, material.GetColor("_EmissionColor").maxColorComponent);
            material.SetTexture("_EmissionMap", emission);
            material.SetColor("_EmissionColor", Color.white * intensity);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        if (baseColor != null || metalSmooth != null || emission != null)
            Debug.Log($"[BlenderAssetPostprocessor] trim sheet bound: {name} ({assetPath})");
        else
            Debug.Log($"[BlenderAssetPostprocessor] no trim sheet yet for {name} ({assetPath})");
    }

    // Batchmode check: -executeMethod BlenderAssetPostprocessor.VerifyTrimSheets
    // Re-imports the Blender FBXes (textures first), then fails if any master
    // material present in them has no baked albedo/metallic map bound.
    public static void VerifyTrimSheets()
    {
        AssetDatabase.ImportAsset(TextureDir.TrimEnd('/'), ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceSynchronousImport);
        int failures = 0, checkedCount = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelDir.TrimEnd('/') }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(obj is Material m) || System.Array.IndexOf(MasterMaterials, m.name) < 0) continue;
                checkedCount++;
                var main = m.GetTexture("_MainTex");
                var ms = m.HasProperty("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null;
                bool ok = main != null && ms != null;
                if (!ok) failures++;
                Debug.Log($"[VerifyTrimSheets] {(ok ? "OK  " : "FAIL")} {path} :: {m.name} albedo={(main ? main.name : "-")} metal={(ms ? ms.name : "-")}");
            }
        }
        Debug.Log($"[VerifyTrimSheets] {checkedCount} master materials checked, {failures} failures");
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 && checkedCount > 0 ? 0 : 1);
    }
}
