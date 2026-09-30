using System.Collections.Generic;
using System.Globalization;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.ShieldsTowerWallMod;

// Me = watch the local player's running bash (design 2.6, 2.7). Local player only, dropped when Player.m_localPlayer
// change. Per tick (UpdateBlock postfix): one reference compare when no bash is running.
//   Cooldown (BashCooldown, decision 30): StartAttack prefix refuse a bash that would start less than BashCooldown s
//       after the last bash start (held button retry every tick: the bash start as soon as the time is up). Vanilla
//       input buffer keep a press only 0.5 s: KeepPress hold a press made once the swing is over until the time is up,
//       else a press 1.1-1.5 s after the start (swing over, guard up) would vanish. A press during the swing keep the
//       vanilla 0.5 s. A start that never reached its attack state (watchdog A) do not count.
//   Swing speed (BashAnimationSpeed, decision 32): BashSpeed, begun here at each start, told when the attack state is
//       reached, ended with the bash.
//   (A) No attack state: from the first bash start that did not reach an attack-tagged state (a held button start a
//       new clone every FixedUpdate when no cooldown: time counted across them), StartTimeout s AND StartFrames
//       rendered frames without InAttack() while not staggering, pushed, dead or in a minor action = ResetTrigger on
//       local animator, bash cancelled, next option. Frames too: after a hitch Unity run many catch-up FixedUpdates in
//       a row with no animator update between, so time alone could pass before animator even saw the trigger.
//   (B) No hit: bash reached its attack state, then ended (IsDone) without its Hit event, not aborted by a stagger,
//       player not dead, still the current attack (not put away) = next option. Held button or queued press: vanilla
//       StartAttack may stop the ended bash and start the next one before this tick's verdict (Player.FixedUpdate and
//       Character.CustomFixedUpdate have no fixed order): OnStarted then give the verdict of the one it replaced.
//   (C) No end: bash still not done MaxAttackSeconds (divided by the swing speed when slower than 1) after it reached
//       its attack state (a clip that never leave, like a looping one: only attack_abort get out, and Attack.Stop fire
//       it only for a looping attack) = Stop, fire attack_abort (vanilla's own way out), next option. Safety net:
//       Custom already refuse held attacks.
//   One hit per bash: Attack.OnAttackTrigger prefix let only the first Hit event of our clone through (clips with two
//       Hit events through Custom): one bash = one hit for one stamina cost.
// Cancel (design 2.6, decision 14): a bash whose tower is about to be reverted is stopped like UnequipItem do (Stop,
// previous attack, current null), else its Hit event would land with vanilla tower data (10 true, unblockable). Its
// swing speed go back to the clip's own.
internal static class BashWatch
{
    internal const float StartTimeout = 0.3f;
    internal const int StartFrames = 3;

    // Longest vanilla player attack clip is about 2.2 s (Atgeir360Attack, dump): 3 s leave room for every real swing
    // at normal speed; a slowed swing get 3 s / speed.
    internal const float MaxAttackSeconds = 3f;

    // Vanilla input buffer of one attack press (Player.PlayerAttackInput).
    internal const float QueuedPress = 0.5f;

    private const string AbortTrigger = "attack_abort";

    private static Player _player;
    private static Attack _running;
    private static BashAnimationKind _mode;
    private static string _trigger;
    private static float _speed = 1f;
    private static bool _reached;
    private static float _reachedAt;
    private static float _pendingSince;
    private static int _pendingFrame;
    private static int _hits;
    private static Player _startedBy;
    private static float _startedAt = float.MinValue;

    // Clone of the running bash (null = none). OnAttackTrigger prefix compare against it.
    internal static Attack Running => _running;

    // Hit events of the running bash so far (self tests).
    internal static int Hits => _hits;

#if DEBUG
    // Self tests only: every Hit event of our bash clones (let through or skipped), when the first one of the running
    // bash came, and every bash start (Time.time). Never reset: test read them before and after a swing.
    internal static int DebugHitEvents;
    internal static int DebugSkippedEvents;
    internal static float DebugFirstHitTime;
    internal static readonly List<float> DebugStarts = new List<float>();
#endif

    // StartAttack prefix: true = this player's next bash must wait (BashCooldown of the applied rules, from the last
    // bash start). Caller checked the tower in hand. Two compares when no cooldown runs.
    internal static bool InCooldown(Player player) => CooldownLeft(player) > 0f;

    // Seconds left before this player may bash again (0 = may).
    internal static float CooldownLeft(Player player)
    {
        if (!ReferenceEquals(player, _startedBy))
        {
            return 0f;
        }
        var rules = TowerSync.Applied;
        if (rules == null || rules.BashCooldown <= 0f)
        {
            return 0f;
        }
        return Mathf.Max(0f, _startedAt + rules.BashCooldown - Time.time);
    }

    // StartAttack prefix just refused this player's bash for the cooldown (vanilla PlayerAttackInput call it each tick
    // while its press timer run). Press buffered and swing over = timer kept above 0 until the cooldown end (two ticks
    // of margin, never above vanilla's 0.5 s): the bash start at the first tick allowed. Refused then for another
    // reason (stagger, push) = the press run out two ticks later, as the vanilla press would have long before.
    // Cheap test first: no press = one float compare.
    internal static void KeepPress(Player player)
    {
        var queued = player.m_queuedAttackTimer;
        if (queued <= 0f || player.InAttack())
        {
            return;
        }
        var keep = Mathf.Min(QueuedPress, CooldownLeft(player) + 2f * Time.fixedDeltaTime);
        if (queued < keep)
        {
            player.m_queuedAttackTimer = keep;
        }
    }

    // StartAttack postfix: local player just started a bash (clone now m_currentAttack).
    internal static void OnStarted(Player player, Attack attack)
    {
        // Option this clone was built with: read before a verdict below move the option on.
        var mode = BashAttack.Mode;
        if (!ReferenceEquals(player, _player) || _running == null || _reached)
        {
            _pendingSince = Time.time;
            _pendingFrame = Time.frameCount;
        }
        // (B) for the bash this start replaced, when it reached its attack state and was stopped by this StartAttack
        // (m_previousAttack) before the UpdateBlock verdict: vanilla start a new attack only once InAttack() is false
        // (no chain on a bash), so its clip ended.
        string failed = null;
        var failedMode = _mode;
        if (_running != null && _reached && ReferenceEquals(player, _player) && !ReferenceEquals(_running, attack)
            && ReferenceEquals(player.m_previousAttack, _running) && _hits == 0 && !_running.m_abortAttack
            && !player.IsStaggering() && !player.IsDead())
        {
            failed = NoHitReason(failedMode);
        }
        var rules = TowerSync.Applied;
        _player = player;
        _running = attack;
        _reached = false;
        _hits = 0;
        _mode = mode;
        _trigger = attack.m_attackAnimation;
        _speed = rules != null ? rules.BashAnimationSpeed : 1f;
        _startedBy = player;
        _startedAt = Time.time;
#if DEBUG
        DebugStarts.Add(Time.time);
#endif
        BashSpeed.Begin(player, attack, _speed);
        if (failed != null)
        {
            BashAttack.FallBack(failedMode, failed);
        }
    }

    // OnAttackTrigger prefix, only for the running clone. True = let this Hit event run.
    internal static bool OnHitEvent()
    {
        _hits++;
#if DEBUG
        DebugHitEvents++;
        if (_hits == 1)
        {
            DebugFirstHitTime = Time.time;
        }
        else
        {
            DebugSkippedEvents++;
        }
#endif
        return _hits <= 1;
    }

    // UpdateBlock postfix, local player.
    internal static void Tick(Player player)
    {
        if (_running == null)
        {
            return;
        }
        if (!ReferenceEquals(player, _player))
        {
            BashSpeed.Reset(); // old player object gone with its animator
            Forget();
            return;
        }
        if (!_reached)
        {
            if (!ReferenceEquals(player.m_currentAttack, _running))
            {
                End(player); // put away, replaced or cancelled before it started: no verdict
                return;
            }
            if (player.InAttack())
            {
                _reached = true;
                _reachedAt = Time.time;
                BashSpeed.OnReached(player);
                return;
            }
            if (Time.time - _pendingSince <= StartTimeout || Time.frameCount - _pendingFrame < StartFrames)
            {
                return;
            }
            if (player.IsStaggering() || player.IsKnockedBack() || player.IsDead() || player.InMinorAction())
            {
                _pendingSince = Time.time; // no fair chance: wait again
                _pendingFrame = Time.frameCount;
                return;
            }
            FailNoAttackState(player);
            return;
        }
        if (!_running.IsDone())
        {
            if (Time.time - _reachedAt > MaxAttackSeconds / Mathf.Min(1f, _speed))
            {
                FailNoEnd(player);
            }
            return;
        }
        var noHit = _hits == 0 && ReferenceEquals(player.m_currentAttack, _running) && !_running.m_abortAttack
                    && !player.IsStaggering() && !player.IsDead();
        var mode = _mode;
        End(player); // done: out of the clip (speed already vanilla), or stopped mid-clip by UnequipItem (put back)
        if (noHit)
        {
            BashAttack.FallBack(mode, NoHitReason(mode));
        }
    }

    private static string NoHitReason(BashAnimationKind mode) => $"{mode}: the swing ends before its hit";

    // Rules apply: cancel the local player's bash when its tower leave the list (catalog already rebuilt).
    internal static void CancelDropped(Player player)
    {
        Cancel(player, all: false);
    }

    // Deactivation: cancel the local player's bash when it use a tower (any snapshot item).
    internal static void CancelAll(Player player)
    {
        Cancel(player, all: true);
    }

    // Deactivation state step: forget everything, cooldown too.
    internal static void Reset()
    {
        BashSpeed.Reset();
        Forget();
        _player = null;
        _startedBy = null;
        _startedAt = float.MinValue;
    }

    private static void Cancel(Player player, bool all)
    {
        if (player == null)
        {
            Reset();
            return;
        }
        var attack = player.m_currentAttack;
        var weapon = attack != null ? attack.GetWeapon() : null;
        if (weapon != null && TowerCatalog.SnapshotOf(weapon) != null && (all || TowerCatalog.TowerOf(weapon) == null))
        {
            Stop(player, attack);
        }
        if (all || _running == null || !ReferenceEquals(player.m_currentAttack, _running))
        {
            End(player);
        }
    }

    // Same as Humanoid.UnequipItem do for a weapon in use: its later Hit event never reach it. Clip keep playing:
    // its own speed back.
    private static void Stop(Player player, Attack attack)
    {
        attack.Stop();
        player.m_previousAttack = attack;
        player.m_currentAttack = null;
        BashSpeed.End(player);
    }

    private static void FailNoAttackState(Player player)
    {
        var mode = _mode;
        var trigger = _trigger;
        if (!string.IsNullOrEmpty(trigger) && player.m_animator != null)
        {
            player.m_animator.ResetTrigger(trigger);
        }
        if (ReferenceEquals(player.m_currentAttack, _running))
        {
            Stop(player, _running);
        }
        End(player);
        _startedAt = float.MinValue; // never swung: no cooldown, next option at once
        BashAttack.FallBack(mode, $"{mode}: the animation '{trigger}' does not start an attack");
    }

    // (C) Bash still running MaxAttackSeconds (/ speed) after its attack state: stop it like UnequipItem do, then
    // attack_abort on the synced animator (every game that play the clip leave a looping state on it). Not the current
    // attack any more (nothing update it) or player dead: dropped, no verdict.
    private static void FailNoEnd(Player player)
    {
        var mode = _mode;
        var trigger = _trigger;
        var limit = MaxAttackSeconds / Mathf.Min(1f, _speed);
        var current = ReferenceEquals(player.m_currentAttack, _running) && !player.IsDead();
        if (current)
        {
            Stop(player, _running);
            if (player.m_zanim != null)
            {
                player.m_zanim.SetTrigger(AbortTrigger);
            }
        }
        End(player);
        if (current)
        {
            BashAttack.FallBack(mode, $"{mode}: the animation '{trigger}' did not end within "
                                      + $"{limit.ToString("0.#", CultureInfo.InvariantCulture)} seconds");
        }
    }

    // Bash over for me: its speed back to the clip's own (when still in the clip), state dropped.
    private static void End(Player player)
    {
        BashSpeed.End(player);
        Forget();
    }

    private static void Forget()
    {
        _running = null;
        _reached = false;
        _hits = 0;
        _speed = 1f;
    }
}
