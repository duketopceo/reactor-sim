using Xunit;

public class AudioCategoryTests
{
    [Fact]
    public void AbsentStoredValuesGiveFullDefaults()
    {
        var c = AudioCategories.FromStored(null, null, null, null, false);
        Assert.Equal(1f, c.Master);
        Assert.Equal(1f, c.PlantGain);
        Assert.Equal(1f, c.AlarmGain);
        Assert.Equal(1f, c.VoiceGain);
    }

    [Theory]
    [InlineData(-0.5f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(0.4f, 0.4f)]
    [InlineData(1f, 1f)]
    [InlineData(7f, 1f)]
    [InlineData(float.PositiveInfinity, 1f)]
    [InlineData(float.NegativeInfinity, 0f)]
    [InlineData(float.NaN, 1f)]
    public void StoredValuesClampToUnitRange(float stored, float expected)
    {
        var c = AudioCategories.FromStored(stored, stored, stored, stored, false);
        Assert.Equal(expected, c.Master);
        Assert.Equal(expected, c.PlantGain);
        Assert.Equal(expected, c.AlarmGain);
        Assert.Equal(expected, c.VoiceGain);
    }

    [Fact]
    public void AlarmsAtZeroSilenceAlarmsButNotPlantOrVoice()
    {
        var c = new AudioCategories(1f, 0.8f, 0f, 0.6f, false);
        Assert.Equal(0f, c.AlarmGain);
        Assert.Equal(0.8f, c.PlantGain);
        Assert.Equal(0.6f, c.VoiceGain);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void AlarmMuteOverridesTheAlarmSliderAtAnyValue(float slider)
    {
        var c = new AudioCategories(1f, 1f, slider, 1f, true);
        Assert.Equal(0f, c.AlarmGain);
        Assert.Equal(1f, c.PlantGain);
        Assert.Equal(1f, c.VoiceGain);
    }

    [Fact]
    public void ConstructorClampsToo()
    {
        var c = new AudioCategories(2f, -1f, 3f, float.NaN, false);
        Assert.Equal(1f, c.Master);
        Assert.Equal(0f, c.PlantGain);
        Assert.Equal(1f, c.AlarmGain);
        Assert.Equal(1f, c.VoiceGain);
    }
}
