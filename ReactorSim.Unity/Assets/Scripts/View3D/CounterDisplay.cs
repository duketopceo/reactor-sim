using UnityEngine;
using System.Collections.Generic;
using TMPro;

// K04 seven-segment counters (boron ppm, game clock, generator kW ...). The kit carries a BIND_GAUGE pivot with no
// visible child, so rotating it as a needle moves nothing and the digit windows stay blank. The binder routes such
// pivots here instead: a world-space TextMeshPro on the counter face shows the value as five amber digits.
//
// Face geometry follows ControlRoom3DBinder's kit axes: the pivot's parent (the kit root) keeps Blender's local frame,
// face normal = local up, face up = local forward, across = local right. The face is 0.30 x 0.12 m (kits.json K04); the
// five digit windows span 0.24 x 0.07 m around the pivot.
public static class CounterDisplay
{
    public const float FaceWidthM = 0.30f;
    public const float FaceHeightM = 0.12f;
    public const float TextWidthM = 0.24f;
    public const float TextHeightM = 0.07f;
    private const float LiftM = 0.002f;
    public static readonly Color Segment = new Color(1f, 0.42f, 0.06f, 1f);   // amber / red-orange

    /// <summary>
    /// True for a gauge pivot that is a counter: its kit is K04 (per the runtime inventory), or it has nothing under it
    /// to rotate (no renderer on or below the pivot), which no needle or bar pivot ever lacks.
    /// </summary>
    public static bool IsCounterPivot(Transform pivot)
    {
        if (pivot == null || !pivot.name.StartsWith("BIND_GAUGE_")) return false;
        var ctl = pivot;
        while (ctl != null && !ctl.name.StartsWith("CTL_")) ctl = ctl.parent;
        if (ctl != null)
        {
            var row = RuntimeInventoryStore.Inventory.Find(ctl.name.Substring(4));
            if (row != null && row.kit == "K04") return true;
        }
        return pivot.GetComponentInChildren<Renderer>(true) == null;
    }

    /// <summary>Creates the digit text on the counter face under the pivot's parent; null when TextMeshPro is unavailable.</summary>
    public static TextMeshPro Attach(Transform pivot)
    {
        var kit = pivot.parent;
        if (kit == null) return null;
        var go = new GameObject(pivot.name + "_Digits");
        go.transform.SetParent(kit, false);
        // Keep the text at true metres whatever scale the imported kit carries.
        Vector3 ls = kit.lossyScale;
        go.transform.localScale = new Vector3(Inv(ls.x), Inv(ls.y), Inv(ls.z));
        float k = Mathf.Max(1e-4f, (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f);
        go.transform.localPosition = pivot.localPosition + ControlRoom3DBinder.KitFaceNormal * (LiftM / k);
        // Readable from the +normal side: the text looks into the face (-normal) with its up along the face up.
        go.transform.localRotation = Quaternion.LookRotation(-ControlRoom3DBinder.KitFaceNormal, ControlRoom3DBinder.KitFaceUp);

        var text = go.AddComponent<TextMeshPro>();
        text.rectTransform.sizeDelta = new Vector2(TextWidthM, TextHeightM);
        text.enableAutoSizing = true;
        text.fontSizeMin = 0.05f;
        text.fontSizeMax = 2f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Segment;
        text.richText = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.text = "-----";
        DarkenWindows(kit);
        return text;
    }

    // The kit's number windows arrive glowing pale cyan (the exported screen emission), which washes out the amber numerals.
    // Each distinct window material is cloned once with a dark screen emission, so the numerals read like a lit display.
    private static readonly Dictionary<Material, Material> darkWindows = new Dictionary<Material, Material>();

    private static void DarkenWindows(Transform kit)
    {
        foreach (var r in kit.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.name.IndexOf("CrtScreen", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                Material dark;
                if (!darkWindows.TryGetValue(m, out dark) || dark == null)
                {
                    dark = new Material(m) { name = m.name + "_NumberWindow" };
                    if (dark.HasProperty("_Color")) dark.SetColor("_Color", Color.black);
                    if (dark.HasProperty("_EmissionColor")) dark.SetColor("_EmissionColor", ThemeColors.SurfaceScreen);
                    if (dark.HasProperty("_EmissionMap")) dark.SetTexture("_EmissionMap", null);
                    darkWindows[m] = dark;
                }
                mats[i] = dark;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    private static float Inv(float v)
    {
        return Mathf.Abs(v) > 1e-4f ? 1f / v : 1f;
    }
}
