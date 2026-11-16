using System;
using System.Collections.Generic;
using System.Linq;
using ReactorSim;
using ReactorSim.Instruments;
using Xunit;

// Phase 1 U5 / KTD6 / R13: every presentation model reads the same Runner-generated snapshots and must agree per state:
// relief, trip, alarm, steam and bypass. The wall map (PlantWallMapModel), the sound mix (ReactorSoundMix), the annunciator
// lamps (LampPredicates), the alarm and horn priority (AudioPriority + AlarmTracker) and the hall steam visuals
// (HallSteamModel) are each asked about every tick of every scenario in ParityScenarios.
public class PresentationParityTests
{
    private static readonly Dictionary<string, ParityCase> Cache = new Dictionary<string, ParityCase>();

    private static ParityCase Case(string name)
    {
        lock (Cache)
        {
            ParityCase c;
            if (!Cache.TryGetValue(name, out c)) Cache[name] = c = ParityScenarios.Generate(name);
            return c;
        }
    }

    public static IEnumerable<object[]> AllScenarios()
    {
        foreach (string n in ParityScenarios.Names) yield return new object[] { n };
    }

    private static readonly LampContext Ctx = LampContext.Default;

    private static bool Lamp(string name, PlantSnapshot s) { return LampPredicates.Evaluate(name, s, Ctx); }

    private static string Where(string scenario, int frame, PlantSnapshot s)
    {
        return scenario + " frame " + frame + " (game min " + s.GameMinutes.ToString("F2") + ")";
    }

    private static bool AnyBopLamp(PlantSnapshot s)
    {
        return Lamp("TURB_OVERSPEED", s) || Lamp("TURB_UNDERSPEED", s) || Lamp("COND_LOW_VACUUM", s) ||
               Lamp("SG_LOW_LEVEL_TRIP", s) || Lamp("GEN_OUT_OF_SYNC", s);
    }

    // Each scenario is only meaningful if it reaches its named state; ParityScenarios.Verify holds that in the Runner suite,
    // and these facts hold it here too so a parity pass can never be vacuous.
    [Fact]
    public void ScenariosReachTheirNamedStates()
    {
        Func<string, Func<PlantSnapshot, bool>, bool> ever = (n, f) => Case(n).Frames.Any(f);
        Assert.True(Case(ParityScenarios.ColdStartup).Frames.Last().GeneratorSynced);
        Assert.False(ever(ParityScenarios.ColdStartup, s => s.Scrammed || s.BopTripMask != 0), "startup to grid must not trip");
        Assert.False(ever(ParityScenarios.SteadyLoad, s => s.Scrammed || s.BopTripMask != 0 || s.DumpSteamKW > 0));
        Assert.True(ever(ParityScenarios.FlowLoss, s => Lamp("RCP_LOW_FLOW", s)));
        Assert.True(ever(ParityScenarios.HighPressure, s => Lamp("PRI_PRESS_HI", s)));
        Assert.True(ever(ParityScenarios.HighPressure, s => s.ReliefOpen));
        Assert.True(ever(ParityScenarios.HighPressure, s => s.SecondaryReliefOpen));
        Assert.True(ever(ParityScenarios.Scram, s => s.Scrammed));
        Assert.True(ever(ParityScenarios.TurbineTripBypass, s => s.TurbineTripped && s.DumpSteamKW > 0));
        Assert.True(ever(ParityScenarios.FeedStarvation, s => Lamp("FEED_FAIL", s)));
        Assert.True(ever(ParityScenarios.BypassWhileTripped, s => s.TurbineTripped && s.Scrammed && s.DumpSteamKW > 0));
    }

    // Relief: the primary and the secondary valve each show on the lamp, the wall component, the hall and the sound, and
    // neither implies the other.
    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void ReliefAgreesOnEverySurface(string scenario)
    {
        var c = Case(scenario);
        for (int i = 0; i < c.Frames.Count; i++)
        {
            var s = c.Frames[i];
            string at = Where(scenario, i, s);
            var mix = ReactorSoundMix.From(s);
            var hall = HallSteamModel.From(s);

            Assert.True(s.ReliefOpen == Lamp("PORV_OPEN", s), "PORV lamp vs snapshot at " + at);
            Assert.True(s.ReliefOpen == hall.PrimaryRelief, "hall primary relief vs snapshot at " + at);
            Assert.True(s.ReliefOpen == mix.PrimaryRelief > 0, "primary relief sound vs snapshot at " + at);
            if (s.ReliefOpen) Assert.NotEqual(PlantWallMapModel.Status.Normal, PlantWallMapModel.Component("Pressurizer", s));

            Assert.True(s.SecondaryReliefOpen == Lamp("SG_RELIEF", s), "SG relief lamp vs snapshot at " + at);
            Assert.True(s.SecondaryReliefOpen == hall.SecondaryRelief, "hall secondary relief vs snapshot at " + at);
            Assert.True(s.SecondaryReliefOpen == mix.SecondaryRelief > 0, "secondary relief sound vs snapshot at " + at);
            if (s.SecondaryReliefOpen) Assert.NotEqual(PlantWallMapModel.Status.Normal, PlantWallMapModel.Component("SteamGenerator", s));
        }
    }

    // Trip: SCRAM and turbine trip read the same on the lamps and on the wall; a latched balance-of-plant trip lights a trip lamp.
    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void TripAgreesOnLampsAndWall(string scenario)
    {
        var c = Case(scenario);
        for (int i = 0; i < c.Frames.Count; i++)
        {
            var s = c.Frames[i];
            string at = Where(scenario, i, s);

            Assert.True(s.Scrammed == Lamp("SCRAM", s), "SCRAM lamp vs snapshot at " + at);
            if (s.Scrammed)
            {
                Assert.Equal(PlantWallMapModel.Status.Alarm, PlantWallMapModel.Component("Safety", s));
                Assert.Equal(PlantWallMapModel.Status.Alarm, PlantWallMapModel.Component("Reactor", s));
            }
            else
            {
                Assert.Equal(PlantWallMapModel.Status.Unavailable, PlantWallMapModel.Component("Safety", s));
            }

            Assert.True(s.TurbineTripped == Lamp("TURBINE_TRIPPED", s), "turbine trip lamp vs snapshot at " + at);
            if (s.TurbineTripped)
            {
                Assert.Equal(PlantWallMapModel.Status.Alarm, PlantWallMapModel.Turbine(s));
                Assert.Equal(PlantWallMapModel.Status.Alarm, PlantWallMapModel.Component("Turbine", s));
                Assert.Equal(PlantWallMapModel.Status.Alarm, PlantWallMapModel.Component("Generator", s));
            }

            Assert.True((s.BopTripMask != 0) == AnyBopLamp(s), "BOP trip lamps vs trip mask at " + at);
            if (s.TurbineTripped) Assert.NotEqual(0, s.BopTripMask);
            if (Lamp("COND_LOW_VACUUM", s)) Assert.Equal(PlantWallMapModel.Status.Alarm, PlantWallMapModel.Component("Condenser", s));
            if (Lamp("SG_LOW_LEVEL_TRIP", s)) Assert.Equal(PlantWallMapModel.Status.Alarm, PlantWallMapModel.Component("SteamGenerator", s));
            if (Lamp("PRI_PRESS_HI", s))
            {
                Assert.Equal(PlantWallMapModel.Status.Alarm, PlantWallMapModel.Primary(s));
                Assert.Equal(PlantWallMapModel.Status.Alarm, PlantWallMapModel.Component("Pressurizer", s));
            }
        }
    }

    // Steam: turbine steam cues (wall routes, sound) follow TurbineSteamKW, bypass cues (wall route, sound, hall) follow DumpSteamKW,
    // and a tripped turbine has no turbine-steam cue anywhere (R7).
    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void SteamAndBypassAgreeOnEverySurface(string scenario)
    {
        var c = Case(scenario);
        for (int i = 0; i < c.Frames.Count; i++)
        {
            var s = c.Frames[i];
            string at = Where(scenario, i, s);
            var mix = ReactorSoundMix.From(s);
            var hall = HallSteamModel.From(s);

            // Turbine steam.
            Assert.True((s.TurbineSteamKW > 0) == mix.TurbineSteam > 0, "turbine steam sound vs TurbineSteamKW at " + at);
            if (PlantWallMapModel.RouteDirection("Steam", s) == 1) Assert.True(mix.TurbineSteam > 0, "wall shows turbine steam but the mix is silent at " + at);
            if (mix.TurbineSteam == 0)
            {
                Assert.Equal(0, PlantWallMapModel.RouteDirection("Steam", s));
                Assert.Equal(0, PlantWallMapModel.RouteDirection("TurbineExhaust", s));
            }
            if (s.TurbineTripped)
            {
                Assert.Equal(0f, mix.TurbineSteam);
                Assert.Equal(0, PlantWallMapModel.RouteDirection("Steam", s));
                Assert.Equal(0, PlantWallMapModel.RouteDirection("TurbineExhaust", s));
                Assert.Equal(0.0, s.TurbineSteamKW);
            }

            // Bypass (dump) steam.
            Assert.True((s.DumpSteamKW > 0) == mix.DumpSteam > 0, "bypass sound vs DumpSteamKW at " + at);
            Assert.True((s.DumpSteamKW > 0) == hall.Bypass > 0, "hall bypass vs DumpSteamKW at " + at);
            if (PlantWallMapModel.RouteDirection("SteamDump", s) == 1)
            {
                Assert.True(mix.DumpSteam > 0 && hall.Bypass > 0, "wall shows bypass but sound or hall is silent at " + at);
            }
            if (s.DumpSteamKW <= 0) Assert.Equal(0, PlantWallMapModel.RouteDirection("SteamDump", s));

            // The header carries whatever either path carries.
            if (s.TurbineSteamKW > 0 || s.DumpSteamKW > 0)
                Assert.True(PlantWallMapModel.RouteActivity("SteamHeader", s) > 0, "steam header idle while steam flows at " + at);
        }
    }

    // Turbine trip with bypass (acceptance): no turbine steam, bypass steam positive, on the sound mix and in the hall.
    [Theory]
    [InlineData(ParityScenarios.TurbineTripBypass)]
    [InlineData(ParityScenarios.BypassWhileTripped)]
    public void TurbineTripWithBypassHasNoTurbineSteamButHasBypassSteam(string scenario)
    {
        var c = Case(scenario);
        int checkedFrames = 0;
        for (int i = 0; i < c.Frames.Count; i++)
        {
            var s = c.Frames[i];
            if (!s.TurbineTripped || s.DumpSteamKW <= 0) continue;
            checkedFrames++;
            var mix = ReactorSoundMix.From(s);
            Assert.Equal(0f, mix.TurbineSteam);
            Assert.True(mix.DumpSteam > 0, Where(scenario, i, s));
            Assert.True(HallSteamModel.From(s).Bypass > 0);
            Assert.Equal(1, PlantWallMapModel.RouteDirection("SteamDump", s));
        }
        Assert.True(checkedFrames > 0, "scenario never had a tripped turbine with the dump open");
        var last = c.Frames.Last();
        Assert.True(last.TurbineTripped && last.DumpSteamKW > 0);
    }

    // Alarm and horn: the horn is driven by AudioPriority.HornAlarms through AlarmTracker. Its conditions must match the lamps,
    // it sounds from the first raised frame until acknowledged, an acknowledge silences everything raised so far, and the
    // highest-priority condition is the one a voice line would announce.
    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void HornConditionsMatchTheLamps(string scenario)
    {
        var c = Case(scenario);
        for (int i = 0; i < c.Frames.Count; i++)
        {
            var s = c.Frames[i];
            string at = Where(scenario, i, s);
            Assert.True(AudioPriority.HornConditionRaised("scram", s) == Lamp("SCRAM", s), "scram horn vs lamp at " + at);
            Assert.True(AudioPriority.HornConditionRaised("bop-trip", s) == AnyBopLamp(s), "bop-trip horn vs lamps at " + at);
            Assert.True(AudioPriority.HornConditionRaised("high-pressure", s) == Lamp("PRI_PRESS_HI", s), "high-pressure horn vs lamp at " + at);
            Assert.True(AudioPriority.HornConditionRaised("high-fuel-temp", s) == Lamp("FUEL_TEMP_HI", s), "high-fuel-temp horn vs lamp at " + at);
            foreach (string a in AudioPriority.HornAlarms)
                Assert.True(AudioPriority.Rank(a) >= 80, a + " has no alarm-class priority");
        }
    }

    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void HornSoundsUntilAcknowledgedThenIsSilentEverywhere(string scenario)
    {
        var c = Case(scenario);
        var horn = new AlarmTracker();
        var lamps = new AlarmTracker();
        var lampAlarms = LampPredicates.AlarmNames.ToList();
        PlantSnapshot cur = null;
        Func<string, bool> hornRaised = n => AudioPriority.HornConditionRaised(n, cur);
        Func<string, bool> lampRaised = n => LampPredicates.Evaluate(n, cur, Ctx);
        HashSet<string> raisedAtAck = null;
        bool newSinceAck = false, newLampSinceAck = false;
        for (int i = 0; i < c.Frames.Count; i++)
        {
            cur = c.Frames[i];
            string at = Where(scenario, i, cur);
            var raised = AudioPriority.HornAlarms.Where(n => AudioPriority.HornConditionRaised(n, cur)).ToList();
            horn.Update(AudioPriority.HornAlarms, hornRaised, cur.AlarmAckSeq);
            lamps.Update(lampAlarms, lampRaised, cur.AlarmAckSeq);

            if (i == c.AckFrame)
            {
                Assert.Equal(c.Frames[i - 1].AlarmAckSeq + 1, cur.AlarmAckSeq);
                raisedAtAck = new HashSet<string>(AudioPriority.HornAlarms.Where(n => AudioPriority.HornConditionRaised(n, c.Frames[i - 1])));
            }
            if (raisedAtAck != null && i > c.AckFrame && raised.Any(n => !raisedAtAck.Contains(n))) newSinceAck = true;
            if (i > c.AckFrame && c.AckFrame >= 0 && lamps.NewlyRaised > 0) newLampSinceAck = true;

            if (raised.Count == 0)
            {
                Assert.False(horn.AnyUnacked, "horn sounding with nothing raised at " + at);
            }
            else if (c.AckFrame < 0 || i < c.AckFrame)
            {
                Assert.True(horn.AnyUnacked, "horn silent while an unacknowledged condition is raised at " + at);
                Assert.Equal(raised.Max(n => AudioPriority.Rank(n)), AudioPriority.Rank(AudioPriority.Pick(raised)));
            }
            else if (!newSinceAck)
            {
                // Acknowledged: the horn and every annunciator tile are silent until a condition the operator has not seen raises.
                Assert.False(horn.AnyUnacked, "horn still sounding after the acknowledge at " + at);
                if (!newLampSinceAck) Assert.False(lamps.AnyUnacked, "an annunciator still flashes after the acknowledge at " + at);
                foreach (string a in raised) Assert.Equal(AlarmState.Acked, horn.StateOf(a));
            }
        }
        if (c.AckFrame >= 0) Assert.True(c.Frames[c.AckFrame].AlarmAckSeq > c.Frames[c.AckFrame - 1].AlarmAckSeq);
    }

    // SCRAM acceptance: the horn is active from the SCRAM until the acknowledge, then silent on every model; the SCRAM voice
    // line outranks every other alarm so it is the one announced.
    [Fact]
    public void ScramHornIsActiveUntilAcknowledgedThenSilent()
    {
        var c = Case(ParityScenarios.Scram);
        Assert.True(c.AckFrame > 0);
        var horn = new AlarmTracker();
        PlantSnapshot cur = null;
        Func<string, bool> raised = n => AudioPriority.HornConditionRaised(n, cur);
        int firstScram = -1;
        for (int i = 0; i < c.Frames.Count; i++)
        {
            cur = c.Frames[i];
            horn.Update(AudioPriority.HornAlarms, raised, cur.AlarmAckSeq);
            if (cur.Scrammed && firstScram < 0) firstScram = i;
            if (firstScram >= 0 && i < c.AckFrame) Assert.True(horn.AnyUnacked, "frame " + i);
            if (i >= c.AckFrame) Assert.False(horn.AnyUnacked, "frame " + i);
        }
        Assert.True(firstScram > 0 && firstScram < c.AckFrame);
        var after = c.Frames[c.AckFrame];
        Assert.True(after.Scrammed, "the plant stays scrammed after the acknowledge; only the horn is silenced");
        Assert.Equal(AlarmState.Acked, horn.StateOf("scram"));
        Assert.True(AudioPriority.Rank("scram") > AudioPriority.Rank("bop-trip"));
        Assert.Equal("scram", AudioPriority.Pick(new[] { "high-pressure", "scram", "bop-trip" }));
        // The sound mix has no horn term: nothing in it can keep sounding an acknowledged alarm.
        var mix = ReactorSoundMix.From(after);
        Assert.Equal(0f, mix.PrimaryRelief);
    }

    // A turbine trip during a SCRAM sounds the horn again after the SCRAM was acknowledged (per-condition tracking).
    [Fact]
    public void ANewTripAfterAcknowledgeSoundsTheHornAgain()
    {
        var horn = new AlarmTracker();
        var f0 = new PlantSnapshot { Scrammed = true, AlarmAckSeq = 0 };
        var f1 = new PlantSnapshot { Scrammed = true, AlarmAckSeq = 1 };
        var f2 = new PlantSnapshot { Scrammed = true, AlarmAckSeq = 1, BopTripMask = 1, TurbineTripped = true };
        PlantSnapshot cur = f0;
        Func<string, bool> raised = n => AudioPriority.HornConditionRaised(n, cur);
        horn.Update(AudioPriority.HornAlarms, raised, 0);
        Assert.True(horn.AnyUnacked);
        cur = f1; horn.Update(AudioPriority.HornAlarms, raised, 1);
        Assert.False(horn.AnyUnacked);
        cur = f2; horn.Update(AudioPriority.HornAlarms, raised, 1);
        Assert.True(horn.AnyUnacked);
        Assert.Equal(AlarmState.Acked, horn.StateOf("scram"));
        Assert.Equal(AlarmState.Unacked, horn.StateOf("bop-trip"));
    }

    // Per-scenario sanity on the state each one is named for, read back through the same models.
    [Fact]
    public void FlowLossShowsOnLampsWallAndSound()
    {
        var c = Case(ParityScenarios.FlowLoss);
        var first = c.Frames[0];
        var low = c.Frames.First(s => Lamp("RCP_LOW_FLOW", s));
        Assert.True(Lamp("FLOW_LO", low));
        Assert.NotEqual(PlantWallMapModel.Status.Normal, PlantWallMapModel.Component("PrimaryPump", low));
        Assert.True(ReactorSoundMix.From(low).Water < ReactorSoundMix.From(first).Water);
        Assert.Equal(0, PlantWallMapModel.RouteDirection("PrimaryHot", c.Frames.Last()));
    }

    [Fact]
    public void FeedStarvationShowsOnLampsAndWall()
    {
        var c = Case(ParityScenarios.FeedStarvation);
        var s = c.Frames.First(f => Lamp("FEED_FAIL", f));
        Assert.Equal(PlantWallMapModel.Status.Warning, PlantWallMapModel.Component("FeedPump", s));
        Assert.Equal(0, PlantWallMapModel.RouteDirection("Feedwater", c.Frames.Last()));
        Assert.True(c.Frames.Any(f => Lamp("SG_LVL_LO", f)), "steam generator level never alarmed low");
    }

    [Fact]
    public void ColdStartupToGridReachesGridWithMatchingSurfaces()
    {
        var c = Case(ParityScenarios.ColdStartup);
        var last = c.Frames.Last();
        Assert.True(last.GeneratorSynced);
        Assert.True(Lamp("GEN_BREAKER_CLOSED", last) && !Lamp("GEN_BREAKER_OPEN", last));
        Assert.True(ReactorSoundMix.From(last).Generator > 0);
        Assert.Equal(PlantWallMapModel.Status.Normal, PlantWallMapModel.Component("Generator", last));
        Assert.Equal(1, PlantWallMapModel.RouteDirection("Grid", last));
        // The whole climb is quiet: no relief, no bypass, no trip on any surface.
        Assert.DoesNotContain(c.Frames, s => s.ReliefOpen || s.SecondaryReliefOpen || s.DumpSteamKW > 0 || s.Scrammed || s.BopTripMask != 0);
    }
}
