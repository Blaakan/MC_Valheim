using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#endif

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Debug build only (calls vanish in Release): more in-world UI self tests, one small test per TESTING.md item so one
// failing check never block another item. Settings forced in memory only (Plugin.TestDisplay / TestSideButton, the
// feature off and on with Plugin.SetOffForTest), never a ConfigEntry write. Mouse clicks are played through the event
// system (pointer down, up, click on what a UI raycast finds at the point): what a real click there does.
//   compendium.window         = T03 / T28: title, "Discovered N / M", tabs, search, rows, headers, text sizes, no overlap.
//   compendium.undiscovered   = T06: "Not discovered yet." + hint, unknown rows in game order, no tooltip, paw print.
//   compendium.midgame        = T09: a character that already knows a lot: all of it at the first open.
//   compendium.memory         = T29: tab, entry per tab and search come back after a close.
//   compendium.search         = T16: results from every tab, discovered only; typing guard.
//   compendium.links          = T17: a discovered ingredient row opens its entry, a "???" row does nothing.
//   compendium.settings       = T18: ShowUndiscovered / RevealAll apply at once to the open window.
//   compendium.pad            = T15 / T26 / T30 / T37: what the controller code does (no button can be pressed).
//   compendium.close          = T04 / T36: Close button, click outside, Esc path, Tab path, teleport; raven reopens.
//   compendium.mouse          = T27: clicks on the inventory through the open window.
//   compendium.sidebutton     = T01 / T02 / T36: the opt-in button: icon, tooltip, click, placement at other UI scales.
//   compendium.sidecount      = T01 / T38: five vanilla side controls, six with ours.
//   compendium.sidelock       = T03: side row locked while a vanilla side dialog is open.
//   compendium.buildclose     = T25: window closed during the catalog build (Tab path, Esc path), one build line each.
//   compendium.cleanlog       = T24: no warning or error line of the mod since it started.
//   compendium.texts          = T35: a text learned while the Encyclopedia is open is in the Texts list after.
//   compendium.bug.refused-toggle = T39: a refused window stays refused after the feature goes off and on. Real bug
//                               of the mod (CompendiumWindow.Destroy forget the refusal): fail until the mod is fixed.
internal static class UiMoreSelfTests
{
#if DEBUG
    private const string WindowName = "compendium.window";
    private const string UndiscoveredName = "compendium.undiscovered";
    private const string MidgameName = "compendium.midgame";
    private const string MemoryName = "compendium.memory";
    private const string SearchName = "compendium.search";
    private const string LinksName = "compendium.links";
    private const string SettingsName = "compendium.settings";
    private const string PadName = "compendium.pad";
    private const string CloseName = "compendium.close";
    private const string MouseName = "compendium.mouse";
    private const string SideButtonName = "compendium.sidebutton";
    private const string SideCountName = "compendium.sidecount";
    private const string SideLockName = "compendium.sidelock";
    private const string BuildCloseName = "compendium.buildclose";
    private const string CleanLogName = "compendium.cleanlog";
    private const string TextsName = "compendium.texts";
    // Real bug of the mod (T39), alone in its test: it fails until the mod is fixed.
    private const string RefusedToggleName = "compendium.bug.refused-toggle";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly KeyValuePair<string, Func<IEnumerator>>[] Tests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(WindowName, RunWindow),
        new KeyValuePair<string, Func<IEnumerator>>(UndiscoveredName, RunUndiscovered),
        new KeyValuePair<string, Func<IEnumerator>>(MidgameName, RunMidgame),
        new KeyValuePair<string, Func<IEnumerator>>(MemoryName, RunMemory),
        new KeyValuePair<string, Func<IEnumerator>>(SearchName, RunSearch),
        new KeyValuePair<string, Func<IEnumerator>>(LinksName, RunLinks),
        new KeyValuePair<string, Func<IEnumerator>>(SettingsName, RunSettings),
        new KeyValuePair<string, Func<IEnumerator>>(PadName, RunPad),
        new KeyValuePair<string, Func<IEnumerator>>(CloseName, RunClose),
        new KeyValuePair<string, Func<IEnumerator>>(MouseName, RunMouse),
        new KeyValuePair<string, Func<IEnumerator>>(SideButtonName, RunSideButton),
        new KeyValuePair<string, Func<IEnumerator>>(SideCountName, RunSideCount),
        new KeyValuePair<string, Func<IEnumerator>>(SideLockName, RunSideLock),
        new KeyValuePair<string, Func<IEnumerator>>(BuildCloseName, RunBuildClose),
        new KeyValuePair<string, Func<IEnumerator>>(CleanLogName, RunCleanLog),
        new KeyValuePair<string, Func<IEnumerator>>(TextsName, RunTexts),
        new KeyValuePair<string, Func<IEnumerator>>(RefusedToggleName, RunRefusedToggle),
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
#endif
    }

#if DEBUG
    // ---------------------------------------------------------------- small helpers

    private static string Join(IEnumerable<string> lines) => string.Join(" | ", lines);

    private static Rect R(Component c) => UiUtil.WorldRect((RectTransform)c.transform);

    // Where a text really draws (rendered bounds; the rect of a centred title is the whole line).
    private static Rect TextBounds(TMP_Text t)
    {
        t.ForceMeshUpdate();
        if (t.textInfo == null || t.textInfo.characterCount == 0)
        {
            return UiUtil.WorldRect(t.rectTransform);
        }
        var b = t.textBounds;
        var min = t.rectTransform.TransformPoint(b.min);
        var max = t.rectTransform.TransformPoint(b.max);
        return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }

    // Sorting order a canvas really draws with: its own when it is a root or overrides sorting, else its nearest such parent's.
    private static int DrawOrder(Canvas canvas)
    {
        for (var c = canvas; c != null;)
        {
            if (c.isRootCanvas || c.overrideSorting)
            {
                return c.sortingOrder;
            }
            var parent = c.transform.parent != null ? c.transform.parent.GetComponentInParent<Canvas>(true) : null;
            if (parent == null)
            {
                return c.sortingOrder;
            }
            c = parent;
        }
        return 0;
    }

    // The wood panel of the open window (vanilla: Texts_frame/bkg, sprite woodpanel_texts). None: empty rect.
    private static Rect WindowFrame()
    {
        var root = CompendiumWindow.Root;
        if (root == null)
        {
            return default;
        }
        foreach (var img in root.GetComponentsInChildren<Image>(true))
        {
            if (img != null && img.sprite != null && img.sprite.name.IndexOf("woodpanel", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return UiUtil.WorldRect(img.rectTransform);
            }
        }
        return default;
    }

    private static bool OnScreen(Vector2 point) => point.x > 4f && point.y > 4f && point.x < Screen.width - 4f && point.y < Screen.height - 4f;

    // A screen point beside the window's wood panel (where the dimmed screen shows). False = none found.
    private static bool PointOutside(Rect frame, out Vector2 point)
    {
        point = default;
        if (frame.width < 2f)
        {
            return false;
        }
        foreach (var c in new[]
                 {
                     new Vector2(frame.xMin - 30f, frame.center.y), new Vector2(frame.xMax + 30f, frame.center.y),
                     new Vector2(frame.center.x, frame.yMin - 30f), new Vector2(frame.center.x, frame.yMax + 30f),
                 })
        {
            if (OnScreen(c))
            {
                point = c;
                return true;
            }
        }
        return false;
    }

    // Pixels of a texture that is not readable (drawn once, then given to the GPU): copied through a render texture.
    private static Color32[] ReadBack(Texture texture)
    {
        var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Texture2D copy = null;
        try
        {
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0);
            copy.Apply();
            return copy.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            if (copy != null)
            {
                UnityEngine.Object.Destroy(copy);
            }
        }
    }

    private static bool SameLine(Rect a, Rect line) => a.center.y >= line.yMin - 2f && a.center.y <= line.yMax + 2f;

    // Active direct children of the side row holding a Selectable, left to right.
    private static List<RectTransform> SideControls(Transform parent)
    {
        var list = new List<RectTransform>();
        for (var i = 0; parent != null && i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child != null && child.gameObject.activeInHierarchy && child.GetComponentInChildren<Selectable>() != null)
            {
                list.Add(child);
            }
        }
        list.Sort((a, b) => UiUtil.WorldRect(a).center.x.CompareTo(UiUtil.WorldRect(b).center.x));
        return list;
    }

    private static string ClickMethod(Component control)
    {
        var b = control != null ? control.GetComponentInChildren<Button>() : null;
        if (b == null)
        {
            return control != null && control.GetComponentInChildren<Toggle>() != null ? "toggle" : "";
        }
        if (b.name == SideButton.ButtonName)
        {
            return "ours";
        }
        return b.onClick.GetPersistentEventCount() > 0 ? b.onClick.GetPersistentMethodName(0) : "";
    }

    private static Button SideButtonBy(InventoryGui gui, string method)
    {
        foreach (var b in gui.GetComponentsInChildren<Button>(true))
        {
            if (b == null || b.name == SideButton.ButtonName)
            {
                continue;
            }
            for (var i = 0; i < b.onClick.GetPersistentEventCount(); i++)
            {
                if (b.onClick.GetPersistentMethodName(i) == method)
                {
                    return b;
                }
            }
        }
        return null;
    }

    private static bool PatchedByUs(Type type, string method, bool prefix)
    {
        var info = Harmony.GetPatchInfo(AccessTools.Method(type, method));
        if (info == null)
        {
            return false;
        }
        return (prefix ? info.Prefixes : info.Postfixes).Any(p => p.owner == ModInfo.Guid);
    }

    private static List<ListRow> EntryRows() => CompendiumWindow.Rows.Where(r => r.Kind == ListRowKind.Entry).ToList();

    // ---------------------------------------------------------------- compendium.window (T03, T28)

    private static IEnumerator RunWindow()
    {
        var run = new UiRun();
        yield return run.Begin(WindowName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        try
        {
            run.Force(sideButton: false);
            // A few things known, so every kind of row and line is on screen.
            var wood = run.Cat.FindItemByPrefab("Wood");
            var rasp = run.Cat.FindItemByPrefab("Raspberry");
            foreach (var e in new[] { wood, rasp })
            {
                if (e != null)
                {
                    run.Player.m_knownMaterial.Add(e.Key);
                }
            }
            yield return TestKit.ShowInventory(gui);
            yield return TestKit.OpenByTab(gui, p, "open");
            if (CompendiumWindow.IsOpen && !CompendiumWindow.Preparing)
            {
                yield return null;
                // Title and counter.
                var title = CompendiumWindow.TitleText;
                var counter = CompendiumWindow.CounterText;
                var k = CompendiumWindow.CurrentKnowledge;
                var real = Knowledge.Take(run.Cat, false);
                if (title == null || title.text != Labels.WindowTitle || !title.gameObject.activeInHierarchy)
                {
                    p.Add($"window title is '{(title != null ? title.text : "missing")}' (expected \"{Labels.WindowTitle}\")");
                }
                var wantCounter = string.Format(Labels.DiscoveredFormat, real.DiscoveredCount, real.ListedCount);
                if (counter == null || k == null || counter.text != wantCounter || !counter.gameObject.activeInHierarchy)
                {
                    p.Add($"counter reads '{(counter != null ? counter.text : "missing")}' (expected \"{wantCounter}\")");
                }
                if (title != null && counter != null)
                {
                    var titleLine = UiUtil.WorldRect(title.rectTransform);
                    var titleText = TextBounds(title);
                    var counterText = TextBounds(counter);
                    counter.ForceMeshUpdate();
                    if (!SameLine(counterText, titleLine) || counterText.xMin < titleText.xMax - 1f)
                    {
                        p.Add($"the counter {UiUtil.Fmt(counterText)} is not on the title line, right of the title {UiUtil.Fmt(titleText)} (line {UiUtil.Fmt(titleLine)})");
                    }
                    if (counter.isTextTruncated || counter.fontSize < 10f || !UiUtil.Inside(counterText, UiUtil.WorldRect((RectTransform)CompendiumWindow.Root.transform), 1f))
                    {
                        p.Add($"the counter is cut (truncated {counter.isTextTruncated}, size {counter.fontSize:F1}, text {UiUtil.Fmt(counterText)})");
                    }
                    // Texts / Encyclopedia tabs: on the title line, left of the title.
                    foreach (var tab in new[] { CompendiumWindow.TopTextsButton, CompendiumWindow.TopEncyclopediaButton })
                    {
                        if (tab == null)
                        {
                            p.Add("a top tab (Texts / Encyclopedia) is missing in the window");
                            continue;
                        }
                        var r = R(tab);
                        if (Mathf.Min(r.yMax, titleLine.yMax) - Mathf.Max(r.yMin, titleLine.yMin) < r.height * 0.5f || r.xMax > titleText.xMin + 1f)
                        {
                            p.Add($"top tab '{tab.name}' {UiUtil.Fmt(r)} is not on the title line, left of the title {UiUtil.Fmt(titleText)}");
                        }
                    }
                }

                // Eight category tabs: one row, under the top tabs, above both panes.
                var tabs = CompendiumWindow.TabButtonList;
                var listView = CompendiumWindow.ListViewport != null ? UiUtil.WorldRect(CompendiumWindow.ListViewport) : default;
                var detailView = CompendiumWindow.DetailViewport != null ? UiUtil.WorldRect(CompendiumWindow.DetailViewport) : default;
                if (CompendiumWindow.TabRowCount != 1 || tabs.Count != Tabs.Count || tabs.Any(t => t == null || !t.gameObject.activeInHierarchy))
                {
                    p.Add($"category tabs: {CompendiumWindow.TabRowCount} row(s), {tabs.Count(t => t != null)} of {Tabs.Count} tabs");
                }
                else
                {
                    var first = R(tabs[0]);
                    var topTab = CompendiumWindow.TopTextsButton != null ? R(CompendiumWindow.TopTextsButton) : first;
                    for (var t = 0; t < tabs.Count; t++)
                    {
                        var r = R(tabs[t]);
                        if (Mathf.Abs(r.yMin - first.yMin) > 1f || (t > 0 && r.xMin < R(tabs[t - 1]).xMax - 1f))
                        {
                            p.Add($"category tab {t} {UiUtil.Fmt(r)} is not in one row after tab {t - 1}");
                        }
                        if (r.yMax > topTab.yMin + 1f || r.yMin < Mathf.Max(listView.yMax, detailView.yMax) - 1f)
                        {
                            p.Add($"category tab {t} {UiUtil.Fmt(r)} is not under the top tabs {UiUtil.Fmt(topTab)} and above the list {UiUtil.Fmt(listView)} and details {UiUtil.Fmt(detailView)}");
                        }
                    }
                }

                // Search field: top of the list box.
                var search = CompendiumWindow.Search;
                if (search == null || !search.gameObject.activeInHierarchy)
                {
                    p.Add("no search field in the window");
                }
                else
                {
                    var r = R(search);
                    var overlapX = Mathf.Min(r.xMax, listView.xMax) - Mathf.Max(r.xMin, listView.xMin);
                    if (r.yMin < listView.yMax - 1f || overlapX < r.width * 0.7f || UiUtil.Overlaps(r, detailView, 1f))
                    {
                        p.Add($"the search field {UiUtil.Fmt(r)} is not at the top of the list box {UiUtil.Fmt(listView)}");
                    }
                }

                // The dump's OVERLAP rule, checked here: none of our parts covers the title, the close button or another.
                var ours = new List<KeyValuePair<string, Rect>>();
                foreach (var t in tabs)
                {
                    if (t != null)
                    {
                        ours.Add(new KeyValuePair<string, Rect>(t.name, R(t)));
                    }
                }
                foreach (var b in new[] { CompendiumWindow.TopTextsButton, CompendiumWindow.TopEncyclopediaButton })
                {
                    if (b != null)
                    {
                        ours.Add(new KeyValuePair<string, Rect>(b.name, R(b)));
                    }
                }
                if (counter != null)
                {
                    ours.Add(new KeyValuePair<string, Rect>("counter", TextBounds(counter)));
                }
                if (search != null)
                {
                    ours.Add(new KeyValuePair<string, Rect>("search", R(search)));
                }
                var closeRect = CompendiumWindow.CloseButton != null ? R(CompendiumWindow.CloseButton) : default;
                var titleRect = title != null ? TextBounds(title) : default;
                for (var i = 0; i < ours.Count; i++)
                {
                    if (CompendiumWindow.CloseButton != null && UiUtil.Overlaps(ours[i].Value, closeRect, 1f))
                    {
                        p.Add($"OVERLAP: {ours[i].Key} covers the close button");
                    }
                    if (title != null && UiUtil.Overlaps(ours[i].Value, titleRect, 1f))
                    {
                        p.Add($"OVERLAP: {ours[i].Key} covers the title");
                    }
                    for (var j = i + 1; j < ours.Count; j++)
                    {
                        if (UiUtil.Overlaps(ours[i].Value, ours[j].Value, 1f))
                        {
                            p.Add($"OVERLAP: {ours[i].Key} and {ours[j].Key}");
                        }
                    }
                }

                // List rows: icon and name side by side; headers orange text, no background, not clickable.
                CompendiumWindow.SelectTab((int)CatalogTab.Materials);
                yield return null;
                yield return null;
                var rows = CompendiumWindow.Rows;
                var entriesSeen = 0;
                var headersSeen = 0;
                foreach (var row in CompendiumWindow.ListPool)
                {
                    if (!row.Go.activeSelf || row.Bound < 0 || row.Bound >= rows.Count)
                    {
                        continue;
                    }
                    if (rows[row.Bound].Kind == ListRowKind.Header)
                    {
                        headersSeen++;
                        var c = row.Name.color;
                        if (Mathf.Abs(c.r - RowView.HeaderColor.r) > 0.02f || Mathf.Abs(c.g - RowView.HeaderColor.g) > 0.02f || Mathf.Abs(c.b - RowView.HeaderColor.b) > 0.02f
                            || (row.Bg != null && row.Bg.enabled) || (row.Button != null && row.Button.enabled))
                        {
                            p.Add($"header row '{row.Name.text}' is not plain orange text (colour {c}, background {row.Bg != null && row.Bg.enabled}, clickable {row.Button != null && row.Button.enabled})");
                        }
                        continue;
                    }
                    entriesSeen++;
                    var iconRect = UiUtil.WorldRect(row.Icon.rectTransform);
                    var nameRect = UiUtil.WorldRect(row.Name.rectTransform);
                    var rowRect = UiUtil.WorldRect(row.Rt);
                    if (UiUtil.Overlaps(iconRect, nameRect, 0.5f) || !UiUtil.Inside(iconRect, rowRect, 1f) || !UiUtil.Inside(nameRect, rowRect, 1f)
                        || nameRect.xMin < iconRect.xMax - 0.5f)
                    {
                        p.Add($"row {row.Bound}: icon {UiUtil.Fmt(iconRect)} and name {UiUtil.Fmt(nameRect)} overlap or leave the row {UiUtil.Fmt(rowRect)}");
                        break;
                    }
                }
                if (entriesSeen == 0 || headersSeen == 0)
                {
                    p.Add($"Materials tab shows {entriesSeen} entry rows and {headersSeen} header rows on screen");
                }

                // Details: rows as large as the paragraphs, paragraphs wrap, a long page scrolls.
                Plugin.SetTestDisplay(new Plugin.DisplayOverride { ShowUndiscovered = true, RevealAll = true });
                run.Cat.PiecesByPrefab.TryGetValue("forge", out var forge);
                CompendiumWindow.OpenEntry(forge ?? wood);
                yield return null;
                yield return null;
                var para = CompendiumWindow.ParagraphFontSize;
                var shownRows = CompendiumWindow.DetailRowsShown;
                for (var i = 0; i < shownRows && i < CompendiumWindow.DetailRowViews.Count; i++)
                {
                    var name = CompendiumWindow.DetailRowViews[i].Name;
                    if (name.enableAutoSizing || Mathf.Abs(name.fontSize - para) > 0.01f)
                    {
                        p.Add($"detail row {i} text size {name.fontSize:F1} (auto {name.enableAutoSizing}) differs from the paragraphs' {para:F1}");
                        break;
                    }
                }
                for (var i = 0; i < CompendiumWindow.ParagraphsShown && i < CompendiumWindow.ParagraphTexts.Count; i++)
                {
                    var t = CompendiumWindow.ParagraphTexts[i];
                    if (t.textWrappingMode != TextWrappingModes.Normal
                        || UiUtil.WorldRect(t.rectTransform).width > UiUtil.WorldRect(CompendiumWindow.DetailViewport).width + 1f)
                    {
                        p.Add($"detail paragraph {i} does not wrap inside the details pane");
                        break;
                    }
                }
                var content = CompendiumWindow.DetailContent;
                var scroll = CompendiumWindow.DetailScroll;
                var viewport = CompendiumWindow.DetailViewport;
                if (shownRows == 0 || para <= 0f || content == null || scroll == null)
                {
                    p.Add($"details of a long entry: {shownRows} rows, paragraph size {para:F1}");
                }
                else if (content.rect.height <= viewport.rect.height + 1f)
                {
                    p.Add($"the Forge's details ({content.rect.height:F0} units) do not exceed the pane ({viewport.rect.height:F0}): scrolling not checked");
                }
                else
                {
                    var before = scroll.verticalNormalizedPosition;
                    for (var i = 0; i < 5; i++)
                    {
                        CompendiumWindow.ScrollDetails(1f);
                        yield return null;
                    }
                    if (!scroll.vertical || scroll.verticalNormalizedPosition >= before - 0.001f)
                    {
                        p.Add($"a long details page does not scroll (position {before:F3} -> {scroll.verticalNormalizedPosition:F3})");
                    }
                }
                Plugin.SetTestDisplay(new Plugin.DisplayOverride { ShowUndiscovered = true, RevealAll = false });

                // Screen behind dimmed like the game's dialog; console above the window.
                var vanillaDark = gui.m_textsDialog.transform.Find("darken");
                var ourDark = CompendiumWindow.Root.transform.Find("darken");
                if (vanillaDark != null)
                {
                    var img = ourDark != null ? ourDark.GetComponent<Image>() : null;
                    var vanillaImg = vanillaDark.GetComponent<Image>();
                    if (ourDark == null || !ourDark.gameObject.activeInHierarchy || img == null || !img.enabled || img.color.a < 0.05f
                        || (vanillaImg != null && img.color != vanillaImg.color))
                    {
                        p.Add("the window does not dim the screen behind it like the Valheim Compendium (its 'darken' layer is missing, off or changed)");
                    }
                }
                else
                {
                    run.Note("the Valheim Compendium has no 'darken' child here: dimming not compared");
                }
                var consoleCanvas = global::Console.instance != null ? global::Console.instance.GetComponentInParent<Canvas>(true) : null;
                var windowCanvas = CompendiumWindow.WindowCanvas;
                var consoleOrder = consoleCanvas != null ? DrawOrder(consoleCanvas) : int.MinValue;
                var windowOrder = windowCanvas != null ? DrawOrder(windowCanvas) : int.MaxValue;
                if (consoleCanvas == null || windowCanvas == null || consoleOrder <= windowOrder)
                {
                    p.Add($"the console canvas (order {consoleOrder}) does not sort above the window (order {windowOrder})");
                }
                run.Note($"screen {Screen.width}x{Screen.height}; title '{(title != null ? title.text : "")}', counter '{(counter != null ? counter.text : "")}', "
                         + $"{CompendiumWindow.TabRowCount} tab row(s), paragraph size {para:F1}, console order {consoleOrder} vs window {windowOrder}");
                run.Detail = $"title \"{Labels.WindowTitle}\", \"{wantCounter}\" on the title line; top tabs left of the title; 8 category tabs in one row above "
                             + "both panes; search on top of the list; no overlap; orange headers without background; detail rows as large as "
                             + $"paragraphs; long page scrolls; dimmed; console above (screen {Screen.width}x{Screen.height})";
            }
        }
        finally
        {
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.undiscovered (T06)

    private static IEnumerator RunUndiscovered()
    {
        var run = new UiRun();
        yield return run.Begin(UndiscoveredName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        var cat = run.Cat;
        try
        {
            run.Force(sideButton: false);
            cat.CreaturesByPrefab.TryGetValue("Boar", out var boar);
            if (boar == null || boar.Creature.Trophy == null)
            {
                p.Add("no Boar entry with a trophy");
            }
            else
            {
                // Boar met only (trophy never held, never killed).
                run.Player.m_customData.Remove(OwnRecords.SeenKey);
                run.Player.m_customData.Remove(TamesReader.Key);
                OwnRecords.ClearCache();
                TamesReader.ClearCache();
                Game.instance.GetPlayerProfile().m_playerStats[0].m_enemyStats[0].Remove(boar.Key);
                var trophy = boar.Creature.Trophy;
                run.Player.m_knownMaterial.Remove(trophy.Key);
                run.Player.m_knownRecipes.Remove(trophy.Key);
                foreach (var prefab in trophy.Item.Prefabs)
                {
                    run.Player.m_trophies.Remove(prefab != null ? prefab.name : "");
                }
                OwnRecords.MarkSeen(boar.Key);
            }
            yield return TestKit.ShowInventory(gui);
            yield return TestKit.OpenByTab(gui, p, "open");
            if (CompendiumWindow.IsOpen && !CompendiumWindow.Preparing)
            {
                // Details of an undiscovered item, piece and creature: "Not discovered yet." + the hint of its kind.
                foreach (var (tab, hint) in new[]
                         {
                             (CatalogTab.Weapons, Labels.HintItem), (CatalogTab.Building, Labels.HintPiece), (CatalogTab.Creatures, Labels.HintCreature),
                         })
                {
                    CompendiumWindow.SelectTab((int)tab);
                    yield return null;
                    if (!CompendiumWindow.SelectWhere(r => !r.Known))
                    {
                        p.Add($"no undiscovered row in the {tab} tab");
                        continue;
                    }
                    yield return null;
                    var want = Labels.NotDiscovered + " " + hint;
                    var lines = CompendiumWindow.RenderedLines;
                    var topic = CompendiumWindow.TopicText != null ? CompendiumWindow.TopicText.text : "";
                    if (lines.Count != 1 || lines[0] != want || topic != Labels.Unknown || CompendiumWindow.HeroShowsSprite || !CompendiumWindow.HeroShowsMark)
                    {
                        p.Add($"undiscovered entry of {tab}: title '{topic}', lines [{Join(lines)}] (expected \"???\" and \"{want}\")");
                    }
                }

                // Undiscovered rows of each group: after the discovered ones (compendium.details), in game order, not by name.
                var k = CompendiumWindow.CurrentKnowledge;
                var groupsChecked = 0;
                var groupsNotByName = 0;
                for (var t = 0; t < Tabs.Count && k != null; t++)
                {
                    var shownUnknown = new Dictionary<SubGroup, List<Entry>>();
                    foreach (var row in ListBuilder.BuildTab(cat, k, (CatalogTab)t, showUndiscovered: true))
                    {
                        if (row.Kind != ListRowKind.Entry || row.Known || row.Entry.SubGroup == null)
                        {
                            continue;
                        }
                        if (!shownUnknown.TryGetValue(row.Entry.SubGroup, out var list))
                        {
                            shownUnknown[row.Entry.SubGroup] = list = new List<Entry>();
                        }
                        list.Add(row.Entry);
                    }
                    foreach (var kv in shownUnknown)
                    {
                        // The group's own (game) order, undiscovered and listed ones only.
                        var want = kv.Key.Entries.Where(e => !k.IsKnown(e) && !e.HiddenUntilKnown).ToList();
                        if (!kv.Value.SequenceEqual(want))
                        {
                            p.Add($"tab {(CatalogTab)t}: the undiscovered rows of a group are not in the game's order");
                            break;
                        }
                        if (kv.Value.Count >= 4)
                        {
                            groupsChecked++;
                            var byName = kv.Value.OrderBy(e => e.SortKey, StringComparer.OrdinalIgnoreCase).ToList();
                            groupsNotByName += byName.SequenceEqual(kv.Value) ? 0 : 1;
                        }
                    }
                }
                if (groupsChecked == 0 || groupsNotByName == 0)
                {
                    p.Add($"undiscovered rows look sorted by name in all {groupsChecked} groups with 4 or more of them (the order must not hint at the names)");
                }

                // No tooltip anywhere in the window (nothing can show a name on hover).
                var tips = CompendiumWindow.Root.GetComponentsInChildren<UITooltip>(true).Length;
                if (tips > 0)
                {
                    p.Add($"{tips} tooltip component(s) in the window");
                }

                // Met-only Boar: paw print (row and head), its trophy still "???" in the Trophies tab and in its drops.
                if (boar != null && boar.Creature.Trophy != null)
                {
                    CompendiumWindow.SelectTab((int)CatalogTab.Creatures);
                    yield return null;
                    CompendiumWindow.OpenEntry(boar);
                    yield return null;
                    yield return null;
                    var paw = PawIcon.Get();
                    var rowView = TestKit.BoundRow(boar);
                    var view = CompendiumWindow.ShownDetails;
                    if (!ReferenceEquals(CompendiumWindow.SelectedEntry, boar) || view == null || view.Title != boar.DisplayName)
                    {
                        p.Add($"met-only Boar not shown by name (title '{(view != null ? view.Title : "nothing")}')");
                    }
                    if (!ReferenceEquals(CompendiumWindow.HeroSprite, paw) || rowView == null || !rowView.Icon.enabled || !ReferenceEquals(rowView.Icon.sprite, paw))
                    {
                        p.Add("met-only Boar: head or list row does not show the paw print icon");
                    }
                    else
                    {
                        // Light grey, not a red mark: the drawn pixels (read back through a render texture: the
                        // sprite's own texture is not readable) and the row's tint.
                        try
                        {
                            float r = 0f, g = 0f, b = 0f;
                            var n = 0;
                            foreach (var px in ReadBack(paw.texture))
                            {
                                if (px.a > 200)
                                {
                                    r += px.r;
                                    g += px.g;
                                    b += px.b;
                                    n++;
                                }
                            }
                            var tint = rowView.Icon.color;
                            run.Note($"paw print: {n} solid pixels, mean colour {(n > 0 ? r / n : 0f):F0},{(n > 0 ? g / n : 0f):F0},{(n > 0 ? b / n : 0f):F0}, row tint {tint}");
                            if (n < 200 || Mathf.Abs(r - g) / n > 8f || Mathf.Abs(g - b) / n > 8f || r / n < 100f
                                || Mathf.Abs(tint.r - tint.g) > 0.05f || Mathf.Abs(tint.g - tint.b) > 0.05f)
                            {
                                p.Add($"the paw print is not light grey (mean colour {(n > 0 ? r / n : 0f):F0},{(n > 0 ? g / n : 0f):F0},{(n > 0 ? b / n : 0f):F0}, row tint {tint})");
                            }
                        }
                        catch (Exception e)
                        {
                            p.Add($"the paw print's pixels could not be read back ({e.GetType().Name}: {e.Message}): its colour is not checked");
                        }
                    }
                    var trophy = boar.Creature.Trophy;
                    var dropRef = view != null
                        ? view.Lines.SelectMany(l => l.Parts).FirstOrDefault(part => part.Ref != null && ReferenceEquals(part.Ref.Entry, trophy))
                        : default;
                    if (dropRef.Ref == null || dropRef.Ref.Known || dropRef.Ref.Name != Labels.Unknown || dropRef.Ref.Icon != null)
                    {
                        p.Add("met-only Boar: its drops do not list the trophy as \"???\"");
                    }
                    if (CompendiumWindow.RenderedLines.Any(l => TestKit.ContainsWord(l, trophy.SortKey)))
                    {
                        p.Add("met-only Boar: its details name the trophy");
                    }
                    CompendiumWindow.SelectTab((int)CatalogTab.Trophies);
                    yield return null;
                    var trophyRow = CompendiumWindow.Rows.FirstOrDefault(r => r.Kind == ListRowKind.Entry && ReferenceEquals(r.Entry, trophy));
                    if (trophyRow.Entry == null || trophyRow.Known || trophyRow.Text != Labels.Unknown || trophyRow.Icon != null)
                    {
                        p.Add($"met-only Boar: its trophy row in the Trophies tab shows '{trophyRow.Text}'");
                    }
                }
                run.Detail = "undiscovered item, piece and creature say \"Not discovered yet.\" with their hint; unknown rows in game order; no "
                             + "tooltip in the window; a met-only Boar shows the grey paw print (row and head), its trophy stays \"???\"";
            }
        }
        finally
        {
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.midgame (T09)

    private static IEnumerator RunMidgame()
    {
        var run = new UiRun();
        yield return run.Begin(MidgameName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        var cat = run.Cat;
        try
        {
            run.Force(sideButton: false);
            var player = run.Player;
            var stats = Game.instance.GetPlayerProfile().m_playerStats[0];
            var k0 = Knowledge.Take(cat, false);
            var injected = new List<Entry>();
            // What a character file of a mid-game player holds: the same vanilla tables, filled.
            var items = cat.Entries.Where(e => e.Kind == EntryKind.Item && !k0.IsKnown(e) && e.Item.Kind != ItemKind.Trophy).ToList();
            foreach (var e in items.Where(e => e.Item.Recipes.Count == 0).Take(40))
            {
                player.m_knownMaterial.Add(e.Key);
                injected.Add(e);
            }
            foreach (var e in items.Where(e => e.Item.Recipes.Count > 0).Take(25))
            {
                player.m_knownRecipes.Add(e.Key);
                injected.Add(e);
            }
            var creatures = cat.Entries.Where(e => e.Kind == EntryKind.Creature && !k0.IsKnown(e)).ToList();
            foreach (var c in creatures.Where(c => c.Creature.Trophy != null && !k0.IsKnown(c.Creature.Trophy)).Take(8))
            {
                player.m_trophies.Add(c.Creature.Trophy.PrefabName);
                injected.Add(c.Creature.Trophy);
                injected.Add(c);
            }
            foreach (var c in creatures.Where(c => !injected.Contains(c)).Take(6))
            {
                stats.m_enemyStats[0][c.Key] = 3f;
                injected.Add(c);
            }
            var pieces = cat.Entries.Where(e => e.Kind == EntryKind.Piece && !k0.IsKnown(e)).ToList();
            foreach (var e in pieces.Where(e => e.Piece.StationName == null).Take(12))
            {
                player.m_knownRecipes.Add(e.Key);
                injected.Add(e);
            }
            foreach (var e in pieces.Where(e => e.Piece.StationName != null).Take(2))
            {
                player.m_knownStations[e.Piece.StationName] = 1;
                injected.Add(e);
            }
            foreach (var e in pieces.Where(e => !injected.Contains(e)).Take(3))
            {
                stats.m_piecesPlacedStats[e.Key] = 2f;
                injected.Add(e);
            }
            injected = injected.Distinct().ToList();
            if (injected.Count < 60)
            {
                p.Add($"setup: only {injected.Count} entries could be marked");
            }

            yield return TestKit.ShowInventory(gui);
            yield return TestKit.OpenByTab(gui, p, "first open");
            if (CompendiumWindow.IsOpen && !CompendiumWindow.Preparing)
            {
                var k = CompendiumWindow.CurrentKnowledge;
                var fresh = Knowledge.Take(cat, false);
                var counter = CompendiumWindow.CounterText != null ? CompendiumWindow.CounterText.text : "";
                var want = string.Format(Labels.DiscoveredFormat, fresh.DiscoveredCount, fresh.ListedCount);
                if (counter != want || fresh.DiscoveredCount < k0.DiscoveredCount + injected.Count)
                {
                    p.Add($"counter '{counter}' (expected \"{want}\", at least {k0.DiscoveredCount + injected.Count} discovered: {k0.DiscoveredCount} before + {injected.Count} marked)");
                }
                var missing = injected.Where(e => k == null || !k.IsKnown(e)).Take(5).ToList();
                if (missing.Count > 0)
                {
                    p.Add($"not discovered at the first open: {string.Join(", ", missing.Select(e => e.ToString()))}");
                }
                // Each one is a named row of its tab.
                for (var t = 0; t < Tabs.Count; t++)
                {
                    var mine = injected.Where(e => (int)e.Tab == t).ToList();
                    if (mine.Count == 0)
                    {
                        continue;
                    }
                    CompendiumWindow.SelectTab(t);
                    yield return null;
                    var rows = CompendiumWindow.Rows;
                    var bad = mine.Where(e => !rows.Any(r => r.Kind == ListRowKind.Entry && ReferenceEquals(r.Entry, e) && r.Known && r.Text == e.DisplayName)).Take(3).ToList();
                    if (bad.Count > 0)
                    {
                        p.Add($"tab {(CatalogTab)t}: no named row for {string.Join(", ", bad.Select(e => e.ToString()))}");
                    }
                }
                // A killed creature shows its count at once.
                var killed = injected.FirstOrDefault(e => e.Kind == EntryKind.Creature && stats.m_enemyStats[0].ContainsKey(e.Key));
                if (killed != null)
                {
                    CompendiumWindow.OpenEntry(killed);
                    yield return null;
                    if (!CompendiumWindow.RenderedLines.Contains(string.Format(Inv, Labels.KilledFormat, 3)))
                    {
                        p.Add($"{killed}: details miss \"Killed: 3\"");
                    }
                }
                run.Detail = $"{injected.Count} entries marked the vanilla ways (materials, recipes, trophies, kills, known pieces, seen stations, placed "
                             + $"pieces): all named rows at the first open, counter \"{want}\"";
            }
        }
        finally
        {
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.memory (T29)

    private static bool SelectNth(int n)
    {
        var seen = 0;
        return CompendiumWindow.SelectWhere(_ => seen++ == n);
    }

    private static IEnumerator RunMemory()
    {
        var run = new UiRun();
        yield return run.Begin(MemoryName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        try
        {
            run.Force(sideButton: false);
            foreach (var prefab in new[] { "Wood", "Stone", "Raspberry", "Flint" })
            {
                var e = run.Cat.FindItemByPrefab(prefab);
                if (e != null)
                {
                    run.Player.m_knownMaterial.Add(e.Key);
                }
            }
            yield return TestKit.ShowInventory(gui);
            yield return TestKit.OpenByTab(gui, p, "open");
            var raven = TopTabs.RavenButton(gui);
            if (CompendiumWindow.IsOpen && !CompendiumWindow.Preparing && raven != null)
            {
                // Two tabs, one entry picked in each.
                CompendiumWindow.SelectTab((int)CatalogTab.Tools);
                yield return null;
                SelectNth(2);
                var a = CompendiumWindow.SelectedEntry;
                CompendiumWindow.SelectTab((int)CatalogTab.Trophies);
                yield return null;
                SelectNth(4);
                var b = CompendiumWindow.SelectedEntry;
                yield return null;
                if (a == null || b == null)
                {
                    p.Add("setup: no entry could be picked in the Tools or Trophies tab");
                }
                CompendiumWindow.Close(selectButton: false);
                yield return null;
                yield return null;
                raven.onClick.Invoke();
                yield return null;
                yield return null;
                if (!CompendiumWindow.IsOpen || CompendiumWindow.CurrentTab != (int)CatalogTab.Trophies || !ReferenceEquals(CompendiumWindow.SelectedEntry, b))
                {
                    p.Add($"reopened on tab {CompendiumWindow.CurrentTab} / {CompendiumWindow.SelectedEntry} (expected Trophies / {b})");
                }
                CompendiumWindow.SelectTab((int)CatalogTab.Tools);
                yield return null;
                if (!ReferenceEquals(CompendiumWindow.SelectedEntry, a))
                {
                    p.Add($"the Tools tab came back on {CompendiumWindow.SelectedEntry} (expected its own entry {a})");
                }

                // A search and a picked result.
                var field = CompendiumWindow.Search;
                if (field == null)
                {
                    p.Add("no search field");
                }
                else
                {
                    field.text = "wo";
                    CompendiumWindow.ApplySearchNow();
                    yield return null;
                    var hits = EntryRows();
                    SelectNth(hits.Count > 1 ? 1 : 0);
                    var picked = CompendiumWindow.SelectedEntry;
                    var rowCount = CompendiumWindow.Rows.Count;
                    if (hits.Count == 0 || picked == null)
                    {
                        p.Add("setup: the search \"wo\" found nothing (Wood is discovered)");
                    }
                    CompendiumWindow.Close(selectButton: false);
                    yield return null;
                    yield return null;
                    raven.onClick.Invoke();
                    yield return null;
                    yield return null;
                    var again = EntryRows();
                    if (!CompendiumWindow.IsOpen || field.text != "wo" || CompendiumWindow.Rows.Count != rowCount || again.Count != hits.Count
                        || !again.All(r => r.Known) || !ReferenceEquals(CompendiumWindow.SelectedEntry, picked))
                    {
                        p.Add($"after close and reopen the search is '{field.text}', {CompendiumWindow.Rows.Count} rows (was {rowCount}), selected "
                              + $"{CompendiumWindow.SelectedEntry} (expected \"wo\", the same rows and {picked})");
                    }
                    // Leaving the search: the tab still has its own entry.
                    CompendiumWindow.SelectTab((int)CatalogTab.Trophies);
                    yield return null;
                    if (field.text.Length != 0 || !ReferenceEquals(CompendiumWindow.SelectedEntry, b))
                    {
                        p.Add($"after the search, Trophies shows {CompendiumWindow.SelectedEntry} with search '{field.text}' (expected {b}, empty search)");
                    }
                }
                run.Detail = $"tab and entry come back after a close (Trophies / {b}), each tab keeps its own entry (Tools / {a}), the search \"wo\" "
                             + "and its picked result come back";
            }
        }
        finally
        {
            if (CompendiumWindow.IsOpen)
            {
                CompendiumWindow.SelectTab(0); // search text gone, first tab
            }
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.search (T16)

    private static IEnumerator RunSearch()
    {
        var run = new UiRun();
        yield return run.Begin(SearchName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        var cat = run.Cat;
        try
        {
            run.Force(sideButton: false);
            // Discovered: Wood (Materials) and the Workbench (Building); both prefab names hold "wo", in any language.
            var wood = cat.FindItemByPrefab("Wood");
            cat.PiecesByPrefab.TryGetValue("piece_workbench", out var bench);
            if (wood == null || bench == null)
            {
                p.Add("no Wood or Workbench entry");
            }
            else
            {
                run.Player.m_knownMaterial.Add(wood.Key);
                run.Player.m_knownRecipes.Add(bench.Key);
            }
            yield return TestKit.ShowInventory(gui);
            yield return TestKit.OpenByTab(gui, p, "open");
            var field = CompendiumWindow.Search;
            if (CompendiumWindow.IsOpen && !CompendiumWindow.Preparing && field != null && wood != null && bench != null)
            {
                const string query = "wo";
                var k = Knowledge.Take(cat, false);
                var hiddenMatches = cat.Entries.Count(e => !k.IsKnown(e) && (e.SearchKey.Contains(query) || e.PrefabSearchKey.Contains(query)));
                field.text = query;
                CompendiumWindow.ApplySearchNow();
                yield return null;
                var rows = CompendiumWindow.Rows;
                var entries = EntryRows();
                var headers = rows.Where(r => r.Kind == ListRowKind.Header).Select(r => r.Text).ToList();
                if (!entries.Any(r => ReferenceEquals(r.Entry, wood)) || !entries.Any(r => ReferenceEquals(r.Entry, bench)))
                {
                    p.Add($"search \"{query}\" misses Wood or the Workbench ({entries.Count} results)");
                }
                if (!headers.Contains(Tabs.LocalizedLabel(CatalogTab.Materials)) || !headers.Contains(Tabs.LocalizedLabel(CatalogTab.Building)))
                {
                    p.Add($"search results are not grouped by tab (headers: {Join(headers)})");
                }
                var bad = entries.Where(r => !r.Known || !k.IsKnown(r.Entry) || r.Text == Labels.Unknown
                                             || !(r.Entry.SearchKey.Contains(query) || r.Entry.PrefabSearchKey.Contains(query))).ToList();
                if (bad.Count > 0 || hiddenMatches == 0)
                {
                    p.Add($"search \"{query}\": {bad.Count} wrong or undiscovered result(s) (first {(bad.Count > 0 ? bad[0].Entry.ToString() : "none")}); "
                          + $"{hiddenMatches} undiscovered entries match and must stay out");
                }
                foreach (var row in CompendiumWindow.ListPool)
                {
                    if (row.Go.activeSelf && row.Bound >= 0 && row.Bound < rows.Count && rows[row.Bound].Kind == ListRowKind.Entry
                        && (row.Name.text == Labels.Unknown || row.Mark.gameObject.activeSelf))
                    {
                        p.Add("a search result row shows \"???\"");
                        break;
                    }
                }

                // Typed text applies by itself a moment after the last key.
                field.text = "woo";
                yield return new WaitForSecondsRealtime(0.4f);
                var narrowed = EntryRows();
                if (narrowed.Count == 0 || narrowed.Count > entries.Count
                    || narrowed.Any(r => !(r.Entry.SearchKey.Contains("woo") || r.Entry.PrefabSearchKey.Contains("woo"))))
                {
                    p.Add($"typing more did not narrow the results by itself ({entries.Count} -> {narrowed.Count})");
                }

                // Typing guard: while the field holds the keyboard the game's own key gates see "chat has focus".
                var controller = run.Player.GetComponent<PlayerController>();
                var position = run.Player.transform.position;
                FocusGuard.FieldUntilFrame = Time.frameCount + 1;
                var guarded = Chat.instance != null && Chat.instance.HasFocus();
                var walks = controller != null && controller.TakeInput();
                FocusGuard.Reset();
                if (!guarded || walks)
                {
                    p.Add($"with the search guard up: Chat.HasFocus {guarded} (expected true), movement input read {walks} (expected false)");
                }
                if (!PatchedByUs(typeof(Chat), nameof(Chat.HasFocus), prefix: false) || !PatchedByUs(typeof(Terminal), nameof(Terminal.TryRunCommand), prefix: true)
                    || !PatchedByUs(typeof(UIGamePad), nameof(UIGamePad.ButtonPressed), prefix: true))
                {
                    p.Add("a search guard patch is missing (Chat.HasFocus, Terminal.TryRunCommand or UIGamePad.ButtonPressed)");
                }
                // The real field: focus it, the window controller raises the guard; let go, one more frame, then down.
                field.ActivateInputField();
                var focused = false;
                for (var i = 0; i < 12 && !focused; i++)
                {
                    yield return null;
                    focused = field.isFocused && FocusGuard.Active;
                }
                if (focused)
                {
                    var whileTyping = Chat.instance != null && Chat.instance.HasFocus();
                    CompendiumWindow.DropSearchFocus();
                    yield return null;
                    yield return null;
                    yield return null;
                    yield return null;
                    if (!whileTyping || FocusGuard.Active || field.isFocused || !CompendiumWindow.IsOpen
                        || Vector3.Distance(position, run.Player.transform.position) > 0.3f)
                    {
                        p.Add($"search field focused: guard {whileTyping}; after letting go: guard still up {FocusGuard.Active}, focused {field.isFocused}, "
                              + $"window open {CompendiumWindow.IsOpen}, player moved {Vector3.Distance(position, run.Player.transform.position):F2} m");
                    }
                }
                run.Note($"search field took the keyboard in this run: {focused} (when false, the guard was checked through FocusGuard only)");
                run.Detail = $"\"{query}\": {entries.Count} results from {headers.Count} tabs, all discovered ({hiddenMatches} undiscovered matches stay "
                             + "out); typing narrows by itself; with the guard up the game sees chat focus and reads no movement";
            }
        }
        finally
        {
            FocusGuard.Reset();
            if (CompendiumWindow.IsOpen)
            {
                CompendiumWindow.DropSearchFocus();
                CompendiumWindow.SelectTab(0);
            }
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.links (T17)

    private static IEnumerator RunLinks()
    {
        var run = new UiRun();
        yield return run.Begin(LinksName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        var cat = run.Cat;
        try
        {
            run.Force(sideButton: false);
            var sword = cat.FindItemByPrefab("SwordBronze");
            var recipe = sword != null ? sword.Item.Recipes.FirstOrDefault(r => r != null && !r.m_noCraftOnlyUpgrade) : null;
            var parts = recipe != null
                ? recipe.m_resources.Where(q => q != null && q.m_resItem != null && !q.m_upgraderResource && q.GetAmount(1) > 0)
                    .Select(q => cat.ItemOf(q.m_resItem)).Where(e => e != null).Distinct().ToList()
                : new List<Entry>();
            if (sword == null || parts.Count < 2)
            {
                p.Add($"the Bronze Sword recipe has {parts.Count} listed ingredient(s): two are needed (one discovered, one not)");
            }
            else
            {
                var known = parts[0];
                var unknown = parts[1];
                var player = run.Player;
                player.m_knownMaterial.Add(sword.Key);
                player.m_knownMaterial.Add(known.Key);
                player.m_knownMaterial.Remove(unknown.Key);
                player.m_knownRecipes.Remove(unknown.Key);
                yield return TestKit.ShowInventory(gui);
                yield return TestKit.OpenByTab(gui, p, "open");
                if (CompendiumWindow.IsOpen && !CompendiumWindow.Preparing)
                {
                    var k = CompendiumWindow.CurrentKnowledge;
                    if (k == null || !k.IsKnown(known) || k.IsKnown(unknown))
                    {
                        p.Add($"setup: {known} discovered {k != null && k.IsKnown(known)}, {unknown} discovered {k != null && k.IsKnown(unknown)}");
                    }
                    CompendiumWindow.OpenEntry(sword);
                    yield return null;
                    yield return null;
                    RowView link = null;
                    RowView dead = null;
                    for (var i = 0; i < CompendiumWindow.DetailRowsShown && i < CompendiumWindow.DetailRowViews.Count; i++)
                    {
                        var row = CompendiumWindow.DetailRowViews[i];
                        if (link == null && ReferenceEquals(row.Target, known))
                        {
                            link = row;
                        }
                        if (dead == null && row.Target == null && row.Name.text.StartsWith(Labels.Unknown, StringComparison.Ordinal) && row.Mark.gameObject.activeSelf)
                        {
                            dead = row;
                        }
                    }
                    if (!ReferenceEquals(CompendiumWindow.SelectedEntry, sword) || link == null || dead == null)
                    {
                        p.Add($"Bronze Sword details: row of the discovered ingredient {(link != null ? "found" : "missing")}, \"???\" row {(dead != null ? "found" : "missing")}");
                    }
                    else
                    {
                        // "???" reference: not a button, a click on it changes nothing.
                        if (dead.Button != null && dead.Button.enabled)
                        {
                            p.Add("the \"???\" ingredient row is clickable");
                        }
                        TestKit.ClickObject(dead.Go, PointerEventData.InputButton.Left);
                        yield return null;
                        CompendiumWindow.OpenEntry(unknown);
                        yield return null;
                        if (!ReferenceEquals(CompendiumWindow.SelectedEntry, sword))
                        {
                            p.Add($"a click on the \"???\" ingredient opened {CompendiumWindow.SelectedEntry}");
                        }
                        // Discovered ingredient: a click opens its entry in its own tab.
                        if (link.Button == null || !link.Button.enabled || link.Name.text.IndexOf(Names.StripTags(known.DisplayName), StringComparison.Ordinal) < 0)
                        {
                            p.Add($"the discovered ingredient row '{link.Name.text}' is not a link");
                        }
                        TestKit.ClickObject(link.Go, PointerEventData.InputButton.Left);
                        yield return null;
                        yield return null;
                        var view = CompendiumWindow.ShownDetails;
                        if (!ReferenceEquals(CompendiumWindow.SelectedEntry, known) || CompendiumWindow.CurrentTab != (int)known.Tab
                            || view == null || view.Title != known.DisplayName)
                        {
                            p.Add($"a click on the discovered ingredient shows {CompendiumWindow.SelectedEntry} in tab {CompendiumWindow.CurrentTab} "
                                  + $"(expected {known} in tab {(int)known.Tab})");
                        }
                        run.Detail = $"in the Bronze Sword's details a click on {known.PrefabName} opened its entry (tab {known.Tab}); the \"???\" row of "
                                     + $"{unknown.PrefabName} is not clickable and a click on it changed nothing";
                    }
                }
            }
        }
        finally
        {
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.settings (T18)

    private static IEnumerator RunSettings()
    {
        var run = new UiRun();
        yield return run.Begin(SettingsName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        var cat = run.Cat;
        try
        {
            run.Force(sideButton: false);
            var wood = cat.FindItemByPrefab("Wood");
            if (wood != null)
            {
                run.Player.m_knownMaterial.Add(wood.Key);
            }
            yield return TestKit.ShowInventory(gui);
            yield return TestKit.OpenByTab(gui, p, "open");
            if (CompendiumWindow.IsOpen && !CompendiumWindow.Preparing)
            {
                CompendiumWindow.SelectTab((int)CatalogTab.Materials);
                yield return null;
                var before = EntryRows();
                var known = before.Count(r => r.Known);
                var unknown = before.Count - known;
                var hiddenEntry = before.FirstOrDefault(r => !r.Known).Entry;
                if (known == 0 || unknown == 0 || hiddenEntry == null)
                {
                    p.Add($"setup: the Materials tab has {known} discovered and {unknown} undiscovered rows");
                }

                // ShowUndiscovered off: at once only discovered rows.
                Plugin.SetTestDisplay(new Plugin.DisplayOverride { ShowUndiscovered = false, RevealAll = false });
                var rows = EntryRows();
                if (!CompendiumWindow.IsOpen || rows.Count != known || rows.Any(r => !r.Known))
                {
                    p.Add($"ShowUndiscovered off: {rows.Count} rows, {rows.Count(r => !r.Known)} undiscovered (expected the {known} discovered ones, at once)");
                }
                yield return null;
                if (CompendiumWindow.ListPool.Any(r => r.Go.activeSelf && r.Bound >= 0 && (r.Name.text == Labels.Unknown || r.Mark.gameObject.activeSelf)))
                {
                    p.Add("ShowUndiscovered off: a \"???\" row is still on screen");
                }

                // RevealAll on: every entry and its details, at once.
                Plugin.SetTestDisplay(new Plugin.DisplayOverride { ShowUndiscovered = true, RevealAll = true });
                rows = EntryRows();
                var all = cat.CountOf(CatalogTab.Materials);
                var counter = CompendiumWindow.CounterText != null ? CompendiumWindow.CounterText.text : "";
                var wantCounter = string.Format(Labels.DiscoveredFormat, cat.Entries.Count, cat.Entries.Count);
                if (rows.Count != all || rows.Any(r => !r.Known || r.Text == Labels.Unknown) || counter != wantCounter)
                {
                    p.Add($"RevealAll on: {rows.Count} rows of {all}, {rows.Count(r => !r.Known)} still \"???\", counter '{counter}' (expected \"{wantCounter}\")");
                }
                if (hiddenEntry != null)
                {
                    CompendiumWindow.OpenEntry(hiddenEntry);
                    yield return null;
                    var view = CompendiumWindow.ShownDetails;
                    if (view == null || !view.Known || view.Title != hiddenEntry.DisplayName || view.Lines.Count == 0 || CompendiumWindow.HeroShowsMark)
                    {
                        p.Add($"RevealAll on: details of the undiscovered {hiddenEntry} show '{(view != null ? view.Title : "nothing")}'");
                    }
                }

                // Back to the defaults: undiscovered rows are "???" again, and so are the details that were open.
                Plugin.SetTestDisplay(new Plugin.DisplayOverride { ShowUndiscovered = true, RevealAll = false });
                rows = EntryRows();
                var shown = CompendiumWindow.ShownDetails;
                if (rows.Count(r => r.Known) != known || rows.Count - rows.Count(r => r.Known) != unknown)
                {
                    p.Add($"defaults again: {rows.Count(r => r.Known)} discovered and {rows.Count - rows.Count(r => r.Known)} undiscovered rows (were {known} / {unknown})");
                }
                if (shown != null && hiddenEntry != null && ReferenceEquals(shown.Entry, hiddenEntry) && (shown.Known || shown.Title != Labels.Unknown))
                {
                    p.Add($"RevealAll off again: the details still name the undiscovered {hiddenEntry}");
                }
                if (CompendiumWindow.TopicText != null && hiddenEntry != null && CompendiumWindow.TopicText.text == hiddenEntry.DisplayName)
                {
                    p.Add("RevealAll off again: the details title still shows the undiscovered name");
                }
                run.Detail = $"Materials: {known} discovered + {unknown} \"???\" rows; ShowUndiscovered off = the {known} at once; RevealAll on = all {all} "
                             + $"named, counter \"{wantCounter}\", details shown; defaults = \"???\" again";
            }
        }
        finally
        {
            Plugin.SetTestDisplay(null);
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.pad (T15, T26, T30, T37)

    private static IEnumerator RunPad()
    {
        var run = new UiRun();
        yield return run.Begin(PadName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        var hints = KeyHints.instance;
        var hintsOn = hints != null && hints.m_keyHintsEnabled;
        try
        {
            run.Force(sideButton: true);
            yield return TestKit.ShowInventory(gui);
            var raven = TopTabs.RavenButton(gui);
            var groups = gui.m_uiGroups;
            if (raven == null || groups == null || groups.Length <= 3)
            {
                p.Add("no Valheim Compendium button or inventory focus groups");
                run.Report();
                yield break;
            }

            // T30 / T15: View/Select reaches us only with the side panel focused.
            gui.SetActiveGroup(groups[SideButton.SidePanelGroup], playSound: false);
            yield return null;
            yield return null;
            yield return null;
            var pad = SideButton.Pad;
            if (pad == null || pad.m_zinputKey != SideButton.PadKey)
            {
                p.Add("the Encyclopedia side button has no View/Select shortcut");
            }
            else
            {
                if (!SideButton.PadAllowed() || !pad.IsInteractive())
                {
                    p.Add($"side panel focused: View/Select allowed {SideButton.PadAllowed()}, shortcut live {pad.IsInteractive()} (expected both)");
                }
                gui.SetActiveGroup(groups[1], playSound: false);
                var onGrid = SideButton.PadAllowed();
                gui.SetActiveGroup(groups[3], playSound: false);
                var onCrafting = SideButton.PadAllowed();
                gui.SetActiveGroup(groups[SideButton.SidePanelGroup], playSound: false);
                if (onGrid || onCrafting)
                {
                    p.Add($"View/Select allowed with the player grid ({onGrid}) or the crafting panel ({onCrafting}) focused");
                }
                if (!PatchedByUs(typeof(UIGamePad), nameof(UIGamePad.ButtonPressed), prefix: true))
                {
                    p.Add("no Encyclopedia gate on UIGamePad.ButtonPressed");
                }
                // What a View/Select press does once allowed (UIGamePad.Update: the button's OnSubmit).
                yield return null;
                if (pad.m_button != null)
                {
                    pad.m_button.OnSubmit(null);
                }
                yield return null;
                yield return null;
                if (!CompendiumWindow.IsOpen)
                {
                    p.Add("the View/Select action on the side button did not open the Encyclopedia");
                }
                else if (SideButton.PadAllowed())
                {
                    p.Add("View/Select still allowed while the Encyclopedia is open");
                }
                CompendiumWindow.Close(selectButton: false);
                yield return null;
            }

            // T30: D-pad order along the row.
            var controls = SideControls(raven.transform.parent);
            var order = controls.Select(c => ClickMethod(c)).ToList();
            var wantOrder = new[] { "OnOpenTexts", "ours", "OnOpenSkills", "OnOpenTrophies", "OnOpenAchievements", "toggle" };
            if (!order.SequenceEqual(wantOrder))
            {
                p.Add($"side row order is [{string.Join(", ", order)}] (expected Valheim Compendium, Encyclopedia, Skills, Trophies, Achievements, PvP)");
            }
            for (var i = 1; i < controls.Count; i++)
            {
                var left = controls[i - 1].GetComponentInChildren<Selectable>();
                var right = controls[i].GetComponentInChildren<Selectable>();
                var toRight = left.FindSelectableOnRight();
                var toLeft = right.FindSelectableOnLeft();
                if (!ReferenceEquals(toRight, right) || !ReferenceEquals(toLeft, left))
                {
                    p.Add($"D-pad: right of '{left.name}' is '{(toRight != null ? toRight.name : "nothing")}' (expected '{right.name}'), left of '{right.name}' is "
                          + $"'{(toLeft != null ? toLeft.name : "nothing")}' (expected '{left.name}')");
                }
            }

            // T26: inventory closed: never ours.
            gui.Hide();
            yield return new WaitForSecondsRealtime(0.6f);
            if (SideButton.PadAllowed() || CompendiumWindow.IsOpen)
            {
                p.Add($"inventory closed: View/Select allowed {SideButton.PadAllowed()}, Encyclopedia open {CompendiumWindow.IsOpen}");
            }
            if (SideButton.Pad != null && SideButton.Pad.m_button != null)
            {
                SideButton.Pad.m_button.OnSubmit(null); // even a stray press of the button itself opens nothing while hidden
                yield return null;
                if (CompendiumWindow.IsOpen)
                {
                    p.Add("inventory closed: the side button's action opened the Encyclopedia");
                    CompendiumWindow.Close(selectButton: false);
                }
            }

            // T15: in the window.
            yield return TestKit.ShowInventory(gui);
            TopTabs.SetLastForTest(TopTabs.Choice.Texts);
            yield return TestKit.OpenByTab(gui, p, "open");
            if (CompendiumWindow.IsOpen && !CompendiumWindow.Preparing)
            {
                // D-pad / stick: next and previous entry, headers skipped, stops at the ends.
                CompendiumWindow.SelectTab((int)CatalogTab.Weapons);
                yield return null;
                var rows = CompendiumWindow.Rows;
                var entryRows = EntryRows();
                var firstEntry = entryRows.FirstOrDefault().Entry;
                var lastEntry = entryRows.LastOrDefault().Entry;
                var moves = 0;
                var headersPassed = 0;
                // Each tab remember its own entry until logout (T29), and tests before me picked one here: me walk from
                // the first row myself (a row click run the same Select), never trust where the tab opened.
                // Bottom end first: on the last entry, down must not move.
                if (lastEntry == null || !CompendiumWindow.SelectWhere(r => ReferenceEquals(r.Entry, lastEntry))
                    || CompendiumWindow.MoveSelection(1) || !ReferenceEquals(CompendiumWindow.SelectedEntry, lastEntry))
                {
                    p.Add($"on the last entry of the Weapons tab, moving down moved (selection now {CompendiumWindow.SelectedEntry})");
                }
                // Top end: on the first entry, up must not move.
                if (firstEntry == null || !CompendiumWindow.SelectWhere(r => ReferenceEquals(r.Entry, firstEntry))
                    || CompendiumWindow.MoveSelection(-1) || !ReferenceEquals(CompendiumWindow.SelectedEntry, firstEntry))
                {
                    p.Add($"on the first entry of the Weapons tab, moving up moved (selection now {CompendiumWindow.SelectedEntry})");
                }
                var last = CompendiumWindow.SelectedEntry;
                var lastIndex = -1;
                for (var i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Kind == ListRowKind.Entry && ReferenceEquals(rows[i].Entry, last))
                    {
                        lastIndex = i;
                        break;
                    }
                }
                // Walk down until some rows and at least one group header are behind me (first group may be long).
                while (moves < rows.Count && (moves < 20 || headersPassed == 0) && CompendiumWindow.MoveSelection(1))
                {
                    moves++;
                    var now = CompendiumWindow.SelectedEntry;
                    var index = -1;
                    for (var i = 0; i < rows.Count; i++)
                    {
                        if (rows[i].Kind == ListRowKind.Entry && ReferenceEquals(rows[i].Entry, now))
                        {
                            index = i;
                            break;
                        }
                    }
                    if (now == null || index <= lastIndex)
                    {
                        p.Add($"move down {moves}: selection went from row {lastIndex} to row {index}");
                        break;
                    }
                    for (var i = lastIndex + 1; i < index; i++)
                    {
                        headersPassed += rows[i].Kind == ListRowKind.Header ? 1 : 0;
                        if (rows[i].Kind == ListRowKind.Entry)
                        {
                            p.Add($"move down {moves}: entry row {i} was skipped");
                        }
                    }
                    lastIndex = index;
                }
                if (moves < 5 || headersPassed == 0)
                {
                    p.Add($"moving down the Weapons tab: {moves} moves, {headersPassed} header rows skipped (expected some of each)");
                }
                if (!CompendiumWindow.MoveSelection(-1))
                {
                    p.Add("moving up did not move");
                }
                // LB / RB: next and previous category tab, wrapping.
                CompendiumWindow.SelectTab(0);
                var visited = new List<int>();
                for (var i = 0; i < Tabs.Count; i++)
                {
                    CompendiumWindow.CycleTab(1);
                    visited.Add(CompendiumWindow.CurrentTab);
                }
                CompendiumWindow.CycleTab(-1);
                if (!visited.SequenceEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 0 }) || CompendiumWindow.CurrentTab != Tabs.Count - 1)
                {
                    p.Add($"tab cycling visited [{string.Join(",", visited)}], then back to {CompendiumWindow.CurrentTab} (expected 1..7, 0, then 7)");
                }
                yield return null;
                yield return null;

                // Inventory inert, no glyph, key hints off while open and back after.
                if (gui.m_inventoryGroup != null && gui.m_inventoryGroup.IsActive)
                {
                    p.Add("the inventory's own focus group is active while the Encyclopedia is open");
                }
                if (gui.ActiveGroup != SideButton.SidePanelGroup)
                {
                    p.Add($"active inventory group is {gui.ActiveGroup} while the Encyclopedia is open (crafting tabs would read LT / RT in group 3)");
                }
                var pads = CompendiumWindow.Root.GetComponentsInChildren<UIGamePad>(true).Length
                           + CompendiumWindow.Root.GetComponentsInChildren<UIInputHint>(true).Length;
                if (pads > 0)
                {
                    p.Add($"{pads} controller shortcut or glyph component(s) inside the window");
                }
                if (hints == null)
                {
                    p.Add("no KeyHints object");
                }
                else
                {
                    hints.m_keyHintsEnabled = true;
                    yield return null;
                    yield return null;
                    var whileOpen = hints.m_inventoryHints.activeSelf || hints.m_inventoryWithContainerHints.activeSelf || hints.m_combatHints.activeSelf;
                    CompendiumWindow.Close(selectButton: false); // as a mouse click on Close does
                    yield return null;
                    yield return null;
                    var afterClose = hints.m_inventoryHints.activeSelf;
                    if (whileOpen || !afterClose)
                    {
                        p.Add($"key hints: shown while the Encyclopedia is open {whileOpen} (expected hidden), back after it closed {afterClose} (expected shown)");
                    }
                }
            }

            // T37: the two top tabs never touch the crafting panel's Craft / Upgrade tabs, and no shortcut sits on LT / RT.
            var craft = gui.m_tabCraft != null && gui.m_tabCraft.interactable;
            var upgrade = gui.m_tabUpgrade != null && gui.m_tabUpgrade.interactable;
            if (!TopTabs.VanillaShown(gui))
            {
                raven.onClick.Invoke();
                yield return null;
                yield return null;
            }
            var triggerPads = gui.m_textsDialog.GetComponentsInChildren<UIGamePad>(true)
                .Count(x => x.m_zinputKey == TopTabs.LeftKey || x.m_zinputKey == TopTabs.RightKey);
            TopTabs.Select(TopTabs.Choice.Encyclopedia);
            yield return null;
            yield return null;
            var group = gui.ActiveGroup;
            TopTabs.Select(TopTabs.Choice.Encyclopedia);
            TopTabs.Select(TopTabs.Choice.Texts);
            yield return null;
            TopTabs.Select(TopTabs.Choice.Texts);
            yield return null;
            if (triggerPads > 0 || craft != (gui.m_tabCraft != null && gui.m_tabCraft.interactable) || upgrade != (gui.m_tabUpgrade != null && gui.m_tabUpgrade.interactable)
                || group != SideButton.SidePanelGroup || gui.ActiveGroup != SideButton.SidePanelGroup)
            {
                p.Add($"top tab switches: {triggerPads} LT/RT shortcut(s) on the dialog, Craft tab changed {craft != (gui.m_tabCraft != null && gui.m_tabCraft.interactable)}, "
                      + $"Upgrade tab changed {upgrade != (gui.m_tabUpgrade != null && gui.m_tabUpgrade.interactable)}, active group {group} / {gui.ActiveGroup}");
            }
            run.Detail = "View/Select opens the Encyclopedia only with the side panel focused (not on the player grid, the crafting panel, with the "
                         + "inventory closed or the window open); D-pad order Valheim Compendium, Encyclopedia, Skills, Trophies, Achievements, PvP; "
                         + "in the window: selection moves row by row over headers and stops at both ends, tabs cycle and wrap, inventory inert, "
                         + "no glyph, key hints hidden and back; "
                         + "top tab switches leave the Craft / Upgrade tabs alone";
        }
        finally
        {
            if (hints != null)
            {
                hints.m_keyHintsEnabled = hintsOn;
            }
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.close (T04, T36)

    private static IEnumerator RavenReopens(UiRun run, Button raven, string after)
    {
        raven.onClick.Invoke();
        yield return null;
        yield return null;
        if (!CompendiumWindow.IsOpen || TopTabs.VanillaShown(run.Gui))
        {
            run.Problems.Add($"raven after {after}: Encyclopedia open {CompendiumWindow.IsOpen}, Valheim Compendium shown {TopTabs.VanillaShown(run.Gui)} "
                             + $"(expected the remembered Encyclopedia, remembered: {TopTabs.Last})");
            if (!CompendiumWindow.IsOpen)
            {
                TopTabs.Select(TopTabs.Choice.Encyclopedia);
                yield return null;
                yield return null;
            }
        }
    }

    private static IEnumerator RunClose()
    {
        var run = new UiRun();
        yield return run.Begin(CloseName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        var player = run.Player;
        try
        {
            run.Force(sideButton: false);
            yield return TestKit.ShowInventory(gui);
            yield return TestKit.OpenByTab(gui, p, "open");
            var raven = TopTabs.RavenButton(gui);
            if (CompendiumWindow.IsOpen && raven != null)
            {
                // Close button (a real click at its place).
                var close = CompendiumWindow.CloseButton;
                if (close == null)
                {
                    p.Add("the window has no visible Close button");
                }
                else
                {
                    var hit = TestKit.Click(TestKit.Center((RectTransform)close.transform), PointerEventData.InputButton.Left);
                    yield return null;
                    yield return null;
                    if (CompendiumWindow.IsOpen || !TestKit.InventoryShown(gui) || TopTabs.VanillaShown(gui))
                    {
                        p.Add($"Close button (click landed on '{hit}'): Encyclopedia open {CompendiumWindow.IsOpen}, inventory shown {TestKit.InventoryShown(gui)}, "
                              + $"Valheim Compendium shown {TopTabs.VanillaShown(gui)} (expected only the Encyclopedia closed)");
                    }
                    yield return RavenReopens(run, raven, "the Close button");
                }

                // Left click outside the window (beside its wood panel, on the dimmed screen).
                if (!PointOutside(WindowFrame(), out var outside))
                {
                    p.Add("test setup: no screen point beside the window's wood panel");
                }
                else
                {
                    var hit = TestKit.Click(outside, PointerEventData.InputButton.Left);
                    yield return null;
                    yield return null;
                    if (CompendiumWindow.IsOpen || !TestKit.InventoryShown(gui) || TopTabs.VanillaShown(gui) || gui.m_dragGo != null)
                    {
                        p.Add($"click outside (landed on '{hit}'): Encyclopedia open {CompendiumWindow.IsOpen}, inventory shown {TestKit.InventoryShown(gui)}, "
                              + $"item on the cursor {gui.m_dragGo != null}");
                    }
                    yield return RavenReopens(run, raven, "a click outside");
                }

                // Esc / B (what the key runs once the game's own key gate passed): only the window, inventory stays.
                CompendiumWindow.CloseFromKey(gui);
                var frames = gui.m_shownFrames;
                yield return null;
                yield return null;
                yield return null;
                if (CompendiumWindow.IsOpen || frames != 0 || !TestKit.InventoryShown(gui))
                {
                    p.Add($"Esc path: Encyclopedia open {CompendiumWindow.IsOpen}, shown-frames {frames}, inventory shown {TestKit.InventoryShown(gui)}");
                }
                if (EscGuard.Problem != null || LogWatch.Count(LogWatch.EscChanged) > 0)
                {
                    p.Add($"the Esc gate check failed ({EscGuard.Problem}); \"InventoryGui.Update changed\" warnings: {LogWatch.Count(LogWatch.EscChanged)}");
                }
                yield return RavenReopens(run, raven, "Esc");

                // Tab / E / Y (InventoryGui.Hide): both close; the raven still reopens the Encyclopedia.
                gui.Hide();
                yield return new WaitForSecondsRealtime(0.6f);
                if (CompendiumWindow.IsOpen || TestKit.InventoryShown(gui))
                {
                    p.Add($"Tab path: Encyclopedia open {CompendiumWindow.IsOpen}, inventory shown {TestKit.InventoryShown(gui)} (expected both closed)");
                }
                yield return TestKit.ShowInventory(gui);
                yield return RavenReopens(run, raven, "Tab");

                // Teleport: the game hides the inventory, the Encyclopedia goes with it.
                var started = player.TeleportTo(player.transform.position, player.transform.rotation, distantTeleport: false);
                if (!started)
                {
                    p.Add("test setup: the teleport did not start");
                }
                else
                {
                    for (var i = 0; i < 30 && (CompendiumWindow.IsOpen || TestKit.InventoryShown(gui)); i++)
                    {
                        yield return null;
                    }
                    if (CompendiumWindow.IsOpen || TestKit.InventoryShown(gui) || !player.IsTeleporting())
                    {
                        p.Add($"teleport: Encyclopedia open {CompendiumWindow.IsOpen}, inventory shown {TestKit.InventoryShown(gui)}, teleporting {player.IsTeleporting()}");
                    }
                    var start = Time.realtimeSinceStartup;
                    while (player.IsTeleporting() && Time.realtimeSinceStartup - start < 20f)
                    {
                        yield return null;
                    }
                    if (player.IsTeleporting())
                    {
                        p.Add("the teleport did not end within 20 s");
                    }
                }

                // Texts picked last: the raven reopens Texts.
                yield return TestKit.ShowInventory(gui);
                raven.onClick.Invoke();
                yield return null;
                yield return null;
                if (CompendiumWindow.IsOpen)
                {
                    TopTabs.Select(TopTabs.Choice.Texts);
                    yield return null;
                    yield return null;
                }
                gui.m_textsDialog.OnClose();
                yield return null;
                raven.onClick.Invoke();
                yield return null;
                yield return null;
                if (CompendiumWindow.IsOpen || !TopTabs.VanillaShown(gui))
                {
                    p.Add($"raven after Texts was picked: Encyclopedia open {CompendiumWindow.IsOpen}, Valheim Compendium shown {TopTabs.VanillaShown(gui)}");
                }
                run.Detail = "Close button and a click outside close only the Encyclopedia; the Esc path too (inventory stays); Tab path and a teleport "
                             + "close both; after each close the raven reopens the Encyclopedia; after Texts was picked it reopens Texts";
            }
        }
        finally
        {
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.mouse (T27)

    private static IEnumerator RunMouse()
    {
        var run = new UiRun();
        yield return run.Begin(MouseName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        var player = run.Player;
        string token = null;
        var given = 5;
        try
        {
            run.Force(sideButton: false);
            yield return TestKit.ShowInventory(gui);
            var inventory = player.GetInventory();
            var elements = gui.m_playerGrid != null ? gui.m_playerGrid.m_elements : null;
            var raven = TopTabs.RavenButton(gui);
            var skills = SideButtonBy(gui, "OnOpenSkills");
            yield return TestKit.OpenByTab(gui, p, "open");
            if (!CompendiumWindow.IsOpen || elements == null || raven == null || skills == null)
            {
                p.Add("test setup: the window, the inventory slots, the raven or the Skills button is missing");
                run.Report();
                yield break;
            }
            // A visible slot: empty, on screen and not under the window's wood panel.
            var frame = WindowFrame();
            var width = inventory.GetWidth();
            var index = -1;
            for (var i = 0; i < elements.Count && index < 0; i++)
            {
                if (elements[i] == null || width <= 0 || inventory.GetItemAt(i % width, i / width) != null)
                {
                    continue;
                }
                var c = TestKit.Center((RectTransform)elements[i].transform);
                var grown = Rect.MinMaxRect(frame.xMin - 12f, frame.yMin - 12f, frame.xMax + 12f, frame.yMax + 12f);
                if (OnScreen(c) && (frame.width < 2f || !grown.Contains(c)))
                {
                    index = i;
                }
            }
            if (index < 0)
            {
                p.Add("test setup: no empty inventory slot is visible beside the window's wood panel");
                run.Report();
                yield break;
            }
            var slot = TestKit.Center((RectTransform)elements[index].transform);
            if (!TestKit.Under(TestKit.TopAt(slot), CompendiumWindow.Root) || !TestKit.Under(TestKit.TopAt(TestKit.Center((RectTransform)skills.transform)), CompendiumWindow.Root))
            {
                p.Add("the visible inventory slot or the Skills button is not covered by the window's click area");
            }
            token = TestKit.GiveItemAt(player, "Raspberry", given, new Vector2i(index % width, index / width));
            var item = token != null ? inventory.GetItemAt(index % width, index / width) : null;
            if (item == null || item.m_shared.m_name != token)
            {
                p.Add("could not put Raspberries in the visible inventory slot");
                run.Report();
                yield break;
            }
            var count = inventory.CountItems(token);
            // Empty stomach for the test (the run puts the foods back): a berry eaten earlier would refuse the last click.
            player.ClearFood();
            var foods = player.GetFoods().Count;
            yield return null;
            yield return null;
            // Right click on the food: nothing (not eaten, window stays).
            var hit = TestKit.Click(slot, PointerEventData.InputButton.Right);
            yield return null;
            yield return null;
            if (!CompendiumWindow.IsOpen || inventory.CountItems(token) != count || player.GetFoods().Count != foods)
            {
                p.Add($"right click on a food slot through the window (landed on '{hit}'): window open {CompendiumWindow.IsOpen}, "
                      + $"stack {count} -> {inventory.CountItems(token)}, foods eaten {foods} -> {player.GetFoods().Count}");
            }
            if (!CompendiumWindow.IsOpen)
            {
                yield return RavenReopens(run, raven, "a right click");
            }
            // Left click on the slot: only closes the Encyclopedia.
            hit = TestKit.Click(slot, PointerEventData.InputButton.Left);
            yield return null;
            yield return null;
            var still = inventory.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == token);
            if (CompendiumWindow.IsOpen || !TestKit.InventoryShown(gui) || gui.m_dragGo != null || (gui.m_splitDialog != null && gui.m_splitDialog.IsActive)
                || inventory.CountItems(token) != count || still == null || still.m_gridPos != item.m_gridPos)
            {
                p.Add($"left click on an inventory slot through the window (landed on '{hit}'): window open {CompendiumWindow.IsOpen}, item on the cursor "
                      + $"{gui.m_dragGo != null}, split dialog {gui.m_splitDialog != null && gui.m_splitDialog.IsActive}, stack {inventory.CountItems(token)}");
            }
            // Left click on a vanilla side button: only closes the Encyclopedia, its dialog does not open.
            yield return RavenReopens(run, raven, "a left click on a slot");
            hit = TestKit.Click(TestKit.Center((RectTransform)skills.transform), PointerEventData.InputButton.Left);
            yield return null;
            yield return null;
            if (CompendiumWindow.IsOpen || gui.IsSkillsPanelOpen || !TestKit.InventoryShown(gui))
            {
                p.Add($"left click on the Skills button through the window (landed on '{hit}'): window open {CompendiumWindow.IsOpen}, Skills open {gui.IsSkillsPanelOpen}");
            }
            if (gui.IsSkillsPanelOpen)
            {
                gui.m_skillsDialog.OnClose();
            }

            // After closing, everything works again: the same clicks now reach the game.
            yield return null;
            hit = TestKit.Click(TestKit.Center((RectTransform)skills.transform), PointerEventData.InputButton.Left);
            yield return null;
            yield return null;
            if (!gui.IsSkillsPanelOpen)
            {
                p.Add($"with the Encyclopedia closed, a click on the Skills button (landed on '{hit}') did not open Skills");
            }
            else
            {
                gui.m_skillsDialog.OnClose();
            }
            yield return null;
            yield return null;
            hit = TestKit.Click(slot, PointerEventData.InputButton.Right);
            for (var i = 0; i < 120 && (inventory.CountItems(token) != count - 1 || player.GetFoods().Count != foods + 1); i++)
            {
                yield return null;
            }
            if (inventory.CountItems(token) != count - 1 || player.GetFoods().Count != foods + 1)
            {
                p.Add($"with the Encyclopedia closed, a right click on the food (landed on '{hit}') did not eat it (stack {count} -> "
                      + $"{inventory.CountItems(token)}, foods {foods} -> {player.GetFoods().Count}): the click check above proves nothing");
            }
            else
            {
                given--;
            }
            run.Detail = "through the open window a right click on a food slot does nothing, a left click on a slot or on the Skills button only "
                         + "closes the Encyclopedia (nothing picked up, no split dialog, no Skills dialog); closed, the same clicks eat the food and "
                         + "open Skills";
        }
        finally
        {
            gui.SetupDragItem(null, null, 1);
            if (gui.IsSkillsPanelOpen)
            {
                gui.m_skillsDialog.OnClose();
            }
            TestKit.TakeItem(player, token, given);
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.sidebutton (T01, T02, T36)

    // Our button in the row: inside the panel background, no overlap, on screen, evenly spaced. Null = fine.
    private static string RowProblem(InventoryGui gui)
    {
        var rt = SideButton.Rect;
        if (!SideButton.Exists || rt == null || !rt.gameObject.activeInHierarchy)
        {
            return "no Encyclopedia side button";
        }
        if (SideButton.CurrentPlacement != SideButton.Placement.Row)
        {
            return $"placement {SideButton.CurrentPlacement}, row: {SideRow.State}";
        }
        var problem = SideButton.FindProblem(gui);
        if (problem != null)
        {
            return "the button " + problem;
        }
        var controls = SideControls(rt.parent);
        var bg = SideButton.PanelBackground;
        var screen = new Rect(0f, 0f, Screen.width, Screen.height);
        var gaps = new List<float>();
        for (var i = 0; i < controls.Count; i++)
        {
            var r = UiUtil.WorldRect(controls[i]);
            if (bg != null && !UiUtil.Inside(r, UiUtil.WorldRect(bg), 1f))
            {
                return $"'{controls[i].name}' {UiUtil.Fmt(r)} is outside the panel {UiUtil.Fmt(UiUtil.WorldRect(bg))}";
            }
            if (!UiUtil.Inside(r, screen, 1f))
            {
                return $"'{controls[i].name}' {UiUtil.Fmt(r)} is off screen";
            }
            if (i > 0)
            {
                var prev = UiUtil.WorldRect(controls[i - 1]);
                if (UiUtil.Overlaps(prev, r, 0.5f))
                {
                    return $"'{controls[i - 1].name}' and '{controls[i].name}' overlap";
                }
                gaps.Add(r.center.x - prev.center.x);
            }
        }
        if (gaps.Count > 0 && gaps.Max() - gaps.Min() > 1.5f)
        {
            return $"controls not evenly spaced (gaps {string.Join(", ", gaps.Select(g => g.ToString("F1", Inv)))})";
        }
        return null;
    }

    private static IEnumerator RunSideButton()
    {
        var run = new UiRun();
        yield return run.Begin(SideButtonName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        try
        {
            run.Force(sideButton: false);
            yield return TestKit.ShowInventory(gui);
            var raven = TopTabs.RavenButton(gui);
            if (raven == null)
            {
                p.Add("the Valheim Compendium button (raven) was not found");
                run.Report();
                yield break;
            }
            // The setting turned on with the inventory open (the setting-changed handler, forced in memory).
            Plugin.SetTestSideButton(true);
            yield return null;
            yield return null;
            var problem = RowProblem(gui);
            if (problem != null)
            {
                p.Add($"SideButton on: {problem}");
                run.Report();
                yield break;
            }
            // In the row: right after the raven, right before Skills.
            var controls = SideControls(SideButton.Rect.parent);
            var at = controls.IndexOf(SideButton.Rect);
            var before = at > 0 ? ClickMethod(controls[at - 1]) : "";
            var after = at >= 0 && at + 1 < controls.Count ? ClickMethod(controls[at + 1]) : "";
            if (before != "OnOpenTexts" || after != "OnOpenSkills")
            {
                p.Add($"our button sits between '{before}' and '{after}' (expected the Valheim Compendium and Skills buttons)");
            }
            // Book icon, tinted like the button it was cloned from; tooltip.
            var icon = SideButton.Icon;
            var ravenIcon = raven.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0);
            if (icon == null || !icon.enabled || !icon.gameObject.activeInHierarchy || !ReferenceEquals(icon.sprite, BookIcon.Get()))
            {
                p.Add($"the button's icon is not the book ({(icon != null && icon.sprite != null ? icon.sprite.name : "none")})");
            }
            else if (ravenIcon != null && icon.color != ravenIcon.color)
            {
                p.Add($"the book icon's tint {icon.color} differs from the raven icon's {ravenIcon.color}");
            }
            var tip = SideButton.Tooltip;
            if (tip == null || tip.m_topic != Labels.ButtonTooltipTopic || tip.m_text != Labels.ButtonTooltipText || tip.m_tooltipPrefab == null)
            {
                p.Add($"tooltip: topic '{(tip != null ? tip.m_topic : "none")}', text '{(tip != null ? tip.m_text : "none")}' (expected \"{Labels.ButtonTooltipTopic}\" and its text)");
            }

            // A click on it opens the Encyclopedia and does not change what the raven reopens (Texts here).
            var hit = TestKit.Click(TestKit.Center(SideButton.Rect), PointerEventData.InputButton.Left);
            yield return null;
            yield return TestKit.WaitPrepared();
            yield return null;
            if (!CompendiumWindow.IsOpen || CompendiumWindow.Rows.Count == 0 || TopTabs.Last != TopTabs.Choice.Texts)
            {
                p.Add($"click on the button (landed on '{hit}'): Encyclopedia open {CompendiumWindow.IsOpen}, {CompendiumWindow.Rows.Count} rows, remembered tab {TopTabs.Last}");
            }
            CompendiumWindow.Close(selectButton: false);
            yield return null;
            raven.onClick.Invoke();
            yield return null;
            yield return null;
            if (CompendiumWindow.IsOpen || !TopTabs.VanillaShown(gui))
            {
                p.Add("after opening the Encyclopedia from the side button, the raven did not reopen Texts");
            }
            // And with Encyclopedia remembered: still Encyclopedia after a side-button open.
            TopTabs.Select(TopTabs.Choice.Encyclopedia);
            yield return null;
            yield return null;
            CompendiumWindow.Close(selectButton: false);
            yield return null;
            TestKit.Click(TestKit.Center(SideButton.Rect), PointerEventData.InputButton.Left);
            yield return null;
            yield return null;
            var openedAgain = CompendiumWindow.IsOpen;
            CompendiumWindow.Close(selectButton: false);
            yield return null;
            if (!openedAgain || TopTabs.Last != TopTabs.Choice.Encyclopedia)
            {
                p.Add($"side button with the Encyclopedia tab remembered: opened {openedAgain}, remembered tab now {TopTabs.Last}");
            }
            TopTabs.SetLastForTest(TopTabs.Choice.Texts);

            // The once-per-inventory placement check and its log lines.
            for (var i = 0; i < 240 && SideButton.LastCheck == "not run"; i++)
            {
                yield return null;
            }
            var placed = LogWatch.LastButtonPlaced;
            if (!SideButton.LastCheck.StartsWith("ok (Row", StringComparison.Ordinal) || LogWatch.Count(LogWatch.ButtonPlaced) < 1
                || placed.IndexOf("Row: ", StringComparison.Ordinal) < 0 || placed.IndexOf(" controls", StringComparison.Ordinal) < 0
                || LogWatch.Count(LogWatch.SidePanelDump) < 1)
            {
                p.Add($"placement check: '{SideButton.LastCheck}', \"Encyclopedia button placed\" lines {LogWatch.Count(LogWatch.ButtonPlaced)} (last: '{placed}'), "
                      + $"side panel dumps {LogWatch.Count(LogWatch.SidePanelDump)}");
            }

            // T02: other UI scales (in memory): still inside the panel, on screen, no overlap, even.
            var scales = new List<string> { $"{Screen.width}x{Screen.height} at the user's UI scale: ok" };
            foreach (var scale in new[] { 0.85f, 1.15f })
            {
                GuiScaler.SetScale(scale);
                for (var i = 0; i < 5; i++)
                {
                    yield return null;
                }
                SideButton.OnShow(gui);
                yield return null;
                problem = RowProblem(gui);
                scales.Add($"UI scale {scale.ToString("0.00", Inv)}: {problem ?? "ok"}");
                if (problem != null)
                {
                    p.Add($"UI scale {scale.ToString("0.00", Inv)}: {problem}");
                }
            }
            GuiScaler.LoadGuiScale();
            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }
            run.Note("placement: " + string.Join("; ", scales) + "; row: " + SideRow.State);
            run.Detail = "the button sits right after the Valheim Compendium button and before Skills, book icon with the raven's tint, tooltip "
                         + "\"Encyclopedia\"; a click opens the Encyclopedia and leaves the raven's remembered tab alone; placement check and log "
                         + $"lines there; placement fine at {Screen.width}x{Screen.height} and at UI scales 0.85 and 1.15";
        }
        finally
        {
            GuiScaler.LoadGuiScale();
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.sidecount (T01, T38)

    private static IEnumerator RunSideCount()
    {
        var run = new UiRun();
        yield return run.Begin(SideCountName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        try
        {
            run.Force(sideButton: false);
            yield return TestKit.ShowInventory(gui);
            var raven = TopTabs.RavenButton(gui);
            if (raven == null)
            {
                p.Add("the Valheim Compendium button (raven) was not found");
                run.Report();
                yield break;
            }
            var off = SideControls(raven.transform.parent);
            if (off.Count != 5 || SideButton.Exists)
            {
                p.Add($"SideButton off: {off.Count} side controls [{string.Join(", ", off.Select(c => c.name))}] (expected the game's five), our button exists {SideButton.Exists}");
            }
            Plugin.SetTestSideButton(true);
            // "At once" (T38): the setting-change handler itself make the button, no frame in between.
            if (!SideButton.Exists)
            {
                p.Add("SideButton on: no button in the same frame");
            }
            yield return null;
            yield return null;
            var on = SideControls(raven.transform.parent);
            if (on.Count != 6 || !SideRow.State.StartsWith("6 controls", StringComparison.Ordinal))
            {
                p.Add($"SideButton on: {on.Count} side controls, row '{SideRow.State}' (expected six)");
            }
            Plugin.SetTestSideButton(false);
            if (SideButton.Exists)
            {
                p.Add("SideButton off again: the button is still there in the same frame");
            }
            yield return null;
            yield return null;
            var again = SideControls(raven.transform.parent);
            if (again.Count != 5 || SideButton.Exists)
            {
                p.Add($"SideButton off again: {again.Count} side controls, our button exists {SideButton.Exists}");
            }
            run.Detail = $"five side controls without the button ({string.Join(", ", off.Select(c => c.name))}), six with it, five again";
        }
        finally
        {
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.sidelock (T03)

    private static IEnumerator RunSideLock()
    {
        var run = new UiRun();
        yield return run.Begin(SideLockName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        try
        {
            run.Force(sideButton: true);
            yield return TestKit.ShowInventory(gui);
            var raven = TopTabs.RavenButton(gui);
            if (raven == null || !SideButton.Exists || SideButton.Button == null)
            {
                p.Add("no Valheim Compendium button or no Encyclopedia side button");
                run.Report();
                yield break;
            }
            var done = new List<string>();
            var dialogs = new (string Name, Action Open, Func<bool> Shown, Action Close)[]
            {
                ("Valheim Compendium", () => raven.onClick.Invoke(), () => TopTabs.VanillaShown(gui), () => gui.m_textsDialog.OnClose()),
                ("Skills", () => gui.OnOpenSkills(), () => gui.IsSkillsPanelOpen, () => gui.m_skillsDialog.OnClose()),
                ("Trophies", () => gui.OnOpenTrophies(), () => gui.IsTrophisPanelOpen, () => gui.OnCloseTrophies()),
                ("Achievements", () => gui.OnOpenAchievements(), () => gui.IsAchievementsPanelOpen, () => gui.OnCloseAchievements()),
            };
            foreach (var d in dialogs)
            {
                d.Open();
                for (var i = 0; i < 4; i++)
                {
                    yield return null;
                }
                if (!d.Shown())
                {
                    p.Add($"test setup: the {d.Name} dialog did not open");
                    continue;
                }
                var center = TestKit.Center(SideButton.Rect);
                var top = TestKit.TopAt(center);
                var onOurs = top != null && top.transform.IsChildOf(SideButton.Rect);
                var clickable = SideButton.Button.IsInteractable();
                var ravenClickable = raven.IsInteractable();
                var padLive = SideButton.Pad != null && SideButton.Pad.IsInteractive();
                if (padLive)
                {
                    p.Add($"{d.Name} open: the View/Select shortcut of the Encyclopedia button is live");
                }
                if (onOurs && clickable)
                {
                    p.Add($"{d.Name} open: the Encyclopedia button is on top and clickable");
                }
                if (clickable != ravenClickable)
                {
                    p.Add($"{d.Name} open: the Encyclopedia button is clickable {clickable} but the game's own side button {ravenClickable} (expected the same lock)");
                }
                var hit = TestKit.Click(center, PointerEventData.InputButton.Left);
                yield return null;
                yield return null;
                if (CompendiumWindow.IsOpen)
                {
                    p.Add($"{d.Name} open: a click where the Encyclopedia button is (landed on '{hit}') opened the Encyclopedia");
                    CompendiumWindow.Close(selectButton: false);
                }
                var stillShown = d.Shown();
                if (d.Name == "Valheim Compendium" && stillShown)
                {
                    p.Add("Valheim Compendium open: a click where the Encyclopedia button is did not close it (its click-outside area)");
                }
                done.Add($"{d.Name}: click lands on '{(top != null ? top.name : "nothing")}', button clickable {clickable}, dialog {(stillShown ? "stays" : "closes")}");
                if (stillShown)
                {
                    d.Close();
                }
                for (var i = 0; i < 4; i++)
                {
                    yield return null;
                }
                // Dialog closed: one click opens ours.
                hit = TestKit.Click(TestKit.Center(SideButton.Rect), PointerEventData.InputButton.Left);
                yield return null;
                yield return null;
                if (!CompendiumWindow.IsOpen)
                {
                    p.Add($"after the {d.Name} dialog closed, one click on the Encyclopedia button (landed on '{hit}') did not open it");
                }
                CompendiumWindow.Close(selectButton: false);
                for (var i = 0; i < 3; i++)
                {
                    yield return null;
                }
            }
            run.Note(string.Join("; ", done));
            run.Detail = "with the Valheim Compendium, Skills, Trophies or Achievements open, a click where the Encyclopedia button is never opens it "
                         + "and its View/Select shortcut is dead, locked like the game's own side buttons; once the dialog is closed one click opens it";
        }
        finally
        {
            if (gui.IsSkillsPanelOpen)
            {
                gui.m_skillsDialog.OnClose();
            }
            if (gui.IsTrophisPanelOpen)
            {
                gui.OnCloseTrophies();
            }
            if (gui.IsAchievementsPanelOpen)
            {
                gui.OnCloseAchievements();
            }
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.buildclose (T25)

    private static IEnumerator WaitCatalogReady(List<string> problems, string when)
    {
        var start = Time.realtimeSinceStartup;
        while (CatalogService.Ready == null && Time.realtimeSinceStartup - start < TestKit.CatalogTimeout)
        {
            yield return null;
        }
        if (CatalogService.Ready == null)
        {
            problems.Add($"{when}: the catalog build did not finish within {TestKit.CatalogTimeout} s with the window closed");
        }
    }

    private static IEnumerator RunBuildClose()
    {
        var run = new UiRun();
        yield return run.Begin(BuildCloseName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        try
        {
            run.Force(sideButton: false);
            yield return TestKit.ShowInventory(gui);
            var raven = TopTabs.RavenButton(gui);
            if (raven == null)
            {
                p.Add("the Valheim Compendium button (raven) was not found");
                run.Report();
                yield break;
            }
            // Catalog there: opening twice builds nothing again.
            var built = LogWatch.Count(LogWatch.CatalogBuilt);
            yield return TestKit.OpenByTab(gui, p, "open 1");
            CompendiumWindow.Close(selectButton: false);
            yield return null;
            raven.onClick.Invoke();
            yield return null;
            yield return null;
            if (!CompendiumWindow.IsOpen || CompendiumWindow.Preparing || LogWatch.Count(LogWatch.CatalogBuilt) != built)
            {
                p.Add($"a second open: open {CompendiumWindow.IsOpen}, preparing {CompendiumWindow.Preparing}, new \"catalog built\" lines "
                      + $"{LogWatch.Count(LogWatch.CatalogBuilt) - built} (expected the same catalog, no new build)");
            }
            CompendiumWindow.Close(selectButton: false);
            yield return null;

            // Fresh catalog (feature off and on, in memory), window closed with Tab (InventoryGui.Hide) while "Preparing".
            Plugin.SetOffForTest(true);
            Plugin.SetOffForTest(false);
            yield return null;
            raven.onClick.Invoke();
            var preparing = CompendiumWindow.IsOpen && CompendiumWindow.Preparing;
            var building = CatalogService.IsBuilding;
            gui.Hide();
            if (!preparing || !building || CompendiumWindow.IsOpen)
            {
                p.Add($"Tab during the build: window was preparing {preparing}, build running {building}, window still open {CompendiumWindow.IsOpen}");
            }
            yield return WaitCatalogReady(p, "Tab during the build");
            yield return new WaitForSecondsRealtime(0.6f);
            if (LogWatch.Count(LogWatch.CatalogBuilt) != built + 1)
            {
                p.Add($"Tab during the build: {LogWatch.Count(LogWatch.CatalogBuilt) - built} \"catalog built\" line(s), expected 1");
            }
            yield return TestKit.ShowInventory(gui);
            raven.onClick.Invoke();
            yield return null;
            yield return null;
            if (!CompendiumWindow.IsOpen || CompendiumWindow.Preparing || CompendiumWindow.Rows.Count == 0)
            {
                p.Add($"reopened after Tab during the build: open {CompendiumWindow.IsOpen}, preparing {CompendiumWindow.Preparing}, {CompendiumWindow.Rows.Count} rows");
            }
            CompendiumWindow.Close(selectButton: false);
            yield return null;

            // Again, closed with the Esc path while "Preparing": the inventory stays, the build finishes, rows at once.
            Plugin.SetOffForTest(true);
            Plugin.SetOffForTest(false);
            yield return null;
            raven.onClick.Invoke();
            preparing = CompendiumWindow.IsOpen && CompendiumWindow.Preparing;
            building = CatalogService.IsBuilding;
            if (CompendiumWindow.IsOpen)
            {
                CompendiumWindow.CloseFromKey(gui);
            }
            if (!preparing || !building || CompendiumWindow.IsOpen)
            {
                p.Add($"Esc during the build: window was preparing {preparing}, build running {building}, window still open {CompendiumWindow.IsOpen}");
            }
            yield return WaitCatalogReady(p, "Esc during the build");
            if (!TestKit.InventoryShown(gui))
            {
                p.Add("Esc during the build closed the inventory too");
                yield return TestKit.ShowInventory(gui);
            }
            if (LogWatch.Count(LogWatch.CatalogBuilt) != built + 2)
            {
                p.Add($"Esc during the build: {LogWatch.Count(LogWatch.CatalogBuilt) - built} \"catalog built\" lines in total, expected 2");
            }
            raven.onClick.Invoke();
            yield return null;
            yield return null;
            if (!CompendiumWindow.IsOpen || CompendiumWindow.Preparing || CompendiumWindow.Rows.Count == 0)
            {
                p.Add($"reopened after Esc during the build: open {CompendiumWindow.IsOpen}, preparing {CompendiumWindow.Preparing}, {CompendiumWindow.Rows.Count} rows");
            }
            run.Detail = "two opens = one catalog; a fresh build with the window closed by the Tab path, then by the Esc path, while it said "
                         + "\"Preparing entries...\": each build still finished (one \"catalog built\" line each) and the next open listed rows at once";
        }
        finally
        {
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.cleanlog (T24)

    private static IEnumerator RunCleanLog()
    {
        var run = new UiRun();
        yield return run.Begin(CleanLogName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        try
        {
            run.Force(sideButton: false);
            // A normal session: open, every tab, an entry in each, a search, Texts and back, close.
            yield return TestKit.ShowInventory(gui);
            yield return TestKit.OpenByTab(gui, p, "open");
            if (CompendiumWindow.IsOpen)
            {
                for (var t = 0; t < Tabs.Count; t++)
                {
                    CompendiumWindow.SelectTab(t);
                    yield return null;
                    SelectNth(1);
                    yield return null;
                }
                if (CompendiumWindow.Search != null)
                {
                    CompendiumWindow.Search.text = "a";
                    CompendiumWindow.ApplySearchNow();
                    yield return null;
                }
                CompendiumWindow.SelectTab(0);
                TopTabs.Select(TopTabs.Choice.Texts);
                yield return null;
                yield return null;
                TopTabs.Select(TopTabs.Choice.Encyclopedia);
                yield return null;
                yield return null;
                CompendiumWindow.Close(selectButton: false);
            }
            gui.Hide();
            yield return new WaitForSecondsRealtime(0.5f);
            if (!LogWatch.Installed)
            {
                p.Add("the log watch is not installed");
            }
            if (LogWatch.Unexpected > 0)
            {
                p.Add($"{LogWatch.Unexpected} warning or error line(s) from the mod since it started: {LogWatch.UnexpectedText()}");
            }
            run.Note($"warning / error lines of the mod since it started: {LogWatch.Bad} in all, {LogWatch.Bad - LogWatch.Unexpected} made on purpose by "
                     + $"self tests, {LogWatch.Unexpected} others; \"catalog built\" lines {LogWatch.Count(LogWatch.CatalogBuilt)}");
            run.Detail = "no warning or error line from the mod since it started (and no uncaught exception of its code in Unity's log), "
                         + "after a session through every tab, entries, a search and both top tabs";
        }
        finally
        {
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.texts (T35)

    private static IEnumerator RunTexts()
    {
        var run = new UiRun();
        yield return run.Begin(TextsName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        const string label = "MC Encyclopedia test note";
        try
        {
            run.Force(sideButton: false);
            yield return TestKit.ShowInventory(gui);
            yield return TestKit.OpenByTab(gui, p, "open");
            if (CompendiumWindow.IsOpen && CompendiumWindow.TopTextsButton != null)
            {
                // A text learned while the Encyclopedia is shown (a rune stone read, a message).
                run.Player.AddKnownText(label, "Learned while the Encyclopedia was open.");
                CompendiumWindow.TopTextsButton.onClick.Invoke();
                yield return null;
                yield return null;
                var texts = gui.m_textsDialog.m_texts;
                var entry = texts != null ? texts.FirstOrDefault(t => t.m_topic == label) : null;
                if (CompendiumWindow.IsOpen || !TopTabs.VanillaShown(gui) || entry == null || entry.m_listElement == null || !entry.m_listElement.activeSelf)
                {
                    p.Add($"Texts tab after a text was learned: Valheim Compendium shown {TopTabs.VanillaShown(gui)}, the new text in its list {entry != null} "
                          + $"({(texts != null ? texts.Count : 0)} entries)");
                }
                if (TopTabs.VanillaTexts == null || TopTabs.VanillaTexts.interactable || TopTabs.VanillaEncyclopedia == null || !TopTabs.VanillaEncyclopedia.interactable)
                {
                    p.Add("back on the Valheim Compendium: Texts is not the current tab");
                }
                run.Detail = "a text learned while the Encyclopedia was open is in the Valheim Compendium's list after a click on the Texts tab";
            }
        }
        finally
        {
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.bug.refused-toggle (T39)

    // A window that could not be built on this inventory (played in memory, as a UI mod would cause), SideButton on,
    // then the feature off and on: the refusal must last until the next inventory screen. No button, no tabs, no new
    // warning. (Design: "side button stay away until the next one"; X07: at most one warning.)
    // REAL BUG, seen in the run: CompendiumWindow.Destroy -> ForgetObjects set _refused false while _sessionGui stay,
    // SideButton.Forget clear _hidden, so OnActivated -> SideButton.Sync build the button again and lay the row out
    // (TopTabs keep its own flag: tabs stay away). Test fail until the mod is fixed; nothing else is checked here.
    private static IEnumerator RunRefusedToggle()
    {
        var run = new UiRun();
        yield return run.Begin(RefusedToggleName);
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
            if (raven == null)
            {
                p.Add("the Valheim Compendium button (raven) was not found");
                run.Report();
                yield break;
            }
            var vanilla = SideControls(raven.transform.parent).Select(c => new KeyValuePair<RectTransform, Vector2>(c, c.anchoredPosition)).ToList();
            // What a failed build leaves: window refused, tabs and button hidden for this inventory screen.
            CompendiumWindow.SetRefusedForTest(gui, true);
            TopTabs.SetRefusedForTest(true);
            SideButton.HideForThisSession();
            Plugin.SetTestSideButton(true);
            yield return null;
            yield return null;
            if (SideButton.Exists || TestKit.FindUnder(gui, SideButton.ButtonName) != null)
            {
                p.Add("setup: a side button exists although the window is refused");
            }
            var bad = LogWatch.Bad;

            Plugin.SetOffForTest(true);
            yield return null;
            Plugin.SetOffForTest(false);
            yield return null;
            yield return null;

            if (!CompendiumWindow.RefusedFor(gui))
            {
                p.Add("after the feature went off and on, the window is no longer refused on this inventory screen");
            }
            if (!TopTabs.Refused)
            {
                p.Add("after the feature went off and on, the top tabs are no longer refused");
            }
            if (SideButton.Exists || TestKit.FindUnder(gui, SideButton.ButtonName) != null)
            {
                p.Add("after the feature went off and on, the Encyclopedia side button is back although its window cannot be built");
            }
            foreach (var c in vanilla)
            {
                if (c.Key != null && c.Key.anchoredPosition != c.Value)
                {
                    p.Add($"after the feature went off and on, side control '{c.Key.name}' is not where the game puts it");
                    break;
                }
            }
            raven.onClick.Invoke();
            yield return null;
            yield return null;
            if (!TopTabs.VanillaShown(gui) || TopTabs.VanillaStrip != null || CompendiumWindow.IsOpen)
            {
                p.Add($"Valheim Compendium after the toggle: shown {TopTabs.VanillaShown(gui)}, tabs on it {TopTabs.VanillaStrip != null}, Encyclopedia open {CompendiumWindow.IsOpen}");
            }
            if (LogWatch.Bad != bad || !LogWatch.Installed)
            {
                p.Add($"{LogWatch.Bad - bad} new warning or error line(s) after the toggle (log watch installed {LogWatch.Installed})");
            }
            run.Detail = "a refused window stays refused after the feature goes off and on: no side button, the game's side row, no tabs, no new warning";
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
#endif
}
