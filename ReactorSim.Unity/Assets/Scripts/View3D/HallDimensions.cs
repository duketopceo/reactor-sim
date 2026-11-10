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
        public static readonly float GalleryEyeAboveWater = 9.0f;    // standing eye height over the water surface
        public static readonly float EyeAboveRoomFloor = 1.6f;
        /// <summary>Control-room floor above the pool floor.</summary>
        public static readonly float RoomFloorAbovePoolFloor = WaterDepth + GalleryEyeAboveWater - EyeAboveRoomFloor;
        /// <summary>Horizontal distance from the window wall to the pool axis.</summary>
        public static readonly float PoolAxisFromWall = 11.0f;

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

        // ---- spent-fuel racks (on a ledge on the far wall, shallow enough to read from the gallery) ----
        public static readonly float RackLedgeHeight = 12.0f;        // ledge top above the pool floor
        public static readonly int RackArrays = 3;
        public static readonly int RackRows = 6;
        public static readonly int RackColumns = 10;

        // ---- budgets (KTD9). The graded copies live in scripts/check-captures.sh; these are the build-time sanity limits ----
        public static readonly int MaxRealtimeLights = 6;
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
            return bad;
        }
    }
}
