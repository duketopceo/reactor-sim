using Xunit;

namespace ReactorSim.Core.Tests
{
    // The operator body's pose (Phase 2 U2, KTD6): seated or standing, matching the existing seat/stand behavior.
    public class OperatorPoseModelTests
    {
        [Fact]
        public void SeatedOperatorIsInTheSeatedPose()
        {
            Assert.Equal(OperatorPose.Seated, OperatorPoseModel.PoseFor(true));
        }

        [Fact]
        public void StandingOperatorIsInTheStandingPose()
        {
            Assert.Equal(OperatorPose.Standing, OperatorPoseModel.PoseFor(false));
        }

        [Fact]
        public void PoseEyeHeightMatchesTheControllerEyeHeight()
        {
            Assert.Equal(OperatorMath.EyeHeight(true), OperatorPoseModel.EyeHeight(OperatorPose.Seated));
            Assert.Equal(OperatorMath.EyeHeight(false), OperatorPoseModel.EyeHeight(OperatorPose.Standing));
        }

        [Fact]
        public void BlobShadowIsWiderStandingThanSeatedAndAlwaysPositive()
        {
            float seated = OperatorPoseModel.ShadowRadius(OperatorPose.Seated);
            float standing = OperatorPoseModel.ShadowRadius(OperatorPose.Standing);
            Assert.True(seated > 0f);
            Assert.True(standing >= seated);
            Assert.True(standing >= ReactorSim.RouteGraph.OperatorRadius, "the shadow covers the body capsule's footprint");
        }
    }
}
