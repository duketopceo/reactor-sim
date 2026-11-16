using System;
using System.Linq;
using Xunit;

namespace ReactorSim.Tests
{
    // Primary pressure protection: high pressurizer pressure reactor trip, automatic PORV with a close hysteresis
    // (manual open still works), and code safety valves above the trip. Setpoints and sources are in Physics.
    public class PrimaryPressureSetpointTests
    {
        [Fact]
        public void Setpoints_AreOrderedAroundTheNormalOperatingPressure()
        {
            Assert.InRange(Physics.NormalPrimaryPressureBar, 150.0, 160.0);
            Assert.True(Physics.NormalPrimaryPressureBar < Physics.PorvCloseBar);
            Assert.True(Physics.PorvCloseBar < Physics.PorvOpenBar);
            Assert.True(Physics.PorvOpenBar < Physics.TripHighPrimaryPressureBar);
            Assert.True(Physics.TripHighPrimaryPressureBar < Physics.SafetyValveOpenBar);
            Assert.True(Physics.SafetyValveCloseBar < Physics.SafetyValveOpenBar);
            Assert.True(Physics.SafetyValveCloseBar > Physics.TripHighPrimaryPressureBar - 1.0);
            Assert.True(Physics.SafetyValveOpenBar < Physics.PressureMaxBar - 10.0);
        }
    }

    public class PrimaryPressureTripTests
    {
        // Full-power preset with the primary pressure forced to a value (everything else normal).
        private static Plant AtPressure(double bar, double heaters = 0.75)
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            p.Thermal.RestoreState(605.0, 305.0, bar, 1.0, 1.0, 60.0, 0.96, 0.6, 1.0, heaters, 0.0, false, 0.0);
            return p;
        }

        [Fact]
        public void AboveSetpoint_Trips_AndRecordsCause()
        {
            var p = AtPressure(Physics.TripHighPrimaryPressureBar + 6.0);
            p.Step(0.2);
            Assert.True(p.Scrammed);
            Assert.Equal("high primary pressure", p.LastScramCause);
            Assert.All(p.Banks, b => Assert.Equal(1.0, b.InsertionTarget));
        }

        [Fact]
        public void AtSetpoint_Trips()
        {
            // Heaters full at a hot primary hold the target far above the setpoint, so pressure sits at it through one substep.
            var p = new Plant();
            p.LoadPreset("full-power");
            p.Thermal.RestoreState(605.0, 330.0, Physics.TripHighPrimaryPressureBar, 1.0, 1.0, 60.0, 0.96, 0.6, 1.0, 1.0, 0.0, false, 0.0);
            p.Step(0.2);
            Assert.True(p.Scrammed);
            Assert.Equal("high primary pressure", p.LastScramCause);
        }

        [Fact]
        public void BelowSetpoint_DoesNotTrip()
        {
            var p = AtPressure(Physics.PorvCloseBar - 3.0, 0.75);
            for (int i = 0; i < 100; i++) p.Step(0.2);
            Assert.False(p.Scrammed);
            Assert.Null(p.LastScramCause);
        }

        [Fact]
        public void Trip_StaysLatched_AgainstRodWithdrawal()
        {
            var p = AtPressure(Physics.TripHighPrimaryPressureBar + 6.0);
            p.Step(0.2);
            Assert.True(p.Scrammed);
            p.SetAllRods(0.0);
            p.Thermal.RestoreState(605.0, 305.0, 152.0, 1.0, 1.0, 60.0, 0.96, 0.6, 1.0, 0.75, 0.0, false, 0.0); // pressure back to normal
            for (int i = 0; i < 25; i++) p.Step(0.2);
            Assert.True(p.Scrammed);
            Assert.All(p.Banks, b => Assert.Equal(1.0, b.InsertionTarget));
            Assert.Equal("high primary pressure", p.LastScramCause);
        }

        [Fact]
        public void Trip_TakesTheModeToShutdown()
        {
            var p = AtPressure(Physics.TripHighPrimaryPressureBar + 6.0);
            p.Step(0.2);
            Assert.Equal(OperatingMode.Shutdown, p.Mode);
        }

        [Fact]
        public void LossOfSink_AtPower_NeverSitsOnTheClamp()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            p.Thermal.SteamDemandFrac = 0.0;
            p.Thermal.PrimaryFlowFrac = 0.0;
            double max = 0;
            for (int i = 0; i < 600; i++) { p.Step(5.0); max = Math.Max(max, p.Thermal.PrimaryPressureBar); }
            Assert.True(p.Scrammed);
            Assert.True(max < Physics.SafetyValveOpenBar + 4.0, "max primary pressure " + max);
            Assert.True(p.Thermal.PrimaryPressureBar < Physics.PressureMaxBar - 5.0);
        }
    }

    public class PorvTests
    {
        // Hot primary: with full heaters the heater-driven target (~170 bar) is above every relief setpoint.
        private static ThermalHydraulics Hot(double bar, double heaters, bool autoOpen = false, bool safetyOpen = false)
        {
            var t = new ThermalHydraulics();
            t.RestoreState(605.0, 305.0, bar, 1.0, 1.0, 60.0, 0.96, 0.6, 1.0, heaters, 0.0, false, 0.0, autoOpen, safetyOpen);
            return t;
        }

        [Fact]
        public void OpensAutomaticallyAboveSetpoint()
        {
            var t = Hot(Physics.PorvOpenBar + 0.5, 1.0);
            Assert.False(t.ReliefOpen);
            t.Step(0.2, 160000.0);
            Assert.True(t.ReliefAutoOpen);
            Assert.True(t.ReliefOpen);
            Assert.False(t.ReliefManualOpen);
        }

        [Fact]
        public void StaysClosedBelowSetpoint()
        {
            var t = Hot(Physics.PorvOpenBar - 0.5, 0.75);
            t.Step(0.2, 160000.0);
            Assert.False(t.ReliefOpen);
        }

        [Fact]
        public void ClosesBelowTheResetPressure()
        {
            var t = Hot(Physics.PorvCloseBar - 1.0, 0.5, autoOpen: true);
            t.Step(0.2, 160000.0);
            Assert.False(t.ReliefAutoOpen);
            Assert.False(t.ReliefOpen);
        }

        [Fact]
        public void HoldsStateInsideTheHysteresisBand()
        {
            double mid = 0.5 * (Physics.PorvCloseBar + Physics.PorvOpenBar);
            var o = Hot(mid, 0.75, autoOpen: true);
            o.Step(0.2, 160000.0);
            Assert.True(o.ReliefAutoOpen);
            var c = Hot(mid, 0.75, autoOpen: false);
            c.Step(0.2, 160000.0);
            Assert.False(c.ReliefAutoOpen);
        }

        [Fact]
        public void Open_ActuallyDepressurises()
        {
            var t = Hot(Physics.PorvOpenBar + 0.5, 0.75);
            double max = 0;
            for (int i = 0; i < 100; i++) { t.Step(0.2, 160000.0); max = Math.Max(max, t.PrimaryPressureBar); }
            Assert.True(max < Physics.PorvOpenBar + 1.5, "max " + max);
            Assert.True(t.PrimaryPressureBar < Physics.PorvOpenBar + 0.5);
        }

        [Fact]
        public void ManualOpen_StillWorks_AtNormalPressure()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            double before = p.Thermal.PrimaryPressureBar;
            p.Apply(PlantIntent.SetReliefValve(true));
            for (int i = 0; i < 10; i++) p.Step(0.2);
            Assert.True(p.Thermal.ReliefManualOpen);
            Assert.True(p.Thermal.ReliefOpen);
            Assert.False(p.Thermal.ReliefAutoOpen);
            Assert.True(p.Thermal.PrimaryPressureBar < before - 1.0, before + " -> " + p.Thermal.PrimaryPressureBar);
        }

        [Fact]
        public void ManualClosed_DoesNotInhibitAutomaticOpening()
        {
            var t = Hot(Physics.PorvOpenBar + 0.5, 1.0);
            t.ReliefManualOpen = false;
            t.Step(0.2, 160000.0);
            Assert.True(t.ReliefOpen);
        }

        [Fact]
        public void ManualOpen_IsNotClosedByTheAutomaticReset()
        {
            // Manual and automatic combine as OR: below the reset the automatic channel closes, the manual one holds the valve open.
            var t = Hot(Physics.PorvCloseBar - 1.0, 0.5, autoOpen: true);
            t.ReliefManualOpen = true;
            t.Step(0.2, 160000.0);
            Assert.False(t.ReliefAutoOpen);
            Assert.True(t.ReliefManualOpen);
            Assert.True(t.ReliefOpen);
        }

        [Fact]
        public void SafetyValve_LiftsAboveItsSetpoint_AndReseatsWithBlowdown()
        {
            var t = Hot(Physics.SafetyValveOpenBar + 0.5, 1.0);
            t.Step(0.2, 160000.0);
            Assert.True(t.SafetyValveOpen);
            Assert.True(t.ReliefOpen); // the discharge shows on the primary relief outputs
            var r = Hot(Physics.SafetyValveCloseBar - 0.5, 0.5, safetyOpen: true);
            r.Step(0.2, 160000.0);
            Assert.False(r.SafetyValveOpen);
        }

        [Fact]
        public void Snapshot_RoundTripsValveState()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            p.Thermal.RestoreState(605.0, 305.0, 163.0, 1.0, 1.0, 60.0, 0.96, 0.6, 1.0, 0.75, 0.0, true, 0.0, true, true);
            p.Thermal.ReliefManualOpen = true; // operator also holds it open
            var s = p.CaptureState(0.0);
            Assert.True(s.ReliefOpen); Assert.True(s.ReliefManualOpen); Assert.True(s.ReliefAutoOpen); Assert.True(s.SafetyValveOpen);
            var back = PlantSnapshot.FromJson(s.ToJson());
            Assert.True(back.ReliefManualOpen); Assert.True(back.ReliefAutoOpen); Assert.True(back.SafetyValveOpen);
            var q = new Plant();
            q.RestoreState(back);
            Assert.True(q.Thermal.ReliefManualOpen); Assert.True(q.Thermal.ReliefAutoOpen); Assert.True(q.Thermal.SafetyValveOpen);
            Assert.Equal(s.ToJson(), q.CaptureState(0.0).ToJson());
        }

        [Fact]
        public void OldSave_WithoutTheNewFields_RestoresReliefAsManual()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            p.Thermal.RestoreState(605.0, 305.0, 150.0, 1.0, 1.0, 60.0, 0.96, 0.6, 1.0, 0.75, 0.0, true, 0.0);
            string json = p.CaptureState(0.0).ToJson();
            // Strip the new keys to imitate a save written before this change.
            json = System.Text.RegularExpressions.Regex.Replace(json, "\"(reliefManualOpen|reliefAutoOpen|safetyValveOpen)\":(true|false),?", "");
            var back = PlantSnapshot.FromJson(json);
            Assert.True(back.ReliefOpen);
            Assert.True(back.ReliefManualOpen);
            Assert.False(back.ReliefAutoOpen);
            Assert.False(back.SafetyValveOpen);
        }
    }

    public class PressureStartupTests
    {
        [Fact]
        public void ColdStartup_ReachesGrid_WithoutTrip_AndStaysBelowTheReliefSetpoint()
        {
            var c = ParityScenarios.Generate(ParityScenarios.ColdStartup);
            Assert.True(c.Frames.Last().GeneratorSynced);
            Assert.DoesNotContain(c.Frames, s => s.Scrammed || s.BopTripMask != 0);
            Assert.DoesNotContain(c.Frames, s => s.ReliefOpen);
            double max = c.Frames.Max(s => s.PrimaryPressureBar);
            Assert.True(max < Physics.PorvCloseBar, "startup max pressure " + max);
        }

        [Fact]
        public void ColdStartup_HoldsNormalBandOnceHot()
        {
            var c = ParityScenarios.Generate(ParityScenarios.ColdStartup);
            var hot = c.Frames.Where(s => s.PrimaryTempC > 285.0).ToList();
            Assert.NotEmpty(hot);
            Assert.All(hot, s => Assert.InRange(s.PrimaryPressureBar, Physics.NormalPrimaryPressureBar - 6.0, Physics.NormalPrimaryPressureBar + 4.0));
        }

        // Root cause of the startup overpressure: pressure is Psat(T) + heaters, so heaters left pegged while the primary heats
        // walk the pressure up with the temperature; the trimmed heater demand holds it at the normal pressure.
        private static Plant HotPrimary(double heaters)
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            p.Thermal.RestoreState(605.0, 330.0, 150.0, 1.0, 1.0, 60.0, 0.96, 0.6, 1.0, heaters, 0.0, false, 0.0);
            return p;
        }

        [Fact]
        public void PeggedHeaters_OnAHotPrimary_TripOnHighPressure()
        {
            var p = HotPrimary(1.0);
            for (int i = 0; i < 50; i++) p.Step(0.2);
            Assert.True(p.Scrammed);
            Assert.Equal("high primary pressure", p.LastScramCause);
        }

        [Fact]
        public void TrimmedHeaters_OnAHotPrimary_HoldTheNormalPressure()
        {
            var p = HotPrimary(1.0);
            double max = 0;
            for (int i = 0; i < 300; i++)
            {
                p.Thermal.HeaterPowerFrac = Physics.TrimHeaterDemand(p.Thermal.PrimaryTempC, p.Thermal.PrimaryPressureBar);
                p.Step(0.2);
                max = Math.Max(max, p.Thermal.PrimaryPressureBar);
            }
            Assert.False(p.Scrammed);
            Assert.True(max < Physics.NormalPrimaryPressureBar + 3.0, "max " + max);
            Assert.InRange(p.Thermal.PrimaryPressureBar, Physics.NormalPrimaryPressureBar - 2.0, Physics.NormalPrimaryPressureBar + 2.0);
        }

        [Theory]
        [InlineData(20.0)]
        [InlineData(150.0)]
        [InlineData(250.0)]
        [InlineData(305.0)]
        [InlineData(340.0)]
        public void TrimHeaterDemand_IsBoundedAndFallsAsTheCoreHeats(double tempC)
        {
            double h = Physics.TrimHeaterDemand(tempC, Physics.NormalPrimaryPressureBar);
            Assert.InRange(h, 0.0, 1.0);
            Assert.True(Physics.TrimHeaterDemand(tempC + 10.0, Physics.NormalPrimaryPressureBar) <= h);
            Assert.True(Physics.TrimHeaterDemand(tempC, Physics.NormalPrimaryPressureBar + 10.0) <= h);
        }

        [Fact]
        public void SteadyFullPower_StaysNearNormal()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            double max = 0;
            for (int i = 0; i < 300; i++) { p.Step(5.0); max = Math.Max(max, p.Thermal.PrimaryPressureBar); }
            Assert.False(p.Scrammed);
            Assert.True(max < Physics.NormalPrimaryPressureBar + 3.0, "max " + max);
        }

        [Theory]
        [InlineData(ParityScenarios.FlowLoss)]
        [InlineData(ParityScenarios.HighPressure)]
        [InlineData(ParityScenarios.FeedStarvation)]
        [InlineData(ParityScenarios.TurbineTripBypass)]
        [InlineData(ParityScenarios.BypassWhileTripped)]
        public void ParityScenarios_NoLongerSitOnTheClamp(string name)
        {
            var c = ParityScenarios.Generate(name);
            double max = c.Frames.Max(s => s.PrimaryPressureBar);
            Assert.True(max < Physics.SafetyValveOpenBar + 4.0, name + " max primary pressure " + max);
            Assert.Equal(0, c.Frames.Count(s => s.PrimaryPressureBar >= Physics.PressureMaxBar - 0.5));
        }

        [Fact]
        public void HighPressureScenario_ReliefAndTripBothAppear()
        {
            var c = ParityScenarios.Generate(ParityScenarios.HighPressure);
            Assert.Contains(c.Frames, s => s.ReliefOpen);
            Assert.Contains(c.Frames, s => s.Scrammed);
        }
    }
}
