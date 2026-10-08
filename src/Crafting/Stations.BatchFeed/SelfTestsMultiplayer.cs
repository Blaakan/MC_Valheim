#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Crafting.StationsBatchFeedMod;

// Multiplayer self tests (tools/Test-Multiplayer.ps1, scenario modded): this game is the ONE client on a real
// dedicated server. The mod is client-only, so the server never load it: no server step possible.
// A dedicated server can NOT play the friend who own a station. Its build put its reference place at
// (1000000, 0, 1000000) in every Game.FixedUpdate (read in the server's own assembly_valheim.dll; the client build
// has no such line). So it build no object of the world, and ZDOMan.ReleaseNearbyZDOS (server, every 2 s) give a
// station handed to it straight back to the player near it (two test runs, player 230 m from the world centre and
// 11 m from it: every station back with this game 3.5 s later). Add rpcs sent to it meanwhile find no Smelter there
// (ZRoutedRpc.HandleRoutedRPC: no instance) and are gone. With one client no second game simulate a station, so the
// round trip of a non-owner add (friend's counter rise to its maximum and never above, bars and cooked food come
// out, ballista drop its missiles) can not be played here: it need a second player, or a server probe step that
// hold the server's reference place on the player and take the station.
// What stay:
//   .owner            owner road on a real server, and what the server get of it
//   .nonowner-smelter non-owner road while this game's copy say "the server own it" (same frame as the hand-over):
//                     the mod's real client check "owner still here?", its counts, its refusals, the add rpcs it
//                     let through. Then the station is back with this game: owner road again, old counts gone
//   .ownergone, .toggle, .settings, .log
// Every other non-owner case run in single player with a made-up owner (SelfTestsOwnership.cs).
// Never write a ZDO this game does not own: the write would go to the server and overwrite the owner's values.
internal static partial class SelfTests
{
    private const string MpOwnerName = "batchfeed.mp.owner";
    private const string MpSmelterName = "batchfeed.mp.nonowner-smelter";
    private const string MpOwnerGoneName = "batchfeed.mp.ownergone";
    private const string MpToggleName = "batchfeed.mp.toggle";
    private const string MpSettingsName = "batchfeed.mp.settings";
    private const string MpLogName = "batchfeed.mp.log";

    private static KeyValuePair<string, Func<IEnumerator>>[] MultiplayerTests() => new[]
    {
        Test(MpOwnerName, RunMpOwner),
        Test(MpSmelterName, RunMpSmelter),
        Test(MpOwnerGoneName, RunMpOwnerGone),
        Test(MpToggleName, RunMpToggle),
        Test(MpSettingsName, RunMpSettings),
        Test(MpLogName, () => RunLog(MpLogName)),
    };

    private static long ServerUid()
    {
        var net = ZNet.instance;
        var peer = net != null && !net.IsServer() ? net.GetServerPeer() : null;
        return peer != null ? peer.m_uid : 0L;
    }

    private static bool OnServer(Checks c) =>
        c.Check(ServerUid() != 0L, "this game is a client joined to a server (multiplayer run)");

    // Data revision of this ZDO the server hold for sure: what this game's ZDOMan noted for the server peer when it
    // put the ZDO in a package to it (ZDOMan.SendZDOs) or got it from it. -1 = server never had it.
    private static long SentToServer(ZDO zdo)
    {
        var man = ZDOMan.instance;
        var server = ServerUid();
        if (man == null || zdo == null || server == 0L)
        {
            return -1L;
        }
        foreach (var peer in man.m_peers)
        {
            if (peer.m_peer != null && peer.m_peer.m_uid == server && peer.m_zdos.TryGetValue(zdo.m_uid, out var info))
            {
                return info.m_dataRevision;
            }
        }
        return -1L;
    }

    // Add rpcs the watched net view sent to its owner (Hooks.WatchRpcs).
    private static int AddOreRpcs()
    {
        var count = 0;
        foreach (var rpc in Hooks.Rpcs)
        {
            count += rpc == "RPC_AddOre" ? 1 : 0;
        }
        return count;
    }

    private static string OwnerWord(long owner) =>
        owner == ZDOMan.GetSessionID() ? "this game" : owner != 0L && owner == ServerUid() ? "the server" : owner.ToString();

    // ---------- batchfeed.mp.owner (M01, data part of M05) ----------

    private static IEnumerator RunMpOwner()
    {
        var c = new Checks(MpOwnerName);
        Rig rig = null;
        LogTap tap = null;
        try
        {
            if (!OnServer(c))
            {
                c.Report();
                yield break;
            }
            rig = new Rig();
            tap = new LogTap();
            HudReady(c);
            var oreToken = Token("CopperOre");
            var go = rig.Spawn("smelter", -4f, 7f);
            var pit = rig.Spawn("fire_pit", 4f, 7f);
            var smelter = go != null ? go.GetComponentInChildren<Smelter>() : null;
            var fire = pit != null ? pit.GetComponent<Fireplace>() : null;
            if (!c.Check(smelter != null && smelter.m_addOreSwitch != null && smelter.m_maxOre == 10 && oreToken != null
                         && fire != null && FeedTarget.FireSkip(fire) == null && fire.m_maxFuel >= 9f, "spawn a vanilla smelter and a campfire"))
            {
                c.Report();
                yield break;
            }
            // Let the server learn about them.
            yield return new WaitForSecondsRealtime(1.5f);
            var wood = fire.m_fuelItem.m_itemData.m_shared.m_name;
            var ore = smelter.m_addOreSwitch.gameObject;
            c.Check(smelter.m_nview.IsOwner() && fire.m_nview.IsOwner(), "M01 this game owns the stations it built (it arrived first)");
            c.Check(rig.Give("CopperOre", 20) && rig.Give(fire.m_fuelItem.name, 20), "give 20 CopperOre and 20 fuel items");
            SetFuel(fire, 2f);

            var mark = tap.Mark();
            var p = Press(ore, alt: true);
            c.Check(p.Removed == 5 && smelter.GetQueueSize() == 5 && p.Messages == 1 && p.Center == Loc($"$msg_added 5 {oreToken} (5/10)"),
                $"M01 smelter you own on a server: batch works as in single player (queue {smelter.GetQueueSize()}, {Show(p)})");
            ExpectLog(c, "M01 smelter", tap.Batch(mark), "owner=you", "added=5");
            p = Press(pit, alt: true);
            c.Check(p.Removed == 5 && Near(Fuel(fire), 7f) && p.Messages == 1 && p.Center == Loc(Words("$msg_fireadding", "5 " + wood) + " (7/" + (int)fire.m_maxFuel + ")"),
                $"M01 campfire you own on a server: batch works as in single player (fuel {Fuel(fire)}, {Show(p)})");

            // What every other game get of the batch (the friend's counters, the friend's smelting): the station's
            // data. Must be what five vanilla adds write, nothing of the mod in it.
            var zdo = smelter.m_nview.GetZDO();
            var fireZdo = fire.m_nview.GetZDO();
            var allOre = true;
            for (var i = 0; i < 5; i++)
            {
                allOre &= zdo.GetString("item" + i) == "CopperOre";
            }
            c.Check(zdo.GetInt(ZDOVars.s_queued) == 5 && allOre && zdo.GetString("item5") == "",
                $"M01 M05 the smelter's data after the batch is what five vanilla adds write: queued 5, item0..item4 = CopperOre, nothing more (queued {zdo.GetInt(ZDOVars.s_queued)}, all CopperOre {allOre}, item5 '{zdo.GetString("item5")}')");

            // "The friend sees the counters": the server hand every other game its copy. Me can not read the server
            // (no mod there), so me read this game's own note of what it sent there.
            var smelterRev = (long)zdo.DataRevision;
            var fireRev = (long)fireZdo.DataRevision;
            yield return Until(() => SentToServer(zdo) >= smelterRev && SentToServer(fireZdo) >= fireRev, 8f);
            c.Check(SentToServer(zdo) >= smelterRev && SentToServer(fireZdo) >= fireRev,
                $"M01 the batch-added values went to the server: this game sent it the smelter's data at revision {SentToServer(zdo)} (queue 5 was revision {smelterRev}) "
                + $"and the campfire's at {SentToServer(fireZdo)} (fuel 7 was revision {fireRev})");
            c.Check(smelter.m_nview.IsOwner() && fire.m_nview.IsOwner() && smelter.GetQueueSize() == 5,
                $"M01 the stations stayed with this game and the queue stayed 5 (owners {OwnerWord(zdo.GetOwner())}, {OwnerWord(fireZdo.GetOwner())}, queue {smelter.GetQueueSize()})");
            c.Report();
        }
        finally
        {
            tap?.Dispose();
            rig?.End();
        }
    }

    // ---------- batchfeed.mp.nonowner-smelter (client side of M02, M04, M11 on a real server) ----------

    private static IEnumerator RunMpSmelter()
    {
        var c = new Checks(MpSmelterName);
        Rig rig = null;
        LogTap tap = null;
        try
        {
            if (!OnServer(c))
            {
                c.Report();
                yield break;
            }
            rig = new Rig();
            tap = new LogTap();
            HudReady(c);
            var oreToken = Token("CopperOre");
            var goA = rig.Spawn("smelter", -4f, 7f);
            var goB = rig.Spawn("smelter", 4f, 7f);
            var a = goA != null ? goA.GetComponentInChildren<Smelter>() : null;
            var b = goB != null ? goB.GetComponentInChildren<Smelter>() : null;
            if (!c.Check(a != null && b != null && a.m_addOreSwitch != null && b.m_addOreSwitch != null && a.m_maxOre == 10 && oreToken != null,
                    "spawn two vanilla smelters (10 ore, no coal: nothing smelts)"))
            {
                c.Report();
                yield break;
            }
            // Let the server learn about them.
            yield return new WaitForSecondsRealtime(1.5f);
            var server = ServerUid();
            var mine = ZDOMan.GetSessionID();
            var zdoA = a.m_nview.GetZDO();
            var zdoB = b.m_nview.GetZDO();
            if (!c.Check(zdoA.GetOwner() == mine && zdoB.GetOwner() == mine && a.GetQueueSize() == 0 && b.GetQueueSize() == 0,
                    $"this game owns the two empty smelters it built (owners {OwnerWord(zdoA.GetOwner())}, {OwnerWord(zdoB.GetOwner())})")
                || !c.Check(rig.Give("CopperOre", 20), "give 20 CopperOre"))
            {
                c.Report();
                yield break;
            }

            // From here to Hooks.Off(): ONE frame, no wait. The server give a station back within about 2 s, so only
            // in the frame of the hand-over is "the server own it" sure on this game's copy. That is all the mod look
            // at: owner uid = the server's = a game still here (its real client check, no test hook).
            // M02 (and M11 without holding): Shift+E three times, then plain E.
            zdoA.SetOwner(server);
            Hooks.WatchRpcs(a.m_nview);
            var ore = a.m_addOreSwitch.gameObject;
            var mark = tap.Mark();
            var p1 = Press(ore, alt: true);
            var line1 = tap.Batch(mark);
            var p2 = Press(ore, alt: true);
            mark = tap.Mark();
            var p3 = Press(ore, alt: true);
            var line3 = tap.Batch(mark);
            var p4 = Press(ore, alt: false);
            var addsA = AddOreRpcs();
            var ownerA = zdoA.GetOwner();
            var leftA = rig.Count(oreToken);
            var queueA = a.GetQueueSize();

            // M04: E, E, Shift+E, then what still fit.
            rig.Empty();
            var gaveB = rig.Give("CopperOre", 20);
            zdoB.SetOwner(server);
            Hooks.WatchRpcs(b.m_nview);
            ore = b.m_addOreSwitch.gameObject;
            var q1 = Press(ore, alt: false);
            var q2 = Press(ore, alt: false);
            var q3 = Press(ore, alt: true);
            var sent = FeedTarget.TryResolve(ore, out var target) ? PendingAdds.Effective(target, target.ReadValue()) : -1f;
            var q4 = Press(ore, alt: true);
            var q5 = Press(ore, alt: false);
            var addsB = AddOreRpcs();
            var ownerB = zdoB.GetOwner();
            var leftB = rig.Count(oreToken);
            var queueB = b.GetQueueSize();
            Hooks.Off();
            var handed = Time.realtimeSinceStartup;

            c.Check(ownerA == server && ownerB == server && gaveB,
                $"every press ran while this game's copy said the server owns the smelter (owners {OwnerWord(ownerA)}, {OwnerWord(ownerB)}): the non-owner road was walked");
            c.Check(p1.Removed == 5 && p1.Messages == 1 && p1.Center == Loc($"$msg_added 5 {oreToken} (5/10)"), $"M02 first batch: 5, summary counts what was sent ({Show(p1)})");
            c.Check(p2.Removed == 5 && p2.Center == Loc($"$msg_added 5 {oreToken} (10/10)"), $"M02 second batch: 5 more, summary says 10/10 ({Show(p2)})");
            c.Check(p3.Removed == 0 && p3.Messages == 1 && p3.Center == Loc("$msg_itsfull"), $"M02 third batch: 'It's full', nothing removed ({Show(p3)})");
            c.Check(p4.Removed == 0 && p4.Messages == 1 && p4.Center == Loc("$msg_itsfull"), $"M11 without holding: a plain E right after the batches is refused the same way ({Show(p4)})");
            c.Check(leftA == 10 && addsA == 10 && queueA == 0,
                $"M02 exactly 10 ore left the inventory, as 10 vanilla add rpcs to the owner; the mod wrote nothing into the station itself (ore left {leftA}, add rpcs {addsA}, local queue {queueA})");
            ExpectLog(c, "M02 first batch", line1, "smelter SmelterInput", "owner=other", "room=5", "added=5", "stop=amount");
            ExpectLog(c, "M02 third batch", line3, "owner=other", "room=0", "stop=pending");
            c.Check(q1.Removed == 1 && q2.Removed == 1 && q3.Removed == 5 && Near(sent, 7f) && q3.Center == Loc($"$msg_added 5 {oreToken} (7/10)"),
                $"M04 E, E, Shift+E: 1 + 1 + 5 ore gone, the mod counts 7 sent ({q1.Removed}, {q2.Removed}, {q3.Removed}, counts {sent}, {Show(q3)})");
            c.Check(q4.Removed == 3 && q4.Center == Loc($"$msg_added 3 {oreToken} (10/10)") && q5.Removed == 0 && q5.Center == Loc("$msg_itsfull"),
                $"M04 then exactly 3 more fit, and the next press is refused ({Show(q4)} / {Show(q5)})");
            c.Check(leftB == 10 && addsB == 10 && queueB == 0, $"M04 never more than 10 sent to a smelter of 10 (ore left {leftB}, add rpcs {addsB}, local queue {queueB})");

            // The server simulate nothing: it give both back. Station with this game again = owner road: the counts
            // of a moment ago (10 of 10 "sent") must not block the new owner, the local copy is the truth now.
            yield return Until(() => zdoA.GetOwner() == mine && zdoB.GetOwner() == mine, 12f);
            var took = Time.realtimeSinceStartup - handed;
            if (c.Check(a != null && zdoA.GetOwner() == mine && zdoB.GetOwner() == mine,
                    $"the dedicated server gave the smelters back to this game (owners {OwnerWord(zdoA.GetOwner())}, {OwnerWord(zdoB.GetOwner())} after {took:0.0} s)"))
            {
                var queue = a.GetQueueSize();
                c.Note($"smelters back with this game {took:0.0} s after the hand-over, queues {queue} and {b.GetQueueSize()}: the server ran none of the adds "
                       + $"(it simulates nothing), so no friend's counter can be read here. The mod's counts live {PendingAdds.Window:0} s after the last add.");
                var want = Mathf.Min(5, a.m_maxOre - queue);
                mark = tap.Mark();
                var p = Press(a.m_addOreSwitch.gameObject, alt: true);
                c.Check(p.Removed == want && a.GetQueueSize() == queue + want,
                    $"the smelter is this game's again: Shift+E adds {want} at once, the counts from before do not block it (queue {queue} -> {a.GetQueueSize()}, {Show(p)})");
                if (want > 0)
                {
                    ExpectLog(c, "smelter back with this game", tap.Batch(mark), "owner=you", $"added={want}");
                }
            }
            c.Report();
        }
        finally
        {
            Hooks.Off();
            tap?.Dispose();
            rig?.End();
        }
    }

    // ---------- batchfeed.mp.ownergone (M10) ----------

    private static IEnumerator RunMpOwnerGone()
    {
        var c = new Checks(MpOwnerGoneName);
        Rig rig = null;
        LogTap tap = null;
        try
        {
            if (!OnServer(c))
            {
                c.Report();
                yield break;
            }
            rig = new Rig();
            tap = new LogTap();
            HudReady(c);
            var oreToken = Token("CopperOre");
            var go = rig.Spawn("smelter", 0f, 7f);
            var smelter = go != null ? go.GetComponentInChildren<Smelter>() : null;
            if (!c.Check(smelter != null && smelter.m_addOreSwitch != null && oreToken != null, "spawn a smelter"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSecondsRealtime(1.5f);
            var ore = smelter.m_addOreSwitch.gameObject;
            var zdo = smelter.m_nview.GetZDO();
            rig.Give("CopperOre", 20);

            // Owner uid of no connected game = the owner just logged out (not the server, not in the player list).
            zdo.SetOwner(MadeUpOwner);
            var mark = tap.Mark();
            var p = Press(ore, alt: true);
            c.Check(p.Removed == 1 && p.Messages == 1 && p.Center == Loc("$msg_added " + oreToken),
                $"M10 owner gone: Shift+E is a single vanilla press, at most 1 ore leaves ({Show(p)})");
            ExpectLog(c, "M10 owner gone", tap.Batch(mark), "smelter SmelterInput", "owner=gone");

            // The server gives the station a connected owner (itself or this game) within a few seconds.
            yield return Until(() => zdo.GetOwner() != MadeUpOwner && zdo.GetOwner() != 0L, 12f);
            var owner = zdo.GetOwner();
            var mine = owner == ZDOMan.GetSessionID();
            if (c.Check(mine || owner == ServerUid(), $"M10 the station got a new connected owner ({(mine ? "this game" : owner == ServerUid() ? "the server" : owner.ToString())})"))
            {
                yield return new WaitForSecondsRealtime(1.5f);
                var queue = smelter.GetQueueSize();
                p = Press(ore, alt: true);
                c.Check(p.Removed == 5 && rig.Count(oreToken) == 14, $"M10 a few seconds later Shift+E adds 5 again ({Show(p)})");
                yield return Until(() => smelter.GetQueueSize() == queue + 5, 12f);
                c.Check(smelter.GetQueueSize() == queue + 5, $"M10 the 5 ore are in the smelter (queue {queue} -> {smelter.GetQueueSize()})");
            }
            c.Report();
        }
        finally
        {
            tap?.Dispose();
            rig?.End();
        }
    }

    // ---------- batchfeed.mp.toggle (T21, the real Enabled setting: multiplayer runs use throwaway config files) ----------

    private static IEnumerator RunMpToggle()
    {
        var c = new Checks(MpToggleName);
        Rig rig = null;
        var view = FeatureRegistry.Find(ModInfo.Guid);
        var enabled = view != null ? view.Value.Enabled : null;
        try
        {
            if (!OnServer(c) || !c.Check(enabled != null && enabled.Value && ModActive(ModInfo.Guid), "the feature is on and active"))
            {
                c.Report();
                yield break;
            }
            rig = new Rig();
            HudReady(c);
            var oreToken = Token("CopperOre");
            var goA = rig.Spawn("smelter", -4f, 7f);
            var goB = rig.Spawn("smelter", 4f, 7f);
            var a = goA != null ? goA.GetComponentInChildren<Smelter>() : null;
            var b = goB != null ? goB.GetComponentInChildren<Smelter>() : null;
            if (!c.Check(a != null && b != null && a.m_addOreSwitch != null && oreToken != null, "spawn two smelters"))
            {
                c.Report();
                yield break;
            }
            yield return null;
            rig.Give("CopperOre", 20);
            ExpectHint(c, "feature on", a.m_addOreSwitch.GetHoverText());

            // What unticking the mod in the MC Mods panel writes.
            enabled.Value = false;
            yield return null;
            c.Check(!ModActive(ModInfo.Guid), "T21 unticked: the feature is inactive at once, no restart");
            ExpectNoHint(c, "T21 unticked", a.m_addOreSwitch.GetHoverText());
            var p = Press(a.m_addOreSwitch.gameObject, alt: true);
            c.Check(p.Removed == 1 && a.GetQueueSize() == 1 && p.Messages == 1 && p.Center == Loc("$msg_added " + oreToken),
                $"T21 unticked: Shift+E adds 1, vanilla's own message ({Show(p)})");

            enabled.Value = true;
            yield return null;
            // Turning off cleared the forced defaults (the self tests unregister with the feature).
            ForceDefaults();
            c.Check(ModActive(ModInfo.Guid), "T21 ticked again: the feature is active at once");
            ExpectHint(c, "T21 ticked again", b.m_addOreSwitch.GetHoverText());
            p = Press(b.m_addOreSwitch.gameObject, alt: true);
            c.Check(p.Removed == 5 && b.GetQueueSize() == 5 && p.Center == Loc($"$msg_added 5 {oreToken} (5/{b.m_maxOre})"),
                $"T21 ticked again: Shift+E adds 5 ({Show(p)})");
            c.Report();
        }
        finally
        {
            if (enabled != null && !enabled.Value)
            {
                enabled.Value = true;
            }
            rig?.End();
        }
    }

    // ---------- batchfeed.mp.settings (T12 ShowHint, T14 hint, T15, T27 with the real settings) ----------

    // Multiplayer runs use throwaway config files, so here the real ConfigEntry values change, like a player does in
    // ConfigurationManager or the cfg file: the hint must follow at once (setting changed = hint line rebuilt).
    private static IEnumerator RunMpSettings()
    {
        var c = new Checks(MpSettingsName);
        Rig rig = null;
        LogTap tap = null;
        var amount = Plugin.Amount.Value;
        var key = Plugin.ModifierKey.Value;
        var show = Plugin.ShowHint.Value;
        try
        {
            if (!OnServer(c))
            {
                c.Report();
                yield break;
            }
            rig = new Rig();
            tap = new LogTap();
            HudReady(c);
            // Real settings from here on. Only "no controller" stays forced (labels).
            Plugin.TestAmount = null;
            Plugin.TestModifierKey = null;
            Plugin.TestShowHint = null;
            Plugin.TestKeyHeld = null;
            Plugin.Amount.Value = 5;
            Plugin.ModifierKey.Value = KeyCode.None;
            Plugin.ShowHint.Value = true;

            var oreToken = Token("CopperOre");
            var goA = rig.Spawn("smelter", -6f, 7f);
            var goB = rig.Spawn("smelter", 0f, 7f);
            var kilnGo = rig.Spawn("charcoal_kiln", 8f, 8f);
            var a = goA != null ? goA.GetComponentInChildren<Smelter>() : null;
            var b = goB != null ? goB.GetComponentInChildren<Smelter>() : null;
            var kiln = kilnGo != null ? kilnGo.GetComponentInChildren<Smelter>() : null;
            if (!c.Check(a != null && b != null && kiln != null && a.m_addOreSwitch != null && a.m_maxOre == 10 && kiln.m_addOreSwitch != null
                         && ConversionIndex(kiln, "Wood") >= 0 && oreToken != null, "spawn two vanilla smelters and a charcoal kiln"))
            {
                c.Report();
                yield break;
            }
            yield return null;
            c.Check(rig.Give("CopperOre", 30), "give 30 CopperOre");
            ExpectHint(c, "default settings", a.m_addOreSwitch.GetHoverText());

            // T15: Amount = 10, live.
            Plugin.Amount.Value = 10;
            ExpectHint(c, "T15 Amount = 10 (hint follows at once)", a.m_addOreSwitch.GetHoverText(), amount: 10);
            var p = Press(a.m_addOreSwitch.gameObject, alt: true);
            c.Check(p.Removed == 10 && a.GetQueueSize() == 10 && p.Center == Loc($"$msg_added 10 {oreToken} (10/10)"), $"T15 Amount = 10: Shift+E adds 10 ({Show(p)})");
            Plugin.Amount.Value = 5;
            ExpectHint(c, "T15 Amount back to 5", b.m_addOreSwitch.GetHoverText());

            // T12: ShowHint = false, live.
            Plugin.ShowHint.Value = false;
            ExpectNoHint(c, "T12 ShowHint = false", b.m_addOreSwitch.GetHoverText());
            p = Press(b.m_addOreSwitch.gameObject, alt: true);
            c.Check(p.Removed == 5 && b.GetQueueSize() == 5, $"T12 ShowHint = false: Shift+E still adds 5 ({Show(p)})");
            Plugin.ShowHint.Value = true;
            ExpectHint(c, "T12 ShowHint = true again", b.m_addOreSwitch.GetHoverText());

            // T14: ModifierKey = RightShift, live. Nobody holds Right Shift on the test PC: the game's key alone adds 1.
            Plugin.ModifierKey.Value = KeyCode.RightShift;
            ExpectHint(c, "T14 ModifierKey = RightShift (hint follows at once)", b.m_addOreSwitch.GetHoverText(), modifier: Loc("$button_rshift"));
            p = Press(b.m_addOreSwitch.gameObject, alt: true);
            c.Check(p.Removed == 1 && b.GetQueueSize() == 6, $"T14 ModifierKey = RightShift: L-Shift + E adds 1 ({Show(p)})");

            // T27: ModifierKey = F13, live.
            rig.Empty();
            if (c.Check(rig.Give("Wood", 12), "give 12 Wood"))
            {
                BatchFeeder.ResetKeyCheck();
                var mark = tap.Mark();
                Plugin.ModifierKey.Value = KeyCode.F13;
                yield return CheckUnreadableKey(c, tap, mark, kiln);
            }
            Plugin.ModifierKey.Value = KeyCode.None;
            ExpectHint(c, "ModifierKey back to None", b.m_addOreSwitch.GetHoverText());
            c.Report();
        }
        finally
        {
            Plugin.Amount.Value = amount;
            Plugin.ModifierKey.Value = key;
            Plugin.ShowHint.Value = show;
            tap?.Dispose();
            rig?.End();
        }
    }
}
#endif
