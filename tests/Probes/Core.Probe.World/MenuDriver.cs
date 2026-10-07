#if DEBUG
using System;
using System.Collections;
using MC.Shared;
using UnityEngine;

namespace MC.Core.ProbeWorldMod;

// Main menu part of run. Me press same FejdStartup buttons a player would (same methods the UI buttons call):
//   Start game (OnStartGame) -> new character "MCProbe" if missing (OnCharacterNew + OnNewCharacterDone, local file)
//   -> Start (OnCharacterStart) -> new world "MCProbe" with fixed seed if missing (OnWorldNew + OnNewWorldDone,
//   local file) -> select it (OnSelectWorld) -> "Start server" and "Public" off, crossplay off -> OnWorldStart.
// OnWorldStart = ZNet.SetServer(server, not open, not public, world) + TransitionToMainScene, like the Start button.
// Throw = step failed (SafeRunner catch it, driver report FAIL probe.setup).
internal static class MenuDriver
{
    private const string T = ProbeDriver.SetupTest;

    internal static IEnumerator StartWorld(ProbeTimings timings)
    {
        // Menu ready = FejdStartup.Start ran (profile list loaded) and platform + Steam matchmaking up.
        var seenAt = -1f;
        while (true)
        {
            var f = FejdStartup.instance;
            var ready = f != null && f.m_profiles != null && PlatformInitializer.PlatformInitialized
                        && ZSteamMatchmaking.instance != null;
            if (!ready)
            {
                seenAt = -1f;
            }
            else if (seenAt < 0f)
            {
                seenAt = Time.realtimeSinceStartup;
            }
            else if (Time.realtimeSinceStartup - seenAt >= 2f)
            {
                break;
            }
            yield return null;
        }
        timings.MenuReady = Time.realtimeSinceStartup;
        SelfTest.Note(T, $"main menu ready {timings.MenuReady - timings.Start:F1} s after plugin load");

        // Proof first: no world start unless real saves are out of reach.
        var error = SaveIsolation.Verify(out var report);
        SelfTest.Note(T, report);
        if (error != null)
        {
            throw new InvalidOperationException("save isolation not proven: " + error + ". World NOT started.");
        }
        SelfTest.Pass(T, "save data isolated in the run folder, cloud saves off");

        ClearMenuBlockers();
        var fejd = FejdStartup.instance;
        PrefsGuard.Snapshot();

        // "Start game" button.
        fejd.OnStartGame();
        yield return null;

        var file = ProbeSettings.CharacterName.ToLowerInvariant();
        if (!PlayerProfile.HaveProfile(file))
        {
            if (!fejd.m_newCharacterPanel.activeInHierarchy)
            {
                fejd.OnCharacterNew();
                yield return null;
            }
            fejd.m_csNewCharacterName.text = ProbeSettings.CharacterName;
            fejd.OnNewCharacterDone(forceLocal: true);
            if (fejd.m_newCharacterError.activeInHierarchy || !PlayerProfile.HaveProfile(file))
            {
                throw new InvalidOperationException($"could not create character '{ProbeSettings.CharacterName}'");
            }
            SkipIntroOnFirstSpawn(file);
            SelfTest.Note(T, $"created character '{ProbeSettings.CharacterName}' (local file)");
            yield return null;
        }
        else
        {
            SelfTest.Note(T, $"reusing character '{ProbeSettings.CharacterName}'");
        }

        fejd.SetSelectedProfile(file);
        var profile = fejd.m_profileIndex >= 0 && fejd.m_profileIndex < fejd.m_profiles.Count ? fejd.m_profiles[fejd.m_profileIndex] : null;
        if (profile == null || profile.GetFilename() != file)
        {
            throw new InvalidOperationException($"character '{file}' not selectable in the character list");
        }
        SelfTest.Note(T, $"character file '{profile.GetPath()}' ({profile.m_fileSource})");

        if (ProbeSettings.IsMultiplayer)
        {
            JoinServer(fejd, timings);
            yield break;
        }

        // "Start" on character screen: go to world list (and "new world" panel when list empty).
        fejd.OnCharacterStart();
        yield return null;

        if (!World.HaveWorld(ProbeSettings.WorldName))
        {
            if (!fejd.m_createWorldPanel.activeInHierarchy)
            {
                fejd.OnWorldNew();
                yield return null;
            }
            fejd.m_newWorldName.text = ProbeSettings.WorldName;
            fejd.m_newWorldSeed.text = ProbeSettings.WorldSeed;
            fejd.OnNewWorldDone(forceLocal: true);
            if (!World.HaveWorld(ProbeSettings.WorldName))
            {
                throw new InvalidOperationException($"could not create world '{ProbeSettings.WorldName}'");
            }
            SelfTest.Note(T, $"created world '{ProbeSettings.WorldName}' seed '{ProbeSettings.WorldSeed}' (local file)");
            yield return null;
        }
        else
        {
            SelfTest.Note(T, $"reusing world '{ProbeSettings.WorldName}'");
        }

        var index = fejd.m_worlds.FindIndex(w => w.m_name == ProbeSettings.WorldName);
        if (index < 0)
        {
            throw new InvalidOperationException($"world '{ProbeSettings.WorldName}' missing from the world list");
        }
        fejd.OnSelectWorld(index);
        var world = fejd.m_world;
        if (world == null || world.m_name != ProbeSettings.WorldName || world.m_dataError != World.SaveDataError.None
            || world.m_fileSource != FileHelpers.FileSource.Local)
        {
            throw new InvalidOperationException($"world '{ProbeSettings.WorldName}' not usable (error {world?.m_dataError}, source {world?.m_fileSource})");
        }
        SelfTest.Note(T, $"world file '{world.GetSaveFWLPath()}' seed '{world.m_seedName}'");

        // Local only: no open server, not public, Steam backend (no PlayFab needed). Without notify: the open-server
        // toggle handler would write the PlayFab auto-login pref.
        fejd.m_openServerToggle.SetIsOnWithoutNotify(false);
        fejd.m_publicServerToggle.SetIsOnWithoutNotify(false);
        fejd.m_crossplayServerToggle.SetIsOnWithoutNotify(false);
        fejd.m_serverPassword.text = "";
        yield return null;
        if (!fejd.CanStartServer())
        {
            throw new InvalidOperationException("the world Start button would be disabled (FejdStartup.CanStartServer is false)");
        }

        timings.WorldRequested = Time.realtimeSinceStartup;
        SelfTest.Note("probe", "world start requested");
        fejd.OnWorldStart();
        var restored = PrefsGuard.Restore();
        SelfTest.Note(T, $"menu prefs (last character/world, crossplay) put back: {restored} changed");
        if (!fejd.m_startingWorld)
        {
            var why = fejd.m_cloudStorageWarningNextSave.activeInHierarchy ? " (cloud storage warning shown)" : "";
            throw new InvalidOperationException("OnWorldStart did not start the world" + why);
        }
    }

    // Multiplayer test after a refusal: back at main menu, close the error panel, select probe character, join again.
    internal static IEnumerator Rejoin(ProbeTimings timings, float timeout = 60f)
    {
        var end = Time.realtimeSinceStartup + timeout;
        var seenAt = -1f;
        while (true)
        {
            var f = FejdStartup.instance;
            var ready = f != null && f.m_profiles != null && ZSteamMatchmaking.instance != null;
            seenAt = !ready ? -1f : seenAt < 0f ? Time.realtimeSinceStartup : seenAt;
            if (seenAt >= 0f && Time.realtimeSinceStartup - seenAt >= 1f)
            {
                break;
            }
            if (Time.realtimeSinceStartup > end)
            {
                throw new TimeoutException($"main menu not ready within {timeout:F0} s for a rejoin");
            }
            yield return null;
        }
        ClearMenuBlockers();
        var fejd = FejdStartup.instance;
        if (fejd.m_connectionFailedPanel != null && fejd.m_connectionFailedPanel.activeSelf)
        {
            fejd.m_connectionFailedPanel.SetActive(false);
        }
        PrefsGuard.Snapshot();
        var file = ProbeSettings.CharacterName.ToLowerInvariant();
        fejd.SetSelectedProfile(file);
        JoinServer(fejd, timings);
    }

    // Multiplayer run: join dedicated server like "-joinserverwithcharacter" (selected character + FejdStartup.JoinServer,
    // which does TransitionToMainScene). Password given before: ZNet.RPC_ClientHandshake enter it by itself.
    private static void JoinServer(FejdStartup fejd, ProbeTimings timings)
    {
        FejdStartup.ServerPassword = ProbeSettings.Password;
        fejd.m_joinServer = new ServerJoinData(new ServerJoinDataDedicated(ProbeSettings.Join));
        if (!fejd.m_joinServer.IsValid)
        {
            throw new InvalidOperationException($"server address '{ProbeSettings.Join}' not understood");
        }
        timings.WorldRequested = Time.realtimeSinceStartup;
        SelfTest.Note("probe", $"joining server {ProbeSettings.Join} (scenario '{ProbeSettings.Scenario}')");
        fejd.JoinServer();
        var restored = PrefsGuard.Restore();
        SelfTest.Note(T, $"menu prefs (last character/world, crossplay) put back: {restored} changed");
    }

    // Popups (news, warnings) and a startup cinematic only block a player's clicks, not our calls. Me still close
    // them so screen and state are the normal menu.
    private static void ClearMenuBlockers()
    {
        for (var i = 0; i < 10 && UnifiedPopup.IsVisible(); i++)
        {
            SelfTest.Note(T, "closing a menu popup");
            UnifiedPopup.Pop();
        }
        if (CinematicsManager.s_instance != null && CinematicsManager.IsStartedPlaying())
        {
            SelfTest.Note(T, "stopping the startup cinematic");
            CinematicsManager.Stop();
        }
    }

    // Fresh profile = intro (Valkyrie ride, story text) on first spawn: me mark it done, player spawn at start stones.
    private static void SkipIntroOnFirstSpawn(string file)
    {
        var p = new PlayerProfile(file, FileHelpers.FileSource.Local);
        if (p.Load() && p.m_firstSpawn)
        {
            p.m_firstSpawn = false;
            p.Save();
        }
    }
}
#endif
