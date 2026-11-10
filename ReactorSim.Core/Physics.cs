// ReactorSim.Core — physical constants & correlations.
// Clean-room values from standard PWR references + IAPWS-IF97 saturation fit.
// All units SI unless suffixed (C, Bar, Pcm, Ppm, Hours).

using System;

namespace ReactorSim
{
    public static class Physics
    {
        // ---- Plant ratings ----
        // 160 MWt: a small-modular-reactor-scale plant. Published SMR designs span roughly 100-300 MWt per module
        // (IAEA ARIS "Advances in SMR Technology Developments"); 160 sits in the lower-middle of that range and gives
        // a ~53 MWe unit at the ~33 % net efficiency typical of a PWR steam cycle.
        public const double RatedThermalMWt = 160.0;
        public const double RatedThermalKW = RatedThermalMWt * 1000.0;
        public const double HighFluxTripPower = 1.09;                 // normalised (1.0 = rated), 109 % high-flux setpoint

        // ---- Control rod drive ----
        // Real PWR rod-control systems step at up to ~72 steps/min over a ~228-step stroke, i.e. ~0.53 %/s of travel
        // (typical Westinghouse-type plant practice). The simulator drives at 1 %/s: about twice the real speed, so a
        // full-range withdrawal takes ~100 s of sim time instead of ~190 s and a startup stays playable at the default
        // time scale while remaining slow enough that reactivity insertion rate stays physical.
        public const double RodDriveSpeedPerSec = 0.01;
        // Trip/SCRAM insertion: a full-length drop in ~2 s (plant technical specifications typically require rod
        // drop to the dashpot within ~2.2-2.7 s), expressed as fraction of stroke per second.
        public const double RodScramSpeedPerSec = 0.5;
        // Rod layout: 4 banks x 12 clusters = 48 full-length rod clusters (D and C shutdown, B and A control).
        // 48 is the standard count for a 3-loop, 157-assembly PWR core (53 for a 4-loop, 193-assembly core); it is
        // kept here as a game-scale abstraction. Bank naming follows the common A-D control-bank convention.
        public const int RodsPerBank = 12;

        // ---- Coolant chemistry ----
        // Operative coolant pH: PWR primary water is held near neutral-to-mildly-alkaline at temperature, typically
        // pH(300 C) 6.9-7.4 with a lithium hydroxide trim against the boric acid (EPRI PWR primary water chemistry
        // guidelines); 7.2 is the middle of that band.
        public const double CoolantPhOperative = 7.2;

        // ---- Point-kinetics delayed neutron data (U-235 thermal, Keepin 6-group) ----
        public const double BetaTotal = 0.0065;          // 650 pcm = 1 dollar
        public const double PromptGenTime = 1.0e-4;      // Lambda, seconds (forgiving for game dt)
        public const double PcmPerUnit = 1.0e-5;         // 1 pcm = 1e-5 dk/k

        public static readonly double[] BetaFraction =
        {
            0.033, 0.219, 0.196, 0.395, 0.115, 0.042     // sum = 1.000
        };

        public static readonly double[] Lambda =         // decay constants, 1/s
        {
            0.0124, 0.0305, 0.111, 0.301, 1.14, 3.01
        };

        // ---- Fission product poisons (I-135 -> Xe-135) ----
        // Real half-lives: I-135 = 6.57 h (lambda = 2.93e-5 /s), Xe-135 = 9.14 h (2.11e-5 /s).
        // Real xenon peaks ~9-11 h after a shutdown from equilibrium and clears after ~2 days. A 150x time warp
        // (1 sim-minute = 2.5 real hours) brings the peak to about 3.5 sim-minutes, so one operator shift sees the
        // whole xenon-pit decision (wait vs. restart) in a few minutes of play.
        public const double PoisonTimeWarp = 150.0;      // 150x faster than real hours
        public const double LambdaIodine = 2.93e-5;      // 1/s (unwarped)
        public const double LambdaXenon = 2.11e-5;       // 1/s (unwarped)
        public const double YieldIodine135 = 0.0639;
        public const double YieldXenon135 = 0.00237;
        public const double XenonBurnPerPower = 8.5e-5;  // 1/s at full power (unwarped), ~4x lambda_Xe

        // ---- Reactivity feedback coefficients (negative = stabilizing) ----
        // Doppler power defect is -1800 pcm at 100% power (300C fuel-to-coolant deltaT),
        // dominating time-warped Xenon burnup feedback so steady power is unconditionally self-stabilizing.
        public const double DopplerCoeffPcmPerC = -6.0;       // fuel-to-coolant power Doppler defect
        public const double ModeratorCoeffPcmPerC = -10.0;    // coolant density/temp feedback
        public const double BoronWorthPcmPerPpm = -2.5;       // soluble boron (-3750 pcm at 1500 ppm)
        public const double BaseExcessReactivityPcm = 4500.0; // clean core k_inf excess (~ +4500 pcm)

        // Fraction of fission power deposited promptly; the remainder (6.3 %) arrives later
        // through the decay-heat groups, so TotalHeatPower = Prompt * Power + DecayHeat.
        public const double PromptHeatFraction = 0.937;

        // ---- Water saturation curve: IAPWS-IF97 Region 4 (exact closed form) ----
        // Valid from the triple point (273.15 K, 0.00611657 bar) to the critical point
        // (647.096 K, 220.64 bar); inputs outside are clamped (verification KD-02).
        public const double CriticalTempK = 647.096;
        public const double CriticalPressureBar = 220.64;
        private const double TriplePressureBar = 0.00611657;
        private static readonly double[] If97N =
        {
            0.0, // 1-based to match the IF97 release
            0.11670521452767e4, -0.72421316703206e6, -0.17073846940092e2,
            0.12020824702470e5, -0.32325550322333e7, 0.14915108613530e2,
            -0.48232657361591e4, 0.40511340542057e6, -0.23855557567849,
            0.65017534844798e3,
        };

        public static double SaturationPressureBar(double tempK)
        {
            double t = Clamp(Sanitize(tempK, 333.15), 273.15, CriticalTempK);
            var n = If97N;
            double th = t + n[9] / (t - n[10]);
            double a = th * th + n[1] * th + n[2];
            double b = n[3] * th * th + n[4] * th + n[5];
            double c = n[6] * th * th + n[7] * th + n[8];
            double x = 2.0 * c / (-b + Math.Sqrt(b * b - 4.0 * a * c));
            return x * x * x * x * 10.0; // MPa -> bar
        }

        public static double SaturationTempK(double pressureBar)
        {
            double p = Clamp(Sanitize(pressureBar, 1.0), TriplePressureBar, CriticalPressureBar) / 10.0; // bar -> MPa
            var n = If97N;
            double be = Math.Sqrt(Math.Sqrt(p));
            double e = be * be + n[3] * be + n[6];
            double f = n[1] * be * be + n[4] * be + n[7];
            double g = n[2] * be * be + n[5] * be + n[8];
            double d = 2.0 * g / (-f - Math.Sqrt(f * f - 4.0 * e * g));
            double s = n[10] + d;
            return 0.5 * (s - Math.Sqrt(s * s - 4.0 * (n[9] + n[10] * d)));
        }

        public static int SanitizeHits { get; private set; }

        public static void ResetSanitizeHits()
        {
            SanitizeHits = 0;
        }

        public const double PowerMaxAssert = 5.0;
        public const double FuelTempMaxC = 1600.0;
        public const double PrimaryTempMaxC = 360.0;
        public const double PressureMaxBar = 200.0;
        public const double SecondaryPressureMaxBar = 120.0;
        public const double PoisonMaxNorm = 10.0;
        public const double ReactivityAbsMaxPcm = 50000.0;

        // ---- Balance of plant: lumped steam cycle sized to the 160 MWt / 60 bar plant ----
        // Machine ratings follow common steam-turbine practice (IEC 60045-1 / ISO 3977 style limits): 110 % overspeed
        // trip, 2 % synchronising window. Plant-size values are derived from the 160 MWt rating below.
        // Turbine / generator / grid
        public const double TurbineRatedRpm = 3000.0;            // 2-pole, 50 Hz
        public const double TurbineRadPerSPerRpm = 0.10471975511965977; // 2*pi/60
        public const double TurbineOverspeedTripRpm = 3300.0;    // +10 %
        public const double TurbineUnderspeedTripRpm = 2550.0;   // -15 %, manual mode while synced
        public const double GeneratorSyncRpmBand = 60.0;         // +/- 2 % of rated
        public const double TurbineRotorEnergyKJ = 530000.0;     // 1/2 J w^2 at rated (inertia constant H = 10 s on a 53 MW set)
        public const double TurbineMechEfficiency = 0.33;        // steam heat -> shaft (typical PWR cycle; 160 MWt -> ~53 MW)
        public const double TurbineWindageKwAtRated = 5300.0;    // no-load (windage + friction) loss, ~10 % of rating (game-scale exaggeration)
        public const double TurbineSpeedCeilingRpm = 6000.0;     // integration clamp
        public const double TurbineTorqueFloorRadPerS = 31.4159; // 10 % of rated omega
        public const double GeneratorRatedKw = 53000.0;          // electrical ceiling at demand 1.0 (~33 % of 160 MWt)
        public const double GeneratorAutoMaxFrac = 1.5;          // governor load ceiling (auto mode)
        public const double GridNominalFrequencyHz = 50.0;
        public const double GridDroopHzPerFrac = 2.5;            // Hz swing at full mismatch
        public const double GridToleranceFrac = 0.05;            // in-sync window around demand
        public const double GeneratorSyncLossFrac = 0.5;         // output/demand mismatch that sheds the breaker
        public const double GeneratorSyncLossDwellS = 60.0;      // ... held this long (manual mode)
        // Condenser / vacuum
        public const double CondenserSteamInBarPerS = 0.05;      // rated exhaust, uncondensed
        public const double CondenserCondenseBarPerS = 0.05;     // rated cooling = rated exhaust
        public const double CondenserVacuumBarPerS = 0.02;       // full-pump suction
        public const double CondenserVacuumFloorBar = 0.08;
        public const double CondenserMaxPressureBar = 2.0;
        public const double CondenserEffReferenceBar = 1.0;      // full turbine efficiency at/below
        public const double CondenserEffMinFrac = 0.55;          // efficiency at the pressure ceiling
        public const double CondenserLowVacuumTripBar = 1.6;
        public const double CondenserLowVacuumWarnBar = 1.3;
        // Hotwell / feed / makeup / cooling tower
        public const double HotwellNominalFrac = 0.7;
        public const double HotwellRatePerSec = 0.002;           // inventory change per s at rated flow mismatch
        public const double HotwellFeedStarveFrac = 0.15;        // feed availability falls off below this
        public const double HotwellMakeupMaxFrac = 0.1;          // auto makeup capacity, fraction of rated flow
        public const double CoolingTowerMaxDeltaTC = 12.0;       // water rise at rated load/flow
        // Steam generator protection (master SG: nominal 60 % level, relief at 75 bar)
        public const double SgLowLevelTripFrac = 0.12;

        public static double Clamp(double v, double lo, double hi)
        {
            if (double.IsNaN(v)) return lo;
            return v < lo ? lo : (v > hi ? hi : v);
        }

        public static double Sanitize(double v, double fallback)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                SanitizeHits++;
                return fallback;
            }
            return v;
        }

        public static bool IsFinite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }
    }
}
