#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class AutoOpenBootstrap
{
    private const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";
    private const string AutoPlaySessionKey = "ReactorSim_AutoPlayEntered_v2";
    private static double _initTime;

    static AutoOpenBootstrap()
    {
        // Batchmode tools (scene builders, checks, imports, player builds) must never be dropped into
        // play mode: an editor left in play mode never exits and holds the project lock.
        if (Application.isBatchMode)
            return;
        _initTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += OnEditorUpdate;
    }

    private static void OnEditorUpdate()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        if (EditorApplication.timeSinceStartup - _initTime < 1.5)
            return;

        EditorApplication.update -= OnEditorUpdate;

        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var active = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(active.path) || active.name != "Bootstrap")
        {
            if (System.IO.File.Exists(BootstrapPath))
            {
                EditorSceneManager.OpenScene(BootstrapPath, OpenSceneMode.Single);
            }
        }

        if (!SessionState.GetBool(AutoPlaySessionKey, false))
        {
            SessionState.SetBool(AutoPlaySessionKey, true);
            EditorApplication.isPlaying = true;
        }
    }
}
#endif
