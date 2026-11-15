using System;
using ReactorSim;
using Xunit;

namespace ReactorSim.Core.Tests
{
    // Phase 1 U3 (R6, R7, AE1, AE2): where the relief and bypass steam leave the module, and which hall steam visuals a snapshot calls for.
    public class ReliefAnchorsTests
    {
        private static double Radius(double[] hall)
        {
            return Math.Sqrt(hall[0] * hall[0] + hall[2] * hall[2]);
        }

        [Fact]
        public void PrimaryReliefSitsOnTheContainmentHeadAboveTheVesselOnTheSideAwayFromTheSteamLines()
        {
            double[] p = ReliefAnchors.Primary();
            double moduleX = p[0], moduleZ = p[2] - HallDimensions.ModuleZ;
            Assert.True(Math.Sqrt(moduleX * moduleX + moduleZ * moduleZ) < HallDimensions.ContainmentDiameter * 0.5, "inside the containment footprint");
            Assert.True(p[1] > HallDimensions.VesselTop, "above the vessel top: the pressurizer end of an integral module");
            Assert.True(p[1] > HallDimensions.ContainmentTop - 0.9, "on the head dome, not the shell");
            Assert.True(p[1] < HallDimensions.WaterDepth, "under the pool surface");
            Assert.True(moduleX < 0 && moduleX < HallDimensions.SteamLineStartX, "opposite the steam lines, which leave at +x");
        }

        [Fact]
        public void HeadSurfaceMatchesTheDomeTheModuleMeshBuilds()
        {
            // The dome is a quarter ellipse: the shoulder at the axis radius, the apex ContainmentTop at r = 0.
            Assert.Equal(HallDimensions.ContainmentTop, ReliefAnchors.HeadSurfaceY(0), 6);
            Assert.Equal(HallDimensions.ContainmentTop - 0.9, ReliefAnchors.HeadSurfaceY(HallDimensions.ContainmentDiameter * 0.5), 6);
        }

        [Fact]
        public void SecondaryReliefValvesSitOnTheSteamLineBetweenTheHeadAndThePoolWall()
        {
            for (int i = 0; i < ReliefAnchors.SecondaryCount; i++)
            {
                double[] s = ReliefAnchors.Secondary(i);
                Assert.Equal(HallDimensions.ModuleZ + HallDimensions.SteamLineZ, s[2], 6);
                Assert.True(s[1] > HallDimensions.PipeRunHeight && s[1] < HallDimensions.WaterDepth, "just above the line, under the surface");
                Assert.True(s[0] > HallDimensions.SteamLineStartX && s[0] < HallDimensions.SteamLineWallX - 0.35, "on the horizontal run, clear of the wall collar");
            }
            Assert.True(ReliefAnchors.Secondary(0)[0] < ReliefAnchors.Secondary(1)[0], "ordered along the line");
            Assert.Throws<ArgumentOutOfRangeException>(() => ReliefAnchors.Secondary(ReliefAnchors.SecondaryCount));
        }

        [Fact]
        public void BypassTeeSitsOnTheSteamLineBeyondBothReliefValvesInsideTheWallPenetration()
        {
            double[] b = ReliefAnchors.Bypass();
            Assert.Equal(HallDimensions.ModuleZ + HallDimensions.SteamLineZ, b[2], 6);
            Assert.True(b[0] > ReliefAnchors.Secondary(ReliefAnchors.SecondaryCount - 1)[0] + 1.0, "separate from the relief valves");
            Assert.True(b[0] < HallDimensions.SteamLineWallX - 0.35, "inside the wall collar");
        }

        [Fact]
        public void EveryAnchorIsInsideThePoolAndUnderTheWaterSurface()
        {
            var all = new System.Collections.Generic.List<double[]> { ReliefAnchors.Primary(), ReliefAnchors.Bypass() };
            for (int i = 0; i < ReliefAnchors.SecondaryCount; i++) all.Add(ReliefAnchors.Secondary(i));
            foreach (double[] a in all)
            {
                Assert.True(Radius(a) < HallDimensions.PoolRadius, "inside the pool");
                Assert.InRange(a[1], HallDimensions.ContainmentTop - 0.9, HallDimensions.WaterDepth);
            }
        }

        [Fact]
        public void FeedwaterLineCarriesNoAnchor()
        {
            double feedZ = HallDimensions.ModuleZ + HallDimensions.FeedLineZ;
            Assert.NotEqual(feedZ, ReliefAnchors.Bypass()[2]);
            for (int i = 0; i < ReliefAnchors.SecondaryCount; i++) Assert.NotEqual(feedZ, ReliefAnchors.Secondary(i)[2]);
        }

        [Fact]
        public void NamesAreDistinctAndNoNameMatchesAnotherKindsBinderPrefix()
        {
            // PlantEquipmentBinder binds by StartsWith, so a name that matches two prefixes would drive two signals.
            string[] prefixes = { "VFX_PORV_SteamJet", "VFX_SGRelief", "VFX_Bypass_SteamJet" };
            string[] names = { ReliefAnchors.PrimaryName, ReliefAnchors.SecondaryName(0), ReliefAnchors.SecondaryName(1), ReliefAnchors.BypassName };
            Assert.Equal(names.Length, new System.Collections.Generic.HashSet<string>(names).Count);
            foreach (string name in names)
            {
                int matches = 0;
                foreach (string prefix in prefixes) if (name.StartsWith(prefix, StringComparison.Ordinal)) matches++;
                Assert.Equal(1, matches);
            }
            Assert.StartsWith("VFX_PORV_SteamJet", ReliefAnchors.PrimaryName);
            Assert.StartsWith("VFX_SGRelief", ReliefAnchors.SecondaryName(0));
            Assert.StartsWith("VFX_Bypass_SteamJet", ReliefAnchors.BypassName);
        }

        [Fact]
        public void SecondaryReliefSoundSitsBetweenItsValves()
        {
            double[] a = ReliefAnchors.Secondary(0), b = ReliefAnchors.Secondary(1), m = ReliefAnchors.SecondarySound();
            Assert.InRange(m[0], a[0], b[0]);
            Assert.Equal(a[1], m[1], 6);
            Assert.Equal(a[2], m[2], 6);
        }

        // AE1: only one relief open -> only that jet.
        [Fact]
        public void PrimaryOpenSecondaryClosedShowsOnlyThePrimaryJet()
        {
            var m = HallSteamModel.From(new PlantSnapshot { ReliefOpen = true, SecondaryReliefOpen = false });
            Assert.True(m.PrimaryRelief);
            Assert.False(m.SecondaryRelief);
            Assert.Equal(0f, m.Bypass);
        }

        [Fact]
        public void SecondaryOpenPrimaryClosedShowsOnlyTheSecondaryJets()
        {
            var m = HallSteamModel.From(new PlantSnapshot { ReliefOpen = false, SecondaryReliefOpen = true });
            Assert.False(m.PrimaryRelief);
            Assert.True(m.SecondaryRelief);
            Assert.Equal(0f, m.Bypass);
        }

        [Fact]
        public void NothingOpenOrNoSnapshotShowsNothing()
        {
            foreach (var m in new[] { HallSteamModel.From(new PlantSnapshot()), HallSteamModel.From(null) })
            {
                Assert.False(m.PrimaryRelief);
                Assert.False(m.SecondaryRelief);
                Assert.Equal(0f, m.Bypass);
            }
        }

        // AE2: tripped turbine with bypass steam flowing -> turbine steam visual off, bypass visual on.
        [Fact]
        public void TurbineTrippedWithBypassSteamShowsBypassButNoTurbineSteam()
        {
            var s = new PlantSnapshot { TurbineTripped = true, TurbineSteamKW = 0, DumpSteamKW = Physics.RatedThermalKW * .3 };
            Assert.Equal(0.0, PlantWallMapModel.RouteActivity("Steam", s));            // the wall's turbine steam route
            Assert.Equal(0f, ReactorSoundMix.From(s).TurbineSteam);
            Assert.True(HallSteamModel.From(s).Bypass > 0.15f);
            Assert.True(PlantWallMapModel.RouteActivity("SteamDump", s) > 0);
        }

        [Fact]
        public void BypassFollowsDumpedHeatAndIsIndependentOfTheReliefSignals()
        {
            float none = HallSteamModel.From(new PlantSnapshot { DumpSteamKW = 0 }).Bypass;
            float small = HallSteamModel.From(new PlantSnapshot { DumpSteamKW = Physics.RatedThermalKW * .01 }).Bypass;
            float large = HallSteamModel.From(new PlantSnapshot { DumpSteamKW = Physics.RatedThermalKW * .6 }).Bypass;
            Assert.Equal(0f, none);
            Assert.InRange(small, 0.15f, large);
            Assert.True(large > small);
            Assert.InRange(HallSteamModel.From(new PlantSnapshot { DumpSteamKW = Physics.RatedThermalKW * 50 }).Bypass, 0f, 1f);
            var relief = HallSteamModel.From(new PlantSnapshot { ReliefOpen = true, SecondaryReliefOpen = true, DumpSteamKW = 0 });
            Assert.Equal(0f, relief.Bypass);
        }

        [Fact]
        public void NonFiniteOrNegativeDumpSteamGivesNoBypass()
        {
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -5.0 })
                Assert.Equal(0f, HallSteamModel.From(new PlantSnapshot { DumpSteamKW = bad }).Bypass);
        }

        // The model is fed by real plant state, not only hand-built snapshots: trip the turbine at power with the dump open.
        [Fact]
        public void RealTurbineTripAtPowerWithDumpOpenShowsBypassOnly()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            for (int i = 0; i < 20; i++) p.Step(6.0);
            p.Apply(PlantIntent.SetSteamDump(0.6));
            p.Bop.Turbine.Trip();
            p.Step(6.0);
            PlantSnapshot s = p.CaptureState();
            Assert.True(s.TurbineTripped);
            Assert.Equal(0.0, s.TurbineSteamKW);
            Assert.Equal(0.0, PlantWallMapModel.RouteActivity("Steam", s));
            var m = HallSteamModel.From(s);
            Assert.True(m.Bypass > 0f, "dump steam carries the heat");
            Assert.False(m.PrimaryRelief);
            Assert.False(m.SecondaryRelief);
        }
    }
}
