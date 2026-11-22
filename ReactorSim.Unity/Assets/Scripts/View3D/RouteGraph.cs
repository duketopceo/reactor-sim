using System;
using System.Collections.Generic;

// Pure route contract (no UnityEngine) for the Phase 2 wing: the rooms the operator can walk between, the doorways and the lift
// that join them, floor heights, clearances, stable IDs and the per-space budget caps. The editor builders, the walk and caps
// checks and ReactorSim.Core.Tests read these numbers, so the route cannot drift from the geometry (same pattern as
// HallDimensions and OperatorMath). Linked into ReactorSim.Core.Tests.
//
// Frame ("room frame"): the control-room frame of design/control-room/layout.json. x runs west to east, y runs front to back
// (the south wall, the one with the window and the decorative door, is y 0; the room interior is y 0..10), h is height above
// the control-room floor. Blender (x, y, z) is the same triple; the FBX import maps it to Unity (x, z, y), so
//     Unity position = (x, h, y)       and       Unity yaw = heading
// (ControlRoomSceneBuilder: "FBX maps Blender (x,y,z) to Unity (x,z,y)"). A heading is degrees from +y toward +x, the same
// convention as layout.json facing_deg; a heading of 90 faces east. HallSightline's "along" runs from the window centre toward
// the window side, so a viewpoint's x is WindowCentreX - along (STAND_Center: 3.0 + 3.0 = 6.0).
//
// Every room is an interior rectangle. Walls sit outside it and are WallThickness thick, so two neighbouring rooms are one
// wall thickness apart and a doorway is at the middle of that gap. Wall thickness, interior sizes and the board arc are the
// layout.json numbers (RouteGraphTests pins each constant that cannot be read from code to its file).
namespace ReactorSim
{
    public static class RouteGraph
    {
        // ---- control room (design/control-room/layout.json "room", "arc", "bench"; checks.min_text_deg) ----
        public const float RoomWidth = 12.0f, RoomDepth = 10.0f, RoomCeiling = 3.2f, WallThickness = 0.2f;
        public const float ArcCentreX = 6.0f, ArcCentreY = 4.5f, ArcRadius = 5.0f, ArcHalfSpanDeg = 87.5f;
        /// <summary>Horizontal depth of the bench in front of the board's vertical face (layout.json bench.horizontal_depth).</summary>
        public const float BenchDepth = 0.58f;
        /// <summary>How far a console body extends behind the vertical face (Blender/scripts/room_layout.py BACK_DEPTH).</summary>
        public const float BoardBackDepth = 0.40f;
        /// <summary>Text is at least this many degrees tall at its approach distance (layout.json checks.min_text_deg).</summary>
        public const float MinTextDeg = 0.9f;
        /// <summary>Body capsule radius of the walking operator (OperatorController: CharacterController radius).</summary>
        public const float OperatorRadius = 0.3f;
        public const string ControlRoomId = "room.control";

        // The south-wall mimic plate beside the decorative door (Blender/scripts/make_control_room.py: dx = W / 2, plate from
        // dx + 0.9 to W - 0.6, centre height 1.75, 1.7 wide, 0.95 high). It occupies x 8.3..10.0 of the south wall, y 0..0.05.
        public const float WallPlateMinX = 8.3f, WallPlateMaxX = 10.0f, WallPlateMinH = 1.275f, WallPlateMaxH = 2.225f, WallPlateDepth = 0.05f;

        // ---- reactor hall (HallDimensions; HallBuilder places the hall root) ----
        /// <summary>The window sits on the -x side of the decorative door at x 6 (the wall plate takes the +x side): x 6 - 3.0 = 3.0.</summary>
        public static readonly float WindowCentreX = RoomWidth * 0.5f - HallDimensions.WindowOffsetFromDoor;
        /// <summary>Hall axis x: the window centre plus VesselShiftX (HallBuilder.Build(wx + VesselShiftX, ...)).</summary>
        public static readonly float HallCentreX = WindowCentreX + HallDimensions.VesselShiftX;
        /// <summary>Hall axis y: PoolAxisFromWall beyond the outer face of the south wall, which is at y -WallThickness.</summary>
        public static readonly float HallCentreY = -WallThickness - HallDimensions.PoolAxisFromWall;
        /// <summary>Thickness of the hall's concrete shell, outside HallRadius (original design choice: room for a 0.3 m opening and its frame).</summary>
        public const float HallShellThickness = 0.3f;
        /// <summary>Hall deck (the pool rim walkway) above the control-room floor: about 16.1 m.</summary>
        public static readonly float DeckFloorH = HallDimensions.WaterDepth + HallDimensions.DeckAboveWater - HallDimensions.RoomFloorAbovePoolFloor;

        // ---- wing geometry (original design; see docs/design/plant-and-assets.md "Phase 2 route") ----
        private const float CorridorEndX = 28.2f;       // the corridor is 16.0 m long: x 12.2 to 28.2
        private const float TurbineLength = 30.0f, TurbineMinY = -8.0f, TurbineMaxY = 12.0f;   // 30 x 20 m, entrance centred on the corridor axis
        private const float TurbineCeiling = 10.0f;      // same clear height as the hall over its deck (HallDimensions.HallCeilingAboveDeck)
        private const float OpeningY = -2.0f;            // the lift opening in the hall shell, and the car's centre line

        // ---- types ----
        public struct Box
        {
            public string Id;
            public float MinX, MinY, MinH, MaxX, MaxY, MaxH;
            public Box(string id, float minX, float minY, float minH, float maxX, float maxY, float maxH)
            { Id = id; MinX = minX; MinY = minY; MinH = minH; MaxX = maxX; MaxY = maxY; MaxH = maxH; }
        }

        public struct Caps
        {
            public int Triangles, Renderers, Lights, Colliders;
            public Caps(int triangles, int renderers, int lights, int colliders)
            { Triangles = triangles; Renderers = renderers; Lights = lights; Colliders = colliders; }
        }

        /// <summary>A budget scope and builder root. Existing spaces keep the caps they already have (HallDimensions); new ones carry the four numeric caps.</summary>
        public sealed class Space
        {
            public string Id, RootName; public bool Existing; public Caps Caps;
        }

        public sealed class Room
        {
            public string Id, Name, SpaceId;
            /// <summary>Interior rectangle; MinH/MaxH are the floor and ceiling heights.</summary>
            public Box Interior;
            public float FloorH { get { return Interior.MinH; } }
            public float CeilingH { get { return Interior.MaxH; } }
            /// <summary>Highest floor the room serves: the lift car's upper landing; otherwise the floor.</summary>
            public float TravelTopH;
            public float MinClearWidth() { return Math.Min(Interior.MaxX - Interior.MinX, Interior.MaxY - Interior.MinY); }
        }

        public sealed class Doorway
        {
            public string Id, FromRoomId, ToRoomId;
            /// <summary>Centre of the aperture on the floor line (the middle of the wall gap).</summary>
            public float X, Y, FloorH, Width, Height;
            /// <summary>Heading of a step from the first room to the second.</summary>
            public float HeadingDeg;
            public float HeadingFromTo(bool forward) { return forward ? HeadingDeg : (HeadingDeg + 180f) % 360f; }
        }

        public sealed class HallOpening
        {
            public string Id; public float X, Y, FloorH, Width, Height, OutwardHeadingDeg;
        }

        public sealed class LiftLanding
        {
            public string Id, RoomId, DoorwayId;
            public float FloorH, X, Y, ExitHeadingDeg;
        }

        public sealed class NamePlate
        {
            public string Id, Text, DoorwayId, RoomId;
            public float X, Y, CentreH, FaceHeadingDeg, ApproachM, TextHeightM, PlateWidth, PlateHeight;
        }

        public sealed class RoutePoint
        {
            public string Id, RoomId;
            public float X, Y, FloorH, EyeHeight, HeadingDeg;
            /// <summary>Key of the operating point in design/control-room/layout.json: the ID upper-cased, dots as underscores.</summary>
            public string LayoutKey { get { return Id.ToUpperInvariant().Replace('.', '_'); } }
        }

        // ---- data ----
        public static readonly IList<Space> Spaces;
        public static readonly IList<Room> Rooms;
        public static readonly IList<Doorway> Doorways;
        public static readonly HallOpening HallLiftOpening;
        public static readonly IList<LiftLanding> Landings;
        public static readonly IList<NamePlate> NamePlates;
        public static readonly IList<RoutePoint> RoutePoints;

        static RouteGraph()
        {
            // Caps are shares of the existing hall budget (HallDimensions.MaxHallTriangles); renderers are one per 200 triangles and
            // colliders a quarter of the renderers (original design choice: the hall has no colliders and the room has no numeric
            // cap, so neither has a number to copy). Lights are inside the pixel-light count: touching spaces sum to at most
            // MaxRealtimeLights, and wing lights light only the Wing layer.
            Spaces = new List<Space>
            {
                new Space { Id = "space.control_room", RootName = "ControlRoom", Existing = true },
                new Space { Id = "space.reactor_hall", RootName = "ReactorHall", Existing = true,
                    Caps = new Caps(HallDimensions.MaxHallTriangles, 0, HallDimensions.MaxRealtimeLights, 0) },
                new Space { Id = "space.wing_corridor", RootName = "Wing_Corridor", Caps = CapsFromShare(10, 2) },
                new Space { Id = "space.wing_lift", RootName = "Wing_Lift", Caps = CapsFromShare(13, 2) },      // lobby 8 % + car 5 %
                new Space { Id = "space.wing_deck", RootName = "Wing_Deck", Caps = CapsFromShare(8, 1) },
                new Space { Id = "space.wing_turbine_hall", RootName = "Wing_TurbineHall", Caps = CapsFromShare(40, 2) }
            };

            float shaftTop = DeckFloorH + RoomCeiling;
            Rooms = new List<Room>
            {
                MakeRoom("room.control", "Control room", "space.control_room", 0f, 0f, RoomWidth, RoomDepth, 0f, RoomCeiling),
                MakeRoom("room.corridor", "East corridor", "space.wing_corridor", 12.2f, 1.0f, CorridorEndX, 3.0f, 0f, RoomCeiling),
                MakeRoom("room.lift_lobby", "Lift lobby", "space.wing_lift", 14.8f, -0.8f, 19.8f, 0.8f, 0f, RoomCeiling),
                MakeRoom("room.lift", "Lift", "space.wing_lift", 17.1f, -3.0f, 19.5f, -1.0f, 0f, shaftTop, DeckFloorH),
                // The deck room is the vestibule between the lift and the hall shell plus the tunnel through the shell; the walkway
                // round the pool rim is the existing Hall_Deck inside the ReactorHall root, reached through the opening.
                MakeRoom("room.reactor_deck", "Reactor operating deck", "space.wing_deck", 0f, -2.6f, 16.9f, -1.4f, DeckFloorH, shaftTop),
                MakeRoom("room.turbine_hall", "Turbine hall", "space.wing_turbine_hall", CorridorEndX + 0.2f, TurbineMinY,
                    CorridorEndX + 0.2f + TurbineLength, TurbineMaxY, 0f, TurbineCeiling)
            };
            // The deck room starts at the opening on the inside face of the hall shell (computed from the hall circle).
            float openingX = HallCentreX + (float)Math.Sqrt(HallDimensions.HallRadius * HallDimensions.HallRadius
                                                            - (OpeningY - HallCentreY) * (OpeningY - HallCentreY));
            Rooms[4].Interior.MinX = openingX;
            HallLiftOpening = new HallOpening
            {
                Id = "opening.hall_lift", X = openingX, Y = OpeningY, FloorH = DeckFloorH, Width = 1.2f, Height = 2.2f,
                OutwardHeadingDeg = Heading(openingX - HallCentreX, OpeningY - HallCentreY)
            };

            Doorways = new List<Doorway>
            {
                Door("door.control_east", "room.control", "room.corridor", 12.1f, 2.0f, 0f, 1.2f, 2.1f, 90f),
                Door("door.corridor_lobby", "room.corridor", "room.lift_lobby", 17.3f, 0.9f, 0f, 2.4f, 2.4f, 180f),
                Door("door.lobby_lift", "room.lift_lobby", "room.lift", 18.3f, -0.9f, 0f, 1.2f, 2.1f, 180f),
                Door("door.lift_deck", "room.lift", "room.reactor_deck", 17.0f, OpeningY, DeckFloorH, 1.2f, 2.1f, 270f),
                Door("door.corridor_turbine", "room.corridor", "room.turbine_hall", CorridorEndX + 0.1f, 2.0f, 0f, 2.0f, 2.4f, 90f)
            };

            Landings = new List<LiftLanding>
            {
                new LiftLanding { Id = "landing.lift_lower", RoomId = "room.lift", DoorwayId = "door.lobby_lift", FloorH = 0f,
                    X = 18.3f, Y = OpeningY, ExitHeadingDeg = 0f },
                new LiftLanding { Id = "landing.lift_upper", RoomId = "room.lift", DoorwayId = "door.lift_deck", FloorH = DeckFloorH,
                    X = 18.3f, Y = OpeningY, ExitHeadingDeg = 270f }
            };

            NamePlates = new List<NamePlate>
            {
                // On the control-room side of the east wall, over the doorway, read from across the room.
                new NamePlate { Id = "plate.control_door", Text = "EAST CORRIDOR", DoorwayId = "door.control_east", RoomId = ControlRoomId,
                    X = RoomWidth, Y = 2.0f, CentreH = 2.65f, FaceHeadingDeg = 270f, ApproachM = 6.0f, TextHeightM = 0.10f, PlateWidth = 1.3f, PlateHeight = 0.20f },
                // A blade sign hung over the corridor at the lobby opening, facing the operator walking east.
                new NamePlate { Id = "plate.lift_lobby", Text = "LIFT TO DECK", DoorwayId = "door.corridor_lobby", RoomId = "room.corridor",
                    X = 16.0f, Y = 1.3f, CentreH = 2.70f, FaceHeadingDeg = 270f, ApproachM = 6.0f, TextHeightM = 0.10f, PlateWidth = 1.1f, PlateHeight = 0.20f },
                // On the corridor end wall over the turbine-hall entrance, read from the lobby along the corridor.
                new NamePlate { Id = "plate.turbine_entrance", Text = "TURBINE HALL", DoorwayId = "door.corridor_turbine", RoomId = "room.corridor",
                    X = CorridorEndX, Y = 2.0f, CentreH = 2.85f, FaceHeadingDeg = 270f, ApproachM = 8.0f, TextHeightM = 0.13f, PlateWidth = 1.6f, PlateHeight = 0.26f }
            };

            float rimBearing = HallLiftOpening.OutwardHeadingDeg;
            float rimR = 0.5f * (HallDimensions.PoolRadius + HallDimensions.HallRadius);   // the middle of the pool rim walkway
            RoutePoints = new List<RoutePoint>
            {
                Point("route.door_east", "room.control", 11.2f, 2.0f, 0f, 90f),
                Point("route.corridor_mid", "room.corridor", 20.2f, 2.0f, 0f, 90f),
                Point("route.lift_lobby", "room.lift_lobby", 18.3f, 0.0f, 0f, 180f),
                Point("route.lift_lower", "room.lift", 18.3f, OpeningY, 0f, 0f),
                Point("route.lift_upper", "room.lift", 18.3f, OpeningY, DeckFloorH, 270f),
                Point("route.deck_rim", "room.reactor_deck",
                    HallCentreX + rimR * (float)Math.Sin(rimBearing * Math.PI / 180.0), HallCentreY + rimR * (float)Math.Cos(rimBearing * Math.PI / 180.0),
                    DeckFloorH, (rimBearing + 180f) % 360f),   // on the rim at the lift opening's bearing, facing the pool
                Point("route.turbine_entrance", "room.turbine_hall", 29.4f, 2.0f, 0f, 90f),
                Point("route.turbine_floor", "room.turbine_hall", 36.4f, 2.0f, 0f, 90f)
            };
        }

        private static Caps CapsFromShare(int percentOfHallTriangles, int lights)
        {
            int tris = HallDimensions.MaxHallTriangles * percentOfHallTriangles / 100;
            int renderers = tris / 200;
            return new Caps(tris, renderers, lights, Math.Max(8, renderers / 4));
        }

        private static Room MakeRoom(string id, string name, string spaceId, float x0, float y0, float x1, float y1, float floorH, float ceilH, float travelTopH = float.NaN)
        {
            return new Room
            {
                Id = id, Name = name, SpaceId = spaceId, Interior = new Box(id, x0, y0, floorH, x1, y1, ceilH),
                TravelTopH = float.IsNaN(travelTopH) ? floorH : travelTopH
            };
        }

        private static Doorway Door(string id, string from, string to, float x, float y, float floorH, float w, float h, float heading)
        {
            return new Doorway { Id = id, FromRoomId = from, ToRoomId = to, X = x, Y = y, FloorH = floorH, Width = w, Height = h, HeadingDeg = heading };
        }

        private static RoutePoint Point(string id, string room, float x, float y, float floorH, float heading)
        {
            return new RoutePoint { Id = id, RoomId = room, X = x, Y = y, FloorH = floorH, EyeHeight = OperatorMath.StandingEyeHeight, HeadingDeg = heading };
        }

        /// <summary>Heading (degrees from +y toward +x, 0..360) of a vector.</summary>
        public static float Heading(float dx, float dy)
        {
            float h = (float)(Math.Atan2(dx, dy) * 180.0 / Math.PI);
            return h < 0f ? h + 360f : h;
        }

        // ---- lookups ----
        public static Room FindRoom(string id) { foreach (var r in Rooms) if (r.Id == id) return r; return null; }
        public static Doorway FindDoorway(string id) { foreach (var d in Doorways) if (d.Id == id) return d; return null; }
        public static LiftLanding FindLanding(string id) { foreach (var l in Landings) if (l.Id == id) return l; return null; }
        public static Space FindSpace(string id) { foreach (var s in Spaces) if (s.Id == id) return s; return null; }

        public static IEnumerable<Doorway> DoorwaysOf(string roomId)
        {
            foreach (var d in Doorways) if (d.FromRoomId == roomId || d.ToRoomId == roomId) yield return d;
        }

        /// <summary>The room on the other side of the doorway from roomId.</summary>
        public static string Other(Doorway d, string roomId) { return d.FromRoomId == roomId ? d.ToRoomId : d.FromRoomId; }

        /// <summary>True when the operator can walk (taking the lift where it is a doorway) from one room to another.</summary>
        public static bool Reachable(string fromId, string toId)
        {
            var seen = new HashSet<string> { fromId };
            var queue = new Queue<string>();
            queue.Enqueue(fromId);
            while (queue.Count > 0)
            {
                string at = queue.Dequeue();
                if (at == toId) return true;
                foreach (var d in DoorwaysOf(at))
                {
                    string next = Other(d, at);
                    if (seen.Add(next)) queue.Enqueue(next);
                }
            }
            return false;
        }

        /// <summary>Every stable ID of the contract, in a fixed order (spaces, rooms, doorways, the hall opening, landings, plates, route points).</summary>
        public static IEnumerable<string> AllIds()
        {
            foreach (var s in Spaces) yield return s.Id;
            foreach (var r in Rooms) yield return r.Id;
            foreach (var d in Doorways) yield return d.Id;
            yield return HallLiftOpening.Id;
            foreach (var l in Landings) yield return l.Id;
            foreach (var p in NamePlates) yield return p.Id;
            foreach (var p in RoutePoints) yield return p.Id;
        }

        // ---- plan geometry ----
        public static bool Inside(Room r, float x, float y, float slack)
        {
            return x >= r.Interior.MinX - slack && x <= r.Interior.MaxX + slack && y >= r.Interior.MinY - slack && y <= r.Interior.MaxY + slack;
        }

        public static bool PlanOverlap(Room a, Room b)
        {
            const float eps = 1e-4f;
            return a.Interior.MinX < b.Interior.MaxX - eps && b.Interior.MinX < a.Interior.MaxX - eps
                && a.Interior.MinY < b.Interior.MaxY - eps && b.Interior.MinY < a.Interior.MaxY - eps;
        }

        /// <summary>
        /// Distance in the plan from the hall axis to the nearest point of the room's interior grown by <paramref name="grow"/> (a wall)
        /// that is south of the control-room wall plane, which cuts the hall circle (HallBuilder.ClipRadius). A room entirely north of
        /// that plane has no hall beside it and returns infinity.
        /// </summary>
        public static float DistanceFromHallCentre(Room r, float grow)
        {
            float maxY = Math.Min(r.Interior.MaxY + grow, -WallThickness);
            if (r.Interior.MinY - grow > maxY) return float.PositiveInfinity;
            float x = Math.Min(Math.Max(HallCentreX, r.Interior.MinX - grow), r.Interior.MaxX + grow);
            float y = Math.Min(Math.Max(HallCentreY, r.Interior.MinY - grow), maxY);
            return (float)Math.Sqrt((x - HallCentreX) * (x - HallCentreX) + (y - HallCentreY) * (y - HallCentreY));
        }

        /// <summary>True when the point is on the pool rim walkway: between the pool and the hall wall, at least a body width from each.</summary>
        public static bool OnDeckRim(float x, float y)
        {
            float r = (float)Math.Sqrt((x - HallCentreX) * (x - HallCentreX) + (y - HallCentreY) * (y - HallCentreY));
            return r >= HallDimensions.PoolRadius + OperatorRadius && r <= HallDimensions.HallRadius - OperatorRadius;
        }

        /// <summary>
        /// A doorway is in the wall gap between two rooms when the gap is one wall thick, the aperture's centre is in the gap and the
        /// aperture's width lies inside both rooms' extent along the wall.
        /// </summary>
        public static bool DoorwaySitsInWall(Doorway d, Room a, Room b)
        {
            const float eps = 1e-3f;
            float h = d.HeadingDeg;
            float halfW = d.Width * 0.5f;
            bool eastWest = Math.Abs(h - 90f) < 1f || Math.Abs(h - 270f) < 1f;
            if (eastWest)
            {
                float lo = h < 180f ? a.Interior.MaxX : b.Interior.MaxX, hi = h < 180f ? b.Interior.MinX : a.Interior.MinX;
                return Math.Abs(hi - lo - WallThickness) < eps && d.X >= lo - eps && d.X <= hi + eps
                    && d.Y - halfW >= Math.Max(a.Interior.MinY, b.Interior.MinY) - eps && d.Y + halfW <= Math.Min(a.Interior.MaxY, b.Interior.MaxY) + eps;
            }
            bool north = Math.Abs(h) < 1f || Math.Abs(h - 360f) < 1f;
            if (!north && Math.Abs(h - 180f) >= 1f) return false;
            float low = north ? a.Interior.MaxY : b.Interior.MaxY, high = north ? b.Interior.MinY : a.Interior.MinY;
            return Math.Abs(high - low - WallThickness) < eps && d.Y >= low - eps && d.Y <= high + eps
                && d.X - halfW >= Math.Max(a.Interior.MinX, b.Interior.MinX) - eps && d.X + halfW <= Math.Min(a.Interior.MaxX, b.Interior.MaxX) + eps;
        }

        /// <summary>Distance (plan) from a point to the board: the annular sector of the arc from the bench front to the console back.</summary>
        public static float DistanceToBoard(float x, float y)
        {
            float dx = x - ArcCentreX, dy = y - ArcCentreY;
            float r = (float)Math.Sqrt(dx * dx + dy * dy);
            float inner = ArcRadius - BenchDepth, outer = ArcRadius + BoardBackDepth;
            float t = (float)(Math.Atan2(dx, dy) * 180.0 / Math.PI);
            if (Math.Abs(t) <= ArcHalfSpanDeg) return Math.Max(0f, Math.Max(r - outer, inner - r));
            float best = float.MaxValue;
            foreach (float end in new[] { -ArcHalfSpanDeg, ArcHalfSpanDeg })
            {
                float s = (float)Math.Sin(end * Math.PI / 180.0), c = (float)Math.Cos(end * Math.PI / 180.0);
                best = Math.Min(best, PointToSegment(x, y, ArcCentreX + inner * s, ArcCentreY + inner * c, ArcCentreX + outer * s, ArcCentreY + outer * c));
            }
            return best;
        }

        /// <summary>Distance (plan) from a point to the south-wall mimic plate.</summary>
        public static float DistanceToWallPlate(float x, float y)
        {
            float cx = Math.Min(Math.Max(x, WallPlateMinX), WallPlateMaxX), cy = Math.Min(Math.Max(y, 0f), WallPlateDepth);
            return (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
        }

        private static float PointToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay, len2 = vx * vx + vy * vy;
            float t = len2 <= 0f ? 0f : Math.Min(1f, Math.Max(0f, ((px - ax) * vx + (py - ay) * vy) / len2));
            float qx = ax + vx * t - px, qy = ay + vy * t - py;
            return (float)Math.Sqrt(qx * qx + qy * qy);
        }

        // ---- the window view (R8) ----

        /// <summary>
        /// Solid envelopes of everything the wing adds: each room's interior grown by a wall, and the deck volume (the hall footprint
        /// from the deck up, which holds the railings and the lift landing).
        /// </summary>
        public static IEnumerable<Box> WingEnvelopes()
        {
            foreach (var r in Rooms)
            {
                if (r.Id == ControlRoomId) continue;
                yield return new Box(r.Id, r.Interior.MinX - WallThickness, r.Interior.MinY - WallThickness, r.FloorH,
                                     r.Interior.MaxX + WallThickness, r.Interior.MaxY + WallThickness, r.CeilingH);
            }
            float R = HallDimensions.HallRadius;
            yield return new Box("wing.deck_volume", HallCentreX - R, HallCentreY - R, DeckFloorH, HallCentreX + R, -WallThickness,
                                 DeckFloorH + HallDimensions.HallCeilingAboveDeck);
        }

        // Rays from the eye through a grid of points across the window opening, carried back to the far side of the hall.
        private const int RayColumns = 9, RayRows = 5;

        private static void Eye(HallSightline.View v, out double ex, out double ey, out double eh)
        {
            ex = WindowCentreX - v.along; ey = v.back; eh = v.eyeHeight;
        }

        private static double RayReach(HallSightline.View v) { return v.back + 2.0 * HallDimensions.HallRadius; }

        /// <summary>IDs of the boxes that a sight line from the viewpoint through the window opening passes through. Empty means the view is clear.</summary>
        public static List<string> SightBlockers(HallSightline.View v, IEnumerable<Box> boxes)
        {
            double ex, ey, eh;
            Eye(v, out ex, out ey, out eh);
            double xl = WindowCentreX - HallDimensions.WindowWidth * 0.5, xr = WindowCentreX + HallDimensions.WindowWidth * 0.5;
            double sill = HallDimensions.WindowSill, head = sill + HallDimensions.WindowHeight;
            var hit = new List<string>();
            foreach (var b in boxes)
            {
                double[] min = { b.MinX, b.MinY, b.MinH }, max = { b.MaxX, b.MaxY, b.MaxH };
                bool blocked = false;
                for (int i = 0; i < RayColumns && !blocked; i++)
                    for (int j = 0; j < RayRows && !blocked; j++)
                    {
                        double wx = xl + (xr - xl) * i / (RayColumns - 1), wh = sill + (head - sill) * j / (RayRows - 1);
                        double k = RayReach(v) / v.back;   // the opening is at y 0, so the horizontal run to it is v.back
                        double[] a = { ex, ey, eh }, e = { ex + (wx - ex) * k, ey + (0.0 - ey) * k, eh + (wh - eh) * k };
                        blocked = HallSightline.SegmentHitsBox(a, e, min, max);
                    }
                if (blocked) hit.Add(b.Id);
            }
            return hit;
        }

        /// <summary>Height (above the control-room floor) of the highest sight line through the opening, over the width of the hall.</summary>
        public static double HighestSightHeight(HallSightline.View v)
        {
            double head = HallDimensions.WindowSill + HallDimensions.WindowHeight;
            return v.eyeHeight + (head - v.eyeHeight) * RayReach(v) / v.back;
        }

        /// <summary>Degrees round the hall wall between the bearing that faces the window (north from the hall axis) and the lift opening.</summary>
        public static float OpeningBearingFromWindowDeg()
        {
            return Math.Abs(OperatorMath.DeltaAngle(0f, HallLiftOpening.OutwardHeadingDeg));
        }

        // ---- caps ----

        /// <summary>The names of the caps a measured count exceeds (empty when all are within).</summary>
        public static List<string> OverCap(Caps caps, int triangles, int renderers, int lights, int colliders)
        {
            var over = new List<string>();
            if (triangles > caps.Triangles) over.Add("triangles");
            if (renderers > caps.Renderers) over.Add("renderers");
            if (lights > caps.Lights) over.Add("lights");
            if (colliders > caps.Colliders) over.Add("colliders");
            return over;
        }

        // ---- whole-contract check ----

        /// <summary>Everything that must hold for the route to be walkable and inside its budget. Empty means all true.</summary>
        public static List<string> Validate()
        {
            var bad = new List<string>();
            var seen = new HashSet<string>();
            foreach (var id in AllIds()) if (!seen.Add(id)) bad.Add("duplicate ID " + id);
            foreach (var r in Rooms)
            {
                if (FindSpace(r.SpaceId) == null) bad.Add(r.Id + " names no space");
                if (!Reachable(ControlRoomId, r.Id) || !Reachable(r.Id, ControlRoomId)) bad.Add(r.Id + " is not reachable both ways");
                if (r.MinClearWidth() < 2f * OperatorRadius + 0.5f) bad.Add(r.Id + " is too narrow for the operator");
                if (r.CeilingH - r.FloorH < OperatorMath.StandingEyeHeight + 0.5f) bad.Add(r.Id + " is too low");
            }
            foreach (var d in Doorways)
            {
                var a = FindRoom(d.FromRoomId); var b = FindRoom(d.ToRoomId);
                if (a == null || b == null) { bad.Add(d.Id + " joins an unknown room"); continue; }
                if (d.Width < 1.0f || d.Height < 2.1f) bad.Add(d.Id + " is under 1.0 x 2.1 m");
                if (!DoorwaySitsInWall(d, a, b)) bad.Add(d.Id + " is not in the wall between its rooms");
            }
            foreach (var s in Spaces)
                if (!s.Existing && (s.Caps.Triangles <= 0 || s.Caps.Renderers <= 0 || s.Caps.Lights <= 0 || s.Caps.Colliders <= 0)) bad.Add(s.Id + " is missing a cap");
            var east = FindDoorway("door.control_east");
            if (east != null && DistanceToBoard(RoomWidth - 1.0f, east.Y) < 1.0f) bad.Add("the east doorway approach is within 1 m of the board");
            if (east != null && DistanceToWallPlate(RoomWidth - 1.0f, east.Y - east.Width * 0.5f) < 1.0f) bad.Add("the east doorway approach is within 1 m of the south wall plate");
            foreach (var v in new[] { HallSightline.Stand(), HallSightline.Seat() })
                foreach (var id in SightBlockers(v, WingEnvelopes())) bad.Add("wing geometry " + id + " crosses the window view from the " + v.name);
            if (Math.Abs(FindLanding("landing.lift_upper").FloorH - DeckFloorH) > 1e-3f) bad.Add("the upper landing is not at the deck height");
            if (OpeningBearingFromWindowDeg() < 30f) bad.Add("the lift opening is on the window side of the hall");
            return bad;
        }
    }
}
