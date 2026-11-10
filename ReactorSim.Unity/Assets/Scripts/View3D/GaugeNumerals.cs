using UnityEngine;
using TMPro;
using ReactorSim.Controls;
using ReactorSim.Instruments;
using ReactorSim.Ui;

// Prints the scale numerals (minimum, middle, maximum) and the unit on a circular dial, so a value can be read off it.
// The text sits on the kit face under the needle pivot, in the kit's local frame (same axes as CounterDisplay): the
// numeral stands at the needle's own angle, the end numerals past the sweep ends, and the unit sits below the hub.
public static class GaugeNumerals
{
    // Face widths (m) of the circular kits (design/control-room/kits.json K01, K01L).
    private const float SmallFaceM = 0.22f, LargeFaceM = 0.45f;
    private const float UnitDropFrac = 0.17f;       // unit label distance below the hub
    private const float LiftM = 0.0025f;

    /// <summary>Kit face width for a circular dial pivot, or 0 when it is not one (bars, counters, other kits).</summary>
    public static float FaceWidth(Transform pivot)
    {
        var row = RowOf(pivot);
        if (row == null) return 0f;
        if (row.kit == "K01") return SmallFaceM;
        if (row.kit == "K01L") return LargeFaceM;
        return 0f;
    }

    public static void Attach(Transform pivot, GaugeSource src)
    {
        float face = FaceWidth(pivot);
        var kit = pivot.parent;
        if (face <= 0f || kit == null || src == null) return;

        Vector3 ls = kit.lossyScale;
        float k = Mathf.Max(1e-4f, (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f);
        Quaternion faceRot = Quaternion.LookRotation(-ControlRoom3DBinder.KitFaceNormal, ControlRoom3DBinder.KitFaceUp);
        var parent = new GameObject(pivot.name + "_Scale").transform;
        parent.SetParent(kit, false);

        foreach (var mark in GaugeScale.Marks(src.Min, src.Max, src.Units))
        {
            // The needle turns about the face normal by NeedleDeg(frac); see GaugeScale.NumeralSpot.
            float deg, radius;
            GaugeScale.NumeralSpot(mark.Frac, GaugeGeometry.NeedleSweepMinDeg, GaugeGeometry.NeedleSweepMaxDeg, out deg, out radius);
            Quaternion turn = Quaternion.AngleAxis(deg, ControlRoom3DBinder.KitFaceNormal);
            Vector3 at = pivot.localPosition + turn * ControlRoom3DBinder.KitFaceUp * (face * radius);
            Make(parent, kit, mark.Text, at, faceRot, face * 0.085f, face, k, ls, ThemeColors.TextInkOnPaper);
        }
        string unit = GaugeScale.UnitLabel(src.Units);
        if (unit.Length > 0)
        {
            Vector3 at = pivot.localPosition - ControlRoom3DBinder.KitFaceUp * (face * UnitDropFrac);
            Make(parent, kit, unit, at, faceRot, face * 0.07f, face, k, ls, ThemeColors.TextInkOnPaper);
        }
    }

    // The edgewise bars (K02): the minimum and maximum stand just past the ends of the pointer's travel, the unit rides
    // on the maximum, so a level reads off the strip. The middle is left off: the tick lines already mark it.
    private const float BarFaceWidthM = 0.14f, BarFaceHeightM = 0.34f;
    private const float BarEndGapM = 0.022f;

    public static bool IsBarKit(Transform pivot)
    {
        var row = RowOf(pivot);
        return row != null && row.kit == "K02";
    }

    // The inventory row of the CTL_ root a pivot sits under, or null.
    private static RuntimeRow RowOf(Transform pivot)
    {
        var ctl = pivot;
        while (ctl != null && !ctl.name.StartsWith("CTL_")) ctl = ctl.parent;
        return ctl == null ? null : RuntimeInventoryStore.Inventory.Find(ctl.name.Substring(4));
    }

    public static void AttachBar(Transform pivot, GaugeSource src, float travelM)
    {
        var kit = pivot.parent;
        if (kit == null || src == null || !IsBarKit(pivot)) return;
        Vector3 ls = kit.lossyScale;
        float k = Mathf.Max(1e-4f, (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f);
        Quaternion faceRot = Quaternion.LookRotation(-ControlRoom3DBinder.KitFaceNormal, ControlRoom3DBinder.KitFaceUp);
        var parent = new GameObject(pivot.name + "_Scale").transform;
        parent.SetParent(kit, false);
        float centre = Vector3.Dot(pivot.localPosition, ControlRoom3DBinder.KitFaceUp);
        float limit = BarFaceHeightM * 0.5f - 0.02f;
        Vector3 off = pivot.localPosition - ControlRoom3DBinder.KitFaceUp * centre;
        var marks = GaugeScale.Marks(src.Min, src.Max, src.Units);
        string unit = GaugeScale.UnitLabel(src.Units);
        float lo = centre - travelM * 0.5f - BarEndGapM;
        float hi = centre + travelM * 0.5f + BarEndGapM;
        // A long strip leaves no room past its ends: the numerals then stand beside the pointer's travel instead of on it.
        Vector3 side = Vector3.zero;
        if (lo < -limit || hi > limit)
        {
            lo = Mathf.Max(-limit, lo);
            hi = Mathf.Min(limit, hi);
            side = Vector3.left * (BarFaceWidthM * 0.32f);
        }
        float size = BarFaceWidthM * 0.11f;
        Make(parent, kit, marks[0].Text, off + side + ControlRoom3DBinder.KitFaceUp * lo, faceRot, size, BarFaceWidthM, k, ls, ThemeColors.TextInkOnPaper);
        Make(parent, kit, marks[2].Text + (unit == "%" ? "" : " ") + unit, off + side + ControlRoom3DBinder.KitFaceUp * hi, faceRot, size, BarFaceWidthM, k, ls, ThemeColors.TextInkOnPaper);
    }

    private static void Make(Transform parent, Transform kit, string text, Vector3 localPos, Quaternion rot, float fontSize,
        float face, float k, Vector3 ls, Color color)
    {
        var go = new GameObject("Numeral_" + text);
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(Inv(ls.x), Inv(ls.y), Inv(ls.z));
        go.transform.localPosition = localPos + ControlRoom3DBinder.KitFaceNormal * (LiftM / k);
        go.transform.localRotation = rot;
        var t = go.AddComponent<TextMeshPro>();
        t.rectTransform.sizeDelta = new Vector2(face * 0.30f, face * 0.12f);
        t.font = UiFonts.Mono;
        t.fontSize = fontSize * 10f;   // TextMeshPro (3D): one font-size unit is 0.1 world units
        t.enableAutoSizing = false;
        t.alignment = TextAlignmentOptions.Center;
        t.color = color;
        t.richText = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.text = text;
    }

    private static float Inv(float v)
    {
        return Mathf.Abs(v) > 1e-4f ? 1f / v : 1f;
    }
}
