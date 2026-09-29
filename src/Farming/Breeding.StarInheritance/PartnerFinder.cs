using UnityEngine;

namespace MC.Farming.BreedingStarInheritanceMod;

// Me find the other parent. Same name test and distance test as vanilla SpawnSystem.GetNrOfInstances
// (prefab name + "(Clone)", range 0 or less = no limit, exactly at range count), but me return the NEAREST one.
// Loop BaseAI.BaseAIInstances once: only at conception and on pregnant ticks without good note.
internal static class PartnerFinder
{
    // Separate partner prefab if species have one, else own prefab (vanilla cache, or resolve like vanilla).
    // Me never write vanilla field.
    internal static GameObject PartnerPrefab(Procreation proc)
    {
        if (proc.m_seperatePartner != null)
        {
            return proc.m_seperatePartner;
        }
        if (proc.m_myPrefab != null)
        {
            return proc.m_myPrefab;
        }
        var nview = proc.m_nview;
        var scene = ZNetScene.instance;
        if (nview == null || !nview.IsValid() || scene == null)
        {
            return null;
        }
        return scene.GetPrefab(nview.GetZDO().GetPrefab());
    }

    // Range for partner look-up at birth: animals roam pen while pregnant, so me use population range too.
    // Any of the two at 0 or less (other mod say "no limit") = no limit.
    internal static float BirthRange(Procreation proc)
    {
        var partner = proc.m_partnerCheckRange;
        var total = proc.m_totalCheckRange;
        if (!(partner > 0f) || !(total > 0f))
        {
            return 0f;
        }
        return Mathf.Max(partner, total);
    }

    // readyOnly = vanilla conception filter: creature with Procreation must be ReadyForProcreation (tamed, not
    // pregnant, not hungry); creature without one count as is. Else = birth filter: any tamed one.
    internal static Character FindNearest(Procreation proc, bool readyOnly, float range, out float distance)
    {
        distance = 0f;
        var prefab = PartnerPrefab(proc);
        if (prefab == null)
        {
            return null;
        }
        var wanted = prefab.name + "(Clone)";
        var self = proc.gameObject;
        var center = proc.transform.position;
        Character best = null;
        var bestDistance = float.MaxValue;
        var all = BaseAI.BaseAIInstances;
        for (var i = 0; i < all.Count; i++)
        {
            var ai = all[i];
            if (ai == null || ReferenceEquals(ai.gameObject, self))
            {
                continue;
            }
            var character = ai.m_character;
            if (character == null || ai.gameObject.name != wanted)
            {
                continue;
            }
            var d = Vector3.Distance(center, ai.transform.position);
            if ((range > 0f && d > range) || d >= bestDistance)
            {
                continue;
            }
            if (readyOnly)
            {
                var other = ai.GetComponent<Procreation>();
                if (other != null && !other.ReadyForProcreation())
                {
                    continue;
                }
            }
            else if (!character.IsTamed())
            {
                continue;
            }
            best = character;
            bestDistance = d;
        }
        if (best != null)
        {
            distance = bestDistance;
        }
        return best;
    }
}
