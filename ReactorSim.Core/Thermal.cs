// ReactorSim.Core — lumped-parameter thermal-hydraulics.
// Nodes: Fuel -> PrimaryCoolant -> SteamGenerator(secondary) -> condenser sink.
// Pressurizer holds primary pressure at/above the saturation curve via heaters/spray.

using System;

namespace ReactorSim
{
    public class ThermalHydraulics
    {
        // ---- Primary side ----
        public double FuelTempC { get; private set; } = 60.0;
        public double PrimaryTempC { get; private set; } = 60.0;
        public double PrimaryPressureBar { get; private set; } = 1.0;
        public double PrimaryLevelFrac { get; set; } = 1.0;  // vessel fill
        public double PrimaryFlowFrac { get; set; } = 0.0;   // circulation pumps

        // Nominal heat capacities (relative lumps) and conductances.
        // Conductances are sized from the rated power so the full-power temperature differences hold at any rating:
        // at 100% power deltaT_fuel-pri = 300C and deltaT_pri-sg = 30C.
        // Fast pellet thermal time constant (~8s) ensures prompt negative Doppler stabilization.
        public double UaFuelToPrimary = Physics.RatedThermalKW / 300.0;  // kW/K (deltaT = 300C at rated power)
        public double UaPrimaryToSg = Physics.RatedThermalKW / 30.0;     // kW/K (deltaT = 30C at rated power)
        public double FuelMassCp = 8.0 * Physics.RatedThermalKW / 300.0; // kJ/K lump (tau_fuel = 8s)
        public double PrimaryMassCp = Physics.RatedThermalKW;            // kJ/K lump (~1000 s thermal inertia at rated power)
        public double AmbientLossKWK = 25.0;     // to containment ambient 25C

        // ---- Steam generator / secondary ----
        public double SecondaryPressureBar { get; private set; } = 1.0;
        public double SteamDemandFrac { get; set; } = 0.0;    // turbine throttle valve (0..1)
        public double SecondaryLevelFrac { get; set; } = 0.6; // nominal 60% SG water inventory
        public double FeedwaterFlowFrac { get; set; } = 1.0;  // automatic makeup when 1.0
        public double SecondaryMassCp = 40000.0;              // kJ/bar pressure inertia
        public double RatedThermalKW = Physics.RatedThermalKW;

        public bool SecondaryReliefOpen { get; private set; }

        // ---- Balance-of-plant coupling (BalanceOfPlant writes the two inputs) ----
        public double SteamDumpFrac { get; set; }                     // condenser bypass valve (0..1), operator
        public bool TurbineStopValveOpen { get; set; } = true;        // closed by a turbine trip
        public double FeedwaterAvailabilityFrac { get; set; } = 1.0;  // hotwell supply to the feed pump (0..1)
        // Per-step outputs (kW of heat carried; feed as a fraction of rated boil-off).
        public double TurbineSteamKW { get; private set; }
        public double DumpSteamKW { get; private set; }
        public double ReliefSteamKW { get; private set; }
        public double FeedwaterDeliveredFrac { get; private set; }
        public const double RatedBoilOffPerSec = 0.0005;              // SG level fraction per s at rated steam

        public double SecondaryTempC
        {
            get { return Physics.SaturationTempK(SecondaryPressureBar) - 273.15; }
        }

        // ---- Pressurizer ----
        public double HeaterPowerFrac { get; set; } = 0.25; // default bias keeps subcooling margin
        public double SprayFrac { get; set; }
        public bool ReliefOpen { get; set; }
        public double PressurizerHeaterKWK = 80.0;
        public double PressurizerSprayKWK = 40.0;
        public double ReliefRateBarPerS = 2.0;

        // Derived boiling margin: how close primary is to flashing (positive = subcooled).
        public double PrimarySatMarginC
        {
            get { return Physics.SaturationTempK(PrimaryPressureBar) - 273.15 - PrimaryTempC; }
        }

        public double SteamLevelFrac { get; private set; } // voiding in vessel head

        public void Step(double dt, double coreHeatKW)
        {
            // 1) Fuel -> primary: conductance degrades with voiding (steam blanket).
            double voidPenalty = 1.0 - 0.7 * SteamLevelFrac * (1.0 - PrimaryLevelFrac);
            voidPenalty = Physics.Clamp(voidPenalty, 0.1, 1.0);
            double uFuel = UaFuelToPrimary * voidPenalty;
            // Exact exponential relaxation toward equilibrium fuel temperature for stability at any dt.
            double eqFuelTemp = PrimaryTempC + coreHeatKW / Math.Max(1.0, uFuel);
            double expF = Math.Exp(-(uFuel / FuelMassCp) * dt);
            double fuelTemp0 = FuelTempC;
            FuelTempC = FuelTempC * expF + eqFuelTemp * (1.0 - expF);
            // Heat the primary actually received this step = core heat minus what the fuel
            // stored (energy-conserving; the end-of-step flux * dt over-credits by up to ~25 %
            // when the fuel is still heating). dt == 0 leaves the primary untouched.
            double qFuel = dt > 0.0 ? coreHeatKW - FuelMassCp * (FuelTempC - fuelTemp0) / dt : 0.0;

            // 2) Primary -> SG: effectiveness scales with pump flow and SG tube coverage.
            double sgTubeCoverage = Physics.Clamp(SecondaryLevelFrac / 0.25, 0.1, 1.0);
            double sgEff = (0.15 + 0.85 * Physics.Clamp(PrimaryFlowFrac, 0, 1)) * sgTubeCoverage;
            double qSg = UaPrimaryToSg * sgEff * (PrimaryTempC - SecondaryTempC);
            if (qSg < 0) qSg = 0; // no reverse heat transfer modeled

            double qAmb = AmbientLossKWK * Math.Max(0, PrimaryTempC - 25.0);
            PrimaryTempC += (qFuel - qSg - qAmb) * dt / PrimaryMassCp;

            // Hard physical bounds.
            FuelTempC = Physics.Clamp(Physics.Sanitize(FuelTempC, 60.0), 20.0, 2200.0);
            PrimaryTempC = Physics.Clamp(Physics.Sanitize(PrimaryTempC, 60.0), 20.0, 365.0);
            PrimaryPressureBar = Physics.Clamp(Physics.Sanitize(PrimaryPressureBar, 1.0), 1.0, 200.0);
            SecondaryPressureBar = Physics.Clamp(Physics.Sanitize(SecondaryPressureBar, 1.0), 1.0, 90.0);

            // 3) Secondary: pressure rises with heat in, falls with steam draw.
            // Turbine throttle draws rated heat when SecondaryPressureBar is at nominal 60 bar.
            double steamPressureFactor = Physics.Clamp((SecondaryPressureBar - 1.0) / 59.0, 0.0, 1.5);
            double turbineValve = TurbineStopValveOpen ? SteamDemandFrac : 0.0;
            double dumpValve = Physics.Clamp(SteamDumpFrac, 0.0, 1.0);
            double steamScale = RatedThermalKW * steamPressureFactor * sgTubeCoverage;
            TurbineSteamKW = turbineValve * steamScale;
            DumpSteamKW = dumpValve * steamScale;
            double steamHeatKW = TurbineSteamKW + DumpSteamKW;

            // Secondary safety relief valves pop above 75 bar.
            double reliefKW = 0.0;
            if (SecondaryPressureBar > 75.0)
            {
                SecondaryReliefOpen = true;
                reliefKW = (SecondaryPressureBar - 75.0) * 20000.0;  // vented to atmosphere (inventory lost)
            }
            else
            {
                SecondaryReliefOpen = false;
            }

            double netSgKW = qSg - steamHeatKW - reliefKW;
            SecondaryPressureBar += netSgKW * dt / SecondaryMassCp;
            // Ambient cooldown of SG when idle
            if (SecondaryPressureBar > 1.0 && qSg < 500.0)
                SecondaryPressureBar -= 0.02 * (SecondaryPressureBar - 1.0) * dt;
            SecondaryPressureBar = Physics.Clamp(Physics.Sanitize(SecondaryPressureBar, 1.0), 1.0, 90.0);

            // Secondary water inventory: steam boils off inventory; feedwater replenishes toward 0.60.
            ReliefSteamKW = reliefKW;
            double boilOffRate = ((steamHeatKW + reliefKW) / RatedThermalKW) * RatedBoilOffPerSec;
            double feedRate = Physics.Clamp(FeedwaterFlowFrac, 0.0, 2.0)
                            * Physics.Clamp(FeedwaterAvailabilityFrac, 0.0, 1.0)
                            * Math.Max(0.0, (0.60 - SecondaryLevelFrac) * 0.01 + boilOffRate);
            FeedwaterDeliveredFrac = feedRate / RatedBoilOffPerSec;
            SecondaryLevelFrac = Physics.Clamp(
                SecondaryLevelFrac + (feedRate - boilOffRate) * dt, 0.05, 1.0);

            // 4) Primary pressure follows saturation curve at primary temp,
            //    plus pressurizer heater subcooling overpressure (+) and spray (-).
            double pSat = Physics.SaturationPressureBar(PrimaryTempC + 273.15);
            double overpressure = HeaterPowerFrac * PressurizerHeaterKWK
                                - SprayFrac * PressurizerSprayKWK;
            double target = Physics.Clamp(pSat + overpressure, 1.0, 200.0);
            double rate = (ReliefOpen ? ReliefRateBarPerS * 5.0 : 3.5); // bar/s slew
            double dP = Physics.Clamp(target - PrimaryPressureBar, -rate * dt, rate * dt);
            PrimaryPressureBar += dP;
            if (ReliefOpen)
                PrimaryPressureBar = Math.Max(1.0, PrimaryPressureBar - ReliefRateBarPerS * dt);
            PrimaryPressureBar = Physics.Clamp(Physics.Sanitize(PrimaryPressureBar, 1.0), 1.0, 200.0);

            // 5) Vessel voiding: when pressure can no longer keep primary liquid.
            double flashing = Math.Max(0, PrimaryTempC - (Physics.SaturationTempK(PrimaryPressureBar) - 273.15));
            double targetVoid = Physics.Clamp(flashing / 20.0 + (1.0 - PrimaryLevelFrac), 0, 1);
            SteamLevelFrac += (targetVoid - SteamLevelFrac) * Math.Min(1.0, dt * 0.5);
            SteamLevelFrac = Physics.Clamp(Physics.Sanitize(SteamLevelFrac, 0.0), 0.0, 1.0);
        }

        public void RestoreState(
            double fuelTempC,
            double primaryTempC,
            double primaryPressureBar,
            double primaryLevelFrac,
            double primaryFlowFrac,
            double secondaryPressureBar,
            double steamDemandFrac,
            double secondaryLevelFrac,
            double feedwaterFlowFrac,
            double heaterPowerFrac,
            double sprayFrac,
            bool reliefOpen,
            double steamLevelFrac)
        {
            FuelTempC = Physics.Clamp(Physics.Sanitize(fuelTempC, 60.0), 20.0, 2200.0);
            PrimaryTempC = Physics.Clamp(Physics.Sanitize(primaryTempC, 60.0), 20.0, 365.0);
            PrimaryPressureBar = Physics.Clamp(Physics.Sanitize(primaryPressureBar, 1.0), 1.0, 200.0);
            PrimaryLevelFrac = Physics.Clamp(Physics.Sanitize(primaryLevelFrac, 1.0), 0.0, 1.0);
            PrimaryFlowFrac = Physics.Clamp(Physics.Sanitize(primaryFlowFrac, 0.0), 0.0, 1.0);
            SecondaryPressureBar = Physics.Clamp(Physics.Sanitize(secondaryPressureBar, 1.0), 1.0, 90.0);
            SteamDemandFrac = Physics.Clamp(Physics.Sanitize(steamDemandFrac, 0.0), 0.0, 1.5);
            SecondaryLevelFrac = Physics.Clamp(Physics.Sanitize(secondaryLevelFrac, 0.6), 0.05, 1.0);
            FeedwaterFlowFrac = Physics.Clamp(Physics.Sanitize(feedwaterFlowFrac, 1.0), 0.0, 2.0);
            HeaterPowerFrac = Physics.Clamp(Physics.Sanitize(heaterPowerFrac, 0.25), 0.0, 1.0);
            SprayFrac = Physics.Clamp(Physics.Sanitize(sprayFrac, 0.0), 0.0, 1.0);
            ReliefOpen = reliefOpen;
            SteamLevelFrac = Physics.Clamp(Physics.Sanitize(steamLevelFrac, 0.0), 0.0, 1.0);
        }
    }
}
