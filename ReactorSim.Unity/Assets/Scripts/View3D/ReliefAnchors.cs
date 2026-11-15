using System;
using ReactorSim;

// Where the relief and bypass steam leave the module, in hall space (origin on the pool axis at the pool floor, y up, the control-room
// window towards -z). One source for three consumers: HallBuilder creates the anchor empties here, ReactorSoundscape puts the matching
// sounds here, and ReactorAssetCheck asserts the built hall carries them. UnityEngine-free so ReactorSim.Core.Tests can pin the geometry.
//
// Primary relief (PORV): an integral module keeps its pressurizer in the vessel head, so the PORV discharges at the top of the
// containment head, off the axis on the side away from the steam lines.
// Secondary relief and bypass: on the steam line, the thicker of the two lines that run from the head to the pool wall at
// HallDimensions.PipeRunHeight. Two SG relief valves on the header, then the bypass (steam dump) tee nearer the wall.
// The feedwater line carries neither, so it has no anchor.
//
// Submerged discharge: the anchors sit just under the 22 m surface, far above the 5 to 9 m the observation window frames. Each relief
// line therefore runs on to a sparger (a short perforated pipe end) at the fuel-display depth, in the open water between the glass and
// the containment, and a bubble plume rises off it past the window. Three spargers, left to right in the order the anchors sit along the
// module: PORV, SG relief (one sparger for the two-valve header), bypass.
namespace ReactorSim
{
    public static class ReliefAnchors
    {
        public const string PrimaryName = "VFX_PORV_SteamJet_Anchor";
        public const string SecondaryPrefix = "VFX_SGRelief_SteamJet_Anchor_";
        public const string BypassName = "VFX_Bypass_SteamJet_Anchor";
        public const int SecondaryCount = 2;
        public const string SpargerPrimaryName = "VFX_PORV_Sparger_Anchor";
        public const string SpargerSecondaryName = "VFX_SGRelief_Sparger_Anchor";
        public const string SpargerBypassName = "VFX_Bypass_Sparger_Anchor";

        /// <summary>Sparger height: the fuel-display depth, so the plume rises through the same part of the pane as the core glow.</summary>
        public static float SpargerY { get { return HallDimensions.SightTargetHeight + 0.2f; } }
        /// <summary>Distance of the spargers from the glass: in front of the containment (whose near edge is 3.6 - 2.25 = 1.35 m behind the glass) and behind the two depth veils.</summary>
        public const float SpargerFromGlass = 1.0f;
        /// <summary>Hall-space x of the three spargers (PORV, SG relief, bypass), about a metre apart so the three plumes read as three sources from the stand and the seat.</summary>
        public static readonly float[] SpargerX = { -1.7f, -0.75f, 0.2f };
        /// <summary>Each horizontal run on the way to the sparger sits in its own z lane (offsets from the sparger z) so no two share a line.</summary>
        private static readonly double[] LaneOffsetZ = { 0.0, 0.25, 0.5 };

        /// <summary>PORV x on the head, module space (the steam lines leave at +x, so this sits opposite them).</summary>
        public const float PrimaryModuleX = -0.9f;
        /// <summary>The anchor sits this far above the pipe axis or the dome surface, so the jet leaves clear of the metal.</summary>
        public const float AboveSurface = 0.3f;
        /// <summary>Module-space x of the two SG relief valves on the steam line.</summary>
        public static readonly float[] SecondaryModuleX = { 2.4f, 4.2f };
        /// <summary>Module-space x of the bypass tee, a fixed distance inside the pool-wall penetration.</summary>
        public static float BypassModuleX { get { return HallDimensions.SteamLineWallX - 2.0f; } }

        public static string SecondaryName(int index) { return SecondaryPrefix + (index + 1); }

        /// <summary>Containment head surface height at module-space radius r (the dome SmrModuleMesh builds: a quarter ellipse of height 0.9 m).</summary>
        public static double HeadSurfaceY(double r)
        {
            double c = HallDimensions.ContainmentDiameter * 0.5;
            double cosPhi = Math.Max(0.0, Math.Min(1.0, r / c));
            return HallDimensions.ContainmentTop - 0.9 + 0.9 * Math.Sqrt(1.0 - cosPhi * cosPhi);
        }

        private static double[] ToHall(double moduleX, double y, double moduleZ)
        {
            return new[] { moduleX, y, HallDimensions.ModuleZ + moduleZ };
        }

        public static double[] Primary()
        {
            return ToHall(PrimaryModuleX, HeadSurfaceY(Math.Abs(PrimaryModuleX)) + AboveSurface, 0.0);
        }

        public static double[] Secondary(int index)
        {
            if (index < 0 || index >= SecondaryCount) throw new ArgumentOutOfRangeException("index");
            return ToHall(SecondaryModuleX[index], HallDimensions.PipeRunHeight + AboveSurface, HallDimensions.SteamLineZ);
        }

        public static double[] Bypass()
        {
            return ToHall(BypassModuleX, HallDimensions.PipeRunHeight + AboveSurface, HallDimensions.SteamLineZ);
        }

        /// <summary>Where the two SG relief valves' header meets the line down to the sparger: between the two valves.</summary>
        public static double[] SecondaryHeader()
        {
            double[] a = Secondary(0), b = Secondary(1);
            return new[] { 0.5 * (a[0] + b[0]), 0.5 * (a[1] + b[1]), 0.5 * (a[2] + b[2]) };
        }

        private static double SpargerZ { get { return -HallDimensions.PoolAxisFromWall + SpargerFromGlass; } }

        private static double[] Sparger(int index) { return new[] { (double)SpargerX[index], (double)SpargerY, SpargerZ }; }

        public static double[] SpargerPrimary() { return Sparger(0); }
        public static double[] SpargerSecondary() { return Sparger(1); }
        public static double[] SpargerBypass() { return Sparger(2); }

        // Corner list of the line from an anchor to its sparger: out to the sparger's z lane at the anchor's height (clear of the head and
        // the steam line, above them and under the surface), along x to the sparger, then straight down to it.
        private static double[][] Downcomer(double[] anchor, double[] sparger, double laneOffset)
        {
            double lane = sparger[2] + laneOffset;
            return new[]
            {
                anchor,
                new[] { anchor[0], anchor[1], lane },
                new[] { sparger[0], anchor[1], lane },
                new[] { sparger[0], anchor[1], sparger[2] },
                sparger,
            };
        }

        public static double[][] PrimaryDowncomer() { return Downcomer(Primary(), SpargerPrimary(), LaneOffsetZ[0]); }
        public static double[][] SecondaryDowncomer() { return Downcomer(SecondaryHeader(), SpargerSecondary(), LaneOffsetZ[1]); }
        public static double[][] BypassDowncomer() { return Downcomer(Bypass(), SpargerBypass(), LaneOffsetZ[2]); }
    }

    // Which hall steam visuals a snapshot calls for. Primary and secondary relief are independent signals; bypass steam follows the
    // dumped heat rate and nothing else, so it stays visible while the turbine is tripped (the turbine-steam cues follow
    // TurbineSteamKW and are silent then). Missing or non-finite readings give nothing, never a plausible jet.
    public struct HallSteamModel
    {
        public bool PrimaryRelief, SecondaryRelief;
        /// <summary>0 when no bypass steam flows, else 0.15 to 1 (a small flow stays visible); heat rate over rated thermal power.</summary>
        public float Bypass;

        public static HallSteamModel From(PlantSnapshot s)
        {
            var m = new HallSteamModel();
            if (s == null) return m;
            m.PrimaryRelief = s.ReliefOpen;
            m.SecondaryRelief = s.SecondaryReliefOpen;
            double frac = s.DumpSteamKW / Physics.RatedThermalKW;
            if (!double.IsNaN(frac) && !double.IsInfinity(frac) && frac > 0)
                m.Bypass = (float)(0.15 + 0.85 * Math.Sqrt(Math.Min(1.0, frac)));
            return m;
        }
    }
}
