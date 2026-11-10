using UnityEngine;

/// <summary>
/// Playtest diagnostics: every 5 s, one Player.log line comparing what the legacy Input API sees with what
/// the window's own event stream (IMGUI) sees. If IMGUI counts clicks and keys but legacy Input stays at zero,
/// the input backend is the problem, not the controls.
/// </summary>
public class InputProbe : MonoBehaviour
{
    private float next;
    private int legacyKeys, legacyClicks, guiKeys, guiClicks;
    private Vector3 lastMouse;
    private float mouseTravel;
    private int frames;
    private float worstFrame;

    private void Update()
    {
        frames++;
        worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
        if (Input.anyKeyDown) legacyKeys++;
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) legacyClicks++;
        Vector3 m = Input.mousePosition;
        mouseTravel += (m - lastMouse).magnitude;
        lastMouse = m;

        if (Time.unscaledTime >= next)
        {
            next = Time.unscaledTime + 5f;
            Debug.Log("[InputProbe] fps=" + (frames / 5f).ToString("F1") + " worst=" + (worstFrame * 1000f).ToString("F0") + "ms"
                + " focused=" + Application.isFocused
                + " legacy keys=" + legacyKeys + " clicks=" + legacyClicks + " mouse=" + m.ToString("F0") + " travel=" + mouseTravel.ToString("F0")
                + " | window-events keys=" + guiKeys + " clicks=" + guiClicks
                + " | cursor=" + Cursor.lockState + " screen=" + Screen.width + "x" + Screen.height + " mode=" + Screen.fullScreenMode);
            frames = 0;
            worstFrame = 0f;
        }
    }

    private void OnGUI()
    {
        var e = Event.current;
        if (e == null) return;
        if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None) guiKeys++;
        if (e.type == EventType.MouseDown) guiClicks++;
    }
}
