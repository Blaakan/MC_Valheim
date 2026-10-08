#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MC.Shared;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Debug build only. Me = self tests of starred idols outside the Forge:
//   forge.icons.rows        star rows as pixels: 1, 2, 3 gold stars top right; lower row for requirement slots
//   forge.grid              inventory and chest slots, drag icon: stars, no quality number (other items keep theirs)
//   forge.stacks            levels never merge: drag both ways, split, pickup, chest; chest saved and read again
//   forge.icons.empty-copy  GPU copy come back empty: plain icon on every call after the first, stars a second later
//   forge.bug.empty-copy-icon  (real bug, fail until mod fixed) the very first call then: plain icon, never no icon
internal static partial class SelfTests
{
    private const string IconRowsName = "forge.icons.rows";
    private const string GridName = "forge.grid";
    private const string StacksName = "forge.stacks";
    private const string EmptyCopyName = "forge.icons.empty-copy";
    private const string BugEmptyCopyName = "forge.bug.empty-copy-icon";

    private static bool IsGold(Color p) => p.a > 0.95f && p.r > 0.7f && p.g > 0.35f && p.b < 0.5f;

    private static bool Is(ItemDrop.ItemData item, string name, int quality, int stack) =>
        item != null && item.m_shared.m_name == name && item.m_quality == quality && item.m_stack == stack;

    private static int Stacks(Inventory inventory, string name, int quality) =>
        inventory.GetAllItems().Count(i => i.m_shared.m_name == name && i.m_quality == quality);

    // Me put item straight in a slot of an inventory (own stack, no merge), like a saved chest hold it.
    private static ItemDrop.ItemData Put(Inventory inventory, string prefab, int quality, int stack, int x, int y)
    {
        var drop = Prefab(prefab);
        if (drop == null)
        {
            return null;
        }
        var item = drop.m_itemData.Clone();
        item.m_quality = quality;
        item.m_stack = stack;
        item.m_dropPrefab = drop.gameObject;
        item.m_worldLevel = Game.m_worldLevel;
        inventory.AddItem(item, stack, x, y);
        return inventory.GetItemAt(x, y);
    }

    // Idol made the way game load one, but its own item data still say "max quality 1": how an idol loaded before
    // mod turned on look. Null = this game share that data with the prefab (me never touch that).
    private static ItemDrop.ItemData OldIdol(string prefab, int quality)
    {
        var go = ObjectDB.instance.GetItemPrefab(prefab);
        if (go == null)
        {
            return null;
        }
        GameObject copy;
        ZNetView.m_forceDisableInit = true;
        try
        {
            copy = Object.Instantiate(go);
        }
        finally
        {
            ZNetView.m_forceDisableInit = false;
        }
        var data = copy.GetComponent<ItemDrop>().m_itemData;
        Object.Destroy(copy);
        if (ReferenceEquals(data.m_shared, go.GetComponent<ItemDrop>().m_itemData.m_shared))
        {
            return null;
        }
        data.m_shared.m_maxQuality = 1;
        data.m_quality = quality;
        data.m_stack = 1;
        data.m_worldLevel = Game.m_worldLevel;
        return data;
    }

    // ---------- forge.icons.rows (T05) ----------

    private static IEnumerator RunIconRows()
    {
        var c = new Checks(IconRowsName);
        var keep = StarIcons.KeepReadable;
        try
        {
            StarIcons.Clear();
            StarIcons.KeepReadable = true;
            var icon = PlainIconOf(SilverIdol);
            for (var stars = 1; stars <= 3 && icon != null; stars++)
            {
                var top = StarIcons.Get(icon, stars);
                var middle = StarIcons.Get(icon, stars, true);
                c.Check(StarIcons.IsStarSprite(top) && StarIcons.IsStarSprite(middle) && top != middle, $"{stars} star(s): two layouts, both starred sprites");
                if (top == null || middle == null || !top.texture.isReadable || !middle.texture.isReadable)
                {
                    c.Check(false, $"{stars} star(s): made textures must stay readable while KeepReadable");
                    continue;
                }
                // Same numbers as StarIcons.DrawStars.
                var w = top.texture.width;
                var h = top.texture.height;
                var outer = Mathf.Max(4f, Mathf.Min(w, h) * 0.12f);
                var rim = Mathf.Max(1f, outer * 0.22f);
                var step = outer * 1.95f;
                var right = w - 1f - rim - outer * 0.951f;
                var topRow = Mathf.RoundToInt(h - 1f - rim - outer);
                var middleRow = Mathf.RoundToInt(h * 0.45f);
                int goldTop = 0, goldMiddle = 0;
                for (var s = 0; s < stars; s++)
                {
                    var x = Mathf.Max(0, Mathf.RoundToInt(right - s * step));
                    goldTop += IsGold(top.texture.GetPixel(x, topRow)) ? 1 : 0;
                    goldMiddle += IsGold(middle.texture.GetPixel(x, middleRow)) ? 1 : 0;
                }
                var before = Mathf.Max(0, Mathf.RoundToInt(right - stars * step));
                c.Check(goldTop == stars && !IsGold(top.texture.GetPixel(before, topRow)),
                    $"{stars} star(s): exactly {stars} gold star(s) in a row ({goldTop} found, none one place further left)");
                c.Check(right > w * 0.7f && topRow > h * 0.7f, $"{stars} star(s): the row ends at the top right of the icon (x {right:0} of {w}, y {topRow} of {h})");
                c.Check(goldMiddle == stars && !IsGold(middle.texture.GetPixel(Mathf.RoundToInt(right), topRow)),
                    $"{stars} star(s), requirement slot layout: the stars sit lower (row {middleRow} of {h}), none at the top right ({goldMiddle} found)");
            }
            c.Check(icon != null, "the Silver Battle Idol has an icon");
            c.Report();
        }
        finally
        {
            StarIcons.KeepReadable = keep;
            StarIcons.Clear();
        }
        yield break;
    }

    // ---------- forge.grid (T05) ----------

    private static IEnumerator RunGrid()
    {
        var rig = new Rig(GridName);
        var c = rig.C;
        try
        {
            var plainIcon = PlainIconOf(SilverIdol);
            var idols = new[]
            {
                new KeyValuePair<ItemDrop.ItemData, int>(rig.Give(SilverIdol, 2, 1), 0),
                new KeyValuePair<ItemDrop.ItemData, int>(rig.Give(SilverIdol, 1, 2), 1),
                new KeyValuePair<ItemDrop.ItemData, int>(rig.Give(SilverIdol, 1, 3), 2),
                new KeyValuePair<ItemDrop.ItemData, int>(rig.Give(SilverIdol, 1, 4), 3),
            };
            var axe = rig.Give("AxeStone", 1, 2);
            var gui = rig.Gui;
            yield return rig.OpenInventory();
            foreach (var pair in idols)
            {
                var slot = rig.Element(gui.m_playerGrid, pair.Key);
                var want = pair.Value > 0 ? StarIcons.Get(plainIcon, pair.Value) : plainIcon;
                c.Check(slot != null && slot.m_icon.enabled && slot.m_icon.sprite == want && StarIcons.IsStarSprite(slot.m_icon.sprite) == (pair.Value > 0),
                    $"inventory slot of the {pair.Value}-star idol shows {pair.Value} star(s): {Describe(slot != null ? slot.m_icon.sprite : null)}");
                c.Check(slot != null && !slot.m_quality.enabled, $"inventory slot of the {pair.Value}-star idol shows no quality number");
            }
            var axeSlot = rig.Element(gui.m_playerGrid, axe);
            c.Check(axeSlot != null && axeSlot.m_quality.enabled && axeSlot.m_quality.text == "2", "another item (level 2 Stone axe) keeps its quality number");

            // While player drag it.
            var two = idols[2].Key;
            if (two != null)
            {
                gui.SetupDragItem(two, rig.Inv, 1);
                yield return Frames(2);
                var dragIcon = gui.m_dragGo != null ? gui.m_dragGo.transform.Find("icon")?.GetComponent<Image>() : null;
                c.Check(dragIcon != null && dragIcon.sprite == StarIcons.Get(plainIcon, 2), $"the dragged icon of a 2-star idol shows 2 stars: {Describe(dragIcon != null ? dragIcon.sprite : null)}");
                gui.SetupDragItem(null, null, 1);
            }

            // In a chest.
            var chest = rig.SpawnChest();
            yield return new WaitForSeconds(0.5f);
            if (chest != null && chest.GetInventory() != null)
            {
                var inside = new[]
                {
                    new KeyValuePair<ItemDrop.ItemData, int>(Put(chest.GetInventory(), SilverIdol, 1, 1, 0, 0), 0),
                    new KeyValuePair<ItemDrop.ItemData, int>(Put(chest.GetInventory(), SilverIdol, 2, 1, 1, 0), 1),
                    new KeyValuePair<ItemDrop.ItemData, int>(Put(chest.GetInventory(), SilverIdol, 4, 1, 2, 0), 3),
                };
                yield return rig.OpenInventory(chest);
                c.Check(ReferenceEquals(gui.m_currentContainer, chest) && gui.m_container.gameObject.activeSelf, "the chest is open");
                foreach (var pair in inside)
                {
                    var slot = rig.Element(gui.ContainerGrid, pair.Key);
                    var want = pair.Value > 0 ? StarIcons.Get(plainIcon, pair.Value) : plainIcon;
                    c.Check(slot != null && slot.m_icon.enabled && slot.m_icon.sprite == want && !slot.m_quality.enabled,
                        $"chest slot of the {pair.Value}-star idol shows {pair.Value} star(s) and no quality number: {Describe(slot != null ? slot.m_icon.sprite : null)}");
                }
                SelfTest.Screenshot(GridName, "inventory-and-chest");
                yield return null;
                yield return null;
            }
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.stacks (T07, M03) ----------

    private static IEnumerator RunStacks()
    {
        var rig = new Rig(StacksName);
        var c = rig.C;
        try
        {
            var name = NameOf(SilverIdol);
            var a = rig.Give(SilverIdol, 2, 1);
            var b = rig.Give(SilverIdol, 1, 3);
            if (a == null || b == null || name == null)
            {
                c.Report();
                yield break;
            }
            var gui = rig.Gui;
            var inv = rig.Inv;
            var grid = gui.m_playerGrid;
            yield return rig.OpenInventory();
            var first = a.m_gridPos;
            var second = b.m_gridPos;

            // Plain stack dragged on the 2-star idol.
            gui.OnSelectedItem(grid, a, first, InventoryGrid.Modifier.Select);
            c.Check(gui.m_dragGo != null && ReferenceEquals(gui.m_dragItem, a), "a click on the plain stack starts the drag");
            gui.OnSelectedItem(grid, b, second, InventoryGrid.Modifier.Select);
            yield return null;
            c.Check(Is(inv.GetItemAt(second.x, second.y), name, 1, 2) && Is(inv.GetItemAt(first.x, first.y), name, 3, 1) && gui.m_dragGo == null,
                "plain idols dropped on a 2-star idol: they swap places");
            c.Check(rig.Has(SilverIdol, 1) == 2 && rig.Has(SilverIdol, 3) == 1 && Stacks(inv, name, 1) == 1 && Stacks(inv, name, 3) == 1, "nothing stacked together");

            // The other way.
            gui.OnSelectedItem(grid, inv.GetItemAt(first.x, first.y), first, InventoryGrid.Modifier.Select);
            gui.OnSelectedItem(grid, inv.GetItemAt(second.x, second.y), second, InventoryGrid.Modifier.Select);
            yield return null;
            c.Check(Is(inv.GetItemAt(first.x, first.y), name, 1, 2) && Is(inv.GetItemAt(second.x, second.y), name, 3, 1) && gui.m_dragGo == null,
                "the 2-star idol dropped on the plain stack: they swap back");

            // One idol split off plain stack and dropped on the 2-star idol: game refuse.
            var plain = inv.GetItemAt(first.x, first.y);
            var star = inv.GetItemAt(second.x, second.y);
            if (plain != null && star != null)
            {
                gui.SetupDragItem(plain, inv, 1);
                gui.OnSelectedItem(grid, star, second, InventoryGrid.Modifier.Select);
                yield return null;
                c.Check(plain.m_stack == 2 && Is(star, name, 3, 1) && rig.Has(SilverIdol, 1) == 2 && rig.Has(SilverIdol, 3) == 1,
                    "one plain idol (split) dropped on the 2-star idol: refused, nothing changes");
                gui.SetupDragItem(null, null, 1);
            }

            // Idol with old own data (max quality 1, from before mod on): healed as soon as grid show it.
            var old = OldIdol(SilverIdol, 2);
            c.Check(old != null && old.m_shared.m_maxQuality == 1, "test idol with its own old item data (max quality 1)");
            if (old != null && inv.AddItem(old))
            {
                yield return Frames(2);
                c.Check(inv.ContainsItem(old) && old.m_shared.m_maxQuality == 4, $"an idol from before the mod was on gets its levels back when shown (max quality {old.m_shared.m_maxQuality})");
                var oldPos = old.m_gridPos;
                plain = inv.GetItemAt(first.x, first.y);
                gui.OnSelectedItem(grid, plain, first, InventoryGrid.Modifier.Select);
                gui.OnSelectedItem(grid, old, oldPos, InventoryGrid.Modifier.Select);
                yield return null;
                c.Check(Is(inv.GetItemAt(oldPos.x, oldPos.y), name, 1, 2) && Is(inv.GetItemAt(first.x, first.y), name, 2, 1),
                    "plain idols dropped on that old 1-star idol: swap, no merge");
                first = oldPos;
            }
            gui.SetupDragItem(null, null, 1);

            // Pick up from ground.
            var dropped = rig.Drop(SilverIdol, 3);
            yield return null;
            c.Check(dropped != null && rig.P.Pickup(dropped.gameObject, false, false), "pickup of a 2-star idol from the ground");
            yield return null;
            c.Check(rig.Has(SilverIdol, 1) == 2 && rig.Has(SilverIdol, 2) == 1 && rig.Has(SilverIdol, 3) == 2 && Stacks(inv, name, 3) == 1,
                $"picked up: it joins the 2-star stack only ({rig.Has(SilverIdol, 1)} plain, {rig.Has(SilverIdol, 2)} one-star, {rig.Has(SilverIdol, 3)} two-star)");

            // Chest: quick move, drag on other level, then what chest saved.
            var chest = rig.SpawnChest();
            yield return new WaitForSeconds(0.5f);
            if (chest == null || chest.GetInventory() == null)
            {
                c.Report();
                yield break;
            }
            var box = chest.GetInventory();
            Put(box, SilverIdol, 1, 1, 0, 0);
            yield return rig.OpenInventory(chest);
            c.Check(ReferenceEquals(gui.m_currentContainer, chest), "the chest is open");
            var stars = rig.Find(SilverIdol, 3);
            gui.OnSelectedItem(grid, stars, stars != null ? stars.m_gridPos : default, InventoryGrid.Modifier.Move);
            yield return null;
            c.Check(box.CountItems(name, 1, false) == 1 && box.CountItems(name, 3, false) == 2 && Stacks(box, name, 3) == 1 && rig.Has(SilverIdol, 3) == 0,
                "2-star idols quick-moved to a chest that holds plain ones: their own stack");
            var mine = rig.Find(SilverIdol, 1);
            var theirs = box.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == name && i.m_quality == 3);
            if (mine != null && theirs != null)
            {
                var minePos = mine.m_gridPos;
                var theirPos = theirs.m_gridPos;
                gui.OnSelectedItem(grid, mine, minePos, InventoryGrid.Modifier.Select);
                gui.OnSelectedItem(gui.ContainerGrid, theirs, theirPos, InventoryGrid.Modifier.Select);
                yield return null;
                c.Check(Is(box.GetItemAt(theirPos.x, theirPos.y), name, 1, 2) && Is(inv.GetItemAt(minePos.x, minePos.y), name, 3, 2),
                    "plain idols dragged onto the chest's 2-star stack: the stacks swap between chest and inventory");
            }
            gui.SetupDragItem(null, null, 1);

            // Every level into chest, then chest read again from what it saved (what a relog load).
            foreach (var quality in new[] { 3, 2 })
            {
                var item = rig.Find(SilverIdol, quality);
                if (item != null)
                {
                    gui.OnSelectedItem(grid, item, item.m_gridPos, InventoryGrid.Modifier.Move);
                }
            }
            yield return null;
            c.Check(box.CountItems(name, 1, false) == 3 && box.CountItems(name, 2, false) == 1 && box.CountItems(name, 3, false) == 2 && rig.Has(SilverIdol) == 0,
                $"chest holds 3 plain, 1 one-star, 2 two-star ({box.CountItems(name, 1, false)}/{box.CountItems(name, 2, false)}/{box.CountItems(name, 3, false)})");
            var saved = chest.m_nview.GetZDO().GetByteArray(ZDOVars.s_items);
            var copy = new Inventory("copy", null, box.GetWidth(), box.GetHeight());
            if (saved != null)
            {
                copy.Load(new ZPackage(saved));
            }
            c.Check(saved != null && copy.CountItems(name, 1, false) == 3 && copy.CountItems(name, 2, false) == 1 && copy.CountItems(name, 3, false) == 2,
                "the chest's saved data keeps every idol at its level");
            yield return rig.CloseWindow();
            chest.m_lastRevision = uint.MaxValue;
            var reloaded = chest.Load();
            c.Check(reloaded && box.CountItems(name, 1, false) == 3 && box.CountItems(name, 2, false) == 1 && box.CountItems(name, 3, false) == 2
                    && box.GetAllItems().All(i => i.m_shared.m_maxQuality == 4),
                $"chest read again from its saved data: levels kept ({box.CountItems(name, 1, false)}/{box.CountItems(name, 2, false)}/{box.CountItems(name, 3, false)}), reloaded {reloaded}");
            inv.MoveAll(box);
            c.Check(rig.Has(SilverIdol, 1) == 3 && rig.Has(SilverIdol, 2) == 1 && rig.Has(SilverIdol, 3) == 2 && box.NrOfItems() == 0,
                $"taken out again: levels kept, never merged ({rig.Has(SilverIdol, 1)}/{rig.Has(SilverIdol, 2)}/{rig.Has(SilverIdol, 3)})");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.icons.empty-copy (T22; the very first call: forge.bug.empty-copy-icon) ----------

    // Game window minimized: GPU copy of icon come back empty. Mod must give plain icon until a later try work.
    // Me check here every call after the first one, and the later try. First call = the bug test below.
    private static IEnumerator RunEmptyCopy()
    {
        var c = new Checks(EmptyCopyName);
        try
        {
            var icon = PlainIconOf(SilverIdol);
            var prefab = Prefab(SilverIdol);
            c.Check(icon != null && prefab != null, "the Silver Battle Idol has an icon");
            if (icon == null || prefab == null)
            {
                c.Report();
                yield break;
            }
            StarIcons.Clear();
            StarIcons.TestForceEmpty = true;
            // Mod try again 1 s after an empty copy (StarIcons.RetrySeconds): me judge only calls well inside it.
            var failedAt = Time.realtimeSinceStartup;
            StarIcons.Get(icon, 2); // first try fail here (what it give back: forge.bug.empty-copy-icon)
            var next = StarIcons.Get(icon, 2);
            c.Check(next == icon, $"while the GPU copy is empty, the calls after the first give the plain icon: {Describe(next)}");
            // Same through the game's own way to ask (what grid, hotbar and messages call).
            var item = prefab.m_itemData.Clone();
            item.m_quality = 3;
            item.m_dropPrefab = prefab.gameObject;
            var asked = item.GetIcon();
            c.Check(asked == icon, $"a 2-star idol asked for its icon meanwhile shows the plain icon: {Describe(asked)}");
            yield return null;
            if (Time.realtimeSinceStartup - failedAt < 0.6f)
            {
                asked = item.GetIcon();
                c.Check(asked == icon, $"and still the plain icon a frame later (no new try before about a second): {Describe(asked)}");
                // Copy work again, but the second is not over: no new try yet.
                StarIcons.TestForceEmpty = false;
                asked = item.GetIcon();
                c.Check(asked == icon, $"copy works again but the second is not over: still the plain icon: {Describe(asked)}");
            }
            else
            {
                SelfTest.Note(EmptyCopyName, "a slow frame used up the retry second: the 'a frame later' checks are skipped in this run");
            }

            // Later copy work again: stars come.
            StarIcons.TestForceEmpty = false;
            yield return new WaitForSecondsRealtime(1.2f);
            var later = item.GetIcon();
            c.Check(StarIcons.IsStarSprite(later) && later == StarIcons.Get(icon, 2), $"about a second later the starred icon is made: {Describe(later)}");
            c.Report();
        }
        finally
        {
            StarIcons.TestForceEmpty = false;
            StarIcons.Clear();
        }
    }

    // ---------- forge.bug.empty-copy-icon (T22, T05: real bug, fail until mod fixed) ----------

    // Run 2026-10-07: first StarIcons.Get while the copy is empty gave no sprite at all (StarIcons.Make end with
    // "entry.Failed ? source : entry.Made", and after an empty copy Failed is false and Made is null), so a never-seen
    // 2-star idol picked up at that moment queue its "New material" popup with no icon (white square).
    private static IEnumerator RunBugEmptyCopy()
    {
        var rig = new Rig(BugEmptyCopyName);
        var c = rig.C;
        var hud = MessageHud.instance;
        try
        {
            var icon = PlainIconOf(SilverIdol);
            var idolName = NameOf(SilverIdol);
            StarIcons.Clear();
            StarIcons.TestForceEmpty = true;
            var first = StarIcons.Get(icon, 2);
            c.Check(first == icon, $"first call while the GPU copy is empty: the plain icon, got {Describe(first)}");

            // What player see: never-seen 2-star idol picked up at that moment.
            StarIcons.Clear();
            rig.P.m_knownMaterial.Remove(idolName);
            var dropped = rig.Drop(SilverIdol, 3, 1, 1.5f);
            yield return null;
            hud.ClearUnlockQueue();
            StarIcons.Clear(); // a frame passed: somebody may have asked for the icon already. Fresh again.
            var picked = dropped != null && rig.P.Pickup(dropped.gameObject, false, false);
            var wanted = L(idolName);
            var queued = hud.m_unlockMsgQueue.FirstOrDefault(m => m != null && m.m_description == wanted);
            c.Check(picked && queued != null && queued.m_icon == icon,
                $"its 'New material' popup gets the plain icon, not an empty one: {(!picked ? "pickup failed" : queued == null ? "no popup queued" : Describe(queued.m_icon))}");
            hud.ClearUnlockQueue();
            c.Report();
        }
        finally
        {
            StarIcons.TestForceEmpty = false;
            StarIcons.Clear();
            rig.Dispose();
        }
    }
}
#endif
