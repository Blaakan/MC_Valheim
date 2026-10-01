using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.TrinketsOnDemandMod.Patches;

// Me = tooltip line naming the trigger, right after the vanilla "$item_fulladrenaline: <effect>" line, on every item
// with a full-adrenaline effect (vanilla trinkets and modded ones). Static overload, all 6 argument types (instance
// GetTooltip(int) call it). Not on an appended tooltip (another item's text glued on). Pending = vanilla, no line.
[HarmonyPatch]
internal static class ItemDataPatches
{
    private const string VanillaHead = "\n$item_fulladrenaline: <color=orange>";

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
        typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool))]
    private static void GetTooltip_Postfix(ItemDrop.ItemData item, bool appending, ref string __result)
    {
        if (appending || item == null || item.m_shared == null || item.m_shared.m_fullAdrenalineSE == null
            || __result == null)
        {
            return;
        }
        try
        {
            if (ServerRules.Current.IsPending)
            {
                return;
            }
            __result = Insert(__result, item.m_shared.m_fullAdrenalineSE, Feedback.TooltipLine());
        }
        catch (Exception e)
        {
            PatchGuard.Report("ItemDrop.ItemData.GetTooltip postfix", e);
        }
    }

    // After the vanilla block (built the same way as vanilla: head + effect text + "</color>"); not found (other mod
    // changed it) = at the end.
    internal static string Insert(string tooltip, StatusEffect effect, string line)
    {
        var block = VanillaHead + effect.GetTooltipString() + "</color>";
        var at = tooltip.IndexOf(block, StringComparison.Ordinal);
        if (at < 0)
        {
            return tooltip + line;
        }
        var end = at + block.Length;
        return tooltip.Substring(0, end) + line + tooltip.Substring(end);
    }
}
