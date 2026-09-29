using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.CompendiumEncyclopediaMod.Patches;

// Inventory screen (design 3.9):
//   Awake  postfix = make the opt-in side button (one per InventoryGui = world session; setting off = nothing);
//   Show   postfix = place it again, fix its pad group, start the once-per-InventoryGui self-check (setting off = nothing);
//   Update prefix  = Esc / B close only our window (m_shownFrames reset; vanilla Update keep running, other mods
//                    transpile it); idle cost one bool;
//   Hide   postfix = close our window (Tab, E, Y, death, teleport); idle cost one bool (run every frame while dead);
//   OnOpenTexts / OnOpenSkills / OnOpenTrophies / OnOpenAchievements postfix = one side dialog at a time (while our
//                    modal window is open only our Texts tab or another mod's code reach these); OnOpenTexts also
//                    reopens the remembered top tab (TopTabs).
// Framework only patch these while feature Active. Every body catch own errors: never throw into game.
[HarmonyPatch]
internal static class InventoryGuiPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
    private static void Awake_Postfix(InventoryGui __instance)
    {
        try
        {
            SideButton.Create(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("InventoryGui.Awake postfix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show), new[] { typeof(Container), typeof(int) })]
    private static void Show_Postfix(InventoryGui __instance)
    {
        try
        {
            SideButton.OnShow(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("InventoryGui.Show postfix", e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
    private static void Update_Prefix(InventoryGui __instance)
    {
        if (!CompendiumWindow.IsOpen)
        {
            return;
        }
        try
        {
            CompendiumWindow.HandleCloseKeys(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("InventoryGui.Update prefix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    private static void Hide_Postfix()
    {
        if (!CompendiumWindow.IsOpen)
        {
            return;
        }
        try
        {
            CompendiumWindow.Close(selectButton: false);
        }
        catch (Exception e)
        {
            PatchGuard.Report("InventoryGui.Hide postfix", e);
        }
    }

    // Raven button (or our Texts tab): our window closes like for any vanilla dialog, then the remembered top tab: last
    // pick Encyclopedia = our window opens at once in its place.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnOpenTexts))]
    private static void OnOpenTexts_Postfix()
    {
        CloseForVanillaDialog("OnOpenTexts");
        try
        {
            TopTabs.OnRavenOpened();
        }
        catch (Exception e)
        {
            PatchGuard.Report("InventoryGui.OnOpenTexts postfix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnOpenSkills))]
    private static void OnOpenSkills_Postfix() => CloseForVanillaDialog("OnOpenSkills");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnOpenTrophies))]
    private static void OnOpenTrophies_Postfix() => CloseForVanillaDialog("OnOpenTrophies");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnOpenAchievements))]
    private static void OnOpenAchievements_Postfix() => CloseForVanillaDialog("OnOpenAchievements");

    private static void CloseForVanillaDialog(string site)
    {
        if (!CompendiumWindow.IsOpen)
        {
            return;
        }
        try
        {
            CompendiumWindow.Close(selectButton: false);
        }
        catch (Exception e)
        {
            PatchGuard.Report($"InventoryGui.{site} postfix", e);
        }
    }
}
