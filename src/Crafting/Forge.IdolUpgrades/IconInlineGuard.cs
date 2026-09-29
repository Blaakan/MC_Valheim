using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// ItemData.GetIcon is tiny (19 byte IL): Mono JIT may copy it inline into callers (inventory grid, hotbar), and
// then a patch made later never reach them. A Harmony patch mark the method "no inline" for the whole game session
// (MonoMod Pin, never undone by unpatch). Me patch it once with an empty postfix at plugin load, before any UI code
// compile, and take it off at once: turning the mod on later in the session still reach every caller.
internal static class IconInlineGuard
{
    internal static void Apply()
    {
        try
        {
            var harmony = new Harmony(ModInfo.Guid + ".noinline");
            harmony.Patch(AccessTools.Method(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetIcon)),
                postfix: new HarmonyMethod(typeof(IconInlineGuard), nameof(Nothing)));
            harmony.UnpatchSelf();
        }
        catch (Exception e)
        {
            Log.Debug($"Could not pre-patch GetIcon (stars may need a restart after turning the mod on): {e.Message}");
        }
    }

    private static void Nothing()
    {
    }
}
