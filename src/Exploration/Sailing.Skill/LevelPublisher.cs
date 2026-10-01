using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SailingSkillMod;

// Me = own Sailing level on own player ZDO (key HelmSkill.Key, float 0-100), so the game that simulate a ship (maybe a
// passenger's, maybe the host's) know the helmsman's level and the crew's best (like Breeding's FarmerSkill). Player
// always own its player ZDO: no RPC, no ownership race. Vanilla games ignore the key.
// When: at spawn (join, respawn, reconnect = new player ZDO without key), at feature on, then checked every 2 s
// (level up, death drain, status effect, raiseskill) and written only when it changed. Feature off: -1 (a removed key
// would not reach other games). Pending rules still publish: the level is no effect by itself, owners decide.
internal static class LevelPublisher
{
    private const float Interval = 2f;

    private static float _next;

    // Throttle start over (next Tick check at once).
    internal static void Reset()
    {
        _next = 0f;
    }

    // Player.Update postfix, local player only: one float compare most frames.
    internal static void Tick(Player player)
    {
        var now = Time.time;
        if (now < _next)
        {
            return;
        }
        _next = now + Interval;
        Publish(player);
    }

    // Spawn, feature on. True when me wrote.
    internal static bool PublishNow() => Publish(Player.m_localPlayer);

    // Me compare with value ON the ZDO (not a cache): new player ZDO without key = write again.
    private static bool Publish(Player player)
    {
        if (!TryGetZdo(player, out var zdo) || !ReferenceEquals(player, Player.m_localPlayer))
        {
            return false;
        }
        var level = HelmSkill.LocalLevel();
        if (zdo.GetFloat(HelmSkill.KeyHash, out var current) && current == level)
        {
            return false;
        }
        zdo.Set(HelmSkill.KeyHash, level);
        Log.Debug($"Published Sailing level {level:0} for the games that simulate ships.");
        return true;
    }

    // Feature off: write -1. No key yet = already "not taking part". Quit safe: ZNet or player gone = skip.
    internal static void Withdraw()
    {
        if (!TryGetZdo(Player.m_localPlayer, out var zdo))
        {
            return;
        }
        if (!zdo.GetFloat(HelmSkill.KeyHash, out var current) || current == HelmSkill.NotPublished)
        {
            return;
        }
        zdo.Set(HelmSkill.KeyHash, HelmSkill.NotPublished);
        Log.Debug("Withdrew the published Sailing level (mod turned off).");
    }

    private static bool TryGetZdo(Player player, out ZDO zdo)
    {
        zdo = null;
        if (player == null || ZNet.instance == null || ZDOMan.instance == null)
        {
            return false;
        }
        var nview = player.m_nview;
        if (nview == null || !nview.IsValid())
        {
            return false;
        }
        zdo = nview.GetZDO();
        return zdo != null;
    }
}
