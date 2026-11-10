using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ReactorSim;

// Batchmode check for the reactor hall (U6/U7), read from the BUILT objects, not from the constants that built them:
// the module measures what the plan promises, the hall has no colliders and sits on the hall layer, a rebuild twice gives one
// hall with the same object and triangle counts, the triangle and light budgets hold, and the binder, gate and racks are wired.
// Prints "[HallCheck] OK/FAIL ..." lines and exits 0/1.
//   "<Unity.exe>" -batchmode -nographics -projectPath ReactorSim.Unity -executeMethod HallCheck.Run -quit -logFile out/unity-HallCheck.log
public static class HallCheck
{
    public static void Run()
    {
        int failures = 0;
        try
        {
            ControlRoomSceneBuilder.Build(false);
            var first = Measure();
            failures += CheckModule();
            failures += CheckHallRules();
            failures += CheckBudget(first);
            failures += CheckRacks();
            failures += CheckWindowAndCrane();

            ControlRoomSceneBuilder.Build(false);
            var second = Measure();
            failures += Report(CountRoots(HallBuilder.RootName) == 1 && CountRoots(HallBuilder.GateName) == 1, "rebuilding twice leaves one hall root and one gate");
            failures += Report(first.transforms == second.transforms && first.triangles == second.triangles && first.renderers == second.renderers,
                $"rebuilding twice gives identical counts (transforms {first.transforms}/{second.transforms}, tris {first.triangles}/{second.triangles}, renderers {first.renderers}/{second.renderers})");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[HallCheck] FAIL exception: " + ex);
            failures++;
        }
        Debug.Log("[HallCheck] " + (failures == 0 ? "PASS" : "FAILED") + ", " + failures + " failures");
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private struct Counts { public int transforms, triangles, renderers, lights; }

    private static Counts Measure()
    {
        var c = new Counts();
        var hall = GameObject.Find(HallBuilder.RootName);
        if (hall == null) return c;
        c.transforms = hall.GetComponentsInChildren<Transform>(true).Length;
        HallBuilder.Stats(hall, out c.triangles, out c.renderers, out c.lights);
        return c;
    }

    private static int CountRoots(string name)
    {
        int n = 0;
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) if (go.name == name) n++;
        return n;
    }

    private static int Report(bool ok, string what)
    {
        Debug.Log("[HallCheck] " + (ok ? "OK   " : "FAIL") + " " + what);
        return ok ? 0 : 1;
    }

    private static Renderer Find(GameObject hall, string name)
    {
        foreach (var r in hall.GetComponentsInChildren<Renderer>(true)) if (r.name == name) return r;
        return null;
    }

    private static int CheckModule()
    {
        var hall = GameObject.Find(HallBuilder.RootName);
        if (hall == null) return Report(false, "a ReactorHall root exists");
        int f = 0;
        // the LOD1 meshes are the bare vessel and the bare containment, so their bounds are the real body sizes
        var vessel = Find(hall, "Smr_Vessel_LOD1");
        var cont = Find(hall, "Smr_Containment_LOD1");
        f += Report(vessel != null && cont != null, "module vessel and containment meshes exist");
        if (vessel == null || cont == null) return f + 1;
        Vector3 vs = vessel.bounds.size, cs = cont.bounds.size;
        f += Report(Mathf.Abs(vs.x - 2.7f) < 0.1f && Mathf.Abs(vs.z - 2.7f) < 0.1f, $"vessel is about 2.7 m across (measured {vs.x:0.00} x {vs.z:0.00})");
        f += Report(vs.y >= 18.0f && vs.y <= 20.0f, $"vessel is 18 to 20 m tall (measured {vs.y:0.00})");
        f += Report(Mathf.Abs(cs.x - 4.5f) < 0.1f, $"containment is about 4.5 m across (measured {cs.x:0.00})");
        f += Report(cont.bounds.max.y > vessel.bounds.max.y && cont.bounds.min.y <= vessel.bounds.min.y, "containment wraps the vessel from base to head");
        var water = Find(hall, "Pool_Surface");
        f += Report(water != null && cont.bounds.max.y < water.bounds.max.y, "the containment head is under the water surface");
        f += Report(Find(hall, "Smr_Pumps") != null && Find(hall, "Smr_Pipes") != null && Find(hall, "Smr_Skin") != null,
            "pump housings, pipes and the translucent containment skin exist");
        f += Report(Find(hall, "Crane_Bridge") != null && Find(hall, "CableTray") != null, "polar crane and cable tray exist");
        return f;
    }

    private static int CheckHallRules()
    {
        var hall = GameObject.Find(HallBuilder.RootName);
        if (hall == null) return 1;
        int colliders = hall.GetComponentsInChildren<Collider>(true).Length;
        int wrongLayer = 0;
        foreach (var t in hall.GetComponentsInChildren<Transform>(true)) if (t.gameObject.layer != ControlRoomSceneBuilder.HallLayer) wrongLayer++;
        int f = Report(colliders == 0, $"the hall has no colliders (found {colliders})");
        f += Report(wrongLayer == 0, $"every hall object is on the hall layer (off-layer: {wrongLayer})");
        f += Report(hall.GetComponent<PlantEquipmentBinder>() != null, "the equipment binder is on the hall root");
        var gate = GameObject.Find(HallBuilder.GateName);
        var vis = gate != null ? gate.GetComponent<HallVisibility>() : null;
        f += Report(vis != null && vis.hallRoot == hall && vis.windowBounds.size.x > 1f, "the frustum gate points at the hall root and a window box");
        return f;
    }

    private static int CheckBudget(Counts c)
    {
        int f = Report(c.triangles <= HallDimensions.MaxHallTriangles, $"hall triangles {c.triangles} <= {HallDimensions.MaxHallTriangles}");
        f += Report(c.lights <= HallDimensions.MaxRealtimeLights, $"hall lights {c.lights} <= {HallDimensions.MaxRealtimeLights}");
        Debug.Log($"[HallCheck] hall totals: {c.triangles} tris across {c.renderers} mesh renderers, {c.lights} lights");
        return f;
    }

    // Racks (U7): the arrays, their lights and haze cones exist, the light budget holds with them, the cells share one material.
    private static int CheckRacks()
    {
        var hall = GameObject.Find(HallBuilder.RootName);
        if (hall == null) return 1;
        int cells = 0, lights = 0, haze = 0;
        foreach (var t in hall.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("RackGlow_Cells_")) cells++;
            else if (t.name.StartsWith("RackGlow_Light_")) lights++;
            else if (t.name.StartsWith("RackGlow_Haze_")) haze++;
        }
        int f = Report(cells == HallDimensions.RackArrays, $"{HallDimensions.RackArrays} rack arrays exist (found {cells})");
        f += Report(lights == HallDimensions.RackLights && haze == HallDimensions.RackArrays,
            $"the racks share {HallDimensions.RackLights} real light (found {lights}) and each has a haze cone (found {haze}); the glow cue is emissive first");
        Material shared = null;
        bool oneMaterial = true;
        foreach (var r in hall.GetComponentsInChildren<Renderer>(true))
            if (r.name.StartsWith("RackGlow_Cells_")) { if (shared == null) shared = r.sharedMaterial; else if (shared != r.sharedMaterial) oneMaterial = false; }
        f += Report(shared != null && oneMaterial, "every rack array shares one emissive material");
        return f;
    }

    // PR4: the window the stand and seat look through is as wide and as low as the sight-line maths assumes, and the parked crane
    // (read from the built meshes, in hall space) crosses no sight line from either viewpoint to the module.
    private static int CheckWindowAndCrane()
    {
        var hall = GameObject.Find(HallBuilder.RootName);
        var glassGo = GameObject.Find("Window_Glass");
        if (hall == null || glassGo == null) return Report(false, "the hall and the window glass exist for the sight-line checks");
        Bounds g = glassGo.GetComponent<Renderer>().bounds;
        float roomFloorWorld = hall.transform.position.y + HallDimensions.RoomFloorAbovePoolFloor;
        float sill = g.min.y - roomFloorWorld, width = Mathf.Max(g.size.x, g.size.z);
        int f = Report(Mathf.Abs(sill - HallDimensions.WindowSill) < 0.02f, $"the window sill is {sill:0.00} m (expected {HallDimensions.WindowSill:0.00})");
        f += Report(width >= HallDimensions.WindowWidth - 0.05f, $"the window is {width:0.00} m wide (expected {HallDimensions.WindowWidth:0.00})");
        f += Report(HallSightline.CheckWindowReach().Count == 0, "the stand sees the module head and the seat sees the water over it through the window");

        var lo = new double[] { double.MaxValue, double.MaxValue, double.MaxValue };
        var hi = new double[] { double.MinValue, double.MinValue, double.MinValue };
        int parts = 0;
        foreach (var mf in hall.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.name.StartsWith("Crane_") || mf.name == "Crane_RailRing" || mf.sharedMesh == null) continue;
            Bounds b = mf.sharedMesh.bounds;   // the crane meshes are built in hall space under a root with identity local transform
            parts++;
            lo[0] = System.Math.Min(lo[0], b.min.x); lo[1] = System.Math.Min(lo[1], b.min.y); lo[2] = System.Math.Min(lo[2], b.min.z);
            hi[0] = System.Math.Max(hi[0], b.max.x); hi[1] = System.Math.Max(hi[1], b.max.y); hi[2] = System.Math.Max(hi[2], b.max.z);
        }
        f += Report(parts >= 2, $"the crane bridge and trolley meshes exist (found {parts})");
        if (parts >= 2)
        {
            var box = new[] { lo, hi };
            f += Report(HallSightline.CraneClearOfModule(HallSightline.Stand(), box) && HallSightline.CraneClearOfModule(HallSightline.Seat(), box),
                $"the built crane (z {lo[2]:0.0}..{hi[2]:0.0}, y {lo[1]:0.0}..{hi[1]:0.0}) crosses no sight line to the module from the stand or the seat");
        }
        return f;
    }
}
