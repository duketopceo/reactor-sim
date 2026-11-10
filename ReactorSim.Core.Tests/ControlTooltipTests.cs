using System;
using System.IO;
using System.Linq;
using Xunit;
using ReactorSim;
using ReactorSim.Controls;

namespace ReactorSim.Core.Tests
{
    // U17 hover info: the pure tooltip model (value with units, setpoint, limits, description) over the generated
    // StreamingAssets/inventory-runtime.json.
    public class ControlTooltipTests
    {
        private static RuntimeInventory Inventory()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "StreamingAssets", "inventory-runtime.json");
            Assert.True(File.Exists(path), "inventory-runtime.json not copied to test output");
            var opts = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
            var inv = System.Text.Json.JsonSerializer.Deserialize<RuntimeInventory>(File.ReadAllText(path), opts);
            Assert.NotNull(inv);
            return inv;
        }

        private static ControlSpec Spec(string name)
        {
            var r = PivotNameParser.Parse(name);
            Assert.Equal(ParseStatus.Ok, r.Status);
            return r.Spec;
        }

        [Fact]
        public void BoronDiluteDial_ShowsPpmPerSecond_AndItsZeroToFiveRange()
        {
            var row = Inventory().Find("dilute_ctl");
            Assert.NotNull(row);
            var snap = new PlantSnapshot { DiluteRate = 1.5 };
            var tip = ControlTooltipModel.ForControl(row, Spec("INT_DIAL_DiluteRate_Boron_0_5"), snap, "", double.NaN, false, "", "");
            Assert.Equal("Dilute rate", tip.Title);
            Assert.Equal("now 1.5 ppm/s", tip.Value);
            Assert.Equal("range 0 - 5 ppm/s", tip.Limits);
            Assert.False(string.IsNullOrWhiteSpace(tip.Description));
            Assert.Contains("ppm/s", tip.ToText());
            Assert.Contains("0 - 5", tip.ToText());
        }

        [Fact]
        public void DialWithAPendingTarget_ShowsTheSetpoint_AndQueuedMarker()
        {
            var row = Inventory().Find("dilute_ctl");
            var snap = new PlantSnapshot { DiluteRate = 1.5 };
            var tip = ControlTooltipModel.ForControl(row, Spec("INT_DIAL_DiluteRate_Boron_0_5"), snap, "", 3.0, true, "", "");
            Assert.Equal("set 3 ppm/s  [queued]", tip.Setpoint);
        }

        [Fact]
        public void RodLever_ShowsTheCommandedTarget_WhereItDiffersFromThePosition()
        {
            var row = Inventory().Find("rod_bank_0_ctl");
            var snap = new PlantSnapshot { BankInsertion = new double[] { 1, 1, 1, 1 }, BankTarget = new double[] { 0.5, 1, 1, 1 } };
            var tip = ControlTooltipModel.ForControl(row, Spec("INT_DIAL_RodBankTarget_0_0_1"), snap, "", double.NaN, false, "", "");
            Assert.Equal("now 1 frac", tip.Value);
            Assert.Equal("set 0.5 frac", tip.Setpoint);
            Assert.Equal("range 0 - 1 frac", tip.Limits);
        }

        [Fact]
        public void ButtonTooltip_ShowsTheStateText_AndNoLimits()
        {
            var row = Inventory().Find("alarm_ack_btn");
            var tip = ControlTooltipModel.ForControl(row, Spec("INT_BTN_AcknowledgeAlarms_Alarms_1"), new PlantSnapshot(), "", double.NaN, false,
                "alarms flashing - press to acknowledge", "");
            Assert.Equal("Acknowledge alarms", tip.Title);
            Assert.Equal("alarms flashing - press to acknowledge", tip.Value);
            Assert.Equal("", tip.Limits);
            Assert.False(string.IsNullOrWhiteSpace(tip.Description));
        }

        [Fact]
        public void IndicatorTooltip_ReadsTheGaugeSelector_WithUnitsAndRange()
        {
            var row = Inventory().Find("boron_ppm");
            var snap = new PlantSnapshot { BoronPpm = 1480.4 };
            var tip = ControlTooltipModel.ForIndicator(row, "BoronPpm", -1, snap, "", "");
            Assert.Equal("now 1480 ppm", tip.Value);
            Assert.Equal("range 0 - 2500 ppm", tip.Limits);
            var rod = ControlTooltipModel.ForIndicator(Inventory().Find("rod_bank_1_pos"), "BankInsertion", 1, new PlantSnapshot { BankInsertion = new double[] { 1, 0.25, 1, 1 } }, "", "");
            Assert.Equal("now 0.25 frac", rod.Value);
        }

        [Fact]
        public void OutOfServiceTooltip_SaysWhy()
        {
            var tip = ControlTooltipModel.ForOutOfService(Inventory().Find("rcp_a_sw"), "", "needs a new plant model (plumbing)");
            Assert.Equal("Out of service: needs a new plant model (plumbing)", tip.Note);
            Assert.Equal("RCP A", tip.Title);
        }

        [Fact]
        public void EveryRuntimeRow_HasALabelAndADescription()
        {
            var inv = Inventory();
            Assert.True(inv.rows.Length > 190);
            foreach (var r in inv.rows)
            {
                Assert.False(string.IsNullOrWhiteSpace(r.label), r.id);
                Assert.False(string.IsNullOrWhiteSpace(r.description), r.id);
            }
            Assert.Equal(inv.rows.Length, inv.rows.Select(r => r.id).Distinct().Count());
        }

        [Fact]
        public void CounterRows_AreTheK04Kit()
        {
            var inv = Inventory();
            foreach (var id in new[] { "boron_ppm", "game_clock", "turbine_mech_mw", "excore_flux" })
                Assert.Equal("K04", inv.Find(id).kit);
        }
    }
}
