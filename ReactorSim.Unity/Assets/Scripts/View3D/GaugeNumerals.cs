using UnityEngine;
using TMPro;
using ReactorSim.Instruments;
using ReactorSim.Ui;

// Prints the scale numerals (minimum, middle, maximum) and the unit on a circular dial, so a value can be read off it.
// The text sits on the kit face under the needle pivot, in the kit's local frame (same axes as CounterDisplay): the
// numerals stand at the needle's own sweep angles, a little inside the tick ring, and the unit sits below the hub.
public static class GaugeNumerals
{
    // Face widths (m) of the circular kits (design/control-room/kits.json K01, K01L).
    private const float SmallFaceM = 0.22f, LargeFaceM = 0.45f;
    private const float RingRadiusFrac = 0.235f;     // numeral radius as a share of the face width
    private const float UnitDropFrac = 0.215f;       // unit label distance below the hub
    private const float LiftM = 0.0025f;

    /// <summary>Kit face width for a circular dial pivot, or 0 when it is not one (bars, counters, other kits).</summary>
    public static float FaceWidth(Transform pivot)
    {
        var ctl = pivot;
        while (ctl != null && !ctl.name.StartsWith("CTL_")) ctl = ctl.parent;
        if (ctl == null) return 0f;
        var row = RuntimeInventoryStore.Inventory.Find(ctl.name.Substring(4));
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
            // The needle turns about the face normal by NeedleDeg(frac); a numeral stands at the same angle from "up".
            Quaternion turn = Quaternion.AngleAxis(GaugeGeometry.NeedleDeg(mark.Frac), ControlRoom3DBinder.KitFaceNormal);
            Vector3 at = pivot.localPosition + turn * ControlRoom3DBinder.KitFaceUp * (face * RingRadiusFrac);
            Make(parent, kit, mark.Text, at, faceRot, face * 0.085f, face, k, ls, ThemeColors.TextInkOnPaper);
        }
        string unit = GaugeScale.UnitLabel(src.Units);
        if (unit.Length > 0)
        {
            Vector3 at = pivot.localPosition - ControlRoom3DBinder.KitFaceUp * (face * UnitDropFrac);
            Make(parent, kit, unit, at, faceRot, face * 0.07f, face, k, ls, ThemeColors.TextInkOnPaper);
        }
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
