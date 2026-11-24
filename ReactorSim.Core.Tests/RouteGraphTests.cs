using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using Xunit;

namespace ReactorSim.Core.Tests
{
    // Phase 2 U1: the pure route contract (RouteGraph) that the wing builders, the walk check and the caps check will read.
    // The scenarios are the U1 list of docs/plans/2026-11-14-ce-phase-2-player-buildings-plan.md.
    public class RouteGraphTests
    {
        private static JsonElement Layout()
        {
            return JsonDocument.Parse(File.ReadAllText(TestRepo.Find("design", "control-room", "layout.json"))).RootElement;
        }

        private static double Num(JsonElement e, params string[] path)
        {
            foreach (var p in path) e = e.GetProperty(p);
            return e.GetDouble();
        }

        // ---- the contract uses the real constants, not drifting copies ----

        [Fact]
        public void ControlRoomConstantsMatchLayoutJson()
        {
            var l = Layout();
            Assert.Equal(RouteGraph.RoomWidth, Num(l, "room", "width"), 6);
            Assert.Equal(RouteGraph.RoomDepth, Num(l, "room", "depth"), 6);
            Assert.Equal(RouteGraph.RoomCeiling, Num(l, "room", "ceiling"), 6);
            Assert.Equal(RouteGraph.WallThickness, Num(l, "room", "wall_thickness"), 6);
        }

        [Fact]
        public void ArcAndBenchConstantsMatchLayoutJson()
        {
            var l = Layout();
            var c = l.GetProperty("arc").GetProperty("center");
            Assert.Equal(RouteGraph.ArcCentreX, c[0].GetDouble(), 6);
            Assert.Equal(RouteGraph.ArcCentreY, c[1].GetDouble(), 6);
            Assert.Equal(RouteGraph.ArcRadius, Num(l, "arc", "radius"), 6);
            Assert.Equal(RouteGraph.ArcHalfSpanDeg, Num(l, "arc", "end_deg"), 6);
            Assert.Equal(RouteGraph.ArcHalfSpanDeg, -Num(l, "arc", "start_deg"), 6);
            Assert.Equal(RouteGraph.BenchDepth, Num(l, "bench", "horizontal_depth"), 6);
        }

        [Fact]
        public void BoardBodyDepthAndWallPlateMatchTheBlenderGenerators()
        {
            // BACK_DEPTH lives in room_layout.py, the south-wall plate in make_control_room.py: pin the numbers the contract copies.
            string room = File.ReadAllText(TestRepo.Find("Blender", "scripts", "room_layout.py"));
            var m = Regex.Match(room, @"^BACK_DEPTH\s*=\s*([0-9.]+)", RegexOptions.Multiline);
            Assert.True(m.Success, "BACK_DEPTH not found in room_layout.py");
            Assert.Equal(RouteGraph.BoardBackDepth, double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 6);

            string cr = File.ReadAllText(TestRepo.Find("Blender", "scripts", "make_control_room.py"));
            Assert.Contains("wx0, wx1 = dx + 0.9, W - 0.6", cr);
            Assert.Contains("mx, mz, mw, mh = (wx0 + wx1) / 2, 1.75, 1.7, 0.95", cr);
            // dx = W / 2: x 6.9 .. 11.4, plate 1.7 wide centred at 9.15, 0.95 high centred at 1.75.
            Assert.Equal(8.3, RouteGraph.WallPlateMinX, 6);
            Assert.Equal(10.0, RouteGraph.WallPlateMaxX, 6);
            Assert.Equal(1.275, RouteGraph.WallPlateMinH, 6);
            Assert.Equal(2.225, RouteGraph.WallPlateMaxH, 6);
        }

        [Fact]
        public void TextReadabilityConstantMatchesLayoutJson()
        {
            Assert.Equal(RouteGraph.MinTextDeg, Num(Layout(), "checks", "min_text_deg"), 6);
            // The walking body is a 0.3 m radius capsule (OperatorController); the clearances are sized from it.
            string oc = File.ReadAllText(TestRepo.Find("ReactorSim.Unity", "Assets", "Scripts", "View3D", "OperatorController.cs"));
            Assert.Contains("body.radius = " + RouteGraph.OperatorRadius.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "f;", oc);
        }

        // ---- reachability: every room, both ways, no dead ends ----

        [Fact]
        public void EveryRoomIsReachableFromTheControlRoomAndBack()
        {
            foreach (var r in RouteGraph.Rooms)
            {
                Assert.True(RouteGraph.Reachable(RouteGraph.ControlRoomId, r.Id), "no route control room -> " + r.Id);
                Assert.True(RouteGraph.Reachable(r.Id, RouteGraph.ControlRoomId), "no route " + r.Id + " -> control room");
            }
        }

        [Fact]
        public void NoRoomIsADeadEndAndEveryDoorwayJoinsTwoKnownRooms()
        {
            foreach (var r in RouteGraph.Rooms)
                Assert.True(RouteGraph.DoorwaysOf(r.Id).Any(), r.Id + " has no doorway");
            foreach (var d in RouteGraph.Doorways)
            {
                Assert.NotNull(RouteGraph.FindRoom(d.FromRoomId));
                Assert.NotNull(RouteGraph.FindRoom(d.ToRoomId));
                Assert.NotEqual(d.FromRoomId, d.ToRoomId);
            }
            // The graph is a tree rooted at the control room: n rooms, n - 1 doorways, so a way out is the way back (R1).
            Assert.Equal(RouteGraph.Rooms.Count - 1, RouteGraph.Doorways.Count);
        }

        [Fact]
        public void TheControlRoomHasOneWayOutAndTheCorridorReachesTheLiftLobbyAndTheTurbineHall()
        {
            var out1 = RouteGraph.DoorwaysOf(RouteGraph.ControlRoomId).ToList();
            Assert.Single(out1);
            Assert.Equal("door.control_east", out1[0].Id);
            var corridor = RouteGraph.DoorwaysOf("room.corridor").Select(d => RouteGraph.Other(d, "room.corridor")).ToList();
            Assert.Contains("room.lift_lobby", corridor);
            Assert.Contains("room.turbine_hall", corridor);
            Assert.Contains(RouteGraph.ControlRoomId, corridor);
        }

        // ---- the east doorway ----

        [Fact]
        public void TheEastDoorwayIsBigEnoughForTheOperator()
        {
            var d = RouteGraph.FindDoorway("door.control_east");
            Assert.True(d.Width >= 1.0f, "doorway " + d.Width + " m wide");
            Assert.True(d.Height >= 2.1f, "doorway " + d.Height + " m high");
            Assert.True(d.Height < RouteGraph.RoomCeiling);
            // An operator body is a 0.3 m radius capsule (OperatorController): the clear width passes it with a margin each side.
            Assert.True(d.Width >= 2f * RouteGraph.OperatorRadius + 0.2f);
            // In the east wall (x between the room interior and the corridor interior), near y 2 m.
            Assert.InRange(d.X, RouteGraph.RoomWidth, RouteGraph.RoomWidth + RouteGraph.WallThickness + 1e-4f);
            Assert.InRange(d.Y, 1.5f, 2.5f);
            Assert.True(d.Y - d.Width * 0.5f > 0f && d.Y + d.Width * 0.5f < RouteGraph.RoomDepth);
        }

        [Fact]
        public void TheEastDoorwayAndItsApproachAreClearOfTheBoardArcAndTheWallPlate()
        {
            var d = RouteGraph.FindDoorway("door.control_east");
            // The approach apron: the aperture's width and 1.0 m back into the room.
            float x0 = RoomInnerEast() - 1.0f, x1 = RoomInnerEast();
            float y0 = d.Y - d.Width * 0.5f, y1 = d.Y + d.Width * 0.5f;
            float worstBoard = float.MaxValue, worstPlate = float.MaxValue;
            for (int i = 0; i <= 10; i++)
                for (int j = 0; j <= 10; j++)
                {
                    float x = x0 + (x1 - x0) * i / 10f, y = y0 + (y1 - y0) * j / 10f;
                    worstBoard = Math.Min(worstBoard, RouteGraph.DistanceToBoard(x, y));
                    worstPlate = Math.Min(worstPlate, RouteGraph.DistanceToWallPlate(x, y));
                }
            Assert.True(worstBoard >= 1.0f, "the doorway approach comes within " + worstBoard + " m of the board");
            Assert.True(worstPlate >= 1.0f, "the doorway approach comes within " + worstPlate + " m of the south wall plate");
            // The aperture itself is more than a capsule width from the board body.
            Assert.True(RouteGraph.DistanceToBoard(RoomInnerEast(), d.Y) >= 2.0f * RouteGraph.OperatorRadius + 0.5f);
        }

        [Fact]
        public void TheBoardDistanceFunctionSeesTheBoard()
        {
            // Negative control: on the board's face, mid-arc, and 2 m behind it.
            Assert.Equal(0f, RouteGraph.DistanceToBoard(RouteGraph.ArcCentreX, RouteGraph.ArcCentreY + RouteGraph.ArcRadius), 3);
            Assert.True(RouteGraph.DistanceToBoard(RouteGraph.ArcCentreX, RouteGraph.ArcCentreY) > 3f);
            Assert.Equal(0.1f, RouteGraph.DistanceToBoard(RouteGraph.ArcCentreX, RouteGraph.ArcCentreY + RouteGraph.ArcRadius + RouteGraph.BoardBackDepth + 0.1f), 3);
            Assert.Equal(0.5f, RouteGraph.DistanceToWallPlate(RouteGraph.WallPlateMinX - 0.5f, 0.0f), 3);
        }

        private static float RoomInnerEast() { return RouteGraph.RoomWidth; }

        // ---- the window view ----

        [Fact]
        public void NoWingGeometryCrossesTheWindowsFramedBandFromTheStandOrTheSeat()
        {
            foreach (var v in new[] { HallSightline.Stand(), HallSightline.Seat() })
                Assert.Empty(RouteGraph.SightBlockers(v, RouteGraph.WingEnvelopes()));
        }

        [Fact]
        public void TheSightCheckSeesABoxPlacedInTheView()
        {
            // Negative control: a box straight behind the opening, in front of the seat, is reported.
            float wx = RouteGraph.WindowCentreX;
            var seat = HallSightline.Seat();
            var blocker = new RouteGraph.Box("test.blocker", wx - 1f, -6f, 0f, wx + 1f, -5f, 10f);
            Assert.NotEmpty(RouteGraph.SightBlockers(seat, new[] { blocker }));
        }

        [Fact]
        public void TheDeckAdditionsSitAboveEverySightLineThroughTheWindow()
        {
            // The deck volume (railings and landing) starts at the deck height; the highest sight line through the opening stays below it.
            var deck = RouteGraph.WingEnvelopes().Single(b => b.Id == "wing.deck_volume");
            Assert.Equal(RouteGraph.DeckFloorH, deck.MinH, 4);
            foreach (var v in new[] { HallSightline.Stand(), HallSightline.Seat() })
                Assert.True(RouteGraph.HighestSightHeight(v) < RouteGraph.DeckFloorH, v.name + " sees up to " + RouteGraph.HighestSightHeight(v) + " m");
        }

        [Fact]
        public void TheLiftOpeningIsOneOpeningOnTheHallCircleAwayFromTheWindowWall()
        {
            var o = RouteGraph.HallLiftOpening;
            Assert.Equal("opening.hall_lift", o.Id);
            // On the hall wall (the circle), not on the control-room wall plane that carries the window.
            float r = (float)Math.Sqrt((o.X - RouteGraph.HallCentreX) * (o.X - RouteGraph.HallCentreX) + (o.Y - RouteGraph.HallCentreY) * (o.Y - RouteGraph.HallCentreY));
            Assert.Equal(HallDimensions.HallRadius, r, 2);
            Assert.True(o.Y < -RouteGraph.WallThickness - 1.0f, "the opening is on the chord wall");
            // At least 30 degrees round the circle from the bearing that faces the window (north, heading 0).
            Assert.True(RouteGraph.OpeningBearingFromWindowDeg() >= 30f, "opening is " + RouteGraph.OpeningBearingFromWindowDeg() + " deg from the window bearing");
            // The window's own x range is far from the opening.
            Assert.True(o.X > RouteGraph.WindowCentreX + HallDimensions.WindowWidth);
            Assert.Equal(RouteGraph.DeckFloorH, o.FloorH, 4);
            Assert.True(o.Width >= 1.0f && o.Height >= 2.1f);
        }

        // ---- heights ----

        [Fact]
        public void LiftLandingsMatchTheRoomFloorAndTheExistingDeckHeight()
        {
            var lower = RouteGraph.FindLanding("landing.lift_lower");
            var upper = RouteGraph.FindLanding("landing.lift_upper");
            Assert.Equal(0f, lower.FloorH, 4);   // the control-room floor is the datum
            // The deck is WaterDepth + DeckAboveWater over the pool floor; the room floor is RoomFloorAbovePoolFloor over it.
            float deck = HallDimensions.WaterDepth + HallDimensions.DeckAboveWater - HallDimensions.RoomFloorAbovePoolFloor;
            Assert.Equal(deck, upper.FloorH, 3);
            Assert.Equal(deck, RouteGraph.DeckFloorH, 3);
            Assert.InRange(upper.FloorH, 16.0f, 16.2f);   // the plan's "about 16 m above the control-room floor"
            Assert.Equal(upper.FloorH, RouteGraph.FindRoom("room.reactor_deck").FloorH, 4);
            Assert.Equal(lower.FloorH, RouteGraph.FindRoom("room.lift_lobby").FloorH, 4);
            Assert.Equal(lower.FloorH, RouteGraph.FindRoom(RouteGraph.ControlRoomId).FloorH, 4);
            Assert.Equal(lower.FloorH, RouteGraph.FindRoom("room.corridor").FloorH, 4);
            Assert.Equal(lower.FloorH, RouteGraph.FindRoom("room.turbine_hall").FloorH, 4);
        }

        [Fact]
        public void TheLiftCarSpansBothLandingsAndArrivesFacingOutOfItsDoor()
        {
            var car = RouteGraph.FindRoom("room.lift");
            var lower = RouteGraph.FindLanding("landing.lift_lower");
            var upper = RouteGraph.FindLanding("landing.lift_upper");
            Assert.Equal(lower.FloorH, car.FloorH, 4);
            Assert.Equal(upper.FloorH, car.TravelTopH, 4);
            Assert.True(car.CeilingH >= upper.FloorH + RouteGraph.RoomCeiling - 1e-3f);
            Assert.Equal("door.lobby_lift", lower.DoorwayId);
            Assert.Equal("door.lift_deck", upper.DoorwayId);
            foreach (var l in new[] { lower, upper })
            {
                var d = RouteGraph.FindDoorway(l.DoorwayId);
                Assert.Equal(d.HeadingFromTo(RouteGraph.Other(d, "room.lift") == d.ToRoomId), l.ExitHeadingDeg, 3);
                // The car interior holds the landing stand point.
                Assert.True(RouteGraph.Inside(car, l.X, l.Y, 0f));
            }
        }

        [Fact]
        public void ClearancesFitTheOperatorEverywhere()
        {
            foreach (var d in RouteGraph.Doorways)
            {
                Assert.True(d.Width >= 1.0f, d.Id + " width " + d.Width);
                Assert.True(d.Height >= 2.1f, d.Id + " height " + d.Height);
                var a = RouteGraph.FindRoom(d.FromRoomId); var b = RouteGraph.FindRoom(d.ToRoomId);
                Assert.True(d.Height <= Math.Min(a.CeilingH - a.FloorH, b.CeilingH - b.FloorH) + 1e-3f, d.Id + " is taller than its rooms");
            }
            foreach (var r in RouteGraph.Rooms)
            {
                Assert.True(r.MinClearWidth() >= 2f * RouteGraph.OperatorRadius + 0.5f, r.Id + " is " + r.MinClearWidth() + " m across");
                Assert.True(r.CeilingH - r.FloorH >= OperatorMath.StandingEyeHeight + 0.5f, r.Id + " is too low");
            }
            Assert.Equal(16.0f, RouteGraph.FindRoom("room.corridor").Interior.MaxX - RouteGraph.FindRoom("room.corridor").Interior.MinX, 3);
        }

        // ---- geometry consistency ----

        [Fact]
        public void DoorwaysSitInTheWallBetweenTheirRoomsAndLandOnTheirFloors()
        {
            foreach (var d in RouteGraph.Doorways)
            {
                var a = RouteGraph.FindRoom(d.FromRoomId); var b = RouteGraph.FindRoom(d.ToRoomId);
                Assert.True(RouteGraph.DoorwaySitsInWall(d, a, b), d.Id + " is not in the wall between " + a.Id + " and " + b.Id);
                Assert.Equal(d.FloorH, Math.Max(a.FloorH, b.FloorH), 3);
            }
        }

        [Fact]
        public void RoomsDoNotOverlapAndTheWingStaysOutsideTheHallShell()
        {
            var rooms = RouteGraph.Rooms.ToList();
            for (int i = 0; i < rooms.Count; i++)
                for (int j = i + 1; j < rooms.Count; j++)
                    Assert.False(RouteGraph.PlanOverlap(rooms[i], rooms[j]), rooms[i].Id + " overlaps " + rooms[j].Id);
            float outer = HallDimensions.HallRadius + RouteGraph.HallShellThickness;
            foreach (var r in rooms)
            {
                if (r.Id == "room.reactor_deck" || r.Id == RouteGraph.ControlRoomId) continue;   // the deck is inside the hall by definition; the control room is the wall plane the hall is cut by
                float d = RouteGraph.DistanceFromHallCentre(r, RouteGraph.WallThickness);
                Assert.True(d >= outer + 0.05f, r.Id + " envelope is " + d + " m from the hall axis, shell outer face is " + outer);
            }
        }

        [Fact]
        public void EveryRoutePointIsInsideItsRoomAtItsFloorHeight()
        {
            foreach (var p in RouteGraph.RoutePoints)
            {
                var r = RouteGraph.FindRoom(p.RoomId);
                Assert.NotNull(r);
                Assert.InRange(p.FloorH, r.FloorH - 1e-3f, r.TravelTopH + 1e-3f);
                if (r.TravelTopH == r.FloorH) Assert.Equal(r.FloorH, p.FloorH, 3);
                Assert.Equal(OperatorMath.StandingEyeHeight, p.EyeHeight, 3);
                if (p.Id == "route.deck_rim") Assert.True(RouteGraph.OnDeckRim(p.X, p.Y));
                else Assert.True(RouteGraph.Inside(r, p.X, p.Y, 0f), p.Id + " is outside " + r.Id);
            }
        }

        [Fact]
        public void NamePlatesAreReadableFromTheirApproachAndClearOfTheOperatorsHead()
        {
            Assert.Equal(3, RouteGraph.NamePlates.Count);   // control-room doorway, lift lobby, turbine-hall entrance (R11)
            double tan = Math.Tan(RouteGraph.MinTextDeg * Math.PI / 180.0);
            foreach (var p in RouteGraph.NamePlates)
            {
                Assert.False(string.IsNullOrWhiteSpace(p.Text));
                Assert.True(p.TextHeightM >= tan * p.ApproachM, p.Id + " text " + p.TextHeightM + " m is under " + RouteGraph.MinTextDeg + " deg at " + p.ApproachM + " m");
                Assert.True(p.PlateHeight >= p.TextHeightM + 0.04f);
                Assert.True(p.CentreH - p.PlateHeight * 0.5f >= OperatorMath.StandingEyeHeight + 0.5f, p.Id + " hangs where it could be walked into");
                var r = RouteGraph.FindRoom(p.RoomId);
                Assert.True(p.CentreH + p.PlateHeight * 0.5f <= r.CeilingH, p.Id + " is above the ceiling");
                Assert.True(RouteGraph.Inside(r, p.X, p.Y, 0.25f), p.Id + " is not in " + r.Id);
                var d = RouteGraph.FindDoorway(p.DoorwayId);
                Assert.Contains(p.RoomId, new[] { d.FromRoomId, d.ToRoomId });
            }
            Assert.Equal(new[] { "door.control_east", "door.corridor_lobby", "door.corridor_turbine" }, RouteGraph.NamePlates.Select(p => p.DoorwayId).OrderBy(s => s, StringComparer.Ordinal).ToArray());
        }

        // ---- stable IDs ----

        [Fact]
        public void EveryRoomDoorwayLandingAndNamePlateHasAUniqueStableId()
        {
            var ids = RouteGraph.AllIds().ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
            foreach (var id in ids) Assert.Matches(@"^[a-z]+\.[a-z_]+$", id);
            // Pinned: Phase 3 persistence keys on these strings, so a rename must fail a test.
            Assert.Equal(new[]
            {
                "space.control_room", "space.reactor_hall", "space.wing_corridor", "space.wing_lift", "space.wing_deck", "space.wing_turbine_hall",
                "room.control", "room.corridor", "room.lift_lobby", "room.lift", "room.reactor_deck", "room.turbine_hall",
                "door.control_east", "door.corridor_lobby", "door.lobby_lift", "door.lift_deck", "door.corridor_turbine",
                "opening.hall_lift",
                "landing.lift_lower", "landing.lift_upper",
                "plate.control_door", "plate.lift_lobby", "plate.turbine_entrance",
                "route.door_east", "route.corridor_mid", "route.lift_lobby", "route.lift_lower", "route.lift_upper",
                "route.deck_rim", "route.turbine_entrance", "route.turbine_floor"
            }, ids.ToArray());
        }

        // ---- caps ----

        [Fact]
        public void EveryNewSpaceHasAllFourPositiveCaps()
        {
            var wing = RouteGraph.Spaces.Where(s => !s.Existing).ToList();
            Assert.Equal(4, wing.Count);   // corridor, lift lobby + car, deck additions, turbine hall (KTD4 roots)
            foreach (var s in wing)
            {
                Assert.True(s.Caps.Triangles > 0 && s.Caps.Renderers > 0 && s.Caps.Lights > 0 && s.Caps.Colliders > 0, s.Id + " is missing a cap");
                Assert.StartsWith("Wing_", s.RootName);
            }
            Assert.Equal(new[] { "Wing_Corridor", "Wing_Lift", "Wing_Deck", "Wing_TurbineHall" }, wing.Select(s => s.RootName).ToArray());
            foreach (var r in RouteGraph.Rooms) Assert.NotNull(RouteGraph.FindSpace(r.SpaceId));
        }

        [Fact]
        public void CapsAreSizedFromTheExistingHallCaps()
        {
            var wing = RouteGraph.Spaces.Where(s => !s.Existing).ToList();
            int tris = wing.Sum(s => s.Caps.Triangles);
            Assert.True(tris <= HallDimensions.MaxHallTriangles, "the wing's triangle caps sum to " + tris + ", more than the hall's " + HallDimensions.MaxHallTriangles);
            foreach (var s in wing)
            {
                Assert.True(s.Caps.Triangles <= HallDimensions.MaxHallTriangles / 2, s.Id + " alone is over half the hall budget");
                Assert.True(s.Caps.Lights <= HallDimensions.MaxRealtimeLights - 1, s.Id + " takes every pixel light");
            }
            // Wing lights light only the Wing layer, but any two touching spaces can overlap in view: their lights stay inside the pixel-light count.
            foreach (var d in RouteGraph.Doorways)
            {
                var a = RouteGraph.FindSpace(RouteGraph.FindRoom(d.FromRoomId).SpaceId);
                var b = RouteGraph.FindSpace(RouteGraph.FindRoom(d.ToRoomId).SpaceId);
                if (a.Id == b.Id || a.Existing || b.Existing) continue;
                Assert.True(a.Caps.Lights + b.Caps.Lights <= HallDimensions.MaxRealtimeLights, a.Id + " + " + b.Id + " light caps");
            }
            // The existing spaces keep their caps untouched.
            var hall = RouteGraph.FindSpace("space.reactor_hall");
            Assert.True(hall.Existing);
            Assert.Equal(HallDimensions.MaxHallTriangles, hall.Caps.Triangles);
            Assert.Equal(HallDimensions.MaxRealtimeLights, hall.Caps.Lights);
            Assert.True(RouteGraph.FindSpace("space.control_room").Existing);
        }

        [Fact]
        public void CapCheckFailsWhenAMeasuredCountIsOverItsCap()
        {
            // Negative control for the check U5 will run: the comparison itself.
            var caps = RouteGraph.FindSpace("space.wing_corridor").Caps;
            Assert.Empty(RouteGraph.OverCap(caps, caps.Triangles, caps.Renderers, caps.Lights, caps.Colliders));
            Assert.Equal(new[] { "triangles" }, RouteGraph.OverCap(caps, caps.Triangles + 1, caps.Renderers, caps.Lights, caps.Colliders).ToArray());
            Assert.Equal(new[] { "renderers", "lights", "colliders" }, RouteGraph.OverCap(caps, 0, caps.Renderers + 1, caps.Lights + 1, caps.Colliders + 1).ToArray());
        }

        // ---- the whole contract ----

        [Fact]
        public void ValidateFindsNothingWrongWithTheShippedContract()
        {
            Assert.Empty(RouteGraph.Validate());
        }

        // ---- layout.json carries the doorway and the new operating points, and leaves the old anchors alone ----

        [Fact]
        public void LayoutJsonCarriesTheDoorwayAndTheRoutePointsOfTheContract()
        {
            var l = Layout();
            var dj = l.GetProperty("doorways").GetProperty("east");
            var d = RouteGraph.FindDoorway("door.control_east");
            Assert.Equal(d.Id, dj.GetProperty("id").GetString());
            Assert.Equal(d.HeadingDeg, (float)dj.GetProperty("heading_deg").GetDouble(), 1);
            Assert.Equal(d.ToRoomId, dj.GetProperty("leads_to").GetString());
            Assert.Equal(d.X, (float)dj.GetProperty("pos")[0].GetDouble(), 3);
            Assert.Equal(d.Y, (float)dj.GetProperty("pos")[1].GetDouble(), 3);
            Assert.Equal(d.Width, (float)dj.GetProperty("width").GetDouble(), 3);
            Assert.Equal(d.Height, (float)dj.GetProperty("height").GetDouble(), 3);
            var ops = l.GetProperty("route_points");
            foreach (var p in RouteGraph.RoutePoints)
            {
                var o = ops.GetProperty(p.LayoutKey);
                Assert.Equal(p.X, (float)o.GetProperty("pos")[0].GetDouble(), 0.01f);
                Assert.Equal(p.Y, (float)o.GetProperty("pos")[1].GetDouble(), 0.01f);
                Assert.Equal(p.FloorH, (float)o.GetProperty("floor_h").GetDouble(), 0.01f);
                Assert.Equal(p.EyeHeight, (float)o.GetProperty("eye_height").GetDouble(), 3);
                Assert.Equal(p.HeadingDeg, (float)o.GetProperty("heading_deg").GetDouble(), 1);
                Assert.Equal(p.Id, o.GetProperty("route_id").GetString());
                Assert.Equal(p.RoomId, o.GetProperty("room").GetString());
                // Route points are not board stops: they stay out of operating_points, which every board-facing check iterates.
                Assert.False(l.GetProperty("operating_points").TryGetProperty(p.LayoutKey, out _), p.LayoutKey + " must not be an operating point");
            }
        }

        [Fact]
        public void TheExistingControlRoomAnchorsAreUnchanged()
        {
            // Pinned from origin/master: the route work only adds points.
            var ops = Layout().GetProperty("operating_points");
            var pinned = new Dictionary<string, double[]>
            {
                { "STAND_reactor", new[] { 2.514, 6.064 } }, { "STAND_primary", new[] { 4.137, 7.835 } }, { "STAND_safety", new[] { 5.559, 8.295 } },
                { "STAND_steam", new[] { 7.074, 8.166 } }, { "STAND_turbine", new[] { 8.536, 7.358 } }, { "STAND_electrical", new[] { 9.578, 5.84 } },
                { "SEAT_A1", new[] { 2.514, 6.064 } }, { "SEAT_A2", new[] { 8.994, 6.874 } }, { "STAND_Center", new[] { 6.0, 4.5 } }
            };
            foreach (var kv in pinned)
            {
                var pos = ops.GetProperty(kv.Key).GetProperty("pos");
                Assert.Equal(kv.Value[0], pos[0].GetDouble(), 3);
                Assert.Equal(kv.Value[1], pos[1].GetDouble(), 3);
            }
            Assert.True(ops.TryGetProperty("SPAWN_A1", out _));
        }

        // ---- cap evaluation (U5): WingCheck measures, RouteGraph decides ----

        private static Dictionary<string, RouteGraph.Caps> AtCaps()
        {
            var m = new Dictionary<string, RouteGraph.Caps>();
            foreach (var s in RouteGraph.Spaces) if (!s.Existing) m[s.Id] = s.Caps;
            return m;
        }

        [Fact]
        public void CountsAtEveryCapPass()
        {
            Assert.Empty(RouteGraph.EvaluateCaps(AtCaps()));
        }

        [Fact]
        public void OneOverAnyCapFailsAndNamesTheSpaceAndMetric()
        {
            var m = AtCaps();
            var c = m["space.wing_deck"];
            m["space.wing_deck"] = new RouteGraph.Caps(c.Triangles, c.Renderers, c.Lights + 1, c.Colliders);
            var bad = RouteGraph.EvaluateCaps(m);
            Assert.Single(bad);
            Assert.Contains("space.wing_deck", bad[0]);
            Assert.Contains("lights", bad[0]);
        }

        [Fact]
        public void EveryMetricOverItsCapIsReported()
        {
            var m = AtCaps();
            var c = m["space.wing_turbine_hall"];
            m["space.wing_turbine_hall"] = new RouteGraph.Caps(c.Triangles + 1, c.Renderers + 1, c.Lights + 1, c.Colliders + 1);
            Assert.Equal(4, RouteGraph.EvaluateCaps(m).Count);
        }

        [Fact]
        public void ANewSpaceWithNoMeasurementFails()
        {
            var m = AtCaps();
            m.Remove("space.wing_corridor");
            var bad = RouteGraph.EvaluateCaps(m);
            Assert.Single(bad);
            Assert.Contains("space.wing_corridor", bad[0]);
        }

        [Fact]
        public void AnUnknownSpaceIdFails()
        {
            var m = AtCaps();
            m["space.nowhere"] = new RouteGraph.Caps(0, 0, 0, 0);
            var bad = RouteGraph.EvaluateCaps(m);
            Assert.Single(bad);
            Assert.Contains("space.nowhere", bad[0]);
        }

        [Fact]
        public void ExistingSpacesAreNotHeldToWingCaps()
        {
            // The control room has no wing caps and the hall's are not numeric here (they live in HallDimensions).
            var m = AtCaps();
            m["space.control_room"] = new RouteGraph.Caps(1000000, 1000, 100, 1000);
            m["space.reactor_hall"] = new RouteGraph.Caps(1000000, 1000, 100, 1000);
            Assert.Empty(RouteGraph.EvaluateCaps(m));
        }

        [Fact]
        public void NoMeasurementsAtAllFailsForEveryNewSpace()
        {
            int fresh = 0;
            foreach (var s in RouteGraph.Spaces) if (!s.Existing) fresh++;
            Assert.Equal(fresh, RouteGraph.EvaluateCaps(new Dictionary<string, RouteGraph.Caps>()).Count);
            Assert.Equal(fresh, RouteGraph.EvaluateCaps(null).Count);
        }
    }
}
