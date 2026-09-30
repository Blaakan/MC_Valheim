using System.Collections.Generic;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Me = boss ladder: boss name token -> boss order 1..8 (design 1.7, 2.2), read from the game's own prefabs
// (Character.m_boss and m_bossOrder > 0), never typed by hand. Kall's first phases and the Hive have order 0: not in.
// Built lazily once per ZNetScene instance (new world = new scene = built again). No ZNetScene = null, nothing kept.
internal static class BossLadder
{
    private static ZNetScene _scene;
    private static Dictionary<string, int> _orders;

    // Token -> order. Null when no ZNetScene (main menu).
    internal static Dictionary<string, int> Get()
    {
        var scene = ZNetScene.instance;
        if (scene == null)
        {
            return null;
        }
        if (_orders != null && ReferenceEquals(scene, _scene))
        {
            return _orders;
        }
        var orders = new Dictionary<string, int>();
        foreach (var prefab in scene.m_prefabs)
        {
            if (prefab == null)
            {
                continue;
            }
            var character = prefab.GetComponent<Character>();
            if (character == null || !character.m_boss || character.m_bossOrder <= 0 || string.IsNullOrEmpty(character.m_name))
            {
                continue;
            }
            // Same token twice (never in 1.0.16): highest order win.
            if (!orders.TryGetValue(character.m_name, out var old) || old < character.m_bossOrder)
            {
                orders[character.m_name] = character.m_bossOrder;
            }
        }
        _orders = orders;
        _scene = scene;
        return orders;
    }

    // Highest order among bosses with at least one kill in the table (0 = none). Pure.
    internal static int RankOf(IDictionary<string, float> kills, IDictionary<string, int> orders)
    {
        var rank = 0;
        if (kills == null || orders == null)
        {
            return 0;
        }
        foreach (var pair in orders)
        {
            if (pair.Value > rank && kills.TryGetValue(pair.Key, out var count) && count >= 1f)
            {
                rank = pair.Value;
            }
        }
        return Mathf.Clamp(rank, 0, MoraleRules.BossCount);
    }

    internal static void Clear()
    {
        _scene = null;
        _orders = null;
    }
}
