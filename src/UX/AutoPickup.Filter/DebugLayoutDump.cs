using System.Diagnostics;
#if DEBUG
using System;
using System.Text;
using MC.Shared;
using UnityEngine;
#endif

namespace MC.UX.AutoPickupFilterMod;

// Debug build only (calls vanish in Release): once per InventoryGui, when the panel is shown and settled, log where
// things sit so button room (T01), parent choice (T27) and slot layout (T13) can be checked from the log alone.
internal static class DebugLayoutDump
{
#if DEBUG
    private static readonly Vector3[] Corners = new Vector3[4];
    private static InventoryGui _dumpedFor;
#endif

    [Conditional("DEBUG")]
    internal static void Run(InventoryGui gui, UnityEngine.RectTransform button, bool worldPlaced)
    {
#if DEBUG
        if (gui == null || ReferenceEquals(_dumpedFor, gui) || gui.m_player == null || FilterUi.PlacementDirty)
        {
            return;
        }
        if (Mathf.Abs(gui.m_player.lossyScale.x) < 0.5f)
        {
            return; // still animating in
        }
        _dumpedFor = gui;
        try
        {
            var sb = new StringBuilder();
            sb.Append("Inventory layout (world rects = screen pixels on an overlay canvas), screen ")
                .Append(Screen.width).Append('x').Append(Screen.height).Append(':');
            Describe(sb, "m_player", gui.m_player);
            for (var i = 0; i < gui.m_player.childCount; i++)
            {
                Describe(sb, "  child", gui.m_player.GetChild(i));
            }
            sb.Append("\n chain from m_player up:");
            for (var t = (Transform)gui.m_player; t != null; t = t.parent)
            {
                sb.Append("\n  '").Append(t.name).Append("' canvasGroup=").Append(t.GetComponent<CanvasGroup>() != null)
                    .Append(" uiGroup=").Append(t.GetComponent<UIGroupHandler>() != null)
                    .Append(" canvas=").Append(t.GetComponent<Canvas>() != null)
                    .Append(ReferenceEquals(t, gui.m_inventoryRoot) ? " (m_inventoryRoot)" : "");
                if (t.GetComponent<Canvas>() != null && t.parent == null)
                {
                    break;
                }
            }
            var groups = gui.m_uiGroups;
            if (groups != null)
            {
                for (var i = 0; i < groups.Length; i++)
                {
                    var g = groups[i];
                    sb.Append("\n uiGroups[").Append(i).Append("] = ").Append(g != null ? g.name : "null")
                        .Append(" canvasGroup=").Append(g != null && g.GetComponent<CanvasGroup>() != null);
                }
            }
            Describe(sb, "m_playerName", gui.m_playerName != null ? gui.m_playerName.transform : null);
            Describe(sb, "m_armor", gui.m_armor != null ? gui.m_armor.transform : null);
            Describe(sb, "m_weight", gui.m_weight != null ? gui.m_weight.transform : null);
            Describe(sb, "m_playerGrid", gui.m_playerGrid != null ? gui.m_playerGrid.transform : null);
            Describe(sb, "m_container", gui.m_container);
            Describe(sb, "m_crafting", gui.m_crafting);
            Describe(sb, "button", button);
            sb.Append("\n button parent: ").Append(worldPlaced ? "m_inventoryRoot (world placement)" : "m_player");
            var elements = gui.m_playerGrid != null ? gui.m_playerGrid.m_elements : null;
            if (elements != null && elements.Count > 0 && elements[0] != null)
            {
                var slot = elements[0].transform;
                sb.Append("\n slot 0 children:");
                Describe(sb, "  slot", slot);
                for (var i = 0; i < slot.childCount; i++)
                {
                    Describe(sb, "  child", slot.GetChild(i));
                }
            }
            Log.Debug(sb.ToString());
        }
        catch (Exception e)
        {
            Log.Debug($"Inventory layout dump failed: {e.Message}");
        }
#endif
    }

#if DEBUG
    private static void Describe(StringBuilder sb, string label, Transform t)
    {
        sb.Append("\n ").Append(label).Append(": ");
        if (t == null)
        {
            sb.Append("null");
            return;
        }
        sb.Append('\'').Append(t.name).Append("' active=").Append(t.gameObject.activeSelf);
        if (t is RectTransform rt)
        {
            rt.GetWorldCorners(Corners);
            sb.Append(" anchors=").Append(rt.anchorMin).Append('-').Append(rt.anchorMax)
                .Append(" pos=").Append(rt.anchoredPosition).Append(" size=").Append(rt.sizeDelta)
                .Append(" world=(").Append(Corners[0].x.ToString("F0")).Append(',').Append(Corners[0].y.ToString("F0"))
                .Append(")-(").Append(Corners[2].x.ToString("F0")).Append(',').Append(Corners[2].y.ToString("F0")).Append(')');
        }
    }
#endif
}
