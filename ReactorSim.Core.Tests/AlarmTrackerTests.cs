using System.Collections.Generic;
using ReactorSim.Instruments;
using Xunit;

public class AlarmTrackerTests
{
    private static readonly string[] Alarms = { "scram", "bop-trip", "high-pressure", "high-fuel-temp" };

    private static void Step(AlarmTracker t, int seq, params string[] raised)
    {
        var set = new HashSet<string>(raised);
        t.Update(Alarms, set.Contains, seq);
    }

    [Fact]
    public void RaisedAlarmsAreUnackedUntilAcknowledged()
    {
        var t = new AlarmTracker();
        Step(t, 0);
        Step(t, 0, "scram", "bop-trip");
        Assert.Equal(AlarmState.Unacked, t.StateOf("scram"));
        Assert.Equal(AlarmState.Unacked, t.StateOf("bop-trip"));
        Assert.True(t.AnyUnacked);
        Assert.Equal(2, t.NewlyRaised);
    }

    // AE3: acknowledgement silences the horn (no unacked alarm) for every raised alarm at once.
    [Fact]
    public void AcknowledgeClearsHornStateForAllRaisedAlarms()
    {
        var t = new AlarmTracker();
        Step(t, 0);
        Step(t, 0, "scram", "bop-trip");
        Step(t, 1, "scram", "bop-trip");
        Assert.False(t.AnyUnacked);
        Assert.Equal(AlarmState.Acked, t.StateOf("scram"));
        Assert.Equal(AlarmState.Acked, t.StateOf("bop-trip"));
    }

    [Fact]
    public void ANewAlarmAfterAcknowledgeSoundsTheHornAgain()
    {
        var t = new AlarmTracker();
        Step(t, 0);
        Step(t, 0, "scram");
        Step(t, 1, "scram");
        Assert.False(t.AnyUnacked);
        Step(t, 1, "scram", "bop-trip");
        Assert.True(t.AnyUnacked);
        Assert.Equal(AlarmState.Acked, t.StateOf("scram"));
        Assert.Equal(AlarmState.Unacked, t.StateOf("bop-trip"));
    }

    [Fact]
    public void ClearedAlarmReturnsToClearAndReRaisesUnacked()
    {
        var t = new AlarmTracker();
        Step(t, 0);
        Step(t, 0, "high-pressure");
        Step(t, 1, "high-pressure");
        Step(t, 1);
        Assert.Equal(AlarmState.Clear, t.StateOf("high-pressure"));
        Step(t, 1, "high-pressure");
        Assert.Equal(AlarmState.Unacked, t.StateOf("high-pressure"));
    }

    [Fact]
    public void FirstUpdateOnlyLearnsTheCountSoARestoredSaveIsNotAcknowledged()
    {
        var t = new AlarmTracker();
        Step(t, 7, "scram");
        Assert.Equal(AlarmState.Unacked, t.StateOf("scram"));
        Assert.True(t.AnyUnacked);
    }

    [Fact]
    public void UnknownAndNullAlarmNamesAreClear()
    {
        var t = new AlarmTracker();
        Assert.Equal(AlarmState.Clear, t.StateOf("nope"));
        Assert.Equal(AlarmState.Clear, t.StateOf(null));
    }
}
