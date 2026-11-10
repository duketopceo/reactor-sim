using UnityEngine;
using ReactorSim;
using ReactorSim.Ui;

// U8 HUD: thin ImGui readout of the active mission + latest advisory.
// Real values come from Core AssistantEngine; this is display only.
public class MissionHud : MonoBehaviour
{
    public PlantBridge bridge;
    public MissionRunner runner;
    public bool showHud = true;

    // Text sits over the 3D scene, so it gets the themed backing panel (R8). Built once, not per frame.
    private GUIStyle panelStyle, labelStyle;

    private void EnsureStyles()
    {
        if (panelStyle != null) return;
        panelStyle = ThemeColors.BoxStyle(Theme.SurfaceTooltip, Theme.FontBody, Theme.TextHeading);
        panelStyle.fontStyle = FontStyle.Bold;
        panelStyle.padding = new RectOffset(10, 10, 8, 8);
        labelStyle = ThemeColors.LabelStyle(Theme.FontCaption, Theme.TextBody);
    }

    private void Awake()
    {
        if (bridge == null)
            bridge = FindAnyObjectByType<PlantBridge>();
        if (runner == null)
        {
            runner = GetComponent<MissionRunner>();
            if (runner == null) runner = gameObject.AddComponent<MissionRunner>();
            runner.bridge = bridge;
        }
    }

    private void OnGUI()
    {
        if (!showHud || bridge == null || bridge.Sim == null) return;
        var eng = bridge.Sim.Plant.Assistant;
        if (eng == null) return;
        EnsureStyles();
        GUILayout.BeginArea(new Rect(10, 10, 440, 180), "Mission (U8)", panelStyle);
        if (eng.CurrentMission != null)
        {
            GUILayout.Label($"{eng.CurrentMission.Title} — {eng.CurrentMission.Summary}", labelStyle);
            int s = eng.CurrentMission.CurrentStepIndex;
            if (s >= 0 && s < eng.CurrentMission.Steps.Count)
                GUILayout.Label($"Step: {eng.CurrentMission.Steps[s].Title}", labelStyle);
            if (eng.CurrentMission.Completed)
                GUILayout.Label("MISSION COMPLETE", labelStyle);
        }
        if (eng.LatestAdvisory != null)
            GUILayout.Label(eng.LatestAdvisory.FormatTerminalLine(), labelStyle);
        GUILayout.Label($"Tier: {eng.Tier}", labelStyle);
        GUILayout.EndArea();
    }
}
