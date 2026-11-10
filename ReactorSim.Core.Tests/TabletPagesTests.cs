using System.Linq;
using Xunit;
using ReactorSim;
using ReactorSim.Controls;
using ReactorSim.Instruments;
using ReactorSim.Tablet;

namespace ReactorSim.Core.Tests
{
    // U14: operator tablet page models (ReactorSim.Unity/Assets/Scripts/UI/TabletPages.cs, linked in).
    public class TabletPagesTests
    {
        private static PinRow Row(string id, string selector = "PrimaryPressureBar")
        {
            return new PinRow { RowId = id, Label = id, Selector = selector };
        }

        // ---- pins ----

        [Fact]
        public void Pin_AddsOnce()
        {
            var b = new PinBoard();
            Assert.True(b.Pin(Row("a")));
            Assert.False(b.Pin(Row("a")));
            Assert.Equal(1, b.Count);
        }

        [Fact]
        public void Pin_NinthEvictsOldest()
        {
            var b = new PinBoard();
            for (int i = 0; i < PinBoard.Cap; i++) Assert.True(b.Pin(Row("r" + i)));
            Assert.Equal(8, b.Count);
            Assert.True(b.Pin(Row("r8")));
            Assert.Equal(8, b.Count);
            Assert.Equal("r0", b.LastEvicted);
            Assert.False(b.Contains("r0"));
            Assert.True(b.Contains("r8"));
            Assert.Equal("r1", b.Rows[0].RowId);
        }

        [Fact]
        public void Unpin_Removes_AndToggleFlips()
        {
            var b = new PinBoard();
            b.Pin(Row("a"));
            b.Pin(Row("b"));
            Assert.True(b.Unpin("a"));
            Assert.False(b.Contains("a"));
            Assert.False(b.Unpin("a"));
            Assert.False(b.Toggle(Row("b")));   // was pinned: now unpinned
            Assert.True(b.Toggle(Row("b")));    // pinned again
            Assert.Equal(1, b.Count);
        }

        [Fact]
        public void PinRow_ReadsLiveValueWithUnits()
        {
            var s = new PlantSnapshot { PrimaryPressureBar = 155.2 };
            var r = Row("primary_pressure");
            Assert.Equal(155.2, r.Read(s), 6);
            Assert.Equal("155 bar", r.Format(s));
            var bank = new PinRow { RowId = "x", Selector = "BankInsertion", Index = 1 };
            Assert.True(double.IsNaN(bank.Read(new PlantSnapshot())) || !double.IsInfinity(bank.Read(new PlantSnapshot())));
        }

        // ---- alarms ----

        private static PlantSnapshot Snap(double simSeconds)
        {
            var s = new PlantSnapshot { SimSeconds = simSeconds };
            // Healthy baseline so only the alarms a test raises are active.
            s.PrimaryPressureBar = 155.0;
            s.PrimarySatMarginC = 30.0;
            s.PrimaryFlowFrac = 1.0;
            s.PrimaryLevelFrac = 0.6;
            s.SecondaryLevelFrac = 0.5;
            s.HeaterPowerFrac = 0.5;
            s.CoolingWaterFrac = 1.0;
            s.HotwellInventoryFrac = 0.7;
            s.BoronPpm = 800.0;
            s.Ph = 7.5;
            s.PrimaryTempC = 300.0;
            s.FeedwaterFlowFrac = 1.0;
            s.GeneratorSynced = true;
            s.BankIntegrity = new[] { 1.0, 1.0, 1.0, 1.0 };
            return s;
        }

        [Fact]
        public void Alarms_OrderBySeverityThenFirstRaised()
        {
            var board = new AlarmBoard();
            var ctx = LampContext.Default;

            // t=10: a warning (pressure low) appears.
            var s = Snap(10);
            s.PrimaryPressureBar = 90.0;
            board.Update(s, ctx);
            Assert.Equal(new[] { "PRI_PRESS_LO" }, board.Active.Select(a => a.Name).ToArray());

            // t=20: another warning (level low) appears, then t=30 a critical (fuel temp) appears last.
            s = Snap(20);
            s.PrimaryPressureBar = 90.0;
            s.PrimaryLevelFrac = 0.2;
            board.Update(s, ctx);
            s = Snap(30);
            s.PrimaryPressureBar = 90.0;
            s.PrimaryLevelFrac = 0.2;
            s.FuelTempC = 1500.0;
            board.Update(s, ctx);

            var names = board.Active.Select(a => a.Name).ToArray();
            Assert.Equal(new[] { "FUEL_TEMP_HI", "PRI_PRESS_LO", "LEVEL_LO" }, names);
            Assert.Equal(AlarmLevel.Critical, board.Active[0].Severity);
            Assert.Equal(10.0, board.Active[1].RaisedAtSimSeconds);
            Assert.Equal(20.0, board.Active[2].RaisedAtSimSeconds);
        }

        [Fact]
        public void Alarms_ClearedThenRaisedAgainGetsNewTime_AndCarryRowId()
        {
            var board = new AlarmBoard();
            board.SetRowId("PRI_PRESS_LO", "alarm_pri_press_lo");
            var ctx = LampContext.Default;
            var s = Snap(5);
            s.PrimaryPressureBar = 90.0;
            board.Update(s, ctx);
            Assert.Equal("alarm_pri_press_lo", board.Active[0].RowId);
            board.Update(Snap(6), ctx);
            Assert.Empty(board.Active);
            s = Snap(50);
            s.PrimaryPressureBar = 90.0;
            board.Update(s, ctx);
            Assert.Equal(50.0, board.Active[0].RaisedAtSimSeconds);
        }

        [Fact]
        public void Alarms_StatusLampsAreNotAlarms()
        {
            Assert.False(AlarmBoard.IsAlarm("GEN_BREAKER_CLOSED"));
            Assert.False(AlarmBoard.IsAlarm("GEN_BREAKER_OPEN"));
            Assert.False(AlarmBoard.IsAlarm("Pause"));
            Assert.True(AlarmBoard.IsAlarm("SCRAM"));
            Assert.True(AlarmBoard.IsAlarm("PORV_OPEN"));
        }

        // ---- trends ----

        [Fact]
        public void Trends_OneSamplePerSimTime_AndNormalised()
        {
            var t = new TabletTrends(4);
            for (int i = 0; i < 6; i++)
            {
                var s = Snap(i);
                s.GridFrequencyHz = 50.0 + i;
                t.Push(s);
                t.Push(s); // same sim time: ignored
            }
            Assert.Equal(4, t.Count);
            Assert.Equal(55.0, t.Latest(TabletTrends.Series.GridFrequency));
            var n = t.Normalized(TabletTrends.Series.GridFrequency, 10);
            Assert.Equal(4, n.Length);
            Assert.Equal(0.0, n[0]);
            Assert.Equal(1.0, n[3]);
        }

        // ---- clock ----

        [Fact]
        public void ClockPace_BuildsSameIntentAsRoomPaceButton()
        {
            // The room's pace-up button, built the way ControlRoom3DBinder.PressButton builds it.
            var room = PivotNameParser.Parse("INT_BTN_SetTimeScale_Clock_2").Spec;
            var roomIntent = ControlIntentFactory.BuildButton(room, false, 10.0, "diegetic");
            var tablet = ClockActions.Build(ClockAction.Faster, false, 10.0);
            Assert.Equal(roomIntent.Kind, tablet.Kind);
            Assert.Equal(IntentKind.SetTimeScale, tablet.Kind);
            Assert.Equal(roomIntent.Value, tablet.Value);
            Assert.Equal(IntentClass.Player, tablet.Class);
        }

        // ---- phone and messages (U15) ----

        [Fact]
        public void Phone_PendingRequestShowsCountdownToWindow()
        {
            var d = new Dispatcher(1UL);
            d.Step(30.0, 0.0, false);
            d.Issue(40.0, 150.0, 750.0, "Grid control");
            d.Step(50.0, 0.0, false); // clock 80
            var page = new PhonePage();
            page.Refresh(d);
            Assert.True(page.HasRequest);
            Assert.Equal(RequestStatus.Pending, page.Status);
            Assert.Equal(40.0, page.TargetMw);
            Assert.Equal(70.0, page.SecondsToStart, 6);
            Assert.Equal(670.0, page.SecondsToEnd, 6);
            Assert.Equal(0.0, page.WindowElapsedFraction);
            Assert.Equal(0.0, page.DeliveredFraction);
            Assert.Equal("1:10", PhonePage.FormatCountdown(page.SecondsToStart));
        }

        [Fact]
        public void Phone_ActiveRequestShowsProgressFraction()
        {
            var d = new Dispatcher(1UL);
            d.Issue(40.0, 100.0, 700.0, "Grid control");
            d.Step(100.0, 0.0, true);            // clock 100: window opens
            d.Step(1.0, 40.0, true);             // now Active
            d.Step(149.0, 20.0, true);           // clock 250: 150 s elapsed
            var page = new PhonePage();
            page.Refresh(d);
            Assert.Equal(RequestStatus.Active, page.Status);
            Assert.Equal(0.0, page.SecondsToStart);
            Assert.Equal(450.0, page.SecondsToEnd, 6);
            Assert.Equal(150.0 / 600.0, page.WindowElapsedFraction, 6);
            double expectedAvg = d.Current.DeliveredMwSeconds / 150.0;
            Assert.True(expectedAvg > 20.0 && expectedAvg < 40.0);
            Assert.Equal(expectedAvg, page.AverageSoFarMw, 6);
            Assert.Equal(expectedAvg / 40.0, page.DeliveredFraction, 6);
        }

        [Fact]
        public void Phone_DeliveredFractionIsCappedAtOne()
        {
            var d = new Dispatcher(1UL);
            d.Issue(10.0, 0.0, 600.0, "x");
            d.Step(1.0, 0.0, true);
            d.Step(100.0, 50.0, true);
            var page = new PhonePage();
            page.Refresh(d);
            Assert.Equal(1.0, page.DeliveredFraction);
        }

        [Fact]
        public void Phone_HistoryIsNewestFirstWithScoreAndStreak()
        {
            var d = new Dispatcher(1UL);
            d.Issue(10.0, 0.0, 300.0, "a");
            d.Step(1.0, 12.0, true);
            d.Step(300.0, 12.0, true); // met
            d.Issue(10.0, d.Clock, d.Clock + 300.0, "b");
            d.Step(1.0, 0.0, true);
            d.Step(300.0, 0.0, true);  // missed
            var page = new PhonePage();
            page.Refresh(d);
            Assert.False(page.HasRequest);
            Assert.Equal(2, page.History.Count);
            Assert.Equal(RequestStatus.Missed, page.History[0].Status);
            Assert.Equal(RequestStatus.Met, page.History[1].Status);
            Assert.Equal(1, page.Met);
            Assert.Equal(1, page.Missed);
            Assert.Equal(0, page.Streak);
        }

        [Fact]
        public void Phone_ReportsEachNewRequestOnce()
        {
            var d = new Dispatcher(1UL);
            var page = new PhonePage();
            Assert.Null(page.Refresh(d));
            d.Issue(20.0, 100.0, 400.0, "Grid");
            var fresh = page.Refresh(d);
            Assert.NotNull(fresh);
            Assert.Equal(20.0, fresh.TargetMw);
            Assert.Null(page.Refresh(d));
        }

        private static void Fire(MessageEngine e, string id, double at)
        {
            e.Inbox.Add(new FiredMessage { Seq = e.NextSeq++, Id = id, Sender = "S" + id, Text = "T" + id, AtSeconds = at });
        }

        [Fact]
        public void Messages_NewestFirstAndUnreadDropsWhenMarkedRead()
        {
            var e = new MessageEngine();
            Fire(e, "a", 10.0);
            Fire(e, "b", 20.0);
            Fire(e, "c", 30.0);
            var page = new MessagesPage();
            page.Refresh(e);
            Assert.Equal(new[] { "c", "b", "a" }, page.Items.Select(m => m.Id).ToArray());
            Assert.Equal(3, page.Unread);
            page.MarkRead(page.Items[0].Seq);
            Assert.Equal(2, page.Unread);
            Assert.False(page.IsUnread(page.Items[0]));
            page.MarkAllRead();
            Assert.Equal(0, page.Unread);
        }

        [Fact]
        public void Messages_RefreshReportsOnlyNewArrivals()
        {
            var e = new MessageEngine();
            Fire(e, "a", 10.0);
            var page = new MessagesPage();
            Assert.Null(page.Refresh(e));   // already there at first look: unread but no toast
            Assert.Equal(1, page.Unread);
            Fire(e, "b", 20.0);
            var arrived = page.Refresh(e);
            Assert.NotNull(arrived);
            Assert.Equal("b", arrived.Id);
            Assert.Null(page.Refresh(e));
            Assert.Equal(2, page.Unread);
        }

        [Fact]
        public void Messages_InboxResetClearsReadMemory()
        {
            var e = new MessageEngine();
            Fire(e, "a", 10.0);
            var page = new MessagesPage();
            page.Refresh(e);
            page.MarkAllRead();
            e.ResetState();
            Fire(e, "a", 5.0); // seq restarts at 1
            var arrived = page.Refresh(e);
            Assert.NotNull(arrived);
            Assert.Equal(1, page.Unread);
        }

        [Fact]
        public void Messages_TimestampIsGameTime()
        {
            Assert.Equal("T+0:05", MessagesPage.FormatStamp(5.0));
            Assert.Equal("T+12:34", MessagesPage.FormatStamp(754.0));
            Assert.Equal("T+1:02:03", MessagesPage.FormatStamp(3723.0));
        }

        [Fact]
        public void ClockActions_MapToPlayerIntents()
        {
            Assert.Equal(IntentKind.PauseClock, ClockActions.Build(ClockAction.PauseResume, false, 1.0).Kind);
            Assert.Equal(IntentKind.ResumeClock, ClockActions.Build(ClockAction.PauseResume, true, 1.0).Kind);
            Assert.Equal(5.0, ClockActions.Build(ClockAction.Slower, false, 10.0).Value);
            Assert.Equal(IntentKind.SkipAhead, ClockActions.Build(ClockAction.SkipAhead, false, 1.0).Kind);
            foreach (ClockAction a in System.Enum.GetValues(typeof(ClockAction)))
                Assert.Equal(IntentClass.Player, ClockActions.Build(a, false, 1.0).Class);
        }
    }
}
