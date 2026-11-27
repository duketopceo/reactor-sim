using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace ReactorSim.Tests
{
    // Phase 4 U4: the RAD-01 / RAD-02 verification checks and the R4 provenance rule for radiation constants.
    public class RadiationVerificationTests
    {
        private static readonly string[] Citations = { "http", "CFR", "DOE", "NIST", "NRC", "OSHA", "IAEA", "ICRP" };

        [Fact]
        public void Rad01_PassesOnShippedModel()
        {
            Assert.Null(RadiationChecks.AllAreasAvailable());
        }

        [Fact]
        public void Rad02_PassesOnShippedModel()
        {
            Assert.Null(RadiationChecks.DoseInvariantToTimeScale());
        }

        [Fact]
        public void Rad01_FailsWithMessage_WhenInputIsNaN()
        {
            var bad = new RadiationPlantState(double.NaN, 0.0, 1.0, false);
            string msg = RadiationChecks.AreaRates("forced", SourceRegister.Default, RadiationAreas.DefaultBarriers, bad);
            Assert.NotNull(msg);
            Assert.Contains("forced", msg);
            Assert.Contains("NonFiniteInput", msg);
            Assert.Contains(RadiationAreas.ReactorDeckId, msg);
        }

        [Fact]
        public void Rad01_FailsWithMessage_WhenBarrierPairIsMissing()
        {
            var barriers = RadiationAreas.DefaultBarriers.Clone();
            Assert.True(barriers.Remove(RadiationAreas.ReactorPoolId, RadiationAreas.ReactorDeckId));
            var state = new RadiationPlantState(1.0, 0.05, 1.0, false);
            string msg = RadiationChecks.AreaRates("gap", SourceRegister.Default, barriers, state);
            Assert.NotNull(msg);
            Assert.Contains("MissingBarrier", msg);
        }

        [Fact]
        public void Rad01_PassesForFiniteState_WithShippedTables()
        {
            var state = new RadiationPlantState(0.0, 0.0, 0.0, true);
            Assert.Null(RadiationChecks.AreaRates("idle", SourceRegister.Default, RadiationAreas.DefaultBarriers, state));
        }

        // Covers R4: every radiation constant cites a public source or says "gameplay choice".
        [Fact]
        public void EveryRadiationConstant_CitesASourceOrIsLabelledAGameplayChoice()
        {
            string[] lines = File.ReadAllLines(TestRepo.Find("ReactorSim.Core", "Physics.cs"));
            int start = IndexOf(lines, "// ---- Radiation");
            int end = IndexOf(lines, "public static double Clamp");
            Assert.True(start >= 0 && end > start, "radiation section markers not found in Physics.cs");

            var unlabelled = new List<string>();
            int consts = 0;
            for (int i = start; i < end; i++)
            {
                if (!lines[i].Contains(" const ")) continue;
                consts++;
                string block = CommentBlock(lines, i, start);
                if (!IsLabelled(block)) unlabelled.Add(lines[i].Trim());
            }
            Assert.True(consts >= 20, "expected the radiation section to hold its constants, found " + consts);
            Assert.True(unlabelled.Count == 0, "constants with neither a citation nor 'gameplay choice':\n" + string.Join("\n", unlabelled));
        }

        [Fact]
        public void Provenance_Detector_RejectsUnlabelledAndAcceptsLabelled()
        {
            Assert.False(IsLabelled("// a plain remark"));
            Assert.True(IsLabelled("// Gameplay Choice: tuned"));
            Assert.True(IsLabelled("// 10 CFR 20.1003"));
        }

        private static int IndexOf(string[] lines, string marker)
        {
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Contains(marker)) return i;
            return -1;
        }

        private static string CommentBlock(string[] lines, int constLine, int sectionStart)
        {
            string text = "";
            string line = lines[constLine];
            int slash = line.IndexOf("//", StringComparison.Ordinal);
            if (slash >= 0) text += line.Substring(slash) + "\n";
            for (int j = constLine - 1; j >= sectionStart; j--)
            {
                string t = lines[j].Trim();
                if (t.Length == 0 || !t.StartsWith("//", StringComparison.Ordinal)) break;
                text += t + "\n";
            }
            return text;
        }

        private static bool IsLabelled(string block)
        {
            if (block.IndexOf("gameplay choice", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (string c in Citations)
                if (block.Contains(c)) return true;
            return false;
        }
    }
}
