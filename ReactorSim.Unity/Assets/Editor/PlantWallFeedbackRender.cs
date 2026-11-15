using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Capture the rebuilt wall from the authored seated camera without saving the user's scene.
public static class PlantWallFeedbackRender
{
    public static void Run()
    {
        try
        {
            var scene = ControlRoomSceneBuilder.Build(false);
            var room = ControlRoomSceneBuilder.FindRoot();
            var wall = ControlRoomSceneBuilder.FindDeep(room.transform, "WholePlantOverview");
            var seat = ControlRoomSceneBuilder.FindDeep(room.transform, "CAM_Stand_Center");
            if (!scene.IsValid() || wall == null || seat == null) throw new InvalidOperationException("Room, wall or seat missing");
            var bounds = new Bounds();
            bool first = true;
            foreach (var r in wall.GetComponentsInChildren<Renderer>())
            {
                if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
            }
            var go = new GameObject("FeedbackCamera");
            var cam = go.AddComponent<Camera>();
            go.transform.position = seat.position;
            go.transform.LookAt(bounds.center);
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 100f;
            var target = new RenderTexture(1600, 900, 24);
            cam.targetTexture = target;
            var previous = RenderTexture.active;
            cam.Render();
            RenderTexture.active = target;
            var img = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            img.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            img.Apply();
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../out/plant-wall-feedback"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "wall-seat.png"), img.EncodeToPNG());
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(img);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(go);
            Debug.Log("[PlantWallFeedbackRender] PASS " + dir);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("[PlantWallFeedbackRender] FAIL " + e);
            EditorApplication.Exit(1);
        }
    }
}
