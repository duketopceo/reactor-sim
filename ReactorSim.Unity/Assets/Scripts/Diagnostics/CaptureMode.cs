using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using ReactorSim;
using ReactorSim.Controls;

/// <summary>
/// In-player capture run (KTD1): `ReactorSim.exe -capture &lt;dir&gt; [-size 1600x900] [-poses seat,stand,crt,...]` starts the sandbox,
/// walks a fixed list of named poses and UI states, and for each one saves a PNG of the real game window (overlay UI included,
/// via ScreenCapture) plus <c>captures.json</c> with pixel measurements and frame-time percentiles. It needs a graphics device
/// (no -nographics) and an interactive desktop session. Nothing runs unless the -capture argument is present.
/// The legacy Input Manager cannot be injected, so the poses call the interaction, tooltip and menu code directly: a capture
/// proves rendering and layout; the owner's playtest logs (InputLog) prove the real input path.
/// Thresholds that grade a run live in scripts/check-captures.sh, not here, so the build cannot grade itself.
/// </summary>
public class CaptureMode : MonoBehaviour
{
    private string outDir;
    private int width, height;
    private List<string> poses;
    private string sizeDir;
    private int shotIndex;
    private readonly List<string> records = new List<string>();
    private OperatorController op;
    private ControlRoom3DBinder binder;

    // ---- start-up ------------------------------------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        string[] args = Environment.GetCommandLineArgs();
        string dir = ArgValue(args, "-capture");
        if (dir == null) return;
        try
        {
            int w = 1600, h = 900;
            string size = ArgValue(args, "-size");
            if (size != null) CaptureMath.ParseSize(size, out w, out h);
            string poseArg = ArgValue(args, "-poses");
            List<string> list = poseArg != null
                ? CaptureMath.ParsePoses(poseArg)
                : CaptureMath.ParsePoses("menu,seat,stand,crt,tooltip,rejected,drag,checklist,pause,tablet,clipboard,gallery,away,hall,glowoff,glowidle,glowmid,glowfull");
            var go = new GameObject("CaptureMode");
            DontDestroyOnLoad(go);
            var c = go.AddComponent<CaptureMode>();
            c.outDir = dir;
            c.width = w;
            c.height = h;
            c.poses = list;
        }
        catch (FormatException ex)
        {
            Debug.LogError("[Capture] FAIL " + ex.Message);
            Application.Quit(2);
        }
    }

    private static string ArgValue(string[] args, string flag)
    {
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == flag && !args[i + 1].StartsWith("-")) return args[i + 1];
        return null;
    }

    private IEnumerator Start()
    {
        int exitCode = 0;
        sizeDir = Path.Combine(outDir, width + "x" + height);
        string problem = null;
        try { Directory.CreateDirectory(sizeDir); }
        catch (Exception ex) { problem = "cannot create " + sizeDir + ": " + ex.Message; }
        if (problem == null && SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            problem = "no graphics device: run without -nographics in an interactive desktop session";
        if (problem != null)
        {
            Debug.LogError("[Capture] FAIL " + problem);
            Application.Quit(2);
            yield break;
        }

        // The menu scene sets its own window size in Start; set ours after it.
        yield return Frames(5);
        Screen.SetResolution(width, height, FullScreenMode.Windowed);
        yield return Frames(10);
        Debug.Log("[Capture] start " + width + "x" + height + " poses=" + string.Join(",", poses));

        bool inMenu = SceneManager.GetActiveScene().name == GameSession.MenuScene;
        if (poses.Contains("menu"))
        {
            if (inMenu) yield return Shot("menu", "menu", 20, 0.3f, null);
            else Skip("menu", "the run did not start in the menu scene");
        }
        if (inMenu)
        {
            GameSession.Mode = StartMode.Sandbox;
            SceneManager.LoadScene(GameSession.PlantScene);
            yield return Frames(30);
        }
        // The plant scene builds its bridge, room and UI over the first frames; the room input gate and HUDs settle after that.
        yield return new WaitForSecondsRealtime(3f);
        op = FindAnyObjectByType<OperatorController>();
        binder = FindAnyObjectByType<ControlRoom3DBinder>();
        if (op == null || binder == null)
        {
            Debug.LogError("[Capture] FAIL the control room did not load (operator=" + (op != null) + " binder=" + (binder != null) + ")");
            exitCode = 1;
        }
        else
        {
            foreach (string pose in poses)
            {
                if (pose == "menu") continue;
                yield return RunPose(pose);
            }
        }

        WriteJson();
        Debug.Log("[Capture] done " + records.Count + " captures in " + sizeDir);
        Application.Quit(exitCode);
    }

    // The glow overrides are static; never leave them set when the capture run ends early.
    private void OnDestroy()
    {
        PlantEquipmentBinder.PowerOverride = null;
        PlantEquipmentBinder.GlowOff = false;
    }

    // ---- poses ---------------------------------------------------------------------------------------------------

    private IEnumerator RunPose(string pose)
    {
        switch (pose)
        {
            case "seat":
                op.CaptureSeat();
                yield return Shot("seat", "seat", 30, 0.5f, null);
                break;
            case "stand":
                op.TravelTo(1);
                yield return Shot("stand", "stand", 30, 0.5f, null);
                break;
            case "crt":
                var screens = new List<CrtScreenRenderer>(FindObjectsByType<CrtScreenRenderer>(FindObjectsSortMode.None));
                screens.Sort((a, b) => string.CompareOrdinal(a.screenName, b.screenName));
                foreach (var crt in screens)
                {
                    var r = crt.GetComponent<Renderer>();
                    if (r == null) continue;
                    op.CaptureLookAt(r.bounds.center, 28f);
                    yield return Shot("crt", "crt-" + crt.screenName, 30, 0.6f, crt);
                }
                break;
            case "tooltip":
                var dial = FirstInteractable(true);
                if (dial == null) { Skip(pose, "no operable dial in the scene"); break; }
                op.CaptureLookAt(dial.FocusPoint, 45f);
                DiegeticInteractable.ForceHover(dial);
                ControlTooltip.AnchorOverride = new Vector2(Screen.width * 0.5f + 40f, Screen.height * 0.5f);
                yield return Shot("tooltip", "tooltip", 20, 0.3f, null);
                ControlTooltip.AnchorOverride = null;
                DiegeticInteractable.ForceHover(null);
                break;
            case "rejected":
                var gauge = FirstInteractable(false);
                if (gauge == null) { Skip(pose, "no indicator in the scene"); break; }
                op.CaptureLookAt(gauge.FocusPoint, 45f);
                yield return Frames(20);
                gauge.Reject(ControlTooltipModel.RejectIndicator);   // draws the note for about 1.6 s
                yield return Shot("rejected", "rejected-press", 3, 0f, null);
                break;
            case "drag":
                var knob = FirstInteractable(true);
                if (knob == null) { Skip(pose, "no operable dial in the scene"); break; }
                op.CaptureLookAt(knob.FocusPoint, 45f);
                yield return Frames(20);
                var row = RuntimeInventoryStore.Inventory.Find(knob.rowId);
                binder.ShowReadout(ControlTooltipModel.DragReadout(string.IsNullOrEmpty(knob.rowLabel) ? knob.RowName : knob.rowLabel,
                    0.62, 0.5, row != null ? row.units : "", false), 3f);
                yield return Shot("drag", "drag-readout", 3, 0f, null);
                break;
            case "checklist":
                yield return Panel("checklist", FindAnyObjectByType<StartupChecklistHud>(), "Expand", "Collapse");
                break;
            case "pause":
                yield return Panel("pause", FindAnyObjectByType<PauseMenu>(), "Open", "Close");
                break;
            case "tablet":
                yield return Panel("tablet", FindAnyObjectByType<OperatorTablet>(), "Raise", "Lower");
                break;
            case "clipboard":
                yield return Panel("clipboard", FindAnyObjectByType<ProcedureClipboard>(), "Open", "Close");
                break;
            case "gallery":
                // The window seen from the nearest quick-travel stand, wide: the same camera in a run with and without the new
                // hall, so the frame-time and draw-call difference is the hall.
                var glass = WindowGlass();
                if (glass == null) { Skip(pose, "no window in the scene"); break; }
                op.CaptureLookAt(glass.bounds.center, 60f);
                yield return Shot("gallery", "gallery", 30, 0.6f, null);
                break;
            case "glowoff":
                PlantEquipmentBinder.GlowOff = true;      // reference frame: no rack emission, haze or light at all
                yield return GlowShot(pose, 0.0);
                PlantEquipmentBinder.GlowOff = false;
                break;
            case "glowidle":
                yield return GlowShot(pose, 0.0);
                break;
            case "glowmid":
                yield return GlowShot(pose, 0.3);
                break;
            case "glowfull":
                yield return GlowShot(pose, 1.0);
                break;
            case "away":
                // Same stand as the gallery pose but looking the other way, so the window is out of the frustum: the frame time
                // here against the same pose on a baseline run is what the hall costs while it cannot be seen.
                var glassAway = WindowGlass();
                if (glassAway == null) { Skip(pose, "no window in the scene"); break; }
                Vector3 window = glassAway.bounds.center;
                op.CaptureLookAt(window, 60f);
                yield return Frames(30);
                Vector3 eye = Camera.main != null ? Camera.main.transform.position : window;
                Vector3 away = new Vector3(eye.x - window.x, 0f, eye.z - window.z).normalized;
                // turned about and up at the ceiling: the window is behind the camera and the view holds almost nothing, the same
                // in every run, so the difference between two runs is the cost of the hall while it cannot be seen
                op.CaptureAt(new Vector3(eye.x, 0f, eye.z), eye + away * 1.0f + Vector3.up * 4f, 60f);
                yield return Frames(30);
                yield return Shot("away", "gallery-away", 30, 0.6f, null);
                break;
            case "hall":
                // Standing at the window looking down at the module, for the owner to judge (not graded).
                var glassNear = WindowGlass();
                var hallNear = HallVisibility.FindHall();
                Transform marker = hallNear != null ? hallNear.transform.Find("GalleryFocus") : null;
                if (glassNear == null || marker == null) { Skip(pose, "no window or no hall gallery focus in the scene"); break; }
                Vector3 centre = glassNear.bounds.center;
                Vector3 toRoom = centre - marker.position;
                toRoom.y = 0f;
                op.CaptureAt(centre + toRoom.normalized * 0.9f, marker.position, 55f);
                yield return Shot("hall", "hall-close", 30, 0.6f, null);
                break;
        }
    }

    // The spent-fuel racks seen from the window with the plant power forced to one fraction (capture only; the rack glow follows
    // plant power as a labelled cue). The JSON carries cropMean, the mean brightness over the racks, so the
    // grader can check the glow really changes in the rendered image.
    private IEnumerator GlowShot(string pose, double power)
    {
        var glass = WindowGlass();
        var hallRoot = HallVisibility.FindHall();
        Transform focus = hallRoot != null ? hallRoot.transform.Find("RackFocus") : null;
        if (glass == null || focus == null) { Skip(pose, "no window or no rack focus in the scene"); yield break; }
        var cells = new List<Renderer>();
        foreach (var r in hallRoot.GetComponentsInChildren<Renderer>(true)) if (r.name.StartsWith("RackGlow_Cells_")) cells.Add(r);
        Vector3 centre = glass.bounds.center;
        Vector3 toRoom = centre - focus.position;
        toRoom.y = 0f;
        op.CaptureAt(centre + toRoom.normalized * 0.9f, focus.position, 45f);
        PlantEquipmentBinder.PowerOverride = power;
        yield return Shot(pose, pose, 40, 0.8f, null, cells);
        PlantEquipmentBinder.PowerOverride = null;
    }

    private static Renderer WindowGlass()
    {
        GameObject g = GameObject.Find("Window_Glass");
        return g != null ? g.GetComponent<Renderer>() : null;
    }

    // A panel is opened through its own method (they are private on some), captured, then closed again.
    private IEnumerator Panel(string pose, UnityEngine.Object target, string open, string close)
    {
        if (!Call(target, open)) { Skip(pose, "could not open it (" + (target == null ? "not in the scene" : "no " + open + " method") + ")"); yield break; }
        yield return Shot(pose, pose, 20, 0.4f, null);
        Call(target, close);
        yield return Frames(5);
    }

    private static bool Call(UnityEngine.Object target, string method)
    {
        if (target == null) return false;
        MethodInfo m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        if (m == null) return false;
        m.Invoke(target, null);
        return true;
    }

    private static DiegeticInteractable FirstInteractable(bool operableDial)
    {
        DiegeticInteractable best = null;
        foreach (var d in FindObjectsByType<DiegeticInteractable>(FindObjectsSortMode.None))
        {
            bool match = operableDial ? d.Operable && d.Spec.Style == ControlStyle.Dial : d.pinnable;
            if (match && (best == null || string.CompareOrdinal(d.RowName, best.RowName) < 0)) best = d;
        }
        return best;
    }

    private void Skip(string pose, string why)
    {
        Debug.LogWarning("[Capture] skipped " + pose + ": " + why);
        records.Add("{\"pose\":\"" + Esc(pose) + "\",\"skipped\":\"" + Esc(why) + "\"}");
    }

    // ---- capture -------------------------------------------------------------------------------------------------

    private static IEnumerator Frames(int n)
    {
        for (int i = 0; i < n; i++) yield return null;
    }

    private IEnumerator Shot(string pose, string label, int settleFrames, float settleSeconds, CrtScreenRenderer crt, List<Renderer> measure = null)
    {
        yield return Frames(settleFrames);
        if (settleSeconds > 0f) yield return new WaitForSecondsRealtime(settleSeconds);
        FrameMetrics.Reset();
        yield return Frames(400);   // frames the percentiles are taken over (a short window is dominated by one slow frame)
        yield return new WaitForEndOfFrame();

        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        string file = (++shotIndex).ToString("00") + "-" + label + ".png";
        try { File.WriteAllBytes(Path.Combine(sizeDir, file), tex.EncodeToPNG()); }
        catch (Exception ex) { Debug.LogError("[Capture] FAIL writing " + file + ": " + ex.Message); }

        var sb = new StringBuilder(256);
        sb.Append("{\"pose\":\"").Append(Esc(pose)).Append("\",\"label\":\"").Append(Esc(label)).Append("\",\"file\":\"").Append(Esc(file)).Append('"');
        sb.Append(",\"width\":").Append(tex.width).Append(",\"height\":").Append(tex.height);
        sb.Append(",\"frameMsP50\":").Append(InputLogFormat.Num(FrameMetrics.Percentile(50)));
        sb.Append(",\"frameMsP95\":").Append(InputLogFormat.Num(FrameMetrics.Percentile(95)));
        sb.Append(",\"frameMsP99\":").Append(InputLogFormat.Num(FrameMetrics.Percentile(99)));
        sb.Append(",\"drawCalls\":").Append(FrameMetrics.DrawCalls);
        if (crt != null) AppendScreenMeasure(sb, crt, tex);
        if (measure != null && measure.Count > 0) AppendCropLuma(sb, measure, tex);
        sb.Append('}');
        records.Add(sb.ToString());
        Destroy(tex);
        Debug.Log("[Capture] " + file);
    }

    // Mean luminance inside the screen rectangle that covers the given renderers.
    private static void AppendCropLuma(StringBuilder sb, List<Renderer> renderers, Texture2D tex)
    {
        var cam = Camera.main;
        if (cam == null) return;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var r in renderers)
        {
            Bounds b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = cam.WorldToScreenPoint(corner);
                if (p.z <= 0f) return;
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
        }
        int x0 = Mathf.Clamp(Mathf.RoundToInt(minX), 0, tex.width - 1), y0 = Mathf.Clamp(Mathf.RoundToInt(minY), 0, tex.height - 1);
        int w = Mathf.Clamp(Mathf.RoundToInt(maxX) - x0, 1, tex.width - x0), h = Mathf.Clamp(Mathf.RoundToInt(maxY) - y0, 1, tex.height - y0);
        Color32[] px = tex.GetPixels32();
        var crop = new byte[w * h * 4];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                Color32 c = px[(y0 + j) * tex.width + x0 + i];
                int o = (j * w + i) * 4;
                crop[o] = c.r; crop[o + 1] = c.g; crop[o + 2] = c.b; crop[o + 3] = 255;
            }
        sb.Append(",\"cropMean\":").Append(InputLogFormat.Num(CaptureMath.CropMeanLuminance(crop, w, 0, 0, w, h)));
        sb.Append(",\"cropW\":").Append(w).Append(",\"cropH\":").Append(h);
    }

    // Measurements of a CRT as it appears in the captured image: its on-screen width, the characters per line of its text, the
    // projected glyph height and the contrast of the rendered pixels inside its rectangle.
    private void AppendScreenMeasure(StringBuilder sb, CrtScreenRenderer crt, Texture2D tex)
    {
        var cam = Camera.main;
        var r = crt.GetComponent<Renderer>();
        if (cam == null || r == null) return;
        Bounds b = r.bounds;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 p = cam.WorldToScreenPoint(corner);
            if (p.z <= 0f) return;   // behind the camera: no measurement
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
        }
        int x0 = Mathf.Clamp(Mathf.RoundToInt(minX), 0, tex.width - 1), y0 = Mathf.Clamp(Mathf.RoundToInt(minY), 0, tex.height - 1);
        int w = Mathf.Clamp(Mathf.RoundToInt(maxX) - x0, 1, tex.width - x0), h = Mathf.Clamp(Mathf.RoundToInt(maxY) - y0, 1, tex.height - y0);

        int columns = 1;
        string text = crt.CurrentText ?? "";
        foreach (string line in text.Split('\n')) columns = Mathf.Max(columns, line.TrimEnd('\r').Length);

        Color32[] px = tex.GetPixels32();
        var crop = new byte[w * h * 4];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                Color32 c = px[(y0 + j) * tex.width + x0 + i];
                int o = (j * w + i) * 4;
                crop[o] = c.r; crop[o + 1] = c.g; crop[o + 2] = c.b; crop[o + 3] = c.a;
            }
        double cell = (double)w / columns;
        sb.Append(",\"screenWidthPx\":").Append(w).Append(",\"screenHeightPx\":").Append(h);
        sb.Append(",\"columns\":").Append(columns);
        sb.Append(",\"cellWidthPx\":").Append(InputLogFormat.Num(cell));
        sb.Append(",\"glyphHeightPx\":").Append(InputLogFormat.Num(cell * CaptureMath.GlyphToCellRatio));
        sb.Append(",\"contrast\":").Append(InputLogFormat.Num(CaptureMath.CropContrast(crop, w, 0, 0, w, h)));
    }

    private void WriteJson()
    {
        var sb = new StringBuilder(2048);
        sb.Append("{\"size\":\"").Append(width).Append('x').Append(height).Append('"');
        sb.Append(",\"build\":\"").Append(Esc(Application.version)).Append('"');
        sb.Append(",\"developmentBuild\":").Append(Debug.isDebugBuild ? "true" : "false");
        sb.Append(",\"captures\":[\n");
        for (int i = 0; i < records.Count; i++) sb.Append(i > 0 ? ",\n" : "").Append(records[i]);
        sb.Append("\n]}\n");
        try { File.WriteAllText(Path.Combine(sizeDir, "captures.json"), sb.ToString(), new UTF8Encoding(false)); }
        catch (Exception ex) { Debug.LogError("[Capture] FAIL writing captures.json: " + ex.Message); }
    }

    private static string Esc(string s)
    {
        return string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }
}
