using System;
using HarmonyLib;
using MC.Shared;

namespace MC.UX.AutoPickupFilterMod.Patches;

// Scythe call Pickable.Interact straight from Attack (not through Player.Interact): me remember that spot too, so
// scythe harvest stay "by hand" for the filter. Use key on a pickable record twice (here + Player.Interact): harmless.
// Never change the call. Framework only patch this while feature Active.
[HarmonyPatch]
internal static class PickablePatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact), new[] { typeof(Humanoid), typeof(bool), typeof(bool) })]
    private static void Interact_Prefix(Pickable __instance, Humanoid character)
    {
        if (character == null || !ReferenceEquals(character, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            HarvestGrace.Record(__instance.transform.position);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Pickable.Interact prefix", e);
        }
    }
}
