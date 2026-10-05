using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Farming.CultivatorReplantMod.Patches;

// Cultivator tooltip (E3): "Tier: Black metal cultivator" and "Replants: ..." for its level; in the crafting panel
// (item = recipe prefab data, qualityLevel = level after the upgrade) also "New: ..." for levels 4-7. Static
// overload, all 6 argument types (instance GetTooltip(int) call it). Not on an appended tooltip (another item's text
// glued on). Rules pending = tiers vanilla, no line. Transplant items: nothing here, their description say it all.
[HarmonyPatch]
internal static class ItemTooltipPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
        typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool))]
    private static void GetTooltip_Postfix(ItemDrop.ItemData item, int qualityLevel, bool crafting, bool appending,
        ref string __result)
    {
        // Crafting panel call this every frame for the picked recipe: name check first, lines cached.
        if (appending || __result == null || !CultivatorTiers.IsCultivator(item))
        {
            return;
        }
        try
        {
            if (ServerRules.Current.IsPending)
            {
                return;
            }
            // qualityLevel = item's own quality outside crafting (vanilla pass m_quality), target level in crafting.
            var lines = CultivatorTiers.TooltipLines(qualityLevel, crafting);
            __result = Insert(__result, qualityLevel, crafting, lines);
        }
        catch (Exception e)
        {
            PatchGuard.Report("ItemDrop.ItemData.GetTooltip postfix", e);
        }
    }

    // Right after the vanilla "Quality: N" line (built like vanilla), so tier sit next to level. Crafting (no quality
    // line) or line not found (other mod changed it) = at the end.
    private static string Insert(string tooltip, int qualityLevel, bool crafting, string lines)
    {
        if (crafting)
        {
            return tooltip + lines;
        }
        var line = "\n$item_quality: <color=orange>" + qualityLevel + "</color>";
        var at = tooltip.IndexOf(line, StringComparison.Ordinal);
        if (at < 0)
        {
            return tooltip + lines;
        }
        var end = at + line.Length;
        return tooltip.Substring(0, end) + lines + tooltip.Substring(end);
    }
}
