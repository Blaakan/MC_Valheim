#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.CultivatorReplantMod;

// Debug build only. Self tests at the real Forge and in the real inventory windows:
//   replant.forge.low   T08, T10: craft a cultivator, upgrade to 2 and 3 for the vanilla cost; level 4 asks only black
//                       metal and linen thread and Forge level 4 (red at level 3), preview lines, upgrade, gem slot
//   replant.forge.high  T12, T17, T18, T28: upgrades to 5, 6 and 7 for their one material, durability, gem, level 7 not
//                       listed any more
//   replant.settings    T26 a-c: cost setting changed while the Upgrade tab is open (waited, not forced), cost with no
//                       item = warnings and level cap
//   replant.potential   T27: Forge of Potential lists the axe, no cultivator of any level, no "no upgrader" warning
//   replant.tooltip     T19, T21: tier lines of every level, transplant item text, weight, no eating, stacks, portal,
//                       drop and pick up
//   replant.slots       T20, T36, T37: gem and level number, sprout and stack amount in inventory, chest and hotbar;
//                       gem in the top-left pickup line
//   replant.gems        T10, T12, T17, T18, T20: gem colour per level (dark grey, cyan, orange, crimson), upper-right
internal static partial class SelfTests
{
    private const string ForgeLowName = "replant.forge.low";
    private const string ForgeHighName = "replant.forge.high";
    private const string SettingsName = "replant.settings";
    private const string PotentialName = "replant.potential";
    private const string TooltipName = "replant.tooltip";
    private const string SlotsName = "replant.slots";
    private const string GemsName = "replant.gems";

    private static readonly string[] TierTitles =
    {
        "", "Bronze cultivator", "Bronze cultivator", "Bronze cultivator", "Black metal cultivator", "Eitr cultivator",
        "Flametal cultivator", "Bloodgold cultivator",
    };

    private const string BronzePlants = "Dandelion, Thistle, Mushroom, Yellow mushroom, Raspberry bush, Blueberry bush";

    private static readonly string[] NewPlants = { "", "", "", "", "Cloudberry bush", "Yggdrasil shoot", "Fiddlehead, Smoke puff", "Lingonberry bush" };

    private static string PlantsOf(int quality)
    {
        var text = BronzePlants;
        for (var q = 4; q <= quality && q < NewPlants.Length; q++)
        {
            text += ", " + NewPlants[q];
        }
        return text;
    }

    private static string Orange(string text) => "<color=orange>" + text + "</color>";

    // Same rows, any order.
    private static bool SameRows(string got, string want)
    {
        var a = got.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries).OrderBy(s => s, StringComparer.Ordinal);
        var b = want.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries).OrderBy(s => s, StringComparer.Ordinal);
        return a.SequenceEqual(b);
    }

    private static void RefreshList() => InventoryGui.instance.UpdateCraftingPanel();

    private static InventoryElement SlotOf(InventoryGrid grid, Inventory inv, ItemDrop.ItemData item)
    {
        return grid != null && item != null ? grid.GetElement(item.m_gridPos.x, item.m_gridPos.y, inv.GetWidth()) : null;
    }

    private static string SlotText(InventoryElement el)
    {
        if (el == null)
        {
            return "no slot";
        }
        return $"number {(el.m_quality.enabled ? "'" + el.m_quality.text + "'" : "hidden")}, amount {(el.m_amount.enabled ? "'" + el.m_amount.text + "'" : "hidden")}, painted icon {IconPainter.IsPainted(el.m_icon.sprite)}";
    }

    private static Sprite VanillaCultivatorIcon()
    {
        var drop = CultivatorTiers.CultivatorDrop;
        return drop != null ? drop.m_itemData.m_shared.m_icons[0] : null;
    }

    private static bool GemSlot(InventoryElement el, int quality)
    {
        var vanilla = VanillaCultivatorIcon();
        return el != null && vanilla != null && !el.m_quality.enabled && ReferenceEquals(el.m_icon.sprite, TierIcons.CultivatorIcon(vanilla, quality));
    }

    private static bool NumberSlot(InventoryElement el, int quality)
    {
        return el != null && el.m_quality.enabled && el.m_quality.text == quality.ToString(CultureInfo.InvariantCulture)
               && ReferenceEquals(el.m_icon.sprite, VanillaCultivatorIcon());
    }

    // One upgrade (or craft, item null) in the open crafting window: row there and possible, panel rows, station level
    // shown, then Craft. Returns through result the new item (same slot for an upgrade).
    private sealed class Crafted
    {
        internal ItemDrop.ItemData Item;
    }

    private static IEnumerator CraftStep(Rig rig, Checks c, Recipe recipe, ItemDrop.ItemData item, int toQuality, string wantRows, Crafted result)
    {
        var gui = InventoryGui.instance;
        var what = item == null ? "craft" : $"level {toQuality - 1} -> {toQuality}";
        result.Item = null;
        RefreshList();
        yield return null;
        var row = RecipeRow(recipe, item);
        if (!c.Check(row >= 0, $"{what}: listed ({gui.m_availableRecipes.Count} rows)"))
        {
            yield break;
        }
        c.Check(gui.m_availableRecipes[row].CanCraft, $"{what}: can be made");
        gui.SetRecipe(row, false);
        yield return Frames(3);
        var rows = RequirementRows();
        c.Check(SameRows(rows, wantRows), $"{what}: asks '{rows}' (want '{wantRows}')");
        c.Check(gui.m_minStationLevelIcon.gameObject.activeSelf && gui.m_minStationLevelText.text == toQuality.ToString(CultureInfo.InvariantCulture),
            $"{what}: Forge level {gui.m_minStationLevelText.text} shown (want {toQuality})");
        var pos = item != null ? item.m_gridPos : new Vector2i(-1, -1);
        var before = new HashSet<ItemDrop.ItemData>(rig.Inv.GetAllItems());
        yield return CraftRow(row);
        if (item != null)
        {
            var now = rig.Inv.GetItemAt(pos.x, pos.y);
            if (c.Check(!rig.Inv.ContainsItem(item) && now != null && CultivatorTiers.IsCultivator(now) && now.m_quality == toQuality,
                    $"{what}: level {toQuality} cultivator in the same slot ({(now != null ? "quality " + now.m_quality : "empty slot")})"))
            {
                result.Item = now;
            }
        }
        else
        {
            var made = rig.Inv.GetAllItems().FirstOrDefault(i => !before.Contains(i) && CultivatorTiers.IsCultivator(i));
            if (c.Check(made != null && made.m_quality == 1, "craft: a new level 1 cultivator"))
            {
                result.Item = made;
            }
        }
    }

    // ---------- replant.forge.low (T08, T10) ----------

    private static IEnumerator RunForgeLow()
    {
        if (!WorldReady(ForgeLowName))
        {
            yield break;
        }
        var c = new Checks(ForgeLowName);
        var player = Player.m_localPlayer;
        var gui = InventoryGui.instance;
        var recipe = CultivatorTiers.CultivatorRecipe;
        var rig = new Rig(player, ForgeLowName);
        var taken = new List<Vector3>();
        try
        {
            UseDefaultRules();
            rig.Noon();
            rig.TakeControls();
            if (!c.Check(recipe != null && CultivatorTiers.InForce, "cultivator recipe captured, tiers in force"))
            {
                c.Report();
                yield break;
            }
            // Vanilla numbers of levels 1 to 3.
            var bronze = Row(recipe, "Bronze");
            var corewood = Row(recipe, "RoundLog");
            c.Check(bronze != null && corewood != null && bronze.GetAmount(1) == 5 && corewood.GetAmount(1) == 5, "level 1: 5 Bronze and 5 Corewood");
            c.Check(bronze != null && corewood != null && bronze.GetAmount(2) == 1 && corewood.GetAmount(2) == 1 && bronze.GetAmount(3) == 2 && corewood.GetAmount(3) == 2,
                "level 2: 1 and 1, level 3: 2 and 2");
            c.Check(recipe.m_resources.Where(r => r != null && r != bronze && r != corewood && !r.m_upgraderResource).All(r => r.GetAmount(1) == 0 && r.GetAmount(2) == 0 && r.GetAmount(3) == 0),
                "no other material for levels 1 to 3");
            c.Check(recipe.GetRequiredStationLevel(1) == 1 && recipe.GetRequiredStationLevel(2) == 2 && recipe.GetRequiredStationLevel(3) == 3, "Forge level 1, 2, 3");

            var forge = new ForgeRig();
            if (!c.Check(SpawnForge(rig, taken, forge), "Forge spawned"))
            {
                c.Report();
                yield break;
            }
            yield return ForgeLevel(rig, forge, 3, c);
            KnowRecipe(rig, recipe);
            var bronze0 = Have(rig, "Bronze");
            var wood0 = Have(rig, "RoundLog");
            rig.Give("Bronze", 8);
            rig.Give("RoundLog", 8);

            // T08: craft, then level 2 and 3.
            yield return OpenStation(rig, forge.Station, false);
            var made = new Crafted();
            yield return CraftStep(rig, c, recipe, null, 1, WantRows("Bronze", 5, "RoundLog", 5), made);
            c.Check(Have(rig, "Bronze") == bronze0 + 3 && Have(rig, "RoundLog") == wood0 + 3, "craft used 5 Bronze and 5 Corewood");
            gui.OnTabUpgradePressed();
            yield return Frames(2);
            var item = made.Item;
            if (item != null)
            {
                c.Check(Near(item.m_durability, item.GetMaxDurability()) && item.m_shared.m_buildPieces == TransplantContent.CultivatorTable, "new cultivator: full durability, cultivator build table");
                yield return CraftStep(rig, c, recipe, item, 2, WantRows("Bronze", 1, "RoundLog", 1), made);
                c.Check(Have(rig, "Bronze") == bronze0 + 2 && Have(rig, "RoundLog") == wood0 + 2, "level 2 used 1 Bronze and 1 Corewood");
                item = made.Item;
            }
            if (item != null)
            {
                yield return CraftStep(rig, c, recipe, item, 3, WantRows("Bronze", 2, "RoundLog", 2), made);
                c.Check(Have(rig, "Bronze") == bronze0 && Have(rig, "RoundLog") == wood0, "level 3 used 2 Bronze and 2 Corewood");
                item = made.Item;
            }
            if (!c.Check(item != null && item.m_quality == 3, "a level 3 cultivator made the vanilla way"))
            {
                c.Report();
                yield break;
            }

            // T10 at Forge level 3: listed, materials there, but the Forge is one level short.
            var metal0 = Have(rig, "BlackMetal");
            var thread0 = Have(rig, "LinenThread");
            rig.Give("BlackMetal", 5);
            rig.Give("LinenThread", 10);
            rig.Give("Bronze", 5);
            rig.Give("RoundLog", 5);
            if (rig.Inv.CountItems(SharedName("Upgrader0Weapon")) == 0)
            {
                rig.Give("Upgrader0Weapon", 1);
            }
            RefreshList();
            yield return null;
            var row = RecipeRow(recipe, item);
            if (c.Check(row >= 0, "Forge level 3: the level 3 cultivator is listed for upgrade"))
            {
                c.Check(!gui.m_availableRecipes[row].CanCraft && player.HaveRequirementItems(recipe, false, 4), "Forge level 3: materials are there but it cannot be made yet");
                gui.SetRecipe(row, false);
                yield return Frames(3);
                var rows = RequirementRows();
                var want = WantRows("BlackMetal", 5, "LinenThread", 10);
                c.Check(SameRows(rows, want), $"level 4 asks '{rows}' only (want '{want}': no Bronze, no Corewood, no idol)");
                c.Check(gui.m_minStationLevelIcon.gameObject.activeSelf && gui.m_minStationLevelText.text == "4", $"Forge level {gui.m_minStationLevelText.text} asked (want 4)");
                var red = false;
                var until = Time.realtimeSinceStartup + 1f;
                while (Time.realtimeSinceStartup < until)
                {
                    red |= gui.m_minStationLevelText.color == Color.red;
                    yield return null;
                }
                c.Check(red, "the Forge level is shown in red at Forge level 3");
                var text = gui.m_recipeDecription != null ? gui.m_recipeDecription.text ?? "" : "";
                c.Check(text.Contains("Tier: " + Orange("Black metal cultivator")), $"preview: tier line ('{OneLine(text)}')");
                c.Check(text.Contains("Replants: " + Orange(BronzePlants + ", Cloudberry bush")), "preview: Replants line with the seven plants");
                c.Check(text.Contains("New: " + Orange("Cloudberry bush")), "preview: New: Cloudberry bush");
                SelfTest.Screenshot(ForgeLowName, "level-3-forge");
                yield return Frames(2);
                yield return CraftRow(row);
                c.Check(rig.Inv.ContainsItem(item) && item.m_quality == 3 && Have(rig, "BlackMetal") == metal0 + 5, "Craft at Forge level 3 does nothing");
            }

            // Forge level 4: upgrade.
            yield return ForgeLevel(rig, forge, 4, c);
            yield return CraftStep(rig, c, recipe, item, 4, WantRows("BlackMetal", 5, "LinenThread", 10), made);
            var level4 = made.Item;
            if (c.Check(level4 != null, "level 4 cultivator made"))
            {
                c.Check(Have(rig, "BlackMetal") == metal0 && Have(rig, "LinenThread") == thread0, "5 Black metal and 10 Linen thread used");
                c.Check(Have(rig, "Bronze") == bronze0 + 5 && Have(rig, "RoundLog") == wood0 + 5, "no Bronze, no Corewood used");
                c.Check(rig.Inv.CountItems(SharedName("Upgrader0Weapon")) >= 1, "no idol used");
                c.Check(Near(level4.m_durability, 800f, 0.5f) && Near(level4.GetMaxDurability(), 800f, 0.5f), $"durability {F(level4.m_durability)} / {F(level4.GetMaxDurability())} (want 800)");
                yield return Frames(3);
                var slot = SlotOf(gui.m_playerGrid, rig.Inv, level4);
                c.Check(GemSlot(slot, 4), $"its inventory slot: level 4 gem, no level number ({SlotText(slot)})");
                c.Check(level4.GetTooltip().Contains("$item_quality: " + Orange("4") + "\nTier: " + Orange("Black metal cultivator")), "tooltip: Quality 4, black metal cultivator");
            }
            yield return CloseStation(rig);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.forge.high (T12, T17, T18, T28) ----------

    private static IEnumerator RunForgeHigh()
    {
        if (!WorldReady(ForgeHighName))
        {
            yield break;
        }
        var c = new Checks(ForgeHighName);
        var player = Player.m_localPlayer;
        var gui = InventoryGui.instance;
        var recipe = CultivatorTiers.CultivatorRecipe;
        var rig = new Rig(player, ForgeHighName);
        var taken = new List<Vector3>();
        try
        {
            UseDefaultRules();
            rig.Noon();
            rig.TakeControls();
            if (!c.Check(recipe != null && CultivatorTiers.InForce, "cultivator recipe captured, tiers in force"))
            {
                c.Report();
                yield break;
            }
            var forge = new ForgeRig();
            if (!c.Check(SpawnForge(rig, taken, forge), "Forge spawned"))
            {
                c.Report();
                yield break;
            }
            yield return ForgeLevel(rig, forge, 7, c);
            KnowRecipe(rig, recipe);
            // Level 4, 5 and 6 cultivators as the spawn command gives them (never through our upgrade).
            var from = new Dictionary<int, ItemDrop.ItemData>();
            for (var q = 4; q <= 6; q++)
            {
                from[q] = rig.Give(PlantCatalog.CultivatorPrefab, 1, q);
            }
            var materials = new[] { "", "", "", "", "", "Eitr", "FlametalNew", "Gold" };
            var amounts = new[] { 0, 0, 0, 0, 0, 15, 5, 5 };
            var durability = new[] { 0f, 0f, 0f, 0f, 0f, 1000f, 1200f, 1400f };
            var keep = new[] { "Bronze", "RoundLog", "BlackMetal", "LinenThread" };
            var kept = keep.ToDictionary(n => n, n => Have(rig, n));
            foreach (var n in keep)
            {
                rig.Give(n, 5);
            }
            var have = new Dictionary<string, int>();
            for (var q = 5; q <= 7; q++)
            {
                have[materials[q]] = Have(rig, materials[q]);
            }
            if (!c.Check(from.Values.All(i => i != null), "cultivators level 4, 5, 6 given"))
            {
                c.Report();
                yield break;
            }
            yield return OpenStation(rig, forge.Station, true);
            c.Check(RecipeRow(recipe, from[4]) >= 0 && !gui.m_availableRecipes[RecipeRow(recipe, from[4])].CanCraft,
                "spawned level 4: listed for level 5, not possible without Refined eitr");
            ItemDrop.ItemData top = null;
            for (var q = 5; q <= 7; q++)
            {
                var material = materials[q];
                rig.Give(material, amounts[q]);
                var made = new Crafted();
                yield return CraftStep(rig, c, recipe, from[q - 1], q, WantRows(material, amounts[q]), made);
                var item = made.Item;
                if (!c.Check(item != null, $"level {q} cultivator made"))
                {
                    continue;
                }
                c.Check(Have(rig, material) == have[material], $"level {q}: {amounts[q]} {L(SharedName(material))} used");
                c.Check(keep.All(n => Have(rig, n) == kept[n] + 5), $"level {q}: nothing else used");
                c.Check(Near(item.m_durability, durability[q], 0.5f), $"level {q}: durability {F(item.m_durability)} (want {F(durability[q])})");
                yield return Frames(3);
                var slot = SlotOf(gui.m_playerGrid, rig.Inv, item);
                c.Check(GemSlot(slot, q), $"level {q}: its slot shows the level {q} gem, no number ({SlotText(slot)})");
                var text = gui.m_recipeDecription != null ? gui.m_recipeDecription.text ?? "" : "";
                c.Note($"level {q} done; panel now: {OneLine(text).Substring(0, Math.Min(120, OneLine(text).Length))}");
                if (q == 7)
                {
                    top = item;
                }
            }
            // Preview of each level (panel text of the row before crafting is the same static tooltip).
            for (var q = 5; q <= 7; q++)
            {
                var tip = ItemDrop.ItemData.GetTooltip(CultivatorTiers.CultivatorDrop.m_itemData, q, true, Game.m_worldLevel);
                c.Check(tip.Contains("Tier: " + Orange(TierTitles[q])) && tip.Contains("New: " + Orange(NewPlants[q])), $"level {q} preview: {TierTitles[q]}, new {NewPlants[q]}");
            }
            if (top != null)
            {
                RefreshList();
                yield return null;
                c.Check(RecipeRow(recipe, top) < 0, "level 7 is the last level: not listed for upgrade");
            }
            SelfTest.Screenshot(ForgeHighName, "level-7");
            yield return Frames(2);
            yield return CloseStation(rig);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.settings (T26 a-c) ----------

    private static IEnumerator RunSettings()
    {
        if (!WorldReady(SettingsName))
        {
            yield break;
        }
        var c = new Checks(SettingsName);
        var player = Player.m_localPlayer;
        var gui = InventoryGui.instance;
        var recipe = CultivatorTiers.CultivatorRecipe;
        var rig = new Rig(player, SettingsName);
        var taken = new List<Vector3>();
        try
        {
            UseDefaultRules();
            rig.Noon();
            rig.TakeControls();
            var shared = CultivatorTiers.CultivatorDrop != null ? CultivatorTiers.CultivatorDrop.m_itemData.m_shared : null;
            if (!c.Check(recipe != null && shared != null && CultivatorTiers.InForce && SelfTestLog.Installed, "cultivator recipe captured, tiers in force, log watch on"))
            {
                c.Report();
                yield break;
            }
            var forge = new ForgeRig();
            if (!c.Check(SpawnForge(rig, taken, forge), "Forge spawned"))
            {
                c.Report();
                yield break;
            }
            yield return ForgeLevel(rig, forge, 4, c);
            KnowRecipe(rig, recipe);
            var level3 = rig.Give(PlantCatalog.CultivatorPrefab, 1, 3);
            var level4 = rig.Give(PlantCatalog.CultivatorPrefab, 1, 4);
            yield return OpenStation(rig, forge.Station, true);
            var row = RecipeRow(recipe, level3);
            if (!c.Check(row >= 0 && RecipeRow(recipe, level4) >= 0, "levels 3 and 4 listed for upgrade with the default rules"))
            {
                c.Report();
                yield break;
            }
            gui.SetRecipe(row, false);
            yield return Frames(3);
            c.Check(SameRows(RequirementRows(), WantRows("BlackMetal", 5, "LinenThread", 10)), $"default: level 4 asks '{RequirementRows()}'");

            // a. Cost changed while the tab is open: within a second it asks 2 Wood. No Rebuild call here.
            ServerRules.TestRules = new CultivatorRules { BlackMetalLevel = "Wood:2" };
            c.Check(TransplantContent.RebuildPending, "cost change waits for the settings to stay still");
            yield return new WaitForSecondsRealtime(1f);
            yield return Frames(2);
            c.Check(!TransplantContent.RebuildPending && SameRows(RequirementRows(), WantRows("Wood", 2)), $"BlackMetalLevel = Wood:2: a second later the tab asks '{RequirementRows()}'");
            var wood = Row(recipe, "Wood");
            c.Check(wood != null && wood.GetAmount(4) == 2 && Row(recipe, "BlackMetal") == null && shared.m_maxQuality == 7, "2 Wood for level 4, no black metal row, still 7 levels");

            // b. A cost that names no item: two warnings, cultivators stop at level 3.
            var mark = SelfTestLog.Mark;
            ServerRules.TestRules = new CultivatorRules { BlackMetalLevel = "Nothing:5" };
            yield return new WaitForSecondsRealtime(1f);
            yield return Frames(2);
            var notItem = SelfTestLog.CountSince(mark, "\"Nothing\" (BlackMetalLevel) is not an item");
            var stops = SelfTestLog.CountSince(mark, "so cultivators stop at level 3");
            c.Check(notItem == 1 && stops == 1, $"BlackMetalLevel = Nothing:5: log warns that Nothing is not an item ({notItem}) and that cultivators stop at level 3 ({stops})");
            c.Check(SelfTestLog.Since(mark).Where(l => l.Text.Contains("Nothing")).All(l => l.Level == BepInEx.Logging.LogLevel.Warning), "both are warnings");
            c.Check(shared.m_maxQuality == 3, $"cultivators stop at level 3 (max quality {shared.m_maxQuality})");
            RefreshList();
            yield return null;
            c.Check(RecipeRow(recipe, level3) < 0, "a level 3 cultivator is no longer listed for upgrade");
            c.Check(rig.Inv.ContainsItem(level4) && level4.m_quality == 4 && RecipeRow(recipe, level4) < 0, "a level 4 cultivator stays level 4 (not listed either)");

            // c. EitrLevel empty: stop at level 4.
            ServerRules.TestRules = new CultivatorRules { EitrLevel = "" };
            yield return new WaitForSecondsRealtime(1f);
            yield return Frames(2);
            RefreshList();
            yield return null;
            c.Check(shared.m_maxQuality == 4, $"EitrLevel empty: cultivators stop at level 4 (max quality {shared.m_maxQuality})");
            c.Check(RecipeRow(recipe, level3) >= 0 && RecipeRow(recipe, level4) < 0, "level 3 can go to 4, level 4 is not listed");

            UseDefaultRules();
            RefreshList();
            yield return null;
            c.Check(shared.m_maxQuality == 7 && RecipeRow(recipe, level4) >= 0, "settings back: 7 levels, level 4 listed again");
            yield return CloseStation(rig);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.potential (T27) ----------

    private sealed class UnityLogCatch : IDisposable
    {
        internal readonly List<string> Lines = new List<string>();
        private readonly string _part;

        internal UnityLogCatch(string part)
        {
            _part = part;
            Application.logMessageReceived += On;
        }

        private void On(string condition, string stackTrace, LogType type)
        {
            if (condition != null && condition.IndexOf(_part, StringComparison.Ordinal) >= 0)
            {
                Lines.Add(condition);
            }
        }

        public void Dispose() => Application.logMessageReceived -= On;
    }

    // Forge of Potential next to the test origin, usable from where the player stands.
    private static CraftingStation SpawnPotential(Rig rig, List<Vector3> taken)
    {
        if (!FindSpot(rig.Origin, Vector3.back, new[] { 6f, 8f, 10f, 12f, 14f, 16f }, 3f, taken, out var spot))
        {
            return null;
        }
        var away = rig.Origin - spot;
        away.y = 0f;
        var go = rig.Spawn("UpgradeStation", spot, Quaternion.LookRotation(away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward));
        var station = go != null ? go.GetComponentInChildren<CraftingStation>() : null;
        if (station != null)
        {
            station.m_useDistance = 60f;
        }
        return station;
    }

    private static IEnumerator RunPotential()
    {
        if (!WorldReady(PotentialName))
        {
            yield break;
        }
        var c = new Checks(PotentialName);
        var player = Player.m_localPlayer;
        var gui = InventoryGui.instance;
        var recipe = CultivatorTiers.CultivatorRecipe;
        var rig = new Rig(player, PotentialName);
        var taken = new List<Vector3>();
        UnityLogCatch log = null;
        try
        {
            UseDefaultRules();
            rig.Noon();
            rig.TakeControls();
            var station = SpawnPotential(rig, taken);
            yield return new WaitForSeconds(0.5f);
            if (!c.Check(recipe != null && station != null && station.m_upgrader, "Forge of Potential spawned (upgrader station)"))
            {
                c.Report();
                yield break;
            }
            var cultivators = new[] { 1, 3, 4 }.Select(q => rig.Give(PlantCatalog.CultivatorPrefab, 1, q)).ToArray();
            var axe = rig.Give("AxeStone", 1);
            var idol = rig.Give("Upgrader0Weapon", 1);
            var axeRecipe = axe != null ? ObjectDB.instance.GetRecipe(axe) : null;
            if (!c.Check(cultivators.All(i => i != null) && axe != null && idol != null && axeRecipe != null, "cultivators level 1, 3, 4, a Stone axe and a Wooden Battle Idol given"))
            {
                c.Report();
                yield break;
            }
            KnowRecipe(rig, recipe);
            KnowRecipe(rig, axeRecipe);
            c.Check(recipe.m_resources.Any(r => r != null && r.m_upgraderResource), "cultivator recipe keeps its idol row (so the game does not warn)");
            log = new UnityLogCatch("has no upgrader resource");
            yield return OpenStation(rig, station, true);
            c.Check(gui.m_availableRecipes.Any(r => ReferenceEquals(r.ItemData, axe)), $"upgrade list shows the Stone axe ({gui.m_availableRecipes.Count} rows)");
            c.Check(!AnyCultivatorRow(), "upgrade list shows no cultivator (levels 1, 3 and 4 carried)");
            RefreshList();
            yield return Frames(2);
            c.Check(!AnyCultivatorRow(), "still none after a list refresh");
            c.Check(log.Lines.All(l => !l.Contains(PlantCatalog.CultivatorItemName)), $"no 'has no upgrader resource' warning for the cultivator ({log.Lines.Count} such line(s) in all)");
            SelfTest.Screenshot(PotentialName, "list");
            yield return Frames(2);
            yield return CloseStation(rig);
            c.Report();
        }
        finally
        {
            log?.Dispose();
            rig.Restore();
        }
    }

    // ---------- replant.tooltip (T19, T21) ----------

    private static IEnumerator RunTooltip()
    {
        if (!WorldReady(TooltipName))
        {
            yield break;
        }
        var c = new Checks(TooltipName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, TooltipName);
        try
        {
            UseDefaultRules();
            rig.Noon();
            rig.NoAutoPickup();
            rig.TakeControls();
            var inv = rig.Inv;
            var drop = CultivatorTiers.CultivatorDrop;
            if (!c.Check(drop != null, "cultivator captured"))
            {
                c.Report();
                yield break;
            }
            // T19: every level, right after the quality line.
            for (var q = 1; q <= 7; q++)
            {
                var item = rig.Give(PlantCatalog.CultivatorPrefab, 1, q);
                if (!c.Check(item != null, $"level {q} cultivator given"))
                {
                    continue;
                }
                var tip = item.GetTooltip();
                var want = "\n$item_quality: " + Orange(q.ToString(CultureInfo.InvariantCulture)) + "\nTier: " + Orange(TierTitles[q]) + "\nReplants: " + Orange(PlantsOf(q));
                c.Check(tip.Contains(want), $"level {q} tooltip: '{TierTitles[q]}' and its plants right after the quality line");
                c.Check(!tip.Contains("New:"), $"level {q} tooltip: no New: line outside the crafting panel");
                if (q >= 2)
                {
                    var preview = ItemDrop.ItemData.GetTooltip(drop.m_itemData, q, true, Game.m_worldLevel);
                    var wantNew = "\nNew: " + Orange(NewPlants[q]);
                    c.Check(q <= 3 ? !preview.Contains("New:") : preview.Contains("\nReplants: " + Orange(PlantsOf(q)) + wantNew),
                        q <= 3 ? $"upgrade preview level {q}: no New: line" : $"upgrade preview level {q}: New: {NewPlants[q]}");
                    c.Check(preview.Contains("\nTier: " + Orange(TierTitles[q])), $"upgrade preview level {q}: tier line");
                }
                inv.RemoveItem(item);
            }

            // T21: the transplant item.
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var ygg = PlantCatalog.ByKey("YggaShoot");
            var name = rasp.ItemDisplayName;
            rig.Give(rasp.ItemName, 15);
            rig.Give(rasp.ItemName, 10);
            var stacks = inv.GetAllItems().Where(i => i.m_shared.m_name == name).Select(i => i.m_stack).OrderByDescending(s => s).ToArray();
            c.Check(stacks.SequenceEqual(new[] { 20, 5 }), $"25 transplants stack as 20 + 5 ({string.Join(" + ", stacks.Select(s => s.ToString()).ToArray())})");
            var small = inv.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == name && i.m_stack == 5);
            if (c.Check(small != null, "stack of 5 found"))
            {
                var tip = small.GetTooltip();
                var text = "Raspberry bush, dug up roots and all. Plant it with the cultivator to grow a new one. Grows on open ground in any land biome, "
                           + "no tilling needed; in the Ashlands, the Mountains and the Deep North only inside a shield.";
                c.Check(name == "Raspberry bush transplant" && small.m_shared.m_description == text && tip.StartsWith(text, StringComparison.Ordinal),
                    $"plain English name and text ('{small.m_shared.m_description}')");
                c.Check(tip.Contains("$item_weight: <color=orange>" + 0.5f.ToString("0.0")) && Near(small.m_shared.m_weight, 0.5f), "weight 0.5");
                c.Check(!tip.Contains("$item_food") && small.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Material, "no food values");
                var yggItem = TransplantContent.ItemPrefab(ygg).GetComponent<ItemDrop>().m_itemData;
                c.Check(yggItem.m_shared.m_description.Contains("Must be planted within a few metres of an Ancient Root in the Mistlands, but not right against it."),
                    $"Yggdrasil transplant text ('{yggItem.m_shared.m_description}')");

                // Right-click does not eat it.
                var foods = player.GetFoods().Count;
                player.UseItem(inv, small, true);
                yield return Frames(2);
                c.Check(small.m_stack == 5 && inv.ContainsItem(small) && player.GetFoods().Count == foods, "right-click does not eat it");

                // Portal.
                var bag = new Inventory("MC_SelfTest", null, 2, 1);
                bag.AddItem(small.Clone());
                c.Check(small.m_shared.m_teleportable && bag.IsTeleportable(false), "it can go through a portal");

                // Dropped: lies on the ground as the produce model, can be picked up again.
                var known = new HashSet<ItemDrop>(ItemDrop.s_instances);
                c.Check(player.DropItem(inv, small, 1), "one dropped");
                yield return new WaitForSeconds(0.8f);
                var onGround = ItemDrop.s_instances.FirstOrDefault(d => d != null && !known.Contains(d) && d.m_itemData != null && d.m_itemData.m_shared.m_name == name);
                if (c.Check(onGround != null && onGround.m_itemData.m_stack == 1, "it lies on the ground"))
                {
                    var produce = ObjectDB.instance.GetItemPrefab(rasp.ProduceItem);
                    var wantMeshes = produce.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh).Where(m => m != null).ToList();
                    var gotMeshes = onGround.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh).Where(m => m != null).ToList();
                    c.Check(wantMeshes.Count > 0 && gotMeshes.Count == wantMeshes.Count && gotMeshes.All(wantMeshes.Contains),
                        $"as the {rasp.ProduceItem} model ({gotMeshes.Count} mesh(es))");
                    c.Check(PrefabHash(onGround.gameObject) == rasp.ItemHash, "known to the network as the transplant");
                    c.Check(player.Pickup(onGround.gameObject, false, false), "picked up again");
                    c.Check(CountByName(inv, name) == 25, $"25 again ({CountByName(inv, name)})");
                }
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.slots (T20, T36, T37) ----------

    private static HotkeyBar FindHotkeyBar()
    {
        var bar = Hud.instance != null ? Hud.instance.GetComponentInChildren<HotkeyBar>(true) : null;
        if (bar != null)
        {
            return bar;
        }
        foreach (var b in Resources.FindObjectsOfTypeAll<HotkeyBar>())
        {
            if (b != null && b.gameObject.scene.IsValid() && b.isActiveAndEnabled)
            {
                return b;
            }
        }
        return null;
    }

    // Item to the first row (the hotbar). Row full (things the player carried before the test plus the seven
    // cultivators, run of 2026-10-08: a torch in slot 1 left no room for the stack of 5): it trades place with one not
    // wanted there, looked for from the right (the tools the test gave). That one goes back at the end (Rig undo).
    private static bool ToHotbar(Rig rig, Inventory inv, ItemDrop.ItemData item, ItemDrop.ItemData[] wanted)
    {
        for (var x = 0; x < inv.GetWidth(); x++)
        {
            if (inv.GetItemAt(x, 0) == null)
            {
                item.m_gridPos = new Vector2i(x, 0);
                inv.Changed();
                return true;
            }
        }
        for (var x = inv.GetWidth() - 1; x >= 0; x--)
        {
            var other = inv.GetItemAt(x, 0);
            if (other == null || Array.IndexOf(wanted, other) >= 0)
            {
                continue;
            }
            var onBar = other.m_gridPos;
            var from = item.m_gridPos;
            other.m_gridPos = from;
            item.m_gridPos = onBar;
            inv.Changed();
            rig.Undo(() =>
            {
                // Both back to their slots while the other one is still carried (one of the player's own things must
                // end where it was; the things the test gave are taken away by the rig after this).
                if (!inv.ContainsItem(other))
                {
                    return;
                }
                if (inv.ContainsItem(item))
                {
                    item.m_gridPos = from;
                }
                other.m_gridPos = onBar;
                inv.Changed();
            });
            return true;
        }
        return false;
    }

    private static IEnumerator RunSlots()
    {
        if (!WorldReady(SlotsName))
        {
            yield break;
        }
        var c = new Checks(SlotsName);
        var player = Player.m_localPlayer;
        var gui = InventoryGui.instance;
        var rig = new Rig(player, SlotsName);
        var taken = new List<Vector3>();
        try
        {
            UseDefaultRules();
            rig.Noon();
            rig.NoAutoPickup();
            rig.TakeControls();
            var inv = rig.Inv;
            var vanilla = VanillaCultivatorIcon();
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var name = rasp.ItemDisplayName;
            var sprout = TransplantContent.ItemPrefab(rasp).GetComponent<ItemDrop>().m_itemData.m_shared.m_icons[0];
            if (!c.Check(vanilla != null && IconPainter.IsPainted(sprout), "cultivator icon and painted transplant icon"))
            {
                c.Report();
                yield break;
            }
            var tools = new Dictionary<int, ItemDrop.ItemData>();
            for (var q = 1; q <= 7; q++)
            {
                tools[q] = rig.Give(PlantCatalog.CultivatorPrefab, 1, q);
            }
            var five = rig.Give(rasp.ItemName, 5);
            var twenty = rig.Give(PlantCatalog.ByKey("BlueberryBush").ItemName, 20);
            if (!c.Check(tools.Values.All(t => t != null) && five != null && twenty != null, "cultivators level 1 to 7, 5 and 20 transplants given"))
            {
                c.Report();
                yield break;
            }

            // Hotbar: level 4 and 7 cultivator and the stack of 5.
            var wanted = new[] { tools[4], tools[7], five };
            var onBar = wanted.Where(i => i.m_gridPos.y == 0 || ToHotbar(rig, inv, i, wanted)).ToList();
            yield return Frames(4);
            var bar = FindHotkeyBar();
            if (c.Check(bar != null && onBar.Count == 3, $"hotbar found, three items on it ({onBar.Count})"))
            {
                HotkeyBar.ElementData At(ItemDrop.ItemData item) => item.m_gridPos.x < bar.m_elements.Count ? bar.m_elements[item.m_gridPos.x] : null;
                foreach (var q in new[] { 4, 7 })
                {
                    var el = At(tools[q]);
                    c.Check(el != null && ReferenceEquals(el.m_icon.sprite, TierIcons.CultivatorIcon(vanilla, q)), $"hotbar: level {q} cultivator shows its gem");
                }
                var stack = At(five);
                c.Check(stack != null && ReferenceEquals(stack.m_icon.sprite, sprout) && stack.m_amount.gameObject.activeSelf && stack.m_amount.text == "5 / 20",
                    $"hotbar: transplant with its sprout icon and '{(stack != null ? stack.m_amount.text : "")}' (want '5 / 20')");
            }

            // Inventory.
            gui.Show(null);
            yield return new WaitForSecondsRealtime(1f);
            var grid = gui.m_playerGrid;
            for (var q = 1; q <= 7; q++)
            {
                var slot = SlotOf(grid, inv, tools[q]);
                c.Check(q >= 4 ? GemSlot(slot, q) : NumberSlot(slot, q), q >= 4
                    ? $"inventory: level {q} shows its gem and no level number ({SlotText(slot)})"
                    : $"inventory: level {q} shows the plain icon and its number ({SlotText(slot)})");
            }
            foreach (var pair in new[] { new KeyValuePair<ItemDrop.ItemData, string>(five, "5/20"), new KeyValuePair<ItemDrop.ItemData, string>(twenty, "20/20") })
            {
                var slot = SlotOf(grid, inv, pair.Key);
                c.Check(slot != null && IconPainter.IsPainted(slot.m_icon.sprite) && ReferenceEquals(slot.m_icon.sprite, pair.Key.GetIcon()) && slot.m_amount.enabled
                        && slot.m_amount.text == pair.Value && !slot.m_quality.enabled && !slot.m_noteleport.enabled && !slot.m_food.enabled,
                    $"inventory: transplant stack shows its sprout icon and '{pair.Value}', no level, food or no-portal mark ({SlotText(slot)})");
            }
            SelfTest.Screenshot(SlotsName, "inventory");
            yield return Frames(2);
            gui.Hide();
            yield return new WaitForSecondsRealtime(0.5f);

            // Chest.
            if (c.Check(FindPlantSpot(rig, taken, 1.5f, out var spot), "free spot for a chest"))
            {
                var chestGo = rig.Spawn("piece_chest_wood", spot, Quaternion.identity);
                yield return Settle();
                yield return Stand(rig, spot);
                var chest = chestGo != null ? chestGo.GetComponent<Container>() : null;
                if (c.Check(chest != null, "wooden chest spawned"))
                {
                    var box = chest.GetInventory();
                    var b2 = box.AddItem(PlantCatalog.CultivatorPrefab, 1, 2, 0, 0L, "", false);
                    var b5 = box.AddItem(PlantCatalog.CultivatorPrefab, 1, 5, 0, 0L, "", false);
                    var b6 = box.AddItem(PlantCatalog.CultivatorPrefab, 1, 6, 0, 0L, "", false);
                    var bs = box.AddItem(rasp.ItemName, 5, 1, 0, 0L, "", false);
                    gui.Show(chest);
                    yield return new WaitForSecondsRealtime(1.2f);
                    var cg = gui.ContainerGrid;
                    if (c.Check(InventoryGui.IsVisible() && cg != null && b2 != null && b5 != null && b6 != null && bs != null, "chest open with cultivators level 2, 5, 6 and 5 transplants"))
                    {
                        c.Check(NumberSlot(SlotOf(cg, box, b2), 2), $"chest: level 2 shows its number ({SlotText(SlotOf(cg, box, b2))})");
                        c.Check(GemSlot(SlotOf(cg, box, b5), 5), $"chest: level 5 shows its gem, no number ({SlotText(SlotOf(cg, box, b5))})");
                        c.Check(GemSlot(SlotOf(cg, box, b6), 6), $"chest: level 6 shows its gem, no number ({SlotText(SlotOf(cg, box, b6))})");
                        var slot = SlotOf(cg, box, bs);
                        c.Check(slot != null && ReferenceEquals(slot.m_icon.sprite, sprout) && slot.m_amount.enabled && slot.m_amount.text == "5/20",
                            $"chest: transplant stack shows its sprout icon and '5/20' ({SlotText(slot)})");
                        SelfTest.Screenshot(SlotsName, "chest");
                        yield return Frames(2);
                    }
                    gui.Hide();
                    yield return new WaitForSecondsRealtime(0.5f);
                    box.RemoveAll();
                }
            }

            // Top-left line of a pickup carries the gem.
            var gem5 = TierIcons.CultivatorIcon(vanilla, 5);
            inv.RemoveItem(tools[5]);
            var dropped = ItemDrop.DropItem(tools[5], 1, player.transform.position + player.transform.forward * 1.2f + Vector3.up, Quaternion.identity);
            yield return new WaitForSeconds(0.8f);
            var watch = WatchTopLeft(L("$msg_added " + PlantCatalog.CultivatorItemName), gem5);
            c.Check(dropped != null && player.Pickup(dropped.gameObject, false, false), "level 5 cultivator dropped and picked up");
            yield return null;
            c.Check(Arrived(watch), "top-left pickup line shows the level 5 gem");
            c.Report();
        }
        finally
        {
            if (InventoryGui.instance != null && InventoryGui.IsVisible())
            {
                InventoryGui.instance.Hide();
            }
            rig.Restore();
        }
    }

    // ---------- replant.gems (gem colours) ----------

    private static readonly string[] GemColourNames = { "dark grey", "cyan", "orange", "crimson" };

    private static readonly Color32[] GemColours =
    {
        new Color32(80, 80, 88, 255), new Color32(60, 200, 240, 255), new Color32(255, 130, 40, 255), new Color32(200, 50, 60, 255),
    };

    // Which of the four named colours most of the gem's own pixels are nearest to (outline dark and light edge pixels
    // left out); where the painted pixels sit. False = icons could not be read.
    private static bool GemLook(Sprite plain, Sprite gem, out int colour, out int upperRight, out int elsewhere, out string counts)
    {
        colour = -1;
        upperRight = 0;
        elsewhere = 0;
        counts = "";
        var a = IconPainter.CopyPixels(plain, null, out var w, out var h);
        var b = IconPainter.CopyPixels(gem, null, out var w2, out var h2);
        if (a == null || b == null || w != w2 || h != h2 || a.Length != b.Length)
        {
            return false;
        }
        var votes = new int[GemColours.Length];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var px = b[y * w + x];
                if (!Differs(a[y * w + x], px))
                {
                    continue;
                }
                if (x >= w / 2 && y >= h / 2)
                {
                    upperRight++;
                }
                else
                {
                    elsewhere++;
                }
                var max = Math.Max(px.r, Math.Max(px.g, px.b));
                var min = Math.Min(px.r, Math.Min(px.g, px.b));
                if (max < 45 || min > 175 || px.a < 200)
                {
                    continue;
                }
                var best = 0;
                var bestD = int.MaxValue;
                for (var i = 0; i < GemColours.Length; i++)
                {
                    var dr = px.r - GemColours[i].r;
                    var dg = px.g - GemColours[i].g;
                    var db = px.b - GemColours[i].b;
                    var d = dr * dr + dg * dg + db * db;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }
                votes[best]++;
            }
        }
        colour = Array.IndexOf(votes, votes.Max());
        counts = string.Join(" / ", votes.Select((v, i) => GemColourNames[i] + " " + v).ToArray());
        return votes.Max() > 0;
    }

    private static IEnumerator RunGems()
    {
        var c = new Checks(GemsName);
        var vanilla = VanillaCultivatorIcon();
        if (!c.Check(vanilla != null && IconPainter.HasGraphics, "vanilla cultivator icon and a graphics device"))
        {
            c.Report();
            yield break;
        }
        for (var q = 4; q <= 7; q++)
        {
            var gem = TierIcons.CultivatorIcon(vanilla, q);
            if (!c.Check(gem != null && IconPainter.IsPainted(gem), $"level {q}: gem icon painted"))
            {
                continue;
            }
            if (!c.Check(GemLook(vanilla, gem, out var colour, out var upperRight, out var elsewhere, out var counts), $"level {q}: icons read"))
            {
                continue;
            }
            c.Check(colour == q - 4, $"level {q}: gem is {GemColourNames[q - 4]} ({counts})");
            c.Check(upperRight >= 20 && elsewhere == 0, $"level {q}: gem in the upper-right corner only ({upperRight} px there, {elsewhere} elsewhere)");
        }
        for (var q = 1; q <= 3; q++)
        {
            c.Check(TierIcons.CultivatorIcon(vanilla, q) == null, $"level {q}: plain icon, no gem");
        }
        c.Report();
    }
}
#endif
