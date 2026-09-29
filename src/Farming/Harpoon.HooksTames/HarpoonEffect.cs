using System.Collections.Generic;
using MC.Shared;

namespace MC.Farming.HarpoonHooksTamesMod;

// Me tell if a status effect hash = harpoon hook (SE_Harpooned). Me check effect TYPE, not item or prefab name:
// works whatever attack slot throw it (Goldenrevolver swap attacks), works for mod harpoons that reuse SE_Harpooned,
// and me go quiet by myself when other mod eat the hash (HarpoonExtended, ValheimPlus).
internal static class HarpoonEffect
{
    // Hash -> is harpoon. Both answers kept (unknown hash too, e.g. fake 99999 on every unarmed hit).
    private static readonly Dictionary<int, bool> Cache = new Dictionary<int, bool>();

    // ObjectDB list me built cache from. New list or new count (world reload, CopyOtherDB, late mod effects) = redo.
    private static List<StatusEffect> _list;
    private static int _count;

    // Said once per session: other mod took hook away from a harpoon hitting a tame.
    private static bool _reported;

    // Hot path (every hit with a status effect). Int compare first, then list ref/count, then dictionary.
    internal static bool IsHarpoon(int hash)
    {
        if (hash == 0)
        {
            return false;
        }
        var db = ObjectDB.instance;
        if (db == null)
        {
            return false;
        }

        var list = db.m_StatusEffects;
        if (list == null)
        {
            return false;
        }
        if (!ReferenceEquals(list, _list) || list.Count != _count)
        {
            Cache.Clear();
            _list = list;
            _count = list.Count;
        }

        if (Cache.TryGetValue(hash, out var known))
        {
            return known;
        }

        // Miss: vanilla lookup (linear scan). Null (unknown hash) = not harpoon.
        var yes = db.GetStatusEffect(hash) is SE_Harpooned;
        Cache[hash] = yes;
        return yes;
    }

    // Harpoon weapon hit a tame but projectile carry no hook hash: other mod (HarpoonExtended creature pulling) or
    // effect chance ate it. Me leave such hits to vanilla, and say so once in log (help people read C03 test).
    // Run for every projectile target check with non-harpoon hash: bool first, then int, then cheap refs.
    internal static void ReportRemovedHookOnce(Projectile projectile, IDestructible destr)
    {
        if (_reported || projectile.m_statusEffectHash != 0)
        {
            return;
        }

        // Weapon = plain C# object (ItemData): normal null check ok.
        var weapon = projectile.m_weapon;
        if (weapon == null || weapon.m_shared == null || !(weapon.m_shared.m_attackStatusEffect is SE_Harpooned))
        {
            return;
        }

        var local = Player.m_localPlayer;
        if (local == null || projectile.m_owner != local)
        {
            return;
        }

        if (!(destr is Character character) || !TameRules.IsTame(character))
        {
            return;
        }

        _reported = true;
        Log.Info("A harpoon hit a tame without its hook effect. Another mod (for example HarpoonExtended with creature "
                 + "pulling) or the weapon's effect chance removed it; Harpoon Hooks Tames leaves such hits to vanilla.");
    }

    // Me turned on/off: forget all.
    internal static void Clear()
    {
        Cache.Clear();
        _list = null;
        _count = 0;
        _reported = false;
    }
}
