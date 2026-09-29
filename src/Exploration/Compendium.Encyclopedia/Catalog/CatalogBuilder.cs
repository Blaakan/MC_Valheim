using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.CompendiumEncyclopediaMod;

/// <summary>Me = time budget of one frame of the build (4 ms). Stages ask <see cref="Over"/> and yield.</summary>
internal sealed class BuildBudget
{
    private readonly Stopwatch _frame = new Stopwatch();
    private readonly long _limitTicks;

    internal BuildBudget(float milliseconds)
    {
        _limitTicks = (long)(Stopwatch.Frequency * (milliseconds / 1000.0));
    }

    internal void StartFrame() => _frame.Restart();

    internal bool Over => _frame.ElapsedTicks > _limitTicks;
}

/// <summary>One named build stage. Critical stage failing = whole build abandoned (never half published).</summary>
internal sealed class BuildStage
{
    internal string Name;
    internal Func<IEnumerator> Run;
    internal bool Critical;
}

/// <summary>
/// Me fill one new <see cref="Catalog"/> from game data, stage by stage, yielding when frame budget spent
/// (<see cref="CatalogService"/> drive me on plugin object). Rules (design 3.2.4), no name lists:
/// items: no ItemDrop, no icon, None/Customization type, DLC missing, no translation = never listed; hidden until known
/// = creature equipment / development items / seasonal-only, when no other source. Pieces: repair/remove, no name/icon,
/// DLC missing, disabled non-seasonal, dishes (ItemDrop) = never listed; seasonal = hidden until known. Creatures: no
/// Character, Player, player/summon/dummy faction, no name, prefab name ending "Test" = never listed.
/// Me only read game data. Sources, habitats: see SourceScan (other half of me).
/// </summary>
internal sealed partial class CatalogBuilder
{
    private readonly ObjectDB _db;
    private readonly ZNetScene _scene;
    private readonly Catalog _c = new Catalog();
    private readonly BuildBudget _b;

    // Working state (thrown away with builder).
    private readonly HashSet<string> _missingTokens = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _recipePrefabs = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _equipment = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<Recipe> _seasonalRecipes = new HashSet<Recipe>();
    private readonly HashSet<GameObject> _seasonalPieces = new HashSet<GameObject>();
    private readonly List<KeyValuePair<Entry, PieceTable>> _toolTables = new List<KeyValuePair<Entry, PieceTable>>();
    private readonly Dictionary<string, List<Recipe>> _recipesByStation = new Dictionary<string, List<Recipe>>(StringComparer.Ordinal);
    private readonly HashSet<long> _useKeys = new HashSet<long>();
    private readonly Dictionary<string, SubGroup> _groups = new Dictionary<string, SubGroup>(StringComparer.Ordinal);
    private readonly List<string> _debug = new List<string>();

    internal CatalogBuilder(ObjectDB db, ZNetScene scene, BuildBudget budget)
    {
        _db = db;
        _scene = scene;
        _b = budget;
    }

    internal Catalog Result => _c;

    /// <summary>Stages in run order.</summary>
    internal List<BuildStage> Stages()
    {
        return new List<BuildStage>
        {
            new BuildStage { Name = "items", Run = StageItems },
            new BuildStage { Name = "recipes", Run = StageRecipes },
            new BuildStage { Name = "representatives", Run = StageRepresentatives },
            new BuildStage { Name = "pieces", Run = StagePieces },
            new BuildStage { Name = "creatures", Run = StageCreatures },
            new BuildStage { Name = "world sources", Run = StageWorldSources },
            new BuildStage { Name = "vegetation", Run = StageVegetation },
            new BuildStage { Name = "spawn lists", Run = StageSpawns },
            new BuildStage { Name = "habitats", Run = StageHabitats },
            new BuildStage { Name = "finalize", Run = StageFinalize, Critical = true },
        };
    }

    /// <summary>Debug lines gathered while building (excluded prefabs, hidden items, no source, creature groups).</summary>
    internal List<string> DebugLines => _debug;

    // ---------------------------------------------------------------- 1. items

    private IEnumerator StageItems()
    {
        var items = _db.m_items != null ? _db.m_items.ToArray() : Array.Empty<GameObject>();
        for (var i = 0; i < items.Length; i++)
        {
            try
            {
                AddItemPrefab(items[i], i);
            }
            catch (Exception e)
            {
                ScanError(items[i], e);
            }
            if (_b.Over)
            {
                yield return null;
            }
        }
    }

    private void AddItemPrefab(GameObject go, int order)
    {
        if (go == null)
        {
            _c.ExcludedItemPrefabs++;
            return;
        }
        if (!go.TryGetComponent<ItemDrop>(out var drop) || drop.m_itemData == null || drop.m_itemData.m_shared == null)
        {
            Exclude(go, "no ItemDrop");
            return;
        }
        var s = drop.m_itemData.m_shared;
        if (s.m_icons == null || s.m_icons.Length == 0 || s.m_icons[0] == null)
        {
            Exclude(go, "no icon");
            return;
        }
        if (s.m_itemType == ItemDrop.ItemData.ItemType.None || s.m_itemType == ItemDrop.ItemData.ItemType.Customization)
        {
            Exclude(go, $"type {s.m_itemType}");
            return;
        }
        if (!DlcOk(s.m_dlc))
        {
            Exclude(go, "DLC not installed");
            return;
        }
        var token = s.m_name;
        if (string.IsNullOrEmpty(token) || _missingTokens.Contains(token))
        {
            Exclude(go, "no name");
            return;
        }
        if (!_c.ItemsByToken.TryGetValue(token, out var entry))
        {
            if (Names.IsMissing(Names.Localize(token)))
            {
                _missingTokens.Add(token);
                Exclude(go, $"name {token} has no translation");
                return;
            }
            entry = NewEntry(EntryKind.Item, token, order);
            entry.Item = new ItemInfo();
            _c.ItemsByToken[token] = entry;
        }
        entry.Item.Prefabs.Add(go);
        if (!_c.ItemsByPrefab.ContainsKey(go.name))
        {
            _c.ItemsByPrefab[go.name] = entry;
        }
        var table = s.m_buildPieces;
        if (table != null && !_toolTables.Any(t => ReferenceEquals(t.Value, table)))
        {
            _toolTables.Add(new KeyValuePair<Entry, PieceTable>(entry, table));
        }
    }

    private void Exclude(GameObject go, string rule)
    {
        _c.ExcludedItemPrefabs++;
        _debug.Add($"Excluded item prefab {go.name}: {rule}.");
    }

    // ---------------------------------------------------------------- 2. recipes

    private IEnumerator StageRecipes()
    {
        CollectSeasonal();
        var recipes = _db.m_recipes != null ? _db.m_recipes.ToArray() : Array.Empty<Recipe>();
        for (var i = 0; i < recipes.Length; i++)
        {
            try
            {
                AddRecipe(recipes[i]);
            }
            catch (Exception e)
            {
                ScanError(recipes[i] != null && recipes[i].m_item != null ? recipes[i].m_item.gameObject : null, e);
            }
            if (_b.Over)
            {
                yield return null;
            }
        }
    }

    // Own stage after recipes: even if the recipe stage broke, every item still get a representative.
    private IEnumerator StageRepresentatives()
    {
        foreach (var e in _c.ItemsByToken.Values)
        {
            try
            {
                ChooseRepresentative(e);
            }
            catch (Exception ex)
            {
                ScanError(e.Item.Prefabs.Count > 0 ? e.Item.Prefabs[0] : null, ex);
            }
            if (_b.Over)
            {
                yield return null;
            }
        }
    }

    // Representative per item: prefab with recipe, else first in ObjectDB order. Kind and icon from it.
    private void ChooseRepresentative(Entry e)
    {
        var info = e.Item;
        var rep = info.Prefabs[0];
        foreach (var p in info.Prefabs)
        {
            if (_recipePrefabs.Contains(p.name))
            {
                rep = p;
                break;
            }
        }
        e.Prefab = rep;
        e.PrefabName = rep.name;
        info.Drop = rep.GetComponent<ItemDrop>();
        var shared = info.Drop.m_itemData.m_shared;
        e.Icon = shared.m_icons[0];
        info.Kind = ItemKinds.Classify(shared);
    }

    // Seasonal groups live on the Player prefab (private list, publicized). Local player first, else ZNetScene prefab.
    private void CollectSeasonal()
    {
        List<SeasonalItemGroup> groups = null;
        var player = Player.m_localPlayer;
        if (player != null)
        {
            groups = player.m_seasonalItemGroups;
        }
        if (groups == null && _scene != null)
        {
            var prefab = _scene.GetPrefab("Player");
            if (prefab != null && prefab.TryGetComponent<Player>(out var p))
            {
                groups = p.m_seasonalItemGroups;
            }
        }
        if (groups == null)
        {
            return;
        }
        foreach (var g in groups)
        {
            if (g == null)
            {
                continue;
            }
            if (g.Recipes != null)
            {
                foreach (var r in g.Recipes)
                {
                    if (r != null)
                    {
                        _seasonalRecipes.Add(r);
                    }
                }
            }
            if (g.Pieces != null)
            {
                foreach (var go in g.Pieces)
                {
                    if (go != null)
                    {
                        _seasonalPieces.Add(go);
                    }
                }
            }
        }
    }

    private void AddRecipe(Recipe recipe)
    {
        if (recipe == null || recipe.m_item == null)
        {
            return;
        }
        var seasonal = !recipe.m_enabled && _seasonalRecipes.Contains(recipe);
        if (!recipe.m_enabled && !seasonal)
        {
            return;
        }
        var output = _c.ItemOf(recipe.m_item);
        if (output == null)
        {
            return;
        }
        output.Item.Recipes.Add(recipe);
        if (recipe.m_noCraftOnlyUpgrade)
        {
            _c.UpgradeOnlyRecipes++;
        }
        _recipePrefabs.Add(recipe.m_item.gameObject.name);
        if (seasonal)
        {
            _c.SeasonalRecipes.Add(recipe);
        }
        if (recipe.m_resources != null)
        {
            foreach (var req in recipe.m_resources)
            {
                if (req == null || req.m_resItem == null || (req.m_amount <= 0 && req.m_amountPerLevel <= 0))
                {
                    continue;
                }
                var ingredient = _c.ItemOf(req.m_resItem);
                if (ingredient != null && ingredient != output)
                {
                    AddUse(ingredient, UseKind.Recipe, output, null);
                }
            }
        }
        if (recipe.m_craftingStation != null && !string.IsNullOrEmpty(recipe.m_craftingStation.m_name))
        {
            var name = recipe.m_craftingStation.m_name;
            if (!_recipesByStation.TryGetValue(name, out var list))
            {
                list = new List<Recipe>();
                _recipesByStation[name] = list;
            }
            list.Add(recipe);
        }
    }

    // One use line per (item, kind, target, station).
    private void AddUse(Entry item, UseKind kind, Entry target, Entry station)
    {
        if (item == null || target == null || item.Item == null)
        {
            return;
        }
        var key = ((long)item.Index << 40) ^ ((long)target.Index << 18) ^ ((long)(station != null ? station.Index + 1 : 0) << 2)
                  ^ (long)kind;
        if (_useKeys.Add(key))
        {
            item.Item.UsedIn.Add(new UseLink { Kind = kind, Target = target, Station = station });
        }
    }

    // ---------------------------------------------------------------- 3. pieces

    private IEnumerator StagePieces()
    {
        for (var t = 0; t < _toolTables.Count; t++)
        {
            var tool = _toolTables[t].Key;
            var table = _toolTables[t].Value;
            if (table == null || table.m_pieces == null)
            {
                continue;
            }
            _c.PieceTables.Add(table);
            var prefabs = table.m_pieces.ToArray();
            _c.PieceSumSeen += prefabs.Length;
            for (var j = 0; j < prefabs.Length; j++)
            {
                try
                {
                    AddPiece(prefabs[j], tool, table, t, j);
                }
                catch (Exception e)
                {
                    ScanError(prefabs[j], e);
                }
                if (_b.Over)
                {
                    yield return null;
                }
            }
        }

        // Upgrades and recipes per station, now that every station is known.
        foreach (var e in _c.PiecesByName.Values)
        {
            var info = e.Piece;
            if (info.ExtensionOfStation != null && _c.StationsByName.TryGetValue(info.ExtensionOfStation, out var station))
            {
                info.ExtensionOf = station;
                if (!station.Piece.Extensions.Contains(e))
                {
                    station.Piece.Extensions.Add(e);
                }
            }
        }
        foreach (var kv in _recipesByStation)
        {
            if (_c.StationsByName.TryGetValue(kv.Key, out var station))
            {
                station.Piece.RecipesHere.AddRange(kv.Value);
            }
        }
    }

    private void AddPiece(GameObject go, Entry tool, PieceTable table, int toolOrder, int index)
    {
        if (go == null)
        {
            return;
        }
        if (!go.TryGetComponent<Piece>(out var piece))
        {
            _debug.Add($"Excluded piece prefab {go.name}: no Piece.");
            return;
        }
        if (piece.m_repairPiece || piece.m_removePiece)
        {
            return;
        }
        if (go.TryGetComponent<ItemDrop>(out _))
        {
            // Dish placed like a piece (Serving tray, feasts): its item entry say where it is placed.
            var dish = _c.ItemOf(go);
            if (dish != null && dish.Item.PlaceableWith == null)
            {
                dish.Item.PlaceableWith = tool;
            }
            _debug.Add($"Piece prefab {go.name} is a placeable item (dish): shown on its item entry.");
            return;
        }
        var name = piece.m_name;
        if (string.IsNullOrEmpty(name))
        {
            _debug.Add($"Excluded piece prefab {go.name}: no name.");
            return;
        }
        if (_c.PiecesByName.TryGetValue(name, out var existing))
        {
            // First table win; other prefab of same name still point to it.
            if (!_c.PiecesByPrefab.ContainsKey(go.name))
            {
                _c.PiecesByPrefab[go.name] = existing;
            }
            return;
        }
        if (_missingTokens.Contains(name) || Names.IsMissing(Names.Localize(name)))
        {
            _missingTokens.Add(name);
            _debug.Add($"Excluded piece prefab {go.name}: name {name} has no translation.");
            return;
        }
        if (piece.m_icon == null)
        {
            _debug.Add($"Excluded piece prefab {go.name}: no icon.");
            return;
        }
        if (!DlcOk(piece.m_dlc))
        {
            _debug.Add($"Excluded piece prefab {go.name}: DLC not installed.");
            return;
        }
        var seasonal = !piece.m_enabled && _seasonalPieces.Contains(go);
        if (!piece.m_enabled && !seasonal)
        {
            _debug.Add($"Excluded piece prefab {go.name}: disabled (old piece).");
            return;
        }

        var catIndex = table.m_categories != null ? table.m_categories.IndexOf(piece.m_category) : -1;
        var label = catIndex >= 0 && table.m_categoryLabels != null && catIndex < table.m_categoryLabels.Count
            ? table.m_categoryLabels[catIndex] ?? ""
            : "";

        var entry = NewEntry(EntryKind.Piece, name, toolOrder * 10000 + index);
        entry.Prefab = go;
        entry.PrefabName = go.name;
        entry.Icon = piece.m_icon;
        entry.Seasonal = seasonal;
        if (seasonal)
        {
            entry.HiddenUntilKnown = true;
            entry.HiddenRule = "seasonal piece";
            _debug.Add($"Hidden until known: piece {go.name} ({name}): seasonal piece.");
        }
        entry.Piece = new PieceInfo { Piece = piece, Tool = tool, CategoryLabel = label };
        _c.PiecesByName[name] = entry;
        _c.PiecesByPrefab[go.name] = entry;

        var group = Group(CatalogTab.Building, toolOrder * 1000 + (catIndex >= 0 ? catIndex : 999),
            $"B{toolOrder}|{label}", label, tool);
        Place(entry, group);

        if (go.TryGetComponent<CraftingStation>(out var station) && !string.IsNullOrEmpty(station.m_name))
        {
            entry.Piece.StationName = station.m_name;
            if (!_c.StationsByName.ContainsKey(station.m_name))
            {
                _c.StationsByName[station.m_name] = entry;
            }
        }
        if (go.TryGetComponent<StationExtension>(out var ext) && ext.m_craftingStation != null)
        {
            entry.Piece.ExtensionOfStation = ext.m_craftingStation.m_name;
        }
        if (piece.m_resources != null)
        {
            foreach (var req in piece.m_resources)
            {
                if (req != null && req.m_resItem != null && req.m_amount > 0)
                {
                    AddUse(_c.ItemOf(req.m_resItem), UseKind.Piece, entry, null);
                }
            }
        }
    }

    // ---------------------------------------------------------------- 4. creatures

    private readonly List<KeyValuePair<Entry, string>> _offspring = new List<KeyValuePair<Entry, string>>();
    private readonly List<KeyValuePair<Entry, string>> _grown = new List<KeyValuePair<Entry, string>>();
    private readonly Dictionary<string, Entry> _bossByKey = new Dictionary<string, Entry>(StringComparer.Ordinal);

    private IEnumerator StageCreatures()
    {
        var prefabs = PrefabsCopy();
        for (var i = 0; i < prefabs.Length; i++)
        {
            try
            {
                AddCreaturePrefab(prefabs[i], i);
            }
            catch (Exception e)
            {
                ScanError(prefabs[i], e);
            }
            if (_b.Over)
            {
                yield return null;
            }
        }
        foreach (var e in _c.CreaturesByName.Values.ToArray())
        {
            try
            {
                FinishCreature(e);
            }
            catch (Exception ex)
            {
                ScanError(e.Prefab, ex);
            }
            if (_b.Over)
            {
                yield return null;
            }
        }
    }

    private void AddCreaturePrefab(GameObject go, int order)
    {
        if (go == null || !go.TryGetComponent<Character>(out var c))
        {
            return;
        }
        var isPlayer = go.TryGetComponent<Player>(out _);
        if (go.TryGetComponent<Humanoid>(out var humanoid))
        {
            CollectEquipment(humanoid, isPlayer);
        }
        if (isPlayer)
        {
            return;
        }
        if (c.m_faction == Character.Faction.Players || c.m_faction == Character.Faction.PlayerSpawned
            || c.m_faction == Character.Faction.TrainingDummy)
        {
            _debug.Add($"Excluded creature prefab {go.name}: faction {c.m_faction}.");
            return;
        }
        var name = c.m_name;
        if (string.IsNullOrEmpty(name) || _missingTokens.Contains(name))
        {
            _debug.Add($"Excluded creature prefab {go.name}: no name.");
            return;
        }
        if (go.name.EndsWith("Test", StringComparison.Ordinal))
        {
            _debug.Add($"Excluded creature prefab {go.name}: test prefab (name ends with Test).");
            return;
        }
        if (!_c.CreaturesByName.TryGetValue(name, out var entry))
        {
            if (Names.IsMissing(Names.Localize(name)))
            {
                _missingTokens.Add(name);
                _debug.Add($"Excluded creature prefab {go.name}: name {name} has no translation.");
                return;
            }
            entry = NewEntry(EntryKind.Creature, name, order);
            entry.Creature = new CreatureInfo();
            _c.CreaturesByName[name] = entry;
        }
        entry.Creature.Prefabs.Add(go);
        _c.CreaturesByPrefab[go.name] = entry;
    }

    // Items only creatures carry (attack items, creature armour) and every unarmed weapon (PlayerUnarmed too).
    private void CollectEquipment(Humanoid h, bool isPlayer)
    {
        if (h.m_unarmedWeapon != null)
        {
            _equipment.Add(h.m_unarmedWeapon.gameObject.name);
        }
        if (isPlayer)
        {
            return;
        }
        AddAll(h.m_defaultItems);
        AddAll(h.m_randomWeapon);
        AddAll(h.m_randomArmor);
        AddAll(h.m_randomShield);
        if (h.m_randomSets != null)
        {
            foreach (var set in h.m_randomSets)
            {
                if (set != null)
                {
                    AddAll(set.m_items);
                }
            }
        }
        if (h.m_randomItems != null)
        {
            foreach (var item in h.m_randomItems)
            {
                if (item != null && item.m_prefab != null)
                {
                    _equipment.Add(item.m_prefab.name);
                }
            }
        }
    }

    private void AddAll(GameObject[] items)
    {
        if (items == null)
        {
            return;
        }
        foreach (var go in items)
        {
            if (go != null)
            {
                _equipment.Add(go.name);
            }
        }
    }

    private void FinishCreature(Entry e)
    {
        var info = e.Creature;
        // Representative = shortest prefab name, then ordinal (Skeleton before Skeleton_NoArcher).
        var rep = info.Prefabs[0];
        foreach (var p in info.Prefabs)
        {
            if (p.name.Length < rep.name.Length
                || (p.name.Length == rep.name.Length && string.CompareOrdinal(p.name, rep.name) < 0))
            {
                rep = p;
            }
        }
        e.Prefab = rep;
        e.PrefabName = rep.name;
        info.Character = rep.GetComponent<Character>();

        var ordered = new List<GameObject> { rep };
        foreach (var p in info.Prefabs)
        {
            if (!ReferenceEquals(p, rep))
            {
                ordered.Add(p);
            }
        }

        foreach (var go in ordered)
        {
            var c = go.GetComponent<Character>();
            if (c.m_boss)
            {
                info.Boss = true;
            }
            if (string.IsNullOrEmpty(info.DefeatKey) && !string.IsNullOrEmpty(c.m_defeatSetGlobalKey))
            {
                info.DefeatKey = c.m_defeatSetGlobalKey;
                if (!_bossByKey.ContainsKey(info.DefeatKey))
                {
                    _bossByKey[info.DefeatKey] = e;
                }
            }
            if (go.TryGetComponent<CharacterDrop>(out var cd) && cd.m_drops != null)
            {
                foreach (var d in cd.m_drops)
                {
                    AddDrop(e, d);
                }
            }
            if (go.TryGetComponent<Tameable>(out var tame))
            {
                info.Tameable = true;
                if (info.Saddle == null && tame.m_saddleItem != null)
                {
                    info.Saddle = _c.ItemOf(tame.m_saddleItem);
                }
                if (go.TryGetComponent<MonsterAI>(out var ai) && ai.m_consumeItems != null)
                {
                    foreach (var food in ai.m_consumeItems)
                    {
                        var item = _c.ItemOf(food);
                        if (item != null && !info.Eats.Contains(item))
                        {
                            info.Eats.Add(item);
                            if (!item.Item.Tames.Contains(e))
                            {
                                item.Item.Tames.Add(e);
                            }
                        }
                    }
                }
            }
            if (go.TryGetComponent<Procreation>(out var proc))
            {
                if (proc.m_offspring != null)
                {
                    _offspring.Add(new KeyValuePair<Entry, string>(e, proc.m_offspring.name));
                }
                // Offspring can be an item (Procreation.Procreate: no Character = ItemDrop, hen = egg): a source.
                AddLaid(e, proc.m_offspring);
                AddLaid(e, proc.m_noPartnerOffspring);
            }
            if (go.TryGetComponent<Growup>(out var grow))
            {
                if (grow.m_grownPrefab != null)
                {
                    _grown.Add(new KeyValuePair<Entry, string>(e, grow.m_grownPrefab.name));
                }
                if (grow.m_altGrownPrefabs != null)
                {
                    foreach (var alt in grow.m_altGrownPrefabs)
                    {
                        if (alt != null && alt.m_prefab != null)
                        {
                            _grown.Add(new KeyValuePair<Entry, string>(e, alt.m_prefab.name));
                        }
                    }
                }
            }
        }
        foreach (var link in info.Drops)
        {
            if (link.Item.Item.Kind == ItemKind.Trophy)
            {
                info.Trophy = link.Item;
                break;
            }
        }
    }

    // Item born from a creature (not a creature prefab): "Laid by a tamed <creature>".
    private void AddLaid(Entry creature, GameObject offspring)
    {
        if (offspring == null || offspring.TryGetComponent<Character>(out _))
        {
            return;
        }
        var item = _c.ItemOf(offspring);
        if (item != null && !item.Item.LaidBy.Contains(creature))
        {
            item.Item.LaidBy.Add(creature);
        }
    }

    // Union over the group's prefabs, one line per item (representative's numbers first).
    private void AddDrop(Entry creature, CharacterDrop.Drop d)
    {
        if (d == null || d.m_prefab == null)
        {
            return;
        }
        var item = _c.ItemOf(d.m_prefab);
        if (item == null)
        {
            return;
        }
        foreach (var existing in creature.Creature.Drops)
        {
            if (existing.Item == item)
            {
                return;
            }
        }
        var link = new DropLink { Creature = creature, Item = item, Drop = d };
        creature.Creature.Drops.Add(link);
        item.Item.DroppedBy.Add(link);
    }

    // ---------------------------------------------------------------- 9. finalize

    private IEnumerator StageFinalize()
    {
        foreach (var e in _c.ItemsByToken.Values)
        {
            try
            {
                FinishItem(e);
            }
            catch (Exception ex)
            {
                // Broken modded item: still put in a group (every entry need one), log it.
                ScanError(e.Prefab, ex);
                if (e.SubGroup == null)
                {
                    Place(e, Group(CatalogTab.Tools, 4, $"I{(int)CatalogTab.Tools}|4", Labels.GroupOther, null));
                }
            }
            if (_b.Over)
            {
                yield return null;
            }
        }
        foreach (var e in _c.CreaturesByName.Values)
        {
            var first = Biomes.First(e.Creature.Habitat);
            var rank = first == Heightmap.Biome.None ? Biomes.Progression.Length : Biomes.Rank(first);
            var group = Group(CatalogTab.Creatures, rank, $"C{rank}", "", null);
            group.IsBiomeGroup = true;
            group.Biome = first;
            Place(e, group);
            _debug.Add($"Creature {e.Key}: prefabs [{string.Join(", ", e.Creature.Prefabs.Select(p => p.name))}], "
                       + $"representative {e.PrefabName}, trophy {(e.Creature.Trophy != null ? e.Creature.Trophy.PrefabName : "none")}, "
                       + $"habitat {e.Creature.Habitat} ({(e.Creature.HabitatSource.Length > 0 ? e.Creature.HabitatSource : "none")})"
                       + (e.Creature.AfterBosses.Count > 0 ? $", after {string.Join(", ", e.Creature.AfterBosses.Select(b => b.Key))}" : "")
                       + (e.Creature.Boss ? ", boss" : "") + (e.Creature.Tameable ? ", tameable" : "") + ".");
        }

        // Display order: groups by Order, entries by game order.
        for (var t = 0; t < Tabs.Count; t++)
        {
            var groups = _c.GroupsMutable((CatalogTab)t);
            groups.Sort((a, b) => a.Order.CompareTo(b.Order));
            foreach (var g in groups)
            {
                g.Entries.Sort((a, b) => a.GameOrder.CompareTo(b.GameOrder));
            }
        }

        // Names once for this language (Localize cache only 100 long).
        foreach (var e in _c.Entries)
        {
            _ = e.DisplayName;
            if (_b.Over)
            {
                yield return null;
            }
        }

        foreach (var e in _c.Entries)
        {
            switch (e.Kind)
            {
                case EntryKind.Item:
                    _c.ItemCount++;
                    break;
                case EntryKind.Piece:
                    _c.PieceCount++;
                    break;
                default:
                    _c.CreatureCount++;
                    break;
            }
            if (e.HiddenUntilKnown)
            {
                _c.HiddenCount++;
            }
        }
    }

    private void FinishItem(Entry e)
    {
        var info = e.Item;
        info.HasNonRecipeSource = info.DroppedBy.Count > 0 || info.Gathered.Count > 0 || info.InChests
                                  || info.MadeFrom.Count > 0 || info.ProducedBy.Count > 0 || info.ProducedUnlisted
                                  || info.LaidBy.Count > 0 || info.Caught || info.SoldByTraders > 0;
        // Upgrade-only recipe (m_noCraftOnlyUpgrade): vanilla Craft tab never offer it (InventoryGui.UpdateRecipeList),
        // so it make nothing new: not a source.
        info.Craftable = info.Recipes.Any(r => !r.m_noCraftOnlyUpgrade);
        info.HasSource = info.Craftable || info.HasNonRecipeSource;

        var allSeasonal = info.Recipes.Count > 0 && info.Recipes.All(r => _c.SeasonalRecipes.Contains(r));
        e.Seasonal = allSeasonal;
        if (!info.HasSource)
        {
            if (info.Prefabs.All(p => _equipment.Contains(p.name)))
            {
                Hide(e, "creature equipment");
            }
            else if (info.Prefabs.All(p => p.name.EndsWith("Test", StringComparison.Ordinal)
                                           || p.name.EndsWith("Cheat", StringComparison.Ordinal)))
            {
                Hide(e, "development item");
            }
        }
        else if (allSeasonal && !info.HasNonRecipeSource)
        {
            Hide(e, "seasonal item");
        }
        if (!info.HasSource && !e.HiddenUntilKnown)
        {
            _c.NoSourceCount++;
            _debug.Add($"No source found: item {e.PrefabName} ({e.Key}), type {info.Drop.m_itemData.m_shared.m_itemType}.");
        }

        ItemGroup(info, out var tab, out var order, out var label);
        Place(e, Group(tab, order, $"I{(int)tab}|{order}", label, null));
    }

    private void Hide(Entry e, string rule)
    {
        e.HiddenUntilKnown = true;
        e.HiddenRule = rule;
        _debug.Add($"Hidden until known: item {e.PrefabName} ({e.Key}): {rule}.");
    }

    // Tab and sub-group of an item kind (design 3.2.6). Order = rank inside the tab.
    private static void ItemGroup(ItemInfo info, out CatalogTab tab, out int order, out string label)
    {
        var shared = info.Drop.m_itemData.m_shared;
        switch (info.Kind)
        {
            case ItemKind.Weapon:
                tab = CatalogTab.Weapons;
                order = WeaponFamily(shared.m_skillType);
                label = order < 11 ? "$skill_" + shared.m_skillType.ToString().ToLowerInvariant() : Labels.GroupOtherWeapons;
                return;
            case ItemKind.Ammo:
                tab = CatalogTab.Weapons;
                order = 20;
                label = Labels.GroupAmmo;
                return;
            case ItemKind.Shield:
                tab = CatalogTab.Weapons;
                order = 21;
                label = Labels.GroupShields;
                return;
            case ItemKind.Helmet:
                tab = CatalogTab.Armor;
                order = 0;
                label = Labels.GroupHelmets;
                return;
            case ItemKind.Chest:
                tab = CatalogTab.Armor;
                order = 1;
                label = Labels.GroupChest;
                return;
            case ItemKind.Legs:
                tab = CatalogTab.Armor;
                order = 2;
                label = Labels.GroupLegs;
                return;
            case ItemKind.Hands:
                tab = CatalogTab.Armor;
                order = 3;
                label = Labels.GroupHands;
                return;
            case ItemKind.Cape:
                tab = CatalogTab.Armor;
                order = 4;
                label = Labels.GroupCapes;
                return;
            case ItemKind.Utility:
                tab = CatalogTab.Armor;
                order = 5;
                label = Labels.GroupUtility;
                return;
            case ItemKind.Trinket:
                tab = CatalogTab.Armor;
                order = 6;
                label = Labels.GroupTrinkets;
                return;
            case ItemKind.Tool:
                tab = CatalogTab.Tools;
                order = 0;
                label = Labels.GroupBuildTools;
                return;
            case ItemKind.SkillTool:
                tab = CatalogTab.Tools;
                order = 1;
                label = Labels.GroupGatherTools;
                return;
            case ItemKind.Torch:
                tab = CatalogTab.Tools;
                order = 2;
                label = Labels.GroupTorches;
                return;
            case ItemKind.Misc:
                tab = CatalogTab.Tools;
                order = 3;
                label = Labels.GroupMisc;
                return;
            case ItemKind.Food:
                tab = CatalogTab.Food;
                order = 0;
                label = Labels.GroupFood;
                return;
            case ItemKind.Potion:
                tab = CatalogTab.Food;
                order = 1;
                label = Labels.GroupPotions;
                return;
            case ItemKind.Material:
                tab = CatalogTab.Materials;
                order = 0;
                label = Labels.GroupMaterials;
                return;
            case ItemKind.Fish:
                tab = CatalogTab.Materials;
                order = 1;
                label = Labels.GroupFish;
                return;
            case ItemKind.Trophy:
                tab = CatalogTab.Trophies;
                order = 0;
                label = "";
                return;
            default:
                tab = CatalogTab.Tools;
                order = 4;
                label = Labels.GroupOther;
                return;
        }
    }

    // Same family order as Sort Chest TypeGroups: melee, ranged, magic, unknown (11) last.
    private static int WeaponFamily(Skills.SkillType skill)
    {
        switch (skill)
        {
            case Skills.SkillType.Swords:
                return 0;
            case Skills.SkillType.Axes:
                return 1;
            case Skills.SkillType.Clubs:
                return 2;
            case Skills.SkillType.Knives:
                return 3;
            case Skills.SkillType.Spears:
                return 4;
            case Skills.SkillType.Polearms:
                return 5;
            case Skills.SkillType.Unarmed:
                return 6;
            case Skills.SkillType.Bows:
                return 7;
            case Skills.SkillType.Crossbows:
                return 8;
            case Skills.SkillType.ElementalMagic:
                return 9;
            case Skills.SkillType.BloodMagic:
                return 10;
            default:
                return 11;
        }
    }

    // ---------------------------------------------------------------- helpers

    private Entry NewEntry(EntryKind kind, string key, int gameOrder)
    {
        var e = new Entry
        {
            Index = _c.Entries.Count,
            Kind = kind,
            Key = key,
            NameToken = key,
            GameOrder = gameOrder,
            Tab = kind == EntryKind.Piece ? CatalogTab.Building : kind == EntryKind.Creature ? CatalogTab.Creatures : CatalogTab.Materials,
        };
        _c.Entries.Add(e);
        return e;
    }

    private SubGroup Group(CatalogTab tab, int order, string key, string label, Entry tool)
    {
        if (_groups.TryGetValue(key, out var g))
        {
            return g;
        }
        g = new SubGroup { Tab = tab, Order = order, LabelToken = label ?? "", Tool = tool };
        _groups[key] = g;
        _c.GroupsMutable(tab).Add(g);
        return g;
    }

    private static void Place(Entry e, SubGroup g)
    {
        e.Tab = g.Tab;
        e.SubGroup = g;
        g.Entries.Add(e);
    }

    private static bool DlcOk(string dlc)
    {
        if (string.IsNullOrEmpty(dlc))
        {
            return true;
        }
        var man = DLCMan.instance;
        return man == null || man.IsDLCInstalled(dlc);
    }

    private GameObject[] PrefabsCopy() =>
        _scene != null && _scene.m_prefabs != null ? _scene.m_prefabs.ToArray() : Array.Empty<GameObject>();

    private int _scanErrors;

    // Broken modded prefab: skip it, keep going. First 5 logged.
    private void ScanError(GameObject go, Exception e)
    {
        _scanErrors++;
        if (_scanErrors <= 5)
        {
            Log.Debug($"Encyclopedia catalog: skipped {(go != null ? go.name : "?")}: {e.Message}");
        }
    }
}
