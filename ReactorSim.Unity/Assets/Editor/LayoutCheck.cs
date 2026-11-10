using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using ReactorSim.Controls;

// Batchmode check (U7, KTD8): builds the scene in memory and proves every inventory row is present, reachable and
// readable from the viewpoint its section is operated from. Prints "[LayoutCheck] OK/FAIL <row> <reason>" per row and
// exits 0 (all good) or 1.
//   "<Unity.exe>" -batchmode -nographics -projectPath ReactorSim.Unity -executeMethod LayoutCheck.Run -quit -logFile out/layout.log
//   LayoutCheck.RunNegative   perturbs one row in memory (behind a wall, then edge-on) and expects the check to fail it.
//
// Per row (thresholds from design/control-room/layout.json `checks`):
//   exists      a CTL_<id> object; out-of-service rows carry an OOS tag and no INT_ pivot; bound controls have an
//               INT_ pivot with a DiegeticInteractable
//   reach       controls: horizontal distance from the row's standing point to the control is at most
//               reach_m + bench.horizontal_depth, and the eye is within interaction_range_m (else clicks do nothing)
//   view angle  the line of sight from the viewpoint eye meets the kit face plane at min_view_angle_deg or more
//   occlusion   the first collider hit on the ray from the eye to the control is the control itself, or none is hit
//               (every collider counts, including the Ignore Raycast wall proxies)
//   text        angular height of the engraved label (min_label_text_deg): reported as WARN only, because the height is
//               estimated from the text mesh bounds
// The viewpoint is the row's standing point: the operator walks, so each section is worked from its standing line
// (the arc through STAND_<section> around STAND_Center), at the point directly in front of the control.
// overview=y rows are also checked from STAND_Center.
// Kit frame: the kit's local +Z (Blender +Y after the FBX export maps Blender (x,y,z) to Unity (x,z,y)) is the face
// normal, flipped if needed to point at the board centre, so only the plane (not the sign) matters for the angle test.
public static class LayoutCheck
{
    private sealed class Viewpoint
    {
        public string Name;
        public Vector3 Eye;
        public Vector3 Floor;
    }

    private sealed class Context
    {
        public List<DesignData.Row> Rows;
        public Dictionary<string, object> Layout;
        public Transform Root;
        public Collider[] Colliders;
        public Dictionary<string, Viewpoint> Points = new Dictionary<string, Viewpoint>();
        public Transform Centre;
        public float ReachM, MinViewDeg, MinLabelTextDeg, BenchDepth, InteractionRange;
    }

    public static void Run()
    {
        int failures = 0;
        try
        {
            ControlRoomSceneBuilder.Build(false);
            var ctx = Load();
            failures = CheckAnchors(ctx);
            failures += CheckWalkPaths(ctx);
            int rowFailures = 0, warnings = 0;
            foreach (var row in ctx.Rows)
            {
                var reasons = new List<string>();
                CheckRow(ctx, row, reasons, ref warnings);
                if (reasons.Count == 0) Debug.Log("[LayoutCheck] OK " + row.Id);
                else
                {
                    rowFailures++;
                    foreach (var r in reasons) Debug.LogError("[LayoutCheck] FAIL " + row.Id + " " + r);
                }
            }
            failures += rowFailures;
            Debug.Log("[LayoutCheck] " + ctx.Rows.Count + " rows, " + rowFailures + " failing rows, " + warnings + " text-size warnings");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[LayoutCheck] FAIL exception: " + ex);
            failures++;
        }
        Debug.Log("[LayoutCheck] " + (failures == 0 ? "PASS" : "FAILED") + ", " + failures + " failures");
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    // ---- setup -----------------------------------------------------------------------------------------------

    private static Context Load()
    {
        var ctx = new Context();
        ctx.Rows = DesignData.LoadInventory();
        ctx.Layout = DesignData.LoadLayout();
        var root = ControlRoomSceneBuilder.FindRoot();
        if (root == null) throw new System.Exception("no ControlRoom object in the scene");
        ctx.Root = root.transform;
        Physics.SyncTransforms();
        ctx.Colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
        ctx.ReachM = DesignData.Num(ctx.Layout, 0.7f, "checks", "reach_m");
        ctx.MinViewDeg = DesignData.Num(ctx.Layout, 45f, "checks", "min_view_angle_deg");
        ctx.MinLabelTextDeg = DesignData.Num(ctx.Layout, 0.5f, "checks", "min_label_text_deg");
        ctx.BenchDepth = DesignData.Num(ctx.Layout, 0.58f, "bench", "horizontal_depth");
        ctx.InteractionRange = DesignData.Num(ctx.Layout, OperatorMath.InteractionRange, "interaction_range_m");
        ctx.Centre = ControlRoomSceneBuilder.FindDeep(ctx.Root, "STAND_Center");

        var ops = DesignData.Get(ctx.Layout, "operating_points") as Dictionary<string, object>;
        if (ops != null)
        {
            foreach (var kv in ops)
            {
                var anchor = ControlRoomSceneBuilder.FindDeep(ctx.Root, kv.Key);
                if (anchor == null) continue;
                float eyeH = DesignData.Num(kv.Value, OperatorMath.StandingEyeHeight, "eye_height");
                ctx.Points[kv.Key] = new Viewpoint
                {
                    Name = kv.Key,
                    Floor = anchor.position,
                    Eye = anchor.position + Vector3.up * eyeH
                };
            }
        }
        return ctx;
    }

    // Walk-path probe (R10): a capsule shaped like the operator's body walks in a straight line from every start point (each stand
    // anchor, STAND_Center, the spawn and both seat anchors) to every quick-travel stand anchor, steering at the goal each step.
    // It fails a path that stalls (the body moves under a quarter of the distance it was asked to for eight steps running),
    // never arrives, or detours past 1.35x the straight line (OperatorMath.IsStall / IsDetour). If CharacterController.Move does
    // nothing in edit mode the probe says so and skips, rather than failing every path.
    private const float WalkStep = 0.05f;

    private static int CheckWalkPaths(Context ctx)
    {
        var starts = new List<KeyValuePair<string, Vector3>>();
        var goals = new List<KeyValuePair<string, Vector3>>();
        var names = new List<string> { "STAND_Center", "SPAWN_A1", "SEAT_A1", "SEAT_A2" };
        var stands = new List<string>();
        var secs = DesignData.Get(ctx.Layout, "sections") as List<object>;
        if (secs != null)
            foreach (var sec in secs)
            {
                var id = DesignData.Get(sec, "id") as string;
                if (id != null) stands.Add("STAND_" + id);
            }
        foreach (var n in stands) names.Add(n);
        foreach (var n in names)
        {
            var t = ControlRoomSceneBuilder.FindDeep(ctx.Root, n);
            if (t == null) continue;   // a missing anchor is already reported by CheckAnchors
            var p = new KeyValuePair<string, Vector3>(n, t.position);
            starts.Add(p);
            if (stands.Contains(n)) goals.Add(p);
        }

        var go = new GameObject("LayoutCheck_Walker");
        try
        {
            go.layer = 2;
            var cc = go.AddComponent<CharacterController>();
            cc.radius = 0.3f;
            cc.height = 1.75f;
            cc.center = new Vector3(0f, 0.875f, 0f);
            cc.stepOffset = OperatorMath.StepOffset;
            cc.skinWidth = 0.03f;

            // Sanity: in open space far above the room the body must move when asked.
            Teleport(go, cc, new Vector3(0f, 500f, 0f));
            cc.Move(Vector3.right * 0.5f);
            if (Mathf.Abs(go.transform.position.x) < 0.1f)
            {
                Debug.LogWarning("[LayoutCheck] WARN walk probe skipped: CharacterController.Move does not move the body in edit mode");
                return 0;
            }

            int failures = 0, paths = 0;
            foreach (var a in starts)
                foreach (var b in goals)
                {
                    if (a.Key == b.Key) continue;
                    paths++;
                    string why = WalkPath(go, cc, a.Value, b.Value);
                    if (why != null)
                    {
                        failures++;
                        Debug.LogError("[LayoutCheck] FAIL walk " + a.Key + " -> " + b.Key + ": " + why);
                    }
                }
            Debug.Log("[LayoutCheck] walk probe: " + paths + " paths, " + failures + " failing");
            return failures;
        }
        finally
        {
            Object.DestroyImmediate(go);
            Physics.SyncTransforms();
        }
    }

    private static void Teleport(GameObject go, CharacterController cc, Vector3 floor)
    {
        cc.enabled = false;
        go.transform.position = floor;
        cc.enabled = true;
        Physics.SyncTransforms();
    }

    // Null when the path is fine, else why it failed.
    private static string WalkPath(GameObject go, CharacterController cc, Vector3 from, Vector3 to)
    {
        Teleport(go, cc, from);
        Vector3 flatLine = to - from;
        flatLine.y = 0f;
        float straight = flatLine.magnitude;
        float travelled = 0f;
        int stallRun = 0;
        int maxSteps = (int)(straight / WalkStep * 3f) + 20;
        for (int k = 0; k < maxSteps; k++)
        {
            Vector3 pos = go.transform.position;
            Vector3 d = to - pos;
            d.y = 0f;
            float remaining = d.magnitude;
            if (remaining < 0.15f)
                return OperatorMath.IsDetour(travelled, straight)
                    ? "detour: walked " + travelled.ToString("F2", CultureInfo.InvariantCulture) + " m for a " + straight.ToString("F2", CultureInfo.InvariantCulture) + " m line"
                    : null;
            Vector3 move = d / remaining * Mathf.Min(WalkStep, remaining);
            cc.Move(move + Vector3.down * 0.02f);
            Vector3 delta = go.transform.position - pos;
            delta.y = 0f;
            travelled += delta.magnitude;
            if (OperatorMath.IsStall(move.magnitude, delta.magnitude))
            {
                if (++stallRun >= 8) return "stalled at " + go.transform.position.ToString("F2") + " after " + travelled.ToString("F2", CultureInfo.InvariantCulture) + " m";
            }
            else stallRun = 0;
        }
        return "never arrived (" + travelled.ToString("F2", CultureInfo.InvariantCulture) + " m walked, " + straight.ToString("F2", CultureInfo.InvariantCulture) + " m line)";
    }

    // Anchors and chairs the operator needs. Also prints how each anchor's forward compares with the board-facing
    // direction (away from the arc centre); a large difference is a WARN, since OperatorController faces geometrically.
    private static int CheckAnchors(Context ctx)
    {
        int failures = 0;
        var names = new List<string> { "SPAWN_A1", "SEAT_A1", "SEAT_A2", "STAND_Center", "CHAIR_A1", "CHAIR_A2" };
        var secs = DesignData.Get(ctx.Layout, "sections") as List<object>;
        if (secs != null)
            foreach (var sec in secs)
            {
                var id = DesignData.Get(sec, "id") as string;
                if (id != null) names.Add("STAND_" + id);
            }
        foreach (var n in names)
        {
            var t = ControlRoomSceneBuilder.FindDeep(ctx.Root, n);
            if (t == null)
            {
                failures++;
                Debug.LogError("[LayoutCheck] FAIL anchor " + n + " missing from the scene");
                continue;
            }
            string diff = "n/a";
            if (ctx.Centre != null && n != "STAND_Center" && !n.StartsWith("CHAIR_"))
            {
                Vector3 outward = t.position - ctx.Centre.position;
                outward.y = 0f;
                Vector3 fwd = t.up; // Blender +Y (toward the board) is the anchor's local up after the FBX export
                fwd.y = 0f;
                if (outward.sqrMagnitude > 1e-4f && fwd.sqrMagnitude > 1e-4f)
                {
                    float a = Vector3.Angle(fwd, outward);
                    diff = a.ToString("F1", CultureInfo.InvariantCulture) + " deg";
                    if (a > 10f)
                        Debug.LogWarning("[LayoutCheck] WARN anchor " + n + " forward is " + diff + " off the board-facing direction (OperatorController does not use it)");
                }
            }
            Debug.Log("[LayoutCheck] anchor " + n + " pos=" + t.position.ToString("F2") + " forward-vs-board=" + diff);
        }
        return failures;
    }

    // ---- per row ---------------------------------------------------------------------------------------------

    // The point on the section's standing line (radius of STAND_<section> from STAND_Center) directly in front of target.
    private static Viewpoint StandingPointFor(Context ctx, string section, Vector3 target)
    {
        Viewpoint anchor;
        if (ctx.Centre == null || !ctx.Points.TryGetValue("STAND_" + section, out anchor)) return null;
        Vector3 c = ctx.Centre.position;
        Vector3 a = anchor.Floor - c;
        a.y = 0f;
        Vector3 t = target - c;
        t.y = 0f;
        if (a.sqrMagnitude < 1e-6f || t.sqrMagnitude < 1e-6f) return anchor;
        Vector3 floor = c + t.normalized * a.magnitude;
        floor.y = anchor.Floor.y;
        float eyeH = anchor.Eye.y - anchor.Floor.y;
        return new Viewpoint { Name = "STAND_" + section, Floor = floor, Eye = floor + Vector3.up * eyeH };
    }

    private static void CheckRow(Context ctx, DesignData.Row row, List<string> reasons, ref int warnings)
    {
        var ctl = ControlRoomSceneBuilder.FindDeep(ctx.Root, "CTL_" + row.Id);
        if (ctl == null)
        {
            reasons.Add("no CTL_" + row.Id + " object in the scene");
            return;
        }

        // pivots
        Transform intPivot = null;
        foreach (var t in ctl.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("INT_")) continue;
            if (PivotNameParser.Parse(t.name).Status == ParseStatus.VisualChild) continue;
            intPivot = t;
            break;
        }
        if (row.IsOutOfService)
        {
            if (intPivot != null) reasons.Add("out of service but has an INT_ pivot (" + intPivot.name + ")");
            if (!HasDescendantEnding(ctl, "_OosTag")) reasons.Add("out of service but has no OOS tag");
        }
        else if (row.IsControl && row["readiness"] == "bound")
        {
            if (intPivot == null) reasons.Add("bound control has no INT_ pivot");
            else if (intPivot.GetComponent<DiegeticInteractable>() == null) reasons.Add("INT_ pivot " + intPivot.name + " has no DiegeticInteractable");
        }

        // geometry
        Vector3 normal = FaceNormal(ctx, ctl);
        Vector3 target = ctl.position + normal * 0.02f;

        float sweep;
        string gaugePivot;
        if (TrySweepAlignment(ctl, normal, out sweep, out gaugePivot) && sweep < 0.9f)
            reasons.Add("needle sweep axis is " + (Mathf.Acos(Mathf.Clamp01(sweep)) * Mathf.Rad2Deg).ToString("F0", CultureInfo.InvariantCulture)
                + " deg off the face normal on " + gaugePivot + " (the needle would leave the dial)");

        // The operator walks: each section is operated from its standing line (the arc through STAND_<section> around
        // the board centre), at the point directly in front of the control. That point is the row's own viewpoint for
        // reach, interaction range, view angle and occlusion.
        string section = row["section"];
        var views = new List<Viewpoint>();
        Viewpoint own = StandingPointFor(ctx, section, target);
        if (own == null) reasons.Add("no STAND_" + section + " operating point (or no STAND_Center) to derive the standing line");
        else views.Add(own);

        if (row.IsControl && own != null)
        {
            Vector3 d = target - own.Floor;
            d.y = 0f;
            float horiz = d.magnitude;
            float limit = ctx.ReachM + ctx.BenchDepth;
            if (horiz > limit)
                reasons.Add("unreachable: " + horiz.ToString("F2", CultureInfo.InvariantCulture) + " m horizontal from the "
                    + own.Name + " standing line (limit " + limit.ToString("F2", CultureInfo.InvariantCulture) + " m = reach + bench depth)");
            float eyeDist = Vector3.Distance(own.Eye, target);
            if (!row.IsOutOfService && eyeDist > ctx.InteractionRange)
                reasons.Add("beyond interaction range: " + eyeDist.ToString("F2", CultureInfo.InvariantCulture) + " m from the eye on the "
                    + own.Name + " standing line (limit " + ctx.InteractionRange.ToString("F1", CultureInfo.InvariantCulture) + " m)");
        }
        if (row["overview"] == "y")
        {
            Viewpoint centre;
            if (ctx.Points.TryGetValue("STAND_Center", out centre)) views.Add(centre);
            else reasons.Add("overview row but no STAND_Center operating point");
        }

        foreach (var v in views)
        {
            string from = " from " + v.Name;
            Vector3 los = target - v.Eye;
            float dist = los.magnitude;
            if (dist < 1e-3f) continue;
            Vector3 dir = los / dist;

            float angle = Mathf.Asin(Mathf.Clamp01(Mathf.Abs(Vector3.Dot(dir, normal)))) * Mathf.Rad2Deg;
            if (angle < ctx.MinViewDeg)
                reasons.Add("view angle " + angle.ToString("F0", CultureInfo.InvariantCulture) + " deg" + from + " is below "
                    + ctx.MinViewDeg.ToString("F0", CultureInfo.InvariantCulture) + " deg (face turned away or edge-on)");

            Collider hit;
            if (FirstHit(ctx.Colliders, v.Eye, target, out hit) && !hit.transform.IsChildOf(ctl))
                reasons.Add("occluded" + from + " by " + hit.name);

            float textDeg;
            if (TryLabelAngle(ctl, v.Eye, out textDeg) && textDeg < ctx.MinLabelTextDeg)
            {
                warnings++;
                Debug.LogWarning("[LayoutCheck] WARN text " + row.Id + from + ": label subtends about "
                    + textDeg.ToString("F2", CultureInfo.InvariantCulture) + " deg (min " + ctx.MinLabelTextDeg.ToString("F1", CultureInfo.InvariantCulture) + ")");
            }
        }
    }

    private static bool HasDescendantEnding(Transform root, string suffix)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.EndsWith(suffix)) return true;
        return false;
    }

    // Blender's FBX export applies the Z-up -> Y-up conversion only at the room root, so every child keeps its Blender
    // local axes: the kit frame's +Y (the face normal, toward the operator) is the CTL_ root's local up in Unity.
    // Sign: a bench kit's normal (mostly vertical) points up; a vertical-panel kit's normal points toward the centre.
    private static Vector3 FaceNormal(Context ctx, Transform ctl)
    {
        Vector3 n = ctl.up;
        if (Mathf.Abs(n.y) > 0.7f)
        {
            if (n.y < 0f) n = -n;
        }
        else if (ctx.Centre != null)
        {
            Vector3 toCentre = ctx.Centre.position - ctl.position;
            toCentre.y = 0f;
            Vector3 flat = n;
            flat.y = 0f;
            if (Vector3.Dot(flat, toCentre) < 0f) n = -n;
        }
        return n.normalized;
    }

    // Gauge needles: the binder sweeps a BIND_GAUGE pivot about ControlRoom3DBinder.KitFaceNormal (local). That axis must
    // be the kit's face normal, or the needle swings out of the dial. Returns |cos| between the sweep axis and the normal.
    private static bool TrySweepAlignment(Transform ctl, Vector3 normal, out float alignment, out string pivotName)
    {
        alignment = 0f;
        pivotName = null;
        foreach (var t in ctl.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("BIND_GAUGE_")) continue;
            if (CounterDisplay.IsCounterPivot(t)) continue;   // K04 counters show digits, they never rotate
            pivotName = t.name;
            alignment = Mathf.Abs(Vector3.Dot(t.TransformDirection(ControlRoom3DBinder.KitFaceNormal).normalized, normal));
            return true;
        }
        return false;
    }

    // Nearest collider along eye -> target (collider.Raycast ignores layers, so the Ignore Raycast wall proxies count).
    private static bool FirstHit(Collider[] colliders, Vector3 from, Vector3 to, out Collider hitCollider)
    {
        hitCollider = null;
        Vector3 d = to - from;
        float dist = d.magnitude;
        if (dist < 1e-4f) return false;
        var ray = new Ray(from, d / dist);
        float best = float.MaxValue;
        for (int i = 0; i < colliders.Length; i++)
        {
            var c = colliders[i];
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
            RaycastHit h;
            if (c.Raycast(ray, out h, dist) && h.distance < best)
            {
                best = h.distance;
                hitCollider = c;
            }
        }
        return hitCollider != null;
    }

    // Angular height of the row's engraved label from the eye: the middle extent of the text mesh bounds (the thin
    // axis is the extrusion, the larger of the other two is the width).
    private static bool TryLabelAngle(Transform ctl, Vector3 eye, out float degrees)
    {
        degrees = 0f;
        foreach (var mf in ctl.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.name.EndsWith("_LabelText") || mf.sharedMesh == null) continue;
            Vector3 s = mf.sharedMesh.bounds.size;
            Vector3 ls = mf.transform.lossyScale;
            var ext = new float[] { Mathf.Abs(s.x * ls.x), Mathf.Abs(s.y * ls.y), Mathf.Abs(s.z * ls.z) };
            System.Array.Sort(ext);
            float h = ext[1];
            float dist = Vector3.Distance(eye, mf.transform.TransformPoint(mf.sharedMesh.bounds.center));
            if (dist < 1e-3f || h < 1e-4f) return false;
            degrees = 2f * Mathf.Atan(h * 0.5f / dist) * Mathf.Rad2Deg;
            return true;
        }
        return false;
    }

    // ---- negative runs ---------------------------------------------------------------------------------------

    /// <summary>
    /// Proves the check can fail: picks a bound gauge row that passes, hides it behind a wall box (expects an
    /// occlusion failure), then turns the same face edge-on to its viewpoint (expects a view-angle failure).
    /// Exit 0 means both perturbations were caught.
    /// </summary>
    public static void RunNegative()
    {
        bool ok = false;
        try
        {
            ControlRoomSceneBuilder.Build(false);
            var ctx = Load();
            foreach (var row in ctx.Rows)
            {
                if (row["role"] != "indicator" || row["readiness"] != "bound" || row.IsOutOfService) continue;
                var baseline = new List<string>();
                int w = 0;
                CheckRow(ctx, row, baseline, ref w);
                if (baseline.Count != 0) continue;

                var ctl = ControlRoomSceneBuilder.FindDeep(ctx.Root, "CTL_" + row.Id);
                Vector3 target = ctl.position + FaceNormal(ctx, ctl) * 0.02f;
                Viewpoint v = StandingPointFor(ctx, row["section"], target);   // the same viewpoint CheckRow uses
                if (v == null) continue;

                // 1. a wall between the eye and the control
                var wall = new GameObject("LayoutCheck_NegativeWall");
                wall.transform.position = Vector3.Lerp(v.Eye, target, 0.5f);
                wall.AddComponent<BoxCollider>().size = new Vector3(0.6f, 0.6f, 0.6f);
                Physics.SyncTransforms();
                ctx.Colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
                var occ = new List<string>();
                CheckRow(ctx, row, occ, ref w);
                bool occluded = occ.Exists(r => r.StartsWith("occluded"));
                Object.DestroyImmediate(wall);
                Physics.SyncTransforms();
                ctx.Colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);

                // 2. the face turned edge-on: its normal perpendicular to the line of sight
                Vector3 los = (target - v.Eye).normalized;
                Vector3 edge = Vector3.Cross(los, Vector3.up).normalized;
                ctl.rotation = Quaternion.FromToRotation(ctl.TransformDirection(ControlRoom3DBinder.KitFaceNormal), edge) * ctl.rotation;
                var ang = new List<string>();
                CheckRow(ctx, row, ang, ref w);
                bool edgeOn = ang.Exists(r => r.StartsWith("view angle"));

                Debug.Log("[LayoutCheck] negative row " + row.Id + ": occlusion caught=" + occluded + ", edge-on caught=" + edgeOn);
                ok = occluded && edgeOn;
                break;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[LayoutCheck] negative exception: " + ex);
        }
        Debug.Log("[LayoutCheck] negative " + (ok ? "PASS (both perturbations were caught)" : "FAILED"));
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }
}
