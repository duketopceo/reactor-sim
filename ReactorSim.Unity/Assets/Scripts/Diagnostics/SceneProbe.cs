using System.IO;
using UnityEngine;

/// <summary>
/// Playtest diagnostics: one Player.log line describing what the player can actually see and click
/// (camera, cursor, controls with colliders, what is under the screen centre), plus optional screenshots.
/// Lets the agent debug a "nothing works" report from the fetched logs alone.
/// </summary>
public static class SceneProbe
{
    public static void Log(string when)
    {
        var cam = Camera.main;
        string camInfo = cam == null
            ? "camera=NONE (no MainCamera tag)"
            : "camera=" + cam.name + " pos=" + cam.transform.position.ToString("F2") + " euler=" + cam.transform.eulerAngles.ToString("F1")
              + " fov=" + cam.fieldOfView.ToString("F0") + " enabled=" + cam.enabled;

        int controls = 0, clickable = 0, outOfService = 0, pinnable = 0;
        foreach (var d in Object.FindObjectsByType<DiegeticInteractable>(FindObjectsInactive.Include))
        {
            if (d.OutOfService) { outOfService++; continue; }
            if (d.pinnable) { pinnable++; continue; }   // indicator hover targets for tablet pins, not controls
            controls++;
            var c = d.GetComponent<Collider>();
            if (d.isActiveAndEnabled && c != null && c.enabled) clickable++;
        }

        string centre = "centre=n/a";
        if (cam != null)
        {
            var ray = cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
            centre = Physics.Raycast(ray, out var hit, 200f)
                ? "centre hits " + hit.collider.name + " at " + hit.distance.ToString("F1") + " m"
                : "centre hits nothing";
        }

        Debug.Log("[SceneProbe] " + when + ": " + camInfo + " | screen=" + Screen.width + "x" + Screen.height
            + " cursor=" + Cursor.lockState + (Cursor.visible ? "/visible" : "/hidden")
            + " | controls=" + controls + " clickable=" + clickable + " oos=" + outOfService + " pinnable=" + pinnable + " | " + centre
            + " | timeScale=" + Time.timeScale);
    }

    /// <summary>Saves a PNG under persistentDataPath/shots/ (written at the end of the frame).</summary>
    public static void Screenshot(string label)
    {
        string dir = Path.Combine(Application.persistentDataPath, "shots");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + label + ".png");
        ScreenCapture.CaptureScreenshot(path);
        Debug.Log("[SceneProbe] screenshot " + path);
    }
}
