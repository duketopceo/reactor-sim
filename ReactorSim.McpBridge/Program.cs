// ReactorSim.McpBridge — U4: MCP (Model Context Protocol) JSON-RPC 2.0 stdio bridge.
// Proxies MCP tool calls and resource reads to the running ReactorSim HTTP API (127.0.0.1:7770).
// Tools exposed:
//   - plant_state      (GET /api/state)
//   - plant_alarms     (GET /api/alarms)
//   - plant_intent     (POST /api/intent)
//   - scenario_run     (POST /api/scenario/{name})
// Resources exposed:
//   - reactor://telemetry/history (GET /api/telemetry)
//   - reactor://assistant/log     (GET /api/assistant)

using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ReactorSim.McpBridge
{
    public static class Program
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private static string _baseUrl = "http://127.0.0.1:7770";

        // U1/KTD9: the API requires a per-session token. The server writes it to a session file
        // (HttpApiServer.DefaultTokenFilePath(port), or --token-file); it is re-read on every request
        // so a restarted game (new token) works without restarting the bridge.
        private static string _tokenFile;

        private static string ReadToken()
        {
            string path = _tokenFile ?? HttpApiServer.DefaultTokenFilePath(new Uri(_baseUrl).Port);
            if (!File.Exists(path))
                throw new InvalidOperationException("API token file not found at " + path + " (is the simulator's dev API running?)");
            return File.ReadAllText(path).Trim();
        }

        private static async Task<string> GetAsync(string url)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add(HttpApiServer.TokenHeader, ReadToken());
            using var resp = await Http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync();
        }

        private static async Task<HttpResponseMessage> PostAsync(string url, HttpContent content)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            req.Headers.Add(HttpApiServer.TokenHeader, ReadToken());
            return await Http.SendAsync(req);
        }

        public static async Task<int> Main(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--url" && i + 1 < args.Length)
                    _baseUrl = args[++i].TrimEnd('/');
                else if (args[i] == "--port" && i + 1 < args.Length)
                    _baseUrl = "http://127.0.0.1:" + args[++i];
                else if (args[i] == "--token-file" && i + 1 < args.Length)
                    _tokenFile = args[++i];
            }

            Console.InputEncoding = new UTF8Encoding(false);
            Console.OutputEncoding = new UTF8Encoding(false);

            using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    string method = root.TryGetProperty("method", out var mProp) ? mProp.GetString() : "";
                    bool hasId = root.TryGetProperty("id", out var idElem);

                    if (!hasId)
                    {
                        // JSON-RPC notification (e.g. notifications/initialized) -> no response
                        continue;
                    }

                    string idRaw = idElem.GetRawText();
                    string responseJson = await HandleRpcAsync(method, root, idRaw);
                    if (!string.IsNullOrEmpty(responseJson))
                    {
                        Console.Out.WriteLine(responseJson);
                        await Console.Out.FlushAsync();
                    }
                }
                catch (Exception ex)
                {
                    string err = $"{{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{{\"code\":-32700,\"message\":\"{PlantIntent.EscapeJson(ex.Message)}\"}}}}";
                    Console.Out.WriteLine(err);
                    await Console.Out.FlushAsync();
                }
            }

            return 0;
        }

        private static async Task<string> HandleRpcAsync(string method, JsonElement root, string idRaw)
        {
            switch (method)
            {
                case "initialize":
                    return $"{{\"jsonrpc\":\"2.0\",\"id\":{idRaw},\"result\":{{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{{\"tools\":{{}},\"resources\":{{}}}},\"serverInfo\":{{\"name\":\"reactor-sim-mcp\",\"version\":\"1.0.0\"}}}}}}";

                case "ping":
                    return $"{{\"jsonrpc\":\"2.0\",\"id\":{idRaw},\"result\":{{}}}}";

                case "tools/list":
                    return BuildToolsListResponse(idRaw);

                case "tools/call":
                    return await HandleToolCallAsync(root, idRaw);

                case "resources/list":
                    return BuildResourcesListResponse(idRaw);

                case "resources/read":
                    return await HandleResourceReadAsync(root, idRaw);

                default:
                    return $"{{\"jsonrpc\":\"2.0\",\"id\":{idRaw},\"error\":{{\"code\":-32601,\"message\":\"Method not found: {PlantIntent.EscapeJson(method)}\"}}}}";
            }
        }

        private static string BuildToolsListResponse(string idRaw)
        {
            return $"{{\"jsonrpc\":\"2.0\",\"id\":{idRaw},\"result\":{{\"tools\":[" +
                   "{\"name\":\"plant_state\",\"description\":\"Get full reactor plant state snapshot, reactivity ledger, active alarms, latest assistant advisory, and active mission progress.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}," +
                   "{\"name\":\"plant_alarms\",\"description\":\"Get active reactor alarms with cause, value vs limit, trend, and recommended next action.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}," +
                   "{\"name\":\"plant_intent\",\"description\":\"Submit a control intent to the reactor plant (e.g., RodBankTarget, AllRodsTarget, DiluteRate, BorateRate, SteamDemand, PrimaryFlow, HeaterPower, Spray, ReliefValve, Scram, ResetScram, AssistTier, SelectMission).\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\"},\"target\":{\"type\":\"string\"},\"value\":{\"type\":\"number\"},\"issuer\":{\"type\":\"string\"}},\"required\":[\"kind\",\"value\"]}}," +
                   "{\"name\":\"scenario_run\",\"description\":\"Load a reactor operational scenario preset (cold-shutdown, hot-standby, full-power, xenon-pit).\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}},\"required\":[\"name\"]}}" +
                   "]}}";
        }

        private static async Task<string> HandleToolCallAsync(JsonElement root, string idRaw)
        {
            if (!root.TryGetProperty("params", out var prms) || !prms.TryGetProperty("name", out var nameProp))
                return $"{{\"jsonrpc\":\"2.0\",\"id\":{idRaw},\"error\":{{\"code\":-32602,\"message\":\"Missing tool name\"}}}}";

            string toolName = nameProp.GetString();
            JsonElement args = prms.TryGetProperty("arguments", out var a) ? a : default;

            try
            {
                string payloadText;
                if (toolName == "plant_state")
                {
                    payloadText = await GetAsync($"{_baseUrl}/api/state");
                }
                else if (toolName == "plant_alarms")
                {
                    payloadText = await GetAsync($"{_baseUrl}/api/alarms");
                }
                else if (toolName == "plant_intent")
                {
                    string kind = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("kind", out var k) ? k.GetString() : "RodBankTarget";
                    string target = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("target", out var t) ? t.GetString() : "A";
                    double val = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("value", out var v) ? v.GetDouble() : 0.0;
                    string issuer = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("issuer", out var iss) ? iss.GetString() : "mcp";

                    if (!Enum.TryParse<IntentKind>(kind, true, out var parsedKind))
                        parsedKind = IntentKind.RodBankTarget;

                    var intent = new PlantIntent(parsedKind, target, val, issuer);
                    var resp = await PostAsync(
                        $"{_baseUrl}/api/intent",
                        new StringContent(intent.ToJson(), Encoding.UTF8, "application/json"));
                    payloadText = await resp.Content.ReadAsStringAsync();
                }
                else if (toolName == "scenario_run")
                {
                    string name = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("name", out var n) ? n.GetString() : "hot-standby";
                    var resp = await PostAsync(
                        $"{_baseUrl}/api/scenario/{Uri.EscapeDataString(name)}",
                        new StringContent("{}", Encoding.UTF8, "application/json"));
                    payloadText = await resp.Content.ReadAsStringAsync();
                }
                else
                {
                    return $"{{\"jsonrpc\":\"2.0\",\"id\":{idRaw},\"error\":{{\"code\":-32602,\"message\":\"Unknown tool: {PlantIntent.EscapeJson(toolName)}\"}}}}";
                }

                return $"{{\"jsonrpc\":\"2.0\",\"id\":{idRaw},\"result\":{{\"content\":[{{\"type\":\"text\",\"text\":\"{PlantIntent.EscapeJson(payloadText)}\"}}],\"isError\":false}}}}";
            }
            catch (Exception ex)
            {
                return $"{{\"jsonrpc\":\"2.0\",\"id\":{idRaw},\"result\":{{\"content\":[{{\"type\":\"text\",\"text\":\"HTTP bridge error: {PlantIntent.EscapeJson(ex.Message)}\"}}],\"isError\":true}}}}";
            }
        }

        private static string BuildResourcesListResponse(string idRaw)
        {
            return $"{{\"jsonrpc\":\"2.0\",\"id\":{idRaw},\"result\":{{\"resources\":[" +
                   "{\"uri\":\"reactor://telemetry/history\",\"name\":\"Reactor Telemetry History\",\"mimeType\":\"application/json\",\"description\":\"Recent per-tick reactor telemetry lines.\"}," +
                   "{\"uri\":\"reactor://assistant/log\",\"name\":\"Reactor Ops Assistant Log\",\"mimeType\":\"application/json\",\"description\":\"Structured log of assistant findings, evidence, and recommendations.\"}" +
                   "]}}";
        }

        private static async Task<string> HandleResourceReadAsync(JsonElement root, string idRaw)
        {
            string uri = root.TryGetProperty("params", out var p) && p.TryGetProperty("uri", out var u) ? u.GetString() : "";
            try
            {
                string endpoint = uri == "reactor://assistant/log" ? "/api/assistant" : "/api/telemetry";
                string text = await GetAsync($"{_baseUrl}{endpoint}");
                return $"{{\"jsonrpc\":\"2.0\",\"id\":{idRaw},\"result\":{{\"contents\":[{{\"uri\":\"{PlantIntent.EscapeJson(uri)}\",\"mimeType\":\"application/json\",\"text\":\"{PlantIntent.EscapeJson(text)}\"}}]}}}}";
            }
            catch (Exception ex)
            {
                return $"{{\"jsonrpc\":\"2.0\",\"id\":{idRaw},\"error\":{{\"code\":-32000,\"message\":\"{PlantIntent.EscapeJson(ex.Message)}\"}}}}";
            }
        }
    }
}
