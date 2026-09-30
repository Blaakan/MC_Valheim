using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.CreaturesMoraleMod.Patches;

// Me = a kill credited to the local player (Game.RPC_RegisterKill run on each credited player's own game, local call
// or routed). Postfix: vanilla already counted it in the profile's kill table -> standing published again, progress
// messages (E5). Only while Active. Body catch own errors.
[HarmonyPatch(typeof(Game), nameof(Game.RPC_RegisterKill))]
internal static class GamePatches
{
    private static void Postfix()
    {
        try
        {
            Standing.PublishAfterKill();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Game.RPC_RegisterKill postfix", e);
        }
    }
}
