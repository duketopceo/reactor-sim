using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace ReactorSim.Tests
{
    public class MessagesTests
    {
        private static MessageEngine Engine(string trigger, double cooldown)
        {
            return MessageEngine.Parse(
                "{\"messages\":[{\"id\":\"m1\",\"sender\":\"Boss\",\"text\":\"Why is the boron that high, \\\"Dave\\\"?\"," +
                "\"trigger\":\"" + trigger + "\",\"cooldownSeconds\":" + cooldown + "}]}");
        }

        private static Plant Running()
        {
            var p = new Plant();
            p.LoadPreset("full-power");
            return p;
        }

        private static void SetBoron(Plant p, double ppm)
        {
            p.Chemistry.RestoreState(ppm, 8.0, 0.0, 0.0, 0.0, 0.0);
        }

        private static void Steps(MessageEngine e, Plant p, Dispatcher d, int n, double dt = 10.0)
        {
            for (int i = 0; i < n; i++) e.Step(p, d, dt, 1.0);
        }

        [Fact]
        public void FiresOnceWhenTriggerBecomesTrue_AndNotWhileItStaysTrue()
        {
            var e = Engine("boron-high", 100.0);
            var p = Running(); var d = new Dispatcher();
            Steps(e, p, d, 5);
            Assert.Empty(e.Inbox);
            SetBoron(p, 900.0);
            Steps(e, p, d, 50);
            Assert.Single(e.Inbox);
            Assert.Equal("m1", e.Inbox[0].Id);
            Assert.Equal("Boss", e.Inbox[0].Sender);
            Assert.Contains("\"Dave\"", e.Inbox[0].Text);
        }

        [Fact]
        public void NotAgainWithinCooldown_EvenIfTriggerFlaps_ButAgainAfterIt()
        {
            var e = Engine("boron-high", 300.0);
            var p = Running(); var d = new Dispatcher();
            Steps(e, p, d, 2);
            SetBoron(p, 900.0); Steps(e, p, d, 2);
            Assert.Single(e.Inbox);
            SetBoron(p, 100.0); Steps(e, p, d, 2);    // trigger false
            SetBoron(p, 900.0); Steps(e, p, d, 2);    // true again, 60 s later: inside the cooldown
            Assert.Single(e.Inbox);
            SetBoron(p, 100.0); Steps(e, p, d, 40);   // false past the cooldown
            SetBoron(p, 900.0); Steps(e, p, d, 2);
            Assert.Equal(2, e.Inbox.Count);
        }

        [Fact]
        public void PresetStartDoesNotAnnounceItself_SyncEdgeDoes()
        {
            var e = Engine("sync-success", 0.0);
            var p = Running(); var d = new Dispatcher();
            Steps(e, p, d, 5);
            Assert.True(p.Bop.Turbine.Synced);
            Assert.Empty(e.Inbox);               // already synced at first look
            p.Bop.SetBreaker(false);
            Steps(e, p, d, 2);
            p.Bop.Turbine.Synced = true;         // closes
            Steps(e, p, d, 2);
            Assert.Single(e.Inbox);
        }

        [Fact]
        public void PresetThatStartsScrammedInThePit_DoesNotAnnounceItself()
        {
            // xenon-pit starts scrammed with high xenon: the "scram" and "xenon-pit" messages describe the preset, not
            // something the player did, so the first look must seed their edges without firing.
            var p = new Plant();
            p.Messages = MessageEngine.LoadFromFile(ShippedMessages());
            p.LoadPreset("xenon-pit");
            p.ResetDispatch();
            Assert.True(p.Scrammed);
            for (int i = 0; i < 3; i++) p.StepDispatch(1.0, 1.0);
            Assert.DoesNotContain(p.Messages.Inbox, m => m.Id == "scram-manager");
            Assert.Empty(p.Messages.Inbox);
        }

        [Fact]
        public void PresetReload_ResetsDispatcherAndInbox()
        {
            var sim = new Simulation();
            var d = sim.Plant.Dispatcher;
            d.Clock = 500.0; d.EverSynced = true; d.MetCount = 2; d.MissedCount = 1; d.Streak = 2; d.RngState = 99UL;
            d.Current = new LoadRequest { Id = 4, TargetMw = 30.0 };
            d.History.Add(new LoadRequest { Id = 3, Status = RequestStatus.Met });
            sim.Plant.Messages.Inbox.Add(new FiredMessage { Seq = 1, Id = "x", Sender = "G", Text = "t" });

            sim.LoadScenarioPreset("cold-shutdown");

            Assert.Null(d.Current);
            Assert.Empty(d.History);
            Assert.Equal(0.0, d.Clock);
            Assert.False(d.EverSynced);
            Assert.Equal(0, d.MetCount + d.MissedCount + d.Streak);
            Assert.Equal(d.InitialSeed, d.RngState);
            Assert.Empty(sim.Plant.Messages.Inbox);
        }

        [Fact]
        public void RequestOutcomeTriggers_FireFromTheDispatcher()
        {
            var e = MessageEngine.Parse(
                "{\"messages\":[" +
                "{\"id\":\"met\",\"sender\":\"G\",\"text\":\"ok\",\"trigger\":\"request-met\",\"cooldownSeconds\":0}," +
                "{\"id\":\"miss\",\"sender\":\"G\",\"text\":\"short\",\"trigger\":\"request-missed\",\"cooldownSeconds\":0}]}");
            var p = Running(); var d = new Dispatcher(5);
            Steps(e, p, d, 1);
            d.Issue(40.0, d.Clock, d.Clock + 60.0, "t");
            for (int i = 0; i < 12; i++) { d.Step(10.0, 10.0, true); e.Step(p, d, 10.0, 1.0); }
            Assert.Single(e.Inbox);
            Assert.Equal("miss", e.Inbox[0].Id);
        }

        [Fact]
        public void ShippedMessagesJson_ParsesAndEveryTriggerExists()
        {
            string path = ShippedMessages();
            var e = MessageEngine.LoadFromFile(path);
            Assert.InRange(e.Defs.Count, 15, 25);
            foreach (var d in e.Defs)
            {
                Assert.True(MessageEngine.Predicates.ContainsKey(d.Trigger), "unknown trigger " + d.Trigger);
                Assert.True(d.CooldownSeconds >= 0.0);
            }
        }

        [Fact]
        public void EveryShippedTrigger_EvaluatesOnAFreshPlantWithoutThrowing()
        {
            var e = MessageEngine.LoadFromFile(ShippedMessages());
            var p = Running(); var d = new Dispatcher();
            Steps(e, p, d, 3);
        }

        [Theory]
        [InlineData("")]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData("{\"messages\":[{\"id\":\"a\",\"sender\":\"s\",\"text\":\"t\",\"trigger\":\"no-such-trigger\",\"cooldownSeconds\":1}]}")]
        [InlineData("{\"messages\":[{\"id\":\"a\",\"sender\":\"s\",\"text\":\"t\",\"trigger\":\"scram\",\"cooldownSeconds\":1,\"extra\":2}]}")]
        [InlineData("{\"messages\":[{\"id\":\"a\",\"sender\":\"s\",\"text\":\"t\",\"trigger\":\"scram\",\"cooldownSeconds\":-1}]}")]
        [InlineData("{\"messages\":[{\"id\":\"a\",\"sender\":\"s\",\"text\":\"t\",\"trigger\":\"scram\",\"cooldownSeconds\":1},{\"id\":\"a\",\"sender\":\"s\",\"text\":\"t\",\"trigger\":\"scram\",\"cooldownSeconds\":1}]}")]
        [InlineData("{\"messages\":[{\"id\":\"a\",\"sender\":\"s\",\"text\":\"t\"")]
        public void Malformed_ThrowsMessageException(string json)
        {
            Assert.Throws<MessageException>(() => MessageEngine.Parse(json));
        }

        [Fact]
        public void UnknownTrigger_ErrorNamesIt()
        {
            var ex = Assert.Throws<MessageException>(() => Engine("boron-hgih", 1.0));
            Assert.Contains("boron-hgih", ex.Message);
        }

        [Fact]
        public void LoadFromFile_MissingFile_GivesEmptyEngine()
        {
            var e = MessageEngine.LoadFromFile(Path.Combine(Path.GetTempPath(), "no-messages-" + Guid.NewGuid() + ".json"));
            Assert.Empty(e.Defs);
        }

        [Fact]
        public void TwoSims_SameInputs_IdenticalMessageState()
        {
            string path = ShippedMessages();
            Simulation Make()
            {
                var sim = new Simulation { DevMode = true };
                sim.Plant.Messages = MessageEngine.LoadFromFile(path);
                sim.EnqueueIntent(PlantIntent.LoadPreset("full-power", "t"));
                sim.Tick();   // the preset's first look; a SCRAM in that same tick would count as starting state
                sim.EnqueueIntent(PlantIntent.TriggerScram("t"));
                return sim;
            }
            var a = Make(); var b = Make();
            for (int i = 0; i < 400; i++) { a.Tick(); b.Tick(); }
            Assert.Equal(a.Plant.Messages.Inbox.Count, b.Plant.Messages.Inbox.Count);
            for (int i = 0; i < a.Plant.Messages.Inbox.Count; i++)
            {
                Assert.Equal(a.Plant.Messages.Inbox[i].Id, b.Plant.Messages.Inbox[i].Id);
                Assert.Equal(a.Plant.Messages.Inbox[i].AtSeconds, b.Plant.Messages.Inbox[i].AtSeconds);
            }
            Assert.Contains(a.Plant.Messages.Inbox, m => m.Id == "scram-manager");
        }

        private static string ShippedMessages()
        {
            return TestRepo.Find("ReactorSim.Unity", "Assets", "StreamingAssets", "messages.json");
        }
    }
}
