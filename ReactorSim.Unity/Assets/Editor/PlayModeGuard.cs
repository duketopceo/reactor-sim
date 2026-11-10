#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Batchmode play-mode checks (RoundTripCheck, RoomRender) must not see a domain reload after the scene's Awake:
/// a script compile mid-play reloads the domain, Unity restores only serialized fields, and runtime state such as
/// PlantBridge.Sim (an auto-property) comes back null with no second Awake. Call Prepare() right before
/// EditorApplication.isPlaying = true: it imports and compiles anything pending first, and tells the editor to
/// defer any later recompile until play mode ends.
/// </summary>
public static class PlayModeGuard
{
    // Unity's Preferences > General > "Script Changes While Playing": 1 = Recompile After Finished Playing.
    private const string ScriptCompilationDuringPlayKey = "ScriptCompilationDuringPlay";
    private const int RecompileAfterFinishedPlaying = 1;

    public static void Prepare()
    {
        EditorPrefs.SetInt(ScriptCompilationDuringPlayKey, RecompileAfterFinishedPlaying);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }
}
#endif
