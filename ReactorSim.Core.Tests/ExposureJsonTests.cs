using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ReactorSim.Tests
{
    // Phase 4 U3: versioned ledger JSON (R11, R12, KTD9, KTD10) and the shared strict parser.
    public class ExposureJsonTests
    {
        private const string Deck = RadiationAreas.ReactorDeckId;

        private static string Doc(string subjects) { return "{\"schema\":1,\"subjects\":[" + subjects + "]}"; }

        private static string Subj(string id, string dose = "1", string peak = "2", string unavail = "3")
        {
            return "{\"id\":\"" + id + "\",\"doseMSv\":" + dose + ",\"peakRateMSvPerH\":" + peak + ",\"unavailableSeconds\":" + unavail + "}";
        }

        private static ExposureLedger TwoSubjects()
        {
            var plant = RadiationTestPlants.AtPower();
            RadPoint pt;
            Assert.True(RadiationAreas.TryGetReferencePoint(Deck, out pt));
            var l = new ExposureLedger();
            Assert.Equal(LedgerRefusal.None, l.SetLocation("zed", Deck, pt.X, pt.Y, pt.H));
            l.Step(plant, 0.1);
            Assert.Equal(LedgerRefusal.None, l.SetLocation("alice", Deck, pt.X, pt.Y, pt.H));
            l.Step(plant, 0.1);
            l.Step(plant, 0.1);
            l.ClearLocation("zed");
            l.Step(plant, 0.1);
            return l;
        }

        private static void AssertRefused(string json, LedgerLoadReason reason)
        {
            var r = ExposureLedger.FromJson(json);
            Assert.False(r.Ok);
            Assert.Null(r.Ledger);
            Assert.Equal(reason, r.Reason);
        }

        [Fact]
        public void Covers_AE4_TwoSubjectsRoundTripToIdenticalBytes()
        {
            var l = TwoSubjects();
            string a = l.ToJson();
            var r = ExposureLedger.FromJson(a);
            Assert.True(r.Ok);
            Assert.Equal(a, r.Ledger.ToJson());
            Assert.StartsWith("{\"schema\":1,\"subjects\":[{\"id\":\"alice\"", a);
            ExposureSubject s;
            Assert.True(r.Ledger.TryGet("zed", out s));
            Assert.True(s.UnavailableSeconds > 0.0);
        }

        [Fact]
        public void ToJson_EmptyLedgerAndTwoRunsAreIdentical()
        {
            Assert.Equal("{\"schema\":1,\"subjects\":[]}", new ExposureLedger().ToJson());
            Assert.Equal(TwoSubjects().ToJson(), TwoSubjects().ToJson());
        }

        [Fact]
        public void ToJson_AfterFromJson_EqualsCanonicalInput()
        {
            string canon = Doc(Subj("a", "0.5", "12.25", "3") + "," + Subj("b", "0", "0", "0"));
            Assert.Equal(canon, ExposureLedger.FromJson(canon).Ledger.ToJson());
        }

        [Fact]
        public void Covers_AE4_Schema2IsUnsupported()
        {
            AssertRefused("{\"schema\":2,\"subjects\":[]}", LedgerLoadReason.UnsupportedSchema);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   \n")]
        public void NullAndEmptyLoadAsEmptyLedger(string json)
        {
            var r = ExposureLedger.FromJson(json);
            Assert.True(r.Ok);
            Assert.Equal(LedgerLoadReason.None, r.Reason);
            Assert.Equal(0, r.Ledger.Count);
        }

        [Fact]
        public void MissingSchemaIsRefused()
        {
            AssertRefused("{\"subjects\":[]}", LedgerLoadReason.MissingSchema);
        }

        [Fact]
        public void NonObjectTopLevelIsRefused()
        {
            AssertRefused("[]", LedgerLoadReason.WrongType);
            AssertRefused("{\"schema\":\"1\",\"subjects\":[]}", LedgerLoadReason.WrongType);
        }

        [Fact]
        public void DuplicateKeyIsRefused()
        {
            AssertRefused("{\"schema\":1,\"schema\":1,\"subjects\":[]}", LedgerLoadReason.Parse);
        }

        [Fact]
        public void UnknownKeyIsRefusedAtBothLevels()
        {
            AssertRefused("{\"schema\":1,\"subjects\":[],\"extra\":1}", LedgerLoadReason.UnknownKey);
            AssertRefused(Doc("{\"id\":\"a\",\"doseMSv\":1,\"peakRateMSvPerH\":2,\"unavailableSeconds\":3,\"x\":1}"), LedgerLoadReason.UnknownKey);
        }

        [Fact]
        public void MissingSubjectFieldIsRefused()
        {
            AssertRefused(Doc("{\"id\":\"a\",\"doseMSv\":1,\"peakRateMSvPerH\":2}"), LedgerLoadReason.WrongType);
        }

        [Fact]
        public void DuplicateSubjectIdIsRefused()
        {
            AssertRefused(Doc(Subj("a") + "," + Subj("a")), LedgerLoadReason.DuplicateSubject);
        }

        [Fact]
        public void NegativeValuesAreRefused()
        {
            AssertRefused(Doc(Subj("a", "-1")), LedgerLoadReason.Negative);
            AssertRefused(Doc(Subj("a", "1", "-1")), LedgerLoadReason.Negative);
            AssertRefused(Doc(Subj("a", "1", "1", "-1")), LedgerLoadReason.Negative);
        }

        [Fact]
        public void NaNStringAndOverflowingExponentAreRefused()
        {
            AssertRefused(Doc(Subj("a", "\"NaN\"")), LedgerLoadReason.WrongType);
            AssertRefused(Doc(Subj("a", "1e999")), LedgerLoadReason.NonFinite);
        }

        [Fact]
        public void ValuesAboveBoundsAreRefused()
        {
            AssertRefused(Doc(Subj("a", "10000001")), LedgerLoadReason.OutOfBounds);
            AssertRefused(Doc(Subj("a", "1", "100001")), LedgerLoadReason.OutOfBounds);
            AssertRefused(Doc(Subj("a", "1", "1", "10000000001")), LedgerLoadReason.OutOfBounds);
            Assert.True(ExposureLedger.FromJson(Doc(Subj("a", "1", "1", "10000000000"))).Ok);
            Assert.True(ExposureLedger.FromJson(Doc(Subj("a", "10000000", "100000", "10000000"))).Ok);
        }

        [Fact]
        public void SeventeenSubjectsAreRefusedAndSixteenLoad()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 17; i++) { if (i > 0) sb.Append(','); sb.Append(Subj("s" + i.ToString("D2"))); }
            AssertRefused(Doc(sb.ToString()), LedgerLoadReason.TooManySubjects);
            var sb16 = new StringBuilder();
            for (int i = 0; i < 16; i++) { if (i > 0) sb16.Append(','); sb16.Append(Subj("s" + i.ToString("D2"))); }
            Assert.Equal(16, ExposureLedger.FromJson(Doc(sb16.ToString())).Ledger.Count);
        }

        [Fact]
        public void BadSubjectIdsAreRefused()
        {
            AssertRefused(Doc(Subj(new string('x', 65))), LedgerLoadReason.InvalidSubjectId);
            AssertRefused(Doc(Subj("a\\u0007b")), LedgerLoadReason.InvalidSubjectId);
            AssertRefused(Doc(Subj("")), LedgerLoadReason.InvalidSubjectId);
            Assert.True(ExposureLedger.FromJson(Doc(Subj(new string('x', 64)))).Ok);
        }

        [Fact]
        public void TrailingTextIsRefused()
        {
            AssertRefused("{\"schema\":1,\"subjects\":[]} junk", LedgerLoadReason.Parse);
        }

        [Fact]
        public void DeepNestingIsRefusedWithoutCrash()
        {
            string deep = new string('[', 10000) + new string(']', 10000);
            AssertRefused(deep, LedgerLoadReason.Parse);
            AssertRefused("{\"schema\":1,\"subjects\":" + deep + "}", LedgerLoadReason.Parse);
        }

        [Fact]
        public void MalformedUnicodeEscapeIsRefusedAsParse()
        {
            AssertRefused(Doc(Subj("a\\uZZZZ")), LedgerLoadReason.Parse);
            AssertRefused(Doc(Subj("a\\u12")), LedgerLoadReason.Parse);
            AssertRefused("{\"schema\":1,\"subjects\":[{\"id\":\"a\\u", LedgerLoadReason.Parse);
        }

        [Fact]
        public void IdWithQuoteAndBackslashRoundTrips()
        {
            var l = new ExposureLedger();
            Assert.Equal(LedgerRefusal.None, l.SetLocation("a\"b\\c", Deck, 0, 0, 0));
            string j = l.ToJson();
            var r = ExposureLedger.FromJson(j);
            Assert.True(r.Ok);
            ExposureSubject s;
            Assert.True(r.Ledger.TryGet("a\"b\\c", out s));
            Assert.Equal(j, r.Ledger.ToJson());
        }

        [Fact]
        public void LoadedSubjectsHaveNoLocation()
        {
            var l = TwoSubjects();
            foreach (var s in l.Subjects) Assert.Equal(s.Id == "zed" ? false : true, s.HasLocation);
            var r = ExposureLedger.FromJson(l.ToJson());
            foreach (var s in r.Ledger.Subjects) Assert.False(s.HasLocation);
        }

        [Fact]
        public void RefusedLoadThroughSimulationKeepsPreviousLedger()
        {
            var sim = new Simulation(new Tuning());
            Assert.Equal(LedgerRefusal.None, sim.Exposure.SetLocation("keep", Deck, 0, 0, 0));
            Assert.Equal(LedgerLoadReason.UnsupportedSchema, sim.LoadExposure("{\"schema\":2,\"subjects\":[]}"));
            Assert.Equal(1, sim.Exposure.Count);
            ExposureSubject s;
            Assert.True(sim.Exposure.TryGet("keep", out s));
            Assert.True(s.HasLocation);
        }

        [Fact]
        public void AcceptedLoadThroughSimulationReplacesLedger()
        {
            var sim = new Simulation(new Tuning());
            sim.Exposure.SetLocation("old", Deck, 0, 0, 0);
            Assert.Equal(LedgerLoadReason.None, sim.LoadExposure(Doc(Subj("new", "4", "5", "6"))));
            ExposureSubject s;
            Assert.False(sim.Exposure.TryGet("old", out s));
            Assert.True(sim.Exposure.TryGet("new", out s));
            Assert.Equal(4.0, s.DoseMSv);
            Assert.False(s.HasLocation);
            Assert.Equal(LedgerLoadReason.None, sim.LoadExposure(null));
            Assert.Equal(0, sim.Exposure.Count);
        }

        [Fact]
        public void DeepestShippedProceduresDocumentStaysUnderTheCap()
        {
            string json = System.IO.File.ReadAllText(TestRepo.Find("ReactorSim.Unity", "Assets", "StreamingAssets", "procedures.json"));
            int depth = Depth(new MiniJson(json, "procedures.json").ParseDocument());
            Assert.True(depth >= 1);
            Assert.True(depth < MiniJson.MaxDepth, "depth " + depth);
        }

        [Fact]
        public void MiniJson_ParseFailuresCarryThePrefix()
        {
            var ex = Assert.Throws<JsonParseException>(() => new MiniJson("{", "thing").ParseDocument());
            Assert.StartsWith("thing parse error at ", ex.Message);
            Assert.Throws<JsonParseException>(() => new MiniJson(new string('[', MiniJson.MaxDepth + 1), "x").ParseDocument());
            Assert.NotNull(new MiniJson(new string('[', MiniJson.MaxDepth) + new string(']', MiniJson.MaxDepth), "x").ParseDocument());
        }

        [Fact]
        public void LoadExposure_LeadingPlusDose_IsRefusedAsParse_AndLedgerUnchanged()
        {
            var sim = new Simulation(new Tuning());
            sim.Exposure.SetLocation("keep", Deck, 0, 0, 0);
            var reason = sim.LoadExposure("{\"schema\":1,\"subjects\":[{\"id\":\"a\",\"doseMSv\":+1,\"peakRateMSvPerH\":2,\"unavailableSeconds\":3}]}");
            Assert.Equal(LedgerLoadReason.Parse, reason);
            Assert.Equal(1, sim.Exposure.Count);
            ExposureSubject s;
            Assert.True(sim.Exposure.TryGet("keep", out s));
            Assert.True(s.HasLocation);
        }

        [Theory]
        [InlineData("+1")]
        [InlineData("01")]
        [InlineData("1.")]
        [InlineData(".5")]
        [InlineData("-")]
        [InlineData("1e")]
        [InlineData("1e+")]
        public void MiniJson_RejectsNonGrammarNumbers(string token)
        {
            Assert.Throws<JsonParseException>(() => new MiniJson("[" + token + "]", "x").ParseDocument());
        }

        [Theory]
        [InlineData("-0.5", -0.5)]
        [InlineData("1e+3", 1000.0)]
        [InlineData("2E-2", 0.02)]
        [InlineData("0", 0.0)]
        [InlineData("12.25", 12.25)]
        public void MiniJson_AcceptsGrammarNumbers(string token, double expected)
        {
            var list = (System.Collections.Generic.List<object>)new MiniJson("[" + token + "]", "x").ParseDocument();
            Assert.Equal(expected, (double)list[0]);
        }

        [Fact]
        public void LoadedUnavailableSecondsBeyondOldDoseBoundAreAccepted_AndFarBeyondRefused()
        {
            var ok = ExposureLedger.FromJson(Doc(Subj("a", "1", "2", "2e7")));
            Assert.True(ok.Ok);
            ExposureSubject s;
            Assert.True(ok.Ledger.TryGet("a", out s));
            Assert.Equal(2.0e7, s.UnavailableSeconds);
            AssertRefused(Doc(Subj("a", "1", "2", "2e10")), LedgerLoadReason.OutOfBounds);
        }

        private static int Depth(object v)
        {
            int best = 0;
            var d = v as Dictionary<string, object>;
            if (d != null) { foreach (var kv in d) best = Math.Max(best, Depth(kv.Value)); return best + 1; }
            var l = v as List<object>;
            if (l != null) { foreach (var o in l) best = Math.Max(best, Depth(o)); return best + 1; }
            return 0;
        }
    }
}
