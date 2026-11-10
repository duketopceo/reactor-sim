// ReactorSim.Unity — U7: Host-authoritative 2-player co-op session manager.
// Host runs Plant.Step() and broadcasts PlantSnapshot JSON lines at 4 Hz over TCP.
// Client receives PlantSnapshot JSON lines (applying them via Sim.RestoreSnapshot)
// and sends PlantIntent JSON lines to Host (which enqueues them via Sim.EnqueueIntent).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using ReactorSim;

public class CoopSessionManager : MonoBehaviour
{
    public enum SessionRole
    {
        Standalone,
        Host,
        Client
    }

    public SessionRole Role { get; private set; } = SessionRole.Standalone;
    public int HostPort { get; private set; } = 7777;
    public string ConnectedEndpoint { get; private set; } = "";
    public string StatusMessage { get; private set; } = "Standalone (Single Operator)";
    public int ConnectedPeerCount
    {
        get
        {
            lock (_peersLock) { return _hostPeers.Count; }
        }
    }

    private PlantBridge _bridge;

    // Host fields
    private TcpListener _listener;
    private Thread _acceptThread;
    private readonly object _peersLock = new object();
    private readonly List<PeerConnection> _hostPeers = new List<PeerConnection>();
    private float _broadcastTimer;
    public float BroadcastIntervalSeconds = 0.25f; // 4 Hz snapshot sync

    // Client fields
    private TcpClient _clientSocket;
    private StreamWriter _clientWriter;
    private Thread _clientReadThread;

    // Main-thread queues
    private readonly ConcurrentQueue<PlantIntent> _incomingHostIntents = new ConcurrentQueue<PlantIntent>();
    private readonly ConcurrentQueue<PlantSnapshot> _incomingClientSnapshots = new ConcurrentQueue<PlantSnapshot>();
    private volatile bool _running;

    private sealed class PeerConnection
    {
        public TcpClient Client;
        public StreamWriter Writer;
        public string Endpoint;
    }

    public void Initialize(PlantBridge bridge)
    {
        _bridge = bridge;
    }

    public bool StartHost(int port = 7777)
    {
        StopSession();
        try
        {
            HostPort = port;
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _running = true;
            Role = SessionRole.Host;
            StatusMessage = $"Hosting on 0.0.0.0:{port} (0 peers)";

            _acceptThread = new Thread(HostAcceptLoop)
            {
                IsBackground = true,
                Name = $"ReactorSim-CoopHost-{port}"
            };
            _acceptThread.Start();
            Debug.Log($"[CoopSession] Hosting co-op session on TCP port {port}");
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = "Host failed: " + ex.Message;
            Debug.LogWarning("[CoopSession] Failed to start host: " + ex.Message);
            StopSession();
            return false;
        }
    }

    public bool StartClient(string hostAddress = "127.0.0.1", int port = 7777)
    {
        StopSession();
        try
        {
            _clientSocket = new TcpClient();
            _clientSocket.NoDelay = true;
            _clientSocket.Connect(hostAddress, port);
            var stream = _clientSocket.GetStream();
            _clientWriter = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

            _running = true;
            Role = SessionRole.Client;
            ConnectedEndpoint = $"{hostAddress}:{port}";
            StatusMessage = $"Connected to Host {ConnectedEndpoint}";

            _clientReadThread = new Thread(() => ClientReadLoop(stream))
            {
                IsBackground = true,
                Name = "ReactorSim-CoopClient"
            };
            _clientReadThread.Start();
            Debug.Log($"[CoopSession] Connected as Partner Client to {ConnectedEndpoint}");
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = "Join failed: " + ex.Message;
            Debug.LogWarning("[CoopSession] Failed to join host: " + ex.Message);
            StopSession();
            return false;
        }
    }

    public void StopSession()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        _listener = null;

        lock (_peersLock)
        {
            foreach (var p in _hostPeers)
            {
                try { p.Client?.Close(); } catch { }
            }
            _hostPeers.Clear();
        }

        try { _clientSocket?.Close(); } catch { }
        _clientSocket = null;
        _clientWriter = null;

        Role = SessionRole.Standalone;
        ConnectedEndpoint = "";
        StatusMessage = "Standalone (Single Operator)";
    }

    public void SendIntentToHost(PlantIntent intent)
    {
        if (intent == null || Role != SessionRole.Client || _clientWriter == null)
            return;

        try
        {
            intent.Issuer = "partner";
            string line = "INTENT:" + intent.ToJson();
            _clientWriter.WriteLine(line);
        }
        catch (Exception ex)
        {
            StatusMessage = "Disconnected from Host: " + ex.Message;
            StopSession();
        }
    }

    private void HostAcceptLoop()
    {
        while (_running && _listener != null)
        {
            try
            {
                var client = _listener.AcceptTcpClient();
                client.NoDelay = true;
                var stream = client.GetStream();
                var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                string ep = client.Client.RemoteEndPoint != null ? client.Client.RemoteEndPoint.ToString() : "peer";

                var peer = new PeerConnection
                {
                    Client = client,
                    Writer = writer,
                    Endpoint = ep
                };

                lock (_peersLock)
                {
                    _hostPeers.Add(peer);
                    StatusMessage = $"Hosting :{HostPort} ({_hostPeers.Count} peer{(_hostPeers.Count == 1 ? "" : "s")})";
                }

                var readerThread = new Thread(() => HostPeerReadLoop(peer, stream))
                {
                    IsBackground = true,
                    Name = $"ReactorSim-Peer-{ep}"
                };
                readerThread.Start();
            }
            catch
            {
                if (!_running) break;
            }
        }
    }

    private void HostPeerReadLoop(PeerConnection peer, NetworkStream stream)
    {
        try
        {
            using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
            {
                string line;
                while (_running && (line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("INTENT:", StringComparison.Ordinal))
                    {
                        string json = line.Substring("INTENT:".Length);
                        var intent = PlantIntent.FromJson(json);
                        if (intent != null)
                            _incomingHostIntents.Enqueue(intent);
                    }
                }
            }
        }
        catch
        {
            // Peer disconnected
        }
        finally
        {
            lock (_peersLock)
            {
                _hostPeers.Remove(peer);
                if (_running && Role == SessionRole.Host)
                    StatusMessage = $"Hosting :{HostPort} ({_hostPeers.Count} peer{(_hostPeers.Count == 1 ? "" : "s")})";
            }
            try { peer.Client?.Close(); } catch { }
        }
    }

    private void ClientReadLoop(NetworkStream stream)
    {
        try
        {
            using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
            {
                string line;
                while (_running && (line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("STATE:", StringComparison.Ordinal))
                    {
                        string json = line.Substring("STATE:".Length);
                        var snap = PlantSnapshot.FromJson(json);
                        if (snap != null)
                            _incomingClientSnapshots.Enqueue(snap);
                    }
                }
            }
        }
        catch
        {
            if (_running)
                StatusMessage = "Host connection closed.";
        }
    }

    private void Update()
    {
        if (_bridge == null || _bridge.Sim == null) return;

        if (Role == SessionRole.Host)
        {
            while (_incomingHostIntents.TryDequeue(out var intent))
            {
                // Partner intents are Player-class only: a remote peer can never run Dev (or Chaos) actions.
                if (intent.Class != IntentClass.Player)
                {
                    Debug.LogWarning("[Coop] dropped non-Player partner intent: " + intent.Kind);
                    continue;
                }
                try { _bridge.Sim.EnqueueIntent(intent); }
                catch (ArgumentException ex) { Debug.LogWarning("[Coop] dropped partner intent: " + ex.Message); }
            }

            _broadcastTimer += Time.unscaledDeltaTime;
            if (_broadcastTimer >= BroadcastIntervalSeconds)
            {
                _broadcastTimer = 0f;
                BroadcastSnapshotToPeers();
            }
        }
        else if (Role == SessionRole.Client)
        {
            PlantSnapshot latest = null;
            while (_incomingClientSnapshots.TryDequeue(out var snap))
            {
                latest = snap;
            }
            if (latest != null)
            {
                _bridge.Sim.RestoreSnapshot(latest);
            }
        }
    }

    private void BroadcastSnapshotToPeers()
    {
        lock (_peersLock)
        {
            if (_hostPeers.Count == 0) return;
        }

        string line = "STATE:" + _bridge.Sim.CaptureSnapshot().ToJson();
        lock (_peersLock)
        {
            for (int i = _hostPeers.Count - 1; i >= 0; i--)
            {
                try
                {
                    _hostPeers[i].Writer.WriteLine(line);
                }
                catch
                {
                    try { _hostPeers[i].Client?.Close(); } catch { }
                    _hostPeers.RemoveAt(i);
                }
            }
        }
    }

    private void OnDestroy()
    {
        StopSession();
    }
}
