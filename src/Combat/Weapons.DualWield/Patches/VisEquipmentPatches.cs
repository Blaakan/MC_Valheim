using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.WeaponsDualWieldMod.Patches;

// Me = cosmetics every game with the mod draw for every player (design 2.7).
//   SetWeaponTrails   postfix: off-hand swing trail (E1). Twice per swing per player; cheap exits in LeftTrails
//                     (players only, no graphics = out, cached type check and trail array).
//   SetBackEquipped   postfix: sheathed pair placed by weapon kind (BackCross): two back weapons crossed in an X on
//                     the back, two knives one per hip, knife + back weapon left where vanilla put each. Every frame
//                     per player: three bool reads; place only the frame vanilla rebuilt the back items, or every 15
//                     frames while a back pair left flat (body lying on the rebuild frame) wait for a retry. While
//                     some player has a crossed pair: a look in a tiny list, and for that player a few vector sums to
//                     keep its X level (two transform writes only when the joint leaned a quarter degree or more).
//                     Knives on the hips ride the hips bone: nothing per frame.
[HarmonyPatch(typeof(VisEquipment))]
internal static class VisEquipmentPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(VisEquipment.SetWeaponTrails))]
    private static void SetWeaponTrails_Postfix(VisEquipment __instance, bool enabled)
    {
        try
        {
            LeftTrails.Apply(__instance, enabled);
        }
        catch (Exception e)
        {
            PatchGuard.Report("VisEquipment.SetWeaponTrails postfix", e);
        }
    }

    // __result true = back instances just made again by vanilla.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(VisEquipment.SetBackEquipped))]
    private static void SetBackEquipped_Postfix(VisEquipment __instance, bool __result)
    {
        if (!__result && !BackCross.RetryPending && !BackCross.LevelPending)
        {
            return;
        }
        try
        {
            if (__result)
            {
                BackCross.Apply(__instance);
            }
            else
            {
                if (BackCross.RetryPending)
                {
                    BackCross.Retry(__instance);
                }
                if (BackCross.LevelPending)
                {
                    BackCross.Level(__instance);
                }
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("VisEquipment.SetBackEquipped postfix", e);
        }
    }
}
