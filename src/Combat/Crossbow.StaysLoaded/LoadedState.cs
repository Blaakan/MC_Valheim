using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MC.Combat.CrossbowStaysLoadedMod;

// Me = "this crossbow hold a bolt" stamp, stored on item (m_customData, saved with inventory/chest/drop).
// Value = "v1:<durability when stamped>". A shot eat a whole m_useDurabilityDrain (usually 1), even from player
// WITHOUT mod, so durability dropped by a shot = someone fired = stamp stale = not loaded.
// Limits (documented in README): shot fired without the mod AND repaired before it reach us again, or world with
// durability loss off, cannot be seen.
internal static class LoadedState
{
    internal const string Key = ModInfo.Guid + ".Loaded";
    private const string Prefix = "v1:";

    // Me = weapon that can keep a load: needs reload, and no eat itself when fired.
    public static bool IsEligible(ItemDrop.ItemData item)
    {
        var attack = item?.m_shared?.m_attack;
        return attack != null && attack.m_requiresReload && !attack.m_consumeItem;
    }

    public static bool HasStamp(ItemDrop.ItemData item) =>
        item?.m_customData != null && item.m_customData.ContainsKey(Key);

    public static void Mark(ItemDrop.ItemData item)
    {
        if (item == null)
        {
            return;
        }
        item.m_customData ??= new Dictionary<string, string>();
        item.m_customData[Key] = Prefix + item.m_durability.ToString("R", CultureInfo.InvariantCulture);
    }

    public static void Clear(ItemDrop.ItemData item) => item?.m_customData?.Remove(Key);

    // Valid stamp + durability not dropped by a shot since = still loaded. Bad format (other mod, hand edit,
    // future version) = not loaded.
    public static bool IsLoaded(ItemDrop.ItemData item)
    {
        if (item?.m_customData == null || !item.m_customData.TryGetValue(Key, out var value) || value == null)
        {
            return false;
        }
        if (!value.StartsWith(Prefix, System.StringComparison.Ordinal))
        {
            return false;
        }
        if (!float.TryParse(value.Substring(Prefix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out var stamped))
        {
            return false;
        }

        // Save/load round durability DOWN to 0.01 every hop (ItemData.Save write (int)(d*100)); chests and drops
        // can hop many times. So allow small drop, far less than one shot. Durability going UP = repair = stale
        // (repair patch re-stamp loaded ones itself).
        var drop = stamped - item.m_durability;
        var shot = item.m_shared.m_useDurabilityDrain * Game.m_durabilityRate;
        var tolerance = Mathf.Max(0.05f, 0.5f * shot);
        return drop > -0.001f && drop < tolerance;
    }
}
