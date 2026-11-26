using System;
using System.IO;
using System.Linq;
using ReactorSim;
using ReactorSim.Core.Tests;
using Xunit;

// Phase 2 U4 (CI slice): the turbine-hall equipment model reads only the snapshot and reuses the Phase 1 readout rules.
public class TurbineHallModelTests
{
    private static PlantSnapshot Snap(Action<PlantSnapshot> edit = null)
    {
        var s = new PlantSnapshot();
        if (edit != null) edit(s);
        return s;
    }

    [Fact]
    public void RotorSpeedEqualsTheWallRotorSpeedForEverySnapshot()
    {
        foreach (string name in ParityScenarios.Names)
        {
            var c = ParityScenarios.Generate(name);
            foreach (var s in c.Frames)
                Assert.Equal(PlantWallMapModel.RotorSpeed(true, s), TurbineHallModel.RotorSpeed(s));
        }
    }

    [Fact]
    public void NonFiniteOrMissingRpmStopsTheRotor()
    {
        Assert.Equal(0, TurbineHallModel.RotorSpeed(null));
        Assert.Equal(0, TurbineHallModel.RotorSpeed(Snap(s => s.TurbineRpm = double.NaN)));
        Assert.Equal(0, TurbineHallModel.RotorSpeed(Snap(s => s.TurbineRpm = double.PositiveInfinity)));
        Assert.Equal(0, TurbineHallModel.RotorSpeed(Snap(s => s.TurbineRpm = -50)));
        Assert.Equal(1, TurbineHallModel.RotorSpeed(Snap(s => s.TurbineRpm = Physics.TurbineRatedRpm * 2)));
    }

    [Fact]
    public void TrippedTurbineRotorFollowsTheSnapshotFrameByFrameWithNoAddedLag()
    {
        var c = ParityScenarios.Generate(ParityScenarios.TurbineTripBypass);
        int first = -1;
        for (int i = 0; i < c.Frames.Count; i++) if (c.Frames[i].TurbineTripped) { first = i; break; }
        Assert.True(first >= 0, "scenario never tripped the turbine");
        double prev = double.MaxValue;
        bool slowed = false;
        for (int i = first; i < c.Frames.Count; i++)
        {
            var s = c.Frames[i];
            double expected = Math.Max(0, Math.Min(1, s.TurbineRpm / Physics.TurbineRatedRpm));
            double speed = TurbineHallModel.RotorSpeed(s);
            Assert.Equal(expected, speed, 12);
            Assert.True(speed <= prev + 1e-9 || (s.TurbineRpm > 0 && !s.TurbineTripped), "rotor sped up while tripped, frame " + i);
            if (speed < prev - 1e-9 && prev != double.MaxValue) slowed = true;
            prev = speed;
            string plate = TurbineHallModel.Plate(TurbineHallModel.Turbine, s);
            Assert.Contains("TRIPPED", plate);
            Assert.Contains("SPEED " + PlantWallMapModel.Reading(s.TurbineRpm, "rpm", 0), plate);
        }
        Assert.True(slowed, "rotor never slowed after the trip");
    }

    [Fact]
    public void TrippedTurbinePlateShowsZeroKwHeatSteamOnceSteamStops()
    {
        var c = ParityScenarios.Generate(ParityScenarios.TurbineTripBypass);
        var s = c.Frames.First(f => f.TurbineTripped && f.TurbineSteamKW <= 0);
        string plate = TurbineHallModel.Plate(TurbineHallModel.Turbine, s);
        Assert.Contains("STEAM 0 kW heat", plate);
        Assert.Contains("TRIPPED", plate);
        Assert.DoesNotContain("NOT TRIPPED", plate);
    }

    [Fact]
    public void PlatesReuseThePhase1ComponentReadouts()
    {
        var s = ParityScenarios.Generate(ParityScenarios.SteadyLoad).Frames.Last();
        Assert.Equal(PlantWallMapModel.ComponentReadout("Turbine", s), TurbineHallModel.Plate(TurbineHallModel.Turbine, s));
        Assert.Equal(PlantWallMapModel.ComponentReadout("Generator", s), TurbineHallModel.Plate(TurbineHallModel.Generator, s));
        Assert.Equal(PlantWallMapModel.ComponentReadout("Condenser", s), TurbineHallModel.Plate(TurbineHallModel.Condenser, s));
        Assert.Equal(PlantWallMapModel.ComponentReadout("FeedPump", s), TurbineHallModel.Plate(TurbineHallModel.FeedPumpGroup, s));
    }

    [Fact]
    public void FeedPumpGroupPlateShowsDeliveredRatedAndDemandCommandAndNoSinglePump()
    {
        var s = Snap(x => { x.FeedwaterDeliveredFrac = 0.62; x.FeedwaterFlowFrac = 0.7; x.FeedwaterAvailabilityFrac = 1; });
        string plate = TurbineHallModel.Plate(TurbineHallModel.FeedPumpGroup, s);
        Assert.Contains("DELIVERED 62.0 % rated", plate);
        Assert.Contains("PUMP 70.0 % command", plate);
        Assert.Equal(1, TurbineHallModel.EquipmentIds.Count(id => id.Contains("feed_pump")));
        Assert.Null(TurbineHallModel.Plate("equip.feed_pump_1", s));
    }

    [Fact]
    public void UnmodeledReadingsShowNotMeasured()
    {
        Assert.Equal("N/M", TurbineHallModel.Plate(TurbineHallModel.Turbine, null));
        var s = Snap(x => { x.FeedwaterDeliveredFrac = double.NaN; x.FeedwaterFlowFrac = double.NaN; x.FeedwaterAvailabilityFrac = double.NaN;
            x.TurbineRpm = double.NaN; x.TurbineSteamKW = double.NaN; x.GeneratorKw = double.NaN; x.CondenserPressureBar = double.NaN; });
        foreach (string id in TurbineHallModel.EquipmentIds)
            Assert.Contains("N/M", TurbineHallModel.Plate(id, s));
        Assert.Null(TurbineHallModel.Plate("equip.nonexistent", s));
        Assert.Null(TurbineHallModel.Plate(null, s));
    }

    [Fact]
    public void EquipmentIdsAreUniqueAndDoNotCollideWithRouteIds()
    {
        var ids = TurbineHallModel.EquipmentIds.ToArray();
        Assert.Equal(4, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        var routes = RouteGraph.AllIds().ToList();
        foreach (string id in ids) Assert.DoesNotContain(id, routes);
    }

    [Fact]
    public void EveryEquipmentIdIsListedInThePlantAndAssetsDesignDoc()
    {
        string doc = File.ReadAllText(TestRepo.Find("docs", "design", "plant-and-assets.md"));
        foreach (string id in TurbineHallModel.EquipmentIds) Assert.Contains("`" + id + "`", doc);
    }
}
