using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using TMPro;
using ReactorSim;
using ReactorSim.Instruments;

// Batchmode check (U7): TMP essentials present, every CRT_* screen has a page and renders text with the live
// power/reactivity, every gauge/bar pivot has a value source, and every shader looked up by name is in
// Always Included Shaders. Prints "[InstrumentCheck] OK/FAIL ..." lines and exits 0/1.
//   InstrumentCheck.Run                       the check
//   InstrumentCheck.EnsureAlwaysIncludedShaders  adds the missing shaders to ProjectSettings/GraphicsSettings.asset
// (the casualty post-mortem page and assistant subtitle checks land with U4/U5.)
public static class InstrumentCheck
{
    // Shaders that scripts resolve by name (Shader.Find) or that TMP text needs; they must survive player builds.
    public static readonly string[] RequiredShaders =
    {
        "Unlit/Color",
        "TextMeshPro/Distance Field",
        "Legacy Shaders/Particles/Additive",   // ReactorWaterAtmosphere pool bubbles, resolved at runtime
    };

    public static void Run()
    {
        pendingRows = null;
        kitByCtl = null;
        int failures = 0;
        try
        {
            ControlRoomSceneBuilder.Build(false);
            failures += CheckTmp();
            failures += CheckScene();
            failures += CheckPages();
            failures += CheckShaders();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[InstrumentCheck] FAIL exception: " + ex);
            failures++;
        }
        Debug.Log("[InstrumentCheck] " + (failures == 0 ? "PASS" : "FAILED") + ", " + failures + " failures");
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static int CheckTmp()
    {
        bool ok = TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null;
        Debug.Log("[InstrumentCheck] " + (ok ? "OK   " : "FAIL") + " TMP default font asset"
            + (ok ? " = " + TMP_Settings.defaultFontAsset.name : " missing (run TmpBootstrap.ImportEssentials)"));
        return ok ? 0 : 1;
    }

    private static int CheckScene()
    {
        int failures = 0;
        var room = ControlRoomSceneBuilder.FindRoot();
        var equip = GameObject.Find("PlantEquipment");
        if (room == null)
        {
            Debug.LogError("[InstrumentCheck] FAIL no ControlRoom object in the scene");
            return 1;
        }
        if (room.GetComponent<ControlRoom3DBinder>() == null)
        {
            Debug.LogError("[InstrumentCheck] FAIL ControlRoom has no ControlRoom3DBinder");
            failures++;
        }
        var cam = Camera.main;
        if (cam == null || cam.GetComponent<OperatorController>() == null)
        {
            Debug.LogError("[InstrumentCheck] FAIL main camera has no OperatorController");
            failures++;
        }

        int crts = 0, gauges = 0, labelled = 0;
        var roots = new List<GameObject> { room };
        if (equip != null) roots.Add(equip);
        foreach (var root in roots)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("CRT_") && !t.name.EndsWith("_Glow") && t.GetComponent<MeshRenderer>() != null)
                {
                    crts++;
                    bool bound = t.GetComponent<CrtScreenRenderer>() != null && CrtPages.IsKnownPage(t.name);
                    if (!bound)
                    {
                        failures++;
                        Debug.LogError("[InstrumentCheck] FAIL " + t.name + ": no CrtScreenRenderer or no page defined");
                    }
                    else Debug.Log("[InstrumentCheck] OK   " + t.name + " bound");
                }
                else if (t.name.StartsWith("BIND_GAUGE_") || t.name.StartsWith("BIND_BAR_"))
                {
                    gauges++;
                    GaugeSource src;
                    if (!GaugeSources.TryResolve(t.name, out src))
                    {
                        if (IsPendingMapping(t))
                        {
                            Debug.Log("[InstrumentCheck] SKIP " + t.name + ": inventory row is needs_mapping (selector lands with U8)");
                            continue;
                        }
                        failures++;
                        Debug.LogError("[InstrumentCheck] FAIL " + t.name + ": no value source");
                    }
                    else
                    {
                        Debug.Log("[InstrumentCheck] OK   " + t.name + " <- " + src.Name);
                        failures += CheckScaleLabels(t, src, ref labelled);
                    }
                }
            }
        }
        Debug.Log("[InstrumentCheck] " + crts + " CRT screens, " + gauges + " gauge/bar pivots inspected, " + labelled + " scale-label sets measured");
        if (labelled == 0)
        {
            failures++;
            Debug.LogError("[InstrumentCheck] FAIL no gauge scale labels were measured against the bezel frame");
        }
        int roomTris, roomRenderers, roomLights;
        HallBuilder.Stats(room, out roomTris, out roomRenderers, out roomLights);
        Debug.Log($"[InstrumentCheck] control-room totals: {roomTris} tris, {roomRenderers} renderers, {roomLights} lights");
        return failures;
    }

    // Label-vs-bezel (the readability regression behind the square frame): every numeral and unit a circular dial prints, and
    // the min/max numerals of an edgewise bar, must lie inside the kit's bezel frame (GaugeScale.BezelInnerHalfFrac,
    // kits.json circular_bezel_inner_frac) or between the bar's rails and end caps, with clearance. The boxes are the exact
    // ones GaugeNumerals places.
    private static int CheckScaleLabels(Transform pivot, GaugeSource src, ref int labelled)
    {
        var ctl = ControlRoomSceneBuilder.FindCtlRoot(pivot);
        if (ctl == null) return 0;
        if (kitByCtl == null)
        {
            kitByCtl = new Dictionary<string, string>();
            foreach (var row in DesignData.LoadInventory()) kitByCtl["CTL_" + row.Id] = row["kit"];
        }
        string kit;
        kitByCtl.TryGetValue(ctl.name, out kit);
        int failures = 0;
        var marks = GaugeScale.Marks(src.Min, src.Max, src.Units);
        string unit = GaugeScale.UnitLabel(src.Units);
        if (kit == "K01" || kit == "K01L")
        {
            labelled++;
            var boxes = new List<KeyValuePair<string, GaugeScale.FaceBox>>
            {
                new KeyValuePair<string, GaugeScale.FaceBox>("min " + marks[0].Text, GaugeScale.EndNumeralBox(marks[0].Text, false)),
                new KeyValuePair<string, GaugeScale.FaceBox>("mid " + marks[1].Text, GaugeScale.MidNumeralBox(marks[1].Text)),
                new KeyValuePair<string, GaugeScale.FaceBox>("max " + marks[2].Text, GaugeScale.EndNumeralBox(marks[2].Text, true)),
            };
            if (unit.Length > 0) boxes.Add(new KeyValuePair<string, GaugeScale.FaceBox>("unit " + unit, GaugeScale.UnitBox(unit)));
            foreach (var b in boxes)
                if (!GaugeScale.InsideBezel(b.Value))
                {
                    failures++;
                    Debug.LogError("[InstrumentCheck] FAIL " + ctl.name + ": " + b.Key + " reaches the bezel frame");
                }
        }
        else if (kit == "K02")
        {
            labelled++;
            var layout = GaugeScale.BarLabels(GaugeGeometry.TravelM(src.Name));
            string maxText = marks[2].Text + (unit.Length == 0 || unit == "%" ? unit : " " + unit);
            if (maxText.Length > GaugeScale.BarMaxChars(layout)) maxText = marks[2].Text;
            foreach (var pair in new[] { new KeyValuePair<string, float>(marks[0].Text, layout.Lo), new KeyValuePair<string, float>(maxText, layout.Hi) })
            {
                var b = GaugeScale.BarNumeralBox(pair.Key, pair.Value, layout);
                if (Mathf.Abs(b.X) + b.W / 2f > GaugeScale.BarStripInnerHalfWidthM - 0.003f
                    || Mathf.Abs(b.Y) + b.H / 2f > GaugeScale.BarStripInnerHalfHeightM - 0.003f)
                {
                    failures++;
                    Debug.LogError("[InstrumentCheck] FAIL " + ctl.name + ": bar numeral '" + pair.Key + "' reaches the rails or end caps");
                }
            }
        }
        return failures;
    }

    // A gauge row marked needs_mapping in the inventory has a BIND_ name whose selector does not exist yet.
    private static bool IsPendingMapping(Transform pivot)
    {
        var ctl = ControlRoomSceneBuilder.FindCtlRoot(pivot);
        if (ctl == null) return false;
        if (pendingRows == null)
        {
            pendingRows = new HashSet<string>();
            foreach (var row in DesignData.LoadInventory())
                if (row["readiness"] == "needs_mapping") pendingRows.Add("CTL_" + row.Id);
        }
        return pendingRows.Contains(ctl.name);
    }

    private static HashSet<string> pendingRows;
    private static Dictionary<string, string> kitByCtl;

    // Every page must render non-empty text containing the live power and reactivity of a fresh plant.
    private static int CheckPages()
    {
        int failures = 0;
        var sim = new Simulation(new Tuning());
        sim.ApplyInteractiveTick();
        sim.LoadScenarioPreset("cold-shutdown");
        sim.Advance(3.0);
        var snap = sim.CaptureSnapshot();
        string power = (snap.Power * ReactorSim.Physics.RatedThermalMWt).ToString("F0", CultureInfo.InvariantCulture);
        string rho = snap.TotalReactivityPcm.ToString("F0", CultureInfo.InvariantCulture);
        var trends = new TrendBuffer(30);
        trends.Push(snap);
        var clock = ClockReadout.FromClock(sim.Clock);
        foreach (var name in CrtPages.KnownPages)
        {
            string text = CrtPages.Build(name, snap, clock, trends, new string[0]);
            bool ok = !string.IsNullOrWhiteSpace(text) && text.Contains("PWR " + power + " MWt") && text.Contains("RHO " + rho + " pcm");
            if (!ok) failures++;
            Debug.Log("[InstrumentCheck] " + (ok ? "OK   " : "FAIL") + " page " + name + " renders power " + power + " / reactivity " + rho);
        }
        return failures;
    }

    private static int CheckShaders()
    {
        int failures = 0;
        var included = IncludedShaders();
        foreach (var name in RequiredShaders)
        {
            var sh = Shader.Find(name);
            bool found = sh != null;
            bool listed = found && included.Contains(sh);
            if (!found || !listed) failures++;
            Debug.Log("[InstrumentCheck] " + (found && listed ? "OK   " : "FAIL") + " shader " + name
                + (!found ? " not found" : (listed ? " in Always Included Shaders" : " NOT in Always Included Shaders (run InstrumentCheck.EnsureAlwaysIncludedShaders)")));
        }
        return failures;
    }

    private static SerializedObject GraphicsSettingsObject()
    {
        var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
        return gs != null ? new SerializedObject(gs) : null;
    }

    private static HashSet<Shader> IncludedShaders()
    {
        var set = new HashSet<Shader>();
        var so = GraphicsSettingsObject();
        if (so == null) return set;
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        for (int i = 0; i < arr.arraySize; i++)
        {
            var sh = arr.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
            if (sh != null) set.Add(sh);
        }
        return set;
    }

    // -executeMethod InstrumentCheck.EnsureAlwaysIncludedShaders : writes ProjectSettings/GraphicsSettings.asset.
    public static void EnsureAlwaysIncludedShaders()
    {
        int failures = 0;
        var so = GraphicsSettingsObject();
        if (so == null)
        {
            Debug.LogError("[InstrumentCheck] FAIL cannot load ProjectSettings/GraphicsSettings.asset");
            failures++;
        }
        else
        {
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            var included = IncludedShaders();
            foreach (var name in RequiredShaders)
            {
                var sh = Shader.Find(name);
                if (sh == null)
                {
                    failures++;
                    Debug.LogError("[InstrumentCheck] FAIL shader not found: " + name);
                    continue;
                }
                if (included.Contains(sh)) { Debug.Log("[InstrumentCheck] OK   already included " + name); continue; }
                arr.arraySize++;
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                Debug.Log("[InstrumentCheck] added to Always Included Shaders: " + name);
            }
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
}
