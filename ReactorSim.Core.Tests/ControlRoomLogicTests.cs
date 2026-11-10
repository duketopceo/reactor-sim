using System;
using Xunit;
using ReactorSim;
using ReactorSim.Controls;
using ReactorSim.Instruments;

namespace ReactorSim.Core.Tests
{
    // Pure logic behind the 3D control room (U6/U7). The source files live under
    // ReactorSim.Unity/Assets/Scripts/View3D and are linked into this project.
    public class ControlRoomLogicTests
    {
        private static ControlSpec Ok(string name)
        {
            var r = PivotNameParser.Parse(name);
            Assert.True(r.Status == ParseStatus.Ok, name + " -> " + r.Status + " " + r.Error);
            return r.Spec;
        }

        [Fact]
        public void Parse_DiluteRate_IsDiluteWithRange0to5()
        {
            var s = Ok("INT_DIAL_DiluteRate_Boron_0_5");
            Assert.Equal(ControlStyle.Dial, s.Style);
            Assert.Equal(IntentKind.DiluteRate, s.Kind);
            Assert.Equal("Boron", s.Target);
            Assert.Equal(0.0, s.Min);
            Assert.Equal(5.0, s.Max);
        }

        [Fact]
        public void Parse_AllRodsTarget_HasNoTarget_UsesDefault()
        {
            var s = Ok("INT_DIAL_AllRodsTarget_0_1");
            Assert.Equal(IntentKind.AllRodsTarget, s.Kind);
            Assert.Equal("ALL", s.Target);
            Assert.Equal(0.0, s.Min);
            Assert.Equal(1.0, s.Max);
        }

        [Fact]
        public void Parse_NaOHRate_MatchesNaohRateCaseInsensitively()
        {
            var s = Ok("INT_DIAL_NaOHRate_pH_0_5");
            Assert.Equal(IntentKind.NaohRate, s.Kind);
            Assert.Equal("pH", s.Target);
        }

        [Fact]
        public void Parse_RodBank_TargetIsBankIndex()
        {
            var s = Ok("INT_DIAL_RodBankTarget_2_0_1");
            Assert.Equal(IntentKind.RodBankTarget, s.Kind);
            Assert.Equal("2", s.Target);
            Assert.Equal(2, s.RodBank);
        }

        [Fact]
        public void Parse_FractionalRange()
        {
            var s = Ok("INT_DIAL_PrimaryFlow_RCP_0_1.2");
            Assert.Equal(1.2, s.Max, 9);
        }

        [Fact]
        public void Parse_Buttons()
        {
            var scram = Ok("INT_BTN_Scram_Core_1");
            Assert.Equal(ControlStyle.Button, scram.Style);
            Assert.Equal(IntentKind.Scram, scram.Kind);
            Assert.Equal(1.0, scram.ButtonValue);
            Assert.Equal(IntentKind.ResetScram, Ok("INT_BTN_ResetScram_Core_0").Kind);
            Assert.Equal(IntentKind.ReliefValve, Ok("INT_BTN_ReliefValve_PORV_1").Kind);
            var pace = Ok("INT_BTN_SetTimeScale_Clock_0.5");
            Assert.Equal(0.5, pace.ButtonValue, 9);
        }

        [Fact]
        public void Parse_BlenderDuplicateSuffixIsStripped()
        {
            var s = Ok("INT_BTN_Scram_Core_1.001");
            Assert.Equal(IntentKind.Scram, s.Kind);
            Assert.Equal("INT_BTN_Scram_Core_1", s.BaseName);
        }

        [Theory]
        [InlineData("INT_DIAL_RodBankTarget_0_0_1_Knob")]
        [InlineData("INT_DIAL_BorateRate_Boron_0_5_Pointer")]
        [InlineData("INT_BTN_Scram_Core_1_Control")]
        public void Parse_VisualChildren_AreNotControlsNorMalformed(string name)
        {
            Assert.Equal(ParseStatus.VisualChild, PivotNameParser.Parse(name).Status);
        }

        [Theory]
        [InlineData("INT_DIAL_Bogus_X_0_1")]          // unknown kind
        [InlineData("INT_DIAL_DiluteRate_Boron_5_0")]  // min >= max
        [InlineData("INT_DIAL_DiluteRate_Boron")]      // no range
        [InlineData("INT_DIAL_DiluteRate_Boron_a_5")]  // non numeric
        [InlineData("INT_BTN_Scram_Core_x")]           // non numeric value
        [InlineData("INT_BTN_Scram")]                  // too short
        [InlineData("INT_WIDGET_Scram_Core_1")]        // unknown style
        public void Parse_Malformed_IsReportedWithTheName(string name)
        {
            var r = PivotNameParser.Parse(name);
            Assert.Equal(ParseStatus.Malformed, r.Status);
            Assert.False(string.IsNullOrEmpty(r.Error));
            Assert.Contains(name, r.Error);
            Assert.Null(r.Spec);
        }

        [Fact]
        public void Parse_NullOrNonInt_IsNotAControl()
        {
            Assert.Equal(ParseStatus.NotControl, PivotNameParser.Parse(null).Status);
            Assert.Equal(ParseStatus.NotControl, PivotNameParser.Parse("BIND_GAUGE_FuelTempC_0_1400").Status);
        }

        [Fact]
        public void Drag_BottomOfRodLever_SubmitsZeroForThatBankOnly()
        {
            var spec = Ok("INT_DIAL_RodBankTarget_1_0_1");
            double frac = DragMapper.FractionAfterDrag(0.5, -100000.0, 300.0);
            Assert.Equal(0.0, frac);
            double value = DragMapper.ValueFromFraction(spec, frac);
            var intent = ControlIntentFactory.BuildDial(spec, value, "diegetic");
            Assert.Equal(IntentKind.RodBankTarget, intent.Kind);
            Assert.Equal("1", intent.Target);
            Assert.Equal(0.0, intent.Value);
        }

        [Fact]
        public void Drag_TopClampsToMax_AndMidpointScalesByRange()
        {
            var spec = Ok("INT_DIAL_SteamDemand_Turbine_0_1.2");
            Assert.Equal(1.2, DragMapper.ValueFromFraction(spec, DragMapper.FractionAfterDrag(0.0, 99999.0, 300.0)), 9);
            Assert.Equal(0.6, DragMapper.ValueFromFraction(spec, 0.5), 9);
            Assert.Equal(0.75, DragMapper.FractionAfterDrag(0.5, 75.0, 300.0), 9);
        }

        [Fact]
        public void Drag_DiscreteDialsRound()
        {
            var spec = Ok("INT_DIAL_AssistTier_Assistant_0_3");
            Assert.True(spec.Discrete);
            Assert.Equal(2.0, DragMapper.ValueFromFraction(spec, 0.7));
        }

        [Theory]
        [InlineData("INT_DIAL_AllRodsTarget_0_1", IntentKind.AllRodsTarget)]
        [InlineData("INT_DIAL_BorateRate_Boron_0_5", IntentKind.BorateRate)]
        [InlineData("INT_DIAL_DiluteRate_Boron_0_5", IntentKind.DiluteRate)]
        [InlineData("INT_DIAL_NaOHRate_pH_0_5", IntentKind.NaohRate)]
        [InlineData("INT_DIAL_IonExchangeRate_pH_0_6", IntentKind.IonExchangeRate)]
        [InlineData("INT_DIAL_PrimaryFlow_RCP_0_1.2", IntentKind.PrimaryFlow)]
        [InlineData("INT_DIAL_PrimaryLevel_PZR_0_1.5", IntentKind.PrimaryLevel)]
        [InlineData("INT_DIAL_HeaterPower_PZR_0_1", IntentKind.HeaterPower)]
        [InlineData("INT_DIAL_Spray_PZR_0_1", IntentKind.Spray)]
        [InlineData("INT_DIAL_SteamDemand_Turbine_0_1.2", IntentKind.SteamDemand)]
        [InlineData("INT_DIAL_FeedwaterFlow_SGFeed_0_1.5", IntentKind.FeedwaterFlow)]
        [InlineData("INT_DIAL_SteamDump_Dump_0_1", IntentKind.SteamDump)]
        [InlineData("INT_DIAL_AssistTier_Assistant_0_3", IntentKind.AssistTier)]
        [InlineData("INT_DIAL_OperatingMode_Core_0_4", IntentKind.OperatingMode)]
        [InlineData("INT_DIAL_SelectMission_Planner_0_10", IntentKind.SelectMission)]
        public void Dial_BuildsIntentOfTheRightKind(string name, IntentKind kind)
        {
            var spec = Ok(name);
            var intent = ControlIntentFactory.BuildDial(spec, spec.Max, "diegetic");
            Assert.NotNull(intent);
            Assert.Equal(kind, intent.Kind);
        }

        [Fact]
        public void Button_Intents()
        {
            Assert.Equal(IntentKind.Scram, ControlIntentFactory.BuildButton(Ok("INT_BTN_Scram_Core_1"), false, 10.0, "d").Kind);
            Assert.Equal(IntentKind.ResetScram, ControlIntentFactory.BuildButton(Ok("INT_BTN_ResetScram_Core_0"), false, 10.0, "d").Kind);
            var relief = Ok("INT_BTN_ReliefValve_PORV_1");
            Assert.Equal(1.0, ControlIntentFactory.BuildButton(relief, false, 10.0, "d").Value);
            Assert.Equal(0.0, ControlIntentFactory.BuildButton(relief, true, 10.0, "d").Value);
            var pause = Ok("INT_BTN_PauseClock_Clock_1");
            Assert.Equal(IntentKind.PauseClock, ControlIntentFactory.BuildButton(pause, false, 10.0, "d").Kind);
            Assert.Equal(IntentKind.ResumeClock, ControlIntentFactory.BuildButton(pause, true, 10.0, "d").Kind);
            var up = ControlIntentFactory.BuildButton(Ok("INT_BTN_SetTimeScale_Clock_2"), false, 10.0, "d");
            Assert.Equal(IntentKind.SetTimeScale, up.Kind);
            Assert.Equal(20.0, up.Value, 9);
            var skip = ControlIntentFactory.BuildButton(Ok("INT_BTN_SkipAhead_Clock_600"), false, 10.0, "d");
            Assert.Equal(IntentKind.SkipAhead, skip.Kind);
            Assert.Equal(600.0, skip.Value, 9);
        }

        [Fact]
        public void ControlValues_ReadSnapshotFields()
        {
            var s = new PlantSnapshot();
            s.BankInsertion = new double[] { 1, 0.8, 1, 1 };
            s.BankTarget = new double[] { 1, 0.25, 1, 1 };
            s.SteamDemandFrac = 0.4;
            Assert.Equal(0.25, ControlValues.Commanded(Ok("INT_DIAL_RodBankTarget_1_0_1"), s), 9);
            Assert.Equal(0.8, ControlValues.Current(Ok("INT_DIAL_RodBankTarget_1_0_1"), s), 9);
            Assert.Equal(0.4, ControlValues.Current(Ok("INT_DIAL_SteamDemand_Turbine_0_1.2"), s), 9);
            // The mode knob reads the plant's mode, so turning it to any mode (including the knob's midpoint) sends.
            var mode = Ok("INT_DIAL_OperatingMode_Core_0_2");
            s.Mode = "Shutdown";
            Assert.Equal(0.0, ControlValues.Current(mode, s), 9);
            s.Mode = "Nominal";
            Assert.Equal(1.0, ControlValues.Commanded(mode, s), 9);
            s.Mode = "Maximum";
            Assert.Equal(2.0, ControlValues.Current(mode, s), 9);
        }

        [Fact]
        public void Needle_InterpolatesBetweenSnapshotsByClockFraction()
        {
            Assert.Equal(15.0, NeedleMath.Interpolate(10.0, 20.0, 0.5), 9);
            Assert.Equal(10.0, NeedleMath.Interpolate(10.0, 20.0, -3.0), 9);
            Assert.Equal(20.0, NeedleMath.Interpolate(10.0, 20.0, 7.0), 9);
            Assert.Equal(5.0, NeedleMath.Interpolate(double.NaN, 5.0, 0.5), 9);
            Assert.Equal(0.25, NeedleMath.Fraction(250.0, 0.0, 1000.0), 9);
            Assert.Equal(1.0, NeedleMath.Fraction(5000.0, 0.0, 1000.0), 9);
            Assert.Equal(0.0, NeedleMath.Fraction(5.0, 10.0, 10.0), 9);
        }

        [Fact]
        public void ClockReadout_FormatsEachState()
        {
            var st = new ClockState { Scale = 10.0, EffectiveScale = 10.0 };
            Assert.Equal("x10", ClockReadout.Format(st));
            st.Scale = 0.5; st.EffectiveScale = 0.5;
            Assert.Equal("x0.5", ClockReadout.Format(st));
            st.Paused = true;
            Assert.Contains("PAUSED", ClockReadout.Format(st));
            st.Paused = false; st.AutoSlow = true; st.EffectiveScale = 3.0; st.Scale = 30.0; st.AutoSlowReason = "short period";
            string slow = ClockReadout.Format(st);
            Assert.Contains("AUTO-SLOW", slow);
            Assert.Contains("x3", slow);
            Assert.Contains("short period", slow);
            st.Frozen = true; st.FrozenReason = "reactor casualty";
            string frozen = ClockReadout.Format(st);
            Assert.Contains("FROZEN", frozen);
            Assert.Contains("reactor casualty", frozen);
            st.Frozen = false; st.Skipping = true; st.EffectiveScale = 60.0;
            Assert.Contains("SKIP", ClockReadout.Format(st));
        }

        [Fact]
        public void ClockReadout_FromRealClock()
        {
            var sim = new Simulation(new Tuning());
            var st = ClockReadout.FromClock(sim.Clock);
            Assert.Equal(sim.Clock.Scale, st.Scale);
            Assert.False(st.Paused);
        }

        [Fact]
        public void GaugeSources_ParseRangesIncludingNegativeAndExponent()
        {
            GaugeSource g;
            Assert.True(GaugeSources.TryResolve("BIND_GAUGE_TotalReactivityPcm_-2500_1000", out g));
            Assert.Equal(-2500.0, g.Min);
            Assert.Equal(1000.0, g.Max);
            var snap = new PlantSnapshot { TotalReactivityPcm = -123.0 };
            Assert.Equal(-123.0, g.Select(snap));

            Assert.True(GaugeSources.TryResolve("BIND_GAUGE_ExcoreFluxCps_0_3.2e8", out g));
            Assert.Equal(3.2e8, g.Max, 1);
            Assert.True(GaugeSources.TryResolve("BIND_GAUGE_QuarterPower_pct_0_100", out g));
            Assert.True(GaugeSources.TryResolve("BIND_BAR_BankInsertion_2_0_1", out g));
            var s2 = new PlantSnapshot();
            s2.BankInsertion = new double[] { 0, 0, 0.7, 0 };
            Assert.Equal(0.7, g.Select(s2), 9);
            Assert.True(GaugeSources.TryResolve("BIND_BAR_SecondaryLevelFrac_1_0_1", out g));
            Assert.False(GaugeSources.TryResolve("BIND_GAUGE_Nonsense_0_1", out g));
            Assert.False(GaugeSources.TryResolve("BIND_GAUGE_FuelTempC", out g));
        }

        [Fact]
        public void GaugeSources_EveryBlenderGaugeAndBarNameResolves()
        {
            string[] names =
            {
                "BIND_GAUGE_FuelTempC_0_1400", "BIND_GAUGE_PrimaryPressureBar_0_180", "BIND_GAUGE_SubCoolingC_0_100",
                "BIND_GAUGE_CorePowerMWt_0_3200", "BIND_GAUGE_ExcoreFluxCps_0_3.2e8", "BIND_GAUGE_SectorFluxCps_0_1.1e8",
                "BIND_GAUGE_TotalReactivityPcm_-2500_1000", "BIND_GAUGE_QuarterPower_pct_0_100",
                "BIND_BAR_BankInsertion_0_0_1", "BIND_BAR_BankInsertion_3_0_1", "BIND_BAR_PrimaryLevelFrac_0_1",
                "BIND_BAR_SecondaryLevelFrac_0_0_1", "BIND_BAR_SecondaryLevelFrac_1_0_1",
            };
            foreach (var n in names)
            {
                GaugeSource g;
                Assert.True(GaugeSources.TryResolve(n, out g), n);
            }
        }

        private static double Gauge(string pivot, double power)
        {
            GaugeSource g;
            Assert.True(GaugeSources.TryResolve(pivot, out g), pivot);
            return g.Select(new PlantSnapshot { Power = power });
        }

        [Fact]
        public void GaugeSources_FullPowerReadsRealPlantUnits()
        {
            Assert.Equal(Physics.RatedThermalMWt, Gauge("BIND_GAUGE_CorePowerMWt_0_200", 1.0), 6);
            Assert.Equal(100.0, Gauge("BIND_GAUGE_QuarterPower_pct_0_100", 1.0), 6);
            Assert.Equal(3.2e8, Gauge("BIND_GAUGE_ExcoreFluxCps_0_3.2e8", 1.0), 3);
            Assert.Equal(1.1e8, Gauge("BIND_GAUGE_SectorFluxCps_0_1.1e8", 1.0), 3);
        }

        [Fact]
        public void GaugeSources_ZeroPowerReadsZeroNotNaN()
        {
            foreach (var n in new[] { "BIND_GAUGE_CorePowerMWt_0_200", "BIND_GAUGE_QuarterPower_pct_0_100",
                "BIND_GAUGE_ExcoreFluxCps_0_3.2e8", "BIND_GAUGE_SectorFluxCps_0_1.1e8" })
            {
                double v = Gauge(n, 0.0);
                Assert.False(double.IsNaN(v), n);
                Assert.Equal(0.0, v);
            }
        }

        [Fact]
        public void HighFluxLamp_FiresAboveNormalisedTripSetpoint()
        {
            Assert.True(GaugeSources.HighFlux(new PlantSnapshot { Power = Physics.HighFluxTripPower + 0.001 }));
            Assert.False(GaugeSources.HighFlux(new PlantSnapshot { Power = Physics.HighFluxTripPower - 0.001 }));
        }

        [Fact]
        public void CrtHeader_PrintsRealMWt()
        {
            var snap = new PlantSnapshot { Power = 0.5 };
            string text = CrtPages.Build(CrtPages.KnownPages[0], snap, new ClockState(), new TrendBuffer(10), new string[0]);
            Assert.Contains("PWR " + (0.5 * Physics.RatedThermalMWt).ToString("F0") + " MWt", text);
        }

        [Fact]
        public void CrtPages_EveryKnownPageShowsPowerAndReactivity()
        {
            var snap = new PlantSnapshot { Power = 0.5, TotalReactivityPcm = -567.0 };
            var clock = new ClockState { Scale = 10.0, EffectiveScale = 10.0 };
            var trends = new TrendBuffer(60);
            trends.Push(snap);
            foreach (var name in CrtPages.KnownPages)
            {
                Assert.True(CrtPages.IsKnownPage(name), name);
                string text = CrtPages.Build(name, snap, clock, trends, new string[] { "[ 1.0m | OPS] test advisory" });
                Assert.False(string.IsNullOrWhiteSpace(text), name);
                Assert.Contains("PWR " + (0.5 * Physics.RatedThermalMWt).ToString("F0") + " MWt", text);
                Assert.Contains("-567", text);
            }
            Assert.False(CrtPages.IsKnownPage("CRT_Nope"));
        }

        [Fact]
        public void CrtPages_OverviewShowsClockState_AssistantShowsAdvisories()
        {
            var snap = new PlantSnapshot();
            var clock = new ClockState { Scale = 10.0, EffectiveScale = 10.0, Paused = true };
            var trends = new TrendBuffer(10);
            string overview = CrtPages.Build("CRT_A1_Core", snap, clock, trends, new string[0]);
            Assert.Contains("PAUSED", overview);
            string term = CrtPages.Build("CRT_AssistantTerminal", snap, clock, trends, new string[] { "advisory-line-one" });
            Assert.Contains("advisory-line-one", term);
        }

        [Fact]
        public void TrendBuffer_KeepsLastSamplesAndRendersFixedWidthSparkline()
        {
            var t = new TrendBuffer(5);
            for (int i = 0; i < 9; i++)
                t.Push(new PlantSnapshot { SimSeconds = i + 1, Power = i * 100.0 });
            Assert.Equal(5, t.Count);
            string line = t.Sparkline(TrendBuffer.Series.Power, 5);
            Assert.Equal(5, line.Length);
            // Same sim time again is not a new sample.
            t.Push(new PlantSnapshot { SimSeconds = 9, Power = 999.0 });
            Assert.Equal(5, t.Count);
        }

        // ---- U8: selector table, new control kinds, per-row geometry ----

        private static PlantSnapshot RealSnapshot(string preset, bool pumpsOn)
        {
            var sim = new Simulation(new Tuning());
            if (preset == "sandbox") SandboxStart.Apply(sim); else sim.LoadScenarioPreset(preset);
            for (int i = 0; i < 20; i++) sim.Tick();
            return sim.CaptureSnapshot();
        }

        [Theory]
        [InlineData("sandbox")]
        [InlineData("cold-shutdown")]
        [InlineData("full-power")]
        public void SelectorTable_EveryEntryIsFiniteOnRealSnapshots(string preset)
        {
            var s = RealSnapshot(preset, true);
            Assert.NotEmpty(GaugeSources.Table);
            foreach (var e in GaugeSources.Table)
            {
                double v = e.Select(s, e.Indexed ? 0 : -1);
                Assert.True(!double.IsNaN(v) && !double.IsInfinity(v), e.Name + " not finite on " + preset + ": " + v);
                Assert.False(string.IsNullOrEmpty(e.Units), e.Name + " has no units");
            }
        }

        [Fact]
        public void SelectorTable_NamesAreUnique_AndResolveWithUnits()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var e in GaugeSources.Table)
            {
                Assert.True(seen.Add(e.Name), "duplicate selector " + e.Name);
                GaugeSource g;
                string pivot = e.Indexed ? "BIND_BAR_" + e.Name + "_0_0_1" : "BIND_GAUGE_" + e.Name + "_0_1";
                Assert.True(GaugeSources.TryResolve(pivot, out g), pivot);
                Assert.Equal(e.Units, g.Units);
            }
            GaugeSource none;
            Assert.False(GaugeSources.TryResolve("BIND_BAR_BankInsertion_0_1", out none)); // indexed selector needs an index
        }

        [Fact]
        public void SelectorTable_NewInstrumentsReadTheirSnapshotField()
        {
            var s = new PlantSnapshot
            {
                DecayHeatPower = 0.03, TotalHeatPower = 0.5, Iodine135 = 0.2, Xenon135 = 0.3, TurbineRpm = 2999,
                GeneratorKw = 45000, GridFrequencyHz = 49.7, CondenserPressureBar = 0.4, HotwellInventoryFrac = 0.55,
                VacuumPumpFrac = 0.8, CoolingWaterFrac = 0.6, BopAutoLoad = false, BopTripMask = 5, BopOutOfSyncSeconds = 12,
                BankIntegrity = new double[] { 1, 0.9, 0.8, 1 }, BankSpeed = new double[] { 0.02, 0.04, 0.02, 0.04 }
            };
            double V(string n) { var e = GaugeSources.Find(n); Assert.NotNull(e); return e.Select(s, 0); }
            Assert.Equal(0.03, V("DecayHeatPower")); Assert.Equal(0.5, V("TotalHeatPower"));
            Assert.Equal(0.2, V("Iodine135")); Assert.Equal(0.3, V("Xenon135")); Assert.Equal(2999, V("TurbineRpm"));
            Assert.Equal(45000, V("GeneratorKw")); Assert.Equal(49.7, V("GridFrequencyHz")); Assert.Equal(0.4, V("CondenserPressureBar"));
            Assert.Equal(0.55, V("HotwellInventoryFrac")); Assert.Equal(0.8, V("VacuumPumpFrac")); Assert.Equal(0.6, V("CoolingWaterFrac"));
            Assert.Equal(0.0, V("BopAutoLoad")); Assert.Equal(5.0, V("BopTripMask")); Assert.Equal(12.0, V("BopOutOfSyncSeconds"));
            Assert.Equal(0.8, V("BankIntegrityMin")); Assert.Equal(0.03, V("BankSpeedMean"), 9);
        }

        [Fact]
        public void NewKinds_DialsBuildTheRightIntent()
        {
            var grid = ControlIntentFactory.BuildDial(Ok("INT_DIAL_GridDemand_Grid_0_1.5"), 0.75, "t");
            Assert.Equal(IntentKind.GridDemand, grid.Kind); Assert.Equal(0.75, grid.Value);
            var vac = ControlIntentFactory.BuildDial(Ok("INT_DIAL_VacuumPump_Cond_0_1"), 0.4, "t");
            Assert.Equal(IntentKind.VacuumPump, vac.Kind); Assert.Equal(0.4, vac.Value);
            var cw = ControlIntentFactory.BuildDial(Ok("INT_DIAL_CoolingWater_Cond_0_1"), 0.9, "t");
            Assert.Equal(IntentKind.CoolingWater, cw.Kind); Assert.Equal(0.9, cw.Value);
        }

        [Fact]
        public void NewKinds_ButtonsToggleFromCurrentState()
        {
            var auto = Ok("INT_BTN_TurbineAutoLoad_Turbine_1");
            var toManual = ControlIntentFactory.BuildButton(auto, true, 1.0, "t");
            Assert.Equal(IntentKind.TurbineAutoLoad, toManual.Kind); Assert.Equal(0.0, toManual.Value);
            var toAuto = ControlIntentFactory.BuildButton(auto, false, 1.0, "t");
            Assert.Equal(1.0, toAuto.Value);

            var brk = Ok("INT_BTN_GeneratorBreaker_Gen_1");
            var close = ControlIntentFactory.BuildButton(brk, false, 1.0, "t");
            Assert.Equal(IntentKind.GeneratorBreaker, close.Kind); Assert.Equal(1.0, close.Value);
            var open = ControlIntentFactory.BuildButton(brk, true, 1.0, "t");
            Assert.Equal(0.0, open.Value);

            Assert.Equal(IntentKind.ResetBopTrips, ControlIntentFactory.BuildButton(Ok("INT_BTN_ResetBopTrips_BOP_1"), false, 1.0, "t").Kind);
        }

        [Fact]
        public void NewKinds_AreSupportedAndHaveDefaultTargets()
        {
            foreach (var k in new[] { IntentKind.TurbineAutoLoad, IntentKind.GeneratorBreaker, IntentKind.GridDemand,
                IntentKind.VacuumPump, IntentKind.CoolingWater, IntentKind.ResetBopTrips })
                Assert.NotEqual("", PivotNameParser.DefaultTarget(k));
            Assert.True(ControlIntentFactory.IsSupported(Ok("INT_DIAL_VacuumPump_0_1")));
            Assert.Equal("Condenser", Ok("INT_DIAL_VacuumPump_0_1").Target);
            Assert.True(ControlIntentFactory.IsSupported(Ok("INT_BTN_GeneratorBreaker_1")));
        }

        [Fact]
        public void ControlValues_ReadTheMatchingSnapshotFieldForTheNewKinds()
        {
            var s = new PlantSnapshot { GridDemandFrac = 0.6, VacuumPumpFrac = 0.3, CoolingWaterFrac = 0.7, BopAutoLoad = true, GeneratorSynced = false };
            Assert.Equal(0.6, ControlValues.Current(Ok("INT_DIAL_GridDemand_Grid_0_1.5"), s));
            Assert.Equal(0.3, ControlValues.Current(Ok("INT_DIAL_VacuumPump_Cond_0_1"), s));
            Assert.Equal(0.7, ControlValues.Current(Ok("INT_DIAL_CoolingWater_Cond_0_1"), s));
            var auto = Ok("INT_BTN_TurbineAutoLoad_Turbine_1");
            var brk = Ok("INT_BTN_GeneratorBreaker_Gen_1");
            Assert.Equal(1.0, ControlValues.Current(auto, s)); Assert.True(ControlValues.ButtonOn(auto, s));
            Assert.Equal(0.0, ControlValues.Current(brk, s)); Assert.False(ControlValues.ButtonOn(brk, s));
            s.BopAutoLoad = false; s.GeneratorSynced = true;
            Assert.False(ControlValues.ButtonOn(auto, s)); Assert.True(ControlValues.ButtonOn(brk, s));
        }

        [Fact]
        public void NewKinds_SubmittedToARealSim_ChangeTheSnapshotField()
        {
            var sim = new Simulation(new Tuning());
            sim.LoadScenarioPreset("full-power");
            sim.EnqueueIntent(ControlIntentFactory.BuildDial(Ok("INT_DIAL_VacuumPump_Cond_0_1"), 0.5, "t"));
            sim.EnqueueIntent(ControlIntentFactory.BuildDial(Ok("INT_DIAL_CoolingWater_Cond_0_1"), 0.6, "t"));
            sim.EnqueueIntent(ControlIntentFactory.BuildDial(Ok("INT_DIAL_GridDemand_Grid_0_1.5"), 0.7, "t"));
            sim.EnqueueIntent(ControlIntentFactory.BuildButton(Ok("INT_BTN_TurbineAutoLoad_Turbine_1"), true, 1.0, "t"));
            sim.Tick();
            var s = sim.CaptureSnapshot();
            Assert.Equal(0.5, ControlValues.Current(Ok("INT_DIAL_VacuumPump_Cond_0_1"), s), 6);
            Assert.Equal(0.6, ControlValues.Current(Ok("INT_DIAL_CoolingWater_Cond_0_1"), s), 6);
            Assert.Equal(0.7, ControlValues.Current(Ok("INT_DIAL_GridDemand_Grid_0_1.5"), s), 6);
            Assert.False(s.BopAutoLoad);
        }

        [Fact]
        public void GaugeGeometry_BarTravelComesFromTheRow_AndMovesExactlyThatFar()
        {
            Assert.Equal(0.30f, GaugeGeometry.TravelM("BankInsertion"));
            Assert.Equal(GaugeGeometry.DefaultBarTravelM, GaugeGeometry.TravelM("DecayHeatPower"));
            float lo = GaugeGeometry.BarY(1.0f, 0.10f, 0f), hi = GaugeGeometry.BarY(1.0f, 0.10f, 1f);
            Assert.Equal(0.10f, hi - lo, 5);
            Assert.Equal(1.0f, (lo + hi) * 0.5f, 5); // centred on the authored rest position
            Assert.Equal(-135f, GaugeGeometry.NeedleDeg(0f)); Assert.Equal(135f, GaugeGeometry.NeedleDeg(1f));
            Assert.Equal(0f, GaugeGeometry.NeedleDeg(0.5f), 4);
        }

        // ---- U17: alarm acknowledge, control sounds, counters ----

        private static readonly string[] TwoAlarms = { "SCRAM", "BORON_LOW" };

        private static void Step(AlarmTracker t, bool scram, bool boron, int seq)
        {
            t.Update(TwoAlarms, n => n == "SCRAM" ? scram : boron, seq);
        }

        [Fact]
        public void NewlyRaisedAlarm_Flashes_ThenHoldsSteadyAfterAcknowledge_AndClearsWithTheCondition()
        {
            var t = new AlarmTracker();
            Step(t, false, false, 0);
            Assert.Equal(AlarmState.Clear, t.StateOf("SCRAM"));
            Step(t, true, false, 0);
            Assert.Equal(AlarmState.Unacked, t.StateOf("SCRAM"));   // flashing
            Assert.True(t.AnyUnacked);
            Step(t, true, false, 1);                                 // AcknowledgeAlarms applied
            Assert.Equal(AlarmState.Acked, t.StateOf("SCRAM"));     // steady
            Assert.False(t.AnyUnacked);
            Step(t, true, false, 1);
            Assert.Equal(AlarmState.Acked, t.StateOf("SCRAM"));     // stays steady while the condition holds
            Step(t, false, false, 1);
            Assert.Equal(AlarmState.Clear, t.StateOf("SCRAM"));     // clears when the condition clears
        }

        [Fact]
        public void ANewAlarmAfterAnAcknowledge_FlashesAgain_AndTheOldOneStaysSteady()
        {
            var t = new AlarmTracker();
            Step(t, true, false, 0);
            Step(t, true, false, 1);
            Assert.Equal(AlarmState.Acked, t.StateOf("SCRAM"));
            Step(t, true, true, 1);
            Assert.Equal(AlarmState.Unacked, t.StateOf("BORON_LOW"));
            Assert.Equal(AlarmState.Acked, t.StateOf("SCRAM"));
            Step(t, true, true, 2);
            Assert.Equal(AlarmState.Acked, t.StateOf("BORON_LOW"));
        }

        [Fact]
        public void AnAlarmThatClearsAndReturns_FlashesAgainEvenAfterBeingAcknowledged()
        {
            var t = new AlarmTracker();
            Step(t, true, false, 0);
            Step(t, true, false, 1);
            Step(t, false, false, 1);
            Step(t, true, false, 1);
            Assert.Equal(AlarmState.Unacked, t.StateOf("SCRAM"));
        }

        [Fact]
        public void AcknowledgeCountRestoredFromASave_DoesNotRetroAcknowledgeAnything()
        {
            var t = new AlarmTracker();
            Step(t, true, false, 7);   // first look: the count is only learned
            Assert.Equal(AlarmState.Unacked, t.StateOf("SCRAM"));
        }

        [Fact]
        public void AcknowledgeAlarms_ThroughThePlant_ReachesTheTrackerViaTheSnapshotCount()
        {
            var sim = new Simulation();
            var tracker = new AlarmTracker();
            var before = sim.Plant.CaptureState(0.0);
            Assert.Equal(0, before.AlarmAckSeq);
            sim.Plant.Scram();
            tracker.Update(new[] { "SCRAM" }, n => LampPredicates.Evaluate(n, sim.Plant.CaptureState(1.0), LampContext.Default), before.AlarmAckSeq);
            Assert.Equal(AlarmState.Unacked, tracker.StateOf("SCRAM"));
            sim.EnqueueIntent(PlantIntent.AcknowledgeAlarms("test"));
            sim.Tick();
            var after = sim.Plant.CaptureState(2.0);
            Assert.Equal(1, after.AlarmAckSeq);
            tracker.Update(new[] { "SCRAM" }, n => LampPredicates.Evaluate(n, after, LampContext.Default), after.AlarmAckSeq);
            Assert.Equal(AlarmState.Acked, tracker.StateOf("SCRAM"));
        }

        [Fact]
        public void StatusAndNoticeLamps_AreNotAlarms()
        {
            foreach (var n in new[] { "Pause", "MODE", "CRITICAL", "GEN_BREAKER_CLOSED", "GEN_BREAKER_OPEN" })
                Assert.False(LampPredicates.IsAlarm(n), n);
            foreach (var n in new[] { "SCRAM", "HIGH_FLUX", "BORON_LOW", "TURBINE_TRIPPED", "PORV_OPEN" })
                Assert.True(LampPredicates.IsAlarm(n), n);
            foreach (var n in LampPredicates.AlarmNames) Assert.True(LampPredicates.Has(n));
        }

        [Fact]
        public void AcknowledgeAlarms_IsSupportedByTheControlMapping()
        {
            var spec = Ok("INT_BTN_AcknowledgeAlarms_Alarms_1");
            Assert.Equal(IntentKind.AcknowledgeAlarms, spec.Kind);
            Assert.Equal("Alarms", PivotNameParser.DefaultTarget(IntentKind.AcknowledgeAlarms));
            Assert.True(ControlIntentFactory.IsSupported(spec));
            var intent = ControlIntentFactory.BuildButton(spec, false, 1.0, "t");
            Assert.Equal(IntentKind.AcknowledgeAlarms, intent.Kind);
            Assert.Equal(IntentClass.Player, intent.Class);
        }

        [Fact]
        public void ControlSounds_DialsTick_BreakerThunks_ButtonsClunk()
        {
            Assert.Equal(ControlSound.Tick, ControlSounds.For(Ok("INT_DIAL_DiluteRate_Boron_0_5")));
            Assert.Equal(ControlSound.Tick, ControlSounds.For(Ok("INT_DIAL_OperatingMode_Core_0_2")));
            Assert.Equal(ControlSound.Breaker, ControlSounds.For(Ok("INT_BTN_GeneratorBreaker_Generator_1")));
            Assert.Equal(ControlSound.Clunk, ControlSounds.For(Ok("INT_BTN_ResetBopTrips_BOP_1")));
            Assert.Equal(ControlSound.Clunk, ControlSounds.For(Ok("INT_BTN_AcknowledgeAlarms_Alarms_1")));
        }

        [Fact]
        public void DetentIndex_ChangesEveryFivePercent_AndEveryStepOnAStesppedKnob()
        {
            var dial = Ok("INT_DIAL_DiluteRate_Boron_0_5");
            Assert.Equal(DetentIndexOf(dial, 0.0), DetentIndexOf(dial, 0.1));        // inside the first detent (0.25 wide)
            Assert.NotEqual(DetentIndexOf(dial, 0.0), DetentIndexOf(dial, 0.4));
            Assert.Equal(ControlSounds.ContinuousDetents, DetentIndexOf(dial, 5.0));
            var mode = Ok("INT_DIAL_OperatingMode_Core_0_2");
            Assert.Equal(0, DetentIndexOf(mode, 0.0));
            Assert.Equal(1, DetentIndexOf(mode, 1.0));
            Assert.Equal(2, DetentIndexOf(mode, 2.0));
        }

        private static int DetentIndexOf(ControlSpec spec, double v) { return ControlSounds.DetentIndex(spec, v); }

        [Theory]
        [InlineData(1500.0, "01500")]
        [InlineData(0.4, "00000")]
        [InlineData(99999.0, "99999")]
        [InlineData(250000.0, "2.5E5")]
        [InlineData(3.2e8, "3.2E8")]
        [InlineData(99999.4, "99999")]
        [InlineData(99999.6, "1.0E5")]
        [InlineData(1e15, "9.9E9")]
        [InlineData(-12.0, "-0012")]
        [InlineData(-99999.0, "-9999")]
        [InlineData(double.NaN, "-----")]
        public void CounterText_ShowsFiveDigits(double v, string want)
        {
            Assert.Equal(want, CounterText.Format(v));
        }
    }
}
