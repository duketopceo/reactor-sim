using ReactorSim.View3D;
using Xunit;

namespace ReactorSim.Core.Tests
{
    public class RoomLightingTests
    {
        [Fact]
        public void Key_AndAmbient_BothLeanCool_WithTheTaskPoolsWarmer()
        {
            Assert.True(RoomLighting.KeyColor.B > RoomLighting.KeyColor.R, "key should lean cool (blue-teal mood)");
            Assert.True(RoomLighting.TaskColor.R > RoomLighting.KeyColor.R, "task pools stay warmer than the key");
            Assert.True(RoomLighting.AmbientSky.B > RoomLighting.AmbientSky.R, "ambient should lean cool");
        }

        [Fact]
        public void Ambient_IsDimmerThanTheKey_AndOrderedSkyEquatorGround()
        {
            double sky = RoomLighting.Luma(RoomLighting.AmbientSky), eq = RoomLighting.Luma(RoomLighting.AmbientEquator),
                ground = RoomLighting.Luma(RoomLighting.AmbientGround), key = RoomLighting.Luma(RoomLighting.KeyColor);
            Assert.True(sky > eq && eq > ground);
            Assert.True(key / sky >= 3.0, "key to fill ratio " + (key / sky));
            Assert.True(sky >= 0.05, "ambient must not crush the room to black");
        }

        [Fact]
        public void KeyIntensityAndRange_StayInAnIndoorBand()
        {
            Assert.InRange(RoomLighting.KeyIntensity, 0.3f, 1.0f);
            Assert.InRange(RoomLighting.KeyRange, 5f, 14f);
            Assert.InRange(RoomLighting.AmbientIntensity, 0.5f, 1.2f);
        }

        [Fact]
        public void TaskLights_AreWarmerThanTheAmbient_AndShortRanged()
        {
            Assert.True(RoomLighting.TaskColor.R > RoomLighting.TaskColor.B);
            Assert.InRange(RoomLighting.TaskRange, 1.5f, 4f);
            Assert.True(RoomLighting.TaskIntensity > RoomLighting.KeyIntensity, "pools of light at the work surfaces outshine the room key");
            Assert.True(RoomLighting.TaskIntensity <= 1.2f, "pools stay below a blown highlight");
        }

        [Fact]
        public void SurfaceSteps_SeparateConsoleWallCeilingAndFloor()
        {
            double wall = RoomLighting.Luma(RoomLighting.WallTint), ceiling = RoomLighting.Luma(RoomLighting.CeilingTint),
                floor = RoomLighting.Luma(RoomLighting.FloorTint);
            Assert.True(wall < 0.75, "walls must step down from the consoles (1.0)");
            Assert.True(floor < 0.45, "the floor must read clearly darker than the bench tops");
            Assert.True(ceiling < wall && floor < wall, "ceiling and floor go darker than the walls");
            Assert.True(RoomLighting.WallTint.B > RoomLighting.WallTint.R, "walls lean cool");
        }

        [Fact]
        public void EmissionLadder_PutsLampsAboveScreens()
        {
            Assert.True(RoomLighting.LampEmission > RoomLighting.ScreenEmission);
            Assert.True(RoomLighting.ScreenEmission > 0.0);
        }
    }
}
