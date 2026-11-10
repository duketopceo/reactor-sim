using System.Collections.Generic;
using UnityEngine;
using ReactorSim;

// Procedural integral small modular reactor module standing in the pool (replaces the old 1 m vessel). Original geometry from
// formulas; scale comes from HallDimensions. Local space: origin on the module axis at the pool floor, y up, the control-room
// window lies towards -z.
//
// A pumped integral PWR variant (KTD7): the steam generators and pressurizer are inside the vessel, so there is no external
// primary loop. What shows outside the vessel is what an integral design really has: canned recirculation pump motors on the
// shoulder, chemical-and-volume-control, safety-injection and vent penetrations through the containment wall, and the steam and
// feedwater lines that leave through the containment head and run to a pool-wall penetration. The containment is drawn as a
// cutaway (KTD8): an opaque shell with a vertical slot facing the window, closed in its upper part by a translucent skin, so the
// vessel and pump housings can be seen. That is a depiction choice, not a claim about the real structure.
public static class SmrModuleMesh
{
    public sealed class Materials
    {
        public Material vessel, dark, containment, skin;
    }

    /// <summary>Centre angle of the cutaway slot (towards the window, -z) and its half width, radians.</summary>
    public static readonly float SlotCentre = -0.5f * Mathf.PI;
    public const float SlotHalfWidthDeg = 52f;

    public static GameObject Build(Transform parent, Materials m)
    {
        var root = new GameObject("SmrModule");
        root.transform.SetParent(parent, false);
        var lod0 = new GameObject("LOD0");
        lod0.transform.SetParent(root.transform, false);
        var lod1 = new GameObject("LOD1");
        lod1.transform.SetParent(root.transform, false);

        float R = HallDimensions.VesselDiameter * 0.5f, C = HallDimensions.ContainmentDiameter * 0.5f;
        float vb = HallDimensions.VesselBase, vt = HallDimensions.VesselTop, cb = HallDimensions.ContainmentBase, ct = HallDimensions.ContainmentTop;
        float slotA0 = SlotCentre - SlotHalfWidthDeg * Mathf.Deg2Rad, slotA1 = SlotCentre + SlotHalfWidthDeg * Mathf.Deg2Rad;

        // ---- vessel ----
        var vessel = new MeshBuilder();
        AddVessel(vessel, R, vb, vt, 32);
        vessel.AddTube(new Vector3(0, vt - 2.4f, 0), new Vector3(0, vt - 2.1f, 0), R + 0.12f, 36, true);   // closure flange
        for (int i = 0; i < 24; i++)
        {
            float a = 2f * Mathf.PI * i / 24;
            var p = new Vector3(Mathf.Cos(a) * (R + 0.07f), vt - 2.1f, Mathf.Sin(a) * (R + 0.07f));
            vessel.AddTube(p, p + Vector3.up * 0.16f, 0.035f, 6, true);                                    // stud
        }
        vessel.AddTube(new Vector3(0, 0f, 0), new Vector3(0, vb + 0.2f, 0), 1.15f, 24, false);             // support skirt
        vessel.AddTube(new Vector3(0, 0f, 0), new Vector3(0, 0.12f, 0), 1.6f, 24, true);                   // skirt base plate
        vessel.Attach(lod0.transform, "Smr_Vessel", m.vessel);
        int trisVessel = vessel.TriangleCount;

        // ---- canned recirculation pump motors on the vessel shoulder (visible through the slot) ----
        var pumps = new MeshBuilder();
        for (int i = 0; i < HallDimensions.PumpHousingCount; i++)
        {
            float a = SlotCentre + Mathf.Deg2Rad * (-62f + 41.3f * i);
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 foot = d * (R + 0.34f) + Vector3.up * (vt - 5.6f);
            pumps.AddTube(foot, foot + Vector3.up * 2.6f, 0.34f, 16, true);                                 // motor housing
            pumps.AddTube(foot + Vector3.up * 2.6f, foot + Vector3.up * 3.0f, 0.22f, 12, true);             // terminal cap
            pumps.AddTube(foot + Vector3.up * 0.9f, foot + Vector3.up * 1.0f, 0.40f, 16, true);             // cooling-jacket ring
            pumps.AddTube(d * (R - 0.05f) + Vector3.up * (vt - 5.2f), d * (R + 0.34f) + Vector3.up * (vt - 5.2f), 0.18f, 10, true);   // suction stub
        }
        pumps.Attach(lod0.transform, "Smr_Pumps", m.dark);

        // ---- penetrations through the containment wall: CVCS in/out, safety injection, vent ----
        var pipes = new MeshBuilder();
        AddPenetration(pipes, R, C, 30f, ct - 6.8f, 0.12f);     // chemical and volume control, letdown
        AddPenetration(pipes, R, C, 150f, ct - 6.8f, 0.10f);    // chemical and volume control, makeup
        AddPenetration(pipes, R, C, 95f, ct - 11.5f, 0.15f);    // safety injection
        AddPenetration(pipes, R, C, 205f, ct - 11.5f, 0.15f);   // safety injection
        AddPenetration(pipes, R, C, 330f, ct - 2.2f, 0.08f);    // reactor vent
        // ---- steam and feedwater lines: out of the containment head, up, then sideways to the pool wall ----
        float run = HallDimensions.PipeRunHeight, wall = HallDimensions.PoolRadius;
        AddLine(pipes, new Vector3(0.85f, ct - 0.2f, -0.55f), run, wall, 0.24f);
        AddLine(pipes, new Vector3(0.85f, ct - 0.2f, 0.55f), run, wall, 0.17f);
        // lifting lugs on the containment head
        for (int i = 0; i < 3; i++)
        {
            float a = 2f * Mathf.PI * (i + 0.5f) / 3f;
            var p = new Vector3(Mathf.Cos(a) * 1.5f, ct - 0.45f, Mathf.Sin(a) * 1.5f);
            pipes.AddBox(p + new Vector3(-0.12f, 0f, -0.12f), p + new Vector3(0.12f, 0.45f, 0.12f));
        }
        pipes.Attach(lod0.transform, "Smr_Pipes", m.dark);

        // ---- containment: opaque shell with a cutaway slot, translucent upper skin over the slot, closed domed head ----
        var shell = new MeshBuilder();
        shell.AddArcShell(C, cb, ct - 0.9f, slotA1, slotA0 + 2f * Mathf.PI, 40, false);
        for (int i = 0; i < 6; i++)                                         // weld bands on the opaque arc
        {
            float y = Mathf.Lerp(cb + 1.5f, ct - 2f, i / 5f);
            shell.AddArcShell(C + 0.03f, y, y + 0.14f, slotA1, slotA0 + 2f * Mathf.PI, 40, false);
        }
        shell.AddArcShell(C - 0.01f, cb, ct - 0.9f, slotA1, slotA0 + 2f * Mathf.PI, 40, true);   // inner face, visible through the slot
        AddHead(shell, C, ct - 0.9f, 0.9f, 40);
        shell.AddTube(new Vector3(0, cb - 0.1f, 0), new Vector3(0, cb + 0.05f, 0), C + 0.1f, 40, true);   // base plate
        shell.Attach(lod0.transform, "Smr_Containment", m.containment);
        // slot edge frame: two vertical angle bars
        var edges = new MeshBuilder();
        edges.AddTube(new Vector3(Mathf.Cos(slotA0) * (C + 0.02f), cb, Mathf.Sin(slotA0) * (C + 0.02f)),
                      new Vector3(Mathf.Cos(slotA0) * (C + 0.02f), ct - 0.9f, Mathf.Sin(slotA0) * (C + 0.02f)), 0.07f, 8, true);
        edges.AddTube(new Vector3(Mathf.Cos(slotA1) * (C + 0.02f), cb, Mathf.Sin(slotA1) * (C + 0.02f)),
                      new Vector3(Mathf.Cos(slotA1) * (C + 0.02f), ct - 0.9f, Mathf.Sin(slotA1) * (C + 0.02f)), 0.07f, 8, true);
        edges.Attach(lod0.transform, "Smr_SlotFrame", m.dark);

        var skin = new MeshBuilder();
        float skinBottom = Mathf.Lerp(cb, ct - 0.9f, 0.55f);
        skin.AddArcShell(C + 0.01f, skinBottom, ct - 0.9f, slotA0, slotA1, 14, false);
        skin.AddArcShell(C + 0.01f, skinBottom, ct - 0.9f, slotA0, slotA1, 14, true);
        skin.Attach(lod0.transform, "Smr_Skin", m.skin);

        int trisAll = vessel.TriangleCount + pumps.TriangleCount + pipes.TriangleCount + shell.TriangleCount + edges.TriangleCount + skin.TriangleCount;

        // ---- LOD1: coarse vessel, containment and steam lines (distant view) ----
        var c1 = new MeshBuilder();
        AddVessel(c1, R, vb, vt, 12);
        c1.Attach(lod1.transform, "Smr_Vessel_LOD1", m.vessel);
        var c2 = new MeshBuilder();
        c2.AddArcShell(C, cb, ct - 0.9f, slotA1, slotA0 + 2f * Mathf.PI, 14, false);
        AddHead(c2, C, ct - 0.9f, 0.9f, 14);
        c2.Attach(lod1.transform, "Smr_Containment_LOD1", m.containment);
        var c3 = new MeshBuilder();
        c3.AddTube(new Vector3(0.85f, ct - 0.2f, -0.55f), new Vector3(0.85f, run, -0.55f), 0.24f, 6, true);
        c3.AddTube(new Vector3(0.85f, run, -0.55f), new Vector3(wall, run, -0.55f), 0.24f, 6, true);
        c3.AddTube(new Vector3(0.85f, ct - 0.2f, 0.55f), new Vector3(0.85f, run, 0.55f), 0.17f, 6, true);
        c3.AddTube(new Vector3(0.85f, run, 0.55f), new Vector3(wall, run, 0.55f), 0.17f, 6, true);
        c3.Attach(lod1.transform, "Smr_Pipes_LOD1", m.dark);

        var group = root.AddComponent<LODGroup>();
        group.SetLODs(new[]
        {
            new LOD(0.20f, lod0.GetComponentsInChildren<Renderer>()),
            new LOD(0.015f, lod1.GetComponentsInChildren<Renderer>()),
        });
        group.RecalculateBounds();

        Debug.Log($"[HallBuilder] SMR module: vessel {vessel.TriangleCount} tris (+{pumps.TriangleCount} pumps, {pipes.TriangleCount} pipes), "
                  + $"containment {shell.TriangleCount + edges.TriangleCount + skin.TriangleCount} tris, LOD0 total {trisAll}, "
                  + $"LOD1 {c1.TriangleCount + c2.TriangleCount + c3.TriangleCount}");
        return root;
    }

    // Vessel body as a lathe: dished lower head, straight shell, domed upper head.
    private static void AddVessel(MeshBuilder b, float R, float vb, float vt, int seg)
    {
        const int cap = 6;
        const float lowerH = 0.8f, upperH = 1.0f;
        var r = new List<float>();
        var y = new List<float>();
        for (int i = 0; i <= cap; i++)
        {
            float phi = 0.5f * Mathf.PI * i / cap;
            r.Add(R * Mathf.Sin(phi));
            y.Add(vb + lowerH * (1f - Mathf.Cos(phi)));
        }
        float yTopCyl = vt - upperH;
        float yMid = 0.5f * (vb + lowerH + yTopCyl);
        r.Add(R); y.Add(yMid);
        for (int i = 0; i <= cap; i++)
        {
            float phi = 0.5f * Mathf.PI * i / cap;
            r.Add(R * Mathf.Cos(phi));
            y.Add(yTopCyl + upperH * Mathf.Sin(phi));
        }
        b.AddLathe(r.ToArray(), y.ToArray(), seg);
    }

    // Domed containment head from y0 up by height.
    private static void AddHead(MeshBuilder b, float C, float y0, float height, int seg)
    {
        const int rings = 6;
        var r = new float[rings + 1];
        var y = new float[rings + 1];
        for (int i = 0; i <= rings; i++)
        {
            float phi = 0.5f * Mathf.PI * i / rings;
            r[i] = C * Mathf.Cos(phi);
            y[i] = y0 + height * Mathf.Sin(phi);
        }
        b.AddLathe(r, y, seg);
    }

    // A radial penetration: pipe from the vessel wall out through the containment wall, with a flange and a valve body outside.
    private static void AddPenetration(MeshBuilder b, float R, float C, float azimuthDeg, float y, float radius)
    {
        float a = azimuthDeg * Mathf.Deg2Rad;
        var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        Vector3 inner = d * (R - 0.05f) + Vector3.up * y, wallPt = d * C + Vector3.up * y, outer = d * (C + 0.55f) + Vector3.up * y;
        b.AddTube(inner, outer, radius, 10, true);
        b.AddTube(wallPt - d * 0.10f, wallPt + d * 0.10f, radius * 2.0f, 12, true);         // penetration sleeve
        PipeSweep.Flange(b, outer - d * 0.08f, d, radius, 12);
        b.AddBox(outer + Vector3.up * -0.14f + new Vector3(-0.14f, 0, -0.14f), outer + Vector3.up * 0.30f + new Vector3(0.14f, 0, 0.14f));   // valve body
    }

    // Steam or feedwater line: up from the head to the run height, then sideways (+x) to the pool wall, with a wall collar.
    private static void AddLine(MeshBuilder b, Vector3 start, float run, float wallRadius, float radius)
    {
        var corners = new List<Vector3>
        {
            start,
            new Vector3(start.x, run, start.z),
            new Vector3(wallRadius - 0.3f, run, start.z),
        };
        PipeSweep.Sweep(b, corners, radius, radius * 3f, 14, 5);
        b.AddTube(new Vector3(wallRadius - 0.35f, run, start.z), new Vector3(wallRadius + 0.05f, run, start.z), radius * 1.9f, 16, true);   // wall collar
        PipeSweep.Flange(b, new Vector3(wallRadius - 0.9f, run, start.z), Vector3.right, radius, 14);
        PipeSweep.Flange(b, new Vector3(start.x, run - 1.2f, start.z), Vector3.up, radius, 14);
    }
}
