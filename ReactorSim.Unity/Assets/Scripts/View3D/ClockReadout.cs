using System;
using System.Globalization;

// Pure logic (no UnityEngine): the clock state as CRT text (U2 SimClock -> "x10", "PAUSED", "AUTO-SLOW ...").
namespace ReactorSim.Instruments
{
    public struct ClockState
    {
        public double Scale;
        public double EffectiveScale;
        public bool Paused;
        public bool AutoSlow;
        public string AutoSlowReason;
        public bool Frozen;
        public string FrozenReason;
        public bool Skipping;
    }

    public static class ClockReadout
    {
        public static ClockState FromClock(SimClock c)
        {
            var st = new ClockState();
            if (c == null) { st.Scale = 1.0; st.EffectiveScale = 1.0; return st; }
            st.Scale = c.Scale;
            st.EffectiveScale = c.EffectiveScale;
            st.Paused = c.Paused;
            st.AutoSlow = c.AutoSlow;
            st.AutoSlowReason = c.AutoSlowReason;
            st.Frozen = c.Frozen;
            st.FrozenReason = c.FrozenReason;
            st.Skipping = c.Skipping;
            return st;
        }

        public static string Mult(double v)
        {
            return "x" + v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // One line, most severe state first: frozen, paused, skipping, auto-slow, otherwise the multiplier.
        public static string Format(ClockState st)
        {
            if (st.Frozen)
                return "FROZEN" + (string.IsNullOrEmpty(st.FrozenReason) ? "" : " - " + st.FrozenReason);
            if (st.Paused) return "PAUSED (" + Mult(st.Scale) + ")";
            if (st.Skipping) return "SKIPPING AHEAD " + Mult(st.EffectiveScale);
            if (st.AutoSlow)
                return "AUTO-SLOW " + Mult(st.EffectiveScale)
                    + (string.IsNullOrEmpty(st.AutoSlowReason) ? "" : " (" + st.AutoSlowReason + ")");
            return Mult(st.Scale);
        }
    }
}
