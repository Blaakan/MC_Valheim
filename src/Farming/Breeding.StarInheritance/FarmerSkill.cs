using MC.Shared;
using UnityEngine;

namespace MC.Farming.BreedingStarInheritanceMod;

// Me = Farming skill of farmers. Skills live only on own game (other players' Player read 0), so every game with
// mod write own player's effective Farming on own player ZDO (like vanilla taming boost read status from other
// player's ZDO). Game that decide birth take best of: own local player (any distance, always), and other players
// with mod within range (published value). -1 or no key = no mod / mod off = not farmer.
// Publish: at spawn (PlayerPatches: join, respawn, reconnect = new player ZDO) and throttled on breeding ticks
// (Procreate prefix: level up, death drain, status effect change).
internal static class FarmerSkill
{
    internal const string Key = ModInfo.Guid + ".FarmingLevel";
    internal const float NotTakingPart = -1f;
    private const float PublishInterval = 5f;

    private static readonly int KeyHash = Key.GetStableHashCode();
    private static float _nextPublish;

    // Throttle start over (next PublishThrottled write at once).
    internal static void Reset()
    {
        _nextPublish = 0f;
    }

    // Called from Procreate prefix (every game, every loaded breeding animal): one float compare most calls.
    internal static void PublishThrottled()
    {
        var now = Time.time;
        if (now < _nextPublish)
        {
            return;
        }
        _nextPublish = now + PublishInterval;
        PublishNow();
    }

    // Me compare with value ON the ZDO (not cache): respawn/reconnect = new player ZDO without key = write again.
    // True when me wrote.
    internal static bool PublishNow()
    {
        if (!TryGetLocal(out var player, out var zdo))
        {
            return false;
        }
        var level = OwnLevel(player);
        if (zdo.GetFloat(KeyHash, out var current) && current == level)
        {
            return false;
        }
        zdo.Set(KeyHash, level);
        Log.Debug($"Published Farming level {level:0} for other players.");
        return true;
    }

    // Mod off: write -1 (removal not reach peers). No key yet = already "not farmer", nothing to do.
    // Guards make quit safe: ZNet or player gone = skip.
    internal static void Withdraw()
    {
        if (!TryGetLocal(out _, out var zdo))
        {
            return;
        }
        if (!zdo.GetFloat(KeyHash, out var current) || current == NotTakingPart)
        {
            return;
        }
        zdo.Set(KeyHash, NotTakingPart);
        Log.Debug("Withdrew the published Farming level (mod turned off).");
    }

    // Own effective Farming, same number as Skills.GetSkillLevel (stored level + status effects, floored), but me
    // never add skill: vanilla GetSkill add missing entry (Farming 0 row in Skills tab, saved in character, before
    // player ever farm). Entry there = vanilla call (other mods' patches on it still work). No entry = 0 + effects.
    internal static float OwnLevel(Player player)
    {
        var skills = player.GetSkills();
        if (skills == null)
        {
            return 0f;
        }
        if (skills.m_skillData.ContainsKey(Skills.SkillType.Farming))
        {
            return player.GetSkillLevel(Skills.SkillType.Farming);
        }
        var level = 0f;
        var seman = player.GetSEMan();
        if (seman != null)
        {
            seman.ModifySkillLevel(Skills.SkillType.Farming, ref level);
        }
        return Mathf.Floor(level);
    }

    // Self test: character have Farming entry (me must never add one).
    internal static bool HasFarmingEntry(Player player)
    {
        var skills = player.GetSkills();
        return skills != null && skills.m_skillData.ContainsKey(Skills.SkillType.Farming);
    }

    // Value other game see for this player. Missing = NotTakingPart.
    internal static float ReadPublished(Player player)
    {
        var nview = player != null ? player.m_nview : null;
        if (nview == null || !nview.IsValid())
        {
            return NotTakingPart;
        }
        return nview.GetZDO().GetFloat(KeyHash, NotTakingPart);
    }

    // Best farmer for birth at position. Local player always count (fresh skill); others only within range
    // (Vector3.Distance < range, like Player.GetPlayersInRange) and only with published level >= 0.
    // Tie: local win. False = no farmer known.
    internal static bool FindBest(Vector3 position, float range, out Player farmer, out float level, out bool local)
    {
        farmer = null;
        level = 0f;
        local = false;
        var known = false;
        var me = Player.m_localPlayer;
        if (me != null)
        {
            farmer = me;
            level = OwnLevel(me);
            local = true;
            known = true;
        }
        var players = Player.GetAllPlayers();
        for (var i = 0; i < players.Count; i++)
        {
            var other = players[i];
            if (other == null || ReferenceEquals(other, me))
            {
                continue;
            }
            if (!(Vector3.Distance(other.transform.position, position) < range))
            {
                continue;
            }
            var published = ReadPublished(other);
            if (!(published >= 0f))
            {
                continue;
            }
            if (!known || published > level)
            {
                farmer = other;
                level = published;
                local = false;
                known = true;
            }
        }
        return known;
    }

    private static bool TryGetLocal(out Player player, out ZDO zdo)
    {
        player = null;
        zdo = null;
        if (ZNet.instance == null || ZDOMan.instance == null)
        {
            return false;
        }
        var p = Player.m_localPlayer;
        if (p == null)
        {
            return false;
        }
        var nview = p.m_nview;
        if (nview == null || !nview.IsValid())
        {
            return false;
        }
        zdo = nview.GetZDO();
        if (zdo == null)
        {
            return false;
        }
        player = p;
        return true;
    }
}
