using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using ReactorSim.Session;
using Xunit;

namespace ReactorSim.Tests
{
    public class WorldSnapshotTests
    {
        private static CrewMember Crew(string id, string name = "Operator", string loc = "control-room", CrewPose pose = CrewPose.Standing)
        {
            return new CrewMember { PeerId = id, DisplayName = name, LocationId = loc, X = 1.5, Y = 0.0, Z = -2.25, YawDeg = 90.0, Pose = pose };
        }

        private static Simulation RunningSim()
        {
            var sim = new Simulation();
            sim.LoadScenarioPreset("full-power");
            for (int i = 0; i < 20; i++) sim.Tick();
            return sim;
        }

        private static WorldSnapshot RoundTrip(WorldSnapshot w, out string body)
        {
            body = w.Encode();
            var parsed = SessionJson.Parse(body);
            Assert.True(parsed.Ok, parsed.Error);
            var d = WorldSnapshot.Decode(parsed.Value, SessionProtocol.WorldSchemaVersion);
            Assert.True(d.Ok, d.Error.ToString());
            return d.World;
        }

        [Fact]
        public void CaptureEncodeDecodeRestore_MatchesPlantAndCrew()
        {
            var host = RunningSim();
            var crew = new List<CrewMember> { Crew("p1", "Alice"), Crew("p2", "Bob", "hall.a", CrewPose.Seated) };
            var world = WorldSnapshot.Capture(host, 7, crew);
            string body;
            var decoded = RoundTrip(world, out body);

            var client = new Simulation();
            decoded.Restore(client);

            // Baseline: the existing solo path (ToJson, FromJson, RestoreSnapshot). Plant.RestoreState recomputes a few
            // derived fields such as periodSeconds, so the world path must match this path, not the raw host JSON.
            var baseline = new Simulation();
            baseline.RestoreSnapshot(PlantSnapshot.FromJson(host.CaptureSnapshot().ToJson()));
            Assert.Equal(baseline.CaptureSnapshot().ToJson(), client.CaptureSnapshot().ToJson());
            Assert.Equal(7, decoded.SnapshotSeq);
            Assert.Equal(2, decoded.Crew.Count);
            for (int i = 0; i < crew.Count; i++)
            {
                Assert.Equal(crew[i].PeerId, decoded.Crew[i].PeerId);
                Assert.Equal(crew[i].DisplayName, decoded.Crew[i].DisplayName);
                Assert.Equal(crew[i].LocationId, decoded.Crew[i].LocationId);
                Assert.Equal(crew[i].X, decoded.Crew[i].X);
                Assert.Equal(crew[i].Y, decoded.Crew[i].Y);
                Assert.Equal(crew[i].Z, decoded.Crew[i].Z);
                Assert.Equal(crew[i].YawDeg, decoded.Crew[i].YawDeg);
                Assert.Equal(crew[i].Pose, decoded.Crew[i].Pose);
            }
        }

        [Fact]
        public void BodyUsesPoseNames()
        {
            var w = WorldSnapshot.Capture(new Simulation(), 1, new List<CrewMember> { Crew("p1"), Crew("p2", pose: CrewPose.Seated) });
            string body = w.Encode();
            Assert.Contains("\"pose\":\"standing\"", body);
            Assert.Contains("\"pose\":\"seated\"", body);
        }

        [Fact]
        public void ClockAndSessionTick_Restore()
        {
            var host = RunningSim();
            host.EnqueueIntent(PlantIntent.SetTimeScale(25.0));
            host.EnqueueIntent(PlantIntent.PauseClock());
            host.Tick();
            Assert.True(host.Clock.Paused);
            Assert.Equal(25.0, host.Clock.Scale);
            long hostTick = host.SessionTick;
            Assert.True(hostTick > 0);

            string body;
            var decoded = RoundTrip(WorldSnapshot.Capture(host, 3, new List<CrewMember>()), out body);
            var client = new Simulation();
            Assert.False(client.Clock.Paused);
            decoded.Restore(client);

            Assert.True(client.Clock.Paused);
            Assert.Equal(25.0, client.Clock.Scale);
            Assert.Equal(hostTick, client.SessionTick);
        }

        [Fact]
        public void RestoreUnpausesWhenHostRunning()
        {
            var client = new Simulation();
            client.EnqueueIntent(PlantIntent.PauseClock());
            client.Tick();
            Assert.True(client.Clock.Paused);
            string body;
            RoundTrip(WorldSnapshot.Capture(new Simulation(), 1, new List<CrewMember>()), out body).Restore(client);
            Assert.False(client.Clock.Paused);
        }

        [Fact]
        public void UnknownFieldsAreIgnored()
        {
            string body = WorldSnapshot.Capture(new Simulation(), 2, new List<CrewMember> { Crew("p1") }).Encode();
            string withExtra = "{\"futureField\":{\"a\":[1,2]}," + body.Substring(1);
            var parsed = SessionJson.Parse(withExtra);
            Assert.True(parsed.Ok, parsed.Error);
            var d = WorldSnapshot.Decode(parsed.Value, 1);
            Assert.True(d.Ok);
            Assert.Equal(SessionErrorCode.None, d.Error);
            Assert.Single(d.World.Crew);
        }

        [Fact]
        public void SchemaAboveClient_IsUnsupportedSchema()
        {
            string body = WorldSnapshot.Capture(new Simulation(), 2, new List<CrewMember>()).Encode();
            var parsed = SessionJson.Parse(body.Replace("\"schema\":1", "\"schema\":2"));
            var d = WorldSnapshot.Decode(parsed.Value, 1);
            Assert.False(d.Ok);
            Assert.Equal(SessionErrorCode.UnsupportedSchema, d.Error);
            Assert.Null(d.World);
        }

        [Fact]
        public void OlderSchemaThanClient_IsAccepted()
        {
            string body = WorldSnapshot.Capture(new Simulation(), 2, new List<CrewMember>()).Encode();
            var d = WorldSnapshot.Decode(SessionJson.Parse(body).Value, 5);
            Assert.True(d.Ok);
        }

        [Fact]
        public void MissingPlant_IsProtocolViolation()
        {
            var parsed = SessionJson.Parse("{\"schema\":1,\"sessionTick\":0,\"snapshotSeq\":1,\"clockPaused\":false,\"clockScale\":10,\"crew\":[]}");
            var d = WorldSnapshot.Decode(parsed.Value, 1);
            Assert.False(d.Ok);
            Assert.Equal(SessionErrorCode.ProtocolViolation, d.Error);
        }

        [Theory]
        [InlineData("\"schema\":1", "\"schema\":\"1\"")]
        [InlineData("\"schema\":1", "\"schema\":1.5")]
        [InlineData("\"snapshotSeq\":4", "\"snapshotSeq\":-4")]
        [InlineData("\"clockPaused\":false", "\"clockPaused\":0")]
        [InlineData("\"crew\":[]", "\"crew\":{}")]
        public void IllTypedFields_AreProtocolViolation(string find, string replace)
        {
            string body = WorldSnapshot.Capture(new Simulation(), 4, new List<CrewMember>()).Encode();
            Assert.Contains(find, body);
            var parsed = SessionJson.Parse(body.Replace(find, replace));
            Assert.True(parsed.Ok, parsed.Error);
            var d = WorldSnapshot.Decode(parsed.Value, 1);
            Assert.False(d.Ok);
            Assert.Equal(SessionErrorCode.ProtocolViolation, d.Error);
        }

        [Fact]
        public void NonObjectBodyOrNull_IsProtocolViolation()
        {
            Assert.Equal(SessionErrorCode.ProtocolViolation, WorldSnapshot.Decode(null, 1).Error);
            Assert.Equal(SessionErrorCode.ProtocolViolation, WorldSnapshot.Decode(JsonValue.NewNumber(1), 1).Error);
        }

        [Fact]
        public void CrewEntryWithBadPoseInBody_IsProtocolViolation()
        {
            string body = WorldSnapshot.Capture(new Simulation(), 2, new List<CrewMember> { Crew("p1") }).Encode();
            var d = WorldSnapshot.Decode(SessionJson.Parse(body.Replace("\"standing\"", "\"floating\"")).Value, 1);
            Assert.False(d.Ok);
            Assert.Equal(SessionErrorCode.ProtocolViolation, d.Error);
        }

        [Fact]
        public void PlantRestoreIsTolerantOfMissingFields()
        {
            string body = "{\"schema\":1,\"sessionTick\":5,\"snapshotSeq\":1,\"clockPaused\":false,\"clockScale\":10,\"plant\":{\"simSeconds\":12},\"crew\":[]}";
            var d = WorldSnapshot.Decode(SessionJson.Parse(body).Value, 1);
            Assert.True(d.Ok);
            var sim = new Simulation();
            d.World.Restore(sim);
            Assert.Equal(12.0, sim.SimSeconds);
        }

        [Fact]
        public void ValidCrewMember_Passes()
        {
            Assert.Equal(SessionErrorCode.None, WorldSnapshot.ValidateCrewMember(Crew("p1"), null));
        }

        [Fact]
        public void UnknownPose_IsInvalid()
        {
            Assert.Equal(SessionErrorCode.InvalidCrewState, WorldSnapshot.ValidateCrewMember(Crew("p1", pose: (CrewPose)99), null));
        }

        [Theory]
        [InlineData(double.NaN, 0.0, 0.0, 0.0)]
        [InlineData(0.0, double.PositiveInfinity, 0.0, 0.0)]
        [InlineData(0.0, 0.0, double.NegativeInfinity, 0.0)]
        [InlineData(0.0, 0.0, 0.0, double.NaN)]
        public void NonFiniteNumbers_AreInvalid(double x, double y, double z, double yaw)
        {
            var m = Crew("p1");
            m.X = x; m.Y = y; m.Z = z; m.YawDeg = yaw;
            Assert.Equal(SessionErrorCode.InvalidCrewState, WorldSnapshot.ValidateCrewMember(m, null));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("abcdefghijklmnopqrstuvwxy")]
        [InlineData("bad\nname")]
        [InlineData("tab\there")]
        [InlineData("nul\0x")]
        public void BadDisplayNames_AreInvalid(string name)
        {
            Assert.Equal(SessionErrorCode.InvalidCrewState, WorldSnapshot.ValidateCrewMember(Crew("p1", name), null));
        }

        [Fact]
        public void DisplayNameBoundaries()
        {
            Assert.Equal(SessionErrorCode.None, WorldSnapshot.ValidateCrewMember(Crew("p1", new string('n', 24)), null));
            Assert.Equal(SessionErrorCode.None, WorldSnapshot.ValidateCrewMember(Crew("p1", "  " + new string('n', 24) + "  "), null));
            Assert.Equal(SessionErrorCode.None, WorldSnapshot.ValidateCrewMember(Crew("p1", "x"), null));
            Assert.Equal(SessionErrorCode.InvalidCrewState, WorldSnapshot.ValidateCrewMember(Crew("p1", new string('n', 25)), null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("Room")]
        [InlineData("room x")]
        [InlineData("room/x")]
        [InlineData("café")]
        public void DefaultLocationValidator_RejectsBadIds(string loc)
        {
            Assert.Equal(SessionErrorCode.InvalidCrewState, WorldSnapshot.ValidateCrewMember(Crew("p1", loc: loc), null));
        }

        [Fact]
        public void DefaultLocationValidator_Boundaries()
        {
            Assert.Equal(SessionErrorCode.None, WorldSnapshot.ValidateCrewMember(Crew("p1", loc: new string('a', 64)), null));
            Assert.Equal(SessionErrorCode.InvalidCrewState, WorldSnapshot.ValidateCrewMember(Crew("p1", loc: new string('a', 65)), null));
            Assert.Equal(SessionErrorCode.None, WorldSnapshot.ValidateCrewMember(Crew("p1", loc: "a.b_c-9"), null));
            Assert.Equal(SessionErrorCode.InvalidCrewState, WorldSnapshot.ValidateCrewMember(Crew("p1", loc: null), null));
        }

        [Fact]
        public void InjectedValidator_RejectingRoomX_Refuses()
        {
            Func<string, bool> v = id => id != "room.x";
            Assert.Equal(SessionErrorCode.InvalidCrewState, WorldSnapshot.ValidateCrewMember(Crew("p1", loc: "room.x"), v));
            Assert.Equal(SessionErrorCode.None, WorldSnapshot.ValidateCrewMember(Crew("p1", loc: "room.y"), v));
        }

        [Fact]
        public void ThrowingValidator_CountsAsInvalid()
        {
            Func<string, bool> v = id => { throw new InvalidOperationException("boom"); };
            Assert.Equal(SessionErrorCode.InvalidCrewState, WorldSnapshot.ValidateCrewMember(Crew("p1"), v));
        }

        [Fact]
        public void NullMember_IsInvalid()
        {
            Assert.Equal(SessionErrorCode.InvalidCrewState, WorldSnapshot.ValidateCrewMember(null, null));
        }

        [Fact]
        public void WorstCaseSnapshot_FitsBudget()
        {
            var sim = RunningSim();
            var snap = sim.CaptureSnapshot();
            // Every double (and every array element) gets a 24-character G17 value: the longest realistic rendering.
            const double longValue = -1.2345678901234567E-300;
            foreach (var f in typeof(PlantSnapshot).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType == typeof(double)) f.SetValue(snap, longValue);
                else if (f.FieldType == typeof(double[]))
                {
                    var arr = (double[])f.GetValue(snap);
                    for (int i = 0; i < arr.Length; i++) arr[i] = longValue;
                }
                else if (f.FieldType == typeof(string)) f.SetValue(snap, "Shutdown");
            }
            var world = new WorldSnapshot
            {
                Schema = SessionProtocol.WorldSchemaVersion,
                SessionTick = SessionProtocol.MaxWireInteger,
                SnapshotSeq = SessionProtocol.MaxWireInteger,
                ClockPaused = true,
                ClockScale = longValue,
                Plant = snap,
                Crew = new List<CrewMember>
                {
                    Crew("p1", new string('W', 24), new string('a', 64)),
                    Crew("p2", new string('é', 24), new string('b', 64), CrewPose.Seated)
                }
            };
            world.Crew[0].X = longValue; world.Crew[0].Y = longValue; world.Crew[0].Z = longValue; world.Crew[0].YawDeg = longValue;
            world.Crew[1].X = longValue; world.Crew[1].Y = longValue; world.Crew[1].Z = longValue; world.Crew[1].YawDeg = longValue;

            string body = world.Encode();
            int bytes = Encoding.UTF8.GetByteCount(body);
            Assert.True(bytes < SessionProtocol.WorldSnapshotBudgetBytes, "encoded " + bytes + " bytes");
            Assert.True(bytes > 1000);
            Assert.True(SessionJson.Parse(body).Ok);
            System.Console.WriteLine("WORLD_SNAPSHOT_WORST_CASE_BYTES=" + bytes);
        }
    }
}
