#if DEBUG
using System.Collections;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CrossbowStaysLoadedMod;

// Debug build only. Multiplayer run (tools/Test-Multiplayer.ps1: real dedicated server, this game joined). Me is a
// client-only mod: the server never load me, so there is no server half; a test check this game and what it send.
//   crossbow.mp.remote-flag     M01 (scenario modded): the "weapon loaded" flag on the player's networked object, the
//                               one value other players' games read to draw the bolt (Player.IsWeaponLoaded for a
//                               player they do not own, WeaponLoadState), through reload, swap, swap back and a shot
//                               that needs no reload. What the friend's screen show stay by hand.
//   crossbow.mp.vanilla-server  M05 (scenario vanilla-server: the server has no MC mod): me active, crossbow keep its
//                               load across a swap, flag set
// The W tests of this run (crossbow.mp.defaults and so on) live in SelfTests.Weapons.cs.
internal static partial class SelfTests
{
    private static bool LoadedFlag(Player p)
    {
        var zdo = p.m_nview != null && p.m_nview.IsValid() ? p.m_nview.GetZDO() : null;
        return zdo != null && zdo.GetBool(ZDOVars.s_weaponLoaded);
    }

    // Bolt model of the held crossbow: WeaponLoadState switch it by Player.IsWeaponLoaded. found = model has one.
    private static bool BoltShown(Player p, out bool found)
    {
        var state = p.GetComponentInChildren<WeaponLoadState>();
        found = state != null && state.m_loaded != null;
        return found && state.m_loaded.activeInHierarchy;
    }

    private static IEnumerator RemoteFlagBody(Rig rig, Checks c)
    {
        var p = rig.P;
        if (!c.Check(SelfTest.IsMultiplayerRun && ZNet.instance != null && !ZNet.instance.IsServer(), "this game is not a player joined to a server"))
        {
            yield break;
        }
        DefaultSettings(rig, c, true);
        var bow = rig.Give(BowName);
        var crossbow = rig.Give(ArbalestName);
        if (!c.Check(bow != null && crossbow != null && rig.GiveAmmo(BoltName, 2),
                $"could not give {BowName} ({bow != null}), {ArbalestName} ({crossbow != null}) and 2 {BoltName}"))
        {
            yield break;
        }
        rig.EmptyHands();
        var s = new Swap();
        var loadTime = crossbow.GetWeaponLoadingTime();

        yield return Take(rig, crossbow, s, loadTime + 4f);
        if (!c.Check(s.InHand && s.Loaded, $"set-up: {ArbalestName} did not load ({s})"))
        {
            yield break;
        }
        c.Check(LoadedFlag(p), "after the reload the player's networked 'weapon loaded' flag is false");

        yield return Take(rig, bow, s, 0f);
        if (!c.Check(s.InHand && !p.IsItemEquiped(crossbow), $"could not swap to {BowName}"))
        {
            yield break;
        }
        c.Check(!LoadedFlag(p), $"with {BowName} in hand the networked 'weapon loaded' flag still says loaded");

        yield return Take(rig, crossbow, s, 1f, 0.3f);
        if (!c.Check(s.InHand, $"{ArbalestName} did not come back into the hand"))
        {
            yield break;
        }
        c.Check(s.LoadedAtOnce && !s.ReloadQueued, $"{ArbalestName} was not loaded at once after swapping back ({s})");
        c.Check(LoadedFlag(p), "after swapping back to the loaded crossbow the networked 'weapon loaded' flag is false: other players would see no bolt");

        // Own copy of the bolt model (same component draws it for other players, from the flag).
        var found = false;
        var shown = false;
        var begin = Time.realtimeSinceStartup;
        while (!shown && Time.realtimeSinceStartup - begin < 2f)
        {
            shown = BoltShown(p, out found);
            if (!shown)
            {
                rig.Tick();
                yield return null;
            }
        }
        if (found)
        {
            c.Check(shown, "the crossbow model in the hand does not show its bolt after swapping back");
        }
        else
        {
            c.Note($"the model of {ArbalestName} has no loaded / unloaded switch (WeaponLoadState): only the flag was checked");
        }

        // Shot straight away: no reload in between.
        var shot = new Shot();
        yield return Fire(rig, crossbow, shot);
        if (!c.Check(shot.Started && shot.Left, $"the loaded {ArbalestName} did not fire after swapping back (attack started {shot.Started}, load gone {shot.Left})"))
        {
            yield break;
        }
        c.Check(shot.Projectile, "the shot let no bolt out");
        c.Check(!LoadedFlag(p), "after the shot the networked 'weapon loaded' flag still says loaded");
        c.Check(!LoadedState.HasStamp(crossbow), $"after the shot the {ArbalestName} still carries the Loaded mark ('{MarkOf(crossbow)}')");
        c.Summary = "joined to a dedicated server: the networked 'weapon loaded' flag of the player went true (reload), false (swap away), "
                    + "true at once (swap back, no reload), false (shot fired with no reload before it)";
    }

    private static IEnumerator VanillaServerBody(Rig rig, Checks c)
    {
        var p = rig.P;
        var view = FeatureRegistry.Find(ModInfo.Guid);
        if (!c.Check(view != null && view.Value.IsActive,
                $"on a server without the mod the mod is not active: {(view != null ? view.Value.State + " (" + view.Value.Status + ")" : "not registered")}"))
        {
            yield break;
        }
        c.Check(SelfTest.Scenario == VanillaServerScenario && ZNet.instance != null && !ZNet.instance.IsServer(),
            $"this game is not a player joined to a server without the mod (scenario '{SelfTest.Scenario}')");
        DefaultSettings(rig, c, true);
        var bow = rig.Give(BowName);
        var crossbow = rig.Give(ArbalestName);
        if (!c.Check(bow != null && crossbow != null, $"could not give {BowName} ({bow != null}) and {ArbalestName} ({crossbow != null})"))
        {
            yield break;
        }
        rig.EmptyHands();
        yield return Cycle(rig, c, crossbow, bow, true, "server without the mod");
        c.Check(ReferenceEquals(p.m_weaponLoaded, crossbow) && LoadedFlag(p),
            "after swapping back the crossbow is not loaded, or the networked 'weapon loaded' flag is false");
        c.Summary = $"joined to a dedicated server that has no MC mod: the mod is active ({view.Value.Status}) and {ArbalestName} was loaded at once after a swap to {BowName} and back";
    }
}
#endif
