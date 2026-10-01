using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SwimmingDiveMod;

// Me = the dive physics of the local player (design 2.1-2.5). Velocity override, never m_swimDepth: every vanilla
// read of m_swimDepth (swim threshold, jump, falling, sinking platform) stay as it is.
//   Tick  (Character.UpdateMotion prefix, every physics tick of the local player, before swim/walk code): state.
//         Surface -> Diving when Crouch held, swimming in water, stamina left. Deep once feet EnterMargin under the
//         vanilla rest height (liquid level - m_swimDepth), until ExitMargin (hysteresis). Diving end: not deep and
//         no Crouch (vanilla buoyancy finish the rise), or anything that stop swimming (land, tar, dead, attached,
//         teleport, debug fly, rules pending, feature off).
//   Sideways (UpdateSwimming prefix): Deep = m_moveDir zero, auto-run off (G2). Except blocked above (ship hull, dock,
//         rock over the head): vanilla sideways swimming stay, so a diver pinned under a hull can slide out (D5).
//   Vertical (UpdateSwimming postfix, after vanilla buoyancy wrote the body speed): own smoothed speed toward the
//         target (Crouch down, Jump up, none = IdleRiseSpeed, no stamina = up), gravity off while me drive y
//         (vanilla set it on again every swim/walk tick), stop going down at the sea floor or at the bottom of the
//         water volume trigger (prefab data: 50 m under the water level in 1.0.16, in-world NOTE), body kept awake
//         (sleeping body = "on ground" for vanilla). Not deep + target 0 (Crouch + Jump, floor stop the Crouch) +
//         stamina: hands off (HandedBack), vanilla buoyancy and gravity keep the diver on the waves, so a wave never
//         rise over a diver held still and make him deep. Take over again from the body speed (no jolt); after a
//         hand-back the floor must be HandBackFloorMargin further down first (no bob at a shallow floor).
//   On the sea floor vanilla skip OnSwimming (no drain, no drowning): me call it (G4).
internal static class DiveController
{
    // Head this close to the surface still check for a ceiling (m above the surface: hull sticking out of water).
    private const float CeilingReachAboveSurface = 0.5f;
    // Point this far under the feet must still be in a water volume to go on down.
    private const float WaterProbeDepth = 0.6f;
    private const string OutOfBreathText = "Out of breath";

    // Character.UpdateMotion prefix, local player only.
    internal static void Tick(Player player)
    {
        if (!ReferenceEquals(DiveState.Owner, player))
        {
            DiveState.Reset();
            DiveState.Owner = player;
        }
        // On land (most ticks): one bool and one float compare.
        if (!DiveState.Diving && !player.IsSwimming())
        {
            return;
        }
        var rules = ServerRules.Current;
        var block = DiveLogic.Blocker(rules.IsPending, player.IsSwimming(), player.InWater() && player.GetLiquidLevel() > -9000f,
            player.IsDead(), player.IsAttached() || player.GetStandingOnShip() != null, player.IsTeleporting(),
            player.IsDebugFlying());
        if (block != DiveBlock.None)
        {
            if (DiveState.Diving)
            {
                Log.Debug($"Dive ended: {block}.");
            }
            Stop(player);
            return;
        }
        DiveInput.Read(player, out var down, out var up);
        var restY = player.GetLiquidLevel() - player.m_swimDepth;
        var feet = player.transform.position.y;
        var hasStamina = player.HaveStamina();
        var deep = DiveLogic.Deep(DiveState.Diving, DiveState.Deep, feet, restY);
        if (!DiveLogic.Diving(DiveState.Diving, deep, down, hasStamina))
        {
            if (DiveState.Diving)
            {
                Log.Debug("Dive ended: back at the surface.");
            }
            Stop(player);
            return;
        }
        if (!DiveState.Diving)
        {
            DiveState.Diving = true;
            DiveState.OutOfBreathShown = false;
            DiveState.Vy = player.m_body != null ? player.m_body.linearVelocity.y : 0f;
            Log.Debug($"Dive started ({rules.Describe()}).");
        }
        DiveState.Deep = deep;
        DiveState.Down = down;
        DiveState.Up = up;
        DiveState.HasStamina = hasStamina;
        DiveState.RestY = restY;
        DiveState.BlockedAbove = deep && CeilingAbove(player);
        if (!hasStamina && !DiveState.OutOfBreathShown)
        {
            DiveState.OutOfBreathShown = true;
            player.Message(MessageHud.MessageType.TopLeft, OutOfBreathText);
        }
    }

    // UpdateSwimming prefix: sideways swimming off while deep (not under a ceiling).
    internal static void LockSideways(Player player)
    {
        if (!DiveState.Deep || DiveState.BlockedAbove)
        {
            return;
        }
        player.m_moveDir = Vector3.zero;
        player.m_autoRun = false;
    }

    // UpdateSwimming postfix: vertical speed. onGround = IsOnGround at the start of UpdateSwimming (vanilla then
    // skipped OnSwimming).
    internal static void ApplyVertical(Player player, float dt, bool onGround)
    {
        var body = player.m_body;
        if (body == null)
        {
            return;
        }
        // Vanilla had the vertical last tick: go on from the body speed its buoyancy just wrote (no jolt), and look
        // for floor or water bottom a bit further (no fight with vanilla buoyancy at a shallow floor).
        var margin = 0f;
        if (DiveState.HandedBack)
        {
            DiveState.Vy = body.linearVelocity.y;
            margin = DiveLogic.HandBackFloorMargin;
        }
        var rules = ServerRules.Current;
        var speed = SwimSpeed(player) * rules.DiveSpeedMultiplier;
        var target = DiveLogic.TargetSpeed(DiveState.Down, DiveState.Up, DiveState.HasStamina, speed,
            rules.IdleRiseSpeed);
        if (target < 0f && (FloorBelow(player, -target * dt + Mathf.Max(0f, -DiveState.Vy) * dt + margin)
                            || !WaterBelow(player, WaterProbeDepth + margin)))
        {
            target = 0f;
            if (DiveState.Vy < 0f)
            {
                DiveState.Vy = 0f;
            }
        }
        if (DiveLogic.VanillaVertical(DiveState.Deep, DiveState.HasStamina, target))
        {
            // Near the surface, nothing to do up or down: hands off. Vanilla buoyancy (already run this tick, from the
            // body speed) and gravity (vanilla put it on) keep the diver on the waves; no write = no jolt. Own speed 0
            // = not moving for the drain and XP rule. On ground vanilla skipped OnSwimming: me too (still = free).
            DiveState.Vy = 0f;
            DiveState.HandedBack = true;
            return;
        }
        DiveState.HandedBack = false;
        var response = Mathf.Clamp(player.m_swimAcceleration, DiveLogic.MinResponse, 1f);
        DiveState.Vy = Mathf.Lerp(DiveState.Vy, target, response);
        var v = body.linearVelocity;
        v.y = DiveState.Vy;
        body.linearVelocity = v;
        body.useGravity = false;
        DiveState.GravityOff = true;
        if (body.IsSleeping())
        {
            body.WakeUp();
        }
        if (onGround)
        {
            // Sea floor, slope or rock under the diver: keep the swim pose, and the drain (and drowning at 0) that
            // vanilla skip on ground. Zero target: OnSwimming prefix put the still-diver drain in while deep or
            // moving up/down (chest-deep water, not deep, target 0 = handed back above, never here: no drain, as
            // vanilla).
            if (DiveState.Deep)
            {
                player.m_zanim.SetBool(Character.s_inWater, true);
            }
            player.OnSwimming(Vector3.zero, dt);
        }
    }

    // Vanilla swim speed (UpdateSwimming): swim speed x attack movement factor, 0 in minor action slowdown, then
    // status effects (swim-speed gear and potions) with the vertical direction.
    internal static float SwimSpeed(Player player)
    {
        var speed = player.m_swimSpeed * player.GetAttackSpeedFactorMovement();
        if (player.InMinorActionSlowdown())
        {
            speed = 0f;
        }
        var dir = DiveState.Up && !DiveState.Down ? Vector3.up : Vector3.down;
        player.m_seman.ApplyStatusEffectSpeedMods(ref speed, dir);
        return Mathf.Max(0f, speed);
    }

    // Stop driving (Tick exit, feature off). Gravity back on at once (vanilla set it again next swim/walk tick too).
    internal static void Stop(Player player)
    {
        if (DiveState.GravityOff && player != null && player.m_body != null)
        {
            player.m_body.useGravity = true;
        }
        var owner = DiveState.Owner;
        DiveState.Reset();
        DiveState.Owner = owner;
    }

    // Feature off: stop the local player's dive (if any) and forget the state.
    internal static void Shutdown()
    {
        var player = Player.m_localPlayer;
        if (player != null && ReferenceEquals(DiveState.Owner, player))
        {
            Stop(player);
        }
        DiveState.Reset();
    }

    // Sea floor (terrain, rock, wreck, built piece) within the next step + clearance under the feet.
    private static bool FloorBelow(Player player, float step)
    {
        var origin = player.transform.position + Vector3.up * 0.3f;
        return Physics.Raycast(origin, Vector3.down, 0.3f + DiveLogic.FloorClearance + step,
            Character.s_groundRayMask, QueryTriggerInteraction.Ignore);
    }

    // Water volume trigger still around a point this deep under the feet. Its depth is prefab data (1.0.16 ocean
    // volume: trigger y -20..40, 50 m under the water level, in-world NOTE): leaving the trigger = liquid level lost
    // (-10000), swimming off, fall to the floor. Me stop going down before that.
    private static bool WaterBelow(Player player, float depth)
    {
        var below = player.transform.position + Vector3.down * depth;
        return Floating.GetLiquidLevel(below, 1f, LiquidType.Water) > -9000f;
    }

    // Solid collider between the head and the surface (ship hull, dock, overhang): design D5 escape rule.
    private static bool CeilingAbove(Player player)
    {
        var head = player.GetHeadPoint();
        var reach = player.GetLiquidLevel() - head.y;
        if (reach <= 0f)
        {
            return false;
        }
        return Physics.Raycast(head, Vector3.up, reach + CeilingReachAboveSurface, Character.s_groundRayMask,
            QueryTriggerInteraction.Ignore);
    }
}
