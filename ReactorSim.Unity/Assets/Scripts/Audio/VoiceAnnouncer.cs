using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ReactorSim;
using ReactorSim.Instruments;

// Kokoro robot voice lines (Assets/Audio/Voice, see tools/voice/README.md): speaks one authored line per plant event.
// The clips are assigned by the scene builder; this component only decides WHEN a line plays. Events come from the live
// snapshot (SCRAM, turbine, breaker sync, assist tier, criticality, alarms), the assistant advisory log, and the
// cold-start-to-grid procedure checklist (the step_* / heatup / power_ascension lines follow the current step).
// One line at a time on a dedicated source; a new SCRAM interrupts whatever is speaking. A short global gap and a
// per-line cooldown keep repeated state from machine-gunning the voice.
public class VoiceAnnouncer : MonoBehaviour
{
    public PlantBridge bridge;
    public AudioClip[] clips;
    public float volume = 0.9f;
    public float minInterval = 2.5f;      // real seconds between any two lines
    public float lineCooldown = 45f;      // per-line repeat guard

    private AudioSource voice;
    private readonly Dictionary<string, AudioClip> bank = new Dictionary<string, AudioClip>();
    private readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
    private float lastAny = -999f;

    private Procedure startupProc;
    private int prevProcIndex = -2;       // -2 = not yet seen

    private PlantSnapshot prev;
    private int prevAdvisoryCount = -1;
    private int prevAlarmCount;
    private bool hadAlarms;
    private int prevAssist = -1;
    private bool prevFluxHi, prevPressHi;

    private void Awake()
    {
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 0f;
        voice.volume = volume;
        if (clips != null)
            foreach (var c in clips)
                if (c != null) bank[c.name] = c;
        LoadStartupProcedure();
    }

    private void LoadStartupProcedure()
    {
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, "procedures.json");
            if (!File.Exists(path)) return;
            var all = ProcedureLoader.LoadFromFile(path);
            foreach (var p in all)
                if (p.Id == "cold-start-to-grid") { startupProc = p; break; }
        }
        catch (System.Exception e) { Debug.LogWarning("[VoiceAnnouncer] procedures.json: " + e.Message); }
    }

    private void Say(string key, bool interrupt = false)
    {
        if (key == null) return;
        AudioClip clip;
        if (!bank.TryGetValue(key, out clip) || clip == null) return;
        float now = Time.unscaledTime;
        float last;
        if (lastPlayed.TryGetValue(key, out last) && now - last < lineCooldown) return;
        if (!interrupt && (voice.isPlaying || now - lastAny < minInterval)) return;
        lastPlayed[key] = now;
        lastAny = now;
        voice.clip = clip;
        voice.Play();
    }

    private void Update()
    {
        if (bridge == null) bridge = FindAnyObjectByType<PlantBridge>();
        if (bridge == null || bridge.Sim == null || Time.timeScale <= 0f) return;
        PlantSnapshot s = bridge.GetSnapshot();
        if (s == null) return;

        // --- hard plant events ---
        if (prev != null)
        {
            if (s.Scrammed && !prev.Scrammed) Say("scram", true);
            if (!s.Scrammed && prev.Scrammed) Say("scram_reset");
            if (s.TurbineTripped && !prev.TurbineTripped) Say("turbine_trip");
            if (s.GeneratorSynced && !prev.GeneratorSynced) Say("sync");
            if (s.IsCritical && !prev.IsCritical) Say("critical");
            int tier = s.AssistTier;
            if (prevAssist >= 0 && tier != prevAssist)
            {
                if (tier == (int)AssistTier.Autopilot) Say("autopilot_on");
                else if (prevAssist == (int)AssistTier.Autopilot) Say("autopilot_off");
            }
            // High-power / high-pressure advisories as one-shot warnings.
            bool fluxHi = GaugeSources.HighFlux(s);
            if (fluxHi && !prevFluxHi) Say("alarm_power_high");
            prevFluxHi = fluxHi;
            bool pressHi = s.PrimaryPressureBar > 165.0;
            if (pressHi && !prevPressHi) Say("alarm_pressure_high");
            prevPressHi = pressHi;
        }

        // --- assistant alarms & advisories ---
        var eng = bridge.Sim.Plant.Assistant;
        if (eng != null)
        {
            int alarmCount = eng.ActiveAlarms != null ? eng.ActiveAlarms.Count : 0;
            if (prevAlarmCount == 0 && alarmCount > 0) hadAlarms = true;
            if (hadAlarms && alarmCount == 0) { Say("all_normal"); hadAlarms = false; }
            prevAlarmCount = alarmCount;
            foreach (var a in eng.ActiveAlarms)
            {
                if (a == null) continue;
                if (a.Code == "REACTIVITY_EXCURSION") Say("alarm_period");
                else if (a.Code == "FUEL_TEMP_HIGH") Say("alarm_temp_high");
            }

            int advCount = eng.MessageLog != null ? eng.MessageLog.Count : 0;
            if (prevAdvisoryCount >= 0 && advCount > prevAdvisoryCount)
            {
                var adv = eng.LatestAdvisory;
                if (adv != null)
                {
                    string text = ((adv.Finding ?? "") + " " + (adv.Recommendation ?? "")).ToLowerInvariant();
                    if (text.Contains("xenon")) Say("advise_xenon");
                    else if (text.Contains("borat") || text.Contains("boron")) Say("advise_borate");
                    else if (text.Contains("slow") || text.Contains("derate") || text.Contains("load")) Say("advise_slow");
                }
            }
            prevAdvisoryCount = advCount;
        }
        prevAssist = s.AssistTier;

        // --- startup procedure steps (cold-start-to-grid) ---
        if (startupProc != null)
        {
            var st = startupProc.Evaluate(s);
            int idx = st.CurrentIndex;
            string stepId = st.Current != null ? st.Current.Id : null;
            if (prevProcIndex == -2)
            {
                if (!st.Complete && idx >= 0) { Say("startup_begin"); SayStep(stepId); }
            }
            else if (idx != prevProcIndex)
            {
                // A step became current: previous step finished. Announce the new step's line.
                if (stepId != null) SayStep(stepId);
            }
            if (prevProcIndex >= 0 && st.Complete) Say("all_normal");
            prevProcIndex = idx;
        }

        prev = s;
        prevAssist = s.AssistTier;
    }

    private void SayStep(string stepId)
    {
        switch (stepId)
        {
            case "draw-vacuum": Say("step_vacuum"); break;
            case "start-rcps": Say("step_pumps"); break;
            case "pressurize": Say("step_pressurize"); break;
            case "pull-shutdown-banks": Say("step_shutdown_banks"); break;
            case "withdraw-bank-a": Say("step_bank_a"); break;
            case "dilute-boron": Say("step_dilute"); break;
            case "heat-up": Say("heatup"); break;
            case "roll-turbine": Say("turbine_roll"); break;
            case "raise-power": Say("power_ascension"); break;
            case "reach-critical": Say("critical"); break;
        }
    }
}
