using System;
using System.IO;
using ReactorSim;
using UnityEditor;
using UnityEngine;

// Diagnostic close-up plus real gallery views: an attractive isolated asset is not proof of scene visibility.
public static class ReactorAssetRender
{
    public static void Run()
    {
        try
        {
            ControlRoomSceneBuilder.Build(false);
            var hall = GameObject.Find(HallBuilder.RootName);
            hall.SetActive(true);
            var motion = hall.GetComponent<ReactorAssetMotion>();
            motion.Bind();
            var snapshot = new PlantSnapshot { BankInsertion = new[] { 0.0, 0.35, 0.7, 1.0 }, FuelTempC = 350 };
            string requested = Environment.GetEnvironmentVariable("REACTOR_ASSET_RENDER_DIR");
            string directory = Path.GetFullPath(string.IsNullOrEmpty(requested)
                ? Path.Combine(Application.dataPath, "../../out/reactor-assets") : requested);
            Directory.CreateDirectory(directory);
            foreach (var lod in hall.GetComponentsInChildren<LODGroup>()) lod.ForceLOD(0);
            // Edit mode never runs Awake, so wake the water atmosphere (bubbles, core halo) by hand and let the bubbles settle.
            var atmosphere = hall.GetComponent<ReactorWaterAtmosphere>();
            typeof(ReactorWaterAtmosphere).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(atmosphere, null);
            float mz = HallDimensions.ModuleZ;
            var stand = HallSightline.EyeInHallSpace(HallSightline.Stand());
            var seat = HallSightline.EyeInHallSpace(HallSightline.Seat());
            float windowY = HallDimensions.RoomFloorAbovePoolFloor + HallDimensions.WindowSill + HallDimensions.WindowHeight * 0.5f;
            var windowCentre = new Vector3(-HallDimensions.VesselShiftX, windowY, -HallDimensions.PoolAxisFromWall);
            foreach (double power in new[] { 0.0, 1.0 })
            {
                motion.Apply(snapshot, power, false, 0);
                atmosphere.ApplyPower((float)power);
                atmosphere.Settle(9f);
                string suffix = power == 0 ? "shutdown" : "power";
                Render(hall, new Vector3(-0.9f, HallAssetPlacer.CoreBase + 2.5f, mz - 2.4f),
                    new Vector3(0, HallAssetPlacer.CoreBase + 2.1f, mz), 50,
                    Path.Combine(directory, "core-close-" + suffix + ".png"), true);
                Render(hall, new Vector3(-HallDimensions.VesselShiftX, HallDimensions.EyeAbovePoolFloor(HallDimensions.StandEyeHeight), -HallDimensions.PoolAxisFromWall - 1.5f),
                    new Vector3(0, HallAssetPlacer.CoreBase + 2, mz), 55,
                    Path.Combine(directory, "window-close-" + suffix + ".png"), false);
                Render(hall, new Vector3((float)stand[0], (float)stand[1], (float)stand[2]), windowCentre, 55,
                    Path.Combine(directory, "window-standing-" + suffix + ".png"), false);
                Render(hall, new Vector3((float)seat[0], (float)seat[1], (float)seat[2]), windowCentre, 55,
                    Path.Combine(directory, "window-seat-" + suffix + ".png"), false);
            }
            Debug.Log("[ReactorAssetRender] PASS " + directory);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogError("[ReactorAssetRender] FAIL " + ex);
            if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
        }
    }

    private static void Render(GameObject hall, Vector3 position, Vector3 target, float fov, string path, bool close)
    {
        var cameraObject = new GameObject("ReactorPreviewCamera");
        var camera = cameraObject.AddComponent<Camera>();
        var texture = new RenderTexture(1280, 960, 24) { antiAliasing = 4 };
        var previous = RenderTexture.active;
        Texture2D image = null;
        GameObject fill = null;
        try
        {
            cameraObject.transform.position = hall.transform.TransformPoint(position);
            cameraObject.transform.LookAt(hall.transform.TransformPoint(target));
            camera.fieldOfView = fov;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 150;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.04f, 0.06f);
            camera.targetTexture = texture;
            if (close)
            {
                // Explicitly diagnostic fill, never saved into the gameplay scene or used for hall-budget claims.
                camera.cullingMask = 1 << ControlRoomSceneBuilder.HallLayer;
                fill = new GameObject("DiagnosticFill");
                fill.transform.rotation = Quaternion.Euler(30, 25, 0);
                var light = fill.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 0.35f;
                light.cullingMask = camera.cullingMask;
            }
            camera.Render();
            RenderTexture.active = texture;
            image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            Debug.Log("[ReactorAssetRender] " + path);
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            if (fill != null) UnityEngine.Object.DestroyImmediate(fill);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
}
