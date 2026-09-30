using System.Text;
using UnityEngine;

namespace MC.Combat.WeaponsMovesetMod;

// Me = turn per-swing Attack clone into a move (design 2.5). Only the clone: item SharedData never touched, every
// swing get fresh clone from StartAttack, so nothing compound and toggle has nothing to put back.
//   m_attackAnimation        = trigger (full name, chain levels 0: fired as is, vanilla facing, no finisher x2)
//   m_attackChainLevels      = 0
//   m_attackRandomAnimations = 0
//   m_damageMultiplier      *= Damage
//   m_staggerMultiplier      = min(m_staggerMultiplier x Stagger, 99)   (100 up = vanilla instant stagger)
//   m_forceMultiplier       *= Push
//   m_attackStamina         *= Stamina
//   m_maxYAngle              = max(m_maxYAngle, AimAngle)              (jump attack only; MoveTracker put it back on
//                                                                       landing)
// Multiply, not set: weapon keep own values, and edits of mods before us (Dual Wielding) compose.
internal static class MoveEdit
{
    // Character.RPC_Damage stagger at once from 100 up. Move stay under it.
    internal const float MaxStagger = 99f;

    // Stamina fallback, then edits. False = clone left vanilla (player can pay normal swing but not the move: better
    // a normal swing than vanilla retry the failing move every tick for 0.5 s and flash the bar each time).
    internal static bool Apply(Attack clone, Humanoid character, ItemDrop.ItemData weapon, MoveKind kind,
        string trigger, MoveRules rules)
    {
        var n = rules != null ? rules.Numbers(kind) : null;
        if (clone == null || character == null || weapon == null || n == null || string.IsNullOrEmpty(trigger))
        {
            return false;
        }
        if (!CanPay(clone, character, weapon, n.Stamina))
        {
            return false;
        }
        clone.m_attackAnimation = trigger;
        clone.m_attackChainLevels = 0;
        clone.m_attackRandomAnimations = 0;
        clone.m_damageMultiplier *= n.Damage;
        clone.m_staggerMultiplier = Stagger(clone.m_staggerMultiplier, n.Stagger);
        clone.m_forceMultiplier *= n.Push;
        clone.m_attackStamina *= n.Stamina;
        if (kind == MoveKind.Jump)
        {
            clone.m_maxYAngle = Mathf.Max(clone.m_maxYAngle, rules.AimAngle);
        }
        return true;
    }

    // Move stagger never reach vanilla instant stagger (100) unless swing already had it (modded weapon: me no take
    // its own rule away).
    internal static float Stagger(float own, float multiplier)
    {
        var value = own * multiplier;
        var cap = own >= 100f ? own : MaxStagger;
        return value > cap ? cap : value;
    }

    // Same test as Attack.Start (cost + 0.1). Me set m_character and m_weapon now (Start set them first thing anyway,
    // GetAttackStamina read them), then ask clone own cost with and without the multiplier (equipment, status effects,
    // skill all in). True = do the move: player pay it, or cannot pay even a normal swing (then vanilla refuse both
    // the same way).
    private static bool CanPay(Attack clone, Humanoid character, ItemDrop.ItemData weapon, float multiplier)
    {
        if (multiplier <= 1f || clone.m_attackStamina <= 0f)
        {
            return true;
        }
        clone.m_character = character;
        clone.m_weapon = weapon;
        var own = clone.m_attackStamina;
        var normal = clone.GetAttackStamina();
        float move;
        clone.m_attackStamina = own * multiplier;
        try
        {
            move = clone.GetAttackStamina();
        }
        finally
        {
            clone.m_attackStamina = own;
        }
        if (move <= normal || move <= 0f || character.HaveStamina(move + 0.1f))
        {
            return true;
        }
        var canNormal = normal <= 0f || character.HaveStamina(normal + 0.1f);
        return !canNormal;
    }

    // Debug line of a started move (design 2.8). Built only when a move start, never per tick. rollText: roll attack's
    // "cut into the roll ..." / "... after the roll ended ..." part (MoveTracker build it), null for a jump attack.
    internal static string Describe(MoveKind kind, ItemDrop.ItemData weapon, WeaponFamily family, string trigger,
        MoveRules rules, string rollText, RulesSource source)
    {
        var n = rules.Numbers(kind);
        var sb = new StringBuilder();
        sb.Append(Moves.Title(kind)).Append(": ").Append(ItemName(weapon)).Append(" (").Append(Families.Key(family))
            .Append(") plays ").Append(trigger);
        if (n != null)
        {
            sb.Append("; damage x").Append(MoveRules.F(n.Damage)).Append(", stagger x").Append(MoveRules.F(n.Stagger))
                .Append(", push x").Append(MoveRules.F(n.Push)).Append(", stamina x").Append(MoveRules.F(n.Stamina));
        }
        if (kind == MoveKind.Jump)
        {
            sb.Append(", aim ").Append(MoveRules.F(rules.AimAngle)).Append('°');
        }
        else if (kind == MoveKind.Roll && !string.IsNullOrEmpty(rollText))
        {
            sb.Append("; ").Append(rollText);
        }
        sb.Append("; ").Append(SourceText(source)).Append(" settings.");
        return sb.ToString();
    }

    internal static string SourceText(RulesSource source)
    {
        switch (source)
        {
            case RulesSource.Server:
                return "server";
            case RulesSource.SelfTest:
                return "self-test";
            case RulesSource.Pending:
                return "pending";
            default:
                return "own";
        }
    }

    // Prefab name (SwordIron) for logs; bare hands have no prefab: item name.
    internal static string ItemName(ItemDrop.ItemData item)
    {
        if (item == null)
        {
            return "no item";
        }
        if (item.m_dropPrefab != null)
        {
            return item.m_dropPrefab.name;
        }
        return item.m_shared != null && !string.IsNullOrEmpty(item.m_shared.m_name) ? item.m_shared.m_name : "item";
    }
}
