using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// U11 front door: Sandbox, Missions, Quit. The whole screen is built in code at Start, so Menu.unity holds only
/// a camera and this component. Sandbox starts cold shutdown with pumps off; Missions starts the existing M1 path.
/// </summary>
public class MainMenu : MonoBehaviour
{
    private void Start()
    {
        Time.timeScale = 1f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        RuntimeUi.EnsureEventSystem();
        if (GetComponent<InputProbe>() == null) gameObject.AddComponent<InputProbe>();
        // Playtest builds stay in a closable window; Unity otherwise restores the last (fullscreen) mode from the registry.
        if (!Application.isBatchMode && Screen.fullScreenMode != FullScreenMode.Windowed)
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);

        // Agent smoke run: `ReactorSim.exe -smoke 60` skips the menu, plays the sandbox for 60 real seconds and quits
        // normally, so the session log, telemetry and Player.log are written exactly as after a real playtest.
        float smoke = SmokeSecondsFromArgs();
        if (smoke > 0f)
        {
            var quitter = new GameObject("SmokeQuit").AddComponent<SmokeQuit>();
            quitter.seconds = smoke;
            DontDestroyOnLoad(quitter.gameObject);
            Debug.Log("[MainMenu] smoke run: sandbox for " + smoke + " s, then quit");
            Launch(StartMode.Sandbox);
            return;
        }

        var canvas = RuntimeUi.CreateCanvas("MenuCanvas", 0, transform);
        RuntimeUi.Stretch(canvas.transform, "Backdrop", ThemeColors.SurfaceScreen);
        var col = RuntimeUi.Column(canvas.transform, "Column", 640f, ThemeColors.SurfacePanel);
        var dim = ThemeColors.TextDim;

        RuntimeUi.MakeLabel(col, "REACTOR SIM", 64, TextAnchor.MiddleCenter, ThemeColors.TextHeading, 90f);
        RuntimeUi.MakeLabel(col, "Pressurized-water reactor control room", 24, TextAnchor.MiddleCenter, dim, 36f);
        RuntimeUi.MakeButton(col, "Sandbox", () => Launch(StartMode.Sandbox), 70f);
        RuntimeUi.MakeLabel(col, "Cold shutdown, pumps off. Take it from nothing to the grid.", 20, TextAnchor.MiddleCenter, dim, 30f);
        RuntimeUi.MakeButton(col, "Missions", () => Launch(StartMode.Missions), 70f);
        RuntimeUi.MakeLabel(col, "Guided missions (optional).", 20, TextAnchor.MiddleCenter, dim, 30f);
        RuntimeUi.MakeButton(col, "Quit", Quit, 70f);
    }

    private static float SmokeSecondsFromArgs()
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "-smoke" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float s) && s > 0f)
                return s;
        }
        return 0f;
    }

    private sealed class SmokeQuit : MonoBehaviour
    {
        public float seconds;
        private float elapsed;
        private bool firstShot, lastShot;

        private void Update()
        {
            seconds -= Time.unscaledDeltaTime;
            elapsed += Time.unscaledDeltaTime;
            // Screenshots only mean something with a real graphics device (not -nographics).
            bool canShoot = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            if (!firstShot && elapsed > 4f)
            {
                firstShot = true;
                SceneProbe.Log("smoke +4s");
                if (canShoot) SceneProbe.Screenshot("smoke-start");
            }
            if (!lastShot && seconds <= 1.5f)
            {
                lastShot = true;
                SceneProbe.Log("smoke end");
                if (canShoot) SceneProbe.Screenshot("smoke-end");
            }
            if (seconds <= 0f)
            {
                enabled = false;
                Debug.Log("[MainMenu] smoke run complete, quitting");
                Application.Quit();
            }
        }
    }

    private static void Launch(StartMode mode)
    {
        GameSession.Mode = mode;
        SceneManager.LoadScene(GameSession.PlantScene);
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
