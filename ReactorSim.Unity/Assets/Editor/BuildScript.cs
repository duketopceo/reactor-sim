using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Batchmode player build:
//   "<Unity.exe>" -batchmode -nographics -projectPath ReactorSim.Unity
//       -executeMethod BuildScript.BuildWindows -buildTarget StandaloneWindows64 [-buildVersion x.y.z] -quit -logFile out/unity-build.log
// -buildVersion stamps the release tag; the log carries "[BuildScript] version=<x.y.z> source=arg|fallback".
// Output: <repo root>/out/build/ReactorSim.exe (the Unity project sits one level below the repo root).
//
// The player always embeds a scene regenerated from the builders in this build, never the committed Bootstrap.unity
// as-is: a stale committed scene once shipped a release with the pre-module hall. The log carries
// "[BuildScript] scene rebuilt ... hall=ReactorHall" once the regenerated scene has been verified.
public static class BuildScript
{
    public static void BuildWindows()
    {
        try { RebuildBootstrapScene(); }
        catch (Exception ex)
        {
            Finish(false, "[BuildScript] FAIL scene rebuild: " + ex.Message);
            return;
        }

        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        string outDir = Path.Combine(repoRoot, "out", "build");
        string exe = Path.Combine(outDir, PlayerSettings.productName + ".exe");
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd.HHmm");

        if (scenes.Length == 0)
        {
            Finish(false, "[BuildWindows] FAIL no enabled scenes in EditorBuildSettings");
            return;
        }

        // Release builds pass -buildVersion <x.y.z> (the tag); otherwise the current bundleVersion's base is kept.
        string version, versionSource;
        try { version = BuildVersionArgs.Resolve(Environment.GetCommandLineArgs(), PlayerSettings.bundleVersion.Split('+')[0], out versionSource); }
        catch (FormatException ex)
        {
            Finish(false, "[BuildScript] FAIL " + ex.Message);
            return;
        }
        Debug.Log("[BuildScript] version=" + version + " source=" + versionSource);

        Directory.CreateDirectory(outDir);
        PlayerSettings.bundleVersion = version + "+" + stamp;

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = exe,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        BuildSummary summary = BuildPipeline.BuildPlayer(options).summary;
        bool ok = summary.result == BuildResult.Succeeded;
        Finish(ok, $"[BuildWindows] {(ok ? "OK  " : "FAIL")} {exe} version={PlayerSettings.bundleVersion} scenes={scenes.Length} result={summary.result} errors={summary.totalErrors} size={summary.totalSize}");
    }

    // Regenerates Bootstrap.unity with the same builder scripts/ci.sh runs, then refuses to continue unless the SMR
    // module hall (HallBuilder.RootName) is in the saved scene.
    static void RebuildBootstrapScene()
    {
        var scene = ControlRoomSceneBuilder.Build(true);
        if (!scene.IsValid()) throw new InvalidOperationException("Bootstrap scene did not open");
        bool hall = scene.GetRootGameObjects().Any(g => g.name == HallBuilder.RootName);
        if (!hall) throw new InvalidOperationException("regenerated scene has no " + HallBuilder.RootName + " root");
        Debug.Log("[BuildScript] scene rebuilt " + scene.path + " roots=" + scene.rootCount + " hall=" + HallBuilder.RootName);
    }

    static void Finish(bool ok, string message)
    {
        if (ok) Debug.Log(message); else Debug.LogError(message);
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }
}
