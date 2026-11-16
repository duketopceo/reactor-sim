using ReactorSim;
using Xunit;

public class ReactorSoundMixTests
{
    [Fact]
    public void TurbineSteamSoundTracksDeliveredSteamRatherThanThrottle()
    {
        var s = new PlantSnapshot { SteamDemandFrac = 1, TurbineSteamKW = Physics.RatedThermalKW * .5 };
        Assert.Equal(.5f, ReactorSoundMix.From(s).TurbineSteam);
        s.TurbineTripped = true;
        s.TurbineSteamKW = 0;
        Assert.Equal(0, ReactorSoundMix.From(s).TurbineSteam);
        s.TurbineSteamKW = double.NaN;
        Assert.Equal(0, ReactorSoundMix.From(s).TurbineSteam);
    }

    [Fact]
    public void ReactsToPlantStateAndStopsReliefWhenClosed()
    {
        var s = new PlantSnapshot { Power = 0, PrimaryFlowFrac = 0, CoolingWaterFrac = 0, VacuumPumpFrac = 0 };
        var idle = ReactorSoundMix.From(s);
        s.Power = 1; s.PrimaryFlowFrac = 1; s.ReliefOpen = true; s.GeneratorSynced = true; s.GridOutputFrac = 0.8;
        var running = ReactorSoundMix.From(s);
        Assert.True(running.Core > idle.Core && running.Water > idle.Water && running.DetectorRate > idle.DetectorRate);
        Assert.True(running.PrimaryRelief > 0 && running.Generator > 0);
        s.ReliefOpen = false; s.GeneratorSynced = false;
        Assert.Equal(0, ReactorSoundMix.From(s).PrimaryRelief);
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

    // AE1: only the secondary relief lifts -> only the secondary source sounds; and the reverse.
    [Fact]
    public void ReliefSourcesAreIndependent()
    {
        var s = new PlantSnapshot { SecondaryReliefOpen = true };
        var secondary = ReactorSoundMix.From(s);
        Assert.True(secondary.SecondaryRelief > 0);
        Assert.Equal(0, secondary.PrimaryRelief);

        s = new PlantSnapshot { ReliefOpen = true };
        var primary = ReactorSoundMix.From(s);
        Assert.True(primary.PrimaryRelief > 0);
        Assert.Equal(0, primary.SecondaryRelief);

        s.SecondaryReliefOpen = true;
        var both = ReactorSoundMix.From(s);
        Assert.True(both.PrimaryRelief > 0 && both.SecondaryRelief > 0);
        var none = ReactorSoundMix.From(new PlantSnapshot());
        Assert.Equal(0, none.PrimaryRelief);
        Assert.Equal(0, none.SecondaryRelief);
    }

    // AE2: turbine tripped with bypass steam flowing -> no turbine steam, bypass steam active.
    [Fact]
    public void BypassSteamSoundsWhileTrippedTurbineIsSilent()
    {
        var s = new PlantSnapshot { TurbineTripped = true, TurbineSteamKW = 0, DumpSteamKW = Physics.RatedThermalKW * .3 };
        var mix = ReactorSoundMix.From(s);
        Assert.Equal(0, mix.TurbineSteam);
        Assert.True(mix.DumpSteam > 0);
        Assert.Equal(0, ReactorSoundMix.From(new PlantSnapshot { DumpSteamKW = 0 }).DumpSteam);
        Assert.InRange(ReactorSoundMix.From(new PlantSnapshot { DumpSteamKW = Physics.RatedThermalKW * 50 }).DumpSteam, 0, 1);
    }

    [Fact]
    public void NanAndNegativeDumpSteamGiveZero()
    {
        Assert.Equal(0, ReactorSoundMix.From(new PlantSnapshot { DumpSteamKW = double.NaN }).DumpSteam);
        Assert.Equal(0, ReactorSoundMix.From(new PlantSnapshot { DumpSteamKW = -5 }).DumpSteam);
    }
}
