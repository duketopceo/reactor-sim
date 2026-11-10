using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using ReactorSim.Ui;

/// <summary>
/// The SDF font assets TmpBootstrap generates from the open-licensed font files in Assets/Fonts, loaded by name.
/// Body is the proportional face (Inter), Bold its bold weight, Mono the monospace face (IBM Plex Mono) for numerics.
/// A missing asset falls back to the TMP default font so a screen still draws.
/// </summary>
public static class UiFonts
{
    public const string BodyName = "Inter Regular SDF";
    public const string BoldName = "Inter Bold SDF";
    public const string MonoName = "IBM Plex Mono Regular SDF";
    /// <summary>Resources-relative folder of the generated assets (under Assets/TextMesh Pro/Resources).</summary>
    public const string ResourcesFolder = "Fonts & Materials/";

    private static TMP_FontAsset body, bold, mono;

    public static TMP_FontAsset Body { get { return Load(ref body, BodyName); } }
    public static TMP_FontAsset Bold { get { return Load(ref bold, BoldName); } }
    public static TMP_FontAsset Mono { get { return Load(ref mono, MonoName); } }

    public static TMP_FontAsset For(TextRole role)
    {
        return role == TextRole.Numeric ? Mono : Body;
    }

    private static TMP_FontAsset Load(ref TMP_FontAsset slot, string name)
    {
        if (slot != null) return slot;
        slot = Resources.Load<TMP_FontAsset>(ResourcesFolder + name);
        if (slot == null)
        {
            Debug.LogWarning("[UiFonts] " + name + " missing (run TmpBootstrap.GenerateFonts); using the TMP default font");
            slot = TMP_Settings.defaultFontAsset;
        }
        return slot;
    }
}

/// <summary>Keeps one TMP label on the type ramp: re-applies its size when the text-scale setting changes.</summary>
public sealed class ThemedText : MonoBehaviour
{
    public TextRole role = TextRole.Body;
    /// <summary>False for a panel that already follows the text-scale setting some other way (the tablet's canvas scaler).</summary>
    public bool followTextScale = true;
    private TMP_Text text;
    private int appliedVersion = -1;

    private void OnEnable() { Apply(true); }

    public void Reapply() { Apply(true); }

    private void Update() { Apply(false); }

    private void Apply(bool force)
    {
        if (!force && appliedVersion == UserSettings.Version) return;
        if (text == null) text = GetComponent<TMP_Text>();
        if (text == null) return;
        appliedVersion = UserSettings.Version;
        text.fontSize = followTextScale ? RuntimeUi.FontSize(role) : Theme.RampSize(role);
    }
}

/// <summary>Keeps a panel's width at its design width times the text scale, never wider than the screen.</summary>
public sealed class ThemedPanelWidth : MonoBehaviour
{
    public float designWidth = 600f;
    private int appliedVersion = -1;
    private Vector2 appliedScreen;

    /// <summary>Changes the design width (compact versus expanded panels) and re-fits at once.</summary>
    public void SetDesignWidth(float width)
    {
        designWidth = width;
        Apply();
    }

    private void OnEnable() { Apply(); }

    // Children are added after the panel exists, so measure the content again once it is all there.
    private void Start() { Apply(); }

    private void Update()
    {
        if (appliedVersion != UserSettings.Version || appliedScreen.x != Screen.width || appliedScreen.y != Screen.height) Apply();
    }

    private void Apply()
    {
        var rt = transform as RectTransform;
        if (rt == null) return;
        appliedVersion = UserSettings.Version;
        appliedScreen = new Vector2(Screen.width, Screen.height);
        double canvasWidth = Theme.CanvasWidth(Screen.width, Screen.height);
        var size = rt.sizeDelta;
        size.x = (float)Theme.PanelWidth(designWidth, UserSettings.TextScale, canvasWidth);
        rt.sizeDelta = size;
        // Keep the panel on screen vertically too: shrink it uniformly when its content outgrew the canvas height.
        rt.localScale = Vector3.one;
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        float height = LayoutUtility.GetPreferredHeight(rt);
        rt.localScale = Vector3.one * (float)Theme.FitScale(height);
    }
}

/// <summary>Shows the theme focus ring on the control's target graphic while it is selected (keyboard or gamepad).</summary>
public sealed class FocusOutline : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    private Outline ring;

    public void OnSelect(BaseEventData e)
    {
        if (ring == null)
        {
            var sel = GetComponent<Selectable>();
            if (sel == null || sel.targetGraphic == null) return;
            ring = sel.targetGraphic.gameObject.AddComponent<Outline>();
            ring.effectColor = ThemeColors.FocusRing;
            ring.effectDistance = new Vector2(Theme.Space1 * 0.5f, -Theme.Space1 * 0.5f);
        }
        ring.enabled = true;
    }

    public void OnDeselect(BaseEventData e)
    {
        if (ring != null) ring.enabled = false;
    }
}

/// <summary>
/// Small code-built uGUI kit on TextMeshPro: one canvas scaled by screen height from the Theme reference resolution,
/// text on the Theme type ramp (scaled by the text-scale setting) using the generated font assets, panels with a header
/// band, dividers and focus states. No scene-serialized references and no hand-authored prefabs (KTD6).
/// </summary>
public static class RuntimeUi
{
    /// <summary>Ramp size for a role at the current text scale, in canvas (reference) pixels.</summary>
    public static float FontSize(TextRole role)
    {
        return Theme.ScaledFont(Theme.RampSize(role), UserSettings.TextScale);
    }

    public static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    /// <summary>Overlay canvas that scales with screen HEIGHT from Theme.ReferenceWidth x ReferenceHeight.</summary>
    public static Canvas CreateCanvas(string name, int sortingOrder, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(Theme.ReferenceWidth, Theme.ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;   // by height
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    public static RectTransform Stretch(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = color;
        return rt;
    }

    /// <summary>A vertical panel of fixed design width, height fitted to its children, placed at an anchor.</summary>
    public static RectTransform Panel(Transform parent, string name, float width, Color color, Vector2 anchor, Vector2 pivot,
        RectOffset padding, float spacing)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = new Vector2(width, 100f);
        go.GetComponent<Image>().color = color;
        go.AddComponent<ThemedPanelWidth>().designWidth = width;
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.padding = padding;
        v.spacing = spacing;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rt;
    }

    /// <summary>A centred vertical column on the 12/24 spacing scale.</summary>
    public static RectTransform Column(Transform parent, string name, float width, Color color)
    {
        return Panel(parent, name, width, color, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new RectOffset(Theme.Space5, Theme.Space5, Theme.Space5, Theme.Space5), Theme.Space3);
    }

    /// <summary>Header band across a panel: a heading on the header surface. Add it first in the panel.</summary>
    public static TMP_Text MakeHeader(Transform parent, string text, TextRole role = TextRole.Title)
    {
        var go = new GameObject("Header", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = ThemeColors.SurfaceHeader;
        img.raycastTarget = false;
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(Theme.Space4, Theme.Space4, Theme.Space2, Theme.Space2);
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = false;
        var t = MakeLabel(go.transform, text, role, TextAnchor.MiddleCenter, ThemeColors.TextHeading);
        t.fontStyle = FontStyles.Bold;
        return t;
    }

    /// <summary>A hairline between panel sections.</summary>
    public static void MakeDivider(Transform parent)
    {
        var go = new GameObject("Divider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = ThemeColors.Divider;
        img.raycastTarget = false;
        var le = go.GetComponent<LayoutElement>();
        le.minHeight = 2f;
        le.preferredHeight = 2f;
    }

    /// <summary>
    /// A small notice box on the tooltip surface pinned to a screen corner or edge (pivot = anchor). fixedWidth 0 sizes the
    /// box to its text on one line; a positive width wraps the text to it. Pure overlay: it never takes a click.
    /// </summary>
    public static RectTransform MakeToast(Transform parent, string name, Vector2 anchor, Vector2 position, float fixedWidth,
        TextRole role, TextAnchor align, Color textColor, out TMP_Text text)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = position;
        var img = go.GetComponent<Image>();
        img.color = ThemeColors.SurfaceTooltip;
        img.raycastTarget = false;
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(Theme.Space3, Theme.Space3, Theme.Space1, Theme.Space1);
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        var fit = go.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = fixedWidth > 0f ? ContentSizeFitter.FitMode.Unconstrained : ContentSizeFitter.FitMode.PreferredSize;
        if (fixedWidth > 0f) rt.sizeDelta = new Vector2(fixedWidth, 0f);
        text = MakeLabel(go.transform, "", role, align, textColor);
        text.textWrappingMode = fixedWidth > 0f ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        return rt;
    }

    public static void MakeSpacer(Transform parent, float height)
    {
        var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
    }

    private static TextAlignmentOptions Align(TextAnchor a)
    {
        switch (a)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
            case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
            default: return TextAlignmentOptions.Left;
        }
    }

    /// <summary>TMP label on the type ramp. minHeight (reference px) is a floor; the text can grow past it.</summary>
    public static TMP_Text MakeLabel(Transform parent, string text, TextRole role, TextAnchor anchor, Color color, float minHeight = -1f)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.font = UiFonts.For(role);
        t.text = text;
        t.fontSize = FontSize(role);
        t.alignment = Align(anchor);
        t.color = color;
        t.richText = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        go.AddComponent<ThemedText>().role = role;
        if (minHeight > 0f) go.AddComponent<LayoutElement>().minHeight = minHeight;
        return t;
    }

    public static Button MakeButton(Transform parent, string text, Action onClick, float height = 56f)
    {
        var go = new GameObject("Button_" + text, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = ThemeColors.Tint;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.colors = ThemeColors.ButtonColors(btn.colors);
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        go.AddComponent<FocusOutline>();
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        var t = MakeLabel(go.transform, text, TextRole.Heading, TextAnchor.MiddleCenter, ThemeColors.TextBody);
        var rt = (RectTransform)t.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(Theme.Space2, 0f);
        rt.offsetMax = new Vector2(-Theme.Space2, 0f);
        return btn;
    }

    public static Slider MakeSlider(Transform parent, float value, Action<float> onChanged)
    {
        var go = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().preferredHeight = 32f;
        go.AddComponent<FocusOutline>();

        var bg = ImageChild(go.transform, "Background", ThemeColors.ButtonOff);
        bg.anchorMin = new Vector2(0f, 0.3f);
        bg.anchorMax = new Vector2(1f, 0.7f);
        bg.offsetMin = bg.offsetMax = Vector2.zero;

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        var fa = (RectTransform)fillArea.transform;
        fa.anchorMin = new Vector2(0f, 0.3f);
        fa.anchorMax = new Vector2(1f, 0.7f);
        fa.offsetMin = new Vector2(8f, 0f);
        fa.offsetMax = new Vector2(-8f, 0f);
        var fill = ImageChild(fillArea.transform, "Fill", ThemeColors.StatusNormal);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0f, 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        var ha = (RectTransform)handleArea.transform;
        ha.anchorMin = Vector2.zero;
        ha.anchorMax = Vector2.one;
        ha.offsetMin = new Vector2(10f, 0f);
        ha.offsetMax = new Vector2(-10f, 0f);
        var handle = ImageChild(handleArea.transform, "Handle", ThemeColors.TextHeading);
        handle.anchorMin = new Vector2(0f, 0f);
        handle.anchorMax = new Vector2(0f, 1f);
        handle.sizeDelta = new Vector2(20f, 0f);

        var s = go.GetComponent<Slider>();
        s.fillRect = fill;
        s.handleRect = handle;
        s.targetGraphic = handle.GetComponent<Image>();
        s.direction = Slider.Direction.LeftToRight;
        s.minValue = 0f;
        s.maxValue = 1f;
        s.SetValueWithoutNotify(value);
        if (onChanged != null) s.onValueChanged.AddListener(v => onChanged(v));
        return s;
    }

    /// <summary>Vertical scroll area of fixed design height. Returns the content column to add rows to.</summary>
    public static RectTransform MakeScrollView(Transform parent, float height)
    {
        var go = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;

        var vp = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        vp.transform.SetParent(go.transform, false);
        var vrt = (RectTransform)vp.transform;
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = vrt.offsetMax = Vector2.zero;

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(vp.transform, false);
        var crt = (RectTransform)content.transform;
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.offsetMin = new Vector2(0f, 0f);
        crt.offsetMax = new Vector2(0f, 0f);
        var v = content.GetComponent<VerticalLayoutGroup>();
        v.spacing = Theme.Space1;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var sr = go.GetComponent<ScrollRect>();
        sr.viewport = vrt;
        sr.content = crt;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 40f;
        return crt;
    }

    public static Toggle MakeToggle(Transform parent, string text, bool isOn, Action<bool> onChanged, Color? textColor = null)
    {
        var go = new GameObject("Toggle_" + text, typeof(RectTransform), typeof(Toggle));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 44f;
        le.preferredHeight = 44f;
        go.AddComponent<FocusOutline>();

        var box = ImageChild(go.transform, "Box", ThemeColors.ButtonOff);
        box.anchorMin = box.anchorMax = new Vector2(0f, 0.5f);
        box.pivot = new Vector2(0f, 0.5f);
        box.sizeDelta = new Vector2(32f, 32f);
        box.anchoredPosition = Vector2.zero;
        var check = ImageChild(box, "Check", ThemeColors.StatusOk);
        check.anchorMin = Vector2.zero;
        check.anchorMax = Vector2.one;
        check.offsetMin = new Vector2(6f, 6f);
        check.offsetMax = new Vector2(-6f, -6f);

        var label = MakeLabel(go.transform, text, TextRole.Body, TextAnchor.MiddleLeft, textColor ?? ThemeColors.TextBody);
        var lrt = (RectTransform)label.transform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(46f, 0f);
        lrt.offsetMax = Vector2.zero;

        var t = go.GetComponent<Toggle>();
        t.targetGraphic = box.GetComponent<Image>();
        t.graphic = check.GetComponent<Image>();
        t.SetIsOnWithoutNotify(isOn);
        if (onChanged != null) t.onValueChanged.AddListener(v => onChanged(v));
        return t;
    }

    private static RectTransform ImageChild(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        return (RectTransform)go.transform;
    }
}
