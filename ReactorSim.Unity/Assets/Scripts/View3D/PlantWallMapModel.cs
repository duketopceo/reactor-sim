using System;
using System.Globalization;
using ReactorSim;
using ReactorSim.Instruments;

// Read-only state for the planned whole-plant wall. Missing data is never shown as healthy.
public static class PlantWallMapModel
{
    public enum Status { Unavailable, Offline, Normal, Warning, Alarm }

    public static string Reading(double value, string unit, int decimals = 1)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "N/M";
        return value.ToString("F" + decimals, CultureInfo.InvariantCulture) + " " + unit;
    }

    public static Status Primary(PlantSnapshot s)
    {
        if (s == null || !Finite(s.PrimaryPressureBar) || !Finite(s.PrimaryFlowFrac) || !Finite(s.PrimarySatMarginC)) return Status.Unavailable;
        var context = LampContext.Default;
        if (LampPredicates.Evaluate("PRI_PRESS_HI", s, context)) return Status.Alarm;
        if (LampPredicates.Evaluate("PRI_PRESS_LO", s, context) ||
            LampPredicates.Evaluate("SUBCOOL_LO", s, context)) return Status.Warning;
        return s.PrimaryFlowFrac <= 0 ? Status.Offline : Status.Normal;
    }

    public static Status Turbine(PlantSnapshot s)
    {
        if (s == null || !Finite(s.TurbineRpm)) return Status.Unavailable;
        if (s.TurbineTripped) return Status.Alarm;
        return s.TurbineRpm <= 0 ? Status.Offline : Status.Normal;
    }

    public static Status Component(string name, PlantSnapshot s)
    {
        if (s == null) return Status.Unavailable;
        var c = LampContext.Default;
        switch (name)
        {
            case "Reactor":
                if (!Finite(s.Power) || !Finite(s.FuelTempC)) return Status.Unavailable;
                return s.Scrammed || LampPredicates.Evaluate("FUEL_TEMP_HI", s, c) || LampPredicates.Evaluate("HIGH_FLUX", s, c) ? Status.Alarm : Status.Normal;
            case "Safety": return s.Scrammed ? Status.Alarm : Status.Normal;
            case "PrimaryPump": case "Pressurizer": return Primary(s);
            case "SteamGenerator":
                if (!Finite(s.SecondaryPressureBar) || !Finite(s.SecondaryLevelFrac)) return Status.Unavailable;
                if (LampPredicates.Evaluate("SG_LOW_LEVEL_TRIP", s, c)) return Status.Alarm;
                return LampPredicates.Evaluate("SG_PRESS_HI", s, c) || LampPredicates.Evaluate("SG_LVL_LO", s, c) || s.SecondaryReliefOpen ? Status.Warning : Status.Normal;
            case "Turbine": return Turbine(s);
            case "Generator": return s.TurbineTripped ? Status.Alarm : s.GeneratorSynced ? Status.Normal : Status.Offline;
            case "Grid":
                if (!Finite(s.GridFrequencyHz)) return Status.Unavailable;
                return LampPredicates.Evaluate("GRID_FREQ_OFF", s, c) ? Status.Warning : Status.Normal;
            case "Condenser":
                if (!Finite(s.CondenserPressureBar) || !Finite(s.HotwellInventoryFrac)) return Status.Unavailable;
                if (LampPredicates.Evaluate("COND_LOW_VACUUM", s, c)) return Status.Alarm;
                return LampPredicates.Evaluate("COND_VAC_WARN", s, c) || LampPredicates.Evaluate("HOTWELL_LOW", s, c) ? Status.Warning : Status.Normal;
            case "Cooling":
                if (!Finite(s.CoolingWaterFrac)) return Status.Unavailable;
                return LampPredicates.Evaluate("COOLING_LOSS", s, c) ? Status.Warning : Status.Normal;
            case "FeedPump":
                if (!Finite(s.FeedwaterFlowFrac) || !Finite(s.Power)) return Status.Unavailable;
                return LampPredicates.Evaluate("FEED_FAIL", s, c) ? Status.Warning : s.FeedwaterFlowFrac <= 0 ? Status.Offline : Status.Normal;
            case "Chemistry":
                if (!Finite(s.BoronPpm) || !Finite(s.Ph)) return Status.Unavailable;
                return LampPredicates.Evaluate("BORON_LOW", s, c) || LampPredicates.Evaluate("PH_DRIFT", s, c) ? Status.Warning : Status.Normal;
            default: return Status.Unavailable;
        }
    }

    private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
}
