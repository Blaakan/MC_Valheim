using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SailingSkillMod;

// Me = who has which Sailing level, as this game can know it. Skills live only on each player's own game, so every
// game with me publish its own effective level on its own player ZDO (LevelPublisher, key below); other games read
// it. Local player always read fresh from own Skills.
//   Physics01(ship)  ship owner (S2-S5) and helmsman HUD: level of the helmsman = player aboard whose player id is
//                    the helm user (vanilla HaveControllingPlayer rule). No helmsman = 0.
//   Damage01(ship)   ship owner (S7): best level of all players aboard (crew's best sailor protect the ship, also
//                    with nobody at the helm).
//   Local01()        rudder (helmsman's own game) and map reveal: own level.
// Missing key or -1 (no mod, mod off) = 0 = no bonus. Level = vanilla GetSkillLevel (stored + status effects, floored),
// s = level / 100 clamped to 0..1.
// Caches: own level 0.5 s; per ship helmsman value 0.5 s in 4 slots (physics ask several times per fixed step).
internal static class HelmSkill
{
    internal const string Key = ModInfo.Guid + ".Level";
    internal static readonly int KeyHash = Key.GetStableHashCode();
    internal const float NotPublished = -1f;

    private const float CacheSeconds = 0.5f;
    private const int Slots = 4;

    private static Player _localFor;
    private static float _localLevel;
    private static float _localUntil = float.NegativeInfinity;

    private static readonly Ship[] SlotShip = new Ship[Slots];
    private static readonly float[] SlotValue = new float[Slots];
    private static readonly float[] SlotUntil = new float[Slots];
    private static int _nextSlot;

#if DEBUG
    private static float? _testLocalLevel;

    // Self test force the local player's level (never the character's skills). Null = normal.
    internal static float? TestLocalLevel
    {
        get => _testLocalLevel;
        set
        {
            _testLocalLevel = value;
            Invalidate();
        }
    }
#endif

    // Level changed a lot (spawn, feature on/off, self test): next read compute again.
    internal static void Invalidate()
    {
        _localFor = null;
        _localUntil = float.NegativeInfinity;
        for (var i = 0; i < Slots; i++)
        {
            SlotShip[i] = null;
        }
    }

    // Own effective Sailing, same number as Skills.GetSkillLevel, but never add the skill: vanilla GetSkill add a
    // missing entry (Sailing 0 row in the Skills panel, saved) before the player ever sailed. Entry there = vanilla call
    // (other mods' patches on it still work). No entry = 0 + status effects (a mead for all skills), floored.
    internal static float OwnLevel(Player player)
    {
        var skills = player != null ? player.GetSkills() : null;
        if (skills == null)
        {
            return 0f;
        }
        if (skills.m_skillData.ContainsKey(SailingSkill.Type))
        {
            return player.GetSkillLevel(SailingSkill.Type);
        }
        var level = 0f;
        var seman = player.GetSEMan();
        if (seman != null)
        {
            seman.ModifySkillLevel(SailingSkill.Type, ref level);
        }
        return Mathf.Floor(level);
    }

    // Local player's level (cached 0.5 s). No local player (server, menu) = 0.
    internal static float LocalLevel()
    {
#if DEBUG
        if (_testLocalLevel.HasValue)
        {
            return _testLocalLevel.Value;
        }
#endif
        var player = Player.m_localPlayer;
        if (player == null)
        {
            return 0f;
        }
        var now = Time.time;
        if (!ReferenceEquals(player, _localFor) || now >= _localUntil)
        {
            _localFor = player;
            _localLevel = OwnLevel(player);
            _localUntil = now + CacheSeconds;
        }
        return _localLevel;
    }

    internal static float Local01() => To01(LocalLevel());

    // Value another game published for this player. Missing = NotPublished.
    internal static float Published(Player player)
    {
        var nview = player != null ? player.m_nview : null;
        if (nview == null || !nview.IsValid())
        {
            return NotPublished;
        }
        return nview.GetZDO().GetFloat(KeyHash, NotPublished);
    }

    // Level of one player as this game know it: own = fresh, other = published (no mod / off = 0).
    internal static float LevelOf(Player player)
    {
        if (player == null)
        {
            return 0f;
        }
        if (ReferenceEquals(player, Player.m_localPlayer))
        {
            return LocalLevel();
        }
        var published = Published(player);
        return published > 0f ? published : 0f;
    }

    // Player aboard who hold the helm (vanilla ShipControlls.HaveValidUser: user id set and that player in the ship's
    // list), else null.
    internal static Player Helmsman(Ship ship)
    {
        if (ship == null)
        {
            return null;
        }
        var players = ship.m_players;
        if (players == null || players.Count == 0)
        {
            return null;
        }
        var controls = ship.m_shipControlls;
        if (controls == null)
        {
            return null;
        }
        var user = controls.GetUser();
        if (user == 0L)
        {
            return null;
        }
        for (var i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p != null && p.GetPlayerID() == user)
            {
                return p;
            }
        }
        return null;
    }

    // Helmsman's s for this ship (cached 0.5 s per ship). Empty ship = 0 at once (moored ships cost one compare).
    internal static float Physics01(Ship ship)
    {
        if (ship == null || ship.m_players.Count == 0)
        {
            return 0f;
        }
        var now = Time.time;
        for (var i = 0; i < Slots; i++)
        {
            if (ReferenceEquals(SlotShip[i], ship))
            {
                if (now < SlotUntil[i])
                {
                    return SlotValue[i];
                }
                return Store(i, ship, now);
            }
        }
        var slot = _nextSlot;
        _nextSlot = (_nextSlot + 1) % Slots;
        return Store(slot, ship, now);
    }

    // Best s of all players aboard (not cached: hits are rare).
    internal static float Damage01(Ship ship)
    {
        if (ship == null)
        {
            return 0f;
        }
        var players = ship.m_players;
        var best = 0f;
        for (var i = 0; i < players.Count; i++)
        {
            var level = LevelOf(players[i]);
            if (level > best)
            {
                best = level;
            }
        }
        return To01(best);
    }

    // Debug line when the helmsman's level of a ship change (new helmsman, level up, left the helm): testers see which
    // level the game that simulate the ship use.
    private static float Store(int slot, Ship ship, float now)
    {
        var helmsman = Helmsman(ship);
        var level = LevelOf(helmsman);
        var value = To01(level);
        var same = ReferenceEquals(SlotShip[slot], ship);
        if (same ? value != SlotValue[slot] : value > 0f)
        {
            Log.Debug($"{Utils.GetPrefabName(ship.gameObject)}: "
                      + (helmsman != null ? $"{helmsman.GetPlayerName()} at the helm, Sailing {level:0}" : "nobody at the helm")
                      + (ship.IsOwner() ? " (this game simulates the ship)." : "."));
        }
        SlotShip[slot] = ship;
        SlotValue[slot] = value;
        SlotUntil[slot] = now + CacheSeconds;
        return value;
    }

    private static float To01(float level) => Mathf.Clamp01(level / 100f);
}
