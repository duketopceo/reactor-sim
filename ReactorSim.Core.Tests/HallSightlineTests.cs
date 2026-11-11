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
        public void TheModuleHeadClearsThePoolRimFromTheStandAndTheSeat()
        {
            double need = HallDimensions.HeadDomeHeight + HallDimensions.HeadVisibleMargin;
            Assert.True(HallSightline.RimVisibleHeight(HallSightline.Stand()) >= need);
            Assert.True(HallSightline.RimVisibleHeight(HallSightline.Seat()) >= need);
        }

        [Fact]
        public void RimHiddenBelowMatchesAHandCalculationAndMovesTheRightWay()
        {
            // stand eye 22 + 2.25 - 1.65 + 1.65 = 24.25 m over the floor, rim 22.15 m, rim 4.5 + 13 - 11 = 6.5 m out, pool radius 11 m past it
            Assert.Equal(22.15 - (24.25 - 22.15) * 11.0 / 6.5, HallSightline.RimHiddenBelow(HallSightline.Stand()), 2);
            // seated eye 22 + 2.25 - 1.65 + 1.2 = 23.8 m, rim 6.1 + 13 - 11 = 8.1 m out
            Assert.Equal(22.15 - (23.8 - 22.15) * 11.0 / 8.1, HallSightline.RimHiddenBelow(HallSightline.Seat()), 2);
        }

        [Fact]
        public void TheSeatUsesTheRealSeatedEye()
        {
            Assert.Equal(1.2, HallSightline.Seat().eyeHeight, 6);
            Assert.True(HallSightline.ReachMarginDeg(HallSightline.Seat(), HallDimensions.SightTargetHeight) >= HallDimensions.SightMarginDeg);
        }

        [Fact]
        public void TheFirstPr6NumbersFailTheSeatedEyeCheck()
        {
            // negative control: gallery eye 3.0 m, pool axis 12 m, head top 21.4 m. Seated eye is 2.6 m over the water: reach atan(1.15 / 6.1) = 10.7 deg
            // against a 3.2 m drop over 18.1 m = 10.0 deg to the head, so the margin is 0.7 deg, under the 3 deg the window must leave.
            double reach = HallSightline.OpeningDownReachDeg(1.2, 0.05, 6.1);
            double needed = HallSightline.DownAngleDeg(2.6 + 0.6, 6.1 + 12.0);
            Assert.True(reach - needed < HallDimensions.SightMarginDeg, $"margin {reach - needed:0.0} deg should be under {HallDimensions.SightMarginDeg} deg");
        }

        [Fact]
        public void ThePr4PoolAndGalleryNumbersLeftTheHeadBehindTheRim()
        {
            // PR4: rim 0.3 m over the water, pool radius 6.5 m, eye 2.2 m over the water, head top 21.2 m over the floor. Same maths, old inputs.
            double eye = 22.0 + 2.2, rimTop = 22.3, toAxis = 6.1 + 10.0, toRim = toAxis - 6.5;
            double hiddenBelow = rimTop - (eye - rimTop) * 6.5 / toRim;
            Assert.True(21.2 - hiddenBelow < HallDimensions.HeadDomeHeight, "the PR4 seat view shows less than the head dome over the rim");
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
        public void TheRealCraneFillsLittleOfTheWindowFromTheStandAndTheSeat()
        {
            double[][] box = HallSightline.CraneBox();
            foreach (var v in new[] { HallSightline.Stand(), HallSightline.Seat() })
            {
                double c = HallSightline.CraneOpeningCoverage(v, box);
                Assert.True(c <= HallSightline.MaxCraneOpeningCoverage, $"{v.name}: crane fills {c:0.00} of the opening");
            }
        }

        [Fact]
        public void ACraneAboveTheHorizonButAcrossTheWindowIsCaught()
        {
            // a bridge parked over the deck in front of the window, at the height of the opening's upper half: no sight line to the
            // module touches it (they slope down), but it fills the top of the view, so the opening check must fail
            double deckY = HallDimensions.WaterDepth + HallDimensions.DeckAboveWater;
            double[][] high = { new[] { -14.0, deckY + 1.5, -12.0 }, new[] { 14.0, deckY + 6.0, -9.0 } };
            Assert.False(HallSightline.CraneClearOfModule(HallSightline.Stand(), high));
            Assert.True(HallSightline.CraneOpeningCoverage(HallSightline.Stand(), high) > HallSightline.MaxCraneOpeningCoverage);
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
