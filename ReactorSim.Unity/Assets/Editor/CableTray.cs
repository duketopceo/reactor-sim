using System.Collections.Generic;
using UnityEngine;

// Cable trays for the reactor hall (original geometry): a ladder tray running along the hall wall at a fixed height, with
// hangers, and a vertical riser down to a junction cabinet beside the pool. These carry the module's power in (instrument and
// pump supplies) and signals out. Local hall space: origin on the pool axis at the pool floor.
public static class CableTray
{
    public static void Build(Transform parent, Material dark, float radius, float y, float fromAngle, float toAngle)
    {
        var b = new MeshBuilder();
        const int steps = 48;
        for (int i = 0; i < steps; i++)
        {
            float a = Mathf.Lerp(fromAngle, toAngle, (float)i / steps), a2 = Mathf.Lerp(fromAngle, toAngle, (float)(i + 1) / steps);
            Vector3 p = new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius), q = new Vector3(Mathf.Cos(a2) * radius, y, Mathf.Sin(a2) * radius);
            b.AddTube(p + Vector3.up * 0.18f, q + Vector3.up * 0.18f, 0.04f, 5, false);   // side rails
            b.AddTube(p - Vector3.up * 0.18f, q - Vector3.up * 0.18f, 0.04f, 5, false);
            if (i % 2 == 0)
            {
                Vector3 inward = -new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                b.AddBox(p + inward * 0.0f - new Vector3(0.03f, 0.18f, 0.03f), p + inward * 0.45f + new Vector3(0.03f, 0.18f, 0.03f));   // rung
            }
            if (i % 6 == 0)
                b.AddTube(p + Vector3.up * 0.2f, p + Vector3.up * 1.4f, 0.03f, 5, true);                                                       // hanger
        }
        // riser to the junction cabinet
        float aEnd = toAngle;
        Vector3 top = new Vector3(Mathf.Cos(aEnd) * (radius - 0.3f), y, Mathf.Sin(aEnd) * (radius - 0.3f));
        var floor = new Vector3(top.x, 22.3f, top.z);
        b.AddTube(floor + Vector3.up * 1.6f, top, 0.16f, 8, true);
        b.AddBox(floor - new Vector3(0.5f, 0f, 0.35f), floor + new Vector3(0.5f, 1.6f, 0.35f));
        b.Attach(parent, "CableTray", dark);
    }
}
