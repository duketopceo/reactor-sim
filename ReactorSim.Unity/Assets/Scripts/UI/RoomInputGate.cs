using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Suspends the room's first-person input (OperatorController look/walk and DiegeticInteractable clicks) and frees the
/// cursor, then re-enables exactly the behaviours it disabled. Shared by PauseMenu, OperatorTablet and
/// ProcedureClipboard; each owns its own gate so one overlay never re-enables what another one suspended.
/// </summary>
public sealed class RoomInputGate
{
    private readonly List<Behaviour> suspended = new List<Behaviour>();

    /// <summary>True while this gate still holds disabled behaviours.</summary>
    public bool HasSuspended { get { return suspended.Count > 0; } }

    public void Suspend()
    {
        suspended.Clear();
        foreach (var c in Object.FindObjectsByType<OperatorController>(FindObjectsSortMode.None)) SuspendOne(c);
        foreach (var c in Object.FindObjectsByType<DiegeticInteractable>(FindObjectsSortMode.None)) SuspendOne(c);
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void Restore()
    {
        foreach (var b in suspended) if (b != null) b.enabled = true;
        suspended.Clear();
    }

    private void SuspendOne(Behaviour b)
    {
        if (b == null || !b.enabled) return;
        b.enabled = false;
        suspended.Add(b);
    }
}
