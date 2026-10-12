using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.UX.CheatedTagRemoverMod;

// Me = one command run on the game that hold the world (dedicated server, host, single player). Steps of a clear:
//   1. no save running (save copy the tables on this thread, then write them on another: me wait);
//   2. count what is tagged now ("before"), and remember which objects (for step 6);
//   3. ask every player with me to clean its own side (ServerLink.Local), clean own memory copies (host, single
//      player), wait a moment so their writes land first;
//   4. write the whole world (WorldSweep);
//   5. check: count again. Players connected = wait first, and write again what came back (a game without me that
//      own a moving creature or a running station keep sending its old copy), a few rounds;
//   6. mark every object of step 2 for saving (what a player's game cleaned come in unmarked), answer with before
//      minus after, say what is left and why, save with the game's own autosave calls (they also make every player's
//      game save its character = cleaned carried items reach their files).
// Every scan and write is one frame, never across a yield: nothing here hold a ZDO or a table over frames (ids only).
internal static class SweepJob
{
    // Seconds (real time): wait for a running save, for the players' own pass, between checks.
    internal const float SaveWait = 60f;
    internal const float HelpWait = 2f;
    internal const float CheckWait = 3f;
    internal const int MaxRounds = 3;

    private static Coroutine _routine;
    private static bool _running;

    internal static bool Running => _running;

#if DEBUG
    // Self test read these after a run (last finished job on this game).
    internal static SweepCounts LastScan { get; private set; }
    internal static SweepCounts LastBefore { get; private set; }
    internal static SweepCounts LastRemoved { get; private set; }
    internal static SweepCounts LastAfter { get; private set; }
    internal static SweepCounts LastWrote { get; private set; }
    internal static int LastRounds { get; private set; }
    internal static int LastMarked { get; private set; }
    internal static bool LastMarkedPortal { get; private set; }
    internal static int Finished { get; private set; }
#endif

    // caller = uid of the admin's connection, 0 = this game's own console. False = already busy or cannot start.
    internal static bool Start(bool write, long caller)
    {
        var plugin = Plugin.Instance;
        if (_running || plugin == null)
        {
            return false;
        }
        _running = true;
        // No wait needed (single player, no save running) = all done inside this call already.
        var routine = plugin.StartCoroutine(Guarded(Steps(write, caller), caller));
        _routine = _running ? routine : null;
        return true;
    }

    // Feature off: stop where it is. Unity drop the coroutine without running its finally.
    internal static void Stop()
    {
        var plugin = Plugin.Instance;
        if (_routine != null && plugin != null)
        {
            plugin.StopCoroutine(_routine);
        }
        _routine = null;
        _running = false;
    }

    // Still a world this game hold, and not on its way out (logout keep the stopped ZNet object for a moment, with
    // empty tables: a scan there would say "nothing found" and a clear "all removed").
    internal static bool Here(ZNet net)
    {
        var game = Game.instance;
        return net != null && ReferenceEquals(net, ZNet.instance) && net.enabled && !net.HaveStopped && net.IsServer()
               && ZDOMan.instance != null && game != null && !game.IsShuttingDown();
    }

    // Steps throw = say so, end clean. (No yield inside a try with catch: me step the inner one by hand.)
    private static IEnumerator Guarded(IEnumerator steps, long caller)
    {
        try
        {
            while (true)
            {
                object current;
                try
                {
                    if (!steps.MoveNext())
                    {
                        break;
                    }
                    current = steps.Current;
                }
                catch (Exception e)
                {
                    PatchGuard.Report("SweepJob", e);
                    Log.Error($"Cheated tag pass stopped by an error: {e}");
                    Tell(caller, ModInfo.Name + " stopped on an error. Nothing more was changed; see BepInEx/LogOutput.log.");
                    break;
                }
                yield return current;
            }
        }
        finally
        {
            _routine = null;
            _running = false;
        }
    }

    private static IEnumerator Steps(bool write, long caller)
    {
        var net = ZNet.instance;
        if (!Here(net))
        {
            yield break;
        }
        // Somebody else's request (admin on a hosted game): the host's carried items are the host's business, not
        // part of that admin's answer.
        var remote = caller != 0L;

        var waited = 0f;
        var told = false;
        while (net.IsSaving())
        {
            if (waited >= SaveWait)
            {
                Tell(caller, "The world is still being saved. Try again in a moment.");
                yield break;
            }
            if (!told)
            {
                told = true;
                Tell(caller, "Waiting for the world save to finish...");
            }
            yield return null;
            waited += Time.unscaledDeltaTime;
            if (!Here(net))
            {
                yield break;
            }
        }

        var seen = write ? new HashSet<ZDOID>() : null;
        var before = new SweepCounts();
        WorldSweep.Run(SweepScope.World, false, before, seen);
        var own = new SweepCounts();
        LiveSweep.Carried(write, own);
        if (!write)
        {
            Log.Info($"Counted cheated tags in {before.Milliseconds} ms ({ZDOMan.instance.NrOfObjects()} objects in the world).");
            if (remote)
            {
                if (own.CarriedItems > 0)
                {
                    Output.Say("On your game: " + Report.List(own) + ".");
                }
            }
            else
            {
                before.CarriedItems = own.CarriedItems;
            }
            Tell(caller, Report.Scan(before, true));
#if DEBUG
            LastScan = before;
            Finished++;
#endif
            yield break;
        }

        var playersWith = ServerLink.AskPlayers(out var playersWithout);
        LiveSweep.Loaded();
        if (remote && own.CarriedItems > 0)
        {
            ServerLink.TellOwnSide(own);
        }
        if (Player.m_localPlayer == null && !net.IsDedicated())
        {
            // Host between two lives (dead, still loading): its items come with its character.
            ServerLink.CleanCarriedWhenSpawned(net);
        }
        if (playersWith > 0)
        {
            yield return new WaitForSecondsRealtime(HelpWait);
            if (!Here(net))
            {
                yield break;
            }
        }

        var wrote = new SweepCounts();
        var after = new SweepCounts();
        var rounds = 0;
        while (true)
        {
            // A save may have started while me waited: writes shift tables the save thread read.
            waited = 0f;
            while (net.IsSaving() && waited < SaveWait)
            {
                yield return null;
                waited += Time.unscaledDeltaTime;
                if (!Here(net))
                {
                    yield break;
                }
            }
            if (net.IsSaving())
            {
                if (rounds == 0)
                {
                    // Nothing written yet: "after" would be empty and the answer a lie.
                    Tell(caller, "The world is still being saved. Try again in a moment.");
                    yield break;
                }
                break;
            }
            rounds++;
            WorldSweep.Run(SweepScope.World, true, wrote, seen);

            // Nobody else here = nobody can write an old copy back: check at once.
            if (net.GetPeerConnections() > 0)
            {
                yield return new WaitForSecondsRealtime(CheckWait);
                if (!Here(net))
                {
                    yield break;
                }
            }
            after = new SweepCounts();
            WorldSweep.Run(SweepScope.World, false, after, seen);
            if (after.World == 0 || net.GetPeerConnections() == 0 || rounds >= MaxRounds)
            {
                break;
            }
        }

        var removed = Report.Removed(before, after, remote ? 0 : own.CarriedItems);
        Log.Info($"Removed cheated tags: {Report.List(removed)}. {rounds} pass(es), {wrote.Milliseconds + before.Milliseconds + after.Milliseconds} ms "
                 + $"of scanning and writing, {ZDOMan.instance.NrOfObjects()} objects in the world.");
        // A save that started while me waited for the check must end first: marks made during a save go to a second
        // list, and a save started right behind the running one still read the first list = marks lost unsaved.
        waited = 0f;
        while (net.IsSaving() && waited < SaveWait)
        {
            yield return null;
            waited += Time.unscaledDeltaTime;
            if (!Here(net))
            {
                yield break;
            }
        }
        var marked = WorldSweep.MarkForSave(seen, out var markedPortal);
        // Still saving after the wait: marks stay (moved to the main list when that save end) and reach the save
        // after it; no save of me own, and me no say "saving".
        var saving = !net.IsSaving() && rounds > 0 && (seen.Count > 0 || own.CarriedItems > 0 || playersWith > 0) && Save(net);
        Tell(caller, Report.Clear(removed, after, playersWith, playersWithout, saving));
#if DEBUG
        LastBefore = before;
        LastRemoved = removed;
        LastAfter = after;
        LastWrote = wrote;
        LastRounds = rounds;
        LastMarked = marked;
        LastMarkedPortal = markedPortal;
        Finished++;
#endif
    }

    // Same calls as the game's own autosave (Game.UpdateSaving): own character, then the world, and every player's
    // game is told to save its character (cleaned carried items reach their files). Not ZNet.SaveWorldAndPlayerProfiles
    // (the "save" command): on a dedicated server it throw when a save ran in the last minute (ZNet.HardSaveBlock
    // read a platform object the server does not have). Seen in uncheat.mp.sweep, game 1.0.17.
    private static bool Save(ZNet net)
    {
        try
        {
            var game = Game.instance;
            if (game == null || !net.EnoughDiskSpaceAvailable(out _))
            {
                return false;
            }
            game.SavePlayerProfile(setLogoutPoint: true);
            net.Save(sync: false, saveOtherPlayerProfiles: true, waitForNextFrame: true);
            return true;
        }
        catch (Exception e)
        {
            PatchGuard.Report("SweepJob.Save", e);
            return false;
        }
    }

    private static void Tell(long caller, string text)
    {
        if (caller == 0L)
        {
            Output.Say(text);
            return;
        }
        Log.Info(text.Replace('\n', ' '));
        ServerLink.Reply(caller, text);
    }
}
