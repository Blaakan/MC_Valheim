using System;
using System.Globalization;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.SneakAmbushMod;

// Me = Sneak XP for sneak attacks (design 2.1, G1, E4, E6).
//   victim's owner (Character.RPC_Damage postfix): vanilla backstab happened (m_backstabTime changed), attacker is a
//     Player, victim not tame, not training dummy, rules not pending, XP cooldown of creature (its ZDO long
//     <guid>.LastXp, network ms) passed, attacker's view valid -> write key, routed RPC <guid>.SneakAttackXp (float
//     victimHealth, bool ranged) to attacker's Player object = attacker's own game. Sent = cooldown used, paid or not
//     over there.
//   attacker's game (handler on EVERY Player's ZNetView, registered by always-on Player.Awake postfix): only for
//     local player, feature active, rules not pending, no other sneak-XP mod paying (unless rule say pay both);
//     amount from ITS rules (server's): (flat + perRef * min(health / ref, maxScale)) * (ranged ? factor : 1),
//     then Player.RaiseSkill (Rested and world skill rate apply). "Sneak attack! +N% Sneak" top left (personal).
// Victim health = prefab base health x star level (stars pay more, world level not).
internal static class SneakXp
{
    internal const string Rpc = ModInfo.Guid + ".SneakAttackXp";
    internal static readonly int LastXpKey = (ModInfo.Guid + ".LastXp").GetStableHashCode();

#if DEBUG
    // Self test force the message on or off (never the config file). Null = personal setting ShowSneakAttackMessage.
    internal static bool? TestShowMessage { get; set; }
#endif

    // Personal setting ShowSneakAttackMessage (not bound yet = on), or the self test override.
    private static bool ShowMessage
    {
        get
        {
#if DEBUG
            if (TestShowMessage.HasValue)
            {
                return TestShowMessage.Value;
            }
#endif
            return Plugin.ShowSneakAttackMessage == null || Plugin.ShowSneakAttackMessage.Value;
        }
    }

    // Always-on Player.Awake postfix. Menu preview character has no ZDO: skip (vanilla Awake also stop there).
    internal static void Register(Player player)
    {
        var nview = player.m_nview;
        if (nview == null || nview.GetZDO() == null)
        {
            return;
        }
        nview.Register<float, bool>(Rpc, (sender, victimHealth, ranged) => Receive(player, victimHealth, ranged));
    }

    // Pure (self test): XP amount for a victim health, with bad input refused (NaN, infinity, negative -> 0 XP).
    internal static float Amount(AmbushRules rules, float victimHealth, bool ranged)
    {
        if (rules == null || rules.IsPending || float.IsNaN(victimHealth) || float.IsInfinity(victimHealth)
            || victimHealth < 0f)
        {
            return 0f;
        }
        var reference = Mathf.Max(AmbushRules.ReferenceHealthMin, rules.ReferenceHealth);
        var cap = reference * rules.MaxHealthScale;
        var health = Mathf.Min(victimHealth, cap);
        var xp = rules.SneakAttackXpFlat + rules.SneakAttackXp * Mathf.Min(health / reference, rules.MaxHealthScale);
        if (ranged)
        {
            xp *= rules.RangedXpFactor;
        }
        return xp > 0f ? xp : 0f;
    }

    // Victim's owner, right after vanilla applied a backstab (Character.RPC_Damage postfix).
    internal static void OnBackstab(Character victim, Player attacker, HitData hit)
    {
        var rules = ServerRules.Current;
        if (rules.IsPending || victim.IsTamed() || victim.GetFaction() == Character.Faction.TrainingDummy)
        {
            return;
        }
        var nview = victim.m_nview;
        if (nview == null || !nview.IsValid() || !nview.IsOwner())
        {
            return;
        }
        var zdo = nview.GetZDO();
        var nowMs = (long)(SmokeRegistry.Now * 1000d);
        var last = zdo.GetLong(LastXpKey, 0L);
        var cooldownMs = (long)(rules.SneakAttackXpCooldown * 1000f);
        if (last != 0L && nowMs - last < cooldownMs)
        {
            Log.Debug($"Sneak attack on {victim.m_name} by {attacker.GetPlayerName()}: no Sneak XP, this creature "
                      + $"paid one {(nowMs - last) / 1000L} s ago (once per {rules.SneakAttackXpCooldown:0} s).");
            return;
        }
        var victimHealth = victim.m_health * Mathf.Max(1, victim.GetLevel());
        var ranged = hit != null && hit.m_ranged;
        var attackerView = attacker.m_nview;
        if (attackerView == null || !attackerView.IsValid())
        {
            return;
        }
        // Cooldown used only when XP really sent. Me cannot know if attacker's game pay (copy off, rules pending,
        // other sneak-XP mod pay instead): sent = counted (design 2.1).
        zdo.Set(LastXpKey, nowMs == 0L ? 1L : nowMs);
        attackerView.InvokeRPC(Rpc, victimHealth, ranged);
        Log.Debug($"Sneak attack on {victim.m_name} by {attacker.GetPlayerName()}: sent {victimHealth:0.#} health"
                  + (ranged ? " (ranged)" : "") + " to their game for Sneak XP.");
    }

    // Attacker's game. Handler exists whatever the toggle said when the player spawned (always on).
    private static void Receive(Player player, float victimHealth, bool ranged)
    {
        try
        {
            if (player == null || !ReferenceEquals(player, Player.m_localPlayer) || !Plugin.FeatureActive)
            {
                return;
            }
            var rules = ServerRules.Current;
            if (rules.IsPending)
            {
                return;
            }
            if (Compat.StandDownXp(rules))
            {
                Log.Debug("Sneak attack: no Sneak XP from " + ModInfo.Name + ", another installed mod pays it "
                          + "(PayAlongsideOtherSneakXpMods is off).");
                return;
            }
            var xp = Amount(rules, victimHealth, ranged);
            if (xp <= 0f)
            {
                return;
            }
            Grant(player, xp, rules, ranged);
        }
        catch (Exception e)
        {
            PatchGuard.Report("SneakXp.Receive", e);
        }
    }

    // Raise Sneak through vanilla (Rested, skill gain rate), then the message.
    internal static void Grant(Player player, float xp, AmbushRules rules, bool ranged)
    {
        var skills = player.GetSkills();
        var skill = skills != null ? skills.GetSkill(Skills.SkillType.Sneak) : null;
        var levelBefore = skill != null ? skill.m_level : 0f;
        var progressBefore = skill != null ? skill.GetLevelPercentage() : 0f;
        player.RaiseSkill(Skills.SkillType.Sneak, xp);
        var leveledUp = skill != null && skill.m_level > levelBefore;
        var gained = skill != null && !leveledUp ? skill.GetLevelPercentage() - progressBefore : 0f;
        Log.Debug($"Sneak attack: {xp.ToString("0.##", CultureInfo.InvariantCulture)} Sneak XP"
                  + (ranged ? " (ranged)" : "") + (leveledUp ? ", Sneak level up." : "."));
        if (!ShowMessage)
        {
            return;
        }
        player.Message(MessageHud.MessageType.TopLeft, MessageText(leveledUp, gained, skill != null && skill.m_level >= 100f));
    }

    // "Sneak attack! +N% Sneak"; after level up or at Sneak 100 only "Sneak attack!" (vanilla say level up).
    internal static string MessageText(bool leveledUp, float progressGained, bool maxed)
    {
        if (leveledUp || maxed || progressGained <= 0f)
        {
            return "Sneak attack!";
        }
        var percent = Mathf.RoundToInt(progressGained * 100f);
        return percent < 1 ? "Sneak attack! +<1% Sneak" : $"Sneak attack! +{percent}% Sneak";
    }
}
