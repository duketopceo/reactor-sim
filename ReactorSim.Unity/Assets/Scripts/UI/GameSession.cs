/// <summary>How the control room scene was entered (set by the menu, read by PlantBridge).</summary>
public enum StartMode
{
    Sandbox,
    Missions
}

/// <summary>Menu to plant hand-off. Static so it survives the scene load; defaults to Sandbox so opening
/// the Bootstrap scene directly (editor, batchmode checks) behaves as the sandbox.</summary>
public static class GameSession
{
    public const string MenuScene = "Menu";
    public const string PlantScene = "Bootstrap";

    public static StartMode Mode = StartMode.Sandbox;

    public static string ModeName
    {
        get { return Mode == StartMode.Missions ? "missions" : "sandbox"; }
    }
}
