using MC.Shared;

namespace MC.UX.ContainerSortMod;

// Me = "By type" order of a chest. Kind come from shared MC.Shared.ItemKinds, same kinds Crafting Search and Sort
// use for its menu, so same item land in same group in both mods:
//   Tools and light = Tool + SkillTool + Torch, Food = Food, Meads and potions = Potion, Other = Misc (tankards) + Other.
// Me only give each kind a place (group rank + rank inside group). Every menu group stay one block in the chest.
internal static class TypeGroups
{
    internal const int Weapons = 0;
    internal const int Ammo = 1;
    internal const int Shields = 2;
    internal const int Armour = 3;
    internal const int UtilityAndTrinkets = 4;
    internal const int ToolsAndLight = 5;
    internal const int FoodAndPotions = 6;
    internal const int Materials = 7;
    internal const int Fish = 8;
    internal const int Trophies = 9;
    internal const int Other = 10;

    // For Debug log only (index = group rank).
    private static readonly string[] Labels =
    {
        "Weapons", "Ammo", "Shields", "Armour", "Utility and trinkets", "Tools and light", "Food and potions",
        "Materials", "Fish", "Trophies", "Other",
    };

    // Group rank and rank inside group. Null shared data = Other.
    internal static void Rank(ItemDrop.ItemData.SharedData shared, out int typeRank, out int subRank)
    {
        subRank = 0;
        switch (ItemKinds.Classify(shared))
        {
            case ItemKind.Weapon:
                typeRank = Weapons;
                subRank = WeaponFamily(shared.m_skillType);
                return;
            case ItemKind.Ammo:
                typeRank = Ammo;
                return;
            case ItemKind.Shield:
                typeRank = Shields;
                return;
            case ItemKind.Helmet:
                typeRank = Armour;
                return;
            case ItemKind.Chest:
                typeRank = Armour;
                subRank = 1;
                return;
            case ItemKind.Legs:
                typeRank = Armour;
                subRank = 2;
                return;
            case ItemKind.Hands:
                typeRank = Armour;
                subRank = 3;
                return;
            case ItemKind.Cape:
                typeRank = Armour;
                subRank = 4;
                return;
            case ItemKind.Utility:
                typeRank = UtilityAndTrinkets;
                return;
            case ItemKind.Trinket:
                typeRank = UtilityAndTrinkets;
                subRank = 1;
                return;
            case ItemKind.Tool:
                typeRank = ToolsAndLight;
                return;
            case ItemKind.SkillTool:
                typeRank = ToolsAndLight;
                subRank = 1;
                return;
            case ItemKind.Torch:
                typeRank = ToolsAndLight;
                subRank = 2;
                return;
            case ItemKind.Food:
                typeRank = FoodAndPotions;
                return;
            case ItemKind.Potion:
                typeRank = FoodAndPotions;
                subRank = 1;
                return;
            case ItemKind.Material:
                typeRank = Materials;
                return;
            case ItemKind.Fish:
                typeRank = Fish;
                return;
            case ItemKind.Trophy:
                typeRank = Trophies;
                return;
            case ItemKind.Misc:
                typeRank = Other; // tankards and Misc first inside Other, like design order Misc then Other
                return;
            default:
                typeRank = Other;
                subRank = 1;
                return;
        }
    }

    internal static string Label(int typeRank) =>
        typeRank >= 0 && typeRank < Labels.Length ? Labels[typeRank] : Labels[Other];

    // Melee first, then ranged, then magic. Unknown (modded) skill last.
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
}
