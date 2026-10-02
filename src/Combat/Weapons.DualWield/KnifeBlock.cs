using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// Me = block of a pair of two knives (design 2.6, G12, D29). Vanilla: off-hand weapon block alone (GetCurrentBlocker =
// left item), and a knife block with power 2. Two knives block together like the item whose moves they use
// (KnifePairMoves, default Skoll and Hati: block 24, parry x4), scaled to the knives' physical damage (slash + pierce +
// blunt, base quality, mean of the two knives) against that item's: two knives that hit half as hard block half as well.
// Then x KnifePairBlock percent. Never below off-hand knife own block and parry (KnifePairBlock 0 = vanilla).
// Knife next to other weapon, lone knife, every other pair: vanilla, untouched.
// Only gameplay reader = Humanoid.BlockAttack: block power through GetBlockPower (SharedData m_blockPower + per level),
// parry bonus field m_timedBlockBonus. Me no patch the tiny getters (JIT may inline them, D10): BlockAttack prefix put
// pair values into off-hand knife's SharedData for that one call, finalizer put knife own values back (also after
// exception). Nothing else run in between (one call, main thread).
internal static class KnifeBlock
{
    // Knife SharedData me changed for the call in progress (null = none), and its own values.
    private static ItemDrop.ItemData.SharedData _shared;
    private static float _blockPower;
    private static float _blockPerLevel;
    private static float _parryBonus;

    // Pure (self test hammer it): base block power of the pair (before the Blocking skill).
    internal static float PairBlockPower(float templateBlock, float templateDamage, float mainDamage, float offDamage,
        float ownBlock, int percent)
    {
        if (percent <= 0)
        {
            return ownBlock;
        }
        var scale = templateDamage > 0f ? (mainDamage + offDamage) * 0.5f / templateDamage : 1f;
        return Mathf.Max(ownBlock, templateBlock * scale * percent / 100f);
    }

    // Pure: parry bonus of the pair = template's, never below off-hand knife own.
    internal static float PairParryBonus(float templateBonus, float ownBonus, int percent) =>
        percent <= 0 ? ownBonus : Mathf.Max(ownBonus, templateBonus);

    // Base damage a block push against: slash, pierce, blunt (base quality, no world level). Elemental part no count.
    internal static float PhysicalDamage(ItemDrop.ItemData.SharedData s) =>
        s.m_damages.m_slash + s.m_damages.m_pierce + s.m_damages.m_blunt;

    // Pair values for player's hands now. False = no knife pair with valid knife moves: vanilla block.
    internal static bool TryValues(Player player, out float blockPower, out float parryBonus)
    {
        blockPower = 0f;
        parryBonus = 0f;
        if (!Hands.IsPaired(player))
        {
            return false;
        }
        var right = player.m_rightItem;
        var left = player.m_leftItem;
        if (!MoveTemplates.IsKnifePair(right, left))
        {
            return false;
        }
        var rules = ServerRules.Current;
        var template = MoveTemplates.For(right, left, rules, player);
        if (template == null)
        {
            return false;
        }
        blockPower = PairBlockPower(template.BlockPower, template.PhysicalDamage, PhysicalDamage(right.m_shared),
            PhysicalDamage(left.m_shared), left.GetBaseBlockPower(), rules.KnifePairBlock);
        parryBonus = PairParryBonus(template.ParryBonus, left.m_shared.m_timedBlockBonus, rules.KnifePairBlock);
        return true;
    }

    // BlockAttack prefix, local player. Knife pair = pair values into off-hand knife's SharedData. True = me changed
    // them (finalizer put back). Nested call while one in force: nothing (outer finalizer put back).
    internal static bool Apply(Player player)
    {
        if (_shared != null || !TryValues(player, out var block, out var parry))
        {
            return false;
        }
        var shared = player.m_leftItem.m_shared;
        _blockPower = shared.m_blockPower;
        _blockPerLevel = shared.m_blockPowerPerLevel;
        _parryBonus = shared.m_timedBlockBonus;
        _shared = shared;
        shared.m_blockPower = block;
        shared.m_blockPowerPerLevel = 0f;
        shared.m_timedBlockBonus = parry;
        return true;
    }

    // BlockAttack finalizer (and OnDeactivated, harmless when nothing in force): knife own values back.
    internal static void Restore()
    {
        var shared = _shared;
        if (shared == null)
        {
            return;
        }
        _shared = null;
        shared.m_blockPower = _blockPower;
        shared.m_blockPowerPerLevel = _blockPerLevel;
        shared.m_timedBlockBonus = _parryBonus;
    }
}
