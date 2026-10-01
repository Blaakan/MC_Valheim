using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace MC.Combat.TrinketsOnDemandMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1
// -Mod Trinkets). Design 7.5:
//   trinkets.network        rules on the wire (round trip, clamping, layout refusal), pending pick, push debounce,
//                           join check verdicts (pure)
//   trinkets.controls       every gamepad name me use exists in ZInput, key check, labels, same-button list (RS swallow),
//                           foreign trinket mod markers (blockers, and Compat's: Surge only by whole name or exact GUID)
//   trinkets.pending        rules pending = vanilla: full bar pops at once, key does nothing, no income, bar drains,
//                           no tooltip line, no ranged bonus; pending over = bar waits again
//   trinkets.hold-and-fire  real trinket: NOTE dump of every item with a full-adrenaline effect and of the Player
//                           adrenaline data; full bar held on gain / 0 call / cross-max gain / above max, effect put
//                           back, stats unchanged, tooltip line; no drain; press: not full, fire, refuse while
//                           active, refresh, no trinket; feedback flash + message on a full bar
//   trinkets.income         income only in a fight, fills to max and waits, stops after the linger, 0 = off
//   trinkets.combat-state   InCombat timing (pure); live hooks: hit a Greydwarf / be hit = fight, training dummy,
//                           tamed and no attacker = no fight, alerted targeting stamps
//   trinkets.ranged-factor  formula (pure) + NOTE dump of bow draw / crossbow reload / arrow and bolt adrenaline, and
//                           melee pay of player weapons by skill (for the RangedReferenceSeconds default)
//   trinkets.ranged-shot    real full-draw Bow shot: arrow's m_adrenaline = base x factor from the formula
// Me force rules only with ServerRules.TestRules / TestPending, the key with Controls.TestPress: never config. Rig put
// back player (equipment, ammo, bar, drain timer, effects, controls), and destroy all me spawn.
internal static class SelfTests
{
    private const string NetworkName = "trinkets.network";
    private const string ControlsName = "trinkets.controls";
    private const string PendingName = "trinkets.pending";
    private const string HoldName = "trinkets.hold-and-fire";
    private const string IncomeName = "trinkets.income";
    private const string CombatName = "trinkets.combat-state";
    private const string RangedName = "trinkets.ranged-factor";
    private const string ShotName = "trinkets.ranged-shot";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(NetworkName, RunNetwork);
        SelfTest.Register(ControlsName, RunControls);
        SelfTest.Register(PendingName, RunPending);
        SelfTest.Register(HoldName, RunHoldAndFire);
        SelfTest.Register(IncomeName, RunIncome);
        SelfTest.Register(CombatName, RunCombatState);
        SelfTest.Register(RangedName, RunRangedFactor);
        SelfTest.Register(ShotName, RunRangedShot);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(NetworkName);
        SelfTest.Unregister(ControlsName);
        SelfTest.Unregister(PendingName);
        SelfTest.Unregister(HoldName);
        SelfTest.Unregister(IncomeName);
        SelfTest.Unregister(CombatName);
        SelfTest.Unregister(RangedName);
        SelfTest.Unregister(ShotName);
        ClearOverrides();
#endif
    }

#if DEBUG
    // Vanilla names me use in tests (Greydwarf, piece_TrainingDummy, Bow: checked in Sneak Ambush's data; ArrowWood and
    // CrossbowArbalest: seen in the in-world run of 2026-10-01; trinket prefab found at run time).
    private const string FoeName = "Greydwarf";
    private const string DummyName = "piece_TrainingDummy";
    private const string BowName = "Bow";
    private const string ArrowName = "ArrowWood";
    private const string CrossbowName = "CrossbowArbalest";
    private const string TooltipMark = "\nTrigger: ";

    private static void ClearOverrides()
    {
        ServerRules.TestPending = false;
        ServerRules.TestRules = null;
        Controls.TestPress = false;
    }

    // ---------- helpers ----------

    private sealed class Checks
    {
        private readonly string _name;
        private readonly List<string> _failures = new List<string>();
        private int _count;

        internal Checks(string name) => _name = name;

        internal bool Check(bool ok, string what)
        {
            _count++;
            if (!ok)
            {
                _failures.Add(what);
            }
            return ok;
        }

        internal void Note(string detail) => SelfTest.Note(_name, detail);

        internal void Report(string extra = "")
        {
            if (_failures.Count == 0 && _count > 0)
            {
                SelfTest.Pass(_name, $"{_count} checks OK{extra}");
            }
            else if (_failures.Count == 0)
            {
                SelfTest.Fail(_name, "no check ran");
            }
            else
            {
                SelfTest.Fail(_name, $"{_failures.Count} of {_count} checks failed: {string.Join("; ", _failures.ToArray())}");
            }
        }
    }

    // Me = what one test change on the player and the world. Restore (finally, no yield) put everything back.
    private sealed class Rig
    {
        internal readonly Player P;
        internal readonly Inventory Inv;
        private readonly ItemDrop.ItemData _trinket;
        private readonly ItemDrop.ItemData _right;
        private readonly ItemDrop.ItemData _left;
        private readonly ItemDrop.ItemData _ammo;
        private readonly float _adrenaline;
        private readonly float _degenTimer;
        private readonly HashSet<int> _tiersBefore = new HashSet<int>();
        private readonly List<ItemDrop.ItemData> _items = new List<ItemDrop.ItemData>();
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly HashSet<int> _effects = new HashSet<int>();
        private PlayerController _controller;
        private bool _controllerEnabled;

        private Rig(Player player)
        {
            P = player;
            Inv = player.GetInventory();
            _trinket = player.m_trinketItem;
            _right = player.m_rightItem;
            _left = player.m_leftItem;
            _ammo = player.m_ammoItem;
            _adrenaline = player.m_adrenaline;
            _degenTimer = player.m_adrenalineDegenTimer;
            foreach (var tier in player.m_adrenalineEffects)
            {
                if (tier.m_se != null && player.GetSEMan().HaveStatusEffect(tier.m_se.NameHash()))
                {
                    _tiersBefore.Add(tier.m_se.NameHash());
                }
            }
        }

        internal static Rig Create(string test)
        {
            var player = Player.m_localPlayer;
            if (player == null || ObjectDB.instance == null || ZNetScene.instance == null || ZNet.instance == null)
            {
                SelfTest.Fail(test, "no local player or no world");
                return null;
            }
            CombatState.Reset();
            FullBar.LastResult = TriggerResult.None;
            return new Rig(player);
        }

        internal ItemDrop.ItemData Give(string prefab, int stack = 1)
        {
            var item = Inv.AddItem(prefab, stack, 1, 0, 0L, "", false);
            if (item != null && !_items.Contains(item))
            {
                _items.Add(item);
            }
            return item;
        }

        // Trinket from the prefab, equipped, max refreshed at once (vanilla refresh it next FixedUpdate).
        internal ItemDrop.ItemData EquipTrinket(string prefab)
        {
            if (P.m_trinketItem != null)
            {
                P.UnequipItem(P.m_trinketItem, false);
            }
            var item = Give(prefab);
            if (item == null || !P.EquipItem(item, false))
            {
                return null;
            }
            P.UpdateModifiers();
            return item;
        }

        internal void EmptyHands()
        {
            if (P.m_rightItem != null)
            {
                P.UnequipItem(P.m_rightItem, false);
            }
            if (P.m_leftItem != null)
            {
                P.UnequipItem(P.m_leftItem, false);
            }
        }

        // Effect this test may start: removed now and at the end.
        internal void Effect(int hash)
        {
            _effects.Add(hash);
            P.GetSEMan().RemoveStatusEffect(hash, true);
        }

        internal bool Has(int hash) => P.GetSEMan().HaveStatusEffect(hash);

        // Creature ahead (or to the side), AI off at once: no walk, no target, no attack.
        internal Character Creature(string prefab, Vector3 offset)
        {
            var go = ZNetScene.instance.GetPrefab(prefab);
            if (go == null)
            {
                return null;
            }
            var pos = P.transform.position + offset;
            pos.y = Mathf.Max(pos.y, ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(pos) : pos.y);
            var obj = Object.Instantiate(go, pos, Quaternion.LookRotation(-Flat(offset)));
            _spawned.Add(obj);
            var ai = obj.GetComponent<BaseAI>();
            if (ai != null)
            {
                ai.enabled = false;
            }
            var wear = obj.GetComponent<WearNTear>();
            if (wear != null)
            {
                wear.enabled = false; // training dummy is a piece: never break, never drop wood
            }
            return obj.GetComponent<Character>();
        }

        // Me drive the player (no keyboard in between).
        internal void TakeControls()
        {
            if (_controller == null)
            {
                _controller = P.GetComponent<PlayerController>();
                if (_controller != null)
                {
                    _controllerEnabled = _controller.enabled;
                    _controller.enabled = false;
                }
            }
            Drive(false);
        }

        internal void Drive(bool attackHold) =>
            P.SetControls(Vector3.zero, false, attackHold, false, false, false, false, false, false, false, false);

        internal void Restore()
        {
            Safe("overrides", ClearOverrides);
            Safe("controls", () =>
            {
                if (_controller != null)
                {
                    Drive(false);
                    _controller.enabled = _controllerEnabled;
                }
            });
            Safe("effects", () =>
            {
                foreach (var hash in _effects)
                {
                    P.GetSEMan().RemoveStatusEffect(hash, true);
                }
            });
            Safe("items", () =>
            {
                foreach (var item in _items)
                {
                    if (item == null)
                    {
                        continue;
                    }
                    if (P.IsItemEquiped(item) || ReferenceEquals(P.m_ammoItem, item))
                    {
                        P.UnequipItem(item, false);
                    }
                    if (Inv.ContainsItem(item))
                    {
                        Inv.RemoveItem(item);
                    }
                }
                _items.Clear();
            });
            Safe("equipment", () =>
            {
                foreach (var item in new[] { _right, _left, _trinket, _ammo })
                {
                    if (item != null && Inv.ContainsItem(item) && !P.IsItemEquiped(item))
                    {
                        P.EquipItem(item, false);
                    }
                }
            });
            Safe("spawned", () =>
            {
                foreach (var go in _spawned)
                {
                    DestroyObject(go);
                }
                _spawned.Clear();
            });
            Safe("bar", () =>
            {
                P.UpdateModifiers();
                P.m_adrenaline = _adrenaline;
                P.m_adrenalineDegenTimer = _degenTimer;
                foreach (var tier in P.m_adrenalineEffects)
                {
                    if (tier.m_se != null && !_tiersBefore.Contains(tier.m_se.NameHash()))
                    {
                        P.GetSEMan().RemoveStatusEffect(tier.m_se.NameHash(), true);
                    }
                }
            });
            Safe("state", () =>
            {
                CombatState.Reset();
                RangedBonus.Reset();
                Feedback.Reset();
                FullBar.LastResult = TriggerResult.None;
            });
        }

        private static void Safe(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up ({what}) failed: {e}");
            }
        }
    }

    private static IEnumerator Frames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return null;
        }
    }

    // Press the trigger through the real Player.Update postfix and wait for its answer.
    private static IEnumerator Press()
    {
        FullBar.LastResult = TriggerResult.None;
        Controls.TestPress = true;
        for (var i = 0; i < 10 && FullBar.LastResult == TriggerResult.None; i++)
        {
            yield return null;
        }
        Controls.TestPress = false;
    }

    // First item in ObjectDB (by name) of type Trinket with a full-adrenaline effect and a cost; any such item else.
    private static string FindTrinket(Checks c, bool dump)
    {
        var rows = new List<string>();
        string pick = null;
        string fallback = null;
        foreach (var go in ObjectDB.instance.m_items.Where(g => g != null).OrderBy(g => g.name, StringComparer.Ordinal))
        {
            var drop = go.GetComponent<ItemDrop>();
            var shared = drop != null ? drop.m_itemData.m_shared : null;
            var se = shared != null ? shared.m_fullAdrenalineSE : null;
            if (se == null)
            {
                continue;
            }
            if (dump)
            {
                var stats = se as SE_Stats;
                rows.Add($"{go.name} ({shared.m_itemType}, cost {F(shared.m_maxAdrenaline)}, effect {se.name} "
                         + $"[{se.GetType().Name}] ttl {F(se.m_ttl)}"
                         + (stats != null ? $", up-front {F(stats.m_adrenalineUpFront)}, gain modifier {F(stats.m_adrenalineModifier)}" : "")
                         + $", equip effect {(shared.m_equipStatusEffect != null ? shared.m_equipStatusEffect.name : "none")})");
            }
            if (pick == null && shared.m_itemType == ItemDrop.ItemData.ItemType.Trinket && shared.m_maxAdrenaline > 0f)
            {
                pick = go.name;
            }
            if (fallback == null && shared.m_maxAdrenaline > 0f)
            {
                fallback = go.name;
            }
        }
        if (dump)
        {
            c.Note($"{rows.Count} items with a full-adrenaline effect: {string.Join("; ", rows.ToArray())}");
        }
        return pick ?? fallback;
    }

    private static string Keys(AnimationCurve curve)
    {
        if (curve == null)
        {
            return "none";
        }
        return string.Join(" ", curve.keys.Select(k => F(k.time) + ":" + F(k.value)).ToArray());
    }

    private static void DumpPlayer(Checks c, Player p)
    {
        c.Note($"Player m_maxAdrenaline {F(p.m_maxAdrenaline)}, miss {F(p.m_attackMissAdrenaline)}, unblocked hit "
               + $"{F(p.m_nonBlockDamageAdrenaline)}, perfect dodge {F(p.m_perfectDodgeAdrenaline)}, stagger "
               + $"{F(p.m_staggerEnemyAdrenaline)}, world rate {F(Game.m_adrenalineRate)}");
        c.Note($"curves (fill:value): drain [{Keys(p.m_adrenalineDegen)}], drain delay [{Keys(p.m_adrenalineDegenDelay)}], "
               + $"gain multiplier [{Keys(p.m_adrenalineGainMultiplier)}]");
        var tiers = p.m_adrenalineEffects
            .Select(t => $"{F(t.m_rate)} {(t.m_se != null ? t.m_se.name + " ttl " + F(t.m_se.m_ttl) : "none")}").ToArray();
        c.Note($"{tiers.Length} tier effects: {string.Join("; ", tiers)}");
        var pops = p.m_adrenalinePopEffects != null && p.m_adrenalinePopEffects.m_effectPrefabs != null
            ? p.m_adrenalinePopEffects.m_effectPrefabs.Where(e => e != null && e.m_prefab != null)
                .Select(e => $"{e.m_prefab.name} (enabled {e.m_enabled}, networked {e.m_prefab.GetComponent<ZNetView>() != null})")
                .ToArray()
            : new string[0];
        c.Note($"pop effects: {(pops.Length > 0 ? string.Join(", ", pops) : "none")}");
        var hud = Hud.instance;
        c.Note($"HUD adrenaline animator has a Flash trigger: {(hud != null && Feedback.CanFlash(hud) ? "yes" : "no")}");
    }

    private static string Tooltip(ItemDrop.ItemData item) =>
        ItemDrop.ItemData.GetTooltip(item, item.m_quality, false, Game.m_worldLevel, -1, false);

    private static void HitFromPlayer(Player p, Character target)
    {
        var hit = new HitData();
        hit.m_damage.m_blunt = 1f;
        hit.m_point = target.transform.position;
        hit.m_dir = (target.transform.position - p.transform.position).normalized;
        hit.SetAttacker(p);
        target.Damage(hit);
    }

    private static void HitPlayer(Player p, Character attacker)
    {
        var hit = new HitData();
        hit.m_damage.m_blunt = 0.1f;
        hit.m_point = p.transform.position;
        hit.m_dir = attacker != null ? (p.transform.position - attacker.transform.position).normalized : Vector3.down;
        if (attacker != null)
        {
            hit.SetAttacker(attacker);
        }
        p.Damage(hit);
    }

    private static void DestroyObject(GameObject go)
    {
        if (go == null)
        {
            return;
        }
        var view = go.GetComponent<ZNetView>();
        if (view != null && view.IsValid() && ZNetScene.instance != null)
        {
            ZNetScene.instance.Destroy(go);
        }
        else
        {
            Object.Destroy(go);
        }
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool Near(float a, float b, float tolerance = 0.001f) => Mathf.Abs(a - b) <= tolerance;

    // ---------- trinkets.network (pure) ----------

    private static IEnumerator RunNetwork()
    {
        var c = new Checks(NetworkName);
        var custom = new TrinketRules
        {
            IncomePerSecond = 2.5f, CombatLingerSeconds = 9f, RefuseWhileActive = true, RangedReferenceSeconds = 1.8f,
            RangedMaxMultiplier = 3f,
        };
        var pkg = new ZPackage();
        custom.Write(pkg);
        pkg.SetPos(0);
        c.Check(TrinketRules.TryRead(pkg, out var back, out var clamped) && !clamped && back.Describe() == custom.Describe()
                && back.RefuseWhileActive && Near(back.IncomePerSecond, 2.5f) && Near(back.RangedMaxMultiplier, 3f)
                && !back.IsPending,
            "rules survive the wire unchanged");

        var wild = new TrinketRules
        {
            IncomePerSecond = float.NaN, CombatLingerSeconds = 0.1f, RangedReferenceSeconds = 100f,
            RangedMaxMultiplier = float.NegativeInfinity,
        };
        pkg = new ZPackage();
        wild.Write(pkg);
        pkg.SetPos(0);
        var d = TrinketRules.Default;
        c.Check(TrinketRules.TryRead(pkg, out back, out clamped) && clamped && Near(back.IncomePerSecond, d.IncomePerSecond)
                && Near(back.CombatLingerSeconds, TrinketRules.LingerMin)
                && Near(back.RangedReferenceSeconds, TrinketRules.ReferenceMax)
                && Near(back.RangedMaxMultiplier, d.RangedMaxMultiplier),
            "out-of-range, NaN and infinite values from the wire are pulled into range");
        pkg = new ZPackage();
        new TrinketRules { IncomePerSecond = -5f, RangedMaxMultiplier = 50f }.Write(pkg);
        pkg.SetPos(0);
        c.Check(TrinketRules.TryRead(pkg, out back, out clamped) && clamped && back.IncomePerSecond == 0f
                && Near(back.RangedMaxMultiplier, TrinketRules.MultiplierMax),
            "negative income and a huge multiplier are pulled into range");

        pkg = new ZPackage();
        pkg.Write(TrinketRules.Layout + 1);
        pkg.Write(1f);
        pkg.SetPos(0);
        c.Check(!TrinketRules.TryRead(pkg, out _, out _), "unknown rules layout refused");
        pkg = new ZPackage();
        pkg.Write(TrinketRules.Layout);
        pkg.Write(3f);
        pkg.SetPos(0);
        c.Check(!TrinketRules.TryRead(pkg, out _, out _), "cut-off rules package refused");
        c.Check(TrinketRules.Pending.IsPending && !TrinketRules.Default.IsPending && !TrinketRules.Own().IsPending,
            "only the built-in Pending rules are pending");
        c.Check(!TrinketRules.Pending.RangedBonusOn && TrinketRules.Pending.IncomePerSecond == 0f
                && TrinketRules.Pending.Describe().Contains("waiting"),
            "pending rules are neutral and say they wait");

        // A server (this single-player world is one) never takes rules from a peer.
        var current = ServerRules.Current;
        pkg = new ZPackage();
        custom.Write(pkg);
        pkg.SetPos(0);
        c.Check(!ServerRules.Receive(pkg) && ReferenceEquals(ServerRules.Current, current) && !ServerRules.UsingServer
                && !ServerRules.Current.IsPending,
            "single player / server took rules from a peer or is pending");

        var own = new TrinketRules();
        c.Check(ReferenceEquals(ServerRules.Select(false, custom, own), own), "single player, host, server: own rules");
        c.Check(ReferenceEquals(ServerRules.Select(false, null, own), own), "server without peer rules: own rules");
        c.Check(ReferenceEquals(ServerRules.Select(true, custom, own), custom), "client with server rules: the server's");
        c.Check(ServerRules.Select(true, null, own).IsPending, "client without (readable) server rules: pending");

        c.Check(!ServerRules.Settled(10f, 9.6f) && ServerRules.Settled(10f, 9.5f)
                && ServerRules.Settled(10f, float.NegativeInfinity),
            "push waits 0.5 s after the last settings change, none after start");

        c.Check(PlayerCheck.Decide(true, true, true, false, true, false) == JoinVerdict.Compatible, "compatible -> Compatible");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, false) == JoinVerdict.Refuse,
            "not compatible (no mod, turned off, other network version) -> Refuse");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, true) == JoinVerdict.Allowed,
            "not compatible with AllowPlayersWithoutMod -> Allowed");
        c.Check(PlayerCheck.Decide(false, true, true, false, false, false) == JoinVerdict.Skip, "not a server -> Skip");
        c.Check(PlayerCheck.Decide(true, false, true, false, false, false) == JoinVerdict.Skip, "not connected -> Skip");
        c.Check(PlayerCheck.Decide(true, true, false, false, false, false) == JoinVerdict.Skip, "not ready -> Skip");
        c.Check(PlayerCheck.Decide(true, true, true, true, false, false) == JoinVerdict.Skip, "already being kicked -> Skip");
        c.Report();
        yield break;
    }

    // ---------- trinkets.controls ----------

    private static IEnumerator RunControls()
    {
        var c = new Checks(ControlsName);
        var zinput = ZInput.instance;
        if (!c.Check(zinput != null, "no ZInput"))
        {
            c.Report();
            yield break;
        }
        foreach (GamepadModifier m in Enum.GetValues(typeof(GamepadModifier)))
        {
            var name = Controls.ModifierName(m);
            c.Check(m == GamepadModifier.None ? name == null : name != null && zinput.GetButtonDef(name) != null,
                $"GamepadModifier {m}: ZInput has no button {name}");
        }
        foreach (GamepadButton b in Enum.GetValues(typeof(GamepadButton)))
        {
            var name = Controls.ButtonName(b);
            c.Check(b == GamepadButton.None ? name == null : name != null && zinput.GetButtonDef(name) != null,
                $"GamepadButton {b}: ZInput has no button {name}");
        }
        c.Check(Controls.IsUsableKey(KeyCode.Y) && Controls.IsUsableKey(KeyCode.U) && Controls.IsUsableKey(KeyCode.Mouse3),
            "Y, U and Mouse3 are usable trigger keys");
        c.Check(!Controls.IsUsableKey(KeyCode.None) && !Controls.IsUsableKey(KeyCode.JoystickButton0)
                && !Controls.IsUsableKey(KeyCode.Mouse5),
            "None, gamepad KeyCodes and Mouse5 are refused as trigger keys");
        var yLabel = Controls.KeyLabel(KeyCode.Y);
        c.Check(yLabel != null && yLabel.StartsWith("[", StringComparison.Ordinal) && yLabel.Length > 2,
            $"key label for Y is '{yLabel}'");
        c.Note($"labels: key {Controls.KeyLabel(Controls.Key) ?? "none"}, gamepad {Controls.PadLabel() ?? "none"}, "
               + $"now {Controls.Label() ?? "none"} (gamepad active {ZInput.IsGamepadActive()}, layout {ZInput.InputLayout})");
        var same = Controls.SameButton("JoyRStick").Select(def => def.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        c.Note($"buttons on the right stick press: {string.Join(", ", same)}");
        c.Check(same.Contains("JoyRStick"), "the right stick press list holds JoyRStick itself");
        if (ZInput.InputLayout == InputLayout.Default)
        {
            c.Check(same.Contains("JoyHide") && same.Contains("JoyRadial"),
                "default layout: hide and radial are on the right stick press (swallowed after a gamepad trigger)");
        }
        var lt = Controls.SameButton("JoyLTrigger").Select(def => def.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        c.Note($"buttons on the left trigger: {string.Join(", ", lt)}");

        c.Check(ForeignMods.Matches("BetterTrinkets") && ForeignMods.Matches("Passive_Trinket_Modifiers")
                && ForeignMods.Matches("Balrond Battle Flow") && ForeignMods.Matches("balrond.battleflow")
                && ForeignMods.Matches("battle_flow"),
            "foreign trinket mods are recognised by name");
        c.Check(!ForeignMods.Matches(ModInfo.Name) && !ForeignMods.Matches(ModInfo.Guid) && !ForeignMods.Matches("Surge")
                && !ForeignMods.Matches("MultiTrinket") && !ForeignMods.Matches(""),
            "this mod and the compatible ones are not blockers");
        var blocker = ForeignMods.Find(out var blockerGuid);
        c.Check(blocker == null, $"a blocking trinket mod is loaded in the probe: {blocker} ({blockerGuid})");
        c.Check(Compat.IsMatch(Compat.SurgeMarker, Compat.SurgeGuid, "Surge", "ezomic.valheim.surge")
                && Compat.IsMatch(Compat.SurgeMarker, Compat.SurgeGuid, "", Compat.SurgeGuid.ToUpperInvariant())
                && Compat.IsMatch(Compat.SurgeMarker, Compat.SurgeGuid, "Surge", "someone.else.surge"),
            "Surge is recognised by its whole name or its exact GUID");
        c.Check(!Compat.IsMatch(Compat.SurgeMarker, Compat.SurgeGuid, "Resurgence", "someone.resurgence")
                && !Compat.IsMatch(Compat.SurgeMarker, Compat.SurgeGuid, "Insurgency Tweaks", "x.insurgency")
                && !Compat.IsMatch(Compat.SurgeMarker, Compat.SurgeGuid, "Surge Plus", "x.surgeplus"),
            "names that only contain 'surge' are not Surge");
        c.Check(Compat.IsMatch("multitrinket", null, "MultiTrinket", "x.multitrinket")
                && Compat.IsMatch("adrenalinemodifier", null, "Adrenaline Modifier", "x.y")
                && !Compat.IsMatch("multitrinket", null, ModInfo.Name, ModInfo.Guid),
            "long markers match inside the name or GUID, not this mod");
        c.Report();
    }

    // ---------- trinkets.pending ----------

    private static IEnumerator RunPending()
    {
        var rig = Rig.Create(PendingName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(PendingName);
        try
        {
            var p = rig.P;
            var prefab = FindTrinket(c, false);
            var item = prefab != null ? rig.EquipTrinket(prefab) : null;
            if (!c.Check(item != null, $"could not equip a trinket ({prefab ?? "none found"})"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();
            ServerRules.TestPending = true;
            c.Check(ServerRules.Current.IsPending, "TestPending gives the pending rules");

            p.m_adrenaline = max;
            p.AddAdrenaline(0f);
            c.Check(Near(p.m_adrenaline, 0f) && rig.Has(hash), "pending: a full bar fires the trinket at once (normal game)");
            rig.Effect(hash);

            p.m_adrenaline = max;
            p.m_adrenalineDegenTimer = 100f;
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.Pending && Near(p.m_adrenaline, max) && !rig.Has(hash),
                $"pending: the key does nothing (answer {FullBar.LastResult})");

            c.Check(!Tooltip(item).Contains(TooltipMark), "pending: no trigger line in the tooltip");
            c.Check(!ServerRules.Current.RangedBonusOn, "pending: no ranged bonus");

            CombatState.MarkExchange();
            p.m_adrenaline = 0.25f * max;
            p.m_adrenalineDegenTimer = 100f;
            yield return new WaitForSeconds(2.2f);
            c.Check(p.m_adrenaline <= 0.25f * max + 0.001f, $"pending: income in a fight ({F(p.m_adrenaline)})");

            var drain = p.m_adrenalineDegen.Evaluate(0.5f);
            p.m_adrenaline = 0.5f * max;
            p.m_adrenalineDegenTimer = 0f;
            yield return new WaitForSeconds(1f);
            if (drain > 0f)
            {
                c.Check(p.m_adrenaline < 0.5f * max - 0.0001f, $"pending: the bar does not drain ({F(p.m_adrenaline)} of {F(max)})");
            }
            else
            {
                c.Note("drain curve is 0 at half a bar: drain not measurable here");
            }

            ServerRules.TestPending = false;
            ServerRules.TestRules = new TrinketRules();
            rig.Effect(hash);
            p.m_adrenaline = max;
            p.AddAdrenaline(0f);
            c.Check(Near(p.m_adrenaline, max) && !rig.Has(hash), "pending over: the full bar waits again");
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.hold-and-fire ----------

    private static IEnumerator RunHoldAndFire()
    {
        var rig = Rig.Create(HoldName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(HoldName);
        try
        {
            var p = rig.P;
            DumpPlayer(c, p);
            var prefab = FindTrinket(c, true);
            var prefabShared = prefab != null ? ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared : null;
            var costBefore = prefabShared != null ? prefabShared.m_maxAdrenaline : 0f;
            var effectBefore = prefabShared != null ? prefabShared.m_fullAdrenalineSE : null;
            ServerRules.TestRules = new TrinketRules();
            var item = prefab != null ? rig.EquipTrinket(prefab) : null;
            if (!c.Check(item != null, $"could not equip a trinket ({prefab ?? "none found"})"))
            {
                c.Report();
                yield break;
            }
            var se = item.m_shared.m_fullAdrenalineSE;
            var hash = se.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();
            c.Note($"trinket {prefab}: cost {F(item.m_shared.m_maxAdrenaline)}, max {F(max)}, effect {se.name}");
            c.Check(max > 0f && max >= item.m_shared.m_maxAdrenaline - 0.01f,
                $"the trinket gives the bar its capacity ({F(max)})");

            // G4: full bar held on every kind of call.
            p.m_adrenaline = max;
            p.AddAdrenaline(1f);
            c.Check(Near(p.m_adrenaline, max) && !rig.Has(hash), "full bar + gain: held full, no effect");
            p.AddAdrenaline(0f);
            c.Check(Near(p.m_adrenaline, max) && !rig.Has(hash), "full bar + 0 call (like a melee miss): held full");
            p.m_adrenaline = 0.9f * max;
            p.AddAdrenaline(max);
            c.Check(p.m_adrenaline <= max + 0.001f && !rig.Has(hash),
                $"gain across the max: no effect ({F(p.m_adrenaline)} of {F(max)})");
            p.m_adrenaline = max + 10f;
            p.AddAdrenaline(0f);
            c.Check(Near(p.m_adrenaline, max) && !rig.Has(hash), "bar above the max (cheaper trinket): set to max, no effect");
            c.Check(ReferenceEquals(item.m_shared.m_fullAdrenalineSE, se) && FullBar.Depth == 0 && FullBar.HiddenCount == 0,
                "effect put back after every call, no scope left open");

            // G1: stats unchanged.
            c.Check(prefabShared != null && ReferenceEquals(prefabShared.m_fullAdrenalineSE, effectBefore)
                    && Near(prefabShared.m_maxAdrenaline, costBefore),
                "the trinket's effect and cost are unchanged");

            // Tooltip.
            var tip = Tooltip(item);
            var at = tip.IndexOf(TooltipMark, StringComparison.Ordinal);
            var vanilla = tip.IndexOf("$item_fulladrenaline", StringComparison.Ordinal);
            c.Check(at > vanilla && vanilla >= 0, "tooltip names the trigger after the full-adrenaline line");
            c.Note("tooltip line: " + (at >= 0 ? tip.Substring(at).Split('\n')[1] : "none"));

            // G3: no drain (timer forced to 0, which would start the vanilla drain at once).
            p.m_adrenaline = 0.5f * max;
            p.m_adrenalineDegenTimer = 0f;
            var since = Time.time;
            yield return new WaitForSeconds(1.5f);
            if (CombatState.LastExchange >= since)
            {
                c.Note("a real fight happened while waiting: drain check skipped");
            }
            else
            {
                c.Check(Near(p.m_adrenaline, 0.5f * max, 0.001f),
                    $"no drain over 1.5 s ({F(p.m_adrenaline)} of {F(0.5f * max)})");
            }

            // Feedback on the rising edge.
            Feedback.ClearMessageCooldown();
            var flashes = Feedback.FlashCount;
            var messages = Feedback.FullMessageCount;
            p.m_adrenaline = max;
            yield return Frames(3);
            var canFlash = Hud.instance != null && Feedback.CanFlash(Hud.instance);
            if (canFlash)
            {
                c.Check(Feedback.FlashCount > flashes, "the bar flashes when it becomes full");
            }
            else
            {
                c.Note("the HUD adrenaline animator has no Flash trigger: no flash (nothing logged)");
            }
            if (Plugin.ShowFullMessage == null || Plugin.ShowFullMessage.Value)
            {
                c.Check(Feedback.FullMessageCount > messages, "a message names the trigger when the bar becomes full");
            }
            SelfTest.Screenshot(HoldName, "full-bar");
            yield return Frames(2);

            // G5: presses.
            p.m_adrenaline = 0.5f * max;
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.NotFull && Near(p.m_adrenaline, 0.5f * max) && !rig.Has(hash),
                $"half bar: 'not full yet', nothing spent (answer {FullBar.LastResult})");
            p.m_adrenaline = max;
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.Fired && Near(p.m_adrenaline, 0f) && rig.Has(hash),
                $"full bar: the key fires the trinket, bar empty (answer {FullBar.LastResult}, bar {F(p.m_adrenaline)})");

            yield return new WaitForSeconds(0.5f);
            var running = p.GetSEMan().GetStatusEffect(hash);
            var timeBefore = running != null ? running.m_time : 0f;
            ServerRules.TestRules = new TrinketRules { RefuseWhileActive = true };
            p.m_adrenaline = max;
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.StillActive && Near(p.m_adrenaline, max),
                $"RefuseWhileActive: refused while the effect runs, bar kept (answer {FullBar.LastResult})");
            ServerRules.TestRules = new TrinketRules();
            yield return Press();
            running = p.GetSEMan().GetStatusEffect(hash);
            c.Check(FullBar.LastResult == TriggerResult.Fired && Near(p.m_adrenaline, 0f) && running != null
                    && running.m_time < timeBefore,
                $"default: the key refreshes a running effect and spends the bar (time {F(timeBefore)} -> "
                + $"{(running != null ? F(running.m_time) : "gone")})");

            p.UnequipItem(item, false);
            p.UpdateModifiers();
            p.m_adrenaline = 0f;
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.NoTrinket, $"no trinket: 'no trinket to trigger' (answer {FullBar.LastResult})");
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.income ----------

    private static IEnumerator RunIncome()
    {
        var rig = Rig.Create(IncomeName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(IncomeName);
        try
        {
            var p = rig.P;
            ServerRules.TestRules = new TrinketRules();
            var prefab = FindTrinket(c, false);
            var item = prefab != null ? rig.EquipTrinket(prefab) : null;
            if (!c.Check(item != null, $"could not equip a trinket ({prefab ?? "none found"})"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();
            var gainAtZero = p.m_adrenalineGainMultiplier.Evaluate(0f) * Game.m_adrenalineRate;
            c.Note($"one income tick at an empty bar should add about {F(gainAtZero)} (1 x world rate x gain curve at 0)");

            // Out of a fight.
            CombatState.Reset();
            p.m_adrenaline = 0f;
            var start = Time.time;
            yield return new WaitForSeconds(2.2f);
            if (CombatState.LastExchange >= start)
            {
                c.Note("a real fight happened while waiting: out-of-fight check skipped");
            }
            else
            {
                c.Check(p.m_adrenaline == 0f, $"no income out of a fight ({F(p.m_adrenaline)})");
            }

            // In a fight.
            CombatState.MarkExchange();
            p.m_adrenaline = 0f;
            yield return new WaitForSeconds(2.3f);
            var gained = p.m_adrenaline;
            if (gainAtZero > 0f)
            {
                c.Check(gained > 0f && gained <= 3.5f * gainAtZero * 1.5f + 0.01f,
                    $"income in a fight: {F(gained)} in 2.3 s (about 2 ticks)");
            }
            else
            {
                c.Note($"gain curve is 0 at an empty bar: income not measurable ({F(gained)})");
            }

            // Fills to max and waits.
            CombatState.MarkExchange();
            p.m_adrenaline = max - 0.01f;
            yield return new WaitForSeconds(1.2f);
            c.Check(Near(p.m_adrenaline, max, 0.02f) && !rig.Has(hash),
                $"income fills the bar and it waits, no effect ({F(p.m_adrenaline)} of {F(max)})");

            // Fight over (and no drain meanwhile: timer forced to 0). Real monster hunting me in the wait (alerted
            // targeting, 7 s after last hit is inside the engaged window) = fight by design: skip, no false fail.
            CombatState.SetForTest(Time.time - 7f, float.NegativeInfinity);
            p.m_adrenaline = 0.5f * max;
            p.m_adrenalineDegenTimer = 0f;
            start = Time.time;
            yield return new WaitForSeconds(1.2f);
            if (CombatState.LastExchange >= start || CombatState.LastTargeted >= start)
            {
                c.Note("a real fight or a hunting monster during the wait: after-fight check skipped");
            }
            else
            {
                c.Check(Near(p.m_adrenaline, 0.5f * max), $"no income 7 s after the last hit, no drain ({F(p.m_adrenaline)})");
            }

            // Income off.
            ServerRules.TestRules = new TrinketRules { IncomePerSecond = 0f };
            CombatState.MarkExchange();
            p.m_adrenaline = 0.5f * max;
            yield return new WaitForSeconds(1.2f);
            c.Check(Near(p.m_adrenaline, 0.5f * max), $"IncomePerSecond 0: no income ({F(p.m_adrenaline)})");

            // No capacity: no income, and vanilla keeps the bar hidden (max 0).
            ServerRules.TestRules = new TrinketRules();
            p.UnequipItem(item, false);
            p.UpdateModifiers();
            CombatState.MarkExchange();
            p.m_adrenaline = 0f;
            yield return new WaitForSeconds(1.2f);
            c.Check(p.m_adrenaline == 0f, $"no trinket: no income ({F(p.m_adrenaline)})");
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.combat-state ----------

    private static IEnumerator RunCombatState()
    {
        var c = new Checks(CombatName);
        const float ni = float.NegativeInfinity;
        c.Check(CombatState.InCombat(100f, 95f, ni, 6f), "hit 5 s ago: in a fight");
        c.Check(!CombatState.InCombat(100f, 93f, ni, 6f), "hit 7 s ago, not targeted: fight over");
        c.Check(CombatState.InCombat(100f, 90f, 99.5f, 6f), "hit 10 s ago, alerted monster still targets you: in a fight");
        c.Check(!CombatState.InCombat(100f, 79f, 99.5f, 6f), "targeted but last hit 21 s ago: fight over");
        c.Check(!CombatState.InCombat(100f, 90f, 98f, 6f), "targeting stopped 2 s ago, hit 10 s ago: fight over");
        c.Check(!CombatState.InCombat(100f, ni, 100f, 6f), "targeted only, never a hit (training dummy): no fight");
        c.Check(CombatState.InCombat(100f, 71f, ni, 30f), "linger 30 s: hit 29 s ago still counts");

        var rig = Rig.Create(CombatName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var p = rig.P;
            var fwd = Flat(p.transform.forward);
            var right = Flat(p.transform.right);
            var foe = rig.Creature(FoeName, fwd * 4f);
            var tame = rig.Creature(FoeName, -fwd * 4f);
            var dummy = rig.Creature(DummyName, right * 4f);
            if (!c.Check(foe != null && tame != null, $"could not spawn {FoeName}"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(2);

            CombatState.Reset();
            var t0 = Time.time;
            HitFromPlayer(p, foe);
            c.Check(CombatState.LastExchange >= t0, "hitting a Greydwarf starts a fight");

            tame.SetTamed(true);
            CombatState.Reset();
            HitFromPlayer(p, tame);
            c.Check(tame.IsTamed() && float.IsNegativeInfinity(CombatState.LastExchange), "hitting a tamed creature is no fight");

            if (dummy != null)
            {
                c.Note($"{DummyName}: faction {dummy.m_faction}, m_aiSkipTarget {dummy.m_aiSkipTarget}, enemy of the "
                       + $"player {BaseAI.IsEnemy(p, dummy)}");
                CombatState.Reset();
                HitFromPlayer(p, dummy);
                c.Check(float.IsNegativeInfinity(CombatState.LastExchange), "hitting a training dummy is no fight");
            }
            else
            {
                c.Note($"{DummyName} not found: dummy check skipped");
            }

            CombatState.Reset();
            t0 = Time.time;
            HitPlayer(p, foe);
            c.Check(CombatState.LastExchange >= t0, "a Greydwarf hitting you starts a fight");
            CombatState.Reset();
            HitPlayer(p, null);
            c.Check(float.IsNegativeInfinity(CombatState.LastExchange), "damage with no attacker (fall, drowning) is no fight");

            CombatState.Reset();
            p.RPC_OnTargeted(0L, false, false);
            c.Check(float.IsNegativeInfinity(CombatState.LastTargeted), "targeted by a monster that is not alerted: no stamp");
            if (MusicMan.instance != null)
            {
                t0 = Time.time;
                p.RPC_OnTargeted(0L, false, true);
                c.Check(CombatState.LastTargeted >= t0 && !CombatState.InCombat(6f),
                    "alerted targeting is stamped, but alone it is no fight");
            }
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.ranged-factor (pure + dumps) ----------

    private static IEnumerator RunRangedFactor()
    {
        var c = new Checks(RangedName);
        const float inf = float.PositiveInfinity;
        c.Check(Near(RangedBonus.FactorFor(4f, inf, 1f, 4f, 1, 1), 4f), "4 s cycle, 1 s reference: x4");
        c.Check(Near(RangedBonus.FactorFor(2.25f, inf, 1f, 4f, 1, 1), 2.25f), "2.25 s cycle: x2.25");
        c.Check(Near(RangedBonus.FactorFor(0.5f, inf, 1f, 4f, 1, 1), 1f), "cycle shorter than the reference: x1 (vanilla)");
        c.Check(Near(RangedBonus.FactorFor(10f, inf, 1f, 4f, 1, 1), 4f), "cycle capped at the max multiplier");
        c.Check(Near(RangedBonus.FactorFor(4f, 0.7f, 1f, 4f, 1, 1), 1f), "shot 0.7 s after the last one (swap burst): x1");
        c.Check(Near(RangedBonus.FactorFor(4f, 2f, 1f, 4f, 1, 1), 2f), "shot 2 s after the last one: x2");
        c.Check(Near(RangedBonus.FactorFor(4f, inf, 1f, 4f, 4, 1), 1.75f), "4 projectiles share the bonus: x1.75 each");
        c.Check(Near(RangedBonus.FactorFor(4f, inf, 1f, 4f, 1, 2), 2.5f), "2 bursts share the bonus: x2.5 each");
        c.Check(Near(RangedBonus.FactorFor(4f, inf, 1f, 1f, 1, 1), 1f), "max multiplier 1: bonus off");
        c.Check(Near(RangedBonus.FactorFor(float.NaN, inf, 1f, 4f, 1, 1), 1f)
                && Near(RangedBonus.FactorFor(4f, inf, 0f, 4f, 1, 1), 1f), "bad numbers: x1");
        c.Check(Near(RangedBonus.FactorFor(4f, inf, 2f, 4f, 0, 0), 2f), "0 projectiles or bursts count as 1");
        c.Check(Near(RangedBonus.BowCycle(1f, 2f, 0f), 2.5f) && Near(RangedBonus.BowCycle(1f, 2f, 1f), 0.9f)
                && Near(RangedBonus.BowCycle(0.5f, 2f, 0f), 1.5f) && Near(RangedBonus.BowCycle(3f, 2f, 0f), 2.5f)
                && Near(RangedBonus.BowCycle(0f, 2f, 0f), RangedBonus.ShotSeconds),
            "bow cycle = draw held (full draw 2 s at skill 0, 0.4 s at 100) + 0.5 s");
        c.Check(Near(RangedBonus.CrossbowCycle(3.5f), 4f) && Near(RangedBonus.CrossbowCycle(1.75f), 2.25f),
            "crossbow cycle = reload + 0.5 s");
        c.Check(new TrinketRules().RangedBonusOn && !new TrinketRules { RangedMaxMultiplier = 1f }.RangedBonusOn,
            "ranged bonus on by default, off at max multiplier 1");
        // Me check the numbers README, TESTING and design say for the defaults (D 2.5 s bow, R 3.5 s crossbow).
        var dr = TrinketRules.Default;
        float AtDefault(float cycle) => RangedBonus.FactorFor(cycle, inf, dr.RangedReferenceSeconds, dr.RangedMaxMultiplier, 1, 1);
        c.Check(Near(dr.RangedReferenceSeconds, 1.5f) && Near(dr.RangedMaxMultiplier, 4f)
                && Near(AtDefault(RangedBonus.BowCycle(1f, 2.5f, 0f)), 2f)
                && Near(AtDefault(RangedBonus.BowCycle(1f, 2.5f, 0.5f)), 4f / 3f)
                && Near(AtDefault(RangedBonus.BowCycle(1f, 2.5f, 0.75f)), 1f)
                && Near(AtDefault(RangedBonus.BowCycle(1f, 2.5f, 1f)), 1f)
                && Near(AtDefault(RangedBonus.BowCycle(1f, 1f, 0f)), 1f)
                && Near(AtDefault(RangedBonus.CrossbowCycle(3.5f)), 8f / 3f)
                && Near(AtDefault(RangedBonus.CrossbowCycle(1.75f)), 1.5f),
            "defaults (1.5 s reference, cap 4): full-draw bow x2 / x1.33 / x1 / x1 at Bows 0 / 50 / 75 / 100, 1 s draw "
            + "bow x1, crossbow x2.67 / x1.5 at Crossbows 0 / 100");

        var db = ObjectDB.instance;
        if (db != null)
        {
            var bows = new List<string>();
            var crossbows = new List<string>();
            var ammo = new List<string>();
            var d = TrinketRules.Default;
            foreach (var go in db.m_items.Where(g => g != null).OrderBy(g => g.name, StringComparer.Ordinal))
            {
                var drop = go.GetComponent<ItemDrop>();
                var shared = drop != null ? drop.m_itemData.m_shared : null;
                var attack = shared != null ? shared.m_attack : null;
                if (shared == null || attack == null)
                {
                    continue;
                }
                if (shared.m_skillType == Skills.SkillType.Bows && shared.m_itemType != ItemDrop.ItemData.ItemType.Ammo)
                {
                    var f0 = RangedBonus.FactorFor(RangedBonus.BowCycle(1f, attack.m_drawDurationMin, 0f), inf,
                        d.RangedReferenceSeconds, d.RangedMaxMultiplier, attack.m_projectiles, attack.m_projectileBursts);
                    var f1 = RangedBonus.FactorFor(RangedBonus.BowCycle(1f, attack.m_drawDurationMin, 1f), inf,
                        d.RangedReferenceSeconds, d.RangedMaxMultiplier, attack.m_projectiles, attack.m_projectileBursts);
                    bows.Add($"{go.name} draw {F(attack.m_drawDurationMin)} s (bowDraw {attack.m_bowDraw}), "
                             + $"{attack.m_projectiles}x{attack.m_projectileBursts}, melee pay {F(attack.m_attackAdrenaline)}, "
                             + $"full-draw factor x{F(f0)} at skill 0 / x{F(f1)} at 100");
                }
                else if (shared.m_skillType == Skills.SkillType.Crossbows && shared.m_itemType != ItemDrop.ItemData.ItemType.Ammo)
                {
                    var f0 = RangedBonus.FactorFor(RangedBonus.CrossbowCycle(attack.m_reloadTime), inf,
                        d.RangedReferenceSeconds, d.RangedMaxMultiplier, attack.m_projectiles, attack.m_projectileBursts);
                    var f1 = RangedBonus.FactorFor(RangedBonus.CrossbowCycle(attack.m_reloadTime * 0.5f), inf,
                        d.RangedReferenceSeconds, d.RangedMaxMultiplier, attack.m_projectiles, attack.m_projectileBursts);
                    crossbows.Add($"{go.name} reload {F(attack.m_reloadTime)} s (requiresReload {attack.m_requiresReload}), "
                                  + $"{attack.m_projectiles}x{attack.m_projectileBursts}, factor x{F(f0)} at skill 0 / "
                                  + $"x{F(f1)} at 100");
                }
                if (shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo && attack.m_attackProjectile != null)
                {
                    var proj = attack.m_attackProjectile.GetComponent<Projectile>();
                    ammo.Add($"{go.name} {F(proj != null ? proj.m_adrenaline : -1f)}");
                }
            }
            c.Note($"bows: {string.Join("; ", bows.ToArray())}");
            c.Note($"crossbows: {string.Join("; ", crossbows.ToArray())}");
            c.Note($"ammo projectile adrenaline per hit: {string.Join(", ", ammo.ToArray())}");
            c.Check(bows.Count > 0 && crossbows.Count > 0, "bows and crossbows found in ObjectDB");
            DumpMeleePay(c, db, Player.m_localPlayer);
        }
        c.Report();
        yield break;
    }

    // Melee side of the ranged reference (design D14): what a player weapon pay per hit, by skill. Only NOTE, no check.
    private static readonly Skills.SkillType[] MeleeSkills =
    {
        Skills.SkillType.Swords, Skills.SkillType.Axes, Skills.SkillType.Clubs, Skills.SkillType.Knives,
        Skills.SkillType.Spears, Skills.SkillType.Polearms, Skills.SkillType.Unarmed, Skills.SkillType.Pickaxes,
        Skills.SkillType.ElementalMagic, Skills.SkillType.BloodMagic,
    };

    // Weapon item types (SharedData.m_skillType default Swords: armor, food and such must stay out).
    private static bool IsWeaponType(ItemDrop.ItemData.ItemType type, Skills.SkillType skill)
    {
        switch (type)
        {
            case ItemDrop.ItemData.ItemType.OneHandedWeapon:
            case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
            case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
            case ItemDrop.ItemData.ItemType.Torch:
            case ItemDrop.ItemData.ItemType.Attach_Atgeir:
            case ItemDrop.ItemData.ItemType.Hands:
                return true;
            case ItemDrop.ItemData.ItemType.Tool:
                return skill == Skills.SkillType.Pickaxes;
            default:
                return false;
        }
    }

    private static void AddPay(SortedDictionary<float, List<string>> groups, float value, string name)
    {
        if (!groups.TryGetValue(value, out var names))
        {
            names = new List<string>();
            groups[value] = names;
        }
        names.Add(name);
    }

    // "value: count (examples)" per value, low to high.
    private static string Groups(SortedDictionary<float, List<string>> groups)
    {
        if (groups.Count == 0)
        {
            return "none";
        }
        return "[" + string.Join("; ", groups.Select(g => $"{F(g.Key)}: {g.Value.Count} ("
            + string.Join(", ", g.Value.Take(3).ToArray()) + (g.Value.Count > 3 ? $" +{g.Value.Count - 3}" : "")
            + ")").ToArray()) + "]";
    }

    // Player weapons = items with a recipe, plus the bare hands (Humanoid.m_unarmedWeapon). Per skill: primary and
    // secondary m_attackAdrenaline (melee and area hits pay it, per hit, x victim's m_enemyAdrenalineMultiplier),
    // m_attackUseAdrenaline when above 0 (paid per attack), and the projectile's own pay (staffs, spear throw).
    private static void DumpMeleePay(Checks c, ObjectDB db, Player player)
    {
        var crafted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipe in db.m_recipes)
        {
            if (recipe != null && recipe.m_item != null)
            {
                crafted.Add(recipe.m_item.gameObject.name);
            }
        }
        var weapons = new List<GameObject>();
        var skipped = new List<string>();
        var unarmed = player != null && player.m_unarmedWeapon != null ? player.m_unarmedWeapon.gameObject : null;
        foreach (var go in db.m_items.Where(g => g != null).OrderBy(g => g.name, StringComparer.Ordinal))
        {
            var drop = go.GetComponent<ItemDrop>();
            var shared = drop != null ? drop.m_itemData.m_shared : null;
            if (shared == null || shared.m_attack == null || !MeleeSkills.Contains(shared.m_skillType)
                || !IsWeaponType(shared.m_itemType, shared.m_skillType))
            {
                continue;
            }
            if (crafted.Contains(go.name) || ReferenceEquals(go, unarmed))
            {
                weapons.Add(go);
            }
            else
            {
                skipped.Add(go.name);
            }
        }
        if (unarmed != null && !weapons.Contains(unarmed))
        {
            weapons.Add(unarmed);
        }
        foreach (var skill in MeleeSkills)
        {
            var primary = new SortedDictionary<float, List<string>>();
            var secondary = new SortedDictionary<float, List<string>>();
            var use = new SortedDictionary<float, List<string>>();
            var projectile = new SortedDictionary<float, List<string>>();
            var count = 0;
            foreach (var go in weapons)
            {
                var shared = go.GetComponent<ItemDrop>().m_itemData.m_shared;
                if (shared.m_skillType != skill || shared.m_attack == null)
                {
                    continue;
                }
                count++;
                var attacks = new List<KeyValuePair<string, Attack>> { new KeyValuePair<string, Attack>("1", shared.m_attack) };
                var second = shared.m_secondaryAttack;
                if (second != null && !string.IsNullOrEmpty(second.m_attackAnimation))
                {
                    attacks.Add(new KeyValuePair<string, Attack>("2", second));
                    AddPay(secondary, second.m_attackAdrenaline, go.name);
                }
                AddPay(primary, shared.m_attack.m_attackAdrenaline, go.name);
                foreach (var pair in attacks)
                {
                    var attack = pair.Value;
                    if (attack.m_attackUseAdrenaline > 0f)
                    {
                        AddPay(use, attack.m_attackUseAdrenaline, go.name + "/" + pair.Key);
                    }
                    var proj = attack.m_attackProjectile != null ? attack.m_attackProjectile.GetComponent<Projectile>() : null;
                    if (proj != null)
                    {
                        AddPay(projectile, proj.m_adrenaline, go.name + "/" + pair.Key);
                    }
                }
            }
            c.Note($"melee pay {skill} ({count} weapons): primary {Groups(primary)}, secondary {Groups(secondary)}"
                   + (use.Count > 0 ? $", use {Groups(use)}" : "")
                   + (projectile.Count > 0 ? $", projectile per hit {Groups(projectile)} (/1 primary, /2 secondary)" : ""));
        }
        c.Note($"melee pay: {weapons.Count} player weapons (items with a recipe{(unarmed != null ? ", plus " + unarmed.name : "")}); "
               + $"{skipped.Count} weapons of these skills without a recipe left out (monster attacks, drops): "
               + string.Join(", ", skipped.Take(8).ToArray()) + (skipped.Count > 8 ? $" +{skipped.Count - 8}" : ""));
    }

    // ---------- trinkets.ranged-shot (live) ----------

    private static IEnumerator RunRangedShot()
    {
        var rig = Rig.Create(ShotName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(ShotName);
        ItemDrop.ItemData bow = null;
        try
        {
            var p = rig.P;
            var rules = new TrinketRules(); // default rules: me read reference and cap from here, never a copy
            ServerRules.TestRules = rules;
            RangedBonus.Reset();
            bow = rig.Give(BowName);
            var arrows = rig.Give(ArrowName, 20);
            if (!c.Check(bow != null && arrows != null, $"could not give {BowName} and {ArrowName}"))
            {
                c.Report();
                yield break;
            }
            rig.EmptyHands();
            if (!c.Check(p.EquipItem(bow, false), $"could not equip {BowName}"))
            {
                c.Report();
                yield break;
            }
            rig.TakeControls();
            yield return new WaitForSeconds(0.5f);

            var d = bow.m_shared.m_attack.m_drawDurationMin;
            var sf = p.GetSkillFactor(Skills.SkillType.Bows);
            var expectedNominal = RangedBonus.BowCycle(1f, d, sf);
            var expectedFactor = RangedBonus.FactorFor(expectedNominal, float.PositiveInfinity,
                rules.RangedReferenceSeconds, rules.RangedMaxMultiplier,
                bow.m_shared.m_attack.m_projectiles, bow.m_shared.m_attack.m_projectileBursts);
            c.Note($"{BowName}: full draw {F(Mathf.Lerp(d, 0.2f * d, sf))} s (D {F(d)}, Bows skill factor {F(sf)}), "
                   + $"expected factor x{F(expectedFactor)}");

            var serial = RangedBonus.Last.Serial;
            var waited = 0f;
            while (p.GetAttackDrawPercentage() < 1f && waited < 6f)
            {
                rig.Drive(true);
                waited += Time.deltaTime;
                yield return null;
            }
            var drawn = p.GetAttackDrawPercentage();
            waited = 0f;
            while (RangedBonus.Last.Serial == serial && waited < 4f)
            {
                rig.Drive(false);
                waited += Time.deltaTime;
                yield return null;
            }
            if (bow.m_lastProjectile != null)
            {
                DestroyObject(bow.m_lastProjectile); // before it lands: no arrow left in the world, nobody hit
            }
            if (expectedFactor <= 1f)
            {
                c.Check(RangedBonus.Last.Serial == serial, "full draw earns no bonus with this bow, and none was paid");
            }
            else if (c.Check(RangedBonus.Last.Serial != serial,
                         $"no scaled arrow within 4 s of the release (draw {F(drawn)}, waited for the draw {F(waited)} s)"))
            {
                var r = RangedBonus.Last;
                var ammoProjectile = arrows.m_shared.m_attack != null && arrows.m_shared.m_attack.m_attackProjectile != null
                    ? arrows.m_shared.m_attack.m_attackProjectile.GetComponent<Projectile>()
                    : null;
                c.Note($"shot: draw {F(r.DrawPercentage)}, cycle {F(r.Nominal)} s, factor x{F(r.Factor)}, arrow pays "
                       + $"{F(r.Base)} -> {F(r.Result)}");
                c.Check(r.Skill == Skills.SkillType.Bows && r.DrawPercentage >= 0.99f, "a full-draw Bow shot was scaled");
                c.Check(Near(r.Nominal, RangedBonus.BowCycle(r.DrawPercentage, d, sf), 0.01f),
                    "cycle = draw held + 0.5 s");
                c.Check(Near(r.Factor, RangedBonus.FactorFor(r.Nominal, r.SinceLast, rules.RangedReferenceSeconds,
                            rules.RangedMaxMultiplier, r.Projectiles, r.Bursts), 0.001f)
                        && float.IsPositiveInfinity(r.SinceLast),
                    "factor follows the formula (first shot: nominal cycle)");
                c.Check(Near(r.Result, r.Base * r.Factor, 0.001f), "arrow pays base x factor");
                c.Check(ammoProjectile == null || Near(r.Base, ammoProjectile.m_adrenaline, 0.001f),
                    $"base = the arrow prefab's own value ({F(ammoProjectile != null ? ammoProjectile.m_adrenaline : -1f)}), "
                    + "the prefab is untouched");
            }
        }
        finally
        {
            if (bow != null && bow.m_lastProjectile != null)
            {
                DestroyObject(bow.m_lastProjectile);
            }
            rig.Restore();
        }
        c.Report();
    }
#endif
}
