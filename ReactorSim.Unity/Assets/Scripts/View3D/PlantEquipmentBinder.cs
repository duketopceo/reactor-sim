using UnityEngine;
using ReactorSim;
using ReactorSim.Instruments;

// Phase 2 plant equipment binder (N4): drives containment-side motion from PlantSnapshot.
// Flywheel_PrimaryFlow spins with PrimaryFlowFrac, CoreGlow_Mesh pulses with Power,
// PORV steam anchor activates when ReliefOpen, RPV CRDM rods track BankInsertion.
public class PlantEquipmentBinder : MonoBehaviour
{
    public PlantBridge bridge;

    private Transform flywheel;
    private Transform coreGlow;
    private Material coreGlowMat;
    private Vector3 coreGlowBaseScale = Vector3.one;
    private Light coreGlowLight;
    private float coreGlowLightBase = 1f;
    private Transform porvAnchor;
    private ParticleSystem porvSteam;
    private ParticleSystem sgSteam;
    private readonly System.Collections.Generic.List<Transform> crdmRods = new System.Collections.Generic.List<Transform>();

    private void Awake()
    {
        if (bridge == null)
            bridge = FindAnyObjectByType<PlantBridge>();
        BindTransforms();
        if (porvAnchor != null)
        {
            porvSteam = MakeSteamJet(porvAnchor.gameObject);
            sgSteam = MakeSteamJet(porvAnchor.gameObject); // same anchor drives both relief paths
        }
    }

    private static ParticleSystem MakeSteamJet(GameObject parent)
    {
        // Runtime-built particle jet so no .prefab asset is needed for the VFX.
        var go = new GameObject(parent.name + "_Steam");
        go.transform.SetParent(parent.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
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
            if (t.name.StartsWith("Flywheel_PrimaryFlow") || t.name == "Flywheel")
                flywheel = t;
            else if (t.name == "CoreGlow_Mesh")
            {
                coreGlow = t;
                coreGlowBaseScale = t.localScale;
                var r = t.GetComponent<Renderer>();
                if (r != null) coreGlowMat = r.material;
                // The reactor hall carries a blue point light as a child of the glow mesh (ControlRoomSceneBuilder).
                coreGlowLight = t.GetComponentInChildren<Light>(true);
                if (coreGlowLight != null) coreGlowLightBase = coreGlowLight.intensity;
            }
            else if (t.name.StartsWith("VFX_PORV_SteamJet") || t.name.StartsWith("VFX_SGRelief"))
                porvAnchor = t;
            else if (t.name.StartsWith("BIND_BAR_BankInsertion_"))
                crdmRods.Add(t);
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
        if (coreGlow != null && coreGlowMat != null)
        {
            // Power is normalised (1.0 = rated thermal power); GlowMapping gives a dim floor at 0 and 1.0 at rated power.
            float g = (float)GlowMapping.Glow(s.Power);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
            float shown = g * (0.85f + 0.15f * pulse);
            var c = coreGlowMat.color;
            c.a = Mathf.Clamp01(0.15f + 0.6f * shown);
            coreGlowMat.color = c;
            if (coreGlowMat.HasProperty("_EmissionColor"))
            {
                // Cherenkov blue; emission only has an effect when the material enables _EMISSION (the hall glow does).
                coreGlowMat.SetColor("_EmissionColor", new Color(0.25f, 0.55f, 1f) * (0.4f + 2.6f * shown));
            }
            if (coreGlowLight != null)
                coreGlowLight.intensity = coreGlowLightBase * (0.15f + 1.35f * shown);
            float scale = 0.8f + g * 0.4f;
            coreGlow.localScale = coreGlowBaseScale * scale;
        }
        if (porvAnchor != null)
        {
            bool relief = s.ReliefOpen || s.SecondaryReliefOpen;
            porvAnchor.gameObject.SetActive(relief);
            if (porvAnchor.gameObject.activeSelf)
                porvAnchor.Rotate(0f, 90f * Time.deltaTime, 0f);
            // Steam particle emission tracks relief state (VFX jet, no prefab asset).
            if (porvSteam != null)
            {
                var e = porvSteam.emission;
                e.rateOverTime = relief ? 60f : 0f;
                if (relief && !porvSteam.isPlaying) porvSteam.Play();
                if (!relief && porvSteam.isPlaying) porvSteam.Stop();
            }
        }
        for (int i = 0; i < crdmRods.Count && i < 4; i++)
        {
            double ins = s.BankInsertion != null && i < s.BankInsertion.Length ? s.BankInsertion[i] : 1.0;
            var rod = crdmRods[i];
            rod.localPosition = new Vector3(rod.localPosition.x, rod.localPosition.y,
                Mathf.Lerp(0f, 1.0f, (float)ins) + rod.localPosition.z * 0f);
        }
    }
}
