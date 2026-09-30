using MC.Shared;

namespace MC.Combat.WeaponsMovesetMod;

// Melee weapon family, read from swing animation name (design table 2.2). Order = settings order and wire order
// (MoveRules trigger arrays): never reorder, only add at end (and bump MoveRules.Layout + ModNetworkVersion).
internal enum WeaponFamily : byte
{
    None = 0,
    Swords,       // swing_longsword, skill not Clubs
    Maces,        // swing_longsword, skill Clubs
    Axes,         // swing_axe
    Battleaxes,   // battleaxe_attack
    DualAxes,     // dualaxes
    Greatswords,  // greatsword
    Atgeirs,      // atgeir_attack
    Knives,       // knife_stab
    DualKnives,   // dual_knives
    Spears,       // spear_poke
    Fists,        // unarmed_attack
    Sledges,      // swing_sledge
}

// Me = weapon families (design 2.2): detection from swing name, eligibility gates, names for settings (key) and
// player text (label).
internal static class Families
{
    internal const int Count = 12;

    internal static readonly WeaponFamily[] All =
    {
        WeaponFamily.Swords, WeaponFamily.Maces, WeaponFamily.Axes, WeaponFamily.Battleaxes, WeaponFamily.DualAxes,
        WeaponFamily.Greatswords, WeaponFamily.Atgeirs, WeaponFamily.Knives, WeaponFamily.DualKnives,
        WeaponFamily.Spears, WeaponFamily.Fists, WeaponFamily.Sledges,
    };

    // Setting key per family (index = Index(family)). Never rename: key live in player config files.
    private static readonly string[] Keys =
    {
        "Swords", "Maces", "Axes", "Battleaxes", "DualAxes", "Greatswords", "Atgeirs", "Knives", "DualKnives",
        "Spears", "Fists", "Sledges",
    };

    // Player words per family (config descriptions, warnings). Family = animation the swing play: MC Dual Wielding give
    // every pair its PairMoves template (default Berserkir axes, "dualaxes"), two knives its KnifePairMoves (default
    // Skoll and Hati, "dual_knives"), so sword, mace and mixed pairs use DualAxes rows.
    private static readonly string[] Labels =
    {
        "one-handed swords",
        "maces and clubs",
        "one-handed axes",
        "battleaxes",
        "dual axes (the Berserkir axes, and every Dual Wielding pair except two knives)",
        "two-handed swords",
        "atgeirs",
        "knives",
        "dual knives (Skoll and Hati, and Dual Wielding pairs of two knives)",
        "spears",
        "bare hands and fist weapons",
        "sledgehammers",
    };

    // -1 for None or unknown value.
    internal static int Index(WeaponFamily family)
    {
        var i = (int)family - 1;
        return i >= 0 && i < Count ? i : -1;
    }

    internal static string Key(WeaponFamily family)
    {
        var i = Index(family);
        return i >= 0 ? Keys[i] : "None";
    }

    internal static string Label(WeaponFamily family)
    {
        var i = Index(family);
        return i >= 0 ? Labels[i] : "unknown weapons";
    }

    // Family of a swing, from clone's CURRENT base name (after Dual Wielding's prefix changed it) + weapon skill for
    // the one shared name (swing_longsword: Clubs skill = maces). Exact base names only: full trigger name (other mod
    // renamed clone, e.g. "swing_longsword2") or modded name = None, me stand down. String switch: no allocation.
    internal static WeaponFamily Of(string animation, Skills.SkillType skill)
    {
        switch (animation)
        {
            case "swing_longsword":
                return skill == Skills.SkillType.Clubs ? WeaponFamily.Maces : WeaponFamily.Swords;
            case "swing_axe":
                return WeaponFamily.Axes;
            case "battleaxe_attack":
                return WeaponFamily.Battleaxes;
            case "dualaxes":
                return WeaponFamily.DualAxes;
            case "greatsword":
                return WeaponFamily.Greatswords;
            case "atgeir_attack":
                return WeaponFamily.Atgeirs;
            case "knife_stab":
                return WeaponFamily.Knives;
            case "dual_knives":
                return WeaponFamily.DualKnives;
            case "spear_poke":
                return WeaponFamily.Spears;
            case "unarmed_attack":
                return WeaponFamily.Fists;
            case "swing_sledge":
                return WeaponFamily.Sledges;
            default:
                return WeaponFamily.None;
        }
    }

    // Can this swing be a move (design 2.2 gates)? Fail any = swing stay vanilla. All cheap compares.
    //   1. weapon kind Weapon (ItemKinds: torch, tankard, pickaxe, scythe, tools, fishing rod, shields are other
    //      kinds; shield pose + Blocking = Shield even when Tower Shield Wall make tower two-handed)
    //   2. skill not Blocking (Tower Shield Wall bash, whatever item type)
    //   3. melee hit: Horizontal, Vertical or Area (bows, crossbows, staves, bombs, Smoke Screen, harpoon = Projectile)
    //   4. no reload, no bow draw
    //   5. known family
    internal static bool Eligible(Attack clone, ItemDrop.ItemData weapon, out WeaponFamily family)
    {
        family = WeaponFamily.None;
        if (clone == null || weapon == null)
        {
            return false;
        }
        var shared = weapon.m_shared;
        if (shared == null
            || ItemKinds.Classify(shared) != ItemKind.Weapon
            || shared.m_skillType == Skills.SkillType.Blocking)
        {
            return false;
        }
        var type = clone.m_attackType;
        if (type != Attack.AttackType.Horizontal && type != Attack.AttackType.Vertical
            && type != Attack.AttackType.Area)
        {
            return false;
        }
        if (clone.m_requiresReload || clone.m_bowDraw)
        {
            return false;
        }
        family = Of(clone.m_attackAnimation, shared.m_skillType);
        return family != WeaponFamily.None;
    }
}
