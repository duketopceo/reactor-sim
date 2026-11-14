using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ReactorSim;

public static class HallAssetPlacer
{
    public static readonly string[] AssetNames = { "ReactorCore", "VesselInternals", "RotatingEquipment" };
    public static readonly float CoreBase = HallDimensions.VesselBase + 3f;

    public static void Build(GameObject hall)
    {
        foreach (string name in AssetNames)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Blender/" + name + ".fbx");
            if (asset == null) throw new InvalidOperationException("Missing reactor FBX: " + name);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, hall.transform);
            instance.name = name;
            instance.transform.localPosition = Vector3.up * CoreBase;
            // Authored -Y faces Unity +Z after FBX conversion; the hall window is towards -Z.
            instance.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var levels = new List<Renderer>[] { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
            Material glowMaterial = null;
            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                for (int i = 0; i < 3; i++)
                    if (renderer.name.EndsWith("_LOD" + i, StringComparison.Ordinal))
                        levels[i].Add(renderer);
                if (renderer.name.StartsWith("RS_VES_CoreGlow_", StringComparison.Ordinal))
                {
                    if (glowMaterial == null)
                    {
                        glowMaterial = new Material(Shader.Find("Standard")) { name = "M_ReactorCoreEmission" };
                        glowMaterial.color = new Color(0.025f, 0.06f, 0.11f);
                        glowMaterial.EnableKeyword("_EMISSION");
                        glowMaterial.SetColor("_EmissionColor", new Color(0.05f, 0.2f, 0.6f));
                        glowMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    }
                    renderer.sharedMaterial = glowMaterial;
                }
            }
            var lod = instance.GetComponent<LODGroup>();
            if (lod == null) lod = instance.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.28f, levels[0].ToArray()),
                new LOD(0.09f, levels[1].ToArray()), new LOD(0.008f, levels[2].ToArray()) });
            lod.RecalculateBounds();
        }
        var lightObject = new GameObject("RS_VES_CoreLight_PIV");
        lightObject.transform.SetParent(hall.transform, false);
        lightObject.transform.localPosition = new Vector3(0, CoreBase + 2.2f, -0.65f);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.18f, 0.52f, 1f);
        light.range = 9f;
        light.intensity = 0.6f;
        light.shadows = LightShadows.None;
        hall.AddComponent<ReactorAssetMotion>();
    }

    public static bool IsMoving(Transform transform)
    {
        while (transform != null)
        {
            if (transform.name.StartsWith("ROD_BANK_", StringComparison.Ordinal) ||
                transform.name.StartsWith("Flywheel_PrimaryFlow_", StringComparison.Ordinal) ||
                transform.name == "ROTOR_Turbine_PIV") return true;
            transform = transform.parent;
        }
        return false;
    }
}
