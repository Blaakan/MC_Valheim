#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using MC.Shared;
using UnityEngine;

namespace MC.UX.CheatedTagRemoverMod;

// Me = tests with a real dedicated server (tools/Test-Multiplayer.ps1). The server hold no game object, only saved
// data: its half (ServerStep) make objects straight in the object table, far from every player, and read them back.
// One client is not a second player: what another player's game see or own stay a by-hand check. To act like a
// player without the mod, this game ignore the server's "clean your side" (ServerLink.TestIgnoreLocal).
internal static partial class SelfTests
{
    // Far from the spawn: no player's area, so only the server hold these objects.
    private static readonly Vector3 FarPlace = new Vector3(4200f, 40f, 4200f);
    private static readonly List<ZDOID> ServerMade = new List<ZDOID>();

    // ---------- server half ----------

    // arg: "admin=add" | "admin=remove" | "make" | "check" | "drop". Answer detail: "key=value;..." or the problem.
    private static IEnumerator ServerStep(string arg, object[] reply)
    {
        yield return null;
        string problem = null;
        var detail = "";
        try
        {
            var net = ZNet.instance;
            var man = ZDOMan.instance;
            if (net == null || man == null || !net.IsServer())
            {
                problem = "not a server";
            }
            else
            {
                switch (arg)
                {
                    case "admin=add":
                    case "admin=remove":
                        var peers = net.GetPeers();
                        if (peers.Count == 0 || peers[0].m_socket == null || net.m_adminList == null)
                        {
                            problem = "no player connected or no admin list";
                            break;
                        }
                        var host = peers[0].m_socket.GetHostName();
                        if (arg == "admin=add")
                        {
                            net.m_adminList.Add(host);
                        }
                        else
                        {
                            net.m_adminList.Remove(host);
                        }
                        detail = "admin=" + (net.IsAdmin(host) ? 1 : 0);
                        break;
                    case "make":
                        ServerDrop(man);
                        detail = ServerMake(man);
                        break;
                    case "check":
                        detail = ServerCheck(man);
                        break;
                    case "drop":
                        detail = "dropped=" + ServerDrop(man);
                        break;
                    default:
                        problem = "unknown step argument '" + arg + "'";
                        break;
                }
            }
        }
        catch (Exception e)
        {
            problem = e.ToString();
        }
        SelfTest.Answer(reply, problem == null, problem ?? detail);
    }

    // Chest with two tagged stacks and one clean (unowned, like any place nobody is near), and a second object with
    // an item like on a stand, a queue tag and a cooking slot tag.
    private static string ServerMake(ZDOMan man)
    {
        var chest = man.CreateNewZDO(FarPlace, 0);
        chest.SetPrefab(ChestPrefab.GetStableHashCode());
        chest.Persistent = true;
        var holder = new Inventory("MCProbe", null, 8, 4);
        var tagged = TestItem("Wood", 20, true);
        tagged.m_customData["MC.SelfTest.Mark"] = "kept";
        var crafted = TestItem("Club", 1, true);
        crafted.m_gridPos = new Vector2i(1, 0);
        crafted.m_crafterID = 4242L;
        crafted.m_crafterName = "MCProbe crafter";
        var clean = TestItem("Stone", 5, false);
        clean.m_gridPos = new Vector2i(2, 0);
        holder.m_inventory.Add(tagged);
        holder.m_inventory.Add(crafted);
        holder.m_inventory.Add(clean);
        var pkg = new ZPackage();
        holder.Save(pkg);
        chest.Set(ZDOVars.s_items, pkg.GetArray());
        chest.Set(ZDOVars.s_cheated, true);
        chest.SetOwner(0L);

        var other = man.CreateNewZDO(FarPlace + new Vector3(2f, 0f, 0f), 0);
        other.SetPrefab(FloorPrefab.GetStableHashCode());
        other.Persistent = true;
        ItemDrop.SaveToZDO(TestItem("Wood", 3, true), other);
        other.Set(ZDOVars.s_cheated, true);
        other.Set(ZDOVars.s_cheatedQueued, true);
        other.Set("slotstatus1", 0);
        other.Set(ZDOVars.s_cheatedQueued + 1, true);
        other.SetOwner(0L);

        ServerMade.Add(chest.m_uid);
        ServerMade.Add(other.m_uid);
        return "made=" + ServerMade.Count + ";" + ServerCheck(man);
    }

    private static string ServerCheck(ZDOMan man)
    {
        var last = "jobs=" + SweepJob.Finished + ";rounds=" + SweepJob.LastRounds
                   + ";left=" + (SweepJob.LastAfter != null ? SweepJob.LastAfter.World : -1)
                   + ";open=" + (SweepJob.LastAfter != null ? SweepJob.LastAfter.StoragesInUse : -1)
                   + ";wrotepieces=" + (SweepJob.LastWrote != null ? SweepJob.LastWrote.Pieces : -1)
                   + ";marked=" + SweepJob.LastMarked + ";portal=" + (SweepJob.LastMarkedPortal ? 1 : 0);
        if (ServerMade.Count < 2)
        {
            return "gone=2;" + last;
        }
        var chest = man.GetZDO(ServerMade[0]);
        var other = man.GetZDO(ServerMade[1]);
        if (chest == null || other == null)
        {
            return "gone=" + ((chest == null ? 1 : 0) + (other == null ? 1 : 0)) + ";" + last;
        }
        var blob = chest.GetByteArray(ZDOVars.s_items);
        var items = ReadInventory(blob);
        var kept = 0;
        if (items != null)
        {
            foreach (var pair in items)
            {
                var item = pair.Value;
                if ((item.m_stack == 20 && item.m_customData.TryGetValue("MC.SelfTest.Mark", out var m) && m == "kept")
                    || (item.m_crafterID == 4242L && item.m_crafterName == "MCProbe crafter") || item.m_stack == 5)
                {
                    kept++;
                }
            }
        }
        var single = other.GetByteArray(ZDOVars.s_itemData);
        var parts = new[]
        {
            "gone=0",
            "objects=" + ((chest.GetBool(ZDOVars.s_cheated) ? 1 : 0) + (other.GetBool(ZDOVars.s_cheated) ? 1 : 0)),
            "queues=" + ((other.GetBool(ZDOVars.s_cheatedQueued) ? 1 : 0) + (other.GetBool(ZDOVars.s_cheatedQueued + 1) ? 1 : 0)),
            "stored=" + Tagged(blob),
            "items=" + (items != null ? items.Count : -1),
            "kept=" + kept,
            "single=" + (single != null && single.Length > 2 ? single[single.Length - 1] & 1 : -1),
            last,
        };
        return string.Join(";", parts);
    }

    private static int ServerDrop(ZDOMan man)
    {
        var n = 0;
        foreach (var id in ServerMade)
        {
            var zdo = man.GetZDO(id);
            if (zdo == null)
            {
                continue;
            }
            zdo.SetOwner(ZDOMan.GetSessionID());
            man.DestroyZDO(zdo);
            n++;
        }
        ServerMade.Clear();
        return n;
    }

    // ---------- client tools ----------

    private sealed class Server
    {
        internal readonly SelfTest.ServerReply Reply = new SelfTest.ServerReply();

        internal bool Ok => Reply.Answered && Reply.Ok;

        internal int Num(string key)
        {
            foreach (var part in (Reply.Detail ?? "").Split(';'))
            {
                var split = part.IndexOf('=');
                if (split > 0 && part.Substring(0, split) == key
                    && int.TryParse(part.Substring(split + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    return value;
                }
            }
            return int.MinValue;
        }
    }

    private static IEnumerator Ask(Server s, string arg)
    {
        yield return SelfTest.CallServer(ServerStepName, arg, s.Reply);
    }

    // Test cut short (failed setup, timeout): server still put back what the test changed there. No wait possible
    // in a finally, so the two steps are just sent; the server run them in order before the next test's first step.
    private static void ServerCleanUp(bool made, bool admin)
    {
        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            return;
        }
        if (made)
        {
            plugin.StartCoroutine(SelfTest.CallServer(ServerStepName, "drop", new SelfTest.ServerReply()));
        }
        if (admin)
        {
            plugin.StartCoroutine(SelfTest.CallServer(ServerStepName, "admin=remove", new SelfTest.ServerReply()));
        }
    }

    // The player's own side: a chest and a portal it owns, both tagged, a tagged stack in the chest, a tagged Club
    // in the inventory.
    private sealed class OwnSide
    {
        internal Container Chest;
        internal ZDO ChestZdo;
        internal ZDO PortalZdo;
        internal ItemDrop.ItemData Carried;
        internal bool Ok;

        internal bool ChestClean => !ChestZdo.GetBool(ZDOVars.s_cheated) && Tagged(ChestZdo.GetByteArray(ZDOVars.s_items)) == 0;

        // Chest tagged again by its owner: first stack, saved the way the game save an item move. Never keep the
        // stack object: a chest reloads its items when its saved data moved.
        internal void TagChest(bool objectTag)
        {
            var stacks = Chest.GetInventory().GetAllItems();
            if (stacks.Count > 0)
            {
                stacks[0].m_cheated = true;
            }
            Chest.GetInventory().Changed();
            if (objectTag)
            {
                ChestZdo.Set(ZDOVars.s_cheated, true);
            }
        }
    }

    private static IEnumerator MakeOwnSide(Checks c, Rig rig, Player player, OwnSide own)
    {
        var chestGo = rig.Spawn(ChestPrefab);
        var portalHashes = Game.instance.PortalPrefabHash;
        var portalGo = portalHashes.Count > 0 ? rig.Spawn(ZNetScene.instance.GetPrefab(portalHashes[0])) : null;
        own.Chest = chestGo != null ? chestGo.GetComponent<Container>() : null;
        yield return null;
        own.ChestZdo = Zdo(chestGo);
        own.PortalZdo = Zdo(portalGo);
        own.Carried = player.GetInventory().AddItem("Club", 1, 1, 0, 0L, "", true);
        var wood = own.Chest != null ? own.Chest.GetInventory().AddItem("Wood", 11, 1, 0, 0L, "", true) : null;
        own.Ok = c.Check(own.ChestZdo != null && own.PortalZdo != null && own.Carried != null && wood != null,
            $"spawn '{ChestPrefab}' with a tagged stack and a portal; a tagged Club in the inventory");
        if (!own.Ok)
        {
            yield break;
        }
        own.ChestZdo.Set(ZDOVars.s_cheated, true);
        own.PortalZdo.Set(ZDOVars.s_cheated, true);
        // The server learns about the new objects.
        yield return new WaitForSeconds(2f);
    }

    private static bool Answered(List<string> lines)
    {
        return Any(lines, "Cheated tags removed: ") || lines.Contains(Report.NothingFound) || Any(lines, "No cheated tag could be removed.");
    }

    // ---------- uncheat.mp.sweep ----------

    private static IEnumerator RunMpSweep()
    {
        var c = new Checks(MpSweepName);
        var s = new Server();
        var player = Player.m_localPlayer;
        var net = ZNet.instance;
        var console = global::Console.instance;
        var plugin = Plugin.Instance;
        if (!c.Check(player != null && net != null && !net.IsServer() && console != null && plugin != null && plugin.IsActive,
                "a player on a dedicated server that has the mod, with a console"))
        {
            c.Report();
            yield break;
        }
        var rig = new Rig(player);
        var lines = new List<string>();
        var own = new OwnSide();
        var made = false;
        var admin = false;
        var done = false;
        try
        {
            yield return Ask(s, "admin=remove");
            var ready = c.Check(s.Ok && s.Num("admin") == 0, "the server answers; the player is not an admin (" + s.Reply + ")");
            if (ready)
            {
                yield return Ask(s, "make");
                made = s.Ok;
                ready = c.Check(s.Ok && s.Num("objects") == 2 && s.Num("queues") == 2 && s.Num("stored") == 2 && s.Num("single") == 1,
                    "the server made its far objects: 2 object tags, 2 queue tags, 2 tagged stacks in a chest, 1 item like on a stand (" + s.Reply + ")");
            }
            var jobs0 = s.Num("jobs");
            if (ready)
            {
                yield return MakeOwnSide(c, rig, player, own);
                ready = own.Ok;
            }
            if (ready)
            {
                Output.TestLines = lines;

                // Not an admin, count: own items counted here, the world refused.
                console.TryRunCommand(ClearCommand.ScanName);
                c.Check(Any(lines, "Asked the server to count the cheated tags of the whole world") && Any(lines, "On your game: "),
                    $"cheated_scan asks the server and counts the carried item on the player's game ({Show(lines)})");
                yield return Until(() => Any(lines, "Only an admin can do this"), 8f);
                c.Check(Any(lines, "Only an admin can do this for the whole world") && own.Carried.m_cheated,
                    $"the server refuses to count for a player who is not an admin; nothing changed ({Show(lines)})");

                // Not an admin, clear: own carried items cleaned (only this game can), the world refused.
                lines.Clear();
                console.TryRunCommand(ClearCommand.ClearName);
                c.Check(Any(lines, "Asked the server to remove the cheated tags of the whole world")
                        && Any(lines, "Cheated tags removed on your game: ") && !own.Carried.m_cheated,
                    $"cheated_clear cleans the carried item on the player's game and asks the server ({Show(lines)})");
                yield return Until(() => Any(lines, "Only an admin can do this"), 8f);
                c.Check(Any(lines, "Only an admin can do this for the whole world"), $"the server refuses a player who is not an admin ({Show(lines)})");
                yield return new WaitForSeconds(1f);
                yield return Ask(s, "check");
                c.Check(s.Ok && s.Num("objects") == 2 && s.Num("stored") == 2 && s.Num("jobs") == jobs0 && LoadedTags(own.Chest) == 1
                        && own.ChestZdo.GetBool(ZDOVars.s_cheated) && own.PortalZdo.GetBool(ZDOVars.s_cheated),
                    "nothing changed in the world: the server's objects, the player's chest and portal (" + s.Reply + ")");
                own.Carried.m_cheated = true;

                yield return Ask(s, "admin=add");
                admin = s.Ok && s.Num("admin") == 1;
                c.Check(admin, "the player is on the server's admin list now (" + s.Reply + ")");

                // Admin, count: answer comes back, nothing changes.
                lines.Clear();
                console.TryRunCommand(ClearCommand.ScanName);
                yield return Until(() => Any(lines, "Cheated tags found (nothing was changed): "), 15f);
                c.Check(Any(lines, "Cheated tags found (nothing was changed): ") && Any(lines, "On your game: "),
                    $"cheated_scan as admin: the server's count arrives ({Show(lines)})");
                yield return Ask(s, "check");
                c.Check(s.Ok && s.Num("objects") == 2 && s.Num("stored") == 2 && s.Num("queues") == 2 && s.Num("single") == 1
                        && own.Carried.m_cheated && !own.ChestClean, "the count changed nothing (" + s.Reply + ")");

                // Admin, clear. The command cleans the carried item itself at once; tag it again so the server's
                // request to this game ("clean your side") shows on it.
                lines.Clear();
                console.TryRunCommand(ClearCommand.ClearName);
                var ownAtOnce = !own.Carried.m_cheated;
                own.Carried.m_cheated = true;
                lines.Clear();
                yield return Until(() => Answered(lines), 40f);
                c.Note("said: " + Show(lines));
                c.Check(ownAtOnce && Any(lines, "The server is removing the cheated tags of the whole world.") && Any(lines, "Cheated tags removed: "),
                    "the server says it started, then what it removed");
                c.Check(Any(lines, "Cheated tags removed on your game: ") && !own.Carried.m_cheated,
                    "asked by the server, the player's game cleaned what it carries and the objects it owns");
                c.Check(own.ChestClean && LoadedTags(own.Chest) == 0 && !own.PortalZdo.GetBool(ZDOVars.s_cheated),
                    "the chest and the portal the player owns: object tags, saved items and loaded items clean");
                c.Check(Any(lines, "1 player online has this mod") && lines.Contains("Saving the world."),
                    "the answer counts the player with the mod and says the world is being saved");
                yield return Ask(s, "check");
                c.Check(s.Ok && s.Num("gone") == 0 && s.Num("objects") == 0 && s.Num("queues") == 0 && s.Num("stored") == 0 && s.Num("single") == 0,
                    "the server's far objects are clean: object tags, queue tags, chest stacks, stand item (" + s.Reply + ")");
                c.Check(s.Num("items") == 3 && s.Num("kept") == 3,
                    "the far chest still has its 3 stacks with the other mod's data, the crafter and the stack sizes (" + s.Reply + ")");
                c.Check(s.Num("jobs") == jobs0 + 2 && s.Num("left") == 0,
                    "the server ran one count and one clean-up, and its last check found nothing (" + s.Reply + ")");
                c.Check(s.Num("wrotepieces") == 2 && s.Num("marked") >= 4 && s.Num("portal") == 1,
                    "the server wrote only its 2 far objects (the player's game cleaned its own chest and portal first) and still "
                    + "marked all of them, the portal file included, for the save (" + s.Reply + ")");

                // A player without the mod owns a tagged, idle chest: the server writes it, the player's game takes
                // the server's version. This game ignores the server's "clean your side" for this part.
                own.TagChest(true);
                c.Check(Tagged(own.ChestZdo.GetByteArray(ZDOVars.s_items)) == 1, "the chest is tagged again (saved by its owner)");
                yield return new WaitForSeconds(2.5f);
                ServerLink.TestIgnoreLocal = true;
                lines.Clear();
                console.TryRunCommand(ClearCommand.ClearName);
                own.Carried.m_cheated = true;
                lines.Clear();
                yield return Until(() => Answered(lines), 40f);
                c.Check(Any(lines, "Cheated tags removed: ") && !Any(lines, "Cheated tags removed on your game: ")
                        && lines.Contains("Saving the world."),
                    $"the server cleaned it alone and saved, less than a minute after its last save ({Show(lines)})");
                yield return Until(() => own.ChestClean, 8f);
                c.Check(own.ChestClean, "the server's write reached the owner's game: object tag and saved items clean");
                yield return new WaitForSeconds(1.6f);
                c.Check(LoadedTags(own.Chest) == 0 && own.Chest.GetInventory().NrOfItems() == 1, "the owner's loaded chest reloaded the clean items (1 stack)");
                c.Check(own.Carried.m_cheated, "what that player carries keeps the tag: only its own game can reach it");
                yield return Ask(s, "check");
                c.Check(s.Ok && s.Num("left") == 0 && s.Num("rounds") == 1, "the server's check found nothing left after one pass (" + s.Reply + ")");
            }
            done = true;
        }
        finally
        {
            Output.TestLines = null;
            ServerLink.TestIgnoreLocal = false;
            if (own.Carried != null && player != null)
            {
                player.GetInventory().RemoveItem(own.Carried);
            }
            rig.DestroyAll();
            if (!done)
            {
                ServerCleanUp(made, admin);
            }
        }
        if (made)
        {
            yield return Ask(s, "drop");
            c.Check(s.Ok, "the server's far objects are gone again (" + s.Reply + ")");
        }
        if (admin)
        {
            yield return Ask(s, "admin=remove");
            c.Check(s.Ok && s.Num("admin") == 0, "the player is off the admin list again");
        }
        c.Report();
    }

    // ---------- uncheat.mp.contested ----------

    // What the server must not win, and what it cannot win: a chest another player has open, and an object another
    // player's game keeps writing. This game plays that other player (it ignores "clean your side").
    private static IEnumerator RunMpContested()
    {
        var c = new Checks(MpContestedName);
        var s = new Server();
        var player = Player.m_localPlayer;
        var net = ZNet.instance;
        var console = global::Console.instance;
        var plugin = Plugin.Instance;
        if (!c.Check(player != null && net != null && !net.IsServer() && console != null && plugin != null && plugin.IsActive,
                "a player on a dedicated server that has the mod, with a console"))
        {
            c.Report();
            yield break;
        }
        var rig = new Rig(player);
        var lines = new List<string>();
        var own = new OwnSide();
        var admin = false;
        var done = false;
        try
        {
            yield return Ask(s, "admin=add");
            admin = s.Ok && s.Num("admin") == 1;
            var ready = c.Check(admin, "the player is on the server's admin list (" + s.Reply + ")");
            if (ready)
            {
                yield return MakeOwnSide(c, rig, player, own);
                ready = own.Ok;
            }
            if (ready)
            {
                Output.TestLines = lines;
                ServerLink.TestIgnoreLocal = true;
                // Only the stack stays tagged: the object tags go, so the answer is about the open chest alone.
                own.ChestZdo.Set(ZDOVars.s_cheated, false);
                own.PortalZdo.Set(ZDOVars.s_cheated, false);

                // Chest open on its owner while the server cleans.
                own.Chest.SetInUse(true);
                yield return new WaitForSeconds(2.5f);
                console.TryRunCommand(ClearCommand.ClearName);
                own.Carried.m_cheated = true;
                lines.Clear();
                yield return Until(() => Answered(lines), 40f);
                c.Note("open chest, said: " + Show(lines));
                c.Check(Any(lines, "Not removed: 1 item in 1 chest that a player has open right now, or that a player who lost the "
                                   + "connection left open (at ") && AnyHolds(lines, "Open and close that chest once, then type cheated_clear again."),
                    "the answer names the open chest, where it stands and what to do");
                c.Check(!Any(lines, "Checked again: no cheated tag is left"), "and does not say that nothing is left");
                yield return new WaitForSeconds(1.5f);
                c.Check(Tagged(own.ChestZdo.GetByteArray(ZDOVars.s_items)) == 1 && LoadedTags(own.Chest) == 1,
                    "the open chest was not written: saved and loaded items still tagged");
                yield return Ask(s, "check");
                c.Check(s.Ok && s.Num("open") == 1 && s.Num("left") == 0, "the server counted 1 open chest and nothing else left (" + s.Reply + ")");

                // Closed: next run cleans it.
                own.Chest.SetInUse(false);
                yield return new WaitForSeconds(2.5f);
                lines.Clear();
                console.TryRunCommand(ClearCommand.ClearName);
                own.Carried.m_cheated = true;
                lines.Clear();
                yield return Until(() => Answered(lines), 40f);
                c.Check(Any(lines, "Cheated tags removed: 1 item in 1 chest or other storage.") && !Any(lines, "Not removed: "),
                    $"once the chest is closed the next run cleans it ({Show(lines)})");
                yield return Until(() => own.ChestClean, 8f);
                c.Check(own.ChestClean, "the closed chest's saved items are clean on its owner's game");

                // An object its owner keeps writing: this game tags the chest object again every frame.
                own.ChestZdo.Set(ZDOVars.s_cheated, true);
                yield return new WaitForSeconds(2.5f);
                lines.Clear();
                console.TryRunCommand(ClearCommand.ClearName);
                own.Carried.m_cheated = true;
                lines.Clear();
                yield return Until(() =>
                {
                    own.ChestZdo.Set(ZDOVars.s_cheated, true);
                    return Answered(lines);
                }, 60f);
                c.Note("busy object, said: " + Show(lines));
                c.Check(Any(lines, "Still tagged: 1 building piece.") && AnyHolds(lines, "Another player's game keeps writing these objects right now")
                        && !Any(lines, "Checked again: no cheated tag is left"),
                    "the answer lists the object that came back and says to try again");
                yield return Ask(s, "check");
                c.Check(s.Ok && s.Num("rounds") == SweepJob.MaxRounds && s.Num("left") == 1,
                    $"the server wrote it {SweepJob.MaxRounds} times and its last check still found it (" + s.Reply + ")");
            }
            done = true;
        }
        finally
        {
            Output.TestLines = null;
            ServerLink.TestIgnoreLocal = false;
            if (own.Chest != null)
            {
                own.Chest.SetInUse(false);
            }
            if (own.Carried != null && player != null)
            {
                player.GetInventory().RemoveItem(own.Carried);
            }
            rig.DestroyAll();
            if (!done)
            {
                ServerCleanUp(false, admin);
            }
        }
        if (admin)
        {
            yield return Ask(s, "admin=remove");
            c.Check(s.Ok && s.Num("admin") == 0, "the player is off the admin list again");
        }
        c.Report();
    }

    // ---------- uncheat.mp.local ----------

    // Server without the mod: the framework calls this copy inactive, the commands stay and work on the own side.
    private static IEnumerator RunMpLocal()
    {
        var c = new Checks(MpLocalName);
        var player = Player.m_localPlayer;
        var net = ZNet.instance;
        var console = global::Console.instance;
        var plugin = Plugin.Instance;
        if (!c.Check(player != null && net != null && !net.IsServer() && console != null && plugin != null,
                "a player on a dedicated server, with a console"))
        {
            c.Report();
            yield break;
        }
        var rig = new Rig(player);
        var lines = new List<string>();
        ItemDrop.ItemData carried = null;
        try
        {
            c.Check(plugin.State == ModState.ServerMissing, $"the mod's status says the server does not have it ({plugin.State}: {plugin.StatusText})");
            c.Check(Terminal.commands.ContainsKey(ClearCommand.ClearName) && Terminal.commands.ContainsKey(ClearCommand.ScanName),
                "both commands still exist");
            var chestGo = rig.Spawn(ChestPrefab);
            var chest = chestGo != null ? chestGo.GetComponent<Container>() : null;
            yield return null;
            var chestZdo = Zdo(chestGo);
            carried = player.GetInventory().AddItem("Club", 1, 1, 0, 0L, "", true);
            var wood = chest != null ? chest.GetInventory().AddItem("Wood", 11, 1, 0, 0L, "", true) : null;
            if (!c.Check(chestZdo != null && carried != null && wood != null, $"spawn '{ChestPrefab}' with a tagged stack; a tagged Club in the inventory"))
            {
                c.Report();
                yield break;
            }
            chestZdo.Set(ZDOVars.s_cheated, true);
            yield return null;
            Output.TestLines = lines;

            console.TryRunCommand(ClearCommand.ScanName);
            c.Check(Any(lines, "Cheated tags found (nothing was changed): ") && AnyHolds(lines, "The server does not have " + ModInfo.Name)
                    && carried.m_cheated && chestZdo.GetBool(ZDOVars.s_cheated),
                $"cheated_scan counts on this game only, says so, changes nothing ({Show(lines)})");

            lines.Clear();
            var jobs = SweepJob.Finished;
            var revision = chestZdo.DataRevision;
            var blob = chestZdo.GetByteArray(ZDOVars.s_items);
            console.TryRunCommand(ClearCommand.ClearName);
            c.Note("said: " + Show(lines));
            c.Check(Any(lines, "Cheated tags removed: ") && AnyHolds(lines, "1 item you carry")
                    && AnyHolds(lines, "only the items you carry and the objects loaded around you were cleaned"),
                "cheated_clear cleans this game's side and says the rest of the world needs the mod on the server");
            c.Check(!carried.m_cheated && LoadedTags(chest) == 0 && !chestZdo.GetBool(ZDOVars.s_cheated) && Tagged(chestZdo.GetByteArray(ZDOVars.s_items)) == 0,
                "carried item, loaded chest (object tag, saved items, loaded items): clean");
            c.Check(SweepJob.Finished == jobs && !SweepJob.Running, "no world pass ran on this game");
            // On a client every real write raises the revision and queues the object for the server in one call.
            c.Check(chestZdo.DataRevision >= revision + 2 && !ReferenceEquals(chestZdo.GetByteArray(ZDOVars.s_items), blob),
                "both changes of the chest (object tag, a new item array) are real writes, on their way to the server like any change this game makes");
        }
        finally
        {
            Output.TestLines = null;
            if (carried != null && player != null)
            {
                player.GetInventory().RemoveItem(carried);
            }
            rig.DestroyAll();
        }
        c.Report();
    }
}
#endif
