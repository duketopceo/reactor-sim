using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using ReactorSim;

/// <summary>
/// Per-frame cost record shared by the probe and the capture run: frame time in ms (FrameTimingManager when it reports, else
/// Time.unscaledDeltaTime) and the draw calls of the last frame (ProfilerRecorder on the render counters, which only report in a
/// development build). Percentiles are nearest-rank over the frames since the last Reset (the capture run resets it per pose).
/// </summary>
public static class FrameMetrics
{
    private static readonly List<double> frameMs = new List<double>(2048);
    private const int MaxFrames = 20000;

    public static long DrawCalls { get; internal set; } = -1;   // -1 = the counter is not available in this build
    public static int Count { get { return frameMs.Count; } }

    public static void Record(double ms)
    {
        if (frameMs.Count >= MaxFrames) frameMs.RemoveRange(0, MaxFrames / 2);
        frameMs.Add(ms);
    }

    public static void Reset() { frameMs.Clear(); }

    public static double Percentile(double p) { return CaptureMath.Percentile(frameMs, p); }
}

/// <summary>
/// Playtest diagnostics: every 5 s, one Player.log line comparing what the legacy Input API sees with what
/// the window's own event stream (IMGUI) sees. If IMGUI counts clicks and keys but legacy Input stays at zero,
/// the input backend is the problem, not the controls. The line also carries the frame-time percentiles and draw
/// calls of the window (FrameMetrics).
/// </summary>
public class InputProbe : MonoBehaviour
{
    private float next;
    private int legacyKeys, legacyClicks, guiKeys, guiClicks;
    private Vector3 lastMouse;
    private float mouseTravel;
    private int frames;
    private float worstFrame;
    private readonly FrameTiming[] timing = new FrameTiming[1];
    private ProfilerRecorder drawCalls;
    private bool hasDrawCalls;
    private readonly List<double> window = new List<double>(1024);   // this 5 s log window

    private void OnEnable()
    {
        try
        {
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            hasDrawCalls = true;
        }
        catch (System.Exception) { hasDrawCalls = false; }
    }

    private void OnDisable()
    {
        if (hasDrawCalls) drawCalls.Dispose();
        hasDrawCalls = false;
    }

    private double FrameMs()
    {
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].cpuFrameTime > 0.0)
            return System.Math.Max(timing[0].cpuFrameTime, timing[0].gpuFrameTime);
        return Time.unscaledDeltaTime * 1000.0;
    }

    private void Update()
    {
        frames++;
        worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
        double ms = FrameMs();
        FrameMetrics.Record(ms);
        window.Add(ms);
        FrameMetrics.DrawCalls = hasDrawCalls && drawCalls.Valid ? drawCalls.LastValue : -1;
        if (Input.anyKeyDown) legacyKeys++;
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) legacyClicks++;
        Vector3 m = Input.mousePosition;
        mouseTravel += (m - lastMouse).magnitude;
        lastMouse = m;

        if (Time.unscaledTime >= next)
        {
            next = Time.unscaledTime + 5f;
            Debug.Log("[InputProbe] fps=" + (frames / 5f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " worst=" + (worstFrame * 1000f).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "ms"
                + " frame p50/p95/p99=" + CaptureMath.Percentile(window, 50).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "/" + CaptureMath.Percentile(window, 95).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                + "/" + CaptureMath.Percentile(window, 99).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "ms draws=" + FrameMetrics.DrawCalls
                + " focused=" + Application.isFocused
                + " legacy keys=" + legacyKeys + " clicks=" + legacyClicks + " mouse=" + m.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " travel=" + mouseTravel.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                + " | window-events keys=" + guiKeys + " clicks=" + guiClicks
                + " | cursor=" + Cursor.lockState + " screen=" + Screen.width + "x" + Screen.height + " mode=" + Screen.fullScreenMode);
            frames = 0;
            worstFrame = 0f;
            window.Clear();
        }
    }

    private void OnGUI()
    {
        var e = Event.current;
        if (e == null) return;
        if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None) guiKeys++;
        if (e.type == EventType.MouseDown) guiClicks++;
    }
}
