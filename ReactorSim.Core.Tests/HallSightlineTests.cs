using Xunit;

namespace ReactorSim.Core.Tests
{
    // The observation window has to let the control-room viewpoints see the reactor module (PR4). The PR2 hall failed this: a
    // 0.35 m sill and a gallery 9 m over the water capped the downward view at about 16 degrees from the stand while the pool
    // centre sat 30 degrees down, so only the far wall showed. These tests state the geometry as numbers.
    public class HallSightlineTests
    {
        [Fact]
        public void CurrentDimensionsLetTheStandAndTheSeatSeeTheModuleHead()
        {
            Assert.Empty(HallSightline.CheckWindowReach());
            Assert.True(HallSightline.ReachMarginDeg(HallSightline.Stand(), HallDimensions.SightTargetHeight) >= HallDimensions.SightMarginDeg);
            Assert.True(HallSightline.ReachMarginDeg(HallSightline.Seat(), HallDimensions.SightTargetHeight) >= HallDimensions.SightMarginDeg);
        }

        [Fact]
        public void TheOldPr2NumbersFailTheSameCheck()
        {
            // PR2: sill 0.35 m, gallery eye 9 m over the water, pool axis 11 m from the wall, stand 4.5 m back
            double reach = HallSightline.OpeningDownReachDeg(1.65, 0.35, 4.5);
            double needed = HallSightline.DownAngleDeg(9.0, 4.5 + 11.0);
            Assert.True(reach < needed, $"reach {reach:0.0} deg should be less than the {needed:0.0} deg to the pool centre");
        }

        [Fact]
        public void SightLinesCrossTheWallInsideTheOpening()
        {
            Assert.True(HallSightline.EdgeMargin(HallSightline.Stand()) >= HallDimensions.SightEdgeMargin);
            Assert.True(HallSightline.EdgeMargin(HallSightline.Seat()) >= HallDimensions.SightEdgeMargin);
        }

        [Fact]
        public void CrossingIsInterpolatedAlongTheSightLine()
        {
            // eye 2 m back and 3 m left of the target's along-position, target 6 m beyond the wall: crosses 2/8 of the way, 3 m * 0.25 from the eye
            Assert.Equal(-2.25, HallSightline.CrossingAlong(-3.0, 0.0, 2.0, 6.0), 6);
        }

        [Fact]
        public void ParkedCraneIsClearOfEverySightLineToTheModule()
        {
            double[][] box = HallSightline.CraneBox();
            Assert.True(HallSightline.CraneClearOfModule(HallSightline.Stand(), box));
            Assert.True(HallSightline.CraneClearOfModule(HallSightline.Seat(), box));
        }

        [Fact]
        public void ACraneBridgeOverThePoolAxisAtSightHeightWouldBeCaught()
        {
            // negative control: a girder box straddling the pool axis at the height of the sight line must be reported
            double[][] over = { new[] { -8.0, 22.0, -2.0 }, new[] { 8.0, 26.0, 2.0 } };
            Assert.False(HallSightline.CraneClearOfModule(HallSightline.Stand(), over));
        }

        [Fact]
        public void SegmentBoxSlabTestHandlesMissesHitsAndParallelSegments()
        {
            double[] lo = { 0, 0, 0 }, hi = { 1, 1, 1 };
            Assert.True(HallSightline.SegmentHitsBox(new[] { -1.0, 0.5, 0.5 }, new[] { 2.0, 0.5, 0.5 }, lo, hi));
            Assert.False(HallSightline.SegmentHitsBox(new[] { -1.0, 2.0, 0.5 }, new[] { 2.0, 2.0, 0.5 }, lo, hi));   // parallel, outside the y slab
            Assert.False(HallSightline.SegmentHitsBox(new[] { -2.0, 0.5, 0.5 }, new[] { -1.0, 0.5, 0.5 }, lo, hi)); // stops short
        }
    }
}
