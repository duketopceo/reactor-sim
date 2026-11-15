using ReactorSim;
using ReactorSim.Instruments;
using UnityEngine;

// Ambient suspended bubbles and a soft core halo give the underwater window depth. Both are visual cues, not a boiling or
// radiation model: the bubble rate and the halo brightness follow power (GlowMapping), nothing feeds back into the sim.
public sealed class ReactorWaterAtmosphere : MonoBehaviour
{
    public PlantBridge bridge;
    private ParticleSystem bubbles;
    private Material particleMaterial;
    private Texture2D particleTexture;
    private Material haloMaterial;
    private Texture2D haloTexture;
    private Mesh haloMesh;

    // Faint, sparse bubble streams rise at the sides of the pane, clear of the fuel array in the middle of the window.
    // Offsets are hall-space x and z from the anchor: x clear of the fuel footprint, z a little either side of it. Rate scales per stream.
    private static readonly Vector3[] StreamOffsets =
    {
        new Vector3(-2.05f, 0f, 0.00f), new Vector3(-1.35f, 0f, 0.35f), new Vector3(1.45f, 0f, -0.15f), new Vector3(2.15f, 0f, 0.30f),
    };
    private static readonly float[] StreamRateScale = { 1f, 0.7f, 0.85f, 0.6f };
    private const float BaseRate = 0.9f;     // per stream, bubbles per second at zero power (decay heat keeps a trickle)
    private const float PowerRate = 1.8f;    // extra per stream at rated power
    private const float BubbleAlpha = 0.4f;
    private readonly System.Collections.Generic.List<ParticleSystem> streams = new System.Collections.Generic.List<ParticleSystem>();

    private void Awake()
    {
        if (bridge == null) bridge = FindAnyObjectByType<PlantBridge>();
        var go = new GameObject("Pool_VisibleBubbles");
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);
        // The anchor sits low in the water between the glass and the module; bubbles rise through the fuel band.
        go.transform.localPosition = new Vector3(0f, HallDimensions.VesselBase + 2.4f, HallDimensions.ModuleZ - 2.9f);
        for (int i = 0; i < StreamOffsets.Length; i++)
        {
            // The first stream is the anchor object itself; the others are children offset from it.
            GameObject host = go;
            if (i > 0)
            {
                host = new GameObject("Pool_BubbleStream" + i);
                host.layer = gameObject.layer;
                host.transform.SetParent(go.transform, false);
            }
            if (i == 0) go.transform.localPosition += StreamOffsets[0];
            else host.transform.localPosition = StreamOffsets[i] - StreamOffsets[0];
            streams.Add(ConfigureStream(host, 2718 + i * 31));
        }
        bubbles = streams[0];

        particleTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "PoolBubbleTexture" };
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            float dx = (x - 15.5f) / 15.5f, dy = (y - 15.5f) / 15.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float alpha = Mathf.Clamp01((1f - d) * 5f) * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(d));
            particleTexture.SetPixel(x, y, ThemeColors.WithAlpha(ThemeColors.TextBody, alpha));
        }
        particleTexture.Apply();
        // The additive particle shader is in Always Included Shaders (InstrumentCheck.RequiredShaders); fall back to the sprite
        // shader, which every build carries, so the bubbles never vanish silently if that list ever loses it.
        var shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader == null)
        {
            Debug.LogWarning("[ReactorWaterAtmosphere] additive particle shader missing from the build; using Sprites/Default");
            shader = Shader.Find("Sprites/Default");
        }
        if (shader != null)
        {
            particleMaterial = new Material(shader) { name = "PoolBubbleMaterial", mainTexture = particleTexture };
            foreach (var stream in streams) stream.GetComponent<ParticleSystemRenderer>().sharedMaterial = particleMaterial;
        }
        foreach (var stream in streams) stream.Play();
        BuildHalo(shader);
    }

    private static ParticleSystem ConfigureStream(GameObject host, int seed)
    {
        var ps = host.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.duration = 12f;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);   // mostly pin-prick, a few larger
        main.startColor = ThemeColors.WithAlpha(ThemeColors.StatusNormal, BubbleAlpha);
        main.maxParticles = 40;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        ps.useAutoRandomSeed = false;
        ps.randomSeed = (uint)seed;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.3f, 0.1f, 0.3f);
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        // The three velocity curves must share one mode, so x and z are zero-width ranges beside the y range.
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.28f, 0.5f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.06f;
        noise.frequency = 0.5f;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(ThemeColors.TextBody, 0f), new GradientColorKey(ThemeColors.TextBody, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        colour.color = fade;
        var emission = ps.emission;
        emission.rateOverTime = BaseRate;
        return ps;
    }

    /// <summary>Render hook: clears the streams and lets them run for the given seconds, so a still shows settled bubbles.</summary>
    public void Settle(float seconds)
    {
        foreach (var stream in streams)
        {
            if (stream == null) continue;
            stream.Clear(true);
            stream.Simulate(seconds, false, true);
        }
    }

    // A camera-facing-the-window quad of soft blue light just in front of the fuel: bloom without a real-time light or post-processing.
    private void BuildHalo(Shader shader)
    {
        if (shader == null) return;
        const int n = 64;
        haloTexture = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "CoreHaloTexture", wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float dx = (x - (n - 1) * 0.5f) / ((n - 1) * 0.5f), dy = (y - (n - 1) * 0.5f) / ((n - 1) * 0.5f);
            float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
            float a = Mathf.Pow(1f - d, 2.2f);
            haloTexture.SetPixel(x, y, ThemeColors.WithAlpha(ThemeColors.TextBody, a));
        }
        haloTexture.Apply();
        haloMesh = new Mesh { name = "CoreHaloQuad" };
        const float halfW = 3.2f, halfH = 4.2f;
        haloMesh.vertices = new[] { new Vector3(-halfW, -halfH, 0), new Vector3(halfW, -halfH, 0), new Vector3(halfW, halfH, 0), new Vector3(-halfW, halfH, 0) };
        haloMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        haloMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };   // faces -z, towards the window
        haloMesh.RecalculateBounds();
        var go = new GameObject("Pool_CoreHalo");
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, HallDimensions.VesselBase + 5.6f, HallDimensions.ModuleZ - 1.0f);
        go.AddComponent<MeshFilter>().sharedMesh = haloMesh;
        var r = go.AddComponent<MeshRenderer>();
        haloMaterial = new Material(shader) { name = "M_CoreHalo", mainTexture = haloTexture };
        r.sharedMaterial = haloMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        SetHalo(0f);
    }

    private void SetHalo(float power)
    {
        if (haloMaterial == null) return;
        // Quiet but not off at zero power (decay heat), clearly brighter at rated power.
        float level = (float)GlowMapping.Level(power);
        Color c = Color.Lerp(ThemeColors.ToColor(ReactorSim.Ui.Theme.CoreGlowCold), ThemeColors.ToColor(ReactorSim.Ui.Theme.CoreGlowHot), level)
            * (0.05f + 0.26f * level);
        c.a = 1f;
        if (haloMaterial.HasProperty("_TintColor")) haloMaterial.SetColor("_TintColor", c);
        if (haloMaterial.HasProperty("_Color")) haloMaterial.SetColor("_Color", c);
    }

    private void SetEmission(float power)
    {
        for (int i = 0; i < streams.Count; i++)
        {
            var emission = streams[i].emission;
            emission.rateOverTime = (BaseRate + PowerRate * power) * StreamRateScale[i];
        }
    }

    /// <summary>Test and render hook: the halo brightness and bubble rate for a power fraction (the same calls Update makes).</summary>
    public void ApplyPower(float power) { SetEmission(power); SetHalo(power); }

    private void Update()
    {
        if (bubbles == null) return;
        PlantSnapshot s = bridge != null ? bridge.GetSnapshot() : null;
        float power = s == null || double.IsNaN(s.Power) || double.IsInfinity(s.Power) ? 0f : Mathf.Clamp01((float)s.Power);
        SetEmission(power);
        SetHalo(power);
    }

    private void OnDestroy()
    {
        if (particleMaterial != null) Destroy(particleMaterial);
        if (particleTexture != null) Destroy(particleTexture);
        if (haloMaterial != null) Destroy(haloMaterial);
        if (haloTexture != null) Destroy(haloTexture);
        if (haloMesh != null) Destroy(haloMesh);
    }
}
