#if DEBUG
using System.Collections;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.StatsPerCreatureMod;

// Multiplayer run (tools/Test-Multiplayer.ps1, scenario "modded"): this game is a client of a real dedicated server.
// This mod is client-side: the server never loads it, so for this mod the server is a server without the mod. No
// server half exists (nothing of this mod runs there) and there is no second player: what a friend's game would send
// (kill message, tame message) is sent here through the same network messages, from this game.
internal static partial class SelfTests
{
    // ---------- percreature.mp.session ----------

    private static IEnumerator RunMpSession()
    {
        if (!SelfTest.IsMultiplayerRun || ZNet.instance == null || ZNet.instance.IsServer())
        {
            SelfTest.Fail(MpSessionName, "this test only runs on a client joined to a dedicated server (tools/Test-Multiplayer.ps1)");
            yield break;
        }
        var rig = Rig.Begin(MpSessionName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(MpSessionName);
        try
        {
            var p = rig.P;
            var plugin = FindPlugin();
            var greyling = CreatureName(Greyling);
            var boarName = CreatureName(Boar);
            var wolfName = CreatureName(Wolf);
            if (!c.Check(plugin != null && greyling != null && boarName != null && wolfName != null, "plugin object and Greyling, Boar, Wolf prefabs found"))
            {
                c.Report();
                yield break;
            }
            c.Check(plugin.IsActive, $"the mod is active on a client of a dedicated server that does not run it ({plugin.State}: {plugin.StatusText})");
            c.Check(CreatureCounts.IsAvailable, "the API is available on the client");
            // Like character that never killed or tamed these three (rest of its kill table stay).
            rig.ForgetKills(greyling, boarName, wolfName);
            rig.SetTames(null);
            var dir = OpenDirection(rig, false, 3f);
            var side = Vector3.Cross(Vector3.up, dir);
            var near = Ground(p.transform.position + dir * 3f);
            var far = Ground(p.transform.position + OpenDirection(rig, false, 40f) * 40f);

            // ----- kill of a creature this client controls -----
            var victims = new Character[1];
            yield return SpawnNear(rig, Greyling, near, victims);
            if (c.Check(victims[0] != null && victims[0].m_nview.IsOwner(), "this client controls the Greyling it spawned"))
            {
                var before = Tally.Of(greyling);
                Hit(victims[0], p, Skills.SkillType.Clubs, 1e7f);
                var w = new Waiter();
                yield return WaitGone(victims[0], 5f, w);
                var after = Tally.Of(greyling);
                c.Check(w.Met && after.Grew(before, 1, 1, 0, 0, 0, 0), $"club kill on the server: Greyling +1 killed, melee +1 (died {w.Met}; before: {before}; after: {after})");
            }

            // ----- kill message as the game that controls a creature sends it to every attacker -----
            {
                var before = Tally.Of(greyling);
                Game.instance.RegisterKill(ZNet.GetUID(), greyling, 0, KillModifiers.Ranged, 2, true);
                yield return null;
                var after = Tally.Of(greyling);
                c.Check(after.Grew(before, 1, 0, 1, 0, 0, 0), $"the game's kill message for this player (2 attackers) counts Greyling +1 (before: {before}; after: {after})");
            }

            // ----- tame next to the player -----
            var holder = new Tameable[1];
            yield return SpawnTameable(rig, Boar, Ground(near + side * 2f), holder);
            var boar = holder[0];
            if (c.Check(boar != null && boar.m_nview.IsOwner(), "this client controls the boar it spawned"))
            {
                rig.ArmMessage();
                var mark = _watch.Mark();
                boar.Tame();
                yield return null;
                c.Check(boar.IsTamed() && rig.CenterText == Loc(boarName + TamedSuffix) && CreatureCounts.GetTames(boarName) == 1
                        && SameLines(TameLines(mark), TameLine(boarName, "message", Stored("1\t" + boarName))),
                    $"tame next to the player on the server: message shown and Boar +1 tamed (count {CreatureCounts.GetTames(boarName)}, "
                    + $"center text '{rig.CenterText}', log {Lines(TameLines(mark))})");

                // The mod keeps nothing on the creature: counting again (tame message from another game) leaves the
                // creature's network data as it is, and the game's own petting still answers.
                var zdo = boar.m_nview.GetZDO();
                mark = _watch.Mark();
                // Me read revision right around the count (same call): nothing else touch the creature in between.
                var revision = zdo.DataRevision;
                p.m_nview.InvokeRPC("Message", (int)MessageHud.MessageType.Center, boarName + TamedSuffix, 0);
                var revisionAfter = zdo.DataRevision;
                yield return null;
                c.Check(CreatureCounts.GetTames(boarName) == 2
                        && SameLines(TameLines(mark), TameLine(boarName, "message", Stored("2\t" + boarName))),
                    $"the game's tame message arriving as a network message for this player counts Boar +1 (count {CreatureCounts.GetTames(boarName)}, "
                    + $"log {Lines(TameLines(mark))})");
                c.Check(revisionAfter == revision, $"counting a tame changes nothing in the creature's network data (revision {revisionAfter}, was {revision})");
                rig.ArmMessage();
                var petted = boar.Interact(p, false, false);
                yield return null;
                c.Check(petted && rig.CenterText != NoMessage, $"the counted tame is an ordinary tame for the game: petting answers ('{rig.CenterText}')");
            }

            // ----- tame far away: nobody within 30 m, this client ran it and is the closest -----
            yield return SpawnTameable(rig, Wolf, far, holder);
            var wolf = holder[0];
            if (c.Check(wolf != null && wolf.m_nview.IsOwner() && Player.GetClosestPlayer(wolf.transform.position, MessageRange) == null
                        && Player.GetClosestPlayer(wolf.transform.position, float.MaxValue) == p,
                    "far setup: this client controls the wolf, nobody is within 30 m, this player is the closest loaded player"))
            {
                rig.ArmMessage();
                var mark = _watch.Mark();
                wolf.Tame();
                yield return null;
                c.Check(wolf.IsTamed() && rig.CenterText == NoMessage && CreatureCounts.GetTames(wolfName) == 1
                        && SameLines(TameLines(mark), TameLine(wolfName, "owner", Stored("2\t" + boarName, "1\t" + wolfName))),
                    $"tame far away on the server: no message, Wolf +1 tamed for this player (count {CreatureCounts.GetTames(wolfName)}, "
                    + $"center text '{rig.CenterText}', log {Lines(TameLines(mark))})");
            }

            var page = new Page();
            yield return ReadPage(page);
            if (c.Check(page.Ok, $"Player Statistics could not be read: {page.Problem}"))
            {
                var s = ParseSection(page.Entry);
                c.Check(s.Row(Loc(greyling)) == $"{Loc(greyling)}: 2 killed (melee 1, ranged 1)" && s.Row(Loc(boarName)) == $"{Loc(boarName)}: 2 tamed"
                        && s.Row(Loc(wolfName)) == $"{Loc(wolfName)}: 1 tamed",
                    $"the page on the server shows 'Greyling: 2 killed (melee 1, ranged 1)', 'Boar: 2 tamed', 'Wolf: 1 tamed' (found {s.Describe()})");
            }
        }
        finally
        {
            rig.End();
        }
        c.Report();
    }

    // ---------- percreature.mp.config ----------

    // Real settings. Only in the multiplayer run: its config files are throwaway copies (a single-player run would
    // write the player's real config file).
    private static IEnumerator RunMpConfig()
    {
        if (!SelfTest.IsMultiplayerRun)
        {
            SelfTest.Fail(MpConfigName, "this test writes settings: it only runs in the multiplayer run (throwaway config files)");
            yield break;
        }
        var rig = Rig.Begin(MpConfigName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(MpConfigName);
        var plugin = FindPlugin();
        if (plugin == null || Plugin.SortBy == null || Plugin.ShowWeaponTypes == null)
        {
            rig.End();
            SelfTest.Fail(MpConfigName, "plugin object or its settings not found");
            yield break;
        }
        var sortBefore = Plugin.SortBy.Value;
        var typesBefore = Plugin.ShowWeaponTypes.Value;
        var enabledBefore = plugin.Enabled.Value;
        try
        {
            // Here page must follow the real settings.
            Plugin.TestDisplay = null;
            var rows = InjectSettingsData(rig);
            if (!c.Check(rows != null, "Boar, Greyling, Deer and Neck prefabs exist"))
            {
                c.Report();
                yield break;
            }
            c.Check(sortBefore == SortOrder.MostKilled && typesBefore && enabledBefore, $"fresh config: SortBy = MostKilled, ShowWeaponTypes = true, Enabled = true (found {sortBefore}, {typesBefore}, {enabledBefore})");
            Plugin.SortBy.Value = SortOrder.MostKilled;
            Plugin.ShowWeaponTypes.Value = true;
            yield return CheckRows(c, "default settings: most killed first, weapon types in parentheses", rows.MostKilledTypes);
            Plugin.SortBy.Value = SortOrder.Name;
            Plugin.ShowWeaponTypes.Value = false;
            yield return CheckRows(c, "Display.SortBy = Name and Display.ShowWeaponTypes = false set while in game: alphabetical, no parentheses at the next open", rows.NamePlain);
            Plugin.SortBy.Value = SortOrder.MostKilled;
            Plugin.ShowWeaponTypes.Value = true;
            yield return CheckRows(c, "both settings set back: most killed first, parentheses back, no restart", rows.MostKilledTypes);

            // Enabled off and on, like the tick box in the MC Mods panel. From the character's real kill table again.
            rig.RestoreKills();
            yield return ToggleSteps(rig, c, plugin, () => plugin.Enabled.Value = false, () => plugin.Enabled.Value = true);
        }
        finally
        {
            Plugin.SortBy.Value = sortBefore;
            Plugin.ShowWeaponTypes.Value = typesBefore;
            if (plugin.Enabled.Value != enabledBefore)
            {
                plugin.Enabled.Value = enabledBefore;
            }
            rig.End();
        }
        c.Report();
    }
}
#endif
