using System;
using System.Collections.Generic;
using System.Globalization;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.CultivatorReplantMod;

// Me = the Ancient Root gate of the Yggdrasil transplant (design 2.6), and the pending hold of every transplant
// sapling. Vanilla Plant.SUpdate call Grow on EVERY SlowUpdater pass once the sapling is due (owner only). Me stand in
// front of Grow (Patches/PlantPatches.cs):
//   - every other plant: one null check and one int set lookup, then vanilla
//   - our other saplings: rules pending = Grow skipped (instance may still carry own config grow time until Rebuild
//     write the server's on it), else vanilla
//   - our Yggdrasil sapling, not Healthy: vanilla (it does nothing, m_destroyIfCantGrow is off)
//   - our Yggdrasil sapling, Healthy: one try per 10 s. Rules pending, no root in reach, or root sap not more than
//     RootSapCost = Grow skipped (no tree, sapling stay Healthy, so scythe and destroy rule never kill it) and try
//     again later. Else me drain RootSapCost from the root (ResourceRoot.Drain: RPC to the root's owner) and vanilla
//     Grow make the tree.
// Root lookup: OverlapSphere on the root's own collider layers only (TransplantContent read them from the
// YggdrasilRoot prefab: static_solid), ResourceRoot in parents, kept on the sapling (RootDrawer) until Unity say it is
// gone. Placement already need the root within RootRange (Piece.m_mustConnectTo), me look 1 m farther so the root
// found at placement is found again. PlantEasily grid ignore m_mustConnectTo: such a sapling just wait (Replant give
// it back).
// Drain on a root another peer own: check run on our copy of its level, RPC land on the owner later, owner skip it
// quietly when its level too low. Me keep a ledger per root of drains this peer sent and not yet seen land
// (EffectiveLevel), so several saplings of this peer (often same pass after zone load) never grow on one drain.
// Known limit left: saplings of two DIFFERENT owners trying before the drop reach both may both grow on one drain.
internal static class RootGate
{
    // Seconds between two grow tries of one sapling.
    internal const float TryInterval = 10f;
    // Seconds between two root lookups for the hover text of one sapling.
    internal const float HoverLookupInterval = 1f;
    // Lookup reach past RootRange (placement check hit the root mesh within RootRange of the ghost).
    internal const float LookupSlack = 1f;
    // Seconds a sent drain stay in the ledger when our copy never show it (owner refused it, owner changed).
    internal const float LedgerTimeout = 15f;

    private const int FirstHits = 128;
    private const int MaxHits = 1024;
    private const int LedgerPruneAt = 16;

    // ZDO prefab hash of MC_Sapling_YggaShoot. Only sapling me root-gate.
    internal static readonly int SaplingHash = ShootSaplingHash();

    // ZDO prefab hashes of all our transplant saplings (pending hold).
    private static readonly HashSet<int> SaplingHashes = AllSaplingHashes();

    // Shared hit buffer (main thread only). Mistlands near a base = many colliders: me grow it when full.
    private static Collider[] _hits = new Collider[FirstHits];
    private static bool _capLogged;

    // Layer mask of the root's colliders. 0 = not read yet (fallback taken at first use).
    private static int _rootMask;

    // Drains this peer sent per root (root ZDO id), not seen land yet on our copy.
    private struct DrainNote
    {
        // Root level once all those drains land (level before first - their costs).
        internal float Expected;
        // Our copy at or below Expected + Slack = they landed (regen 0.0025/s never fill half a cost in 15 s).
        internal float Slack;
        // Time.time of last drain sent.
        internal float SentAt;
    }

    private static readonly Dictionary<ZDOID, DrainNote> Ledger = new Dictionary<ZDOID, DrainNote>();
    private static readonly List<ZDOID> Expired = new List<ZDOID>();

    // Layers FindRoot search (self tests). Root prefab not read yet = static_solid (all layers if that layer gone).
    internal static int RootMask
    {
        get
        {
            if (_rootMask == 0)
            {
                _rootMask = FallbackMask();
            }
            return _rootMask;
        }
    }

    // TransplantContent, once the YggdrasilRoot prefab is found: its collider layers. 0 = fallback.
    internal static void UseRootMask(int mask)
    {
        _rootMask = mask != 0 ? mask : FallbackMask();
    }

    // One of our transplant saplings (ZDO prefab hash)?
    internal static bool IsTransplantSapling(int prefabHash) => SaplingHashes.Contains(prefabHash);

    // Drains in the ledger (self tests).
    internal static int LedgerCount => Ledger.Count;

    // Self tests: forget all sent drains.
    internal static void ClearLedger() => Ledger.Clear();

    // Plant.Grow prefix body. True = vanilla Grow run (grow now, or not our plant). False = Grow skipped, result null.
    internal static bool BeforeGrow(Plant plant, ref GameObject result)
    {
        // Hot path: every due plant of every kind come here every pass. Reference check, field read, one set lookup.
        var nview = plant.m_nview;
        if (ReferenceEquals(nview, null))
        {
            return true;
        }
        var zdo = nview.GetZDO();
        if (zdo == null)
        {
            return true;
        }
        var prefab = zdo.GetPrefab();
        if (!SaplingHashes.Contains(prefab))
        {
            return true;
        }
        if (prefab != SaplingHash)
        {
            // Our bush or forage sapling. Pending: its grow time may be own config (prefab copy at spawn), me hold it
            // until server rules come and Rebuild write theirs on it. Else vanilla.
            var current = ServerRules.Current;
            if (current != null && !current.IsPending)
            {
                return true;
            }
            result = null;
            return false;
        }
        // Not Healthy: vanilla rule (does nothing, our sapling never destroy itself).
        if (plant.GetStatus() != Plant.Status.Healthy)
        {
            return true;
        }

        var drawer = DrawerOf(plant);
        var now = Time.time;
        if (now < drawer.NextTry)
        {
            result = null;
            return false;
        }
        drawer.NextTry = now + TryInterval;

        var rules = ServerRules.Current;
        if (rules == null || rules.IsPending)
        {
            drawer.LastState = RootState.Pending;
            result = null;
            return false;
        }

        var radius = rules.RootRange + LookupSlack;
        var root = CachedRoot(drawer, radius);
        if (root == null)
        {
            root = FindRoot(plant.transform.position, radius);
            Remember(drawer, root, radius);
        }
        if (root == null)
        {
            drawer.LastState = RootState.NoRoot;
            result = null;
            return false;
        }

        float cost = rules.RootSapCost;
        // Level once drains this peer already sent land. Vanilla CanDrain rule (level > cost) on it, then Drain check
        // our copy again and send the RPC to the root's owner (at once when we own it).
        var level = EffectiveLevel(root);
        if (!(level > cost) || !root.Drain(cost))
        {
            drawer.LastState = RootState.Waiting;
            result = null;
            return false;
        }
        // Owner = drain done already (our copy show it). Other owner = RPC on its way: me write it down.
        if (!root.m_nview.IsOwner())
        {
            NoteDrain(root, level, cost);
        }
        drawer.LastState = RootState.Unknown;
        return true;
    }

    // Root level as it will be once the drains this peer sent land: our copy, or less while a sent drain is not on our
    // copy yet. Drop seen, copy lower than expected (owner refused, other peer drained), or note too old = note gone,
    // our copy. Cheap when no drain waiting (hover call it every frame).
    internal static float EffectiveLevel(ResourceRoot root)
    {
        var raw = root.GetLevel();
        if (Ledger.Count == 0)
        {
            return raw;
        }
        var zdo = root.m_nview.GetZDO();
        if (zdo == null)
        {
            return raw;
        }
        var id = zdo.m_uid;
        if (!Ledger.TryGetValue(id, out var note))
        {
            return raw;
        }
        if (raw <= note.Expected + note.Slack || Time.time - note.SentAt >= LedgerTimeout)
        {
            Ledger.Remove(id);
            return raw;
        }
        return note.Expected;
    }

    // A drain of cost sent to a root another peer own, with level = EffectiveLevel read just before. Next one of this
    // peer on that root count from level - cost. Internal: self tests fake a remote root with it.
    internal static void NoteDrain(ResourceRoot root, float level, float cost)
    {
        var zdo = root.m_nview.GetZDO();
        if (zdo == null || !(cost > 0f))
        {
            return;
        }
        var now = Time.time;
        if (Ledger.Count >= LedgerPruneAt)
        {
            Prune(now);
        }
        Ledger[zdo.m_uid] = new DrainNote { Expected = level - cost, Slack = cost * 0.5f, SentAt = now };
    }

    // Nearest Ancient Root (by its position) with a collider within range of pos. Null = none. Main thread only.
    internal static ResourceRoot FindRoot(Vector3 pos, float range)
    {
        if (!(range > 0f))
        {
            return null;
        }
        // Root's collider layers only (static_solid): pieces, items, creatures, terrain never fill the buffer. Same
        // colliders the placement check find (it look for the root's ZNetView name in parents).
        var mask = RootMask;
        var count = Physics.OverlapSphereNonAlloc(pos, range, _hits, mask, QueryTriggerInteraction.Collide);
        while (count >= _hits.Length && _hits.Length < MaxHits)
        {
            _hits = new Collider[_hits.Length * 2];
            count = Physics.OverlapSphereNonAlloc(pos, range, _hits, mask, QueryTriggerInteraction.Collide);
        }
        if (count >= _hits.Length && !_capLogged)
        {
            _capLogged = true;
            Log.Warning($"The search for an Ancient Root around a Yggdrasil transplant near {pos} reached its limit of "
                        + $"{MaxHits} objects, so a root there may be missed and the transplant may wait. A smaller "
                        + "RootRange helps.");
        }

        ResourceRoot best = null;
        var bestSqr = float.MaxValue;
        for (var i = 0; i < count; i++)
        {
            var hit = _hits[i];
            if (hit == null)
            {
                continue;
            }
            var root = hit.GetComponentInParent<ResourceRoot>();
            if (!Usable(root))
            {
                continue;
            }
            // Root collider = non-convex mesh: ClosestPoint no work on it, me compare root positions.
            var sqr = (root.transform.position - pos).sqrMagnitude;
            if (sqr < bestSqr)
            {
                best = root;
                bestSqr = sqr;
            }
        }
        // Drop references: buffer must not keep dead colliders alive.
        Array.Clear(_hits, 0, count);
        return best;
    }

    // Extra hover lines of our Yggdrasil sapling (each start with a new line). Null = not our sapling (hover left as
    // is). "" = rules pending (nothing to promise yet).
    internal static string HoverLines(Plant plant)
    {
        // Every hovered plant come here every frame: same cheap check as BeforeGrow first.
        var nview = plant.m_nview;
        if (ReferenceEquals(nview, null))
        {
            return null;
        }
        var zdo = nview.GetZDO();
        if (zdo == null || zdo.GetPrefab() != SaplingHash)
        {
            return null;
        }

        var rules = ServerRules.Current;
        if (rules == null || rules.IsPending)
        {
            return "";
        }

        var drawer = DrawerOf(plant);
        var radius = rules.RootRange + LookupSlack;
        var root = CachedRoot(drawer, radius);
        var now = Time.time;
        if (root == null && now >= drawer.NextLookup)
        {
            drawer.NextLookup = now + HoverLookupInterval;
            root = FindRoot(plant.transform.position, radius);
            Remember(drawer, root, radius);
        }

        if (root == null)
        {
            return "\nNeeds an Ancient Root within " + Meters(rules.RootRange) + " m";
        }

        var cost = rules.RootSapCost;
        // Same level the grow try use (drains this peer sent count already).
        var level = EffectiveLevel(root);
        var text = "\nDraws " + cost.ToString(CultureInfo.InvariantCulture) +
                   " sap from the Ancient Root when grown (root: " +
                   Mathf.FloorToInt(level).ToString(CultureInfo.InvariantCulture) + " / " +
                   Mathf.RoundToInt(root.m_maxLevel).ToString(CultureInfo.InvariantCulture) + ")";
        if (!(level > cost))
        {
            text += " - waiting for it to refill";
        }
        return text;
    }

    // Cached root of the sapling, or null when gone (Unity null, ZDO dropped) or found with a wider search than now.
    private static ResourceRoot CachedRoot(RootDrawer drawer, float radius)
    {
        var root = drawer.Root;
        if (!Usable(root))
        {
            return null;
        }
        // RootRange shrink: root found farther than new reach may be out of it now. Me look again.
        return drawer.RootRadius <= radius + 0.01f ? root : null;
    }

    private static void Remember(RootDrawer drawer, ResourceRoot root, float radius)
    {
        drawer.Root = root;
        drawer.RootRadius = radius;
    }

    // Root alive and its ZDO there (GetLevel read the ZDO; zone unloading reset it before Unity destroy the object).
    private static bool Usable(ResourceRoot root)
    {
        if (root == null)
        {
            return false;
        }
        var nview = root.m_nview;
        return nview != null && nview.IsValid();
    }

    // Content put RootDrawer on the sapling prefab. Missing (older copy, other build path) = me add one, once.
    private static RootDrawer DrawerOf(Plant plant)
    {
        var drawer = plant.GetComponent<RootDrawer>();
        if (drawer == null)
        {
            drawer = plant.gameObject.AddComponent<RootDrawer>();
        }
        return drawer;
    }

    // Notes too old go (roots left behind never read again). Rare: only when ledger grow.
    private static void Prune(float now)
    {
        Expired.Clear();
        foreach (var pair in Ledger)
        {
            if (now - pair.Value.SentAt >= LedgerTimeout)
            {
                Expired.Add(pair.Key);
            }
        }
        foreach (var id in Expired)
        {
            Ledger.Remove(id);
        }
        Expired.Clear();
    }

    // static_solid = layer of the root mesh in 1.0. Layer gone (game change) = all layers, like vanilla placement.
    private static int FallbackMask()
    {
        var mask = LayerMask.GetMask("static_solid");
        return mask != 0 ? mask : Physics.AllLayers;
    }

    private static string Meters(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static int ShootSaplingHash()
    {
        // Catalog row always there (fixed table). Missing anyway = 0: no real prefab, gate never act.
        var kind = PlantCatalog.ByKey("YggaShoot");
        return kind != null ? kind.SaplingHash : 0;
    }

    private static HashSet<int> AllSaplingHashes()
    {
        var set = new HashSet<int>();
        foreach (var kind in PlantCatalog.All)
        {
            set.Add(kind.SaplingHash);
        }
        return set;
    }
}
