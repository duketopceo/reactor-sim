// ReactorSim.Core — full plant state snapshot & deterministic JSON round-trip (U2).
// Used for save/load, network state broadcast (U7), HTTP/MCP state payload (U3/U4),
// and deterministic test assertions (U1).

using System;
using System.Globalization;
using System.Text;

namespace ReactorSim
{
    [Serializable]
    public class PlantSnapshot
    {
        public double SimSeconds;
        public double GameMinutes;
        public string Mode = "Shutdown";
        public bool Scrammed;
        public int AssistTier = 1;
        public int ActiveMissionIndex = 0;

        // Core neutronics & poisons
        public double Power;
        public double DecayHeatPower;
        public double TotalHeatPower;
        public double PeriodSeconds;
        public double Iodine135;
        public double Xenon135;
        public double[] Precursors = new double[6];
        public double[] DecayStore = new double[3];

        // Reactivity ledger (pcm)
        public double BaseReactivityPcm;
        public double RodReactivityPcm;
        public double BoronReactivityPcm;
        public double DopplerPcm;
        public double ModeratorPcm;
        public double XenonPcm;
        public double IodinePcm;
        public double TotalReactivityPcm;
        public bool IsCritical;

        // Control rod banks (4 banks: D, C, B, A)
        public double[] BankInsertion = new double[4];
        public double[] BankTarget = new double[4];
        public double[] BankIntegrity = new double[4];
        public double[] BankBurnup = new double[4];
        public double[] BankSpeed = new double[4];

        // Chemistry
        public double BoronPpm;
        public double Ph;
        public double BorateRate;
        public double DiluteRate;
        public double NaohRate;
        public double IonExchangeRate;

        // Thermal-hydraulics
        public double FuelTempC;
        public double PrimaryTempC;
        public double PrimaryPressureBar;
        public double PrimarySatMarginC;
        public double PrimaryLevelFrac;
        public double PrimaryFlowFrac;
        public double SteamLevelFrac;
        public double SecondaryPressureBar;
        public double SecondaryTempC;
        public double SteamDemandFrac;
        public double SecondaryLevelFrac;
        public double FeedwaterFlowFrac;
        public bool SecondaryReliefOpen;
        public double HeaterPowerFrac;
        public double SprayFrac;
        public bool ReliefOpen;

        // Balance of plant (defaults = a snapshot written before the BOP existed)
        public double SteamDumpFrac;
        public double TurbineSteamKW;
        public double FeedwaterAvailabilityFrac = 1.0;
        public bool BopAutoLoad = true;
        public double GridDemandFrac;
        public double VacuumPumpFrac = 1.0;
        public double CoolingWaterFrac = 1.0;
        public double TurbineRpm;
        public bool GeneratorSynced;
        public bool TurbineTripped;
        public double TurbineMechanicalKw;
        public double GeneratorKw;
        public double GridOutputFrac;
        public double GridFrequencyHz = Physics.GridNominalFrequencyHz;
        public double CondenserPressureBar = 1.0;
        public double HotwellInventoryFrac = Physics.HotwellNominalFrac;
        public int BopTripMask;
        public double[] BopTripMeasured = new double[5];
        public double BopOutOfSyncSeconds;

        // Operator alarms (appended)
        public int AlarmAckSeq;

        public string ToJson()
        {
            var sb = new StringBuilder(1024);
            var ic = CultureInfo.InvariantCulture;
            sb.Append("{");
            sb.AppendFormat(ic, "\"simSeconds\":{0:G17},\"gameMinutes\":{1:G17},", SimSeconds, GameMinutes);
            sb.AppendFormat(ic, "\"mode\":\"{0}\",\"scrammed\":{1},", Mode, Scrammed ? "true" : "false");
            sb.AppendFormat(ic, "\"assistTier\":{0},\"activeMissionIndex\":{1},", AssistTier, ActiveMissionIndex);

            sb.AppendFormat(ic, "\"power\":{0:G17},\"decayHeatPower\":{1:G17},\"totalHeatPower\":{2:G17},\"periodSeconds\":{3:G17},",
                Power, DecayHeatPower, TotalHeatPower, Physics.Sanitize(PeriodSeconds, 99999.0));
            sb.AppendFormat(ic, "\"iodine135\":{0:G17},\"xenon135\":{1:G17},", Iodine135, Xenon135);
            sb.Append("\"precursors\":"); AppendArray(sb, Precursors); sb.Append(",");
            sb.Append("\"decayStore\":"); AppendArray(sb, DecayStore); sb.Append(",");

            sb.AppendFormat(ic, "\"baseReactivityPcm\":{0:G17},\"rodReactivityPcm\":{1:G17},\"boronReactivityPcm\":{2:G17},",
                BaseReactivityPcm, RodReactivityPcm, BoronReactivityPcm);
            sb.AppendFormat(ic, "\"dopplerPcm\":{0:G17},\"moderatorPcm\":{1:G17},\"xenonPcm\":{2:G17},\"iodinePcm\":{3:G17},",
                DopplerPcm, ModeratorPcm, XenonPcm, IodinePcm);
            sb.AppendFormat(ic, "\"totalReactivityPcm\":{0:G17},\"isCritical\":{1},",
                TotalReactivityPcm, IsCritical ? "true" : "false");

            sb.Append("\"bankInsertion\":"); AppendArray(sb, BankInsertion); sb.Append(",");
            sb.Append("\"bankTarget\":"); AppendArray(sb, BankTarget); sb.Append(",");
            sb.Append("\"bankIntegrity\":"); AppendArray(sb, BankIntegrity); sb.Append(",");
            sb.Append("\"bankBurnup\":"); AppendArray(sb, BankBurnup); sb.Append(",");
            sb.Append("\"bankSpeed\":"); AppendArray(sb, BankSpeed); sb.Append(",");

            sb.AppendFormat(ic, "\"boronPpm\":{0:G17},\"ph\":{1:G17},\"borateRate\":{2:G17},\"diluteRate\":{3:G17},\"naohRate\":{4:G17},\"ionExchangeRate\":{5:G17},",
                BoronPpm, Ph, BorateRate, DiluteRate, NaohRate, IonExchangeRate);

            sb.AppendFormat(ic, "\"fuelTempC\":{0:G17},\"primaryTempC\":{1:G17},\"primaryPressureBar\":{2:G17},\"primarySatMarginC\":{3:G17},",
                FuelTempC, PrimaryTempC, PrimaryPressureBar, PrimarySatMarginC);
            sb.AppendFormat(ic, "\"primaryLevelFrac\":{0:G17},\"primaryFlowFrac\":{1:G17},\"steamLevelFrac\":{2:G17},",
                PrimaryLevelFrac, PrimaryFlowFrac, SteamLevelFrac);
            sb.AppendFormat(ic, "\"secondaryPressureBar\":{0:G17},\"secondaryTempC\":{1:G17},\"steamDemandFrac\":{2:G17},",
                SecondaryPressureBar, SecondaryTempC, SteamDemandFrac);
            sb.AppendFormat(ic, "\"secondaryLevelFrac\":{0:G17},\"feedwaterFlowFrac\":{1:G17},\"secondaryReliefOpen\":{2},",
                SecondaryLevelFrac, FeedwaterFlowFrac, SecondaryReliefOpen ? "true" : "false");
            sb.AppendFormat(ic, "\"heaterPowerFrac\":{0:G17},\"sprayFrac\":{1:G17},\"reliefOpen\":{2},",
                HeaterPowerFrac, SprayFrac, ReliefOpen ? "true" : "false");

            sb.AppendFormat(ic, "\"steamDumpFrac\":{0:G17},\"turbineSteamKW\":{1:G17},\"feedwaterAvailabilityFrac\":{2:G17},",
                SteamDumpFrac, TurbineSteamKW, FeedwaterAvailabilityFrac);
            sb.AppendFormat(ic, "\"bopAutoLoad\":{0},\"gridDemandFrac\":{1:G17},\"vacuumPumpFrac\":{2:G17},\"coolingWaterFrac\":{3:G17},",
                BopAutoLoad ? "true" : "false", GridDemandFrac, VacuumPumpFrac, CoolingWaterFrac);
            sb.AppendFormat(ic, "\"turbineRpm\":{0:G17},\"generatorSynced\":{1},\"turbineTripped\":{2},",
                TurbineRpm, GeneratorSynced ? "true" : "false", TurbineTripped ? "true" : "false");
            sb.AppendFormat(ic, "\"turbineMechanicalKw\":{0:G17},\"generatorKw\":{1:G17},\"gridOutputFrac\":{2:G17},\"gridFrequencyHz\":{3:G17},",
                TurbineMechanicalKw, GeneratorKw, GridOutputFrac, GridFrequencyHz);
            sb.AppendFormat(ic, "\"condenserPressureBar\":{0:G17},\"hotwellInventoryFrac\":{1:G17},\"bopTripMask\":{2},\"bopOutOfSyncSeconds\":{3:G17},",
                CondenserPressureBar, HotwellInventoryFrac, BopTripMask, BopOutOfSyncSeconds);
            sb.Append("\"bopTripMeasured\":"); AppendArray(sb, BopTripMeasured);
            sb.AppendFormat(ic, ",\"alarmAckSeq\":{0}", AlarmAckSeq);
            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendArray(StringBuilder sb, double[] arr)
        {
            sb.Append("[");
            if (arr != null)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(arr[i].ToString("G17", CultureInfo.InvariantCulture));
                }
            }
            sb.Append("]");
        }

        public static PlantSnapshot FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var s = new PlantSnapshot();
            s.SimSeconds = PlantIntent.ExtractJsonDouble(json, "simSeconds", 0.0);
            s.GameMinutes = PlantIntent.ExtractJsonDouble(json, "gameMinutes", s.SimSeconds / 60.0);
            s.Mode = PlantIntent.ExtractJsonString(json, "mode") ?? "Shutdown";
            s.Scrammed = PlantIntent.ExtractJsonBool(json, "scrammed", false);
            s.AssistTier = (int)PlantIntent.ExtractJsonDouble(json, "assistTier", 1);
            s.ActiveMissionIndex = (int)PlantIntent.ExtractJsonDouble(json, "activeMissionIndex", 0);

            s.Power = PlantIntent.ExtractJsonDouble(json, "power", 1e-5);
            s.DecayHeatPower = PlantIntent.ExtractJsonDouble(json, "decayHeatPower", 0.0);
            s.TotalHeatPower = PlantIntent.ExtractJsonDouble(json, "totalHeatPower", s.Power + s.DecayHeatPower);
            s.PeriodSeconds = PlantIntent.ExtractJsonDouble(json, "periodSeconds", 99999.0);
            s.Iodine135 = PlantIntent.ExtractJsonDouble(json, "iodine135", 0.0);
            s.Xenon135 = PlantIntent.ExtractJsonDouble(json, "xenon135", 0.0);
            s.Precursors = PlantIntent.ExtractJsonDoubleArray(json, "precursors") ?? new double[6];
            s.DecayStore = PlantIntent.ExtractJsonDoubleArray(json, "decayStore") ?? new double[3];

            s.BaseReactivityPcm = PlantIntent.ExtractJsonDouble(json, "baseReactivityPcm", Physics.BaseExcessReactivityPcm);
            s.RodReactivityPcm = PlantIntent.ExtractJsonDouble(json, "rodReactivityPcm", -8000.0);
            s.BoronReactivityPcm = PlantIntent.ExtractJsonDouble(json, "boronReactivityPcm", -2250.0);
            s.DopplerPcm = PlantIntent.ExtractJsonDouble(json, "dopplerPcm", 0.0);
            s.ModeratorPcm = PlantIntent.ExtractJsonDouble(json, "moderatorPcm", 0.0);
            s.XenonPcm = PlantIntent.ExtractJsonDouble(json, "xenonPcm", 0.0);
            s.IodinePcm = PlantIntent.ExtractJsonDouble(json, "iodinePcm", 0.0);
            s.TotalReactivityPcm = PlantIntent.ExtractJsonDouble(json, "totalReactivityPcm", -5000.0);
            s.IsCritical = PlantIntent.ExtractJsonBool(json, "isCritical", false);

            s.BankInsertion = PlantIntent.ExtractJsonDoubleArray(json, "bankInsertion") ?? new double[] { 1, 1, 1, 1 };
            s.BankTarget = PlantIntent.ExtractJsonDoubleArray(json, "bankTarget") ?? new double[] { 1, 1, 1, 1 };
            s.BankIntegrity = PlantIntent.ExtractJsonDoubleArray(json, "bankIntegrity") ?? new double[] { 1, 1, 1, 1 };
            s.BankBurnup = PlantIntent.ExtractJsonDoubleArray(json, "bankBurnup") ?? new double[4];
            s.BankSpeed = PlantIntent.ExtractJsonDoubleArray(json, "bankSpeed") ?? new double[] { Physics.RodDriveSpeedPerSec, Physics.RodDriveSpeedPerSec, Physics.RodDriveSpeedPerSec, Physics.RodDriveSpeedPerSec };

            s.BoronPpm = PlantIntent.ExtractJsonDouble(json, "boronPpm", 1500.0);
            s.Ph = PlantIntent.ExtractJsonDouble(json, "ph", Physics.CoolantPhOperative);
            s.BorateRate = PlantIntent.ExtractJsonDouble(json, "borateRate", 0.0);
            s.DiluteRate = PlantIntent.ExtractJsonDouble(json, "diluteRate", 0.0);
            s.NaohRate = PlantIntent.ExtractJsonDouble(json, "naohRate", 0.0);
            s.IonExchangeRate = PlantIntent.ExtractJsonDouble(json, "ionExchangeRate", 0.0);

            s.FuelTempC = PlantIntent.ExtractJsonDouble(json, "fuelTempC", 60.0);
            s.PrimaryTempC = PlantIntent.ExtractJsonDouble(json, "primaryTempC", 60.0);
            s.PrimaryPressureBar = PlantIntent.ExtractJsonDouble(json, "primaryPressureBar", 1.0);
            s.PrimarySatMarginC = PlantIntent.ExtractJsonDouble(json, "primarySatMarginC", 40.0);
            s.PrimaryLevelFrac = PlantIntent.ExtractJsonDouble(json, "primaryLevelFrac", 1.0);
            s.PrimaryFlowFrac = PlantIntent.ExtractJsonDouble(json, "primaryFlowFrac", 0.0);
            s.SteamLevelFrac = PlantIntent.ExtractJsonDouble(json, "steamLevelFrac", 0.0);
            s.SecondaryPressureBar = PlantIntent.ExtractJsonDouble(json, "secondaryPressureBar", 1.0);
            s.SecondaryTempC = PlantIntent.ExtractJsonDouble(json, "secondaryTempC", 100.0);
            s.SteamDemandFrac = PlantIntent.ExtractJsonDouble(json, "steamDemandFrac", 0.0);
            s.SecondaryLevelFrac = PlantIntent.ExtractJsonDouble(json, "secondaryLevelFrac", 0.6);
            s.FeedwaterFlowFrac = PlantIntent.ExtractJsonDouble(json, "feedwaterFlowFrac", 1.0);
            s.SecondaryReliefOpen = PlantIntent.ExtractJsonBool(json, "secondaryReliefOpen", false);
            s.HeaterPowerFrac = PlantIntent.ExtractJsonDouble(json, "heaterPowerFrac", 0.25);
            s.SprayFrac = PlantIntent.ExtractJsonDouble(json, "sprayFrac", 0.0);
            s.ReliefOpen = PlantIntent.ExtractJsonBool(json, "reliefOpen", false);

            s.SteamDumpFrac = PlantIntent.ExtractJsonDouble(json, "steamDumpFrac", 0.0);
            s.TurbineSteamKW = PlantIntent.ExtractJsonDouble(json, "turbineSteamKW", 0.0);
            s.FeedwaterAvailabilityFrac = PlantIntent.ExtractJsonDouble(json, "feedwaterAvailabilityFrac", 1.0);
            s.BopAutoLoad = PlantIntent.ExtractJsonBool(json, "bopAutoLoad", true);
            s.GridDemandFrac = PlantIntent.ExtractJsonDouble(json, "gridDemandFrac", 0.0);
            s.VacuumPumpFrac = PlantIntent.ExtractJsonDouble(json, "vacuumPumpFrac", 1.0);
            s.CoolingWaterFrac = PlantIntent.ExtractJsonDouble(json, "coolingWaterFrac", 1.0);
            s.TurbineRpm = PlantIntent.ExtractJsonDouble(json, "turbineRpm", 0.0);
            s.GeneratorSynced = PlantIntent.ExtractJsonBool(json, "generatorSynced", false);
            s.TurbineTripped = PlantIntent.ExtractJsonBool(json, "turbineTripped", false);
            s.TurbineMechanicalKw = PlantIntent.ExtractJsonDouble(json, "turbineMechanicalKw", 0.0);
            s.GeneratorKw = PlantIntent.ExtractJsonDouble(json, "generatorKw", 0.0);
            s.GridOutputFrac = PlantIntent.ExtractJsonDouble(json, "gridOutputFrac", 0.0);
            s.GridFrequencyHz = PlantIntent.ExtractJsonDouble(json, "gridFrequencyHz", Physics.GridNominalFrequencyHz);
            s.CondenserPressureBar = PlantIntent.ExtractJsonDouble(json, "condenserPressureBar", 1.0);
            s.HotwellInventoryFrac = PlantIntent.ExtractJsonDouble(json, "hotwellInventoryFrac", Physics.HotwellNominalFrac);
            s.BopTripMask = (int)PlantIntent.ExtractJsonDouble(json, "bopTripMask", 0);
            s.BopTripMeasured = PlantIntent.ExtractJsonDoubleArray(json, "bopTripMeasured") ?? new double[5];
            s.BopOutOfSyncSeconds = PlantIntent.ExtractJsonDouble(json, "bopOutOfSyncSeconds", 0.0);
            s.AlarmAckSeq = (int)PlantIntent.ExtractJsonDouble(json, "alarmAckSeq", 0);

            return s;
        }
    }
}
