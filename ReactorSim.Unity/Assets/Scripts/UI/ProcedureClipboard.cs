using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ReactorSim;
using ReactorSim.Clipboard;
using ReactorSim.Ui;

/// <summary>
/// U16 (R16) procedure clipboard on a desk. A thin board and clip built in Awake from primitives (K18 is a furniture
/// kit and the generated room places none). Clicking it within OperatorMath.InteractionRange opens a held-clipboard
/// checklist (uGUI and TextMeshPro, screen space) for one of this clipboard's procedures. Ticks and the current step come from
/// Procedure.Evaluate over the live snapshot every frame; a click can never tick a step. "Show me" outlines the
/// current step's CTL_ rows in screen space (works with any render pipeline, no shader needed) and stays on after
/// the panel is closed so the player can walk to the control. Opening suspends room look and clicks the same way
/// the operator tablet does; click outside the panel or Esc closes.
/// procedures.json (StreamingAssets) is loaded once for all clipboards; a bad file logs its named error.
/// </summary>
public class ProcedureClipboard : MonoBehaviour
{
    [Tooltip("Procedure ids shown on this clipboard (set by ControlRoomSceneBuilder).")]
    public string[] procedureIds = new string[0];
    public string label = "Procedure clipboard";

    // The pad is ~0.23 x 0.32 m; local +Z is the top edge, +Y is the writing face.
    public const float Width = 0.23f, Length = 0.32f;

    private static List<Procedure> loaded;
    private static bool loadTried;

    private PlantBridge bridge;
    private OperatorTablet tablet;
    private bool open;
    private int openedFrame = -1;
    private bool showMe;
    private int selected;
    private bool hovered;
    private bool escPending;
    private List<Procedure> mine;
    private readonly RoomInputGate inputGate = new RoomInputGate();
    private readonly ShowMeOutline outline = new ShowMeOutline();
    private ProcedureState cachedState;
    private int cachedStateFrame = -1;
    private int cachedStateSelected = -1;
    private int bridgeLookupFrame = -1;
    private int tabletLookupFrame = -1;

    // uGUI, built on first use
    private Canvas canvas;
    private RectTransform panel, tabRow, content;
    private TMP_Text title, bannerText, hoverText;
    private GameObject bannerBox, hoverBox;
    private Toggle showMeToggle;
    private Button[] tabs = new Button[0];
    private readonly List<TMP_Text> rows = new List<TMP_Text>();

    public static List<Procedure> Procedures
    {
        get
        {
            if (!loadTried)
            {
                loadTried = true;
                try { loaded = ProcedureLoader.LoadFromFile(Path.Combine(Application.streamingAssetsPath, "procedures.json")); }
                catch (ProcedureException ex) { Debug.LogError("[ProcedureClipboard] procedures.json rejected: " + ex.Message); }
                catch (IOException ex) { Debug.LogError("[ProcedureClipboard] procedures.json unreadable: " + ex.Message); }
            }
            return loaded;
        }
    }

    private void Awake()
    {
        BuildGeometry();
        var box = gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.02f, 0f);
        box.size = new Vector3(Width, 0.06f, Length);
    }

    // ---- geometry --------------------------------------------------------------------------------------------

    private void BuildGeometry()
    {
        Shader shader = null;
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.isSupported) { shader = r.sharedMaterial.shader; break; }
        }
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Part("Board", new Vector3(0f, 0.006f, 0f), new Vector3(Width, 0.012f, Length), shader, new Color(0.35f, 0.24f, 0.14f));
        Part("Paper", new Vector3(0f, 0.0135f, -0.01f), new Vector3(Width - 0.03f, 0.003f, Length - 0.06f), shader, new Color(0.93f, 0.92f, 0.86f));
        Part("Clip", new Vector3(0f, 0.018f, Length * 0.5f - 0.02f), new Vector3(0.08f, 0.02f, 0.035f), shader, new Color(0.7f, 0.72f, 0.75f));
    }

    private void Part(string partName, Vector3 pos, Vector3 size, Shader shader, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = partName;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        var r = go.GetComponent<MeshRenderer>();
        if (shader == null) return;
        var m = new Material(shader);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        r.sharedMaterial = m;
    }

    // ---- open / close ----------------------------------------------------------------------------------------

    private List<Procedure> MyProcedures()
    {
        var result = new List<Procedure>();
        var all = Procedures;
        if (all == null) return result;
        foreach (string id in procedureIds)
        {
            var p = all.Find(x => x.Id == id);
            if (p == null) Debug.LogError("[ProcedureClipboard] " + name + ": no procedure \"" + id + "\" in procedures.json");
            else result.Add(p);
        }
        return result;
    }

    private bool InRange
    {
        get
        {
            var cam = Camera.main;
            var c = GetComponent<Collider>();
            if (cam == null || c == null) return true;
            Vector3 eye = cam.transform.position;
            return OperatorMath.InInteractionRange(Vector3.Distance(eye, c.ClosestPoint(eye)));
        }
    }

    private bool Blocked
    {
        get
        {
            if (PauseMenu.IsOpen || StartupChecklistHud.IsExpanded) return true;
            if (tablet == null && tabletLookupFrame != Time.frameCount)
            {
                tabletLookupFrame = Time.frameCount;
                tablet = FindAnyObjectByType<OperatorTablet>();
            }
            return tablet != null && tablet.IsRaised;
        }
    }

    private void OnMouseEnter() { hovered = true; }
    private void OnMouseExit() { hovered = false; }

    private void OnMouseDown()
    {
        if (open || Blocked || !InRange) return;
        Open();
    }

    // Cached once found; while missing, look at most once per frame.
    private void FindBridge()
    {
        if (bridge != null || bridgeLookupFrame == Time.frameCount) return;
        bridgeLookupFrame = Time.frameCount;
        bridge = FindAnyObjectByType<PlantBridge>();
    }

    // Procedure.Evaluate over the frame's snapshot, at most once per frame per selected procedure.
    private ProcedureState CurrentState(Procedure proc)
    {
        if (cachedStateFrame != Time.frameCount || cachedStateSelected != selected)
        {
            PlantSnapshot snap = bridge != null ? bridge.GetSnapshot() : null;
            cachedState = snap != null ? proc.Evaluate(snap) : null;
            cachedStateFrame = Time.frameCount;
            cachedStateSelected = selected;
        }
        return cachedState;
    }

    private void Open()
    {
        FindBridge();
        open = true;
        openedFrame = Time.frameCount;
        inputGate.Suspend();
    }

    private void Close()
    {
        open = false;
        inputGate.Restore();
    }

    /// <summary>True when any desk clipboard is open.</summary>
    public static bool AnyOpen()
    {
        foreach (var c in FindObjectsByType<ProcedureClipboard>(FindObjectsSortMode.None))
            if (c.open) return true;
        return false;
    }

    /// <summary>
    /// Closes every open clipboard (restoring the input it suspended). Another overlay calls this BEFORE suspending input
    /// itself, so the clipboard's later close cannot re-enable the room behind that overlay.
    /// </summary>
    public static void CloseAllOpen()
    {
        foreach (var c in FindObjectsByType<ProcedureClipboard>(FindObjectsSortMode.None))
            if (c.open) c.Close();
    }

    private void Update()
    {
        if (open && Input.GetKeyDown(KeyCode.Escape) && !DiegeticInteractable.InputConsumedThisFrame) escPending = true;
    }


    // Esc also opens the pause menu, which suspends whatever is still enabled. Decide after both Updates have run:
    // if the pause menu took over, keep our suspended behaviours disabled until it closes (it does not know them).
    private void LateUpdate()
    {
        if (escPending)
        {
            escPending = false;
            open = false;
        }
        if (!open && inputGate.HasSuspended && !PauseMenu.IsOpen) inputGate.Restore();
        Refresh();
    }

    private void OnDisable()
    {
        open = false;
        inputGate.Restore();
        outline.Hide();
        if (canvas != null) canvas.gameObject.SetActive(false);
    }

    // ---- drawing (uGUI) ---------------------------------------------------------------------------------------

    private const float PanelWidth = 720f;

    private void Build()
    {
        if (canvas != null) return;
        RuntimeUi.EnsureEventSystem();
        canvas = RuntimeUi.CreateCanvas("ClipboardCanvas", 60, transform);

        // Click outside the panel closes it: a transparent full-screen button behind the panel.
        var back = RuntimeUi.Stretch(canvas.transform, "Backdrop", ThemeColors.WithAlpha(ThemeColors.ScrimDim, 0f));
        var backBtn = back.gameObject.AddComponent<Button>();
        backBtn.transition = Selectable.Transition.None;
        backBtn.onClick.AddListener(() => { if (Time.frameCount != openedFrame) Close(); });

        panel = RuntimeUi.Panel(canvas.transform, "Paper", PanelWidth, ThemeColors.SurfacePaper, new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new RectOffset(Theme.Space5, Theme.Space5, Theme.Space3, Theme.Space5), Theme.Space2);
        panel.anchoredPosition = new Vector2(0f, -90f);
        InkBar(panel);

        var tr = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        tr.transform.SetParent(panel, false);
        tabRow = (RectTransform)tr.transform;
        var h = tr.GetComponent<HorizontalLayoutGroup>();
        h.spacing = Theme.Space2;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = false;

        title = RuntimeUi.MakeLabel(panel, "", TextRole.Heading, TextAnchor.MiddleLeft, ThemeColors.TextInkOnPaper);
        title.fontStyle = FontStyles.Bold;
        content = RuntimeUi.MakeScrollView(panel, 680f);

        var foot = new GameObject("Footer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        foot.transform.SetParent(panel, false);
        var fh = foot.GetComponent<HorizontalLayoutGroup>();
        fh.spacing = Theme.Space3;
        fh.childControlWidth = true;
        fh.childControlHeight = true;
        fh.childForceExpandWidth = false;
        fh.childForceExpandHeight = false;
        showMeToggle = RuntimeUi.MakeToggle(foot.transform, "Show me the next control", showMe, v => showMe = v, ThemeColors.TextInkOnPaper);
        showMeToggle.GetComponent<LayoutElement>().flexibleWidth = 1f;
        var close = RuntimeUi.MakeButton(foot.transform, "Close", Close, 48f);
        close.GetComponent<LayoutElement>().minWidth = 140f;
        RuntimeUi.MakeLabel(panel, "Steps tick themselves from the plant. Esc or click outside to put the clipboard down.",
            TextRole.Caption, TextAnchor.MiddleLeft, ThemeColors.TextInkOnPaper);

        // "Next:" banner while "Show me" is on and the panel is closed.
        bannerBox = Banner(canvas.transform, "Banner", 860f, out bannerText, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -12f));
        // Hover hint beside the cursor.
        hoverBox = Banner(canvas.transform, "Hover", 420f, out hoverText, TextAnchor.MiddleLeft, new Vector2(0f, 0f), Vector2.zero);
        ((RectTransform)hoverBox.transform).pivot = new Vector2(0f, 1f);
        hoverBox.GetComponent<Image>().raycastTarget = false;
        bannerBox.GetComponent<Image>().raycastTarget = false;
        bannerBox.SetActive(false);
        hoverBox.SetActive(false);
        panel.gameObject.SetActive(false);
        back.gameObject.SetActive(false);
        backdrop = back.gameObject;
    }

    private GameObject backdrop;

    private static void InkBar(RectTransform parent)
    {
        var go = new GameObject("InkBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = ThemeColors.TextInkOnPaper;
        img.raycastTarget = false;
        var le = go.GetComponent<LayoutElement>();
        le.minHeight = 6f;
        le.preferredHeight = 6f;
    }

    private static GameObject Banner(Transform parent, string name, float width, out TMP_Text text, TextAnchor anchor, Vector2 at, Vector2 pos)
    {
        var box = RuntimeUi.Panel(parent, name, width, ThemeColors.SurfaceTooltip, at, new Vector2(at.x, 1f),
            new RectOffset(Theme.Space3, Theme.Space3, Theme.Space2, Theme.Space2), Theme.Space1);
        box.anchoredPosition = pos;
        text = RuntimeUi.MakeLabel(box, "", TextRole.Body, anchor, ThemeColors.TextBody);
        return box.gameObject;
    }

    private static void Set(TMP_Text t, string text)
    {
        if (t.text != text) t.text = text;
    }

    // Each frame: panel, banner, hover hint and the outline follow the state. Runs after Update/LateUpdate logic.
    private void Refresh()
    {
        if (mine == null) mine = MyProcedures();
        var procs = mine;
        FindBridge();
        if (procs.Count == 0) { if (canvas != null) canvas.gameObject.SetActive(false); outline.Hide(); return; }
        if (selected >= procs.Count) selected = 0;

        bool needUi = open || showMe || (hovered && !Blocked);
        if (!needUi)
        {
            if (canvas != null && canvas.gameObject.activeSelf) canvas.gameObject.SetActive(false);
            outline.Hide();
            return;
        }
        Build();
        canvas.gameObject.SetActive(true);
        ProcedureState state = (open || showMe) ? CurrentState(procs[selected]) : null;

        if (showMe && state != null) outline.Show(ClipboardView.HighlightRows(state)); else outline.Hide();

        if (backdrop.activeSelf != open) backdrop.SetActive(open);
        if (panel.gameObject.activeSelf != open)
        {
            panel.gameObject.SetActive(open);
            if (open) BuildTabs(procs);
        }
        if (open) RefreshPanel(procs, state);

        bool banner = !open && showMe && state != null && !state.Complete;
        if (bannerBox.activeSelf != banner) bannerBox.SetActive(banner);
        if (banner) Set(bannerText, "Next: " + state.Current.Text);

        bool hover = !open && hovered && !Blocked;
        if (hoverBox.activeSelf != hover) hoverBox.SetActive(hover);
        if (hover)
        {
            Set(hoverText, label + (InRange ? " - click to read" : " - walk closer"));
            float s = Screen.height / (float)Theme.ReferenceHeight;
            ((RectTransform)hoverBox.transform).anchoredPosition = new Vector2((Input.mousePosition.x + 16f) / s, (Input.mousePosition.y - 8f) / s);
        }
    }

    private void BuildTabs(List<Procedure> procs)
    {
        foreach (var t in tabs) if (t != null) Destroy(t.gameObject);
        tabs = new Button[procs.Count > 1 ? procs.Count : 0];
        tabRow.gameObject.SetActive(procs.Count > 1);
        if (procs.Count <= 1) return;
        for (int i = 0; i < procs.Count; i++)
        {
            int idx = i;
            string t = string.IsNullOrEmpty(procs[i].Title) ? procs[i].Id : procs[i].Title;
            tabs[i] = RuntimeUi.MakeButton(tabRow, t, () => { selected = idx; content.anchoredPosition = Vector2.zero; }, 40f);
        }
    }

    private void RefreshPanel(List<Procedure> procs, ProcedureState state)
    {
        if (showMeToggle.isOn != showMe) showMeToggle.SetIsOnWithoutNotify(showMe);
        for (int i = 0; i < tabs.Length; i++)
        {
            var lt = tabs[i].GetComponentInChildren<TMP_Text>();
            if (lt != null) lt.fontStyle = i == selected ? FontStyles.Bold | FontStyles.Underline : FontStyles.Normal;
        }
        var proc = procs[selected];
        int n = 0;
        if (state == null)
        {
            Set(title, proc.Title);
            while (rows.Count <= 0) rows.Add(RuntimeUi.MakeLabel(content, "", TextRole.Body, TextAnchor.UpperLeft, ThemeColors.TextInkOnPaper));
            rows[0].gameObject.SetActive(true);
            Set(rows[0], "Plant not running.");
            rows[0].color = ThemeColors.TextInkOnPaper;
            rows[0].fontStyle = FontStyles.Normal;
            n = 1;
        }
        else
        {
            Set(title, ClipboardView.Header(proc, state));
            foreach (var line in ClipboardView.Lines(proc, state))
            {
                while (rows.Count <= n) rows.Add(RuntimeUi.MakeLabel(content, "", TextRole.Body, TextAnchor.UpperLeft, ThemeColors.TextInkOnPaper));
                var row = rows[n++];
                row.gameObject.SetActive(true);
                Set(row, line.Mark + "  " + line.Number + ". " + line.Text);
                row.color = line.Current ? ThemeColors.TextPaperCurrent : (line.Ticked ? ThemeColors.TextPaperDone : ThemeColors.TextInkOnPaper);
                row.fontStyle = line.Current ? FontStyles.Bold : FontStyles.Normal;
            }
        }
        for (int i = n; i < rows.Count; i++) rows[i].gameObject.SetActive(false);
    }
}

/// <summary>
/// The "Show me" outline, shared by the desk clipboard and the startup checklist panel: a pulsing screen-space box
/// around each given CTL_ row root (bounds of its renderers, 8 corners projected), drawn as four thin uGUI bars per box
/// on its own overlay canvas. Show each frame while wanted, Hide otherwise. Each user keeps its own instance.
/// </summary>
public sealed class ShowMeOutline
{
    private const float Thickness = 3f;
    private const float Pad = 6f;

    private readonly Dictionary<string, Transform> rowRoots = new Dictionary<string, Transform>();
    private readonly Dictionary<string, Renderer[]> rowRenderers = new Dictionary<string, Renderer[]>();
    private readonly List<RectTransform[]> boxes = new List<RectTransform[]>();
    private GameObject root;

    public void Hide()
    {
        if (root != null && root.activeSelf) root.SetActive(false);
    }

    private void EnsureRoot()
    {
        if (root != null) return;
        var canvas = RuntimeUi.CreateCanvas("ShowMeCanvas", 30, null);
        root = canvas.gameObject;
        root.GetComponent<GraphicRaycaster>().enabled = false;
    }

    private RectTransform[] Box(int index)
    {
        while (boxes.Count <= index)
        {
            var bars = new RectTransform[4];
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(root.transform, false);
                go.GetComponent<Image>().raycastTarget = false;
                bars[i] = (RectTransform)go.transform;
                bars[i].anchorMin = bars[i].anchorMax = bars[i].pivot = Vector2.zero;
            }
            boxes.Add(bars);
        }
        return boxes[index];
    }

    private static void Place(RectTransform bar, float x, float y, float w, float h, Color c)
    {
        bar.anchoredPosition = new Vector2(x, y);
        bar.sizeDelta = new Vector2(w, h);
        bar.GetComponent<Image>().color = c;
    }

    public void Show(string[] rows)
    {
        var cam = Camera.main;
        if (cam == null || rows == null || rows.Length == 0) { Hide(); return; }
        EnsureRoot();
        root.SetActive(true);
        float s = Screen.height / (float)Theme.ReferenceHeight;   // canvas units per screen pixel is 1 / s
        float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 6f);
        Color color = ThemeColors.WithAlpha(ThemeColors.HighlightPulse, pulse);
        int used = 0;
        foreach (string row in rows)
        {
            Transform t;
            if (!rowRoots.TryGetValue(row, out t) || t == null)
            {
                var go = GameObject.Find("CTL_" + row);
                t = go != null ? go.transform : null;
                rowRoots[row] = t;
                rowRenderers.Remove(row);
            }
            if (t == null) continue;
            Renderer[] renderers;
            if (!rowRenderers.TryGetValue(row, out renderers))
            {
                renderers = t.GetComponentsInChildren<Renderer>();
                rowRenderers[row] = renderers;
            }
            Bounds b = WorldBounds(t, renderers);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool ok = true;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 sp = cam.WorldToScreenPoint(c);
                if (sp.z <= 0.05f) { ok = false; break; }
                minX = Mathf.Min(minX, sp.x); maxX = Mathf.Max(maxX, sp.x);
                minY = Mathf.Min(minY, sp.y); maxY = Mathf.Max(maxY, sp.y);
            }
            if (!ok) continue;
            // Screen pixels (y up) to canvas units, then pad.
            float x0 = minX / s - Pad, y0 = minY / s - Pad, w = (maxX - minX) / s + 2f * Pad, h = (maxY - minY) / s + 2f * Pad;
            var bars = Box(used++);
            foreach (var bar in bars) bar.gameObject.SetActive(true);
            Place(bars[0], x0, y0 + h - Thickness, w, Thickness, color);   // top
            Place(bars[1], x0, y0, w, Thickness, color);                    // bottom
            Place(bars[2], x0, y0, Thickness, h, color);                    // left
            Place(bars[3], x0 + w - Thickness, y0, Thickness, h, color);    // right
        }
        for (int i = used; i < boxes.Count; i++)
            foreach (var bar in boxes[i]) bar.gameObject.SetActive(false);
    }

    private static Bounds WorldBounds(Transform root, Renderer[] renderers)
    {
        bool has = false;
        var bounds = new Bounds();
        foreach (var r in renderers)
        {
            if (r == null || !r.enabled) continue;
            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }
        if (!has) bounds = new Bounds(root.position, Vector3.one * 0.06f);
        return bounds;
    }
}
