using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.SailingSkillMod.Patches;

// ALWAYS ON (whole session, feature on or off): Sailing is a real skill for vanilla. Without these, Skills.Load drop the
// saved level of a character loaded while the mod is off, and the next save lose it. Framework apply all always-on
// classes together (all or nothing), so me keep them small and safe: constants only, no config, no feature state.
//   IsSkillValid postfix   Load keep the Sailing entry (vanilla = Enum.IsDefined, false for our number).
//   Awake postfix          def in m_skills of every Skills (main menu preview, player), icon.
//   GetSkillDef postfix    fallback when some code rebuilt m_skills: entry never get m_info null (Save, panel NRE).
//   SetupLanguage postfix  words back after every language load (language change wipe all words).
//   InitTerminal postfix   Tab of raiseskill / resetskill know "Sailing" (once per process).
// Console cheats in own classes below (one __state type per class).
[AlwaysOnPatch]
[HarmonyPatch]
internal static class SkillRegistrationPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Skills), nameof(Skills.IsSkillValid))]
    private static void IsSkillValid_Postfix(Skills.SkillType type, ref bool __result)
    {
        try
        {
            if (type == SailingSkill.Type)
            {
                __result = true;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Skills.IsSkillValid postfix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Skills), nameof(Skills.Awake))]
    private static void Awake_Postfix(Skills __instance)
    {
        try
        {
            SailingSkill.Register(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Skills.Awake postfix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Skills), nameof(Skills.GetSkillDef))]
    private static void GetSkillDef_Postfix(Skills.SkillType type, ref Skills.SkillDef __result)
    {
        try
        {
            if (__result == null && type == SailingSkill.Type)
            {
                __result = SailingSkill.Def;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Skills.GetSkillDef postfix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
    private static void SetupLanguage_Postfix(Localization __instance)
    {
        try
        {
            SailingSkill.AddWords(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Localization.SetupLanguage postfix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
    private static void InitTerminal_Postfix()
    {
        try
        {
            SailingSkill.WrapCommands();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Terminal.InitTerminal postfix", e);
        }
    }
}

// ALWAYS ON. raiseskill sailing N / raiseskill all N. Vanilla only know enum names ("Skill not found sailing") and
// "all" loop enum values only. Another mod's prefix already handled the call (__runOriginal false, e.g. another
// Sailing skill mod) = me do nothing. __state = vanilla still going to run when me looked (for "all" postfix).
[AlwaysOnPatch]
[HarmonyPatch(typeof(Skills), nameof(Skills.CheatRaiseSkill))]
internal static class CheatRaiseSkillPatches
{
    [HarmonyPrefix]
    private static bool Prefix(Skills __instance, string name, float value, bool showMessage, bool __runOriginal,
        out bool __state)
    {
        __state = __runOriginal;
        try
        {
            if (!__runOriginal || !SailingSkill.IsCheatName(name))
            {
                return true;
            }
            SailingSkill.CheatRaise(__instance, value, showMessage);
            __state = false;
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Skills.CheatRaiseSkill prefix", e);
            return true;
        }
    }

    // "all": vanilla raised every enum skill (silently each); ours too, silent (vanilla print one "All skills" line).
    [HarmonyPostfix]
    private static void Postfix(Skills __instance, string name, float value, bool __state)
    {
        try
        {
            if (__state && SailingSkill.IsAll(name))
            {
                SailingSkill.CheatRaise(__instance, value, showMessage: false);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Skills.CheatRaiseSkill postfix", e);
        }
    }
}

// ALWAYS ON. resetskill sailing / resetskill all, same way as raise.
[AlwaysOnPatch]
[HarmonyPatch(typeof(Skills), nameof(Skills.CheatResetSkill))]
internal static class CheatResetSkillPatches
{
    [HarmonyPrefix]
    private static bool Prefix(Skills __instance, string name, bool __runOriginal, out bool __state)
    {
        __state = __runOriginal;
        try
        {
            if (!__runOriginal || !SailingSkill.IsCheatName(name))
            {
                return true;
            }
            SailingSkill.CheatReset(__instance, print: true);
            __state = false;
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Skills.CheatResetSkill prefix", e);
            return true;
        }
    }

    [HarmonyPostfix]
    private static void Postfix(Skills __instance, string name, bool __state)
    {
        try
        {
            if (__state && SailingSkill.IsAll(name))
            {
                SailingSkill.CheatReset(__instance, print: false);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Skills.CheatResetSkill postfix", e);
        }
    }
}
