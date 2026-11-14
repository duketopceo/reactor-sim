using Xunit;

namespace ReactorSim.Core.Tests
{
    // The observation window is an aquarium pane: the eye sits underwater and the vessel mid-body is seen at eye level
    // through the water (the gallery-looked-down design is gone). These tests state that geometry as numbers.
    public class HallSightlineTests
    {
        [Fact]
        public void CurrentDimensionsLetTheStandAndTheSeatSeeTheVesselBand()
        {
            Assert.Empty(HallSightline.CheckWindowReach());
        }

        [Fact]
        public void TheEyeIsUnderwaterAndTheVesselBandSitsInThePane()
        {
            // GalleryEyeAboveWater is negative: the standing eye is at the fuel display's depth.
            Assert.True(HallDimensions.GalleryEyeAboveWater < 0.0f);
            Assert.Equal(HallDimensions.WaterDepth + HallDimensions.GalleryEyeAboveWater, HallDimensions.EyeAbovePoolFloor(HallDimensions.StandEyeHeight), 4);
            double low, high;
            HallSightline.VisibleBandOnAxis(HallSightline.Stand(), out low, out high);
            // Room floor 7.35 m above pool floor; sill 7.65, top 9.95, standing eye 9.0.
            Assert.Equal(7.65 + (7.65 - 9.0) * 12.0 / 4.5, low, 2);
            Assert.Equal(9.95 + (9.95 - 9.0) * 12.0 / 4.5, high, 2);
            Assert.True(low < HallDimensions.VesselBandLow, "the window must see down into the vessel band");
            Assert.True(high > HallDimensions.VesselBandHigh, "the window must see up through the vessel band");
        }

        [Fact]
        public void TheSeatBandIsHigherButStillCoversTheVesselMidBody()
        {
            double low, high;
            HallSightline.VisibleBandOnAxis(HallSightline.Seat(), out low, out high);
            // Seated eye 7.35 + 1.2 = 8.55, 6.1 m back.
            Assert.Equal(7.65 + (7.65 - 8.55) * 12.0 / 6.1, low, 2);
            Assert.True(low <= HallDimensions.VesselBandLow + 1.5);
            Assert.True(high >= HallDimensions.VesselBandHigh);
        }

        [Fact]
        public void ThePoolReachesTheWindowWallSoTheGlassIsInTheWater()
        {
            Assert.True(HallDimensions.PoolRadius > HallDimensions.PoolAxisFromWall, "the pool edge must cross the window-wall plane");
        }

        [Fact]
        public void TheSeatUsesTheRealSeatedEye()
        {
            Assert.Equal(1.2, HallSightline.Seat().eyeHeight, 6);
        }

        [Fact]
        public void AnAboveWaterEyeWouldFrameTheWrongBand()
        {
            // negative control: raise the eye 3 m (still wet, but higher) and the same edge maths frames a band that tops
            // out below the vessel mid-body — the submerged eye height is what puts the band on the vessel.
            double eye = HallDimensions.EyeAbovePoolFloor(HallDimensions.StandEyeHeight) + 3.0;
            double sillY = HallDimensions.RoomFloorAbovePoolFloor + HallDimensions.WindowSill;
            double topY = sillY + HallDimensions.WindowHeight;
            double high = topY + (topY - eye) * HallDimensions.PoolAxisFromWall / HallDimensions.StandBackFromWall;
            Assert.True(high < HallDimensions.VesselBandLow, $"a higher eye frames up to {high:0.0} m, below the vessel band");
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
            // A bridge across the opening's upper half must still be caught when the gallery height changes.
            double floor = HallDimensions.RoomFloorAbovePoolFloor;
            double[][] high = { new[] { -14.0, floor + 1.25, -12.0 }, new[] { 14.0, floor + 3.2, -9.0 } };
            Assert.False(HallSightline.CraneClearOfModule(HallSightline.Stand(), high));
            Assert.True(HallSightline.CraneOpeningCoverage(HallSightline.Stand(), high) > HallSightline.MaxCraneOpeningCoverage);
        }

        [Fact]
        public void ACraneBridgeOverThePoolAxisAtSightHeightWouldBeCaught()
        {
            // negative control: a girder box straddling the pool axis at the height of the sight line must be reported
            double target = HallDimensions.SightTargetHeight;
            double[][] over = { new[] { -8.0, target - 1.0, -2.0 }, new[] { 8.0, target + 2.0, 2.0 } };
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
