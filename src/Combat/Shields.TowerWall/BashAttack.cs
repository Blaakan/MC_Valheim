using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.ShieldsTowerWallMod;

// Me = the shield bash Attack (design 2.6, 2.7). One object shared by every tower copy (m_attack); vanilla StartAttack
// clone it per swing. Built from the player's unarmed primary attack (no player at ObjectDB time: built lazily, at the
// first heal with a player, or at rules apply when a player exist). Rebuilt (new object) at every rules apply.
// Animation: BashAnimation option -> trigger, checked once per rules generation and player object (HasParameter
// allocate): trigger must exist on the player animator; Custom must also be an attack trigger some player item fire,
// and not one of a held, aimed, reloaded or attached attack (bash could hang in its clip: decision 28).
// Refused or failing (watchdogs, BashWatch) = next option of the fallback chain, one Warning; the fallback hold for the
// rules generation (settings change retry the chosen option).
//   Custom -> ShieldPunch -> OtherPunch ; Kick -> OtherPunch ; OtherPunch = last.
// Swing speed (BashAnimationSpeed) and bash cooldown are not in the Attack: BashSpeed scale the clip, BashWatch refuse
// a start too soon.
internal static class BashAttack
{
    internal const string ShieldArmTrigger = "unarmed_attack1";
    internal const string OtherArmTrigger = "unarmed_attack0";
    internal const string KickTrigger = "unarmed_kick";
    internal const float RayWidth = 0.4f;

    private static Attack _attack;
    private static TowerRules _rules;
    private static BashAnimationKind _mode;
    private static Player _resolvedFor;
    private static readonly HashSet<BashAnimationKind> Warned = new HashSet<BashAnimationKind>();
    private static bool _lastResortReported;
    private static HashSet<string> _attackTriggers;
    private static HashSet<string> _heldTriggers;
    private static ObjectDB _triggersFor;

    // Bash of the applied rules, null = not built yet (no player seen) or no rules.
    internal static Attack Current => _attack != null && ReferenceEquals(_rules, TowerSync.Applied) ? _attack : null;

    // Option in use after fallbacks.
    internal static BashAnimationKind Mode => _mode;

    // Rules apply, deactivation: next Get build a new object, fallback state reset.
    internal static void Reset()
    {
        _attack = null;
        _rules = null;
        _resolvedFor = null;
        Warned.Clear();
        _lastResortReported = false;
    }

    // Bash for the applied rules; build it when missing (source = humanoid whose unarmed attack is the template; null =
    // local player). Trigger checked again when the local player object changed (death, new world).
    internal static Attack Get(Humanoid source)
    {
        var rules = TowerSync.Applied;
        if (rules == null)
        {
            return null;
        }
        if (_attack == null || !ReferenceEquals(_rules, rules))
        {
            if (!Build(rules, source))
            {
                return null;
            }
        }
        var local = Player.m_localPlayer;
        if (local != null && !ReferenceEquals(local, _resolvedFor))
        {
            Resolve(local);
        }
        return _attack;
    }

    // Watchdog verdict: option failed in game. Only once per option (a bash already using the next one is ignored).
    internal static void FallBack(BashAnimationKind failed, string reason)
    {
        if (_attack == null || failed != _mode)
        {
            return;
        }
        var next = Next(failed);
        if (next == null)
        {
            if (!_lastResortReported)
            {
                _lastResortReported = true;
                Log.Error($"{reason}. {failed} is the last animation to fall back to, so the shield bash may not work "
                          + "in this game version; try another BashAnimation.");
            }
            return;
        }
        Log.Warning($"{reason}; using {next.Value}.");
        Warned.Add(failed);
        _mode = next.Value;
        var local = Player.m_localPlayer;
        if (local != null)
        {
            Resolve(local);
        }
        else
        {
            _attack.m_attackAnimation = TriggerOf(_mode, _rules);
        }
    }

    internal static string TriggerOf(BashAnimationKind kind, TowerRules rules)
    {
        switch (kind)
        {
            case BashAnimationKind.ShieldPunch:
                return ShieldArmTrigger;
            case BashAnimationKind.OtherPunch:
                return OtherArmTrigger;
            case BashAnimationKind.Kick:
                return KickTrigger;
            case BashAnimationKind.Custom:
                return rules != null ? rules.BashCustomTrigger : "";
            default:
                return OtherArmTrigger;
        }
    }

    internal static BashAnimationKind? Next(BashAnimationKind kind)
    {
        switch (kind)
        {
            case BashAnimationKind.Custom:
                return BashAnimationKind.ShieldPunch;
            case BashAnimationKind.ShieldPunch:
            case BashAnimationKind.Kick:
                return BashAnimationKind.OtherPunch;
            default:
                return null;
        }
    }

    // Every trigger a player item fire (Attack.Start: name + chain level, or name + random, or name as is): the
    // allowed Custom names. Built once per ObjectDB. Player items = items with an icon (creature attack items have
    // none), plus the player's unarmed attacks. Listed towers count with their vanilla attack.
    // Names of held (looping), aimed (bow draw), reloaded or attached attacks are left out, even when another item
    // fire them plainly: their clip wait for a button, a bool or a target the bash never give. A looping clip leave
    // only on attack_abort, which Attack.Stop fire only for a looping attack (the bash clone is not): the bearer would
    // hang in it (staff_rapidfire).
    internal static HashSet<string> AttackTriggers()
    {
        var db = ObjectDB.instance;
        if (_attackTriggers != null && ReferenceEquals(db, _triggersFor))
        {
            return _attackTriggers;
        }
        var set = new HashSet<string>(StringComparer.Ordinal);
        var held = new HashSet<string>(StringComparer.Ordinal);
        if (db != null && db.m_items != null)
        {
            foreach (var go in db.m_items)
            {
                var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                var s = drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
                if (s == null || s.m_icons == null || s.m_icons.Length == 0)
                {
                    continue;
                }
                var snap = TowerCatalog.SnapshotOfPrefab(go);
                AddTriggers(set, held, snap != null ? snap.Attack : s.m_attack);
                AddTriggers(set, held, s.m_secondaryAttack);
            }
        }
        var player = Player.m_localPlayer;
        if (player != null && player.m_unarmedWeapon != null && player.m_unarmedWeapon.m_itemData != null)
        {
            var unarmed = player.m_unarmedWeapon.m_itemData.m_shared;
            AddTriggers(set, held, unarmed.m_attack);
            AddTriggers(set, held, unarmed.m_secondaryAttack);
        }
        set.ExceptWith(held);
        _attackTriggers = set;
        _heldTriggers = held;
        _triggersFor = db;
        return set;
    }

    // Triggers refused because a held, aimed, reloaded or attached attack fire them (built with AttackTriggers).
    internal static HashSet<string> HeldTriggers()
    {
        AttackTriggers();
        return _heldTriggers;
    }

    private static void AddTriggers(HashSet<string> set, HashSet<string> held, Attack attack)
    {
        if (attack == null || string.IsNullOrEmpty(attack.m_attackAnimation))
        {
            return;
        }
        if (attack.m_loopingAttack || attack.m_bowDraw || attack.m_requiresReload || attack.m_attach)
        {
            set = held;
        }
        var name = attack.m_attackAnimation;
        if (attack.m_attackChainLevels > 1)
        {
            for (var i = 0; i < attack.m_attackChainLevels; i++)
            {
                set.Add(name + i);
            }
        }
        else if (attack.m_attackRandomAnimations >= 2)
        {
            for (var i = 0; i < attack.m_attackRandomAnimations; i++)
            {
                set.Add(name + i);
            }
        }
        else
        {
            set.Add(name);
        }
    }

    // Clone of the unarmed primary with the bash values (design 2.6 table). Kept from unarmed: attack type, speed
    // factors, heights, adrenaline, skill raise, hit type, noise, effect lists (never changed).
    private static bool Build(TowerRules rules, Humanoid source)
    {
        Humanoid h = source != null ? source : Player.m_localPlayer;
        if (h == null || h.m_unarmedWeapon == null || h.m_unarmedWeapon.m_itemData == null
            || h.m_unarmedWeapon.m_itemData.m_shared == null || h.m_unarmedWeapon.m_itemData.m_shared.m_attack == null)
        {
            return false;
        }
        var a = h.m_unarmedWeapon.m_itemData.m_shared.m_attack.Clone();
        a.m_attackAnimation = TriggerOf(rules.BashAnimation, rules);
        a.m_attackChainLevels = 0;
        a.m_attackRandomAnimations = 0;
        a.m_attackStamina = rules.BashStamina;
        a.m_staggerMultiplier = rules.BashStagger;
        a.m_forceMultiplier = 1f;
        a.m_attackRange = rules.BashRange;
        a.m_attackAngle = rules.BashAngle;
        a.m_attackRayWidth = RayWidth;
        // Every creature in the arc take full damage and push (no vanilla split); only one keep the heavy stagger
        // multiplier (BashTarget, decision 35).
        a.m_multiHit = true;
        a.m_lowerDamagePerHit = false;
        a.m_hitTerrain = false;
        _attack = a;
        _rules = rules;
        _mode = rules.BashAnimation;
        _resolvedFor = null;
        Warned.Clear();
        _lastResortReported = false;
        return true;
    }

    // Check the option in use on this player's animator; refused = Warning (once per option) and next option.
    private static void Resolve(Player player)
    {
        _resolvedFor = player;
        if (_attack == null || player.m_zanim == null)
        {
            return;
        }
        var kind = _mode;
        for (var guard = 0; guard < 6; guard++)
        {
            var trigger = TriggerOf(kind, _rules);
            var problem = Check(kind, trigger, player.m_zanim);
            if (problem == null)
            {
                break;
            }
            var next = Next(kind);
            if (next == null)
            {
                if (!_lastResortReported)
                {
                    _lastResortReported = true;
                    Log.Error($"{problem}. {kind} is the last animation to fall back to, so the shield bash may not "
                              + "work in this game version.");
                }
                break;
            }
            if (Warned.Add(kind))
            {
                Log.Warning($"{problem}; using {next.Value}.");
            }
            kind = next.Value;
        }
        _mode = kind;
        _attack.m_attackAnimation = TriggerOf(kind, _rules);
    }

    // Null = fine. Text = why this option cannot be used (player-facing log text).
    private static string Check(BashAnimationKind kind, string trigger, ZSyncAnimation zanim)
    {
        if (kind == BashAnimationKind.Custom && string.IsNullOrEmpty(trigger))
        {
            return "BashAnimation is Custom, but BashCustomTrigger is empty";
        }
        if (!zanim.HasParameter(trigger, AnimatorControllerParameterType.Trigger))
        {
            return kind == BashAnimationKind.Custom
                ? $"BashCustomTrigger '{trigger}' is not a player attack animation"
                : $"The bash animation {kind} ('{trigger}') does not exist in this game version";
        }
        if (kind == BashAnimationKind.Custom && !AttackTriggers().Contains(trigger))
        {
            return HeldTriggers().Contains(trigger)
                ? $"BashCustomTrigger '{trigger}' is the animation of an attack that is held, aimed or reloaded, "
                  + "which a bash cannot play"
                : $"BashCustomTrigger '{trigger}' is not a player attack animation";
        }
        return null;
    }
}
