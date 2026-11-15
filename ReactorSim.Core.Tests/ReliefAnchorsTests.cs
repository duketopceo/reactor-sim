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
            string[] prefixes = { "VFX_PORV_SteamJet", "VFX_SGRelief_SteamJet", "VFX_Bypass_SteamJet", "VFX_PORV_Sparger", "VFX_SGRelief_Sparger", "VFX_Bypass_Sparger" };
            string[] names = { ReliefAnchors.PrimaryName, ReliefAnchors.SecondaryName(0), ReliefAnchors.SecondaryName(1), ReliefAnchors.BypassName,
                ReliefAnchors.SpargerPrimaryName, ReliefAnchors.SpargerSecondaryName, ReliefAnchors.SpargerBypassName };
            Assert.Equal(names.Length, new System.Collections.Generic.HashSet<string>(names).Count);
            foreach (string name in names)
            {
                int matches = 0;
                foreach (string prefix in prefixes) if (name.StartsWith(prefix, StringComparison.Ordinal)) matches++;
                Assert.Equal(1, matches);
            }
            Assert.StartsWith("VFX_PORV_SteamJet", ReliefAnchors.PrimaryName);
            Assert.StartsWith("VFX_SGRelief_SteamJet", ReliefAnchors.SecondaryName(0));
            Assert.StartsWith("VFX_Bypass_SteamJet", ReliefAnchors.BypassName);
        }

        [Fact]
        public void SecondaryReliefHeaderSitsBetweenItsValves()
        {
            double[] a = ReliefAnchors.Secondary(0), b = ReliefAnchors.Secondary(1), m = ReliefAnchors.SecondaryHeader();
            Assert.InRange(m[0], a[0], b[0]);
            Assert.Equal(a[1], m[1], 6);
            Assert.Equal(a[2], m[2], 6);
        }

        // ---- submerged discharge: each relief line ends in a sparger in view of the observation window ----

        private static double[][] Spargers()
        {
            return new[] { ReliefAnchors.SpargerPrimary(), ReliefAnchors.SpargerSecondary(), ReliefAnchors.SpargerBypass() };
        }

        private static double[][][] Downcomers()
        {
            return new[] { ReliefAnchors.PrimaryDowncomer(), ReliefAnchors.SecondaryDowncomer(), ReliefAnchors.BypassDowncomer() };
        }

        [Fact]
        public void EverySpargerSitsInsideTheBandTheWindowFramesFromTheStandAndTheSeat()
        {
            foreach (double[] sp in Spargers())
                foreach (var view in new[] { HallSightline.Stand(), HallSightline.Seat() })
                {
                    double beyond = sp[2] + HallDimensions.PoolAxisFromWall;     // metres behind the glass
                    double low, high;
                    HallSightline.VisibleBandAt(view, beyond, out low, out high);
                    Assert.True(sp[1] >= low + 0.5, view.name + ": sparger " + sp[1] + " m is under the framed band (" + low + ")");
                    Assert.True(sp[1] <= high - 1.5, view.name + ": no room above the sparger for the plume to climb inside the window (" + high + ")");
                    Assert.True(HallSightline.EdgeMarginAt(view, sp[0], beyond) >= HallDimensions.SightEdgeMargin, view.name + ": sparger at x " + sp[0] + " is too near a window edge");
                }
        }

        [Fact]
        public void TheJetAnchorsAloneAreFarAboveWhatTheWindowFramesWhichIsWhyTheSpargersExist()
        {
            // Negative control for the band test: the anchors sit just under the surface, over 12 m above the top of the framed band.
            var anchors = new System.Collections.Generic.List<double[]> { ReliefAnchors.Primary(), ReliefAnchors.Bypass(), ReliefAnchors.Secondary(0), ReliefAnchors.Secondary(1) };
            foreach (double[] a in anchors)
                foreach (var view in new[] { HallSightline.Stand(), HallSightline.Seat() })
                {
                    double low, high;
                    HallSightline.VisibleBandAt(view, a[2] + HallDimensions.PoolAxisFromWall, out low, out high);
                    Assert.True(a[1] > high + 10.0, view.name + ": anchor at " + a[1] + " m vs framed top " + high + " m");
                }
        }

        [Fact]
        public void SpargersSitAtTheFuelDisplayDepth()
        {
            foreach (double[] sp in Spargers())
                Assert.InRange(sp[1], HallDimensions.SightTargetHeight - 0.5, HallDimensions.SightTargetHeight + 0.5);
        }

        [Fact]
        public void SpargersAreFarEnoughApartToTellApartFromTheStandAndTheSeat()
        {
            double[][] sp = Spargers();
            for (int i = 0; i < sp.Length; i++)
                for (int j = i + 1; j < sp.Length; j++)
                {
                    Assert.True(Math.Abs(sp[i][0] - sp[j][0]) >= 0.9, "hall x separation of spargers " + i + " and " + j);
                    foreach (var v in new[] { HallSightline.Stand(), HallSightline.Seat() })
                    {
                        double beyond = sp[i][2] + HallDimensions.PoolAxisFromWall;
                        double a = HallSightline.CrossingAlong(v.along, sp[i][0] + HallDimensions.VesselShiftX, v.back, beyond);
                        double b = HallSightline.CrossingAlong(v.along, sp[j][0] + HallDimensions.VesselShiftX, v.back, beyond);
                        Assert.True(Math.Abs(a - b) >= 0.6, v.name + ": spargers " + i + " and " + j + " cross the glass " + Math.Abs(a - b) + " m apart");
                    }
                }
        }

        [Fact]
        public void SpargersLeftToRightFollowTheAnchorsOnTheModule()
        {
            // PORV (opposite the steam lines), then the SG relief, then the bypass tee: the same order as the anchors along module x.
            double[][] sp = Spargers();
            Assert.True(sp[0][0] < sp[1][0] && sp[1][0] < sp[2][0]);
            Assert.True(ReliefAnchors.Primary()[0] < ReliefAnchors.Secondary(0)[0] && ReliefAnchors.Secondary(1)[0] < ReliefAnchors.Bypass()[0]);
        }

        [Fact]
        public void SpargersAreUnderWaterInTheOpenWaterBetweenTheGlassAndTheContainment()
        {
            double front = HallDimensions.ModuleZ - HallDimensions.ContainmentDiameter * 0.5;
            foreach (double[] sp in Spargers())
            {
                Assert.InRange(sp[1], 1.0, HallDimensions.WaterDepth - 1.0);
                Assert.True(Radius(sp) < HallDimensions.PoolRadius - 0.5, "inside the pool");
                Assert.True(sp[2] < front - 0.2, "in front of the containment shell, not inside it or behind it");
                Assert.True(sp[2] > -HallDimensions.PoolAxisFromWall + 0.5, "clear of the glass");
            }
        }

        [Fact]
        public void EachDowncomerRunsFromItsReliefAnchorDownToItsSpargerAndStaysUnderWater()
        {
            double[][] from = { ReliefAnchors.Primary(), ReliefAnchors.SecondaryHeader(), ReliefAnchors.Bypass() };
            double[][] to = Spargers();
            double[][][] runs = Downcomers();
            for (int k = 0; k < runs.Length; k++)
            {
                double[][] run = runs[k];
                Assert.True(run.Length >= 3);
                Assert.Equal(from[k], run[0]);
                Assert.Equal(to[k], run[run.Length - 1]);
                double[] a = run[run.Length - 2], b = run[run.Length - 1];
                Assert.Equal(a[0], b[0], 6);
                Assert.Equal(a[2], b[2], 6);
                Assert.True(a[1] > b[1] + 5.0, "a long vertical drop into the window's view");
                foreach (double[] c in run) Assert.True(c[1] < HallDimensions.WaterDepth, "no corner above the surface");
            }
        }

        [Fact]
        public void DowncomerRunsDoNotOverlapEachOther()
        {
            // Lanes differ in z (or y), so no two horizontal runs share a line.
            var lanes = new System.Collections.Generic.HashSet<string>();
            foreach (double[][] run in Downcomers())
                for (int i = 0; i + 1 < run.Length; i++)
                {
                    double[] a = run[i], b = run[i + 1];
                    bool horizontalAlongX = Math.Abs(a[1] - b[1]) < 1e-9 && Math.Abs(a[2] - b[2]) < 1e-9 && Math.Abs(a[0] - b[0]) > 1e-9;
                    if (horizontalAlongX) Assert.True(lanes.Add(a[1].ToString("0.000") + "/" + a[2].ToString("0.000")), "two relief runs share a lane");
                }
        }

        // R9: the fuel array and its glow stay readable. Seen from the stand the module sits on the left of the pane, from the seat on the right,
        // so the only places clear of the fuel from both are the open water left of it, the narrow gap between the two footprints, and the right
        // edge of the pane; the spargers and their downcomers sit there.
        private const double ClearOfFuelOnGlass = 0.06;      // m on the glass between a pipe centre line and the fuel footprint: more than the 0.05 m pipe radius

        /// <summary>The fuel array's footprint on the glass from a viewpoint: {low, high} along the wall from the window centre.</summary>
        private static double[] FuelFootprintOnGlass(HallSightline.View v)
        {
            double a = HallSightline.CrossingAlong(v.along, -HallDimensions.FuelArrayHalfWidth + HallDimensions.VesselShiftX, v.back, HallDimensions.ModuleFromWall);
            double b = HallSightline.CrossingAlong(v.along, HallDimensions.FuelArrayHalfWidth + HallDimensions.VesselShiftX, v.back, HallDimensions.ModuleFromWall);
            return new[] { Math.Min(a, b), Math.Max(a, b) };
        }

        private static double GlassCrossing(HallSightline.View v, double[] p)
        {
            return HallSightline.CrossingAlong(v.along, p[0] + HallDimensions.VesselShiftX, v.back, p[2] + HallDimensions.PoolAxisFromWall);
        }

        private static void AssertClearOfFuel(string what, HallSightline.View v, double lo, double hi)
        {
            double[] fuel = FuelFootprintOnGlass(v);
            Assert.True(hi <= fuel[0] - ClearOfFuelOnGlass || lo >= fuel[1] + ClearOfFuelOnGlass,
                v.name + ": " + what + " crosses the glass over [" + lo.ToString("0.00") + ", " + hi.ToString("0.00") + "] m, the fuel array covers [" + fuel[0].ToString("0.00") + ", " + fuel[1].ToString("0.00") + "] m");
        }

        [Fact]
        public void SpargerSightLinesStayClearOfTheFuelArrayFromTheStandAndTheSeat()
        {
            string[] names = { "PORV sparger", "SG relief sparger", "bypass sparger" };
            double[][] sp = Spargers();
            for (int i = 0; i < sp.Length; i++)
                foreach (var v in new[] { HallSightline.Stand(), HallSightline.Seat() })
                {
                    // The drum runs along z, so both its ends count (the near end crosses the glass at a slightly different place).
                    double near = GlassCrossing(v, new[] { sp[i][0], sp[i][1], sp[i][2] - ReliefAnchors.SpargerHalfLength });
                    double far = GlassCrossing(v, new[] { sp[i][0], sp[i][1], sp[i][2] + ReliefAnchors.SpargerHalfLength });
                    AssertClearOfFuel(names[i], v, Math.Min(near, far), Math.Max(near, far));
                }
        }

        [Fact]
        public void DowncomerPipesStayClearOfTheFuelArrayWhereverTheyRunThroughItsHeightBand()
        {
            double bandLow = HallDimensions.VesselBandLow, bandHigh = HallDimensions.VesselBandHigh;
            double[][][] runs = Downcomers();
            for (int k = 0; k < runs.Length; k++)
                for (int i = 0; i + 1 < runs[k].Length; i++)
                {
                    double[] a = runs[k][i], b = runs[k][i + 1];
                    if (Math.Max(a[1], b[1]) < bandLow || Math.Min(a[1], b[1]) > bandHigh) continue;   // never at fuel height
                    foreach (var v in new[] { HallSightline.Stand(), HallSightline.Seat() })
                    {
                        double ca = GlassCrossing(v, a), cb = GlassCrossing(v, b);
                        AssertClearOfFuel("downcomer " + k + " segment " + i, v, Math.Min(ca, cb), Math.Max(ca, cb));
                    }
                }
        }

        [Fact]
        public void TheFuelFootprintGuardCatchesAPipeInFrontOfTheFuel()
        {
            // Negative control: a pipe 1 m behind the glass on the sight line from each viewpoint to the fuel centre is in front of the fuel.
            foreach (var v in new[] { HallSightline.Stand(), HallSightline.Seat() })
            {
                double centre = HallSightline.CrossingAlong(v.along, HallDimensions.VesselShiftX, v.back, HallDimensions.ModuleFromWall);
                double hallX = v.along + (centre - v.along) * (v.back + 1.0) / v.back - HallDimensions.VesselShiftX;
                double[] inFront = { hallX, HallDimensions.SightTargetHeight, -HallDimensions.PoolAxisFromWall + 1.0 };
                double c = GlassCrossing(v, inFront);
                Assert.Equal(centre, c, 6);
                double[] fuel = FuelFootprintOnGlass(v);
                Assert.True(c > fuel[0] && c < fuel[1], v.name + ": the control should sit over the fuel");
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertClearOfFuel("control", v, c, c));
            }
        }

        [Fact]
        public void TheHeaderLinkingTheTwoSgReliefValvesCarriesNoAnchorOfItsOwn()
        {
            Assert.Equal(ReliefAnchors.Secondary(0)[2], ReliefAnchors.SecondaryHeader()[2], 6);
            Assert.Equal(ReliefAnchors.Secondary(0)[1], ReliefAnchors.SecondaryHeader()[1], 6);
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
