using System;
using System.Runtime.CompilerServices;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.ShieldsTowerWallMod;

// Me = write tower data into SharedData and take it back out (design 2.0, 2.0.1, 2.1, 7.3). SharedData is a copy per
// item object (1.9), so me write the ObjectDB prefab (items made later copy it) AND every live copy me can reach:
// local player inventory (equipped too), loaded containers, ground items. Copies made before (or missed) get healed
// on sight: EquipItem, StartAttack, GetTooltip prefixes. Every write start from the vanilla snapshot: idempotent.
// Only players' towers are towers (decision 25): creature copies go back to the snapshot.
internal static class TowerData
{
    // Copy written with the rules of this stamp (heal skip it). Weak table: copy freed = entry freed.
    private sealed class Mark
    {
        internal int Stamp;
    }

    private static readonly ConditionalWeakTable<ItemDrop.ItemData.SharedData, Mark> Marks =
        new ConditionalWeakTable<ItemDrop.ItemData.SharedData, Mark>();

    private static int _stamp = 1;

    // Every rules apply and every revert: old marks stale, heal write again.
    internal static void NewStamp()
    {
        unchecked
        {
            _stamp++;
        }
        if (_stamp == 0)
        {
            _stamp = 1;
        }
    }

    // Design 2.0.1 table, from snapshot + rules. m_attack = shared bash when it exist, else vanilla attack (empty
    // animation: StartAttack prefix build the bash and set it before vanilla look).
    internal static void Apply(ItemDrop.ItemData.SharedData s, TowerInfo t, TowerRules r)
    {
        var v = t.Snapshot;
        s.m_itemType = ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft;
        s.m_attachOverride = ItemDrop.ItemData.ItemType.Shield;
        s.m_timedBlockBonus = Mathf.Min(v.TimedBlockBonus, 1f);
        s.m_perfectBlockAdrenaline = 0f;
        s.m_blockPower = v.BlockPower * r.BlockArmorMultiplier;
        s.m_blockPowerPerLevel = v.BlockPowerPerLevel * r.BlockArmorMultiplier;
        var force = r.BlockForcePercent / 100f;
        s.m_deflectionForce = v.DeflectionForce * force;
        s.m_deflectionForcePerLevel = v.DeflectionForcePerLevel * force;
        s.m_movementModifier = -r.CarrySlowPercent / 100f;
        s.m_damages = new HitData.DamageTypes { m_blunt = t.BashDamage };
        s.m_damagesPerLevel = default;
        s.m_attackForce = r.BashKnockback;
        s.m_blockable = true;
        s.m_dodgeable = true;
        s.m_backstabBonus = 1f;
        s.m_buildBlockCharges = false;
        var bash = BashAttack.Current;
        s.m_attack = bash ?? v.Attack;
    }

    // Snapshot back (same fields as Apply). Mark gone: a later heal write again.
    internal static void Revert(ItemDrop.ItemData.SharedData s, TowerSnapshot v)
    {
        s.m_itemType = v.ItemType;
        s.m_attachOverride = v.AttachOverride;
        s.m_timedBlockBonus = v.TimedBlockBonus;
        s.m_perfectBlockAdrenaline = v.PerfectBlockAdrenaline;
        s.m_blockPower = v.BlockPower;
        s.m_blockPowerPerLevel = v.BlockPowerPerLevel;
        s.m_deflectionForce = v.DeflectionForce;
        s.m_deflectionForcePerLevel = v.DeflectionForcePerLevel;
        s.m_movementModifier = v.MovementModifier;
        s.m_damages = v.Damages;
        s.m_damagesPerLevel = v.DamagesPerLevel;
        s.m_attackForce = v.AttackForce;
        s.m_blockable = v.Blockable;
        s.m_dodgeable = v.Dodgeable;
        s.m_backstabBonus = v.BackstabBonus;
        s.m_buildBlockCharges = v.BuildBlockCharges;
        s.m_attack = v.Attack;
        Marks.Remove(s);
    }

    // Heal on sight (players): bring this copy in line with the applied rules. Listed tower = tower data (once per
    // stamp) + current bash; was a tower but not listed now (or no rules) = snapshot back. Source = who hold it (bash
    // template from its unarmed attack); null = local player.
    internal static void Heal(ItemDrop.ItemData item, Humanoid source)
    {
        if (item == null || item.m_shared == null)
        {
            return;
        }
        var s = item.m_shared;
        var info = TowerCatalog.TowerOf(item);
        var rules = TowerSync.Applied;
        if (info == null || rules == null)
        {
            var snap = TowerCatalog.SnapshotOf(item);
            if (snap != null && s.m_itemType != snap.ItemType)
            {
                Revert(s, snap);
            }
            return;
        }
        if (!IsMarked(s))
        {
            Apply(s, info, rules);
            SetMark(s);
        }
        var bash = BashAttack.Get(source);
        if (bash != null && !ReferenceEquals(s.m_attack, bash))
        {
            s.m_attack = bash;
        }
    }

    // Non-player humanoid pick up / equip a tower: its own copy stay vanilla (decision 25).
    internal static void RevertCopy(ItemDrop.ItemData item)
    {
        var snap = TowerCatalog.SnapshotOf(item);
        if (snap != null && item.m_shared != null)
        {
            Revert(item.m_shared, snap);
        }
    }

    // True = this item carry tower data of the applied rules.
    internal static bool IsAppliedTower(ItemDrop.ItemData item) =>
        item != null && item.m_shared != null
        && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
        && TowerCatalog.TowerOf(item) != null;

    // Rules apply (catalog already rebuilt for these rules; null rules = vanilla everywhere). Prefabs first (items
    // made later copy them), then every live copy. Each item alone: one bad item never stop the pass.
    internal static void ApplyAll(TowerRules rules)
    {
        var failed = 0;
        foreach (var snap in TowerCatalog.Snapshots)
        {
            try
            {
                var info = rules != null ? TowerCatalog.TowerOfPrefab(snap.PrefabObject) : null;
                if (info != null)
                {
                    Apply(snap.PrefabShared, info, rules);
                    SetMark(snap.PrefabShared);
                }
                else
                {
                    Revert(snap.PrefabShared, snap);
                }
            }
            catch (Exception e)
            {
                failed++;
                PatchGuard.Report("TowerData.ApplyAll prefab", e);
            }
        }
        var player = Player.m_localPlayer;
        if (player != null)
        {
            failed += FixInventory(player.GetInventory(), rules);
        }
        failed += FixWorld(rules, revert: false);
        WarnFailed(failed);
    }

    // Deactivation step: local player's inventory (equipped items too) back to vanilla.
    internal static void RevertInventory(Player player)
    {
        if (player == null)
        {
            return;
        }
        WarnFailed(FixInventory(player.GetInventory(), null));
    }

    // Deactivation step: prefabs back to vanilla.
    internal static void RevertPrefabs()
    {
        var failed = 0;
        foreach (var snap in TowerCatalog.Snapshots)
        {
            try
            {
                Revert(snap.PrefabShared, snap);
            }
            catch (Exception e)
            {
                failed++;
                PatchGuard.Report("TowerData.RevertPrefabs", e);
            }
        }
        WarnFailed(failed);
    }

    // Deactivation step (not while quitting): loaded containers and ground items back to vanilla.
    internal static void RevertWorld()
    {
        WarnFailed(FixWorld(null, revert: true));
    }

    // Tower in the left hand (or hidden left) + anything in the right hand (or hidden right) = right one goes
    // (decision 15: the tower is what the mod changed). Hidden right item: UnequipItem clear the hidden slot and
    // refresh the look (vanilla), so showing hands again never drop the tower, weapon not left drawn on the back.
    internal static void FixHands(Player player)
    {
        if (player == null)
        {
            return;
        }
        if (!IsAppliedTower(player.m_leftItem) && !IsAppliedTower(player.m_hiddenLeftItem))
        {
            return;
        }
        if (player.m_rightItem != null)
        {
            player.UnequipItem(player.m_rightItem);
        }
        if (player.m_hiddenRightItem != null)
        {
            player.UnequipItem(player.m_hiddenRightItem, triggerEquipEffects: false);
        }
    }

    private static bool IsMarked(ItemDrop.ItemData.SharedData s) =>
        Marks.TryGetValue(s, out var mark) && mark.Stamp == _stamp;

    private static void SetMark(ItemDrop.ItemData.SharedData s) => Marks.GetOrCreateValue(s).Stamp = _stamp;

    // One item: listed tower = apply, other snapshot item = revert, anything else untouched. Rules null = revert.
    private static void Fix(ItemDrop.ItemData item, TowerRules rules)
    {
        if (item == null || item.m_shared == null)
        {
            return;
        }
        var snap = TowerCatalog.SnapshotOf(item);
        if (snap == null)
        {
            return;
        }
        var info = rules != null ? TowerCatalog.TowerOf(item) : null;
        if (info != null)
        {
            Apply(item.m_shared, info, rules);
            SetMark(item.m_shared);
        }
        else
        {
            Revert(item.m_shared, snap);
        }
    }

    private static int FixInventory(Inventory inventory, TowerRules rules)
    {
        if (inventory == null)
        {
            return 0;
        }
        var failed = 0;
        foreach (var item in inventory.GetAllItems())
        {
            try
            {
                Fix(item, rules);
            }
            catch (Exception e)
            {
                failed++;
                PatchGuard.Report("TowerData.FixInventory", e);
            }
        }
        return failed;
    }

    // Loaded containers (chests, carts, ships, tombstones) and ground items. Revert = rules ignored.
    private static int FixWorld(TowerRules rules, bool revert)
    {
        var failed = 0;
        var use = revert ? null : rules;
        Container[] containers = null;
        try
        {
            containers = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None);
        }
        catch (Exception e)
        {
            failed++;
            PatchGuard.Report("TowerData.FixWorld containers", e);
        }
        if (containers != null)
        {
            foreach (var container in containers)
            {
                try
                {
                    if (container != null)
                    {
                        failed += FixInventory(container.GetInventory(), use);
                    }
                }
                catch (Exception e)
                {
                    failed++;
                    PatchGuard.Report("TowerData.FixWorld container", e);
                }
            }
        }
        var drops = ItemDrop.s_instances;
        for (var i = 0; i < drops.Count; i++)
        {
            try
            {
                var drop = drops[i];
                if (drop != null)
                {
                    Fix(drop.m_itemData, use);
                }
            }
            catch (Exception e)
            {
                failed++;
                PatchGuard.Report("TowerData.FixWorld ground item", e);
            }
        }
        return failed;
    }

    private static void WarnFailed(int failed)
    {
        if (failed > 0)
        {
            Log.Warning($"{failed} tower shield item(s) could not be updated; they keep their old values until they "
                        + "are equipped again. See the error above.");
        }
    }
}
