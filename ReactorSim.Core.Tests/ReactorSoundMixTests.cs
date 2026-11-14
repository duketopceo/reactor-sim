using ReactorSim;
using Xunit;

public class ReactorSoundMixTests
{
    [Fact]
    public void ReactsToPlantStateAndStopsReliefWhenClosed()
    {
        var s = new PlantSnapshot { Power = 0, PrimaryFlowFrac = 0, CoolingWaterFrac = 0, VacuumPumpFrac = 0 };
        var idle = ReactorSoundMix.From(s);
        s.Power = 1; s.PrimaryFlowFrac = 1; s.ReliefOpen = true; s.GeneratorSynced = true; s.GridOutputFrac = 0.8;
        var running = ReactorSoundMix.From(s);
        Assert.True(running.Core > idle.Core && running.Water > idle.Water && running.DetectorRate > idle.DetectorRate);
        Assert.True(running.Relief > 0 && running.Generator > 0);
        s.ReliefOpen = false; s.GeneratorSynced = false;
        Assert.Equal(0, ReactorSoundMix.From(s).Relief);
        Assert.Equal(0, ReactorSoundMix.From(s).Generator);
    }

    [Fact]
    public void MissingAndInvalidValuesStayFiniteAndBounded()
    {
        Assert.Equal(0, ReactorSoundMix.From(null).Core);
        var mix = ReactorSoundMix.From(new PlantSnapshot { Power = double.NaN, PrimaryFlowFrac = double.PositiveInfinity, CoolingWaterFrac = -1 });
        Assert.InRange(mix.Core, 0, 0.155f);
        Assert.InRange(mix.Water, 0, 0.175f);
        Assert.InRange(mix.DetectorRate, 0, 6);
    }
}
