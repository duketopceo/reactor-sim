using System.IO;
using UnityEngine;
using ReactorSim;
using ReactorSim.Controls;
using ReactorSim.Instruments;

/// <summary>
/// Loads StreamingAssets/inventory-runtime.json (generated from design/control-room/inventory.csv by
/// scripts/inventory-rollup.py) once. A missing or bad file logs once and leaves an empty inventory: tooltips then fall
/// back to the pivot's label and the binder's own text.
/// </summary>
public static class RuntimeInventoryStore
{
    private static RuntimeInventory inventory;

    public static RuntimeInventory Inventory
    {
        get
        {
            if (inventory == null) inventory = Load();
            return inventory;
        }
    }

    private static RuntimeInventory Load()
    {
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, "inventory-runtime.json");
            var inv = JsonUtility.FromJson<RuntimeInventory>(File.ReadAllText(path));
            if (inv != null && inv.rows != null && inv.rows.Length > 0) return inv;
            Debug.LogError("[ControlTooltip] inventory-runtime.json holds no rows (run scripts/inventory-rollup.py)");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[ControlTooltip] inventory-runtime.json unreadable, tooltips have no descriptions: " + ex.Message);
        }
        return new RuntimeInventory();
    }
}

/// <summary>
/// Hover tooltip (U17, R18): label, live value with units, setpoint where one exists, limits and a one-line description
/// for the control or indicator under the cursor. The text comes from ControlTooltipModel (pure, tested); the binder
/// supplies control state it alone knows (pending drags, guard and SCRAM cover). Draws with IMGUI at the cursor; the
/// font follows the text-scale setting.
/// </summary>
public class ControlTooltip : MonoBehaviour
{
    public ControlRoom3DBinder binder;

    private GUIStyle style;
    private int styleSize;

    // Built once per frame (on the Layout event) and reused for Repaint and input events of the same frame.
    private int builtFrame = -1;
    private DiegeticInteractable builtFor;
    private TooltipText builtTooltip;
    private string builtText;
    private float builtWidth, builtHeight;

    private void Awake()
    {
        if (binder == null) binder = GetComponent<ControlRoom3DBinder>();
        if (binder == null) binder = FindAnyObjectByType<ControlRoom3DBinder>();
        if (RuntimeInventoryStore.Inventory == null) return;   // load once, up front
    }

    private void EnsureStyle()
    {
        int size = UserSettings.Scaled(ReactorSim.Ui.Theme.FontCaption);
        if (style != null && styleSize == size) return;
        styleSize = size;
        style = ThemeColors.BoxStyle(ReactorSim.Ui.Theme.SurfaceTooltip, size, ReactorSim.Ui.Theme.TextBody);
        style.richText = false;
        style.padding = new RectOffset(10, 10, 6, 6);
    }

    private void OnGUI()
    {
        if (binder == null || binder.bridge == null || binder.bridge.Sim == null || PauseMenu.IsOpen) return;
        var h = DiegeticInteractable.Hovered;
        if (h == null) return;
        var s = binder.bridge.GetSnapshot();
        if (s == null) return;
        if (builtFrame != Time.frameCount || builtFor != h)
        {
            builtFrame = Time.frameCount;
            builtFor = h;
            builtTooltip = binder.BuildTooltip(h, s, RuntimeInventoryStore.Inventory);
            if (builtTooltip != null)
            {
                EnsureStyle();
                builtText = builtTooltip.ToText();
                builtWidth = Mathf.Min(Screen.width - 8f, 440f * UserSettings.TextScale);
                builtHeight = style.CalcHeight(new GUIContent(builtText), builtWidth);
            }
        }
        if (builtTooltip == null) return;
        EnsureStyle();
        string text = builtText;
        float width = builtWidth;
        float height = builtHeight;
        Vector2 m = Event.current.mousePosition;
        float x = Mathf.Clamp(m.x + 18f, 4f, Mathf.Max(4f, Screen.width - width - 4f));
        float y = Mathf.Clamp(m.y + 18f, 4f, Mathf.Max(4f, Screen.height - height - 4f));
        GUI.Box(new Rect(x, y, width, height), text, style);
    }
}
