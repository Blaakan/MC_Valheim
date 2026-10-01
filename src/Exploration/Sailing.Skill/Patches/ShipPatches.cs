using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SailingSkillMod.Patches;

// Me = one owner physics step of one ship (Ship.CustomFixedUpdate, owner branch), for the patches it call inside:
// GetWindAngleFactor, GetSailForce, ApplyEdgeForce read helmsman level, rules and acceleration factor from here
// (computed once per step). Main thread, one ship at a time (MonoUpdaters loop): plain static fields. Finalizer always
// clear it.
internal static class ShipTick
{
    internal static Ship Current;
    internal static float S;
    internal static SailingRules Rules;
    internal static float Accel = 1f;

    internal static bool Is(Ship ship) => ReferenceEquals(ship, Current);

    internal static void Begin(Ship ship, float s, SailingRules rules, float accel)
    {
        Current = ship;
        S = s;
        Rules = rules;
        Accel = accel;
    }

    internal static void End()
    {
        Current = null;
        Rules = null;
        S = 0f;
        Accel = 1f;
    }
}

// Ship fields of one step: old value and value me wrote (value type: no allocation per step).
internal struct ShipScale
{
    internal bool Tick;
    internal ScaledField BackwardForce;
    internal ScaledField DampingForward;
    internal ScaledField StearVelForceFactor;
    internal ScaledField StearForce;
}

// S3 turn + S5 accelerate, on the ship owner only (vanilla: only owner run forces). Prefix scale, finalizer (run after
// every mod's postfix, also on exception) put each field back if it still hold what me wrote (ScaledField): safe with
// mods that set these fields once or scale and put them back themselves; a mod that change one during the step keep
// its change. Acceleration: paddle force and forward drag x factor here, sail force only along the bow (GetSailForce
// postfix below: sideways push, leeway and heel stay vanilla). Push along the bow and forward drag x same factor = same
// top speed along the bow (sqrt(thrust/drag)), reached factor times sooner, and a coasting ship slow down sooner too.
// m_sailForceFactor never touched. Turning: speed-dependent rudder force and paddle steering force x factor
// (m_angularDamping untouched: it also damp roll and pitch).
// Every fixed step for every ship on every game: non-owner = two cheap checks and out; empty ship = one more.
[HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate), new[] { typeof(float) })]
internal static class ShipFixedUpdatePatches
{
#if DEBUG
    // Self test: factors used on the last owner step of this ship (1 = none).
    internal static Ship LastShip;
    internal static float LastAccel = 1f;
    internal static float LastTurn = 1f;
#endif

    [HarmonyPrefix]
    private static void Prefix(Ship __instance, out ShipScale __state)
    {
        __state = default;
        try
        {
            var nview = __instance.m_nview;
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                return;
            }
            var rules = ServerRules.Current;
            if (rules.IsPending)
            {
                return;
            }
            var s = HelmSkill.Physics01(__instance);
            var accel = s > 0f ? SailMath.Scale(rules.AccelerationBonusAtMax, s) : 1f;
            var turn = s > 0f ? SailMath.Scale(rules.TurnBonusAtMax, s) : 1f;
            ShipTick.Begin(__instance, s, rules, accel);
            __state.Tick = true;
#if DEBUG
            LastShip = __instance;
            LastAccel = accel;
            LastTurn = turn;
#endif
            if (accel != 1f)
            {
                __state.BackwardForce = ScaledField.Scale(ref __instance.m_backwardForce, accel);
                __state.DampingForward = ScaledField.Scale(ref __instance.m_dampingForward, accel);
            }
            if (turn != 1f)
            {
                __state.StearVelForceFactor = ScaledField.Scale(ref __instance.m_stearVelForceFactor, turn);
                __state.StearForce = ScaledField.Scale(ref __instance.m_stearForce, turn);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Ship.CustomFixedUpdate prefix", e);
        }
    }

    // Void finalizer: exception (if any) still thrown as vanilla, me only put fields back (each one only if it still
    // hold my value).
    [HarmonyFinalizer]
    private static void Finalizer(Ship __instance, ShipScale __state)
    {
        try
        {
            __state.BackwardForce.PutBack(ref __instance.m_backwardForce);
            __state.DampingForward.PutBack(ref __instance.m_dampingForward);
            __state.StearVelForceFactor.PutBack(ref __instance.m_stearVelForceFactor);
            __state.StearForce.PutBack(ref __instance.m_stearForce);
            if (__state.Tick)
            {
                ShipTick.End();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Ship.CustomFixedUpdate finalizer", e);
        }
    }
}

// S3 rudder travel: only the helmsman's own game call ApplyControlls (Player.SetDoodadControlls -> ShipControlls),
// every fixed step; rudder value then reach the owner by vanilla Rudder RPC (every 0.2 s). Own level, no owner check:
// a helmsman with me keep the faster rudder also on a ship a game without me simulate (README say so). Finalizer put
// m_rudderSpeed back if it still hold my value (ScaledField).
[HarmonyPatch(typeof(Ship), nameof(Ship.ApplyControlls), new[] { typeof(Vector3) })]
internal static class ShipControlsPatches
{
    [HarmonyPrefix]
    private static void Prefix(Ship __instance, out ScaledField __state)
    {
        __state = default;
        try
        {
            var rules = ServerRules.Current;
            if (rules.IsPending || rules.RudderBonusAtMax <= 0f)
            {
                return;
            }
            var s = HelmSkill.Local01();
            if (s <= 0f)
            {
                return;
            }
            __state = ScaledField.Scale(ref __instance.m_rudderSpeed, SailMath.Scale(rules.RudderBonusAtMax, s));
        }
        catch (Exception e)
        {
            PatchGuard.Report("Ship.ApplyControlls prefix", e);
        }
    }

    [HarmonyFinalizer]
    private static void Finalizer(Ship __instance, ScaledField __state)
    {
        try
        {
            __state.PutBack(ref __instance.m_rudderSpeed);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Ship.ApplyControlls finalizer", e);
        }
    }
}

// S2 wind, S5 sail response and sail acceleration, S4 brake. All postfixes, no saved state.
[HarmonyPatch]
internal static class ShipSailPatches
{
    // S2: wind factor with skill (floor 0.7 -> WindFloorAtMax, no-go cone smaller). Delta form: other mods' change to
    // the result stay. Owner step (inside GetSailForce): helmsman level from ShipTick. Elsewhere (helmsman's HUD wind
    // icon, Hud.UpdateShipHud, every frame): HelmSkill.Physics01, cached; the helmsman there is the local player.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Ship), nameof(Ship.GetWindAngleFactor))]
    private static void GetWindAngleFactor_Postfix(Ship __instance, ref float __result)
    {
        try
        {
            float s;
            SailingRules rules;
            if (ShipTick.Is(__instance))
            {
                s = ShipTick.S;
                rules = ShipTick.Rules;
            }
            else
            {
                rules = ServerRules.Current;
                if (rules.IsPending)
                {
                    return;
                }
                s = HelmSkill.Physics01(__instance);
            }
            if (s <= 0f)
            {
                return;
            }
            var env = EnvMan.instance;
            if (env == null)
            {
                return;
            }
            var d = Vector3.Dot(env.GetWindDir(), -__instance.transform.forward);
            __result += SailMath.WindBonus(d, s, rules.WindFloorAtMax, rules.NoGoShiftAtMax);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Ship.GetWindAngleFactor postfix", e);
        }
    }

    // Owner step only (ShipTick), two parts.
    // S5 sail response: vanilla SmoothDamp the sail force toward its target with a 1 s literal (no field). After it, me
    // move the stored force a bit further toward the same target (1 - exp(-rate * s * dt)): sail fill and empty faster,
    // never past the target, nothing compound. Target = vanilla's (3 lines copied from Ship.GetSailForce, 1.0.16: keep
    // in step after game updates), with the vanilla m_sailForceFactor (me never scale it).
    // G5 acceleration on the sail: only the part of the returned force along the bow x ShipTick.Accel (SailMath.BowBoost);
    // sideways push (leeway, heel) stay vanilla. Returned value only: stored m_sailForce stay unboosted, nothing compound.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Ship), nameof(Ship.GetSailForce))]
    private static void GetSailForce_Postfix(Ship __instance, float sailSize, float dt, ref Vector3 __result)
    {
        try
        {
            if (!ShipTick.Is(__instance))
            {
                return;
            }
            var s = ShipTick.S;
            if (s <= 0f)
            {
                return;
            }
            var rules = ShipTick.Rules;
            var env = EnvMan.instance;
            if (rules.SailResponseAtMax > 0f && env != null)
            {
                var factor = __instance.GetWindAngleFactor();
                factor *= Mathf.Lerp(0.25f, 1f, env.GetWindIntensity());
                var target = Vector3.Normalize(env.GetWindDir() + __instance.transform.forward)
                             * (factor * __instance.m_sailForceFactor * sailSize);
                var before = __instance.m_sailForce;
                var after = Vector3.Lerp(before, target, SailMath.CatchUp(rules.SailResponseAtMax, s, dt));
                __instance.m_sailForce = after;
                // Delta: another mod's change to the returned force stay.
                __result += after - before;
            }
            var accel = ShipTick.Accel;
            if (accel != 1f)
            {
                __result = SailMath.BowBoost(__result, __instance.transform.forward, accel);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Ship.GetSailForce postfix", e);
        }
    }

    // S4: active brake while the speed setting is Stop. ApplyEdgeForce is private and called only at the end of the
    // owner's in-water block: free gate "owner, in water, after every vanilla force". Forward speed decay at
    // BrakeAtMax * s per second (setting velocity after queued impulses = what vanilla damping do). Needs a helmsman
    // (ShipTick s): a ship left at Stop with nobody at the helm drift as vanilla.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Ship), nameof(Ship.ApplyEdgeForce))]
    private static void ApplyEdgeForce_Postfix(Ship __instance, float dt)
    {
        try
        {
            if (!ShipTick.Is(__instance))
            {
                return;
            }
            var s = ShipTick.S;
            var rules = ShipTick.Rules;
            if (s <= 0f || rules.BrakeAtMax <= 0f || __instance.m_speed != Ship.Speed.Stop)
            {
                return;
            }
            var body = __instance.m_body;
            if (body == null || body.isKinematic)
            {
                return;
            }
            var forward = __instance.transform.forward;
            var v = body.linearVelocity;
            var vf = Vector3.Dot(v, forward);
            body.linearVelocity = v - forward * (vf * SailMath.BrakeFraction(rules.BrakeAtMax, s, dt));
        }
        catch (Exception e)
        {
            PatchGuard.Report("Ship.ApplyEdgeForce postfix", e);
        }
    }
}
