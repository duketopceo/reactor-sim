using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Small code-built uGUI kit (legacy UnityEngine.UI.Text, built-in font) for the menu and the escape menu.
/// No TextMeshPro and no scene-serialized references, so the screens render even if TMP resources are missing.
/// </summary>
public static class RuntimeUi
{
    private static Font font;

    public static Font DefaultFont
    {
        get
        {
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (Exception) { }
                if (font == null)
                {
                    try { font = Font.CreateDynamicFontFromOSFont("Arial", 16); } catch (Exception) { }
                }
            }
            return font;
        }
    }

    public static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    public static Canvas CreateCanvas(string name, int sortingOrder, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
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

    /// <summary>A centred vertical column, width fixed, height fitted to its children.</summary>
    public static RectTransform Column(Transform parent, string name, float width, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, 100f);
        go.GetComponent<Image>().color = color;
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(36, 36, 30, 30);
        v.spacing = 14f;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rt;
    }

    public static Text MakeLabel(Transform parent, string text, int size, TextAnchor anchor, Color color, float height = -1f)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = DefaultFont;
        t.text = text;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height > 0f ? height : size * 1.5f;
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
        var colors = btn.colors;
        colors = ThemeColors.ButtonColors(colors);
        btn.colors = colors;
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        go.AddComponent<LayoutElement>().preferredHeight = height;
        var t = MakeLabel(go.transform, text, 28, TextAnchor.MiddleCenter, ThemeColors.TextBody);
        var rt = (RectTransform)t.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        DestroyObj(t.GetComponent<LayoutElement>());
        return btn;
    }

    public static Slider MakeSlider(Transform parent, float value, Action<float> onChanged)
    {
        var go = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().preferredHeight = 32f;

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

    public static Toggle MakeToggle(Transform parent, string text, bool isOn, Action<bool> onChanged)
    {
        var go = new GameObject("Toggle_" + text, typeof(RectTransform), typeof(Toggle));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().preferredHeight = 40f;

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

        var label = MakeLabel(go.transform, text, 26, TextAnchor.MiddleLeft, ThemeColors.TextBody);
        DestroyObj(label.GetComponent<LayoutElement>());
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

    private static void DestroyObj(UnityEngine.Object o)
    {
        if (o != null) UnityEngine.Object.Destroy(o);
    }
}
