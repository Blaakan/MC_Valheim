using System.Collections.Generic;
using UnityEngine;

namespace MC.Combat.SneakAmbushMod;

// Me = what creatures THIS game owns remember about the smoke, for a few seconds (design 2.10, 2.11, D24):
//   blind   creature chasing a player near a burst cannot see or hear any player until BlindUntil
//   reveal  creature a player hit can see that player through smoke until RevealUntil (each hit renew it)
// Keys = transform instance ids (observer transform is what vanilla pass to CanSeeTarget / CanHearTarget; same
// object as the Character). Times = shared network time (seconds). Owner-local on purpose: ownership change forget
// them (short windows). Expired entries go on read; all cleared when last cloud leave (registry), on feature off.
// Observer cache: transform -> creature (null = no creature AI: turret, plain object). Not the boss flag (read live).
internal static class AiMemory
{
    private static readonly Dictionary<int, double> BlindUntil = new Dictionary<int, double>();
    private static readonly Dictionary<long, double> RevealUntil = new Dictionary<long, double>();
    private static readonly Dictionary<int, Character> Observers = new Dictionary<int, Character>();
    private static readonly List<long> TempKeys = new List<long>();

    internal static bool AnyBlinded => BlindUntil.Count > 0;

    internal static int BlindCount => BlindUntil.Count;

    internal static int RevealCount => RevealUntil.Count;

    // Burst: creature blind until then, and earlier reveals of it gone (burst take away what a hit gave).
    internal static void Blind(Transform creature, double until)
    {
        if (creature == null)
        {
            return;
        }
        var id = creature.GetInstanceID();
        BlindUntil[id] = until;
        ClearReveals(id);
    }

    internal static bool IsBlinded(Transform observer, double now)
    {
        var id = observer.GetInstanceID();
        if (!BlindUntil.TryGetValue(id, out var until))
        {
            return false;
        }
        if (now >= until)
        {
            BlindUntil.Remove(id);
            return false;
        }
        return true;
    }

    // Player hit this creature: it see that player through smoke until then.
    internal static void Reveal(Character victim, Character attacker, double until)
    {
        if (victim == null || attacker == null)
        {
            return;
        }
        RevealUntil[Key(victim.transform.GetInstanceID(), attacker.transform.GetInstanceID())] = until;
    }

    internal static bool IsRevealed(Transform observer, Character target, double now)
    {
        if (RevealUntil.Count == 0)
        {
            return false;
        }
        var key = Key(observer.GetInstanceID(), target.transform.GetInstanceID());
        if (!RevealUntil.TryGetValue(key, out var until))
        {
            return false;
        }
        if (now >= until)
        {
            RevealUntil.Remove(key);
            return false;
        }
        return true;
    }

    // Creature behind this observer transform, or null when it has no creature AI (turret, NPC prop). Cached.
    internal static Character ObserverCreature(Transform observer)
    {
        var id = observer.GetInstanceID();
        if (Observers.TryGetValue(id, out var creature))
        {
            return creature;
        }
        creature = null;
        if (observer.TryGetComponent<BaseAI>(out var ai) && ai.m_character != null)
        {
            creature = ai.m_character;
        }
        Observers[id] = creature;
        return creature;
    }

    internal static void Clear()
    {
        BlindUntil.Clear();
        RevealUntil.Clear();
        Observers.Clear();
        TempKeys.Clear();
    }

    private static void ClearReveals(int observerId)
    {
        if (RevealUntil.Count == 0)
        {
            return;
        }
        TempKeys.Clear();
        foreach (var key in RevealUntil.Keys)
        {
            if ((int)(key >> 32) == observerId)
            {
                TempKeys.Add(key);
            }
        }
        foreach (var key in TempKeys)
        {
            RevealUntil.Remove(key);
        }
        TempKeys.Clear();
    }

    private static long Key(int observerId, int targetId) => ((long)observerId << 32) | (uint)targetId;
}
