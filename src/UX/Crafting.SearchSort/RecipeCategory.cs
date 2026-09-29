using MC.Shared;

namespace MC.UX.CraftingSearchSortMod;

// Me = sort options of the menu: index (menu order), id (saved in character), label, and which rows belong.
// Built on shared MC.Shared.ItemKinds, so Sort Chest put same item in same group. A row belong to at most two
// options (e.g. Weapons + Swords, Armor + Helmets).
internal static class RecipeCategory
{
    internal const int Default = 0;
    internal const int Name = 1;
    internal const int Weapons = 2;
    private const int FirstFamily = 3; // weapon families 3..13, same order as Families
    internal const int Shields = 14;
    internal const int Armor = 15;
    internal const int Helmets = 16;
    internal const int ChestArmor = 17;
    internal const int LegArmor = 18;
    internal const int Capes = 19;
    internal const int Ammo = 20;
    internal const int Tools = 21;
    internal const int Food = 22;
    internal const int Meads = 23;
    internal const int Trinkets = 24;
    internal const int Utility = 25;
    internal const int Materials = 26;
    internal const int Fish = 27;
    internal const int Trophies = 28;
    internal const int Other = 29;
    internal const int Count = 30;

    private static readonly Skills.SkillType[] Families =
    {
        Skills.SkillType.Swords, Skills.SkillType.Axes, Skills.SkillType.Clubs, Skills.SkillType.Knives,
        Skills.SkillType.Spears, Skills.SkillType.Polearms, Skills.SkillType.Bows, Skills.SkillType.Crossbows,
        Skills.SkillType.ElementalMagic, Skills.SkillType.BloodMagic, Skills.SkillType.Unarmed,
    };

    // Saved in Player.m_customData. Never rename: old characters keep these.
    private static readonly string[] Ids =
    {
        "default", "name", "weapons",
        "w_swords", "w_axes", "w_clubs", "w_knives", "w_spears", "w_polearms", "w_bows", "w_crossbows",
        "w_elementalmagic", "w_bloodmagic", "w_unarmed",
        "shields", "armor", "helmets", "chest", "legs", "capes", "ammo", "tools", "food", "meads", "trinkets",
        "utility", "materials", "fish", "trophies", "other",
    };

    // English labels. Weapon families use vanilla skill names when game know them (see Label).
    private static readonly string[] English =
    {
        "Default", "Name (A-Z)", "Weapons",
        "Swords", "Axes", "Clubs", "Knives", "Spears", "Polearms", "Bows", "Crossbows",
        "Elemental magic", "Blood magic", "Unarmed",
        "Shields", "Armor", "Helmets", "Chest armor", "Leg armor", "Capes", "Ammo", "Tools and light", "Food",
        "Meads and potions", "Trinkets", "Utility", "Materials", "Fish", "Trophies", "Other",
    };

    // Localized family labels, filled on first use, dropped on language change.
    private static readonly string[] FamilyLabels = new string[Count];

    internal static string Id(int option) => option >= 0 && option < Count ? Ids[option] : Ids[Default];

    // Unknown or old id = Default.
    internal static int FromId(string id)
    {
        for (var i = 0; i < Count; i++)
        {
            if (Ids[i] == id)
            {
                return i;
            }
        }
        return Default;
    }

    internal static string Label(int option)
    {
        if (option < 0 || option >= Count)
        {
            return English[Default];
        }
        if (option < FirstFamily || option >= FirstFamily + Families.Length)
        {
            return English[option];
        }

        var label = FamilyLabels[option];
        if (label == null)
        {
            // Vanilla skill name token: "$skill_" + enum lower (SkillsDialog). Not known = "[token]" back: use English.
            var skill = Families[option - FirstFamily];
            var localized = Localization.instance.Localize("$skill_" + skill.ToString().ToLowerInvariant());
            label = string.IsNullOrEmpty(localized) || localized.StartsWith("[") ? English[option] : localized;
            FamilyLabels[option] = label;
        }
        return label;
    }

    internal static void ClearLabels()
    {
        for (var i = 0; i < FamilyLabels.Length; i++)
        {
            FamilyLabels[i] = null;
        }
    }

    // Options a row belong to: first (always one, maybe Other) and second (-1 = none).
    internal static void Of(ItemKind kind, Skills.SkillType skill, out int first, out int second)
    {
        second = -1;
        switch (kind)
        {
            case ItemKind.Weapon:
                first = Weapons;
                second = FamilyOf(skill);
                return;
            case ItemKind.Shield:
                first = Shields;
                return;
            case ItemKind.Helmet:
                first = Armor;
                second = Helmets;
                return;
            case ItemKind.Chest:
                first = Armor;
                second = ChestArmor;
                return;
            case ItemKind.Legs:
                first = Armor;
                second = LegArmor;
                return;
            case ItemKind.Cape:
                first = Armor;
                second = Capes;
                return;
            case ItemKind.Hands:
                first = Armor; // gloves count for Armor only
                return;
            case ItemKind.Ammo:
                first = Ammo;
                return;
            case ItemKind.Tool:
            case ItemKind.SkillTool:
            case ItemKind.Torch:
                first = Tools;
                return;
            case ItemKind.Food:
                first = Food;
                return;
            case ItemKind.Potion:
                first = Meads;
                return;
            case ItemKind.Trinket:
                first = Trinkets;
                return;
            case ItemKind.Utility:
                first = Utility;
                return;
            case ItemKind.Material:
                first = Materials;
                return;
            case ItemKind.Fish:
                first = Fish;
                return;
            case ItemKind.Trophy:
                first = Trophies;
                return;
            default:
                first = Other; // Misc (tankards) and Other
                return;
        }
    }

    // Weapon family option of a skill, -1 = none (weapon then only in Weapons).
    private static int FamilyOf(Skills.SkillType skill)
    {
        for (var i = 0; i < Families.Length; i++)
        {
            if (Families[i] == skill)
            {
                return FirstFamily + i;
            }
        }
        return -1;
    }
}
