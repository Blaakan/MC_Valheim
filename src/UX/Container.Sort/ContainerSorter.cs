using System;
using System.Collections.Generic;
using System.Globalization;
using MC.Shared;
using UnityEngine;

namespace MC.UX.ContainerSortMod;

// Me = the sort. Only for the chest open in the container panel, only when this client own it at click time AND local
// copy same as saved data. Owning is needed but not enough: game skip reload while chest in use, so after ship
// hand-off (or open in same second other player changed it) old copy can stay. Saving old copy = dupe or lose items.
// Me never use Inventory.AddItem/RemoveItem/MoveItemToThis: they save every call, move units one by one and can
// refuse a slot. Me write m_gridPos / m_stack on the live items, then ONE Inventory.Changed() = one save for all.
internal static class ContainerSorter
{
    private const CompareOptions NameOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    // Same result on every machine whatever OS culture; "Epee" with accent sort with "E".
    private static readonly CompareInfo Names = CultureInfo.InvariantCulture.CompareInfo;

    private static readonly KeyComparer[] Comparers =
    {
        new KeyComparer(SortCriterion.Name), new KeyComparer(SortCriterion.Type), new KeyComparer(SortCriterion.Biome),
    };

    internal const string StaleMessage = "This container changed elsewhere. Close it and open it again to sort.";

    private static bool _warnedTooMany;

    private struct SortKey
    {
        internal ItemDrop.ItemData Item;
        internal int Biome;
        internal int Type;
        internal int Sub;
        internal string Name;
        internal string Prefab;
        internal int Slot;
        internal int ListIndex;
    }

    // What one click did. Written = something on the items changed (then Changed() must run, even after an error).
    private sealed class Run
    {
        internal bool Written;
        internal int Moved;
        internal int Merged;
    }

    // Tombstone no: Take all there give the player back own slots and hotbar; sort would scramble them.
    internal static bool IsEligible(Container container) =>
        container != null && container.GetInventory() != null && container.GetComponent<TombStone>() == null;

    // Sort button. Same guards as vanilla Take all / Place stacks, plus ownership, fresh local copy and split dialog.
    internal static void TrySortOpenContainer()
    {
        var gui = InventoryGui.instance;
        var player = Player.m_localPlayer;
        if (gui == null || player == null || player.IsTeleporting())
        {
            return;
        }
        if (gui.m_splitDialog != null && gui.m_splitDialog.IsActive)
        {
            return;
        }
        var container = gui.m_currentContainer;
        if (container == null)
        {
            return;
        }
        if (!container.IsOwner())
        {
            // Panel shown but chest owned by other client (MultiUserChest-like mod, ship owner changed): hands off.
            Log.Debug("Sort skipped: this client does not own the open container.");
            return;
        }
        if (!IsEligible(container))
        {
            return;
        }
        if (!LocalCopyCurrent(container))
        {
            // Game did not reload chest (in-use flag stuck after ship hand-off, or opened same second other player
            // changed it). Me never save old copy over newer data: that dupe or lose items.
            Log.Debug("Sort skipped: local copy of the container is out of date.");
            player.Message(MessageHud.MessageType.Center, StaleMessage);
            return;
        }

        // Cancel drag first, like vanilla buttons. Dragged item never left its slot: nothing lost.
        gui.SetupDragItem(null, null, 1);

        var inventory = container.GetInventory();
        var criterion = Plugin.SortBy.Value;
        var run = new Run();
        try
        {
            Sort(inventory, criterion, Plugin.MergeStacks.Value, run);
        }
        finally
        {
            // Even after an error: stacks only moved between identical items, state valid. Save what was written.
            if (run.Written)
            {
                inventory.Changed();
                gui.m_moveItemEffects.Create(player.transform.position, Quaternion.identity);
            }
        }
        Log.Debug($"Sorted {inventory.GetName()} by {criterion}: {inventory.NrOfItems()} items, {run.Moved} moved, "
                  + $"{run.Merged} stacks merged{(run.Written ? "" : " (already sorted, nothing saved)")}.");
    }

    // Local items = what ZDO hold? First exact bytes (true right after any own save). Else read ZDO bytes back through
    // vanilla Load + Save once (old item format, durability round trip, stack clamp): same content never refused.
    // Scratch inventory: nobody listen to its m_onChanged, not player inventory: its Load never save, never show
    // cheated message. Only make and destroy item prefabs, like vanilla Container.Load. Click only, never per frame.
    private static bool LocalCopyCurrent(Container container)
    {
        if (container.m_nview == null || !container.m_nview.IsValid())
        {
            return false;
        }
        var stored = container.m_nview.GetZDO().GetByteArray(ZDOVars.s_items);
        if (stored == null)
        {
            return true; // Nothing ever saved: nothing newer to overwrite.
        }
        var inventory = container.GetInventory();
        var pkg = new ZPackage();
        inventory.Save(pkg);
        var local = pkg.GetArray();
        if (SameBytes(local, stored))
        {
            return true;
        }
        var scratch = new Inventory(inventory.GetName(), null, inventory.GetWidth(), inventory.GetHeight());
        scratch.Load(new ZPackage(stored));
        var normalized = new ZPackage();
        scratch.Save(normalized);
        return SameBytes(local, normalized.GetArray());
    }

    private static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }
        return true;
    }

    // Top left to bottom right, row by row, no gap. Live list reordered too (saved data in slot order).
    private static void Sort(Inventory inventory, SortCriterion criterion, bool merge, Run run)
    {
        var items = inventory.GetAllItems();
        var width = inventory.GetWidth();
        var height = inventory.GetHeight();
        if (items.Count == 0 || width <= 0 || height <= 0)
        {
            return;
        }
        if (merge)
        {
            MergeStacks(items, width, run);
        }

        var count = items.Count;
        if (count > width * height)
        {
            // Only broken old data. Never lose an item: no sort.
            if (!_warnedTooMany)
            {
                _warnedTooMany = true;
                Log.Warning($"'{inventory.GetName()}' holds more items ({count}) than slots ({width}x{height}); sorting skipped.");
            }
            return;
        }

        // All keys first (can throw: localization, biome index), write only after.
        var byBiome = criterion == SortCriterion.Biome;
        var keys = new SortKey[count];
        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            TypeGroups.Rank(item.m_shared, out var type, out var sub);
            keys[i] = new SortKey
            {
                Item = item,
                Biome = byBiome ? (int)BiomeIndex.Get(item) : 0,
                Type = type,
                Sub = sub,
                Name = LocalName(item),
                Prefab = ItemPrefab.Name(item) ?? "",
                Slot = SlotIndex(item.m_gridPos, width),
                ListIndex = i,
            };
        }
        var comparer = (int)criterion;
        if (comparer < 0 || comparer >= Comparers.Length)
        {
            comparer = (int)SortCriterion.Type;
        }
        Array.Sort(keys, 0, count, Comparers[comparer]);

        for (var i = 0; i < count; i++)
        {
            var pos = new Vector2i(i % width, i / width);
            var item = keys[i].Item;
            if (item.m_gridPos != pos)
            {
                run.Written = true;
                run.Moved++;
                item.m_gridPos = pos;
            }
        }

        // List order alone invisible: no save for it.
        items.Clear();
        for (var i = 0; i < count; i++)
        {
            items.Add(keys[i].Item);
        }
    }

    // Partial stacks of IDENTICAL items fill up, in slot order (top-left stacks fill first). Emptied items leave the
    // list. Stack never go above max; stack already above it (stack-size mod removed) never get more, but can still
    // give items to a partial stack.
    private static void MergeStacks(List<ItemDrop.ItemData> items, int width, Run run)
    {
        var count = items.Count;
        if (count < 2)
        {
            return;
        }
        var order = new int[count];
        var slots = new int[count];
        for (var i = 0; i < count; i++)
        {
            order[i] = i;
            slots[i] = SlotIndex(items[i].m_gridPos, width);
        }
        Array.Sort(order, (x, y) =>
        {
            var c = slots[x].CompareTo(slots[y]);
            return c != 0 ? c : x.CompareTo(y);
        });

        List<ItemDrop.ItemData> emptied = null;
        for (var ia = 0; ia < count; ia++)
        {
            var a = items[order[ia]];
            if (a.m_shared == null)
            {
                continue;
            }
            var max = a.m_shared.m_maxStackSize;
            if (max <= 1 || a.m_stack <= 0 || a.m_stack >= max)
            {
                continue;
            }
            for (var ib = ia + 1; ib < count && a.m_stack < max; ib++)
            {
                var b = items[order[ib]];
                if (b.m_stack <= 0 || !CanMerge(a, b))
                {
                    continue;
                }
                var move = Math.Min(max - a.m_stack, b.m_stack);
                run.Written = true;
                a.m_stack += move;
                b.m_stack -= move;
                if (b.m_stack == 0)
                {
                    emptied ??= new List<ItemDrop.ItemData>();
                    emptied.Add(b);
                    run.Merged++;
                }
            }
        }
        if (emptied != null)
        {
            // Only items me emptied (reference compare): a zero stack from old data stay where it is.
            items.RemoveAll(item => item.m_stack <= 0 && emptied.Contains(item));
        }
    }

    // Merge only when both would save the same apart from stack and position: nothing (flags, crafter, custom data
    // of other mods, cheated mark) can get lost or spread. Stricter than vanilla merges.
    private static bool CanMerge(ItemDrop.ItemData a, ItemDrop.ItemData b)
    {
        if (a.m_shared.m_maxStackSize <= 1 || b.m_shared == null)
        {
            return false;
        }
        return string.Equals(a.m_shared.m_name, b.m_shared.m_name, StringComparison.Ordinal)
               && string.Equals(ItemPrefab.Name(a), ItemPrefab.Name(b), StringComparison.Ordinal)
               && a.m_quality == b.m_quality
               && a.m_worldLevel == b.m_worldLevel
               && a.m_variant == b.m_variant
               && a.m_crafterID == b.m_crafterID
               && string.Equals(a.m_crafterName ?? "", b.m_crafterName ?? "", StringComparison.Ordinal)
               // Save keep durability as int (d * 100): compare that, not the float (reloaded item differ in bits).
               && (int)(a.m_durability * 100f) == (int)(b.m_durability * 100f)
               && a.m_pickedUp == b.m_pickedUp
               && a.m_cheated == b.m_cheated
               && a.m_equipped == b.m_equipped
               && CustomDataEqual(a.m_customData, b.m_customData);
    }

    // Null and empty same. Else same keys, same values (ordinal).
    private static bool CustomDataEqual(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        var countA = a != null ? a.Count : 0;
        var countB = b != null ? b.Count : 0;
        if (countA != countB)
        {
            return false;
        }
        if (countA == 0)
        {
            return true;
        }
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var value) || !string.Equals(kv.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private static string LocalName(ItemDrop.ItemData item)
    {
        var token = item.m_shared != null ? item.m_shared.m_name : null;
        if (string.IsNullOrEmpty(token))
        {
            return "";
        }
        return Localization.instance.Localize(token) ?? "";
    }

    private static int SlotIndex(Vector2i pos, int width) => pos.y * width + pos.x;

    // First non-zero wins: [biome] [type, sub] name, quality desc, stack desc, prefab, durability desc, variant, old
    // slot, old list index. Total order, so sort again = same order = nothing move.
    private sealed class KeyComparer : IComparer<SortKey>
    {
        private readonly SortCriterion _criterion;

        internal KeyComparer(SortCriterion criterion)
        {
            _criterion = criterion;
        }

        public int Compare(SortKey a, SortKey b)
        {
            int c;
            if (_criterion == SortCriterion.Biome && (c = a.Biome.CompareTo(b.Biome)) != 0)
            {
                return c;
            }
            if (_criterion != SortCriterion.Name)
            {
                if ((c = a.Type.CompareTo(b.Type)) != 0)
                {
                    return c;
                }
                if ((c = a.Sub.CompareTo(b.Sub)) != 0)
                {
                    return c;
                }
            }
            if ((c = Names.Compare(a.Name, b.Name, NameOptions)) != 0)
            {
                return c;
            }
            if ((c = b.Item.m_quality.CompareTo(a.Item.m_quality)) != 0)
            {
                return c;
            }
            if ((c = b.Item.m_stack.CompareTo(a.Item.m_stack)) != 0)
            {
                return c;
            }
            if ((c = string.CompareOrdinal(a.Prefab, b.Prefab)) != 0)
            {
                return c;
            }
            if ((c = b.Item.m_durability.CompareTo(a.Item.m_durability)) != 0)
            {
                return c;
            }
            if ((c = a.Item.m_variant.CompareTo(b.Item.m_variant)) != 0)
            {
                return c;
            }
            if ((c = a.Slot.CompareTo(b.Slot)) != 0)
            {
                return c;
            }
            return a.ListIndex.CompareTo(b.ListIndex);
        }
    }
}
