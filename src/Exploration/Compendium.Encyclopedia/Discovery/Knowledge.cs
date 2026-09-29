using System;
using System.Collections.Generic;
using MC.Shared;

namespace MC.Exploration.CompendiumEncyclopediaMod;

/// <summary>
/// Me = snapshot of what this character knows, for one catalog. Take a new one on every open, tab change, search
/// change (a few thousand hash lookups); never cache across opens. Read only: me never write vanilla data.
/// Rules (design 3.3):
///   item     = held once (m_knownMaterial) or recipe known (m_knownRecipes) or, trophy, in m_trophies; world
///              modifier AllRecipesUnlocked / debug no-cost mode and item has a recipe the Craft tab offer (enabled or
///              in season, not upgrade-only);
///   piece    = buildable known (m_knownRecipes) or, station, station seen (m_knownStations) or placed once (profile);
///              AllPiecesUnlocked / no-cost mode;
///   creature = killed (profile slot 0) or its trophy known or tamed (Creature Kill and Tame Counts data) or met
///              (own Seen record: vanilla showed its name plate);
///   biome    = own Biomes record, or vanilla m_knownBiome holds its token, its name in current language, or an
///              alternate-biome name of it.
/// RevealAll = every entry and biome known.
/// </summary>
internal sealed class Knowledge
{
    private readonly bool[] _known;
    private Dictionary<string, float> _kills;
    private Dictionary<string, float> _placed;
    private Dictionary<string, float> _pickedUp;
    private Dictionary<string, float> _crafted;
    private Dictionary<string, float> _eaten;
    private IReadOnlyDictionary<string, int> _tames;
    private Dictionary<string, int> _stations;

    /// <summary>Catalog this snapshot belong to (entry indexes).</summary>
    internal readonly Catalog Catalog;

    /// <summary>Spoiler mode: everything known.</summary>
    internal readonly bool RevealAll;

    /// <summary>Biomes known (flags).</summary>
    internal Heightmap.Biome KnownBiomes { get; private set; }

    /// <summary>Discovered entries (hidden-until-known ones included once known).</summary>
    internal int DiscoveredCount { get; private set; }

    /// <summary>Entries the list can show: every entry except unknown hidden-until-known ones. "Discovered N / M" use it.</summary>
    internal int ListedCount { get; private set; }

    internal int DiscoveredItems { get; private set; }
    internal int DiscoveredPieces { get; private set; }
    internal int DiscoveredCreatures { get; private set; }

    /// <summary>Creatures in the own Seen record (met).</summary>
    internal int MetCount { get; private set; }

    private Knowledge(Catalog catalog, bool revealAll)
    {
        Catalog = catalog;
        RevealAll = revealAll;
        _known = new bool[catalog.Entries.Count];
    }

    /// <summary>Is this entry discovered? Null = false. Entry of another catalog = false.</summary>
    internal bool IsKnown(Entry e)
    {
        if (e == null)
        {
            return false;
        }
        if (RevealAll)
        {
            return true;
        }
        return e.Index >= 0 && e.Index < _known.Length && ReferenceEquals(Catalog.Entries[e.Index], e) && _known[e.Index];
    }

    /// <summary>Listed in the window: not hidden-until-known, or known.</summary>
    internal bool IsListed(Entry e) => e != null && (!e.HiddenUntilKnown || IsKnown(e));

    /// <summary>Single biome flag known? (None = false.)</summary>
    internal bool IsBiomeKnown(Heightmap.Biome biome) =>
        biome != Heightmap.Biome.None && (RevealAll || (KnownBiomes & biome) == biome);

    /// <summary>All-time kills (profile slot 0, total), rounded. 0 for non-creatures.</summary>
    internal int Kills(Entry creature) => Read(_kills, creature);

    /// <summary>Tames counted by Creature Kill and Tame Counts (0 when that mod never ran).</summary>
    internal int Tames(Entry creature) =>
        creature != null && _tames != null && _tames.TryGetValue(creature.Key, out var n) ? n : 0;

    /// <summary>Pieces placed (profile slot 0).</summary>
    internal int Placed(Entry piece) => Read(_placed, piece);

    internal int PickedUp(Entry item) => Read(_pickedUp, item);

    internal int Crafted(Entry item) => Read(_crafted, item);

    internal int Eaten(Entry item) => Read(_eaten, item);

    /// <summary>Highest level seen of this station piece (m_knownStations), 0 = never seen.</summary>
    internal int StationLevel(Entry piece)
    {
        var name = piece != null && piece.Piece != null ? piece.Piece.StationName : null;
        return name != null && _stations != null && _stations.TryGetValue(name, out var level) ? level : 0;
    }

    private static int Read(Dictionary<string, float> dict, Entry e) =>
        e != null && dict != null && dict.TryGetValue(e.Key, out var v) ? Names.ToCount(v) : 0;

    // ---------------------------------------------------------------- take

    /// <summary>
    /// Snapshot for the local player. No player (main menu, respawn wait) = nothing known (except RevealAll).
    /// <paramref name="honourUnlocks"/> false = ignore AllRecipesUnlocked / AllPiecesUnlocked / no-cost (self tests only).
    /// </summary>
    internal static Knowledge Take(Catalog catalog, bool revealAll, bool honourUnlocks = true)
    {
        var k = new Knowledge(catalog, revealAll);
        var player = Player.m_localPlayer;
        k.ReadStats(player);
        k.KnownBiomes = ComputeBiomes(player);

        if (player != null)
        {
            var zs = ZoneSystem.instance;
            var allRecipes = honourUnlocks && (player.m_noPlacementCost
                                               || (zs != null && zs.GetGlobalKey(GlobalKeys.AllRecipesUnlocked)));
            var allPieces = honourUnlocks && (player.m_noPlacementCost
                                              || (zs != null && zs.GetGlobalKey(GlobalKeys.AllPiecesUnlocked)));
            var materials = player.m_knownMaterial;
            var recipes = player.m_knownRecipes;
            var trophies = player.m_trophies;
            var season = player.CurrentSeason;
            var seen = OwnRecords.GetSeen(player);
            k.MetCount = seen.Count;

            var entries = catalog.Entries;
            // Items first: creatures read their trophy item.
            foreach (var e in entries)
            {
                if (e.Kind == EntryKind.Item)
                {
                    k._known[e.Index] = ItemKnown(e, materials, recipes, trophies, allRecipes, season);
                }
                else if (e.Kind == EntryKind.Piece)
                {
                    k._known[e.Index] = allPieces || recipes.Contains(e.Key)
                                        || (e.Piece.StationName != null && k._stations != null
                                            && k._stations.ContainsKey(e.Piece.StationName))
                                        || Read(k._placed, e) >= 1;
                }
            }
            foreach (var e in entries)
            {
                if (e.Kind != EntryKind.Creature)
                {
                    continue;
                }
                var trophy = e.Creature.Trophy;
                k._known[e.Index] = Read(k._kills, e) >= 1 || (trophy != null && k._known[trophy.Index])
                                    || k.Tames(e) >= 1 || seen.Contains(e.Key);
            }
        }
        k.Count();
        return k;
    }

    /// <summary>Self tests only: knowledge from a rule (real stats kept for counts), biomes given.</summary>
    internal static Knowledge Synthetic(Catalog catalog, Func<Entry, bool> known, Heightmap.Biome biomes)
    {
        var k = new Knowledge(catalog, false);
        k.ReadStats(Player.m_localPlayer);
        k.KnownBiomes = biomes;
        foreach (var e in catalog.Entries)
        {
            k._known[e.Index] = known(e);
        }
        k.Count();
        return k;
    }

    private static bool ItemKnown(Entry e, HashSet<string> materials, HashSet<string> recipes, HashSet<string> trophies,
        bool allRecipes, SeasonalItemGroup season)
    {
        if (materials.Contains(e.Key) || recipes.Contains(e.Key) || (allRecipes && UnlockedByAll(e, season)))
        {
            return true;
        }
        if (e.Item.Kind == MC.Shared.ItemKind.Trophy)
        {
            foreach (var p in e.Item.Prefabs)
            {
                if (trophies.Contains(p.name))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // "All recipes unlocked" / no-cost: known when the Craft tab would offer one of its recipes. Like
    // Player.GetAvailableRecipes: a disabled recipe only in its season; like InventoryGui.UpdateRecipeList: never an
    // upgrade-only one (m_noCraftOnlyUpgrade).
    private static bool UnlockedByAll(Entry e, SeasonalItemGroup season)
    {
        foreach (var r in e.Item.Recipes)
        {
            if (r != null && !r.m_noCraftOnlyUpgrade
                          && (r.m_enabled || (season != null && season.Recipes != null && season.Recipes.Contains(r))))
            {
                return true;
            }
        }
        return false;
    }

    private void ReadStats(Player player)
    {
        var game = Game.instance;
        var profile = game != null ? game.GetPlayerProfile() : null;
        var slots = profile != null ? profile.m_playerStats : null;
        var stats = slots != null && slots.Length > 0 ? slots[0] : null;
        if (stats != null)
        {
            _kills = stats.m_enemyStats != null && stats.m_enemyStats.Length > 0 ? stats.m_enemyStats[0] : null;
            _placed = stats.m_piecesPlacedStats;
            _pickedUp = stats.m_itemPickupStats;
            _crafted = stats.m_itemCraftStats;
            _eaten = stats.m_foodEatenStats;
        }
        if (player != null)
        {
            _tames = TamesReader.Read(player);
            _stations = player.m_knownStations;
        }
    }

    private void Count()
    {
        foreach (var e in Catalog.Entries)
        {
            var known = IsKnown(e);
            if (!e.HiddenUntilKnown || known)
            {
                ListedCount++;
            }
            if (!known)
            {
                continue;
            }
            DiscoveredCount++;
            switch (e.Kind)
            {
                case EntryKind.Item:
                    DiscoveredItems++;
                    break;
                case EntryKind.Piece:
                    DiscoveredPieces++;
                    break;
                default:
                    DiscoveredCreatures++;
                    break;
            }
        }
    }

    /// <summary>One Debug line (design 3.15).</summary>
    internal string Summary() =>
        $"Knowledge: {DiscoveredCount} of {Catalog.Entries.Count} entries discovered ({DiscoveredItems}/{DiscoveredPieces}/"
        + $"{DiscoveredCreatures}), {MetCount} creatures met, biomes {KnownBiomes}{(RevealAll ? ", reveal all" : "")}.";

    // ---------------------------------------------------------------- biomes

    // Own record, plus vanilla names: token, current-language name, single alternate-biome names (raw and localized).
    // Vanilla stores localized names (language dependent), so a biome found in another language before install stays
    // unknown until visited again (decision 17).
    private static Heightmap.Biome ComputeBiomes(Player player)
    {
        if (player == null)
        {
            return Heightmap.Biome.None;
        }
        var mask = OwnRecords.GetBiomes(player) & Biomes.AllKnownFlags;
        var vanilla = player.m_knownBiome;
        if (vanilla == null || vanilla.Count == 0)
        {
            return mask;
        }
        foreach (var b in Biomes.Progression)
        {
            if ((mask & b) != 0)
            {
                continue;
            }
            try
            {
                if (VanillaKnows(vanilla, b))
                {
                    mask |= b;
                }
            }
            catch (Exception e)
            {
                PatchGuard.Report("Knowledge.VanillaBiomes", e);
            }
        }
        return mask;
    }

    private static bool VanillaKnows(HashSet<string> vanilla, Heightmap.Biome biome)
    {
        var token = Biomes.Token(biome);
        if (vanilla.Contains(token) || vanilla.Contains(Names.Localize(token)))
        {
            return true;
        }
        foreach (var alt in AltBiomeList.m_altBiomes)
        {
            if (alt == null || (alt.m_biome & biome) == 0)
            {
                continue;
            }
            var text = token;
            if (!string.IsNullOrEmpty(alt.m_nameOverride))
            {
                text = alt.m_nameOverride;
            }
            if (!string.IsNullOrEmpty(alt.m_namePrefix))
            {
                text = alt.m_namePrefix + " " + text;
            }
            if (!string.IsNullOrEmpty(alt.m_nameSuffix))
            {
                text = text + " " + alt.m_nameSuffix;
            }
            if (vanilla.Contains(text) || vanilla.Contains(Names.Localize(text)))
            {
                return true;
            }
        }
        return false;
    }
}
