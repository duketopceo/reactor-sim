using System.Collections.Generic;
using UnityEngine;
using TMPro;
using ReactorSim.Instruments;
using ReactorSim.Ui;

/// <summary>
/// Focused view of a CRT screen (KTD4). A screen is too small to read from the seat at the real window size, so a left
/// click on a screen face eases the camera to a fixed pose in front of it (PixelBudget.FocusDistance) with a narrower
/// field of view. Escape or a second click eases back. Escape leaves focus before it can open the pause menu
/// (PauseMenu checks ConsumesEscape), and while focused the room's walking, look and control clicks are suspended
/// through RoomInputGate, so dial presses are ignored. A one-line "Esc: back" prompt shows. The capture mode enters
/// and leaves by screen name (EnterByName, Leave).
/// </summary>
public sealed class ScreenFocus : MonoBehaviour
{
    public const float FocusFov = 35f;
    public const float EaseSeconds = 0.35f;
    public const string Prompt = "Esc: back";
    private const float MaxPickDistance = 8f;

    private enum Mode { None, Entering, Focused, Leaving }

    private static readonly List<CrtScreenRenderer> screens = new List<CrtScreenRenderer>();

    /// <summary>True from the click that enters until the view has eased all the way back out.</summary>
    public static bool Active { get { return instance != null && instance.mode != Mode.None; } }
    /// <summary>Frame in which focus last began leaving; Escape in that frame belongs to the focus view.</summary>
    public static int LeftFrame { get; private set; }
    /// <summary>True when this frame's Escape must not open the pause menu.</summary>
    public static bool ConsumesEscape { get { return Active || LeftFrame == Time.frameCount; } }
    /// <summary>Name of the focused screen, or empty.</summary>
    public static string FocusedName { get { return Active && instance.target != null ? instance.target.screenName : ""; } }

    private static ScreenFocus instance;

    private Camera cam;
    private Mode mode = Mode.None;
    private CrtScreenRenderer target;
    private readonly RoomInputGate gate = new RoomInputGate();
    private Vector3 startPos, endPos;
    private Quaternion startRot, endRot;
    private float startFov;
    private float t;
    private float enteredAt;
    private GameObject promptRoot;

    /// <summary>Called by a screen once it knows its face; makes sure the camera carries the focus controller.</summary>
    public static void Register(CrtScreenRenderer screen, Camera camera)
    {
        if (screen != null && !screens.Contains(screen)) screens.Add(screen);
        if (camera != null && camera.GetComponent<ScreenFocus>() == null) camera.gameObject.AddComponent<ScreenFocus>();
    }

    public static void Unregister(CrtScreenRenderer screen)
    {
        screens.Remove(screen);
    }

    private void Awake()
    {
        cam = GetComponent<Camera>();
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void OnDisable()
    {
        if (mode != Mode.None) Finish();
    }

    /// <summary>Enters the named screen's focused view (capture mode). Instant skips the ease. False if there is no such screen.</summary>
    public static bool EnterByName(string screenName, bool instant)
    {
        if (instance == null) return false;
        foreach (var s in screens)
            if (s != null && s.HasFace && s.screenName == screenName) { instance.Enter(s, instant); return true; }
        return false;
    }

    /// <summary>Leaves the focused view (capture mode). Instant skips the ease.</summary>
    public static void Leave(bool instant)
    {
        if (instance != null) instance.BeginLeave(instant);
    }

    private static bool OverlayOpen()
    {
        if (PauseMenu.IsOpen || StartupChecklistHud.IsExpanded || ProcedureClipboard.AnyOpen()) return true;
        var tablet = FindAnyObjectByType<OperatorTablet>();
        return tablet != null && tablet.IsRaised;
    }

    private void Update()
    {
        if (cam == null) return;
        switch (mode)
        {
            case Mode.None:
                if (Input.GetMouseButtonDown(0) && !OverlayOpen()) TryPick();
                break;
            case Mode.Entering:
            case Mode.Focused:
                // Escape or a second click (not the one that entered) goes back.
                if (Input.GetKeyDown(KeyCode.Escape) || (Input.GetMouseButtonDown(0) && Time.unscaledTime - enteredAt > 0.25f))
                    BeginLeave(false);
                break;
        }
    }

    private void LateUpdate()
    {
        if (mode == Mode.None || target == null) return;
        if (mode == Mode.Entering || mode == Mode.Leaving)
        {
            t = Mathf.Clamp01(t + Time.unscaledDeltaTime / EaseSeconds);
            float k = mode == Mode.Entering ? Smooth(t) : 1f - Smooth(t);
            Apply(k);
            if (t >= 1f)
            {
                if (mode == Mode.Entering) mode = Mode.Focused;
                else Finish();
            }
        }
        else Apply(1f);
    }

    private static float Smooth(float x) { return x * x * (3f - 2f * x); }

    private void Apply(float k)
    {
        cam.transform.position = Vector3.Lerp(startPos, endPos, k);
        cam.transform.rotation = Quaternion.Slerp(startRot, endRot, k);
        cam.fieldOfView = Mathf.Lerp(startFov, FocusFov, k);
    }

    private void TryPick()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        CrtScreenRenderer best = null;
        float bestDist = MaxPickDistance;
        foreach (var s in screens)
        {
            if (s == null || !s.HasFace) continue;
            float d;
            if (s.RayHitsFace(ray, out d) && d < bestDist) { best = s; bestDist = d; }
        }
        if (best == null) return;
        // A control in front of the screen keeps its own click.
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, bestDist - 0.01f) && hit.collider.GetComponentInParent<DiegeticInteractable>() != null) return;
        Enter(best, false);
    }

    private void Enter(CrtScreenRenderer screen, bool instant)
    {
        if (mode != Mode.None) return;
        target = screen;
        startPos = cam.transform.position;
        startRot = cam.transform.rotation;
        startFov = cam.fieldOfView;
        float dist = (float)PixelBudget.FocusDistance(screen.FaceWidth, screen.FaceHeight, FocusFov, cam.aspect);
        endPos = screen.FaceCenter + screen.FaceNormal * Mathf.Max(0.2f, dist);
        endRot = Quaternion.LookRotation(-screen.FaceNormal, screen.FaceUp);
        gate.Suspend();
        enteredAt = Time.unscaledTime;
        t = instant ? 1f : 0f;
        mode = instant ? Mode.Focused : Mode.Entering;
        ShowPrompt(true);
        if (instant) Apply(1f);
    }

    private void BeginLeave(bool instant)
    {
        if (mode == Mode.None || mode == Mode.Leaving) return;
        LeftFrame = Time.frameCount;
        if (instant) { Apply(0f); Finish(); return; }
        mode = Mode.Leaving;
        t = 0f;
        ShowPrompt(false);
    }

    // Back at the operator's pose: hand the camera, the cursor and the controls back.
    private void Finish()
    {
        if (cam != null) cam.fieldOfView = startFov;
        mode = Mode.None;
        target = null;
        ShowPrompt(false);
        gate.Restore();
    }

    private void ShowPrompt(bool on)
    {
        if (promptRoot == null)
        {
            if (!on) return;
            var canvas = RuntimeUi.CreateCanvas("ScreenFocusCanvas", 70, transform);
            canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>().enabled = false;
            promptRoot = canvas.gameObject;
            TMP_Text text;
            RuntimeUi.MakeToast(canvas.transform, "Prompt", new Vector2(0.5f, 0f), new Vector2(0f, 24f), 0f,
                TextRole.Body, TextAnchor.MiddleCenter, ThemeColors.TextBody, out text);
            text.text = Prompt;
        }
        promptRoot.SetActive(on);
    }
}
