using ReactorSim;
using Xunit;

public class PlantWallMapModelTests
{
    [Fact]
    public void RoutesUseOnlyTheirOwnModeledActivity()
    {
        var s = new PlantSnapshot { PrimaryFlowFrac = .4, FeedwaterFlowFrac = .2, CoolingWaterFrac = .7, GridOutputFrac = 1.2 };
        Assert.Equal(.4, PlantWallMapModel.RouteActivity("PrimaryHot", s));
        Assert.Equal(.2, PlantWallMapModel.RouteActivity("Feedwater", s));
        Assert.Equal(.7, PlantWallMapModel.RouteActivity("CoolingReturn", s));
        Assert.Equal(1, PlantWallMapModel.RouteActivity("Grid", s));
        Assert.True(double.IsNaN(PlantWallMapModel.RouteActivity("Steam", s)));
        s.PrimaryFlowFrac = double.NaN;
        Assert.True(double.IsNaN(PlantWallMapModel.RouteActivity("PrimaryCold", s)));
    }

    [Fact]
    public void RodsUseSnapshotBankOrderAndHandleMissingValues()
    {
        var s = new PlantSnapshot { BankInsertion = new[] { .1, .2, .3, .4 } };
        Assert.Equal(.4, PlantWallMapModel.BankInsertion("A", s));
        Assert.Equal(.1, PlantWallMapModel.BankInsertion("D", s));
        Assert.True(double.IsNaN(PlantWallMapModel.BankInsertion("E", s)));
        s.BankInsertion = null;
        Assert.True(double.IsNaN(PlantWallMapModel.BankInsertion("A", s)));
    }
    [Fact]
    public void MissingAndInvalidReadingsAreUnavailable()
    {
        Assert.Equal("N/M", PlantWallMapModel.Reading(double.NaN, "bar"));
        Assert.Equal(PlantWallMapModel.Status.Unavailable, PlantWallMapModel.Primary(null));
        Assert.Equal(PlantWallMapModel.Status.Unavailable, PlantWallMapModel.Primary(new PlantSnapshot { PrimarySatMarginC = double.NaN }));
    }

    [Fact]
    public void PrimaryUsesExistingAlarmLimits()
    {
        var s = new PlantSnapshot { PrimaryPressureBar = 150, PrimaryFlowFrac = 1, PrimarySatMarginC = 30 };
        Assert.Equal(PlantWallMapModel.Status.Normal, PlantWallMapModel.Primary(s));
        s.PrimaryPressureBar = 170;
        Assert.Equal(PlantWallMapModel.Status.Alarm, PlantWallMapModel.Primary(s));
        s.PrimaryPressureBar = 90;
        Assert.Equal(PlantWallMapModel.Status.Warning, PlantWallMapModel.Primary(s));
    }

    [Fact]
    public void TripAndUnavailableStatesAreNotShownAsHealthy()
    {
        var s = new PlantSnapshot { TurbineRpm = 3000, TurbineTripped = true };
        Assert.Equal(PlantWallMapModel.Status.Alarm, PlantWallMapModel.Component("Turbine", s));
        Assert.Equal(PlantWallMapModel.Status.Unavailable, PlantWallMapModel.Component("Unknown", s));
        s.Scrammed = true;
        Assert.Equal(PlantWallMapModel.Status.Alarm, PlantWallMapModel.Component("Safety", s));
        s.CondenserPressureBar = double.NaN;
        Assert.Equal(PlantWallMapModel.Status.Unavailable, PlantWallMapModel.Component("Condenser", s));
    }
}
