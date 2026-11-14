using System;
using System.Collections.Generic;
using ReactorSim;
using ReactorSim.Instruments;
using UnityEngine;

// The hall binder delegates the authored FBX pivots here; fixed fuel never belongs to a moving bank.
public sealed class ReactorAssetMotion : MonoBehaviour
{
    private sealed class Bank
    {
        public string name;
        public Transform pivot;
        public Vector3 withdrawn, inserted;
    }

    private sealed class Rotor
    {
        public Transform pivot;
        public Vector3 axis;
        public bool turbine;
    }

    private readonly List<Bank> banks = new List<Bank>();
    private readonly List<Rotor> rotors = new List<Rotor>();
    private readonly List<Renderer> glow = new List<Renderer>();
    private MaterialPropertyBlock properties;
    private Light coreLight;
    private Vector3 coreLightRestPosition;

    public void Bind()
    {
        if (properties == null) properties = new MaterialPropertyBlock();
        banks.Clear();
        rotors.Clear();
        glow.Clear();
        coreLight = null;
        var seen = new HashSet<int>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "RS_VES_CoreLight_PIV")
            {
                coreLight = t.GetComponent<Light>();
                coreLightRestPosition = t.localPosition;
            }
            else if (t.name.StartsWith("ROD_BANK_", StringComparison.Ordinal))
            {
                string[] parts = t.name.Split('_');
                if (parts.Length != 4 || parts[2].Length != 1 || parts[3] != "PIV")
                    throw new InvalidOperationException("Invalid rod bank pivot " + t.name);
                int index = parts[2][0] - 'A';
                if (index < 0 || index > 3 || !seen.Add(index))
                    throw new InvalidOperationException("Duplicate or invalid rod bank " + t.name);
                var withdrawn = t.parent.Find("RS_ROD_Bank" + parts[2] + "_Withdrawn_PIV");
                var inserted = t.parent.Find("RS_ROD_Bank" + parts[2] + "_Inserted_PIV");
                if (withdrawn == null || inserted == null)
                    throw new InvalidOperationException("Rod endpoints missing for " + t.name);
                banks.Add(new Bank { name = parts[2], pivot = t,
                    withdrawn = withdrawn.localPosition, inserted = inserted.localPosition });
            }
            else if (t.name.StartsWith("Flywheel_PrimaryFlow_", StringComparison.Ordinal) ||
                     t.name == "ROTOR_Turbine_PIV")
            {
                // Marker-derived axes survive FBX coordinate-system conversion.
                Transform marker = null;
                foreach (Transform child in t)
                    if (child.name.StartsWith("RS_ROD_RotorAxis_", StringComparison.Ordinal)) marker = child;
                if (marker == null) throw new InvalidOperationException("Rotor axis missing for " + t.name);
                rotors.Add(new Rotor { pivot = t, axis = marker.localPosition.normalized,
                    turbine = t.name == "ROTOR_Turbine_PIV" });
            }
            else if (t.name.StartsWith("RS_VES_CoreGlow_", StringComparison.Ordinal))
            {
                var renderer = t.GetComponent<Renderer>();
                if (renderer != null) glow.Add(renderer);
            }
        }
        if (banks.Count != 4) throw new InvalidOperationException("Expected four authored rod banks");
    }

    public void Apply(PlantSnapshot snapshot, double power, bool glowOff, float deltaTime)
    {
        foreach (var bank in banks)
        {
            double input = PlantWallMapModel.BankInsertion(bank.name, snapshot);
            float fraction = double.IsNaN(input) ? 1f : Mathf.Clamp01((float)input);
            bank.pivot.localPosition = Vector3.Lerp(bank.withdrawn, bank.inserted, fraction);
        }
        foreach (var rotor in rotors)
        {
            // Readable visual mapping prevents a fast rotor aliasing as stationary; no simulation feedback.
            float speed = rotor.turbine ? Mathf.Clamp01((float)snapshot.TurbineRpm / 3000f)
                                        : Mathf.Clamp01((float)snapshot.PrimaryFlowFrac);
            rotor.pivot.Rotate(rotor.axis, -speed * 240f * deltaTime, Space.Self);
        }
        Color tint = Color.Lerp(ThemeColors.ToColor(ReactorSim.Ui.Theme.CoreGlowCold),
            ThemeColors.ToColor(ReactorSim.Ui.Theme.CoreGlowHot),
            (float)GlowMapping.FuelTemperatureLevel(snapshot.FuelTempC));
        Color emission = tint * (glowOff ? 0f : (float)GlowMapping.RackEmission(power));
        if (coreLight != null)
        {
            // Reuse the fourth light as a neutral inspection lamp; never add another real-time light.
            bool inspecting = ReactorInspectionView.Active && Camera.main != null;
            coreLight.transform.localPosition = coreLightRestPosition;
            if (inspecting) coreLight.transform.position = Camera.main.transform.position;
            coreLight.color = inspecting ? ThemeColors.TextBody : tint;
            coreLight.intensity = glowOff ? 0f : inspecting ? 2f : 0.75f * (float)GlowMapping.LightScale(power);
        }
        foreach (var renderer in glow)
        {
            renderer.GetPropertyBlock(properties);
            properties.SetColor("_EmissionColor", emission);
            renderer.SetPropertyBlock(properties);
        }
    }
}
