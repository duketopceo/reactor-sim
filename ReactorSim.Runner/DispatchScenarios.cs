// U15 dispatcher scenario: the plant holds full load on the auto-load governor while the grid dispatcher
// phones in requests; a 40 MW request over a 10-minute window is met by the delivered generator output,
// and the dispatcher's own seeded requests start arriving after the sync.

using System;
using System.Globalization;

namespace ReactorSim
{
    internal static class DispatchScenarios
    {
        private static void AssertTrue(bool cond, string msg)
        {
            if (!cond) throw new InvalidOperationException(msg);
        }

        private static string F(double v) { return v.ToString("G4", CultureInfo.InvariantCulture); }

        public static void VerifyHoldLoadMeetsRequest()
        {
            var sim = new Simulation { DevMode = true };
            sim.EnqueueIntent(PlantIntent.LoadPreset("full-power", "u15"));
            sim.Tick();
            var d = sim.Plant.Dispatcher;
            sim.RunGameMinutes(1, s => s.Plant.AssertFinite("U15:settle"));
            AssertTrue(d.EverSynced, "dispatcher should have seen the generator synced");

            // Replace whatever the dispatcher has queued with a known 40 MW / 10 min request.
            var req = d.Issue(40.0, d.Clock + 60.0, d.Clock + 660.0, "scenario");
            sim.RunGameMinutes(13, s => s.Plant.AssertFinite("U15:hold"));
            double mwe = sim.Plant.Bop.Turbine.GeneratorKw / 1000.0;
            AssertTrue(req.Status == RequestStatus.Met,
                "40 MW request not met: status=" + req.Status + " average=" + F(req.AverageMw) + " MW, generator now " + F(mwe) + " MW");
            AssertTrue(d.MetCount == 1 && d.Streak == 1, "score not recorded: met=" + d.MetCount + " streak=" + d.Streak);
            AssertTrue(d.History.Count == 1, "history should hold the resolved request");

            // The dispatcher keeps calling: a seeded request of its own follows.
            sim.RunGameMinutes(10, s => s.Plant.AssertFinite("U15:next"));
            AssertTrue(d.Current != null || d.History.Count >= 2, "dispatcher issued no follow-up request");
            Console.WriteLine("    request #" + req.Id + ": avg " + F(req.AverageMw) + " MW vs 40 MW -> " + req.Status
                              + "; follow-up issued=" + (d.Current != null || d.History.Count >= 2));
        }
    }
}
