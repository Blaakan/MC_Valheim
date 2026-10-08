#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;

namespace MC.UX.AutoPickupFilterMod;

// Debug build only. Multiplayer self tests (tools/Test-Multiplayer.ps1, scenario modded): this game is a client
// joined to a real dedicated server. The mod is client only (BepInProcess valheim.exe): the dedicated server never
// loads it, so there is no server step here; the server is "a server without this mod" for every test below.
// A second player cannot be played: "another player's game" is a made-up owner id (handoff) or the server itself
// (remote harvest; where the server does not run the place, a made-up owner id again).
internal static partial class SelfTests
{
    private const string MpFilterName = "lootfilter.mp.filter";
    private const string MpHandoffName = "lootfilter.mp.handoff";
    private const string MpRemoteHarvestName = "lootfilter.mp.remote-harvest";

    // Client of a server? Else the test say why and stop.
    private static bool OnServerAsClient(string name)
    {
        if (ZNet.instance == null || ZNet.instance.IsServer() || ZRoutedRpc.instance == null)
        {
            SelfTest.Fail(name, "this game is not a client joined to a server");
            return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- lootfilter.mp.filter (M01, M04)

    private static IEnumerator RunMpFilter()
    {
        if (!Ready(MpFilterName, out var player, out var gui) || !OnServerAsClient(MpFilterName))
        {
            yield break;
        }
        var c = new Checks(MpFilterName);
        var rig = new Rig(player, MpFilterName);
        try
        {
            var inv = rig.Inv;
            NoteRoom(c, rig);
            c.Note($"client {ZDOMan.GetSessionID()} on server {ZRoutedRpc.instance.GetServerPeerID()}, {ZNet.instance.GetNrOfPlayers()} player(s)");

            // Everything: both picked.
            Player.m_enableAutoPickup = false;
            var stone = rig.Drop("Stone", 5, rig.Spot(0.9f, 0.45f, 0.3f));
            var wood = rig.Drop("Wood", 5, rig.Spot(0.9f, -0.45f, 0.3f));
            if (!c.Check(stone != null && wood != null, "Stone and Wood on the ground"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(1.2f);
            var stones = Count(inv, "Stone");
            var woods = Count(inv, "Wood");
            Player.m_enableAutoPickup = true;
            var both = new List<ItemDrop> { stone, wood };
            yield return WaitGone(both, 4f);
            c.Check(!Alive(stone) && !Alive(wood) && Count(inv, "Stone") == stones + 5 && Count(inv, "Wood") == woods + 5, "Everything: Stone and Wood picked up");

            // Skip ignored, set with the button and a middle-click on the carried Stone.
            yield return Open();
            yield return Frames(2);
            var button = FindButton();
            var carried = inv.GetItem(Shared("Stone"));
            if (!c.Check(button != null && carried != null, "button is there; Stone carried"))
            {
                c.Report();
                yield break;
            }
            Click(button);
            c.Check(FilterState.Mode == FilterMode.SkipIgnored, "button click: Skip ignored");
            yield return MarkGesture(gui.m_playerGrid, carried);
            yield return null;
            c.Check(SameSet(FilterState.Ignored, "Stone") && BadgeOn(gui.m_playerGrid, carried, SpriteIgnored), "middle-click: Stone ignored, red mark");
            yield return Close();
            Player.m_enableAutoPickup = false;
            stone = rig.Drop("Stone", 5, rig.Spot(0.9f, 0.45f, 0.3f));
            wood = rig.Drop("Wood", 5, rig.Spot(0.9f, -0.45f, 0.3f));
            yield return new WaitForSeconds(1.2f);
            stones = Count(inv, "Stone");
            woods = Count(inv, "Wood");
            Player.m_enableAutoPickup = true;
            yield return new WaitForSeconds(2.5f);
            c.Check(!Alive(wood) && Count(inv, "Wood") == woods + 5, "Skip ignored: Wood picked up");
            c.Check(Alive(stone) && Count(inv, "Stone") == stones && HoverOf(stone).Contains(SkipLineIgnored), "Skip ignored: Stone stays, with the grey line");
            c.Check(Alive(stone) && stone.m_nview.IsOwner(), "the skipped Stone is an ordinary item of this client");
            yield return UsePickup(rig, stone);
            c.Check(!Alive(stone) && Count(inv, "Stone") == stones + 5, "E picks the skipped Stone up");

            // Only selected, set with the commands.
            var lines = Run(Chat.instance, "lootfilter mode only");
            lines.AddRange(Run(Chat.instance, "lootfilter_select Coins"));
            c.Check(FilterState.Mode == FilterMode.OnlySelected && SameSet(FilterState.Selected, "Coins"), $"commands: Only selected with Coins ({Lines(lines)})");
            Player.m_enableAutoPickup = false;
            var coins = rig.Drop("Coins", 5, rig.Spot(0.9f, 0f, 0.3f));
            wood = rig.Drop("Wood", 5, rig.Spot(0.9f, -0.5f, 0.3f));
            yield return new WaitForSeconds(1.2f);
            var coinCount = Count(inv, "Coins");
            woods = Count(inv, "Wood");
            Player.m_enableAutoPickup = true;
            yield return new WaitForSeconds(2.5f);
            c.Check(!Alive(coins) && Count(inv, "Coins") == coinCount + 5, "Only selected: Coins picked up");
            c.Check(Alive(wood) && Count(inv, "Wood") == woods && HoverOf(wood).Contains(SkipLineNotSelected), "Only selected: Wood stays, with the grey line");
            lines = Run(Chat.instance, "lootfilter");
            c.Check(AnyLine(lines, ModInfo.Name + ": mode Only selected, auto pickup on."), $"status command answers ({Lines(lines)})");

            // M04: the filter lives in the character (it goes wherever the character goes), nothing asked of the server.
            CheckStored(c, player, "OnlySelected", "Stone", "Coins", "on the server");
            c.Check(rig.NewGuardReports == 0 && rig.Logs.Count(LogLevel.Error | LogLevel.Fatal) == 0, $"no error from the mod on the client ({rig.Logs.First(LogLevel.Error | LogLevel.Fatal)})");
            c.Check(ZNet.instance != null && ZNet.instance.GetServerPeer() != null && Player.m_localPlayer == player, "still connected to the server at the end");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.mp.handoff (M02)

    private static IEnumerator RunMpHandoff()
    {
        if (!Ready(MpHandoffName, out var player, out _) || !OnServerAsClient(MpHandoffName))
        {
            yield break;
        }
        var c = new Checks(MpHandoffName);
        var rig = new Rig(player, MpHandoffName);
        try
        {
            yield return ClaimCore(rig, c);
            c.Check(ZNet.instance != null && ZNet.instance.GetServerPeer() != null, "still connected to the server at the end");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.mp.remote-harvest (M05)

    // Bush run by another game: here the dedicated server (the only other game there is). The pick then runs on the
    // server, the berries are born there and reach this client over the network inside the 3 s window.
    private static IEnumerator RunMpRemoteHarvest()
    {
        if (!Ready(MpRemoteHarvestName, out var player, out _) || !OnServerAsClient(MpRemoteHarvestName))
        {
            yield break;
        }
        var c = new Checks(MpRemoteHarvestName);
        var rig = new Rig(player, MpRemoteHarvestName);
        try
        {
            var inv = rig.Inv;
            NoteRoom(c, rig);
            OnlyCoins();
            var server = ZRoutedRpc.instance.GetServerPeerID();
            var bush = rig.Spawn("RaspberryBush", rig.Spot(1.3f), Quaternion.identity);
            var pickable = bush != null ? bush.GetComponent<Pickable>() : null;
            var view = bush != null ? bush.GetComponent<ZNetView>() : null;
            if (!c.Check(pickable != null && view != null && view.IsValid(), "spawned a RaspberryBush"))
            {
                c.Report();
                yield break;
            }
            yield return Settle();
            c.Check(pickable.CanBePicked(), "the bush can be picked");
            // Hand the bush to the server. The server keeps only what lies in its own loaded area (around the world
            // centre); else it hands the bush back to the nearest player within 2 s.
            var zdo = view.GetZDO();
            zdo.SetOwner(server);
            var held = 0f;
            var gaveBack = 0;
            var until = Time.time + 9f;
            while (Time.time < until && held < 3f && gaveBack < 3)
            {
                yield return null;
                if (zdo.GetOwner() == server)
                {
                    held += Time.deltaTime;
                }
                else
                {
                    held = 0f;
                    gaveBack++;
                    zdo.SetOwner(server);
                }
            }
            var fromCentre = new Vector2(player.transform.position.x, player.transform.position.z).magnitude;
            if (held < 3f)
            {
                // Harness limit, no fault of the mod: a dedicated server only runs what lies around the world centre
                // (its reference point never moves: ZDOMan.ReleaseNearbyZDOS, ZNet.GetReferencePosition), and this mod
                // has no code on the server to ask for more. Round 1: handed back 3 times, 229.53 m from the centre.
                c.Note($"the dedicated server does not run the bush here (it handed it back {gaveBack} time(s); the player is {F(fromCentre)} m from the world centre): "
                       + "the other game is played by a made-up owner instead, the server-run pick was NOT tested");
                yield return RemoteHarvestStandIn(rig, c, bush, pickable, zdo);
                if (view != null && view.IsValid())
                {
                    view.ClaimOwnership();
                }
                c.Check(rig.NewGuardReports == 0 && rig.Logs.Count(LogLevel.Error | LogLevel.Fatal) == 0, $"no error from the mod ({rig.Logs.First(LogLevel.Error | LogLevel.Fatal)})");
                c.Report();
                yield break;
            }
            c.Note($"the dedicated server runs the bush ({F(fromCentre)} m from the world centre)");
            var since = rig.DropsNow();
            var berries = Count(inv, "Raspberry");
            var pressed = Time.time;
            player.Interact(bush, false, false);
            var drops = new List<ItemDrop>();
            yield return WaitNewDrops(rig, since, 6f, drops);
            if (c.Check(drops.Count > 0, "the server ran the pick: berries reached this client"))
            {
                var total = drops.Sum(d => d.m_itemData.m_stack);
                c.Note($"{drops.Count} berry drop(s) arrived {F(Time.time - pressed)} s after the press; owned by this client at arrival: {drops.Count(d => d.m_nview.IsOwner())}");
                c.Check(drops.All(HarvestGrace.IsTagged), $"berries born on the server count as this player's harvest ({drops.Count(HarvestGrace.IsTagged)} of {drops.Count})");
                c.Check(!drops.Any(d => HoverOf(d).Contains(SkipLineAny)), "none shows 'Auto pickup skips this'");
                yield return WaitGone(drops, 6f);
                if (AliveCount(drops) > 0)
                {
                    yield return WalkOver(rig, drops, 6f);
                }
                c.Check(AliveCount(drops) == 0 && Count(inv, "Raspberry") == berries + total, $"the berries are picked up by this player ({AliveCount(drops)} left)");
            }
            if (view != null && view.IsValid())
            {
                view.ClaimOwnership();
            }
            c.Check(rig.NewGuardReports == 0 && rig.Logs.Count(LogLevel.Error | LogLevel.Fatal) == 0, $"no error from the mod ({rig.Logs.First(LogLevel.Error | LogLevel.Fatal)})");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // Stand-in for "another game runs the bush" where the server will not: a made-up player id owns the bush, so the
    // press of this player runs no pick here (the game sends it to the owner). The berries are then born 1.5 s later
    // where Pickable.Drop puts them, owned by that id: this game must count them as its own harvest, ask the owner
    // for them (it never asks for an item the filter skips) and pick them up once it has them.
    private static IEnumerator RemoteHarvestStandIn(Rig rig, Checks c, GameObject bush, Pickable pickable, ZDO zdo)
    {
        var player = rig.Player;
        var inv = rig.Inv;
        var since = rig.DropsNow();
        var berries = Count(inv, "Raspberry");
        zdo.SetOwner(OtherPlayer);
        player.Interact(bush, false, false);
        // 1.5 s of lag. The server hands a stray thing back to the nearest player every 2 s: keep the other owner.
        var end = Time.time + 1.5f;
        while (Time.time < end)
        {
            zdo.SetOwner(OtherPlayer);
            yield return null;
        }
        c.Check(rig.NewDrops(since).Count == 0 && pickable.CanBePicked(), $"E on a bush another game owns: this game itself picks nothing ({rig.NewDrops(since).Count} new item(s))");
        var born = new List<ItemDrop>();
        var at = bush.transform.position + Vector3.up * pickable.m_spawnOffset;
        for (var i = 0; i < 2; i++)
        {
            var drop = rig.Drop("Raspberry", 1, at + Vector3.up * (0.5f * i));
            if (drop != null && drop.m_nview != null && drop.m_nview.IsValid())
            {
                drop.m_nview.GetZDO().SetOwner(OtherPlayer);
                born.Add(drop);
            }
        }
        if (!c.Check(born.Count == 2, "two berries born at the bush 1.5 s after the press, owned by the other game"))
        {
            yield break;
        }
        c.Check(born.All(FilterState.ListBlocks), "the lists alone would skip the berries (Only selected, Coins only)");
        c.Check(born.All(HarvestGrace.IsTagged), $"berries born after the press count as this player's harvest ({born.Count(HarvestGrace.IsTagged)} of {born.Count})");
        c.Check(!born.Any(d => HoverOf(d).Contains(SkipLineAny)), "none shows 'Auto pickup skips this'");
        var asks = 0;
        end = Time.time + 2f;
        while (Time.time < end && asks == 0)
        {
            foreach (var d in born)
            {
                if (Alive(d))
                {
                    d.m_nview.GetZDO().SetOwner(OtherPlayer);
                }
            }
            yield return new WaitForFixedUpdate();
            foreach (var d in born)
            {
                if (Alive(d))
                {
                    asks = Mathf.Max(asks, d.m_ownerRetryCounter);
                }
            }
        }
        c.Check(asks > 0, $"this game asks the other owner for the berries ({asks} ownership request(s))");
        foreach (var d in born)
        {
            if (Alive(d))
            {
                d.m_nview.ClaimOwnership();
            }
        }
        yield return WaitGone(born, 4f);
        if (AliveCount(born) > 0)
        {
            yield return WalkOver(rig, born, 4f);
        }
        c.Check(AliveCount(born) == 0 && Count(inv, "Raspberry") == berries + 2, $"once handed over, the berries are picked up by this player ({AliveCount(born)} left)");
    }
}
#endif
