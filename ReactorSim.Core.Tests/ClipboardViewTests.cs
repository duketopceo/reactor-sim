using System.Collections.Generic;
using System.Linq;
using ReactorSim.Clipboard;
using Xunit;

namespace ReactorSim.Tests
{
    // U16: the display model behind the procedure clipboard. Pure: ProcedureState -> lines.
    public class ClipboardViewTests
    {
        private const string Json = @"{ ""procedures"": [ { ""id"": ""p"", ""title"": ""Test proc"", ""steps"": [
            { ""id"": ""s1"", ""text"": ""Pumps"",  ""rows"": [""a"", ""b""], ""when"": ""primary_flow_at_least"", ""value"": 0.8 },
            { ""id"": ""s2"", ""text"": ""Heaters"", ""rows"": [""c""],       ""when"": ""primary_pressure_at_least"", ""value"": 60 },
            { ""id"": ""s3"", ""text"": ""Vacuum"",  ""rows"": [""d"", ""e""], ""when"": ""condenser_pressure_at_most"", ""value"": 0.3 } ] } ] }";

        private static Procedure Proc() { return ProcedureLoader.Parse(Json)[0]; }

        private static PlantSnapshot AllDone()
        {
            return new PlantSnapshot { PrimaryFlowFrac = 1.0, PrimaryPressureBar = 155, CondenserPressureBar = 0.08 };
        }

        [Fact]
        public void CurrentStepLine_IsMarkedAndOthersAreNot()
        {
            var p = Proc();
            var lines = ClipboardView.Lines(p, p.Evaluate(new PlantSnapshot()));
            Assert.Equal(3, lines.Count);
            Assert.True(lines[0].Current);
            Assert.False(lines[1].Current);
            Assert.False(lines[2].Current);
            Assert.Equal("[>]", lines[0].Mark);
            Assert.Equal("[ ]", lines[1].Mark);
            Assert.Equal(1, lines.Count(l => l.Current));
        }

        [Fact]
        public void TickedSteps_ShowTicks_AndCurrentMovesOn()
        {
            var p = Proc();
            var lines = ClipboardView.Lines(p, p.Evaluate(new PlantSnapshot { PrimaryFlowFrac = 1.0 }));
            Assert.True(lines[0].Ticked);
            Assert.Equal("[x]", lines[0].Mark);
            Assert.False(lines[0].Current);
            Assert.True(lines[1].Current);
            Assert.Equal("[>]", lines[1].Mark);
            Assert.Equal("Pumps", lines[0].Text);
            Assert.Equal(2, lines[1].Number);
        }

        [Fact]
        public void CompleteProcedure_AllTicked_NoCurrent_AndSaysSo()
        {
            var p = Proc();
            var st = p.Evaluate(AllDone());
            var lines = ClipboardView.Lines(p, st);
            Assert.All(lines, l => Assert.True(l.Ticked));
            Assert.DoesNotContain(lines, l => l.Current);
            Assert.Contains("complete", ClipboardView.Header(p, st).ToLowerInvariant());
        }

        [Fact]
        public void Header_CountsTickedSteps()
        {
            var p = Proc();
            Assert.Equal("Test proc  0/3", ClipboardView.Header(p, p.Evaluate(new PlantSnapshot())));
            Assert.Equal("Test proc  1/3", ClipboardView.Header(p, p.Evaluate(new PlantSnapshot { PrimaryFlowFrac = 1.0 })));
        }

        [Fact]
        public void HighlightedRows_EqualTheCurrentStepsRows_AndTrackTheCurrentStep()
        {
            var p = Proc();
            Assert.Equal(new[] { "a", "b" }, ClipboardView.HighlightRows(p.Evaluate(new PlantSnapshot())));
            Assert.Equal(new[] { "c" }, ClipboardView.HighlightRows(p.Evaluate(new PlantSnapshot { PrimaryFlowFrac = 1.0 })));
            Assert.Empty(ClipboardView.HighlightRows(p.Evaluate(AllDone())));
        }

        private const string RichJson = @"{ ""procedures"": [ { ""id"": ""p"", ""title"": ""Rich"", ""steps"": [
            { ""id"": ""s1"", ""text"": ""Pumps"", ""phase"": ""A. Lineup"", ""control"": ""Pump knob"", ""target"": ""80 % or more"", ""rows"": [""a""], ""when"": ""primary_flow_at_least"", ""value"": 0.8 },
            { ""id"": ""s2"", ""text"": ""Boron"", ""phase"": ""B. Approach"", ""control"": ""Dilute knob"", ""target"": ""1200 ppm"", ""rows"": [""b""], ""when"": ""boron_at_most"", ""value"": 1200 },
            { ""id"": ""s3"", ""text"": ""Vac"", ""rows"": [""c""], ""when"": ""condenser_pressure_at_most"", ""value"": 0.3 } ] } ] }";

        [Fact]
        public void Lines_CarryPhaseControlAndTarget_EmptyWhenAbsent()
        {
            var p = ProcedureLoader.Parse(RichJson)[0];
            var lines = ClipboardView.Lines(p, p.Evaluate(new PlantSnapshot()));
            Assert.Equal("A. Lineup", lines[0].Phase);
            Assert.Equal("Pump knob", lines[0].Control);
            Assert.Equal("80 % or more", lines[0].Target);
            Assert.Equal("", lines[2].Phase);
            Assert.Equal("", lines[2].Control);
            Assert.Equal("", lines[2].Target);
        }

        [Fact]
        public void LiveValue_OnlyOnCurrentLine_ShowsTheReadingForThatSteps_Predicate()
        {
            var p = ProcedureLoader.Parse(RichJson)[0];
            var snap = new PlantSnapshot { PrimaryFlowFrac = 0.375 };
            var lines = ClipboardView.Lines(p, p.Evaluate(snap), snap);
            Assert.Equal("37.5 %", lines[0].LiveValue);
            Assert.Equal("", lines[1].LiveValue);
            Assert.Equal("", lines[2].LiveValue);

            snap = new PlantSnapshot { PrimaryFlowFrac = 1.0, BoronPpm = 1347.25 };
            lines = ClipboardView.Lines(p, p.Evaluate(snap), snap);
            Assert.Equal("", lines[0].LiveValue);
            Assert.Equal("1347 ppm", lines[1].LiveValue);
        }

        [Fact]
        public void LiveValue_IsCultureInvariant()
        {
            var p = ProcedureLoader.Parse(RichJson)[0];
            var snap = new PlantSnapshot { PrimaryFlowFrac = 0.375 };
            var old = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.Equal("37.5 %", ClipboardView.Lines(p, p.Evaluate(snap), snap)[0].LiveValue);
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = old; }
        }

        [Fact]
        public void LiveValue_CoversEveryPredicateInTheShippedProcedure()
        {
            var proc = ProcedureLoader.LoadFromFile(TestRepo.Find("ReactorSim.Unity", "Assets", "StreamingAssets", "procedures.json")).Single();
            foreach (var step in proc.Steps)
                Assert.False(string.IsNullOrEmpty(ClipboardView.LiveValue(step, new PlantSnapshot())), step.Id + " has no live value");
        }

        [Fact]
        public void ShippedProcedures_AllRenderOneCurrentLineOnColdPlant()
        {
            var all = ProcedureLoader.LoadFromFile(TestRepo.Find("ReactorSim.Unity", "Assets", "StreamingAssets", "procedures.json"));
            foreach (var p in all)
            {
                var lines = ClipboardView.Lines(p, p.Evaluate(new PlantSnapshot()));
                Assert.Equal(p.Steps.Count, lines.Count);
                Assert.Equal(1, lines.Count(l => l.Current));
            }
        }

        // ---- U5: the model behind the always-on startup checklist panel ----

        [Fact]
        public void Hud_ReportsCurrentStepPhaseControlTargetAndLiveValue()
        {
            var p = ProcedureLoader.Parse(RichJson)[0];
            var snap = new PlantSnapshot { PrimaryFlowFrac = 1.0, BoronPpm = 1347.25 };
            var hud = ClipboardView.Hud(p, p.Evaluate(snap), snap);
            Assert.False(hud.Complete);
            Assert.Equal(2, hud.StepNumber);
            Assert.Equal(3, hud.StepCount);
            Assert.Equal("B. Approach", hud.Phase);
            Assert.Equal("Boron", hud.Action);
            Assert.Equal("Dilute knob", hud.Control);
            Assert.Equal("1200 ppm", hud.Target);
            Assert.Equal("1347 ppm", hud.LiveValue);
            Assert.False(hud.Ticked);
            Assert.Equal("[>]", hud.Mark);
            Assert.Equal(new[] { "b" }, hud.HighlightRows);
        }

        [Fact]
        public void Hud_Reading_ShowsLiveValueAgainstTarget_WithoutCultureDependence()
        {
            var p = ProcedureLoader.Parse(RichJson)[0];
            var snap = new PlantSnapshot { PrimaryFlowFrac = 0.375 };
            var old = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var hud = ClipboardView.Hud(p, p.Evaluate(snap), snap);
                Assert.Equal("37.5 % (target 80 % or more)", hud.Reading);
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = old; }
        }

        [Fact]
        public void Hud_Reading_FallsBackWhenTargetOrLiveValueIsMissing()
        {
            var p = ProcedureLoader.Parse(RichJson)[0];
            // Step 3 has no target; its live value alone is the reading.
            var snap = new PlantSnapshot { PrimaryFlowFrac = 1.0, BoronPpm = 100, CondenserPressureBar = 0.5 };
            var hud = ClipboardView.Hud(p, p.Evaluate(snap), snap);
            Assert.Equal(3, hud.StepNumber);
            Assert.Equal("0.50 bar", hud.Reading);
            // No snapshot: the target alone.
            hud = ClipboardView.Hud(p, p.Evaluate(new PlantSnapshot()), null);
            Assert.Equal("", hud.LiveValue);
            Assert.Equal("target 80 % or more", hud.Reading);
        }

        [Fact]
        public void Hud_AtCompletion_ReportsCompleteAndNoHighlightedRows()
        {
            var p = Proc();
            var snap = AllDone();
            var hud = ClipboardView.Hud(p, p.Evaluate(snap), snap);
            Assert.True(hud.Complete);
            Assert.Equal("On the grid", hud.Action);
            Assert.Equal(3, hud.StepNumber);
            Assert.Equal(3, hud.StepCount);
            Assert.True(hud.Ticked);
            Assert.Equal("[x]", hud.Mark);
            Assert.Empty(hud.HighlightRows);
            Assert.Equal("", hud.LiveValue);
        }

        [Fact]
        public void RegressionNote_NamesTheStepThatUnticked_OnlyWhenCurrentMovesBackward()
        {
            var p = ProcedureLoader.Parse(RichJson)[0];
            var two = p.Evaluate(new PlantSnapshot { PrimaryFlowFrac = 1.0 });   // current index 1
            var zero = p.Evaluate(new PlantSnapshot());                            // current index 0
            var note = ClipboardView.RegressionNote(p, two.CurrentIndex, zero);
            Assert.NotNull(note);
            Assert.Contains("Pumps", note);
            Assert.Contains("1", note);
            Assert.Null(ClipboardView.RegressionNote(p, zero.CurrentIndex, two));            // forward
            Assert.Null(ClipboardView.RegressionNote(p, two.CurrentIndex, two));             // unchanged
            Assert.Null(ClipboardView.RegressionNote(p, ClipboardView.NoPrevious, zero));    // first look
        }

        [Fact]
        public void RegressionNote_FromCompleteBackToAStep_IsReported()
        {
            var p = Proc();
            var done = p.Evaluate(AllDone());
            var tripped = p.Evaluate(new PlantSnapshot { PrimaryFlowFrac = 1.0, PrimaryPressureBar = 155, CondenserPressureBar = 0.9 });
            Assert.True(done.Complete);
            var note = ClipboardView.RegressionNote(p, done.CurrentIndex, tripped);
            Assert.NotNull(note);
            Assert.Contains("Vacuum", note);
        }
    }
}
