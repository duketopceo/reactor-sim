using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using ReactorSim.Ui;

/// <summary>
/// U11 escape menu for the control room: resume, reset sandbox, master volume, alarm mute, quit to menu.
/// Opening pauses the sim clock through the Player-class PauseClock intent (it restores the prior state on
/// close, so an already-paused sim stays paused). While open, the 3D room's mouse look and control clicks
/// are switched off so clicks on this menu do not also reach the controls behind it.
/// Reset reloads the scene (a fresh Simulation, cold shutdown): no Dev intent and no DevMode flip involved.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    private PlantBridge bridge;
    private GameObject root;
    private bool open;
    private bool pausedByMenu;
    private bool alarmMuted;
    private float nextAudioScan;
    private ProceduralPlantAudio plantAudio;
    private readonly RoomInputGate inputGate = new RoomInputGate();

    private void Awake()
    {
        bridge = GetComponent<PlantBridge>();
        if (bridge == null) bridge = FindAnyObjectByType<PlantBridge>();
    }

    private void Update()
    {
        // Escape leaves a focused screen first; it must not also open this menu (either Update order works).
        if (Input.GetKeyDown(KeyCode.Escape) && !ScreenFocus.ConsumesEscape && !DiegeticInteractable.InputConsumedThisFrame)
        {
            if (open) Close(); else Open();
        }
        if (alarmMuted) ApplyAlarmMute();
    }

    /// <summary>True while the escape menu is showing (the operator tablet will not raise over it).</summary>
    public static bool IsOpen { get; private set; }

    private void Open()
    {
        if (root == null) Build();
        // The tablet closes first so it restores the room's input before this menu suspends it.
        var tablet = FindAnyObjectByType<OperatorTablet>();
        if (tablet != null) tablet.Lower();
        open = true;
        IsOpen = true;
        root.SetActive(true);
        pausedByMenu = bridge != null && !bridge.Paused;
        if (pausedByMenu) bridge.Paused = true;

        inputGate.Suspend();
    }

    private void Close()
    {
        open = false;
        IsOpen = false;
        if (root != null) root.SetActive(false);
        if (pausedByMenu && bridge != null) bridge.Paused = false;
        pausedByMenu = false;
        inputGate.Restore();
    }

    private void Build()
    {
        RuntimeUi.EnsureEventSystem();
        var canvas = RuntimeUi.CreateCanvas("PauseMenuCanvas", 100, transform);
        root = canvas.gameObject;
        RuntimeUi.Stretch(canvas.transform, "Dim", ThemeColors.ScrimDim);
        var col = RuntimeUi.Column(canvas.transform, "Column", 660f, ThemeColors.SurfacePanel);
        var dim = ThemeColors.TextDim;
        bool sandbox = GameSession.Mode == StartMode.Sandbox;

        RuntimeUi.MakeHeader(col, "PAUSED", TextRole.Display);
        RuntimeUi.MakeButton(col, "Resume", Close);
        RuntimeUi.MakeButton(col, sandbox ? "Reset sandbox (cold shutdown)" : "Restart mission", ResetSession);
        RuntimeUi.MakeDivider(col);
        RuntimeUi.MakeLabel(col, "Master volume", TextRole.Body, TextAnchor.MiddleLeft, ThemeColors.TextBody);
        RuntimeUi.MakeSlider(col, AudioListener.volume, v => AudioListener.volume = v);
        RuntimeUi.MakeToggle(col, "Mute alarm sounds", alarmMuted, v =>
        {
            alarmMuted = v;
            ApplyAlarmMute();
        });
        RuntimeUi.MakeDivider(col);
        if (sandbox)
            RuntimeUi.MakeToggle(col, "Hide startup checklist panel", UserSettings.ChecklistHidden, v => UserSettings.ChecklistHidden = v);
        RuntimeUi.MakeToggle(col, "Invert look (Y axis)", UserSettings.InvertY, v => UserSettings.InvertY = v);
        AddSetting(col, "Text size (menus, tablet, clipboard)", OperatorMath.TextScaleMin, OperatorMath.TextScaleMax,
            UserSettings.TextScale, v => UserSettings.TextScale = v);
        AddSetting(col, "Field of view", OperatorMath.FovMin, OperatorMath.FovMax,
            UserSettings.Fov, v => UserSettings.Fov = v);
        AddSetting(col, "Mouse sensitivity", OperatorMath.SensitivityMin, OperatorMath.SensitivityMax,
            UserSettings.MouseSensitivity, v => UserSettings.MouseSensitivity = v);
        RuntimeUi.MakeDivider(col);
        RuntimeUi.MakeButton(col, "Quit to menu", QuitToMenu);
        RuntimeUi.MakeLabel(col, "The sandbox is not saved. Quitting to the menu discards this plant.", TextRole.Caption,
            TextAnchor.MiddleCenter, dim);
        root.SetActive(false);
    }

    // A labelled slider over [lo, hi]; the setting is saved and applied live (UserSettings).
    private static void AddSetting(Transform col, string label, float lo, float hi, float value, System.Action<float> apply)
    {
        RuntimeUi.MakeLabel(col, label, TextRole.Body, TextAnchor.MiddleLeft, ThemeColors.TextBody);
        RuntimeUi.MakeSlider(col, OperatorMath.ToSlider(value, lo, hi), t => apply(OperatorMath.FromSlider(t, lo, hi)));
    }

    // Alarm sounds belong to ProceduralPlantAudio (klaxon loop + one-shot sfx source); muted from outside.
    private void ApplyAlarmMute()
    {
        if (plantAudio == null && Time.unscaledTime >= nextAudioScan)
        {
            plantAudio = FindAnyObjectByType<ProceduralPlantAudio>();
            nextAudioScan = Time.unscaledTime + 1f;
        }
        if (plantAudio == null) return;
        if (plantAudio.scramKlaxon != null) plantAudio.scramKlaxon.mute = alarmMuted;
        if (plantAudio.sfxSource != null) plantAudio.sfxSource.mute = alarmMuted;
    }

    private void ResetSession()
    {
        Close();
        SceneManager.LoadScene(GameSession.PlantScene);
    }

    private void QuitToMenu()
    {
        Close();
        SceneManager.LoadScene(GameSession.MenuScene);
    }
}
