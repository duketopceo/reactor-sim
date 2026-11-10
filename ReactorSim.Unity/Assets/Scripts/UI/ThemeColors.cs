using UnityEngine;
using UnityEngine.UI;
using ReactorSim.Ui;

// Unity adapter for Theme (pure, tested): converts tokens to Color and builds IMGUI styles
// from them. UI scripts take every text and panel color from here.
public static class ThemeColors
{
    public static Color ToColor(Rgba c)
    {
        return new Color((float)c.R, (float)c.G, (float)c.B, (float)c.A);
    }

    public static Color SurfaceScreen { get { return ToColor(Theme.SurfaceScreen); } }
    public static Color SurfacePanel { get { return ToColor(Theme.SurfacePanel); } }
    public static Color SurfaceRow { get { return ToColor(Theme.SurfaceRow); } }
    public static Color SurfacePaper { get { return ToColor(Theme.SurfacePaper); } }
    public static Color SurfaceTooltip { get { return ToColor(Theme.SurfaceTooltip); } }
    public static Color TextBody { get { return ToColor(Theme.TextBody); } }
    public static Color TextDim { get { return ToColor(Theme.TextDim); } }
    public static Color TextHeading { get { return ToColor(Theme.TextHeading); } }
    public static Color TextInkOnPaper { get { return ToColor(Theme.TextInkOnPaper); } }
    public static Color TextPaperCurrent { get { return ToColor(Theme.TextPaperCurrent); } }
    public static Color TextPaperDone { get { return ToColor(Theme.TextPaperDone); } }
    public static Color StatusNormal { get { return ToColor(Theme.StatusNormal); } }
    public static Color StatusOk { get { return ToColor(Theme.StatusOk); } }
    public static Color StatusCaution { get { return ToColor(Theme.StatusCaution); } }
    public static Color StatusCritical { get { return ToColor(Theme.StatusCritical); } }
    public static Color ButtonOff { get { return ToColor(Theme.ButtonOff); } }
    public static Color ButtonOn { get { return ToColor(Theme.ButtonOn); } }
    public static Color ButtonBorder { get { return ToColor(Theme.ButtonBorder); } }
    public static Color ButtonHover { get { return ToColor(Theme.ButtonHover); } }
    public static Color ButtonPressed { get { return ToColor(Theme.ButtonPressed); } }
    public static Color ScrimDim { get { return ToColor(Theme.ScrimDim); } }
    public static Color CameraBackground { get { return ToColor(Theme.CameraBackground); } }
    public static Color DeviceBezel { get { return ToColor(Theme.DeviceBezel); } }
    public static Color HighlightPulse { get { return ToColor(Theme.HighlightPulse); } }

    /// <summary>Identity multiplier. Button ColorTint multiplies the graphic color, so a button whose block colors are
    /// theme tokens keeps its Image at this tint.</summary>
    public static Color Tint { get { return Color.white; } }

    /// <summary>Button color block whose normal/hover/pressed states are the theme button tokens (use with an Image tinted Tint).</summary>
    public static ColorBlock ButtonColors(ColorBlock b)
    {
        b.normalColor = ButtonOff;
        b.highlightedColor = ButtonHover;
        b.selectedColor = ButtonHover;
        b.pressedColor = ButtonPressed;
        b.disabledColor = ButtonOff;
        b.colorMultiplier = 1f;
        return b;
    }

    /// <summary>For buttons whose Image color is set per state by code (tabs, message rows): hover and press only
    /// darken the image, which can only raise light-text contrast.</summary>
    public static ColorBlock DarkenOnInteract(ColorBlock b)
    {
        b.normalColor = Color.white;
        b.highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        b.selectedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        b.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        b.disabledColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        b.colorMultiplier = 1f;
        return b;
    }

    public static Color FillCritical { get { return ToColor(Theme.FillCritical); } }

    /// <summary>"#RRGGBB" for IMGUI rich-text color tags.</summary>
    public static string Hex(Rgba c)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(ToColor(c));
    }

    /// <summary>The same color with a different alpha (animation, fades).</summary>
    public static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }
    public static Color CrtPhosphor { get { return ToColor(Theme.CrtPhosphor); } }

    /// <summary>IMGUI label style with a themed text color.</summary>
    public static GUIStyle LabelStyle(int size, Rgba text, bool bold = false)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = size, wordWrap = true, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal };
        s.normal.textColor = ToColor(text);
        return s;
    }

    private static readonly System.Collections.Generic.Dictionary<Rgba, Texture2D> surfaceTextures =
        new System.Collections.Generic.Dictionary<Rgba, Texture2D>();

    /// <summary>1x1 texture of one surface token (alpha included), built once and cached.</summary>
    public static Texture2D SurfaceTexture(Rgba surface)
    {
        Texture2D tex;
        if (surfaceTextures.TryGetValue(surface, out tex) && tex != null) return tex;
        tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, ToColor(surface));
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        surfaceTextures[surface] = tex;
        return tex;
    }

    /// <summary>IMGUI box style on a themed backing surface: a 1x1 texture of the surface token, alpha included.</summary>
    public static GUIStyle BoxStyle(Rgba surface, int size, Rgba text)
    {
        var s = new GUIStyle(GUI.skin.box) { fontSize = size, wordWrap = true, alignment = TextAnchor.UpperLeft };
        s.normal.background = SurfaceTexture(surface);
        s.normal.textColor = ToColor(text);
        return s;
    }
}
