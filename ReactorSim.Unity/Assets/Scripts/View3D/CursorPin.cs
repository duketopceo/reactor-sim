using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Locks the cursor for mouse-look and dial drags and puts the pointer back where it was when the lock ends, so releasing the
/// right mouse button or a dial does not leave the pointer jumped to the window centre (R9). The restore is a Windows player
/// feature (user32 SetCursorPos); in the editor and on other platforms the lock behaves as plain Unity.
/// Call Tick() once per frame (OperatorController does).
/// </summary>
public static class CursorPin
{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int x, y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);

    private static Point saved;
    private static bool hasSaved;
#endif

    private static readonly CursorLockModel model = new CursorLockModel();

    public static bool Locked { get { return model.Locked; } }

    /// <summary>Takes a hold on the cursor. Holds nest: mouse-look and a dial drag can overlap.</summary>
    public static void Lock()
    {
        if (!model.Acquire()) return;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        hasSaved = GetCursorPos(out saved);
#endif
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>Drops a hold; the cursor frees and the pointer restore is scheduled when the last one goes.</summary>
    public static void Unlock()
    {
        if (!model.Release(Time.frameCount)) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>Drops a scheduled pointer restore. Tick runs from OperatorController.Update, so a gate that disables the
    /// controller must cancel it or the pointer snaps back later, over the overlay.</summary>
    public static void CancelRestore() { model.CancelRestore(); }

    public static void Tick()
    {
        if (!model.RestoreDue(Time.frameCount)) return;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (hasSaved && Application.isFocused) SetCursorPos(saved.x, saved.y);
#endif
    }
}
