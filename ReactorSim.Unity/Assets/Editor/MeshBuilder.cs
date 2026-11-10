using System.Collections.Generic;
using UnityEngine;

// Procedural mesh accumulator for the editor-side builders (reactor module, containment, pipes, racks, crane, hall shell).
// Original geometry generated from formulas, all in the owner's local space (y up). Winding is made to agree with the stored
// normals in ToMesh, so the generators need not get it right by hand: flip a normal and the face flips with it.
public sealed class MeshBuilder
{
    private readonly List<Vector3> v = new List<Vector3>();
    private readonly List<Vector3> n = new List<Vector3>();
    private readonly List<int> t = new List<int>();

    public int TriangleCount { get { return t.Count / 3; } }

    /// <summary>Surface of revolution about the y axis through the (r, y) profile, smooth-shaded. inward flips the normals.</summary>
    public void AddLathe(float[] r, float[] y, int seg, bool inward = false)
    {
        int rows = r.Length, cols = seg + 1, baseIdx = v.Count;
        float flip = inward ? -1f : 1f;
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
                n.Add(new Vector3(nn.x * ca, nn.y, nn.x * sa) * flip);
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

    /// <summary>Cylinder from p0 to p1 with flat end caps (or none).</summary>
    public void AddTube(Vector3 p0, Vector3 p1, float radius, int seg, bool caps)
    {
        AddFrustum(p0, p1, radius, radius, seg, caps);
    }

    /// <summary>Cone frustum from p0 (radius r0) to p1 (radius r1).</summary>
    public void AddFrustum(Vector3 p0, Vector3 p1, float r0, float r1, int seg, bool caps)
    {
        Vector3 axis = (p1 - p0).normalized;
        Vector3 u = Mathf.Abs(axis.y) < 0.95f ? Vector3.Cross(axis, Vector3.up).normalized : Vector3.Cross(axis, Vector3.right).normalized;
        Vector3 w = Vector3.Cross(axis, u);
        int cols = seg + 1, baseIdx = v.Count;
        float slope = (r0 - r1) / Mathf.Max(0.0001f, (p1 - p0).magnitude);
        for (int k = 0; k < 2; k++)
            for (int j = 0; j < cols; j++)
            {
                float a = 2f * Mathf.PI * j / seg;
                Vector3 radial = u * Mathf.Cos(a) + w * Mathf.Sin(a);
                v.Add((k == 0 ? p0 : p1) + radial * (k == 0 ? r0 : r1));
                n.Add((radial + axis * slope).normalized);
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
            float rr = k == 0 ? r0 : r1;
            int ci = v.Count;
            v.Add(c); n.Add(nrm);
            for (int j = 0; j < cols; j++)
            {
                float a = 2f * Mathf.PI * j / seg;
                v.Add(c + (u * Mathf.Cos(a) + w * Mathf.Sin(a)) * rr); n.Add(nrm);
            }
            for (int j = 0; j < seg; j++) AddTri(ci, ci + 1 + j, ci + 2 + j);
        }
    }

    /// <summary>Part of a vertical cylinder wall between two angles (radians, 0 along +x, towards +z), facing out or in.</summary>
    public void AddArcShell(float radius, float y0, float y1, float a0, float a1, int seg, bool inward)
    {
        int cols = seg + 1, baseIdx = v.Count;
        for (int k = 0; k < 2; k++)
            for (int j = 0; j < cols; j++)
            {
                float a = Mathf.Lerp(a0, a1, (float)j / seg), ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                v.Add(new Vector3(radius * ca, k == 0 ? y0 : y1, radius * sa));
                n.Add(new Vector3(ca, 0f, sa) * (inward ? -1f : 1f));
            }
        for (int j = 0; j < seg; j++)
        {
            int p0 = baseIdx + j, p1 = p0 + 1, q0 = p0 + cols, q1 = q0 + 1;
            AddTri(p0, q0, p1);
            AddTri(p1, q0, q1);
        }
    }

    /// <summary>
    /// Wall strip along an open or closed boundary polyline (x, z pairs), from y0 to y1, facing the side the normals point to.
    /// normalsLeft picks which side of the travel direction the face looks at.
    /// </summary>
    public void AddWallStrip(IList<Vector2> boundary, float y0, float y1, bool normalsLeft)
    {
        for (int i = 0; i + 1 < boundary.Count; i++)
        {
            Vector2 a = boundary[i], b = boundary[i + 1];
            Vector2 d = (b - a).normalized;
            Vector2 left = new Vector2(-d.y, d.x);
            Vector2 nn = normalsLeft ? left : -left;
            var nrm = new Vector3(nn.x, 0f, nn.y);
            int bi = v.Count;
            v.Add(new Vector3(a.x, y0, a.y)); n.Add(nrm);
            v.Add(new Vector3(b.x, y0, b.y)); n.Add(nrm);
            v.Add(new Vector3(a.x, y1, a.y)); n.Add(nrm);
            v.Add(new Vector3(b.x, y1, b.y)); n.Add(nrm);
            AddTri(bi, bi + 2, bi + 1);
            AddTri(bi + 1, bi + 2, bi + 3);
        }
    }

    /// <summary>Flat polygon fan from a centre point to a boundary polyline (x, z pairs) at height y, facing up or down.</summary>
    public void AddFan(Vector2 centre, IList<Vector2> boundary, float y, bool up)
    {
        var nrm = up ? Vector3.up : Vector3.down;
        int ci = v.Count;
        v.Add(new Vector3(centre.x, y, centre.y)); n.Add(nrm);
        for (int i = 0; i < boundary.Count; i++) { v.Add(new Vector3(boundary[i].x, y, boundary[i].y)); n.Add(nrm); }
        for (int i = 0; i + 1 < boundary.Count; i++) AddTri(ci, ci + 1 + i, ci + 2 + i);
    }

    /// <summary>Flat band between an inner and an outer polyline (same point count, x and z pairs) at height y.</summary>
    public void AddBand(IList<Vector2> inner, IList<Vector2> outer, float y, bool up)
    {
        var nrm = up ? Vector3.up : Vector3.down;
        int bi = v.Count;
        for (int i = 0; i < inner.Count; i++)
        {
            v.Add(new Vector3(inner[i].x, y, inner[i].y)); n.Add(nrm);
            v.Add(new Vector3(outer[i].x, y, outer[i].y)); n.Add(nrm);
        }
        for (int i = 0; i + 1 < inner.Count; i++)
        {
            int a = bi + i * 2;
            AddTri(a, a + 1, a + 2);
            AddTri(a + 2, a + 1, a + 3);
        }
    }

    /// <summary>Axis-aligned box with flat-shaded faces.</summary>
    public void AddBox(Vector3 min, Vector3 max)
    {
        Vector3[] c =
        {
            new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z),
            new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z),
        };
        Quad(c[0], c[1], c[2], c[3], Vector3.back);
        Quad(c[5], c[4], c[7], c[6], Vector3.forward);
        Quad(c[4], c[0], c[3], c[7], Vector3.left);
        Quad(c[1], c[5], c[6], c[2], Vector3.right);
        Quad(c[3], c[2], c[6], c[7], Vector3.up);
        Quad(c[4], c[5], c[1], c[0], Vector3.down);
    }

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm)
    {
        int bi = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        for (int i = 0; i < 4; i++) n.Add(nrm);
        AddTri(bi, bi + 1, bi + 2);
        AddTri(bi, bi + 2, bi + 3);
    }

    private void AddTri(int a, int b, int c) { t.Add(a); t.Add(b); t.Add(c); }

    public Mesh ToMesh(string name)
    {
        for (int i = 0; i + 2 < t.Count; i += 3)
        {
            Vector3 fn = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            Vector3 vn = n[t[i]] + n[t[i + 1]] + n[t[i + 2]];
            // Unity front faces are clockwise as seen from the front, which puts this cross product on the viewer's side, so
            // a triangle is correct when it points the same way as its vertex normals.
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

    /// <summary>Puts the mesh on a child object of parent with the given material and returns it.</summary>
    public GameObject Attach(Transform parent, string name, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = ToMesh(name);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }
}
