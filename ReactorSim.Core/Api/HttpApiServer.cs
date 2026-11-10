// ReactorSim.Core — U3: In-process HTTP JSON API for live inspection, intent submission, and MCP bridging.
// Uses System.Net.HttpListener (part of netstandard2.1) with zero external dependencies.

using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ReactorSim
{
    public sealed class HttpApiServer : IDisposable
    {
        private readonly Simulation _sim;
        private readonly object _simLock;
        private HttpListener _listener;
        private Thread _workerThread;
        private volatile bool _running;

        // KTD9 / R14: the server is dev-only. Nothing constructs it in normal play; callers create it on
        // request. Every request needs the per-session token in this header, and there are no CORS headers,
        // so a web page cannot read or drive it. The token is random per server instance and is written to
        // a session file (default: <LocalApplicationData>/ReactorSim/api-<port>.token) that the MCP bridge
        // reads; the file is deleted on Dispose. The file lives in the user's profile, not a shared temp dir.
        public const string TokenHeader = "X-ReactorSim-Token";

        private readonly string _tokenFilePath;

        public int Port { get; private set; }
        public bool IsRunning => _running;
        public string Token { get; }
        public string TokenFilePath => _tokenFilePath;

        public static string DefaultTokenFilePath(int port)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ReactorSim", "api-" + port + ".token");
        }

        public HttpApiServer(Simulation sim, int port = 7770, object simLock = null, string tokenFilePath = null)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _simLock = simLock ?? new object();
            Port = port;
            _tokenFilePath = string.IsNullOrEmpty(tokenFilePath) ? DefaultTokenFilePath(port) : tokenFilePath;
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder(32);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            Token = sb.ToString();
        }

        private static bool IsLoopbackHost(string hostHeader)
        {
            if (string.IsNullOrEmpty(hostHeader)) return false;
            string host = hostHeader;
            if (host.StartsWith("["))
            {
                int end = host.IndexOf(']');
                host = end > 0 ? host.Substring(0, end + 1) : host;
            }
            else
            {
                int colon = host.LastIndexOf(':');
                if (colon >= 0) host = host.Substring(0, colon);
            }
            return string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
                || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "[::1]", StringComparison.Ordinal);
        }

        private bool TokenMatches(string presented)
        {
            if (presented == null || presented.Length != Token.Length) return false;
            int diff = 0;
            for (int i = 0; i < Token.Length; i++) diff |= presented[i] ^ Token[i];
            return diff == 0;
        }

        public bool Start()
        {
            if (_running) return true;
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_tokenFilePath)));
                WriteTokenFile(_tokenFilePath, Token);
                _running = true;
                _workerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = $"ReactorSim-HttpApi-{Port}"
                };
                _workerThread.Start();
                return true;
            }
            catch (Exception)
            {
                _running = false;
                try { _listener?.Close(); } catch { }
                _listener = null;
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        private static extern int Chmod(string path, uint mode);

        // The token grants control of the plant: owner-only on Unix (Windows inherits the profile ACL).
        private static void WriteTokenFile(string path, string token)
        {
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                File.WriteAllText(path, "");
                if (Chmod(path, Convert.ToUInt32("600", 8)) != 0)
                    throw new IOException("could not restrict permissions on " + path);
            }
            File.WriteAllText(path, token);
        }

        private void ListenLoop()
        {
            while (_running && _listener != null && _listener.IsListening)
            {
                HttpListenerContext ctx = null;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception)
                {
                    continue;
                }

                if (ctx != null)
                {
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
                }
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var resp = ctx.Response;
            // No CORS headers on purpose: browsers get no cross-origin access, and a preflight
            // (OPTIONS) falls through to the 401/404 paths below.
            if (!IsLoopbackHost(req.Headers["Host"]))
            {
                WriteJson(resp, 403, "{\"error\":\"non_loopback_host\"}");
                return;
            }
            if (!TokenMatches(req.Headers[TokenHeader]))
            {
                WriteJson(resp, 401, "{\"error\":\"missing_or_invalid_token\"}");
                return;
            }

            string path = (req.Url?.AbsolutePath ?? "/").TrimEnd('/');
            if (string.IsNullOrEmpty(path)) path = "/";

            try
            {
                if (string.Equals(req.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(path, "/api/state", StringComparison.OrdinalIgnoreCase) || path == "/")
                    {
                        string json = BuildStateJson();
                        WriteJson(resp, 200, json);
                        return;
                    }
                    if (string.Equals(path, "/api/telemetry", StringComparison.OrdinalIgnoreCase))
                    {
                        string json = BuildTelemetryJson();
                        WriteJson(resp, 200, json);
                        return;
                    }
                    if (string.Equals(path, "/api/alarms", StringComparison.OrdinalIgnoreCase))
                    {
                        string json = BuildAlarmsJson();
                        WriteJson(resp, 200, json);
                        return;
                    }
                    if (string.Equals(path, "/api/assistant", StringComparison.OrdinalIgnoreCase))
                    {
                        string json = BuildAssistantLogJson();
                        WriteJson(resp, 200, json);
                        return;
                    }
                }
                else if (string.Equals(req.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    string body = "";
                    if (req.HasEntityBody)
                    {
                        using (var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                        {
                            body = sr.ReadToEnd();
                        }
                    }

                    if (string.Equals(path, "/api/intent", StringComparison.OrdinalIgnoreCase))
                    {
                        var intent = PlantIntent.FromJson(body);
                        if (intent == null)
                        {
                            WriteJson(resp, 400, "{\"error\":\"invalid_intent_json\"}");
                            return;
                        }
                        _sim.EnqueueIntent(intent);
                        WriteJson(resp, 200, $"{{\"ok\":true,\"queued\":{intent.ToJson()}}}");
                        return;
                    }

                    if (path.StartsWith("/api/scenario/", StringComparison.OrdinalIgnoreCase))
                    {
                        string scenario = path.Substring("/api/scenario/".Length).Trim();
                        var intent = PlantIntent.LoadPreset(scenario, "http");
                        _sim.EnqueueIntent(intent);
                        WriteJson(resp, 200, $"{{\"ok\":true,\"scenario\":\"{PlantIntent.EscapeJson(scenario)}\"}}");
                        return;
                    }
                }

                WriteJson(resp, 404, "{\"error\":\"not_found\"}");
            }
            catch (IntentRejectedException ex)
            {
                WriteJson(resp, 403, $"{{\"error\":\"intent_rejected\",\"message\":\"{PlantIntent.EscapeJson(ex.Message)}\"}}");
            }
            catch (Exception ex)
            {
                WriteJson(resp, 400, $"{{\"error\":\"{PlantIntent.EscapeJson(ex.Message)}\"}}");
            }
        }

        private string BuildStateJson()
        {
            PlantSnapshot snap;
            string alarmsJson;
            string advisoryJson;
            string missionJson;

            lock (_simLock)
            {
                snap = _sim.CaptureSnapshot();
                alarmsJson = BuildAlarmsJsonUnlocked();
                var latest = _sim.Plant.Assistant.LatestAdvisory;
                advisoryJson = latest != null ? latest.ToJson() : "null";
                var m = _sim.Plant.Assistant.CurrentMission;
                if (m != null)
                {
                    int idx = m.CurrentStepIndex;
                    var step = (m.Steps.Count > 0 && idx < m.Steps.Count) ? m.Steps[idx] : null;
                    string stepTitle = step != null ? PlantIntent.EscapeJson(step.Title) : "Complete";
                    string stepInst = step != null ? PlantIntent.EscapeJson(step.Instruction) : "All steps complete.";
                    missionJson = $"{{\"id\":\"{PlantIntent.EscapeJson(m.Id)}\",\"title\":\"{PlantIntent.EscapeJson(m.Title)}\",\"stepIndex\":{idx},\"totalSteps\":{m.Steps.Count},\"completed\":{(m.Completed ? "true" : "false")},\"currentStepTitle\":\"{stepTitle}\",\"currentStepInstruction\":\"{stepInst}\"}}";
                }
                else
                {
                    missionJson = "null";
                }
            }

            var sb = new StringBuilder(1024);
            sb.Append("{\"snapshot\":");
            sb.Append(snap.ToJson());
            sb.Append(",\"alarms\":");
            sb.Append(alarmsJson);
            sb.Append(",\"latestAdvisory\":");
            sb.Append(advisoryJson);
            sb.Append(",\"mission\":");
            sb.Append(missionJson);
            sb.Append('}');
            return sb.ToString();
        }

        private string BuildTelemetryJson()
        {
            var lines = _sim.GetTelemetryHistorySnapshot();
            var sb = new StringBuilder(lines.Count * 96 + 32);
            sb.Append("{\"lines\":[");
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(PlantIntent.EscapeJson(lines[i])).Append('"');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private string BuildAlarmsJson()
        {
            lock (_simLock)
            {
                return BuildAlarmsJsonUnlocked();
            }
        }

        private string BuildAlarmsJsonUnlocked()
        {
            var alarms = _sim.Plant.Assistant.ActiveAlarms;
            var sb = new StringBuilder(256);
            sb.Append('[');
            for (int i = 0; i < alarms.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(alarms[i].ToJson());
            }
            sb.Append(']');
            return sb.ToString();
        }

        private string BuildAssistantLogJson()
        {
            lock (_simLock)
            {
                var log = _sim.Plant.Assistant.MessageLog;
                var sb = new StringBuilder(512);
                sb.Append('[');
                for (int i = 0; i < log.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(log[i].ToJson());
                }
                sb.Append(']');
                return sb.ToString();
            }
        }

        private static void WriteJson(HttpListenerResponse resp, int statusCode, string json)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                resp.StatusCode = statusCode;
                resp.ContentType = "application/json; charset=utf-8";
                resp.ContentLength64 = bytes.Length;
                resp.OutputStream.Write(bytes, 0, bytes.Length);
                resp.OutputStream.Close();
            }
            catch
            {
                // Ignore client disconnect errors
            }
        }

        public void Dispose()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
            try { if (File.Exists(_tokenFilePath) && File.ReadAllText(_tokenFilePath) == Token) File.Delete(_tokenFilePath); } catch { }
        }
    }
}
