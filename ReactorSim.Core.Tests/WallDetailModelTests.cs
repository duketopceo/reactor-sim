using ReactorSim;
using Xunit;

// U2 (R3): the wall's click-to-select detail plate. Selection logic is UnityEngine-free so it is tested here (KTD1).
public class WallDetailModelTests
{
    private static PlantSnapshot Plant() => new PlantSnapshot { Power = .42, FuelTempC = 456, PrimaryFlowFrac = .9, PrimaryPressureBar = 155,
        BankInsertion = new[] { .1, .2, .3, .4 } };

    [Fact]
    public void WithNothingSelectedThePlateShowsTheSelectPrompt()
    {
        var sel = new WallSelection();
        Assert.Null(sel.Selected);
        Assert.Equal(WallDetailModel.Prompt, WallDetailModel.DetailText(sel.Selected, Plant()));
        Assert.Contains("SELECT A COMPONENT", WallDetailModel.Prompt);
        // The prompt does not depend on the snapshot, even a missing one.
        Assert.Equal(WallDetailModel.Prompt, WallDetailModel.DetailText(null, null));
    }

    [Fact]
    public void SelectingAComponentShowsItsReadout()
    {
        var sel = new WallSelection();
        Assert.True(sel.Click("Reactor"));
        Assert.Equal("Reactor", sel.Selected);
        string text = WallDetailModel.DetailText(sel.Selected, Plant());
        Assert.Contains(PlantWallMapModel.ComponentReadout("Reactor", Plant()), text);
        Assert.Contains("PWR 42.0 %", text);
        Assert.NotEqual(WallDetailModel.Prompt, text);
    }

    [Fact]
    public void SelectingTheSameComponentAgainClearsToThePrompt()
    {
        var sel = new WallSelection();
        sel.Click("Turbine");
        Assert.True(sel.Click("Turbine"));
        Assert.Null(sel.Selected);
        Assert.Equal(WallDetailModel.Prompt, WallDetailModel.DetailText(sel.Selected, Plant()));
    }

    [Fact]
    public void ClickingAnotherComponentMovesTheSelection()
    {
        var sel = new WallSelection();
        sel.Click("Reactor");
        Assert.True(sel.Click("Pressurizer"));
        Assert.Equal("Pressurizer", sel.Selected);
        Assert.Contains("PRESS 155.0 bar", WallDetailModel.DetailText(sel.Selected, Plant()));
    }

    [Fact]
    public void AnUnknownOrEmptyNameChangesNothing()
    {
        var sel = new WallSelection();
        sel.Click("Reactor");
        Assert.False(sel.Click("Legend_Title"));
        Assert.False(sel.Click(""));
        Assert.False(sel.Click(null));
        Assert.Equal("Reactor", sel.Selected);
        sel.Clear();
        Assert.Null(sel.Selected);
    }

    [Fact]
    public void EveryWallComponentIsSelectableAndHasAReadoutAndTitle()
    {
        Assert.Equal(12, WallDetailModel.Components.Length);
        foreach (string name in WallDetailModel.Components)
        {
            Assert.True(WallDetailModel.IsComponent(name));
            string text = WallDetailModel.DetailText(name, Plant());
            Assert.StartsWith(WallDetailModel.Title(name), text);
            Assert.NotEqual("N/M", PlantWallMapModel.ComponentReadout(name, Plant()));
        }
        Assert.False(WallDetailModel.IsComponent("Core_Fuel"));
    }

    [Fact]
    public void ASelectedComponentWithAMissingSnapshotReadsNotMeasured()
    {
        string text = WallDetailModel.DetailText("Reactor", null);
        Assert.Contains("N/M", text);
        Assert.DoesNotContain("NaN", text);
    }

    [Fact]
    public void DetailTextStaysShortEnoughForItsPlate()
    {
        // The plate holds a title plus the longest readout (the reactor's three lines) with no wrapping surprises.
        foreach (string name in WallDetailModel.Components)
        {
            string[] lines = WallDetailModel.DetailText(name, Plant()).Split('\n');
            Assert.InRange(lines.Length, 2, 5);
            foreach (string line in lines) Assert.True(line.Length <= 56, name + " line too long: " + line);
        }
    }
}
