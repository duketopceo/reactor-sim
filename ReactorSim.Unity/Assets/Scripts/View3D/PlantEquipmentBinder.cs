using UnityEngine;
using ReactorSim;
using ReactorSim.Instruments;

// Phase 2 plant equipment binder (N4): drives hall and containment-side motion from PlantSnapshot.
// Flywheel_PrimaryFlow spins with PrimaryFlowFrac, the spent-fuel racks glow with power (RackGlow_* objects: cells, haze cones
// and one shared point light, all through GlowMapping from the power FRACTION), PORV and SG relief steam anchors activate on ReliefOpen / SecondaryReliefOpen, the bypass anchor on DumpSteamKW (see HallSteamModel); each relief line also has a sparger anchor in view of the window whose bubble plume follows the same signal,
// RPV CRDM rods track BankInsertion. The rack glow is a labelled visual cue, not a physics claim (see GlowMapping).
public class PlantEquipmentBinder : MonoBehaviour
{
    public PlantBridge bridge;
    private ReactorAssetMotion reactorMotion;

    private Transform flywheel;
    private Material rackCellMat;
    private Material rackHazeMat;
    private readonly System.Collections.Generic.List<Light> rackLights = new System.Collections.Generic.List<Light>();
    private readonly System.Collections.Generic.List<float> rackLightBase = new System.Collections.Generic.List<float>();
    private static readonly Color RackCellColour = new Color(0.25f, 0.55f, 1f);
    private static readonly Color RackHazeColour = new Color(0.30f, 0.60f, 1f);

    /// <summary>Capture/diagnostics only: forces the power fraction the glow follows (null = follow the plant).</summary>
    public static double? PowerOverride { get; set; }
    /// <summary>Capture only: switches the rack emission, haze and lights fully off (a reference frame), ignoring the dim floor.</summary>
    public static bool GlowOff { get; set; }

    // With Domain Reload disabled (Enter Play Mode options) statics survive from one play session to the next, so a capture pose
    // forced in one run would leak into the next. Reset them before the first scene loads.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        PowerOverride = null;
        GlowOff = false;
    }

    private readonly System.Collections.Generic.List<ParticleSystem> primaryReliefJets = new System.Collections.Generic.List<ParticleSystem>();
    private readonly System.Collections.Generic.List<ParticleSystem> secondaryReliefJets = new System.Collections.Generic.List<ParticleSystem>();
    private readonly System.Collections.Generic.List<ParticleSystem> bypassJets = new System.Collections.Generic.List<ParticleSystem>();
    // Submerged discharge: a bubble plume off each relief line's sparger (ReliefAnchors.Sparger*), in view of the observation window.
    private readonly System.Collections.Generic.List<ParticleSystem> primaryPlumes = new System.Collections.Generic.List<ParticleSystem>();
    private readonly System.Collections.Generic.List<ParticleSystem> secondaryPlumes = new System.Collections.Generic.List<ParticleSystem>();
    private readonly System.Collections.Generic.List<ParticleSystem> bypassPlumes = new System.Collections.Generic.List<ParticleSystem>();
    private Texture2D softTexture;

    private void Awake()
    {
        if (bridge == null)
            bridge = FindAnyObjectByType<PlantBridge>();
        BindTransforms();
        reactorMotion = GetComponent<ReactorAssetMotion>();
        if (reactorMotion != null) reactorMotion.Bind();
    }

    private readonly System.Collections.Generic.List<Material> jetMaterials = new System.Collections.Generic.List<Material>();

    private void OnDestroy()
    {
        foreach (var material in jetMaterials) if (material != null) Destroy(material);
        jetMaterials.Clear();
        if (softTexture != null) Destroy(softTexture);
    }

    private Material SoftMaterial(string name, Color tint)
    {
        if (softTexture == null) softTexture = ReliefParticles.MakeSoftTexture("ReliefSoftSprite");
        var m = ReliefParticles.MakeMaterial(name, softTexture);
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", tint);
        jetMaterials.Add(m);
        return m;
    }

    private ParticleSystem MakeSteamJet(GameObject parent)
    {
        // Runtime-built particle jet so no .prefab asset is needed for the VFX. Soft round additive sprites that grow and fade.
        var go = new GameObject(parent.name + "_Steam");
        go.layer = parent.layer;
        go.transform.SetParent(parent.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.startLifetime = 0.9f;
        main.startSpeed = 2.2f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.3f);
        main.startColor = new Color(0.85f, 0.93f, 1f, 0.8f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        var grow = ps.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.6f));
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = ReliefParticles.Fade(Color.white);
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = SoftMaterial("SteamJetMaterial", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        return ps;
    }

    // Bubble plume that climbs from a sparger: soft round bubbles that rise, widen a little and fade. PORV: big, dense; SG relief:
    // medium; bypass: fine and wide (its rate follows the dump flow). Position, not colour, tells the three apart; the sizes help.
    private enum Plume { Primary, Secondary, Bypass }

    private ParticleSystem MakePlume(GameObject parent, Plume kind)
    {
        var go = new GameObject(parent.name + "_Plume");
        go.layer = parent.layer;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, 0.12f);          // anchor +z is up: leave from the top of the sparger pipe
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        float lo = kind == Plume.Primary ? 0.10f : kind == Plume.Secondary ? 0.09f : 0.06f;
        float hi = kind == Plume.Primary ? 0.22f : kind == Plume.Secondary ? 0.19f : 0.13f;
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3.0f, 4.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(lo, hi);
        main.startColor = new Color(0.82f, 0.95f, 1f, 0.8f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 400;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = kind == Plume.Bypass ? 9f : 5f;
        shape.radius = kind == Plume.Primary ? 0.16f : kind == Plume.Secondary ? 0.13f : 0.2f;
        var spread = ps.velocityOverLifetime;                              // gentle sideways wobble so it reads as bubbles, not a beam
        spread.enabled = true;
        spread.space = ParticleSystemSimulationSpace.Local;
        spread.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        spread.y = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        spread.z = new ParticleSystem.MinMaxCurve(0f);
        var grow = ps.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.8f, 1f, 1.5f));
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = ReliefParticles.Fade(Color.white);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = SoftMaterial("ReliefPlumeMaterial", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        return ps;
    }

    private void BindTransforms()
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Flywheel_PrimaryFlow" || t.name == "Flywheel")
                flywheel = t;
            else if (t.name.StartsWith("RackGlow_Cells_"))
            {
                var r = t.GetComponent<Renderer>();
                if (r != null && rackCellMat == null) rackCellMat = r.sharedMaterial;
            }
            else if (t.name.StartsWith("RackGlow_Haze_"))
            {
                var r = t.GetComponent<Renderer>();
                if (r != null && rackHazeMat == null) rackHazeMat = r.sharedMaterial;
            }
            else if (t.name.StartsWith("RackGlow_Light_"))
            {
                var l = t.GetComponent<Light>();
                if (l != null) { rackLights.Add(l); rackLightBase.Add(l.intensity); }
            }
            else if (t.name.StartsWith("VFX_PORV_SteamJet"))
                primaryReliefJets.Add(MakeSteamJet(t.gameObject));
            else if (t.name.StartsWith("VFX_SGRelief_SteamJet"))
                secondaryReliefJets.Add(MakeSteamJet(t.gameObject));
            else if (t.name.StartsWith("VFX_Bypass_SteamJet"))
                bypassJets.Add(MakeSteamJet(t.gameObject));
            else if (t.name.StartsWith("VFX_PORV_Sparger"))
                primaryPlumes.Add(MakePlume(t.gameObject, Plume.Primary));
            else if (t.name.StartsWith("VFX_SGRelief_Sparger"))
                secondaryPlumes.Add(MakePlume(t.gameObject, Plume.Secondary));
            else if (t.name.StartsWith("VFX_Bypass_Sparger"))
                bypassPlumes.Add(MakePlume(t.gameObject, Plume.Bypass));
        }
    }

    private void Update()
    {
        if (bridge == null || bridge.Sim == null) return;
        PlantSnapshot s = bridge.GetSnapshot();

        if (flywheel != null)
        {
            float speed = (float)PlantWallMapModel.RotorSpeed(false, s) * 360f;   // Clamp01 passes NaN through, which would poison the transform
            flywheel.Rotate(0f, 0f, -speed * Time.deltaTime);
        }
        // Power is a fraction of rated (1.0 = rated); GlowMapping turns it into emission, haze and light, with a dim floor at 0.
        double power = PowerOverride.HasValue ? PowerOverride.Value : s.Power;
        if (reactorMotion != null) reactorMotion.Apply(s, power, GlowOff, Time.deltaTime);
        float shimmer = GlowOff ? 0f : 0.94f + 0.06f * Mathf.Sin(Time.time * 2.3f);
        if (rackCellMat != null && rackCellMat.HasProperty("_EmissionColor"))
            rackCellMat.SetColor("_EmissionColor", RackCellColour * ((float)GlowMapping.RackEmission(power) * shimmer));
        if (rackHazeMat != null && rackHazeMat.HasProperty("_TintColor"))
        {
            Color c = RackHazeColour;
            c.a = (float)GlowMapping.Haze(power) * shimmer;
            rackHazeMat.SetColor("_TintColor", c);
        }
        float lightScale = (float)GlowMapping.LightScale(power) * shimmer;
        for (int i = 0; i < rackLights.Count; i++)
            if (rackLights[i] != null) rackLights[i].intensity = rackLightBase[i] * lightScale;
        HallSteamModel steam = HallSteamModel.From(s);
        ApplyReliefState(steam.PrimaryRelief, steam.SecondaryRelief);
        ApplyBypass(steam.Bypass);
    }

    /// <summary>Bypass (steam dump) jet at 0..1 intensity from DumpSteamKW; independent of the turbine and of both reliefs.</summary>
    public void ApplyBypass(float intensity)
    {
        foreach (var plume in bypassPlumes)
        {
            if (plume == null) continue;
            bool plumeOn = intensity > 0f;
            var plumeEmission = plume.emission;
            plumeEmission.rateOverTime = plumeOn ? 40f + 90f * Mathf.Clamp01(intensity) : 0f;
            if (plumeOn && !plume.isPlaying) plume.Play();
            else if (!plumeOn && plume.isPlaying) plume.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        foreach (var jet in bypassJets)
        {
            if (jet == null) continue;
            bool on = intensity > 0f;
            var emission = jet.emission;
            emission.rateOverTime = on ? 60f * Mathf.Clamp01(intensity) : 0f;
            if (on && !jet.isPlaying) jet.Play();
            else if (!on && jet.isPlaying) jet.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    // Kept separate so each authored relief path can be checked independently in play mode.
    public void ApplyReliefState(bool primaryOpen, bool secondaryOpen)
    {
        SetReliefJets(primaryReliefJets, primaryOpen);
        SetReliefJets(secondaryReliefJets, secondaryOpen);
        SetPlumes(primaryPlumes, primaryOpen, 90f);
        SetPlumes(secondaryPlumes, secondaryOpen, 80f);
    }

    private static void SetPlumes(System.Collections.Generic.List<ParticleSystem> plumes, bool open, float rate)
    {
        foreach (var plume in plumes)
        {
            if (plume == null) continue;
            var emission = plume.emission;
            emission.rateOverTime = open ? rate : 0f;
            if (open && !plume.isPlaying) plume.Play();
            else if (!open && plume.isPlaying) plume.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private static void SetReliefJets(System.Collections.Generic.List<ParticleSystem> jets, bool open)
    {
        foreach (var jet in jets)
        {
            if (jet == null) continue;
            var emission = jet.emission;
            emission.rateOverTime = open ? 60f : 0f;
            if (open && !jet.isPlaying) jet.Play();
            else if (!open && jet.isPlaying) jet.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
