using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
#endif

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Debug build only (calls vanish in Release): more in-world self tests of the data side, one small test per TESTING.md
// item so one failing check never block another item. World probe run them (tools/Test-InWorld.ps1).
//   compendium.listed           = T05: every tab filled, stations and their upgrades under Building, trader-only items
//                                 and Piggy / Hen listed as "???", debug and attack prefabs and hair never listed, a
//                                 picked up Megingjord say "Sold by a trader".
//   compendium.item-details     = T10: Bronze Sword recipe and upgrade rows from the game's recipe, Wood and
//                                 Raspberries sources, own records.
//   compendium.piece-details    = T11: Workbench build cost, Forge "Needs a", upgrades, recipes at the station.
//   compendium.creature-details = T12: Boar habitat, drops, taming food, stats only after the first kill.
//   compendium.afterboss        = T12 / T13: "Appears after defeating <boss>" (boss "???" while unknown).
//   compendium.obfuscated       = T13: unknown drop, station, biome, saddle are "???" with numbers kept, long lists
//                                 fold, Smelter conversions.
//   compendium.eggs             = T33: Chicken Egg "Laid by a tamed ???" then "... Hen".
//   compendium.fishing          = T34: reading the Fishing Rod page add no Fishing skill.
//   compendium.pickup           = T07: Raspberries picked up, the Club learned by its recipe while holding Wood.
//   compendium.discover         = T08: Workbench by Hammer + Wood, Deer by its trophy, Forge by standing next to one.
//   compendium.smelter          = T13: Workbench seen + Hammer, Stone, Surtling Cores = Smelter buildable, then Copper Ore.
//   compendium.aim              = T06: Boar near but not aimed at = "???"; aimed at through a wall = "???"; aimed at =
//                                 met (real crosshair ray of the game). compendium.aim-greyling = T08: same, a Greyling.
//   compendium.boss             = T08: boss alerted far away, never aimed at = met, "Killed: 0".
//   compendium.kills            = T14: real kills (Character.Damage with the player as attacker) count.
//   compendium.farkill          = T08: a Neck killed from beyond the name-plate distance, never met: the kill discovers.
//   compendium.resets           = T21: resetknownitems and resetcharacter (the methods the console commands call).
//   compendium.unlocks          = T32: no-cost mode and the two world keys discover and undo.
//   compendium.unlocks-dlc      = T40: unlock modes discover nothing the crafting panel would not offer.
//   compendium.language         = T19: a name changed + language change event: name, order, biomes.
// Every test put back what it change (PlayerState, ProfileState, world keys, spawned things) in finally.
internal static class MoreSelfTests
{
#if DEBUG
    private const string ListedName = "compendium.listed";
    private const string ItemDetailsName = "compendium.item-details";
    private const string PieceDetailsName = "compendium.piece-details";
    private const string CreatureDetailsName = "compendium.creature-details";
    private const string AfterBossName = "compendium.afterboss";
    private const string ObfuscatedName = "compendium.obfuscated";
    private const string EggsName = "compendium.eggs";
    private const string FishingName = "compendium.fishing";
    private const string PickupName = "compendium.pickup";
    private const string DiscoverName = "compendium.discover";
    private const string SmelterName = "compendium.smelter";
    private const string AimName = "compendium.aim";
    private const string AimGreylingName = "compendium.aim-greyling";
    private const string BossName = "compendium.boss";
    private const string KillsName = "compendium.kills";
    private const string FarKillName = "compendium.farkill";
    private const string ResetsName = "compendium.resets";
    private const string UnlocksName = "compendium.unlocks";
    private const string UnlocksDlcName = "compendium.unlocks-dlc";
    private const string LanguageName = "compendium.language";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly string[] AllNames =
    {
        ListedName, ItemDetailsName, PieceDetailsName, CreatureDetailsName, AfterBossName, ObfuscatedName, EggsName,
        FishingName, PickupName, DiscoverName, SmelterName, AimName, AimGreylingName, BossName, KillsName, FarKillName, ResetsName,
        UnlocksName, UnlocksDlcName, LanguageName,
    };
#endif

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(ListedName, () => TestKit.RunData(ListedName, CheckListed));
        SelfTest.Register(ItemDetailsName, () => TestKit.RunData(ItemDetailsName, CheckItemDetails));
        SelfTest.Register(PieceDetailsName, () => TestKit.RunData(PieceDetailsName, CheckPieceDetails));
        SelfTest.Register(CreatureDetailsName, () => TestKit.RunData(CreatureDetailsName, CheckCreatureDetails));
        SelfTest.Register(AfterBossName, () => TestKit.RunData(AfterBossName, CheckAfterBoss));
        SelfTest.Register(ObfuscatedName, () => TestKit.RunData(ObfuscatedName, CheckObfuscated));
        SelfTest.Register(EggsName, () => TestKit.RunData(EggsName, CheckEggs));
        SelfTest.Register(FishingName, () => TestKit.RunData(FishingName, CheckFishing));
        SelfTest.Register(PickupName, () => TestKit.RunData(PickupName, CheckPickup));
        SelfTest.Register(DiscoverName, RunDiscover);
        SelfTest.Register(SmelterName, RunSmelter);
        SelfTest.Register(AimName, () => RunAim(AimName, "Boar"));
        SelfTest.Register(AimGreylingName, () => RunAim(AimGreylingName, "Greyling"));
        SelfTest.Register(BossName, RunBoss);
        SelfTest.Register(KillsName, RunKills);
        SelfTest.Register(FarKillName, RunFarKill);
        SelfTest.Register(ResetsName, () => TestKit.RunData(ResetsName, CheckResets));
        SelfTest.Register(UnlocksName, () => TestKit.RunData(UnlocksName, CheckUnlocks));
        SelfTest.Register(UnlocksDlcName, () => TestKit.RunData(UnlocksDlcName, CheckUnlocksDlc));
        SelfTest.Register(LanguageName, () => TestKit.RunData(LanguageName, CheckLanguage));
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        foreach (var name in AllNames)
        {
            SelfTest.Unregister(name);
        }
#endif
    }

#if DEBUG
    // ---------------------------------------------------------------- small helpers

    private static Entry Item(Catalog cat, string prefab, List<string> problems)
    {
        var e = cat.FindItemByPrefab(prefab);
        if (e == null)
        {
            problems.Add($"item prefab '{prefab}' has no entry");
        }
        return e;
    }

    private static Entry Piece(Catalog cat, string prefab, List<string> problems)
    {
        cat.PiecesByPrefab.TryGetValue(prefab, out var e);
        if (e == null)
        {
            problems.Add($"piece prefab '{prefab}' has no entry");
        }
        return e;
    }

    private static Entry Creature(Catalog cat, string prefab, List<string> problems)
    {
        cat.CreaturesByPrefab.TryGetValue(prefab, out var e);
        if (e == null)
        {
            problems.Add($"creature prefab '{prefab}' has no entry");
        }
        return e;
    }

    // Knowledge where only these entries are discovered (no biome known).
    private static Knowledge Only(Catalog cat, params Entry[] known) =>
        Knowledge.Synthetic(cat, e => Array.IndexOf(known, e) >= 0, Heightmap.Biome.None);

    private static Knowledge Real(Catalog cat) => Knowledge.Take(cat, false, honourUnlocks: false);

    private static string Join(IEnumerable<string> lines) => string.Join(" | ", lines);

    // Drop numbers written from the prefab's own fields (not through the mod): amount min to max-1, chance in %.
    private static string DropText(CharacterDrop.Drop d)
    {
        string amount;
        if (d.m_onePerPlayer)
        {
            amount = "1 per player";
        }
        else
        {
            var min = d.m_amountMin;
            var high = d.m_amountMax > min ? d.m_amountMax - 1 : min;
            amount = high > min ? min.ToString(Inv) + "-" + high.ToString(Inv) : min.ToString(Inv);
        }
        var chance = Mathf.Clamp01(d.m_chance) * 100f;
        return $"{amount} ({chance.ToString("0.#", Inv)}%{(d.m_levelMultiplier ? ", more with stars" : "")})";
    }

    // Make the character forget one entry every vanilla way (the test put everything back after with PlayerState /
    // ProfileState). Creature: kill count, trophy, and the whole Seen and Tames records.
    private static void Forget(Player player, Entry e)
    {
        if (e == null)
        {
            return;
        }
        var profile = Game.instance.GetPlayerProfile();
        var stats = profile.m_playerStats[0];
        switch (e.Kind)
        {
            case EntryKind.Item:
                player.m_knownMaterial.Remove(e.Key);
                player.m_knownRecipes.Remove(e.Key);
                foreach (var p in e.Item.Prefabs)
                {
                    if (p != null)
                    {
                        player.m_trophies.Remove(p.name);
                    }
                }
                break;
            case EntryKind.Piece:
                player.m_knownRecipes.Remove(e.Key);
                if (e.Piece.StationName != null)
                {
                    player.m_knownStations.Remove(e.Piece.StationName);
                }
                stats.m_piecesPlacedStats.Remove(e.Key);
                break;
            default:
                stats.m_enemyStats[0].Remove(e.Key);
                Forget(player, e.Creature.Trophy);
                player.m_customData.Remove(OwnRecords.SeenKey);
                player.m_customData.Remove(TamesReader.Key);
                OwnRecords.ClearCache();
                TamesReader.ClearCache();
                break;
        }
    }

    private static ListRow? RowOf(Catalog cat, Knowledge k, Entry e)
    {
        foreach (var r in ListBuilder.BuildTab(cat, k, e.Tab, showUndiscovered: true))
        {
            if (r.Kind == ListRowKind.Entry && ReferenceEquals(r.Entry, e))
            {
                return r;
            }
        }
        return null;
    }

    private static void ExpectUnknownRow(Catalog cat, Knowledge none, Entry e, string what, List<string> problems)
    {
        var row = RowOf(cat, none, e);
        if (row == null)
        {
            problems.Add($"{what} has no row in the {e.Tab} tab of a character that knows nothing");
        }
        else if (row.Value.Text != Labels.Unknown || row.Value.Icon != null || row.Value.Known)
        {
            problems.Add($"{what} row of a character that knows nothing shows '{row.Value.Text}'");
        }
    }

    // ---------------------------------------------------------------- compendium.listed (T05)

    private static string CheckListed(Catalog cat, List<string> problems)
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            problems.Add("no local player");
            return "";
        }
        for (var t = 0; t < Tabs.Count; t++)
        {
            if (cat.CountOf((CatalogTab)t) == 0)
            {
                problems.Add($"tab {(CatalogTab)t} has no entry");
            }
        }
        if (LogWatch.Count(LogWatch.CatalogBuilt) < 1)
        {
            problems.Add("no Info \"Encyclopedia catalog built ...\" line was logged since the mod started");
        }
        else
        {
            // "The line gives the counts": the last line (this catalog's build) against the entries counted here.
            var builtLine = LogWatch.LastCatalogBuilt;
            var itemCount = cat.Entries.Count(e => e.Kind == EntryKind.Item);
            var pieceCount = cat.Entries.Count(e => e.Kind == EntryKind.Piece);
            var creatureCount = cat.Entries.Count(e => e.Kind == EntryKind.Creature);
            if (builtLine.IndexOf($": {itemCount} items (", StringComparison.Ordinal) < 0
                || builtLine.IndexOf($", {pieceCount} pieces, {creatureCount} creatures,", StringComparison.Ordinal) < 0)
            {
                problems.Add($"the \"catalog built\" line does not give this catalog's counts ({itemCount} items, {pieceCount} pieces, {creatureCount} creatures): "
                             + $"'{(builtLine.Length > 200 ? builtLine.Substring(0, 200) : builtLine)}'");
            }
        }

        // Crafting stations and their upgrades: under Building.
        var stations = 0;
        var upgrades = 0;
        foreach (var e in cat.Entries)
        {
            if (e.Kind != EntryKind.Piece)
            {
                continue;
            }
            var station = e.Piece.StationName != null;
            var upgrade = e.Piece.ExtensionOf != null;
            stations += station ? 1 : 0;
            upgrades += upgrade ? 1 : 0;
            if ((station || upgrade) && e.Tab != CatalogTab.Building)
            {
                problems.Add($"{e} (station {station}, upgrade {upgrade}) is in tab {e.Tab}, not Building");
            }
        }
        if (stations == 0 || upgrades == 0)
        {
            problems.Add($"{stations} crafting stations and {upgrades} station upgrades listed (expected some of each)");
        }

        // New character: trader-only items and the young / farm creatures are "???" rows.
        var none = Knowledge.Synthetic(cat, _ => false, Heightmap.Biome.None);
        foreach (var prefab in new[] { "BeltStrength", "FishingRod", "HelmetDverger", "HelmetHat1" })
        {
            var e = cat.FindItemByPrefab(prefab);
            if (e == null || e.HiddenUntilKnown)
            {
                problems.Add($"item '{prefab}' is {(e == null ? "not listed" : "hidden until known (" + e.HiddenRule + ")")}: expected a \"???\" row");
                continue;
            }
            ExpectUnknownRow(cat, none, e, $"item '{prefab}'", problems);
        }
        foreach (var prefab in new[] { "Boar_piggy", "Hen" })
        {
            if (!cat.CreaturesByPrefab.TryGetValue(prefab, out var e) || e.HiddenUntilKnown)
            {
                problems.Add($"creature '{prefab}' is {(e == null ? "not listed" : "hidden until known")}: expected a creature row");
                continue;
            }
            ExpectUnknownRow(cat, none, e, $"creature '{prefab}'", problems);
        }

        // Never listed for a new character: attack items, debug items, hair and beards.
        var notListed = new List<string>();
        foreach (var prefab in new[] { "Abomination_attack1", "bjorn_bite", "PlayerUnarmed", "CapeTest", "SwordCheat", "SledgeCheat" })
        {
            var e = cat.FindItemByPrefab(prefab);
            notListed.Add($"{prefab} {(e == null ? "no entry" : e.HiddenUntilKnown ? "hidden until known" : "LISTED")}");
            if (e != null && (!e.HiddenUntilKnown || RowOf(cat, none, e) != null))
            {
                problems.Add($"'{prefab}' is listed for a character that never had it");
            }
        }
        if (cat.CreaturesByPrefab.TryGetValue("DvergerTest", out var dverger) && (!dverger.HiddenUntilKnown || RowOf(cat, none, dverger) != null))
        {
            problems.Add("creature 'DvergerTest' is listed");
        }
        foreach (var e in cat.Entries)
        {
            if (e.Kind != EntryKind.Item || e.HiddenUntilKnown || e.Item.Drop == null || e.Item.Drop.m_itemData == null)
            {
                continue;
            }
            if (e.Item.Drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Customization)
            {
                problems.Add($"hair or beard {e} is listed");
            }
        }
        SelfTest.Note(ListedName, "never listed for a new character: " + string.Join(", ", notListed) + $"; DvergerTest "
                                  + $"{(dverger == null ? "no entry" : dverger.HiddenUntilKnown ? "hidden until known" : "LISTED")}; "
                                  + $"{stations} stations and {upgrades} station upgrades under Building; traders found {cat.TraderCount}");
        SelfTest.Note(ListedName, "hidden until known (" + cat.HiddenCount + "): "
                                  + string.Join(", ", cat.Entries.Where(e => e.HiddenUntilKnown).Select(e => e.PrefabName + " [" + (e.HiddenRule ?? "?") + "]")));
        SelfTest.Note(ListedName, "listed items with no source found: "
                                  + string.Join(", ", cat.Entries.Where(e => e.Kind == EntryKind.Item && !e.HiddenUntilKnown && !e.Item.HasSource).Select(e => e.PrefabName)));

        // Megingjord picked up (inventory path: Player.OnInventoryChanged): discovered, "Where to get it: Sold by a trader".
        var belt = cat.FindItemByPrefab("BeltStrength");
        if (belt != null)
        {
            var state = PlayerState.Capture(player);
            var profile = ProfileState.Capture();
            string given = null;
            try
            {
                Forget(player, belt);
                if (Real(cat).IsKnown(belt))
                {
                    problems.Add("Megingjord still discovered after forgetting it (test setup)");
                }
                given = TestKit.GiveItem(player, "BeltStrength", 1);
                if (given == null)
                {
                    problems.Add("could not put a Megingjord in the inventory");
                }
                else
                {
                    var k = Real(cat);
                    if (!k.IsKnown(belt))
                    {
                        problems.Add("Megingjord not discovered after it entered the inventory");
                    }
                    var view = DetailBuilder.Build(belt, k);
                    var block = TestKit.Block(view, Labels.WhereToGet);
                    if (block == null || !block.Any(l => l.Text() == Labels.SoldByTrader))
                    {
                        problems.Add($"Megingjord details miss \"{Labels.WhereToGet}: {Labels.SoldByTrader}\" (lines: {Join(TestKit.Texts(view).Take(12))})");
                    }
                }
            }
            finally
            {
                TestKit.TakeItem(player, given, 1);
                state.Restore();
                profile.Restore();
                TestKit.ClearUnlockPopups();
            }
        }
        return $"{cat.Entries.Count} entries in {Tabs.Count} filled tabs; {stations} stations and {upgrades} upgrades under Building; trader "
               + "items, Piggy and Hen are \"???\" rows; debug, attack and hair prefabs not listed; a picked up Megingjord says "
               + "\"Sold by a trader\"";
    }

    // ---------------------------------------------------------------- compendium.item-details (T10)

    // What the crafting panel charge for one quality: every requirement with an amount, upgrade-station ones apart.
    private static List<string> ExpectedCost(Catalog cat, Knowledge k, Recipe recipe, int quality, out List<string> upgrader)
    {
        var normal = new List<string>();
        upgrader = new List<string>();
        foreach (var req in recipe.m_resources)
        {
            if (req == null || req.m_resItem == null)
            {
                continue;
            }
            var amount = req.GetAmount(quality);
            if (amount <= 0)
            {
                continue;
            }
            var name = Presentation.Name(cat.ItemOf(req.m_resItem), k);
            (req.m_upgraderResource ? upgrader : normal).Add(name + " ×" + amount.ToString(Inv));
        }
        return normal;
    }

    private static string CheckItemDetails(Catalog cat, List<string> problems)
    {
        var player = Player.m_localPlayer;
        var all = Knowledge.Take(cat, true);
        var done = new List<string>();

        // Bronze Sword: stats text, recipe head and cost, upgrade rows, all from the game's own Recipe object.
        var sword = Item(cat, "SwordBronze", problems);
        var recipe = sword != null ? sword.Item.Recipes.FirstOrDefault(r => r != null && !r.m_noCraftOnlyUpgrade) : null;
        if (sword != null && recipe == null)
        {
            problems.Add("the Bronze Sword has no craftable recipe in this game's data");
        }
        if (recipe != null)
        {
            var view = DetailBuilder.Build(sword, all);
            if (view.Failed || view.FailedBlocks > 0)
            {
                problems.Add($"Bronze Sword details failed: {Join(view.Errors)}");
            }
            var shared = sword.Item.Drop.m_itemData.m_shared;
            var description = Names.StripTags(Names.Localize(shared.m_description)).Trim();
            var first = view.Lines.Count > 0 ? view.Lines[0] : null;
            var stats = first != null && first.Kind == DetailLineKind.Paragraph && first.Parts.Count > 0 && first.Parts[0].OwnText
                ? Names.StripTags(first.Text())
                : null;
            if (stats == null || description.Length == 0 || stats.IndexOf(description, StringComparison.Ordinal) < 0
                || stats.Length <= description.Length)
            {
                problems.Add($"Bronze Sword details do not start with its stats text (description + stats): '{(stats != null && stats.Length > 80 ? stats.Substring(0, 80) : stats)}'");
            }

            var crafting = TestKit.Block(view, Names.StripTags(Names.Localize(Labels.Crafting)));
            var station = cat.StationOf(recipe.m_craftingStation);
            var forge = Piece(cat, "forge", problems);
            if (station == null || !ReferenceEquals(station, forge))
            {
                problems.Add($"the Bronze Sword recipe's station is {(station != null ? station.ToString() : "none")}, not the Forge (prefab data)");
            }
            var expected = new List<string>
            {
                station != null
                    ? Labels.At + Names.StripTags(station.DisplayName) + string.Format(Inv, Labels.LevelFormat, recipe.GetRequiredStationLevel(1))
                    : Labels.ByHand,
            };
            if (recipe.m_amount > 1)
            {
                expected.Add(string.Format(Inv, Labels.MakesFormat, recipe.m_amount));
            }
            if (recipe.m_requireOnlyOneIngredient)
            {
                expected.Add(Labels.AnyOneOf);
            }
            expected.AddRange(ExpectedCost(cat, all, recipe, 1, out _).Select(Names.StripTags));
            var got = crafting != null ? crafting.Select(l => Names.StripTags(l.Text())).ToList() : new List<string>();
            if (sword.Item.Recipes.Count(r => r != null) == 1 && !got.SequenceEqual(expected))
            {
                problems.Add($"Bronze Sword crafting block is [{Join(got)}], the game's recipe says [{Join(expected)}]");
            }
            else if (sword.Item.Recipes.Count(r => r != null) != 1 && !expected.All(got.Contains))
            {
                problems.Add($"Bronze Sword crafting block [{Join(got)}] misses lines of [{Join(expected)}]");
            }
            if (got.Contains(Labels.AtUpgradeStation))
            {
                problems.Add($"Bronze Sword crafting cost holds \"{Labels.AtUpgradeStation}\"");
            }

            // Upgrades: one "Quality q (station level L)" row per quality, then that quality's cost.
            var maxQuality = shared.m_maxQuality;
            var upgradeBlock = TestKit.Block(view, Labels.Upgrades);
            var expectedUp = new List<string>();
            var anyUpgrader = recipe.m_resources.Any(r => r != null && r.m_resItem != null && r.m_upgraderResource);
            for (var q = 2; q <= maxQuality; q++)
            {
                var head = string.Format(Inv, Labels.QualityFormat, q);
                var st = recipe.GetRequiredStation(q);
                if (st != null)
                {
                    head += " (" + Presentation.Name(cat.StationOf(st), all) + string.Format(Inv, Labels.StationLevelFormat, recipe.GetRequiredStationLevel(q));
                }
                expectedUp.Add(Names.StripTags(head));
                expectedUp.AddRange(ExpectedCost(cat, all, recipe, q, out var up).Select(Names.StripTags));
                if (up.Count > 0)
                {
                    expectedUp.Add(Labels.AtUpgradeStation);
                    expectedUp.AddRange(up.Select(Names.StripTags));
                }
            }
            if (anyUpgrader && maxQuality > 1)
            {
                expectedUp.Add(string.Format(Inv, Labels.BeyondMaxFormat, maxQuality));
            }
            var gotUp = upgradeBlock != null ? upgradeBlock.Select(l => Names.StripTags(l.Text())).ToList() : new List<string>();
            if (maxQuality > 1 && sword.Item.Recipes.Count(r => r != null) == 1)
            {
                // The block runs to the next header: only its first lines are the upgrade rows.
                if (gotUp.Count < expectedUp.Count || !gotUp.Take(expectedUp.Count).SequenceEqual(expectedUp))
                {
                    problems.Add($"Bronze Sword upgrade rows are [{Join(gotUp.Take(expectedUp.Count + 2))}], the game's recipe says [{Join(expectedUp)}]");
                }
            }
            done.Add($"Bronze Sword: [{Join(expected)}], {Math.Max(0, maxQuality - 1)} upgrade qualities");
        }

        // Every item with an upgrade-station requirement: those rows only under a quality row, then "Beyond quality N".
        var withUpgrader = 0;
        foreach (var e in cat.Entries)
        {
            if (e.Kind != EntryKind.Item || e.Item.Drop == null || e.Item.Drop.m_itemData == null)
            {
                continue;
            }
            var max = e.Item.Drop.m_itemData.m_shared.m_maxQuality;
            if (max <= 1 || !e.Item.Recipes.Any(r => r != null && r.m_resources != null
                                                     && r.m_resources.Any(q => q != null && q.m_resItem != null && q.m_upgraderResource)))
            {
                continue;
            }
            withUpgrader++;
            var texts = TestKit.Texts(DetailBuilder.Build(e, all));
            var beyond = string.Format(Inv, Labels.BeyondMaxFormat, max);
            var firstQuality = texts.FindIndex(t => t.StartsWith(string.Format(Inv, Labels.QualityFormat, 2), StringComparison.Ordinal));
            var firstStation = texts.IndexOf(Labels.AtUpgradeStation);
            if (!texts.Contains(beyond) || (firstStation >= 0 && (firstQuality < 0 || firstStation < firstQuality)))
            {
                problems.Add($"{e}: upgrade-station rows not only under the quality rows, or no \"{beyond}\" line");
                if (problems.Count > 20)
                {
                    break;
                }
            }
        }
        SelfTest.Note(ItemDetailsName, $"{withUpgrader} items have an upgrade-station requirement in this game's data (each checked: rows only "
                                       + "under quality rows, \"Beyond quality N\" line)");

        // Wood: chopped (with biomes), "Used in" list.
        var wood = Item(cat, "Wood", problems);
        if (wood != null)
        {
            var view = DetailBuilder.Build(wood, all);
            var chopped = view.Lines.FirstOrDefault(l => l.Kind == DetailLineKind.Row && l.Text().StartsWith(Labels.Chopped, StringComparison.Ordinal));
            if (chopped == null || !chopped.Parts.Any(p => p.Ref != null && p.Ref.IsBiome && p.Ref.Known))
            {
                problems.Add($"Wood details miss \"{Labels.Chopped} in <biomes>\" (lines: {Join(TestKit.Texts(view).Take(14))})");
            }
            var used = TestKit.Block(view, Labels.UsedIn);
            if (used == null || used.Count == 0)
            {
                problems.Add("Wood details have no \"Used in\" list");
            }
            done.Add($"Wood: '{(chopped != null ? Names.StripTags(chopped.Text()) : "?")}', {(used != null ? used.Count : 0)} \"Used in\" lines");
        }

        // Raspberries: picked in Meadows.
        var rasp = Item(cat, "Raspberry", problems);
        if (rasp != null)
        {
            var view = DetailBuilder.Build(rasp, all);
            var picked = view.Lines.FirstOrDefault(l => l.Kind == DetailLineKind.Row && l.Text().StartsWith(Labels.Picked + Labels.In, StringComparison.Ordinal));
            if (picked == null || !picked.Parts.Any(p => p.Ref != null && p.Ref.IsBiome && p.Ref.Biome == Heightmap.Biome.Meadows))
            {
                problems.Add($"Raspberries details miss \"Picked in Meadows\" (lines: {Join(TestKit.Texts(view).Take(14))})");
            }
            done.Add($"Raspberries: '{(picked != null ? Names.StripTags(picked.Text()) : "?")}'");

            // Own records: the three profile counters of this item, as numbers.
            var profile = ProfileState.Capture();
            try
            {
                var stats = Game.instance.GetPlayerProfile().m_playerStats[0];
                stats.m_itemPickupStats.Remove(rasp.Key);
                stats.m_itemCraftStats.Remove(rasp.Key);
                stats.m_foodEatenStats.Remove(rasp.Key);
                var none = DetailBuilder.Build(rasp, Only(cat, rasp));
                if (TestKit.Block(none, Labels.YourRecords) != null)
                {
                    problems.Add("\"Your records\" shown for an item never picked up, crafted or eaten");
                }
                stats.m_itemPickupStats[rasp.Key] = 7f;
                stats.m_itemCraftStats[rasp.Key] = 2f;
                stats.m_foodEatenStats[rasp.Key] = 3f;
                var records = TestKit.Block(DetailBuilder.Build(rasp, Only(cat, rasp)), Labels.YourRecords);
                var texts = records != null ? records.Select(l => l.Text()).ToList() : new List<string>();
                var want = new List<string>
                {
                    string.Format(Inv, Labels.PickedUpFormat, 7), string.Format(Inv, Labels.CraftedFormat, 2), string.Format(Inv, Labels.EatenFormat, 3),
                };
                if (texts.Count < 3 || !texts.Take(3).SequenceEqual(want))
                {
                    problems.Add($"own records show [{Join(texts)}], the profile says [{Join(want)}]");
                }
            }
            finally
            {
                profile.Restore();
            }
        }
        return Join(done) + "; own records follow the profile counters";
    }

    // ---------------------------------------------------------------- compendium.piece-details (T11)

    private static string CheckPieceDetails(Catalog cat, List<string> problems)
    {
        var all = Knowledge.Take(cat, true);
        var done = new List<string>();
        var bench = Piece(cat, "piece_workbench", problems);
        if (bench != null && bench.Piece.Piece == null)
        {
            problems.Add("the Workbench entry has no Piece component");
        }
        if (bench != null && bench.Piece.Piece != null)
        {
            var piece = bench.Piece.Piece;
            var view = DetailBuilder.Build(bench, all);
            var expected = new List<string>();
            foreach (var req in piece.m_resources)
            {
                if (req != null && req.m_resItem != null && req.m_amount > 0)
                {
                    expected.Add(Names.StripTags(Presentation.Name(cat.ItemOf(req.m_resItem), all)) + " ×" + req.m_amount.ToString(Inv));
                }
            }
            if (piece.m_craftingStation != null)
            {
                expected.Add(Labels.NeedsA + Names.StripTags(Presentation.Name(cat.StationOf(piece.m_craftingStation), all)) + Labels.Nearby);
            }
            var cost = TestKit.Block(view, Labels.BuildCost);
            var got = cost != null ? cost.Select(l => Names.StripTags(l.Text())).ToList() : new List<string>();
            if (expected.Count == 0 || got.Count < expected.Count || !got.Take(expected.Count).SequenceEqual(expected))
            {
                problems.Add($"Workbench build cost is [{Join(got)}], the piece says [{Join(expected)}]");
            }
            var comfort = string.Format(Inv, Labels.ComfortFormat, piece.m_comfort);
            var hasComfort = TestKit.Texts(view).Contains(comfort);
            if (hasComfort != (piece.m_comfort > 0))
            {
                problems.Add($"Workbench comfort line shown {hasComfort}, the piece's comfort is {piece.m_comfort}");
            }
            done.Add($"Workbench cost [{Join(expected)}], comfort {piece.m_comfort}");
        }

        var forge = Piece(cat, "forge", problems);
        if (forge != null && forge.Piece.Piece == null)
        {
            problems.Add("the Forge entry has no Piece component");
        }
        if (forge != null && forge.Piece.Piece != null)
        {
            var piece = forge.Piece.Piece;
            var info = forge.Piece;
            var view = DetailBuilder.Build(forge, all);
            var texts = TestKit.Texts(view);
            var needs = piece.m_craftingStation != null
                ? Labels.NeedsA + Names.StripTags(Presentation.Name(cat.StationOf(piece.m_craftingStation), all)) + Labels.Nearby
                : null;
            var hasNeeds = texts.Any(t => t.StartsWith(Labels.NeedsA, StringComparison.Ordinal));
            if ((needs != null && !texts.Contains(needs)) || (needs == null && hasNeeds))
            {
                problems.Add($"Forge \"Needs a ...\" line: expected {(needs ?? "none")}, lines: {Join(texts.Take(10))}");
            }
            // Upgrades: one row per station upgrade.
            var ups = TestKit.Block(view, Labels.StationUpgrades);
            var upNames = info.Extensions.Select(x => Names.StripTags(x.DisplayName)).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var gotUps = ups != null ? ups.Select(l => Names.StripTags(l.Text())).OrderBy(n => n, StringComparer.Ordinal).ToList() : new List<string>();
            if (upNames.Count == 0 || !gotUps.SequenceEqual(upNames))
            {
                problems.Add($"Forge upgrades are [{Join(gotUps)}], the catalog says [{Join(upNames)}]");
            }
            // Recipes at this station: one row per item made here, lowest level, A to Z.
            var levels = new Dictionary<Entry, int>();
            foreach (var r in info.RecipesHere)
            {
                var item = r != null && !r.m_noCraftOnlyUpgrade ? cat.ItemOf(r.m_item) : null;
                if (item == null)
                {
                    continue;
                }
                var lvl = r.GetRequiredStationLevel(1);
                if (!levels.TryGetValue(item, out var old) || lvl < old)
                {
                    levels[item] = lvl;
                }
            }
            var wantRecipes = levels.Select(kv => Names.StripTags(kv.Key.DisplayName) + string.Format(Inv, Labels.LevelFormat, kv.Value)).ToList();
            var here = TestKit.Block(view, Labels.RecipesHere);
            var gotRecipes = here != null ? here.Select(l => Names.StripTags(l.Text())).ToList() : new List<string>();
            if (wantRecipes.Count == 0 || gotRecipes.Count != wantRecipes.Count || wantRecipes.Any(w => !gotRecipes.Contains(w)))
            {
                problems.Add($"Forge \"{Labels.RecipesHere}\": {gotRecipes.Count} rows, the game's recipes give {wantRecipes.Count} "
                             + $"(first rows: {Join(gotRecipes.Take(4))})");
            }
            string NameOnly(string row) => row.LastIndexOf(" (", StringComparison.Ordinal) > 0 ? row.Substring(0, row.LastIndexOf(" (", StringComparison.Ordinal)) : row;
            for (var i = 1; i < gotRecipes.Count; i++)
            {
                if (Names.CompareNames(NameOnly(gotRecipes[i - 1]), NameOnly(gotRecipes[i])) > 0)
                {
                    problems.Add($"Forge recipes not in A to Z order: '{gotRecipes[i - 1]}' before '{gotRecipes[i]}'");
                    break;
                }
            }
            done.Add($"Forge: needs '{needs ?? "nothing"}', {upNames.Count} upgrades, {wantRecipes.Count} recipes");
        }
        return Join(done);
    }

    // ---------------------------------------------------------------- compendium.creature-details (T12)

    private static string CheckCreatureDetails(Catalog cat, List<string> problems)
    {
        var boar = Creature(cat, "Boar", problems);
        if (boar == null)
        {
            return "";
        }
        var info = boar.Creature;
        var profile = ProfileState.Capture();
        var kills = Game.instance.GetPlayerProfile().m_playerStats[0].m_enemyStats[0];
        try
        {
            // Everything discovered, nothing revealed by the setting: the first-kill tier follows the real kill count.
            var k = Knowledge.Synthetic(cat, _ => true, Biomes.AllKnownFlags);
            kills.Remove(boar.Key);
            var view = DetailBuilder.Build(boar, Knowledge.Synthetic(cat, _ => true, Biomes.AllKnownFlags));
            var texts = TestKit.Texts(view);
            if (view.Failed || view.FailedBlocks > 0)
            {
                problems.Add($"Boar details failed: {Join(view.Errors)}");
            }
            if (!texts.Contains(string.Format(Inv, Labels.KilledFormat, 0)))
            {
                problems.Add("Boar details with no kill miss \"Killed: 0\"");
            }
            // Habitat: Meadows.
            var habitat = TestKit.Block(view, Labels.Habitat);
            if (habitat == null || !habitat.Any(l => l.Parts.Any(p => p.Ref != null && p.Ref.IsBiome && p.Ref.Biome == Heightmap.Biome.Meadows && p.Ref.Known)))
            {
                problems.Add($"Boar habitat does not list Meadows (habitat {info.Habitat})");
            }
            // Drops: one row per drop of the prefab's CharacterDrop, with amount and chance.
            var drops = TestKit.Block(view, Labels.Drops);
            var gotDrops = drops != null ? drops.Select(l => Names.StripTags(l.Text())).ToList() : new List<string>();
            var wantDrops = info.Drops.Select(d => Names.StripTags(Presentation.Name(d.Item, k)) + " " + DropText(d.Drop)).ToList();
            if (wantDrops.Count == 0 || !gotDrops.SequenceEqual(wantDrops))
            {
                problems.Add($"Boar drops are [{Join(gotDrops)}], the prefab says [{Join(wantDrops)}]");
            }
            // One row per listed item the Boar prefabs drop (their CharacterDrop lists, each item once).
            var dropped = new HashSet<Entry>();
            foreach (var go in info.Prefabs)
            {
                if (go == null || !go.TryGetComponent<CharacterDrop>(out var characterDrop) || characterDrop.m_drops == null)
                {
                    continue;
                }
                foreach (var d in characterDrop.m_drops)
                {
                    var dropItem = d != null && d.m_prefab != null ? cat.ItemOf(d.m_prefab) : null;
                    if (dropItem != null)
                    {
                        dropped.Add(dropItem);
                    }
                }
            }
            if (dropped.Count != wantDrops.Count || info.Drops.Any(d => !dropped.Contains(d.Item)))
            {
                problems.Add($"Boar: {wantDrops.Count} drop rows, its CharacterDrop lists hold {dropped.Count} listed items");
            }
            // Taming food.
            var taming = TestKit.Block(view, Labels.Taming);
            var tamingTexts = taming != null ? taming.Select(l => Names.StripTags(l.Text())).ToList() : new List<string>();
            var ai = boar.Prefab != null ? boar.Prefab.GetComponent<MonsterAI>() : null;
            var foods = ai != null && ai.m_consumeItems != null
                ? ai.m_consumeItems.Where(i => i != null && cat.ItemOf(i) != null).Select(i => Names.StripTags(cat.ItemOf(i).DisplayName)).ToList()
                : new List<string>();
            if (!info.Tameable || tamingTexts.Count == 0 || tamingTexts[0] != Labels.CanBeTamed || foods.Count == 0
                || foods.Any(f => !tamingTexts.Contains(f)))
            {
                problems.Add($"Boar taming block is [{Join(tamingTexts)}], its MonsterAI eats [{Join(foods)}]");
            }
            // Health and damage modifiers: only after the first kill.
            var health = string.Format(Inv, Labels.HealthFormat, Math.Round(info.Character.m_health).ToString("0", Inv));
            if (texts.Contains(Labels.AfterFirstKill) || texts.Contains(health))
            {
                problems.Add("Boar details show health / damage modifiers before the first kill");
            }
            kills[boar.Key] = 1f;
            var after = TestKit.Texts(DetailBuilder.Build(boar, Knowledge.Synthetic(cat, _ => true, Biomes.AllKnownFlags)));
            if (!after.Contains(Labels.AfterFirstKill) || !after.Contains(health) || !after.Contains(string.Format(Inv, Labels.KilledFormat, 1)))
            {
                problems.Add($"Boar details after one kill miss \"{Labels.AfterFirstKill}\", \"{health}\" or \"Killed: 1\" (lines: {Join(after.Skip(Math.Max(0, after.Count - 8)))})");
            }
            return $"Boar: habitat Meadows, drops [{Join(wantDrops)}], eats [{Join(foods)}], \"{health}\" only after the first kill";
        }
        finally
        {
            profile.Restore();
        }
    }

    // ---------------------------------------------------------------- compendium.afterboss (T12, T13)

    private static string CheckAfterBoss(Catalog cat, List<string> problems)
    {
        var gated = cat.Entries.Where(e => e.Kind == EntryKind.Creature && e.Creature.AfterBosses.Count > 0).ToList();
        SelfTest.Note(AfterBossName, $"creatures that need a defeated boss to spawn ({gated.Count}): "
                                     + string.Join(", ", gated.Select(e => e.PrefabName + " <- " + string.Join("+", e.Creature.AfterBosses.Select(b => b.PrefabName)))));
        if (gated.Count == 0)
        {
            problems.Add("no creature of this game's data needs a defeated boss to spawn: the \"Appears after defeating ...\" line cannot be checked");
            return "";
        }
        var all = Knowledge.Take(cat, true);
        foreach (var c in gated)
        {
            var boss = c.Creature.AfterBosses[0];
            var revealed = TestKit.Texts(DetailBuilder.Build(c, all));
            var want = Labels.AppearsAfter + Names.StripTags(boss.DisplayName);
            if (!revealed.Contains(want))
            {
                problems.Add($"{c}: no \"{want}\" line (lines: {Join(revealed.Take(8))})");
            }
            var hidden = TestKit.LabelTexts(DetailBuilder.Build(c, Only(cat, c)));
            if (!hidden.Contains(Labels.AppearsAfter + Labels.Unknown) || hidden.Any(t => TestKit.ContainsWord(t, boss.SortKey)))
            {
                problems.Add($"{c}: with the boss undiscovered the line is not \"{Labels.AppearsAfter}{Labels.Unknown}\" (lines: {Join(hidden.Take(8))})");
            }
        }
        return $"{gated.Count} creature(s) say \"Appears after defeating <boss>\", and \"... ???\" while the boss is undiscovered";
    }

    // ---------------------------------------------------------------- compendium.obfuscated (T13)

    private static string CheckObfuscated(Catalog cat, List<string> problems)
    {
        var done = new List<string>();

        // Unknown drop and biome: "???" with the numbers kept.
        var boar = Creature(cat, "Boar", problems);
        if (boar != null)
        {
            var view = DetailBuilder.Build(boar, Only(cat, boar));
            var drops = TestKit.Block(view, Labels.Drops);
            var got = drops != null ? drops.Select(l => l.Text()).ToList() : new List<string>();
            var want = boar.Creature.Drops.Select(d => Labels.Unknown + " " + DropText(d.Drop)).ToList();
            if (want.Count == 0 || !got.SequenceEqual(want))
            {
                problems.Add($"Boar drops with nothing else discovered are [{Join(got)}], expected [{Join(want)}]");
            }
            var habitat = TestKit.Block(view, Labels.Habitat);
            if (habitat == null || habitat.Count == 0 || habitat.Any(l => l.Text() != Labels.Unknown && l.Text() != Labels.HabitatUnknown
                                                                          && !l.Text().StartsWith(Labels.AppearsAfter, StringComparison.Ordinal)))
            {
                problems.Add($"Boar habitat with no biome discovered is [{Join(habitat != null ? habitat.Select(l => l.Text()) : new string[0])}], expected \"???\" rows");
            }
            done.Add($"drops [{Join(got)}]");
        }

        // Unknown station and ingredients: "At ??? (level N)", "??? ×N".
        var sword = Item(cat, "SwordBronze", problems);
        var recipe = sword != null ? sword.Item.Recipes.FirstOrDefault(r => r != null && !r.m_noCraftOnlyUpgrade) : null;
        if (sword != null && (recipe == null || recipe.m_craftingStation == null))
        {
            problems.Add("the Bronze Sword has no recipe at a station in this game's data: the \"At ??? (level N)\" line cannot be checked");
        }
        if (recipe != null && recipe.m_craftingStation != null)
        {
            var texts = TestKit.Texts(DetailBuilder.Build(sword, Only(cat, sword)));
            var head = Labels.At + Labels.Unknown + string.Format(Inv, Labels.LevelFormat, recipe.GetRequiredStationLevel(1));
            if (!texts.Contains(head))
            {
                problems.Add($"Bronze Sword with the station undiscovered: no \"{head}\" (lines: {Join(texts.Take(8))})");
            }
            foreach (var req in recipe.m_resources)
            {
                if (req == null || req.m_resItem == null || req.m_upgraderResource || req.GetAmount(1) <= 0)
                {
                    continue;
                }
                var line = Labels.Unknown + " ×" + req.GetAmount(1).ToString(Inv);
                if (!texts.Contains(line))
                {
                    problems.Add($"Bronze Sword with its ingredients undiscovered: no \"{line}\"");
                }
            }
            done.Add($"'{head}'");
        }

        // Unknown biome in a source line.
        var rasp = Item(cat, "Raspberry", problems);
        if (rasp != null)
        {
            var texts = TestKit.Texts(DetailBuilder.Build(rasp, Only(cat, rasp)));
            var meadows = Names.StripTags(Names.Localize(Biomes.Token(Heightmap.Biome.Meadows)));
            if (!texts.Any(t => t.StartsWith(Labels.Picked + Labels.In + Labels.Unknown, StringComparison.Ordinal))
                || texts.Any(t => t.StartsWith(Labels.Picked, StringComparison.Ordinal) && TestKit.ContainsWord(t, meadows)))
            {
                problems.Add($"Raspberries with no biome discovered: no \"Picked in ???\" (lines: {Join(texts.Take(8))})");
            }
        }

        // "Used in": every target unknown = one folded line with the count.
        var wood = Item(cat, "Wood", problems);
        if (wood != null)
        {
            var view = DetailBuilder.Build(wood, Only(cat, wood));
            var used = TestKit.Block(view, Labels.UsedIn);
            var targets = wood.Item.UsedIn.Where(u => u.Target != null && !ReferenceEquals(u.Target, wood)).Select(u => u.Target).Distinct().Count();
            var folded = used != null ? used.Where(l => l.Kind == DetailLineKind.Collapsed).ToList() : new List<DetailLine>();
            if (targets < 2 || folded.Count != 1 || folded[0].CollapsedCount != targets
                || folded[0].Text() != string.Format(Inv, Labels.CollapsedFormat, targets))
            {
                problems.Add($"Wood \"Used in\" with nothing else discovered: {folded.Count} folded line(s) "
                             + $"'{(folded.Count > 0 ? folded[0].Text() : "")}', expected one \"{string.Format(Inv, Labels.CollapsedFormat, targets)}\"");
            }
            if (used != null && used.Any(l => l.Kind == DetailLineKind.Row && !l.Text().Contains(Labels.Unknown)))
            {
                problems.Add("Wood \"Used in\" names a target while nothing else is discovered");
            }
            done.Add($"Wood used in: '{(folded.Count > 0 ? folded[0].Text() : "?")}'");
        }

        // Smelter: conversions folded while no ore or bar is discovered; Copper Ore known = "Turns Copper Ore into ???".
        var smelter = Piece(cat, "smelter", problems);
        var copper = Item(cat, "CopperOre", problems);
        if (smelter != null)
        {
            var conversions = smelter.Piece.Conversions;
            var view = DetailBuilder.Build(smelter, Only(cat, smelter));
            var block = TestKit.Block(view, Labels.Conversions);
            if (conversions.Count == 0 || block == null || block.Count != 1 || block[0].Kind != DetailLineKind.Collapsed
                || block[0].CollapsedCount != conversions.Count)
            {
                problems.Add($"Smelter conversions with no ore or bar discovered: [{Join(block != null ? block.Select(l => l.Text()) : new string[0])}], "
                             + $"expected one folded line for {conversions.Count} conversions");
            }
            var all = TestKit.LabelTexts(view);
            foreach (var cv in conversions)
            {
                foreach (var side in new[] { cv.From, cv.To })
                {
                    if (side != null && all.Any(t => TestKit.ContainsWord(t, side.SortKey)))
                    {
                        problems.Add($"Smelter details name the undiscovered {side}");
                    }
                }
            }
            if (copper != null)
            {
                var fromCopper = conversions.Count(c => ReferenceEquals(c.From, copper));
                var withOre = DetailBuilder.Build(smelter, Only(cat, smelter, copper));
                var texts = TestKit.Texts(withOre);
                var want = Labels.Turns + Names.StripTags(copper.DisplayName) + Labels.Into + Labels.Unknown;
                var folded = withOre.Lines.Where(l => l.Kind == DetailLineKind.Collapsed).Sum(l => l.CollapsedCount);
                if (fromCopper == 0 || !texts.Contains(want) || folded != conversions.Count - fromCopper)
                {
                    problems.Add($"Smelter with Copper Ore discovered: no \"{want}\" or wrong fold ({folded} folded of {conversions.Count}, "
                                 + $"{fromCopper} from Copper Ore; lines: {Join(texts.Skip(Math.Max(0, texts.Count - 6)))})");
                }
                done.Add($"Smelter: {conversions.Count} conversions folded, then '{want}'");
            }
        }

        // Lox met, saddle undiscovered: "Can wear: ???".
        var lox = Creature(cat, "Lox", problems);
        if (lox != null)
        {
            var saddle = lox.Creature.Saddle;
            var texts = TestKit.LabelTexts(DetailBuilder.Build(lox, Only(cat, lox)));
            if (saddle == null)
            {
                problems.Add("the Lox has no saddle item in this game's data (prefab data): \"Can wear\" cannot be checked");
            }
            else if (!texts.Contains(Labels.CanWear + Labels.Unknown) || texts.Any(t => TestKit.ContainsWord(t, saddle.SortKey)))
            {
                problems.Add($"Lox with the saddle undiscovered: no \"{Labels.CanWear}{Labels.Unknown}\", or the saddle is named (lines: {Join(texts)})");
            }
            done.Add("Lox: 'Can wear: ???'");
        }
        return Join(done);
    }

    // ---------------------------------------------------------------- compendium.eggs (T33)

    private static string CheckEggs(Catalog cat, List<string> problems)
    {
        var player = Player.m_localPlayer;
        var egg = Item(cat, "ChickenEgg", problems);
        var hen = Creature(cat, "Hen", problems);
        if (player == null || egg == null || hen == null)
        {
            return "";
        }
        var state = PlayerState.Capture(player);
        var profile = ProfileState.Capture();
        string given = null;
        try
        {
            Forget(player, egg);
            Forget(player, hen);
            given = TestKit.GiveItem(player, "ChickenEgg", 1);
            if (given == null)
            {
                problems.Add("could not put a Chicken Egg in the inventory");
                return "";
            }
            var k = Real(cat);
            if (!k.IsKnown(egg) || k.IsKnown(hen))
            {
                problems.Add($"setup: egg discovered {k.IsKnown(egg)}, Hen discovered {k.IsKnown(hen)} (expected true / false)");
            }
            var before = TestKit.LabelTexts(DetailBuilder.Build(egg, k));
            if (!before.Contains(Labels.LaidBy + Labels.Unknown) || before.Any(t => TestKit.ContainsWord(t, hen.SortKey)))
            {
                problems.Add($"egg details with the Hen undiscovered: no \"{Labels.LaidBy}{Labels.Unknown}\" (lines: {Join(before)})");
            }
            OwnRecords.MarkSeen(hen.Key);
            var after = TestKit.Texts(DetailBuilder.Build(egg, Real(cat)));
            var want = Labels.LaidBy + Names.StripTags(hen.DisplayName);
            if (!after.Contains(want))
            {
                problems.Add($"egg details with the Hen discovered: no \"{want}\" (lines: {Join(after)})");
            }
            return $"\"{Labels.LaidBy}{Labels.Unknown}\", then \"{want}\" once the Hen is discovered";
        }
        finally
        {
            TestKit.TakeItem(player, given, 1);
            state.Restore();
            profile.Restore();
            TestKit.ClearUnlockPopups();
        }
    }

    // ---------------------------------------------------------------- compendium.fishing (T34)

    private static string SkillText(Player player)
    {
        var data = player.GetSkills().m_skillData;
        return string.Join(", ", data.OrderBy(kv => (int)kv.Key)
            .Select(kv => $"{kv.Key} {(kv.Value != null ? kv.Value.m_level : -1f):0.##}/{(kv.Value != null ? kv.Value.m_accumulator : -1f):0.##}"));
    }

    private static string CheckFishing(Catalog cat, List<string> problems)
    {
        var player = Player.m_localPlayer;
        var rod = Item(cat, "FishingRod", problems);
        if (player == null || rod == null || player.GetSkills() == null)
        {
            problems.Add("no Fishing Rod entry or no skills on the character");
            return "";
        }
        var state = PlayerState.Capture(player);
        try
        {
            var data = player.GetSkills().m_skillData;
            // A character with no Fishing skill yet (another test may have given the probe one).
            data.Remove(Skills.SkillType.Fishing);
            var before = SkillText(player);
            var removedBefore = DetailBuilder.SkillEntriesRemoved;
            var view = DetailBuilder.Build(rod, Knowledge.Take(cat, true));
            var removed = DetailBuilder.SkillEntriesRemoved - removedBefore;
            if (!view.Known || view.Failed || view.FailedBlocks > 0 || view.Lines.Count == 0)
            {
                problems.Add($"Fishing Rod details not built with everything revealed ({Join(view.Errors)})");
            }
            if (data.ContainsKey(Skills.SkillType.Fishing))
            {
                problems.Add("reading the Fishing Rod's details gave the character a Fishing skill entry");
            }
            var after = SkillText(player);
            if (after != before)
            {
                problems.Add($"reading the Fishing Rod's details changed the skills: [{before}] -> [{after}]");
            }
            SelfTest.Note(FishingName, $"level-0 skill entries the rod's tooltip added and the details builder removed: {removed} "
                                       + $"({(removed > 0 ? "the tooltip does add one, as vanilla hovering does" : "none in this game's data")})");
            return $"Fishing Rod details read with everything revealed: no Fishing skill entry, skills unchanged ({removed} entry removed again)";
        }
        finally
        {
            state.Restore();
        }
    }

    // ---------------------------------------------------------------- compendium.pickup (T07)

    // Items enter the inventory the way a pickup ends (Player.OnInventoryChanged -> AddKnownItem ->
    // UpdateKnownRecipesList). All in one frame.
    private static string CheckPickup(Catalog cat, List<string> problems)
    {
        var player = Player.m_localPlayer;
        var rasp = Item(cat, "Raspberry", problems);
        var wood = Item(cat, "Wood", problems);
        var club = Item(cat, "Club", problems);
        if (player == null || problems.Count > 0)
        {
            return "";
        }
        var state = PlayerState.Capture(player);
        var profile = ProfileState.Capture();
        var given = new List<KeyValuePair<string, int>>();
        try
        {
            foreach (var e in new[] { rasp, wood, club })
            {
                Forget(player, e);
            }
            var k0 = Real(cat);
            if (k0.IsKnown(rasp) || k0.IsKnown(club) || k0.IsKnown(wood))
            {
                problems.Add("setup: Raspberries, Wood or the Club still discovered after forgetting them");
            }
            Give(player, "Raspberry", 1, given, problems);
            if (!Real(cat).IsKnown(rasp))
            {
                problems.Add("Raspberries not discovered after entering the inventory");
            }
            // Holding Wood teaches the Club recipe; the Club itself was never held.
            Give(player, "Wood", 10, given, problems);
            var k = Real(cat);
            var needs = string.Join(", ", club.Item.Recipes.Where(r => r != null).SelectMany(r => r.m_resources)
                .Where(q => q != null && q.m_resItem != null).Select(q => q.m_resItem.name).Distinct());
            if (!player.m_knownRecipes.Contains(club.Key) || player.m_knownMaterial.Contains(club.Key) || !k.IsKnown(club))
            {
                problems.Add($"Club not discovered by its recipe while holding Wood (recipe known {player.m_knownRecipes.Contains(club.Key)}, "
                             + $"held {player.m_knownMaterial.Contains(club.Key)}; its recipe needs {needs})");
            }
            return $"Raspberries discovered by entering the inventory; the Club (recipe needs {needs}) discovered by its recipe while holding Wood, never held";
        }
        finally
        {
            foreach (var g in given)
            {
                TestKit.TakeItem(player, g.Key, g.Value);
            }
            state.Restore();
            profile.Restore();
            TestKit.ClearUnlockPopups();
            player.UpdateEvents();
        }
    }

    // ---------------------------------------------------------------- compendium.discover (T08)

    // Real paths: items enter the inventory (Player.OnInventoryChanged -> AddKnownItem -> UpdateKnownRecipesList), a
    // station is seen by standing next to it (Player.UpdateStations -> CraftingStation.UpdateKnownStationsInRange).
    private static IEnumerator RunDiscover()
    {
        var box = new TestKit.CatalogBox();
        yield return TestKit.WaitCatalog(box);
        var player = Player.m_localPlayer;
        if (box.Value == null || player == null)
        {
            SelfTest.Fail(DiscoverName, box.Error ?? "no local player");
            yield break;
        }
        var cat = box.Value;
        var problems = new List<string>();
        var wood = Item(cat, "Wood", problems);
        var hammer = Item(cat, "Hammer", problems);
        var bench = Piece(cat, "piece_workbench", problems);
        var forge = Piece(cat, "forge", problems);
        var deer = Creature(cat, "Deer", problems);
        var trophyDeer = Item(cat, "TrophyDeer", problems);
        if (problems.Count > 0)
        {
            TestKit.Report(DiscoverName, problems, "");
            yield break;
        }
        var state = PlayerState.Capture(player);
        var profile = ProfileState.Capture();
        var given = new List<KeyValuePair<string, int>>();
        GameObject station = null;
        var done = new List<string>();
        try
        {
            foreach (var e in new[] { wood, hammer, bench, forge, deer, trophyDeer })
            {
                Forget(player, e);
            }
            var k0 = Real(cat);
            foreach (var e in new[] { bench, forge, deer })
            {
                if (k0.IsKnown(e))
                {
                    problems.Add($"setup: {e} still discovered after forgetting it");
                }
            }

            // T08: Hammer + Wood carried = Workbench buildable.
            Give(player, "Wood", 10, given, problems);
            Give(player, "Hammer", 1, given, problems);
            if (!player.m_knownRecipes.Contains(bench.Key) || !Real(cat).IsKnown(bench))
            {
                problems.Add("Workbench not discovered while carrying a Hammer and Wood");
            }
            done.Add("Workbench by Hammer + Wood");

            // T08: Deer by its trophy only (never met, never killed).
            Give(player, "TrophyDeer", 1, given, problems);
            var k = Real(cat);
            if (!k.IsKnown(deer) || OwnRecords.GetSeen(player).Contains(deer.Key) || k.Kills(deer) != 0)
            {
                problems.Add($"Deer by its trophy: discovered {k.IsKnown(deer)}, met {OwnRecords.GetSeen(player).Contains(deer.Key)}, kills {k.Kills(deer)}");
            }
            done.Add("Deer by holding its trophy");

            // T08: Forge by standing next to one.
            var stationName = forge.Piece.StationName;
            var pos = TestKit.PointNear(player, 3f, 0f);
            station = TestKit.Spawn("forge", pos, Quaternion.identity);
            if (station == null)
            {
                problems.Add("the forge prefab could not be spawned");
            }
            else
            {
                var start = Time.realtimeSinceStartup;
                while (!player.m_knownStations.ContainsKey(stationName) && Time.realtimeSinceStartup - start < 6f)
                {
                    yield return null;
                }
                var distance = Vector3.Distance(station.transform.position, player.transform.position);
                if (!player.m_knownStations.ContainsKey(stationName) || !Real(cat).IsKnown(forge))
                {
                    problems.Add($"Forge not discovered after standing {distance:F1} m from one for {Time.realtimeSinceStartup - start:F1} s");
                }
                done.Add($"Forge by standing {distance:F1} m from one");
            }
        }
        finally
        {
            TestKit.Despawn(station);
            foreach (var g in given)
            {
                TestKit.TakeItem(player, g.Key, g.Value);
            }
            state.Restore();
            profile.Restore();
            TestKit.ClearUnlockPopups();
            player.UpdateEvents();
        }
        yield return null;
        TestKit.Report(DiscoverName, problems, Join(done));
    }

    private static void Give(Player player, string prefab, int amount, List<KeyValuePair<string, int>> given, List<string> problems)
    {
        var token = TestKit.GiveItem(player, prefab, amount);
        if (token == null)
        {
            problems.Add($"could not put {amount} x {prefab} in the inventory");
            return;
        }
        given.Add(new KeyValuePair<string, int>(token, amount));
    }

    // ---------------------------------------------------------------- compendium.smelter (T13)

    private static IEnumerator RunSmelter()
    {
        var box = new TestKit.CatalogBox();
        yield return TestKit.WaitCatalog(box);
        var player = Player.m_localPlayer;
        if (box.Value == null || player == null)
        {
            SelfTest.Fail(SmelterName, box.Error ?? "no local player");
            yield break;
        }
        var cat = box.Value;
        var problems = new List<string>();
        var smelter = Piece(cat, "smelter", problems);
        var bench = Piece(cat, "piece_workbench", problems);
        var copper = Item(cat, "CopperOre", problems);
        if (problems.Count > 0)
        {
            TestKit.Report(SmelterName, problems, "");
            yield break;
        }
        var state = PlayerState.Capture(player);
        var profile = ProfileState.Capture();
        var given = new List<KeyValuePair<string, int>>();
        GameObject station = null;
        var detail = "";
        try
        {
            // New character for this: no ore, no bar, no Smelter, no Workbench seen.
            Forget(player, smelter);
            Forget(player, bench);
            foreach (var cv in smelter.Piece.Conversions)
            {
                Forget(player, cv.From);
                Forget(player, cv.To);
            }
            var benchStation = bench.Piece.StationName;
            station = TestKit.Spawn("piece_workbench", TestKit.PointNear(player, 3f, 0f), Quaternion.identity);
            if (station == null)
            {
                problems.Add("the workbench prefab could not be spawned");
            }
            else
            {
                var start = Time.realtimeSinceStartup;
                while (!player.m_knownStations.ContainsKey(benchStation) && Time.realtimeSinceStartup - start < 6f)
                {
                    yield return null;
                }
                if (!player.m_knownStations.ContainsKey(benchStation))
                {
                    problems.Add("the Workbench was not seen after standing next to one for 6 s");
                }
            }
            Give(player, "Hammer", 1, given, problems);
            Give(player, "Stone", 20, given, problems);
            Give(player, "SurtlingCore", 5, given, problems);
            var k = Real(cat);
            if (!player.m_knownRecipes.Contains(smelter.Key) || !k.IsKnown(smelter))
            {
                var piece = smelter.Piece.Piece;
                var cost = piece != null && piece.m_resources != null
                    ? string.Join(", ", piece.m_resources.Where(r => r != null && r.m_resItem != null).Select(r => r.m_resItem.name + " x" + r.m_amount))
                    : "?";
                problems.Add("the Smelter is not buildable (not discovered) with a seen Workbench while holding a Hammer, Stone and Surtling "
                             + $"Cores; its cost is {cost}, its station {(piece != null && piece.m_craftingStation != null ? piece.m_craftingStation.name : "none")}");
            }
            else
            {
                var view = DetailBuilder.Build(smelter, k);
                var block = TestKit.Block(view, Labels.Conversions);
                var texts = TestKit.LabelTexts(view);
                if (block == null || block.Count != 1 || block[0].Kind != DetailLineKind.Collapsed)
                {
                    problems.Add($"Smelter conversions before any ore: [{Join(block != null ? block.Select(l => l.Text()) : new string[0])}], expected one folded line");
                }
                foreach (var cv in smelter.Piece.Conversions)
                {
                    foreach (var side in new[] { cv.From, cv.To })
                    {
                        if (side != null && texts.Any(t => TestKit.ContainsWord(t, side.SortKey)))
                        {
                            problems.Add($"Smelter details name {side} before it is discovered");
                        }
                    }
                }
                Give(player, "CopperOre", 1, given, problems);
                var after = TestKit.Texts(DetailBuilder.Build(smelter, Real(cat)));
                var want = Labels.Turns + Names.StripTags(copper.DisplayName) + Labels.Into + Labels.Unknown;
                if (!after.Contains(want))
                {
                    problems.Add($"Smelter details after picking up Copper Ore: no \"{want}\" (lines: {Join(after.Skip(Math.Max(0, after.Count - 6)))})");
                }
                detail = $"Workbench seen, Hammer + Stone + Surtling Cores held: Smelter buildable, conversions folded "
                         + $"('{(block != null && block.Count > 0 ? block[0].Text() : "?")}'), then '{want}' with Copper Ore";
            }
        }
        finally
        {
            TestKit.Despawn(station);
            foreach (var g in given)
            {
                TestKit.TakeItem(player, g.Key, g.Value);
            }
            state.Restore();
            profile.Restore();
            TestKit.ClearUnlockPopups();
            player.UpdateEvents();
        }
        yield return null;
        TestKit.Report(SmelterName, problems, detail);
    }

    // ---------------------------------------------------------------- compendium.aim (T06, T08)

    private static void Aim(Player player, Vector3 target)
    {
        var cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
        var from = cam != null ? cam.position : player.m_eye.position;
        var dir = (target - from).normalized;
        var flat = new Vector3(dir.x, 0f, dir.z);
        if (flat.sqrMagnitude < 1e-6f)
        {
            return;
        }
        player.m_lookYaw = Quaternion.LookRotation(flat.normalized);
        player.m_lookPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg, -89f, 89f);
    }

    // First thing the game's crosshair ray hits now (same ray as Player.FindHoverObject, own body skipped).
    private static GameObject CrosshairHit(Player player)
    {
        var cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
        if (cam == null)
        {
            return null;
        }
        var hits = Physics.RaycastAll(cam.position, cam.forward, 50f, player.m_interactMask);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider.attachedRigidbody != null && h.collider.attachedRigidbody.gameObject == player.gameObject)
            {
                continue;
            }
            return h.collider.attachedRigidbody != null ? h.collider.attachedRigidbody.gameObject : h.collider.gameObject;
        }
        return null;
    }

    private static IEnumerator RunAim(string name, string prefab)
    {
        var box = new TestKit.CatalogBox();
        yield return TestKit.WaitCatalog(box);
        var player = Player.m_localPlayer;
        var hud = EnemyHud.instance;
        if (box.Value == null || player == null || hud == null || GameCamera.instance == null)
        {
            SelfTest.Fail(name, box.Error ?? "no local player, EnemyHud or camera");
            yield break;
        }
        var cat = box.Value;
        var problems = new List<string>();
        var done = new List<string>();
        var state = PlayerState.Capture(player);
        var profile = ProfileState.Capture();
        var yaw0 = player.m_lookYaw;
        var pitch0 = player.m_lookPitch;
        GameObject spawned = null;
        GameObject wall = null;
        EnemyHud.HudData data = null;
        try
        {
            for (var once = 0; once < 1; once++)
            {
                var entry = Creature(cat, prefab, problems);
                if (entry == null)
                {
                    continue;
                }
                Forget(player, entry);
                if (Real(cat).IsKnown(entry))
                {
                    problems.Add($"setup: {entry} still discovered after forgetting it");
                    continue;
                }
                var metLines = LogWatch.MetLines(entry.Key);

                // A free line from the player to the creature's place (the spawn stones stand around here).
                var eye = player.m_eye.position;
                var solid = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
                var found = false;
                var pos = Vector3.zero;
                var away = Vector3.forward;
                foreach (var angle in new[] { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f })
                {
                    var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
                    var p = player.transform.position + dir * 6f;
                    p.y = ZoneSystem.instance.GetGroundHeight(p) + 0.2f;
                    if (Mathf.Abs(p.y - player.transform.position.y) < 2f && !Physics.Linecast(eye, p + Vector3.up * 0.6f, solid)
                        && !Physics.Linecast(eye - dir * 4f + Vector3.up * 0.5f, p + Vector3.up * 0.6f, solid))
                    {
                        found = true;
                        pos = p;
                        away = -dir;
                        break;
                    }
                }
                if (!found)
                {
                    problems.Add($"{prefab}: no free line of sight 6 m around the player to place the creature (test setup)");
                    continue;
                }

                // 1. Near, never under the crosshair (the player looks the other way): plate made, hidden, not met.
                Aim(player, eye + away * 10f);
                yield return null;
                yield return null;
                spawned = TestKit.Spawn(prefab, pos, Quaternion.LookRotation(away, Vector3.up));
                if (spawned == null)
                {
                    problems.Add($"prefab '{prefab}' could not be spawned");
                    continue;
                }
                var c = spawned.GetComponent<Character>();
                data = null;
                for (var i = 0; i < 60 && data == null; i++)
                {
                    spawned.transform.position = pos;
                    Aim(player, player.m_eye.position + away * 10f);
                    yield return null;
                    hud.m_huds.TryGetValue(c, out data);
                }
                for (var i = 0; i < 20; i++)
                {
                    spawned.transform.position = pos;
                    Aim(player, player.m_eye.position + away * 10f);
                    yield return null;
                }
                var shown = data != null && data.m_gui != null && data.m_gui.activeSelf;
                if (data == null)
                {
                    problems.Add($"{prefab}: no name plate object made {Vector3.Distance(pos, player.transform.position):F1} m away");
                }
                if (shown || OwnRecords.GetSeen(player).Contains(entry.Key) || Real(cat).IsKnown(entry) || LogWatch.MetLines(entry.Key) != metLines)
                {
                    problems.Add($"{prefab} near the player but never aimed at: plate shown {shown}, met {OwnRecords.GetSeen(player).Contains(entry.Key)}, "
                                 + $"discovered {Real(cat).IsKnown(entry)}");
                }

                // 2. Aimed at through a wall (a collider on the building-piece layer between camera and creature).
                wall = new GameObject("MC_Compendium_TestWall");
                wall.layer = LayerMask.NameToLayer("piece");
                var collider = wall.AddComponent<BoxCollider>();
                collider.size = new Vector3(8f, 8f, 0.2f);
                var target = c.GetCenterPoint();
                var camPos = GameCamera.instance.transform.position;
                wall.transform.position = Vector3.Lerp(player.m_eye.position, target, 0.55f);
                wall.transform.rotation = Quaternion.LookRotation((target - camPos).normalized, Vector3.up);
                Physics.SyncTransforms();
                var blockedBy = "";
                var hovered = false;
                for (var i = 0; i < 30; i++)
                {
                    spawned.transform.position = pos;
                    Aim(player, c.GetCenterPoint());
                    yield return null;
                    hovered |= player.GetHoverCreature() == c;
                    var hit = CrosshairHit(player);
                    blockedBy = hit != null ? hit.name : "nothing";
                }
                shown = data != null && data.m_gui != null && data.m_gui.activeSelf;
                if (blockedBy != wall.name)
                {
                    problems.Add($"{prefab}: test setup, the crosshair ray did not end on the wall (it hits '{blockedBy}')");
                }
                else if (hovered || shown || OwnRecords.GetSeen(player).Contains(entry.Key) || Real(cat).IsKnown(entry)
                         || LogWatch.MetLines(entry.Key) != metLines)
                {
                    problems.Add($"{prefab} aimed at through a wall: hovered {hovered}, plate shown {shown}, met {OwnRecords.GetSeen(player).Contains(entry.Key)}, "
                                 + $"\"Met\" lines {LogWatch.MetLines(entry.Key) - metLines}");
                }

                // 3. Wall gone, crosshair on it: plate shows, met, discovered, "Killed: 0", paw icon, trophy still "???".
                wall.SetActive(false);
                UnityEngine.Object.Destroy(wall);
                wall = null;
                Physics.SyncTransforms();
                hovered = false;
                for (var i = 0; i < 60 && !(hovered && OwnRecords.GetSeen(player).Contains(entry.Key)); i++)
                {
                    spawned.transform.position = pos;
                    Aim(player, c.GetCenterPoint());
                    yield return null;
                    hovered |= player.GetHoverCreature() == c;
                }
                for (var i = 0; i < 3; i++)
                {
                    spawned.transform.position = pos;
                    yield return null;
                }
                shown = data != null && data.m_gui != null && data.m_gui.activeSelf;
                var k = Real(cat);
                if (!hovered)
                {
                    var hit = CrosshairHit(player);
                    problems.Add($"{prefab}: test setup, the crosshair never got on the creature (ray hits '{(hit != null ? hit.name : "nothing")}')");
                }
                else
                {
                    if (!shown || !OwnRecords.GetSeen(player).Contains(entry.Key) || !k.IsKnown(entry))
                    {
                        problems.Add($"{prefab} aimed at: plate shown {shown}, met {OwnRecords.GetSeen(player).Contains(entry.Key)}, discovered {k.IsKnown(entry)}");
                    }
                    if (LogWatch.MetLines(entry.Key) != metLines + 1)
                    {
                        problems.Add($"{prefab} aimed at: {LogWatch.MetLines(entry.Key) - metLines} Debug \"Met\" line(s), expected 1");
                    }
                    var view = DetailBuilder.Build(entry, k);
                    if (!TestKit.Texts(view).Contains(string.Format(Inv, Labels.KilledFormat, 0)))
                    {
                        problems.Add($"{prefab} met only: details miss \"Killed: 0\"");
                    }
                    var trophy = entry.Creature.Trophy;
                    if (trophy != null)
                    {
                        if (k.IsKnown(trophy) || Presentation.Icon(entry, k, out _) != IconKind.GenericCreature)
                        {
                            problems.Add($"{prefab} met only: trophy discovered {k.IsKnown(trophy)}, icon {Presentation.Icon(entry, k, out _)} (expected the paw print)");
                        }
                        var trophyRow = RowOf(cat, k, trophy);
                        if (trophyRow != null && (trophyRow.Value.Text != Labels.Unknown || trophyRow.Value.Icon != null))
                        {
                            problems.Add($"{prefab} met only: its trophy row shows '{trophyRow.Value.Text}'");
                        }
                        var trophyRef = view.Lines.SelectMany(l => l.Parts).FirstOrDefault(p => p.Ref != null && ReferenceEquals(p.Ref.Entry, trophy));
                        if (trophyRef.Ref != null && (trophyRef.Ref.Known || trophyRef.Ref.Name != Labels.Unknown || trophyRef.Ref.Icon != null))
                        {
                            problems.Add($"{prefab} met only: its drops name the trophy");
                        }
                    }
                    done.Add($"{prefab}: near and not aimed at = \"???\", aimed at through a wall = \"???\", aimed at = met");
                }

                if (data != null)
                {
                    data.m_hoverTimer = 99999f;
                }
                TestKit.Despawn(spawned);
                spawned = null;
                data = null;
                Aim(player, player.m_eye.position + away * 10f);
                yield return null;
                yield return null;
            }
        }
        finally
        {
            if (data != null)
            {
                data.m_hoverTimer = 99999f;
            }
            if (wall != null)
            {
                UnityEngine.Object.Destroy(wall);
            }
            TestKit.Despawn(spawned);
            player.m_lookYaw = yaw0;
            player.m_lookPitch = pitch0;
            state.Restore();
            profile.Restore();
        }
        // The destroy happen at the end of the frame: one more shown plate may be recorded before it.
        yield return null;
        yield return null;
        state.Restore();
        if (problems.Count == 0 && done.Count == 0)
        {
            problems.Add("nothing was checked");
        }
        TestKit.Report(name, problems, Join(done));
    }

    // ---------------------------------------------------------------- compendium.boss (T08)

    private static IEnumerator RunBoss()
    {
        var box = new TestKit.CatalogBox();
        yield return TestKit.WaitCatalog(box);
        var player = Player.m_localPlayer;
        var hud = EnemyHud.instance;
        if (box.Value == null || player == null || hud == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(BossName, box.Error ?? "no local player, EnemyHud or world");
            yield break;
        }
        var cat = box.Value;
        var problems = new List<string>();
        var boss = Creature(cat, "Eikthyr", problems);
        if (boss == null)
        {
            TestKit.Report(BossName, problems, "");
            yield break;
        }
        var state = PlayerState.Capture(player);
        var profile = ProfileState.Capture();
        var yaw0 = player.m_lookYaw;
        var pitch0 = player.m_lookPitch;
        var hadKey = ZoneSystem.instance.GetGlobalKey(GlobalKeys.activeBosses, out float bossesBefore);
        GameObject spawned = null;
        var detail = "";
        try
        {
            Forget(player, boss);
            if (Real(cat).IsKnown(boss))
            {
                problems.Add("setup: Eikthyr still discovered after forgetting it");
            }
            var metLines = LogWatch.MetLines(boss.Key);
            // Further than the plate distance of normal creatures, and the player looks straight up.
            var distance = Mathf.Min(hud.m_maxShowDistanceBoss - 10f, hud.m_maxShowDistance + 8f);
            var pos = TestKit.PointNear(player, distance, 0f);
            player.m_lookPitch = -85f;
            spawned = TestKit.Spawn("Eikthyr", pos, Quaternion.identity);
            if (spawned == null)
            {
                problems.Add("the Eikthyr prefab could not be spawned");
            }
            else
            {
                var c = spawned.GetComponent<Character>();
                var ai = spawned.GetComponent<BaseAI>();
                var hovered = false;
                var alertedByTest = false;
                for (var i = 0; i < 90 && !OwnRecords.GetSeen(player).Contains(boss.Key); i++)
                {
                    spawned.transform.position = pos;
                    player.m_lookPitch = -85f;
                    yield return null;
                    hovered |= player.GetHoverCreature() == c;
                    if (i == 5 && ai != null && !ai.IsAlerted())
                    {
                        ai.SetAlerted(true); // what its senses do once it notices the player
                        alertedByTest = true;
                    }
                }
                var k = Real(cat);
                var real = Vector3.Distance(pos, player.transform.position);
                SelfTest.Note(BossName, $"Eikthyr {real:F0} m away (creature plates up to {hud.m_maxShowDistance} m, boss plates up to "
                                        + $"{hud.m_maxShowDistanceBoss} m), boss {c.IsBoss()}, alerted {ai != null && ai.IsAlerted()} "
                                        + $"({(alertedByTest ? "set by the test" : "by its own senses")}), aimed at {hovered}");
                if (hovered)
                {
                    problems.Add("test setup: the crosshair was on the boss");
                }
                if (!OwnRecords.GetSeen(player).Contains(boss.Key) || !k.IsKnown(boss))
                {
                    problems.Add($"alerted boss {real:F0} m away, never aimed at: met {OwnRecords.GetSeen(player).Contains(boss.Key)}, discovered {k.IsKnown(boss)}");
                }
                if (LogWatch.MetLines(boss.Key) != metLines + 1)
                {
                    problems.Add($"{LogWatch.MetLines(boss.Key) - metLines} Debug \"Met\" line(s) for the boss, expected 1");
                }
                if (k.Kills(boss) != 0 || !TestKit.Texts(DetailBuilder.Build(boss, k)).Contains(string.Format(Inv, Labels.KilledFormat, 0)))
                {
                    problems.Add($"boss met before any kill: kills {k.Kills(boss)}, details miss \"Killed: 0\"");
                }
                detail = $"Eikthyr alerted {real:F0} m away, never under the crosshair: met and discovered with \"Killed: 0\"";
            }
        }
        finally
        {
            TestKit.Despawn(spawned);
            // An alerted boss counts itself in a world key; a destroyed one never uncounts.
            if (ZoneSystem.instance != null)
            {
                if (hadKey)
                {
                    ZoneSystem.instance.SetGlobalKey(GlobalKeys.activeBosses, bossesBefore);
                }
                else
                {
                    ZoneSystem.instance.RemoveGlobalKey(GlobalKeys.activeBosses);
                }
            }
            player.m_lookYaw = yaw0;
            player.m_lookPitch = pitch0;
            state.Restore();
            profile.Restore();
        }
        yield return null;
        yield return null;
        state.Restore();
        profile.Restore();
        TestKit.Report(BossName, problems, detail);
    }

    // ---------------------------------------------------------------- compendium.kills (T14, T08)

    // One creature spawned, killed by a hit of the local player (vanilla path: Character.Damage -> RPC_Damage ->
    // OnDeath -> Game.RPC_RegisterKill -> profile). True in result[0] = it died.
    internal static IEnumerator KillOne(Player player, string prefab, Vector3 pos, Skills.SkillType skill, bool[] result, HashSet<int> litter)
    {
        result[0] = false;
        var go = TestKit.Spawn(prefab, pos, Quaternion.identity);
        if (go == null)
        {
            yield break;
        }
        var c = go.GetComponent<Character>();
        for (var i = 0; i < 5; i++)
        {
            if (go != null)
            {
                go.transform.position = pos;
            }
            yield return null;
        }
        if (go == null || c == null)
        {
            yield break;
        }
        c.Damage(TestKit.KillingHit(player, c, skill));
        var start = Time.realtimeSinceStartup;
        while (go != null && Time.realtimeSinceStartup - start < 10f)
        {
            go.transform.position = pos;
            yield return null;
        }
        result[0] = go == null;
        if (go != null)
        {
            TestKit.Despawn(go);
        }
        yield return null;
        TestKit.ClearLitter(litter, pos, 10f);
    }

    private static IEnumerator RunKills()
    {
        var box = new TestKit.CatalogBox();
        yield return TestKit.WaitCatalog(box);
        var player = Player.m_localPlayer;
        var gui = InventoryGui.instance;
        if (box.Value == null || player == null || gui == null || EnemyHud.instance == null)
        {
            SelfTest.Fail(KillsName, box.Error ?? "no local player, InventoryGui or EnemyHud");
            yield break;
        }
        var cat = box.Value;
        var problems = new List<string>();
        var boar = Creature(cat, "Boar", problems);
        var deer = Creature(cat, "Deer", problems);
        if (problems.Count > 0)
        {
            TestKit.Report(KillsName, problems, "");
            yield break;
        }
        var state = PlayerState.Capture(player);
        var profile = ProfileState.Capture();
        var litter = TestKit.WorldLitter();
        var near = TestKit.PointNear(player, 4f, 3.5f);
        var detail = "";
        // T14 say kills with god on count too: god on for the kills (in memory, same flag the "god" command set), put back after.
        var godBefore = player.InGodMode();
        try
        {
            player.SetGodMode(true);
            Plugin.TestDisplay = new Plugin.DisplayOverride { ShowUndiscovered = true, RevealAll = false };
            Forget(player, boar);
            Forget(player, deer);
            var kills = Game.instance.GetPlayerProfile().m_playerStats[0].m_enemyStats[0];
            var metBoar = LogWatch.MetLines(boar.Key);
            var died = new bool[1];

            // Three Boars, god mode on (the probe's): each kill counts.
            for (var i = 0; i < 3; i++)
            {
                yield return KillOne(player, "Boar", near, Skills.SkillType.Swords, died, litter);
                if (!died[0])
                {
                    problems.Add($"Boar {i + 1} did not die from the test hit within 10 s");
                }
            }
            kills.TryGetValue(boar.Key, out var raw);
            var k = Real(cat);
            if (Names.ToCount(raw) != 3 || k.Kills(boar) != 3 || !k.IsKnown(boar) || !player.InGodMode())
            {
                problems.Add($"after three Boar kills: profile count {raw}, Encyclopedia count {k.Kills(boar)}, discovered {k.IsKnown(boar)}, "
                             + $"god mode still on {player.InGodMode()}");
            }
            // Never under the crosshair: the kills alone discovered it.
            var boarMet = OwnRecords.GetSeen(player).Contains(boar.Key);
            SelfTest.Note(KillsName, $"Boars killed {Vector3.Distance(near, player.transform.position):F1} m away: met {boarMet} "
                                     + $"(\"Met\" lines {LogWatch.MetLines(boar.Key) - metBoar}), discovered by the kills {k.IsKnown(boar)}");

            // In the window: "Killed: 3", one more kill and a reopen: "Killed: 4".
            yield return TestKit.ShowInventory(gui);
            CompendiumWindow.Open();
            yield return null;
            yield return TestKit.WaitPrepared();
            CompendiumWindow.OpenEntry(boar);
            yield return null;
            var line3 = string.Format(Inv, Labels.KilledFormat, 3);
            if (!CompendiumWindow.IsOpen || !ReferenceEquals(CompendiumWindow.SelectedEntry, boar) || !CompendiumWindow.RenderedLines.Contains(line3))
            {
                problems.Add($"window after three kills: open {CompendiumWindow.IsOpen}, Boar selected {ReferenceEquals(CompendiumWindow.SelectedEntry, boar)}, "
                             + $"lines [{Join(CompendiumWindow.RenderedLines.Take(4))}] (expected \"{line3}\")");
            }
            CompendiumWindow.Close(selectButton: false);
            yield return KillOne(player, "Boar", near, Skills.SkillType.Swords, died, litter);
            if (!died[0])
            {
                problems.Add("the fourth Boar did not die from the test hit within 10 s");
            }
            if (!TestKit.InventoryShown(gui))
            {
                yield return TestKit.ShowInventory(gui);
            }
            CompendiumWindow.Open();
            yield return null;
            CompendiumWindow.OpenEntry(boar);
            yield return null;
            var line4 = string.Format(Inv, Labels.KilledFormat, 4);
            if (!CompendiumWindow.RenderedLines.Contains(line4))
            {
                problems.Add($"window reopened after one more kill: lines [{Join(CompendiumWindow.RenderedLines.Take(4))}] (expected \"{line4}\")");
            }

            // Met only: "Killed: 0".
            OwnRecords.MarkSeen(deer.Key);
            CompendiumWindow.SelectTab((int)CatalogTab.Creatures);
            yield return null;
            CompendiumWindow.OpenEntry(deer);
            yield return null;
            var line0 = string.Format(Inv, Labels.KilledFormat, 0);
            if (!ReferenceEquals(CompendiumWindow.SelectedEntry, deer) || !CompendiumWindow.RenderedLines.Contains(line0))
            {
                problems.Add($"met-only Deer: selected {ReferenceEquals(CompendiumWindow.SelectedEntry, deer)}, lines "
                             + $"[{Join(CompendiumWindow.RenderedLines.Take(4))}] (expected \"{line0}\")");
            }
            CompendiumWindow.Close(selectButton: false);
            gui.Hide();
            detail = "three Boar kills in god mode: \"Killed: 3\"; one more and a reopen: \"Killed: 4\"; met-only Deer: \"Killed: 0\"";
        }
        finally
        {
            player.SetGodMode(godBefore);
            Plugin.TestDisplay = null;
            TestKit.CloseAll(gui);
            TestKit.ClearLitter(litter, near, 12f);
            state.Restore();
            profile.Restore();
        }
        yield return null;
        yield return null;
        state.Restore();
        TestKit.ClearLitter(litter, near, 12f);
        TestKit.Report(KillsName, problems, detail);
    }

    // ---------------------------------------------------------------- compendium.farkill (T08)

    // A creature killed from beyond the name-plate distance (as with a bow), never met: the kill alone discovers it.
    private static IEnumerator RunFarKill()
    {
        var box = new TestKit.CatalogBox();
        yield return TestKit.WaitCatalog(box);
        var player = Player.m_localPlayer;
        if (box.Value == null || player == null || EnemyHud.instance == null)
        {
            SelfTest.Fail(FarKillName, box.Error ?? "no local player or EnemyHud");
            yield break;
        }
        var cat = box.Value;
        var problems = new List<string>();
        var neck = Creature(cat, "Neck", problems);
        if (neck == null)
        {
            TestKit.Report(FarKillName, problems, "");
            yield break;
        }
        var state = PlayerState.Capture(player);
        var profile = ProfileState.Capture();
        var litter = TestKit.WorldLitter();
        var far = TestKit.PointNear(player, EnemyHud.instance.m_maxShowDistance + 5f, 0f);
        var distance = Vector3.Distance(far, player.transform.position);
        var detail = "";
        try
        {
            Forget(player, neck);
            if (Real(cat).IsKnown(neck))
            {
                problems.Add("setup: the Neck is still discovered after forgetting it");
            }
            var metLines = LogWatch.MetLines(neck.Key);
            var died = new bool[1];
            yield return KillOne(player, "Neck", far, Skills.SkillType.Bows, died, litter);
            var k = Real(cat);
            var met = OwnRecords.GetSeen(player).Contains(neck.Key);
            if (!died[0] || met || LogWatch.MetLines(neck.Key) != metLines || !k.IsKnown(neck) || k.Kills(neck) != 1)
            {
                problems.Add($"Neck killed {distance:F0} m away (name plates up to {EnemyHud.instance.m_maxShowDistance} m): died {died[0]}, met {met}, "
                             + $"discovered {k.IsKnown(neck)}, kills {k.Kills(neck)} (expected never met, discovered by the one kill)");
            }
            detail = $"a Neck killed {distance:F0} m away (beyond the {EnemyHud.instance.m_maxShowDistance} m of name plates), never met, is discovered by the kill";
        }
        finally
        {
            TestKit.ClearLitter(litter, far, 12f);
            state.Restore();
            profile.Restore();
        }
        yield return null;
        yield return null;
        state.Restore();
        TestKit.ClearLitter(litter, far, 12f);
        TestKit.Report(FarKillName, problems, detail);
    }

    // ---------------------------------------------------------------- compendium.resets (T21)

    // The two methods the console commands call (Terminal: resetknownitems -> Player.ResetCharacterKnownItems,
    // resetcharacter -> Player.ResetCharacter). All in one frame, everything put back in finally.
    private static string CheckResets(Catalog cat, List<string> problems)
    {
        var player = Player.m_localPlayer;
        var boar = Creature(cat, "Boar", problems);
        var greyling = Creature(cat, "Greyling", problems);
        var deer = Creature(cat, "Deer", problems);
        var trophyDeer = Item(cat, "TrophyDeer", problems);
        var bench = Piece(cat, "piece_workbench", problems);
        var rasp = Item(cat, "Raspberry", problems);
        if (player == null || problems.Count > 0)
        {
            return "";
        }
        var state = PlayerState.Capture(player);
        var profile = ProfileState.Capture();
        try
        {
            var stats = Game.instance.GetPlayerProfile().m_playerStats[0];
            foreach (var e in new[] { boar, greyling, deer, trophyDeer, bench, rasp })
            {
                Forget(player, e);
            }
            // The character of the item: a killed Boar, a met-only Greyling, a placed Workbench, a Deer known only by its
            // trophy, a held item, two visited biomes (here and one far away).
            stats.m_enemyStats[0][boar.Key] = 2f;
            OwnRecords.MarkSeen(greyling.Key);
            stats.m_piecesPlacedStats[bench.Key] = 1f;
            player.AddKnownItem(trophyDeer.Item.Drop.m_itemData);
            player.AddKnownItem(rasp.Item.Drop.m_itemData);
            var here = player.GetCurrentBiome();
            var other = here == Heightmap.Biome.Swamp ? Heightmap.Biome.Mountain : Heightmap.Biome.Swamp;
            OwnRecords.RecordBiome(here);
            OwnRecords.RecordBiome(other);
            player.m_knownBiome.Add(Names.Localize(Biomes.Token(other)));
            var k = Real(cat);
            foreach (var e in new[] { boar, greyling, deer, trophyDeer, bench, rasp })
            {
                if (!k.IsKnown(e))
                {
                    problems.Add($"setup: {e} not discovered");
                }
            }
            if (!k.IsBiomeKnown(here) || !k.IsBiomeKnown(other))
            {
                problems.Add($"setup: biomes {here} / {other} not known ({k.KnownBiomes})");
            }

            // resetknownitems.
            player.ResetCharacterKnownItems();
            OwnRecords.ClearCache();
            k = Real(cat);
            if (k.IsKnown(rasp) || k.IsKnown(trophyDeer) || k.DiscoveredItems != 0)
            {
                problems.Add($"resetknownitems: {k.DiscoveredItems} items still discovered (Raspberries {k.IsKnown(rasp)}, Deer Trophy {k.IsKnown(trophyDeer)})");
            }
            if (!k.IsKnown(bench))
            {
                problems.Add("resetknownitems: the placed Workbench is no longer discovered");
            }
            if (k.IsKnown(deer))
            {
                problems.Add("resetknownitems: the Deer (known only by its trophy) is still discovered");
            }
            if (!k.IsKnown(boar) || !k.IsKnown(greyling))
            {
                problems.Add($"resetknownitems: killed Boar discovered {k.IsKnown(boar)}, met Greyling discovered {k.IsKnown(greyling)} (expected both)");
            }
            if (!k.IsBiomeKnown(here) || !k.IsBiomeKnown(other))
            {
                problems.Add($"resetknownitems: biomes changed ({k.KnownBiomes})");
            }

            // resetcharacter.
            player.AddKnownItem(rasp.Item.Drop.m_itemData);
            player.ResetCharacter();
            var keysLeft = player.m_customData.ContainsKey(OwnRecords.SeenKey) || player.m_customData.ContainsKey(OwnRecords.BiomesKey);
            k = Real(cat);
            if (keysLeft)
            {
                problems.Add("resetcharacter: the Encyclopedia's met-creatures or biomes record is still on the character");
            }
            if (k.DiscoveredItems != 0 || k.IsKnown(greyling))
            {
                problems.Add($"resetcharacter: {k.DiscoveredItems} items still discovered, met-only Greyling discovered {k.IsKnown(greyling)}");
            }
            if (k.IsBiomeKnown(other))
            {
                problems.Add($"resetcharacter: the far biome {other} is still known");
            }
            if (!k.IsKnown(boar) || k.Kills(boar) != 2)
            {
                problems.Add($"resetcharacter: Boar discovered {k.IsKnown(boar)} with {k.Kills(boar)} kills (expected its 2 kills kept)");
            }
            if (!k.IsKnown(bench))
            {
                problems.Add("resetcharacter: the placed Workbench is no longer discovered");
            }
            // "After reopening": the window records the biome you stand in at each open.
            OwnRecords.RecordCurrentBiome();
            k = Real(cat);
            if (!k.IsBiomeKnown(here) || k.IsBiomeKnown(other))
            {
                problems.Add($"resetcharacter, then the next open: biomes {k.KnownBiomes} (expected only {here})");
            }
            return $"resetknownitems: items and the trophy-only Deer \"???\", placed Workbench, killed Boar, met Greyling and biomes kept; "
                   + $"resetcharacter: items and the met-only Greyling \"???\", only the biome you stand in ({here}) known at the next open, "
                   + "Boar kept with its kill count, Workbench kept";
        }
        finally
        {
            state.Restore();
            profile.Restore();
            TestKit.ClearUnlockPopups();
            player.UpdateEvents();
        }
    }

    // ---------------------------------------------------------------- compendium.unlocks (T32) and -dlc (T40)

    private sealed class UnlockData
    {
        internal Knowledge Before;
        internal Knowledge NoCost;
        internal Knowledge BackFromNoCost;
        internal Knowledge RecipesKey;
        internal Knowledge BackFromRecipesKey;
        internal Knowledge PiecesKey;
        internal Knowledge BackFromPiecesKey;
        internal HashSet<Entry> Offered = new HashSet<Entry>();
        internal int LockedRecipes;
        internal readonly List<KeyValuePair<string, Entry>> LockedItems = new List<KeyValuePair<string, Entry>>();
        internal string Season = "none";
    }

    // Turn each unlock mode on and off the way the console does (nocost -> Player.SetNoPlacementCost, setkey /
    // removekey -> ZoneSystem.SetGlobalKey / RemoveGlobalKey), a knowledge snapshot at each step. The crafting panel's
    // own list in no-cost mode (Player.GetAvailableRecipes) says what "craftable" means.
    private static UnlockData ReadUnlocks(Catalog cat, Player player, List<string> problems)
    {
        var zs = ZoneSystem.instance;
        var data = new UnlockData();
        var profile = ProfileState.Capture();
        var noCost = player.m_noPlacementCost;
        var hadRecipes = zs.GetGlobalKey(GlobalKeys.AllRecipesUnlocked);
        var hadPieces = zs.GetGlobalKey(GlobalKeys.AllPiecesUnlocked);
        try
        {
            player.m_noPlacementCost = false;
            if (hadRecipes)
            {
                zs.RemoveGlobalKey(GlobalKeys.AllRecipesUnlocked);
            }
            if (hadPieces)
            {
                zs.RemoveGlobalKey(GlobalKeys.AllPiecesUnlocked);
            }
            if (zs.GetGlobalKey(GlobalKeys.AllRecipesUnlocked) || zs.GetGlobalKey(GlobalKeys.AllPiecesUnlocked))
            {
                problems.Add("setup: an unlock world key could not be removed");
                return null;
            }
            data.Before = Knowledge.Take(cat, false);
            data.Season = player.CurrentSeason != null ? player.CurrentSeason.name : "none";

            player.SetNoPlacementCost(true);
            var offered = new List<Recipe>();
            player.GetAvailableRecipes(ref offered);
            foreach (var r in offered)
            {
                var item = r != null && !r.m_noCraftOnlyUpgrade && r.m_item != null ? cat.ItemOf(r.m_item) : null;
                if (item != null)
                {
                    data.Offered.Add(item);
                }
            }
            data.NoCost = Knowledge.Take(cat, false);
            player.SetNoPlacementCost(false);
            data.BackFromNoCost = Knowledge.Take(cat, false);

            zs.SetGlobalKey(GlobalKeys.AllRecipesUnlocked);
            if (!zs.GetGlobalKey(GlobalKeys.AllRecipesUnlocked))
            {
                problems.Add("setup: the world key AllRecipesUnlocked did not apply at once");
            }
            data.RecipesKey = Knowledge.Take(cat, false);
            zs.RemoveGlobalKey(GlobalKeys.AllRecipesUnlocked);
            data.BackFromRecipesKey = Knowledge.Take(cat, false);

            zs.SetGlobalKey(GlobalKeys.AllPiecesUnlocked);
            if (!zs.GetGlobalKey(GlobalKeys.AllPiecesUnlocked))
            {
                problems.Add("setup: the world key AllPiecesUnlocked did not apply at once");
            }
            data.PiecesKey = Knowledge.Take(cat, false);
            zs.RemoveGlobalKey(GlobalKeys.AllPiecesUnlocked);
            data.BackFromPiecesKey = Knowledge.Take(cat, false);

            var season = player.CurrentSeason;
            foreach (var r in ObjectDB.instance.m_recipes)
            {
                if (r == null || r.m_item == null || r.m_noCraftOnlyUpgrade || !(r.m_enabled || (season != null && season.Recipes.Contains(r))))
                {
                    continue;
                }
                var dlc = r.m_item.m_itemData.m_shared.m_dlc;
                if (!string.IsNullOrEmpty(dlc) && DLCMan.instance != null && !DLCMan.instance.IsDLCInstalled(dlc))
                {
                    data.LockedRecipes++;
                    // Catalog leave out items of a missing DLC (CatalogBuilder: "DLC not installed"): entry null then.
                    data.LockedItems.Add(new KeyValuePair<string, Entry>(r.m_item.name, cat.ItemOf(r.m_item)));
                }
            }
            return data;
        }
        finally
        {
            if (player.m_noPlacementCost != noCost)
            {
                player.m_noPlacementCost = noCost;
            }
            if (zs.GetGlobalKey(GlobalKeys.AllRecipesUnlocked) != hadRecipes)
            {
                if (hadRecipes)
                {
                    zs.SetGlobalKey(GlobalKeys.AllRecipesUnlocked);
                }
                else
                {
                    zs.RemoveGlobalKey(GlobalKeys.AllRecipesUnlocked);
                }
            }
            if (zs.GetGlobalKey(GlobalKeys.AllPiecesUnlocked) != hadPieces)
            {
                if (hadPieces)
                {
                    zs.SetGlobalKey(GlobalKeys.AllPiecesUnlocked);
                }
                else
                {
                    zs.RemoveGlobalKey(GlobalKeys.AllPiecesUnlocked);
                }
            }
            profile.Restore();
        }
    }

    private static int Differences(Catalog cat, Knowledge a, Knowledge b, Func<Entry, bool> which, out Entry first)
    {
        var n = 0;
        first = null;
        foreach (var e in cat.Entries)
        {
            if (which(e) && a.IsKnown(e) != b.IsKnown(e))
            {
                n++;
                first ??= e;
            }
        }
        return n;
    }

    private static string CheckUnlocks(Catalog cat, List<string> problems)
    {
        var player = Player.m_localPlayer;
        if (player == null || ZoneSystem.instance == null)
        {
            problems.Add("no local player or world");
            return "";
        }
        var d = ReadUnlocks(cat, player, problems);
        if (d == null)
        {
            return "";
        }
        var season = player.CurrentSeason;
        bool OutOfSeason(Entry e) => e.Kind == EntryKind.Item && e.Item.Recipes.Count > 0
                                     && e.Item.Recipes.All(r => r == null || (!r.m_enabled && (season == null || !season.Recipes.Contains(r))));
        var pieces = cat.Entries.Count(e => e.Kind == EntryKind.Piece);
        var seasonalOut = cat.Entries.Where(e => OutOfSeason(e) && !d.Before.IsKnown(e)).ToList();

        void Mode(string mode, Knowledge on, Knowledge off, bool items, bool allPieces)
        {
            if (items)
            {
                var missing = d.Offered.Where(e => !on.IsKnown(e)).Take(4).ToList();
                if (d.Offered.Count < 50 || missing.Count > 0)
                {
                    problems.Add($"{mode}: {d.Offered.Count} items have a recipe the crafting panel offers; not discovered: "
                                 + (missing.Count > 0 ? string.Join(", ", missing.Select(e => e.PrefabName)) : "none (but too few offered)"));
                }
                var leaked = seasonalOut.Where(on.IsKnown).Take(4).ToList();
                if (leaked.Count > 0)
                {
                    problems.Add($"{mode}: seasonal items out of season discovered: {string.Join(", ", leaked.Select(e => e.PrefabName))}");
                }
            }
            else if (Differences(cat, on, d.Before, e => e.Kind == EntryKind.Item, out var changedItem) > 0)
            {
                problems.Add($"{mode}: items changed (first {changedItem}) although this mode is about pieces only");
            }
            if (allPieces)
            {
                if (on.DiscoveredPieces != pieces)
                {
                    problems.Add($"{mode}: {on.DiscoveredPieces} of {pieces} building pieces discovered (expected all)");
                }
            }
            else if (Differences(cat, on, d.Before, e => e.Kind == EntryKind.Piece, out var changedPiece) > 0)
            {
                problems.Add($"{mode}: pieces changed (first {changedPiece}) although this mode is about recipes only");
            }
            // A creature can only follow through a trophy that has a recipe (none in the vanilla data): said, not judged.
            var creaturesChanged = Differences(cat, on, d.Before, e => e.Kind == EntryKind.Creature, out var changedCreature);
            if (creaturesChanged > 0)
            {
                SelfTest.Note(UnlocksName, $"{mode}: {creaturesChanged} creature(s) discovered through a craftable trophy (first {changedCreature})");
            }
            var left = Differences(cat, off, d.Before, _ => true, out var firstLeft);
            if (left > 0)
            {
                problems.Add($"{mode} turned off again: {left} entries not as before (first {firstLeft})");
            }
        }

        Mode("no-cost mode", d.NoCost, d.BackFromNoCost, items: true, allPieces: true);
        Mode("world key AllRecipesUnlocked", d.RecipesKey, d.BackFromRecipesKey, items: true, allPieces: false);
        Mode("world key AllPiecesUnlocked", d.PiecesKey, d.BackFromPiecesKey, items: false, allPieces: true);
        SelfTest.Note(UnlocksName, $"before: {d.Before.DiscoveredItems} items / {d.Before.DiscoveredPieces} pieces; no-cost: {d.NoCost.DiscoveredItems} / "
                                   + $"{d.NoCost.DiscoveredPieces}; AllRecipesUnlocked: {d.RecipesKey.DiscoveredItems} / {d.RecipesKey.DiscoveredPieces}; "
                                   + $"AllPiecesUnlocked: {d.PiecesKey.DiscoveredItems} / {d.PiecesKey.DiscoveredPieces}; {d.Offered.Count} items offered by the "
                                   + $"crafting panel in no-cost mode; {seasonalOut.Count} seasonal items out of season (season: {d.Season})");
        return $"no-cost mode discovers the {d.Offered.Count} items the crafting panel offers and all {pieces} pieces; AllRecipesUnlocked the items "
               + $"only, AllPiecesUnlocked the pieces only; {seasonalOut.Count} seasonal items out of season stay \"???\"; each mode off = as before";
    }

    private static string CheckUnlocksDlc(Catalog cat, List<string> problems)
    {
        var player = Player.m_localPlayer;
        if (player == null || ZoneSystem.instance == null)
        {
            problems.Add("no local player or world");
            return "";
        }
        var d = ReadUnlocks(cat, player, problems);
        if (d == null)
        {
            return "";
        }
        foreach (var (mode, on) in new[] { ("no-cost mode", d.NoCost), ("world key AllRecipesUnlocked", d.RecipesKey) })
        {
            var extra = cat.Entries.Where(e => e.Kind == EntryKind.Item && on.IsKnown(e) && !d.Before.IsKnown(e) && !d.Offered.Contains(e)).ToList();
            if (extra.Count > 0)
            {
                problems.Add($"{mode} discovers {extra.Count} item(s) the crafting panel does not offer even in no-cost mode: "
                             + string.Join(", ", extra.Take(6).Select(e => e.PrefabName + " (dlc '" + (e.Item.Drop != null ? e.Item.Drop.m_itemData.m_shared.m_dlc : "") + "')")));
            }
        }
        // The comparison must have something to compare: the mode discovers items, and the panel offers items.
        var newInNoCost = cat.Entries.Count(e => e.Kind == EntryKind.Item && d.NoCost.IsKnown(e) && !d.Before.IsKnown(e));
        if (d.Offered.Count < 50 || newInNoCost < 50)
        {
            problems.Add($"nothing to compare: the crafting panel offers {d.Offered.Count} items in no-cost mode, the mode discovers {newInNoCost} new ones");
        }
        // Items of a DLC that is not installed: no entry at all (never a row), or an entry no unlock mode discovers.
        var listed = d.LockedItems.Where(kv => kv.Value != null).ToList();
        foreach (var kv in listed)
        {
            foreach (var (mode, on) in new[] { ("no-cost mode", d.NoCost), ("world key AllRecipesUnlocked", d.RecipesKey) })
            {
                if (on.IsKnown(kv.Value) && !d.Before.IsKnown(kv.Value) && !d.Offered.Contains(kv.Value))
                {
                    problems.Add($"{mode} discovers {kv.Key}, an item of a DLC that is not installed");
                }
            }
        }
        SelfTest.Note(UnlocksDlcName, $"{d.LockedRecipes} enabled recipe(s) of this game's data make an item of a DLC that is not installed "
                                      + $"(Player.GetAvailableRecipes never offers those): {string.Join(", ", d.LockedItems.Select(kv => kv.Key + (kv.Value != null ? " (listed)" : " (no entry)")))}");
        return $"the {newInNoCost} items no-cost mode discovers and the ones AllRecipesUnlocked discovers are all offered by the crafting panel "
               + $"in no-cost mode ({d.Offered.Count} offered); {d.LockedRecipes} recipe(s) make an item of a DLC that is not installed: "
               + $"{d.LockedRecipes - listed.Count} of them have no entry at all (such items are never listed), {listed.Count} have one that stays \"???\"";
    }

    // ---------------------------------------------------------------- compendium.language (T19)

    // A language switch, played small: one item name and one biome name changed in the game's translation table (in
    // memory, put back after), then what Localization.OnLanguageChange runs of ours (the hook itself is checked). A real
    // switch (Localization.SetLanguage) would write the game's language setting and drop words other mods added.
    private static string CheckLanguage(Catalog cat, List<string> problems)
    {
        var player = Player.m_localPlayer;
        var loc = Localization.instance;
        var addWord = AccessTools.Method(typeof(Localization), "AddWord");
        var cacheField = AccessTools.Field(typeof(Localization), "m_cache");
        var wordsField = AccessTools.Field(typeof(Localization), "m_translations");
        var words = wordsField != null ? wordsField.GetValue(loc) as Dictionary<string, string> : null;
        var cache = cacheField != null ? cacheField.GetValue(loc) : null;
        var evict = cache != null ? cache.GetType().GetMethod("EvictAll") : null;
        if (player == null || addWord == null || words == null || evict == null)
        {
            problems.Add("the game's translation table was not found (Localization.AddWord / m_translations / m_cache): game changed?");
            return "";
        }
        var hooked = Localization.OnLanguageChange != null
                     && Localization.OnLanguageChange.GetInvocationList().Any(h => h.Method.DeclaringType == typeof(Names) && h.Method.Name == nameof(Names.OnLanguageChanged));
        if (!hooked)
        {
            problems.Add("the Encyclopedia is not subscribed to Localization.OnLanguageChange");
        }
        var entry = Item(cat, "Raspberry", problems);
        if (entry == null || entry.SubGroup == null)
        {
            problems.Add("the Raspberries entry or its group is missing");
            return "";
        }
        var biome = Heightmap.Biome.Swamp;
        var itemKey = entry.NameToken.TrimStart('$');
        var biomeKey = Biomes.Token(biome).TrimStart('$');
        var hadItem = words.TryGetValue(itemKey, out var oldItem);
        var hadBiome = words.TryGetValue(biomeKey, out var oldBiome);
        var state = PlayerState.Capture(player);
        const string newItem = "Zzzz Test Berry";
        const string newBiome = "Zzzz Test Marsh";
        try
        {
            // Before: the group's entries all discovered, where the item sorts.
            Knowledge Group() => Knowledge.Synthetic(cat, e => ReferenceEquals(e.SubGroup, entry.SubGroup), Heightmap.Biome.None);
            List<Entry> GroupRows() => ListBuilder.BuildTab(cat, Group(), entry.Tab, showUndiscovered: false)
                .Where(r => r.Kind == ListRowKind.Entry).Select(r => r.Entry).ToList();
            var nameBefore = entry.DisplayName;
            var rowsBefore = GroupRows();
            var stamp = Names.Stamp;
            if (rowsBefore.Count < 3 || ReferenceEquals(rowsBefore[rowsBefore.Count - 1], entry))
            {
                problems.Add($"setup: the item's group has {rowsBefore.Count} entries or the item already sorts last");
            }
            // A biome visited in the old language: vanilla keeps its translated name, the mod keeps its own record.
            var oldBiomeName = Names.Localize(Biomes.Token(biome));
            player.m_customData.TryGetValue(OwnRecords.BiomesKey, out var biomesRaw);
            player.m_knownBiome.Add(oldBiomeName);
            OwnRecords.RecordBiome(biome);
            if (!Real(cat).IsBiomeKnown(biome))
            {
                problems.Add($"setup: {biome} not known after visiting it");
            }

            addWord.Invoke(loc, new object[] { itemKey, newItem });
            addWord.Invoke(loc, new object[] { biomeKey, newBiome });
            evict.Invoke(cache, null);
            Names.OnLanguageChanged();

            if (Names.Stamp != stamp + 1 || entry.DisplayName != newItem || entry.SortKey != newItem)
            {
                problems.Add($"after the language change the item is named '{entry.DisplayName}' (expected '{newItem}', was '{nameBefore}')");
            }
            var rowsAfter = GroupRows();
            if (rowsAfter.Count != rowsBefore.Count || !ReferenceEquals(rowsAfter[rowsAfter.Count - 1], entry))
            {
                problems.Add($"after the language change the item sorts at {rowsAfter.IndexOf(entry) + 1} of {rowsAfter.Count} (expected last, by its new name)");
            }
            var row = ListBuilder.BuildTab(cat, Group(), entry.Tab, showUndiscovered: false).FirstOrDefault(r => ReferenceEquals(r.Entry, entry));
            if (row.Text != newItem)
            {
                problems.Add($"the list row reads '{row.Text}' after the language change");
            }
            var k = Real(cat);
            var header = Presentation.BiomeName(biome, k);
            if (!k.IsBiomeKnown(biome) || header != newBiome)
            {
                problems.Add($"the biome visited before the language change: known {k.IsBiomeKnown(biome)}, shown as '{header}' (expected '{newBiome}')");
            }
            // Without the mod's own record the old translated name alone would have lost it.
            if (biomesRaw == null)
            {
                player.m_customData.Remove(OwnRecords.BiomesKey);
            }
            else
            {
                player.m_customData[OwnRecords.BiomesKey] = biomesRaw;
            }
            OwnRecords.ClearCache();
            var vanillaOnly = Real(cat).IsBiomeKnown(biome);
            SelfTest.Note(LanguageName, $"item '{nameBefore}' -> '{entry.DisplayName}', row {rowsBefore.IndexOf(entry) + 1} -> {rowsAfter.IndexOf(entry) + 1} of "
                                        + $"{rowsAfter.Count}; biome '{oldBiomeName}' -> '{header}', known by the game's own record alone after the change: {vanillaOnly}");
            return $"name and sort order follow the new translation ('{nameBefore}' -> '{newItem}', now last of {rowsAfter.Count}); a biome "
                   + "visited before the change stays known under its new name";
        }
        finally
        {
            if (hadItem)
            {
                addWord.Invoke(loc, new object[] { itemKey, oldItem });
            }
            else
            {
                words.Remove(itemKey);
            }
            if (hadBiome)
            {
                addWord.Invoke(loc, new object[] { biomeKey, oldBiome });
            }
            else
            {
                words.Remove(biomeKey);
            }
            evict.Invoke(cache, null);
            Names.OnLanguageChanged();
            state.Restore();
        }
    }
#endif
}
