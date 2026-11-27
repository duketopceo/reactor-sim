using System;

// Pure logic (no UnityEngine): the lift as a prompted interaction with a locked fade (Phase 2 KTD7). One activation starts a
// transfer from the landing the operator stands on to the other landing of the same lift; movement and look stay locked from
// the start of the fade out to the end of the fade in; the operator arrives on the other landing facing out of the car
// (RouteGraph.LiftLanding.ExitHeadingDeg); activation is ignored while a transfer runs. Each Step advances at most one phase,
// so a long frame can never skip the move. LiftInteraction (engine slice) only drives this and applies the arrival.
// Linked into ReactorSim.Core.Tests.
namespace ReactorSim
{
    public enum LiftPhase
    {
        Idle,
        FadingOut,
        Moving,
        FadingIn
    }

    public sealed class LiftTransitionModel
    {
        /// <summary>Default fade lengths (seconds): short enough to feel immediate, long enough to hide the teleport.</summary>
        public const float DefaultFadeOutSeconds = 0.6f, DefaultFadeInSeconds = 0.6f;

        private readonly float fadeOutSeconds;
        private readonly float fadeInSeconds;
        private float elapsed;

        public LiftPhase Phase { get; private set; }
        /// <summary>The landing the operator is on, or is arriving at once the move has happened.</summary>
        public string CurrentLandingId { get; private set; }
        /// <summary>The landing the running transfer goes to; null when idle.</summary>
        public RouteGraph.LiftLanding Target { get; private set; }
        /// <summary>True when the move happened this Step: the driver places the operator at Target and faces ArrivalYawDeg.</summary>
        public bool MovedThisStep { get; private set; }

        public LiftTransitionModel(float fadeOutSeconds, float fadeInSeconds)
        {
            this.fadeOutSeconds = Finite(fadeOutSeconds, DefaultFadeOutSeconds);
            this.fadeInSeconds = Finite(fadeInSeconds, DefaultFadeInSeconds);
            Phase = LiftPhase.Idle;
        }

        public LiftTransitionModel() : this(DefaultFadeOutSeconds, DefaultFadeInSeconds) { }

        /// <summary>Movement and look are locked for the whole transfer.</summary>
        public bool InputLocked { get { return Phase != LiftPhase.Idle; } }

        /// <summary>Screen fade, 0 clear to 1 black.</summary>
        public float FadeAlpha
        {
            get
            {
                switch (Phase)
                {
                    case LiftPhase.FadingOut: return fadeOutSeconds > 0f ? Math.Min(1f, elapsed / fadeOutSeconds) : 1f;
                    case LiftPhase.Moving: return 1f;
                    case LiftPhase.FadingIn: return fadeInSeconds > 0f ? Math.Max(0f, 1f - elapsed / fadeInSeconds) : 0f;
                    default: return 0f;
                }
            }
        }

        public float ArrivalYawDeg { get { return Target != null ? Target.ExitHeadingDeg : float.NaN; } }
        public float ArrivalFloorH { get { return Target != null ? Target.FloorH : float.NaN; } }

        /// <summary>The other landing of the same lift car, or null when the ID is unknown or the car has no other landing.</summary>
        public static RouteGraph.LiftLanding OtherLanding(string landingId)
        {
            var from = RouteGraph.FindLanding(landingId);
            if (from == null) return null;
            foreach (var l in RouteGraph.Landings)
                if (l.RoomId == from.RoomId && l.Id != from.Id) return l;
            return null;
        }

        /// <summary>Starts a transfer from fromLandingId. Returns false, changing nothing, while a transfer runs or for an unknown landing.</summary>
        public bool TryStart(string fromLandingId)
        {
            if (Phase != LiftPhase.Idle) return false;
            var to = OtherLanding(fromLandingId);
            if (to == null) return false;
            CurrentLandingId = fromLandingId;
            Target = to;
            Phase = LiftPhase.FadingOut;
            elapsed = 0f;
            MovedThisStep = false;
            return true;
        }

        /// <summary>Advances the transfer by dt seconds, at most one phase per call. A zero, negative or non-finite dt does nothing.</summary>
        public void Step(float dt)
        {
            MovedThisStep = false;
            if (!(dt > 0f) || float.IsInfinity(dt)) return;
            switch (Phase)
            {
                case LiftPhase.FadingOut:
                    elapsed += dt;
                    if (elapsed >= fadeOutSeconds) { Phase = LiftPhase.Moving; elapsed = 0f; }
                    break;
                case LiftPhase.Moving:
                    CurrentLandingId = Target.Id;
                    MovedThisStep = true;
                    Phase = LiftPhase.FadingIn;
                    elapsed = 0f;
                    break;
                case LiftPhase.FadingIn:
                    elapsed += dt;
                    if (elapsed >= fadeInSeconds) { Phase = LiftPhase.Idle; elapsed = 0f; Target = null; }
                    break;
            }
        }

        private static float Finite(float seconds, float fallback)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return fallback;
            return Math.Max(0f, seconds);
        }
    }
}
