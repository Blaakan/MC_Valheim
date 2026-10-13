#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Debug build only (whole file; Release has nothing of me): what the mod do next to OTHER mods that are not installed
// here. Me play the thing those mods do to the game for real (never a flag of ours set by hand). Stand-ins: the real
// mod still need a look by a person, these tests take the mod's own part off that look.
//   compendium.x.textspage     = X06: a page another mod add to the Valheim Compendium's list (a patch on the game's
//                                list building, as Jewelcrafting-like mods do) stay on the Texts tab and work; the
//                                Encyclopedia, built from that very dialog, carry nothing of it.
//   compendium.x.refused-build = X07: an inventory screen the window cannot be built on (a part of the crafting panel
//                                gone, as a UI mod leave it): the real build is refused once, with one warning: no
//                                tab, no button, the game's side row, and no second warning whatever is tried after.
internal static class UiModsSelfTests
{
    private const string TextsPageName = "compendium.x.textspage";
    private const string RefusedBuildName = "compendium.x.refused-build";

    private const string PageTopic = "MC test page of another mod";
    private const string PageMarker = "MCOTHERMODPAGE";

    private static bool _pageOn;

    private static readonly KeyValuePair<string, Func<IEnumerator>>[] Tests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(TextsPageName, RunTextsPage),
        new KeyValuePair<string, Func<IEnumerator>>(RefusedBuildName, RunRefusedBuild),
    };

    // Called by UiSelfTests.Register / Unregister (inside their Debug-only part).
    internal static void Register()
    {
        foreach (var t in Tests)
        {
            SelfTest.Register(t.Key, t.Value);
        }
    }

    internal static void Unregister()
    {
        foreach (var t in Tests)
        {
            SelfTest.Unregister(t.Key);
        }
    }

    private static bool Need(List<string> problems, bool ok, string problem)
    {
        if (!ok)
        {
            problems.Add(problem);
        }
        return ok;
    }

    private static bool Has(string text, string part) => text != null && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

    // ---------------------------------------------------------------- compendium.x.textspage (X06)

    // The other mod: a postfix on the game's list building add its own page (test only, removed in finally).
    private static void OtherModPage_Postfix(TextsDialog __instance)
    {
        if (_pageOn && __instance != null && __instance.m_texts != null)
        {
            __instance.m_texts.Add(new TextsDialog.TextInfo(PageTopic, PageMarker + ": text of a page another mod adds to the Valheim Compendium."));
        }
    }

    private static IEnumerator RunTextsPage()
    {
        var run = new UiRun();
        yield return run.Begin(TextsPageName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        Harmony other = null;
        try
        {
            run.Force(sideButton: false);
            yield return TestKit.ShowInventory(gui);
            var raven = TopTabs.RavenButton(gui);
            var dialog = gui.m_textsDialog;
            var build = AccessTools.Method(typeof(TextsDialog), nameof(TextsDialog.UpdateTextsList));
            if (raven == null || dialog == null || build == null)
            {
                p.Add($"test setup: Valheim Compendium button {raven != null}, dialog {dialog != null}, its list building method {build != null}");
                run.Report();
                yield break;
            }
            if (TopTabs.VanillaShown(gui))
            {
                dialog.OnClose();
            }
            // Our window gone: it is built again below, from a dialog that carry the other mod's row.
            CompendiumWindow.Destroy();
            yield return null;
            other = new Harmony(ModInfo.Guid + ".selftest.othermodpage");
            other.Patch(build, postfix: new HarmonyMethod(typeof(UiModsSelfTests), nameof(OtherModPage_Postfix)));
            _pageOn = true;

            // Texts tab: its page is in the list as usual and show its text on a click.
            raven.onClick.Invoke();
            yield return TestKit.Frames(3);
            var texts = dialog.m_texts;
            var page = texts != null ? texts.FirstOrDefault(t => t != null && t.m_topic == PageTopic) : null;
            var row = page != null && page.m_listElement != null ? page.m_listElement.GetComponent<Button>() : null;
            if (!Need(p, TopTabs.VanillaShown(gui) && page != null && row != null && page.m_listElement.activeSelf,
                    $"Texts tab with another mod's page: Valheim Compendium shown {TopTabs.VanillaShown(gui)}, its page in the list {page != null} "
                    + $"({(texts != null ? texts.Count : 0)} texts), its row {row != null}"))
            {
                run.Report();
                yield break;
            }
            Need(p, TopTabs.VanillaStrip != null && TopTabs.VanillaTexts != null && !TopTabs.VanillaTexts.interactable && TopTabs.VanillaEncyclopedia != null,
                "Texts tab with another mod's page: the Texts / Encyclopedia tabs are not on the dialog with Texts current");
            row.onClick.Invoke();
            yield return TestKit.Frames(2);
            Need(p, dialog.m_textAreaTopic != null && dialog.m_textAreaTopic.text == PageTopic && dialog.m_textArea != null && Has(dialog.m_textArea.text, PageMarker)
                    && page.m_selected != null && page.m_selected.activeSelf,
                $"a click on the other mod's row: topic shown '{(dialog.m_textAreaTopic != null ? dialog.m_textAreaTopic.text : "")}' (expected '{PageTopic}'), "
                + $"its text shown {dialog.m_textArea != null && Has(dialog.m_textArea.text, PageMarker)}");

            // Encyclopedia tab: the window is built from this dialog and carry nothing of the page.
            if (TopTabs.VanillaEncyclopedia != null)
            {
                TopTabs.VanillaEncyclopedia.onClick.Invoke();
            }
            yield return TestKit.Frames(2);
            yield return TestKit.WaitPrepared();
            var root = CompendiumWindow.Root;
            // Window object on screen too: the "shown texts" check below would find nothing in a hidden window.
            if (!Need(p, CompendiumWindow.IsOpen && !CompendiumWindow.Preparing && root != null && root.activeInHierarchy && !TopTabs.VanillaShown(gui),
                    $"Encyclopedia tab with another mod's page in the list: Encyclopedia open {CompendiumWindow.IsOpen}, its window on screen "
                    + $"{root != null && root.activeInHierarchy}, Valheim Compendium shown {TopTabs.VanillaShown(gui)}"))
            {
                run.Report();
                yield break;
            }
            // Texts that are shown. (The hidden copy of the dialog's text area keep what the dialog showed when the
            // window was built: never drawn, so it is only noted.)
            var ofPage = root.GetComponentsInChildren<TMP_Text>(true).Where(t => t != null && (Has(t.text, PageTopic) || Has(t.text, PageMarker))).ToList();
            var carried = ofPage.Where(t => t.enabled && t.gameObject.activeInHierarchy).Select(t => UiUtil.Path(t.transform)).ToList();
            Need(p, carried.Count == 0 && root.GetComponentInChildren<TextsDialog>(true) == null,
                $"the Encyclopedia window shows the other mod's page: {carried.Count} text(s) ({string.Join(", ", carried.Take(3))}), a Valheim Compendium "
                + $"part inside it {root.GetComponentInChildren<TextsDialog>(true) != null}");
            run.Note($"texts of the other mod's page in the window: {carried.Count} shown, {ofPage.Count - carried.Count} in hidden objects (never drawn)");
            var tabBefore = CompendiumWindow.CurrentTab;
            for (var t = 0; t < Tabs.Count; t++)
            {
                CompendiumWindow.SelectTab(t);
                yield return null;
                var listed = CompendiumWindow.Rows.Count(r => Has(r.Text, PageTopic) || Has(r.Text, PageMarker));
                Need(p, listed == 0, $"category tab {t} of the Encyclopedia lists {listed} row(s) of the other mod's page");
            }
            if (Need(p, CompendiumWindow.Search != null, "the Encyclopedia has no search field"))
            {
                CompendiumWindow.Search.text = PageTopic;
                CompendiumWindow.ApplySearchNow();
                yield return null;
                var found = CompendiumWindow.Rows.Count(r => r.Kind == ListRowKind.Entry);
                Need(p, found == 0, $"a search for the other mod's page name finds {found} entry row(s) in the Encyclopedia");
            }
            CompendiumWindow.SelectTab(tabBefore); // leaves the search, the tab as it was
            yield return null;

            // Back on Texts: its page is listed again (the list is filled fresh).
            if (CompendiumWindow.TopTextsButton != null)
            {
                CompendiumWindow.TopTextsButton.onClick.Invoke();
            }
            yield return TestKit.Frames(3);
            var again = dialog.m_texts != null ? dialog.m_texts.FirstOrDefault(t => t != null && t.m_topic == PageTopic) : null;
            Need(p, TopTabs.VanillaShown(gui) && !CompendiumWindow.IsOpen && again != null && again.m_listElement != null && again.m_listElement.activeSelf,
                $"back on the Texts tab: Valheim Compendium shown {TopTabs.VanillaShown(gui)}, Encyclopedia open {CompendiumWindow.IsOpen}, the other "
                + $"mod's page listed again {again != null}");
            run.Detail = "a page another mod adds to the Valheim Compendium's list is on the Texts tab with our two tabs, its row shows its text, "
                         + "and it is listed again after a visit to the Encyclopedia; the Encyclopedia window built from that dialog has no text, no "
                         + "row and no search result of it";
        }
        finally
        {
            _pageOn = false;
            try
            {
                other?.UnpatchSelf();
            }
            catch (Exception e)
            {
                SelfTest.Note(TextsPageName, $"could not remove the test patch: {e.GetType().Name}");
            }
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.x.refused-build (X07)

    private struct RowPlace
    {
        internal RectTransform Rt;
        internal Vector2 Pos;
        internal Vector3 Scale;
    }

    // Active direct children of the side row holding a Selectable, ours excluded: where the game put them.
    private static List<RowPlace> SideRowPlaces(Transform parent)
    {
        var list = new List<RowPlace>();
        for (var i = 0; parent != null && i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child != null && child.name != SideButton.ButtonName && child.gameObject.activeSelf && child.GetComponentInChildren<Selectable>() != null)
            {
                list.Add(new RowPlace { Rt = child, Pos = child.anchoredPosition, Scale = child.localScale });
            }
        }
        return list;
    }

    private static string RowMoved(List<RowPlace> vanilla)
    {
        foreach (var c in vanilla)
        {
            if (c.Rt == null)
            {
                return "a side control is gone";
            }
            if (c.Rt.anchoredPosition != c.Pos || c.Rt.localScale != c.Scale)
            {
                return $"'{c.Rt.name}' at {c.Rt.anchoredPosition:F2} scale {c.Rt.localScale.x:F3} (the game puts it at {c.Pos:F2} scale {c.Scale.x:F3})";
            }
        }
        return null;
    }

    // An Encyclopedia side button that can be seen (a hidden one do not count).
    private static bool ButtonShown(InventoryGui gui)
    {
        var t = TestKit.FindUnder(gui, SideButton.ButtonName);
        return t != null && t.gameObject.activeInHierarchy;
    }

    private static IEnumerator RunRefusedBuild()
    {
        var run = new UiRun();
        yield return run.Begin(RefusedBuildName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        var windowRefused = CompendiumWindow.RefusedFor(gui);
        var tabsRefused = TopTabs.Refused;
        var expect = LogWatch.Expect(LogWatch.CannotBuild);
        try
        {
            run.Force(sideButton: false);
            yield return TestKit.ShowInventory(gui);
            var raven = TopTabs.RavenButton(gui);
            var dialog = gui.m_textsDialog;
            if (raven == null || dialog == null || gui.m_recipeElementPrefab == null || !LogWatch.Installed)
            {
                p.Add($"test setup: Valheim Compendium button {raven != null}, dialog {dialog != null}, crafting list row "
                      + $"{gui.m_recipeElementPrefab != null}, log watch {LogWatch.Installed}");
                run.Report();
                yield break;
            }
            if (TopTabs.VanillaShown(gui))
            {
                dialog.OnClose();
            }
            var vanilla = SideRowPlaces(raven.transform.parent);
            // Optional button on, and our window not built yet (as on a new inventory screen).
            Plugin.SetTestSideButton(true);
            CompendiumWindow.Destroy();
            yield return TestKit.Frames(2);
            raven.onClick.Invoke();
            yield return TestKit.Frames(3);
            if (!Need(p, ButtonShown(gui) && TopTabs.VanillaShown(gui) && TopTabs.VanillaEncyclopedia != null && CompendiumWindow.Root == null
                         && !CompendiumWindow.RefusedFor(gui),
                    $"test setup: Encyclopedia side button shown {ButtonShown(gui)}, Valheim Compendium shown {TopTabs.VanillaShown(gui)}, its Encyclopedia "
                    + $"tab {TopTabs.VanillaEncyclopedia != null}, window already built {CompendiumWindow.Root != null}, already refused {CompendiumWindow.RefusedFor(gui)}"))
            {
                run.Report();
                yield break;
            }
            var warnings = LogWatch.Count(LogWatch.CannotBuild);
            var bad = LogWatch.Bad;

            // The click on the Encyclopedia tab on a screen whose crafting list row is gone (what a UI mod that rebuilt
            // the crafting panel leave): the real build run and is refused. Row back before any other code run.
            var rowPrefab = gui.m_recipeElementPrefab;
            gui.m_recipeElementPrefab = null;
            try
            {
                TopTabs.VanillaEncyclopedia.onClick.Invoke();
            }
            finally
            {
                gui.m_recipeElementPrefab = rowPrefab;
            }
            yield return TestKit.Frames(3);
            var newWarnings = LogWatch.Count(LogWatch.CannotBuild) - warnings;
            var newBad = LogWatch.Bad - bad;
            Need(p, !CompendiumWindow.IsOpen && CompendiumWindow.Root == null && CompendiumWindow.RefusedFor(gui),
                $"after the refused build: Encyclopedia open {CompendiumWindow.IsOpen}, window objects left {CompendiumWindow.Root != null}, refused for "
                + $"this inventory screen {CompendiumWindow.RefusedFor(gui)}");
            Need(p, newWarnings == 1 && newBad == 1,
                $"after the refused build: {newWarnings} \"{LogWatch.CannotBuild}\" line(s) and {newBad} warning or error line(s) of the mod in all "
                + "(expected one warning and nothing else)");
            Need(p, TopTabs.VanillaStrip == null && TestKit.FindUnder(gui, TopTabs.StripName) == null,
                "after the refused build: the Texts / Encyclopedia tabs are still on the Valheim Compendium");
            Need(p, TopTabs.VanillaShown(gui) && dialog.m_texts != null && dialog.m_texts.Count > 0,
                $"after the refused build: the Valheim Compendium is shown {TopTabs.VanillaShown(gui)} with {(dialog.m_texts != null ? dialog.m_texts.Count : 0)} "
                + "texts (expected the game's dialog left open and filled)");
            Need(p, !ButtonShown(gui), "after the refused build: the Encyclopedia side button is still shown (it would do nothing)");
            var moved = RowMoved(vanilla);
            Need(p, moved == null, $"after the refused build the side row is not the game's: {moved}");

            // Whatever is tried after: nothing built, no tab, no button, no second warning.
            dialog.OnClose();
            yield return null;
            raven.onClick.Invoke();
            yield return TestKit.Frames(3);
            Need(p, TopTabs.VanillaShown(gui) && TopTabs.VanillaStrip == null && !CompendiumWindow.IsOpen,
                $"Valheim Compendium opened again: shown {TopTabs.VanillaShown(gui)}, our tabs on it {TopTabs.VanillaStrip != null}, Encyclopedia open {CompendiumWindow.IsOpen}");
            var opened = CompendiumWindow.Open(); // what a click on the side button runs
            Need(p, !opened && !CompendiumWindow.IsOpen && CompendiumWindow.Root == null,
                $"what a side button click runs, after the refusal: returned {opened}, Encyclopedia open {CompendiumWindow.IsOpen}, window built {CompendiumWindow.Root != null}");
            gui.Hide();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return TestKit.ShowInventory(gui);
            moved = RowMoved(vanilla);
            Need(p, !ButtonShown(gui) && moved == null,
                $"inventory closed and opened again: Encyclopedia side button shown {ButtonShown(gui)}, side row {(moved ?? "as the game puts it")}");
            Need(p, LogWatch.Bad - bad == 1,
                $"{LogWatch.Bad - bad - 1} more warning or error line(s) of the mod after the first refusal (expected none)");
            run.Detail = "on an inventory screen where the window cannot be built, the click on the Encyclopedia tab runs the real build, which is "
                         + "refused with one warning and nothing else in the log: the Valheim Compendium stays open without our tabs, the side "
                         + "button (setting on) is hidden, the side row is the game's; opening the dialog again, what a button click runs and "
                         + "reopening the inventory build nothing and log nothing more";
        }
        finally
        {
            Plugin.TestSideButton = false;
            SideButton.Sync(gui);
            CompendiumWindow.SetRefusedForTest(gui, windowRefused);
            TopTabs.SetRefusedForTest(tabsRefused);
            expect.Dispose();
            run.End();
        }
        run.Report();
    }
}
#endif
