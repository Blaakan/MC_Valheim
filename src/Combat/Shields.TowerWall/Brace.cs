using MC.Shared;
using UnityEngine;

namespace MC.Combat.ShieldsTowerWallMod;

// Me = bracing: holding block with a tower shield (design 2.2, 2.5). Local player only.
// Status effect "Braced" (SE_Stats template made at runtime, name <guid>.Brace): on the player while a tower is in the
// left hand (one add per equip, never one per block press: each add clone + analytics event). Re-added at once if
// something else removed it, removed quietly when the tower leave the hand, on deactivation, and forgotten when
// Player.m_localPlayer change (death). Every tick (UpdateBlock postfix, after vanilla wrote m_internalBlockingState)
// its fields follow the state:
//   braced            -> speed -BraceSlow% (every ground speed and turning, clamp at 0), shown on HUD
//   not braced        -> speed 0, hidden
//   braced, stamina for one more full block (reserve)      -> stagger -BraceStaggerResist%, "Braced"
//   braced, stamina at reserve or less (guard break vanilla) -> stagger 0, "Braced (exhausted)", icon flash
// Stagger resist never push the sum with other effects' stagger modifiers below -1 (vanilla add them with no floor).
// Per hit (Character.RPC_Damage prefix, before vanilla block): braced = IsBlocking() live, holds = braced and stamina
// above reserve. Holds decide the stagger resist for this hit and the push (ApplyPushback prefix inside this hit);
// braced + frontal attack hit from a hostile attacker = made blockable (vanilla BlockAttack do the rest).
internal static class Brace
{
    internal const string EffectName = ModInfo.Guid + ".Brace";
    internal const string BracedText = "Braced";
    internal const string ExhaustedText = "Braced (exhausted)";

    // Per-hit decision of the RPC_Damage call running now (saved and put back around nested calls).
    internal struct HitScope
    {
        internal Character Victim;
        internal bool Holds;
    }

    private static SE_Stats _template;
    private static int _hash;
    private static Player _player;
    private static SE_Stats _clone;
    private static HitScope _scope;

    internal static int Hash
    {
        get
        {
            EnsureTemplate();
            return _hash;
        }
    }

    // Braced clone on the local player now (self tests), null = none.
    internal static SE_Stats Clone => _clone;

    internal static HitScope SaveScope() => _scope;

    internal static void RestoreScope(HitScope scope) => _scope = scope;

    // One full block of stamina, as vanilla spend it: BlockAttack with block fraction 1 and equipment modifier, then
    // Player.UseStamina x world stamina rate (StaminaRate key). Status effects not counted: L12.
    internal static float Reserve(Humanoid h) =>
        h.m_blockStaminaDrain * (1f + h.GetEquipmentBlockStaminaModifier()) * Game.m_staminaRate;

    // Per tick upkeep (UpdateBlock postfix) and after a rules apply.
    internal static void Upkeep(Player player)
    {
        if (player == null)
        {
            return;
        }
        if (!ReferenceEquals(player, _player))
        {
            _player = player;
            _clone = null; // old player gone with its effects
        }
        var tower = TowerCatalog.Held(player);
        var rules = TowerSync.Applied;
        if (tower == null || rules == null)
        {
            if (_clone != null)
            {
                Remove(player);
            }
            return;
        }
        var clone = Ensure(player);
        if (clone == null)
        {
            return;
        }
        var braced = player.m_internalBlockingState;
        var holds = braced && player.GetStamina() > Reserve(player);
        clone.m_speedModifier = braced ? -rules.BraceSlowPercent / 100f : 0f;
        clone.m_staggerModifier = holds ? StaggerModifier(player, clone, rules) : 0f;
        clone.m_hidden = !braced;
        var exhausted = braced && !holds;
        clone.m_flashIcon = exhausted;
        clone.m_name = exhausted ? ExhaustedText : BracedText;
        var icons = player.m_leftItem.m_shared.m_icons;
        clone.m_icon = icons != null && icons.Length > 0 ? icons[0] : null;
    }

    // Character.RPC_Damage prefix, victim = local player (owner run RPC_Damage), before vanilla code.
    internal static void OnIncomingHit(Player player, HitData hit, TowerRules rules)
    {
        if (TowerCatalog.Held(player) == null)
        {
            return;
        }
        var braced = player.IsBlocking();
        var holds = braced && player.GetStamina() > Reserve(player);
        _scope.Victim = player;
        _scope.Holds = holds;
        var clone = Ensure(player);
        if (clone != null)
        {
            clone.m_staggerModifier = holds ? StaggerModifier(player, clone, rules) : 0f;
        }
        // (a) Frontal attack hit the game mark unblockable, from a hostile attacker: blockable while braced. Cheapest
        // checks first; attacker lookup last.
        if (!rules.BlockUnblockableAttacks || hit.m_blockable
            || (hit.m_hitType != HitData.HitType.EnemyHit && hit.m_hitType != HitData.HitType.PlayerHit)
            || !hit.HaveAttacker() || hit.m_attacker == player.GetZDOID() || !braced)
        {
            return;
        }
        var flat = hit.m_dir;
        flat.y = 0f;
        if (flat.sqrMagnitude <= 1e-6f || Vector3.Dot(flat, player.transform.forward) >= 0f)
        {
            return;
        }
        if (IsHostile(hit.GetAttacker(), player, hit))
        {
            hit.m_blockable = true;
        }
    }

    // Only an enemy's attack get made blockable: a block clear the hit's status effect (an ally's Staff of Protection
    // bubble would be lost) and give Blocking skill and adrenaline for free.
    //   attacker a Player: only a hit without m_ignorePVP (PvP attack). Aoe put m_ignorePVP on spells that must reach
    //     every player whatever PvP (buffs); other player hits vanilla drop before the block when PvP is off.
    //   other attacker: BaseAI.IsEnemy (faction, tame, group, aggravated), the friend-or-foe test vanilla Aoe use.
    //   attacker gone (null): vanilla drop the hit anyway.
    internal static bool IsHostile(Character attacker, Player victim, HitData hit)
    {
        if (attacker == null)
        {
            return false;
        }
        if (attacker is Player)
        {
            return !hit.m_ignorePVP;
        }
        return BaseAI.IsEnemy(attacker, victim);
    }

    // Stagger modifier while the brace hold: -BraceStaggerResist%, but SEMan.ModifyStagger add every effect's
    // modifier with no floor, so me keep the sum with the others (Fader's power: -50%) at -1 or more: below, a blocked
    // hit would lower the stagger bar. Never above 0. Run only while the brace hold (short list, no allocation).
    private static float StaggerModifier(Player player, SE_Stats clone, TowerRules rules)
    {
        var want = -rules.BraceStaggerResistPercent / 100f;
        if (want >= 0f)
        {
            return 0f;
        }
        var others = 0f;
        foreach (var se in player.m_seman.m_statusEffects)
        {
            if (!ReferenceEquals(se, clone) && se is SE_Stats stats)
            {
                others += stats.m_staggerModifier;
            }
        }
        return Mathf.Min(0f, Mathf.Max(want, -1f - others));
    }

    // Character.ApplyPushback(Vector3, float) prefix, local player: frontal push while the brace hold = scaled. Inside
    // a hit: the holds decided before the block (the hit that break the guard push as vanilla). Outside a hit (area
    // knockback): decided now.
    internal static void ScalePush(Player player, Vector3 dir, ref float pushForce)
    {
        var rules = TowerSync.Applied;
        if (rules == null || rules.BraceKnockbackResistPercent <= 0 || TowerCatalog.Held(player) == null)
        {
            return;
        }
        bool holds;
        if (ReferenceEquals(_scope.Victim, player))
        {
            holds = _scope.Holds;
        }
        else
        {
            holds = player.IsBlocking() && player.GetStamina() > Reserve(player);
        }
        if (!holds)
        {
            return;
        }
        var flat = dir;
        flat.y = 0f;
        if (flat.sqrMagnitude <= 1e-6f || Vector3.Dot(flat, player.transform.forward) >= 0f)
        {
            return;
        }
        pushForce *= 1f - rules.BraceKnockbackResistPercent / 100f;
    }

    // Deactivation step, and upkeep when the tower leave the hand.
    internal static void Remove(Player player)
    {
        if (player != null && player.m_seman != null && _hash != 0)
        {
            player.m_seman.RemoveStatusEffect(_hash, quiet: true);
        }
        _clone = null;
    }

    internal static void Reset()
    {
        _clone = null;
        _player = null;
        _scope = default;
    }

    // Clone on the player (re-added if something else removed it). Null = cannot (no SEMan).
    private static SE_Stats Ensure(Player player)
    {
        var seman = player.m_seman;
        if (seman == null)
        {
            return null;
        }
        EnsureTemplate();
        if (_clone != null && ReferenceEquals(_player, player) && seman.HaveStatusEffect(_hash))
        {
            return _clone;
        }
        _player = player;
        _clone = seman.GetStatusEffect(_hash) as SE_Stats;
        if (_clone == null)
        {
            _clone = seman.AddStatusEffect(_template) as SE_Stats;
        }
        return _clone;
    }

    // Template lives whole process (clones share its native object: MemberwiseClone), fields follow the rules on
    // each clone. Never registered in ObjectDB: local add only.
    private static void EnsureTemplate()
    {
        if (_template != null)
        {
            return;
        }
        var se = ScriptableObject.CreateInstance<SE_Stats>();
        se.name = EffectName;
        se.hideFlags = HideFlags.HideAndDontSave;
        se.m_name = BracedText;
        se.m_ttl = 0f;
        se.m_hidden = true;
        _template = se;
        _hash = se.NameHash();
    }
}
