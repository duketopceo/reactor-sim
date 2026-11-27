using System;
using System.Collections.Generic;
using Xunit;

namespace ReactorSim.Tests
{
    // Phase 4 U1: the radiation source register, shielding table and dose field.
    // Scenarios are the U1 list of docs/plans/2026-11-14-ce-phase-4-radiation-access-plan.md (R1-R6, R10, R12).
    public class RadiationModelTests
    {
        private static readonly double R0Sq = Physics.RadSoftenRadiusM * Physics.RadSoftenRadiusM;
        private static readonly Slab[] None = new Slab[0];

        private static Plant AtPower()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            return p;
        }

        private static Plant Cold()
        {
            var p = new Plant();
            p.LoadPreset("cold-shutdown");
            return p;
        }

        private static Plant Tripped(int steps = 5)
        {
            var p = AtPower();
            p.Apply(PlantIntent.TriggerScram());
            for (int i = 0; i < steps; i++) p.Step(1.0);
            return p;
        }

        private static DoseReading AtReference(Plant plant, string area)
        {
            RadPoint pt;
            Assert.True(RadiationAreas.TryGetReferencePoint(area, out pt), area);
            return DoseField.Evaluate(plant, area, pt.X, pt.Y, pt.H);
        }

        private static RadSource Constant(string id, string area, double x, double y, double h, double strength)
        {
            return new RadSource(id, area, x, y, h, StrengthRule.Constant, strength, 0.0);
        }

        private static BarrierTable OpenBarriers()
        {
            var t = new BarrierTable();
            foreach (string src in new[] { RadiationAreas.ReactorPoolId, RadiationAreas.ReactorHallId })
                foreach (string subj in RadiationAreas.AllAreaIds) t.Set(src, subj, None);
            return t;
        }

        private static double Over(SourceRegister reg, BarrierTable table, string area, double x, double y, double h)
        {
            var r = DoseField.Evaluate(reg, table, new RadiationPlantState(1.0, 0.0, 1.0, false), area, x, y, h);
            Assert.True(r.Available, r.Reason.ToString());
            return r.RateMSvPerH - Physics.RadBackgroundMSvPerH;
        }

        // ---- readable range (KTD3, risk row on pool water) ----

        [Fact]
        public void ControlRoomAtRatedPower_IsAtLeastBackground_AndBelowRadiationAreaThreshold()
        {
            var r = AtReference(AtPower(), RadiationAreas.ControlRoomId);
            Assert.True(r.Available);
            Assert.True(r.RateMSvPerH >= Physics.RadBackgroundMSvPerH);
            Assert.True(r.RateMSvPerH < Physics.RadAreaThresholdMSvPerH);
            Assert.Equal(PostingLevel.None, DoseField.Classify(r));
        }

        [Fact]
        public void DeckAtRatedPower_IsAtOrAboveRadiationAreaThreshold()
        {
            var r = AtReference(AtPower(), RadiationAreas.ReactorDeckId);
            Assert.True(r.Available);
            Assert.True(r.RateMSvPerH >= Physics.RadAreaThresholdMSvPerH, r.RateMSvPerH.ToString());
            Assert.True(DoseField.Classify(r) >= PostingLevel.RadiationArea);
        }

        [Fact]
        public void DeckReading_FallsAfterTrip_AndColdPlantIsNotPosted()
        {
            double power = AtReference(AtPower(), RadiationAreas.ReactorDeckId).RateMSvPerH;
            double tripped = AtReference(Tripped(), RadiationAreas.ReactorDeckId).RateMSvPerH;
            double cold = AtReference(Cold(), RadiationAreas.ReactorDeckId).RateMSvPerH;
            Assert.True(tripped < power);
            Assert.True(cold <= tripped);
            Assert.True(cold >= Physics.RadBackgroundMSvPerH);
            Assert.True(cold < Physics.RadAreaThresholdMSvPerH);
        }

        // ---- distance law (KTD4) ----

        [Fact]
        public void TwoMetresReadsMoreThanFourMetres_AndMatchesTheSoftenedFormula()
        {
            var reg = new SourceRegister(new[] { Constant("t.src", RadiationAreas.ReactorHallId, 0, 0, 0, 10.0) });
            var open = OpenBarriers();
            double at2 = Over(reg, open, RadiationAreas.ReactorHallId, 2, 0, 0);
            double at4 = Over(reg, open, RadiationAreas.ReactorHallId, 4, 0, 0);
            Assert.True(at2 > at4);
            Assert.Equal(10.0 / (4.0 + R0Sq), at2, 12);
            Assert.Equal(10.0 / (16.0 + R0Sq), at4, 12);
        }

        [Fact]
        public void FarFromTheSource_DoublingTheDistanceQuartersTheRate()
        {
            var reg = new SourceRegister(new[] { Constant("t.src", RadiationAreas.ReactorHallId, 0, 0, 0, 10.0) });
            var open = OpenBarriers();
            double near = Over(reg, open, RadiationAreas.ReactorHallId, 100, 0, 0);
            double far = Over(reg, open, RadiationAreas.ReactorHallId, 200, 0, 0);
            Assert.InRange(near / far, 3.9999, 4.0001);
        }

        [Fact]
        public void AtTheSourcePosition_RateIsFinite_AndEqualsStrengthOverSoftenRadiusSquared()
        {
            var reg = new SourceRegister(new[] { Constant("t.src", RadiationAreas.ReactorHallId, 3, 4, 5, 10.0) });
            double d = Over(reg, OpenBarriers(), RadiationAreas.ReactorHallId, 3, 4, 5);
            Assert.True(double.IsFinite(d));
            Assert.Equal(10.0 / R0Sq, d, 9);
        }

        [Fact]
        public void DistanceIsThreeDimensional()
        {
            var reg = new SourceRegister(new[] { Constant("t.src", RadiationAreas.ReactorHallId, 0, 0, 0, 10.0) });
            double d = Over(reg, OpenBarriers(), RadiationAreas.ReactorHallId, 1, 2, 2);
            Assert.Equal(10.0 / (9.0 + R0Sq), d, 12);
        }

        // ---- shielding (KTD5) ----

        [Fact]
        public void OneHalfValueLayerOfConcrete_HalvesTheContribution()
        {
            var reg = new SourceRegister(new[] { Constant("t.src", RadiationAreas.ReactorPoolId, 0, 0, 0, 10.0) });
            var open = OpenBarriers();
            var shielded = OpenBarriers();
            shielded.Set(RadiationAreas.ReactorPoolId, RadiationAreas.ReactorHallId,
                new[] { new Slab(RadMaterial.Concrete, Physics.RadConcreteHvlM) });
            double bare = Over(reg, open, RadiationAreas.ReactorHallId, 3, 0, 0);
            double behind = Over(reg, shielded, RadiationAreas.ReactorHallId, 3, 0, 0);
            Assert.Equal(0.5, behind / bare, 12);
        }

        [Fact]
        public void EachMaterialHalvesAtItsOwnHalfValueLayer_AndSlabsMultiply()
        {
            foreach (var m in new[] { RadMaterial.Concrete, RadMaterial.Steel, RadMaterial.Lead, RadMaterial.Water })
                Assert.Equal(0.5, BarrierTable.Attenuation(new[] { new Slab(m, BarrierTable.HvlM(m)) }), 12);
            Assert.Equal(1.0, BarrierTable.Attenuation(None));
            Assert.Equal(0.25, BarrierTable.Attenuation(new[]
            {
                new Slab(RadMaterial.Steel, Physics.RadSteelHvlM), new Slab(RadMaterial.Lead, Physics.RadLeadHvlM)
            }), 12);
            Assert.True(Physics.RadLeadHvlM < Physics.RadSteelHvlM && Physics.RadSteelHvlM < Physics.RadConcreteHvlM);
        }

        [Fact]
        public void CoreReachesTheDeckOnlyThroughThePoolWaterSlab()
        {
            Slab[] slabs;
            Assert.True(RadiationAreas.DefaultBarriers.TryGet(RadiationAreas.ReactorPoolId, RadiationAreas.ReactorDeckId, out slabs));
            Assert.Contains(slabs, s => s.Material == RadMaterial.Water);
            foreach (string subj in RadiationAreas.AllAreaIds)
            {
                if (subj == RadiationAreas.ReactorPoolId) continue;
                Assert.True(RadiationAreas.DefaultBarriers.TryGet(RadiationAreas.ReactorPoolId, subj, out slabs), subj);
                Assert.Contains(slabs, s => s.Material == RadMaterial.Water);
            }

            var core = new SourceRegister(new[] { SourceRegister.Default.Find(SourceRegister.CoreId) });
            RadPoint deck;
            Assert.True(RadiationAreas.TryGetReferencePoint(RadiationAreas.ReactorDeckId, out deck));
            var stripped = RadiationAreas.DefaultBarriers.Clone();
            stripped.Set(RadiationAreas.ReactorPoolId, RadiationAreas.ReactorDeckId, None);
            double withWater = Over(core, RadiationAreas.DefaultBarriers, RadiationAreas.ReactorDeckId, deck.X, deck.Y, deck.H);
            double without = Over(core, stripped, RadiationAreas.ReactorDeckId, deck.X, deck.Y, deck.H);
            Assert.True(without > withWater);
        }

        [Fact]
        public void HallSources_DoNotNeedThePoolSlabToReachTheDeck()
        {
            Slab[] slabs;
            Assert.True(RadiationAreas.DefaultBarriers.TryGet(RadiationAreas.ReactorHallId, RadiationAreas.ReactorDeckId, out slabs));
            Assert.Empty(slabs);
        }

        // ---- classification (R10) ----

        [Fact]
        public void EachAreaClassificationEqualsTheClassificationOfItsReferencePointRate()
        {
            foreach (var plant in new[] { AtPower(), Tripped(), Cold() })
                foreach (string area in RadiationAreas.AllAreaIds)
                {
                    var reading = AtReference(plant, area);
                    Assert.Equal(DoseField.Classify(reading.RateMSvPerH), DoseField.ClassifyArea(plant, area));
                }
        }

        [Fact]
        public void AreaClassification_AtPower_PostsTheHallAndLeavesTheControlRoomOpen()
        {
            var plant = AtPower();
            Assert.Equal(PostingLevel.None, DoseField.ClassifyArea(plant, RadiationAreas.ControlRoomId));
            Assert.True(DoseField.ClassifyArea(plant, RadiationAreas.ReactorHallId) >= PostingLevel.RadiationArea);
            Assert.True(DoseField.ClassifyArea(plant, RadiationAreas.ReactorPoolId) >= PostingLevel.HighRadiationArea);
        }

        [Fact]
        public void ClassifyArea_UnknownArea_IsUnavailable_NotNone()
        {
            Assert.Equal(PostingLevel.Unavailable, DoseField.ClassifyArea(AtPower(), "room.nowhere"));
            Assert.Equal(PostingLevel.Unavailable, DoseField.ClassifyArea(AtPower(), null));
        }

        [Fact]
        public void Classification_AtExactlyEachThreshold_UsesGreaterThanWording()
        {
            Assert.Equal(PostingLevel.None, DoseField.Classify(Physics.RadAreaThresholdMSvPerH));
            Assert.Equal(PostingLevel.RadiationArea, DoseField.Classify(Next(Physics.RadAreaThresholdMSvPerH)));
            Assert.Equal(PostingLevel.RadiationArea, DoseField.Classify(Physics.RadHighAreaThresholdMSvPerH));
            Assert.Equal(PostingLevel.HighRadiationArea, DoseField.Classify(Next(Physics.RadHighAreaThresholdMSvPerH)));
            Assert.Equal(PostingLevel.HighRadiationArea, DoseField.Classify(Physics.RadVeryHighAreaThresholdMSvPerH));
            Assert.Equal(PostingLevel.VeryHighRadiationArea, DoseField.Classify(Next(Physics.RadVeryHighAreaThresholdMSvPerH)));
            Assert.Equal(PostingLevel.None, DoseField.Classify(0.0));
        }

        [Fact]
        public void Classification_OfBadRates_IsUnavailable()
        {
            Assert.Equal(PostingLevel.Unavailable, DoseField.Classify(double.NaN));
            Assert.Equal(PostingLevel.Unavailable, DoseField.Classify(double.PositiveInfinity));
            Assert.Equal(PostingLevel.Unavailable, DoseField.Classify(-0.1));
            Assert.Equal(PostingLevel.Unavailable, DoseField.Classify(DoseReading.Unavailable(DoseUnavailableReason.UnknownArea)));
        }

        private static double Next(double v) { return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(v) + 1); }

        // ---- source strengths follow the plant (R2, KTD1) ----

        [Fact]
        public void AfterATrip_N16IsZero_AndTheCoreFollowsDecayHeat()
        {
            var plant = Tripped();
            Assert.True(plant.Scrammed);
            var state = RadiationPlantState.FromPlant(plant);
            var n16 = SourceRegister.Default.Find(SourceRegister.PrimaryN16Id);
            var core = SourceRegister.Default.Find(SourceRegister.CoreId);
            Assert.Equal(0.0, n16.Strength(state));
            Assert.True(plant.Core.DecayHeatPower > 0.0);
            Assert.Equal(Physics.RadCoreResidualMSvPerHM2PerUnit * plant.Core.DecayHeatPower, core.Strength(state), 12);

            double before = core.Strength(state);
            for (int i = 0; i < 600; i++) plant.Step(1.0);
            Assert.True(core.Strength(RadiationPlantState.FromPlant(plant)) < before);
        }

        [Fact]
        public void AtPower_N16AndCoreScaleWithPowerAndFlow()
        {
            var n16 = SourceRegister.Default.Find(SourceRegister.PrimaryN16Id);
            var core = SourceRegister.Default.Find(SourceRegister.CoreId);
            double full = n16.Strength(new RadiationPlantState(1.0, 0.06, 1.0, false));
            Assert.Equal(Physics.RadN16MSvPerHM2PerUnit, full, 12);
            Assert.Equal(0.5 * full, n16.Strength(new RadiationPlantState(0.5, 0.06, 1.0, false)), 12);
            Assert.Equal(0.5 * full, n16.Strength(new RadiationPlantState(1.0, 0.06, 0.5, false)), 12);
            Assert.Equal(0.0, n16.Strength(new RadiationPlantState(1.0, 0.06, 0.0, false)));
            Assert.Equal(Physics.RadCoreOperatingMSvPerHM2PerUnit + Physics.RadCoreResidualMSvPerHM2PerUnit * 0.06,
                core.Strength(new RadiationPlantState(1.0, 0.06, 1.0, false)), 9);
        }

        [Fact]
        public void CorrosionStrength_IsConstantAcrossPlantStates()
        {
            var c = SourceRegister.Default.Find(SourceRegister.CorrosionId);
            double a = c.Strength(RadiationPlantState.FromPlant(AtPower()));
            Assert.Equal(Physics.RadCorrosionMSvPerHM2, a);
            Assert.Equal(a, c.Strength(RadiationPlantState.FromPlant(Cold())));
            Assert.Equal(a, c.Strength(RadiationPlantState.FromPlant(Tripped())));
        }

        [Fact]
        public void ColdShutDownPlant_StillReadsAboveBackgroundInTheHall_BecauseOfCorrosionProducts()
        {
            var plant = Cold();
            var state = RadiationPlantState.FromPlant(plant);
            Assert.True(SourceRegister.Default.Find(SourceRegister.PrimaryN16Id).Strength(state) < 0.01);   // source-level power only
            var r = AtReference(plant, RadiationAreas.ReactorHallId);
            Assert.True(r.Available);
            Assert.True(r.RateMSvPerH > Physics.RadBackgroundMSvPerH);
        }

        [Fact]
        public void Evaluation_DoesNotChangeThePlant()
        {
            var plant = AtPower();
            string before = plant.CaptureState().ToJson();
            foreach (string area in RadiationAreas.AllAreaIds) AtReference(plant, area);
            Assert.Equal(before, plant.CaptureState().ToJson());
        }

        // ---- availability (R5, R6) ----

        [Fact]
        public void UnknownArea_IsUnavailable_WithUnknownAreaReason()
        {
            foreach (string id in new[] { "room.nowhere", "", null, "ROOM.CONTROL", "route.deck_rim" })
            {
                var r = DoseField.Evaluate(AtPower(), id, 1, 1, 1);
                Assert.False(r.Available);
                Assert.Equal(DoseUnavailableReason.UnknownArea, r.Reason);
            }
        }

        [Fact]
        public void NonFinitePlantInput_IsUnavailable_WithNonFiniteInputReason()
        {
            double nan = double.NaN, inf = double.PositiveInfinity;
            var cases = new[]
            {
                new RadiationPlantState(nan, 0.0, 1.0, false),
                new RadiationPlantState(1.0, 0.0, nan, false),
                new RadiationPlantState(1.0, nan, 1.0, false),
                new RadiationPlantState(inf, 0.0, 1.0, false),
                new RadiationPlantState(1.0, 0.0, -inf, false)
            };
            foreach (var s in cases)
            {
                var r = DoseField.Evaluate(SourceRegister.Default, RadiationAreas.DefaultBarriers, s, RadiationAreas.ControlRoomId, 1, 1, 1);
                Assert.False(r.Available);
                Assert.Equal(DoseUnavailableReason.NonFiniteInput, r.Reason);
            }
        }

        [Fact]
        public void NaNFlowOnARealPlant_IsUnavailable()
        {
            var plant = AtPower();
            plant.Thermal.PrimaryFlowFrac = double.NaN;
            var r = DoseField.Evaluate(plant, RadiationAreas.ControlRoomId, 1, 1, 1);
            Assert.False(r.Available);
            Assert.Equal(DoseUnavailableReason.NonFiniteInput, r.Reason);
        }

        [Fact]
        public void NonFiniteOrOutOfRangePoint_IsUnavailable_WithOutOfBoundsReason()
        {
            double[][] bad =
            {
                new[] { double.NaN, 0.0, 0.0 }, new[] { 0.0, double.PositiveInfinity, 0.0 }, new[] { 0.0, 0.0, double.NegativeInfinity },
                new[] { 1000.5, 0.0, 0.0 }, new[] { 0.0, -1000.5, 0.0 }, new[] { 0.0, 0.0, 1e9 }
            };
            foreach (var p in bad)
            {
                var r = DoseField.Evaluate(AtPower(), RadiationAreas.ReactorHallId, p[0], p[1], p[2]);
                Assert.False(r.Available);
                Assert.Equal(DoseUnavailableReason.OutOfBounds, r.Reason);
            }
            Assert.True(DoseField.Evaluate(AtPower(), RadiationAreas.ReactorHallId, 1000.0, -1000.0, 1000.0).Available);
        }

        [Fact]
        public void UnavailableReading_NeverCarriesAUsableRate()
        {
            var r = DoseField.Evaluate(AtPower(), "room.nowhere", 0, 0, 0);
            Assert.False(r.Available);
            Assert.True(double.IsNaN(r.RateMSvPerH));
        }

        [Fact]
        public void EveryAvailableReading_IsAtLeastBackground_AndFinite()
        {
            foreach (var plant in new[] { AtPower(), Tripped(), Cold() })
                foreach (string area in RadiationAreas.AllAreaIds)
                    for (double x = -40; x <= 40; x += 8)
                        for (double y = -40; y <= 40; y += 8)
                            for (double h = -10; h <= 30; h += 10)
                            {
                                var r = DoseField.Evaluate(plant, area, x, y, h);
                                Assert.True(r.Available);
                                Assert.True(double.IsFinite(r.RateMSvPerH));
                                Assert.True(r.RateMSvPerH >= Physics.RadBackgroundMSvPerH);
                            }
        }

        [Fact]
        public void RateIsClampedToTheBound()
        {
            var reg = new SourceRegister(new[] { Constant("t.huge", RadiationAreas.ReactorHallId, 0, 0, 0, 1e300) });
            var r = DoseField.Evaluate(reg, OpenBarriers(), new RadiationPlantState(1.0, 0.0, 1.0, false), RadiationAreas.ReactorHallId, 0, 0, 0);
            Assert.True(r.Available);
            Assert.Equal(Physics.RadMaxRateMSvPerH, r.RateMSvPerH);
        }

        [Fact]
        public void MissingBarrierPair_IsUnavailable_NotUnshielded()
        {
            var reg = new SourceRegister(new[] { Constant("t.src", RadiationAreas.ReactorPoolId, 0, 0, 0, 10.0) });
            var table = new BarrierTable();
            table.Set(RadiationAreas.ReactorPoolId, RadiationAreas.ReactorPoolId, None);
            var r = DoseField.Evaluate(reg, table, new RadiationPlantState(1.0, 0.0, 1.0, false), RadiationAreas.ReactorHallId, 1, 0, 0);
            Assert.False(r.Available);
            Assert.Equal(DoseUnavailableReason.MissingBarrier, r.Reason);
        }

        [Fact]
        public void SourceWithNonFiniteStrength_IsUnavailable_NotSkipped()
        {
            var reg = new SourceRegister(new[] { Constant("t.nan", RadiationAreas.ReactorHallId, 0, 0, 0, double.NaN) });
            var r = DoseField.Evaluate(reg, OpenBarriers(), new RadiationPlantState(1.0, 0.0, 1.0, false), RadiationAreas.ReactorHallId, 1, 0, 0);
            Assert.False(r.Available);
            Assert.Equal(DoseUnavailableReason.NonFiniteInput, r.Reason);
        }

        // ---- register validation (KTD5, KTD6) ----

        [Fact]
        public void DefaultRegisterAndBarrierTable_HaveNoProblems()
        {
            Assert.Empty(SourceRegister.Default.Validate(RadiationAreas.DefaultBarriers));
            Assert.Empty(RadiationAreas.DefaultBarriers.Validate());
        }

        [Fact]
        public void DefaultRegister_HoldsTheThreeKtd2Sources_InFixedOrder()
        {
            var ids = new List<string>();
            foreach (var s in SourceRegister.Default.Sources) ids.Add(s.Id);
            Assert.Equal(new[] { "rad.core", "rad.primary_n16", "rad.corrosion" }, ids.ToArray());
            Assert.Equal(RadiationAreas.ReactorPoolId, SourceRegister.Default.Find("rad.core").AreaId);
            Assert.Equal(RadiationAreas.ReactorHallId, SourceRegister.Default.Find("rad.primary_n16").AreaId);
            Assert.Equal(RadiationAreas.ReactorHallId, SourceRegister.Default.Find("rad.corrosion").AreaId);
            Assert.Null(SourceRegister.Default.Find("rad.nothing"));
        }

        [Fact]
        public void EverySourceAreaAndSubjectAreaPair_IsInTheDefaultBarrierTable()
        {
            foreach (var s in SourceRegister.Default.Sources)
                foreach (string subj in RadiationAreas.AllAreaIds)
                {
                    Slab[] slabs;
                    Assert.True(RadiationAreas.DefaultBarriers.TryGet(s.AreaId, subj, out slabs), s.AreaId + " -> " + subj);
                }
        }

        [Fact]
        public void RegisterWithAMissingBarrierPair_ReportsThatPair()
        {
            var table = RadiationAreas.DefaultBarriers.Clone();
            table.Remove(RadiationAreas.ReactorPoolId, RadiationAreas.ControlRoomId);
            var problems = SourceRegister.Default.Validate(table);
            Assert.Contains(problems, p => p.Contains(RadiationAreas.ReactorPoolId) && p.Contains(RadiationAreas.ControlRoomId));
            Assert.Single(problems);
        }

        [Fact]
        public void Validation_FlagsDuplicateIds_UnknownAreas_AndBadNumbers()
        {
            var open = OpenBarriers();
            var dup = new SourceRegister(new[]
            {
                Constant("t.a", RadiationAreas.ReactorHallId, 0, 0, 0, 1.0),
                Constant("t.a", RadiationAreas.ReactorHallId, 0, 0, 0, 1.0)
            });
            Assert.Contains(dup.Validate(open), p => p.Contains("t.a") && p.Contains("duplicate"));

            var unknown = new SourceRegister(new[] { Constant("t.b", "room.nowhere", 0, 0, 0, 1.0) });
            Assert.Contains(unknown.Validate(open), p => p.Contains("room.nowhere"));

            var negative = new SourceRegister(new[] { Constant("t.c", RadiationAreas.ReactorHallId, 0, 0, 0, -1.0) });
            Assert.NotEmpty(negative.Validate(open));

            var nanPos = new SourceRegister(new[] { Constant("t.d", RadiationAreas.ReactorHallId, double.NaN, 0, 0, 1.0) });
            Assert.NotEmpty(nanPos.Validate(open));

            var noId = new SourceRegister(new[] { Constant("", RadiationAreas.ReactorHallId, 0, 0, 0, 1.0) });
            Assert.NotEmpty(noId.Validate(open));
        }

        [Fact]
        public void BarrierValidation_FlagsBadThicknessAndUnknownAreas()
        {
            var t = OpenBarriers();
            t.Set(RadiationAreas.ReactorHallId, RadiationAreas.ControlRoomId, new[] { new Slab(RadMaterial.Concrete, -0.1) });
            Assert.NotEmpty(t.Validate());
            t.Set(RadiationAreas.ReactorHallId, RadiationAreas.ControlRoomId, new[] { new Slab(RadMaterial.Concrete, double.NaN) });
            Assert.NotEmpty(t.Validate());
            t.Set(RadiationAreas.ReactorHallId, RadiationAreas.ControlRoomId, None);
            t.Set("room.nowhere", RadiationAreas.ControlRoomId, None);
            Assert.Contains(t.Validate(), p => p.Contains("room.nowhere"));
        }

        [Fact]
        public void BarrierTable_SetReplaces_RemoveDeletes_CloneIsIndependent()
        {
            var t = new BarrierTable();
            t.Set("a", "b", None);
            t.Set("a", "b", new[] { new Slab(RadMaterial.Lead, 0.01) });
            Slab[] s;
            Assert.True(t.TryGet("a", "b", out s));
            Assert.Single(s);
            var c = t.Clone();
            Assert.True(t.Remove("a", "b"));
            Assert.False(t.Remove("a", "b"));
            Assert.False(t.TryGet("a", "b", out s));
            Assert.True(c.TryGet("a", "b", out s));
            Assert.False(t.TryGet(null, "b", out s));
        }

        [Fact]
        public void PhysicsConstants_AreOrderedLikeThePostingRegulations()
        {
            Assert.True(Physics.RadBackgroundMSvPerH > 0.0);
            Assert.True(Physics.RadBackgroundMSvPerH < Physics.RadAreaThresholdMSvPerH);
            Assert.True(Physics.RadAreaThresholdMSvPerH < Physics.RadHighAreaThresholdMSvPerH);
            Assert.True(Physics.RadHighAreaThresholdMSvPerH < Physics.RadVeryHighAreaThresholdMSvPerH);
            Assert.Equal(0.3, Physics.RadSoftenRadiusM);
        }

        // ---- determinism (R12) ----

        [Fact]
        public void IdenticalEvaluations_AreBitIdentical()
        {
            foreach (string area in RadiationAreas.AllAreaIds)
            {
                var a = AtReference(Tripped(), area);
                var b = AtReference(Tripped(), area);
                Assert.Equal(BitConverter.DoubleToInt64Bits(a.RateMSvPerH), BitConverter.DoubleToInt64Bits(b.RateMSvPerH));
                Assert.Equal(a.Available, b.Available);
            }
        }
    }
}
