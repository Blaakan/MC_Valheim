using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Crafting.StationsBatchFeedMod.Patches;

// Each vanilla add say "Added X" in the center: five in one frame = flicker and fade work queued five times.
// ONLY while BatchFeeder loop run (flag set and cleared in the same call), me swallow center messages and keep the
// last one (it tell why a press was refused); one summary replace them. Top-left messages pass.
// Tame message ("... $hud_tamedone") always pass, even inside the loop: never hide it (Creature Kill and Tame
// Counts count tames from Player.Message, and the player must see it).
// Skill level up from 0 is a center message too (Skills.RaiseSkill, first cooking level): me hold it, BatchFeeder
// show it under the summary. Never kept as refuse reason.
// Hook MessageHud, not Player.Message: some mods talk to MessageHud directly.
[HarmonyPatch]
internal static class MessageHudPatches
{
    private const string TameToken = "$hud_tamedone";
    private const string SkillUpToken = "$msg_skillup";

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.ShowMessage))]
    private static bool ShowMessage_Prefix(MessageHud.MessageType type, string text)
    {
        // Cheap check first: this run for every message in the game.
        if (!BatchFeeder.Muting || type != MessageHud.MessageType.Center)
        {
            return true;
        }
        try
        {
            if (text != null && text.IndexOf(TameToken, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
            if (text != null && text.IndexOf(SkillUpToken, StringComparison.Ordinal) >= 0)
            {
                BatchFeeder.HeldSkillUp = text;
                return false;
            }
            BatchFeeder.LastMuted = text;
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("MessageHud.ShowMessage prefix", e);
            return true;
        }
    }
}
