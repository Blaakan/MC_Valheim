#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Combat.WeaponsMovesetMod;

// Debug build only. Tools of the self tests (SelfTests.cs and the other SelfTests.*.cs files):
//   LogTap   listen to this mod's own log lines (every level, whatever the log file shows): tests check the Debug /
//            Info / Warning text of TESTING.md and count errors, with no hook in mod code
//   HitTap   temporary Harmony hooks (own id, only while a test asks): every hit as sent and as the target's owner got
//            it (after the wire copy), the damage number, stagger calls, melee hit events (weapon, pitch), stamina
//            bar flashes; can put one foe in front of every melee event of the local player
//   Foe      creature spawned with its AI off, destroyed by Rig.Restore
//   JumpPress, Settle, Vanilla, PatchedCount, StateNow: small shared steps
internal static partial class SelfTests
{
    private const LogLevel Dbg = LogLevel.Debug;
    private const LogLevel Inf = LogLevel.Info;
    private const LogLevel Wrn = LogLevel.Warning;
    private const LogLevel Err = LogLevel.Error | LogLevel.Fatal;

    private struct LogLine
    {
        internal LogLevel Level;
        internal string Text;
        internal float At; // Time.fixedTime when logged, -1 = unknown (line from another thread)
    }

    // Lines of this mod only, never the [selftest] ones. Mark = number of lines seen so far: "since mark" = lines of
    // one test step. Old lines dropped past Keep (marks stay valid: absolute numbers).
    private sealed class LogTap : ILogListener
    {
        private const int Keep = 8000;
        private const int Drop = 2000;
        private static readonly object Gate = new object();
        private static readonly List<LogLine> Lines = new List<LogLine>();
        private static readonly List<LogLine> Alerts = new List<LogLine>(); // warnings and errors since the start
        private const int KeepAlerts = 2000;
        // Warnings and errors of every OTHER log source, text = "<source>: <line>" (a sibling mod's complaint).
        private static readonly List<LogLine> Foreign = new List<LogLine>();
        private static int _base;
        private static LogTap _instance;

        internal static bool Installed => _instance != null;

        internal static void Install()
        {
            if (_instance != null)
            {
                return;
            }
            _instance = new LogTap();
            BepInEx.Logging.Logger.Listeners.Add(_instance);
        }

        internal static int Mark
        {
            get
            {
                lock (Gate)
                {
                    return _base + Lines.Count;
                }
            }
        }

        internal static List<LogLine> Since(int mark, LogLevel levels, string contains = null)
        {
            var result = new List<LogLine>();
            lock (Gate)
            {
                for (var i = Mathf.Max(0, mark - _base); i < Lines.Count; i++)
                {
                    var line = Lines[i];
                    if ((line.Level & levels) != 0
                        && (contains == null || line.Text.IndexOf(contains, StringComparison.Ordinal) >= 0))
                    {
                        result.Add(line);
                    }
                }
            }
            return result;
        }

        internal static int Count(int mark, LogLevel levels, string contains = null) => Since(mark, levels, contains).Count;

        // Every warning and error of this mod since the game started (index = how many were there at some moment).
        internal static int AlertMark
        {
            get
            {
                lock (Gate)
                {
                    return Alerts.Count;
                }
            }
        }

        internal static List<LogLine> AlertsSince(int index, LogLevel levels)
        {
            var result = new List<LogLine>();
            lock (Gate)
            {
                for (var i = Mathf.Max(0, index); i < Alerts.Count; i++)
                {
                    if ((Alerts[i].Level & levels) != 0)
                    {
                        result.Add(Alerts[i]);
                    }
                }
            }
            return result;
        }

        // First such line since mark, null = none.
        internal static string First(int mark, LogLevel levels, string contains)
        {
            var lines = Since(mark, levels, contains);
            return lines.Count > 0 ? lines[0].Text : null;
        }

        internal static string Last(int mark, LogLevel levels, string contains)
        {
            var lines = Since(mark, levels, contains);
            return lines.Count > 0 ? lines[lines.Count - 1].Text : null;
        }

        internal static int ForeignMark
        {
            get
            {
                lock (Gate)
                {
                    return Foreign.Count;
                }
            }
        }

        // Warnings and errors another log source wrote since ForeignMark was index (source name as in the log file).
        internal static List<string> ForeignSince(int index, string source)
        {
            var result = new List<string>();
            lock (Gate)
            {
                for (var i = Mathf.Max(0, index); i < Foreign.Count; i++)
                {
                    if (Foreign[i].Text.StartsWith(source + ": ", StringComparison.Ordinal))
                    {
                        result.Add(Foreign[i].Text);
                    }
                }
            }
            return result;
        }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null || eventArgs.Source == null)
                {
                    return;
                }
                if (eventArgs.Source.SourceName != ModInfo.Name)
                {
                    if ((eventArgs.Level & (Wrn | Err)) != 0)
                    {
                        var theirs = eventArgs.Data as string ?? eventArgs.Data?.ToString() ?? "";
                        if (!theirs.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                        {
                            lock (Gate)
                            {
                                if (Foreign.Count < KeepAlerts)
                                {
                                    Foreign.Add(new LogLine { Level = eventArgs.Level, Text = eventArgs.Source.SourceName + ": " + theirs, At = -1f });
                                }
                            }
                        }
                    }
                    return;
                }
                var text = eventArgs.Data as string ?? eventArgs.Data?.ToString();
                if (text == null || text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                {
                    return;
                }
                float at;
                try
                {
                    at = Time.fixedTime;
                }
                catch (Exception)
                {
                    at = -1f; // not the main thread
                }
                lock (Gate)
                {
                    if (Lines.Count >= Keep)
                    {
                        Lines.RemoveRange(0, Drop);
                        _base += Drop;
                    }
                    var line = new LogLine { Level = eventArgs.Level, Text = text, At = at };
                    Lines.Add(line);
                    if ((eventArgs.Level & (Wrn | Err)) != 0 && Alerts.Count < KeepAlerts)
                    {
                        Alerts.Add(line);
                    }
                }
            }
            catch (Exception)
            {
                // Me never throw inside the logger.
            }
        }

        public void Dispose()
        {
        }
    }

    // One hit: as the attack sent it (Character.Damage), and what the target's owner did with the wire copy.
    private sealed class HitRec
    {
        internal Character Target;
        internal float At;
        internal bool FromPlayer;      // attacker = local player
        internal bool Air;             // local player airborne when it hit
        internal float Damage;         // total damage as sent
        internal float Push;
        internal float Stagger;        // m_staggerMultiplier
        internal float Backstab;       // m_backstabBonus
        internal Skills.SkillType Skill;
        internal bool Wire;            // owner got it (RPC_Damage ran here)
        internal float WireDamage;
        internal float WirePush;
        internal float WireStagger;
        internal bool WireFromPlayer;
        internal float Shown = -1f;    // damage number (total at ApplyDamage, after resistances), -1 = not applied
        internal float StaggerDamage = -1f; // what it adds to the stagger bar
        internal float BarBefore = -1f;
        internal float BarAfter = -1f;
        internal float PushAfter = -1f;     // target's knockback speed right after
        internal float BackstabBefore;
        internal bool Backstabbed;          // vanilla backstab branch ran (m_backstabTime changed)
        internal float HealthBefore;
        internal float HealthAfter = -1f;
        internal float SkillBefore = -1f;   // HitTap.WatchSkill level + accumulator before the hit, -1 = none watched
        internal float SkillAfter = -1f;    // same when Character.Damage returned
    }

    // One melee hit event of the local player (Attack.DoMeleeAttack), hit or miss.
    private sealed class SwingRec
    {
        internal Attack Attack;
        internal ItemDrop.ItemData Weapon; // weapon striking this event (Dual Wielding put the off hand in first)
        internal float At;
        internal float Pitch;              // degrees under level
        internal bool Air;
    }

    private static class HitTap
    {
        private static Harmony _harmony;
        private static HitRec _open;

        internal static readonly List<HitRec> Hits = new List<HitRec>();
        internal static readonly List<SwingRec> Swings = new List<SwingRec>();
        internal static readonly List<Character> Staggered = new List<Character>();
        internal static int Flashes;

        // Foe me put in front of every melee event of the local player (hit sure, whatever the swing's timing).
        internal static Character Present;
        internal static float PresentGap = 0.5f;

        // Skill me read before and after every hit (xp another mod gives inside the hit).
        internal static Skills.Skill WatchSkill;

        internal static bool Running => _harmony != null;

        internal static void Start()
        {
            Stop();
            Clear();
            var h = new Harmony(ModInfo.Guid + ".selftest.hits");
            try
            {
                var self = typeof(HitTap);
                h.Patch(AccessTools.Method(typeof(Character), nameof(Character.Damage)),
                    prefix: new HarmonyMethod(self, nameof(DamagePre)), finalizer: new HarmonyMethod(self, nameof(DamageEnd)));
                h.Patch(AccessTools.Method(typeof(Character), nameof(Character.RPC_Damage)),
                    prefix: new HarmonyMethod(self, nameof(WirePre)), postfix: new HarmonyMethod(self, nameof(WirePost)));
                h.Patch(AccessTools.Method(typeof(Character), nameof(Character.ApplyDamage)),
                    prefix: new HarmonyMethod(self, nameof(ApplyPre)));
                h.Patch(AccessTools.Method(typeof(Character), nameof(Character.RPC_Stagger)),
                    prefix: new HarmonyMethod(self, nameof(StaggerPre)));
                h.Patch(AccessTools.Method(typeof(Attack), nameof(Attack.DoMeleeAttack)),
                    prefix: new HarmonyMethod(self, nameof(SwingPre)) { priority = Priority.Last });
                h.Patch(AccessTools.Method(typeof(Hud), nameof(Hud.StaminaBarEmptyFlash)),
                    prefix: new HarmonyMethod(self, nameof(FlashPre)));
            }
            catch (Exception)
            {
                h.UnpatchSelf();
                throw;
            }
            _harmony = h;
        }

        internal static void Stop()
        {
            Present = null;
            PresentGap = 0.5f;
            WatchSkill = null;
            _open = null;
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
        }

        internal static void Clear()
        {
            Hits.Clear();
            Swings.Clear();
            Staggered.Clear();
            Flashes = 0;
        }

        // Hits of the local player on this target, in order.
        internal static List<HitRec> OnTarget(Character target, int from = 0)
        {
            var list = new List<HitRec>();
            for (var i = from; i < Hits.Count; i++)
            {
                if (Hits[i].FromPlayer && ReferenceEquals(Hits[i].Target, target))
                {
                    list.Add(Hits[i]);
                }
            }
            return list;
        }

        // Melee hit events of one attack clone, in order.
        internal static List<SwingRec> Of(Attack attack)
        {
            var list = new List<SwingRec>();
            foreach (var s in Swings)
            {
                if (ReferenceEquals(s.Attack, attack))
                {
                    list.Add(s);
                }
            }
            return list;
        }

        private static void DamagePre(Character __instance, HitData hit)
        {
            try
            {
                if (hit == null)
                {
                    return;
                }
                var p = Player.m_localPlayer;
                var rec = new HitRec
                {
                    Target = __instance,
                    At = Time.fixedTime,
                    FromPlayer = p != null && ReferenceEquals(hit.GetAttacker(), p),
                    Air = p != null && !p.IsOnGround(),
                    Damage = hit.GetTotalDamage(),
                    Push = hit.m_pushForce,
                    Stagger = hit.m_staggerMultiplier,
                    Backstab = hit.m_backstabBonus,
                    Skill = hit.m_skill,
                    BackstabBefore = __instance.m_backstabTime,
                    HealthBefore = __instance.GetHealth(),
                    SkillBefore = SkillSum(),
                };
                Hits.Add(rec);
                _open = rec;
            }
            catch (Exception e)
            {
                PatchGuard.Report("SelfTests hit tap Character.Damage", e);
            }
        }

        private static void DamageEnd()
        {
            var rec = _open;
            _open = null;
            if (rec != null)
            {
                rec.SkillAfter = SkillSum();
            }
        }

        private static float SkillSum()
        {
            var skill = WatchSkill;
            return skill != null ? skill.m_level * 1000f + skill.m_accumulator : -1f;
        }

        private static void WirePre(Character __instance, HitData hit)
        {
            try
            {
                var rec = _open;
                if (rec == null || hit == null || !ReferenceEquals(rec.Target, __instance))
                {
                    return;
                }
                rec.Wire = true;
                rec.WireDamage = hit.GetTotalDamage();
                rec.WirePush = hit.m_pushForce;
                rec.WireStagger = hit.m_staggerMultiplier;
                rec.WireFromPlayer = Player.m_localPlayer != null && ReferenceEquals(hit.GetAttacker(), Player.m_localPlayer);
            }
            catch (Exception e)
            {
                PatchGuard.Report("SelfTests hit tap Character.RPC_Damage prefix", e);
            }
        }

        private static void WirePost(Character __instance)
        {
            try
            {
                var rec = _open;
                if (rec == null || !ReferenceEquals(rec.Target, __instance))
                {
                    return;
                }
                rec.PushAfter = __instance.m_pushForce.magnitude;
                rec.BarAfter = __instance.m_staggerDamage;
                rec.Backstabbed = __instance.m_backstabTime != rec.BackstabBefore;
                rec.HealthAfter = __instance.GetHealth();
            }
            catch (Exception e)
            {
                PatchGuard.Report("SelfTests hit tap Character.RPC_Damage postfix", e);
            }
        }

        private static void ApplyPre(Character __instance, HitData hit)
        {
            try
            {
                var rec = _open;
                if (rec == null || hit == null || !ReferenceEquals(rec.Target, __instance))
                {
                    return;
                }
                rec.Shown = hit.GetTotalDamage();
                rec.StaggerDamage = hit.m_damage.GetTotalStaggerDamage() * hit.m_staggerMultiplier;
                rec.BarBefore = __instance.m_staggerDamage;
            }
            catch (Exception e)
            {
                PatchGuard.Report("SelfTests hit tap Character.ApplyDamage", e);
            }
        }

        private static void StaggerPre(Character __instance)
        {
            try
            {
                Staggered.Add(__instance);
            }
            catch (Exception e)
            {
                PatchGuard.Report("SelfTests hit tap Character.RPC_Stagger", e);
            }
        }

        private static void FlashPre()
        {
            Flashes++;
        }

        private static void SwingPre(Attack __instance)
        {
            try
            {
                var p = Player.m_localPlayer;
                if (p == null || !ReferenceEquals(__instance.m_character, p))
                {
                    return;
                }
                __instance.GetMeleeAttackDir(out var joint, out var dir);
                Swings.Add(new SwingRec
                {
                    Attack = __instance,
                    Weapon = __instance.m_weapon,
                    At = Time.fixedTime,
                    Pitch = Pitch(dir),
                    Air = !p.IsOnGround(),
                });
                var foe = Present;
                if (foe == null || foe.m_body == null)
                {
                    return;
                }
                // Same origin as vanilla DoMeleeAttack; foe's middle on the swing's line, its skin PresentGap m away.
                var origin = joint.position + Vector3.up * __instance.m_attackHeight + p.transform.right * __instance.m_attackOffset;
                var offset = Middle(foe) - foe.transform.position;
                Place(foe, origin + dir.normalized * (Girth(foe) + PresentGap) - offset);
                Physics.SyncTransforms();
            }
            catch (Exception e)
            {
                PatchGuard.Report("SelfTests hit tap Attack.DoMeleeAttack", e);
            }
        }
    }

    // Creature of a test: AI off at once (no target, no attack, no walk), Rig.Restore destroy it.
    private sealed class Foe
    {
        internal GameObject Go;
        internal Character Body;
        internal BaseAI Ai;

        internal bool Alive => Go != null && Body != null && !Body.IsDead();
    }

    // health > 0: that much health (it survive every test hit). 0 = the creature's own (stagger bar depends on it).
    private static Foe SpawnFoe(Rig rig, string prefab, int slot, float health = 0f)
    {
        var source = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
        if (source == null)
        {
            return null;
        }
        var go = Object.Instantiate(source, ParkSpot(rig, slot), Quaternion.LookRotation(rig.Forward));
        rig.Track(go);
        var foe = new Foe { Go = go, Body = go.GetComponent<Character>(), Ai = go.GetComponent<BaseAI>() };
        if (foe.Ai != null)
        {
            foe.Ai.enabled = false;
        }
        if (foe.Body == null)
        {
            return null;
        }
        if (health > 0f)
        {
            foe.Body.SetMaxHealth(health);
            foe.Body.SetHealth(health);
        }
        return foe;
    }

    // Behind the player's start spot, out of the roll's way: foes wait there between hits.
    private static Vector3 ParkSpot(Rig rig, int slot)
    {
        var right = Vector3.Cross(Vector3.up, rig.Forward);
        var pos = rig.Home - rig.Forward * 7f + right * (3f * slot - 3f);
        pos.y = ZoneSystem.instance.GetGroundHeight(pos) + 0.2f;
        return pos;
    }

    private static void Park(Rig rig, Foe foe, int slot)
    {
        if (foe != null && foe.Alive)
        {
            Place(foe.Body, ParkSpot(rig, slot));
        }
    }

    private static void Place(Character body, Vector3 pos)
    {
        var root = body.transform.root;
        if (root != null && !ReferenceEquals(root, body.transform))
        {
            root.position += pos - body.transform.position;
        }
        else
        {
            body.transform.position = pos;
        }
        var rb = body.m_body;
        if (rb != null)
        {
            rb.position = pos;
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
            }
        }
    }

    // Radius and middle of a character; safe for one with no capsule collider of its own.
    private static float Girth(Character body) => body.GetCollider() != null ? body.GetRadius() : 0.5f;

    private static Vector3 Middle(Character body) =>
        body.GetCollider() != null ? body.GetCenterPoint() : body.transform.position + Vector3.up;

    // In front of the player on the ground, gap m between the player's skin and the foe's.
    private static void PlaceAhead(Player p, Character body, Vector3 dir, float gap)
    {
        dir.y = 0f;
        var pos = p.transform.position + dir.normalized * (gap + Girth(body) + Girth(p));
        pos.y = ZoneSystem.instance.GetGroundHeight(pos);
        Place(body, pos);
        Physics.SyncTransforms();
    }

    // What the entry Debug line says the combo does next (MoveTracker.Enter).
    private static string EntryTail(MoveInfo move)
    {
        if (move.Step < 0)
        {
            return "the next attack starts a new combo.";
        }
        var next = NextStep(move);
        return next > 0 ? $"the combo continues at step {next}." : "the next attack starts the combo at its first swing.";
    }

    // The weapon's own first attack, untouched by me: name, chain and numbers of the item's attack.
    private static bool Vanilla(Attack a, Attack shared) =>
        a != null && shared != null && a.m_attackAnimation == shared.m_attackAnimation
        && a.m_attackChainLevels == shared.m_attackChainLevels && Near(a.m_damageMultiplier, shared.m_damageMultiplier)
        && Near(a.m_staggerMultiplier, shared.m_staggerMultiplier) && Near(a.m_forceMultiplier, shared.m_forceMultiplier)
        && Near(a.m_attackStamina, shared.m_attackStamina);

    private static string Fired(Attack a) =>
        a == null ? "no attack" : a.m_attackAnimation + (a.m_attackChainLevels > 1 ? a.m_currentAttackCainLevel.ToString() : "");

    // Game methods my patches sit on while me Active (Patches/*.cs, not the network ones).
    private static MethodBase[] CombatTargets() => new MethodBase[]
    {
        AccessTools.Method(typeof(Humanoid), nameof(Humanoid.StartAttack), new[] { typeof(Character), typeof(bool) }),
        AccessTools.Method(typeof(Attack), nameof(Attack.Start)),
        AccessTools.Method(typeof(Player), nameof(Player.UpdateDodge)),
        AccessTools.Method(typeof(Character), nameof(Character.Jump)),
        AccessTools.Method(typeof(ZSyncAnimation), nameof(ZSyncAnimation.RPC_SetTrigger)),
    };

    // How many of them carry a patch owned by this mod's Harmony id (5 = all on, 0 = all off).
    private static int PatchedCount()
    {
        var n = 0;
        foreach (var method in CombatTargets())
        {
            var info = method != null ? Harmony.GetPatchInfo(method) : null;
            if (info != null && info.Owners.Contains(ModInfo.Guid))
            {
                n++;
            }
        }
        return n;
    }

    private static string StateNow()
    {
        var view = FeatureRegistry.Find(ModInfo.Guid);
        return view != null ? view.Value.State : "not registered";
    }

    private static string StatusNow()
    {
        var view = FeatureRegistry.Find(ModInfo.Guid);
        return view != null ? view.Value.Status : "";
    }

    private static bool ActiveNow()
    {
        var view = FeatureRegistry.Find(ModInfo.Guid);
        return view != null && view.Value.IsActive;
    }

    // What one jump + press did (JumpPress fill it).
    private sealed class JumpRun
    {
        internal bool Jumped;        // game took the jump
        internal bool TokenAtJump;   // jump token live right after
        internal float JumpAt = -1f;
        internal float PressAt = -1f;
        internal float StartAt = -1f;
        internal float EnteredAt = -1f;
        internal float LandAt = -1f;
        internal Attack Started;     // first new attack after the press
        internal Attack Clone;       // move clone, null = no move
        internal MoveInfo Move;
        internal bool StartAir;

        internal bool IsMove => Clone != null && ReferenceEquals(Clone, Started);
    }

    // Jump in place, press pressAfter s later (how: other press than the buffered primary one), follow until the first
    // new attack is in its animation, or follow s passed since the press. Caller put and face the player first, and
    // wait for the landing after.
    private static IEnumerator JumpPress(Rig rig, JumpRun run, float pressAfter = 0.1f, float follow = 1f,
        Action<Player> how = null)
    {
        var p = rig.P;
        var before = MoveTracker.LastMove.Clone;
        var prev = p.m_currentAttack;
        p.Jump();
        run.JumpAt = Time.fixedTime;
        run.Jumped = p.m_jumpTimer == 0f;
        run.TokenAtJump = MoveTracker.JumpLive;
        if (pressAfter > 0f)
        {
            yield return WaitTicks(pressAfter);
        }
        if (how != null)
        {
            how(p);
        }
        else
        {
            Press(p);
        }
        run.PressAt = Time.fixedTime;
        while (Time.fixedTime - run.PressAt < follow)
        {
            yield return Fixed;
            var now = Time.fixedTime;
            if (run.LandAt < 0f && now - run.JumpAt >= MoveTracker.JumpGroundLock && p.IsOnGround())
            {
                run.LandAt = now;
            }
            var current = p.m_currentAttack;
            if (run.Started == null && current != null && !ReferenceEquals(current, prev))
            {
                run.Started = current;
                run.StartAt = now;
                run.StartAir = !p.IsOnGround();
            }
            if (run.Clone == null && !ReferenceEquals(MoveTracker.LastMove.Clone, before))
            {
                run.Move = MoveTracker.LastMove;
                run.Clone = run.Move.Clone;
            }
            if (run.Started != null && run.Started.m_wasInAttack)
            {
                run.EnteredAt = now;
                break;
            }
        }
    }

    // Player down, calm, and the move cooldown of these rules over: next jump or roll can be a move again.
    private static IEnumerator Settle(Rig rig, MoveRules rules = null)
    {
        var p = rig.P;
        yield return WaitLanded(p, 4f);
        yield return WaitIdle(p, 4f);
        var cooldown = rules != null ? rules.Cooldown : 0f;
        var until = Time.fixedTime + cooldown + 0.5f;
        while (Time.fixedTime < until && Time.fixedTime - MoveTracker.LastMoveAt < cooldown + 0.06f)
        {
            yield return Fixed;
        }
        Put(p, rig.Home);
        Face(p, rig.Forward);
        yield return Fixed;
    }

    private static void PressSecondary(Player p) => p.m_queuedSecondAttackTimer = 0.5f;

    private static void Look(Player p, float pitchDown)
    {
        p.m_lookPitch = pitchDown;
        p.SetMouseLook(Vector2.zero);
    }

    // Me wait until test() or seconds pass (physics ticks). ok.Value = it came true.
    private static IEnumerator WaitFor(Func<bool> test, float seconds, Box<bool> ok = null)
    {
        var until = Time.fixedTime + seconds;
        var done = test();
        while (!done && Time.fixedTime < until)
        {
            yield return Fixed;
            done = test();
        }
        if (ok != null)
        {
            ok.Value = done;
        }
    }

    // Same in real time (network waits: physics may stall while the game loads).
    private static IEnumerator WaitReal(Func<bool> test, float seconds, Box<bool> ok = null)
    {
        var until = Time.realtimeSinceStartup + seconds;
        var done = test();
        while (!done && Time.realtimeSinceStartup < until)
        {
            yield return null;
            done = test();
        }
        if (ok != null)
        {
            ok.Value = done;
        }
    }
}
#endif
