namespace MC.Exploration.SwimmingDiveMod;

// Me = dive state of the local player (only the local player ever dive: its game own its body). One static set,
// written by DiveController each physics tick (Character.UpdateMotion prefix), read by the swim, jump, stamina and
// camera patches. Owner = the Player it belong to: new local player (logout, respawn copy) = state reset.
//   Surface (Diving false) = vanilla swimming.
//   Diving  = me drive the vertical speed. Deep = feet far enough under the vanilla rest height: under-water rules on
//             (no sideways swimming, stamina drain while still). Not deep and nothing to do up or down (Crouch + Jump,
//             floor stop the Crouch) = HandedBack: vanilla buoyancy keep the vertical, diver ride the waves.
internal static class DiveState
{
    internal static Player Owner;
    internal static bool Diving;
    internal static bool Deep;
    internal static bool Down;              // Crouch held this tick (input gate passed)
    internal static bool Up;                // Jump held this tick
    internal static bool HasStamina;
    internal static bool BlockedAbove;      // solid thing between head and surface: sideways swimming allowed
    internal static float Vy;               // own vertical speed, smoothed (read from the body only when me take over)
    internal static float RestY;            // vanilla rest height of the feet (liquid level - m_swimDepth)
    internal static bool GravityOff;        // me turned body gravity off (put back on stop)
    internal static bool HandedBack;        // last tick vanilla buoyancy and gravity had the vertical (Vy 0)
    internal static bool OutOfBreathShown;  // message once per dive

    // Diver really moving up or down (swim XP rule, design D6).
    internal static bool MovingVertically => Diving && (Vy > 0.1f || Vy < -0.1f);

    // Diving for this player (camera and stamina patches ask with the player they see).
    internal static bool DivingFor(Player player) => Diving && player != null && ReferenceEquals(Owner, player);

    internal static void Reset()
    {
        Owner = null;
        Diving = false;
        Deep = false;
        Down = false;
        Up = false;
        HasStamina = false;
        BlockedAbove = false;
        Vy = 0f;
        RestY = 0f;
        GravityOff = false;
        HandedBack = false;
        OutOfBreathShown = false;
    }
}

// Why the local player cannot dive right now (null = can). Debug log and self test say it.
internal enum DiveBlock : byte
{
    None,
    Pending,       // client of a server whose rules did not come yet
    NotSwimming,   // IsSwimming false (on land, 0.5 s after leaving swim depth)
    NotWater,      // tar is the higher liquid, or no liquid level at all
    Dead,
    Attached,      // chair, bed, ship controls, standing on a ship
    Teleporting,
    DebugFly,
}

// Me = the pure rules of the dive (self test hammer them, no Unity object needed).
internal static class DiveLogic
{
    // Feet this far under the vanilla rest height = Deep (under-water rules on) ...
    internal const float EnterMargin = 0.5f;
    // ... and Deep stay until the feet come back above rest height minus this (hysteresis).
    internal const float ExitMargin = 0.2f;
    // Diver stop this far above the sea floor when going down (ray from the feet).
    internal const float FloorClearance = 0.15f;
    // After a hand-back, floor (or water bottom) must be this much further down before Crouch take the vertical again.
    // Else at a shallow floor vanilla buoyancy lift, me push down, vanilla lift... (bob, drain and XP every cycle).
    internal const float HandBackFloorMargin = 0.5f;
    // Smallest per-tick share of the speed change (vanilla swim acceleration 0.05 per tick).
    internal const float MinResponse = 0.05f;
    // Swim target sent to vanilla OnSwimming while the diver is still (> 0.1 = vanilla drain rule applies).
    internal const float StillDrainTarget = 0.5f;

    internal static DiveBlock Blocker(bool pending, bool swimming, bool water, bool dead, bool attached,
        bool teleporting, bool debugFly)
    {
        if (pending)
        {
            return DiveBlock.Pending;
        }
        if (dead)
        {
            return DiveBlock.Dead;
        }
        if (debugFly)
        {
            return DiveBlock.DebugFly;
        }
        if (teleporting)
        {
            return DiveBlock.Teleporting;
        }
        if (attached)
        {
            return DiveBlock.Attached;
        }
        if (!swimming)
        {
            return DiveBlock.NotSwimming;
        }
        return water ? DiveBlock.None : DiveBlock.NotWater;
    }

    // Deep next tick. Never deep while not diving.
    internal static bool Deep(bool diving, bool wasDeep, float feetY, float restY)
    {
        if (!diving)
        {
            return false;
        }
        return feetY < restY - (wasDeep ? ExitMargin : EnterMargin);
    }

    // Diving next tick. Start: Crouch held with stamina left. Keep: deep, or Crouch still held with stamina. So near
    // the surface (not deep) without Crouch the diver go back to vanilla, whose buoyancy finish the rise.
    internal static bool Diving(bool wasDiving, bool deep, bool down, bool hasStamina)
    {
        if (!wasDiving)
        {
            return down && hasStamina;
        }
        return deep || (down && hasStamina);
    }

    // Target vertical speed (m/s, up > 0). No stamina = forced up, Crouch ignored. Both keys = stay.
    internal static float TargetSpeed(bool down, bool up, bool hasStamina, float speed, float idleRise)
    {
        if (!hasStamina)
        {
            return speed;
        }
        if (down && !up)
        {
            return -speed;
        }
        if (up && !down)
        {
            return speed;
        }
        if (up)
        {
            return 0f;
        }
        return idleRise;
    }

    // Vertical left to vanilla this tick: not deep, stamina left, target 0 (Crouch + Jump, or floor / water bottom
    // stop the Crouch). Vanilla buoyancy and gravity then keep the diver on the waves like any swimmer, so a wave never
    // rise over a diver held at one height and make him deep (drain while still, no sideways swimming). Deep = me hold
    // the depth (G2); no stamina = forced ascent, never handed back.
    internal static bool VanillaVertical(bool deep, bool hasStamina, float target) =>
        !deep && hasStamina && target == 0f;
}
