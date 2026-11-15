using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ReactorSim;

// Captures a generated preview scene in play mode without saving over the working scene.
[InitializeOnLoad]
public static class PlantFeedbackPlayCapture
{
    private const string Stage = "PlantFeedbackPlayCapture.Stage";
    private const string Started = "PlantFeedbackPlayCapture.Started";
    static PlantFeedbackPlayCapture() { if (SessionState.GetInt(Stage, 0) != 0) Arm(); }

    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-feedbackScene");
        if (i < 0 || i + 1 >= args.Length) { Debug.LogError("[PlantFeedbackPlayCapture] Missing scene"); EditorApplication.Exit(1); return; }
        EditorSceneManager.OpenScene(args[i + 1], OpenSceneMode.Single);
        SessionState.SetInt(Stage, 1);
        SessionState.SetFloat(Started, (float)EditorApplication.timeSinceStartup);
        Arm();
        PlayModeGuard.Prepare();
        EditorApplication.isPlaying = true;
    }

    private static void Arm() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }
    private static void Poll()
    {
        int stage = SessionState.GetInt(Stage, 0);
        if (stage == 0) return;
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Started, 0) > 120)
        { Debug.LogError("[PlantFeedbackPlayCapture] Timeout"); Finish(1); return; }
        if (stage == 3) { if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish(0); return; }
        if (!EditorApplication.isPlaying) return;
        try
        {
            var hall = HallVisibility.FindHall();
            var room = ControlRoomSceneBuilder.FindRoot();
            var cam = Camera.main;
            if (hall == null || room == null || cam == null) throw new InvalidOperationException("Missing hall, room or camera");
            if (stage == 1)
            {
                AudioListener.pause = true;   // automated runs are silent
                hall.SetActive(true);
                var controller = cam.GetComponent<OperatorController>();
                if (controller != null) controller.enabled = false;
                var stand = ControlRoomSceneBuilder.FindDeep(room.transform, "CAM_Stand_Center");
                if (stand == null) throw new InvalidOperationException("Standing camera missing");
                cam.transform.position = stand.position;
                cam.transform.LookAt(hall.transform.TransformPoint(new Vector3(0, HallDimensions.SightTargetHeight, HallDimensions.ModuleZ)));
                cam.fieldOfView = 60;
                SessionState.SetFloat(Started, (float)EditorApplication.timeSinceStartup);
                SessionState.SetInt(Stage, 2);
                return;
            }
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Started, 0) < 6) return;
            if (GameObject.Find("ReactorInspectionPrompt") != null)
                throw new InvalidOperationException("The idle reactor inspection prompt is still visible");
            hall.SetActive(true);
            var bubbles = ControlRoomSceneBuilder.FindDeep(hall.transform, "Pool_VisibleBubbles");
            var ps = bubbles != null ? bubbles.GetComponent<ParticleSystem>() : null;
            if (ps == null || ps.particleCount == 0 || ps.GetComponent<ParticleSystemRenderer>().sharedMaterial == null)
                throw new InvalidOperationException("Underwater particles are not active");
            var soundscape = UnityEngine.Object.FindAnyObjectByType<ReactorSoundscape>();
            if (soundscape == null || soundscape.GetComponentsInChildren<AudioSource>().Length < 6)
                throw new InvalidOperationException("Reactor sound layers are missing");
            CheckReliefBindings(hall);
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../out/plant-feedback-play"));
            Directory.CreateDirectory(dir);
            Capture(cam, Path.Combine(dir, "reactor.png"));
            var seat = ControlRoomSceneBuilder.FindDeep(room.transform, "CAM_Stand_Center");
            var map = ControlRoomSceneBuilder.FindDeep(room.transform, "WholePlantOverview");
            if (seat == null || map == null) throw new InvalidOperationException("Missing seat=" + (seat == null) + " map=" + (map == null));
            var view = map.GetComponent<PlantWallMapView>();
            var rod = ControlRoomSceneBuilder.FindDeep(map, "RodBank_A");
            var feed = ControlRoomSceneBuilder.FindDeep(map, "Pipe_Feedwater_00");
            var dump = ControlRoomSceneBuilder.FindDeep(map, "Pipe_SteamDump_00");
            if (view == null || rod == null || feed == null || dump == null) throw new InvalidOperationException("Missing live wall binding or bypass");
            var panel = ControlRoomSceneBuilder.FindDeep(map, "LiveMetricsPlate");
            var text = ControlRoomSceneBuilder.FindDeep(map, "LiveMetrics");
            if (panel == null || text == null || text.GetComponent<TMPro.TextMeshPro>() == null)
                throw new InvalidOperationException("Live metrics panel missing");
            var state = new PlantSnapshot { BankInsertion = new[] { 0.0, 0.0, 0.0, 0.0 }, FeedwaterDeliveredFrac = 0.0 };
            view.ApplySnapshot(state);
            var withdrawn = rod.position;
            var properties = new MaterialPropertyBlock();
            var feedRenderer = feed.GetComponent<Renderer>();
            if (feedRenderer == null) throw new InvalidOperationException("Feedwater renderer missing");
            feedRenderer.GetPropertyBlock(properties);
            Color idle = properties.GetColor("_Color");
            state.BankInsertion[3] = 1.0;
            state.FeedwaterDeliveredFrac = 1.0;
            state.Power = .42;
            state.FuelTempC = 456;
            view.ApplySnapshot(state);
            feedRenderer.GetPropertyBlock(properties);
            if (Vector3.Distance(withdrawn, rod.position) < 0.01f || properties.GetColor("_Color").maxColorComponent <= idle.maxColorComponent)
                throw new InvalidOperationException("Live rod or delivered feedwater binding did not respond");
            if (!text.GetComponent<TMPro.TextMeshPro>().text.Contains("POWER 42.0 %") ||
                !text.GetComponent<TMPro.TextMeshPro>().text.Contains("FUEL 456.0 C"))
                throw new InvalidOperationException("Live metrics do not match the supplied snapshot");
            var metrics = text.GetComponent<TMPro.TextMeshPro>();
            metrics.ForceMeshUpdate();
            if (metrics.textBounds.size.x > metrics.rectTransform.rect.width || metrics.textBounds.size.y > metrics.rectTransform.rect.height)
                throw new InvalidOperationException("Live metrics overflow their backing panel");
            CaptureLiveDetail(cam, view, map, panel, dir);
            view.ApplySnapshot(view.bridge != null ? view.bridge.GetSnapshot() : null);
            var renderers = map.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("No wall renderers");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            cam.transform.position = seat.position;
            cam.transform.LookAt(bounds.center);
            Capture(cam, Path.Combine(dir, "wall.png"));
            cam.transform.position = panel.position + map.forward * 2.2f;
            cam.transform.LookAt(panel.position);
            Capture(cam, Path.Combine(dir, "wall-metrics-close.png"));
            Debug.Log("[PlantFeedbackPlayCapture] PASS bubbles=" + ps.particleCount + " " + dir);
            SessionState.SetInt(Stage, 3);
            EditorApplication.isPlaying = false;
        }
        catch (Exception ex) { Debug.LogError("[PlantFeedbackPlayCapture] FAIL " + ex); Finish(1); }
    }

    // The grouped-readings plate under the summary plate: apply two different snapshots, require the text to follow each one, stay inside
    // its backing plate, and save a close capture of each (live-detail-a.png, live-detail-b.png) so the change can be seen as well as asserted.
    private static void CaptureLiveDetail(Camera cam, PlantWallMapView view, Transform map, Transform summaryPlate, string dir)
    {
        var plate = ControlRoomSceneBuilder.FindDeep(map, "LiveDetailPlate");
        var text = ControlRoomSceneBuilder.FindDeep(map, "LiveDetail");
        var tmp = text != null ? text.GetComponent<TMPro.TextMeshPro>() : null;
        if (plate == null || tmp == null) throw new InvalidOperationException("Live detail plate or text missing");
        var a = new PlantSnapshot { BankInsertion = new[] { 0.1, 0.2, 0.3, 0.4 }, Power = 0.42, FuelTempC = 456, PrimaryPressureBar = 155.2, BoronPpm = 1180, SimSeconds = 3725 };
        var b = new PlantSnapshot { BankInsertion = new[] { 0.5, 0.5, 0.5, 0.5 }, Power = 0.87, FuelTempC = 512, PrimaryPressureBar = 156.8, BoronPpm = 1090, SimSeconds = 3730 };
        string[] seen = new string[2];
        var snapshots = new[] { a, b };
        string[] expect = { "CORE 42.0 %", "CORE 87.0 %" };
        string[] expectTemp = { "456.0 C", "512.0 C" };
        for (int i = 0; i < 2; i++)
        {
            view.ApplySnapshot(snapshots[i]);
            tmp.ForceMeshUpdate();
            seen[i] = tmp.text;
            if (!seen[i].Contains(expect[i]) || !seen[i].Contains(expectTemp[i]))
                throw new InvalidOperationException("Live detail did not show snapshot " + i + ": " + seen[i].Replace("\n", " | "));
            if (tmp.textBounds.size.x > tmp.rectTransform.rect.width + 0.001f || tmp.textBounds.size.y > tmp.rectTransform.rect.height + 0.001f)
                throw new InvalidOperationException("Live detail overflows its backing plate in snapshot " + i);
            cam.transform.position = plate.position + map.forward * 2.2f;
            cam.transform.LookAt(plate.position);
            Capture(cam, Path.Combine(dir, i == 0 ? "live-detail-a.png" : "live-detail-b.png"));
        }
        if (seen[0] == seen[1]) throw new InvalidOperationException("Live detail text did not change between snapshots");
        Debug.Log("[PlantFeedbackPlayCapture] OK live detail follows the snapshot: A=" + seen[0].Split('\n')[0] + " B=" + seen[1].Split('\n')[0]);
    }

    // The real hall's own anchors (HallBuilder.BuildReliefAnchors), through the real binder: each jet answers only its own signal.
    private static void CheckReliefBindings(GameObject hall)
    {
        var binder = hall.GetComponent<PlantEquipmentBinder>();
        if (binder == null) throw new InvalidOperationException("Hall has no equipment binder");
        var pri = JetAt(hall, ReliefAnchors.PrimaryName);
        var sec = new System.Collections.Generic.List<ParticleSystem>();
        for (int i = 0; i < ReliefAnchors.SecondaryCount; i++) sec.Add(JetAt(hall, ReliefAnchors.SecondaryName(i)));
        var byp = JetAt(hall, ReliefAnchors.BypassName);
        binder.ApplyReliefState(true, false); binder.ApplyBypass(0f);
        Expect(Rate(pri) > 0 && Rate(sec[0]) == 0 && Rate(sec[1]) == 0 && Rate(byp) == 0, "primary relief alone drives only the PORV jet");
        binder.ApplyReliefState(false, true);
        Expect(Rate(pri) == 0 && Rate(sec[0]) > 0 && Rate(sec[1]) > 0 && Rate(byp) == 0, "secondary relief alone drives only the SG relief jets");
        var trip = HallSteamModel.From(new PlantSnapshot { TurbineTripped = true, TurbineSteamKW = 0, DumpSteamKW = ReactorSim.Physics.RatedThermalKW * .3 });
        binder.ApplyReliefState(trip.PrimaryRelief, trip.SecondaryRelief); binder.ApplyBypass(trip.Bypass);
        Expect(Rate(pri) == 0 && Rate(sec[0]) == 0 && Rate(byp) > 0, "bypass steam alone (turbine tripped) drives only the bypass jet");
        binder.ApplyReliefState(false, false); binder.ApplyBypass(0f);
        Expect(Rate(pri) == 0 && Rate(sec[0]) == 0 && Rate(byp) == 0, "all jets stop when nothing is open");
    }

    private static ParticleSystem JetAt(GameObject hall, string anchor)
    {
        var t = ControlRoomSceneBuilder.FindDeep(hall.transform, anchor);
        var ps = t != null ? t.GetComponentInChildren<ParticleSystem>(true) : null;
        if (ps == null) throw new InvalidOperationException("Relief anchor or its bound jet missing: " + anchor);
        return ps;
    }

    private static float Rate(ParticleSystem ps) { return ps.emission.rateOverTime.constant; }

    private static void Expect(bool ok, string what)
    {
        if (!ok) throw new InvalidOperationException("Relief binding: " + what);
        Debug.Log("[PlantFeedbackPlayCapture] OK " + what);
    }

    private static void Capture(Camera cam, string path)
    {
        var rt = new RenderTexture(1600, 900, 24);
        var previous = RenderTexture.active;
        var old = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        cam.targetTexture = old;
        RenderTexture.active = previous;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(rt);
    }

    private static void Finish(int code)
    {
        SessionState.SetInt(Stage, 0);
        EditorApplication.update -= Poll;
        EditorApplication.Exit(code);
    }
}
