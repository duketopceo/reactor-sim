using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ReactorSim;

// Builds the reactor hall behind the control-room window (U6): a circular hall with a deep pool, the integral SMR module (see
// SmrModuleMesh) standing in it, tinted water as stacked translucent layers, a polar crane, cable trays and hall lights.
// Everything is collider-free original geometry on HallLayer under one "ReactorHall" root, plus a separate frustum gate object
// (HallVisibility) that switches the root off when the window is out of view. The room is 29 m above the pool floor, so the
// window looks down about 9 m onto the water.
//
// Local hall space: origin on the pool axis at the pool floor, y up, the control-room window towards -z. The root is placed and
// turned so that local -z points at the room. All numbers come from HallDimensions.
public static class HallBuilder
{
    public const string RootName = "ReactorHall";
    public const string GateName = "HallVisibilityGate";

    /// <summary>Builds the hall. cx and zOuter are the window-wall position; dir is +1 when the hall lies at larger z than the wall.</summary>
    public static GameObject Build(float cx, float roomFloorY, float zOuter, float dir, Bounds windowBounds, System.Func<string, Color, bool, bool, Material> makeMaterial)
    {
        var bad = HallDimensions.Validate();
        if (bad.Count > 0) Debug.LogError("[HallBuilder] FAIL hall dimensions invalid: " + string.Join("; ", bad));

        float D = HallDimensions.PoolAxisFromWall, Rh = HallDimensions.HallRadius, Rp = HallDimensions.PoolRadius;
        float water = HallDimensions.WaterDepth, deckY = water + HallDimensions.DeckAboveWater;
        float roomFloorLocal = HallDimensions.RoomFloorAbovePoolFloor, ceilY = roomFloorLocal + 6f;

        var root = new GameObject(RootName);
        root.transform.position = new Vector3(cx, roomFloorY - roomFloorLocal, zOuter + dir * D);
        root.transform.rotation = dir > 0f ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
        Transform T = root.transform;

        // ---- materials ----
        Material concrete = makeMaterial("M_HallConcrete", new Color(0.38f, 0.40f, 0.43f), false, false);
        Material deck = makeMaterial("M_HallDeck", new Color(0.26f, 0.28f, 0.30f), false, false);
        Material tile = makeMaterial("M_PoolTile", new Color(0.55f, 0.66f, 0.72f), false, false);
        Material steel = makeMaterial("M_HallSteel", new Color(0.58f, 0.62f, 0.68f), false, false);
        Material dark = makeMaterial("M_HallSteelDark", new Color(0.30f, 0.32f, 0.36f), false, false);
        Material paint = makeMaterial("M_ContainmentPaint", new Color(0.72f, 0.76f, 0.80f), false, false);
        Material skin = makeMaterial("M_ContainmentSkin", new Color(0.62f, 0.78f, 0.88f, 0.22f), true, false);
        foreach (var m in new[] { steel, dark, paint })
        {
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.7f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.5f);
        }
        // Water: one tint colour for every layer, so the order the layers blend in does not matter, and an emissive baseline so
        // the pool reads blue at zero power and under any lighting.
        Material waterVolume = makeMaterial("M_PoolVolume", new Color(0.03f, 0.26f, 0.44f, 0.04f), true, false);
        Material waterSurface = makeMaterial("M_PoolSurface", new Color(0.10f, 0.45f, 0.72f, 0.10f), true, false);
        waterSurface.EnableKeyword("_EMISSION");
        waterSurface.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        if (waterSurface.HasProperty("_EmissionColor")) waterSurface.SetColor("_EmissionColor", new Color(0.01f, 0.06f, 0.14f));

        // ---- shell: hall wall, deck, ceiling, floor, pool wall ----
        var ang = Angles(D, Rh);
        var hallEdge = new List<Vector2>();
        var poolEdge = new List<Vector2>();
        foreach (float a in ang)
        {
            float r = ClipRadius(a, D, Rh);
            hallEdge.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r));
            poolEdge.Add(new Vector2(Mathf.Cos(a) * Rp, Mathf.Sin(a) * Rp));
        }
        // wall: curved part full height; the straight stretch on the control-room wall plane only up to the room floor (above
        // it the room's own wall is there)
        var arcWall = new MeshBuilder();
        var chordWall = new MeshBuilder();
        for (int i = 0; i + 1 < hallEdge.Count; i++)
        {
            float midAngle = 0.5f * (ang[i] + ang[i + 1]);
            bool chord = ClipRadius(midAngle, D, Rh) < Rh - 0.001f;
            var seg = new List<Vector2> { hallEdge[i], hallEdge[i + 1] };
            if (chord) chordWall.AddWallStrip(seg, 0f, roomFloorLocal, true);
            else arcWall.AddWallStrip(seg, 0f, ceilY, true);
        }
        arcWall.Attach(T, "Hall_Wall", concrete);
        chordWall.Attach(T, "Hall_NearWall", concrete);

        var deckMesh = new MeshBuilder();
        deckMesh.AddBand(poolEdge, hallEdge, deckY, true);
        deckMesh.Attach(T, "Hall_Deck", deck);
        var ceiling = new MeshBuilder();
        ceiling.AddFan(Vector2.zero, hallEdge, ceilY, false);
        ceiling.Attach(T, "Hall_Ceiling", concrete);

        var poolMesh = new MeshBuilder();
        poolMesh.AddArcShell(Rp, 0f, deckY, 0f, 2f * Mathf.PI, 72, true);
        var poolCircle = new List<Vector2>();
        for (int i = 0; i <= 48; i++) { float a = 2f * Mathf.PI * i / 48; poolCircle.Add(new Vector2(Mathf.Cos(a) * Rp, Mathf.Sin(a) * Rp)); }
        poolMesh.AddFan(Vector2.zero, poolCircle, 0f, true);
        poolMesh.Attach(T, "Pool_Basin", tile);

        // ---- water: surface plus stacked tint layers (one mesh, one draw) ----
        var wSurface = new MeshBuilder();
        wSurface.AddFan(Vector2.zero, poolCircle, water, true);
        wSurface.Attach(T, "Pool_Surface", waterSurface);
        var wVolume = new MeshBuilder();
        const int layers = 10;
        for (int k = 1; k <= layers; k++) wVolume.AddFan(Vector2.zero, ScaleAll(poolCircle, 0.995f), water - k * (water / (layers + 1f)), true);
        wVolume.Attach(T, "Pool_Volume", waterVolume);

        // ---- the module, crane, trays ----
        SmrModuleMesh.Build(T, new SmrModuleMesh.Materials { vessel = steel, dark = dark, containment = paint, skin = skin });
        PolarCrane.Build(T, steel, dark, roomFloorLocal - 2.4f, Rh, D);
        CableTray.Build(T, dark, Rh - 0.45f, deckY + 3.6f, 0.15f * Mathf.PI, 0.85f * Mathf.PI);

        // ---- spent-fuel racks on a ledge on the far wall; their glow follows plant power (U7) ----
        Material rackCells = makeMaterial("M_RackCells", new Color(0.05f, 0.10f, 0.18f), false, true);
        rackCells.enableInstancing = true;
        Shader additive = Shader.Find("Legacy Shaders/Particles/Additive");
        if (additive == null) additive = Shader.Find("Sprites/Default");
        var rackHaze = new Material(additive) { name = "M_RackHaze" };
        if (rackHaze.HasProperty("_TintColor")) rackHaze.SetColor("_TintColor", new Color(0.30f, 0.60f, 1f, (float)ReactorSim.Instruments.GlowMapping.HazeFloor));
        float ledge = HallDimensions.RackLedgeHeight;
        FuelRackMesh.BuildLedge(T, tile, Rp, ledge, 0.5f * Mathf.PI - 1.25f, 0.5f * Mathf.PI + 1.25f, 3.4f);
        var rackMats = new FuelRackMesh.Materials { cells = rackCells, frame = dark, haze = rackHaze };
        for (int i = 0; i < HallDimensions.RackArrays; i++)
        {
            float phi = 0.5f * Mathf.PI + (i - 0.5f * (HallDimensions.RackArrays - 1)) * 0.70f;
            float rr = Rp - 1.8f;
            FuelRackMesh.Build(T, i, new Vector3(Mathf.Cos(phi) * rr, ledge, Mathf.Sin(phi) * rr),
                               Mathf.Atan2(-Mathf.Cos(phi), -Mathf.Sin(phi)) * Mathf.Rad2Deg, rackMats);
        }
        var rackFocus = new GameObject("RackFocus");
        rackFocus.transform.SetParent(T, false);
        rackFocus.transform.localPosition = new Vector3(0f, ledge + 2.2f, Rp - 1.8f);

        // gallery focus: where the capture poses look (mid-depth of the visible water, over the module)
        var focus = new GameObject("GalleryFocus");
        focus.transform.SetParent(T, false);
        focus.transform.localPosition = new Vector3(0f, water - 3f, 0f);

        // ---- lights: three cool point lights over the pool, hall layer only ----
        Vector3[] lightAt = { new Vector3(-4f, deckY + 4f, -2f), new Vector3(4f, deckY + 4f, -2f), new Vector3(0f, deckY + 4f, 5f) };
        for (int i = 0; i < lightAt.Length; i++)
        {
            var lg = new GameObject("HallLight_" + i);
            lg.transform.SetParent(T, false);
            lg.transform.localPosition = lightAt[i];
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.72f, 0.84f, 1f);
            l.range = 24f;
            l.intensity = 1.5f;
            l.shadows = LightShadows.None;
        }

        FinishHall(root, windowBounds);
        return root;
    }

    /// <summary>Layer, static flags, culling masks, binder and the frustum gate. Run after every piece is parented under the root.</summary>
    public static void FinishHall(GameObject root, Bounds windowBounds)
    {
        foreach (var tr in root.GetComponentsInChildren<Transform>(true))
        {
            tr.gameObject.layer = ControlRoomSceneBuilder.HallLayer;
            if (tr.GetComponent<MeshRenderer>() != null)
                GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.BatchingStatic);
        }
        foreach (var l in root.GetComponentsInChildren<Light>(true)) l.cullingMask = 1 << ControlRoomSceneBuilder.HallLayer;

        var binder = root.AddComponent<PlantEquipmentBinder>();
        var bridgeGo = GameObject.Find("PlantSim");
        if (bridgeGo != null) binder.bridge = bridgeGo.GetComponent<PlantBridge>();

        var gate = new GameObject(GateName);
        var vis = gate.AddComponent<HallVisibility>();
        vis.hallRoot = root;
        vis.windowBounds = windowBounds;
    }

    // ---- boundary helpers ----

    // Radius of the hall edge in direction a: the hall circle, cut by the control-room wall plane at z = -D.
    private static float ClipRadius(float a, float D, float Rh)
    {
        float s = Mathf.Sin(a);
        if (s >= 0f) return Rh;
        return Mathf.Min(Rh, D / -s);
    }

    // Sample angles for one closed sweep from the first point where the hall circle leaves the control-room wall plane, round
    // the circle, to the same point again. The second kink (where the circle meets the plane again) is always a sample.
    private static List<float> Angles(float D, float Rh)
    {
        float k = Mathf.Asin(Mathf.Clamp01(D / Rh));
        var list = new List<float>();
        const int n = 120;
        for (int i = 0; i <= n; i++) list.Add(-k + 2f * Mathf.PI * i / n);
        list.Add(Mathf.PI + k);
        list.Sort();
        return list;
    }

    private static List<Vector2> ScaleAll(List<Vector2> pts, float s)
    {
        var r = new List<Vector2>(pts.Count);
        foreach (var p in pts) r.Add(p * s);
        return r;
    }

    /// <summary>Triangles, mesh renderers and real-time lights under a hall root (for the checks).</summary>
    public static void Stats(GameObject root, out int triangles, out int renderers, out int lights)
    {
        triangles = 0; renderers = 0; lights = 0;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) { triangles += (int)(mf.sharedMesh.GetIndexCount(0) / 3); renderers++; }
        lights = root.GetComponentsInChildren<Light>(true).Length;
    }
}
