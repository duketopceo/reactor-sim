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

    // Returns NaN for routes without a directly modeled flow/output reading.
    public static double RouteActivity(string name, PlantSnapshot s)
    {
        if (s == null) return double.NaN;
        double value;
        switch (name)
        {
            case "PrimaryHot": case "PrimaryCold": value = s.PrimaryFlowFrac; break;
            case "Feedwater": value = s.FeedwaterDeliveredFrac; break;
            case "CoolingSupply": case "CoolingReturn": value = s.CoolingWaterFrac; break;
            case "Grid": value = s.GridOutputFrac; break;
            case "Steam": case "TurbineExhaust": value = s.TurbineSteamKW / Physics.RatedThermalKW; break;
            case "SteamDump": value = s.DumpSteamKW / Physics.RatedThermalKW; break;
            case "SteamHeader": value = (s.TurbineSteamKW + s.DumpSteamKW) / Physics.RatedThermalKW; break;
            case "Shaft": value = s.TurbineRpm / Physics.TurbineRatedRpm; break;
            default: return double.NaN;
        }
        return Finite(value) ? Math.Max(0, Math.Min(1, value)) : double.NaN;
    }

    // The snapshot orders banks D, C, B, A; the asset labels them A, B, C, D.
    public static double BankInsertion(string bank, PlantSnapshot s)
    {
        int index;
        switch (bank) { case "A": index = 3; break; case "B": index = 2; break;
            case "C": index = 1; break; case "D": index = 0; break; default: return double.NaN; }
        if (s == null || s.BankInsertion == null || index >= s.BankInsertion.Length || !Finite(s.BankInsertion[index])) return double.NaN;
        return Math.Max(0, Math.Min(1, s.BankInsertion[index]));
    }

    // Compact component readouts use actual snapshot values; command values are labeled explicitly.
    // Returns N/M for missing sensors rather than filling in plausible-looking numbers.
    public static string ComponentReadout(string name, PlantSnapshot s)
    {
        if (s == null) return "N/M";
        switch (name)
        {
            case "Reactor":
                return "PWR " + Reading(s.Power * 100, "%") + "  FUEL " + Reading(s.FuelTempC, "C") +
                    "\nRHO " + Reading(s.TotalReactivityPcm, "pcm", 0) + "  PERIOD " + Reading(s.PeriodSeconds, "s") +
                    "\nRODS A " + Reading(BankInsertion("A", s) * 100, "%", 0) + " B " + Reading(BankInsertion("B", s) * 100, "%", 0) +
                    " C " + Reading(BankInsertion("C", s) * 100, "%", 0) + " D " + Reading(BankInsertion("D", s) * 100, "%", 0);
            case "Safety": return s.Scrammed ? "SCRAM ACTIVE" : "SCRAM CLEAR  |  SAFETY FLOW N/M";
            case "PrimaryPump":
                return "FLOW " + Reading(s.PrimaryFlowFrac * 100, "%") + "  TEMP " + Reading(s.PrimaryTempC, "C") +
                    "\nLEVEL " + Reading(s.PrimaryLevelFrac * 100, "%");
            case "Pressurizer":
                return "PRESS " + Reading(s.PrimaryPressureBar, "bar") + "  MARGIN " + Reading(s.PrimarySatMarginC, "C") +
                    "\nHEAT " + Reading(s.HeaterPowerFrac * 100, "%") + "  SPRAY " + Reading(s.SprayFrac * 100, "%") +
                    "  RELIEF " + (s.ReliefOpen ? "OPEN" : "CLOSED");
            case "SteamGenerator":
                return "PRESS " + Reading(s.SecondaryPressureBar, "bar") + "  TEMP " + Reading(s.SecondaryTempC, "C") +
                    "\nLEVEL " + Reading(s.SecondaryLevelFrac * 100, "%") + "  RELIEF " + (s.SecondaryReliefOpen ? "OPEN" : "CLOSED") +
                    "\nTHROTTLE " + Reading(s.SteamDemandFrac * 100, "% command") + "  DUMP " + Reading(s.DumpSteamKW, "kW heat", 0);
            case "Turbine":
                return "SPEED " + Reading(s.TurbineRpm, "rpm", 0) + "  STEAM " + Reading(s.TurbineSteamKW, "kW heat", 0) +
                    "\n" + (s.TurbineTripped ? "TRIPPED" : "NOT TRIPPED");
            case "Generator":
                return "OUTPUT " + Reading(s.GeneratorKw, "kW", 0) + "  " + (s.GeneratorSynced ? "SYNCED" : "BREAKER OPEN");
            case "Grid":
                return "FREQ " + Reading(s.GridFrequencyHz, "Hz", 2) + "  OUTPUT " + Reading(s.GridOutputFrac * 100, "% rated");
            case "Condenser":
                return "PRESS " + Reading(s.CondenserPressureBar, "bar", 2) + "  HOTWELL " + Reading(s.HotwellInventoryFrac * 100, "%") +
                    "\nVACUUM PUMP " + Reading(s.VacuumPumpFrac * 100, "% command");
            case "Cooling": return "COOLING " + Reading(s.CoolingWaterFrac * 100, "%");
            case "FeedPump":
                return "DELIVERED " + Reading(s.FeedwaterDeliveredFrac * 100, "% rated") +
                    "\nPUMP " + Reading(s.FeedwaterFlowFrac * 100, "% command") +
                    "  SUPPLY " + Reading(s.FeedwaterAvailabilityFrac * 100, "%");
            case "Chemistry":
                return "BORON " + Reading(s.BoronPpm, "ppm", 0) + "  PH " + Reading(s.Ph, "", 2) +
                    "\nCHEMISTRY FLOW N/M";
            default: return "N/M";
        }
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
            case "Safety": return s.Scrammed ? Status.Alarm : Status.Unavailable;
            case "PrimaryPump":
                if (!Finite(s.PrimaryFlowFrac)) return Status.Unavailable;
                if (s.PrimaryFlowFrac <= 0) return Status.Offline;
                return LampPredicates.Evaluate("RCP_LOW_FLOW", s, c) || LampPredicates.Evaluate("FLOW_LO", s, c) ? Status.Warning : Status.Normal;
            case "Pressurizer":
                if (!Finite(s.PrimaryPressureBar) || !Finite(s.PrimarySatMarginC)) return Status.Unavailable;
                if (LampPredicates.Evaluate("PRI_PRESS_HI", s, c)) return Status.Alarm;
                return LampPredicates.Evaluate("PRI_PRESS_LO", s, c) || LampPredicates.Evaluate("SUBCOOL_LO", s, c) ||
                    s.ReliefOpen ? Status.Warning : Status.Normal;
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
