using System.Collections.Generic;
using ReactorSim;
using ReactorSim.Instruments;
using TMPro;
using UnityEngine;

// Displays the current snapshot on the separate plant-overview asset. It never submits intents.
public sealed class PlantWallMapView : MonoBehaviour
{
    public PlantBridge bridge;
    private readonly Dictionary<string, Renderer> lamps = new Dictionary<string, Renderer>();
    private readonly Dictionary<Renderer, string> routes = new Dictionary<Renderer, string>();
    private readonly Dictionary<Transform, Vector3> rods = new Dictionary<Transform, Vector3>();
    private MaterialPropertyBlock block;
    private TextMeshPro metrics;
    private TextMeshPro detail;
    private string lastSummary, lastDetail;
    private Renderer fuel;
    private ControlRoom3DBinder roomBinder;
    private Material metricsMaterial;

    // U2: click-to-select detail. The model owns the selection and the plate text; the view highlights the node and applies the text.
    private readonly WallSelection selection = new WallSelection();
    private readonly Dictionary<string, Renderer> nodes = new Dictionary<string, Renderer>();
    private string highlightApplied;
    private PlantSnapshot lastSnapshot;

    // U2: a moving arrowhead per directed route, shown only while the model gives that route a direction.
    private sealed class FlowCue
    {
        public string Route;
        public Transform Cue;
        public double[][] Path;
        public float Length;
        public float Phase;
    }
    private readonly List<FlowCue> cues = new List<FlowCue>();
    private Vector3 boardOriginWorld, boardRight, boardUp, boardFront;
    private float boardScale;
    private Material cueMaterial;
    private Mesh cueMesh;
    private const float CueSpeedMin = 0.2f, CueSpeedSpan = 0.6f;   // board units per second at idle-visible and full activity (design choice)
    private const float CueLength = 0.15f, CueHalfWidth = 0.07f, CueLift = 0.055f;   // board units; the arrowhead is wider than the 0.055 pipe

    /// <summary>The selected wall component, or null.</summary>
    public string SelectedComponent { get { return selection.Selected; } }
    /// <summary>Number of route cues built (one per directed route).</summary>
    public int FlowCueCount { get { return cues.Count; } }

    private void Awake()
    {
        if (bridge == null) bridge = FindAnyObjectByType<PlantBridge>();
        roomBinder = GetComponentInParent<ControlRoom3DBinder>();
        block = new MaterialPropertyBlock();
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("System_"))
            {
                var nodeRenderer = t.GetComponent<Renderer>();
                string nodeName = t.name.Substring(7);
                if (nodeRenderer != null && WallDetailModel.IsComponent(nodeName)) nodes[nodeName] = nodeRenderer;
            }
            if (t.name.StartsWith("Status_"))
            {
                var r = t.GetComponent<Renderer>();
                if (r != null) lamps[t.name.Substring(7)] = r;
            }
            if (t.name.StartsWith("Pipe_"))
            {
                int end = t.name.LastIndexOf('_');
                var r = t.GetComponent<Renderer>();
                if (r != null && end > 5) routes[r] = t.name.Substring(5, end - 5);
            }
            if (t.name.StartsWith("RodBank_") && t.parent != null) rods[t] = t.localPosition;
            if (t.name == "Core_Fuel") fuel = t.GetComponent<Renderer>();
        }
        MountMetrics();
        BuildFlowCues();
    }

    /// <summary>
    /// A click on a wall component (DiegeticInteractable on its System_ node): selects it, clears it when it is already selected,
    /// moves the selection when another is clicked. Read-only: nothing is submitted to the plant.
    /// </summary>
    public bool ClickComponent(string component)
    {
        if (!selection.Click(component)) return false;
        InputLog.Event(InputLogFormat.Press, "row", "wall:" + component, "kind", selection.Selected == null ? "wall_clear" : "wall_select");
        ApplySnapshot(lastSnapshot);
        return true;
    }

    /// <summary>Clears the selection (capture and test code).</summary>
    public void ClearSelection()
    {
        selection.Clear();
        ApplySnapshot(lastSnapshot);
    }

    /// <summary>Board coordinates (x right, z up, as authored) to a world point on the pipe plane.</summary>
    public Vector3 BoardToWorld(double x, double z)
    {
        return boardOriginWorld + boardRight * ((float)x * boardScale) + boardUp * ((float)z * boardScale);
    }

    /// <summary>The cue object of a directed route (null when none was built); active only while the route has a direction.</summary>
    public Transform CueFor(string route)
    {
        for (int i = 0; i < cues.Count; i++) if (cues[i].Route == route) return cues[i].Cue;
        return null;
    }

    // Calibrates the authored board plane against two pipe segments whose board positions the model knows, then builds one arrowhead per
    // directed route. A wrong calibration is caught by the play capture (every route's first point must land on its own pipe).
    private void BuildFlowCues()
    {
        var a = FindPipe("PrimaryHot", 0);
        var b = FindPipe("Grid", 0);
        if (a == null || b == null || fuel == null) return;
        double[][] pa = PlantWallMapModel.RoutePath("PrimaryHot"), pb = PlantWallMapModel.RoutePath("Grid");
        double ax = (pa[0][0] + pa[1][0]) * 0.5, az = (pa[0][1] + pa[1][1]) * 0.5;
        double bx = (pb[0][0] + pb[1][0]) * 0.5;
        Vector3 d = b.bounds.center - a.bounds.center;
        float boardDx = (float)(bx - ax);
        if (d.sqrMagnitude < 1e-8f || Mathf.Abs(boardDx) < 1e-6f) return;
        boardScale = d.magnitude / Mathf.Abs(boardDx);
        boardRight = d.normalized * Mathf.Sign(boardDx);
        boardUp = transform.up;
        boardOriginWorld = a.bounds.center - boardRight * ((float)ax * boardScale) - boardUp * ((float)az * boardScale);
        Vector3 front = Vector3.Cross(boardRight, boardUp);
        Renderer back = null;
        foreach (var r in GetComponentsInChildren<Renderer>(true)) if (r.name == "Board_Back") { back = r; break; }
        if (back != null && Vector3.Dot(front, fuel.bounds.center - back.bounds.center) < 0f) front = -front;
        boardFront = front.normalized;

        cueMesh = new Mesh { name = "FlowCueArrow" };
        cueMesh.vertices = new[] { new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(-0.2f, 0f, 0f), new Vector3(-0.5f, -0.5f, 0f) };
        cueMesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 };   // both windings: visible from any side
        cueMesh.RecalculateBounds();
        cueMaterial = new Material(Shader.Find("Unlit/Color"));
        cueMaterial.color = ThemeColors.TextBody;
        foreach (string route in PlantWallMapModel.DirectedRoutes)
        {
            var path = PlantWallMapModel.RoutePath(route);
            var go = new GameObject("FlowCue_" + route);
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = cueMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = cueMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            float length = 0f;
            for (int i = 0; i + 1 < path.Length; i++)
            {
                double sx = path[i + 1][0] - path[i][0], sz = path[i + 1][1] - path[i][1];
                length += (float)System.Math.Sqrt(sx * sx + sz * sz);
            }
            cues.Add(new FlowCue { Route = route, Cue = go.transform, Path = path, Length = length });
        }
    }

    private Renderer FindPipe(string route, int index)
    {
        foreach (var pair in routes)
            if (pair.Value == route && pair.Key.name == "Pipe_" + route + "_" + index.ToString("00")) return pair.Key;
        return null;
    }

    // Places a cue at distance `along` (board units) on its polyline, pointing along the segment it is on.
    private void PlaceCue(FlowCue cue, float along)
    {
        float remaining = along;
        for (int i = 0; i + 1 < cue.Path.Length; i++)
        {
            double dx = cue.Path[i + 1][0] - cue.Path[i][0], dz = cue.Path[i + 1][1] - cue.Path[i][1];
            float seg = (float)System.Math.Sqrt(dx * dx + dz * dz);
            if (remaining > seg && i + 2 < cue.Path.Length) { remaining -= seg; continue; }
            float f = seg > 0f ? Mathf.Clamp01(remaining / seg) : 0f;
            Vector3 heading = (boardRight * (float)dx + boardUp * (float)dz).normalized;
            Vector3 pos = BoardToWorld(cue.Path[i][0] + dx * f, cue.Path[i][1] + dz * f) + boardFront * (CueLift * boardScale);
            cue.Cue.position = pos;
            cue.Cue.rotation = Quaternion.LookRotation(boardFront, Vector3.Cross(boardFront, heading));
            cue.Cue.localScale = new Vector3(CueLength, CueHalfWidth * 2f, 1f) * (boardScale / Mathf.Max(1e-4f, transform.lossyScale.x));
            return;
        }
    }

    private void UpdateCues(PlantSnapshot s)
    {
        for (int i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];
            bool on = PlantWallMapModel.RouteDirection(cue.Route, s) > 0;
            if (cue.Cue.gameObject.activeSelf != on) cue.Cue.gameObject.SetActive(on);
            if (!on) continue;
            double activity = PlantWallMapModel.RouteActivity(cue.Route, s);
            cue.Phase = Mathf.Repeat(cue.Phase + Time.deltaTime * (CueSpeedMin + CueSpeedSpan * (float)activity), cue.Length);
            PlaceCue(cue, cue.Phase);
        }
    }

    private void ApplySelectionHighlight()
    {
        if (highlightApplied == selection.Selected) return;
        Renderer old, now;
        if (highlightApplied != null && nodes.TryGetValue(highlightApplied, out old)) old.SetPropertyBlock(null);
        highlightApplied = selection.Selected;
        if (highlightApplied == null || !nodes.TryGetValue(highlightApplied, out now)) return;
        var m = now.sharedMaterial;
        Color baseColor = m != null && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m != null && m.HasProperty("_Color") ? m.color : ThemeColors.TextBody;
        Color color = Color.Lerp(baseColor, ThemeColors.HighlightPulse, 0.75f);
        now.GetPropertyBlock(block);
        block.SetColor("_Color", color);
        block.SetColor("_BaseColor", color);
        block.SetColor("_EmissionColor", ThemeColors.HighlightPulse * 0.6f);
        now.SetPropertyBlock(block);
    }

    private void MountMetrics()
    {
        Renderer board = null;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r.name == "Board_Back") { board = r; break; }
        if (board == null) return;
        // The wrapper has a uniform scale and faces into the room; the plate is flush beneath the diagram.
        Vector3 center = transform.InverseTransformPoint(board.bounds.center);
        float width = board.bounds.size.x / Mathf.Abs(transform.lossyScale.x);
        float height = board.bounds.size.y / Mathf.Abs(transform.lossyScale.y);
        float face = fuel != null && Vector3.Dot(fuel.bounds.center - board.bounds.center, transform.forward) < 0f ? -1f : 1f;
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = "LiveMetricsPlate";
        plate.transform.SetParent(transform, false);
        plate.transform.localPosition = new Vector3(center.x, center.y - height * 0.5f - 0.48f, center.z + face * 0.08f);
        plate.transform.localScale = new Vector3(width, 0.92f, 0.12f);
        Destroy(plate.GetComponent<Collider>());
        plate.layer = gameObject.layer;
        metricsMaterial = new Material(Shader.Find("Unlit/Color"));
        metricsMaterial.color = ThemeColors.SurfaceScreen;
        plate.GetComponent<Renderer>().sharedMaterial = metricsMaterial;
        var go = new GameObject("LiveMetrics");
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = plate.transform.localPosition + new Vector3(0f, 0.37f, face * 0.075f);
        go.transform.localRotation = face > 0f ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
        metrics = go.AddComponent<TextMeshPro>();
        metrics.fontSize = 1.5f;
        metrics.alignment = TextAlignmentOptions.Top;
        metrics.textWrappingMode = TextWrappingModes.NoWrap;
        metrics.color = ThemeColors.TextBody;
        metrics.rectTransform.pivot = new Vector2(0.5f, 1f);
        metrics.rectTransform.sizeDelta = new Vector2(width - 0.24f, 0.75f);

        // The selected component's readout (or the select prompt) sits on a second plate directly beneath the summary plate.
        var plate2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate2.name = "LiveDetailPlate";
        plate2.transform.SetParent(transform, false);
        plate2.transform.localPosition = plate.transform.localPosition + new Vector3(0f, -1.0f, 0f);
        plate2.transform.localScale = new Vector3(width, 0.92f, 0.12f);
        Destroy(plate2.GetComponent<Collider>());
        plate2.layer = gameObject.layer;
        plate2.GetComponent<Renderer>().sharedMaterial = metricsMaterial;
        var go2 = new GameObject("LiveDetail");
        go2.layer = gameObject.layer;
        go2.transform.SetParent(transform, false);
        go2.transform.localPosition = plate2.transform.localPosition + new Vector3(0f, 0.44f, face * 0.075f);
        go2.transform.localRotation = go.transform.localRotation;
        detail = go2.AddComponent<TextMeshPro>();
        detail.enableAutoSizing = true;
        detail.fontSizeMin = 0.6f;
        // Auto-size still fits the text to the plate's own bounds; the cap only lets short readouts use the room (a 4-line readout was
        // 6.7 px tall at the 28 degree wall pose, under the 10 px floor, with most of the plate empty).
        detail.fontSizeMax = 2.4f;
        detail.alignment = TextAlignmentOptions.Top;
        detail.lineSpacing = -15f;   // a 4-line readout is height-limited; tighter lines raise the glyph size (upper-case text, no descender clash)
        detail.textWrappingMode = TextWrappingModes.Normal;
        detail.color = ThemeColors.TextBody;
        detail.rectTransform.pivot = new Vector2(0.5f, 1f);
        detail.rectTransform.sizeDelta = new Vector2(width - 0.24f, 0.86f);
    }

    private void OnDestroy()
    {
        if (metricsMaterial != null) Destroy(metricsMaterial);
        if (cueMaterial != null) Destroy(cueMaterial);
        if (cueMesh != null) Destroy(cueMesh);
    }

    private void Update()
    {
        ApplySnapshot(bridge != null ? bridge.GetSnapshot() : null);
    }

    // Kept separate from snapshot acquisition so the actual scene binding can be exercised in play mode.
    public void ApplySnapshot(PlantSnapshot s)
    {
        lastSnapshot = s;
        ApplySelectionHighlight();
        UpdateCues(s);
        if (fuel != null)
        {
            bool available = s != null && !double.IsNaN(s.Power) && !double.IsInfinity(s.Power) &&
                !double.IsNaN(s.FuelTempC) && !double.IsInfinity(s.FuelTempC);
            Color tint = available ? Color.Lerp(ThemeColors.ToColor(ReactorSim.Ui.Theme.CoreGlowCold),
                ThemeColors.ToColor(ReactorSim.Ui.Theme.CoreGlowHot), (float)GlowMapping.FuelTemperatureLevel(s.FuelTempC)) : ThemeColors.TextDim;
            fuel.GetPropertyBlock(block);
            block.SetColor("_Color", tint);
            block.SetColor("_BaseColor", tint);
            block.SetColor("_EmissionColor", tint * (available ? (float)GlowMapping.RackEmission(s.Power) : 0f));
            fuel.SetPropertyBlock(block);
        }
        foreach (var pair in routes)
        {
            double activity = PlantWallMapModel.RouteActivity(pair.Value, s);
            var material = pair.Key.sharedMaterial;
            // Unknown routes retain their authored circuit color, including after a reading disappears.
            if (double.IsNaN(activity)) { pair.Key.SetPropertyBlock(null); continue; }
            Color baseColor = material != null && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") :
                material != null && material.HasProperty("_Color") ? material.color : ThemeColors.TextBody;
            Color color = baseColor * Mathf.Lerp(0.3f, 1f, (float)activity);
            pair.Key.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);
            block.SetColor("_EmissionColor", baseColor * ((float)activity * 0.45f));
            pair.Key.SetPropertyBlock(block);
        }
        foreach (var pair in rods)
        {
            double insertion = PlantWallMapModel.BankInsertion(pair.Key.name.Substring(8), s);
            var rodRenderer = pair.Key.GetComponent<Renderer>();
            if (rodRenderer != null) rodRenderer.enabled = !double.IsNaN(insertion);
            if (double.IsNaN(insertion)) { pair.Key.localPosition = pair.Value; continue; }
            // Move down in the mounted board's vertical direction, preserving the imported FBX axes.
            Vector3 offset = pair.Key.parent.InverseTransformVector(-transform.up * (0.3f * (float)insertion * transform.lossyScale.y));
            pair.Key.localPosition = pair.Value + offset;
        }
        foreach (var pair in lamps)
        {
            Color color;
            switch (PlantWallMapModel.Component(pair.Key, s))
            {
                case PlantWallMapModel.Status.Normal: color = ThemeColors.StatusOk; break;
                case PlantWallMapModel.Status.Warning: color = ThemeColors.StatusCaution; break;
                case PlantWallMapModel.Status.Alarm: color = ThemeColors.StatusCritical; break;
                case PlantWallMapModel.Status.Offline: color = ThemeColors.StatusNormal; break;
                default: color = ThemeColors.TextDim; break;
            }
            pair.Value.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);
            block.SetColor("_EmissionColor", color);
            pair.Value.SetPropertyBlock(block);
        }
        if (metrics == null) return;
        var alarmContext = roomBinder != null ? roomBinder.CurrentLampContext() : LampContext.Default;
        string summary = PlantWallMapModel.Summary(s, alarmContext);
        if (summary != lastSummary) { lastSummary = summary; metrics.text = summary; }
        if (detail != null)
        {
            string shown = WallDetailModel.DetailText(selection.Selected, s);
            if (shown != lastDetail) { lastDetail = shown; detail.text = shown; }
        }
    }
}
