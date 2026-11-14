using UnityEngine;
using ReactorSim;
using ReactorSim.Controls;

// Phase 2 procedural plant audio: generates all SFX clips in code (no audio assets shipped).
// rcp hum (flow), steam hiss (steam demand), rod clunks (bank movement), button clicks,
// klaxon (SCRAM/high-temp), geiger (flux) — synthesized oscillators, zero external files.
public class ProceduralPlantAudio : MonoBehaviour
{
    public PlantBridge bridge;
    public AudioSource rcpLoop;
    public AudioSource steamLoop;
    public AudioSource scramKlaxon;
    public AudioSource sfxSource;
    public AudioSource turbineLoop;

    /// <summary>The scene's audio, for control sounds (ControlRoom3DBinder plays them through PlayControl).</summary>
    public static ProceduralPlantAudio Instance { get; private set; }

    // The horn sounds while any of these conditions is raised and not yet acknowledged. Tracked per condition, so after
    // an acknowledge a NEW condition (a turbine trip during a scram) sounds the horn again.
    private static readonly string[] HornAlarms = { "scram", "bop-trip", "high-pressure", "high-fuel-temp" };
    private readonly ReactorSim.Instruments.AlarmTracker hornTracker = new ReactorSim.Instruments.AlarmTracker();
    private PlantSnapshot hornSnap;
    private System.Func<string, bool> hornRaised;

    private double[] lastRodInsertion = new double[4] { 1, 1, 1, 1 };
    private float rodMoveCooldown;

    private bool HornConditionRaised(string name)
    {
        var s = hornSnap;
        if (s == null) return false;
        switch (name)
        {
            case "scram": return s.Scrammed;
            case "bop-trip": return s.BopTripMask != 0;
            case "high-pressure": return s.PrimaryPressureBar > 165.0;
            case "high-fuel-temp": return s.FuelTempC > 1200.0;
            default: return false;
        }
    }

    private void Awake()
    {
        Instance = this;
        if (bridge == null)
            bridge = FindAnyObjectByType<PlantBridge>();
        EnsureSources();
        GenerateClips();
    }

    private void EnsureSources()
    {
        if (rcpLoop == null)
        {
            rcpLoop = gameObject.AddComponent<AudioSource>();
            rcpLoop.loop = true;
            rcpLoop.playOnAwake = true;
        }
        if (steamLoop == null)
        {
            steamLoop = gameObject.AddComponent<AudioSource>();
            steamLoop.loop = true;
            steamLoop.playOnAwake = true;
        }
        if (scramKlaxon == null)
        {
            scramKlaxon = gameObject.AddComponent<AudioSource>();
            scramKlaxon.loop = true;
        }
        if (sfxSource == null)
            sfxSource = gameObject.AddComponent<AudioSource>();
        if (turbineLoop == null)
        {
            turbineLoop = gameObject.AddComponent<AudioSource>();
            turbineLoop.loop = true;
            turbineLoop.volume = 0f;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ---- control feel (U17): detent tick on a dial, clunk on a switch or button, thunk on the generator breaker ----

    private readonly System.Collections.Generic.Dictionary<ControlSound, AudioClip> controlClips =
        new System.Collections.Generic.Dictionary<ControlSound, AudioClip>();

    /// <summary>One-shot control sound (clips are synthesised on first use).</summary>
    public void PlayControl(ControlSound kind)
    {
        if (sfxSource == null) return;
        AudioClip clip;
        if (!controlClips.TryGetValue(kind, out clip) || clip == null)
        {
            clip = MakeControlClip(kind);
            controlClips[kind] = clip;
        }
        sfxSource.PlayOneShot(clip, kind == ControlSound.Breaker ? 1f : (kind == ControlSound.Tick ? 0.35f : (kind == ControlSound.Reject ? 0.5f : 0.6f)));
    }

    // Tick: a 4 ms click. Clunk: a short low thump with a noisy edge. Breaker: a deep, longer thud with a metallic ring.
    private AudioClip MakeControlClip(ControlSound kind)
    {
        const int rate = 22050;
        float duration = kind == ControlSound.Tick ? 0.025f : (kind == ControlSound.Clunk ? 0.12f : (kind == ControlSound.Reject ? 0.16f : 0.45f));
        float f = kind == ControlSound.Tick ? 1800f : (kind == ControlSound.Clunk ? 120f : (kind == ControlSound.Reject ? 82f : 55f));
        float decay = kind == ControlSound.Tick ? 160f : (kind == ControlSound.Clunk ? 30f : (kind == ControlSound.Reject ? 22f : 9f));
        int samples = Mathf.Max(1, (int)(rate * duration));
        AudioClip clip = AudioClip.Create("proc_control_" + kind, samples, 1, rate, false);
        float[] data = new float[samples];
        System.Random rng = new System.Random(17 + (int)kind);
        float phase = 0f;
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            float env = Mathf.Exp(-decay * t);
            phase += f / rate;
            float v = Mathf.Sin(phase * Mathf.PI * 2f);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            if (kind == ControlSound.Tick) v = v * 0.5f + noise * 0.5f;
            else if (kind == ControlSound.Clunk) v = v * 0.8f + noise * 0.25f * Mathf.Exp(-300f * t);
            else if (kind == ControlSound.Reject)
            {
                // Dull refusal: two soft thuds, lower and duller than a real clunk.
                float gate = t < 0.07f ? 1f : (t < 0.11f ? 0.15f : 0.85f);
                v = v * 0.55f + noise * 0.45f * Mathf.Exp(-200f * t);
                v *= gate;
            }
            else v = v * 0.9f + Mathf.Sin(t * 880f * Mathf.PI * 2f) * 0.18f * Mathf.Exp(-14f * t) + noise * 0.3f * Mathf.Exp(-120f * t);
            data[i] = v * env * 0.9f;
        }
        clip.SetData(data, 0);
        return clip;
    }

    private AudioClip MakeTone(float freq, float duration, float volume, bool noise = false)
    {
        int rate = 22050;
        int samples = Mathf.Max(1, (int)(rate * duration));
        AudioClip clip = AudioClip.Create("proc_" + freq + "_" + volume, samples, 1, rate, false);
        float[] data = new float[samples];
        float phase = 0f;
        System.Random rng = new System.Random((int)(freq * 100));
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            float v;
            if (noise)
                v = (float)(rng.NextDouble() * 2.0 - 1.0) * volume;
            else
            {
                phase += freq / rate;
                v = Mathf.Sin(phase * Mathf.PI * 2f) * volume;
            }
            float env = 1f - (i / (float)(samples - 1));
            data[i] = v * env;
        }
        clip.SetData(data, 0);
        return clip;
    }

    // Alternating 520/700 Hz tones with a touch of 2nd harmonic; short fades at tone edges avoid clicks.
    private AudioClip MakeKlaxon()
    {
        const int rate = 22050;
        const float duration = 2f;
        int samples = (int)(rate * duration);
        AudioClip clip = AudioClip.Create("proc_klaxon", samples, 1, rate, false);
        float[] data = new float[samples];
        float phase = 0f;
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            float seg = Mathf.Repeat(t, 1f);
            float f = seg < 0.5f ? 520f : 700f;
            phase += f / rate;
            float segT = Mathf.Repeat(seg, 0.5f);
            float fade = Mathf.Clamp01(Mathf.Min(segT, 0.5f - segT) / 0.01f);
            float v = Mathf.Sin(phase * Mathf.PI * 2f) * 0.55f + Mathf.Sin(phase * Mathf.PI * 4f) * 0.2f;
            data[i] = v * fade * 0.8f;
        }
        clip.SetData(data, 0);
        return clip;
    }

    public enum StingKind { Trip, Relief, Alarm, Chaos }

    private readonly System.Collections.Generic.Dictionary<StingKind, AudioClip> stings =
        new System.Collections.Generic.Dictionary<StingKind, AudioClip>();

    /// <summary>One-shot stinger for a trip, relief lift, new alarm or chaos event (clips built on first use).</summary>
    public void PlaySting(StingKind kind)
    {
        if (sfxSource == null) return;
        AudioClip clip;
        if (!stings.TryGetValue(kind, out clip) || clip == null)
        {
            clip = MakeSting(kind);
            stings[kind] = clip;
        }
        sfxSource.PlayOneShot(clip, kind == StingKind.Trip ? 1f : 0.7f);
    }

    private AudioClip MakeSting(StingKind kind)
    {
        const int rate = 22050;
        float duration, f0, f1;
        switch (kind)
        {
            case StingKind.Trip: duration = 1.4f; f0 = 900f; f1 = 140f; break;      // falling siren
            case StingKind.Relief: duration = 0.35f; f0 = 300f; f1 = 520f; break;    // short rising blip
            case StingKind.Alarm: duration = 0.5f; f0 = 660f; f1 = 660f; break;
            default: duration = 0.45f; f0 = 180f; f1 = 620f; break;                   // chaos: springy "boing"
        }
        int samples = Mathf.Max(1, (int)(rate * duration));
        AudioClip clip = AudioClip.Create("proc_sting_" + kind, samples, 1, rate, false);
        float[] data = new float[samples];
        float phase = 0f;
        for (int i = 0; i < samples; i++)
        {
            float u = i / (float)(samples - 1);
            float f = Mathf.Lerp(f0, f1, kind == StingKind.Chaos ? Mathf.Sin(u * Mathf.PI * 3f) * 0.5f + 0.5f : u);
            phase += f / rate;
            float env = Mathf.Clamp01(u / 0.03f) * (1f - u);
            data[i] = Mathf.Sin(phase * Mathf.PI * 2f) * 0.7f * env;
        }
        clip.SetData(data, 0);
        return clip;
    }

    // Continuous loops have short edge fades instead of a full-second decay and restart.
    private AudioClip MakeLoopClip(int frequency, bool noise)
    {
        const int rate = 22050, count = rate * 2;
        var data = new float[count];
        var rng = new System.Random(177 + frequency);
        float filtered = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)rate;
            float sample;
            if (noise)
            {
                float n = (float)(rng.NextDouble() * 2 - 1);
                filtered += (n - filtered) * 0.35f;
                sample = filtered * 0.55f;
            }
            else sample = Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.4f
                + Mathf.Sin(4f * Mathf.PI * frequency * t) * 0.1f;
            float fade = Mathf.Min(1f, Mathf.Min(i, count - 1 - i) / (rate * 0.008f));
            data[i] = sample * fade;
        }
        var clip = AudioClip.Create("reactor_loop_" + frequency, count, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void GenerateClips()
    {
        if (rcpLoop != null && rcpLoop.clip == null)
        {
            rcpLoop.clip = MakeLoopClip(85, false);
            if (!rcpLoop.isPlaying) rcpLoop.Play();
        }
        if (steamLoop != null && steamLoop.clip == null)
        {
            steamLoop.clip = MakeLoopClip(0, true);
            if (!steamLoop.isPlaying) steamLoop.Play();
        }
        if (scramKlaxon != null && scramKlaxon.clip == null)
        {
            // Two-tone klaxon whoop (loops cleanly: 2 s, four alternating half-second tones).
            scramKlaxon.clip = MakeKlaxon();
        }
    }

    private void Update()
    {
        if (bridge == null || bridge.Sim == null) return;
        PlantSnapshot s = bridge.GetSnapshot();

        if (rcpLoop != null)
        {
            rcpLoop.volume = Mathf.Clamp01((float)s.PrimaryFlowFrac) * 0.8f;
            rcpLoop.pitch = 0.6f + Mathf.Clamp01((float)s.PrimaryFlowFrac) * 0.8f;
        }
        if (steamLoop != null)
        {
            steamLoop.volume = Mathf.Clamp01((float)s.SteamDemandFrac) * 0.7f;
            steamLoop.pitch = 0.7f + Mathf.Clamp01((float)s.SteamDemandFrac) * 0.6f;
        }
        if (turbineLoop != null)
        {
            // A hum that rises with turbine speed (rpm 0..3000 and beyond).
            if (turbineLoop.clip == null)
            {
                turbineLoop.clip = MakeLoopClip(110, false);
                turbineLoop.Play();
            }
            float rpmFrac = Mathf.Clamp01((float)s.TurbineRpm / 3000f);
            turbineLoop.volume = rpmFrac * 0.28f;
            turbineLoop.pitch = 0.5f + rpmFrac * 0.9f;
        }
        if (scramKlaxon != null)
        {
            // Acknowledge / silence (AcknowledgeAlarms) quiets the conditions raised so far; a new one sounds again.
            hornSnap = s;
            if (hornRaised == null) hornRaised = HornConditionRaised;
            hornTracker.Update(HornAlarms, hornRaised, s.AlarmAckSeq);
            bool sound = hornTracker.AnyUnacked;
            if (sound && !scramKlaxon.isPlaying) scramKlaxon.Play();
            if (!sound && scramKlaxon.isPlaying) scramKlaxon.Stop();
        }
        // Rod clunk when any bank moves.
        rodMoveCooldown -= Time.deltaTime;
        if (s.BankInsertion != null && rodMoveCooldown <= 0f)
        {
            bool moved = false;
            for (int i = 0; i < 4 && i < s.BankInsertion.Length; i++)
            {
                if (System.Math.Abs(s.BankInsertion[i] - lastRodInsertion[i]) > 0.001) moved = true;
                lastRodInsertion[i] = s.BankInsertion[i];
            }
            if (moved && sfxSource != null)
            {
                sfxSource.clip = MakeTone(140f, 0.09f, 0.8f);
                sfxSource.Play();
                rodMoveCooldown = 0.15f;
            }
        }
    }
}

