using ReactorSim;
using UnityEngine;

// Ambient suspended bubbles give the underwater gallery depth. Their rate is a visual cue, not a boiling model.
public sealed class ReactorWaterAtmosphere : MonoBehaviour
{
    public PlantBridge bridge;
    private ParticleSystem bubbles;
    private Material particleMaterial;
    private Texture2D particleTexture;

    private void Awake()
    {
        if (bridge == null) bridge = FindAnyObjectByType<PlantBridge>();
        var go = new GameObject("Pool_VisibleBubbles");
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, HallDimensions.VesselBase + 3.8f, -2.5f);
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
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
        main.startColor = ThemeColors.WithAlpha(ThemeColors.StatusNormal, 0.45f);
        main.maxParticles = 400;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var shape = bubbles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(6f, 2f, 3f);
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
        var shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader != null)
        {
            particleMaterial = new Material(shader) { name = "PoolBubbleMaterial", mainTexture = particleTexture };
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = particleMaterial;
        }
        bubbles.Play();
    }

    private void Update()
    {
        if (bubbles == null) return;
        PlantSnapshot s = bridge != null ? bridge.GetSnapshot() : null;
        float power = s == null || double.IsNaN(s.Power) || double.IsInfinity(s.Power) ? 0f : Mathf.Clamp01((float)s.Power);
        var emission = bubbles.emission;
        emission.rateOverTime = 20f + 25f * power;
    }

    private void OnDestroy()
    {
        if (particleMaterial != null) Destroy(particleMaterial);
        if (particleTexture != null) Destroy(particleTexture);
    }
}
