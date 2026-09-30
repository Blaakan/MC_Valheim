using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Crafting.ForgeIdolUpgradesMod.Patches;

// Crafting panel. Everything here act only at the Forge of Potential (upgrader station) or on our idol recipes;
// other stations stay vanilla (first check in every patch).
[HarmonyPatch]
internal static class InventoryGuiPatches
{
    // Window open or close: back to the vanilla Upgrade tab, idol level pick forgotten.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    private static void Show_Prefix()
    {
        IdolsTab.Mode = false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    private static void Hide_Postfix(InventoryGui __instance)
    {
        try
        {
            IdolsTab.Leave(__instance);
            IdolChoice.Clear();
            ForgePanel.Forget();
        }
        catch (Exception e)
        {
            PatchGuard.Report("InventoryGui.Hide postfix", e);
        }
    }

    // Vanilla tab buttons leave the Idols tab.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnTabUpgradePressed))]
    private static void OnTabUpgradePressed_Prefix()
    {
        IdolsTab.Mode = false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnTabCraftPressed))]
    private static void OnTabCraftPressed_Prefix()
    {
        IdolsTab.Mode = false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateCraftingPanel))]
    private static void UpdateCraftingPanel_Postfix(InventoryGui __instance)
    {
        try
        {
            IdolsTab.AfterPanelUpdate(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("InventoryGui.UpdateCraftingPanel postfix", e);
        }
    }

    // Idols tab: our rows instead of the vanilla list. Other mods' postfixes (Crafting Search and Sort) still run.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipeList))]
    private static bool UpdateRecipeList_Prefix(InventoryGui __instance)
    {
        try
        {
            var player = Player.m_localPlayer;
            if (!IdolsTab.Mode || !IdolsTab.AtForge(player))
            {
                return true;
            }
            IdolsTab.BuildRows(__instance, player);
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("InventoryGui.UpdateRecipeList prefix", e);
            IdolsTab.Mode = false;
            return true;
        }
    }

    // Every frame: our texts, slots and button over the vanilla ones (Idols tab, or refinement at the Forge).
    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipe))]
    private static void UpdateRecipe_Postfix(InventoryGui __instance, Player player)
    {
        if (player == null || __instance.m_selectedRecipe.Recipe == null)
        {
            return;
        }
        try
        {
            if (IdolUpgrade.IsOurs(__instance.m_selectedRecipe.Recipe))
            {
                IdolsTab.AfterUpdateRecipe(__instance, player);
            }
            else if (IdolsTab.AtForge(player))
            {
                ForgePanel.AfterUpdateRecipe(__instance, player);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("InventoryGui.UpdateRecipe postfix", e);
        }
    }

    // Requirement panel at the Forge: show the idol the item level need (IdolTierRule), with vanilla's own count and
    // red blink. Our slot text (ForgePanel) come after, in the UpdateRecipe postfix.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirementList))]
    private static void SetupRequirementList_Prefix(InventoryGui __instance, int quality, Player player, out bool __state)
    {
        __state = false;
        try
        {
            __state = IdolSwap.Begin(player, __instance.m_selectedRecipe.Recipe, quality);
        }
        catch (Exception e)
        {
            IdolSwap.End();
            __state = false;
            PatchGuard.Report("InventoryGui.SetupRequirementList prefix", e);
        }
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirementList))]
    private static void SetupRequirementList_Finalizer(bool __state)
    {
        if (__state)
        {
            IdolSwap.End();
        }
    }

    // Button press. Idol row: our checks. Refinement: vanilla checks minus the free-slot rule (it kept room for the
    // refund of a broken item; with OnFailure = LoseLevels items never break).
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnCraftPressed))]
    private static bool OnCraftPressed_Prefix(InventoryGui __instance)
    {
        try
        {
            var player = Player.m_localPlayer;
            var selected = __instance.m_selectedRecipe;
            if (player == null || selected.Recipe == null || !IdolsTab.AtForge(player))
            {
                return true;
            }
            if (IdolUpgrade.IsOurs(selected.Recipe))
            {
                IdolsTab.Press(__instance, player);
                return false;
            }
            // OnFailure = Destroy: vanilla press, with its free-slot rule (room for the materials a broken item gives).
            if (selected.ItemData != null && ServerRules.Current.Failure != FailureMode.Destroy
                && ForgeRefine.TryPlan(player.GetInventory(), selected.Recipe, selected.ItemData.m_quality + 1, out var plan)
                && (plan.Level >= 0 || IdolUpgrade.Free()))
            {
                ForgeUi.StartTimer(__instance, player);
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            PatchGuard.Report("InventoryGui.OnCraftPressed prefix", e);
            return true;
        }
    }

    // Timer done. Decided by what the press snapshot (m_craftRecipe), not by the tab: player can switch tabs while
    // the 8-12 s timer run. On any error: never fall to vanilla (vanilla remove the item/idol stack first).
    // Idol upgrade first of all prefixes: it is our own recipe, nobody else should touch it.
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    private static bool DoCraftingIdol_Prefix(InventoryGui __instance, Player player)
    {
        if (!IdolUpgrade.IsOurs(__instance.m_craftRecipe) || player == null)
        {
            return true;
        }
        try
        {
            IdolsTab.Craft(__instance, player);
        }
        catch (Exception e)
        {
            PatchGuard.Report("InventoryGui.DoCrafting prefix (idol upgrade)", e);
        }
        finally
        {
            // HarmonyX still run the other prefixes: a Forge mod taking over refinement (ReforgedPotential) would
            // remove m_craftUpgradeItem (our idol stack). Empty snapshot = nothing for it to touch.
            __instance.m_craftUpgradeItem = null;
            __instance.m_craftRecipe = null;
            // Vanilla UpdateRecipe set it right after anyway; early = no re-run if a later patch throw on the null.
            __instance.m_craftTimer = -1f;
        }
        return false;
    }

    // Refinement last: when another Forge mod already took the roll over (its prefix skipped vanilla), me step
    // aside, so one refinement never roll twice. HarmonyX still run every prefix, so me check __runOriginal.
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    private static bool DoCraftingRefine_Prefix(InventoryGui __instance, Player player, bool __runOriginal)
    {
        if (!__runOriginal || __instance.m_craftRecipe == null || __instance.m_craftUpgradeItem == null || player == null)
        {
            return __runOriginal;
        }
        var station = player.GetCurrentCraftingStation();
        if (station == null || !station.m_upgrader)
        {
            return true;
        }
        try
        {
            return !ForgeRefine.Run(__instance, player);
        }
        catch (Exception e)
        {
            PatchGuard.Report("InventoryGui.DoCrafting prefix (refinement)", e);
            return false;
        }
    }
}
