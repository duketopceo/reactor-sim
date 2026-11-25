// ReactorSim.Core - versioned world snapshot: plant state, session tick, clock state and crew entries (Phase 3, U2, KTD11/KTD14).
// Decode never throws on bad input: it returns a result carrying a world or a SessionErrorCode.

using System;
using System.Collections.Generic;
using System.Text;

namespace ReactorSim.Session
{
    // Serialized as the names "standing" and "seated".
    public enum CrewPose
    {
        Standing = 0,
        Seated = 1
    }

    public sealed class CrewMember
    {
        public string PeerId = "";
        public string DisplayName = "";
        public string LocationId = "";
        public double X;
        public double Y;
        public double Z;
        public double YawDeg;
        public CrewPose Pose = CrewPose.Standing;

        public CrewMember Clone()
        {
            return (CrewMember)MemberwiseClone();
        }
    }

    public sealed class WorldSnapshotDecodeResult
    {
        public bool Ok;
        public WorldSnapshot World;
        public SessionErrorCode Error;
    }

    public sealed class WorldSnapshot
    {
        public int Schema = SessionProtocol.WorldSchemaVersion;
        public long SessionTick;
        // Host-assigned freshness counter; distinct from the envelope sequence number (KTD9).
        public long SnapshotSeq;
        public bool ClockPaused;
        // Requested scale as stored by SimClock.Scale, not the effective (auto-slowed) scale.
        public double ClockScale;
        public PlantSnapshot Plant;
        public List<CrewMember> Crew = new List<CrewMember>();

        public static WorldSnapshot Capture(Simulation sim, long snapshotSeq, IList<CrewMember> crew)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            var w = new WorldSnapshot
            {
                Schema = SessionProtocol.WorldSchemaVersion,
                SessionTick = sim.SessionTick,
                SnapshotSeq = snapshotSeq,
                ClockPaused = sim.Clock.Paused,
                ClockScale = sim.Clock.Scale,
                Plant = sim.CaptureSnapshot()
            };
            if (crew != null)
            {
                for (int i = 0; i < crew.Count; i++)
                {
                    if (crew[i] != null) w.Crew.Add(crew[i].Clone());
                }
            }
            return w;
        }

        // Encodes the envelope body object. The plant is embedded as the raw PlantSnapshot.ToJson object, so a large plant
        // is written once and never reparsed on the host. Throws ArgumentException on a non-finite crew number or a
        // missing plant: the host validates crew entries before they reach a snapshot.
        public string Encode()
        {
            if (Plant == null) throw new InvalidOperationException("world snapshot has no plant");
            var sb = new StringBuilder(4096);
            sb.Append("{\"schema\":");
            SessionJson.AppendInteger(sb, Schema);
            sb.Append(",\"sessionTick\":");
            SessionJson.AppendInteger(sb, SessionTick);
            sb.Append(",\"snapshotSeq\":");
            SessionJson.AppendInteger(sb, SnapshotSeq);
            sb.Append(",\"clockPaused\":").Append(ClockPaused ? "true" : "false");
            sb.Append(",\"clockScale\":");
            SessionJson.AppendNumber(sb, ClockScale);
            sb.Append(",\"plant\":").Append(Plant.ToJson());
            sb.Append(",\"crew\":[");
            for (int i = 0; i < Crew.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendCrew(sb, Crew[i]);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static void AppendCrew(StringBuilder sb, CrewMember m)
        {
            sb.Append("{\"peerId\":");
            SessionJson.AppendString(sb, m.PeerId);
            sb.Append(",\"name\":");
            SessionJson.AppendString(sb, m.DisplayName);
            sb.Append(",\"loc\":");
            SessionJson.AppendString(sb, m.LocationId);
            sb.Append(",\"x\":");
            SessionJson.AppendNumber(sb, m.X);
            sb.Append(",\"y\":");
            SessionJson.AppendNumber(sb, m.Y);
            sb.Append(",\"z\":");
            SessionJson.AppendNumber(sb, m.Z);
            sb.Append(",\"yaw\":");
            SessionJson.AppendNumber(sb, m.YawDeg);
            sb.Append(",\"pose\":");
            SessionJson.AppendString(sb, m.Pose == CrewPose.Seated ? "seated" : "standing");
            sb.Append('}');
        }

        // Strict on the envelope fields, tolerant on the plant: the plant object is re-serialized with the SessionJson writer
        // (numbers round-trip at G17) and restored through the existing PlantSnapshot.FromJson, whose defaults cover missing
        // fields (KTD14). Unknown fields are ignored. Crew entries are checked with the default location validator.
        public static WorldSnapshotDecodeResult Decode(JsonValue body, int clientSchema)
        {
            if (body == null || body.Kind != JsonKind.Object)
                return Fail(SessionErrorCode.ProtocolViolation);

            long schema;
            if (!TryInteger(body.Get("schema"), out schema) || schema < 1 || schema > int.MaxValue)
                return Fail(SessionErrorCode.ProtocolViolation);
            if (schema > clientSchema)
                return Fail(SessionErrorCode.UnsupportedSchema);

            long tick, seq;
            if (!TryInteger(body.Get("sessionTick"), out tick) || !TryInteger(body.Get("snapshotSeq"), out seq))
                return Fail(SessionErrorCode.ProtocolViolation);

            JsonValue paused = body.Get("clockPaused");
            JsonValue scale = body.Get("clockScale");
            if (paused == null || paused.Kind != JsonKind.Bool || scale == null || scale.Kind != JsonKind.Number)
                return Fail(SessionErrorCode.ProtocolViolation);

            JsonValue plantValue = body.Get("plant");
            if (plantValue == null || plantValue.Kind != JsonKind.Object)
                return Fail(SessionErrorCode.ProtocolViolation);
            PlantSnapshot plant = PlantSnapshot.FromJson(SessionJson.Write(plantValue));
            if (plant == null)
                return Fail(SessionErrorCode.ProtocolViolation);

            JsonValue crewValue = body.Get("crew");
            if (crewValue == null || crewValue.Kind != JsonKind.Array)
                return Fail(SessionErrorCode.ProtocolViolation);
            var crew = new List<CrewMember>(crewValue.Items.Count);
            for (int i = 0; i < crewValue.Items.Count; i++)
            {
                CrewMember m = DecodeCrew(crewValue.Items[i]);
                if (m == null || ValidateCrewMember(m, null) != SessionErrorCode.None)
                    return Fail(SessionErrorCode.ProtocolViolation);
                crew.Add(m);
            }

            return new WorldSnapshotDecodeResult
            {
                Ok = true,
                Error = SessionErrorCode.None,
                World = new WorldSnapshot
                {
                    Schema = (int)schema,
                    SessionTick = tick,
                    SnapshotSeq = seq,
                    ClockPaused = paused.Bool,
                    ClockScale = scale.Number,
                    Plant = plant,
                    Crew = crew
                }
            };
        }

        // Applies the plant through Simulation.RestoreSnapshot, then clock pause and scale and the session tick.
        public void Restore(Simulation sim)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            if (Plant == null) throw new InvalidOperationException("world snapshot has no plant");
            sim.RestoreSnapshot(Plant);
            sim.Clock.ApplyRemoteState(ClockPaused, ClockScale);
            sim.SetSessionTickFromHost(SessionTick);
        }

        // Returns None or InvalidCrewState. A null validator selects the default location rule; a validator that throws
        // counts as invalid.
        public static SessionErrorCode ValidateCrewMember(CrewMember m, Func<string, bool> locationValidator)
        {
            if (m == null)
                return SessionErrorCode.InvalidCrewState;
            if (!IsFinite(m.X) || !IsFinite(m.Y) || !IsFinite(m.Z) || !IsFinite(m.YawDeg))
                return SessionErrorCode.InvalidCrewState;
            if (m.Pose != CrewPose.Standing && m.Pose != CrewPose.Seated)
                return SessionErrorCode.InvalidCrewState;
            if (!IsValidDisplayName(m.DisplayName))
                return SessionErrorCode.InvalidCrewState;
            if (m.LocationId == null)
                return SessionErrorCode.InvalidCrewState;
            try
            {
                bool ok = locationValidator != null ? locationValidator(m.LocationId) : IsDefaultLocationId(m.LocationId);
                return ok ? SessionErrorCode.None : SessionErrorCode.InvalidCrewState;
            }
            catch (Exception)
            {
                return SessionErrorCode.InvalidCrewState;
            }
        }

        public static bool IsDefaultLocationId(string id)
        {
            if (id == null || id.Length < 1 || id.Length > SessionProtocol.LocationIdMaxChars)
                return false;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        // 1..DisplayNameMaxChars printable characters after trimming surrounding whitespace.
        private static bool IsValidDisplayName(string name)
        {
            if (name == null)
                return false;
            string t = name.Trim();
            if (t.Length < 1 || t.Length > SessionProtocol.DisplayNameMaxChars)
                return false;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (char.IsControl(c))
                    return false;
                if (char.IsHighSurrogate(c) && i + 1 < t.Length && char.IsLowSurrogate(t[i + 1]))
                {
                    i++;
                    continue;
                }
                if (char.IsSurrogate(c))
                    return false;
            }
            return true;
        }

        private static CrewMember DecodeCrew(JsonValue v)
        {
            if (v == null || v.Kind != JsonKind.Object)
                return null;
            JsonValue peer = v.Get("peerId"), name = v.Get("name"), loc = v.Get("loc"), pose = v.Get("pose");
            JsonValue x = v.Get("x"), y = v.Get("y"), z = v.Get("z"), yaw = v.Get("yaw");
            if (!IsKind(peer, JsonKind.String) || !IsKind(name, JsonKind.String) || !IsKind(loc, JsonKind.String) || !IsKind(pose, JsonKind.String))
                return null;
            if (!IsKind(x, JsonKind.Number) || !IsKind(y, JsonKind.Number) || !IsKind(z, JsonKind.Number) || !IsKind(yaw, JsonKind.Number))
                return null;
            CrewPose p;
            if (pose.Text == "standing") p = CrewPose.Standing;
            else if (pose.Text == "seated") p = CrewPose.Seated;
            else return null;
            return new CrewMember
            {
                PeerId = peer.Text,
                DisplayName = name.Text,
                LocationId = loc.Text,
                X = x.Number,
                Y = y.Number,
                Z = z.Number,
                YawDeg = yaw.Number,
                Pose = p
            };
        }

        private static bool IsKind(JsonValue v, JsonKind kind)
        {
            return v != null && v.Kind == kind;
        }

        // A JSON number that is a whole value in 0..MaxWireInteger.
        private static bool TryInteger(JsonValue v, out long result)
        {
            result = 0;
            if (v == null || v.Kind != JsonKind.Number)
                return false;
            double d = v.Number;
            if (d < 0.0 || d > SessionProtocol.MaxWireInteger || d != Math.Floor(d))
                return false;
            result = (long)d;
            return true;
        }

        private static bool IsFinite(double d)
        {
            return !double.IsNaN(d) && !double.IsInfinity(d);
        }

        private static WorldSnapshotDecodeResult Fail(SessionErrorCode code)
        {
            return new WorldSnapshotDecodeResult { Ok = false, World = null, Error = code };
        }
    }
}
