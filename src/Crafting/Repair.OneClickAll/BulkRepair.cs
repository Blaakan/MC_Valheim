using System;
using System.Collections.Generic;
using MC.Shared;

namespace MC.Crafting.RepairOneClickAllMod;

// Vanilla repair button fix ONE item per click (InventoryGui.OnRepairPressed -> one vanilla repair).
// Me press vanilla repair again and again, right after the real click, until nothing left for this station.
// Me never write durability myself: vanilla still do skill gain, station rules (CanRepair), nocost, roof/fire check,
// and other mods' patches on the vanilla repair (repair cost mods, Crossbow Stays Loaded) still run once per item.
// Careful: some cost mods read the stack trace and look for vanilla method names. No name in this mod may contain
// the name of the vanilla repair method or of the "have repairable items" method.
internal static class BulkRepair
{
    // Summary list this many item names, rest become "+N".
    private const int NamesInSummary = 3;

    // Vanilla say this (not localized) when a press find nothing. Never a real reason for a block.
    private const string VanillaNothingLeft = "No more item to repair";

    private static readonly EffectList NoEffects = new EffectList();

    // True only while me press vanilla again. MessageHudPatches swallow center messages then (one per item = flicker).
    internal static bool Muting;

    // Last center message swallowed. When a press fix nothing, this is why (cost mod: "not enough coins").
    internal static string LastMuted;

    // Worn items and their durability right before the click. Me compare after, to know what got fixed.
    internal sealed class Snapshot
    {
        internal readonly List<ItemDrop.ItemData> Items = new List<ItemDrop.ItemData>();
        internal readonly List<float> Durability = new List<float>();
    }

    // Before click. Null = zero or one worn item: vanilla click already do everything, me stay out.
    internal static Snapshot Take()
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            return null;
        }

        // Own list. Never touch InventoryGui.m_tempWornItems: vanilla clear and refill it on every call.
        var worn = new List<ItemDrop.ItemData>();
        player.GetInventory().GetWornItems(worn);
        if (worn.Count < 2)
        {
            return null;
        }

        var snapshot = new Snapshot();
        foreach (var item in worn)
        {
            snapshot.Items.Add(item);
            snapshot.Durability.Add(item.m_durability);
        }
        return snapshot;
    }

    // After click. Vanilla (or another mod) already did its one press.
    internal static void Finish(InventoryGui gui, Snapshot snapshot)
    {
        var player = Player.m_localPlayer;
        if (snapshot == null || player == null)
        {
            return;
        }

        // Click fix nothing = blocked (no station, station not usable, cost mod say no...). Next press block too: stop.
        if (CountFixed(snapshot) == 0)
        {
            return;
        }

        var station = player.GetCurrentCraftingStation();
        EffectList savedEffects = null;
        var effectsSwapped = false;
        string blockReason = null;
        var extra = 0;
        try
        {
            // One repair sound per click: the real click already played it. Station stay quiet for the rest.
            // Only vanilla repair read this field, and me put it back below, same frame.
            if (station != null)
            {
                savedEffects = station.m_repairItemDoneEffects;
                station.m_repairItemDoneEffects = NoEffects;
                effectsSwapped = true;
            }
            Muting = true;

            // Each good press fix at least one item, so never more presses than worn items: no endless loop.
            for (var i = 1; i < snapshot.Items.Count && gui.HaveRepairableItems(); i++)
            {
                var before = TotalDurability(snapshot);
                LastMuted = null;
                gui.RepairOneItem();
                // Vanilla click do this after each repair. Cost mods refresh their own list here too.
                gui.UpdateRepair();
                if (TotalDurability(snapshot) <= before)
                {
                    // Press fix nothing: something block it (cost mod, no more materials). Stop, else loop forever.
                    blockReason = LastMuted;
                    break;
                }
                extra++;
            }
        }
        catch (Exception e)
        {
            // Other mod throw in vanilla repair. Stop pressing; items fixed so far stay fixed.
            PatchGuard.Report($"{nameof(BulkRepair)}.{nameof(Finish)}", e);
        }
        finally
        {
            Muting = false;
            LastMuted = null;
            if (effectsSwapped)
            {
                station.m_repairItemDoneEffects = savedEffects;
            }
        }

        if (extra > 0)
        {
            // Vanilla rebuild craft panel after first item only: upgrade list durability bars stale. Rebuild again.
            gui.UpdateCraftingPanel();
            ShowSummary(player, snapshot);
        }

        if (!string.IsNullOrEmpty(blockReason) && blockReason != VanillaNothingLeft)
        {
            // Center is busy with summary. Reason go top-left so player know why some items stay worn.
            player.Message(MessageHud.MessageType.TopLeft, blockReason);
        }

        Log.Debug($"Repair click: {CountFixed(snapshot)} item(s) repaired ({extra} extra press(es)){(blockReason != null ? ", stopped: " + blockReason : "")}.");
    }

    // One center message for the whole click, same words as vanilla: "Repaired <item>, <item>, <item> +N".
    // Item names stay $tokens, so MessageHud translate them in player language, like vanilla message.
    private static void ShowSummary(Player player, Snapshot snapshot)
    {
        var names = new List<string>(NamesInSummary);
        var count = 0;
        for (var i = 0; i < snapshot.Items.Count; i++)
        {
            if (snapshot.Items[i].m_durability > snapshot.Durability[i])
            {
                count++;
                if (names.Count < NamesInSummary)
                {
                    names.Add(snapshot.Items[i].m_shared.m_name);
                }
            }
        }

        var words = string.Join(", ", names);
        if (count > names.Count)
        {
            words += " +" + (count - names.Count);
        }
        player.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_repaired", words));
    }

    private static int CountFixed(Snapshot snapshot)
    {
        var count = 0;
        for (var i = 0; i < snapshot.Items.Count; i++)
        {
            if (snapshot.Items[i].m_durability > snapshot.Durability[i])
            {
                count++;
            }
        }
        return count;
    }

    private static float TotalDurability(Snapshot snapshot)
    {
        var total = 0f;
        foreach (var item in snapshot.Items)
        {
            total += item.m_durability;
        }
        return total;
    }
}
