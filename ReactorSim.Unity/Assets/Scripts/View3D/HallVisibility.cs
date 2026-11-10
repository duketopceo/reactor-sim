using UnityEngine;
using ReactorSim;

// Frustum gate for the reactor hall (KTD9). The hall is only ever seen through the control-room window, so when the window
// opening is outside the camera frustum the whole hall root (renderers, lights, the equipment binder) is switched off. This
// component sits on its own object, outside the hall root, so it keeps running while the hall is off.
public class HallVisibility : MonoBehaviour
{
    public GameObject hallRoot;
    /// <summary>World-space box around the window opening, padded a little so the hall is on before the edge shows.</summary>
    public Bounds windowBounds;
    public Camera viewCamera;

    private static HallVisibility instance;
    private bool lastVisible = true;
    private readonly double[] matrix = new double[16];

    /// <summary>The hall root, found even while it is switched off (GameObject.Find skips inactive objects).</summary>
    public static GameObject FindHall()
    {
        if (instance != null && instance.hallRoot != null) return instance.hallRoot;
        return GameObject.Find("ReactorHall");
    }

    private void Awake() { instance = this; }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void LateUpdate()
    {
        if (hallRoot == null) return;
        Camera cam = viewCamera != null ? viewCamera : Camera.main;
        bool visible = true;
        if (cam != null)
        {
            Matrix4x4 vp = cam.projectionMatrix * cam.worldToCameraMatrix;
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++) matrix[r * 4 + c] = vp[r, c];
            Vector3 lo = windowBounds.min, hi = windowBounds.max;
            visible = HallVisibilityMath.AabbVisible(HallVisibilityMath.PlanesFromViewProjection(matrix), lo.x, lo.y, lo.z, hi.x, hi.y, hi.z);
        }
        if (visible != lastVisible || hallRoot.activeSelf != visible)
        {
            hallRoot.SetActive(visible);
            lastVisible = visible;
        }
    }
}
