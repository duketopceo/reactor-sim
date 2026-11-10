using System.Collections.Generic;
using UnityEngine;
using TMPro;
using ReactorSim;
using ReactorSim.Instruments;

// Phase 2 CRT screen (KTD4, KTD7): world-space TextMeshPro laid on the face of a CRT_* mesh. The page text comes from
// CrtPages (pure logic: a fixed Columns x RowBudget grid with a header band, status row and big key readouts). The
// text is fixed-size monospace (IBM Plex Mono SDF from UiFonts): its size is solved once so the full grid fills the
// face, never auto-sized down, so a page that cannot be read from the seat is read through ScreenFocus instead.
// The screen material is emissive (ControlRoomSceneBuilder), so room lights and shadows do not dim it, and the plain
// SDF TMP shader draws the glyphs with outline off.
public class CrtScreenRenderer : MonoBehaviour
{
    public PlantBridge bridge;
    public string screenName = "CRT_A1_Core";
    public Color phosphor { get { return ThemeColors.CrtPhosphor; } }   // theme token; not a serialized override
    [Tooltip("Seconds between page rebuilds (text rebuilds are the expensive part).")]
    public float refreshSeconds = 0.1f;

    public TextMeshPro Text { get; private set; }
    public string CurrentText { get { return lastText; } }

    // The face the text lies on, in world space (valid once HasFace). ScreenFocus frames and picks with these.
    public bool HasFace { get; private set; }
    public Vector3 FaceCenter { get; private set; }
    public Vector3 FaceNormal { get; private set; }
    public Vector3 FaceUp { get; private set; }
    public float FaceWidth { get; private set; }
    public float FaceHeight { get; private set; }

    // Rect margins inside the face.
    private const float WidthFill = 0.92f, HeightFill = 0.90f;
    // TextMeshPro (3D) draws one font-size unit as 0.1 world units; the calibration below measures the real size anyway.
    private const float FontUnitsPerMetre = 10f;

    private readonly TrendBuffer trends = new TrendBuffer(120);
    private readonly List<string> terminal = new List<string>(8);
    private string lastText = "";
    private float nextRefresh;
    private bool placed;

    private void Awake()
    {
        if (bridge == null)
            bridge = FindAnyObjectByType<PlantBridge>();
    }

    private void OnDestroy()
    {
        ScreenFocus.Unregister(this);
    }

    private void Update()
    {
        if (bridge == null || bridge.Sim == null) return;
        if (!placed) Place();
        if (Text == null) return;
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + refreshSeconds;

        var s = bridge.GetSnapshot();
        if (s == null) return;
        trends.Push(s);
        if (screenName == "CRT_AssistantTerminal") ReadAdvisories();
        string text = CrtPages.Build(screenName, s, ClockReadout.FromClock(bridge.Clock), trends, terminal);
        if (text != lastText)
        {
            lastText = text;
            Text.text = text;
        }
    }

    // The latest advisories; CrtPages cuts each to the page width and keeps the newest five.
    private void ReadAdvisories()
    {
        terminal.Clear();
        lock (bridge.SimLock)
        {
            var log = bridge.Sim.Plant.Assistant.MessageLog;
            int from = Mathf.Max(0, log.Count - 5);
            for (int i = from; i < log.Count; i++)
            {
                var m = log[i];
                terminal.Add("[" + ReactorSim.Ui.ReadoutFormat.Number(m.GameMinute, 1) + "m " + m.Category + "] " + m.Finding);
            }
        }
    }

    /// <summary>Distance along the ray to this face when the ray crosses inside its rectangle.</summary>
    public bool RayHitsFace(Ray ray, out float distance)
    {
        distance = 0f;
        if (!HasFace) return false;
        var plane = new Plane(FaceNormal, FaceCenter);
        float enter;
        if (!plane.Raycast(ray, out enter) || enter <= 0f) return false;
        Vector3 local = ray.GetPoint(enter) - FaceCenter;
        Vector3 right = Vector3.Cross(FaceUp, FaceNormal).normalized;
        if (Mathf.Abs(Vector3.Dot(local, right)) > FaceWidth * 0.5f || Mathf.Abs(Vector3.Dot(local, FaceUp)) > FaceHeight * 0.5f)
            return false;
        distance = enter;
        return true;
    }

    // Lays the text on the thinnest face of this CRT mesh that looks toward the viewer.
    private void Place()
    {
        var cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
        if (cam == null) return;
        var mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) { placed = true; return; }

        Bounds lb = mf.sharedMesh.bounds;
        Vector3 ls = transform.lossyScale;
        Vector3[] axes = { transform.right, transform.up, transform.forward };
        float[] extent =
        {
            Mathf.Abs(lb.size.x * ls.x), Mathf.Abs(lb.size.y * ls.y), Mathf.Abs(lb.size.z * ls.z)
        };
        int thin = 0;
        for (int i = 1; i < 3; i++) if (extent[i] < extent[thin]) thin = i;

        Vector3 worldCenter = transform.TransformPoint(lb.center);
        Vector3 toViewer = cam.transform.position - worldCenter;
        Vector3 normal = axes[thin].normalized;
        if (Vector3.Dot(normal, toViewer) < 0f) normal = -normal;

        // Up on screen = the in-plane axis closest to world up; width = the other one.
        int a = (thin + 1) % 3, b = (thin + 2) % 3;
        int upIdx = Mathf.Abs(Vector3.Dot(axes[a].normalized, Vector3.up)) >= Mathf.Abs(Vector3.Dot(axes[b].normalized, Vector3.up)) ? a : b;
        int wIdx = upIdx == a ? b : a;
        Vector3 up = axes[upIdx].normalized;
        if (Vector3.Dot(up, Vector3.up) < 0f) up = -up;

        var go = new GameObject(screenName + "_Text");
        go.transform.SetParent(transform, true);
        go.transform.position = worldCenter + normal * (extent[thin] * 0.5f + 0.003f);
        go.transform.rotation = Quaternion.LookRotation(-normal, up);
        float k = Mathf.Max(1e-4f, (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f);
        go.transform.localScale = Vector3.one / k;

        FaceCenter = go.transform.position;
        FaceNormal = normal;
        FaceUp = up;
        FaceWidth = extent[wIdx];
        FaceHeight = extent[upIdx];
        HasFace = true;

        Text = go.AddComponent<TextMeshPro>();
        Text.font = UiFonts.Mono;
        Text.rectTransform.sizeDelta = new Vector2(FaceWidth * WidthFill, FaceHeight * HeightFill);
        Text.enableAutoSizing = false;
        Text.alignment = TextAlignmentOptions.TopLeft;
        Text.color = phosphor;
        Text.richText = true;   // CrtPages marks big key rows with <size=..%>; it escapes '<' in the data text
        Text.textWrappingMode = TextWrappingModes.NoWrap;
        Text.overflowMode = TextOverflowModes.Overflow;
        FitFontToGrid();
        placed = true;
        ScreenFocus.Register(this, cam);
    }

    // Solves the font size once so a full Columns x RowBudget grid of this monospace face fills the text rect. The first
    // guess comes from PixelBudget; the measured size of a template page then corrects it, so the result does not depend
    // on the TextMeshPro world-unit convention or on the face's exact metrics.
    private void FitFontToGrid()
    {
        var rect = Text.rectTransform.sizeDelta;
        int rows = (int)System.Math.Ceiling(CrtPages.RowBudget);
        double em = PixelBudget.EmToFillFace(FaceWidth, FaceHeight, CrtPages.Columns, rows);
        float size = Mathf.Max(0.1f, (float)em * FontUnitsPerMetre);
        Text.fontSize = size;
        var lines = new string[rows];
        string row = new string('0', CrtPages.Columns);
        for (int i = 0; i < lines.Length; i++) lines[i] = row;
        Text.text = string.Join("\n", lines);
        Text.ForceMeshUpdate();
        float w = Text.preferredWidth, h = Text.preferredHeight;
        if (w > 1e-5f && h > 1e-5f)
            Text.fontSize = size * Mathf.Min(rect.x / w, rect.y / h);
        Text.text = "";
    }
}
