using System;

// Pure logic (no UnityEngine): the click-hold-drag gesture of a dial (KTD2) and the minimum hit size of a control (R7).
// Sits beside DragMapper (ControlMapping.cs). Linked into ReactorSim.Core.Tests.
namespace ReactorSim.Controls
{
    // The caller feeds the vertical pointer delta in pixels each frame (up is positive) and reads the fraction back.
    // Nothing happens until the pointer has travelled past a dead zone, and the dead-zone distance itself is never
    // applied, so a press with no real drag changes nothing and the value never jumps.
    public sealed class DialDrag
    {
        public const double DefaultDeadZonePixels = 6.0;
        public const double DefaultFineFactor = 0.2;
        /// <summary>Pixels for a full sweep of a continuous dial at gain 1.</summary>
        public const double DefaultContinuousPixels = 400.0;
        /// <summary>Pixels per detent of a stepped knob at gain 1.</summary>
        public const double DefaultDetentPixels = 40.0;
        public const double MinFullRangePixels = 120.0;
        public const double MaxFullRangePixels = 1200.0;

        private readonly double _pixelsPerFullRange;
        private readonly double _deadZone;
        private readonly double _fineFactor;
        private readonly int _detents;
        private double _travel;

        public double StartFraction { get; private set; }
        public double Fraction { get; private set; }
        /// <summary>True once the pointer has travelled past the dead zone (a real drag).</summary>
        public bool Started { get; private set; }
        public bool Finished { get; private set; }
        public bool Cancelled { get; private set; }
        /// <summary>Detents entered since the press (stepped knobs only; every change of detent counts once).</summary>
        public int DetentCrossings { get; private set; }
        public double Delta { get { return Fraction - StartFraction; } }

        public DialDrag(double startFraction, double pixelsPerFullRange, double deadZonePixels, double fineFactor, int detents)
        {
            if (double.IsNaN(startFraction) || double.IsInfinity(startFraction)) startFraction = 0.5;
            StartFraction = Clamp01(startFraction);
            Fraction = StartFraction;
            _pixelsPerFullRange = pixelsPerFullRange > 0.0 ? pixelsPerFullRange : DefaultContinuousPixels;
            _deadZone = deadZonePixels >= 0.0 ? deadZonePixels : DefaultDeadZonePixels;
            _fineFactor = fineFactor > 0.0 && fineFactor <= 1.0 ? fineFactor : DefaultFineFactor;
            _detents = detents > 0 ? detents : 0;
        }

        /// <summary>Pixels for a full sweep of this control: a flat amount for a continuous dial, per detent for a stepped knob, divided by the global gain.</summary>
        public static double PixelsPerFullRange(ControlSpec spec, double gain, double continuousPixels, double detentPixels)
        {
            double basePx = continuousPixels > 0.0 ? continuousPixels : DefaultContinuousPixels;
            if (spec != null && spec.Discrete)
            {
                double steps = Math.Max(1.0, Math.Round(spec.Max - spec.Min));
                basePx = steps * (detentPixels > 0.0 ? detentPixels : DefaultDetentPixels);
            }
            basePx = Math.Min(MaxFullRangePixels, Math.Max(MinFullRangePixels, basePx));
            return basePx / (gain > 0.0 ? gain : 1.0);
        }

        /// <summary>A drag for this control starting at startFraction, with the dials' tuning.</summary>
        public static DialDrag For(ControlSpec spec, double startFraction, InputTuning tuning)
        {
            var t = tuning ?? new InputTuning();
            int detents = spec != null && spec.Discrete ? (int)Math.Max(1.0, Math.Round(spec.Max - spec.Min)) : 0;
            return new DialDrag(startFraction, PixelsPerFullRange(spec, t.DragGain, t.DragContinuousPixels, t.DragDetentPixels),
                t.DragDeadZonePixels, t.DragFineFactor, detents);
        }

        /// <summary>The detent the fraction sits in (0 for a continuous dial).</summary>
        public int DetentIndex
        {
            get { return _detents > 0 ? (int)Math.Floor(Fraction * _detents + 0.5) : 0; }
        }

        /// <summary>Feeds one frame of pointer travel (pixels, up positive). The fine modifier scales this delta only. True when the fraction changed.</summary>
        public bool Move(double dyPixels, bool fine)
        {
            if (Finished || double.IsNaN(dyPixels) || double.IsInfinity(dyPixels)) return false;
            if (!Started)
            {
                _travel += dyPixels;
                if (Math.Abs(_travel) > _deadZone) Started = true;   // the dead-zone distance is dropped: no jump
                return false;
            }
            double before = Fraction;
            int detentBefore = DetentIndex;
            Fraction = Clamp01(Fraction + dyPixels * (fine ? _fineFactor : 1.0) / _pixelsPerFullRange);
            if (DetentIndex != detentBefore) DetentCrossings++;
            return Fraction != before;
        }

        /// <summary>Ends the drag with a commit: the final fraction, or the start fraction when the pointer never left the dead zone.</summary>
        public double Release()
        {
            Finished = true;
            return Started ? Fraction : StartFraction;
        }

        /// <summary>Ends the drag and returns the exact pre-drag fraction.</summary>
        public double Cancel()
        {
            Finished = true;
            Cancelled = true;
            Fraction = StartFraction;
            return StartFraction;
        }

        private static double Clamp01(double v) { return v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v); }
    }

    // A control's collider is padded so it covers at least MinPixels on screen from the reference viewing distance, but never
    // more than MaxSideMetres so neighbouring controls do not overlap.
    public static class HitArea
    {
        public const double MinPixels = 28.0;
        public const double ReferenceDistanceMetres = 1.3;
        public const double MaxSideMetres = 0.06;

        /// <summary>Metres that span `pixels` on screen at `distance` for a vertical field of view and window height.</summary>
        public static double MetresForPixels(double pixels, double distance, double fovDegrees, double screenHeightPixels)
        {
            if (!(screenHeightPixels > 0.0) || !(distance > 0.0)) return 0.0;
            return pixels * 2.0 * distance * Math.Tan(fovDegrees * Math.PI / 360.0) / screenHeightPixels;
        }

        /// <summary>Smallest collider side (m) for a control at the given field of view and window height.</summary>
        public static double MinSideMetres(double fovDegrees, double screenHeightPixels)
        {
            return Math.Min(MaxSideMetres, MetresForPixels(MinPixels, ReferenceDistanceMetres, fovDegrees, screenHeightPixels));
        }
    }
}
