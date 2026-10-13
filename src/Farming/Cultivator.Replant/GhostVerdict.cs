using BepInEx.Bootstrap;
using UnityEngine;

namespace MC.Farming.CultivatorReplantMod;

// Me = second look at the placement ghost, only for players who also run PlantEasily (other author's mod).
// Vanilla Player.UpdatePlacementGhost refuse our Yggdrasil transplant away from an Ancient Root (Piece.m_mustConnectTo:
// status Invalid) and any piece inside a no-build zone (status NoBuildZone). PlantEasily has a postfix on the same
// method: it look at the aimed ghost with its own plant checks (ground, biome, room, open sky: it know nothing of
// roots or no-build zones) and, when those pass, write Valid over the game's verdict. Ghost green, transplant planted
// where it never grow, or where nobody may build.
// Me run after it (Patches/PlayerPatches.cs: last postfix, after PlantEasily's) and put these two refusals back on a
// transplant ghost of ours: the status, and the red ghost. Player.TryPlacePiece call UpdatePlacementGhost itself and
// read the status right after, so the place button is refused too, with the game's own message; Player.PlacePiece
// check nothing and only TryPlacePiece call it for the aimed ghost.
// Not looked at: PlantEasily's grid copies around the aimed ghost (its own objects, its own planting). Aimed ghost
// refused = nothing of the grid planted. Aimed ghost fine = a Yggdrasil copy out of reach of the root wait, Replant
// give it back (RootGate).
// PlantEasily not installed = nothing done, ever. This run every frame for the local player (LateUpdate), so:
// reference checks first, no allocation. No ghost or status not Valid = out. A ghost is looked at once, when new
// (one name read). Then root search only for the Yggdrasil ghost (RootGate.FindRoot: root layers, shared buffer;
// cheaper than the game's own search for the same ghost) and the game's own no-build test.
internal static class GhostVerdict
{
    // PlantEasily's plugin GUID = its Harmony id (it patch with a Harmony of that name).
    internal const string PlantEasilyId = "advize.PlantEasily";

    private const int Unknown = 0;
    private const int Here = 1;
    private const int Absent = 2;

    // Is PlantEasily loaded? Looked up at the first ghost (in a world: every plugin loaded long before).
    private static int _otherMod;

    // Ghost looked at last, and what me found. Piece = its Piece when it is one of our transplants, else null.
    private static GameObject _ghost;
    private static Piece _piece;
    private static bool _needsRoot;

#if DEBUG
    // Self test: second look off (control: what PlantEasily alone make of the ghost). Never in release.
    internal static bool TestOff { get; set; }
#endif

    // Player.UpdatePlacementGhost postfix, after every other mod's. Caller catch.
    internal static void AfterGhostUpdate(Player player)
    {
        if (_otherMod == Absent)
        {
            return;
        }
        var ghost = player.m_placementGhost;
        // Only a Valid can be wrong here: nobody but the game refuse, me never allow.
        if (ReferenceEquals(ghost, null) || player.m_placementStatus != Player.PlacementStatus.Valid)
        {
            return;
        }
#if DEBUG
        if (TestOff)
        {
            return;
        }
#endif
        if (_otherMod == Unknown)
        {
            _otherMod = Chainloader.PluginInfos.ContainsKey(PlantEasilyId) ? Here : Absent;
            if (_otherMod == Absent)
            {
                return;
            }
        }
        if (!ReferenceEquals(ghost, _ghost))
        {
            Look(ghost);
        }
        var piece = _piece;
        if (ReferenceEquals(piece, null))
        {
            return;
        }
        // Ghost destroyed under the game (it then leave an old status): nothing to judge.
        if (ghost == null)
        {
            return;
        }

        // Same order as the game: no root = Invalid, then a no-build zone win over it.
        var pos = ghost.transform.position;
        var status = Player.PlacementStatus.Valid;
        if (_needsRoot && RootGate.FindRoot(pos, piece.m_connectRadius) == null)
        {
            status = Player.PlacementStatus.Invalid;
        }
        if (Location.IsInsideNoBuildLocation(pos))
        {
            status = Player.PlacementStatus.NoBuildZone;
        }
        if (status == Player.PlacementStatus.Valid)
        {
            return;
        }
        player.m_placementStatus = status;
        // What the game's Player.SetPlacementGhostValid(false) do, without its component lookup.
        piece.SetInvalidPlacementHeightlight(true);
    }

    // New ghost: one of our transplant saplings? The game give the ghost its prefab's name. Once per ghost (name read
    // allocate), result kept.
    private static void Look(GameObject ghost)
    {
        _ghost = ghost;
        _piece = null;
        _needsRoot = false;
        if (ghost == null || !RootGate.IsTransplantSapling(ghost.name.GetStableHashCode()))
        {
            return;
        }
        var piece = ghost.GetComponent<Piece>();
        if (piece == null)
        {
            return;
        }
        _piece = piece;
        // Same field the game's root rule read on this ghost.
        _needsRoot = piece.m_mustConnectTo != null;
    }
}
