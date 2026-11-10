using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ReactorSim;
using ReactorSim.Clipboard;

/// <summary>
/// U16 (R16) procedure clipboard on a desk. A thin board and clip built in Awake from primitives (K18 is a furniture
/// kit and the generated room places none). Clicking it within OperatorMath.InteractionRange opens a held-clipboard
/// checklist (IMGUI, screen space) for one of this clipboard's procedures. Ticks and the current step come from
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

    private Vector2 scroll;
    private GUIStyle titleStyle, bodyStyle, currentStyle, hintStyle, tabStyle, tabOnStyle, btnStyle;
    private int styleSettingsVersion = -1;
    private Texture2D white;

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

    // Cached once found; while missing, look at most once per frame (OnGUI runs several times per frame).
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
        if (open && Input.GetKeyDown(KeyCode.Escape)) escPending = true;
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
    }

    private void OnDisable()
    {
        open = false;
        inputGate.Restore();
    }

    // ---- drawing ---------------------------------------------------------------------------------------------

    private void MakeStyles()
    {
        if (titleStyle != null && styleSettingsVersion == UserSettings.Version) return;
        styleSettingsVersion = UserSettings.Version;
        white = Texture2D.whiteTexture;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = UserSettings.Scaled(26), fontStyle = FontStyle.Bold, wordWrap = true };
        bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = UserSettings.Scaled(20), wordWrap = true };
        currentStyle = new GUIStyle(bodyStyle) { fontStyle = FontStyle.Bold };
        hintStyle = new GUIStyle(GUI.skin.label) { fontSize = UserSettings.Scaled(16), wordWrap = true };
        tabStyle = new GUIStyle(GUI.skin.button) { fontSize = UserSettings.Scaled(16) };
        tabOnStyle = new GUIStyle(tabStyle) { fontStyle = FontStyle.Bold };
        btnStyle = new GUIStyle(GUI.skin.button) { fontSize = UserSettings.Scaled(18) };
    }

    private void OnGUI()
    {
        MakeStyles();
        if (mine == null) mine = MyProcedures();
        var procs = mine;
        FindBridge();
        if (procs.Count == 0) { DrawHoverHint(); return; }
        if (selected >= procs.Count) selected = 0;

        // The state only feeds the panel and the "Show me" outline/hint; the outline and hint draw on Repaint only.
        bool repaint = Event.current.type == EventType.Repaint;
        ProcedureState state = (open || (showMe && repaint)) ? CurrentState(procs[selected]) : null;

        float s = Screen.height / 1080f;
        var saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s;
        if (showMe && state != null && repaint) outline.Draw(ClipboardView.HighlightRows(state), s);
        if (open) DrawPanel(procs, state, vw);
        else if (showMe && state != null && !state.Complete)
            GUI.Label(new Rect(vw * 0.5f - 420f, 12f, 840f, 40f), "Next: " + state.Current.Text, bodyStyle);
        GUI.matrix = saved;

        if (!open) DrawHoverHint();
    }

    private void DrawHoverHint()
    {
        if (!hovered || Blocked) return;
        string text = label + (InRange ? " - click to read" : " - walk closer");
        var r = new Rect(Input.mousePosition.x + 16f, Screen.height - Input.mousePosition.y + 8f, 340f, 28f);
        GUI.Box(r, text);
    }

    private void DrawPanel(List<Procedure> procs, ProcedureState state, float vw)
    {
        var panel = new Rect(vw * 0.5f - 360f, 90f, 720f, 860f);
        if (Event.current.type == EventType.MouseDown && Time.frameCount != openedFrame)
        {
            var screenPt = Event.current.mousePosition;
            if (!panel.Contains(screenPt)) { Close(); Event.current.Use(); return; }
        }

        var old = GUI.color;
        GUI.color = ThemeColors.SurfacePaper;
        GUI.DrawTexture(panel, white);
        GUI.color = ThemeColors.TextInkOnPaper;
        GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 6f), white);
        GUI.color = old;

        var prevContent = GUI.contentColor;
        GUI.contentColor = ThemeColors.TextInkOnPaper;
        GUILayout.BeginArea(new Rect(panel.x + 24f, panel.y + 20f, panel.width - 48f, panel.height - 40f));

        if (procs.Count > 1)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < procs.Count; i++)
            {
                string t = string.IsNullOrEmpty(procs[i].Title) ? procs[i].Id : procs[i].Title;
                if (GUILayout.Button(t, i == selected ? tabOnStyle : tabStyle, GUILayout.Height(32f))) { selected = i; scroll = Vector2.zero; }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
        }

        var proc = procs[selected];
        if (state == null)
        {
            GUILayout.Label(proc.Title, titleStyle);
            GUILayout.Label("Plant not running.", bodyStyle);
        }
        else
        {
            GUILayout.Label(ClipboardView.Header(proc, state), titleStyle);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(680f));
            foreach (var line in ClipboardView.Lines(proc, state))
            {
                var style = line.Current ? currentStyle : bodyStyle;
                var old2 = GUI.contentColor;
                GUI.contentColor = line.Current ? ThemeColors.TextPaperCurrent : (line.Ticked ? ThemeColors.TextPaperDone : ThemeColors.TextInkOnPaper);
                GUILayout.Label(line.Mark + "  " + line.Number + ". " + line.Text, style);
                GUI.contentColor = old2;
                GUILayout.Space(4f);
            }
            GUILayout.EndScrollView();
        }

        GUILayout.BeginHorizontal();
        showMe = GUILayout.Toggle(showMe, "  Show me the next control", btnStyle, GUILayout.Height(36f));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", btnStyle, GUILayout.Width(120f), GUILayout.Height(36f))) Close();
        GUILayout.EndHorizontal();
        GUILayout.Label("Steps tick themselves from the plant. Esc or click outside to put the clipboard down.", hintStyle);

        GUILayout.EndArea();
        GUI.contentColor = prevContent;
    }
}

/// <summary>
/// The "Show me" outline, shared by the desk clipboard and the startup checklist panel: a pulsing screen-space box
/// around each given CTL_ row root (bounds of its renderers, 8 corners projected). Drawn on Repaint only
/// (GUI.DrawTexture ignores every other event). Each user keeps its own instance (its own lookup caches).
/// </summary>
public sealed class ShowMeOutline
{
    private readonly Dictionary<string, Transform> rowRoots = new Dictionary<string, Transform>();
    private readonly Dictionary<string, Renderer[]> rowRenderers = new Dictionary<string, Renderer[]>();

    /// <summary>s is the IMGUI scale (Screen.height / 1080) of the GUI.matrix the caller set.</summary>
    public void Draw(string[] rows, float s)
    {
        var cam = Camera.main;
        if (cam == null || rows == null) return;
        var white = Texture2D.whiteTexture;
        float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 6f);
        var old = GUI.color;
        GUI.color = ThemeColors.WithAlpha(ThemeColors.HighlightPulse, pulse);
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
                minY = Mathf.Min(minY, Screen.height - sp.y); maxY = Mathf.Max(maxY, Screen.height - sp.y);
            }
            if (!ok) continue;
            var r = new Rect(minX / s - 6f, minY / s - 6f, (maxX - minX) / s + 12f, (maxY - minY) / s + 12f);
            const float w = 3f;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, w), white);
            GUI.DrawTexture(new Rect(r.x, r.yMax - w, r.width, w), white);
            GUI.DrawTexture(new Rect(r.x, r.y, w, r.height), white);
            GUI.DrawTexture(new Rect(r.xMax - w, r.y, w, r.height), white);
        }
        GUI.color = old;
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
