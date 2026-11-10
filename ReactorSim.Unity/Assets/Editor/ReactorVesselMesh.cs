using System.Collections.Generic;
using UnityEngine;

// Procedural reactor pressure vessel for the reactor hall (original geometry generated from formulas): a cylindrical
// shell, a bolted closure-head flange with a ring of studs and nuts, an ellipsoidal dome, a hex cluster of thin
// control-rod drive mechanism (CRDM) housings on the dome, and four coolant nozzles at flange level. Everything is
// generated in local space (y up, origin on the vessel axis at the pool floor) so the builder only places the root.
public static class ReactorVesselMesh
{
    // Heights are metres above the pool floor; the water surface is at 0.8 m, so the shell and nozzles sit under the
    // water and the flange, studs, dome and CRDM housings break the surface.
    public const float ShellRadius = 0.50f, ShellBottom = 0.05f, FlangeBottom = 0.70f, FlangeTop = 0.90f, FlangeRadius = 0.64f;
    public const float DomeHeight = 0.42f, StudRadius = 0.575f;
    private const float CrdmSpacing = 0.085f, CrdmMaxRadius = 0.30f;

    public static GameObject Build(Transform parent, Vector3 position, Material shell, Material dark)
    {
        var root = new GameObject("Reactor_Vessel");
        root.transform.SetParent(parent, true);
        root.transform.position = position;

        var s = new MeshBuilder();
        s.AddTube(new Vector3(0, ShellBottom, 0), new Vector3(0, FlangeBottom + 0.02f, 0), ShellRadius, 36, true);
        s.AddTube(new Vector3(0, FlangeBottom, 0), new Vector3(0, FlangeTop, 0), FlangeRadius, 40, true);
        AddDome(s);
        AddStuds(s);
        AddNozzles(s);
        Attach(root.transform, "Vessel_Shell", s.ToMesh("Vessel_Shell"), shell);

        var c = new MeshBuilder();
        AddCrdm(c);
        Attach(root.transform, "Vessel_CRDM", c.ToMesh("Vessel_CRDM"), dark);
        Debug.Log($"[Phase2SceneBuilder] reactor vessel: shell {s.TriangleCount} tris, CRDM {c.TriangleCount} tris");
        return root;
    }

    private static void Attach(Transform parent, string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    private static float DomeY(float r)
    {
        float k = Mathf.Clamp01(r / ShellRadius);
        return FlangeTop + DomeHeight * Mathf.Sqrt(1f - k * k);
    }

    private static void AddDome(MeshBuilder b)
    {
        const int rings = 10;
        var rs = new float[rings + 1];
        var ys = new float[rings + 1];
        for (int i = 0; i <= rings; i++)
        {
            float th = (Mathf.PI * 0.5f) * i / rings;
            rs[i] = ShellRadius * Mathf.Cos(th);
            ys[i] = FlangeTop + DomeHeight * Mathf.Sin(th);
        }
        b.AddLathe(rs, ys, 40);
    }

    private static void AddStuds(MeshBuilder b)
    {
        const int n = 28;
        for (int i = 0; i < n; i++)
        {
            float a = 2f * Mathf.PI * i / n;
            var p = new Vector3(Mathf.Cos(a) * StudRadius, FlangeTop, Mathf.Sin(a) * StudRadius);
            b.AddTube(p, p + Vector3.up * 0.10f, 0.020f, 8, true);          // stud
            b.AddTube(p, p + Vector3.up * 0.045f, 0.034f, 6, true);         // nut
        }
    }

    private static void AddNozzles(MeshBuilder b)
    {
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.PI * 0.25f + i * Mathf.PI * 0.5f;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float y = 0.50f;
            b.AddTube(d * (ShellRadius - 0.05f) + Vector3.up * y, d * 0.82f + Vector3.up * y, 0.095f, 20, true);
            b.AddTube(d * 0.76f + Vector3.up * y, d * 0.86f + Vector3.up * y, 0.130f, 20, true);   // weld-prep collar
        }
    }

    private static void AddCrdm(MeshBuilder b)
    {
        for (int q = -4; q <= 4; q++)
        {
            for (int p = -4; p <= 4; p++)
            {
                // axial hex lattice
                float x = CrdmSpacing * (p + 0.5f * q), z = CrdmSpacing * q * 0.8660254f;
                float r = Mathf.Sqrt(x * x + z * z);
                if (r > CrdmMaxRadius) continue;
                float y0 = DomeY(r) - 0.02f;
                var bp = new Vector3(x, y0, z);
                b.AddTube(bp, bp + Vector3.up * 0.30f, 0.018f, 8, true);                          // pressure housing
                b.AddTube(bp + Vector3.up * 0.24f, bp + Vector3.up * 0.265f, 0.026f, 8, true);   // coil stack ring
                b.AddTube(bp + Vector3.up * 0.30f, bp + Vector3.up * 0.325f, 0.024f, 8, true);   // top cap
            }
        }
    }

    private sealed class MeshBuilder
    {
        private readonly List<Vector3> v = new List<Vector3>();
        private readonly List<Vector3> n = new List<Vector3>();
        private readonly List<int> t = new List<int>();
        public int TriangleCount { get { return t.Count / 3; } }

        // Surface of revolution about the y axis through the (r, y) profile, smooth-shaded.
        public void AddLathe(float[] r, float[] y, int seg)
        {
            int rows = r.Length, cols = seg + 1, baseIdx = v.Count;
            for (int i = 0; i < rows; i++)
            {
                // outward normal in the (r, y) plane: average of the adjoining segment normals
                Vector2 nn = Vector2.zero;
                if (i > 0) nn += SegNormal(r[i - 1], y[i - 1], r[i], y[i]);
                if (i < rows - 1) nn += SegNormal(r[i], y[i], r[i + 1], y[i + 1]);
                nn.Normalize();
                for (int j = 0; j < cols; j++)
                {
                    float a = 2f * Mathf.PI * j / seg, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    v.Add(new Vector3(r[i] * ca, y[i], r[i] * sa));
                    n.Add(new Vector3(nn.x * ca, nn.y, nn.x * sa));
                }
            }
            for (int i = 0; i < rows - 1; i++)
                for (int j = 0; j < seg; j++)
                {
                    int a0 = baseIdx + i * cols + j, a1 = a0 + 1, b0 = a0 + cols, b1 = b0 + 1;
                    AddTri(a0, b0, a1);
                    AddTri(a1, b0, b1);
                }
        }

        private static Vector2 SegNormal(float r0, float y0, float r1, float y1)
        {
            return new Vector2(y1 - y0, -(r1 - r0)).normalized;
        }

        // Cylinder from p0 to p1 with flat end caps.
        public void AddTube(Vector3 p0, Vector3 p1, float radius, int seg, bool caps)
        {
            Vector3 axis = (p1 - p0).normalized;
            Vector3 u = Mathf.Abs(axis.y) < 0.95f ? Vector3.Cross(axis, Vector3.up).normalized : Vector3.Cross(axis, Vector3.right).normalized;
            Vector3 w = Vector3.Cross(axis, u);
            int cols = seg + 1, baseIdx = v.Count;
            for (int k = 0; k < 2; k++)
                for (int j = 0; j < cols; j++)
                {
                    float a = 2f * Mathf.PI * j / seg;
                    Vector3 radial = u * Mathf.Cos(a) + w * Mathf.Sin(a);
                    v.Add((k == 0 ? p0 : p1) + radial * radius);
                    n.Add(radial);
                }
            for (int j = 0; j < seg; j++)
            {
                int a0 = baseIdx + j, a1 = a0 + 1, b0 = a0 + cols, b1 = b0 + 1;
                AddTri(a0, b0, a1);
                AddTri(a1, b0, b1);
            }
            if (!caps) return;
            for (int k = 0; k < 2; k++)
            {
                Vector3 c = k == 0 ? p0 : p1, nrm = k == 0 ? -axis : axis;
                int ci = v.Count;
                v.Add(c); n.Add(nrm);
                for (int j = 0; j < cols; j++)
                {
                    float a = 2f * Mathf.PI * j / seg;
                    v.Add(c + (u * Mathf.Cos(a) + w * Mathf.Sin(a)) * radius); n.Add(nrm);
                }
                for (int j = 0; j < seg; j++) AddTri(ci, ci + 1 + j, ci + 2 + j);
            }
        }

        private void AddTri(int a, int b, int c) { t.Add(a); t.Add(b); t.Add(c); }

        // Winding is made to agree with the stored normals here, so the generators above need not get it right by hand.
        public Mesh ToMesh(string name)
        {
            for (int i = 0; i + 2 < t.Count; i += 3)
            {
                Vector3 fn = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                Vector3 vn = n[t[i]] + n[t[i + 1]] + n[t[i + 2]];
                // Unity front faces are clockwise as seen from the front, which puts this cross product on the viewer's
                // side, so a triangle is correct when it points the same way as its vertex normals.
                if (Vector3.Dot(fn, vn) < 0f) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
            }
            var m = new Mesh { name = name };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
