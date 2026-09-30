using System.Collections.Generic;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Me = where alerted bosses are (design 2.8): creature within 60 m of one = in a boss fight, stay vanilla (adds and
// creatures near). Me look at loaded Character.IsBoss() creatures; alert read from the ZDO on games that no own them
// (owner: own flag). Me refresh at most once per second per game, when asked. List reused: no allocation.
internal static class BossFights
{
    internal const float Range = 60f;
    private const float RangeSqr = Range * Range;

    private static readonly List<Vector3> Positions = new List<Vector3>();
    private static float _nextRefresh = -1f;

    internal static bool IsNear(Vector3 position)
    {
        var now = Time.time;
        if (now >= _nextRefresh)
        {
            _nextRefresh = now + 1f;
            Refresh();
        }
        for (var i = 0; i < Positions.Count; i++)
        {
            if ((Positions[i] - position).sqrMagnitude < RangeSqr)
            {
                return true;
            }
        }
        return false;
    }

    private static void Refresh()
    {
        Positions.Clear();
        foreach (var character in Character.GetAllCharacters())
        {
            if (character == null || !character.IsBoss() || character.IsDead())
            {
                continue;
            }
            var nview = character.m_nview;
            if (nview == null || !nview.IsValid())
            {
                continue;
            }
            bool alerted;
            if (nview.IsOwner())
            {
                var ai = character.GetBaseAI();
                alerted = ai != null && ai.IsAlerted();
            }
            else
            {
                alerted = nview.GetZDO().GetBool(ZDOVars.s_alert);
            }
            if (alerted)
            {
                Positions.Add(character.transform.position);
            }
        }
    }

    internal static void Clear()
    {
        Positions.Clear();
        _nextRefresh = -1f;
    }
}
