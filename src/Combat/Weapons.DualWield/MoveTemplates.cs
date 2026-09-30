using System;
using System.Collections.Generic;
using System.Text;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// One vanilla item whose moves a pair borrow (design 2.5): primary attack (combo), special (or null), stance.
// Me hold the prefab's own Attack objects (read only, never changed).
internal sealed class MoveTemplate
{
    internal readonly string PrefabName;
    internal readonly Attack Primary;
    // Null = item has no special the player animator can play: pair use main weapon own special (as MainWeapon).
    internal readonly Attack Secondary;
    internal readonly ItemDrop.ItemData.AnimationState Stance;
    // Special stamina / primary stamina of the item (Berserkir 32 / 16 = 2, Skoll and Hati 42 / 14 = 3).
    internal readonly float SpecialRatio;
    // Log text: "AxeBerzerkr (dualaxes0-3, special dualaxes_secondary)".
    internal readonly string Summary;

    internal MoveTemplate(string prefabName, Attack primary, Attack secondary,
        ItemDrop.ItemData.AnimationState stance, string summary)
    {
        PrefabName = prefabName;
        Primary = primary;
        Secondary = secondary;
        Stance = stance;
        SpecialRatio = secondary != null && primary.m_attackStamina > 0f
            ? secondary.m_attackStamina / primary.m_attackStamina
            : 1f;
        Summary = summary;
    }
}

// Me = the two move templates (design 2.5, G6, E3): PairMoves (default AxeBerzerkr) for every pair, KnifePairMoves
// (default KnifeSkollAndHati) when both weapons are knives. Read from ObjectDB at runtime, checked against the local
// player's animator (every trigger the attack fire must exist). Bad setting = default item + warning once; default
// bad too (future game) = null: pair swing with main weapon own moves and stance, off hand never strike, warning once.
// Item whose moves are not dual moves (a sword, an axe): valid; DualSwing hand fallback make both weapons strike.
// Cache keyed to rules object, ObjectDB and local player (reference compares): rebuilt when one change.
// HasParameter copy the animator's parameter array: only here, at rebuild, never per swing.
internal static class MoveTemplates
{
    private static bool _built;
    private static DualRules _rules;
    private static ObjectDB _db;
    private static Player _player;
    private static MoveTemplate _pair;
    private static MoveTemplate _knife;
    private static string _lastInfo;
    private static readonly HashSet<string> Warned = new HashSet<string>();

    // Template for this pair, or null (no valid moves: pair swing like the main weapon alone).
    internal static MoveTemplate For(ItemDrop.ItemData main, ItemDrop.ItemData off, DualRules rules, Player player)
    {
        Refresh(rules, player);
        return IsKnifePair(main, off) ? _knife : _pair;
    }

    internal static bool IsKnifePair(ItemDrop.ItemData main, ItemDrop.ItemData off) =>
        main != null && off != null && main.m_shared.m_skillType == Skills.SkillType.Knives
        && off.m_shared.m_skillType == Skills.SkillType.Knives;

    // Rules, ObjectDB or player changed = build again. No ObjectDB or player yet = nothing (built again when they come).
    internal static void Refresh(DualRules rules, Player player)
    {
        var db = ObjectDB.instance;
        if (_built && ReferenceEquals(rules, _rules) && ReferenceEquals(db, _db) && ReferenceEquals(player, _player))
        {
            return;
        }
        _pair = null;
        _knife = null;
        // Animator without parameters = not set up yet: no cache, no warning (every trigger would look missing).
        // Try again at next call.
        if (rules == null || db == null || player == null || player.m_zanim == null || player.m_animator == null
            || player.m_animator.parameterCount == 0)
        {
            _built = false;
            return;
        }
        _built = true;
        _rules = rules;
        // New ObjectDB = new world entered: Info line again even when the moves are the same as last world.
        if (!ReferenceEquals(db, _db))
        {
            _lastInfo = null;
        }
        _db = db;
        _player = player;
        _pair = Resolve(rules.PairMoves, DualRules.DefaultPairMoves, "PairMoves", player);
        _knife = Resolve(rules.KnifePairMoves, DualRules.DefaultKnifePairMoves, "KnifePairMoves", player);
        var info = Describe(_pair, _knife);
        if (info != _lastInfo)
        {
            _lastInfo = info;
            Log.Info(info);
        }
    }

    // OnDeactivated: cache and last Info line forgotten (turned on again = Info line again).
    internal static void Clear()
    {
        _built = false;
        _rules = null;
        _db = null;
        _player = null;
        _pair = null;
        _knife = null;
        _lastInfo = null;
    }

    // Stamina of one pair swing (design 2.6, D9): higher primary cost of the two weapons, times the template's special
    // ratio for the special, times SwingStamina percent. Vanilla GetAttackStamina then apply equipment, status
    // effects and the main weapon's skill as usual.
    internal static float Stamina(ItemDrop.ItemData main, ItemDrop.ItemData off, MoveTemplate template, bool special,
        DualRules rules)
    {
        var cost = Mathf.Max(main.m_shared.m_attack.m_attackStamina, off.m_shared.m_attack.m_attackStamina);
        var ratio = special ? template.SpecialRatio : 1f;
        return cost * ratio * rules.SwingStamina / 100f;
    }

    // Template-owned fields of a move (design 2.5). Me read all into this struct first, then write them in one block:
    // exception while reading = clone untouched.
    internal struct Shape
    {
        private string _animation;
        private int _chainLevels;
        private int _randomAnimations;
        private Attack.AttackType _type;
        private float _range;
        private float _angle;
        private float _rayWidth;
        private float _rayWidthCharExtra;
        private float _height;
        private float _heightChar1;
        private float _heightChar2;
        private float _offset;
        private float _maxYAngle;
        private Attack.HitPointType _hitPointType;
        private bool _multiHit;
        private bool _lowerDamagePerHit;
        private bool _hitThroughWalls;
        private DestructibleType _resetChainIfHit;
        private float _speedFactor;
        private float _speedFactorRotation;
        private float _startNoise;
        private float _hitNoise;
        private float _adrenaline;
        private float _damageMultiplier;
        private float _forceMultiplier;
        private float _staggerMultiplier;
        private float _stamina;

        internal static Shape Read(Attack t, float stamina)
        {
            return new Shape
            {
                _animation = t.m_attackAnimation,
                _chainLevels = t.m_attackChainLevels,
                _randomAnimations = t.m_attackRandomAnimations,
                _type = t.m_attackType,
                _range = t.m_attackRange,
                _angle = t.m_attackAngle,
                _rayWidth = t.m_attackRayWidth,
                _rayWidthCharExtra = t.m_attackRayWidthCharExtra,
                _height = t.m_attackHeight,
                _heightChar1 = t.m_attackHeightChar1,
                _heightChar2 = t.m_attackHeightChar2,
                _offset = t.m_attackOffset,
                _maxYAngle = t.m_maxYAngle,
                _hitPointType = t.m_hitPointtype,
                _multiHit = t.m_multiHit,
                _lowerDamagePerHit = t.m_lowerDamagePerHit,
                _hitThroughWalls = t.m_hitThroughWalls,
                _resetChainIfHit = t.m_resetChainIfHit,
                _speedFactor = t.m_speedFactor,
                _speedFactorRotation = t.m_speedFactorRotation,
                _startNoise = t.m_attackStartNoise,
                _hitNoise = t.m_attackHitNoise,
                _adrenaline = t.m_attackAdrenaline,
                _damageMultiplier = t.m_damageMultiplier,
                _forceMultiplier = t.m_forceMultiplier,
                _staggerMultiplier = t.m_staggerMultiplier,
                _stamina = stamina,
            };
        }

        // Plain field writes: cannot fail half way.
        internal void WriteTo(Attack c)
        {
            c.m_attackAnimation = _animation;
            c.m_attackChainLevels = _chainLevels;
            c.m_attackRandomAnimations = _randomAnimations;
            c.m_attackType = _type;
            c.m_attackRange = _range;
            c.m_attackAngle = _angle;
            c.m_attackRayWidth = _rayWidth;
            c.m_attackRayWidthCharExtra = _rayWidthCharExtra;
            c.m_attackHeight = _height;
            c.m_attackHeightChar1 = _heightChar1;
            c.m_attackHeightChar2 = _heightChar2;
            c.m_attackOffset = _offset;
            c.m_maxYAngle = _maxYAngle;
            c.m_hitPointtype = _hitPointType;
            c.m_multiHit = _multiHit;
            c.m_lowerDamagePerHit = _lowerDamagePerHit;
            c.m_hitThroughWalls = _hitThroughWalls;
            c.m_resetChainIfHit = _resetChainIfHit;
            c.m_speedFactor = _speedFactor;
            c.m_speedFactorRotation = _speedFactorRotation;
            c.m_attackStartNoise = _startNoise;
            c.m_attackHitNoise = _hitNoise;
            c.m_attackAdrenaline = _adrenaline;
            c.m_damageMultiplier = _damageMultiplier;
            c.m_forceMultiplier = _forceMultiplier;
            c.m_staggerMultiplier = _staggerMultiplier;
            c.m_attackStamina = _stamina;
        }
    }

    private static MoveTemplate Resolve(string wanted, string fallback, string setting, Player player)
    {
        var template = TryBuild(wanted, player, out var reason);
        if (template != null)
        {
            return template;
        }
        if (!string.Equals(wanted, fallback, StringComparison.Ordinal))
        {
            WarnOnce($"Moves.{setting} = {(string.IsNullOrEmpty(wanted) ? "(empty)" : wanted)}: {reason}. The default {fallback} is used instead.");
            template = TryBuild(fallback, player, out reason);
            if (template != null)
            {
                return template;
            }
        }
        WarnOnce($"Moves.{setting}: the default moves {fallback} cannot be used ({reason}). Such pairs swing with "
                 + "their main weapon's own moves and stance, and the off-hand weapon never strikes.");
        return null;
    }

    // Item exist, primary is a melee swing, every trigger it fire exist in the player animator. Special checked same
    // way; bad special = null (no warning: pair then use main weapon own special, Info line say so).
    private static MoveTemplate TryBuild(string name, Player player, out string reason)
    {
        reason = null;
        if (string.IsNullOrEmpty(name))
        {
            reason = "no item given";
            return null;
        }
        var prefab = ObjectDB.instance.GetItemPrefab(name);
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
        {
            reason = "no item with this name in the game";
            return null;
        }
        var shared = drop.m_itemData.m_shared;
        if (!IsMelee(shared.m_attack))
        {
            reason = "its attack is not a melee swing";
            return null;
        }
        var missing = MissingTrigger(shared.m_attack, player);
        if (missing != null)
        {
            reason = $"the player animations have no '{missing}' move";
            return null;
        }
        var secondary = IsMelee(shared.m_secondaryAttack) && MissingTrigger(shared.m_secondaryAttack, player) == null
            ? shared.m_secondaryAttack
            : null;
        var summary = new StringBuilder(name).Append(" (").Append(Triggers(shared.m_attack)).Append(", special ")
            .Append(secondary != null ? Triggers(secondary) : "the main weapon's own").Append(')').ToString();
        return new MoveTemplate(name, shared.m_attack, secondary, shared.m_animationState, summary);
    }

    private static bool IsMelee(Attack a) =>
        a != null && !string.IsNullOrEmpty(a.m_attackAnimation)
                  && (a.m_attackType == Attack.AttackType.Horizontal || a.m_attackType == Attack.AttackType.Vertical);

    // Same names Attack.Start fire: chain = name0..name(n-1), random = name0..name(r-1), else name alone.
    // First name the animator lack, or null.
    private static string MissingTrigger(Attack a, Player player)
    {
        var zanim = player.m_zanim;
        var count = a.m_attackChainLevels > 1 ? a.m_attackChainLevels
            : a.m_attackRandomAnimations >= 2 ? a.m_attackRandomAnimations : 0;
        if (count == 0)
        {
            return zanim.HasParameter(a.m_attackAnimation, AnimatorControllerParameterType.Trigger)
                ? null
                : a.m_attackAnimation;
        }
        for (var i = 0; i < count; i++)
        {
            var trigger = a.m_attackAnimation + i;
            if (!zanim.HasParameter(trigger, AnimatorControllerParameterType.Trigger))
            {
                return trigger;
            }
        }
        return null;
    }

    private static string Triggers(Attack a)
    {
        var count = a.m_attackChainLevels > 1 ? a.m_attackChainLevels
            : a.m_attackRandomAnimations >= 2 ? a.m_attackRandomAnimations : 0;
        return count == 0 ? a.m_attackAnimation : $"{a.m_attackAnimation}0-{count - 1}";
    }

    private static string Describe(MoveTemplate pair, MoveTemplate knife)
    {
        var sb = new StringBuilder("Pairs use the moves of ");
        sb.Append(pair != null ? pair.Summary : "their main weapon (no dual moves found)");
        sb.Append("; two knives use ");
        sb.Append(knife != null ? knife.Summary : "the moves of their main knife (no dual moves found)");
        sb.Append('.');
        return sb.ToString();
    }

    private static void WarnOnce(string text)
    {
#if DEBUG
        LastWarning = text;
#endif
        if (Warned.Add(text))
        {
            Log.Warning(text);
        }
    }

#if DEBUG
    // Self test read me: last template warning (also when already logged once before).
    internal static string LastWarning;
#endif
}
