using MC.Shared;
using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = the Music effect's rules: who gets it (Grant, on the owner's own game: performer for itself, each listener for
// its own player after an Encore it heard within BonusRange) and how much comfort it adds (ComfortFor, read by the
// GetComfortLevel postfix). Status effects tick only on the owner, so every game add the effect to its OWN player by
// hash (owner path of SEMan.AddStatusEffect: ObjectDB template clone), never to a remote player's copy.
internal static class MusicBonus
{
    // Local player get (or renew) the Music effect for BonusMinutes. False = rules pending / bonus off / no player.
    internal static bool Grant(Player player, bool fromOther)
    {
        var rules = ServerRules.Current;
        if (player == null || player.IsDead() || rules.IsPending || rules.ComfortBonus <= 0 || InstrumentContent.Effect == null)
        {
            return false;
        }
        var seman = player.GetSEMan();
        if (seman == null)
        {
            return false;
        }
        var ttl = rules.BonusMinutes * 60f;
        // New clones copy the template's TTL; an effect already running keep its own copy: set it too, then reset.
        InstrumentContent.Effect.m_ttl = ttl;
        var running = seman.GetStatusEffect(InstrumentContent.EffectHash);
        if (running != null)
        {
            running.m_ttl = ttl;
        }
        var had = running != null;
        seman.AddStatusEffect(InstrumentContent.EffectHash, resetTime: true);
        if (!had)
        {
            var text = fromOther
                ? "The music warms you: +" + rules.ComfortBonus + " comfort"
                : "Your music warms everyone near you: +" + rules.ComfortBonus + " comfort";
            player.Message(MessageHud.MessageType.TopLeft, text + ".");
        }
        Log.Debug($"Music effect {(had ? "renewed" : "started")} for {rules.BonusMinutes:0.#} min ({(fromOther ? "heard" : "own")} performance).");
        return true;
    }

    // Comfort the Music effect is worth right now: 0 while the feature is off (comfort patch gone), rules pending, or
    // bonus 0. Icon, tooltip and the comfort patch all read this, so they never disagree.
    internal static int BonusInForce(MusicRules rules) =>
        !Plugin.FeatureActive || rules.IsPending ? 0 : System.Math.Max(0, rules.ComfortBonus);

    // Comfort the Music effect adds now for this (local) player: 0 without the effect or with bonus off. Shelter or
    // not: resting outdoors by a fire (sitting) use this comfort for Rested too.
    internal static int ComfortFor(Player player)
    {
        var seman = player.m_seman;
        if (seman == null || !seman.HaveStatusEffect(InstrumentContent.EffectHash))
        {
            return 0;
        }
        var rules = ServerRules.Current;
        var bonus = BonusInForce(rules);
        if (bonus <= 0)
        {
            return 0;
        }
        return bonus;
    }

    // Distance check for an Encore heard from another player (performer position as the packet / instance gives it).
    internal static bool InRange(Player listener, Vector3 performerPosition)
    {
        var rules = ServerRules.Current;
        return listener != null && !rules.IsPending
               && (listener.transform.position - performerPosition).sqrMagnitude <= rules.BonusRange * rules.BonusRange;
    }
}
