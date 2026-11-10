using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ReactorSim.Controls;
using ReactorSim.Instruments;
using ReactorSim.Ui;
using ReactorSim.View3D;
using System.Collections.Generic;

// Control-room scene builder: installs the Blender FBX, camera, audio and CRT renderer components into
// Bootstrap.unity. Run from menu:  ReactorSim > Build Phase-2 Scene
// KTD12: every build deletes and re-instances the ControlRoom (and any old PlantEquipment) so a regenerated room
// always replaces the previous one. PlantEquipment is not part of this scene any more; the only plant geometry is the
// procedural "ReactorHall" (HallBuilder: an integral SMR module in a deep pool) seen through the south-wall window, a separate scene root so it never joins the
// room's bounds, lights or colliders.
//   - OperatorController + CameraShake on the main camera (tagged MainCamera), AlarmFeedback on the room
//   - a CrtScreenRenderer on every CRT_* screen mesh (not the *_Glow strip)
//   - a DiegeticInteractable + fitted BoxCollider on every INT_* control pivot (not its _Knob/_Pointer/_Control
//     meshes), carrying the inventory row's label and guard flag; out-of-service rows get a hover-only interactable
//   - COL_* collision proxies become BoxColliders on the Ignore Raycast layer with their renderer disabled: they
//     block the CharacterController but not the OnMouse* ray
//   - the imported CAM_* cameras are disabled (RoomRender reads their pose only)
public static class ControlRoomSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Bootstrap.unity";
    private const string FbxDir = "Assets/Blender";

    [MenuItem("ReactorSim/Build Phase-2 Scene")]
    public static void BuildPhase2Scene()
    {
        Build(true);
    }

    /// <summary>Builds the scene hierarchy; when save is false the result stays in memory (used by the checks).</summary>
    public static UnityEngine.SceneManagement.Scene Build(bool save)
    {
        var scene = EditorSceneManager.GetActiveScene();
        bool needOpen = string.IsNullOrEmpty(scene.path)
            || (scene.path != ScenePath && !scene.name.Contains("Bootstrap"));
        if (needOpen)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError("[Phase2SceneBuilder] Bootstrap.unity not found.");
                return scene;
            }
        }

        var crPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FbxDir + "/ControlRoom.fbx");

        // KTD12: replace, never reuse. (Old PlantEquipment instances go too; the room has no plant geometry.)
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "ControlRoom" || root.name == "PlantEquipment" || root.name == HallBuilder.RootName || root.name == HallBuilder.GateName)
                Object.DestroyImmediate(root);
        RemoveMissingScripts(scene);
        ApplyRoomAmbient();

        GameObject bridgeGo = GameObject.Find("PlantSim");
        if (bridgeGo == null)
        {
            bridgeGo = new GameObject("PlantSim");
            bridgeGo.AddComponent<PlantBridge>();
        }
        var bridge = bridgeGo.GetComponent<PlantBridge>();
        // U14: handheld operator tablet (Tab). Lives on the PlantSim object beside PlantBridge.
        if (bridgeGo.GetComponent<OperatorTablet>() == null) bridgeGo.AddComponent<OperatorTablet>();

        var crRoot = new GameObject("ControlRoom");
        if (crPrefab != null)
            PrefabUtility.InstantiatePrefab(crPrefab, crRoot.transform);
        else
            Debug.LogError("[Phase2SceneBuilder] " + FbxDir + "/ControlRoom.fbx not found.");
        var binder = crRoot.AddComponent<ControlRoom3DBinder>();
        binder.bridge = bridge;
        crRoot.AddComponent<AlarmFeedback>().bridge = bridge;
        crRoot.AddComponent<ControlTooltip>().binder = binder;   // U17 hover info

        var rows = new Dictionary<string, DesignData.Row>();
        try
        {
            foreach (var r in DesignData.LoadInventory()) rows[r.Id] = r;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[Phase2SceneBuilder] inventory.csv unreadable, controls get no row metadata: " + ex.Message);
        }

        int collisionBoxes = 0;
        foreach (Transform t in crRoot.GetComponentsInChildren<Transform>(true))
        {
            var importedCam = t.GetComponent<Camera>();
            if (importedCam != null) importedCam.enabled = false; // imported CAM_* viewpoints must not render over the operator
            var importedLight = t.GetComponent<Light>();
            if (importedLight != null) NormaliseRoomLight(importedLight);

            if (t.name.StartsWith("COL_"))
            {
                if (MakeCollisionProxy(t)) collisionBoxes++;
                continue;
            }
            if (t.name.StartsWith("CRT_"))
            {
                var existing = t.GetComponent<CrtScreenRenderer>();
                if (t.name.EndsWith("_Glow"))
                {
                    if (existing != null) Object.DestroyImmediate(existing);
                }
                else if (t.GetComponent<MeshRenderer>() != null)
                {
                    if (existing == null) existing = t.gameObject.AddComponent<CrtScreenRenderer>();
                    existing.bridge = bridge;
                    existing.screenName = t.name;
                    ApplyScreenMaterial(t.GetComponent<MeshRenderer>());
                }
            }
            if (!t.name.StartsWith("INT_")) continue;

            var parsed = PivotNameParser.Parse(t.name);
            if (parsed.Status == ParseStatus.VisualChild) continue; // a mesh under a control pivot, not a control
            // Ok controls and malformed names both get an interactable so a malformed name is reported at play.
            // Hit area: at least HitArea.MinPixels on screen at the default field of view and the playtest window height (R7).
            FitCollider(t, (float)HitArea.MinSideMetres(OperatorMath.FovDefault, 900.0));
            var inter = t.gameObject.AddComponent<DiegeticInteractable>();
            inter.binder = binder;
            DesignData.Row row;
            var ctl = FindCtlRoot(t);
            if (ctl != null && rows.TryGetValue(ctl.name.Substring(4), out row))
            {
                inter.rowId = row.Id;
                inter.rowLabel = row.Label;
                inter.guarded = row["kit"] == "K11" && parsed.Status == ParseStatus.Ok && parsed.Spec.Style == ControlStyle.Button;
            }
        }

        // Out-of-service rows (KTD6): placed with a tag and no INT_ pivot. A hover-only interactable on the kit root
        // says why and refuses clicks.
        int oos = 0;
        foreach (var kv in rows)
        {
            if (!kv.Value.IsOutOfService) continue;
            var ctlRoot = FindDeep(crRoot.transform, "CTL_" + kv.Key);
            if (ctlRoot == null) continue;
            FitCollider(ctlRoot, 0.04f);
            var oosInter = ctlRoot.gameObject.AddComponent<DiegeticInteractable>();
            oosInter.outOfService = true;
            oosInter.rowId = kv.Key;
            oosInter.rowLabel = kv.Value.Label;
            oosInter.outOfServiceReason = kv.Value["readiness"] == "needs_core_model"
                ? "needs a new plant model (" + kv.Value["phase"] + ")"
                : "backend binding pending (" + kv.Value["phase"] + ")";
            oos++;
        }
        AttachStandbyScreens(crRoot, bridge);   // before the darkening: it finds the faces by their exported screen material
        DarkenGlowingScreens(crRoot);
        ApplyRoomLooks(crRoot);
        Debug.Log("[Phase2SceneBuilder] " + collisionBoxes + " collision proxies, " + oos + " out-of-service rows tagged.");

        // U14: indicators have no collider of their own. Each bound gauge row's CTL_ root gets a hover-only collider
        // and a pinnable DiegeticInteractable so OperatorTablet's right-click ray can find it (binding gauge:<Name>[:<index>]).
        int pinnable = 0;
        foreach (var kv in rows)
        {
            var row = kv.Value;
            if (row.IsOutOfService || row["role"] != "indicator") continue;
            string[] bind = row["binding"].Split(':');
            if (bind.Length < 2 || bind[0] != "gauge" || GaugeSources.Find(bind[1]) == null) continue;
            var ctlRoot = FindDeep(crRoot.transform, "CTL_" + kv.Key);
            if (ctlRoot == null || ctlRoot.GetComponent<DiegeticInteractable>() != null) continue;
            FitCollider(ctlRoot, 0.04f);
            var pin = ctlRoot.gameObject.AddComponent<DiegeticInteractable>();
            pin.pinnable = true;
            pin.rowId = kv.Key;
            pin.rowLabel = row.Label;
            pin.pinSelector = bind[1];
            int idx;
            pin.pinIndex = bind.Length > 2 && int.TryParse(bind[2], out idx) ? idx : -1;
            pinnable++;
        }
        Debug.Log("[Phase2SceneBuilder] " + pinnable + " indicator rows made pinnable.");

        // U6: window in the south wall beside the door, and the reactor hall beyond it (primitives, no Blender asset).
        BuildReactorWindow(crRoot.transform);

        // U16: procedure clipboards on the bench in front of each chair (children of crRoot, so a rebuild replaces them).
        PlaceClipboard(crRoot.transform, "SEAT_A1", "CLIPBOARD_A1", "Startup checklist", new[] { "cold-start-to-grid" });
        PlaceClipboard(crRoot.transform, "SEAT_A2", "CLIPBOARD_A2", "Startup checklist", new[] { "cold-start-to-grid" });

        // RoomCam: reuse imported camera if present, else create one.
        Camera cam = Camera.main;
        if (cam == null)
        {
            var found = Object.FindAnyObjectByType<Camera>();
            Transform t = found != null ? found.transform : null;
            if (t != null && t.name.Contains("RoomCam")) cam = found as Camera;
        }
        if (cam == null)
        {
            var camGo = new GameObject("RoomCam");
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.07f, 0.09f, 1f);
            cam.transform.position = new Vector3(0f, 5f, 14f);
            cam.transform.rotation = Quaternion.Euler(82f, 0f, 0f);
            cam.fieldOfView = 24f;
        }
        // Camera.main (OperatorController, CRT raycasts, InstrumentCheck) needs the tag.
        cam.gameObject.tag = "MainCamera";
        // ProceduralPlantAudio loops need a listener or Unity spams "no audio listeners".
        if (cam.GetComponent<AudioListener>() == null)
            cam.gameObject.AddComponent<AudioListener>();
        // Operator (walk, sit, mouse look, zoom to panel, recentre) and the shake it composes.
        if (cam.GetComponent<CameraShake>() == null)
            cam.gameObject.AddComponent<CameraShake>();
        if (cam.GetComponent<OperatorController>() == null)
            cam.gameObject.AddComponent<OperatorController>();

        // Audio root
        if (GameObject.Find("AudioRoot") == null)
        {
            var audioGo = new GameObject("AudioRoot");
            var audio = audioGo.AddComponent<ProceduralPlantAudio>();
            audio.bridge = bridge;
        }

        // U11: mission readout (HUD + runner). Idempotent; the HUD shows the live hint in the sandbox too.
        if (GameObject.Find("MissionUI") == null)
        {
            var missionGo = new GameObject("MissionUI");
            var runner = missionGo.AddComponent<MissionRunner>();
            runner.bridge = bridge;
            var hud = missionGo.AddComponent<MissionHud>();
            hud.bridge = bridge;
            hud.runner = runner;
        }

        // U5: always-on startup checklist (sandbox only at runtime; the pause menu can hide it). Idempotent.
        if (GameObject.Find("StartupChecklistUI") == null)
            new GameObject("StartupChecklistUI").AddComponent<StartupChecklistHud>();

        if (save)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveOpenScenes();
        }
        Debug.Log("[Phase2SceneBuilder] Phase-2 scene hierarchy built" + (save ? " and saved." : " (not saved)."));
        return scene;
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    /// <summary>The root-level ControlRoom object (the wrapper that carries the binder), not the FBX root inside it.</summary>
    public static GameObject FindRoot()
    {
        var scene = EditorSceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "ControlRoom") return root;
        return GameObject.Find("ControlRoom");
    }

    public static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // Bench slope (layout.json): front edge 0.76 m, 15 deg rise, 0.58 m deep, its front edge about 0.6 m ahead of the seat
    // anchor. The clipboard lies on the slope 0.85 m ahead and 0.55 m to the operator's right, top edge toward the board.
    private static void PlaceClipboard(Transform parent, string seatName, string objName, string label, string[] procedureIds)
    {
        var seat = FindDeep(parent, seatName);
        if (seat == null)
        {
            Debug.LogWarning("[Phase2SceneBuilder] " + seatName + " not found, no clipboard placed.");
            return;
        }
        // Toward the board: outward from the board centre through the seat (the same geometric rule OperatorController
        // and LayoutCheck use). Anchors keep Blender's local frame after the FBX export, so seat.forward points up;
        // their local up is the board-facing axis and serves as the fallback.
        var centre = FindDeep(parent, "STAND_Center");
        Vector3 fwd = centre != null
            ? Vector3.ProjectOnPlane(seat.position - centre.position, Vector3.up).normalized
            : Vector3.ProjectOnPlane(seat.up, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        const float ahead = 0.85f, side = 0.55f;
        float h = 0.76f + (ahead - 0.6f) / 0.58f * (0.915f - 0.76f) + 0.004f;
        var go = new GameObject(objName);
        go.transform.SetParent(parent, true);
        go.transform.position = new Vector3(seat.position.x, 0f, seat.position.z) + fwd * ahead + right * side + Vector3.up * h;
        go.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(-15f, 0f, 0f);
        var cb = go.AddComponent<ProcedureClipboard>();
        cb.procedureIds = procedureIds;
        cb.label = label;
    }

    public static Transform FindCtlRoot(Transform t)
    {
        for (; t != null; t = t.parent)
            if (t.name.StartsWith("CTL_")) return t;
        return null;
    }

    // ---- U6: reactor window and hall -------------------------------------------------------------------------
    // Room frame in Unity (FBX maps Blender (x,y,z) to Unity (x,z,y)): x west-east, z south-north, y up. The south wall is
    // a box whose room-side face is the interior. Wall_South and COL_Wall_South are hidden and replaced by framed pieces
    // (left, right, sill, header) around a window opening, plus a glass pane. The door is in the same wall at the middle
    // (door frame +-0.6 m), so the opening is offset 3.0 m toward the longer side of the wall, leaving 1.4 m of wall
    // between the door frame and the opening. Pieces are children of the ControlRoom root so LayoutCheck sees their
    // colliders; the hall itself is a separate root with no colliders at all.
    private const float WindowWidth = 3.2f, WindowSill = 0.35f, WindowHeight = 1.9f, WindowOffsetFromDoor = 2.6f;
    // The standing viewpoint (STAND_Center) and the seat sit 3.5 m apart along the wall, so a pool straight behind the
    // window looks well off to one side from the stand. The pool is moved toward the stand sightline (VesselShiftX, from the
    // window centre) and pulled close to the wall (PoolDepth) so one position reads reasonably centred from both.
    private const float VesselShiftX = 0.25f;
    private const int IgnoreRaycastLayer = 2;
    // Unnamed user layer for the reactor hall (culling only; no TagManager change). Hall lights light only this layer,
    // room lights skip it.
    public const int HallLayer = 9;


    private static void BuildReactorWindow(Transform crRoot)
    {
        Transform wallT = FindDeep(crRoot, "Wall_South");
        var wallR = wallT != null ? wallT.GetComponent<Renderer>() : null;
        if (wallR == null)
        {
            Debug.LogWarning("[Phase2SceneBuilder] Wall_South not found, no window or reactor hall built.");
            return;
        }
        Bounds wb = wallR.bounds;
        Transform ceil = FindDeep(crRoot, "Ceiling");
        var ceilR = ceil != null ? ceil.GetComponent<Renderer>() : null;
        float roomCentreZ = ceilR != null ? ceilR.bounds.center.z : wb.center.z + 1f;
        float inward = roomCentreZ >= wb.center.z ? 1f : -1f;   // +1: the room lies at larger z than the wall
        float zInner = inward > 0f ? wb.max.z : wb.min.z;       // room-side face of the wall
        float zOuter = inward > 0f ? wb.min.z : wb.max.z;       // hall-side face
        float zMid = 0.5f * (zInner + zOuter);
        float thick = Mathf.Max(0.05f, wb.size.z);
        float y0 = wb.min.y, yTop = wb.max.y;

        Transform door = FindDeep(crRoot, "Door_Leaf");
        var doorR = door != null ? door.GetComponent<Renderer>() : null;
        float doorX = doorR != null ? doorR.bounds.center.x : wb.center.x;
        float wx = doorX + (wb.max.x - doorX >= doorX - wb.min.x ? WindowOffsetFromDoor : -WindowOffsetFromDoor);
        float xl = wx - WindowWidth * 0.5f, xr = wx + WindowWidth * 0.5f;
        // keep the wider opening inside the wall, with a little masonry left at the corner
        if (xr > wb.max.x - 0.25f) { float d = xr - (wb.max.x - 0.25f); wx -= d; xl -= d; xr -= d; }
        if (xl < wb.min.x + 0.25f) { float d = (wb.min.x + 0.25f) - xl; wx += d; xl += d; xr += d; }
        if (Mathf.Abs(wx - doorX) - WindowWidth * 0.5f < 0.9f)
            Debug.LogWarning($"[Phase2SceneBuilder] the window edge is {Mathf.Abs(wx - doorX) - WindowWidth * 0.5f:0.00} m from the door centre (door frame is 0.6 m).");
        float ySill = y0 + WindowSill, yHead = ySill + WindowHeight;

        // The old wall box and its collision proxy go dark; they belong to the FBX instance so they are hidden, not destroyed.
        wallR.enabled = false;
        wallT.gameObject.SetActive(false);
        Transform colWall = FindDeep(crRoot, "COL_Wall_South");
        if (colWall != null) colWall.gameObject.SetActive(false);
        else Debug.LogWarning("[Phase2SceneBuilder] COL_Wall_South not found, the old wall collider may still block the window.");

        Material wallMat = wallR.sharedMaterial;
        Transform frameSrc = FindDeep(crRoot, "Door_FrameL");
        var frameR = frameSrc != null ? frameSrc.GetComponent<Renderer>() : null;
        Material frameMat = frameR != null ? frameR.sharedMaterial : MakeMaterial("M_WindowFrame", new Color(0.55f, 0.57f, 0.6f), false, false);

        var win = new GameObject("ReactorWindow");
        win.transform.SetParent(crRoot, true);

        // Wall pieces: visible, and solid for the CharacterController (Ignore Raycast, like every COL_ proxy).
        WindowBox(win.transform, "Window_WallLeft", wb.min.x, xl, y0, yTop, zMid, thick, wallMat, true);
        WindowBox(win.transform, "Window_WallRight", xr, wb.max.x, y0, yTop, zMid, thick, wallMat, true);
        WindowBox(win.transform, "Window_WallSill", xl, xr, y0, ySill, zMid, thick, wallMat, true);
        WindowBox(win.transform, "Window_WallHeader", xl, xr, yHead, yTop, zMid, thick, wallMat, true);
        // Steel frame proud of the room face (visual only; the wall pieces already block).
        const float ft = 0.08f, fz = 0.07f;
        float zFrame = zInner - inward * (fz * 0.5f - 0.02f);
        WindowBox(win.transform, "Window_FrameSill", xl - ft, xr + ft, ySill - ft * 0.5f, ySill + ft * 0.5f, zFrame, fz, frameMat, false);
        WindowBox(win.transform, "Window_FrameHead", xl - ft, xr + ft, yHead - ft * 0.5f, yHead + ft * 0.5f, zFrame, fz, frameMat, false);
        WindowBox(win.transform, "Window_FrameL", xl - ft, xl, ySill, yHead, zFrame, fz, frameMat, false);
        WindowBox(win.transform, "Window_FrameR", xr, xr + ft, ySill, yHead, zFrame, fz, frameMat, false);

        // Glass: translucent, solid for movement, never a click target (Ignore Raycast, so OnMouse rays pass to controls).
        var glass = WindowBox(win.transform, "Window_Glass", xl, xr, ySill, yHead, zMid, 0.04f,
                              MakeMaterial("M_WindowGlass", new Color(0.75f, 0.88f, 1f, 0.10f), true, false), true);
        glass.GetComponent<BoxCollider>().size = new Vector3(1f, 1f, 1.5f); // a little thicker than the pane so nothing slips past

        // Gallery rail on the hall side of the glass: a top rail and a mid rail with posts (visual only).
        float zRail = zOuter - inward * 0.35f;
        Material railMat = MakeMaterial("M_GalleryRail", new Color(0.62f, 0.65f, 0.7f), false, false);
        WindowBox(win.transform, "Rail_Top", xl, xr, y0 + 1.0f, y0 + 1.05f, zRail, 0.05f, railMat, false);
        WindowBox(win.transform, "Rail_Mid", xl, xr, y0 + 0.62f, y0 + 0.66f, zRail, 0.04f, railMat, false);
        int posts = Mathf.CeilToInt((xr - xl) / 0.8f);
        for (int i = 0; i <= posts; i++)
        {
            float px = Mathf.Lerp(xl + 0.03f, xr - 0.03f, (float)i / posts);
            WindowBox(win.transform, "Rail_Post_" + i, px - 0.025f, px + 0.025f, ySill, y0 + 1.05f, zRail, 0.05f, railMat, false);
        }

        // The hall is only seen through this opening, so the frustum gate tests the opening (padded a little).
        var gateBounds = new Bounds(new Vector3(wx, 0.5f * (ySill + yHead), zMid), new Vector3(WindowWidth + 0.6f, WindowHeight + 0.6f, 1.2f));
        HallBuilder.Build(wx + VesselShiftX, y0, zOuter, -inward, gateBounds, MakeMaterial);
        Debug.Log($"[Phase2SceneBuilder] reactor window built: opening x {xl:0.00}..{xr:0.00} (door x {doorX:0.00}), sill {WindowSill:0.00} m, "
                  + $"{WindowWidth:0.00} x {WindowHeight:0.00} m, wall z {zInner:0.00}/{zOuter:0.00}; hall built.");
    }

    // A box spanning x0..x1, y0..y1, centred on z with the given thickness. Solid pieces keep a collider on the Ignore Raycast
    // layer; visual pieces lose the collider the primitive carries.
    private static GameObject WindowBox(Transform parent, string name, float x0, float x1, float y0, float y1, float z, float thick,
                                        Material mat, bool solid)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.position = new Vector3(0.5f * (x0 + x1), 0.5f * (y0 + y1), z);
        go.transform.localScale = new Vector3(Mathf.Max(0.001f, x1 - x0), Mathf.Max(0.001f, y1 - y0), thick);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        if (solid) go.layer = IgnoreRaycastLayer;
        else Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    // Built-in pipeline Standard material (the clipboard falls back the same way). Transparent ones use the Fade setup.
    internal static Material MakeMaterial(string name, Color colour, bool fade, bool emissive)
    {
        Shader sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        var m = new Material(sh) { name = name };
        m.color = colour;
        if (fade && m.HasProperty("_Mode"))
        {
            m.SetFloat("_Mode", 2f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = emissive ? 3001 : 3000;
        }
        if (emissive && m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetColor("_EmissionColor", new Color(0.25f, 0.55f, 1f) * 0.4f);
        }
        return m;
    }

    // One emissive screen material shared by every CRT_* face (KTD4). Black albedo means room lights and shadows add
    // nothing; the emission carries the dark screen colour, so the phosphor text on top keeps its contrast in any light.
    private static Material screenMaterial;

    private static void ApplyScreenMaterial(MeshRenderer r)
    {
        if (r == null) return;
        r.sharedMaterial = ScreenMaterial();
    }

    private static Material ScreenMaterial()
    {
        if (screenMaterial == null)
        {
            screenMaterial = MakeMaterial("CRT_Screen", Color.black, false, false);
            if (screenMaterial.HasProperty("_Glossiness")) screenMaterial.SetFloat("_Glossiness", 0f);
            if (screenMaterial.HasProperty("_SpecularHighlights")) screenMaterial.SetFloat("_SpecularHighlights", 0f);
            if (screenMaterial.HasProperty("_GlossyReflections")) screenMaterial.SetFloat("_GlossyReflections", 0f);
            if (screenMaterial.HasProperty("_EmissionColor"))
            {
                screenMaterial.EnableKeyword("_EMISSION");
                screenMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                screenMaterial.SetColor("_EmissionColor", ThemeColors.SurfaceScreen);
            }
        }
        return screenMaterial;
    }

    // The old SeatCamera component is gone; scenes saved with it carry a missing-script component.
    // Blender point lights are authored in watts (350 W per ceiling fixture so the Blender render reads well). The FBX
    // carries that number into Unity's built-in pipeline as a unitless intensity of hundreds, which blows every lit
    // surface out to white. Ceiling fixtures get a fixed indoor-office level instead.
    public const float RoomLightIntensity = RoomLighting.KeyIntensity;
    public const float RoomLightRange = RoomLighting.KeyRange;

    // A monitor face that no CRT_ page owns (an out-of-service screen kit: its mesh keeps the exported screen material but
    // has no CRT_ name) would be an empty dark pane: it gets a renderer on the standby page instead, so every screen in
    // the room shows something deliberate. Counter windows ("Digit...") keep their own display.
    public const string StandbyScreenName = "STANDBY";
    private static void AttachStandbyScreens(GameObject root, PlantBridge bridge)
    {
        int n = 0;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            string nm = r.gameObject.name;
            if (nm.StartsWith("CRT_") || nm.StartsWith("Digit")) continue;
            if (r.GetComponentInParent<CrtScreenRenderer>() != null) continue;
            bool screen = false;
            foreach (var m in r.sharedMaterials)
                if (m != null && m.name.IndexOf("CrtScreen", System.StringComparison.OrdinalIgnoreCase) >= 0) { screen = true; break; }
            if (!screen) continue;
            var crt = r.gameObject.AddComponent<CrtScreenRenderer>();
            crt.bridge = bridge;
            crt.screenName = StandbyScreenName;
            n++;
        }
        Debug.Log("[Phase2SceneBuilder] " + n + " unassigned monitor faces set to the standby page.");
    }

    // Cool, dim ambient under the warm ceiling key (RoomLighting). The hall keeps its own layer-masked lights.
    // Any mesh still carrying the exported glowing screen material (a monitor face that is not a CRT_ page, a counter window)
    // gets the dark screen material, so no screen can render as a flat blown-out rectangle.
    private static void DarkenGlowingScreens(GameObject root)
    {
        int n = 0;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            bool glowing = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || mats[i].name.IndexOf("CrtScreen", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                mats[i] = ScreenMaterial();
                glowing = true;
            }
            if (!glowing) continue;
            r.sharedMaterials = mats;
            n++;
        }
        Debug.Log("[Phase2SceneBuilder] " + n + " screen faces set to the dark screen material.");
    }

    // Warm task lights over each console bench and value steps between console, wall, ceiling and floor (RoomLighting).
    private static void ApplyRoomLooks(GameObject root)
    {
        var bounds = new Bounds(root.transform.position, Vector3.zero);
        bool any = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        Vector3 centre = bounds.center;
        int tasks = 0, tinted = 0;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = r.gameObject.name;
            if (n.StartsWith("Console_"))
            {
                Vector3 towards = new Vector3(centre.x - r.bounds.center.x, 0f, centre.z - r.bounds.center.z).normalized;
                var go = new GameObject("TaskLight_" + n.Substring("Console_".Length));
                go.transform.SetParent(root.transform, false);
                go.transform.position = r.bounds.center + Vector3.up * (r.bounds.extents.y + RoomLighting.TaskHeightM)
                    + towards * RoomLighting.TaskTowardRoomM;
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = ThemeColors.ToColor(RoomLighting.TaskColor);
                l.intensity = RoomLighting.TaskIntensity;
                l.range = RoomLighting.TaskRange;
                l.shadows = LightShadows.None;
                l.cullingMask = ~(1 << HallLayer);
                tasks++;
                continue;
            }
            Rgba? tint = null;
            if (n.StartsWith("Wall_")) tint = RoomLighting.WallTint;
            else if (n == "Ceiling") tint = RoomLighting.CeilingTint;
            else if (n == "Floor") tint = RoomLighting.FloorTint;
            if (!tint.HasValue) continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || !mats[i].HasProperty("_Color")) continue;
                var copy = new Material(mats[i]) { name = mats[i].name + "_" + n };
                copy.color = copy.color * ThemeColors.ToColor(tint.Value);
                mats[i] = copy;
                tinted++;
            }
            r.sharedMaterials = mats;
        }
        Debug.Log("[Phase2SceneBuilder] " + tasks + " task lights, " + tinted + " room surfaces stepped.");
    }

    private static void ApplyRoomAmbient()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ThemeColors.ToColor(RoomLighting.AmbientSky);
        RenderSettings.ambientEquatorColor = ThemeColors.ToColor(RoomLighting.AmbientEquator);
        RenderSettings.ambientGroundColor = ThemeColors.ToColor(RoomLighting.AmbientGround);
        RenderSettings.ambientIntensity = RoomLighting.AmbientIntensity;
    }

    private static void NormaliseRoomLight(Light light)
    {
        Debug.Log($"[Phase2SceneBuilder] light {light.name}: imported {light.type} intensity={light.intensity:0.###} range={light.range:0.##} -> {RoomLightIntensity} / {RoomLightRange}");
        light.intensity = RoomLightIntensity;
        light.color = ThemeColors.ToColor(RoomLighting.KeyColor);
        if (light.type == LightType.Point || light.type == LightType.Spot) light.range = RoomLightRange;
        light.shadows = LightShadows.None;
        light.cullingMask = ~(1 << HallLayer);
    }

    private static void RemoveMissingScripts(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
    }

    // COL_* meshes: a BoxCollider sized to the mesh, no renderer, Ignore Raycast layer (blocks the CharacterController,
    // skipped by the OnMouse* ray so the shell never steals clicks).
    private static bool MakeCollisionProxy(Transform t)
    {
        var mf = t.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning("[Phase2SceneBuilder] " + t.name + " has no mesh to size a collider from.");
            return false;
        }
        var box = t.GetComponent<BoxCollider>();
        if (box == null) box = t.gameObject.AddComponent<BoxCollider>();
        box.center = mf.sharedMesh.bounds.center;
        box.size = mf.sharedMesh.bounds.size;
        box.isTrigger = false;
        var mr = t.GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;
        t.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        return true;
    }

    // Bounds of every mesh at or below `root`, in root's local space (exact for yawed and pitched panels).
    private static bool TryLocalBounds(Transform root, out Bounds bounds)
    {
        bool has = false;
        bounds = new Bounds();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || !mr.enabled) continue;
            Bounds mb = mf.sharedMesh.bounds;
            Vector3 c = mb.center, e = mb.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector3 p = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!has) { bounds = new Bounds(p, Vector3.zero); has = true; }
                else bounds.Encapsulate(p);
            }
        }
        return has;
    }

    // Sizes a BoxCollider on `t` to the meshes at or below it (an empty pivot has none of its own); never thinner than
    // minSide metres so a small control stays clickable.
    private static void FitCollider(Transform t, float minSide)
    {
        var box = t.GetComponent<BoxCollider>();
        if (box == null) box = t.gameObject.AddComponent<BoxCollider>();
        Bounds b;
        float s = Mathf.Max(1e-4f, (Mathf.Abs(t.lossyScale.x) + Mathf.Abs(t.lossyScale.y) + Mathf.Abs(t.lossyScale.z)) / 3f);
        float min = minSide / s;
        if (!TryLocalBounds(t, out b))
        {
            box.center = Vector3.zero;
            box.size = new Vector3(min, min, min);
        }
        else
        {
            box.center = b.center;
            box.size = new Vector3(Mathf.Max(b.size.x, min), Mathf.Max(b.size.y, min), Mathf.Max(b.size.z, min));
        }
        box.isTrigger = false;
    }
}
