using System;
using System.Collections.Generic;
using ReactorSim;
using UnityEngine;

// Additional spatial layers complement the existing pump, turbine, control, voice and alarm audio.
// These are synthesized locally and follow the plant snapshot; master volume is AudioListener.volume and the Plant category gain is multiplied into every source here.
public sealed class ReactorSoundscape : MonoBehaviour
{
    public PlantBridge bridge;
    private readonly List<AudioClip> clips = new List<AudioClip>();
    private AudioSource core, water, primaryRelief, secondaryRelief, bypass, cooling, generator, detector;
    private bool positioned;
    private float tickCountdown = 1f;
    private readonly System.Random random = new System.Random(8127);

    private void Awake()
    {
        if (bridge == null) bridge = FindAnyObjectByType<PlantBridge>();
        core = MakeSource("ReactorCoreHum", MakeLoop("core", 60, 0.06f, 21), true);
        water = MakeSource("PoolWater", MakeLoop("water", 13, 0.13f, 22), true);
        primaryRelief = MakeSource("PrimaryReliefSteam", MakeLoop("relief", 0, 0.65f, 23), true);
        secondaryRelief = MakeSource("SecondaryReliefSteam", MakeLoop("relief", 0, 0.65f, 26), true);
        bypass = MakeSource("BypassSteam", MakeLoop("bypass", 0, 0.5f, 27), true);
        cooling = MakeSource("CoolingMachinery", MakeLoop("cooling", 42, 0.09f, 24), true);
        generator = MakeSource("GeneratorElectrical", MakeLoop("generator", 60, 0.02f, 25), true);
        detector = MakeSource("FluxDetector", MakeTick(), false);
        detector.spatialBlend = 0f;
        detector.volume = 0.35f;
    }

    private static Vector3 HallPoint(double[] p) { return new Vector3((float)p[0], (float)p[1], (float)p[2]); }

    private AudioSource MakeSource(string name, AudioClip clip, bool spatial)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = name != "FluxDetector";
        source.playOnAwake = false;
        source.volume = 0f;
        source.spatialBlend = spatial ? 0.7f : 0f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 4f;
        source.maxDistance = 45f;
        if (source.loop) source.Play();
        return source;
    }

    private AudioClip MakeLoop(string name, int frequency, float noiseAmount, int seed)
    {
        const int sampleRate = 22050, seconds = 3;
        int count = sampleRate * seconds;
        var data = new float[count];
        var rng = new System.Random(seed);
        float filtered = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)sampleRate;
            float n = (float)(rng.NextDouble() * 2 - 1);
            filtered += (n - filtered) * (name == "relief" ? 0.35f : (name == "bypass" ? 0.2f : 0.035f));
            float hum = frequency == 0 ? 0f : Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.42f
                + Mathf.Sin(2f * Mathf.PI * frequency * 2 * t) * 0.12f;
            float fade = Mathf.Min(1f, Mathf.Min(i, count - 1 - i) / (sampleRate * 0.025f));
            data[i] = Mathf.Clamp((hum + filtered * noiseAmount) * fade, -1f, 1f);
        }
        var clip = AudioClip.Create("reactor_" + name, count, 1, sampleRate, false);
        clip.SetData(data, 0);
        clips.Add(clip);
        return clip;
    }

    private AudioClip MakeTick()
    {
        const int rate = 22050;
        var data = new float[(int)(rate * 0.025f)];
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)rate;
            data[i] = Mathf.Sin(2f * Mathf.PI * 2200f * t) * Mathf.Exp(-220f * t) * 0.45f;
        }
        var clip = AudioClip.Create("reactor_detector_tick", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        clips.Add(clip);
        return clip;
    }

    private void Update()
    {
        if (bridge == null || bridge.Sim == null) return;
        if (!positioned)
        {
            var hall = HallVisibility.FindHall();
            if (hall != null)
            {
                core.transform.position = hall.transform.TransformPoint(new Vector3(0, HallDimensions.VesselBase + 5.2f, HallDimensions.ModuleZ));
                water.transform.position = hall.transform.TransformPoint(new Vector3(-HallDimensions.VesselShiftX, HallDimensions.WaterDepth + HallDimensions.GalleryEyeAboveWater, -HallDimensions.PoolAxisFromWall + 1f));
                // Relief and bypass sound come from the same hall points as their steam jets (HallBuilder.BuildReliefAnchors).
                primaryRelief.transform.position = hall.transform.TransformPoint(HallPoint(ReliefAnchors.Primary()));
                secondaryRelief.transform.position = hall.transform.TransformPoint(HallPoint(ReliefAnchors.SecondarySound()));
                bypass.transform.position = hall.transform.TransformPoint(HallPoint(ReliefAnchors.Bypass()));
                cooling.transform.position = hall.transform.TransformPoint(new Vector3(4f, HallDimensions.VesselBase + 6f, HallDimensions.ModuleZ));
                generator.transform.position = hall.transform.TransformPoint(new Vector3(-3f, HallDimensions.VesselBase + 4f, HallDimensions.ModuleZ));
                positioned = true;
            }
        }
        var mix = ReactorSoundMix.From(bridge.GetSnapshot());
        float step = Time.unscaledDeltaTime * 0.8f;
        float gain = UserSettings.Audio.PlantGain;
        core.volume = Mathf.MoveTowards(core.volume, positioned ? mix.Core * gain : 0, step);
        water.volume = Mathf.MoveTowards(water.volume, positioned ? mix.Water * gain : 0, step);
        primaryRelief.volume = Mathf.MoveTowards(primaryRelief.volume, positioned ? mix.PrimaryRelief * gain : 0, step);
        secondaryRelief.volume = Mathf.MoveTowards(secondaryRelief.volume, positioned ? mix.SecondaryRelief * gain : 0, step);
        bypass.volume = Mathf.MoveTowards(bypass.volume, positioned ? mix.DumpSteam * gain : 0, step);
        cooling.volume = Mathf.MoveTowards(cooling.volume, positioned ? mix.Cooling * gain : 0, step);
        generator.volume = Mathf.MoveTowards(generator.volume, positioned ? mix.Generator * gain : 0, step);
        tickCountdown -= Time.deltaTime;
        if (!bridge.Paused && mix.DetectorRate > 0 && tickCountdown <= 0)
        {
            detector.PlayOneShot(detector.clip, 0.10f * gain);
            tickCountdown = (0.75f + (float)random.NextDouble() * 0.5f) / mix.DetectorRate;
        }
    }

    private void OnDestroy()
    {
        foreach (var clip in clips) if (clip != null) Destroy(clip);
    }
}
