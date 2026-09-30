using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.SneakAmbushMod.Patches;

// ALWAYS ON. Network prefab list ready (every game and dedicated server): Smoke Screen item, projectile and cloud
// registered (idempotent). Without me games log "Missing prefab hash", host delete clouds, projectiles and dropped
// Smoke Screens near host player as unknown prefabs, and world load warn about saved ones. Dedicated server make no
// world object (reference far outside world), so it no delete them, unless server-side simulation mod make it run
// world. Dedicated server has no main menu: me build the clones here from the ZNetScene's BombSmoke when no database
// built them yet.
[AlwaysOnPatch]
[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
internal static class ZNetScenePatches
{
    [HarmonyPostfix]
    private static void Postfix(ZNetScene __instance)
    {
        try
        {
            SmokeContent.RegisterInZNetScene(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("ZNetScene.Awake postfix", e);
        }
    }
}
