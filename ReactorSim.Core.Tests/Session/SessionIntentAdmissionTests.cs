using System;
using System.Collections.Generic;
using System.Linq;
using ReactorSim.Session;
using Xunit;

namespace ReactorSim.Tests
{
    // KTD10 admission order and KTD11 crew updates, driven through the host's byte interface.
    public class SessionIntentAdmissionTests
    {
        private static SessionHostHarness JoinedHost(out JoinInfo p1, Func<string, bool> validator = null)
        {
            var h = new SessionHostHarness(validator);
            p1 = h.Join(1);
            return h;
        }

        private static OutFrame OnlyResult(SessionHostHarness h, long seq, SessionErrorCode code)
        {
            OutFrame f = Assert.Single(h.Drain());
            Assert.Equal("intent_result", f.Type);
            Assert.Equal(seq, (long)f.Env.Body.Get("seq").Number);
            Assert.Equal((int)code, f.Code);
            Assert.Equal(code == SessionErrorCode.None, f.Env.Body.Get("accepted").Bool);
            Assert.False(f.CloseAfter);
            return f;
        }

        [Fact]
        public void RodBankIntentClaimingHostIsQueuedWithPeerIdAsIssuer_AE2()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Feed(1, h.IntentFrame(2, "RodBankTarget", "0.5", "host"));
            OnlyResult(h, 2, SessionErrorCode.None);
            List<PlantIntent> queued = h.Sim.DrainPendingIntents();
            PlantIntent intent = Assert.Single(queued);
            Assert.Equal(IntentKind.RodBankTarget, intent.Kind);
            Assert.Equal("p1", intent.Issuer);
            Assert.Equal(0.5, intent.Value);
        }

        [Fact]
        public void DevClassIntentGetsNotPermittedAndNothingIsQueued()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Feed(1, h.IntentFrame(2, "LoadPreset", "1"));
            OnlyResult(h, 2, SessionErrorCode.NotPermitted);
            Assert.Empty(h.Sim.DrainPendingIntents());
            h.Feed(1, h.IntentFrame(3, "SetTimeScaleDev", "10"));
            OnlyResult(h, 3, SessionErrorCode.NotPermitted);
            Assert.Empty(h.Sim.DrainPendingIntents());
        }

        [Fact]
        public void NonFiniteValueMapsToNonFiniteValueAndNothingIsQueued()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            Assert.Equal(SessionErrorCode.NonFiniteValue, h.Host.EnqueueForPeer("p1", new PlantIntent(IntentKind.RodBankTarget, "", double.NaN, "x", -1.0)));
            Assert.Equal(SessionErrorCode.NonFiniteValue, h.Host.EnqueueForPeer("p1", new PlantIntent(IntentKind.RodBankTarget, "", double.PositiveInfinity, "x", -1.0)));
            Assert.Empty(h.Sim.DrainPendingIntents());
            Assert.Equal(SessionErrorCode.None, h.Host.EnqueueForPeer("p1", new PlantIntent(IntentKind.RodBankTarget, "", 0.3, "host", -1.0)));
            Assert.Equal("p1", Assert.Single(h.Sim.DrainPendingIntents()).Issuer);
        }

        [Fact]
        public void OverflowingNumberOnTheWireClosesWithProtocolViolationAndQueuesNothing()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Feed(1, h.Frame("intent", 2, "{\"kind\":\"RodBankTarget\",\"value\":1e999}"));
            OutFrame f = Assert.Single(h.Drain());
            Assert.Equal((int)SessionErrorCode.ProtocolViolation, f.Code);
            Assert.True(f.CloseAfter);
            Assert.Empty(h.Sim.DrainPendingIntents());
        }

        [Fact]
        public void ClockIntentIsRefusedByDefaultAndAcceptedOnceTheHostAllowsIt()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            Assert.False(h.Host.AllowRemoteClock);
            foreach (string kind in new[] { "PauseClock", "ResumeClock", "SetTimeScale", "SkipAhead" })
            {
                long seq = 2 + Array.IndexOf(new[] { "PauseClock", "ResumeClock", "SetTimeScale", "SkipAhead" }, kind);
                h.Feed(1, h.IntentFrame(seq, kind, "1"));
                OnlyResult(h, seq, SessionErrorCode.NotPermitted);
            }
            Assert.Empty(h.Sim.DrainPendingIntents());

            h.Host.AllowRemoteClock = true;
            h.Feed(1, h.IntentFrame(6, "PauseClock", "0"));
            OnlyResult(h, 6, SessionErrorCode.None);
            PlantIntent q = Assert.Single(h.Sim.DrainPendingIntents());
            Assert.Equal(IntentKind.PauseClock, q.Kind);
            Assert.Equal("p1", q.Issuer);
        }

        [Fact]
        public void RepeatedOrLowerSequenceGetsDuplicateAndIntentIsQueuedOnce()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Feed(1, h.IntentFrame(5, "RodBankTarget"));
            OnlyResult(h, 5, SessionErrorCode.None);
            h.Feed(1, h.IntentFrame(5, "RodBankTarget"));
            OnlyResult(h, 5, SessionErrorCode.Duplicate);
            h.Feed(1, h.IntentFrame(3, "RodBankTarget"));
            OnlyResult(h, 3, SessionErrorCode.Duplicate);
            Assert.Single(h.Sim.DrainPendingIntents());
            Assert.Contains(h.Host.DrainEvents(), e => e.Kind == SessionEventKind.IntentRefused && e.Code == SessionErrorCode.Duplicate && e.Seq == 3);
        }

        [Fact]
        public void HelloSequenceCountsSoAnIntentAtTheSameNumberIsDuplicate()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Feed(1, h.IntentFrame(1, "RodBankTarget"));
            OnlyResult(h, 1, SessionErrorCode.Duplicate);
            Assert.Empty(h.Sim.DrainPendingIntents());
        }

        [Fact]
        public void SequenceSurvivesReconnect()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Feed(1, h.IntentFrame(7, "RodBankTarget"));
            h.Drain();
            h.Sim.DrainPendingIntents();
            h.Host.OnDisconnected(1, h.Now);
            h.Open(2);
            h.Feed(2, h.Hello(8, null, "Ann", token: p1.Token));
            Assert.Equal("welcome", Assert.Single(h.Drain()).Type);
            h.Feed(2, h.IntentFrame(7, "RodBankTarget"));
            OutFrame f = Assert.Single(h.Drain());
            Assert.Equal((int)SessionErrorCode.Duplicate, f.Code);
            Assert.Empty(h.Sim.DrainPendingIntents());
        }

        [Fact]
        public void SequenceAtTheWireMaximumAcceptsOnceThenRepeatIsDuplicateAndConnectionStaysOpen()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Feed(1, h.IntentFrame(SessionProtocol.MaxWireInteger, "RodBankTarget"));
            OnlyResult(h, SessionProtocol.MaxWireInteger, SessionErrorCode.None);
            h.Feed(1, h.IntentFrame(SessionProtocol.MaxWireInteger, "RodBankTarget"));
            OnlyResult(h, SessionProtocol.MaxWireInteger, SessionErrorCode.Duplicate);
            Assert.Equal(PeerState.Active, h.Host.Peers[0].State);
            Assert.Equal(1, h.Host.ConnectionCount);
        }

        [Theory]
        [InlineData("{\"kind\":\"NotARealKind\",\"value\":1}")]
        [InlineData("{\"kind\":\"rodbanktarget\",\"value\":1}")]
        [InlineData("{\"kind\":7,\"value\":1}")]
        [InlineData("{\"value\":1}")]
        [InlineData("{\"kind\":\"RodBankTarget\"}")]
        [InlineData("{\"kind\":\"RodBankTarget\",\"value\":\"1\"}")]
        [InlineData("{\"kind\":\"RodBankTarget\",\"value\":1,\"target\":5}")]
        public void UndefinedOrMalformedIntentGetsInvalidIntentAndHostKeepsRunning(string body)
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Feed(1, h.Frame("intent", 2, body));
            OnlyResult(h, 2, SessionErrorCode.InvalidIntent);
            Assert.Empty(h.Sim.DrainPendingIntents());
            h.Sim.Tick();
            h.Feed(1, h.IntentFrame(3, "RodBankTarget"));
            OnlyResult(h, 3, SessionErrorCode.None);
        }

        [Fact]
        public void OverlongTargetGetsInvalidIntent()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            string target = new string('t', SessionProtocol.IntentStringMaxChars + 1);
            h.Feed(1, h.Frame("intent", 2, "{\"kind\":\"RodBankTarget\",\"target\":\"" + target + "\",\"value\":1}"));
            OnlyResult(h, 2, SessionErrorCode.InvalidIntent);
        }

        [Fact]
        public void AcceptedIntentWhileHostClockIsPausedIsQueuedNotApplied()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Sim.Clock.ApplyRemoteState(true, 1.0);
            h.Feed(1, h.IntentFrame(2, "RodBankTarget", "0.9"));
            OnlyResult(h, 2, SessionErrorCode.None);
            Assert.Single(h.Sim.DrainPendingIntents());
        }

        [Fact]
        public void TwentyOneIntentsAtOneInstantAcceptTwentyThenRateLimit()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Now = 1.0;
            var codes = new List<int>();
            for (int i = 0; i < 21; i++)
            {
                h.Feed(1, h.IntentFrame(2 + i, "RodBankTarget"));
                codes.Add(Assert.Single(h.Drain()).Code);
            }
            Assert.Equal(20, codes.Count(c => c == 0));
            Assert.Equal((int)SessionErrorCode.RateLimited, codes[20]);
            Assert.Equal(20, h.Sim.DrainPendingIntents().Count);
        }

        [Fact]
        public void BucketRefillsOneTokenAfterTenthOfASecondAndCapsAfterAHugeJump()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Now = 1.0;
            long seq = 2;
            for (int i = 0; i < 21; i++)
            {
                h.Feed(1, h.IntentFrame(seq++, "RodBankTarget"));
                h.Drain();
            }
            h.Sim.DrainPendingIntents();

            h.Now = 1.1;
            h.Feed(1, h.IntentFrame(seq++, "RodBankTarget"));
            Assert.Equal((int)SessionErrorCode.None, Assert.Single(h.Drain()).Code);
            h.Feed(1, h.IntentFrame(seq++, "RodBankTarget"));
            Assert.Equal((int)SessionErrorCode.RateLimited, Assert.Single(h.Drain()).Code);

            h.Now = 1001.1;
            int accepted = 0;
            for (int i = 0; i < 25; i++)
            {
                h.Feed(1, h.IntentFrame(seq++, "RodBankTarget"));
                if (Assert.Single(h.Drain()).Code == 0) accepted++;
            }
            Assert.Equal(20, accepted);
        }

        [Fact]
        public void RateLimitedIntentIsRefusedBeforeClassCheckAndStillSpendsTokens()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Now = 1.0;
            long seq = 2;
            for (int i = 0; i < 20; i++)
            {
                h.Feed(1, h.IntentFrame(seq++, "LoadPreset", "1"));
                Assert.Equal((int)SessionErrorCode.NotPermitted, Assert.Single(h.Drain()).Code);
            }
            h.Feed(1, h.IntentFrame(seq++, "LoadPreset", "1"));
            Assert.Equal((int)SessionErrorCode.RateLimited, Assert.Single(h.Drain()).Code);
        }

        [Fact]
        public void IntentOnHandshakingConnectionGetsNotJoined()
        {
            var h = new SessionHostHarness();
            h.Open(1);
            h.Feed(1, h.IntentFrame(1, "RodBankTarget"));
            OutFrame f = Assert.Single(h.Drain());
            Assert.Equal("intent_result", f.Type);
            Assert.Equal((int)SessionErrorCode.NotJoined, f.Code);
            Assert.False(f.CloseAfter);
            Assert.Empty(h.Sim.DrainPendingIntents());
        }

        [Fact]
        public void IntentSentAfterGoodbyeIsIgnoredBecauseTheConnectionIsGone()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Feed(1, h.Frame("goodbye", 2));
            h.Drain();
            h.Feed(1, h.IntentFrame(3, "RodBankTarget"));
            Assert.Empty(h.Drain());
            Assert.Empty(h.Sim.DrainPendingIntents());
        }

        [Fact]
        public void HostUiIntentAndPeerIntentApplyInQueueOrder()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Sim.EnqueueIntent(new PlantIntent(IntentKind.RodBankTarget, "", 0.1, "local", -1.0));
            h.Feed(1, h.IntentFrame(2, "RodBankTarget", "0.9"));
            h.Drain();
            h.Sim.EnqueueIntent(new PlantIntent(IntentKind.RodBankTarget, "", 0.2, "local", -1.0));
            List<PlantIntent> queue = h.Sim.DrainPendingIntents();
            Assert.Equal(new[] { "local", "p1", "local" }, queue.Select(i => i.Issuer).ToArray());
            Assert.Equal(new[] { 0.1, 0.9, 0.2 }, queue.Select(i => i.Value).ToArray());
        }

        [Fact]
        public void FourThousandNinetySevenByteFrameGetsFrameTooLargeAndIsDisconnected_AE3()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.FeedRaw(1, Enumerable.Repeat((byte)'a', SessionProtocol.ControlFrameMaxBytes + 1).ToArray());
            OutFrame f = Assert.Single(h.Drain());
            Assert.Equal("reject", f.Type);
            Assert.Equal((int)SessionErrorCode.FrameTooLarge, f.Code);
            Assert.True(f.CloseAfter);
            Assert.Empty(h.Sim.DrainPendingIntents());
            Assert.Equal(PeerState.Closed, h.Host.Peers[0].State);
        }

        [Fact]
        public void OversizedIntentFrameSplitAcrossReadsStillFaultsBeforeQueueing()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            byte[] big = Enumerable.Repeat((byte)'a', 3000).ToArray();
            h.FeedRaw(1, big);
            h.FeedRaw(1, big);
            Assert.Equal((int)SessionErrorCode.FrameTooLarge, Assert.Single(h.Drain()).Code);
            Assert.Empty(h.Sim.DrainPendingIntents());
        }

        // ---- crew updates ----

        private static CrewMember CrewOf(SessionHostHarness h, string peerId)
        {
            return h.Host.GetCrew().Single(c => c.PeerId == peerId);
        }

        [Fact]
        public void ValidCrewUpdateChangesPeerEntryInNextWorld()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Feed(1, h.CrewFrame(2, "control-room", "seated", "3.5", "Annie"));
            Assert.Empty(h.Drain());
            h.Tick(0.25);
            OutFrame world = Assert.Single(h.Drain());
            WorldSnapshotDecodeResult w = WorldSnapshot.Decode(world.Env.Body, 1);
            Assert.True(w.Ok);
            CrewMember m = w.World.Crew.Single();
            Assert.Equal("p1", m.PeerId);
            Assert.Equal("control-room", m.LocationId);
            Assert.Equal(CrewPose.Seated, m.Pose);
            Assert.Equal(3.5, m.X);
            Assert.Equal(2.0, m.Z);
            Assert.Equal(90.0, m.YawDeg);
            Assert.Equal("Annie", m.DisplayName);
        }

        [Fact]
        public void CrewUpdateWithoutNameKeepsTheHelloName()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Feed(1, h.CrewFrame(2, "turbine-hall"));
            Assert.Equal("Ann", CrewOf(h, "p1").DisplayName);
            Assert.Equal("turbine-hall", CrewOf(h, "p1").LocationId);
        }

        [Theory]
        [InlineData("Bad Location")]
        [InlineData("UPPER")]
        [InlineData("")]
        public void InvalidLocationGetsInvalidCrewStateAndEntryIsUnchanged(string loc)
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Feed(1, h.CrewFrame(2, "control-room"));
            h.Drain();
            h.Feed(1, h.CrewFrame(3, loc));
            OutFrame f = Assert.Single(h.Drain());
            Assert.Equal("reject", f.Type);
            Assert.Equal((int)SessionErrorCode.InvalidCrewState, f.Code);
            Assert.Equal(3, (long)f.Env.Body.Get("seq").Number);
            Assert.False(f.CloseAfter);
            Assert.Equal("control-room", CrewOf(h, "p1").LocationId);
            Assert.Contains(h.Host.DrainEvents(), e => e.Kind == SessionEventKind.CrewRefused && e.Code == SessionErrorCode.InvalidCrewState);
        }

        [Theory]
        [InlineData("{\"loc\":\"a\",\"x\":1,\"y\":0,\"z\":0,\"yaw\":0,\"pose\":\"flying\"}")]
        [InlineData("{\"loc\":\"a\",\"x\":\"1\",\"y\":0,\"z\":0,\"yaw\":0,\"pose\":\"standing\"}")]
        [InlineData("{\"loc\":\"a\",\"y\":0,\"z\":0,\"yaw\":0,\"pose\":\"standing\"}")]
        [InlineData("{\"loc\":5,\"x\":1,\"y\":0,\"z\":0,\"yaw\":0,\"pose\":\"standing\"}")]
        [InlineData("{\"loc\":\"a\",\"x\":1,\"y\":0,\"z\":0,\"yaw\":0,\"pose\":\"standing\",\"name\":\"\"}")]
        [InlineData("{\"loc\":\"a\",\"x\":1,\"y\":0,\"z\":0,\"yaw\":0,\"pose\":\"standing\",\"name\":\"123456789012345678901234x\"}")]
        public void MalformedCrewUpdateGetsInvalidCrewState(string body)
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Feed(1, h.Frame("crew_update", 2, body));
            OutFrame f = Assert.Single(h.Drain());
            Assert.Equal((int)SessionErrorCode.InvalidCrewState, f.Code);
            Assert.Equal("unplaced", CrewOf(h, "p1").LocationId);
        }

        [Fact]
        public void HostLocationValidatorDecidesCrewLocations()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1, loc => loc == "only-here");
            h.Feed(1, h.CrewFrame(2, "control-room"));
            Assert.Equal((int)SessionErrorCode.InvalidCrewState, Assert.Single(h.Drain()).Code);
            h.Feed(1, h.CrewFrame(3, "only-here"));
            Assert.Empty(h.Drain());
            Assert.Equal("only-here", CrewOf(h, "p1").LocationId);
        }

        [Fact]
        public void CrewUpdatesUseTheirOwnBucketOfFortyBurst()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Now = 1.0;
            for (int i = 0; i < 40; i++)
            {
                h.Feed(1, h.CrewFrame(2 + i, "control-room"));
                Assert.Empty(h.Drain());
            }
            h.Feed(1, h.CrewFrame(42, "control-room"));
            OutFrame f = Assert.Single(h.Drain());
            Assert.Equal((int)SessionErrorCode.RateLimited, f.Code);
            Assert.Equal("reject", f.Type);
        }

        [Fact]
        public void IntentBucketAndCrewBucketAreIndependent()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Now = 1.0;
            long seq = 2;
            for (int i = 0; i < 21; i++)
            {
                h.Feed(1, h.IntentFrame(seq++, "RodBankTarget"));
                h.Drain();
            }
            h.Feed(1, h.CrewFrame(seq++, "control-room"));
            Assert.Empty(h.Drain());
        }

        [Fact]
        public void DuplicateCrewUpdateIsDroppedAndEntryIsNotRewritten()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Feed(1, h.CrewFrame(5, "control-room"));
            h.Feed(1, h.CrewFrame(5, "turbine-hall"));
            Assert.Empty(h.Drain());
            Assert.Equal("control-room", CrewOf(h, "p1").LocationId);
        }

        [Fact]
        public void CrewEntryPersistsAcrossReconnect()
        {
            JoinInfo p1;
            var h = JoinedHost(out p1);
            h.Feed(1, h.CrewFrame(2, "control-room", "seated", "4.25"));
            h.Host.OnDisconnected(1, h.Now);
            h.Tick(20.0);
            h.Open(2);
            h.Feed(2, h.Hello(3, null, "Ann", token: p1.Token));
            OutFrame welcome = h.Drain().First();
            WorldSnapshotDecodeResult w = WorldSnapshot.Decode(welcome.Env.Body.Get("world"), 1);
            CrewMember m = w.World.Crew.Single(c => c.PeerId == "p1");
            Assert.Equal("control-room", m.LocationId);
            Assert.Equal(CrewPose.Seated, m.Pose);
            Assert.Equal(4.25, m.X);
        }
    }
}
