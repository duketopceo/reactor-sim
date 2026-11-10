using System.Collections.Generic;

// Pure numbers (no UnityEngine) for the reactor hall: the true-scale integral small modular reactor module standing in a deep
// pool, seen from a gallery. Every height is in metres above the pool floor unless it says otherwise; the editor builders read
// these constants and ReactorSim.Core.Tests checks them, so the scale claims are tested on any box and the builder cannot
// drift from them. Public engineering figures for the class of module (about 2.7 m vessel, about 18 m tall, 4.5 m containment).
namespace ReactorSim
{
    public static class HallDimensions
    {
        // ---- pool and gallery ----
        public static readonly float WaterDepth = 22.0f;            // water surface above the pool floor
        public static readonly float DeckAboveWater = 0.30f;         // hall deck (pool rim) above the water
        public static readonly float PoolRadius = 6.5f;
        public static readonly float HallRadius = 15.0f;             // circular hall, concrete wall
        // Standing eye height over the water surface. Low on purpose: from the standing and seated spots the window can only
        // look down about 15 to 20 degrees, so the water the player should see has to lie inside that reach (see HallSightline).
        public static readonly float GalleryEyeAboveWater = 2.2f;
        public static readonly float EyeAboveRoomFloor = 1.6f;
        /// <summary>Control-room floor above the pool floor.</summary>
        public static readonly float RoomFloorAbovePoolFloor = WaterDepth + GalleryEyeAboveWater - EyeAboveRoomFloor;
        /// <summary>Horizontal distance from the window wall to the pool axis.</summary>
        public static readonly float PoolAxisFromWall = 10.0f;

        // ---- observation window (south wall) and the control-room anchors that look through it ----
        public static readonly float WindowWidth = 4.0f;
        public static readonly float WindowSill = 0.05f;             // glass runs almost to the floor so the standing eye can look steeply down
        public static readonly float WindowHeight = 2.3f;
        public static readonly float WindowOffsetFromDoor = 3.0f;    // opening centre, along the wall from the door centre
        public static readonly float HeaderMinWall = 0.3f;           // wall the builder keeps above the opening
        public static readonly float VesselShiftX = 1.2f;            // pool axis, from the window centre along the wall (towards the seat side)
        public static readonly float StandEyeHeight = 1.65f;         // OperatorMath.StandingEyeHeight
        public static readonly float StandBackFromWall = 4.5f;       // STAND_Center distance from the window wall
        public static readonly float SeatBackFromWall = 6.1f;        // seat distance from the window wall (the seat view uses the standing eye height)
        public static readonly float StandAlongWall = -3.0f;         // stand position along the wall, from the window centre
        public static readonly float SeatAlongWall = 0.49f;          // seat position along the wall, from the window centre
        public static readonly float SightMarginDeg = 1.0f;          // the module must sit this far inside the opening's vertical reach
        public static readonly float SightEdgeMargin = 0.25f;        // and this far (m) inside the opening's side edges

        // ---- reactor module (vessel inside containment, both standing on the pool floor) ----
        public static readonly float VesselDiameter = 2.7f;
        public static readonly float VesselBase = 1.4f;              // lower head sits on a skirt this high
        public static readonly float VesselHeight = 18.8f;           // overall, lower head to upper head
        public static readonly float ContainmentDiameter = 4.5f;
        public static readonly float ContainmentBase = 0.6f;
        public static readonly float ContainmentHeight = 20.6f;      // overall, base to head
        public static readonly int PumpHousingCount = 4;             // canned recirculation pump motors on the vessel (pumped integral design)

        public static float VesselTop { get { return VesselBase + VesselHeight; } }
        public static float ContainmentTop { get { return ContainmentBase + ContainmentHeight; } }
        /// <summary>Height at which the steam and feedwater stubs run sideways to the pool wall (just under the surface).</summary>
        public static float PipeRunHeight { get { return WaterDepth - 0.45f; } }
        /// <summary>The point the stand's sight line aims at: the top of the module, just under the water surface.</summary>
        public static float SightTargetHeight { get { return ContainmentTop; } }
        /// <summary>Eye height over the pool floor for a viewpoint at the given eye height over the room floor.</summary>
        public static float EyeAbovePoolFloor(float eyeAboveRoomFloor) { return RoomFloorAbovePoolFloor + eyeAboveRoomFloor; }

        // ---- spent-fuel racks (on a ledge on the far wall, shallow enough to read from the gallery) ----
        public static readonly float RackLedgeHeight = 14.5f;        // ledge top above the pool floor
        public static readonly int RackArrays = 3;
        public static readonly int RackRows = 6;
        public static readonly int RackColumns = 10;
        public static readonly float RackCellHeight = 3.2f;
        public static readonly float RackHazeLength = 3.6f;          // haze cone above the rack top, kept under the surface

        // ---- crane (parked behind the pool, out of the sight line to the module) ----
        public static readonly float CraneBridgeZ = 9.0f;            // bridge centre in hall space (+z is away from the window)
        public static readonly float CraneRailAboveDeck = 5.2f;      // rail height over the hall deck
        public static readonly float HallCeilingAboveDeck = 10.0f;   // hall ceiling over the deck: clears the trolley and motor on the crane

        // ---- budgets (KTD9). The graded copies live in scripts/check-captures.sh; these are the build-time sanity limits ----
        public static readonly int MaxRealtimeLights = 4;            // the default pixel-light count: every real light on the hall layer is a pixel light
        public static readonly int RackLights = 1;                   // one shared real light for all rack arrays; the glow cue is emissive first
        public static readonly int MaxHallTriangles = 120000;

        /// <summary>Everything that must hold for the module to be the size the plan promises. Empty means all true.</summary>
        public static List<string> Validate()
        {
            var bad = new List<string>();
            if (VesselDiameter < 2.6f || VesselDiameter > 2.8f) bad.Add("vessel diameter " + VesselDiameter + " m is not about 2.7 m");
            if (VesselHeight < 18.0f || VesselHeight > 20.0f) bad.Add("vessel height " + VesselHeight + " m is not 18 to 20 m");
            if (ContainmentDiameter < 4.4f || ContainmentDiameter > 4.6f) bad.Add("containment diameter " + ContainmentDiameter + " m is not about 4.5 m");
            if (ContainmentDiameter - VesselDiameter < 1.0f) bad.Add("no room for pump housings between the vessel and the containment");
            if (VesselBase < ContainmentBase) bad.Add("the vessel starts below the containment floor");
            if (VesselTop >= ContainmentTop) bad.Add("the vessel top pokes through the containment head");
            if (ContainmentTop >= WaterDepth - 0.5f) bad.Add("the containment head is not under the water");
            if (PipeRunHeight <= ContainmentTop) bad.Add("the steam stubs would run below the containment head");
            if (PipeRunHeight >= WaterDepth) bad.Add("the steam stubs would run above the water");
            if (ContainmentDiameter * 0.5f + 1.0f > PoolRadius) bad.Add("the containment does not fit in the pool");
            if (PoolRadius + 1.0f > HallRadius) bad.Add("the pool does not fit in the hall");
            if (RackLedgeHeight >= WaterDepth - 3.0f) bad.Add("the rack ledge is too shallow to be under water");
            if (RackLedgeHeight < ContainmentBase + 4.0f) bad.Add("the rack ledge is too deep to read from the gallery");
            if (RackLedgeHeight + RackCellHeight + RackHazeLength >= WaterDepth) bad.Add("the rack haze would poke above the water");
            if (RackLights < 1 || RackLights > 2) bad.Add("the racks need one or two real lights");
            if (MaxRealtimeLights > 4) bad.Add("more than 4 real lights on the hall layer exceeds the default pixel-light count");
            if (HallCeilingAboveDeck < CraneRailAboveDeck + 3.5f) bad.Add("the hall ceiling is lower than the top of the crane trolley");
            if (WindowSill >= StandEyeHeight) bad.Add("the window sill is above the standing eye");
            bad.AddRange(HallSightline.CheckWindowReach());
            return bad;
        }
    }
}
