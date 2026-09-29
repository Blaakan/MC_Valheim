using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.CompendiumEncyclopediaMod.Patches;

// Own records follow the local player (design 3.4):
//   AddKnownBiome postfix  -> record the biome flag (language independent; vanilla keep the localized name);
//   ResetCharacter postfix -> resetcharacter forget vanilla biomes, so our Seen and Biomes go too.
// resetknownitems (ResetCharacterKnownItems) keep both: they are not items.
// Framework only patch these while feature Active. Every body catch own errors: never throw into game.
[HarmonyPatch]
internal static class PlayerPatches
{
    // Once a second, only when the sector change. Reference compare first (other players' copies skipped).
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.AddKnownBiome))]
    private static void AddKnownBiome_Postfix(Player __instance, BiomeSector biome)
    {
        try
        {
            if (biome == null || !ReferenceEquals(__instance, Player.m_localPlayer))
            {
                return;
            }
            OwnRecords.RecordBiome(biome.Biome);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.AddKnownBiome postfix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.ResetCharacter))]
    private static void ResetCharacter_Postfix(Player __instance)
    {
        try
        {
            if (ReferenceEquals(__instance, Player.m_localPlayer))
            {
                OwnRecords.ResetFor(__instance);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.ResetCharacter postfix", e);
        }
    }
}
