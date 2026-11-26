using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ReactorSim.Session;
using Xunit;

namespace ReactorSim.Tests
{
    public class SessionHostJoinTests
    {
        private static void AssertRefusal(OutFrame f, SessionErrorCode code, bool close)
        {
            Assert.Equal("reject", f.Type);
            Assert.Equal((int)code, f.Code);
            Assert.Equal(close, f.CloseAfter);
        }

        private static SessionHostHarness NewHost()
        {
            return new SessionHostHarness();
        }

        [Fact]
        public void HostExposesIdentityFromEntropy()
        {
            var h = NewHost();
            Assert.Equal(SessionProtocol.JoinCodeDigits, h.Host.JoinCode.Length);
            Assert.All(h.Host.JoinCode, c => Assert.InRange(c, '0', '9'));
            Assert.Equal(32, h.Host.SessionId.Length);
            Assert.Equal(16, h.Host.HostId.Length);
            Assert.NotEqual(h.Host.SessionId, h.Host.HostId);
        }

        [Fact]
        public void ValidHelloGivesWelcomeForP1WithWorldBeforeAnyOtherFrame()
        {
            var h = NewHost();
            h.Open(1);
            h.Feed(1, h.Hello(1, null, "Ann"));
            h.Tick(0.25);
            List<OutFrame> frames = h.Drain();

            Assert.Equal("welcome", frames[0].Type);
            Assert.False(frames[0].CloseAfter);
            Assert.Equal(1, frames[0].ConnectionId);
            Assert.Equal(h.Host.SessionId, frames[0].Env.Sid);
            JsonValue body = frames[0].Env.Body;
            Assert.Equal("p1", body.Get("peerId").Text);
            Assert.Equal(h.Host.HostId, body.Get("hostId").Text);
            Assert.Equal(h.Host.SessionId, body.Get("sid").Text);
            Assert.Equal(SessionProtocol.TokenBytes * 2, body.Get("token").Text.Length);
            Assert.Equal(SessionProtocol.GraceSeconds, body.Get("graceSeconds").Number);
            WorldSnapshotDecodeResult w = WorldSnapshot.Decode(body.Get("world"), SessionProtocol.WorldSchemaVersion);
            Assert.True(w.Ok);
            Assert.Single(w.World.Crew);
            Assert.Equal("p1", w.World.Crew[0].PeerId);
            Assert.Equal("Ann", w.World.Crew[0].DisplayName);

            Assert.Equal("world", frames[1].Type);
            Assert.True(frames.Skip(1).All(f => f.Type == "world"));
            Assert.True(frames[1].Env.Seq > frames[0].Env.Seq);
        }

        [Fact]
        public void ProtocolTwoHelloGetsUnsupportedProtocolAndPlantIsUnchanged_AE1()
        {
            var h = NewHost();
            string before = h.Sim.CaptureSnapshot().ToJson();
            h.Open(1);
            h.Feed(1, h.Hello(1, null, "Ann", v: 2));
            List<OutFrame> frames = h.Drain();
            Assert.Single(frames);
            AssertRefusal(frames[0], SessionErrorCode.UnsupportedProtocol, true);
            Assert.Equal(before, h.Sim.CaptureSnapshot().ToJson());
            Assert.Empty(h.Sim.DrainPendingIntents());
            Assert.Empty(h.Host.Peers);
        }

        [Fact]
        public void ClientSchemaOlderThanHostGetsUnsupportedSchema()
        {
            var h = NewHost();
            h.Open(1);
            h.Feed(1, h.Hello(1, null, "Ann", schema: 0));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.UnsupportedSchema, true);
        }

        [Fact]
        public void WrongJoinCodeStaysOpenAndCorrectRetryJoins()
        {
            var h = NewHost();
            h.Open(1);
            string wrong = h.Host.JoinCode == "000000" ? "000001" : "000000";
            h.Feed(1, h.Hello(1, wrong));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.BadJoinCode, false);
            h.Feed(1, h.Hello(2, null));
            List<OutFrame> frames = h.Drain();
            Assert.Equal("welcome", Assert.Single(frames).Type);
        }

        [Fact]
        public void FifthWrongJoinCodeClosesTheConnection()
        {
            var h = NewHost();
            h.Open(1);
            string wrong = h.Host.JoinCode == "000000" ? "000001" : "000000";
            for (int i = 1; i <= 4; i++)
            {
                h.Feed(1, h.Hello(i, wrong));
                AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.BadJoinCode, false);
            }
            h.Feed(1, h.Hello(5, wrong));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.BadJoinCode, true);
            h.Feed(1, h.Hello(6, null));
            Assert.Empty(h.Drain());
        }

        [Fact]
        public void SecondJoinerGetsSessionFullWhileP1IsActive()
        {
            var h = NewHost();
            h.Join(1);
            h.Open(2);
            h.Feed(2, h.Hello(1, null, "Bo"));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.SessionFull, true);
        }

        [Fact]
        public void StaleP1KeepsItsSlotAndNewJoinerAfterGraceGetsP2()
        {
            var h = NewHost();
            h.Join(1);
            h.Host.OnDisconnected(1, h.Now);
            Assert.Equal(PeerState.Stale, h.Host.Peers[0].State);

            h.Open(2);
            h.Feed(2, h.Hello(1, null, "Bo"));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.SessionFull, true);

            h.Tick(SessionProtocol.GraceSeconds);
            Assert.Equal(PeerState.Closed, h.Host.Peers[0].State);
            h.Drain();
            h.Open(3);
            h.Feed(3, h.Hello(1, null, "Bo"));
            List<OutFrame> frames = h.Drain();
            Assert.Equal("welcome", Assert.Single(frames).Type);
            Assert.Equal("p2", frames[0].Env.Body.Get("peerId").Text);
        }

        [Fact]
        public void NoHelloWithinFiveSecondsClosesWithHandshakeTimeout()
        {
            var h = NewHost();
            h.Open(1);
            h.Tick(4.9);
            Assert.Empty(h.Drain());
            h.Tick(5.0);
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.HandshakeTimeout, true);
            Assert.Equal(0, h.Host.ConnectionCount);
        }

        [Fact]
        public void FiveSecondsOfSilenceTurnsP1StaleAndTokenResumeAt20SecondsKeepsIdentity_AE4()
        {
            var h = NewHost();
            JoinInfo p1 = h.Join(1, "Ann");
            h.TickAndDrain(5.0);
            Assert.Equal(PeerState.Stale, h.Host.Peers[0].State);
            List<SessionEvent> events = h.Host.DrainEvents();
            Assert.Contains(events, e => e.Kind == SessionEventKind.PeerStale && e.PeerId == "p1");

            h.Tick(25.0);
            h.Open(2);
            h.Feed(2, h.Hello(2, null, "ignored", token: p1.Token, sid: h.Host.SessionId));
            List<OutFrame> frames = h.Drain();
            Assert.Equal("welcome", frames[0].Type);
            Assert.Equal("p1", frames[0].Env.Body.Get("peerId").Text);
            Assert.Equal(p1.Token, frames[0].Env.Body.Get("token").Text);
            WorldSnapshotDecodeResult w = WorldSnapshot.Decode(frames[0].Env.Body.Get("world"), 1);
            Assert.Equal("Ann", w.World.Crew.Single(c => c.PeerId == "p1").DisplayName);
            Assert.Equal(PeerState.Active, h.Host.Peers[0].State);
            Assert.Contains(h.Host.DrainEvents(), e => e.Kind == SessionEventKind.PeerResumed && e.PeerId == "p1");
        }

        [Fact]
        public void TokenReconnectAfterGraceGetsTokenExpired_AE4()
        {
            var h = NewHost();
            JoinInfo p1 = h.Join(1);
            h.TickAndDrain(5.0);
            h.Tick(36.0);
            h.Open(2);
            h.Feed(2, h.Hello(2, null, "Ann", token: p1.Token, sid: h.Host.SessionId));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.TokenExpired, true);
        }

        [Fact]
        public void GraceExpiryIsEvaluatedWhenTheHelloArrivesWithoutAnIntermediateTick()
        {
            var h = NewHost();
            JoinInfo p1 = h.Join(1);
            h.Host.OnDisconnected(1, 0);
            h.Now = 31.0;
            h.Open(2);
            h.Feed(2, h.Hello(2, null, "Ann", token: p1.Token));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.TokenExpired, true);
        }

        [Fact]
        public void WrongTokenGetsTokenInvalid()
        {
            var h = NewHost();
            JoinInfo p1 = h.Join(1);
            h.Host.OnDisconnected(1, h.Now);
            string wrong = (p1.Token[0] == 'a' ? "b" : "a") + p1.Token.Substring(1);
            h.Open(2);
            h.Feed(2, h.Hello(2, null, "Ann", token: wrong));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.TokenInvalid, true);

            h.Open(3);
            h.Feed(3, h.Hello(2, null, "Ann", token: "zz"));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.TokenInvalid, true);
        }

        [Fact]
        public void TokenWithAnotherSessionIdGetsSessionUnknown()
        {
            var h = NewHost();
            JoinInfo p1 = h.Join(1);
            h.Host.OnDisconnected(1, h.Now);
            h.Open(2);
            h.Feed(2, h.Hello(2, null, "Ann", token: p1.Token, sid: "00000000000000000000000000000000"));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.SessionUnknown, true);
        }

        [Fact]
        public void TokenReconnectWhileActiveSupersedesTheOldConnection()
        {
            var h = NewHost();
            JoinInfo p1 = h.Join(1);
            h.Open(2);
            h.Feed(2, h.Hello(2, null, "Ann", token: p1.Token));
            List<OutFrame> frames = h.Drain();
            Assert.Equal(2, frames.Count);
            OutFrame oldClose = frames.Single(f => f.ConnectionId == 1);
            AssertRefusal(oldClose, SessionErrorCode.Superseded, true);
            OutFrame welcome = frames.Single(f => f.ConnectionId == 2);
            Assert.Equal("welcome", welcome.Type);
            Assert.Equal("p1", welcome.Env.Body.Get("peerId").Text);
            Assert.Equal(PeerState.Active, h.Host.Peers[0].State);

            h.Host.OnDisconnected(1, h.Now);
            Assert.Equal(PeerState.Active, h.Host.Peers[0].State);
            Assert.Equal(2, h.Host.Peers[0].ConnectionId);
        }

        [Fact]
        public void GoodbyeFreesTheSlotAtOnce()
        {
            var h = NewHost();
            h.Join(1);
            h.Feed(1, h.Frame("goodbye", 2));
            List<OutFrame> frames = h.Drain();
            Assert.True(frames.All(f => f.CloseAfter));
            Assert.Equal(PeerState.Closed, h.Host.Peers[0].State);
            Assert.Contains(h.Host.DrainEvents(), e => e.Kind == SessionEventKind.PeerLeft && e.PeerId == "p1");

            h.Join(2, "Bo");
            Assert.Equal("p2", h.Host.Peers.Last().Id);
        }

        [Fact]
        public void TokenOfAPeerThatSaidGoodbyeIsExpired()
        {
            var h = NewHost();
            JoinInfo p1 = h.Join(1);
            h.Feed(1, h.Frame("goodbye", 2));
            h.Drain();
            h.Open(2);
            h.Feed(2, h.Hello(2, null, "Ann", token: p1.Token));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.TokenExpired, true);
        }

        [Fact]
        public void SecondHelloOnAnActivePeerGivesProtocolViolation()
        {
            var h = NewHost();
            h.Join(1);
            h.Feed(1, h.Hello(2, null));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.ProtocolViolation, true);
            Assert.Equal(PeerState.Closed, h.Host.Peers[0].State);
        }

        [Fact]
        public void ActiveConnectionClosedByTransportTurnsPeerStaleAtOnce()
        {
            var h = NewHost();
            h.Join(1);
            h.Host.OnDisconnected(1, h.Now);
            Assert.Equal(PeerState.Stale, h.Host.Peers[0].State);
            Assert.Equal(-1, h.Host.Peers[0].ConnectionId);
        }

        [Fact]
        public void TimeGoingBackwardsAdvancesNoTimer()
        {
            var h = NewHost();
            h.Open(1);
            h.Tick(4.0);
            h.Tick(1.0);
            h.Tick(3.0);
            Assert.Empty(h.Drain());
            h.Tick(4.9);
            Assert.Empty(h.Drain());
            Assert.Equal(1, h.Host.ConnectionCount);

            var g = NewHost();
            g.Join(1);
            g.Tick(4.0);
            g.Tick(0.0);
            g.Tick(4.5);
            Assert.Equal(PeerState.Active, g.Host.Peers[0].State);
            g.Tick(5.0);
            Assert.Equal(PeerState.Stale, g.Host.Peers[0].State);
        }

        [Fact]
        public void SevenSecondGapBetweenTicksWithHeartbeatFedFirstKeepsPeerActive()
        {
            var h = NewHost();
            h.Join(1);
            h.TickAndDrain(1.0);
            h.Now = 8.0;
            h.Feed(1, h.Frame("heartbeat", 2));
            h.Tick(8.0);
            Assert.Equal(PeerState.Active, h.Host.Peers[0].State);
        }

        [Fact]
        public void HeartbeatOnHandshakingConnectionIsDropped()
        {
            var h = NewHost();
            h.Open(1);
            h.Feed(1, h.Frame("heartbeat", 1));
            Assert.Empty(h.Drain());
            Assert.Equal(1, h.Host.ConnectionCount);
        }

        [Fact]
        public void GoodbyeOnHandshakingConnectionCloses()
        {
            var h = NewHost();
            h.Open(1);
            h.Feed(1, h.Frame("goodbye", 1));
            Assert.True(h.Drain().All(f => f.CloseAfter));
            Assert.Equal(0, h.Host.ConnectionCount);
        }

        [Fact]
        public void CrewUpdateOnHandshakingConnectionGetsNotJoined()
        {
            var h = NewHost();
            h.Open(1);
            h.Feed(1, h.CrewFrame(1, "control-room"));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.NotJoined, false);
            Assert.Equal(1, h.Host.ConnectionCount);
        }

        [Theory]
        [InlineData("{\"v\":1,\"t\":\"warp\",\"seq\":1,\"sid\":\"x\",\"body\":{}}")]
        [InlineData("not json at all")]
        [InlineData("{\"v\":1,\"t\":\"welcome\",\"seq\":1,\"sid\":\"x\",\"body\":{}}")]
        public void UnknownTypeBadJsonOrHostOnlyTypeOnHandshakingGivesProtocolViolation(string line)
        {
            var h = NewHost();
            h.Open(1);
            h.Feed(1, line);
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.ProtocolViolation, true);
            Assert.Equal(0, h.Host.ConnectionCount);
        }

        [Fact]
        public void OverLimitFrameOnHandshakingGivesFrameTooLarge()
        {
            var h = NewHost();
            h.Open(1);
            h.FeedRaw(1, Enumerable.Repeat((byte)'a', SessionProtocol.ControlFrameMaxBytes + 1).ToArray());
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.FrameTooLarge, true);
            Assert.Equal(0, h.Host.ConnectionCount);
        }

        [Fact]
        public void FrameOfExactlyTheLimitIsNotTooLarge()
        {
            var h = NewHost();
            h.Open(1);
            var bytes = Enumerable.Repeat((byte)'a', SessionProtocol.ControlFrameMaxBytes).Concat(new[] { (byte)'\n' }).ToArray();
            h.FeedRaw(1, bytes);
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.ProtocolViolation, true);
        }

        [Fact]
        public void HeartbeatOnActivePeerOnlyRefreshesLiveness()
        {
            var h = NewHost();
            h.Join(1);
            h.Now = 4.0;
            h.Feed(1, h.Frame("heartbeat", 2));
            Assert.Empty(h.Drain());
            h.Tick(8.0);
            Assert.Equal(PeerState.Active, h.Host.Peers[0].State);
            h.Tick(9.0);
            Assert.Equal(PeerState.Stale, h.Host.Peers[0].State);
        }

        [Fact]
        public void UnknownTypeOnActivePeerGivesProtocolViolation()
        {
            var h = NewHost();
            h.Join(1);
            h.Feed(1, "{\"v\":1,\"t\":\"warp\",\"seq\":2,\"sid\":\"" + h.Host.SessionId + "\",\"body\":{}}");
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.ProtocolViolation, true);
            Assert.Equal(PeerState.Closed, h.Host.Peers[0].State);
        }

        [Fact]
        public void HostOnlyTypeSentByActiveClientGivesProtocolViolation()
        {
            var h = NewHost();
            h.Join(1);
            h.Feed(1, h.Frame("world", 2));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.ProtocolViolation, true);
        }

        [Fact]
        public void WrongSessionIdOnActiveConnectionGivesSessionUnknown()
        {
            var h = NewHost();
            h.Join(1);
            h.Feed(1, SessionEnvelope.Encode("heartbeat", 2, "00000000000000000000000000000000", "{}"));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.SessionUnknown, true);
        }

        [Fact]
        public void IdleHandshakingConnectionsDoNotBlockTokenResumeOfStalePeer()
        {
            var h = NewHost();
            JoinInfo p1 = h.Join(1, "Ann");
            h.TickAndDrain(5.0);
            Assert.Equal(PeerState.Stale, h.Host.Peers[0].State);
            h.Open(2);
            h.Open(3);
            h.Now = 6.0;
            h.Open(4);
            h.Feed(4, h.Hello(2, null, "ignored", token: p1.Token, sid: h.Host.SessionId));
            List<OutFrame> frames = h.Drain();
            Assert.Equal(2, frames.Count);
            Assert.Equal(2, frames[0].ConnectionId);
            AssertRefusal(frames[0], SessionErrorCode.TooManyConnections, true);
            Assert.Equal(4, frames[1].ConnectionId);
            Assert.Equal("welcome", frames[1].Type);
            Assert.Equal("p1", frames[1].Env.Body.Get("peerId").Text);
            Assert.Equal(PeerState.Active, h.Host.Peers[0].State);
        }

        [Fact]
        public void ThirdConcurrentHandshakingConnectionEvictsTheOldest()
        {
            var h = NewHost();
            h.Open(1);
            h.Open(2);
            h.Open(3);
            List<OutFrame> frames = h.Drain();
            OutFrame f = Assert.Single(frames);
            Assert.Equal(1, f.ConnectionId);
            AssertRefusal(f, SessionErrorCode.TooManyConnections, true);
            Assert.Equal(2, h.Host.ConnectionCount);
            h.Feed(2, h.Hello(1));
            OutFrame w2 = Assert.Single(h.Drain());
            Assert.Equal("welcome", w2.Type);
            Assert.Equal(2, w2.ConnectionId);
        }

        [Fact]
        public void TimedOutHandshakingConnectionsAreExpiredBeforeAnyEviction()
        {
            var h = NewHost();
            h.Open(1);
            h.Open(2);
            h.Now = 6.0;
            h.Open(3);
            List<OutFrame> frames = h.Drain();
            Assert.Equal(2, frames.Count);
            Assert.Equal(1, frames[0].ConnectionId);
            AssertRefusal(frames[0], SessionErrorCode.HandshakeTimeout, true);
            Assert.Equal(2, frames[1].ConnectionId);
            AssertRefusal(frames[1], SessionErrorCode.HandshakeTimeout, true);
            Assert.DoesNotContain(frames, x => x.Code == (int)SessionErrorCode.TooManyConnections);
            Assert.Equal(1, h.Host.ConnectionCount);
        }

        [Fact]
        public void EvictionRaisesConnectionRefusedForTheEvictedConnection()
        {
            var h = NewHost();
            h.Open(1);
            h.Open(2);
            h.Open(3);
            List<SessionEvent> events = h.Host.DrainEvents();
            SessionEvent e = Assert.Single(events, x => x.Kind == SessionEventKind.ConnectionRefused);
            Assert.Equal(1, e.ConnectionId);
            Assert.Equal(SessionErrorCode.TooManyConnections, e.Code);
        }

        [Fact]
        public void EvictionNeverTouchesAnActivePeerConnection()
        {
            var h = NewHost();
            h.Join(1);
            h.Open(2);
            h.Open(3);
            h.Open(4);
            List<OutFrame> frames = h.Drain();
            OutFrame f = Assert.Single(frames);
            Assert.Equal(2, f.ConnectionId);
            AssertRefusal(f, SessionErrorCode.TooManyConnections, true);
            Assert.Equal(PeerState.Active, h.Host.Peers[0].State);
            Assert.Equal(3, h.Host.ConnectionCount);
        }

        [Fact]
        public void EvictionScenarioProducesIdenticalOutboxBytesAcrossRuns()
        {
            Func<List<string>> run = () =>
            {
                var h = NewHost();
                JoinInfo p1 = h.Join(1, "Ann");
                h.TickAndDrain(5.0);
                h.Open(2);
                h.Open(3);
                h.Now = 6.0;
                h.Open(4);
                h.Feed(4, h.Hello(2, null, "x", token: p1.Token, sid: h.Host.SessionId));
                var lines = new List<string>();
                foreach (OutFrame o in h.Drain())
                    lines.Add(o.ConnectionId + "|" + o.CloseAfter + "|" + o.Frame);
                return lines;
            };
            List<string> a = run();
            List<string> b = run();
            Assert.Equal(2, a.Count);
            Assert.Equal(a, b);
        }

        [Fact]
        public void ActiveConnectionDoesNotCountTowardsHandshakingLimit()
        {
            var h = NewHost();
            h.Join(1);
            h.Open(2);
            h.Open(3);
            Assert.Empty(h.Drain());
            Assert.Equal(3, h.Host.ConnectionCount);
        }

        [Fact]
        public void FiftyFramesInOneSecondAreAllowedAndFiftyFirstGetsRateLimited()
        {
            var h = NewHost();
            h.Join(1);
            for (int i = 0; i < 49; i++)
                h.Feed(1, h.Frame("heartbeat", 2 + i));
            Assert.Empty(h.Drain());
            h.Feed(1, h.Frame("heartbeat", 51));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.RateLimited, true);
            Assert.Equal(0, h.Host.ConnectionCount);
        }

        [Fact]
        public void FrameRateWindowResetsAfterOneSecond()
        {
            var h = NewHost();
            h.Join(1);
            for (int i = 0; i < 49; i++)
                h.Feed(1, h.Frame("heartbeat", 2 + i));
            h.Now = 1.5;
            for (int i = 0; i < 50; i++)
                h.Feed(1, h.Frame("heartbeat", 52 + i));
            Assert.Empty(h.Drain());
        }

        [Fact]
        public void ClientThatDrainsNothingForTenSecondsHoldsAtMostOneWorld()
        {
            var h = NewHost();
            h.Join(1);
            long seq = 2;
            for (double t = 0.25; t <= 10.0; t += 0.25)
            {
                h.Now = t;
                h.Feed(1, h.Frame("heartbeat", seq++));
                h.Tick(t);
            }
            List<OutFrame> frames = h.Drain();
            Assert.Equal(PeerState.Active, h.Host.Peers[0].State);
            Assert.Equal("world", Assert.Single(frames).Type);
        }

        [Fact]
        public void NewerWorldReplacesTheUnsentOne()
        {
            var h = NewHost();
            h.Join(1);
            h.Tick(0.25);
            h.Feed(1, h.Frame("heartbeat", 2));
            h.Tick(0.5);
            OutFrame world = Assert.Single(h.Drain());
            Assert.Equal(3, (long)world.Env.Body.Get("snapshotSeq").Number);
        }

        [Fact]
        public void WorldFramesGoOnlyToActivePeersEveryQuarterSecond()
        {
            var h = NewHost();
            h.Join(1);
            h.Tick(0.2);
            Assert.Empty(h.Drain());
            h.Tick(0.25);
            List<OutFrame> frames = h.Drain();
            Assert.Equal("world", Assert.Single(frames).Type);
            Assert.False(frames[0].CloseAfter);
            h.Feed(1, h.Frame("heartbeat", 2));
            h.Tick(0.5);
            long first = (long)frames[0].Env.Body.Get("snapshotSeq").Number;
            List<OutFrame> next = h.Drain();
            Assert.Equal(first + 1, (long)Assert.Single(next).Env.Body.Get("snapshotSeq").Number);

            h.Host.OnDisconnected(1, 0.5);
            h.Tick(1.0);
            Assert.Empty(h.Drain());
        }

        [Fact]
        public void WorldFramesAreSentWhileTheClockIsPaused()
        {
            var h = NewHost();
            h.Join(1);
            h.Sim.Clock.ApplyRemoteState(true, 1.0);
            h.Tick(0.25);
            OutFrame world = Assert.Single(h.Drain());
            Assert.True(world.Env.Body.Get("clockPaused").Bool);
        }

        [Fact]
        public void FixedTimeCompareAcceptsEqualAndRefusesFirstOrLastByteDifference()
        {
            var a = new byte[16];
            for (int i = 0; i < a.Length; i++) a[i] = (byte)(i + 1);
            var equal = (byte[])a.Clone();
            var first = (byte[])a.Clone();
            first[0] ^= 1;
            var last = (byte[])a.Clone();
            last[15] ^= 0x80;
            Assert.True(SessionIds.FixedTimeEquals(a, equal));
            Assert.False(SessionIds.FixedTimeEquals(a, first));
            Assert.False(SessionIds.FixedTimeEquals(a, last));
            Assert.False(SessionIds.FixedTimeEquals(a, new byte[15]));
            Assert.False(SessionIds.FixedTimeEquals(a, null));
        }

        [Fact]
        public void HexAndJoinCodeHelpersBehave()
        {
            Assert.Equal("00ff10", SessionIds.ToHex(new byte[] { 0x00, 0xFF, 0x10 }));
            byte[] parsed;
            Assert.True(SessionIds.TryParseHex("00ff10", 3, out parsed));
            Assert.Equal(new byte[] { 0x00, 0xFF, 0x10 }, parsed);
            Assert.False(SessionIds.TryParseHex("00ff1", 3, out parsed));
            Assert.False(SessionIds.TryParseHex("00ff1g", 3, out parsed));
            Assert.False(SessionIds.TryParseHex("00FF10", 3, out parsed));
            Assert.Equal("000000", SessionIds.JoinCodeFrom(new byte[] { 0, 0, 0, 0 }));
            Assert.Equal(6, SessionIds.JoinCodeFrom(new byte[] { 255, 255, 255, 255 }).Length);
        }

        [Fact]
        public void SameEntropyAndInputsGiveIdenticalOutboxBytes()
        {
            Func<List<string>> run = () =>
            {
                var h = NewHost();
                var all = new List<string>();
                h.Open(1);
                h.Feed(1, h.Hello(1, null, "Ann"));
                h.Tick(0.25);
                h.Feed(1, h.IntentFrame(2, "RodBankTarget"));
                h.Feed(1, h.CrewFrame(3, "control-room"));
                h.Tick(0.5);
                h.Host.OnDisconnected(1, 0.5);
                h.Tick(6.0);
                foreach (var o in h.Host.DrainOutbox())
                    all.Add(o.ConnectionId + "|" + o.Frame + "|" + o.CloseAfter);
                return all;
            };
            List<string> a = run();
            List<string> b = run();
            Assert.NotEmpty(a);
            Assert.Equal(a, b);
        }

        [Fact]
        public void EveryHostFrameCarriesTheSessionId()
        {
            var h = NewHost();
            h.Open(1);
            h.Feed(1, h.Hello(1, "999999x"));
            h.Feed(1, h.Hello(2, null));
            h.Tick(0.25);
            foreach (OutFrame f in h.Drain())
                Assert.Equal(h.Host.SessionId, f.Env.Sid);
        }

        [Fact]
        public void HelloWithMissingOrBadFieldsGivesProtocolViolation()
        {
            var h = NewHost();
            h.Open(1);
            h.Feed(1, "{\"v\":1,\"t\":\"hello\",\"seq\":1,\"body\":{\"joinCode\":\"1\"}}");
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.ProtocolViolation, true);
        }

        [Fact]
        public void HelloWithInvalidDisplayNameGivesInvalidCrewState()
        {
            var h = NewHost();
            h.Open(1);
            h.Feed(1, h.Hello(1, null, "   "));
            AssertRefusal(Assert.Single(h.Drain()), SessionErrorCode.InvalidCrewState, true);
        }

        [Fact]
        public void UnknownConnectionInputsAreIgnored()
        {
            var h = NewHost();
            h.Feed(99, h.Hello(1, null));
            h.Host.OnDisconnected(99, 0);
            Assert.Empty(h.Drain());
            h.Open(1);
            h.Open(1);
            Assert.Equal(1, h.Host.ConnectionCount);
        }

        [Fact]
        public void DisconnectOfHandshakingConnectionFreesItsSlot()
        {
            var h = NewHost();
            h.Open(1);
            h.Open(2);
            h.Host.OnDisconnected(1, 0);
            h.Open(3);
            Assert.Empty(h.Drain());
            Assert.Equal(2, h.Host.ConnectionCount);
        }
    }
}
