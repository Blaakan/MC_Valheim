using System;
using System.Collections.Generic;
using UnityEngine;

namespace MC.Crafting.StationsBatchFeedMod;

// Me = adds this player sent to a station that ANOTHER game own, not yet seen back in local ZDO copy.
// Why: non-owner read stale copy. Vanilla check "full?" on stale copy, owner then overfill (smelter, fuel, ballista)
// or eat the item (fire, cooking slot). Five adds per press make that race easy, so me remember what me sent.
// Effective value = max(local, baseline + sent): conservative (burn or finished products only make me add less).
// Entry die when local copy catch up (local >= baseline + sent) or Window seconds after last add.
// Owner never use this (local = truth). Nothing here is written to any ZDO.
internal static class PendingAdds
{
    internal const float Window = 3f;

    private sealed class Entry
    {
        internal float Baseline;
        internal int Sent;
        internal float Expires;
        internal string LockedPrefab;
    }

    private readonly struct Key : IEquatable<Key>
    {
        private readonly ZDOID _id;
        private readonly FeedKind _kind;

        internal Key(ZDOID id, FeedKind kind)
        {
            _id = id;
            _kind = kind;
        }

        public bool Equals(Key other) => _id.Equals(other._id) && _kind == other._kind;

        public override bool Equals(object obj) => obj is Key other && Equals(other);

        public override int GetHashCode() => (_id.GetHashCode() * 397) ^ (int)_kind;
    }

    private static readonly Dictionary<Key, Entry> Entries = new Dictionary<Key, Entry>();
    private static readonly List<Key> Dead = new List<Key>();

    internal static void Clear() => Entries.Clear();

    // Me own it now: my local copy is truth, old guesses go.
    internal static void Drop(in FeedTarget target)
    {
        if (Entries.Count > 0)
        {
            Entries.Remove(KeyOf(target));
        }
    }

    internal static float Effective(in FeedTarget target, float local)
    {
        var entry = Live(target, local);
        return entry == null ? local : Mathf.Max(local, entry.Baseline + entry.Sent);
    }

    // Ballista: missile kind me already sent this window (owner relabel all missiles with last kind it get).
    internal static string LockedPrefab(in FeedTarget target, float local) => Live(target, local)?.LockedPrefab;

    // Me sent "added" more items. baseline = local value before this send (used only when no live entry).
    internal static void Record(in FeedTarget target, float baseline, int added, string lockedPrefab)
    {
        if (added <= 0)
        {
            return;
        }
        var expires = Time.time + Window;
        var entry = Live(target, target.ReadValue());
        if (entry != null)
        {
            entry.Sent += added;
            entry.Expires = expires;
            entry.LockedPrefab ??= lockedPrefab;
            return;
        }
        Entries[KeyOf(target)] = new Entry { Baseline = baseline, Sent = added, Expires = expires, LockedPrefab = lockedPrefab };
    }

    // Live entry or null. Prune old ones first, and drop this one when local copy caught up (owner update arrived).
    private static Entry Live(in FeedTarget target, float local)
    {
        if (Entries.Count == 0)
        {
            return null;
        }
        Prune();
        var key = KeyOf(target);
        if (!Entries.TryGetValue(key, out var entry))
        {
            return null;
        }
        if (local >= entry.Baseline + entry.Sent)
        {
            Entries.Remove(key);
            return null;
        }
        return entry;
    }

    private static void Prune()
    {
        var now = Time.time;
        foreach (var pair in Entries)
        {
            if (pair.Value.Expires <= now)
            {
                Dead.Add(pair.Key);
            }
        }
        foreach (var key in Dead)
        {
            Entries.Remove(key);
        }
        Dead.Clear();
    }

    private static Key KeyOf(in FeedTarget target) => new Key(target.NView.GetZDO().m_uid, target.Kind);
}
