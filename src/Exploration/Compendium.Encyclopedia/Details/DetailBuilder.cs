using System;
using System.Collections.Generic;
using System.Globalization;
using MC.Shared;

namespace MC.Exploration.CompendiumEncyclopediaMod;

/// <summary>
/// Me turn (entry, knowledge) into a <see cref="DetailView"/>: lines of text and refs, no UI. One rule for every line
/// (goal 7): a name of another entry or a biome is always a <see cref="RefTarget"/> resolved against knowledge, never a
/// localized name formatted into a sentence. Numbers stay ("??? ×2", "??? 1-2 (50%)", "At ??? (level 2)").
/// Long lists ("Used in", "Recipes at this station", a station's conversions): known targets A→Z, unknown ones folded
/// into one "??? ×N not discovered yet" line; other blocks list each unknown on its own line.
/// Live game objects read at call time (recipes, drops, pieces), so in-place edits by other mods show.
/// Whole build in a try (calls vanilla tooltip code other mods patch): throw = title + "Details unavailable"; each block
/// in own try too: one failing block drop only itself (<see cref="DetailView.FailedBlocks"/>).
/// </summary>
internal static class DetailBuilder
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Build details. Never throw. Null entry or knowledge = empty "???" view.</summary>
    internal static DetailView Build(Entry entry, Knowledge k)
    {
        var view = new DetailView { Entry = entry };
        if (entry == null || k == null)
        {
            return view;
        }
        try
        {
            view.Known = k.IsKnown(entry);
            view.Title = Presentation.Name(entry, k);
            view.IconKind = Presentation.Icon(entry, k, out var sprite);
            view.Icon = sprite;
            if (!view.Known)
            {
                Undiscovered(view, entry);
                return view;
            }
            switch (entry.Kind)
            {
                case EntryKind.Item:
                    BuildItem(view, entry, k);
                    break;
                case EntryKind.Piece:
                    BuildPiece(view, entry, k);
                    break;
                default:
                    BuildCreature(view, entry, k);
                    break;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("DetailBuilder.Build", e);
            view.Failed = true;
            view.Lines.Clear();
            view.Errors.Add($"build: {e.GetType().Name}: {e.Message}");
            Paragraph(view, Labels.DetailsUnavailable, own: false);
        }
        return view;
    }

    // ---------------------------------------------------------------- undiscovered (3.5.4)

    private static void Undiscovered(DetailView v, Entry e)
    {
        v.Subtitle = Tabs.LocalizedLabel(e.Tab);
        string hint;
        switch (e.Kind)
        {
            case EntryKind.Item:
                hint = Labels.HintItem;
                break;
            case EntryKind.Piece:
                hint = Labels.HintPiece;
                break;
            default:
                hint = Labels.HintCreature;
                break;
        }
        Paragraph(v, Labels.NotDiscovered + " " + hint, own: false);
    }

    // ---------------------------------------------------------------- item (3.5.1)

    private static void BuildItem(DetailView v, Entry e, Knowledge k)
    {
        var label = e.SubGroup != null ? e.SubGroup.PlainLabel : "";
        v.Subtitle = Tabs.LocalizedLabel(e.Tab) + (label.Length > 0 ? Labels.Separator + label : "")
                     + (e.Seasonal ? Labels.Separator + Labels.Seasonal : "");
        Block(v, "item stats", () => ItemStats(v, e));
        Block(v, "item crafting", () => ItemCrafting(v, e, k));
        Block(v, "item sources", () => ItemSources(v, e, k));
        Block(v, "item used in", () => ItemUsedIn(v, e, k));
        Block(v, "item records", () => ItemRecords(v, e, k));
        Block(v, "item placeable", () => ItemPlaceable(v, e, k));
    }

    // The item's own text, as the crafting panel shows it (description, damage, armour, food, weight, set bonus).
    private static void ItemStats(DetailView v, Entry e)
    {
        var drop = e.Item.Drop;
        if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
        {
            return;
        }
        // GetTooltip read Player.m_localPlayer with no null check: without a player, description only.
        var player = Player.m_localPlayer;
        var raw = player != null ? TooltipKeepingSkills(drop.m_itemData, player) : drop.m_itemData.m_shared.m_description;
        var text = Names.Localize(raw).Trim();
        if (text.Length > 0)
        {
            Paragraph(v, text, own: true);
        }
        if (e.Item.Kind == ItemKind.Trophy)
        {
            var lore = Names.Localize(e.Key + "_lore");
            if (!Names.IsMissing(lore))
            {
                Paragraph(v, lore, own: true);
            }
        }
    }

    private static readonly HashSet<Skills.SkillType> SkillsBefore = new HashSet<Skills.SkillType>();
    private static readonly List<Skills.SkillType> SkillsAdded = new List<Skills.SkillType>();

    /// <summary>Level-0 skill entries GetTooltip added and me removed (self tests read it).</summary>
    internal static int SkillEntriesRemoved;

    // GetTooltip read skill levels (GetSkillLevel / GetSkillFactor -> private Skills.GetSkill), and GetSkill ADD a
    // level-0 entry for a skill the character never had: it would show in the Skills tab and be saved. Vanilla do that
    // only when the player hover the real item; reading a Encyclopedia page must change nothing. So me note the skill
    // keys before, and after the call (finally) remove the entries it added that are still level 0, accumulator 0.
    private static string TooltipKeepingSkills(ItemDrop.ItemData item, Player player)
    {
        var skills = player.GetSkills();
        var data = skills != null ? skills.m_skillData : null;
        SkillsBefore.Clear();
        if (data != null)
        {
            foreach (var key in data.Keys)
            {
                SkillsBefore.Add(key);
            }
        }
        try
        {
            return ItemDrop.ItemData.GetTooltip(item, 1, true, Game.m_worldLevel);
        }
        finally
        {
            if (data != null && data.Count > SkillsBefore.Count)
            {
                SkillsAdded.Clear();
                foreach (var kv in data)
                {
                    if (!SkillsBefore.Contains(kv.Key) && kv.Value != null && kv.Value.m_level == 0f && kv.Value.m_accumulator == 0f)
                    {
                        SkillsAdded.Add(kv.Key);
                    }
                }
                foreach (var key in SkillsAdded)
                {
                    data.Remove(key);
                    SkillEntriesRemoved++;
                }
                SkillsAdded.Clear();
            }
            SkillsBefore.Clear();
        }
    }

    private static void ItemCrafting(DetailView v, Entry e, Knowledge k)
    {
        var recipes = e.Item.Recipes;
        if (recipes.Count == 0)
        {
            return;
        }
        var cat = k.Catalog;
        Header(v, Names.Localize(Labels.Crafting));
        var maxQuality = e.Item.Drop != null && e.Item.Drop.m_itemData != null ? e.Item.Drop.m_itemData.m_shared.m_maxQuality : 1;
        foreach (var recipe in recipes)
        {
            if (recipe == null)
            {
                continue;
            }
            if (recipe.m_noCraftOnlyUpgrade)
            {
                // Vanilla Craft tab never list it (InventoryGui.UpdateRecipeList): no craft head, only its upgrades.
                Row(v, 0).Parts.Add(T(Labels.UpgradeOnly));
            }
            else
            {
                CraftHead(v, recipe, k);
            }
            if (maxQuality <= 1)
            {
                continue;
            }
            Header(v, Labels.Upgrades);
            var upgraderRecipe = false;
            for (var q = 2; q <= maxQuality; q++)
            {
                var qr = Row(v, 0);
                qr.Parts.Add(T(string.Format(Inv, Labels.QualityFormat, q)));
                var station = recipe.GetRequiredStation(q);
                if (station != null)
                {
                    qr.Parts.Add(T(" ("));
                    qr.Parts.Add(R(RefTarget.Of(cat.StationOf(station), k)));
                    qr.Parts.Add(T(string.Format(Inv, Labels.StationLevelFormat, recipe.GetRequiredStationLevel(q))));
                }
                upgraderRecipe |= Requirements(v, recipe, q, 1, k);
            }
            if (upgraderRecipe)
            {
                // Upgrade station ignore m_maxQuality (InventoryGui.UpdateRecipeList / UpdateRecipe): no upper end.
                Row(v, 0).Parts.Add(T(string.Format(Inv, Labels.BeyondMaxFormat, maxQuality)));
            }
        }
    }

    // "At <station> (level N)" or "By hand", then what crafting charge at quality 1 (the Craft tab's cost).
    private static void CraftHead(DetailView v, Recipe recipe, Knowledge k)
    {
        var cat = k.Catalog;
        var head = Row(v, 0);
        if (recipe.m_craftingStation != null)
        {
            head.Parts.Add(T(Labels.At));
            head.Parts.Add(R(RefTarget.Of(cat.StationOf(recipe.m_craftingStation), k)));
            head.Parts.Add(T(string.Format(Inv, Labels.LevelFormat, recipe.GetRequiredStationLevel(1))));
        }
        else
        {
            head.Parts.Add(T(Labels.ByHand));
        }
        if (cat.SeasonalRecipes.Contains(recipe))
        {
            head.Parts.Add(T(Labels.Separator + Labels.Seasonal));
        }
        if (recipe.m_amount > 1)
        {
            Row(v, 1).Parts.Add(T(string.Format(Inv, Labels.MakesFormat, recipe.m_amount)));
        }
        var indent = 1;
        if (recipe.m_requireOnlyOneIngredient)
        {
            Row(v, 1).Parts.Add(T(Labels.AnyOneOf));
            indent = 2;
        }
        Requirements(v, recipe, 1, indent, k);
    }

    // Normal requirements of a quality, then upgrade-station-only ones (m_upgraderResource) under their own label.
    // Quality 1 = crafting: vanilla never charge upgrade-station ones there (Player.HaveRequirementItems,
    // InventoryGui.SetupRequirementList drop them), so they are not listed. Return true when the recipe has any.
    private static bool Requirements(DetailView v, Recipe recipe, int quality, int indent, Knowledge k)
    {
        if (recipe.m_resources == null)
        {
            return false;
        }
        var cat = k.Catalog;
        var upgrader = false;
        var hasUpgrader = false;
        foreach (var req in recipe.m_resources)
        {
            if (req == null || req.m_resItem == null)
            {
                continue;
            }
            if (req.m_upgraderResource)
            {
                hasUpgrader = true;
                upgrader |= quality > 1 && req.GetAmount(quality) > 0;
                continue;
            }
            var amount = req.GetAmount(quality);
            if (amount > 0)
            {
                AmountRow(v, indent, RefTarget.Of(cat.ItemOf(req.m_resItem), k), amount);
            }
        }
        if (!upgrader)
        {
            return hasUpgrader;
        }
        Row(v, indent).Parts.Add(T(Labels.AtUpgradeStation));
        foreach (var req in recipe.m_resources)
        {
            if (req != null && req.m_resItem != null && req.m_upgraderResource)
            {
                var amount = req.GetAmount(quality);
                if (amount > 0)
                {
                    AmountRow(v, indent + 1, RefTarget.Of(cat.ItemOf(req.m_resItem), k), amount);
                }
            }
        }
        return true;
    }

    private static void ItemSources(DetailView v, Entry e, Knowledge k)
    {
        var info = e.Item;
        if (!info.HasNonRecipeSource)
        {
            if (!info.Craftable)
            {
                Header(v, Labels.WhereToGet);
                Paragraph(v, Labels.NoSource, own: false);
            }
            return; // crafted only: the crafting block say it
        }
        Header(v, Labels.WhereToGet);
        if (info.DroppedBy.Count > 0)
        {
            Row(v, 0).Parts.Add(T(Labels.DroppedBy));
            foreach (var link in info.DroppedBy)
            {
                var row = Row(v, 1);
                row.Parts.Add(R(RefTarget.Of(link.Creature, k)));
                row.Parts.Add(T(" " + DropAmount(link.Drop)));
            }
        }
        foreach (var kind in new[] { GatherKind.Picked, GatherKind.Mined, GatherKind.Chopped, GatherKind.Broken })
        {
            foreach (var g in info.Gathered)
            {
                if (g.Kind != kind)
                {
                    continue;
                }
                var row = Row(v, 0);
                row.Parts.Add(T(GatherLabel(kind)));
                if (g.Biomes != Heightmap.Biome.None)
                {
                    row.Parts.Add(T(Labels.In));
                    BiomeRefs(row, g.Biomes, k);
                }
            }
        }
        foreach (var cv in info.MadeFrom)
        {
            var row = Row(v, 0);
            row.Parts.Add(T(Labels.MadeFrom));
            row.Parts.Add(R(RefTarget.Of(cv.From, k)));
            if (cv.Station != null)
            {
                row.Parts.Add(T(Labels.AtStation));
                row.Parts.Add(R(RefTarget.Of(cv.Station, k)));
            }
        }
        foreach (var producer in info.ProducedBy)
        {
            var row = Row(v, 0);
            row.Parts.Add(T(Labels.ProducedBy));
            row.Parts.Add(R(RefTarget.Of(producer, k)));
        }
        if (info.ProducedUnlisted && info.ProducedBy.Count == 0)
        {
            Row(v, 0).Parts.Add(T(Labels.ProducedByBuilding));
        }
        foreach (var creature in info.LaidBy)
        {
            var row = Row(v, 0);
            row.Parts.Add(T(Labels.LaidBy));
            row.Parts.Add(R(RefTarget.Of(creature, k)));
        }
        if (info.InChests)
        {
            Row(v, 0).Parts.Add(T(Labels.FoundInChests));
        }
        if (info.Caught)
        {
            var row = Row(v, 0);
            if (info.CaughtIn != Heightmap.Biome.None)
            {
                row.Parts.Add(T(Labels.CaughtIn));
                BiomeRefs(row, info.CaughtIn, k);
            }
            else
            {
                row.Parts.Add(T(Labels.Caught));
            }
        }
        if (info.SoldByTraders > 0)
        {
            Row(v, 0).Parts.Add(T(Labels.SoldByTrader));
        }
    }

    private static string GatherLabel(GatherKind kind)
    {
        switch (kind)
        {
            case GatherKind.Picked:
                return Labels.Picked;
            case GatherKind.Mined:
                return Labels.Mined;
            case GatherKind.Chopped:
                return Labels.Chopped;
            default:
                return Labels.Broken;
        }
    }

    // Long list: known targets A→Z (with station for conversions), unknown targets folded. Then tame lines.
    private static void ItemUsedIn(DetailView v, Entry e, Knowledge k)
    {
        var info = e.Item;
        if (info.UsedIn.Count == 0 && info.Tames.Count == 0)
        {
            return;
        }
        Header(v, Labels.UsedIn);
        var seen = new HashSet<Entry>();
        var known = new List<UseLink>();
        var unknown = 0;
        foreach (var link in info.UsedIn)
        {
            if (link.Target == null || !seen.Add(link.Target))
            {
                continue;
            }
            if (k.IsKnown(link.Target))
            {
                known.Add(link);
            }
            else
            {
                unknown++;
            }
        }
        known.Sort((a, b) => Names.CompareNames(a.Target.SortKey, b.Target.SortKey));
        foreach (var link in known)
        {
            var row = Row(v, 0);
            row.Parts.Add(R(RefTarget.Of(link.Target, k)));
            if (link.Kind == UseKind.Conversion && link.Station != null)
            {
                row.Parts.Add(T(Labels.AtStation));
                row.Parts.Add(R(RefTarget.Of(link.Station, k)));
            }
        }
        Collapsed(v, unknown, 0);
        foreach (var creature in info.Tames)
        {
            var row = Row(v, 0);
            row.Parts.Add(T(Labels.TamesPrefix));
            row.Parts.Add(R(RefTarget.Of(creature, k)));
        }
    }

    private static void ItemRecords(DetailView v, Entry e, Knowledge k)
    {
        var picked = k.PickedUp(e);
        var crafted = k.Crafted(e);
        var eaten = k.Eaten(e);
        if (picked == 0 && crafted == 0 && eaten == 0)
        {
            return;
        }
        Header(v, Labels.YourRecords);
        if (picked > 0)
        {
            Row(v, 0).Parts.Add(T(string.Format(Inv, Labels.PickedUpFormat, picked)));
        }
        if (crafted > 0)
        {
            Row(v, 0).Parts.Add(T(string.Format(Inv, Labels.CraftedFormat, crafted)));
        }
        if (eaten > 0)
        {
            Row(v, 0).Parts.Add(T(string.Format(Inv, Labels.EatenFormat, eaten)));
        }
    }

    private static void ItemPlaceable(DetailView v, Entry e, Knowledge k)
    {
        var tool = e.Item.PlaceableWith;
        if (tool == null)
        {
            return;
        }
        var row = Row(v, 0);
        if (tool == e)
        {
            row.Parts.Add(T(Labels.PlaceableSelf));
            return;
        }
        row.Parts.Add(T(Labels.PlaceableWith));
        row.Parts.Add(R(RefTarget.Of(tool, k)));
    }

    // ---------------------------------------------------------------- piece (3.5.2)

    private static void BuildPiece(DetailView v, Entry e, Knowledge k)
    {
        var info = e.Piece;
        var label = Names.Localize(info.CategoryLabel);
        v.Subtitle = Tabs.LocalizedLabel(CatalogTab.Building) + (label.Length > 0 ? Labels.Separator + label : "")
                     + (e.Seasonal ? Labels.Separator + Labels.Seasonal : "");
        Block(v, "piece tool", () =>
        {
            if (info.Tool != null)
            {
                var row = Row(v, 0);
                row.Parts.Add(T(Labels.BuiltWith));
                row.Parts.Add(R(RefTarget.Of(info.Tool, k)));
            }
        });
        Block(v, "piece description", () => PieceDescription(v, info));
        Block(v, "piece cost", () => PieceCost(v, info, k));
        Block(v, "piece station", () => PieceStation(v, e, k));
        Block(v, "piece conversions", () => PieceConversions(v, info, k));
        Block(v, "piece records", () =>
        {
            var placed = k.Placed(e);
            if (placed > 0)
            {
                Header(v, Labels.YourRecords);
                Row(v, 0).Parts.Add(T(string.Format(Inv, Labels.PlacedFormat, placed)));
            }
        });
    }

    private static void PieceDescription(DetailView v, PieceInfo info)
    {
        var piece = info.Piece;
        if (piece == null)
        {
            return;
        }
        var text = Names.Localize(piece.m_description).Trim();
        if (text.Length > 0 && !Names.IsMissing(text))
        {
            Paragraph(v, text, own: true);
        }
        if (piece.m_comfort > 0)
        {
            Row(v, 0).Parts.Add(T(string.Format(Inv, Labels.ComfortFormat, piece.m_comfort)));
        }
    }

    private static void PieceCost(DetailView v, PieceInfo info, Knowledge k)
    {
        var piece = info.Piece;
        if (piece == null)
        {
            return;
        }
        var cat = k.Catalog;
        var any = false;
        if (piece.m_resources != null)
        {
            foreach (var req in piece.m_resources)
            {
                if (req == null || req.m_resItem == null || req.m_amount <= 0)
                {
                    continue;
                }
                if (!any)
                {
                    Header(v, Labels.BuildCost);
                    any = true;
                }
                AmountRow(v, 0, RefTarget.Of(cat.ItemOf(req.m_resItem), k), req.m_amount);
            }
        }
        if (piece.m_craftingStation != null)
        {
            if (!any)
            {
                Header(v, Labels.BuildCost);
            }
            var row = Row(v, 0);
            row.Parts.Add(T(Labels.NeedsA));
            row.Parts.Add(R(RefTarget.Of(cat.StationOf(piece.m_craftingStation), k)));
            row.Parts.Add(T(Labels.Nearby));
        }
    }

    private static void PieceStation(DetailView v, Entry e, Knowledge k)
    {
        var info = e.Piece;
        if (info.ExtensionOf != null)
        {
            var row = Row(v, 0);
            row.Parts.Add(T(Labels.UpgradeFor));
            row.Parts.Add(R(RefTarget.Of(info.ExtensionOf, k)));
        }
        if (info.StationName == null)
        {
            return;
        }
        var level = k.StationLevel(e);
        if (level > 0)
        {
            Row(v, 0).Parts.Add(T(string.Format(Inv, Labels.HighestLevelFormat, level)));
        }
        if (info.Extensions.Count > 0)
        {
            Header(v, Labels.StationUpgrades);
            foreach (var ext in KnownFirst(info.Extensions, k))
            {
                Row(v, 0).Parts.Add(R(RefTarget.Of(ext, k)));
            }
        }
        if (info.RecipesHere.Count == 0)
        {
            return;
        }
        // One line per item made here (lowest level), known A→Z, unknown folded.
        var cat = k.Catalog;
        var levels = new Dictionary<Entry, int>();
        foreach (var recipe in info.RecipesHere)
        {
            // Upgrade-only recipe: nothing is made here from it (Craft tab never list it).
            var item = recipe != null && !recipe.m_noCraftOnlyUpgrade ? cat.ItemOf(recipe.m_item) : null;
            if (item == null)
            {
                continue;
            }
            var lvl = recipe.GetRequiredStationLevel(1);
            if (!levels.TryGetValue(item, out var old) || lvl < old)
            {
                levels[item] = lvl;
            }
        }
        if (levels.Count == 0)
        {
            return;
        }
        Header(v, Labels.RecipesHere);
        var known = new List<Entry>();
        var unknown = 0;
        foreach (var item in levels.Keys)
        {
            if (k.IsKnown(item))
            {
                known.Add(item);
            }
            else
            {
                unknown++;
            }
        }
        known.Sort((a, b) => Names.CompareNames(a.SortKey, b.SortKey));
        foreach (var item in known)
        {
            var row = Row(v, 0);
            row.Parts.Add(R(RefTarget.Of(item, k)));
            row.Parts.Add(T(string.Format(Inv, Labels.LevelFormat, levels[item])));
        }
        Collapsed(v, unknown, 0);
    }

    // Both sides known: A→Z by input. One side known: listed with other side "???". Both unknown: folded.
    private static void PieceConversions(DetailView v, PieceInfo info, Knowledge k)
    {
        if (info.Conversions.Count > 0)
        {
            Header(v, Labels.Conversions);
            var both = new List<Conversion>();
            var half = new List<Conversion>();
            var none = 0;
            foreach (var cv in info.Conversions)
            {
                var from = k.IsKnown(cv.From);
                var to = k.IsKnown(cv.To);
                if (from && to)
                {
                    both.Add(cv);
                }
                else if (from || to)
                {
                    half.Add(cv);
                }
                else
                {
                    none++;
                }
            }
            both.Sort((a, b) => Names.CompareNames(a.From.SortKey, b.From.SortKey));
            foreach (var cv in both)
            {
                ConversionRow(v, cv, k);
            }
            foreach (var cv in half)
            {
                ConversionRow(v, cv, k);
            }
            Collapsed(v, none, 0);
        }
        foreach (var item in info.Produces)
        {
            var row = Row(v, 0);
            row.Parts.Add(T(Labels.Produces));
            row.Parts.Add(R(RefTarget.Of(item, k)));
        }
    }

    private static void ConversionRow(DetailView v, Conversion cv, Knowledge k)
    {
        var row = Row(v, 0);
        row.Parts.Add(T(Labels.Turns));
        row.Parts.Add(R(RefTarget.Of(cv.From, k)));
        row.Parts.Add(T(Labels.Into));
        row.Parts.Add(R(RefTarget.Of(cv.To, k)));
    }

    // ---------------------------------------------------------------- creature (3.5.3)

    private static void BuildCreature(DetailView v, Entry e, Knowledge k)
    {
        var info = e.Creature;
        v.Subtitle = Tabs.LocalizedLabel(CatalogTab.Creatures) + (info.Boss ? Labels.Separator + Labels.Boss : "");
        Block(v, "creature counts", () =>
        {
            Row(v, 0).Parts.Add(T(string.Format(Inv, Labels.KilledFormat, k.Kills(e))));
            var tames = k.Tames(e);
            if (tames >= 1)
            {
                Row(v, 0).Parts.Add(T(string.Format(Inv, Labels.TamedFormat, tames)));
            }
        });
        Block(v, "creature habitat", () =>
        {
            Header(v, Labels.Habitat);
            if (info.Habitat == Heightmap.Biome.None)
            {
                Row(v, 0).Parts.Add(T(Labels.HabitatUnknown));
            }
            foreach (var b in Biomes.Progression)
            {
                if ((info.Habitat & b) != 0)
                {
                    Row(v, 0).Parts.Add(R(RefTarget.OfBiome(b, k)));
                }
            }
            foreach (var boss in info.AfterBosses)
            {
                var row = Row(v, 0);
                row.Parts.Add(T(Labels.AppearsAfter));
                row.Parts.Add(R(RefTarget.Of(boss, k)));
            }
        });
        Block(v, "creature drops", () =>
        {
            if (info.Drops.Count == 0)
            {
                return;
            }
            Header(v, Labels.Drops);
            foreach (var link in info.Drops)
            {
                var row = Row(v, 0);
                row.Parts.Add(R(RefTarget.Of(link.Item, k)));
                row.Parts.Add(T(" " + DropAmount(link.Drop)));
            }
        });
        Block(v, "creature taming", () =>
        {
            if (!info.Tameable)
            {
                return;
            }
            Header(v, Labels.Taming);
            Row(v, 0).Parts.Add(T(Labels.CanBeTamed));
            foreach (var food in info.Eats)
            {
                Row(v, 1).Parts.Add(R(RefTarget.Of(food, k)));
            }
            if (info.Saddle != null)
            {
                var row = Row(v, 0);
                row.Parts.Add(T(Labels.CanWear));
                row.Parts.Add(R(RefTarget.Of(info.Saddle, k)));
            }
        });
        Block(v, "creature combat", () => CreatureCombat(v, e, k));
    }

    // Extra knowledge tier (decision 3): health and damage modifiers after the first kill (or reveal all).
    private static void CreatureCombat(DetailView v, Entry e, Knowledge k)
    {
        var c = e.Creature.Character;
        if (c == null || (!k.RevealAll && k.Kills(e) < 1))
        {
            return;
        }
        Header(v, Labels.AfterFirstKill);
        Row(v, 0).Parts.Add(T(string.Format(Inv, Labels.HealthFormat, Math.Round(c.m_health).ToString("0", Inv))));
        var mods = c.m_damageModifiers;
        var lines = new List<string>();
        AddMod(lines, "$inventory_blunt", mods.m_blunt);
        AddMod(lines, "$inventory_slash", mods.m_slash);
        AddMod(lines, "$inventory_pierce", mods.m_pierce);
        AddMod(lines, "$inventory_chop", mods.m_chop);
        AddMod(lines, "$inventory_pickaxe", mods.m_pickaxe);
        AddMod(lines, "$inventory_fire", mods.m_fire);
        AddMod(lines, "$inventory_frost", mods.m_frost);
        AddMod(lines, "$inventory_lightning", mods.m_lightning);
        AddMod(lines, "$inventory_poison", mods.m_poison);
        AddMod(lines, "$inventory_spirit", mods.m_spirit);
        if (lines.Count == 0)
        {
            return;
        }
        Row(v, 0).Parts.Add(T(Names.Localize(Labels.DamageModifiers) + ":"));
        foreach (var line in lines)
        {
            Row(v, 1).Parts.Add(T(line));
        }
    }

    // Vanilla words, like SE_Stats.GetDamageModifiersTooltipString (Normal and Ignore skipped).
    private static void AddMod(List<string> lines, string typeToken, HitData.DamageModifier mod)
    {
        string word;
        switch (mod)
        {
            case HitData.DamageModifier.Immune:
                word = "$inventory_immune";
                break;
            case HitData.DamageModifier.SlightlyResistant:
                word = "$inventory_slightlyresistant";
                break;
            case HitData.DamageModifier.Resistant:
                word = "$inventory_resistant";
                break;
            case HitData.DamageModifier.VeryResistant:
                word = "$inventory_veryresistant";
                break;
            case HitData.DamageModifier.SlightlyWeak:
                word = "$inventory_slightlyweak";
                break;
            case HitData.DamageModifier.Weak:
                word = "$inventory_weak";
                break;
            case HitData.DamageModifier.VeryWeak:
                word = "$inventory_veryweak";
                break;
            default:
                return;
        }
        lines.Add(Names.Localize(typeToken) + ": " + Names.Localize(word));
    }

    // ---------------------------------------------------------------- shared helpers

    /// <summary>
    /// Drop numbers as the game rolls them at normal world settings, 0 stars (design 1.9): amount min to max-1 when
    /// max > min (Unity integer Random.Range), else min; chance in %; "more with stars" when level multiplier on.
    /// </summary>
    internal static string DropAmount(CharacterDrop.Drop d)
    {
        if (d == null)
        {
            return "";
        }
        string amount;
        if (d.m_onePerPlayer)
        {
            amount = Labels.OnePerPlayer;
        }
        else
        {
            var min = d.m_amountMin;
            var high = d.m_amountMax > min ? d.m_amountMax - 1 : min;
            amount = high > min ? $"{Names.Num(min)}-{Names.Num(high)}" : Names.Num(min);
        }
        var chance = Math.Min(1f, Math.Max(0f, d.m_chance)) * 100f;
        return $"{amount} ({chance.ToString("0.#", Inv)}%{(d.m_levelMultiplier ? Labels.MoreWithStars : "")})";
    }

    // Known entries A→Z, then unknown ones in given order (each on its own line).
    private static List<Entry> KnownFirst(List<Entry> entries, Knowledge k)
    {
        var known = new List<Entry>();
        var unknown = new List<Entry>();
        foreach (var e in entries)
        {
            (k.IsKnown(e) ? known : unknown).Add(e);
        }
        known.Sort((a, b) => Names.CompareNames(a.SortKey, b.SortKey));
        known.AddRange(unknown);
        return known;
    }

    private static void BiomeRefs(DetailLine row, Heightmap.Biome mask, Knowledge k)
    {
        var first = true;
        foreach (var b in Biomes.Progression)
        {
            if ((mask & b) == 0)
            {
                continue;
            }
            if (!first)
            {
                row.Parts.Add(T(Labels.ListSeparator));
            }
            row.Parts.Add(R(RefTarget.OfBiome(b, k)));
            first = false;
        }
    }

    private static void AmountRow(DetailView v, int indent, RefTarget target, int amount)
    {
        var row = Row(v, indent);
        row.Parts.Add(R(target));
        row.Parts.Add(T(string.Format(Inv, Labels.TimesFormat, amount)));
    }

    private static void Block(DetailView v, string name, Action body)
    {
        var start = v.Lines.Count;
        try
        {
            body();
        }
        catch (Exception e)
        {
            if (v.Lines.Count > start)
            {
                v.Lines.RemoveRange(start, v.Lines.Count - start);
            }
            v.FailedBlocks++;
            v.Errors.Add($"{name}: {e.GetType().Name}: {e.Message}");
            PatchGuard.Report($"DetailBuilder ({name})", e);
        }
    }

    private static DetailLine Row(DetailView v, int indent)
    {
        var line = new DetailLine { Kind = DetailLineKind.Row, Indent = indent };
        v.Lines.Add(line);
        return line;
    }

    private static void Header(DetailView v, string text)
    {
        var line = new DetailLine { Kind = DetailLineKind.Header };
        line.Parts.Add(T(text));
        v.Lines.Add(line);
    }

    private static void Paragraph(DetailView v, string text, bool own)
    {
        var line = new DetailLine { Kind = DetailLineKind.Paragraph };
        line.Parts.Add(new DetailPart(text, null, own));
        v.Lines.Add(line);
    }

    private static void Collapsed(DetailView v, int count, int indent)
    {
        if (count <= 0)
        {
            return;
        }
        var line = new DetailLine { Kind = DetailLineKind.Collapsed, Indent = indent, CollapsedCount = count };
        line.Parts.Add(T(string.Format(Inv, Labels.CollapsedFormat, count)));
        v.Lines.Add(line);
    }

    private static DetailPart T(string text) => new DetailPart(text, null, false);

    private static DetailPart R(RefTarget target) => new DetailPart(null, target, false);
}
