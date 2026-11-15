using System;

// Pure audio category levels (no UnityEngine; linked into ReactorSim.Core.Tests). Four sliders, all 0-1 and all full by
// default: Master (applied once, to the listener), and the Plant, Alarms and Voice categories (multiplied into each
// source's own volume). The alarm mute is an override, not a slider: it gives the alarm category gain 0 whatever the
// Alarms slider says. Stored values are independent PlayerPrefs keys, so an absent key means "default", never an error.
public readonly struct AudioCategories
{
    public const float DefaultLevel = 1f;

    public readonly float Master;
    public readonly float Plant;
    public readonly float Alarms;
    public readonly float Voice;
    public readonly bool AlarmsMuted;

    public AudioCategories(float master, float plant, float alarms, float voice, bool alarmsMuted)
    {
        Master = Clamp(master);
        Plant = Clamp(plant);
        Alarms = Clamp(alarms);
        Voice = Clamp(voice);
        AlarmsMuted = alarmsMuted;
    }

    /// <summary>Category levels from stored values; null (key absent) gives the default.</summary>
    public static AudioCategories FromStored(float? master, float? plant, float? alarms, float? voice, bool alarmsMuted)
    {
        return new AudioCategories(
            master ?? DefaultLevel, plant ?? DefaultLevel, alarms ?? DefaultLevel, voice ?? DefaultLevel, alarmsMuted);
    }

    /// <summary>Clamps to 0-1; NaN (a corrupt stored value) falls back to the default.</summary>
    public static float Clamp(float value)
    {
        if (float.IsNaN(value)) return DefaultLevel;
        return Math.Max(0f, Math.Min(1f, value));
    }

    /// <summary>Multiplier for plant machinery, steam and ambience sources.</summary>
    public float PlantGain { get { return Plant; } }

    /// <summary>Multiplier for the horn, stings and other alarm sounds; 0 while the alarm mute is on.</summary>
    public float AlarmGain { get { return AlarmsMuted ? 0f : Alarms; } }

    /// <summary>Multiplier for the voice announcer.</summary>
    public float VoiceGain { get { return Voice; } }
}
