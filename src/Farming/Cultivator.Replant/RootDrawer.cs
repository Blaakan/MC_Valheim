using UnityEngine;

namespace MC.Farming.CultivatorReplantMod;

// What the last grow try of a Yggdrasil transplant sapling decided (RootGate.BeforeGrow, sapling owner only).
internal enum RootState
{
    // No try yet (or the try let vanilla Grow run).
    Unknown,
    // No Ancient Root in reach: sapling wait.
    NoRoot,
    // Root there but its sap not more than the cost (drains this peer sent and not landed yet count, RootGate
    // EffectiveLevel): sapling wait for it to refill.
    Waiting,
    // Client still wait for server rules: no drain, sapling wait.
    Pending,
}

// Me = small memory on each Yggdrasil transplant sapling, for RootGate. Content put me on the sapling prefab, every
// planted sapling get own copy by Instantiate. Fields internal = Unity no serialize them: each copy start empty (no
// try time, no root), never carry a root from the prefab. Me hold no logic: RootGate read and write me.
internal sealed class RootDrawer : MonoBehaviour
{
    // Time.time of next grow try. Vanilla call Grow every SlowUpdater pass once due; RootGate try once per 10 s.
    internal float NextTry;

    // Ancient Root this sapling draw from (found once, kept). Unity null = root unloaded or gone: look again.
    internal ResourceRoot Root;

    // Search radius Root was found with. RootRange setting shrink below it = me look again (root may be out of
    // reach now). Root itself never move.
    internal float RootRadius;

    // Time.time of next root lookup for the hover text (hover come every frame: at most one lookup per second).
    internal float NextLookup;

    // What last grow try decided. Self tests read it.
    internal RootState LastState;
}
