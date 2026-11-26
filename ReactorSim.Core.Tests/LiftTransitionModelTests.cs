using System;
using Xunit;

namespace ReactorSim.Core.Tests
{
    // The lift's prompted, locked fade (Phase 2 U3, KTD7): one clean transfer per activation, input locked throughout,
    // arrival on the other landing facing out of the car, and no phase skipped by a long or bad frame.
    public class LiftTransitionModelTests
    {
        private const string Lower = "landing.lift_lower", Upper = "landing.lift_upper";

        private static void RunToIdle(LiftTransitionModel m, float dt)
        {
            for (int i = 0; i < 1000 && m.Phase != LiftPhase.Idle; i++) m.Step(dt);
        }

        [Fact]
        public void ActivationFromIdleStartsOneTransferToTheOtherLanding()
        {
            var m = new LiftTransitionModel();
            Assert.True(m.TryStart(Lower));
            Assert.Equal(LiftPhase.FadingOut, m.Phase);
            Assert.Equal(Upper, m.Target.Id);
            Assert.Equal(RouteGraph.DeckFloorH, m.ArrivalFloorH, 3);
        }

        [Fact]
        public void SecondActivationWhileRunningIsIgnored()
        {
            var m = new LiftTransitionModel();
            Assert.True(m.TryStart(Lower));
            m.Step(0.1f);
            Assert.False(m.TryStart(Lower));
            Assert.False(m.TryStart(Upper));
            Assert.Equal(Upper, m.Target.Id);

            int moves = 0;
            for (int i = 0; i < 1000 && m.Phase != LiftPhase.Idle; i++)
            {
                m.Step(0.05f);
                if (m.MovedThisStep) moves++;
                if (m.Phase != LiftPhase.Idle) Assert.False(m.TryStart(Upper));
            }
            Assert.Equal(1, moves);
            Assert.Equal(Upper, m.CurrentLandingId);
        }

        [Fact]
        public void InputIsLockedFromStartToArrival()
        {
            var m = new LiftTransitionModel();
            Assert.False(m.InputLocked);
            m.TryStart(Upper);
            for (int i = 0; i < 1000 && m.Phase != LiftPhase.Idle; i++)
            {
                Assert.True(m.InputLocked);
                m.Step(0.05f);
            }
            Assert.False(m.InputLocked);
            Assert.Equal(Lower, m.CurrentLandingId);
        }

        [Fact]
        public void ArrivalFacesOutOfTheCarTowardTheLandingDoorway()
        {
            foreach (var from in new[] { Lower, Upper })
            {
                var m = new LiftTransitionModel();
                m.TryStart(from);
                var to = m.Target;
                Assert.Equal(to.ExitHeadingDeg, m.ArrivalYawDeg);

                var door = RouteGraph.FindDoorway(to.DoorwayId);
                Assert.NotNull(door);
                double toDoor = Math.Atan2(door.X - to.X, door.Y - to.Y) * 180.0 / Math.PI;   // heading: from +y toward +x
                Assert.True(Math.Abs(OperatorMath.DeltaAngle((float)toDoor, m.ArrivalYawDeg)) < 45f,
                    to.Id + " arrival yaw does not face its doorway");
            }
        }

        [Fact]
        public void MoveHappensOnlyAfterTheScreenIsFullyBlack()
        {
            var m = new LiftTransitionModel(0.5f, 0.5f);
            m.TryStart(Lower);
            m.Step(0.25f);
            Assert.Equal(0.5f, m.FadeAlpha, 3);
            Assert.False(m.MovedThisStep);
            m.Step(0.25f);
            Assert.Equal(LiftPhase.Moving, m.Phase);
            Assert.Equal(1f, m.FadeAlpha);
            m.Step(0.01f);
            Assert.True(m.MovedThisStep);
            Assert.Equal(1f, m.FadeAlpha, 3);
        }

        [Fact]
        public void ALongFrameAdvancesOnlyOnePhase()
        {
            var m = new LiftTransitionModel();
            m.TryStart(Lower);
            m.Step(100f);
            Assert.Equal(LiftPhase.Moving, m.Phase);
            m.Step(100f);
            Assert.Equal(LiftPhase.FadingIn, m.Phase);
            Assert.True(m.MovedThisStep);
            m.Step(100f);
            Assert.Equal(LiftPhase.Idle, m.Phase);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void BadTimeStepNeverAdvances(float dt)
        {
            var m = new LiftTransitionModel();
            m.TryStart(Lower);
            m.Step(dt);
            Assert.Equal(LiftPhase.FadingOut, m.Phase);
            Assert.Equal(0f, m.FadeAlpha);
        }

        [Fact]
        public void UnknownLandingDoesNotStart()
        {
            var m = new LiftTransitionModel();
            Assert.False(m.TryStart("landing.nowhere"));
            Assert.False(m.TryStart(null));
            Assert.Equal(LiftPhase.Idle, m.Phase);
            Assert.False(m.InputLocked);
        }

        [Fact]
        public void NonFiniteOrNegativeFadeLengthsFallBackSafely()
        {
            var m = new LiftTransitionModel(float.NaN, -2f);
            m.TryStart(Lower);
            RunToIdle(m, 0.05f);
            Assert.Equal(Upper, m.CurrentLandingId);
        }
    }
}
