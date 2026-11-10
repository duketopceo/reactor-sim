using System.Collections.Generic;
using UnityEngine;
using TMPro;
using ReactorSim;
using ReactorSim.Instruments;

// Phase 2 CRT screen (KTD7): world-space TextMeshPro laid on the face of a CRT_* mesh. The page text comes from
// CrtPages (pure logic: reactor overview with clock state, trends, assistant terminal, ...). The CRT keeps its
// phosphor material underneath; the text sits just in front of the screen face, on the side facing the seat.
public class CrtScreenRenderer : MonoBehaviour
{
    public PlantBridge bridge;
    public string screenName = "CRT_A1_Core";
    public Color phosphor { get { return ThemeColors.CrtPhosphor; } }   // theme token; not a serialized override
    [Tooltip("Seconds between page rebuilds (text rebuilds are the expensive part).")]
    public float refreshSeconds = 0.1f;

    public TextMeshPro Text { get; private set; }
    public string CurrentText { get { return lastText; } }

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

    private void ReadAdvisories()
    {
        terminal.Clear();
        lock (bridge.SimLock)
        {
            var log = bridge.Sim.Plant.Assistant.MessageLog;
            int from = Mathf.Max(0, log.Count - 6);
            for (int i = from; i < log.Count; i++)
            {
                var m = log[i];
                string line = "[" + m.GameMinute.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                    + "m " + m.Category + "] " + m.Finding;
                if (line.Length > 64) line = line.Substring(0, 61) + "...";
                terminal.Add(line);
            }
        }
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

        Text = go.AddComponent<TextMeshPro>();
        Text.rectTransform.sizeDelta = new Vector2(extent[wIdx] * 0.92f, extent[upIdx] * 0.9f);
        Text.enableAutoSizing = true;
        Text.fontSizeMin = 0.05f;
        Text.fontSizeMax = 4f;
        Text.alignment = TextAlignmentOptions.TopLeft;
        Text.color = phosphor;
        Text.richText = false;
        Text.textWrappingMode = TextWrappingModes.NoWrap;
        Text.overflowMode = TextOverflowModes.Overflow;
        placed = true;
    }
}
