using System;
using System.Collections.Generic;
using ReactorSim;
using ReactorSim.Instruments;

// Pure alarm and voice priority (no UnityEngine; linked into ReactorSim.Core.Tests). One rank table covers the horn
// alarm names (AudioPriority.HornAlarms) and the voice line keys (VoiceAnnouncer), so simultaneous cues stay
// intelligible: a SCRAM outranks everything, then trips, then limit alarms, then state changes, then advisories and
// procedure steps. Pick chooses the one voice line to play when several fire in the same tick.
public static class AudioPriority
{
    private static readonly Dictionary<string, int> Ranks = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        { "scram", 100 },
        { "turbine_trip", 90 }, { "bop-trip", 90 },
        { "alarm_pressure_high", 80 }, { "high-pressure", 80 },
        { "alarm_temp_high", 80 }, { "high-fuel-temp", 80 },
        { "alarm_power_high", 70 }, { "alarm_period", 70 },
        { "scram_reset", 50 }, { "critical", 45 }, { "sync", 45 },
        { "autopilot_on", 40 }, { "autopilot_off", 40 },
        { "all_normal", 30 },
        { "advise_xenon", 20 }, { "advise_borate", 20 }, { "advise_slow", 20 },
        { "startup_begin", 10 }, { "step_vacuum", 10 }, { "step_pumps", 10 }, { "step_pressurize", 10 },
        { "step_shutdown_banks", 10 }, { "step_bank_a", 10 }, { "step_dilute", 10 }, { "heatup", 10 },
        { "turbine_roll", 10 }, { "power_ascension", 10 },
    };

    /// <summary>
    /// The conditions that sound the horn until acknowledged. Tracked one by one (AlarmTracker), so after an acknowledge a NEW
    /// condition (a turbine trip during a scram) sounds the horn again.
    /// </summary>
    public static readonly string[] HornAlarms = { "scram", "bop-trip", "high-pressure", "high-fuel-temp" };

    /// <summary>
    /// True while a horn condition is raised in the snapshot. The limits come from LampPredicates so the horn and the annunciator
    /// lamps cannot disagree; any balance-of-plant trip bit counts as bop-trip.
    /// </summary>
    public static bool HornConditionRaised(string name, PlantSnapshot s)
    {
        if (s == null || name == null) return false;
        switch (name)
        {
            case "scram": return s.Scrammed;
            case "bop-trip": return s.BopTripMask != 0;
            case "high-pressure": return s.PrimaryPressureBar > LampPredicates.PriPressHiBar;
            case "high-fuel-temp": return s.FuelTempC > LampPredicates.FuelTempHiC;
            default: return false;
        }
    }

    /// <summary>Priority of a horn alarm name or voice key; unknown or null is 0.</summary>
    public static int Rank(string key)
    {
        int rank;
        return key != null && Ranks.TryGetValue(key, out rank) ? rank : 0;
    }

    /// <summary>The highest-ranked key (the first one on a tie); null for a null or empty list. Null entries are skipped.</summary>
    public static string Pick(IList<string> keys)
    {
        string best = null;
        int bestRank = -1;
        if (keys == null) return null;
        for (int i = 0; i < keys.Count; i++)
        {
            string k = keys[i];
            if (k == null) continue;
            int r = Rank(k);
            if (r > bestRank) { best = k; bestRank = r; }
        }
        return best;
    }

    /// <summary>
    /// True when a new line should cut off the one being spoken: strictly higher rank, and it is an alarm-class line
    /// (rank 70 or above). Advisories and procedure steps wait their turn.
    /// </summary>
    public static bool Interrupts(string incoming, string playing)
    {
        int r = Rank(incoming);
        return r >= 70 && r > Rank(playing);
    }
}
