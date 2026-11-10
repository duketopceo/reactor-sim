// ReactorSim.Core — plant layer: control rods, chemistry, pumps, modes, SCRAM,
// unified PlantIntent mutation handler (U2), Snapshot capture/restore (U2),
// Assistant & Alarms (U5/U8), and finite-state assertions (U1).

using System;
using System.Collections.Generic;
using System.Globalization;

namespace ReactorSim
{
    public enum OperatingMode { Shutdown, Nominal, Maximum }

    public class ControlRodBank
    {
        public string Name;
        public int RodCount;
        public double WorthPcm;          // bank total when fully inserted
        public double Insertion;         // 0 = withdrawn, 1 = fully inserted
        public double InsertionTarget;
        public double SpeedPerSec = Physics.RodDriveSpeedPerSec; // see Physics.RodDriveSpeedPerSec for the rationale
        public double Integrity = 1.0;    // depletes with absorbed fluence
        public double Burnup;             // 0..1 absorber depletion

        public ControlRodBank(string name, int rods, double worthPcm)
        {
            Name = name; RodCount = rods; WorthPcm = worthPcm;
            Insertion = 1.0; InsertionTarget = 1.0;
        }

        public void Step(double dt)
        {
            double d = Physics.Clamp(InsertionTarget - Insertion,
                                     -SpeedPerSec * dt, SpeedPerSec * dt);
            Insertion = Physics.Clamp(Insertion + d, 0.0, 1.0);
        }

        // Integral rod worth: S-curve (differential worth peaks mid-travel).
        public double ReactivityPcmAt(double insertion)
        {
            double x = Physics.Clamp(insertion, 0, 1);
            double sCurve = x - Math.Sin(2 * Math.PI * x) / (2 * Math.PI);
            return WorthPcm * sCurve * Integrity;
        }

        public double ReactivityPcm
        {
            get { return ReactivityPcmAt(Insertion); }
        }

        // Absorber depletion proportional to local flux x insertion.
        public void Burn(double dt, double powerFrac)
        {
            Burnup = Physics.Clamp(Burnup + powerFrac * Insertion * dt * 1e-7, 0, 1);
            Integrity = 1.0 - 0.4 * Burnup; // up to 40% worth loss
        }
    }

    public class Chemistry
    {
        public double BoronPpm { get; private set; } = 1500.0; // cold shutdown-ish
        public double Ph { get; private set; } = Physics.CoolantPhOperative;
        public double OperativePh = Physics.CoolantPhOperative;

        // Dosing rates set by Plant intents (pumps).
        public double BorateRate { get; set; }      // +ppm/s
        public double DiluteRate { get; set; }      // -ppm/s
        public double NaohRate { get; set; }        // +pH/s (weak)
        public double IonExchangeRate { get; set; } // 0..1, strips poisons+boron

        public void Step(double dt)
        {
            double ionBoronStrip = Physics.Clamp(IonExchangeRate, 0.0, 1.0) * 0.5;
            BoronPpm += (BorateRate - DiluteRate - ionBoronStrip) * dt;
            BoronPpm = Physics.Clamp(Physics.Sanitize(BoronPpm, 0.0), 0.0, 4000.0);

            // pH: boric acid pushes down, NaOH pushes up, drifts to operative point.
            double acidPull = BoronPpm / 4000.0 * 1.2;
            Ph += (NaohRate * dt * 0.05)
                  + ((OperativePh - 0.3 - acidPull) - Ph) * 0.0005 * dt;
            Ph = Physics.Clamp(Physics.Sanitize(Ph, Physics.CoolantPhOperative), 4.0, 10.0);
        }

        // Boron worth degraded up to 20% when pH strays >1 from operative point.
        public double BoronWorthPcm
        {
            get
            {
                double dev = Math.Abs(OperativePh - Ph);
                double penalty = dev > 1.0
                    ? 1.0 - Physics.Clamp((dev - 1.0) / OperativePh, 0, 0.2)
                    : 1.0;
                return BoronPpm * Physics.BoronWorthPcmPerPpm * penalty;
            }
        }

        public void RestoreState(double boronPpm, double ph, double borateRate, double diluteRate, double naohRate, double ionExchangeRate)
        {
            BoronPpm = Physics.Clamp(Physics.Sanitize(boronPpm, 1500.0), 0.0, 4000.0);
            Ph = Physics.Clamp(Physics.Sanitize(ph, Physics.CoolantPhOperative), 4.0, 10.0);
            BorateRate = Physics.Clamp(Physics.Sanitize(borateRate, 0.0), 0.0, 20.0);
            DiluteRate = Physics.Clamp(Physics.Sanitize(diluteRate, 0.0), 0.0, 20.0);
            NaohRate = Physics.Clamp(Physics.Sanitize(naohRate, 0.0), -5.0, 5.0);
            IonExchangeRate = Physics.Clamp(Physics.Sanitize(ionExchangeRate, 0.0), 0.0, 1.0);
        }
    }

    public class Plant
    {
        public ReactorCore Core = new ReactorCore();
        public ThermalHydraulics Thermal = new ThermalHydraulics();
        public Chemistry Chemistry = new Chemistry();
        public BalanceOfPlant Bop = new BalanceOfPlant();
        public ControlRodBank[] Banks;
        public OperatingMode Mode = OperatingMode.Shutdown;
        public bool Scrammed { get; private set; }
        /// <summary>Counts AcknowledgeAlarms intents. The alarm tracker (view side) acknowledges every alarm raised when it changes.</summary>
        public int AlarmAckSeq { get; private set; }
        public AssistantEngine Assistant = new AssistantEngine();

        // U15: grid dispatcher and plant-manager messages. Stepped from Simulation.Tick; the host loads the
        // message definitions (messages.json) into Messages.
        public Dispatcher Dispatcher = new Dispatcher();
        public MessageEngine Messages = new MessageEngine();

        public void StepDispatch(double dt, double timeScale)
        {
            Dispatcher.Step(dt, Bop.Turbine.GeneratorKw / 1000.0, Bop.Turbine.Synced);
            Messages.Step(this, Dispatcher, dt, timeScale);
        }

        public void ResetDispatch()
        {
            Dispatcher.Reset(Dispatcher.InitialSeed);
            Messages.ResetState();
        }

        // Audit trail of intents applied during the current tick.
        public readonly List<PlantIntent> RecentIntents = new List<PlantIntent>();

        // Rated values for unit conversion.
        public double RatedThermalKW = Physics.RatedThermalKW;

        public Plant()
        {
            // 48 rod clusters in 4 banks of 12 (D/C shutdown, B/A control); see Physics.RodsPerBank.
            Banks = new ControlRodBank[]
            {
                new ControlRodBank("D", Physics.RodsPerBank, -1500),  // shutdown banks
                new ControlRodBank("C", Physics.RodsPerBank, -1500),
                new ControlRodBank("B", Physics.RodsPerBank, -2000),
                new ControlRodBank("A", Physics.RodsPerBank, -3000),  // regulating bank
            };
            UpdateReactivityLedger();
        }

        // Single mutation entry point (KTD4, R3, U2).
        public bool Apply(PlantIntent intent)
        {
            if (intent == null) return false;
            RecentIntents.Add(intent);
            if (RecentIntents.Count > 64)
                RecentIntents.RemoveAt(0);

            switch (intent.Kind)
            {
                case IntentKind.RodBankTarget:
                    int idx = ResolveBankIndex(intent.Target);
                    if (idx >= 0 && idx < Banks.Length)
                    {
                        if (!Scrammed)
                            Banks[idx].InsertionTarget = Physics.Clamp(intent.Value, 0.0, 1.0);
                        return true;
                    }
                    return false;

                case IntentKind.AllRodsTarget:
                    if (!Scrammed)
                        SetAllRods(intent.Value);
                    return true;

                case IntentKind.BorateRate:
                    Chemistry.BorateRate = Physics.Clamp(intent.Value, 0.0, 20.0);
                    return true;

                case IntentKind.DiluteRate:
                    Chemistry.DiluteRate = Physics.Clamp(intent.Value, 0.0, 20.0);
                    return true;

                case IntentKind.NaohRate:
                    Chemistry.NaohRate = Physics.Clamp(intent.Value, -5.0, 5.0);
                    return true;

                case IntentKind.IonExchangeRate:
                    Chemistry.IonExchangeRate = Physics.Clamp(intent.Value, 0.0, 1.0);
                    return true;

                case IntentKind.SteamDemand:
                    Thermal.SteamDemandFrac = Physics.Clamp(intent.Value, 0.0, 1.2);
                    return true;

                case IntentKind.PrimaryFlow:
                    Thermal.PrimaryFlowFrac = Physics.Clamp(intent.Value, 0.0, 1.0);
                    return true;

                case IntentKind.PrimaryLevel:
                    Thermal.PrimaryLevelFrac = Physics.Clamp(intent.Value, 0.0, 1.0);
                    return true;

                case IntentKind.FeedwaterFlow:
                    Thermal.FeedwaterFlowFrac = Physics.Clamp(intent.Value, 0.0, 2.0);
                    return true;

                case IntentKind.HeaterPower:
                    Thermal.HeaterPowerFrac = Physics.Clamp(intent.Value, 0.0, 1.0);
                    return true;

                case IntentKind.Spray:
                    Thermal.SprayFrac = Physics.Clamp(intent.Value, 0.0, 1.0);
                    return true;

                case IntentKind.ReliefValve:
                    Thermal.ReliefOpen = intent.Value >= 0.5;
                    return true;

                case IntentKind.Scram:
                    Scram();
                    return true;

                case IntentKind.ResetScram:
                    ResetScram();
                    return true;

                case IntentKind.OperatingMode:
                    int m = (int)Math.Round(intent.Value);
                    if (m >= 0 && m <= 2) Mode = (OperatingMode)m;
                    return true;

                case IntentKind.AssistTier:
                    int t = (int)Math.Round(intent.Value);
                    if (t >= 0 && t <= 3) Assistant.Tier = (AssistTier)t;
                    return true;

                case IntentKind.SelectMission:
                    Assistant.SelectMission((int)Math.Round(intent.Value), this);
                    return true;

                case IntentKind.LoadPreset:
                    LoadPreset(intent.Target);
                    return true;

                case IntentKind.TurbineAutoLoad:
                    Bop.AutoLoad = intent.Value >= 0.5;
                    return true;

                case IntentKind.SteamDump:
                    Thermal.SteamDumpFrac = Physics.Clamp(intent.Value, 0.0, 1.0);
                    return true;

                case IntentKind.GeneratorBreaker:
                    return Bop.SetBreaker(intent.Value >= 0.5);

                case IntentKind.GridDemand:
                    Bop.GridDemandFrac = Physics.Clamp(intent.Value, 0.0, 1.5);
                    return true;

                case IntentKind.VacuumPump:
                    Bop.VacuumPumpFrac = Physics.Clamp(intent.Value, 0.0, 1.0);
                    return true;

                case IntentKind.CoolingWater:
                    Bop.CoolingWaterFrac = Physics.Clamp(intent.Value, 0.0, 1.0);
                    return true;

                case IntentKind.ResetBopTrips:
                    Bop.ResetTrips();
                    return true;

                case IntentKind.AcknowledgeAlarms:
                    AlarmAckSeq++;
                    return true;

                default:
                    return false;
            }
        }

        private int ResolveBankIndex(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return 3;
            string t = target.Trim().ToUpperInvariant();
            if (t == "D" || t == "0") return 0;
            if (t == "C" || t == "1") return 1;
            if (t == "B" || t == "2") return 2;
            if (t == "A" || t == "3") return 3;
            int parsed;
            if (int.TryParse(t, out parsed) && parsed >= 0 && parsed < Banks.Length)
                return parsed;
            return -1;
        }

        public void SetAllRods(double target)
        {
            if (double.IsNaN(target) || double.IsInfinity(target))
                throw new ArgumentOutOfRangeException(nameof(target), target, "rod target must be finite");
            foreach (var b in Banks) b.InsertionTarget = Physics.Clamp(target, 0, 1);
        }

        public void Scram()
        {
            Scrammed = true;
            SetAllRods(1.0);
            foreach (var b in Banks) b.SpeedPerSec = Physics.RodScramSpeedPerSec; // fast insert
            Core.Scram();
            Mode = OperatingMode.Shutdown;
        }

        public void ResetScram()
        {
            Scrammed = false;
            foreach (var b in Banks) b.SpeedPerSec = Physics.RodDriveSpeedPerSec;
        }

        public void UpdateReactivityLedger()
        {
            Core.RodReactivityPcm = SumRodWorth();
            Core.BoronReactivityPcm = Chemistry.BoronWorthPcm;

            // Fuel Doppler power defect: proportional to fuel overheat above primary coolant.
            // At 100% power, FuelTempC - PrimaryTempC = 300C => -1800 pcm stabilizing feedback.
            double fuelOverheatC = Math.Max(0.0, Thermal.FuelTempC - Thermal.PrimaryTempC);
            Core.DopplerPcm = Physics.DopplerCoeffPcmPerC * fuelOverheatC;
            Core.ModeratorPcm = Physics.ModeratorCoeffPcmPerC
                                * Math.Max(0.0, Thermal.PrimaryTempC - 291.0) * 0.01
                                * Thermal.PrimaryLevelFrac;
        }

        // One sim tick: tightly substeps coupled rods -> chemistry -> feedback -> kinetics -> poisons -> TH at 0.2s.
        public const double MaxStepSeconds = 3600.0;

        public void Step(double dt)
        {
            // Reject before touching state. NaN/Inf/huge dt also overflow the substep count
            // ((int)Ceiling(+Inf) saturates to int.MaxValue on .NET 9+/ARM64 = effective hang).
            // dt == 0 is a documented no-op (verification EDGE-01).
            if (double.IsNaN(dt) || dt < 0.0 || dt > MaxStepSeconds)
                throw new ArgumentOutOfRangeException(nameof(dt), dt, "Plant.Step dt must be finite, >= 0 and <= " + MaxStepSeconds + " s");
            int sub = Math.Max(1, (int)Math.Ceiling(dt / 0.2));
            double h = dt / sub;
            for (int i = 0; i < sub; i++)
            {
                foreach (var b in Banks) { b.Step(h); b.Burn(h, Core.Power); }
                Chemistry.Step(h);

                UpdateReactivityLedger();

                Core.StepKinetics(h);
                double modDensity = Physics.Clamp(Thermal.PrimaryLevelFrac * (1.0 - 0.5 * Thermal.SteamLevelFrac), 0.0, 1.0);
                Core.StepPoisons(h, modDensity, Chemistry.IonExchangeRate);
                Core.StepDecayHeat(h);

                Thermal.Step(h, Core.TotalHeatPower * RatedThermalKW);
                Bop.Step(h, Thermal);
            }

            UpdateReactivityLedger();
            if (Core.Power > 0.85) Mode = OperatingMode.Maximum;
            else if (Core.Power > 0.02) Mode = OperatingMode.Nominal;
            else if (Scrammed) Mode = OperatingMode.Shutdown;
        }

        private double SumRodWorth()
        {
            double s = 0;
            foreach (var b in Banks) s += b.ReactivityPcm;
            return s;
        }

        // U1 finite-state and physical-bounds verification sweep.
        public void AssertFinite()
        {
            CheckFinite(Core.Power, "Core.Power", 0.0, 3.01);
            CheckFinite(Core.DecayHeatPower, "Core.DecayHeatPower", 0.0, 1.0);
            CheckFinite(Core.Iodine135, "Core.Iodine135", 0.0, 5.01);
            CheckFinite(Core.Xenon135, "Core.Xenon135", 0.0, 5.01);
            CheckFinite(Core.TotalReactivityPcm, "Core.TotalReactivityPcm", -25000.0, 10000.0);
            CheckFinite(Chemistry.BoronPpm, "Chemistry.BoronPpm", 0.0, 4000.1);
            CheckFinite(Chemistry.Ph, "Chemistry.Ph", 3.9, 10.1);
            CheckFinite(Thermal.FuelTempC, "Thermal.FuelTempC", -45.0, 2201.0);
            CheckFinite(Thermal.PrimaryTempC, "Thermal.PrimaryTempC", -1.0, 370.0);
            CheckFinite(Thermal.PrimaryPressureBar, "Thermal.PrimaryPressureBar", 0.5, 205.0);
            CheckFinite(Thermal.SecondaryPressureBar, "Thermal.SecondaryPressureBar", 0.4, 125.0);
            CheckFinite(Thermal.SecondaryLevelFrac, "Thermal.SecondaryLevelFrac", 0.0, 1.01);
            for (int i = 0; i < Banks.Length; i++)
            {
                CheckFinite(Banks[i].Insertion, "Banks.Insertion", 0.0, 1.01);
                CheckFinite(Banks[i].Integrity, "Banks.Integrity", 0.0, 1.01);
            }
        }

        private static void CheckFinite(double val, string name, double min, double max)
        {
            if (double.IsNaN(val) || double.IsInfinity(val))
                throw new InvalidOperationException("Non-finite state in " + name + ": " + val);
            if (val < min || val > max)
                throw new InvalidOperationException(
                    string.Format(CultureInfo.InvariantCulture, "Out-of-bounds state in {0}: {1} not in [{2}, {3}]", name, val, min, max));
        }

        // U2 Snapshot Capture & Restore
        public PlantSnapshot CaptureState(double simSeconds = 0.0)
        {
            UpdateReactivityLedger();
            var s = new PlantSnapshot
            {
                SimSeconds = simSeconds,
                GameMinutes = simSeconds / 60.0,
                Mode = Mode.ToString(),
                Scrammed = Scrammed,
                AlarmAckSeq = AlarmAckSeq,
                AssistTier = (int)Assistant.Tier,
                ActiveMissionIndex = Assistant.ActiveMissionIndex,

                Power = Core.Power,
                DecayHeatPower = Core.DecayHeatPower,
                TotalHeatPower = Core.TotalHeatPower,
                PeriodSeconds = Core.PeriodSeconds,
                Iodine135 = Core.Iodine135,
                Xenon135 = Core.Xenon135,
                Precursors = Core.CopyPrecursors(),
                DecayStore = Core.CopyDecayStore(),

                BaseReactivityPcm = Core.BaseReactivityPcm,
                RodReactivityPcm = Core.RodReactivityPcm,
                BoronReactivityPcm = Core.BoronReactivityPcm,
                DopplerPcm = Core.DopplerPcm,
                ModeratorPcm = Core.ModeratorPcm,
                XenonPcm = Core.XenonPcm,
                IodinePcm = Core.IodinePcm,
                TotalReactivityPcm = Core.TotalReactivityPcm,
                IsCritical = Core.IsCritical,

                BoronPpm = Chemistry.BoronPpm,
                Ph = Chemistry.Ph,
                BorateRate = Chemistry.BorateRate,
                DiluteRate = Chemistry.DiluteRate,
                NaohRate = Chemistry.NaohRate,
                IonExchangeRate = Chemistry.IonExchangeRate,

                FuelTempC = Thermal.FuelTempC,
                PrimaryTempC = Thermal.PrimaryTempC,
                PrimaryPressureBar = Thermal.PrimaryPressureBar,
                PrimarySatMarginC = Thermal.PrimarySatMarginC,
                PrimaryLevelFrac = Thermal.PrimaryLevelFrac,
                PrimaryFlowFrac = Thermal.PrimaryFlowFrac,
                SteamLevelFrac = Thermal.SteamLevelFrac,
                SecondaryPressureBar = Thermal.SecondaryPressureBar,
                SecondaryTempC = Thermal.SecondaryTempC,
                SteamDemandFrac = Thermal.SteamDemandFrac,
                SecondaryLevelFrac = Thermal.SecondaryLevelFrac,
                FeedwaterFlowFrac = Thermal.FeedwaterFlowFrac,
                SecondaryReliefOpen = Thermal.SecondaryReliefOpen,
                HeaterPowerFrac = Thermal.HeaterPowerFrac,
                SprayFrac = Thermal.SprayFrac,
                ReliefOpen = Thermal.ReliefOpen,

                SteamDumpFrac = Thermal.SteamDumpFrac,
                TurbineSteamKW = Thermal.TurbineSteamKW,
                FeedwaterAvailabilityFrac = Thermal.FeedwaterAvailabilityFrac,
                BopAutoLoad = Bop.AutoLoad,
                GridDemandFrac = Bop.GridDemandFrac,
                VacuumPumpFrac = Bop.VacuumPumpFrac,
                CoolingWaterFrac = Bop.CoolingWaterFrac,
                TurbineRpm = Bop.Turbine.Rpm,
                GeneratorSynced = Bop.Turbine.Synced,
                TurbineTripped = Bop.Turbine.Tripped,
                TurbineMechanicalKw = Bop.Turbine.MechanicalKw,
                GeneratorKw = Bop.Turbine.GeneratorKw,
                GridOutputFrac = Bop.GridOutputFrac,
                GridFrequencyHz = Bop.GridFrequencyHz,
                CondenserPressureBar = Bop.CondenserPressureBar,
                HotwellInventoryFrac = Bop.HotwellInventoryFrac,
                BopTripMask = Bop.TripMask,
                BopTripMeasured = Bop.TripMeasured(),
                BopOutOfSyncSeconds = Bop.OutOfSyncSeconds
            };

            for (int i = 0; i < 4; i++)
            {
                s.BankInsertion[i] = Banks[i].Insertion;
                s.BankTarget[i] = Banks[i].InsertionTarget;
                s.BankIntegrity[i] = Banks[i].Integrity;
                s.BankBurnup[i] = Banks[i].Burnup;
                s.BankSpeed[i] = Banks[i].SpeedPerSec;
            }
            return s;
        }

        public void RestoreState(PlantSnapshot s)
        {
            if (s == null) return;
            OperatingMode parsedMode;
            if (Enum.TryParse(s.Mode, true, out parsedMode))
                Mode = parsedMode;
            Scrammed = s.Scrammed;
            AlarmAckSeq = Math.Max(0, s.AlarmAckSeq);
            if (s.AssistTier >= 0 && s.AssistTier <= 3)
                Assistant.Tier = (AssistTier)s.AssistTier;
            Assistant.SelectMission(s.ActiveMissionIndex, this);

            for (int i = 0; i < 4; i++)
            {
                if (s.BankInsertion != null && i < s.BankInsertion.Length)
                    Banks[i].Insertion = Physics.Clamp(s.BankInsertion[i], 0.0, 1.0);
                if (s.BankTarget != null && i < s.BankTarget.Length)
                    Banks[i].InsertionTarget = Physics.Clamp(s.BankTarget[i], 0.0, 1.0);
                if (s.BankIntegrity != null && i < s.BankIntegrity.Length)
                    Banks[i].Integrity = Physics.Clamp(s.BankIntegrity[i], 0.1, 1.0);
                if (s.BankBurnup != null && i < s.BankBurnup.Length)
                    Banks[i].Burnup = Physics.Clamp(s.BankBurnup[i], 0.0, 1.0);
                if (s.BankSpeed != null && i < s.BankSpeed.Length)
                    Banks[i].SpeedPerSec = Physics.Clamp(s.BankSpeed[i], 0.005, 1.0);
            }

            Chemistry.RestoreState(s.BoronPpm, s.Ph, s.BorateRate, s.DiluteRate, s.NaohRate, s.IonExchangeRate);
            Thermal.RestoreState(
                s.FuelTempC, s.PrimaryTempC, s.PrimaryPressureBar,
                s.PrimaryLevelFrac, s.PrimaryFlowFrac, s.SecondaryPressureBar,
                s.SteamDemandFrac, s.SecondaryLevelFrac, s.FeedwaterFlowFrac,
                s.HeaterPowerFrac, s.SprayFrac, s.ReliefOpen, s.SteamLevelFrac);
            Thermal.SteamDumpFrac = Physics.Clamp(Physics.Sanitize(s.SteamDumpFrac, 0.0), 0.0, 1.0);
            Bop.Restore(s.BopAutoLoad, s.GridDemandFrac, s.VacuumPumpFrac, s.CoolingWaterFrac,
                        s.TurbineRpm, s.GeneratorSynced, s.TurbineTripped, s.CondenserPressureBar,
                        s.HotwellInventoryFrac, s.BopTripMask, s.BopTripMeasured, s.BopOutOfSyncSeconds);
            Thermal.TurbineStopValveOpen = !Bop.Turbine.Tripped;
            Thermal.FeedwaterAvailabilityFrac = Bop.FeedAvailabilityFrac;

            double modDensity = Physics.Clamp(Thermal.PrimaryLevelFrac * (1.0 - 0.5 * Thermal.SteamLevelFrac), 0.0, 1.0);
            Core.BaseReactivityPcm = s.BaseReactivityPcm;
            Core.RestoreState(s.Power, s.Iodine135, s.Xenon135, s.DecayHeatPower, s.Precursors, s.DecayStore, modDensity);
            UpdateReactivityLedger();
            Assistant.Evaluate(this, s.GameMinutes);
        }

        // Load named operational presets for testing, missions, and UI quick-jump.
        public void LoadPreset(string presetName)
        {
            string key = (presetName ?? "cold-shutdown").Trim().ToLowerInvariant();
            Core.Init();
            Scrammed = false;
            foreach (var b in Banks)
            {
                b.Insertion = 1.0;
                b.InsertionTarget = 1.0;
                b.SpeedPerSec = Physics.RodDriveSpeedPerSec;
                b.Burnup = 0.0;
                b.Integrity = 1.0;
            }

            if (key == "hot-standby" || key == "low-power")
            {
                // At P=3%, FuelTemp=300C, PriTemp=291C => Doppler=-54, Xe=-72, I=-6 => sum=-132 pcm.
                // Base=+4500 => +4368 pcm. Bank A=0.50 (-1500 pcm), Boron=1147.2 ppm (-2868 pcm) => net = 0.0 pcm.
                Banks[0].Insertion = Banks[0].InsertionTarget = 0.0;
                Banks[1].Insertion = Banks[1].InsertionTarget = 0.0;
                Banks[2].Insertion = Banks[2].InsertionTarget = 0.0;
                Banks[3].Insertion = Banks[3].InsertionTarget = 0.50;
                Chemistry.RestoreState(1147.2, Physics.CoolantPhOperative, 0.0, 0.0, 0.0, 0.0);
                Thermal.RestoreState(300.0, 291.0, 115.0, 1.0, 1.0, 56.0, 0.03, 0.60, 1.0, 0.50, 0.0, false, 0.0);
                Core.RestoreState(0.03, 0.03, 0.03, 0.002, null, new[] { 0.001, 0.0006, 0.0003 }, 1.0);
                Mode = OperatingMode.Nominal;
                Assistant.SelectMission(0, this);
            }
            else if (key == "full-power" || key == "sustained" || key == "nominal")
            {
                // At P=100%, FuelTemp=605C, PriTemp=305C => Doppler=-1800, Mod=-1.4, Xe=-2400, I=-200 => sum=-4401.4 pcm.
                // Base=+4500 => +98.6 pcm. Bank A=0.16 (-66.7 pcm), Boron=12.8 ppm (-31.9 pcm) => net = 0.0 pcm.
                Banks[0].Insertion = Banks[0].InsertionTarget = 0.0;
                Banks[1].Insertion = Banks[1].InsertionTarget = 0.0;
                Banks[2].Insertion = Banks[2].InsertionTarget = 0.0;
                Banks[3].Insertion = Banks[3].InsertionTarget = 0.16;
                Chemistry.RestoreState(12.8, Physics.CoolantPhOperative, 0.0, 0.0, 0.0, 0.0);
                Thermal.RestoreState(605.0, 305.0, 152.0, 1.0, 1.0, 60.0, 0.96, 0.60, 1.0, 0.75, 0.0, false, 0.0);
                Core.RestoreState(1.0, 1.0, 1.0, 0.063, null, new[] { 0.031, 0.022, 0.010 }, 1.0);
                Mode = OperatingMode.Maximum;
                Assistant.SelectMission(1, this);
            }
            else if (key == "xenon-pit" || key == "pit")
            {
                Scrammed = true;
                foreach (var b in Banks) { b.Insertion = 1.0; b.InsertionTarget = 1.0; b.SpeedPerSec = Physics.RodScramSpeedPerSec; }
                Chemistry.RestoreState(10.0, Physics.CoolantPhOperative, 0.0, 0.0, 0.0, 0.0);
                Thermal.RestoreState(300.0, 292.0, 120.0, 1.0, 1.0, 55.0, 0.05, 0.60, 1.0, 0.50, 0.0, false, 0.0);
                Core.RestoreState(1.0e-5, 0.45, 2.01, 0.025, null, new[] { 0.005, 0.012, 0.008 }, 1.0);
                Mode = OperatingMode.Shutdown;
                Assistant.SelectMission(2, this);
            }
            else // cold-shutdown default
            {
                Chemistry.RestoreState(1500.0, Physics.CoolantPhOperative, 0.0, 0.0, 0.0, 0.0);
                Thermal.RestoreState(60.0, 60.0, 1.0, 1.0, 1.0, 1.0, 0.0, 0.60, 1.0, 0.25, 0.0, false, 0.0);
                Core.RestoreState(1.0e-5, 0.0, 0.0, 0.0, null, new double[3], 1.0);
                Mode = OperatingMode.Shutdown;
                Assistant.SelectMission(0, this);
            }

            Thermal.SteamDumpFrac = 0.0;
            Thermal.TurbineStopValveOpen = true;
            Thermal.FeedwaterAvailabilityFrac = 1.0;
            Bop.SetRunning(key == "full-power" || key == "sustained" || key == "nominal");

            UpdateReactivityLedger();
            Assistant.Evaluate(this, 0.0);
        }

        // Convenience telemetry row.
        public string Telemetry(double tMin)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "t={0,6:F1}m  P={1,7:P2}  rho={2,7:F0}pcm  Tfuel={3,6:F0}C  Tpri={4,5:F0}C  " +
                "Ppri={5,5:F1}bar  Psat={6,5:F0}C  SG={7,4:F1}bar  Xe={8,4:F2}  I={9,4:F2}  " +
                "B={10,4:F0}ppm  pH={11,3:F1}  rods={12,4:F2}  {13}",
                tMin, Core.Power, Core.TotalReactivityPcm,
                Thermal.FuelTempC, Thermal.PrimaryTempC, Thermal.PrimaryPressureBar,
                Thermal.PrimarySatMarginC + Thermal.PrimaryTempC,
                Thermal.SecondaryPressureBar, Core.Xenon135, Core.Iodine135,
                Chemistry.BoronPpm, Chemistry.Ph, Banks[3].Insertion,
                Scrammed ? "SCRAM" : (Core.IsCritical ? "CRITICAL" : "subcritical"));
        }

        /// <summary>
        /// Fail-fast finite/bounds sweep across core, thermal, chemistry, and rods.
        /// Silent Sanitize on the step path must not hide a failed scenario —
        /// callers use this as the assertion surface.
        /// </summary>
        public void AssertFinite(string context = null)
        {
            string where = string.IsNullOrEmpty(context) ? "AssertFinite" : "AssertFinite@" + context;
            if (Physics.SanitizeHits > 0)
                Fail(where, "Sanitize replaced non-finite values (" + Physics.SanitizeHits + " hit(s))");
            RequireFinite(Core.Power, "Power", where);
            RequireFinite(Core.DecayHeatPower, "DecayHeatPower", where);
            RequireFinite(Core.Iodine135, "Iodine135", where);
            RequireFinite(Core.Xenon135, "Xenon135", where);
            RequireFinite(Core.BaseReactivityPcm, "BaseReactivityPcm", where);
            RequireFinite(Core.RodReactivityPcm, "RodReactivityPcm", where);
            RequireFinite(Core.BoronReactivityPcm, "BoronReactivityPcm", where);
            RequireFinite(Core.DopplerPcm, "DopplerPcm", where);
            RequireFinite(Core.ModeratorPcm, "ModeratorPcm", where);
            RequireFinite(Core.XenonPcm, "XenonPcm", where);
            RequireFinite(Core.IodinePcm, "IodinePcm", where);
            RequireFinite(Core.TotalReactivityPcm, "TotalReactivityPcm", where);

            RequireFinite(Thermal.FuelTempC, "FuelTempC", where);
            RequireFinite(Thermal.PrimaryTempC, "PrimaryTempC", where);
            RequireFinite(Thermal.PrimaryPressureBar, "PrimaryPressureBar", where);
            RequireFinite(Thermal.SecondaryPressureBar, "SecondaryPressureBar", where);
            RequireFinite(Thermal.PrimaryLevelFrac, "PrimaryLevelFrac", where);
            RequireFinite(Thermal.SecondaryLevelFrac, "SecondaryLevelFrac", where);
            RequireFinite(Thermal.SteamLevelFrac, "SteamLevelFrac", where);

            RequireFinite(Chemistry.BoronPpm, "BoronPpm", where);
            RequireFinite(Chemistry.Ph, "Ph", where);

            for (int i = 0; i < Banks.Length; i++)
            {
                RequireFinite(Banks[i].Insertion, "Bank" + Banks[i].Name + ".Insertion", where);
                RequireFinite(Banks[i].ReactivityPcm, "Bank" + Banks[i].Name + ".ReactivityPcm", where);
            }

            RequireFinite(Bop.Turbine.Rpm, "TurbineRpm", where);
            RequireFinite(Bop.Turbine.MechanicalKw, "TurbineMechanicalKw", where);
            RequireFinite(Bop.Turbine.GeneratorKw, "GeneratorKw", where);
            RequireFinite(Bop.CondenserPressureBar, "CondenserPressureBar", where);
            RequireFinite(Bop.HotwellInventoryFrac, "HotwellInventoryFrac", where);
            RequireFinite(Bop.GridFrequencyHz, "GridFrequencyHz", where);
            RequireFinite(Thermal.TurbineSteamKW, "TurbineSteamKW", where);
            RequireFinite(Thermal.DumpSteamKW, "DumpSteamKW", where);
            RequireFinite(Thermal.FeedwaterDeliveredFrac, "FeedwaterDeliveredFrac", where);

            RequireInRange(Core.Power, 0, Physics.PowerMaxAssert, "Power", where);
            RequireInRange(Core.DecayHeatPower, 0, Physics.PowerMaxAssert, "DecayHeatPower", where);
            RequireInRange(Core.Iodine135, 0, Physics.PoisonMaxNorm, "Iodine135", where);
            RequireInRange(Core.Xenon135, 0, Physics.PoisonMaxNorm, "Xenon135", where);
            if (Math.Abs(Core.TotalReactivityPcm) > Physics.ReactivityAbsMaxPcm)
                Fail(where, "TotalReactivityPcm out of envelope: " + Core.TotalReactivityPcm);

            RequireInRange(Thermal.FuelTempC, -40.0, Physics.FuelTempMaxC, "FuelTempC", where);
            RequireInRange(Thermal.PrimaryTempC, 0.0, Physics.PrimaryTempMaxC, "PrimaryTempC", where);
            RequireInRange(Thermal.PrimaryPressureBar, 0.5, Physics.PressureMaxBar, "PrimaryPressureBar", where);
            RequireInRange(Thermal.SecondaryPressureBar, 0.4, Physics.SecondaryPressureMaxBar, "SecondaryPressureBar", where);
            RequireInRange(Bop.Turbine.Rpm, 0.0, Physics.TurbineSpeedCeilingRpm, "TurbineRpm", where);
            RequireInRange(Bop.CondenserPressureBar, Physics.CondenserVacuumFloorBar, Physics.CondenserMaxPressureBar, "CondenserPressureBar", where);
            RequireInRange(Bop.HotwellInventoryFrac, 0.0, 1.0, "HotwellInventoryFrac", where);
        }

        private static void RequireFinite(double v, string name, string where)
        {
            if (!Physics.IsFinite(v))
                Fail(where, name + " is not finite: " + v);
        }

        private static void RequireInRange(double v, double lo, double hi, string name, string where)
        {
            if (v < lo || v > hi)
                Fail(where, name + " out of envelope: " + v);
        }

        private static void Fail(string where, string detail)
        {
            throw new InvalidOperationException(where + ": " + detail);
        }
    }
}
