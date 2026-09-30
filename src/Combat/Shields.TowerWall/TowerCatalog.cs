using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.ShieldsTowerWallMod;

// Me = vanilla values of one tower prefab (design 2.0, 2.0.1), taken first time me see it in the game session. Every
// write start from here (idempotent, never "multiply current value"); revert write it back. Never taken again for same
// prefab object: prefab edits live whole process, second look would copy our own values.
internal sealed class TowerSnapshot
{
    internal readonly string Prefab;
    internal readonly GameObject PrefabObject;
    internal readonly ItemDrop.ItemData.SharedData PrefabShared;
    internal readonly ItemDrop.ItemData.ItemType ItemType;
    internal readonly ItemDrop.ItemData.ItemType AttachOverride;
    internal readonly ItemDrop.ItemData.AnimationState AnimationState;
    internal readonly Skills.SkillType SkillType;
    internal readonly float TimedBlockBonus;
    internal readonly float PerfectBlockAdrenaline;
    internal readonly float BlockPower;
    internal readonly float BlockPowerPerLevel;
    internal readonly float DeflectionForce;
    internal readonly float DeflectionForcePerLevel;
    internal readonly float MovementModifier;
    internal readonly HitData.DamageTypes Damages;
    internal readonly HitData.DamageTypes DamagesPerLevel;
    internal readonly float AttackForce;
    internal readonly bool Blockable;
    internal readonly bool Dodgeable;
    internal readonly float BackstabBonus;
    internal readonly bool BuildBlockCharges;
    internal readonly Attack Attack;

    internal TowerSnapshot(string prefab, GameObject prefabObject, ItemDrop.ItemData.SharedData s)
    {
        Prefab = prefab;
        PrefabObject = prefabObject;
        PrefabShared = s;
        ItemType = s.m_itemType;
        AttachOverride = s.m_attachOverride;
        AnimationState = s.m_animationState;
        SkillType = s.m_skillType;
        TimedBlockBonus = s.m_timedBlockBonus;
        PerfectBlockAdrenaline = s.m_perfectBlockAdrenaline;
        BlockPower = s.m_blockPower;
        BlockPowerPerLevel = s.m_blockPowerPerLevel;
        DeflectionForce = s.m_deflectionForce;
        DeflectionForcePerLevel = s.m_deflectionForcePerLevel;
        MovementModifier = s.m_movementModifier;
        Damages = s.m_damages;
        DamagesPerLevel = s.m_damagesPerLevel;
        AttackForce = s.m_attackForce;
        Blockable = s.m_blockable;
        Dodgeable = s.m_dodgeable;
        BackstabBonus = s.m_backstabBonus;
        BuildBlockCharges = s.m_buildBlockCharges;
        Attack = s.m_attack;
    }

    internal bool IsShield => IsShieldData(ItemType, AnimationState, SkillType);

    // Shield = item type Shield + shield pose + Blocking skill (every vanilla shield, nothing else: dump 24 of 24).
    internal static bool IsShieldData(ItemDrop.ItemData.ItemType type, ItemDrop.ItemData.AnimationState anim,
        Skills.SkillType skill) =>
        type == ItemDrop.ItemData.ItemType.Shield
        && anim == ItemDrop.ItemData.AnimationState.Shield
        && skill == Skills.SkillType.Blocking;
}

// One tower of the rules in force: list entry (bash damage) + its vanilla snapshot.
internal sealed class TowerInfo
{
    internal readonly TowerEntry Entry;
    internal readonly TowerSnapshot Snapshot;

    internal TowerInfo(TowerEntry entry, TowerSnapshot snapshot)
    {
        Entry = entry;
        Snapshot = snapshot;
    }

    internal float BashDamage => Entry.BashDamage;
}

// Me = which items are tower shields right now (design 2.0, 7.1). Built from the rules TowerSync applied and the
// current ObjectDB; checked against the vanilla snapshot: unknown name = MISSING, not a shield = refused, shield that
// can parry = accepted but lose parry (one Warning each, once per rule set).
// Item found by m_dropPrefab (every real item has it), or by prefab SharedData reference (prefab's own data in recipe
// panels has no drop prefab). Never by item name: creature copies (FW_/SP_ShieldBlackmetalTower) share the name.
// Hot paths (local player, per tick / per hit) use Held: cached verdict for the left item, recomputed only when the
// left item reference or Version change, then one live read of item type (the copy carry tower data now).
internal static class TowerCatalog
{
    // Process lifetime: snapshot per prefab name, and same snapshots by prefab object / prefab SharedData.
    private static readonly Dictionary<string, TowerSnapshot> SnapshotsByName = new Dictionary<string, TowerSnapshot>();
    private static readonly Dictionary<GameObject, TowerSnapshot> SnapshotsByPrefab = new Dictionary<GameObject, TowerSnapshot>();
    private static readonly Dictionary<ItemDrop.ItemData.SharedData, TowerSnapshot> SnapshotsByShared =
        new Dictionary<ItemDrop.ItemData.SharedData, TowerSnapshot>();

    // Towers of the applied rules (empty = none: mod off, client waiting, no ObjectDB yet).
    private static readonly Dictionary<GameObject, TowerInfo> ByPrefab = new Dictionary<GameObject, TowerInfo>();
    private static readonly Dictionary<ItemDrop.ItemData.SharedData, TowerInfo> ByShared =
        new Dictionary<ItemDrop.ItemData.SharedData, TowerInfo>();

    private static int _version;
    private static string _warnedKey;

    // Held cache (local player only).
    private static ItemDrop.ItemData _heldItem;
    private static int _heldVersion = -1;
    private static TowerInfo _heldInfo;

    // Raised on every rebuild and clear: cached verdicts drop.
    internal static int Version => _version;

    internal static int Count => ByPrefab.Count;

    internal static IEnumerable<TowerSnapshot> Snapshots => SnapshotsByName.Values;

    internal static IEnumerable<TowerInfo> Towers => ByPrefab.Values;

    // New tower table for these rules and this ObjectDB (TowerSync apply, ObjectDB postfix). Null rules or no items =
    // empty table (vanilla).
    internal static void Rebuild(ObjectDB db, TowerRules rules)
    {
        Bump();
        ByPrefab.Clear();
        ByShared.Clear();
        if (rules == null || db == null || db.m_items == null || db.m_items.Count == 0)
        {
            return;
        }
        List<string> missing = null;
        List<string> notShield = null;
        List<string> parry = null;
        foreach (var entry in rules.Entries)
        {
            var go = db.GetItemPrefab(entry.Prefab);
            var drop = go != null ? go.GetComponent<ItemDrop>() : null;
            var shared = drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
            if (shared == null)
            {
                (missing ??= new List<string>()).Add(entry.Prefab);
                continue;
            }
            var snap = SnapshotFor(entry.Prefab, go, shared);
            if (snap == null || !snap.IsShield)
            {
                (notShield ??= new List<string>()).Add(entry.Prefab);
                continue;
            }
            if (snap.TimedBlockBonus > 1f)
            {
                (parry ??= new List<string>()).Add(entry.Prefab);
            }
            var info = new TowerInfo(entry, snap);
            ByPrefab[go] = info;
            ByShared[shared] = info;
        }
        WarnOnce(rules, missing, notShield, parry);
    }

    // Mod off: no tower (snapshots stay: process lifetime). Warnings said again after next turn on (like TowerSync's
    // list problems and TowerGuard).
    internal static void Clear()
    {
        Bump();
        ByPrefab.Clear();
        ByShared.Clear();
        _heldItem = null;
        _heldInfo = null;
        _warnedKey = null;
    }

    // Tower info of an item, null = not a tower of the applied rules. Dictionary lookups, no allocation.
    internal static TowerInfo TowerOf(ItemDrop.ItemData item)
    {
        if (item == null || ByPrefab.Count == 0)
        {
            return null;
        }
        TowerInfo info;
        if (item.m_dropPrefab != null)
        {
            return ByPrefab.TryGetValue(item.m_dropPrefab, out info) ? info : null;
        }
        return item.m_shared != null && ByShared.TryGetValue(item.m_shared, out info) ? info : null;
    }

    internal static TowerInfo TowerOfPrefab(GameObject prefab) =>
        prefab != null && ByPrefab.TryGetValue(prefab, out var info) ? info : null;

    // Vanilla snapshot of an item's prefab, null = never a tower this session (so never written by me).
    internal static TowerSnapshot SnapshotOf(ItemDrop.ItemData item)
    {
        if (item == null || SnapshotsByPrefab.Count == 0)
        {
            return null;
        }
        TowerSnapshot snap;
        if (item.m_dropPrefab != null)
        {
            return SnapshotsByPrefab.TryGetValue(item.m_dropPrefab, out snap) ? snap : null;
        }
        return item.m_shared != null && SnapshotsByShared.TryGetValue(item.m_shared, out snap) ? snap : null;
    }

    internal static TowerSnapshot SnapshotOfPrefab(GameObject prefab) =>
        prefab != null && SnapshotsByPrefab.TryGetValue(prefab, out var snap) ? snap : null;

    internal static TowerSnapshot SnapshotOfName(string prefab) =>
        prefab != null && SnapshotsByName.TryGetValue(prefab, out var snap) ? snap : null;

    // Hot path: tower in the left hand AND its copy carry tower data (TwoHandedWeaponLeft). Local player only (one
    // cache). Two reference compares + one int compare + one field read when nothing changed.
    internal static TowerInfo Held(Humanoid h)
    {
        var info = HeldListed(h);
        return info != null && h.m_leftItem.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
            ? info
            : null;
    }

    // Left item is a listed tower, applied or not yet (StartAttack heal).
    internal static TowerInfo HeldListed(Humanoid h)
    {
        var left = h.m_leftItem;
        if (left == null)
        {
            return null;
        }
        if (!ReferenceEquals(left, _heldItem) || _heldVersion != _version)
        {
            _heldItem = left;
            _heldVersion = _version;
            _heldInfo = TowerOf(left);
        }
        return _heldInfo;
    }

    private static void Bump()
    {
        unchecked
        {
            _version++;
        }
    }

    // Snapshot of this prefab, taken now if first time. Other prefab object under a known name (a mod re-registered
    // it) = new object never written by me: fresh snapshot. Not a shield on first sight = no snapshot (never written).
    private static TowerSnapshot SnapshotFor(string name, GameObject go, ItemDrop.ItemData.SharedData shared)
    {
        if (SnapshotsByName.TryGetValue(name, out var snap) && ReferenceEquals(snap.PrefabObject, go)
            && ReferenceEquals(snap.PrefabShared, shared))
        {
            return snap;
        }
        if (!TowerSnapshot.IsShieldData(shared.m_itemType, shared.m_animationState, shared.m_skillType))
        {
            return null;
        }
        if (snap != null)
        {
            SnapshotsByPrefab.Remove(snap.PrefabObject);
            SnapshotsByShared.Remove(snap.PrefabShared);
        }
        snap = new TowerSnapshot(name, go, shared);
        SnapshotsByName[name] = snap;
        SnapshotsByPrefab[go] = snap;
        SnapshotsByShared[shared] = snap;
        return snap;
    }

    // One Warning per kind of problem, once per rule set (a silent typo is a bug: Combat Adjustments' lesson).
    private static void WarnOnce(TowerRules rules, List<string> missing, List<string> notShield, List<string> parry)
    {
        if (rules.Key == _warnedKey)
        {
            return;
        }
        _warnedKey = rules.Key;
        var owner = ServerRules.UsingServer ? "The server's Towers setting" : "The Towers setting";
        if (missing != null)
        {
            Log.Warning($"{owner} names items this game does not have (MISSING), ignored: {string.Join(", ", missing.ToArray())}.");
        }
        if (notShield != null)
        {
            Log.Warning($"{owner} names items that are not shields, ignored: {string.Join(", ", notShield.ToArray())}. "
                        + "Only shields can be tower shields.");
        }
        if (parry != null)
        {
            Log.Warning($"{owner} names shields that can parry: {string.Join(", ", parry.ToArray())}. As tower "
                        + "shields they can no longer parry.");
        }
    }
}
