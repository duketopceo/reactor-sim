using System;
using Xunit;
using ReactorSim.Controls;

namespace ReactorSim.Core.Tests
{
    // The click-hold-drag gesture of a dial, its tuning and the feedback strings (playtest-2 U2).
    public class DialDragTests
    {
        private static ControlSpec Ok(string name)
        {
            var r = PivotNameParser.Parse(name);
            Assert.True(r.Status == ParseStatus.Ok, name + " -> " + r.Status + " " + r.Error);
            return r.Spec;
        }

        // Continuous dial: 400 px per full sweep, 6 px dead zone, fine = 0.2.
        private static DialDrag Continuous(double start) { return new DialDrag(start, 400.0, 6.0, 0.2, 0); }

        [Fact]
        public void PressAndReleaseWithNoMotion_ChangesNothing()
        {
            var d = Continuous(0.4);
            Assert.Equal(0.4, d.Release());
            Assert.False(d.Started);
            Assert.Equal(0.4, d.Fraction);
        }

        [Fact]
        public void JitterInsideTheDeadZone_ChangesNothing()
        {
            var d = Continuous(0.4);
            d.Move(3, false); d.Move(-2, false); d.Move(4, false);
            Assert.False(d.Started);
            Assert.Equal(0.4, d.Release());
        }

        [Fact]
        public void UpwardTravelRaisesByTheDerivedFraction_DownwardLowersByTheSame()
        {
            var up = Continuous(0.5);
            up.Move(6.5, false);              // crosses the dead zone, nothing applied yet
            Assert.True(up.Started);
            Assert.Equal(0.5, up.Fraction);
            up.Move(100, false);
            Assert.Equal(0.5 + 100.0 / 400.0, up.Fraction, 12);

            var down = Continuous(0.5);
            down.Move(-6.5, false);
            down.Move(-100, false);
            Assert.Equal(0.5 - 100.0 / 400.0, down.Fraction, 12);
        }

        [Fact]
        public void TheDeadZoneDistanceIsNotAppliedAsAJump()
        {
            var d = Continuous(0.5);
            d.Move(5, false);
            Assert.Equal(0.5, d.Fraction);
            d.Move(5, false);                 // total 10 px > 6 px: now a drag, but still no change yet
            Assert.True(d.Started);
            Assert.Equal(0.5, d.Fraction);
            d.Move(40, false);
            Assert.Equal(0.6, d.Fraction, 12);
        }

        [Fact]
        public void TheFineModifierScalesOnlySubsequentDeltas()
        {
            var d = Continuous(0.5);
            d.Move(10, false);                // start
            d.Move(40, false);                // coarse: +0.1
            Assert.Equal(0.6, d.Fraction, 12);
            d.Move(40, true);                 // fine: +0.1 * 0.2 = +0.02
            Assert.Equal(0.62, d.Fraction, 12);
            d.Move(40, false);                // back to coarse
            Assert.Equal(0.72, d.Fraction, 12);
        }

        [Fact]
        public void CancelRestoresTheExactPreDragValue()
        {
            double start = 0.1234567890123;
            var d = Continuous(start);
            d.Move(10, false);
            d.Move(150, false);
            Assert.NotEqual(start, d.Fraction);
            Assert.Equal(start, d.Cancel());
            Assert.Equal(start, d.Fraction);
            Assert.True(d.Cancelled);
            Assert.True(d.Finished);
        }

        [Fact]
        public void AfterTheDragEnds_FurtherMovesAreIgnored()
        {
            var d = Continuous(0.5);
            d.Move(10, false); d.Move(40, false);
            double committed = d.Release();
            Assert.False(d.Move(100, false));
            Assert.Equal(committed, d.Fraction);
            var c = Continuous(0.5);
            c.Move(10, false);
            c.Cancel();
            Assert.False(c.Move(100, false));
            Assert.Equal(0.5, c.Fraction);
        }

        [Fact]
        public void ClampsAtBothEnds_AndAVeryLargeDeltaDoesNotOverrun()
        {
            var d = Continuous(0.5);
            d.Move(10, false);
            d.Move(1e9, false);
            Assert.Equal(1.0, d.Fraction);
            d.Move(-1e12, false);
            Assert.Equal(0.0, d.Fraction);
            d.Move(5, false);
            Assert.True(d.Fraction >= 0.0 && d.Fraction <= 1.0);
        }

        [Fact]
        public void NaNAndInfiniteDeltasAreIgnored()
        {
            var d = Continuous(0.5);
            Assert.False(d.Move(double.NaN, false));
            Assert.False(d.Move(double.PositiveInfinity, false));
            Assert.False(d.Move(double.NegativeInfinity, true));
            Assert.False(d.Started);
            d.Move(10, false);
            d.Move(40, false);
            double before = d.Fraction;
            Assert.False(d.Move(double.NaN, false));
            Assert.Equal(before, d.Fraction);
        }

        [Fact]
        public void ANaNStartFractionFallsBackToTheMiddle()
        {
            Assert.Equal(0.5, Continuous(double.NaN).Fraction);
            Assert.Equal(1.0, Continuous(7.0).Fraction);
        }

        [Fact]
        public void AStepKnobSnapsToDetents_AndEachCrossingIsCountedOnce()
        {
            var spec = Ok("INT_DIAL_OperatingMode_Core_0_2");
            Assert.True(spec.Discrete);
            // Two detents (0, 1, 2); 40 px per detent by default -> 80 px per sweep, raised to the 120 px minimum.
            var d = DialDrag.For(spec, 0.0, new InputTuning());
            d.Move(10, false);
            Assert.Equal(0, d.DetentIndex);
            int seenValue = -1;
            for (int i = 0; i < 40; i++)
            {
                d.Move(10, false);
                int v = (int)DragMapper.ValueFromFraction(spec, d.Fraction);
                Assert.True(v >= seenValue, "value never goes backwards while dragging up");
                seenValue = v;
            }
            Assert.Equal(2, seenValue);
            Assert.Equal(2, d.DetentCrossings);          // 0 -> 1 and 1 -> 2, each exactly once
            Assert.Equal(2, d.DetentIndex);
            // Wiggling inside a detent does not add crossings.
            d.Move(-1, false); d.Move(1, false);
            Assert.Equal(2, d.DetentCrossings);
        }

        [Fact]
        public void PixelsPerFullRange_IsDerivedFromTheControlAndScaledByTheGain()
        {
            var cont = Ok("INT_DIAL_PrimaryFlow_Primary_0_1");
            var step = Ok("INT_DIAL_OperatingMode_Core_0_2");
            Assert.Equal(400.0, DialDrag.PixelsPerFullRange(cont, 1.0, 400.0, 40.0));
            Assert.Equal(200.0, DialDrag.PixelsPerFullRange(cont, 2.0, 400.0, 40.0));
            Assert.Equal(120.0, DialDrag.PixelsPerFullRange(step, 1.0, 400.0, 40.0));   // 2 detents * 40 = 80, floored at 120
            var tenSteps = Ok("INT_DIAL_AssistTier_Assistant_0_9");
            Assert.Equal(360.0, DialDrag.PixelsPerFullRange(tenSteps, 1.0, 400.0, 40.0));
            Assert.Equal(400.0, DialDrag.PixelsPerFullRange(cont, double.NaN, 400.0, 40.0));
            Assert.Equal(1200.0, DialDrag.PixelsPerFullRange(cont, 1.0, 99999.0, 40.0));
        }

        // ---- tuning file ----

        [Fact]
        public void TheShippedInputTuningFileMatchesTheInCodeDefaults()
        {
            string path = TestRepo.Find("ReactorSim.Unity", "Assets", "StreamingAssets", "input-tuning.json");
            var t = InputTuning.Parse(System.IO.File.ReadAllText(path));
            var d = new InputTuning();
            Assert.Equal(d.DragGain, t.DragGain);
            Assert.Equal(d.DragDeadZonePixels, t.DragDeadZonePixels);
            Assert.Equal(d.DragFineFactor, t.DragFineFactor);
            Assert.Equal(d.DragContinuousPixels, t.DragContinuousPixels);
            Assert.Equal(d.DragDetentPixels, t.DragDetentPixels);
            Assert.Equal(d.WalkSpeed, t.WalkSpeed, 6);
            Assert.Equal(d.SprintMultiplier, t.SprintMultiplier, 6);
            Assert.Equal(d.AccelSeconds, t.AccelSeconds, 6);
            Assert.Equal(d.DecelSeconds, t.DecelSeconds, 6);
        }

        [Fact]
        public void InputTuning_OverridesSomeKeys_KeepsTheRestAtTheirDefaults()
        {
            var t = InputTuning.Parse("{ \"walkSpeed\": 2.2, \"dragGain\": 1.5 }");
            Assert.Equal(2.2, t.WalkSpeed, 6);
            Assert.Equal(1.5, t.DragGain, 6);
            Assert.Equal(DialDrag.DefaultDeadZonePixels, t.DragDeadZonePixels);
            Assert.Equal(1.0, InputTuning.Parse("").DragGain);
        }

        [Theory]
        [InlineData("{\"walkSped\": 2}")]
        [InlineData("{\"walkSpeed\": 0}")]
        [InlineData("{\"dragFineFactor\": 1.5}")]
        [InlineData("{\"dragGain\": -1}")]
        [InlineData("{\"sprintMultiplier\": 0.5}")]
        [InlineData("{\"accelSeconds\": \"fast\"}")]
        public void InputTuning_RejectsUnknownKeysAndBadValues(string json)
        {
            Assert.Throws<InputTuningException>(() => InputTuning.Parse(json));
        }

        // ---- hit area ----

        [Fact]
        public void HitArea_TwentyEightPixelsAt13mAnd70Deg900px_IsAboutSixCentimetres()
        {
            // 28 px * 2 * 1.3 m * tan 35 deg / 900 = 0.0566 m
            Assert.Equal(0.0566, HitArea.MetresForPixels(28, 1.3, 70, 900), 3);
            Assert.Equal(0.0566, HitArea.MinSideMetres(70, 900), 3);
        }

        [Fact]
        public void HitArea_IsCappedSoNeighboursDoNotOverlap_AndSurvivesBadInput()
        {
            Assert.Equal(HitArea.MaxSideMetres, HitArea.MinSideMetres(70, 300));
            Assert.Equal(0.0, HitArea.MetresForPixels(28, 1.3, 70, 0));
            Assert.True(HitArea.MinSideMetres(70, 1080) < HitArea.MinSideMetres(70, 900));
        }

        // ---- feedback strings ----

        private static RuntimeInventory Inv()
        {
            return new RuntimeInventory
            {
                rows = new[]
                {
                    new RuntimeRow { id = "pzr_level_ctl", label = "Pressurizer level demand", role = "control" },
                    new RuntimeRow { id = "cooling_water_ctl", label = "Cooling water flow", role = "control" },
                    new RuntimeRow { id = "pzr_level", label = "Pressurizer level", role = "indicator" },
                    new RuntimeRow { id = "cooling_water_flow", label = "Cooling water flow", role = "indicator" },
                    new RuntimeRow { id = "primary_pressure", label = "Primary pressure", role = "indicator" },
                }
            };
        }

        [Fact]
        public void ClickOnAnIndicator_NamesTheKnobThatOperatesIt()
        {
            var inv = Inv();
            Assert.Equal("Indicator. Right-click to pin. Operate it with the Pressurizer level demand knob",
                ControlTooltipModel.IndicatorNote(ControlTooltipModel.RelatedControl(inv, "pzr_level")));
            Assert.Equal("Indicator. Right-click to pin. Operate it with the Cooling water flow knob",
                ControlTooltipModel.IndicatorNote(ControlTooltipModel.RelatedControl(inv, "cooling_water_flow")));
        }

        [Fact]
        public void ClickOnAnIndicatorWithNoKnob_StillSaysWhatItIs()
        {
            var inv = Inv();
            Assert.Null(ControlTooltipModel.RelatedControl(inv, "primary_pressure"));
            string note = ControlTooltipModel.IndicatorNote(null);
            Assert.StartsWith("Indicator.", note);
            Assert.Contains("pin", note);
            Assert.Null(ControlTooltipModel.RelatedControl(null, "pzr_level"));
            Assert.Null(ControlTooltipModel.RelatedControl(inv, ""));
        }

        [Fact]
        public void DragReadout_ShowsLabelValueWithUnitAndTheChangeSinceThePress()
        {
            Assert.Equal("Rod bank A target\n0.62 frac   (+0.12)", ControlTooltipModel.DragReadout("Rod bank A target", 0.62, 0.5, "frac", false));
            Assert.Equal("Dilute rate\n1.5 ppm/s   (-0.5)", ControlTooltipModel.DragReadout("Dilute rate", 1.5, 2.0, "ppm/s", false));
            Assert.Equal("Operating mode\n2   (+1)", ControlTooltipModel.DragReadout("Operating mode", 2.0, 1.0, "", true));
            Assert.Equal("X\n3 m   (0)", ControlTooltipModel.DragReadout("X", 3.0, 3.0, "m", false));
        }
    }
}
