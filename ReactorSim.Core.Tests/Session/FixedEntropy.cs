using System;
using System.Collections.Generic;
using System.Text;
using ReactorSim.Session;
using Xunit;

namespace ReactorSim.Tests
{
    // Deterministic entropy for hermetic tests: a fixed linear sequence, never random.
    public sealed class FixedEntropy : ISessionEntropy
    {
        private uint _state;

        public FixedEntropy(uint seed = 12345)
        {
            _state = seed;
        }

        public void Fill(byte[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                _state = unchecked(_state * 1664525u + 1013904223u);
                buffer[i] = (byte)(_state >> 24);
            }
        }
    }

    internal static class SessionTestSupport
    {
        internal static string BaselineJson(Simulation host)
        {
            var baseline = new Simulation();
            baseline.RestoreSnapshot(PlantSnapshot.FromJson(host.CaptureSnapshot().ToJson()));
            return baseline.CaptureSnapshot().ToJson();
        }
    }

    public sealed class OutFrame
    {
        public int ConnectionId;
        public string Frame;
        public bool CloseAfter;
        public SessionEnvelope Env;

        public string Type { get { return Env == null ? null : Env.T; } }

        public int Code
        {
            get
            {
                JsonValue c = Env.Body.Get("code");
                return (int)c.Number;
            }
        }
    }

    public sealed class JoinInfo
    {
        public string PeerId;
        public string Token;
        public int ConnectionId;
    }

    // Drives a SessionHost with UTF-8 bytes and a manual clock, and decodes what it sends.
    public sealed class SessionHostHarness
    {
        public readonly Simulation Sim = new Simulation();
        public readonly SessionHost Host;
        public double Now;

        public SessionHostHarness(Func<string, bool> locationValidator = null, uint seed = 12345)
        {
            Host = new SessionHost(Sim, new FixedEntropy(seed), locationValidator);
        }

        public void Open(int conn)
        {
            Host.OnConnected(conn, Now);
        }

        public void Feed(int conn, string line)
        {
            FeedRaw(conn, Encoding.UTF8.GetBytes(line + "\n"));
        }

        public void FeedRaw(int conn, byte[] bytes)
        {
            Host.OnBytes(conn, bytes, 0, bytes.Length, Now);
        }

        public void Tick(double now)
        {
            Now = now;
            Host.Tick(now);
        }

        public List<OutFrame> Drain()
        {
            var result = new List<OutFrame>();
            foreach (var o in Host.DrainOutbox())
            {
                result.Add(new OutFrame
                {
                    ConnectionId = o.ConnectionId,
                    Frame = o.Frame,
                    CloseAfter = o.CloseAfter,
                    Env = o.Frame == null ? null : SessionEnvelope.Decode(o.Frame).Envelope
                });
            }
            return result;
        }

        public string Hello(long seq, string code = null, string name = "Ann", int v = 1, long schema = 1, string token = null, string sid = null)
        {
            var sb = new StringBuilder();
            sb.Append("{\"v\":").Append(v).Append(",\"t\":\"hello\",\"seq\":").Append(seq);
            if (sid != null) { sb.Append(",\"sid\":"); SessionJson.AppendString(sb, sid); }
            sb.Append(",\"body\":{\"schema\":").Append(schema);
            sb.Append(",\"joinCode\":"); SessionJson.AppendString(sb, code ?? Host.JoinCode);
            sb.Append(",\"name\":"); SessionJson.AppendString(sb, name);
            if (token != null) { sb.Append(",\"token\":"); SessionJson.AppendString(sb, token); }
            sb.Append("}}");
            return sb.ToString();
        }

        public string Frame(string type, long seq, string bodyJson = "{}")
        {
            return SessionEnvelope.Encode(type, seq, Host.SessionId, bodyJson);
        }

        public string IntentFrame(long seq, string kind, string value = "0.5", string issuerClaim = "host")
        {
            var sb = new StringBuilder();
            sb.Append("{\"kind\":"); SessionJson.AppendString(sb, kind);
            sb.Append(",\"target\":\"\",\"value\":").Append(value);
            sb.Append(",\"issuer\":"); SessionJson.AppendString(sb, issuerClaim);
            sb.Append('}');
            return Frame("intent", seq, sb.ToString());
        }

        public string CrewFrame(long seq, string loc, string pose = "standing", string x = "1.5", string name = null)
        {
            var sb = new StringBuilder();
            sb.Append("{\"loc\":"); SessionJson.AppendString(sb, loc);
            sb.Append(",\"x\":").Append(x).Append(",\"y\":0,\"z\":2,\"yaw\":90,\"pose\":");
            SessionJson.AppendString(sb, pose);
            if (name != null) { sb.Append(",\"name\":"); SessionJson.AppendString(sb, name); }
            sb.Append('}');
            return Frame("crew_update", seq, sb.ToString());
        }

        // Opens the connection, sends a valid Hello (seq 1), drains, and returns the identity from the Welcome.
        public JoinInfo Join(int conn, string name = "Ann")
        {
            Open(conn);
            Feed(conn, Hello(1, null, name));
            List<OutFrame> frames = Drain();
            Assert.Single(frames);
            Assert.Equal("welcome", frames[0].Type);
            Assert.False(frames[0].CloseAfter);
            return new JoinInfo
            {
                ConnectionId = conn,
                PeerId = frames[0].Env.Body.Get("peerId").Text,
                Token = frames[0].Env.Body.Get("token").Text
            };
        }

        // Moves the clock and ticks, then drops what the tick queued so tests see only later frames.
        public void TickAndDrain(double now)
        {
            Tick(now);
            Drain();
        }
    }
}
