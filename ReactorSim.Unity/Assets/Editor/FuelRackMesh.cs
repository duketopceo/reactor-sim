using System.Collections.Generic;
using UnityEngine;
using ReactorSim;

// Spent-fuel storage racks standing on the pool ledge (U7): a grid of tall square cells per array (one merged mesh, one shared
// emissive material, so a whole array is one draw and the binder sets one emission colour), a dark frame, a cone of water haze
// above it (additive, opacity from GlowMapping) and one capped point light. The glow follows plant power as a labelled visual
// cue (see GlowMapping). Object names are what PlantEquipmentBinder and HallCheck look for: RackGlow_Cells_<i>,
// RackGlow_Haze_<i>, RackGlow_Light_<i>.
public static class FuelRackMesh
{
    public const float Pitch = 0.30f, CellSize = 0.24f, CellHeight = 3.2f;

    public sealed class Materials
    {
        public Material cells, frame, haze;
    }

    /// <summary>Builds rack array number index at a local hall position (on the ledge) turned about y.</summary>
    public static GameObject Build(Transform parent, int index, Vector3 position, float yawDegrees, Materials m)
    {
        var root = new GameObject("Rack_" + index);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = position;
        root.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);

        int rows = HallDimensions.RackRows, cols = HallDimensions.RackColumns;
        float w = cols * Pitch, d = rows * Pitch;

        var cells = new MeshBuilder();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                float x = (c - 0.5f * (cols - 1)) * Pitch, z = (r - 0.5f * (rows - 1)) * Pitch;
                cells.AddBox(new Vector3(x - 0.5f * CellSize, 0.05f, z - 0.5f * CellSize), new Vector3(x + 0.5f * CellSize, CellHeight, z + 0.5f * CellSize));
            }
        cells.Attach(root.transform, "RackGlow_Cells_" + index, m.cells);

        var frame = new MeshBuilder();
        frame.AddBox(new Vector3(-0.5f * w - 0.08f, -0.12f, -0.5f * d - 0.08f), new Vector3(0.5f * w + 0.08f, 0.05f, 0.5f * d + 0.08f));      // base plate
        for (int s = 0; s < 4; s++)                                                                                                       // corner posts
        {
            float x = (s & 1) == 0 ? -0.5f * w - 0.04f : 0.5f * w + 0.04f, z = (s & 2) == 0 ? -0.5f * d - 0.04f : 0.5f * d + 0.04f;
            frame.AddBox(new Vector3(x - 0.05f, 0.05f, z - 0.05f), new Vector3(x + 0.05f, CellHeight + 0.15f, z + 0.05f));
        }
        frame.AddBox(new Vector3(-0.5f * w - 0.09f, CellHeight, -0.5f * d - 0.09f), new Vector3(0.5f * w + 0.09f, CellHeight + 0.08f, -0.5f * d - 0.01f));   // top bars
        frame.AddBox(new Vector3(-0.5f * w - 0.09f, CellHeight, 0.5f * d + 0.01f), new Vector3(0.5f * w + 0.09f, CellHeight + 0.08f, 0.5f * d + 0.09f));
        frame.Attach(root.transform, "Rack_Frame_" + index, m.frame);

        // haze: an open cone narrowing upward from the rack top, both faces so it shows from above and below
        var haze = new MeshBuilder();
        haze.AddFrustum(new Vector3(0f, CellHeight + 0.1f, 0f), new Vector3(0f, CellHeight + 5.5f, 0f), 0.5f * w, 0.25f, 20, false);
        haze.Attach(root.transform, "RackGlow_Haze_" + index, m.haze);

        var lg = new GameObject("RackGlow_Light_" + index);
        lg.transform.SetParent(root.transform, false);
        lg.transform.localPosition = new Vector3(0f, CellHeight + 1.6f, 0f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(0.30f, 0.58f, 1f);
        l.range = 8f;
        l.intensity = 1.8f;
        l.shadows = LightShadows.None;
        return root;
    }

    /// <summary>The ledge the racks stand on: a stepped band round part of the pool wall at the ledge height.</summary>
    public static void BuildLedge(Transform parent, Material tile, float poolRadius, float ledgeHeight, float fromAngle, float toAngle, float width)
    {
        var b = new MeshBuilder();
        var outer = new List<Vector2>();
        var inner = new List<Vector2>();
        const int steps = 40;
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(fromAngle, toAngle, (float)i / steps);
            outer.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (poolRadius - 0.02f));
            inner.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (poolRadius - width));
        }
        b.AddBand(inner, outer, ledgeHeight, true);
        b.AddArcShell(poolRadius - width, 0f, ledgeHeight, fromAngle, toAngle, steps, true);       // ledge face towards the pool centre
        b.Attach(parent, "Pool_Ledge", tile);
    }
}
