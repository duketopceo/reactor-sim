using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// Build a regenerated room as a separate preview, preserving the working Bootstrap scene.
public static class PlantWallPreviewBuild
{
    public static void Run()
    {
        try
        {
            string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            string folderName = "PlantWallPreview_" + stamp;
            string folder = "Assets/" + folderName;
            string scenePath = folder + "/Bootstrap.unity";
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string output = Path.Combine(repo, "out", "plant-wall-preview-" + stamp, "ReactorSim.exe");
            if (File.Exists(scenePath) || File.Exists(output))
                throw new InvalidOperationException("Preview scene or executable already exists; choose a fresh output location.");
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", folderName);
            var scene = ControlRoomSceneBuilder.Build(false);
            if (!scene.IsValid() || !scene.GetRootGameObjects().Any(root => root.name == "ControlRoom"))
                throw new InvalidOperationException("Regenerated control room is missing.");
            if (!scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Any(t => t.name == "WholePlantOverview"))
                throw new InvalidOperationException("Plant overview was not added to the regenerated scene.");
            if (!EditorSceneManager.SaveScene(scene, scenePath, true))
                throw new InvalidOperationException("Could not save the preview scene.");
            AssetDatabase.SaveAssets();
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled)
                .Select(s => s.path == "Assets/Scenes/Bootstrap.unity" ? scenePath : s.path).ToArray();
            if (!scenes.Contains(scenePath)) throw new InvalidOperationException("Bootstrap is not enabled in build settings.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = scenes, locationPathName = output, target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Player build failed: " + report.summary.result);
            Debug.Log("[PlantWallPreviewBuild] OK " + output);
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogError("[PlantWallPreviewBuild] FAIL " + ex);
            EditorApplication.Exit(1);
        }
    }
}
