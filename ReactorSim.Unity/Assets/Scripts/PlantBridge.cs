using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using ReactorSim;

/// <summary>
/// Owns the ReactorSim simulation inside a Unity scene, starts the in-process HTTP API (U3),
/// manages the Co-op session (U7), and attaches the Control Desk UI (U6).
/// </summary>
public class PlantBridge : MonoBehaviour
{
    // The sim clock lives in Core (KTD1): scale, pause, auto-slow and skip-ahead are Simulation.Clock state,
    // changed through clock intents. These wrappers keep the legacy desk UI working.
    private float requestedWarp = -1f;

    /// <summary>Sim-seconds per real second as requested by the player (profile default until changed).</summary>
    public float timeWarp
    {
        get { return Sim != null ? (float)Sim.Clock.Scale : 1f; }
        set
        {
            if (Sim == null || Mathf.Abs(value - requestedWarp) < 1e-3f) return;
            requestedWarp = value;
            SubmitIntent(PlantIntent.SetTimeScale(value, "player"));
        }
    }

    private bool? requestedPause;

    public bool Paused
    {
        get { return Sim != null && Sim.Clock.Paused; }
        set
        {
            // Compare against the last request, not the clock: the intent applies on the next tick, so a fast
            // Esc-Esc would otherwise see the clock still unpaused and drop the resume.
            if (Sim == null || (requestedPause.HasValue ? requestedPause.Value : Sim.Clock.Paused) == value) return;
            requestedPause = value;
            SubmitIntent(value ? PlantIntent.PauseClock("player") : PlantIntent.ResumeClock("player"));
        }
    }

    public SimClock Clock { get { return Sim != null ? Sim.Clock : null; } }

    /// <summary>Fraction (0..1) of the way to the next sim tick, for interpolating instruments between ticks.</summary>
    public float TickFraction { get { return Sim != null ? (float)Sim.Clock.InterpolationFraction : 0f; } }

    private PlantSnapshot cachedSnapshot;
    private int cachedSnapshotFrame = -1;

    /// <summary>
    /// The plant snapshot for this frame, captured once under SimLock and shared by every 3D-room consumer
    /// (gauges, CRTs, audio, equipment, alarm feedback) so they do not each capture per frame.
    /// </summary>
    public PlantSnapshot GetSnapshot()
    {
        if (Sim == null) return null;
        if (cachedSnapshot == null || cachedSnapshotFrame != Time.frameCount)
        {
            lock (SimLock) { cachedSnapshot = Sim.CaptureSnapshot(); }
            cachedSnapshotFrame = Time.frameCount;
        }
        return cachedSnapshot;
    }

    public bool logTelemetry = true;
    public float telemetryIntervalSeconds = 30f;

    public int httpApiPort = 7770;

    public Simulation Sim { get; private set; }
    public HttpApiServer HttpServer { get; private set; }
    public CoopSessionManager Coop { get; private set; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public ControlDeskUI DeskUI { get; private set; }
#endif

    [Tooltip("Cutover 2D->3D: when false (default), the legacy IMGUI ControlDeskUI overlay stays off; the 3D control room is the UI. Enable for the debug overlay.")]
    public bool show2DControlDesk = false;

    public readonly object SimLock = new object();

    private float telemetryTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrapOnPlay()
    {
        // The menu scene has no plant; the control room scene brings its own PlantSim.
        if (SceneManager.GetActiveScene().name == GameSession.MenuScene) return;
        if (FindAnyObjectByType<PlantBridge>() == null)
        {
            var go = new GameObject("PlantSim");
            DontDestroyOnLoad(go);
            go.AddComponent<PlantBridge>();
        }
    }

    private void Awake()
    {
        Application.runInBackground = true;
        EnsureCameraExists();

        // Gameplay tuning profile (StreamingAssets/tuning.json); a bad profile logs its named error and
        // the in-code defaults (identical to the shipped file) are used.
        Tuning tuning;
        try { tuning = Tuning.LoadFromFile(System.IO.Path.Combine(Application.streamingAssetsPath, "tuning.json")); }
        catch (TuningException ex)
        {
            Debug.LogError("[PlantBridge] tuning.json rejected, using defaults: " + ex.Message);
            tuning = new Tuning();
        }

        Sim = new Simulation(tuning);
        Sim.ApplyInteractiveTick();

        // Plant-manager messages (StreamingAssets/messages.json); same policy as tuning: a bad file logs its named
        // error and the plant runs with an empty message engine. Loaded into Plant.Messages, which preset loads and
        // sandbox resets keep (they only call ResetDispatch, which clears state, not the definitions).
        try { Sim.Plant.Messages = MessageEngine.LoadFromFile(System.IO.Path.Combine(Application.streamingAssetsPath, "messages.json")); }
        catch (MessageException ex)
        {
            Debug.LogError("[PlantBridge] messages.json rejected, no plant messages: " + ex.Message);
            Sim.Plant.Messages = new MessageEngine();
        }
        // Sandbox and Missions both start cold with pumps off and hints on (see SandboxStart); the menu's
        // GameSession.Mode only changes what the player does next (M1 is the selected mission either way).
        SandboxStart.Apply(Sim);

        // Dev playtest hook: `ReactorSim.exe -dev` flips DevMode and starts the token-gated loopback API (port 7770)
        // so presets like /api/scenario/full-power can jump the plant to a state for inspection. Off by default.
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-dev") >= 0)
        {
            Sim.DevMode = true;
            StartDevApi();
        }

        // The HTTP/MCP API is dev-only (KTD9/R14): no listener starts here. Dev mode calls StartDevApi().

        var sessionLog = GetComponent<SessionLog>();
        if (sessionLog == null) sessionLog = gameObject.AddComponent<SessionLog>();
        sessionLog.Begin(this, GameSession.ModeName);
        if (GetComponent<PauseMenu>() == null) gameObject.AddComponent<PauseMenu>();
        if (GetComponent<InputProbe>() == null) gameObject.AddComponent<InputProbe>();

        Coop = GetComponent<CoopSessionManager>();
        if (Coop == null)
            Coop = gameObject.AddComponent<CoopSessionManager>();
        Coop.Initialize(this);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (show2DControlDesk)
        {
            DeskUI = GetComponent<ControlDeskUI>();
            if (DeskUI == null)
                DeskUI = gameObject.AddComponent<ControlDeskUI>();
            DeskUI.Initialize(this);
        }
#endif
    }

    /// <summary>
    /// Starts the token-gated loopback HTTP/MCP API (port 7770, or 7771 if taken). Called by dev mode only;
    /// the per-session token is written to HttpServer.TokenFilePath for the MCP bridge.
    /// </summary>
    public bool StartDevApi()
    {
        if (HttpServer != null && HttpServer.IsRunning) return true;
        HttpServer = new HttpApiServer(Sim, httpApiPort, SimLock);
        if (!HttpServer.Start())
        {
            HttpServer.Dispose();
            HttpServer = new HttpApiServer(Sim, httpApiPort + 1, SimLock);
            HttpServer.Start();
        }
        if (HttpServer.IsRunning)
            Debug.Log($"[PlantBridge] Dev HTTP API active at http://127.0.0.1:{HttpServer.Port}/ (token file: {HttpServer.TokenFilePath})");
        return HttpServer.IsRunning;
    }

    private static void EnsureCameraExists()
    {
        if (Camera.main == null && FindAnyObjectByType<Camera>() == null)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = ThemeColors.CameraBackground;
        }
    }

    /// <summary>
    /// Unified PlantIntent submission from local UI controls.
    /// Routes to Host over TCP when connected as a co-op Partner Client, or enqueues locally on Host/Standalone.
    /// </summary>
    public void SubmitIntent(PlantIntent intent)
    {
        if (intent == null || Sim == null) return;
        if (Coop != null && Coop.Role == CoopSessionManager.SessionRole.Client)
        {
            Coop.SendIntentToHost(intent);
        }
        else
        {
            // One validated, logged path: EnqueueIntent applies the non-finite guard and the Dev-intent
            // gate; Sim.Tick applies the intent and records it in the intent log.
            try { Sim.EnqueueIntent(intent); }
            catch (IntentRejectedException ex) { Debug.LogWarning("[PlantBridge] intent rejected: " + ex.Message); }
            catch (ArgumentException ex) { Debug.LogWarning("[PlantBridge] intent dropped: " + ex.Message); }
        }
    }

    private void Update()
    {
        if (Sim == null) return;

        // Only Standalone or Host steps the authoritative physics simulation; Client receives snapshots from Host.
        if (Coop == null || Coop.Role != CoopSessionManager.SessionRole.Client)
        {
            // Core converts real seconds to whole ticks (carrying the remainder); paused/frozen runs none.
            lock (SimLock)
            {
                Sim.Advance(Time.deltaTime);
            }
        }

        telemetryTimer += Time.deltaTime;
        if (logTelemetry && telemetryTimer >= telemetryIntervalSeconds)
        {
            telemetryTimer = 0f;
            Debug.Log(Sim.Plant.Telemetry(Sim.GameMinutes));
        }
    }

    private void OnDestroy()
    {
        HttpServer?.Dispose();
    }
}
