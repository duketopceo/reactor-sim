using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ReactorSim.Session;
using Xunit;

namespace ReactorSim.Tests
{
    public enum LinkDirection
    {
        ToHost,
        ToClient
    }

    // One frame about to cross the link. Index counts frames in its direction from 0, so a test can name "the next frame".
    public sealed class LinkFrame
    {
        public LinkDirection Direction;
        public long Index;
        public string Line;
        public double Now;

        public string Type
        {
            get
            {
                SessionEnvelopeResult r = SessionEnvelope.Decode(Line);
                return r.Ok ? r.Envelope.T : null;
            }
        }
    }

    public sealed class LinkDelivery
    {
        public double Delay;
        public byte[] Bytes;
    }

    // Per-frame fault functions: a frame maps to zero or more deliveries. The transport appends the newline.
    public static class Faults
    {
        private static byte[] Wire(string line)
        {
            return Encoding.UTF8.GetBytes(line + "\n");
        }

        public static List<LinkDelivery> Pass(LinkFrame f)
        {
            return new List<LinkDelivery> { new LinkDelivery { Delay = 0, Bytes = Wire(f.Line) } };
        }

        public static List<LinkDelivery> Drop(LinkFrame f)
        {
            return new List<LinkDelivery>();
        }

        public static Func<LinkFrame, List<LinkDelivery>> Delay(double seconds)
        {
            return f => new List<LinkDelivery> { new LinkDelivery { Delay = seconds, Bytes = Wire(f.Line) } };
        }

        public static List<LinkDelivery> Duplicate(LinkFrame f)
        {
            return new List<LinkDelivery>
            {
                new LinkDelivery { Delay = 0, Bytes = Wire(f.Line) },
                new LinkDelivery { Delay = 0, Bytes = Wire(f.Line) }
            };
        }

        // Keeps only the first keepBytes bytes of the frame, then the newline: a cut line, not a cut stream.
        public static Func<LinkFrame, List<LinkDelivery>> Truncate(int keepBytes)
        {
            return f =>
            {
                byte[] full = Encoding.UTF8.GetBytes(f.Line);
                var cut = new byte[Math.Min(keepBytes, full.Length) + 1];
                Array.Copy(full, cut, cut.Length - 1);
                cut[cut.Length - 1] = (byte)'\n';
                return new List<LinkDelivery> { new LinkDelivery { Delay = 0, Bytes = cut } };
            };
        }
    }

    // Test-only in-memory transport joining one SessionHost and one SessionClient (each with its own Simulation) on a
    // shared fake clock. Time moves in 0.05 s steps; the host Simulation ticks every 0.25 s. Single-threaded, no wall clock.
    public sealed class LoopbackHarness
    {
        private sealed class Pending
        {
            public double At;
            public long Order;
            public bool ToHost;
            public int Conn;
            // Null means a close notification for the receiving side.
            public byte[] Bytes;
        }

        public readonly Simulation HostSim;
        public readonly Simulation ClientSim = new Simulation();
        public readonly SessionHost Host;
        public readonly SessionClient Client;
        public readonly List<string> Trace = new List<string>();
        public readonly List<SessionEvent> HostEvents = new List<SessionEvent>();
        public readonly List<ClientEvent> ClientEvents = new List<ClientEvent>();

        public double Now;
        public bool TickSims = true;
        public bool LinkUp = true;
        public bool HostGone;
        public bool HostFrozen;
        public Func<LinkFrame, List<LinkDelivery>> ToHostFault = Faults.Pass;
        public Func<LinkFrame, List<LinkDelivery>> ToClientFault = Faults.Pass;
        public long ToHostCount { get { return _toHostIndex; } }

        private readonly List<Pending> _queue = new List<Pending>();
        private int _step;
        private int _conn = -1;
        private int _nextConn = 1;
        private long _order;
        private long _toHostIndex;
        private long _toClientIndex;

        public LoopbackHarness(Simulation hostSim = null, string name = "Ann")
        {
            HostSim = hostSim ?? new Simulation();
            Host = new SessionHost(HostSim, new FixedEntropy(12345));
            Client = new SessionClient(ClientSim, name, Host.JoinCode);
        }

        public static Simulation ColdStartedHost()
        {
            var sim = new Simulation();
            sim.EnqueueIntent(PlantIntent.SetDilute(2.5));
            sim.EnqueueIntent(PlantIntent.SetPrimaryFlow(1.0));
            sim.EnqueueIntent(PlantIntent.SetRodBank("D", 0.0));
            sim.EnqueueIntent(PlantIntent.SetRodBank("C", 0.0));
            sim.EnqueueIntent(PlantIntent.SetRodBank("B", 0.0));
            sim.EnqueueIntent(PlantIntent.SetRodBank("A", 0.4));
            sim.EnqueueIntent(PlantIntent.SetSteamDemand(0.2));
            int ticks = (int)Math.Round(60.0 / sim.TickDt);
            for (int i = 0; i < ticks; i++) sim.Tick();
            return sim;
        }

        // Asks the client to join and lets the whole handshake settle at the current time.
        public void JoinNow()
        {
            Assert.True(Client.Connect(Now));
            Settle();
        }

        public void RunFor(double seconds)
        {
            int steps = (int)Math.Round(seconds * 20.0);
            for (int i = 0; i < steps; i++) Step();
        }

        // Returns true when the condition held within the time limit.
        public bool RunUntil(Func<bool> condition, double limitSeconds)
        {
            int steps = (int)Math.Round(limitSeconds * 20.0);
            for (int i = 0; i < steps; i++)
            {
                if (condition()) return true;
                Step();
            }
            return condition();
        }

        public void Step()
        {
            _step++;
            Now = _step / 20.0;
            Settle();
            if (!HostGone && !HostFrozen)
            {
                if (TickSims && _step % 5 == 0)
                    HostSim.Tick();
                Host.Tick(Now);
            }
            Client.Tick(Now);
            Settle();
        }

        // Moves frames and notifications until nothing more is due at the current time.
        public void Settle()
        {
            for (int guard = 0; guard < 1000; guard++)
            {
                bool moved = Pump();
                moved |= DeliverDue();
                if (!moved) break;
            }
            HostEvents.AddRange(Host.DrainEvents());
            ClientEvents.AddRange(Client.DrainEvents());
        }

        // The link dies. With notify, both ends see the connection close at once; without, they only see silence.
        public void LinkDown(bool notify)
        {
            LinkUp = false;
            _queue.Clear();
            if (notify)
            {
                int old = _conn;
                _conn = -1;
                if (old >= 0 && !HostGone && !HostFrozen) Host.OnDisconnected(old, Now);
                Client.OnDisconnected(Now);
                Trace.Add(Stamp("link down"));
            }
            else
            {
                Trace.Add(Stamp("link silent"));
            }
            Settle();
        }

        public void RestoreLink()
        {
            LinkUp = true;
            Trace.Add(Stamp("link up"));
        }

        // The host process dies: its connection resets and nothing answers a new one.
        public void KillHost()
        {
            HostGone = true;
            _queue.Clear();
            int old = _conn;
            _conn = -1;
            Trace.Add(Stamp("host gone"));
            if (old >= 0) Client.OnDisconnected(Now);
            Settle();
        }

        private bool Pump()
        {
            bool moved = false;
            if (!HostGone && !HostFrozen)
            {
                foreach (OutboxEntry o in Host.DrainOutbox())
                {
                    moved = true;
                    PumpHostEntry(o);
                }
            }
            foreach (ClientOutput o in Client.DrainOutbox())
            {
                moved = true;
                PumpClientOutput(o);
            }
            return moved;
        }

        private void PumpHostEntry(OutboxEntry o)
        {
            if (o.ConnectionId != _conn)
                return;
            double last = Now;
            if (o.Frame != null)
            {
                var frame = new LinkFrame { Direction = LinkDirection.ToClient, Index = _toClientIndex++, Line = o.Frame, Now = Now };
                Trace.Add(Stamp("H>C " + o.Frame));
                foreach (LinkDelivery d in ToClientFault(frame))
                {
                    if (!LinkUp) continue;
                    double at = Now + d.Delay;
                    if (at > last) last = at;
                    _queue.Add(new Pending { At = at, Order = _order++, ToHost = false, Conn = o.ConnectionId, Bytes = d.Bytes });
                }
            }
            if ((o.CloseAfter || o.Frame == null) && LinkUp)
                _queue.Add(new Pending { At = last, Order = _order++, ToHost = false, Conn = o.ConnectionId, Bytes = null });
        }

        private void PumpClientOutput(ClientOutput o)
        {
            switch (o.Kind)
            {
                case ClientOutputKind.Connect:
                    if (_conn >= 0 && !HostGone && !HostFrozen)
                        Host.OnDisconnected(_conn, Now);
                    _conn = -1;
                    if (LinkUp && !HostGone)
                    {
                        _conn = _nextConn++;
                        Trace.Add(Stamp("connect " + _conn.ToString(CultureInfo.InvariantCulture)));
                        if (!HostFrozen) Host.OnConnected(_conn, Now);
                        Client.OnConnected(Now);
                    }
                    else
                    {
                        Trace.Add(Stamp("connect refused"));
                        Client.OnDisconnected(Now);
                    }
                    return;
                case ClientOutputKind.Close:
                    if (_conn >= 0 && !HostGone && !HostFrozen)
                        Host.OnDisconnected(_conn, Now);
                    _conn = -1;
                    Trace.Add(Stamp("client close"));
                    return;
                default:
                    PumpClientFrame(o);
                    return;
            }
        }

        private void PumpClientFrame(ClientOutput o)
        {
            if (_conn < 0)
                return;
            var frame = new LinkFrame { Direction = LinkDirection.ToHost, Index = _toHostIndex++, Line = o.Frame, Now = Now };
            Trace.Add(Stamp("C>H " + o.Frame));
            double last = Now;
            foreach (LinkDelivery d in ToHostFault(frame))
            {
                if (!LinkUp) continue;
                double at = Now + d.Delay;
                if (at > last) last = at;
                _queue.Add(new Pending { At = at, Order = _order++, ToHost = true, Conn = _conn, Bytes = d.Bytes });
            }
            if (o.CloseAfter && LinkUp)
                _queue.Add(new Pending { At = last, Order = _order++, ToHost = true, Conn = _conn, Bytes = null });
        }

        private bool DeliverDue()
        {
            bool moved = false;
            while (true)
            {
                Pending next = null;
                for (int i = 0; i < _queue.Count; i++)
                {
                    Pending p = _queue[i];
                    if (p.At > Now + 1e-9) continue;
                    if (next == null || p.At < next.At || (p.At == next.At && p.Order < next.Order))
                        next = p;
                }
                if (next == null) break;
                _queue.Remove(next);
                moved = true;
                if (next.Conn != _conn)
                    continue;
                Deliver(next);
            }
            return moved;
        }

        private void Deliver(Pending p)
        {
            if (p.ToHost)
            {
                if (HostGone || HostFrozen) return;
                if (p.Bytes == null)
                {
                    _conn = -1;
                    Host.OnDisconnected(p.Conn, Now);
                    return;
                }
                Trace.Add(Stamp("deliver C>H " + Encoding.UTF8.GetString(p.Bytes).TrimEnd('\n')));
                Host.OnBytes(p.Conn, p.Bytes, 0, p.Bytes.Length, Now);
                return;
            }
            if (p.Bytes == null)
            {
                _conn = -1;
                Trace.Add(Stamp("deliver close H>C"));
                Client.OnDisconnected(Now);
                return;
            }
            Trace.Add(Stamp("deliver H>C " + Encoding.UTF8.GetString(p.Bytes).TrimEnd('\n')));
            Client.OnBytes(p.Bytes, 0, p.Bytes.Length, Now);
        }

        private string Stamp(string text)
        {
            return Now.ToString("F2", CultureInfo.InvariantCulture) + " " + text;
        }

        public int DeliveredToClient(string type)
        {
            string needle = "deliver H>C ";
            string marker = "\"t\":\"" + type + "\"";
            return Trace.Count(t => t.Contains(needle) && t.Contains(marker));
        }
    }

    public class SessionLoopbackTests
    {
        private static string BaselineJson(Simulation host)
        {
            var baseline = new Simulation();
            baseline.RestoreSnapshot(PlantSnapshot.FromJson(host.CaptureSnapshot().ToJson()));
            return baseline.CaptureSnapshot().ToJson();
        }

        private static LoopbackHarness Joined(Simulation hostSim = null)
        {
            var h = new LoopbackHarness(hostSim);
            h.JoinNow();
            Assert.Equal(ClientState.Active, h.Client.State);
            return h;
        }

        [Fact]
        public void LateJoinAfterColdStartGivesTheClientTheHostPlant()
        {
            Simulation hostSim = LoopbackHarness.ColdStartedHost();
            Assert.True(hostSim.SimSeconds >= 60.0);
            var h = new LoopbackHarness(hostSim);
            h.JoinNow();

            Assert.Equal(ClientState.Active, h.Client.State);
            Assert.Equal("p1", h.Client.PeerId);
            // Raw ToJson equality across a restore is not available: Plant.RestoreState recomputes derived values, so a
            // restored plant differs from its source in a few trailing digits. The oracle is the same solo path
            // WorldSnapshotTests uses: ToJson, FromJson, RestoreSnapshot on a fresh Simulation.
            Assert.Equal(BaselineJson(hostSim), h.ClientSim.CaptureSnapshot().ToJson());
            Assert.Equal(hostSim.SessionTick, h.ClientSim.SessionTick);
            Assert.Equal(hostSim.Clock.Paused, h.ClientSim.Clock.Paused);
            Assert.Equal(hostSim.Clock.Scale, h.ClientSim.Clock.Scale);
            CrewMember crew = Assert.Single(h.Client.Crew);
            Assert.Equal("p1", crew.PeerId);
            Assert.Equal("Ann", crew.DisplayName);
        }

        [Fact]
        public void ClientScramTripsTheHostAndTheNextWorldShowsIt()
        {
            var h = Joined(LoopbackHarness.ColdStartedHost());
            h.RunFor(1.0);
            Assert.False(h.HostSim.CaptureSnapshot().Scrammed);
            Assert.False(h.ClientSim.CaptureSnapshot().Scrammed);

            long seq = h.Client.SendIntent(PlantIntent.TriggerScram("host"));
            Assert.True(seq > 0);
            h.Settle();
            Assert.False(h.HostSim.CaptureSnapshot().Scrammed);
            Assert.True(h.RunUntil(() => h.ClientSim.CaptureSnapshot().Scrammed, 3.0));

            Assert.True(h.HostSim.CaptureSnapshot().Scrammed);
            SessionErrorCode code;
            Assert.True(h.Client.TryGetIntentResult(seq, out code));
            Assert.Equal(SessionErrorCode.None, code);
            IntentLogEntry applied = h.HostSim.GetIntentLogSnapshot().Single(e => e.Intent.Kind == IntentKind.Scram);
            Assert.Equal("p1", applied.Intent.Issuer);
        }

        [Fact]
        public void DelayWithReorderNeverAppliesOlderState()
        {
            var h = Joined(LoopbackHarness.ColdStartedHost());
            h.ToClientFault = f => Faults.Delay(f.Index % 2 == 0 ? 0.3 : 0.0)(f);

            long lastSeq = h.Client.LastSnapshotSeq;
            double lastSim = h.ClientSim.SimSeconds;
            for (int i = 0; i < 400; i++)
            {
                h.Step();
                Assert.True(h.Client.LastSnapshotSeq >= lastSeq);
                Assert.True(h.ClientSim.SimSeconds >= lastSim);
                lastSeq = h.Client.LastSnapshotSeq;
                lastSim = h.ClientSim.SimSeconds;
            }

            List<long> applied = h.ClientEvents.Where(e => e.Kind == ClientEventKind.WorldApplied).Select(e => e.SnapshotSeq).ToList();
            for (int i = 1; i < applied.Count; i++)
                Assert.True(applied[i] > applied[i - 1]);
            Assert.True(h.DeliveredToClient("world") > applied.Count - 1, "the fault must actually reorder frames");
            Assert.Equal(ClientState.Active, h.Client.State);
        }

        [Fact]
        public void ReorderedClientIntentsAreRefusedAsDuplicateNotAppliedOutOfOrder()
        {
            var h = Joined();
            bool first = true;
            h.ToHostFault = f =>
            {
                if (f.Type == "intent" && first)
                {
                    first = false;
                    return Faults.Delay(0.3)(f);
                }
                return Faults.Pass(f);
            };
            long a = h.Client.SendIntent(PlantIntent.SetRodBank("A", 0.3));
            long b = h.Client.SendIntent(PlantIntent.SetSteamDemand(0.4));
            h.RunFor(1.0);

            List<IntentLogEntry> log = h.HostSim.GetIntentLogSnapshot().Where(e => e.Intent.Issuer == "p1").ToList();
            Assert.Equal(IntentKind.SteamDemand, Assert.Single(log).Intent.Kind);
            SessionErrorCode codeA, codeB;
            Assert.True(h.Client.TryGetIntentResult(a, out codeA));
            Assert.True(h.Client.TryGetIntentResult(b, out codeB));
            Assert.Equal(SessionErrorCode.Duplicate, codeA);
            Assert.Equal(SessionErrorCode.None, codeB);
        }

        [Fact]
        public void DuplicatedClientFramesQueueEachIntentOnceAndEveryFinalResultIsAccepted()
        {
            var h = Joined();
            h.ToHostFault = Faults.Duplicate;
            long a = h.Client.SendIntent(PlantIntent.SetRodBank("A", 0.3));
            h.RunFor(0.5);
            long b = h.Client.SendIntent(PlantIntent.SetSteamDemand(0.4));
            h.RunFor(0.5);
            long c = h.Client.SendIntent(PlantIntent.SetPrimaryFlow(0.8));
            h.RunFor(2.0);

            List<IntentLogEntry> log = h.HostSim.GetIntentLogSnapshot().Where(e => e.Intent.Issuer == "p1").ToList();
            Assert.Equal(3, log.Count);
            Assert.Equal(new[] { IntentKind.RodBankTarget, IntentKind.SteamDemand, IntentKind.PrimaryFlow }, log.Select(e => e.Intent.Kind).ToArray());
            foreach (long seq in new[] { a, b, c })
            {
                SessionErrorCode code;
                Assert.True(h.Client.TryGetIntentResult(seq, out code));
                Assert.Equal(SessionErrorCode.None, code);
                Assert.Single(h.ClientEvents, e => e.Kind == ClientEventKind.IntentResult && e.Seq == seq);
            }
            Assert.Equal(3, h.HostEvents.Count(e => e.Kind == SessionEventKind.IntentRefused && e.Code == SessionErrorCode.Duplicate));
            Assert.Equal(ClientState.Active, h.Client.State);
        }

        [Fact]
        public void TruncatedClientFrameClosesThatConnectionWithProtocolViolationAndLeavesThePlantAlone()
        {
            var h = Joined(LoopbackHarness.ColdStartedHost());
            h.RunFor(1.0);
            string before = h.HostSim.CaptureSnapshot().ToJson();
            int ticksBefore = h.HostSim.TickCount;

            long bad = h.ToHostCount;
            h.ToHostFault = f => f.Index == bad ? Faults.Truncate(20)(f) : Faults.Pass(f);
            Assert.True(h.Client.SendIntent(PlantIntent.SetRodBank("A", 0.9)) > 0);
            h.Settle();

            Assert.Contains(h.HostEvents, e => e.Kind == SessionEventKind.ConnectionRefused && e.Code == SessionErrorCode.ProtocolViolation);
            Assert.Empty(h.HostSim.DrainPendingIntents());
            Assert.Equal(before, h.HostSim.CaptureSnapshot().ToJson());
            Assert.Equal(PeerState.Closed, h.Host.Peers[0].State);
            Assert.Equal(0, h.Host.ConnectionCount);

            h.RunFor(4.0);
            Assert.True(h.HostSim.TickCount > ticksBefore);
            Assert.DoesNotContain(h.HostSim.GetIntentLogSnapshot(), e => e.Intent.Issuer == "p1");
            // The host freed the slot, so the client's token resume is refused and the session ends Failed.
            Assert.Equal(ClientState.Failed, h.Client.State);
            Assert.Equal(ClientFailure.SessionLost, h.Client.FailureReason);
            Assert.Equal(SessionErrorCode.TokenExpired, h.Client.FailureCode);
        }

        [Fact]
        public void TenSecondLinkDropResumesAsP1WithTheSameCrewEntry()
        {
            var h = Joined();
            h.RunFor(2.0);
            Assert.True(h.Client.SendCrewUpdate(new CrewMember { LocationId = "panel.a", X = 1.5, Z = 2, YawDeg = 90, Pose = CrewPose.Seated }) > 0);
            h.RunFor(1.0);
            CrewMember before = Assert.Single(h.Host.GetCrew());
            Assert.Equal("panel.a", before.LocationId);

            double lostAt = h.Now;
            h.LinkDown(true);
            h.RunFor(10.0);
            Assert.Equal(ClientState.Reconnecting, h.Client.State);
            h.RestoreLink();
            Assert.True(h.RunUntil(() => h.Client.State == ClientState.Active, 30.0));

            Assert.True(h.Now - lostAt <= 31.0);
            Assert.Equal("p1", h.Client.PeerId);
            Assert.Contains(h.ClientEvents, e => e.Kind == ClientEventKind.Resumed && e.PeerId == "p1");
            Assert.Single(h.Host.Peers);
            CrewMember after = Assert.Single(h.Host.GetCrew());
            Assert.Equal("p1", after.PeerId);
            Assert.Equal(before.DisplayName, after.DisplayName);
            Assert.Equal(before.LocationId, after.LocationId);
            Assert.Equal(before.X, after.X);
            Assert.Equal(CrewPose.Seated, after.Pose);
            h.RunFor(1.0);
            Assert.Equal("panel.a", Assert.Single(h.Client.Crew).LocationId);
        }

        [Fact]
        public void SilentLinkDropResumesToo()
        {
            var h = Joined();
            h.RunFor(2.0);
            h.LinkDown(false);
            h.RunFor(10.0);
            h.RestoreLink();
            Assert.True(h.RunUntil(() => h.Client.State == ClientState.Active, 30.0));
            Assert.Equal("p1", h.Client.PeerId);
            Assert.Single(h.Host.Peers);
        }

        [Fact]
        public void FortySecondDropEndsFailedAndAFreshJoinGetsP2()
        {
            var h = Joined();
            h.RunFor(2.0);
            double lostAt = h.Now;
            h.LinkDown(true);
            h.RunFor(40.0);

            Assert.Equal(ClientState.Failed, h.Client.State);
            Assert.Equal(ClientFailure.HostLost, h.Client.FailureReason);
            Assert.Contains(h.HostEvents, e => e.Kind == SessionEventKind.PeerLeft && e.PeerId == "p1" && e.Code == SessionErrorCode.TokenExpired);

            h.RestoreLink();
            h.Client.AcknowledgeFailure();
            Assert.Equal(ClientState.Idle, h.Client.State);
            h.JoinNow();
            Assert.Equal(ClientState.Active, h.Client.State);
            Assert.Equal("p2", h.Client.PeerId);
            CrewMember only = Assert.Single(h.Host.GetCrew());
            Assert.Equal("p2", only.PeerId);
            Assert.True(h.Now - lostAt >= 40.0);
        }

        [Fact]
        public void HostProcessLossDrivesTheClientToFailedWithinTheBackoffBound()
        {
            var h = Joined();
            h.RunFor(2.0);
            double lostAt = h.Now;
            h.KillHost();
            Assert.True(h.RunUntil(() => h.Client.State == ClientState.Failed, 60.0));

            double elapsed = h.Now - lostAt;
            double bound = SessionProtocol.ReconnectBackoffSeconds.Sum() + SessionProtocol.ReconnectBackoffSeconds.Last();
            Assert.True(elapsed <= bound + 0.1, "failed after " + elapsed.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(ClientFailure.HostLost, h.Client.FailureReason);
        }

        [Fact]
        public void FrozenHostWithOpenConnectionAlsoFailsWithinSilencePlusBackoffBound()
        {
            var h = Joined();
            h.RunFor(2.0);
            double lostAt = h.Now;
            h.HostFrozen = true;
            Assert.True(h.RunUntil(() => h.Client.State == ClientState.Failed, 60.0));

            double elapsed = h.Now - lostAt;
            double bound = SessionProtocol.StaleAfterSilenceSeconds
                + SessionProtocol.ReconnectBackoffSeconds.Sum() + SessionProtocol.ReconnectBackoffSeconds.Last();
            Assert.True(elapsed <= bound + 0.1, "failed after " + elapsed.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(ClientFailure.HostLost, h.Client.FailureReason);
        }

        [Fact]
        public void SoloSimulationIsUnchangedBySessionCode_R9()
        {
            Simulation a = LoopbackHarness.ColdStartedHost();
            Simulation b = LoopbackHarness.ColdStartedHost();
            for (int i = 0; i < 200; i++) { a.Tick(); b.Tick(); }
            Assert.Equal(a.CaptureSnapshot().ToJson(), b.CaptureSnapshot().ToJson());

            // A hosted plant with a joined client that sends no intents evolves exactly like a plant with no session.
            var h = Joined(LoopbackHarness.ColdStartedHost());
            h.TickSims = false;
            for (int i = 0; i < 200; i++)
            {
                h.HostSim.Tick();
                h.RunFor(0.25);
            }
            Assert.Equal(a.CaptureSnapshot().ToJson(), h.HostSim.CaptureSnapshot().ToJson());
            Assert.Equal(a.SessionTick, h.HostSim.SessionTick);
        }

        private static string RunMixedScenario(out string finalHostJson)
        {
            var h = new LoopbackHarness(LoopbackHarness.ColdStartedHost());
            h.JoinNow();
            h.ToClientFault = f => Faults.Delay(f.Index % 3 == 0 ? 0.3 : 0.0)(f);
            h.ToHostFault = f => f.Index % 4 == 1 ? Faults.Duplicate(f) : Faults.Pass(f);
            h.RunFor(1.0);
            h.Client.SendIntent(PlantIntent.SetRodBank("A", 0.3));
            h.RunFor(0.5);
            h.Client.SendCrewUpdate(new CrewMember { LocationId = "panel.b", X = 2, Pose = CrewPose.Standing });
            h.Client.SendIntent(PlantIntent.SetSteamDemand(0.5));
            h.RunFor(2.0);
            h.LinkDown(true);
            h.RunFor(6.0);
            h.RestoreLink();
            h.RunUntil(() => h.Client.State == ClientState.Active, 30.0);
            h.RunFor(2.0);
            finalHostJson = h.HostSim.CaptureSnapshot().ToJson() + "|" + h.ClientSim.CaptureSnapshot().ToJson();
            return string.Join("\n", h.Trace);
        }

        [Fact]
        public void TheSameScenarioRunTwiceGivesIdenticalFrameBytesAndPlants()
        {
            string jsonA, jsonB;
            string traceA = RunMixedScenario(out jsonA);
            string traceB = RunMixedScenario(out jsonB);
            Assert.True(traceA.Length > 1000);
            Assert.Equal(traceA, traceB);
            Assert.Equal(jsonA, jsonB);
        }
    }
}
