using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Farming.CultivatorReplantMod;

// Me = Cultivator Replant content: one transplant item and one transplant sapling per PlantCatalog row, and the
// saplings put in the cultivator's piece list. Item = clone of the plant's produce (its ground model; its icon with a
// sprout mark painted on, TierIcons), made a plain Material: no food, no status effect. Sapling = clone of a vanilla
// sapling (crop sapling for forage, tree sapling for bushes and Yggdrasil) that grow into the vanilla wild prefab.
// Built once per game session under inactive DontDestroyOnLoad holder (their Awake never run there): items from first
// database that has the produce (main menu ObjectDB once filled, or game ZNetScene on dedicated server), saplings from
// first ZNetScene (templates and grown prefabs live there). One row broken (game update) = that row left out, one
// error in log, other rows work. Registration always on ([AlwaysOnPatch] ObjectDB / ZNetScene postfixes): transplants
// never vanish from inventories and planted saplings stay known while mod installed, also with feature off. Sapling
// piece m_enabled follow feature and rules (pending = hidden from build menu); grow times (prefab and loaded saplings)
// and root range follow rules (Rebuild). Prefab names PERMANENT after release (PlantKind.ItemName / SaplingName: saved in inventories, chests,
// ZDOs). Me never touch vanilla prefabs: clones only. Only vanilla thing me change = cultivator piece table, our
// saplings added at the end (shared prefab, added once).
internal static class TransplantContent
{
    private const float RebuildDelay = 0.5f;

    // Sapling grow time spread around the plant's own time (vanilla saplings have a range too).
    private const float GrowSpreadLow = 0.9f;
    private const float GrowSpreadHigh = 1.1f;

    // Land kinds: m_biome Land (no Ocean), no tilled soil, heat and cold rule of Plant.UpdateHealth (shield only).
    private const string WhereLand = "Grows on open ground in any land biome, no tilling needed; in the Ashlands, the "
                                     + "Mountains and the Deep North only inside a shield.";
    private const string WhereAshlands = "Grows only in the Ashlands.";
    private const string WhereDeepNorth = "Grows only in the Deep North.";

    // Built once, RootRange may change after: no numbers here (hint and hover give them).
    private const string WhereRoot =
        "Must be planted within a few metres of an Ancient Root in the Mistlands, but not right against it. Draws sap "
        + "from the root to grow.";

    // One plant row: what me built for it. Item and sapling built apart (sapling need ZNetScene).
    private sealed class Entry
    {
        internal PlantKind Kind;
        internal string Description;
        internal GameObject Item;
        internal ItemDrop ItemDrop;
        internal bool ItemFailed;    // build blew up: no new try this session
        internal GameObject Sapling;
        internal Plant Plant;
        internal Piece Piece;
        internal bool SaplingFailed;
    }

    private static readonly List<Entry> Entries = MakeEntries();
    private static readonly HashSet<string> LoggedOnce = new HashSet<string>();

    private static GameObject _holder;
    private static GameObject _cultivator;
    private static PieceTable _table;
    private static bool _rebuildPending;
    private static float _rebuildAt;
    private static bool _quitHooked;
    private static bool _quitting;
    private static int _loadedUpdated;
    // Saplings on (enable) at last Rebuild: RequestRebuild skip the wait when this flip.
    private static bool _builtEnabled;

    // At least one transplant item / sapling made (a row left out: its ItemPrefab / SaplingPrefab = null).
    internal static bool ItemsBuilt
    {
        get
        {
            foreach (var entry in Entries)
            {
                if (!ReferenceEquals(entry.Item, null))
                {
                    return true;
                }
            }
            return false;
        }
    }

    internal static bool SaplingsBuilt
    {
        get
        {
            foreach (var entry in Entries)
            {
                if (!ReferenceEquals(entry.Sapling, null))
                {
                    return true;
                }
            }
            return false;
        }
    }

    // Vanilla Cultivator item prefab and its piece table (_CultivatorPieceTable), once a database showed them.
    internal static PieceTable CultivatorTable => _table;
    internal static GameObject CultivatorPrefab => _cultivator;

    internal static bool RebuildPending => _rebuildPending;

    // How many loaded transplant saplings the last enabled Rebuild gave the rules' grow time (self tests).
    internal static int LoadedSaplingsUpdated => _loadedUpdated;

    // Transplant item prefab of this row. Null = not built yet, or row left out (source missing).
    internal static GameObject ItemPrefab(PlantKind kind)
    {
        var entry = EntryOf(kind);
        return entry != null ? entry.Item : null;
    }

    // Transplant sapling prefab of this row. Null = no ZNetScene yet, or row left out.
    internal static GameObject SaplingPrefab(PlantKind kind)
    {
        var entry = EntryOf(kind);
        return entry != null ? entry.Sapling : null;
    }

    // Which row is this transplant item? Null = not a transplant. Drop prefab reference first (every transplant item
    // get our prefab from the item database: crafting, loading, pickup); name only before any build (Object.name
    // allocate, so never after). Tooltips and self tests.
    internal static PlantKind KindOfItem(ItemDrop.ItemData item)
    {
        if (item == null)
        {
            return null;
        }
        var prefab = item.m_dropPrefab;
        if (ReferenceEquals(prefab, null))
        {
            return null;
        }
        var anyBuilt = false;
        foreach (var entry in Entries)
        {
            if (ReferenceEquals(entry.Item, null))
            {
                continue;
            }
            anyBuilt = true;
            if (ReferenceEquals(entry.Item, prefab))
            {
                return entry.Kind;
            }
        }
        if (anyBuilt || prefab == null)
        {
            return null;
        }
        var name = prefab.name;
        foreach (var entry in Entries)
        {
            if (entry.Kind.ItemName == name)
            {
                return entry.Kind;
            }
        }
        return null;
    }

    // Database hold the vanilla items? Main menu first Awake run before CopyOtherDB fill it: empty in vanilla, but
    // ItemManager-based mods (Warfare...) put only their own items in there at that moment. Me wait for the real one
    // (has vanilla Wood), else every vanilla source look "missing" and me log false errors.
    internal static bool HasVanillaItems(ObjectDB db)
    {
        return db != null && db.m_items != null && db.m_items.Count > 0 && db.m_itemByHash != null
               && db.m_itemByHash.ContainsKey(VanillaAnchorHash);
    }

    private const string VanillaAnchor = "Wood";
    private static readonly int VanillaAnchorHash = VanillaAnchor.GetStableHashCode();

    // ObjectDB.Awake / CopyOtherDB postfix (always on). Database without vanilla items yet (main menu first Awake):
    // skip silently, CopyOtherDB postfix do the job. Idempotent: add only what is missing. Cultivator recipe captured
    // for the tiers (CultivatorTiers), then content rebuilt (tiers applied).
    internal static void RegisterInObjectDB(ObjectDB db)
    {
        if (!HasVanillaItems(db))
        {
            return;
        }
        HookQuit();
        BuildItems(db.GetItemPrefab, "item database");
        RetryIcons();
        foreach (var entry in Entries)
        {
            var item = entry.Item;
            if (item == null)
            {
                continue;
            }
            var hash = entry.Kind.ItemHash;
            if (!db.m_itemByHash.ContainsKey(hash))
            {
                if (!db.m_items.Contains(item))
                {
                    db.m_items.Add(item);
                }
                db.m_itemByHash[hash] = item;
                db.m_itemByData[entry.ItemDrop.m_itemData.m_shared] = item;
            }
        }
        FindCultivator(db.GetItemPrefab(PlantCatalog.CultivatorPrefab));
        // Own guard: tier capture blow up never stop the rebuild (pieces) after it.
        try
        {
            CultivatorTiers.Capture(db);
        }
        catch (Exception e)
        {
            PatchGuard.Report("TransplantContent.RegisterInObjectDB CultivatorTiers.Capture", e);
        }
        Rebuild();
    }

    // ZNetScene.Awake postfix (always on, after PlantEverything's postfix): items and saplings known to network, so
    // dropped transplants and planted saplings have a prefab (not "Missing prefab hash"), host never delete them as
    // unknown prefabs. Saplings built here the first time (templates and grown plants live in the scene list), put in
    // the cultivator's piece table. Dedicated server has no main menu: items built here when no database built them.
    // Runs every world load (new ZNetScene each time): idempotent.
    internal static void RegisterInZNetScene(ZNetScene scene)
    {
        if (scene == null || scene.m_prefabs == null || scene.m_prefabs.Count == 0)
        {
            return;
        }
        HookQuit();
        BuildItems(scene.GetPrefab, "network prefab list");
        RetryIcons();
        foreach (var entry in Entries)
        {
            if (entry.Item != null)
            {
                AddPrefab(scene, entry.Item, entry.Kind.ItemHash);
            }
        }
        BuildSaplings(scene);
        foreach (var entry in Entries)
        {
            if (entry.Sapling != null)
            {
                AddPrefab(scene, entry.Sapling, entry.Kind.SaplingHash);
            }
        }
        var db = ObjectDB.instance;
        FindCultivator(db != null ? db.GetItemPrefab(PlantCatalog.CultivatorPrefab) : null);
        FindCultivator(scene.GetPrefab(PlantCatalog.CultivatorPrefab));
        AddToTable();
        Rebuild();
    }

    // Rules changed: rebuild once they stay still RebuildDelay s (ZNet.Update postfix call UpdatePendingRebuild).
    // Saplings switch on or off (server rules came after pending, pending again): rebuild NOW. RootGate pending hold
    // end same moment, so no loaded sapling get one grow try on a stale grow time while me wait.
    internal static void RequestRebuild()
    {
        if (WantEnabled() != _builtEnabled)
        {
            _rebuildPending = false;
            Rebuild();
            return;
        }
        _rebuildPending = true;
        _rebuildAt = Time.unscaledTime + RebuildDelay;
    }

    // What Rebuild would set now: feature on and rules here (not pending).
    private static bool WantEnabled()
    {
        if (!Plugin.FeatureActive)
        {
            return false;
        }
        var rules = ServerRules.Current;
        return rules != null && !rules.IsPending;
    }

    internal static void UpdatePendingRebuild()
    {
        if (!_rebuildPending || Time.unscaledTime < _rebuildAt)
        {
            return;
        }
        _rebuildPending = false;
        Rebuild();
    }

    // Saplings and tiers from rules in force. Called at registration, on rules change (after the delay),
    // OnActivated, OnDeactivated. Piece m_enabled = feature on + rules here (pending = hidden). Feature off = pieces
    // hidden, nothing read (game quit run OnDeactivated while Unity already destroy prefabs: Unity null skipped).
    // Grow times go on the prefab (saplings spawned later) AND on saplings already loaded (UpdateLoadedSaplings):
    // joined while pending, or rules changed, no sapling keep old time. Root range = prefab only (placement ghost).
    // Local build menu refreshed when a piece came or went. Always end with CultivatorTiers.Apply (it revert itself
    // when off or pending).
    internal static void Rebuild()
    {
        CultivatorRules rules = null;
        var enable = false;
        if (Plugin.FeatureActive)
        {
            rules = ServerRules.Current;
            enable = rules != null && !rules.IsPending;
        }
        _builtEnabled = enable;
        var menuChanged = false;
        foreach (var entry in Entries)
        {
            var piece = entry.Piece;
            // Unity null: not built, or destroyed at game quit.
            if (piece == null)
            {
                continue;
            }
            if (enable)
            {
                var plant = entry.Plant;
                if (plant != null)
                {
                    SetGrowTime(plant, entry.Kind, rules.GrowTimeMultiplier);
                }
                if (entry.Kind.NeedsRoot)
                {
                    var range = rules.RootRange > 0f ? rules.RootRange : CultivatorRules.Default.RootRange;
                    if (!Mathf.Approximately(piece.m_connectRadius, range))
                    {
                        // Ghost copy keep old radius: menu refresh make a new ghost.
                        piece.m_connectRadius = range;
                        menuChanged = true;
                    }
                }
            }
            if (piece.m_enabled != enable)
            {
                piece.m_enabled = enable;
                menuChanged = true;
            }
        }
        if (enable)
        {
            // Own guard: walk blow up never stop menu and tiers after it.
            try
            {
                _loadedUpdated = UpdateLoadedSaplings(rules.GrowTimeMultiplier);
            }
            catch (Exception e)
            {
                PatchGuard.Report("TransplantContent.Rebuild loaded saplings", e);
            }
        }
        if (menuChanged)
        {
            RefreshMenu(enable);
        }
        CultivatorTiers.Apply();
    }

    // Saplings already in the world get the grow time of the rules in force, like new ones. Instance keep the copy of
    // the prefab made at spawn (Plant.GetGrowTime read the instance), so without me a sapling loaded while pending (own
    // config on prefab) or before a rules change grow on the old time. None grew on it before: vanilla never grow a
    // plant in its first 10 s, RootGate hold all of ours while pending, and pending over = Rebuild at once (no wait,
    // RequestRebuild). Rebuild rare (debounced): walk all loaded objects, one set lookup each. Returns how many
    // saplings got it.
    internal static int UpdateLoadedSaplings(float multiplier)
    {
        var scene = ZNetScene.instance;
        if (scene == null || scene.m_instances == null)
        {
            return 0;
        }
        var count = 0;
        foreach (var pair in scene.m_instances)
        {
            var zdo = pair.Key;
            if (zdo == null)
            {
                continue;
            }
            var prefab = zdo.GetPrefab();
            if (!RootGate.IsTransplantSapling(prefab)
                || !PlantCatalog.TryFind(prefab, out var kind, out var isSapling) || !isSapling)
            {
                continue;
            }
            var view = pair.Value;
            if (view == null)
            {
                continue;
            }
            var plant = view.GetComponent<Plant>();
            if (plant == null)
            {
                continue;
            }
            SetGrowTime(plant, kind, multiplier);
            count++;
        }
        return count;
    }

    private static List<Entry> MakeEntries()
    {
        var list = new List<Entry>();
        foreach (var kind in PlantCatalog.All)
        {
            list.Add(new Entry { Kind = kind, Description = DescriptionOf(kind) });
        }
        return list;
    }

    private static Entry EntryOf(PlantKind kind)
    {
        if (kind == null)
        {
            return null;
        }
        foreach (var entry in Entries)
        {
            if (ReferenceEquals(entry.Kind, kind))
            {
                return entry;
            }
        }
        return null;
    }

    // Item and piece text: what it is, then where it grows.
    private static string DescriptionOf(PlantKind kind)
    {
        string where;
        if (kind.NeedsRoot)
        {
            where = WhereRoot;
        }
        else if (kind.OnlyInBiome && kind.Biome == Heightmap.Biome.AshLands)
        {
            where = WhereAshlands;
        }
        else if (kind.OnlyInBiome && kind.Biome == Heightmap.Biome.DeepNorth)
        {
            where = WhereDeepNorth;
        }
        else
        {
            where = WhereLand;
        }
        return $"{kind.DisplayName}, dug up roots and all. Plant it with the cultivator to grow a new one. {where}";
    }

    private static Transform Holder()
    {
        if (_holder == null)
        {
            var holder = new GameObject(ModInfo.Guid + ".Prefabs");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            _holder = holder;
        }
        return _holder.transform;
    }

    // Each row alone. Source missing = one error, tried again with next database (game update only). Build blew up =
    // one error, row left out for the session.
    private static void BuildItems(Func<string, GameObject> find, string where)
    {
        var made = 0;
        foreach (var entry in Entries)
        {
            if (entry.Item != null || entry.ItemFailed)
            {
                continue;
            }
            var kind = entry.Kind;
            var produce = find(kind.ProduceItem);
            if (produce == null)
            {
                ErrorOnce("produce:" + kind.Key,
                    $"The vanilla item {kind.ProduceItem} is missing from the {where} (the game may have changed), so "
                    + $"the \"{kind.ItemDisplayName}\" item cannot be made and that plant cannot be replanted. The "
                    + "other plants still work.");
                continue;
            }
            try
            {
                BuildItem(entry, produce);
                made++;
            }
            catch (Exception e)
            {
                entry.ItemFailed = true;
                Log.Error($"Could not make the \"{kind.ItemDisplayName}\" item from the vanilla {kind.ProduceItem} "
                          + "(the game may have changed), so that plant cannot be replanted. The other plants still "
                          + $"work. {e}");
            }
        }
        if (made > 0)
        {
            Log.Debug($"Made {made} transplant item(s) from the {where}.");
        }
    }

    // All or nothing per row: entry set only at end; half-built clone destroyed.
    private static void BuildItem(Entry entry, GameObject produce)
    {
        var kind = entry.Kind;
        var produceDrop = produce.GetComponent<ItemDrop>();
        if (produceDrop == null || produceDrop.m_itemData == null || produceDrop.m_itemData.m_shared == null)
        {
            throw new InvalidOperationException($"{kind.ProduceItem} has no item data");
        }
        var produceShared = produceDrop.m_itemData.m_shared;
        var produceIcon = produceShared.m_icons != null && produceShared.m_icons.Length > 0
            ? produceShared.m_icons[0]
            : null;

        // Instantiate deep copy serialized ItemData / SharedData, so the vanilla produce keep its own.
        var item = Object.Instantiate(produce, Holder(), false);
        try
        {
            item.name = kind.ItemName;
            var drop = item.GetComponent<ItemDrop>();
            var shared = drop.m_itemData.m_shared;
            if (ReferenceEquals(shared, produceShared))
            {
                throw new InvalidOperationException($"the clone shares the {kind.ProduceItem} data");
            }
            shared.m_name = kind.ItemDisplayName;
            shared.m_description = entry.Description;
            // Plant with roots, not a meal: material, no food, no eat effect.
            shared.m_itemType = ItemDrop.ItemData.ItemType.Material;
            shared.m_food = 0f;
            shared.m_foodStamina = 0f;
            shared.m_foodEitr = 0f;
            shared.m_foodBurnTime = 0f;
            shared.m_foodRegen = 0f;
            shared.m_isDrink = false;
            shared.m_consumeStatusEffect = null;
            shared.m_appendToolTip = null;
            shared.m_maxStackSize = 20;
            shared.m_weight = 0.5f;
            shared.m_teleportable = true;
            shared.m_value = 0;
            shared.m_questItem = false;
            var icon = TransplantIconOf(kind, produceIcon);
            if (icon != null)
            {
                shared.m_icons = new[] { icon };
            }
            drop.m_itemData.m_dropPrefab = item;

            entry.Item = item;
            entry.ItemDrop = drop;
        }
        catch
        {
            Object.Destroy(item);
            throw;
        }
    }

    // Produce icon + sprout mark. Dedicated server (no graphics device) or painting failed: plain produce icon.
    private static Sprite TransplantIconOf(PlantKind kind, Sprite produceIcon)
    {
        if (produceIcon == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            return produceIcon;
        }
        try
        {
            var painted = TierIcons.TransplantIcon(produceIcon);
            return painted != null ? painted : produceIcon;
        }
        catch (Exception e)
        {
            WarnOnce("icon:" + kind.Key,
                $"Could not paint the icon of the \"{kind.ItemDisplayName}\" item; it shows the plain "
                + $"{kind.ProduceItem} icon. {e.Message}");
            return produceIcon;
        }
    }

    // Icon still plain (GPU copy came back empty at build: game window minimized while main menu load): me paint again
    // at each registration. Runs before world load fill inventories (they copy item data), so loaded transplants get
    // the sprout too. Sapling piece follow item icon. Painted or failed for good (IconPainter cache) = nothing to do.
    private static void RetryIcons()
    {
        if (!IconPainter.HasGraphics)
        {
            return;
        }
        foreach (var entry in Entries)
        {
            var drop = entry.ItemDrop;
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
            {
                continue;
            }
            var shared = drop.m_itemData.m_shared;
            var current = shared.m_icons != null && shared.m_icons.Length > 0 ? shared.m_icons[0] : null;
            if (current == null || IconPainter.IsPainted(current))
            {
                continue;
            }
            var icon = TransplantIconOf(entry.Kind, current);
            if (icon == null || ReferenceEquals(icon, current))
            {
                continue;
            }
            shared.m_icons = new[] { icon };
            if (entry.Piece != null)
            {
                entry.Piece.m_icon = icon;
            }
        }
    }

    // Each row alone, only rows whose item exist (piece cost = the item). Template or grown plant missing = one error,
    // tried again next world load. Build blew up = one error, row left out for the session.
    private static void BuildSaplings(ZNetScene scene)
    {
        var made = 0;
        foreach (var entry in Entries)
        {
            if (entry.Sapling != null || entry.SaplingFailed || entry.Item == null)
            {
                continue;
            }
            var kind = entry.Kind;
            var template = scene.GetPrefab(kind.SaplingTemplate);
            if (template == null)
            {
                ErrorOnce("template:" + kind.Key,
                    $"The vanilla sapling {kind.SaplingTemplate} is missing (the game may have changed), so the "
                    + $"\"{kind.ItemDisplayName}\" cannot be planted. The other plants still work.");
                continue;
            }
            var grown = new GameObject[kind.GrownPrefabs.Length];
            string missing = null;
            for (var i = 0; i < grown.Length; i++)
            {
                var prefab = scene.GetPrefab(kind.GrownPrefabs[i]);
                // Plant.Grow scale the grown plant through its ZNetView: none = no use.
                if (prefab == null || prefab.GetComponent<ZNetView>() == null)
                {
                    missing = kind.GrownPrefabs[i];
                    break;
                }
                grown[i] = prefab;
            }
            if (missing != null || grown.Length == 0)
            {
                ErrorOnce("grown:" + kind.Key,
                    $"The vanilla plant {missing ?? "(none)"} is missing (the game may have changed), so the "
                    + $"\"{kind.ItemDisplayName}\" cannot be planted. The other plants still work.");
                continue;
            }
            ZNetView root = null;
            if (kind.NeedsRoot)
            {
                var rootPrefab = scene.GetPrefab(PlantCatalog.RootPrefab);
                root = rootPrefab != null ? rootPrefab.GetComponent<ZNetView>() : null;
                if (root == null)
                {
                    ErrorOnce("root:" + kind.Key,
                        $"The vanilla Ancient Root ({PlantCatalog.RootPrefab}) is missing (the game may have changed), "
                        + $"so the \"{kind.ItemDisplayName}\" cannot be planted. The other plants still work.");
                    continue;
                }
                RootGate.UseRootMask(RootLayerMask(rootPrefab));
            }
            try
            {
                BuildSapling(entry, template, grown, root);
                made++;
            }
            catch (Exception e)
            {
                entry.SaplingFailed = true;
                Log.Error($"Could not make the \"{kind.ItemDisplayName}\" sapling from the vanilla "
                          + $"{kind.SaplingTemplate} (the game may have changed), so it cannot be planted. The other "
                          + $"plants still work. {e}");
            }
        }
        if (made > 0)
        {
            Log.Debug($"Made {made} transplant sapling(s).");
        }
    }

    // Layers of the Ancient Root's colliders (1.0: one mesh, root1, on static_solid). RootGate search only these
    // layers, so a busy base never push the root out of its hit buffer. Nothing found or blew up = 0 (RootGate then
    // take static_solid).
    private static int RootLayerMask(GameObject rootPrefab)
    {
        try
        {
            var mask = 0;
            foreach (var collider in rootPrefab.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null)
                {
                    mask |= 1 << collider.gameObject.layer;
                }
            }
            Log.Debug($"Ancient Root collider layer mask: {mask}.");
            return mask;
        }
        catch (Exception e)
        {
            WarnOnce("rootmask", "Could not read the layers of the Ancient Root, so the search for it around Yggdrasil "
                                 + $"transplants uses the usual layer for rocks and roots. {e.Message}");
            return 0;
        }
    }

    // All or nothing per row: entry set only at end; half-built clone destroyed. Grown plant = vanilla prefab, so an
    // uninstall leave grown plants as ordinary plants.
    private static void BuildSapling(Entry entry, GameObject template, GameObject[] grown, ZNetView root)
    {
        var kind = entry.Kind;
        if (template.GetComponent<Plant>() == null || template.GetComponent<Piece>() == null
            || template.GetComponent<ZNetView>() == null)
        {
            throw new InvalidOperationException($"{kind.SaplingTemplate} is not a sapling");
        }
        var sapling = Object.Instantiate(template, Holder(), false);
        try
        {
            sapling.name = kind.SaplingName;
            var plant = sapling.GetComponent<Plant>();
            var piece = sapling.GetComponent<Piece>();
            // Root collider: hover and grow space need it (Plant.HaveGrowSpace skip colliders on a Plant object).
            if (sapling.GetComponent<Collider>() == null)
            {
                throw new InvalidOperationException($"{kind.SaplingTemplate} has no collider on its root");
            }
            // Plant.SUpdate switch these on and off without null check.
            if (plant.m_healthy == null || plant.m_unhealthy == null)
            {
                throw new InvalidOperationException($"{kind.SaplingTemplate} has no healthy or unhealthy look");
            }

            plant.m_name = kind.ItemDisplayName;
            plant.m_grownPrefabs = grown;
            plant.m_minScale = kind.MinScale;
            plant.m_maxScale = kind.MaxScale;
            plant.m_growRadius = kind.GrowRadius;
            plant.m_biome = kind.Biome;
            plant.m_tolerateHeat = kind.TolerateHeat;
            plant.m_tolerateCold = kind.TolerateCold;
            plant.m_needCultivatedGround = false;
            // Misplaced transplant wait (never vanish): too hot, no root in reach...
            plant.m_destroyIfCantGrow = false;
            // Crop template show its own crop at half time (carrot top, magecap): wrong plant. Sapling look only.
            plant.m_healthyGrown = null;
            plant.m_unhealthyGrown = null;
            SetGrowTime(plant, kind, CultivatorRules.Default.GrowTimeMultiplier);

            var itemShared = entry.ItemDrop.m_itemData.m_shared;
            var icon = itemShared.m_icons != null && itemShared.m_icons.Length > 0 ? itemShared.m_icons[0] : null;
            piece.m_name = kind.ItemDisplayName;
            piece.m_description = entry.Description;
            if (icon != null)
            {
                piece.m_icon = icon;
            }
            piece.m_resources = new[]
            {
                new Piece.Requirement { m_resItem = entry.ItemDrop, m_amount = 1, m_recover = false },
            };
            piece.m_cultivatedGroundOnly = false;
            piece.m_groundOnly = true;
            piece.m_onlyInBiome = kind.OnlyInBiome ? kind.Biome : Heightmap.Biome.None;
            piece.m_vegetationGroundOnly = kind.VegetationGroundOnly;
            piece.m_allowedInDeepSnow = kind.AllowedInDeepSnow;
            // Monsters leave it alone, like vanilla tree saplings.
            piece.m_primaryTarget = false;
            piece.m_randomTarget = false;
            piece.m_canBeRemoved = false;
            // Hidden until Rebuild see feature on and rules here.
            piece.m_enabled = false;
            piece.m_mustConnectTo = root;
            piece.m_connectRadius = kind.NeedsRoot ? CultivatorRules.Default.RootRange : 0f;
            if (kind.NeedsRoot && sapling.GetComponent<RootDrawer>() == null)
            {
                sapling.AddComponent<RootDrawer>();
            }

            entry.Sapling = sapling;
            entry.Plant = plant;
            entry.Piece = piece;
        }
        catch
        {
            Object.Destroy(sapling);
            throw;
        }
    }

    // Grow time = plant's own time x multiplier, spread 0.9..1.1 (seconds). Bad multiplier = 1.
    private static void SetGrowTime(Plant plant, PlantKind kind, float multiplier)
    {
        GrowTimes(kind, multiplier, out plant.m_growTime, out plant.m_growTimeMax);
    }

    // m_growTime / m_growTimeMax me write for this row and multiplier (self tests compare instances with it).
    internal static void GrowTimes(PlantKind kind, float multiplier, out float low, out float high)
    {
        if (!(multiplier > 0f) || float.IsInfinity(multiplier))
        {
            multiplier = 1f;
        }
        var seconds = kind.GrowMinutes * 60f * multiplier;
        low = seconds * GrowSpreadLow;
        high = seconds * GrowSpreadHigh;
    }

    // First prefab with an item and a piece table wins (main menu database, else scene list).
    private static void FindCultivator(GameObject prefab)
    {
        if (_table != null || prefab == null)
        {
            return;
        }
        var drop = prefab.GetComponent<ItemDrop>();
        var shared = drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
        if (shared == null || shared.m_buildPieces == null)
        {
            return;
        }
        _cultivator = prefab;
        _table = shared.m_buildPieces;
    }

    // Saplings at the end of the cultivator's list, once (table = shared prefab, live the whole session).
    private static void AddToTable()
    {
        var table = _table;
        if (table == null)
        {
            if (SaplingsBuilt)
            {
                ErrorOnce("table",
                    $"The vanilla Cultivator ({PlantCatalog.CultivatorPrefab}) or its list of plants is missing (the "
                    + "game may have changed), so transplants cannot be planted.");
            }
            return;
        }
        if (table.m_pieces == null)
        {
            table.m_pieces = new List<GameObject>();
        }
        foreach (var entry in Entries)
        {
            var sapling = entry.Sapling;
            if (sapling != null && !table.m_pieces.Contains(sapling))
            {
                table.m_pieces.Add(sapling);
            }
        }
    }

    private static void AddPrefab(ZNetScene scene, GameObject prefab, int hash)
    {
        if (scene.HasPrefab(hash))
        {
            return;
        }
        scene.m_prefabs.Add(prefab);
        scene.m_namedPrefabs.Add(hash, prefab);
    }

    // Piece came or went: local player learn pieces whose transplant it already know (on), build menu and ghost made
    // again. Never at game quit (player half gone; ghost would be made while Unity close).
    private static void RefreshMenu(bool enabled)
    {
        if (_quitting)
        {
            return;
        }
        try
        {
            var player = Player.m_localPlayer;
            if (player == null || ZoneSystem.instance == null || ObjectDB.instance == null)
            {
                return;
            }
            if (enabled)
            {
                player.UpdateKnownRecipesList();
            }
            player.UpdateAvailablePiecesList();
        }
        catch (Exception e)
        {
            PatchGuard.Report("TransplantContent.RefreshMenu", e);
        }
    }

    private static void HookQuit()
    {
        if (_quitHooked)
        {
            return;
        }
        _quitHooked = true;
        Application.quitting += OnQuitting;
    }

    private static void OnQuitting()
    {
        _quitting = true;
    }

    private static void ErrorOnce(string key, string text)
    {
        if (LoggedOnce.Add(key))
        {
            Log.Error(text);
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
