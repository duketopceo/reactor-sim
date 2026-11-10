using UnityEngine;

/// <summary>
/// U17 player settings (text scale, field of view, mouse sensitivity), persisted with PlayerPrefs and applied live:
/// consumers read the properties each frame (or compare Version) so a change in the pause menu takes effect at once.
/// Ranges and clamping are pure (OperatorMath). A blocked or missing PlayerPrefs leaves the defaults.
/// </summary>
public static class UserSettings
{
    private const string TextKey = "reactorsim.textScale";
    private const string FovKey = "reactorsim.fov";
    private const string SensKey = "reactorsim.mouseSensitivity";

    private const string ChecklistHiddenKey = "reactorsim.checklistHidden";

    private static bool loaded;
    private static bool checklistHidden;
    private static float textScale = OperatorMath.TextScaleDefault;
    private static float fov = OperatorMath.FovDefault;
    private static float sensitivity = OperatorMath.SensitivityDefault;

    /// <summary>Bumped on every change; cheap to poll.</summary>
    public static int Version { get; private set; }

    public static float TextScale
    {
        get { Load(); return textScale; }
        set { Load(); textScale = OperatorMath.ClampTextScale(value); Store(TextKey, textScale); }
    }

    public static float Fov
    {
        get { Load(); return fov; }
        set { Load(); fov = OperatorMath.ClampFov(value); Store(FovKey, fov); }
    }

    public static float MouseSensitivity
    {
        get { Load(); return sensitivity; }
        set { Load(); sensitivity = OperatorMath.ClampSensitivity(value); Store(SensKey, sensitivity); }
    }

    /// <summary>True when the player turned the startup checklist panel off (default: shown).</summary>
    public static bool ChecklistHidden
    {
        get { Load(); return checklistHidden; }
        set { Load(); checklistHidden = value; Store(ChecklistHiddenKey, value ? 1f : 0f); }
    }

    /// <summary>Scales a font size (points) by the text-scale setting.</summary>
    public static int Scaled(int size)
    {
        return Mathf.Max(8, Mathf.RoundToInt(size * TextScale));
    }

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        textScale = OperatorMath.ClampTextScale(Read(TextKey, OperatorMath.TextScaleDefault));
        fov = OperatorMath.ClampFov(Read(FovKey, OperatorMath.FovDefault));
        sensitivity = OperatorMath.ClampSensitivity(Read(SensKey, OperatorMath.SensitivityDefault));
        checklistHidden = Read(ChecklistHiddenKey, 0f) > 0.5f;
    }

    private static float Read(string key, float fallback)
    {
        try { return PlayerPrefs.GetFloat(key, fallback); }
        catch (System.Exception) { return fallback; }
    }

    private static void Store(string key, float v)
    {
        Version++;
        try
        {
            PlayerPrefs.SetFloat(key, v);
            PlayerPrefs.Save();
        }
        catch (System.Exception) { }
    }
}
