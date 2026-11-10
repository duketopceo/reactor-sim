using System.Linq;
using Xunit;

namespace ReactorSim.Tests
{
    public class SimClockTests
    {
        private static Simulation At(double scale, bool dev = false)
        {
            return new Simulation(new Tuning { DefaultScale = scale }) { DevMode = dev };
        }

        private static Simulation FullPower(Tuning t = null)
        {
            var sim = new Simulation(t ?? new Tuning()) { DevMode = true };
            sim.EnqueueIntent(PlantIntent.LoadPreset("full-power", "t"));
            sim.Tick();                // applies the preset
            return sim;
        }

        private static bool Applied(Simulation sim, IntentKind k)
        {
            return sim.GetIntentLogSnapshot().Any(e => e.Intent.Kind == k && !e.Rejected);
        }

        [Fact]
        public void Advance_Scale4_YieldsMatchingTicks_RemainderCarried()
        {
            var sim = At(4.0);
            Assert.Equal(5.0, sim.TickDt);
            for (int n = 1; n <= 15; n++)
            {
                sim.Advance(1.0);                       // 4 sim-seconds per call
                Assert.Equal((int)(4.0 * n / 5.0), sim.TickCount);
            }
            Assert.Equal(12, sim.TickCount);            // 60 sim-seconds = 12 ticks
            Assert.InRange(sim.Clock.InterpolationFraction, 0.0, 1.0);
        }

        [Fact]
        public void Advance_ReturnsTicksApplied_AndExposesInterpolationFraction()
        {
            var sim = At(1.0);
            Assert.Equal(0, sim.Advance(2.5));
            Assert.Equal(0.5, sim.Clock.InterpolationFraction, 9);
            Assert.Equal(1, sim.Advance(2.5));
            Assert.Equal(0.0, sim.Clock.InterpolationFraction, 9);
        }

        [Fact]
        public void Pause_StopsTicks_QueuesPlayerIntents_ResumeAppliesThem_ScramAppliesWhilePaused()
        {
            var sim = At(6.0);                          // one tick per real second
            sim.Advance(2.0);
            sim.EnqueueIntent(PlantIntent.PauseClock("t"));
            sim.Advance(1.0);
            Assert.True(sim.Clock.Paused);
            int frozenAt = sim.TickCount;

            sim.Advance(100.0);
            Assert.Equal(frozenAt, sim.TickCount);

            sim.EnqueueIntent(PlantIntent.SetRodBank(0, 0.25, "t"));
            sim.Advance(10.0);
            Assert.Equal(frozenAt, sim.TickCount);
            Assert.False(Applied(sim, IntentKind.RodBankTarget));

            sim.EnqueueIntent(PlantIntent.TriggerScram("t"));
            sim.Advance(1.0);
            Assert.True(sim.Plant.Scrammed);            // SCRAM applies while paused
            Assert.Equal(frozenAt, sim.TickCount);
            Assert.False(Applied(sim, IntentKind.RodBankTarget));

            sim.EnqueueIntent(PlantIntent.ResumeClock("t"));
            sim.Advance(2.0);
            Assert.False(sim.Clock.Paused);
            Assert.True(sim.TickCount > frozenAt);
            Assert.True(Applied(sim, IntentKind.RodBankTarget));
        }

        [Fact]
        public void AutoSlow_EngagesOnShortPeriodNearCriticality_ReleasesAfterHold()
        {
            var t = new Tuning { DefaultScale = 8.0, AutoSlowScale = 2.0, AutoSlowHoldSeconds = 10.0, AutoSlowPeriodSeconds = 120.0 };
            var sim = new Simulation(t);
            var p = sim.Plant;
            p.Chemistry.DiluteRate = 1.5;
            p.Thermal.PrimaryFlowFrac = 1.0;
            p.Thermal.HeaterPowerFrac = 1.0;
            for (int i = 0; i < 3; i++) p.Banks[i].InsertionTarget = 0.0;
            for (int i = 0; i < 600 && !sim.Clock.AutoSlow; i++)
            {
                p.Banks[3].InsertionTarget = System.Math.Max(0.0, p.Banks[3].InsertionTarget - 0.01);
                sim.Advance(1.0);
            }
            Assert.True(sim.Clock.AutoSlow, "short period never engaged auto-slow; period=" + p.Core.PeriodSeconds);
            Assert.Contains("period", sim.Clock.AutoSlowReason);
            Assert.Equal(2.0, sim.Clock.EffectiveScale);

            // Clear the condition (rods in, period goes negative), then outlast the hold.
            for (int i = 0; i < 4; i++) p.Banks[i].InsertionTarget = 1.0;
            for (int i = 0; i < 400 && sim.Clock.AutoSlow; i++) sim.Advance(1.0);
            Assert.False(sim.Clock.AutoSlow);
            Assert.Equal(8.0, sim.Clock.EffectiveScale);
        }

        [Fact]
        public void AutoSlow_EngagesOnTurbineTrip()
        {
            var sim = FullPower(new Tuning { DefaultScale = 10.0, AutoSlowScale = 2.0 });
            Assert.False(sim.Clock.AutoSlow);
            sim.EnqueueIntent(PlantIntent.SetCoolingWater(0.0, "t"));
            for (int i = 0; i < 400 && sim.Plant.Bop.TripMask == 0; i++) sim.Advance(1.0);
            Assert.NotEqual(0, sim.Plant.Bop.TripMask);
            Assert.True(sim.Clock.AutoSlow);
            Assert.Contains("trip", sim.Clock.AutoSlowReason);
            Assert.Equal(2.0, sim.Clock.EffectiveScale);
        }

        [Fact]
        public void AutoSlow_EngagesOnScram()
        {
            var sim = FullPower(new Tuning { DefaultScale = 10.0, AutoSlowScale = 2.0 });
            sim.EnqueueIntent(PlantIntent.TriggerScram("t"));
            sim.Advance(1.0);
            Assert.True(sim.Clock.AutoSlow);
            Assert.Contains("scram", sim.Clock.AutoSlowReason);
        }

        [Fact]
        public void ClockIntents_AreLoggedAsPlayer_AndReplayToSameTickCount()
        {
            Simulation Run()
            {
                var sim = At(6.0);
                sim.Advance(3.0);
                sim.EnqueueIntent(PlantIntent.SetTimeScale(12.0, "t"));
                sim.Advance(4.0);
                sim.EnqueueIntent(PlantIntent.PauseClock("t"));
                sim.Advance(2.0);
                sim.EnqueueIntent(PlantIntent.ResumeClock("t"));
                sim.Advance(5.0);
                return sim;
            }
            var a = Run();
            var b = Run();
            Assert.Equal(a.TickCount, b.TickCount);
            Assert.Equal(a.SessionTick, b.SessionTick);
            var kinds = a.GetIntentLogSnapshot().Select(e => e.Intent.Kind).ToArray();
            Assert.Equal(new[] { IntentKind.SetTimeScale, IntentKind.PauseClock, IntentKind.ResumeClock }, kinds);
            Assert.All(a.GetIntentLogSnapshot(), e => Assert.Equal(IntentClass.Player, e.Class));
            Assert.Equal(kinds, b.GetIntentLogSnapshot().Select(e => e.Intent.Kind).ToArray());
        }

        [Fact]
        public void PlayerScale_IsClamped_DevCanExceed_DevIntentNeedsDevMode()
        {
            var t = new Tuning();
            var sim = At(4.0, dev: true);
            sim.EnqueueIntent(PlantIntent.SetTimeScale(t.MaxScale * 10, "t"));
            sim.Tick();
            Assert.Equal(t.MaxScale, sim.Clock.Scale);
            sim.EnqueueIntent(PlantIntent.SetTimeScale(0.0001, "t"));
            sim.Tick();
            Assert.Equal(t.MinScale, sim.Clock.Scale);

            sim.EnqueueIntent(PlantIntent.SetTimeScaleDev(t.MaxScale * 2, "t"));
            sim.Tick();
            Assert.Equal(t.MaxScale * 2, sim.Clock.Scale);
            Assert.True(sim.Clock.Scale > t.MaxScale);
            Assert.Equal(IntentClass.Dev, PlantIntent.SetTimeScaleDev(1.0).Class);

            var locked = At(4.0, dev: false);
            Assert.Throws<IntentRejectedException>(() => locked.EnqueueIntent(PlantIntent.SetTimeScaleDev(100.0, "t")));
        }

        [Fact]
        public void SkipAhead_RunsAtPlayerMax_StopsAtChosenSpan()
        {
            var t = new Tuning { DefaultScale = 4.0 };
            var sim = new Simulation(t);
            sim.EnqueueIntent(PlantIntent.SkipAhead(600.0, "t"));
            sim.Tick();
            Assert.True(sim.Clock.Skipping);
            Assert.Equal(t.MaxScale, sim.Clock.EffectiveScale);
            for (int i = 0; i < 100 && sim.Clock.Skipping; i++) sim.Advance(1.0);
            Assert.False(sim.Clock.Skipping);
            Assert.Contains("span", sim.Clock.SkipStopReason);
            Assert.InRange(sim.SimSeconds, 600.0, 600.0 + sim.TickDt + 1e-9);
            Assert.Equal(4.0, sim.Clock.EffectiveScale);
        }

        [Fact]
        public void SkipAhead_StopsWhenANewAlarmRaises()
        {
            var sim = FullPower();
            sim.EnqueueIntent(PlantIntent.SkipAhead(3600.0, "t"));
            sim.Tick();
            Assert.True(sim.Clock.Skipping);
            sim.Plant.Assistant.ActiveAlarms.Add(new PlantAlarm { Code = "TEST_ALARM", Severity = AlarmSeverity.Warning });
            sim.Clock.Observe(sim.Plant, sim.SimSeconds);
            Assert.False(sim.Clock.Skipping);
            Assert.Equal("alarm TEST_ALARM", sim.Clock.SkipStopReason);
        }

        [Fact]
        public void SkipAhead_IgnoresAdvisoryAlarms()
        {
            var sim = FullPower();
            sim.EnqueueIntent(PlantIntent.SkipAhead(3600.0, "t"));
            sim.Tick();
            sim.Plant.Assistant.ActiveAlarms.Add(new PlantAlarm { Code = "TEST_ADVISORY", Severity = AlarmSeverity.Advisory });
            sim.Clock.Observe(sim.Plant, sim.SimSeconds);
            Assert.True(sim.Clock.Skipping);
        }

        [Fact]
        public void LoadingAScrammedPreset_IsNotAScramEdge()
        {
            var sim = FullPower();
            sim.EnqueueIntent(PlantIntent.TriggerScram("t"));
            sim.Tick();
            var snap = sim.CaptureSnapshot();
            var fresh = FullPower();
            fresh.Tick();
            fresh.RestoreSnapshot(snap);
            fresh.Tick();
            Assert.False(fresh.Clock.AutoSlow, "restore produced a spurious edge: " + fresh.Clock.AutoSlowReason);
        }

        [Fact]
        public void SkipAhead_AfterScram_StopsWhenXenonPassesPeak()
        {
            var sim = FullPower();
            sim.EnqueueIntent(PlantIntent.TriggerScram("t"));
            sim.Tick();
            Assert.True(sim.Plant.Scrammed);
            sim.EnqueueIntent(PlantIntent.SkipAhead(24 * 3600.0, "t", untilXenonPeak: true));
            double start = sim.SimSeconds;
            double peak = sim.Plant.Core.Xenon135;
            sim.Tick();
            Assert.True(sim.Clock.Skipping);
            for (int i = 0; i < 20000 && sim.Clock.Skipping; i++)
            {
                sim.Advance(1.0);
                peak = System.Math.Max(peak, sim.Plant.Core.Xenon135);
            }
            Assert.False(sim.Clock.Skipping);
            Assert.Contains("xenon", sim.Clock.SkipStopReason);
            // The sim's xenon peaks a few game minutes after the SCRAM (compressed timescale): the skip must
            // end there, not at the 24 h span cap, with xenon just past the peak.
            Assert.True(sim.SimSeconds - start > 60.0, "stopped before xenon could rise: " + (sim.SimSeconds - start));
            Assert.True(sim.SimSeconds - start < 3600.0, "ran past the xenon peak toward the span cap: " + (sim.SimSeconds - start));
            Assert.True(sim.Plant.Core.Xenon135 > 0.95 * peak, "stopped far from the peak");
            // Past the peak means xenon keeps falling after the stop (sampling per Advance call can miss the
            // exact peak tick, so check the trend directly).
            double atStop = sim.Plant.Core.Xenon135;
            for (int i = 0; i < 24; i++) sim.Tick();
            Assert.True(sim.Plant.Core.Xenon135 < atStop, "xenon should be past its peak at the stop");
        }

        [Fact]
        public void SkipAhead_StopsOnBopTrip_BeforeSpan()
        {
            var sim = FullPower(new Tuning { DefaultScale = 4.0 });
            sim.EnqueueIntent(PlantIntent.SetCoolingWater(0.0, "t"));
            sim.EnqueueIntent(PlantIntent.SkipAhead(24 * 3600.0, "t"));
            sim.Tick();
            Assert.True(sim.Clock.Skipping);
            for (int i = 0; i < 1000 && sim.Clock.Skipping; i++) sim.Advance(1.0);
            Assert.False(sim.Clock.Skipping);
            Assert.Contains("trip", sim.Clock.SkipStopReason);
            Assert.NotEqual(0, sim.Plant.Bop.TripMask);
            Assert.True(sim.SimSeconds < 3600.0);
        }

        [Fact]
        public void InteractiveOneSecondTicks_QueuedPlayerIntentAppliesWithinOneRealSecondAtAutoSlowScale()
        {
            var t = new Tuning { DefaultScale = 3.0, AutoSlowScale = 3.0, InteractiveTickSeconds = 1.0 };
            var sim = new Simulation(t);
            sim.ApplyInteractiveTick();
            Assert.Equal(1.0, sim.TickDt);
            sim.Advance(0.5);
            sim.EnqueueIntent(PlantIntent.SetRodBank(0, 0.5, "t"));
            sim.Advance(1.0);
            Assert.True(Applied(sim, IntentKind.RodBankTarget));
        }

        [Fact]
        public void Frozen_AppliesNoTicksUntilReset()
        {
            var sim = At(6.0);
            sim.Advance(2.0);
            int n = sim.TickCount;
            sim.Clock.Freeze("reactor casualty");
            Assert.True(sim.Clock.Frozen);
            Assert.Equal("reactor casualty", sim.Clock.FrozenReason);
            Assert.Equal(0, sim.Advance(50.0));
            Assert.Equal(n, sim.TickCount);
            sim.Clock.Unfreeze();
            Assert.False(sim.Clock.Frozen);
            Assert.True(sim.Advance(2.0) > 0);
        }

        [Fact]
        public void TickBudget_CapsTicksPerAdvance()
        {
            var sim = new Simulation(new Tuning { DefaultScale = 60.0, MaxTicksPerAdvance = 5 });
            Assert.Equal(5, sim.Advance(1000.0));
            Assert.InRange(sim.Clock.InterpolationFraction, 0.0, 1.0);
        }
    }
}
