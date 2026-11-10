using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using TMPro;

// One-shot, non-interactive import of the TextMeshPro Essential Resources (settings, default SDF font, shaders)
// into Assets/TextMesh Pro. TMP's own menu importer opens a window, which does not run in batchmode, so this
// imports the .unitypackage that ships inside the uGUI package directly.
//   "<Unity.exe>" -batchmode -nographics -projectPath ReactorSim.Unity -executeMethod TmpBootstrap.ImportEssentials -logFile out/tmp-import.log
// Exits 0 once Assets/TextMesh Pro/Resources/TMP Settings.asset exists, 1 on failure or timeout. Commit the result.
[InitializeOnLoad]
public static class TmpBootstrap
{
    // The package import recompiles scripts, and the domain reload drops the EditorApplication.update
    // subscription; SessionState survives the reload so the poll (and the batchmode exit) is re-armed.
    private const string PendingKey = "ReactorSim_TmpBootstrap_Pending";
    private const string StartKey = "ReactorSim_TmpBootstrap_Start";

    static TmpBootstrap()
    {
        if (!SessionState.GetBool(PendingKey, false)) return;
        startTime = SessionState.GetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
        EditorApplication.update += Poll;
    }

    private const string SettingsAsset = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
    private const double TimeoutSeconds = 180.0;

    private static double startTime;
    private static bool importFinished;
    private static bool importFailed;

    public static void ImportEssentials()
    {
        if (File.Exists(SettingsAsset))
        {
            Debug.Log("[TmpBootstrap] OK TMP Essential Resources already present: " + SettingsAsset);
            Finish(0);
            return;
        }

        string package = FindPackage();
        if (package == null)
        {
            Debug.LogError("[TmpBootstrap] FAIL could not find 'TMP Essential Resources.unitypackage' in the uGUI package");
            Finish(1);
            return;
        }
        Debug.Log("[TmpBootstrap] importing " + package);

        startTime = EditorApplication.timeSinceStartup;
        SessionState.SetBool(PendingKey, true);
        SessionState.SetFloat(StartKey, (float)startTime);
        AssetDatabase.importPackageCompleted += OnCompleted;
        AssetDatabase.importPackageFailed += OnFailed;
        AssetDatabase.importPackageCancelled += OnCancelled;
        EditorApplication.update += Poll;
        AssetDatabase.ImportPackage(package, false);
    }

    private static string FindPackage()
    {
        var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
        if (info == null)
            info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui");
        if (info == null) return null;
        string path = Path.Combine(info.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
        return File.Exists(path) ? path : null;
    }

    private static void OnCompleted(string packageName)
    {
        importFinished = true;
    }

    private static void OnFailed(string packageName, string errorMessage)
    {
        importFailed = true;
        Debug.LogError("[TmpBootstrap] FAIL import failed: " + errorMessage);
    }

    private static void OnCancelled(string packageName)
    {
        importFailed = true;
        Debug.LogError("[TmpBootstrap] FAIL import cancelled");
    }

    // Polled each editor tick: succeeds once the settings asset is on disk, whether or not the completion
    // callback survived a domain reload.
    private static void Poll()
    {
        bool present = File.Exists(SettingsAsset);
        if (importFailed)
        {
            Cleanup();
            Finish(1);
        }
        else if (present && (importFinished || EditorApplication.timeSinceStartup - startTime > 30.0) && !EditorApplication.isUpdating)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TmpBootstrap] OK imported TMP Essential Resources: " + SettingsAsset);
            Cleanup();
            Finish(0);
        }
        else if (EditorApplication.timeSinceStartup - startTime > TimeoutSeconds)
        {
            Debug.LogError("[TmpBootstrap] FAIL timed out waiting for " + SettingsAsset);
            Cleanup();
            Finish(1);
        }
    }

    private static void Cleanup()
    {
        AssetDatabase.importPackageCompleted -= OnCompleted;
        AssetDatabase.importPackageFailed -= OnFailed;
        AssetDatabase.importPackageCancelled -= OnCancelled;
        EditorApplication.update -= Poll;
        SessionState.EraseBool(PendingKey);
        SessionState.EraseFloat(StartKey);
    }

    private static void Finish(int code)
    {
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }
}
