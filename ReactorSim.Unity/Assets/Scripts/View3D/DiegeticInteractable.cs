using UnityEngine;
using ReactorSim;
using ReactorSim.Controls;

// Phase 2 diegetic control: attaches to a control pivot named INT_* (see PivotNameParser for the grammar).
// Buttons act on click. A dial is turned by click, hold, drag up or down and release (KTD2): a press does nothing by itself;
// once the pointer has travelled past a small dead zone the dial follows it (DialDrag), the cursor is locked so the drag
// cannot leave the window, a modifier slows the drag for fine adjustment, Escape or right-click cancels and restores the
// pre-drag value, and release commits once. The binder throttles submission while dragging and always sends on release.
//
// KTD4 rules: clicks and drags only act within OperatorMath.InteractionRange of the eye (hover still works);
// a guarded switch (kit K11, set by the scene builder from inventory.csv) needs two clicks, the first lifts the
// guard; an out-of-service row (the builder puts one on the kit root, no INT_ pivot) shows why and refuses clicks.
// Every click that does nothing says why on screen (ControlRoom3DBinder.Notice) and is logged with its reason.
//
// Runs before the menus (execution order -100) so a cancelling Escape or right-click is flagged for the rest of the frame:
// other scripts that read those keys check InputConsumedThisFrame.
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Collider))]
public class DiegeticInteractable : MonoBehaviour
{
    public ControlRoom3DBinder binder;

    [Header("Inventory row (set by ControlRoomSceneBuilder)")]
    public string rowId = "";
    [Tooltip("Full row name for the hover tooltip.")]
    public string rowLabel = "";
    public bool guarded;
    public bool outOfService;
    public string outOfServiceReason = "";

    [Header("Tablet pin (U14, set by ControlRoomSceneBuilder on indicator rows)")]
    [Tooltip("Hover-only indicator: no control, but right-click pins its readout to the operator tablet.")]
    public bool pinnable;
    public string pinSelector = "";
    public int pinIndex = -1;

    private const float GuardSeconds = 3f;
    private const float MoveLogSeconds = 0.25f;

    public ControlSpec Spec { get; private set; }
    public ParseStatus Status { get; private set; }
    public bool OutOfService { get { return outOfService; } }

    /// <summary>The control being dragged, or null.</summary>
    public static DiegeticInteractable Dragging { get; private set; }
    /// <summary>True while any control is being dragged; OperatorController ignores mouse-look and walking then.</summary>
    public static bool DragActive { get { return Dragging != null; } }
    /// <summary>The control under the cursor, or null.</summary>
    public static DiegeticInteractable Hovered { get; private set; }

    private static int consumedFrame = -1;
    /// <summary>True in the frame a drag consumed Escape or a right click (to cancel). Menus, panels, the tablet pin and mouse-look ignore those inputs then.</summary>
    public static bool InputConsumedThisFrame { get { return consumedFrame == Time.frameCount; } }
    /// <summary>Set by a right-click cancel: mouse-look stays off until the right button is released (OperatorController clears it).</summary>
    public static bool SuppressLook { get; private set; }
    public static void ReleaseLookSuppress() { SuppressLook = false; }

    /// <summary>The live drag of this control (null when not dragging).</summary>
    public DialDrag Drag { get; private set; }
    /// <summary>The value the dial showed when the press began.</summary>
    public double DragStartValue { get; private set; }

    private int dragBeganFrame;
    private float lastMoveLog;
    private float guardLiftedUntil;
    private Collider col;
    private ControlRoom3DBinder notifier;

    // Cached (RequireComponent guarantees one); re-fetched only if it was destroyed.
    private Collider Col
    {
        get
        {
            if (col == null) col = GetComponent<Collider>();
            return col;
        }
    }

    /// <summary>Row id for logs, or the object name when the row has none.</summary>
    public string RowName { get { return string.IsNullOrEmpty(rowId) ? name : rowId; } }

    /// <summary>World point the camera zooms toward when this control is hovered.</summary>
    public Vector3 FocusPoint
    {
        get
        {
            var c = Col;
            return c != null ? c.bounds.center : transform.position;
        }
    }

    /// <summary>True when the control is within interaction range of the camera.</summary>
    public bool InRange
    {
        get
        {
            var cam = Camera.main;
            if (cam == null) return true;
            var c = Col;
            Vector3 eye = cam.transform.position;
            Vector3 p = c != null ? c.ClosestPoint(eye) : transform.position;
            return OperatorMath.InInteractionRange(Vector3.Distance(eye, p));
        }
    }

    /// <summary>Extra tooltip line (out of service, too far, guard state), or empty.</summary>
    public string HoverNote
    {
        get
        {
            if (outOfService)
                return "Out of service" + (outOfServiceReason.Length > 0 ? ": " + outOfServiceReason : "");
            if (pinnable) return "";   // indicators are read, not operated: range does not matter
            if (!InRange)
                return "Too far - walk closer (within " + OperatorMath.InteractionRange.ToString("0.#") + " m)";
            if (guarded && Spec != null && Spec.Style == ControlStyle.Button)
                return Time.unscaledTime < guardLiftedUntil ? "Guard lifted - click again to operate" : "Guarded - click to lift the guard";
            return "";
        }
    }

    /// <summary>True for a control the player can operate (not an indicator, not out of service).</summary>
    public bool Operable { get { return Spec != null && !outOfService && !pinnable; } }

    private void Awake()
    {
        col = GetComponent<Collider>();
        if (outOfService || pinnable)
        {
            Status = ParseStatus.NotControl;
            return;
        }
        if (binder == null)
            binder = FindAnyObjectByType<ControlRoom3DBinder>();
        var r = PivotNameParser.Parse(name);
        Status = r.Status;
        Spec = r.Spec;
        if (r.Status == ParseStatus.Malformed)
            Debug.LogWarning("[DiegeticInteractable] " + r.Error);
        if (binder != null && Spec != null)
            binder.Register(this);
    }

    private void OnDisable()
    {
        if (Hovered == this) Hovered = null;
        if (Drag != null) CancelDrag("disabled");
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused && Drag != null) CancelDrag("focus_lost");
    }

    /// <summary>Capture runs only: makes this control the hovered one without a mouse.</summary>
    public static void ForceHover(DiegeticInteractable d) { Hovered = d; }

    private void OnMouseEnter()
    {
        if (Spec == null && !outOfService && !pinnable) return;
        Hovered = this;
        InputLog.Event(InputLogFormat.Hover, "row", RowName, "kind", Kind());
    }

    private void OnMouseExit()
    {
        if (Hovered == this && Drag == null) Hovered = null;
    }

    private string Kind()
    {
        if (outOfService) return "out_of_service";
        if (pinnable) return "indicator";
        if (Spec == null) return "unbound";
        return Spec.Style == ControlStyle.Button ? "button" : "dial";
    }

    /// <summary>Why a press on this row would do nothing, or null when it would act.</summary>
    public string RejectReason()
    {
        if (outOfService) return ControlTooltipModel.RejectOutOfService;
        if (pinnable) return ControlTooltipModel.RejectIndicator;
        if (binder == null) return ControlTooltipModel.RejectNoBinder;
        if (Spec == null) return ControlTooltipModel.RejectNoSpec;
        if (!InRange) return ControlTooltipModel.RejectOutOfRange;
        return null;
    }

    /// <summary>The one-line on-screen note for a rejected press.</summary>
    public string RejectNote(string reason)
    {
        switch (reason)
        {
            case ControlTooltipModel.RejectIndicator:
                return ControlTooltipModel.IndicatorNote(ControlTooltipModel.RelatedControl(RuntimeInventoryStore.Inventory, rowId));
            case ControlTooltipModel.RejectOutOfService:
            case ControlTooltipModel.RejectOutOfRange:
                return HoverNote;
            default:
                return (string.IsNullOrEmpty(rowLabel) ? RowName : rowLabel) + " is not wired to anything";
        }
    }

    /// <summary>Logs a rejected press and tells the player why on screen.</summary>
    public void Reject(string reason)
    {
        InputLog.Event(InputLogFormat.PressRejected, "row", RowName, "reason", reason);
        var n = binder != null ? binder : (notifier != null ? notifier : (notifier = FindAnyObjectByType<ControlRoom3DBinder>()));
        if (n != null) n.Notice(RejectNote(reason));
    }

    private void OnMouseDown()
    {
        if (Dragging != null) return;
        string reason = RejectReason();
        if (reason != null)
        {
            Reject(reason);
            return;
        }
        if (Spec.Style == ControlStyle.Button)
        {
            if (guarded)
            {
                if (Time.unscaledTime >= guardLiftedUntil)
                {
                    guardLiftedUntil = Time.unscaledTime + GuardSeconds; // first click lifts the guard
                    InputLog.Event(InputLogFormat.Press, "row", RowName, "kind", "guard_lift");
                    return;
                }
                guardLiftedUntil = 0f;
            }
            InputLog.Event(InputLogFormat.Press, "row", RowName, "kind", "button");
            binder.PressButton(this);
            return;
        }
        BeginDrag();
    }

    private void BeginDrag()
    {
        double frac = binder.StartFraction(this);
        DragStartValue = DragMapper.ValueFromFraction(Spec, frac);
        Drag = DialDrag.For(Spec, frac, UserSettings.Tuning);
        Dragging = this;
        dragBeganFrame = Time.frameCount;
        lastMoveLog = -1f;
        CursorPin.Lock();
        InputLog.Event(InputLogFormat.Press, "row", RowName, "kind", "dial");
        InputLog.Event(InputLogFormat.DragStart, "row", RowName, "start", InputLogFormat.Num(DragStartValue));
    }

    private void Update()
    {
        if (Drag == null) return;
        string cancel = CancelReason();
        if (cancel != null)
        {
            CancelDrag(cancel);
            return;
        }
        if (!Input.GetMouseButton(0))
        {
            Commit();
            return;
        }
        // Locking the cursor warps it to the window centre; that frame's delta is not the hand's.
        if (Time.frameCount <= dragBeganFrame + 1) return;
        bool fine = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        Drag.Move(Input.GetAxis("Mouse Y") * OperatorMath.MouseAxisToPixels, fine);
        if (!Drag.Started) return;
        binder.DragTo(this, Drag.Fraction, false);
        if (Time.unscaledTime - lastMoveLog >= MoveLogSeconds)
        {
            lastMoveLog = Time.unscaledTime;
            InputLog.Event(InputLogFormat.DragMove, "row", RowName,
                "value", InputLogFormat.Num(DragMapper.ValueFromFraction(Spec, Drag.Fraction)), "fine", fine ? "1" : "0");
        }
    }

    // Escape and right-click cancel a drag and are consumed so they do nothing else this frame; losing the window, the pause
    // menu opening or the row becoming unavailable cancel it the same way.
    private string CancelReason()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { consumedFrame = Time.frameCount; return "escape"; }
        if (Input.GetMouseButtonDown(1)) { consumedFrame = Time.frameCount; SuppressLook = true; return "right_click"; }
        if (!Application.isFocused) return "focus_lost";
        if (PauseMenu.IsOpen) return "pause_menu";
        if (outOfService || Spec == null || binder == null) return "unavailable";
        return null;
    }

    private void Commit()
    {
        double frac = Drag.Release();
        bool moved = Drag.Started;
        double value = DragMapper.ValueFromFraction(Spec, frac);
        if (moved) binder.DragTo(this, frac, true);
        InputLog.Event(InputLogFormat.DragCommit, "row", RowName, "value", InputLogFormat.Num(value),
            "delta", InputLogFormat.Num(value - DragStartValue), "moved", moved ? "1" : "0");
        EndDrag();
    }

    private void CancelDrag(string reason)
    {
        Drag.Cancel();
        if (binder != null && Spec != null) binder.CancelDrag(this);
        InputLog.Event(InputLogFormat.DragCancel, "row", RowName, "reason", reason);
        EndDrag();
    }

    private void EndDrag()
    {
        Drag = null;
        if (Dragging == this) Dragging = null;
        CursorPin.Unlock();
    }
}
