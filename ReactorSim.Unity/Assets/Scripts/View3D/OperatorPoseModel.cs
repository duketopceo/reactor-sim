// Pure logic (no UnityEngine): which pose the simple operator body shows and the size of its blob shadow (Phase 2 KTD6).
// The body is a static rig with two poses chosen from the existing seat/stand state; the shadow is a soft projected quad,
// because every room light is built without shadows. Linked into ReactorSim.Core.Tests.
public enum OperatorPose
{
    Standing,
    Seated
}

public static class OperatorPoseModel
{
    /// <summary>Blob-shadow radius (metres) under a standing body: a little wider than the 0.3 m body capsule.</summary>
    public const float StandingShadowRadius = 0.35f;
    /// <summary>Blob-shadow radius (metres) under a seated body, which sits partly over the chair.</summary>
    public const float SeatedShadowRadius = 0.3f;

    public static OperatorPose PoseFor(bool seated)
    {
        return seated ? OperatorPose.Seated : OperatorPose.Standing;
    }

    /// <summary>Eye height above the floor for a pose; the same numbers OperatorController uses (OperatorMath.EyeHeight).</summary>
    public static float EyeHeight(OperatorPose pose)
    {
        return OperatorMath.EyeHeight(pose == OperatorPose.Seated);
    }

    public static float ShadowRadius(OperatorPose pose)
    {
        return pose == OperatorPose.Seated ? SeatedShadowRadius : StandingShadowRadius;
    }
}
