namespace MC.Farming.HarpoonHooksTamesMod;

// Me = protection while one harpoon hook hit run on a tame (Character.Damage call, incl. other mods' postfix).
// Other mod on-hit proc (EpicLoot lightning, slow, life steal...) may hit SAME tame again inside that call:
// me stop those extra hits from local player, so hook stay harmless. Hits without attacker (drown, fire),
// other players' hits and hits on other creatures pass.
internal static class HitScope
{
    // Tame under protection now. Only set for one Damage call; finalizer clear it.
    private static Character s_target;

    // Run for EVERY Character.Damage: plain reference check first (no Unity ==, no alloc).
    internal static bool IsNestedHit(Character target, HitData hit)
    {
        if ((object)s_target == null)
        {
            return false;
        }
        if (!ReferenceEquals(target, s_target) || hit == null || !hit.HaveAttacker())
        {
            return false;
        }
        if (HarpoonEffect.IsHarpoon(hit.m_statusEffectHash))
        {
            return false;
        }

        var local = Player.m_localPlayer;
        return local != null && hit.m_attacker == local.GetZDOID();
    }

    // True = me opened it (caller must Close). Already open = false, outer call keep it.
    internal static bool Open(Character target)
    {
        if ((object)s_target != null)
        {
            return false;
        }
        s_target = target;
        return true;
    }

    internal static void Close()
    {
        s_target = null;
    }
}
