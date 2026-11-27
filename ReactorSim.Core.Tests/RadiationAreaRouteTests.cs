using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static ReactorSim.Tests.RadiationTestPlants;

namespace ReactorSim.Core.Tests
{
    // Phase 4 U1: the Core radiation areas and source points are pinned to RouteGraph and HallDimensions, which Core cannot reference
    // (KTD6). The numbers Core copies into Physics.cs fail here if the Unity-side geometry moves.
    public class RadiationAreaRouteTests
    {
        private static double WaterSurfaceH { get { return HallDimensions.WaterDepth - HallDimensions.RoomFloorAbovePoolFloor; } }

        [Fact]
        public void EveryCoreAreaId_ExceptThePool_IsARouteGraphId()
        {
            var ids = new HashSet<string>(RouteGraph.AllIds());
            foreach (string a in RadiationAreas.AllAreaIds)
            {
                if (a == RadiationAreas.ReactorPoolId) continue;
                Assert.Contains(a, ids);
            }
            Assert.DoesNotContain(RadiationAreas.ReactorPoolId, ids);
        }

        [Fact]
        public void ThePoolArea_NamesTheReactorHallSpaceAsParent_AndThatSpaceExists()
        {
            string parent = RadiationAreas.ParentOf(RadiationAreas.ReactorPoolId);
            Assert.Equal("space.reactor_hall", parent);
            Assert.NotNull(RouteGraph.FindSpace(parent));
            Assert.Null(RadiationAreas.ParentOf(RadiationAreas.ControlRoomId));
        }

        [Fact]
        public void EveryRoomOfTheRoute_IsARadiationArea()
        {
            foreach (var r in RouteGraph.Rooms) Assert.True(RadiationAreas.IsKnown(r.Id), r.Id);
            Assert.True(RadiationAreas.IsKnown("space.reactor_hall"));
        }

        [Fact]
        public void CopiedHallGeometryMatchesRouteGraphAndHallDimensions()
        {
            Assert.Equal(Physics.RadHallCentreXM, RouteGraph.HallCentreX, 3);
            Assert.Equal(Physics.RadHallCentreYM, RouteGraph.HallCentreY, 3);
            Assert.Equal(Physics.RadDeckFloorHM, RouteGraph.DeckFloorH, 1);
            Assert.Equal(Physics.RadWaterSurfaceHM, WaterSurfaceH, 2);
            Assert.Equal(Physics.RadPoolRadiusM, HallDimensions.PoolRadius, 3);
            Assert.Equal(Physics.RadHallRadiusM, HallDimensions.HallRadius, 3);
            Assert.Equal(Physics.RadHallCeilingAboveDeckM, HallDimensions.HallCeilingAboveDeck, 3);
            Assert.Equal(Physics.RadCoreHM, HallDimensions.SightTargetHeight - HallDimensions.RoomFloorAbovePoolFloor, 2);
            Assert.Equal(Physics.RadModuleYM, -RouteGraph.WallThickness - HallDimensions.ModuleFromWall, 3);
            Assert.Equal(Physics.RadModuleXM, RouteGraph.HallCentreX, 3);
        }

        [Fact]
        public void RoomAreaSourcePoints_LieInsideTheirRoom_AndHallSourcesInsideTheHall()
        {
            foreach (var s in SourceRegister.Default.Sources)
            {
                var room = RouteGraph.FindRoom(s.AreaId);
                if (room != null)
                {
                    var b = room.Interior;
                    Assert.InRange(s.X, b.MinX, b.MaxX); Assert.InRange(s.Y, b.MinY, b.MaxY); Assert.InRange(s.H, b.MinH, b.MaxH);
                }
                else if (s.AreaId == RadiationAreas.ReactorHallId)
                {
                    Assert.True(Radial(s.X, s.Y) < HallDimensions.HallRadius, s.Id);
                    Assert.InRange(s.H, WaterSurfaceH, RouteGraph.DeckFloorH + HallDimensions.HallCeilingAboveDeck);
                }
                else
                {
                    Assert.Equal(RadiationAreas.ReactorPoolId, s.AreaId);
                    Assert.True(Radial(s.X, s.Y) < HallDimensions.PoolRadius, s.Id);
                    Assert.InRange(s.H, -HallDimensions.RoomFloorAbovePoolFloor, WaterSurfaceH);
                }
            }
        }

        [Fact]
        public void ReferencePoints_LieInsideTheirArea()
        {
            foreach (string a in RadiationAreas.AllAreaIds)
            {
                RadPoint p;
                Assert.True(RadiationAreas.TryGetReferencePoint(a, out p), a);
                var room = RouteGraph.FindRoom(a);
                if (room != null)
                {
                    var b = room.Interior;
                    Assert.InRange(p.X, b.MinX, b.MaxX); Assert.InRange(p.Y, b.MinY, b.MaxY); Assert.InRange(p.H, b.MinH, b.MaxH);
                }
                else if (a == RadiationAreas.ReactorHallId)
                {
                    Assert.True(Radial(p.X, p.Y) < HallDimensions.HallRadius);
                    Assert.InRange(p.H, WaterSurfaceH, RouteGraph.DeckFloorH + HallDimensions.HallCeilingAboveDeck);
                }
                else
                {
                    Assert.True(Radial(p.X, p.Y) < HallDimensions.PoolRadius);
                    Assert.InRange(p.H, -HallDimensions.RoomFloorAbovePoolFloor, WaterSurfaceH);
                }
            }
        }

        [Fact]
        public void RoomReferencePoints_AreCloseToTheirRoutePoint_WhereOneExists()
        {
            foreach (var rp in RouteGraph.RoutePoints)
            {
                RadPoint p;
                Assert.True(RadiationAreas.TryGetReferencePoint(rp.RoomId, out p), rp.RoomId);
                var room = RouteGraph.FindRoom(rp.RoomId);
                Assert.InRange(p.H, room.FloorH, room.CeilingH);
            }
            RadPoint control;
            RadiationAreas.TryGetReferencePoint("room.control", out control);
            var door = RouteGraph.RoutePoints.First(r => r.Id == "route.door_east");
            Assert.Equal(door.X, control.X, 2);
            Assert.Equal(door.Y, control.Y, 2);
        }

        [Fact]
        public void EveryRoutePoint_ReadsAvailableAtPower()
        {
            var plant = AtPower();
            foreach (var rp in RouteGraph.RoutePoints)
            {
                var r = DoseField.Evaluate(plant, rp.RoomId, rp.X, rp.Y, rp.FloorH + 1.0);
                Assert.True(r.Available, rp.Id);
                Assert.True(r.RateMSvPerH >= Physics.RadBackgroundMSvPerH, rp.Id);
            }
        }

        [Fact]
        public void ControlRoomRoutePoint_AtPower_IsBelowTheRadiationAreaThreshold()
        {
            var rp = RouteGraph.RoutePoints.First(r => r.Id == "route.door_east");
            var r = DoseField.Evaluate(AtPower(), rp.RoomId, rp.X, rp.Y, rp.FloorH + 1.0);
            Assert.True(r.RateMSvPerH >= Physics.RadBackgroundMSvPerH);
            Assert.True(r.RateMSvPerH < Physics.RadAreaThresholdMSvPerH);
        }

        [Fact]
        public void DeckRimRoutePoint_AtPower_IsAtOrAboveTheRadiationAreaThreshold()
        {
            var rp = RouteGraph.RoutePoints.First(r => r.Id == "route.deck_rim");
            var r = DoseField.Evaluate(AtPower(), rp.RoomId, rp.X, rp.Y, rp.FloorH + 1.0);
            Assert.True(r.RateMSvPerH >= Physics.RadAreaThresholdMSvPerH, r.RateMSvPerH.ToString());
        }

        [Fact]
        public void DeckReference_IsNearTheRouteRimPoint()
        {
            RadPoint p;
            Assert.True(RadiationAreas.TryGetReferencePoint(RadiationAreas.ReactorDeckId, out p));
            var rim = RouteGraph.RoutePoints.First(r => r.Id == "route.deck_rim");
            Assert.True(Math.Sqrt((p.X - rim.X) * (p.X - rim.X) + (p.Y - rim.Y) * (p.Y - rim.Y)) < 2.0);
        }

        private static double Radial(double x, double y)
        {
            double dx = x - RouteGraph.HallCentreX, dy = y - RouteGraph.HallCentreY;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
