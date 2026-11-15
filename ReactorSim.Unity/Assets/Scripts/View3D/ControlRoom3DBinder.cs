using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using TMPro;
using ReactorSim;
using ReactorSim.Controls;
using ReactorSim.Instruments;

// Phase 2 diegetic 3D control-room binder: binds Blender pivots (BIND gauges/bars/lamps, INT controls)
// to PlantSnapshot fields and PlantIntent submissions via PlantBridge.
//  - gauges and bars glide between the last two sim snapshots using the clock's tick fraction
//  - INT_* controls are continuous drags / buttons parsed from the pivot name (PivotNameParser)
//  - hover shows the control name, its plant value and any queued (not yet applied) target
// Attach to the ControlRoom prefab instance placed in Bootstrap.unity.
public class ControlRoom3DBinder : MonoBehaviour
{
    // Kit axes in Unity. Blender's FBX export converts axes only at the room root, so every kit keeps its Blender
    // local frame (X across the face, Y out of the face toward the operator, Z up the face). In Unity that is:
    public static readonly Vector3 KitFaceNormal = Vector3.up;     // needles and knobs turn about it; buttons press along -it
    public static readonly Vector3 KitFaceUp = Vector3.forward;    // edgewise bars slide along it
    /// <summary>The face's right as the operator sees it (looking down -KitFaceNormal with KitFaceUp up): derived, not assumed.</summary>
    public static readonly Vector3 KitFaceRight = Vector3.Cross(KitFaceUp, -KitFaceNormal);
    public static readonly Vector3 KitAcross = Vector3.right;      // rod-bank levers pitch about it

    public PlantBridge bridge;

    private const string Issuer = "diegetic";
    private const float SubmitIntervalSeconds = 0.1f;
    private const float ScramCoverSeconds = 3f;

    private sealed class GaugeBinding
    {
        public Transform Pivot;
        public GaugeSource Source;
        public bool IsBar;
        public float Travel;
        public float RestAlong;   // authored rest position along KitFaceUp (mid-travel)
    }

    private sealed class CounterBinding
    {
        public Transform Pivot;
        public GaugeSource Source;
        public TextMeshPro Text;
        public string Shown = "";
        public long RawBits;       // bit pattern of the last value formatted (exact compare, NaN-safe)
        public bool HasRaw;
    }

    // One BIND_LAMP_ tile group with its lamp name resolved once at bind time.
    private sealed class LampTile
    {
        public string Key;                 // BIND_LAMP_<name>
        public string Name;                // <name>
        public bool Known;                 // LampPredicates.Has(Name)
        public bool IsMode;
        public LampStyle Style;
        public List<Renderer> Renderers;
        public int Shown;                  // last state written (0 = nothing yet), see LampCode
    }

    private sealed class ControlState
    {
        public DiegeticInteractable Interactable;
        public ControlSpec Spec;
        public Vector3 BasePos;
        public Quaternion BaseRot;
        public float ShownFrac = -1f;
        public float PressedUntil;
        // Kit auxiliary cover pivot (PART_Guard / PART_ScramCover), a sibling of the INT_ pivot; null when absent.
        public Transform AuxPivot;
        public Quaternion AuxRest;
        public float AuxFrac = -1f;
    }

    private sealed class Pending
    {
        public double Value;
        public double SimSecondsAtSubmit;
    }

    private readonly List<GaugeBinding> gauges = new List<GaugeBinding>();
    private readonly List<CounterBinding> counters = new List<CounterBinding>();
    private readonly AlarmTracker alarms = new AlarmTracker();
    private static readonly string[] AlarmLamps = new List<string>(LampPredicates.AlarmNames).ToArray();
    private PlantSnapshot alarmSnap;
    private LampContext alarmCtx;
    private System.Func<string, bool> alarmRaised;
    private readonly Dictionary<string, int> lastDetent = new Dictionary<string, int>();
    private readonly Dictionary<string, List<Renderer>> lampTiles =
        new Dictionary<string, List<Renderer>>(System.StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> lampRowIds =
        new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> lampFlashUntil =
        new Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase);
    private readonly List<ControlState> controls = new List<ControlState>();
    private readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
    private readonly Dictionary<string, double> lastSubmitted = new Dictionary<string, double>();
    private readonly Dictionary<string, float> lastSubmitTime = new Dictionary<string, float>();
    private readonly Dictionary<string, double> dragStartFrac = new Dictionary<string, double>();
    private readonly Dictionary<string, double> dragStartValue = new Dictionary<string, double>();
    private readonly Dictionary<string, Pending> preDragPending = new Dictionary<string, Pending>();
    private DiegeticInteractable highlighted;
    private readonly List<Renderer> highlightRenderers = new List<Renderer>();
    private string readoutOverride;
    private float readoutOverrideUntil;
    private MaterialPropertyBlock mpb;
    private readonly List<LampTile> lampList = new List<LampTile>();
    private static int colorId, emissionColorId;

    private PlantSnapshot prevSnap;
    private PlantSnapshot curSnap;

    private string actionText = "";
    private float actionUntil;
    private float scramArmedUntil;
    private GameObject readoutBox, noticeBox;
    private TMPro.TMP_Text readoutLabel, noticeLabel;

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        colorId = Shader.PropertyToID("_Color");
        emissionColorId = Shader.PropertyToID("_EmissionColor");
        if (bridge == null)
            bridge = FindAnyObjectByType<PlantBridge>();
        if (bridge == null) return;

        BindGauges();
        BindLamps();
    }

    // ---- binding -------------------------------------------------------------------------------------------

    private void BindGauges()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            bool gauge = n.StartsWith("BIND_GAUGE_");
            bool bar = n.StartsWith("BIND_BAR_");
            if (!gauge && !bar) continue;
            GaugeSource src;
            if (!GaugeSources.TryResolve(n, out src))
            {
                Debug.LogWarning("[ControlRoom3DBinder] no value source for " + n);
                continue;
            }
            if (gauge && CounterDisplay.IsCounterPivot(t))
            {
                // K04 seven-segment counter: nothing under the pivot to rotate, so the value is drawn as digits.
                counters.Add(new CounterBinding { Pivot = t, Source = src, Text = CounterDisplay.Attach(t) });
                continue;
            }
            if (gauge) GaugeNumerals.Attach(t, src);   // circular dials get min / mid / max numerals and the unit
            else GaugeNumerals.AttachBar(t, src, GaugeGeometry.TravelM(src.Name));   // bars get min / max numerals and the unit
            gauges.Add(new GaugeBinding
            {
                Pivot = t,
                Source = src,
                IsBar = bar,
                Travel = GaugeGeometry.TravelM(src.Name),
                RestAlong = Vector3.Dot(t.localPosition, KitFaceUp)
            });
        }
    }

    private void BindLamps()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("BIND_LAMP_")) continue;
            var r = t.GetComponent<Renderer>();
            if (r == null) continue;
            List<Renderer> list;
            if (!lampTiles.TryGetValue(t.name, out list))
            {
                list = new List<Renderer>();
                lampTiles[t.name] = list;
            }
            list.Add(r);
            if (!lampRowIds.ContainsKey(t.name))
            {
                // The tile's inventory row: its CTL_<row id> ancestor.
                for (var a = t.parent; a != null; a = a.parent)
                    if (a.name.StartsWith("CTL_")) { lampRowIds[t.name] = a.name.Substring(4); break; }
            }
        }
        lampList.Clear();
        foreach (var kv in lampTiles)
        {
            string name = kv.Key.Substring("BIND_LAMP_".Length);
            bool known = LampPredicates.Has(name);
            lampList.Add(new LampTile
            {
                Key = kv.Key,
                Name = name,
                Known = known,
                IsMode = name == "MODE",
                Style = known ? LampPredicates.Style(name) : LampStyle.AmberSteady,
                Renderers = kv.Value
            });
        }
    }

    /// <summary>Called by each DiegeticInteractable once its pivot name parsed as a control.</summary>
    public void Register(DiegeticInteractable inter)
    {
        if (inter == null || inter.Spec == null) return;
        var state = new ControlState
        {
            Interactable = inter,
            Spec = inter.Spec,
            BasePos = inter.transform.localPosition,
            BaseRot = inter.transform.localRotation
        };
        state.AuxPivot = FindAuxPivot(inter.transform);
        if (state.AuxPivot != null) state.AuxRest = state.AuxPivot.localRotation;
        controls.Add(state);
    }

    // The kit's auxiliary cover pivot (PART_Guard on guarded buttons, PART_ScramCover on the SCRAM button),
    // authored as a sibling of the INT_ pivot under the CTL_ root so it never inherits press motion.
    private static Transform FindAuxPivot(Transform pivot)
    {
        Transform root = pivot;
        while (root != null && !root.name.StartsWith("CTL_")) root = root.parent;
        if (root == null) root = pivot.parent;
        if (root == null) return null;
        foreach (Transform c in root.GetComponentsInChildren<Transform>(true))
        {
            if (c == pivot || c.IsChildOf(pivot)) continue;
            string n = c.name;
            if (n.EndsWith("_PART_Guard", System.StringComparison.OrdinalIgnoreCase)
                || n.EndsWith("_PART_ScramCover", System.StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return null;
    }

    // ---- per frame -----------------------------------------------------------------------------------------

    private void Update()
    {
        if (bridge == null || bridge.Sim == null) return;
        var s = bridge.GetSnapshot();
        if (s == null) return;
        if (curSnap == null) { curSnap = s; prevSnap = s; }
        else if (s.SimSeconds != curSnap.SimSeconds) { prevSnap = curSnap; curSnap = s; }

        ExpirePending(s);
        UpdateHud();
        UpdateHoverHighlight();
        UpdateGauges();
        UpdateCounters();
        UpdateLamps(s);
        UpdateControlVisuals(s);
    }

    private void UpdateGauges()
    {
        double f = bridge.TickFraction;
        for (int i = 0; i < gauges.Count; i++)
        {
            var g = gauges[i];
            if (g.Pivot == null) continue;
            double v = NeedleMath.Interpolate(g.Source.Select(prevSnap), g.Source.Select(curSnap), f);
            float frac = (float)NeedleMath.Fraction(v, g.Source.Min, g.Source.Max);
            if (g.IsBar) SlidePivot(g, frac);
            // Clockwise as seen from the operator (looking down -KitFaceNormal): frac 0 lower-left, 1 lower-right.
            else g.Pivot.localRotation = Quaternion.AngleAxis(GaugeGeometry.NeedleDeg(frac), KitFaceNormal);
        }
    }

    private void UpdateCounters()
    {
        for (int i = 0; i < counters.Count; i++)
        {
            var c = counters[i];
            if (c.Text == null) continue;
            double raw = c.Source.Select(curSnap);
            long bits = System.BitConverter.DoubleToInt64Bits(raw);
            if (c.HasRaw && bits == c.RawBits) continue;   // same value as last formatted: same digits
            c.RawBits = bits;
            c.HasRaw = true;
            string text = CounterText.Format(raw);
            if (text == c.Shown) continue;
            c.Shown = text;
            c.Text.text = text;
        }
    }

    /// <summary>The digits a counter pivot currently shows (round-trip and layout checks), or null when it is not a counter.</summary>
    public string CounterTextOf(Transform pivot)
    {
        for (int i = 0; i < counters.Count; i++)
            if (counters[i].Pivot == pivot) return counters[i].Shown;
        return null;
    }

    private static void SlidePivot(GaugeBinding g, float frac)
    {
        // Per-row travel (GaugeGeometry.TravelM), centred on the pivot's authored rest position.
        var p = g.Pivot.localPosition;
        float along = GaugeGeometry.BarY(g.RestAlong, g.Travel, frac);
        g.Pivot.localPosition = p + KitFaceUp * (along - Vector3.Dot(p, KitFaceUp));
    }

    // MODE lights for a few seconds after the operating mode changes (a single sim tick is too short to see).
    private const float ModeLampSeconds = 6f;
    private float modeLitUntil;

    private void UpdateLamps(PlantSnapshot s)
    {
        var ctx = CurrentLampContext();
        if (LampPredicates.Evaluate("MODE", s, ctx)) modeLitUntil = Time.time + ModeLampSeconds;
        // Annunciator acknowledge (U17): raised alarms flash until AcknowledgeAlarms, hold steady after, clear with the condition.
        alarmSnap = s;
        alarmCtx = ctx;
        if (alarmRaised == null) alarmRaised = n => LampPredicates.Evaluate(n, alarmSnap, alarmCtx);
        alarms.Update(AlarmLamps, alarmRaised, s.AlarmAckSeq);
        bool anyFlash = lampFlashUntil.Count > 0;
        for (int i = 0; i < lampList.Count; i++)
        {
            var tile = lampList[i];
            // U14 "find it": a located tile overrides its normal state for a few seconds.
            float until;
            if (anyFlash && lampFlashUntil.TryGetValue(tile.Key, out until) && Time.time < until)
            {
                bool hi = Mathf.Repeat(Time.time, 0.5f) < 0.25f;
                var fc = hi ? new Color(0.6f, 0.95f, 1f, 1f) : LampOff;
                SetLampRaw(tile, hi ? CodeFindHigh : CodeFindLow, fc, hi ? fc * 3f : Color.black);
                continue;
            }
            if (!tile.Known) continue;
            bool on = tile.IsMode ? Time.time < modeLitUntil : LampPredicates.Evaluate(tile.Name, s, ctx);
            SetLamp(tile, on, on && alarms.StateOf(tile.Name) == AlarmState.Unacked);
        }
    }

    /// <summary>Lamp inputs as UpdateLamps builds them (the tablet's alarm page evaluates the same predicates).</summary>
    public LampContext CurrentLampContext()
    {
        return new LampContext
        {
            ShortPeriodSeconds = bridge != null && bridge.Clock != null ? bridge.Clock.Tuning.AutoSlowPeriodSeconds : 60.0,
            ClockPausedOrFrozen = bridge != null && bridge.Clock != null && (bridge.Clock.Paused || bridge.Clock.Frozen),
            Previous = prevSnap
        };
    }

    /// <summary>Lamp names (without BIND_LAMP_) that have a tile in the room.</summary>
    public IEnumerable<string> LampTileNames()
    {
        foreach (var k in lampTiles.Keys) yield return k.Substring("BIND_LAMP_".Length);
    }

    /// <summary>Inventory row id of a lamp's tile (its CTL_ parent), or empty.</summary>
    public string LampRowId(string lampName)
    {
        string id;
        return lampRowIds.TryGetValue("BIND_LAMP_" + lampName, out id) ? id : "";
    }

    /// <summary>World centre of a lamp's tile; false when the room has no tile for it.</summary>
    public bool TryGetLampPoint(string lampName, out Vector3 point)
    {
        point = Vector3.zero;
        List<Renderer> list;
        if (!lampTiles.TryGetValue("BIND_LAMP_" + lampName, out list) || list.Count == 0 || list[0] == null) return false;
        point = list[0].bounds.center;
        return true;
    }

    /// <summary>Makes a lamp's tile blink cyan for a few seconds regardless of its state (U14 find-it).</summary>
    public void FlashLamp(string lampName, float seconds)
    {
        lampFlashUntil["BIND_LAMP_" + lampName] = Time.time + seconds;
    }

    private static readonly Color LampOff = new Color(0.12f, 0.08f, 0.05f, 1f);
    private static readonly Color LampAmber = new Color(1f, 0.62f, 0.1f, 1f);
    private static readonly Color LampRed = new Color(1f, 0.12f, 0.08f, 1f);
    private static readonly Color LampGreen = new Color(0.15f, 1f, 0.3f, 1f);

    /// <summary>Acknowledge state of an alarm lamp (name without BIND_LAMP_): Clear, Unacked (flashing) or Acked (steady).</summary>
    public AlarmState AlarmStateOf(string lampName)
    {
        return alarms.StateOf(lampName);
    }

    /// <summary>True while any alarm is raised and unacknowledged.</summary>
    public bool AnyAlarmUnacked { get { return alarms.AnyUnacked; } }

    // flash: the tile pulses (every colour, so a flashing amber tile is not just a lit one); otherwise it holds steady.
    // The colours are a pure function of (tile style, state code), so a tile whose code did not change since the
    // last write keeps its property block untouched. Codes: off, on steady, on at the low flash phase, find-it phases.
    private const int CodeOff = 1, CodeOn = 2, CodeOnDim = 3, CodeFindHigh = 4, CodeFindLow = 5;

    private void SetLamp(LampTile tile, bool on, bool flash)
    {
        var style = tile.Style;
        Color baseColor = style == LampStyle.GreenSteady ? LampGreen
            : (style == LampStyle.RedFlash || style == LampStyle.RedSteady) ? LampRed : LampAmber;
        float pulse = 1f;
        if (flash) pulse = Mathf.Repeat(Time.time, 0.5f) < 0.25f ? 1f : 0.2f;
        int code = !on ? CodeOff : (pulse < 1f ? CodeOnDim : CodeOn);
        if (tile.Shown == code) return;
        Color c = on ? baseColor * pulse : LampOff;
        c.a = 1f;
        Color emission = on ? baseColor * (2.2f * pulse) : Color.black;
        SetLampRaw(tile, code, c, emission);
    }

    private void SetLampRaw(LampTile tile, int code, Color c, Color emission)
    {
        if (tile.Shown == code) return;
        tile.Shown = code;
        var list = tile.Renderers;
        c.a = 1f;
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(colorId, c);
            mpb.SetColor(emissionColorId, emission);
            r.SetPropertyBlock(mpb);
        }
    }

    private void UpdateControlVisuals(PlantSnapshot s)
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < controls.Count; i++)
        {
            var c = controls[i];
            if (c.Interactable == null) continue;
            var t = c.Interactable.transform;
            if (c.Spec.Style == ControlStyle.Dial)
            {
                double v = ShownValue(c.Spec, s);
                float target = (float)DragMapper.FractionFromValue(c.Spec, v);
                c.ShownFrac = c.ShownFrac < 0f ? target : Mathf.Lerp(c.ShownFrac, target, 1f - Mathf.Exp(-14f * dt));
                // ANIM table (Blender lib_common): knob sweep +-150 deg about the face normal (clockwise to raise),
                // rod-bank lever sweep +-40 deg about the across-the-face axis.
                if (c.Spec.IsRodBank)
                    t.localRotation = c.BaseRot * Quaternion.AngleAxis(Mathf.Lerp(-40f, 40f, c.ShownFrac), KitAcross);
                else
                    t.localRotation = c.BaseRot * Quaternion.AngleAxis(-Mathf.Lerp(-150f, 150f, c.ShownFrac), KitFaceNormal);
            }
            else
            {
                bool down = Time.time < c.PressedUntil || ButtonOn(c.Spec, s);
                float target = down ? 1f : 0f;
                c.ShownFrac = c.ShownFrac < 0f ? target : Mathf.Lerp(c.ShownFrac, target, 1f - Mathf.Exp(-20f * dt));
                // Plunger: 0 .. 0.025 m into the face.
                t.localPosition = c.BasePos - KitFaceNormal * (0.025f * c.ShownFrac);
            }
            if (c.AuxPivot != null)
            {
                // Blender pose_aux_deg -100 about the across-face axis swings the cover toward the operator
                // (verified against the imported FBX: AngleAxis(-100, local X) moves the cover's free edge +face-normal).
                bool scramCover = c.AuxPivot.name.EndsWith("_PART_ScramCover", System.StringComparison.OrdinalIgnoreCase);
                bool open = scramCover
                    ? c.Spec.Kind == IntentKind.Scram && Time.time < scramArmedUntil
                    : c.Interactable != null && c.Interactable.GuardOpen;
                float at = open ? 1f : 0f;
                c.AuxFrac = c.AuxFrac < 0f ? at : Mathf.Lerp(c.AuxFrac, at, 1f - Mathf.Exp(-10f * dt));
                c.AuxPivot.localRotation = c.AuxRest * Quaternion.AngleAxis(-100f * c.AuxFrac, KitAcross);
            }
        }
    }

    // ---- control state -------------------------------------------------------------------------------------

    /// <summary>Transient plunger travel for a rejected press: the control dips and returns, nothing is submitted.</summary>
    public void RejectPress(DiegeticInteractable inter)
    {
        for (int i = 0; i < controls.Count; i++)
            if (controls[i].Interactable == inter)
            {
                controls[i].PressedUntil = Time.time + 0.12f;
                return;
            }
    }

    private bool ButtonOn(ControlSpec spec, PlantSnapshot s)
    {
        if (spec.Kind == IntentKind.PauseClock || spec.Kind == IntentKind.ResumeClock)
            return bridge.Clock != null && bridge.Clock.Paused;
        return ControlValues.ButtonOn(spec, s);
    }

    private static string Key(ControlSpec spec) { return spec.BaseName; }

    private double ShownValue(ControlSpec spec, PlantSnapshot s)
    {
        Pending p;
        if (pending.TryGetValue(Key(spec), out p)) return p.Value;
        return ControlValues.Commanded(spec, s);
    }

    private bool IsQueued(ControlSpec spec, PlantSnapshot s)
    {
        Pending p;
        if (!pending.TryGetValue(Key(spec), out p)) return false;
        return bridge.Clock == null || bridge.Clock.Halted || s.SimSeconds <= p.SimSecondsAtSubmit;
    }

    private void ExpirePending(PlantSnapshot s)
    {
        if (pending.Count == 0) return;
        if (bridge.Clock != null && bridge.Clock.Halted) return;
        List<string> done = null;
        foreach (var kv in pending)
        {
            if (s.SimSeconds > kv.Value.SimSecondsAtSubmit)
            {
                if (done == null) done = new List<string>();
                done.Add(kv.Key);
            }
        }
        if (done != null)
            for (int i = 0; i < done.Count; i++) pending.Remove(done[i]);
    }

    // ---- interaction (called by DiegeticInteractable) ------------------------------------------------------

    public double StartFraction(DiegeticInteractable inter)
    {
        var spec = inter.Spec;
        var s = bridge != null ? bridge.GetSnapshot() : null;
        string key = Key(spec);
        lastSubmitted.Remove(key);
        double shown = ShownValue(spec, s);
        double frac = DragMapper.FractionFromValue(spec, shown);
        dragStartFrac[key] = frac;
        dragStartValue[key] = shown;
        Pending prior;
        if (pending.TryGetValue(key, out prior)) preDragPending[key] = new Pending { Value = prior.Value, SimSecondsAtSubmit = prior.SimSecondsAtSubmit };
        else preDragPending.Remove(key);
        lastDetent[Key(spec)] = ControlSounds.DetentIndex(spec, DragMapper.ValueFromFraction(spec, frac));
        return frac;
    }

    public void DragTo(DiegeticInteractable inter, double frac, bool release)
    {
        if (bridge == null || bridge.Sim == null) return;
        var spec = inter.Spec;
        string key = Key(spec);
        double value = DragMapper.ValueFromFraction(spec, frac);
        var s = bridge.GetSnapshot();
        double simNow = s != null ? s.SimSeconds : 0.0;

        // The control shows where the player put it straight away, even before the plant applies it.
        Pending p;
        if (!pending.TryGetValue(key, out p)) { p = new Pending(); pending[key] = p; }
        p.Value = value;
        bool moved = true;
        double start;
        if (dragStartFrac.TryGetValue(key, out start) && System.Math.Abs(start - frac) < 1e-9 && !lastSubmitted.ContainsKey(key))
            moved = false;
        if (!moved)
        {
            if (release) pending.Remove(key);
            return;
        }

        double last;
        bool hasLast = lastSubmitted.TryGetValue(key, out last);
        bool changed = !hasLast || System.Math.Abs(last - value) > 1e-9;
        float lastT;
        bool due = !lastSubmitTime.TryGetValue(key, out lastT) || Time.unscaledTime - lastT >= SubmitIntervalSeconds;
        bool send = changed && (release || (!spec.Discrete && due));
        if (!send) return;

        var intent = ControlIntentFactory.BuildDial(spec, value, Issuer);
        if (intent == null) return;
        PlayDetent(spec, key, value);
        p.SimSecondsAtSubmit = simNow;
        lastSubmitted[key] = value;
        lastSubmitTime[key] = Time.unscaledTime;
        bridge.SubmitIntent(intent);
        Flash(Label(spec) + " -> " + FormatValue(spec, value) + (IsQueued(spec, s) ? " (queued)" : ""));
    }

    /// <summary>
    /// A drag was cancelled (Escape, right click, focus lost): the dial goes back to its pre-drag state. Nothing is submitted
    /// unless the drag had already sent a value mid-drag, in which case the original value is sent once to put the plant back.
    /// </summary>
    public void CancelDrag(DiegeticInteractable inter)
    {
        if (bridge == null || bridge.Sim == null || inter == null || inter.Spec == null) return;
        var spec = inter.Spec;
        string key = Key(spec);
        double original;
        bool sent = lastSubmitted.ContainsKey(key);
        lastSubmitted.Remove(key);
        Pending prior;
        if (sent && dragStartValue.TryGetValue(key, out original) && !double.IsNaN(original))
        {
            var intent = ControlIntentFactory.BuildDial(spec, original, Issuer);
            if (intent != null)
            {
                var s = bridge.GetSnapshot();
                pending[key] = new Pending { Value = original, SimSecondsAtSubmit = s != null ? s.SimSeconds : 0.0 };
                lastSubmitTime[key] = Time.unscaledTime;
                bridge.SubmitIntent(intent);
                Flash(Label(spec) + " restored to " + FormatValue(spec, original));
                return;
            }
        }
        if (preDragPending.TryGetValue(key, out prior)) pending[key] = prior;
        else pending.Remove(key);
        Flash(Label(spec) + " unchanged");
    }

    /// <summary>One-line on-screen note (a click that did nothing says why).</summary>
    public void Notice(string text)
    {
        if (!string.IsNullOrEmpty(text)) Flash(text);
    }

    /// <summary>Capture runs only: shows a drag readout without a real drag.</summary>
    public void ShowReadout(string text, float seconds)
    {
        readoutOverride = text;
        readoutOverrideUntil = Time.unscaledTime + seconds;
    }

    // Operable controls light up under the cursor; indicators and out-of-service rows never do.
    private void UpdateHoverHighlight()
    {
        var h = DiegeticInteractable.Hovered;
        if (h != null && !h.Operable) h = null;
        if (h == highlighted) return;
        for (int i = 0; i < highlightRenderers.Count; i++)
            if (highlightRenderers[i] != null) highlightRenderers[i].SetPropertyBlock(null);
        highlightRenderers.Clear();
        highlighted = h;
        if (h == null) return;
        h.GetComponentsInChildren(highlightRenderers);
        for (int i = 0; i < highlightRenderers.Count; i++)
        {
            var r = highlightRenderers[i];
            var m = r.sharedMaterial;
            if (m == null || !m.HasProperty(colorId)) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(colorId, Color.Lerp(m.GetColor(colorId), ThemeColors.HighlightPulse, 0.45f));
            r.SetPropertyBlock(mpb);
        }
    }

    public void PressButton(DiegeticInteractable inter)
    {
        if (bridge == null || bridge.Sim == null) return;
        var spec = inter.Spec;
        var s = bridge.GetSnapshot();

        // SCRAM cover: the first press lifts it, a second press within a few seconds trips the reactor.
        if (spec.Kind == IntentKind.Scram && Time.time > scramArmedUntil)
        {
            scramArmedUntil = Time.time + ScramCoverSeconds;
            Flash("SCRAM cover lifted - press again to trip");
            return;
        }

        double scale = bridge.Clock != null ? bridge.Clock.Scale : 1.0;
        var intent = ControlIntentFactory.BuildButton(spec, ButtonOn(spec, s), scale, Issuer);
        if (intent == null) return;
        if (spec.Kind == IntentKind.Scram) scramArmedUntil = 0f;

        foreach (var c in controls)
            if (c.Interactable == inter) c.PressedUntil = Time.time + 0.25f;
        var audio = ProceduralPlantAudio.Instance;
        if (audio != null) audio.PlayControl(ControlSounds.For(spec));

        bridge.SubmitIntent(intent);
        bool halted = bridge.Clock != null && bridge.Clock.Halted;
        bool appliesWhileHalted = Simulation.AppliesWhileHalted(intent.Kind, intent.Class);
        Flash(Label(spec) + (halted && !appliesWhileHalted ? " (queued)" : " sent"));
    }

    // A detent tick whenever a dial passes into a new detent (continuous dials: every 5 percent; stepped knobs: each step).
    private void PlayDetent(ControlSpec spec, string key, double value)
    {
        int idx = ControlSounds.DetentIndex(spec, value);
        int last;
        if (lastDetent.TryGetValue(key, out last) && last == idx) return;
        lastDetent[key] = idx;
        var audio = ProceduralPlantAudio.Instance;
        if (audio != null) audio.PlayControl(ControlSounds.For(spec));
    }

    private void Flash(string text)
    {
        actionText = text;
        actionUntil = Time.unscaledTime + 1.6f;
    }

    // ---- hover HUD -----------------------------------------------------------------------------------------

    public static string Label(ControlSpec spec)
    {
        switch (spec.Kind)
        {
            case IntentKind.RodBankTarget:
                return "Rod Bank " + (spec.RodBank >= 0 && spec.RodBank < 26 ? ((char)('A' + spec.RodBank)).ToString() : spec.Target);
            case IntentKind.PauseClock:
            case IntentKind.ResumeClock: return "Pause / Resume";
            case IntentKind.SetTimeScale: return spec.ButtonValue >= 1.0 ? "Pace Up" : "Pace Down";
            case IntentKind.SkipAhead: return "Skip Ahead " + (spec.ButtonValue / 60.0).ToString("0.#", CultureInfo.InvariantCulture) + " min";
            case IntentKind.ReliefValve: return "Relief Valve (PORV)";
            case IntentKind.Scram: return "SCRAM";
            case IntentKind.ResetScram: return "Reset SCRAM";
            default: return SplitCamel(spec.Kind.ToString());
        }
    }

    private static string SplitCamel(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length + 4);
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static string FormatValue(ControlSpec spec, double v)
    {
        if (double.IsNaN(v)) return "--";
        return spec.Discrete ? ((int)System.Math.Round(v)).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Hover tooltip for the control or indicator under the cursor (ControlTooltip draws it): label, live value, setpoint,
    /// limits and description. Null when there is nothing to say.
    /// </summary>
    public TooltipText BuildTooltip(DiegeticInteractable h, PlantSnapshot s, RuntimeInventory inv)
    {
        if (h == null || s == null) return null;
        RuntimeRow row = inv != null ? inv.Find(h.rowId) : null;
        if (h.OutOfService) return ControlTooltipModel.ForOutOfService(row, h.rowLabel, h.outOfServiceReason);
        if (h.pinnable) return ControlTooltipModel.ForIndicator(row, h.pinSelector, h.pinIndex, s, h.rowLabel, h.HoverNote);
        var spec = h.Spec;
        if (spec == null) return null;
        string title = string.IsNullOrEmpty(h.rowLabel) ? Label(spec) : h.rowLabel;
        string state = "";
        if (spec.Style == ControlStyle.Button)
        {
            if (spec.Kind == IntentKind.PauseClock || spec.Kind == IntentKind.ResumeClock)
                state = ClockReadout.Format(ClockReadout.FromClock(bridge.Clock));
            else if (spec.Kind == IntentKind.SetTimeScale || spec.Kind == IntentKind.SkipAhead)
                state = "clock " + ClockReadout.Format(ClockReadout.FromClock(bridge.Clock));
            else if (spec.Kind == IntentKind.ReliefValve)
                state = s.ReliefOpen ? "OPEN" : "closed";
            else if (spec.Kind == IntentKind.TurbineAutoLoad)
                state = s.BopAutoLoad ? "AUTO" : "MANUAL";
            else if (spec.Kind == IntentKind.GeneratorBreaker)
                state = s.GeneratorSynced ? "CLOSED (on grid)" : "OPEN";
            else if (spec.Kind == IntentKind.Scram)
                state = s.Scrammed ? "TRIPPED" : (Time.time <= scramArmedUntil ? "cover lifted - press to trip" : "cover closed");
            else if (spec.Kind == IntentKind.AcknowledgeAlarms)
                state = alarms.AnyUnacked ? "alarms flashing - press to acknowledge" : "no unacknowledged alarms";
        }
        double pendingValue = double.NaN;
        bool queued = false;
        Pending p;
        if (pending.TryGetValue(Key(spec), out p))
        {
            pendingValue = p.Value;
            queued = IsQueued(spec, s);
        }
        return ControlTooltipModel.ForControl(row, spec, s, title, pendingValue, queued, state, h.HoverNote);
    }

    // Drag readout (top centre) and the one-line action / rejected-press notice (bottom centre), as uGUI + TextMeshPro.
    private void EnsureHud()
    {
        if (readoutBox != null) return;
        var canvas = RuntimeUi.CreateCanvas("ControlHudCanvas", 18, transform);
        canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>().enabled = false;
        readoutBox = RuntimeUi.MakeToast(canvas.transform, "DragReadout", new Vector2(0.5f, 1f), new Vector2(0f, -170f), 0f,
            ReactorSim.Ui.TextRole.Body, TextAnchor.MiddleCenter, ThemeColors.TextBody, out readoutLabel).gameObject;
        noticeBox = RuntimeUi.MakeToast(canvas.transform, "ActionNotice", new Vector2(0.5f, 0f), new Vector2(0f, 24f), 0f,
            ReactorSim.Ui.TextRole.Caption, TextAnchor.MiddleCenter, ThemeColors.TextBody, out noticeLabel).gameObject;
        readoutBox.SetActive(false);
        noticeBox.SetActive(false);
    }

    private void UpdateHud()
    {
        EnsureHud();
        string readout = null;
        var d = DiegeticInteractable.Dragging;
        if (d != null && d.Drag != null && d.Drag.Started && d.Spec != null)
        {
            var row = RuntimeInventoryStore.Inventory.Find(d.rowId);
            double value = DragMapper.ValueFromFraction(d.Spec, d.Drag.Fraction);
            readout = ControlTooltipModel.DragReadout(string.IsNullOrEmpty(d.rowLabel) ? Label(d.Spec) : d.rowLabel, value,
                d.DragStartValue, row != null ? row.units : "", d.Spec.Discrete);
        }
        else if (readoutOverride != null && Time.unscaledTime < readoutOverrideUntil) readout = readoutOverride;
        SetBox(readoutBox, readoutLabel, readout);
        SetBox(noticeBox, noticeLabel, Time.unscaledTime < actionUntil ? actionText : null);
    }

    private static void SetBox(GameObject box, TMPro.TMP_Text label, string text)
    {
        if (box.activeSelf != (text != null)) box.SetActive(text != null);
        if (text != null && label.text != text) label.text = text;
    }
}
