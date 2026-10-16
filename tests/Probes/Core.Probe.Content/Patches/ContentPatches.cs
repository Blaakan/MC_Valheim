#if DEBUG
using HarmonyLib;
using MC.Shared;

namespace MC.Core.ProbeContentMod.Patches;

// Me = the hooks of the real libraries, same kind (prefix/postfix) and same Harmony priority (ContentRegistry say
// which library each copy). Always on: a real content mod keep its content whatever MC mods' toggles say (also in the
// second game of Test-InWorld, where the script turn every MC mod off). Idle when no test run.
[AlwaysOnPatch]
[HarmonyPatch]
internal static class ContentPatches
{
    // blaxxun ItemManager: PrefabManager.Patch_ObjectDBInit [HarmonyPriority(700)] as Awake prefix.
    [HarmonyPrefix]
    [HarmonyPriority(700)]
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    private static void ObjectDBAwakePrefix(ObjectDB __instance)
    {
        if (ContentRegistry.Start())
        {
            ContentRegistry.DbAwakePrefix(__instance);
        }
    }

    // blaxxun ItemManager: same method as CopyOtherDB postfix, then UpdateRegisters.
    [HarmonyPostfix]
    [HarmonyPriority(700)]
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    private static void ObjectDBCopyPostfix(ObjectDB __instance)
    {
        if (ContentRegistry.Start())
        {
            ContentRegistry.DbCopyPostfix(__instance);
        }
    }

    // Jotunn PieceManager.RegisterCustomData: Awake postfix priority 200.
    [HarmonyPostfix]
    [HarmonyPriority(200)]
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    private static void ObjectDBPiecePostfix(ObjectDB __instance)
    {
        if (ContentRegistry.Start())
        {
            ContentRegistry.DbPiecePostfix(__instance);
        }
    }

    // Jotunn ItemManager.InvokeOnItemsRegistered: Awake postfix priority 0 (mods edit item data there).
    [HarmonyPostfix]
    [HarmonyPriority(0)]
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    private static void ObjectDBLatePostfix(ObjectDB __instance)
    {
        if (ContentRegistry.Start())
        {
            ContentRegistry.DbLatePostfix(__instance);
        }
    }

    // Jotunn InvokeOnItemsRegisteredFejd: CopyOtherDB postfix priority 0.
    [HarmonyPostfix]
    [HarmonyPriority(0)]
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    private static void ObjectDBCopyLatePostfix(ObjectDB __instance)
    {
        if (ContentRegistry.Start())
        {
            ContentRegistry.DbLatePostfix(__instance);
        }
    }

    // Jotunn CreatureManager: spawn list put in each SpawnSystem in its Awake prefix.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.Awake))]
    private static void SpawnSystemAwakePrefix(SpawnSystem __instance)
    {
        if (ContentRegistry.Start())
        {
            ContentRegistry.SpawnPrefix(__instance);
        }
    }

    // blaxxun PrefabManager.Patch_ZNetSceneAwake: ZNetScene.Awake prefix.
    [HarmonyPrefix]
    [HarmonyPriority(700)]
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
    private static void ZNetSceneAwakePrefix(ZNetScene __instance)
    {
        if (ContentRegistry.Start())
        {
            ContentRegistry.ScenePrefix(__instance);
        }
    }
}
#endif
