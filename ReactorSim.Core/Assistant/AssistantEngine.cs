// ReactorSim.Core — deterministic Ops Assistant, Alarm Manager, Procedures & Missions (U5, U8).
// Reads live Plant state & reactivity ledger, explains alarms with cause/limit/trend/action,
// tracks mission objectives, and supports Off / Advisory / Supervised / Autopilot tiers.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ReactorSim
{
    public enum AlarmSeverity
    {
        Advisory = 0,
        Caution = 1,
        Warning = 2,
        Critical = 3
    }

    [Serializable]
    public class PlantAlarm
    {
        public string Code = "";
        public AlarmSeverity Severity;
        public string Title = "";
        public string Cause = "";
        public string CurrentVsLimit = "";
        public string Trend = "";
        public string NextAction = "";

        public string ToJson()
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"code\":\"").Append(PlantIntent.EscapeJson(Code)).Append("\"");
            sb.Append(",\"severity\":\"").Append(Severity.ToString()).Append("\"");
            sb.Append(",\"title\":\"").Append(PlantIntent.EscapeJson(Title)).Append("\"");
            sb.Append(",\"cause\":\"").Append(PlantIntent.EscapeJson(Cause)).Append("\"");
            sb.Append(",\"currentVsLimit\":\"").Append(PlantIntent.EscapeJson(CurrentVsLimit)).Append("\"");
            sb.Append(",\"trend\":\"").Append(PlantIntent.EscapeJson(Trend)).Append("\"");
            sb.Append(",\"nextAction\":\"").Append(PlantIntent.EscapeJson(NextAction)).Append("\"}");
            return sb.ToString();
        }
    }

    [Serializable]
    public class AdvisoryMessage
    {
        public double GameMinute;
        public string Category = "OPS";
        public string Finding = "";
        public string Evidence = "";
        public string Recommendation = "";
        public PlantIntent SuggestedIntent;

        public string FormatTerminalLine()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[{0,5:F1}m | {1}] {2} — Evidence: {3} -> {4}",
                GameMinute, Category, Finding, Evidence, Recommendation);
        }

        public string ToJson()
        {
            var sb = new StringBuilder(256);
            sb.AppendFormat(CultureInfo.InvariantCulture, "{{\"gameMinute\":{0:F2}", GameMinute);
            sb.Append(",\"category\":\"").Append(PlantIntent.EscapeJson(Category)).Append("\"");
            sb.Append(",\"finding\":\"").Append(PlantIntent.EscapeJson(Finding)).Append("\"");
            sb.Append(",\"evidence\":\"").Append(PlantIntent.EscapeJson(Evidence)).Append("\"");
            sb.Append(",\"recommendation\":\"").Append(PlantIntent.EscapeJson(Recommendation)).Append("\"");
            if (SuggestedIntent != null)
                sb.Append(",\"suggestedIntent\":").Append(SuggestedIntent.ToJson());
            else
                sb.Append(",\"suggestedIntent\":null");
            sb.Append("}");
            return sb.ToString();
        }
    }

    public class MissionStep
    {
        public string Title;
        public string Instruction;
        public Func<Plant, bool> IsComplete;
        public Func<Plant, PlantIntent> AutopilotIntent;
        public bool Completed;
    }

    public class MissionDefinition
    {
        public int Index;
        public string Id;
        public string Title;
        public string Summary;
        public List<MissionStep> Steps = new List<MissionStep>();
        public bool Completed;

        public int CurrentStepIndex
        {
            get
            {
                for (int i = 0; i < Steps.Count; i++)
                {
                    if (!Steps[i].Completed) return i;
                }
                return Steps.Count - 1;
            }
        }
    }

    public class AssistantEngine
    {
        public AssistTier Tier { get; set; } = AssistTier.Advisory;
        public List<PlantAlarm> ActiveAlarms { get; private set; } = new List<PlantAlarm>();
        public List<AdvisoryMessage> MessageLog { get; private set; } = new List<AdvisoryMessage>();
        public AdvisoryMessage LatestAdvisory { get; private set; }
        public PlantIntent PendingSupervisedIntent { get; private set; }

        public List<MissionDefinition> Missions { get; private set; } = new List<MissionDefinition>();
        public int ActiveMissionIndex { get; private set; } = 0;

        // Previous tick values for trend calculation
        private double _prevFuelTemp = 60.0;
        private double _prevPriTemp = 60.0;
        private double _prevSgPress = 1.0;
        private double _prevXenon = 0.0;
        private string _lastLoggedSignature = "";
        private double _lastLoggedMinute = -10.0;

        // Track whether player attempted restart during xenon pit in Mission 3
        public bool ExperiencedXenonPitDenial { get; private set; }

        public AssistantEngine()
        {
            BuildMissions();
        }

        public MissionDefinition CurrentMission
        {
            get
            {
                if (Missions.Count == 0) return null;
                int idx = Math.Max(0, Math.Min(Missions.Count - 1, ActiveMissionIndex));
                return Missions[idx];
            }
        }

        public void SelectMission(int index, Plant plant = null)
        {
            ActiveMissionIndex = Math.Max(0, Math.Min(Missions.Count - 1, index));
            ExperiencedXenonPitDenial = false;
            var m = CurrentMission;
            if (m != null)
            {
                m.Completed = false;
                foreach (var s in m.Steps) s.Completed = false;
            }
            _lastLoggedSignature = "";
        }

        private void BuildMissions()
        {
            Missions.Clear();

            // Mission 1: First Criticality (F1)
            var m1 = new MissionDefinition
            {
                Index = 0,
                Id = "mission-1-startup",
                Title = "Mission 1: First Criticality",
                Summary = "Take the reactor from cold shutdown to stable low-power criticality using shutdown banks, boron dilution, and Regulating Bank A."
            };
            m1.Steps.Add(new MissionStep
            {
                Title = "1. Establish Primary Circulation",
                Instruction = "Start Reactor Coolant Pumps (Primary Flow >= 80%) and verify Pressurizer heaters are active.",
                IsComplete = p => p.Thermal.PrimaryFlowFrac >= 0.80,
                AutopilotIntent = p => PlantIntent.SetPrimaryFlow(1.0, "autopilot")
            });
            m1.Steps.Add(new MissionStep
            {
                Title = "2. Withdraw Shutdown Banks D, C, B",
                Instruction = "Withdraw Shutdown Banks D, C, and B fully (target 0.00) while keeping Regulating Bank A inserted.",
                IsComplete = p => p.Banks[0].Insertion < 0.05 && p.Banks[1].Insertion < 0.05 && p.Banks[2].Insertion < 0.05,
                AutopilotIntent = p =>
                {
                    if (p.Banks[0].InsertionTarget > 0.0) return PlantIntent.SetRodBank(0, 0.0, "autopilot");
                    if (p.Banks[1].InsertionTarget > 0.0) return PlantIntent.SetRodBank(1, 0.0, "autopilot");
                    if (p.Banks[2].InsertionTarget > 0.0) return PlantIntent.SetRodBank(2, 0.0, "autopilot");
                    return null;
                }
            });
            m1.Steps.Add(new MissionStep
            {
                Title = "3. Dilute Boron & Withdraw Bank A to Criticality",
                Instruction = "Set Boron Dilution (~2.5 ppm/s) and withdraw Bank A toward 0.44 until power rises above 2%.",
                IsComplete = p => p.Core.IsCritical && p.Core.Power >= 0.02,
                AutopilotIntent = p =>
                {
                    if (p.Chemistry.BoronPpm > 1180.0 && p.Chemistry.DiluteRate < 2.0)
                        return PlantIntent.SetDilute(2.5, "autopilot");
                    if (p.Chemistry.BoronPpm <= 1180.0 && p.Chemistry.DiluteRate > 0.0)
                        return PlantIntent.SetDilute(0.0, "autopilot");
                    if (p.Banks[3].InsertionTarget > 0.44)
                        return PlantIntent.SetRodBank(3, 0.44, "autopilot");
                    return null;
                }
            });
            m1.Steps.Add(new MissionStep
            {
                Title = "4. Stabilize Low-Power Operation (2% - 40%)",
                Instruction = "Stop boron dilution and let Doppler feedback hold power steady between 2% and 40% with no SCRAM.",
                IsComplete = p => !p.Scrammed && p.Core.Power >= 0.02 && p.Core.Power <= 0.40 && Math.Abs(p.Core.TotalReactivityPcm) < 80.0,
                AutopilotIntent = p =>
                {
                    if (p.Chemistry.DiluteRate > 0.0) return PlantIntent.SetDilute(0.0, "autopilot");
                    if (p.Thermal.SteamDemandFrac < 0.15) return PlantIntent.SetSteamDemand(0.15, "autopilot");
                    return null;
                }
            });
            Missions.Add(m1);

            // Mission 2: Power Ascension & SCRAM (F2 front half)
            var m2 = new MissionDefinition
            {
                Index = 1,
                Id = "mission-2-power-scram",
                Title = "Mission 2: Power Ascension & SCRAM",
                Summary = "Ascend to sustained high power (>60%), balance secondary steam draw, build I-135/Xe-135 inventory, then execute a clean SCRAM."
            };
            m2.Steps.Add(new MissionStep
            {
                Title = "1. Ascend Above 60% Rated Power",
                Instruction = "Open Turbine Steam Demand (>= 60%), dilute boron, and withdraw Bank A to bring thermal power above 60%.",
                IsComplete = p => p.Core.Power >= 0.60,
                AutopilotIntent = p =>
                {
                    if (p.Thermal.SteamDemandFrac < 0.75) return PlantIntent.SetSteamDemand(0.75, "autopilot");
                    if (p.Chemistry.BoronPpm > 200.0 && p.Chemistry.DiluteRate < 2.0) return PlantIntent.SetDilute(2.5, "autopilot");
                    if (p.Banks[3].InsertionTarget > 0.20) return PlantIntent.SetRodBank(3, 0.20, "autopilot");
                    return null;
                }
            });
            m2.Steps.Add(new MissionStep
            {
                Title = "2. Build Fission-Product Poison Inventory",
                Instruction = "Hold high power until Iodine-135 and Xenon-135 build above 0.45 normalized equilibrium units.",
                IsComplete = p => p.Core.Iodine135 >= 0.45 && p.Core.Xenon135 >= 0.45,
                AutopilotIntent = p =>
                {
                    if (p.Chemistry.BoronPpm < 150.0 && p.Chemistry.DiluteRate > 0.0) return PlantIntent.SetDilute(0.0, "autopilot");
                    return null;
                }
            });
            m2.Steps.Add(new MissionStep
            {
                Title = "3. Trigger Reactor SCRAM & Verify Decay-Heat Cooling",
                Instruction = "Press SCRAM. Observe fast rod insertion (-8000 pcm), prompt power collapse, and decay-heat removal via primary flow.",
                IsComplete = p => p.Scrammed && p.Core.Power < 0.005 && p.Thermal.PrimaryFlowFrac >= 0.5,
                AutopilotIntent = p => !p.Scrammed ? PlantIntent.TriggerScram("autopilot") : null
            });
            Missions.Add(m2);

            // Mission 3: The Xenon Pit (F2 back half)
            var m3 = new MissionDefinition
            {
                Index = 2,
                Id = "mission-3-xenon-pit",
                Title = "Mission 3: The Xenon Pit",
                Summary = "Experience post-SCRAM Xenon-135 peaking where I-135 decay locks the core subcritical even with all rods out, then recover once the pit clears."
            };
            m3.Steps.Add(new MissionStep
            {
                Title = "1. Observe Xenon-135 Peak (> 1.45 eq / < -3400 pcm)",
                Instruction = "After SCRAM from full power, watch I-135 decay into Xe-135 without neutron burnup until Xe-135 exceeds 1.45.",
                IsComplete = p => p.Core.Xenon135 >= 1.45,
                AutopilotIntent = null
            });
            m3.Steps.Add(new MissionStep
            {
                Title = "2. Attempt Restart Inside the Xenon Pit (Physics Lockout)",
                Instruction = "Reset SCRAM and withdraw all rod banks (0.00). Observe that Xenon poisoning (-4800+ pcm) denies criticality.",
                IsComplete = p => ExperiencedXenonPitDenial,
                AutopilotIntent = p =>
                {
                    if (p.Scrammed) return PlantIntent.ClearScram("autopilot");
                    if (p.Banks[3].InsertionTarget > 0.0) return PlantIntent.SetAllRods(0.0, "autopilot");
                    return null;
                }
            });
            m3.Steps.Add(new MissionStep
            {
                Title = "3. Wait Out the Pit & Re-Establish Criticality",
                Instruction = "Allow Xe-135 to decay until net reactivity turns positive and reactor power recovers above 2%.",
                IsComplete = p => !p.Scrammed && p.Core.Power >= 0.02 && p.Core.IsCritical,
                AutopilotIntent = p =>
                {
                    if (p.Chemistry.BoronPpm > 1.0 && p.Chemistry.DiluteRate < 2.0)
                        return PlantIntent.SetDilute(3.0, "autopilot");
                    if (p.Chemistry.BoronPpm <= 1.0 && p.Chemistry.DiluteRate > 0.0)
                        return PlantIntent.SetDilute(0.0, "autopilot");
                    if (p.Banks[3].InsertionTarget < 0.35 && p.Core.Xenon135 < 1.82)
                        return PlantIntent.SetRodBank(3, 0.35, "autopilot");
                    return null;
                }
            });
            Missions.Add(m3);
        }

        // Estimates time to Xenon peak and time until Xenon decays below restart threshold.
        public void EstimateXenonPitWindow(Plant p, out double peakXe, out double minutesToPeak, out double minutesToRecovery)
        {
            double w = Physics.PoisonTimeWarp;
            double lI = Physics.LambdaIodine * w;
            double lXe = Physics.LambdaXenon * w;
            double burnAtFull = Physics.XenonBurnPerPower * w;
            double totalYield = Physics.YieldIodine135 + Physics.YieldXenon135;
            double fI = Physics.YieldIodine135 / totalYield;
            double rEq = lXe + burnAtFull;

            double simI = p.Core.Iodine135;
            double simXe = p.Core.Xenon135;
            peakXe = simXe;
            minutesToPeak = 0.0;
            minutesToRecovery = 0.0;

            double dt = 6.0; // 0.1 game-min step
            bool passedPeak = false;
            for (int step = 1; step <= 1200; step++) // up to 120 game-minutes
            {
                double expI = Math.Exp(-lI * dt);
                simI *= expI;
                double sourceXe = fI * simI * rEq;
                double expXe = Math.Exp(-lXe * dt);
                simXe = simXe * expXe + (sourceXe / lXe) * (1.0 - expXe);

                double min = (step * dt) / 60.0;
                if (simXe > peakXe && !passedPeak)
                {
                    peakXe = simXe;
                    minutesToPeak = min;
                }
                else if (simXe < peakXe - 0.005)
                {
                    passedPeak = true;
                }

                double maxTolerableXeWorth = Math.Max(2400.0,
                    p.Core.BaseReactivityPcm + simI * ReactorCore.IWorthAtEq);
                double restartXeThreshold = maxTolerableXeWorth / Math.Abs(ReactorCore.XeWorthAtEq);
                if (simXe > restartXeThreshold)
                {
                    minutesToRecovery = min;
                }
            }
        }

        public void Evaluate(Plant p, double gameMinutes)
        {
            ActiveAlarms.Clear();

            double dFuel = p.Thermal.FuelTempC - _prevFuelTemp;
            double dPri = p.Thermal.PrimaryTempC - _prevPriTemp;
            double dSgP = p.Thermal.SecondaryPressureBar - _prevSgPress;
            double dXe = p.Core.Xenon135 - _prevXenon;

            // Check if player is attempting restart during Xenon pit (all rods commanded out, not scrammed, Xe > 1.35, rho < 0)
            if (!p.Scrammed && p.Core.Xenon135 > 1.30 && p.Banks[3].Insertion < 0.15 && p.Core.TotalReactivityPcm < -20.0)
            {
                ExperiencedXenonPitDenial = true;
            }

            // 1. Evaluate Alarms (with Cause, Value vs Limit, Trend, Next Action - R11)
            if (p.Scrammed)
            {
                ActiveAlarms.Add(new PlantAlarm
                {
                    Code = "RX_SCRAM",
                    Severity = AlarmSeverity.Warning,
                    Title = "REACTOR SCRAM TRIP ACTIVE",
                    Cause = string.Format(CultureInfo.InvariantCulture, "Control rods fast-inserted ({0:F0} pcm); fission chain reaction terminated.", p.Core.RodReactivityPcm),
                    CurrentVsLimit = string.Format(CultureInfo.InvariantCulture, "P={0:P2} | Decay Heat={1:P2}", p.Core.Power, p.Core.DecayHeatPower),
                    Trend = dFuel < -0.1 ? "Cooling" : "Steady",
                    NextAction = "Maintain primary circulation flow (>= 80%) to remove decay heat before resetting SCRAM."
                });
            }

            // Excursion / high reactivity / short period (AE1)
            if (p.Core.TotalReactivityPcm > 220.0 || (p.Core.PeriodSeconds > 0 && p.Core.PeriodSeconds < 15.0 && p.Core.Power > 0.01))
            {
                ActiveAlarms.Add(new PlantAlarm
                {
                    Code = "REACTIVITY_EXCURSION",
                    Severity = p.Core.TotalReactivityPcm > 450.0 ? AlarmSeverity.Critical : AlarmSeverity.Warning,
                    Title = "HIGH POSITIVE REACTIVITY / SHORT PERIOD",
                    Cause = string.Format(
                        CultureInfo.InvariantCulture,
                        "Reactivity ledger imbalance: Base +{0:F0} + Rods {1:F0} + Boron {2:F0} + Doppler {3:F0} + Xe {4:F0} = {5:+0;-0;0} pcm.",
                        p.Core.BaseReactivityPcm, p.Core.RodReactivityPcm, p.Core.BoronReactivityPcm,
                        p.Core.DopplerPcm, p.Core.XenonPcm, p.Core.TotalReactivityPcm),
                    CurrentVsLimit = string.Format(CultureInfo.InvariantCulture, "rho={0:+0;-0;0} pcm (limit +200 pcm) | T={1:F1}s", p.Core.TotalReactivityPcm, p.Core.PeriodSeconds),
                    Trend = dFuel > 1.0 ? "Fuel heating rapidly (Doppler Negative Feedback building)" : "Rising",
                    NextAction = "Insert Regulating Bank A (increase insertion target) or SCRAM if power exceeds 115%."
                });
            }

            // High Fuel Temperature
            if (p.Thermal.FuelTempC > 850.0)
            {
                ActiveAlarms.Add(new PlantAlarm
                {
                    Code = "FUEL_TEMP_HIGH",
                    Severity = p.Thermal.FuelTempC > 1200.0 ? AlarmSeverity.Critical : AlarmSeverity.Warning,
                    Title = "HIGH FUEL CLADDING TEMPERATURE",
                    Cause = string.Format(CultureInfo.InvariantCulture, "Core heat ({0:F0} MWt) exceeds fuel-to-primary transfer at Tpri={1:F0}C.", p.Core.TotalHeatPower * Physics.RatedThermalMWt, p.Thermal.PrimaryTempC),
                    CurrentVsLimit = string.Format(CultureInfo.InvariantCulture, "{0:F0}C / 850C nominal (1600C damage)", p.Thermal.FuelTempC),
                    Trend = TrendLabel(dFuel, 0.5),
                    NextAction = "Insert Bank A to reduce reactor power or increase primary coolant flow."
                });
            }

            // Loss of secondary heat sink / SG overpressure (AE3)
            if (p.Thermal.SecondaryPressureBar > 68.0 || (p.Core.TotalHeatPower > 0.15 && p.Thermal.SteamDemandFrac < 0.05 && dSgP > 0.05))
            {
                ActiveAlarms.Add(new PlantAlarm
                {
                    Code = "LOSS_OF_SECONDARY_SINK",
                    Severity = p.Thermal.SecondaryReliefOpen ? AlarmSeverity.Critical : AlarmSeverity.Warning,
                    Title = p.Thermal.SecondaryReliefOpen ? "SG SAFETY RELIEF LIFTING (LOSS OF SINK)" : "SECONDARY STEAM GENERATOR HIGH PRESSURE",
                    Cause = string.Format(
                        CultureInfo.InvariantCulture,
                        "Secondary steam demand ({0:P0}) is below primary heat input ({1:P0}), driving SG pressure and Tsat ({2:F0}C) up, which reduces primary-to-SG cooling and raises Primary Temp ({3:F0}C) & Pressure ({4:F1} bar).",
                        p.Thermal.SteamDemandFrac, p.Core.TotalHeatPower, p.Thermal.SecondaryTempC, p.Thermal.PrimaryTempC, p.Thermal.PrimaryPressureBar),
                    CurrentVsLimit = string.Format(CultureInfo.InvariantCulture, "SG={0:F1} bar / 68.0 bar (Relief @ 75 bar)", p.Thermal.SecondaryPressureBar),
                    Trend = TrendLabel(dSgP, 0.05),
                    NextAction = "Open Turbine Steam Demand to match reactor thermal power, or insert rods to reduce heat load."
                });
            }

            // Low Primary Subcooling Margin
            if (p.Thermal.PrimaryTempC > 120.0 && p.Thermal.PrimarySatMarginC < 12.0)
            {
                ActiveAlarms.Add(new PlantAlarm
                {
                    Code = "LOW_SUBCOOLING",
                    Severity = p.Thermal.PrimarySatMarginC < 3.0 ? AlarmSeverity.Critical : AlarmSeverity.Caution,
                    Title = "LOW PRIMARY SUBCOOLING MARGIN",
                    Cause = string.Format(CultureInfo.InvariantCulture, "Primary temp ({0:F0}C) is approaching saturation temp ({1:F0}C) at {2:F1} bar; risk of core voiding.", p.Thermal.PrimaryTempC, p.Thermal.PrimaryTempC + p.Thermal.PrimarySatMarginC, p.Thermal.PrimaryPressureBar),
                    CurrentVsLimit = string.Format(CultureInfo.InvariantCulture, "Margin={0:F1}C / 15.0C min (Void={1:P0})", p.Thermal.PrimarySatMarginC, p.Thermal.SteamLevelFrac),
                    Trend = TrendLabel(-dPri, 0.2),
                    NextAction = "Increase Pressurizer Heaters (>50%) and close Pressurizer Spray / PORV relief valve."
                });
            }

            // Xenon Pit Advisory / Warning (AE2)
            if (p.Core.Xenon135 > 1.25 && p.Core.Power < 0.05)
            {
                double peakXe, minToPeak, minToRec;
                EstimateXenonPitWindow(p, out peakXe, out minToPeak, out minToRec);
                ActiveAlarms.Add(new PlantAlarm
                {
                    Code = "XENON_PIT_TRANSIENT",
                    Severity = minToRec > 0.5 ? AlarmSeverity.Warning : AlarmSeverity.Caution,
                    Title = minToRec > 0.5 ? "XENON PIT LOCKOUT (RESTART DENIED BY POISON)" : "POST-SCRAM XENON PEAK BUILDING",
                    Cause = string.Format(
                        CultureInfo.InvariantCulture,
                        "I-135 ({0:F2}) is decaying into Xe-135 ({1:F2}, {2:F0} pcm) with zero neutron flux burnup. Peak Xe ~{3:F2} ({4:F0} pcm) in {5:F1}m; recovery window in ~{6:F1}m.",
                        p.Core.Iodine135, p.Core.Xenon135, p.Core.XenonPcm, peakXe, peakXe * ReactorCore.XeWorthAtEq, minToPeak, minToRec),
                    CurrentVsLimit = string.Format(CultureInfo.InvariantCulture, "Xe={0:F2} ({1:F0} pcm) | Net rho={2:+0;-0;0} pcm", p.Core.Xenon135, p.Core.XenonPcm, p.Core.TotalReactivityPcm),
                    Trend = dXe > 0.001 ? "Rising into pit peak" : "Decaying toward recovery",
                    NextAction = minToRec > 0.5
                        ? string.Format(CultureInfo.InvariantCulture, "Wait ~{0:F0} game-min for Xe-135 decay and dilute Boron to 0 ppm to widen restart margin.", minToRec)
                        : "Dilute Boron and withdraw Shutdown Banks D-B to prepare for re-criticality."
                });
            }

            // 2. Advance Mission Steps
            var mission = CurrentMission;
            if (mission != null && !mission.Completed)
            {
                bool allDone = true;
                for (int i = 0; i < mission.Steps.Count; i++)
                {
                    var step = mission.Steps[i];
                    if (!step.Completed && step.IsComplete != null && step.IsComplete(p))
                    {
                        step.Completed = true;
                    }
                    if (!step.Completed)
                    {
                        allDone = false;
                        break; // sequential progression
                    }
                }
                if (allDone) mission.Completed = true;
            }

            // 3. Build Assistant Advisory (R9, R10)
            if (Tier == AssistTier.Off)
            {
                LatestAdvisory = null;
                PendingSupervisedIntent = null;
            }
            else
            {
                var adv = BuildAdvisory(p, gameMinutes);
                LatestAdvisory = adv;
                PendingSupervisedIntent = (Tier == AssistTier.Supervised) ? adv.SuggestedIntent : null;

                // Record to log when advisory changes meaningfully or every 5 game-minutes
                string sig = adv.Category + "|" + adv.Finding;
                if (sig != _lastLoggedSignature || (gameMinutes - _lastLoggedMinute) >= 5.0)
                {
                    _lastLoggedSignature = sig;
                    _lastLoggedMinute = gameMinutes;
                    MessageLog.Add(adv);
                    if (MessageLog.Count > 100)
                        MessageLog.RemoveAt(0);
                }

                // Autopilot tier executes bounded procedural actions automatically
                if (Tier == AssistTier.Autopilot && adv.SuggestedIntent != null)
                {
                    p.Apply(adv.SuggestedIntent);
                }
            }

            _prevFuelTemp = p.Thermal.FuelTempC;
            _prevPriTemp = p.Thermal.PrimaryTempC;
            _prevSgPress = p.Thermal.SecondaryPressureBar;
            _prevXenon = p.Core.Xenon135;
        }

        private AdvisoryMessage BuildAdvisory(Plant p, double gameMinutes)
        {
            var ic = CultureInfo.InvariantCulture;

            // Priority 1: Excursion / Safety protection
            if (p.Core.Power > 1.15 && !p.Scrammed)
            {
                return new AdvisoryMessage
                {
                    GameMinute = gameMinutes,
                    Category = "SAFETY",
                    Finding = string.Format(ic, "Overpower transient at {0:P1}; immediate rod insertion required.", p.Core.Power),
                    Evidence = string.Format(ic, "rho={0:+0;-0;0} pcm (Rods {1:F0}, Boron {2:F0}, Doppler {3:F0}, Xe {4:F0}), Tfuel={5:F0}C",
                        p.Core.TotalReactivityPcm, p.Core.RodReactivityPcm, p.Core.BoronReactivityPcm, p.Core.DopplerPcm, p.Core.XenonPcm, p.Thermal.FuelTempC),
                    Recommendation = "Insert Regulating Bank A to 0.60 or SCRAM if fuel exceeds 1100C.",
                    SuggestedIntent = p.Thermal.FuelTempC > 1100.0
                        ? PlantIntent.TriggerScram("assistant")
                        : PlantIntent.SetRodBank(3, Math.Min(1.0, p.Banks[3].Insertion + 0.25), "assistant")
                };
            }

            // Priority 2: Xenon Pit explanation (AE2)
            if (p.Core.Xenon135 > 1.25 && p.Core.Power < 0.05)
            {
                double peakXe, minToPeak, minToRec;
                EstimateXenonPitWindow(p, out peakXe, out minToPeak, out minToRec);

                PlantIntent suggested = null;
                var activeMission = CurrentMission;
                if (activeMission != null && !activeMission.Completed)
                {
                    var mStep = activeMission.Steps[activeMission.CurrentStepIndex];
                    if (mStep.AutopilotIntent != null)
                        suggested = mStep.AutopilotIntent(p);
                }
                if (suggested == null && p.Chemistry.BoronPpm > 10.0 && p.Chemistry.DiluteRate < 2.0)
                    suggested = PlantIntent.SetDilute(2.5, "assistant");

                return new AdvisoryMessage
                {
                    GameMinute = gameMinutes,
                    Category = "POISON",
                    Finding = minToRec > 0.5
                        ? string.Format(ic, "Xenon-135 pit active (Xe={0:F2}, {1:F0} pcm) — restart physically blocked for ~{2:F1} game-min.", p.Core.Xenon135, p.Core.XenonPcm, minToRec)
                        : string.Format(ic, "Xenon-135 decaying past pit threshold (Xe={0:F2}, {1:F0} pcm) — core can now reach criticality.", p.Core.Xenon135, p.Core.XenonPcm),
                    Evidence = string.Format(ic, "I-135={0:F2} decays into Xe-135 with zero flux burnup; peak Xe={1:F2} ({2:F0} pcm) vs clean excess +{3:F0} pcm.",
                        p.Core.Iodine135, peakXe, peakXe * ReactorCore.XeWorthAtEq, p.Core.BaseReactivityPcm),
                    Recommendation = p.Chemistry.BoronPpm > 10.0
                        ? "Dilute remaining Boron to 0 ppm to maximize positive reactivity margin as Xenon decays."
                        : "Hold cooling flow and wait for Xe-135 decay to lift net rho above 0 pcm.",
                    SuggestedIntent = suggested
                };
            }

            // Priority 3: Loss of Secondary Sink (AE3)
            if (p.Thermal.SecondaryPressureBar > 68.0)
            {
                return new AdvisoryMessage
                {
                    GameMinute = gameMinutes,
                    Category = "THERMAL",
                    Finding = string.Format(ic, "Secondary SG pressure high ({0:F1} bar) due to low steam draw vs reactor power.", p.Thermal.SecondaryPressureBar),
                    Evidence = string.Format(ic, "Core heat={0:P0}, SteamDemand={1:P0}, SG Tsat={2:F0}C -> Primary Tpri={3:F0}C ({4:F1} bar).",
                        p.Core.TotalHeatPower, p.Thermal.SteamDemandFrac, p.Thermal.SecondaryTempC, p.Thermal.PrimaryTempC, p.Thermal.PrimaryPressureBar),
                    Recommendation = "Open Turbine Steam Demand to match thermal power and arrest SG pressure rise.",
                    SuggestedIntent = PlantIntent.SetSteamDemand(Math.Max(0.25, Math.Min(1.0, p.Core.TotalHeatPower)), "assistant")
                };
            }

            // Priority 4: Active Mission / Procedure guidance
            var m = CurrentMission;
            if (m != null && !m.Completed)
            {
                var step = m.Steps[m.CurrentStepIndex];
                return new AdvisoryMessage
                {
                    GameMinute = gameMinutes,
                    Category = "PROC",
                    Finding = string.Format(ic, "{0} — {1}", m.Title, step.Title),
                    Evidence = string.Format(ic, "P={0:P2}, rho={1:+0;-0;0} pcm (Rods {2:F0}, B {3:F0}ppm={4:F0}pcm, Dop {5:F0}, Xe {6:F0}), Tfuel={7:F0}C, SG={8:F1}bar",
                        p.Core.Power, p.Core.TotalReactivityPcm, p.Core.RodReactivityPcm, p.Chemistry.BoronPpm,
                        p.Core.BoronReactivityPcm, p.Core.DopplerPcm, p.Core.XenonPcm, p.Thermal.FuelTempC, p.Thermal.SecondaryPressureBar),
                    Recommendation = step.Instruction,
                    SuggestedIntent = step.AutopilotIntent != null ? step.AutopilotIntent(p) : null
                };
            }

            if (p.Core.TotalReactivityPcm > 220.0 && !p.Scrammed)
            {
                return new AdvisoryMessage
                {
                    GameMinute = gameMinutes,
                    Category = "KINETICS",
                    Finding = string.Format(ic, "Fast positive reactivity excursion (+{0:F0} pcm, period {1:F1}s).", p.Core.TotalReactivityPcm, p.Core.PeriodSeconds),
                    Evidence = string.Format(ic, "Ledger: Base +{0:F0} pcm, Rods {1:F0} pcm, Boron {2:F0} pcm; Doppler ({3:F0} pcm) will turn negative as Tfuel ({4:F0}C) heats.",
                        p.Core.BaseReactivityPcm, p.Core.RodReactivityPcm, p.Core.BoronReactivityPcm, p.Core.DopplerPcm, p.Thermal.FuelTempC),
                    Recommendation = "Insert Bank A slightly (+10%) to arrest period before Doppler overshoot.",
                    SuggestedIntent = PlantIntent.SetRodBank(3, Math.Min(1.0, p.Banks[3].InsertionTarget + 0.10), "assistant")
                };
            }

            // Default steady-state monitoring
            return new AdvisoryMessage
            {
                GameMinute = gameMinutes,
                Category = "NOMINAL",
                Finding = string.Format(ic, "Plant operating at {0:P1} power ({1:F0} MWt).", p.Core.Power, p.Core.Power * Physics.RatedThermalMWt),
                Evidence = string.Format(ic, "rho={0:+0;-0;0} pcm, Tfuel={1:F0}C, Tpri={2:F0}C ({3:F1} bar), SG={4:F1} bar, Xe={5:F2}",
                    p.Core.TotalReactivityPcm, p.Thermal.FuelTempC, p.Thermal.PrimaryTempC,
                    p.Thermal.PrimaryPressureBar, p.Thermal.SecondaryPressureBar, p.Core.Xenon135),
                Recommendation = "Monitor reactivity ledger and balance turbine steam demand with thermal output.",
                SuggestedIntent = null
            };
        }

        private static string TrendLabel(double delta, double threshold)
        {
            if (delta > threshold) return "Rising";
            if (delta < -threshold) return "Falling";
            return "Steady";
        }
    }
}
