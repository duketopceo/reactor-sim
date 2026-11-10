using System.Collections.Generic;
using UnityEngine;
using ReactorSim;
using ReactorSim.Clipboard;
using ReactorSim.Ui;

/// <summary>
/// U5 (R13, KTD6) always-on startup checklist: a compact panel at the top left (below MissionHud) showing the phase,
/// step N of total, the action, the control, the live reading against its target and a tick mark. On by default in
/// sandbox mode, off in missions mode (MissionHud owns that screen); the pause menu can hide it (UserSettings).
/// P expands the full list with ticks and the "Show me" outline toggle, sharing ClipboardView (lines, live values)
/// and ShowMeOutline with the desk ProcedureClipboard. Expanding frees the cursor through RoomInputGate; P or Esc
/// closes. The panel hides while the operator tablet or pause menu is open, shows an "On the grid" state at
/// completion and a caution line naming the step that unticked when the current step moves backward (SCRAM, trip).
/// All colors come from Theme tokens over a backing panel. The display model is pure (ClipboardView.Hud).
/// </summary>
public class StartupChecklistHud : MonoBehaviour
{
    public const string ProcedureId = "cold-start-to-grid";
    private const float CautionSeconds = 12f;

    /// <summary>True while the full list is open (the desk clipboard will not open over it).</summary>
    public static bool IsExpanded { get; private set; }

    private PlantBridge bridge;
    private OperatorTablet tablet;
    private Procedure procedure;
    private ProcedureState state;
    private PlantSnapshot snap;
    private StartupHudModel model;
    private bool expanded;
    private bool showMe;
    private bool escPending;
    private int previousIndex = ClipboardView.NoPrevious;
    private string cautionNote;
    private float cautionUntil;
    private Vector2 scroll;
    private int bridgeLookupFrame = -1;
    private int tabletLookupFrame = -1;
    private readonly RoomInputGate inputGate = new RoomInputGate();
    private readonly ShowMeOutline outline = new ShowMeOutline();

    private GUIStyle panelStyle, headStyle, bodyStyle, dimStyle, cautionStyle, okStyle, currentStyle, toggleStyle, btnStyle;
    private int styleSettingsVersion = -1;

    /// <summary>Closes every open full list, restoring the input it suspended (call before another overlay suspends).</summary>
    public static void CollapseAll()
    {
        foreach (var h in FindObjectsByType<StartupChecklistHud>(FindObjectsSortMode.None))
            if (h.expanded) h.Collapse();
    }

    private static bool Enabled
    {
        get { return GameSession.Mode == StartMode.Sandbox && !UserSettings.ChecklistHidden; }
    }

    private bool OverlayOpen
    {
        get
        {
            if (PauseMenu.IsOpen) return true;
            if (tablet == null && tabletLookupFrame != Time.frameCount)
            {
                tabletLookupFrame = Time.frameCount;
                tablet = FindAnyObjectByType<OperatorTablet>();
            }
            return tablet != null && tablet.IsRaised;
        }
    }

    private void FindBridge()
    {
        if (bridge != null || bridgeLookupFrame == Time.frameCount) return;
        bridgeLookupFrame = Time.frameCount;
        bridge = FindAnyObjectByType<PlantBridge>();
    }

    private void Expand()
    {
        expanded = true;
        IsExpanded = true;
        scroll = Vector2.zero;
        inputGate.Suspend();
    }

    private void Collapse()
    {
        expanded = false;
        IsExpanded = false;
        inputGate.Restore();
    }

    // The state is evaluated once per frame in Update; OnGUI (several calls per frame) only reads it.
    private void Update()
    {
        FindBridge();
        if (procedure == null)
        {
            var all = ProcedureClipboard.Procedures;
            if (all != null && all.Count > 0)
            {
                procedure = all.Find(x => x.Id == ProcedureId);
                if (procedure == null) procedure = all[0];
            }
        }
        snap = bridge != null ? bridge.GetSnapshot() : null;
        if (procedure != null && snap != null)
        {
            state = procedure.Evaluate(snap);
            model = ClipboardView.Hud(procedure, state, snap);
            string note = ClipboardView.RegressionNote(procedure, previousIndex, state);
            if (note != null) { cautionNote = note; cautionUntil = Time.unscaledTime + CautionSeconds; }
            if (state.Complete) cautionNote = null;
            previousIndex = state.CurrentIndex;
        }
        else
        {
            state = null;
            model = null;
        }

        if (expanded && Input.GetKeyDown(KeyCode.Escape)) escPending = true;
        if (Input.GetKeyDown(KeyCode.P) && Enabled && model != null && !OverlayOpen)
        {
            if (expanded) Collapse();
            else if (!ProcedureClipboard.AnyOpen()) Expand();
        }
    }

    // Esc also opens the pause menu, which suspends whatever is still enabled. Decide after both Updates have run:
    // if the pause menu took over, keep our suspended behaviours disabled until it closes (it does not know them).
    private void LateUpdate()
    {
        if (escPending)
        {
            escPending = false;
            expanded = false;
            IsExpanded = false;
        }
        if (!expanded && inputGate.HasSuspended && !PauseMenu.IsOpen) inputGate.Restore();
    }

    private void OnDisable()
    {
        expanded = false;
        IsExpanded = false;
        inputGate.Restore();
    }

    // ---- drawing ---------------------------------------------------------------------------------------------

    private void MakeStyles()
    {
        if (panelStyle != null && styleSettingsVersion == UserSettings.Version) return;
        styleSettingsVersion = UserSettings.Version;
        int body = UserSettings.Scaled(Theme.FontCaption);
        int head = UserSettings.Scaled(Theme.FontBody);
        panelStyle = ThemeColors.BoxStyle(Theme.SurfaceTooltip, body, Theme.TextBody);
        panelStyle.padding = new RectOffset(12, 12, 10, 10);
        headStyle = ThemeColors.LabelStyle(head, Theme.TextBody, true);
        bodyStyle = ThemeColors.LabelStyle(body, Theme.TextBody);
        dimStyle = ThemeColors.LabelStyle(body, Theme.TextDim);
        cautionStyle = ThemeColors.LabelStyle(body, Theme.StatusCaution, true);
        okStyle = ThemeColors.LabelStyle(body, Theme.StatusOk);
        currentStyle = ThemeColors.LabelStyle(body, Theme.TextBody, true);
        toggleStyle = new GUIStyle(GUI.skin.toggle) { fontSize = body, wordWrap = true };
        btnStyle = new GUIStyle(GUI.skin.button) { fontSize = body };
        SetText(toggleStyle);
        SetText(btnStyle);
    }

    private static void SetText(GUIStyle s)
    {
        var c = ThemeColors.ToColor(Theme.TextBody);
        s.normal.textColor = c;
        s.hover.textColor = c;
        s.active.textColor = c;
        s.focused.textColor = c;
        s.onNormal.textColor = c;
        s.onHover.textColor = c;
        s.onActive.textColor = c;
        s.onFocused.textColor = c;
    }

    private void OnGUI()
    {
        if (!Enabled || model == null || procedure == null || state == null) return;
        if (OverlayOpen) return;
        MakeStyles();

        float s = Mathf.Max(1f, Screen.height / 1080f);
        var saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        // "Show me" stays on after the list closes, like the desk clipboard's, so the player can walk to the control.
        if (showMe && Event.current.type == EventType.Repaint) outline.Draw(model.HighlightRows, s);
        if (expanded) DrawFull();
        else DrawCompact();
        GUI.matrix = saved;
    }

    private bool CautionActive()
    {
        return !string.IsNullOrEmpty(cautionNote) && Time.unscaledTime < cautionUntil;
    }

    // Compact panel, capped at 470 x 330 (clear of the tooltip and hint regions, below MissionHud's 10..190 band).
    private void DrawCompact()
    {
        GUILayout.BeginArea(new Rect(10f, 196f, 470f, 330f));
        GUILayout.BeginVertical(panelStyle);
        if (model.Complete)
        {
            GUILayout.Label(model.Mark + " " + model.Action, headStyle);
            GUILayout.Label("All " + model.StepCount + " steps ticked.", okStyle);
        }
        else
        {
            if (model.Phase.Length > 0) GUILayout.Label(model.Phase, dimStyle);
            GUILayout.Label(model.Mark + " Step " + model.StepNumber + " of " + model.StepCount, headStyle);
            GUILayout.Label(model.Action, currentStyle);
            if (model.Control.Length > 0) GUILayout.Label("Control: " + model.Control, bodyStyle);
            if (model.Reading.Length > 0) GUILayout.Label(model.Reading, bodyStyle);
        }
        if (CautionActive()) GUILayout.Label(cautionNote, cautionStyle);
        GUILayout.Label("P - full list", dimStyle);
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    // Full list, capped at 620 x 760: ticks per step, the current step's control and reading, and "Show me".
    private void DrawFull()
    {
        GUILayout.BeginArea(new Rect(10f, 196f, 620f, 760f));
        GUILayout.BeginVertical(panelStyle);
        GUILayout.Label(model.Complete ? model.Action : ClipboardView.Header(procedure, state), headStyle);
        if (CautionActive()) GUILayout.Label(cautionNote, cautionStyle);
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(520f));
        foreach (var line in ClipboardView.Lines(procedure, state, snap))
        {
            var style = line.Current ? currentStyle : (line.Ticked ? okStyle : bodyStyle);
            GUILayout.Label(line.Mark + "  " + line.Number + ". " + line.Text, style);
            if (line.Current)
            {
                if (line.Control.Length > 0) GUILayout.Label("      Control: " + line.Control, bodyStyle);
                string reading = ClipboardView.Reading(line.LiveValue, line.Target);
                if (reading.Length > 0) GUILayout.Label("      " + reading, bodyStyle);
            }
        }
        GUILayout.EndScrollView();
        showMe = GUILayout.Toggle(showMe, "  Show me the next control", toggleStyle);
        if (GUILayout.Button("Close (P or Esc)", btnStyle, GUILayout.Height(32f))) Collapse();
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}
