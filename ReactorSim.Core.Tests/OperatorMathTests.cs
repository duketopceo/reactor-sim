using Xunit;

namespace ReactorSim.Core.Tests
{
    // Pure clamp, speed, anchor and range maths of the walkable operator (OperatorController, KTD4).
    public class OperatorMathTests
    {
        [Fact]
        public void PitchBeyondTheLimitClampsToTheLimit()
        {
            Assert.Equal(OperatorMath.MaxPitch, OperatorMath.ClampPitch(120f));
            Assert.Equal(OperatorMath.MinPitch, OperatorMath.ClampPitch(-120f));
            Assert.Equal(10f, OperatorMath.ClampPitch(10f));
        }

        [Fact]
        public void SeatedYawClampsToTheSeatRange()
        {
            float range = OperatorMath.SeatedYawRange;
            Assert.Equal(-65f + range, OperatorMath.ClampYaw(-65f + range + 40f, -65f, range), 3);
            Assert.Equal(-65f - range, OperatorMath.ClampYaw(-65f - range - 40f, -65f, range), 3);
            Assert.Equal(-50f, OperatorMath.ClampYaw(-50f, -65f, range), 3);
        }

        [Fact]
        public void SeatedYawClampWorksAcrossTheWrapAround()
        {
            // base 170, input 200 is 30 deg to the right of base: inside the range, unchanged.
            Assert.Equal(200f, OperatorMath.ClampYaw(200f, 170f, 110f), 3);
            // input 350 is 180 deg away: clamps to base + range.
            Assert.Equal(170f + 110f, OperatorMath.ClampYaw(350f, 170f, 110f), 3);
        }

        [Fact]
        public void StandingYawIsFree()
        {
            Assert.Equal(300f, OperatorMath.ClampYaw(300f, 0f, 180f));
        }

        [Fact]
        public void WalkSpeedIsZeroWhileSeatedOrDragging()
        {
            Assert.Equal(0f, OperatorMath.EffectiveWalkSpeed(true, false, 1.6f));
            Assert.Equal(0f, OperatorMath.EffectiveWalkSpeed(false, true, 1.6f));
            Assert.Equal(0f, OperatorMath.EffectiveWalkSpeed(true, true, 1.6f));
            Assert.Equal(1.6f, OperatorMath.EffectiveWalkSpeed(false, false, 1.6f));
        }

        [Fact]
        public void StandingUpFromTheSeatPutsTheEyeAt165OverTheSeatAnchor()
        {
            float seatAnchorFloorY = 0f;
            Assert.Equal(1.65f, OperatorMath.EyeY(seatAnchorFloorY, false), 4);
            Assert.Equal(1.2f, OperatorMath.EyeY(seatAnchorFloorY, true), 4);
            Assert.Equal(3.65f, OperatorMath.EyeY(2f, false), 4);
        }

        [Fact]
        public void InteractionRangeIsTwoMetres()
        {
            Assert.True(OperatorMath.InInteractionRange(1.9f));
            Assert.False(OperatorMath.InInteractionRange(2.1f));
            Assert.True(OperatorMath.InInteractionRange(OperatorMath.InteractionRange));
        }

        [Fact]
        public void SitPromptNeedsAChairWithinOnePointFiveMetresAndInView()
        {
            Assert.True(OperatorMath.SitPromptVisible(1.0f, 20f));
            Assert.False(OperatorMath.SitPromptVisible(1.6f, 0f));   // too far
            Assert.False(OperatorMath.SitPromptVisible(1.0f, 100f)); // behind the view
        }

        [Fact]
        public void SitPromptShowsWhenStandingOnTheChair()
        {
            // The chair is under the eye, out of any view cone: standing at the anchor still offers it.
            Assert.True(OperatorMath.SitPromptVisible(0.2f, 90f));
        }

        // ---- U17: quick travel, swivel, settings ----

        [Fact]
        public void QuickTravelKeys_RunInBoardOrder()
        {
            Assert.Equal("STAND_reactor", OperatorMath.StandAnchorForKey(1));
            Assert.Equal("STAND_safety", OperatorMath.StandAnchorForKey(3));
            Assert.Equal("STAND_electrical", OperatorMath.StandAnchorForKey(6));
            Assert.Null(OperatorMath.StandAnchorForKey(0));
            Assert.Null(OperatorMath.StandAnchorForKey(7));
        }

        [Fact]
        public void Key3_MovesToStandSafety_FacingTheBoard()
        {
            // Anchors come from design/control-room/layout.json (room x east, y front-back = Unity x, z).
            var layout = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "design", "control-room", "layout.json"))).RootElement;
            var ops = layout.GetProperty("operating_points");
            string name = OperatorMath.StandAnchorForKey(3);
            var anchor = ops.GetProperty(name);
            var centre = ops.GetProperty("STAND_Center");
            float px = (float)anchor.GetProperty("pos")[0].GetDouble(), pz = (float)anchor.GetProperty("pos")[1].GetDouble();
            float cx = (float)centre.GetProperty("pos")[0].GetDouble(), cz = (float)centre.GetProperty("pos")[1].GetDouble();
            float yaw = OperatorMath.OutwardYaw(px, pz, cx, cz, 0f);
            float facing = (float)anchor.GetProperty("facing_deg").GetDouble();   // heading from +y toward +x = Unity yaw
            Assert.Equal("STAND_safety", name);
            Assert.True(System.Math.Abs(OperatorMath.DeltaAngle(facing, yaw)) < 1.5f,
                "key 3 faces " + yaw + " deg, the board is at " + facing + " deg");
        }

        [Fact]
        public void EveryQuickTravelStop_FacesTheBoard()
        {
            var layout = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "design", "control-room", "layout.json"))).RootElement;
            var ops = layout.GetProperty("operating_points");
            var centre = ops.GetProperty("STAND_Center").GetProperty("pos");
            for (int n = 1; n <= 6; n++)
            {
                var a = ops.GetProperty(OperatorMath.StandAnchorForKey(n));
                float yaw = OperatorMath.OutwardYaw((float)a.GetProperty("pos")[0].GetDouble(), (float)a.GetProperty("pos")[1].GetDouble(),
                    (float)centre[0].GetDouble(), (float)centre[1].GetDouble(), 0f);
                Assert.True(System.Math.Abs(OperatorMath.DeltaAngle((float)a.GetProperty("facing_deg").GetDouble(), yaw)) < 1.5f, "key " + n);
            }
        }

        [Fact]
        public void OutwardYaw_OnTheCentre_UsesTheFallback()
        {
            Assert.Equal(33f, OperatorMath.OutwardYaw(6f, 4.5f, 6f, 4.5f, 33f));
            Assert.Equal(90f, OperatorMath.OutwardYaw(7f, 4.5f, 6f, 4.5f, 0f), 3);
        }

        [Fact]
        public void Swivel_TurnsAtTheSwivelRate_AndNeverPastTheSeatedClamp()
        {
            float range = OperatorMath.SeatedYawRange;
            Assert.Equal(-65f + OperatorMath.SwivelDegPerSec * 0.1f, OperatorMath.Swivel(-65f, -65f, 1, 0.1f, range), 3);
            Assert.Equal(-65f - OperatorMath.SwivelDegPerSec * 0.1f, OperatorMath.Swivel(-65f, -65f, -1, 0.1f, range), 3);
            // Held for a long time: pinned at the clamp on either side.
            float yaw = -65f;
            for (int i = 0; i < 200; i++) yaw = OperatorMath.Swivel(yaw, -65f, 1, 0.05f, range);
            Assert.Equal(-65f + range, yaw, 3);
            for (int i = 0; i < 400; i++) yaw = OperatorMath.Swivel(yaw, -65f, -1, 0.05f, range);
            Assert.Equal(-65f - range, yaw, 3);
            Assert.Equal(-40f, OperatorMath.Swivel(-40f, -65f, 0, 0.1f, range), 3);
        }

        [Fact]
        public void Settings_ClampToTheirRanges_AndMapToTheSlider()
        {
            Assert.Equal(OperatorMath.TextScaleMax, OperatorMath.ClampTextScale(9f));
            Assert.Equal(OperatorMath.FovMin, OperatorMath.ClampFov(10f));
            Assert.Equal(OperatorMath.SensitivityDefault, OperatorMath.ClampSensitivity(float.NaN));
            Assert.Equal(OperatorMath.FovMin, OperatorMath.FromSlider(0f, OperatorMath.FovMin, OperatorMath.FovMax));
            Assert.Equal(OperatorMath.FovMax, OperatorMath.FromSlider(2f, OperatorMath.FovMin, OperatorMath.FovMax));
            Assert.Equal(0.5f, OperatorMath.ToSlider(70f, 50f, 90f), 3);
            Assert.True(OperatorMath.TextScaleDefault >= OperatorMath.TextScaleMin && OperatorMath.TextScaleDefault <= OperatorMath.TextScaleMax);
            Assert.True(OperatorMath.FovDefault >= OperatorMath.FovMin && OperatorMath.FovDefault <= OperatorMath.FovMax);
        }
    }
}
