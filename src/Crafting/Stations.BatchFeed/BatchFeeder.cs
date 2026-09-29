using System;
using System.Collections.Generic;
using System.Text;
using MC.Shared;
using UnityEngine;

namespace MC.Crafting.StationsBatchFeedMod;

// Me = the batch press. Modifier + Use on covered spot = vanilla press again and again, same frame, same spot,
// through the spot's own Interactable.Interact (never Player.Interact: that is where our prefix live).
// So vanilla still pick the item, check the rules, remove one item, send its own RPC, raise skill, and other
// mods' patches on those methods still run once per item. Me never write any ZDO.
// Loop stop at: Amount, room (from local copy + PendingAdds), press that fail or remove nothing, press that remove
// more than one (another batch mod did its fill inside), ballista missile kind change.
internal static class BatchFeeder
{
    // Loop never ask vanilla fire to toggle: alt = "add fuel instead of toggle" in Fireplace.Interact.
    private const bool AltAddsFuel = true;

    private static readonly EffectList NoEffects = new EffectList();

    // Item name -> count, before and after the loop. Reused: no garbage per press beyond the summary text.
    private static readonly Dictionary<string, int> CountsBefore = new Dictionary<string, int>();
    private static readonly Dictionary<string, int> CountsAfter = new Dictionary<string, int>();
    private static readonly List<string> NamesInOrder = new List<string>();

    // True only inside the loop. MessageHudPatches swallow center messages then (one per item = flicker).
    internal static bool Muting;

    // Last center message swallowed. Nothing added = this tell why (no items, no fire, wrong ammo...).
    internal static string LastMuted;

    // First skill level (0 -> 1) is a center message in vanilla (Skills.RaiseSkill). Me hold it during loop and show
    // it under the summary: never lost, never a refuse reason.
    internal static string HeldSkillUp;

    internal static void Reset()
    {
        Muting = false;
        LastMuted = null;
        HeldSkillUp = null;
        CountsBefore.Clear();
        CountsAfter.Clear();
        NamesInOrder.Clear();
    }

    // Batch key down? Default (None) and controller = vanilla alt (Alternative placement / alt keys binding).
    internal static bool BatchKeyHeld(bool alt)
    {
        var key = Plugin.ModifierKey.Value;
        if (key == KeyCode.None || ZInput.IsGamepadActive())
        {
            return alt;
        }
        return ZInput.GetKey(key, logWarning: false);
    }

    // Batch press. True = me handled it, skip vanilla press. False = let vanilla do its normal one press.
    internal static bool TryRun(Player player, in FeedTarget target)
    {
        // No owner: vanilla add would be lost on most stations (RPC nobody handle). Vanilla press claim it for next time.
        if (!target.IsLive || !target.NView.HasOwner() || !target.IsBatchable())
        {
            return false;
        }
        var inventory = player.GetInventory();
        if (inventory == null)
        {
            return false;
        }

        string lockPrefab = null;
        if (target.Kind == FeedKind.TurretAmmo)
        {
            var predicted = target.PredictAmmo(inventory);
            if (predicted == null || predicted.m_dropPrefab == null)
            {
                return false; // vanilla say "no ammo" / "other ammo" itself
            }
            lockPrefab = predicted.m_dropPrefab.name;
        }

        var owner = target.NView.IsOwner();
        if (!owner && !OwnerConnected(target.NView))
        {
            // Owner game gone (log out), server not yet give station to next player (up to 2 s): server drop every add
            // sent to it. Vanilla lose one item there, batch would lose Amount. So me let vanilla do its single press.
            Log.Debug($"Batch feed: {target.PieceName} {target.Kind} owner=gone, single vanilla press");
            return false;
        }
        var local = target.ReadValue();
        var effective = local;
        if (owner)
        {
            PendingAdds.Drop(target);
        }
        else
        {
            effective = PendingAdds.Effective(target, local);
            var locked = target.Kind == FeedKind.TurretAmmo ? PendingAdds.LockedPrefab(target, local) : null;
            if (locked != null && locked != lockPrefab)
            {
                // Owner would relabel every missile with the new kind (dupe or loss). Vanilla words, nothing removed.
                ShowOtherAmmo(player, locked);
                player.m_lastHoverInteractTime = Time.time;
                Log.Debug($"Batch feed: {target.PieceName} {target.Kind} owner=other stop=otherammo (sent {locked} a moment ago)");
                return true;
            }
        }

        var amount = Plugin.Amount.Value;
        var room = target.Room(effective, amount);
        if (room == 0)
        {
            if (!owner && target.Room(local, 1) > 0)
            {
                // Local copy still show room, but me already sent enough to fill it: vanilla add would overfill or be lost.
                player.Message(MessageHud.MessageType.Center, target.FullMessage());
                player.m_lastHoverInteractTime = Time.time;
                Log.Debug($"Batch feed: {target.PieceName} {target.Kind} owner=other room=0 stop=pending");
                return true;
            }
            return false; // really full: vanilla press say "It's full" itself
        }

        TakeCounts(inventory, CountsBefore, NamesInOrder);
        var before = inventory.CountItems(null, -1, matchWorldLevel: false);
        var removedTotal = 0;
        var presses = 0;
        var stop = "amount";
        EffectList savedEffects = null;
        var swapTried = false;
        var swapped = false;

        Muting = true;
        LastMuted = null;
        HeldSkillUp = null;
        try
        {
            var use = target.Interactable;
            for (var k = 0; k < room; k++)
            {
                if (k > 0 && lockPrefab != null)
                {
                    // Ballista: one missile kind per batch, the kind vanilla picked for press one.
                    var next = target.PredictAmmo(inventory);
                    if (next == null || next.m_dropPrefab == null || next.m_dropPrefab.name != lockPrefab)
                    {
                        stop = "otherammo";
                        break;
                    }
                }

                var ok = use.Interact(player, false, AltAddsFuel);
                var after = inventory.CountItems(null, -1, matchWorldLevel: false);
                var removed = before - after;
                before = after;
                if (removed > 0)
                {
                    removedTotal += removed;
                }
                // "true" alone is no proof: cooking station say true for an incompatible item and remove nothing.
                if (!ok || removed <= 0)
                {
                    stop = "refused";
                    break;
                }
                presses++;
                if (removed > 1)
                {
                    // Vanilla add path remove exactly one. More = other batch mod filled inside this press. Stop now,
                    // else its stale-copy fill repeat every press (non-owner overfill).
                    stop = "othermod";
                    Log.Debug($"Batch feed: one press removed {removed} items: another mod batched it, me stop here.");
                    break;
                }
                if (owner && !swapTried)
                {
                    // One add sound per press: first add played it, rest stay quiet. Only the add RPC read the field,
                    // and me put it back below, same frame.
                    swapTried = true;
                    savedEffects = target.GetAddEffects();
                    if (savedEffects != null)
                    {
                        target.SetAddEffects(NoEffects);
                        swapped = true;
                    }
                }
            }
            if (stop == "amount" && room < amount)
            {
                stop = "room";
            }
        }
        catch (Exception e)
        {
            // Other mod throw inside an add. Stop; items added so far stay, summary below.
            stop = "exception";
            PatchGuard.Report($"{nameof(BatchFeeder)}.{nameof(TryRun)}", e);
        }
        finally
        {
            Muting = false;
            if (swapped)
            {
                target.SetAddEffects(savedEffects);
            }
        }

        // Loop ran: from here me always say "handled", even if the tail throw. Else caller let vanilla add one more.
        player.m_lastHoverInteractTime = Time.time;
        try
        {
            if (removedTotal > 0)
            {
                player.DoInteractAnimation(target.UseTarget.gameObject);
                var live = target.IsLive;
                var stillOwner = live && target.NView.IsOwner();
                if (live && !stillOwner)
                {
                    PendingAdds.Record(target, local, removedTotal, lockPrefab);
                }
                ShowSummary(player, target, inventory, live, stillOwner);
            }
            else if (!string.IsNullOrEmpty(LastMuted))
            {
                // Nothing added: show vanilla's own reason once (held skill line under it, if any).
                player.Message(MessageHud.MessageType.Center, WithSkillUp(LastMuted));
            }
            else if (!string.IsNullOrEmpty(HeldSkillUp))
            {
                player.Message(MessageHud.MessageType.Center, HeldSkillUp);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(BatchFeeder)}.{nameof(TryRun)} summary", e);
        }
        LastMuted = null;
        HeldSkillUp = null;

        Log.Debug($"Batch feed: {target.PieceName} {target.Kind} owner={(owner ? "you" : "other")} room={room} asked={amount} "
                  + $"added={removedTotal} presses={presses} stop={stop}");
        return true;
    }

    // Plain press or hold repeat on covered spot. Owner = pure vanilla. Non-owner and this press WOULD add:
    // block it when my pending adds say full (or other missile kind), else let vanilla run and remember it (state).
    // Never block a fire toggle or a cooking take-out. True = me blocked it (skip vanilla).
    internal static bool GuardPlainPress(Player player, in FeedTarget target, bool hold, bool alt, ref PressRecord state)
    {
        if (!target.IsLive || !target.NView.HasOwner())
        {
            return false;
        }
        if (target.NView.IsOwner())
        {
            PendingAdds.Drop(target);
            return false;
        }
        if ((hold && !target.ReactsToHold()) || target.WouldToggle(hold, alt) || !target.IsBatchable())
        {
            return false;
        }
        var inventory = player.GetInventory();
        if (inventory == null)
        {
            return false;
        }

        var local = target.ReadValue();
        string predicted = null;
        if (target.Kind == FeedKind.TurretAmmo)
        {
            var item = target.PredictAmmo(inventory);
            predicted = item != null && item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            var locked = PendingAdds.LockedPrefab(target, local);
            if (locked != null && predicted != null && predicted != locked)
            {
                ShowOtherAmmo(player, locked);
                player.m_lastHoverInteractTime = Time.time;
                return true;
            }
        }

        if (target.Room(PendingAdds.Effective(target, local), 1) == 0 && target.Room(local, 1) > 0)
        {
            player.Message(MessageHud.MessageType.Center, target.FullMessage());
            player.m_lastHoverInteractTime = Time.time;
            return true;
        }

        state = new PressRecord(target, inventory.CountItems(null, -1, matchWorldLevel: false), local, predicted);
        return false;
    }

    // After vanilla plain press (non-owner): what left the inventory went to the owner. Remember it.
    internal static void AfterPlainPress(Player player, in PressRecord state)
    {
        var target = state.Target;
        if (!target.IsLive || target.NView.IsOwner())
        {
            return;
        }
        var inventory = player.GetInventory();
        if (inventory == null)
        {
            return;
        }
        var removed = state.InventoryBefore - inventory.CountItems(null, -1, matchWorldLevel: false);
        if (removed > 0)
        {
            PendingAdds.Record(target, state.LocalBefore, removed, state.PredictedPrefab);
        }
    }

    // Owner uid on ZDO belong to a game still here? Server: its peer list. Client: server uid or a player in the player
    // list (server send new list right on disconnect). Dead owner waiting respawn (character id None) = false: single
    // vanilla press, safe. Run only on batch press, not every frame; foreach on List = no garbage.
    private static bool OwnerConnected(ZNetView nview)
    {
        var net = ZNet.instance;
        if (net == null)
        {
            return false;
        }
        var ownerId = nview.GetZDO().GetOwner();
        if (ownerId == ZDOMan.GetSessionID())
        {
            return true;
        }
        if (net.IsServer())
        {
            var peer = net.GetPeer(ownerId);
            return peer != null && peer.IsReady();
        }
        var server = net.GetServerPeer();
        if (server != null && server.m_uid == ownerId)
        {
            return true;
        }
        foreach (var info in net.GetPlayerList())
        {
            if (!info.m_characterID.IsNone() && info.m_characterID.UserID == ownerId)
            {
                return true;
            }
        }
        return false;
    }

    // Vanilla ballista words: "The ballista is already loaded with <missile>".
    private static void ShowOtherAmmo(Player player, string lockedPrefab)
    {
        var name = lockedPrefab;
        var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(lockedPrefab) : null;
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        if (drop != null)
        {
            name = drop.m_itemData.m_shared.m_name;
        }
        var loc = Localization.instance;
        player.Message(MessageHud.MessageType.Center, loc.Localize("$msg_turretotherammo") + loc.Localize(name));
    }

    // One center message for the whole press, vanilla words: "Added 5 Copper Ore (5/10)",
    // "Added 3 Copper Ore, 2 Tin Ore (5/10)", fires "Adding 5 Wood to the fire (8/10)".
    // Item names stay $tokens: MessageHud translate them in player language, like vanilla messages.
    private static void ShowSummary(Player player, in FeedTarget target, Inventory inventory, bool live, bool owner)
    {
        TakeCounts(inventory, CountsAfter, null);
        var items = new StringBuilder();
        foreach (var name in NamesInOrder)
        {
            CountsAfter.TryGetValue(name, out var left);
            var gone = CountsBefore[name] - left;
            if (gone <= 0)
            {
                continue;
            }
            if (items.Length > 0)
            {
                items.Append(", ");
            }
            items.Append(gone).Append(' ').Append(name);
        }

        // Owner: live ZDO already hold the adds (RPC ran right away). Non-owner: my pending model.
        var state = "";
        if (live)
        {
            var value = target.ReadValue();
            if (!owner)
            {
                value = PendingAdds.Effective(target, value);
            }
            state = " (" + target.Display(value) + ")";
        }
        var text = target.Kind == FeedKind.Fire
            ? Localization.instance.Localize("$msg_fireadding", items.ToString()) + state
            : "$msg_added " + items + state;
        player.Message(MessageHud.MessageType.Center, WithSkillUp(text));
    }

    // Held skill line go on second line. MessageHud localize whole text, so its $tokens still get translated.
    // Center message ignore icon: nothing lost.
    private static string WithSkillUp(string text) =>
        string.IsNullOrEmpty(HeldSkillUp) ? text : text + "\n" + HeldSkillUp;

    // Count per item name (all world levels), in inventory order when names list given.
    private static void TakeCounts(Inventory inventory, Dictionary<string, int> counts, List<string> names)
    {
        counts.Clear();
        names?.Clear();
        foreach (var item in inventory.GetAllItems())
        {
            var name = item.m_shared.m_name;
            if (counts.TryGetValue(name, out var count))
            {
                counts[name] = count + item.m_stack;
            }
            else
            {
                counts[name] = item.m_stack;
                names?.Add(name);
            }
        }
    }
}

// Me = what the Player.Interact prefix hand to its postfix for a plain press to remember (non-owner only).
internal readonly struct PressRecord
{
    internal readonly bool Active;
    internal readonly FeedTarget Target;
    internal readonly int InventoryBefore;
    internal readonly float LocalBefore;
    internal readonly string PredictedPrefab;

    internal PressRecord(in FeedTarget target, int inventoryBefore, float localBefore, string predictedPrefab)
    {
        Active = true;
        Target = target;
        InventoryBefore = inventoryBefore;
        LocalBefore = localBefore;
        PredictedPrefab = predictedPrefab;
    }
}
