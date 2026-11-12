using System;
using System.Collections.Generic;
using ReactorSim.Csv;
using UnityEditor;
using UnityEngine;

public static class AssetManifestCheck
{
    public static void Validate(GameObject hall)
    {
        var manifest = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Blender/ReactorManifest.csv");
        if (manifest == null) throw new InvalidOperationException("Missing reactor manifest sidecar");
        var records = CsvReader.Parse(manifest.text);
        var header = records[0];
        int assetColumn = header.IndexOf("asset"), pathColumn = header.IndexOf("object_path"),
            idColumn = header.IndexOf("rs_id"), bindingColumn = header.IndexOf("rs_bind"),
            trianglesColumn = header.IndexOf("est_tris");
        if (Math.Min(assetColumn, Math.Min(pathColumn, Math.Min(idColumn,
                Math.Min(bindingColumn, trianglesColumn)))) < 0)
            throw new InvalidOperationException("Invalid manifest header");
        var bindings = new HashSet<string> { "", "plant.core", "plant.primary.pumpA", "plant.turb.speed",
            "plant.core.rods.bankA", "plant.core.rods.bankB", "plant.core.rods.bankC", "plant.core.rods.bankD" };
        var identities = new HashSet<string>();
        var paths = new HashSet<string>();
        int expectedTriangles = 0, actualTriangles = 0, meshes = 0;
        for (int i = 1; i < records.Count; i++)
        {
            var row = records[i];
            if (row.Count != header.Count || !identities.Add(row[idColumn]) || !bindings.Contains(row[bindingColumn]))
                throw new InvalidOperationException("Invalid manifest row " + i);
            string path = row[assetColumn] + "/" + row[pathColumn];
            if (!paths.Add(path)) throw new InvalidOperationException("Duplicate manifest path " + path);
            var t = hall.transform.Find(path);
            if (t == null) throw new InvalidOperationException("Unresolved manifest path " + path);
            int expected = int.Parse(row[trianglesColumn], System.Globalization.CultureInfo.InvariantCulture);
            expectedTriangles += expected;
            var filter = t.GetComponent<MeshFilter>();
            int actual = 0;
            if (filter != null && filter.sharedMesh != null)
            {
                for (int submesh = 0; submesh < filter.sharedMesh.subMeshCount; submesh++)
                    actual += (int)filter.sharedMesh.GetIndexCount(submesh) / 3;
                meshes++;
            }
            if (actual != expected) throw new InvalidOperationException("Triangle drift " + path + ": " + actual + "/" + expected);
            actualTriangles += actual;
        }
        foreach (string asset in HallAssetPlacer.AssetNames)
        {
            var root = hall.transform.Find(asset);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                string relative = AnimationUtility.CalculateTransformPath(t, hall.transform);
                if (!paths.Contains(relative)) throw new InvalidOperationException("Unregistered asset object " + relative);
            }
        }
        Debug.Log($"[AssetManifestCheck] PASS {records.Count - 1} identities, {meshes} meshes, {actualTriangles}/{expectedTriangles} resident triangles");
    }
}
