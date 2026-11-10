using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ReactorSim;
using ReactorSim.Ui;

/// <summary>
/// Mission panel (missions mode only): the mission title, the current step and the latest advisory, drawn with uGUI on
/// the shared RuntimeUi kit. Display only; the values come from the Core AssistantEngine through MissionHudModel.
/// </summary>
public class MissionHud : MonoBehaviour
{
    public PlantBridge bridge;
    public MissionRunner runner;
    public bool showHud = true;

    private Canvas canvas;
    private TMP_Text title, step, advisory;

    private void Awake()
    {
        if (bridge == null)
            bridge = FindAnyObjectByType<PlantBridge>();
        if (runner == null)
        {
            runner = GetComponent<MissionRunner>();
            if (runner == null) runner = gameObject.AddComponent<MissionRunner>();
            runner.bridge = bridge;
        }
    }

    private void Build()
    {
        RuntimeUi.EnsureEventSystem();
        canvas = RuntimeUi.CreateCanvas("MissionCanvas", 40, transform);
        var panel = RuntimeUi.Panel(canvas.transform, "Panel", 470f, ThemeColors.SurfaceTooltip, new Vector2(0f, 1f),
            new Vector2(0f, 1f), new RectOffset(Theme.Space3, Theme.Space3, Theme.Space3, Theme.Space3), Theme.Space1);
        panel.anchoredPosition = new Vector2(10f, -10f);
        panel.GetComponent<Image>().raycastTarget = false;
        title = RuntimeUi.MakeLabel(panel, "", TextRole.Heading, TextAnchor.MiddleLeft, ThemeColors.TextHeading);
        title.fontStyle = FontStyles.Bold;
        step = RuntimeUi.MakeLabel(panel, "", TextRole.Body, TextAnchor.MiddleLeft, ThemeColors.TextBody);
        advisory = RuntimeUi.MakeLabel(panel, "", TextRole.Caption, TextAnchor.MiddleLeft, ThemeColors.TextDim);
    }

    private static void Set(TMP_Text t, string text, Color color)
    {
        bool has = !string.IsNullOrEmpty(text);
        if (t.gameObject.activeSelf != has) t.gameObject.SetActive(has);
        if (has && t.text != text) t.text = text;
        t.color = color;
    }

    // Player text is the plain-language next action. The raw finding, evidence and category line is a diagnostic: it
    // shows only in the editor and development builds, toggled with F3.
    private static bool diagnostics;

    private static string AdvisoryText(AdvisoryMessage a)
    {
        if (a == null) return null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (diagnostics) return a.FormatTerminalLine();
#endif
        return string.IsNullOrEmpty(a.Recommendation) ? null : "Next: " + a.Recommendation;
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.F3)) diagnostics = !diagnostics;
#endif
        MissionHudModel model = null;
        if (showHud && bridge != null && bridge.Sim != null && bridge.Sim.Plant.Assistant != null)
        {
            var eng = bridge.Sim.Plant.Assistant;
            var m = eng.CurrentMission;
            int index = m != null ? m.CurrentStepIndex : -1;
            int count = m != null ? m.Steps.Count : 0;
            model = MissionHudModel.Build(GameSession.Mode == StartMode.Missions, m != null ? m.Title : null, index, count,
                index >= 0 && index < count ? m.Steps[index].Title : null, m != null && m.Completed,
                AdvisoryText(eng.LatestAdvisory));
        }
        bool visible = model != null && model.Visible && !PauseMenu.IsOpen;
        if (!visible)
        {
            if (canvas != null) canvas.gameObject.SetActive(false);
            return;
        }
        if (canvas == null) Build();
        canvas.gameObject.SetActive(true);
        Set(title, model.Title, ThemeColors.TextHeading);
        Set(step, model.Step, model.Complete ? ThemeColors.StatusOk : ThemeColors.TextBody);
        Set(advisory, model.Advisory, ThemeColors.TextDim);
    }
}
