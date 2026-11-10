using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace ReactorSim.Tests
{
    // U1 / KTD9: the local API is token-gated, has no browser cross-origin access,
    // rejects non-loopback Host headers, and cannot be used to run Dev intents.
    public class ApiSecurityTests : IDisposable
    {
        private readonly Simulation _sim = new Simulation();
        private readonly HttpApiServer _server;
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private readonly string _tokenFile = Path.Combine(Path.GetTempPath(), "reactorsim-test-" + Guid.NewGuid().ToString("N") + ".token");
        private readonly int _port;

        public ApiSecurityTests()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            _port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            _server = new HttpApiServer(_sim, _port, null, _tokenFile);
            Assert.True(_server.Start());
        }

        public void Dispose()
        {
            _server.Dispose();
            _http.Dispose();
            try { File.Delete(_tokenFile); } catch { }
        }

        private string Url(string path) { return "http://127.0.0.1:" + _port + path; }

        private HttpRequestMessage Post(string path, string body, bool withToken, string host = null)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, Url(path))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (withToken) req.Headers.Add(HttpApiServer.TokenHeader, _server.Token);
            if (host != null) req.Headers.Host = host;
            return req;
        }

        [Fact]
        public async Task IntentWithoutToken_Returns401_AndChangesNothing()
        {
            var resp = (await _http.SendAsync(Post("/api/intent", PlantIntent.SetSteamDemand(0.5, "web").ToJson(), false)));
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            Assert.Empty(_sim.DrainPendingIntents());
        }

        [Fact]
        public async Task IntentWithWrongToken_Returns401()
        {
            var req = Post("/api/intent", PlantIntent.SetSteamDemand(0.5, "web").ToJson(), false);
            req.Headers.Add(HttpApiServer.TokenHeader, "not-the-token");
            Assert.Equal(HttpStatusCode.Unauthorized, (await _http.SendAsync(req)).StatusCode);
            Assert.Empty(_sim.DrainPendingIntents());
        }

        [Fact]
        public async Task StateWithoutToken_Returns401()
        {
            var resp = (await _http.GetAsync(Url("/api/state")));
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task IntentWithToken_IsQueued()
        {
            var resp = (await _http.SendAsync(Post("/api/intent", PlantIntent.SetSteamDemand(0.5, "web").ToJson(), true)));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var queued = _sim.DrainPendingIntents();
            Assert.Single(queued);
            Assert.Equal(IntentKind.SteamDemand, queued[0].Kind);
        }

        [Fact]
        public async Task Responses_CarryNoCorsHeaders()
        {
            var req = new HttpRequestMessage(HttpMethod.Get, Url("/api/state"));
            req.Headers.Add(HttpApiServer.TokenHeader, _server.Token);
            req.Headers.Add("Origin", "https://evil.example");
            var ok = (await _http.SendAsync(req));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.False(ok.Headers.Contains("Access-Control-Allow-Origin"));

            var denied = (await _http.GetAsync(Url("/api/state")));
            Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
        }

        [Fact]
        public async Task NonLoopbackHost_IsRejected_EvenWithToken()
        {
            var resp = (await _http.SendAsync(Post("/api/intent", PlantIntent.SetSteamDemand(0.5, "web").ToJson(), true, "evil.example")));
            Assert.False(resp.IsSuccessStatusCode);
            Assert.Empty(_sim.DrainPendingIntents());
        }

        [Fact]
        public async Task DevIntentOverApi_WithDevModeOff_IsRejected_EvenWithValidToken()
        {
            var resp = (await _http.SendAsync(Post("/api/intent", PlantIntent.LoadPreset("full-power", "http").ToJson(), true)));
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            var scen = (await _http.SendAsync(Post("/api/scenario/full-power", "{}", true)));
            Assert.Equal(HttpStatusCode.Forbidden, scen.StatusCode);
            Assert.Empty(_sim.DrainPendingIntents());
        }

        [Fact]
        public async Task DevIntentOverApi_WithDevModeOn_IsQueued()
        {
            _sim.DevMode = true;
            var resp = (await _http.SendAsync(Post("/api/scenario/full-power", "{}", true)));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Single(_sim.DrainPendingIntents());
        }

        [Fact]
        public void TokenFile_HoldsSessionToken_AndIsRemovedOnDispose()
        {
            Assert.Equal(_server.Token, File.ReadAllText(_tokenFile).Trim());
            Assert.True(_server.Token.Length >= 32);
            _server.Dispose();
            Assert.False(File.Exists(_tokenFile));
        }
    }
}
