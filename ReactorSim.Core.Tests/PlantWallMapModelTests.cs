using ReactorSim;
using Xunit;

public class PlantWallMapModelTests
{
    [Fact]
    public void PumpAndPressurizerReportTheirOwnConditions()
    {
        var s = new PlantSnapshot { PrimaryFlowFrac = 1, PrimaryPressureBar = 90, PrimarySatMarginC = 30 };
        Assert.Equal(PlantWallMapModel.Status.Normal, PlantWallMapModel.Component("PrimaryPump", s));
        Assert.Equal(PlantWallMapModel.Status.Warning, PlantWallMapModel.Component("Pressurizer", s));
        s.PrimaryFlowFrac = 0;
        s.PrimaryPressureBar = 150;
        Assert.Equal(PlantWallMapModel.Status.Offline, PlantWallMapModel.Component("PrimaryPump", s));
        Assert.Equal(PlantWallMapModel.Status.Normal, PlantWallMapModel.Component("Pressurizer", s));
        s.ReliefOpen = true;
        Assert.Equal(PlantWallMapModel.Status.Warning, PlantWallMapModel.Component("Pressurizer", s));
        Assert.Equal(PlantWallMapModel.Status.Unavailable, PlantWallMapModel.Component("Safety", s));
    }

    [Fact]
    public void DetailedReadoutsDistinguishActualFromCommandAndHandleMissingData()
    {
        var s = new PlantSnapshot { FeedwaterDeliveredFrac = .2, FeedwaterFlowFrac = .9,
            BankInsertion = new[] { .1, .2, .3, .4 }, FuelTempC = double.NaN };
        string feed = PlantWallMapModel.ComponentReadout("FeedPump", s);
        Assert.Contains("DELIVERED 20.0 % rated", feed);
        Assert.Contains("PUMP 90.0 % command", feed);
        string reactor = PlantWallMapModel.ComponentReadout("Reactor", s);
        Assert.Contains("FUEL N/M", reactor);
        Assert.Contains("RODS A 40 % B 30 % C 20 % D 10 %", reactor);
        Assert.Equal("N/M", PlantWallMapModel.ComponentReadout("Reactor", null));
        Assert.Equal("N/M", PlantWallMapModel.ComponentReadout("Unknown", s));
    }

    [Fact]
    public void RoutesUseOnlyTheirOwnModeledActivity()
    {
        var s = new PlantSnapshot { PrimaryFlowFrac = .4, FeedwaterFlowFrac = .9, FeedwaterDeliveredFrac = .2, CoolingWaterFrac = .7, GridOutputFrac = 1.2, TurbineSteamKW = Physics.RatedThermalKW * .6, DumpSteamKW = Physics.RatedThermalKW * .25, TurbineRpm = Physics.TurbineRatedRpm * .8 };
        Assert.Equal(.4, PlantWallMapModel.RouteActivity("PrimaryHot", s));
        Assert.Equal(.2, PlantWallMapModel.RouteActivity("Feedwater", s));
        Assert.Equal(.7, PlantWallMapModel.RouteActivity("CoolingReturn", s));
        Assert.Equal(1, PlantWallMapModel.RouteActivity("Grid", s));
        Assert.Equal(.6, PlantWallMapModel.RouteActivity("Steam", s), 6);
        Assert.Equal(.6, PlantWallMapModel.RouteActivity("TurbineExhaust", s), 6);
        Assert.Equal(.25, PlantWallMapModel.RouteActivity("SteamDump", s), 6);
        Assert.Equal(.85, PlantWallMapModel.RouteActivity("SteamHeader", s), 6);
        Assert.Equal(.8, PlantWallMapModel.RouteActivity("Shaft", s), 6);
        Assert.True(double.IsNaN(PlantWallMapModel.RouteActivity("Surge", s)));
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
