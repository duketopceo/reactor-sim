using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ReactorSim.Session;
using Xunit;

namespace ReactorSim.Tests
{
    // Hand-made host frames, so the client is tested against the wire contract in SessionHost.cs, not against the host.
    public sealed class ScriptedHost
    {
        public const string Sid = "0123456789abcdef0123456789abcdef";
        public static readonly string Token = new string('a', 32);

        public readonly Simulation Sim = new Simulation();
        public long Seq;

        public ScriptedHost()
        {
            for (int i = 0; i < 40; i++) Sim.Tick();
        }

        public static List<CrewMember> Crew(string peer = "p1", string name = "Ann")
        {
            return new List<CrewMember> { new CrewMember { PeerId = peer, DisplayName = name, LocationId = "unplaced" } };
        }

        public string WorldBody(long snapshotSeq)
        {
            return WorldSnapshot.Capture(Sim, snapshotSeq, Crew()).Encode();
        }

        public string Welcome(long snapshotSeq, string peer = "p1", string token = null, string sid = Sid, long? seq = null, string worldBody = null)
        {
            string body = "{\"hostId\":\"0011223344556677\",\"sid\":\"" + sid + "\",\"peerId\":\"" + peer + "\",\"token\":\"" + (token ?? Token)
                + "\",\"graceSeconds\":30,\"world\":" + (worldBody ?? WorldBody(snapshotSeq)) + "}";
            return SessionEnvelope.Encode("welcome", seq ?? ++Seq, sid, body);
        }

        public string World(long snapshotSeq, long? seq = null, string sid = Sid)
        {
            return SessionEnvelope.Encode("world", seq ?? ++Seq, sid, WorldBody(snapshotSeq));
        }

        public string Reject(SessionErrorCode code, long refSeq = 0, string sid = Sid)
        {
            string body = "{\"code\":" + (int)code + ",\"name\":\"" + code + "\",\"seq\":" + refSeq + "}";
            return SessionEnvelope.Encode("reject", ++Seq, sid, body);
        }

        public string IntentResult(long seq, SessionErrorCode code)
        {
            string body = "{\"seq\":" + seq + ",\"code\":" + (int)code + ",\"accepted\":" + (code == SessionErrorCode.None ? "true" : "false") + "}";
            return SessionEnvelope.Encode("intent_result", ++Seq, Sid, body);
        }
    }

    public class SessionClientTests
    {
        private static void Feed(SessionClient c, string line, double now)
        {
            byte[] b = Encoding.UTF8.GetBytes(line + "\n");
            c.OnBytes(b, 0, b.Length, now);
        }

        private static SessionEnvelope Sent(ClientOutput o)
        {
            Assert.Equal(ClientOutputKind.Send, o.Kind);
            SessionEnvelopeResult r = SessionEnvelope.Decode(o.Frame);
            Assert.True(r.Ok);
            return r.Envelope;
        }

        private static List<SessionEnvelope> SentFrames(SessionClient c)
        {
            return c.DrainOutbox().Where(o => o.Kind == ClientOutputKind.Send).Select(Sent).ToList();
        }

        // Idle -> Joining (Connect output) -> hello sent on OnConnected. Returns the hello.
        private static SessionEnvelope StartJoin(SessionClient c, double now = 0)
        {
            Assert.True(c.Connect(now));
            List<ClientOutput> connect = c.DrainOutbox();
            Assert.Equal(ClientOutputKind.Connect, Assert.Single(connect).Kind);
            c.OnConnected(now);
            return Assert.Single(SentFrames(c));
        }

        private static SessionClient JoinedClient(ScriptedHost host, out Simulation sim, double now = 0, long snapshotSeq = 1)
        {
            sim = new Simulation();
            var c = new SessionClient(sim, "Ann", "123456");
            StartJoin(c, now);
            Feed(c, host.Welcome(snapshotSeq), now);
            c.DrainOutbox();
            c.DrainEvents();
            Assert.Equal(ClientState.Active, c.State);
            return c;
        }

        [Fact]
        public void ConnectStartsJoiningAndHelloFollowsOnConnected()
        {
            var c = new SessionClient(new Simulation(), "Ann", "123456");
            Assert.Equal(ClientState.Idle, c.State);
            Assert.True(c.Connect(0));
            Assert.Equal(ClientState.Joining, c.State);
            Assert.Equal(ClientOutputKind.Connect, Assert.Single(c.DrainOutbox()).Kind);
            c.OnConnected(0);
            SessionEnvelope hello = Assert.Single(SentFrames(c));
            Assert.Equal("hello", hello.T);
            Assert.Equal(1, hello.Seq);
            Assert.Null(hello.Sid);
            Assert.Equal(SessionProtocol.WorldSchemaVersion, (int)hello.Body.Get("schema").Number);
            Assert.Equal("123456", hello.Body.Get("joinCode").Text);
            Assert.Equal("Ann", hello.Body.Get("name").Text);
            Assert.Null(hello.Body.Get("token"));
        }

        [Fact]
        public void ConnectIsIgnoredUnlessIdle()
        {
            var c = new SessionClient(new Simulation(), "Ann", "123456");
            Assert.True(c.Connect(0));
            c.DrainOutbox();
            Assert.False(c.Connect(0.1));
            Assert.Empty(c.DrainOutbox());
        }

        [Fact]
        public void WelcomeGivesActiveAndRestoresThePlantFromItsWorld()
        {
            var host = new ScriptedHost();
            var sim = new Simulation();
            var c = new SessionClient(sim, "Ann", "123456");
            StartJoin(c);
            Feed(c, host.Welcome(5), 0.1);

            Assert.Equal(ClientState.Active, c.State);
            Assert.Equal("p1", c.PeerId);
            Assert.Equal(ScriptedHost.Sid, c.SessionId);
            Assert.Equal(ScriptedHost.Token, c.Token);
            Assert.Equal(30, c.GraceSeconds);
            Assert.Equal(5, c.LastSnapshotSeq);
            Assert.Single(c.Crew);
            Assert.Equal(SessionTestSupport.BaselineJson(host.Sim), sim.CaptureSnapshot().ToJson());
            List<ClientEvent> events = c.DrainEvents();
            Assert.Contains(events, e => e.Kind == ClientEventKind.Joined && e.PeerId == "p1");
            Assert.Contains(events, e => e.Kind == ClientEventKind.WorldApplied && e.SnapshotSeq == 5);
        }

        [Fact]
        public void WorldBeforeWelcomeIsIgnored()
        {
            var host = new ScriptedHost();
            var sim = new Simulation();
            string before = sim.CaptureSnapshot().ToJson();
            var c = new SessionClient(sim, "Ann", "123456");
            StartJoin(c);
            Feed(c, host.World(3), 0.1);
            Assert.Equal(ClientState.Joining, c.State);
            Assert.Equal(before, sim.CaptureSnapshot().ToJson());
            Assert.Empty(c.DrainEvents());
            Feed(c, host.Welcome(4), 0.2);
            Assert.Equal(ClientState.Active, c.State);
            Assert.Equal(4, c.LastSnapshotSeq);
        }

        [Fact]
        public void LateOlderWorldIsIgnored_AE5()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, snapshotSeq: 1);
            Feed(c, host.World(12), 0.5);
            Assert.Equal(12, c.LastSnapshotSeq);

            for (int i = 0; i < 20; i++) host.Sim.Tick();
            string at12 = sim.CaptureSnapshot().ToJson();
            Feed(c, host.World(11), 0.6);

            Assert.Equal(12, c.LastSnapshotSeq);
            Assert.Equal(at12, sim.CaptureSnapshot().ToJson());
            Assert.DoesNotContain(c.DrainEvents(), e => e.Kind == ClientEventKind.WorldApplied && e.SnapshotSeq == 11);
        }

        [Fact]
        public void EqualSnapshotSeqIsNotAppliedTwice()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, snapshotSeq: 7);
            Feed(c, host.World(7), 0.5);
            Assert.DoesNotContain(c.DrainEvents(), e => e.Kind == ClientEventKind.WorldApplied);
        }

        [Fact]
        public void HostFrameWithNonIncreasingEnvelopeSeqIsIgnored()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, snapshotSeq: 1);
            Feed(c, host.World(10, seq: 50), 0.5);
            c.DrainEvents();
            Feed(c, host.World(11, seq: 50), 0.6);
            Feed(c, host.World(12, seq: 49), 0.7);
            Assert.Equal(10, c.LastSnapshotSeq);
            Assert.Empty(c.DrainEvents());
        }

        [Fact]
        public void HostFrameWithOtherSessionIdIsIgnoredAfterWelcome()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, snapshotSeq: 1);
            Feed(c, host.World(9, sid: "ffffffffffffffffffffffffffffffff"), 0.5);
            Assert.Equal(1, c.LastSnapshotSeq);
            Assert.Equal(ClientState.Active, c.State);
        }

        [Fact]
        public void RejectWhileJoiningGivesFailedWithTheCode()
        {
            var host = new ScriptedHost();
            var c = new SessionClient(new Simulation(), "Ann", "123456");
            StartJoin(c);
            Feed(c, host.Reject(SessionErrorCode.SessionFull, 1), 0.1);

            Assert.Equal(ClientState.Failed, c.State);
            Assert.Equal(SessionErrorCode.SessionFull, c.FailureCode);
            Assert.Equal(ClientFailure.Refused, c.FailureReason);
            ClientEvent failed = c.DrainEvents().Single(e => e.Kind == ClientEventKind.Failed);
            Assert.Equal(SessionErrorCode.SessionFull, failed.Code);
            Assert.Equal(ClientFailure.Refused, failed.Failure);
            Assert.Contains(c.DrainOutbox(), o => o.Kind == ClientOutputKind.Close);
        }

        [Fact]
        public void BadJoinCodeSurfacesAndStaysJoiningUntilRetriedWithNewCode()
        {
            var host = new ScriptedHost();
            var sim = new Simulation();
            var c = new SessionClient(sim, "Ann", "000000");
            StartJoin(c);
            Feed(c, host.Reject(SessionErrorCode.BadJoinCode, 1), 0.1);

            Assert.Equal(ClientState.Joining, c.State);
            Assert.Contains(c.DrainEvents(), e => e.Kind == ClientEventKind.Refused && e.Code == SessionErrorCode.BadJoinCode);
            Assert.Empty(c.DrainOutbox());

            c.RetryJoin("654321", 0.2);
            SessionEnvelope hello = Assert.Single(SentFrames(c));
            Assert.Equal("hello", hello.T);
            Assert.Equal(2, hello.Seq);
            Assert.Equal("654321", hello.Body.Get("joinCode").Text);

            Feed(c, host.Welcome(1), 0.3);
            Assert.Equal(ClientState.Active, c.State);
        }

        [Fact]
        public void ConnectionClosedAfterBadJoinCodeFailsWithThatCode()
        {
            var host = new ScriptedHost();
            var c = new SessionClient(new Simulation(), "Ann", "000000");
            StartJoin(c);
            Feed(c, host.Reject(SessionErrorCode.BadJoinCode, 1), 0.1);
            c.OnDisconnected(0.1);
            Assert.Equal(ClientState.Failed, c.State);
            Assert.Equal(SessionErrorCode.BadJoinCode, c.FailureCode);
            Assert.Equal(ClientFailure.Refused, c.FailureReason);
        }

        [Fact]
        public void TwoRetriesAfterOneBadJoinCodeSendOnlyOneHello()
        {
            var host = new ScriptedHost();
            var c = new SessionClient(new Simulation(), "Ann", "000000");
            StartJoin(c);
            Feed(c, host.Reject(SessionErrorCode.BadJoinCode, 1), 0.1);
            c.DrainEvents();

            c.RetryJoin("111111", 0.2);
            c.RetryJoin("222222", 0.3);
            SessionEnvelope hello = Assert.Single(SentFrames(c));
            Assert.Equal("111111", hello.Body.Get("joinCode").Text);
        }

        [Fact]
        public void RetryJoinWithoutAnOutstandingRefusalSendsNothing()
        {
            var c = new SessionClient(new Simulation(), "Ann", "000000");
            StartJoin(c);
            c.RetryJoin("111111", 0.2);
            Assert.Empty(c.DrainOutbox());
        }

        [Fact]
        public void RetryAfterTheFinalBadJoinCodeStillReportsBadJoinCodeOnClose()
        {
            var host = new ScriptedHost();
            var c = new SessionClient(new Simulation(), "Ann", "000000");
            StartJoin(c);
            Feed(c, host.Reject(SessionErrorCode.BadJoinCode, 1), 0.1);
            c.RetryJoin("111111", 0.2);
            c.OnDisconnected(0.3);
            Assert.Equal(ClientState.Failed, c.State);
            Assert.Equal(ClientFailure.Refused, c.FailureReason);
            Assert.Equal(SessionErrorCode.BadJoinCode, c.FailureCode);
        }

        [Fact]
        public void RejectOverTheControlLimitFailsWithFrameTooLarge()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            Feed(c, PaddedReject(host, SessionProtocol.ControlFrameMaxBytes + 1), 1);
            Assert.Equal(ClientState.Failed, c.State);
            Assert.Equal(SessionErrorCode.FrameTooLarge, c.FailureCode);
            Assert.Equal(ClientFailure.ProtocolError, c.FailureReason);
            Assert.Contains(c.DrainOutbox(), o => o.Kind == ClientOutputKind.Close);
        }

        [Fact]
        public void RejectExactlyAtTheControlLimitIsHandled()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            Feed(c, PaddedReject(host, SessionProtocol.ControlFrameMaxBytes), 1);
            Assert.Equal(ClientState.Active, c.State);
            Assert.Contains(c.DrainEvents(), e => e.Kind == ClientEventKind.Refused && e.Code == SessionErrorCode.InvalidCrewState);
        }

        private static string PaddedReject(ScriptedHost host, int totalBytes)
        {
            string reject = host.Reject(SessionErrorCode.InvalidCrewState, 7);
            int pad = totalBytes - Encoding.UTF8.GetByteCount(reject);
            Assert.True(pad > 9);
            string padded = reject.Substring(0, reject.Length - 1) + ",\"pad\":\"" + new string('x', pad - 9) + "\"}";
            Assert.Equal(totalBytes, Encoding.UTF8.GetByteCount(padded));
            return padded;
        }

        [Fact]
        public void CrewUpdateWithAnUndefinedPoseIsRefusedLocallyWithoutConsumingASequenceNumber()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            Assert.Equal(0, c.SendCrewUpdate(new CrewMember { LocationId = "panel.a", Pose = (CrewPose)7 }));
            Assert.Empty(c.DrainOutbox());
            Assert.Contains(c.DrainEvents(), e => e.Kind == ClientEventKind.IntentResult && e.Seq == 0 && e.Code == SessionErrorCode.InvalidCrewState);
            long seq = c.SendCrewUpdate(new CrewMember { LocationId = "panel.a" });
            Assert.Equal(2, seq);
        }

        [Fact]
        public void CrewUpdateOverTheControlLimitIsRefusedLocallyWithoutConsumingASequenceNumber()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            Assert.Equal(0, c.SendCrewUpdate(new CrewMember { LocationId = new string('l', SessionProtocol.ControlFrameMaxBytes) }));
            Assert.Empty(c.DrainOutbox());
            Assert.Contains(c.DrainEvents(), e => e.Kind == ClientEventKind.IntentResult && e.Seq == 0 && e.Code == SessionErrorCode.FrameTooLarge);
            Assert.Equal(2, c.SendCrewUpdate(new CrewMember { LocationId = "panel.a" }));
        }

        [Fact]
        public void LargestAllowedIntentStaysUnderTheControlLimit()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            string target = new string('\u0001', SessionProtocol.IntentStringMaxChars);
            long seq = c.SendIntent(new PlantIntent { Kind = IntentKind.AllRodsTarget, Target = target, Value = -1.2345678901234567e-300, AtMinute = 1e300 });
            Assert.Equal(2, seq);
            ClientOutput o = Assert.Single(c.DrainOutbox());
            Assert.True(Encoding.UTF8.GetByteCount(o.Frame) <= SessionProtocol.ControlFrameMaxBytes);
        }

        [Fact]
        public void ConnectionLostWhileJoiningFailsAsHostLost()
        {
            var c = new SessionClient(new Simulation(), "Ann", "123456");
            StartJoin(c);
            c.OnDisconnected(0.5);
            Assert.Equal(ClientState.Failed, c.State);
            Assert.Equal(ClientFailure.HostLost, c.FailureReason);
        }

        [Fact]
        public void NoWelcomeWithinHandshakeTimeoutFailsWithHandshakeTimeout()
        {
            var c = new SessionClient(new Simulation(), "Ann", "123456");
            StartJoin(c, 10);
            c.Tick(14.9);
            Assert.Equal(ClientState.Joining, c.State);
            c.Tick(15);
            Assert.Equal(ClientState.Failed, c.State);
            Assert.Equal(SessionErrorCode.HandshakeTimeout, c.FailureCode);
            Assert.Equal(ClientFailure.HandshakeTimeout, c.FailureReason);
            Assert.Contains(c.DrainOutbox(), o => o.Kind == ClientOutputKind.Close);
        }

        [Fact]
        public void HandshakeTimeoutAlsoCoversAConnectionThatNeverOpens()
        {
            var c = new SessionClient(new Simulation(), "Ann", "123456");
            c.Connect(0);
            c.Tick(5);
            Assert.Equal(ClientState.Failed, c.State);
            Assert.Equal(ClientFailure.HandshakeTimeout, c.FailureReason);
        }

        [Fact]
        public void HeartbeatGoesOutEachSecondWhileActive()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 0);
            c.Tick(0.5);
            Assert.Empty(c.DrainOutbox());
            c.Tick(1);
            SessionEnvelope hb = Assert.Single(SentFrames(c));
            Assert.Equal("heartbeat", hb.T);
            Assert.Equal(ScriptedHost.Sid, hb.Sid);
            Assert.Equal(2, hb.Seq);
            c.Tick(1.5);
            Assert.Empty(c.DrainOutbox());
            Feed(c, host.World(2), 2);
            c.Tick(2);
            Assert.Equal("heartbeat", Assert.Single(SentFrames(c)).T);
        }

        [Fact]
        public void FiveSecondsOfHostSilenceTurnsReconnectingAndClosesTheConnection()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 0);
            Feed(c, host.World(2), 1);
            c.Tick(5.9);
            Assert.Equal(ClientState.Active, c.State);
            c.DrainOutbox();
            c.Tick(6);
            Assert.Equal(ClientState.Reconnecting, c.State);
            Assert.Contains(c.DrainOutbox(), o => o.Kind == ClientOutputKind.Close);
            Assert.Contains(c.DrainEvents(), e => e.Kind == ClientEventKind.Reconnecting);
        }

        [Fact]
        public void HostFramesKeepTheClientAliveAcrossLongRuns()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 0);
            for (int i = 1; i <= 80; i++)
            {
                double t = i * 0.25;
                Feed(c, host.World(1 + i), t);
                c.Tick(t);
            }
            Assert.Equal(ClientState.Active, c.State);
        }

        [Fact]
        public void ClosedConnectionWhileActiveTurnsReconnectingAtOnce()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 0);
            c.OnDisconnected(2);
            Assert.Equal(ClientState.Reconnecting, c.State);
            Assert.Contains(c.DrainEvents(), e => e.Kind == ClientEventKind.Reconnecting);
        }

        [Fact]
        public void ReconnectAttemptsGoOutAtTheBackoffScheduleThenFailAsHostLost()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 0);
            c.OnDisconnected(10);
            c.DrainOutbox();

            double[] attemptAt = { 11, 13, 17, 25, 33, 40 };
            for (int i = 0; i < attemptAt.Length; i++)
            {
                c.Tick(attemptAt[i] - 0.01);
                Assert.Empty(c.DrainOutbox());
                c.Tick(attemptAt[i]);
                Assert.Equal(ClientOutputKind.Connect, Assert.Single(c.DrainOutbox()).Kind);
                Assert.Equal(ClientState.Reconnecting, c.State);
                c.OnConnected(attemptAt[i]);
                SessionEnvelope hello = Assert.Single(SentFrames(c));
                Assert.Equal("hello", hello.T);
                Assert.Equal(ScriptedHost.Token, hello.Body.Get("token").Text);
                Assert.Equal(ScriptedHost.Sid, hello.Sid);
                c.OnDisconnected(attemptAt[i] + 0.1);
            }
            c.Tick(46.99);
            Assert.Equal(ClientState.Reconnecting, c.State);
            c.Tick(47);
            Assert.Equal(ClientState.Failed, c.State);
            Assert.Equal(ClientFailure.HostLost, c.FailureReason);
            Assert.Contains(c.DrainEvents(), e => e.Kind == ClientEventKind.Failed && e.Failure == ClientFailure.HostLost);
        }

        [Fact]
        public void SixthReconnectAttemptLandsAtThirtySecondsWithinGrace()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 0);
            c.OnDisconnected(0);
            c.DrainOutbox();
            double[] firstFive = { 1, 3, 7, 15, 23 };
            for (int i = 0; i < firstFive.Length; i++)
            {
                c.Tick(firstFive[i]);
                Assert.Equal(ClientOutputKind.Connect, Assert.Single(c.DrainOutbox()).Kind);
                c.OnConnected(firstFive[i]);
                c.OnDisconnected(firstFive[i]);
                c.DrainOutbox();
            }
            c.Tick(29.99);
            Assert.Empty(c.DrainOutbox());
            c.Tick(30);
            Assert.Equal(ClientOutputKind.Connect, Assert.Single(c.DrainOutbox()).Kind);
        }

        [Fact]
        public void ConnectionThatOpensButNeverWelcomesIsReplacedByTheNextAttempt()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 0);
            c.OnDisconnected(10);
            c.Tick(11);
            c.OnConnected(11);
            c.DrainOutbox();
            c.Tick(12.9);
            Assert.Empty(c.DrainOutbox());
            c.Tick(13);
            Assert.Equal(ClientOutputKind.Connect, Assert.Single(c.DrainOutbox()).Kind);
        }

        [Fact]
        public void WelcomeWhileReconnectingResumesTheSamePeer()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 0, snapshotSeq: 3);
            c.OnDisconnected(10);
            c.Tick(11);
            c.OnConnected(11);
            c.DrainOutbox();
            c.DrainEvents();
            for (int i = 0; i < 10; i++) host.Sim.Tick();
            Feed(c, host.Welcome(8), 11.1);

            Assert.Equal(ClientState.Active, c.State);
            Assert.Equal("p1", c.PeerId);
            Assert.Equal(8, c.LastSnapshotSeq);
            Assert.Equal(SessionTestSupport.BaselineJson(host.Sim), sim.CaptureSnapshot().ToJson());
            List<ClientEvent> events = c.DrainEvents();
            Assert.Contains(events, e => e.Kind == ClientEventKind.Resumed && e.PeerId == "p1");
            Assert.DoesNotContain(events, e => e.Kind == ClientEventKind.Joined);
        }

        [Fact]
        public void ResumeWelcomeForAnotherPeerFails()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 0);
            c.OnDisconnected(10);
            c.Tick(11);
            c.OnConnected(11);
            Feed(c, host.Welcome(8, peer: "p2"), 11.1);
            Assert.Equal(ClientState.Failed, c.State);
            Assert.Equal(ClientFailure.ProtocolError, c.FailureReason);
        }

        [Theory]
        [InlineData(SessionErrorCode.TokenExpired)]
        [InlineData(SessionErrorCode.SessionUnknown)]
        [InlineData(SessionErrorCode.TokenInvalid)]
        public void FatalRejectWhileReconnectingFailsAsSessionLost(SessionErrorCode code)
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 0);
            c.OnDisconnected(10);
            c.Tick(11);
            c.OnConnected(11);
            c.DrainOutbox();
            Feed(c, host.Reject(code, 2, sid: "ffffffffffffffffffffffffffffffff"), 11.1);
            Assert.Equal(ClientState.Failed, c.State);
            Assert.Equal(code, c.FailureCode);
            Assert.Equal(ClientFailure.SessionLost, c.FailureReason);
        }

        [Fact]
        public void TransientRejectWhileReconnectingKeepsRetrying()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 0);
            c.OnDisconnected(10);
            c.Tick(11);
            c.OnConnected(11);
            Feed(c, host.Reject(SessionErrorCode.TooManyConnections), 11.1);
            Assert.Equal(ClientState.Reconnecting, c.State);
            c.DrainOutbox();
            c.Tick(13);
            Assert.Equal(ClientOutputKind.Connect, Assert.Single(c.DrainOutbox()).Kind);
        }

        [Fact]
        public void SequenceNumbersKeepRisingAcrossReconnect()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 0);
            long a = c.SendIntent(PlantIntent.SetRodBank("A", 0.3));
            long b = c.SendIntent(PlantIntent.SetRodBank("B", 0.3));
            Assert.Equal(2, a);
            Assert.Equal(3, b);
            c.OnDisconnected(10);
            c.Tick(11);
            c.OnConnected(11);
            long helloSeq = SentFrames(c).Last().Seq;
            Assert.True(helloSeq > b);
            Feed(c, host.Welcome(9), 11.1);
            long d = c.SendIntent(PlantIntent.SetRodBank("C", 0.3));
            Assert.True(d > helloSeq);
        }

        [Fact]
        public void LeaveSendsGoodbyeThenClosesAndReturnsToIdle()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 0);
            c.Leave(3);
            Assert.Equal(ClientState.Idle, c.State);
            List<ClientOutput> outs = c.DrainOutbox();
            ClientOutput bye = Assert.Single(outs);
            Assert.True(bye.CloseAfter);
            SessionEnvelope env = Sent(bye);
            Assert.Equal("goodbye", env.T);
            Assert.Equal(ScriptedHost.Sid, env.Sid);
            Assert.Equal(0, c.SendIntent(PlantIntent.SetRodBank("A", 0.3)));
        }

        [Fact]
        public void LeaveWhileReconnectingClosesWithoutGoodbye()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 0);
            c.OnDisconnected(10);
            c.DrainOutbox();
            c.Leave(10.5);
            Assert.Equal(ClientState.Idle, c.State);
            Assert.DoesNotContain(c.DrainOutbox(), o => o.Kind == ClientOutputKind.Send);
            c.Tick(11);
            Assert.Empty(c.DrainOutbox());
        }

        [Fact]
        public void AcknowledgeFailureReturnsToIdleAndAFreshJoinHasNoToken()
        {
            var host = new ScriptedHost();
            var c = new SessionClient(new Simulation(), "Ann", "123456");
            StartJoin(c);
            Feed(c, host.Reject(SessionErrorCode.SessionFull, 1), 0.1);
            c.DrainOutbox();
            Assert.Equal(ClientState.Failed, c.State);
            c.AcknowledgeFailure();
            Assert.Equal(ClientState.Idle, c.State);
            Assert.Equal(ClientFailure.None, c.FailureReason);

            SessionEnvelope hello = StartJoin(c, 5);
            Assert.Null(hello.Body.Get("token"));
            Assert.Equal("123456", hello.Body.Get("joinCode").Text);
        }

        [Fact]
        public void AcknowledgeFailureOutsideFailedDoesNothing()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            c.AcknowledgeFailure();
            Assert.Equal(ClientState.Active, c.State);
        }

        [Fact]
        public void SupersededRejectWhileActiveFails()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            Feed(c, host.Reject(SessionErrorCode.Superseded), 1);
            Assert.Equal(ClientState.Failed, c.State);
            Assert.Equal(SessionErrorCode.Superseded, c.FailureCode);
            Assert.Equal(ClientFailure.Superseded, c.FailureReason);
        }

        [Fact]
        public void NonFatalRejectWhileActiveIsSurfacedWithItsSequenceNumber()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            Feed(c, host.Reject(SessionErrorCode.InvalidCrewState, 7), 1);
            Assert.Equal(ClientState.Active, c.State);
            Assert.Contains(c.DrainEvents(), e => e.Kind == ClientEventKind.Refused && e.Code == SessionErrorCode.InvalidCrewState && e.Seq == 7);
        }

        [Fact]
        public void AcceptedIntentResultIsKeptWhenALaterDuplicateArrives()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            long seq = c.SendIntent(PlantIntent.SetRodBank("A", 0.3));
            Feed(c, host.IntentResult(seq, SessionErrorCode.None), 1);
            Feed(c, host.IntentResult(seq, SessionErrorCode.Duplicate), 1.1);

            SessionErrorCode code;
            Assert.True(c.TryGetIntentResult(seq, out code));
            Assert.Equal(SessionErrorCode.None, code);
            List<ClientEvent> results = c.DrainEvents().Where(e => e.Kind == ClientEventKind.IntentResult).ToList();
            ClientEvent only = Assert.Single(results);
            Assert.True(only.Accepted);
            Assert.Equal(seq, only.Seq);
        }

        [Fact]
        public void IntentResultForASequenceNeverSentIsIgnored()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            Feed(c, host.IntentResult(99, SessionErrorCode.None), 1);
            Assert.DoesNotContain(c.DrainEvents(), e => e.Kind == ClientEventKind.IntentResult);
        }

        private static List<ClientEvent> IntentResults(List<ClientEvent> events)
        {
            return events.Where(e => e.Kind == ClientEventKind.IntentResult).ToList();
        }

        private static void AssertConnectionLostThen(List<ClientEvent> events, ClientEventKind next, long first, long second)
        {
            Assert.Equal(3, events.Count);
            Assert.Equal(ClientEventKind.IntentResult, events[0].Kind);
            Assert.Equal(first, events[0].Seq);
            Assert.Equal(SessionErrorCode.ConnectionLost, events[0].Code);
            Assert.False(events[0].Accepted);
            Assert.Equal(ClientEventKind.IntentResult, events[1].Kind);
            Assert.Equal(second, events[1].Seq);
            Assert.Equal(SessionErrorCode.ConnectionLost, events[1].Code);
            Assert.False(events[1].Accepted);
            Assert.Equal(next, events[2].Kind);
        }

        [Fact]
        public void DroppedConnectionResolvesPendingIntentsInOrderBeforeReconnecting()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            long a = c.SendIntent(PlantIntent.SetRodBank("A", 0.3));
            long b = c.SendIntent(PlantIntent.SetRodBank("A", 0.4));
            c.DrainOutbox();
            c.OnDisconnected(2);
            AssertConnectionLostThen(c.DrainEvents(), ClientEventKind.Reconnecting, a, b);
            SessionErrorCode code;
            Assert.True(c.TryGetIntentResult(a, out code));
            Assert.Equal(SessionErrorCode.ConnectionLost, code);
            Assert.True(c.TryGetIntentResult(b, out code));
            Assert.Equal(SessionErrorCode.ConnectionLost, code);
        }

        [Fact]
        public void IntentBeyondPendingCapacityIsRefusedLocallyAndEveryAdmittedIntentStillResolves()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            var admitted = new List<long>();
            for (int i = 0; i < 1024; i++)
                admitted.Add(c.SendIntent(PlantIntent.SetRodBank("A", 0.3)));
            Assert.DoesNotContain(0L, admitted);
            c.DrainOutbox();
            c.DrainEvents();

            long refused = c.SendIntent(PlantIntent.SetRodBank("A", 0.3));
            Assert.Equal(0, refused);
            Assert.Empty(c.DrainOutbox());
            ClientEvent local = Assert.Single(c.DrainEvents());
            Assert.Equal(ClientEventKind.IntentResult, local.Kind);
            Assert.Equal(SessionErrorCode.RateLimited, local.Code);

            c.OnDisconnected(2);
            List<ClientEvent> events = c.DrainEvents();
            List<long> lost = events.Where(e => e.Kind == ClientEventKind.IntentResult && e.Code == SessionErrorCode.ConnectionLost)
                .Select(e => e.Seq).ToList();
            Assert.Equal(admitted, lost);
        }

        [Fact]
        public void HostResultForAResolvedSequenceIsIgnoredAfterResume()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 0, snapshotSeq: 3);
            long a = c.SendIntent(PlantIntent.SetRodBank("A", 0.3));
            c.OnDisconnected(10);
            c.Tick(11);
            c.OnConnected(11);
            Feed(c, host.Welcome(8), 11.1);
            c.DrainOutbox();
            c.DrainEvents();
            Feed(c, host.IntentResult(a, SessionErrorCode.None), 11.2);
            Assert.Empty(IntentResults(c.DrainEvents()));
            SessionErrorCode code;
            Assert.True(c.TryGetIntentResult(a, out code));
            Assert.Equal(SessionErrorCode.ConnectionLost, code);
        }

        [Fact]
        public void HostSilenceResolvesPendingIntentsBeforeReconnecting()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 0);
            long a = c.SendIntent(PlantIntent.SetRodBank("A", 0.3));
            long b = c.SendIntent(PlantIntent.SetRodBank("A", 0.4));
            Feed(c, host.World(2), 1);
            c.DrainOutbox();
            c.DrainEvents();
            c.Tick(6);
            Assert.Equal(ClientState.Reconnecting, c.State);
            AssertConnectionLostThen(c.DrainEvents(), ClientEventKind.Reconnecting, a, b);
        }

        [Fact]
        public void RejectThatFailsTheClientResolvesPendingIntentsBeforeFailed()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            long a = c.SendIntent(PlantIntent.SetRodBank("A", 0.3));
            long b = c.SendIntent(PlantIntent.SetRodBank("A", 0.4));
            c.DrainEvents();
            Feed(c, host.Reject(SessionErrorCode.Superseded), 1);
            Assert.Equal(ClientState.Failed, c.State);
            AssertConnectionLostThen(c.DrainEvents(), ClientEventKind.Failed, a, b);
        }

        [Fact]
        public void AcceptedResultBeforeTheDropIsNotOverwrittenByConnectionLost()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            long a = c.SendIntent(PlantIntent.SetRodBank("A", 0.3));
            long b = c.SendIntent(PlantIntent.SetRodBank("A", 0.4));
            Feed(c, host.IntentResult(a, SessionErrorCode.None), 1);
            c.DrainEvents();
            c.OnDisconnected(2);
            List<ClientEvent> events = c.DrainEvents();
            ClientEvent only = Assert.Single(IntentResults(events));
            Assert.Equal(b, only.Seq);
            Assert.Equal(SessionErrorCode.ConnectionLost, only.Code);
            SessionErrorCode code;
            Assert.True(c.TryGetIntentResult(a, out code));
            Assert.Equal(SessionErrorCode.None, code);
        }

        [Fact]
        public void DropWithNoPendingIntentsRaisesNoIntentResult()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            c.OnDisconnected(2);
            List<ClientEvent> events = c.DrainEvents();
            Assert.Empty(IntentResults(events));
            Assert.Contains(events, e => e.Kind == ClientEventKind.Reconnecting);
        }

        [Fact]
        public void WireIntentResultWithTheClientLocalCodeIsIgnoredAndTheSequenceStaysPending()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            long a = c.SendIntent(PlantIntent.SetRodBank("A", 0.3));
            c.DrainEvents();
            Feed(c, host.IntentResult(a, SessionErrorCode.ConnectionLost), 1);
            Assert.Empty(IntentResults(c.DrainEvents()));
            SessionErrorCode code;
            Assert.False(c.TryGetIntentResult(a, out code));
            c.OnDisconnected(2);
            ClientEvent e = Assert.Single(IntentResults(c.DrainEvents()));
            Assert.Equal(a, e.Seq);
            Assert.Equal(SessionErrorCode.ConnectionLost, e.Code);
        }

        [Fact]
        public void WireRejectWithTheClientLocalCodeIsIgnored()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            Feed(c, host.Reject(SessionErrorCode.ConnectionLost, 7), 1);
            Assert.Equal(ClientState.Active, c.State);
            Assert.Empty(c.DrainEvents());
        }

        [Fact]
        public void LocalLeaveWithPendingIntentsRaisesNoConnectionLostResult()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            c.SendIntent(PlantIntent.SetRodBank("A", 0.3));
            c.DrainEvents();
            c.Leave(2);
            Assert.Equal(ClientState.Idle, c.State);
            Assert.Empty(IntentResults(c.DrainEvents()));
        }

        [Fact]
        public void RefusedIntentResultCarriesTheCode()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            long seq = c.SendIntent(PlantIntent.SetRodBank("A", 0.3));
            Feed(c, host.IntentResult(seq, SessionErrorCode.NotPermitted), 1);
            ClientEvent e = c.DrainEvents().Single(x => x.Kind == ClientEventKind.IntentResult);
            Assert.False(e.Accepted);
            Assert.Equal(SessionErrorCode.NotPermitted, e.Code);
        }

        [Fact]
        public void SendIntentEncodesTheHostShapeAndNeverClaimsAnIssuer()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            long seq = c.SendIntent(PlantIntent.SetRodBank("A", 0.25, "host"));
            SessionEnvelope env = Assert.Single(SentFrames(c));
            Assert.Equal("intent", env.T);
            Assert.Equal(seq, env.Seq);
            Assert.Equal(ScriptedHost.Sid, env.Sid);
            SessionIntentResult decoded = SessionEnvelope.DecodeIntentBody(env.Body);
            Assert.True(decoded.Ok);
            Assert.Equal(IntentKind.RodBankTarget, decoded.Intent.Kind);
            Assert.Equal(0.25, decoded.Intent.Value);
            Assert.Null(env.Body.Get("issuer"));
        }

        [Fact]
        public void SendIntentRefusesLocallyWhenNotActiveAndWhenNonFinite()
        {
            var c = new SessionClient(new Simulation(), "Ann", "123456");
            Assert.Equal(0, c.SendIntent(PlantIntent.SetRodBank("A", 0.3)));
            ClientEvent e = Assert.Single(c.DrainEvents());
            Assert.Equal(ClientEventKind.IntentResult, e.Kind);
            Assert.Equal(SessionErrorCode.NotJoined, e.Code);
            Assert.False(e.Accepted);
            Assert.Empty(c.DrainOutbox());

            var host = new ScriptedHost();
            Simulation sim;
            var active = JoinedClient(host, out sim);
            var bad = new PlantIntent(IntentKind.SteamDemand, "", double.NaN, "local");
            Assert.Equal(0, active.SendIntent(bad));
            Assert.Equal(SessionErrorCode.NonFiniteValue, Assert.Single(active.DrainEvents()).Code);
            Assert.Empty(active.DrainOutbox());
        }

        [Fact]
        public void SendCrewUpdateEncodesTheHostShape()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            long seq = c.SendCrewUpdate(new CrewMember { LocationId = "panel.a", X = 1.5, Y = 0, Z = 2, YawDeg = 90, Pose = CrewPose.Seated });
            SessionEnvelope env = Assert.Single(SentFrames(c));
            Assert.Equal("crew_update", env.T);
            Assert.Equal(seq, env.Seq);
            Assert.Equal("panel.a", env.Body.Get("loc").Text);
            Assert.Equal(1.5, env.Body.Get("x").Number);
            Assert.Equal(90, env.Body.Get("yaw").Number);
            Assert.Equal("seated", env.Body.Get("pose").Text);

            Assert.Equal(0, c.SendCrewUpdate(new CrewMember { LocationId = "panel.a", X = double.NaN }));
            Assert.Empty(c.DrainOutbox());
        }

        [Fact]
        public void StateFrameOverTheLimitFailsWithFrameTooLargeAndCloses()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            var chunk = new byte[65536];
            for (int i = 0; i < chunk.Length; i++) chunk[i] = (byte)'a';
            for (int i = 0; i < 5 && c.State == ClientState.Active; i++)
                c.OnBytes(chunk, 0, chunk.Length, 1);
            Assert.Equal(ClientState.Failed, c.State);
            Assert.Equal(SessionErrorCode.FrameTooLarge, c.FailureCode);
            Assert.Equal(ClientFailure.ProtocolError, c.FailureReason);
            Assert.Contains(c.DrainOutbox(), o => o.Kind == ClientOutputKind.Close);
        }

        [Fact]
        public void FrameExactlyAtTheStateLimitIsAccepted()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            string world = host.World(5);
            int pad = SessionProtocol.StateFrameMaxBytes - Encoding.UTF8.GetByteCount(world);
            Assert.True(pad > 0);
            string padded = world.Substring(0, world.Length - 1) + ",\"pad\":\"" + new string('x', pad - 9) + "\"}";
            Assert.Equal(SessionProtocol.StateFrameMaxBytes, Encoding.UTF8.GetByteCount(padded));
            Assert.Equal(1, c.LastSnapshotSeq);
            Feed(c, padded, 1);
            Assert.Equal(ClientState.Active, c.State);
            Assert.Equal(5, c.LastSnapshotSeq);
        }

        [Fact]
        public void GarbageFromTheHostFailsAsProtocolError()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim);
            Feed(c, "not json", 1);
            Assert.Equal(ClientState.Failed, c.State);
            Assert.Equal(ClientFailure.ProtocolError, c.FailureReason);
            Assert.Equal(SessionErrorCode.ProtocolViolation, c.FailureCode);
        }

        [Fact]
        public void WelcomeWithBrokenWorldFailsAsProtocolError()
        {
            var host = new ScriptedHost();
            var c = new SessionClient(new Simulation(), "Ann", "123456");
            StartJoin(c);
            Feed(c, host.Welcome(1, worldBody: "{\"schema\":1}"), 0.1);
            Assert.Equal(ClientState.Failed, c.State);
            Assert.Equal(ClientFailure.ProtocolError, c.FailureReason);
        }

        [Fact]
        public void WelcomeWithNewerSchemaFailsWithUnsupportedSchema()
        {
            var host = new ScriptedHost();
            var c = new SessionClient(new Simulation(), "Ann", "123456");
            StartJoin(c);
            string body = host.WorldBody(1).Replace("\"schema\":1", "\"schema\":2");
            Feed(c, host.Welcome(1, worldBody: body), 0.1);
            Assert.Equal(ClientState.Failed, c.State);
            Assert.Equal(SessionErrorCode.UnsupportedSchema, c.FailureCode);
        }

        [Fact]
        public void BytesWhileIdleOrFailedAreIgnored()
        {
            var host = new ScriptedHost();
            var sim = new Simulation();
            string before = sim.CaptureSnapshot().ToJson();
            var c = new SessionClient(sim, "Ann", "123456");
            Feed(c, host.Welcome(1), 0);
            Assert.Equal(ClientState.Idle, c.State);
            Assert.Equal(before, sim.CaptureSnapshot().ToJson());
            Assert.Empty(c.DrainEvents());
        }

        [Fact]
        public void FramesSplitAcrossReadsAreReassembled()
        {
            var host = new ScriptedHost();
            var sim = new Simulation();
            var c = new SessionClient(sim, "Ann", "123456");
            StartJoin(c);
            byte[] all = Encoding.UTF8.GetBytes(host.Welcome(2) + "\n");
            int mid = all.Length / 2;
            c.OnBytes(all, 0, mid, 0.1);
            Assert.Equal(ClientState.Joining, c.State);
            c.OnBytes(all, mid, all.Length - mid, 0.1);
            Assert.Equal(ClientState.Active, c.State);
        }

        [Fact]
        public void TimeGoingBackwardCountsAsNoTimePassing()
        {
            var host = new ScriptedHost();
            Simulation sim;
            var c = JoinedClient(host, out sim, now: 10);
            c.Tick(11);
            c.DrainOutbox();
            c.Tick(5);
            Assert.Empty(c.DrainOutbox());
            Assert.Equal(ClientState.Active, c.State);
        }

        [Fact]
        public void ConstructorRejectsNullArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new SessionClient(null, "Ann", "123456"));
            Assert.Throws<ArgumentNullException>(() => new SessionClient(new Simulation(), null, "123456"));
            Assert.Throws<ArgumentNullException>(() => new SessionClient(new Simulation(), "Ann", null));
        }
    }
}
