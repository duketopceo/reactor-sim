using System.Collections.Generic;
using TMPro;
using UnityEngine;
using ReactorSim.Ui;

// Explicit remote inspection, not walking through water or a claim of gallery line-of-sight.
[RequireComponent(typeof(Camera))]
public sealed class ReactorInspectionView : MonoBehaviour
{
    private static ReactorInspectionView instance;
    public static bool Active { get { return instance != null && instance.active; } }
    public static bool ConsumesEscape { get { return Active || leftFrame == Time.frameCount; } }
    private static int leftFrame = -1;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { instance = null; leftFrame = -1; }

    private readonly RoomInputGate gate = new RoomInputGate();
    private readonly List<Behaviour> suspended = new List<Behaviour>();
    private Camera cameraView;
    private GameObject hall;
    private GameObject prompt;
    private TMP_Text label;
    private Vector3 savedPosition;
    private Quaternion savedRotation;
    private float savedFov;
    private int savedMask;
    private int view;
    private bool active;
    private static readonly string[] Names = { "Fuel array", "Rod drives", "Primary pumps", "Turbine cutaway" };

    private void Awake() { instance = this; cameraView = GetComponent<Camera>(); }
    private void OnDestroy() { Leave(); if (instance == this) instance = null; }
    private void OnDisable() { Leave(); if (prompt != null) prompt.SetActive(false); }

    private bool OverlayOpen()
    {
        if (PauseMenu.IsOpen || StartupChecklistHud.IsExpanded || ProcedureClipboard.AnyOpen() || ScreenFocus.Active) return true;
        var tablet = FindAnyObjectByType<OperatorTablet>();
        return tablet != null && tablet.IsRaised;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F8))
        {
            if (active) Leave(); else if (!OverlayOpen()) Enter();
        }
        if (active)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { Leave(); return; }
            if (Input.GetKeyDown(KeyCode.RightArrow)) SetView((view + 1) % Names.Length);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) SetView((view + Names.Length - 1) % Names.Length);
            cameraView.fieldOfView = Mathf.Clamp(cameraView.fieldOfView - Input.mouseScrollDelta.y * 3f, 25f, 70f);
        }
        // Only show navigation help while actually inspecting; the persistent F8 prompt is unnecessary.
        ShowPrompt(active);
    }

    public bool Enter()
    {
        if (active || OverlayOpen()) return false;
        hall = HallVisibility.FindHall();
        if (hall == null) return false;
        savedPosition = transform.position;
        savedRotation = transform.rotation;
        savedFov = cameraView.fieldOfView;
        savedMask = cameraView.cullingMask;
        gate.Suspend();
        // Keep overlays and shake from taking ownership of the inspection camera.
        foreach (var behaviour in FindObjectsByType<Behaviour>(FindObjectsSortMode.None))
        {
            if (!(behaviour is ScreenFocus || behaviour is CameraShake || behaviour is OperatorTablet ||
                  behaviour is ProcedureClipboard || behaviour is StartupChecklistHud) || !behaviour.enabled) continue;
            suspended.Add(behaviour);
            behaviour.enabled = false;
        }
        active = true;
        hall.SetActive(true);
        cameraView.cullingMask = 1 << hall.layer;
        SetView(0);
        ShowPrompt(true);
        return true;
    }

    public void Leave()
    {
        if (!active) return;
        leftFrame = Time.frameCount;
        active = false;
        if (cameraView != null)
        {
            transform.SetPositionAndRotation(savedPosition, savedRotation);
            cameraView.fieldOfView = savedFov;
            cameraView.cullingMask = savedMask;
        }
        foreach (var behaviour in suspended) if (behaviour != null) behaviour.enabled = true;
        suspended.Clear();
        gate.Restore();
        if (label != null) label.text = "F8: live reactor inspection";
    }

    public void SetView(int index)
    {
        if (!active || index < 0 || index >= Names.Length) return;
        view = index;
        var core = hall.transform.Find("ReactorCore");
        if (core == null) { Leave(); return; }
        float coreBase = core.localPosition.y;
        Vector3 position, target;
        if (view == 0) { position = new Vector3(-0.65f, coreBase + 2.5f, -4.3f); target = new Vector3(0, coreBase + 2.1f, -0.37f); }
        else if (view == 1) { position = new Vector3(-0.8f, coreBase + 8f, -3.6f); target = new Vector3(0, coreBase + 7.8f, 0); }
        else if (view == 2) { position = new Vector3(0, coreBase + 11.3f, -4.2f); target = new Vector3(0, coreBase + 11.3f, 0); }
        else { position = new Vector3(-3.1f, coreBase + 1.0f, -1.7f); target = new Vector3(-3.1f, coreBase + 0.7f, 0); }
        transform.position = hall.transform.TransformPoint(position);
        transform.rotation = Quaternion.LookRotation(hall.transform.TransformPoint(target) - transform.position, hall.transform.up);
        cameraView.fieldOfView = view == 0 ? 58f : 50f;
        if (label != null) label.text = Names[view] + " - live cutaway | Left/Right: views | Wheel: zoom | Esc/F8: return";
    }

    private void ShowPrompt(bool show)
    {
        if (prompt == null)
        {
            if (!show) return;
            var canvas = RuntimeUi.CreateCanvas("ReactorInspectionPrompt", 70, transform);
            canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>().enabled = false;
            prompt = canvas.gameObject;
            RuntimeUi.MakeToast(canvas.transform, "Prompt", new Vector2(0.5f, 0), new Vector2(0, 68), 0,
                TextRole.Body, TextAnchor.MiddleCenter, ThemeColors.TextBody, out label);
            label.text = "F8: live reactor inspection";
            if (active) SetView(view);
        }
        prompt.SetActive(show);
    }
}
