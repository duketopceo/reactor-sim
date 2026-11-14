using System;
using ReactorSim;

// Pure sound levels derived from the same snapshot as the instruments.
public struct ReactorSoundMix
{
    public float Core, Water, Relief, Cooling, Generator, DetectorRate;

    private static float Unit(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) return 0;
        return (float)Math.Min(1, value);
    }

    public static ReactorSoundMix From(PlantSnapshot s)
    {
        if (s == null) return new ReactorSoundMix();
        float power = Unit(s.Power), flow = Unit(s.PrimaryFlowFrac);
        float cooling = Math.Max(Unit(s.CoolingWaterFrac), Unit(s.VacuumPumpFrac));
        return new ReactorSoundMix {
            Core = 0.035f + 0.12f * (float)Math.Sqrt(power),
            Water = 0.025f + 0.15f * flow,
            Relief = s.ReliefOpen || s.SecondaryReliefOpen ? 0.22f : 0,
            Cooling = 0.10f * cooling,
            Generator = s.GeneratorSynced ? 0.12f * Unit(s.GridOutputFrac) : 0,
            DetectorRate = 0.35f + 5.65f * (float)Math.Sqrt(power)
        };
    }
}
