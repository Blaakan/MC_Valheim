using System;
using UnityEngine;
using Random = System.Random;

namespace MC.Farming.FishingFightMod;

// Rod against a struggling fish.
internal enum RodVerdict : byte
{
    Good,   // rod point away from the run, far enough off the line: reel bring line in
    Wrong,  // rod on the run side, or too close to the line: reel cost a lot, no line
}

// Me = the fight rules as pure maths (self test hammer them): which way the rod must point, how long calm and
// struggle last, stamina and line speeds. Vanilla numbers (reel cost, reel speed) come from the float and fish the
// caller pass, so prefab values and other mods' changes on them stay.
internal static class FightLogic
{
    // Side of the run, seen by the fisher looking at the fish: +1 = fish run to the fisher's right, -1 = left.
    internal const int Right = 1;
    internal const int Left = -1;

    // Yaw from 'from' to 'to' in degrees, flat (y ignored). Positive = 'to' turned to the right of 'from' (clockwise
    // seen from above, Unity left hand, same as Quaternion.Euler(0, +a, 0)). Zero vector = 0.
    internal static float SignedYaw(Vector3 from, Vector3 to)
    {
        from.y = 0f;
        to.y = 0f;
        if (from.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f)
        {
            return 0f;
        }
        return Vector3.SignedAngle(from, to, Vector3.up);
    }

    // Side pressure: fish run right -> rod must point left of the line (and the other way), at least minAngle off it.
    // lineDir = fisher -> float, rodDir = where the fisher's body face (the rod is in its hands). Line too short to
    // tell (float at the feet): Good (nothing to judge, fish almost in).
    internal static RodVerdict Judge(Vector3 lineDir, Vector3 rodDir, int side, float minAngle)
    {
        lineDir.y = 0f;
        rodDir.y = 0f;
        if (lineDir.sqrMagnitude < 0.01f || rodDir.sqrMagnitude < 1e-6f || side == 0)
        {
            return RodVerdict.Good;
        }
        var yaw = SignedYaw(lineDir, rodDir);
        return side * yaw <= -minAngle ? RodVerdict.Good : RodVerdict.Wrong;
    }

    // Flat unit vector to the fisher's right when looking along lineDir.
    internal static Vector3 RightOf(Vector3 lineDir)
    {
        lineDir.y = 0f;
        if (lineDir.sqrMagnitude < 1e-6f)
        {
            return Vector3.right;
        }
        return Vector3.Cross(Vector3.up, lineDir.normalized).normalized;
    }

    // Where the fish swim in a struggle: across the line to its side; away from the fisher too while it take line.
    internal static Vector3 RunDirection(Vector3 lineDir, int side, bool takingLine)
    {
        lineDir.y = 0f;
        var away = lineDir.sqrMagnitude < 1e-6f ? Vector3.forward : lineDir.normalized;
        var dir = RightOf(away) * (side >= 0 ? 1f : -1f);
        if (takingLine)
        {
            dir += away * 0.5f;
        }
        return dir.normalized;
    }

    // Calm time before the next fight (s): rule mean, +-40 %, hard fish fight more often.
    internal static float CalmDuration(FightRules rules, float d01, Random rng) =>
        rules.CalmSeconds * Range(rng, 0.6f, 1.4f) * Mathf.Lerp(1.2f, 0.8f, Mathf.Clamp01(d01));

    // Fight time (s): rule mean, +-33 %, longer for hard fish and for each star (vanilla: +1.5 s max per star).
    internal static float StruggleDuration(FightRules rules, float d01, int quality, Random rng) =>
        rules.StruggleSeconds * Range(rng, 0.67f, 1.33f) * Mathf.Lerp(0.8f, 1.2f, Mathf.Clamp01(d01))
        + Math.Max(0, quality - 1) * 0.5f;

    // Chance per second that a fighting fish turn to the other side (never in its first second on a side).
    internal static float SwitchRate(float d01) => Mathf.Lerp(0.1f, 0.5f, Mathf.Clamp01(d01));

    internal const float MinSecondsPerSide = 1f;

    // Shore or shallows ahead of the run (water less deep than the fish want): turn to the other side only when that
    // way is deeper: deep enough, or clearly deeper than ahead. Both ways shallow and alike (near a beach): keep the
    // side, the player's rule must not swap every second. Depths in m, 2 m along each run (FishPatches).
    internal const float ShallowTurnMargin = 0.25f;

    internal static bool TurnFromShallows(float ahead, float other, float minDepth) =>
        ahead < minDepth && (other >= minDepth || other > ahead + ShallowTurnMargin);

    // Catch bar height at this skill (0..1).
    internal static float ZoneSize(FightRules rules, float skill) =>
        Mathf.Lerp(rules.BarSize, rules.BarSizeAtMaxSkill, Mathf.Clamp01(skill));

    // Vanilla reel cost per second (FishingFloat.FixedUpdate): (pull cost + fish cost x quality), skill bring it down
    // to x maxSkillMultiplier at 100. Fish cost = Fish.GetStaminaUse (more while it fight).
    internal static float ReelCost(float pullStaminaUse, float fishStaminaUse, int quality, float maxSkillMultiplier,
        float skill)
    {
        var cost = pullStaminaUse + fishStaminaUse * Math.Max(1, quality);
        return Mathf.Lerp(cost, cost * maxSkillMultiplier, Mathf.Clamp01(skill));
    }

    // Vanilla reel speed (m/s) at this skill, times the rule.
    internal static float ReelSpeed(float pullLineSpeed, float pullLineSpeedMaxSkill, float skill, float multiplier) =>
        Mathf.Lerp(pullLineSpeed, pullLineSpeedMaxSkill, Mathf.Clamp01(skill)) * multiplier;

    // Reel in a fight on the good side: vanilla half speed while the fish fight.
    internal const float StruggleReelFactor = 0.5f;

    // How far (m) the float may be dragged past the line while the line still come in: vanilla 0.2 when calm; a
    // running fish hold the float about that far out, so a struggle allow 1 m (vanilla break at m_breakDistance, 4).
    internal const float CalmDrag = 0.2f;
    internal const float StruggleDrag = 1f;

    // Line taken by a fighting fish when not reeled (m/s): rule for a middle fish, slower easy fish, faster hard ones.
    internal static float LineRunSpeed(FightRules rules, float d01) =>
        rules.LineRunSpeed * Mathf.Lerp(0.6f, 1.4f, Mathf.Clamp01(d01));

    // Fish body swim speed in a struggle (m/s): fast enough to drag the float to the side where everyone see it.
    internal static float FishRunSpeed(float d01) => Mathf.Lerp(2.5f, 4.5f, Mathf.Clamp01(d01));

    private static float Range(Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
}
