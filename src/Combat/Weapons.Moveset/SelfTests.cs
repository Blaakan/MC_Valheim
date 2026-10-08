using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace MC.Combat.WeaponsMovesetMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1
// -Mod Weapons.Moveset) in throwaway world: god mode, world modifier StaminaRate 0. Design 7.5:
//   moveset.triggers    animation names in player Animator, defaults resolve, drop-down lists start with default,
//                       bad names fall back with one warning, every player melee weapon has a family, gates say no
//                       to torch, tools, bows, bombs, shields... controller probe find every table trigger's attack
//                       state, playing the clip the live Animator play (and the learned state when one is known)
//   moveset.rules       pure: rules on the wire, client rule pick, warning advice, push wait, join verdicts, Decide
//                       truth table (cut into the roll too), StepOf, clone edit, stagger cap, fired trigger name
//   moveset.jump        every family: jump, press, jump attack play its trigger with our numbers, enter in 0.5 s,
//                       combo go on from move step, aim back after landing, next swing on ground normal; spears and
//                       sledges normal swing. NOTE airtime, entry delay, clip, dummy hit time
//   moveset.jump-input  held button at 20 FPS, jump + land then press, fall without jump, jump in restart gap
//   moveset.roll        states forgotten first (each row = first roll attack of a session). Every family: roll,
//                       buffered press, roll attack cut into the roll at FlowStart (after the i-frames), cross-faded
//                       into the probed state (first use of a trigger), Animator never through idle/locomotion (gap 0),
//                       enter that very state, combo go on; Off families: roll gate stay shut, normal swing after roll.
//                       NOTE roll, i-frames, cut, gap, clip
//   moveset.roll-input  held button at 50 and 20 FPS, two StartAttack in one tick (in the roll, after it), window
//                       (0.06 s = still in roll's blend out; 0.2 / 0.3 s = flow or normal swing, never roll attack
//                       after stand-up; Window 1.5 + 1 s = normal swing), FlowStart 0 = cut IframeMargin after
//                       i-frames, learned state (probe off, no state = no cut, trigger alone after roll first; probe's
//                       state = the one entered), other player's view (remote handler on own Animator), cooldown
//   moveset.aim         jump attack swing follow aim down in the air (30, 0, and 45 looking almost straight down:
//                       never past 45), level after landing
//   moveset.stamina     move cost x3: can pay normal only = normal swing on first try, can pay move = 3 swings paid;
//                       press held in the roll with normal-only stamina = refused in the roll, normal swing after
//   moveset.exclusions  torch, pickaxe, scythe, bomb, Smoke Screen after jump and roll: no attack in the roll,
//                       vanilla swing, no move
//   moveset.watchdog    move animation with no way in (trigger alone forced): no cut, start after the roll, clock
//                       from start, restart refused, cancel at 0.5 s, trigger reset, warning once, normal swing next tick
//   moveset.order       NOTE: tick order of PlayerController, Player, MonoUpdaters and Humanoid (hooks only for test)
// Jump and roll tests each split in two (families / input cases): one test must end inside the 120 s timeout.
// Me force rules only through ServerRules.TestRules (never config). Me put back place, look, hands, frame rate,
// controller, god mode, stamina modifier; me destroy dummy and take back every item me gave (Rig.Restore).
// More tests in the other SelfTests.*.cs files (same class): Taps (log tap, hit tap, foes, jump helper), Moves (hits,
// numbers, settings, stamina, facing, flow, toggle...), Cross (other mods, ranged items), Mp (dedicated server).
internal static partial class SelfTests
{
    private const string TriggersName = "moveset.triggers";
    private const string RulesName = "moveset.rules";
    private const string JumpName = "moveset.jump";
    private const string JumpInputName = "moveset.jump-input";
    private const string RollName = "moveset.roll";
    private const string RollInputName = "moveset.roll-input";
    private const string AimName = "moveset.aim";
    private const string StaminaName = "moveset.stamina";
    private const string ExclusionsName = "moveset.exclusions";
    private const string WatchdogName = "moveset.watchdog";
    private const string OrderName = "moveset.order";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(TriggersName, RunTriggers);
        SelfTest.Register(RulesName, RunRules);
        SelfTest.Register(JumpName, RunJump);
        SelfTest.Register(JumpInputName, RunJumpInput);
        SelfTest.Register(RollName, RunRoll);
        SelfTest.Register(RollInputName, RunRollInput);
        SelfTest.Register(AimName, RunAim);
        SelfTest.Register(StaminaName, RunStamina);
        SelfTest.Register(ExclusionsName, RunExclusions);
        SelfTest.Register(WatchdogName, RunWatchdog);
        SelfTest.Register(OrderName, RunOrder);
        RegisterMoves();
        RegisterCross();
        RegisterLog(); // last single-player test: it look at the log of every test before it
        RegisterMp();
#endif
    }

    // Plugin.BindConfig (every start, also when me never activate): log tap from the first line, and the multiplayer
    // test of a server without the mod (me inactive there, so OnDeactivated must not take it away).
    [Conditional("DEBUG")]
    internal static void RegisterAlways()
    {
#if DEBUG
        LogTap.Install();
        RegisterMpInactive();
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        UnregisterMoves();
        UnregisterCross();
        UnregisterLog();
        UnregisterMp();
        if (MoveRules.TestOwn != null)
        {
            MoveRules.TestOwn = null;
            ServerRules.OwnChanged(); // own snapshot was made from the test object: make it again from the config
        }
        if (Compat.TestGco.HasValue)
        {
            Compat.TestGco = null;
            Compat.Reset();
        }
        SelfTest.Unregister(TriggersName);
        SelfTest.Unregister(RulesName);
        SelfTest.Unregister(JumpName);
        SelfTest.Unregister(JumpInputName);
        SelfTest.Unregister(RollName);
        SelfTest.Unregister(RollInputName);
        SelfTest.Unregister(AimName);
        SelfTest.Unregister(StaminaName);
        SelfTest.Unregister(ExclusionsName);
        SelfTest.Unregister(WatchdogName);
        SelfTest.Unregister(OrderName);
        ServerRules.TestRules = null;
        MoveTriggers.TestHasParameter = null;
        RollFlow.TestNoState = false;
        RollFlow.TestNoProbe = false;
#endif
    }

#if DEBUG
    // Me put training dummy this far in front of player (design 7.5).
    private const float DummyDistance = 1.8f;

    // Animator triggers with no way in from idle or run: watchdog try them (design 7.5).
    private static readonly string[] WatchdogTriggers = { "reload_crossbow_done", "recharge_lightningstaff_done" };

    private static readonly WaitForFixedUpdate Fixed = new WaitForFixedUpdate();

    private static readonly MethodInfo MemberwiseCloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

    // Me test one item per family row (design 7.5). "" = bare hands (PlayerUnarmed).
    private sealed class Row
    {
        internal readonly string Prefab;
        internal readonly WeaponFamily Family;

        internal Row(string prefab, WeaponFamily family)
        {
            Prefab = prefab;
            Family = family;
        }

        internal string Label => (Prefab.Length == 0 ? "bare hands" : Prefab) + " (" + Families.Key(Family) + ")";
    }

    private static readonly Row[] Rows =
    {
        new Row("SwordIron", WeaponFamily.Swords),
        new Row("MaceIron", WeaponFamily.Maces),
        new Row("Club", WeaponFamily.Maces),
        new Row("AxeIron", WeaponFamily.Axes),
        new Row("Battleaxe", WeaponFamily.Battleaxes),
        new Row("AxeBerzerkr", WeaponFamily.DualAxes),
        new Row("THSwordKrom", WeaponFamily.Greatswords),
        new Row("AtgeirIron", WeaponFamily.Atgeirs),
        new Row("KnifeCopper", WeaponFamily.Knives),
        new Row("KnifeSkollAndHati", WeaponFamily.DualKnives),
        new Row("SpearBronze", WeaponFamily.Spears),
        new Row("", WeaponFamily.Fists),
        new Row("FistFenrirClaw", WeaponFamily.Fists),
        new Row("SledgeIron", WeaponFamily.Sledges),
    };

    private static readonly Row SwordRow = Rows[0];

    private sealed class Checks
    {
        private readonly string _name;
        private readonly List<string> _failures = new List<string>();
        private int _count;

        internal Checks(string name) => _name = name;

        internal void Check(bool ok, string what)
        {
            _count++;
            if (!ok)
            {
                _failures.Add(what);
            }
        }

        internal void Report(string extra = "")
        {
            if (_failures.Count == 0)
            {
                SelfTest.Pass(_name, $"{_count} checks OK{extra}");
            }
            else
            {
                SelfTest.Fail(_name, $"{_failures.Count} of {_count} checks failed: {string.Join("; ", _failures.ToArray())}");
            }
        }
    }

    // Nested wait put its result here (iterator cannot have out).
    private sealed class Box<T>
    {
        internal T Value;
    }

    private sealed class Dummy
    {
        internal GameObject Go;
        internal Humanoid Body;
        internal MonsterAI Ai;
    }

    // Test stage: what me change on the player, and how me put it back (design 7.5: position, hands, controller,
    // frame rate, rules, tracker). Restore run in finally: no yield there, so re-equip is best effort (NOTE).
    private sealed class Rig
    {
        internal readonly string Test;
        internal readonly Player P;
        internal readonly Vector3 Home;
        internal readonly Vector3 Forward;
        private readonly Quaternion _rotation;
        private readonly Quaternion _lookYaw;
        private readonly float _lookPitch;
        private readonly ItemDrop.ItemData _right;
        private readonly ItemDrop.ItemData _left;
        private readonly PlayerController _controller;
        private readonly bool _controllerEnabled;
        private readonly int _fps;
        private readonly int _vSync;
        private bool _controllerTaken;
        private readonly List<ItemDrop.ItemData> _given = new List<ItemDrop.ItemData>();
        private readonly List<GameObject> _spawned = new List<GameObject>();

        internal Rig(string test, Player player)
        {
            Test = test;
            P = player;
            Home = P.transform.position;
            _rotation = P.transform.rotation;
            _lookYaw = P.m_lookYaw;
            _lookPitch = P.m_lookPitch;
            _right = P.m_rightItem;
            _left = P.m_leftItem;
            _controller = P.GetComponent<PlayerController>();
            _controllerEnabled = _controller != null && _controller.enabled;
            _fps = Application.targetFrameRate;
            _vSync = QualitySettings.vSyncCount;
            Forward = ClearDirection(P);
            ServerRules.TestRules = null;
            MoveTracker.Reset();
        }

        internal ItemDrop.ItemData Give(string prefab, int stack = 1)
        {
            var item = P.GetInventory().AddItem(prefab, stack, 1, 0, 0L, "", false);
            if (item != null && !_given.Contains(item))
            {
                _given.Add(item);
            }
            return item;
        }

        internal void TakeBack(ItemDrop.ItemData item)
        {
            if (item == null || !_given.Contains(item))
            {
                return;
            }
            _given.Remove(item);
            if (P == null)
            {
                return;
            }
            P.UnequipItem(item, false);
            if (P.GetInventory().ContainsItem(item))
            {
                P.GetInventory().RemoveItem(item);
            }
        }

        // Me destroy this object in Restore too (foes of the other test files).
        internal void Track(GameObject go)
        {
            if (go != null && !_spawned.Contains(go))
            {
                _spawned.Add(go);
            }
        }

        internal Dummy SpawnDummy(float distance = DummyDistance)
        {
            var prefab = ZNetScene.instance.GetPrefab("piece_TrainingDummy");
            if (prefab == null)
            {
                return null;
            }
            var go = Object.Instantiate(prefab, DummySpot(distance), Quaternion.LookRotation(-Forward));
            _spawned.Add(go);
            var dummy = new Dummy
            {
                Go = go,
                Body = go.GetComponentInChildren<Humanoid>(),
                Ai = go.GetComponentInChildren<MonsterAI>(),
            };
            // Off at once: no target, no attack, no push (OnDisable take it out of BaseAI.Instances).
            if (dummy.Ai != null)
            {
                dummy.Ai.enabled = false;
            }
            return dummy.Body != null ? dummy : null;
        }

        // Hit push dummy away: me put it back on its spot before each try.
        internal void PlaceDummy(Dummy dummy, float distance = DummyDistance)
        {
            if (dummy == null || dummy.Go == null)
            {
                return;
            }
            var pos = DummySpot(distance);
            dummy.Go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-Forward));
            var body = dummy.Body.m_body;
            if (body != null)
            {
                body.position = pos;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero; // Unity warn when set on a kinematic body
                }
            }
        }

        private Vector3 DummySpot(float distance)
        {
            var pos = Home + Forward * distance;
            pos.y = ZoneSystem.instance.GetGroundHeight(pos);
            return pos;
        }

        // Held button: controller off (it rewrite controls every tick), test call SetControls itself.
        internal void TakeController()
        {
            if (_controller != null)
            {
                _controller.enabled = false;
                _controllerTaken = true;
            }
            Release(P);
        }

        internal void GiveController()
        {
            if (!_controllerTaken)
            {
                return;
            }
            _controllerTaken = false;
            if (P != null)
            {
                Release(P);
            }
            if (_controller != null)
            {
                _controller.enabled = _controllerEnabled;
            }
        }

        // Low frame rate = 2-3 physics ticks per drawn frame (restart gap, design 1.2).
        internal void SetFps(int fps)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = fps;
        }

        internal void RestoreFps()
        {
            Application.targetFrameRate = _fps;
            QualitySettings.vSyncCount = _vSync;
        }

        internal void Restore()
        {
            ServerRules.TestRules = null;
            MoveTriggers.TestHasParameter = null;
            MoveTriggers.Invalidate();
            RollFlow.TestNoState = false;
            RollFlow.TestNoProbe = false;
            RestoreFps();
            if (P != null)
            {
                GiveController();
                foreach (var item in _given.ToArray())
                {
                    TakeBack(item);
                }
                ReEquip(_right);
                ReEquip(_left);
            }
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    ZNetScene.instance.Destroy(go);
                }
            }
            _spawned.Clear();
            if (P != null)
            {
                Put(P, Home);
                P.transform.rotation = _rotation;
                P.m_body.rotation = _rotation;
                P.m_lookYaw = _lookYaw;
                P.m_lookPitch = _lookPitch;
                P.SetMouseLook(Vector2.zero);
            }
            MoveTracker.Reset();
        }

        private void ReEquip(ItemDrop.ItemData item)
        {
            if (item == null || P.IsItemEquiped(item) || !P.GetInventory().ContainsItem(item))
            {
                return;
            }
            if (!P.EquipItem(item, false))
            {
                SelfTest.Note(Test, $"could not put {item.m_shared.m_name} back in the player's hand (player busy)");
            }
        }
    }

    // ---------- helpers ----------

    private static Player LocalPlayer(string test)
    {
        var p = Player.m_localPlayer;
        if (p == null)
        {
            SelfTest.Fail(test, "no local player");
        }
        return p;
    }

    private static ItemDrop Prefab(string name)
    {
        var go = ObjectDB.instance.GetItemPrefab(name);
        return go != null ? go.GetComponent<ItemDrop>() : null;
    }

    private static bool Near(float a, float b, float tolerance = 0.001f) =>
        Mathf.Abs(a - b) <= tolerance * Mathf.Max(1f, Mathf.Abs(b));

    private static string S(float seconds) =>
        seconds < 0f || float.IsNaN(seconds) ? "n/a" : seconds.ToString("0.000", CultureInfo.InvariantCulture);

    private static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // Seconds from one moment to another; n/a when one was never seen (-1), not a fake 0.000.
    private static string Span(float from, float to) => from < 0f || to < 0f ? "n/a" : S(to - from);

    // Degrees under level (negative = over).
    private static float Pitch(Vector3 dir)
    {
        var d = dir.normalized;
        return Mathf.Asin(Mathf.Clamp(-d.y, -1f, 1f)) * Mathf.Rad2Deg;
    }

    private static void Put(Player p, Vector3 pos)
    {
        p.transform.position = pos;
        p.m_body.position = pos;
        p.m_body.linearVelocity = Vector3.zero;
    }

    // Body and camera yaw both along dir: with "attack towards look direction" on or off, swing go that way.
    private static void Face(Player p, Vector3 dir)
    {
        dir.y = 0f;
        var rot = Quaternion.LookRotation(dir.normalized);
        p.transform.rotation = rot;
        p.m_body.rotation = rot;
        p.SetMouseLookForward();
        p.SetMouseLook(Vector2.zero);
    }

    private static void Press(Player p) => p.m_queuedAttackTimer = 0.5f;

    private static void Hold(Player p, bool press) =>
        p.SetControls(Vector3.zero, press, true, false, false, false, false, false, false, false, false);

    private static void Release(Player p) =>
        p.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);

    // Me look for first flat way with room to roll: no wall 6 m ahead, ground within 1 m of player feet.
    private static Vector3 ClearDirection(Player p)
    {
        var fwd = p.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f)
        {
            fwd = Vector3.forward;
        }
        fwd.Normalize();
        var mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain");
        var origin = p.transform.position;
        for (var i = 0; i < 8; i++)
        {
            var dir = Quaternion.Euler(0f, 45f * i, 0f) * fwd;
            var blocked = Physics.Raycast(origin + Vector3.up * 0.6f, dir, 6f, mask)
                          || Physics.Raycast(origin + Vector3.up * 1.4f, dir, 6f, mask);
            var ground = ZoneSystem.instance.GetGroundHeight(origin + dir * 5f);
            if (!blocked && Mathf.Abs(ground - origin.y) < 1f)
            {
                return dir;
            }
        }
        return fwd;
    }

    // Player calm: on ground, no attack, roll, stagger, push, minor action, buffered press or move watch, last
    // attack more than 0.3 s ago (next press start a combo at step 0). Three ticks in a row.
    // No attack = also no attack still STARTING: InAttack read only the Animator, and in the restart gap (trigger set,
    // Animator not in attack state yet, 0.2 s after a roll) it say false while m_timeSinceLastAttack still big. Old
    // Idle said calm there: next step unequip or roll over the pending trigger, Animator play that stale swing later
    // and it eat the next roll (0.5 s dodge buffer run out) or the next press (first run: AxeBerzerkr roll after the
    // Battleaxe row, stamina case B after case A). Starting = current attack not done, never in its animation, young.
    // Playing one keep InAttack true until Attack.Update stop it. Older than StuckStart and never in = stuck (trigger
    // gone some other way, e.g. other mod's test): no wait forever on it.
    private const float StuckStart = 1f;

    private static bool Idle(Player p)
    {
        if (p == null)
        {
            return false;
        }
        var attack = p.m_currentAttack;
        var starting = attack != null && !attack.m_attackDone && !attack.m_wasInAttack && attack.m_time < StuckStart;
        return !starting && p.IsOnGround() && !p.InAttack() && !p.InDodge() && !p.IsStaggering()
               && !p.IsKnockedBack() && !p.InMinorAction() && p.m_queuedAttackTimer <= 0f
               && p.m_queuedDodgeTimer <= 0f && p.m_timeSinceLastAttack > 0.3f && !MoveTracker.Watching;
    }

    // Attack.Start fired one trigger: base + chain step, base + random pick, or base. Attack dropped before its
    // animation (item taken off in restart gap) leave that trigger set: Animator play the swing later and block the
    // next roll or jump. Me reset every name it could be (local Animator only, like MoveTracker.ResetTrigger).
    private static void ResetFired(Player p, Attack a)
    {
        if (p == null || a == null || string.IsNullOrEmpty(a.m_attackAnimation))
        {
            return;
        }
        var zanim = p.m_zanim;
        if (zanim == null)
        {
            return;
        }
        var animator = zanim.m_animator;
        if (animator == null)
        {
            return;
        }
        if (a.m_attackChainLevels > 1)
        {
            animator.ResetTrigger(a.m_attackAnimation + a.m_currentAttackCainLevel);
        }
        else if (a.m_attackRandomAnimations >= 2)
        {
            for (var i = 0; i < a.m_attackRandomAnimations; i++)
            {
                animator.ResetTrigger(a.m_attackAnimation + i);
            }
        }
        else
        {
            animator.ResetTrigger(a.m_attackAnimation);
        }
    }

    // Dummy harmless: its MonsterAI still off and out of BaseAI.Instances (no UpdateAI: no attack, no move, no push)
    // and it never started an attack. Its target say nothing: BaseAI.Awake hook MonsterAI.OnDamaged on
    // Character.m_onDamaged, so every hit set the attacker as target, AI component on or off (vanilla).
    private static bool DummyInert(Dummy dummy)
    {
        if (dummy == null || dummy.Body == null)
        {
            return true;
        }
        var aiOff = dummy.Ai == null || (!dummy.Ai.enabled && !BaseAI.Instances.Contains(dummy.Ai));
        return aiOff && dummy.Body.m_currentAttack == null && dummy.Body.m_previousAttack == null;
    }

    private static IEnumerator WaitIdle(Player p, float seconds, Box<bool> ok = null)
    {
        var until = Time.fixedTime + seconds;
        var calm = 0;
        while (Time.fixedTime < until)
        {
            yield return Fixed;
            calm = Idle(p) ? calm + 1 : 0;
            if (calm >= 3)
            {
                if (ok != null)
                {
                    ok.Value = true;
                }
                yield break;
            }
        }
        if (ok != null)
        {
            ok.Value = false;
        }
    }

    private static IEnumerator WaitTicks(float seconds)
    {
        var until = Time.fixedTime + seconds - 0.001f;
        while (Time.fixedTime < until)
        {
            yield return Fixed;
        }
    }

    private static IEnumerator WaitLanded(Player p, float seconds)
    {
        var until = Time.fixedTime + seconds;
        while (Time.fixedTime < until && !p.IsOnGround())
        {
            yield return Fixed;
        }
    }

    // Me wait for new attack in m_currentAttack (not prev), at most seconds.
    private static IEnumerator WaitNewAttack(Player p, Attack prev, float seconds, Box<Attack> result)
    {
        result.Value = null;
        var until = Time.fixedTime + seconds;
        while (Time.fixedTime < until)
        {
            yield return Fixed;
            var a = p.m_currentAttack;
            if (a != null && !ReferenceEquals(a, prev))
            {
                result.Value = a;
                yield break;
            }
        }
    }

    // Roll started with Dodge: me wait for m_inDodge true -> false edge (tick roll end). press = me press every
    // tick while rolling (vanilla 0.5 s buffer still live at edge).
    private static IEnumerator WaitRollEdge(Player p, float seconds, bool press, Box<float> edgeAt)
    {
        edgeAt.Value = -1f;
        var seen = false;
        var until = Time.fixedTime + seconds;
        while (Time.fixedTime < until)
        {
            yield return Fixed;
            if (p.m_inDodge)
            {
                seen = true;
                if (press)
                {
                    Press(p);
                }
            }
            else if (seen)
            {
                edgeAt.Value = Time.fixedTime;
                yield break;
            }
        }
    }

    // Me empty both hands, then equip row weapon (bare hands: nothing). Null = could not equip.
    private static IEnumerator Equip(Rig rig, Row row, Box<ItemDrop.ItemData> held)
    {
        var p = rig.P;
        held.Value = null;
        yield return WaitIdle(p, 4f);
        p.UnequipItem(p.m_rightItem, false);
        p.UnequipItem(p.m_leftItem, false);
        ItemDrop.ItemData item;
        if (row.Prefab.Length == 0)
        {
            item = p.m_unarmedWeapon != null ? p.m_unarmedWeapon.m_itemData : null;
        }
        else
        {
            item = rig.Give(row.Prefab);
            if (item != null && !p.EquipItem(item, false))
            {
                item = null;
            }
        }
        yield return WaitIdle(p, 3f);
        held.Value = item != null && ReferenceEquals(p.GetCurrentWeapon(), item) ? item : null;
    }

    // Clip(s) Animator go to (in transition) or play, layers 0 and 1: for trigger -> clip NOTE.
    private static string Clips(Animator animator)
    {
        if (animator == null)
        {
            return "no animator";
        }
        var sb = new StringBuilder();
        for (var layer = 0; layer < animator.layerCount && layer < 2; layer++)
        {
            var moving = animator.IsInTransition(layer);
            var infos = moving ? animator.GetNextAnimatorClipInfo(layer) : animator.GetCurrentAnimatorClipInfo(layer);
            sb.Append(layer == 0 ? "" : ", ").Append("layer ").Append(layer).Append(moving ? " to " : " in ");
            if (infos.Length == 0)
            {
                sb.Append("none");
            }
            for (var i = 0; i < infos.Length; i++)
            {
                sb.Append(i > 0 ? "+" : "").Append(infos[i].clip != null ? infos[i].clip.name : "?");
            }
        }
        return sb.ToString();
    }

    // Layer 0 clip Animator go to (in transition) or play: first clip name, null = none.
    private static string Clip0(Animator animator)
    {
        if (animator == null)
        {
            return null;
        }
        var infos = animator.IsInTransition(0) ? animator.GetNextAnimatorClipInfo(0) : animator.GetCurrentAnimatorClipInfo(0);
        return infos.Length > 0 && infos[0].clip != null ? infos[0].clip.name : null;
    }

    // "Gap" of user feedback: layer 0 in, or going to, a state with tag neither dodge nor attack (idle, locomotion).
    // Vanilla blend out of the roll (next state = locomotion) count too.
    private static bool InGap(Animator animator)
    {
        if (animator.IsInTransition(0))
        {
            var next = animator.GetNextAnimatorStateInfo(0).tagHash;
            return next != RollFlow.DodgeTag && next != RollFlow.AttackTag;
        }
        var current = animator.GetCurrentAnimatorStateInfo(0).tagHash;
        return current != RollFlow.DodgeTag && current != RollFlow.AttackTag;
    }

    // Normalized time of dodge state (layer 0 current), -1 when not in it.
    private static float DodgeNormalized(Animator animator)
    {
        if (animator == null)
        {
            return -1f;
        }
        var s = animator.GetCurrentAnimatorStateInfo(0);
        return s.tagHash == RollFlow.DodgeTag ? s.normalizedTime : -1f;
    }

    // Layer 0 state Animator is in or go to: full path and short name hash (me compare with name lookup).
    private static void EntryHashes(Animator animator, out int full, out int shortName)
    {
        var s = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
        full = s.fullPathHash;
        shortName = s.shortNameHash;
    }

    // What one roll + attack did, tick by tick (Roll coroutine fill it).
    private sealed class RollRun
    {
        internal float DodgeAt = -1f;
        internal float RollStart = -1f;     // first tick m_inDodge
        internal float RollEnd = -1f;       // first tick m_inDodge false again
        internal float IframesEnd = -1f;    // first tick m_dodgeInvincible false
        internal float StartAt = -1f;       // tick the first new attack started
        internal float EnteredAt = -1f;     // tick it was in its attack state (m_wasInAttack)
        internal float CutNorm = -1f;       // dodge state normalized time when it started (-1 = not in dodge)
        internal Attack Started;
        internal Attack Clone;              // move clone, null = no move started
        internal MoveInfo Move;
        internal bool StartedInRoll;        // m_inDodge still true on the start tick
        internal bool InvulnAfterStart;     // IsDodgeInvincible seen from the start on (must never)
        internal float Delay = -1f;         // MoveTracker entry delay of the move
        internal int GapTicks;              // ticks of "gap" from roll start to entry
        internal int EntryFull;
        internal int EntryShort;
        internal string Clip;               // layer 0 clip at entry
        internal int Refusals;              // attacks our gate refused inside the roll
        internal int Skips;                 // presses inside the roll for which the gate stayed shut
        // Caller set it when the button stay held after the roll (controller off, nobody let go). Vanilla then start a
        // normal swing AGAIN on every tick of the stand-up (Animator not in the attack yet, so StartAttack pass: the
        // restart gap of design 1.2), each time a new Attack object, until one is in its animation. Started = the
        // newest one (the one that play and hit), StartAt stay the first start. First run: moveset.x.dualwield-off
        // kept the first object, which never played: no hit event of it, and "next" was a later combo step.
        internal bool Held;
        internal int Restarts;              // Held: starts after the first one, before the swing was in its animation

        internal float Cut => StartAt >= 0f && RollStart >= 0f ? StartAt - RollStart : -1f;
        internal float Gap => GapTicks * Time.fixedDeltaTime;
    }

    // Me roll along rig.Forward; press every tick while rolling (buffered press) when press; follow until first new
    // attack is in its state (or me give up). Caller put and face the player first. how: other press than the
    // buffered primary one (secondary press, held button...), called every tick while rolling until an attack start.
    private static IEnumerator Roll(Rig rig, RollRun run, bool press = true, Action<Player> how = null)
    {
        var p = rig.P;
        var animator = p.m_zanim.m_animator;
        var before = MoveTracker.LastMove.Clone;
        var prev = p.m_currentAttack;
        var refusals = MoveTracker.GateRefusals;
        var skips = MoveTracker.GateSkips;
        p.Dodge(rig.Forward);
        run.DodgeAt = Time.fixedTime;
        while (Time.fixedTime - run.DodgeAt < 4f)
        {
            yield return Fixed;
            var now = Time.fixedTime;
            var inDodge = p.m_inDodge;
            if (run.RollStart < 0f && inDodge)
            {
                run.RollStart = now;
            }
            if (run.RollStart >= 0f && run.IframesEnd < 0f && !p.m_dodgeInvincible)
            {
                run.IframesEnd = now;
            }
            if (run.RollStart >= 0f && run.RollEnd < 0f && !inDodge)
            {
                run.RollEnd = now;
            }
            var current = p.m_currentAttack;
            if (run.Started == null && current != null && !ReferenceEquals(current, prev))
            {
                run.Started = current;
                run.StartAt = now;
                run.StartedInRoll = inDodge;
                run.CutNorm = DodgeNormalized(animator);
            }
            else if (run.Held && run.Started != null && current != null && !ReferenceEquals(current, run.Started)
                     && !run.Started.m_wasInAttack)
            {
                run.Started = current; // held button restarted the swing in the stand-up: me follow the newest one
                run.Restarts++;
            }
            if (inDodge && press && run.Started == null)
            {
                if (how != null)
                {
                    how(p);
                }
                else
                {
                    Press(p); // buffered press: vanilla keeps it 0.5 s. Stop once started: no stray combo step after.
                }
            }
            if (run.Clone == null && !ReferenceEquals(MoveTracker.LastMove.Clone, before))
            {
                run.Move = MoveTracker.LastMove;
                run.Clone = run.Move.Clone;
            }
            var entered = run.Started != null && run.Started.m_wasInAttack;
            if (run.RollStart >= 0f && !entered && animator != null && InGap(animator))
            {
                run.GapTicks++;
            }
            if (run.Started != null && p.IsDodgeInvincible())
            {
                run.InvulnAfterStart = true;
            }
            if (entered)
            {
                run.EnteredAt = now;
                run.Clip = Clip0(animator);
                if (animator != null)
                {
                    EntryHashes(animator, out run.EntryFull, out run.EntryShort);
                }
                if (run.Clone != null && ReferenceEquals(run.Started, run.Clone))
                {
                    run.Delay = MoveTracker.LastEntryDelay;
                }
                break;
            }
            if ((run.RollStart < 0f && now - run.DodgeAt > 0.6f) || (run.Started != null && now - run.StartAt > 1.5f))
            {
                break;
            }
        }
        run.Refusals = MoveTracker.GateRefusals - refusals;
        run.Skips = MoveTracker.GateSkips - skips;
    }

    // What a roll + attack gave, with its numbers (check and NOTE text: a failed check must say what happened).
    private static string RollWhat(RollRun run)
    {
        var roll = $"the roll (m_inDodge) lasted {Span(run.RollStart, run.RollEnd)} s";
        if (run.Started == null)
        {
            return "no attack; " + roll;
        }
        if (run.Clone == null || !ReferenceEquals(run.Started, run.Clone))
        {
            return $"normal swing {Fired(run.Started)} {Span(run.RollStart, run.StartAt)} s after the roll started; {roll}";
        }
        var where = run.Move.Cut ? $"cut into the roll {S(run.Cut)} s after it started" : $"{S(run.Move.RollAge)} s after the roll ended";
        return $"{run.Move.Kind} attack {run.Move.Trigger}, {where}, flow {run.Move.Flow}, {run.GapTicks} idle tick(s) before its animation, "
               + $"in it {Span(run.StartAt, run.EnteredAt)} s after its start; {roll}";
    }

    // Step after a move that play step k of an n-level chain (design 2.5); -1 = not a chain step.
    private static int NextStep(MoveInfo move)
    {
        if (move.Step < 0)
        {
            return -1;
        }
        return move.Step + 1 < move.ChainLevels ? move.Step + 1 : 0;
    }

    private static bool Same(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }
        for (var i = 0; i < a.Length; i++)
        {
            if (!Near(a[i], b[i]))
            {
                return false;
            }
        }
        return true;
    }

    private static float[] Numbers(MoveRules r) => new[]
    {
        r.Cooldown, r.Jump.Damage, r.Jump.Stagger, r.Jump.Push, r.Jump.Stamina, r.AimAngle, r.Roll.Damage,
        r.Roll.Stagger, r.Roll.Push, r.Roll.Stamina, r.Window, r.FlowStart, r.FlowBlend,
    };

    private static bool SameRules(MoveRules a, MoveRules b)
    {
        return a.JumpAttack == b.JumpAttack && a.RollAttack == b.RollAttack && Same(Numbers(a), Numbers(b))
               && a.JumpTriggers.SequenceEqual(b.JumpTriggers) && a.RollTriggers.SequenceEqual(b.RollTriggers);
    }

    // Wire layout written by hand (layout number and family count free): proves TryRead refuse other ones.
    private static ZPackage Raw(MoveRules r, int layout, int familyCount)
    {
        var pkg = new ZPackage();
        pkg.Write(layout);
        pkg.Write(r.JumpAttack);
        pkg.Write(r.RollAttack);
        foreach (var n in Numbers(r))
        {
            pkg.Write(n);
        }
        pkg.Write(familyCount);
        for (var i = 0; i < familyCount; i++)
        {
            pkg.Write(r.JumpTriggers[i % Families.Count]);
        }
        for (var i = 0; i < familyCount; i++)
        {
            pkg.Write(r.RollTriggers[i % Families.Count]);
        }
        pkg.SetPos(0);
        return pkg;
    }

    // ---------- moveset.triggers ----------

    private static IEnumerator RunTriggers()
    {
        var p = LocalPlayer(TriggersName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(TriggersName);
        try
        {
            CheckAnimator(c, p);
            CheckResolve(c);
            CheckLists(c);
            CheckFamilies(c, p);
            CheckGates(c, p);
            CheckProbe(c, p);
            var bolt = Prefab("BoltBone") != null;
            var arrow = Prefab("ArrowWood") != null;
            SelfTest.Note(TriggersName, $"spawn names (TESTING S1): BoltBone {(bolt ? "exists" : "MISSING")}, "
                                        + $"ArrowWood {(arrow ? "exists" : "MISSING")}; Goo's Combat Overhaul "
                                        + $"{(Compat.GcoLoaded ? "installed" : "not installed")}");
            c.Report();
        }
        finally
        {
            MoveTriggers.TestHasParameter = null;
            MoveTriggers.ResetWarnings();
            MoveTriggers.Invalidate();
        }
    }

    // Every name in list, every default, and triggers game itself need: all must be Trigger parameters.
    private static void CheckAnimator(Checks c, Player p)
    {
        var zanim = p.GetZAnim();
        var missing = MoveTriggers.Melee
            .Where(n => !zanim.HasParameter(n, AnimatorControllerParameterType.Trigger)).ToList();
        c.Check(missing.Count == 0,
            $"every animation in the settings list is a Trigger of the player Animator (missing: {string.Join(", ", missing.ToArray())})");
        c.Check(MoveTriggers.Melee.Length == 39 && MoveTriggers.Melee.Distinct().Count() == 39,
            $"39 distinct melee animations offered, found {MoveTriggers.Melee.Length}");
        foreach (var kind in Moves.All)
        {
            foreach (var family in Families.All)
            {
                var def = MoveTriggers.Default(kind, family);
                c.Check(def == MoveTriggers.Off || Array.IndexOf(MoveTriggers.Melee, def) >= 0,
                    $"{Moves.Name(kind)} default of {Families.Key(family)} ({def}) is in the list");
            }
        }
        c.Check(zanim.HasParameter("jump", AnimatorControllerParameterType.Trigger)
                && zanim.HasParameter("dodge", AnimatorControllerParameterType.Trigger),
            "jump and dodge triggers exist");
        foreach (var name in WatchdogTriggers)
        {
            SelfTest.Note(TriggersName, $"watchdog trigger {name}: "
                                        + (zanim.HasParameter(name, AnimatorControllerParameterType.Trigger) ? "exists" : "MISSING"));
        }
    }

    // Default rules give defaults, no warning. Bad name from server fall back, one warning.
    private static void CheckResolve(Checks c)
    {
        MoveTriggers.TestHasParameter = null;
        MoveTriggers.ResetWarnings();
        MoveTriggers.Invalidate();
        var defaults = MoveRules.Defaults();
        foreach (var kind in Moves.All)
        {
            foreach (var family in Families.All)
            {
                var def = MoveTriggers.Default(kind, family);
                var want = def == MoveTriggers.Off ? null : def;
                var got = MoveTriggers.Resolve(defaults, kind, family);
                c.Check(got == want, $"{Moves.Name(kind)} of {Families.Key(family)} resolves to {want ?? "Off"}, got {got ?? "Off"}");
            }
        }
        c.Check(MoveTriggers.WarningCount == 0, $"default rules resolve without a warning ({MoveTriggers.WarningCount})");

        // Unknown name from server (through wire): family default, exactly one warning.
        var sent = MoveRules.Defaults();
        sent.JumpTriggers[Families.Index(WeaponFamily.Swords)] = "swing_longsword9";
        var pkg = new ZPackage();
        sent.Write(pkg);
        pkg.SetPos(0);
        var read = MoveRules.TryRead(pkg, out var received, out _);
        c.Check(read, "rules with an unknown animation name are readable (names are checked later)");
        if (read)
        {
            MoveTriggers.ResetWarnings();
            MoveTriggers.Invalidate();
            var got = MoveTriggers.Resolve(received, MoveKind.Jump, WeaponFamily.Swords);
            var again = MoveTriggers.Resolve(received, MoveKind.Jump, WeaponFamily.Swords);
            c.Check(got == "swing_longsword2" && again == got, $"unknown name falls back to swing_longsword2, got {got ?? "Off"}");
            var other = MoveRules.Defaults();
            other.JumpTriggers[Families.Index(WeaponFamily.Swords)] = "swing_longsword9";
            MoveTriggers.Resolve(other, MoveKind.Jump, WeaponFamily.Swords); // new snapshot: rebuild, already warned
            c.Check(MoveTriggers.WarningCount == 1, $"exactly one warning for the unknown name ({MoveTriggers.WarningCount})");
        }

        // Listed name Animator no have (game update dropped it), me pretend with hook: default.
        // axe_secondary: no family's default, so only this one setting fall back.
        MoveTriggers.ResetWarnings();
        MoveTriggers.Invalidate();
        MoveTriggers.TestHasParameter = n => n != "axe_secondary";
        var dropped = MoveRules.Defaults();
        dropped.RollTriggers[Families.Index(WeaponFamily.Axes)] = "axe_secondary";
        var axe = MoveTriggers.Resolve(dropped, MoveKind.Roll, WeaponFamily.Axes);
        c.Check(axe == "swing_axe1" && MoveTriggers.WarningCount == 1,
            $"a name the Animator lacks falls back to the default swing_axe1 with one warning (got {axe ?? "Off"}, {MoveTriggers.WarningCount} warnings)");

        // Default gone too: Off (normal swing), one warning.
        MoveTriggers.ResetWarnings();
        MoveTriggers.Invalidate();
        MoveTriggers.TestHasParameter = n => n != "swing_axe1";
        var gone = MoveTriggers.Resolve(MoveRules.Defaults(), MoveKind.Roll, WeaponFamily.Axes);
        c.Check(gone == null && MoveTriggers.WarningCount == 1,
            $"when the default is missing too the move is Off with one warning (got {gone ?? "Off"}, {MoveTriggers.WarningCount} warnings)");

        MoveTriggers.TestHasParameter = null;
        MoveTriggers.ResetWarnings();
        MoveTriggers.Invalidate();
    }

    // Each animation drop-down: own default first (BepInEx clamp typo to it), then Off, 40 values.
    private static void CheckLists(Checks c)
    {
        foreach (var family in Families.All)
        {
            var f = Families.Index(family);
            foreach (var kind in Moves.All)
            {
                var entry = kind == MoveKind.Jump ? Plugin.JumpAnimation[f] : Plugin.RollAnimation[f];
                var list = entry != null ? entry.Description.AcceptableValues as AcceptableValueList<string> : null;
                var values = list != null ? list.AcceptableValues : new string[0];
                var def = MoveTriggers.Default(kind, family);
                c.Check(values.Length == MoveTriggers.Melee.Length + 1 && values.Distinct().Count() == values.Length
                        && values[0] == def && (def == MoveTriggers.Off || values[1] == MoveTriggers.Off),
                    $"{Moves.AnimationSection(kind)} / {Families.Key(family)}: list starts with {def}, then Off, 40 values (has {values.Length})");
                c.Check(entry != null && (string)entry.DefaultValue == def,
                    $"{Moves.AnimationSection(kind)} / {Families.Key(family)}: default value {def}");
            }
        }
    }

    // Every player melee weapon (item with recipe, plus bare hands) must have family; named rows must have own one.
    private static void CheckFamilies(Checks c, Player p)
    {
        var withRecipe = new HashSet<string>(ObjectDB.instance.m_recipes
            .Where(r => r != null && r.m_item != null).Select(r => r.m_item.gameObject.name));
        var counts = new Dictionary<WeaponFamily, int>();
        var unmapped = new List<string>();
        var drops = ObjectDB.instance.m_items.Where(go => go != null && withRecipe.Contains(go.name))
            .Select(go => go.GetComponent<ItemDrop>()).Where(d => d != null).ToList();
        if (p.m_unarmedWeapon != null)
        {
            drops.Add(p.m_unarmedWeapon);
        }
        foreach (var drop in drops)
        {
            var s = drop.m_itemData.m_shared;
            var a = s.m_attack;
            if (a == null || ItemKinds.Classify(s) != ItemKind.Weapon || s.m_skillType == Skills.SkillType.Blocking
                || a.m_requiresReload || a.m_bowDraw
                || (a.m_attackType != Attack.AttackType.Horizontal && a.m_attackType != Attack.AttackType.Vertical
                    && a.m_attackType != Attack.AttackType.Area))
            {
                continue;
            }
            var family = Families.Of(a.m_attackAnimation, s.m_skillType);
            if (family == WeaponFamily.None)
            {
                unmapped.Add($"{drop.gameObject.name} ({a.m_attackAnimation})");
                continue;
            }
            counts[family] = counts.TryGetValue(family, out var n) ? n + 1 : 1;
        }
        var line = string.Join(", ", Families.All.Select(f => $"{Families.Key(f)} {(counts.TryGetValue(f, out var n) ? n : 0)}").ToArray());
        SelfTest.Note(TriggersName, $"player melee weapons per family: {line}");
        if (unmapped.Count > 0)
        {
            SelfTest.Note(TriggersName, $"player melee weapons with no family (a game update added one?): {string.Join(", ", unmapped.ToArray())}");
        }
        c.Check(Families.All.All(f => counts.ContainsKey(f)), "every family has at least one player weapon");

        var named = Rows.Where(r => r.Prefab.Length > 0).ToList();
        named.Add(new Row("Club", WeaponFamily.Maces));
        named.Add(new Row("PlayerUnarmed", WeaponFamily.Fists));
        foreach (var row in named)
        {
            var drop = row.Prefab == "PlayerUnarmed" && p.m_unarmedWeapon != null ? p.m_unarmedWeapon : Prefab(row.Prefab);
            if (drop == null)
            {
                c.Check(false, $"{row.Prefab} exists");
                continue;
            }
            var ok = Families.Eligible(drop.m_itemData.m_shared.m_attack, drop.m_itemData, out var family);
            c.Check(ok && family == row.Family, $"{row.Prefab} is eligible and maps to {Families.Key(row.Family)} (got {Families.Key(family)})");
        }
    }

    // Gates (design 2.2): torch, tools, bows, crossbows, staves, harpoon, bombs, Smoke Screen, tankard, shields,
    // and anything with the Blocking skill never become a move.
    private static void CheckGates(Checks c, Player p)
    {
        string[] excluded =
        {
            "Torch", "PickaxeIron", "Scythe", "Bow", "CrossbowArbalest", "StaffFireball", "SpearChitin", "BombOoze",
            "MC_SmokeScreen", "Tankard", "Hammer", "ShieldWoodTower",
        };
        foreach (var name in excluded)
        {
            var drop = Prefab(name);
            if (drop == null)
            {
                if (name == "MC_SmokeScreen")
                {
                    SelfTest.Note(TriggersName, "MC_SmokeScreen not registered (Sneak Ambush off or absent): SKIP");
                }
                else
                {
                    c.Check(false, $"{name} exists");
                }
                continue;
            }
            var attack = drop.m_itemData.m_shared.m_attack;
            c.Check(attack == null || !Families.Eligible(attack, drop.m_itemData, out _), $"{name} is never a move");
        }
        var unarmed = p.m_unarmedWeapon;
        if (unarmed != null && MemberwiseCloneMethod != null)
        {
            var data = unarmed.m_itemData.Clone();
            var shared = (ItemDrop.ItemData.SharedData)MemberwiseCloneMethod.Invoke(unarmed.m_itemData.m_shared, null);
            shared.m_skillType = Skills.SkillType.Blocking;
            data.m_shared = shared;
            c.Check(!Families.Eligible(shared.m_attack, data, out _), "a fist attack with the Blocking skill (tower bash) is never a move");
            c.Check(unarmed.m_itemData.m_shared.m_skillType != Skills.SkillType.Blocking, "the real bare hands data is untouched");
        }
    }

    // Roll flow (design 2.9): every default roll animation has a clip in the table (the probe's reference, from the
    // live Animator). Controller probe, one trigger at a time with the stance of that trigger's own weapon type: every
    // table trigger has an attack state, it plays the table's clip, and it is the learned one when a swing already
    // taught it this session. The player's own state behaviours keep their effects (the probe mutes only its copy's).
    // NOTE cost, muted behaviours, and what one batch with the bare-hands stance finds (does the stance matter?).
    private static void CheckProbe(Checks c, Player p)
    {
        foreach (var family in Families.All)
        {
            var def = MoveTriggers.Default(MoveKind.Roll, family);
            if (def == MoveTriggers.Off)
            {
                continue;
            }
            c.Check(RollFlow.ClipOf(def) != null, $"the roll animation {def} of {Families.Key(family)} has a clip in the roll flow's clip table");
        }
        var animator = p.m_zanim != null ? p.m_zanim.m_animator : null;
        if (animator == null)
        {
            c.Check(false, "the local player has an Animator");
            return;
        }
        var own = OwnEffects(animator);
        var triggers = RollFlow.TableTriggers.ToArray();
        var perTrigger = new int[triggers.Length];
        var one = new int[1];
        var clip = new string[1];
        var ms = 0.0;
        var updates = 0;
        var muted = 0;
        var learnedSeen = 0;
        for (var i = 0; i < triggers.Length; i++)
        {
            var trigger = triggers[i];
            var stance = StanceOf(p, trigger);
            RollFlow.ProbeNow(animator, new[] { trigger }, stance, one, clip);
            ms += StateProbe.LastMilliseconds;
            updates += StateProbe.LastUpdates;
            muted = StateProbe.LastMuted;
            perTrigger[i] = one[0];
            var want = RollFlow.ClipOf(trigger);
            c.Check(one[0] != 0 && animator.HasState(0, one[0]),
                $"the animation controller has an attack state for {trigger} (probed with stance {StateProbe.LastStance})");
            c.Check(clip[0] == want, $"the probed state of {trigger} plays its clip {want} (got {clip[0] ?? "none"})");
            var learned = RollFlow.LearnedHash(trigger);
            if (learned != 0)
            {
                learnedSeen++;
                c.Check(learned == one[0], $"the probed state of {trigger} is the one a swing taught this session ({one[0]}, learned {learned})");
            }
        }
        var batch = new int[triggers.Length];
        RollFlow.ProbeNow(animator, triggers, 0, batch, null);
        var same = 0;
        for (var i = 0; i < triggers.Length; i++)
        {
            if (batch[i] != 0 && batch[i] == perTrigger[i])
            {
                same++;
            }
        }
        var ownAfter = OwnEffects(animator);
        c.Check(ownAfter == own, $"the probe leaves the player's own state behaviours untouched ({own} with effects before, {ownAfter} after)");
        c.Check(StateProbe.LastShared == 0,
            $"the probe's copy has its own state behaviours, none shared with the player's Animator ({StateProbe.LastShared} shared)");
        SelfTest.Note(TriggersName, $"controller probe: {triggers.Length} triggers one at a time in {ms.ToString("0.0", CultureInfo.InvariantCulture)} ms "
                                    + $"({updates} Animator updates), {learnedSeen} compared with a learned state; {muted} state behaviours "
                                    + $"muted on the copy, the player's own have {own} with effects; one batch of all with the bare-hands "
                                    + $"stance: {StateProbe.LastMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms, "
                                    + $"{same} of {triggers.Length} the same state");
    }

    // Stance (statei) of the weapon type a trigger belongs to: base name (digits off) -> family -> that family's row
    // weapon (bare hands for fists). Unknown = the Animator's own.
    private static int StanceOf(Player p, string trigger)
    {
        var baseName = trigger.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        var family = Families.Of(baseName, Skills.SkillType.Swords);
        foreach (var row in Rows)
        {
            if (row.Family != family)
            {
                continue;
            }
            var drop = row.Prefab.Length == 0 ? p.m_unarmedWeapon : Prefab(row.Prefab);
            if (drop != null)
            {
                return (int)drop.m_itemData.m_shared.m_animationState;
            }
        }
        return StateProbe.LiveStance;
    }

    // StateController behaviours of this Animator that would spawn an effect or toggle children on entry.
    private static int OwnEffects(Animator animator)
    {
        var n = 0;
        foreach (var b in animator.GetBehaviours<StateController>())
        {
            if (b != null && ((b.m_enterEffect != null && b.m_enterEffect.HasEffects()) || b.m_enterDisableChildren
                              || b.m_enterEnableChildren))
            {
                n++;
            }
        }
        return n;
    }

    // ---------- moveset.rules ----------

    private static IEnumerator RunRules()
    {
        var p = LocalPlayer(RulesName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(RulesName);
        try
        {
            CheckWire(c);
            CheckPick(c);
            CheckJoin(c);
            CheckDecide(c);
            CheckStep(c);
            CheckEdit(c, p);
            c.Report();
        }
        finally
        {
            ServerRules.TestRules = null;
        }
    }

    private static void CheckWire(Checks c)
    {
        var odd = MoveRules.Defaults();
        odd.JumpAttack = false;
        odd.Cooldown = 2.5f;
        odd.Jump.Damage = 1.7f;
        odd.Jump.Stagger = 3.5f;
        odd.Jump.Push = 0.5f;
        odd.Jump.Stamina = 2f;
        odd.AimAngle = 45f;
        odd.Roll.Damage = 0.8f;
        odd.Roll.Stagger = 0f;
        odd.Roll.Push = 4f;
        odd.Roll.Stamina = 0.25f;
        odd.Window = 0.9f;
        odd.FlowStart = 1.1f;
        odd.FlowBlend = 0.3f;
        odd.JumpTriggers[Families.Index(WeaponFamily.Swords)] = "greatsword2";
        odd.JumpTriggers[Families.Index(WeaponFamily.Fists)] = MoveTriggers.Off;
        odd.RollTriggers[Families.Index(WeaponFamily.Spears)] = "spear_poke";
        var pkg = new ZPackage();
        odd.Write(pkg);
        pkg.SetPos(0);
        c.Check(MoveRules.TryRead(pkg, out var back, out var clamped) && !clamped && SameRules(back, odd)
                && back.Describe() == odd.Describe(),
            "rules survive the wire unchanged (every field)");
        c.Check(MoveRules.TryRead(Raw(odd, MoveRules.Layout, Families.Count), out back, out _) && SameRules(back, odd),
            $"the hand-written layout {MoveRules.Layout} matches Write");
        c.Check(MoveRules.Layout == 2, $"rules layout 2 since the roll flow (is {MoveRules.Layout})");

        var wild = MoveRules.Defaults();
        wild.Cooldown = 100f;
        wild.Jump.Damage = 50f;
        wild.Jump.Stagger = -3f;
        wild.Jump.Push = float.PositiveInfinity;
        wild.Jump.Stamina = float.NaN;
        wild.AimAngle = 200f;
        wild.Window = 0f;
        wild.FlowStart = -5f;
        wild.FlowBlend = 9f;
        pkg = new ZPackage();
        wild.Write(pkg);
        pkg.SetPos(0);
        c.Check(MoveRules.TryRead(pkg, out back, out clamped) && clamped && Near(back.Cooldown, MoveRules.MaxCooldown)
                && Near(back.Jump.Damage, MoveRules.MaxDamage) && Near(back.Jump.Stagger, MoveRules.MinStagger)
                && Near(back.Jump.Push, MoveRules.MaxPush) && Near(back.Jump.Stamina, MoveRules.DefaultJumpStamina)
                && Near(back.AimAngle, MoveRules.MaxAimAngle) && Near(back.Window, MoveRules.MinWindow)
                && Near(back.FlowStart, MoveRules.MinFlowStart) && Near(back.FlowBlend, MoveRules.MaxFlowBlend),
            "out-of-range numbers from the wire are pulled into the setting ranges (NaN = default)");

        c.Check(!MoveRules.TryRead(Raw(odd, MoveRules.Layout + 1, Families.Count), out _, out _), "unknown layout refused");
        c.Check(!MoveRules.TryRead(Raw(odd, 1, Families.Count), out _, out _),
            "layout 1 (the first test build, before the roll flow) refused");
        c.Check(!MoveRules.TryRead(Raw(odd, MoveRules.Layout, Families.Count - 1), out _, out _), "other family count refused");
        pkg = new ZPackage();
        pkg.Write(MoveRules.Layout);
        pkg.Write(true);
        pkg.SetPos(0);
        c.Check(!MoveRules.TryRead(pkg, out _, out _), "cut package refused");
        c.Check(!MoveRules.TryRead(null, out _, out _), "no package refused");

        c.Check(!MoveRules.Off.JumpAttack && !MoveRules.Off.RollAttack, "pending rules: both moves off");
        var defaults = MoveRules.Defaults();
        c.Check(Families.All.All(f => defaults.Trigger(MoveKind.Jump, f) == MoveTriggers.Default(MoveKind.Jump, f)
                                      && defaults.Trigger(MoveKind.Roll, f) == MoveTriggers.Default(MoveKind.Roll, f)),
            "default rules carry the default animations");
    }

    // Client pick (design Decision 20). Single player / server never take rules from peer.
    private static void CheckPick(Checks c)
    {
        var own = MoveRules.Defaults();
        var server = MoveRules.Defaults();
        server.Cooldown = 3f;
        c.Check(ReferenceEquals(ServerRules.Pick(true, null, true, own), MoveRules.Off),
            "connected client without the server's rules: moves off (never its own settings)");
        c.Check(ReferenceEquals(ServerRules.Pick(true, server, true, own), server), "connected client uses the server's rules");
        c.Check(ReferenceEquals(ServerRules.Pick(true, server, false, own), MoveRules.Off),
            "rules from an earlier connection are not used");
        c.Check(ReferenceEquals(ServerRules.Pick(false, server, true, own), own), "server, host and single player use their own");

        var pkg = new ZPackage();
        server.Write(pkg);
        pkg.SetPos(0);
        c.Check(!ServerRules.Receive(pkg) && !ServerRules.Receive(new ZPackage()),
            "this game (server of the test world) never takes rules from a peer");
        ServerRules.TestRules = null;
        var current = ServerRules.Current;
        c.Check(ServerRules.Source == RulesSource.Own && current.JumpAttack == Plugin.JumpAttack.Value
                && Near(current.Cooldown, Plugin.Cooldown.Value),
            "the test world uses its own settings");
        var forced = MoveRules.Defaults();
        ServerRules.TestRules = forced;
        c.Check(ReferenceEquals(ServerRules.Current, forced) && ServerRules.Source == RulesSource.SelfTest,
            "self-test rules override in memory");
        ServerRules.TestRules = null;
        c.Check(!ServerRules.FromServer(current) && !ServerRules.FromServer(forced) && !ServerRules.FromServer(null),
            "own and self-test rules are not the server's (warnings name the player's own settings)");

        // Warning advice: own settings = where to change it; server's = ask its admin (own settings do nothing there).
        var ownAdvice = Moves.AnimationAdvice(MoveKind.Jump, false);
        var serverAdvice = Moves.AnimationAdvice(MoveKind.Roll, true);
        c.Check(ownAdvice.Contains(Moves.JumpAnimationSection) && !ownAdvice.Contains("server")
                && serverAdvice.Contains(Moves.RollAnimationSection) && serverAdvice.Contains("server admin"),
            $"warning advice: own \"{ownAdvice}\" / server \"{serverAdvice}\"");

        // Server push wait: settings still PushDelay s, then one push (slider drag = one push, not one per frame).
        c.Check(!ServerRules.Settled(10f, 10f - ServerRules.PushDelay * 0.5f) && ServerRules.Settled(10f, 10f - ServerRules.PushDelay)
                && ServerRules.Settled(0f, float.NegativeInfinity),
            "settings push waits until they stay still, turning on pushes at once");
    }

    private static void CheckJoin(Checks c)
    {
        c.Check(PlayerCheck.Decide(true, true, true, false, true, false) == JoinVerdict.Compatible,
            "compatible player allowed");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, false) == JoinVerdict.Refuse,
            "not compatible (no mod, other network version, turned off) refused with the setting off");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, true) == JoinVerdict.Allowed,
            "not compatible allowed with AllowPlayersWithoutMod on");
        c.Check(PlayerCheck.Decide(false, true, true, false, false, false) == JoinVerdict.Skip
                && PlayerCheck.Decide(true, false, true, false, false, false) == JoinVerdict.Skip
                && PlayerCheck.Decide(true, true, false, false, false, false) == JoinVerdict.Skip
                && PlayerCheck.Decide(true, true, true, true, false, false) == JoinVerdict.Skip,
            "not the server, player gone, not ready or already being kicked: skipped");
    }

    // Decide truth table (design 7.5).
    private static void CheckDecide(Checks c)
    {
        var rules = MoveRules.Defaults(); // window 0.4, cooldown 1, flow start 0.85
        const float none = float.PositiveInfinity;
        var fs = rules.FlowStart;
        var inside = fs + 0.04f; // still inside a 0.92 s roll

        // rollTime: seconds inside the roll (negative = not in a roll); iframes: roll's i-frames still on (with the
        // margin); flowOver: after a roll with its animation, the blend out of it is over (stand-up done).
        MoveKind D(MoveRules r, bool secondary = false, bool inAttack = false, bool swimming = false,
            bool attached = false, bool onGround = true, bool jumpLive = false, float rollTime = -1f,
            bool iframes = false, float rollAge = none, bool flowOver = false, float sinceLastMove = none,
            bool gco = false)
        {
            return MoveTracker.Decide(r, secondary, inAttack, swimming, attached, onGround, jumpLive, rollTime,
                iframes, rollAge, flowOver, sinceLastMove, gco, out _, out _);
        }

        // Cut into the roll (roll flow, design 2.4).
        c.Check(D(rules, rollTime: fs) == MoveKind.Roll && D(rules, rollTime: inside) == MoveKind.Roll,
            $"inside the roll from FlowStart ({F(fs)} s) with the i-frames over: roll attack (cut)");
        c.Check(D(rules, rollTime: fs - 0.02f) == MoveKind.None && D(rules, rollTime: 0f) == MoveKind.None,
            "inside the roll before FlowStart: no move (vanilla refuses attacks in the roll)");
        c.Check(D(rules, rollTime: inside, iframes: true) == MoveKind.None,
            "inside the roll with its i-frames (or their margin) still on: no cut, whatever the time");
        var flow0 = MoveRules.Defaults();
        flow0.FlowStart = 0f;
        c.Check(D(flow0, rollTime: 0.3f, iframes: true) == MoveKind.None && D(flow0, rollTime: 0.64f) == MoveKind.Roll,
            "FlowStart 0: cut as soon as the i-frames and their margin are over, never before");
        c.Check(D(rules, secondary: true, rollTime: inside) == MoveKind.None, "secondary inside the roll: none");
        var late = MoveRules.Defaults();
        late.FlowStart = 2f;
        c.Check(D(late, rollTime: 0.9f) == MoveKind.None && D(late, rollAge: 0.02f) == MoveKind.Roll,
            "FlowStart longer than the roll: no cut, roll attack after the roll");
        var inRoll = MoveTracker.Decide(rules, false, false, false, false, true, false, inside, false, none, false,
            0.2f, false, out var cooledIn, out var leftIn);
        c.Check(inRoll == MoveKind.None && cooledIn == MoveKind.Roll && Near(leftIn, 0.8f),
            $"cooldown inside the roll: no cut (got {inRoll}, {cooledIn}, {F(leftIn)} s left)");

        // After the roll: only while the Animator still blends out of the roll (design 2.4).
        c.Check(D(rules, rollAge: 0.1f) == MoveKind.Roll && D(rules, rollAge: 0.1f, flowOver: true) == MoveKind.None,
            "after a roll: roll attack while it still blends out of the roll, normal swing once the stand-up is over");
        var wide = MoveRules.Defaults();
        wide.Window = 1.5f;
        c.Check(D(wide, rollAge: 1f, flowOver: true) == MoveKind.None && D(wide, rollAge: 1f) == MoveKind.Roll,
            "Window 1.5, press 1 s after: normal swing after a normal roll (stand-up over), roll attack after a dash");

        c.Check(D(rules, onGround: false, jumpLive: true) == MoveKind.Jump, "airborne with a jump token: jump attack");
        c.Check(D(rules, onGround: false) == MoveKind.None, "airborne without a jump token: normal swing");
        c.Check(D(rules, onGround: true, jumpLive: true) == MoveKind.None, "jump token but on the ground: normal swing");
        c.Check(D(rules, rollAge: 0.02f) == MoveKind.Roll && D(rules, rollAge: 0.4f) == MoveKind.Roll,
            "roll ended 0.02 s and 0.4 s ago: roll attack");
        c.Check(D(rules, rollAge: 0.41f) == MoveKind.None && D(rules) == MoveKind.None,
            "roll outside the window or no roll: normal swing");
        c.Check(D(rules, onGround: false, jumpLive: true, rollAge: 0.1f) == MoveKind.Jump, "both tokens: jump wins");
        c.Check(D(rules, secondary: true, onGround: false, jumpLive: true, rollAge: 0.1f) == MoveKind.None, "secondary: none");
        c.Check(D(rules, inAttack: true, onGround: false, jumpLive: true, rollAge: 0.1f) == MoveKind.None,
            "in an attack (queued chain): none");
        c.Check(D(rules, swimming: true, onGround: false, jumpLive: true, rollAge: 0.1f) == MoveKind.None, "swimming: none");
        c.Check(D(rules, attached: true, onGround: false, jumpLive: true, rollAge: 0.1f) == MoveKind.None, "attached: none");
        c.Check(D(rules, onGround: false, jumpLive: true, gco: true) == MoveKind.None
                && D(rules, rollAge: 0.1f, gco: true) == MoveKind.Roll,
            "Goo's Combat Overhaul: no jump attack, roll attack still");
        c.Check(D(rules, onGround: false, jumpLive: true, rollAge: 0.1f, gco: true) == MoveKind.None,
            "Goo's Combat Overhaul: roll, jump, attack in the air stays a normal swing (no roll attack on its jump attack)");
        c.Check(D(rules, onGround: false, rollAge: 0.1f) == MoveKind.Roll,
            "roll that ended in the air without a jump (off a ledge): roll attack");
        var noJump = MoveRules.Defaults();
        noJump.JumpAttack = false;
        var noRoll = MoveRules.Defaults();
        noRoll.RollAttack = false;
        c.Check(D(noJump, onGround: false, jumpLive: true) == MoveKind.None && D(noJump, rollAge: 0.1f) == MoveKind.Roll,
            "jump attack off: roll attack still");
        c.Check(D(noJump, onGround: false, jumpLive: true, rollAge: 0.1f) == MoveKind.None,
            "jump attack off: roll, jump, attack in the air is a normal swing (the jump owns the air attack)");
        c.Check(D(noRoll, rollAge: 0.1f) == MoveKind.None && D(noRoll, rollTime: inside) == MoveKind.None
                && D(noRoll, onGround: false, jumpLive: true) == MoveKind.Jump,
            "roll attack off: no roll attack (after or inside the roll), jump attack still");
        c.Check(D(MoveRules.Off, onGround: false, jumpLive: true, rollAge: 0.1f) == MoveKind.None && D(null, rollAge: 0.1f) == MoveKind.None
                && D(MoveRules.Off, rollTime: inside) == MoveKind.None,
            "pending rules or none: no move");
        var kind = MoveTracker.Decide(rules, false, false, false, false, true, false, -1f, false, 0.1f, false, 0.25f,
            false, out var cooled, out var left);
        c.Check(kind == MoveKind.None && cooled == MoveKind.Roll && Near(left, 0.75f),
            $"cooldown: roll attack 0.25 s after the last move is a normal swing, 0.75 s left (got {kind}, {cooled}, {F(left)})");
        c.Check(D(rules, rollAge: 0.1f, sinceLastMove: 1f) == MoveKind.Roll, "cooldown over at exactly 1 s");
        var noCooldown = MoveRules.Defaults();
        noCooldown.Cooldown = 0f;
        c.Check(D(noCooldown, rollAge: 0.1f, sinceLastMove: 0f) == MoveKind.Roll, "cooldown 0: no limit");
    }

    private static void CheckStep(Checks c)
    {
        c.Check(MoveTriggers.StepOf("swing_longsword1", "swing_longsword", 3) == 1, "swing_longsword1 is step 1 of 3");
        c.Check(MoveTriggers.StepOf("swing_longsword2", "swing_longsword", 3) == 2, "swing_longsword2 is step 2 of 3");
        c.Check(MoveTriggers.StepOf("dualaxes2", "dualaxes", 4) == 2, "dualaxes2 is step 2 of the pair's 4");
        c.Check(MoveTriggers.StepOf("dualaxes3", "swing_axe", 3) == -1, "another family's step is not a step");
        c.Check(MoveTriggers.StepOf("knife_secondary", "knife_stab", 3) == -1, "knife_secondary is not a step");
        c.Check(MoveTriggers.StepOf("swing_longsword3", "swing_longsword", 3) == -1, "step past the chain is not a step");
        c.Check(MoveTriggers.StepOf("unarmed_attack1", "unarmed_attack", 2) == 1, "unarmed_attack1 is step 1 of 2");
        c.Check(MoveTriggers.StepOf("spear_poke", "spear_poke", 0) == -1, "single animation is not a step");
        c.Check(MoveTriggers.StepOf("swing_longsword", "swing_longsword", 3) == -1, "bare base name is not a step");
        var next = new MoveInfo { Step = 1, ChainLevels = 3 };
        var last = new MoveInfo { Step = 2, ChainLevels = 3 };
        var pair = new MoveInfo { Step = 2, ChainLevels = 4 };
        c.Check(NextStep(next) == 2 && NextStep(last) == 0 && NextStep(pair) == 3,
            "next step: 1 of 3 -> 2, 2 of 3 -> 0, 2 of 4 -> 3");
    }

    // MoveEdit on clone of sword attack; item own attack must stay untouched.
    private static void CheckEdit(Checks c, Player p)
    {
        c.Check(Near(MoveEdit.Stagger(1f, 2f), 2f) && Near(MoveEdit.Stagger(60f, 2f), 99f)
                && Near(MoveEdit.Stagger(150f, 2f), 150f) && Near(MoveEdit.Stagger(150f, 0.5f), 75f)
                && Near(MoveEdit.Stagger(1f, 0f), 0f),
            "stagger: multiplied, capped at 99, never below a weapon's own 100+");
        var drop = Prefab("SwordIron");
        if (drop == null)
        {
            c.Check(false, "SwordIron exists");
            return;
        }
        var weapon = drop.m_itemData.Clone();
        var shared = weapon.m_shared.m_attack;
        var own = new[] { shared.m_damageMultiplier, shared.m_staggerMultiplier, shared.m_forceMultiplier, shared.m_attackStamina, shared.m_maxYAngle };
        var rules = MoveRules.Defaults();
        rules.Jump.Damage = 1.5f;
        rules.Jump.Stagger = 2f;
        rules.Jump.Push = 1.25f;
        rules.Jump.Stamina = 2f;
        rules.AimAngle = 30f;
        var clone = shared.Clone();
        var applied = MoveEdit.Apply(clone, p, weapon, MoveKind.Jump, "swing_longsword2", rules);
        c.Check(applied && clone.m_attackAnimation == "swing_longsword2" && clone.m_attackChainLevels == 0
                && clone.m_attackRandomAnimations == 0,
            "jump edit: full trigger name, chain levels 0");
        c.Check(Near(clone.m_damageMultiplier, own[0] * 1.5f) && Near(clone.m_staggerMultiplier, MoveEdit.Stagger(own[1], 2f))
                && Near(clone.m_forceMultiplier, own[2] * 1.25f) && Near(clone.m_attackStamina, own[3] * 2f)
                && Near(clone.m_maxYAngle, Mathf.Max(own[4], 30f)),
            "jump edit: damage, stagger, push, stamina multiplied, aim raised");
        var roll = shared.Clone();
        MoveEdit.Apply(roll, p, weapon, MoveKind.Roll, "swing_longsword1", rules);
        c.Check(roll.m_attackAnimation == "swing_longsword1" && Near(roll.m_maxYAngle, own[4])
                && Near(roll.m_damageMultiplier, own[0] * rules.Roll.Damage),
            "roll edit: its own numbers, aim unchanged");
        c.Check(shared.m_attackAnimation == "swing_longsword" && shared.m_attackChainLevels == 3
                && Near(shared.m_damageMultiplier, own[0]) && Near(shared.m_attackStamina, own[3]) && Near(shared.m_maxYAngle, own[4]),
            "the item's own attack is untouched");
        c.Check(!MoveEdit.Apply(shared.Clone(), p, weapon, MoveKind.None, "swing_longsword2", rules)
                && !MoveEdit.Apply(shared.Clone(), p, weapon, MoveKind.Jump, null, rules),
            "no move or no animation: no edit");

        // Name a started attack fired (Dropped keep the trigger only when the new attack fired the same name).
        var chained = shared.Clone();
        chained.m_currentAttackCainLevel = 1;
        var random = shared.Clone();
        random.m_attackChainLevels = 0;
        random.m_attackRandomAnimations = 2;
        var single = shared.Clone();
        single.m_attackAnimation = "spear_poke";
        single.m_attackChainLevels = 0;
        single.m_attackRandomAnimations = 0;
        c.Check(MoveTracker.FiredTrigger(chained) == "swing_longsword1" && MoveTracker.FiredTrigger(clone) == "swing_longsword2"
                && MoveTracker.FiredTrigger(single) == "spear_poke" && MoveTracker.FiredTrigger(random) == null
                && MoveTracker.FiredTrigger(null) == null,
            $"fired trigger: chain = base + level ({MoveTracker.FiredTrigger(chained) ?? "null"}), move = its full name, "
            + "single = base, random = unknown");
    }

    // ---------- moveset.jump ----------

    private static IEnumerator RunJump()
    {
        var p = LocalPlayer(JumpName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(JumpName);
        var rig = new Rig(JumpName, p);
        try
        {
            var rules = MoveRules.Defaults();
            rules.Cooldown = 0f; // rows come fast one after other; cooldown have own test
            rules.Jump.Damage = 1.5f;
            rules.Jump.Stagger = 2.5f;
            rules.Jump.Push = 1.25f;
            rules.Jump.Stamina = 1.5f;
            ServerRules.TestRules = rules;
            var idle = new Box<bool>();
            yield return WaitIdle(p, 5f, idle);
            c.Check(idle.Value, "player idle at the start");
            var dummy = rig.SpawnDummy();
            c.Check(dummy != null, "training dummy spawned");
            SelfTest.Note(JumpName, $"gravity {F(Physics.gravity.y)} m/s2, physics tick {S(Time.fixedDeltaTime)} s, "
                                    + $"jump force {F(p.m_jumpForce)}, attack towards look direction {p.AttackTowardsPlayerLookDir}");
            foreach (var row in Rows)
            {
                yield return JumpRow(rig, c, row, rules, dummy);
            }
            c.Check(DummyInert(dummy), "the dummy's AI stayed off and the dummy never attacked during the whole test");
            if (dummy != null && dummy.Ai != null)
            {
                var target = dummy.Ai.GetTargetCreature();
                var who = target == null ? "none" : ReferenceEquals(target, p) ? "the player" : target.name;
                SelfTest.Note(JumpName, $"dummy target after the hits: {who} (vanilla MonsterAI.OnDamaged sets it on "
                                        + "every hit even with the AI off; harmless while the AI is off)");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    private static IEnumerator JumpRow(Rig rig, Checks c, Row row, MoveRules rules, Dummy dummy)
    {
        var p = rig.P;
        var held = new Box<ItemDrop.ItemData>();
        yield return Equip(rig, row, held);
        var weapon = held.Value;
        if (weapon == null)
        {
            c.Check(false, $"{row.Label}: could not equip");
            yield break;
        }
        Put(p, rig.Home);
        Face(p, rig.Forward);
        rig.PlaceDummy(dummy);
        yield return Fixed;
        yield return Fixed;
        var shared = weapon.m_shared.m_attack;
        var trigger = MoveTriggers.Default(MoveKind.Jump, row.Family);
        var on = trigger != MoveTriggers.Off;
        var dt = Time.fixedDeltaTime;
        c.Check(DummyInert(dummy), $"{row.Label}: the dummy's AI is still off and the dummy never attacked");
        c.Check(!p.IsKnockedBack() && !p.IsStaggering(), $"{row.Label}: player free before the press");

        var mark = LogTap.Mark;
        var before = MoveTracker.LastMove.Clone;
        var prev = p.m_currentAttack;
        var health = dummy != null ? dummy.Body.GetHealth() : 0f;
        var y0 = p.transform.position.y;
        p.Jump();
        var jumpAt = Time.fixedTime;
        c.Check(p.m_jumpTimer == 0f && MoveTracker.JumpLive, $"{row.Label}: jump taken, token live");
        yield return WaitTicks(0.1f);
        Press(p);
        var pressAt = Time.fixedTime;

        Attack started = null;
        Attack clone = null;
        var move = default(MoveInfo);
        float startAt = -1f, landAt = -1f, hitAt = -1f, delay = -1f;
        float aimAir = float.NaN, aimLanded = float.NaN;
        var apex = y0;
        bool startAir = false, hitAir = false, entered = false, entryOk = false;
        string clip = null;
        while (Time.fixedTime - pressAt < 2.5f)
        {
            yield return Fixed;
            var now = Time.fixedTime;
            apex = Mathf.Max(apex, p.transform.position.y);
            if (landAt < 0f && now - jumpAt >= MoveTracker.JumpGroundLock && p.IsOnGround())
            {
                landAt = now;
            }
            var current = p.m_currentAttack;
            if (started == null && current != null && !ReferenceEquals(current, prev))
            {
                started = current;
                startAt = now;
                startAir = !p.IsOnGround();
            }
            if (clone == null && !ReferenceEquals(MoveTracker.LastMove.Clone, before))
            {
                move = MoveTracker.LastMove;
                clone = move.Clone;
            }
            if (clone != null)
            {
                if (!entered && MoveTracker.LastEntryDelay >= 0f && ReferenceEquals(MoveTracker.LastMove.Clone, clone))
                {
                    entered = true;
                    delay = MoveTracker.LastEntryDelay;
                    clip = Clips(p.m_animator);
                    var next = NextStep(move);
                    entryOk = next < 0 || (clone.m_attackAnimation == move.BaseName && clone.m_nextAttackChainLevel == next);
                }
                if (landAt < 0f && float.IsNaN(aimAir) && !p.IsOnGround())
                {
                    aimAir = clone.m_maxYAngle;
                }
                if (landAt >= 0f && float.IsNaN(aimLanded) && now >= landAt + 1.5f * dt)
                {
                    aimLanded = clone.m_maxYAngle;
                }
            }
            if (dummy != null && hitAt < 0f && dummy.Body.GetHealth() < health - 0.01f)
            {
                hitAt = now - pressAt;
                hitAir = !p.IsOnGround();
            }
            if (landAt >= 0f && now - landAt >= 0.1f && started != null
                && (started.m_attackDone || !ReferenceEquals(p.m_currentAttack, started)))
            {
                break;
            }
        }

        c.Check(started != null && startAt - pressAt <= 2f * dt + 0.001f,
            $"{row.Label}: the press started an attack on the next tick (no press eaten)");
        if (on)
        {
            c.Check(clone != null, $"{row.Label}: a jump attack started");
            if (clone != null)
            {
                c.Check(ReferenceEquals(clone, started) && move.Kind == MoveKind.Jump && move.Trigger == trigger
                        && move.Family == row.Family && ReferenceEquals(move.Weapon, weapon),
                    $"{row.Label}: the move is the jump attack {trigger} (got {move.Kind} {move.Trigger})");
                c.Check(move.BaseName == shared.m_attackAnimation && move.ChainLevels == shared.m_attackChainLevels
                        && Near(move.MaxYBefore, shared.m_maxYAngle),
                    $"{row.Label}: chain and aim saved as they arrived");
                c.Check(clone.m_attackChainLevels == 0 && clone.m_attackRandomAnimations == 0,
                    $"{row.Label}: the move's clone has chain levels 0");
                c.Check(Near(clone.m_damageMultiplier, shared.m_damageMultiplier * rules.Jump.Damage)
                        && Near(clone.m_staggerMultiplier, MoveEdit.Stagger(shared.m_staggerMultiplier, rules.Jump.Stagger))
                        && Near(clone.m_forceMultiplier, shared.m_forceMultiplier * rules.Jump.Push)
                        && Near(clone.m_attackStamina, shared.m_attackStamina * rules.Jump.Stamina),
                    $"{row.Label}: the jump attack's multipliers are on the clone");
                c.Check(startAir, $"{row.Label}: the jump attack started in the air");
                c.Check(entered && delay <= MoveTracker.StartDeadline + dt,
                    $"{row.Label}: {trigger} entered its animation within 0.5 s (delay {S(delay)}, start failures {MoveTracker.StartFailures})");
                c.Check(!entered || entryOk,
                    $"{row.Label}: after entry the clone is {move.BaseName} with next step {NextStep(move)} (is {clone.m_attackAnimation}, {clone.m_nextAttackChainLevel})");
                if (move.AimEdited && !float.IsNaN(aimAir))
                {
                    c.Check(Near(aimAir, Mathf.Max(move.MaxYBefore, rules.AimAngle)), $"{row.Label}: aim {F(aimAir)} in the air");
                }
                c.Check(!float.IsNaN(aimLanded) && Near(aimLanded, move.MaxYBefore),
                    $"{row.Label}: aim back to {F(move.MaxYBefore)} after landing (is {F(aimLanded)})");
            }
        }
        else
        {
            c.Check(clone == null, $"{row.Label}: jump attack Off, no move");
            c.Check(started != null && started.m_attackAnimation == shared.m_attackAnimation
                    && Near(started.m_damageMultiplier, shared.m_damageMultiplier),
                $"{row.Label}: Off = the normal swing {shared.m_attackAnimation}");
        }
        c.Check(!MoveTracker.JumpLive, $"{row.Label}: the jump token is used up");
        SelfTest.Note(JumpName, $"{row.Label}: {(on ? trigger : "Off")}; airtime {S(landAt - jumpAt)} s, apex "
                                + $"{F(apex - y0)} m; attack start {S(startAt - jumpAt)} s after the jump; entry after {S(delay)} s"
                                + $" into {clip ?? "n/a"}; dummy hit {S(hitAt)} s after the press "
                                + (hitAt < 0f
                                    ? $"(no hit seen: reach {F(shared.m_attackRange)} m at {F(shared.m_attackHeight)} m over the feet, dummy {F(DummyDistance)} m ahead)"
                                    : hitAir ? "(player still airborne)" : "(after landing)"));

        // On ground and calm: next press = normal swing at step 0.
        yield return WaitIdle(p, 4f);
        if (on && clone != null)
        {
            var box = new Box<Attack>();
            var last = p.m_currentAttack;
            Press(p);
            yield return WaitNewAttack(p, last, 0.6f, box);
            var normal = box.Value;
            c.Check(normal != null && ReferenceEquals(MoveTracker.LastMove.Clone, clone)
                    && normal.m_attackAnimation == shared.m_attackAnimation && normal.m_currentAttackCainLevel == 0,
                $"{row.Label}: a press on the ground after it is a normal {shared.m_attackAnimation} at step 0 "
                + $"(got {(normal != null ? normal.m_attackAnimation + normal.m_currentAttackCainLevel : "no attack")})");
            yield return WaitIdle(p, 4f);
        }
        // Debug lines (TESTING T02): one "Jump attack: <item> (<type>) plays <animation>" for a move and its "started
        // after" line, none for an Off type (also not from the ground press after); never a "did not start" warning.
        if (on)
        {
            var head = $"Jump attack: {MoveEdit.ItemName(weapon)} ({Families.Key(row.Family)}) plays {trigger};";
            c.Check(LogTap.Count(mark, Dbg, "Jump attack: ") == 1 && LogTap.Count(mark, Dbg, head) == 1,
                $"{row.Label}: exactly one Debug line \"{head} ...\" ({LogTap.Count(mark, Dbg, "Jump attack: ")} jump attack lines)");
            var entry = LogTap.First(mark, Dbg, $"Jump attack {trigger} started after ");
            c.Check(clone == null || !entered || (entry != null && entry.EndsWith(EntryTail(move), StringComparison.Ordinal)),
                $"{row.Label}: Debug line \"Jump attack {trigger} started after ...; {EntryTail(move)}\" (got \"{entry ?? "none"}\")");
        }
        else
        {
            c.Check(LogTap.Count(mark, Dbg, "Jump attack") == 0, $"{row.Label}: no \"Jump attack\" Debug line for an Off weapon type");
        }
        c.Check(LogTap.Count(mark, Wrn, "did not start") == 0, $"{row.Label}: no \"did not start\" warning");
        rig.TakeBack(weapon);
    }

    // ---------- moveset.jump-input ----------

    private static IEnumerator RunJumpInput()
    {
        var p = LocalPlayer(JumpInputName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(JumpInputName);
        var rig = new Rig(JumpInputName, p);
        try
        {
            var rules = MoveRules.Defaults();
            rules.Cooldown = 0f;
            ServerRules.TestRules = rules;
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            var weapon = held.Value;
            if (weapon == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var shared = weapon.m_shared.m_attack;
            var box = new Box<Attack>();

            // 1. Button held at 20 FPS from jump on: first attack = jump attack, never replaced.
            rig.SetFps(20);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return WaitIdle(p, 4f);
            Put(p, rig.Home);
            Face(p, rig.Forward);
            yield return Fixed;
            var before = MoveTracker.LastMove.Clone;
            var resets = MoveTracker.TriggerResets;
            var failures = MoveTracker.StartFailures;
            var refused = MoveTracker.RefusedRestarts;
            rig.TakeController();
            p.Jump();
            Hold(p, true);
            var frames = Time.frameCount;
            var ticks = 0;
            Attack clone = null;
            var entered = false;
            var currentAtEntry = false;
            var t0 = Time.fixedTime;
            while (Time.fixedTime - t0 < 2f)
            {
                yield return Fixed;
                ticks++;
                Hold(p, false);
                if (clone == null && !ReferenceEquals(MoveTracker.LastMove.Clone, before))
                {
                    clone = MoveTracker.LastMove.Clone;
                }
                if (clone != null && MoveTracker.LastEntryDelay >= 0f && ReferenceEquals(MoveTracker.LastMove.Clone, clone))
                {
                    entered = true;
                    currentAtEntry = ReferenceEquals(p.m_currentAttack, clone);
                    break;
                }
                if (clone != null && !MoveTracker.Watching)
                {
                    break;
                }
            }
            rig.GiveController();
            var perFrame = ticks / (float)Mathf.Max(1, Time.frameCount - frames);
            rig.RestoreFps();
            c.Check(clone != null && MoveTracker.LastMove.Kind == MoveKind.Jump && ReferenceEquals(MoveTracker.LastMove.Clone, clone),
                "held button at 20 FPS: the first attack of the jump is the jump attack");
            c.Check(entered && currentAtEntry, "held button at 20 FPS: the jump attack entered its animation and was never replaced");
            c.Check(MoveTracker.TriggerResets == resets && MoveTracker.StartFailures == failures,
                "held button at 20 FPS: no trigger reset, no start failure");
            SelfTest.Note(JumpInputName, $"held button at 20 FPS: {F(perFrame)} physics ticks per frame, "
                                         + $"{MoveTracker.RefusedRestarts - refused} restarts refused, entry after {S(MoveTracker.LastEntryDelay)} s");
            yield return WaitLanded(p, 3f);
            yield return WaitIdle(p, 4f);

            // 2. Jump, land without attacking, then press: no move.
            var quiet = LogTap.Mark;
            Put(p, rig.Home);
            Face(p, rig.Forward);
            yield return Fixed;
            before = MoveTracker.LastMove.Clone;
            p.Jump();
            var jumpAt = Time.fixedTime;
            yield return WaitTicks(0.15f);
            yield return WaitLanded(p, 3f);
            var airtime = Time.fixedTime - jumpAt;
            yield return WaitTicks(0.1f);
            c.Check(!MoveTracker.JumpLive, "the jump token ends on landing");
            var last = p.m_currentAttack;
            Press(p);
            yield return WaitNewAttack(p, last, 0.6f, box);
            c.Check(box.Value != null && ReferenceEquals(MoveTracker.LastMove.Clone, before)
                    && box.Value.m_attackAnimation == shared.m_attackAnimation,
                "jump, land, then press: a normal swing");
            SelfTest.Note(JumpInputName, $"airtime of a standing jump: {S(airtime)} s");
            yield return WaitIdle(p, 4f);

            // 3. Me lift player 5 m (no jump), fall past 0.2 s ground grace, press in the air: no move.
            Put(p, rig.Home + Vector3.up * 5f);
            Face(p, rig.Forward);
            yield return WaitTicks(0.3f);
            c.Check(!p.IsOnGround() && !MoveTracker.JumpLive, "falling without a jump: airborne, no jump token");
            before = MoveTracker.LastMove.Clone;
            last = p.m_currentAttack;
            Press(p);
            yield return WaitNewAttack(p, last, 0.6f, box);
            c.Check(box.Value != null && ReferenceEquals(MoveTracker.LastMove.Clone, before)
                    && box.Value.m_attackAnimation == shared.m_attackAnimation,
                "falling without a jump, press in the air: a normal swing");
            c.Check(LogTap.Count(quiet, Dbg, "Jump attack") == 0,
                "jump, land, press and falling without a jump: no \"Jump attack\" Debug line (TESTING T04)");
            yield return WaitLanded(p, 4f);
            yield return WaitIdle(p, 4f);

            // 4. Jump in restart gap (ground swing started, Animator not in attack yet): no jump token.
            Put(p, rig.Home);
            Face(p, rig.Forward);
            yield return Fixed;
            before = MoveTracker.LastMove.Clone;
            last = p.m_currentAttack;
            Press(p);
            yield return WaitNewAttack(p, last, 0.5f, box);
            if (box.Value == null)
            {
                c.Check(false, "restart gap: the ground swing started");
            }
            else if (!p.InAttack() && !box.Value.m_wasInAttack)
            {
                p.Jump();
                var jumped = p.m_jumpTimer == 0f;
                c.Check(!MoveTracker.JumpLive, "a jump taken while an attack is starting gives no jump token");
                SelfTest.Note(JumpInputName, $"restart gap: the game {(jumped ? "took" : "refused")} the jump while "
                                             + $"{box.Value.m_attackAnimation} was starting");
            }
            else
            {
                SelfTest.Note(JumpInputName, "restart gap: the swing was already in its animation after its first tick; case not exercised");
            }
            c.Check(ReferenceEquals(MoveTracker.LastMove.Clone, before), "restart gap: no move");
            yield return WaitLanded(p, 3f);
            yield return WaitIdle(p, 4f);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- moveset.roll ----------

    private static IEnumerator RunRoll()
    {
        var p = LocalPlayer(RollName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(RollName);
        var rig = new Rig(RollName, p);
        try
        {
            var rules = MoveRules.Defaults();
            rules.Cooldown = 0f;
            rules.Roll.Damage = 1.4f;
            rules.Roll.Stagger = 1.75f;
            rules.Roll.Push = 1.2f;
            rules.Roll.Stamina = 1.25f;
            ServerRules.TestRules = rules;
            SelfTest.Note(RollName, $"attack towards look direction {p.AttackTowardsPlayerLookDir}; roll direction {rig.Forward}");
            // Every row's first roll attack as the first of a game session: no state learned from a swing, none probed.
            RollFlow.ForgetAll();
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in Rows)
            {
                yield return RollRow(rig, c, row, rules, used);
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // used: roll animations already played in this test (the first use must take its state from the probe).
    private static IEnumerator RollRow(Rig rig, Checks c, Row row, MoveRules rules, HashSet<string> used)
    {
        var p = rig.P;
        var held = new Box<ItemDrop.ItemData>();
        yield return Equip(rig, row, held);
        var weapon = held.Value;
        if (weapon == null)
        {
            c.Check(false, $"{row.Label}: could not equip");
            yield break;
        }
        Put(p, rig.Home);
        Face(p, rig.Forward);
        yield return Fixed;
        yield return Fixed;
        var shared = weapon.m_shared.m_attack;
        var trigger = MoveTriggers.Default(MoveKind.Roll, row.Family);
        var on = trigger != MoveTriggers.Off;
        var dt = Time.fixedDeltaTime;

        var mark = LogTap.Mark;
        var run = new RollRun();
        var fades = RollFlow.CrossFades;
        yield return Roll(rig, run);
        var move = run.Move;
        var clone = run.Clone;
        var started = run.Started;
        var entered = run.EnteredAt >= 0f;
        var fadedInto = RollFlow.CrossFades > fades ? RollFlow.LastHash : 0; // state the cut cross-faded into
        var firstUse = on && used.Add(trigger);

        c.Check(run.RollStart >= 0f && run.RollEnd >= 0f, $"{row.Label}: the roll started and ended");
        c.Check(run.IframesEnd >= 0f && (started == null || run.IframesEnd <= run.StartAt) && !run.InvulnAfterStart,
            $"{row.Label}: the roll's i-frames ended before the attack started and never came back (i-frames {Span(run.RollStart, run.IframesEnd)} s, attack {S(run.Cut)} s)");
        if (on)
        {
            // Cut point: FlowStart after roll start, or IframeMargin after the tick after the i-frames end if later.
            var iframes = run.IframesEnd >= 0f ? run.IframesEnd - run.RollStart + dt : 0f;
            var expectCut = Mathf.Max(rules.FlowStart, iframes + MoveTracker.IframeMargin);
            c.Check(started != null && run.StartedInRoll && Mathf.Abs(run.Cut - expectCut) <= 1.5f * dt,
                $"{row.Label}: the attack started inside the roll at the cut point ({S(run.Cut)} s, expected {S(expectCut)} s)");
            c.Check(clone != null, $"{row.Label}: a roll attack started");
            if (clone != null)
            {
                c.Check(ReferenceEquals(clone, started) && move.Kind == MoveKind.Roll && move.Trigger == trigger
                        && move.Family == row.Family && ReferenceEquals(move.Weapon, weapon) && move.Cut
                        && Mathf.Abs(move.RollTime - run.Cut) <= 0.5f * dt,
                    $"{row.Label}: the move is the roll attack {trigger} cut into the roll {S(move.RollTime)} s after it started (got {move.Kind} {move.Trigger}, cut {move.Cut})");
                c.Check(clone.m_attackChainLevels == 0
                        && Near(clone.m_damageMultiplier, shared.m_damageMultiplier * rules.Roll.Damage)
                        && Near(clone.m_staggerMultiplier, MoveEdit.Stagger(shared.m_staggerMultiplier, rules.Roll.Stagger))
                        && Near(clone.m_forceMultiplier, shared.m_forceMultiplier * rules.Roll.Push)
                        && Near(clone.m_attackStamina, shared.m_attackStamina * rules.Roll.Stamina)
                        && Near(clone.m_maxYAngle, shared.m_maxYAngle),
                    $"{row.Label}: chain levels 0 and the roll attack's multipliers on the clone, aim unchanged");
                c.Check(move.Flow == FlowResult.CrossFade && move.Source != StateSource.None,
                    $"{row.Label}: cross-faded out of the roll (flow {move.Flow}, state {move.Source})");
                if (firstUse)
                {
                    // Nothing learned and nothing probed before this roll: only the controller probe can know it.
                    c.Check(move.Source == StateSource.Probed,
                        $"{row.Label}: first roll attack with {trigger} since the states were forgotten: its state came from the animation controller (state {move.Source})");
                }
                if (move.Flow == FlowResult.CrossFade)
                {
                    c.Check(entered && fadedInto != 0 && run.EntryFull == fadedInto,
                        $"{row.Label}: the Animator entered the state the roll flow cross-faded into ({fadedInto}, entered {run.EntryFull})");
                }
                c.Check(run.RollEnd >= 0f && Mathf.Abs(run.RollEnd - run.StartAt - dt) < 0.5f * dt,
                    $"{row.Label}: the roll ended on the tick after the cut ({Span(run.StartAt, run.RollEnd)} s)");
                c.Check(entered && run.Delay >= 0f && run.Delay <= 2f * dt + 0.001f,
                    $"{row.Label}: {trigger} entered its animation within two ticks (delay {S(run.Delay)})");
                c.Check(run.GapTicks == 0,
                    $"{row.Label}: no idle or locomotion between the roll and the attack (gap {S(run.Gap)} s)");
                var clipWant = RollFlow.ClipOf(trigger);
                c.Check(clipWant == null || run.Clip == clipWant,
                    $"{row.Label}: the attack state plays the trigger's own clip {clipWant ?? "n/a"} (got {run.Clip ?? "none"})");
                var next = NextStep(move);
                c.Check(!entered || next < 0 || (clone.m_attackAnimation == move.BaseName && clone.m_nextAttackChainLevel == next),
                    $"{row.Label}: after entry the clone is {move.BaseName} with next step {next} (is {clone.m_attackAnimation}, {clone.m_nextAttackChainLevel})");
                c.Check(run.Refusals == 0, $"{row.Label}: nothing refused inside the roll ({run.Refusals})");
                // Debug lines (TESTING T06, T07, T10 c, T23): the move line with where it cut in and how it left the
                // roll, the entry line with what the combo does next, and on a first use the controller probe's
                // line, logged when the roll started (not on the cut tick).
                var head = $"Roll attack: {MoveEdit.ItemName(weapon)} ({Families.Key(row.Family)}) plays {trigger}; ";
                var line = LogTap.First(mark, Dbg, "Roll attack: ");
                c.Check(LogTap.Count(mark, Dbg, "Roll attack: ") == 1 && line != null && line.StartsWith(head, StringComparison.Ordinal)
                        && line.Contains("; cut into the roll ") && line.Contains(" s after it started (cross-fade from the roll, "),
                    $"{row.Label}: one Debug line \"{head}...; cut into the roll ... (cross-fade from the roll, ...)\" (got \"{line ?? "none"}\")");
                var entry = LogTap.First(mark, Dbg, $"Roll attack {trigger} started after ");
                c.Check(!entered || (entry != null && entry.EndsWith(EntryTail(move), StringComparison.Ordinal)),
                    $"{row.Label}: Debug line \"Roll attack {trigger} started after ...; {EntryTail(move)}\" (got \"{entry ?? "none"}\")");
                if (firstUse)
                {
                    var found = LogTap.Since(mark, Dbg, $"Found the animation state of {trigger} in the animation controller (");
                    c.Check(found.Count == 1 && found[0].At <= run.RollStart + 1.5f * dt,
                        $"{row.Label}: first use: Debug \"Found the animation state of {trigger} in the animation controller\" once, when the roll started ({found.Count} lines)");
                    c.Check(line != null && line.Contains("state from the animation controller"),
                        $"{row.Label}: first use: the move line says \"state from the animation controller\"");
                }
            }
        }
        else
        {
            c.Check(run.GapTicks > 0,
                $"{row.Label}: the usual stand-up between the roll and the swing stays (gap {S(run.Gap)} s)");
            c.Check(LogTap.Count(mark, Dbg, "Roll attack") == 0, $"{row.Label}: no \"Roll attack\" Debug line for an Off weapon type");
            c.Check(clone == null, $"{row.Label}: roll attack Off, no move");
            c.Check(started != null && !run.StartedInRoll && run.RollEnd >= 0f && Mathf.Abs(run.StartAt - run.RollEnd - dt) < 0.5f * dt,
                $"{row.Label}: no attack inside the roll; the normal swing started on the tick after the roll ended ({Span(run.RollEnd, run.StartAt)} s)");
            c.Check(run.Refusals == 0 && run.Skips > 0,
                $"{row.Label}: the roll gate stayed shut for the press inside the roll (no clone made there; {run.Skips} ticks, {run.Refusals} refused)");
            c.Check(started != null && started.m_attackAnimation == shared.m_attackAnimation
                    && Near(started.m_damageMultiplier, shared.m_damageMultiplier),
                $"{row.Label}: Off = the normal swing {shared.m_attackAnimation}");
        }
        c.Check(float.IsNegativeInfinity(MoveTracker.RollEndAt) && !MoveTracker.RollActive, $"{row.Label}: the roll token is used up");

        // Combo go on: me keep pressing during move; next attack = chain next step.
        var nextText = "n/a";
        if (on && entered && clone != null)
        {
            var expect = NextStep(move);
            Attack next = null;
            var from = Time.fixedTime;
            while (Time.fixedTime - from < 3f)
            {
                Press(p);
                yield return Fixed;
                var current = p.m_currentAttack;
                if (current != null && !ReferenceEquals(current, clone))
                {
                    next = current;
                    break;
                }
            }
            p.m_queuedAttackTimer = 0f;
            c.Check(next != null, $"{row.Label}: a press during the roll attack starts the next attack");
            if (next != null)
            {
                c.Check(ReferenceEquals(MoveTracker.LastMove.Clone, clone), $"{row.Label}: the next attack is not a move");
                if (expect >= 0)
                {
                    c.Check(next.m_attackAnimation == move.BaseName && next.m_currentAttackCainLevel == expect,
                        $"{row.Label}: the combo continues with {move.BaseName}{expect} (fired {next.m_attackAnimation}{next.m_currentAttackCainLevel})");
                }
                var nextEntered = false;
                var until = Time.fixedTime + 1f;
                while (!nextEntered && Time.fixedTime < until)
                {
                    nextEntered = next.m_wasInAttack;
                    if (!nextEntered)
                    {
                        yield return Fixed;
                    }
                }
                c.Check(nextEntered, $"{row.Label}: the next step {next.m_attackAnimation}{next.m_currentAttackCainLevel} entered its animation");
                nextText = $"{next.m_attackAnimation}{next.m_currentAttackCainLevel} {(nextEntered ? "played" : "did not play")} "
                           + $"{S(Time.fixedTime - from)} s after the first extra press";
            }
        }
        var iframesText = run.RollStart < 0f ? "n/a" : run.IframesEnd < 0f ? "whole roll" : Span(run.RollStart, run.IframesEnd) + " s";
        var flowText = on
            ? $"cut at {S(run.Cut)} s (dodge state at {(run.CutNorm >= 0f ? run.CutNorm.ToString("0.00", CultureInfo.InvariantCulture) : "n/a")} "
              + $"of its clip), {move.Flow} ({move.Source}), gap {S(run.Gap)} s, entry after {S(run.Delay)} s into {run.Clip ?? "n/a"}"
            : $"vanilla: attack {Span(run.RollEnd, run.StartAt)} s after the roll ended, gap {S(run.Gap)} s (the stand-up to idle), "
              + $"in its state {Span(run.StartAt, run.EnteredAt)} s after its start, clip {run.Clip ?? "n/a"}";
        SelfTest.Note(RollName, $"{row.Label}: {(on ? trigger : "Off")}; press to roll {Span(run.DodgeAt, run.RollStart)} s, roll "
                                + $"(m_inDodge) {Span(run.RollStart, run.RollEnd)} s, i-frames {iframesText}; {flowText}; next: {nextText}");
        var bad = LogTap.Since(mark, Wrn | Err);
        c.Check(bad.Count == 0,
            $"{row.Label}: no warning and no error from the mod during the row ({bad.Count}{(bad.Count > 0 ? ", first: " + bad[0].Text : "")})");
        yield return WaitIdle(p, 4f);
        rig.TakeBack(weapon);
    }

    // ---------- moveset.roll-input ----------

    private static IEnumerator RunRollInput()
    {
        var p = LocalPlayer(RollInputName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(RollInputName);
        var rig = new Rig(RollInputName, p);
        try
        {
            var rules = MoveRules.Defaults();
            rules.Cooldown = 0f;
            ServerRules.TestRules = rules;
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            var weapon = held.Value;
            if (weapon == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var shared = weapon.m_shared.m_attack;
            yield return RollHold(rig, c, rules, 50);
            yield return RollHold(rig, c, rules, 20);
            yield return DoubleStart(rig, c, rules, true);
            yield return DoubleStart(rig, c, rules, false);
            yield return Window(rig, c, rules, 0.06f, WindowWant.Flow, shared);
            yield return Window(rig, c, rules, 0.2f, WindowWant.FlowOrSwing, shared);
            yield return Window(rig, c, rules, 0.3f, WindowWant.FlowOrSwing, shared);
            // TESTING T22 as written: "about a third of a second after the roll ends, once the character has stood up"
            // = a normal swing (first run: the Animator was out of the roll's blend from 0.2 s on).
            yield return Window(rig, c, rules, 0.34f, WindowWant.Swing, shared);
            yield return Window(rig, c, rules, rules.Window + 0.2f, WindowWant.Swing, shared);
            yield return Window(rig, c, rules, 1f, WindowWant.Swing, shared); // TESTING T08, first half as written
            // A long Window never brings back "roll, stand-up, roll attack": after a normal roll it must flow.
            var wide = MoveRules.Defaults();
            wide.Cooldown = 0f;
            wide.Window = 1.5f;
            ServerRules.TestRules = wide;
            yield return Window(rig, c, wide, 1f, WindowWant.Swing, shared);
            ServerRules.TestRules = rules;
            yield return EarlyCut(rig, c);
            ServerRules.TestRules = rules;
            yield return Learned(rig, c, rules);
            yield return RemoteView(rig, c, rules);
            yield return Cooldown(rig, c, shared);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // Attack held through the roll and after (at fps): roll attack cut into the roll at FlowStart, never replaced.
    private static IEnumerator RollHold(Rig rig, Checks c, MoveRules rules, int fps)
    {
        var p = rig.P;
        var dt = Time.fixedDeltaTime;
        rig.SetFps(fps);
        yield return new WaitForSecondsRealtime(0.5f);
        yield return WaitIdle(p, 4f);
        Put(p, rig.Home);
        Face(p, rig.Forward);
        yield return Fixed;
        var before = MoveTracker.LastMove.Clone;
        var resets = MoveTracker.TriggerResets;
        var failures = MoveTracker.StartFailures;
        var refused = MoveTracker.RefusedRestarts;
        rig.TakeController();
        p.Dodge(rig.Forward);
        var holding = false;
        float rollStart = -1f, rollEnd = -1f, startAt = -1f;
        Attack clone = null;
        bool entered = false, currentAtEntry = false, inRoll = false;
        var frames = Time.frameCount;
        var ticks = 0;
        var t0 = Time.fixedTime;
        while (Time.fixedTime - t0 < 4f)
        {
            yield return Fixed;
            ticks++;
            var now = Time.fixedTime;
            if (!holding && p.m_inDodge)
            {
                holding = true;
                rollStart = now;
                Hold(p, true); // pressed during the roll, then held
            }
            else if (holding)
            {
                Hold(p, false);
            }
            if (holding && rollEnd < 0f && !p.m_inDodge)
            {
                rollEnd = now;
            }
            if (clone == null && !ReferenceEquals(MoveTracker.LastMove.Clone, before))
            {
                clone = MoveTracker.LastMove.Clone;
                startAt = now;
                inRoll = p.m_inDodge;
            }
            if (clone != null && MoveTracker.LastEntryDelay >= 0f && ReferenceEquals(MoveTracker.LastMove.Clone, clone))
            {
                entered = true;
                currentAtEntry = ReferenceEquals(p.m_currentAttack, clone);
                break;
            }
            if (clone != null && !MoveTracker.Watching)
            {
                break;
            }
        }
        rig.GiveController();
        var perFrame = ticks / (float)Mathf.Max(1, Time.frameCount - frames);
        rig.RestoreFps();
        var cut = startAt >= 0f && rollStart >= 0f ? startAt - rollStart : -1f;
        c.Check(clone != null && MoveTracker.LastMove.Kind == MoveKind.Roll && MoveTracker.LastMove.Cut && inRoll
                && Mathf.Abs(cut - rules.FlowStart) <= 1.5f * dt,
            $"held button at {fps} FPS: the roll attack cut into the roll at FlowStart ({S(cut)} s)");
        c.Check(entered && currentAtEntry, $"held button at {fps} FPS: the roll attack entered its animation and was never replaced");
        c.Check(MoveTracker.TriggerResets == resets && MoveTracker.StartFailures == failures,
            $"held button at {fps} FPS: never dropped or cancelled");
        SelfTest.Note(RollInputName, $"held button at {fps} FPS: {F(perFrame)} physics ticks per frame, cut at {S(cut)} s, "
                                     + $"roll over {Span(startAt, rollEnd)} s after the cut, {MoveTracker.RefusedRestarts - refused} "
                                     + $"restarts refused, entry after {S(MoveTracker.LastEntryDelay)} s, flow {MoveTracker.LastMove.Flow}");
        yield return WaitIdle(p, 4f);
    }

    // Two StartAttack in one tick: at the cut point inside the roll, or right after the roll's end. Second refused,
    // move play.
    private static IEnumerator DoubleStart(Rig rig, Checks c, MoveRules rules, bool inside)
    {
        var p = rig.P;
        var what = inside ? "double start inside the roll" : "double start after the roll";
        yield return WaitIdle(p, 4f);
        Put(p, rig.Home);
        Face(p, rig.Forward);
        yield return Fixed;
        var before = MoveTracker.LastMove.Clone;
        var refused = MoveTracker.RefusedRestarts;
        if (inside)
        {
            p.Dodge(rig.Forward);
            var ready = false;
            var t0 = Time.fixedTime;
            while (Time.fixedTime - t0 < 3f)
            {
                yield return Fixed;
                if (p.m_inDodge && MoveTracker.RollActive && !p.m_dodgeInvincible
                    && Time.fixedTime - MoveTracker.RollStartAt + 0.001f >= rules.FlowStart)
                {
                    ready = true;
                    break;
                }
            }
            if (!ready)
            {
                c.Check(false, $"{what}: the roll reached the cut point");
                yield break;
            }
        }
        else
        {
            var edge = new Box<float>();
            p.Dodge(rig.Forward);
            yield return WaitRollEdge(p, 4f, false, edge);
            if (edge.Value < 0f)
            {
                c.Check(false, $"{what}: the roll ended");
                yield break;
            }
        }
        var first = p.StartAttack(null, false);
        var second = p.StartAttack(null, false);
        var clone = MoveTracker.LastMove.Clone;
        c.Check(first && !second, $"{what}: first StartAttack {first}, second {second} (expected true, false)");
        c.Check(!ReferenceEquals(clone, before) && MoveTracker.LastMove.Kind == MoveKind.Roll && MoveTracker.LastMove.Cut == inside
                && ReferenceEquals(p.m_currentAttack, clone) && MoveTracker.RefusedRestarts == refused + 1,
            $"{what}: the roll attack is current and the restart was refused");
        c.Check(MoveTracker.LastMove.Flow == FlowResult.CrossFade,
            $"{what}: cross-faded out of the roll (flow {MoveTracker.LastMove.Flow})");
        c.Check(!inside || p.m_inDodge, $"{what}: m_inDodge put back after the call (the roll gate closed)");
        var entered = false;
        var currentAtEntry = false;
        var until = Time.fixedTime + 1f;
        while (Time.fixedTime < until)
        {
            yield return Fixed;
            if (MoveTracker.LastEntryDelay >= 0f && ReferenceEquals(MoveTracker.LastMove.Clone, clone))
            {
                entered = MoveTracker.LastEntryDelay <= 3f * Time.fixedDeltaTime + 0.001f;
                currentAtEntry = ReferenceEquals(p.m_currentAttack, clone);
                break;
            }
        }
        c.Check(entered && currentAtEntry,
            $"{what}: the roll attack entered its animation within three ticks ({S(MoveTracker.LastEntryDelay)} s)");
        yield return WaitIdle(p, 4f);
    }

    // What a press some time after a roll must give (design 2.4).
    private enum WindowWant
    {
        Flow,        // roll attack cross-faded out of the roll's blend (Animator still in it)
        FlowOrSwing, // roll attack only if it cross-faded out of the roll, else a normal swing: never roll, stand-up, move
        Swing,       // normal swing
    }

    // Press "after" seconds past roll end (no press during the roll).
    private static IEnumerator Window(Rig rig, Checks c, MoveRules rules, float after, WindowWant want, Attack shared)
    {
        var p = rig.P;
        yield return WaitIdle(p, 4f);
        Put(p, rig.Home);
        Face(p, rig.Forward);
        yield return Fixed;
        var animator = p.m_zanim.m_animator;
        var mark = LogTap.Mark;
        var before = MoveTracker.LastMove.Clone;
        var last = p.m_currentAttack;
        var edge = new Box<float>();
        p.Dodge(rig.Forward);
        yield return WaitRollEdge(p, 4f, false, edge);
        if (edge.Value < 0f)
        {
            c.Check(false, $"window {F(after)} s: the roll ended");
            yield break;
        }
        while (Time.fixedTime - edge.Value < after - 0.5f * Time.fixedDeltaTime)
        {
            yield return Fixed;
        }
        var inRollBlend = animator != null && RollFlow.InRoll(animator);
        Press(p);
        var box = new Box<Attack>();
        yield return WaitNewAttack(p, last, 0.6f, box);
        var moved = !ReferenceEquals(MoveTracker.LastMove.Clone, before);
        var what = $"press {F(after)} s after the roll (window {F(rules.Window)} s)";
        var swing = box.Value != null && !moved && box.Value.m_attackAnimation == shared.m_attackAnimation;
        var roll = box.Value != null && moved && MoveTracker.LastMove.Kind == MoveKind.Roll && !MoveTracker.LastMove.Cut
                   && ReferenceEquals(box.Value, MoveTracker.LastMove.Clone);
        if (want == WindowWant.Swing)
        {
            c.Check(swing, $"{what}: normal swing");
        }
        else if (want == WindowWant.Flow)
        {
            c.Check(roll, $"{what}: roll attack ({S(MoveTracker.LastMove.RollAge)} s)");
        }
        else
        {
            c.Check(roll || swing, $"{what}: a roll attack or a normal swing");
        }
        if (roll)
        {
            var until = Time.fixedTime + 1f;
            while (Time.fixedTime < until && MoveTracker.LastEntryDelay < 0f && MoveTracker.Watching)
            {
                yield return Fixed;
            }
            var flow = MoveTracker.LastMove.Flow;
            // Every roll attack after a roll flows out of it: never the roll, the stand-up, then the move.
            c.Check(inRollBlend && flow == FlowResult.CrossFade
                    && MoveTracker.LastEntryDelay >= 0f && MoveTracker.LastEntryDelay <= 3f * Time.fixedDeltaTime + 0.001f,
                $"{what}: the roll attack cross-faded out of the roll's blend (flow {flow}), entered after {S(MoveTracker.LastEntryDelay)} s");
            var line = LogTap.First(mark, Dbg, "Roll attack: ");
            c.Check(line != null && line.Contains(" s after the roll ended (cross-fade from the roll, "),
                $"{what}: Debug line \"Roll attack: ... s after the roll ended (cross-fade from the roll, ...)\" (got \"{line ?? "none"}\")");
        }
        else if (swing)
        {
            c.Check(LogTap.Count(mark, Dbg, "Roll attack") == 0, $"{what}: no \"Roll attack\" Debug line for the normal swing");
        }
        SelfTest.Note(RollInputName, $"{what}: Animator {(inRollBlend ? "still in" : "out of")} the roll's blend at the press, "
                                     + (roll ? $"roll attack, flow {MoveTracker.LastMove.Flow}, entry after {S(MoveTracker.LastEntryDelay)} s"
                                        : swing ? "normal swing" : "no attack"));
        yield return WaitIdle(p, 4f);
    }

    // FlowStart 0 (design 2.4, Decision 22): the cut waits IframeMargin after the roll's i-frames end, so other players'
    // games (which read the i-frames from the ZDO) never see the roll attack's start as invulnerable.
    private static IEnumerator EarlyCut(Rig rig, Checks c)
    {
        var p = rig.P;
        var dt = Time.fixedDeltaTime;
        var early = MoveRules.Defaults();
        early.Cooldown = 0f;
        early.FlowStart = 0f;
        ServerRules.TestRules = early;
        yield return WaitIdle(p, 4f);
        Put(p, rig.Home);
        Face(p, rig.Forward);
        yield return Fixed;
        var run = new RollRun();
        yield return Roll(rig, run);
        // Tick see the i-frames off on the same or the next tick as this coroutine: margin counted from there.
        var off = run.IframesEnd >= 0f ? run.IframesEnd - run.RollStart : -1f;
        var margin = run.Cut - off;
        c.Check(run.Clone != null && run.Move.Kind == MoveKind.Roll && run.Move.Cut && run.StartedInRoll && off >= 0f
                && margin >= MoveTracker.IframeMargin - 0.5f * dt && margin <= MoveTracker.IframeMargin + 2.5f * dt,
            $"FlowStart 0: the roll attack cut into the roll about {F(MoveTracker.IframeMargin)} s after the i-frames ended (cut {S(run.Cut)} s, i-frames off {S(off)} s)");
        c.Check(!run.InvulnAfterStart, "FlowStart 0: never invulnerable from the attack start on");
        SelfTest.Note(RollInputName, $"FlowStart 0: i-frames off {S(off)} s into the roll, cut at {S(run.Cut)} s ({S(margin)} s later), "
                                     + $"flow {run.Move.Flow}, entry after {S(run.Delay)} s");
        yield return WaitIdle(p, 4f);
    }

    // Learning (design 2.9): learned states forgotten, controller probe off = no state: no cut, roll attack after the
    // roll with the trigger alone (NOTE what the controller does with it in the roll's blend out); state learned = the
    // one entered = the one the probe finds (probed before, with the sword's stance); second roll attack cut into the
    // roll, cross-faded from the learned state, no gap.
    private static IEnumerator Learned(Rig rig, Checks c, MoveRules rules)
    {
        var p = rig.P;
        const string trigger = "swing_longsword1";
        var animator = p.m_zanim.m_animator;
        var probed = new int[1];
        RollFlow.ProbeNow(animator, new[] { trigger }, StateProbe.LiveStance, probed, null);
        RollFlow.ForgetLearned();
        RollFlow.TestNoProbe = true;
        try
        {
            yield return WaitIdle(p, 4f);
            Put(p, rig.Home);
            Face(p, rig.Forward);
            yield return Fixed;
            var first = new RollRun();
            yield return Roll(rig, first);
            c.Check(first.Clone != null && first.Move.Kind == MoveKind.Roll && !first.Move.Cut && !first.StartedInRoll
                    && first.Move.Flow == FlowResult.TriggerOnly && first.Refusals == 0,
                $"no state known: no cut into the roll; the roll attack started after it with the trigger alone (cut {first.Move.Cut}, flow {first.Move.Flow})");
            var entered = first.EnteredAt >= 0f;
            c.Check(entered, "trigger alone: the roll attack entered its animation");
            var took = entered && first.GapTicks == 0 ? "took it at once (no idle or locomotion in between)"
                : "waited for the end of the roll's blend to idle";
            SelfTest.Note(RollInputName, $"trigger alone in the roll's blend out: the controller {took}; attack "
                                         + $"{Span(first.RollEnd, first.StartAt)} s after the roll ended, entry {S(first.Delay)} s after "
                                         + $"the start, gap {S(first.Gap)} s, clip {first.Clip ?? "n/a"}");
            var until = Time.fixedTime + 1f;
            while (Time.fixedTime < until && MoveTracker.Learning)
            {
                yield return Fixed;
            }
            var learned = RollFlow.LearnedHash(trigger);
            c.Check(learned != 0 && learned == first.EntryFull,
                $"the state of {trigger} was learned and is the one the Animator entered ({learned}, entered {first.EntryFull})");
            c.Check(probed[0] != 0 && probed[0] == first.EntryFull,
                $"the animation controller's state for {trigger} (probed before the roll) is the one the Animator entered ({probed[0]}, entered {first.EntryFull})");
            yield return WaitIdle(p, 4f);
            Put(p, rig.Home);
            Face(p, rig.Forward);
            yield return Fixed;
            var second = new RollRun();
            yield return Roll(rig, second);
            c.Check(second.Clone != null && second.Move.Cut && second.Move.Flow == FlowResult.CrossFade
                    && second.Move.Source == StateSource.Learned && second.EnteredAt >= 0f && second.GapTicks == 0,
                $"second roll attack: cut into the roll, cross-faded from the learned state with no gap (cut {second.Move.Cut}, flow {second.Move.Flow}, state {second.Move.Source}, gap {S(second.Gap)} s)");
            yield return WaitIdle(p, 4f);
        }
        finally
        {
            RollFlow.TestNoProbe = false;
        }
    }

    // Other player's view (design 2.9): during a roll with no attack, the roll attack trigger set on the Animator the
    // way the SetTrigger RPC does, then the remote handler's part after its player filters (the local player is
    // filtered out by design). Animator must go straight into an attack state, trigger gone, no second swing.
    private static IEnumerator RemoteView(Rig rig, Checks c, MoveRules rules)
    {
        var p = rig.P;
        const string trigger = "swing_longsword1";
        yield return WaitIdle(p, 4f);
        Put(p, rig.Home);
        Face(p, rig.Forward);
        // No attack of the player may hit from this swing: Humanoid.OnAttackTrigger hit with m_currentAttack.
        if (p.m_currentAttack != null)
        {
            p.m_previousAttack = p.m_currentAttack;
            p.m_currentAttack = null;
        }
        yield return Fixed;
        var animator = p.m_zanim.m_animator;
        p.Dodge(rig.Forward);
        var dodgeAt = Time.fixedTime;
        float rollStart = -1f, fedAt = -1f;
        var result = FlowResult.None;
        var pendingAfter = true;
        while (Time.fixedTime - dodgeAt < 3f)
        {
            yield return Fixed;
            var now = Time.fixedTime;
            if (rollStart < 0f && p.m_inDodge)
            {
                rollStart = now;
            }
            if (rollStart >= 0f && now - rollStart + 0.001f >= rules.FlowStart && !p.m_dodgeInvincible
                && animator != null && RollFlow.InRoll(animator))
            {
                animator.SetTrigger(trigger); // what ZSyncAnimation.RPC_SetTrigger does on every peer
                result = RollFlow.ForOther(animator, trigger, rules, out _);
                pendingAfter = animator.GetBool(trigger);
                fedAt = now;
                break;
            }
            if (rollStart < 0f && now - dodgeAt > 0.6f)
            {
                break;
            }
        }
        if (fedAt < 0f)
        {
            c.Check(false, "other player's view: the roll reached the cut point");
            yield break;
        }
        var gap = 0;
        var attackAt = -1f;
        var t0 = Time.fixedTime;
        while (Time.fixedTime - t0 < 0.5f && attackAt < 0f)
        {
            yield return Fixed;
            RollFlow.NextOrCurrent(animator, out _, out var tag);
            if (tag == RollFlow.AttackTag)
            {
                attackAt = Time.fixedTime;
            }
            else if (InGap(animator))
            {
                gap++;
            }
        }
        // Swing over, then no second one from a left-over trigger. The Animator itself is read: InAttack() answers from
        // the game's tag cache, taken each tick before the Animator update, so on the tick the cross-fade began it
        // still said "no attack" and the first run's wait ended at once and took this very swing for a second one.
        var until = Time.fixedTime + 3f;
        var over = false;
        while (!over && Time.fixedTime < until)
        {
            yield return Fixed;
            RollFlow.NextOrCurrent(animator, out _, out var tag);
            over = tag != RollFlow.AttackTag;
        }
        var overAt = Time.fixedTime;
        var replayed = false;
        until = Time.fixedTime + 0.6f;
        while (over && Time.fixedTime < until && !replayed)
        {
            yield return Fixed;
            RollFlow.NextOrCurrent(animator, out _, out var tag);
            replayed = tag == RollFlow.AttackTag;
        }
        c.Check(result == FlowResult.CrossFade && !pendingAfter,
            $"other player's view: the roll attack trigger cross-faded out of the roll and is no longer pending (flow {result})");
        c.Check(attackAt >= 0f && attackAt - fedAt <= 2f * Time.fixedDeltaTime + 0.001f && gap == 0,
            $"other player's view: straight into the attack state ({Span(fedAt, attackAt)} s, gap {gap} ticks)");
        c.Check(over, $"other player's view: the swing ended ({Span(attackAt, overAt)} s after it started)");
        c.Check(!replayed, "other player's view: no second swing after it");
        SelfTest.Note(RollInputName, $"other player's view: trigger at {S(fedAt - rollStart)} s into the roll, attack state "
                                     + $"{Span(fedAt, attackAt)} s later, gap {gap} ticks, swing over {Span(attackAt, overAt)} s after "
                                     + $"it started, {(replayed ? "played twice" : "played once")}");
        yield return WaitIdle(p, 4f);
    }

    // Cooldown 5: roll attack, roll again: normal swing. NOTE the fastest roll loop against the default 1 s.
    private static IEnumerator Cooldown(Rig rig, Checks c, Attack shared)
    {
        var p = rig.P;
        var rules = MoveRules.Defaults();
        rules.Cooldown = 5f;
        ServerRules.TestRules = rules;
        var edge = new Box<float>();
        var box = new Box<Attack>();
        MoveTracker.Reset(); // moves before in this test must not block first one
        yield return WaitIdle(p, 4f);
        Put(p, rig.Home);
        Face(p, rig.Forward);
        yield return Fixed;
        var before = MoveTracker.LastMove.Clone;
        var last = p.m_currentAttack;
        p.Dodge(rig.Forward);
        yield return WaitRollEdge(p, 4f, true, edge);
        yield return WaitNewAttack(p, last, 0.6f, box);
        p.m_queuedAttackTimer = 0f; // press kept after a cut would chain a combo step
        var first = MoveTracker.LastMove.Clone;
        c.Check(box.Value != null && !ReferenceEquals(first, before) && ReferenceEquals(box.Value, first),
            "cooldown 5 s: the first roll attack plays");
        yield return WaitIdle(p, 4f);
        Put(p, rig.Home);
        Face(p, rig.Forward);
        yield return Fixed;
        last = p.m_currentAttack;
        p.Dodge(rig.Forward);
        yield return WaitRollEdge(p, 4f, true, edge);
        yield return WaitNewAttack(p, last, 0.6f, box);
        p.m_queuedAttackTimer = 0f; // press kept after a cut would chain a combo step
        c.Check(box.Value != null && ReferenceEquals(MoveTracker.LastMove.Clone, first)
                && box.Value.m_attackAnimation == shared.m_attackAnimation,
            "cooldown 5 s: the second roll right after gives a normal swing");
        yield return WaitIdle(p, 4f);

        // Fastest vanilla loop (roll as soon as move animation end): time between two move starts.
        var loose = MoveRules.Defaults();
        loose.Cooldown = 0f;
        ServerRules.TestRules = loose;
        Put(p, rig.Home);
        Face(p, rig.Forward);
        yield return Fixed;
        before = MoveTracker.LastMove.Clone;
        last = p.m_currentAttack;
        p.Dodge(rig.Forward);
        yield return WaitRollEdge(p, 4f, true, edge);
        yield return WaitNewAttack(p, last, 0.6f, box);
        p.m_queuedAttackTimer = 0f; // press kept after a cut would chain a combo step
        var one = MoveTracker.LastMove.Clone;
        var until = Time.fixedTime + 2f;
        while (Time.fixedTime < until && (MoveTracker.LastEntryDelay < 0f || p.InAttack()))
        {
            yield return Fixed;
        }
        var firstEntry = MoveTracker.LastMoveAt;
        last = p.m_currentAttack;
        p.Dodge(-rig.Forward);
        yield return WaitRollEdge(p, 4f, true, edge);
        yield return WaitNewAttack(p, last, 0.6f, box);
        p.m_queuedAttackTimer = 0f; // press kept after a cut would chain a combo step
        var two = MoveTracker.LastMove.Clone;
        if (!ReferenceEquals(one, before) && !ReferenceEquals(two, one) && box.Value != null)
        {
            var gap = MoveTracker.LastStartAt - firstEntry;
            SelfTest.Note(RollInputName, $"fastest roll, roll attack, roll, roll attack loop: {S(gap)} s from the first roll "
                                         + $"attack's entry (where the cooldown starts) to the second one's start; the default "
                                         + $"Cooldown of 1 s {(gap >= MoveRules.DefaultCooldown ? "never blocks it" : "WOULD block it")}");
        }
        else
        {
            SelfTest.Note(RollInputName, "fastest roll loop: could not measure (a roll attack did not start)");
        }
        yield return WaitIdle(p, 4f);
        ServerRules.TestRules = null;
    }

    // ---------- moveset.aim ----------

    private static IEnumerator RunAim()
    {
        var p = LocalPlayer(AimName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(AimName);
        var rig = new Rig(AimName, p);
        try
        {
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            if (held.Value == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            // (AimAngle, look pitch down): default; 0 = level; largest setting while looking almost straight down
            // (vanilla aim never pass 45, so the setting stop there).
            var cases = new[] { (30f, 45f), (0f, 45f), (MoveRules.MaxAimAngle, 85f) };
            foreach (var (aim, lookDown) in cases)
            {
                var rules = MoveRules.Defaults();
                rules.Cooldown = 0f;
                rules.AimAngle = aim;
                ServerRules.TestRules = rules;
                yield return WaitIdle(p, 4f);
                Put(p, rig.Home);
                Face(p, rig.Forward);
                p.m_lookPitch = lookDown;
                p.SetMouseLook(Vector2.zero);
                yield return null;
                yield return Fixed;
                var before = MoveTracker.LastMove.Clone;
                p.Jump();
                yield return Fixed;
                Press(p);
                Attack clone = null;
                float air = float.NaN, expected = float.NaN, landed = float.NaN, landAt = -1f;
                var t0 = Time.fixedTime;
                while (Time.fixedTime - t0 < 3f)
                {
                    yield return Fixed;
                    var now = Time.fixedTime;
                    if (clone == null && !ReferenceEquals(MoveTracker.LastMove.Clone, before))
                    {
                        clone = MoveTracker.LastMove.Clone;
                    }
                    if (clone == null)
                    {
                        continue;
                    }
                    if (float.IsNaN(air) && !p.IsOnGround())
                    {
                        clone.GetMeleeAttackDir(out _, out var dir);
                        air = Pitch(dir);
                        // Vanilla: aim = eye pitch on body heading; swing turn toward it by m_maxYAngle at most.
                        var look = p.GetAimDir(p.transform.position);
                        var fwd = p.transform.forward;
                        expected = Mathf.Min(aim, Pitch(new Vector3(fwd.x, look.y, fwd.z)));
                    }
                    if (landAt < 0f && now - t0 >= MoveTracker.JumpGroundLock && p.IsOnGround())
                    {
                        landAt = now;
                    }
                    if (landAt >= 0f && now >= landAt + 1.5f * Time.fixedDeltaTime)
                    {
                        clone.GetMeleeAttackDir(out _, out var dir);
                        landed = Pitch(dir);
                        break;
                    }
                }
                c.Check(clone != null && MoveTracker.LastMove.Kind == MoveKind.Jump, $"aim {F(aim)}: a jump attack started");
                c.Check(!float.IsNaN(air) && Mathf.Abs(air - expected) <= 1f && air <= MoveRules.MaxAimAngle + 0.5f,
                    $"aim {F(aim)}, look {F(lookDown)} down: the swing pitches {F(air)} degrees down in the air "
                    + $"(expected {F(expected)}, never above {F(MoveRules.MaxAimAngle)})");
                c.Check(!float.IsNaN(landed) && Mathf.Abs(landed) <= 0.5f,
                    $"aim {F(aim)}: the swing is level after landing ({F(landed)} degrees)");
                SelfTest.Note(AimName, $"aim {F(aim)}: look {F(lookDown)} down, swing {F(air)} down in the air, {F(landed)} after landing");
                p.m_lookPitch = 0f;
                p.SetMouseLook(Vector2.zero);
                yield return WaitIdle(p, 4f);
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- moveset.stamina ----------

    private static IEnumerator RunStamina()
    {
        var p = LocalPlayer(StaminaName);
        if (p == null)
        {
            yield break;
        }
        var zone = ZoneSystem.instance;
        if (zone == null)
        {
            SelfTest.Fail(StaminaName, "no ZoneSystem");
            yield break;
        }
        var c = new Checks(StaminaName);
        var rig = new Rig(StaminaName, p);
        var god = p.InGodMode();
        var hadRate = zone.GetGlobalKey(GlobalKeys.StaminaRate, out float rate);
        try
        {
            // Normal player: stamina really used (world modifier off), no god mode.
            p.SetGodMode(false);
            zone.RemoveGlobalKey(GlobalKeys.StaminaRate);
            var until = Time.fixedTime + 3f;
            while (Time.fixedTime < until && Game.m_staminaRate <= 0f)
            {
                yield return Fixed;
            }
            c.Check(Game.m_staminaRate > 0f, $"stamina is used again (rate {F(Game.m_staminaRate)})");
            var rules = MoveRules.Defaults();
            rules.Cooldown = 0f;
            rules.Roll.Stamina = 3f;
            ServerRules.TestRules = rules;
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            var weapon = held.Value;
            if (weapon == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var shared = weapon.m_shared.m_attack;
            var probe = shared.Clone();
            probe.m_character = p;
            probe.m_weapon = weapon;
            var cost = probe.GetAttackStamina();
            c.Check(cost > 0.5f, $"a sword swing costs stamina ({F(cost)})");
            var edge = new Box<float>();

            // A. Stamina for one swing, not three: normal swing on the first try (no retry, no flash loop).
            yield return WaitIdle(p, 4f);
            Put(p, rig.Home);
            Face(p, rig.Forward);
            p.m_stamina = p.GetMaxStamina();
            yield return Fixed;
            var before = MoveTracker.LastMove.Clone;
            var prev = p.m_currentAttack;
            p.Dodge(rig.Forward);
            yield return WaitRollEdge(p, 4f, false, edge);
            c.Check(edge.Value >= 0f, "roll A ended");
            p.m_stamina = cost * 2f;
            Press(p);
            yield return Fixed;
            var a = p.m_currentAttack;
            c.Check(a != null && !ReferenceEquals(a, prev) && p.m_queuedAttackTimer <= 0f,
                "stamina for one swing but not three: the attack started on the first try");
            c.Check(a != null && ReferenceEquals(MoveTracker.LastMove.Clone, before) && a.m_attackAnimation == shared.m_attackAnimation
                    && Near(a.m_attackStamina, shared.m_attackStamina),
                "stamina for one swing but not three: a normal swing at its normal cost");
            yield return WaitIdle(p, 4f);

            // B. Stamina for the move: roll attack, three swings paid when it starts.
            Put(p, rig.Home);
            Face(p, rig.Forward);
            p.m_stamina = p.GetMaxStamina();
            yield return Fixed;
            before = MoveTracker.LastMove.Clone;
            p.Dodge(rig.Forward);
            yield return WaitRollEdge(p, 4f, false, edge);
            c.Check(edge.Value >= 0f, "roll B ended");
            var start = cost * 3f + 5f;
            p.m_stamina = start;
            Press(p);
            var previous = start;
            var biggestDrop = 0f;
            var afterEntry = 0;
            Attack clone = null;
            var t0 = Time.fixedTime;
            while (Time.fixedTime - t0 < 1.5f && afterEntry < 3)
            {
                yield return Fixed;
                var s = p.m_stamina;
                biggestDrop = Mathf.Max(biggestDrop, previous - s);
                previous = s;
                if (clone == null && !ReferenceEquals(MoveTracker.LastMove.Clone, before))
                {
                    clone = MoveTracker.LastMove.Clone;
                }
                if (clone != null && MoveTracker.LastEntryDelay >= 0f && ReferenceEquals(MoveTracker.LastMove.Clone, clone))
                {
                    afterEntry++;
                }
            }
            c.Check(clone != null && MoveTracker.LastMove.Kind == MoveKind.Roll && Near(clone.m_attackStamina, shared.m_attackStamina * 3f),
                "stamina for the move: roll attack with three times the swing's stamina");
            c.Check(Mathf.Abs(biggestDrop - cost * 3f) <= 0.6f,
                $"the roll attack took three swings of stamina when it started ({F(biggestDrop)}, expected {F(cost * 3f)})");
            SelfTest.Note(StaminaName, $"sword swing {F(cost)} stamina; roll attack x3 paid {F(biggestDrop)} "
                                       + $"({(clone != null ? $"move {MoveTracker.LastMove.Kind} {MoveTracker.LastMove.Trigger}" : "no move started")}, "
                                       + $"stamina rate {F(Game.m_staminaRate)})");
            yield return WaitIdle(p, 4f);

            // C. Press during the roll with stamina for one swing but not three: our roll gate let the attack in, it
            // is no roll attack (stamina fallback), so refused inside the roll like vanilla; the roll play its full
            // length and the normal swing start on the tick after it.
            Put(p, rig.Home);
            Face(p, rig.Forward);
            p.m_stamina = p.GetMaxStamina();
            yield return Fixed;
            before = MoveTracker.LastMove.Clone;
            prev = p.m_currentAttack;
            var refusals = MoveTracker.GateRefusals;
            p.Dodge(rig.Forward);
            float rollStart = -1f, rollEnd = -1f, startAt = -1f;
            Attack got = null;
            var gotInRoll = false;
            t0 = Time.fixedTime;
            while (Time.fixedTime - t0 < 3f)
            {
                yield return Fixed;
                var now = Time.fixedTime;
                if (rollStart < 0f && p.m_inDodge)
                {
                    rollStart = now;
                    p.m_stamina = cost * 2f; // after the roll paid its own
                }
                if (p.m_inDodge)
                {
                    Press(p);
                }
                if (rollStart >= 0f && rollEnd < 0f && !p.m_inDodge)
                {
                    rollEnd = now;
                }
                var current = p.m_currentAttack;
                if (current != null && !ReferenceEquals(current, prev))
                {
                    got = current;
                    startAt = now;
                    gotInRoll = p.m_inDodge;
                    break;
                }
            }
            c.Check(got != null && !gotInRoll && rollEnd >= 0f && Mathf.Abs(startAt - rollEnd - Time.fixedDeltaTime) < 0.5f * Time.fixedDeltaTime,
                $"press in the roll, stamina for a normal swing only: no attack inside the roll, the swing started on the tick after it ({Span(rollEnd, startAt)} s)");
            c.Check(rollStart >= 0f && rollEnd - rollStart >= 0.8f,
                $"press in the roll, stamina for a normal swing only: the roll played its full length ({Span(rollStart, rollEnd)} s)");
            c.Check(MoveTracker.GateRefusals > refusals, $"press in the roll, stamina for a normal swing only: refused inside the roll ({MoveTracker.GateRefusals - refusals} ticks)");
            c.Check(got != null && ReferenceEquals(MoveTracker.LastMove.Clone, before) && got.m_attackAnimation == shared.m_attackAnimation
                    && Near(got.m_attackStamina, shared.m_attackStamina),
                "press in the roll, stamina for a normal swing only: a normal swing at its normal cost");
            yield return WaitIdle(p, 4f);
            c.Report();
        }
        finally
        {
            if (p != null)
            {
                p.SetGodMode(god);
                p.m_stamina = p.GetMaxStamina();
            }
            if (zone != null)
            {
                if (hadRate)
                {
                    zone.SetGlobalKey(GlobalKeys.StaminaRate, rate);
                }
                else
                {
                    zone.RemoveGlobalKey(GlobalKeys.StaminaRate);
                }
            }
            rig.Restore();
        }
    }

    // ---------- moveset.exclusions ----------

    private static readonly string[] ExcludedLive = { "Torch", "PickaxeIron", "Scythe", "BombOoze", "MC_SmokeScreen" };

    private static IEnumerator RunExclusions()
    {
        var p = LocalPlayer(ExclusionsName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(ExclusionsName);
        var rig = new Rig(ExclusionsName, p);
        try
        {
            var rules = MoveRules.Defaults();
            rules.Cooldown = 0f;
            ServerRules.TestRules = rules;
            foreach (var name in ExcludedLive)
            {
                if (Prefab(name) == null)
                {
                    // Only Sneak Ambush's item may be missing (TESTING T11: "if installed"); a game item must be there.
                    SelfTest.Note(ExclusionsName, $"{name} not registered: SKIP");
                    c.Check(name == "MC_SmokeScreen", $"{name} exists in this game");
                    continue;
                }
                yield return ExclusionRow(rig, c, name);
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // Jump then press, roll then press: the item's own attack, no move, tokens used. Me take the item off right
    // after the start: the swing never hits (no dug ground), the bomb never flies.
    private static IEnumerator ExclusionRow(Rig rig, Checks c, string name)
    {
        var p = rig.P;
        yield return WaitIdle(p, 4f);
        p.UnequipItem(p.m_rightItem, false);
        p.UnequipItem(p.m_leftItem, false);
        // Two for stackables (a bomb that flew anyway still leave one); AddItem make two items of a non-stackable.
        var item = rig.Give(name, Prefab(name).m_itemData.m_shared.m_maxStackSize > 1 ? 2 : 1);
        var box = new Box<Attack>();
        var edge = new Box<float>();
        for (var roll = 0; roll < 2; roll++)
        {
            var what = roll == 1 ? "after a roll" : "in a jump";
            yield return WaitIdle(p, 4f);
            if (item == null || (!p.IsItemEquiped(item) && !p.EquipItem(item, false)))
            {
                c.Check(false, $"{name}: could not equip");
                break;
            }
            yield return WaitIdle(p, 3f);
            Put(p, rig.Home);
            Face(p, rig.Forward);
            yield return Fixed;
            var shared = item.m_shared.m_attack;
            var before = MoveTracker.LastMove.Clone;
            var last = p.m_currentAttack;
            var mark = LogTap.Mark;
            if (roll == 0)
            {
                p.Jump();
                c.Check(MoveTracker.JumpLive, $"{name}: jump token live");
                yield return WaitTicks(0.1f);
                Press(p);
            }
            else
            {
                p.Dodge(rig.Forward);
                yield return WaitRollEdge(p, 4f, true, edge);
                // Our roll gate let the press in at the cut point, but no roll attack for this item: refused there.
                c.Check(ReferenceEquals(p.m_currentAttack, last), $"{name} {what}: no attack inside the roll");
            }
            yield return WaitNewAttack(p, last, 0.6f, box);
            var a = box.Value;
            // The item's attack must start (first run: it did for every item, 6 checks each): a row with no attack
            // would have looked at nothing and still passed.
            c.Check(a != null, $"{name} {what}: its attack started");
            c.Check(LogTap.Count(mark, Dbg, " attack: ") == 0, $"{name} {what}: no move Debug line");
            if (a != null)
            {
                c.Check(ReferenceEquals(MoveTracker.LastMove.Clone, before) && a.m_attackAnimation == shared.m_attackAnimation
                        && a.m_attackChainLevels == shared.m_attackChainLevels && Near(a.m_damageMultiplier, shared.m_damageMultiplier),
                    $"{name} {what}: its own attack {shared.m_attackAnimation}, no move (got {a.m_attackAnimation})");
                c.Check(!MoveTracker.JumpLive && float.IsNegativeInfinity(MoveTracker.RollEndAt), $"{name} {what}: tokens used up");
                p.UnequipItem(item, false);
                ResetFired(p, a); // attack dropped in its restart gap: no stale swing later
            }
            yield return WaitLanded(p, 3f);
        }
        yield return WaitIdle(p, 4f);
        rig.TakeBack(item);
    }

    // ---------- moveset.watchdog ----------

    private static IEnumerator RunWatchdog()
    {
        var p = LocalPlayer(WatchdogName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(WatchdogName);
        var rig = new Rig(WatchdogName, p);
        try
        {
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            if (held.Value == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var tested = new Box<bool>();
            foreach (var candidate in WatchdogTriggers)
            {
                if (!p.GetZAnim().HasParameter(candidate, AnimatorControllerParameterType.Trigger))
                {
                    SelfTest.Note(WatchdogName, $"{candidate} is not a Trigger of the player Animator: next candidate");
                    continue;
                }
                yield return Watchdog(rig, c, candidate, tested);
                if (tested.Value)
                {
                    break;
                }
            }
            c.Check(tested.Value, "a candidate trigger stayed pending until the deadline (the start check could be tested)");
            c.Report();
        }
        finally
        {
            MoveTracker.ResetWarnings();
            rig.Restore();
        }
    }

    private static IEnumerator Watchdog(Rig rig, Checks c, string candidate, Box<bool> tested)
    {
        var p = rig.P;
        var dt = Time.fixedDeltaTime;
        tested.Value = false;
        var rules = MoveRules.Defaults();
        rules.Cooldown = 0f;
        rules.RollTriggers[Families.Index(WeaponFamily.Swords)] = candidate; // test rules: list skipped, not HasParameter
        ServerRules.TestRules = rules;
        MoveTriggers.Invalidate();
        MoveTracker.ResetWarnings();
        // Trigger alone (no cross-fade): the trigger must stay pending for the start check to be tested.
        RollFlow.TestNoState = true;
        yield return WaitIdle(p, 4f);
        Put(p, rig.Home);
        Face(p, rig.Forward);
        yield return Fixed;
        var animator = p.m_zanim.m_animator;
        var before = MoveTracker.LastMove.Clone;
        var resets = MoveTracker.TriggerResets;
        var failures = MoveTracker.StartFailures;
        var refused = MoveTracker.RefusedRestarts;
        p.Dodge(rig.Forward);
        Attack clone = null;
        float startAt = -1f, cancelAt = -1f, lastAge = -1f, clockAt = -1f;
        bool lastPending = false, pressedAgain = false, refusedSeen = false, stillCurrent = false, enteredAnyway = false;
        var afterRoll = false;
        var refusedAtPress = 0;
        bool pendingAfter = true, doneAfter = false, detachedAfter = false;
        Attack next = null;
        var t0 = Time.fixedTime;
        while (Time.fixedTime - t0 < 5f)
        {
            yield return Fixed;
            var now = Time.fixedTime;
            if (p.m_inDodge)
            {
                Press(p);
            }
            if (clone == null && !ReferenceEquals(MoveTracker.LastMove.Clone, before))
            {
                clone = MoveTracker.LastMove.Clone;
                startAt = now;
                afterRoll = !MoveTracker.LastMove.Cut && !p.m_inDodge && MoveTracker.LastMove.Flow == FlowResult.TriggerOnly;
            }
            if (clone == null)
            {
                continue;
            }
            if (MoveTracker.LastEntryDelay >= 0f && ReferenceEquals(MoveTracker.LastMove.Clone, clone))
            {
                enteredAnyway = true;
                break;
            }
            if (MoveTracker.WatchStarting && ReferenceEquals(MoveTracker.Watched.Clone, clone))
            {
                // Start check clock: from the attack start (no cut: nothing to wait for).
                clockAt = MoveTracker.WatchClockAt;
                lastAge = now - clockAt;
                lastPending = animator != null && animator.GetBool(candidate);
                if (!pressedAgain && lastAge >= 0.2f)
                {
                    pressedAgain = true;
                    refusedAtPress = MoveTracker.RefusedRestarts;
                    Press(p); // second press while starting: refused
                }
                else if (pressedAgain && !refusedSeen && MoveTracker.RefusedRestarts > refusedAtPress)
                {
                    refusedSeen = true;
                    stillCurrent = ReferenceEquals(p.m_currentAttack, clone);
                }
                continue;
            }
            // Watch over: cancelled (or dropped) this tick.
            cancelAt = now;
            pendingAfter = animator != null && animator.GetBool(candidate);
            doneAfter = clone.m_attackDone;
            detachedAfter = !ReferenceEquals(p.m_currentAttack, clone);
            yield return Fixed;
            next = p.m_currentAttack;
            break;
        }
        if (clone == null)
        {
            c.Check(false, $"{candidate}: the roll attack with it started");
            yield break;
        }
        if (enteredAnyway)
        {
            SelfTest.Note(WatchdogName, $"{candidate} entered an attack state after the roll: it has a way in, next candidate");
            yield return WaitIdle(p, 4f);
            yield break;
        }
        if (!lastPending)
        {
            SelfTest.Note(WatchdogName, $"the Animator consumed {candidate} before the deadline (last look at {S(lastAge)} s): next candidate");
            yield return WaitIdle(p, 4f);
            yield break;
        }
        tested.Value = true;
        c.Check(afterRoll, $"{candidate}: no attack state known, so no cut into the roll: the move started after it with the trigger alone");
        c.Check(Mathf.Abs(clockAt - startAt) < 0.5f * dt, $"{candidate}: the start check clock ran from the attack start ({S(clockAt - startAt)} s after it)");
        c.Check(lastAge >= 0.4f, $"{candidate} still pending just before the deadline ({S(lastAge)} s on the clock)");
        c.Check(refusedSeen && stillCurrent, $"{candidate}: a second press while starting was refused, the move stayed current");
        c.Check(cancelAt >= 0f && cancelAt - clockAt >= MoveTracker.StartDeadline - 0.001f
                && cancelAt - clockAt <= MoveTracker.StartDeadline + 2f * dt,
            $"{candidate}: cancelled at the 0.5 s deadline of its clock ({S(cancelAt - clockAt)} s)");
        c.Check(MoveTracker.TriggerResets == resets + 1 && !pendingAfter,
            $"{candidate}: trigger reset once ({MoveTracker.TriggerResets - resets}), no longer pending");
        c.Check(doneAfter && detachedAfter, $"{candidate}: the move's clone is stopped and no longer the current attack");
        c.Check(MoveTracker.StartFailures == failures + 1 && MoveTracker.StartWarnings == 1,
            $"{candidate}: one start failure, warning logged once ({MoveTracker.StartWarnings})");
        c.Check(next != null && !ReferenceEquals(next, clone) && ReferenceEquals(MoveTracker.LastMove.Clone, clone)
                && next.m_attackAnimation == "swing_longsword" && next.m_currentAttackCainLevel == 0,
            $"{candidate}: on the next tick a normal swing_longsword0 started (got {(next != null ? next.m_attackAnimation + next.m_currentAttackCainLevel : "none")})");
        SelfTest.Note(WatchdogName, $"{candidate}: started after the roll, clock started {S(clockAt - startAt)} s after the start, "
                                    + $"pending {S(lastAge)} s on the clock, cancelled {S(cancelAt - clockAt)} s after the clock "
                                    + $"started, {MoveTracker.RefusedRestarts - refused} restarts refused");
        yield return WaitIdle(p, 4f);
    }

    // ---------- moveset.order ----------

    private static bool _orderOn;
    private static readonly List<string> OrderLog = new List<string>();

    private static IEnumerator RunOrder()
    {
        var p = LocalPlayer(OrderName);
        if (p == null)
        {
            yield break;
        }
        var harmony = new Harmony(ModInfo.Guid + ".selftest.order");
        var fps = Application.targetFrameRate;
        var vSync = QualitySettings.vSyncCount;
        try
        {
            harmony.Patch(AccessTools.Method(typeof(PlayerController), nameof(PlayerController.FixedUpdate)),
                new HarmonyMethod(AccessTools.Method(typeof(SelfTests), nameof(OrderController))));
            harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.FixedUpdate)),
                new HarmonyMethod(AccessTools.Method(typeof(SelfTests), nameof(OrderPlayer))));
            harmony.Patch(AccessTools.Method(typeof(MonoUpdaters), nameof(MonoUpdaters.FixedUpdate)),
                new HarmonyMethod(AccessTools.Method(typeof(SelfTests), nameof(OrderUpdaters))));
            harmony.Patch(AccessTools.Method(typeof(Humanoid), nameof(Humanoid.CustomFixedUpdate), new[] { typeof(float) }),
                new HarmonyMethod(AccessTools.Method(typeof(SelfTests), nameof(OrderHumanoid))));
            var animator = p.m_animator;
            SelfTest.Note(OrderName, $"player Animator update mode {(animator != null ? animator.updateMode.ToString() : "n/a")}, "
                                     + $"culling {(animator != null ? animator.cullingMode.ToString() : "n/a")}; physics tick {S(Time.fixedDeltaTime)} s");
            var normal = new List<string>();
            yield return OrderRun(normal, 10);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 20;
            yield return new WaitForSecondsRealtime(0.5f);
            var slow = new List<string>();
            yield return OrderRun(slow, 10);
            Application.targetFrameRate = fps;
            QualitySettings.vSyncCount = vSync;
            SelfTest.Note(OrderName, $"per physics tick (C = PlayerController.FixedUpdate, P = Player.FixedUpdate, "
                                     + $"M = MonoUpdaters.FixedUpdate, H = Humanoid.CustomFixedUpdate of the local player): {Summary(normal)}");
            SelfTest.Note(OrderName, $"first ticks: {string.Join(" | ", normal.Take(4).ToArray())}");
            SelfTest.Note(OrderName, $"at 20 FPS: {Summary(slow)}; first ticks: {string.Join(" | ", slow.Take(6).ToArray())}");
            var seen = normal.Count(s => s.Contains("P#") && s.Contains("H#"));
            if (seen >= 5)
            {
                SelfTest.Pass(OrderName, $"tick order logged for {normal.Count + slow.Count} physics ticks");
            }
            else
            {
                SelfTest.Fail(OrderName, $"hooks saw Player and Humanoid updates in only {seen} of {normal.Count} ticks");
            }
        }
        finally
        {
            _orderOn = false;
            Application.targetFrameRate = fps;
            QualitySettings.vSyncCount = vSync;
            harmony.UnpatchSelf();
        }
    }

    // Me record ticks: one line per physics tick, entries in call order with frame and MonoUpdaters count.
    private static IEnumerator OrderRun(List<string> ticks, int count)
    {
        yield return Fixed;
        OrderLog.Clear();
        _orderOn = true;
        for (var i = 0; i < count; i++)
        {
            yield return Fixed;
            ticks.Add(string.Join(" ", OrderLog.ToArray()));
            OrderLog.Clear();
        }
        _orderOn = false;
    }

    // "CPMH x7, PCMH x3" style count of tick patterns.
    private static string Summary(List<string> ticks)
    {
        return string.Join(", ", ticks
            .Select(t => new string(t.Split(' ').Where(e => e.Length > 0).Select(e => e[0]).ToArray()))
            .GroupBy(s => s).Select(g => $"{g.Key} x{g.Count()}").ToArray());
    }

    private static void OrderAdd(char what)
    {
        OrderLog.Add($"{what}#f{Time.frameCount}u{MonoUpdaters.UpdateCount}");
    }

    private static void OrderController()
    {
        try
        {
            if (_orderOn)
            {
                OrderAdd('C');
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("SelfTests order PlayerController.FixedUpdate", e);
        }
    }

    private static void OrderPlayer(Player __instance)
    {
        try
        {
            if (_orderOn && ReferenceEquals(__instance, Player.m_localPlayer))
            {
                OrderAdd('P');
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("SelfTests order Player.FixedUpdate", e);
        }
    }

    private static void OrderUpdaters()
    {
        try
        {
            if (_orderOn)
            {
                OrderAdd('M');
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("SelfTests order MonoUpdaters.FixedUpdate", e);
        }
    }

    private static void OrderHumanoid(Humanoid __instance)
    {
        try
        {
            if (_orderOn && ReferenceEquals(__instance, Player.m_localPlayer))
            {
                OrderAdd('H');
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("SelfTests order Humanoid.CustomFixedUpdate", e);
        }
    }
#endif
}
