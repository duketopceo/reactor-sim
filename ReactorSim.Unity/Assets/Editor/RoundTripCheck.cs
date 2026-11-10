#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ReactorSim;
using ReactorSim.Controls;
using ReactorSim.Instruments;

// R9 round trip (U9): for EVERY bound control row of design/control-room/inventory.csv, operate the control through the
// binder's own DragTo / PressButton path in play mode and prove three things:
//   (a) the sim's intent log holds the row's intent kind (applied, not rejected, issued by the diegetic binder)
//   (b) the bound snapshot value moved to / toward the expected value or state
//   (c) the paired indicator's pivot moved (needle turned, bar slid, lamp emission flipped) by about what its value change predicts
// Rows with no indicator in the inventory (RoundTripPlan.Exclusions) still run (a) and (b).
//   "<Unity.exe>" -batchmode -nographics -projectPath ReactorSim.Unity -executeMethod RoundTripCheck.Run -logFile out/unity-RoundTripCheck.Run.log
//   RoundTripCheck.RunNegative   unbinds one paired indicator (rod_bank_0_pos) before play and expects the check to FAIL
//                                rod_bank_0_ctl on the indicator (exit 0 = it did)
// Do NOT pass -quit: the work finishes asynchronously in play mode and the method exits the editor itself (0/1), the same
// pattern as RoomRender (SessionState stage machine re-armed by [InitializeOnLoad] across the play-mode domain reload).
// No graphics device is needed: nothing is rendered, pivots and lamp property blocks are read back.
//
// How it advances the sim (deterministic): PlantBridge is disabled for the run, so nothing ticks behind the check's back.
// Each row loads its preset, then the check ticks Simulation.Tick() directly (TickDt 1 s in interactive mode) after the
// control is driven, and finally ticks twice more with a few player-loop frames after each so the binder's snapshot
// interpolation (it shows the PREVIOUS of the last two distinct snapshots) has caught up before the pivot is read.
//
// Preset rule: reactor / primary / safety rows start from "cold-shutdown"; steam / turbine / electrical rows start from
// "full-power" (the turbine, grid and steam rows need a running plant). Per-row fixtures (RoundTripPlan.For) set up the
// start state a control needs to have any effect: a scrammed plant for Reset SCRAM, a paused clock for Resume, manual
// load mode for the grid demand and breaker rows, a tripped turbine for the trip reset.
// [InitializeOnLoad] is what re-arms Poll after the play-mode domain reload; without it the editor sits in play mode
// forever and the timeout (checked inside Poll) never fires.
[InitializeOnLoad]
public static class RoundTripCheck
{
    private const string ScenePath = "Assets/Scenes/Bootstrap.unity";
    private const double WarmupSeconds = 2.0;
    private const double TimeoutSeconds = 600.0;
    private const int SettleFrames = 3;
    private const string NegativeRow = "rod_bank_0_ctl";

    private const string StageKey = "ReactorSim_RoundTrip_Stage";   // 0 idle, 1 play mode: warm-up then run, 2 wait for exit from play
    private const string StartKey = "ReactorSim_RoundTrip_Start";
    private const string CodeKey = "ReactorSim_RoundTrip_Code";
    private const string ModeKey = "ReactorSim_RoundTrip_Mode";     // "positive" | "negative"

    private static bool playSeen;
    private static double startRealtime;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static bool routineStarted;

    // run state (play-mode domain only)
    private static PlantBridge bridge;
    private static ControlRoom3DBinder binder;
    private static Transform root;
    private static Dictionary<string, DesignData.Row> byId;
    private static MaterialPropertyBlock mpb;
    private static int rowsRun, rowsFailed;
    private static readonly List<string> failedRows = new List<string>();
    private static readonly List<string> failedWhy = new List<string>();

    static RoundTripCheck()
    {
        if (SessionState.GetInt(StageKey, 0) == 0) return;
        Arm();
    }

    private static void Arm()
    {
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    public static void Run() { Begin("positive"); }

    public static void RunNegative() { Begin("negative"); }

    private static void Begin(string mode)
    {
        try
        {
            if (!File.Exists(ScenePath)) { Fail("scene not found: " + ScenePath); return; }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (mode == "negative" && !UnbindNegativeIndicator()) { Finish(1); return; }
            SessionState.SetString(ModeKey, mode);
            SessionState.SetInt(CodeKey, 0);
            SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
            SessionState.SetInt(StageKey, 1);
            playSeen = false;
            routineStarted = false;
            Arm();
            PlayModeGuard.Prepare();   // no script compile / domain reload after Awake (it would null PlantBridge.Sim)
            EditorApplication.isPlaying = true;
        }
        catch (Exception e)
        {
            Fail("could not enter play mode: " + e);
        }
    }

    // Negative fixture: before play, rename the paired indicator's BIND_ pivot so ControlRoom3DBinder (which matches by
    // prefix) never binds it. The check still finds it by name (it matches "BIND_" anywhere), so the row fails on the
    // indicator not moving, with the row id, rather than on a missing object.
    private static bool UnbindNegativeIndicator()
    {
        var rootGo = ControlRoomSceneBuilder.FindRoot();
        if (rootGo == null) { Debug.LogError("[RoundTripCheck] FAIL negative: no ControlRoom object in the scene"); return false; }
        foreach (var row in DesignData.LoadInventory())
        {
            if (row.Id != NegativeRow) continue;
            var ind = FindIndicatorPivot(rootGo.transform, row["paired_indicator"]);
            if (ind == null)
            {
                Debug.LogError("[RoundTripCheck] FAIL negative: no indicator pivot under CTL_" + row["paired_indicator"]);
                return false;
            }
            ind.name = "UNBOUND_" + ind.name;
            Debug.Log("[RoundTripCheck] negative: unbound " + ind.name + " (indicator of " + NegativeRow + ")");
            return true;
        }
        Debug.LogError("[RoundTripCheck] FAIL negative: row " + NegativeRow + " not in the inventory");
        return false;
    }

    private static void Poll()
    {
        int stage = SessionState.GetInt(StageKey, 0);
        if (stage == 0) { EditorApplication.update -= Poll; return; }

        double start = SessionState.GetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
        if (EditorApplication.timeSinceStartup - start > TimeoutSeconds)
        {
            Debug.LogError("[RoundTripCheck] FAIL timed out (stage " + stage + ")");
            SessionState.SetInt(CodeKey, 1);
            if (stage == 1 && EditorApplication.isPlaying)
            {
                SessionState.SetInt(StageKey, 2);
                SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
                EditorApplication.isPlaying = false;
            }
            else Finish(1);
            return;
        }

        if (stage == 1)
        {
            // Only "playing" gates the run: PlayModeGuard defers recompiles until play ends, so a pending compile can
            // no longer reload the domain mid-check, but it keeps isCompiling true for the whole session.
            if (!EditorApplication.isPlaying) return;
            if (!playSeen)
            {
                playSeen = true;
                startRealtime = Time.realtimeSinceStartupAsDouble;
                return;
            }
            // Wall-clock warm-up only (Awake of PlantBridge / binder / interactables has run by then).
            if (!routineStarted)
            {
                if (Time.realtimeSinceStartupAsDouble - startRealtime < WarmupSeconds) return;
                routineStarted = true;
                stack.Clear();
                stack.Push(Execute());
            }
            bool more;
            try { more = Advance(); }
            catch (Exception e)
            {
                Debug.LogError("[RoundTripCheck] FAIL exception: " + e);
                SessionState.SetInt(CodeKey, 1);
                more = false;
            }
            if (!more)
            {
                SessionState.SetInt(StageKey, 2);
                SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
                EditorApplication.isPlaying = false;
            }
        }
        else if (stage == 2)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Finish(SessionState.GetInt(CodeKey, 1));
        }
    }

    // Drives the nested iterators; a yielded IEnumerator is run to completion first, a yielded null waits one editor update.
    private static bool Advance()
    {
        while (stack.Count > 0)
        {
            var top = stack.Peek();
            if (!top.MoveNext()) { stack.Pop(); continue; }
            var nested = top.Current as IEnumerator;
            if (nested != null) { stack.Push(nested); continue; }
            return true;
        }
        return false;
    }

    private static void Fail(string msg)
    {
        Debug.LogError("[RoundTripCheck] FAIL " + msg);
        Finish(1);
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Poll;
        SessionState.EraseInt(StageKey);
        SessionState.EraseFloat(StartKey);
        SessionState.EraseInt(CodeKey);
        SessionState.EraseString(ModeKey);
        Debug.Log("[RoundTripCheck] exit " + code);
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    // ---- the run -------------------------------------------------------------------------------------------------

    private static IEnumerator Execute()
    {
        bool negative = SessionState.GetString(ModeKey, "positive") == "negative";
        rowsRun = 0; rowsFailed = 0;
        failedRows.Clear(); failedWhy.Clear();
        mpb = new MaterialPropertyBlock();

        bridge = UnityEngine.Object.FindAnyObjectByType<PlantBridge>();
        binder = UnityEngine.Object.FindAnyObjectByType<ControlRoom3DBinder>();
        var rootGo = ControlRoomSceneBuilder.FindRoot();
        if (bridge == null || bridge.Sim == null || binder == null || rootGo == null)
        {
            var all = UnityEngine.Object.FindObjectsByType<PlantBridge>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            string seen = "";
            foreach (var b in all)
                seen += " [" + b.name + " scene=" + b.gameObject.scene.name + " active=" + b.isActiveAndEnabled + " sim=" + (b.Sim != null) + "]";
            Debug.LogError("[RoundTripCheck] FAIL setup: PlantBridge found=" + (bridge != null) + " sim=" + (bridge != null && bridge.Sim != null)
                + " all=" + all.Length + seen + ", ControlRoom3DBinder " + (binder != null) + ", ControlRoom " + (rootGo != null)
                + ", playing=" + EditorApplication.isPlaying + " frame=" + Time.frameCount);
            SessionState.SetInt(CodeKey, 1);
            yield break;
        }
        root = rootGo.transform;
        // Nothing may tick the sim behind the check: the check ticks it by hand.
        bridge.enabled = false;

        var rows = DesignData.LoadInventory();
        byId = new Dictionary<string, DesignData.Row>();
        foreach (var r in rows) byId[r.Id] = r;

        foreach (var row in rows)
        {
            if (!row.IsControl || row["readiness"] != "bound") continue;
            if (negative && row.Id != NegativeRow) continue;
            rowsRun++;
            var v = new RoundTripPlan.Verdict();
            yield return RunRow(row, v);
            if (v.Ok) Debug.Log("[RoundTripCheck] OK " + row.Id + " " + v.Detail);
            else
            {
                rowsFailed++;
                failedRows.Add(row.Id);
                failedWhy.Add(string.Join("; ", v.Failures.ToArray()));
                Debug.LogError("[RoundTripCheck] FAIL " + row.Id + " " + string.Join("; ", v.Failures.ToArray()));
            }
        }

        Debug.Log("[RoundTripCheck] " + rowsRun + " bound control rows round-tripped, " + rowsFailed + " failing");
        int code;
        if (negative)
        {
            bool ok = rowsFailed == 1 && failedRows[0] == NegativeRow && failedWhy[0].Contains("indicator");
            Debug.Log("[RoundTripCheck] negative " + (ok
                ? "PASS (the check failed " + NegativeRow + " on its unbound indicator: " + failedWhy[0] + ")"
                : "FAILED (the check did not fail " + NegativeRow + " on its indicator; failing rows: " + string.Join(",", failedRows.ToArray()) + ")"));
            code = ok ? 0 : 1;
        }
        else
        {
            Debug.Log("[RoundTripCheck] " + (rowsFailed == 0 && rowsRun > 0 ? "PASS" : "FAILED") + ", " + rowsFailed + " failures");
            code = rowsFailed == 0 && rowsRun > 0 ? 0 : 1;
        }
        SessionState.SetInt(CodeKey, code);
    }

    // Longer than ControlRoom3DBinder's MODE latch (6 s of unscaled time).
    private const float ModeLatchWaitSeconds = 6.5f;

    private static IEnumerator WaitRealSeconds(float seconds)
    {
        float until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until) yield return null;
    }

    private static IEnumerator WaitFrames(int n)
    {
        int target = Time.frameCount + n;
        while (Time.frameCount < target) yield return null;
    }

    // Two distinct snapshots must follow any state change before the binder shows it (it displays the previous of the
    // last two while the tick fraction is 0), so tick once, let frames run, tick again, let frames run.
    private static IEnumerator Settle()
    {
        for (int i = 0; i < 2; i++)
        {
            TickSim(1, null);
            yield return WaitFrames(SettleFrames);
        }
    }

    private static PlantSnapshot Capture()
    {
        lock (bridge.SimLock) { return bridge.Sim.CaptureSnapshot(); }
    }

    private static string TickSim(int ticks, Func<Simulation, bool> stopWhen)
    {
        try
        {
            lock (bridge.SimLock)
            {
                var sim = bridge.Sim;
                for (int i = 0; i < ticks; i++)
                {
                    sim.Tick();
                    if (stopWhen != null && stopWhen(sim)) break;
                }
            }
            return null;
        }
        catch (Exception e)
        {
            return "sim tick threw " + e.GetType().Name + ": " + e.Message;
        }
    }

    private static IEnumerator RunRow(DesignData.Row row, RoundTripPlan.Verdict v)
    {
        string id = row.Id;
        var c = RoundTripPlan.For(id, row["section"]);

        // ---- find the control and its spec
        var ctl = ControlRoomSceneBuilder.FindDeep(root, "CTL_" + id);
        DiegeticInteractable inter = null;
        if (ctl == null) v.Fail("no CTL_" + id + " object");
        else
        {
            foreach (var t in ctl.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("INT_")) continue;
                if (PivotNameParser.Parse(t.name).Status != ParseStatus.Ok) continue;
                inter = t.GetComponent<DiegeticInteractable>();
                break;
            }
            if (inter == null) v.Fail("no INT_ pivot with a DiegeticInteractable under CTL_" + id);
            else if (inter.Spec == null) v.Fail("interactable on " + inter.name + " has no ControlSpec (Awake did not run?)");
        }

        // ---- find the paired indicator (or confirm the exclusion)
        string paired = row["paired_indicator"];
        string excludedWhy;
        bool excluded = RoundTripPlan.Exclusions.TryGetValue(id, out excludedWhy);
        Transform ind = null;
        GaugeSource src = null;
        bool isBar = false;
        string lampName = null;
        if (excluded && paired.Length > 0) v.Fail("is on the exclusion list but also has paired_indicator " + paired);
        else if (!excluded && paired.Length == 0) v.Fail("indicator: no paired_indicator and not on the exclusion list");
        else if (!excluded)
        {
            DesignData.Row indRow;
            if (!byId.TryGetValue(paired, out indRow)) v.Fail("indicator: paired_indicator " + paired + " is not a row");
            else if (indRow["readiness"] != "bound") v.Fail("indicator: " + paired + " is not bound");
            else
            {
                ind = FindIndicatorPivot(root, paired);
                if (ind == null) v.Fail("indicator: no BIND_ pivot under CTL_" + paired);
                else
                {
                    string bn = ind.name.Substring(ind.name.IndexOf("BIND_", StringComparison.Ordinal));
                    isBar = bn.StartsWith("BIND_BAR_", StringComparison.Ordinal);
                    if (bn.StartsWith("BIND_LAMP_", StringComparison.Ordinal)) lampName = bn.Substring("BIND_LAMP_".Length);
                    else if (!GaugeSources.TryResolve(bn, out src)) v.Fail("indicator: " + bn + " does not resolve to a value source");
                    if (lampName != null && !LampPredicates.Has(lampName)) v.Fail("indicator: lamp " + lampName + " has no predicate");
                    if (lampName != null && ind.GetComponent<Renderer>() == null) v.Fail("indicator: lamp " + ind.name + " has no Renderer");
                }
            }
        }
        if (!v.Ok) yield break;

        var spec = inter.Spec;
        var sim = bridge.Sim;

        // ---- start state
        lock (bridge.SimLock) { RoundTripPlan.Prepare(sim, c); }
        yield return Settle();
        // MODE is latched on for a few real seconds after any mode change (the preset load is one): let the latch
        // expire so the baseline shows the lamp's settled state.
        if (lampName == "MODE") yield return WaitRealSeconds(ModeLatchWaitSeconds);
        PlantSnapshot before = Capture();
        bool haltedBefore = bridge.Sim.Clock.Halted;
        PoseReading pose0 = ReadPose(ind, isBar, lampName);
        AlarmState ackBefore = lampName != null ? binder.AlarmStateOf(lampName) : AlarmState.Clear;
        long seqFrom;
        lock (bridge.SimLock) { seqFrom = RoundTripPlan.NextSeq(sim); }
        double startVal = ControlValues.Commanded(spec, before);
        double targetVal = double.NaN;
        double buttonBefore = double.NaN, buttonWant = double.NaN;
        int expectedHits = 1;
        string err;

        // ---- operate it through the binder
        if (spec.Style == ControlStyle.Dial)
        {
            targetVal = RoundTripPlan.TargetValue(spec, c, startVal);
            double frac = DragMapper.FractionFromValue(spec, targetVal);
            binder.StartFraction(inter);
            if (spec.Discrete)
            {
                // A stepped knob sends nothing while it is being turned, only on release.
                double mid = frac < 0.5 ? 0.9 : 0.1;
                binder.DragTo(inter, mid, false);
                err = TickSim(1, null);
                if (err != null) v.Fail(err);
                lock (bridge.SimLock)
                {
                    if (RoundTripPlan.CountSince(sim, seqFrom, spec.Kind) != 0)
                        v.Fail("stepped knob sent an intent before release");
                }
            }
            binder.DragTo(inter, frac, true);
        }
        else
        {
            buttonBefore = RoundTripPlan.ButtonState(spec, sim, before);
            buttonWant = RoundTripPlan.ExpectedButton(spec, sim, buttonBefore);
            // The SCRAM cover: the first press lifts it, the second trips (ControlRoom3DBinder.PressButton).
            // Guarded switches (K11) are gated by clicks in DiegeticInteractable, which the binder path skips.
            int presses = spec.Kind == IntentKind.Scram ? 2 : 1;
            for (int i = 0; i < presses; i++) binder.PressButton(inter);
        }

        if (spec.Kind == IntentKind.SkipAhead)
            err = TickSim(c.Ticks, s => !s.Clock.Skipping);
        else
            err = TickSim(c.Ticks, null);
        if (err != null) v.Fail(err);
        yield return Settle();
        PlantSnapshot after = Capture();
        bool haltedAfter = bridge.Sim.Clock.Halted;
        PoseReading pose1 = ReadPose(ind, isBar, lampName);
        AlarmState ackAfter = lampName != null ? binder.AlarmStateOf(lampName) : AlarmState.Clear;

        // ---- (a) intent log
        List<IntentLogEntry> log;
        lock (bridge.SimLock) { log = sim.GetIntentLogSnapshot(); }
        RoundTripPlan.CheckLog(log, seqFrom, spec, targetVal, expectedHits, v);

        // ---- (b) bound snapshot value
        if (spec.Style == ControlStyle.Dial)
        {
            RoundTripPlan.CheckDial(spec, startVal, targetVal, before, after, v);
            v.Detail += "value " + Fmt(startVal) + "->" + Fmt(ControlValues.Current(spec, after)) + " (target " + Fmt(targetVal) + ")";
        }
        else
        {
            double buttonAfter = RoundTripPlan.ButtonState(spec, sim, after);
            RoundTripPlan.CheckButton(spec, buttonBefore, buttonAfter, buttonWant, v);
            v.Detail += "state " + Fmt(buttonBefore) + "->" + Fmt(buttonAfter) + " (want " + Fmt(buttonWant) + ")";
        }

        // ---- (c) paired indicator pose
        if (excluded) v.Detail += "; no indicator (" + excludedWhy + ")";
        else if (spec.Kind == IntentKind.AcknowledgeAlarms && lampName != null)
        {
            // The tile's lit state does not change on an acknowledge: it goes from flashing to steady.
            if (ackBefore != AlarmState.Unacked) v.Fail("indicator " + paired + ": tile " + lampName + " was " + ackBefore + " before the acknowledge, expected Unacked (flashing)");
            if (ackAfter != AlarmState.Acked) v.Fail("indicator " + paired + ": tile " + lampName + " is " + ackAfter + " after the acknowledge, expected Acked (steady)");
            if (!pose1.Lit) v.Fail("indicator " + paired + ": tile " + lampName + " is dark after the acknowledge, an acknowledged alarm holds steady");
            v.Detail += "; indicator " + paired + " tile " + lampName + " " + ackBefore + "->" + ackAfter;
        }
        else if (lampName != null)
        {
            var ctxBefore = RoundTripPlan.LampContextFor(sim, before, haltedBefore);
            var ctxAfter = RoundTripPlan.LampContextFor(sim, before, haltedAfter);   // Previous = the start snapshot: MODE lights on the change
            bool wantBefore = LampPredicates.Evaluate(lampName, before, ctxBefore);
            bool wantAfter = LampPredicates.Evaluate(lampName, after, ctxAfter);
            if (wantBefore == wantAfter) v.Fail("indicator " + paired + ": lamp " + lampName + " predicate does not change (" + wantBefore + ")");
            else
            {
                bool litBefore = pose0.Lit, litAfter = pose1.Lit;
                if (litBefore != wantBefore) v.Fail("indicator " + paired + ": lamp " + lampName + " started " + (litBefore ? "lit" : "dark") + ", expected " + (wantBefore ? "lit" : "dark"));
                if (litAfter != wantAfter) v.Fail("indicator " + paired + ": lamp " + lampName + " ended " + (litAfter ? "lit" : "dark") + ", expected " + (wantAfter ? "lit" : "dark"));
            }
            v.Detail += "; indicator " + paired + " lamp " + lampName + " " + (pose0.Lit ? "lit" : "dark") + "->" + (pose1.Lit ? "lit" : "dark");
        }
        else
        {
            double span = RoundTripPlan.PoseSpan(src, isBar, before, after);
            if (pose0.Text != null)
            {
                // K04 counter: the digits are the pose.
                if (span < RoundTripPlan.NeedleEpsilonDeg)
                    v.Fail("indicator " + paired + ": its value " + src.Name + " did not change (" + Fmt(src.Select(before)) + " -> " + Fmt(src.Select(after)) + ")");
                else if (pose1.Text == pose0.Text)
                    v.Fail("indicator " + paired + ": counter " + ind.name + " still shows " + pose0.Text + ", its value changed " + Fmt(src.Select(before)) + " -> " + Fmt(src.Select(after)));
                v.Detail += "; indicator " + paired + " counter " + src.Name + " " + pose0.Text + "->" + pose1.Text;
            }
            else
            {
            double moved = isBar ? Mathf.Abs(pose1.Along - pose0.Along) : Mathf.Abs(Vector3.SignedAngle(pose0.Dir, pose1.Dir, ControlRoom3DBinder.KitFaceNormal));
            string unit = isBar ? "m" : "deg";
            double floor = isBar ? RoundTripPlan.BarEpsilonM : RoundTripPlan.NeedleEpsilonDeg;
            if (span < floor)
                v.Fail("indicator " + paired + ": its value " + src.Name + " did not change (" + Fmt(src.Select(before)) + " -> " + Fmt(src.Select(after)) + ")");
            else if (moved < Math.Max(floor, RoundTripPlan.PoseFraction * span))
                v.Fail("indicator " + paired + ": pivot " + ind.name + " moved " + Fmt(moved) + " " + unit + ", its value change predicts " + Fmt(span) + " " + unit);
            v.Detail += "; indicator " + paired + " " + src.Name + " " + Fmt(src.Select(before)) + "->" + Fmt(src.Select(after)) + " pose " + Fmt(moved) + " " + unit + " (predicted " + Fmt(span) + ")";
            }
        }
    }

    // ---- scene readback --------------------------------------------------------------------------------------------

    /// <summary>A BIND_ pivot under CTL_&lt;rowId&gt;; matched by "contains" so the negative run's renamed pivot is still found.</summary>
    private static Transform FindIndicatorPivot(Transform rootT, string rowId)
    {
        var ctl = ControlRoomSceneBuilder.FindDeep(rootT, "CTL_" + rowId);
        if (ctl == null) return null;
        foreach (var t in ctl.GetComponentsInChildren<Transform>(true))
            if (t.name.Contains("BIND_GAUGE_") || t.name.Contains("BIND_BAR_") || t.name.Contains("BIND_LAMP_")) return t;
        return null;
    }

    private struct PoseReading
    {
        public Vector3 Dir;      // pivot local +X rotated: its angle about the face normal is the needle angle
        public float Along;      // pivot local position along the face-up axis (edgewise bar)
        public bool Lit;         // lamp emission above the lit threshold
        public string Text;      // K04 counter digits (null when the pivot is not a counter)
    }

    private static PoseReading ReadPose(Transform ind, bool isBar, string lampName)
    {
        var p = new PoseReading();
        if (ind == null) return p;
        if (lampName != null)
        {
            var r = ind.GetComponent<Renderer>();
            if (r != null)
            {
                r.GetPropertyBlock(mpb);
                Color e = mpb.GetColor("_EmissionColor");
                p.Lit = (e.r + e.g + e.b) / 3f > 0.15f;
            }
        }
        else if (CounterDisplay.IsCounterPivot(ind)) p.Text = binder.CounterTextOf(ind) ?? "";
        else if (isBar) p.Along = Vector3.Dot(ind.localPosition, ControlRoom3DBinder.KitFaceUp);
        else p.Dir = ind.localRotation * Vector3.right;
        return p;
    }

    private static string Fmt(double d)
    {
        if (double.IsNaN(d)) return "n/a";
        return Math.Abs(d) >= 1000.0 || (d != 0.0 && Math.Abs(d) < 0.001) ? d.ToString("0.###e+0", CultureInfo.InvariantCulture) : d.ToString("0.####", CultureInfo.InvariantCulture);
    }

}

// ---- PURE BEGIN: no UnityEngine below this line until PURE END (cases, expectations and checks). It can be compiled
// ---- against ReactorSim.Core alone to prove every row's preset, target and expectation without Unity; keep it free of Unity types.

public sealed class RoundTripCase
{
    public string RowId = "";
    public string Preset = RoundTripPlan.ColdShutdown;
    /// <summary>"", "scrammed", "paused", "manual" (load-following off), "turbine-tripped", "weak-cooling".</summary>
    public string Fixture = "";
    /// <summary>Dial target as a fraction of the dial's range; NaN means the rule (move at least a quarter of the range, interior).</summary>
    public double TargetFrac = double.NaN;
    /// <summary>Dial target as a value (stepped knobs); wins over TargetFrac.</summary>
    public double TargetValue = double.NaN;
    /// <summary>Sim ticks (1 s each) run after the control is operated; a skip-ahead row ticks until the skip ends.</summary>
    public int Ticks = 30;
}

public static class RoundTripPlan
{
    public const string ColdShutdown = "cold-shutdown";
    public const string FullPower = "full-power";
    public const string Issuer = "diegetic";   // ControlRoom3DBinder.Issuer
    public const double NeedleEpsilonDeg = 0.001;
    public const double BarEpsilonM = 0.00001;
    /// <summary>The pivot must move at least this fraction of what its value change predicts.</summary>
    public const double PoseFraction = 0.25;

    // ---- EXCLUSIONS BEGIN
    // Bound control rows with no paired indicator. InventoryTests keeps this list and the empty paired_indicator cells in
    // step. These rows still run the intent-log and snapshot-value checks.
    public static readonly Dictionary<string, string> Exclusions = new Dictionary<string, string>
    {
        { "clock_slow_btn", "no inventory indicator shows the time scale (it is on the tablet and hover text only)" },
        { "clock_fast_btn", "no inventory indicator shows the time scale (it is on the tablet and hover text only)" },
        { "assist_tier_ctl", "its effect is text on the CRT_AssistantTerminal page, a render with no pivot to move" },
        { "mission_select_ctl", "its effect is text on the CRT_AssistantTerminal page, a render with no pivot to move" },
    };
    // ---- EXCLUSIONS END

    public sealed class Verdict
    {
        public readonly List<string> Failures = new List<string>();
        public string Detail = "";
        public bool Ok { get { return Failures.Count == 0; } }
        public void Fail(string why) { Failures.Add(why); }
    }

    public static string PresetFor(string section)
    {
        switch (section)
        {
            case "steam":
            case "turbine":
            case "electrical": return FullPower;
            default: return ColdShutdown;   // reactor, primary, safety
        }
    }

    public static RoundTripCase For(string rowId, string section)
    {
        var c = new RoundTripCase { RowId = rowId, Preset = PresetFor(section) };
        switch (rowId)
        {
            case "rod_bank_0_ctl":
            case "rod_bank_1_ctl":
            case "rod_bank_2_ctl":
            case "rod_bank_3_ctl":
            case "rod_all_ctl":
                c.TargetFrac = 0.5;          // rods start fully in at 1.0 and drive out at 0.01 per second
                break;
            case "borate_ctl":
            case "dilute_ctl":
            case "naoh_ctl":
            case "ionx_ctl":
                c.Ticks = 60;                // slow chemistry needs a minute to show
                break;
            case "vacuum_pump_ctl":
                c.Fixture = "weak-cooling";
                c.Ticks = 60;
                break;
            case "mode_ctl":
                c.TargetValue = 1;           // Nominal; a drag back to exactly the dial's middle is swallowed (see report)
                break;
            case "assist_tier_ctl":
                c.TargetValue = 2;           // Supervised
                break;
            case "mission_select_ctl":
                c.TargetValue = 2;
                break;
            case "scram_reset_btn":
                c.Fixture = "scrammed";
                break;
            case "clock_resume_btn":
                c.Fixture = "paused";
                break;
            case "turbine_trip_reset":
                c.Fixture = "turbine-tripped";
                break;
            case "alarm_ack_btn":
                c.Fixture = "scrammed";      // SCRAM raises its tile: flashing before the acknowledge, steady after
                break;
            case "turbine_auto_ctl":
                c.Ticks = 10;
                break;
            case "gen_breaker_sw":
                c.Fixture = "manual";        // in load-following mode the breaker re-closes on the next tick
                c.Ticks = 10;
                break;
            case "grid_demand_ctl":
                c.Fixture = "manual";        // grid demand only drives the output in manual mode
                c.Ticks = 10;
                break;
            case "clock_skip_btn":
                c.Ticks = 700;               // ticks until the 10 minute skip ends (stops early)
                break;
        }
        return c;
    }

    /// <summary>Loads the row's preset and sets up its fixture. Caller holds the sim lock.</summary>
    public static void Prepare(Simulation sim, RoundTripCase c)
    {
        sim.LoadScenarioPreset(c.Preset);
        bool queued = false;
        if (sim.Clock.Paused) { sim.EnqueueIntent(PlantIntent.ResumeClock("roundtrip-setup")); queued = true; }
        if (Math.Abs(sim.Clock.Scale - sim.Clock.Tuning.DefaultScale) > 1e-9)
        {
            sim.EnqueueIntent(PlantIntent.SetTimeScale(sim.Clock.Tuning.DefaultScale, "roundtrip-setup"));
            queued = true;
        }
        if (c.Fixture == "paused") { sim.EnqueueIntent(PlantIntent.PauseClock("roundtrip-setup")); queued = true; }
        if (queued) sim.Tick();
        switch (c.Fixture)
        {
            case "scrammed": sim.Plant.Scram(); break;
            case "manual": sim.Plant.Bop.AutoLoad = false; break;
            case "turbine-tripped": sim.Plant.Bop.Turbine.Trip(); break;
            // Full cooling water condenses all the exhaust and holds the shell at the vacuum floor whatever the pumps do;
            // with 0.8 the pumps decide whether the pressure climbs.
            case "weak-cooling": sim.Plant.Bop.CoolingWaterFrac = 0.8; break;
        }
    }

    public static long NextSeq(Simulation sim)
    {
        var log = sim.GetIntentLogSnapshot();
        return log.Count == 0 ? 0 : log[log.Count - 1].Seq + 1;
    }

    public static int CountSince(Simulation sim, long seqFrom, IntentKind kind)
    {
        int n = 0;
        foreach (var e in sim.GetIntentLogSnapshot())
            if (e.Seq >= seqFrom && e.Intent != null && e.Intent.Kind == kind) n++;
        return n;
    }

    // ---- dials ---------------------------------------------------------------------------------------------------

    public static double TargetValue(ControlSpec spec, RoundTripCase c, double startValue)
    {
        if (!double.IsNaN(c.TargetValue)) return DragMapper.ValueFromFraction(spec, DragMapper.FractionFromValue(spec, c.TargetValue));
        double startFrac = DragMapper.FractionFromValue(spec, startValue);
        double f = !double.IsNaN(c.TargetFrac) ? c.TargetFrac : (startFrac <= 0.5 ? 0.75 : 0.25);
        return DragMapper.ValueFromFraction(spec, f);
    }

    // The commanded value must land on the target (within a tenth of the move) and the live value must move toward it.
    public static void CheckDial(ControlSpec spec, double startValue, double targetValue, PlantSnapshot before, PlantSnapshot after, Verdict v)
    {
        double move = targetValue - startValue;
        if (double.IsNaN(startValue) || Math.Abs(move) < 1e-9)
        {
            v.Fail("test setup: target " + targetValue + " equals the start " + startValue);
            return;
        }
        if (spec.Kind == IntentKind.OperatingMode)
        {
            string want = ((OperatingMode)(int)Math.Round(targetValue)).ToString();
            if (after.Mode != want) v.Fail("operating mode is " + after.Mode + ", expected " + want);
            return;
        }
        double cmd1 = ControlValues.Commanded(spec, after);
        if (double.IsNaN(cmd1) || Math.Abs(cmd1 - targetValue) > 0.1 * Math.Abs(move) + 1e-6)
            v.Fail("commanded value is " + cmd1 + " after, expected " + targetValue + " (from " + startValue + ")");
        double cur0 = ControlValues.Current(spec, before), cur1 = ControlValues.Current(spec, after);
        if (double.IsNaN(cur0) || double.IsNaN(cur1) || (cur1 - cur0) * Math.Sign(move) <= 1e-9)
            v.Fail("bound value did not move toward " + targetValue + " (" + cur0 + " -> " + cur1 + ")");
    }

    // ---- buttons -------------------------------------------------------------------------------------------------

    public static double ButtonState(ControlSpec spec, Simulation sim, PlantSnapshot s)
    {
        switch (spec.Kind)
        {
            case IntentKind.Scram:
            case IntentKind.ResetScram: return s.Scrammed ? 1.0 : 0.0;
            case IntentKind.ReliefValve: return s.ReliefOpen ? 1.0 : 0.0;
            case IntentKind.PauseClock:
            case IntentKind.ResumeClock: return sim.Clock.Paused ? 1.0 : 0.0;
            case IntentKind.SetTimeScale: return sim.Clock.Scale;
            case IntentKind.SkipAhead: return s.GameMinutes;
            case IntentKind.TurbineAutoLoad: return s.BopAutoLoad ? 1.0 : 0.0;
            case IntentKind.GeneratorBreaker: return s.GeneratorSynced ? 1.0 : 0.0;
            case IntentKind.ResetBopTrips: return s.TurbineTripped ? 1.0 : 0.0;
            case IntentKind.AcknowledgeAlarms: return s.AlarmAckSeq;
            default: return double.NaN;
        }
    }

    public static double ExpectedButton(ControlSpec spec, Simulation sim, double before)
    {
        switch (spec.Kind)
        {
            case IntentKind.Scram: return 1.0;
            case IntentKind.ResetScram: return 0.0;
            case IntentKind.ReliefValve: return 1.0 - before;
            case IntentKind.PauseClock: return 1.0;
            case IntentKind.ResumeClock: return 0.0;
            case IntentKind.SetTimeScale:
                return Math.Min(sim.Clock.Tuning.MaxScale, Math.Max(sim.Clock.Tuning.MinScale, before * spec.ButtonValue));
            case IntentKind.SkipAhead: return before + spec.ButtonValue / 60.0;
            case IntentKind.TurbineAutoLoad: return 1.0 - before;
            case IntentKind.GeneratorBreaker: return 1.0 - before;
            case IntentKind.ResetBopTrips: return 0.0;
            case IntentKind.AcknowledgeAlarms: return before + 1.0;
            default: return double.NaN;
        }
    }

    public static void CheckButton(ControlSpec spec, double before, double after, double want, Verdict v)
    {
        if (double.IsNaN(want)) { v.Fail("test setup: no expected state for " + spec.Kind); return; }
        if (Math.Abs(want - before) < 1e-9) { v.Fail("test setup: " + spec.Kind + " already in the expected state " + want); return; }
        if (spec.Kind == IntentKind.SkipAhead)
        {
            // A skip may end early on an alarm; it must at least advance the game clock by half the span.
            if (after < before + 0.5 * (want - before)) v.Fail("game clock advanced " + (after - before) + " min, expected about " + (want - before));
            return;
        }
        if (Math.Abs(after - want) > 1e-6) v.Fail("state is " + after + " after, expected " + want + " (from " + before + ")");
    }

    // ---- intent log ----------------------------------------------------------------------------------------------

    public static void CheckLog(List<IntentLogEntry> log, long seqFrom, ControlSpec spec, double targetValue, int expectedHits, Verdict v)
    {
        var hits = new List<IntentLogEntry>();
        foreach (var e in log)
            if (e.Seq >= seqFrom && e.Intent != null && e.Intent.Kind == spec.Kind) hits.Add(e);
        if (hits.Count == 0) { v.Fail("intent log holds no " + spec.Kind + " entry"); return; }
        if (hits.Count != expectedHits) v.Fail("intent log holds " + hits.Count + " " + spec.Kind + " entries, expected " + expectedHits);
        foreach (var e in hits)
        {
            if (e.Rejected) v.Fail(spec.Kind + " was rejected: " + e.Error);
            if (e.Intent.Issuer != Issuer) v.Fail(spec.Kind + " was issued by '" + e.Intent.Issuer + "', expected '" + Issuer + "'");
        }
        if (spec.Style == ControlStyle.Dial && !double.IsNaN(targetValue))
        {
            var last = hits[hits.Count - 1];
            if (Math.Abs(last.Intent.Value - targetValue) > 1e-6)
                v.Fail(spec.Kind + " intent carries " + last.Intent.Value + ", expected " + targetValue);
            if (spec.Discrete && Math.Abs(last.Intent.Value - Math.Round(last.Intent.Value)) > 1e-12)
                v.Fail(spec.Kind + " stepped knob sent a non-integer " + last.Intent.Value);
        }
    }

    // ---- indicators ----------------------------------------------------------------------------------------------

    /// <summary>How far the pivot should move for the value change: degrees of needle for a gauge, metres for a bar.</summary>
    public static double PoseSpan(GaugeSource src, bool isBar, PlantSnapshot a, PlantSnapshot b)
    {
        double fa = NeedleMath.Fraction(src.Select(a), src.Min, src.Max);
        double fb = NeedleMath.Fraction(src.Select(b), src.Min, src.Max);
        double span = isBar ? GaugeGeometry.TravelM(src.Name) : GaugeGeometry.NeedleSweepMaxDeg - GaugeGeometry.NeedleSweepMinDeg;
        return Math.Abs(fb - fa) * span;
    }

    public static LampContext LampContextFor(Simulation sim, PlantSnapshot previous, bool clockHalted)
    {
        return new LampContext
        {
            ShortPeriodSeconds = sim.Clock.Tuning.AutoSlowPeriodSeconds,
            ClockPausedOrFrozen = clockHalted,
            Previous = previous
        };
    }
}
// ---- PURE END
#endif
