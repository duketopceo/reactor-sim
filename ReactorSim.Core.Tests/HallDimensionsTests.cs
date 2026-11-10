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
        public void GalleryEyeIsLowEnoughForTheWindowToLookDownAtThePool()
        {
            // 2.2 m over the water: the stand and seat reach (about 19 and 15 degrees) covers the module head
            Assert.Equal(2.2f, HallDimensions.GalleryEyeAboveWater);
            Assert.Equal(HallDimensions.WaterDepth + 2.2f - 1.6f, HallDimensions.RoomFloorAbovePoolFloor, 4);
            Assert.True(HallDimensions.PoolAxisFromWall - HallDimensions.PoolRadius > 1.0f, "the deck must stay between the wall and the pool edge");
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
        public void LightBudgetIsAtMostTheDefaultPixelLightCount()
        {
            Assert.True(HallDimensions.MaxRealtimeLights <= 4);
            Assert.True(HallDimensions.RackLights >= 1 && HallDimensions.RackLights <= 2);
        }
    }
}
