using System;
using System.IO;
using UnityEngine;
using ReactorSim;

/// <summary>
/// U11: per-session playtest log. Wires a <see cref="SessionRecorder"/> to the running Simulation so every session
/// leaves <c>Application.persistentDataPath/sessions/&lt;yyyyMMdd-HHmmss&gt;/</c> with log.jsonl, telemetry.csv,
/// session.json and intent-log.jsonl. Checkpoints every 30 real seconds (so a crash still leaves a usable
/// record) and finishes on quit or when the scene unloads (reset, quit to menu).
/// </summary>
public class SessionLog : MonoBehaviour
{
    private const float CheckpointSeconds = 30f;

    private SessionRecorder recorder;
    private PlantBridge bridge;
    private float timer;

    public string Directory { get { return recorder != null ? recorder.Directory : null; } }

    public void Begin(PlantBridge owner, string mode)
    {
        bridge = owner;
        try
        {
            string root = Path.Combine(Application.persistentDataPath, "sessions");
            recorder = new SessionRecorder(root, DateTime.Now, owner.Sim, Application.version, mode);
            Debug.Log("[SessionLog] writing " + recorder.Directory);
        }
        catch (Exception ex)
        {
            Debug.LogError("[SessionLog] could not start session log: " + ex.Message);
            recorder = null;
        }
    }

    private void Update()
    {
        if (recorder == null) return;
        timer += Time.unscaledDeltaTime;
        if (timer < CheckpointSeconds) return;
        timer = 0f;
        try { lock (bridge.SimLock) { recorder.Checkpoint(); } }
        catch (Exception ex) { Debug.LogWarning("[SessionLog] checkpoint failed: " + ex.Message); }
    }

    private void OnApplicationQuit() { Finish(); }
    private void OnDestroy() { Finish(); }

    private void Finish()
    {
        if (recorder == null) return;
        try { lock (bridge.SimLock) { recorder.Finish(); } }
        catch (Exception ex) { Debug.LogWarning("[SessionLog] finish failed: " + ex.Message); }
        recorder = null;
    }
}
