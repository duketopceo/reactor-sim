using System;
using Xunit;

namespace ReactorSim.Core.Tests
{
    // Phase 2 U2 camera maths (KTD3): shake intensity, near clip, floor following and entry facing. Pure OperatorMath only;
    // the OperatorController / CameraShake / UserSettings wiring is the engine slice (KTD9).
    public class OperatorCameraMathTests
    {
        // ---- shake intensity (R5) ----

        [Fact]
        public void ShakeIntensityZeroGivesNoOffsetAndNoRoll()
        {
            Assert.Equal(0f, OperatorMath.ScaleShake(0.07f, 0f));
            Assert.Equal(0f, OperatorMath.ScaleShake(2.2f, 0f));
        }

        [Fact]
        public void ShakeIntensityOneLeavesShakeUnchanged()
        {
            Assert.Equal(0.07f, OperatorMath.ScaleShake(0.07f, 1f));
            Assert.Equal(-2.2f, OperatorMath.ScaleShake(-2.2f, 1f));
        }

        [Fact]
        public void ShakeIntensityScalesLinearlyBetweenTheEnds()
        {
            Assert.Equal(0.5f, OperatorMath.ScaleShake(1f, 0.5f), 5);
        }

        [Theory]
        [InlineData(-0.5f, 0f)]
        [InlineData(1.7f, 1f)]
        [InlineData(0.25f, 0.25f)]
        public void ShakeIntensityOutsideZeroToOneClamps(float raw, float expected)
        {
            Assert.Equal(expected, OperatorMath.ClampShakeIntensity(raw));
        }

        [Fact]
        public void NonFiniteShakeIntensityReadsTheDefault()
        {
            Assert.Equal(OperatorMath.ShakeIntensityDefault, OperatorMath.ClampShakeIntensity(float.NaN));
            Assert.Equal(OperatorMath.ShakeIntensityDefault, OperatorMath.ClampShakeIntensity(float.PositiveInfinity));
            Assert.Equal(1f, OperatorMath.ShakeIntensityDefault);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.3f)]
        [InlineData(1f)]
        public void ShakeSliderMapsBothWays(float intensity)
        {
            float t = OperatorMath.ToSlider(intensity, OperatorMath.ShakeIntensityMin, OperatorMath.ShakeIntensityMax);
            Assert.Equal(intensity, OperatorMath.FromSlider(t, OperatorMath.ShakeIntensityMin, OperatorMath.ShakeIntensityMax), 5);
        }

        // ---- near clip (KTD3) ----

        [Fact]
        public void NearClipIsFiveCentimetresAndInsideTheBodyCapsule()
        {
            Assert.Equal(0.05f, OperatorMath.NearClip);
            Assert.True(OperatorMath.NearClip < RouteGraph.OperatorRadius + OperatorMath.BodySkinWidth,
                "the camera can never come closer to a wall than the capsule allows, so a near plane inside it never clips the wall");
        }

        // ---- floor following (KTD3) ----

        [Fact]
        public void FloorFollowsAStepUpToTheStepOffset()
        {
            Assert.Equal(OperatorMath.StepOffset, OperatorMath.FollowFloor(0f, true, OperatorMath.StepOffset), 5);
            Assert.Equal(0.1f, OperatorMath.FollowFloor(0f, true, 0.1f), 5);
        }

        [Fact]
        public void FloorIgnoresAHitHigherThanAStep()
        {
            // A desk top or railing under the cast is not a floor the body could have stepped onto.
            Assert.Equal(0f, OperatorMath.FollowFloor(0f, true, OperatorMath.StepOffset + 0.3f));
        }

        [Fact]
        public void FloorFollowsADropAndALiftFloorChangeExactly()
        {
            Assert.Equal(-0.15f, OperatorMath.FollowFloor(0f, true, -0.15f), 5);
            // The lift sets the new landing as the last floor, so the next cast on that floor keeps the eye where it is.
            float deck = RouteGraph.DeckFloorH;
            Assert.Equal(deck, OperatorMath.FollowFloor(deck, true, deck), 5);
            Assert.Equal(deck + OperatorMath.EyeHeight(false), OperatorMath.EyeY(OperatorMath.FollowFloor(deck, true, deck), false), 5);
        }

        [Fact]
        public void FloorKeepsTheLastHeightWhenTheCastFindsNothing()
        {
            Assert.Equal(1.5f, OperatorMath.FollowFloor(1.5f, false, 0f));
            Assert.Equal(1.5f, OperatorMath.FollowFloor(1.5f, true, float.NaN));
            Assert.Equal(1.5f, OperatorMath.FollowFloor(1.5f, true, float.NegativeInfinity));
        }

        // ---- entry facing (KTD3) ----

        [Fact]
        public void EntryYawThroughEveryDoorwayFacesIntoTheRoomBeingEntered()
        {
            foreach (var d in RouteGraph.Doorways)
            {
                foreach (var entering in new[] { d.FromRoomId, d.ToRoomId })
                {
                    float yaw = OperatorMath.EntryYawDeg(d, entering, -1f);
                    double rad = yaw * Math.PI / 180.0;
                    // Heading is degrees from +y toward +x (RouteGraph frame), so a step is (sin, cos).
                    float x = d.X + (float)Math.Sin(rad) * 1.0f;
                    float y = d.Y + (float)Math.Cos(rad) * 1.0f;
                    Assert.True(RouteGraph.Inside(RouteGraph.FindRoom(entering), x, y, 0.05f),
                        d.Id + ": one metre along the entry yaw is not inside " + entering);
                }
            }
        }

        [Fact]
        public void EntryYawForARoomTheDoorwayDoesNotJoinIsTheFallback()
        {
            var d = RouteGraph.FindDoorway("door.control_east");
            Assert.Equal(123f, OperatorMath.EntryYawDeg(d, "room.nowhere", 123f));
            Assert.Equal(123f, OperatorMath.EntryYawDeg(null, RouteGraph.ControlRoomId, 123f));
        }
    }
}
