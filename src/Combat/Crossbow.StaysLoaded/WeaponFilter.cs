using System;
using System.Collections.Generic;

namespace MC.Combat.CrossbowStaysLoadedMod;

// Me = which reload weapons keep their load. User choose in config (live):
//   Weapons.Crossbows     -> every weapon using Crossbows skill (also crossbows from other mods)
//   Weapons.ExtraItems    -> more prefab names (default GrapplingHook; e.g. StaffLightning = Dundr, other mods' items)
//   Weapons.ExcludedItems -> never, win over the rest
// Names match prefab name (spawn command name) or the item token ($item_...), case ignored.
internal static class WeaponFilter
{
    private static HashSet<string> _extra = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> _excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

#if DEBUG
    // Self test only (SelfTests.cs): Crossbows setting forced in memory, null = player's own setting. Single-player
    // test never write player's cfg. Name lists: test call Rebuild with own text, then again with the settings.
    internal static bool? TestCrossbows;

    private static bool CrossbowsSetting => TestCrossbows ?? Plugin.KeepCrossbows?.Value ?? true;
#else
    private static bool CrossbowsSetting => Plugin.KeepCrossbows?.Value ?? true;
#endif

    public static void Rebuild(string extra, string excluded)
    {
        _extra = Parse(extra);
        _excluded = Parse(excluded);
    }

    public static bool Allows(string prefabName, ItemDrop.ItemData.SharedData shared)
    {
        if (shared == null)
        {
            return false;
        }
        if (Matches(_excluded, prefabName, shared))
        {
            return false;
        }
        var crossbows = CrossbowsSetting;
        if (crossbows && shared.m_skillType == Skills.SkillType.Crossbows)
        {
            return true;
        }
        return Matches(_extra, prefabName, shared);
    }

    // Unity object: explicit null check, no ?. (destroyed-object rule).
    public static string PrefabName(ItemDrop.ItemData item) =>
        item != null && item.m_dropPrefab != null ? item.m_dropPrefab.name : null;

    private static bool Matches(HashSet<string> set, string prefabName, ItemDrop.ItemData.SharedData shared) =>
        set.Count > 0 && ((prefabName != null && set.Contains(prefabName)) || (shared.m_name != null && set.Contains(shared.m_name)));

    private static HashSet<string> Parse(string list)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (list ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = part.Trim();
            if (name.Length > 0)
            {
                set.Add(name);
            }
        }
        return set;
    }
}
