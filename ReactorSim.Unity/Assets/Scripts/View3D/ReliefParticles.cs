using UnityEngine;

// Particle looks for the relief and bypass visuals, built at runtime so no texture or material asset is needed (same approach as the
// pool bubbles in ReactorWaterAtmosphere). Everything is a soft round sprite on the additive particle shader (in
// InstrumentCheck.RequiredShaders): the old jets used an opaque square quad ("Unlit/Color"), which read as blocks.
public static class ReliefParticles
{
    /// <summary>A 32 px round sprite: bright core, soft edge, alpha 0 at the rim.</summary>
    public static Texture2D MakeSoftTexture(string name)
    {
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x - (n - 1) * 0.5f) / ((n - 1) * 0.5f), dy = (y - (n - 1) * 0.5f) / ((n - 1) * 0.5f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float falloff = Mathf.Clamp01(1f - d);
                float a = falloff * falloff * (3f - 2f * falloff);          // smoothstep: no hard edge anywhere
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
    }

    /// <summary>Additive particle material over the soft sprite (falls back to Sprites/Default, which every build carries).</summary>
    public static Material MakeMaterial(string name, Texture2D tex)
    {
        Shader shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader) { name = name, mainTexture = tex };
        if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        return mat;
    }

    /// <summary>Colour over lifetime: fade in quickly, hold, fade out, so a particle never pops in or out.</summary>
    public static ParticleSystem.MinMaxGradient Fade(Color c)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
        return new ParticleSystem.MinMaxGradient(g);
    }
}
