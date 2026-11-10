// ReactorSim.Core — balance of plant: turbine/generator, grid, condenser + vacuum, hotwell/feed,
// cooling tower and latched protection trips. Runs on top of ThermalHydraulics, which keeps
// owning the steam generator. Coupling, once per plant substep after Thermal.Step:
//   Thermal  -> BOP : TurbineSteamKW, DumpSteamKW, FeedwaterDeliveredFrac, SG level
//   BOP -> Thermal  : TurbineStopValveOpen (closed by a turbine trip),
//                     FeedwaterAvailabilityFrac (hotwell supply to the feed pump)
// Default AutoLoad mode is a load-following governor: the breaker closes in the sync band and
// the generator absorbs the shaft power, so plants driven only by steam demand behave as before.
// Manual mode is the full operator choreography (spin up, sync, set grid demand) with the
// speed and sync trips armed. Everything is deterministic: fixed evaluation order, constants
// in Physics, no randomness.

using System;

namespace ReactorSim
{
    public enum BopTripKind { Overspeed = 0, Underspeed = 1, LowVacuum = 2, SgLowLevel = 3, OutOfSync = 4 }

    public sealed class BopTrip
    {
        public BopTripKind Kind { get; private set; }
        public string Code { get; private set; }
        public bool Active { get; internal set; }
        public double Limit { get; internal set; }
        public double Measured { get; internal set; }

        public BopTrip(BopTripKind kind, string code) { Kind = kind; Code = code; }

        internal void Fire(double limit, double measured) { Active = true; Limit = limit; Measured = measured; }
        internal void Clear() { Active = false; Limit = 0.0; Measured = 0.0; }
    }

    public class TurbineGenerator
    {
        public double Rpm { get; internal set; }
        public bool Synced { get; internal set; }      // breaker closed
        public bool Tripped { get; internal set; }     // stop valve shut, latched
        public double MechanicalKw { get; private set; }
        public double GeneratorKw { get; private set; }
        public double VacuumEfficiencyFrac { get; internal set; } = 1.0;

        public double SpeedFrac { get { return Rpm / Physics.TurbineRatedRpm; } }

        public bool InSyncBand { get { return Math.Abs(Rpm - Physics.TurbineRatedRpm) <= Physics.GeneratorSyncRpmBand; } }

        // steamKw: heat carried by steam admitted to the turbine this step (0 when tripped).
        // demandFrac: grid load setpoint (manual mode).
        public void Step(double dt, double steamKw, double demandFrac, bool autoLoad)
        {
            double speedFrac = SpeedFrac;
            double mech = Tripped ? 0.0 : Math.Max(0.0, steamKw) * Physics.TurbineMechEfficiency * VacuumEfficiencyFrac;
            double windage = Physics.TurbineWindageKwAtRated * speedFrac * Math.Abs(speedFrac);

            double generation = 0.0;
            if (autoLoad)
            {
                // Governor: close in the band, then the stiff bus holds synchronous speed and the
                // generator takes whatever the shaft delivers (up to its ceiling).
                if (!Synced && !Tripped && InSyncBand) Synced = true;
                if (Synced)
                    generation = Physics.Clamp(mech - windage, 0.0, Physics.GeneratorRatedKw * Physics.GeneratorAutoMaxFrac);
            }
            else if (Synced && demandFrac > 0.0)
            {
                double speedCap = Physics.Clamp(speedFrac, 0.0, 1.0);
                generation = Math.Min(demandFrac * Physics.GeneratorRatedKw * speedCap, mech);
            }

            // Power balance on the rotor; every term divides by the same (floored) real speed.
            double omegaRated = Physics.TurbineRatedRpm * Physics.TurbineRadPerSPerRpm;
            double omega = Math.Max(Math.Abs(speedFrac) * omegaRated, Physics.TurbineTorqueFloorRadPerS);
            double inertia = 2.0 * Physics.TurbineRotorEnergyKJ / (omegaRated * omegaRated);
            double dOmega = (mech - generation - windage) / omega * dt / inertia;
            Rpm = Physics.Clamp(Rpm + dOmega / Physics.TurbineRadPerSPerRpm, 0.0, Physics.TurbineSpeedCeilingRpm);

            if (autoLoad && Synced)
            {
                if (generation > 0.0 && mech - windage <= Physics.GeneratorRatedKw * Physics.GeneratorAutoMaxFrac)
                    Rpm = Physics.TurbineRatedRpm;          // generating: locked to the grid
                else if (generation <= 0.0 && Rpm < Physics.TurbineRatedRpm - Physics.GeneratorSyncRpmBand)
                    Synced = false;                         // motoring not modelled: coast off the bus
            }

            MechanicalKw = mech;
            GeneratorKw = generation;
        }

        public bool TrySync()
        {
            if (Tripped || !InSyncBand) return false;
            Synced = true;
            return true;
        }

        public void Trip() { Tripped = true; Synced = false; }
    }

    public class BalanceOfPlant
    {
        // ---- Operator surface ----
        public bool AutoLoad = true;                 // governor mode (default) vs manual choreography
        public double GridDemandFrac;                // manual-mode load setpoint (0..1.5)
        public double VacuumPumpFrac = 1.0;          // condenser air-removal pumps (0..1)
        public double CoolingWaterFrac = 1.0;        // circulating water / tower flow (0..1)

        // ---- Equipment state ----
        public readonly TurbineGenerator Turbine = new TurbineGenerator();
        public double CondenserPressureBar { get; internal set; } = 1.0;
        public double HotwellInventoryFrac { get; internal set; } = Physics.HotwellNominalFrac;
        public double OutOfSyncSeconds { get; internal set; }

        // ---- Derived per step ----
        public double CondenserExhaustFrac { get; private set; }
        public double CondensateFrac { get; private set; }
        public double MakeupFrac { get; private set; }
        public double FeedAvailabilityFrac { get; private set; } = 1.0;
        public double TowerRejectedKw { get; private set; }
        public double TowerWaterDeltaTC { get; private set; }
        public double GridOutputFrac { get; private set; }
        public double GridFrequencyHz { get; private set; } = Physics.GridNominalFrequencyHz;

        public double VacuumEfficiencyFrac
        {
            get
            {
                double t = Physics.Clamp((CondenserPressureBar - Physics.CondenserEffReferenceBar)
                                         / (Physics.CondenserMaxPressureBar - Physics.CondenserEffReferenceBar), 0.0, 1.0);
                return 1.0 - t * (1.0 - Physics.CondenserEffMinFrac);
            }
        }

        public bool GridInTolerance
        {
            get { return Turbine.Synced && Math.Abs(GridOutputFrac - EffectiveDemandFrac) <= Physics.GridToleranceFrac; }
        }

        // In auto mode the dispatcher follows the machine, so demand == output.
        public double EffectiveDemandFrac { get { return AutoLoad ? GridOutputFrac : GridDemandFrac; } }

        // ---- Trips: fixed registry order ----
        public readonly BopTrip[] Trips =
        {
            new BopTrip(BopTripKind.Overspeed, "TURB-OVERSPEED"),
            new BopTrip(BopTripKind.Underspeed, "TURB-UNDERSPEED"),
            new BopTrip(BopTripKind.LowVacuum, "COND-LOW-VACUUM"),
            new BopTrip(BopTripKind.SgLowLevel, "SG-LOW-LEVEL"),
            new BopTrip(BopTripKind.OutOfSync, "GEN-OUT-OF-SYNC"),
        };

        public BopTrip Trip(BopTripKind kind) { return Trips[(int)kind]; }

        public BopTrip FirstActiveTrip
        {
            get { foreach (var t in Trips) if (t.Active) return t; return null; }
        }

        public int TripMask
        {
            get { int m = 0; for (int i = 0; i < Trips.Length; i++) if (Trips[i].Active) m |= 1 << i; return m; }
        }

        // Close (true) or open (false) the generator breaker. Closing is refused while tripped or
        // outside the sync band; opening always succeeds (in auto mode it re-closes in band).
        public bool SetBreaker(bool close)
        {
            if (close) return Turbine.TrySync();
            Turbine.Synced = false;
            OutOfSyncSeconds = 0.0;
            return true;
        }

        // Clear the latch and reopen the stop valve. The breaker stays open: re-sync required.
        public void ResetTrips()
        {
            Turbine.Tripped = false;
            foreach (var t in Trips) t.Clear();
            OutOfSyncSeconds = 0.0;
        }

        // One substep, after ThermalHydraulics.Step; writes the coupling inputs back to Thermal.
        public void Step(double dt, ThermalHydraulics th)
        {
            double rated = th.RatedThermalKW;

            // Condenser: turbine + dump exhaust in, cooling water condenses up to its flow,
            // vacuum pumps pull the shell down. Relief steam vents to atmosphere instead.
            CondenserExhaustFrac = Physics.Clamp((th.TurbineSteamKW + th.DumpSteamKW) / rated, 0.0, 1.5);
            double cooling = Physics.Clamp(CoolingWaterFrac, 0.0, 1.0);
            CondensateFrac = Math.Min(CondenserExhaustFrac, cooling);
            CondenserPressureBar += (CondenserExhaustFrac * Physics.CondenserSteamInBarPerS
                                     - CondensateFrac * Physics.CondenserCondenseBarPerS
                                     - Physics.Clamp(VacuumPumpFrac, 0.0, 1.0) * Physics.CondenserVacuumBarPerS) * dt;
            CondenserPressureBar = Physics.Clamp(CondenserPressureBar, Physics.CondenserVacuumFloorBar, Physics.CondenserMaxPressureBar);
            Turbine.VacuumEfficiencyFrac = VacuumEfficiencyFrac;

            // Hotwell: condensate in, feed pump out, automatic makeup toward nominal.
            MakeupFrac = Physics.Clamp(Physics.HotwellNominalFrac - HotwellInventoryFrac, 0.0, Physics.HotwellMakeupMaxFrac);
            HotwellInventoryFrac += (CondensateFrac + MakeupFrac - th.FeedwaterDeliveredFrac) * Physics.HotwellRatePerSec * dt;
            HotwellInventoryFrac = Physics.Clamp(HotwellInventoryFrac, 0.0, 1.0);
            FeedAvailabilityFrac = Physics.Clamp(HotwellInventoryFrac / Physics.HotwellFeedStarveFrac, 0.0, 1.0);

            // Cooling tower: rejects what condensed; less water for the same heat runs hotter.
            TowerRejectedKw = CondensateFrac * rated;
            TowerWaterDeltaTC = Physics.Clamp(Physics.CoolingTowerMaxDeltaTC * (TowerRejectedKw / rated) / Math.Max(cooling, 0.05), 0.0, 60.0);

            // Turbine + generator + grid.
            Turbine.Step(dt, th.TurbineSteamKW, Physics.Clamp(GridDemandFrac, 0.0, 1.5), AutoLoad);
            GridOutputFrac = Turbine.Synced ? Physics.Clamp(Turbine.GeneratorKw / Physics.GeneratorRatedKw, 0.0, 1.5) : 0.0;
            GridFrequencyHz = Turbine.Synced
                ? Physics.Clamp(Physics.GridNominalFrequencyHz + Physics.GridDroopHzPerFrac * (GridOutputFrac - EffectiveDemandFrac), 40.0, 60.0)
                : Physics.GridNominalFrequencyHz;

            EvaluateTrips(dt, th);

            th.TurbineStopValveOpen = !Turbine.Tripped;
            th.FeedwaterAvailabilityFrac = FeedAvailabilityFrac;
        }

        private void EvaluateTrips(double dt, ThermalHydraulics th)
        {
            var over = Trips[(int)BopTripKind.Overspeed];
            if (!over.Active && Turbine.Rpm >= Physics.TurbineOverspeedTripRpm)
            { over.Fire(Physics.TurbineOverspeedTripRpm, Turbine.Rpm); Turbine.Trip(); }

            var under = Trips[(int)BopTripKind.Underspeed];
            if (!under.Active && !AutoLoad && Turbine.Synced && Turbine.Rpm <= Physics.TurbineUnderspeedTripRpm)
            { under.Fire(Physics.TurbineUnderspeedTripRpm, Turbine.Rpm); Turbine.Trip(); }

            var vac = Trips[(int)BopTripKind.LowVacuum];
            if (!vac.Active && CondenserPressureBar >= Physics.CondenserLowVacuumTripBar)
            { vac.Fire(Physics.CondenserLowVacuumTripBar, CondenserPressureBar); Turbine.Trip(); }

            // Low SG level only protects a running turbine (a cold, drained SG is not a fault).
            var lvl = Trips[(int)BopTripKind.SgLowLevel];
            if (!lvl.Active && !Turbine.Tripped && th.TurbineSteamKW > 0.0 && th.SecondaryLevelFrac <= Physics.SgLowLevelTripFrac)
            { lvl.Fire(Physics.SgLowLevelTripFrac, th.SecondaryLevelFrac); Turbine.Trip(); }

            // Manual mode: a closed breaker that cannot track the bus is shed (breaker only; the
            // unloaded rotor may then run on to the overspeed trip if steam stays on).
            var sync = Trips[(int)BopTripKind.OutOfSync];
            if (!sync.Active && !AutoLoad && Turbine.Synced)
            {
                double mismatch = Math.Abs(GridOutputFrac - Physics.Clamp(GridDemandFrac, 0.0, 1.5));
                OutOfSyncSeconds = mismatch > Physics.GeneratorSyncLossFrac ? OutOfSyncSeconds + dt : 0.0;
                if (OutOfSyncSeconds >= Physics.GeneratorSyncLossDwellS)
                { sync.Fire(Physics.GeneratorSyncLossFrac, mismatch); Turbine.Synced = false; OutOfSyncSeconds = 0.0; }
            }
            else if (!sync.Active)
            {
                OutOfSyncSeconds = 0.0;
            }
        }

        // ---- Snapshot support ----
        internal void Restore(bool autoLoad, double gridDemand, double vacuumPump, double coolingWater,
                              double rpm, bool synced, bool tripped, double condenserBar, double hotwell,
                              int tripMask, double[] tripMeasured, double outOfSyncS)
        {
            AutoLoad = autoLoad;
            GridDemandFrac = Physics.Clamp(Physics.Sanitize(gridDemand, 0.0), 0.0, 1.5);
            VacuumPumpFrac = Physics.Clamp(Physics.Sanitize(vacuumPump, 1.0), 0.0, 1.0);
            CoolingWaterFrac = Physics.Clamp(Physics.Sanitize(coolingWater, 1.0), 0.0, 1.0);
            Turbine.Rpm = Physics.Clamp(Physics.Sanitize(rpm, 0.0), 0.0, Physics.TurbineSpeedCeilingRpm);
            Turbine.Synced = synced;
            Turbine.Tripped = tripped;
            CondenserPressureBar = Physics.Clamp(Physics.Sanitize(condenserBar, 1.0), Physics.CondenserVacuumFloorBar, Physics.CondenserMaxPressureBar);
            HotwellInventoryFrac = Physics.Clamp(Physics.Sanitize(hotwell, Physics.HotwellNominalFrac), 0.0, 1.0);
            OutOfSyncSeconds = Math.Max(0.0, Physics.Sanitize(outOfSyncS, 0.0));
            for (int i = 0; i < Trips.Length; i++)
            {
                Trips[i].Clear();
                if ((tripMask & (1 << i)) != 0)
                    Trips[i].Fire(TripLimit((BopTripKind)i), tripMeasured != null && i < tripMeasured.Length ? tripMeasured[i] : 0.0);
            }
            Turbine.VacuumEfficiencyFrac = VacuumEfficiencyFrac;
            FeedAvailabilityFrac = Physics.Clamp(HotwellInventoryFrac / Physics.HotwellFeedStarveFrac, 0.0, 1.0);
        }

        internal double[] TripMeasured()
        {
            var m = new double[Trips.Length];
            for (int i = 0; i < Trips.Length; i++) m[i] = Trips[i].Measured;
            return m;
        }

        public static double TripLimit(BopTripKind kind)
        {
            switch (kind)
            {
                case BopTripKind.Overspeed: return Physics.TurbineOverspeedTripRpm;
                case BopTripKind.Underspeed: return Physics.TurbineUnderspeedTripRpm;
                case BopTripKind.LowVacuum: return Physics.CondenserLowVacuumTripBar;
                case BopTripKind.SgLowLevel: return Physics.SgLowLevelTripFrac;
                default: return Physics.GeneratorSyncLossFrac;
            }
        }

        // Preset helper: a running plant (rated speed, synced, vacuum drawn) or a cold one.
        internal void SetRunning(bool running)
        {
            Restore(true, 0.0, 1.0, 1.0, running ? Physics.TurbineRatedRpm : 0.0, running, false,
                    running ? Physics.CondenserVacuumFloorBar : 1.0, Physics.HotwellNominalFrac, 0, null, 0.0);
        }
    }
}
