using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
#endif

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Debug build only (calls vanish in Release): self tests with other mods, and on a real dedicated server.
// Other MC mods are found softly (FeatureRegistry, object names, reflection on their public doors): no reference to
// them. A needed mod not loaded = FAIL (the item cannot be checked), never a silent pass.
//   compendium.x.tamecounts  = X01 / T14: Creature Kill and Tame Counts: same kill number, its section stays in Texts,
//                              a tame alone discovers a creature ("Tamed: 1", "Killed: 0").
//   compendium.x.craftsearch = X02: Crafting Search and Sort's field lets go of the keyboard when ours opens.
//   compendium.x.lootfilter  = X03: Loot Pickup Filter's button cannot be clicked through our window, we draw over its
//                              lists panel.
//   compendium.x.sortchest   = X04: chest open and chest grid focused: View/Select is not ours; our window covers its
//                              buttons.
//   compendium.x.content     = X08: items other MC mods add (as a content mod does) are listed and discoverable.
//   compendium.x.copy        = X11: a copy of the Valheim Compendium dialog (as another mod makes) gets no tabs, and
//                              neither window closes the other.
// Multiplayer (tools/Test-Multiplayer.ps1, scenario modded; this client-only mod is not on the dedicated server):
//   compendium.mp.server     = M01: joined to a dedicated server without the mod: active, window, a kill counts.
//   compendium.mp.met        = M02: a tamed creature another game controls is met once its name plate shows.
internal static class UiCrossSelfTests
{
#if DEBUG
    private const string TameCountsName = "compendium.x.tamecounts";
    private const string CraftSearchName = "compendium.x.craftsearch";
    private const string LootFilterName = "compendium.x.lootfilter";
    private const string SortChestName = "compendium.x.sortchest";
    private const string ContentName = "compendium.x.content";
    private const string CopyName = "compendium.x.copy";
    private const string MpServerName = "compendium.mp.server";
    private const string MpMetName = "compendium.mp.met";

    private const string StatsGuid = "MC.Exploration.Stats.PerCreature";
    private const string CraftSearchGuid = "MC.UX.Crafting.SearchSort";
    private const string LootFilterGuid = "MC.UX.AutoPickup.Filter";
    private const string SortChestGuid = "MC.UX.Container.Sort";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly KeyValuePair<string, Func<IEnumerator>>[] Tests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(TameCountsName, RunTameCounts),
        new KeyValuePair<string, Func<IEnumerator>>(CraftSearchName, RunCraftSearch),
        new KeyValuePair<string, Func<IEnumerator>>(LootFilterName, RunLootFilter),
        new KeyValuePair<string, Func<IEnumerator>>(SortChestName, RunSortChest),
        new KeyValuePair<string, Func<IEnumerator>>(ContentName, () => TestKit.RunData(ContentName, CheckContent)),
        new KeyValuePair<string, Func<IEnumerator>>(CopyName, RunCopy),
    };
#endif

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        foreach (var t in Tests)
        {
            SelfTest.Register(t.Key, t.Value);
        }
        SelfTest.RegisterMultiplayer(MpServerName, SelfTest.Modded, RunMpServer);
        SelfTest.RegisterMultiplayer(MpMetName, SelfTest.Modded, RunMpMet);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        foreach (var t in Tests)
        {
            SelfTest.Unregister(t.Key);
        }
        SelfTest.UnregisterMultiplayer(MpServerName);
        SelfTest.UnregisterMultiplayer(MpMetName);
#endif
    }

#if DEBUG
    private static string Join(IEnumerable<string> lines) => string.Join(" | ", lines);

    // Creature forgotten every way (kills, trophy, met record, tames of Creature Kill and Tame Counts). The run's
    // PlayerState / ProfileState put it all back.
    private static void ForgetCreature(Player player, Entry creature)
    {
        Game.instance.GetPlayerProfile().m_playerStats[0].m_enemyStats[0].Remove(creature.Key);
        player.m_customData.Remove(OwnRecords.SeenKey);
        player.m_customData.Remove(TamesReader.Key);
        OwnRecords.ClearCache();
        TamesReader.ClearCache();
        var trophy = creature.Creature.Trophy;
        if (trophy != null)
        {
            player.m_knownMaterial.Remove(trophy.Key);
            player.m_knownRecipes.Remove(trophy.Key);
            foreach (var prefab in trophy.Item.Prefabs)
            {
                player.m_trophies.Remove(prefab != null ? prefab.name : "");
            }
        }
    }

    // ---------------------------------------------------------------- compendium.x.tamecounts (X01, T14)

    private static IEnumerator RunTameCounts()
    {
        var run = new UiRun();
        yield return run.Begin(TameCountsName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        var player = run.Player;
        var cat = run.Cat;
        GameObject spawned = null;
        try
        {
            run.Force(sideButton: false);
            cat.CreaturesByPrefab.TryGetValue("Boar", out var boar);
            var counts = TestKit.FindType(StatsGuid, "MC.Exploration.StatsPerCreatureMod.CreatureCounts");
            var getKills = counts != null ? counts.GetMethod("GetKills", new[] { typeof(string) }) : null;
            var getTames = counts != null ? counts.GetMethod("GetTames", new[] { typeof(string) }) : null;
            if (!TestKit.ModActive(StatsGuid, out var why) || getKills == null || getTames == null || boar == null || EnemyHud.instance == null)
            {
                p.Add($"Creature Kill and Tame Counts is needed for this check: {(why.Length > 0 ? why : "active")}, its CreatureCounts door "
                      + $"{(getKills != null && getTames != null ? "found" : "not found")}, Boar entry {boar != null}");
                run.Report();
                yield break;
            }
            ForgetCreature(player, boar);
            var kills = Game.instance.GetPlayerProfile().m_playerStats[0].m_enemyStats[0];
            var boarName = Names.StripTags(boar.DisplayName);

            // Same kill number in both mods, and its section sits in the Texts tab (Player Statistics).
            kills[boar.Key] = 3f;
            var theirs = (int)getKills.Invoke(null, new object[] { boar.Key });
            var ours = Knowledge.Take(cat, false, honourUnlocks: false).Kills(boar);
            if (theirs != 3 || ours != 3)
            {
                p.Add($"Boar kills: the Encyclopedia says {ours}, Creature Kill and Tame Counts says {theirs} (profile: 3)");
            }
            yield return TestKit.ShowInventory(gui);
            var raven = TopTabs.RavenButton(gui);
            if (raven == null)
            {
                p.Add("the Valheim Compendium button (raven) was not found");
                run.Report();
                yield break;
            }
            raven.onClick.Invoke();
            yield return null;
            yield return null;
            var statsTopic = Localization.instance.Localize("$inventory_stats");
            var texts = gui.m_textsDialog.m_texts;
            var stats = texts != null ? texts.FirstOrDefault(t => t.m_topic == statsTopic) : null;
            var statsText = stats != null ? stats.m_text ?? "" : "";
            if (!TopTabs.VanillaShown(gui) || stats == null || statsText.IndexOf(">Creatures<", StringComparison.Ordinal) < 0
                || statsText.IndexOf(boarName + ": 3 killed", StringComparison.Ordinal) < 0)
            {
                p.Add($"Texts tab: Player Statistics entry {stats != null}, its Creatures section {statsText.IndexOf(">Creatures<", StringComparison.Ordinal) >= 0}, "
                      + $"\"{boarName}: 3 killed\" in it {statsText.IndexOf(boarName + ": 3 killed", StringComparison.Ordinal) >= 0}");
            }
            if (TopTabs.VanillaEncyclopedia != null)
            {
                TopTabs.VanillaEncyclopedia.onClick.Invoke();
            }
            yield return null;
            yield return TestKit.WaitPrepared();
            CompendiumWindow.OpenEntry(boar);
            yield return null;
            if (!CompendiumWindow.IsOpen || !CompendiumWindow.RenderedLines.Contains(string.Format(Inv, Labels.KilledFormat, 3)))
            {
                p.Add($"Encyclopedia: Boar details miss \"Killed: 3\" (lines: {Join(CompendiumWindow.RenderedLines.Take(4))})");
            }
            if (CompendiumWindow.Root != null && (CompendiumWindow.Root.GetComponentInChildren<TextsDialog>(true) != null
                                                  || CompendiumWindow.Root.GetComponentsInChildren<TMP_Text>(true).Any(t => (t.text ?? "").IndexOf(">Creatures<", StringComparison.Ordinal) >= 0)))
            {
                p.Add("the other mod's statistics section is inside the Encyclopedia window");
            }
            CompendiumWindow.Close(selectButton: false);
            gui.Hide();
            yield return new WaitForSecondsRealtime(0.5f);

            // A tame alone discovers: never met, never killed, no trophy.
            kills.Remove(boar.Key);
            ForgetCreature(player, boar);
            var metLines = LogWatch.MetLines(boar.Key);
            var pos = TestKit.PointNear(player, 4f, 3.5f);
            spawned = TestKit.Spawn("Boar", pos, Quaternion.identity);
            var tameable = spawned != null ? spawned.GetComponent<Tameable>() : null;
            var character = spawned != null ? spawned.GetComponent<Character>() : null;
            if (tameable == null || character == null)
            {
                p.Add("the Boar prefab could not be spawned or cannot be tamed");
            }
            else
            {
                EnemyHud.HudData data = null;
                for (var i = 0; i < 60 && data == null; i++)
                {
                    spawned.transform.position = pos;
                    yield return null;
                    EnemyHud.instance.m_huds.TryGetValue(character, out data);
                }
                tameable.Tame(); // what the console's "tame" runs for each creature nearby
                for (var i = 0; i < 5; i++)
                {
                    spawned.transform.position = pos;
                    yield return null;
                }
                var k = Knowledge.Take(cat, false, honourUnlocks: false);
                var met = OwnRecords.GetSeen(player).Contains(boar.Key);
                var theirTames = (int)getTames.Invoke(null, new object[] { boar.Key });
                var lines = TestKit.Texts(DetailBuilder.Build(boar, k));
                if (!character.IsTamed() || theirTames != 1)
                {
                    p.Add($"test setup: Boar tamed {character.IsTamed()}, Creature Kill and Tame Counts counted {theirTames} tame(s)");
                }
                if (met || LogWatch.MetLines(boar.Key) != metLines)
                {
                    p.Add($"the tamed Boar was recorded as met although its name plate never showed (met {met})");
                }
                if (!k.IsKnown(boar) || k.Tames(boar) != 1 || k.Kills(boar) != 0
                    || !lines.Contains(string.Format(Inv, Labels.TamedFormat, 1)) || !lines.Contains(string.Format(Inv, Labels.KilledFormat, 0)))
                {
                    p.Add($"tame alone: Boar discovered {k.IsKnown(boar)}, tames {k.Tames(boar)}, kills {k.Kills(boar)}, lines [{Join(lines.Take(4))}] "
                          + "(expected discovered with \"Tamed: 1\" and \"Killed: 0\")");
                }
                // At the next open, in the window.
                yield return TestKit.ShowInventory(gui);
                CompendiumWindow.Open();
                yield return null;
                yield return TestKit.WaitPrepared();
                CompendiumWindow.OpenEntry(boar);
                yield return null;
                if (!ReferenceEquals(CompendiumWindow.SelectedEntry, boar) || !CompendiumWindow.RenderedLines.Contains(string.Format(Inv, Labels.TamedFormat, 1))
                    || !CompendiumWindow.RenderedLines.Contains(string.Format(Inv, Labels.KilledFormat, 0)))
                {
                    p.Add($"window after the tame: Boar selected {ReferenceEquals(CompendiumWindow.SelectedEntry, boar)}, lines [{Join(CompendiumWindow.RenderedLines.Take(4))}]");
                }
                if (data != null)
                {
                    data.m_hoverTimer = 99999f;
                }
            }
            run.Detail = "same Boar kill number as Creature Kill and Tame Counts (3) and as its line in Player Statistics; its section is in the "
                         + "Texts tab and not in the Encyclopedia; a tame alone (never met) discovers the Boar with \"Tamed: 1\" and \"Killed: 0\"";
        }
        finally
        {
            TestKit.Despawn(spawned);
            run.End();
        }
        yield return null;
        yield return null;
        run.State?.Restore();
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.x.craftsearch (X02)

    private static IEnumerator RunCraftSearch()
    {
        var run = new UiRun();
        yield return run.Begin(CraftSearchName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        try
        {
            run.Force(sideButton: false);
            if (!TestKit.ModActive(CraftSearchGuid, out var why))
            {
                p.Add($"Crafting Search and Sort is needed for this check: {why}");
                run.Report();
                yield break;
            }
            yield return TestKit.ShowInventory(gui);
            var fieldT = TestKit.FindUnder(gui, "MC_CraftSearchField");
            var field = fieldT != null ? fieldT.GetComponent<TMP_InputField>() : null;
            var es = EventSystem.current;
            if (field == null || !field.gameObject.activeInHierarchy || es == null)
            {
                p.Add("Crafting Search and Sort's search field was not found in the crafting panel");
                run.Report();
                yield break;
            }
            var results = new List<string>();
            foreach (var way in new[] { "side button way", "Encyclopedia tab" })
            {
                // Its field takes the keyboard, then the Encyclopedia opens.
                es.SetSelectedGameObject(field.gameObject);
                field.ActivateInputField();
                for (var i = 0; i < 4; i++)
                {
                    yield return null;
                }
                var selected = es.currentSelectedGameObject == field.gameObject;
                var focused = field.isFocused;
                if (way == "side button way")
                {
                    CompendiumWindow.Open();
                }
                else
                {
                    yield return TestKit.OpenByTab(gui, p, way);
                }
                for (var i = 0; i < 4; i++)
                {
                    yield return null;
                }
                results.Add($"{way}: its field selected {selected} / typing {focused} before, selected {es.currentSelectedGameObject == field.gameObject} / "
                            + $"typing {field.isFocused} after");
                if (!selected)
                {
                    p.Add($"{way}: test setup, its field could not be selected");
                }
                if (!CompendiumWindow.IsOpen || es.currentSelectedGameObject == field.gameObject || field.isFocused)
                {
                    p.Add($"{way}: Encyclopedia open {CompendiumWindow.IsOpen}, its field still selected {es.currentSelectedGameObject == field.gameObject}, "
                          + $"still typing {field.isFocused}");
                }
                CompendiumWindow.Close(selectButton: false);
                if (TopTabs.VanillaShown(gui))
                {
                    gui.m_textsDialog.OnClose();
                }
                yield return null;
                yield return null;
            }
            run.Note(string.Join("; ", results));
            run.Detail = "Crafting Search and Sort's field lets go of the keyboard when the Encyclopedia opens (from its tab and the side button way)";
        }
        finally
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.x.lootfilter (X03)

    private static IEnumerator RunLootFilter()
    {
        var run = new UiRun();
        yield return run.Begin(LootFilterName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        MethodInfo hideLists = null;
        try
        {
            run.Force(sideButton: false);
            if (!TestKit.ModActive(LootFilterGuid, out var why))
            {
                p.Add($"Loot Pickup Filter is needed for this check: {why}");
                run.Report();
                yield break;
            }
            yield return TestKit.ShowInventory(gui);
            yield return null;
            var buttonT = TestKit.FindUnder(gui, "MC_LootFilterButton") as RectTransform;
            var raven = TopTabs.RavenButton(gui);
            if (buttonT == null || !buttonT.gameObject.activeInHierarchy || raven == null)
            {
                p.Add("Loot Pickup Filter's Auto pickup button was not found on the inventory screen");
                run.Report();
                yield break;
            }
            var label = buttonT.GetComponentInChildren<TMP_Text>();
            var labelBefore = label != null ? label.text : "";

            // Its lists panel (controller only, RT + R3): shown through its own toggle.
            var ui = TestKit.FindType(LootFilterGuid, "MC.UX.AutoPickupFilterMod.FilterUi");
            var toggle = ui != null ? ui.GetMethod("ToggleListsPanel", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) : null;
            hideLists = ui != null ? ui.GetMethod("HideListsPanel", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) : null;
            if (toggle != null)
            {
                toggle.Invoke(null, new object[] { gui });
                yield return null;
                yield return null;
            }
            var panel = GameObject.Find("MC_LootFilterLists");

            yield return TestKit.OpenByTab(gui, p, "open");
            if (!CompendiumWindow.IsOpen)
            {
                run.Report();
                yield break;
            }
            // Its button under our window: a click there only closes the Encyclopedia, its mode does not change.
            var center = TestKit.Center(buttonT);
            var top = TestKit.TopAt(center);
            if (!TestKit.Under(top, CompendiumWindow.Root))
            {
                p.Add($"a click on the Auto pickup button would land on '{(top != null ? top.name : "nothing")}', not on the Encyclopedia");
            }
            // Its lists panel under our window.
            var panelNote = "its lists panel could not be shown (or hid itself when the Encyclopedia opened): draw order not checked";
            // The panel's largest part (its background) is where it is seen.
            RectTransform panelRt = null;
            if (panel != null && panel.activeInHierarchy)
            {
                var largest = 0f;
                foreach (var rt in panel.GetComponentsInChildren<RectTransform>(false))
                {
                    var r = UiUtil.WorldRect(rt);
                    if (r.width * r.height > largest)
                    {
                        largest = r.width * r.height;
                        panelRt = rt;
                    }
                }
            }
            if (panelRt != null)
            {
                var panelTop = TestKit.TopAt(TestKit.Center(panelRt));
                var over = TestKit.Under(panelTop, CompendiumWindow.Root);
                panelNote = $"lists panel shown, the point at its centre belongs to '{(panelTop != null ? panelTop.name : "nothing")}'";
                if (!over)
                {
                    p.Add($"with its lists panel open, the Encyclopedia does not draw over it ({panelNote})");
                }
                var panelCanvas = panel.GetComponentInParent<Canvas>();
                if (panelCanvas != null && CompendiumWindow.WindowCanvas != null && panelCanvas.overrideSorting
                    && panelCanvas.sortingOrder >= CompendiumWindow.WindowCanvas.sortingOrder)
                {
                    p.Add($"its lists panel sorts at {panelCanvas.sortingOrder}, the Encyclopedia at {CompendiumWindow.WindowCanvas.sortingOrder}");
                }
            }
            var hit = TestKit.Click(center, PointerEventData.InputButton.Left);
            yield return null;
            yield return null;
            var labelAfter = label != null ? label.text : "";
            if (labelAfter != labelBefore || hit.IndexOf(CompendiumWindow.WindowName, StringComparison.Ordinal) < 0)
            {
                p.Add($"click on the Auto pickup button through the window (landed on '{hit}'): its label '{labelBefore}' -> '{labelAfter}'");
            }
            run.Note($"Auto pickup button label '{labelBefore}'; {panelNote}");
            run.Detail = "the Auto pickup button cannot be clicked through the Encyclopedia (a click there lands on the window, the mode stays); "
                         + panelNote;
        }
        finally
        {
            try
            {
                var panel = GameObject.Find("MC_LootFilterLists");
                if (panel != null && panel.activeInHierarchy && hideLists != null)
                {
                    hideLists.Invoke(null, null);
                }
            }
            catch (Exception e)
            {
                SelfTest.Note(LootFilterName, $"could not hide the lists panel again: {e.GetType().Name}");
            }
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.x.sortchest (X04)

    private static IEnumerator RunSortChest()
    {
        var run = new UiRun();
        yield return run.Begin(SortChestName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        GameObject chest = null;
        try
        {
            run.Force(sideButton: true);
            if (!TestKit.ModActive(SortChestGuid, out var why))
            {
                p.Add($"Sort Chest is needed for this check: {why}");
                run.Report();
                yield break;
            }
            // A chest: the first piece of the Hammer's build list that is a container.
            var hammer = ObjectDB.instance.GetItemPrefab("Hammer");
            var table = hammer != null ? hammer.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces : null;
            var chestPrefab = table != null ? table.m_pieces.FirstOrDefault(g => g != null && g.GetComponent<Container>() != null) : null;
            if (chestPrefab == null)
            {
                p.Add("test setup: no chest piece found in the Hammer's build list");
                run.Report();
                yield break;
            }
            chest = Object.Instantiate(chestPrefab, TestKit.PointNear(run.Player, 2.2f, 0f), Quaternion.identity);
            var container = chest.GetComponent<Container>();
            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }
            gui.Show(container);
            yield return new WaitForSecondsRealtime(1.2f);
            var groups = gui.m_uiGroups;
            if (!gui.IsContainerOpen() || groups == null || groups.Length <= SideButton.SidePanelGroup || !SideButton.Exists)
            {
                p.Add($"test setup: chest '{chestPrefab.name}' open {gui.IsContainerOpen()}, Encyclopedia side button {SideButton.Exists}");
                run.Report();
                yield break;
            }
            // Chest grid focused: View/Select is not ours.
            gui.SetActiveGroup(groups[0], playSound: false);
            for (var i = 0; i < 3; i++)
            {
                yield return null;
            }
            if (gui.ActiveGroup != 0 || SideButton.PadAllowed() || CompendiumWindow.IsOpen)
            {
                p.Add($"chest grid focused (group {gui.ActiveGroup}): View/Select allowed for the Encyclopedia {SideButton.PadAllowed()}, open {CompendiumWindow.IsOpen}");
            }
            // Encyclopedia open: Sort Chest's buttons are covered and its shortcuts dead.
            var sort = TestKit.FindUnder(gui, "MC_ContainerSort_Sort") as RectTransform;
            if (sort == null || !sort.gameObject.activeInHierarchy)
            {
                p.Add("Sort Chest's Sort button was not found on the open chest");
            }
            else
            {
                CompendiumWindow.Open();
                for (var i = 0; i < 4; i++)
                {
                    yield return null;
                }
                yield return TestKit.WaitPrepared();
                var top = TestKit.TopAt(TestKit.Center(sort));
                var livePads = sort.GetComponentsInChildren<UIGamePad>(true).Count(pad => pad.isActiveAndEnabled && pad.IsInteractive());
                if (!CompendiumWindow.IsOpen || !TestKit.Under(top, CompendiumWindow.Root) || livePads > 0)
                {
                    p.Add($"Encyclopedia open over a chest: open {CompendiumWindow.IsOpen}, a click on Sort would land on '{(top != null ? top.name : "nothing")}', "
                          + $"{livePads} live controller shortcut(s) on its button");
                }
                var hit = TestKit.Click(TestKit.Center(sort), PointerEventData.InputButton.Left);
                yield return null;
                yield return null;
                if (hit.IndexOf(CompendiumWindow.WindowName, StringComparison.Ordinal) < 0 || !gui.IsContainerOpen())
                {
                    p.Add($"click on Sort through the window landed on '{hit}' (expected the Encyclopedia window), chest still open {gui.IsContainerOpen()}");
                }
            }
            run.Detail = $"chest '{chestPrefab.name}' open with its grid focused: View/Select is not the Encyclopedia's; with the Encyclopedia open "
                         + "the Sort button is covered and its controller shortcut dead";
        }
        finally
        {
            TestKit.CloseAll(gui);
            TestKit.Despawn(chest);
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.x.content (X08)

    // Items other MC mods add to the game's item list at world load, the way a content mod does (Spyglass, Music
    // Instruments, Sneak Ambush): each has an entry and is discovered by holding it.
    private static string CheckContent(Catalog cat, List<string> problems)
    {
        var player = Player.m_localPlayer;
        var added = ObjectDB.instance.m_items.Where(g => g != null && g.name.StartsWith("MC_", StringComparison.Ordinal) && g.GetComponent<ItemDrop>() != null).ToList();
        if (player == null || added.Count == 0)
        {
            problems.Add("no item added by another MC mod is in the game's item list (Spyglass, Music Instruments, Sneak Ambush not loaded?): nothing to check");
            return "";
        }
        var state = PlayerState.Capture(player);
        var profile = ProfileState.Capture();
        var names = new List<string>();
        try
        {
            foreach (var prefab in added)
            {
                var drop = prefab.GetComponent<ItemDrop>();
                var e = cat.ItemOf(prefab);
                if (e == null)
                {
                    problems.Add($"added item '{prefab.name}' has no entry");
                    continue;
                }
                if (Names.IsMissing(Names.Localize(e.NameToken)) || e.Icon == null || e.SubGroup == null)
                {
                    problems.Add($"added item '{prefab.name}': name '{e.DisplayName}', icon {e.Icon != null}, group {e.SubGroup != null}");
                }
                player.m_knownMaterial.Remove(e.Key);
                player.m_knownRecipes.Remove(e.Key);
                if (Knowledge.Take(cat, false, honourUnlocks: false).IsKnown(e))
                {
                    problems.Add($"setup: '{prefab.name}' still discovered after forgetting it");
                }
                player.AddKnownItem(drop.m_itemData);
                var k = Knowledge.Take(cat, false, honourUnlocks: false);
                var view = DetailBuilder.Build(e, k);
                var row = ListBuilder.BuildTab(cat, k, e.Tab, showUndiscovered: true).FirstOrDefault(r => r.Kind == ListRowKind.Entry && ReferenceEquals(r.Entry, e));
                if (!k.IsKnown(e) || view.Failed || view.FailedBlocks > 0 || view.Title != e.DisplayName || !row.Known || row.Text != e.DisplayName)
                {
                    problems.Add($"added item '{prefab.name}' held once: discovered {k.IsKnown(e)}, details '{view.Title}' (errors: {Join(view.Errors)}), row '{row.Text}'");
                }
                names.Add($"{prefab.name} ({e.Tab}{(e.HiddenUntilKnown ? ", hidden until known" : "")})");
            }
        }
        finally
        {
            state.Restore();
            profile.Restore();
            TestKit.ClearUnlockPopups();
            player.UpdateEvents();
        }
        return $"{added.Count} item(s) added by other MC mods are listed and discovered by holding one: {string.Join(", ", names)}";
    }

    // ---------------------------------------------------------------- compendium.x.copy (X11)

    private static IEnumerator RunCopy()
    {
        var run = new UiRun();
        yield return run.Begin(CopyName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        GameObject copy = null;
        try
        {
            run.Force(sideButton: false);
            yield return TestKit.ShowInventory(gui);
            var raven = TopTabs.RavenButton(gui);
            if (raven == null || gui.m_textsDialog == null)
            {
                p.Add("no Valheim Compendium button or dialog");
                run.Report();
                yield break;
            }
            if (TopTabs.VanillaShown(gui))
            {
                gui.m_textsDialog.OnClose();
                yield return null;
            }
            // What such a mod does: its own copy of the (closed) Valheim Compendium dialog.
            copy = Object.Instantiate(gui.m_textsDialog.gameObject, gui.m_textsDialog.transform.parent);
            copy.name = "OtherMod_TextsCopy";
            var dialog = copy.GetComponent<TextsDialog>();
            if (copy.GetComponentsInChildren<Transform>(true).Any(t => t.name == TopTabs.StripName))
            {
                p.Add("a copy of the closed Valheim Compendium carries the Texts / Encyclopedia tabs");
            }
            yield return TestKit.OpenByTab(gui, p, "open");
            if (CompendiumWindow.IsOpen && dialog != null)
            {
                // The copy is shown while the Encyclopedia is open.
                dialog.Setup(run.Player);
                yield return null;
                yield return null;
                if (!CompendiumWindow.IsOpen || !copy.activeSelf)
                {
                    p.Add($"the copy was shown: Encyclopedia open {CompendiumWindow.IsOpen}, copy shown {copy.activeSelf} (expected both)");
                }
                if (copy.GetComponentsInChildren<Transform>(true).Any(t => t.name == TopTabs.StripName)
                    || (TopTabs.VanillaStrip != null && TopTabs.VanillaStrip.transform.IsChildOf(copy.transform)))
                {
                    p.Add("the shown copy got the Texts / Encyclopedia tabs");
                }
                // Opening the Encyclopedia again does not close the copy.
                CompendiumWindow.Close(selectButton: false);
                yield return null;
                raven.onClick.Invoke();
                yield return null;
                yield return null;
                if (!CompendiumWindow.IsOpen || !copy.activeSelf)
                {
                    p.Add($"Encyclopedia reopened from the raven: open {CompendiumWindow.IsOpen}, copy still shown {copy.activeSelf} (expected both)");
                }
                // Closing the copy does not close the Encyclopedia.
                dialog.OnClose();
                yield return null;
                yield return null;
                if (!CompendiumWindow.IsOpen || copy.activeSelf)
                {
                    p.Add($"the copy was closed: Encyclopedia open {CompendiumWindow.IsOpen}, copy shown {copy.activeSelf}");
                }
                run.Detail = "a copy of the Valheim Compendium dialog gets no tabs, shown or not; opening the Encyclopedia leaves it shown and "
                             + "closing it leaves the Encyclopedia open";
            }
        }
        finally
        {
            if (copy != null)
            {
                copy.SetActive(false);
                Object.Destroy(copy);
            }
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- multiplayer

    // On a real dedicated server (which never has this client-only mod): the mod is active, the catalog is built from
    // the server's world, the window opens from the tab, and a kill the server credits to this player is counted.
    private static IEnumerator RunMpServer()
    {
        var run = new UiRun();
        yield return run.Begin(MpServerName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        var player = run.Player;
        var cat = run.Cat;
        var litter = TestKit.WorldLitter();
        var near = TestKit.PointNear(player, 4f, 3.5f);
        try
        {
            run.Force(sideButton: false);
            var bad = LogWatch.Bad;
            var znet = ZNet.instance;
            if (znet == null || znet.IsServer() || ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
            {
                p.Add($"not a client of a server (ZNet {znet != null}, server {znet != null && znet.IsServer()}, status {ZNet.GetConnectionStatus()})");
            }
            if (!Plugin.Instance.IsActive)
            {
                p.Add($"the Encyclopedia is not active on this server: {Plugin.Instance.StatusText}");
            }
            for (var t = 0; t < Tabs.Count; t++)
            {
                if (cat.CountOf((CatalogTab)t) == 0)
                {
                    p.Add($"tab {(CatalogTab)t} has no entry on this server's world");
                }
            }
            cat.CreaturesByPrefab.TryGetValue("Boar", out var boar);
            if (boar == null)
            {
                p.Add("no Boar entry");
                run.Report();
                yield break;
            }
            ForgetCreature(player, boar);

            // The window, from the game's own dialog.
            yield return TestKit.ShowInventory(gui);
            yield return TestKit.OpenByTab(gui, p, "open");
            var k = Knowledge.Take(cat, false);
            var counter = CompendiumWindow.CounterText != null ? CompendiumWindow.CounterText.text : "";
            if (!CompendiumWindow.IsOpen || CompendiumWindow.Rows.Count == 0 || counter != string.Format(Labels.DiscoveredFormat, k.DiscoveredCount, k.ListedCount))
            {
                p.Add($"window on the server: open {CompendiumWindow.IsOpen}, {CompendiumWindow.Rows.Count} rows, counter '{counter}'");
            }
            CompendiumWindow.Close(selectButton: false);
            gui.Hide();

            // The server's player list must know this character (kills are credited through it).
            var start = Time.realtimeSinceStartup;
            while (!znet.GetPlayerList().Any(i => i.m_characterID == znet.LocalPlayerCharacterID && !i.m_characterID.IsNone())
                   && Time.realtimeSinceStartup - start < 15f)
            {
                yield return null;
            }
            var died = new bool[1];
            yield return MoreSelfTests.KillOne(player, "Boar", near, Skills.SkillType.Swords, died, litter);
            for (var i = 0; i < 30 && Knowledge.Take(cat, false, honourUnlocks: false).Kills(boar) < 1; i++)
            {
                yield return null;
            }
            k = Knowledge.Take(cat, false, honourUnlocks: false);
            if (!died[0] || k.Kills(boar) != 1 || !k.IsKnown(boar))
            {
                p.Add($"Boar killed on the server: died {died[0]}, kills counted {k.Kills(boar)}, discovered {k.IsKnown(boar)} (player in the server's list: "
                      + $"{znet.GetPlayerList().Any(i => i.m_characterID == znet.LocalPlayerCharacterID)})");
            }
            yield return TestKit.ShowInventory(gui);
            CompendiumWindow.Open();
            yield return null;
            CompendiumWindow.OpenEntry(boar);
            yield return null;
            if (!CompendiumWindow.RenderedLines.Contains(string.Format(Inv, Labels.KilledFormat, 1)))
            {
                p.Add($"window after the kill: lines [{Join(CompendiumWindow.RenderedLines.Take(4))}] (expected \"Killed: 1\")");
            }
            if (LogWatch.Bad != bad)
            {
                p.Add($"{LogWatch.Bad - bad} warning or error line(s) from the mod during the test: {LogWatch.UnexpectedText()}");
            }
            run.Note($"client of a dedicated server; catalog {cat.ItemCount} items, {cat.PieceCount} pieces, {cat.CreatureCount} creatures; counter '{counter}'");
            run.Detail = "joined to a dedicated server without the mod: active, catalog built, window opens from the Encyclopedia tab, a Boar kill "
                         + "is counted (\"Killed: 1\"), no warning";
        }
        finally
        {
            TestKit.ClearLitter(litter, near, 12f);
            run.End();
        }
        yield return null;
        yield return null;
        run.State?.Restore();
        TestKit.ClearLitter(litter, near, 12f);
        run.Report();
    }

    // A tamed creature whose ZDO another game owns (here the server takes it for a moment: nobody else is connected)
    // is recorded as met once its name plate shows; near but with the plate hidden it is not.
    private static IEnumerator RunMpMet()
    {
        var run = new UiRun();
        yield return run.Begin(MpMetName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var player = run.Player;
        var cat = run.Cat;
        GameObject spawned = null;
        EnemyHud.HudData data = null;
        try
        {
            run.Force(sideButton: false);
            var hud = EnemyHud.instance;
            cat.CreaturesByPrefab.TryGetValue("Boar", out var boar);
            if (hud == null || boar == null || ZNet.instance == null)
            {
                p.Add("no EnemyHud, Boar entry or network");
                run.Report();
                yield break;
            }
            ForgetCreature(player, boar);
            var metLines = LogWatch.MetLines(boar.Key);
            var bad = LogWatch.Bad;
            var pos = TestKit.PointNear(player, 4f, 3.5f);
            spawned = TestKit.Spawn("Boar", pos, Quaternion.identity);
            var c = spawned != null ? spawned.GetComponent<Character>() : null;
            var nview = spawned != null ? spawned.GetComponent<ZNetView>() : null;
            if (c == null || nview == null)
            {
                p.Add("the Boar prefab could not be spawned");
                run.Report();
                yield break;
            }
            for (var i = 0; i < 60 && data == null; i++)
            {
                spawned.transform.position = pos;
                yield return null;
                hud.m_huds.TryGetValue(c, out data);
            }
            // Somebody's tame, controlled by another game.
            c.SetTamed(true);
            yield return null;
            var server = ZNet.instance.GetServerPeer();
            if (server != null && nview.IsValid())
            {
                nview.GetZDO().SetOwner(server.m_uid);
            }
            for (var i = 0; i < 3; i++)
            {
                spawned.transform.position = pos;
                yield return null;
            }
            var shown = data != null && data.m_gui != null && data.m_gui.activeSelf;
            if (data == null || shown || OwnRecords.GetSeen(player).Contains(boar.Key))
            {
                p.Add($"tamed Boar near, never aimed at: plate made {data != null}, shown {shown}, met {OwnRecords.GetSeen(player).Contains(boar.Key)}");
            }
            if (data != null)
            {
                data.m_hoverTimer = 0f; // what the crosshair on it does
                var owned = true;
                for (var i = 0; i < 4; i++)
                {
                    spawned.transform.position = pos;
                    yield return null;
                    owned &= nview.IsValid() && nview.IsOwner();
                }
                shown = data.m_gui != null && data.m_gui.activeSelf;
                var k = Knowledge.Take(cat, false, honourUnlocks: false);
                run.Note($"tamed {c.IsTamed()}, owned by this game while its plate showed: {owned} (false = another game owned it)");
                if (!shown || !OwnRecords.GetSeen(player).Contains(boar.Key) || !k.IsKnown(boar) || LogWatch.MetLines(boar.Key) != metLines + 1)
                {
                    p.Add($"tamed Boar of another game, plate shown {shown}: met {OwnRecords.GetSeen(player).Contains(boar.Key)}, discovered {k.IsKnown(boar)}, "
                          + $"\"Met\" lines {LogWatch.MetLines(boar.Key) - metLines}");
                }
            }
            if (LogWatch.Bad != bad)
            {
                p.Add($"{LogWatch.Bad - bad} warning or error line(s) from the mod during the test: {LogWatch.UnexpectedText()}");
            }
            run.Detail = "on a dedicated server a tamed Boar handed to another game is not met while its plate is hidden and is met (Debug \"Met\" "
                         + "line) once the plate shows";
        }
        finally
        {
            if (data != null)
            {
                data.m_hoverTimer = 99999f;
            }
            TestKit.Despawn(spawned);
            run.End();
        }
        yield return null;
        yield return null;
        run.State?.Restore();
        run.Report();
    }
#endif
}
