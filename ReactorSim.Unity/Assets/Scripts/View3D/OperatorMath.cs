using System;

// Pure logic (no UnityEngine): clamp, speed, eye-height and range maths of the walkable operator (OperatorController).
// Linked into ReactorSim.Core.Tests.
public static class OperatorMath
{
    public const float SeatedEyeHeight = 1.2f;
    public const float StandingEyeHeight = 1.65f;
    /// <summary>Cursor ray operates a control within this distance of the eye (design/control-room/layout.json interaction_range_m).</summary>
    public const float InteractionRange = 2.0f;
    /// <summary>A chair this close (horizontal metres) can be sat on.</summary>
    public const float SitRange = 1.5f;
    /// <summary>The chair counts as "in view" within this angle of the view direction.</summary>
    public const float SitViewHalfAngle = 60f;
    /// <summary>Standing this close to the chair, it is under the eye and out of any view cone; it is still offered.</summary>
    public const float AtChairDistance = 0.6f;
    public const float SeatedYawRange = 110f;
    public const float MinPitch = -60f;   // looking up
    public const float MaxPitch = 55f;    // looking down
    public const float WalkSpeed = 1.6f;
    /// <summary>Chair swivel rate while Q or E is held (seated; stays inside SeatedYawRange).</summary>
    public const float SwivelDegPerSec = 90f;

    /// <summary>Board sections in R4 order; number keys 1-6 travel to STAND_&lt;section&gt; in this order.</summary>
    public static readonly string[] BoardSections = { "reactor", "primary", "safety", "steam", "turbine", "electrical" };

    /// <summary>Section for quick-travel key 1-6, or null for any other number.</summary>
    public static string SectionForKey(int number)
    {
        return number >= 1 && number <= BoardSections.Length ? BoardSections[number - 1] : null;
    }

    /// <summary>Anchor name quick-travel key 1-6 moves to (STAND_&lt;section&gt;), or null.</summary>
    public static string StandAnchorForKey(int number)
    {
        string sec = SectionForKey(number);
        return sec == null ? null : "STAND_" + sec;
    }

    /// <summary>
    /// Yaw (degrees about up, Unity: atan2(x, z)) of the way an operator at (px, pz) faces the board: away from the
    /// arc centre (cx, cz). A point on the centre has no outward direction and returns the fallback.
    /// </summary>
    public static float OutwardYaw(float px, float pz, float cx, float cz, float fallback)
    {
        float dx = px - cx, dz = pz - cz;
        if (dx * dx + dz * dz <= 1e-4f) return fallback;
        return (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
    }

    /// <summary>Turns the chair by dir (-1 left, +1 right) for dt seconds, never past the seated yaw clamp around baseYaw.</summary>
    public static float Swivel(float yaw, float baseYaw, int dir, float dt, float range)
    {
        if (dir == 0 || dt <= 0f) return ClampYaw(yaw, baseYaw, range);
        return ClampYaw(yaw + Math.Sign(dir) * SwivelDegPerSec * dt, baseYaw, range);
    }

    // ---- player settings (pause menu): ranges shared by the UI and the persistence layer ----
    public const float TextScaleMin = 0.8f, TextScaleMax = 1.5f, TextScaleDefault = 1f;
    public const float FovMin = 50f, FovMax = 90f, FovDefault = 70f;
    public const float SensitivityMin = 0.4f, SensitivityMax = 2.5f, SensitivityDefault = 1f;

    public static float ClampTextScale(float v) { return Clamp(v, TextScaleMin, TextScaleMax, TextScaleDefault); }
    public static float ClampFov(float v) { return Clamp(v, FovMin, FovMax, FovDefault); }
    public static float ClampSensitivity(float v) { return Clamp(v, SensitivityMin, SensitivityMax, SensitivityDefault); }

    private static float Clamp(float v, float lo, float hi, float fallback)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) return fallback;
        return Math.Min(hi, Math.Max(lo, v));
    }

    /// <summary>Maps a 0..1 slider position onto [lo, hi].</summary>
    public static float FromSlider(float t, float lo, float hi)
    {
        return lo + Math.Min(1f, Math.Max(0f, t)) * (hi - lo);
    }

    public static float ToSlider(float v, float lo, float hi)
    {
        return hi > lo ? Math.Min(1f, Math.Max(0f, (v - lo) / (hi - lo))) : 0f;
    }

    public static float DeltaAngle(float current, float target)
    {
        float d = (target - current) % 360f;
        if (d < 0f) d += 360f;
        if (d > 180f) d -= 360f;
        return d;
    }

    public static float ClampPitch(float pitch)
    {
        return Math.Min(MaxPitch, Math.Max(MinPitch, pitch));
    }

    /// <summary>Keeps yaw within +-range of baseYaw; a range of 180 or more is free look.</summary>
    public static float ClampYaw(float yaw, float baseYaw, float range)
    {
        if (range >= 180f) return yaw;
        float d = DeltaAngle(baseYaw, yaw);
        return baseYaw + Math.Min(range, Math.Max(-range, d));
    }

    public static float EyeHeight(bool seated)
    {
        return seated ? SeatedEyeHeight : StandingEyeHeight;
    }

    public static float EyeY(float anchorFloorY, bool seated)
    {
        return anchorFloorY + EyeHeight(seated);
    }

    public static float EffectiveWalkSpeed(bool seated, bool dragging, float speed)
    {
        return (seated || dragging) ? 0f : speed;
    }

    public static bool InInteractionRange(float distance)
    {
        return distance <= InteractionRange;
    }

    public static bool SitPromptVisible(float horizontalDistanceToChair, float angleToChairDeg)
    {
        if (horizontalDistanceToChair > SitRange) return false;
        return horizontalDistanceToChair <= AtChairDistance || angleToChairDeg <= SitViewHalfAngle;
    }
}
