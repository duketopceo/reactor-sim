using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace ReactorSim
{
    /// <summary>
    /// U11: writes one playtest session to <c>root/&lt;yyyyMMdd-HHmmss&gt;/</c>: log.jsonl (SimLogger), telemetry.csv
    /// (TelemetryRecorder), session.json (header, rewritten at each checkpoint) and intent-log.jsonl (the
    /// Simulation intent log, rewritten at each checkpoint and at Finish). Plain C#, no UnityEngine, so it is
    /// unit-tested in ReactorSim.Core.Tests; the SessionLog MonoBehaviour only wires it to the player.
    /// The Core writers auto-flush per line, so a crash loses at most the line being written.
    /// </summary>
    public sealed class SessionRecorder : IDisposable
    {
        private readonly Simulation _sim;
        private readonly DateTime _start;
        private readonly string _buildVersion;
        private readonly string _mode;
        private bool _finished;

        public string Directory { get; private set; }

        public static string FolderName(DateTime t)
        {
            return t.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        }

        public SessionRecorder(string root, DateTime start, Simulation sim, string buildVersion, string mode)
        {
            _sim = sim;
            _start = start;
            _buildVersion = buildVersion ?? "";
            _mode = mode ?? "";

            string baseDir = Path.Combine(root, FolderName(start));
            string dir = baseDir;
            for (int n = 2; System.IO.Directory.Exists(dir); n++) dir = baseDir + "-" + n.ToString(CultureInfo.InvariantCulture);
            System.IO.Directory.CreateDirectory(dir);
            Directory = dir;

            sim.Logger = new SimLogger(Path.Combine(dir, "log.jsonl"));
            sim.Recorder = new TelemetryRecorder(Path.Combine(dir, "telemetry.csv"));
            WriteHeader(false);
        }

        /// <summary>Rewrites session.json and intent-log.jsonl; call periodically so a crash leaves a usable record.</summary>
        public void Checkpoint()
        {
            if (_finished) return;
            WriteIntentLog();
            WriteHeader(false);
        }

        /// <summary>Final dump (quit): intent log, closing header, then the writers are closed.</summary>
        public void Finish()
        {
            if (_finished) return;
            try
            {
                WriteIntentLog();
                WriteHeader(true);
            }
            finally
            {
                // Always close the writers and unhook the sim, even when a final write fails (disk full, locked).
                _finished = true;
                Detach();
            }
        }

        public void Dispose()
        {
            if (!_finished) Finish();
        }

        private void Detach()
        {
            if (_sim.Logger != null) { _sim.Logger.Dispose(); _sim.Logger = null; }
            if (_sim.Recorder != null) { _sim.Recorder.Dispose(); _sim.Recorder = null; }
        }

        private void WriteIntentLog()
        {
            var sb = new StringBuilder(1024);
            foreach (var e in _sim.GetIntentLogSnapshot())
            {
                sb.Append("{\"sessionTick\":").Append(e.SessionTick.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"seq\":").Append(e.Seq.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"class\":\"").Append(e.Class.ToString()).Append('"');
                sb.Append(",\"rejected\":").Append(e.Rejected ? "true" : "false");
                sb.Append(",\"error\":\"").Append(Esc(e.Error)).Append('"');
                sb.Append(",\"intent\":").Append(e.Intent != null ? e.Intent.ToJson() : "null");
                sb.Append("}\n");
            }
            File.WriteAllText(Path.Combine(Directory, "intent-log.jsonl"), sb.ToString(), new UTF8Encoding(false));
        }

        private void WriteHeader(bool ended)
        {
            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(512);
            sb.Append("{\"buildVersion\":\"").Append(Esc(_buildVersion)).Append('"');
            sb.Append(",\"mode\":\"").Append(Esc(_mode)).Append('"');
            sb.Append(",\"startLocal\":\"").Append(_start.ToString("yyyy-MM-ddTHH:mm:ss", ic)).Append('"');
            sb.Append(",\"startUtc\":\"").Append(_start.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", ic)).Append('"');
            sb.Append(",\"ticks\":").Append(_sim.TickCount.ToString(ic));
            sb.Append(",\"sessionTicks\":").Append(_sim.SessionTick.ToString(ic));
            sb.Append(",\"ended\":").Append(ended ? "true" : "false");
            sb.Append(",\"tuning\":{");
            var tuning = _sim.Clock.Tuning;
            bool first = true;
            foreach (var f in typeof(Tuning).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = f.GetValue(tuning);
                if (!(v is double) && !(v is int)) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(f.Name).Append("\":").Append(Convert.ToString(v, ic));
            }
            sb.Append("}}\n");
            File.WriteAllText(Path.Combine(Directory, "session.json"), sb.ToString(), new UTF8Encoding(false));
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
