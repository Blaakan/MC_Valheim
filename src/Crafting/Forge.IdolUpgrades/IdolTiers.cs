namespace MC.Crafting.ForgeIdolUpgradesMod;

// Me = default table of the 8 idol tiers: metal and trophies of the idol's biome. All prefab names checked in the
// 1.0.16 game data (runtime dump). Players can change every list in the config (section per tier).
// Trophy lists come from the user (2026-09-29). Notes: Oozer drops the Blob trophy; Charred Twitcher has no trophy
// (left out); Kall has no trophy, so his boss step uses his Crown Jewel drop. Kvastur and Serpent: in no list.
internal sealed class IdolTierDefaults
{
    internal readonly int Tier;
    internal readonly string Title;
    internal readonly string Material;
    internal readonly string Base;
    internal readonly string Elite;
    internal readonly string Boss;

    private IdolTierDefaults(int tier, string title, string material, string baseTrophies, string elite, string boss)
    {
        Tier = tier;
        Title = title;
        Material = material;
        Base = baseTrophies;
        Elite = elite;
        Boss = boss;
    }

    internal const int Count = 8;

    internal static readonly IdolTierDefaults[] All =
    {
        new IdolTierDefaults(0, "Wooden", "Wood",
            "TrophyDeer",
            "TrophyBoar, TrophyNeck",
            "TrophyEikthyr"),
        new IdolTierDefaults(1, "Bronze", "Bronze",
            "TrophyGreydwarf, TrophyGreydwarfShaman, TrophyGreydwarfBrute, TrophySkeleton",
            "TrophyForestTroll, TrophySkeletonPoison, TrophyBjorn, TrophyGhost",
            "TrophyTheElder, TrophySkeletonHildir"),
        new IdolTierDefaults(2, "Iron", "Iron",
            "TrophyBlob, TrophyDraugr, TrophyDraugrElite, TrophyLeech, TrophySurtling",
            "TrophyAbomination, TrophyWraith, TrophyWrithan",
            "TrophyBonemass"),
        new IdolTierDefaults(3, "Silver", "Silver",
            "TrophyHatchling, TrophyWolf, TrophyUlv, TrophyCultist, TrophyBlob_Frost",
            "TrophySGolem, TrophyFenring",
            "TrophyDragonQueen, TrophyCultist_Hildir"),
        new IdolTierDefaults(4, "Black Metal", "BlackMetal",
            "TrophyDeathsquito, TrophyGoblin, TrophyGoblinShaman, TrophyGrowth, TrophyLox",
            "TrophyGoblinBrute, TrophyBjornUndead",
            "TrophyGoblinKing, TrophyGoblinBruteBrosShaman, TrophyGoblinBruteBrosBrute"),
        new IdolTierDefaults(5, "Black Marble", "BlackMarble",
            "TrophySeeker, TrophyTick, TrophyHare, TrophyDvergr",
            "TrophySeekerBrute, TrophyGjall",
            "TrophySeekerQueen"),
        new IdolTierDefaults(6, "Flametal", "FlametalNew",
            "TrophyCharredMelee, TrophyCharredArcher, TrophyAsksvin, TrophyVolture, TrophyBlob_Lava",
            "TrophyFallenValkyrie, TrophyMorgen, TrophyCharredMage, TrophyBonemawSerpent",
            "TrophyFader"),
        new IdolTierDefaults(7, "Bloodgold", "Gold",
            "TrophyMoose, TrophyElaking, TrophyMole, TrophyJotunWarrior, TrophyBlob_Morkhalla, TrophySeal",
            "TrophyBarka, TrophyJotunWitch",
            "CrownJewel"),
    };

    // Idol prefabs of a tier (both families). Game data: Upgrader0Weapon .. Upgrader7Armor.
    internal static string WeaponIdol(int tier) => "Upgrader" + tier + "Weapon";

    internal static string ArmorIdol(int tier) => "Upgrader" + tier + "Armor";
}
