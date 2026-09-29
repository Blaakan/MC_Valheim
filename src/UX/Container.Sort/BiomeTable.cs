using System;
using System.Collections.Generic;

namespace MC.UX.ContainerSortMod;

// Me = hand-made list: raw item prefab -> biome where players first get it (vanilla 1.0.16, 355 names, all checked
// in game data). Crafted items (bars, food, meads, weapons, armour...) not here: BiomeIndex derive them from recipes.
// Table win over world scan and derivation. Evidence per name: see docs/design/ux-container-sort.md (curated table).
internal static class BiomeTable
{
    private static readonly string[] Meadows =
    {
        "Wood", "Stone", "Flint", "Resin", "Feathers", "LeatherScraps", "DeerHide", "DeerMeat", "RawMeat", "NeckTail",
        "HardAntler", "Raspberry", "Mushroom", "Dandelion", "Honey", "QueenBee", "Acorn", "BeechSeeds", "BirchSeeds",
        "StoneRock", "Amber", "AmberPearl", "Coins", "SilverNecklace", "BarberKit", "Ironpit", "FeastMeadows",
        "FeastMeadows_Material", "TrophyBoar", "TrophyDeer", "TrophyNeck", "TrophyEikthyr", "Fish1", "Fish2",
    };

    private static readonly string[] BlackForest =
    {
        "FineWood", "RoundLog", "PineCone", "FirCone", "CopperOre", "TinOre", "SurtlingCore", "Coal", "GreydwarfEye",
        "TrollHide", "BoneFragments", "AncientSeed", "Thistle", "Blueberries", "CarrotSeeds", "Carrot", "MushroomYellow",
        "Ruby", "Ectoplasm", "CryptKey", "Pukeberries", "BjornHide", "BjornMeat", "BjornPaw", "BeltStrength",
        "BarrelRings", "Thunderstone", "YmirRemains", "ChickenEgg", "ChickenMeat", "HildirKey_forestcrypt",
        "chest_hildir1", "SpiceForests", "FeastBlackforest", "FeastBlackforest_Material", "TrophyGreydwarf",
        "TrophyGreydwarfBrute", "TrophyGreydwarfShaman", "TrophyForestTroll", "TrophySkeleton", "TrophySkeletonPoison",
        "TrophyGhost", "TrophyTheElder", "TrophyBjorn", "TrophySkeletonHildir", "Fish5",
    };

    private static readonly string[] Swamp =
    {
        "IronScrap", "IronOre", "Guck", "Bloodbag", "Entrails", "Ooze", "WitheredBone", "ElderBark", "Root", "Chain",
        "TurnipSeeds", "Turnip", "WrithanRoots", "Wishbone", "CandleWick", "BlobVial", "CuredSquirrelHamstring",
        "FragrantBundle", "FreshSeaweed", "VineGreenSeeds", "PowderedDragonEgg", "PungentPebbles", "ScytheHandle",
        "MushroomBzerker", "FeastSwamps", "FeastSwamps_Material", "TrophyDraugr", "TrophyDraugrElite",
        "TrophyDraugrFem", "TrophyBlob", "TrophyLeech", "TrophySurtling", "TrophyAbomination", "TrophyWraith",
        "TrophyBonemass", "TrophyKvastur", "TrophyWrithan", "Fish6",
    };

    private static readonly string[] Ocean =
    {
        "SerpentMeat", "SerpentScale", "Chitin", "SpiceOceans", "FeastOceans", "FeastOceans_Material", "TrophySerpent",
        "Fish3", "Fish8",
    };

    private static readonly string[] Mountain =
    {
        "SilverOre", "Obsidian", "Crystal", "WolfPelt", "WolfMeat", "WolfFang", "WolfClaw", "WolfHairBundle",
        "FreezeGland", "DragonTear", "DragonEgg", "OnionSeeds", "Onion", "JuteRed", "HildirKey_mountaincave",
        "chest_hildir2", "SpiceMountains", "FeastMountains", "FeastMountains_Material", "TrophyWolf", "TrophyFenring",
        "TrophyHatchling", "TrophySGolem", "TrophyCultist", "TrophyUlv", "TrophyDragonQueen", "TrophyCultist_Hildir",
        "TrophyBlob_Frost", "Fish4_cave",
    };

    private static readonly string[] Plains =
    {
        "Barley", "Flax", "Cloudberry", "Tar", "Needle", "LoxMeat", "LoxPelt", "BlackMetalScrap", "GoblinTotem",
        "YagluthDrop", "UndeadBjornRibcage", "RottenMeat", "HildirKey_plainsfortress", "chest_hildir3", "SpicePlains",
        "FeastPlains", "FeastPlains_Material", "TrophyDeathsquito", "TrophyGoblin", "TrophyGoblinBrute",
        "TrophyGoblinShaman", "TrophyGoblinKing", "TrophyGrowth", "TrophyLox", "TrophyGoblinBruteBrosBrute",
        "TrophyGoblinBruteBrosShaman", "TrophyBjornUndead", "Fish7",
    };

    private static readonly string[] Mistlands =
    {
        "BlackMarble", "BlackCore", "Sap", "Softtissue", "YggdrasilWood", "Carapace", "Mandible", "BugMeat",
        "RoyalJelly", "QueenDrop", "GiantBloodSack", "Bilebag", "ScaleHide", "HareMeat", "DvergrKeyFragment",
        "DvergrNeedle", "CopperScrap", "Hook", "JuteBlue", "Wisp", "MushroomJotunPuffs", "MushroomMagecap",
        "SpiceMistlands", "FeastMistlands", "FeastMistlands_Material", "TrophyDvergr", "TrophyGjall", "TrophyHare",
        "TrophySeeker", "TrophySeekerBrute", "TrophySeekerQueen", "TrophyTick", "Fish9", "Fish12",
    };

    private static readonly string[] Ashlands =
    {
        "Blackwood", "CharcoalResin", "Grausten", "FlametalOreNew", "FlametalOre", "Flametal", "SulfurStone",
        "ProustitePowder", "MorgenHeart", "MorgenSinew", "MoltenCore", "CharredBone", "Charredskull", "CharredCogwheel",
        "AskHide", "AskBladder", "AsksvinMeat", "AsksvinCarrionNeck", "AsksvinCarrionPelvic", "AsksvinCarrionRibcage",
        "AsksvinCarrionSkull", "AsksvinEgg", "VoltureMeat", "VoltureEgg", "BoneMawSerpentMeat", "BonemawSerpentScale",
        "BonemawSerpentTooth", "CelestialFeather", "FaderDrop", "FaderEmber", "DyrnwynBladeFragment",
        "DyrnwynHiltFragment", "DyrnwynTipFragment", "BellFragment", "GemstoneBlue", "GemstoneGreen", "GemstoneRed",
        "BronzeScrap", "Pot_Shard_Green", "Pot_Shard_Red", "Fiddleheadfern", "Vineberry", "VineberrySeeds",
        "MushroomSmokePuff", "SpiceAshlands", "FeastAshlands", "FeastAshlands_Material", "TrophyAsksvin",
        "TrophyBonemawSerpent", "TrophyCharredArcher", "TrophyCharredMage", "TrophyCharredMelee", "TrophyFader",
        "TrophyFallenValkyrie", "TrophyMorgen", "TrophyVolture", "TrophyBlob_Lava", "Fish11",
    };

    private static readonly string[] DeepNorth =
    {
        "Frostwood", "FirConeFrost", "BarkaBranch", "Ice", "FrozenFuel", "FrostCore", "GoldOre", "MooseHide",
        "MooseMeat", "MooseSinew", "SealBlubber", "SealHide", "ElakingHairBundle", "MoleClaws", "NornThread",
        "Leatherstraps", "MemorialCoal", "OozeMork", "OrbFrostFire", "OrbThunderBlood", "CrownJewel", "FrozenKingDrop",
        "HatefulBlood", "AncientCoin", "AncientGemstoneBlack", "AncientGemstoneGreen", "AncientGemstoneOrange",
        "AncientGemstonePurple", "GlowWorm", "Kale", "KaleSeeds", "Oat", "OatSeeds", "Poteitr", "PoteitrSeeds",
        "Lingonberry", "LastBossGate_RuneTile", "SpiceDeepNorth", "FeastDeepNorth", "FeastDeepNorth_Material",
        // Moulds: Deep North dungeon loot.
        "MoldArmorGoldChest", "MoldArmorGoldHelmet", "MoldArmorGoldLegs", "MoldArmorMageChest", "MoldArmorMageHelmet",
        "MoldArmorMageLegs", "MoldArmormediumChest", "MoldArmorMediumHelmet", "MoldArmorMediumLegs", "MoldAtgeir",
        "MoldAxe", "MoldAxe2H", "MoldBow", "MoldCrossbow", "MoldFistweapon", "MoldKeys", "MoldKnife", "MoldMace",
        "MoldMace2H", "MoldShieldBuckler", "MoldShieldRound", "MoldShieldTower", "MoldSmallParts", "MoldSpear",
        "MoldStafffrostorbs", "MoldStaffOrbofAhri", "MoldStaffspiritcaller", "MoldStaffthunderblood", "MoldSword",
        "MoldSword2H",
        "TrophyBarka", "TrophyBlob_Morkhalla", "TrophyElaking", "TrophyJotunWarrior", "TrophyJotunWitch", "TrophyMole",
        "TrophyMoose", "TrophySeal", "Fish10",
    };

    // Prefab name -> biome. Built once (static data).
    internal static readonly Dictionary<string, BiomeRank> Entries = Build();

    private static Dictionary<string, BiomeRank> Build()
    {
        var map = new Dictionary<string, BiomeRank>(StringComparer.Ordinal);
        Add(map, Meadows, BiomeRank.Meadows);
        Add(map, BlackForest, BiomeRank.BlackForest);
        Add(map, Swamp, BiomeRank.Swamp);
        Add(map, Ocean, BiomeRank.Ocean);
        Add(map, Mountain, BiomeRank.Mountain);
        Add(map, Plains, BiomeRank.Plains);
        Add(map, Mistlands, BiomeRank.Mistlands);
        Add(map, Ashlands, BiomeRank.Ashlands);
        Add(map, DeepNorth, BiomeRank.DeepNorth);
        return map;
    }

    // Same name twice = first (earliest biome) win.
    private static void Add(Dictionary<string, BiomeRank> map, string[] names, BiomeRank rank)
    {
        foreach (var name in names)
        {
            if (!map.ContainsKey(name))
            {
                map[name] = rank;
            }
        }
    }
}
