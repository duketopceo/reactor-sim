using System;
using System.Collections.Generic;

// Pure sight-line maths (no UnityEngine) for the reactor window: how far down an eye standing or sitting in the control room can
// look through the observation opening, how far down the pool and the module are, and whether a sight line crosses a box (the
// parked crane). Linked into ReactorSim.Core.Tests and read by HallCheck, so the "you can see the reactor from here" claim is a
// number checked on any box, not a hope tested only in a render.
// Frames: heights are metres above the room floor unless a name says "AbovePoolFloor"; "back" is the distance from the window wall
// into the room; "along" is the position along the wall from the window centre.
namespace ReactorSim
{
    public static class HallSightline
    {
        private const double RadToDeg = 180.0 / Math.PI;

        /// <summary>Downward angle in degrees for a drop over a horizontal run.</summary>
        public static double DownAngleDeg(double drop, double run)
        {
            return Math.Atan2(drop, Math.Max(1e-9, run)) * RadToDeg;
        }

        /// <summary>
        /// Steepest downward sight line (degrees below horizontal) an eye at eyeHeight, back metres from the glass, can take through
        /// an opening whose lower edge is sill high.
        /// </summary>
        public static double OpeningDownReachDeg(double eyeHeight, double sill, double back)
        {
            return DownAngleDeg(eyeHeight - sill, back);
        }

        /// <summary>Where along the wall (from the window centre) the sight line from an eye to a target crosses the wall plane.</summary>
        public static double CrossingAlong(double eyeAlong, double targetAlong, double eyeBack, double targetBeyondWall)
        {
            return eyeAlong + (targetAlong - eyeAlong) * eyeBack / (eyeBack + targetBeyondWall);
        }

        /// <summary>True when the segment a-b passes through the axis-aligned box (slab test). All points are {x, y, z}.</summary>
        public static bool SegmentHitsBox(double[] a, double[] b, double[] boxMin, double[] boxMax)
        {
            double t0 = 0.0, t1 = 1.0;
            for (int i = 0; i < 3; i++)
            {
                double d = b[i] - a[i];
                if (Math.Abs(d) < 1e-12)
                {
                    if (a[i] < boxMin[i] || a[i] > boxMax[i]) return false;
                    continue;
                }
                double ta = (boxMin[i] - a[i]) / d, tb = (boxMax[i] - a[i]) / d;
                if (ta > tb) { double s = ta; ta = tb; tb = s; }
                t0 = Math.Max(t0, ta);
                t1 = Math.Min(t1, tb);
                if (t0 > t1) return false;
            }
            return true;
        }

        /// <summary>One viewpoint looking through the window at the module (or the water over it).</summary>
        public struct View
        {
            public string name;
            public double eyeHeight, back, along;
        }

        public static View Stand() { return new View { name = "stand", eyeHeight = HallDimensions.StandEyeHeight, back = HallDimensions.StandBackFromWall, along = HallDimensions.StandAlongWall }; }
        public static View Seat() { return new View { name = "seat", eyeHeight = HallDimensions.StandEyeHeight, back = HallDimensions.SeatBackFromWall, along = HallDimensions.SeatAlongWall }; }

        /// <summary>Downward angle (degrees) from the viewpoint to a target at the given height above the pool floor over the pool axis.</summary>
        public static double AngleToAxisTargetDeg(View v, double targetAbovePoolFloor)
        {
            double drop = HallDimensions.EyeAbovePoolFloor((float)v.eyeHeight) - targetAbovePoolFloor;
            return DownAngleDeg(drop, v.back + HallDimensions.PoolAxisFromWall);
        }

        /// <summary>Margin in degrees by which the sight line to the target sits inside the opening's downward reach (negative = cut off).</summary>
        public static double ReachMarginDeg(View v, double targetAbovePoolFloor)
        {
            return OpeningDownReachDeg(v.eyeHeight, HallDimensions.WindowSill, v.back) - AngleToAxisTargetDeg(v, targetAbovePoolFloor);
        }

        /// <summary>Margin in metres by which the sight line to the pool axis crosses the wall inside the opening's side edges.</summary>
        public static double EdgeMargin(View v)
        {
            double crossing = CrossingAlong(v.along, HallDimensions.VesselShiftX, v.back, HallDimensions.PoolAxisFromWall);
            return HallDimensions.WindowWidth * 0.5 - Math.Abs(crossing);
        }

        /// <summary>
        /// Height of the module (metres above the pool floor) at which the sight line from the viewpoint grazes the near pool rim and
        /// meets the pool axis. Everything on the axis above this height is seen over the rim; below it the rim hides the module.
        /// </summary>
        public static double RimHiddenBelow(View v)
        {
            double eye = HallDimensions.EyeAbovePoolFloor((float)v.eyeHeight);
            double rimTop = HallDimensions.WaterDepth + HallDimensions.DeckAboveWater;
            double toAxis = v.back + HallDimensions.PoolAxisFromWall;
            double toRim = toAxis - HallDimensions.PoolRadius;
            return rimTop - (eye - rimTop) * HallDimensions.PoolRadius / Math.Max(1e-9, toRim);
        }

        /// <summary>Metres of the module, measured down from its head, that show above the near pool rim (negative = the head is hidden).</summary>
        public static double RimVisibleHeight(View v)
        {
            return HallDimensions.ContainmentTop - RimHiddenBelow(v);
        }

        /// <summary>
        /// What the window has to show: the module head, from the stand and from the seat,
        /// through the middle of the opening. Empty means the claim holds.
        /// </summary>
        public static List<string> CheckWindowReach()
        {
            var bad = new List<string>();
            double m = HallDimensions.SightMarginDeg;
            View stand = Stand(), seat = Seat();
            double standMargin = ReachMarginDeg(stand, HallDimensions.SightTargetHeight);
            double seatMargin = ReachMarginDeg(seat, HallDimensions.SightTargetHeight);
            if (standMargin < m) bad.Add("from the stand the window cannot look down to the module head (margin " + standMargin.ToString("0.0") + " deg)");
            if (seatMargin < m) bad.Add("from the seat the window cannot look down to the module head (margin " + seatMargin.ToString("0.0") + " deg)");
            double needVisible = HallDimensions.HeadDomeHeight + HallDimensions.HeadVisibleMargin;
            foreach (View v in new[] { stand, seat })
                if (RimVisibleHeight(v) < needVisible) bad.Add("from the " + v.name + " the pool rim hides the module head (" + RimVisibleHeight(v).ToString("0.00") + " m shows, " + needVisible.ToString("0.00") + " m needed)");
            foreach (View v in new[] { stand, seat })
                if (EdgeMargin(v) < HallDimensions.SightEdgeMargin) bad.Add("from the " + v.name + " the module sits too close to the window edge (" + EdgeMargin(v).ToString("0.00") + " m)");
            return bad;
        }

        /// <summary>Hall-space eye position {x, y, z} for a viewpoint (x along the wall from the pool axis, y above the pool floor, z negative towards the room).</summary>
        public static double[] EyeInHallSpace(View v)
        {
            return new[]
            {
                v.along - HallDimensions.VesselShiftX,
                (double)HallDimensions.EyeAbovePoolFloor((float)v.eyeHeight),
                -(HallDimensions.PoolAxisFromWall + v.back),
            };
        }

        /// <summary>Hall-space bounds {min, max} of the parked crane (bridge, trucks, trolley and hoist), the same numbers PolarCrane uses.</summary>
        public static double[][] CraneBox()
        {
            double deckY = HallDimensions.WaterDepth + HallDimensions.DeckAboveWater;
            double railY = deckY + HallDimensions.CraneRailAboveDeck;
            double half = Math.Sqrt(Math.Pow(HallDimensions.HallRadius - 0.12, 2) - Math.Pow(HallDimensions.CraneBridgeZ + 2.3, 2)) - 0.9;
            return new[]
            {
                new[] { -half - 0.7, railY - 1.0, HallDimensions.CraneBridgeZ - 2.3 },
                new[] { half + 0.7, railY + 3.3, HallDimensions.CraneBridgeZ + 2.3 },
            };
        }

        /// <summary>
        /// True when no sight line from the viewpoint to the module (head centre and both flanks) or to the water over it crosses the
        /// crane box, i.e. the girders do not cut across the view of the reactor.
        /// </summary>
        public static bool CraneClearOfModule(View v, double[][] craneBox)
        {
            double[] eye = EyeInHallSpace(v);
            double r = HallDimensions.ContainmentDiameter * 0.5;
            double top = HallDimensions.SightTargetHeight;
            double[][] targets =
            {
                new[] { 0.0, top, 0.0 }, new[] { -r, top, 0.0 }, new[] { r, top, 0.0 },
                new[] { 0.0, (double)HallDimensions.WaterDepth, 0.0 },
                new[] { 0.0, (double)HallDimensions.WaterDepth, -HallDimensions.PoolRadius },
                new[] { 0.0, (double)HallDimensions.WaterDepth, HallDimensions.PoolRadius },
            };
            foreach (double[] t in targets)
                if (SegmentHitsBox(eye, t, craneBox[0], craneBox[1])) return false;
            return true;
        }
    }
}
