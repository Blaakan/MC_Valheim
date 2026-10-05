using System.Collections.Generic;

namespace MC.Farming.CultivatorReplantMod;

// Me = one row per plant the cultivator can dig up: which wild prefabs, which cultivator level needed, what the
// transplant grow into, what vanilla things me clone for item and sapling, where it may grow. Prefab names checked in
// game data (SoftRef bundles, 1.0.16). Item and sapling prefab names come from Key: PERMANENT after release (saved in
// inventories, chests, ZDOs).
internal sealed class PlantKind
{
    internal string Key;
    // English plant name ("Raspberry bush"). Hover name for targets without own name, tooltip lists.
    internal string DisplayName;
    // Lowest cultivator quality that may dig it up: 1 = any (bronze), 4 black metal, 5 eitr, 6 flametal, 7 bloodgold.
    internal int Tier;
    // Vanilla world prefabs me dig up.
    internal string[] WildPrefabs;
    // Vanilla prefabs the sapling grow into (random pick, like vanilla Plant).
    internal string[] GrownPrefabs;
    // Vanilla item me clone for the transplant item (ground model) and whose icon me paint on.
    internal string ProduceItem;
    // Vanilla sapling piece me clone for the transplant sapling.
    internal string SaplingTemplate;
    // Grow time before GrowTimeMultiplier, minutes. Bushes and forage = their own regrow time (D5).
    internal float GrowMinutes;
    internal float MinScale;
    internal float MaxScale;
    internal float GrowRadius;
    internal Heightmap.Biome Biome = Heightmap.Biome.Land;
    // True = Piece.m_onlyInBiome = Biome (ghost red elsewhere).
    internal bool OnlyInBiome;
    internal bool TolerateHeat;
    internal bool TolerateCold;
    internal bool AllowedInDeepSnow;
    // Ashlands: keep off lava (Piece.m_vegetationGroundOnly).
    internal bool VegetationGroundOnly;
    // Yggdrasil: must be planted near an Ancient Root and drain its sap to grow (RootGate).
    internal bool NeedsRoot;

    internal string ItemName => "MC_Transplant_" + Key;
    internal string SaplingName => "MC_Sapling_" + Key;
    internal string ItemDisplayName => DisplayName + " transplant";

    internal int ItemHash { get; private set; }
    internal int SaplingHash { get; private set; }
    internal int[] WildHashes { get; private set; }

    internal void Hash()
    {
        ItemHash = ItemName.GetStableHashCode();
        SaplingHash = SaplingName.GetStableHashCode();
        WildHashes = new int[WildPrefabs.Length];
        for (var i = 0; i < WildPrefabs.Length; i++)
        {
            WildHashes[i] = WildPrefabs[i].GetStableHashCode();
        }
    }
}

internal static class PlantCatalog
{
    internal const int VanillaMaxQuality = 3;
    internal const int FirstNewTier = 4;
    internal const int MaxTier = 7;

    internal const string CultivatorPrefab = "Cultivator";
    internal const string CultivatorItemName = "$item_cultivator";
    internal const string RootPrefab = "YggdrasilRoot";

    private const string ForageSapling = "sapling_carrot";
    private const string MushroomSapling = "sapling_magecap";
    private const string BushSapling = "Beech_Sapling";
    private const string ShootSapling = "Birch_Sapling";

    private static readonly PlantKind[] Kinds =
    {
        Forage("Dandelion", "Dandelion", 1, "Pickable_Dandelion", "Dandelion", ForageSapling, 240f),
        Forage("Thistle", "Thistle", 1, "Pickable_Thistle", "Thistle", ForageSapling, 240f),
        Forage("Mushroom", "Mushroom", 1, "Pickable_Mushroom", "Mushroom", MushroomSapling, 240f),
        Forage("MushroomYellow", "Yellow mushroom", 1, "Pickable_Mushroom_yellow", "MushroomYellow", MushroomSapling,
            240f),
        Bush("RaspberryBush", "Raspberry bush", 1, "RaspberryBush", "Raspberry"),
        Bush("BlueberryBush", "Blueberry bush", 1, "BlueberryBush", "Blueberries"),
        Bush("CloudberryBush", "Cloudberry bush", 4, "CloudberryBush", "Cloudberry"),
        new PlantKind
        {
            Key = "YggaShoot",
            DisplayName = "Yggdrasil shoot",
            Tier = 5,
            WildPrefabs = new[] { "YggaShoot_small1", "YggaShoot1", "YggaShoot2", "YggaShoot3" },
            GrownPrefabs = new[] { "YggaShoot1", "YggaShoot2", "YggaShoot3" },
            ProduceItem = "YggdrasilWood",
            SaplingTemplate = ShootSapling,
            GrowMinutes = 120f,
            MinScale = 0.8f,
            MaxScale = 1.2f,
            GrowRadius = 2f,
            NeedsRoot = true,
        },
        Ashlands(Forage("Fiddlehead", "Fiddlehead", 6, "Pickable_Fiddlehead", "Fiddleheadfern", ForageSapling, 300f)),
        Ashlands(Forage("SmokePuff", "Smoke puff", 6, "Pickable_SmokePuff", "MushroomSmokePuff", MushroomSapling,
            240f)),
        DeepNorth(Bush("LingonberryBush", "Lingonberry bush", 7, "LingonberryBush", "Lingonberry")),
    };

    private static readonly Dictionary<int, PlantKind> ByWildHash = new Dictionary<int, PlantKind>();
    private static readonly Dictionary<int, PlantKind> BySaplingHash = new Dictionary<int, PlantKind>();
    private static readonly Dictionary<string, PlantKind> ByKeyMap = new Dictionary<string, PlantKind>();

    static PlantCatalog()
    {
        foreach (var kind in Kinds)
        {
            kind.Hash();
            foreach (var hash in kind.WildHashes)
            {
                ByWildHash[hash] = kind;
            }
            BySaplingHash[kind.SaplingHash] = kind;
            ByKeyMap[kind.Key] = kind;
        }
    }

    internal static IReadOnlyList<PlantKind> All => Kinds;

    // ZDO prefab hash of a world object -> plant row. isSapling = one of our own transplant saplings (E2).
    internal static bool TryFind(int prefabHash, out PlantKind kind, out bool isSapling)
    {
        if (ByWildHash.TryGetValue(prefabHash, out kind))
        {
            isSapling = false;
            return true;
        }
        if (BySaplingHash.TryGetValue(prefabHash, out kind))
        {
            isSapling = true;
            return true;
        }
        isSapling = false;
        return false;
    }

    internal static PlantKind ByKey(string key) => key != null && ByKeyMap.TryGetValue(key, out var kind) ? kind : null;

    // "bronze" for vanilla levels 1-3 (and below), then the tier metal.
    internal static string TierName(int quality)
    {
        switch (quality)
        {
            case 4:
                return "black metal";
            case 5:
                return "eitr";
            case 6:
                return "flametal";
            default:
                return quality >= MaxTier ? "bloodgold" : "bronze";
        }
    }

    // "Black metal cultivator".
    internal static string TierTitle(int quality)
    {
        var name = TierName(quality);
        return char.ToUpperInvariant(name[0]) + name.Substring(1) + " cultivator";
    }

    // Every plant a cultivator of this quality may dig up.
    internal static IEnumerable<PlantKind> UnlockedAt(int quality)
    {
        foreach (var kind in Kinds)
        {
            if (kind.Tier <= quality)
            {
                yield return kind;
            }
        }
    }

    // Plants this exact quality adds (bronze: every tier-1 plant at quality 1, nothing at 2 and 3).
    internal static IEnumerable<PlantKind> NewAt(int quality)
    {
        foreach (var kind in Kinds)
        {
            if (kind.Tier == quality)
            {
                yield return kind;
            }
        }
    }

    private static PlantKind Forage(string key, string name, int tier, string wild, string produce, string template,
        float minutes)
    {
        return new PlantKind
        {
            Key = key,
            DisplayName = name,
            Tier = tier,
            WildPrefabs = new[] { wild },
            GrownPrefabs = new[] { wild },
            ProduceItem = produce,
            SaplingTemplate = template,
            GrowMinutes = minutes,
            // Forage prefabs no sync scale: random scale would be lost on reload.
            MinScale = 1f,
            MaxScale = 1f,
            GrowRadius = 0.5f,
        };
    }

    private static PlantKind Bush(string key, string name, int tier, string wild, string produce)
    {
        return new PlantKind
        {
            Key = key,
            DisplayName = name,
            Tier = tier,
            WildPrefabs = new[] { wild },
            GrownPrefabs = new[] { wild },
            ProduceItem = produce,
            SaplingTemplate = BushSapling,
            GrowMinutes = 300f,
            MinScale = 0.9f,
            MaxScale = 1.1f,
            GrowRadius = 1f,
        };
    }

    private static PlantKind Ashlands(PlantKind kind)
    {
        kind.Biome = Heightmap.Biome.AshLands;
        kind.OnlyInBiome = true;
        kind.TolerateHeat = true;
        kind.VegetationGroundOnly = true;
        return kind;
    }

    private static PlantKind DeepNorth(PlantKind kind)
    {
        kind.Biome = Heightmap.Biome.DeepNorth;
        kind.OnlyInBiome = true;
        kind.TolerateCold = true;
        kind.AllowedInDeepSnow = true;
        return kind;
    }
}
