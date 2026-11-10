using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ReactorSim;
using ReactorSim.Controls;

// Batchmode check (U6/U7): every INT_* pivot parses, has a collider and interactable, and maps to a PlantIntent; and
// every inventory row that is a bound control (design/control-room/inventory.csv) has its pivot under CTL_<id>.
// Prints "[ControlMapCheck] OK/FAIL ..." lines and exits 0 (all good) or 1.
//   "<Unity.exe>" -batchmode -nographics -projectPath ReactorSim.Unity -executeMethod ControlMapCheck.Run -quit -logFile out/control-map.log
//   ControlMapCheck.RunNegative   proves the check fails when a bound control's pivot is missing (exit 0 = it did)
public static class ControlMapCheck
{
    public static void Run()
    {
        int failures = 0;
        try
        {
            ControlRoomSceneBuilder.Build(false);
            failures = Evaluate();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[ControlMapCheck] FAIL exception: " + ex);
            failures++;
        }
        Debug.Log("[ControlMapCheck] " + (failures == 0 ? "PASS" : "FAILED") + ", " + failures + " failures");
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static int Evaluate()
    {
        int failures = 0;
        var root = ControlRoomSceneBuilder.FindRoot();
        if (root == null)
        {
            Debug.LogError("[ControlMapCheck] FAIL no ControlRoom object in the scene");
            return 1;
        }

        var specs = new List<ControlSpec>();
        int pivots = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("INT_")) continue;
            var r = PivotNameParser.Parse(t.name);
            if (r.Status == ParseStatus.VisualChild) continue;
            pivots++;
            if (r.Status == ParseStatus.Malformed)
            {
                failures++;
                Debug.LogError("[ControlMapCheck] FAIL " + r.Error);
                continue;
            }
            specs.Add(r.Spec);
            string why = null;
            if (t.GetComponent<Collider>() == null) why = "no collider";
            else if (t.GetComponent<DiegeticInteractable>() == null) why = "no DiegeticInteractable";
            else if (!ControlIntentFactory.IsSupported(r.Spec)) why = "kind " + r.Spec.Kind + " has no intent mapping";
            if (why != null)
            {
                failures++;
                Debug.LogError("[ControlMapCheck] FAIL " + t.name + ": " + why);
            }
            else
            {
                var probe = r.Spec.Style == ControlStyle.Dial
                    ? ControlIntentFactory.BuildDial(r.Spec, r.Spec.Min, "check")
                    : ControlIntentFactory.BuildButton(r.Spec, false, 1.0, "check");
                Debug.Log("[ControlMapCheck] OK   " + t.name + " -> " + probe.Kind + " target=" + probe.Target);
            }
        }

        failures += CheckBoundRows(root.transform);
        Debug.Log("[ControlMapCheck] " + pivots + " control pivots inspected");
        return failures;
    }

    // Every bound control row needs an INT_ pivot under its CTL_ root, with the row's intent kind and style.
    private static int CheckBoundRows(Transform root)
    {
        int failures = 0, rowsChecked = 0;
        foreach (var row in DesignData.LoadInventory())
        {
            if (!row.IsControl || row["readiness"] != "bound") continue;
            rowsChecked++;
            var ctl = ControlRoomSceneBuilder.FindDeep(root, "CTL_" + row.Id);
            ParseResultInfo info = FindPivot(ctl);
            string why = null;
            if (ctl == null) why = "no CTL_" + row.Id + " object";
            else if (info.Pivot == null) why = "no INT_ pivot under CTL_" + row.Id;
            else if (!string.Equals(info.Spec.Kind.ToString(), row.BindingKind, System.StringComparison.OrdinalIgnoreCase))
                why = "pivot " + info.Pivot.name + " is kind " + info.Spec.Kind + ", row binds " + row.BindingKind;
            else if ((info.Spec.Style == ControlStyle.Dial) != row["range_or_positions"].Contains(".."))
                why = "pivot " + info.Pivot.name + " is " + info.Spec.Style + " but row range is '" + row["range_or_positions"] + "'";
            if (why != null)
            {
                failures++;
                Debug.LogError("[ControlMapCheck] FAIL row " + row.Id + ": " + why);
            }
            else Debug.Log("[ControlMapCheck] OK   row " + row.Id + " -> " + info.Pivot.name);
        }
        Debug.Log("[ControlMapCheck] " + rowsChecked + " bound control rows checked against the inventory");
        return failures;
    }

    private struct ParseResultInfo { public Transform Pivot; public ControlSpec Spec; }

    private static ParseResultInfo FindPivot(Transform ctl)
    {
        var info = new ParseResultInfo();
        if (ctl == null) return info;
        foreach (var t in ctl.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("INT_")) continue;
            var r = PivotNameParser.Parse(t.name);
            if (r.Status != ParseStatus.Ok) continue;
            info.Pivot = t;
            info.Spec = r.Spec;
            return info;
        }
        return info;
    }

    /// <summary>Renames the first bound control's pivot in memory and expects Evaluate to fail on exactly that row.</summary>
    public static void RunNegative()
    {
        bool ok = false;
        try
        {
            ControlRoomSceneBuilder.Build(false);
            var root = ControlRoomSceneBuilder.FindRoot();
            foreach (var row in DesignData.LoadInventory())
            {
                if (!row.IsControl || row["readiness"] != "bound") continue;
                var info = FindPivot(ControlRoomSceneBuilder.FindDeep(root.transform, "CTL_" + row.Id));
                if (info.Pivot == null) continue;
                info.Pivot.name = "REMOVED_" + info.Pivot.name;
                Debug.Log("[ControlMapCheck] negative: removed pivot of row " + row.Id);
                ok = CheckBoundRows(root.transform) == 1;
                break;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[ControlMapCheck] negative exception: " + ex);
        }
        Debug.Log("[ControlMapCheck] negative " + (ok ? "PASS (the check failed the missing pivot)" : "FAILED (the check did not catch it)"));
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }
}
