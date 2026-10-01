using UnityEngine;

namespace MC.Exploration.SailingSkillMod;

// Me = the pure numbers of the ship bonuses (patches call me, self test sailing.math hammer me). s = Sailing level / 100
// (0..1). Every bonus grow in a straight line with s; s = 0 = vanilla exactly.
internal static class SailMath
{
    // Ship.GetWindAngleFactor constants (1.0.16): floor 0.7, headwind cutoff LerpStep(0.75, 0.8, d).
    internal const float VanillaFloor = 0.7f;
    internal const float CutLow = 0.75f;
    internal const float CutHigh = 0.8f;

    // Copy of vanilla Ship.GetWindAngleFactor on d = Dot(windDir, -shipForward) (+1 = wind straight onto the bow).
    // Keep in step with game updates (tools/Update-GameRefs.ps1 list Ship when it change).
    internal static float VanillaWindFactor(float d) =>
        Mathf.Lerp(VanillaFloor, 1f, 1f - Utils.Abs(d)) * (1f - Utils.LerpStep(CutLow, CutHigh, d));

    // Same shape with skill: floor lerp 0.7 -> floorAtMax, cutoff moved toward the bow by noGoShiftAtMax * s. Never
    // below vanilla (floorAtMax >= 0.7, shift >= 0), never above 1, grow with s.
    internal static float WindFactor(float d, float s, float floorAtMax, float noGoShiftAtMax)
    {
        var floor = Mathf.Lerp(VanillaFloor, floorAtMax, s);
        var cut = noGoShiftAtMax * s;
        return Mathf.Lerp(floor, 1f, 1f - Utils.Abs(d)) * (1f - Utils.LerpStep(CutLow + cut, CutHigh + cut, d));
    }

    // What the postfix add to the game's result (delta form: other mods' changes to the result stay).
    internal static float WindBonus(float d, float s, float floorAtMax, float noGoShiftAtMax) =>
        WindFactor(d, s, floorAtMax, noGoShiftAtMax) - VanillaWindFactor(d);

    // Half-angle of the no-go cone around the bow (degrees): wind closer to the bow than this = no pull at all.
    internal static float NoGoHalfAngle(float s, float noGoShiftAtMax) =>
        Mathf.Acos(Mathf.Clamp(CutHigh + noGoShiftAtMax * s, -1f, 1f)) * Mathf.Rad2Deg;

    // Multiplier 1 + bonus * s (acceleration, turning, rudder, map reveal).
    internal static float Scale(float bonusAtMax, float s) => 1f + bonusAtMax * s;

    // Share of the way from the stored sail force to its target done this step (exponential catch-up at rate * s per
    // second). 0 at s = 0, never over 1 (never past the target). Under 10% per 0.02 s step at the largest setting;
    // float round it to exactly 1 only for absurd rate * dt (over ~17), which is still fine (= reach the target).
    internal static float CatchUp(float ratePerSecond, float s, float dt) => 1f - Mathf.Exp(-ratePerSecond * s * dt);

    // Acceleration on the sail: part of the force along the bow x accel, sideways part (leeway, heel) and up part
    // untouched. forward = unit bow direction. accel 1 = same force.
    internal static Vector3 BowBoost(Vector3 force, Vector3 forward, float accel) =>
        force + forward * (Vector3.Dot(force, forward) * (accel - 1f));

    // Share of the forward speed the brake take this step (rate * s per second), at most all of it.
    internal static float BrakeFraction(float ratePerSecond, float s, float dt) => Mathf.Min(1f, ratePerSecond * s * dt);

    // Multiplier on the hit damage of a ship.
    internal static float DamageFactor(float reductionAtMax, float s) => 1f - reductionAtMax * s;

    // One vanilla owner step of the forward speed along the bow (Ship.CustomFixedUpdate, 1.0.16): thrust as a speed
    // change per step, then quadratic drag vf^2 * dampingForward * submersion, clamped to 1 per step. Self test use it
    // to show that scaling the push along the bow and the drag by the same factor keep the top speed and shorten the
    // time to reach it.
    internal static float ForwardStep(float v, float thrustPerStep, float dampingForward, float submersion)
    {
        v += thrustPerStep;
        var drag = v * v * Utils.Sign(v) * dampingForward * submersion;
        return v - Utils.Clamp(drag, -1f, 1f);
    }
}
