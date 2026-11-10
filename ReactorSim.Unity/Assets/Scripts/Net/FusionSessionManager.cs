using System;
using UnityEngine;
using ReactorSim;

// U7 Photon Fusion (Shared mode) session — inert until the Fusion package is installed.
// When installed (com.exitgames.fusion), restore the full version from
// FusionSessionManager.cs.full (host/client lifecycle, NetworkRunner in Shared mode,
// 4 Hz snapshot RPC, partner intents -> PlantBridge.SubmitIntent) and delete this stub.
// CoopSessionManager (TCP) remains the shipping LAN path; Fusion is the internet path.
public class FusionSessionManager : MonoBehaviour
{
    public enum FusionRole { Standalone, Host, Client }

    public FusionRole Role { get; private set; } = FusionRole.Standalone;
    public string StatusMessage { get; private set; } = "Fusion: Standalone (package not installed)";
    public int hostPort = 7777;

    private PlantBridge _bridge;
    private float _broadcastTimer;

    public void Initialize(PlantBridge bridge)
    {
        _bridge = bridge;
    }

    public void StartHost(string sessionCode = "REACTOR")
    {
        Role = FusionRole.Host;
        StatusMessage = $"Fusion hosting room {sessionCode} (stub - install Fusion package for live session)";
        Debug.LogWarning("[Fusion] " + StatusMessage);
    }

    public void StartClient(string sessionCode = "REACTOR", string hostAddress = "127.0.0.1")
    {
        Role = FusionRole.Client;
        StatusMessage = $"Fusion joining room {sessionCode} (stub - install Fusion package for live session)";
        Debug.LogWarning("[Fusion] " + StatusMessage);
    }

    public void StopSession()
    {
        Role = FusionRole.Standalone;
        StatusMessage = "Fusion: Standalone (package not installed)";
    }

    public void ApplyPartnerIntent(PlantIntent intent)
    {
        if (intent == null) return;
        if (intent.Class != IntentClass.Player) return; // partner intents are Player-class only
        intent.Issuer = "partner-fusion";
        if (_bridge != null)
            _bridge.SubmitIntent(intent);
    }

    public void PushSnapshotToPeers()
    {
        if (Role != FusionRole.Host) return;
        Debug.LogWarning("[Fusion] Peer snapshot broadcast requires Fusion package (com.exitgames.fusion).");
    }

    private void Update()
    {
        if (_bridge == null || _bridge.Sim == null) return;
        if (Role != FusionRole.Host) return;
        _broadcastTimer += Time.unscaledDeltaTime;
        if (_broadcastTimer >= 0.25f)
        {
            _broadcastTimer = 0f;
            PushSnapshotToPeers();
        }
    }

    private void OnDestroy()
    {
        StopSession();
    }
}
