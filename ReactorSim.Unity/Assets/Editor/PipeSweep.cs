using System.Collections.Generic;
using UnityEngine;

// Pipes as polyline tubes with rounded elbows (original geometry). A pipe is a list of corner points; each interior corner is
// replaced by a short quadratic arc so a run reads as bent pipe rather than butted cylinders. Returns the swept centre line so
// callers can hang flanges and supports on it.
public static class PipeSweep
{
    public static List<Vector3> Sweep(MeshBuilder b, IList<Vector3> corners, float radius, float bendRadius, int seg, int arcSteps = 4)
    {
        var line = new List<Vector3>(corners.Count * (arcSteps + 1));
        line.Add(corners[0]);
        for (int i = 1; i + 1 < corners.Count; i++)
        {
            Vector3 p = corners[i];
            Vector3 d0 = (p - corners[i - 1]).normalized, d1 = (corners[i + 1] - p).normalized;
            float turn = Vector3.Angle(d0, d1);
            if (turn < 1f) { line.Add(p); continue; }
            // trim each leg by the tangent length of the bend, but never more than half the leg
            float trim = bendRadius * Mathf.Tan(turn * 0.5f * Mathf.Deg2Rad);
            trim = Mathf.Min(trim, 0.5f * (p - corners[i - 1]).magnitude, 0.5f * (corners[i + 1] - p).magnitude);
            Vector3 a = p - d0 * trim, c = p + d1 * trim;
            for (int s = 0; s <= arcSteps; s++)
            {
                float u = (float)s / arcSteps;
                line.Add((1f - u) * (1f - u) * a + 2f * (1f - u) * u * p + u * u * c);
            }
        }
        line.Add(corners[corners.Count - 1]);
        for (int i = 0; i + 1 < line.Count; i++)
        {
            if ((line[i + 1] - line[i]).sqrMagnitude < 1e-8f) continue;
            b.AddTube(line[i], line[i + 1], radius, seg, i == 0 || i + 2 == line.Count);
        }
        return line;
    }

    /// <summary>A flanged joint: a short wider ring around a pipe at a point along a direction.</summary>
    public static void Flange(MeshBuilder b, Vector3 at, Vector3 dir, float pipeRadius, int seg)
    {
        Vector3 d = dir.normalized;
        b.AddTube(at - d * 0.05f, at + d * 0.05f, pipeRadius * 1.55f, seg, true);
    }
}
