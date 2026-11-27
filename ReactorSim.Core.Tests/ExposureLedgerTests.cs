using System;
using Xunit;

namespace ReactorSim.Tests
{
    // Phase 4 U2: the per-operator exposure ledger (R7-R9, R12, KTD7, KTD10).
    public class ExposureLedgerTests
    {
        private const string Deck = RadiationAreas.ReactorDeckId;

        private static Plant AtPower()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            return p;
        }

        private static Plant Tripped()
        {
            var p = AtPower();
            p.Apply(PlantIntent.TriggerScram());
            for (int i = 0; i < 5; i++) p.Step(1.0);
            return p;
        }

        private static double DeckRate(Plant p)
        {
            RadPoint pt;
            Assert.True(RadiationAreas.TryGetReferencePoint(Deck, out pt));
            return DoseField.Evaluate(p, Deck, pt.X, pt.Y, pt.H).RateMSvPerH;
        }

        private static ExposureLedger OnDeck(string id = "op1")
        {
            RadPoint pt;
            RadiationAreas.TryGetReferencePoint(Deck, out pt);
            var l = new ExposureLedger();
            Assert.Equal(LedgerRefusal.None, l.SetLocation(id, Deck, pt.X, pt.Y, pt.H));
            return l;
        }

        [Fact]
        public void Step_AddsRateTimesDtInHours()
        {
            var plant = AtPower();
            var l = OnDeck();
            l.Step(plant, 5.0);
            ExposureSubject s;
            Assert.True(l.TryGet("op1", out s));
            Assert.Equal(DeckRate(plant) * 5.0 / 3600.0, s.DoseMSv, 12);
            Assert.Equal(DeckRate(plant), s.PeakRateMSvPerH, 12);
            Assert.Equal(0.0, s.UnavailableSeconds);
        }

        [Fact]
        public void PerTickDose_FallsAfterATrip()
        {
            var l = OnDeck();
            l.Step(AtPower(), 5.0);
            ExposureSubject s;
            l.TryGet("op1", out s);
            double before = s.DoseMSv;
            l.Step(Tripped(), 5.0);
            double after = s.DoseMSv - before;
            Assert.True(after > 0.0);
            Assert.True(after < before, "tripped " + after + " vs power " + before);
        }

        [Fact]
        public void UnknownArea_AddsUnavailableTime_NeverDose()
        {
            var l = new ExposureLedger();
            Assert.Equal(LedgerRefusal.None, l.SetLocation("op1", "room.nowhere", 0, 0, 0));
            var plant = AtPower();
            l.Step(plant, 5.0);
            l.Step(plant, 5.0);
            ExposureSubject s;
            l.TryGet("op1", out s);
            Assert.Equal(0.0, s.DoseMSv);
            Assert.Equal(10.0, s.UnavailableSeconds);
            Assert.Equal(0.0, s.PeakRateMSvPerH);
        }

        [Fact]
        public void NoLocation_AddsUnavailableTime_AndClearLocationReturnsToIt()
        {
            var l = OnDeck();
            var plant = AtPower();
            l.Step(plant, 5.0);
            ExposureSubject s;
            l.TryGet("op1", out s);
            double dose = s.DoseMSv;
            Assert.True(l.ClearLocation("op1"));
            Assert.False(s.HasLocation);
            l.Step(plant, 5.0);
            Assert.Equal(dose, s.DoseMSv);
            Assert.Equal(5.0, s.UnavailableSeconds);
            Assert.False(l.ClearLocation("ghost"));
        }

        [Fact]
        public void InvalidSetLocation_KeepsPreviousLocation_AndRefusesWithReason()
        {
            var l = OnDeck();
            ExposureSubject s;
            l.TryGet("op1", out s);
            double x = s.X, y = s.Y, h = s.H;
            Assert.Equal(LedgerRefusal.NonFiniteCoordinate, l.SetLocation("op1", RadiationAreas.ControlRoomId, double.NaN, 0, 0));
            Assert.Equal(LedgerRefusal.NonFiniteCoordinate, l.SetLocation("op1", RadiationAreas.ControlRoomId, 0, double.PositiveInfinity, 0));
            Assert.Equal(LedgerRefusal.OutOfBounds, l.SetLocation("op1", RadiationAreas.ControlRoomId, 0, 0, Physics.RadMaxCoordinateM + 1.0));
            Assert.Equal(LedgerRefusal.InvalidArea, l.SetLocation("op1", null, 0, 0, 0));
            Assert.Equal(Deck, s.AreaId);
            Assert.Equal(x, s.X); Assert.Equal(y, s.Y); Assert.Equal(h, s.H);
        }

        [Fact]
        public void CoordinateBound_IsInclusive()
        {
            var l = new ExposureLedger();
            double m = Physics.RadMaxCoordinateM;
            Assert.Equal(LedgerRefusal.None, l.SetLocation("a", Deck, m, -m, m));
        }

        [Fact]
        public void RefusedSetLocation_DoesNotRegisterANewSubject()
        {
            var l = new ExposureLedger();
            Assert.Equal(LedgerRefusal.NonFiniteCoordinate, l.SetLocation("new", Deck, double.NaN, 0, 0));
            Assert.Equal(0, l.Count);
        }

        [Fact]
        public void SeventeenthSubject_IsRefused_ExistingSubjectsStillUpdate()
        {
            var l = new ExposureLedger();
            for (int i = 0; i < Physics.RadMaxSubjects; i++)
                Assert.Equal(LedgerRefusal.None, l.SetLocation("op" + i.ToString("00"), Deck, 0, 0, 0));
            Assert.Equal(LedgerRefusal.TooManySubjects, l.SetLocation("extra", Deck, 0, 0, 0));
            Assert.Equal(Physics.RadMaxSubjects, l.Count);
            Assert.Equal(LedgerRefusal.None, l.SetLocation("op00", Deck, 1, 1, 1));
        }

        [Fact]
        public void BadSubjectIds_AreRefused()
        {
            var l = new ExposureLedger();
            Assert.Equal(LedgerRefusal.InvalidSubjectId, l.SetLocation(null, Deck, 0, 0, 0));
            Assert.Equal(LedgerRefusal.InvalidSubjectId, l.SetLocation("", Deck, 0, 0, 0));
            Assert.Equal(LedgerRefusal.InvalidSubjectId, l.SetLocation(new string('a', 65), Deck, 0, 0, 0));
            Assert.Equal(LedgerRefusal.InvalidSubjectId, l.SetLocation("a\nb", Deck, 0, 0, 0));
            Assert.Equal(LedgerRefusal.InvalidSubjectId, l.SetLocation("a\u007fb", Deck, 0, 0, 0));
            Assert.Equal(LedgerRefusal.None, l.SetLocation(new string('a', 64), Deck, 0, 0, 0));
            Assert.Equal(1, l.Count);
        }

        [Fact]
        public void Subjects_AreSortedByOrdinalId_RegardlessOfInsertionOrder()
        {
            var l = new ExposureLedger();
            foreach (string id in new[] { "b", "B", "a", "_", "A" }) l.SetLocation(id, Deck, 0, 0, 0);
            var ids = new System.Collections.Generic.List<string>();
            foreach (var s in l.Subjects) ids.Add(s.Id);
            Assert.Equal(new[] { "A", "B", "_", "a", "b" }, ids.ToArray());
        }

        [Fact]
        public void PeakRate_IsMonotone()
        {
            var l = OnDeck();
            l.Step(AtPower(), 5.0);
            ExposureSubject s;
            l.TryGet("op1", out s);
            double peak = s.PeakRateMSvPerH;
            l.Step(Tripped(), 5.0);
            Assert.Equal(peak, s.PeakRateMSvPerH);
            l.Step(AtPower(), 5.0);
            Assert.Equal(peak, s.PeakRateMSvPerH, 12);
        }

        [Fact]
        public void Step_RejectsBadDt_WithoutChangingAnything()
        {
            var plant = AtPower();
            var l = OnDeck();
            l.Step(plant, 5.0);
            ExposureSubject s;
            l.TryGet("op1", out s);
            double dose = s.DoseMSv;
            foreach (double bad in new[] { double.NaN, -1.0, Plant.MaxStepSeconds + 1.0, double.PositiveInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() => l.Step(plant, bad));
            Assert.Equal(dose, s.DoseMSv);
            l.Step(plant, 0.0);
            Assert.Equal(dose, s.DoseMSv);
        }

        [Fact]
        public void Step_NullPlant_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => OnDeck().Step(null, 1.0));
        }

        [Fact]
        public void Dose_IsClampedAtTheBound()
        {
            var l = OnDeck();
            var plant = AtPower();
            for (int i = 0; i < 200; i++) l.Step(plant, Plant.MaxStepSeconds);
            ExposureSubject s;
            l.TryGet("op1", out s);
            Assert.True(s.DoseMSv <= Physics.RadMaxDoseMSv);
            Assert.True(Physics.IsFinite(s.DoseMSv));
        }

        [Fact]
        public void NonFinitePlantInput_AddsUnavailableTime_NotDose()
        {
            var plant = AtPower();
            plant.Thermal.PrimaryFlowFrac = double.NaN;
            var l = OnDeck();
            l.Step(plant, 5.0);
            ExposureSubject s;
            l.TryGet("op1", out s);
            Assert.Equal(0.0, s.DoseMSv);
            Assert.Equal(5.0, s.UnavailableSeconds);
        }

        [Fact]
        public void Clear_EmptiesTheLedger()
        {
            var l = OnDeck();
            l.Clear();
            Assert.Equal(0, l.Count);
            ExposureSubject s;
            Assert.False(l.TryGet("op1", out s));
        }

        [Fact]
        public void TwoIdenticalRuns_GiveIdenticalLedgers()
        {
            Func<ExposureLedger> run = () =>
            {
                var l = OnDeck("op1");
                l.SetLocation("op2", RadiationAreas.ControlRoomId, 1, 2, 1);
                l.SetLocation("op0", "nowhere", 0, 0, 0);
                var p = AtPower();
                for (int i = 0; i < 50; i++) { p.Step(5.0); l.Step(p, 5.0); }
                return l;
            };
            var a = run(); var b = run();
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a.Subjects[i].Id, b.Subjects[i].Id);
                Assert.Equal(BitConverter.DoubleToInt64Bits(a.Subjects[i].DoseMSv), BitConverter.DoubleToInt64Bits(b.Subjects[i].DoseMSv));
                Assert.Equal(a.Subjects[i].UnavailableSeconds, b.Subjects[i].UnavailableSeconds);
            }
        }
    }
}
