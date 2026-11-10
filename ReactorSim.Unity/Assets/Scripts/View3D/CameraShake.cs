using UnityEngine;

// Trauma-style camera shake. This component only computes an offset and roll; OperatorController applies them in
// LateUpdate so shake composes with mouse-look instead of fighting it.
public class CameraShake : MonoBehaviour
{
    [Tooltip("Metres of positional shake at full trauma.")]
    public float maxOffset = 0.07f;
    [Tooltip("Degrees of roll at full trauma.")]
    public float maxRollDegrees = 2.2f;
    public float frequency = 24f;

    private float trauma;
    private float decayPerSecond = 0.5f;

    /// <summary>Current positional shake (world space), valid after Update.</summary>
    public Vector3 Offset { get; private set; }
    /// <summary>Current roll shake in degrees, valid after Update.</summary>
    public float Roll { get; private set; }

    /// <summary>Adds shake (amplitude 0..1) that fades out over roughly `seconds`.</summary>
    public void Kick(float amplitude, float seconds)
    {
        trauma = Mathf.Clamp01(Mathf.Max(trauma, amplitude));
        decayPerSecond = Mathf.Max(0.05f, trauma / Mathf.Max(0.1f, seconds));
    }

    private void Update()
    {
        if (trauma <= 0f)
        {
            Offset = Vector3.zero;
            Roll = 0f;
            return;
        }
        trauma = Mathf.Max(0f, trauma - decayPerSecond * Time.unscaledDeltaTime);
        float amp = trauma * trauma;
        float t = Time.unscaledTime * frequency;
        Offset = new Vector3(Noise(t, 1f), Noise(t, 2f), Noise(t, 3f)) * (maxOffset * amp);
        Roll = Noise(t, 4f) * maxRollDegrees * amp;
    }

    private static float Noise(float t, float seed)
    {
        return Mathf.PerlinNoise(t, seed * 17.3f) * 2f - 1f;
    }
}
