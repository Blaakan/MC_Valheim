using System.Collections.Generic;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// Which weapon strike one hit event.
internal enum Hand : byte
{
    Main,
    Off,
    Both,
}

// Me = the hits of a pair swing (design 2.6, G7, E5).
//   Attack.Start postfix: record swing = clone, main, off, hand pattern of the trigger really fired (plus its chain
//   step, for a trigger not in the table), event index 0.
//   Attack.DoMeleeAttack prefix: our clone? (one reference compare, only cost for every other attack) -> hand of this
//   event = HandAt(pattern, index), index + 1.
//     Main: nothing change.
//     Off:  save weapon-owned fields of the clone, put off-hand weapon's in (m_weapon too), finalizer put them back.
//     Both: main hand hit scaled by BothHandsDamage (and half skill raise), then postfix put off-hand weapon's fields
//           in (same scaling), zero adrenaline and snow shovel for this call, call DoMeleeAttack once more (re-entry
//           flag), finalizer put everything back.
// Saved fields live in static struct (_saved), guarded by re-entry flag; not Harmony __state (postfix second call
// need them too). Pattern depend only on the fired trigger: skipped or doubled event never shift later swings.
internal static class DualSwing
{
    // Hand table (design 2.6), fixed: chain triggers by base name and level; one-off names (Weapon Moveset move with
    // chain levels 0) by full name. Trigger not here (other PairMoves item, Moveset move outside dual set) = null:
    // HandAt use the fallback rule (alternate over the combo, or both on a single move).
    private static readonly Hand[] M = { Hand.Main };
    private static readonly Hand[] O = { Hand.Off };
    private static readonly Hand[] B = { Hand.Both };
    private static readonly Hand[] MO = { Hand.Main, Hand.Off };

    private static readonly Dictionary<string, Hand[][]> ChainPatterns = new Dictionary<string, Hand[][]>
    {
        { "dualaxes", new[] { M, O, MO, MO } },
        { "dual_knives", new[] { M, O, B } },
    };

    private static readonly Dictionary<string, Hand[]> NamePatterns = new Dictionary<string, Hand[]>
    {
        { "dualaxes0", M },
        { "dualaxes1", O },
        { "dualaxes2", MO },
        { "dualaxes3", MO },
        { "dualaxes_secondary", B },
        { "dual_knives0", M },
        { "dual_knives1", O },
        { "dual_knives2", B },
        { "dual_knives_secondary", B },
    };

    // Pure (self test hammer it): pattern of the trigger Attack.Start fired. Chain (levels > 1): base name + level.
    // Else the name alone (vanilla no keep random suffix: such a name stay unknown).
    internal static Hand[] PatternFor(string animation, int chainLevels, int level)
    {
        if (string.IsNullOrEmpty(animation))
        {
            return null;
        }
        if (chainLevels > 1)
        {
            return ChainPatterns.TryGetValue(animation, out var steps) && level >= 0 && level < steps.Length
                ? steps[level]
                : null;
        }
        return NamePatterns.TryGetValue(animation, out var pattern) ? pattern : null;
    }

    // Pure: hand of event number index. BothHands = both, always. Known trigger = its pattern; event past the pattern
    // end (duplicated event) = main. Unknown trigger (pattern null, design 2.6, E3): chain move (chainStep >= 0) =
    // alternate over the combo, step + event even = main, odd = off, so one-event swings change hand every step
    // (sword combo: main, off, main); single move (chainStep < 0: special, one-off, random) = both on every event,
    // like the dual specials. Vanilla non-dual clips have one hit event: plain "alternate inside swing" = off hand
    // never strike.
    internal static Hand HandAt(Hand[] pattern, int index, bool bothHands, int chainStep)
    {
        if (bothHands)
        {
            return Hand.Both;
        }
        if (pattern == null)
        {
            if (chainStep < 0)
            {
                return Hand.Both;
            }
            return (chainStep + index) % 2 == 0 ? Hand.Main : Hand.Off;
        }
        return index >= 0 && index < pattern.Length ? pattern[index] : Hand.Main;
    }

    // Pure: chain step HandAt need for a trigger not in the table. Chain move = its level, else -1 (single move).
    internal static int ChainStep(int chainLevels, int level) => chainLevels > 1 ? Mathf.Max(0, level) : -1;

    // ---------- recorded swing ----------

    private static Attack _clone;
    private static Player _player;
    private static ItemDrop.ItemData _main;
    private static ItemDrop.ItemData _off;
    private static Hand[] _pattern;
    private static int _chainStep;      // ChainStep of the fired trigger (fallback rule only)
    private static int _index;
    private static bool _special;
    private static bool _bothHandsMode;
    private static float _offFactor;    // OffHandDamage / 100
    private static float _bothFactor;   // BothHandsDamage / 100

    // Clone the Attack.Start prefix just converted; Attack.Start postfix record it when Start say yes.
    private static Attack _converted;
    private static bool _convertedSpecial;

    // Cheap check for the Attack.Start postfix (every attack of every character on this game).
    internal static bool HasConverted => _converted != null;

    internal static Attack RecordedClone => _clone;

    // Attack.Start prefix (Priority.High, before Weapon Moveset), inside local player's StartAttack: pair swing with
    // its main weapon = template move copied into the clone (template-owned fields + stamina), before vanilla pick the
    // trigger. Special only with SecondaryMoves = PairMoves and a template special; else main weapon own special,
    // untouched (main hand only). After a swap: previousAttack = null (combo start again).
    internal static void TryConvert(Attack clone, Player player, ItemDrop.ItemData weapon, ref Attack previousAttack)
    {
        _converted = null;
        var right = player.m_rightItem;
        var left = player.m_leftItem;
        if (weapon == null || !ReferenceEquals(weapon, right) || left == null || !Eligibility.IsEligible(right)
            || !Eligibility.IsEligible(left))
        {
            return;
        }
        var rules = ServerRules.Current;
        var template = MoveTemplates.For(right, left, rules, player);
        if (template == null)
        {
            return;
        }
        var special = Hands.SecondaryRequested;
        Attack source;
        if (special)
        {
            if (rules.SecondaryMoves != SecondaryMovesMode.PairMoves || template.Secondary == null)
            {
                return;
            }
            source = template.Secondary;
        }
        else
        {
            source = template.Primary;
        }
        // Locals first (struct), then one block of writes: exception while reading = clone as vanilla made it.
        var shape = MoveTemplates.Shape.Read(source, MoveTemplates.Stamina(right, left, template, special, rules));
        shape.WriteTo(clone);
        // Swap since last converted swing = combo start again. Flag stay up until a converted swing really start
        // (OnStarted): swing refused (no stamina) no eat it, next try start from step 0 too.
        if (Hands.ComboRestart)
        {
            previousAttack = null;
        }
        _converted = clone;
        _convertedSpecial = special;
    }

    // Attack.Start postfix. Start refused (stamina, ...) = nothing recorded, old record stay (harmless: its clone
    // never hit again, StartAttack stopped it).
    internal static void OnStarted(Attack clone, bool started)
    {
        if (!ReferenceEquals(clone, _converted))
        {
            return;
        }
        _converted = null;
        if (!started)
        {
            return;
        }
        Hands.ComboRestart = false;
        var player = Player.m_localPlayer;
        if (player == null)
        {
            return;
        }
        var rules = ServerRules.Current;
        _clone = clone;
        _player = player;
        _main = clone.m_weapon;
        _off = player.m_leftItem;
        _special = _convertedSpecial;
        _pattern = PatternFor(clone.m_attackAnimation, clone.m_attackChainLevels, clone.m_currentAttackCainLevel);
        _chainStep = ChainStep(clone.m_attackChainLevels, clone.m_currentAttackCainLevel);
        _index = 0;
        _bothHandsMode = rules.HitPattern == HitPatternMode.BothHands;
        _offFactor = rules.OffHandDamage / 100f;
        _bothFactor = rules.BothHandsDamage / 100f;
#if DEBUG
        if (Recording)
        {
            _trigger = clone.m_attackChainLevels > 1
                ? clone.m_attackAnimation + clone.m_currentAttackCainLevel
                : clone.m_attackAnimation;
            Swings.Add(new SwingRecord(_trigger, _main, _off, _special, clone.m_attackStamina, Time.time));
        }
#endif
    }

    // ---------- one hit event ----------

    // Weapon-owned fields of the clone (design 2.6) plus what me scale or zero (adrenaline, snow shovel: second call
    // of a both-hands event). Value copy, no alloc.
    private struct WeaponFields
    {
        internal ItemDrop.ItemData Weapon;
        internal float DamageMultiplier;
        internal float ForceMultiplier;
        internal float RaiseSkillAmount;
        internal Skills.SkillType SpecialHitSkill;
        internal DestructibleType SpecialHitType;
        internal DestructibleType SkillHitType;
        internal float DamagePerMissingHp;
        internal float DamageByTotalHealthMissing;
        internal float HealthReturnHit;
        internal float EitrAdd;
        internal GameObject SpawnOnHit;
        internal float SpawnOnHitChance;
        internal EffectList HitEffect;
        internal EffectList HitTerrainEffect;
        internal EffectList TriggerEffect;
        internal float Adrenaline;
        internal bool SnowShovel;

        internal static WeaponFields Read(Attack a)
        {
            return new WeaponFields
            {
                Weapon = a.m_weapon,
                DamageMultiplier = a.m_damageMultiplier,
                ForceMultiplier = a.m_forceMultiplier,
                RaiseSkillAmount = a.m_raiseSkillAmount,
                SpecialHitSkill = a.m_specialHitSkill,
                SpecialHitType = a.m_specialHitType,
                SkillHitType = a.m_skillHitType,
                DamagePerMissingHp = a.m_damageMultiplierPerMissingHP,
                DamageByTotalHealthMissing = a.m_damageMultiplierByTotalHealthMissing,
                HealthReturnHit = a.m_attackHealthReturnHit,
                EitrAdd = a.m_attackEitrAdd,
                SpawnOnHit = a.m_spawnOnHit,
                SpawnOnHitChance = a.m_spawnOnHitChance,
                HitEffect = a.m_hitEffect,
                HitTerrainEffect = a.m_hitTerrainEffect,
                TriggerEffect = a.m_triggerEffect,
                Adrenaline = a.m_attackAdrenaline,
                SnowShovel = a.m_snowShovel,
            };
        }

        internal void WriteTo(Attack a)
        {
            a.m_weapon = Weapon;
            a.m_damageMultiplier = DamageMultiplier;
            a.m_forceMultiplier = ForceMultiplier;
            a.m_raiseSkillAmount = RaiseSkillAmount;
            a.m_specialHitSkill = SpecialHitSkill;
            a.m_specialHitType = SpecialHitType;
            a.m_skillHitType = SkillHitType;
            a.m_damageMultiplierPerMissingHP = DamagePerMissingHp;
            a.m_damageMultiplierByTotalHealthMissing = DamageByTotalHealthMissing;
            a.m_attackHealthReturnHit = HealthReturnHit;
            a.m_attackEitrAdd = EitrAdd;
            a.m_spawnOnHit = SpawnOnHit;
            a.m_spawnOnHitChance = SpawnOnHitChance;
            a.m_hitEffect = HitEffect;
            a.m_hitTerrainEffect = HitTerrainEffect;
            a.m_triggerEffect = TriggerEffect;
            a.m_attackAdrenaline = Adrenaline;
            a.m_snowShovel = SnowShovel;
        }
    }

    private static WeaponFields _saved;
    private static bool _savedActive;
    private static float _savedMissAdrenaline;
    private static bool _missSaved;
    private static bool _bothPending;
    private static bool _inSecondCall;

    // Hand striking the current DoMeleeAttack call (Debug Character.Damage record read it).
    internal static Hand CurrentHand { get; private set; }

    // DoMeleeAttack prefix. Cheap exit first: not our clone, or our own second call.
    internal static void BeforeHit(Attack clone)
    {
        if (!ReferenceEquals(clone, _clone) || _inSecondCall)
        {
            return;
        }
        var hand = HandAt(_pattern, _index, _bothHandsMode, _chainStep);
        _index++;
        CurrentHand = Hand.Main;
        ForgetHitPoints();
        // Off-hand weapon gone since swing start (unequipped, swapped away): main hand strike instead.
        if (hand != Hand.Main && !OffStillHeld())
        {
            hand = Hand.Main;
        }
        if (hand == Hand.Main)
        {
            RecordHit(clone, Hand.Main);
            return;
        }
        _saved = WeaponFields.Read(clone);
        _savedActive = true;
        if (hand == Hand.Off)
        {
            PutOffHand(clone, _saved.DamageMultiplier * _offFactor, _saved.ForceMultiplier, 1f);
            CurrentHand = Hand.Off;
            RecordHit(clone, Hand.Off);
            return;
        }
        // Both: main hand first, each weapon's share of the event.
        clone.m_damageMultiplier = _saved.DamageMultiplier * _bothFactor;
        clone.m_forceMultiplier = _saved.ForceMultiplier * _bothFactor;
        clone.m_raiseSkillAmount = _saved.RaiseSkillAmount * 0.5f;
        _bothPending = true;
        RecordHit(clone, Hand.Main);
    }

    // DoMeleeAttack postfix: second half of a both-hands event. Adrenaline (hit or miss) counted once per event. Deep
    // North snow cleared once per event too (vanilla end every DoMeleeAttack with the snow shovel sweep). Noise and
    // FreezeFrame run twice but not add up (max noise, frame pause reset).
    internal static void AfterHit(Attack clone)
    {
        if (!_bothPending || !ReferenceEquals(clone, _clone) || _inSecondCall)
        {
            return;
        }
        _bothPending = false;
        if (!OffStillHeld())
        {
            return;
        }
        PutOffHand(clone, _saved.DamageMultiplier * _bothFactor * _offFactor, _saved.ForceMultiplier * _bothFactor, 0.5f);
        clone.m_attackAdrenaline = 0f;
        clone.m_snowShovel = false;
        _savedMissAdrenaline = _player.m_attackMissAdrenaline;
        _missSaved = true;
        _player.m_attackMissAdrenaline = 0f;
        CurrentHand = Hand.Off;
        RecordHit(clone, Hand.Off);
        ForgetHitPoints();
        _inSecondCall = true;
        try
        {
            clone.DoMeleeAttack();
        }
        finally
        {
            _inSecondCall = false;
        }
    }

    // DoMeleeAttack finalizer (also after exception): clone and player as before the event. Inner (second) call and
    // other attacks: nothing.
    internal static void Restore(Attack clone)
    {
        if (_inSecondCall || !ReferenceEquals(clone, _clone))
        {
            return;
        }
        RestoreNow();
    }

    private static void RestoreNow()
    {
        if (_savedActive && _clone != null)
        {
            _saved.WriteTo(_clone);
        }
        _savedActive = false;
        _saved = default;
        if (_missSaved && _player != null)
        {
            _player.m_attackMissAdrenaline = _savedMissAdrenaline;
        }
        _missSaved = false;
        _bothPending = false;
        CurrentHand = Hand.Main;
    }

    private static bool OffStillHeld() =>
        _off != null && _player != null && ReferenceEquals(_player.m_leftItem, _off);

    // Weapon-owned fields of the off-hand weapon (design 2.6): its primary for a combo swing, its special for a
    // special swing when it has one. Damage, force and skill raise scaled from the saved main-hand values.
    private static void PutOffHand(Attack clone, float damage, float force, float raiseFactor)
    {
        var shared = _off.m_shared;
        var src = _special && _off.HaveSecondaryAttack() ? shared.m_secondaryAttack : shared.m_attack;
        clone.m_weapon = _off;
        clone.m_damageMultiplier = damage;
        clone.m_forceMultiplier = force;
        clone.m_raiseSkillAmount = src.m_raiseSkillAmount * raiseFactor;
        clone.m_specialHitSkill = src.m_specialHitSkill;
        clone.m_specialHitType = src.m_specialHitType;
        clone.m_skillHitType = src.m_skillHitType;
        clone.m_damageMultiplierPerMissingHP = src.m_damageMultiplierPerMissingHP;
        clone.m_damageMultiplierByTotalHealthMissing = src.m_damageMultiplierByTotalHealthMissing;
        clone.m_attackHealthReturnHit = src.m_attackHealthReturnHit;
        clone.m_attackEitrAdd = src.m_attackEitrAdd;
        clone.m_spawnOnHit = src.m_spawnOnHit;
        clone.m_spawnOnHitChance = src.m_spawnOnHitChance;
        clone.m_hitEffect = src.m_hitEffect;
        clone.m_hitTerrainEffect = src.m_hitTerrainEffect;
        clone.m_triggerEffect = src.m_triggerEffect;
    }

    // OnDeactivated (and player change): forget swing, clone back as before if a hit was cut short.
    internal static void Clear()
    {
        if (!_inSecondCall)
        {
            RestoreNow();
        }
        _clone = null;
        _player = null;
        _main = null;
        _off = null;
        _pattern = null;
        _chainStep = -1;
        _index = 0;
        _converted = null;
        _inSecondCall = false;
#if DEBUG
        _trigger = null;
#endif
    }

    // ---------- Debug record for the self tests ----------

    [System.Diagnostics.Conditional("DEBUG")]
    private static void RecordHit(Attack clone, Hand hand)
    {
#if DEBUG
        if (Recording)
        {
            Hits.Add(new HitRecord(Swings.Count - 1, _trigger, _index - 1, hand, clone, _player));
        }
#endif
    }

    // New DoMeleeAttack call of our clone: hit point list of the last call no belong to it.
    [System.Diagnostics.Conditional("DEBUG")]
    private static void ForgetHitPoints()
    {
#if DEBUG
        _hitPoints = null;
        _skillFactor = -1f;
#endif
    }

#if DEBUG
    // Self test turn this on, read Swings / Hits / Damages, turn it off. Off = no alloc.
    internal static bool Recording;
    private static string _trigger;

    internal sealed class SwingRecord
    {
        internal readonly string Trigger;
        internal readonly ItemDrop.ItemData Main;
        internal readonly ItemDrop.ItemData Off;
        internal readonly bool Special;
        internal readonly float Stamina;   // clone m_attackStamina when Start said yes (before vanilla modifiers)
        internal readonly float Time;

        internal SwingRecord(string trigger, ItemDrop.ItemData main, ItemDrop.ItemData off, bool special, float stamina,
            float time)
        {
            Trigger = trigger;
            Main = main;
            Off = off;
            Special = special;
            Stamina = stamina;
            Time = time;
        }
    }

    // Clone values while the hit run (weapon-owned fields and the template's chain reset), plus what self test note
    // for unverified points: animator clips on every layer (R2), hand joints (R4), player miss adrenaline (E5).
    internal sealed class HitRecord
    {
        internal readonly int Swing;       // index in Swings (-1 = swing started before recording)
        internal readonly string Trigger;
        internal readonly int Event;
        internal readonly Hand Hand;
        internal readonly ItemDrop.ItemData Weapon;
        internal readonly float DamageMultiplier;
        internal readonly float ForceMultiplier;
        internal readonly float RaiseSkillAmount;
        internal readonly float DamagePerMissingHp;
        internal readonly float SpawnOnHitChance;
        internal readonly GameObject SpawnOnHit;
        internal readonly DestructibleType SpecialHitType;
        internal readonly Skills.SkillType SpecialHitSkill;
        internal readonly DestructibleType ResetChainIfHit;
        internal readonly EffectList HitEffect;
        internal readonly EffectList TriggerEffect;
        internal readonly float Adrenaline;
        internal readonly float MissAdrenaline;
        internal readonly bool SnowShovel;
        internal readonly Vector3 LeftHand;
        internal readonly Vector3 RightHand;
        internal readonly string Clips;
        internal readonly float Time;

        internal HitRecord(int swing, string trigger, int eventIndex, Hand hand, Attack a, Player player)
        {
            Swing = swing;
            Trigger = trigger;
            Event = eventIndex;
            Hand = hand;
            Weapon = a.m_weapon;
            DamageMultiplier = a.m_damageMultiplier;
            ForceMultiplier = a.m_forceMultiplier;
            RaiseSkillAmount = a.m_raiseSkillAmount;
            DamagePerMissingHp = a.m_damageMultiplierPerMissingHP;
            SpawnOnHitChance = a.m_spawnOnHitChance;
            SpawnOnHit = a.m_spawnOnHit;
            SpecialHitType = a.m_specialHitType;
            SpecialHitSkill = a.m_specialHitSkill;
            ResetChainIfHit = a.m_resetChainIfHit;
            HitEffect = a.m_hitEffect;
            TriggerEffect = a.m_triggerEffect;
            Adrenaline = a.m_attackAdrenaline;
            SnowShovel = a.m_snowShovel;
            Time = UnityEngine.Time.time;
            if (player == null)
            {
                Clips = "?";
                return;
            }
            MissAdrenaline = player.m_attackMissAdrenaline;
            var vis = player.m_visEquipment;
            if (vis != null && vis.m_leftHand != null && vis.m_rightHand != null)
            {
                LeftHand = vis.m_leftHand.position;
                RightHand = vis.m_rightHand.position;
            }
            Clips = ClipsOf(player.m_animator);
        }

        // "0:DualAxes Attack 1>next | 1:..." per layer: current clip, and the next one while in a transition.
        private static string ClipsOf(Animator animator)
        {
            if (animator == null)
            {
                return "?";
            }
            var sb = new System.Text.StringBuilder();
            for (var layer = 0; layer < animator.layerCount; layer++)
            {
                if (layer > 0)
                {
                    sb.Append(" | ");
                }
                sb.Append(layer).Append(':');
                AppendClips(sb, animator.GetCurrentAnimatorClipInfo(layer));
                if (animator.IsInTransition(layer))
                {
                    sb.Append('>');
                    AppendClips(sb, animator.GetNextAnimatorClipInfo(layer));
                }
            }
            return sb.ToString();
        }

        private static void AppendClips(System.Text.StringBuilder sb, AnimatorClipInfo[] infos)
        {
            if (infos == null || infos.Length == 0)
            {
                sb.Append('-');
                return;
            }
            for (var i = 0; i < infos.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append('+');
                }
                var clip = infos[i].clip;
                sb.Append(clip != null ? clip.name : "?");
            }
        }
    }

    // Attacker-side damage of one local player hit (Debug Character.Damage prefix), with the swing trigger and event
    // it came from. Vanilla DoMeleeAttack scale each hit by two things that are no ours: the random skill factor (a
    // fresh roll per hit, 0.85-1 at skill 100) and, when the sweep touch more than one object (the target plus the
    // ground, a rock, a tree, another creature) with m_multiHit and m_lowerDamagePerHit, 1 / (objects x 0.75). Me keep
    // both (SkillFactor, Split), so a test can compare hits without them (Base).
    internal sealed class DamageRecord
    {
        internal readonly int Swing;
        internal readonly string Trigger;
        internal readonly int Event;
        internal readonly Hand Hand;
        internal readonly float Total;
        internal readonly Skills.SkillType Skill;
        internal readonly string Target;
        internal readonly float SkillFactor;   // -1 = no roll seen for this hit
        internal readonly int Objects;         // objects the sweep of this DoMeleeAttack call touched (0 = unknown)
        internal readonly string ObjectNames;
        internal readonly float Split;         // vanilla's multi-object factor for this hit (1 = none)
        // Damage by type as the attacker dealt it (before the victim's resistances), and the backstab bonus the hit
        // carried: each tell which weapon struck (frost sword, poison axe, knife backstab).
        internal readonly float Slash;
        internal readonly float Frost;
        internal readonly float Poison;
        internal readonly float Lightning;
        internal readonly float Backstab;
        internal readonly float Time;

        internal DamageRecord(int swing, string trigger, int eventIndex, Hand hand, float total,
            Skills.SkillType skill, string target, float skillFactor, int objects, string objectNames, float split,
            HitData hit)
        {
            Slash = hit.m_damage.m_slash;
            Frost = hit.m_damage.m_frost;
            Poison = hit.m_damage.m_poison;
            Lightning = hit.m_damage.m_lightning;
            Backstab = hit.m_backstabBonus;
            Time = UnityEngine.Time.time;
            Swing = swing;
            Trigger = trigger;
            Event = eventIndex;
            Hand = hand;
            Total = total;
            Skill = skill;
            Target = target;
            SkillFactor = skillFactor;
            Objects = objects;
            ObjectNames = objectNames;
            Split = split;
        }

        // Damage before the random skill factor and the multi-object split: weapon damage x the clone's multiplier
        // (x level, last chain step x2, status effects). -1 = factor or objects not recorded.
        internal float Base => SkillFactor > 0f && Objects > 0 && Split > 0f ? Total / (SkillFactor * Split) : -1f;
    }

    internal static readonly List<SwingRecord> Swings = new List<SwingRecord>();
    internal static readonly List<HitRecord> Hits = new List<HitRecord>();
    internal static readonly List<DamageRecord> Damages = new List<DamageRecord>();

    // Last random skill factor vanilla rolled for the local player (Player.GetRandomSkillFactor postfix): DoMeleeAttack
    // roll it for each object right before it build that object's HitData and call Damage.
    private static float _skillFactor = -1f;

    // Hit point list of the local player's current melee sweep (Attack.AddHitPoint postfix): complete before the
    // first Damage call of that DoMeleeAttack (the rays run first, then the damage loop).
    private static List<Attack.HitPoint> _hitPoints;
    private static bool _hitPointsSplit;

    internal static void ResetRecords()
    {
        Swings.Clear();
        Hits.Clear();
        Damages.Clear();
        _hitPoints = null;
        _skillFactor = -1f;
    }

    // Debug Player.GetRandomSkillFactor postfix, local player, while recording.
    internal static void RecordSkillFactor(float factor) => _skillFactor = factor;

    // Debug Attack.AddHitPoint postfix, local player's attack, while recording. Same list every call of one sweep.
    internal static void RecordHitPoints(Attack attack, List<Attack.HitPoint> list)
    {
        _hitPoints = list;
        _hitPointsSplit = attack.m_multiHit && attack.m_lowerDamagePerHit;
    }

    // Debug Character.Damage prefix: hit from the local player while recording.
    internal static void RecordDamage(Character target, HitData hit)
    {
        if (!Recording || hit == null || !ReferenceEquals(hit.GetAttacker(), Player.m_localPlayer))
        {
            return;
        }
        var objects = _hitPoints != null ? _hitPoints.Count : 0;
        var names = "";
        if (_hitPoints != null)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var point in _hitPoints)
            {
                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(point != null && point.go != null ? point.go.name : "?");
            }
            names = sb.ToString();
        }
        // Vanilla: num5 /= list.Count * 0.75 when m_multiHit, m_lowerDamagePerHit and more than one object.
        var split = _hitPointsSplit && objects > 1 ? 1f / (objects * 0.75f) : 1f;
        Damages.Add(new DamageRecord(Swings.Count - 1, _trigger, _index - 1, CurrentHand, hit.GetTotalDamage(), hit.m_skill,
            target != null ? target.name : "?", _skillFactor, objects, names, split, hit));
        // One roll per hit: a later Damage with no roll of its own (not melee) must not reuse it.
        _skillFactor = -1f;
    }
#endif
}
