// Pure logic (no UnityEngine): the hold count and deferred pointer-restore schedule behind CursorPin. Mouse-look and a dial
// drag can both hold the cursor at once (RMB-look, then LMB on a control); the cursor frees only when the last hold is
// released. Linked into ReactorSim.Core.Tests.
public sealed class CursorLockModel
{
    // Unity applies a lock change at the end of the frame, so the pointer is restored two frames after the last release.
    public const int RestoreDelayFrames = 2;

    private int holds;
    private int restoreAtFrame = -1;

    public bool Locked { get { return holds > 0; } }
    public bool RestorePending { get { return restoreAtFrame >= 0; } }

    /// <summary>Takes a hold. True when this is the first one, so the caller should actually lock the cursor.</summary>
    public bool Acquire()
    {
        holds++;
        restoreAtFrame = -1;
        return holds == 1;
    }

    /// <summary>Drops a hold. True when it was the last one, so the caller should free the cursor.</summary>
    public bool Release(int frame)
    {
        if (holds == 0) return false;
        holds--;
        if (holds > 0) return false;
        restoreAtFrame = frame + RestoreDelayFrames;
        return true;
    }

    /// <summary>Drops a scheduled pointer restore (the owner of the tick is gone).</summary>
    public void CancelRestore() { restoreAtFrame = -1; }

    /// <summary>True once, on the frame the scheduled restore falls due.</summary>
    public bool RestoreDue(int frame)
    {
        if (restoreAtFrame < 0 || frame < restoreAtFrame) return false;
        restoreAtFrame = -1;
        return true;
    }
}
