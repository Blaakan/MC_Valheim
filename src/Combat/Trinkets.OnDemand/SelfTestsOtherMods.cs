#if DEBUG
using System.Collections;
using BepInEx.Bootstrap;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.TrinketsOnDemandMod;

// Debug build only. Checks with other MC mods loaded in same game (same class as SelfTests.cs). Soft: other mod
// found by GUID in shared registry and its settings through BepInEx, never through its types. Most of them sit
// in test of item they belong to:
//   Tower Shield Wall      trinkets.block-fight (tower shield block)
//   Crossbow Stays Loaded  trinkets.crossbow-shot (second loaded crossbow)
//   Harpoon Hooks Tames    trinkets.other-ranged (harpoon on tamed Boar)
//   Music Instruments      trinkets.gates (keys held by its free play)
//   Sneak Ambush, Creature Morale: fight's end without hits is trinkets.block-fight / trinkets.targeting
// Here:
//   trinkets.dual-wield    X01: default keys of two mods differ; two KnifeCopper dual wielded: full bar held
//                          through swing that misses and one that hits; one adrenaline call per swing that hits
internal static partial class SelfTests
{
    private const string DualWieldName = "trinkets.dual-wield";

    // Default value of key setting of another plugin (BepInEx config, no type of theirs), or None.
    private static KeyCode DefaultKeyOf(string guid, string section, string key)
    {
        if (!Chainloader.PluginInfos.TryGetValue(guid, out var info) || info.Instance == null)
        {
            return KeyCode.None;
        }
        return info.Instance.Config.TryGetEntry<KeyCode>(section, key, out var entry) ? (KeyCode)entry.DefaultValue : KeyCode.None;
    }

    private static IEnumerator RunDualWield()
    {
        if (!ModActive(DualWieldGuid))
        {
            // No pass without a check: skipped test must not read as proof (same as other-mod checks elsewhere).
            SelfTest.Fail(DualWieldName, "Dual Wielding is not loaded and active: nothing of TESTING.md X01 was checked");
            yield break;
        }
        var rig = Rig.Create(DualWieldName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(DualWieldName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            ServerRules.TestRules = new TrinketRules();

            // Default keys: H swaps hands, Left Alt picks main hand, Y fires trinket.
            var swap = DefaultKeyOf(DualWieldGuid, "Controls", "SwapHandsKey");
            var main = DefaultKeyOf(DualWieldGuid, "Controls", "MainHandKey");
            var trigger = Plugin.TriggerKey != null ? (KeyCode)Plugin.TriggerKey.DefaultValue : KeyCode.None;
            c.Check(swap != KeyCode.None && trigger != KeyCode.None && swap != trigger && main != trigger,
                $"default keys: Dual Wielding swaps hands with {swap} (main hand {main}), the trinket trigger is {trigger}");

            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var fwd = Flat(p.transform.forward);
            var dummy = rig.Creature(DummyName, fwd * 1.3f);
            var first = rig.Give(KnifeName);
            var second = rig.Give(KnifeName);
            if (!c.Check(item != null && dummy != null && first != null && second != null,
                    $"could not equip a trinket, give two {KnifeName} or spawn {DummyName}"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();
            rig.EmptyHands();
            rig.TakeControls();
            rig.Moves();
            p.EquipItem(first, false);
            p.EquipItem(second, false);
            yield return FixedTicks(3);
            if (!c.Check(ReferenceEquals(p.m_rightItem, first) && ReferenceEquals(p.m_leftItem, second),
                    $"two {KnifeName} are not held one in each hand (Dual Wielding active)"))
            {
                c.Report();
                yield break;
            }
            var started = new bool[1];

            // Full bar, swing that misses.
            Face(p, -fwd);
            yield return FixedTicks(2);
            p.m_adrenaline = max;
            var calls = FullBar.Calls;
            yield return Swing(p, false, started);
            c.Check(started[0] && FullBar.Calls > calls, "the dual-wield swing at nothing did not reach the game's adrenaline call");
            c.Check(Near(p.m_adrenaline, max) && !rig.Has(hash), $"full bar + a dual-wield swing that misses: bar {F(p.m_adrenaline)} of {F(max)}, effect {rig.Has(hash)}");

            // Swing that hits (training dummy at arm's length): held on full bar; one adrenaline call per swing.
            Face(p, dummy.transform.position - p.transform.position);
            yield return FixedTicks(2);
            p.m_adrenaline = max;
            calls = FullBar.Calls;
            dummy.m_localPlayerHasHit = false;
            yield return Swing(p, false, started);
            if (started[0] && dummy.m_localPlayerHasHit)
            {
                c.Check(Near(p.m_adrenaline, max) && !rig.Has(hash), $"full bar + a dual-wield swing that hits: bar {F(p.m_adrenaline)} of {F(max)}, effect {rig.Has(hash)}");
                c.Check(FullBar.Calls == calls + 1, $"a dual-wield swing that hit made {FullBar.Calls - calls} adrenaline call(s), expected one per swing");
            }
            else
            {
                c.Note("the dual-wield swing did not reach the training dummy: only the miss was checked");
            }
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }
}
#endif
