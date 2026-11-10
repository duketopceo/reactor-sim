using System;
using System.Collections.Generic;

// Pure logic (no UnityEngine): the lamp predicates behind every BIND_LAMP_<NAME> tile and status lamp. KTD9: the
// predicates stay C# comparisons keyed by lamp name (no expression evaluator); inventory.csv records each threshold
// as documentation and InventoryTests ties the `lamp:` rows to this table both ways. Linked into ReactorSim.Core.Tests.
namespace ReactorSim.Instruments
{
    public enum LampStyle
    {
        AmberSteady,
        RedFlash,    // critical alarm: pulses
        RedSteady,   // energized / closed status (US convention)
        GreenSteady  // open / stopped status
    }

    // Inputs a lamp needs besides the current snapshot.
    public struct LampContext
    {
        public double ShortPeriodSeconds;   // SHORT_PERIOD threshold (the clock's auto-slow period)
        public bool ClockPausedOrFrozen;    // desk Pause lamp
        public PlantSnapshot Previous;      // last distinct snapshot; MODE lights when Mode differs from it

        public static LampContext Default
        {
            get { return new LampContext { ShortPeriodSeconds = 60.0 }; }
        }
    }

    public static class LampPredicates
    {
        public const double FuelTempHiC = 1200.0;
        public const double PriPressHiBar = 165.0;
        public const double PriPressLoBar = 100.0;
        public const double SubcoolLoC = 15.0;
        public const double SgPressHiBar = 80.0;
        public const double SgLevelLoFrac = 0.25;
        public const double RcpLowFlowFrac = 0.25;
        public const double XenonPitPcm = -800.0;
        public const double BoronLowPpm = 400.0;
        public const double FlowLoFrac = 0.4;
        public const double TempHiC = 330.0;
        public const double LevelLoFrac = 0.4;
        public const double HeaterOffFrac = 0.001;
        public const double SprayFaultFrac = 0.99;
        public const double FeedFailFlowFrac = 0.05;
        public const double FeedFailPower = 0.10;            // 10 % of rated (Power is normalised, 1.0 = rated thermal power)
        public const double CoolingLossFrac = 0.3;
        public const double RodWearIntegrity = 0.8;          // integrity bottoms at 0.6 at full burnup
        public const double PhLow = 6.0;
        public const double PhHigh = 8.2;
        public const double HotwellLowFrac = 0.3;            // nominal 0.7; feed starves at 0.15
        public const double GridFreqBandHz = 0.5;

        // BopTripKind order in BalanceOfPlant (bit i of BopTripMask).
        private const int BitOverspeed = 1 << 0, BitUnderspeed = 1 << 1, BitLowVacuum = 1 << 2,
            BitSgLowLevel = 1 << 3, BitOutOfSync = 1 << 4;

        private sealed class Lamp
        {
            public readonly string Name;
            public readonly LampStyle Style;
            public readonly Func<PlantSnapshot, LampContext, bool> On;
            public Lamp(string name, LampStyle style, Func<PlantSnapshot, LampContext, bool> on)
            {
                Name = name; Style = style; On = on;
            }
        }

        private static Lamp L(string name, LampStyle style, Func<PlantSnapshot, bool> on)
        {
            return new Lamp(name, style, (s, c) => on(s));
        }

        private static readonly Lamp[] Lamps =
        {
            L("SCRAM", LampStyle.RedFlash, s => s.Scrammed),
            L("HIGH_FLUX", LampStyle.RedFlash, s => GaugeSources.HighFlux(s)),
            new Lamp("SHORT_PERIOD", LampStyle.RedFlash, (s, c) => s.PeriodSeconds > 0.0 && s.PeriodSeconds < c.ShortPeriodSeconds),
            L("FUEL_TEMP_HI", LampStyle.RedFlash, s => s.FuelTempC > FuelTempHiC),
            L("PRI_PRESS_HI", LampStyle.RedFlash, s => s.PrimaryPressureBar > PriPressHiBar),
            L("PRI_PRESS_LO", LampStyle.AmberSteady, s => s.PrimaryPressureBar < PriPressLoBar),
            L("SUBCOOL_LO", LampStyle.AmberSteady, s => s.PrimarySatMarginC < SubcoolLoC),
            L("PORV_OPEN", LampStyle.AmberSteady, s => s.ReliefOpen),
            L("SG_PRESS_HI", LampStyle.AmberSteady, s => s.SecondaryPressureBar > SgPressHiBar),
            L("SG_LVL_LO", LampStyle.AmberSteady, s => s.SecondaryLevelFrac < SgLevelLoFrac),
            L("SG_RELIEF", LampStyle.AmberSteady, s => s.SecondaryReliefOpen),
            L("RCP_LOW_FLOW", LampStyle.AmberSteady, s => s.PrimaryFlowFrac < RcpLowFlowFrac),
            L("XENON_PIT", LampStyle.AmberSteady, s => s.XenonPcm < XenonPitPcm),
            L("BORON_LOW", LampStyle.AmberSteady, s => s.BoronPpm < BoronLowPpm),
            L("FLOW_LO", LampStyle.AmberSteady, s => s.PrimaryFlowFrac < FlowLoFrac),
            L("TEMP_HI", LampStyle.AmberSteady, s => s.PrimaryTempC > TempHiC),
            L("LEVEL_LO", LampStyle.AmberSteady, s => s.PrimaryLevelFrac < LevelLoFrac),
            L("HEATER_OFF", LampStyle.AmberSteady, s => s.HeaterPowerFrac <= HeaterOffFrac),
            L("SPRAY_FAULT", LampStyle.AmberSteady, s => s.SprayFrac > SprayFaultFrac),
            L("FEED_FAIL", LampStyle.AmberSteady, s => s.FeedwaterFlowFrac < FeedFailFlowFrac && s.Power > FeedFailPower),
            L("COOLING_LOSS", LampStyle.AmberSteady, s => s.CoolingWaterFrac < CoolingLossFrac),
            new Lamp("Pause", LampStyle.AmberSteady, (s, c) => c.ClockPausedOrFrozen),
            // U8: previously dead tiles and the new balance-of-plant, grid and status lamps.
            new Lamp("MODE", LampStyle.AmberSteady, (s, c) => c.Previous != null && !string.Equals(c.Previous.Mode, s.Mode, StringComparison.Ordinal)),
            L("ROD_WEAR", LampStyle.AmberSteady, s => SnapshotMath.Min(s.BankIntegrity) < RodWearIntegrity),
            L("PH_DRIFT", LampStyle.AmberSteady, s => s.Ph < PhLow || s.Ph > PhHigh),
            L("TURB_OVERSPEED", LampStyle.RedFlash, s => (s.BopTripMask & BitOverspeed) != 0),
            L("TURB_UNDERSPEED", LampStyle.RedFlash, s => (s.BopTripMask & BitUnderspeed) != 0),
            L("COND_LOW_VACUUM", LampStyle.RedFlash, s => (s.BopTripMask & BitLowVacuum) != 0),
            L("SG_LOW_LEVEL_TRIP", LampStyle.RedFlash, s => (s.BopTripMask & BitSgLowLevel) != 0),
            L("GEN_OUT_OF_SYNC", LampStyle.RedFlash, s => (s.BopTripMask & BitOutOfSync) != 0),
            L("TURBINE_TRIPPED", LampStyle.RedFlash, s => s.TurbineTripped),
            L("COND_VAC_WARN", LampStyle.AmberSteady, s => s.CondenserPressureBar > ReactorSim.Physics.CondenserLowVacuumWarnBar),
            L("HOTWELL_LOW", LampStyle.AmberSteady, s => s.HotwellInventoryFrac < HotwellLowFrac),
            L("GRID_FREQ_OFF", LampStyle.AmberSteady, s => Math.Abs(s.GridFrequencyHz - ReactorSim.Physics.GridNominalFrequencyHz) > GridFreqBandHz),
            L("CRITICAL", LampStyle.AmberSteady, s => s.IsCritical),
            L("GEN_BREAKER_CLOSED", LampStyle.RedSteady, s => s.GeneratorSynced),
            L("GEN_BREAKER_OPEN", LampStyle.GreenSteady, s => !s.GeneratorSynced),
        };

        private static readonly Dictionary<string, Lamp> ByName = BuildIndex();

        private static Dictionary<string, Lamp> BuildIndex()
        {
            var d = new Dictionary<string, Lamp>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Lamps.Length; i++) d[Lamps[i].Name] = Lamps[i];
            return d;
        }

        // Lamp names without the BIND_LAMP_ prefix.
        public static IEnumerable<string> Names
        {
            get { for (int i = 0; i < Lamps.Length; i++) yield return Lamps[i].Name; }
        }

        public static bool Has(string name)
        {
            return name != null && ByName.ContainsKey(name);
        }

        public static bool Evaluate(string name, PlantSnapshot s, LampContext ctx)
        {
            Lamp l;
            return s != null && name != null && ByName.TryGetValue(name, out l) && l.On(s, ctx);
        }

        // Status and notice lamps are not alarms: they never flash or need an acknowledge.
        private static readonly HashSet<string> NotAlarms =
            new HashSet<string>(new[] { "Pause", "MODE", "CRITICAL" }, StringComparer.OrdinalIgnoreCase);

        /// <summary>True for a lamp that is an alarm (flashes until acknowledged): not a status lamp, a clock or mode notice.</summary>
        public static bool IsAlarm(string name)
        {
            Lamp l;
            if (name == null || !ByName.TryGetValue(name, out l) || NotAlarms.Contains(name)) return false;
            return l.Style == LampStyle.AmberSteady || l.Style == LampStyle.RedFlash;
        }

        public static IEnumerable<string> AlarmNames
        {
            get
            {
                for (int i = 0; i < Lamps.Length; i++)
                    if (IsAlarm(Lamps[i].Name)) yield return Lamps[i].Name;
            }
        }

        public static LampStyle Style(string name)
        {
            Lamp l;
            return name != null && ByName.TryGetValue(name, out l) ? l.Style : LampStyle.AmberSteady;
        }
    }
}
