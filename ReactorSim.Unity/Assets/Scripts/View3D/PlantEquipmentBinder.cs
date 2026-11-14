using UnityEngine;
using ReactorSim;
using ReactorSim.Instruments;

// Phase 2 plant equipment binder (N4): drives hall and containment-side motion from PlantSnapshot.
// Flywheel_PrimaryFlow spins with PrimaryFlowFrac, the spent-fuel racks glow with power (RackGlow_* objects: cells, haze cones
// and one shared point light, all through GlowMapping from the power FRACTION), PORV steam anchor activates when ReliefOpen,
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

    private void Awake()
    {
        if (bridge == null)
            bridge = FindAnyObjectByType<PlantBridge>();
        BindTransforms();
        reactorMotion = GetComponent<ReactorAssetMotion>();
        if (reactorMotion != null) reactorMotion.Bind();
    }

    private static ParticleSystem MakeSteamJet(GameObject parent)
    {
        // Runtime-built particle jet so no .prefab asset is needed for the VFX.
        var go = new GameObject(parent.name + "_Steam");
        go.transform.SetParent(parent.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.startLifetime = 0.9f;
        main.startSpeed = 2.2f;
        main.startSize = 0.25f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.9f, 0.95f, 1f, 0.35f) };
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
            else if (t.name.StartsWith("VFX_SGRelief"))
                secondaryReliefJets.Add(MakeSteamJet(t.gameObject));
        }
    }

    private void Update()
    {
        if (bridge == null || bridge.Sim == null) return;
        PlantSnapshot s = bridge.GetSnapshot();

        if (flywheel != null)
        {
            float speed = Mathf.Clamp01((float)s.PrimaryFlowFrac) * 360f;
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
        ApplyReliefState(s.ReliefOpen, s.SecondaryReliefOpen);
    }

    // Kept separate so each authored relief path can be checked independently in play mode.
    public void ApplyReliefState(bool primaryOpen, bool secondaryOpen)
    {
        SetReliefJets(primaryReliefJets, primaryOpen);
        SetReliefJets(secondaryReliefJets, secondaryOpen);
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
