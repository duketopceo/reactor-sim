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
                : CaptureMath.ParsePoses("menu,seat,stand,crt,tooltip,rejected,drag,checklist,pause,tablet,clipboard,gallery,away,hall");
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
                var hall = HallVisibility.FindHall();
                if (hall == null) { Skip(pose, "no ReactorHall object in the scene"); break; }
                op.CaptureLookAt(HallFocus(hall), 60f);
                yield return Shot("gallery", "gallery", 30, 0.6f, null);
                break;
            case "hall":
                // Close view of the module for the owner to judge (not graded): narrow field of view, same stand as the gallery pose.
                var hallNear = HallVisibility.FindHall();
                if (hallNear == null) { Skip(pose, "no ReactorHall object in the scene"); break; }
                op.CaptureLookAt(HallFocus(hallNear), 30f);
                yield return Shot("hall", "hall-close", 30, 0.6f, null);
                break;
            case "away":
                // Same stand as the gallery pose but looking the other way, so the window is out of the frustum: the frame time
                // here against the same pose on a baseline run is what the hall costs while it cannot be seen.
                var hallAway = HallVisibility.FindHall();
                if (hallAway == null) { Skip(pose, "no ReactorHall object in the scene"); break; }
                Vector3 focus = HallFocus(hallAway);
                op.CaptureLookAt(focus, 60f);
                yield return Frames(5);
                Vector3 eye = Camera.main != null ? Camera.main.transform.position : focus;
                op.CaptureLookAt(eye + new Vector3(eye.x - focus.x, 0f, eye.z - focus.z), 60f);
                yield return Shot("away", "gallery-away", 30, 0.6f, null);
                break;
        }
    }

    // The point the gallery pose looks at: the hall's GalleryFocus marker when it has one, else the middle of its renderers.
    private static Vector3 HallFocus(GameObject hall)
    {
        Transform marker = hall.transform.Find("GalleryFocus");
        if (marker != null) return marker.position;
        var rs = hall.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return hall.transform.position;
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b.center;
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

    private IEnumerator Shot(string pose, string label, int settleFrames, float settleSeconds, CrtScreenRenderer crt)
    {
        yield return Frames(settleFrames);
        if (settleSeconds > 0f) yield return new WaitForSecondsRealtime(settleSeconds);
        FrameMetrics.Reset();
        yield return Frames(30);   // frames the percentiles are taken over
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
        sb.Append('}');
        records.Add(sb.ToString());
        Destroy(tex);
        Debug.Log("[Capture] " + file);
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
