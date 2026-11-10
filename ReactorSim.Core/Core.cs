// ReactorSim.Core — reactor core: point kinetics, poisons, decay heat, reactivity ledger.
// Standard six-group point-reactor kinetics equations (PRKE) plus I-135/Xe-135 ODEs
// and a three-group exponential decay-heat model.

using System;

namespace ReactorSim
{
    public class ReactorCore
    {
        // Neutron population, normalized: 1.0 = rated power.
        public double Power { get; private set; } = 1.0e-5; // source level

        // Reactor period T = P / (dP/dt) in seconds (positive = rising, negative = falling).
        public double PeriodSeconds { get; private set; } = double.PositiveInfinity;

        // Delayed precursor concentrations (scaled so lambda_i * C_i = beta_i * P / PromptGenTime at steady state).
        private readonly double[] _precursors = new double[6];

        // Poison concentrations, normalized units (equilibrium at full power = 1.0).
        public double Iodine135 { get; private set; }
        public double Xenon135 { get; private set; }

        // Decay heat: 3 lumped groups, fraction of rated power (~6.3% total at steady 100%).
        private readonly double[] _decayGroups = { 0.031, 0.022, 0.010 };
        private readonly double[] _decayLambda = { 0.08, 0.012, 0.0018 }; // 1/game-s
        private readonly double[] _decayStore = new double[3];

        // Fuel thermal state is owned by ThermalHydraulics; core reports normalized thermal power (1.0 = rated thermal power, Physics.RatedThermalMWt).
        public double DecayHeatPower { get; private set; }
        public double TotalHeatPower { get { return Physics.PromptHeatFraction * Power + DecayHeatPower; } }

        // Neutron source strength (power units x prompt time): subcritical equilibrium is
        // P = SourcePower / -rho.
        public const double SourcePower = 1.0e-5 * Physics.BetaTotal;

        // Read-only views of hidden state so verification can hash/compare it.
        public int PrecursorGroupCount { get { return _precursors.Length; } }
        public double GetPrecursor(int group) { return _precursors[group]; }
        public int DecayGroupCount { get { return _decayStore.Length; } }
        public double GetDecayStore(int group) { return _decayStore[group]; }

        // Reactivity ledger (pcm = 1e-5 dk/k). Inputs set by Plant each tick.
        public double BaseReactivityPcm { get; set; } = Physics.BaseExcessReactivityPcm;
        public double RodReactivityPcm { get; set; }
        public double BoronReactivityPcm { get; set; }
        public double DopplerPcm { get; set; }
        public double ModeratorPcm { get; set; }
        public double XenonPcm { get; private set; }
        public double IodinePcm { get; private set; }

        public double TotalReactivityPcm
        {
            get
            {
                return BaseReactivityPcm + RodReactivityPcm + BoronReactivityPcm + DopplerPcm
                     + ModeratorPcm + XenonPcm + IodinePcm;
            }
        }

        public double TotalReactivityDk { get { return TotalReactivityPcm * Physics.PcmPerUnit; } }

        public bool IsCritical { get { return TotalReactivityPcm >= -50.0 && Power > 1.0e-3; } }

        // Equilibrium poisoning worth at rated power (-2400 pcm Xe, -200 pcm I-135).
        // Post-SCRAM Xenon peak (~2.02x eq = -4848 pcm + Iodine -80 pcm = -4928 pcm) exceeds BaseExcessReactivityPcm (+4500 pcm).
        public const double XeWorthAtEq = -2400.0;
        public const double IWorthAtEq = -200.0;

        public ReactorCore()
        {
            Init();
        }

        public void Init()
        {
            Power = 1.0e-5;
            PeriodSeconds = 1.0e6;
            for (int g = 0; g < 6; g++)
            {
                double betaI = Physics.BetaTotal * Physics.BetaFraction[g];
                _precursors[g] = (betaI * Power) / (Physics.PromptGenTime * Physics.Lambda[g]);
            }
            for (int g = 0; g < 3; g++)
                _decayStore[g] = 0.0;
            DecayHeatPower = 0.0;
            Iodine135 = 0.0;
            Xenon135 = 0.0;
            XenonPcm = 0.0;
            IodinePcm = 0.0;
        }

        // Advance neutronics by dt (seconds of SIM time).
        // Coupled analytic prompt-jump + precursor integration per substep h.
        public void StepKinetics(double dt)
        {
            double pStart = Power;
            double rho = TotalReactivityDk;
            double betaEff = Physics.BetaTotal;
            double a = (rho - betaEff) / Physics.PromptGenTime;
            // Source neutron floor so subcritical startup multiplies measurably.
            const double sourceTerm = SourcePower / Physics.PromptGenTime;

            int sub = Math.Max(1, (int)Math.Ceiling(dt / 0.02));
            double h = dt / sub;
            for (int i = 0; i < sub; i++)
            {
                double q = sourceTerm;
                for (int g = 0; g < 6; g++)
                    q += Physics.Lambda[g] * _precursors[g];

                if (Math.Abs(a) < 1e-9)
                {
                    Power += q * h;
                }
                else
                {
                    double ah = Physics.Clamp(a * h, -50.0, 20.0);
                    double ea = Math.Exp(ah);
                    const double limit = 3.0; // sanity ceiling: 300% power
                    Power = Math.Min(Power * ea + (q / a) * (ea - 1.0), limit);
                }

                if (!Physics.IsFinite(Power) || Power < 1.0e-6)
                    Power = 1.0e-6;

                // Coupled precursor update inside substep h using exact exponential decay.
                for (int g = 0; g < 6; g++)
                {
                    double lam = Physics.Lambda[g];
                    double betaI = betaEff * Physics.BetaFraction[g];
                    double eqC = (betaI * Power) / (Physics.PromptGenTime * lam);
                    double expL = Math.Exp(-lam * h);
                    _precursors[g] = _precursors[g] * expL + eqC * (1.0 - expL);
                    if (!Physics.IsFinite(_precursors[g]) || _precursors[g] < 0)
                        _precursors[g] = 0;
                }
            }

            double dLogP = Math.Log(Math.Max(1e-9, Power) / Math.Max(1e-9, pStart)) / Math.Max(1e-4, dt);
            PeriodSeconds = Math.Abs(dLogP) < 1e-6 ? 99999.0 : Physics.Clamp(1.0 / dLogP, -99999.0, 99999.0);
        }

        // Advance I-135 and Xe-135 poison concentrations.
        // Normalized so that at steady P=1.0: Iodine135 = 1.0 and Xenon135 = 1.0.
        public void StepPoisons(double dt, double moderatorDensityFrac, double ionExchangeStripRate = 0.0)
        {
            double w = Physics.PoisonTimeWarp;
            double p = Physics.Clamp(Power, 0.0, 3.0);
            double strip = Physics.Clamp(ionExchangeStripRate, 0.0, 1.0) * 0.002;

            double lI = Physics.LambdaIodine * w + strip;
            double lXe = Physics.LambdaXenon * w;
            double burnAtFull = Physics.XenonBurnPerPower * w;
            double burn = burnAtFull * p;

            // Iodine-135: dI/dt = lI_base * P - lI * I (so I_eq = 1.0 at P=1, strip=0)
            double prodI = Physics.LambdaIodine * w * p;
            double expI = Math.Exp(-lI * dt);
            Iodine135 = Iodine135 * expI + (prodI / Math.Max(1e-9, lI)) * (1.0 - expI);
            Iodine135 = Physics.Clamp(Physics.Sanitize(Iodine135, 0.0), 0.0, 5.0);

            // Xenon-135: at P=1, total removal rate is rEq = lXe + burnAtFull.
            // Yield split: fXe (~3.6%) from direct fission, fI (~96.4%) from I-135 decay.
            double totalYield = Physics.YieldIodine135 + Physics.YieldXenon135;
            double fI = Physics.YieldIodine135 / totalYield;
            double fXe = Physics.YieldXenon135 / totalYield;
            double rEq = lXe + burnAtFull;

            double sourceXe = (fXe * p + fI * Iodine135) * rEq;
            double lossXe = lXe + burn;
            double expXe = Math.Exp(-lossXe * dt);
            Xenon135 = Xenon135 * expXe + (sourceXe / Math.Max(1e-9, lossXe)) * (1.0 - expXe);
            Xenon135 = Physics.Clamp(Physics.Sanitize(Xenon135, 0.0), 0.0, 5.0);

            // Worth scales with moderation: dead/voided fluid doesn't moderate thermal absorption.
            double mod = Physics.Clamp(moderatorDensityFrac, 0.0, 1.0);
            XenonPcm = XeWorthAtEq * Xenon135 * mod;
            IodinePcm = IWorthAtEq * Iodine135 * mod;
        }

        public void StepDecayHeat(double dt)
        {
            double dh = 0;
            for (int g = 0; g < 3; g++)
            {
                double lam = _decayLambda[g];
                double eqStore = _decayGroups[g] * Power;
                double expD = Math.Exp(-lam * dt);
                _decayStore[g] = _decayStore[g] * expD + eqStore * (1.0 - expD);
                if (!Physics.IsFinite(_decayStore[g]) || _decayStore[g] < 0)
                    _decayStore[g] = 0;
                dh += _decayStore[g];
            }
            DecayHeatPower = dh;
        }

        // SCRAM physics: prompt neutron population collapses; decay heat persists on _decayStore.
        public void Scram()
        {
            Power = Math.Min(Power, 1.0e-4);
            for (int g = 0; g < 6; g++)
                _precursors[g] *= 0.05;
        }

        // State snapshot helpers for serialization & preset scenarios.
        public double[] CopyPrecursors()
        {
            var arr = new double[6];
            Array.Copy(_precursors, arr, 6);
            return arr;
        }

        public double[] CopyDecayStore()
        {
            var arr = new double[3];
            Array.Copy(_decayStore, arr, 3);
            return arr;
        }

        public void RestoreState(
            double power,
            double iodine135,
            double xenon135,
            double decayHeatPower,
            double[] precursors,
            double[] decayStore,
            double moderatorDensityFrac = 1.0)
        {
            Power = Physics.Clamp(Physics.Sanitize(power, 1e-5), 1e-6, 3.0);
            Iodine135 = Physics.Clamp(Physics.Sanitize(iodine135, 0.0), 0.0, 5.0);
            Xenon135 = Physics.Clamp(Physics.Sanitize(xenon135, 0.0), 0.0, 5.0);
            DecayHeatPower = Physics.Clamp(Physics.Sanitize(decayHeatPower, 0.0), 0.0, 1.0);

            if (precursors != null && precursors.Length == 6)
            {
                for (int i = 0; i < 6; i++)
                    _precursors[i] = Math.Max(0.0, Physics.Sanitize(precursors[i], 0.0));
            }
            else
            {
                for (int g = 0; g < 6; g++)
                {
                    double betaI = Physics.BetaTotal * Physics.BetaFraction[g];
                    _precursors[g] = (betaI * Power) / (Physics.PromptGenTime * Physics.Lambda[g]);
                }
            }

            if (decayStore != null && decayStore.Length == 3)
            {
                double sum = 0;
                for (int i = 0; i < 3; i++)
                {
                    _decayStore[i] = Math.Max(0.0, Physics.Sanitize(decayStore[i], 0.0));
                    sum += _decayStore[i];
                }
                DecayHeatPower = sum;
            }

            double mod = Physics.Clamp(moderatorDensityFrac, 0.0, 1.0);
            XenonPcm = XeWorthAtEq * Xenon135 * mod;
            IodinePcm = IWorthAtEq * Iodine135 * mod;
        }
    }
}
