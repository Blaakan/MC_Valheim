#if DEBUG
using System.Collections;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.HarpoonHooksTamesMod;

// Debug build only. Multiplayer self tests (tools/Test-Multiplayer.ps1, scenario "modded"): this game is a client
// joined to a real dedicated server. The mod is client-only (BepInProcess valheim.exe), so the server never loads
// it and has no server step of ours. The server is NOT the "game without the mod" of the hand-off items: a dedicated
// server simulate no creature (see harpoon.mp.handoff). No second player exists: a tame simulated by a friend's game,
// and what the friend see on his screen, stay hand tests.
internal static partial class SelfTests
{
    private static bool JoinedAsClient(Checks c)
    {
        var net = ZNet.instance;
        return c.Check(net != null && !net.IsServer() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected && net.GetServerPeer() != null,
            "this game is not a client joined to a server");
    }

    // ---------- harpoon.mp.hook: tame this client simulates ----------

    private static IEnumerator RunMpHook()
    {
        var c = new Checks(MpHookName);
        var rig = Rig.Create(MpHookName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null || !JoinedAsClient(c))
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            yield return rig.Stage(12f, 10f, c);
            var boar = rig.Spawn("Boar", rig.At(10f), true);
            if (!c.Check(boar != null, "could not spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.8f);
            var zdo = boar.m_nview.GetZDO();
            var key = AttackerKey(p);
            c.Check(boar.m_nview.IsOwner() && TameRules.IsTame(boar) && !p.IsPVPEnabled(), "setup: tamed Boar simulated by this client, PvP off");

            var hits = Stat(PlayerStatType.EnemyHits);
            var before = Snap.Take(boar);
            var mark = Tap.Mark();
            ClearCenter();
            var shot = DirectHit(rig, h, boar);
            CheckHarmlessHook(c, "joined to a server without the mod", rig, h, boar, before, shot);
            c.Check(Tap.CountSince(mark, $"Hooked tame {boar.m_name} (simulated by this game)") == 1, "one Debug line 'Hooked tame ... (simulated by this game)'");
            c.Check(!zdo.GetBool(key) && zdo.GetInt(ZDOVars.s_attackers) == 0 && Tap.CountSince(mark, "Cleared the attacker mark") == 1,
                $"no kill-credit mark left on the tame: this client runs it and cleared it ({Marks(zdo, key)})");
            c.Check(Near(Stat(PlayerStatType.EnemyHits), hits + 1f), "Enemy Hits +1 (this game runs the animal)");
            yield return CheckCalm(c, "joined to a server", rig, boar, before, 2f);
            var pull = new PullResult();
            yield return Walk(rig, h, boar, WalkSpeed, 2.4f, pull);
            c.Check(pull.Moved >= 2f && !pull.Ended && boar != null && Near(boar.GetHealth(), boar.GetMaxHealth()),
                $"walking away drags it, unharmed (the player went {F(pull.Walked)} m in {F(pull.Seconds)} s, it came {F(pull.Moved)} m, hook lost {pull.Ended})");
            c.Check(ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected, "still connected to the server");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.mp.handoff: tame held by another game (the server) at the throw ----------

    // Dedicated server never simulate a creature. Its own build of the game (valheim_server_Data, NOT the client
    // code in .ref) do in Game.FixedUpdate: ZNet.SetReferencePosition(1000000, 0, 1000000), every physics step. So
    // no place a player stand is in its active area: it make no object there, a routed call for an object it hold
    // is dropped (ZRoutedRpc.HandleRoutedRPC find no instance), and ZDOMan.ReleaseNearbyZDOS (every 2 s) give what
    // it own near a player to that player. Seen in two runs: tame given back after 1.91 s (191 m from the world
    // centre) and after 1 s (19.7 m from it). So the server is NOT the friend's game of M01 / M05 (hook, drag,
    // stamina, block, attacker mark on a game without the mod): that need a second player's game.
    // What is real here: for up to 2 s the tame belong to another game. Me throw in the frame of the hand-off, so
    // no answer can come in between: this game must take the tame as "simulated by another player" (hit sent away,
    // nothing applied here). The server drop that hit. Tame come back: it must be whole and hook like any tame.
    private static IEnumerator RunMpHandoff()
    {
        var c = new Checks(MpHandoffName);
        var rig = Rig.Create(MpHandoffName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null || !JoinedAsClient(c))
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            var server = ZNet.instance.GetServerPeer().m_uid;
            yield return rig.Stage(10f, 3f, c);
            var boar = rig.Spawn("Boar", rig.At(7f), true);
            if (!c.Check(boar != null, "could not spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(1.5f); // server learn the boar and that it is tamed
            if (!c.Check(boar != null && boar.m_nview.IsValid() && boar.m_nview.IsOwner() && TameRules.IsTame(boar) && !p.IsPVPEnabled()
                         && Near(boar.GetHealth(), boar.GetMaxHealth()),
                    "setup: tamed Boar at full health simulated by this client, PvP off"))
            {
                c.Report();
                yield break;
            }
            var nview = boar.m_nview;
            var zdo = nview.GetZDO();
            var key = AttackerKey(p);

            // Hand over and throw, same frame.
            var before = Snap.Take(boar);
            var hits = Stat(PlayerStatType.EnemyHits);
            var mark = Tap.Mark();
            ClearCenter();
            zdo.SetOwner(server);
            c.Check(!nview.IsOwner() && zdo.GetOwner() == server, "setup: the tame is handed to the server, this game does not simulate it any more");
            var shot = DirectHit(rig, h, boar);
            c.Check(shot.Valid && shot.Stopped, "M01: the harpoon is allowed to hit and stops on a tame another game holds");
            if (h.SingleHit)
            {
                c.Check(Near(shot.RaiseSkill, 0f) && Near(shot.Adrenaline, 0f),
                    $"M01: that hit pays no Spears skill ({F(shot.RaiseSkill)}) and no adrenaline ({F(shot.Adrenaline)})");
            }
            c.Check(Tap.CountSince(mark, $"Hooked tame {boar.m_name} (simulated by another player)") == 1,
                "M01: one Debug line 'Hooked tame ... (simulated by another player)'");
            c.Check(HookOn(boar, h) == null && Untouched(boar, before) && Near(Stat(PlayerStatType.EnemyHits), hits)
                    && Tap.CountSince(mark, "Cleared the attacker mark") == 0,
                "M01: this game sent the hit to the game holding the tame and applied nothing itself (no hook made here, Enemy Hits unchanged, "
                + $"no attacker mark handled here; {Describe(boar, before)})");

            // Who answer: the server give the tame back (today), or a game that do simulate it hook it and its
            // "harpooned" message come here.
            var box = new Box();
            var want = HarpoonedText(boar);
            yield return Until(() => boar == null || nview.IsOwner() || Center() == want, 8f, box);
            if (!c.Check(boar != null, $"M01: the tame is still there after the hand-off (gone after {F(box.Seconds)} s)"))
            {
                c.Report();
                yield break;
            }
            if (!nview.IsOwner() && Center() == want)
            {
                // Not a dedicated server of today: a game that simulate the tame took the hit. Rest need new test.
                c.Note($"another game simulates the tame and hooked it {F(box.Seconds)} s after the throw ('{want}' came back): the drag, stamina, block and "
                       + "attacker mark checks of M01 / M05 could be played against that game; this test does not play them");
                yield return Wait(1f);
                c.Check(boar != null && Near(boar.GetHealth(), before.Health) && !zdo.GetBool(ZDOVars.s_alert),
                    "M01: the game that simulates the tame hooked it without damage and did not alert it");
                c.Check(ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected, "still connected to the server");
                c.Report();
                yield break;
            }
            if (!c.Check(nview.IsOwner(),
                    $"setup: {F(box.Seconds)} s after the hand-off the tame was neither given back nor hooked by the game holding it (owner {zdo.GetOwner()}, server {server})"))
            {
                c.Report();
                yield break;
            }
            c.Note($"the server gave the tame back {F(box.Seconds)} s after the hand-off: a dedicated server simulates no creature (its game keeps its reference point at "
                   + "(1000000, 0, 1000000), so it makes no object where players are, drops the hit sent to it and hands what it owns to the nearest player within 2 s). "
                   + "NOT tested here, it needs a second player's game: the hook, the drag, the stamina drain, the block release and the attacker mark on a tame "
                   + "simulated by a game without the mod");
            yield return Wait(1f); // late answer to the lost hit would show now
            if (!c.Check(boar != null && nview.IsOwner(), "M01: the tame stays with this game after it came back"))
            {
                c.Report();
                yield break;
            }
            c.Check(HookOn(boar, h) == null && Center() != want,
                $"M01: the hit sent to the server was dropped there: no hook and no 'harpooned' message (message '{Center()}')");
            c.Check(boar.IsTamed() && Untouched(boar, before) && Near(boar.GetHealth(), boar.GetMaxHealth()) && !zdo.GetBool(key)
                    && zdo.GetInt(ZDOVars.s_attackers) == 0,
                $"M01: back with this game the tame is whole: tamed, full health, no damaging hit, not pushed, not alerted, no attacker mark ({Describe(boar, before)}; {Marks(zdo, key)})");

            // Thrown again, now this game simulate it: plain harmless hook.
            before = Snap.Take(boar);
            mark = Tap.Mark();
            ClearCenter();
            shot = DirectHit(rig, h, boar);
            CheckHarmlessHook(c, "M01 thrown again after the tame came back", rig, h, boar, before, shot);
            c.Check(Tap.CountSince(mark, $"Hooked tame {boar.m_name} (simulated by this game)") == 1,
                "M01: the Debug line of the second throw says 'simulated by this game'");
            c.Check(!zdo.GetBool(key) && zdo.GetInt(ZDOVars.s_attackers) == 0, $"M01: no kill-credit mark left by that hook ({Marks(zdo, key)})");
            c.Check(ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected, "still connected to the server");
            c.Report();
        }
        finally
        {
            rig.Done(); // take the boar back if another game hold it, remove it for everybody
        }
    }

    // ---------- harpoon.mp.toggle: Enabled off and on for real ----------

    private static IEnumerator RunMpToggle()
    {
        var c = new Checks(MpToggleName);
        var rig = Rig.Create(MpToggleName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        var found = FeatureRegistry.Find(ModInfo.Guid);
        try
        {
            var h = Harpoon.Resolve(c);
            // Only in a multiplayer run: its config files are throwaway. Never write Enabled in a player's own config.
            if (h == null || !c.Check(SelfTest.IsMultiplayerRun, "not a multiplayer test run: the Enabled setting is not touched")
                          || !c.Check(found != null && found.Value.Enabled != null, "the mod is not in the MC Mods list"))
            {
                c.Report();
                yield break;
            }
            var view = found.Value;
            yield return rig.Stage(10f, 3f, c);
            var tame = rig.Spawn("Boar", rig.At(8f), true);
            if (!c.Check(tame != null, "could not spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.6f);
            c.Check(view.IsActive && AllPatched() && !rig.P.IsPVPEnabled(), $"on: active, patches in place, PvP off ({view.State})");

            // What unticking the mod in MC Mods (or Enabled = false in the file) does.
            var box = new Box();
            view.Enabled.Value = false;
            yield return Until(() => view.IsDisabledByUser, 5f, box);
            c.Check(box.Ok && NonePatched(), $"Enabled = false: the mod is off and none of its patches is left ({view.State}: {view.Status})");
            CheckPassesThrough(c, "T18/T19 Enabled = false", rig, h, tame);

            view.Enabled.Value = true;
            yield return Until(() => view.IsActive, 5f, box);
            c.Check(box.Ok && AllPatched(), $"Enabled = true again: active, patches back, no restart ({view.State}: {view.Status})");
            var before = Snap.Take(tame);
            ClearCenter();
            var shot = DirectHit(rig, h, tame);
            CheckHarmlessHook(c, "T18 Enabled = true again", rig, h, tame, before, shot);
            c.Report();
        }
        finally
        {
            if (SelfTest.IsMultiplayerRun && found != null && found.Value.Enabled != null && !found.Value.Enabled.Value)
            {
                found.Value.Enabled.Value = true;
            }
            rig.Done();
        }
    }
}
#endif
