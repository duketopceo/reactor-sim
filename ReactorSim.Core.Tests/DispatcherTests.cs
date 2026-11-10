using System.Collections.Generic;
using Xunit;

namespace ReactorSim.Tests
{
    public class DispatcherTests
    {
        private const double Dt = 6.0;

        // Runs the dispatcher from clock 0 with the given synthetic output until the clock reaches endSeconds.
        private static void Run(Dispatcher d, double endSeconds, double mw, bool synced = true)
        {
            while (d.Clock < endSeconds) d.Step(Dt, mw, synced);
        }

        private static Dispatcher WithWindow(double target, out LoadRequest req)
        {
            var d = new Dispatcher(7);
            req = d.Issue(target, 60.0, 660.0, "test");   // 10-minute window opening at 60 s
            return d;
        }

        [Fact]
        public void Request40MwOver10Minutes_MetWhenAverageAtLeastTarget()
        {
            LoadRequest r;
            var d = WithWindow(40.0, out r);
            Run(d, 700.0, 40.0);
            Assert.Equal(RequestStatus.Met, r.Status);
            Assert.True(r.AverageMw >= 40.0 - 1e-9);
            Assert.Equal(1, d.MetCount);
            Assert.Equal(1, d.Streak);
            Assert.Single(d.History);
        }

        [Fact]
        public void Request40MwOver10Minutes_MissedWhenAverageBelowTarget()
        {
            LoadRequest r;
            var d = WithWindow(40.0, out r);
            Run(d, 700.0, 30.0);
            Assert.Equal(RequestStatus.Missed, r.Status);
            Assert.Equal(1, d.MissedCount);
            Assert.Equal(0, d.Streak);
        }

        [Fact]
        public void ScoreUsesAverage_NotInstantaneous_AndOnlyTheWindowCounts()
        {
            LoadRequest r;
            var d = WithWindow(40.0, out r);
            Run(d, 60.0, 0.0);                  // before the window: output does not count
            Run(d, 360.0, 20.0);                // first half of the window
            Run(d, 660.0, 60.0);                // second half: averages 40
            Run(d, 700.0, 0.0);
            Assert.Equal(RequestStatus.Met, r.Status);
            Assert.InRange(r.AverageMw, 39.99, 40.01);
        }

        [Fact]
        public void WithinTolerance_CountsAsMet()
        {
            LoadRequest r;
            var d = WithWindow(40.0, out r);
            Run(d, 700.0, 40.0 * (1.0 - d.Config.ToleranceFrac * 0.5));
            Assert.Equal(RequestStatus.Met, r.Status);
        }

        [Fact]
        public void NoRequestsBeforeFirstSync()
        {
            var d = new Dispatcher(3);
            Run(d, 20000.0, 0.0, synced: false);
            Assert.Null(d.Current);
            Assert.Empty(d.History);
            Assert.False(d.EverSynced);
        }

        [Fact]
        public void FirstRequestComesAfterFirstSync_ThenAnotherAfterResolution()
        {
            var d = new Dispatcher(3);
            Run(d, 600.0, 0.0, synced: false);
            double syncAt = d.Clock;
            while (d.Current == null && d.Clock < syncAt + 2000.0) d.Step(Dt, 50.0, true);
            Assert.NotNull(d.Current);
            Assert.True(d.Clock >= syncAt + d.Config.FirstRequestDelaySeconds);
            int firstId = d.Current.Id;
            while (d.History.Count == 0 && d.Clock < syncAt + 5000.0) d.Step(Dt, 50.0, true);
            Assert.Single(d.History);
            while (d.Current == null && d.Clock < syncAt + 9000.0) d.Step(Dt, 50.0, true);
            Assert.NotNull(d.Current);
            Assert.NotEqual(firstId, d.Current.Id);
        }

        [Fact]
        public void SameSeed_SameRequestSequence_DifferentSeed_Differs()
        {
            string a = Sequence(42), b = Sequence(42), c = Sequence(43);
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
        }

        private static string Sequence(ulong seed)
        {
            var d = new Dispatcher(seed);
            var seen = new List<string>();
            int lastId = 0;
            for (int i = 0; i < 12000 && seen.Count < 6; i++)
            {
                d.Step(Dt, 50.0, true);
                if (d.Current != null && d.Current.Id != lastId)
                {
                    lastId = d.Current.Id;
                    seen.Add(d.Current.TargetMw + "@" + d.Current.WindowStart + "-" + d.Current.WindowEnd + ":" + d.Current.Caller);
                }
            }
            Assert.Equal(6, seen.Count);
            return string.Join("|", seen);
        }

        [Fact]
        public void RequestsStayInConfiguredRanges_AndHistoryIsBounded()
        {
            var d = new Dispatcher(11);
            var cfg = d.Config;
            int lastId = 0;
            for (int i = 0; i < 60000; i++)
            {
                d.Step(Dt, 50.0, true);
                if (d.Current != null && d.Current.Id != lastId)
                {
                    lastId = d.Current.Id;
                    var r = d.Current;
                    Assert.InRange(r.TargetMw, cfg.MinTargetMw, cfg.MaxTargetMw);
                    Assert.InRange(r.DurationSeconds, cfg.MinWindowSeconds - 1e-6, cfg.MaxWindowSeconds + 1e-6);
                }
            }
            Assert.True(lastId > cfg.HistoryLimit);
            Assert.True(d.History.Count <= cfg.HistoryLimit);
        }

        [Fact]
        public void TwoSims_SameInputs_IdenticalDispatcherState()
        {
            var a = Held(); var b = Held();
            for (int i = 0; i < 1500; i++) { a.Tick(); b.Tick(); }
            Assert.Equal(a.Plant.Dispatcher.StateString(), b.Plant.Dispatcher.StateString());
            Assert.True(a.Plant.Dispatcher.EverSynced);
            Assert.NotEqual(1, a.Plant.Dispatcher.NextId); // something was actually issued
        }

        private static Simulation Held()
        {
            var sim = new Simulation { DevMode = true };
            sim.EnqueueIntent(PlantIntent.LoadPreset("full-power", "test"));
            sim.Tick();
            return sim;
        }
    }
}
