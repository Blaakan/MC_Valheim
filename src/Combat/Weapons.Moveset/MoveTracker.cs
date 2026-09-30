using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsMovesetMod;

// One move as edited in Attack.Start prefix, and what the watch need after (design 2.7). Base name, chain level
// count and m_maxYAngle are the clone's AS IT ARRIVED in our prefix (after Dual Wielding's conversion), never the
// weapon's SharedData: a Dual Wielding axe pair chain is its template's (dualaxes, 4 levels).
internal struct MoveInfo
{
    internal Attack Clone;
    internal ItemDrop.ItemData Weapon;
    internal MoveKind Kind;
    internal WeaponFamily Family;
    internal string Trigger;
    internal string BaseName;
    internal int ChainLevels;
    internal int Step;            // step of own chain the move play (-1: not a step, combo restart after it)
    internal float MaxYBefore;
    internal bool AimEdited;      // jump attack pitched: put m_maxYAngle back on landing
    internal MoveRules Rules;
    internal float RollAge;       // roll attack after the roll: seconds from roll end to attack start (-1 = cut)
    internal float RollTime;      // roll attack inside the roll: seconds from roll start to attack start (-1 = not)
    internal bool Cut;            // roll attack started inside the roll (cut into it, roll gate opened)
    internal int RollSeq;         // roll counter at start: other number while starting = a NEW roll came
    internal FlowResult Flow;     // how the roll attack left the roll (None for jump attack)
    internal StateSource Source;  // where the cross-faded attack state came from
}

// Me = the move logic of the local player (design 2.1, 2.3, 2.4, 2.7, 2.9).
//   Tokens: jump token (Character.Jump from input that reached ForceJump) live until landing. Roll token: inside the
//   roll (m_inDodge false -> true, RollActive) and after it (true -> false, RollEndAt), only if no attack started in
//   that roll. Any successful attack start eat all: only FIRST attack of a jump or roll.
//   Decide (StartAttack prefix): move or not. Roll attack inside the roll (from FlowStart, i-frames over + margin,
//   weapon can flow): open vanilla's InDodge gate for this call (finalizer close it). Roll attack after a roll with
//   roll animation: only while layer 0 still blend out of it (never roll, stand-up, then roll attack). Attack.Start
//   prefix: edit clone (MoveEdit), or refuse an attack that got in through the gate but is no roll attack that can
//   flow. StartAttack postfix: roll attack = cross-fade out of the roll (RollFlow); watch.
//   Watch (every tick from UpdateDodge postfix): starting phase = refuse restarts until animation enter its state;
//   entered = cooldown start, combo continue from move step; 0.5 s without entering = cancel, normal swing instead,
//   warn (clock wait while the cut roll still in its dodge state). Jump attack: aim pitch back on first tick on ground.
//   Learning: first attack of a trigger with no learned state = me note the attack state Animator go to (RollFlow).
//   Roll start: roll animation's attack state made known (RollFlow's controller probe), so first roll attack flow too.
// All times Time.fixedTime. Only local player; dedicated server never get here (no local player).
internal static class MoveTracker
{
    // Vanilla ground-contact lock after a jump (Character.OnCollisionStay ignore ground while m_jumpTimer < 0.1).
    internal const float JumpGroundLock = 0.1f;

    // A2 start check: move animation must enter its state within this.
    internal const float StartDeadline = 0.5f;

    // Failsafe for the restart refusal: Tick cancel at StartDeadline (same tick, after attack input), so refusal
    // never need longer; if Tick ever stop, refusal still end by itself.
    private const float RefuseLimit = StartDeadline + 0.1f;

    // Jump in restart gap (attack started, Animator not in attack state yet) belong to that attack: vanilla Jump need
    // !InAttack() and the gap is short.
    private const float StartingAttackAge = 0.5f;

    // Learning give up after this (trigger alone after a roll wait for the roll's blend, about 0.2 s, then entry).
    private const float LearnLimit = 1.5f;

    // Cut wait this long after roll's i-frames end. Other peers read i-frames from ZDO (s_dodgeinv, owner write it in
    // UpdateDodge): they see the end only after ZDO send + latency. Hard rule "roll i-frames never reach the attack"
    // must hold on their screen too (their game check hits of creatures they own), also with FlowStart 0.
    internal const float IframeMargin = 0.2f;

    // fixedTime steps are floats: 0.85 s after roll start may read 0.84999.
    private const float TimeSlack = 0.0001f;

    private const float None = float.NegativeInfinity;

    // Tokens and player.
    private static Player _player;
    private static bool _prevInDodge;
    private static bool _jumpLive;
    private static float _jumpAt = None;
    private static bool _rollActive;           // inside a roll, no attack started in it yet
    private static float _rollStartAt = None;
    private static int _rollSeq;               // +1 each roll start
    private static float _iframesEndAt = None; // tick me saw roll's i-frames off (None = not yet)
    private static float _rollEndAt = None;
    private static bool _rollEndInDodge;       // roll ended out of vanilla dodge state (dash mods: no)
    private static float _lastMoveAt = None;

    // Current StartAttack call (finalizer clear).
    private static MoveKind _pendingKind;
    private static MoveRules _pendingRules;
    private static float _pendingRollAge;
    private static float _pendingRollTime = -1f;
    private static bool _pendingCut;
    private static Player _gatePlayer;         // m_inDodge me cleared for this call (finalizer put true back)
    private static MoveKind _cooledDown;
    private static float _cooldownLeft;
    private static MoveKind _staminaSkip;
    private static bool _hasApplied;
    private static MoveInfo _applied;

    // Move watch.
    private static bool _watching;
    private static bool _starting;
    private static bool _aimPending;
    private static bool _graceTick;
    private static bool _refusedLogged;
    private static float _startAt;             // real start (entry delay)
    private static float _clockAt;             // start check clock (wait while cut roll still in dodge state)
    private static Player _watchPlayer;
    private static MoveInfo _watch;

    // Learning (design 2.9).
    private static Attack _learnAttack;
    private static string _learnTrigger;
    private static int _learnFrom;
    private static float _learnAt;

    // Start check warned once per (move, family) per game session.
    private static readonly bool[,] Warned = new bool[Moves.Count, Families.Count];

#if DEBUG
    // Self tests read these.
    internal static bool JumpLive => _jumpLive;
    internal static bool RollActive => _rollActive;
    internal static float RollStartAt => _rollStartAt;
    internal static int RollSeq => _rollSeq;
    internal static float RollEndAt => _rollEndAt;
    internal static float LastMoveAt => _lastMoveAt;
    internal static bool Watching => _watching;
    internal static bool WatchStarting => _watching && _starting;
    internal static float WatchClockAt => _clockAt;
    internal static MoveInfo Watched => _watch;
    internal static bool Learning => _learnAttack != null;
    internal static MoveInfo LastMove;           // last move started (kept after watch end)
    internal static float LastStartAt = -1f;     // fixedTime the last move started
    internal static float LastEntryDelay = -1f;  // seconds from last move start to its entry, -1 = not entered
    internal static int RefusedRestarts;
    internal static int TriggerResets;
    internal static int StartFailures;
    internal static int StartWarnings;           // start check warnings really logged
    internal static int GateRefusals;            // attacks refused inside a roll (no roll attack)
    internal static int GateSkips;               // roll gate kept shut: weapon cannot flow (no clone made)
    internal static float IframesEndAt => _iframesEndAt;

    // Self test: forget which (move, family) already warned, so watchdog test see its one warning.
    internal static void ResetWarnings()
    {
        System.Array.Clear(Warned, 0, Warned.Length);
        StartWarnings = 0;
    }
#endif

    // Cheap test before Decide (StartAttack prefix): any token of any age.
    internal static bool AnyToken => _jumpLive || _rollActive || _rollEndAt > None;

    // Attack.Start prefix cheap exit.
    internal static bool HasPending => _pendingKind != MoveKind.None;

    // Pure (self test hammer it). Move for this attack start, or None. Jump win when both tokens live. Cooldown
    // block = None with cooledDown/cooldownLeft set (Debug line when the normal swing start).
    //   rollTime: seconds since the roll started while inside it (token live, vanilla dodge state), negative = not.
    //   iframes: roll's i-frames still on (m_dodgeInvincible, or ended less than IframeMargin ago). Never cut then,
    //   whatever FlowStart.
    //   rollAge / sinceLastMove: seconds (+infinity = none).
    //   flowOver: roll had roll animation and layer 0 done blending out of it (stand-up over). No roll attack after
    //   that, whatever Window: it would play roll, stand-up, then swing (user feedback 2026-09-30). Dash = false.
    internal static MoveKind Decide(MoveRules rules, bool secondary, bool inAttack, bool swimming, bool attached,
        bool onGround, bool jumpLive, float rollTime, bool iframes, float rollAge, bool flowOver, float sinceLastMove,
        bool gco, out MoveKind cooledDown, out float cooldownLeft)
    {
        cooledDown = MoveKind.None;
        cooldownLeft = 0f;
        // Secondary keep vanilla role; InAttack = queued chain stay chain; water and seats stay vanilla.
        if (rules == null || secondary || inAttack || swimming || attached)
        {
            return MoveKind.None;
        }
        MoveKind kind;
        if (jumpLive && !onGround)
        {
            // Jump own the air attack. Jump attack off (setting, or GCO own jump + primary) = normal swing, never a
            // roll attack from a roll just before the jump. Roll that end in air (ledge, no jump) still go below.
            if (!rules.JumpAttack || gco)
            {
                return MoveKind.None;
            }
            kind = MoveKind.Jump;
        }
        else if (rules.RollAttack
                 && (CanCut(rules, rollTime, iframes) || (rollAge >= 0f && rollAge <= rules.Window && !flowOver)))
        {
            kind = MoveKind.Roll;
        }
        else
        {
            return MoveKind.None;
        }
        if (sinceLastMove < rules.Cooldown)
        {
            cooledDown = kind;
            cooldownLeft = rules.Cooldown - sinceLastMove;
            return MoveKind.None;
        }
        return kind;
    }

    // Inside the roll, at or past the cut point, i-frames over (hard rule: roll invulnerability never reach attack).
    private static bool CanCut(MoveRules rules, float rollTime, bool iframes) =>
        rollTime >= 0f && !iframes && rollTime + TimeSlack >= rules.FlowStart;

    // OnActivated / OnDeactivated: forget everything. Move still starting: trigger reset (only if its Player alive
    // and still the local one), aim put back. Roll gate closed. Warned flags stay (game session).
    internal static void Reset()
    {
        DropWatch(resetTrigger: true);
        ClearTokens();
        StopLearn();
        _player = null;
        _prevInDodge = false;
        ClearCall();
    }

    // Humanoid.StartAttack prefix: restart refusal test. Move clone still current, not done, not entered, clock young.
    internal static bool IsStarting(Humanoid character)
    {
        if (!_watching || !_starting)
        {
            return false;
        }
        var clone = _watch.Clone;
        return clone != null && ReferenceEquals(character.m_currentAttack, clone) && !clone.m_attackDone
               && !clone.m_wasInAttack && Time.fixedTime - _clockAt < RefuseLimit;
    }

    // Our prefix refused a restart (skip original, false). Debug once per move.
    internal static void OnRefused()
    {
#if DEBUG
        RefusedRestarts++;
#endif
        if (_refusedLogged)
        {
            return;
        }
        _refusedLogged = true;
        Log.Debug($"Refused an attack restart while {_watch.Trigger} was starting.");
    }

    // Humanoid.StartAttack prefix (local player, not refused): decide move of this call. Roll attack inside the roll:
    // vanilla StartAttack refuse InDodge, so me clear m_inDodge for this call only (finalizer set it back; UpdateDodge
    // recompute it from Animator at tick end anyway). Nothing between read it: Attack.Update and movement already ran
    // this tick (tick order C M H P, design 1.1).
    internal static void BeforeStart(Player player, bool secondary)
    {
        ClearCall();
        if (!AnyToken || !ReferenceEquals(player, _player))
        {
            return;
        }
        var now = Time.fixedTime;
        var rules = ServerRules.Current;
        // GCO lookup only when a jump attack is in question (cached after first look).
        var gco = _jumpLive && rules.JumpAttack && Compat.GcoLoaded;
        var rollAge = now - _rollEndAt;
        // After a vanilla roll: roll attack only while layer 0 still blend out of the dodge state (it flow from there).
        var flowOver = _rollEndInDodge && !InRollState(player);
        // Cut only in vanilla dodge state (dash mods without one: after the roll only).
        var inRoll = _rollActive && player.m_inDodge && player.GetNextOrCurrentAnimHash() == RollFlow.DodgeTag;
        var rollTime = inRoll ? now - _rollStartAt : -1f;
        // I-frames as other peers see them: on, not seen off yet by Tick, or off for less than IframeMargin.
        var iframes = player.m_dodgeInvincible || _iframesEndAt == None
                      || now - _iframesEndAt + TimeSlack < IframeMargin;
        var kind = Decide(rules, secondary, player.InAttack(), player.IsSwimming(), player.IsAttached(),
            player.IsOnGround(), _jumpLive, rollTime, iframes, rollAge, flowOver, now - _lastMoveAt, gco,
            out _cooledDown, out _cooldownLeft);
        if (kind == MoveKind.Roll && inRoll && !CanFlow(player, rules))
        {
            // Gate stay shut: vanilla refuse (InDodge), press stay buffered, attack start when roll end. No clone and
            // no sibling-mod work each tick for a weapon that could never become a flowing roll attack.
#if DEBUG
            GateSkips++;
#endif
            kind = MoveKind.None;
        }
        _pendingKind = kind;
        _pendingRules = rules;
        _pendingRollAge = rollAge;
        _pendingRollTime = rollTime;
        if (kind == MoveKind.Roll && inRoll)
        {
            _pendingCut = true;
            _gatePlayer = player;
            player.m_inDodge = false;
        }
    }

    // Layer 0 in dodge state (also its blend out) or blending into one: game's per-tick cached tags, no Animator call.
    private static bool InRollState(Player player) =>
        player.GetCurrentAnimHash() == RollFlow.DodgeTag || player.GetNextAnimHash() == RollFlow.DodgeTag;

    // Cheap look before the roll gate (only from the cut point to roll end, a few ticks per roll): can current weapon
    // become a roll attack that flow (eligible, animation on, attack state known)? Weapon's own attack = clone before
    // Attack.Start prefixes. Weapon in both hands (Dual Wielding pair): its prefix change the family inside
    // Attack.Start, so yes here and OnAttackStart decide on the real clone (backstop).
    private static bool CanFlow(Player player, MoveRules rules)
    {
        var weapon = player.GetCurrentWeapon();
        if (weapon == null || weapon.m_shared == null)
        {
            return false;
        }
        if (ReferenceEquals(weapon, player.m_rightItem) && PairHand(weapon) && PairHand(player.m_leftItem))
        {
            return true;
        }
        if (!Families.Eligible(weapon.m_shared.m_attack, weapon, out var family))
        {
            return false;
        }
        var trigger = MoveTriggers.Resolve(rules, MoveKind.Roll, family);
        return trigger != null && RollFlow.HasStateFor(AnimatorOf(player), trigger);
    }

    // Roll start: make the attack state of the roll animation known now (RollFlow ask the controller probe on its first
    // look, a few ms once per animation and session), not on the cut tick 0.85 s later. Own weapon: its family's
    // animation. Weapon in each hand (Dual Wielding pair, family set inside Attack.Start): both pair rows' animations
    // (its default templates); another template is probed at the cut. After the first roll: lookups only.
    private static void Prepare(Player player)
    {
        var rules = ServerRules.Current;
        if (rules == null || !rules.RollAttack)
        {
            return;
        }
        var weapon = player.GetCurrentWeapon();
        if (weapon == null || weapon.m_shared == null)
        {
            return;
        }
        var animator = AnimatorOf(player);
        if (animator == null)
        {
            return;
        }
        if (ReferenceEquals(weapon, player.m_rightItem) && PairHand(weapon) && PairHand(player.m_leftItem))
        {
            Know(animator, MoveTriggers.Resolve(rules, MoveKind.Roll, WeaponFamily.DualAxes));
            Know(animator, MoveTriggers.Resolve(rules, MoveKind.Roll, WeaponFamily.DualKnives));
            return;
        }
        if (Families.Eligible(weapon.m_shared.m_attack, weapon, out var family))
        {
            Know(animator, MoveTriggers.Resolve(rules, MoveKind.Roll, family));
        }
    }

    private static void Know(Animator animator, string trigger)
    {
        if (trigger != null)
        {
            RollFlow.HasStateFor(animator, trigger);
        }
    }

    // Weapon (not torch) in a hand. Vanilla never has one in each hand: only dual-wield mods do.
    private static bool PairHand(ItemDrop.ItemData item) =>
        item != null && item.m_shared != null && item.IsWeapon()
        && item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Torch;

    // Attack.Start prefix (after Dual Wielding's), only while a move is pending: gates, trigger, edit. True = refuse
    // this start: it came in through our roll gate but is no roll attack that can flow (vanilla would refuse it:
    // InDodge).
    internal static bool OnAttackStart(Attack clone, Humanoid character, ItemDrop.ItemData weapon)
    {
        var kind = _pendingKind;
        var rules = _pendingRules;
        var cut = _pendingCut;
        _pendingKind = MoveKind.None; // one Attack.Start per StartAttack call
        _pendingCut = false;
        if (kind == MoveKind.None || rules == null || clone == null || !ReferenceEquals(character, _player))
        {
            return Refuse(cut);
        }
        if (!Families.Eligible(clone, weapon, out var family))
        {
            return Refuse(cut);
        }
        var trigger = MoveTriggers.Resolve(rules, kind, family);
        if (trigger == null)
        {
            return Refuse(cut); // Off: normal swing, no bonus (inside the roll: when the roll end)
        }
        if (cut && !RollFlow.HasStateFor(AnimatorOf(_player), trigger))
        {
            // No attack state to cross-fade into: trigger alone mid-roll would snap facing and bend the roll's motion,
            // and the swing may still wait for roll end. Refused: it start after the roll, learning get its state.
            return Refuse(cut);
        }
        // Save chain and aim as they arrived (after Dual Wielding), before our edit.
        var baseName = clone.m_attackAnimation;
        var levels = clone.m_attackChainLevels;
        var maxY = clone.m_maxYAngle;
        if (!MoveEdit.Apply(clone, character, weapon, kind, trigger, rules))
        {
            _staminaSkip = kind;
            return Refuse(cut);
        }
        _applied = new MoveInfo
        {
            Clone = clone,
            Weapon = weapon,
            Kind = kind,
            Family = family,
            Trigger = trigger,
            BaseName = baseName,
            ChainLevels = levels,
            Step = MoveTriggers.StepOf(trigger, baseName, levels),
            MaxYBefore = maxY,
            AimEdited = kind == MoveKind.Jump && clone.m_maxYAngle != maxY,
            Rules = rules,
            RollAge = kind == MoveKind.Roll && !cut ? _pendingRollAge : -1f,
            RollTime = cut ? _pendingRollTime : -1f,
            Cut = cut,
            RollSeq = _rollSeq,
        };
        _hasApplied = true;
        return false;
    }

    private static bool Refuse(bool cut)
    {
#if DEBUG
        if (cut)
        {
            GateRefusals++;
        }
#endif
        return cut;
    }

    // Humanoid.StartAttack postfix (local player). Success = tokens eaten (any attack: primary, secondary, move or
    // not); roll attack = cross-fade out of the roll; move applied and really current = watch it; else learn.
    internal static void AfterStart(Player player, bool started)
    {
        if (!started)
        {
            return;
        }
        var mine = ReferenceEquals(player, _player);
        if (mine && _cooledDown != MoveKind.None && !_hasApplied)
        {
            Log.Debug($"{Moves.Title(_cooledDown)} skipped: cooldown ({MoveRules.F(_cooldownLeft)} s left).");
        }
        if (mine && _staminaSkip != MoveKind.None && !_hasApplied)
        {
            Log.Debug($"{Moves.Title(_staminaSkip)} skipped: stamina for a normal swing only.");
        }
        ClearTokens(keepLastMove: true);
        if (!mine)
        {
            return;
        }
        // Old watch first: its clone was just stopped by vanilla (or already entered).
        if (_watching)
        {
            FinishWatch(player);
        }
        var attack = player.m_currentAttack;
        if (_hasApplied && ReferenceEquals(attack, _applied.Clone))
        {
            if (_applied.Kind == MoveKind.Roll)
            {
                // Trigger already set on the local Animator (Attack.Start, RPC to self run at once).
                _applied.Flow = RollFlow.Cut(AnimatorOf(player), _applied.Trigger, _applied.Rules.FlowBlend,
                    out _applied.Source);
            }
            StartWatch(player);
            if (_applied.Flow != FlowResult.CrossFade)
            {
                StartLearn(player, attack, _applied.Trigger);
            }
            return;
        }
        StartLearn(player, attack, null);
    }

    // Humanoid.StartAttack finalizer: every path (original skipped by any prefix, early return, exception).
    internal static void ClearCall()
    {
        _pendingKind = MoveKind.None;
        _pendingRules = null;
        _pendingCut = false;
        _pendingRollTime = -1f;
        _cooledDown = MoveKind.None;
        _staminaSkip = MoveKind.None;
        CloseGate();
        if (_hasApplied)
        {
            _hasApplied = false;
            _applied = default;
        }
    }

    // Put m_inDodge back (me only open the gate when it was true). Player gone (Unity null) = nothing to put back.
    private static void CloseGate()
    {
        var player = _gatePlayer;
        if (ReferenceEquals(player, null))
        {
            return;
        }
        _gatePlayer = null;
        if (player != null)
        {
            player.m_inDodge = true;
        }
    }

    // Character.Jump postfix: local player really jumped (from input, ForceJump ran in the call). Jump in the restart
    // gap of an attack still starting belong to that attack (it then play in the air): no token.
    internal static void OnJumped(Player player)
    {
        if (player == null || !ReferenceEquals(player, _player))
        {
            return;
        }
        var attack = player.m_currentAttack;
        if (attack != null && !attack.m_attackDone && !attack.m_wasInAttack && attack.m_time < StartingAttackAge)
        {
            return;
        }
        _jumpLive = true;
        _jumpAt = Time.fixedTime;
    }

    // Player.UpdateDodge postfix, local player, every physics tick (after attack input of the same tick): player
    // change, roll edges, jump token end, move watch, learning. No allocation.
    internal static void Tick(Player player)
    {
        if (!ReferenceEquals(player, _player))
        {
            NewPlayer(player);
        }
        var now = Time.fixedTime;

        // Roll edges. False -> true = new roll (or dash that set m_inDodge): token inside the roll, old end token
        // gone. True -> false = roll over (or cut by our roll attack): end token only if no attack used this roll and
        // no hit cut it (staggering); roll animation still there (current state dodge, blending out) = later roll
        // attack must flow from it (dash mods: no dodge state, window alone).
        var inDodge = player.m_inDodge;
        if (inDodge != _prevInDodge)
        {
            _prevInDodge = inDodge;
            if (inDodge)
            {
                _rollSeq++;
                _rollStartAt = now;
                _rollActive = true;
                _iframesEndAt = None;
                _rollEndAt = None;
                _rollEndInDodge = false;
                Prepare(player);
            }
            else
            {
                _rollEndAt = _rollActive && !player.IsStaggering() ? now : None;
                _rollEndInDodge = _rollEndAt > None && InRollState(player);
                _rollActive = false;
            }
        }
        else if (_rollEndAt > None && now - _rollEndAt > MoveRules.MaxWindow)
        {
            _rollEndAt = None; // older than any window: Decide not needed any more
            _rollEndInDodge = false;
        }

        // Roll's i-frames end (DodgeMortal event, animation update after last tick): cut margin count from here.
        if (_rollActive && _iframesEndAt == None && !player.m_dodgeInvincible)
        {
            _iframesEndAt = now;
        }

        // Jump token end on landing (after vanilla ground lock).
        if (_jumpLive && now - _jumpAt >= JumpGroundLock && player.IsOnGround())
        {
            _jumpLive = false;
        }

        if (_watching)
        {
            TickWatch(player, now);
        }
        if (_learnAttack != null)
        {
            LearnTick(player, now);
        }
    }

    // New local player (spawn, respawn, reconnect): old state belong to old player. Old watch dropped silently (old
    // Player dead or gone: no trigger reset on it).
    private static void NewPlayer(Player player)
    {
        DropWatch(resetTrigger: true);
        ClearTokens();
        StopLearn();
        _player = player;
        _prevInDodge = player.m_inDodge;
        // GCO line (and Debug plugin list) at spawn, not at first jump attack.
        _ = Compat.GcoLoaded;
#if DEBUG
        var animator = player.m_animator;
        Log.Debug($"Local player animator update mode: {(animator != null ? animator.updateMode.ToString() : "no animator")}.");
#endif
    }

    private static void StartWatch(Player player)
    {
        _watch = _applied;
        _watching = true;
        _starting = true;
        _aimPending = _applied.AimEdited;
        _graceTick = false;
        _refusedLogged = false;
        _startAt = Time.fixedTime;
        _clockAt = _startAt;
        _watchPlayer = player;
#if DEBUG
        LastMove = _applied;
        LastStartAt = _startAt;
        LastEntryDelay = -1f;
#endif
        Log.Debug(MoveEdit.Describe(_watch.Kind, _watch.Weapon, _watch.Family, _watch.Trigger, _watch.Rules,
            RollText(_watch), ServerRules.Source));
    }

    // Roll attack part of the Debug line: where it started and how it left the roll. Only at move start.
    private static string RollText(MoveInfo move)
    {
        if (move.Kind != MoveKind.Roll)
        {
            return null;
        }
        var how = RollFlow.HowText(move.Flow, move.Source);
        return move.Cut
            ? $"cut into the roll {MoveRules.F(move.RollTime)} s after it started ({how})"
            : $"{MoveRules.F(move.RollAge)} s after the roll ended ({how})";
    }

    private static void TickWatch(Player player, float now)
    {
        var clone = _watch.Clone;
        var current = ReferenceEquals(player.m_currentAttack, clone);

        // Aim only in the air (also before entry): pitched fan on the ground hit the terrain.
        if (_aimPending && player.IsOnGround())
        {
            RestoreAim();
        }

        if (_starting)
        {
            if (clone.m_wasInAttack)
            {
                Enter(now, continueCombo: true);
            }
            else if (!current || clone.m_attackDone)
            {
                Dropped(player);
                return;
            }
            else if (_rollSeq != _watch.RollSeq || player.IsStaggering())
            {
                Interrupted(player, _rollSeq != _watch.RollSeq ? "a roll started" : "the player was staggered");
                return;
            }
            else if (player.m_inDodge)
            {
                // Same roll the move cut into, still in its dodge state (a cut always cross-fade, so start tick only;
                // kept as a guard). Start check clock wait.
                _clockAt = now;
                return;
            }
            else if (_graceTick)
            {
                // Deadline passed with Animator in attack, one tick waited, game still not mark it started: count
                // as started, no combo continuation (me cannot know game's own chain value).
                Enter(now, continueCombo: false);
            }
            else if (now - _clockAt >= StartDeadline)
            {
                if (player.InAttack())
                {
                    _graceTick = true; // Attack.Update run before us (tick order), but one more tick costs nothing
                    return;
                }
                Cancel(player);
                return;
            }
            else
            {
                return;
            }
        }

        // Playing: done or replaced = end (aim back too: swing over in the air, clone never hit again); else wait
        // only for landing (aim).
        if (!current || clone.m_attackDone || !_aimPending)
        {
            RestoreAim();
            EndWatch();
        }
    }

    // Move animation in its state: cooldown start, combo continue from the move's step (design 2.5).
    private static void Enter(float now, bool continueCombo)
    {
        _starting = false;
        _lastMoveAt = now;
        var clone = _watch.Clone;
        var delay = now - _startAt;
#if DEBUG
        LastEntryDelay = delay;
#endif
        string next;
        if (continueCombo && _watch.Step >= 0)
        {
            // Attack.Update already wrote own value (m_wasInAttack set with it): me overwrite. Base name back = next
            // clone (same base) continue vanilla chain from here.
            var level = _watch.Step + 1;
            if (level >= _watch.ChainLevels)
            {
                level = 0;
            }
            clone.m_attackAnimation = _watch.BaseName;
            clone.m_nextAttackChainLevel = level;
            next = level > 0
                ? $"the combo continues at step {level}"
                : "the next attack starts the combo at its first swing";
        }
        else
        {
            next = continueCombo
                ? "the next attack starts a new combo"
                : "no combo continuation (the game had not marked it started)";
        }
        Log.Debug($"{Moves.Title(_watch.Kind)} {_watch.Trigger} started after {MoveRules.F(delay)} s; {next}.");
    }

    // Game dropped the clone before its animation started (unequip, stop): trigger reset, unless NEW attack fired
    // the same name. Clone stopped but still current (vanilla Abort: stagger on entry tick, Stop clear m_wasInAttack)
    // = our own trigger: reset too (already used = no harm).
    private static void Dropped(Player player)
    {
        var cur = player.m_currentAttack;
        var fired = cur != null && !ReferenceEquals(cur, _watch.Clone) ? FiredTrigger(cur) : null;
        var reset = fired == null || fired != _watch.Trigger;
        if (reset)
        {
            ResetTrigger();
        }
        Log.Debug($"{Moves.Title(_watch.Kind)} {_watch.Trigger} was dropped before it started"
                  + (reset ? "; trigger reset." : "; the new attack uses the same animation."));
        RestoreAim();
        EndWatch();
    }

    // Trigger name a started attack fired, like vanilla Attack.Start build it: chain = base + current chain level
    // (clone keep only base name), else base name alone (our moves: full name, chain levels 0). Random animation =
    // null (me cannot know which). String concat: only at attack start or drop, never per tick.
    internal static string FiredTrigger(Attack attack)
    {
        if (attack == null || string.IsNullOrEmpty(attack.m_attackAnimation))
        {
            return null;
        }
        if (attack.m_attackChainLevels > 1)
        {
            return attack.m_attackAnimation + attack.m_currentAttackCainLevel;
        }
        return attack.m_attackRandomAnimations >= 2 ? null : attack.m_attackAnimation;
    }

    // A2: no entry in 0.5 s (trigger with no way in from current state). Reset trigger, drop clone like vanilla
    // UnequipItem, queue a press so vanilla start normal swing next tick (tokens already eaten), warn once.
    private static void Cancel(Player player)
    {
        DropClone(player);
        player.m_queuedAttackTimer = 0.5f;
#if DEBUG
        StartFailures++;
#endif
        WarnStartFailed();
        EndWatch();
    }

    // Roll or stagger began before the move entered its state (vanilla start a queued roll in the restart gap, since
    // Animator not show the attack yet). Not an animation problem: no warning, no new press (player chose the roll).
    // Me drop the move now, else start check would cancel it mid-roll with a wrong warning.
    private static void Interrupted(Player player, string why)
    {
        DropClone(player);
        Log.Debug($"{Moves.Title(_watch.Kind)} {_watch.Trigger} was dropped before it started ({why}); trigger reset.");
        EndWatch();
    }

    // Reset trigger, stop clone, take it off the player like vanilla UnequipItem: stopped clone that stay current
    // would still hit with move numbers if its animation play late (Humanoid.OnAttackTrigger no check IsDone).
    private static void DropClone(Player player)
    {
        var clone = _watch.Clone;
        ResetTrigger();
        clone.Stop();
        if (ReferenceEquals(player.m_currentAttack, clone))
        {
            player.m_previousAttack = clone;
            player.m_currentAttack = null;
        }
        RestoreAim();
    }

    private static void WarnStartFailed()
    {
        var k = Moves.Index(_watch.Kind);
        var f = Families.Index(_watch.Family);
        if (k < 0 || f < 0 || Warned[k, f])
        {
            return;
        }
        Warned[k, f] = true;
#if DEBUG
        StartWarnings++;
#endif
        var where = _watch.Kind == MoveKind.Jump ? "airborne" : _watch.Cut ? "cutting into a roll" : "after a roll";
        Log.Warning($"The {Moves.Name(_watch.Kind)} animation {_watch.Trigger} did not start for "
                    + $"{MoveEdit.ItemName(_watch.Weapon)} ({where}), so a normal swing was used. "
                    + Moves.AnimationAdvice(_watch.Kind, ServerRules.FromServer(_watch.Rules)));
    }

    // New successful start while old watch still on (old clone stopped by vanilla StartAttack, or playing).
    private static void FinishWatch(Player player)
    {
        if (_starting && !_watch.Clone.m_wasInAttack)
        {
            Dropped(player);
            return;
        }
        RestoreAim();
        EndWatch();
    }

    // Reset / new player: move still starting = trigger reset (safe only on live local Player), aim back.
    private static void DropWatch(bool resetTrigger)
    {
        if (!_watching)
        {
            return;
        }
        if (resetTrigger && _starting && _watch.Clone != null && !_watch.Clone.m_wasInAttack)
        {
            ResetTrigger();
        }
        RestoreAim();
        EndWatch();
    }

    // Local Animator only (vanilla has no reset RPC: other peers may see a late swing, never a hit). Player dead or
    // gone (logout, death within 0.5 s) = skip: Animator may be destroyed.
    private static void ResetTrigger()
    {
        var player = _watchPlayer;
        if (player == null || !ReferenceEquals(player, Player.m_localPlayer) || string.IsNullOrEmpty(_watch.Trigger))
        {
            return;
        }
        var animator = AnimatorOf(player);
        if (animator == null)
        {
            return;
        }
        animator.ResetTrigger(_watch.Trigger);
#if DEBUG
        TriggerResets++;
#endif
    }

    // Animator ZSyncAnimation set triggers on (RPC_SetTrigger). Null (Unity null too) = none.
    private static Animator AnimatorOf(Player player)
    {
        var zanim = player.m_zanim;
        if (zanim == null)
        {
            return null;
        }
        var animator = zanim.m_animator;
        return animator == null ? null : animator;
    }

    private static void RestoreAim()
    {
        if (!_aimPending)
        {
            return;
        }
        _aimPending = false;
        if (_watch.Clone != null)
        {
            _watch.Clone.m_maxYAngle = _watch.MaxYBefore;
        }
    }

    private static void EndWatch()
    {
        _watching = false;
        _starting = false;
        _aimPending = false;
        _graceTick = false;
        _watchPlayer = null;
        _watch = default;
    }

    // Learn which layer 0 state the Animator go to for this trigger (design 2.9): me remember the state at the start;
    // first attack-tagged state after it that is another state = the trigger's (a chain step leave the previous step's
    // state). Trigger null = the name vanilla fired. Known trigger (its state still in this controller) = nothing to do;
    // state gone (other mod swapped controller) = learn again.
    private static void StartLearn(Player player, Attack attack, string trigger)
    {
        StopLearn();
        if (attack == null)
        {
            return;
        }
        trigger ??= FiredTrigger(attack);
        if (trigger == null)
        {
            return;
        }
        var animator = AnimatorOf(player);
        if (animator == null || RollFlow.Knows(animator, trigger))
        {
            return;
        }
        RollFlow.NextOrCurrent(animator, out _learnFrom, out _);
        _learnAttack = attack;
        _learnTrigger = trigger;
        _learnAt = Time.fixedTime;
    }

    // Two or three Animator reads per tick, only for the first swings of a new trigger.
    private static void LearnTick(Player player, float now)
    {
        var attack = _learnAttack;
        if (!ReferenceEquals(player.m_currentAttack, attack) || attack.m_attackDone || now - _learnAt > LearnLimit)
        {
            StopLearn();
            return;
        }
        var animator = AnimatorOf(player);
        if (animator == null)
        {
            StopLearn();
            return;
        }
        RollFlow.NextOrCurrent(animator, out var hash, out var tag);
        if (tag == RollFlow.AttackTag && hash != _learnFrom)
        {
            RollFlow.Learn(_learnTrigger, hash);
            StopLearn();
        }
    }

    private static void StopLearn()
    {
        _learnAttack = null;
        _learnTrigger = null;
        _learnFrom = 0;
    }

    private static void ClearTokens(bool keepLastMove = false)
    {
        _jumpLive = false;
        _jumpAt = None;
        _rollActive = false;
        _iframesEndAt = None;
        _rollEndAt = None;
        _rollEndInDodge = false;
        if (!keepLastMove)
        {
            _lastMoveAt = None;
        }
    }
}
