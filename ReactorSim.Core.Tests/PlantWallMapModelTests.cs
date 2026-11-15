using ReactorSim;
using Xunit;

public class PlantWallMapModelTests
{
    [Fact]
    public void SummaryUsesLiveReadingsAndMarksUnavailableData()
    {
        var s = new PlantSnapshot { Mode = "Power", Power = .42, FuelTempC = 456, PrimaryPressureBar = 150,
            PrimarySatMarginC = 30, PrimaryFlowFrac = .8, FeedwaterDeliveredFrac = .3, GeneratorKw = 1234 };
        string summary = PlantWallMapModel.Summary(s, ReactorSim.Instruments.LampContext.Default);
        Assert.Contains("POWER 42.0 %", summary);
        Assert.Contains("FUEL 456.0 C", summary);
        Assert.Contains("PRIMARY 150.0 bar", summary);
        Assert.Contains("FEED 30.0 % rated", summary);
        Assert.Contains("OUTPUT 1234 kW", summary);
        s.FuelTempC = double.NaN;
        Assert.Contains("FUEL N/M", PlantWallMapModel.Summary(s, ReactorSim.Instruments.LampContext.Default));
        Assert.Equal("PLANT DATA UNAVAILABLE", PlantWallMapModel.Summary(null, ReactorSim.Instruments.LampContext.Default));
    }

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

    [Fact]
    public void GroupedReadoutCoversEverySystemAndMarksUnsourcedFieldsUnavailable()
    {
        var s = new PlantSnapshot { Power = .5, PrimaryPressureBar = 155, TurbineRpm = 1800, TurbineTripped = true,
            SimSeconds = 3725, BankInsertion = new[] { .1, .2, .3, .4 }, CondenserPressureBar = double.NaN };
        string g = PlantWallMapModel.GroupedReadout(s);
        foreach (string key in new[] { "CORE 50.0 %", "PRIMARY 155.0 bar", "SG ", "TURBINE 1800 rpm TRIPPED", "COND N/M",
            "COOLING", "BORON", "CLOCK 01:02:05", "FIRST-OUT N/M", "PROCEDURE N/M", "RODS D/C/B/A 10/20/30/40 %" })
            Assert.Contains(key, g);
        Assert.Equal("PLANT DATA UNAVAILABLE", PlantWallMapModel.GroupedReadout(null));
        s.SimSeconds = double.NaN;
        Assert.Contains("CLOCK N/M", PlantWallMapModel.GroupedReadout(s));
    }

    [Theory]
    [InlineData("Reactor")] [InlineData("PrimaryPump")] [InlineData("Pressurizer")] [InlineData("SteamGenerator")]
    [InlineData("Turbine")] [InlineData("Condenser")] [InlineData("Cooling")] [InlineData("FeedPump")] [InlineData("Chemistry")]
    public void NaNSnapshotIsNeverShownAsHealthy(string name)
    {
        var nan = double.NaN;
        var s = new PlantSnapshot { Power = nan, FuelTempC = nan, PrimaryFlowFrac = nan, PrimaryPressureBar = nan,
            PrimarySatMarginC = nan, SecondaryPressureBar = nan, SecondaryLevelFrac = nan, TurbineRpm = nan,
            CondenserPressureBar = nan, HotwellInventoryFrac = nan, CoolingWaterFrac = nan, FeedwaterFlowFrac = nan,
            BoronPpm = nan, Ph = nan };
        Assert.Equal(PlantWallMapModel.Status.Unavailable, PlantWallMapModel.Component(name, s));
    }
    [Fact]
    public void RotorSpeedIsBoundedAndNeverNaN()
    {
        Assert.Equal(0, PlantWallMapModel.RotorSpeed(false, null));
        var s = new PlantSnapshot { PrimaryFlowFrac = .4, TurbineRpm = Physics.TurbineRatedRpm * .5 };
        Assert.Equal(.4, PlantWallMapModel.RotorSpeed(false, s), 9);
        Assert.Equal(.5, PlantWallMapModel.RotorSpeed(true, s), 9);
        s.PrimaryFlowFrac = 7; s.TurbineRpm = -50;
        Assert.Equal(1, PlantWallMapModel.RotorSpeed(false, s));
        Assert.Equal(0, PlantWallMapModel.RotorSpeed(true, s));
        s.PrimaryFlowFrac = double.NaN; s.TurbineRpm = double.PositiveInfinity;
        Assert.Equal(0, PlantWallMapModel.RotorSpeed(false, s));
        Assert.Equal(0, PlantWallMapModel.RotorSpeed(true, s));
    }

    static readonly string[] Components = { "Reactor", "Safety", "PrimaryPump", "Pressurizer", "SteamGenerator", "Turbine",
        "Generator", "Grid", "Condenser", "Cooling", "FeedPump", "Chemistry" };

    // Every reading valid, so any unavailable marker below comes from a missing source, not from bad data.
    static PlantSnapshot FullyPopulated() => new PlantSnapshot { Mode = "Power", Power = .8, FuelTempC = 600,
        TotalReactivityPcm = 0, PeriodSeconds = 900, PrimaryFlowFrac = .9, PrimaryTempC = 300, PrimaryLevelFrac = .5,
        PrimaryPressureBar = 155, PrimarySatMarginC = 20, HeaterPowerFrac = .3, SprayFrac = .1, SecondaryPressureBar = 60,
        SecondaryTempC = 270, SecondaryLevelFrac = .5, SteamDemandFrac = .8, DumpSteamKW = 500, TurbineRpm = 1800,
        TurbineSteamKW = 90000, GeneratorKw = 30000, GeneratorSynced = true, GridFrequencyHz = 60, GridOutputFrac = .8,
        CondenserPressureBar = .08, HotwellInventoryFrac = .6, VacuumPumpFrac = 1, CoolingWaterFrac = 1,
        FeedwaterDeliveredFrac = .8, FeedwaterFlowFrac = .9, FeedwaterAvailabilityFrac = 1, BoronPpm = 800, Ph = 7.2,
        SimSeconds = 3725 };

    [Fact]
    public void ReadingsWithoutAnAuthoritativeSourceStayUnavailableEvenWhenEverythingElseIsValid()
    {
        var s = FullyPopulated();
        Assert.Contains("SAFETY FLOW N/M", PlantWallMapModel.ComponentReadout("Safety", s));
        Assert.Contains("CHEMISTRY FLOW N/M", PlantWallMapModel.ComponentReadout("Chemistry", s));
        string grouped = PlantWallMapModel.GroupedReadout(s);
        Assert.Contains("FIRST-OUT N/M", grouped);
        Assert.Contains("PROCEDURE N/M", grouped);
        // The snapshot has one shared secondary state, so no per-generator or per-pump reading may appear.
        foreach (string name in Components)
        {
            string text = PlantWallMapModel.ComponentReadout(name, s);
            Assert.DoesNotMatch(@"\b(SG|RCP|PUMP)[ -]?[1-4A-D]\b", text);
        }
    }

    [Fact]
    public void DemandedRatesAreAlwaysLabeledAsCommands()
    {
        var s = FullyPopulated();
        Assert.Contains("THROTTLE 80.0 % command", PlantWallMapModel.ComponentReadout("SteamGenerator", s));
        Assert.Contains("VACUUM PUMP 100.0 % command", PlantWallMapModel.ComponentReadout("Condenser", s));
        string feed = PlantWallMapModel.ComponentReadout("FeedPump", s);
        Assert.Contains("PUMP 90.0 % command", feed);
        Assert.Contains("DELIVERED 80.0 % rated", feed);
    }

    [Fact]
    public void HeatRatesNeverCarryAMassFlowUnit()
    {
        var s = FullyPopulated();
        Assert.Contains("STEAM 90000 kW heat", PlantWallMapModel.ComponentReadout("Turbine", s));
        Assert.Contains("DUMP 500 kW heat", PlantWallMapModel.ComponentReadout("SteamGenerator", s));
        foreach (string name in Components)
        {
            string text = PlantWallMapModel.ComponentReadout(name, s);
            Assert.DoesNotContain("kg/s", text);
            Assert.DoesNotContain("t/h", text);
        }
        Assert.DoesNotContain("kg/s", PlantWallMapModel.GroupedReadout(s));
    }

    // U2 (R3): each component's readout carries its audited fields with units, and any NaN source reads N/M with no unit.
    static readonly (string Name, string[] Fields)[] ReadoutFields =
    {
        ("Reactor", new[] { "PWR 80.0 %", "FUEL 600.0 C", "RHO 0 pcm", "PERIOD 900.0 s", "RODS A " }),
        ("Safety", new[] { "SCRAM CLEAR", "SAFETY FLOW N/M" }),
        ("PrimaryPump", new[] { "FLOW 90.0 %", "TEMP 300.0 C", "LEVEL 50.0 %" }),
        ("Pressurizer", new[] { "PRESS 155.0 bar", "MARGIN 20.0 C", "HEAT 30.0 %", "SPRAY 10.0 %", "RELIEF CLOSED" }),
        ("SteamGenerator", new[] { "PRESS 60.0 bar", "TEMP 270.0 C", "LEVEL 50.0 %", "RELIEF CLOSED", "THROTTLE 80.0 % command", "DUMP 500 kW heat" }),
        ("Turbine", new[] { "SPEED 1800 rpm", "STEAM 90000 kW heat", "NOT TRIPPED" }),
        ("Generator", new[] { "OUTPUT 30000 kW", "SYNCED" }),
        ("Grid", new[] { "FREQ 60.00 Hz", "OUTPUT 80.0 % rated" }),
        ("Condenser", new[] { "PRESS 0.08 bar", "HOTWELL 60.0 %", "VACUUM PUMP 100.0 % command" }),
        ("Cooling", new[] { "COOLING 100.0 %" }),
        ("FeedPump", new[] { "DELIVERED 80.0 % rated", "PUMP 90.0 % command", "SUPPLY 100.0 %" }),
        ("Chemistry", new[] { "BORON 800 ppm", "PH 7.20", "CHEMISTRY FLOW N/M" }),
    };

    [Fact]
    public void EveryComponentReadoutReturnsItsAuditedFieldsWithUnits()
    {
        var s = FullyPopulated();
        s.BankInsertion = new[] { .1, .2, .3, .4 };
        Assert.Equal(Components.Length, ReadoutFields.Length);
        foreach (var (name, fields) in ReadoutFields)
        {
            string text = PlantWallMapModel.ComponentReadout(name, s);
            foreach (string field in fields) Assert.Contains(field, text);
        }
    }

    [Fact]
    public void ANaNSourceReadsNotMeasuredWithNoUnit()
    {
        var nan = double.NaN;
        var s = new PlantSnapshot { Power = nan, FuelTempC = nan, TotalReactivityPcm = nan, PeriodSeconds = nan, PrimaryFlowFrac = nan,
            PrimaryTempC = nan, PrimaryLevelFrac = nan, PrimaryPressureBar = nan, PrimarySatMarginC = nan, HeaterPowerFrac = nan,
            SprayFrac = nan, SecondaryPressureBar = nan, SecondaryTempC = nan, SecondaryLevelFrac = nan, SteamDemandFrac = nan,
            DumpSteamKW = nan, TurbineRpm = nan, TurbineSteamKW = nan, GeneratorKw = nan, GridFrequencyHz = nan, GridOutputFrac = nan,
            CondenserPressureBar = nan, HotwellInventoryFrac = nan, VacuumPumpFrac = nan, CoolingWaterFrac = nan,
            FeedwaterDeliveredFrac = nan, FeedwaterFlowFrac = nan, FeedwaterAvailabilityFrac = nan, BoronPpm = nan, Ph = nan,
            BankInsertion = new[] { nan, nan, nan, nan } };
        foreach (string name in Components)
        {
            string text = PlantWallMapModel.ComponentReadout(name, s);
            Assert.DoesNotMatch(@"NaN|Infinity", text);
            // N/M is bare: never followed by a unit.
            Assert.DoesNotMatch(@"N/M (%|bar|pcm|ppm|rpm|kW|Hz|heat|command|rated)", text);
        }
        Assert.Contains("PWR N/M", PlantWallMapModel.ComponentReadout("Reactor", s));
        Assert.Contains("SPEED N/M", PlantWallMapModel.ComponentReadout("Turbine", s));
    }

    // U2 (R4): a direction exists only where the model knows the flow sign, and never at zero activity.
    [Fact]
    public void ARouteWithZeroActivityHasNoDirection()
    {
        var s = new PlantSnapshot { PrimaryFlowFrac = 0, FeedwaterDeliveredFrac = 0, GridOutputFrac = 0 };
        Assert.Equal(0, PlantWallMapModel.RouteDirection("PrimaryHot", s));
        Assert.Equal(0, PlantWallMapModel.RouteDirection("Feedwater", s));
        Assert.Equal(0, PlantWallMapModel.RouteDirection("Grid", s));
        Assert.Equal(0, PlantWallMapModel.RouteDirection("Steam", s));
    }

    [Fact]
    public void ARouteWithKnownPositiveFlowHasForwardDirection()
    {
        var s = new PlantSnapshot { PrimaryFlowFrac = .4, FeedwaterDeliveredFrac = .2, GridOutputFrac = .5,
            TurbineSteamKW = Physics.RatedThermalKW * .6, DumpSteamKW = Physics.RatedThermalKW * .25 };
        foreach (string name in new[] { "PrimaryHot", "PrimaryCold", "Feedwater", "Grid", "Steam", "SteamHeader", "TurbineExhaust", "SteamDump" })
            Assert.Equal(1, PlantWallMapModel.RouteDirection(name, s));
    }

    [Fact]
    public void NoDirectionWhereTheModelDoesNotKnowTheFlowSignOrTheReadingIsMissing()
    {
        var s = new PlantSnapshot { PrimaryFlowFrac = 1, CoolingWaterFrac = 1, TurbineRpm = Physics.TurbineRatedRpm,
            FeedwaterDeliveredFrac = 1, GridOutputFrac = 1 };
        // Shaft is rotation, the cooling circuit's drawn order is not a modeled flow sign, the rest have no flow source.
        foreach (string name in new[] { "Shaft", "CoolingSupply", "CoolingReturn", "Surge", "Chemistry", "Safety", "Unknown" })
            Assert.Equal(0, PlantWallMapModel.RouteDirection(name, s));
        s.PrimaryFlowFrac = double.NaN;
        Assert.Equal(0, PlantWallMapModel.RouteDirection("PrimaryHot", s));
        Assert.Equal(0, PlantWallMapModel.RouteDirection("PrimaryHot", null));
    }

    [Fact]
    public void ForwardDirectionFollowsThePathOrderOfTheAuthoredAsset()
    {
        // The model's forward heading must match the polyline the Blender generator draws, point for point, so a change to the
        // asset cannot silently reverse an arrow.
        string py = System.IO.File.ReadAllText(TestRepo.Find("Blender", "scripts", "make_plant_overview.py"));
        int checkedRoutes = 0;
        foreach (string name in PlantWallMapModel.DirectedRoutes)
        {
            var m = System.Text.RegularExpressions.Regex.Match(py, "pipe\\(\"" + name + "\", \\[(.*?)\\], ");
            Assert.True(m.Success, "no pipe() call for " + name);
            var points = System.Text.RegularExpressions.Regex.Matches(m.Groups[1].Value, @"\((-?[\d.]+),\s*(-?[\d.]+)\)");
            var path = PlantWallMapModel.RoutePath(name);
            Assert.Equal(points.Count, path.Length);
            for (int i = 0; i < path.Length; i++)
            {
                Assert.Equal(double.Parse(points[i].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), path[i][0], 9);
                Assert.Equal(double.Parse(points[i].Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), path[i][1], 9);
            }
            checkedRoutes++;
        }
        Assert.Equal(8, checkedRoutes);
        Assert.Null(PlantWallMapModel.RoutePath("Shaft"));
    }
}
