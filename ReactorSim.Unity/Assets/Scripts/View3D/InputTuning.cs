using System;
using System.Globalization;
using System.Text.RegularExpressions;

// Pure logic (no UnityEngine): dial-drag and movement tuning the owner can change without a rebuild
// (StreamingAssets/input-tuning.json). Defaults are the shipped constants (DialDrag, OperatorMath). It is a separate file
// from tuning.json because the sim's own profile rejects unknown keys. Unknown keys and out-of-range values are an error,
// missing keys keep their defaults. Linked into ReactorSim.Core.Tests.
namespace ReactorSim.Controls
{
    public sealed class InputTuningException : Exception
    {
        public InputTuningException(string message) : base(message) { }
    }

    public sealed class InputTuning
    {
        /// <summary>Global drag gain: above 1 means fewer pixels per sweep.</summary>
        public double DragGain = 1.0;
        public double DragDeadZonePixels = DialDrag.DefaultDeadZonePixels;
        public double DragFineFactor = DialDrag.DefaultFineFactor;
        public double DragContinuousPixels = DialDrag.DefaultContinuousPixels;
        public double DragDetentPixels = DialDrag.DefaultDetentPixels;
        public double WalkSpeed = OperatorMath.WalkSpeed;
        public double SprintMultiplier = OperatorMath.SprintMultiplier;
        public double AccelSeconds = OperatorMath.AccelSeconds;
        public double DecelSeconds = OperatorMath.DecelSeconds;

        private static readonly Regex Pair = new Regex("\"([A-Za-z0-9_]+)\"\\s*:\\s*([^,}\\s]+)", RegexOptions.CultureInvariant);

        public static InputTuning Parse(string json)
        {
            var t = new InputTuning();
            if (string.IsNullOrWhiteSpace(json)) return t;
            foreach (Match m in Pair.Matches(json))
            {
                string key = m.Groups[1].Value;
                double v;
                if (!double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                    || double.IsNaN(v) || double.IsInfinity(v))
                    throw new InputTuningException("input tuning key \"" + key + "\" needs a number, got " + m.Groups[2].Value);
                switch (key)
                {
                    case "dragGain": t.DragGain = v; break;
                    case "dragDeadZonePixels": t.DragDeadZonePixels = v; break;
                    case "dragFineFactor": t.DragFineFactor = v; break;
                    case "dragContinuousPixels": t.DragContinuousPixels = v; break;
                    case "dragDetentPixels": t.DragDetentPixels = v; break;
                    case "walkSpeed": t.WalkSpeed = v; break;
                    case "sprintMultiplier": t.SprintMultiplier = v; break;
                    case "accelSeconds": t.AccelSeconds = v; break;
                    case "decelSeconds": t.DecelSeconds = v; break;
                    default: throw new InputTuningException("unknown input tuning key \"" + key + "\"");
                }
            }
            t.Validate();
            return t;
        }

        public void Validate()
        {
            if (!(DragGain > 0.0)) throw new InputTuningException("dragGain must be > 0");
            if (!(DragDeadZonePixels >= 0.0)) throw new InputTuningException("dragDeadZonePixels must be >= 0");
            if (!(DragFineFactor > 0.0 && DragFineFactor <= 1.0)) throw new InputTuningException("dragFineFactor must be in (0, 1]");
            if (!(DragContinuousPixels > 0.0)) throw new InputTuningException("dragContinuousPixels must be > 0");
            if (!(DragDetentPixels > 0.0)) throw new InputTuningException("dragDetentPixels must be > 0");
            if (!(WalkSpeed > 0.0)) throw new InputTuningException("walkSpeed must be > 0");
            if (!(SprintMultiplier >= 1.0)) throw new InputTuningException("sprintMultiplier must be >= 1");
            if (!(AccelSeconds >= 0.0)) throw new InputTuningException("accelSeconds must be >= 0");
            if (!(DecelSeconds >= 0.0)) throw new InputTuningException("decelSeconds must be >= 0");
        }
    }
}
