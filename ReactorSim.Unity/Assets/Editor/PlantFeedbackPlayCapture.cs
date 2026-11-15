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
            CheckFlowCues(cam, view, map, dir);
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

    // U2: the wall's click-to-select detail plate and route direction cues, exercised through the real components. Every wall node is
    // clicked through DiegeticInteractable's own OnMouseDown (select, move, clear, prompt), the plate text must follow the model for a
    // normal, a changed and an all-NaN snapshot and stay inside its backing plate for every component (this is the overflow assertion
    // at the capture size), and a close capture of the prompt and of a selected component is saved (live-detail-prompt.png,
    // live-detail-a.png, live-detail-b.png) so the result can be looked at as well as asserted.
    private static void CaptureLiveDetail(Camera cam, PlantWallMapView view, Transform map, Transform summaryPlate, string dir)
    {
        var plate = ControlRoomSceneBuilder.FindDeep(map, "LiveDetailPlate");
        var text = ControlRoomSceneBuilder.FindDeep(map, "LiveDetail");
        var tmp = text != null ? text.GetComponent<TMPro.TextMeshPro>() : null;
        if (plate == null || tmp == null) throw new InvalidOperationException("Live detail plate or text missing");
        var a = new PlantSnapshot { BankInsertion = new[] { 0.1, 0.2, 0.3, 0.4 }, Power = 0.42, FuelTempC = 456, PrimaryPressureBar = 155.2, BoronPpm = 1180, SimSeconds = 3725 };
        var b = new PlantSnapshot { BankInsertion = new[] { 0.5, 0.5, 0.5, 0.5 }, Power = 0.87, FuelTempC = 512, PrimaryPressureBar = 156.8, BoronPpm = 1090, SimSeconds = 3730 };
        double nan = double.NaN;
        var missing = new PlantSnapshot { Power = nan, FuelTempC = nan, TotalReactivityPcm = nan, PeriodSeconds = nan, PrimaryFlowFrac = nan,
            PrimaryTempC = nan, PrimaryLevelFrac = nan, PrimaryPressureBar = nan, PrimarySatMarginC = nan, HeaterPowerFrac = nan,
            SprayFrac = nan, SecondaryPressureBar = nan, SecondaryTempC = nan, SecondaryLevelFrac = nan, SteamDemandFrac = nan,
            DumpSteamKW = nan, TurbineRpm = nan, TurbineSteamKW = nan, GeneratorKw = nan, GridFrequencyHz = nan, GridOutputFrac = nan,
            CondenserPressureBar = nan, HotwellInventoryFrac = nan, VacuumPumpFrac = nan, CoolingWaterFrac = nan,
            FeedwaterDeliveredFrac = nan, FeedwaterFlowFrac = nan, FeedwaterAvailabilityFrac = nan, BoronPpm = nan, Ph = nan,
            BankInsertion = new[] { nan, nan, nan, nan } };
        var snapshots = new[] { a, b, missing };

        view.ClearSelection();
        view.ApplySnapshot(a);
        tmp.ForceMeshUpdate();
        if (tmp.text != WallDetailModel.Prompt) throw new InvalidOperationException("With nothing selected the plate does not show the prompt: " + tmp.text.Replace("\n", " | "));
        AssertInsidePlate(tmp, "the prompt");
        CloseCapture(cam, plate, map, Path.Combine(dir, "live-detail-prompt.png"));

        foreach (string component in WallDetailModel.Components)
        {
            var node = ControlRoomSceneBuilder.FindDeep(map, "System_" + component);
            var click = node != null ? node.GetComponent<DiegeticInteractable>() : null;
            if (click == null || !click.IsWallNode || click.wallComponent != component || node.GetComponent<Collider>() == null)
                throw new InvalidOperationException("Wall node System_" + component + " has no click target (collider + wall DiegeticInteractable)");
            node.SendMessage("OnMouseDown");   // the real click path
            if (view.SelectedComponent != component) throw new InvalidOperationException("Clicking " + component + " selected " + (view.SelectedComponent ?? "nothing"));
            for (int i = 0; i < snapshots.Length; i++)
            {
                view.ApplySnapshot(snapshots[i]);
                tmp.ForceMeshUpdate();
                string want = WallDetailModel.DetailText(component, snapshots[i]);
                if (tmp.text != want) throw new InvalidOperationException(component + " plate text does not follow the model: " + tmp.text.Replace("\n", " | "));
                if (i == 2 && (tmp.text.Contains("NaN") || tmp.text.Contains("Infinity"))) throw new InvalidOperationException(component + " shows a raw NaN: " + tmp.text.Replace("\n", " | "));
                AssertInsidePlate(tmp, component + " snapshot " + i);
            }
            node.SendMessage("OnMouseDown");   // clicking it again clears
            if (view.SelectedComponent != null) throw new InvalidOperationException("Clicking " + component + " again did not clear the selection");
            view.ApplySnapshot(a);
            tmp.ForceMeshUpdate();
            if (tmp.text != WallDetailModel.Prompt) throw new InvalidOperationException("The plate did not return to the prompt after clearing " + component);
        }

        // Clicking another component moves the selection, and the highlight follows it.
        var first = ControlRoomSceneBuilder.FindDeep(map, "System_Reactor");
        var second = ControlRoomSceneBuilder.FindDeep(map, "System_Turbine");
        var firstRenderer = first.GetComponent<Renderer>();
        var secondRenderer = second.GetComponent<Renderer>();
        var props = new MaterialPropertyBlock();
        first.SendMessage("OnMouseDown");
        view.ApplySnapshot(a);
        firstRenderer.GetPropertyBlock(props);
        Color lit = props.GetColor("_Color");
        if (!firstRenderer.HasPropertyBlock() || lit.maxColorComponent <= 0f) throw new InvalidOperationException("The selected component is not highlighted");
        second.SendMessage("OnMouseDown");
        if (view.SelectedComponent != "Turbine") throw new InvalidOperationException("Clicking another component did not move the selection");
        view.ApplySnapshot(a);
        if (firstRenderer.HasPropertyBlock()) throw new InvalidOperationException("The previous component is still highlighted after the selection moved");
        if (!secondRenderer.HasPropertyBlock()) throw new InvalidOperationException("The newly selected component is not highlighted");
        view.ClearSelection();
        if (secondRenderer.HasPropertyBlock()) throw new InvalidOperationException("The highlight stayed after the selection was cleared");

        // Captures of a selected component under two different snapshots.
        first.SendMessage("OnMouseDown");
        string[] seen = new string[2];
        var shown = new[] { a, b };
        for (int i = 0; i < 2; i++)
        {
            view.ApplySnapshot(shown[i]);
            tmp.ForceMeshUpdate();
            seen[i] = tmp.text;
            AssertInsidePlate(tmp, "reactor capture " + i);
            CloseCapture(cam, plate, map, Path.Combine(dir, i == 0 ? "live-detail-a.png" : "live-detail-b.png"));
        }
        if (seen[0] == seen[1]) throw new InvalidOperationException("Live detail text did not change between snapshots");
        if (!seen[0].Contains("PWR 42.0 %") || !seen[1].Contains("PWR 87.0 %")) throw new InvalidOperationException("Reactor readout does not match the supplied snapshots");
        view.ClearSelection();
        Debug.Log("[PlantFeedbackPlayCapture] OK wall detail: prompt, click select/move/clear on " + WallDetailModel.Components.Length + " components, text inside the plate for normal/changed/NaN snapshots");
    }

    private static void AssertInsidePlate(TMPro.TextMeshPro tmp, string what)
    {
        if (tmp.textBounds.size.x > tmp.rectTransform.rect.width + 0.001f || tmp.textBounds.size.y > tmp.rectTransform.rect.height + 0.001f)
            throw new InvalidOperationException("Wall detail text overflows its backing plate (" + what + "): " + tmp.textBounds.size + " in " + tmp.rectTransform.rect.size);
    }

    private static void CloseCapture(Camera cam, Transform plate, Transform map, string file)
    {
        cam.transform.position = plate.position + map.forward * 2.2f;
        cam.transform.LookAt(plate.position);
        Capture(cam, file);
    }

    // U2 (R4): one arrowhead per directed route, off at zero activity and for a missing reading, on while the model gives the route a
    // direction, and calibrated: the first point of every route's authored path lands on that route's own first pipe segment.
    private static void CheckFlowCues(Camera cam, PlantWallMapView view, Transform map, string dir)
    {
        if (view.FlowCueCount != PlantWallMapModel.DirectedRoutes.Length)
            throw new InvalidOperationException("Expected " + PlantWallMapModel.DirectedRoutes.Length + " flow cues, found " + view.FlowCueCount);
        view.ApplySnapshot(new PlantSnapshot());
        foreach (string route in PlantWallMapModel.DirectedRoutes)
        {
            var cue = view.CueFor(route);
            if (cue == null) throw new InvalidOperationException("No flow cue for " + route);
            if (cue.gameObject.activeSelf) throw new InvalidOperationException("Flow cue " + route + " is shown at zero activity");
        }
        var flowing = new PlantSnapshot { PrimaryFlowFrac = 0.8, FeedwaterDeliveredFrac = 0.6, GridOutputFrac = 0.5,
            TurbineSteamKW = ReactorSim.Physics.RatedThermalKW * 0.6, DumpSteamKW = ReactorSim.Physics.RatedThermalKW * 0.2 };
        view.ApplySnapshot(flowing);
        foreach (string route in PlantWallMapModel.DirectedRoutes)
        {
            var cue = view.CueFor(route);
            if (!cue.gameObject.activeSelf) throw new InvalidOperationException("Flow cue " + route + " is not shown while the route flows");
            var pipe = ControlRoomSceneBuilder.FindDeep(map, "Pipe_" + route + "_00");
            var pipeRenderer = pipe != null ? pipe.GetComponent<Renderer>() : null;
            if (pipeRenderer == null) throw new InvalidOperationException("Pipe_" + route + "_00 missing");
            var first = PlantWallMapModel.RoutePath(route)[0];
            Bounds near = pipeRenderer.bounds;
            near.Expand(0.06f);
            Vector3 world = view.BoardToWorld(first[0], first[1]);
            if (!near.Contains(world))
                throw new InvalidOperationException("Flow cue calibration is off for " + route + ": path start " + world + " is outside " + near);
        }
        if (view.CueFor("Shaft") != null) throw new InvalidOperationException("A route with no known flow sign has a cue");
        view.ApplySnapshot(new PlantSnapshot { PrimaryFlowFrac = double.NaN, FeedwaterDeliveredFrac = double.NaN, GridOutputFrac = double.NaN,
            TurbineSteamKW = double.NaN, DumpSteamKW = double.NaN });
        foreach (string route in PlantWallMapModel.DirectedRoutes)
            if (view.CueFor(route).gameObject.activeSelf) throw new InvalidOperationException("Flow cue " + route + " is shown for a missing reading");
        view.ApplySnapshot(flowing);
        // A look at the whole wall with every directed route flowing and one component selected (wall-flow.png).
        var back = ControlRoomSceneBuilder.FindDeep(map, "Board_Back");
        if (back == null) throw new InvalidOperationException("Board_Back missing");
        view.ClickComponent("SteamGenerator");
        view.ApplySnapshot(flowing);
        Vector3 boardCenter = back.GetComponent<Renderer>().bounds.center;
        cam.transform.position = boardCenter + map.forward * 3.4f;
        cam.transform.LookAt(boardCenter);
        Capture(cam, Path.Combine(dir, "wall-flow.png"));
        view.ClearSelection();
        view.ApplySnapshot(flowing);
        Debug.Log("[PlantFeedbackPlayCapture] OK flow cues: " + view.FlowCueCount + " routes, hidden at zero and NaN, shown while flowing, calibrated to their pipes");
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
