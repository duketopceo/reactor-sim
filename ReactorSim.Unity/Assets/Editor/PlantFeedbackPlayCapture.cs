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
                hall.SetActive(true);
                var controller = cam.GetComponent<OperatorController>();
                if (controller != null) controller.enabled = false;
                var stand = ControlRoomSceneBuilder.FindDeep(room.transform, "CAM_Stand_Center");
                if (stand == null) throw new InvalidOperationException("Standing camera missing");
                cam.transform.position = stand.position;
                cam.transform.LookAt(hall.transform.TransformPoint(new Vector3(0, HallDimensions.SightTargetHeight, 0)));
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
            CheckReliefBindings();
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

    private static void CheckReliefBindings()
    {
        // The currently placed hall has no relief anchors, so exercise both authored anchor types in a temporary fixture.
        var fixture = new GameObject("ReliefBindingFixture");
        fixture.SetActive(false);
        var primary = new GameObject("VFX_PORV_SteamJet_Anchor");
        primary.transform.SetParent(fixture.transform, false);
        var secondary = new GameObject("VFX_SGRelief_SteamJet_Anchor_1");
        secondary.transform.SetParent(fixture.transform, false);
        var binder = fixture.AddComponent<PlantEquipmentBinder>();
        fixture.SetActive(true);
        var priJet = primary.GetComponentInChildren<ParticleSystem>();
        var secJet = secondary.GetComponentInChildren<ParticleSystem>();
        if (priJet == null || secJet == null) throw new InvalidOperationException("Relief fixture jets not bound");
        binder.ApplyReliefState(true, false);
        if (priJet.emission.rateOverTime.constant <= 0 || secJet.emission.rateOverTime.constant != 0)
            throw new InvalidOperationException("Primary relief incorrectly drives secondary relief");
        binder.ApplyReliefState(false, true);
        if (priJet.emission.rateOverTime.constant != 0 || secJet.emission.rateOverTime.constant <= 0)
            throw new InvalidOperationException("Secondary relief incorrectly drives primary relief");
        UnityEngine.Object.Destroy(fixture);
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
