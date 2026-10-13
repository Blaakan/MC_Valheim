#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.MusicInstrumentsMod;

// Debug build only. More tests of a multiplayer run (tools/Test-Multiplayer.ps1; same class as SelfTests.cs):
//   music.mp.vanilla.leave  M20  scenario vanilla-server (the server does not have the mod): the mod says so, then a
//                                real log out to the main menu: no warning of the mod while the world closes, active
//                                again at the menu. Registered from BindConfig: the mod is inactive in that scenario.
//   music.mp.leave.off      M20  scenario modded: the server owner turns the mod off (the server turns it on again by
//                                itself half a minute later), the same log out: no warning, active again at the menu
// Both leave the server; the probe joins again before the next test.
internal static partial class SelfTests
{
    private const string MpVanillaLeaveName = "music.mp.vanilla.leave";
    private const string MpLeaveOffName = "music.mp.leave.off";
    private const string SrvOffFor = "music.mp.srv.off-for";
    private const string VanillaServerScenario = "vanilla-server";

    // The warnings TESTING.md M20 names: the recipe checks must not speak while the world closes.
    private static readonly string[] LeaveWarnings =
    {
        "is not an item in this game",
        "is not a crafting station in this game",
        "has no valid material",
    };

    // ---------- server half ----------

    // Me = small object on the server that turns the mod on again when its time comes (the step that made me is over
    // by then, and the mod's own code is off).
    private sealed class BackOn : MonoBehaviour
    {
        internal ConfigEntry<bool> Entry;
        internal float At;

        private void Update()
        {
            if (Time.realtimeSinceStartup < At)
            {
                return;
            }
            try
            {
                if (Entry != null)
                {
                    Entry.Value = true;
                }
            }
            finally
            {
                Destroy(gameObject);
            }
        }
    }

    // Server owner turns the mod off (its Enabled setting: this run's config is a throwaway) for "<seconds>", then it
    // comes back on by itself. Answers at once.
    private static IEnumerator ServerOffFor(string arg, object[] reply)
    {
        yield return null;
        var view = FeatureRegistry.Find(ModInfo.Guid);
        var enabled = view != null ? view.Value.Enabled : null;
        if (ZNet.instance == null || !ZNet.instance.IsServer() || enabled == null
            || !float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds < 5f || seconds > 60f)
        {
            SelfTest.Answer(reply, false, $"not a server, no Enabled setting, or a bad time '{arg}' (5 to 60 seconds)");
            yield break;
        }
        var go = new GameObject("MC_MusicSelfTest_BackOn");
        Object.DontDestroyOnLoad(go);
        var back = go.AddComponent<BackOn>();
        back.Entry = enabled;
        back.At = Time.realtimeSinceStartup + seconds;
        SelfTest.Answer(reply, true, $"turned off for {arg} s");
        enabled.Value = false;
    }

    // ---------- client side ----------

    private static string OwnState()
    {
        var view = FeatureRegistry.Find(ModInfo.Guid);
        return view != null ? view.Value.State : "?";
    }

    private static string OwnStatus()
    {
        var view = FeatureRegistry.Find(ModInfo.Guid);
        return view != null ? view.Value.Status : "?";
    }

    // The menu's Log out > Yes (Menu.Logout calls this), then the main menu: no warning or error of the mod since the
    // log out began, and the mod active again there.
    private static IEnumerator LeaveAndCheck(Checks c, string where)
    {
        var game = Game.instance;
        if (!c.Check(game != null && !game.m_shuttingDown && Player.m_localPlayer != null, $"in the world of {where} before the log out"))
        {
            yield break;
        }
        var mark = LogTap.ProblemMark;
        game.Logout();
        var end = Time.realtimeSinceStartup + 70f;
        var seenAt = -1f;
        while (Time.realtimeSinceStartup < end)
        {
            var ready = FejdStartup.instance != null && Player.m_localPlayer == null;
            seenAt = !ready ? -1f : seenAt < 0f ? Time.realtimeSinceStartup : seenAt;
            if (seenAt >= 0f && Time.realtimeSinceStartup - seenAt >= 2f)
            {
                break;
            }
            yield return null;
        }
        if (!c.Check(FejdStartup.instance != null && Player.m_localPlayer == null, $"back at the main menu after leaving {where}"))
        {
            yield break;
        }
        var bad = new List<string>();
        foreach (var line in LogTap.ProblemsSince(mark))
        {
            if (!LogTap.Expected(line))
            {
                bad.Add(line.Length > 200 ? line.Substring(0, 200) + "..." : line);
            }
        }
        for (var i = 0; i < bad.Count && i < 5; i++)
        {
            c.Note("while the world closed: " + bad[i]);
        }
        c.Check(bad.Count == 0, $"no warning or error from Music Instruments while the world of {where} closed ({bad.Count}{(bad.Count > 0 ? ", first: " + bad[0] : "")})");
        // The mod write each of these warnings only once per game (InstrumentContent.WarnOnce). Another test of this
        // run that logged out before me would have used them up, and me would see a clean log out for nothing. So me
        // count them since the game started: no multiplayer test asks for one (lines of the tests themselves left out).
        var early = new List<string>();
        foreach (var line in LogTap.ProblemsSince(0))
        {
            if (!LogTap.Expected(line))
            {
                early.Add(line);
            }
        }
        foreach (var text in LeaveWarnings)
        {
            var count = 0;
            var first = "";
            foreach (var line in early)
            {
                if (line.IndexOf(text, StringComparison.Ordinal) >= 0)
                {
                    count++;
                    first = first.Length > 0 ? first : line.Length > 200 ? line.Substring(0, 200) + "..." : line;
                }
            }
            c.Check(count == 0, $"no \"... {text} ...\" warning since the game started, this log out included ({count}{(count > 0 ? ", first: " + first : "")})");
        }
        yield return WaitReal(() => OwnState() == nameof(ModState.Active), 5f);
        c.Check(OwnState() == nameof(ModState.Active) && Plugin.FeatureActive, $"back at the menu the mod is active again (state {OwnState()}, status '{OwnStatus()}')");
        c.Check(Performance.Mode == PerformanceMode.None && EmitterCount() == 0 && !SongWindow.Built && !MiniGameHud.Built, "and nothing of it is left on screen or sounding");
    }

    // ---------- music.mp.vanilla.leave (M20, scenario vanilla-server) ----------

    private static IEnumerator RunMpVanillaLeave()
    {
        var c = new Checks(MpVanillaLeaveName);
        if (!IsClient || Player.m_localPlayer == null)
        {
            SelfTest.Fail(MpVanillaLeaveName, "not a player on a server");
            yield break;
        }
        LogTap.Install();
        c.Check(OwnState() == nameof(ModState.ServerMissing) && OwnStatus().StartsWith("Inactive: the server does not have this mod", StringComparison.Ordinal)
                && !Plugin.FeatureActive,
            $"on a server without Music Instruments the mod says so (state {OwnState()}, status '{OwnStatus()}')");
        yield return LeaveAndCheck(c, "a server without the mod");
        c.Report();
    }

    // ---------- music.mp.leave.off (M20, scenario modded) ----------

    private static IEnumerator RunMpLeaveOff()
    {
        var c = new Checks(MpLeaveOffName);
        if (!IsClient || Player.m_localPlayer == null)
        {
            SelfTest.Fail(MpLeaveOffName, "not a player on a server");
            yield break;
        }
        LogTap.Install();
        const float offSeconds = 35f;
        c.Check(OwnState() == nameof(ModState.Active), $"active on the server with the mod (state {OwnState()})");
        var reply = new SelfTest.ServerReply();
        var asked = Time.realtimeSinceStartup;
        yield return SelfTest.CallServer(SrvOffFor, offSeconds.ToString(CultureInfo.InvariantCulture), reply);
        if (!c.Check(reply.Answered && reply.Ok, "the server owner turns the mod off: " + reply))
        {
            c.Report();
            yield break;
        }
        yield return WaitReal(() => OwnState() == nameof(ModState.ServerMissing), 12f);
        var turnedOff = c.Check(OwnState() == nameof(ModState.ServerMissing) && OwnStatus().StartsWith("Inactive: the server has this mod turned off", StringComparison.Ordinal)
                                && !Plugin.FeatureActive,
            $"the mod goes inactive on this game (state {OwnState()}, status '{OwnStatus()}')");
        if (turnedOff)
        {
            yield return LeaveAndCheck(c, "a server that has the mod turned off");
        }
        // The server turns the mod on again by itself: me wait that out, so the next test joins a server with it.
        var left = offSeconds + 4f - (Time.realtimeSinceStartup - asked);
        if (left > 0f)
        {
            yield return Real(left);
        }
        c.Report();
    }
}
#endif
