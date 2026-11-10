using UnityEngine;
using ReactorSim;

// Unity read-out for U8: consumes Core AssistantEngine missions (not a second impl).
// Mission progress is computed in Core (Plant.Assistant.Evaluate); this just displays it
// on the CRT screens and HUD, and selects missions via PlantIntent.SetMission.
public class MissionRunner : MonoBehaviour
{
    public PlantBridge bridge;
    public string missionTitle = "";
    public string currentStep = "";
    public bool missionComplete;

    private void Awake()
    {
        if (bridge == null)
            bridge = FindAnyObjectByType<PlantBridge>();
    }

    public void SelectMission(int index)
    {
        if (bridge == null) return;
        bridge.SubmitIntent(PlantIntent.SetMission(index, "mission-hud"));
    }

    private void Update()
    {
        if (bridge == null || bridge.Sim == null) return;
        var eng = bridge.Sim.Plant.Assistant;
        if (eng == null || eng.CurrentMission == null) return;
        var m = eng.CurrentMission;
        missionTitle = $"M{m.Index + 1} {m.Title}";
        missionComplete = m.Completed;
        int stepIdx = m.CurrentStepIndex;
        if (stepIdx >= 0 && stepIdx < m.Steps.Count)
            currentStep = m.Steps[stepIdx].Title;
    }
}
