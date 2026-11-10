using UnityEngine;
using ReactorSim.Controls;

// Phase 2 diegetic control: attaches to a control pivot named INT_* (see PivotNameParser for the grammar).
// Buttons act on click. Dials map a vertical drag to a value in the pivot's range, relative to where the
// control currently sits; the binder throttles submission while dragging and always sends on release.
//
// KTD4 rules: clicks and drags only act within OperatorMath.InteractionRange of the eye (hover still works);
// a guarded switch (kit K11, set by the scene builder from inventory.csv) needs two clicks, the first lifts the
// guard; an out-of-service row (the builder puts one on the kit root, no INT_ pivot) shows why and refuses clicks.
[RequireComponent(typeof(Collider))]
public class DiegeticInteractable : MonoBehaviour
{
    public ControlRoom3DBinder binder;
    [Tooltip("Cursor travel (pixels) that moves a dial through its whole range.")]
    public float pixelsPerFullRange = 320f;

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

    public ControlSpec Spec { get; private set; }
    public ParseStatus Status { get; private set; }
    public bool OutOfService { get { return outOfService; } }

    /// <summary>True while any control is being dragged; OperatorController ignores mouse-look and walking then.</summary>
    public static bool DragActive { get; private set; }
    /// <summary>The control under the cursor, or null.</summary>
    public static DiegeticInteractable Hovered { get; private set; }

    private float dragStartY;
    private double dragStartFrac;
    private bool dragging;
    private float guardLiftedUntil;
    private Collider col;

    // Cached (RequireComponent guarantees one); re-fetched only if it was destroyed.
    private Collider Col
    {
        get
        {
            if (col == null) col = GetComponent<Collider>();
            return col;
        }
    }

    private double CurrentDragFraction()
    {
        return DragMapper.FractionAfterDrag(dragStartFrac, Input.mousePosition.y - dragStartY, pixelsPerFullRange);
    }

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
        if (dragging)
        {
            // Disabled mid-drag (Esc menu): commit where the hand let go so the dial never sticks pending.
            if (binder != null)
                binder.DragTo(this, CurrentDragFraction(), true);
            dragging = false;
            DragActive = false;
        }
    }

    private void OnMouseEnter()
    {
        if (Spec == null && !outOfService && !pinnable) return;
        Hovered = this;
    }

    private void OnMouseExit()
    {
        if (Hovered == this && !dragging) Hovered = null;
    }

    private void OnMouseDown()
    {
        if (outOfService) return;
        if (binder == null || Spec == null)
        {
            Debug.LogWarning("[Input] press " + name + (binder == null ? " (NO BINDER)" : "") + (Spec == null ? " (NO SPEC)" : ""));
            return;
        }
        if (!InRange) return; // hover only: walk to the section's operating point
        if (Spec.Style == ControlStyle.Button)
        {
            if (guarded)
            {
                if (Time.unscaledTime >= guardLiftedUntil)
                {
                    guardLiftedUntil = Time.unscaledTime + GuardSeconds; // first click lifts the guard
                    return;
                }
                guardLiftedUntil = 0f;
            }
            binder.PressButton(this);
            return;
        }
        dragging = true;
        DragActive = true;
        dragStartY = Input.mousePosition.y;
        dragStartFrac = binder.StartFraction(this);
    }

    private void OnMouseDrag()
    {
        if (!dragging || binder == null) return;
        binder.DragTo(this, CurrentDragFraction(), false);
    }

    private void OnMouseUp()
    {
        if (!dragging || binder == null) return;
        binder.DragTo(this, CurrentDragFraction(), true);
        dragging = false;
        DragActive = false;
    }
}
