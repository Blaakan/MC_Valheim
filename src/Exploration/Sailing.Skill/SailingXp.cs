using System.Globalization;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SailingSkillMod;

// Me = Sailing XP (G1). Skills live on own game, so the helmsman's own game pay it: every 1 s (vanilla cadence of Run,
// Swim, Ride) while the local player hold a helm (Player.GetControlledShip), flat (x/z) distance the ship moved since
// the last sample, same ship only. Under 1 m (bobbing, anchored drift) or over 60 m (teleport, ship just loaded) = no
// XP (vanilla distance stats use same limits, Player.UpdateStats). XP = km * XpPerKm through Player.RaiseSkill: Rested
// bonus, other status effects and world skill-gain modifier apply like for vanilla skills. Paddling counts too.
// Passengers earn nothing. Rules pending = nothing (vanilla).
internal static class SailingXp
{
    internal const float SampleInterval = 1f;
    internal const float MinStep = 1f;
    internal const float MaxStep = 60f;

    private static Ship _ship;
    private static Vector3 _last;
    private static float _next;
    private static float _sessionMetres;
    private static float _sessionXp;
    private static string _sessionShip;

    // Feature on/off, spawn: forget the helm session (no log line).
    internal static void Reset()
    {
        _ship = null;
        _next = 0f;
        _sessionMetres = 0f;
        _sessionXp = 0f;
        _sessionShip = null;
    }

    // Pure (self test): XP for one sample.
    internal static float XpFor(float flatMetres, SailingRules rules)
    {
        if (rules == null || rules.IsPending || !(flatMetres >= MinStep) || flatMetres > MaxStep)
        {
            return 0f;
        }
        return flatMetres / 1000f * rules.XpPerKm;
    }

    // Player.Update postfix, local player only: one float compare most frames.
    internal static void Tick(Player player)
    {
        var now = Time.time;
        if (now < _next)
        {
            return;
        }
        _next = now + SampleInterval;
        var ship = player.IsDead() ? null : player.GetControlledShip();
        if (ship == null)
        {
            EndSession();
            return;
        }
        var pos = ship.transform.position;
        if (!ReferenceEquals(ship, _ship))
        {
            EndSession();
            _ship = ship;
            _last = pos;
            _sessionShip = Utils.GetPrefabName(ship.gameObject);
            var rules = ServerRules.Current;
            Log.Debug(rules.IsPending
                ? $"At the helm of {_sessionShip}: no Sailing XP until the server's rules arrive."
                : $"At the helm of {_sessionShip}: earning Sailing XP ({F(rules.XpPerKm)} per km).");
            return;
        }
        var dx = pos.x - _last.x;
        var dz = pos.z - _last.z;
        _last = pos;
        var metres = Mathf.Sqrt(dx * dx + dz * dz);
        var xp = XpFor(metres, ServerRules.Current);
        if (xp <= 0f)
        {
            return;
        }
        player.RaiseSkill(SailingSkill.Type, xp);
        _sessionMetres += metres;
        _sessionXp += xp;
    }

    // Helm left (or ship changed): one Debug line with what the session paid.
    private static void EndSession()
    {
        if (_ship == null && _sessionShip == null)
        {
            return;
        }
        if (_sessionShip != null)
        {
            Log.Debug($"Left the helm of {_sessionShip}: {F(_sessionMetres / 1000f)} km sailed, {F(_sessionXp)} Sailing "
                      + "XP (before bonuses).");
        }
        _ship = null;
        _sessionMetres = 0f;
        _sessionXp = 0f;
        _sessionShip = null;
    }

    private static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
