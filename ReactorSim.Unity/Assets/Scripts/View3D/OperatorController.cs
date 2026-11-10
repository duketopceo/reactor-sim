using UnityEngine;
using ReactorSim;

// First-person operator (U7, KTD4); replaces SeatCamera. The camera is the eye of a body-less CharacterController
// capsule that spawns seated at SPAWN_A1. The cursor is free by default so hover and drag act on controls.
//   WASD               walk (not while seated or while a control is being dragged), with a short acceleration ramp
//   Shift              sprint while walking
//   F                  sit on the chair being looked at, within 1.5 m (standing); stand up from the chair (seated)
//   1-6                quick travel: stand at STAND_<section> (reactor, primary, safety, steam, turbine, electrical),
//                      facing the board
//   Q / E (seated)     swivel the chair left / right, within the seated yaw clamp
//   hold right mouse   look around (suppressed while a control is being dragged; yaw limited while seated; the pointer returns
//                      to where it was; the invert-Y setting flips pitch)
//   mouse wheel        zoom; zooming in drifts the view toward the hovered control
//   C or middle mouse  recentre view and zoom
// Camera shake (CameraShake) is composed on top in LateUpdate.
// Facing: anchors are Blender empties whose +Y faces the board; the FBX export maps Blender (x,y,z) to Unity (x,z,y),
// so the anchor's Unity forward is the operator's facing. To stay independent of that axis mapping the facing is
// computed geometrically: away from the arc centre (STAND_Center), i.e. toward the board.
[RequireComponent(typeof(Camera))]
public class OperatorController : MonoBehaviour
{
    public float lookSpeed = 2.2f;
    public float defaultPitch = 8f;
    public float defaultFov = 70f;   // overridden by the FOV setting (UserSettings)
    public float minFov = 20f;
    public float maxFov = 90f;
    public float walkSpeed = OperatorMath.WalkSpeed;

    private Camera cam;
    private CameraShake shake;
    private CharacterController body;
    private Transform bodyT;
    private Transform centre, spawn;
    private readonly Transform[] seats = new Transform[2];
    private readonly Transform[] chairs = new Transform[2];
    private readonly Transform[] stands = new Transform[OperatorMath.BoardSections.Length];
    private int settingsVersion = -1;

    private bool seated;
    private float velX, velZ;   // planar walking velocity (m/s), ramped toward the wanted velocity
    private int lookBeganFrame;
    private float walkTime, walkIntended, walkAchieved;
    private int walkStalls;
    private float floorY;
    private float eye;
    private float baseYaw, yaw, pitch;
    private float fovTarget;
    private float focusUntil;
    private bool looking;
    private int chairInRange = -1;
    private GameObject hintBox;
    private TMPro.TMP_Text hintLabel;

    public bool Seated { get { return seated; } }

    private void Awake()
    {
        cam = GetComponent<Camera>();
        shake = GetComponent<CameraShake>();
        walkSpeed = (float)UserSettings.Tuning.WalkSpeed;
        FindAnchors();
        CreateBody();
        Spawn();
    }

    private void FindAnchors()
    {
        centre = Find("STAND_Center");
        spawn = Find("SPAWN_A1");
        seats[0] = Find("SEAT_A1");
        seats[1] = Find("SEAT_A2");
        chairs[0] = Find("CHAIR_A1");
        chairs[1] = Find("CHAIR_A2");
        for (int i = 0; i < stands.Length; i++) stands[i] = Find(OperatorMath.StandAnchorForKey(i + 1));
    }

    private static Transform Find(string name)
    {
        var go = GameObject.Find(name);
        if (go == null) Debug.LogWarning("[OperatorController] anchor " + name + " not found");
        return go != null ? go.transform : null;
    }

    private void CreateBody()
    {
        var go = new GameObject("OperatorBody");
        go.layer = 2; // Ignore Raycast: never steals the OnMouse* ray, still collides with the room
        bodyT = go.transform;
        body = go.AddComponent<CharacterController>();
        body.radius = 0.3f;
        body.height = 1.75f;
        body.center = new Vector3(0f, 0.875f, 0f);
        body.stepOffset = OperatorMath.StepOffset;
        body.skinWidth = 0.03f;
    }

    private void Spawn()
    {
        var at = spawn != null ? spawn : seats[0];
        seated = true;
        eye = OperatorMath.EyeHeight(true);
        if (at != null)
        {
            floorY = at.position.y;
            StandAt(at);
        }
        else
        {
            // Room not found (e.g. stripped scene): stay where the camera is.
            floorY = transform.position.y - eye;
            Teleport(new Vector3(transform.position.x, floorY, transform.position.z));
            ResetLook(transform.eulerAngles.y);
        }
        defaultFov = UserSettings.Fov;
        fovTarget = defaultFov;
        cam.fieldOfView = defaultFov;
        settingsVersion = UserSettings.Version;
        ApplyTransform();
    }

    // Moves the body to an anchor and looks outward from the room centre, level (no fov change).
    private void StandAt(Transform anchor)
    {
        Teleport(anchor.position);
        ResetLook(OutwardYaw(anchor.position, anchor.eulerAngles.y));
    }

    private void ResetLook(float newBaseYaw)
    {
        baseYaw = newBaseYaw;
        yaw = baseYaw;
        pitch = defaultPitch;
    }

    private float OutwardYaw(Vector3 p, float fallback)
    {
        if (centre == null) return fallback;
        return OperatorMath.OutwardYaw(p.x, p.z, centre.position.x, centre.position.z, fallback);
    }

    private void Teleport(Vector3 floorPos)
    {
        body.enabled = false;
        bodyT.position = new Vector3(floorPos.x, floorY, floorPos.z);
        body.enabled = true;
    }

    private System.Collections.IEnumerator Start()
    {
        // Two frames in: layout, colliders and the first LateUpdate have settled.
        yield return null;
        yield return null;
        SceneProbe.Log("sandbox start");
    }

    private void Update()
    {
        if (settingsVersion != UserSettings.Version)
        {
            // Pause-menu settings apply live: the new FOV becomes the resting view.
            settingsVersion = UserSettings.Version;
            defaultFov = UserSettings.Fov;
            fovTarget = defaultFov;
        }
        CursorPin.Tick();
        UpdateLook();
        UpdateZoom();
        UpdateChairHint();
        if (Input.GetKeyDown(KeyCode.F)) ToggleSeat();
        if (seated) UpdateSwivel();
        UpdateQuickTravel();
        UpdateWalk();
        UpdateHint();

        if (Input.GetKeyDown(KeyCode.C) || Input.GetMouseButtonDown(2))
        {
            yaw = seated ? baseYaw : OutwardYaw(bodyT.position, yaw);
            pitch = defaultPitch;
            fovTarget = defaultFov;
        }
    }

    private float YawRange { get { return seated ? OperatorMath.SeatedYawRange : 180f; } }

    private void UpdateLook()
    {
        // A right-click that cancelled a drag must not start a look; the suppression lasts until the button is released.
        if (!Input.GetMouseButton(1)) DiegeticInteractable.ReleaseLookSuppress();
        bool wantLook = Input.GetMouseButton(1) && !DiegeticInteractable.DragActive
            && !DiegeticInteractable.SuppressLook && !DiegeticInteractable.InputConsumedThisFrame;
        if (wantLook && !looking)
        {
            CursorPin.Lock();
            lookBeganFrame = Time.frameCount;
        }
        if (!wantLook && looking) CursorPin.Unlock();
        looking = wantLook;
        if (!looking) return;
        // Locking warps the cursor to the window centre; that frame's mouse delta is not the hand's.
        if (Time.frameCount <= lookBeganFrame + 1) return;
        float speed = lookSpeed * UserSettings.MouseSensitivity;
        yaw += Input.GetAxis("Mouse X") * speed;
        pitch += OperatorMath.PitchDelta(Input.GetAxis("Mouse Y"), speed, UserSettings.InvertY);
        yaw = OperatorMath.ClampYaw(yaw, baseYaw, YawRange);
        pitch = OperatorMath.ClampPitch(pitch);
    }

    private void UpdateZoom()
    {
        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(wheel) > 0.0001f)
        {
            fovTarget = Mathf.Clamp(fovTarget - wheel * 60f, minFov, maxFov);
            if (wheel > 0f) focusUntil = Time.unscaledTime + 0.6f;
        }

        // Zooming in drifts the view toward the hovered control so a panel can be read up close.
        var h = DiegeticInteractable.Hovered;
        if (h == null || Time.unscaledTime >= focusUntil || looking) return;
        Vector3 d = h.FocusPoint - EyePosition();
        if (d.sqrMagnitude < 1e-4f) return;
        float ty = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        float tp = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
        float step = 120f * Time.unscaledDeltaTime;
        yaw = OperatorMath.ClampYaw(Mathf.MoveTowardsAngle(yaw, ty, step), baseYaw, YawRange);
        pitch = OperatorMath.ClampPitch(Mathf.MoveTowards(pitch, tp, step));
    }

    // ---- sit / stand -----------------------------------------------------------------------------------------

    private void UpdateChairHint()
    {
        chairInRange = -1;
        if (seated) return;
        float best = float.MaxValue;
        Vector3 eyePos = EyePosition();
        for (int i = 0; i < chairs.Length; i++)
        {
            if (chairs[i] == null || seats[i] == null) continue;
            Vector3 toChair = chairs[i].position + Vector3.up * 0.45f - eyePos;
            Vector3 flat = chairs[i].position - bodyT.position;
            flat.y = 0f;
            float hd = flat.magnitude;
            float angle = Vector3.Angle(cam.transform.forward, toChair);
            if (OperatorMath.SitPromptVisible(hd, angle) && hd < best)
            {
                best = hd;
                chairInRange = i;
            }
        }
    }

    private void ToggleSeat()
    {
        if (DiegeticInteractable.DragActive) return;
        if (seated)
        {
            seated = false; // the body already stands over the seat anchor: eye rises to 1.65 m
            return;
        }
        if (chairInRange < 0) return;
        var seat = seats[chairInRange];
        seated = true;
        StandAt(seat);
    }

    // ---- quick travel and chair swivel (U17) -----------------------------------------------------------------------

    private void UpdateQuickTravel()
    {
        if (DiegeticInteractable.DragActive) return;
        for (int n = 1; n <= stands.Length; n++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + (n - 1)) || Input.GetKeyDown(KeyCode.Keypad1 + (n - 1)))
            {
                TravelTo(n);
                return;
            }
    }

    /// <summary>Stands at STAND_&lt;section&gt; for key n (1-6), facing the board (outward from STAND_Center).</summary>
    public void TravelTo(int n)
    {
        var anchor = n >= 1 && n <= stands.Length ? stands[n - 1] : null;
        if (anchor == null) return;
        seated = false;
        StandAt(anchor);
        fovTarget = defaultFov;
    }

    private void UpdateSwivel()
    {
        int dir = (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0);
        if (dir == 0 || DiegeticInteractable.DragActive) return;
        yaw = OperatorMath.Swivel(yaw, baseYaw, dir, Time.unscaledDeltaTime, YawRange);
    }

    // ---- walking ---------------------------------------------------------------------------------------------

    private void UpdateWalk()
    {
        var tuning = UserSettings.Tuning;
        float top = OperatorMath.EffectiveWalkSpeed(seated, DiegeticInteractable.DragActive, walkSpeed);
        if (top <= 0f)
        {
            velX = velZ = 0f;
            return;
        }
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        bool sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float wanted = OperatorMath.TargetSpeed(top, (float)tuning.SprintMultiplier, sprint);
        Quaternion flat = Quaternion.Euler(0f, yaw, 0f);
        Vector3 move = flat * Vector3.forward * v + flat * Vector3.right * h;
        if (move.sqrMagnitude > 1f) move.Normalize();
        OperatorMath.RampVelocity(velX, velZ, move.x * wanted, move.z * wanted, Time.deltaTime, wanted,
            (float)tuning.AccelSeconds, (float)tuning.DecelSeconds, out velX, out velZ);
        if (velX * velX + velZ * velZ < 1e-6f) { velX = velZ = 0f; return; }

        Vector3 before = bodyT.position;
        body.SimpleMove(new Vector3(velX, 0f, velZ));
        LogWalk(before);
    }

    // Walking samples every half second: where the body is, how far it was asked to go and how far it got (a snag shows as
    // achieved far below intended).
    private void LogWalk(Vector3 before)
    {
        Vector3 d = bodyT.position - before;
        d.y = 0f;
        float intended = Mathf.Sqrt(velX * velX + velZ * velZ) * Time.deltaTime;
        float achieved = d.magnitude;
        walkTime += Time.deltaTime;
        walkIntended += intended;
        walkAchieved += achieved;
        if (OperatorMath.IsStall(intended, achieved)) walkStalls++;
        if (walkTime < 0.5f) return;
        InputLog.Event(InputLogFormat.Walk,
            "x", InputLogFormat.Num(bodyT.position.x), "z", InputLogFormat.Num(bodyT.position.z),
            "speed", InputLogFormat.Num(walkAchieved / walkTime), "intended_m", InputLogFormat.Num(walkIntended),
            "achieved_m", InputLogFormat.Num(walkAchieved), "stalls", walkStalls.ToString());
        walkTime = walkIntended = walkAchieved = 0f;
        walkStalls = 0;
    }

    // ---- capture runs (CaptureMode) ----------------------------------------------------------------------------

    /// <summary>Sits on the first chair (the spawn pose).</summary>
    public void CaptureSeat()
    {
        if (seats[0] == null) return;
        seated = true;
        StandAt(seats[0]);
        fovTarget = defaultFov;
    }

    /// <summary>Stands at the quick-travel stop nearest a world point, then looks at it with the given field of view.</summary>
    public void CaptureLookAt(Vector3 point, float fov)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < stands.Length; i++)
        {
            if (stands[i] == null) continue;
            Vector3 f = stands[i].position - point;
            f.y = 0f;
            if (f.sqrMagnitude < bestD) { bestD = f.sqrMagnitude; best = i; }
        }
        if (best >= 0) TravelTo(best + 1);
        Vector3 d = point - EyePosition();
        yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        pitch = OperatorMath.ClampPitch(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
        fovTarget = Mathf.Clamp(fov, minFov, maxFov);
    }

    /// <summary>Current vertical field of view (degrees) and eye position, for the capture measurements.</summary>
    public float CurrentFov { get { return cam != null ? cam.fieldOfView : defaultFov; } }

    // ---- camera ----------------------------------------------------------------------------------------------

    private Vector3 EyePosition()
    {
        return new Vector3(bodyT.position.x, floorY + eye, bodyT.position.z);
    }

    private void LateUpdate()
    {
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fovTarget, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
        eye = Mathf.Lerp(eye, OperatorMath.EyeHeight(seated), 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
        ApplyTransform();
    }

    private void ApplyTransform()
    {
        Vector3 offset = shake != null ? shake.Offset : Vector3.zero;
        float roll = shake != null ? shake.Roll : 0f;
        transform.position = EyePosition() + offset;
        transform.rotation = Quaternion.Euler(pitch, yaw, roll);
    }

    // Bottom-centre hint (uGUI + TextMeshPro): the seat prompt and the controls reminder.
    private void UpdateHint()
    {
        string seatPrompt = OperatorMath.SeatPrompt(seated, chairInRange >= 0);
        string hint = seated ? "Q/E  swivel      " + seatPrompt + "      1-6  go to a section" : seatPrompt;
        if (hintBox == null)
        {
            if (hint == null) return;
            var canvas = RuntimeUi.CreateCanvas("OperatorHintCanvas", 15, transform);
            canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>().enabled = false;
            hintBox = RuntimeUi.MakeToast(canvas.transform, "Hint", new Vector2(0.5f, 0f), new Vector2(0f, 70f), 0f,
                ReactorSim.Ui.TextRole.Caption, TextAnchor.MiddleCenter, ThemeColors.TextBody, out hintLabel).gameObject;
        }
        if (hintBox.activeSelf != (hint != null)) hintBox.SetActive(hint != null);
        if (hint != null && hintLabel.text != hint) hintLabel.text = hint;
    }

    private void OnDisable()
    {
        if (looking) CursorPin.Unlock();
        looking = false;
    }
}
