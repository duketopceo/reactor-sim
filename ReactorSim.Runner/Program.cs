// ReactorSim console harness — scripted startup transient + U1–U10 verification suite,
// intent script runner (--script), JSONL logger (--log), CSV recorder (--record),
// golden replay verifier (--replay / --golden), live HTTP API server (--serve), and
// determinism/numerics/edge-case checks (--verify [--check <id>] / --list; see
// docs/testing/simulation-verification.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace ReactorSim
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string scenario = null;
            string scriptPath = null;
            string logPath = null;
            string recordPath = null;
            string replayScriptPath = null;
            string goldenCsvPath = null;
            bool serveMode = false;
            int servePort = 7770;
            bool verify = false, listChecks = false, verbose = false;
            string checkFilter = null;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--scenario" && i + 1 < args.Length) scenario = args[++i];
                else if (a == "--script" && i + 1 < args.Length) scriptPath = args[++i];
                else if (a == "--log" && i + 1 < args.Length) logPath = args[++i];
                else if (a == "--record" && i + 1 < args.Length) recordPath = args[++i];
                else if (a == "--replay" && i + 1 < args.Length) replayScriptPath = args[++i];
                else if (a == "--golden" && i + 1 < args.Length) goldenCsvPath = args[++i];
                else if (a == "--verify") verify = true;
                else if (a == "--list") listChecks = true;
                else if (a == "--check" && i + 1 < args.Length) checkFilter = args[++i];
                else if (a == "--verbose" || a == "-v") verbose = true;
                else if (a == "--serve")
                {
                    serveMode = true;
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int p))
                    {
                        servePort = p;
                        i++;
                    }
                }
            }

            StartupScenarios.Verbose = verbose;
            if (verify || listChecks)
            {
                PlantDriver.Verbose = verbose;
                return Verification.Run(listChecks, checkFilter, verbose);
            }

            if (!string.IsNullOrEmpty(replayScriptPath) && !string.IsNullOrEmpty(goldenCsvPath))
            {
                return RunGoldenReplay(replayScriptPath, goldenCsvPath, logPath);
            }

            if (!string.IsNullOrEmpty(scriptPath))
            {
                return RunIntentScriptFile(scriptPath, logPath, recordPath);
            }

            if (serveMode)
            {
                return RunHttpServerInteractive(servePort, logPath);
            }

            if (!string.IsNullOrEmpty(scenario))
            {
                return RunScenarioSuite(scenario, logPath, recordPath);
            }

            return RunBaselineDemo(logPath, recordPath);
        }

        private static int RunBaselineDemo(string logPath, string recordPath)
        {
            var sim = new Simulation();
            if (!string.IsNullOrEmpty(logPath)) sim.Logger = new SimLogger(logPath);
            if (!string.IsNullOrEmpty(recordPath)) sim.Recorder = new TelemetryRecorder(recordPath);

            try
            {
                Console.WriteLine("ReactorSim baseline — startup transient");
                Console.WriteLine("tick dt = " + sim.TickDt + "s sim-time, " +
                                  sim.TicksPerGameMinute + " ticks/game-min\n");

                Action<Simulation> print = s =>
                {
                    Console.WriteLine(s.Plant.Telemetry(s.GameMinutes));
                    if (s.Plant.Assistant.LatestAdvisory != null)
                        Console.WriteLine("  " + s.Plant.Assistant.LatestAdvisory.FormatTerminalLine());
                };

                // All mutations through PlantIntent pipeline (U2)
                sim.EnqueueIntent(PlantIntent.SetDilute(2.5));
                sim.EnqueueIntent(PlantIntent.SetPrimaryFlow(1.0));
                sim.EnqueueIntent(PlantIntent.SetSteamDemand(0.0));
                sim.EnqueueIntent(PlantIntent.SetRodBank("D", 0.0));
                sim.EnqueueIntent(PlantIntent.SetRodBank("C", 0.0));
                sim.EnqueueIntent(PlantIntent.SetRodBank("B", 0.0));
                sim.RunGameMinutes(2, print);

                sim.EnqueueIntent(PlantIntent.SetDilute(0.0));
                sim.EnqueueIntent(PlantIntent.SetRodBank("A", 0.44));
                sim.EnqueueIntent(PlantIntent.SetSteamDemand(0.15));
                sim.RunGameMinutes(5, print);

                sim.EnqueueIntent(PlantIntent.SetDilute(3.0));
                sim.EnqueueIntent(PlantIntent.SetRodBank("A", 0.18));
                sim.EnqueueIntent(PlantIntent.SetSteamDemand(0.95));
                sim.RunGameMinutes(8, print);

                sim.EnqueueIntent(PlantIntent.SetDilute(0.0));
                sim.RunGameMinutes(5, print);

                Console.WriteLine("\n--- SCRAM ---");
                sim.EnqueueIntent(PlantIntent.TriggerScram());
                sim.RunGameMinutes(5, print);

                Console.WriteLine("\nDone. Xenon pit is active (Xe={0:F2}, {1:F0} pcm).",
                    sim.Plant.Core.Xenon135, sim.Plant.Core.XenonPcm);
                return 0;
            }
            finally
            {
                sim.Logger?.Dispose();
                sim.Recorder?.Dispose();
            }
        }

        private static int RunScenarioSuite(string filter, string logPath, string recordPath)
        {
            string key = filter.Trim().ToLowerInvariant();
            bool all = key == "all";
            int failures = 0;

            Console.WriteLine("=================================================================");
            Console.WriteLine(" ReactorSim Headless Verification Suite (U1 - U10)");
            Console.WriteLine("=================================================================");

            if (all || key == "startup")
                failures += RunCheck("U1.1 Cold Shutdown -> Criticality -> Stable Low Power", VerifyStartup);

            if (all || key == "excursion")
                failures += RunCheck("U1.2 +300 pcm Excursion Self-Limits via Doppler (AE1)", VerifyExcursion);

            if (all || key == "sustained")
                failures += RunCheck("U1.3 Sustained 100% Full Power Equilibrium", VerifySustainedFullPower);

            if (all || key == "scram")
                failures += RunCheck("U1.4 SCRAM Collapses Prompt Power to Decay-Heat Floor", VerifyScram);

            if (all || key == "xenon-pit")
                failures += RunCheck("U1.5 Post-SCRAM Xenon Pit Blocks Restart Then Decays (AE2)", VerifyXenonPit);

            if (all || key == "loss-of-sink")
                failures += RunCheck("U1.6 Loss of Secondary Heat Sink Causal Chain (AE3)", VerifyLossOfSink);

            if (all || key == "soak")
                failures += RunCheck("U1.7 60-Minute Soak Finite-State & Bounds Verification", VerifySoak60Min);

            if (all || key == "u2" || key == "snapshot")
                failures += RunCheck("U2 Intent Pipeline Parity & Snapshot JSON Round-Trip", VerifyIntentAndSnapshot);

            if (all || key == "u3" || key == "http")
                failures += RunCheck("U3 In-Process HTTP JSON API Live Smoke Test", VerifyHttpApi);

            if (all || key == "u4" || key == "mcp")
                failures += RunCheck("U4 MCP Bridge Stdio JSON-RPC End-to-End Smoke Test", VerifyMcpBridge);

            if (all || key == "u5" || key == "u8" || key == "missions")
                failures += RunCheck("U5/U8 Ops Assistant Tiers & 3-Mission Arc Completion", VerifyAssistantAndMissions);

            if (all || key == "u9" || key == "replay")
                failures += RunCheck("U9 Structured JSONL Logging & Golden CSV Replay", VerifyLoggingAndGoldenReplay);

            if (all || key == "u10" || key == "bop" || key == "bop-auto")
                failures += RunCheck("U10.1 Balance of Plant: Auto-Load Generation at Power", BopScenarios.VerifyAutoLoadAtPower);

            if (all || key == "u10" || key == "bop" || key == "bop-manual")
                failures += RunCheck("U10.2 Balance of Plant: Load Rejection, Overspeed Trip, Re-Sync", BopScenarios.VerifyManualChoreography);

            if (all || key == "u10" || key == "bop" || key == "bop-vacuum")
                failures += RunCheck("U10.3 Balance of Plant: Loss of Condenser Cooling -> Low-Vacuum Trip", BopScenarios.VerifyLossOfCondenserCooling);

            if (all || key == "u2-clock" || key == "cold-to-sync")
                failures += RunCheck("U2 Sim Clock: Cold Shutdown -> Breaker Close Within 15 Real Minutes", BopScenarios.VerifyColdToSyncTiming);

            if (all || key == "u15" || key == "dispatch")
                failures += RunCheck("U15 Dispatcher: Plant Holds Load and Meets a 40 MW Request", DispatchScenarios.VerifyHoldLoadMeetsRequest);

            if (all || key == "startup-grid")
                failures += RunCheck("U4 Startup: Checklist From Cold Start to Grid Sync, Player Intents Only", StartupScenarios.VerifyStartupToGrid);

            Console.WriteLine("=================================================================");
            if (failures == 0)
            {
                Console.WriteLine(" ALL SCENARIOS PASSED (0 failures)");
                Console.WriteLine("=================================================================");
                return 0;
            }
            else
            {
                Console.WriteLine(" FAILED SCENARIOS: " + failures);
                Console.WriteLine("=================================================================");
                return 1;
            }
        }

        private static int RunCheck(string name, Action test)
        {
            Console.Write("[RUN ] " + name + " ... ");
            try
            {
                test();
                Console.WriteLine("PASS");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL");
                Console.WriteLine("       -> " + ex.Message);
                return 1;
            }
        }

        private static void AssertTrue(bool cond, string msg)
        {
            if (!cond) throw new InvalidOperationException(msg);
        }

        // U1.1: Cold shutdown -> boron dilution + bank withdrawal -> criticality -> stable low power
        private static void VerifyStartup()
        {
            var sim = new Simulation();
            sim.LoadScenarioPreset("cold-shutdown");
            AssertTrue(sim.Plant.Core.Power < 1e-3, "Initial power should be source level.");
            AssertTrue(!sim.Plant.Core.IsCritical, "Should start subcritical.");

            sim.EnqueueIntent(PlantIntent.SetPrimaryFlow(1.0));
            sim.EnqueueIntent(PlantIntent.SetHeaters(0.45));
            sim.EnqueueIntent(PlantIntent.SetRodBank("D", 0.0));
            sim.EnqueueIntent(PlantIntent.SetRodBank("C", 0.0));
            sim.EnqueueIntent(PlantIntent.SetRodBank("B", 0.0));
            sim.EnqueueIntent(PlantIntent.SetDilute(2.5));
            sim.RunGameMinutes(2.2, null); // Boron drops from 1500 to ~1170 ppm (-2925 pcm)

            sim.EnqueueIntent(PlantIntent.SetDilute(0.0));
            sim.EnqueueIntent(PlantIntent.SetRodBank("A", 0.44));
            sim.EnqueueIntent(PlantIntent.SetSteamDemand(0.15));
            sim.RunGameMinutes(4.0, null);

            double p = sim.Plant.Core.Power;
            AssertTrue(p >= 0.02 && p <= 0.45, $"Expected stable low power in [2%, 45%], got {p:P2}");
            AssertTrue(!sim.Plant.Scrammed, "Should not have scrammed during startup.");
        }

        // U1.2 & AE1: +300 pcm step excursion self-limits via negative fuel Doppler before fuel damage
        private static void VerifyExcursion()
        {
            var sim = new Simulation();
            sim.LoadScenarioPreset("hot-standby");
            sim.RunGameMinutes(1.0, null);
            double initialFuel = sim.Plant.Thermal.FuelTempC;

            // Withdraw Bank A from 0.50 to 0.36 to inject > +450 pcm positive reactivity
            sim.EnqueueIntent(PlantIntent.SetRodBank("A", 0.36));
            double peakRho = -9999.0;
            double peakFuel = initialFuel;
            bool sawExcursionAlarm = false;

            for (int i = 0; i < 40; i++) // 4 game-minutes
            {
                sim.Tick();
                if (sim.Plant.Core.TotalReactivityPcm > peakRho)
                    peakRho = sim.Plant.Core.TotalReactivityPcm;
                if (sim.Plant.Thermal.FuelTempC > peakFuel)
                    peakFuel = sim.Plant.Thermal.FuelTempC;
                foreach (var al in sim.Plant.Assistant.ActiveAlarms)
                {
                    if (al.Code == "REACTIVITY_EXCURSION") sawExcursionAlarm = true;
                }
            }

            double finalRho = sim.Plant.Core.TotalReactivityPcm;
            double finalDoppler = sim.Plant.Core.DopplerPcm;

            AssertTrue(peakRho >= 230.0, $"Expected positive excursion > +230 pcm, peak was {peakRho:F1} pcm");
            AssertTrue(sawExcursionAlarm, "Assistant should raise REACTIVITY_EXCURSION alarm during excursion (AE1).");
            AssertTrue(finalDoppler < -200.0, $"Doppler feedback should turn strongly negative, got {finalDoppler:F1} pcm");
            AssertTrue(finalRho < peakRho - 150.0, $"Doppler must pull net reactivity back down (peak={peakRho:F0}, final={finalRho:F0})");
            AssertTrue(peakFuel < 1550.0, $"Excursion must self-limit below fuel damage threshold (peakFuel={peakFuel:F0}C)");
        }

        // U1.3: Sustained 100% power equilibrium holds fuel ~500-700C, SG ~55-65 bar, subcooled primary
        private static void VerifySustainedFullPower()
        {
            var sim = new Simulation();
            sim.LoadScenarioPreset("full-power");
            sim.RunGameMinutes(15.0, null);

            var p = sim.Plant;
            AssertTrue(p.Core.Power >= 0.85 && p.Core.Power <= 1.15, $"Full power should hold near 100%, got {p.Core.Power:P1}");
            AssertTrue(p.Thermal.FuelTempC >= 500.0 && p.Thermal.FuelTempC <= 720.0, $"Fuel temp should be 500-720C, got {p.Thermal.FuelTempC:F1}C");
            AssertTrue(p.Thermal.SecondaryPressureBar >= 54.0 && p.Thermal.SecondaryPressureBar <= 66.0, $"SG pressure should be 54-66 bar, got {p.Thermal.SecondaryPressureBar:F1} bar");
            AssertTrue(p.Thermal.PrimarySatMarginC >= 12.0, $"Primary coolant must remain subcooled (>=12C margin), got {p.Thermal.PrimarySatMarginC:F1}C");
            AssertTrue(p.Core.Xenon135 >= 0.90 && p.Core.Xenon135 <= 1.10, $"Equilibrium Xenon should be ~1.0, got {p.Core.Xenon135:F2}");
        }

        // U1.4: SCRAM from full power collapses prompt neutron power while decay heat persists
        private static void VerifyScram()
        {
            var sim = new Simulation();
            sim.LoadScenarioPreset("full-power");
            sim.RunGameMinutes(1.0, null);

            sim.EnqueueIntent(PlantIntent.TriggerScram());
            sim.RunGameMinutes(1.0, null);

            var p = sim.Plant;
            AssertTrue(p.Scrammed, "Plant should be marked Scrammed.");
            AssertTrue(p.Core.Power < 1.0e-3, $"Prompt power must collapse < 0.1%, got {p.Core.Power:P3}");
            AssertTrue(p.Core.DecayHeatPower >= 0.015 && p.Core.DecayHeatPower <= 0.07, $"Decay heat should remain 1.5-7% after SCRAM, got {p.Core.DecayHeatPower:P2}");
            AssertTrue(p.Core.RodReactivityPcm <= -7900.0, $"All rods should be fully inserted (-8000 pcm), got {p.Core.RodReactivityPcm:F0} pcm");
        }

        // U1.5 & AE2: Post-SCRAM Xenon-135 pit rises above 1.95 (-4500+ pcm), physically denies restart with all rods out & 0 boron, then decays to allow re-criticality
        private static void VerifyXenonPit()
        {
            var sim = new Simulation();
            sim.LoadScenarioPreset("full-power");
            sim.RunGameMinutes(1.0, null);

            // SCRAM and strip boron to 0 ppm so ONLY Xenon-135 + Iodine-135 stands against BaseExcessReactivity (+4500 pcm)
            sim.EnqueueIntent(PlantIntent.TriggerScram());
            sim.EnqueueIntent(PlantIntent.SetDilute(5.0));
            sim.EnqueueIntent(PlantIntent.SetSteamDemand(0.10));
            sim.RunGameMinutes(3.0, null); // 3.0 game-minutes post-SCRAM -> near the Xenon peak (~1.97 with the 150x poison time warp)

            var p = sim.Plant;
            AssertTrue(p.Chemistry.BoronPpm < 1.0, "Boron should be diluted to 0 ppm.");
            AssertTrue(p.Core.Xenon135 > 1.95, $"Xenon-135 should peak > 1.95 after SCRAM from full power, got {p.Core.Xenon135:F3}");
            AssertTrue(p.Core.XenonPcm < -4500.0, $"Xenon worth should be more negative than -4500 pcm, got {p.Core.XenonPcm:F0} pcm");

            // Check Assistant Xenon pit window estimator (AE2)
            p.Assistant.EstimateXenonPitWindow(p, out double estPeak, out double minToPeak, out double minToRecovery);
            AssertTrue(minToRecovery > 1.0, $"Assistant should report positive recovery wait time in Xenon pit, got {minToRecovery:F1}m");

            // Attempt restart inside the Xenon pit: clear SCRAM and withdraw ALL rods to 0.0
            sim.EnqueueIntent(PlantIntent.ClearScram());
            sim.EnqueueIntent(PlantIntent.SetAllRods(0.0));
            // Full-stroke withdrawal at the 1 %/s drive speed takes ~100 s.
            sim.RunGameMinutes(1.75, null);

            AssertTrue(p.Banks[3].Insertion < 0.01, "All rods should be completely withdrawn (0.00).");
            AssertTrue(p.Core.TotalReactivityPcm < -100.0, $"Net reactivity must remain negative in the Xenon pit even with all rods out and 0 boron! Got {p.Core.TotalReactivityPcm:F1} pcm");
            AssertTrue(p.Core.Power < 1.0e-3, $"Reactor must remain subcritical in the Xenon pit, got P={p.Core.Power:P3}");
            AssertTrue(p.Assistant.ExperiencedXenonPitDenial, "Assistant should record ExperiencedXenonPitDenial.");

            // Set Regulating Bank A to 0.35 while waiting out the pit so power levels off cleanly once Xenon decays
            sim.EnqueueIntent(PlantIntent.SetRodBank("A", 0.35));
            sim.RunGameMinutes(12.0, null);
            AssertTrue(p.Core.Xenon135 < 1.55, $"Xenon should decay below 1.55 after waiting, got {p.Core.Xenon135:F2}");
            AssertTrue(p.Core.Power > 0.02, $"Reactor should recover criticality and rise above 2% power once Xenon decays, got {p.Core.Power:P2}");
        }

        // U1.6 & AE3: Loss of secondary heat sink causal chain
        private static void VerifyLossOfSink()
        {
            var sim = new Simulation();
            sim.LoadScenarioPreset("full-power");
            sim.RunGameMinutes(1.0, null);

            double initialSgBar = sim.Plant.Thermal.SecondaryPressureBar;
            double initialPriTemp = sim.Plant.Thermal.PrimaryTempC;

            // Trip turbine steam demand to 0 while core is at full power
            sim.EnqueueIntent(PlantIntent.SetSteamDemand(0.0));
            bool sawSinkAlarm = false;
            for (int i = 0; i < 25; i++)
            {
                sim.Tick();
                foreach (var al in sim.Plant.Assistant.ActiveAlarms)
                {
                    if (al.Code == "LOSS_OF_SECONDARY_SINK") sawSinkAlarm = true;
                }
            }

            AssertTrue(sim.Plant.Thermal.SecondaryPressureBar > initialSgBar + 10.0,
                $"SG pressure should rise sharply on loss of steam draw ({initialSgBar:F1} -> {sim.Plant.Thermal.SecondaryPressureBar:F1} bar)");
            AssertTrue(sim.Plant.Thermal.PrimaryTempC > initialPriTemp + 5.0,
                $"Primary temperature should rise as SG heat transfer degrades ({initialPriTemp:F1} -> {sim.Plant.Thermal.PrimaryTempC:F1}C)");
            AssertTrue(sawSinkAlarm, "Assistant must raise LOSS_OF_SECONDARY_SINK alarm tracing SG -> Primary causal chain (AE3).");
        }

        // U1.7: 60-game-minute continuous soak with AssertFinite on every tick
        private static void VerifySoak60Min()
        {
            var sim = new Simulation();
            sim.LoadScenarioPreset("full-power");
            sim.RunGameMinutes(60.0, null);
            AssertTrue(sim.GameMinutes >= 60.0, "Should complete 60 game-minutes.");
            AssertTrue(sim.Plant.Core.Power > 0.70 && sim.Plant.Core.Power < 1.25, $"Power should remain stable over 60m soak, got {sim.Plant.Core.Power:P2}");
        }

        // U2: Intent script parity vs direct calls + Snapshot JSON round-trip
        private static void VerifyIntentAndSnapshot()
        {
            var simA = new Simulation();
            simA.LoadScenarioPreset("hot-standby");
            simA.EnqueueIntent(PlantIntent.SetDilute(1.5));
            simA.EnqueueIntent(PlantIntent.SetRodBank("A", 0.42));
            simA.EnqueueIntent(PlantIntent.SetSteamDemand(0.25));
            simA.RunGameMinutes(5.0, null);

            // Capture snapshot -> serialize to JSON -> deserialize -> restore into fresh Simulation B
            string json = simA.CaptureSnapshot().ToJson();
            var restoredSnap = PlantSnapshot.FromJson(json);
            AssertTrue(restoredSnap != null, "Deserialized snapshot must not be null.");

            var simB = new Simulation();
            simB.RestoreSnapshot(restoredSnap);

            // Step both simulations forward 5 more game-minutes and verify identical state
            simA.RunGameMinutes(5.0, null);
            simB.RunGameMinutes(5.0, null);

            double diffP = Math.Abs(simA.Plant.Core.Power - simB.Plant.Core.Power);
            double diffT = Math.Abs(simA.Plant.Thermal.FuelTempC - simB.Plant.Thermal.FuelTempC);
            double diffXe = Math.Abs(simA.Plant.Core.Xenon135 - simB.Plant.Core.Xenon135);

            AssertTrue(diffP < 1e-6 && diffT < 1e-4 && diffXe < 1e-6,
                $"Restored snapshot trajectory diverged: dP={diffP}, dT={diffT}, dXe={diffXe}");
        }

        // U3: In-process HTTP API server smoke test
        private static void VerifyHttpApi()
        {
            var sim = new Simulation();
            sim.LoadScenarioPreset("hot-standby");
            sim.Tick();

            int port = 17770;
            string tokenFile = Path.Combine(Path.GetTempPath(), "reactorsim-u3-" + Guid.NewGuid().ToString("N") + ".token");
            using (var server = new HttpApiServer(sim, port, null, tokenFile))
            {
                bool started = server.Start();
                AssertTrue(started, "HttpApiServer failed to start on 127.0.0.1:" + port);

                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                {
                    client.DefaultRequestHeaders.Add(HttpApiServer.TokenHeader, server.Token);
                    string stateJson = client.GetStringAsync($"http://127.0.0.1:{port}/api/state").GetAwaiter().GetResult();
                    AssertTrue(stateJson.Contains("\"snapshot\"") && stateJson.Contains("\"alarms\""), "/api/state missing expected fields.");

                    string telemJson = client.GetStringAsync($"http://127.0.0.1:{port}/api/telemetry").GetAwaiter().GetResult();
                    AssertTrue(telemJson.Contains("\"lines\""), "/api/telemetry missing lines array.");

                    string intentPayload = PlantIntent.SetRodBank("A", 0.25, "http-test").ToJson();
                    var postResp = client.PostAsync(
                        $"http://127.0.0.1:{port}/api/intent",
                        new StringContent(intentPayload, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                    AssertTrue(postResp.IsSuccessStatusCode, "POST /api/intent failed.");

                    sim.Tick();
                    AssertTrue(Math.Abs(sim.Plant.Banks[3].InsertionTarget - 0.25) < 1e-6,
                        "Intent posted via HTTP was not applied on next simulation tick.");
                }
            }
        }

        // U4: MCP Bridge stdio JSON-RPC 2.0 end-to-end test against live HttpApiServer
        private static void VerifyMcpBridge()
        {
            var sim = new Simulation();
            sim.LoadScenarioPreset("hot-standby");
            sim.Tick();

            int port = 17772;
            string tokenFile = Path.Combine(Path.GetTempPath(), "reactorsim-u4-" + Guid.NewGuid().ToString("N") + ".token");
            using (var server = new HttpApiServer(sim, port, null, tokenFile))
            {
                AssertTrue(server.Start(), "Failed to start HttpApiServer on port " + port);

                string bridgeDll = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory, "..", "..", "..", "..",
                    "ReactorSim.McpBridge", "bin", "Release", "net8.0", "ReactorSim.McpBridge.dll"));
                if (!File.Exists(bridgeDll))
                {
                    bridgeDll = Path.GetFullPath(Path.Combine(
                        AppContext.BaseDirectory, "..", "..", "..", "..",
                        "ReactorSim.McpBridge", "bin", "Debug", "net8.0", "ReactorSim.McpBridge.dll"));
                }
                AssertTrue(File.Exists(bridgeDll), "ReactorSim.McpBridge.dll not found at " + bridgeDll);

                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"\"{bridgeDll}\" --port {port} --token-file \"{tokenFile}\"",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    proc.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}");
                    string initResp = proc.StandardOutput.ReadLine();
                    AssertTrue(initResp != null && initResp.Contains("reactor-sim-mcp"), "MCP initialize failed: " + initResp);

                    proc.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}");
                    string listResp = proc.StandardOutput.ReadLine();
                    AssertTrue(listResp != null && listResp.Contains("plant_state") && listResp.Contains("plant_intent"), "MCP tools/list missing tools.");

                    proc.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"plant_intent\",\"arguments\":{\"kind\":\"RodBankTarget\",\"target\":\"A\",\"value\":0.33}}}");
                    string callResp = proc.StandardOutput.ReadLine();
                    AssertTrue(callResp != null && callResp.Contains("\"isError\":false"), "MCP plant_intent call failed: " + callResp);

                    sim.Tick();
                    AssertTrue(Math.Abs(sim.Plant.Banks[3].InsertionTarget - 0.33) < 1e-6, "MCP plant_intent did not mutate Bank A target.");

                    proc.StandardInput.Close();
                    proc.WaitForExit(2000);
                }
            }
        }

        // U5 & U8: Assistant Tiers and 3-Mission Arc
        private static void VerifyAssistantAndMissions()
        {
            // Mission 1 with Autopilot tier
            var sim = new Simulation();
            sim.LoadScenarioPreset("cold-shutdown");
            sim.Plant.Apply(PlantIntent.SetAssist(AssistTier.Autopilot));
            sim.Plant.Apply(PlantIntent.SetMission(0));
            sim.RunGameMinutes(15.0, null);
            AssertTrue(sim.Plant.Assistant.CurrentMission.Completed,
                $"Mission 1 should complete under Autopilot, stuck at step {sim.Plant.Assistant.CurrentMission.CurrentStepIndex}: {sim.Plant.Assistant.CurrentMission.Steps[sim.Plant.Assistant.CurrentMission.CurrentStepIndex].Title} (P={sim.Plant.Core.Power:P2}, rho={sim.Plant.Core.TotalReactivityPcm:F0})");

            // Mission 2: Power Ascension & SCRAM (LoadPreset("full-power") already selects Mission 2 at index 1)
            sim.LoadScenarioPreset("full-power");
            sim.Plant.Apply(PlantIntent.SetAssist(AssistTier.Autopilot));
            sim.RunGameMinutes(5.0, null);
            AssertTrue(sim.Plant.Assistant.CurrentMission.Completed,
                $"Mission 2 should complete, stuck at step {sim.Plant.Assistant.CurrentMission.CurrentStepIndex}");

            // Mission 3: The Xenon Pit
            sim.LoadScenarioPreset("xenon-pit");
            sim.Plant.Apply(PlantIntent.SetAssist(AssistTier.Autopilot));
            sim.RunGameMinutes(16.0, null);
            AssertTrue(sim.Plant.Assistant.CurrentMission.Completed,
                $"Mission 3 should complete, stuck at step {sim.Plant.Assistant.CurrentMission.CurrentStepIndex}");
        }

        // U9: Structured JSONL logging + Golden CSV recording & replay verification
        private static void VerifyLoggingAndGoldenReplay()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ReactorSim_U9_Test");
            Directory.CreateDirectory(tempDir);
            string jsonlPath = Path.Combine(tempDir, "run.jsonl");
            string goldenCsv = Path.Combine(tempDir, "golden.csv");

            var sim1 = new Simulation();
            using (var logger = new SimLogger(jsonlPath))
            using (var rec1 = new TelemetryRecorder(goldenCsv))
            {
                sim1.Logger = logger;
                sim1.Recorder = rec1;
                sim1.LoadScenarioPreset("hot-standby");
                sim1.EnqueueIntent(PlantIntent.SetRodBank("A", 0.42));
                sim1.EnqueueIntent(PlantIntent.SetSteamDemand(0.20));
                sim1.RunGameMinutes(3.0, null);
            }

            AssertTrue(File.Exists(jsonlPath) && new FileInfo(jsonlPath).Length > 100, "JSONL log file should be non-empty.");
            var goldenRows = TelemetryRecorder.LoadCsv(goldenCsv);
            int expectedRows = 3 * sim1.TicksPerGameMinute;
            AssertTrue(goldenRows.Count == expectedRows, $"Expected {expectedRows} CSV rows for 3 game-minutes, got {goldenRows.Count}");

            // Replay identical scenario and compare against golden CSV
            var sim2 = new Simulation();
            using (var rec2 = new TelemetryRecorder())
            {
                sim2.Recorder = rec2;
                sim2.LoadScenarioPreset("hot-standby");
                sim2.EnqueueIntent(PlantIntent.SetRodBank("A", 0.42));
                sim2.EnqueueIntent(PlantIntent.SetSteamDemand(0.20));
                sim2.RunGameMinutes(3.0, null);

                bool match = TelemetryRecorder.VerifyAgainstGolden(rec2.Rows, goldenRows, 1e-6, out string msg);
                AssertTrue(match, "Golden replay mismatch: " + msg);
            }
        }

        private static int RunIntentScriptFile(string scriptPath, string logPath, string recordPath)
        {
            if (!File.Exists(scriptPath))
            {
                Console.Error.WriteLine("Script file not found: " + scriptPath);
                return 1;
            }
            var intents = PlantIntent.ParseListFromJson(File.ReadAllText(scriptPath));
            var sim = new Simulation { DevMode = true }; // headless replay is dev tooling: scripts may LoadPreset
            if (!string.IsNullOrEmpty(logPath)) sim.Logger = new SimLogger(logPath);
            if (!string.IsNullOrEmpty(recordPath)) sim.Recorder = new TelemetryRecorder(recordPath);

            try
            {
                int nextIdx = 0;
                double endMin = 15.0;
                foreach (var it in intents)
                {
                    if (it.AtMinute + 2.0 > endMin) endMin = it.AtMinute + 2.0;
                }

                int totalTicks = (int)Math.Round(endMin * sim.TicksPerGameMinute);
                for (int t = 0; t < totalTicks; t++)
                {
                    while (nextIdx < intents.Count && intents[nextIdx].AtMinute <= sim.GameMinutes + 1e-6)
                    {
                        sim.EnqueueIntent(intents[nextIdx]);
                        nextIdx++;
                    }
                    sim.Tick();
                    if ((t + 1) % sim.TicksPerGameMinute == 0)
                        Console.WriteLine(sim.Plant.Telemetry(sim.GameMinutes));
                }
                return 0;
            }
            finally
            {
                sim.Logger?.Dispose();
                sim.Recorder?.Dispose();
            }
        }

        private static int RunGoldenReplay(string replayScriptPath, string goldenCsvPath, string logPath)
        {
            string tempCsv = Path.GetTempFileName();
            int rc = RunIntentScriptFile(replayScriptPath, logPath, tempCsv);
            if (rc != 0) return rc;

            var actual = TelemetryRecorder.LoadCsv(tempCsv);
            var golden = TelemetryRecorder.LoadCsv(goldenCsvPath);
            if (TelemetryRecorder.VerifyAgainstGolden(actual, golden, 1e-4, out string msg))
            {
                Console.WriteLine("Golden replay verified: OK (" + actual.Count + " ticks)");
                return 0;
            }
            Console.Error.WriteLine("Golden replay FAILED: " + msg);
            return 1;
        }

        private static int RunHttpServerInteractive(int port, string logPath)
        {
            var sim = new Simulation();
            sim.LoadScenarioPreset("hot-standby");
            if (!string.IsNullOrEmpty(logPath)) sim.Logger = new SimLogger(logPath);

            using (var server = new HttpApiServer(sim, port))
            {
                if (!server.Start())
                {
                    Console.Error.WriteLine("Failed to bind HTTP API on 127.0.0.1:" + port);
                    return 1;
                }
                Console.WriteLine($"ReactorSim HTTP server listening on http://127.0.0.1:{port}/ (Ctrl+C to stop)");
                Console.WriteLine($"Session token file (send as {HttpApiServer.TokenHeader}): {server.TokenFilePath}");
                while (true)
                {
                    sim.Tick();
                    Thread.Sleep((int)sim.RealMsPerTick);
                }
            }
        }
    }
}
