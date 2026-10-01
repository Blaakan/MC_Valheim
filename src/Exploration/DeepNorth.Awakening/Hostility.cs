namespace MC.Exploration.DeepNorthAwakeningMod;

// Me = who is Jotun army and who is nature (design 2.5). Vanilla put all of them in Faction.DeepNorth, so BaseAI.IsEnemy
// say "friends". Once the north is awake (WorldState.HostilityOn), army vs nature = enemies, both ways. Prefab told by
// the ZDO prefab hash (no string, no allocation: IsEnemy run for every creature pair an AI look at).
internal static class Hostility
{
    internal const string Krigen = "JotunWarrior";
    internal const string KrigenDual = "JotunWarriorDualWield";
    internal const string Hexen = "JotunWitch";
    internal const string Elaking = "Elaking";
    internal const string Gammeltroll = "TrollFrost";
    internal const string Barka = "Barka";
    internal const string FrostGreydwarf = "Greydwarf_Frozen";
    internal const string FrostShaman = "Greydwarf_Shaman_Frozen";

    internal static readonly string[] Army = { Krigen, KrigenDual, Hexen, Elaking };
    internal static readonly string[] Nature = { Gammeltroll, Barka, FrostGreydwarf, FrostShaman };

    private static readonly int KrigenHash = Krigen.GetStableHashCode();
    private static readonly int KrigenDualHash = KrigenDual.GetStableHashCode();
    private static readonly int HexenHash = Hexen.GetStableHashCode();
    private static readonly int ElakingHash = Elaking.GetStableHashCode();
    private static readonly int GammeltrollHash = Gammeltroll.GetStableHashCode();
    private static readonly int BarkaHash = Barka.GetStableHashCode();
    private static readonly int FrostGreydwarfHash = FrostGreydwarf.GetStableHashCode();
    private static readonly int FrostShamanHash = FrostShaman.GetStableHashCode();

    internal static bool IsArmy(int prefab)
    {
        return prefab != 0 && (prefab == KrigenHash || prefab == KrigenDualHash || prefab == HexenHash
                               || prefab == ElakingHash);
    }

    // Place in Army (0-3), -1 = not army.
    internal static int ArmyIndex(int prefab)
    {
        if (prefab == 0)
        {
            return -1;
        }
        return prefab == KrigenHash ? 0 : prefab == KrigenDualHash ? 1 : prefab == HexenHash ? 2 : prefab == ElakingHash ? 3 : -1;
    }

    internal static bool IsNature(int prefab)
    {
        return prefab != 0 && (prefab == GammeltrollHash || prefab == BarkaHash || prefab == FrostGreydwarfHash
                               || prefab == FrostShamanHash);
    }

    // Pure (self test): one army, one nature, either order.
    internal static bool AreFoes(int prefabA, int prefabB)
    {
        return (IsArmy(prefabA) && IsNature(prefabB)) || (IsNature(prefabA) && IsArmy(prefabB));
    }

    // Prefab hash of a live creature, 0 when it has no ZDO (ghost, being destroyed).
    internal static int PrefabOf(Character c)
    {
        var nview = c.m_nview;
        if (nview == null)
        {
            return 0;
        }
        var zdo = nview.GetZDO();
        return zdo != null ? zdo.GetPrefab() : 0;
    }

    // BaseAI.IsEnemy postfix body. Vanilla said "not enemies" before this is asked.
    internal static bool MakeFoes(Character a, Character b)
    {
        if (a == null || b == null)
        {
            return false;
        }
        if (a.m_faction != Character.Faction.DeepNorth || b.m_faction != Character.Faction.DeepNorth)
        {
            return false;
        }
        if (a.IsTamed() || b.IsTamed())
        {
            return false;
        }
        return AreFoes(PrefabOf(a), PrefabOf(b));
    }
}
