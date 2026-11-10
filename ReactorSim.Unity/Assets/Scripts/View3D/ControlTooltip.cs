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
/// supplies control state it alone knows (pending drags, guard and SCRAM cover). Drawn as a uGUI + TextMeshPro box that follows
/// the cursor; its size follows the text-scale setting through the type ramp.
/// </summary>
public class ControlTooltip : MonoBehaviour
{
    public ControlRoom3DBinder binder;

    /// <summary>Capture runs only: draw the tooltip at this position (pixels from the top-left) instead of at the (absent) cursor.</summary>
    public static Vector2? AnchorOverride;

    private const float BoxWidth = 460f;      // reference pixels
    private const float CursorGap = 18f;      // screen pixels
    private const float EdgeGap = 4f;

    private Canvas canvas;
    private RectTransform box;
    private TMPro.TMP_Text label;
    private DiegeticInteractable shownFor;
    private string shownText;

    private void Awake()
    {
        if (binder == null) binder = GetComponent<ControlRoom3DBinder>();
        if (binder == null) binder = FindAnyObjectByType<ControlRoom3DBinder>();
        if (RuntimeInventoryStore.Inventory == null) return;   // load once, up front
    }

    private void Build()
    {
        canvas = RuntimeUi.CreateCanvas("TooltipCanvas", 20, transform);
        canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>().enabled = false;   // never takes a click
        box = RuntimeUi.MakeToast(canvas.transform, "Tooltip", new Vector2(0f, 1f), Vector2.zero, BoxWidth,
            ReactorSim.Ui.TextRole.Caption, TextAnchor.UpperLeft, ThemeColors.TextBody, out label);
        box.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        TooltipText tip = null;
        var h = DiegeticInteractable.Hovered;
        if (binder != null && binder.bridge != null && binder.bridge.Sim != null && !PauseMenu.IsOpen && h != null)
        {
            var s = binder.bridge.GetSnapshot();
            if (s != null) tip = binder.BuildTooltip(h, s, RuntimeInventoryStore.Inventory);
        }
        if (tip == null)
        {
            if (box != null && box.gameObject.activeSelf) box.gameObject.SetActive(false);
            return;
        }
        if (box == null) Build();

        string text = tip.ToText();
        if (shownText != text || shownFor != h)
        {
            shownText = text;
            shownFor = h;
            label.text = text;
        }
        if (!box.gameObject.activeSelf) box.gameObject.SetActive(true);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(box);

        // Cursor in screen pixels from the bottom-left; the box hangs below and right of it and stays on screen.
        Vector2 m = AnchorOverride.HasValue ? new Vector2(AnchorOverride.Value.x, Screen.height - AnchorOverride.Value.y) : (Vector2)Input.mousePosition;
        float k = canvas.scaleFactor;
        float w = box.rect.width * k, hgt = box.rect.height * k;
        float x, top;
        ReactorSim.Ui.TooltipClamp.Place(m.x, m.y, w, hgt, Screen.width, Screen.height, CursorGap, EdgeGap, out x, out top);
        box.anchoredPosition = new Vector2(x / k, (top - Screen.height) / k);
    }
}
