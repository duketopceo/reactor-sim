using System;
using ReactorSim;

// Pure logic (no UnityEngine): the status wall's click-to-select detail plate (Phase 1 U2, R3). A click on a wall component selects it,
// a second click on the same one clears it, a click on another moves the selection. The plate text is the select prompt with nothing
// selected, otherwise the component's title and PlantWallMapModel.ComponentReadout. Linked into ReactorSim.Core.Tests (KTD1);
// PlantWallMapView only applies what this returns.
public static class WallDetailModel
{
    public const string Prompt = "SELECT A COMPONENT ON THE WALL\nFOR ITS LIVE READINGS";

    // The wall nodes the asset draws (System_<name> boxes in PlantOverview.fbx), each backed by a ComponentReadout case.
    public static readonly string[] Components =
    {
        "Reactor", "Safety", "PrimaryPump", "Pressurizer", "SteamGenerator", "Turbine",
        "Generator", "Grid", "Condenser", "Cooling", "FeedPump", "Chemistry"
    };

    public static bool IsComponent(string name)
    {
        return !string.IsNullOrEmpty(name) && Array.IndexOf(Components, name) >= 0;
    }

    // The legend the asset prints under each node.
    public static string Title(string name)
    {
        switch (name)
        {
            case "Reactor": return "REACTOR / FUEL";
            case "Safety": return "SAFETY";
            case "PrimaryPump": return "PRIMARY PUMP";
            case "Pressurizer": return "PRESSURIZER";
            case "SteamGenerator": return "STEAM GENERATOR";
            case "Turbine": return "TURBINE";
            case "Generator": return "GENERATOR";
            case "Grid": return "GRID";
            case "Condenser": return "CONDENSER";
            case "Cooling": return "COOLING";
            case "FeedPump": return "FEED PUMP";
            case "Chemistry": return "BORON / MAKEUP";
            default: return "";
        }
    }

    public static string DetailText(string selected, PlantSnapshot s)
    {
        if (!IsComponent(selected)) return Prompt;
        return Title(selected) + "\n" + PlantWallMapModel.ComponentReadout(selected, s);
    }
}

public sealed class WallSelection
{
    public string Selected { get; private set; }

    // Returns true when the click was on a wall component (selected, moved or cleared); false changes nothing.
    public bool Click(string component)
    {
        if (!WallDetailModel.IsComponent(component)) return false;
        Selected = Selected == component ? null : component;
        return true;
    }

    public void Clear() { Selected = null; }
}
