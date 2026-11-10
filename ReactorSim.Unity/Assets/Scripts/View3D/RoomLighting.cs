using ReactorSim.Ui;

// Pure (no UnityEngine): the control-room lighting plan. A warm key from the ceiling fixtures over a cooler, darker
// ambient gives the room contrast (the first build lit everything with one flat blue-grey). ControlRoomSceneBuilder
// applies these numbers; RoomLightingTests holds the relationships. The reactor hall has its own lighting (hall layer).
namespace ReactorSim.View3D
{
    public static class RoomLighting
    {
        // Ceiling fixtures (key): warm white. Intensity is a unitless built-in-pipeline value, not Blender watts.
        public static readonly Rgba KeyColor = new Rgba(1.00, 0.92, 0.80);
        public const float KeyIntensity = 0.95f;
        public const float KeyRange = 9f;

        // Ambient (fill): cool and dim, sky brighter than equator brighter than ground.
        public static readonly Rgba AmbientSky = new Rgba(0.15, 0.18, 0.24);
        public static readonly Rgba AmbientEquator = new Rgba(0.08, 0.09, 0.11);
        public static readonly Rgba AmbientGround = new Rgba(0.04, 0.04, 0.045);
        public const float AmbientIntensity = 1.0f;

        // Emission ladder for self-lit surfaces: panel surfaces take no emission, screens carry a dark glow, lamps a bright one.
        public const double ScreenEmission = 1.0;
        public const double LampEmission = 2.5;

        /// <summary>Rec. 709 luma of an sRGB colour (good enough to order lights).</summary>
        public static double Luma(Rgba c)
        {
            return 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
        }
    }
}
