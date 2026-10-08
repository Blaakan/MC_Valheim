using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#endif

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Debug build only (calls vanish in Release): in-world UI self tests run by the world probe (tools/Test-InWorld.ps1,
// fresh character "MCProbe"), after the three data tests. Each ends with one PASS or FAIL line; NOTE lines give rects.
// Settings forced in memory only (Plugin.TestDisplay, Plugin.TestSideButton), never a ConfigEntry write. "Enabled off / on"
// below = the feature turned off and on live in memory (Plugin.SetOffForTest: same OnDeactivated / OnActivated path as
// the Enabled setting, the .cfg never touched).
//   compendium.ui     = opt-in side button off (default): no button, vanilla row. Raven button (onClick, as a click)
//                       opens the Valheim Compendium with the two top tabs (Texts current), on the title line, inside
//                       the frame, clear of title, close button, panes (screenshot). Its Encyclopedia tab opens our
//                       window in the same place and size, same tab rects (Encyclopedia current), clear of the category
//                       tabs, title, counter, search; Esc gate check passed; window modal (own focus group active, every
//                       live inventory group off, canvas sorted above, a click on the raven button and on a slot lands
//                       on the window); every category tab lists rows, undiscovered rows show no sprite (screenshot
//                       each); undiscovered / discovered item / creature details; our Texts tab back to the vanilla
//                       dialog (list filled fresh); what LT / RT run (TopTabs.Select, a trigger cannot be faked) both
//                       ways, twice in a row = no change; the remembered tab: raven reopens Encyclopedia, then Texts;
//                       Esc path (our Update prefix, CloseFromKey closes only the window and zeroes m_shownFrames; the
//                       key itself is T04); Hide closes everything.
//   compendium.toggle = Valheim Compendium open: Enabled off removes the top tabs at once, the dialog stays open exactly
//                       as the game made it (every object's place, size, scale, state and text); nothing named
//                       MC_Compendium_* left (screenshot); Enabled on puts them back at once; Encyclopedia opens a
//                       window that starts a fresh catalog build (off forgot it): closed during it, build finishes, the
//                       raven (remembered Encyclopedia) reopens it with rows at once (T25); Enabled off with our window
//                       open destroys it; on again: the raven opens a new window (memory survives the toggle).
//   compendium.siderow = opt-in off (default): no button, vanilla row noted. Opt-in on (in memory): our button in the
//                       row right after the raven, 6 controls evenly spaced inside the panel background (screenshot);
//                       opt-in off live: button gone, every control back exactly (position, scale, size,
//                       navigation); on again; Enabled off with it on: row back exactly, Enabled on: row again. Then the
//                       raven hidden (as a UI mod would): row layout dropped, controls on their baselines, D-pad links
//                       vanilla, ours Automatic; raven back: row and links again. "Another mod" moves a side control 3
//                       times: twice adopted and laid out again, the third time we give up. Clean-up; opt-in off again:
//                       vanilla row exactly.
// Everything changed (known material, kill count, the in-memory off switch, forced settings, remembered tab) is put back in finally;
// window, vanilla dialog and inventory closed.
internal static class UiSelfTests
{
#if DEBUG
    private const string UiName = "compendium.ui";
    private const string ToggleName = "compendium.toggle";
    private const string SideRowName = "compendium.siderow";
    private const float CatalogTimeout = 60f;
    private const float PrepareTimeout = 30f;
#endif

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(UiName, RunUi);
        SelfTest.Register(ToggleName, RunToggle);
        SelfTest.Register(SideRowName, RunSideRow);
        // One small test per TESTING.md item (Ui/UiSelfTests.More.cs), other mods and multiplayer (Ui/UiSelfTests.Cross.cs).
        UiMoreSelfTests.Register();
        UiCrossSelfTests.Register();
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(UiName);
        SelfTest.Unregister(ToggleName);
        SelfTest.Unregister(SideRowName);
        UiMoreSelfTests.Unregister();
        UiCrossSelfTests.Unregister();
#endif
    }

#if DEBUG
    // ---------------------------------------------------------------- compendium.ui

    private static IEnumerator RunUi()
    {
        var gui = InventoryGui.instance;
        var player = Player.m_localPlayer;
        var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
        if (gui == null || player == null || profile == null)
        {
            SelfTest.Fail(UiName, "no InventoryGui, local player or profile");
            yield break;
        }

        Catalog cat = null;
        var start = Time.realtimeSinceStartup;
        while (cat == null)
        {
            cat = CatalogService.EnsureReady();
            if (cat != null)
            {
                break;
            }
            if (Time.realtimeSinceStartup - start > CatalogTimeout)
            {
                SelfTest.Fail(UiName, $"catalog not ready after {CatalogTimeout} s");
                yield break;
            }
            yield return null;
        }

        var k0 = Knowledge.Take(cat, false);
        var item = PickItem(cat, k0);
        var creature = cat.FindCreature("$enemy_greyling") ?? cat.Entries.FirstOrDefault(e => e.Kind == EntryKind.Creature && !e.HiddenUntilKnown);
        if (item == null || creature == null)
        {
            SelfTest.Fail(UiName, $"no test targets (item {item}, creature {creature})");
            yield break;
        }
        SelfTest.Note(UiName, $"targets: discovered item {item}, killed creature {creature}");

        var problems = new List<string>();
        var kills = profile.m_playerStats[0].m_enemyStats[0];
        var addedMaterial = !player.m_knownMaterial.Contains(item.Key);
        var hadKill = kills.TryGetValue(creature.Key, out var oldKill);
        var lastBefore = TopTabs.Last;
        try
        {
            // The window's display settings and the opt-in side button forced in memory (the user's .cfg may have them
            // changed, T18): the checks below expect the defaults.
            Plugin.TestDisplay = new Plugin.DisplayOverride { ShowUndiscovered = true, RevealAll = false };
            Plugin.TestSideButton = false;
            SideButton.Sync(gui);
            TopTabs.SetLastForTest(TopTabs.Choice.Texts);
            if (addedMaterial)
            {
                player.m_knownMaterial.Add(item.Key);
            }
            kills[creature.Key] = (hadKill ? oldKill : 0f) + 1f;
            var expectedKills = Names.ToCount(kills[creature.Key]);

            if (!SideButton.InventoryShown(gui))
            {
                gui.Show(null);
            }
            yield return new WaitForSecondsRealtime(1.5f); // open animation
            if (EscGuard.Problem != null)
            {
                problems.Add($"Esc gate check failed at activation ({EscGuard.Problem})");
            }
            if (SideButton.Exists || HasObject(gui, SideButton.ButtonName))
            {
                problems.Add("an Encyclopedia side button exists while the opt-in setting is off");
            }
            var raven = TopTabs.RavenButton(gui);
            SelfTest.Screenshot(UiName, "side-panel");
            yield return null;
            yield return null;

            if (raven == null)
            {
                problems.Add("the Valheim Compendium button (raven) was not found");
            }
            else
            {
                // The Valheim Compendium, opened like a click on the raven.
                raven.onClick.Invoke();
                yield return null;
                yield return null;
                var vanillaTabs = CheckVanillaTabs(gui, problems, "raven");
                SelfTest.Screenshot(UiName, "texts-tabs");
                yield return null;
                yield return null;

                // Encyclopedia through its tab.
                var dialogRect = DialogWorldRect(gui);
                var frameRect = FrameWorldRect(gui.m_textsDialog.transform);
                if (TopTabs.VanillaEncyclopedia != null)
                {
                    TopTabs.VanillaEncyclopedia.onClick.Invoke();
                }
                yield return null;
                yield return null; // focus groups update in their own Update
                if (!CompendiumWindow.IsOpen || CompendiumWindow.Root == null)
                {
                    problems.Add("window not open after clicking the Encyclopedia tab");
                }
                else
                {
                    if (TopTabs.VanillaShown(gui))
                    {
                        problems.Add("the Valheim Compendium is still shown under the Encyclopedia");
                    }
                    if (TopTabs.Last != TopTabs.Choice.Encyclopedia)
                    {
                        problems.Add($"remembered top tab is {TopTabs.Last} after picking Encyclopedia");
                    }
                    var waitStart = Time.realtimeSinceStartup;
                    while (CompendiumWindow.Preparing && Time.realtimeSinceStartup - waitStart < PrepareTimeout)
                    {
                        yield return null;
                    }
                    if (CompendiumWindow.Preparing)
                    {
                        problems.Add($"window still \"Preparing entries...\" after {PrepareTimeout} s");
                    }
                    CheckWindowTabs(problems, vanillaTabs, dialogRect, frameRect);
                    CheckModal(gui, raven, problems);
                    SelfTest.Screenshot(UiName, "window");
                    yield return null;
                    yield return null;

                    var counts = new StringBuilder();
                    for (var t = 0; t < Tabs.Count; t++)
                    {
                        CompendiumWindow.SelectTab(t);
                        yield return null;
                        yield return null;
                        var rows = CompendiumWindow.Rows;
                        counts.Append(t == 0 ? "" : ", ").Append((CatalogTab)t).Append(' ').Append(rows.Count);
                        if (CompendiumWindow.CurrentTab != t)
                        {
                            problems.Add($"tab {(CatalogTab)t} did not open");
                        }
                        if (rows.Count == 0)
                        {
                            problems.Add($"tab {(CatalogTab)t} lists no row");
                        }
                        CheckRowsSpoilerSafe(problems, (CatalogTab)t);
                        SelfTest.Screenshot(UiName, $"tab{t}-{(CatalogTab)t}");
                        yield return null;
                        yield return null;
                    }
                    SelfTest.Note(UiName, "rows per tab (headers included): " + counts);

                    // Undiscovered entry: "???" title, "?" mark, no sprite.
                    CompendiumWindow.SelectTab((int)item.Tab);
                    yield return null;
                    if (!CompendiumWindow.SelectWhere(r => !r.Known))
                    {
                        problems.Add($"no undiscovered row in tab {item.Tab} to select");
                    }
                    else
                    {
                        yield return null;
                        var view = CompendiumWindow.ShownDetails;
                        if (view == null || view.Known || view.Title != Labels.Unknown)
                        {
                            problems.Add($"undiscovered entry details show '{(view != null ? view.Title : "nothing")}' (expected \"???\")");
                        }
                        if (CompendiumWindow.HeroShowsSprite || !CompendiumWindow.HeroShowsMark)
                        {
                            problems.Add("undiscovered entry details show a sprite or no \"?\" mark");
                        }
                        SelfTest.Screenshot(UiName, "undiscovered");
                        yield return null;
                        yield return null;
                    }

                    // Discovered item: its row selected, name and icon in the details.
                    CompendiumWindow.OpenEntry(item);
                    yield return null;
                    var itemView = CompendiumWindow.ShownDetails;
                    if (!ReferenceEquals(CompendiumWindow.SelectedEntry, item) || itemView == null || !itemView.Known
                        || itemView.Title != item.DisplayName)
                    {
                        problems.Add($"discovered item {item} not selected or its details wrong ('{(itemView != null ? itemView.Title : "nothing")}')");
                    }
                    else if (!CompendiumWindow.HeroShowsSprite)
                    {
                        problems.Add($"discovered item {item} details show no icon");
                    }
                    SelfTest.Screenshot(UiName, "discovered-item");
                    yield return null;
                    yield return null;

                    // Creature with a kill: the kill count line.
                    CompendiumWindow.OpenEntry(creature);
                    yield return null;
                    var killLine = string.Format(Labels.KilledFormat, expectedKills);
                    if (!ReferenceEquals(CompendiumWindow.SelectedEntry, creature))
                    {
                        problems.Add($"creature {creature} not selected after opening it");
                    }
                    else if (!CompendiumWindow.RenderedLines.Contains(killLine))
                    {
                        problems.Add($"creature {creature} details miss \"{killLine}\" (lines: {string.Join(" | ", CompendiumWindow.RenderedLines.Take(6))})");
                    }
                    // Trophy not discovered: the paw print (drawn in code), in the head and in its list row.
                    if (creature.Creature.Trophy == null || !CompendiumWindow.CurrentKnowledge.IsKnown(creature.Creature.Trophy))
                    {
                        var paw = PawIcon.Get();
                        if (!ReferenceEquals(CompendiumWindow.HeroSprite, paw))
                        {
                            problems.Add($"creature {creature} details head does not show the paw icon ({(CompendiumWindow.HeroSprite != null ? CompendiumWindow.HeroSprite.name : "no sprite")})");
                        }
                        var rowView = CompendiumWindow.ListPool.FirstOrDefault(r => r.Go.activeSelf && r.Bound >= 0
                            && r.Bound < CompendiumWindow.Rows.Count && ReferenceEquals(CompendiumWindow.Rows[r.Bound].Entry, creature));
                        if (rowView == null || !rowView.Icon.enabled || !ReferenceEquals(rowView.Icon.sprite, paw))
                        {
                            problems.Add($"creature {creature} list row does not show the paw icon");
                        }
                    }
                    SelfTest.Screenshot(UiName, "creature");
                    yield return null;
                    yield return null;
                    var tabBefore = CompendiumWindow.CurrentTab;
                    var selBefore = CompendiumWindow.SelectedEntry;

                    // Back to the game's texts through our Texts tab: the vanilla dialog, its list filled fresh.
                    if (CompendiumWindow.TopTextsButton != null)
                    {
                        CompendiumWindow.TopTextsButton.onClick.Invoke();
                    }
                    yield return null;
                    yield return null;
                    CheckTextsBack(gui, problems, "Texts tab");
                    CheckVanillaTabs(gui, problems, "Texts tab");
                    SelfTest.Screenshot(UiName, "texts-again");
                    yield return null;
                    yield return null;

                    // What LT / RT run (a trigger press cannot be faked): RT = Encyclopedia, twice = still one window;
                    // LT = Texts, twice = still the vanilla dialog. The window keeps its tab and entry (session memory).
                    TopTabs.Select(TopTabs.Choice.Encyclopedia);
                    TopTabs.Select(TopTabs.Choice.Encyclopedia);
                    yield return null;
                    yield return null;
                    if (!CompendiumWindow.IsOpen || TopTabs.VanillaShown(gui))
                    {
                        problems.Add($"RT (twice): window open {CompendiumWindow.IsOpen}, vanilla dialog shown {TopTabs.VanillaShown(gui)}");
                    }
                    else if (CompendiumWindow.CurrentTab != tabBefore || !ReferenceEquals(CompendiumWindow.SelectedEntry, selBefore))
                    {
                        problems.Add($"window came back on tab {CompendiumWindow.CurrentTab} / {CompendiumWindow.SelectedEntry} (was {tabBefore} / {selBefore})");
                    }
                    TopTabs.Select(TopTabs.Choice.Texts);
                    TopTabs.Select(TopTabs.Choice.Texts);
                    yield return null;
                    yield return null;
                    CheckTextsBack(gui, problems, "LT (twice)");

                    // Remembered tab: Encyclopedia picked, closed with Esc, the raven reopens it.
                    TopTabs.Select(TopTabs.Choice.Encyclopedia);
                    yield return null;
                    yield return null;
                    var escOk = EscPath(gui, problems);
                    yield return null;
                    yield return null;
                    raven.onClick.Invoke();
                    yield return null;
                    yield return null;
                    if (!CompendiumWindow.IsOpen || TopTabs.VanillaShown(gui))
                    {
                        problems.Add($"raven after Encyclopedia: window open {CompendiumWindow.IsOpen}, vanilla dialog shown {TopTabs.VanillaShown(gui)} "
                                     + "(expected the remembered Encyclopedia)");
                    }
                    // Texts picked, closed, the raven reopens Texts.
                    TopTabs.Select(TopTabs.Choice.Texts);
                    yield return null;
                    gui.m_textsDialog.OnClose();
                    yield return null;
                    yield return null;
                    raven.onClick.Invoke();
                    yield return null;
                    yield return null;
                    if (CompendiumWindow.IsOpen || !TopTabs.VanillaShown(gui) || TopTabs.VanillaStrip == null)
                    {
                        problems.Add($"raven after Texts: window open {CompendiumWindow.IsOpen}, vanilla dialog shown {TopTabs.VanillaShown(gui)}, "
                                     + $"tabs {TopTabs.VanillaStrip != null} (expected the remembered Texts)");
                    }
                    // Opened the side button way (CompendiumWindow.Open only), then RT in the window: memory stay Texts,
                    // the raven still reopen Texts.
                    gui.m_textsDialog.OnClose();
                    yield return null;
                    CompendiumWindow.Open();
                    yield return null;
                    TopTabs.Select(TopTabs.Choice.Encyclopedia);
                    yield return null;
                    var sideLast = TopTabs.Last;
                    var sideOpen = CompendiumWindow.IsOpen;
                    CompendiumWindow.Close(selectButton: false);
                    yield return null;
                    raven.onClick.Invoke();
                    yield return null;
                    yield return null;
                    if (!sideOpen || sideLast != TopTabs.Choice.Texts || CompendiumWindow.IsOpen || !TopTabs.VanillaShown(gui))
                    {
                        problems.Add($"window opened without the tabs, then RT: window open {sideOpen}, remembered {sideLast}; raven then: window "
                                     + $"open {CompendiumWindow.IsOpen}, vanilla dialog shown {TopTabs.VanillaShown(gui)} (expected Texts kept)");
                    }
                    SelfTest.Note(UiName, $"switching: tab pick and LT/RT both ways, remembered tab reopened by the raven (Encyclopedia, then Texts); "
                                          + $"window opened without the tabs + RT kept Texts; Esc path ok {escOk}");
                }
            }

            gui.Hide();
            yield return new WaitForSecondsRealtime(0.5f);
            if (CompendiumWindow.IsOpen || SideButton.InventoryShown(gui) || TopTabs.VanillaShown(gui))
            {
                problems.Add("window, vanilla dialog or inventory still open after InventoryGui.Hide");
            }
            if (TopTabs.VanillaStrip != null)
            {
                problems.Add("top tabs still on the Valheim Compendium after it closed");
            }
        }
        finally
        {
            Plugin.TestDisplay = null;
            Plugin.TestSideButton = null;
            TopTabs.SetLastForTest(lastBefore);
            if (addedMaterial)
            {
                player.m_knownMaterial.Remove(item.Key);
            }
            if (hadKill)
            {
                kills[creature.Key] = oldKill;
            }
            else
            {
                kills.Remove(creature.Key);
            }
            CompendiumWindow.Close(selectButton: false);
            if (TopTabs.VanillaShown(gui))
            {
                gui.m_textsDialog.OnClose();
            }
            if (SideButton.InventoryShown(gui))
            {
                gui.Hide();
            }
            SideButton.Sync(gui);
        }
        Report(UiName, problems, $"no side button by default; raven dialog with Texts / Encyclopedia tabs; Encyclopedia in its place, modal, "
                                 + $"{Tabs.Count} category tabs, undiscovered / discovered / creature details; Texts back (tab, LT/RT); "
                                 + "remembered tab; Esc path");
    }

    // Esc path. A key press cannot be faked, so: our Update prefix is on InventoryGui.Update, the IL gate it relies on
    // was found (EscGuard, checked above), and the code the prefix runs after its key gate closes only the window and
    // zeroes m_shownFrames (vanilla's "m_shownFrames > 1" then skips Hide this frame). The real key is T04.
    private static bool EscPath(InventoryGui gui, List<string> problems)
    {
        var prefixOn = Harmony.GetPatchInfo(AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.Update)))?
            .Prefixes.Any(p => p.owner == ModInfo.Guid) ?? false;
        if (!prefixOn)
        {
            problems.Add("no Encyclopedia prefix on InventoryGui.Update: Esc would close the whole inventory");
        }
        if (!CompendiumWindow.IsOpen)
        {
            problems.Add("Esc path: the window was not open");
            return false;
        }
        CompendiumWindow.CloseFromKey(gui);
        var shownFrames = gui.m_shownFrames;
        if (CompendiumWindow.IsOpen)
        {
            problems.Add("window still open after the Esc path");
        }
        if (shownFrames != 0)
        {
            problems.Add($"the Esc path left m_shownFrames at {shownFrames} (vanilla would close the inventory too)");
        }
        if (TopTabs.VanillaShown(gui))
        {
            problems.Add("the Esc path showed the Valheim Compendium (it must close only the window)");
        }
        SelfTest.Note(UiName, $"Esc path: Update prefix on {prefixOn}, m_shownFrames after it {shownFrames}, window closed "
                              + $"{!CompendiumWindow.IsOpen} (the key itself is not simulated: T04)");
        return prefixOn && !CompendiumWindow.IsOpen && shownFrames == 0;
    }

    private struct TabRects
    {
        internal bool Ok;
        internal Rect Texts;
        internal Rect Encyclopedia;
    }

    // Vanilla dialog shown with our strip: last child, Texts current, both inside the wood frame, clear of the title,
    // the visible close button, the pane boxes and each other. NOTE: the rects.
    private static TabRects CheckVanillaTabs(InventoryGui gui, List<string> problems, string when)
    {
        var result = default(TabRects);
        var dialog = gui.m_textsDialog;
        if (dialog == null || !dialog.gameObject.activeSelf)
        {
            problems.Add($"{when}: the Valheim Compendium is not shown");
            return result;
        }
        var strip = TopTabs.VanillaStrip;
        var texts = TopTabs.VanillaTexts;
        var ency = TopTabs.VanillaEncyclopedia;
        if (strip == null || texts == null || ency == null || !strip.activeInHierarchy)
        {
            problems.Add($"{when}: no Texts / Encyclopedia tabs on the Valheim Compendium");
            return result;
        }
        if (strip.transform.parent != dialog.transform || strip.transform.GetSiblingIndex() != dialog.transform.childCount - 1)
        {
            problems.Add($"{when}: the tabs strip is not the dialog's last child (clicks could land on its click-outside button)");
        }
        if (texts.interactable || !ency.interactable)
        {
            problems.Add($"{when}: on the Valheim Compendium Texts should be current (interactable {texts.interactable}) and Encyclopedia "
                         + $"clickable ({ency.interactable})");
        }
        CheckLabel(texts, Labels.TabTexts, problems, when);
        CheckLabel(ency, Labels.TabEncyclopedia, problems, when);
        result.Texts = UiUtil.WorldRect((RectTransform)texts.transform);
        result.Encyclopedia = UiUtil.WorldRect((RectTransform)ency.transform);
        result.Ok = true;
        var others = new List<(string, Rect)>();
        var closes = DialogParts.FindPersistent(dialog.gameObject, "OnClose");
        foreach (var c in closes)
        {
            var rt = (RectTransform)c.transform;
            if (!(rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one))
            {
                others.Add(("close button", UiUtil.WorldRect(rt)));
            }
        }
        var left = DialogParts.ListScroll(dialog.m_listRoot);
        var right = DialogParts.TextScroll(dialog.m_textArea);
        if (left != null && right != null)
        {
            var title = DialogParts.FindTitle(dialog.gameObject, dialog.m_textAreaTopic, dialog.m_textArea, left, right, closes);
            if (title != null)
            {
                others.Add(("title", TextWorldRect(title)));
            }
            DialogParts.PaneArea(dialog.transform, left, right, left.verticalScrollbar, right.verticalScrollbar, out var lp, out var rp);
            others.Add(("left pane", UiUtil.WorldRect(lp)));
            others.Add(("right pane", UiUtil.WorldRect(rp)));
        }
        var frame = FrameWorldRect(dialog.transform);
        CheckClear(problems, when, result, others, frame);
        SelfTest.Note(UiName, $"{when}: vanilla tabs Texts {UiUtil.Fmt(result.Texts)}, Encyclopedia {UiUtil.Fmt(result.Encyclopedia)}; "
                              + string.Join(", ", others.Select(o => $"{o.Item1} {UiUtil.Fmt(o.Item2)}")) + $"; frame {UiUtil.Fmt(frame)}");
        return result;
    }

    // Our window: same place and size as the vanilla dialog, its top tabs on the same rects (1 px), Encyclopedia
    // current, clear of the category tabs, title, counter, search, close button.
    private static void CheckWindowTabs(List<string> problems, TabRects vanilla, Rect dialogRect, Rect frameRect)
    {
        var root = CompendiumWindow.Root != null ? CompendiumWindow.Root.transform as RectTransform : null;
        var texts = CompendiumWindow.TopTextsButton;
        var ency = CompendiumWindow.TopEncyclopediaButton;
        if (root == null || texts == null || ency == null || !texts.gameObject.activeInHierarchy)
        {
            problems.Add("our window has no Texts / Encyclopedia tabs");
            return;
        }
        var winRect = UiUtil.WorldRect(root);
        var winFrame = FrameWorldRect(root);
        if (!Near(winRect, dialogRect) || !Near(winFrame, frameRect))
        {
            problems.Add($"our window is not in the vanilla dialog's place (window {UiUtil.Fmt(winRect)} frame {UiUtil.Fmt(winFrame)}, vanilla "
                         + $"{UiUtil.Fmt(dialogRect)} frame {UiUtil.Fmt(frameRect)})");
        }
        if (!texts.interactable || ency.interactable)
        {
            problems.Add($"in our window Encyclopedia should be current (interactable {ency.interactable}) and Texts clickable ({texts.interactable})");
        }
        CheckLabel(texts, Labels.TabTexts, problems, "window");
        CheckLabel(ency, Labels.TabEncyclopedia, problems, "window");
        var mine = new TabRects
        {
            Ok = true,
            Texts = UiUtil.WorldRect((RectTransform)texts.transform),
            Encyclopedia = UiUtil.WorldRect((RectTransform)ency.transform),
        };
        if (vanilla.Ok && (!Near(mine.Texts, vanilla.Texts) || !Near(mine.Encyclopedia, vanilla.Encyclopedia)))
        {
            problems.Add($"top tabs moved between the dialogs (window {UiUtil.Fmt(mine.Texts)} {UiUtil.Fmt(mine.Encyclopedia)}, vanilla "
                         + $"{UiUtil.Fmt(vanilla.Texts)} {UiUtil.Fmt(vanilla.Encyclopedia)})");
        }
        var others = new List<(string, Rect)>();
        foreach (var t in CompendiumWindow.TabButtonList)
        {
            if (t != null)
            {
                others.Add((t.name, UiUtil.WorldRect((RectTransform)t.transform)));
            }
        }
        if (CompendiumWindow.TitleText != null)
        {
            others.Add(("title", TextWorldRect(CompendiumWindow.TitleText)));
        }
        if (CompendiumWindow.CounterText != null && CompendiumWindow.CounterText.text.Length > 0)
        {
            others.Add(("counter", TextWorldRect(CompendiumWindow.CounterText)));
        }
        if (CompendiumWindow.Search != null)
        {
            others.Add(("search", UiUtil.WorldRect((RectTransform)CompendiumWindow.Search.transform)));
        }
        if (CompendiumWindow.CloseButton != null)
        {
            others.Add(("close button", UiUtil.WorldRect((RectTransform)CompendiumWindow.CloseButton.transform)));
        }
        CheckClear(problems, "window", mine, others, winFrame);
        SelfTest.Note(UiName, $"window: top tabs Texts {UiUtil.Fmt(mine.Texts)}, Encyclopedia {UiUtil.Fmt(mine.Encyclopedia)}; window "
                              + $"{UiUtil.Fmt(winRect)}, frame {UiUtil.Fmt(winFrame)}; "
                              + string.Join(", ", others.Where(o => !o.Item1.StartsWith("MC_Compendium_Tab", StringComparison.Ordinal) || o.Item1.EndsWith("0", StringComparison.Ordinal))
                                  .Select(o => $"{o.Item1} {UiUtil.Fmt(o.Item2)}")));
    }

    private static void CheckClear(List<string> problems, string when, TabRects tabs, List<(string, Rect)> others, Rect frame)
    {
        if (UiUtil.Overlaps(tabs.Texts, tabs.Encyclopedia, 0.5f))
        {
            problems.Add($"{when}: the Texts and Encyclopedia tabs overlap");
        }
        foreach (var (name, r) in new[] { ("Texts", tabs.Texts), ("Encyclopedia", tabs.Encyclopedia) })
        {
            if (frame.width > 1f && !UiUtil.Inside(r, frame, 1f))
            {
                problems.Add($"{when}: the {name} tab {UiUtil.Fmt(r)} leaves the frame {UiUtil.Fmt(frame)}");
            }
            foreach (var (otherName, o) in others)
            {
                if (UiUtil.Overlaps(r, o, 1f))
                {
                    problems.Add($"{when}: the {name} tab {UiUtil.Fmt(r)} overlaps the {otherName} {UiUtil.Fmt(o)}");
                }
            }
        }
    }

    private static void CheckLabel(Button b, string expected, List<string> problems, string when)
    {
        var label = b.GetComponentsInChildren<TMP_Text>(false).FirstOrDefault();
        if (label == null || label.text != expected)
        {
            problems.Add($"{when}: tab '{b.name}' reads '{(label != null ? label.text : "nothing")}' (expected \"{expected}\")");
        }
    }

    // After a switch to Texts: window closed, vanilla dialog shown with our tabs (Texts current), its list filled by
    // its own Setup (rows alive), Texts remembered.
    private static void CheckTextsBack(InventoryGui gui, List<string> problems, string how)
    {
        var dialog = gui.m_textsDialog;
        var texts = dialog != null ? dialog.m_texts : null;
        var rowsAlive = texts != null && texts.Count > 0 && texts.All(t => t.m_listElement != null && t.m_listElement.activeSelf);
        if (CompendiumWindow.IsOpen || !TopTabs.VanillaShown(gui) || !rowsAlive || TopTabs.Last != TopTabs.Choice.Texts
            || TopTabs.VanillaTexts == null || TopTabs.VanillaTexts.interactable)
        {
            problems.Add($"{how}: window open {CompendiumWindow.IsOpen}, vanilla dialog shown {TopTabs.VanillaShown(gui)}, its list "
                         + $"{(texts != null ? texts.Count : 0)} entries (rows alive {rowsAlive}), remembered {TopTabs.Last}, Texts current "
                         + $"{TopTabs.VanillaTexts != null && !TopTabs.VanillaTexts.interactable}");
        }
    }

    private static bool Near(Rect a, Rect b) =>
        Mathf.Abs(a.xMin - b.xMin) <= 1f && Mathf.Abs(a.yMin - b.yMin) <= 1f && Mathf.Abs(a.xMax - b.xMax) <= 1f && Mathf.Abs(a.yMax - b.yMax) <= 1f;

    private static Rect DialogWorldRect(InventoryGui gui) =>
        gui.m_textsDialog != null ? UiUtil.WorldRect((RectTransform)gui.m_textsDialog.transform) : default;

    // The wood panel image of the dialog (vanilla: Texts_frame/bkg, sprite woodpanel_texts). None: empty rect.
    private static Rect FrameWorldRect(Transform dialog)
    {
        foreach (var img in dialog.GetComponentsInChildren<Image>(true))
        {
            if (img != null && img.sprite != null && img.sprite.name.IndexOf("woodpanel", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return UiUtil.WorldRect(img.rectTransform);
            }
        }
        return default;
    }

    // Where a text can draw: its rect, grown to the rendered text bounds (a long localized title may overflow its rect).
    private static Rect TextWorldRect(TMP_Text t)
    {
        var r = UiUtil.WorldRect(t.rectTransform);
        t.ForceMeshUpdate();
        if (t.textInfo == null || t.textInfo.characterCount == 0)
        {
            return r;
        }
        var b = t.textBounds;
        var min = t.rectTransform.TransformPoint(b.min);
        var max = t.rectTransform.TransformPoint(b.max);
        var tr = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        return tr.width > 1f && tr.height > 1f ? UiUtil.Union(r, tr) : r;
    }

    private static bool HasObject(InventoryGui gui, string name) =>
        gui.GetComponentsInChildren<Transform>(true).Any(t => t.name == name);

    // Raspberries (a vanilla anchor) when listed and undiscovered, else the first undiscovered listed item.
    private static Entry PickItem(Catalog cat, Knowledge k)
    {
        var anchor = cat.FindItem("$item_raspberries");
        if (anchor != null && !anchor.HiddenUntilKnown && !k.IsKnown(anchor) && anchor.Icon != null)
        {
            return anchor;
        }
        return cat.Entries.FirstOrDefault(e => e.Kind == EntryKind.Item && !e.HiddenUntilKnown && !k.IsKnown(e) && e.Icon != null);
    }

    private static void CheckModal(InventoryGui gui, Button raven, List<string> problems)
    {
        var group = CompendiumWindow.Group;
        if (group == null || !group.IsActive)
        {
            problems.Add("window focus group is not active");
        }
        if (gui.m_uiGroups != null)
        {
            foreach (var g in gui.m_uiGroups)
            {
                // A group whose object is off (container grid with no chest open) never runs its Update: its IsActive
                // keeps a stale value, and it takes no input anyway.
                if (g == null || !g.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (g.IsActive)
                {
                    problems.Add($"inventory focus group '{g.name}' still active while the window is open");
                }
            }
        }
        var canvas = CompendiumWindow.WindowCanvas;
        var rootCanvas = CompendiumWindow.RootCanvas;
        if (canvas == null || !canvas.overrideSorting || (rootCanvas != null && canvas.sortingOrder <= rootCanvas.sortingOrder))
        {
            problems.Add("window canvas is not sorted above the inventory");
        }
        if (rootCanvas == null || rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            SelfTest.Note(UiName, "root canvas is not screen-space overlay: click checks skipped");
            return;
        }
        var blocker = CompendiumWindow.Blocker;
        if (blocker == null || !UiUtil.Inside(new Rect(0f, 0f, Screen.width, Screen.height), UiUtil.WorldRect(blocker), 2f))
        {
            problems.Add($"click blocker does not cover the screen ({(blocker != null ? UiUtil.Fmt(UiUtil.WorldRect(blocker)) : "none")})");
        }
        if (raven != null)
        {
            ExpectWindowHit("the Valheim Compendium button", UiUtil.WorldRect((RectTransform)raven.transform).center, problems);
        }
        var elements = gui.m_playerGrid != null ? gui.m_playerGrid.m_elements : null;
        if (elements != null && elements.Count > 0 && elements[0] != null)
        {
            ExpectWindowHit("an inventory slot", UiUtil.WorldRect((RectTransform)elements[0].transform).center, problems);
        }
    }

    private static void ExpectWindowHit(string what, Vector2 screen, List<string> problems)
    {
        var es = EventSystem.current;
        var root = CompendiumWindow.Root;
        if (es == null || root == null)
        {
            return;
        }
        var hits = new List<RaycastResult>();
        es.RaycastAll(new PointerEventData(es) { position = screen }, hits);
        var top = hits.Count > 0 ? hits[0].gameObject : null;
        SelfTest.Note(UiName, $"click on {what} at ({screen.x:F0},{screen.y:F0}) lands on '{(top != null ? UiUtil.Path(top.transform) : "nothing")}'");
        if (top == null || !top.transform.IsChildOf(root.transform))
        {
            problems.Add($"a click on {what} is not caught by the window (lands on '{(top != null ? top.name : "nothing")}')");
        }
    }

    // Rows bound to undiscovered entries: no sprite, "?" mark on. Bound headers: the text shown is the header's, and it
    // names no undiscovered tool or unvisited biome (same rule as compendium.details).
    private static void CheckRowsSpoilerSafe(List<string> problems, CatalogTab tab)
    {
        var rows = CompendiumWindow.Rows;
        var k = CompendiumWindow.CurrentKnowledge;
        foreach (var row in CompendiumWindow.ListPool)
        {
            if (row.Bound < 0 || row.Bound >= rows.Count || !row.Go.activeSelf)
            {
                continue;
            }
            if (rows[row.Bound].Kind == ListRowKind.Header)
            {
                var next = row.Bound + 1 < rows.Count && rows[row.Bound + 1].Kind == ListRowKind.Entry ? rows[row.Bound + 1].Entry : null;
                var leak = row.Name.text != rows[row.Bound].Text
                    ? $"header row shows '{row.Name.text}' instead of '{rows[row.Bound].Text}'"
                    : k != null ? SelfTests.HeaderLeak(row.Name.text, next != null ? next.SubGroup : null, k) : null;
                if (leak != null)
                {
                    problems.Add($"tab {tab}: {leak}");
                    return;
                }
                continue;
            }
            var r = rows[row.Bound];
            if (r.Known)
            {
                continue;
            }
            if ((row.Icon.enabled && row.Icon.sprite != null) || !row.Mark.gameObject.activeSelf || row.Name.text != Labels.Unknown)
            {
                problems.Add($"tab {tab}: undiscovered row {row.Bound} shows '{row.Name.text}', sprite {(row.Icon.sprite != null ? row.Icon.sprite.name : "none")}");
                return;
            }
        }
    }

    // ---------------------------------------------------------------- compendium.toggle

    private static IEnumerator RunToggle()
    {
        var plugin = Plugin.Instance;
        var gui = InventoryGui.instance;
        if (plugin == null || gui == null || Player.m_localPlayer == null || gui.m_textsDialog == null)
        {
            SelfTest.Fail(ToggleName, "no plugin, InventoryGui, Valheim Compendium dialog or local player");
            yield break;
        }
        var problems = new List<string>();
        var lastBefore = TopTabs.Last;
        try
        {
            Plugin.TestSideButton = false;
            SideButton.Sync(gui);
            TopTabs.SetLastForTest(TopTabs.Choice.Texts);
            if (!SideButton.InventoryShown(gui))
            {
                gui.Show(null);
            }
            yield return new WaitForSecondsRealtime(1f);
            var raven = TopTabs.RavenButton(gui);
            if (raven == null)
            {
                problems.Add("the Valheim Compendium button (raven) was not found");
            }
            else
            {
                // a. The Valheim Compendium with our tabs; the dialog as the game made it (everything but our strip).
                raven.onClick.Invoke();
                yield return new WaitForSecondsRealtime(0.5f); // text layout and list snap settle
                var dialog = gui.m_textsDialog;
                var oldStrip = TopTabs.VanillaStrip;
                if (oldStrip == null)
                {
                    problems.Add("no top tabs on the Valheim Compendium before toggling");
                }
                var before = Snapshot(dialog);

                // b. Enabled off with it open: tabs gone at once, the dialog still open and exactly as before.
                Plugin.SetOffForTest(true);
                yield return null;
                yield return null; // Object.Destroy happen at the end of the frame
                if (plugin.IsActive)
                {
                    problems.Add($"feature still active after Enabled = false ({plugin.StatusText})");
                }
                if (oldStrip != null || TopTabs.VanillaStrip != null)
                {
                    problems.Add("top tabs still on the Valheim Compendium after Enabled = false");
                }
                if (!TopTabs.VanillaShown(gui))
                {
                    problems.Add("the Valheim Compendium closed when the mod was turned off (it must stay as it was)");
                }
                CompareSnapshots(before, Snapshot(dialog), problems, "after Enabled = false");
                CheckNothingLeft(gui, problems, "after Enabled = false (vanilla dialog open)");
                SelfTest.Note(ToggleName, $"Valheim Compendium compared object by object ({before.Count} objects): "
                                          + $"{(problems.Count == 0 ? "exactly as the game made it" : "differences, see FAIL")}");
                SelfTest.Screenshot(ToggleName, "disabled");
                yield return null;
                yield return null;

                // c. Enabled on with it open: tabs back at once, the rest untouched.
                Plugin.SetOffForTest(false);
                yield return null;
                yield return null;
                if (!plugin.IsActive)
                {
                    problems.Add($"feature not active after Enabled = true ({plugin.StatusText})");
                }
                if (TopTabs.VanillaStrip == null || !TopTabs.VanillaStrip.activeInHierarchy || TopTabs.VanillaTexts == null
                    || TopTabs.VanillaTexts.interactable)
                {
                    problems.Add("top tabs not back at once on the open Valheim Compendium after Enabled = true");
                }
                CompareSnapshots(before, Snapshot(dialog), problems, "after Enabled = true");
                SelfTest.Screenshot(ToggleName, "reactivated-texts");
                yield return null;
                yield return null;

                // d. Encyclopedia: Enabled off forgot the catalog, so this open starts a fresh build (T25): closed
                // during it, the build still finishes, and the raven (Encyclopedia remembered) reopens it with rows.
                if (TopTabs.VanillaEncyclopedia != null)
                {
                    TopTabs.VanillaEncyclopedia.onClick.Invoke();
                }
                yield return null;
                var preparing = CompendiumWindow.Preparing;
                var building = CatalogService.IsBuilding;
                yield return null;
                var oldWindow = CompendiumWindow.Root;
                var oldWindowId = oldWindow != null ? oldWindow.GetInstanceID() : 0;
                if (!CompendiumWindow.IsOpen || oldWindow == null)
                {
                    problems.Add("window does not open from the Encyclopedia tab after Enabled = true");
                }
                else
                {
                    yield return CloseDuringBuild(gui, raven, preparing, building, problems);
                }

                // e. Enabled off with our window open: window destroyed, nothing left.
                oldWindow = CompendiumWindow.Root;
                if (oldWindow != null)
                {
                    oldWindowId = oldWindow.GetInstanceID();
                }
                Plugin.SetOffForTest(true);
                yield return null;
                yield return null;
                if (oldWindow != null)
                {
                    problems.Add("window object still exists after Enabled = false");
                }
                if (CompendiumWindow.Root != null || CompendiumWindow.IsOpen || SideButton.Exists)
                {
                    problems.Add("window or button still referenced after Enabled = false");
                }
                CheckNothingLeft(gui, problems, "after Enabled = false (window open)");
                if (gui.m_uiGroups != null && gui.m_uiGroups.Length > SideButton.SidePanelGroup && gui.ActiveGroup != SideButton.SidePanelGroup)
                {
                    SelfTest.Note(ToggleName, $"active inventory group after Enabled = false: {gui.ActiveGroup}");
                }

                // f. Enabled on: nothing shows by itself (no side button by default); the raven opens a NEW window at
                // once (Encyclopedia still remembered: session memory survives a live toggle).
                Plugin.SetOffForTest(false);
                yield return null;
                yield return null;
                if (SideButton.Exists)
                {
                    problems.Add("a side button appeared after Enabled = true with the opt-in setting off");
                }
                raven.onClick.Invoke();
                yield return null;
                yield return null;
                if (!CompendiumWindow.IsOpen || CompendiumWindow.Root == null)
                {
                    problems.Add($"the raven did not reopen the remembered Encyclopedia after Enabled = true (remembered {TopTabs.Last})");
                }
                else if (CompendiumWindow.Root.GetInstanceID() == oldWindowId)
                {
                    problems.Add("window after Enabled = true is the old object");
                }
                SelfTest.Screenshot(ToggleName, "reactivated");
                yield return null;
                yield return null;
            }
        }
        finally
        {
            if (Plugin.TestOff)
            {
                Plugin.SetOffForTest(false);
            }
            Plugin.TestSideButton = null;
            TopTabs.SetLastForTest(lastBefore);
            CompendiumWindow.Close(selectButton: false);
            if (TopTabs.VanillaShown(gui))
            {
                gui.m_textsDialog.OnClose();
            }
            if (SideButton.InventoryShown(gui))
            {
                gui.Hide();
            }
            SideButton.Sync(gui);
        }
        Report(ToggleName, problems, "Enabled off removed the top tabs from the open Valheim Compendium (left exactly as the game made it) and "
                                     + "destroyed an open window; Enabled on put the tabs back at once and the raven opened a new window; a window "
                                     + "closed during the catalog build found the list ready at the next open");
    }

    // Window closed during the catalog build (T25; by hand the build is over in about 0.1-0.2 s): the build go on
    // without the window, and the raven (Encyclopedia remembered) reopens it with rows at once. Window left open.
    private static IEnumerator CloseDuringBuild(InventoryGui gui, Button raven, bool preparing, bool building, List<string> problems)
    {
        CompendiumWindow.Close(selectButton: false);
        var start = Time.realtimeSinceStartup;
        var frames = 0;
        while (CatalogService.Ready == null && Time.realtimeSinceStartup - start < CatalogTimeout)
        {
            frames++;
            yield return null;
        }
        var ready = CatalogService.Ready != null;
        SelfTest.Note(ToggleName, $"window closed during the catalog build: preparing {preparing}, build running {building}; catalog "
                                  + $"ready {ready} {frames} frame(s) later with the window closed");
        if (!preparing || !building)
        {
            problems.Add($"first open after Enabled = true did not start a catalog build (preparing {preparing}, building {building})");
        }
        if (!ready)
        {
            problems.Add($"catalog build did not finish within {CatalogTimeout} s once the window was closed");
            yield break;
        }
        raven.onClick.Invoke();
        yield return null;
        yield return null;
        if (!CompendiumWindow.IsOpen || CompendiumWindow.Preparing || CompendiumWindow.Rows.Count == 0 || TopTabs.VanillaShown(gui))
        {
            problems.Add($"reopened by the raven after the interrupted build: open {CompendiumWindow.IsOpen}, preparing {CompendiumWindow.Preparing}, "
                         + $"{CompendiumWindow.Rows.Count} rows, vanilla dialog shown {TopTabs.VanillaShown(gui)}");
        }
    }

    private static void CheckNothingLeft(InventoryGui gui, List<string> problems, string when)
    {
        var leftovers = gui.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("MC_Compendium", StringComparison.Ordinal))
            .Select(t => t.name).Distinct().ToList();
        if (leftovers.Count > 0)
        {
            problems.Add($"objects left under the inventory {when}: " + string.Join(", ", leftovers));
        }
    }

    // Every object of the vanilla dialog except our strip, the list content (vanilla remakes the rows at each Setup and
    // snaps the list a few frames after it) and the scroll bars' insides (driven by the scroll views): path, on/off,
    // sibling index, anchors, pivot, anchored position, size, scale, rotation, text. Exact values ("R").
    private static List<string> Snapshot(TextsDialog dialog)
    {
        var list = new List<string>();
        var rows = dialog.m_listRoot;
        foreach (var t in dialog.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || Under(t, dialog.transform, TopTabs.StripName) || (rows != null && t.IsChildOf(rows)) || InsideScrollbar(t))
            {
                continue;
            }
            var sb = new StringBuilder(UiUtil.Path(t)).Append('|').Append(t.gameObject.activeSelf).Append('|').Append(t.GetSiblingIndex());
            if (t is RectTransform rt)
            {
                sb.Append('|').Append(V(rt.anchorMin)).Append(V(rt.anchorMax)).Append(V(rt.pivot)).Append(V(rt.anchoredPosition)).Append(V(rt.sizeDelta));
            }
            sb.Append('|').Append(t.localScale.x.ToString("R")).Append(',').Append(t.localScale.y.ToString("R"))
                .Append('|').Append(t.localRotation.eulerAngles.z.ToString("R"));
            var text = t.GetComponent<TMP_Text>();
            if (text != null)
            {
                sb.Append("|text=").Append(text.text);
            }
            list.Add(sb.ToString());
        }
        return list;
    }

    private static string V(Vector2 v) => "(" + v.x.ToString("R") + "," + v.y.ToString("R") + ")";

    private static bool InsideScrollbar(Transform t)
    {
        for (var x = t.parent; x != null; x = x.parent)
        {
            if (x.GetComponent<Scrollbar>() != null)
            {
                return true;
            }
        }
        return false;
    }

    private static bool Under(Transform t, Transform root, string name)
    {
        for (var x = t; x != null && x != root; x = x.parent)
        {
            if (x.name == name)
            {
                return true;
            }
        }
        return false;
    }

    private static void CompareSnapshots(List<string> before, List<string> after, List<string> problems, string when)
    {
        var missing = before.Except(after).ToList();
        var added = after.Except(before).ToList();
        if (missing.Count == 0 && added.Count == 0 && before.Count == after.Count)
        {
            return;
        }
        problems.Add($"Valheim Compendium not as before {when}: {missing.Count} object state(s) gone, {added.Count} new; first gone "
                     + $"'{missing.FirstOrDefault()}', first new '{added.FirstOrDefault()}'");
    }

    // ---------------------------------------------------------------- compendium.siderow

    private struct ControlState
    {
        internal RectTransform Rt;
        internal Selectable Sel;
        internal Vector2 Pos;
        internal Vector3 Scale;
        internal Vector2 Size;
        internal Navigation Nav;
    }

    // Active direct children of the side row holding a Selectable, ours excluded, with their exact state.
    private static List<ControlState> RowControls(Transform parent)
    {
        var list = new List<ControlState>();
        for (var i = 0; parent != null && i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child == null || child.name == SideButton.ButtonName || !child.gameObject.activeSelf)
            {
                continue;
            }
            var sel = child.GetComponentInChildren<Selectable>();
            if (sel == null)
            {
                continue;
            }
            list.Add(new ControlState
            {
                Rt = child,
                Sel = child.GetComponent<Selectable>(),
                Pos = child.anchoredPosition,
                Scale = child.localScale,
                Size = child.sizeDelta,
                Nav = child.GetComponent<Selectable>() != null ? child.GetComponent<Selectable>().navigation : default,
            });
        }
        return list;
    }

    // Every control exactly on its recorded state (position, scale, size: float for float; navigation).
    private static void CheckRowExact(List<ControlState> vanilla, List<string> problems, string when)
    {
        foreach (var c in vanilla)
        {
            if (c.Rt == null)
            {
                problems.Add($"{when}: a side control was destroyed");
                continue;
            }
            var pos = c.Rt.anchoredPosition;
            var scale = c.Rt.localScale;
            var size = c.Rt.sizeDelta;
            if (pos.x != c.Pos.x || pos.y != c.Pos.y || scale.x != c.Scale.x || scale.y != c.Scale.y || scale.z != c.Scale.z
                || size.x != c.Size.x || size.y != c.Size.y)
            {
                problems.Add($"{when}: side control '{c.Rt.name}' not exact: position {pos:F3} (vanilla {c.Pos:F3}), scale {scale:F3} "
                             + $"(vanilla {c.Scale:F3}), size {size:F3} (vanilla {c.Size:F3})");
            }
            if (c.Sel != null && !c.Sel.navigation.Equals(c.Nav))
            {
                problems.Add($"{when}: side control '{c.Rt.name}' navigation not the game's");
            }
        }
    }

    private static IEnumerator RunSideRow()
    {
        var plugin = Plugin.Instance;
        var gui = InventoryGui.instance;
        if (plugin == null || gui == null || Player.m_localPlayer == null)
        {
            SelfTest.Fail(SideRowName, "no plugin, InventoryGui or local player");
            yield break;
        }
        var problems = new List<string>();
        List<ControlState> vanilla = null;
        try
        {
            // Default (opt-in off): no button, the game's row as it is.
            Plugin.TestSideButton = false;
            SideButton.Sync(gui);
            if (!SideButton.InventoryShown(gui))
            {
                gui.Show(null);
            }
            yield return new WaitForSecondsRealtime(1f);
            var raven = TopTabs.RavenButton(gui);
            if (raven == null)
            {
                problems.Add("the Valheim Compendium button (raven) was not found");
            }
            else
            {
                vanilla = RowControls(raven.transform.parent);
                if (SideButton.Exists || HasObject(gui, SideButton.ButtonName))
                {
                    problems.Add("an Encyclopedia side button exists with the opt-in setting off");
                }
                if (SideRow.State != "not laid out" || SideRow.Baselines().Count > 0)
                {
                    problems.Add($"side row touched with the opt-in setting off ({SideRow.State})");
                }
                SelfTest.Note(SideRowName, "opt-in off (default): no button; the game's row: "
                                           + string.Join(", ", vanilla.Select(c => $"{c.Rt.name} {c.Pos:F1}")));
                SelfTest.Screenshot(SideRowName, "default-off");
                yield return null;
                yield return null;

                yield return OptInCycle(plugin, gui, vanilla, problems);
                yield return OutsideChanges(plugin, gui, problems);
                yield return RefusedWindow(gui, vanilla, problems);

                // Back to the default: the game's row exactly as noted at the start.
                Plugin.TestSideButton = false;
                SideButton.Sync(gui);
                yield return null;
                yield return null;
                if (SideButton.Exists || HasObject(gui, SideButton.ButtonName))
                {
                    problems.Add("button still there after the opt-in setting went back off");
                }
                CheckRowExact(vanilla, problems, "opt-in off at the end");
            }
        }
        finally
        {
            Plugin.TestSideButton = null;
            SideButton.Sync(gui);
            if (SideButton.InventoryShown(gui))
            {
                gui.Hide();
            }
        }
        Report(SideRowName, problems, "opt-in off left the game's row untouched; opt-in on laid the row out with our button, off (setting or "
                                      + "Enabled) put every control back exactly; a dropped row layout put the positions and the D-pad links back; "
                                      + "an outside move was adopted twice, the third made us give up; a refused window left no button");
    }

    // Window refused on this InventoryGui (UI mod), opt-in turned on after it, then off and on: no button, the game's row
    // exact (a button there would do nothing on click). Refusal gone (next InventoryGui in play): the button is back.
    private static IEnumerator RefusedWindow(InventoryGui gui, List<ControlState> vanilla, List<string> problems)
    {
        var wasRefused = CompendiumWindow.RefusedFor(gui);
        Plugin.TestSideButton = false;
        SideButton.Sync(gui);
        CompendiumWindow.SetRefusedForTest(gui, true);
        try
        {
            Plugin.TestSideButton = true;
            SideButton.Sync(gui);
            yield return null;
            yield return null;
            if (SideButton.Exists || HasObject(gui, SideButton.ButtonName))
            {
                problems.Add("refused window, opt-in turned on: an Encyclopedia side button was made");
            }
            CheckRowExact(vanilla, problems, "refused window, opt-in on");
            Plugin.TestSideButton = false;
            SideButton.Sync(gui);
            Plugin.TestSideButton = true;
            SideButton.Sync(gui);
            yield return null;
            yield return null;
            if (SideButton.Exists || HasObject(gui, SideButton.ButtonName))
            {
                problems.Add("refused window, opt-in off and on: an Encyclopedia side button was made");
            }
            CheckRowExact(vanilla, problems, "refused window, opt-in off and on");
            SelfTest.Screenshot(SideRowName, "refused-window");
            yield return null;
            yield return null;
        }
        finally
        {
            CompendiumWindow.SetRefusedForTest(gui, wasRefused);
        }
        Plugin.TestSideButton = false;
        SideButton.Sync(gui);
        Plugin.TestSideButton = true;
        SideButton.Sync(gui);
        yield return null;
        CheckOptInButton(gui, problems, "refusal gone, opt-in off and on");
        SelfTest.Note(SideRowName, $"refused window: no button with the opt-in turned on, nor after off and on; refusal gone: button back "
                                   + $"(row: {SideRow.State})");
    }

    // Opt-in on: button in the row; off live: row exactly vanilla; on again; Enabled off: exactly vanilla; Enabled on.
    private static IEnumerator OptInCycle(Plugin plugin, InventoryGui gui, List<ControlState> vanilla, List<string> problems)
    {
        Plugin.TestSideButton = true;
        SideButton.Sync(gui);
        yield return null;
        CheckOptInButton(gui, problems, "opt-in on");
        SelfTest.Screenshot(SideRowName, "opt-in");
        yield return null;
        yield return null;

        var oldButton = SideButton.Button != null ? SideButton.Button.gameObject : null;
        Plugin.TestSideButton = false;
        SideButton.Sync(gui);
        yield return null;
        yield return null;
        if (oldButton != null || SideButton.Exists || HasObject(gui, SideButton.ButtonName))
        {
            problems.Add("button still there after the opt-in setting was turned off");
        }
        CheckRowExact(vanilla, problems, "opt-in off again");

        Plugin.TestSideButton = true;
        SideButton.Sync(gui);
        yield return null;
        CheckOptInButton(gui, problems, "opt-in on again");

        Plugin.SetOffForTest(true);
        yield return null;
        yield return null;
        if (SideButton.Exists || HasObject(gui, SideButton.ButtonName))
        {
            problems.Add("button still there after Enabled = false (opt-in on)");
        }
        CheckRowExact(vanilla, problems, "Enabled = false with the opt-in on");
        Plugin.SetOffForTest(false);
        yield return null;
        CheckOptInButton(gui, problems, "Enabled = true with the opt-in on");
        SelfTest.Note(SideRowName, $"opt-in cycle done; row: {SideRow.State}");
    }

    private static void CheckOptInButton(InventoryGui gui, List<string> problems, string when)
    {
        var rt = SideButton.Rect;
        if (!SideButton.Exists || rt == null || !rt.gameObject.activeInHierarchy)
        {
            problems.Add($"{when}: no Encyclopedia side button");
            return;
        }
        if (SideButton.CurrentPlacement != SideButton.Placement.Row)
        {
            problems.Add($"{when}: side row not laid out with our button in it (placement {SideButton.CurrentPlacement}, row: {SideRow.State})");
        }
        var p = SideButton.FindProblem(gui);
        if (p != null)
        {
            problems.Add($"{when}: the Encyclopedia button {p}");
        }
        if (SideButton.Pad == null || SideButton.Pad.m_zinputKey != SideButton.PadKey)
        {
            problems.Add($"{when}: the Encyclopedia button has no View/Select pad");
        }
        var tip = SideButton.Tooltip;
        if (tip == null || tip.m_topic != Labels.ButtonTooltipTopic)
        {
            problems.Add($"{when}: the Encyclopedia button has no \"{Labels.ButtonTooltipTopic}\" tooltip");
        }
        CheckRow(rt, SideButton.AnchorButton, problems, when);
    }

    // Row laid out by us (vanilla side panel has no layout group): our button right after the anchor, every control
    // inside the panel background, no two controls overlapping, centres evenly spaced (1.5 px).
    private static void CheckRow(RectTransform rt, Button anchor, List<string> problems, string when)
    {
        var parent = rt.parent;
        var controls = new List<RectTransform>();
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child != null && child.gameObject.activeInHierarchy && child.GetComponentInChildren<Selectable>() != null)
            {
                controls.Add(child);
            }
        }
        controls.Sort((a, b) => UiUtil.WorldRect(a).center.x.CompareTo(UiUtil.WorldRect(b).center.x));
        var bg = SideButton.PanelBackground;
        var bgRect = bg != null ? UiUtil.WorldRect(bg) : default;
        var sb = new StringBuilder();
        sb.Append(when).Append(": row ").Append(SideRow.State).Append("; background '").Append(bg != null ? bg.name : "none").Append("' ")
            .Append(UiUtil.Fmt(bgRect)).Append("; order");
        var gaps = new List<float>();
        for (var i = 0; i < controls.Count; i++)
        {
            var r = UiUtil.WorldRect(controls[i]);
            sb.Append(i == 0 ? " " : ", ").Append(controls[i].name).Append(' ').Append(UiUtil.Fmt(r));
            if (bg != null && !UiUtil.Inside(r, bgRect, 1f))
            {
                problems.Add($"{when}: side control '{controls[i].name}' {UiUtil.Fmt(r)} is outside the panel background {UiUtil.Fmt(bgRect)}");
            }
            if (i > 0)
            {
                var prev = UiUtil.WorldRect(controls[i - 1]);
                gaps.Add(r.center.x - prev.center.x);
                if (UiUtil.Overlaps(prev, r, 0.5f))
                {
                    problems.Add($"{when}: side controls '{controls[i - 1].name}' and '{controls[i].name}' overlap");
                }
            }
        }
        SelfTest.Note(SideRowName, sb.ToString());
        var at = controls.IndexOf(rt);
        var anchorAt = anchor != null ? controls.IndexOf((RectTransform)anchor.transform) : -1;
        if (anchorAt < 0 || at != anchorAt + 1)
        {
            problems.Add($"{when}: our button is not right after the Valheim Compendium button (positions {at} and {anchorAt})");
        }
        if (gaps.Count > 0 && gaps.Max() - gaps.Min() > 1.5f)
        {
            problems.Add($"{when}: side controls not evenly spaced (gaps {string.Join(", ", gaps.Select(g => g.ToString("F1")))})");
        }
        if (controls.Count < 2)
        {
            problems.Add($"{when}: only {controls.Count} control(s) in the side row");
        }
    }

    // Opt-in on (set by OptInCycle). The raven hidden: row dropped; back: row again. Then "another mod" moves a vanilla
    // side control 3 times: adopted twice, the third time we give up. Clean-up: Enabled off (puts back what we still
    // own), the moved control on its vanilla place, Enabled on (fresh row, pad there although off and on happened in
    // the same frame).
    private static IEnumerator OutsideChanges(Plugin plugin, InventoryGui gui, List<string> problems)
    {
        RectTransform moved = null;
        var vanillaPos = Vector2.zero;
        GameObject anchorOff = null;
        // Third move below make the mod warn on purpose: the clean-log test must not count those lines. Two of them
        // (design 3.15): the give-up line, and the placement check's "outside the side panel background" line when
        // that check (a coroutine started at a Show, a few frames late) lands while our button sits after the last control.
        IDisposable expectedWarning = null;
        IDisposable expectedPlacement = null;
        try
        {
            var anchor = SideButton.AnchorButton;
            var baselines = SideRow.Baselines();
            var pick = baselines.FirstOrDefault(b => b.Rt != null && anchor != null && b.Rt != anchor.transform);
            if (SideButton.CurrentPlacement != SideButton.Placement.Row || pick.Rt == null)
            {
                problems.Add($"side row not laid out before the outside changes (placement {SideButton.CurrentPlacement}, row: {SideRow.State})");
                yield break;
            }
            // Row layout dropped (a UI mod hides the Valheim Compendium button): every control back on its baseline,
            // both D-pad links back to vanilla, ours Automatic; button back = row and links again.
            var navLinked = SideRow.NavLinked;
            var navPrev = SideRow.NavPrev;
            var navNext = SideRow.NavNext;
            var navPrevBase = SideRow.NavPrevBase;
            var navNextBase = SideRow.NavNextBase;
            anchorOff = anchor.gameObject;
            anchorOff.SetActive(false);
            SideButton.OnShow(gui);
            yield return null;
            var ours = SideButton.Button;
            SelfTest.Note(SideRowName, $"Valheim Compendium button hidden: placement {SideButton.CurrentPlacement}, row: {SideRow.State}; "
                                       + $"navigation linked before {navLinked}, now {SideRow.NavLinked}, ours "
                                       + $"{(ours != null ? ours.navigation.mode.ToString() : "missing")}");
            if (SideButton.CurrentPlacement == SideButton.Placement.Row)
            {
                problems.Add("row still laid out with the Valheim Compendium button hidden");
            }
            if (SideRow.NavLinked)
            {
                problems.Add("navigation still linked to our button after the row layout was dropped");
            }
            if (navLinked && navPrev != null && !navPrev.navigation.Equals(navPrevBase))
            {
                problems.Add($"'{navPrev.name}' navigation not restored after the row layout was dropped");
            }
            if (navLinked && navNext != null && !navNext.navigation.Equals(navNextBase))
            {
                problems.Add($"'{navNext.name}' navigation not restored after the row layout was dropped");
            }
            if (ours == null || ours.navigation.mode != Navigation.Mode.Automatic)
            {
                problems.Add("our button's navigation not back to Automatic after the row layout was dropped");
            }
            foreach (var b in baselines)
            {
                if (b.Rt != null && (b.Rt.anchoredPosition != b.Pos || b.Rt.localScale != b.Scale))
                {
                    problems.Add($"row dropped: '{b.Rt.name}' not back on its baseline ({b.Rt.anchoredPosition:F2}, was {b.Pos:F2})");
                }
            }
            anchorOff.SetActive(true);
            anchorOff = null;
            SideButton.OnShow(gui);
            yield return null;
            SelfTest.Note(SideRowName, $"Valheim Compendium button back: placement {SideButton.CurrentPlacement}, row: {SideRow.State}");
            if (SideButton.CurrentPlacement != SideButton.Placement.Row || SideRow.NavLinked != navLinked
                || !SideRow.State.Contains("external changes 0"))
            {
                problems.Add($"row or navigation not laid out again once the Valheim Compendium button is back (placement "
                             + $"{SideButton.CurrentPlacement}, navigation linked {SideRow.NavLinked}, row: {SideRow.State})");
            }

            moved = pick.Rt;
            vanillaPos = pick.Pos;
            SelfTest.Note(SideRowName, $"'{moved.name}' plays the control another mod moves (vanilla position {vanillaPos:F1})");
            expectedWarning = LogWatch.Expect(LogWatch.GaveUp);
            expectedPlacement = LogWatch.Expect(LogWatch.OutsidePanel);
            for (var i = 1; i <= 3; i++)
            {
                // "Another mod" moves it 3 units up, then the inventory shows again (our Show postfix).
                var before = moved.anchoredPosition;
                moved.anchoredPosition = before + new Vector2(0f, 3f);
                SideButton.OnShow(gui);
                yield return null;
                var now = moved.anchoredPosition;
                SelfTest.Note(SideRowName, $"move {i}: '{moved.name}' {before:F1} -> {now:F1}, placement {SideButton.CurrentPlacement}, row: {SideRow.State}");
                if (i < 3)
                {
                    if (SideButton.CurrentPlacement != SideButton.Placement.Row || SideRow.GaveUp)
                    {
                        problems.Add($"move {i}: row not laid out again (placement {SideButton.CurrentPlacement})");
                    }
                    if (!Mathf.Approximately(now.y, before.y + 3f))
                    {
                        problems.Add($"move {i}: the other mod's place was not kept (y {now.y:F2}, expected {before.y + 3f:F2})");
                    }
                    if (!SideRow.State.Contains($"external changes {i}"))
                    {
                        problems.Add($"move {i}: not counted as an outside change ({SideRow.State})");
                    }
                    var p = SideButton.FindProblem(gui);
                    if (p != null)
                    {
                        problems.Add($"move {i}: the Encyclopedia button {p}");
                    }
                }
                else
                {
                    if (!SideRow.GaveUp || SideButton.CurrentPlacement == SideButton.Placement.Row)
                    {
                        problems.Add($"move 3: we did not give up (placement {SideButton.CurrentPlacement}, row: {SideRow.State})");
                    }
                    if (now != before + new Vector2(0f, 3f))
                    {
                        problems.Add($"move 3: the moved control was touched after we gave up ({now:F2})");
                    }
                    foreach (var b in baselines)
                    {
                        if (b.Rt != null && b.Rt != moved && b.Rt.anchoredPosition != b.Pos)
                        {
                            problems.Add($"move 3: '{b.Rt.name}' not back on its baseline ({b.Rt.anchoredPosition:F2}, was {b.Pos:F2})");
                        }
                    }
                    SelfTest.Screenshot(SideRowName, "gave-up");
                    yield return null;
                    yield return null;
                }
            }
        }
        finally
        {
            // Feature off (puts back what we still own), the moved control on its vanilla place, feature on (fresh row).
            if (anchorOff != null)
            {
                anchorOff.SetActive(true);
            }
            Plugin.SetOffForTest(true);
            if (moved != null)
            {
                moved.anchoredPosition = vanillaPos;
            }
            // Feature off = our button gone, its late placement check end without a word: scopes close only now.
            expectedWarning?.Dispose();
            expectedPlacement?.Dispose();
            Plugin.SetOffForTest(false);
        }
        yield return null;
        if (SideButton.CurrentPlacement != SideButton.Placement.Row || SideRow.GaveUp)
        {
            problems.Add($"row not laid out again after the clean-up (placement {SideButton.CurrentPlacement}, row: {SideRow.State})");
        }
        // Off and on in the same frame: the old button still existed while the new one was made.
        if (SideButton.Pad == null)
        {
            problems.Add("no View/Select pad on the button after Enabled off and on in the same frame");
        }
    }

    private static void Report(string name, List<string> problems, string passDetail)
    {
        if (problems.Count == 0)
        {
            SelfTest.Pass(name, passDetail);
            return;
        }
        var shown = problems.Take(12).ToList();
        SelfTest.Fail(name, $"{problems.Count} problem(s): {string.Join(" | ", shown)}{(problems.Count > shown.Count ? " | ..." : "")}");
    }
#endif
}
