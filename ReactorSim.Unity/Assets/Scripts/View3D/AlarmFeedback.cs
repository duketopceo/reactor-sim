using System.Collections.Generic;
using UnityEngine;
using ReactorSim;

// Dramatic feedback (R5): turns plant edges into sound, shake and light.
//  - SCRAM / BOP trip edge: trip stinger + hard camera shake
//  - PORV lift, new high-pressure or high-fuel-temp alarm: shorter sting + smaller shake
//  - Chaos-class intents landing in the intent log: short sting + small shake
//  - while any alarm holds: klaxon beacons pulse red and the room lights tint and flicker toward red
// The klaxon loop itself lives in ProceduralPlantAudio.
public class AlarmFeedback : MonoBehaviour
{
    public PlantBridge bridge;
    public CameraShake shake;
    public ProceduralPlantAudio audioFx;

    private sealed class RoomLight
    {
        public Light Light;
        public Color BaseColor;
        public float BaseIntensity;
    }

    private readonly List<RoomLight> roomLights = new List<RoomLight>();
    private readonly List<Light> beaconLights = new List<Light>();
    private readonly List<Renderer> beaconRenderers = new List<Renderer>();
    private MaterialPropertyBlock mpb;

    private bool primed;
    private bool prevScrammed;
    private int prevTripMask;
    private bool prevRelief;
    private bool prevHot;
    private long lastChaosSeq = -1;
    private float nextChaosPoll;
    private float level;

    private void Start()
    {
        mpb = new MaterialPropertyBlock();
        if (bridge == null) bridge = FindAnyObjectByType<PlantBridge>();
        if (audioFx == null) audioFx = FindAnyObjectByType<ProceduralPlantAudio>();
        if (shake == null && Camera.main != null) shake = Camera.main.GetComponent<CameraShake>();
        if (shake == null) shake = FindAnyObjectByType<CameraShake>();

        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.name.StartsWith("BIND_KLAXON_Beacon_")) continue;
            beaconRenderers.Add(r);
            var go = new GameObject(r.name + "_Light");
            go.transform.SetParent(r.transform, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.1f, 0.05f);
            l.range = 9f;
            l.intensity = 0f;
            beaconLights.Add(l);
        }
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (beaconLights.Contains(l)) continue;
            if (l.GetComponentInParent<PlantEquipmentBinder>() != null) continue; // reactor hall lights are not room lights; the glow light is driven by power
            roomLights.Add(new RoomLight { Light = l, BaseColor = l.color, BaseIntensity = l.intensity });
        }
    }

    private void Update()
    {
        if (bridge == null || bridge.Sim == null) return;
        var s = bridge.GetSnapshot();
        if (s == null) return;

        bool hot = s.FuelTempC > 1200.0 || s.PrimaryPressureBar > 165.0;
        if (primed)
        {
            if (s.Scrammed && !prevScrammed) Trip(1f, 2.5f);
            if ((s.BopTripMask & ~prevTripMask) != 0) Trip(0.75f, 2f);
            if (s.ReliefOpen && !prevRelief) Sting(ProceduralPlantAudio.StingKind.Relief, 0.3f, 0.8f);
            if (hot && !prevHot) Sting(ProceduralPlantAudio.StingKind.Alarm, 0.35f, 0.9f);
        }
        prevScrammed = s.Scrammed;
        prevTripMask = s.BopTripMask;
        prevRelief = s.ReliefOpen;
        prevHot = hot;
        primed = true;

        PollChaos();
        UpdateLighting(s.Scrammed || s.BopTripMask != 0 || hot);
    }

    private void Trip(float shakeAmp, float shakeSeconds)
    {
        if (shake != null) shake.Kick(shakeAmp, shakeSeconds);
        if (audioFx != null) audioFx.PlaySting(ProceduralPlantAudio.StingKind.Trip);
    }

    private void Sting(ProceduralPlantAudio.StingKind kind, float shakeAmp, float shakeSeconds)
    {
        if (shake != null) shake.Kick(shakeAmp, shakeSeconds);
        if (audioFx != null) audioFx.PlaySting(kind);
    }

    // Chaos faults arrive as Chaos-class intents; the log is polled a few times a second (it copies).
    private void PollChaos()
    {
        if (Time.unscaledTime < nextChaosPoll) return;
        nextChaosPoll = Time.unscaledTime + 0.25f;
        List<IntentLogEntry> log;
        lock (bridge.SimLock) { log = bridge.Sim.GetIntentLogSnapshot(); }
        long newest = lastChaosSeq;
        bool fresh = false;
        for (int i = 0; i < log.Count; i++)
        {
            var e = log[i];
            if (e.Class != IntentClass.Chaos || e.Rejected || e.Seq <= lastChaosSeq) continue;
            if (e.Seq > newest) newest = e.Seq;
            fresh = true;
        }
        if (lastChaosSeq < 0)
        {
            // First poll: adopt existing history without stinging for it.
            lastChaosSeq = newest < 0 ? 0 : newest;
            return;
        }
        lastChaosSeq = newest;
        if (fresh) Sting(ProceduralPlantAudio.StingKind.Chaos, 0.4f, 1f);
    }

    private void UpdateLighting(bool alarm)
    {
        level = Mathf.MoveTowards(level, alarm ? 1f : 0f, Time.unscaledDeltaTime * 4f);
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 7f);
        for (int i = 0; i < beaconLights.Count; i++)
            if (beaconLights[i] != null) beaconLights[i].intensity = level * (1.5f + 3.5f * pulse);
        Color emission = new Color(1f, 0.1f, 0.05f) * (level * (1.2f + 2.5f * pulse));
        for (int i = 0; i < beaconRenderers.Count; i++)
        {
            var r = beaconRenderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_EmissionColor", emission);
            r.SetPropertyBlock(mpb);
        }
        Color red = new Color(1f, 0.15f, 0.1f);
        for (int i = 0; i < roomLights.Count; i++)
        {
            var rl = roomLights[i];
            if (rl.Light == null) continue;
            rl.Light.color = Color.Lerp(rl.BaseColor, red, 0.6f * level);
            rl.Light.intensity = rl.BaseIntensity * (1f - 0.4f * level * (1f - pulse));
        }
    }
}
