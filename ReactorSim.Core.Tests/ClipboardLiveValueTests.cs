using System.Collections.Generic;
using System.Linq;
using ReactorSim.Clipboard;
using Xunit;

namespace ReactorSim.Tests
{
    // The clipboard's live reading must map each shipped predicate to the right snapshot field and format.
    public class ClipboardLiveValueTests
    {
        // A distinct value per field, so a predicate wired to the wrong field cannot produce the expected text.
        private static PlantSnapshot DistinctSnapshot(bool critical, bool autoLoad, bool synced)
        {
            return new PlantSnapshot
            {
                PrimaryFlowFrac = 0.375,
                PrimaryPressureBar = 155.4,
                CondenserPressureBar = 0.123,
                BankInsertion = new[] { 0.11, 0.22, 0.33, 0.44 },
                BoronPpm = 1347.25,
                IsCritical = critical,
                Power = 0.425,
                PrimaryTempC = 251.6,
                BopAutoLoad = autoLoad,
                TurbineRpm = 2987.4,
                GeneratorSynced = synced,
                GridOutputFrac = 0.205,
                SteamDemandFrac = 0.61,
                HeaterPowerFrac = 0.72,
            };
        }

        private static void AssertShippedLiveValues(PlantSnapshot snap, IDictionary<string, string> expected)
        {
            var proc = ProcedureLoader.LoadFromFile(TestRepo.Find("ReactorSim.Unity", "Assets", "StreamingAssets", "procedures.json")).Single();
            Assert.Equal(proc.Steps.Select(s => s.Id).OrderBy(x => x), expected.Keys.OrderBy(x => x));
            foreach (var step in proc.Steps)
                Assert.Equal(expected[step.Id], ClipboardView.LiveValue(step, snap));
        }

        [Fact]
        public void LiveValue_ExactTextForEveryShippedStep_CriticalAutoClosed()
        {
            AssertShippedLiveValues(DistinctSnapshot(critical: true, autoLoad: true, synced: true), new Dictionary<string, string>
            {
                { "start-rcps", "37.5 %" },
                { "pressurize", "155 bar" },
                { "draw-vacuum", "0.12 bar" },
                { "pull-shutdown-banks", "D 11 % C 22 % B 33 %" },
                { "dilute-boron", "1347 ppm" },
                { "withdraw-bank-a", "44 %" },
                { "reach-critical", "critical 42.5 %" },
                { "heat-up", "252 C" },
                { "auto-load", "auto" },
                { "roll-turbine", "2987 rpm" },
                { "close-breaker", "closed" },
                { "raise-power", "42.5 %" },
                { "take-load", "20.5 %" },
            });
        }

        [Fact]
        public void LiveValue_ExactTextForEveryShippedStep_SubcriticalManualOpen()
        {
            AssertShippedLiveValues(DistinctSnapshot(critical: false, autoLoad: false, synced: false), new Dictionary<string, string>
            {
                { "start-rcps", "37.5 %" },
                { "pressurize", "155 bar" },
                { "draw-vacuum", "0.12 bar" },
                { "pull-shutdown-banks", "D 11 % C 22 % B 33 %" },
                { "dilute-boron", "1347 ppm" },
                { "withdraw-bank-a", "44 %" },
                { "reach-critical", "subcritical 42.5 %" },
                { "heat-up", "252 C" },
                { "auto-load", "manual" },
                { "roll-turbine", "2987 rpm" },
                { "close-breaker", "open" },
                { "raise-power", "42.5 %" },
                { "take-load", "20.5 %" },
            });
        }
    }
}
