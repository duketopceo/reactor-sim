using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using TMPro;

// One-shot, non-interactive import of the TextMeshPro Essential Resources (settings, default SDF font, shaders)
// into Assets/TextMesh Pro. TMP's own menu importer opens a window, which does not run in batchmode, so this
// imports the .unitypackage that ships inside the uGUI package directly.
//   "<Unity.exe>" -batchmode -nographics -projectPath ReactorSim.Unity -executeMethod TmpBootstrap.ImportEssentials -logFile out/tmp-import.log
// Exits 0 once Assets/TextMesh Pro/Resources/TMP Settings.asset exists, 1 on failure or timeout. Commit the result.
//
// GenerateFonts then builds the UI font SDF assets from the open-licensed files in Assets/Fonts (Inter, IBM Plex Mono;
// SIL OFL 1.1, licenses beside them), idempotent: an asset that already exists is left alone.
//   "<Unity.exe>" -batchmode -nographics -projectPath ReactorSim.Unity -executeMethod TmpBootstrap.GenerateFonts -logFile out/tmp-fonts.log
// Output goes to Assets/TextMesh Pro/Resources/Fonts & Materials (UiFonts loads it by name). Commit the .asset and .meta files.
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

    private const string FontsDir = "Assets/Fonts";
    private const string FontsOutDir = "Assets/TextMesh Pro/Resources/Fonts & Materials";

    // Printable ASCII plus the few symbols the UI prints (degree, plus-minus, dashes, arrows, comparison).
    private const string ExtraGlyphs = "\u00B0\u00B1\u00D7\u00B7\u2013\u2014\u2026\u2022\u2265\u2264\u2190\u2191\u2192\u2193";

    public static void GenerateFonts()
    {
        if (!File.Exists(SettingsAsset))
        {
            Debug.LogError("[TmpBootstrap] FAIL TMP Essential Resources missing; run TmpBootstrap.ImportEssentials first");
            Finish(1);
            return;
        }
        int failures = 0;
        failures += BuildFont("Inter-Regular", UiFonts.BodyName);
        failures += BuildFont("Inter-Bold", UiFonts.BoldName);
        failures += BuildFont("IBMPlexMono-Regular", UiFonts.MonoName);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (failures == 0) Debug.Log("[TmpBootstrap] OK UI font assets ready in " + FontsOutDir);
        else Debug.LogError("[TmpBootstrap] FAIL " + failures + " font asset(s) not built");
        Finish(failures == 0 ? 0 : 1);
    }

    private static int BuildFont(string ttfName, string assetName)
    {
        string ttfPath = FontsDir + "/" + ttfName + ".ttf";
        string assetPath = FontsOutDir + "/" + assetName + ".asset";
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath) != null)
        {
            Debug.Log("[TmpBootstrap] OK " + assetName + " already present");
            return 0;
        }
        var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        if (font == null)
        {
            Debug.LogError("[TmpBootstrap] FAIL font file not imported: " + ttfPath);
            return 1;
        }
        Directory.CreateDirectory(FontsOutDir);
        // Dynamic population keeps the source font in the asset so a glyph missing from the pre-built set still draws;
        // the pre-built set (ASCII and the symbols above) means normal play never rasterizes at runtime.
        var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        if (fa == null)
        {
            Debug.LogError("[TmpBootstrap] FAIL CreateFontAsset returned null for " + ttfPath);
            return 1;
        }
        fa.name = assetName;
        AssetDatabase.CreateAsset(fa, assetPath);
        fa.material.name = assetName + " Material";
        AssetDatabase.AddObjectToAsset(fa.material, fa);

        var chars = new System.Text.StringBuilder();
        for (char c = ' '; c <= '~'; c++) chars.Append(c);
        chars.Append(ExtraGlyphs);
        string missing;
        fa.TryAddCharacters(chars.ToString(), out missing);
        if (!string.IsNullOrEmpty(missing)) Debug.LogWarning("[TmpBootstrap] " + assetName + " lacks glyphs: " + missing);

        int n = 0;
        foreach (var tex in fa.atlasTextures)
        {
            if (tex == null || EditorUtility.IsPersistent(tex)) continue;
            tex.name = assetName + " Atlas" + (n++ == 0 ? "" : " " + n);
            AssetDatabase.AddObjectToAsset(tex, fa);
        }
        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        Debug.Log("[TmpBootstrap] OK built " + assetPath);
        return 0;
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
