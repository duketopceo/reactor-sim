using System;
using System.Collections.Generic;
using ReactorSim;

// Pure turbine-hall equipment model (no UnityEngine); linked into ReactorSim.Core.Tests. Rotor speed comes only from the snapshot,
// so a tripped turbine coasts down exactly as the simulation does: no decay or lag is added here. Plates reuse the Phase 1 readout
// rules of PlantWallMapModel (actual vs "% command", "kW heat", "N/M" for anything unmodeled). The plant model has only aggregate
// feed readings, so the feed pumps share one group plate and no plate is bound to a single pump.
public static class TurbineHallModel
{
    public const string Turbine = "equip.turbine";
    public const string Generator = "equip.generator";
    public const string Condenser = "equip.condenser";
    public const string FeedPumpGroup = "equip.feed_pump_group";

    public static readonly string[] EquipmentIds = { Turbine, Generator, Condenser, FeedPumpGroup };

    // Visual rotor speed 0..1 for the turbine rotor: the wall model's turbine speed, which already stops the rotor for a missing
    // or non-finite reading, so the hall and the wall can never disagree.
    public static double RotorSpeed(PlantSnapshot s)
    {
        return PlantWallMapModel.RotorSpeed(true, s);
    }

    // Status plate text for an equipment ID; null for an unknown ID, N/M for a missing snapshot.
    public static string Plate(string equipmentId, PlantSnapshot s)
    {
        switch (equipmentId)
        {
            case Turbine: return PlantWallMapModel.ComponentReadout("Turbine", s);
            case Generator: return PlantWallMapModel.ComponentReadout("Generator", s);
            case Condenser: return PlantWallMapModel.ComponentReadout("Condenser", s);
            case FeedPumpGroup: return PlantWallMapModel.ComponentReadout("FeedPump", s);
            default: return null;
        }
    }
}
