using System;
using ReactorSim.Instruments;

// Pure logic (no UnityEngine): drag-to-value mapping, control spec -> PlantIntent, and which snapshot
// field a control reads back. Linked into ReactorSim.Core.Tests.
namespace ReactorSim.Controls
{
    public static class DragMapper
    {
        // Relative drag: moving the cursor up by pixelsPerFullRange raises the control through its whole range.
        // Screen Y grows upward in Unity, so dyPixels > 0 means "dragged up".
        public static double FractionAfterDrag(double startFrac, double dyPixels, double pixelsPerFullRange)
        {
            if (!(pixelsPerFullRange > 0.0)) pixelsPerFullRange = 300.0;
            if (double.IsNaN(startFrac)) startFrac = 0.5;
            double f = startFrac + dyPixels / pixelsPerFullRange;
            if (double.IsNaN(f)) return startFrac;
            return f < 0.0 ? 0.0 : (f > 1.0 ? 1.0 : f);
        }

        public static double ValueFromFraction(ControlSpec spec, double frac)
        {
            if (frac < 0.0) frac = 0.0;
            if (frac > 1.0) frac = 1.0;
            double v = spec.Min + frac * (spec.Max - spec.Min);
            if (spec.Discrete) v = Math.Round(v);
            return v;
        }

        public static double FractionFromValue(ControlSpec spec, double value)
        {
            if (double.IsNaN(value) || !(spec.Max > spec.Min)) return 0.5;
            double f = (value - spec.Min) / (spec.Max - spec.Min);
            return f < 0.0 ? 0.0 : (f > 1.0 ? 1.0 : f);
        }
    }

    public static class ControlIntentFactory
    {
        // Intent for a dial set to `value` (already inside the spec's range). Null when the kind has no dial intent.
        public static PlantIntent BuildDial(ControlSpec spec, double value, string issuer)
        {
            switch (spec.Kind)
            {
                case IntentKind.RodBankTarget:
                    return PlantIntent.SetRodBank(spec.RodBank >= 0 ? spec.RodBank : 0, value, issuer);
                case IntentKind.AllRodsTarget: return PlantIntent.SetAllRods(value, issuer);
                case IntentKind.BorateRate: return PlantIntent.SetBorate(value, issuer);
                case IntentKind.DiluteRate: return PlantIntent.SetDilute(value, issuer);
                case IntentKind.NaohRate: return PlantIntent.SetNaoh(value, issuer);
                case IntentKind.IonExchangeRate: return PlantIntent.SetIonExchange(value, issuer);
                case IntentKind.SteamDemand: return PlantIntent.SetSteamDemand(value, issuer);
                case IntentKind.PrimaryFlow: return PlantIntent.SetPrimaryFlow(value, issuer);
                case IntentKind.PrimaryLevel: return new PlantIntent(IntentKind.PrimaryLevel, "PZR", value, issuer);
                case IntentKind.FeedwaterFlow: return PlantIntent.SetFeedwater(value, issuer);
                case IntentKind.HeaterPower: return PlantIntent.SetHeaters(value, issuer);
                case IntentKind.Spray: return PlantIntent.SetSpray(value, issuer);
                case IntentKind.SteamDump: return PlantIntent.SetSteamDump(value, issuer);
                case IntentKind.GridDemand: return PlantIntent.SetGridDemand(value, issuer);
                case IntentKind.VacuumPump: return PlantIntent.SetVacuumPump(value, issuer);
                case IntentKind.CoolingWater: return PlantIntent.SetCoolingWater(value, issuer);
                case IntentKind.AssistTier: return PlantIntent.SetAssist((AssistTier)(int)Math.Round(value), issuer);
                case IntentKind.OperatingMode: return new PlantIntent(IntentKind.OperatingMode, "Core", value, issuer);
                case IntentKind.SelectMission: return PlantIntent.SetMission((int)Math.Round(value), issuer);
                default: return null;
            }
        }

        // Intent for a button press. `currentOn` is the control's present state for toggles (relief open, clock
        // paused); `clockScale` is the requested sim scale, which pace buttons multiply by ButtonValue.
        public static PlantIntent BuildButton(ControlSpec spec, bool currentOn, double clockScale, string issuer)
        {
            switch (spec.Kind)
            {
                case IntentKind.Scram: return PlantIntent.TriggerScram(issuer);
                case IntentKind.ResetScram: return PlantIntent.ClearScram(issuer);
                case IntentKind.ReliefValve: return PlantIntent.SetReliefValve(!currentOn, issuer);
                case IntentKind.PauseClock:
                case IntentKind.ResumeClock:
                    return currentOn ? PlantIntent.ResumeClock(issuer) : PlantIntent.PauseClock(issuer);
                case IntentKind.SetTimeScale: return PlantIntent.SetTimeScale(clockScale * spec.ButtonValue, issuer);
                case IntentKind.SkipAhead: return PlantIntent.SkipAhead(spec.ButtonValue, issuer);
                case IntentKind.ResetBopTrips: return PlantIntent.ResetBopTrips(issuer);
                case IntentKind.AcknowledgeAlarms: return PlantIntent.AcknowledgeAlarms(issuer);
                case IntentKind.TurbineAutoLoad: return PlantIntent.SetTurbineAutoLoad(!currentOn, issuer);
                case IntentKind.GeneratorBreaker: return PlantIntent.SetGeneratorBreaker(!currentOn, issuer);
                default: return null;
            }
        }

        // True when ControlMapCheck can wire this spec to an intent (dial or button).
        public static bool IsSupported(ControlSpec spec)
        {
            if (spec == null) return false;
            return spec.Style == ControlStyle.Dial
                ? BuildDial(spec, spec.Min, "check") != null
                : BuildButton(spec, false, 1.0, "check") != null;
        }
    }

    public enum ControlSound { Tick, Clunk, Breaker, Reject }

    // Control feel (U17): which procedural sound a control makes. Pure so the mapping is tested.
    public static class ControlSounds
    {
        /// <summary>Detents a continuous dial ticks through across its range.</summary>
        public const int ContinuousDetents = 20;

        /// <summary>Dials tick, the generator breaker thunks, every other button clunks.</summary>
        public static ControlSound For(ControlSpec spec)
        {
            if (spec == null) return ControlSound.Clunk;
            if (spec.Style == ControlStyle.Dial) return ControlSound.Tick;
            return spec.Kind == IntentKind.GeneratorBreaker ? ControlSound.Breaker : ControlSound.Clunk;
        }

        /// <summary>Index of the detent a dial value sits in: every integer step for a stepped knob, 5 percent steps otherwise.</summary>
        public static int DetentIndex(ControlSpec spec, double value)
        {
            if (spec == null || !(spec.Max > spec.Min) || double.IsNaN(value)) return 0;
            double span = spec.Max - spec.Min;
            double steps = spec.Discrete ? Math.Max(1.0, Math.Round(span)) : ContinuousDetents;
            double f = (value - spec.Min) / span;
            f = f < 0.0 ? 0.0 : (f > 1.0 ? 1.0 : f);
            return (int)Math.Floor(f * steps + 0.5);
        }
    }

    public static class ControlValues
    {
        // What the plant is doing now (NaN when the control has no readable value).
        public static double Current(ControlSpec spec, PlantSnapshot s)
        {
            if (s == null) return double.NaN;
            switch (spec.Kind)
            {
                case IntentKind.RodBankTarget: return SnapshotMath.Elem(s.BankInsertion, spec.RodBank);
                case IntentKind.AllRodsTarget: return SnapshotMath.Mean(s.BankInsertion);
                case IntentKind.BorateRate: return s.BorateRate;
                case IntentKind.DiluteRate: return s.DiluteRate;
                case IntentKind.NaohRate: return s.NaohRate;
                case IntentKind.IonExchangeRate: return s.IonExchangeRate;
                case IntentKind.SteamDemand: return s.SteamDemandFrac;
                case IntentKind.PrimaryFlow: return s.PrimaryFlowFrac;
                case IntentKind.PrimaryLevel: return s.PrimaryLevelFrac;
                case IntentKind.FeedwaterFlow: return s.FeedwaterFlowFrac;
                case IntentKind.HeaterPower: return s.HeaterPowerFrac;
                case IntentKind.Spray: return s.SprayFrac;
                case IntentKind.SteamDump: return s.SteamDumpFrac;
                case IntentKind.GridDemand: return s.GridDemandFrac;
                case IntentKind.VacuumPump: return s.VacuumPumpFrac;
                case IntentKind.CoolingWater: return s.CoolingWaterFrac;
                case IntentKind.TurbineAutoLoad: return s.BopAutoLoad ? 1.0 : 0.0;
                case IntentKind.GeneratorBreaker: return s.GeneratorSynced ? 1.0 : 0.0;
                case IntentKind.AssistTier: return s.AssistTier;
                case IntentKind.SelectMission: return s.ActiveMissionIndex;
                case IntentKind.OperatingMode: return ModeIndex(s.Mode);
                default: return double.NaN;
            }
        }

        // What the control is set to (differs from Current for rods, which move at the drive speed).
        public static double Commanded(ControlSpec spec, PlantSnapshot s)
        {
            if (s != null)
            {
                if (spec.Kind == IntentKind.RodBankTarget) return SnapshotMath.Elem(s.BankTarget, spec.RodBank);
                if (spec.Kind == IntentKind.AllRodsTarget) return SnapshotMath.Mean(s.BankTarget);
            }
            return Current(spec, s);
        }

        // The snapshot carries the plant's OperatingMode by name (Shutdown, Nominal, Maximum = knob positions 0, 1, 2).
        private static double ModeIndex(string mode)
        {
            OperatingMode m;
            return Enum.TryParse(mode, out m) ? (double)(int)m : double.NaN;
        }

        // State of a toggle button (relief open, scrammed, paused handled by the caller for the clock).
        public static bool ButtonOn(ControlSpec spec, PlantSnapshot s)
        {
            if (s == null) return false;
            switch (spec.Kind)
            {
                case IntentKind.Scram: return s.Scrammed;
                case IntentKind.ReliefValve: return s.ReliefOpen;
                case IntentKind.TurbineAutoLoad: return s.BopAutoLoad;
                case IntentKind.GeneratorBreaker: return s.GeneratorSynced;
                default: return false;
            }
        }
    }
}
