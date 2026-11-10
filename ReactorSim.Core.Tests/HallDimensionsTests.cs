using Xunit;

namespace ReactorSim.Core.Tests
{
    // The scale of the SMR pool hall (U6, R17/R18): the numbers the builders read must describe a believable integral module.
    public class HallDimensionsTests
    {
        [Fact]
        public void ConstantsPassTheirOwnValidation()
        {
            Assert.Empty(HallDimensions.Validate());
        }

        [Fact]
        public void VesselIsAboutTwoPointSevenMetresByEighteenToTwentyMetres()
        {
            Assert.InRange(HallDimensions.VesselDiameter, 2.6f, 2.8f);
            Assert.InRange(HallDimensions.VesselHeight, 18.0f, 20.0f);
        }

        [Fact]
        public void ContainmentIsAboutFourPointFiveMetresAndWrapsTheVessel()
        {
            Assert.InRange(HallDimensions.ContainmentDiameter, 4.4f, 4.6f);
            Assert.True(HallDimensions.ContainmentDiameter > HallDimensions.VesselDiameter + 1.0f);
            Assert.True(HallDimensions.VesselBase >= HallDimensions.ContainmentBase);
            Assert.True(HallDimensions.VesselTop < HallDimensions.ContainmentTop);
        }

        [Fact]
        public void ModuleStandsInDeepWaterWithItsHeadUnderTheSurface()
        {
            Assert.True(HallDimensions.WaterDepth >= 20.0f);
            Assert.True(HallDimensions.ContainmentTop < HallDimensions.WaterDepth);
            Assert.True(HallDimensions.PipeRunHeight > HallDimensions.ContainmentTop);
            Assert.True(HallDimensions.PipeRunHeight < HallDimensions.WaterDepth);
        }

        [Fact]
        public void GalleryEyeIsNineMetresOverTheWaterAndRoomFloorFollows()
        {
            Assert.Equal(9.0f, HallDimensions.GalleryEyeAboveWater);
            // eye = room floor + 1.6 m, so the room floor is 29.4 m over the pool floor
            Assert.Equal(HallDimensions.WaterDepth + 9.0f - 1.6f, HallDimensions.RoomFloorAbovePoolFloor, 4);
        }

        [Fact]
        public void PumpedIntegralDesignHasPumpHousings()
        {
            Assert.True(HallDimensions.PumpHousingCount >= 2);
        }

        [Fact]
        public void RackLedgeIsUnderWaterButShallowEnoughToSee()
        {
            Assert.True(HallDimensions.RackLedgeHeight < HallDimensions.WaterDepth - 3.0f);
            Assert.True(HallDimensions.RackLedgeHeight > 4.0f);
            Assert.True(HallDimensions.RackArrays >= 2 && HallDimensions.RackArrays <= 3);
        }

        [Fact]
        public void LightBudgetIsAtMostSix()
        {
            Assert.True(HallDimensions.MaxRealtimeLights <= 6);
        }
    }
}
