using System.Collections.Generic;
using Xunit;

public class AudioPriorityTests
{
    [Fact]
    public void ScramOutranksEveryOtherHornAndVoiceKey()
    {
        string[] others = { "bop-trip", "high-pressure", "high-fuel-temp", "turbine_trip", "alarm_pressure_high",
            "alarm_temp_high", "alarm_power_high", "alarm_period", "scram_reset", "sync", "critical", "all_normal",
            "advise_xenon", "step_pumps", "startup_begin" };
        foreach (var k in others)
        {
            Assert.True(AudioPriority.Rank("scram") > AudioPriority.Rank(k), k);
            Assert.Equal("scram", AudioPriority.Pick(new List<string> { k, "scram" }));
        }
    }

    // AE3: two alarms and a voice line in one tick: the highest-priority voice line is the one picked.
    [Fact]
    public void SimultaneousLinesPickTheHighestRankedRegardlessOfOrder()
    {
        Assert.Equal("scram", AudioPriority.Pick(new List<string> { "alarm_temp_high", "scram", "step_pumps" }));
        Assert.Equal("scram", AudioPriority.Pick(new List<string> { "step_pumps", "alarm_temp_high", "scram" }));
        Assert.Equal("turbine_trip", AudioPriority.Pick(new List<string> { "alarm_power_high", "turbine_trip", "sync" }));
    }

    [Fact]
    public void AlarmsOutrankAdvisoriesAndProcedureLines()
    {
        Assert.True(AudioPriority.Rank("alarm_pressure_high") > AudioPriority.Rank("advise_slow"));
        Assert.True(AudioPriority.Rank("alarm_pressure_high") > AudioPriority.Rank("step_vacuum"));
        Assert.True(AudioPriority.Rank("turbine_trip") > AudioPriority.Rank("alarm_power_high"));
    }

    [Fact]
    public void TiesKeepTheFirstAndEmptyOrNullGivesNull()
    {
        Assert.Equal("step_pumps", AudioPriority.Pick(new List<string> { "step_pumps", "step_vacuum" }));
        Assert.Null(AudioPriority.Pick(new List<string>()));
        Assert.Null(AudioPriority.Pick(null));
        Assert.Equal("mystery", AudioPriority.Pick(new List<string> { null, "mystery" }));
        Assert.Equal(0, AudioPriority.Rank(null));
        Assert.Equal(0, AudioPriority.Rank("no-such-key"));
    }

    [Fact]
    public void OnlyAHigherRankedAlarmInterruptsTheLineSpeaking()
    {
        Assert.True(AudioPriority.Interrupts("scram", "step_pumps"));
        Assert.True(AudioPriority.Interrupts("scram", "alarm_temp_high"));
        Assert.False(AudioPriority.Interrupts("scram", "scram"));
        Assert.False(AudioPriority.Interrupts("alarm_temp_high", "scram"));
        Assert.False(AudioPriority.Interrupts("step_pumps", "advise_slow"));
        Assert.True(AudioPriority.Interrupts("scram", null));
    }
}
