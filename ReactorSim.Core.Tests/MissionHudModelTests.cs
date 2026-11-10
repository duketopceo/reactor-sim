using ReactorSim.Ui;
using Xunit;

namespace ReactorSim.Core.Tests
{
    public class MissionHudModelTests
    {
        [Fact]
        public void Sandbox_ShowsNothing()
        {
            var m = MissionHudModel.Build(false, "Cold start", 0, 3, "Pumps", false, "advice");
            Assert.False(m.Visible);
        }

        [Fact]
        public void NoMission_ShowsNothing()
        {
            Assert.False(MissionHudModel.Build(true, null, -1, 0, null, false, "advice").Visible);
            Assert.False(MissionHudModel.Build(true, "", -1, 0, null, false, null).Visible);
        }

        [Fact]
        public void ActiveMission_ShowsTitleStepNOfMAndAdvisory_WithoutDebugLabels()
        {
            var m = MissionHudModel.Build(true, "Cold start", 1, 5, "Raise boron", false, "[ 4.0m OPS] check flow");
            Assert.True(m.Visible);
            Assert.Equal("Cold start", m.Title);
            Assert.Equal("Step 2 of 5: Raise boron", m.Step);
            Assert.Equal("[ 4.0m OPS] check flow", m.Advisory);
            foreach (var text in new[] { m.Title, m.Step, m.Advisory })
            {
                Assert.DoesNotContain("U8", text);
                Assert.DoesNotContain("Tier", text);
            }
        }

        [Fact]
        public void CompletedMission_SaysSoInPlainWords()
        {
            var m = MissionHudModel.Build(true, "Cold start", 5, 5, null, true, null);
            Assert.True(m.Complete);
            Assert.Equal("Mission complete", m.Step);
            Assert.Equal("", m.Advisory);
        }

        [Fact]
        public void LongText_IsCutToTheLineBudget_AndLosesMarkupAndNewlines()
        {
            string longText = new string('x', 200);
            var m = MissionHudModel.Build(true, longText, 0, 2, longText, false, "<b>bold</b>\nsecond");
            Assert.True(m.Title.Length <= MissionHudModel.LineBudget);
            Assert.True(m.Step.Length <= MissionHudModel.LineBudget);
            Assert.EndsWith("..", m.Title);
            Assert.DoesNotContain("<", m.Advisory);
            Assert.DoesNotContain("\n", m.Advisory);
        }
    }
}
