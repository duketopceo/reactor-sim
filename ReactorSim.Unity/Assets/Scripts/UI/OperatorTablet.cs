using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using TextRole = ReactorSim.Ui.TextRole;
using ReactorSim;
using ReactorSim.Instruments;
using ReactorSim.Tablet;

/// <summary>
/// U14 operator tablet (R14). Tab raises and lowers a handheld screen-space tablet with Pins, Alarms, Trends and Clock
/// and Phone pages (Phone: dispatcher requests and plant-manager messages, U15). While it is raised the room's mouse look,
/// walking and control clicks are suspended exactly as PauseMenu does and the cursor is free; the sim keeps running.
///   right-click an indicator in the room   pin / unpin its readout (the pinnable DiegeticInteractable the builder puts on
///                                          each gauge row's CTL_ root is what the ray finds)
///   Alarms page, click a row               "find it": lowers the tablet, blinks that annunciator tile cyan (ControlRoom3DBinder
///                                          .FlashLamp) and lays a floor arrow at the operator's feet pointing at it
///   Clock page                             submits the room's own pace / pause / skip intents through PlantBridge.SubmitIntent
/// Page models (pins, alarm order, trends, clock intents) are pure and tested: see TabletPages.cs.
/// </summary>
public class OperatorTablet : MonoBehaviour
{
    public const float FindSeconds = 6f;

    private const int AlarmRowCount = 10;
    private const int TrendBars = 60;
    private static readonly string[] TabNames = { "Pins", "Alarms", "Trends", "Clock", "Phone" };

    private static Color Ink { get { return ThemeColors.TextBody; } }
    private static Color Dim { get { return ThemeColors.TextDim; } }
    private static Color RowBg { get { return ThemeColors.SurfaceRow; } }
    private static Color TabOff { get { return ThemeColors.ButtonOff; } }
    private static Color TabOn { get { return ThemeColors.ButtonOn; } }
    private static Color Crit { get { return ThemeColors.StatusCritical; } }
    private static Color Warn { get { return ThemeColors.StatusCaution; } }

    public PlantBridge bridge;
    public ControlRoom3DBinder binder;

    private readonly PinBoard pins = new PinBoard();
    private readonly AlarmBoard alarms = new AlarmBoard();
    private readonly TabletTrends trends = new TabletTrends(TrendBars);
    private readonly RoomInputGate inputGate = new RoomInputGate();

    private bool raised;
    private bool wired;
    private float slide;           // 0 hidden .. 1 fully raised
    private int page;
    private float nextRefresh;

    private GameObject root;
    private RectTransform panel;
    private TMP_Text headerClock;
    private readonly Image[] tabImages = new Image[TabNames.Length];
    private readonly GameObject[] pages = new GameObject[5];

    // phone page (U15): Dispatcher and Messages sub-views
    private const int PhoneTab = 4;
    private const int MessageRowCount = 6;
    private const int HistoryRowCount = 5;
    private readonly PhonePage phone = new PhonePage();
    private readonly MessagesPage messages = new MessagesPage();
    private int phoneSub;                         // 0 dispatcher, 1 messages
    private readonly Image[] phoneSubImg = new Image[2];
    private readonly GameObject[] phoneView = new GameObject[2];
    private TMP_Text phoneCardTitle;
    private TMP_Text phoneCardStatus;
    private RectTransform phoneBarFill;
    private RectTransform phoneBarMark;
    private Image phoneBarFillImg;
    private TMP_Text phoneBarText;
    private TMP_Text phoneStats;
    private readonly TMP_Text[] phoneHist = new TMP_Text[HistoryRowCount];
    private readonly GameObject[] msgRowGo = new GameObject[MessageRowCount];
    private readonly TMP_Text[] msgText = new TMP_Text[MessageRowCount];
    private readonly Image[] msgImg = new Image[MessageRowCount];
    private readonly FiredMessage[] msgShown = new FiredMessage[MessageRowCount];
    private TMP_Text msgEmpty;
    private TMP_Text phoneTabText;
    private int shownUnread = -1;
    private string phoneToast = "";
    private float phoneToastUntil;
    private TMP_Text phoneToastText;
    private GameObject phoneToastBox;
    private static Color Good { get { return ThemeColors.StatusOk; } }

    // pins page
    private readonly GameObject[] pinRowGo = new GameObject[PinBoard.Cap];
    private readonly TMP_Text[] pinLabel = new TMP_Text[PinBoard.Cap];
    private readonly TMP_Text[] pinValue = new TMP_Text[PinBoard.Cap];
    private TMP_Text pinEmpty;
    // alarms page
    private readonly GameObject[] alarmRowGo = new GameObject[AlarmRowCount];
    private readonly TMP_Text[] alarmText = new TMP_Text[AlarmRowCount];
    private readonly AlarmEntry[] alarmShown = new AlarmEntry[AlarmRowCount];
    private TMP_Text alarmEmpty;
    private TMP_Text alarmMore;
    // trends page
    private readonly TMP_Text[] trendTitle = new TMP_Text[4];
    private readonly Image[][] trendBar = new Image[4][];
    // clock page
    private TMP_Text clockState;
    private TMP_Text clockPauseLabel;

    // find-it
    private GameObject arrow;
    private Vector3 arrowTarget;
    private float arrowUntil;
    private float floorY;
    private bool haveFloor;

    private string toast = "";
    private float toastUntil;
    private TMP_Text toastText, hintText;
    private GameObject toastBox, hintBox, toastRoot;

    public bool IsRaised { get { return raised; } }

    // ---- lifecycle -------------------------------------------------------------------------------------------

    private void EnsureRefs()
    {
        if (bridge == null) bridge = GetComponent<PlantBridge>();
        if (bridge == null) bridge = FindAnyObjectByType<PlantBridge>();
        if (binder == null) binder = FindAnyObjectByType<ControlRoom3DBinder>();
        if (!wired && binder != null)
        {
            wired = true;
            foreach (string n in binder.LampTileNames()) alarms.SetRowId(n, binder.LampRowId(n));
        }
    }

    private CanvasScaler tabletScaler;
    private int appliedTextVersion = -1;

    // Text scale (pause menu): capped at 1.25 so the device still fits a 1080-high window.
    private void ApplyTextScale()
    {
        appliedTextVersion = UserSettings.Version;
        if (tabletScaler == null) return;
        float k = Mathf.Min(UserSettings.TextScale, 1.25f);
        tabletScaler.referenceResolution = new Vector2(1920f, 1080f) / k;
    }

    private void Update()
    {
        EnsureRefs();
        if (appliedTextVersion != UserSettings.Version) ApplyTextScale();
        if (bridge == null || bridge.Sim == null) return;
        var s = bridge.GetSnapshot();
        if (s != null)
        {
            trends.Push(s);
            alarms.Update(s, binder != null ? binder.CurrentLampContext() : LampContext.Default);
        }

        PollPhone();

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (raised) Lower(); else Raise();
        }
        if (!raised && !PauseMenu.IsOpen && Input.GetMouseButtonDown(1) && !DiegeticInteractable.InputConsumedThisFrame) TryPinUnderCursor();

        AnimateSlide();
        UpdateArrow();
        if (raised && Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.2f;
            RefreshPage(s);
        }
    }

    public void Raise()
    {
        if (raised || PauseMenu.IsOpen) return;
        if (root == null) Build();
        // One overlay at a time: close a procedure clipboard first, as PauseMenu.Open lowers the tablet.
        ProcedureClipboard.CloseAllOpen();
        StartupChecklistHud.CollapseAll();
        raised = true;
        inputGate.Suspend();
        nextRefresh = 0f;
    }

    public void Lower()
    {
        if (!raised) return;
        raised = false;
        inputGate.Restore();
    }

    private void OnDisable()
    {
        Lower();
    }

    private void AnimateSlide()
    {
        if (root == null) return;
        float target = raised ? 1f : 0f;
        slide = Mathf.MoveTowards(slide, target, Time.unscaledDeltaTime * 5f);
        float e = slide * slide * (3f - 2f * slide);
        root.SetActive(slide > 0.001f);
        if (panel != null) panel.anchoredPosition = new Vector2(0f, Mathf.Lerp(-panel.sizeDelta.y - 40f, 24f, e));
    }

    // ---- pinning ---------------------------------------------------------------------------------------------

    private void TryPinUnderCursor()
    {
        var cam = Camera.main;
        if (cam == null) return;
        RaycastHit hit;
        if (!UnityEngine.Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out hit, 100f)) return;
        var pin = hit.collider.GetComponentInParent<DiegeticInteractable>();
        if (pin == null || !pin.pinnable) return;
        var row = new PinRow
        {
            RowId = pin.rowId,
            Label = string.IsNullOrEmpty(pin.rowLabel) ? pin.pinSelector : pin.rowLabel,
            Selector = pin.pinSelector,
            Index = pin.pinIndex
        };
        bool now = pins.Toggle(row);
        string msg = now ? "Pinned: " + row.Label + "  (Tab to read it)" : "Unpinned: " + row.Label;
        if (now && pins.LastEvicted.Length > 0) msg += "  - oldest pin dropped";
        ShowToast(msg);
    }

    // ---- find it ---------------------------------------------------------------------------------------------

    private void Locate(AlarmEntry a)
    {
        Vector3 p;
        if (a == null || binder == null || !binder.TryGetLampPoint(a.Name, out p))
        {
            ShowToast((a != null ? a.Label : "Alarm") + ": no tile in this room");
            return;
        }
        binder.FlashLamp(a.Name, FindSeconds);
        arrowTarget = p;
        arrowUntil = Time.unscaledTime + FindSeconds;
        ShowToast("Find it: " + a.Label + " - follow the arrow");
        Lower(); // get the tablet out of the way so the tile and the arrow are visible
    }

    private void UpdateArrow()
    {
        bool on = Time.unscaledTime < arrowUntil;
        if (!on)
        {
            if (arrow != null && arrow.activeSelf) arrow.SetActive(false);
            return;
        }
        var cam = Camera.main;
        if (cam == null) return;
        if (arrow == null) arrow = BuildArrow();
        if (!haveFloor) FindFloor();
        Vector3 eye = cam.transform.position;
        Vector3 flat = arrowTarget - eye;
        flat.y = 0f;
        if (flat.sqrMagnitude < 1e-4f) { arrow.SetActive(false); return; }
        flat.Normalize();
        float baseY = haveFloor ? floorY : eye.y - 1.6f;
        float pulse = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 8f);
        arrow.SetActive(true);
        arrow.transform.position = new Vector3(eye.x, baseY + 0.03f, eye.z) + flat * 0.9f;
        arrow.transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
        arrow.transform.localScale = new Vector3(pulse, 1f, pulse);
    }

    private void FindFloor()
    {
        var spawn = GameObject.Find("SPAWN_A1");
        if (spawn == null) spawn = GameObject.Find("STAND_Center");
        if (spawn == null) return;
        floorY = spawn.transform.position.y;
        haveFloor = true;
    }

    // Shaft plus a diamond head, flat on the floor, pointing along the root's +Z. No colliders, Ignore Raycast layer.
    private static GameObject BuildArrow()
    {
        var rootGo = new GameObject("FindItArrow");
        var color = new Color(0.4f, 0.95f, 1f, 1f);
        MakeArrowPart(rootGo.transform, new Vector3(0f, 0f, 0.28f), Quaternion.identity, new Vector3(0.1f, 0.012f, 0.56f), color);
        MakeArrowPart(rootGo.transform, new Vector3(0f, 0f, 0.62f), Quaternion.Euler(0f, 45f, 0f), new Vector3(0.26f, 0.012f, 0.26f), color);
        SetLayerRecursive(rootGo, 2);
        return rootGo;
    }

    private static void MakeArrowPart(Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Color color)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var col = part.GetComponent<Collider>();
        if (col != null) Destroy(col);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = pos;
        part.transform.localRotation = rot;
        part.transform.localScale = scale;
        var r = part.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.material.color = color;
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
    }

    // ---- UI build --------------------------------------------------------------------------------------------

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(string name, Transform parent, Color c)
    {
        var rt = NewRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    private static void Place(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = offMin;
        rt.offsetMax = offMax;
    }

    private static TMP_Text NewText(Transform parent, string name, TextRole role, TextAnchor anchor, Color color)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = UiFonts.For(role);
        t.fontSize = ReactorSim.Ui.Theme.RampSize(role);
        t.alignment = TabletAlign(anchor);
        t.color = color;
        t.richText = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Truncate;
        t.raycastTarget = false;
        rt.gameObject.AddComponent<ThemedText>().role = role;
        Place(rt, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f));
        return t;
    }

    private static TextAlignmentOptions TabletAlign(TextAnchor a)
    {
        switch (a)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
            case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
            default: return TextAlignmentOptions.Left;
        }
    }

    private static RectTransform NewPage(string name, Transform parent)
    {
        var rt = NewRect(name, parent);
        Place(rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 6f;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return rt;
    }

    private static RectTransform NewRow(Transform parent, string name, float height, Color bg)
    {
        var img = NewImage(name, parent, bg);
        img.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return (RectTransform)img.transform;
    }

    // A button whose Image color the tablet sets per state (selected tab, unread row): hover only darkens it.
    private static Image StateButtonImage(Button b)
    {
        b.colors = ThemeColors.DarkenOnInteract(b.colors);
        var img = b.GetComponent<Image>();
        img.color = ThemeColors.ButtonOff;
        return img;
    }

    private static void SmallButtonText(Button b, TextRole role, TextAnchor anchor, Color color)
    {
        var t = b.GetComponentInChildren<TMP_Text>();
        if (t == null) return;
        var tt = t.GetComponent<ThemedText>();
        if (tt != null) tt.role = role;
        t.font = UiFonts.For(role);
        t.fontSize = ReactorSim.Ui.Theme.RampSize(role);
        t.alignment = TabletAlign(anchor);
        t.color = color;
        if (anchor == TextAnchor.MiddleLeft)
        {
            var rt = (RectTransform)t.transform;
            rt.offsetMin = new Vector2(14f, 0f);
        }
    }

    private void Build()
    {
        RuntimeUi.EnsureEventSystem();
        var canvas = RuntimeUi.CreateCanvas("TabletCanvas", 90, transform); // below the pause menu (100)
        root = canvas.gameObject;
        tabletScaler = canvas.GetComponent<CanvasScaler>();
        ApplyTextScale();

        panel = NewRect("Tablet", canvas.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0f);
        panel.sizeDelta = new Vector2(740f, 820f);
        panel.anchoredPosition = new Vector2(0f, -900f);

        var bezel = panel.gameObject.AddComponent<Image>();
        bezel.color = ThemeColors.DeviceBezel;
        bezel.raycastTarget = true; // the device body swallows clicks, they never reach the room
        var screen = NewImage("Screen", panel, ThemeColors.SurfaceScreen);
        Place((RectTransform)screen.transform, Vector2.zero, Vector2.one, new Vector2(22f, 22f), new Vector2(-22f, -22f));

        var title = NewText(screen.transform, "Title", TextRole.Heading, TextAnchor.MiddleLeft, Dim);
        title.text = "OPERATOR TABLET";
        Place((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(12f, -48f), new Vector2(0f, -6f));
        headerClock = NewText(screen.transform, "Clock", TextRole.Heading, TextAnchor.MiddleRight, Ink);
        Place((RectTransform)headerClock.transform, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(0f, -48f), new Vector2(-12f, -6f));

        var tabBar = NewRect("Tabs", screen.transform);
        Place(tabBar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -108f), new Vector2(-10f, -54f));
        var h = tabBar.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = true;
        for (int i = 0; i < TabNames.Length; i++)
        {
            int idx = i;
            var b = RuntimeUi.MakeButton(tabBar, TabNames[i], () => SelectPage(idx), 54f);
            tabImages[i] = StateButtonImage(b);
            SmallButtonText(b, TextRole.Body, TextAnchor.MiddleCenter, ThemeColors.TextBody);
            if (i == PhoneTab) phoneTabText = b.GetComponentInChildren<TMP_Text>();
        }

        var body = NewRect("Pages", screen.transform);
        Place(body, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -118f));
        BuildPinsPage(body);
        BuildAlarmsPage(body);
        BuildTrendsPage(body);
        BuildClockPage(body);
        BuildPhonePage(body);
        SelectPage(0);
        // The device follows the text-scale setting through its canvas scaler (ApplyTextScale), so its labels sit on the
        // unscaled ramp; letting ThemedText multiply by the setting as well would scale the text twice.
        foreach (var tt in root.GetComponentsInChildren<ThemedText>(true))
        {
            tt.followTextScale = false;
            tt.Reapply();
        }
        root.SetActive(false);
    }

    private void SelectPage(int i)
    {
        if (i < 0 || i >= pages.Length) return;
        page = i;
        for (int p = 0; p < pages.Length; p++) pages[p].SetActive(p == i);
        for (int t = 0; t < tabImages.Length; t++) tabImages[t].color = t == i ? TabOn : TabOff;
        nextRefresh = 0f;
    }

    private void BuildPinsPage(Transform body)
    {
        var pg = NewPage("PinsPage", body);
        pages[0] = pg.gameObject;
        RuntimeUi.MakeLabel(pg, "Right-click a gauge in the room to pin it here (max " + PinBoard.Cap + ", oldest drops off).", TextRole.Body, TextAnchor.MiddleLeft, Dim, 48f);
        for (int i = 0; i < PinBoard.Cap; i++)
        {
            var row = NewRow(pg, "Pin" + i, 56f, RowBg);
            pinRowGo[i] = row.gameObject;
            pinLabel[i] = NewText(row, "Label", TextRole.Body, TextAnchor.MiddleLeft, Ink);
            Place((RectTransform)pinLabel[i].transform, Vector2.zero, new Vector2(0.58f, 1f), new Vector2(10f, 0f), Vector2.zero);
            pinValue[i] = NewText(row, "Value", TextRole.Heading, TextAnchor.MiddleRight, Good);
            Place((RectTransform)pinValue[i].transform, new Vector2(0.58f, 0f), new Vector2(0.88f, 1f), Vector2.zero, Vector2.zero);
            int slot = i;
            var x = RuntimeUi.MakeButton(row, "x", () => Unpin(slot), 40f);
            Place((RectTransform)x.transform, new Vector2(0.9f, 0.1f), new Vector2(0.99f, 0.9f), Vector2.zero, Vector2.zero);
            SmallButtonText(x, TextRole.Body, TextAnchor.MiddleCenter, ThemeColors.TextBody);
        }
        pinEmpty = RuntimeUi.MakeLabel(pg, "Nothing pinned. The reactor is not going to read itself.", TextRole.Heading, TextAnchor.MiddleCenter, Dim, 80f);
    }

    private void Unpin(int slot)
    {
        if (slot < 0 || slot >= pins.Count) return;
        pins.Unpin(pins.Rows[slot].RowId);
        nextRefresh = 0f;
    }

    private void BuildAlarmsPage(Transform body)
    {
        var pg = NewPage("AlarmsPage", body);
        pages[1] = pg.gameObject;
        RuntimeUi.MakeLabel(pg, "Click an alarm to find its tile: it blinks and an arrow points the way.", TextRole.Body, TextAnchor.MiddleLeft, Dim, 36f);
        for (int i = 0; i < AlarmRowCount; i++)
        {
            int slot = i;
            var b = RuntimeUi.MakeButton(pg, "-", () => LocateSlot(slot), 44f);
            alarmRowGo[i] = b.gameObject;
            SmallButtonText(b, TextRole.Body, TextAnchor.MiddleLeft, Ink);
            alarmText[i] = b.GetComponentInChildren<TMP_Text>();
        }
        alarmMore = RuntimeUi.MakeLabel(pg, "", TextRole.Body, TextAnchor.MiddleCenter, Dim, 28f);
        alarmEmpty = RuntimeUi.MakeLabel(pg, "All quiet. Suspiciously quiet.", TextRole.Heading, TextAnchor.MiddleCenter, Dim, 90f);
    }

    private void LocateSlot(int slot)
    {
        if (slot >= 0 && slot < AlarmRowCount) Locate(alarmShown[slot]);
    }

    private void BuildTrendsPage(Transform body)
    {
        var pg = NewPage("TrendsPage", body);
        pages[2] = pg.gameObject;
        for (int s = 0; s < 4; s++)
        {
            var block = NewRow(pg, "Trend" + s, 146f, RowBg);
            trendTitle[s] = NewText(block, "Title", TextRole.Body, TextAnchor.MiddleLeft, Ink);
            Place((RectTransform)trendTitle[s].transform, new Vector2(0f, 0.8f), Vector2.one, Vector2.zero, Vector2.zero);
            var area = NewRect("Chart", block);
            Place(area, Vector2.zero, new Vector2(1f, 0.8f), new Vector2(10f, 8f), new Vector2(-10f, -2f));
            trendBar[s] = new Image[TrendBars];
            for (int i = 0; i < TrendBars; i++)
            {
                var bar = NewImage("Bar" + i, area, ThemeColors.StatusNormal);
                float x0 = (float)i / TrendBars;
                float x1 = (float)(i + 1) / TrendBars - 0.003f;
                var rt = (RectTransform)bar.transform;
                rt.anchorMin = new Vector2(x0, 0f);
                rt.anchorMax = new Vector2(x1, 0.05f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                bar.enabled = false;
                trendBar[s][i] = bar;
            }
        }
    }

    private void BuildClockPage(Transform body)
    {
        var pg = NewPage("ClockPage", body);
        pages[3] = pg.gameObject;
        clockState = RuntimeUi.MakeLabel(pg, "", TextRole.Title, TextAnchor.MiddleCenter, Ink, 90f);
        RuntimeUi.MakeLabel(pg, "The same pace, pause and skip intents as the clock rack in the room.", TextRole.Body, TextAnchor.MiddleCenter, Dim, 40f);
        foreach (ClockAction a in System.Enum.GetValues(typeof(ClockAction)))
        {
            ClockAction act = a;
            var b = RuntimeUi.MakeButton(pg, ClockActions.Label(a), () => DoClock(act), 84f);
            if (a == ClockAction.PauseResume) clockPauseLabel = b.GetComponentInChildren<TMP_Text>();
        }
    }

    private void BuildPhonePage(Transform body)
    {
        var pg = NewPage("PhonePage", body);
        pages[PhoneTab] = pg.gameObject;

        var sub = NewRect("SubTabs", pg);
        sub.gameObject.AddComponent<LayoutElement>().preferredHeight = 50f;
        var h = sub.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = true;
        string[] subNames = { "Dispatcher", "Messages" };
        for (int i = 0; i < 2; i++)
        {
            int idx = i;
            var b = RuntimeUi.MakeButton(sub, subNames[i], () => SelectPhoneSub(idx), 50f);
            phoneSubImg[i] = StateButtonImage(b);
            SmallButtonText(b, TextRole.Body, TextAnchor.MiddleCenter, ThemeColors.TextBody);
        }

        // dispatcher view
        var dv = NewPage("DispatcherView", pg);
        dv.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        phoneView[0] = dv.gameObject;
        var card = NewRow(dv, "Card", 168f, RowBg);
        phoneCardTitle = NewText(card, "Caller", TextRole.Body, TextAnchor.UpperLeft, Ink);
        Place((RectTransform)phoneCardTitle.transform, new Vector2(0f, 0.38f), Vector2.one, new Vector2(0f, 0f), new Vector2(0f, -8f));
        phoneCardStatus = NewText(card, "Status", TextRole.Numeric, TextAnchor.MiddleLeft, Good);
        Place((RectTransform)phoneCardStatus.transform, Vector2.zero, new Vector2(1f, 0.38f), Vector2.zero, Vector2.zero);

        var bar = NewRow(dv, "Progress", 46f, TabOff);
        var fill = NewImage("Fill", bar, ThemeColors.ButtonOn);
        phoneBarFillImg = fill;
        phoneBarFill = (RectTransform)fill.transform;
        Place(phoneBarFill, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
        var mark = NewImage("WindowMark", bar, ThemeColors.TextHeading);
        phoneBarMark = (RectTransform)mark.transform;
        Place(phoneBarMark, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f));
        phoneBarText = NewText(bar, "BarText", TextRole.Body, TextAnchor.MiddleCenter, ThemeColors.TextBody);
        Place((RectTransform)phoneBarText.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        phoneStats = RuntimeUi.MakeLabel(dv, "", TextRole.Body, TextAnchor.MiddleLeft, Dim, 44f);
        RuntimeUi.MakeLabel(dv, "Recent calls", TextRole.Body, TextAnchor.MiddleLeft, Dim, 30f);
        for (int i = 0; i < HistoryRowCount; i++)
            phoneHist[i] = RuntimeUi.MakeLabel(dv, "", TextRole.Body, TextAnchor.MiddleLeft, Ink, 34f);

        // messages view
        var mv = NewPage("MessagesView", pg);
        mv.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        phoneView[1] = mv.gameObject;
        RuntimeUi.MakeButton(mv, "Mark all read", () => { messages.MarkAllRead(); nextRefresh = 0f; }, 44f);
        for (int i = 0; i < MessageRowCount; i++)
        {
            int slot = i;
            var b = RuntimeUi.MakeButton(mv, "-", () => ReadMessage(slot), 70f);
            msgRowGo[i] = b.gameObject;
            msgImg[i] = StateButtonImage(b);
            SmallButtonText(b, TextRole.Body, TextAnchor.MiddleLeft, Ink);
            msgText[i] = b.GetComponentInChildren<TMP_Text>();
            msgText[i].textWrappingMode = TextWrappingModes.Normal;
            msgText[i].overflowMode = TextOverflowModes.Truncate;
        }
        msgEmpty = RuntimeUi.MakeLabel(mv, "No messages. Enjoy it while it lasts.", TextRole.Heading, TextAnchor.MiddleCenter, Dim, 80f);
        SelectPhoneSub(0);
    }

    private void SelectPhoneSub(int i)
    {
        phoneSub = i;
        for (int v = 0; v < 2; v++)
        {
            phoneView[v].SetActive(v == i);
            phoneSubImg[v].color = v == i ? TabOn : TabOff;
        }
        nextRefresh = 0f;
    }

    private void ReadMessage(int slot)
    {
        if (slot < 0 || slot >= MessageRowCount || msgShown[slot] == null) return;
        messages.MarkRead(msgShown[slot].Seq);
        nextRefresh = 0f;
    }

    // Runs every frame, tablet raised or not: updates the unread badge and raises a toast on a new call or message.
    private void PollPhone()
    {
        var plant = bridge.Sim.Plant;
        var req = phone.Refresh(plant.Dispatcher);
        var msg = messages.Refresh(plant.Messages);
        if (msg != null) ShowPhoneToast(msg.Sender + ": " + msg.Text);
        else if (req != null) ShowPhoneToast("Grid control: " + ReactorSim.Ui.ReadoutFormat.Value(req.TargetMw, 0, "MW")
            + " wanted, window opens in " + PhonePage.FormatCountdown(phone.SecondsToStart));
        if (phoneTabText != null && shownUnread != messages.Unread)
        {
            shownUnread = messages.Unread;
            phoneTabText.text = shownUnread > 0 ? "Phone (" + shownUnread + ")" : "Phone";
            phoneTabText.color = ThemeColors.TextBody;   // the count in the label carries "unread"; caution amber fails on a lit tab
        }
    }

    private void ShowPhoneToast(string text)
    {
        if (text.Length > 140) text = text.Substring(0, 137) + "...";
        phoneToast = text;
        phoneToastUntil = Time.unscaledTime + 6f;
    }

    private void RefreshPhone()
    {
        if (phoneSub == 0) RefreshDispatcherView(); else RefreshMessagesView();
    }

    private void RefreshDispatcherView()
    {
        var d = bridge.Sim.Plant.Dispatcher;
        float frac = 0f;
        if (!phone.HasRequest)
        {
            phoneCardTitle.text = d.EverSynced ? "The grid is quiet. Nobody has asked you for anything. Savour it."
                : "The phone is silent. Sync the generator to the grid and it will start ringing.";
            phoneCardStatus.text = d.EverSynced ? "No request" : "Not synced yet";
            phoneCardStatus.color = Dim;
            phoneBarText.text = "";
        }
        else if (phone.Status == RequestStatus.Pending)
        {
            phoneCardTitle.text = phone.Caller;
            phoneCardStatus.text = "INCOMING  window opens in " + PhonePage.FormatCountdown(phone.SecondsToStart);
            phoneCardStatus.color = Warn;
            phoneBarText.text = "target " + ReactorSim.Ui.ReadoutFormat.Value(phone.TargetMw, 0, "MW");
        }
        else
        {
            phoneCardTitle.text = phone.Caller;
            phoneCardStatus.text = "LIVE  " + PhonePage.FormatCountdown(phone.SecondsToEnd) + " left";
            bool onTrack = phone.AverageSoFarMw >= phone.TargetMw * (1.0 - d.Config.ToleranceFrac);
            phoneCardStatus.color = onTrack ? Good : Crit;
            frac = (float)phone.DeliveredFraction;
            phoneBarText.text = ReactorSim.Ui.ReadoutFormat.Number(phone.AverageSoFarMw, 1) + " / " + ReactorSim.Ui.ReadoutFormat.Value(phone.TargetMw, 0, "MW") + " average";
        }
        phoneBarFill.anchorMax = new Vector2(Mathf.Clamp01(frac), 1f);
        phoneBarFillImg.color = phone.HasRequest && phone.Status == RequestStatus.Active
            && phone.AverageSoFarMw < phone.TargetMw * (1.0 - d.Config.ToleranceFrac) ? ThemeColors.FillCritical : ThemeColors.ButtonOn;
        float mx = phone.HasRequest && phone.Status == RequestStatus.Active ? (float)phone.WindowElapsedFraction : 0f;
        phoneBarMark.anchorMin = new Vector2(Mathf.Clamp01(mx), 0f);
        phoneBarMark.anchorMax = new Vector2(Mathf.Clamp01(mx), 1f);
        phoneBarMark.gameObject.SetActive(phone.HasRequest && phone.Status == RequestStatus.Active);
        phoneStats.text = "Met " + phone.Met + "   Missed " + phone.Missed + "   Streak " + phone.Streak
            + (phone.HasRequest && phone.Status == RequestStatus.Active ? "     (white mark = how far through the window)" : "");
        for (int i = 0; i < HistoryRowCount; i++)
        {
            if (i >= phone.History.Count) { phoneHist[i].text = ""; continue; }
            var r = phone.History[i];
            bool met = r.Status == RequestStatus.Met;
            phoneHist[i].text = (met ? "MET     " : "MISSED  ") + ReactorSim.Ui.ReadoutFormat.Value(r.TargetMw, 0, "MW") + " for "
                + PhonePage.FormatCountdown(r.DurationSeconds) + "   avg " + ReactorSim.Ui.ReadoutFormat.Value(r.AverageMw, 1, "MW");
            phoneHist[i].color = met ? Good : Crit;
        }
    }

    private void RefreshMessagesView()
    {
        for (int i = 0; i < MessageRowCount; i++)
        {
            bool has = i < messages.Items.Count;
            msgRowGo[i].SetActive(has);
            msgShown[i] = has ? messages.Items[i] : null;
            if (!has) continue;
            var m = messages.Items[i];
            bool unread = messages.IsUnread(m);
            msgText[i].text = (unread ? "* " : "") + MessagesPage.FormatStamp(m.AtSeconds) + "  " + m.Sender + "\n" + m.Text;
            msgText[i].color = unread ? ThemeColors.TextBody : Dim;
            msgImg[i].color = unread ? TabOn : TabOff;
        }
        msgEmpty.gameObject.SetActive(messages.Items.Count == 0);
    }

    private void DoClock(ClockAction a)
    {
        if (bridge == null || bridge.Sim == null) return;
        var clock = bridge.Clock;
        bool paused = clock != null && clock.Paused;
        double scale = clock != null ? clock.Scale : 1.0;
        var intent = ClockActions.Build(a, paused, scale);
        if (intent == null) return;
        bridge.SubmitIntent(intent);
        nextRefresh = 0f;
    }

    // ---- refresh ---------------------------------------------------------------------------------------------

    private static string Age(double seconds)
    {
        if (seconds < 60.0) return ((int)seconds) + "s";
        if (seconds < 3600.0) return ((int)(seconds / 60.0)) + "m";
        return ((int)(seconds / 3600.0)) + "h";
    }

    private void RefreshPage(PlantSnapshot s)
    {
        headerClock.text = ClockReadout.Format(ClockReadout.FromClock(bridge.Clock));
        switch (page)
        {
            case 0: RefreshPins(s); break;
            case 1: RefreshAlarms(s); break;
            case 2: RefreshTrends(); break;
            case 3: RefreshClock(); break;
            case PhoneTab: RefreshPhone(); break;
        }
    }

    private void RefreshPins(PlantSnapshot s)
    {
        for (int i = 0; i < PinBoard.Cap; i++)
        {
            bool has = i < pins.Count;
            pinRowGo[i].SetActive(has);
            if (!has) continue;
            var r = pins.Rows[i];
            pinLabel[i].text = r.Label;
            pinValue[i].text = r.Format(s);
        }
        pinEmpty.gameObject.SetActive(pins.Count == 0);
    }

    private void RefreshAlarms(PlantSnapshot s)
    {
        var list = alarms.Active;
        int shown = Mathf.Min(list.Count, AlarmRowCount);
        double now = s != null ? s.SimSeconds : 0.0;
        for (int i = 0; i < AlarmRowCount; i++)
        {
            bool has = i < shown;
            alarmRowGo[i].SetActive(has);
            alarmShown[i] = has ? list[i] : null;
            if (!has) continue;
            var a = list[i];
            alarmText[i].text = (a.Severity == AlarmLevel.Critical ? "CRIT  " : "WARN  ") + a.Label + "   " + Age(now - a.RaisedAtSimSeconds);
            alarmText[i].color = a.Severity == AlarmLevel.Critical ? Crit : Warn;
        }
        alarmMore.text = list.Count > AlarmRowCount ? "+" + (list.Count - AlarmRowCount) + " more" : "";
        alarmEmpty.gameObject.SetActive(list.Count == 0);
    }

    private void RefreshTrends()
    {
        for (int s = 0; s < 4; s++)
        {
            var series = (TabletTrends.Series)s;
            double latest = trends.Latest(series);
            trendTitle[s].text = TabletTrends.Titles[s] + "   "
                + ReactorSim.Ui.ReadoutFormat.Value(latest, s == 3 ? 2 : 1, TabletTrends.UnitNames[s]);
            double[] n = trends.Normalized(series, TrendBars);
            int first = TrendBars - n.Length;
            for (int i = 0; i < TrendBars; i++)
            {
                var bar = trendBar[s][i];
                if (i < first) { bar.enabled = false; continue; }
                bar.enabled = true;
                var rt = (RectTransform)bar.transform;
                rt.anchorMax = new Vector2(rt.anchorMax.x, 0.05f + 0.95f * (float)n[i - first]);
            }
        }
    }

    private void RefreshClock()
    {
        clockState.text = ClockReadout.Format(ClockReadout.FromClock(bridge.Clock));
        if (clockPauseLabel != null)
            clockPauseLabel.text = bridge.Clock != null && bridge.Clock.Paused ? "Resume" : "Pause";
    }

    // ---- HUD -------------------------------------------------------------------------------------------------

    private void ShowToast(string text)
    {
        toast = text;
        toastUntil = Time.unscaledTime + 2.4f;
    }

    // Toasts (short notices, the phone notice and the Tab hint) on their own overlay canvas.
    private void LateUpdate()
    {
        bool showToast = Time.unscaledTime < toastUntil;
        bool showPhone = Time.unscaledTime < phoneToastUntil;
        bool showHint = !raised && bridge != null && bridge.Sim != null && !PauseMenu.IsOpen;
        if (toastRoot == null)
        {
            if (!showToast && !showPhone && !showHint) return;
            BuildToasts();
        }
        if (toastBox.activeSelf != showToast) toastBox.SetActive(showToast);
        if (showToast && toastText.text != toast) toastText.text = toast;
        if (phoneToastBox.activeSelf != showPhone) phoneToastBox.SetActive(showPhone);
        string phoneLine = "Phone  " + phoneToast;
        if (showPhone && phoneToastText.text != phoneLine) phoneToastText.text = phoneLine;
        if (hintBox.activeSelf != showHint) hintBox.SetActive(showHint);
    }

    private void BuildToasts()
    {
        var canvas = RuntimeUi.CreateCanvas("TabletToastCanvas", 95, transform);
        canvas.GetComponent<GraphicRaycaster>().enabled = false;
        toastRoot = canvas.gameObject;
        toastBox = RuntimeUi.MakeToast(canvas.transform, "Toast", new Vector2(0.5f, 1f), new Vector2(0f, -56f), 0f,
            TextRole.Caption, TextAnchor.MiddleCenter, ThemeColors.TextBody, out toastText).gameObject;
        phoneToastBox = RuntimeUi.MakeToast(canvas.transform, "PhoneToast", new Vector2(0f, 0f), new Vector2(12f, 12f), 460f,
            TextRole.Caption, TextAnchor.MiddleLeft, ThemeColors.StatusCaution, out phoneToastText).gameObject;   // on the tooltip backing token
        hintBox = RuntimeUi.MakeToast(canvas.transform, "TabHint", new Vector2(1f, 0f), new Vector2(-12f, 12f), 0f,
            TextRole.Caption, TextAnchor.MiddleCenter, ThemeColors.TextBody, out hintText).gameObject;
        hintText.text = "Tab  tablet";
        toastBox.SetActive(false);
        phoneToastBox.SetActive(false);
        hintBox.SetActive(false);
    }
}
