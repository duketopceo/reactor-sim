// ReactorSim.Core — U9: Structured JSONL tick logger, CSV telemetry recorder, and golden replay verifier.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ReactorSim
{
    public sealed class SimLogger : IDisposable
    {
        private readonly StreamWriter _writer;

        public SimLogger(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            _writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
        }

        public void LogTick(Simulation sim, List<PlantIntent> appliedIntents)
        {
            if (_writer == null) return;
            var p = sim.Plant;
            var c = p.Core;
            var t = p.Thermal;
            var ch = p.Chemistry;
            var a = p.Assistant;

            var sb = new StringBuilder(512);
            sb.Append("{\"tick\":");
            sb.Append(sim.TickCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"gameMin\":");
            sb.Append(sim.GameMinutes.ToString("F2", CultureInfo.InvariantCulture));
            sb.Append(",\"power\":");
            sb.Append(c.Power.ToString("G9", CultureInfo.InvariantCulture));
            sb.Append(",\"rhoPcm\":");
            sb.Append(c.TotalReactivityPcm.ToString("F2", CultureInfo.InvariantCulture));
            sb.Append(",\"fuelTempC\":");
            sb.Append(t.FuelTempC.ToString("F2", CultureInfo.InvariantCulture));
            sb.Append(",\"priTempC\":");
            sb.Append(t.PrimaryTempC.ToString("F2", CultureInfo.InvariantCulture));
            sb.Append(",\"priBar\":");
            sb.Append(t.PrimaryPressureBar.ToString("F2", CultureInfo.InvariantCulture));
            sb.Append(",\"sgBar\":");
            sb.Append(t.SecondaryPressureBar.ToString("F2", CultureInfo.InvariantCulture));
            sb.Append(",\"xe\":");
            sb.Append(c.Xenon135.ToString("F4", CultureInfo.InvariantCulture));
            sb.Append(",\"iodine\":");
            sb.Append(c.Iodine135.ToString("F4", CultureInfo.InvariantCulture));
            sb.Append(",\"boronPpm\":");
            sb.Append(ch.BoronPpm.ToString("F2", CultureInfo.InvariantCulture));
            sb.Append(",\"scram\":");
            sb.Append(p.Scrammed ? "true" : "false");

            sb.Append(",\"intents\":[");
            if (appliedIntents != null)
            {
                for (int i = 0; i < appliedIntents.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(appliedIntents[i].ToJson());
                }
            }
            sb.Append("],\"alarms\":[");
            for (int i = 0; i < a.ActiveAlarms.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(a.ActiveAlarms[i].Code).Append('"');
            }
            sb.Append("]}");
            _writer.WriteLine(sb.ToString());
        }

        public void Dispose()
        {
            _writer?.Dispose();
        }
    }

    public sealed class TelemetryRecorder : IDisposable
    {
        private readonly StreamWriter _writer;
        public List<TelemetryRow> Rows { get; } = new List<TelemetryRow>();

        public struct TelemetryRow
        {
            public int Tick;
            public double GameMinutes;
            public double PowerFraction;
            public double ReactivityPcm;
            public double FuelTempC;
            public double PrimaryTempC;
            public double PrimaryPressureBar;
            public double SgPressureBar;
            public double XenonConc;
            public double BoronPpm;

            public string ToCsv()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R},{9:R}",
                    Tick, GameMinutes, PowerFraction, ReactivityPcm, FuelTempC,
                    PrimaryTempC, PrimaryPressureBar, SgPressureBar, XenonConc, BoronPpm);
            }

            public static TelemetryRow FromCsv(string line)
            {
                var parts = line.Split(',');
                return new TelemetryRow
                {
                    Tick = int.Parse(parts[0], CultureInfo.InvariantCulture),
                    GameMinutes = double.Parse(parts[1], CultureInfo.InvariantCulture),
                    PowerFraction = double.Parse(parts[2], CultureInfo.InvariantCulture),
                    ReactivityPcm = double.Parse(parts[3], CultureInfo.InvariantCulture),
                    FuelTempC = double.Parse(parts[4], CultureInfo.InvariantCulture),
                    PrimaryTempC = double.Parse(parts[5], CultureInfo.InvariantCulture),
                    PrimaryPressureBar = double.Parse(parts[6], CultureInfo.InvariantCulture),
                    SgPressureBar = double.Parse(parts[7], CultureInfo.InvariantCulture),
                    XenonConc = double.Parse(parts[8], CultureInfo.InvariantCulture),
                    BoronPpm = double.Parse(parts[9], CultureInfo.InvariantCulture)
                };
            }
        }

        public const string CsvHeader = "tick,game_min,power_frac,rho_pcm,fuel_temp_c,pri_temp_c,pri_bar,sg_bar,xenon,boron_ppm";

        public TelemetryRecorder(string csvPath = null)
        {
            if (!string.IsNullOrEmpty(csvPath))
            {
                string dir = Path.GetDirectoryName(csvPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                _writer = new StreamWriter(csvPath, false, new UTF8Encoding(false)) { AutoFlush = true };
                _writer.WriteLine(CsvHeader);
            }
        }

        public void RecordTick(Simulation sim)
        {
            var p = sim.Plant;
            var row = new TelemetryRow
            {
                Tick = sim.TickCount,
                GameMinutes = sim.GameMinutes,
                PowerFraction = p.Core.Power,
                ReactivityPcm = p.Core.TotalReactivityPcm,
                FuelTempC = p.Thermal.FuelTempC,
                PrimaryTempC = p.Thermal.PrimaryTempC,
                PrimaryPressureBar = p.Thermal.PrimaryPressureBar,
                SgPressureBar = p.Thermal.SecondaryPressureBar,
                XenonConc = p.Core.Xenon135,
                BoronPpm = p.Chemistry.BoronPpm
            };
            Rows.Add(row);
            _writer?.WriteLine(row.ToCsv());
        }

        public static List<TelemetryRow> LoadCsv(string path)
        {
            var list = new List<TelemetryRow>();
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("tick", StringComparison.OrdinalIgnoreCase))
                    continue;
                list.Add(TelemetryRow.FromCsv(line));
            }
            return list;
        }

        public static bool VerifyAgainstGolden(IList<TelemetryRow> actual, IList<TelemetryRow> golden, double tolerance, out string mismatchMessage)
        {
            if (actual.Count != golden.Count)
            {
                mismatchMessage = $"Row count mismatch: actual={actual.Count}, golden={golden.Count}";
                return false;
            }
            for (int i = 0; i < actual.Count; i++)
            {
                var a = actual[i];
                var g = golden[i];
                if (a.Tick != g.Tick)
                {
                    mismatchMessage = $"Tick mismatch at row {i}: actual={a.Tick}, golden={g.Tick}";
                    return false;
                }
                // Every recorded column is compared (game minutes, primary temperature, SG pressure
                // and boron used to be skipped, so a replay diverging only there passed).
                if (!CloseEnough(a.GameMinutes, g.GameMinutes, 1e-9) ||
                    !CloseEnough(a.PowerFraction, g.PowerFraction, tolerance) ||
                    !CloseEnough(a.ReactivityPcm, g.ReactivityPcm, tolerance * 100.0) ||
                    !CloseEnough(a.FuelTempC, g.FuelTempC, tolerance * 10.0) ||
                    !CloseEnough(a.PrimaryTempC, g.PrimaryTempC, tolerance * 10.0) ||
                    !CloseEnough(a.PrimaryPressureBar, g.PrimaryPressureBar, tolerance * 10.0) ||
                    !CloseEnough(a.SgPressureBar, g.SgPressureBar, tolerance * 10.0) ||
                    !CloseEnough(a.XenonConc, g.XenonConc, tolerance) ||
                    !CloseEnough(a.BoronPpm, g.BoronPpm, tolerance * 100.0))
                {
                    mismatchMessage = $"Value mismatch at tick {a.Tick}: actual=({a.ToCsv()}) vs golden=({g.ToCsv()})";
                    return false;
                }
            }
            mismatchMessage = "OK";
            return true;
        }

        private static bool CloseEnough(double a, double b, double tol)
        {
            return Math.Abs(a - b) <= tol;
        }

        public void Dispose()
        {
            _writer?.Dispose();
        }
    }
}
