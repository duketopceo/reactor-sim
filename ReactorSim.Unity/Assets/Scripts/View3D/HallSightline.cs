using System;
using System.Collections.Generic;

// Pure sight-line maths (no UnityEngine) for the reactor window: how much of the vessel an eye standing or sitting in the
// control room can see through the observation opening at eye level (the aquarium window: the eye is underwater and the
// vessel mid-body sits in the pane), and whether a sight line crosses a box (the parked crane). Linked into
// ReactorSim.Core.Tests and read by HallCheck, so the "you can see the reactor from here" claim is a number checked on any
// box, not a hope tested only in a render.
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
        public static View Seat() { return new View { name = "seat", eyeHeight = HallDimensions.SeatEyeHeight, back = HallDimensions.SeatBackFromWall, along = HallDimensions.SeatAlongWall }; }

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
        /// Height (metres above the pool floor) at which the sight line from the viewpoint through a point `edgeY` high on the
        /// window wall crosses the pool axis. Similar triangles: the wall is `back` from the eye, the axis `PoolAxisFromWall` beyond it.
        /// </summary>
        public static double AxisHeightThroughEdge(View v, double edgeY)
        {
            double eye = HallDimensions.EyeAbovePoolFloor((float)v.eyeHeight);
            return edgeY + (edgeY - eye) * HallDimensions.PoolAxisFromWall / Math.Max(1e-9, v.back);
        }

        /// <summary>The lowest and highest vessel heights (above the pool floor) the opening frames on the pool axis.</summary>
        public static void VisibleBandOnAxis(View v, out double low, out double high)
        {
            double sillY = HallDimensions.RoomFloorAbovePoolFloor + HallDimensions.WindowSill;
            low = AxisHeightThroughEdge(v, sillY);
            high = AxisHeightThroughEdge(v, sillY + HallDimensions.WindowHeight);
        }

        /// <summary>
        /// What the window has to show: a band of the vessel mid-body, from the stand and from the seat, through the middle of
        /// the opening (the aquarium claim: the reactor is seen at eye level through the water, not looked down at from a gallery).
        /// Empty means the claim holds.
        /// </summary>
        public static List<string> CheckWindowReach()
        {
            var bad = new List<string>();
            foreach (View v in new[] { Stand(), Seat() })
            {
                double low, high;
                VisibleBandOnAxis(v, out low, out high);
                // The seat sits farther back, so its band rides higher; it gets a wider low-side allowance.
                double lowLimit = HallDimensions.VesselBandLow + (v.back > HallDimensions.StandBackFromWall ? 1.5 : 0.0);
                if (low > lowLimit)
                    bad.Add("from the " + v.name + " the window cannot see down to the vessel mid-body (lowest visible " + low.ToString("0.0") + " m, need " + lowLimit.ToString("0.0") + " m)");
                if (high < HallDimensions.VesselBandHigh)
                    bad.Add("from the " + v.name + " the window cannot see up to the vessel mid-body (highest visible " + high.ToString("0.0") + " m, need " + HallDimensions.VesselBandHigh.ToString("0.0") + " m)");
                if (EdgeMargin(v) < HallDimensions.SightEdgeMargin)
                    bad.Add("from the " + v.name + " the module sits too close to the window edge (" + EdgeMargin(v).ToString("0.00") + " m)");
            }
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
        /// Fraction (0..1) of a grid of sight lines through the observation opening, from the viewpoint, that run into the crane box
        /// before they leave the hall. Unlike the module sight lines this sees a crane above the horizon: it is how much of the
        /// window the girders and trolley fill, so a crane that sits across the view of the reactor is a number that can fail.
        /// </summary>
        public static double CraneOpeningCoverage(View v, double[][] craneBox, int grid = 12)
        {
            double[] eye = EyeInHallSpace(v);
            double wallZ = -HallDimensions.PoolAxisFromWall;
            double centreX = -HallDimensions.VesselShiftX;                   // window centre, hall space
            double sillY = HallDimensions.RoomFloorAbovePoolFloor + HallDimensions.WindowSill;
            int hits = 0;
            for (int i = 0; i < grid; i++)
                for (int j = 0; j < grid; j++)
                {
                    double px = centreX + (-0.5 + (i + 0.5) / grid) * HallDimensions.WindowWidth;
                    double py = sillY + (j + 0.5) / grid * HallDimensions.WindowHeight;
                    double t = (HallDimensions.HallRadius - eye[2]) / (wallZ - eye[2]);   // extend the ray to the far hall wall
                    double[] far = { eye[0] + (px - eye[0]) * t, eye[1] + (py - eye[1]) * t, HallDimensions.HallRadius };
                    if (SegmentHitsBox(new[] { px, py, wallZ }, far, craneBox[0], craneBox[1])) hits++;
                }
            return hits / (double)(grid * grid);
        }

        /// <summary>Most of the window the crane may fill from the stand or the seat before it counts as across the view.</summary>
        public const double MaxCraneOpeningCoverage = 0.25;

        /// <summary>
        /// True when the crane neither crosses a sight line to the module (head centre and both flanks) or to the water over it, nor
        /// fills more than <see cref="MaxCraneOpeningCoverage"/> of the opening seen from the viewpoint.
        /// </summary>
        public static bool CraneClearOfModule(View v, double[][] craneBox)
        {
            if (CraneOpeningCoverage(v, craneBox) > MaxCraneOpeningCoverage) return false;
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
