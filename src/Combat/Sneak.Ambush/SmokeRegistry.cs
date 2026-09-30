using System.Collections.Generic;
using UnityEngine;

namespace MC.Combat.SneakAmbushMod;

// Me = every Smoke Screen cloud loaded on this game, and THE smoke rule (who is hidden from whom). Clouds add and
// remove themselves (OnEnable / OnDisable). Every game near a cloud has an instance, so the owner of every creature
// near it know it. Hot callers (AI sense postfix, health bar postfix every frame) first test Count == 0 (one int).
// Registry empty = AiMemory cleared (blind, reveal, observer cache): nothing to hide from any more.
internal static class SmokeRegistry
{
    // Cloud reach below its base (cloud sit on ground or water; feet a bit lower still count).
    internal const float BelowBase = 0.5f;

    private static readonly List<SmokeCloud> Clouds = new List<SmokeCloud>();

    internal static int Count => Clouds.Count;

    internal static IReadOnlyList<SmokeCloud> All => Clouds;

    // Shared network time (same on every game), seconds. Zero before a world runs.
    internal static double Now
    {
        get
        {
            var net = ZNet.instance;
            return net != null ? net.GetTimeSeconds() : 0d;
        }
    }

    internal static void Add(SmokeCloud cloud)
    {
        if (cloud != null && !Clouds.Contains(cloud))
        {
            Clouds.Add(cloud);
        }
    }

    internal static void Remove(SmokeCloud cloud)
    {
        Clouds.Remove(cloud);
        if (Clouds.Count == 0)
        {
            AiMemory.Clear();
        }
    }

    // What creatures aim at on a character (vanilla BaseAI.CanSeeTarget): body centre crouched, eye standing.
    internal static Vector3 TracedPoint(Character target)
    {
        if (target.IsCrouching() || target.m_eye == null)
        {
            return target.GetCenterPoint();
        }
        return target.m_eye.position;
    }

    // THE rule (design 2.9). Observer = transform vanilla pass (creature AI root, or turret). observerPoint = its
    // eye (sight) or position + 1 m (hearing). Hearing with BlocksHearing off: only blind counts.
    //   target not Player / rules pending         -> not hidden
    //   geometry (clouds) or observer blinded      -> hidden, unless observer is no creature AI, is a boss, or
    //                                                 target hit it lately (reveal)
    // Observer lookups and reveal lookup run only when something say hidden (rare). No allocation.
    internal static bool Hides(Transform observer, Vector3 observerPoint, Character target, bool hearing, out bool geometry)
    {
        geometry = false;
        if (!(target is Player) || observer == null)
        {
            return false;
        }
        var rules = ServerRules.Current;
        if (rules.IsPending)
        {
            return false;
        }
        var now = Now;
        var hidden = false;
        if (!hearing || rules.BlocksHearing)
        {
            geometry = GeometryHides(observerPoint, TracedPoint(target), rules, now);
            hidden = geometry;
        }
        if (!hidden && AiMemory.AnyBlinded)
        {
            hidden = AiMemory.IsBlinded(observer, now);
        }
        if (!hidden)
        {
            return false;
        }
        var creature = AiMemory.ObserverCreature(observer);
        if (creature == null || creature.IsBoss())
        {
            return false;
        }
        return !AiMemory.IsRevealed(observer, target, now);
    }

    // Pure geometry of the clouds active now, between an observer point and a target point:
    //   one inside, other outside          -> hidden (outside cannot see in, inside cannot see out)
    //   both inside same cloud, far apart  -> hidden beyond InsideSightRange
    //   neither inside, cloud between      -> hidden when BlocksLineOfSight (E1)
    internal static bool GeometryHides(Vector3 observerPoint, Vector3 targetPoint, AmbushRules rules, double now)
    {
        for (var i = 0; i < Clouds.Count; i++)
        {
            var cloud = Clouds[i];
            if (!cloud.IsActive(now, rules))
            {
                continue;
            }
            var a = cloud.Contains(observerPoint);
            var b = cloud.Contains(targetPoint);
            if (a != b)
            {
                return true;
            }
            if (a)
            {
                var range = rules.InsideSightRange;
                if ((observerPoint - targetPoint).sqrMagnitude > range * range)
                {
                    return true;
                }
                continue;
            }
            if (rules.BlocksLineOfSight && cloud.SegmentCrosses(observerPoint, targetPoint))
            {
                return true;
            }
        }
        return false;
    }

    // Point inside any cloud active now (In smoke cue, health bars).
    internal static bool InActiveCloud(Vector3 point, AmbushRules rules, double now)
    {
        for (var i = 0; i < Clouds.Count; i++)
        {
            var cloud = Clouds[i];
            if (cloud.IsActive(now, rules) && cloud.Contains(point))
            {
                return true;
            }
        }
        return false;
    }

    // Health bar rule (design 2.12), local player's game. Geometry only: blind and reveal are creature states.
    //   OnlyInside   creature inside an active cloud that the player is not inside -> no bar
    //   ThroughSmoke geometry of the smoke rule say hidden                           -> no bar
    internal static bool HidesHealthBar(Vector3 playerPoint, Vector3 creaturePoint, AmbushRules rules)
    {
        var now = Now;
        switch (rules.HealthBars)
        {
            case HealthBarMode.OnlyInside:
                for (var i = 0; i < Clouds.Count; i++)
                {
                    var cloud = Clouds[i];
                    if (cloud.IsActive(now, rules) && cloud.Contains(creaturePoint) && !cloud.Contains(playerPoint))
                    {
                        return true;
                    }
                }
                return false;
            case HealthBarMode.ThroughSmoke:
                return GeometryHides(playerPoint, creaturePoint, rules, now);
            default:
                return false;
        }
    }
}
