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
                        (float)snapshot.BankInsertion[bank]);
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
            snapshot.BankInsertion = new[] { double.NaN, -1.0, 2.0, 0.5 };
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
