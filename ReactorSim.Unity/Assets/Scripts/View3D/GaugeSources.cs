using System;
using System.Globalization;

// Pure logic (no UnityEngine): resolves BIND_GAUGE_* / BIND_BAR_* pivot names to a value selector and range.
//   BIND_GAUGE_<Name>_<min>_<max>        e.g. BIND_GAUGE_TotalReactivityPcm_-2500_1000, ..._ExcoreFluxCps_0_3.2e8
//   BIND_BAR_<Name>[_<index>]_<min>_<max> e.g. BIND_BAR_BankInsertion_2_0_1
namespace ReactorSim.Instruments
{
    public sealed class GaugeSource
    {
        public string Name = "";
        public int Index = -1;
        public double Min;
        public double Max = 1.0;
        public Func<PlantSnapshot, double> Select;
        public string Units = "";
    }

    // Pure bar/needle geometry, kept out of the binder so it is data, not literals. Travel is per selector
    // (BankInsertion bars are the 0.30 m rod strips; every other K02 edgewise bar is 0.18 m, the kit's
    // travel_m in Blender/scripts/lib_kits.py). Needle sweep mirrors ANIM["gauge_sweep_z"] in lib_common.py.
    public static class GaugeGeometry
    {
        public const float DefaultBarTravelM = 0.18f;
        public const float NeedleSweepMinDeg = -135f;
        public const float NeedleSweepMaxDeg = 135f;

        private static readonly System.Collections.Generic.Dictionary<string, float> TravelByName =
            new System.Collections.Generic.Dictionary<string, float> { { "BankInsertion", 0.30f } };

        public static float TravelM(string selectorName)
        {
            float t;
            return selectorName != null && TravelByName.TryGetValue(selectorName, out t) ? t : DefaultBarTravelM;
        }

        // The pointer rests at mid travel: local Y runs restY - travel/2 (frac 0) to restY + travel/2 (frac 1).
        public static float BarY(float restY, float travelM, float frac)
        {
            return restY - travelM * 0.5f + travelM * frac;
        }

        public static float NeedleDeg(float frac)
        {
            return NeedleSweepMinDeg + (NeedleSweepMaxDeg - NeedleSweepMinDeg) * frac;
        }
    }

    public static class GaugeSources
    {
        // Snapshot Power is normalised (1.0 = rated). Excore/sector flux have no Core field:
        // derived from normalised power so the dial reads sensibly.
        public static bool HighFlux(PlantSnapshot s) { return s.Power > ReactorSim.Physics.HighFluxTripPower; }

        public static bool TryResolve(string pivotName, out GaugeSource source)
        {
            source = null;
            if (string.IsNullOrEmpty(pivotName)) return false;
            bool gauge = pivotName.StartsWith("BIND_GAUGE_", StringComparison.Ordinal);
            bool bar = pivotName.StartsWith("BIND_BAR_", StringComparison.Ordinal);
            if (!gauge && !bar) return false;

            string rest = pivotName.Substring(gauge ? 11 : 9);
            string[] tok = rest.Split('_');
            if (tok.Length < 3) return false;
            double min, max;
            if (!TryNum(tok[tok.Length - 2], out min) || !TryNum(tok[tok.Length - 1], out max) || !(min < max)) return false;

            int n = tok.Length - 2;
            string name = string.Join("_", tok, 0, n);
            int index = -1;
            if (bar && n >= 2)
            {
                int idx;
                if (int.TryParse(tok[n - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out idx))
                {
                    index = idx;
                    name = string.Join("_", tok, 0, n - 1);
                }
            }

            Func<PlantSnapshot, double> sel = Selector(name, index);
            if (sel == null) return false;
            source = new GaugeSource { Name = name, Index = index, Min = min, Max = max, Select = sel, Units = Find(name).Units };
            return true;
        }

        // KTD9: the one table of snapshot selectors. Every value an inventory gauge shows has a row here, with
        // its engineering units. `Indexed` selectors (rod bank) need an index in the pivot name.
        public sealed class SelectorEntry
        {
            public readonly string Name;
            public readonly string Units;
            public readonly bool Indexed;
            public readonly Func<PlantSnapshot, int, double> Select;
            public SelectorEntry(string name, string units, Func<PlantSnapshot, int, double> select, bool indexed = false)
            {
                Name = name; Units = units; Select = select; Indexed = indexed;
            }
        }

        private static SelectorEntry E(string name, string units, Func<PlantSnapshot, double> f)
        {
            return new SelectorEntry(name, units, (s, i) => f(s));
        }

        public static readonly SelectorEntry[] Table =
        {
            // Core and reactivity. Excore/sector flux have no Core field: derived from normalised power.
            E("CorePowerMWt", "MWt", s => s.Power * ReactorSim.Physics.RatedThermalMWt),
            E("QuarterPower_pct", "%", s => s.Power * 100.0),
            E("ExcoreFluxCps", "cps", s => s.Power * 3.2e8),
            E("SectorFluxCps", "cps", s => s.Power * 1.1e8),
            E("DecayHeatPower", "frac", s => s.DecayHeatPower),
            E("TotalHeatPower", "frac", s => s.TotalHeatPower),
            E("PeriodSeconds", "s", s => s.PeriodSeconds),
            E("TotalReactivityPcm", "pcm", s => s.TotalReactivityPcm),
            E("RodReactivityPcm", "pcm", s => s.RodReactivityPcm),
            E("BoronReactivityPcm", "pcm", s => s.BoronReactivityPcm),
            E("DopplerPcm", "pcm", s => s.DopplerPcm),
            E("ModeratorPcm", "pcm", s => s.ModeratorPcm),
            E("XenonPcm", "pcm", s => s.XenonPcm),
            E("IodinePcm", "pcm", s => s.IodinePcm),
            E("Xenon135", "frac", s => s.Xenon135),
            E("Iodine135", "frac", s => s.Iodine135),
            // Rods
            new SelectorEntry("BankInsertion", "frac", (s, i) => SnapshotMath.Elem(s.BankInsertion, i), true),
            E("BankIntegrityMin", "frac", s => SnapshotMath.Min(s.BankIntegrity)),
            E("BankSpeedMean", "frac/s", s => SnapshotMath.Mean(s.BankSpeed)),
            // Chemistry
            E("BoronPpm", "ppm", s => s.BoronPpm),
            E("Ph", "pH", s => s.Ph),
            // Primary
            E("FuelTempC", "degC", s => s.FuelTempC),
            E("PrimaryTempC", "degC", s => s.PrimaryTempC),
            E("PrimaryPressureBar", "bar", s => s.PrimaryPressureBar),
            E("SubCoolingC", "degC", s => s.PrimarySatMarginC),
            E("PrimaryLevelFrac", "frac", s => s.PrimaryLevelFrac),
            E("PrimaryFlowFrac", "frac", s => s.PrimaryFlowFrac),
            E("HeaterPowerFrac", "frac", s => s.HeaterPowerFrac),
            E("SprayFrac", "frac", s => s.SprayFrac),
            // Secondary, feed and steam
            E("SecondaryPressureBar", "bar", s => s.SecondaryPressureBar),
            E("SecondaryTempC", "degC", s => s.SecondaryTempC),
            E("SecondaryLevelFrac", "frac", s => s.SecondaryLevelFrac),
            E("SteamLevelFrac", "frac", s => s.SteamLevelFrac),
            E("SteamDemandFrac", "frac", s => s.SteamDemandFrac),
            E("SteamDumpFrac", "frac", s => s.SteamDumpFrac),
            E("FeedwaterFlowFrac", "frac", s => s.FeedwaterFlowFrac),
            E("FeedwaterAvailabilityFrac", "frac", s => s.FeedwaterAvailabilityFrac),
            E("TurbineSteamKW", "kW", s => s.TurbineSteamKW),
            // Turbine, generator, grid, condenser
            E("TurbineRpm", "rpm", s => s.TurbineRpm),
            E("TurbineMechanicalKw", "kW", s => s.TurbineMechanicalKw),
            E("GeneratorKw", "kW", s => s.GeneratorKw),
            E("GridFrequencyHz", "Hz", s => s.GridFrequencyHz),
            E("GridOutputFrac", "frac", s => s.GridOutputFrac),
            E("GridDemandFrac", "frac", s => s.GridDemandFrac),
            E("CondenserPressureBar", "bar", s => s.CondenserPressureBar),
            E("HotwellInventoryFrac", "frac", s => s.HotwellInventoryFrac),
            E("VacuumPumpFrac", "frac", s => s.VacuumPumpFrac),
            E("CoolingWaterFrac", "frac", s => s.CoolingWaterFrac),
            E("BopAutoLoad", "0/1", s => s.BopAutoLoad ? 1.0 : 0.0),
            E("BopTripMask", "mask", s => s.BopTripMask),
            E("BopOutOfSyncSeconds", "s", s => s.BopOutOfSyncSeconds),
            // Clock
            E("GameMinutes", "min", s => s.GameMinutes),
        };

        public static SelectorEntry Find(string name)
        {
            for (int i = 0; i < Table.Length; i++)
                if (Table[i].Name == name) return Table[i];
            return null;
        }

        private static Func<PlantSnapshot, double> Selector(string name, int index)
        {
            var e = Find(name);
            if (e == null) return null;
            if (e.Indexed && index < 0) return null;
            int idx = index;
            return s => e.Select(s, idx);
        }

        private static bool TryNum(string s, out double v)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                && !double.IsNaN(v) && !double.IsInfinity(v);
        }
    }
}
