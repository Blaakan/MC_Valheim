using UnityEngine;

namespace MC.Combat.ShieldsTowerWallMod;

// Me = slow bash swing (design 2.7 "Swing speed", decision 32). Local player's running bash only.
// Vanilla clips set their own animator speed with Speed animation events (CharacterAnimEvent.Speed: the punches play
// x2 from their first frame, the kick x1 then x2 around its hit). Me never override: me scale each Speed event of the
// running bash by BashAnimationSpeed (clip's own rhythm kept), and when the clip reach its attack state with no Speed
// event yet (kick, bomb throw: speed 1 until their first event) me scale that start speed once. Whole swing slower,
// Hit event too (it is an event of the same clip).
// Other players: owner's ZSyncAnimation write the animator speed in the ZDO each fixed step when it change; every
// other game set its copy of the player to it each fixed step (vanilla sync). They see the same slow swing, with or
// without the mod.
// Back to the clip's own speed: bash put away, cancelled or mod off in the middle of the clip = last unscaled value
// (into FreezeFrame's stored speed while a hit-stop run: it put that back after), only while the speed is still the
// one me wrote. Clip over: vanilla set 1 (CharacterAnimEvent.CustomFixedUpdate, out of attack state). Stagger: vanilla
// RPC_Stagger set 1 and abort the bash; me leave it.
internal static class BashSpeed
{
    private static Player _player;
    private static Attack _attack;
    private static float _factor = 1f;
    private static float _raw = 1f;     // clip's own speed now: last Speed event unscaled, or the start speed
    private static bool _eventSeen;     // a Speed event of this bash came (start then already right)
    private static bool _scaled;        // animator speed now = _raw x _factor, written by me

    // CharacterAnimEvent.Speed prefix: one static read when no bash is slowed.
    internal static bool Scaling => _attack != null;

#if DEBUG
    // Self tests only: clip's own speed now, and whether the start was scaled by me (no Speed event before the attack
    // state) for the bash now running or last run.
    internal static float DebugRaw => _raw;
    internal static bool DebugStartScaled;
#endif

    // BashWatch.OnStarted: new bash (clone just started, trigger sent, animator not updated yet). Factor 1 = nothing
    // to do at all.
    internal static void Begin(Player player, Attack attack, float factor)
    {
        End(player);
        _raw = 1f;
#if DEBUG
        DebugStartScaled = false;
#endif
        if (player == null || attack == null || Mathf.Approximately(factor, 1f))
        {
            return;
        }
        _player = player;
        _attack = attack;
        _factor = factor;
        _eventSeen = false;
        _scaled = false;
    }

    // Prefix of CharacterAnimEvent.Speed (Unity animation event). Only the local player's running bash, while it is
    // the current attack, not done and not staggered.
    internal static void OnSpeedEvent(CharacterAnimEvent ev, ref float speedScale)
    {
        var player = _player;
        if (!ReferenceEquals(ev.m_character, player) || player == null)
        {
            return;
        }
        if (!ReferenceEquals(player.m_currentAttack, _attack) || _attack.IsDone() || player.IsStaggering())
        {
            return;
        }
        _raw = speedScale;
        _eventSeen = true;
        speedScale *= _factor;
        _scaled = true;
    }

    // BashWatch.Tick: bash just reached its attack state. No Speed event yet = clip play at its start speed (1, set by
    // vanilla out of attack): me scale it once. Frozen (hit-stop, speed ~0) = leave it.
    internal static void OnReached(Player player)
    {
        if (_attack == null || _eventSeen || !ReferenceEquals(player, _player) || player.m_animator == null)
        {
            return;
        }
        var now = player.m_animator.speed;
        if (now < 0.01f)
        {
            return;
        }
        _raw = now;
        player.m_animator.speed = now * _factor;
        _scaled = true;
#if DEBUG
        DebugStartScaled = true;
#endif
    }

    // Bash over, put away, replaced, cancelled, mod off. Still in the clip and the speed is still the one me wrote =
    // clip's own speed back. Out of the clip: vanilla already set 1 (or will, next fixed step). Staggered, or speed
    // changed by someone else since (RPC_Stagger set 1): leave it, never undo vanilla.
    internal static void End(Player player)
    {
        if (_attack == null)
        {
            return;
        }
        var owner = _player;
        var scaled = _scaled;
        var raw = _raw;
        var written = _raw * _factor;
        _attack = null;
        _player = null;
        _scaled = false;
        _eventSeen = false;
        _factor = 1f;
        if (!scaled || owner == null || !ReferenceEquals(owner, player) || !owner.InAttack() || owner.IsStaggering())
        {
            return;
        }
        var ev = owner.m_animEvent;
        if (ev != null && ev.m_pauseTimer > 0f)
        {
            if (Mathf.Abs(ev.m_pauseSpeed - written) < 0.001f)
            {
                ev.m_pauseSpeed = raw; // hit-stop put this back when it end
            }
        }
        else if (owner.m_animator != null && Mathf.Abs(owner.m_animator.speed - written) < 0.001f)
        {
            owner.m_animator.speed = raw;
        }
    }

    // Deactivation, local player gone: forget, write nothing.
    internal static void Reset()
    {
        _attack = null;
        _player = null;
        _scaled = false;
        _eventSeen = false;
        _factor = 1f;
        _raw = 1f;
    }
}
