using System.Collections.Generic;
using ReactorSim;
using TMPro;
using UnityEngine;

// Displays the current snapshot on the separate plant-overview asset. It never submits intents.
public sealed class PlantWallMapView : MonoBehaviour
{
    public PlantBridge bridge;
    private readonly Dictionary<string, Renderer> lamps = new Dictionary<string, Renderer>();
    private MaterialPropertyBlock block;
    private TextMeshPro metrics;

    private void Awake()
    {
        if (bridge == null) bridge = FindAnyObjectByType<PlantBridge>();
        block = new MaterialPropertyBlock();
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("Status_"))
            {
                var r = t.GetComponent<Renderer>();
                if (r != null) lamps[t.name.Substring(7)] = r;
            }
            if (t.name == "MetricsSocket")
            {
                var go = new GameObject("LiveMetrics");
                go.transform.SetParent(t, false);
                metrics = go.AddComponent<TextMeshPro>();
                metrics.fontSize = 0.55f;
                metrics.alignment = TextAlignmentOptions.Center;
                metrics.color = Color.white;
                metrics.rectTransform.sizeDelta = new Vector2(7f, 0.65f);
            }
        }
    }

    private void Update()
    {
        PlantSnapshot s = bridge != null ? bridge.GetSnapshot() : null;
        foreach (var pair in lamps)
        {
            Color color;
            switch (PlantWallMapModel.Component(pair.Key, s))
            {
                case PlantWallMapModel.Status.Normal: color = new Color(0.1f, 0.8f, 0.3f); break;
                case PlantWallMapModel.Status.Warning: color = new Color(1f, 0.6f, 0.05f); break;
                case PlantWallMapModel.Status.Alarm: color = Color.red; break;
                case PlantWallMapModel.Status.Offline: color = new Color(0.3f, 0.45f, 0.6f); break;
                default: color = Color.gray; break;
            }
            pair.Value.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            block.SetColor("_EmissionColor", color);
            pair.Value.SetPropertyBlock(block);
        }
        if (metrics == null) return;
        if (s == null) { metrics.text = "PLANT DATA UNAVAILABLE"; return; }
        int alarms = 0;
        foreach (string name in ReactorSim.Instruments.LampPredicates.AlarmNames)
            if (ReactorSim.Instruments.LampPredicates.Evaluate(name, s, ReactorSim.Instruments.LampContext.Default)) alarms++;
        metrics.text = (s.Mode ?? "N/M") + "   ALARMS " + alarms + "   " + (s.Scrammed ? "SCRAM" : "NO SCRAM") + "\n" +
            "POWER " + PlantWallMapModel.Reading(s.Power * 100, "%") +
            "   FUEL " + PlantWallMapModel.Reading(s.FuelTempC, "C") +
            "   PRIMARY " + PlantWallMapModel.Reading(s.PrimaryPressureBar, "bar") +
            "   SG " + PlantWallMapModel.Reading(s.SecondaryPressureBar, "bar") + "\n" +
            "FLOW " + PlantWallMapModel.Reading(s.PrimaryFlowFrac * 100, "%") +
            "   TURBINE " + PlantWallMapModel.Reading(s.TurbineRpm, "rpm", 0) +
            "   GRID " + PlantWallMapModel.Reading(s.GridFrequencyHz, "Hz") +
            "   COND " + PlantWallMapModel.Reading(s.CondenserPressureBar, "bar", 2) + "\n" +
            "RHO " + PlantWallMapModel.Reading(s.TotalReactivityPcm, "pcm", 0) +
            "   BORON " + PlantWallMapModel.Reading(s.BoronPpm, "ppm", 0) +
            "   FEED " + PlantWallMapModel.Reading(s.FeedwaterFlowFrac * 100, "%") +
            "   OUTPUT " + PlantWallMapModel.Reading(s.GeneratorKw, "kW", 0);
    }
}
