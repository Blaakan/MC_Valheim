using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.CompendiumEncyclopediaMod.Patches;

// "Met" = vanilla really showed the creature's name plate to this player (design 1.11, 3.4). EnemyHud.LateUpdate ->
// ShowHud make a plate (HudData) for every character within m_maxShowDistance (10 in code, 30 in the 1.0.16 prefab) of
// the local player's position, seen or not (no sight or camera test), so plate made != plate seen.
// UpdateHuds (same LateUpdate) then switch each plate on or off: bosses when alerted (100 m), mounts, and other
// creatures only for 60 s after the crosshair was on them (m_hoverTimer reset by Player.GetHoverCreature: first
// collider the camera ray hit, so a wall, tree or rock in between = not hovered) and while in front of the camera.
// Me read that result: after UpdateHuds, every plate whose m_gui is active = met. Same rule as vanilla, whatever the
// prefab numbers are. Run even with HUD hidden (Ctrl+F3 only hide m_hudRoot, plates keep their state).
// Cost per frame: one loop over the plates near the player (a few), activeSelf each; record work only for shown
// plates (one set lookup when already met). Framework only patch this while feature Active. Body catch own errors.
[HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.UpdateHuds))]
internal static class EnemyHudPatches
{
    private static void Postfix(EnemyHud __instance)
    {
        try
        {
            RecordShownPlates(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("EnemyHud.UpdateHuds postfix", e);
        }
    }

    /// <summary>Mark as met every non-player character whose plate is shown now.</summary>
    private static void RecordShownPlates(EnemyHud hud)
    {
        // No camera = UpdateHuds stopped before its loop: new plates still "on" from ShowHud, never judged. Skip.
        if (hud == null || hud.m_huds.Count == 0 || Player.m_localPlayer == null || Utils.GetMainCamera() == null)
        {
            return;
        }
        foreach (var kv in hud.m_huds)
        {
            var data = kv.Value;
            var c = data.m_character;
            if (data.m_gui == null || c == null || !data.m_gui.activeSelf || c.IsPlayer())
            {
                continue;
            }
            OwnRecords.MarkSeen(c.m_name);
        }
    }
}
