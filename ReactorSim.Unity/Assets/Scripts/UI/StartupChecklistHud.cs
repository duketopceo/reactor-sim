using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
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
/// Drawn with uGUI and TextMeshPro on the shared RuntimeUi kit (type ramp, themed panel, focus states); the panel keeps
/// its place at the top left and its size follows the text-scale setting. All colors come from Theme tokens over a
/// backing panel. The display model is pure (ClipboardView.Hud).
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
    private int bridgeLookupFrame = -1;
    private int tabletLookupFrame = -1;
    private readonly RoomInputGate inputGate = new RoomInputGate();
    private readonly ShowMeOutline outline = new ShowMeOutline();

    // uGUI, built on first use
    private Canvas canvas;
    private Image panelImage;
    private ThemedPanelWidth panelWidth;
    private GameObject compactBox, fullBox;
    private TMP_Text cPhase, cStep, cAction, cControl, cReading, cCaution, cHint;
    private TMP_Text fHeader, fCaution;
    private RectTransform fContent;
    private readonly List<TMP_Text> fRows = new List<TMP_Text>();
    private Toggle fShowMe;

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

        if (expanded && Input.GetKeyDown(KeyCode.Escape) && !DiegeticInteractable.InputConsumedThisFrame) escPending = true;
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
        LateUpdateView();
    }

    private void OnDisable()
    {
        expanded = false;
        IsExpanded = false;
        inputGate.Restore();
        outline.Hide();
        if (canvas != null) canvas.gameObject.SetActive(false);
    }

    // ---- drawing (uGUI) ---------------------------------------------------------------------------------------

    private const float CompactWidth = 470f, FullWidth = 620f;

    private void Build()
    {
        if (canvas != null) return;
        RuntimeUi.EnsureEventSystem();
        canvas = RuntimeUi.CreateCanvas("ChecklistCanvas", 40, transform);
        // Top left, clear of the tooltip and hint regions and below MissionHud's band.
        var panel = RuntimeUi.Panel(canvas.transform, "Panel", CompactWidth, ThemeColors.SurfaceTooltip, new Vector2(0f, 1f),
            new Vector2(0f, 1f), new RectOffset(Theme.Space3, Theme.Space3, Theme.Space3, Theme.Space3), Theme.Space1);
        panel.anchoredPosition = new Vector2(10f, -196f);
        panelImage = panel.GetComponent<Image>();
        panelWidth = panel.GetComponent<ThemedPanelWidth>();

        compactBox = Box(panel, "Compact");
        cPhase = RuntimeUi.MakeLabel(compactBox.transform, "", TextRole.Caption, TextAnchor.MiddleLeft, ThemeColors.TextDim);
        cStep = RuntimeUi.MakeLabel(compactBox.transform, "", TextRole.Heading, TextAnchor.MiddleLeft, ThemeColors.TextHeading);
        cStep.fontStyle = FontStyles.Bold;
        cAction = RuntimeUi.MakeLabel(compactBox.transform, "", TextRole.Body, TextAnchor.MiddleLeft, ThemeColors.TextBody);
        cAction.fontStyle = FontStyles.Bold;
        cControl = RuntimeUi.MakeLabel(compactBox.transform, "", TextRole.Caption, TextAnchor.MiddleLeft, ThemeColors.TextBody);
        cReading = RuntimeUi.MakeLabel(compactBox.transform, "", TextRole.Numeric, TextAnchor.MiddleLeft, ThemeColors.TextBody);
        cCaution = RuntimeUi.MakeLabel(compactBox.transform, "", TextRole.Caption, TextAnchor.MiddleLeft, ThemeColors.StatusCaution);
        cCaution.fontStyle = FontStyles.Bold;
        cHint = RuntimeUi.MakeLabel(compactBox.transform, "P - full list", TextRole.Caption, TextAnchor.MiddleLeft, ThemeColors.TextDim);

        fullBox = Box(panel, "Full");
        fHeader = RuntimeUi.MakeLabel(fullBox.transform, "", TextRole.Heading, TextAnchor.MiddleLeft, ThemeColors.TextHeading);
        fHeader.fontStyle = FontStyles.Bold;
        fCaution = RuntimeUi.MakeLabel(fullBox.transform, "", TextRole.Caption, TextAnchor.MiddleLeft, ThemeColors.StatusCaution);
        fCaution.fontStyle = FontStyles.Bold;
        RuntimeUi.MakeDivider(fullBox.transform);
        fContent = RuntimeUi.MakeScrollView(fullBox.transform, 520f);
        fShowMe = RuntimeUi.MakeToggle(fullBox.transform, "Show me the next control", showMe, v => showMe = v);
        RuntimeUi.MakeButton(fullBox.transform, "Close (P or Esc)", Collapse, 44f);
        fullBox.SetActive(false);
    }

    private static GameObject Box(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
        go.transform.SetParent(parent, false);
        var v = go.GetComponent<VerticalLayoutGroup>();
        v.spacing = Theme.Space1;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return go;
    }

    private static void Set(TMP_Text t, string text)
    {
        bool has = !string.IsNullOrEmpty(text);
        if (t.gameObject.activeSelf != has) t.gameObject.SetActive(has);
        if (has && t.text != text) t.text = text;
    }

    private bool CautionActive()
    {
        return !string.IsNullOrEmpty(cautionNote) && Time.unscaledTime < cautionUntil;
    }

    // Runs after Update so the model for this frame is final; also keeps the "Show me" outline on the camera's new pose.
    private void LateUpdateView()
    {
        bool visible = Enabled && model != null && procedure != null && state != null && !OverlayOpen;
        if (!visible)
        {
            if (canvas != null) canvas.gameObject.SetActive(false);
            outline.Hide();
            return;
        }
        Build();
        canvas.gameObject.SetActive(true);
        // "Show me" stays on after the list closes, like the desk clipboard's, so the player can walk to the control.
        if (showMe) outline.Show(model.HighlightRows); else outline.Hide();

        if (compactBox.activeSelf == expanded) compactBox.SetActive(!expanded);
        if (fullBox.activeSelf != expanded)
        {
            fullBox.SetActive(expanded);
            panelWidth.SetDesignWidth(expanded ? FullWidth : CompactWidth);
        }
        panelImage.raycastTarget = expanded;
        if (expanded) RefreshFull(); else RefreshCompact();
    }

    private void RefreshCompact()
    {
        if (model.Complete)
        {
            Set(cPhase, "");
            Set(cStep, model.Mark + " " + model.Action);
            Set(cAction, "All " + model.StepCount + " steps ticked.");
            cAction.color = ThemeColors.StatusOk;
            Set(cControl, "");
            Set(cReading, "");
        }
        else
        {
            Set(cPhase, model.Phase);
            Set(cStep, model.Mark + " Step " + model.StepNumber + " of " + model.StepCount);
            Set(cAction, model.Action);
            cAction.color = ThemeColors.TextBody;
            Set(cControl, model.Control.Length > 0 ? "Control: " + model.Control : "");
            Set(cReading, model.Reading);
        }
        Set(cCaution, CautionActive() ? cautionNote : "");
        Set(cHint, "P - full list");
    }

    private void RefreshFull()
    {
        Set(fHeader, model.Complete ? model.Action : ClipboardView.Header(procedure, state));
        Set(fCaution, CautionActive() ? cautionNote : "");
        if (fShowMe.isOn != showMe) fShowMe.SetIsOnWithoutNotify(showMe);

        int n = 0;
        foreach (var line in ClipboardView.Lines(procedure, state, snap))
        {
            while (fRows.Count <= n)
                fRows.Add(RuntimeUi.MakeLabel(fContent, "", TextRole.Caption, TextAnchor.UpperLeft, ThemeColors.TextBody));
            var row = fRows[n++];
            row.gameObject.SetActive(true);
            string text = line.Mark + "  " + line.Number + ". " + line.Text;
            if (line.Current)
            {
                if (line.Control.Length > 0) text += "\n      Control: " + line.Control;
                string reading = ClipboardView.Reading(line.LiveValue, line.Target);
                if (reading.Length > 0) text += "\n      " + reading;
            }
            if (row.text != text) row.text = text;
            row.color = line.Ticked && !line.Current ? ThemeColors.StatusOk : ThemeColors.TextBody;
            row.fontStyle = line.Current ? FontStyles.Bold : FontStyles.Normal;
        }
        for (int i = n; i < fRows.Count; i++) fRows[i].gameObject.SetActive(false);
    }
}
