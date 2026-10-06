using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod.Patches;

// Me = key capture while the song window or the mini-game run (KeyCapture). Vanilla gates (movement, mouse look,
// hotbar, use, inventory, map, camera zoom...) ask Chat.HasFocus: me say yes while we hold the keys, so the lane keys
// (D F J K, or any key the player picks) never walk, equip or open anything. Esc still opens the menu (Menu.Update
// does not ask). Compendium and Crafting Search and Sort OR their own flags the same way: all coexist.
[HarmonyPatch]
internal static class ChatPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chat), nameof(Chat.HasFocus))]
    private static void HasFocus_Postfix(ref bool __result)
    {
        if (!__result && KeyCapture.Active)
        {
            __result = true;
        }
    }
}

// Me = console key binds (bind j say hi) fire from Chat.Update through TryRunCommand with skipAllowedCheck = true:
// skipped while we hold the keys, so a lane key never runs a bound command. Typed commands untouched.
[HarmonyPatch]
internal static class TerminalPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.TryRunCommand))]
    private static bool TryRunCommand_Prefix(bool skipAllowedCheck)
    {
        try
        {
            return !(skipAllowedCheck && KeyCapture.Active);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Terminal.TryRunCommand prefix", e);
            return true;
        }
    }
}

// Me = Enter in the song window (play) never also opens the chat: Chat.Update read its "Chat" button without asking
// HasFocus, and script order with our window is not fixed. Prefix: the press is forgotten before chat looks.
[HarmonyPatch]
internal static class ChatUpdatePatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chat), nameof(Chat.Update))]
    private static void Update_Prefix()
    {
        if (!Performance.WindowOpen && Performance.Mode != PerformanceMode.MiniGame)
        {
            return;
        }
        try
        {
            ZInput.ResetButtonStatus("Chat");
        }
        catch (Exception e)
        {
            PatchGuard.Report("Chat.Update prefix", e);
        }
    }
}

// Me = Esc (or the gamepad menu button) closes the song window without opening the pause menu on the same press.
// Prefix: runs before Menu.Update reads the key; hidden-frames counter back to 0 = vanilla waits (needs > 1).
// Me hold back the Console key (F5) during the rhythm game: Console.Update not ask Chat.HasFocus, so a lane key on it
// would open the console (and the open console end the run).
[HarmonyPatch]
internal static class ConsolePatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Console), nameof(Console.Update))]
    private static void Update_Prefix()
    {
        if (Performance.Mode != PerformanceMode.MiniGame)
        {
            return;
        }
        try
        {
            ZInput.ResetButtonStatus("Console");
        }
        catch (Exception e)
        {
            PatchGuard.Report("Console.Update prefix", e);
        }
    }
}

[HarmonyPatch]
internal static class MenuPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Menu), nameof(Menu.Update))]
    private static void Update_Prefix(Menu __instance)
    {
        if (!Performance.WindowOpen)
        {
            return;
        }
        try
        {
            if (__instance.m_root != null && __instance.m_root.gameObject.activeSelf)
            {
                return;
            }
            if (ZInput.GetKeyDown(KeyCode.Escape, false) || ZInput.GetButtonDown("JoyMenu") || ZInput.GetButtonDown("JoyButtonB"))
            {
                Performance.CloseWindow();
                __instance.m_hiddenFrames = 0;
                ZInput.ResetButtonStatus("JoyButtonB");
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Menu.Update prefix", e);
        }
    }
}

// Me = cursor free while the song window is open (like vanilla does for the radial menu); vanilla locks it again the
// frame after we close.
[HarmonyPatch]
internal static class MouseCapturePatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    private static void UpdateMouseCapture_Postfix()
    {
        if (!Performance.WindowOpen)
        {
            return;
        }
        try
        {
            ZCursor.LockState = ZInput.IsMouseActive() ? CursorLockMode.None : CursorLockMode.Locked;
            ZCursor.Show();
        }
        catch (Exception e)
        {
            PatchGuard.Report("GameCamera.UpdateMouseCapture postfix", e);
        }
    }
}

// Me = world camera gone (logout, disconnect, quit: the game scene unloads). Our canvases outlive scenes: drop the
// performance, every emitter, the window and the HUD now.
[HarmonyPatch(typeof(GameCamera), nameof(GameCamera.OnDestroy))]
internal static class GameCameraDestroyPatches
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            Performance.Shutdown();
        }
        catch (Exception e)
        {
            PatchGuard.Report("GameCamera.OnDestroy postfix (performance)", e);
        }
        try
        {
            Listeners.Shutdown();
        }
        catch (Exception e)
        {
            PatchGuard.Report("GameCamera.OnDestroy postfix (listeners)", e);
        }
        try
        {
            SongShare.Reset();
            SongShare.ServerReset();
        }
        catch (Exception e)
        {
            PatchGuard.Report("GameCamera.OnDestroy postfix (server songs)", e);
        }
    }
}

// Me = game music fades while music is heard (personal GameMusicVolume): MusicMan moves its volume toward this
// target with its own fade, so no jumps.
[HarmonyPatch]
internal static class MusicVolumePatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(MusicVolume), nameof(MusicVolume.UpdateProximityVolumes))]
    private static void UpdateProximityVolumes_Postfix(ref float __result)
    {
        var duck = Listeners.Duck;
        if (duck < 0.999f)
        {
            __result *= duck;
        }
    }
}
