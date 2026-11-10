using System;
using System.IO;
using Xunit;

namespace ReactorSim.Tests
{
    // U11: the per-session writer behind the Unity SessionLog (UnityEngine-free so it runs here).
    public class SessionRecorderTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "rs-session-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private static Simulation NewSim()
        {
            var sim = new Simulation(new Tuning());
            sim.LoadScenarioPreset("cold-shutdown");
            return sim;
        }

        [Fact]
        public void FolderName_UsesSortableTimestamp()
        {
            Assert.Equal("20261107-153009", SessionRecorder.FolderName(new DateTime(2026, 11, 7, 15, 30, 9)));
        }

        [Fact]
        public void Start_CreatesFolderWithHeader_AndCollisionGetsSuffix()
        {
            var t = new DateTime(2026, 11, 7, 15, 30, 9);
            using (var a = new SessionRecorder(_root, t, NewSim(), "9.9.9", "sandbox"))
            using (var b = new SessionRecorder(_root, t, NewSim(), "9.9.9", "sandbox"))
            {
                Assert.NotEqual(a.Directory, b.Directory);
                Assert.EndsWith("20261107-153009", a.Directory);
                Assert.EndsWith("20261107-153009-2", b.Directory);
                string header = File.ReadAllText(Path.Combine(a.Directory, "session.json"));
                Assert.Contains("\"buildVersion\":\"9.9.9\"", header);
                Assert.Contains("\"mode\":\"sandbox\"", header);
                Assert.Contains("\"DefaultScale\":10", header);
                Assert.Contains("\"startLocal\":\"2026-11-07T15:30:09", header);
            }
        }

        [Fact]
        public void AfterTicks_FolderHoldsNonEmptyJsonlCsvAndIntentLog()
        {
            var sim = NewSim();
            using (var rec = new SessionRecorder(_root, DateTime.Now, sim, "1.0", "sandbox"))
            {
                sim.EnqueueIntent(PlantIntent.SetPrimaryFlow(1.0, "player"));
                for (int i = 0; i < 20; i++) sim.Tick();
                rec.Finish();

                string jsonl = File.ReadAllText(Path.Combine(rec.Directory, "log.jsonl"));
                var lines = jsonl.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal(20, lines.Length);
                Assert.Contains("\"tick\":1", lines[0]);

                var csv = File.ReadAllLines(Path.Combine(rec.Directory, "telemetry.csv"));
                Assert.Equal(TelemetryRecorder.CsvHeader, csv[0]);
                Assert.Equal(21, csv.Length);

                string intents = File.ReadAllText(Path.Combine(rec.Directory, "intent-log.jsonl"));
                Assert.Contains("PrimaryFlow", intents);
                Assert.Contains("\"rejected\":false", intents);

                string header = File.ReadAllText(Path.Combine(rec.Directory, "session.json"));
                Assert.Contains("\"ticks\":20", header);
                Assert.Contains("\"ended\":true", header);
            }
            Assert.Null(sim.Logger);
            Assert.Null(sim.Recorder);
        }

        [Fact]
        public void FilesAreReadableWhileOpen_SoACrashLosesNothingFlushed()
        {
            var sim = NewSim();
            using (var rec = new SessionRecorder(_root, DateTime.Now, sim, "1.0", "missions"))
            {
                for (int i = 0; i < 5; i++) sim.Tick();
                rec.Checkpoint();
                using (var fs = new FileStream(Path.Combine(rec.Directory, "log.jsonl"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                    Assert.Equal(5, sr.ReadToEnd().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length);
                Assert.True(File.Exists(Path.Combine(rec.Directory, "intent-log.jsonl")));
                Assert.Contains("\"ended\":false", File.ReadAllText(Path.Combine(rec.Directory, "session.json")));
            }
        }
    }
}
