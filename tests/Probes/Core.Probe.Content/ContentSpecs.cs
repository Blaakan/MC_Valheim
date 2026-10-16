#if DEBUG
using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Core.ProbeContentMod;

// How a real library put content in the game (ContentRegistry has one hook per way).
internal enum Way
{
    ItemManager,
    JotunnLateEdit,
    JotunnPiece,
    AfterLoad,
}

internal enum ContentKind
{
    Item,
    Piece,
    Creature,
    Other,
}

// One stand-in prefab: clone of a vanilla prefab under a new name, changed by Edit (at clone time, or in the late
// postfix for the JotunnLateEdit way).
internal sealed class ContentSpec
{
    internal string Name;
    internal string Source;
    internal ContentKind Kind;
    internal Way Way;
    internal Action<GameObject> Edit;
    internal Func<GameObject, string> Expect;  // probe.content: null = clone data as wanted, else the problem
    internal bool Recipe;          // items: copy the source's recipe for the clone (station, resources), enabled
    internal string PieceTable;    // JotunnPiece: tool item whose piece table get the piece (e.g. Hammer)
}

// Me = the stand-in content list. Names in src/Shared/SelfTestContent.cs (mods' tests read them there).
internal static class ContentSpecs
{
    internal static readonly List<ContentSpec> All = new List<ContentSpec>
    {
        // Shields: tower data like Warfare's (ShieldBronzeTower_TW: parry 0, movement -0.2, block force 100). Expect
        // read the vanilla values the mods' snapshots keep (Tower Shield Wall write the prefab while on).
        Item(SelfTestContent.TowerShield, "ShieldIronTower", Way.ItemManager, go => Shield(go, "Stand-in tower shield", 0f, -0.2f)),
        Item(SelfTestContent.LateEditTower, "ShieldBanded", Way.JotunnLateEdit, go => Shield(go, "Stand-in late-edit tower shield", 0f, -0.2f)),
        // After load: clone of a shield no MC mod writes (a vanilla tower is already written by Tower Shield Wall by
        // then, and a copy of written data is a known limit, docs/research/third-party-content-audit.md). Block 32.
        Item(SelfTestContent.AfterLoadTower, "ShieldBanded", Way.AfterLoad, go =>
        {
            Shield(go, "Stand-in after-load tower shield", 0f, -0.2f);
            Shared(go).m_blockPower = 32f;
        }),
        Item(SelfTestContent.RoundShield, "ShieldBanded", Way.ItemManager, go => Rename(go, "Stand-in round shield")),

        // Weapons.
        Item(SelfTestContent.SwordPlain, "SwordBronze", Way.ItemManager, go => Rename(go, "Stand-in sword")),
        Item(SelfTestContent.SwordCustomSwing, "SwordIron", Way.ItemManager, go =>
        {
            Rename(go, "Stand-in sword with its own swing");
            Shared(go).m_attack.m_attackAnimation = SelfTestContent.CustomSwingName;
        }),
        Item(SelfTestContent.ReloadGun, "CrossbowArbalest", Way.ItemManager, go =>
        {
            Rename(go, "Stand-in reload gun");
            Shared(go).m_skillType = SelfTestContent.CustomSkill;
        }),
        Item(SelfTestContent.CustomSkillAxe, "AxeBronze", Way.ItemManager, go =>
        {
            Rename(go, "Stand-in throwing axe");
            Shared(go).m_skillType = SelfTestContent.CustomSkill;
        }),

        // Creature content. Trophy before the creature: the creature's Edit look its clone up.
        Item(SelfTestContent.Trophy, "TrophyBoar", Way.ItemManager, go => Rename(go, "Stand-in trophy"), recipe: false),
        new ContentSpec
        {
            Name = SelfTestContent.Creature, Source = "Boar", Kind = ContentKind.Creature, Way = Way.ItemManager,
            Edit = StandInCreature,
            Expect = go => go.GetComponent<Character>() == null ? "no Character" : ContentRegistry.SpawnListProblem(),
        },

        // Piece.
        new ContentSpec
        {
            Name = SelfTestContent.Torch, Source = "piece_groundtorch_wood", Kind = ContentKind.Piece, Way = Way.JotunnPiece,
            PieceTable = "Hammer", Edit = go => go.GetComponent<Piece>().m_name = "Stand-in torch",
            Expect = ContentRegistry.PieceProblem,
        },
    };

    // Creature: own name, drops the stand-in trophy instead of the boar's.
    private static void StandInCreature(GameObject go)
    {
        var c = go.GetComponent<Character>();
        if (c != null)
        {
            c.m_name = "Stand-in creature";
        }
        var drops = go.GetComponent<CharacterDrop>();
        var trophy = ContentRegistry.CloneOf(SelfTestContent.Trophy);
        if (drops == null || trophy == null)
        {
            return;
        }
        foreach (var d in drops.m_drops)
        {
            if (d != null && d.m_prefab != null && d.m_prefab.name == "TrophyBoar")
            {
                d.m_prefab = trophy;
            }
        }
    }

    static ContentSpecs()
    {
        foreach (var s in All)
        {
            if (s.Kind == ContentKind.Item && s.Expect == null)
            {
                s.Expect = go => Shared(go) == null ? "no ItemDrop" : null;
            }
        }
    }

    internal static List<string> Describe()
    {
        var list = new List<string>();
        foreach (var s in All)
        {
            list.Add($"{s.Name} ({s.Kind}, {s.Way}, from {s.Source})");
        }
        return list;
    }

    internal static ContentSpec Item(string name, string source, Way way, Action<GameObject> edit, bool recipe = true) =>
        new ContentSpec { Name = name, Source = source, Kind = ContentKind.Item, Way = way, Edit = edit, Recipe = recipe };

    internal static ItemDrop.ItemData.SharedData Shared(GameObject go)
    {
        var drop = go != null ? go.GetComponent<ItemDrop>() : null;
        return drop != null ? drop.m_itemData.m_shared : null;
    }

    // Own display name (plain text: not a $token, shown as is), so no stack or compare mixes it with the vanilla item.
    private static void Rename(GameObject go, string display)
    {
        var s = Shared(go);
        if (s != null)
        {
            s.m_name = display;
        }
    }

    private static void Shield(GameObject go, string display, float parryBonus, float movement)
    {
        Rename(go, display);
        var s = Shared(go);
        if (s == null)
        {
            return;
        }
        s.m_timedBlockBonus = parryBonus;
        s.m_movementModifier = movement;
        s.m_deflectionForce = 100f;
    }
}
#endif
