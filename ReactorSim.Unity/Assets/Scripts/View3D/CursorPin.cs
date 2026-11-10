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

    // Unity applies a lock change at the end of the frame, so the pointer is restored two frames after the unlock.
    private static int restoreAtFrame = -1;

    public static bool Locked { get; private set; }

    public static void Lock()
    {
        if (Locked) return;
        restoreAtFrame = -1;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        hasSaved = GetCursorPos(out saved);
#endif
        Locked = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public static void Unlock()
    {
        if (!Locked) return;
        Locked = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        restoreAtFrame = Time.frameCount + 2;
    }

    public static void Tick()
    {
        if (restoreAtFrame < 0 || Time.frameCount < restoreAtFrame) return;
        restoreAtFrame = -1;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (hasSaved && Application.isFocused) SetCursorPos(saved.x, saved.y);
#endif
    }
}
