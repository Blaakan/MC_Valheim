#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.UX.AutoPickupFilterMod;

// Debug build only. What auto pickup collects in each mode, the vanilla on/off switch, own drops, manual pickups,
// ownership (never claimed) and another mod's patch on Player.AutoPickup. Helpers and Rig live in SelfTests.cs.
internal static partial class SelfTests
{
    private const string EverythingName = "lootfilter.everything";
    private const string SkipName = "lootfilter.skip";
    private const string OnlyName = "lootfilter.only";
    private const string VSwitchName = "lootfilter.vswitch";
    private const string OwnDropsName = "lootfilter.owndrops";
    private const string SpearName = "lootfilter.spear";
    private const string HandoffName = "lootfilter.handoff";
    private const string ForeignPatchName = "lootfilter.foreign-patch";

    // Room for what the tests pick up? (Full or overloaded inventory = vanilla picks nothing: test would lie.)
    private static void NoteRoom(Checks c, Rig rig)
    {
        var inv = rig.Inv;
        c.Check(inv.GetEmptySlots() >= 6 && inv.GetTotalWeight() + 80f < rig.Player.GetMaxCarryWeight(),
            $"room in the inventory for the test items ({inv.GetEmptySlots()} free slots, weight {F(inv.GetTotalWeight())}/{F(rig.Player.GetMaxCarryWeight())})");
    }

    // ---------------------------------------------------------------- lootfilter.everything (T02)

    private static IEnumerator RunEverything()
    {
        if (!Ready(EverythingName, out var player, out _))
        {
            yield break;
        }
        var c = new Checks(EverythingName);
        var rig = new Rig(player, EverythingName);
        try
        {
            var inv = rig.Inv;
            NoteRoom(c, rig);
            c.Check(FilterState.Mode == FilterMode.Everything, "mode is Everything");
            Player.m_enableAutoPickup = false;
            var stone = rig.Drop("Stone", 10, rig.Spot(0.9f, 0.45f, 0.3f));
            var wood = rig.Drop("Wood", 10, rig.Spot(0.9f, -0.45f, 0.3f));
            if (!c.Check(stone != null && wood != null, "10 Stone and 10 Wood on the ground next to the player"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(1f);
            c.Check(Alive(stone) && Alive(wood), "auto pickup off: nothing picked up");
            var stones = Count(inv, "Stone");
            var woods = Count(inv, "Wood");
            Messages.Clear();
            Player.m_enableAutoPickup = true;
            var both = new List<ItemDrop> { stone, wood };
            yield return WaitGone(both, 4f);
            c.Check(!Alive(stone) && Count(inv, "Stone") == stones + 10, $"auto pickup on: the Stone is picked up (+{Count(inv, "Stone") - stones})");
            c.Check(!Alive(wood) && Count(inv, "Wood") == woods + 10, $"auto pickup on: the Wood is picked up (+{Count(inv, "Wood") - woods})");
            c.Check(TopLeftStarting("Auto pickup filter") == 0, "Everything mode: no filter message when auto pickup comes on");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.skip (T03, T04, T05)

    private static IEnumerator RunSkip()
    {
        if (!Ready(SkipName, out var player, out var gui))
        {
            yield break;
        }
        var c = new Checks(SkipName);
        var rig = new Rig(player, SkipName);
        try
        {
            var inv = rig.Inv;
            NoteRoom(c, rig);
            var carried = rig.Give("Stone", 5);
            if (!c.Check(carried != null, "5 Stone carried"))
            {
                c.Report();
                yield break;
            }
            var stoneName = Shared("Stone");

            // T03: middle-click marks, red mark, message.
            FilterState.SetMode(FilterMode.SkipIgnored);
            yield return Open();
            yield return Frames(2);
            var grid = gui.m_playerGrid;
            Messages.Clear();
            yield return MarkGesture(grid, carried);
            yield return null;
            c.Check(SameSet(FilterState.Ignored, "Stone"), $"middle-click on the Stone slot: Stone is ignored ({FilterState.Join(FilterState.Ignored)})");
            c.Check(BadgeOn(grid, carried, SpriteIgnored), "red mark on the Stone slot");
            c.Check(TopLeft("Ignored by auto pickup: " + stoneName) == 1, $"message 'Ignored by auto pickup: Stone' (got: {AllMessages()})");
            yield return Close();

            // Stone stays and is not pulled, Wood is picked.
            Player.m_enableAutoPickup = false;
            var stone = rig.Drop("Stone", 10, rig.Spot(0.9f, 0.45f, 0.3f));
            var wood = rig.Drop("Wood", 10, rig.Spot(0.9f, -0.45f, 0.3f));
            if (!c.Check(stone != null && wood != null, "10 Stone and 10 Wood on the ground"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(1.2f);
            var stones = Count(inv, "Stone");
            var woods = Count(inv, "Wood");
            var place = stone.transform.position;
            var reach = Vector3.Distance(place, player.transform.position + Vector3.up);
            c.Check(reach < player.m_autoPickupRange, $"the Stone lies inside the auto pickup range ({F(reach)} of {F(player.m_autoPickupRange)} m)");
            Player.m_enableAutoPickup = true;
            yield return new WaitForSeconds(2.5f);
            c.Check(!Alive(wood) && Count(inv, "Wood") == woods + 10, "Wood is picked up");
            c.Check(Alive(stone) && Count(inv, "Stone") == stones, "ignored Stone stays on the ground");
            if (Alive(stone))
            {
                var moved = Vector3.Distance(stone.transform.position, place);
                c.Check(moved < 0.15f, $"ignored Stone is not pulled towards the player (moved {F(moved)} m)");
            }

            // T04: grey line, E still picks, ShowInHoverText off = line gone at once.
            var hover = HoverOf(stone);
            var line = hover.IndexOf(SkipLineIgnored, StringComparison.Ordinal);
            c.Check(line > 0 && hover.IndexOf(L(stoneName), StringComparison.Ordinal) >= 0 && hover.IndexOf(L(stoneName), StringComparison.Ordinal) < line,
                $"hover text has the grey line under the name ('{hover.Replace("\n", " / ")}')");
            c.Check(hover.Contains("<color=#A0A0A0>" + SkipLineIgnored + "</color>"), "the line is grey");
            TestHooks.ShowInHoverText = false;
            c.Check(RaiseChanged(Plugin.ShowInHoverText), "ShowInHoverText change handler ran");
            c.Check(!HoverOf(stone).Contains(SkipLineAny), "ShowInHoverText off: the line is gone at once");
            TestHooks.ShowInHoverText = true;
            RaiseChanged(Plugin.ShowInHoverText);
            c.Check(HoverOf(stone).Contains(SkipLineIgnored), "ShowInHoverText on: the line is back");
            yield return UsePickup(rig, stone);
            c.Check(!Alive(stone) && Count(inv, "Stone") == stones + 10, "E on the ignored Stone picks it up");

            // T05: unmark, then it is auto-picked again.
            var rest = rig.Drop("Stone", 10, rig.Spot(0.9f, 0.45f, 0.3f));
            yield return new WaitForSeconds(1.5f);
            c.Check(Alive(rest), "more ignored Stone stays on the ground");
            stones = Count(inv, "Stone");
            yield return Open();
            yield return Frames(2);
            Messages.Clear();
            FilterUi.TestForgetMarkMessageTime();
            yield return MarkGesture(grid, carried);
            c.Check(FilterState.Ignored.Count == 0, "middle-click again: Stone no longer ignored");
            c.Check(TopLeft("No longer ignored: " + stoneName) == 1, $"message 'No longer ignored: Stone' (got: {AllMessages()})");
            yield return null;
            yield return null;
            c.Check(!BadgeOn(grid, carried), "mark gone");
            yield return Close();
            var one = new List<ItemDrop> { rest };
            yield return WaitGone(one, 3f);
            c.Check(!Alive(rest) && Count(inv, "Stone") == stones + 10, "the remaining Stone is picked up");
            c.Check(rig.NewGuardReports == 0, "no error inside the mod");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.only (T06, T09)

    private static IEnumerator RunOnly()
    {
        if (!Ready(OnlyName, out var player, out var gui))
        {
            yield break;
        }
        var c = new Checks(OnlyName);
        var rig = new Rig(player, OnlyName);
        try
        {
            var inv = rig.Inv;
            NoteRoom(c, rig);
            var carried = rig.Give("Coins", 3);
            if (!c.Check(carried != null, "3 Coins carried"))
            {
                c.Report();
                yield break;
            }

            // T06: select Coins with a middle-click, green mark, only Coins picked.
            FilterState.SetMode(FilterMode.OnlySelected);
            yield return Open();
            yield return Frames(2);
            var grid = gui.m_playerGrid;
            Messages.Clear();
            yield return MarkGesture(grid, carried);
            yield return null;
            c.Check(SameSet(FilterState.Selected, "Coins"), $"middle-click on the Coins slot: Coins is selected ({FilterState.Join(FilterState.Selected)})");
            c.Check(BadgeOn(grid, carried, SpriteSelected), "green mark on the Coins slot");
            c.Check(TopLeft("Selected for auto pickup: " + Shared("Coins")) == 1, $"message 'Selected for auto pickup: Coins' (got: {AllMessages()})");
            yield return Close();

            Player.m_enableAutoPickup = false;
            var coins = rig.Drop("Coins", 5, rig.Spot(0.9f, 0f, 0.3f));
            var stone = rig.Drop("Stone", 5, rig.Spot(0.8f, 0.6f, 0.3f));
            var wood = rig.Drop("Wood", 5, rig.Spot(0.8f, -0.6f, 0.3f));
            if (!c.Check(coins != null && stone != null && wood != null, "5 Coins, 5 Stone and 5 Wood on the ground"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(1.2f);
            var coinCount = Count(inv, "Coins");
            var stoneCount = Count(inv, "Stone");
            var woodCount = Count(inv, "Wood");
            Player.m_enableAutoPickup = true;
            yield return new WaitForSeconds(2.5f);
            c.Check(!Alive(coins) && Count(inv, "Coins") == coinCount + 5, "the Coins are picked up");
            c.Check(Alive(stone) && Alive(wood) && Count(inv, "Stone") == stoneCount && Count(inv, "Wood") == woodCount, "Stone and Wood stay on the ground");
            c.Check(HoverOf(stone).Contains(SkipLineNotSelected) && HoverOf(wood).Contains(SkipLineNotSelected), "both show 'Auto pickup skips this (not selected)'");

            // T09: empty Selected list.
            var lines = Run(Console.instance, "lootfilter clear selected");
            c.Check(FilterState.Selected.Count == 0 && AnyLine(lines, "Loot filter: Selected list cleared (1 removed)."), $"lootfilter clear selected empties the list ({Lines(lines)})");
            FilterState.SetMode(FilterMode.Everything);
            Player.m_enableAutoPickup = false; // keep the test items on the ground while in Everything mode
            yield return Open();
            yield return Frames(2);
            var button = FindButton();
            if (c.Check(button != null, "button is there"))
            {
                Click(button);
                Messages.Clear();
                Click(button);
                c.Check(FilterState.Mode == FilterMode.OnlySelected, "two clicks: Only selected");
                c.Check(TopLeftStarting(MsgOnlyEmpty) == 1 && Messages.Any(m => m.Text.Contains("Nothing is selected yet, so nothing is picked up automatically.")),
                    $"switching to Only selected with an empty list warns (got: {AllMessages()})");
            }
            yield return Close();
            var more = rig.Drop("Coins", 5, rig.Spot(0.9f, 0f, 0.3f));
            yield return new WaitForSeconds(0.8f);
            coinCount = Count(inv, "Coins");
            Player.m_enableAutoPickup = true;
            yield return new WaitForSeconds(2.5f);
            c.Check(Alive(more) && Alive(stone) && Alive(wood) && Count(inv, "Coins") == coinCount && Count(inv, "Stone") == stoneCount && Count(inv, "Wood") == woodCount,
                "empty Selected list: nothing is auto-picked");
            yield return UsePickup(rig, stone);
            c.Check(!Alive(stone) && Count(inv, "Stone") == stoneCount + 5, "E still picks up");
            c.Check(rig.NewGuardReports == 0, "no error inside the mod");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.vswitch (T10, T28)

    // Wait until a top-left message with this text was shown, up to seconds.
    private static IEnumerator WaitMessage(string text, float seconds)
    {
        var end = Time.time + seconds;
        while (Time.time < end && TopLeft(text) == 0)
        {
            yield return null;
        }
    }

    private static IEnumerator RunVSwitch()
    {
        if (!Ready(VSwitchName, out var player, out _))
        {
            yield break;
        }
        var c = new Checks(VSwitchName);
        var rig = new Rig(player, VSwitchName);
        try
        {
            var inv = rig.Inv;
            NoteRoom(c, rig);
            FilterState.SetMode(FilterMode.SkipIgnored);
            FilterState.Toggle(FilterState.Ignored, "Stone");
            // Auto pickup seen "on" by the mod first (like a player who presses V while playing).
            yield return new WaitForSeconds(0.3f);

            // T10: V off (the flag the auto pickup key flips; the key press itself is not played).
            Player.m_enableAutoPickup = false;
            var wood = rig.Drop("Wood", 5, rig.Spot(0.9f, -0.45f, 0.3f));
            var woods = Count(inv, "Wood");
            yield return new WaitForSeconds(2.5f);
            c.Check(Alive(wood) && Count(inv, "Wood") == woods, "auto pickup off: Wood next to the player is not picked up");
            yield return Open();
            yield return Frames(2);
            var button = FindButton();
            c.Check(LabelOf(button) == "Auto pickup: Off (Skip ignored)", $"button reads 'Auto pickup: Off (Skip ignored)' (it reads '{LabelOf(button)}')");
            c.Check(TooltipOf(button).Contains("Auto pickup is off: turn it on or off with $KEY_AutoPickup."), "tooltip says auto pickup is off");
            yield return Close();

            // V on: mode message, Wood picked, Stone not.
            var stone = rig.Drop("Stone", 5, rig.Spot(0.9f, 0.45f, 0.3f));
            var stones = Count(inv, "Stone");
            yield return new WaitForSeconds(0.8f);
            Messages.Clear();
            Player.m_enableAutoPickup = true;
            yield return WaitMessage(MsgSkip(1), 1.5f);
            c.Check(TopLeft(MsgSkip(1)) == 1, $"auto pickup on: message '{MsgSkip(1)}' (got: {AllMessages()})");
            yield return new WaitForSeconds(2.5f);
            c.Check(TopLeftStarting("Auto pickup filter") == 1, "the mode message comes once");
            c.Check(!Alive(wood) && Count(inv, "Wood") == woods + 5, "auto pickup on: Wood is picked up");
            c.Check(Alive(stone) && Count(inv, "Stone") == stones, "auto pickup on: ignored Stone is not");
            yield return Open();
            yield return Frames(2);
            c.Check(LabelOf(FindButton()) == "Auto pickup: Skip ignored", "button reads 'Auto pickup: Skip ignored' again");
            yield return Close();

            // Everything mode: V on shows no filter message.
            FilterState.SetMode(FilterMode.Everything);
            Player.m_enableAutoPickup = false;
            yield return new WaitForSeconds(0.3f);
            Messages.Clear();
            Player.m_enableAutoPickup = true;
            yield return new WaitForSeconds(0.7f);
            c.Check(TopLeftStarting("Auto pickup filter") == 0, $"Everything mode: auto pickup on shows no filter message (got: {AllMessages()})");
            // (Stone on the ground is picked up now: Everything mode.)

            // T28: V off, character loaded again (the mod meets the character anew): label Off at once, no message
            // until V; then the mode message.
            FilterState.SetMode(FilterMode.OnlySelected);
            FilterState.Toggle(FilterState.Selected, "Coins");
            Player.m_enableAutoPickup = false;
            yield return new WaitForSeconds(0.3f);
            FilterState.Reset();
            Messages.Clear();
            // Same frame as the inventory opening: the label is written already. The panel itself is still switched
            // off in this frame (opening animation not started: round 1 found no shown button here), so the button is
            // looked up shown or not.
            InventoryGui.instance.Show(null);
            var atOnce = LabelOf(FindButton(shownOnly: false));
            c.Check(atOnce == "Auto pickup: Off (Only selected)", $"after the load, auto pickup still off: the button's label is 'Auto pickup: Off (Only selected)' in the frame the inventory opens ('{atOnce}')");
            // First frame the button is on screen: same text (never a frame with the mode of before, or "on").
            var shownBy = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < shownBy && FindButton() == null)
            {
                yield return null;
            }
            var firstSeen = LabelOf(FindButton());
            c.Check(firstSeen == "Auto pickup: Off (Only selected)", $"the first frame the button is on screen it reads 'Auto pickup: Off (Only selected)' ('{firstSeen}')");
            yield return new WaitForSeconds(1.2f);
            c.Check(TopLeftStarting("Auto pickup filter") == 0, $"no mode message while auto pickup stays off (got: {AllMessages()})");
            yield return Close();
            Player.m_enableAutoPickup = true;
            yield return WaitMessage(MsgOnly(1), 1.5f);
            c.Check(TopLeft(MsgOnly(1)) == 1, $"auto pickup on: that character's mode message '{MsgOnly(1)}' (got: {AllMessages()})");
            c.Check(rig.NewGuardReports == 0, "no error inside the mod");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.owndrops (T11, T36)

    private static IEnumerator RunOwnDrops()
    {
        if (!Ready(OwnDropsName, out var player, out _))
        {
            yield break;
        }
        var c = new Checks(OwnDropsName);
        var rig = new Rig(player, OwnDropsName);
        try
        {
            var inv = rig.Inv;
            NoteRoom(c, rig);
            var woodName = Shared("Wood");
            // T11: in each mode the filter itself would let Wood in (not ignored / selected): only the vanilla rule
            // "your own drop is not auto-picked" keeps it on the ground.
            FilterState.Toggle(FilterState.Selected, "Wood");
            foreach (var mode in new[] { FilterMode.Everything, FilterMode.SkipIgnored, FilterMode.OnlySelected })
            {
                FilterState.SetMode(mode);
                var woods = Count(inv, "Wood");
                var stack = rig.Give("Wood", 10);
                var since = rig.DropsNow();
                var dropped = stack != null && player.DropItem(inv, stack, 10);
                yield return Frames(2);
                var mine = rig.NewDrops(since).Where(d => d.m_itemData.m_shared.m_name == woodName).ToList();
                if (!c.Check(dropped && mine.Count == 1, $"{mode}: dropped a stack of 10 Wood from the inventory"))
                {
                    continue;
                }
                var drop = mine[0];
                c.Check(!drop.m_autoPickup, $"{mode}: the dropped stack is flagged 'no auto pickup'");
                yield return new WaitForSeconds(0.9f);
                yield return StandOn(rig, drop, 1.6f);
                c.Check(Alive(drop) && Count(inv, "Wood") == woods, $"{mode}: standing on it, it is not auto-picked back");
                c.Check(!HoverOf(drop).Contains(SkipLineAny), $"{mode}: no 'Auto pickup skips this' line on it (the filter does not skip it)");
                yield return UsePickup(rig, drop);
                c.Check(!Alive(drop) && Count(inv, "Wood") == woods + 10, $"{mode}: E picks it up");
                rig.MoveTo(rig.Origin);
                var back = inv.GetItem(woodName);
                if (back != null)
                {
                    inv.RemoveItem(back);
                }
                yield return null;
            }

            // T36: spawn and pick up at once (what 'spawn Stone 5 p' does: Player.Pickup, no auto pickup).
            FilterState.SetMode(FilterMode.SkipIgnored);
            FilterState.Toggle(FilterState.Ignored, "Stone");
            var stones = Count(inv, "Stone");
            var stone = rig.Drop("Stone", 5, rig.Spot(1.5f, 0f, 0.3f));
            var picked = stone != null && player.Pickup(stone.gameObject, false, false);
            yield return null;
            c.Check(picked && !Alive(stone) && Count(inv, "Stone") == stones + 5, "Skip ignored, Stone ignored: a direct pickup (spawn ... p) puts the Stone in the inventory");
            FilterState.SetMode(FilterMode.OnlySelected);
            var woodsNow = Count(inv, "Wood");
            FilterState.Toggle(FilterState.Selected, "Wood"); // now not selected
            var wood = rig.Drop("Wood", 5, rig.Spot(1.5f, 0f, 0.3f));
            picked = wood != null && player.Pickup(wood.gameObject, false, false);
            yield return null;
            c.Check(picked && !Alive(wood) && Count(inv, "Wood") == woodsNow + 5, "Only selected, Wood not selected: a direct pickup still works");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.spear (T14)

    private static IEnumerator RunSpear()
    {
        if (!Ready(SpearName, out var player, out _))
        {
            yield break;
        }
        var c = new Checks(SpearName);
        var rig = new Rig(player, SpearName);
        try
        {
            var inv = rig.Inv;
            FilterState.SetMode(FilterMode.OnlySelected);
            FilterState.Toggle(FilterState.Selected, "Coins");
            var spear = rig.Give("SpearFlint");
            if (!c.Check(spear != null, "SpearFlint given"))
            {
                c.Report();
                yield break;
            }
            var spearName = spear.m_shared.m_name;
            var throwAttack = spear.m_shared.m_secondaryAttack;
            var projectile = throwAttack != null && throwAttack.m_attackProjectile != null ? throwAttack.m_attackProjectile.GetComponent<Projectile>() : null;
            c.Check(projectile != null && projectile.m_respawnItemOnHit,
                $"SpearFlint's throw (projectile '{(throwAttack != null && throwAttack.m_attackProjectile != null ? throwAttack.m_attackProjectile.name : "none")}') falls back to the ground as an item");

            // Real throw at the ground just ahead. The throw goes where the player looks: body and look along the
            // test lane, 65 degrees down. Round 1: the attack did not start (pick up animation of the test before).
            var since = rig.DropsNow();
            player.EquipItem(spear, false);
            rig.Face(rig.Forward, 65f);
            yield return Frames(3);
            var attack = new bool[1];
            yield return StartAttackSoon(player, true, 3f, attack);
            var started = attack[0];
            var fallen = new List<ItemDrop>();
            var end = Time.time + 6f;
            while (Time.time < end && fallen.Count == 0)
            {
                yield return null;
                fallen = rig.NewDrops(since).Where(d => d.m_itemData.m_shared.m_name == spearName).ToList();
            }
            ItemDrop drop;
            if (fallen.Count > 0)
            {
                drop = fallen[0];
                c.Note($"real throw: the spear landed {F(Vector3.Distance(drop.transform.position, rig.Origin))} m away");
            }
            else
            {
                // Throw did not come through here: same call the projectile makes when it lands.
                c.Note($"the real throw gave no item within 6 s (attack started: {started}); the projectile's drop call was replayed instead");
                if (inv.ContainsItem(spear))
                {
                    player.UnequipItem(spear, false);
                    inv.RemoveItem(spear);
                }
                drop = ItemDrop.DropItem(spear, 1, rig.Spot(0.9f, 0f, 0.5f), Quaternion.identity);
                rig.Track(drop.gameObject);
            }
            c.Check(drop.m_autoPickup, "the fallen spear is an ordinary item that auto pickup may take");
            yield return new WaitForSeconds(1f);
            var have = Count(inv, "SpearFlint");
            yield return StandOn(rig, drop, 2f);
            c.Check(Alive(drop) && Count(inv, "SpearFlint") == have, "Only selected (Coins only): walking over the spear leaves it on the ground");
            c.Check(HoverOf(drop).Contains(SkipLineNotSelected), "it shows 'Auto pickup skips this (not selected)'");
            yield return UsePickup(rig, drop);
            c.Check(!Alive(drop) && Count(inv, "SpearFlint") == have + 1, "E picks it up");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.handoff (M02) + lootfilter.mp.handoff

    // Made-up player id: "another player's game owns this item".
    private const long OtherPlayer = 987654321012L;

    // Skipped item owned by someone else: this game must never ask for it (ItemDrop.RequestOwn count stays 0), while
    // an item the filter lets in is asked for. Then: marked item types carry nothing of the mod.
    private static IEnumerator ClaimCore(Rig rig, Checks c)
    {
        var player = rig.Player;
        var inv = rig.Inv;
        FilterState.SetMode(FilterMode.SkipIgnored);
        FilterState.Toggle(FilterState.Ignored, "Stone");
        Player.m_enableAutoPickup = false;
        var stone = rig.Drop("Stone", 5, rig.Spot(0.9f, 0.45f, 0.3f));
        var wood = rig.Drop("Wood", 5, rig.Spot(0.9f, -0.45f, 0.3f));
        if (!c.Check(stone != null && wood != null, "ignored Stone and plain Wood on the ground next to the player"))
        {
            yield break;
        }
        yield return new WaitForSeconds(1.2f);
        var stoneZdo = stone.m_nview.GetZDO();
        var woodZdo = wood.m_nview.GetZDO();
        stone.m_ownerRetryCounter = 0;
        wood.m_ownerRetryCounter = 0;
        var stones = Count(inv, "Stone");
        var asksStone = 0;
        var asksWood = 0;
        Player.m_enableAutoPickup = true;
        var end = Time.time + 1.2f;
        while (Time.time < end && Alive(stone) && Alive(wood))
        {
            // Host or server hands a stray item back to the nearest player every 2 s: keep the other owner.
            stoneZdo.SetOwner(OtherPlayer);
            woodZdo.SetOwner(OtherPlayer);
            yield return new WaitForFixedUpdate();
            if (Alive(stone))
            {
                asksStone = Mathf.Max(asksStone, stone.m_ownerRetryCounter);
            }
            if (Alive(wood))
            {
                asksWood = Mathf.Max(asksWood, wood.m_ownerRetryCounter);
            }
        }
        c.Check(asksWood > 0, $"control: the game asks the other owner for the Wood it wants ({asksWood} request(s))");
        c.Check(asksStone == 0, $"the skipped Stone is never claimed ({asksStone} ownership request(s))");
        c.Check(Alive(stone) && Count(inv, "Stone") == stones, "the skipped Stone stays on the ground for the other player");
        // Give both back to this game: Wood goes in, Stone still stays.
        if (Alive(stone))
        {
            stone.m_nview.ClaimOwnership();
        }
        if (Alive(wood))
        {
            wood.m_nview.ClaimOwnership();
        }
        var one = new List<ItemDrop> { wood };
        yield return WaitGone(one, 3f);
        c.Check(!Alive(wood), "once this game owns it, the Wood is picked up as usual");
        c.Check(Alive(stone), "the Stone still stays");

        // Items of a marked type are plain items: nothing of the mod on them, in the inventory or dropped.
        var carried = rig.Give("Stone", 3);
        if (c.Check(carried != null, "Stone carried"))
        {
            c.Check(!carried.m_customData.Keys.Any(k => k.StartsWith(ModInfo.Guid, StringComparison.Ordinal)), "a carried item of an ignored type holds no data of the mod");
            var since = rig.DropsNow();
            player.DropItem(inv, carried, 3);
            yield return Frames(2);
            var dropped = rig.NewDrops(since).FirstOrDefault(d => d.m_itemData.m_shared.m_name == Shared("Stone"));
            c.Check(dropped != null && !dropped.m_itemData.m_customData.Keys.Any(k => k.StartsWith(ModInfo.Guid, StringComparison.Ordinal)),
                "handed over (dropped): the item on the ground holds no data of the mod");
        }
        c.Check(rig.NewGuardReports == 0 && rig.Logs.Count(LogLevel.Error | LogLevel.Fatal) == 0, $"no error from the mod ({rig.Logs.First(LogLevel.Error | LogLevel.Fatal)})");
    }

    private static IEnumerator RunHandoff()
    {
        if (!Ready(HandoffName, out var player, out _))
        {
            yield break;
        }
        var c = new Checks(HandoffName);
        var rig = new Rig(player, HandoffName);
        try
        {
            yield return ClaimCore(rig, c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.foreign-patch (C07)

    private const string ForeignOwner = "MC.Test.ForeignAutoPickup";
    private static int _foreignRuns;

    // Stand-in for another auto pickup mod: its own small loop replaces the vanilla one (prefix returns false). Like
    // the real ones it still asks ItemDrop.IsPiece before taking an item.
    private static bool ForeignAutoPickup(Player __instance)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer) || !Player.m_enableAutoPickup)
        {
            return false;
        }
        _foreignRuns++;
        var center = __instance.transform.position + Vector3.up;
        foreach (var drop in ItemDrop.s_instances.ToArray())
        {
            if (drop == null || !drop.m_autoPickup || drop.IsPiece() || !drop.CanPickup())
            {
                continue;
            }
            if (Vector3.Distance(drop.transform.position, center) <= __instance.m_autoPickupRange)
            {
                __instance.Pickup(drop.gameObject);
            }
        }
        return false;
    }

    private static IEnumerator RunForeignPatch()
    {
        if (!Ready(ForeignPatchName, out var player, out _))
        {
            yield break;
        }
        var c = new Checks(ForeignPatchName);
        var rig = new Rig(player, ForeignPatchName);
        var foreign = new Harmony(ForeignOwner);
        try
        {
            var inv = rig.Inv;
            FilterState.SetMode(FilterMode.SkipIgnored);
            FilterState.Toggle(FilterState.Ignored, "Stone");
            Player.m_enableAutoPickup = false;
            var stone = rig.Drop("Stone", 5, rig.Spot(0.9f, 0.45f, 0.3f));
            var wood = rig.Drop("Wood", 5, rig.Spot(0.9f, -0.45f, 0.3f));
            if (!c.Check(stone != null && wood != null, "ignored Stone and plain Wood on the ground"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(1.2f);
            var stones = Count(inv, "Stone");
            var woods = Count(inv, "Wood");
            var target = AccessTools.Method(typeof(Player), nameof(Player.AutoPickup), new[] { typeof(float) });
            _foreignRuns = 0;
            foreign.Patch(target, prefix: new HarmonyMethod(typeof(SelfTests), nameof(ForeignAutoPickup)));
            // Like a fresh start with that mod installed: the mod looks at the patches on its first auto pickup run.
            AutoPickupGuard.ForeignCheckDone = false;
            Player.m_enableAutoPickup = true;
            yield return new WaitForSeconds(2.5f);
            var warned = rig.Logs.Count(LogLevel.Warning, "Another mod changes Player.AutoPickup (");
            c.Check(warned == 1 && rig.Logs.Count(LogLevel.Warning, ForeignOwner) == 1, $"one warning 'Another mod changes Player.AutoPickup (...)' naming {ForeignOwner} ({warned})");
            c.Check(_foreignRuns > 0, $"the other mod's loop ran in place of the vanilla one ({_foreignRuns} times)");
            c.Check(!Alive(wood) && Count(inv, "Wood") == woods + 5, "the other mod's loop still picks up the Wood");
            c.Check(Alive(stone) && Count(inv, "Stone") == stones, "our ignored Stone is still skipped");
            c.Check(HoverOf(stone).Contains(SkipLineIgnored), "and still shows the grey line");
            c.Check(rig.NewGuardReports == 0 && rig.Logs.Count(LogLevel.Error | LogLevel.Fatal) == 0, "no error from the mod");
            c.Report();
        }
        finally
        {
            foreign.UnpatchSelf();
            rig.Restore();
        }
    }
}
#endif
