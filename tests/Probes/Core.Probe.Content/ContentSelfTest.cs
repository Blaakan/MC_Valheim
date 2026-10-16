#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using MC.Shared;
using UnityEngine;

namespace MC.Core.ProbeContentMod;

// Me = probe.content: the stand-in content is really in the game, so a mod test that miss it is the mod's fault.
// Per prefab: in ObjectDB (items) or its piece table (pieces), in ZNetScene, its recipe (when its source has one), its
// data as the spec want (late edits too), and an inventory copy made by name point back at the clone (m_dropPrefab).
// After-load prefabs: waited for (they come a few seconds after spawn). Note: which real content mods are installed.
internal static class ContentSelfTest
{
    internal const string Name = "probe.content";
    private const float AfterLoadWait = 20f;

    private static bool _registered;

    // Known real content mods (GUID -> name) that tests can check besides the stand-in content.
    private static readonly Dictionary<string, string> RealMods = new Dictionary<string, string>
    {
        { "Therzie.Warfare", "Warfare" },
        { "com.jotunn.jotunn", "Jotunn" },
    };

    internal static void Register()
    {
        if (_registered)
        {
            return;
        }
        _registered = true;
        SelfTest.Register(Name, Run);
    }

    private static IEnumerator Run()
    {
        var failures = new List<string>();
        var count = 0;
        void Check(bool ok, string problem)
        {
            count++;
            if (!ok)
            {
                failures.Add(problem);
            }
        }

        var real = new List<string>();
        foreach (var pair in RealMods)
        {
            if (Chainloader.PluginInfos.TryGetValue(pair.Key, out var info))
            {
                real.Add($"{pair.Value} {info.Metadata.Version}");
            }
        }
        SelfTest.Note(Name, $"real third-party content mods installed: {(real.Count == 0 ? "none" : string.Join(", ", real.ToArray()))}");

        var until = Time.time + AfterLoadWait;
        while (!SelfTestContent.AfterLoadIn && Time.time < until)
        {
            yield return null;
        }

        var db = ObjectDB.instance;
        var scene = ZNetScene.instance;
        var player = Player.m_localPlayer;
        Check(db != null && scene != null && player != null, "no ObjectDB, ZNetScene or local player");
        if (db == null || scene == null || player == null)
        {
            Report(failures, count);
            yield break;
        }
        foreach (var spec in ContentSpecs.All)
        {
            var clone = ContentRegistry.CloneOf(spec.Name);
            Check(clone != null, $"{spec.Name}: never made (source {spec.Source} not found?)");
            if (clone == null)
            {
                continue;
            }
            Check(scene.GetPrefab(spec.Name) == clone, $"{spec.Name}: not in ZNetScene");
            if (spec.Kind == ContentKind.Item)
            {
                CheckItem(spec, clone, db, player, Check);
            }
            if (spec.Expect != null)
            {
                var problem = spec.Expect(clone);
                Check(problem == null, $"{spec.Name}: {problem}");
            }
        }
        Report(failures, count);
    }

    private static void CheckItem(ContentSpec spec, GameObject clone, ObjectDB db, Player player, Action<bool, string> check)
    {
        check(db.GetItemPrefab(spec.Name) == clone, $"{spec.Name}: not in ObjectDB");
        var shared = ContentSpecs.Shared(clone);
        check(shared != null && db.GetItemPrefab(shared) == clone, $"{spec.Name}: ObjectDB does not find it by its item data (UpdateRegisters?)");
        var hasSourceRecipe = false;
        var hasRecipe = false;
        foreach (var r in db.m_recipes)
        {
            if (r == null || r.m_item == null)
            {
                continue;
            }
            hasSourceRecipe |= r.m_item.name == spec.Source;
            hasRecipe |= r.m_item.gameObject == clone && r.m_enabled;
        }
        if (spec.Recipe && hasSourceRecipe)
        {
            check(hasRecipe, $"{spec.Name}: no enabled recipe");
        }
        var inv = player.GetInventory();
        var item = inv.AddItem(spec.Name, 1, 1, 0, 0L, "", false);
        check(item != null, $"{spec.Name}: Inventory.AddItem by name gave nothing");
        if (item != null)
        {
            check(item.m_dropPrefab == clone, $"{spec.Name}: the item made by name does not point back at the clone (m_dropPrefab {(item.m_dropPrefab != null ? item.m_dropPrefab.name : "null")})");
            check(shared != null && item.m_shared.m_name == shared.m_name, $"{spec.Name}: the item made by name has another name ({item.m_shared.m_name})");
            inv.RemoveItem(item);
        }
    }

    private static void Report(List<string> failures, int count)
    {
        if (failures.Count == 0 && count > 0)
        {
            SelfTest.Pass(Name, $"{count} checks OK: {ContentSpecs.All.Count} stand-in prefab(s) in the game");
        }
        else
        {
            SelfTest.Fail(Name, $"{failures.Count} of {count} checks failed: {string.Join("; ", failures.ToArray())}");
        }
    }
}
#endif
