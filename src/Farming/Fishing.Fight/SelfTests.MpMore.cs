#if DEBUG
using System.Collections;
using System.Globalization;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.FishingFightMod;

// Debug build only. One more multiplayer self test (tools/Test-Multiplayer.ps1, same class as SelfTests.cs; helpers
// of SelfTests.Mp.cs). The server stands in for "the other game": the place where it holds an object is where every
// other player's game draws it.
//   fishing.mp.watch-run  (modded) M03: a fight in REAL water (shore trip, like the single-player live tests): while
//                         the fish runs to the fisher's right and drags the float, the server's copies of the fish
//                         and of the float move to that side too
// What a second player's screen shows (the look of the run, the splashes) and a watcher without the mod stay by hand.
internal static partial class SelfTests
{
    private const string MpWatchRunName = "fishing.mp.watch-run";
    private const string StepWhere = "fishing.mp.server-where";

    private static void RegisterMultiplayerMore()
    {
        SelfTest.RegisterMultiplayer(MpWatchRunName, SelfTest.Modded, RunMpWatchRun);
        SelfTest.RegisterServerStep(StepWhere, ServerWhereStep);
    }

    private static void UnregisterMultiplayerMore()
    {
        SelfTest.UnregisterMultiplayer(MpWatchRunName);
        SelfTest.UnregisterServerStep(StepWhere);
    }

    private static string Where(Vector3 v) =>
        v.x.ToString("R", CultureInfo.InvariantCulture) + ";" + v.y.ToString("R", CultureInfo.InvariantCulture) + ";"
        + v.z.ToString("R", CultureInfo.InvariantCulture);

    // "x;y;z|x;y;z" (fish, float) -> into[0], into[1].
    private static bool ReadWhere(string text, Vector3[] into)
    {
        var parts = (text ?? "").Split('|');
        if (parts.Length != 2 || into == null || into.Length < 2)
        {
            return false;
        }
        for (var i = 0; i < 2; i++)
        {
            var xyz = parts[i].Split(';');
            if (xyz.Length != 3
                || !float.TryParse(xyz[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !float.TryParse(xyz[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !float.TryParse(xyz[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                return false;
            }
            into[i] = new Vector3(x, y, z);
        }
        return true;
    }

    // ---------- fishing.mp.watch-run (M03) ----------

    private static IEnumerator RunMpWatchRun() => RunLive(MpWatchRunName, true, MpWatchRunBody);

    private static IEnumerator MpWatchRunBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        if (!c.Check(IsClient, "connected to a server as a client"))
        {
            yield break;
        }
        // Calm first (fish off the zone for free), then a fight to the right, not reeled.
        ServerRules.TestRules = new FightRules { OffBarStamina = 0f };
        FightHud.TestDisplay = DefaultDisplay;
        Fight.TestPin = FishPin.Away;
        Fight.TestSide = FightLogic.Right;
        Fight.TestPhase = FightPhase.Calm;
        var hook = new Hooked();
        yield return HookNew(rig, st, 1, hook);
        var fight = hook.Fight;
        var ff = hook.Float;
        var fish = hook.Fish;
        if (!c.Check(fight != null, "fight starts on the client (a Perch hooked 10 m out in real water)"))
        {
            yield break;
        }
        var ids = IdText(fish.GetZDOID()) + "|" + IdText(ff.m_nview.GetZDO().m_uid);
        var asked = Time.time;
        while (ff != null && fish != null && ReferenceEquals(Fight.Current, fight) && Time.time - asked < 1f)
        {
            rig.Fill(p.GetMaxStamina());
            yield return new WaitForFixedUpdate();
        }
        var before = new Vector3[2];
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(StepWhere, ids, reply);
        if (!c.Check(reply.Answered && reply.Ok && ReadWhere(reply.Detail, before),
                "server: it holds the client's fish and float (calm phase): " + reply))
        {
            yield break;
        }
        if (ff == null || fish == null || !ReferenceEquals(Fight.Current, fight))
        {
            c.Check(false, "the fight ended before the fish fought");
            yield break;
        }

        // The fight: the fish runs to the fisher's right and drags the float.
        var right = FightLogic.RightOf(Flat(ff.transform.position - p.transform.position));
        var fish0 = fish.transform.position;
        var float0 = ff.transform.position;
        Fight.TestPhase = FightPhase.Struggle;
        rig.Reel(false);
        asked = Time.time;
        while (ff != null && fish != null && ReferenceEquals(Fight.Current, fight) && Time.time - asked < 2.5f)
        {
            rig.Fill(p.GetMaxStamina());
            yield return new WaitForFixedUpdate();
        }
        if (ff == null || fish == null || !ReferenceEquals(Fight.Current, fight))
        {
            c.Check(false, "the fight ended during the 2.5 s watched (line broke or fish lost)");
            yield break;
        }
        var fishRun = Vector3.Dot(fish.transform.position - fish0, right);
        var floatRun = Vector3.Dot(ff.transform.position - float0, right);
        var zdo = fish.m_nview.GetZDO();
        c.Check(fight.Phase == FightPhase.Struggle && fishRun > 1.5f && floatRun > 0.3f && zdo.GetFloat(ZDOVars.s_escape) > 0f
                && !fish.IsOutOfWater(),
            $"client: in a fight to the right the fish runs {F(fishRun)} m to the fisher's right in 2.5 s and drags the float {F(floatRun)} m that way");
        var after = new Vector3[2];
        reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(StepWhere, ids, reply);
        if (c.Check(reply.Answered && reply.Ok && ReadWhere(reply.Detail, after), "server: it still holds both during the fight: " + reply))
        {
            var fishSeen = Vector3.Dot(after[0] - before[0], right);
            var floatSeen = Vector3.Dot(after[1] - before[1], right);
            c.Check(fishSeen > 1f && fishSeen > fishRun * 0.5f && floatSeen > 0.2f,
                $"server (where every other player's game draws them): its copy of the fish moved {F(fishSeen)} m to that side and its copy of the float {F(floatSeen)} m (on the client {F(fishRun)} m and {F(floatRun)} m)");
        }
        Fight.TestPhase = FightPhase.Calm;
        yield return new WaitForFixedUpdate();
    }

    // Server half: where the server holds the fish and the float. arg "<fish id>|<float id>"; waits up to 5 s for
    // both. Answer "x;y;z|x;y;z".
    private static IEnumerator ServerWhereStep(string arg, object[] reply)
    {
        var parts = (arg ?? "").Split('|');
        var fishId = ZDOID.None;
        var floatId = ZDOID.None;
        if (parts.Length != 2 || !TryId(parts[0], out fishId) || !TryId(parts[1], out floatId) || ZDOMan.instance == null)
        {
            SelfTest.Answer(reply, false, "bad argument '" + arg + "'");
            yield break;
        }
        var start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < 5f)
        {
            var fish = ZDOMan.instance.GetZDO(fishId);
            var ff = ZDOMan.instance.GetZDO(floatId);
            if (fish != null && ff != null)
            {
                SelfTest.Answer(reply, true, Where(fish.GetPosition()) + "|" + Where(ff.GetPosition()));
                yield break;
            }
            yield return null;
        }
        SelfTest.Answer(reply, false, "the server does not hold both the fish and the float");
    }
}
#endif
