using System;
using ReactorSim;
using UnityEditor;
using UnityEngine;

// Exercises imported geometry and the same motion entry point called by PlantEquipmentBinder.
public static class ReactorAssetCheck
{
    public static void Run()
    {
        try
        {
            ControlRoomSceneBuilder.Build(false);
            var hall = GameObject.Find(HallBuilder.RootName);
            var motion = hall.GetComponent<ReactorAssetMotion>();
            motion.Bind();
            var snapshot = new PlantSnapshot { BankInsertion = new[] { 0.0, 0.25, 0.75, 1.0 } };
            var fuel = Find(hall, "RS_VES_FuelAssembly_00_PIV");
            Vector3 fuelPosition = fuel.position;
            for (int pass = 0; pass < 2; pass++)
            {
                // Reordering must not remap bank IDs.
                for (int bank = 0; bank < 4; bank++)
                    Find(hall, "ROD_BANK_" + (char)('A' + bank) + "_PIV").parent.SetAsFirstSibling();
                motion.Bind();
                motion.Apply(snapshot, 0.5, false, 0);
                for (int bank = 0; bank < 4; bank++)
                {
                    string letter = ((char)('A' + bank)).ToString();
                    var pivot = Find(hall, "ROD_BANK_" + letter + "_PIV");
                    var withdrawn = pivot.parent.Find("RS_ROD_Bank" + letter + "_Withdrawn_PIV");
                    var inserted = pivot.parent.Find("RS_ROD_Bank" + letter + "_Inserted_PIV");
                    Vector3 expected = Vector3.Lerp(withdrawn.position, inserted.position,
                        (float)snapshot.BankInsertion[3 - bank]); // snapshot order is D, C, B, A
                    Require(Vector3.Distance(pivot.position, expected) < 0.001f, "bank " + letter + " follows its own actual insertion");
                    Require(inserted.position.y < withdrawn.position.y - 3.8f, "bank " + letter + " inserts downward in metres");
                    foreach (var renderer in pivot.GetComponentsInChildren<Renderer>())
                        Require((GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & StaticEditorFlags.BatchingStatic) == 0,
                            "moving rods are not batching-static");
                }
            }
            Require(Vector3.Distance(fuel.position, fuelPosition) < 1e-6f, "fuel remains stationary");
            snapshot.BankInsertion = null;
            motion.Apply(snapshot, 0, false, 0);
            var a = Find(hall, "ROD_BANK_A_PIV");
            Require(Vector3.Distance(a.position, a.parent.Find("RS_ROD_BankA_Inserted_PIV").position) < 0.001f,
                "missing rod state safely inserts");
            snapshot.BankInsertion = new[] { 0.5, 2.0, -1.0, double.NaN }; // bank A is last (D, C, B, A)
            motion.Apply(snapshot, 0, false, 0);
            Require(Vector3.Distance(a.position, a.parent.Find("RS_ROD_BankA_Inserted_PIV").position) < 0.001f,
                "nonfinite insertion safely inserts");
            var plant = new Plant();
            foreach (var bank in plant.Banks) { bank.Insertion = 0; bank.InsertionTarget = 0; }
            plant.Scram();
            for (int tick = 0; tick < 100; tick++) { plant.Step(0.02); motion.Apply(plant.CaptureState(), 0, false, 0.02f); }
            Require(plant.Banks[0].Insertion > 0.9, "actual plant SCRAM progresses to inserted");
            Require(Vector3.Distance(a.position, a.parent.Find("RS_ROD_BankA_Inserted_PIV").position) < 0.4f,
                "visible rods follow actual SCRAM progression");
            var rotor = Find(hall, "ROTOR_Turbine_PIV");
            Quaternion rotation = rotor.localRotation;
            snapshot.TurbineRpm = 0;
            snapshot.PrimaryFlowFrac = 0;
            motion.Apply(snapshot, 0, false, 0.1f);
            Require(Quaternion.Angle(rotation, rotor.localRotation) < 0.01, "zero RPM stops rotor");
            snapshot.TurbineRpm = 1500;
            motion.Apply(snapshot, 1, false, 0.1f);
            Require(Quaternion.Angle(rotation, rotor.localRotation) > 5, "live RPM rotates rotor");
            var impeller = Find(hall, "Flywheel_PrimaryFlow_Impeller00_PIV");
            Quaternion pumpRotation = impeller.localRotation;
            motion.Apply(snapshot, 1, false, 0.1f);
            Require(Quaternion.Angle(pumpRotation, impeller.localRotation) < 0.01, "zero primary flow stops impeller");
            snapshot.PrimaryFlowFrac = 1;
            motion.Apply(snapshot, 1, false, 0.1f);
            Require(Quaternion.Angle(pumpRotation, impeller.localRotation) > 10, "actual primary flow rotates impeller");
            var glow = Find(hall, "RS_VES_CoreGlow_01_LOD0").GetComponent<Renderer>();
            var block = new MaterialPropertyBlock();
            motion.Apply(snapshot, 0, false, 0);
            glow.GetPropertyBlock(block);
            float low = block.GetColor("_EmissionColor").maxColorComponent;
            motion.Apply(snapshot, 1, false, 0);
            glow.GetPropertyBlock(block);
            Require(block.GetColor("_EmissionColor").maxColorComponent > low, "core emission increases with power");
            Require(glow.sharedMaterial.IsKeywordEnabled("_EMISSION"), "Unity core material has emission enabled");
            AssetManifestCheck.Validate(hall);
            CheckReliefAnchors(hall);
            Debug.Log("[ReactorAssetCheck] PASS");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogError("[ReactorAssetCheck] FAIL " + ex);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
    }

    // Phase 1 U3 (KTD5): the relief and bypass anchors are authored into the hall itself. The binder finds them by name prefix, so a missing
    // or misplaced anchor means a relief with no jet; the sound and the jet must come from one place; and the anchors must not cost the budget.
    private static void CheckReliefAnchors(GameObject hall)
    {
        Require(hall.GetComponentsInChildren<Transform>(true).Length > 1, "hall has children");
        CheckAnchor(hall, ReliefAnchors.PrimaryName, ReliefAnchors.Primary());
        for (int i = 0; i < ReliefAnchors.SecondaryCount; i++)
            CheckAnchor(hall, ReliefAnchors.SecondaryName(i), ReliefAnchors.Secondary(i));
        CheckAnchor(hall, ReliefAnchors.BypassName, ReliefAnchors.Bypass());
        CheckAnchor(hall, ReliefAnchors.SpargerPrimaryName, ReliefAnchors.SpargerPrimary());
        CheckAnchor(hall, ReliefAnchors.SpargerSecondaryName, ReliefAnchors.SpargerSecondary());
        CheckAnchor(hall, ReliefAnchors.SpargerBypassName, ReliefAnchors.SpargerBypass());
        int primary = 0, secondary = 0, bypass = 0, sPrimary = 0, sSecondary = 0, sBypass = 0;
        foreach (Transform t in hall.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("VFX_PORV_SteamJet")) primary++;
            else if (t.name.StartsWith("VFX_SGRelief_SteamJet")) secondary++;
            else if (t.name.StartsWith("VFX_Bypass_SteamJet")) bypass++;
            else if (t.name.StartsWith("VFX_PORV_Sparger")) sPrimary++;
            else if (t.name.StartsWith("VFX_SGRelief_Sparger")) sSecondary++;
            else if (t.name.StartsWith("VFX_Bypass_Sparger")) sBypass++;
        }
        Require(primary == 1 && secondary == ReliefAnchors.SecondaryCount && bypass == 1,
            "exactly one PORV, " + ReliefAnchors.SecondaryCount + " SG relief and one bypass anchor bind (found " + primary + "/" + secondary + "/" + bypass + ")");
        Require(sPrimary == 1 && sSecondary == 1 && sBypass == 1,
            "exactly one PORV, one SG relief and one bypass sparger anchor bind (found " + sPrimary + "/" + sSecondary + "/" + sBypass + ")");
        // The relief lines and the spargers are real geometry the operator sees through the window; they must be built and under water.
        foreach (string meshName in new[] { "ReliefLines", "Spargers", "SpargerPerforations" })
        {
            Transform m = Find(hall, meshName);
            var filter = m.GetComponent<MeshFilter>();
            Require(filter != null && filter.sharedMesh != null && filter.sharedMesh.triangles.Length > 0, meshName + " has geometry");
            Require(m.GetComponent<Renderer>().bounds.max.y < hall.transform.position.y + HallDimensions.WaterDepth, meshName + " stays under the water surface");
        }
        Require(Find(hall, "Spargers").GetComponent<Renderer>().bounds.min.y > hall.transform.position.y + HallDimensions.VesselBandLow - 1.0f, "spargers sit at the fuel-display depth, in the window's band");
        int triangles, renderers, lights;
        HallBuilder.Stats(hall, out triangles, out renderers, out lights);
        Require(triangles <= HallDimensions.MaxHallTriangles, "hall triangles " + triangles + " <= " + HallDimensions.MaxHallTriangles + " with the anchors");
        Require(lights <= HallDimensions.MaxRealtimeLights, "hall lights " + lights + " <= " + HallDimensions.MaxRealtimeLights + " with the anchors");
    }

    private static void CheckAnchor(GameObject hall, string name, double[] expectedHall)
    {
        Transform anchor = Find(hall, name);
        Vector3 local = hall.transform.InverseTransformPoint(anchor.position);
        var expected = new Vector3((float)expectedHall[0], (float)expectedHall[1], (float)expectedHall[2]);
        Require(Vector3.Distance(local, expected) < 0.01f, name + " is at its ReliefAnchors position");
        Require(anchor.GetComponent<Renderer>() == null && anchor.GetComponent<MeshFilter>() == null, name + " adds no geometry");
        Require(anchor.gameObject.layer == ControlRoomSceneBuilder.HallLayer, name + " is on the hall layer");
        Require(Vector3.Dot(anchor.forward, hall.transform.up) > 0.99f, name + " points up, the direction its jet emits");
    }

    public static Transform Find(GameObject root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        throw new InvalidOperationException("Missing " + name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Debug.Log("[ReactorAssetCheck] OK " + message);
    }
}
