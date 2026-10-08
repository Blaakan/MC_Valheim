#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Debug build only. Me = self tests with other mods. Each one need its MC mod loaded and on (Test-InWorld load every
// MC mod): when it is not, test FAIL and say so, never pass on nothing. Me reach other mod through reflection (no
// reference between mods), so a renamed member there also end as FAIL that say what me could not reach.
//   forge.compat.search          Crafting Search and Sort: filter and sort on the Idols tab, tabs keep working
//   forge.compat.search-forge    Crafting Search and Sort: UPGRADE tab search read the recipe's own idol
//   forge.compat.crossbow        Crossbow Stays Loaded: success = reload needed; failure at level 1 = still loaded
//   forge.compat.crossbow-down   Crossbow Stays Loaded: failure at level 2+ re-make the crossbow = reload needed
//   forge.compat.repair          One Click Repair All: repair pressed on the Idols tab
//   forge.compat.other-forge     stand-in for another Forge mod (foreign prefix that take DoCrafting over)
//   forge.compat.sort-chest      Sort Chest: each idol level keep its own stack, stars stay
//   forge.compat.loot-filter     Loot Pickup Filter: mark and stars on one slot, one entry for every level
//   forge.cleanlog               what this mod say in log while every other forge test ran
//   forge.bug.menu-warning       (real bug, fail until mod fixed) no warning that call existing items unknown
internal static partial class SelfTests
{
    private const string SearchName = "forge.compat.search";
    private const string SearchForgeName = "forge.compat.search-forge";
    private const string CrossbowName = "forge.compat.crossbow";
    private const string CrossbowDownName = "forge.compat.crossbow-down";
    private const string RepairName = "forge.compat.repair";
    private const string OtherForgeName = "forge.compat.other-forge";
    private const string SortChestName = "forge.compat.sort-chest";
    private const string LootFilterName = "forge.compat.loot-filter";
    private const string CleanLogName = "forge.cleanlog";
    private const string BugMenuWarningName = "forge.bug.menu-warning";

    private const string SearchGuid = "MC.UX.Crafting.SearchSort";
    private const string SearchSpace = "MC.UX.CraftingSearchSortMod.";
    private const string CrossbowGuid = "MC.Combat.Crossbow.StaysLoaded";
    private const string RepairGuid = "MC.Crafting.Repair.OneClickAll";
    private const string SortChestGuid = "MC.UX.Container.Sort";
    private const string LootFilterGuid = "MC.UX.AutoPickup.Filter";
    private const string LootFilterSpace = "MC.UX.AutoPickupFilterMod.";
    private const string OtherForgeId = "mc.selftest.other-forge-mod";

    // False (and a failed check) when other MC mod not there or not on.
    private static bool Needs(Checks c, OtherMod mod, string name)
    {
        c.Check(mod.Active, $"{name} ({mod.Guid}) must be installed and on for this check, it is {mod.State}");
        return mod.Active;
    }

    private static Dictionary<string, string> CopyOf(Dictionary<string, string> data) =>
        data != null ? new Dictionary<string, string>(data) : null;

    private static void PutBack(Dictionary<string, string> live, Dictionary<string, string> saved)
    {
        if (live == null || saved == null)
        {
            return;
        }
        live.Clear();
        foreach (var pair in saved)
        {
            live[pair.Key] = pair.Value;
        }
    }

    // Lower case, no spaces: how Crafting Search and Sort compare.
    private static string Squash(string text) => new string((text ?? "").ToLowerInvariant().Where(ch => !char.IsWhiteSpace(ch)).ToArray());

    // A word of `name` (localized) that `other` no hold: typing it find the first and not the second.
    private static string WordOnlyIn(string name, string other, string fallback)
    {
        var rest = Squash(other);
        foreach (var word in (name ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var w = word.ToLowerInvariant();
            if (w.Length >= 3 && !rest.Contains(w))
            {
                return w;
            }
        }
        return fallback;
    }

    private static IEnumerator TypeInto(TMP_InputField field, string text)
    {
        field.text = text;
        // That mod wait 0.1 s after last key, then rebuild the list.
        yield return new WaitForSecondsRealtime(0.6f);
    }

    // ---------- forge.compat.search (C01) ----------

    private static IEnumerator RunSearch()
    {
        var rig = new Rig(SearchName);
        var c = rig.C;
        var data = CopyOf(rig.P.m_customData);
        TMP_InputField field = null;
        MethodInfo choose = null;
        var defaultSort = 0;
        try
        {
            var mod = new OtherMod(SearchGuid);
            if (!Needs(c, mod, "Crafting Search and Sort"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = DefaultRules();
            var search = mod.Type(SearchSpace + "CraftSearch");
            var category = mod.Type(SearchSpace + "RecipeCategory");
            choose = Method(search, "ChooseOption", typeof(int));
            var byName = GetStatic(category, "Name") as int?;
            defaultSort = GetStatic(category, "Default") as int? ?? 0;
            StoneAxeReady(rig);
            var axe = rig.Give("AxeStone", 1, 3);
            rig.Give(WoodIdol, 1, 1);
            rig.Give(SilverIdol, 1, 1);
            rig.Give(SilverIdol, 1, 2);
            yield return rig.OpenForge();
            yield return rig.ClickIdolsTab();
            var gui = rig.Gui;
            field = GetStatic(mod.Type(SearchSpace + "SearchUi"), "Field") as TMP_InputField;
            c.Check(field != null && choose != null && byName.HasValue, $"Crafting Search and Sort's search field and sort are reachable (field {field != null}, sort {choose != null})");
            if (field == null || choose == null || !byName.HasValue)
            {
                c.Report();
                yield break;
            }
            var silverName = NameOf(SilverIdol);
            var woodName = NameOf(WoodIdol);
            c.Check(gui.m_availableRecipes.Count == 3 && gui.m_availableRecipes.All(r => IdolUpgrade.IsOurs(r.Recipe)), $"Idols tab: 3 idol rows before typing ({gui.m_availableRecipes.Count})");

            // Part of the silver idol's name.
            var term = WordOnlyIn(L(silverName), L(woodName) + " " + WoodIdol, "upgrader3");
            yield return TypeInto(field, term);
            c.Check(GetStatic(search, "Term") as string == Squash(term), $"the mod took the typed text '{term}'");
            c.Check(gui.m_availableRecipes.Count == 2 && gui.m_availableRecipes.All(r => IdolUpgrade.IsOurs(r.Recipe) && r.ItemData != null && r.ItemData.m_shared.m_name == silverName),
                $"typing '{term}' keeps only the two silver idol rows ({gui.m_availableRecipes.Count} rows)");
            c.Check(IdolsTab.Mode && rig.ButtonLabel == "Upgrade idol", "the Idols panel still works on the filtered list");
            yield return TypeInto(field, "");
            c.Check(gui.m_availableRecipes.Count == 3, $"filter cleared: 3 rows again ({gui.m_availableRecipes.Count})");

            // Sort by name.
            choose.Invoke(null, new object[] { byName.Value });
            yield return Frames(3);
            var names = gui.m_availableRecipes.Select(r => L(r.Recipe.m_item.m_itemData.m_shared.m_name)).ToList();
            var sorted = names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            c.Check(names.Count == 3 && names.SequenceEqual(sorted) && gui.m_availableRecipes.All(r => IdolUpgrade.IsOurs(r.Recipe)),
                $"sort by name: the idol rows are in name order ({string.Join(", ", names.ToArray())})");
            c.Check(RowProblem(rig, rig.Find(SilverIdol, 2), 1) == null, $"sorted rows keep their stars ({RowProblem(rig, rig.Find(SilverIdol, 2), 1)})");

            // Tabs keep working.
            gui.OnTabUpgradePressed();
            yield return Frames(3);
            c.Check(!IdolsTab.Mode && gui.m_availableRecipes.All(r => !IdolUpgrade.IsOurs(r.Recipe)) && RowOf(gui, axe) >= 0, "UPGRADE tab: vanilla rows, the Stone axe is listed");
            yield return rig.ClickIdolsTab();
            c.Check(IdolsTab.Mode && gui.m_availableRecipes.Count == 3 && gui.m_availableRecipes.All(r => IdolUpgrade.IsOurs(r.Recipe)), "IDOLS tab again: the idol rows");
            yield return TypeInto(field, term);
            c.Check(gui.m_availableRecipes.Count == 2, $"and the filter still works after the tab switch ({gui.m_availableRecipes.Count} rows)");
            c.Report();
        }
        finally
        {
            if (field != null)
            {
                field.text = "";
            }
            if (choose != null)
            {
                choose.Invoke(null, new object[] { defaultSort });
            }
            PutBack(rig.P.m_customData, data);
            rig.Dispose();
        }
    }

    // ---------- forge.compat.search-forge (C08) ----------

    private static IEnumerator RunSearchForge()
    {
        var rig = new Rig(SearchForgeName);
        var c = rig.C;
        TMP_InputField field = null;
        try
        {
            var mod = new OtherMod(SearchGuid);
            if (!Needs(c, mod, "Crafting Search and Sort") || !StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = DefaultRules();
            var axe = rig.Give("AxeStone", 1, 6);
            rig.Give(BronzeIdol, 1, 1);
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            var gui = rig.Gui;
            field = GetStatic(mod.Type(SearchSpace + "SearchUi"), "Field") as TMP_InputField;
            c.Check(field != null, "Crafting Search and Sort's search field is reachable");
            if (field == null || axe == null)
            {
                c.Report();
                yield break;
            }
            var wood = L(NameOf(WoodIdol));
            var bronze = L(NameOf(BronzeIdol));
            c.Check(rig.Row >= 0 && rig.SlotName(0) == bronze, $"level 6 Stone axe: the requirement shows the Bronze idol, is '{rig.SlotName(0)}'");
            var others = L(axe.m_shared.m_name) + " AxeStone";
            var woodWord = WordOnlyIn(wood, bronze + " " + others, null);
            var bronzeWord = WordOnlyIn(bronze, wood + " " + others, null);
            c.Check(woodWord != null && bronzeWord != null, $"a word of each idol name to type ('{woodWord}', '{bronzeWord}')");
            if (woodWord == null || bronzeWord == null)
            {
                c.Report();
                yield break;
            }
            yield return TypeInto(field, woodWord);
            c.Check(RowOf(gui, axe) >= 0, $"typing '{woodWord}' keeps the axe (the search reads the idol the game asks for)");
            yield return TypeInto(field, bronzeWord);
            c.Check(RowOf(gui, axe) < 0, $"typing '{bronzeWord}' hides it (known limitation)");
            yield return TypeInto(field, "");
            c.Check(RowOf(gui, axe) >= 0, "filter cleared: the axe is listed again");
            c.Report();
        }
        finally
        {
            if (field != null)
            {
                field.text = "";
            }
            rig.Dispose();
        }
    }

    // ---------- forge.compat.crossbow (C02) and forge.compat.crossbow-down (C09) ----------

    private sealed class Crossbow
    {
        internal const string Prefab = "CrossbowArbalest";
        internal string Idol;
        internal ItemDrop.ItemData Bow;
        private MethodInfo _mark;
        private MethodInfo _loaded;

        // False = not usable (a check failed and say why).
        internal bool Setup(Rig rig, int level, int idols)
        {
            var mod = new OtherMod(CrossbowGuid);
            if (!Needs(rig.C, mod, "Crossbow Stays Loaded"))
            {
                return false;
            }
            var state = mod.Type("MC.Combat.CrossbowStaysLoadedMod.LoadedState");
            _mark = Method(state, "Mark", typeof(ItemDrop.ItemData));
            _loaded = Method(state, "IsLoaded", typeof(ItemDrop.ItemData));
            Idol = OwnIdolOf(Prefab);
            rig.C.Check(_mark != null && _loaded != null && Idol != null,
                $"Crossbow Stays Loaded's loaded mark is reachable and the Arbalest has an idol recipe (mark {_mark != null}, idol '{Idol}')");
            if (_mark == null || _loaded == null || Idol == null)
            {
                return false;
            }
            rig.Teach(Prefab);
            Bow = rig.Give(Prefab, 1, level);
            rig.Give(Idol, idols, 1);
            return Bow != null;
        }

        // Like a loaded crossbow put away: that mod's mark on the item at its durability now.
        internal void Load() => _mark.Invoke(null, new object[] { Bow });

        internal bool Loaded => (bool)_loaded.Invoke(null, new object[] { Bow });
    }

    private static IEnumerator RunCrossbow()
    {
        var rig = new Rig(CrossbowName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            var x = new Crossbow();
            if (!x.Setup(rig, 1, 2))
            {
                c.Report();
                yield break;
            }
            SelfTest.Note(CrossbowName, $"{Crossbow.Prefab}: idol {x.Idol}, durability {x.Bow.m_shared.m_maxDurability} + {x.Bow.m_shared.m_durabilityPerLevel} per level");
            yield return rig.OpenForge();

            x.Load();
            c.Check(x.Loaded, "a loaded crossbow (mark set)");
            yield return rig.Select(x.Bow);
            c.Check(rig.Row >= 0 && rig.CraftText.EndsWith("A failure costs only the idol.", StringComparison.Ordinal), $"level 1 crossbow at the Forge, text '{rig.CraftText}'");
            ForgeRefine.TestRoll = Fail;
            yield return rig.Press();
            c.Check(x.Bow.m_quality == 1 && rig.Inv.ContainsItem(x.Bow) && x.Loaded, "failure (level 1, nothing changes on the item): it stays loaded");

            x.Load();
            yield return rig.Select(x.Bow);
            ForgeRefine.TestRoll = Win;
            yield return rig.Press();
            c.Check(x.Bow.m_quality == 2 && Mathf.Approximately(x.Bow.m_durability, x.Bow.GetMaxDurability()) && !x.Loaded,
                $"success: level 2 at full durability, it needs a reload (loaded {x.Loaded})");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    private static IEnumerator RunCrossbowDown()
    {
        var rig = new Rig(CrossbowDownName);
        var c = rig.C;
        try
        {
            ServerRules.TestRules = DefaultRules();
            var x = new Crossbow();
            if (!x.Setup(rig, 3, 3))
            {
                c.Report();
                yield break;
            }
            yield return rig.OpenForge();

            // Full durability, level 3 -> 2.
            x.Load();
            yield return rig.Select(x.Bow);
            c.Check(rig.Row >= 0 && x.Loaded, "a loaded level 3 crossbow at the Forge");
            ForgeRefine.TestRoll = Fail;
            yield return rig.Press();
            c.Check(x.Bow.m_quality == 2 && Mathf.Approximately(x.Bow.m_durability, x.Bow.GetMaxDurability()) && !x.Loaded,
                $"failure at level 3: one level lower at full durability, it needs a reload (level {x.Bow.m_quality}, loaded {x.Loaded})");

            // Damaged, level 3 -> 2.
            x.Bow.m_quality = 3;
            x.Bow.m_durability = x.Bow.GetMaxDurability() * 0.5f;
            x.Load();
            yield return rig.Select(x.Bow);
            ForgeRefine.TestRoll = Fail;
            yield return rig.Press();
            c.Check(x.Bow.m_quality == 2 && !x.Loaded, $"failure at level 3 with a damaged crossbow: it needs a reload too (loaded {x.Loaded})");

            // OnFailure = Destroy: gone.
            ServerRules.TestRules = DefaultRules(r => r.Failure = FailureMode.Destroy);
            x.Load();
            yield return rig.Select(x.Bow);
            ForgeRefine.TestRoll = Fail;
            yield return rig.Press();
            c.Check(!rig.Inv.ContainsItem(x.Bow) && rig.Has(Crossbow.Prefab) == 0, "OnFailure = Destroy: the crossbow is gone");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.compat.repair (C03) ----------

    private static IEnumerator RunRepair()
    {
        var rig = new Rig(RepairName);
        var c = rig.C;
        try
        {
            var mod = new OtherMod(RepairGuid);
            if (!Needs(c, mod, "One Click Repair All"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = DefaultRules();
            var axe = rig.Give("AxeStone", 1, 1);
            var club = rig.Give("Club", 1, 1);
            var stack = rig.Give(SilverIdol, 1, 1);
            if (axe == null || club == null || stack == null)
            {
                c.Report();
                yield break;
            }
            axe.m_durability = axe.GetMaxDurability() * 0.5f;
            club.m_durability = club.GetMaxDurability() * 0.5f;
            yield return rig.OpenForge();
            yield return rig.ClickIdolsTab();
            yield return rig.Select(stack);
            var gui = rig.Gui;
            var tab = rig.IdolsTabButton();
            c.Check(IdolsTab.Mode && rig.Row >= 0 && IdolUpgrade.IsOurs(gui.m_selectedRecipe.Recipe), "Idols tab open, an idol row selected");
            c.Check(rig.Forge.Station.m_canRepair && gui.m_repairButton.gameObject.activeSelf, "the Forge of Potential shows the repair button");

            // Vanilla repair an item at its own station only; no-cost mode repair anywhere.
            var here = gui.CanRepair(axe) && gui.CanRepair(club);
            if (!here)
            {
                SelfTest.Note(RepairName, "vanilla does not repair a Stone axe or a Club at the Forge of Potential: no-cost mode on for the press");
                rig.P.m_noPlacementCost = true;
            }
            gui.OnRepairPressed();
            yield return Frames(2);
            rig.P.m_noPlacementCost = false;
            yield return Frames(2);
            c.Check(Mathf.Approximately(axe.m_durability, axe.GetMaxDurability()) && Mathf.Approximately(club.m_durability, club.GetMaxDurability()),
                $"one press of repair fixes both worn items (axe {axe.m_durability}/{axe.GetMaxDurability()}, club {club.m_durability}/{club.GetMaxDurability()})");
            c.Check(IdolsTab.Mode && tab != null && !tab.interactable && gui.m_tabUpgrade.interactable, "the Idols tab stays selected");
            c.Check(gui.m_availableRecipes.Count > 0 && gui.m_availableRecipes.All(r => IdolUpgrade.IsOurs(r.Recipe)) && IdolUpgrade.IsOurs(gui.m_selectedRecipe.Recipe)
                    && ReferenceEquals(gui.m_selectedRecipe.ItemData, stack), "the list still shows the idol rows, the same idol selected");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.compat.other-forge (C04) ----------

    private static int _foreignRolls;

    // How another Forge mod's patch look to this mod: prefix with bool result on InventoryGui.DoCrafting that take a
    // refinement over (vanilla skipped). It do nothing to the item: me only test how this mod react.
    private static bool ForeignDoCrafting(InventoryGui __instance)
    {
        if (__instance.m_craftRecipe != null && __instance.m_craftUpgradeItem != null)
        {
            _foreignRolls++;
            return false;
        }
        return true;
    }

    private static List<string> ForeignOwners()
    {
        var owners = new List<string>();
        var info = Harmony.GetPatchInfo(AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.DoCrafting)));
        if (info == null)
        {
            return owners;
        }
        owners.AddRange(info.Transpilers.Select(p => p.owner));
        owners.AddRange(info.Prefixes.Where(p => p.PatchMethod != null && p.PatchMethod.ReturnType == typeof(bool)).Select(p => p.owner));
        return owners.Where(o => o != ModInfo.Guid).Distinct().ToList();
    }

    private static IEnumerator RunOtherForge()
    {
        var rig = new Rig(OtherForgeName);
        var c = rig.C;
        var harmony = new Harmony(OtherForgeId);
        try
        {
            ServerRules.TestRules = DefaultRules();
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var real = ForeignOwners();
            if (real.Count > 0)
            {
                SelfTest.Note(OtherForgeName, $"a real mod already takes DoCrafting over: {string.Join(", ", real.ToArray())}");
            }
            var wood = L(NameOf(WoodIdol));
            var axe = rig.Give("AxeStone", 1, 6);
            rig.Give(WoodIdol, 2, 1);
            rig.Give(SilverIdol, 2, 1);
            rig.Give("Silver", 5);
            rig.Give("TrophyWolf", 5);

            rig.Provoke();
            _foreignRolls = 0;
            harmony.Patch(AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.DoCrafting)),
                prefix: new HarmonyMethod(typeof(SelfTests).GetMethod(nameof(ForeignDoCrafting), BindingFlags.Static | BindingFlags.NonPublic)));
            ForgeGuard.Reset();
            var mark = LogWatch.Mark();
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            var gui = rig.Gui;
            c.Check(Logged(mark, LogLevel.Warning, "Another mod changes crafting or the Forge of Potential", OtherForgeId) != null,
                $"opening the Forge: a warning in the log names that mod: {Tail(mark)}");
            c.Check(Logged(mark, LogLevel.Warning, "the higher idols at high levels", "are off on this game", OtherForgeId) != null,
                "a second warning says the higher idols at high levels are off on this game");
            c.Check(rig.Row >= 0 && rig.SlotName(0) == wood && rig.ButtonOn && !rig.CraftText.Contains("From level"),
                $"a level 6 Stone axe asks for its own Wooden idol, no Bronze ('{rig.SlotName(0)}', '{rig.CraftText}')");

            mark = LogWatch.Mark();
            ForgeRefine.TestRoll = Win;
            yield return rig.Press();
            c.Check(_foreignRolls == 1 && Logged(mark, LogLevel.Info, "Refinement of") == null && axe.m_quality == 6 && rig.Has(WoodIdol) == 2,
                $"a refinement is rolled once, by the other mod: this mod steps aside ({_foreignRolls} foreign roll(s), axe level {axe.m_quality}, {rig.Has(WoodIdol)} idols)");

            yield return rig.ClickIdolsTab();
            var stack = rig.Find(SilverIdol, 1);
            yield return rig.Select(stack);
            c.Check(rig.Row >= 0 && rig.ButtonOn, "the Idols tab still offers the idol upgrade");
            yield return rig.Press();
            c.Check(rig.Has(SilverIdol, 1) == 1 && rig.Has(SilverIdol, 2) == 1 && rig.Has("Silver") == 0 && rig.Has("TrophyWolf") == 0,
                $"an idol upgrade works and no idol disappears ({rig.Has(SilverIdol, 1)} plain, {rig.Has(SilverIdol, 2)} one-star)");
            c.Check(_foreignRolls == 1 && gui.m_craftRecipe == null && gui.m_craftUpgradeItem == null, "the other mod was given nothing of the idol upgrade to work on");

            // Stand-in gone: tier rule back.
            harmony.UnpatchSelf();
            ForgeGuard.Reset();
            gui.OnTabUpgradePressed();
            yield return rig.Select(axe);
            if (real.Count == 0)
            {
                c.Check(rig.Row >= 0 && rig.SlotName(0) == L(NameOf(BronzeIdol)), $"without the other mod the level 6 axe asks for Bronze again, is '{rig.SlotName(0)}'");
            }
            c.Report();
        }
        finally
        {
            harmony.UnpatchSelf();
            ForgeGuard.Reset();
            rig.Dispose();
        }
    }

    // ---------- forge.compat.sort-chest (C05) ----------

    private static IEnumerator RunSortChest()
    {
        var rig = new Rig(SortChestName);
        var c = rig.C;
        try
        {
            var mod = new OtherMod(SortChestGuid);
            if (!Needs(c, mod, "Sort Chest"))
            {
                c.Report();
                yield break;
            }
            var sort = Method(mod.Type("MC.UX.ContainerSortMod.ContainerSorter"), "TrySortOpenContainer");
            c.Check(sort != null, "Sort Chest's sort (what its button calls) is reachable");
            var chest = rig.SpawnChest();
            yield return new WaitForSeconds(0.5f);
            if (sort == null || chest == null || chest.GetInventory() == null)
            {
                c.Report();
                yield break;
            }
            var gui = rig.Gui;
            var box = chest.GetInventory();
            var name = NameOf(SilverIdol);
            var plainIcon = PlainIconOf(SilverIdol);
            Put(box, SilverIdol, 1, 1, 4, 1);
            Put(box, SilverIdol, 3, 1, 0, 0);
            Put(box, SilverIdol, 1, 1, 2, 0);
            Put(box, SilverIdol, 2, 1, 1, 1);
            yield return rig.OpenInventory(chest);
            c.Check(ReferenceEquals(gui.m_currentContainer, chest) && box.NrOfItems() == 4, $"chest open with 2 plain stacks, a 1-star and a 2-star idol ({box.NrOfItems()} stacks)");
            sort.Invoke(null, null);
            yield return Frames(3);
            c.Check(box.CountItems(name, 1, false) == 2 && box.CountItems(name, 2, false) == 1 && box.CountItems(name, 3, false) == 1,
                $"sorted: every idol keeps its level ({box.CountItems(name, 1, false)}/{box.CountItems(name, 2, false)}/{box.CountItems(name, 3, false)})");
            c.Check(Stacks(box, name, 1) == 1 && Stacks(box, name, 2) == 1 && Stacks(box, name, 3) == 1 && box.NrOfItems() == 3,
                $"the two plain stacks merged (stack merging on), each level in its own stack ({box.NrOfItems()} stacks, {Stacks(box, name, 1)} plain stack(s))");
            foreach (var item in box.GetAllItems())
            {
                var level = IdolLevels.Of(item);
                var slot = rig.Element(gui.ContainerGrid, item);
                var want = level > 0 ? StarIcons.Get(plainIcon, level) : plainIcon;
                c.Check(slot != null && slot.m_icon.enabled && slot.m_icon.sprite == want && !slot.m_quality.enabled,
                    $"after the sort the {level}-star stack shows its stars: {Describe(slot != null ? slot.m_icon.sprite : null)}");
            }
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.compat.loot-filter (C06) ----------

    private static IEnumerator RunLootFilter()
    {
        var rig = new Rig(LootFilterName);
        var c = rig.C;
        var data = CopyOf(rig.P.m_customData);
        FieldInfo modeField = null;
        MethodInfo toggle = null, setMode = null;
        HashSet<string> ignored = null;
        object oldMode = null;
        var added = false;
        try
        {
            var mod = new OtherMod(LootFilterGuid);
            if (!Needs(c, mod, "Loot Pickup Filter"))
            {
                c.Report();
                yield break;
            }
            var state = mod.Type(LootFilterSpace + "FilterState");
            var modeType = mod.Type(LootFilterSpace + "FilterMode");
            modeField = state?.GetField("Mode", AnyStatic);
            ignored = GetStatic(state, "Ignored") as HashSet<string>;
            toggle = Method(state, "Toggle", typeof(HashSet<string>), typeof(string));
            setMode = modeType != null ? Method(state, "SetMode", modeType) : null;
            var isMarked = Method(state, "IsMarked", typeof(ItemDrop.ItemData));
            var listBlocks = Method(state, "ListBlocks", typeof(ItemDrop));
            var ensure = Method(state, "EnsureLocal");
            var ok = modeField != null && ignored != null && toggle != null && setMode != null && isMarked != null && listBlocks != null && ensure != null;
            c.Check(ok, "Loot Pickup Filter's filter (mode, Ignored list, mark, verdict) is reachable");
            if (!ok)
            {
                c.Report();
                yield break;
            }
            ensure.Invoke(null, null);
            oldMode = modeField.GetValue(null);
            var plainIcon = PlainIconOf(SilverIdol);
            var plain = rig.Give(SilverIdol, 1, 1);
            var star = rig.Give(SilverIdol, 1, 3);
            var other = rig.Give(WoodIdol, 1, 2);

            // Player mark the idol: Skip ignored mode, one entry in Ignored list.
            setMode.Invoke(null, new[] { Enum.Parse(modeType, "SkipIgnored") });
            if (!ignored.Contains(SilverIdol))
            {
                toggle.Invoke(null, new object[] { ignored, SilverIdol });
                added = true;
            }
            c.Check(ignored.Count(k => k == SilverIdol) == 1 && !ignored.Contains(WoodIdol), "one filter entry for the idol");
            c.Check((bool)isMarked.Invoke(null, new object[] { plain }) && (bool)isMarked.Invoke(null, new object[] { star })
                    && !(bool)isMarked.Invoke(null, new object[] { other }), "that entry marks the plain and the starred idol, not another kind of idol");
            var onGround = rig.Drop(SilverIdol, 1);
            var starOnGround = rig.Drop(SilverIdol, 3, 1, 4f);
            yield return null;
            c.Check(onGround != null && starOnGround != null && (bool)listBlocks.Invoke(null, new object[] { onGround })
                    && (bool)listBlocks.Invoke(null, new object[] { starOnGround }), "auto pickup skips that idol at every level (one entry covers them all)");

            yield return rig.OpenInventory();
            foreach (var pair in new[] { new KeyValuePair<ItemDrop.ItemData, int>(star, 2), new KeyValuePair<ItemDrop.ItemData, int>(plain, 0) })
            {
                var slot = rig.Element(rig.Gui.m_playerGrid, pair.Key);
                var badge = slot != null ? slot.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "MC_LootFilterMark") : null;
                var want = pair.Value > 0 ? StarIcons.Get(plainIcon, pair.Value) : plainIcon;
                c.Check(badge != null && badge.enabled && badge.gameObject.activeInHierarchy && badge.sprite != null,
                    $"the filter mark shows on the {pair.Value}-star idol's slot");
                c.Check(slot != null && slot.m_icon.enabled && slot.m_icon.sprite == want,
                    $"and the slot shows {pair.Value} star(s) with it: {Describe(slot != null ? slot.m_icon.sprite : null)}");
            }
            SelfTest.Screenshot(LootFilterName, "mark-and-stars");
            yield return null;
            yield return null;
            c.Report();
        }
        finally
        {
            // Filter of test character back like before (lists, mode, saved character data).
            try
            {
                if (added && toggle != null && ignored != null && ignored.Contains(SilverIdol))
                {
                    toggle.Invoke(null, new object[] { ignored, SilverIdol });
                }
                if (setMode != null && oldMode != null)
                {
                    setMode.Invoke(null, new[] { oldMode });
                }
            }
            finally
            {
                PutBack(rig.P.m_customData, data);
                rig.Dispose();
            }
        }
    }

    // ---------- forge.cleanlog (T17) ----------

    private const string UnknownHead = "Unknown item name(s) in the idol settings, ignored: ";

    // Names an "Unknown item name(s)" warning listed (IdolCatalog.Build: names joined with ", ", then a dot).
    private static string[] UnknownNames(string warning)
    {
        var at = warning.IndexOf(UnknownHead, StringComparison.Ordinal);
        if (at < 0)
        {
            return new string[0];
        }
        var rest = warning.Substring(at + UnknownHead.Length);
        var end = rest.IndexOf('\n');
        if (end >= 0)
        {
            rest = rest.Substring(0, end);
        }
        rest = rest.TrimEnd();
        if (rest.EndsWith(".", StringComparison.Ordinal))
        {
            rest = rest.Substring(0, rest.Length - 1);
        }
        return rest.Split(new[] { ", " }, StringSplitOptions.None).Select(n => n.Trim()).ToArray();
    }

    // "Unknown item name(s)" warnings no test asked for, since the game started.
    private static List<LogLine> UnknownWarnings() =>
        LogWatch.Since(0).Where(l => !l.Provoked && l.Text.Contains(UnknownHead)).ToList();

    // Me run last. Log watch heard every line of this mod since plugin load (and error lines of other log sources
    // with this mod's code in their stack). With settings in force (no test rules) there must be no error line and
    // no "Unknown item name(s)" warning about a name the game really does not have. Lines a test made on purpose
    // (typo rules, stand-in Forge mod) no count, a failed self test's own line neither. Warning that call items the
    // game HAS unknown = the mod's own mistake: forge.bug.menu-warning count those.
    private static IEnumerator RunCleanLog()
    {
        var c = new Checks(CleanLogName);
        string[] needed =
        {
            IdolsName, RefineName, TiersName, TooltipName, IconsName, PopupName, TabName, CostName, StepsName, MissingName, GridName, StacksName,
            OddsName, FailureName, SuccessName, ChoiceName, SpaceName, TooltipTextName,
        };
        var missing = needed.Where(n => !Ran.Contains(n)).ToArray();
        c.Check(missing.Length == 0, $"the tests that play through T01-T12 ran before this one in this game session (not run: {string.Join(", ", missing)})");
        c.Check(LogWatch.Installed, "the log watch was listening");
        var lines = LogWatch.Since(0);
        c.Check(lines.Any(l => l.Text.Contains(Log.ReadyMarker)), $"the log watch heard the mod from its start (its ready line is among the {lines.Count} lines)");
        var errors = lines.Where(l => (l.Level & (LogLevel.Error | LogLevel.Fatal)) != 0 && !l.Text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal)).ToList();
        c.Check(errors.Count == 0, $"no error line from {ModInfo.Name} ({errors.Count} found, first: {(errors.Count > 0 ? errors[0].Text.Split('\n')[0] : "")})");
        var reallyUnknown = UnknownWarnings().SelectMany(l => UnknownNames(l.Text)).Where(n => Prefab(n) == null).Distinct().ToList();
        c.Check(reallyUnknown.Count == 0,
            $"no 'Unknown item name(s)' warning about a name this game does not have, with the settings in force ({reallyUnknown.Count} name(s): {string.Join(", ", reallyUnknown.Take(8).ToArray())})");
        SelfTest.Note(CleanLogName, $"{lines.Count} lines heard from {ModInfo.Name}, {lines.Count(l => l.Level == LogLevel.Warning)} warnings "
                                    + $"({lines.Count(l => l.Level == LogLevel.Warning && l.Provoked)} provoked by tests)");
        c.Report();
        yield break;
    }

    // ---------- forge.bug.menu-warning (T17, T29: real bug, fail until mod fixed) ----------

    // Run 2026-10-07: at game start (and in multiplayer runs at every return to the main menu) the mod logged one
    // "Unknown item name(s) in the idol settings, ignored: Upgrader0Weapon, Upgrader0Armor, Wood, TrophyDeer ..."
    // naming every default item. All those items exist. IdolCatalog.Ensure only wait while the item database is
    // empty; the main menu's database hold a few items of other mods and none of the game's, so the table get built
    // against it and every name look unknown. Me fail when any warning since game start named an item the game has.
    private static IEnumerator RunBugMenuWarning()
    {
        var c = new Checks(BugMenuWarningName);
        c.Check(LogWatch.Installed, "the log watch was listening");
        var wrong = new List<string>();
        var names = new List<string>();
        foreach (var line in UnknownWarnings())
        {
            var known = UnknownNames(line.Text).Where(n => n.Length > 0 && Prefab(n) != null).ToList();
            if (known.Count > 0)
            {
                wrong.Add(line.Text);
                names.AddRange(known);
            }
        }
        names = names.Distinct().ToList();
        c.Check(wrong.Count == 0,
            $"no 'Unknown item name(s)' warning names an item the game has ({wrong.Count} such warning(s) since the game started, {names.Count} existing item(s) called unknown: "
            + $"{string.Join(", ", names.Take(6).ToArray())}{(names.Count > 6 ? ", ..." : "")})");
        c.Report();
        yield break;
    }
}
#endif
