#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.UX.AutoPickupFilterMod;

// Debug build only. Inventory screen side of the self tests: button, badges, mark gestures, controller, commands,
// live toggle, what is saved in the character. Helpers and Rig live in SelfTests.cs.
internal static partial class SelfTests
{
    private const string ButtonName = "lootfilter.button";
    private const string LayoutName = "lootfilter.layout";
    private const string MarksName = "lootfilter.marks";
    private const string MarkColoursName = "lootfilter.mark-colours";
    private const string BugBadgeHotbarName = "lootfilter.bug.badge-hotbar-number";
    private const string BugListsPanelName = "lootfilter.bug.lists-panel-over-grid";
    private const string ChestName = "lootfilter.chest";
    private const string BadgesName = "lootfilter.badges";
    private const string OutsideGridName = "lootfilter.outside-grid";
    private const string PersistName = "lootfilter.persist";
    private const string FutureDataName = "lootfilter.future-data";
    private const string CommandsName = "lootfilter.commands";
    private const string GamepadName = "lootfilter.gamepad";
    private const string MarkKeyName = "lootfilter.markkey";
    private const string DialogsName = "lootfilter.dialogs";
    private const string ToggleName = "lootfilter.toggle";
    private const string CompatSearchName = "lootfilter.compat-search";
    private const string CompatRepairName = "lootfilter.compat-repair";
    private const string LogName = "lootfilter.log";

    private const string MsgEverything = "Auto pickup filter: Everything (normal game)";
    private const string MsgOnlyEmpty =
        "Auto pickup filter: Only selected items. Nothing is selected yet, so nothing is picked up automatically.";
    private const string MsgChooseMode = "Choose Skip ignored or Only selected on the Auto pickup button first.";
    private const string MsgChooseModePad = "Choose Skip ignored or Only selected first: hold LT and click the right stick.";

    private static string MsgSkip(int n) => $"Auto pickup filter: Skip ignored items ({n} ignored)";

    private static string MsgOnly(int n) => $"Auto pickup filter: Only selected items ({n} selected)";

    // Player and inventory screen there? Else the test say why and stop.
    private static bool Ready(string name, out Player player, out InventoryGui gui)
    {
        player = Player.m_localPlayer;
        gui = InventoryGui.instance;
        if (player == null || gui == null || gui.m_playerGrid == null || gui.m_player == null)
        {
            SelfTest.Fail(name, "no local player or no inventory screen");
            return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- lootfilter.button (T01, T27)

    private static IEnumerator RunButton()
    {
        if (!Ready(ButtonName, out var player, out var gui))
        {
            yield break;
        }
        var c = new Checks(ButtonName);
        var rig = new Rig(player, ButtonName);
        try
        {
            yield return Open();
            var button = FindButton();
            if (!c.Check(button != null, "button " + ButtonObject + " is there while the inventory is open"))
            {
                c.Report();
                yield break;
            }
            var es = EventSystem.current;
            var grid = gui.m_playerGrid;

            // Place (T01).
            c.Check(LabelOf(button) == "Auto pickup: Everything", $"label in Everything mode is '{LabelOf(button)}'");
            var b = WorldRect((RectTransform)button.transform);
            var panel = WorldRect(gui.m_player);
            var slots = grid.m_elements;
            var first = slots.Count > 0 ? WorldRect((RectTransform)slots[0].transform) : default;
            var last = slots.Count > 0 ? WorldRect((RectTransform)slots[slots.Count - 1].transform) : default;
            var slotArea = Rect.MinMaxRect(Mathf.Min(first.xMin, last.xMin), Mathf.Min(first.yMin, last.yMin),
                Mathf.Max(first.xMax, last.xMax), Mathf.Max(first.yMax, last.yMax));
            c.Note($"screen {Screen.width}x{Screen.height}; button {F(b)}; player panel {F(panel)}; first slot {F(first)}; "
                   + $"slots {F(slotArea)}; button parent '{button.transform.parent.name}' "
                   + $"({(ReferenceEquals(button.transform.parent, gui.m_player) ? "player panel" : "inventory root")})");
            c.Check(slots.Count > 0, "player grid has slots");
            c.Check(b.yMin >= panel.yMax - 1f && b.yMin - panel.yMax <= 24f,
                $"button sits just above the player panel (gap {F(b.yMin - panel.yMax)} px)");
            c.Check(Mathf.Abs(b.xMin - first.xMin) <= 1.5f, $"button left edge on the first slot's left edge (off by {F(b.xMin - first.xMin)} px)");
            c.Check(!b.Overlaps(slotArea), "button does not overlap the slots");
            if (GlyphRect(gui.m_playerName, out var nameRect))
            {
                c.Check(!b.Overlaps(nameRect), $"button does not overlap the player name {F(nameRect)}");
            }
            if (GlyphRect(gui.m_armor, out var armorRect))
            {
                c.Check(!b.Overlaps(armorRect), $"button does not overlap the armor number {F(armorRect)}");
            }
            if (GlyphRect(gui.m_weight, out var weightRect))
            {
                c.Check(!b.Overlaps(weightRect), $"button does not overlap the weight number {F(weightRect)}");
            }
            if (gui.m_crafting != null && gui.m_crafting.gameObject.activeInHierarchy)
            {
                var crafting = WorldRect(gui.m_crafting);
                c.Check(!b.Overlaps(crafting), $"button does not overlap the crafting panel {F(crafting)}");
            }
            if (gui.m_uiGroups != null && gui.m_uiGroups.Length > 2 && gui.m_uiGroups[2] != null)
            {
                var side = gui.m_uiGroups[2].GetComponentsInChildren<Selectable>(false);
                var hit = side.Where(s => s.transform is RectTransform && !s.transform.IsChildOf(button.transform)
                                          && b.Overlaps(WorldRect((RectTransform)s.transform))).Select(s => s.name).ToArray();
                c.Check(hit.Length == 0, $"button does not overlap the side buttons of group '{gui.m_uiGroups[2].name}' "
                                         + $"({side.Length} checked; overlapped: {string.Join(", ", hit)})");
            }
            c.Check(b.xMin >= 0f && b.yMin >= 0f && b.xMax <= Screen.width && b.yMax <= Screen.height, "button fully on screen");
            SelfTest.Screenshot(ButtonName, "inventory");
            yield return null;
            yield return null;

            // Clicks (T01): the three modes, message each time, Space / Enter never click it again.
            var top = TopHit(b.center);
            c.Check(top != null && top.transform.IsChildOf(button.transform),
                $"a mouse pointer on the button lands on the button (top hit: {(top != null ? top.name : "nothing")})");
            var modes = new[] { FilterMode.SkipIgnored, FilterMode.OnlySelected, FilterMode.Everything };
            var labels = new[] { "Auto pickup: Skip ignored", "Auto pickup: Only selected", "Auto pickup: Everything" };
            var texts = new[] { MsgSkip(0), MsgOnlyEmpty, MsgEverything };
            for (var i = 0; i < 3; i++)
            {
                Messages.Clear();
                // Worst case: the click left the button selected.
                es.SetSelectedGameObject(button.gameObject);
                Click(button);
                c.Check(FilterState.Mode == modes[i], $"click {i + 1}: mode is {FilterState.Mode}, expected {modes[i]}");
                c.Check(TopLeft(texts[i]) == 1, $"click {i + 1}: one top-left message '{texts[i]}' (got: {AllMessages()})");
                c.Check(es.currentSelectedGameObject != button.gameObject, $"click {i + 1}: the button is not left selected");
                // Space and Enter = submit to the selected object.
                var selected = es.currentSelectedGameObject;
                if (selected != null)
                {
                    ExecuteEvents.Execute(selected, new BaseEventData(es), ExecuteEvents.submitHandler);
                }
                c.Check(FilterState.Mode == modes[i], $"click {i + 1}: a submit (Space / Enter) after it does not change the mode again");
                yield return null;
                yield return null;
                c.Check(LabelOf(button) == labels[i], $"click {i + 1}: label is '{LabelOf(button)}', expected '{labels[i]}'");
            }
            // The last message really reach the top-left text of the HUD (one message per second there).
            var hud = MessageHud.instance;
            var until = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < until && hud != null && hud.m_messageText.text != MsgEverything)
            {
                yield return null;
            }
            c.Check(hud != null && hud.m_messageText.text == MsgEverything,
                $"the mode message is shown in the HUD's top-left text (it reads '{(hud != null ? hud.m_messageText.text : "")}')");

            // Tooltip (T01).
            var tip = button.GetComponent<UITooltip>();
            c.Check(tip != null && tip.m_topic == "Auto pickup filter", "tooltip topic is 'Auto pickup filter'");
            var text = TooltipOf(button);
            foreach (var part in new[]
                     {
                         "Click to change the mode.", "Everything: pick up every item, as in the normal game.",
                         "Skip ignored: pick up everything except ignored items (red mark).",
                         "Only selected: pick up only selected items (green mark).",
                         "Middle-click an item in your inventory or a chest to add it to or remove it from the list of the current mode.",
                         "Ignored (0): none", "Selected (0): none", "Auto pickup is on",
                     })
            {
                c.Check(text.Contains(part), $"tooltip has '{part}'");
            }
            FilterState.Toggle(FilterState.Ignored, "Stone");
            FilterState.Toggle(FilterState.Selected, "Coins");
            var stone = Shared("Stone");
            var coins = Shared("Coins");
            FilterState.SetMode(FilterMode.SkipIgnored);
            yield return null;
            yield return null;
            text = TooltipOf(button);
            var iIgnored = text.IndexOf("Ignored, in use now (1): " + stone, StringComparison.Ordinal);
            var iSelected = text.IndexOf("Selected (1): " + coins, StringComparison.Ordinal);
            c.Check(iIgnored >= 0 && iSelected > iIgnored, "Skip ignored: tooltip lists Ignored first, marked 'in use now', then Selected");
            FilterState.SetMode(FilterMode.OnlySelected);
            yield return null;
            yield return null;
            text = TooltipOf(button);
            iSelected = text.IndexOf("Selected, in use now (1): " + coins, StringComparison.Ordinal);
            iIgnored = text.IndexOf("Ignored (1): " + stone, StringComparison.Ordinal);
            c.Check(iSelected >= 0 && iIgnored > iSelected, "Only selected: tooltip lists Selected first, marked 'in use now', then Ignored");
            FilterState.SetMode(FilterMode.Everything);
            yield return null;
            yield return null;
            text = TooltipOf(button);
            c.Check(text.Contains("Ignored (1): " + stone) && text.Contains("Selected (1): " + coins) && !text.Contains("in use now"),
                "Everything: tooltip lists both lists, none 'in use now'");
            // What the hover box would show (vanilla tooltip object, text translated).
            if (tip != null)
            {
                tip.OnHoverStart(button.gameObject);
                var field = typeof(UITooltip).GetField("m_tooltip", BindingFlags.NonPublic | BindingFlags.Static);
                var box = field != null ? field.GetValue(null) as GameObject : null;
                var topicT = box != null ? Utils.FindChild(box.transform, "Topic") : null;
                var textT = box != null ? Utils.FindChild(box.transform, "Text") : null;
                var shownTopic = topicT != null ? topicT.GetComponent<TMP_Text>().text : "";
                var shownText = textT != null ? textT.GetComponent<TMP_Text>().text : "";
                c.Check(shownTopic == "Auto pickup filter" && shownText.Contains("Ignored (1): " + L(stone))
                        && shownText.Contains("Selected (1): " + L(coins)),
                    $"hovering fills the vanilla tooltip box with the topic and both lists (topic '{shownTopic}')");
                UITooltip.HideTooltip();
            }

            // T27: button stay clickable when another UI group is the active one.
            // A recipe click start with SetActiveGroup(crafting group) (InventoryGui.OnSelectedRecipe).
            if (gui.m_uiGroups != null && gui.m_uiGroups.Length > 3)
            {
                gui.SetActiveGroup(gui.m_uiGroups[3], false);
                yield return Frames(3);
                var before = FilterState.Mode;
                top = TopHit(WorldRect((RectTransform)button.transform).center);
                c.Check(button.IsInteractable(), "crafting group active (after a recipe click): the button is still interactable");
                c.Check(top != null && top.transform.IsChildOf(button.transform), "crafting group active: a pointer on the button still lands on it");
                Click(button);
                c.Check(FilterState.Mode != before, "crafting group active: a click still changes the mode");
                gui.SetActiveGroup(1, false);
                yield return Frames(2);
            }
            // Chest slot click: InventoryGui.OnSelectedItem make the chest grid's group the active one.
            yield return Close();
            var chest = rig.Spawn("piece_chest_wood", rig.Spot(1.6f), Quaternion.LookRotation(-rig.Forward));
            var container = chest != null ? chest.GetComponent<Container>() : null;
            if (c.Check(container != null, "spawned a wood chest (piece_chest_wood)"))
            {
                yield return Settle();
                yield return Open(container);
                yield return Frames(3);
                button = FindButton();
                c.Check(gui.IsContainerOpen() && gui.m_container.gameObject.activeInHierarchy, "chest panel is open");
                if (c.Check(button != null, "button is there with a chest open"))
                {
                    gui.OnSelectedItem(gui.ContainerGrid, null, new Vector2i(0, 0), InventoryGrid.Modifier.Select);
                    yield return Frames(3);
                    var before = FilterState.Mode;
                    c.Check(button.IsInteractable(), "after a chest slot click: the button is still interactable");
                    Click(button);
                    c.Check(FilterState.Mode != before, "after a chest slot click: a click still changes the mode");
                }
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.layout (T20)

    private static IEnumerator RunLayout()
    {
        if (!Ready(LayoutName, out var player, out var gui))
        {
            yield break;
        }
        var c = new Checks(LayoutName);
        var rig = new Rig(player, LayoutName);
        try
        {
            yield return Open();
            var button = FindButton();
            if (!c.Check(button != null, "button is there"))
            {
                c.Report();
                yield break;
            }
            var rt = (RectTransform)button.transform;
            var scale = gui.m_player.lossyScale.x;
            var r0 = WorldRect(rt);

            // Offsets: in memory + the entries' real change handlers. No reopen.
            TestHooks.ButtonOffsetX = 50f;
            TestHooks.ButtonOffsetY = 20f;
            c.Check(RaiseChanged(Plugin.ButtonOffsetX) && RaiseChanged(Plugin.ButtonOffsetY), "ButtonOffsetX / ButtonOffsetY change handlers ran");
            yield return null;
            yield return null;
            var r1 = WorldRect(rt);
            // The mod keeps the button on screen at the top and the sides: expected move is cut there.
            var wantX = Mathf.Min(50f * scale, Screen.width - r0.xMax);
            var wantY = Mathf.Min(20f * scale, Screen.height - r0.yMax);
            c.Check(Mathf.Abs(r1.xMin - r0.xMin - wantX) <= 1f && Mathf.Abs(r1.yMin - r0.yMin - wantY) <= 1f,
                $"offsets (50, 20) moved the button by ({F(r1.xMin - r0.xMin)}, {F(r1.yMin - r0.yMin)}) px, expected ({F(wantX)}, {F(wantY)}) at UI scale {F(scale)}");
            c.Check(r1.xMin > r0.xMin, "the button moved right");
            c.Check(InventoryGui.IsVisible() && ReferenceEquals(button, FindButton()), "same button, inventory never closed");
            TestHooks.ButtonOffsetX = 0f;
            TestHooks.ButtonOffsetY = 0f;
            RaiseChanged(Plugin.ButtonOffsetX);
            RaiseChanged(Plugin.ButtonOffsetY);
            yield return null;
            yield return null;
            var r2 = WorldRect(rt);
            c.Check(Mathf.Abs(r2.xMin - r0.xMin) <= 1f && Mathf.Abs(r2.yMin - r0.yMin) <= 1f, "offsets back to 0: the button is back at its place");

            // Taller inventory (inventorysize): only on a plain 4-row inventory (another mod may own the rows).
            var inv = rig.Inv;
            var rows = inv.GetHeight();
            var width = inv.GetWidth();
            if (rows == 4)
            {
                player.SetInventorySize(6);
                yield return Frames(5);
                var grid = gui.m_playerGrid;
                c.Check(grid.m_elements.Count == width * 6, $"6 rows: the grid has {grid.m_elements.Count} slots, expected {width * 6}");
                button = FindButton();
                if (c.Check(button != null && grid.m_elements.Count > 0, "6 rows: button is there"))
                {
                    var b = WorldRect((RectTransform)button.transform);
                    var panel = WorldRect(gui.m_player);
                    var first = WorldRect((RectTransform)grid.m_elements[0].transform);
                    c.Check(b.yMin >= panel.yMax - 1f && b.yMin - panel.yMax <= 24f, $"6 rows: button still just above the taller panel (gap {F(b.yMin - panel.yMax)} px)");
                    c.Check(Mathf.Abs(b.xMin - first.xMin) <= 1.5f, "6 rows: button still on the first slot's left edge");
                    c.Check(b.yMax <= Screen.height && b.xMin >= 0f, "6 rows: button on screen");
                }
                var stone = inv.AddItem("Stone", 3, 1, 0, 0L, "", new Vector2i(0, 5), false);
                FilterState.SetMode(FilterMode.SkipIgnored);
                FilterState.Toggle(FilterState.Ignored, "Stone");
                yield return Frames(3);
                c.Check(stone != null && stone.m_gridPos.y == 5 && BadgeOn(grid, stone, SpriteIgnored), "6 rows: ignored Stone in row 6 shows the red mark");
                if (stone != null)
                {
                    inv.RemoveItem(stone);
                }
                player.SetInventorySize(4);
                yield return Frames(5);
                button = FindButton();
                c.Check(grid.m_elements.Count == width * 4 && button != null, "back to 4 rows: grid and button back");
                if (button != null)
                {
                    var b = WorldRect((RectTransform)button.transform);
                    c.Check(Mathf.Abs(b.xMin - r0.xMin) <= 1f && Mathf.Abs(b.yMin - r0.yMin) <= 1f, "back to 4 rows: button at its first place");
                }
            }
            else
            {
                c.Note($"inventory has {rows} rows (another mod?): the inventorysize part was not run");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.marks (T07, T08, T29)

    private static IEnumerator RunMarks()
    {
        if (!Ready(MarksName, out var player, out var gui))
        {
            yield break;
        }
        var c = new Checks(MarksName);
        var rig = new Rig(player, MarksName);
        try
        {
            var names = new[] { "Stone", "Coins", "Wood", "Resin", "Flint", "Feathers" };
            var items = names.Select(n => rig.Give(n, 2)).ToArray();
            if (!c.Check(items.All(i => i != null), "six test items given"))
            {
                c.Report();
                yield break;
            }
            var stone = items[0];
            var coins = items[1];
            FilterState.Toggle(FilterState.Ignored, "Stone");
            FilterState.Toggle(FilterState.Selected, "Coins");
            yield return Open();
            var grid = gui.m_playerGrid;
            var button = FindButton();

            // T07: lists kept per mode, badge of the list in use only.
            FilterState.SetMode(FilterMode.SkipIgnored);
            yield return Frames(3);
            c.Check(BadgeOn(grid, stone, SpriteIgnored) && !BadgeOn(grid, coins) && BadgesOn(grid) == 1, "Skip ignored: only Stone has a mark, the red one");
            c.Check(TooltipOf(button).Contains("(1): " + Shared("Stone")) && TooltipOf(button).Contains("(1): " + Shared("Coins")), "Skip ignored: tooltip lists both lists");
            FilterState.SetMode(FilterMode.OnlySelected);
            yield return Frames(3);
            c.Check(BadgeOn(grid, coins, SpriteSelected) && !BadgeOn(grid, stone) && BadgesOn(grid) == 1, "Only selected: only Coins has a mark, the green one");
            c.Check(TooltipOf(button).Contains("(1): " + Shared("Stone")) && TooltipOf(button).Contains("(1): " + Shared("Coins")), "Only selected: tooltip lists both lists");
            FilterState.SetMode(FilterMode.Everything);
            yield return Frames(3);
            c.Check(BadgesOn(grid) == 0, "Everything: no mark");
            c.Check(TooltipOf(button).Contains("(1): " + Shared("Stone")) && TooltipOf(button).Contains("(1): " + Shared("Coins")), "Everything: tooltip lists both lists");
            c.Check(SameSet(FilterState.Ignored, "Stone") && SameSet(FilterState.Selected, "Coins"), "switching modes changed no list");

            // ShowMarkers off and on, live.
            FilterState.SetMode(FilterMode.SkipIgnored);
            yield return Frames(3);
            TestHooks.ShowMarkers = false;
            c.Check(RaiseChanged(Plugin.ShowMarkers), "ShowMarkers change handler ran");
            yield return Frames(2);
            c.Check(BadgesOn(grid) == 0, "ShowMarkers off: marks gone at once");
            TestHooks.ShowMarkers = true;
            RaiseChanged(Plugin.ShowMarkers);
            yield return Frames(2);
            c.Check(BadgeOn(grid, stone, SpriteIgnored), "ShowMarkers on: mark back");

            // T08: mark in Everything mode = hint only.
            FilterState.SetMode(FilterMode.Everything);
            yield return Frames(2);
            var ignoredBefore = FilterState.Join(FilterState.Ignored);
            var selectedBefore = FilterState.Join(FilterState.Selected);
            Messages.Clear();
            FilterUi.TestForgetMarkMessageTime();
            yield return MarkGesture(grid, items[2]);
            c.Check(TopLeft(MsgChooseMode) == 1, $"Everything: marking shows the hint (got: {AllMessages()})");
            c.Check(FilterState.Join(FilterState.Ignored) == ignoredBefore && FilterState.Join(FilterState.Selected) == selectedBefore, "Everything: lists unchanged");
            yield return null;
            c.Check(BadgesOn(grid) == 0, "Everything: no mark");

            // T29: six marks in a row = six badges at once, messages rate limited.
            FilterState.SetMode(FilterMode.SkipIgnored);
            FilterState.ClearList(FilterState.Ignored);
            yield return Frames(2);
            Messages.Clear();
            FilterUi.TestForgetMarkMessageTime();
            var start = Time.unscaledTime;
            foreach (var item in items)
            {
                yield return MarkGesture(grid, item);
            }
            var took = Time.unscaledTime - start;
            yield return null;
            c.Check(names.All(n => FilterState.Ignored.Contains(n)) && FilterState.Ignored.Count == 6, $"six items marked in {F(took)} s: all six in Ignored ({FilterState.Join(FilterState.Ignored)})");
            c.Check(items.All(i => BadgeOn(grid, i, SpriteIgnored)), "all six red marks shown");
            var said = TopLeftStarting("Ignored by auto pickup: ");
            var allowed = 1 + Mathf.FloorToInt(took);
            c.Check(said >= 1 && said <= allowed && said <= 2 + Mathf.FloorToInt(took / 2f),
                $"{said} mark message(s) for six marks in {F(took)} s (at most one per second: {allowed})");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.mark-colours (T03, T06, T07, T12)

    // "Red mark" / "green mark" of the items: the other tests know the mark by its sprite name only. Here the picture
    // itself is read back: ignored = mostly red, selected = mostly green, drawn full (no tint, not see-through).
    private static IEnumerator RunMarkColours()
    {
        if (!Ready(MarkColoursName, out var player, out var gui))
        {
            yield break;
        }
        var c = new Checks(MarkColoursName);
        var rig = new Rig(player, MarkColoursName);
        try
        {
            var stone = rig.Give("Stone", 2);
            var coins = rig.Give("Coins", 2);
            if (!c.Check(stone != null && coins != null, "Stone and Coins given"))
            {
                c.Report();
                yield break;
            }
            FilterState.Toggle(FilterState.Ignored, "Stone");
            FilterState.Toggle(FilterState.Selected, "Coins");
            FilterState.SetMode(FilterMode.SkipIgnored);
            yield return Open();
            yield return Frames(3);
            var grid = gui.m_playerGrid;
            var badge = BadgeOf(grid, stone);
            if (c.Check(BadgeOn(grid, stone, SpriteIgnored) && MarkColour(badge, out var red), "Skip ignored: the mark of the ignored Stone is shown and its picture can be read"))
            {
                MarkColour(badge, out red);
                c.Check(red.r > 0.3f && red.r > red.g * 1.4f && red.r > red.b * 1.4f, $"the mark of an ignored item is red (average colour r {F(red.r)}, g {F(red.g)}, b {F(red.b)})");
                c.Check(red.a > 0.9f && badge.canvasRenderer.GetAlpha() > 0.9f, $"it is drawn full, not see-through (alpha {F(red.a)})");
            }
            FilterState.SetMode(FilterMode.OnlySelected);
            yield return Frames(3);
            badge = BadgeOf(grid, coins);
            if (c.Check(BadgeOn(grid, coins, SpriteSelected) && MarkColour(badge, out var green), "Only selected: the mark of the selected Coins is shown and its picture can be read"))
            {
                MarkColour(badge, out green);
                c.Check(green.g > 0.3f && green.g > green.r * 1.4f && green.g > green.b * 1.4f, $"the mark of a selected item is green (average colour r {F(green.r)}, g {F(green.g)}, b {F(green.b)})");
                c.Check(green.a > 0.9f && badge.canvasRenderer.GetAlpha() > 0.9f, $"it is drawn full, not see-through (alpha {F(green.a)})");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.chest (T12)

    private static IEnumerator RunChest()
    {
        if (!Ready(ChestName, out var player, out var gui))
        {
            yield break;
        }
        var c = new Checks(ChestName);
        var rig = new Rig(player, ChestName);
        try
        {
            var chest = rig.Spawn("piece_chest_wood", rig.Spot(1.6f), Quaternion.LookRotation(-rig.Forward));
            var container = chest != null ? chest.GetComponent<Container>() : null;
            if (!c.Check(container != null, "spawned a wood chest (piece_chest_wood)"))
            {
                c.Report();
                yield break;
            }
            yield return Settle();
            var inChest = container.GetInventory().AddItem("Resin", 5, 1, 0, 0L, "", false);
            var carried = rig.Give("Resin", 3);
            FilterState.SetMode(FilterMode.SkipIgnored);
            yield return Open(container);
            yield return Frames(3);
            var chestGrid = gui.ContainerGrid;
            if (!c.Check(inChest != null && carried != null && gui.IsContainerOpen() && chestGrid != null && chestGrid.m_elements.Count > 0,
                    "chest panel open with Resin in it"))
            {
                c.Report();
                yield break;
            }
            Messages.Clear();
            yield return MarkGesture(chestGrid, inChest);
            yield return null;
            c.Check(FilterState.Ignored.Contains("Resin"), "middle-click on the chest slot put Resin in Ignored");
            c.Check(TopLeft("Ignored by auto pickup: " + Shared("Resin")) == 1, $"message 'Ignored by auto pickup: Resin' (got: {AllMessages()})");
            c.Check(BadgeOn(chestGrid, inChest, SpriteIgnored), "red mark on the chest slot");
            c.Check(BadgeOn(gui.m_playerGrid, carried, SpriteIgnored), "red mark on the Resin you carry");

            var before = Count(rig.Inv, "Resin");
            var drop = rig.Drop("Resin", 5, rig.Spot(0.9f, 0.5f, 0.3f));
            yield return new WaitForSeconds(2.5f);
            c.Check(Alive(drop) && Count(rig.Inv, "Resin") == before, "Resin on the ground next to you is not auto-picked");

            yield return Close();
            yield return Open(container);
            yield return Frames(3);
            c.Check(gui.IsContainerOpen() && BadgeOn(gui.ContainerGrid, inChest, SpriteIgnored), "chest closed and opened again: mark still on the chest slot");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.badges (T13)

    private static IEnumerator RunBadges() => BadgesCore(BadgesName, hotbarNumberOnly: false);

    // REAL BUG of 0.1.0, alone here so the other placement checks stay green. Round 1 (2560x1440): the mark sits in
    // the top-left corner of the slot, right on the hotbar number of the first row (screenshot
    // lootfilter.badges__marks: numbers 2, 3, 4 hidden under the marks). MarkerOverlay.TryPickCorner finds something in
    // every corner and takes the "least covered" one. FAIL until the mod keeps the hotbar number readable.
    private static IEnumerator RunBugBadgeHotbar() => BadgesCore(BugBadgeHotbarName, hotbarNumberOnly: true);

    // hotbarNumberOnly false: every slot part but the hotbar number. True: the hotbar number alone.
    private static IEnumerator BadgesCore(string name, bool hotbarNumberOnly)
    {
        if (!Ready(name, out var player, out var gui))
        {
            yield break;
        }
        var c = new Checks(name);
        var rig = new Rig(player, name);
        try
        {
            var inv = rig.Inv;
            // Top row = hotbar row (number 1-8 in the slot).
            var free = new List<int>();
            for (var x = 0; x < inv.GetWidth(); x++)
            {
                if (inv.GetItemAt(x, 0) == null)
                {
                    free.Add(x);
                }
            }
            if (!c.Check(free.Count >= 3, $"three free slots in the top row (found {free.Count})"))
            {
                c.Report();
                yield break;
            }
            var spear = inv.AddItem("SpearFlint", 1, 1, 0, 0L, "", new Vector2i(free[0], 0), false);
            var ore = inv.AddItem("CopperOre", 2, 1, 0, 0L, "", new Vector2i(free[1], 0), false);
            var arrows = inv.AddItem("ArrowWood", 20, 1, 0, 0L, "", new Vector2i(free[2], 0), false);
            if (!c.Check(spear != null && ore != null && arrows != null, "SpearFlint, CopperOre and ArrowWood given"))
            {
                c.Report();
                yield break;
            }
            player.EquipItem(spear, false);
            spear.m_durability = spear.GetMaxDurability() * 0.5f;
            FilterState.SetMode(FilterMode.SkipIgnored);
            FilterState.Toggle(FilterState.Ignored, "SpearFlint");
            FilterState.Toggle(FilterState.Ignored, "CopperOre");
            FilterState.Toggle(FilterState.Ignored, "ArrowWood");
            yield return Open();
            yield return Frames(4);
            var grid = gui.m_playerGrid;
            c.Note($"SpearFlint max quality {spear.m_shared.m_maxQuality}, teleportable CopperOre {ore.m_shared.m_teleportable}, "
                   + $"spear equipped {spear.m_equipped}, durability {F(spear.m_durability)}/{F(spear.GetMaxDurability())}");
            var seen = new HashSet<string>();
            foreach (var item in new[] { spear, ore, arrows })
            {
                var who = item.m_dropPrefab != null ? item.m_dropPrefab.name : "?";
                var element = ElementOf(grid, item);
                var badge = BadgeOf(grid, item);
                if (!c.Check(element != null && badge != null && badge.enabled, $"{who}: mark shown"))
                {
                    continue;
                }
                var box = WorldRect(badge.rectTransform);
                var slot = WorldRect((RectTransform)element.transform);
                if (!hotbarNumberOnly)
                {
                    c.Check(box.xMin >= slot.xMin - 0.5f && box.xMax <= slot.xMax + 0.5f && box.yMin >= slot.yMin - 0.5f && box.yMax <= slot.yMax + 0.5f,
                        $"{who}: mark inside its slot");
                    c.Check(!badge.raycastTarget, $"{who}: mark never takes clicks");
                }
                var parts = new List<KeyValuePair<string, Rect>>();
                if (element.m_noteleport != null && element.m_noteleport.enabled)
                {
                    parts.Add(new KeyValuePair<string, Rect>("no-portal icon", WorldRect(element.m_noteleport.rectTransform)));
                }
                if (element.m_equiped != null && element.m_equiped.enabled)
                {
                    var eq = WorldRect(element.m_equiped.rectTransform);
                    // Equipped marker that fill the slot is a background: nothing to hide.
                    if (eq.width * eq.height < slot.width * slot.height * 0.4f)
                    {
                        parts.Add(new KeyValuePair<string, Rect>("equipped marker", eq));
                    }
                    else
                    {
                        seen.Add("equipped marker (full slot, behind)");
                    }
                }
                if (element.m_durability != null && element.m_durability.gameObject.activeInHierarchy)
                {
                    parts.Add(new KeyValuePair<string, Rect>("durability bar", WorldRect((RectTransform)element.m_durability.transform)));
                }
                if (GlyphRect(element.m_quality, out var quality))
                {
                    parts.Add(new KeyValuePair<string, Rect>("quality number", quality));
                }
                if (GlyphRect(element.m_amount, out var amount))
                {
                    parts.Add(new KeyValuePair<string, Rect>("stack amount", amount));
                }
                var bindingT = element.transform.Find("binding");
                if (GlyphRect(bindingT != null ? bindingT.GetComponent<TMP_Text>() : null, out var binding))
                {
                    parts.Add(new KeyValuePair<string, Rect>("hotbar number", binding));
                }
                foreach (var part in parts)
                {
                    seen.Add(part.Key);
                    // The hotbar number has its own test (lootfilter.bug.badge-hotbar-number): not met in 0.1.0.
                    if (hotbarNumberOnly != (part.Key == "hotbar number"))
                    {
                        continue;
                    }
                    c.Check(!box.Overlaps(part.Value), $"{who}: mark {F(box)} does not cover the {part.Key} {F(part.Value)}");
                }
                var anchor = badge.rectTransform.anchorMin;
                c.Note($"{who}: mark corner {(anchor.y > 0.5f ? "top" : "bottom")}-{(anchor.x > 0.5f ? "right" : "left")}, slot parts shown: "
                       + string.Join(", ", parts.Select(p => p.Key).ToArray()));
            }
            if (hotbarNumberOnly)
            {
                c.Check(seen.Contains("hotbar number"), "the hotbar number was on screen for the check");
            }
            else
            {
                foreach (var need in new[] { "no-portal icon", "durability bar", "stack amount" })
                {
                    c.Check(seen.Contains(need), $"the {need} was on screen for the check");
                }
                c.Check(seen.Any(s => s.StartsWith("equipped marker", StringComparison.Ordinal)), "the equipped marker was on screen for the check");
                SelfTest.Screenshot(name, "marks");
                yield return null;
                yield return null;
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.outside-grid (C06)

    private static IEnumerator RunOutsideGrid()
    {
        if (!Ready(OutsideGridName, out var player, out var gui))
        {
            yield break;
        }
        var c = new Checks(OutsideGridName);
        var rig = new Rig(player, OutsideGridName);
        try
        {
            var inv = rig.Inv;
            var stone = rig.Give("Stone", 2);
            var wood = rig.Give("Wood", 2);
            if (!c.Check(stone != null && wood != null, "Stone and Wood given"))
            {
                c.Report();
                yield break;
            }
            FilterState.SetMode(FilterMode.SkipIgnored);
            FilterState.Toggle(FilterState.Ignored, "Stone");
            FilterState.Toggle(FilterState.Ignored, "Wood");
            yield return Open();
            yield return Frames(3);
            var grid = gui.m_playerGrid;
            c.Check(BadgeOn(grid, stone) && BadgeOn(grid, wood) && BadgesOn(grid) == 2, "two marked items, two marks");
            // Extended inventory mods keep items at grid places the vanilla grid has no slot for.
            var home = wood.m_gridPos;
            var errors = rig.Logs.Count(LogLevel.Error | LogLevel.Fatal);
            wood.m_gridPos = new Vector2i(0, inv.GetHeight());
            yield return Frames(6);
            c.Check(rig.NewGuardReports == 0 && rig.Logs.Count(LogLevel.Error | LogLevel.Fatal) == errors,
                $"marked item outside the grid: no error from the mod ({rig.Logs.First(LogLevel.Error | LogLevel.Fatal)})");
            c.Check(BadgeOn(grid, stone) && BadgesOn(grid) == 1, "marked item outside the grid: no mark for it, the other mark stays on its slot");
            wood.m_gridPos = new Vector2i(inv.GetWidth() + 3, 0);
            yield return Frames(4);
            c.Check(rig.NewGuardReports == 0 && BadgeOn(grid, stone), "marked item past the last column: no error, other mark right");
            wood.m_gridPos = home;
            yield return Frames(3);
            c.Check(BadgeOn(grid, stone) && BadgeOn(grid, wood) && BadgesOn(grid) == 2, "item back in the grid: both marks on the right slots");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.persist (T15, T16, M04)

    // "key" then "value" as Player.Save write them (length byte + UTF-8 each) somewhere in the blob?
    private static bool BlobHas(byte[] blob, string key, string value)
    {
        var a = System.Text.Encoding.UTF8.GetBytes(key);
        var b = System.Text.Encoding.UTF8.GetBytes(value);
        if (a.Length > 127 || b.Length > 127)
        {
            return false;
        }
        var needle = new byte[a.Length + b.Length + 2];
        needle[0] = (byte)a.Length;
        Array.Copy(a, 0, needle, 1, a.Length);
        needle[a.Length + 1] = (byte)b.Length;
        Array.Copy(b, 0, needle, a.Length + 2, b.Length);
        for (var i = 0; i + needle.Length <= blob.Length; i++)
        {
            var j = 0;
            while (j < needle.Length && blob[i + j] == needle[j])
            {
                j++;
            }
            if (j == needle.Length)
            {
                return true;
            }
        }
        return false;
    }

    // Mode and lists as the character carry them: in Player.m_customData and in what Player.Save write to the file.
    private static void CheckStored(Checks c, Player player, string mode, string ignored, string selected, string when)
    {
        var data = player.m_customData;
        data.TryGetValue(FilterState.KeyVersion, out var v);
        data.TryGetValue(FilterState.KeyMode, out var m);
        data.TryGetValue(FilterState.KeyIgnored, out var i);
        data.TryGetValue(FilterState.KeySelected, out var s);
        c.Check(v == "1" && m == mode && i == ignored && s == selected,
            $"{when}: character data holds Version=1, Mode={mode}, Ignored={ignored}, Selected={selected} (got {v}, {m}, {i}, {s})");
        var pkg = new ZPackage();
        player.Save(pkg);
        var blob = pkg.GetArray();
        c.Check(BlobHas(blob, FilterState.KeyMode, mode) && BlobHas(blob, FilterState.KeyIgnored, ignored)
                && BlobHas(blob, FilterState.KeySelected, selected) && BlobHas(blob, FilterState.KeyVersion, "1"),
            $"{when}: the character save data (Player.Save) carries the mode and both lists");
    }

    private static IEnumerator RunPersist()
    {
        if (!Ready(PersistName, out var player, out var gui))
        {
            yield break;
        }
        var c = new Checks(PersistName);
        var rig = new Rig(player, PersistName);
        try
        {
            // T15: what is saved, and loading it again (same path as a new body after login, respawn, restart).
            FilterState.SetMode(FilterMode.SkipIgnored);
            FilterState.Toggle(FilterState.Ignored, "Stone");
            FilterState.Toggle(FilterState.Ignored, "Resin");
            FilterState.Toggle(FilterState.Selected, "Coins");
            CheckStored(c, player, "SkipIgnored", "Resin,Stone", "Coins", "after setting the filter");
            var saved = Rig.FilterKeys.ToDictionary(k => k, k => player.m_customData[k]);
            FilterState.Reset();
            c.Check(FilterState.Mode == FilterMode.Everything && FilterState.Ignored.Count == 0 && FilterState.Selected.Count == 0, "filter forgotten in memory (like leaving the character)");
            FilterState.EnsureLocal();
            c.Check(FilterState.Mode == FilterMode.SkipIgnored && SameSet(FilterState.Ignored, "Stone", "Resin") && SameSet(FilterState.Selected, "Coins"),
                $"loaded again from the character: mode {FilterState.Mode}, ignored {FilterState.Join(FilterState.Ignored)}, selected {FilterState.Join(FilterState.Selected)}");
            var stone = rig.Give("Stone", 2);
            yield return Open();
            yield return Frames(3);
            c.Check(stone != null && BadgeOn(gui.m_playerGrid, stone, SpriteIgnored), "marks back after the reload");
            c.Check(LabelOf(FindButton()) == "Auto pickup: Skip ignored", "button label follows the reloaded mode");
            yield return Close();

            // T16: a character that never used the filter takes the config defaults, live, until its first change.
            foreach (var key in Rig.FilterKeys)
            {
                player.m_customData.Remove(key);
            }
            TestHooks.DefaultIgnored = "stone,Wood";
            TestHooks.DefaultSelected = "";
            FilterState.Reset();
            FilterState.EnsureLocal();
            c.Check(FilterState.Mode == FilterMode.Everything, "never-used character starts in Everything mode");
            c.Check(SameSet(FilterState.Ignored, "Stone", "Wood"), $"defaults 'stone,Wood' give Stone and Wood, exact spelling (got {FilterState.Join(FilterState.Ignored)})");
            c.Check(!Rig.FilterKeys.Any(k => player.m_customData.ContainsKey(k)), "nothing written to the character yet");
            var lines = Run(Console.instance, "lootfilter");
            c.Check(AnyLine(lines, "Ignored (2): Stone") && AnyLineHas(lines, "Wood"), $"lootfilter shows the default Ignored list ({Lines(lines)})");
            FilterState.SetMode(FilterMode.SkipIgnored);
            c.Check(SameSet(FilterState.Ignored, "Stone", "Wood"), "switching to Skip ignored: Stone and Wood already ignored");
            yield return Open();
            yield return Frames(2);
            var tip = TooltipOf(FindButton());
            c.Check(tip.Contains("Ignored, in use now (2): ") && tip.Contains(Shared("Stone")) && tip.Contains(Shared("Wood")), "tooltip shows the default Ignored list");
            yield return Close();

            // Second never-used character, still in Everything mode: a defaults edit applies at once.
            foreach (var key in Rig.FilterKeys)
            {
                player.m_customData.Remove(key);
            }
            FilterState.Reset();
            FilterState.EnsureLocal();
            TestHooks.DefaultIgnored = "Resin";
            c.Check(RaiseChanged(Plugin.DefaultIgnored), "Defaults.IgnoredItems change handler ran");
            c.Check(SameSet(FilterState.Ignored, "Resin"), $"defaults changed to Resin: list follows at once (got {FilterState.Join(FilterState.Ignored)})");
            lines = Run(Console.instance, "lootfilter");
            c.Check(AnyLine(lines, "Ignored (1): Resin"), $"lootfilter shows Resin at once ({Lines(lines)})");
            // After its first own change the character keeps its lists.
            FilterState.CycleMode();
            c.Check(player.m_customData.ContainsKey(FilterState.KeyMode), "first mode change writes the filter to the character");
            TestHooks.DefaultIgnored = "Flint";
            RaiseChanged(Plugin.DefaultIgnored);
            c.Check(SameSet(FilterState.Ignored, "Resin"), "defaults changed again: this character keeps its own list");

            // The first character still has its own lists (config defaults never touch a used character).
            foreach (var pair in saved)
            {
                player.m_customData[pair.Key] = pair.Value;
            }
            FilterState.Reset();
            FilterState.EnsureLocal();
            c.Check(FilterState.Mode == FilterMode.SkipIgnored && SameSet(FilterState.Ignored, "Stone", "Resin") && SameSet(FilterState.Selected, "Coins"),
                "a character that used the filter keeps its lists whatever the defaults say");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.future-data (T40)

    // Character last played with a newer version of the mod (other format number, mode name this version never heard
    // of): it must load, keep its lists, and say so in the log.
    private static IEnumerator RunFutureData()
    {
        if (!Ready(FutureDataName, out var player, out _))
        {
            yield break;
        }
        var c = new Checks(FutureDataName);
        var rig = new Rig(player, FutureDataName);
        try
        {
            var data = player.m_customData;
            data[FilterState.KeyVersion] = "7";
            data[FilterState.KeyMode] = "SomeNewMode";
            data[FilterState.KeyIgnored] = "Stone, Resin ,";
            data[FilterState.KeySelected] = "Coins";
            FilterState.Reset();
            var loaded = FilterState.EnsureLocal();
            c.Check(loaded && FilterState.Mode == FilterMode.Everything, $"unknown mode name: the filter falls back to Everything (mode {FilterState.Mode})");
            c.Check(SameSet(FilterState.Ignored, "Stone", "Resin") && SameSet(FilterState.Selected, "Coins"),
                $"both lists are read, spaces and empty entries dropped (ignored {FilterState.Join(FilterState.Ignored)}; selected {FilterState.Join(FilterState.Selected)})");
            c.Check(rig.Logs.Count(LogLevel.Warning, "has format 7 (this version reads 1)") == 1, "one warning about the newer data format");
            c.Check(rig.NewGuardReports == 0 && rig.Logs.Count(LogLevel.Error | LogLevel.Fatal) == 0, $"no error from the mod ({rig.Logs.First(LogLevel.Error | LogLevel.Fatal)})");
            yield return Open();
            yield return Frames(2);
            c.Check(LabelOf(FindButton()) == "Auto pickup: Everything", $"the button shows Everything ('{LabelOf(FindButton())}')");
            c.Check(TooltipOf(FindButton()).Contains("Ignored (2): ") && TooltipOf(FindButton()).Contains("Selected (1): " + Shared("Coins")), "the tooltip lists the kept lists");
            // Auto pickup behaves like the normal game in that state.
            yield return Close();
            var before = Count(rig.Inv, "Stone");
            var drop = rig.Drop("Stone", 3, rig.Spot(0.9f, 0.4f, 0.3f));
            var one = new List<ItemDrop> { drop };
            yield return WaitGone(one, 4f);
            c.Check(!Alive(drop) && Count(rig.Inv, "Stone") == before + 3, "Everything mode applies: Stone (in the kept Ignored list) is picked up");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.commands (T17)

    // Tab in a terminal: same call as the Tab key (Terminal.tabCycle with the command's options), on the shown input
    // field. result[0] = input text after it; null = the caret could not be put at the end of the typed text (then the
    // vanilla step cannot be played here).
    private static IEnumerator TabComplete(Terminal terminal, string typed, string word, List<string> options, string[] result)
    {
        result[0] = null;
        var input = terminal != null ? terminal.m_input : null;
        if (input == null || options == null)
        {
            yield break;
        }
        var window = terminal.m_chatWindow.gameObject;
        var windowOn = window.activeSelf;
        var inputOn = input.gameObject.activeSelf;
        var old = input.text;
        var chat = terminal as Chat;
        try
        {
            if (chat != null)
            {
                chat.m_hideTimer = 0f; // chat window shows while this timer is young
            }
            window.SetActive(true);
            input.gameObject.SetActive(true);
            input.text = typed;
            yield return null;
            yield return null;
            input.caretPosition = typed.Length;
            if (input.caretPosition == typed.Length)
            {
                terminal.m_tabCaretPosition = -1;
                terminal.tabCycle(word, options, false);
                result[0] = input.text;
            }
        }
        finally
        {
            input.text = old;
            terminal.m_tabCaretPosition = -1;
            input.gameObject.SetActive(inputOn);
            window.SetActive(windowOn);
        }
    }

    private static IEnumerator RunCommands()
    {
        if (!Ready(CommandsName, out var player, out var gui))
        {
            yield break;
        }
        var chat = Chat.instance;
        var con = Console.instance;
        if (chat == null || con == null)
        {
            SelfTest.Fail(CommandsName, "no chat or no console");
            yield break;
        }
        var c = new Checks(CommandsName);
        var rig = new Rig(player, CommandsName);
        try
        {
            var all = new List<string>();
            yield return Open();
            var button = FindButton();

            // Chat (what "/lootfilter" + Enter runs: the text without the slash).
            var lines = Run(chat, "lootfilter");
            all.AddRange(lines);
            c.Check(AnyLine(lines, ModInfo.Name + ": mode Everything, auto pickup on."), $"status: mode and auto pickup on ({Lines(lines)})");
            c.Check(AnyLine(lines, "Ignored (0): none") && AnyLine(lines, "Selected (0): none"), "status: both lists");
            c.Check(AnyLine(lines, "Commands: lootfilter [item] | lootfilter mode everything|skip|only | lootfilter clear ignored|selected | lootfilter defaults | lootfilter_ignore <item> | lootfilter_select <item>"), "status: command list");

            lines = Run(chat, "lootfilter_ignore resin");
            all.AddRange(lines);
            c.Check(FilterState.Ignored.Contains("Resin"), "lootfilter_ignore resin: Resin (exact name) in Ignored");
            c.Check(AnyLine(lines, "Ignored by auto pickup: Resin"), $"lootfilter_ignore resin: says so with the exact name ({Lines(lines)})");
            c.Check(AnyLine(lines, "(The current mode is Everything: this list applies in Skip ignored mode.)"), "lootfilter_ignore resin: note that Ignored is not the list in use");

            lines = Run(chat, "lootfilter_select Amber");
            all.AddRange(lines);
            c.Check(FilterState.Selected.Contains("Amber") && AnyLine(lines, "Selected for auto pickup: Amber"), $"lootfilter_select Amber: Amber in Selected ({Lines(lines)})");
            c.Check(AnyLine(lines, "(The current mode is Everything: this list applies in Only selected mode.)"), "lootfilter_select Amber: note that Selected is not the list in use");

            FilterState.SetMode(FilterMode.SkipIgnored);
            lines = Run(chat, "lootfilter Wood");
            all.AddRange(lines);
            c.Check(FilterState.Ignored.Contains("Wood") && AnyLine(lines, "Ignored by auto pickup: Wood") && !AnyLine(lines, "(The current mode"),
                $"Skip ignored: lootfilter Wood adds Wood to Ignored, no note ({Lines(lines)})");
            lines = Run(chat, "lootfilter Wood");
            all.AddRange(lines);
            c.Check(!FilterState.Ignored.Contains("Wood") && AnyLine(lines, "No longer ignored: Wood"), "Skip ignored: lootfilter Wood again removes it");

            FilterState.SetMode(FilterMode.Everything);
            var ignored = FilterState.Join(FilterState.Ignored);
            var selected = FilterState.Join(FilterState.Selected);
            lines = Run(chat, "lootfilter Wood");
            all.AddRange(lines);
            c.Check(AnyLine(lines, "The filter mode is Everything, so there is no list to change."), $"Everything: lootfilter Wood explains ({Lines(lines)})");
            c.Check(FilterState.Join(FilterState.Ignored) == ignored && FilterState.Join(FilterState.Selected) == selected, "Everything: lootfilter Wood changes no list");

            lines = Run(chat, "lootfilter_ignore Nonsense");
            all.AddRange(lines);
            c.Check(AnyLine(lines, "Unknown item: Nonsense (use the spawn name; press Tab to complete)") && FilterState.Join(FilterState.Ignored) == ignored,
                $"unknown item refused ({Lines(lines)})");

            // Console.
            Messages.Clear();
            lines = Run(con, "lootfilter mode only");
            all.AddRange(lines);
            c.Check(FilterState.Mode == FilterMode.OnlySelected && AnyLine(lines, MsgOnly(1)), $"lootfilter mode only switches the mode ({Lines(lines)})");
            c.Check(TopLeft(MsgOnly(1)) == 1, "lootfilter mode only: mode message top-left too");
            yield return null;
            yield return null;
            c.Check(LabelOf(button) == "Auto pickup: Only selected", $"the button follows (label '{LabelOf(button)}')");

            lines = Run(con, "lootfilter clear selected");
            all.AddRange(lines);
            c.Check(FilterState.Selected.Count == 0 && AnyLine(lines, "Loot filter: Selected list cleared (1 removed)."), $"lootfilter clear selected ({Lines(lines)})");

            TestHooks.DefaultIgnored = "stone";
            TestHooks.DefaultSelected = "Coins,amber";
            lines = Run(con, "lootfilter defaults");
            all.AddRange(lines);
            c.Check(SameSet(FilterState.Ignored, "Stone") && SameSet(FilterState.Selected, "Coins", "Amber")
                    && AnyLine(lines, "Loot filter lists replaced by the Defaults of the config file: 1 ignored, 2 selected."),
                $"lootfilter defaults copies the config defaults ({Lines(lines)})");

            // Item you do not carry leaves the tooltip list.
            yield return null;
            yield return null;
            c.Check(TooltipOf(button).Contains(Shared("Stone")), "tooltip lists Stone (not carried)");
            lines = Run(chat, "lootfilter_ignore Stone");
            all.AddRange(lines);
            yield return null;
            yield return null;
            c.Check(!FilterState.Ignored.Contains("Stone") && AnyLine(lines, "No longer ignored: Stone") && !TooltipOf(button).Contains(Shared("Stone")),
                "lootfilter_ignore Stone removes it; gone from the tooltip");

            // T39: item of a mod that is not installed now: kept as typed, shown by its name, removable by command.
            TestHooks.DefaultIgnored = "stone,MC_NotInstalledItem";
            lines = Run(con, "lootfilter defaults");
            all.AddRange(lines);
            c.Check(SameSet(FilterState.Ignored, "Stone", "MC_NotInstalledItem"), $"an item name the game does not know is kept as typed ({FilterState.Join(FilterState.Ignored)})");
            lines = Run(con, "lootfilter");
            all.AddRange(lines);
            c.Check(AnyLineHas(lines, "MC_NotInstalledItem"), $"status lists it by its name ({Lines(lines)})");
            yield return null;
            yield return null;
            c.Check(TooltipOf(button).Contains("MC_NotInstalledItem"), "tooltip lists it by its name");
            lines = Run(chat, "lootfilter_ignore mc_notinstalleditem");
            all.AddRange(lines);
            c.Check(!FilterState.Ignored.Contains("MC_NotInstalledItem") && AnyLine(lines, "No longer ignored: MC_NotInstalledItem"), $"it can be removed by command, any casing ({Lines(lines)})");
            lines = Run(chat, "lootfilter_ignore MC_NotInstalledItem");
            all.AddRange(lines);
            c.Check(!FilterState.Ignored.Contains("MC_NotInstalledItem") && AnyLine(lines, "Unknown item: MC_NotInstalledItem"), "once removed it is an unknown item again");

            // Tab completion: options, and the vanilla Tab step itself in console and chat.
            Terminal.commands.TryGetValue("lootfilter", out var main);
            Terminal.commands.TryGetValue("lootfilter_ignore", out var ign);
            Terminal.commands.TryGetValue("lootfilter_select", out var sel);
            if (c.Check(main != null && ign != null && sel != null, "the three commands are registered"))
            {
                var mainOptions = main.GetTabOptions();
                var itemOptions = ign.GetTabOptions();
                c.Check(itemOptions != null && itemOptions.Contains("Raspberry") && sel.GetTabOptions().Contains("Raspberry"), "Tab options of lootfilter_ignore / lootfilter_select hold the item names");
                c.Check(mainOptions != null && mainOptions.Contains("mode") && mainOptions.Contains("clear") && mainOptions.Contains("defaults") && mainOptions.Contains("Raspberry"),
                    "Tab options of lootfilter hold mode, clear, defaults and the item names");
                // The vanilla Tab step with our options (played when the caret can be put in the input field).
                var done = new string[1];
                var played = 0;
                yield return TabComplete(con, "lootfilter_ignore Rasp", "Rasp", itemOptions, done);
                if (done[0] != null)
                {
                    played++;
                    c.Check(done[0] == "lootfilter_ignore Raspberry", $"console: 'lootfilter_ignore Rasp' + Tab gives '{done[0]}'");
                }
                yield return TabComplete(con, "lootfilter Rasp", "Rasp", mainOptions, done);
                if (done[0] != null)
                {
                    played++;
                    c.Check(done[0] == "lootfilter Raspberry", $"console: 'lootfilter Rasp' + Tab gives '{done[0]}'");
                }
                yield return TabComplete(chat, "/lootfilter_ignore Rasp", "Rasp", itemOptions, done);
                if (done[0] != null)
                {
                    played++;
                    c.Check(done[0] == "/lootfilter_ignore Raspberry", $"chat: '/lootfilter_ignore Rasp' + Tab gives '{done[0]}'");
                }
                c.Note($"vanilla Tab step played in {played} of 3 input fields (0 = the caret could not be placed by code; the options above are checked either way)");
                // No cheat: allowed in chat, valid without devcommands, never the cheat prompt.
                c.Check(!main.IsCheat && !ign.IsCheat && !sel.IsCheat, "commands are not cheats");
                c.Check(main.IsValid(chat) && ign.IsValid(chat) && sel.IsValid(chat) && main.IsValid(con), $"commands valid in chat and console (console cheats on: {con.IsCheatsEnabled()})");
            }
            var prompt = L("$achievements_confirm_cheat");
            c.Check(!all.Any(l => l == prompt), "no cheat prompt in any answer");
            c.Check(rig.NewGuardReports == 0, "no command failed inside the mod");
            c.Check(rig.Logs.Count(LogLevel.Error | LogLevel.Fatal) == 0, $"no error line of the mod in the log during the commands ({rig.Logs.First(LogLevel.Error | LogLevel.Fatal)})");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.gamepad (T18, T35)

    private static GameObject ListsPanel() => GameObject.Find(ListsObject);

    private static string ListsText(GameObject panel)
    {
        var t = panel != null ? Utils.FindChild(panel.transform, "Text") : null;
        var text = t != null ? t.GetComponent<TMP_Text>() : null;
        return text != null ? text.text : "";
    }

    private static IEnumerator RunGamepad()
    {
        if (!Ready(GamepadName, out var player, out var gui))
        {
            yield break;
        }
        var c = new Checks(GamepadName);
        var rig = new Rig(player, GamepadName);
        try
        {
            var stone = rig.Give("Stone", 3);
            if (!c.Check(stone != null, "Stone given"))
            {
                c.Report();
                yield break;
            }
            // Controller alone in use (TestHooks stand in for ZInput).
            TestHooks.PadActive = true;
            TestHooks.PadOnly = true;
            FilterState.SetMode(FilterMode.SkipIgnored);
            yield return Open();
            yield return Frames(3);
            var grid = gui.m_playerGrid;
            c.Check(grid.m_uiGroup != null && grid.m_uiGroup.IsActive, "your inventory grid is the focused group");

            // Right stick click = mark / unmark the selected item.
            Messages.Clear();
            FilterUi.TestForgetMarkMessageTime();
            yield return StickGesture(grid, stone);
            c.Check(FilterState.Ignored.Contains("Stone") && TopLeft("Ignored by auto pickup: " + Shared("Stone")) == 1, $"right stick click marks the selected item (got: {AllMessages()})");
            yield return null;
            c.Check(BadgeOn(grid, stone, SpriteIgnored), "its red mark shows");
            FilterUi.TestForgetMarkMessageTime();
            yield return StickGesture(grid, stone);
            c.Check(!FilterState.Ignored.Contains("Stone") && TopLeft("No longer ignored: " + Shared("Stone")) == 1, "right stick click again unmarks it");

            // LT + right stick click = next mode, with its message.
            Messages.Clear();
            TestHooks.LeftTrigger = true;
            yield return StickGesture(grid, stone);
            TestHooks.LeftTrigger = false;
            c.Check(FilterState.Mode == FilterMode.OnlySelected && TopLeft(MsgOnlyEmpty) == 1, $"LT + right stick click cycles the mode with its message (mode {FilterState.Mode}; got: {AllMessages()})");
            c.Check(FilterState.Ignored.Count == 0 && FilterState.Selected.Count == 0, "LT + right stick click marks nothing");

            // RT + right stick click = lists panel (T18), controller wording (T35).
            TestHooks.RightTrigger = true;
            yield return StickGesture(grid, stone);
            var panel = ListsPanel();
            if (c.Check(panel != null && panel.activeInHierarchy && panel.transform.childCount > 0, "RT + right stick click opens the lists panel"))
            {
                yield return null;
                var box = WorldRect((RectTransform)panel.transform.GetChild(0));
                c.Note($"lists panel {F(box)}, slots {F(SlotArea(grid))}, screen {Screen.width}x{Screen.height}");
                c.Check(box.xMin >= -0.5f && box.yMin >= -0.5f && box.xMax <= Screen.width + 0.5f && box.yMax <= Screen.height + 0.5f, "lists panel fully on screen");
                // "Next to the button, not over the grid" has its own test (lootfilter.bug.lists-panel-over-grid):
                // not met in 0.1.0.
                var text = ListsText(panel);
                c.Check(text.Contains("Hold LT and click the right stick to change the mode.") && text.Contains("Right stick click on an item in your inventory")
                        && text.Contains("RT + right stick click closes this list."), "controller only: panel explains LT + stick, stick, RT + stick");
                c.Check(!text.Contains("Click to change the mode") && !text.Contains("Middle-click"), "controller only: no mouse wording");
                c.Check(text.Contains("Selected, in use now (0): none") && text.Contains("Ignored (0): none"), "panel lists both lists");
                // Mouse touched: controller is no longer alone.
                TestHooks.PadOnly = false;
                yield return null;
                yield return null;
                text = ListsText(panel);
                c.Check(text.Contains("Click to change the mode.") && text.Contains("Middle-click"), "mouse moved: panel switches to the mouse wording");
                TestHooks.PadOnly = true;
                yield return null;
                yield return null;
                text = ListsText(panel);
                c.Check(text.Contains("Hold LT and click the right stick to change the mode.") && !text.Contains("Click to change the mode"), "controller again: wording switches back");
                SelfTest.Screenshot(GamepadName, "lists-panel");
                yield return null;
                yield return null;
            }
            yield return StickGesture(grid, stone);
            c.Check(ListsPanel() == null, "RT + right stick click again closes the panel");
            yield return StickGesture(grid, stone);
            c.Check(ListsPanel() != null, "panel open again");
            TestHooks.RightTrigger = false;
            yield return Close();
            c.Check(ListsPanel() == null, "closing the inventory closes the panel");
            yield return Open();
            yield return Frames(3);
            c.Check(ListsPanel() == null, "inventory opened again: panel stays closed");

            // Everything mode: hint with the controller wording.
            FilterState.SetMode(FilterMode.Everything);
            yield return null;
            Messages.Clear();
            FilterUi.TestForgetMarkMessageTime();
            yield return StickGesture(grid, stone);
            c.Check(TopLeft(MsgChooseModePad) == 1 && FilterState.Ignored.Count == 0 && FilterState.Selected.Count == 0, $"Everything: right stick click shows the controller hint, marks nothing (got: {AllMessages()})");

            // D-pad can never land on the button.
            var button = FindButton();
            c.Check(button != null && button.GetComponentsInChildren<Selectable>(true).All(s => s.navigation.mode == Navigation.Mode.None), "button takes no D-pad navigation");

            // GamepadControls off: right stick does nothing.
            FilterState.SetMode(FilterMode.SkipIgnored);
            TestHooks.GamepadControls = false;
            c.Check(RaiseChanged(Plugin.GamepadControls), "GamepadControls change handler ran");
            Messages.Clear();
            FilterUi.TestForgetMarkMessageTime();
            yield return StickGesture(grid, stone);
            TestHooks.LeftTrigger = true;
            yield return StickGesture(grid, stone);
            TestHooks.LeftTrigger = false;
            c.Check(FilterState.Ignored.Count == 0 && FilterState.Mode == FilterMode.SkipIgnored && OurMessages() == 0, "GamepadControls off: right stick clicks do nothing");
            TestHooks.GamepadControls = true;
            RaiseChanged(Plugin.GamepadControls);

            // Chest grid focused: nothing of ours happens.
            yield return Close();
            var chest = rig.Spawn("piece_chest_wood", rig.Spot(1.6f), Quaternion.LookRotation(-rig.Forward));
            var container = chest != null ? chest.GetComponent<Container>() : null;
            if (c.Check(container != null, "spawned a wood chest"))
            {
                yield return Settle();
                container.GetInventory().AddItem("Resin", 2, 1, 0, 0L, "", false);
                yield return Open(container);
                yield return Frames(3);
                gui.SetActiveGroup(0, false);
                yield return Frames(3);
                c.Check(gui.IsContainerOpen() && !grid.m_uiGroup.IsActive, "chest grid is the focused group");
                Messages.Clear();
                FilterUi.TestForgetMarkMessageTime();
                yield return StickGesture(grid, stone);
                TestHooks.LeftTrigger = true;
                yield return StickGesture(grid, stone);
                TestHooks.LeftTrigger = false;
                TestHooks.RightTrigger = true;
                yield return StickGesture(grid, stone);
                TestHooks.RightTrigger = false;
                c.Check(FilterState.Ignored.Count == 0 && FilterState.Mode == FilterMode.SkipIgnored && OurMessages() == 0 && ListsPanel() == null,
                    $"chest grid focused: right stick clicks do nothing of ours (got: {AllMessages()})");
            }
            c.Check(rig.NewGuardReports == 0, "no error inside the mod");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.bug.lists-panel-over-grid (T18)

    // REAL BUG of 0.1.0, alone here so the other controller checks stay green. Round 1 (2560x1440): the lists panel
    // (62.67,702.41)-(476,1440) lies over the Auto pickup button and over the left half of the slots
    // (62.67,1016)-(801.33,1381.33), screenshot lootfilter.gamepad__lists-panel. FilterUi.PositionListsPanel puts the
    // panel's bottom-left on the button's top-left, where there are 3 px of screen left, then Utils.ClampUIToScreen
    // pushes it down. FAIL until the panel opens next to the button and off the grid (TESTING.md T18).
    private static IEnumerator RunBugListsPanel()
    {
        if (!Ready(BugListsPanelName, out var player, out var gui))
        {
            yield break;
        }
        var c = new Checks(BugListsPanelName);
        var rig = new Rig(player, BugListsPanelName);
        try
        {
            var stone = rig.Give("Stone", 3);
            if (!c.Check(stone != null, "Stone given"))
            {
                c.Report();
                yield break;
            }
            TestHooks.PadActive = true;
            TestHooks.PadOnly = true;
            FilterState.SetMode(FilterMode.SkipIgnored);
            yield return Open();
            yield return Frames(3);
            var grid = gui.m_playerGrid;
            var button = FindButton();
            TestHooks.RightTrigger = true;
            yield return StickGesture(grid, stone);
            TestHooks.RightTrigger = false;
            var panel = ListsPanel();
            if (!c.Check(button != null && panel != null && panel.activeInHierarchy && panel.transform.childCount > 0 && grid.m_elements.Count > 0,
                    "RT + right stick click opens the lists panel"))
            {
                c.Report();
                yield break;
            }
            yield return null;
            var box = WorldRect((RectTransform)panel.transform.GetChild(0));
            var slots = SlotArea(grid);
            var b = WorldRect((RectTransform)button.transform);
            c.Check(!box.Overlaps(slots), $"lists panel {F(box)} is not over the grid {F(slots)} (screen {Screen.width}x{Screen.height})");
            c.Check(!box.Overlaps(b), $"lists panel is next to the Auto pickup button {F(b)}, not over it");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.markkey (T19, T34)

    private static IEnumerator RunMarkKey()
    {
        if (!Ready(MarkKeyName, out var player, out var gui))
        {
            yield break;
        }
        var c = new Checks(MarkKeyName);
        var rig = new Rig(player, MarkKeyName);
        try
        {
            var stone = rig.Give("Stone", 3);
            if (!c.Check(stone != null, "Stone given"))
            {
                c.Report();
                yield break;
            }
            FilterState.SetMode(FilterMode.SkipIgnored);
            yield return Open();
            yield return Frames(2);
            var grid = gui.m_playerGrid;
            var button = FindButton();

            // T19: keyboard key L.
            TestHooks.MarkKey = new KeyboardShortcut(KeyCode.L);
            c.Check(RaiseChanged(Plugin.MarkKey), "MarkKey change handler ran");
            yield return null;
            yield return null;
            c.Check(TooltipOf(button).Contains("Hover an item in your inventory or a chest and press L to add it to or remove it from the list of the current mode."),
                "MarkKey L: tooltip says 'Hover an item ... and press L'");
            FilterUi.TestForgetMarkMessageTime();
            yield return MarkGesture(grid, stone);
            c.Check(FilterState.Ignored.Contains("Stone"), "MarkKey L: pressing it over a slot marks the item");
            // Console open: the key is being typed there.
            var window = Console.instance != null ? Console.instance.m_chatWindow.gameObject : null;
            if (c.Check(window != null, "console window found"))
            {
                window.SetActive(true);
                yield return null;
                c.Check(Console.IsVisible(), "console is open");
                yield return MarkGesture(grid, stone);
                c.Check(FilterState.Ignored.Contains("Stone"), "console open: the key press over a slot toggles no mark");
                window.SetActive(false);
                yield return null;
            }
            yield return MarkGesture(grid, stone);
            c.Check(!FilterState.Ignored.Contains("Stone"), "console closed: the key works again (unmarked)");

            // T34: keys the game cannot read.
            var warnings = rig.Logs.Count(LogLevel.Warning);
            TestHooks.MarkKey = new KeyboardShortcut(KeyCode.F13);
            RaiseChanged(Plugin.MarkKey);
            c.Check(rig.Logs.Count(LogLevel.Warning) == warnings + 1 && rig.Logs.Count(LogLevel.Warning, "Controls.MarkKey = F13: the game cannot read the key F13") == 1,
                "MarkKey F13: one warning naming the key");
            yield return null;
            yield return null;
            c.Check(TooltipOf(button).Contains("Marking with the mouse or keyboard is off: the game cannot read the MarkKey 'F13'. Pick another key."), "MarkKey F13: tooltip says marking is off");
            yield return MarkGesture(grid, stone);
            c.Check(!FilterState.Ignored.Contains("Stone"), "MarkKey F13: no mark from mouse or keyboard");
            yield return Frames(60);
            c.Check(rig.Logs.Count(LogLevel.Warning) == warnings + 1, "MarkKey F13: the warning is not repeated while the inventory stays open");
            c.Check(rig.NewGuardReports == 0 && rig.Logs.Count(LogLevel.Error | LogLevel.Fatal) == 0,
                $"MarkKey F13: no error from the mod, no Update_Postfix / HandleMarkKey report ({rig.Logs.First(LogLevel.Error | LogLevel.Fatal)})");

            TestHooks.MarkKey = new KeyboardShortcut(KeyCode.Mouse5);
            RaiseChanged(Plugin.MarkKey);
            c.Check(rig.Logs.Count(LogLevel.Warning) == warnings + 2 && rig.Logs.Count(LogLevel.Warning, "Controls.MarkKey = Mouse5: the game cannot read the key Mouse5") == 1,
                "MarkKey Mouse5: one warning naming the key");
            yield return null;
            yield return null;
            c.Check(TooltipOf(button).Contains("the game cannot read the MarkKey 'Mouse5'"), "MarkKey Mouse5: tooltip says marking is off");
            yield return Frames(30);
            c.Check(rig.NewGuardReports == 0 && rig.Logs.Count(LogLevel.Error | LogLevel.Fatal) == 0 && rig.Logs.Count(LogLevel.Warning) == warnings + 2, "MarkKey Mouse5: no error, warning not repeated");

            // Controller gestures still work with the unreadable key (TestHooks play the controller).
            TestHooks.PadActive = true;
            TestHooks.PadOnly = true;
            yield return StickGesture(grid, stone);
            c.Check(FilterState.Ignored.Contains("Stone"), "unreadable MarkKey: right stick click still marks");
            TestHooks.LeftTrigger = true;
            yield return StickGesture(grid, stone);
            TestHooks.LeftTrigger = false;
            c.Check(FilterState.Mode == FilterMode.OnlySelected, "unreadable MarkKey: LT + right stick click still cycles the mode");
            TestHooks.PadActive = false;
            TestHooks.PadOnly = false;
            var lines = Run(Console.instance, "lootfilter mode skip");
            yield return null;
            yield return null;
            c.Check(FilterState.Mode == FilterMode.SkipIgnored && LabelOf(button) == "Auto pickup: Skip ignored", $"unreadable MarkKey: label follows 'lootfilter mode skip' ('{LabelOf(button)}'; {Lines(lines)})");

            // Back to the middle mouse button.
            TestHooks.MarkKey = new KeyboardShortcut(KeyCode.Mouse2);
            RaiseChanged(Plugin.MarkKey);
            yield return null;
            yield return null;
            c.Check(TooltipOf(button).Contains("Middle-click an item in your inventory or a chest"), "MarkKey Mouse2: tooltip back to 'Middle-click'");
            yield return MarkGesture(grid, stone);
            c.Check(!FilterState.Ignored.Contains("Stone"), "MarkKey Mouse2: middle-click marks again (Stone unmarked)");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.dialogs (T26)

    private static IEnumerator RunDialogs()
    {
        if (!Ready(DialogsName, out var player, out var gui))
        {
            yield break;
        }
        var c = new Checks(DialogsName);
        var rig = new Rig(player, DialogsName);
        GameObject cover = null;
        try
        {
            var stone = rig.Give("Stone", 5);
            var wood = rig.Give("Wood", 5);
            if (!c.Check(stone != null && wood != null, "Stone and Wood given"))
            {
                c.Report();
                yield break;
            }
            FilterState.SetMode(FilterMode.SkipIgnored);
            yield return Open();
            yield return Frames(2);
            var grid = gui.m_playerGrid;

            // Control: the gesture marks when nothing is in the way.
            yield return MarkGesture(grid, stone);
            c.Check(FilterState.Ignored.Contains("Stone"), "control: middle-click marks Stone");
            FilterState.ClearList(FilterState.Ignored);
            Messages.Clear();
            FilterUi.TestForgetMarkMessageTime();

            gui.OnOpenSkills();
            yield return Frames(2);
            c.Check(gui.m_skillsDialog.gameObject.activeSelf, "Skills dialog open");
            yield return MarkGesture(grid, stone);
            c.Check(FilterState.Ignored.Count == 0 && OurMessages() == 0, $"Skills dialog open: middle-click marks nothing, no message (got: {AllMessages()})");
            gui.m_skillsDialog.gameObject.SetActive(false);

            gui.OnOpenTrophies();
            yield return Frames(2);
            c.Check(gui.m_trophiesPanel.activeSelf, "Trophies panel open");
            yield return MarkGesture(grid, stone);
            c.Check(FilterState.Ignored.Count == 0 && OurMessages() == 0, "Trophies panel open: middle-click marks nothing, no message");
            gui.m_trophiesPanel.SetActive(false);
            gui.SetActiveGroup(1, false);
            yield return Frames(2);

            // Dragging an item.
            gui.SetupDragItem(stone, rig.Inv, 1);
            yield return null;
            c.Check(gui.m_dragGo != null, "dragging Stone");
            yield return MarkGesture(grid, wood);
            c.Check(FilterState.Ignored.Count == 0 && OurMessages() == 0, "while dragging: middle-click on another slot marks nothing");
            gui.SetupDragItem(null, null, 1);
            yield return null;

            // Split dialog.
            gui.ShowSplitDialog(stone, rig.Inv);
            yield return null;
            c.Check(gui.m_splitDialog.IsActive, "split dialog open");
            yield return MarkGesture(grid, wood);
            c.Check(FilterState.Ignored.Count == 0 && OurMessages() == 0, "split dialog open: middle-click marks nothing");
            gui.HideSplitDialog();
            yield return null;

            // Something of another mod drawn over the slots (stand-in: a see-through panel over the whole screen).
            var canvas = gui.m_player.GetComponentInParent<Canvas>();
            if (c.Check(canvas != null, "inventory canvas found"))
            {
                cover = new GameObject("MC_LootFilterTestCover", typeof(RectTransform));
                var rt = (RectTransform)cover.transform;
                rt.SetParent(canvas.rootCanvas.transform, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.SetAsLastSibling();
                var image = cover.AddComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0.03f);
                image.raycastTarget = true;
                yield return Frames(2);
                var slot = ElementOf(grid, stone);
                var top = slot != null ? TopHit(WorldRect((RectTransform)slot.transform).center) : null;
                c.Check(top != null && slot != null && !top.transform.IsChildOf(slot.transform), $"something other than the slot is on top at the pointer ({(top != null ? top.name : "nothing")})");
                yield return MarkGesture(grid, stone);
                c.Check(FilterState.Ignored.Count == 0 && OurMessages() == 0, "slot covered by another panel: middle-click marks nothing");
                Object.Destroy(cover);
                cover = null;
                yield return Frames(2);
            }
            yield return MarkGesture(grid, stone);
            c.Check(FilterState.Ignored.Contains("Stone"), "everything closed again: middle-click marks");
            c.Check(rig.NewGuardReports == 0, "no error inside the mod");
            c.Report();
        }
        finally
        {
            if (cover != null)
            {
                Object.Destroy(cover);
            }
            var g = InventoryGui.instance;
            if (g != null)
            {
                g.SetupDragItem(null, null, 1);
                g.HideSplitDialog();
            }
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.toggle (T21, M06)

    private static bool PatchedByUs(MethodBase method)
    {
        var info = method != null ? Harmony.GetPatchInfo(method) : null;
        if (info == null)
        {
            return false;
        }
        return info.Prefixes.Any(p => p.owner == ModInfo.Guid) || info.Postfixes.Any(p => p.owner == ModInfo.Guid)
               || info.Finalizers.Any(p => p.owner == ModInfo.Guid);
    }

    // Marks of ours still alive under a grid (shown or not).
    private static int MarkObjects(InventoryGrid grid)
    {
        var n = 0;
        if (grid == null)
        {
            return 0;
        }
        foreach (var element in grid.m_elements)
        {
            if (element != null && element.transform.Find(MarkObject) != null)
            {
                n++;
            }
        }
        return n;
    }

    private static IEnumerator RunToggle()
    {
        if (!Ready(ToggleName, out var player, out var gui))
        {
            yield break;
        }
        var plugin = PluginInstance();
        if (plugin == null)
        {
            SelfTest.Fail(ToggleName, "plugin instance not found");
            yield break;
        }
        var c = new Checks(ToggleName);
        var rig = new Rig(player, ToggleName);
        // Status line change in memory only while the test turn the mod off and on: config file never written.
        var saveOnSet = plugin.Config.SaveOnConfigSet;
        plugin.Config.SaveOnConfigSet = false;
        try
        {
            var autoPickup = AccessTools.Method(typeof(Player), nameof(Player.AutoPickup), new[] { typeof(float) });
            var isPiece = AccessTools.Method(typeof(ItemDrop), nameof(ItemDrop.IsPiece));
            var stone = rig.Give("Stone", 2);
            FilterState.SetMode(FilterMode.SkipIgnored);
            FilterState.Toggle(FilterState.Ignored, "Stone");
            yield return Open();
            yield return Frames(3);
            c.Check(plugin.IsActive && FindButton() != null && BadgeOn(gui.m_playerGrid, stone, SpriteIgnored) && PatchedByUs(autoPickup),
                "before: mod active, button and mark shown, auto pickup hooked");
            yield return Close();

            // Off (framework path: OnDeactivated + patches removed).
            TestHooks.ForceOff = true;
            FeatureRegistry.RefreshAll();
            yield return Frames(3);
            c.Check(!plugin.IsActive, $"mod turned off (status: {plugin.StatusText})");
            c.Check(!PatchedByUs(autoPickup) && !PatchedByUs(isPiece), "off: no patch of the mod left on Player.AutoPickup / ItemDrop.IsPiece");
            var lines = Run(Console.instance, "lootfilter");
            c.Check(AnyLine(lines, "'lootfilter' is not a recognized command."), $"off: lootfilter is not a recognized command ({Lines(lines)})");
            // M06: the lists stay in the character while the mod is off, and the character still saves.
            CheckStored(c, player, "SkipIgnored", "Stone", "", "mod off");
            yield return Open();
            yield return Frames(3);
            c.Check(FindButton() == null, "off: no button in the inventory");
            c.Check(MarkObjects(gui.m_playerGrid) == 0, "off: no mark in the inventory");
            yield return Close();
            // Normal game: the ignored Stone is picked up, no grey line.
            var before = Count(rig.Inv, "Stone");
            var drop = rig.Drop("Stone", 5, rig.Spot(0.9f, 0.4f, 0.3f));
            c.Check(!HoverOf(drop).Contains(SkipLineAny), "off: no 'Auto pickup skips this' line on the item");
            var one = new List<ItemDrop> { drop };
            yield return WaitGone(one, 4f);
            c.Check(!Alive(drop) && Count(rig.Inv, "Stone") == before + 5, "off: ignored Stone is auto-picked again (normal game)");

            // On again.
            TestHooks.ForceOff = false;
            FeatureRegistry.RefreshAll();
            yield return Frames(3);
            c.Check(plugin.IsActive && PatchedByUs(autoPickup), $"mod turned on again (status: {plugin.StatusText})");
            c.Check(FilterState.EnsureLocal() && FilterState.Mode == FilterMode.SkipIgnored && SameSet(FilterState.Ignored, "Stone"), "on: same mode and lists");
            lines = Run(Console.instance, "lootfilter");
            c.Check(AnyLine(lines, ModInfo.Name + ": mode Skip ignored"), $"on: lootfilter answers again ({Lines(lines)})");
            yield return Open();
            yield return Frames(3);
            c.Check(FindButton() != null && LabelOf(FindButton()) == "Auto pickup: Skip ignored", "on: button back with the same mode");
            c.Check(BadgeOn(gui.m_playerGrid, stone, SpriteIgnored), "on: mark back");
            yield return Close();
            before = Count(rig.Inv, "Stone");
            drop = rig.Drop("Stone", 5, rig.Spot(0.9f, -0.4f, 0.3f));
            yield return new WaitForSeconds(2.5f);
            c.Check(Alive(drop) && Count(rig.Inv, "Stone") == before, "on: Stone is skipped again");
            c.Check(HoverOf(drop).Contains(SkipLineIgnored), "on: the grey line is back");

            // Same with the inventory open: button and marks go and come without closing it.
            yield return Open();
            yield return Frames(3);
            TestHooks.ForceOff = true;
            FeatureRegistry.RefreshAll();
            yield return Frames(3);
            c.Check(InventoryGui.IsVisible() && FindButton() == null && MarkObjects(gui.m_playerGrid) == 0, "inventory open, mod off: button and marks disappear, inventory stays open");
            TestHooks.ForceOff = false;
            FeatureRegistry.RefreshAll();
            yield return Frames(4);
            c.Check(InventoryGui.IsVisible() && FindButton() != null && BadgeOn(gui.m_playerGrid, stone, SpriteIgnored), "inventory open, mod on: button and marks appear without closing it");
            c.Check(rig.Logs.Count(LogLevel.Error | LogLevel.Fatal) == 0, $"no error from the mod ({rig.Logs.First(LogLevel.Error | LogLevel.Fatal)})");
            c.Report();
        }
        finally
        {
            rig.Restore();
            plugin.Config.SaveOnConfigSet = saveOnSet;
        }
    }

    // ---------------------------------------------------------------- lootfilter.compat-search (C01)

    private static IEnumerator RunCompatSearch()
    {
        if (!Ready(CompatSearchName, out var player, out var gui))
        {
            yield break;
        }
        if (!OtherModActive("MC.UX.Crafting.SearchSort"))
        {
            SelfTest.Fail(CompatSearchName, "Crafting Search and Sort (MC.UX.Crafting.SearchSort) is not active in this run: nothing checked");
            yield break;
        }
        var c = new Checks(CompatSearchName);
        var rig = new Rig(player, CompatSearchName);
        try
        {
            var stone = rig.Give("Stone", 3);
            FilterState.SetMode(FilterMode.SkipIgnored);
            yield return Open();
            yield return Frames(5);
            var es = EventSystem.current;
            var grid = gui.m_playerGrid;
            var button = FindButton();
            var row = GameObject.Find("MC_CraftSearchRow");
            var fieldGo = GameObject.Find("MC_CraftSearchField");
            var sortGo = GameObject.Find("MC_CraftSortButton");
            var field = fieldGo != null ? fieldGo.GetComponent<TMP_InputField>() : null;
            if (!c.Check(button != null && stone != null && row != null && field != null && sortGo != null, "our button and the crafting search row, field and sort button are on screen"))
            {
                c.Report();
                yield break;
            }
            var b = WorldRect((RectTransform)button.transform);
            c.Check(!b.Overlaps(WorldRect((RectTransform)row.transform)) && !b.Overlaps(WorldRect((RectTransform)fieldGo.transform))
                    && !b.Overlaps(WorldRect((RectTransform)sortGo.transform)), "Auto pickup button and the search row do not overlap");

            // Click our button, then type in the search field: Enter there never clicks the button again.
            Click(button);
            var mode = FilterState.Mode;
            es.SetSelectedGameObject(fieldGo);
            field.ActivateInputField();
            var until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until && !field.isFocused)
            {
                yield return null;
            }
            if (c.Check(field.isFocused, "search field has the cursor"))
            {
                ExecuteEvents.Execute(fieldGo, new BaseEventData(es), ExecuteEvents.submitHandler);
                c.Check(FilterState.Mode == mode, "Enter in the search field does not change the filter mode");
                var ignored = FilterState.Join(FilterState.Ignored);
                Messages.Clear();
                yield return MarkGesture(grid, stone);
                c.Check(FilterState.Join(FilterState.Ignored) == ignored && OurMessages() == 0, "cursor in the search field: middle-click on a slot marks nothing");
                // Keyboard mark key: the letter goes to the field only.
                TestHooks.MarkKey = new KeyboardShortcut(KeyCode.L);
                RaiseChanged(Plugin.MarkKey);
                es.SetSelectedGameObject(fieldGo);
                field.ActivateInputField();
                yield return Frames(3);
                if (field.isFocused)
                {
                    yield return MarkGesture(grid, stone);
                    c.Check(FilterState.Join(FilterState.Ignored) == ignored, "MarkKey L with the cursor in the search field: no mark");
                }
                else
                {
                    c.Check(false, "search field lost the cursor before the MarkKey L check");
                }
                TestHooks.MarkKey = new KeyboardShortcut(KeyCode.Mouse2);
                RaiseChanged(Plugin.MarkKey);
            }
            field.DeactivateInputField();
            es.SetSelectedGameObject(null);
            yield return Frames(3);

            // Sort menu over a slot (only when it really covers one).
            var sortButton = sortGo.GetComponent<Button>();
            if (sortButton != null)
            {
                Click(sortButton);
                yield return Frames(3);
                var menu = GameObject.Find("MC_CraftSortMenu");
                if (menu != null)
                {
                    var m = WorldRect((RectTransform)menu.transform);
                    var slot = ElementOf(grid, stone);
                    var s = WorldRect((RectTransform)slot.transform);
                    if (m.Overlaps(s))
                    {
                        var list = FilterState.Join(FilterState.Ignored);
                        var x = Mathf.Clamp(s.center.x, m.xMin + 1f, m.xMax - 1f);
                        var y = Mathf.Clamp(s.center.y, m.yMin + 1f, m.yMax - 1f);
                        TestHooks.Pointer = new Vector2(x, y);
                        TestHooks.PressMark();
                        yield return null;
                        yield return null;
                        TestHooks.Pointer = null;
                        c.Check(FilterState.Join(FilterState.Ignored) == list, "sort menu over a slot: middle-click on the menu marks nothing");
                    }
                    else
                    {
                        c.Note($"sort menu {F(m)} does not cover the test slot {F(s)}: that part does not apply here");
                    }
                    Click(sortButton);
                    yield return Frames(2);
                }
                else
                {
                    c.Note("sort menu did not open: that part was not checked");
                }
            }
            c.Check(rig.NewGuardReports == 0, "no error inside the mod");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.compat-repair (C03)

    private static IEnumerator RunCompatRepair()
    {
        if (!Ready(CompatRepairName, out var player, out var gui))
        {
            yield break;
        }
        if (!OtherModActive("MC.Crafting.Repair.OneClickAll"))
        {
            SelfTest.Fail(CompatRepairName, "One Click Repair All (MC.Crafting.Repair.OneClickAll) is not active in this run: nothing checked");
            yield break;
        }
        var c = new Checks(CompatRepairName);
        var rig = new Rig(player, CompatRepairName);
        try
        {
            var items = new[] { rig.Give("Club"), rig.Give("AxeStone"), rig.Give("Hammer") };
            if (!c.Check(items.All(i => i != null && i.m_shared.m_useDurability), "three tools that wear given (Club, AxeStone, Hammer)"))
            {
                c.Report();
                yield break;
            }
            foreach (var item in items)
            {
                item.m_durability = item.GetMaxDurability() * 0.4f;
            }
            // No workbench at the spawn: the vanilla build cheat flag lets the repair button work anywhere.
            player.m_noPlacementCost = true;
            yield return Open();
            yield return Frames(3);
            var button = FindButton();
            if (!c.Check(button != null, "button is there"))
            {
                c.Report();
                yield break;
            }
            Messages.Clear();
            gui.OnRepairPressed();
            Click(button);
            var mode = FilterState.Mode;
            c.Check(items.All(i => Mathf.Approximately(i.m_durability, i.GetMaxDurability())), "one repair click repaired all three tools");
            var centre = Messages.Where(m => m.Type == MessageHud.MessageType.Center).ToArray();
            c.Check(centre.Length >= 1 && centre.Length < items.Length, $"the repair talks in the centre, not once per item ({centre.Length} message(s) for {items.Length} tools: {AllMessages()})");
            c.Check(mode == FilterMode.SkipIgnored && TopLeft(MsgSkip(0)) == 1, "the mode click right after still cycles the mode with its top-left message");
            // Both end up on screen: centre text and top-left text.
            var hud = MessageHud.instance;
            var until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until && hud.m_messageText.text != MsgSkip(0))
            {
                yield return null;
            }
            c.Check(hud.m_messageText.text == MsgSkip(0) && centre.Length >= 1 && hud.m_messageCenterText.text == L(centre[centre.Length - 1].Text),
                $"HUD holds both: top-left '{hud.m_messageText.text}', centre '{hud.m_messageCenterText.text}'");
            SelfTest.Screenshot(CompatRepairName, "both-messages");
            yield return null;
            yield return null;
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.log (T23)

    // Session log so far: nothing bad from this mod. Own small test: it also see what the tester's other mods and the
    // earlier tests made this mod log.
    private static IEnumerator RunLog()
    {
        var c = new Checks(LogName);
        yield return null;
        var path = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
        string[] lines = null;
        string error = null;
        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                lines = reader.ReadToEnd().Split('\n');
            }
        }
        catch (Exception e)
        {
            error = e.Message;
        }
        if (!c.Check(lines != null, $"read {path} ({error})"))
        {
            c.Report();
            yield break;
        }
        var source = ":" + ModInfo.Name + "]";
        var bad = new List<string>();
        var layout = 0;
        var listsPanel = 0;
        var zinput = 0;
        var ready = 0;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.Contains(SelfTest.Prefix))
            {
                continue; // test result lines (a FAIL is logged as an error)
            }
            var fromUs = line.Contains(source);
            var isError = line.StartsWith("[Error", StringComparison.Ordinal) || line.StartsWith("[Fatal", StringComparison.Ordinal);
            if (isError && (fromUs || line.Contains(ModInfo.Guid) || line.Contains("AutoPickupFilterMod")))
            {
                bad.Add(line.Length > 160 ? line.Substring(0, 160) : line);
            }
            else if (!line.StartsWith("[", StringComparison.Ordinal) && line.Contains("MC.UX.AutoPickupFilterMod.") && !line.Contains("AutoPickupFilterMod.SelfTests"))
            {
                bad.Add("stack trace: " + (line.Length > 140 ? line.Substring(0, 140) : line).Trim());
            }
            if (line.Contains("Inventory layout not recognised"))
            {
                layout++;
            }
            if (line.Contains("lists panel cannot be shown"))
            {
                listsPanel++;
            }
            // Vanilla ZInput warning for a key it cannot map (asked every frame = a flood).
            if (line.Contains("lacks a proper counterpart in the"))
            {
                zinput++;
            }
            if (line.Contains(Log.ReadyMarker + " " + ModInfo.Guid + " "))
            {
                ready++;
            }
        }
        c.Check(bad.Count == 0, $"no error or exception of {ModInfo.Name} in the log so far ({bad.Count}: {string.Join(" || ", bad.Take(3).ToArray())})");
        c.Check(layout == 0, $"no 'Inventory layout not recognised' warning ({layout})");
        c.Check(listsPanel == 0, $"no 'lists panel cannot be shown' warning ({listsPanel})");
        c.Check(zinput <= 2, $"no repeated ZInput key warning ({zinput} 'lacks a proper counterpart' line(s) in the whole log)");
        c.Check(ready >= 1, $"the [MC:ready] line of the mod is there ({ready})");
        c.Note($"{lines.Length} log lines read");
        c.Report();
    }
}
#endif
