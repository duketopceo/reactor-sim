using UnityEngine;
using ReactorSim;

// Polar crane for the reactor hall (original geometry): a rail band running round the hall wall, a two-girder bridge spanning
// the hall on end trucks, a trolley with a hoist drum, and a hoist cable down to a lifting block hanging over the pool. Static:
// the pose is fixed (a parked crane), no animation. Local hall space: origin on the pool axis at the pool floor, y up.
public static class PolarCrane
{
    public static void Build(Transform parent, Material steel, Material dark, float railY, float hallRadius, float wallDistance)
    {
        var b = new MeshBuilder();
        // rail band on the wall (skips the stretch cut by the control-room wall plane at z = -wallDistance)
        float cut = Mathf.Acos(Mathf.Clamp01(wallDistance / hallRadius));
        float a0 = -0.5f * Mathf.PI + cut, a1 = -0.5f * Mathf.PI + 2f * Mathf.PI - cut;
        float rr = hallRadius - 0.12f;
        b.AddArcShell(rr, railY - 0.55f, railY - 0.05f, a0, a1, 96, true);          // rail corbel
        b.AddArcShell(rr - 0.35f, railY - 0.05f, railY + 0.25f, a0, a1, 96, true);   // rail head
        b.AddArcShell(rr - 0.35f, railY - 0.05f, railY + 0.25f, a0, a1, 96, false);
        b.Attach(parent, "Crane_RailRing", dark);

        // bridge across the hall at z = zb: two girders on end trucks, deck plates, walkway rail
        float zb = 2.2f, half = Mathf.Sqrt(rr * rr - zb * zb) - 0.5f;
        var g = new MeshBuilder();
        float yTop = railY + 0.9f;
        for (int s = -1; s <= 1; s += 2)
        {
            float z = zb + s * 1.6f;
            g.AddBox(new Vector3(-half, railY + 0.25f, z - 0.22f), new Vector3(half, yTop, z + 0.22f));          // girder
            g.AddBox(new Vector3(-half, yTop, z - 0.5f), new Vector3(half, yTop + 0.08f, z + 0.5f));            // top flange
        }
        for (int s = -1; s <= 1; s += 2)
        {
            float x = s * half;
            g.AddBox(new Vector3(x - 0.7f, railY - 0.3f, zb - 2.3f), new Vector3(x + 0.7f, yTop, zb + 2.3f)); // end truck
        }
        for (int i = -6; i <= 6; i++)                                                                          // cross braces
            g.AddBox(new Vector3(i * (half / 6.5f) - 0.08f, railY + 0.3f, zb - 1.6f), new Vector3(i * (half / 6.5f) + 0.08f, yTop - 0.1f, zb + 1.6f));
        g.Attach(parent, "Crane_Bridge", steel);

        // trolley and hoist, parked over the pool axis
        var t = new MeshBuilder();
        t.AddBox(new Vector3(-1.4f, yTop + 0.08f, zb - 1.9f), new Vector3(1.4f, yTop + 1.3f, zb + 1.9f));      // trolley frame
        t.AddTube(new Vector3(-1.1f, yTop + 1.0f, zb), new Vector3(1.1f, yTop + 1.0f, zb), 0.55f, 20, true);     // hoist drum
        t.AddBox(new Vector3(0.6f, yTop + 1.3f, zb - 0.8f), new Vector3(1.4f, yTop + 2.2f, zb + 0.8f));          // motor and gearbox
        float blockY = HallDimensions.WaterDepth + HallDimensions.DeckAboveWater + 3.5f;
        t.AddTube(new Vector3(0, yTop + 0.1f, zb), new Vector3(0, blockY + 0.9f, zb), 0.05f, 6, true);          // hoist cable
        t.AddBox(new Vector3(-0.45f, blockY, zb - 0.3f), new Vector3(0.45f, blockY + 0.9f, zb + 0.3f));          // lifting block
        t.AddTube(new Vector3(0, blockY, zb), new Vector3(0, blockY - 0.7f, zb), 0.13f, 10, true);              // hook shank
        t.Attach(parent, "Crane_Trolley", dark);
    }
}
