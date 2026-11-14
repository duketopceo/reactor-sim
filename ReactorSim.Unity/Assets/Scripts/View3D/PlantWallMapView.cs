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
    private Renderer fuel;
    private ControlRoom3DBinder roomBinder;
    private Material metricsMaterial;

    private void Awake()
    {
        if (bridge == null) bridge = FindAnyObjectByType<PlantBridge>();
        roomBinder = GetComponentInParent<ControlRoom3DBinder>();
        block = new MaterialPropertyBlock();
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
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
    }

    private void OnDestroy()
    {
        if (metricsMaterial != null) Destroy(metricsMaterial);
    }

    private void Update()
    {
        ApplySnapshot(bridge != null ? bridge.GetSnapshot() : null);
    }

    // Kept separate from snapshot acquisition so the actual scene binding can be exercised in play mode.
    public void ApplySnapshot(PlantSnapshot s)
    {
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
        metrics.text = PlantWallMapModel.Summary(s, alarmContext);
    }
}
