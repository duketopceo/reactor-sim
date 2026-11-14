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

    private void Awake()
    {
        if (bridge == null) bridge = FindAnyObjectByType<PlantBridge>();
        var go = new GameObject("Pool_VisibleBubbles");
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, HallDimensions.VesselBase + 3.8f, HallDimensions.ModuleZ - 2.9f);   // in the water between the glass and the module
        bubbles = go.AddComponent<ParticleSystem>();
        bubbles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        bubbles.useAutoRandomSeed = false;
        bubbles.randomSeed = 2718;
        var main = bubbles.main;
        main.loop = true;
        main.duration = 12f;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
        main.startColor = ThemeColors.WithAlpha(ThemeColors.StatusNormal, 0.45f);
        main.maxParticles = 400;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var shape = bubbles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(4f, 2.5f, 0.8f);
        var velocity = bubbles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.y = 0.65f;
        var emission = bubbles.emission;
        emission.rateOverTime = 20f;

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
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = particleMaterial;
        }
        bubbles.Play();
        BuildHalo(shader);
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

    /// <summary>Test and render hook: the halo brightness for a power fraction (the same call Update makes).</summary>
    public void ApplyPower(float power) { SetHalo(power); }

    private void Update()
    {
        if (bubbles == null) return;
        PlantSnapshot s = bridge != null ? bridge.GetSnapshot() : null;
        float power = s == null || double.IsNaN(s.Power) || double.IsInfinity(s.Power) ? 0f : Mathf.Clamp01((float)s.Power);
        var emission = bubbles.emission;
        emission.rateOverTime = 20f + 25f * power;
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
