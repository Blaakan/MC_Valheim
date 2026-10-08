#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Debug build only. Me = self tests of Forge of Potential panel and rules, one small test per TESTING.md item:
//   forge.tab            IDOLS tab: label, place (left of UPGRADE), one row per idol stack below 3 stars, stars on rows
//   forge.cost           plain -> 1 star: panel text, 3 requirement slots, trophy icon going round, tooltip, timer, message
//   forge.steps          1 -> 2 -> 3 stars: same idol, same slot, costs, gone from the list at 3 stars
//   forge.missing        cost missing: grey row, red amounts, button off + why; full inventory: need space
//   forge.tooltip.text   idol tooltip words for every level with the default rules
//   forge.odds           Black metal axe + 3-star idol: exact text, button, starred slot, one log line per attempt
//   forge.failure        failure with a plain idol: 1 level lost, message, no material back; level 1 lose nothing
//   forge.success        success: level up, full durability, crafter, world level, taken off, mod data kept
//   forge.tiers.stone    Stone axe 5 -> 6 -> 7: Bronze idol from level 6; leather armor, Flint axe, level 4 again
//   forge.tiers.live     tier rules change while Forge open; level 40 = Bloodgold
//   forge.failure.rules  LevelsLost = 2, OnFailure = Destroy (vanilla break, refund, free slots)
//   forge.choice         which idol get spent: most stars, mouse click on the slot, IdolChoice = Lowest
//   forge.space          default rules: refinement start with a full inventory
//   forge.rules.live     chance and trophy cost change while game run; typo in a trophy list get logged
//   forge.rules.wiring   changed rule setting give new rules snapshot (me write no config value)
//   forge.timer.tabs     tab switched while timer run: started job still end, once
//   forge.timer.loss     cost, idol or item gone before timer end: nothing happen, nothing used
//   forge.rules.bad-material   unknown or empty tier metal: upgrade stay blocked (never free), log name the bad name
//   forge.tooltip.zero-cost    metal cost 0: tooltip show trophies only
//   forge.bug.bad-material-silent  (real bug, fail until mod fixed) unknown tier metal: panel and tooltip must say why
//   forge.bug.tooltip-zero-cost    (real bug, fail until mod fixed) trophy cost 0: no comma hanging in tooltip
internal static partial class SelfTests
{
    private const string TabName = "forge.tab";
    private const string CostName = "forge.cost";
    private const string StepsName = "forge.steps";
    private const string MissingName = "forge.missing";
    private const string TooltipTextName = "forge.tooltip.text";
    private const string OddsName = "forge.odds";
    private const string FailureName = "forge.failure";
    private const string SuccessName = "forge.success";
    private const string StoneName = "forge.tiers.stone";
    private const string TiersLiveName = "forge.tiers.live";
    private const string FailureRulesName = "forge.failure.rules";
    private const string ChoiceName = "forge.choice";
    private const string SpaceName = "forge.space";
    private const string RulesLiveName = "forge.rules.live";
    private const string WiringName = "forge.rules.wiring";
    private const string TimerTabsName = "forge.timer.tabs";
    private const string TimerLossName = "forge.timer.loss";
    private const string BadMaterialName = "forge.rules.bad-material";
    private const string ZeroCostName = "forge.tooltip.zero-cost";
    private const string BugBadMaterialName = "forge.bug.bad-material-silent";
    private const string BugZeroCostName = "forge.bug.tooltip-zero-cost";

    private const string WoodIdol = "Upgrader0Weapon";
    private const string BronzeIdol = "Upgrader1Weapon";
    private const string SilverIdol = "Upgrader3Weapon";
    private const string Marker = "MC.Test.Marker";

    private static readonly Color GreyRow = new Color(0.66f, 0.66f, 0.66f, 1f);
    private static readonly Color GreyIcon = new Color(1f, 0f, 1f, 0f);

    // Forced roll far above / below every chance me use here.
    private const float Fail = 0.99f;
    private const float Win = 0f;

    // "Upgrade at a Forge of Potential, Idols tab: <this part>" of idol tooltip (localized text). Null = no line.
    private static string CostLine(string tooltip)
    {
        const string head = "Idols tab: ";
        var at = tooltip.IndexOf(head, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }
        at += head.Length;
        var end = tooltip.IndexOf("</color>", at, StringComparison.Ordinal);
        return end < 0 ? tooltip.Substring(at) : tooltip.Substring(at, end - at);
    }

    private static string RowProblem(Rig rig, ItemDrop.ItemData item, int level)
    {
        var row = RowOf(rig.Gui, item);
        if (row < 0)
        {
            return "no row";
        }
        var pair = rig.Gui.m_availableRecipes[row];
        if (!IdolUpgrade.IsOurs(pair.Recipe))
        {
            return "row is not an idol upgrade";
        }
        var icon = pair.InterfaceElement.transform.Find("icon")?.GetComponent<Image>();
        var plain = item.m_shared.m_icons[0];
        var want = level > 0 ? StarIcons.Get(plain, level) : plain;
        if (icon == null || icon.sprite != want || StarIcons.IsStarSprite(icon.sprite) != (level > 0))
        {
            return $"row icon is {Describe(icon != null ? icon.sprite : null)}";
        }
        var quality = pair.InterfaceElement.transform.Find("QualityLevel");
        return quality != null && quality.gameObject.activeSelf ? "row shows a quality number" : null;
    }

    // Left mouse click on requirement slot, through event system (what real mouse do).
    private static bool ClickSlot(Rig rig, int slot)
    {
        var go = rig.Gui.m_recipeRequirementList[slot];
        var events = EventSystem.current;
        if (go == null || events == null)
        {
            return false;
        }
        var data = new PointerEventData(events) { button = PointerEventData.InputButton.Left };
        return ExecuteEvents.Execute(go, data, ExecuteEvents.pointerClickHandler);
    }

    private static string[] RecoverNames(Recipe recipe) =>
        recipe.m_resources.Where(q => q != null && q.m_recover && q.m_resItem != null)
            .Select(q => q.m_resItem.m_itemData.m_shared.m_name).Distinct().ToArray();

    private static int[] CountAll(Rig rig, string[] names) => names.Select(n => rig.Inv.CountItems(n, -1, false)).ToArray();

    // Stone axe (wooden idol tier): the item TESTING.md use. False = game data not what tests think.
    private static bool StoneAxeReady(Rig rig)
    {
        var own = OwnIdolOf("AxeStone");
        rig.C.Check(own == WoodIdol, $"the Stone axe's own idol must be {WoodIdol} in the game data, is '{own}'");
        if (own != WoodIdol)
        {
            return false;
        }
        rig.Teach("AxeStone");
        return true;
    }

    // ---------- forge.tab (T01) ----------

    private static IEnumerator RunTab()
    {
        var rig = new Rig(TabName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            var stacks = new[]
            {
                new KeyValuePair<ItemDrop.ItemData, int>(rig.Give(SilverIdol, 2, 1), 0),
                new KeyValuePair<ItemDrop.ItemData, int>(rig.Give(SilverIdol, 1, 2), 1),
                new KeyValuePair<ItemDrop.ItemData, int>(rig.Give(SilverIdol, 1, 3), 2),
                new KeyValuePair<ItemDrop.ItemData, int>(rig.Give("Upgrader0Armor", 1, 2), 1),
            };
            var full = rig.Give(SilverIdol, 1, 4);
            yield return rig.OpenForge();
            var gui = rig.Gui;
            var tab = rig.IdolsTabButton();
            c.Check(tab != null && tab.gameObject.activeSelf, "an IDOLS tab shows at the Forge of Potential");
            if (tab == null)
            {
                c.Report();
                yield break;
            }
            c.Check(!IdolsTab.Mode && gui.m_tabUpgrade.gameObject.activeSelf && !gui.m_tabUpgrade.interactable && tab.interactable,
                "the Forge opens on the vanilla UPGRADE tab (UPGRADE selected, IDOLS clickable)");
            c.Check(gui.m_availableRecipes.All(r => !IdolUpgrade.IsOurs(r.Recipe)), "the UPGRADE tab lists no idol row");
            var label = tab.GetComponentInChildren<TMP_Text>(true);
            c.Check(label != null && label.text == "IDOLS", $"the tab reads IDOLS, is '{(label != null ? label.text : "no label")}'");
            var tabRect = tab.transform as RectTransform;
            var craftRect = gui.m_tabCraft.transform as RectTransform;
            c.Check(!gui.m_tabCraft.gameObject.activeSelf && (tabRect.anchoredPosition - craftRect.anchoredPosition).magnitude < 0.01f,
                $"the IDOLS tab takes the place of the Craft tab the Forge hides ({tabRect.anchoredPosition} vs {craftRect.anchoredPosition})");
            c.Check(tab.transform.position.x < gui.m_tabUpgrade.transform.position.x,
                $"the IDOLS tab sits on the left of UPGRADE (x {tab.transform.position.x:0} vs {gui.m_tabUpgrade.transform.position.x:0})");

            yield return rig.ClickIdolsTab();
            c.Check(IdolsTab.Mode && !tab.interactable && gui.m_tabUpgrade.interactable, "click on IDOLS: IDOLS selected, UPGRADE clickable");
            foreach (var pair in stacks)
            {
                var problem = pair.Key != null ? RowProblem(rig, pair.Key, pair.Value) : "stack not given";
                c.Check(problem == null, $"{pair.Value}-star stack ({(pair.Value == 1 && pair.Key != null && pair.Key.m_shared.m_name != NameOf(SilverIdol) ? "armor idol" : "silver idol")}): one row with its stars on the icon, no quality number ({problem})");
            }
            c.Check(full != null && RowOf(gui, full) < 0, "a 3-star idol is not listed");
            var carried = rig.Inv.GetAllItems().Count(i => IdolCatalog.IsIdol(i) && IdolLevels.Of(i) < IdolLevels.Max);
            c.Check(gui.m_availableRecipes.Count == carried && gui.m_availableRecipes.All(r => IdolUpgrade.IsOurs(r.Recipe)),
                $"the list is exactly the idol stacks below 3 stars: {gui.m_availableRecipes.Count} rows for {carried} stacks");
            SelfTest.Screenshot(TabName, "idols-rows");
            yield return null;
            yield return null;
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.cost (T02) ----------

    private static IEnumerator RunCost()
    {
        var rig = new Rig(CostName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            rig.Give(SilverIdol, 3, 1);
            rig.Give("Silver", 5);
            rig.Give("TrophyWolf", 3);
            rig.Give("TrophyUlv", 2);
            yield return rig.OpenForge();
            yield return rig.ClickIdolsTab();
            var gui = rig.Gui;
            var stack = rig.Find(SilverIdol, 1);
            yield return rig.Select(stack);
            c.Check(rig.Row >= 0 && rig.RowCanCraft, "the plain silver idol stack is listed and can be upgraded");
            if (rig.Row < 0)
            {
                c.Report();
                yield break;
            }
            var idolName = NameOf(SilverIdol);
            var silver = L(NameOf("Silver"));
            var plain = PlainIconOf(SilverIdol);
            var pool = IdolCatalog.TierOf(IdolCatalog.IdolOfPrefab(Prefab(SilverIdol)))?.Pools[1];

            // Panel text.
            var text = rig.Description;
            c.Check(text.Contains("Level: <color=orange>no star</color> (35% refinement chance)"), $"panel: level and its chance, is '{text}'");
            c.Check(text.Contains("After upgrade: <color=orange>1 star</color> (55% refinement chance)"), "panel: the next level and its chance");
            c.Check(text.Contains($"Cost: 5 {silver}, 5 common trophies of this tier (any mix) and this idol."), "panel: the cost");
            c.Check(pool != null && pool.Names.Count == 5 && text.Contains("Common trophies: ") && pool.Names.All(n => text.Contains(L(n))),
                "panel: the 5 common trophies of the silver tier by name");
            c.Check(rig.CraftText == "Upgrade to 1 star", $"panel: 'Upgrade to 1 star', is '{rig.CraftText}'");
            c.Check(gui.m_recipeIcon.sprite == StarIcons.Get(plain, 1), "panel: the big icon shows the idol with 1 star");

            // Requirements.
            c.Check(SlotShown(gui, 0) && rig.SlotName(0) == silver && rig.SlotAmount(0) == "5" && rig.SlotTip(0) == silver + " (you have 5)",
                $"slot 1 = 5 Silver, is '{rig.SlotName(0)}' x '{rig.SlotAmount(0)}', tooltip '{rig.SlotTip(0)}'");
            c.Check(SlotShown(gui, 1) && rig.SlotName(1) == "Common trophies" && rig.SlotAmount(1) == "5",
                $"slot 2 = 5 Common trophies, is '{rig.SlotName(1)}' x '{rig.SlotAmount(1)}'");
            var tip = rig.SlotTip(1);
            c.Check(tip.StartsWith("Any mix of 5: ", StringComparison.Ordinal) && tip.Contains(L(NameOf("TrophyWolf")) + " (3)")
                    && tip.Contains(L(NameOf("TrophyUlv")) + " (2)") && pool != null && pool.Names.All(n => tip.Contains(L(n) + " (")),
                $"trophy tooltip lists every common trophy with how many you have, is '{tip}'");
            c.Check(SlotShown(gui, 2) && rig.SlotName(2) == L(idolName) && rig.SlotAmount(2) == "1" && rig.SlotIcon(2) == plain
                    && rig.SlotTip(2) == L(idolName) + ": the idol itself",
                $"slot 3 = the idol itself, is '{rig.SlotName(2)}' x '{rig.SlotAmount(2)}', tooltip '{rig.SlotTip(2)}'");
            c.Check(!SlotShown(gui, 3), "slot 4 empty");
            c.Check(rig.ButtonOn && rig.ButtonLabel == "Upgrade idol" && rig.ButtonTip == "", $"button 'Upgrade idol' is on, is '{rig.ButtonLabel}' / '{rig.ButtonTip}'");

            // Trophy icon go round the pool, one per second.
            var seen = new HashSet<Sprite>();
            var foreign = false;
            if (pool != null)
            {
                var icons = new HashSet<Sprite>(pool.Items.Select(i => i.m_itemData.GetIcon()));
                var until = Time.time + 2.4f;
                var giveUp = Time.realtimeSinceStartup + 8f;
                while (Time.time < until && Time.realtimeSinceStartup < giveUp)
                {
                    var shown = rig.SlotIcon(1);
                    if (shown != null)
                    {
                        seen.Add(shown);
                        foreign |= !icons.Contains(shown);
                    }
                    yield return null;
                }
            }
            c.Check(seen.Count >= 2 && !foreign, $"the trophy icon goes round the common trophies ({seen.Count} different icons in 2.4 s, foreign icon {foreign})");

            // Press: Forge timer, then one idol of stack has 1 star.
            rig.ClearCenter();
            gui.OnCraftPressed();
            yield return null;
            c.Check(gui.m_craftTimer >= 0f && IdolUpgrade.IsOurs(gui.m_craftRecipe), "Upgrade idol starts the Forge timer");
            c.Check(rig.Has(SilverIdol, 1) == 3 && rig.Has("Silver") == 5, "nothing is used before the timer ends");
            gui.m_craftTimer = 1000f;
            yield return null;
            yield return null;
            var name = idolName;
            var oneStar = rig.Inv.GetAllItems().Where(i => i.m_shared.m_name == name && i.m_quality == 2).ToList();
            var plainLeft = rig.Inv.GetAllItems().Where(i => i.m_shared.m_name == name && i.m_quality == 1).ToList();
            c.Check(oneStar.Count == 1 && oneStar[0].m_stack == 1 && plainLeft.Count == 1 && plainLeft[0].m_stack == 2,
                $"one idol has 1 star in a new stack, 2 stay plain ({rig.Has(SilverIdol, 2)} one-star, {rig.Has(SilverIdol, 1)} plain)");
            c.Check(rig.Has("Silver") == 0 && rig.Has("TrophyWolf") + rig.Has("TrophyUlv") == 0, "the 5 silver and the 5 trophies are gone");
            c.Check(rig.Center == L(idolName) + " upgraded to 1 star.", $"message says it was upgraded, is '{rig.Center}'");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.steps (T03) ----------

    private static IEnumerator RunSteps()
    {
        var rig = new Rig(StepsName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            var idol = rig.Give(SilverIdol, 1, 2);
            rig.Give("Silver", 10);
            rig.Give("TrophyFenring", 3);
            yield return rig.OpenForge();
            yield return rig.ClickIdolsTab();
            yield return rig.Select(idol);
            if (idol == null || rig.Row < 0)
            {
                c.Check(false, "the 1-star idol is not listed in the Idols tab");
                c.Report();
                yield break;
            }
            var gui = rig.Gui;
            var pos = idol.m_gridPos;
            var idolName = L(NameOf(SilverIdol));
            c.Check(rig.RowCanCraft && rig.CraftText == "Upgrade to 2 stars" && rig.SlotAmount(0) == "10" && rig.SlotName(1) == "Elite trophies"
                    && rig.SlotAmount(1) == "3", $"1 -> 2 stars costs 10 silver + 3 elite trophies ('{rig.CraftText}', {rig.SlotAmount(0)}, {rig.SlotName(1)} x {rig.SlotAmount(1)})");
            rig.ClearCenter();
            yield return rig.Press();
            c.Check(rig.Inv.ContainsItem(idol) && idol.m_quality == 3 && idol.m_gridPos.x == pos.x && idol.m_gridPos.y == pos.y,
                $"1 -> 2 stars: the same idol, in its slot (quality {idol.m_quality}, slot {idol.m_gridPos} was {pos})");
            c.Check(rig.Has("Silver") == 0 && rig.Has("TrophyFenring") == 0, "1 -> 2 stars: 10 silver and 3 Fenring trophies used");
            c.Check(rig.Center == idolName + " upgraded to 2 stars.", $"message, is '{rig.Center}'");

            rig.Give("Silver", 15);
            rig.Give("TrophyDragonQueen", 1);
            yield return rig.Select(idol);
            c.Check(rig.Row >= 0 && rig.RowCanCraft && rig.CraftText == "Upgrade to 3 stars" && rig.SlotAmount(0) == "15"
                    && rig.SlotName(1) == "Boss trophies" && rig.SlotAmount(1) == "1",
                $"2 -> 3 stars costs 15 silver + 1 boss trophy ('{rig.CraftText}', {rig.SlotAmount(0)}, {rig.SlotName(1)} x {rig.SlotAmount(1)})");
            rig.ClearCenter();
            yield return rig.Press();
            c.Check(rig.Inv.ContainsItem(idol) && idol.m_quality == 4 && idol.m_gridPos.x == pos.x && idol.m_gridPos.y == pos.y,
                $"2 -> 3 stars: the same idol, in its slot (quality {idol.m_quality}, slot {idol.m_gridPos})");
            c.Check(rig.Has("Silver") == 0 && rig.Has("TrophyDragonQueen") == 0, "2 -> 3 stars: 15 silver and the Moder trophy used");
            c.Check(rig.Center == idolName + " upgraded to 3 stars.", $"message, is '{rig.Center}'");
            yield return rig.Select(idol);
            c.Check(rig.Row < 0 && gui.m_availableRecipes.All(r => !ReferenceEquals(r.ItemData, idol)), "at 3 stars the idol leaves the Idols list");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.missing (T04) ----------

    private static IEnumerator RunMissing()
    {
        var rig = new Rig(MissingName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            var stack = rig.Give(SilverIdol, 2, 1);
            yield return rig.OpenForge();
            yield return rig.ClickIdolsTab();
            yield return rig.Select(stack);
            if (stack == null || rig.Row < 0)
            {
                c.Check(false, "the plain idol stack is not listed in the Idols tab");
                c.Report();
                yield break;
            }
            var gui = rig.Gui;
            var element = rig.RowElement;
            var rowName = element.transform.Find("name")?.GetComponent<TMP_Text>();
            var rowIcon = element.transform.Find("icon")?.GetComponent<Image>();
            c.Check(!rig.RowCanCraft && rowName != null && Near(rowName.color, GreyRow) && rowIcon != null && Near(rowIcon.color, GreyIcon),
                $"no metal, no trophies: the row is greyed like a vanilla recipe you cannot make (name {(rowName != null ? rowName.color : Color.clear)}, icon {(rowIcon != null ? rowIcon.color : Color.clear)})");
            c.Check(!rig.ButtonOn && rig.ButtonTip == L("$msg_missingrequirement"), $"button off, tooltip says the requirement is missing, is '{rig.ButtonTip}'");
            if (English)
            {
                c.Check(rig.ButtonTip.IndexOf("Missing requirement", StringComparison.OrdinalIgnoreCase) >= 0, $"tooltip reads 'Missing requirement', is '{rig.ButtonTip}'");
            }

            // Missing amounts flash red (metal and trophies), idol's own amount stay white.
            bool redMetal = false, whiteMetal = false, redTrophy = false, whiteTrophy = false, idolWhite = true;
            var until = Time.time + 1f;
            var giveUp = Time.realtimeSinceStartup + 6f;
            while (Time.time < until && Time.realtimeSinceStartup < giveUp)
            {
                redMetal |= Near(rig.SlotAmountColor(0), Color.red);
                whiteMetal |= Near(rig.SlotAmountColor(0), Color.white);
                redTrophy |= Near(rig.SlotAmountColor(1), Color.red);
                whiteTrophy |= Near(rig.SlotAmountColor(1), Color.white);
                idolWhite &= Near(rig.SlotAmountColor(2), Color.white);
                yield return null;
            }
            c.Check(redMetal && whiteMetal && redTrophy && whiteTrophy, $"the missing amounts flash red (metal red {redMetal} / white {whiteMetal}, trophies red {redTrophy} / white {whiteTrophy})");
            c.Check(idolWhite, "the idol's own amount never turns red");
            rig.ClearCenter();
            gui.OnCraftPressed();
            yield return null;
            c.Check(gui.m_craftTimer < 0f && rig.Center == L("$msg_missingrequirement"), $"a press starts nothing and says why, message '{rig.Center}'");

            // Full cost, stack of 2, no 1-star stack, no free slot: no room for upgraded idol.
            rig.Give("Silver", 5);
            rig.Give("TrophyWolf", 5);
            var clubs = rig.Fill();
            yield return rig.Select(stack);
            c.Check(rig.Inv.GetEmptySlots() == 0 && rig.Has(SilverIdol, 2) == 0, $"inventory full ({clubs} clubs added), no 1-star stack");
            c.Check(!rig.ButtonOn && !rig.RowCanCraft && rig.ButtonTip == L("$inventory_needspace") && !rig.ButtonTip.StartsWith("[", StringComparison.Ordinal),
                $"full inventory: button off, tooltip asks for free inventory space, is '{rig.ButtonTip}'");
            // Words of the game's own message, for eyes (TESTING.md quote the English one).
            SelfTest.Note(MissingName, $"need-space tooltip reads '{rig.ButtonTip}'");
            c.Check(Near(rig.SlotAmountColor(0), Color.white) && Near(rig.SlotAmountColor(1), Color.white), "with the cost carried no amount is red");
            rig.ClearCenter();
            gui.OnCraftPressed();
            yield return null;
            c.Check(gui.m_craftTimer < 0f && rig.Center == L("$inventory_needspace") && rig.Has("Silver") == 5 && rig.Has(SilverIdol, 1) == 2,
                $"full inventory: a press starts nothing, uses nothing, message '{rig.Center}'");
            rig.Unfill(1);
            yield return rig.Select(stack);
            c.Check(rig.ButtonOn && rig.RowCanCraft && rig.ButtonTip == "", "one free slot: the button is on");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.tooltip.text (T06) ----------

    private static IEnumerator RunTooltipText()
    {
        var c = new Checks(TooltipTextName);
        try
        {
            ServerRules.TestRules = DefaultRules();
            var item = Prefab(SilverIdol).m_itemData.Clone();
            var silver = L(NameOf("Silver"));
            string[] words = { "no star", "1 star", "2 stars", "3 stars" };
            int[] chances = { 35, 55, 75, 95 };
            string[] costs = { $"5 {silver}, 5 common trophies of its biome", $"10 {silver}, 3 elite trophies of its biome", $"15 {silver}, 1 boss trophy of its biome" };
            var qualityLine = L("$item_quality") + ":";
            for (var level = 0; level <= 3; level++)
            {
                item.m_quality = level + 1;
                var text = L(item.GetTooltip());
                c.Check(text.Contains($"Level: <color=orange>{words[level]}</color> ({level} of 3)"), $"{words[level]}: level line, tooltip '{text}'");
                c.Check(text.Contains($"Refinement chance: <color=orange>{chances[level]}%</color>"), $"{words[level]}: refinement chance {chances[level]}%");
                c.Check(!text.Contains(qualityLine), $"{words[level]}: no '{qualityLine}' line");
                if (level < 3)
                {
                    c.Check(CostLine(text) == costs[level], $"{words[level]}: next upgrade costs '{costs[level]}', tooltip says '{CostLine(text)}'");
                }
                else
                {
                    c.Check(text.Contains("Maximum level.") && CostLine(text) == null, "3 stars: 'Maximum level', no cost line");
                }
            }
            // Recipe panel build tooltip in "crafting" mode (no vanilla quality line to replace): our lines still come.
            var crafting = L(ItemDrop.ItemData.GetTooltip(item, 3, true, Game.m_worldLevel, 1));
            c.Check(crafting.Contains("Level: <color=orange>2 stars</color> (2 of 3)") && crafting.Contains("Refinement chance: <color=orange>75%</color>"),
                $"crafting-panel tooltip carries the level lines too: '{crafting}'");
            c.Report();
        }
        finally
        {
            ServerRules.TestRules = null;
        }
        yield break;
    }

    // ---------- forge.odds (T08) ----------

    private static IEnumerator RunOdds()
    {
        var rig = new Rig(OddsName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            IdolChoice.TestPick = IdolPick.Highest;
            const string idol = "Upgrader4Weapon";
            var own = OwnIdolOf("AxeBlackMetal");
            c.Check(own == idol, $"the Black metal axe's own idol must be {idol} in the game data, is '{own}'");
            if (own != idol)
            {
                c.Report();
                yield break;
            }
            rig.Teach("AxeBlackMetal");
            var axe = rig.Give("AxeBlackMetal", 1, 3);
            rig.Give(idol, 1, 4);
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            if (axe == null || rig.Row < 0)
            {
                c.Check(false, "the Black metal axe is not listed in the UPGRADE tab");
                c.Report();
                yield break;
            }
            var idolName = L(NameOf(idol));
            var plain = PlainIconOf(idol);
            var button = L("$inventory_upgraderbutton");
            c.Check(rig.RowCanCraft && rig.ButtonOn, "the axe can be refined with the 3-star idol");
            c.Check(rig.CraftText == "95% chance with a 3-star idol. A failure costs 1 level.", $"text under the recipe, is '{rig.CraftText}'");
            c.Check(rig.ButtonLabel == button + " (95%)", $"button shows the chance, is '{rig.ButtonLabel}'");
            if (English)
            {
                c.Check(rig.ButtonLabel == "Attempt Refinement (95%)", $"button reads 'Attempt Refinement (95%)', is '{rig.ButtonLabel}'");
            }
            c.Check(SlotShown(rig.Gui, 0) && rig.SlotName(0) == idolName && rig.SlotAmount(0) == "1", $"requirement = 1 {idolName}, is '{rig.SlotName(0)}' x '{rig.SlotAmount(0)}'");
            c.Check(rig.SlotIcon(0) == StarIcons.Get(plain, 3, true) && StarIcons.IsStarSprite(rig.SlotIcon(0)) && rig.SlotIcon(0) != StarIcons.Get(plain, 3),
                $"the idol under the requirements shows 3 stars, in the lower (requirement slot) layout: {Describe(rig.SlotIcon(0))}");
            c.Check(rig.SlotTip(0) == idolName + " (3-star): 95% chance.", $"slot tooltip, is '{rig.SlotTip(0)}'");
            SelfTest.Screenshot(OddsName, "blackmetal-axe");
            yield return null;
            yield return null;

            var mark = LogWatch.Mark();
            ForgeRefine.TestRoll = Win;
            yield return rig.Press();
            c.Check(axe.m_quality == 4, $"forced success: level 4, is {axe.m_quality}");
            var line = LogWatch.Since(mark).Select(l => l.Text).FirstOrDefault(t => t.StartsWith("Refinement of", StringComparison.Ordinal));
            c.Check(line != null && Regex.IsMatch(line, @"^Refinement of AxeBlackMetal to level 4 with Upgrader4Weapon at level 3 \(95% chance, roll 0[.,]0\): success\.$"),
                $"one log line for the attempt, is '{line}'");

            rig.Give(idol, 1, 1);
            yield return rig.Select(axe);
            c.Check(rig.CraftText == "35% chance with a plain idol. A failure costs 1 level." && rig.ButtonLabel == button + " (35%)",
                $"with a plain idol: 35%, is '{rig.CraftText}' / '{rig.ButtonLabel}'");
            mark = LogWatch.Mark();
            ForgeRefine.TestRoll = Fail;
            yield return rig.Press();
            line = LogWatch.Since(mark).Select(l => l.Text).FirstOrDefault(t => t.StartsWith("Refinement of", StringComparison.Ordinal));
            c.Check(axe.m_quality == 3 && line != null
                    && Regex.IsMatch(line, @"^Refinement of AxeBlackMetal to level 5 with Upgrader4Weapon at level 0 \(35% chance, roll 99[.,]0\): failed, down to level 3\.$"),
                $"a failed attempt logs its own line, is '{line}' (axe level {axe.m_quality})");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.failure (T09, C07) ----------

    private static IEnumerator RunFailure()
    {
        var rig = new Rig(FailureName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var axe = rig.Give("AxeStone", 1, 3);
            rig.Give(WoodIdol, 2, 1);
            if (axe == null)
            {
                c.Report();
                yield break;
            }
            axe.m_customData[Marker] = "kept";
            var axeName = axe.m_shared.m_name;
            var recover = RecoverNames(RecipeOf("AxeStone"));
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            c.Check(rig.Row >= 0 && rig.CraftText == "35% chance with a plain idol. A failure costs 1 level.", $"text under the recipe at level 3, is '{rig.CraftText}'");

            var before = CountAll(rig, recover);
            var mark = LogWatch.Mark();
            rig.ClearCenter();
            axe.m_durability = axe.GetMaxDurability() * 0.5f;
            ForgeRefine.TestRoll = Fail;
            yield return rig.Press();
            c.Check(rig.Inv.ContainsItem(axe) && axe.m_quality == 2, $"failure: the same axe, 1 level lower (level {axe.m_quality})");
            c.Check(rig.Has(WoodIdol) == 1, $"failure: one idol is gone ({rig.Has(WoodIdol)} left of 2)");
            var want = L(Localization.instance.Localize("$msg_upgrader_fail", axeName, "2"));
            c.Check(rig.Center == want && !want.Contains("[msg_"), $"failure message, is '{rig.Center}', expected '{want}'");
            if (English)
            {
                c.Check(rig.Center.IndexOf("failed, downgraded to level 2", StringComparison.OrdinalIgnoreCase) >= 0, $"message reads '... failed, downgraded to level 2', is '{rig.Center}'");
            }
            c.Check(before.SequenceEqual(CountAll(rig, recover)), $"failure: no material comes back ({string.Join(", ", recover)})");
            c.Check(Logged(mark, LogLevel.Info, "Refinement of AxeStone to level 4 with Upgrader0Weapon at level 0", "failed, down to level 2.") != null,
                $"log line says 'failed, down to level 2': {Tail(mark)}");
            c.Check(axe.m_customData.TryGetValue(Marker, out var kept) && kept == "kept", "failure: data other mods keep on the item stays");
            c.Check(Mathf.Approximately(axe.m_durability, axe.GetMaxDurability()), "failure: re-made at full durability, like vanilla");

            // Level 1: failure cost only the idol, item not touched.
            axe.m_quality = 1;
            axe.m_durability = axe.GetMaxDurability() * 0.5f;
            var durability = axe.m_durability;
            yield return rig.Select(axe);
            c.Check(rig.Row >= 0 && rig.CraftText == "35% chance with a plain idol. A failure costs only the idol.", $"text under the recipe at level 1, is '{rig.CraftText}'");
            mark = LogWatch.Mark();
            rig.ClearCenter();
            ForgeRefine.TestRoll = Fail;
            yield return rig.Press();
            c.Check(rig.Inv.ContainsItem(axe) && axe.m_quality == 1 && Mathf.Approximately(axe.m_durability, durability),
                $"failure at level 1: stays level 1, untouched (level {axe.m_quality}, durability {axe.m_durability} was {durability})");
            c.Check(rig.Has(WoodIdol) == 0, "failure at level 1: the idol is used");
            c.Check(rig.Center == L(axeName) + " refinement failed. It stays at level 1.", $"message at level 1, is '{rig.Center}'");
            c.Check(Logged(mark, LogLevel.Info, "Refinement of AxeStone to level 2", "failed, item stays at level 1.") != null, $"log line at level 1: {Tail(mark)}");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.success (T10, C07) ----------

    private static IEnumerator RunSuccess()
    {
        var rig = new Rig(SuccessName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var axe = rig.Give("AxeStone", 1, 3);
            rig.Give(WoodIdol, 2, 1);
            if (axe == null)
            {
                c.Report();
                yield break;
            }
            var player = rig.P;
            axe.m_customData[Marker] = "kept";
            axe.m_durability = axe.GetMaxDurability() * 0.5f;
            axe.m_crafterID = 0L;
            axe.m_crafterName = "";
            axe.m_worldLevel = Game.m_worldLevel + 1;
            var held = player.EquipItem(axe, false) && player.IsItemEquiped(axe);
            c.Check(held, $"the axe is in the player's hand before the Forge is used (attacking {player.InAttack()}, dodging {player.InDodge()}, swimming {player.IsSwimming()})");
            var pos = axe.m_gridPos;
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            c.Check(rig.Row >= 0 && rig.ButtonOn, "the axe the player holds is listed and can be refined");
            // Game put hand items away when a station is used (Player.SetCraftingStation call HideHandItems): axe is
            // then "hidden right item", no longer equipped, and come back to the hand when player draw again. So
            // "equipped" at the Forge = in hand or put away. First refinement take it as the game left it.
            var putAway = player.m_hiddenRightItem == axe;
            SelfTest.Note(SuccessName, $"at the Forge the axe is {(player.IsItemEquiped(axe) ? "in hand" : putAway ? "put away by the station" : "neither in hand nor put away")}");
            c.Check(player.IsItemEquiped(axe) || putAway, "at the Forge the axe is still the player's weapon (in hand, or put away by the station) before the refinement");
            rig.ClearCenter();
            ForgeRefine.TestRoll = Win;
            yield return rig.Press();
            c.Check(rig.Inv.ContainsItem(axe) && axe.m_quality == 4 && axe.m_gridPos.x == pos.x && axe.m_gridPos.y == pos.y,
                $"success: the axe gains one level, in its slot (level {axe.m_quality})");
            c.Check(Mathf.Approximately(axe.m_durability, axe.GetMaxDurability()), $"success: full durability ({axe.m_durability} of {axe.GetMaxDurability()})");
            c.Check(axe.m_crafterID == player.GetPlayerID() && axe.m_crafterName == player.GetPlayerName(),
                $"success: you are its crafter ('{axe.m_crafterName}', id match {axe.m_crafterID == player.GetPlayerID()})");
            c.Check(axe.m_worldLevel == Game.m_worldLevel, $"success: the current world level ({axe.m_worldLevel}, world {Game.m_worldLevel})");
            c.Check(!player.IsItemEquiped(axe) && !axe.m_equipped && player.m_hiddenRightItem != axe && player.m_hiddenLeftItem != axe,
                $"success: the axe is taken off, it is no longer the weapon the player draws (in hand {player.IsItemEquiped(axe)}, put away {player.m_hiddenRightItem == axe})");
            c.Check(axe.m_customData.TryGetValue(Marker, out var kept) && kept == "kept", "success: data other mods keep on the item stays");
            c.Check(rig.Has(WoodIdol) == 1, $"success: one idol is used ({rig.Has(WoodIdol)} left of 2)");
            var want = L(Localization.instance.Localize("$msg_upgrader_success", axe.m_shared.m_name, "4"));
            c.Check(rig.Center == want && !want.Contains("[msg_"), $"success message, is '{rig.Center}', expected '{want}'");
            c.Check(InventoryGui.IsVisible() && player.GetCurrentCraftingStation() == rig.Forge.Station, "the Forge window stays open");

            // Second refinement with axe really in hand: player click it in inventory while Forge window is open
            // (game equip it then, station hide hand items only when player start to use it).
            var inHand = player.EquipItem(axe, false);
            yield return rig.Select(axe);
            c.Check(inHand && rig.Row >= 0 && player.IsItemEquiped(axe) && axe.m_equipped, "the axe is equipped again (in hand) with the Forge window open");
            ForgeRefine.TestRoll = Win;
            yield return rig.Press();
            c.Check(axe.m_quality == 5 && !player.IsItemEquiped(axe) && !axe.m_equipped && player.m_hiddenRightItem != axe,
                $"success with the axe in hand: level 5 and the axe is taken off (level {axe.m_quality}, in hand {player.IsItemEquiped(axe)})");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.tiers.stone (T20) ----------

    private static IEnumerator RunStone()
    {
        var rig = new Rig(StoneName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            var armorIdol = OwnIdolOf("ArmorLeatherChest");
            var flintIdol = OwnIdolOf("AxeFlint");
            c.Check(armorIdol == "Upgrader0Armor" && flintIdol == BronzeIdol,
                $"game data: leather tunic on Upgrader0Armor (is '{armorIdol}'), Flint axe on {BronzeIdol} (is '{flintIdol}')");
            if (!StoneAxeReady(rig) || armorIdol != "Upgrader0Armor" || flintIdol != BronzeIdol)
            {
                c.Report();
                yield break;
            }
            rig.Teach("ArmorLeatherChest", "AxeFlint");
            var wood = L(NameOf(WoodIdol));
            var bronze = L(NameOf(BronzeIdol));
            var axe = rig.Give("AxeStone", 1, 5);
            rig.Give(WoodIdol, 2, 1);
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            if (axe == null || rig.Row < 0)
            {
                c.Check(false, "the level 5 Stone axe is not listed in the UPGRADE tab");
                c.Report();
                yield break;
            }
            c.Check(rig.SlotName(0) == wood && rig.ButtonOn && rig.RowCanCraft && !rig.CraftText.Contains("From level"),
                $"level 5: asks for the Wooden Battle Idol, is '{rig.SlotName(0)}' ('{rig.CraftText}')");
            ForgeRefine.TestRoll = Win;
            yield return rig.Press();
            c.Check(axe.m_quality == 6 && rig.Has(WoodIdol) == 1, $"refined to level 6 with a Wooden idol (level {axe.m_quality}, {rig.Has(WoodIdol)} wooden left)");

            yield return rig.Select(axe);
            var rowName = rig.RowElement != null ? rig.RowElement.transform.Find("name")?.GetComponent<TMP_Text>() : null;
            c.Check(rig.SlotName(0) == bronze, $"level 6: the requirement becomes the Bronze Battle Idol, is '{rig.SlotName(0)}'");
            c.Check(rig.CraftText == "From level 6 this item needs Bronze idols. You carry none.", $"level 6 text without a Bronze idol, is '{rig.CraftText}'");
            c.Check(!rig.RowCanCraft && rowName != null && Near(rowName.color, GreyRow), "level 6: the row is greyed");
            c.Check(!rig.ButtonOn && rig.Has(WoodIdol) == 1, "level 6: the button is off although a Wooden idol is carried");
            SelfTest.Screenshot(StoneName, "level6-no-bronze");
            yield return null;
            yield return null;

            rig.Give(BronzeIdol, 1, 1);
            yield return rig.Select(axe);
            c.Check(rig.ButtonOn && rig.RowCanCraft, "with a Bronze idol the button lights up");
            c.Check(rig.CraftText == "35% chance with a plain idol. A failure costs 1 level. From level 6 this item needs Bronze idols.",
                $"level 6 text with a Bronze idol, is '{rig.CraftText}'");
            var mark = LogWatch.Mark();
            ForgeRefine.TestRoll = Win;
            yield return rig.Press();
            c.Check(axe.m_quality == 7 && rig.Has(BronzeIdol) == 0 && rig.Has(WoodIdol) == 1,
                $"the refinement spends the Bronze idol, the Wooden idol stays (level {axe.m_quality}, bronze {rig.Has(BronzeIdol)}, wooden {rig.Has(WoodIdol)})");
            c.Check(Logged(mark, LogLevel.Info, "Refinement of AxeStone to level 7 with Upgrader1Weapon at level 0 (own idol tier 0, raised by item level)") != null,
                $"the log line names Upgrader1Weapon and why: {Tail(mark)}");

            var armor = rig.Give("ArmorLeatherChest", 1, 6);
            yield return rig.Select(armor);
            c.Check(rig.Row >= 0 && rig.SlotName(0) == L(NameOf("Upgrader1Armor")), $"level 6 leather tunic asks for the Bronze Protection Idol, is '{rig.SlotName(0)}'");
            var flint = rig.Give("AxeFlint", 1, 4);
            yield return rig.Select(flint);
            c.Check(rig.Row >= 0 && rig.SlotName(0) == bronze, $"level 4 Flint axe asks for its own Bronze idol, is '{rig.SlotName(0)}'");
            if (flint != null)
            {
                flint.m_quality = 6;
            }
            yield return rig.Select(flint);
            c.Check(rig.Row >= 0 && rig.SlotName(0) == L(NameOf("Upgrader2Weapon")), $"level 6 Flint axe asks for an Iron idol, is '{rig.SlotName(0)}'");
            axe.m_quality = 4;
            yield return rig.Select(axe);
            c.Check(rig.Row >= 0 && rig.SlotName(0) == wood && rig.ButtonOn, $"a level 4 Stone axe asks for the Wooden idol again, is '{rig.SlotName(0)}'");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.tiers.live (T21) ----------

    private static IEnumerator RunTiersLive()
    {
        var rig = new Rig(TiersLiveName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var wood = L(NameOf(WoodIdol));
            var axe = rig.Give("AxeStone", 1, 4);
            rig.Give(WoodIdol, 1, 1);
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            if (axe == null || rig.Row < 0)
            {
                c.Check(false, "the Stone axe is not listed in the UPGRADE tab");
                c.Report();
                yield break;
            }
            c.Check(rig.SlotName(0) == wood, $"defaults: a level 4 Stone axe asks for the Wooden idol, is '{rig.SlotName(0)}'");

            // Rules change while Forge window stay open (live setting change = new rules snapshot).
            ServerRules.TestRules = DefaultRules(r => r.BaseLevels = 3);
            yield return Frames(3);
            c.Check(rig.SlotName(0) == L(NameOf(BronzeIdol)) && rig.CraftText.Contains("From level 4 this item needs Bronze idols."),
                $"LevelsOnOwnIdol = 3: level 4 asks for the Bronze idol, is '{rig.SlotName(0)}' ('{rig.CraftText}')");
            ServerRules.TestRules = DefaultRules(r =>
            {
                r.BaseLevels = 3;
                r.LevelsPerTier = 1;
            });
            axe.m_quality = 5;
            yield return Frames(3);
            c.Check(rig.SlotName(0) == L(NameOf("Upgrader2Weapon")), $"LevelsPerIdolTier = 1: level 5 asks for Iron, is '{rig.SlotName(0)}'");
            axe.m_quality = 6;
            yield return Frames(3);
            c.Check(rig.SlotName(0) == L(NameOf(SilverIdol)), $"LevelsPerIdolTier = 1: level 6 asks for Silver, is '{rig.SlotName(0)}'");
            ServerRules.TestRules = DefaultRules(r =>
            {
                r.BaseLevels = 3;
                r.LevelsPerTier = 1;
                r.TierByLevel = false;
            });
            yield return Frames(3);
            var six = rig.SlotName(0);
            axe.m_quality = 40;
            yield return Frames(3);
            c.Check(six == wood && rig.SlotName(0) == wood && !rig.CraftText.Contains("From level"),
                $"HigherIdolAtHighLevels = false: every level asks for the Wooden idol (level 6 '{six}', level 40 '{rig.SlotName(0)}')");
            ServerRules.TestRules = DefaultRules();
            yield return Frames(3);
            c.Check(rig.SlotName(0) == L(NameOf("Upgrader7Weapon")) && rig.CraftText == "From level 30 this item needs Bloodgold idols. You carry none.",
                $"defaults: a level 40 Stone axe asks for the Bloodgold Battle Idol, is '{rig.SlotName(0)}' ('{rig.CraftText}')");
            c.Check(InventoryGui.IsVisible(), "the Forge window stayed open through every change");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.failure.rules (T19) ----------

    private static IEnumerator RunFailureRules()
    {
        var rig = new Rig(FailureRulesName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules(r => r.LevelsLost = 2);
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var axe = rig.Give("AxeStone", 1, 4);
            rig.Give(WoodIdol, 3, 1);
            if (axe == null)
            {
                c.Report();
                yield break;
            }
            var axeName = axe.m_shared.m_name;
            var gui = rig.Gui;
            c.Check(ForgePanel.FailureText(4) == "A failure costs 2 levels." && ForgePanel.FailureText(2) == "A failure costs 1 level."
                    && ForgePanel.FailureText(1) == "A failure costs only the idol.", "LevelsLost = 2: 2 levels, 1 level at level 2, nothing at level 1");
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            c.Check(rig.Row >= 0 && rig.CraftText == "35% chance with a plain idol. A failure costs 2 levels.", $"LevelsLost = 2: text at level 4, is '{rig.CraftText}'");
            rig.ClearCenter();
            ForgeRefine.TestRoll = Fail;
            yield return rig.Press();
            c.Check(rig.Inv.ContainsItem(axe) && axe.m_quality == 2, $"LevelsLost = 2: a failure at level 4 leaves level 2, is {axe.m_quality}");
            c.Check(rig.Center == L(Localization.instance.Localize("$msg_upgrader_fail", axeName, "2")), $"message names level 2, is '{rig.Center}'");
            yield return rig.Select(axe);
            c.Check(rig.CraftText == "35% chance with a plain idol. A failure costs 1 level.", $"LevelsLost = 2: text at level 2, is '{rig.CraftText}'");
            ForgeRefine.TestRoll = Fail;
            yield return rig.Press();
            c.Check(rig.Inv.ContainsItem(axe) && axe.m_quality == 1, $"LevelsLost = 2: a failure at level 2 stops at level 1 (never below), is {axe.m_quality}");

            // OnFailure = Destroy: vanilla break. Vanilla want one free slot per recoverable material + 1.
            ServerRules.TestRules = DefaultRules(r => r.Failure = FailureMode.Destroy);
            axe.m_quality = 3;
            var recipe = RecipeOf("AxeStone");
            var recover = recipe.m_resources.Where(q => q != null && q.m_recover && q.m_resItem != null).ToArray();
            var share = Prefab(WoodIdol).m_itemData.m_shared.m_breakReturnIngreientsAmount;
            var clubs = rig.Fill();
            yield return rig.Select(axe);
            c.Check(rig.Row >= 0 && rig.CraftText == "35% chance with a plain idol. A failure destroys the item.", $"Destroy: text, is '{rig.CraftText}'");
            rig.ClearCenter();
            gui.OnCraftPressed();
            yield return null;
            c.Check(rig.Inv.GetEmptySlots() == 0 && gui.m_craftTimer < 0f && rig.Center == L("$inventory_needspace"),
                $"Destroy: with a full inventory the Forge asks for free slots like vanilla ({clubs} clubs, timer {gui.m_craftTimer}, message '{rig.Center}')");
            rig.Unfill(clubs);
            yield return rig.Select(axe);
            var names = recover.Select(q => q.m_resItem.m_itemData.m_shared.m_name).ToArray();
            var before = CountAll(rig, names);
            rig.ClearCenter();
            ForgeRefine.TestRoll = Fail;
            yield return rig.Press();
            c.Check(!rig.Inv.ContainsItem(axe) && rig.Has("AxeStone") == 0, "Destroy: a failure destroys the item");
            c.Check(rig.Center == L(Localization.instance.Localize("$msg_upgrader_broke", axeName, "3")), $"Destroy: the vanilla message, is '{rig.Center}'");
            var after = CountAll(rig, names);
            var refund = new List<string>();
            var refundOk = recover.Length > 0;
            for (var i = 0; i < recover.Length; i++)
            {
                var expected = Mathf.CeilToInt((recover[i].GetAmount(1) + recover[i].GetAmount(3)) * share);
                refund.Add($"{recover[i].m_resItem.name} +{after[i] - before[i]} (vanilla rule: {expected})");
                refundOk &= after[i] - before[i] == expected;
            }
            c.Check(refundOk && refund.Any(), $"Destroy: part of the materials comes back, by the vanilla rule: {string.Join(", ", refund.ToArray())}");
            c.Check(rig.Has(WoodIdol) == 0, "the idol is used on every attempt");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.choice (T11) ----------

    private static IEnumerator RunChoice()
    {
        var rig = new Rig(ChoiceName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            IdolChoice.TestPick = IdolPick.Highest;
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var axe = rig.Give("AxeStone", 1, 3);
            rig.Give(WoodIdol, 2, 1);
            rig.Give(WoodIdol, 2, 3);
            var idolName = NameOf(WoodIdol);
            var plain = PlainIconOf(WoodIdol);
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            if (axe == null || rig.Row < 0)
            {
                c.Check(false, "the Stone axe is not listed in the UPGRADE tab");
                c.Report();
                yield break;
            }
            c.Check(IdolChoice.LevelToSpend(rig.Inv, idolName, 1) == 2 && rig.ButtonLabel.EndsWith("(75%)", StringComparison.Ordinal)
                    && rig.SlotIcon(0) == StarIcons.Get(plain, 2, true),
                $"a plain and a 2-star idol carried: the Forge picks the 2-star one (75%), button '{rig.ButtonLabel}'");
            c.Check(rig.SlotTip(0).EndsWith("Click to use another level you carry.", StringComparison.Ordinal), $"slot tooltip says you can click, is '{rig.SlotTip(0)}'");

            c.Check(ClickSlot(rig, 0), "the idol slot takes a left mouse click");
            yield return Frames(2);
            c.Check(IdolChoice.LevelToSpend(rig.Inv, idolName, 1) == 0 && rig.ButtonLabel.EndsWith("(35%)", StringComparison.Ordinal)
                    && rig.CraftText.StartsWith("35% chance with a plain idol.", StringComparison.Ordinal) && rig.SlotIcon(0) == plain,
                $"click: it switches to the plain one (35%), button '{rig.ButtonLabel}', text '{rig.CraftText}'");
            ClickSlot(rig, 0);
            yield return Frames(2);
            c.Check(IdolChoice.LevelToSpend(rig.Inv, idolName, 1) == 2 && rig.ButtonLabel.EndsWith("(75%)", StringComparison.Ordinal),
                $"click again: back to the 2-star one, button '{rig.ButtonLabel}'");

            // Window closed, IdolChoice = Lowest, Forge opened again.
            yield return rig.CloseWindow();
            IdolChoice.TestPick = IdolPick.Lowest;
            yield return rig.ReopenForge();
            yield return rig.Select(axe);
            c.Check(rig.Row >= 0 && IdolChoice.LevelToSpend(rig.Inv, idolName, 1) == 0 && rig.ButtonLabel.EndsWith("(35%)", StringComparison.Ordinal),
                $"IdolChoice = Lowest: the plain one is picked first, button '{rig.ButtonLabel}'");
            ClickSlot(rig, 0);
            yield return Frames(2);
            c.Check(IdolChoice.LevelToSpend(rig.Inv, idolName, 1) == 2 && rig.ButtonLabel.EndsWith("(75%)", StringComparison.Ordinal),
                $"a clicked pick wins over the setting, button '{rig.ButtonLabel}'");

            // Picked level = level that get spent.
            ForgeRefine.TestRoll = Win;
            yield return rig.Press();
            c.Check(axe.m_quality == 4 && rig.Has(WoodIdol, 3) == 1 && rig.Has(WoodIdol, 1) == 2,
                $"the refinement spends the clicked 2-star idol, not a plain one ({rig.Has(WoodIdol, 3)} two-star, {rig.Has(WoodIdol, 1)} plain left)");
            yield return rig.Select(axe);
            c.Check(IdolChoice.LevelToSpend(rig.Inv, idolName, 1) == 2, "the clicked pick lasts while the window stays open");
            yield return rig.CloseWindow();
            yield return rig.ReopenForge();
            yield return rig.Select(axe);
            c.Check(IdolChoice.LevelToSpend(rig.Inv, idolName, 1) == 0 && rig.ButtonLabel.EndsWith("(35%)", StringComparison.Ordinal),
                $"window closed and opened: the click is forgotten, the setting (Lowest) applies, button '{rig.ButtonLabel}'");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.space (T12) ----------

    private static IEnumerator RunSpace()
    {
        var rig = new Rig(SpaceName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var axe = rig.Give("AxeStone", 1, 3);
            rig.Give(WoodIdol, 1, 1);
            yield return rig.OpenForge();
            var clubs = rig.Fill();
            yield return rig.Select(axe);
            if (axe == null || rig.Row < 0)
            {
                c.Check(false, "the Stone axe is not listed in the UPGRADE tab");
                c.Report();
                yield break;
            }
            var gui = rig.Gui;
            var recover = RecipeOf("AxeStone").m_resources.Count(q => q != null && q.m_recover);
            c.Check(rig.Inv.GetEmptySlots() == 0 && recover + 1 > rig.Inv.GetEmptySlots(),
                $"inventory full ({clubs} clubs): vanilla would ask for {recover + 1} free slots");
            c.Check(rig.ButtonOn, "OnFailure = LoseLevels: the button is on with a full inventory");
            rig.ClearCenter();
            gui.OnCraftPressed();
            yield return null;
            c.Check(gui.m_craftTimer >= 0f && gui.m_craftRecipe != null && ReferenceEquals(gui.m_craftUpgradeItem, axe) && rig.Center != L("$inventory_needspace"),
                $"the refinement starts (timer {gui.m_craftTimer}, message '{rig.Center}')");
            ForgeRefine.TestRoll = Win;
            gui.m_craftTimer = 1000f;
            yield return Frames(2);
            c.Check(axe.m_quality == 4 && rig.Has(WoodIdol) == 0, $"and it ends normally (level {axe.m_quality})");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.rules.live (T13) ----------

    private static IEnumerator RunRulesLive()
    {
        var rig = new Rig(RulesLiveName);
        var c = rig.C;
        try
        {
            Action<ForgeRules> sixty = r =>
            {
                r.Chance[2] = 60;
                r.Trophies[1] = 2;
            };
            ServerRules.TestRules = DefaultRules(sixty);
            IdolChoice.TestPick = IdolPick.Highest;
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var axe = rig.Give("AxeStone", 1, 3);
            var two = rig.Give(WoodIdol, 1, 3);
            var button = L("$inventory_upgraderbutton");
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            if (axe == null || two == null || rig.Row < 0)
            {
                c.Check(false, "the Stone axe is not listed in the UPGRADE tab");
                c.Report();
                yield break;
            }
            c.Check(rig.ButtonLabel == button + " (60%)", $"ChanceLevel2 = 60: the Forge button shows 60%, is '{rig.ButtonLabel}'");
            c.Check(rig.CraftText.StartsWith("60% chance with a 2-star idol.", StringComparison.Ordinal) && rig.SlotTip(0).Contains("60% chance"),
                $"the panel and the slot tooltip show 60%, are '{rig.CraftText}' / '{rig.SlotTip(0)}'");
            c.Check(L(two.GetTooltip()).Contains("Refinement chance: <color=orange>60%</color>"), "the idol's tooltip shows 60%");

            // Other change with window open: no restart, not even close and open.
            ServerRules.TestRules = DefaultRules(r =>
            {
                sixty(r);
                r.Chance[2] = 61;
            });
            yield return Frames(3);
            c.Check(rig.ButtonLabel == button + " (61%)" && InventoryGui.IsVisible(), $"changed again with the Forge open: the button follows, is '{rig.ButtonLabel}'");
            ServerRules.TestRules = DefaultRules(sixty);

            var plain = rig.Give(SilverIdol, 1, 1);
            yield return rig.ClickIdolsTab();
            yield return rig.Select(plain);
            c.Check(rig.Row >= 0 && rig.SlotName(1) == "Common trophies" && rig.SlotAmount(1) == "2",
                $"Level1Trophies = 2: the Idols tab asks 2 trophies, slot '{rig.SlotName(1)}' x '{rig.SlotAmount(1)}'");
            c.Check(rig.Description.Contains("2 common trophies of this tier") && plain != null && L(plain.GetTooltip()).Contains("2 common trophies of its biome"),
                "the panel text and the idol's tooltip say 2 common trophies");

            // Typo in a trophy list: log say which name ignored, good names still count.
            rig.Provoke();
            var mark = LogWatch.Mark();
            ServerRules.TestRules = DefaultRules(r => r.TierCommon[3] = "TrophyWolf, TrophyWolff");
            var pool = IdolCatalog.TierOf(IdolCatalog.IdolOfPrefab(Prefab(SilverIdol)))?.Pools[1];
            var warning = Logged(mark, LogLevel.Warning, "Unknown item name(s) in the idol settings, ignored: TrophyWolff.");
            c.Check(warning != null, $"a typo in a trophy list is named in the log: {Tail(mark)}");
            c.Check(pool != null && pool.Names.Count == 1 && pool.Names[0] == NameOf("TrophyWolf"), "the good name of that list still counts");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.rules.wiring (T13, T19, T21) ----------

    // Mod read settings through one snapshot (ServerRules.Current). Changed rule setting must throw snapshot away, so
    // next read take new values. Me raise config file's own "setting changed" event for each rule setting, no value
    // changed (no config write), like BepInEx do after a real change.
    private static IEnumerator RunWiring()
    {
        var c = new Checks(WiringName);
        var testRules = ServerRules.TestRules;
        try
        {
            ServerRules.TestRules = null;
            var plugin = Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) ? info.Instance : null;
            var field = typeof(ConfigFile).GetField("SettingChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            var handler = plugin != null && field != null ? field.GetValue(plugin.Config) as EventHandler<SettingChangedEventArgs> : null;
            c.Check(handler != null, "the config file's SettingChanged listeners can be reached");
            if (handler == null)
            {
                c.Report();
                yield break;
            }
            Action<ConfigEntryBase> raise = entry => handler(plugin.Config, new SettingChangedEventArgs(entry));

            var first = ServerRules.Current;
            c.Check(ReferenceEquals(first, ServerRules.Current), "the rules snapshot is kept while no setting changes");
            // Every setting land in its own place of snapshot (defaults differ from each other).
            var wrong = new List<string>();
            for (var level = 0; level <= IdolLevels.Max; level++)
            {
                if (first.Chance[level] != Plugin.Chance[level].Value)
                {
                    wrong.Add("ChanceLevel" + level);
                }
                if (level > 0 && (first.Material[level] != Plugin.MaterialCost[level].Value || first.Trophies[level] != Plugin.TrophyCost[level].Value))
                {
                    wrong.Add($"Level{level}Material/Trophies");
                }
            }
            if (first.Failure != Plugin.Failure.Value || first.LevelsLost != Plugin.LevelsLost.Value)
            {
                wrong.Add("OnFailure/LevelsLost");
            }
            if (first.TierByLevel != Plugin.TierByLevel.Value || first.BaseLevels != Plugin.BaseLevels.Value || first.LevelsPerTier != Plugin.LevelsPerTier.Value)
            {
                wrong.Add("HigherIdolAtHighLevels/LevelsOnOwnIdol/LevelsPerIdolTier");
            }
            for (var t = 0; t < IdolTierDefaults.Count; t++)
            {
                if (first.TierMaterial[t] != Plugin.Tiers[t].Material.Value || first.TierCommon[t] != Plugin.Tiers[t].Base.Value
                    || first.TierElite[t] != Plugin.Tiers[t].Elite.Value || first.TierBoss[t] != Plugin.Tiers[t].Boss.Value)
                {
                    wrong.Add("Tier " + t);
                }
            }
            c.Check(wrong.Count == 0, $"the snapshot holds each setting's value in its own place (wrong: {string.Join(", ", wrong.ToArray())})");

            var rules = new List<KeyValuePair<string, ConfigEntryBase>>
            {
                new KeyValuePair<string, ConfigEntryBase>("ChanceLevel2", Plugin.Chance[2]),
                new KeyValuePair<string, ConfigEntryBase>("OnFailure", Plugin.Failure),
                new KeyValuePair<string, ConfigEntryBase>("LevelsLost", Plugin.LevelsLost),
                new KeyValuePair<string, ConfigEntryBase>("HigherIdolAtHighLevels", Plugin.TierByLevel),
                new KeyValuePair<string, ConfigEntryBase>("LevelsOnOwnIdol", Plugin.BaseLevels),
                new KeyValuePair<string, ConfigEntryBase>("LevelsPerIdolTier", Plugin.LevelsPerTier),
                new KeyValuePair<string, ConfigEntryBase>("Level1Material", Plugin.MaterialCost[1]),
                new KeyValuePair<string, ConfigEntryBase>("Level1Trophies", Plugin.TrophyCost[1]),
                new KeyValuePair<string, ConfigEntryBase>("Tier 3 Material", Plugin.Tiers[3].Material),
                new KeyValuePair<string, ConfigEntryBase>("Tier 3 CommonTrophies", Plugin.Tiers[3].Base),
            };
            var stale = new List<string>();
            var pushed = true;
            foreach (var pair in rules)
            {
                var before = ServerRules.Current;
                raise(pair.Value);
                pushed &= ServerRules.PushPending;
                var after = ServerRules.Current;
                if (ReferenceEquals(before, after) || after.Describe() != ForgeRules.Own().Describe())
                {
                    stale.Add(pair.Key);
                }
                yield return null;
            }
            c.Check(stale.Count == 0, $"every rule setting that changes gives a new snapshot read from the settings (stale after: {string.Join(", ", stale.ToArray())})");
            c.Check(pushed, "and marks the rules to be sent to the players again (server / host)");

            var kept = ServerRules.Current;
            raise(Plugin.Pick);
            raise(Plugin.AllowPlayersWithoutMod);
            c.Check(ReferenceEquals(kept, ServerRules.Current), "IdolChoice (personal) and the General settings do not touch the rules");
            c.Report();
        }
        finally
        {
            ServerRules.TestRules = testRules;
        }
    }

    // ---------- forge.timer.tabs (T26) ----------

    private static IEnumerator RunTimerTabs()
    {
        var rig = new Rig(TimerTabsName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            // Twice of everything a job use: a job that end twice would show in the counts.
            var axe = rig.Give("AxeStone", 1, 3);
            rig.Give(WoodIdol, 2, 1);
            rig.Give(SilverIdol, 2, 1);
            rig.Give("Silver", 10);
            rig.Give("TrophyWolf", 10);
            yield return rig.OpenForge();
            yield return rig.ClickIdolsTab();
            var gui = rig.Gui;
            var stack = rig.Find(SilverIdol, 1);
            yield return rig.Select(stack);
            if (axe == null || rig.Row < 0)
            {
                c.Check(false, "the plain idol stack is not listed in the Idols tab");
                c.Report();
                yield break;
            }

            // Idol upgrade started, then UPGRADE tab clicked while timer run.
            gui.OnCraftPressed();
            yield return null;
            c.Check(gui.m_craftTimer >= 0f && IdolUpgrade.IsOurs(gui.m_craftRecipe), "idol upgrade timer running");
            gui.OnTabUpgradePressed();
            yield return Frames(2);
            c.Check(!IdolsTab.Mode && gui.m_availableRecipes.All(r => !IdolUpgrade.IsOurs(r.Recipe)) && gui.m_craftTimer >= 0f,
                "UPGRADE clicked during the timer: vanilla rows, the timer goes on");
            var mark = LogWatch.Mark();
            gui.m_craftTimer = 1000f;
            yield return Frames(5);
            c.Check(rig.Has(SilverIdol, 1) == 1 && rig.Has(SilverIdol, 2) == 1 && rig.Has("Silver") == 5 && rig.Has("TrophyWolf") == 5,
                $"the idol upgrade still ends, once: one star gained, its cost used once ({rig.Has(SilverIdol, 1)} plain, {rig.Has(SilverIdol, 2)} one-star, silver {rig.Has("Silver")} of 10, trophies {rig.Has("TrophyWolf")} of 10)");
            c.Check(rig.Inv.ContainsItem(axe) && axe.m_quality == 3 && rig.Has(WoodIdol) == 2 && Logged(mark, LogLevel.Info, "Refinement of") == null,
                $"nothing else is refined or used (axe level {axe.m_quality}, {rig.Has(WoodIdol)} wooden idols)");

            // Refinement started, then IDOLS tab clicked while timer run.
            yield return rig.Select(axe);
            ForgeRefine.TestRoll = Win;
            gui.OnCraftPressed();
            yield return null;
            c.Check(rig.Row >= 0 && gui.m_craftTimer >= 0f && ReferenceEquals(gui.m_craftUpgradeItem, axe), "refinement timer running");
            yield return rig.ClickIdolsTab();
            c.Check(IdolsTab.Mode && gui.m_availableRecipes.Count > 0 && gui.m_availableRecipes.All(r => IdolUpgrade.IsOurs(r.Recipe)) && gui.m_craftTimer >= 0f,
                "IDOLS clicked during the timer: idol rows, the timer goes on");
            mark = LogWatch.Mark();
            gui.m_craftTimer = 1000f;
            yield return Frames(5);
            var rolls = LogWatch.Since(mark).Count(l => l.Text.StartsWith("Refinement of", StringComparison.Ordinal));
            c.Check(rig.Inv.ContainsItem(axe) && axe.m_quality == 4 && rig.Has(WoodIdol) == 1 && rolls == 1,
                $"the refinement still ends, once: one level, one roll, one idol used (level {axe.m_quality}, {rolls} roll(s) logged, {rig.Has(WoodIdol)} wooden idol left of 2)");
            c.Check(rig.Has(SilverIdol, 1) == 1 && rig.Has(SilverIdol, 2) == 1 && rig.Has("Silver") == 5 && rig.Has("TrophyWolf") == 5,
                "no idol of the Idols list is touched, no cost is used");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.timer.loss (T27) ----------

    private static IEnumerator RunTimerLoss()
    {
        var rig = new Rig(TimerLossName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var silverName = NameOf("Silver");
            var woodName = NameOf(WoodIdol);
            var axe = rig.Give("AxeStone", 1, 3);
            rig.Give(SilverIdol, 2, 1);
            rig.Give("Silver", 5);
            rig.Give("TrophyWolf", 5);
            yield return rig.OpenForge();
            yield return rig.ClickIdolsTab();
            var gui = rig.Gui;
            var stack = rig.Find(SilverIdol, 1);
            yield return rig.Select(stack);
            if (axe == null || stack == null || silverName == null || rig.Row < 0)
            {
                c.Check(false, "the plain idol stack is not listed in the Idols tab");
                c.Report();
                yield break;
            }

            // Metal leave inventory during idol upgrade timer.
            gui.OnCraftPressed();
            yield return null;
            c.Check(gui.m_craftTimer >= 0f, "idol upgrade timer running");
            rig.Inv.RemoveItem(silverName, 5, -1, false);
            rig.ClearCenter();
            gui.m_craftTimer = 1000f;
            yield return Frames(3);
            c.Check(rig.Has(SilverIdol, 1) == 2 && rig.Has(SilverIdol, 2) == 0 && rig.Has("TrophyWolf") == 5 && rig.Center == L("$msg_missingrequirement"),
                $"metal gone before the timer ends: no upgrade, nothing else used, message '{rig.Center}'");

            // Idol stack itself leave inventory during timer.
            rig.Give("Silver", 5);
            yield return rig.Select(stack);
            gui.OnCraftPressed();
            yield return null;
            c.Check(rig.Row >= 0 && gui.m_craftTimer >= 0f, "idol upgrade timer running again");
            rig.Inv.RemoveItem(stack);
            rig.ClearCenter();
            gui.m_craftTimer = 1000f;
            yield return Frames(3);
            c.Check(rig.Has(SilverIdol) == 0 && rig.Has("Silver") == 5 && rig.Has("TrophyWolf") == 5 && rig.Center == L("$msg_missingrequirement"),
                $"idol stack gone before the timer ends: nothing is made, nothing used, message '{rig.Center}'");

            // Refinement: idol leave during timer, then the item.
            rig.Give(WoodIdol, 1, 1);
            gui.OnTabUpgradePressed();
            yield return rig.Select(axe);
            ForgeRefine.TestRoll = Win;
            gui.OnCraftPressed();
            yield return null;
            c.Check(rig.Row >= 0 && gui.m_craftTimer >= 0f, "refinement timer running");
            rig.Inv.RemoveItem(woodName, 1, -1, false);
            var mark = LogWatch.Mark();
            gui.m_craftTimer = 1000f;
            yield return Frames(3);
            c.Check(rig.Inv.ContainsItem(axe) && axe.m_quality == 3 && Logged(mark, LogLevel.Info, "Refinement of", "stopped: no idol left.") != null,
                $"idol gone before the timer ends: the item keeps its level ({axe.m_quality}): {Tail(mark)}");

            rig.Give(WoodIdol, 1, 1);
            yield return rig.Select(axe);
            gui.OnCraftPressed();
            yield return null;
            c.Check(rig.Row >= 0 && gui.m_craftTimer >= 0f, "refinement timer running again");
            rig.Inv.RemoveItem(axe);
            mark = LogWatch.Mark();
            gui.m_craftTimer = 1000f;
            yield return Frames(3);
            c.Check(rig.Has(WoodIdol) == 1 && Logged(mark, LogLevel.Info, "Refinement of", "stopped: item no longer in inventory.") != null,
                $"item gone before the timer ends: the idol is not used: {Tail(mark)}");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.rules.bad-material (T24; what the mod say on screen: forge.bug.bad-material-silent) ----------

    private static IEnumerator RunBadMaterial()
    {
        var rig = new Rig(BadMaterialName);
        var c = rig.C;
        try
        {
            rig.Provoke();
            var mark = LogWatch.Mark();
            ServerRules.TestRules = DefaultRules(r => r.TierMaterial[3] = "Silverr");
            var stack = rig.Give(SilverIdol, 1, 1);
            rig.Give("Silver", 5);
            rig.Give("TrophyWolf", 5);
            yield return rig.OpenForge();
            yield return rig.ClickIdolsTab();
            yield return rig.Select(stack);
            if (stack == null || rig.Row < 0)
            {
                c.Check(false, "the plain idol stack is not listed in the Idols tab");
                c.Report();
                yield break;
            }
            var gui = rig.Gui;
            c.Check(!rig.ButtonOn && !rig.RowCanCraft && rig.Has("Silver") == 5, "unknown metal name: the upgrade stays blocked with the cost carried (never free)");
            c.Check(Logged(mark, LogLevel.Warning, "Unknown item name(s)", "Silverr") != null, $"the log warning names the bad metal: {Tail(mark)}");
            gui.OnCraftPressed();
            yield return null;
            c.Check(gui.m_craftTimer < 0f && rig.Has(SilverIdol, 1) == 1 && rig.Has(SilverIdol, 2) == 0 && rig.Has("Silver") == 5 && rig.Has("TrophyWolf") == 5,
                $"unknown metal name: a press starts nothing and uses nothing (timer {gui.m_craftTimer}, {rig.Has(SilverIdol, 2)} one-star idol)");

            mark = LogWatch.Mark();
            ServerRules.TestRules = DefaultRules(r => r.TierMaterial[3] = "");
            yield return rig.Select(stack);
            c.Check(rig.Row >= 0 && !rig.ButtonOn && !rig.RowCanCraft, "empty metal name: the upgrade stays blocked too");
            c.Check(Logged(mark, LogLevel.Warning, "Unknown item name(s)") != null, $"empty metal name: the log has a warning too: {Tail(mark)}");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.bug.bad-material-silent (T24: real bug, fail until mod fixed) ----------

    // Run 2026-10-07: with Material = "Silverr" the panel text has no red note (only "Cost: 5 common trophies of this
    // tier (any mix) and this idol.") and idol tooltip cost line is "5 common trophies of its biome": player see every
    // shown cost met and a dead button. IdolsTab.Description and IdolTooltip.Rewrite skip a tier without metal.
    private static IEnumerator RunBugBadMaterial()
    {
        var rig = new Rig(BugBadMaterialName);
        var c = rig.C;
        try
        {
            rig.Provoke();
            ServerRules.TestRules = DefaultRules(r => r.TierMaterial[3] = "Silverr");
            var stack = rig.Give(SilverIdol, 1, 1);
            rig.Give("Silver", 5);
            rig.Give("TrophyWolf", 5);
            yield return rig.OpenForge();
            yield return rig.ClickIdolsTab();
            yield return rig.Select(stack);
            if (stack == null || rig.Row < 0)
            {
                c.Check(false, "the plain idol stack is not listed in the Idols tab");
                c.Report();
                yield break;
            }
            c.Check(rig.Description.Contains("<color=red>"),
                $"the Idols tab says in red why the upgrade is blocked (no metal set for this tier), panel text is '{rig.Description}'");
            var line = CostLine(L(stack.GetTooltip()));
            c.Check(line != "5 common trophies of its biome", $"the idol's tooltip does not show the cost as trophies only, cost line is '{line}'");

            ServerRules.TestRules = DefaultRules(r => r.TierMaterial[3] = "");
            yield return rig.Select(stack);
            c.Check(rig.Description.Contains("<color=red>"), $"empty metal name: the panel says why in red too ('{rig.Description}')");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.tooltip.zero-cost (T25) ----------

    private static IEnumerator RunZeroCost()
    {
        var c = new Checks(ZeroCostName);
        try
        {
            var item = Prefab(SilverIdol).m_itemData.Clone();
            item.m_quality = 1;
            var silver = L(NameOf("Silver"));
            ServerRules.TestRules = DefaultRules();
            var line = CostLine(L(item.GetTooltip()));
            c.Check(line == $"5 {silver}, 5 common trophies of its biome", $"defaults: metal and trophies, is '{line}'");
            ServerRules.TestRules = DefaultRules(r => r.Material[1] = 0);
            line = CostLine(L(item.GetTooltip()));
            c.Check(line == "5 common trophies of its biome", $"Level1Material = 0: only the trophies, is '{line}'");
            c.Report();
        }
        finally
        {
            ServerRules.TestRules = null;
        }
        yield break;
    }

    // ---------- forge.bug.tooltip-zero-cost (T25: real bug, fail until mod fixed) ----------

    // Run 2026-10-07: Level1Trophies = 0 give cost line "5 Silver, " (comma with nothing after), both costs 0 give an
    // empty "Idols tab: " line. IdolTooltip.Rewrite write ", " right after the metal and trophies only when > 0.
    private static IEnumerator RunBugZeroCost()
    {
        var c = new Checks(BugZeroCostName);
        try
        {
            var item = Prefab(SilverIdol).m_itemData.Clone();
            item.m_quality = 1;
            var silver = L(NameOf("Silver"));
            ServerRules.TestRules = DefaultRules(r => r.Trophies[1] = 0);
            var line = CostLine(L(item.GetTooltip()));
            c.Check(line == "5 " + silver, $"Level1Trophies = 0: the cost line is just '5 {silver}', is '{line}'");
            ServerRules.TestRules = DefaultRules(r =>
            {
                r.Material[1] = 0;
                r.Trophies[1] = 0;
            });
            line = CostLine(L(item.GetTooltip()));
            c.Check(line == null || line.Trim().Length > 0, $"both 0: no 'Idols tab:' line with nothing after it, is '{line}'");
            c.Report();
        }
        finally
        {
            ServerRules.TestRules = null;
        }
        yield break;
    }
}
#endif
