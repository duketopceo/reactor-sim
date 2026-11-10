using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// U11: generates Assets/Scenes/Menu.unity (a camera + MainMenu; the screen itself is built in code at Start)
// and puts it first in the build settings, Bootstrap second. Batchmode:
//   "<Unity.exe>" -batchmode -nographics -projectPath ReactorSim.Unity
//       -executeMethod MenuSceneBuilder.BuildAllScenes -quit -logFile out/unity-scenes.log
// BuildAllScenes = the Phase-2 control room build (adds MissionHud/MissionRunner) + the menu scene + build settings.
// MenuSceneBuilder.BuildMenuScene alone does just the menu scene and build settings.
public static class MenuSceneBuilder
{
    public const string MenuPath = "Assets/Scenes/Menu.unity";
    public const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";

    [MenuItem("ReactorSim/Build Menu Scene")]
    public static void BuildMenuScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("MenuCamera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.04f, 0.06f, 0.08f, 1f);
        camGo.AddComponent<AudioListener>();

        var menuGo = new GameObject("MainMenu");
        menuGo.AddComponent<MainMenu>();

        if (!EditorSceneManager.SaveScene(scene, MenuPath))
            throw new InvalidOperationException("could not save " + MenuPath);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(MenuPath, true),
            new EditorBuildSettingsScene(BootstrapPath, true)
        };
        AssetDatabase.SaveAssets();
        Debug.Log("[MenuSceneBuilder] saved " + MenuPath + "; build settings: Menu, Bootstrap.");
    }

    /// <summary>Batchmode entry: control room scene (with MissionHud/MissionRunner), then the menu scene.</summary>
    public static void BuildAllScenes()
    {
        try
        {
            ControlRoomSceneBuilder.BuildPhase2Scene();
            BuildMenuScene();
        }
        catch (Exception ex)
        {
            Debug.LogError("[MenuSceneBuilder] FAIL " + ex);
            EditorApplication.Exit(1);
        }
    }
}
