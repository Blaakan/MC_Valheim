#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewSpyglassMod;

// Debug build only. Me = more multiplayer self tests (tools/Test-Multiplayer.ps1: this game is client of a real
// dedicated server; config files of the run are throwaway, so real settings are switched here). TESTING.md items in
// brackets:
//   spyglass.mp.no-horizons    scenario "modded": Distant Horizons really turned off (its Enabled switch): a raised
//                              x8 spyglass leaves the fog alone, no far land of that mod, no error [T11]
//   spyglass.mp.leave-mod-off  scenario "modded": the server turns the spyglass off, this game logs out to the main
//                              menu: no warning from the spyglass while the world closes, mod active again at the
//                              menu. The server turns it on again by itself [M08, second half]
//   spyglass.mp.leave-no-mod   scenario "vanilla-server" (server without MC mods): the same after leaving a server
//                              that does not have the mod [M08, first half]. Registered in BindConfig: the mod is
//                              never active on such a server
// The two "leave" tests end at the main menu; the probe joins the server again before the next test.
internal static partial class SelfTests
{
    private const string MpNoHorizonsTest = "spyglass.mp.no-horizons";
    private const string MpLeaveOffTest = "spyglass.mp.leave-mod-off";
    private const string MpLeaveNoModTest = "spyglass.mp.leave-no-mod";
    private const string VanillaServerScenario = "vanilla-server";

    private const string StepOffFor = "spyglass.mp.s.off-for";

    // Seconds the server keeps the spyglass off for spyglass.mp.leave-mod-off: enough to see it, log out and stand at
    // the main menu.
    private const float OffSeconds = 30f;

    // ---------- server half ----------

    // "<seconds>": the server owner turns the spyglass off (the real Enabled switch) and on again that many seconds
    // later, by itself: the client that asked is at the main menu by then and cannot ask for it.
    private static IEnumerator ServerOffFor(string arg, object[] reply)
    {
        if (!OnServer(reply))
        {
            yield break;
        }
        var found = FeatureRegistry.Find(ModInfo.Guid);
        if (!float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds < 1f || seconds > 60f
            || !found.HasValue || found.Value.Enabled == null
            || !BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) || !(info.Instance is MonoBehaviour host))
        {
            SelfTest.Answer(reply, false, "bad argument or no Enabled switch on the server: '" + arg + "'");
            yield break;
        }
        var view = found.Value;
        // Timer first, on the plugin object (it lives on while the feature is off): whatever happens next, the
        // spyglass comes back on.
        host.StartCoroutine(OnAgainAfter(view.Enabled, seconds));
        view.Enabled.Value = false;
        var end = Time.realtimeSinceStartup + 5f;
        while (view.State != nameof(ModState.Disabled) && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
        SelfTest.Answer(reply, view.State == nameof(ModState.Disabled),
            $"the spyglass is {view.State} on the server for {F(seconds)} s: {view.Status}");
    }

    private static IEnumerator OnAgainAfter(ConfigEntry<bool> enabled, float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        enabled.Value = true;
        SelfTest.Note(MpLeaveOffTest, "server: the spyglass is turned on again");
    }

    // ---------- leaving a server on which the spyglass is not running ----------

    // This game is client of a server on which the spyglass cannot run (state ServerMissing, status holding `why`):
    // log out to the main menu the way the pause menu's Log out > Yes does, look at this mod's log lines meanwhile
    // and at its state back at the menu.
    private static IEnumerator LeaveAndWatch(Checks c, string why)
    {
        var net = ZNet.instance;
        var game = Game.instance;
        var own = FeatureRegistry.Find(ModInfo.Guid);
        if (!c.Check(SelfTest.IsMultiplayerRun && net != null && !net.IsServer() && game != null && !game.IsShuttingDown() && Player.m_localPlayer != null
                     && _tap != null && own.HasValue,
                "this game is a client in the server's world and its log is listened to"))
        {
            yield break;
        }
        var view = own.Value;
        yield return WaitReal(() => view.State == nameof(ModState.ServerMissing), 10f);
        var status = view.Status;
        var recipe = SpyglassContent.CraftRecipe;
        if (!c.Check(view.State == nameof(ModState.ServerMissing) && status.IndexOf(why, StringComparison.OrdinalIgnoreCase) >= 0 && !Plugin.FeatureActive,
                $"before leaving, the MC Mods panel says: {view.State}, \"{status}\""))
        {
            yield break;
        }
        c.Check(recipe != null && !recipe.m_enabled, "the spyglass recipe is hidden on this game meanwhile");
        var problems = _tap.Problems().Count;
        var errors = _tap.ErrorMark;

        game.Logout();
        var end = Time.realtimeSinceStartup + 60f;
        var seen = -1f;
        while (Time.realtimeSinceStartup < end)
        {
            var atMenu = FejdStartup.instance != null && Player.m_localPlayer == null && Game.instance == null;
            seen = !atMenu ? -1f : seen < 0f ? Time.realtimeSinceStartup : seen;
            if (seen >= 0f && Time.realtimeSinceStartup - seen >= 2f)
            {
                break;
            }
            yield return null;
        }
        if (!c.Check(seen >= 0f && Time.realtimeSinceStartup - seen >= 2f, "logged out: back at the main menu"))
        {
            yield break;
        }
        // Me wait until the mod is on again, and one second more, before me read the log: the false warnings of 0.1.0
        // came from the recipe rebuild of that turn-on. Log read before it would miss a turn-on that comes late.
        yield return WaitReal(() => view.IsActive, 10f);
        var active = view.IsActive;
        yield return Seconds(1f);
        var fresh = ProblemsSince(problems);
        var named = ErrorsNamingMe(errors);
        c.Check(fresh.Count == 0,
            fresh.Count == 0
                ? "no warning or error from the spyglass while the world closed and the mod turned on again (no recipe material, crafting station or 'no valid material' line)"
                : $"{fresh.Count} warning or error line(s) from the spyglass while the world closed and the mod turned on again: {First(fresh)}");
        c.Check(named.Count == 0, named.Count == 0 ? "and no error naming its code" : $"{named.Count} error(s) naming the spyglass's code: {First(named)}");
        c.Check(active && view.IsActive, $"back at the menu the MC Mods panel shows the mod active again ({view.State}: \"{view.Status}\")");
        var left = OnScreen();
        c.Check(left.Length == 0, "nothing of the spyglass on the main menu" + (left.Length == 0 ? "" : ": " + left));
    }

    // Scenario "vanilla-server".
    private static IEnumerator RunMpLeaveNoMod()
    {
        var c = new Checks(MpLeaveNoModTest);
        yield return LeaveAndWatch(c, "does not have this mod");
        c.Report();
    }

    // Scenario "modded".
    private static IEnumerator RunMpLeaveModOff()
    {
        var c = new Checks(MpLeaveOffTest);
        if (!Ready(MpLeaveOffTest, out _, out _))
        {
            yield break;
        }
        var ok = new bool[1];
        yield return MpStart(c, ok);
        if (!ok[0])
        {
            c.Report();
            yield break;
        }
        var reply = new SelfTest.ServerReply();
        var asked = Time.realtimeSinceStartup;
        yield return Server(c, StepOffFor, OffSeconds.ToString(CultureInfo.InvariantCulture), reply, "the server owner turned the spyglass off");
        if (reply.Answered && reply.Ok)
        {
            yield return LeaveAndWatch(c, "turned off");
            c.Check(Time.realtimeSinceStartup - asked < OffSeconds - 3f,
                $"all of it while the server still had the spyglass off ({F(Time.realtimeSinceStartup - asked)} s of {F(OffSeconds)} s)");
        }
        // The server turns it on again by itself: the next test (the probe joins again first) must find it on.
        yield return WaitReal(() => Time.realtimeSinceStartup - asked > OffSeconds + 4f, OffSeconds + 6f);
        c.Report();
    }

    // ---------- spyglass.mp.no-horizons ----------

    private static IEnumerator RunMpNoHorizons()
    {
        var c = new Checks(MpNoHorizonsTest);
        if (!Ready(MpNoHorizonsTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, MpNoHorizonsTest);
        try
        {
            var ok = new bool[1];
            yield return MpStart(c, ok);
            var found = FeatureRegistry.Find(ScopeCamera.DistantHorizonsGuid);
            if (!ok[0] || !c.Check(found.HasValue && found.Value.Enabled != null && found.Value.IsActive,
                    "Distant Horizons is loaded and active before the test (not: nothing of TESTING.md T11 was checked)"))
            {
                c.Report();
                yield break;
            }
            var horizons = found.Value;
            var enabled = horizons.Enabled;
            rig.Noon();
            rig.ClearWeather();
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            Scope.TestHoldToLook = false;
            rig.Look(OpenYaw(player, out _), -1f);
            yield return new WaitForSeconds(1f);
            var landBefore = FarTiles().Count;

            // The other mod's real switch, like its tick box in the MC Mods panel. On again whatever happens.
            rig.OnRestore("Distant Horizons on", () =>
            {
                if (!enabled.Value)
                {
                    enabled.Value = true;
                }
            });
            enabled.Value = false;
            yield return WaitReal(() => horizons.State == nameof(ModState.Disabled), 5f);
            yield return Seconds(1f);
            c.Check(horizons.State == nameof(ModState.Disabled), $"Distant Horizons turned off for real ({horizons.State}: \"{horizons.Status}\")");
            var landOff = FarTiles().Count;
            c.Check(landBefore > 0 && landOff == 0, $"its far land is gone ({landBefore} far tiles drawn before, {landOff} now): beyond the normal view there is only the game's fog");
            var fog = new float[1];
            yield return FogSettled(fog, 15f);
            var before = fog[0];
            var farPlane = cam.m_camera.farClipPlane;
            var problems = _tap != null ? _tap.Problems().Count : 0;
            var errors = _tap != null ? _tap.ErrorMark : 0;

            Scope.TestSetMagnification(8f);
            yield return RaiseUp(c, player, "Distant Horizons off");
            c.Check(!ScopeCamera.DistantHorizonsSeen, "the spyglass finds Distant Horizons off (the shared registry is asked, no stand-in)");
            var touched = false;
            var worst = 0f;
            for (var i = 0; i < 60; i++)
            {
                yield return new WaitForEndOfFrame();
                touched |= ScopeCamera.FogApplied;
                worst = Mathf.Max(worst, Mathf.Abs(RenderSettings.fogDensity - before));
            }
            c.Check(!touched && worst <= before * 0.02f + 0.0000001f,
                $"the fog is left as it is for 60 frames at x8 (density {D(RenderSettings.fogDensity)}, before {D(before)}, largest change {D(worst)})");
            c.Check(Near(ScopeCamera.CurrentMagnification, 8f, 0.01f) && Near(cam.m_camera.farClipPlane, farPlane, 0.5f),
                $"zoom x8 all the same, view distance of the game unchanged (x{F(ScopeCamera.CurrentMagnification)}, far plane {F(cam.m_camera.farClipPlane)} m)");
            c.Check(FarTiles().Count == 0, "no far land appears while looking");
            SelfTest.Screenshot(MpNoHorizonsTest, "x8");
            yield return null;
            yield return null;
            yield return LowerDown();
            var logged = _tap != null ? _tap.ErrorsSince(errors) : new List<string>();
            c.Check(_tap != null && ProblemsSince(problems).Count == 0 && logged.Count == 0,
                "no error in the log and no warning from the spyglass meanwhile" + (logged.Count == 0 ? "" : ": " + First(logged)));

            enabled.Value = true;
            yield return WaitReal(() => horizons.IsActive, 10f);
            c.Check(horizons.IsActive, $"Distant Horizons on again ({horizons.State})");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }
}
#endif
