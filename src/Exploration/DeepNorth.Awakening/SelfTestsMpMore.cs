#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Debug build only. Later multiplayer self test (scenario "modded": this game is a player on a real dedicated server,
// both with the mod; tools in SelfTestsMp.cs):
//   dn.mp.killtarget.there  M10 with the player IN the area: after Kall on the server, 6 kills clear an area, storms
//                           always. The player kills 3 of its Jotun for real (3 of 6 on the console), stays, then the
//                           server's own ClearKillsMin / Max go down to 3: its log line, "The Jotun Retreat" on the
//                           player's screen, the console says cleared, the blizzard stops, the purple leaves the map
// Server half:
//   dn.mp.log               how many of the server's own log lines hold a text
internal static partial class SelfTests
{
    private const string MpTargetThereName = "dn.mp.killtarget.there";
    private const string StepLog = "dn.mp.log";

    private static void RegisterMpMore()
    {
        SelfTest.RegisterMultiplayer(MpTargetThereName, SelfTest.Modded, RunMpTargetThere);
        SelfTest.RegisterServerStep(StepLog, ServerLog);
    }

    private static void UnregisterMpMore()
    {
        SelfTest.UnregisterMultiplayer(MpTargetThereName);
        SelfTest.UnregisterServerStep(StepLog);
    }

    // The argument is the text; the answer is the count of this mod's log lines (since it was turned on) that hold it.
    private static IEnumerator ServerLog(string arg, object[] reply)
    {
        yield return null;
        var text = arg ?? "";
        var count = text.Length > 0 ? EnsureTap().Count(0, text) : -1;
        SelfTest.Answer(reply, text.Length > 0, "count=" + count.ToString(CultureInfo.InvariantCulture) + ";");
    }

    private static IEnumerator RunMpTargetThere()
    {
        var c = new Checks(MpTargetThereName);
        var s = new Server();
        var player = Player.m_localPlayer;
        var clock = new Clock();
        var hud = new HudLog();
        var spawned = new List<GameObject>();
        var stones0 = 0;
        var changed = false;
        var travelled = false;
        var cell = HeldCells.NoTag;
        var home = Vector3.zero;
        try
        {
            if (!AsPlayer(c))
            {
                c.Report();
                yield break;
            }
            _home ??= player.transform.position;
            home = _home.Value;
            yield return Ask(s, StepState, "");
            var haveArea = LandSlot(SlotStay, out var point, out cell);
            if (!c.Check(s.Ok, "the server answers (" + s.Reply + ")") || !c.Check(haveArea, "a dry invaded Deep North area exists"))
            {
                c.Report();
                yield break;
            }
            stones0 = s.Num("stones");
            changed = true;
            // Stage 3, storms always, 6 kills clear an area, no bands.
            yield return Ask(s, StepSet, "defaults=1;kall=0;rescan=1;stones=3;StormShare=100;NatureBandChance=0;ClearKillsMin=6;ClearKillsMax=6");
            yield return Until(() => StonesAre(3) && ServerRules.UsingServer && ServerRules.Current.StormShare == 100
                                     && ServerRules.Current.ClearKillsMin == 6 && ServerRules.Current.ClearKillsMax == 6, 8f);
            c.Check(s.Ok && StonesAre(3) && ServerRules.Current.StormShare == 100 && ServerRules.Current.ClearKillsMin == 6
                    && ServerRules.Current.ClearKillsMax == 6,
                "the server's stage 3 and its rules (storms always, 6 kills clear an area) are on the player");
            yield return Ask(s, StepSet, "kall=1");
            yield return Until(() => WorldState.KallDefeated && HeldCells.TestKnown, 8f);
            c.Check(s.Ok && WorldState.KallDefeated && HeldCells.TestKnown, "Kall defeated on the server: the player knows, the area lists came");
            hud.Begin();
            var box = new Box();
            travelled = true;
            yield return TeleportAndWait(player, point, MpTravel, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                var blizzard = Storms.Env();
                var retreat = Localize(HeldCells.ClearedText);
                yield return Until(() => TaggedOf(cell).Count > 0, clock.Cap(30f));
                yield return WaitTagged(pos, 400f, 1, clock.Cap(4f));
                yield return Until(() => Storms.Override() == blizzard && MapOverlay.Ready && MapOverlay.TestPixel(pos) == 2, 8f);
                c.Check(TaggedOf(cell).Count > 0 && blizzard != null && Storms.Override() == blizzard && EnvMan.instance.GetEnvironmentOverride() == blizzard
                        && MapOverlay.TestPixel(pos) == 2,
                    $"in the area: its Jotun ({Kinds(TaggedOf(cell))}), the blizzard (StormShare 100 on the server), purple on the map "
                    + $"(pixel state {MapOverlay.TestPixel(pos)})");

                // 3 of its Jotun killed by the player, for real.
                var need = 4 - ArmyNear(pos, 90f, cell).Count;
                if (need > 0)
                {
                    SpawnRing(Hostility.Krigen, pos, need, 16f, spawned);
                    // The server must know them before they die.
                    yield return new WaitForSeconds(2f);
                }
                var slain = SlayArmy(pos, cell, 3, null);
                yield return WaitServerCell(s, cell, 3, false, clock.Cap(10f));
                yield return Until(() => HeldCells.KillsIn(cell) == 3, 4f);
                s.Cell(cell, out var kills, out _, out var cleared, out _);
                var line = First(RunConsole(AdminCommand.Name));
                c.Check(slain == 3 && kills == 3 && !cleared && ReadOf(line, JotunDefeated, out var k, out var n) && k == 3 && n == 6,
                    $"3 Jotun killed by the player in the area: the server counts 3, deepnorth_stones there shows 3 of 6 ('{line}')");
                Cells.FromId(cell, out var ci, out var cj);
                var logLine = string.Format(CultureInfo.InvariantCulture,
                    "3 Jotun defeated in Deep North area {0},{1}, where 3 are enough now: the area is cleared for good.", ci, cj);
                yield return Ask(s, StepLog, logLine);
                var logged = s.Num("count");

                // The server's own settings go down to 3 while the player stays in the area. No further kill.
                HeldCells.TestResetNews();
                var centreBefore = hud.Count(retreat);
                var t0 = Time.realtimeSinceStartup;
                yield return Ask(s, StepSet, "ClearKillsMin=3;ClearKillsMax=3");
                c.Check(s.Ok, "ClearKillsMin and ClearKillsMax set to 3 on the server (" + (s.Ok ? "done" : s.Reply.ToString()) + ")");
                yield return Until(() => HeldCells.IsCleared(cell), 8f);
                var took = Time.realtimeSinceStartup - t0;
                HeldCells.TestLastNews(out var kind, out var newsCell, out var shown);
                c.Check(HeldCells.IsCleared(cell) && kind == HeldCells.NewsCleared && newsCell == cell && shown && hud.Count(retreat) == centreBefore + 1
                        && CenterText() == retreat,
                    $"{F(took)} s later, without another kill: \"{retreat}\" shows on the player's screen, once ({hud.Count(retreat) - centreBefore}; "
                    + $"centre text '{CenterText()}')");
                yield return Until(() => Storms.Override() == null && EnvMan.instance.GetEnvironmentOverride() != blizzard, 3f);
                c.Check(Storms.Override() == null && EnvMan.instance.GetEnvironmentOverride() != blizzard && !Storms.StormAt(pos, ServerRules.Current),
                    "the blizzard stops there (the game is no longer given it; StormShare still 100)");
                yield return Until(() => MapOverlay.TestPixel(pos) == 1, 3f);
                c.Check(MapOverlay.TestPixel(pos) == 1, $"the purple leaves the map there (pixel state {MapOverlay.TestPixel(pos)})");
                line = First(RunConsole(AdminCommand.Name));
                c.Check(line.Contains("is cleared") && line.IndexOf("3 of 3", System.StringComparison.Ordinal) < 0,
                    $"deepnorth_stones there says the area is cleared: '{line}'");
                yield return WaitServerCell(s, cell, 0, true, 6f);
                s.Cell(cell, out kills, out _, out cleared, out var marked);
                yield return Ask(s, StepLog, logLine);
                c.Check(cleared && marked && kills == 0 && s.Ok && s.Num("count") == logged + 1,
                    $"on the server: cleared {cleared}, kept in the world {marked}, and its log says \"{logLine}\" once more ({s.Num("count") - logged})");
            }
        }
        finally
        {
            hud.End();
            ClearOverrides();
            KillAll(spawned);
            SweepArea(player);
        }
        if (travelled)
        {
            yield return GoHomeMp(c, player, home);
        }
        if (changed)
        {
            yield return PutServerBack(c, s, stones0, cell);
        }
        c.Report();
    }
}
#endif
