using ReactorSim.Ui;

// Pure (no UnityEngine): the control-room lighting plan. Mood: dim, cool and gloomy (blue-teal ambient and ceiling key, deep
// shadows) with pools of pale light over each console bench so the instruments stay readable; CRTs and lamps are self-lit.
// ControlRoomSceneBuilder applies these numbers; RoomLightingTests holds the relationships. The reactor hall has its own
// lighting (hall layer).
namespace ReactorSim.View3D
{
    public static class RoomLighting
    {
        // Ceiling fixtures (key): cool blue-teal white, low. Intensity is a unitless built-in-pipeline value, not Blender watts.
        public static readonly Rgba KeyColor = new Rgba(0.66, 0.84, 1.00);
        public const float KeyIntensity = 0.55f;
        public const float KeyRange = 7f;

        // Ambient (fill): cool and dim, sky brighter than equator brighter than ground.
        public static readonly Rgba AmbientSky = new Rgba(0.06, 0.10, 0.15);
        public static readonly Rgba AmbientEquator = new Rgba(0.03, 0.05, 0.07);
        public static readonly Rgba AmbientGround = new Rgba(0.012, 0.02, 0.026);
        public const float AmbientIntensity = 1.0f;

        // Task lights over each console bench: pale, slightly warm pools against the cool room; small range so only nearby surfaces
        // take them (pixel-light budget).
        public static readonly Rgba TaskColor = new Rgba(1.00, 0.90, 0.74);
        public const float TaskIntensity = 0.85f;
        public const float TaskRange = 2.6f;
        public const float TaskHeightM = 0.9f;      // above the console's top edge
        public const float TaskTowardRoomM = 0.5f;  // pulled toward the room centre, over the bench

        // Ceiling fixture panels (self-lit): cooler and dimmer than the exported warm white, so the panels match the key.
        public static readonly Rgba FixtureEmissionTint = new Rgba(0.62, 0.82, 1.00);
        public const float FixtureEmissionScale = 0.55f;

        // Surface value steps (albedo multipliers) that separate the room: the consoles and bench tops keep full value, the
        // walls step down and cool, the ceiling and the floor go darker still.
        public static readonly Rgba WallTint = new Rgba(0.40, 0.52, 0.66);
        public static readonly Rgba CeilingTint = new Rgba(0.22, 0.28, 0.38);
        public static readonly Rgba FloorTint = new Rgba(0.22, 0.25, 0.30);

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
