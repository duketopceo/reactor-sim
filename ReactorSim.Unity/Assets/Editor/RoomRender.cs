#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Renders the control room from named viewpoints IN PLAY MODE (so PlantBridge, CRT pages and the operator have
// initialised) and writes <repo>/out/render/unity-<name>.png (1600x900, or ROOM_RENDER_SIZE=WxH / -roomRenderSize WxH). KTD7: this is the render the gates trust.
//   "<Unity.exe>" -batchmode -projectPath ReactorSim.Unity -executeMethod RoomRender.Run -logFile out/unity-RoomRender.log
// Needs a graphics device: do NOT pass -nographics. Do NOT pass -quit either: the work finishes asynchronously
// (play mode), and the method exits the editor itself with 0 (all PNGs good) or 1 (any failure).
// Anchors: CAM_Seat_A1, CAM_Stand_Center, CAM_Top. When NONE exist (today's room) fixed viewpoints computed from
// the ControlRoom renderers' bounds are used; when some exist and one is missing the run fails naming it.
// Log lines: "[RoomRender] OK <path> <bytes>" / "[RoomRender] FAIL ...".
[InitializeOnLoad]
public static class RoomRender
{
    private const string ScenePath = "Assets/Scenes/Bootstrap.unity";
    // Size defaults to 1600x900; ROOM_RENDER_SIZE=1920x1080 (or the flag -roomRenderSize 1920x1080) renders at another size, for review captures.
    private static readonly int Width = EnvSize(0, 1600), Height = EnvSize(1, 900);
    private const double WarmupSeconds = 2.0;
    private const double TimeoutSeconds = 180.0;

    // Entering/leaving play mode reloads the domain and drops the update subscription; SessionState survives it,
    // so the state machine is re-armed from the static constructor (same trick as TmpBootstrap).
    private const string StageKey = "ReactorSim_RoomRender_Stage";   // 0 idle, 1 wait for play + warm-up, 2 wait for exit from play
    private const string StartKey = "ReactorSim_RoomRender_Start";
    private const string CodeKey = "ReactorSim_RoomRender_Code";

    private static readonly string[] Anchors = { "CAM_Seat_A1", "CAM_Stand_Center", "CAM_Top" };
    private static readonly string[] Names = { "seat", "stand", "top" };

    private static bool playSeen;
    private static int startFrame;
    private static double startRealtime;

    private static int EnvSize(int part, int fallback)
    {
        string text = Environment.GetEnvironmentVariable("ROOM_RENDER_SIZE") ?? "";
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-roomRenderSize") text = args[i + 1];   // same thing as a player-style flag
        string[] wh = text.Split('x');
        return wh.Length == 2 && int.TryParse(wh[part], out int n) && n >= 320 && n <= 4096 ? n : fallback;
    }

    static RoomRender()
    {
        if (SessionState.GetInt(StageKey, 0) == 0) return;
        Arm();
    }

    private static void Arm()
    {
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    public static void Run()
    {
        try
        {
            if (!File.Exists(ScenePath))
            {
                Fail("scene not found: " + ScenePath);
                return;
            }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetInt(CodeKey, 0);
            SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
            SessionState.SetInt(StageKey, 1);
            playSeen = false;
            Arm();
            PlayModeGuard.Prepare();   // no script compile / domain reload after Awake (it would null runtime state)
            EditorApplication.isPlaying = true;
        }
        catch (Exception e)
        {
            Fail("could not enter play mode: " + e);
        }
    }

    private static void Poll()
    {
        int stage = SessionState.GetInt(StageKey, 0);
        if (stage == 0) { EditorApplication.update -= Poll; return; }

        double start = SessionState.GetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
        if (EditorApplication.timeSinceStartup - start > TimeoutSeconds)
        {
            Debug.LogError("[RoomRender] FAIL timed out (stage " + stage + ")");
            SessionState.SetInt(CodeKey, 1);
            if (stage == 1 && EditorApplication.isPlaying)
            {
                SessionState.SetInt(StageKey, 2);
                SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
                EditorApplication.isPlaying = false;
            }
            else Finish(1);
            return;
        }

        if (stage == 1)
        {
            // Only "playing" gates the run: PlayModeGuard defers recompiles until play ends, so a pending compile can
            // no longer reload the domain mid-check, but it keeps isCompiling true for the whole session.
            if (!EditorApplication.isPlaying) return;
            if (!playSeen)
            {
                playSeen = true;
                startFrame = Time.frameCount;
                startRealtime = Time.realtimeSinceStartupAsDouble;
                return;
            }
            // Wall-clock warmup only: in batchmode play mode the player loop may tick slowly or not at all,
            // so the frame count is logged as a diagnostic rather than waited on.
            if (Time.realtimeSinceStartupAsDouble - startRealtime < WarmupSeconds)
                return;
            var bridge = UnityEngine.Object.FindAnyObjectByType<PlantBridge>();
            Debug.Log("[RoomRender] play mode frames=" + (Time.frameCount - startFrame)
                + " sim=" + (bridge != null && bridge.Sim != null ? "ticks " + bridge.Sim.SessionTick : "none"));

            int code;
            try { code = RenderAll() ? 0 : 1; }
            catch (Exception e) { Debug.LogError("[RoomRender] FAIL exception: " + e); code = 1; }
            SessionState.SetInt(CodeKey, code);
            SessionState.SetInt(StageKey, 2);
            SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
            EditorApplication.isPlaying = false;
        }
        else if (stage == 2)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Finish(SessionState.GetInt(CodeKey, 1));
        }
    }

    private static void Fail(string msg)
    {
        Debug.LogError("[RoomRender] FAIL " + msg);
        Finish(1);
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Poll;
        SessionState.EraseInt(StageKey);
        SessionState.EraseFloat(StartKey);
        SessionState.EraseInt(CodeKey);
        Debug.Log("[RoomRender] exit " + code);
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    private struct View
    {
        public Vector3 pos;
        public Quaternion rot;
        public bool ortho;
        public float orthoSize;
        public float fov;
    }

    private static bool RenderAll()
    {
        var anchors = new Transform[Anchors.Length];
        var present = new List<string>();
        var missing = new List<string>();
        for (int i = 0; i < Anchors.Length; i++)
        {
            anchors[i] = FindInScene(Anchors[i]);
            if (anchors[i] != null) present.Add(Anchors[i]); else missing.Add(Anchors[i]);
        }
        if (present.Count > 0 && missing.Count > 0)
        {
            Debug.LogError("[RoomRender] FAIL missing camera anchor(s): " + string.Join(", ", missing.ToArray()));
            return false;
        }
        bool fallback = present.Count == 0;
        if (fallback)
            Debug.Log("[RoomRender] no CAM_* anchors in the scene; using fixed viewpoints from the ControlRoom bounds");

        string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        string outDir = Path.Combine(repoRoot, "out", "render");
        Directory.CreateDirectory(outDir);

        bool ok = true;
        var baseViews = new View?[2];
        for (int i = 0; i < Anchors.Length; i++)
        {
            string path = Path.Combine(outDir, "unity-" + Names[i] + ".png");
            View view;
            if (fallback)
            {
                if (!TryFallbackView(Names[i], out view))
                {
                    Debug.LogError("[RoomRender] FAIL " + Names[i] + ": no ControlRoom renderers to frame");
                    ok = false;
                    continue;
                }
            }
            else
            {
                view = new View { pos = anchors[i].position, rot = anchors[i].rotation, fov = 60f };
                var ac = anchors[i].GetComponent<Camera>();
                if (ac != null) { view.fov = ac.fieldOfView; view.ortho = ac.orthographic; view.orthoSize = ac.orthographicSize; }
            }
            if (i < 2) baseViews[i] = view;   // seat and stand positions seed the window views below
            Debug.Log($"[RoomRender] view {Names[i]} pos={view.pos} forward={view.rot * Vector3.forward} fov={view.fov} ortho={view.ortho}");
            // The top view looks down through the roof: hide the ceiling slab and fixtures the way render_room.py does.
            var hidden = Names[i] == "top" ? HideCeiling() : null;
            try
            {
                if (!RenderView(view, path, Names[i])) ok = false;
            }
            finally
            {
                if (hidden != null) foreach (var r in hidden) r.enabled = true;
            }
        }
        if (!RenderWindowViews(baseViews, outDir)) ok = false;
        return ok;
    }

    // U6: two views of the reactor window (the glass pane named Window_Glass). A seated operator cannot turn to the window
    // (it is behind the chair, outside SeatedYawRange), so "window-seat" is a camera at the seat position at the real SEATED eye
    // height (the eye HallSightline.Seat() checks) aimed at the glass; "window-stand" is taken from STAND_Center.
    private static bool RenderWindowViews(View?[] baseViews, string outDir)
    {
        var glass = FindInScene("Window_Glass");
        var glassR = glass != null ? glass.GetComponent<Renderer>() : null;
        if (glassR == null)
        {
            Debug.LogError("[RoomRender] FAIL window views: no Window_Glass in the scene (rebuild with ReactorSim > Build Phase-2 Scene)");
            return false;
        }
        Vector3 target = glassR.bounds.center;
        string[] names = { "window-seat", "window-stand" };
        bool ok = true;
        for (int i = 0; i < 2; i++)
        {
            if (!baseViews[i].HasValue)
            {
                Debug.LogError("[RoomRender] FAIL " + names[i] + ": base viewpoint missing");
                ok = false;
                continue;
            }
            View v = baseViews[i].Value;
            Vector3 dir = target - v.pos;
            v.rot = dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : Quaternion.identity;
            v.ortho = false;
            v.fov = 60f;
            Debug.Log($"[RoomRender] view {names[i]} pos={v.pos} forward={v.rot * Vector3.forward} fov={v.fov}");
            if (!RenderView(v, Path.Combine(outDir, "unity-" + names[i] + ".png"), names[i])) ok = false;
        }
        return ok;
    }

    private static System.Collections.Generic.List<Renderer> HideCeiling()
    {
        var hidden = new System.Collections.Generic.List<Renderer>();
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            string n = r.gameObject.name;
            if (r.enabled && (n == "Ceiling" || n.StartsWith("Fixture_")))
            {
                r.enabled = false;
                hidden.Add(r);
            }
        }
        Debug.Log("[RoomRender] top view: hid " + hidden.Count + " ceiling renderers");
        return hidden;
    }

    private static bool RenderView(View v, string path, string label)
    {
        var go = new GameObject("RoomRender_Cam_" + label);
        // HallVisibility gates the hall on Camera.main (the operator's camera, which may face away from the window); this camera is
        // a different one, so without this the hall root is off and the window renders the clear colour.
        GameObject hall = HallVisibility.FindHall();
        bool hallWasOn = hall != null && hall.activeSelf;
        if (hall != null) hall.SetActive(true);
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        Texture2D tex = null;
        var prev = RenderTexture.active;
        try
        {
            go.transform.SetPositionAndRotation(v.pos, v.rot);
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.08f, 1f);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 500f;
            cam.orthographic = v.ortho;
            if (v.ortho) cam.orthographicSize = v.orthoSize; else cam.fieldOfView = v.fov;
            cam.aspect = (float)Width / Height;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            if (png == null || png.Length < 1024)
            {
                Debug.LogError("[RoomRender] FAIL " + label + ": PNG empty or tiny (" + (png == null ? 0 : png.Length) + " bytes)");
                return false;
            }
            if (IsFlat(tex))
            {
                Debug.LogError("[RoomRender] FAIL " + label + ": image is a single flat colour");
                return false;
            }
            File.WriteAllBytes(path, png);
            Debug.Log("[RoomRender] OK " + path + " " + png.Length);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[RoomRender] FAIL " + label + ": " + e.Message);
            return false;
        }
        finally
        {
            if (hall != null) hall.SetActive(hallWasOn);
            RenderTexture.active = prev;
            var c = go.GetComponent<Camera>();
            if (c != null) c.targetTexture = null;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(go);
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }
    }

    // Samples a 40x24 grid; flat when every sample is within 2/255 of the first.
    private static bool IsFlat(Texture2D tex)
    {
        Color32 first = tex.GetPixel(0, 0);
        for (int y = 0; y < Height; y += Height / 24)
            for (int x = 0; x < Width; x += Width / 40)
            {
                Color32 c = tex.GetPixel(x, y);
                if (Mathf.Abs(c.r - first.r) > 2 || Mathf.Abs(c.g - first.g) > 2 || Mathf.Abs(c.b - first.b) > 2)
                    return false;
            }
        return true;
    }

    private static Transform FindInScene(string name)
    {
        var scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            var t = ControlRoomSceneBuilder.FindDeep(root.transform, name);
            if (t != null) return t;
        }
        return null;
    }

    private static bool TryFallbackView(string key, out View view)
    {
        view = default(View);
        var room = GameObject.Find("ControlRoom");
        if (room == null) return false;
        Bounds b = default(Bounds);
        bool any = false;
        float floorTop = float.NaN;
        foreach (var r in room.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            if (r.name == "Floor") floorTop = r.bounds.max.y;
        }
        if (!any) return false;
        if (float.IsNaN(floorTop)) floorTop = b.min.y;

        var wallT = FindInScene("MimicWall");
        var wallR = wallT != null ? wallT.GetComponentInChildren<Renderer>() : null;
        Vector3 wall = wallR != null ? wallR.bounds.center : b.center;

        if (key == "top")
        {
            view.pos = new Vector3(b.center.x, b.max.y + 5f, b.center.z);
            view.rot = Quaternion.Euler(90f, 0f, 0f);
            view.ortho = true;
            view.orthoSize = Mathf.Max(b.size.z * 0.5f, b.size.x * 0.5f * Height / Width) * 1.05f;
            view.fov = 60f;
            return true;
        }

        Vector3 eye;
        if (key == "seat")
        {
            var chair = FindInScene("Station_A1_Reactor_Chair");
            var chairR = chair != null ? chair.GetComponentInChildren<Renderer>() : null;
            eye = chairR != null ? chairR.bounds.center + Vector3.up * 0.6f
                                 : new Vector3(b.center.x, floorTop + 1.2f, b.center.z);
        }
        else
        {
            eye = new Vector3(b.center.x, floorTop + 1.7f, b.center.z);
        }
        Vector3 dir = wall - eye;
        view.pos = eye;
        view.rot = dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : Quaternion.identity;
        view.fov = 60f;
        return true;
    }
}
#endif
