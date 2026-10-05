using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MC.Shared;

namespace MC.Farming.CultivatorReplantMod;

// Me = cultivator tiers (design 2.3). Tier = upgrade level of the vanilla Cultivator: 1-3 bronze (vanilla), 4 black
// metal, 5 eitr, 6 flametal, 7 bloodgold, all at the Forge (station level = quality, vanilla formula, Forge reach 7).
// Me write two vanilla things and put them back:
// - Cultivator prefab m_maxQuality: 7 (lower when a tier cost has no valid material).
// - Recipe_Cultivator m_resources: NEW array = vanilla rows + one row per tier material (m_amount 0,
//   m_amountPerLevel 1 so Compendium list the Cultivator as a use, never recovered).
// Amount per quality come from the Piece.Requirement.GetAmount postfix (TryAmount), keyed by requirement OBJECT:
// tier row = its amount at its own quality, 0 else; vanilla Bronze / Corewood rows = 0 from quality 4; idol row
// (Forge of Potential only) = 0 from quality 2, so no page or mod offer an idol upgrade of the Cultivator (Forge of
// Potential hide it anyway: HideAtUpgrader, D3). Level 1 = vanilla for every row.
// Recipe and prefab objects live the whole game session (ObjectDB Awake / CopyOtherDB run many times on same assets):
// me snapshot vanilla ONCE per object, never our own array. Feature off or rules pending = vanilla back (else vanilla
// GetAmount(4) sell a level 4 for 4 Bronze + 4 Corewood). Other mod put new array in recipe while tiers on (recipe
// config mod, server sync) = Resync: its rows the new base, tier rows on top again. Items made before a change keep
// their own SharedData copy (stale m_maxQuality): HealInventory fix the player's ones, from an ALWAYS ON
// UpdateRecipeList prefix (InventoryGuiHealPatches), so a copy from chest or ground heal both ways, feature on or off.
internal static class CultivatorTiers
{
    // Config key of each tier cost, for the warnings (index = quality).
    private static readonly string[] CostSetting =
        { "", "", "", "", "BlackMetalLevel", "EitrLevel", "FlametalLevel", "BloodgoldLevel" };

    // Own empty array (Array.Empty is shared with every other mod: never ours to compare by reference).
    private static readonly Piece.Requirement[] NoRows = new Piece.Requirement[0];

    // One tier material: what, which quality, how many.
    private struct TierRow
    {
        internal ItemDrop Item;
        internal int Quality;
        internal int Amount;
    }

    // What GetAmount answer for one of our rows. Quality VanillaRow = vanilla row (0 from quality 4), IdolRow = idol
    // row (0 from quality 2), above 0 = tier row (its amount at that quality only).
    private struct RowAmount
    {
        internal int Quality;
        internal int Amount;
    }

    private const int VanillaRow = 0;
    private const int IdolRow = -1;

    private static ObjectDB _db;
    private static ItemDrop _prefab;
    private static Recipe _recipe;
    private static Piece.Requirement[] _vanillaResources;
    private static int _vanillaMaxQuality;
    // Array me put in the recipe (kept after a revert: same rules again = same array back), what it was built from.
    private static Piece.Requirement[] _installed;
    private static Piece.Requirement[] _installedBase;
    private static readonly List<TierRow> InstalledSpec = new List<TierRow>();
    // Tier row objects of _installed (same order as InstalledSpec): same spec on a new base = same objects again.
    private static readonly List<Piece.Requirement> InstalledTierRows = new List<Piece.Requirement>();
    private static int _resyncCount;
    // m_maxQuality me wrote on the prefab; 0 = vanilla value in place.
    private static int _installedMaxQuality;
    private static bool _inForce;

    // Requirement object -> amount rule. Requirement no override Equals / GetHashCode: lookup by reference.
    private static readonly Dictionary<Piece.Requirement, RowAmount> Rows =
        new Dictionary<Piece.Requirement, RowAmount>();
    // Every row me ever made: never taken as "vanilla" when another mod copy our array.
    private static readonly HashSet<Piece.Requirement> OurRows = new HashSet<Piece.Requirement>();

    private static readonly List<TierRow> Spec = new List<TierRow>();
    private static readonly HashSet<string> LoggedOnce = new HashSet<string>();
    private static readonly string[] TooltipCache = new string[(PlantCatalog.MaxTier + 1) * 2];

    // Tiers in force now (feature on, rules here, cultivator found).
    internal static bool InForce => _inForce;
    internal static Recipe CultivatorRecipe => _recipe;
    internal static ItemDrop CultivatorDrop => _prefab;
    internal static int VanillaMaxQuality => _vanillaMaxQuality;
    // Self tests: array me put in the recipe (null before first install), base it was built from (vanilla rows or
    // another mod's), how many times Resync took a swapped array.
    internal static Piece.Requirement[] InstalledResources => _installed;
    internal static Piece.Requirement[] BaseResources => _vanillaResources;
    internal static int ResyncCount => _resyncCount;

    // Tiers on but recipe hold another array than ours (other mod swap it). Per frame (ZNet.Update): bool + two field
    // reads. Plain reference check on purpose: Apply do the Unity null check.
    internal static bool NeedsResync
    {
        get
        {
            if (!_inForce)
            {
                return false;
            }
            var recipe = _recipe;
            return !ReferenceEquals(recipe, null) && !ReferenceEquals(recipe.m_resources, _installed);
        }
    }

    // Max quality of the prefab now (heal target); 0 = nothing captured.
    internal static int MaxQuality
    {
        get
        {
            var prefab = _prefab;
            if (prefab == null || prefab.m_itemData == null || prefab.m_itemData.m_shared == null)
            {
                return 0;
            }
            return prefab.m_itemData.m_shared.m_maxQuality;
        }
    }

    // THE cultivator check of the whole mod: vanilla name (shared by every copy, every level, PlantEasily /
    // PlantEverything use it too).
    internal static bool IsCultivator(ItemDrop.ItemData item)
    {
        return item != null && item.m_shared != null && item.m_shared.m_name == PlantCatalog.CultivatorItemName;
    }

    // Tier of a cultivator = its quality, at least 1 (refined past 3 before install = that tier). No tool = 0.
    internal static int TierOf(ItemDrop.ItemData tool)
    {
        return tool == null ? 0 : Math.Max(1, tool.m_quality);
    }

    // ObjectDB Awake / CopyOtherDB (TransplantContent.RegisterInObjectDB). Database without vanilla items yet (main menu
    // first Awake, maybe holding only another mod's items): skip quietly.
    internal static void Capture(ObjectDB db)
    {
        if (!TransplantContent.HasVanillaItems(db) || db.m_recipes == null)
        {
            return;
        }
        _db = db;
        var go = db.GetItemPrefab(PlantCatalog.CultivatorPrefab);
        var drop = go != null ? go.GetComponent<ItemDrop>() : null;
        if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
        {
            WarnOnce("noprefab", $"The vanilla Cultivator ({PlantCatalog.CultivatorPrefab}) is missing from the item "
                                 + "database (the game may have changed), so cultivator levels 4 to 7 are not available.");
            return;
        }
        var recipe = FindRecipe(db, drop);
        if (recipe == null)
        {
            WarnOnce("norecipe", "The vanilla Cultivator recipe is missing (the game may have changed), so cultivator "
                                 + "levels 4 to 7 are not available.");
            return;
        }
        if (!ReferenceEquals(drop, _prefab))
        {
            ReleasePrefab();
            _prefab = drop;
            _vanillaMaxQuality = drop.m_itemData.m_shared.m_maxQuality;
            _installedMaxQuality = 0;
        }
        if (!ReferenceEquals(recipe, _recipe))
        {
            ReleaseRecipe();
            _recipe = recipe;
            _vanillaResources = StripOurs(recipe.m_resources);
            _installed = null;
            _installedBase = null;
            InstalledSpec.Clear();
            InstalledTierRows.Clear();
            Rows.Clear();
            _inForce = false;
        }
        Apply();
    }

    // Rules or feature changed (TransplantContent.Rebuild end here), or new database. Idempotent.
    internal static void Apply()
    {
        var recipe = _recipe;
        var prefab = _prefab;
        // Unity null: never captured, or destroyed at game quit.
        if (recipe == null || prefab == null || prefab.m_itemData == null || prefab.m_itemData.m_shared == null)
        {
            _inForce = false;
            return;
        }
        SyncBase(recipe, prefab);
        var rules = ServerRules.Current;
        var db = ObjectDB.instance;
        if (!TransplantContent.HasVanillaItems(db))
        {
            db = _db;
        }
        if (!Plugin.FeatureActive || rules == null || rules.IsPending || db == null)
        {
            Revert(recipe, prefab);
        }
        else
        {
            Install(recipe, prefab, rules, db);
        }
        HealLocal();
    }

    // Another mod put a new array in the recipe while tiers on (recipe config mod at sync or config reload, ObjectDB
    // postfix after ours): without me its rows price levels 4-7 with the vanilla formula (Bronze / Corewood, no tier
    // material). Its rows become the base for levels 1-3 (SyncBase), tier rows go on top again (Apply). Called from the
    // always-on UpdateRecipeList prefix and the ZNet.Update postfix: nothing to do = NeedsResync reads only. Max quality
    // never checked here (MoreBettererUpgrades write it at each SetRecipe: tug of war, documented unsupported).
    internal static void Resync()
    {
        if (!NeedsResync)
        {
            return;
        }
        var recipe = _recipe;
        var current = recipe.m_resources;
        if (current != null && ReferenceEquals(current, _installedBase) && _installed != null)
        {
            // Base me built from put back (other mod re-apply same array): our array again, nothing to rebuild.
            recipe.m_resources = _installed;
            return;
        }
        _resyncCount++;
        if (LoggedOnce.Add("resync"))
        {
            Log.Info("Another mod replaced the Cultivator recipe: its materials are kept for levels 1 to 3, and the "
                     + "level 4 to 7 materials were added to it again.");
        }
        else if (_resyncCount == 100)
        {
            // Tug of war (other mod write a new array again and again): say it once, keep going.
            WarnOnce("resync:many", "Another mod keeps replacing the Cultivator recipe (100 times this session). "
                                    + "Cultivator upgrades may show the wrong materials while both mods fight over it.");
        }
        Apply();
    }

    // GetAmount postfix. Hot (crafting panel, requirement checks, every placed piece): flag first, then one lookup.
    internal static bool TryAmount(Piece.Requirement req, int quality, out int amount)
    {
        amount = 0;
        if (!_inForce || req == null || !Rows.TryGetValue(req, out var row))
        {
            return false;
        }
        if (row.Quality == VanillaRow)
        {
            // Vanilla row: vanilla below the new tiers, nothing from level 4 up.
            return quality >= PlantCatalog.FirstNewTier;
        }
        if (row.Quality == IdolRow)
        {
            // Idol row: vanilla at level 1, nothing for any upgrade (Forge of Potential never upgrade the Cultivator).
            return quality >= 2;
        }
        amount = quality == row.Quality ? row.Amount : 0;
        return true;
    }

    // Every cultivator in this inventory get the prefab's m_maxQuality (stale SharedData copies: Upgrade tab read the
    // item's own copy, DoCrafting the prefab's). Int compare first, name only for the few that differ.
    internal static void HealInventory(Inventory inv)
    {
        if (inv == null)
        {
            return;
        }
        var target = MaxQuality;
        if (target < 1)
        {
            return;
        }
        var items = inv.GetAllItems();
        if (items == null)
        {
            return;
        }
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item == null || item.m_shared == null || item.m_shared.m_maxQuality == target)
            {
                continue;
            }
            if (IsCultivator(item))
            {
                item.m_shared.m_maxQuality = target;
            }
        }
    }

    internal static void HealLocal()
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            return;
        }
        HealInventory(player.GetInventory());
    }

    // Upgrade row of a cultivator at a Forge of Potential (upgrader station): hide it, so an idol never push a
    // cultivator into a tier without the tier cost (D3). Craft tab rows stay.
    internal static bool HideAtUpgrader(InventoryGui gui, Recipe recipe)
    {
        if (recipe == null || !IsCultivatorRecipe(recipe))
        {
            return false;
        }
        var player = Player.m_localPlayer;
        if (player == null)
        {
            return false;
        }
        var station = player.GetCurrentCraftingStation();
        if (station == null || !station.m_upgrader)
        {
            return false;
        }
        return gui == null || !gui.InCraftTab();
    }

    // Upgrade row of ANOTHER cultivator recipe (added by another mod) to level 4 or more while tiers on: hide it, its
    // own rows price the new levels with the vanilla formula (Bronze / Corewood, no tier material). Its craft row and
    // upgrades to 2 and 3 stay. Not per frame (one call per list row).
    internal static bool HideForeignUpgrade(Recipe recipe, ItemDrop.ItemData item)
    {
        if (!_inForce || item == null || recipe == null || ReferenceEquals(recipe, _recipe)
            || item.m_quality < PlantCatalog.FirstNewTier - 1)
        {
            return false;
        }
        var drop = recipe.m_item;
        return drop != null && IsCultivator(drop.m_itemData);
    }

    // Tooltip lines (plain English, vanilla colours) for a cultivator of this quality. Crafting panel (target quality
    // of an upgrade) also say what the new level add. Built once per (quality, crafting): plant table never change.
    internal static string TooltipLines(int quality, bool crafting)
    {
        if (quality < 1)
        {
            quality = 1;
        }
        else if (quality > PlantCatalog.MaxTier)
        {
            quality = PlantCatalog.MaxTier;
        }
        var index = quality * 2 + (crafting ? 1 : 0);
        var cached = TooltipCache[index];
        if (cached != null)
        {
            return cached;
        }
        var text = new StringBuilder();
        text.Append("\nTier: <color=orange>").Append(PlantCatalog.TierTitle(quality)).Append("</color>");
        text.Append("\nReplants: <color=orange>");
        AppendNames(text, PlantCatalog.UnlockedAt(quality));
        text.Append("</color>");
        if (crafting && quality >= PlantCatalog.FirstNewTier)
        {
            var start = text.Length;
            text.Append("\nNew: <color=orange>");
            if (AppendNames(text, PlantCatalog.NewAt(quality)) == 0)
            {
                text.Length = start;
            }
            else
            {
                text.Append("</color>");
            }
        }
        cached = text.ToString();
        TooltipCache[index] = cached;
        return cached;
    }

    private static int AppendNames(StringBuilder text, IEnumerable<PlantKind> kinds)
    {
        var count = 0;
        foreach (var kind in kinds)
        {
            if (count > 0)
            {
                text.Append(", ");
            }
            text.Append(kind.DisplayName);
            count++;
        }
        return count;
    }

    private static bool IsCultivatorRecipe(Recipe recipe)
    {
        if (ReferenceEquals(recipe, _recipe))
        {
            return true;
        }
        var item = recipe.m_item;
        return item != null && IsCultivator(item.m_itemData);
    }

    private static Recipe FindRecipe(ObjectDB db, ItemDrop drop)
    {
        Recipe byItem = null;
        foreach (var recipe in db.m_recipes)
        {
            if (recipe == null || !ReferenceEquals(recipe.m_item, drop))
            {
                continue;
            }
            if (recipe.name == "Recipe_Cultivator")
            {
                return recipe;
            }
            if (byItem == null)
            {
                byItem = recipe;
            }
        }
        return byItem;
    }

    // Another mod wrote the recipe or max quality since (neither vanilla snapshot nor ours): its value is the new base
    // (our old rows taken out), so a revert give back its choice, not ours.
    private static void SyncBase(Recipe recipe, ItemDrop prefab)
    {
        var current = recipe.m_resources;
        if (!ReferenceEquals(current, _installed) && !ReferenceEquals(current, _vanillaResources))
        {
            _vanillaResources = StripOurs(current);
        }
        var shared = prefab.m_itemData.m_shared;
        if (shared.m_maxQuality != _vanillaMaxQuality && shared.m_maxQuality != _installedMaxQuality)
        {
            _vanillaMaxQuality = shared.m_maxQuality;
            _installedMaxQuality = 0;
        }
    }

    private static Piece.Requirement[] StripOurs(Piece.Requirement[] rows)
    {
        if (rows == null || OurRows.Count == 0)
        {
            return rows;
        }
        var ours = 0;
        foreach (var row in rows)
        {
            if (row != null && OurRows.Contains(row))
            {
                ours++;
            }
        }
        if (ours == 0)
        {
            return rows;
        }
        var kept = new Piece.Requirement[rows.Length - ours];
        var at = 0;
        foreach (var row in rows)
        {
            if (row == null || !OurRows.Contains(row))
            {
                kept[at++] = row;
            }
        }
        return kept;
    }

    private static void Revert(Recipe recipe, ItemDrop prefab)
    {
        _inForce = false;
        if (!ReferenceEquals(recipe.m_resources, _vanillaResources))
        {
            recipe.m_resources = _vanillaResources;
        }
        var shared = prefab.m_itemData.m_shared;
        if (shared.m_maxQuality != _vanillaMaxQuality)
        {
            shared.m_maxQuality = _vanillaMaxQuality;
        }
        _installedMaxQuality = 0;
    }

    private static void Install(Recipe recipe, ItemDrop prefab, CultivatorRules rules, ObjectDB db)
    {
        // Main menu: no ZNetScene. Me warn only in game (menu apply is done again in game).
        var inGame = ZNetScene.instance != null;
        Spec.Clear();
        var cap = PlantCatalog.MaxTier;
        for (var q = PlantCatalog.FirstNewTier; q <= PlantCatalog.MaxTier; q++)
        {
            var text = rules.CostOf(q);
            if (ParseInto(text, q, db, inGame, Spec) > 0)
            {
                continue;
            }
            // Free level = no level: cultivator stop below it (and below every level above it).
            cap = q - 1;
            if (inGame)
            {
                WarnOnce("cap:" + q + ":" + text,
                    $"The cultivator upgrade to level {q} ({PlantCatalog.TierTitle(q).ToLowerInvariant()}) has no valid "
                    + $"material ({CostSetting[q]} = \"{text}\"), so cultivators stop at level {q - 1}.");
            }
            break;
        }

        var baseRows = _vanillaResources ?? NoRows;
        if (_installed == null || !ReferenceEquals(_installedBase, baseRows) || !SameSpec(Spec, InstalledSpec))
        {
            Build(baseRows);
        }
        // New array only when content changed (Crafting Search and Sort rebuild its cache on a new array).
        if (!ReferenceEquals(recipe.m_resources, _installed))
        {
            recipe.m_resources = _installed;
        }
        var shared = prefab.m_itemData.m_shared;
        if (shared.m_maxQuality != cap)
        {
            shared.m_maxQuality = cap;
        }
        _installedMaxQuality = cap;
        _inForce = true;
    }

    // Base rows (same objects) + one row per tier material. No tier material at all = base array itself. Same spec on a
    // new base (other mod swap the recipe, Resync) = same tier row objects again, so OurRows no grow at each swap.
    private static void Build(Piece.Requirement[] baseRows)
    {
        Rows.Clear();
        foreach (var row in baseRows)
        {
            if (row != null)
            {
                // Idol row is vanilla object too: in Rows (0 past level 1), never in OurRows.
                Rows[row] = new RowAmount { Quality = row.m_upgraderResource ? IdolRow : VanillaRow, Amount = 0 };
            }
        }
        var reuse = InstalledTierRows.Count == Spec.Count && SameSpec(Spec, InstalledSpec);
        if (!reuse)
        {
            InstalledTierRows.Clear();
        }
        Piece.Requirement[] rows;
        if (Spec.Count == 0)
        {
            rows = baseRows;
        }
        else
        {
            rows = new Piece.Requirement[baseRows.Length + Spec.Count];
            Array.Copy(baseRows, rows, baseRows.Length);
            for (var i = 0; i < Spec.Count; i++)
            {
                var tier = Spec[i];
                Piece.Requirement req;
                if (reuse)
                {
                    req = InstalledTierRows[i];
                }
                else
                {
                    req = new Piece.Requirement
                    {
                        m_resItem = tier.Item,
                        m_amount = 0,
                        m_extraAmountOnlyOneIngredient = 0,
                        m_amountPerLevel = 1,
                        m_upgraderResource = false,
                        m_recover = false,
                    };
                    InstalledTierRows.Add(req);
                    OurRows.Add(req);
                }
                rows[baseRows.Length + i] = req;
                Rows[req] = new RowAmount { Quality = tier.Quality, Amount = tier.Amount };
            }
        }
        _installed = rows;
        _installedBase = baseRows;
        InstalledSpec.Clear();
        InstalledSpec.AddRange(Spec);
    }

    private static bool SameSpec(List<TierRow> a, List<TierRow> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }
        for (var i = 0; i < a.Count; i++)
        {
            if (!ReferenceEquals(a[i].Item, b[i].Item) || a[i].Quality != b[i].Quality || a[i].Amount != b[i].Amount)
            {
                return false;
            }
        }
        return true;
    }

    // "Name:amount,Name:amount" item prefab names (no amount = 1). Unknown names, bad amounts and a material listed
    // twice in one level are skipped (one warning each, in game only). Return how many rows added.
    private static int ParseInto(string text, int quality, ObjectDB db, bool inGame, List<TierRow> into)
    {
        var start = into.Count;
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }
        var setting = CostSetting[quality];
        foreach (var raw in text.Split(','))
        {
            var part = raw.Trim();
            if (part.Length == 0)
            {
                continue;
            }
            var colon = part.IndexOf(':');
            var name = (colon < 0 ? part : part.Substring(0, colon)).Trim();
            var amountText = colon < 0 ? "1" : part.Substring(colon + 1).Trim();
            if (!int.TryParse(amountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount)
                || amount < 1)
            {
                if (inGame)
                {
                    WarnOnce("amount:" + setting + ":" + part,
                        $"The cultivator upgrade material \"{part}\" ({setting}) has no valid amount; it is skipped "
                        + "(write it as Name:amount, for example BlackMetal:5).");
                }
                continue;
            }
            var prefab = name.Length > 0 ? db.GetItemPrefab(name) : null;
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                if (inGame)
                {
                    WarnOnce("item:" + setting + ":" + name,
                        $"The cultivator upgrade material \"{name}\" ({setting}) is not an item in this game; it is "
                        + "skipped.");
                }
                continue;
            }
            if (Listed(into, start, drop))
            {
                if (inGame)
                {
                    WarnOnce("twice:" + setting + ":" + name,
                        $"The cultivator upgrade material \"{name}\" is listed twice in {setting}; only the first one "
                        + "counts.");
                }
                continue;
            }
            into.Add(new TierRow { Item = drop, Quality = quality, Amount = amount });
        }
        return into.Count - start;
    }

    private static bool Listed(List<TierRow> rows, int from, ItemDrop drop)
    {
        for (var i = from; i < rows.Count; i++)
        {
            if (ReferenceEquals(rows[i].Item, drop))
            {
                return true;
            }
        }
        return false;
    }

    // Recipe object changed (another mod gave the Cultivator a new recipe): old one get its array back if ours.
    private static void ReleaseRecipe()
    {
        var old = _recipe;
        if (old != null && _installed != null && ReferenceEquals(old.m_resources, _installed))
        {
            old.m_resources = _vanillaResources;
        }
    }

    private static void ReleasePrefab()
    {
        var old = _prefab;
        if (old != null && old.m_itemData != null && old.m_itemData.m_shared != null && _installedMaxQuality > 0
            && old.m_itemData.m_shared.m_maxQuality == _installedMaxQuality)
        {
            old.m_itemData.m_shared.m_maxQuality = _vanillaMaxQuality;
        }
    }

    private static void WarnOnce(string key, string text)
    {
        if (LoggedOnce.Add(key))
        {
            Log.Warning(text);
        }
    }
}
