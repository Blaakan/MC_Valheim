using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MC.Shared;
using UnityEngine;
#endif

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Debug build only (calls vanish in Release): in-world self tests run by the world probe (tools/Test-InWorld.ps1),
// fresh character "MCProbe". Three tests, each end with one PASS or FAIL line (NOTE lines give numbers):
//   compendium.catalog   = catalog build finish; every tab has many entries; no duplicate id; every entry named and
//                          grouped; vanilla anchors (Wood, Workbench, Boar) present; hidden-until-known count; NOTE
//                          of every upgrader station prefab (m_upgrader) with its m_hasCraftTab (design 3.5.1).
//   compendium.details   = DetailBuilder on EVERY entry with 3 knowledges (probe's real one, reveal all, synthetic
//                          half): nothing throw, no block fail; undiscovered refs are "???" with no icon; undiscovered
//                          entry details show nothing; no label text carry an undiscovered name; list rows and search
//                          never show an undiscovered name or sprite, unknown rows after known ones.
//   compendium.knowledge = fresh character know little; mark things known the vanilla way (AddKnownItem, profile
//                          kill and placed counts, station seen) and with own records (met, biome): each flip to
//                          discovered; unlock modes (no-cost) follow the Craft tab; met check through the real
//                          EnemyHud: one creature spawned 5 m away, plate made but hidden = not met, plate shown = met.
// Tests leave the world as before: they put back what they change (sets, stats, custom data, no-cost flag), and the one
// creature the met check spawns is destroyed and its Seen record put back.
internal static class SelfTests
{
#if DEBUG
    private const string CatalogName = "compendium.catalog";
    private const string DetailsName = "compendium.details";
    private const string KnowledgeName = "compendium.knowledge";
    private const float CatalogTimeout = 60f;
    private const float FrameBudgetMs = 8f;
#endif

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(CatalogName, RunCatalog);
        SelfTest.Register(DetailsName, RunDetails);
        SelfTest.Register(KnowledgeName, RunKnowledge);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(CatalogName);
        SelfTest.Unregister(DetailsName);
        SelfTest.Unregister(KnowledgeName);
#endif
    }

#if DEBUG
    // Box so a waiting loop inside each test can hand the catalog out (no nested coroutine: probe may drive MoveNext).
    private sealed class CatalogBox
    {
        internal Catalog Value;
        internal float Started = -1f;
        internal string Error;
    }

    // One wait step. True = done (Value set, or Error set).
    private static bool WaitStep(CatalogBox box)
    {
        if (box.Started < 0f)
        {
            box.Started = Time.realtimeSinceStartup;
        }
        try
        {
            box.Value = CatalogService.EnsureReady();
        }
        catch (Exception e)
        {
            box.Error = $"EnsureReady threw {e.GetType().Name}: {e.Message}";
            return true;
        }
        if (box.Value != null)
        {
            return true;
        }
        if (Time.realtimeSinceStartup - box.Started > CatalogTimeout)
        {
            box.Error = $"catalog not ready after {CatalogTimeout} s (building: {CatalogService.IsBuilding})";
            return true;
        }
        return false;
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

    // ---------------------------------------------------------------- compendium.catalog

    private static IEnumerator RunCatalog()
    {
        var box = new CatalogBox();
        while (!WaitStep(box))
        {
            yield return null;
        }
        if (box.Value == null)
        {
            SelfTest.Fail(CatalogName, box.Error);
            yield break;
        }
        var problems = new List<string>();
        try
        {
            CheckCatalog(box.Value, problems);
        }
        catch (Exception e)
        {
            problems.Add($"check threw {e}");
        }
        var cat = box.Value;
        Report(CatalogName, problems,
            $"{cat.ItemCount} items, {cat.PieceCount} pieces, {cat.CreatureCount} creatures, {cat.HiddenCount} hidden until "
            + $"known, {cat.NoSourceCount} items with no source, built in {cat.BuildMs} ms over {cat.BuildFrames} frames");
    }

    private static void CheckCatalog(Catalog cat, List<string> problems)
    {
        // Minimums far below vanilla 1.0.16 numbers: a tab under them = a rule or stage broke.
        var minimums = new Dictionary<CatalogTab, int>
        {
            { CatalogTab.Weapons, 40 }, { CatalogTab.Armor, 40 }, { CatalogTab.Tools, 8 }, { CatalogTab.Food, 30 },
            { CatalogTab.Materials, 80 }, { CatalogTab.Trophies, 40 }, { CatalogTab.Building, 100 },
            { CatalogTab.Creatures, 40 },
        };
        var counts = new List<string>();
        foreach (var kv in minimums)
        {
            var n = cat.CountOf(kv.Key);
            var groups = cat.SubGroups(kv.Key).Count;
            counts.Add($"{kv.Key} {n} ({groups} groups)");
            if (n < kv.Value)
            {
                problems.Add($"tab {kv.Key} has {n} entries (expected at least {kv.Value})");
            }
        }
        SelfTest.Note(CatalogName, "per tab: " + string.Join(", ", counts));

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var inGroups = 0;
        for (var i = 0; i < cat.Entries.Count; i++)
        {
            var e = cat.Entries[i];
            if (e.Index != i)
            {
                problems.Add($"{e} has index {e.Index} at position {i}");
            }
            if (!ids.Add(e.Kind + "|" + e.Key))
            {
                problems.Add($"duplicate id {e.Kind} {e.Key}");
            }
            if (string.IsNullOrEmpty(e.NameToken) || string.IsNullOrEmpty(e.DisplayName))
            {
                problems.Add($"{e} has no name token or name");
            }
            if (e.Prefab == null || e.SubGroup == null || !e.SubGroup.Entries.Contains(e) || e.SubGroup.Tab != e.Tab)
            {
                problems.Add($"{e} has no prefab or is not in its sub-group");
            }
            if (e.Kind != EntryKind.Creature && e.Icon == null)
            {
                problems.Add($"{e} has no icon");
            }
        }
        for (var t = 0; t < Tabs.Count; t++)
        {
            foreach (var g in cat.SubGroups((CatalogTab)t))
            {
                inGroups += g.Entries.Count;
            }
        }
        if (inGroups != cat.Entries.Count)
        {
            problems.Add($"{inGroups} entries in sub-groups but {cat.Entries.Count} entries");
        }

        // Vanilla anchors (tokens checked in the 1.0.16 localization table, design "Unverified names" list).
        foreach (var token in new[] { "$item_wood", "$item_sword_bronze", "$item_trophy_boar" })
        {
            var e = cat.FindItem(token);
            if (e == null || e.HiddenUntilKnown)
            {
                problems.Add($"item {token} missing or hidden");
            }
        }
        foreach (var token in new[] { "$piece_workbench", "$piece_forge" })
        {
            var e = cat.FindPiece(token);
            if (e == null || e.Piece.StationName == null)
            {
                problems.Add($"station piece {token} missing or not a station");
            }
        }
        var boar = cat.FindCreature("$enemy_boar");
        if (boar == null)
        {
            problems.Add("creature $enemy_boar missing");
        }
        else if (boar.Creature.Trophy == null || boar.Creature.Trophy.Key != "$item_trophy_boar")
        {
            problems.Add($"Boar trophy is {(boar.Creature.Trophy != null ? boar.Creature.Trophy.Key : "none")}");
        }
        var forge = cat.FindPiece("$piece_forge");
        if (forge != null && (forge.Piece.RecipesHere.Count == 0 || forge.Piece.Extensions.Count == 0))
        {
            problems.Add($"Forge has {forge.Piece.RecipesHere.Count} recipes and {forge.Piece.Extensions.Count} upgrades");
        }
        if (cat.CreaturesByName.Values.Count(c => c.Creature.Habitat != Heightmap.Biome.None) < cat.CreatureCount / 2)
        {
            problems.Add("less than half of the creatures have a habitat");
        }
        // Item born by Procreation (hen: egg) = a source of that item ("Laid by a tamed ...").
        foreach (var c in cat.CreaturesByName.Values)
        {
            foreach (var go in c.Creature.Prefabs)
            {
                if (go == null || !go.TryGetComponent<Procreation>(out var proc))
                {
                    continue;
                }
                foreach (var child in new[] { proc.m_offspring, proc.m_noPartnerOffspring })
                {
                    var item = child != null && !child.TryGetComponent<Character>(out _) ? cat.ItemOf(child) : null;
                    if (item != null && (!item.Item.LaidBy.Contains(c) || !item.Item.HasSource))
                    {
                        problems.Add($"{item} is born from {c} but has no \"laid by\" source");
                    }
                }
            }
        }

        // Numbers and unverified prefab data: notes, not failures.
        var hiddenRules = cat.Entries.Where(e => e.HiddenUntilKnown).GroupBy(e => e.HiddenRule ?? "?")
            .Select(g => $"{g.Key} {g.Count()}");
        SelfTest.Note(CatalogName, $"hidden until known: {cat.HiddenCount} ({string.Join(", ", hiddenRules)})");
        SelfTest.Note(CatalogName, "unverified prefab data: "
                                   + $"PlayerUnarmed {Describe(cat.FindItemByPrefab("PlayerUnarmed"))}; "
                                   + $"CapeTest {Describe(cat.FindItemByPrefab("CapeTest"))}; "
                                   + $"SwordCheat {Describe(cat.FindItemByPrefab("SwordCheat"))}; "
                                   + $"Abomination_attack1 {Describe(cat.FindItemByPrefab("Abomination_attack1"))}; "
                                   + $"bjorn_bite {Describe(cat.FindItemByPrefab("bjorn_bite"))}; "
                                   + $"BeltStrength {Describe(cat.FindItemByPrefab("BeltStrength"))}; "
                                   + $"FishingRod {Describe(cat.FindItemByPrefab("FishingRod"))}; "
                                   + $"DvergerTest {(cat.CreaturesByPrefab.ContainsKey("DvergerTest") ? "LISTED" : "not listed")}; "
                                   + $"Boar_piggy {(cat.CreaturesByPrefab.TryGetValue("Boar_piggy", out var piggy) ? piggy.Key + " habitat " + piggy.Creature.Habitat : "not listed")}; "
                                   + $"Hen {(cat.CreaturesByPrefab.TryGetValue("Hen", out var hen) ? hen.Key + " habitat " + hen.Creature.Habitat : "not listed")}; "
                                   + $"Hen offspring {Offspring(cat, "Hen")}; "
                                   + $"traders found {cat.TraderCount}; "
                                   + $"upgrade-only recipes {cat.UpgradeOnlyRecipes}");
        var noSource = cat.Entries.Where(e => e.Kind == EntryKind.Item && !e.HiddenUntilKnown && !e.Item.HasSource)
            .Select(e => e.PrefabName).ToList();
        SelfTest.Note(CatalogName, $"listed items with no source found ({noSource.Count}): {string.Join(", ", noSource)}");
        SelfTest.Note(CatalogName, UpgraderStations(cat));
    }

    // Upgrader stations (CraftingStation.m_upgrader): only a Craft tab there (m_hasCraftTab) would let quality 1 be
    // crafted for the upgrade-station items alone (Player.HaveRequirementItems keep only m_upgraderResource at an
    // upgrader); the details never show that cost (design 3.5.1). Prefab data: noted, not a failure.
    private static string UpgraderStations(Catalog cat)
    {
        var stations = new List<string>();
        var seen = new HashSet<CraftingStation>();
        var scene = ZNetScene.instance;
        foreach (var list in new[] { scene != null ? scene.m_prefabs : null, scene != null ? scene.m_nonNetViewPrefabs : null })
        {
            if (list == null)
            {
                continue;
            }
            foreach (var go in list)
            {
                if (go == null)
                {
                    continue;
                }
                foreach (var cs in go.GetComponentsInChildren<CraftingStation>(true))
                {
                    if (cs != null && cs.m_upgrader && seen.Add(cs))
                    {
                        stations.Add($"{go.name} ({cs.m_name}, '{Names.Localize(cs.m_name)}'): m_hasCraftTab {cs.m_hasCraftTab}");
                    }
                }
            }
        }
        var withUpgrader = 0;
        var chargedAtQ1 = 0;
        var recipes = cat.Entries.Where(e => e.Kind == EntryKind.Item).SelectMany(e => e.Item.Recipes).Distinct();
        foreach (var r in recipes)
        {
            var reqs = r != null && r.m_resources != null ? r.m_resources.Where(q => q != null && q.m_resItem != null && q.m_upgraderResource).ToList() : null;
            if (reqs == null || reqs.Count == 0)
            {
                continue;
            }
            withUpgrader++;
            if (reqs.Any(q => q.GetAmount(1) > 0))
            {
                chargedAtQ1++;
            }
        }
        return $"upgrader stations ({stations.Count}): {(stations.Count > 0 ? string.Join("; ", stations) : "none")}; recipes with an "
               + $"upgrade-station requirement {withUpgrader}, {chargedAtQ1} of them with a quality-1 amount";
    }

    // What a creature's Procreation give birth to, and how the catalog lists it (item offspring = a source).
    private static string Offspring(Catalog cat, string creaturePrefab)
    {
        if (!cat.CreaturesByPrefab.TryGetValue(creaturePrefab, out var c))
        {
            return "creature not listed";
        }
        var parts = new List<string>();
        foreach (var go in c.Creature.Prefabs)
        {
            if (go == null || !go.TryGetComponent<Procreation>(out var proc))
            {
                continue;
            }
            foreach (var child in new[] { proc.m_offspring, proc.m_noPartnerOffspring })
            {
                if (child == null)
                {
                    continue;
                }
                var item = cat.FindItemByPrefab(child.name);
                parts.Add(child.TryGetComponent<Character>(out _)
                    ? $"{child.name} (creature)"
                    : $"{child.name} (item, {Describe(item)}{(item != null && item.Item.LaidBy.Contains(c) ? ", laid by it" : "")})");
            }
        }
        return parts.Count > 0 ? string.Join(" / ", parts.Distinct()) : "none";
    }

    private static string Describe(Entry e)
    {
        if (e == null)
        {
            return "not listed";
        }
        return e.HiddenUntilKnown ? $"hidden ({e.HiddenRule})" : e.Item != null && !e.Item.HasSource ? "listed, no source" : "listed";
    }

    // ---------------------------------------------------------------- compendium.details

    private static IEnumerator RunDetails()
    {
        var box = new CatalogBox();
        while (!WaitStep(box))
        {
            yield return null;
        }
        if (box.Value == null)
        {
            SelfTest.Fail(DetailsName, box.Error);
            yield break;
        }
        var cat = box.Value;
        var problems = new List<string>();
        var knowledges = new List<KeyValuePair<string, Knowledge>>();
        try
        {
            knowledges.Add(new KeyValuePair<string, Knowledge>("real", Knowledge.Take(cat, false)));
            knowledges.Add(new KeyValuePair<string, Knowledge>("reveal all", Knowledge.Take(cat, true)));
            knowledges.Add(new KeyValuePair<string, Knowledge>("half", Knowledge.Synthetic(cat, e => e.Index % 2 == 0,
                Heightmap.Biome.Meadows | Heightmap.Biome.Swamp | Heightmap.Biome.Mountain | Heightmap.Biome.Plains
                | Heightmap.Biome.AshLands)));
        }
        catch (Exception e)
        {
            SelfTest.Fail(DetailsName, $"knowledge threw {e}");
            yield break;
        }

        var watch = Stopwatch.StartNew();
        var budget = (long)(Stopwatch.Frequency * (FrameBudgetMs / 1000.0));
        var lines = 0;
        var upgradeStationItems = 0;
        // Reading details must not change the character (GetTooltip would add level-0 skills, DetailBuilder undo it).
        var skillsBefore = SkillSnapshot(Player.m_localPlayer);
        var removedBefore = DetailBuilder.SkillEntriesRemoved;
        foreach (var kv in knowledges)
        {
            var k = kv.Value;
            var labels = new HashSet<string>(StringComparer.Ordinal);
            var thrown = 0;
            var failedBlocks = 0;
            foreach (var e in cat.Entries)
            {
                DetailView view = null;
                try
                {
                    view = DetailBuilder.Build(e, k);
                    lines += view.Lines.Count;
                    CheckView(kv.Key, e, view, k, labels, problems);
                    if (k.RevealAll && CheckCraftBlock(e, view, problems))
                    {
                        upgradeStationItems++;
                    }
                }
                catch (Exception ex)
                {
                    thrown++;
                    if (thrown <= 5)
                    {
                        problems.Add($"[{kv.Key}] {e} threw {ex.GetType().Name}: {ex.Message}");
                    }
                }
                if (view != null && (view.Failed || view.FailedBlocks > 0))
                {
                    failedBlocks++;
                    if (failedBlocks <= 5)
                    {
                        problems.Add($"[{kv.Key}] {e} failed: {string.Join("; ", view.Errors)}");
                    }
                }
                if (watch.ElapsedTicks > budget)
                {
                    yield return null;
                    watch.Restart();
                }
            }
            if (failedBlocks > 5 || thrown > 5)
            {
                problems.Add($"[{kv.Key}] {thrown} entries threw, {failedBlocks} had failed blocks in total");
            }

            // Label text (not refs, not the entry's own game text) never carry an undiscovered name.
            var unknownNames = cat.Entries.Where(e => !k.IsKnown(e)).Select(e => e.SortKey)
                .Where(n => n != null && n.Length >= 4).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var leaks = 0;
            foreach (var label in labels)
            {
                foreach (var name in unknownNames)
                {
                    if (ContainsWord(label, name))
                    {
                        leaks++;
                        if (leaks <= 5)
                        {
                            problems.Add($"[{kv.Key}] label \"{label}\" contains undiscovered name \"{name}\"");
                        }
                    }
                }
                if (watch.ElapsedTicks > budget)
                {
                    yield return null;
                    watch.Restart();
                }
            }

            var hiddenHeaders = 0;
            try
            {
                hiddenHeaders = CheckRows(kv.Key, cat, k, problems);
            }
            catch (Exception ex)
            {
                problems.Add($"[{kv.Key}] list check threw {ex}");
            }
            SelfTest.Note(DetailsName, $"[{kv.Key}] {k.Summary()} {labels.Count} distinct label texts and {hiddenHeaders} list headers of an undiscovered tool or biome checked.");
            yield return null;
            watch.Restart();
        }
        SelfTest.Note(DetailsName, $"[reveal all] {upgradeStationItems} items list upgrade-station costs (upgrades only, never "
                                   + "in the quality-1 crafting cost)");
        var skillsAfter = SkillSnapshot(Player.m_localPlayer);
        SelfTest.Note(DetailsName, $"character skills before and after building every detail: [{skillsBefore}] / [{skillsAfter}]; "
                                   + $"level-0 skill entries the item tooltips added and the details builder removed: "
                                   + $"{DetailBuilder.SkillEntriesRemoved - removedBefore}");
        if (skillsAfter != skillsBefore)
        {
            problems.Add($"building the details changed the character's skills: [{skillsBefore}] -> [{skillsAfter}]");
        }
        Report(DetailsName, problems, $"{cat.Entries.Count} entries x {knowledges.Count} knowledges, {lines} lines, no leak, "
                                      + "skills unchanged");
    }

    // Skill list of a character as text (type, level, accumulator), sorted: equal text = same skills.
    private static string SkillSnapshot(Player player)
    {
        var skills = player != null ? player.GetSkills() : null;
        if (skills == null || skills.m_skillData == null)
        {
            return "no skills object";
        }
        return string.Join(", ", skills.m_skillData.OrderBy(kv => (int)kv.Key)
            .Select(kv => $"{kv.Key} {(kv.Value != null ? kv.Value.m_level : -1f):0.##}/{(kv.Value != null ? kv.Value.m_accumulator : -1f):0.##}"));
    }

    // Crafting block as the vanilla Craft tab charge it: an "At an upgrade station:" row only inside an upgrade row
    // ("Quality N ..."), never under a recipe's head ("At <station> (level N)" / "By hand" = quality 1); an upgrade-only
    // recipe (m_noCraftOnlyUpgrade) never has a head. True = the item has upgrade-station rows.
    private static bool CheckCraftBlock(Entry e, DetailView view, List<string> problems)
    {
        if (e.Kind != EntryKind.Item || !view.Known || e.Item.Recipes.Count == 0)
        {
            return false;
        }
        var inUpgrade = false;
        var any = false;
        var heads = 0;
        var qualityPrefix = Labels.QualityFormat.Substring(0, Labels.QualityFormat.IndexOf('{'));
        foreach (var line in view.Lines)
        {
            if (line.Kind != DetailLineKind.Row)
            {
                continue;
            }
            var text = line.Text();
            if (text == Labels.AtUpgradeStation)
            {
                any = true;
                if (!inUpgrade)
                {
                    problems.Add($"{e}: \"{Labels.AtUpgradeStation}\" in the quality-1 crafting cost");
                    return any;
                }
            }
            else if (line.Indent == 0 && text.StartsWith(qualityPrefix, StringComparison.Ordinal))
            {
                inUpgrade = true;
            }
            else if (line.Indent == 0 && (text.StartsWith(Labels.At, StringComparison.Ordinal) || text == Labels.ByHand
                                          || text.StartsWith(Labels.ByHand + Labels.Separator, StringComparison.Ordinal)))
            {
                inUpgrade = false;
                heads++;
            }
        }
        var craftable = e.Item.Recipes.Count(r => r != null && !r.m_noCraftOnlyUpgrade);
        if (heads != craftable)
        {
            problems.Add($"{e}: {heads} crafting heads for {craftable} craftable recipes (upgrade-only recipes have none)");
        }
        return any;
    }

    // Spoiler rules on one detail view.
    private static void CheckView(string tag, Entry e, DetailView view, Knowledge k, HashSet<string> labels,
        List<string> problems)
    {
        var known = k.IsKnown(e);
        if (!known)
        {
            var hasRef = view.Lines.Any(l => l.Parts.Any(p => p.IsRef || p.OwnText));
            if (view.Title != Labels.Unknown || view.Icon != null || view.IconKind != IconKind.Unknown || hasRef)
            {
                problems.Add($"[{tag}] undiscovered {e} shows details (title \"{view.Title}\", refs {hasRef})");
            }
        }
        else if (e.Kind == EntryKind.Creature && view.IconKind == IconKind.Sprite
                 && (e.Creature.Trophy == null || !k.IsKnown(e.Creature.Trophy)))
        {
            problems.Add($"[{tag}] creature {e} shows a sprite of an undiscovered trophy");
        }
        // Subtitle not checked: tab, sub-group and piece category labels are game data, not entry names (a category
        // label such as a material word would give false alarms). Line labels are ours: they must never hold a name.
        foreach (var line in view.Lines)
        {
            foreach (var p in line.Parts)
            {
                if (p.Ref == null)
                {
                    if (!p.OwnText && !string.IsNullOrEmpty(p.Text))
                    {
                        labels.Add(Names.StripTags(p.Text));
                    }
                    continue;
                }
                var r = p.Ref;
                bool expect;
                if (r.Entry != null)
                {
                    expect = k.IsKnown(r.Entry);
                }
                else if (r.IsBiome)
                {
                    expect = k.IsBiomeKnown(r.Biome);
                }
                else
                {
                    expect = false;
                }
                if (r.Known != expect)
                {
                    problems.Add($"[{tag}] {e}: ref to {(r.Entry != null ? r.Entry.ToString() : r.Biome.ToString())} known={r.Known}, expected {expect}");
                }
                if (!r.Known && (r.Name != Labels.Unknown || r.Icon != null || r.IconKind != IconKind.Unknown))
                {
                    problems.Add($"[{tag}] {e}: undiscovered ref shows \"{r.Name}\" / icon {r.IconKind}");
                }
                if (r.Known && r.Entry != null && r.Entry.Kind == EntryKind.Creature && r.IconKind == IconKind.Sprite
                    && (r.Entry.Creature.Trophy == null || !k.IsKnown(r.Entry.Creature.Trophy)))
                {
                    problems.Add($"[{tag}] {e}: creature ref {r.Entry} shows an undiscovered trophy sprite");
                }
            }
        }
    }

    // List rows: unknown rows "???" without sprite, after known rows in each group; hidden unknown never listed;
    // search never return an undiscovered entry.
    // Return how many headers had an undiscovered tool or biome (the ones the header check really tests).
    private static int CheckRows(string tag, Catalog cat, Knowledge k, List<string> problems)
    {
        var hiddenHeaders = 0;
        for (var t = 0; t < Tabs.Count; t++)
        {
            var rows = ListBuilder.BuildTab(cat, k, (CatalogTab)t, showUndiscovered: true);
            var seenUnknown = false;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Kind == ListRowKind.Header)
                {
                    seenUnknown = false;
                    // Header text can name a tool or a biome: it goes through knowledge too.
                    var group = i + 1 < rows.Count && rows[i + 1].Kind == ListRowKind.Entry ? rows[i + 1].Entry.SubGroup : null;
                    var leak = HeaderLeak(row.Text, group, k);
                    if (leak != null)
                    {
                        problems.Add($"[{tag}] {(CatalogTab)t}: {leak}");
                    }
                    if (group != null && ((group.Tool != null && !k.IsKnown(group.Tool))
                                          || (group.IsBiomeGroup && group.Biome != Heightmap.Biome.None && !k.IsBiomeKnown(group.Biome))))
                    {
                        hiddenHeaders++;
                    }
                    continue;
                }
                var known = k.IsKnown(row.Entry);
                if (!known)
                {
                    seenUnknown = true;
                    if (row.Text != Labels.Unknown || row.Icon != null || row.IconKind != IconKind.Unknown)
                    {
                        problems.Add($"[{tag}] list row of undiscovered {row.Entry} shows \"{row.Text}\"");
                    }
                    if (row.Entry.HiddenUntilKnown)
                    {
                        problems.Add($"[{tag}] hidden-until-known {row.Entry} listed while undiscovered");
                    }
                }
                else if (seenUnknown)
                {
                    problems.Add($"[{tag}] known {row.Entry} listed after an undiscovered row in {(CatalogTab)t}");
                    seenUnknown = false;
                }
            }
            var onlyKnown = ListBuilder.BuildTab(cat, k, (CatalogTab)t, showUndiscovered: false);
            if (onlyKnown.Any(r => r.Kind == ListRowKind.Entry && !r.Known))
            {
                problems.Add($"[{tag}] ShowUndiscovered=false still lists undiscovered rows in {(CatalogTab)t}");
            }
        }
        var probes = 0;
        foreach (var e in cat.Entries)
        {
            if (k.IsKnown(e) || e.SortKey.Length < 3)
            {
                continue;
            }
            foreach (var row in ListBuilder.BuildSearch(cat, k, e.SortKey))
            {
                if (row.Kind == ListRowKind.Entry && !k.IsKnown(row.Entry))
                {
                    problems.Add($"[{tag}] search \"{e.SortKey}\" returned undiscovered {row.Entry}");
                    break;
                }
            }
            if (++probes >= 40)
            {
                break;
            }
        }
        return hiddenHeaders;
    }

    /// <summary>
    /// Spoiler check of one list header (UI self test use me too): an undiscovered tool is "???" in "tool · category"
    /// and never named; an unvisited biome group header is "???" and never names the biome. Null = fine.
    /// </summary>
    internal static string HeaderLeak(string text, SubGroup g, Knowledge k)
    {
        if (g == null)
        {
            return $"header \"{text}\" has no entry under it";
        }
        text ??= "";
        if (g.Tool != null && !k.IsKnown(g.Tool))
        {
            if (!text.StartsWith(Labels.Unknown, StringComparison.Ordinal) || ContainsWord(text, g.Tool.SortKey))
            {
                return $"header \"{text}\" names the undiscovered tool {g.Tool}";
            }
        }
        if (g.IsBiomeGroup && g.Biome != Heightmap.Biome.None && !k.IsBiomeKnown(g.Biome))
        {
            var name = Names.StripTags(Names.Localize(Biomes.Token(g.Biome)));
            if (text != Labels.Unknown || (name.Length > 0 && ContainsWord(text, name)))
            {
                return $"header \"{text}\" names the unvisited biome {g.Biome}";
            }
        }
        return null;
    }

    // Whole-word, case-insensitive.
    private static bool ContainsWord(string text, string word)
    {
        var at = 0;
        while (true)
        {
            var i = text.IndexOf(word, at, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
            {
                return false;
            }
            var before = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
            var end = i + word.Length;
            var after = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (before && after)
            {
                return true;
            }
            at = i + 1;
        }
    }

    // ---------------------------------------------------------------- compendium.knowledge

    private static IEnumerator RunKnowledge()
    {
        var box = new CatalogBox();
        while (!WaitStep(box))
        {
            yield return null;
        }
        if (box.Value == null)
        {
            SelfTest.Fail(KnowledgeName, box.Error);
            yield break;
        }
        var problems = new List<string>();
        string detail;
        try
        {
            detail = CheckKnowledge(box.Value, problems);
            CheckUnlocks(box.Value, problems);
        }
        catch (Exception e)
        {
            problems.Add($"check threw {e}");
            detail = "";
        }
        yield return MetByNamePlate(box.Value, problems);
        Report(KnowledgeName, problems, detail + "; a creature near but never aimed at is not met, its shown name plate "
                                        + "makes it met; unlock modes follow the Craft tab");
    }

    // "All recipes / pieces unlocked" (no-cost mode here, same code path as the world keys): every item the Craft tab
    // would offer counts as discovered (not a seasonal item out of season, not an upgrade-only recipe), every piece
    // too. Synchronous: m_noPlacementCost put back in the same frame.
    private static void CheckUnlocks(Catalog cat, List<string> problems)
    {
        var player = Player.m_localPlayer;
        var before = Knowledge.Take(cat, false, honourUnlocks: false);
        var season = player.CurrentSeason;
        bool Offered(Recipe r) => r != null && !r.m_noCraftOnlyUpgrade
                                  && (r.m_enabled || (season != null && season.Recipes.Contains(r)));
        var craftItem = cat.Entries.FirstOrDefault(e => e.Kind == EntryKind.Item && !before.IsKnown(e) && e.Item.Recipes.Any(Offered));
        var notOffered = cat.Entries.Where(e => e.Kind == EntryKind.Item && !before.IsKnown(e) && e.Item.Recipes.Count > 0
                                                && !e.Item.Recipes.Any(Offered)).ToList();
        var piece = cat.Entries.FirstOrDefault(e => e.Kind == EntryKind.Piece && !before.IsKnown(e));
        var old = player.m_noPlacementCost;
        try
        {
            player.m_noPlacementCost = true;
            var k = Knowledge.Take(cat, false, honourUnlocks: true);
            Expect(problems, k, craftItem, "no-cost mode (item with a recipe the Craft tab offers)");
            Expect(problems, k, piece, "no-cost mode (piece)");
            foreach (var e in notOffered)
            {
                if (k.IsKnown(e))
                {
                    problems.Add($"{e} discovered by no-cost mode although the Craft tab never offers its recipes "
                                 + "(seasonal out of season or upgrade-only)");
                }
            }
            SelfTest.Note(KnowledgeName, $"no-cost mode: {k.DiscoveredItems} items and {k.DiscoveredPieces} pieces discovered "
                                         + $"(before: {before.DiscoveredItems} / {before.DiscoveredPieces}); {notOffered.Count} "
                                         + $"items with recipes the Craft tab does not offer now stay undiscovered "
                                         + $"(season: {(season != null ? season.name : "none")}); item {craftItem}, piece {piece}");
        }
        finally
        {
            player.m_noPlacementCost = old;
        }
    }

    // "Met" through the real EnemyHud path: a creature spawned 5 m away, in front of the camera but off the crosshair,
    // gets its hidden plate (HudData) and is NOT met; once vanilla's hover timer is reset (what the crosshair on it
    // does), its plate shows and it IS met. The creature is held in place (Deer flee), its plate hidden again and the
    // creature destroyed; the Seen record put back (again one frame later: the destroy happen at the end of the frame).
    private static IEnumerator MetByNamePlate(Catalog cat, List<string> problems)
    {
        var player = Player.m_localPlayer;
        var hud = EnemyHud.instance;
        var cam = Utils.GetMainCamera();
        if (player == null || hud == null || cam == null || ZNetScene.instance == null || ZoneSystem.instance == null)
        {
            problems.Add("met check: no local player, EnemyHud, camera or world");
            yield break;
        }
        SelfTest.Note(KnowledgeName, $"EnemyHud: plates made within {hud.m_maxShowDistance} m (bosses {hud.m_maxShowDistanceBoss} m, "
                                     + $"when alerted); a creature's plate shows for {hud.m_hoverShowDuration} s after the crosshair");
        var k0 = Knowledge.Take(cat, false, honourUnlocks: false);
        var target = cat.FindCreature("$enemy_deer");
        if (target == null || k0.IsKnown(target) || target.Creature.Boss || target.Prefab == null)
        {
            target = cat.Entries.FirstOrDefault(e => e.Kind == EntryKind.Creature && !e.HiddenUntilKnown && !e.Creature.Boss
                                                     && e.Prefab != null && !k0.IsKnown(e));
        }
        if (target == null)
        {
            problems.Add("met check: no undiscovered creature to spawn");
            yield break;
        }
        player.m_customData.TryGetValue(OwnRecords.SeenKey, out var seenRaw);
        GameObject spawned = null;
        EnemyHud.HudData data = null;
        try
        {
            var up = Vector3.up;
            var fwd = Vector3.ProjectOnPlane(cam.transform.forward, up).normalized;
            var right = Vector3.ProjectOnPlane(cam.transform.right, up).normalized;
            var pos = player.transform.position + fwd * 4f + right * 3.5f;
            pos.y = ZoneSystem.instance.GetGroundHeight(pos) + 0.2f;
            var distance = Vector3.Distance(pos, player.transform.position);
            spawned = UnityEngine.Object.Instantiate(target.Prefab, pos, Quaternion.LookRotation(-fwd, up));
            var c = spawned.GetComponent<Character>();
            for (var i = 0; i < 60 && data == null; i++)
            {
                spawned.transform.position = pos;
                yield return null;
                hud.m_huds.TryGetValue(c, out data);
            }
            if (data == null)
            {
                problems.Add($"met check: no name plate made for {target} {distance:F1} m away");
                yield break;
            }
            for (var i = 0; i < 3; i++)
            {
                spawned.transform.position = pos;
                yield return null;
            }
            var shown = data.m_gui != null && data.m_gui.activeSelf;
            var met = OwnRecords.GetSeen(player).Contains(target.Key);
            var known = Knowledge.Take(cat, false, honourUnlocks: false).IsKnown(target);
            SelfTest.Note(KnowledgeName, $"met check: {target} {distance:F1} m away, plate made, shown {shown}, met {met}, "
                                         + $"discovered {known} (hover timer {data.m_hoverTimer:F0})");
            if (shown)
            {
                problems.Add($"met check: the plate of {target} shows before the crosshair was on it (hovered by the camera?)");
            }
            if (met != shown)
            {
                problems.Add($"met check: {target} met {met} while its plate shown {shown}");
            }
            if (!shown && known)
            {
                problems.Add($"met check: {target} discovered only by being near (plate made, never shown)");
            }

            // What the crosshair on it does in vanilla (EnemyHud.UpdateHuds: hover creature -> timer 0).
            data.m_hoverTimer = 0f;
            for (var i = 0; i < 3; i++)
            {
                spawned.transform.position = pos;
                yield return null;
            }
            shown = data.m_gui != null && data.m_gui.activeSelf;
            met = OwnRecords.GetSeen(player).Contains(target.Key);
            known = Knowledge.Take(cat, false, honourUnlocks: false).IsKnown(target);
            SelfTest.Note(KnowledgeName, $"met check: after the crosshair, plate shown {shown}, met {met}, discovered {known}");
            if (!shown)
            {
                problems.Add($"met check: the plate of {target} did not show after its hover timer was reset");
            }
            if (!met || !known)
            {
                problems.Add($"met check: {target} not met or not discovered after its plate was shown (met {met}, discovered {known})");
            }
        }
        finally
        {
            if (data != null)
            {
                data.m_hoverTimer = 99999f; // plate off again: nothing recorded before the destroy take effect
            }
            if (spawned != null && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(spawned);
            }
            RestoreCustom(player, OwnRecords.SeenKey, seenRaw);
            OwnRecords.ClearCache();
        }
        yield return null;
        RestoreCustom(player, OwnRecords.SeenKey, seenRaw);
        OwnRecords.ClearCache();
        if (OwnRecords.GetSeen(player).Contains(target.Key) && (seenRaw == null || !seenRaw.Contains(target.Key)))
        {
            problems.Add($"met check: {target} still in the Seen record after the clean-up");
        }
    }

    // Everything here is synchronous: changes and their undo happen in the same frame (try/finally).
    private static string CheckKnowledge(Catalog cat, List<string> problems)
    {
        var player = Player.m_localPlayer;
        var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
        if (player == null || profile == null)
        {
            problems.Add("no local player or profile");
            return "";
        }
        var k0 = Knowledge.Take(cat, false, honourUnlocks: false);
        SelfTest.Note(KnowledgeName, "before: " + k0.Summary());
        if (k0.DiscoveredCount * 4 > cat.Entries.Count)
        {
            problems.Add($"fresh character already knows {k0.DiscoveredCount} of {cat.Entries.Count} entries");
        }

        // Targets: vanilla anchors first, else any undiscovered entry of the right kind.
        var item = Pick(cat, k0, cat.FindItem("$item_raspberries"),
            e => e.Kind == EntryKind.Item && e.Item.Kind != ItemKind.Trophy && !e.HiddenUntilKnown && e.Item.Recipes.Count == 0);
        var trophyCreature = Pick(cat, k0, cat.FindCreature("$enemy_boar"),
            e => e.Kind == EntryKind.Creature && e.Creature.Trophy != null && !k0.IsKnown(e.Creature.Trophy));
        var trophy = trophyCreature != null ? trophyCreature.Creature.Trophy : null;
        var killed = Pick(cat, k0, cat.FindCreature("$enemy_greyling"),
            e => e.Kind == EntryKind.Creature && e != trophyCreature && (e.Creature.Trophy == null || e.Creature.Trophy != trophy));
        var met = Pick(cat, k0, cat.FindCreature("$enemy_deer"),
            e => e.Kind == EntryKind.Creature && e != trophyCreature && e != killed);
        var placed = Pick(cat, k0, cat.FindPiece("$piece_workbench"), e => e.Kind == EntryKind.Piece && !e.HiddenUntilKnown);
        var station = Pick(cat, k0, cat.FindPiece("$piece_forge"),
            e => e.Kind == EntryKind.Piece && e.Piece.StationName != null && e != placed);
        if (met == trophyCreature || met == killed)
        {
            met = null;
        }
        if (station == placed)
        {
            station = null;
        }
        var biome = Heightmap.Biome.None;
        foreach (var b in new[] { Heightmap.Biome.DeepNorth, Heightmap.Biome.AshLands, Heightmap.Biome.Mistlands })
        {
            if (!k0.IsBiomeKnown(b))
            {
                biome = b;
                break;
            }
        }
        SelfTest.Note(KnowledgeName, $"targets: item {item}, trophy {trophy} of {trophyCreature}, killed {killed}, met {met}, "
                                     + $"placed {placed}, station {station}, biome {biome}");

        // Snapshot of everything we touch.
        var materials = new HashSet<string>(player.m_knownMaterial);
        var recipes = new HashSet<string>(player.m_knownRecipes);
        var trophies = new HashSet<string>(player.m_trophies);
        var stations = new Dictionary<string, int>(player.m_knownStations);
        var stats = profile.m_playerStats[0];
        var kills = stats.m_enemyStats[0];
        var placedStats = stats.m_piecesPlacedStats;
        var hadKill = killed != null && kills.TryGetValue(killed.Key, out _);
        var oldKill = hadKill ? kills[killed.Key] : 0f;
        var hadPlaced = placed != null && placedStats.TryGetValue(placed.Key, out _);
        var oldPlaced = hadPlaced ? placedStats[placed.Key] : 0f;
        player.m_customData.TryGetValue(OwnRecords.SeenKey, out var seenRaw);
        player.m_customData.TryGetValue(OwnRecords.BiomesKey, out var biomesRaw);

        Knowledge k1;
        try
        {
            if (item != null)
            {
                player.AddKnownItem(item.Item.Drop.m_itemData);
            }
            if (trophy != null)
            {
                player.AddKnownItem(trophy.Item.Drop.m_itemData);
            }
            if (killed != null)
            {
                kills[killed.Key] = 1f;
            }
            if (met != null)
            {
                OwnRecords.MarkSeen(met.Key);
            }
            if (placed != null)
            {
                placedStats[placed.Key] = 1f;
            }
            if (station != null)
            {
                player.m_knownStations[station.Piece.StationName] = 1;
            }
            if (biome != Heightmap.Biome.None)
            {
                OwnRecords.RecordBiome(biome);
            }

            k1 = Knowledge.Take(cat, false, honourUnlocks: false);
            SelfTest.Note(KnowledgeName, "after marking: " + k1.Summary());
            Expect(problems, k1, item, "item held (AddKnownItem)");
            Expect(problems, k1, trophy, "trophy held (AddKnownItem)");
            Expect(problems, k1, trophyCreature, "creature by its trophy");
            Expect(problems, k1, killed, "creature by a kill");
            Expect(problems, k1, met, "creature met (own Seen record)");
            Expect(problems, k1, placed, "piece placed once (profile)");
            Expect(problems, k1, station, "station seen (m_knownStations)");
            if (biome != Heightmap.Biome.None && !k1.IsBiomeKnown(biome))
            {
                problems.Add($"biome {biome} not known after recording it");
            }
            if (killed != null && k1.Kills(killed) != 1)
            {
                problems.Add($"kill count of {killed} is {k1.Kills(killed)}, expected 1");
            }
            if (trophyCreature != null && Presentation.Icon(trophyCreature, k1, out _) != IconKind.Sprite)
            {
                problems.Add($"{trophyCreature} with known trophy does not show the trophy icon");
            }
            if (killed != null && (killed.Creature.Trophy == null || !k1.IsKnown(killed.Creature.Trophy))
                && Presentation.Icon(killed, k1, out _) != IconKind.GenericCreature)
            {
                problems.Add($"{killed} without known trophy does not show the generic creature icon");
            }
            if (killed != null)
            {
                var view = DetailBuilder.Build(killed, k1);
                if (!view.Lines.Any(l => l.Text() == string.Format(Labels.KilledFormat, 1)))
                {
                    problems.Add($"{killed} details miss \"Killed: 1\"");
                }
            }
        }
        finally
        {
            // Put everything back, whatever happened above.
            Restore(player.m_knownMaterial, materials);
            Restore(player.m_knownRecipes, recipes);
            Restore(player.m_trophies, trophies);
            player.m_knownStations.Clear();
            foreach (var kv in stations)
            {
                player.m_knownStations[kv.Key] = kv.Value;
            }
            if (killed != null)
            {
                if (hadKill)
                {
                    kills[killed.Key] = oldKill;
                }
                else
                {
                    kills.Remove(killed.Key);
                }
            }
            if (placed != null)
            {
                if (hadPlaced)
                {
                    placedStats[placed.Key] = oldPlaced;
                }
                else
                {
                    placedStats.Remove(placed.Key);
                }
            }
            RestoreCustom(player, OwnRecords.SeenKey, seenRaw);
            RestoreCustom(player, OwnRecords.BiomesKey, biomesRaw);
            OwnRecords.ClearCache();
            player.UpdateEvents();
        }

        var k2 = Knowledge.Take(cat, false, honourUnlocks: false);
        if (k2.DiscoveredCount != k0.DiscoveredCount || k2.KnownBiomes != k0.KnownBiomes)
        {
            problems.Add($"not restored: {k2.Summary()} (before: {k0.Summary()})");
        }
        return $"{k0.DiscoveredCount} discovered before, {k1.DiscoveredCount} after marking, {k2.DiscoveredCount} after restore";
    }

    // Anchor if listed and undiscovered, else first undiscovered entry matching the rule (catalog order).
    private static Entry Pick(Catalog cat, Knowledge k, Entry anchor, Func<Entry, bool> rule)
    {
        if (anchor != null && !k.IsKnown(anchor) && rule(anchor))
        {
            return anchor;
        }
        foreach (var e in cat.Entries)
        {
            if (!k.IsKnown(e) && rule(e))
            {
                return e;
            }
        }
        return null;
    }

    private static void Expect(List<string> problems, Knowledge k, Entry e, string how)
    {
        if (e != null && !k.IsKnown(e))
        {
            problems.Add($"{e} not discovered by {how}");
        }
    }

    private static void Restore(HashSet<string> set, HashSet<string> saved)
    {
        set.Clear();
        set.UnionWith(saved);
    }

    private static void RestoreCustom(Player player, string key, string raw)
    {
        if (raw == null)
        {
            player.m_customData.Remove(key);
        }
        else
        {
            player.m_customData[key] = raw;
        }
    }
#endif
}
