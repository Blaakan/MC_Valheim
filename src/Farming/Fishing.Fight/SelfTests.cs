using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using MC.Shared;
using UnityEngine;
#endif

namespace MC.Farming.FishingFightMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1,
// -Mod Fishing.Fight). Design 7.5:
//   fishing.network  rules on the wire (round trip, wild values clamped, unknown layout and cut-off refused), which
//                    rules apply (own / server / pending), push debounce, join check verdicts (pure)
//   fishing.logic    catch bar maths (hold climb, release fall and bounce, zone stay on the track, every fish motion
//                    stay on the track, harder fish move more), rod verdict (sides, angle limit, both ways), run
//                    direction, durations, costs, fish profiles, other-mod classification (pure)
//   fishing.pending  rules pending (client waiting for the server) = no take-over (vanilla reel)
//   fishing.fight    live, on a shore found with WorldGenerator (probe spawn is inland): rod in hand, float and Fish1
//                    spawned and hooked; calm with the fish in the bar = line in, no stamina used, regen on; fish out
//                    of the bar = line hold, stamina drain; struggle = bar gone, splash key on, fish swim to its side,
//                    no reel = fish take line, wrong side = x4 stamina and no line, right side = line in, side switch
//                    flip the verdict; calm again = bar back; reel to the end = fish in the bag, float gone; second
//                    fish at 0 stamina = lost, fish let go, float stay; screenshots of the bar and the arrow; travel
//                    back
// Me force rules only with ServerRules.TestRules / TestPending, phase and marker with Fight.TestPhase / TestPin, the
// arrow with FightHud.TestArrow: never config. Rig put back controls, look, items, stamina, world stamina rate, time
// and the player's place.
internal static class SelfTests
{
    private const string NetworkName = "fishing.network";
    private const string LogicName = "fishing.logic";
    private const string PendingName = "fishing.pending";
    private const string FightName = "fishing.fight";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(NetworkName, RunNetwork);
        SelfTest.Register(LogicName, RunLogic);
        SelfTest.Register(PendingName, RunPending);
        SelfTest.Register(FightName, RunFight);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(NetworkName);
        SelfTest.Unregister(LogicName);
        SelfTest.Unregister(PendingName);
        SelfTest.Unregister(FightName);
        ClearOverrides();
#endif
    }

#if DEBUG
    // Each trip (vanilla distant teleport: 8 s minimum, then area ready and floor found).
    private const float TravelTimeout = 35f;

    private static void ClearOverrides()
    {
        ServerRules.TestPending = false;
        ServerRules.TestRules = null;
        Fight.ClearTest();
        FightHud.TestArrow = null;
    }

    // ---------- helpers ----------

    private sealed class Checks
    {
        private readonly string _name;
        private readonly List<string> _failures = new List<string>();
        private int _count;

        internal Checks(string name) => _name = name;

        internal bool Check(bool ok, string what)
        {
            _count++;
            if (!ok)
            {
                _failures.Add(what);
            }
            return ok;
        }

        internal void Note(string detail) => SelfTest.Note(_name, detail);

        internal void Report(string extra = "")
        {
            if (_failures.Count == 0)
            {
                SelfTest.Pass(_name, $"{_count} checks OK{extra}");
            }
            else
            {
                SelfTest.Fail(_name, $"{_failures.Count} of {_count} checks failed: {string.Join("; ", _failures.ToArray())}");
            }
        }
    }

    private sealed class Box
    {
        internal bool Ok;
        internal string Detail = "";
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string F(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";

    private static bool Near(float a, float b, float tolerance = 0.001f) => Mathf.Abs(a - b) <= tolerance;

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // ---------- fishing.network ----------

    private static IEnumerator RunNetwork()
    {
        var c = new Checks(NetworkName);
        var custom = new FightRules
        {
            BarSize = 0.3f, BarSizeAtMaxSkill = 0.5f, FishDifficulty = 1.5f, OffBarStamina = 2f, StruggleStamina = 0.5f,
            WrongSideStamina = 6f, RodAngle = 45f, CalmSeconds = 10f, StruggleSeconds = 4f, LineRunSpeed = 2f,
            ReelSpeed = 1.5f,
        };
        var pkg = new ZPackage();
        custom.Write(pkg);
        pkg.SetPos(0);
        c.Check(FightRules.TryRead(pkg, out var back, out var clamped) && !clamped
                && back.Describe() == custom.Describe() && Near(back.RodAngle, 45f) && !back.IsPending,
            "rules survive the wire unchanged");

        var wild = new FightRules
        {
            BarSize = float.NaN, BarSizeAtMaxSkill = 5f, FishDifficulty = -1f, OffBarStamina = 99f,
            StruggleStamina = float.PositiveInfinity, WrongSideStamina = 0f, RodAngle = 180f, CalmSeconds = 0f,
            StruggleSeconds = 100f, LineRunSpeed = -2f, ReelSpeed = 50f,
        };
        pkg = new ZPackage();
        wild.Write(pkg);
        pkg.SetPos(0);
        var d = FightRules.Default;
        c.Check(FightRules.TryRead(pkg, out back, out clamped) && clamped
                && Near(back.BarSize, d.BarSize) && Near(back.BarSizeAtMaxSkill, FightRules.BarSizeMax)
                && Near(back.FishDifficulty, FightRules.DifficultyMin)
                && Near(back.OffBarStamina, FightRules.StaminaMultiplierMax) && Near(back.StruggleStamina, d.StruggleStamina)
                && Near(back.WrongSideStamina, FightRules.WrongSideMin) && Near(back.RodAngle, FightRules.RodAngleMax)
                && Near(back.CalmSeconds, FightRules.CalmSecondsMin)
                && Near(back.StruggleSeconds, FightRules.StruggleSecondsMax) && Near(back.LineRunSpeed, 0f)
                && Near(back.ReelSpeed, FightRules.ReelSpeedMax),
            "out-of-range, NaN and infinite values from the wire are pulled into range");

        pkg = new ZPackage();
        pkg.Write(FightRules.Layout + 1);
        pkg.Write(1f);
        pkg.SetPos(0);
        c.Check(!FightRules.TryRead(pkg, out _, out _), "unknown rules layout refused");
        pkg = new ZPackage();
        pkg.Write(FightRules.Layout);
        pkg.Write(1f);
        pkg.SetPos(0);
        c.Check(!FightRules.TryRead(pkg, out _, out _), "cut-off rules package refused");
        c.Check(FightRules.Pending.IsPending && !FightRules.Default.IsPending && !FightRules.Own().IsPending,
            "only the built-in Pending rules are pending");
        c.Check(Near(d.BarSize, 0.24f) && Near(d.BarSizeAtMaxSkill, 0.38f) && Near(d.FishDifficulty, 1f)
                && Near(d.OffBarStamina, 1f) && Near(d.StruggleStamina, 1f) && Near(d.WrongSideStamina, 4f)
                && Near(d.RodAngle, 30f) && Near(d.CalmSeconds, 7f) && Near(d.StruggleSeconds, 3f)
                && Near(d.LineRunSpeed, 1.5f) && Near(d.ReelSpeed, 1f),
            "defaults: bar 24-38 %, difficulty x1, stamina x1 / x1 / wrong x4, 30 deg, calm 7 s, fight 3 s, 1.5 m/s, reel x1");

        // A server (this single-player world is one) never takes rules from a peer.
        var current = ServerRules.Current;
        pkg = new ZPackage();
        custom.Write(pkg);
        pkg.SetPos(0);
        c.Check(!ServerRules.Receive(pkg) && ReferenceEquals(ServerRules.Current, current) && !ServerRules.UsingServer
                && !ServerRules.IsPending,
            "single player / server took rules from a peer or is pending");

        var own = new FightRules();
        c.Check(ReferenceEquals(ServerRules.Select(false, custom, own), own), "single player, host, server: own rules");
        c.Check(ReferenceEquals(ServerRules.Select(false, null, own), own), "server without peer rules: own rules");
        c.Check(ReferenceEquals(ServerRules.Select(true, custom, own), custom), "client with server rules: the server's");
        c.Check(ServerRules.Select(true, null, own).IsPending, "client without (readable) server rules: pending");
        c.Check(!ServerRules.Settled(10f, 9.8f) && ServerRules.Settled(10f, 9.5f)
                && ServerRules.Settled(0f, float.NegativeInfinity),
            "push waits 0.5 s after the last settings change (at once when turned on)");

        c.Check(PlayerCheck.Decide(true, true, true, false, true, false) == JoinVerdict.Compatible, "compatible -> Compatible");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, false) == JoinVerdict.Refuse,
            "not compatible (no mod, turned off, other network version) -> Refuse");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, true) == JoinVerdict.Allowed,
            "not compatible with AllowPlayersWithoutMod -> Allowed");
        c.Check(PlayerCheck.Decide(false, true, true, false, false, false) == JoinVerdict.Skip, "not a server -> Skip");
        c.Check(PlayerCheck.Decide(true, false, true, false, false, false) == JoinVerdict.Skip, "not connected -> Skip");
        c.Check(PlayerCheck.Decide(true, true, false, false, false, false) == JoinVerdict.Skip, "not ready -> Skip");
        c.Check(PlayerCheck.Decide(true, true, true, true, false, false) == JoinVerdict.Skip, "already being kicked -> Skip");
        c.Report();
        yield break;
    }

    // ---------- fishing.logic ----------

    private static IEnumerator RunLogic()
    {
        var c = new Checks(LogicName);
        const float dt = 0.02f;
        var rng = new System.Random(1234);

        // Zone: hold = climb to the top and stay; let go = fall to the bottom (bounce settle).
        var easy = FishProfile.For("Fish1", 1, Heightmap.Biome.Meadows, 1f);
        var bar = new CatchBar(easy, 0.25f, rng);
        c.Check(Near(bar.ZonePos, 0f) && bar.FishPos > 0f && bar.FishPos < 0.3f,
            $"start: zone at the bottom, easy fish low (fish {F(bar.FishPos)})");
        var z0 = bar.ZonePos;
        for (var i = 0; i < 10; i++)
        {
            bar.Step(dt, true);
        }
        c.Check(bar.ZonePos > z0 && bar.ZoneSpeed > 0f, $"hold: zone climbs (pos {F(bar.ZonePos)}, speed {F(bar.ZoneSpeed)})");
        for (var i = 0; i < 200; i++)
        {
            bar.Step(dt, true);
        }
        c.Check(Near(bar.ZonePos, 1f - bar.ZoneSize, 0.0001f), $"hold long: zone stops at the top ({F(bar.ZonePos)})");
        for (var i = 0; i < 10; i++)
        {
            bar.Step(dt, false);
        }
        c.Check(bar.ZoneSpeed < 0f && bar.ZonePos < 1f - bar.ZoneSize, "release: zone falls");
        var bounced = false;
        for (var i = 0; i < 300; i++)
        {
            bar.Step(dt, false);
            bounced |= bar.ZonePos <= 0f || (bar.ZonePos < 0.05f && bar.ZoneSpeed > 0f);
        }
        c.Check(bounced && Near(bar.ZonePos, 0f, 0.0001f) && Near(bar.ZoneSpeed, 0f),
            $"release long: zone back on the bottom and settled ({F(bar.ZonePos)}, {F(bar.ZoneSpeed)})");

        // Fish marker: every motion stays on the track; in-zone test matches the positions.
        var motionsOk = true;
        var inZoneOk = true;
        foreach (FishMotion motion in Enum.GetValues(typeof(FishMotion)))
        {
            var b = new CatchBar(new FishProfile(90f, motion), 0.3f, new System.Random(7 + (int)motion));
            for (var i = 0; i < 20000; i++)
            {
                b.Step(dt, i % 120 < 60);
                motionsOk &= b.FishPos >= 0f && b.FishPos <= 1f && b.ZonePos >= 0f && b.ZonePos <= 1f - b.ZoneSize + 1e-5f;
                inZoneOk &= b.InZone == (b.FishPos >= b.ZonePos && b.FishPos <= b.ZonePos + b.ZoneSize);
            }
        }
        c.Check(motionsOk, "every fish motion and the zone stay on the track (20000 steps each, wild fish)");
        c.Check(inZoneOk, "InZone = fish centre between zone bottom and top");

        // Harder fish travel more (sum of moves over the same time, several seeds).
        var calm = Travel(15f, 11);
        var wildTravel = Travel(85f, 11);
        c.Check(wildTravel > calm * 1.5f, $"hard fish move more than easy ones (85: {F(wildTravel)}, 15: {F(calm)})");
        c.Note($"marker travel over 60 s: difficulty 15 = {F(calm)} tracks, 85 = {F(wildTravel)} tracks");

        // Sinker drift down, floater drift up (no targets: difficulty 0 never pick one).
        var sink = new CatchBar(new FishProfile(0f, FishMotion.Sinker), 0.3f, new System.Random(3));
        var floater = new CatchBar(new FishProfile(0f, FishMotion.Floater), 0.3f, new System.Random(3));
        sink.FishPos = 0.5f;
        floater.FishPos = 0.5f;
        for (var i = 0; i < 100; i++)
        {
            sink.Step(dt, false);
            floater.Step(dt, false);
        }
        c.Check(sink.FishPos < 0.5f && floater.FishPos > 0.5f,
            $"sinker drifts down ({F(sink.FishPos)}), floater drifts up ({F(floater.FishPos)})");

        // Rod verdict. Line straight north. Fish run right: rod must point left (negative yaw) >= 30 deg.
        var north = Vector3.forward;
        c.Check(FightLogic.SignedYaw(north, Vector3.right) > 89f && FightLogic.SignedYaw(north, Vector3.left) < -89f,
            "yaw sign: right of the line positive, left negative");
        c.Check(FightLogic.Judge(north, Yaw(-45f), FightLogic.Right, 30f) == RodVerdict.Good, "fish right, rod 45 left: good");
        c.Check(FightLogic.Judge(north, Yaw(-31f), FightLogic.Right, 30f) == RodVerdict.Good, "fish right, rod 31 left: good");
        c.Check(FightLogic.Judge(north, Yaw(-29f), FightLogic.Right, 30f) == RodVerdict.Wrong, "fish right, rod 29 left: wrong");
        c.Check(FightLogic.Judge(north, north, FightLogic.Right, 30f) == RodVerdict.Wrong, "fish right, rod on the line: wrong");
        c.Check(FightLogic.Judge(north, Yaw(45f), FightLogic.Right, 30f) == RodVerdict.Wrong, "fish right, rod 45 right: wrong");
        c.Check(FightLogic.Judge(north, Yaw(120f), FightLogic.Right, 30f) == RodVerdict.Wrong, "fish right, rod 120 right: wrong");
        c.Check(FightLogic.Judge(north, Yaw(-120f), FightLogic.Right, 30f) == RodVerdict.Good, "fish right, rod 120 left: good");
        c.Check(FightLogic.Judge(north, Yaw(45f), FightLogic.Left, 30f) == RodVerdict.Good, "fish left, rod 45 right: good");
        c.Check(FightLogic.Judge(north, Yaw(-45f), FightLogic.Left, 30f) == RodVerdict.Wrong, "fish left, rod 45 left: wrong");
        c.Check(FightLogic.Judge(north, Yaw(40f), FightLogic.Left, 45f) == RodVerdict.Wrong, "angle rule: 40 < 45 is wrong");
        var east = Vector3.right;
        c.Check(FightLogic.Judge(east, Quaternion.Euler(0f, 90f - 45f, 0f) * Vector3.forward, FightLogic.Right, 30f)
                == RodVerdict.Good, "works for any line direction (line east, rod north-east, fish right)");
        c.Check(FightLogic.Judge(Vector3.zero, north, FightLogic.Right, 30f) == RodVerdict.Good,
            "float at the feet: nothing to judge");
        c.Check(FightLogic.Judge(north + Vector3.up * 5f, Yaw(-45f) + Vector3.down * 3f, FightLogic.Right, 30f)
                == RodVerdict.Good, "height ignored (flat yaw)");

        // Run direction: across the line to the fish's side, away too while it takes line.
        var runRight = FightLogic.RunDirection(north, FightLogic.Right, false);
        var runLeftAway = FightLogic.RunDirection(north, FightLogic.Left, true);
        c.Check(Near(runRight.x, 1f, 0.001f) && Near(runRight.z, 0f, 0.001f), $"run right = east for a north line {F(runRight)}");
        c.Check(runLeftAway.x < -0.8f && runLeftAway.z > 0.3f && Near(runLeftAway.magnitude, 1f, 0.001f),
            $"run left while taking line = west and a bit away {F(runLeftAway)}");

        // Durations, rates, sizes, costs.
        var rules = FightRules.Default;
        var rr = new System.Random(5);
        var calmOk = true;
        var fightOk = true;
        for (var i = 0; i < 1000; i++)
        {
            var cd = FightLogic.CalmDuration(rules, 0.5f, rr);
            calmOk &= cd >= rules.CalmSeconds * 0.6f * 0.8f - 0.001f && cd <= rules.CalmSeconds * 1.4f * 1.2f + 0.001f;
            var sd = FightLogic.StruggleDuration(rules, 0.5f, 3, rr);
            fightOk &= sd >= rules.StruggleSeconds * 0.67f * 0.8f + 1f - 0.001f
                       && sd <= rules.StruggleSeconds * 1.33f * 1.2f + 1f + 0.001f;
        }
        c.Check(calmOk, "calm time stays within the rule's +-40 % and the difficulty factor");
        c.Check(fightOk, "fight time stays within +-33 %, the difficulty factor and +0.5 s per star");
        c.Check(FightLogic.SwitchRate(1f) > FightLogic.SwitchRate(0f), "hard fish switch sides more often");
        c.Check(Near(FightLogic.ZoneSize(rules, 0f), 0.24f) && Near(FightLogic.ZoneSize(rules, 1f), 0.38f)
                && Near(FightLogic.ZoneSize(rules, 0.5f), 0.31f), "bar size follows the skill");
        c.Check(Near(FightLogic.ReelCost(10f, 1f, 1, 0.2f, 0f), 11f) && Near(FightLogic.ReelCost(10f, 2f, 3, 0.2f, 1f), 3.2f)
                && Near(FightLogic.ReelCost(10f, 1f, 0, 0.2f, 0f), 11f),
            "reel cost = vanilla formula (10 + fish x quality, x0.2 at skill 100)");
        c.Check(Near(FightLogic.ReelSpeed(1f, 2f, 0.5f, 1f), 1.5f) && Near(FightLogic.ReelSpeed(1f, 2f, 1f, 2f), 4f),
            "reel speed = vanilla lerp x rule");
        c.Check(FightLogic.LineRunSpeed(rules, 1f) > FightLogic.LineRunSpeed(rules, 0f)
                && Near(FightLogic.LineRunSpeed(rules, 0.5f), rules.LineRunSpeed), "hard fish take line faster");

        // Fish profiles.
        var perch = FishProfile.For("Fish1", 1, Heightmap.Biome.Meadows, 1f);
        var perch3 = FishProfile.For("Fish1", 3, Heightmap.Biome.Meadows, 1f);
        var salmon = FishProfile.For("Fish10", 1, Heightmap.Biome.DeepNorth, 1f);
        var modFish = FishProfile.For("SomeModFish", 1, Heightmap.Biome.Plains, 1f);
        var maxed = FishProfile.For("Fish10", 5, Heightmap.Biome.DeepNorth, 2f);
        c.Check(Near(perch.Difficulty, 15f) && perch.Motion == FishMotion.Mixed, $"perch = 15 mixed ({F(perch.Difficulty)})");
        c.Check(Near(perch3.Difficulty, 15f + 2f * FishProfile.StarStep), "two stars add two steps");
        c.Check(salmon.Difficulty > perch.Difficulty, "later biome fish are harder");
        c.Check(Near(modFish.Difficulty, FishProfile.BiomeDifficulty(Heightmap.Biome.Plains)), "unknown fish: biome difficulty");
        c.Check(Near(maxed.Difficulty, FishProfile.MaxDifficulty), "difficulty capped at 100");
        c.Check(Near(FishProfile.For("Fish1", 1, Heightmap.Biome.Meadows, 0.25f).Difficulty, FishProfile.MinDifficulty),
            "difficulty floor at 5");
        var all = new[] { "Fish1", "Fish2", "Fish3", "Fish4_cave", "Fish5", "Fish6", "Fish7", "Fish8", "Fish9", "Fish10",
            "Fish11", "Fish12" };
        var known = true;
        var missing = new List<string>();
        foreach (var name in all)
        {
            known &= FishProfile.IsKnown(name);
            if (ZNetScene.instance != null && ZNetScene.instance.GetPrefab(name) == null)
            {
                missing.Add(name);
            }
        }
        c.Check(known, "all 12 vanilla fish have a profile");
        c.Check(missing.Count == 0, "every profile name is a game prefab (missing: " + string.Join(", ", missing.ToArray()) + ")");

        // Other mods.
        c.Check(ForeignMods.Classify("sighsorry.TrollingFishing", "TrollingFishing") == ForeignKind.Blocks, "Trolling Fishing blocks");
        c.Check(ForeignMods.Classify("com.GrindstoneSkills", "GrindstoneSkills") == ForeignKind.BlocksIfFishing,
            "GrindstoneSkills blocks only with its fishing on");
        c.Check(ForeignMods.Classify("Azumatt.Hooked", "Hooked") == ForeignKind.BlocksIfFishing, "Hooked blocks only with its minigame on");
        c.Check(ForeignMods.Classify("someone.peasfishing", "PeasFishing") == ForeignKind.Blocks, "PeasFishing blocks by name");
        c.Check(ForeignMods.Classify("Andejx.ChillHook", "ChillHook") == ForeignKind.Blocks, "ChillHook blocks by name");
        c.Check(ForeignMods.Classify("x.y", "ChillFishing") == ForeignKind.Blocks, "ChillFishing blocks by name");
        c.Check(ForeignMods.Classify("randyknapp.mods.epicloot", "Epic Loot") == ForeignKind.Composes, "EpicLoot composes");
        c.Check(ForeignMods.Classify("com.FeastMaster", "FeastMaster") == ForeignKind.Composes, "FeastMaster composes");
        c.Check(ForeignMods.Classify("games.loxley.comfyfishing", "ComfyFishing") == ForeignKind.Composes, "ComfyFishing composes");
        c.Check(ForeignMods.Classify("MC.Exploration.Swimming.Dive", "Swim Dive") == ForeignKind.None, "unrelated mod: none");
        c.Report();
        yield break;
    }

    private static Vector3 Yaw(float degrees) => Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;

    // Total marker travel (tracks) over 60 s, average of 4 seeds.
    private static float Travel(float difficulty, int seed)
    {
        var sum = 0f;
        for (var s = 0; s < 4; s++)
        {
            var b = new CatchBar(new FishProfile(difficulty, FishMotion.Mixed), 0.3f, new System.Random(seed + s));
            var last = b.FishPos;
            for (var i = 0; i < 3000; i++)
            {
                b.Step(0.02f, false);
                sum += Mathf.Abs(b.FishPos - last);
                last = b.FishPos;
            }
        }
        return sum / 4f;
    }

    // ---------- fishing.pending ----------

    private static IEnumerator RunPending()
    {
        var c = new Checks(PendingName);
        try
        {
            c.Check(!ServerRules.IsPending, "single player is never pending");
            ServerRules.TestRules = new FightRules { RodAngle = 50f };
            ServerRules.TestPending = true;
            var rules = ServerRules.Current;
            c.Check(rules.IsPending && ReferenceEquals(rules, FightRules.Pending), "pending wins over test rules");
            c.Check(rules.Describe().IndexOf("waiting", StringComparison.Ordinal) >= 0, "pending rules say they wait");
            ServerRules.TestPending = false;
            rules = ServerRules.Current;
            c.Check(!rules.IsPending && Near(rules.RodAngle, 50f), "pending over: rules in force again");
            c.Note("pending with a hooked fish (vanilla reel, no bar) is checked live by fishing.fight");
        }
        finally
        {
            ServerRules.TestPending = false;
            ServerRules.TestRules = null;
        }
        c.Report();
        yield break;
    }

    // ---------- fishing.fight (live) ----------

    // Me = what the live test change on player and world. Restore (finally, no yield) put everything back.
    private sealed class FishRig
    {
        internal readonly Player P;
        internal readonly Inventory Inv;
        internal readonly Vector3 Origin;
        internal readonly Quaternion OriginRotation;
        private readonly Quaternion _yaw;
        private readonly ItemDrop.ItemData _right;
        private readonly ItemDrop.ItemData _left;
        private readonly List<ItemDrop.ItemData> _items = new List<ItemDrop.ItemData>();
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
        private PlayerController _controller;
        private bool _controllerEnabled;
        private bool _staminaTaken;
        private bool _hadStamina;
        private float _stamina;
        private bool _envSaved;
        private bool _todOn;
        private float _tod;
        internal bool Travelled;
        internal bool Back;

        internal FishRig(Player player)
        {
            P = player;
            Inv = player.GetInventory();
            Origin = player.transform.position;
            OriginRotation = player.transform.rotation;
            _yaw = player.m_lookYaw;
            _right = player.m_rightItem;
            _left = player.m_leftItem;
        }

        // Count of an item before the test (caught fish taken out again at the end).
        internal void Remember(string itemName) => _counts[itemName] = Inv.CountItems(itemName);

        internal ItemDrop.ItemData Give(string prefab)
        {
            var item = Inv.AddItem(prefab, 1, 1, 0, 0L, "", false);
            if (item != null)
            {
                _items.Add(item);
            }
            return item;
        }

        internal void Track(GameObject go)
        {
            if (go != null)
            {
                _spawned.Add(go);
            }
        }

        internal void TakeControls()
        {
            if (_controller == null)
            {
                _controller = P.GetComponent<PlayerController>();
                if (_controller != null)
                {
                    _controllerEnabled = _controller.enabled;
                    _controller.enabled = false;
                }
            }
            Reel(false);
        }

        // Block held (vanilla reel input) or not; no other control. m_blocking set too: with the game's ToggleBlock
        // option SetControls flip it on every press instead.
        internal void Reel(bool hold)
        {
            P.SetControls(Vector3.zero, false, false, false, false, hold, hold, false, false, false, false);
            P.m_blocking = hold;
        }

        internal void TakeStaminaRate()
        {
            if (_staminaTaken)
            {
                return;
            }
            var zone = ZoneSystem.instance;
            _hadStamina = zone.GetGlobalKey(GlobalKeys.StaminaRate, out _stamina);
            _staminaTaken = true;
            zone.RemoveGlobalKey(GlobalKeys.StaminaRate);
        }

        internal void Noon()
        {
            var env = EnvMan.instance;
            if (!_envSaved)
            {
                _envSaved = true;
                _todOn = env.m_debugTimeOfDay;
                _tod = env.m_debugTime;
            }
            env.m_debugTimeOfDay = true;
            env.m_debugTime = 0.5f;
        }

        // Face a flat direction now (body, look yaw, look dir): while blocking the body follow the look yaw.
        internal void Face(Vector3 dir)
        {
            dir = Flat(dir);
            if (dir.sqrMagnitude < 1e-6f)
            {
                return;
            }
            var rot = Quaternion.LookRotation(dir.normalized);
            P.m_lookYaw = rot;
            P.transform.rotation = rot;
            if (P.m_body != null)
            {
                P.m_body.rotation = rot;
            }
            P.SetLookDir(dir.normalized);
        }

        internal void Fill(float stamina)
        {
            P.m_stamina = Mathf.Min(stamina, P.GetMaxStamina());
            P.m_staminaRegenTimer = 5f;
        }

        internal IEnumerator Travel(Vector3 target, Quaternion rotation, Box result, float timeout)
        {
            var start = Time.time;
            while (!P.TeleportTo(target, rotation, true))
            {
                if (Time.time - start > 6f)
                {
                    result.Detail = "Player.TeleportTo refused for 6 s";
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
            }
            yield return null;
            while (P != null && P.IsTeleporting())
            {
                if (Time.time - start > timeout)
                {
                    result.Detail = $"still teleporting after {F(timeout)} s";
                    yield break;
                }
                yield return new WaitForSeconds(0.25f);
            }
            result.Ok = true;
            result.Detail = $"{F(Time.time - start)} s";
        }

        internal void Restore()
        {
            ClearOverrides();
            Try("fight", Fight.Shutdown);
            Try("controls", () =>
            {
                if (_controller != null)
                {
                    Reel(false);
                    _controller.enabled = _controllerEnabled;
                }
            });
            Try("spawned", DestroySpawned);
            Try("items", () =>
            {
                foreach (var item in _items)
                {
                    if (item == null || P == null)
                    {
                        continue;
                    }
                    if (P.IsItemEquiped(item))
                    {
                        P.UnequipItem(item, false);
                    }
                    if (Inv.ContainsItem(item))
                    {
                        Inv.RemoveItem(item);
                    }
                }
                _items.Clear();
                foreach (var pair in _counts)
                {
                    var extra = Inv.CountItems(pair.Key) - pair.Value;
                    if (extra > 0)
                    {
                        Inv.RemoveItem(pair.Key, extra);
                    }
                }
                if (_right != null && Inv.ContainsItem(_right) && !P.IsItemEquiped(_right))
                {
                    P.EquipItem(_right, false);
                }
                if (_left != null && Inv.ContainsItem(_left) && !P.IsItemEquiped(_left))
                {
                    P.EquipItem(_left, false);
                }
            });
            Try("stamina", () =>
            {
                if (_staminaTaken)
                {
                    _staminaTaken = false;
                    if (_hadStamina && ZoneSystem.instance != null)
                    {
                        ZoneSystem.instance.SetGlobalKey(GlobalKeys.StaminaRate, _stamina);
                    }
                }
                P.m_stamina = P.GetMaxStamina();
                P.m_staminaRegenTimer = 0f;
            });
            Try("look", () =>
            {
                P.m_lookYaw = _yaw;
            });
            Try("time", () =>
            {
                if (_envSaved && EnvMan.instance != null)
                {
                    EnvMan.instance.m_debugTimeOfDay = _todOn;
                    EnvMan.instance.m_debugTime = _tod;
                }
            });
            Try("travel back", () =>
            {
                if (!Travelled || Back || P == null)
                {
                    return;
                }
                if (P.IsTeleporting())
                {
                    P.m_teleportTargetPos = Origin;
                    P.m_teleportTargetRot = OriginRotation;
                }
                else
                {
                    P.TeleportTo(Origin, OriginRotation, true);
                }
            });
        }

        // Float and fish gone with their ZDOs. Called at the shore before the trip back (later the zone is unloaded
        // and the objects already gone, ZDOs left behind), and again by Restore as the safety net.
        internal void DestroySpawned()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    var nview = go.GetComponent<ZNetView>();
                    if (nview != null && nview.IsValid())
                    {
                        ZNetScene.instance.Destroy(go);
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(go);
                    }
                }
            }
            _spawned.Clear();
        }

        private static void Try(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                SelfTest.Note(FightName, $"restore {what} failed: {e.Message}");
            }
        }
    }

    private static IEnumerator RunFight()
    {
        var c = new Checks(FightName);
        var player = Player.m_localPlayer;
        if (player == null || ZoneSystem.instance == null || WorldGenerator.instance == null || ZNetScene.instance == null
            || ObjectDB.instance == null || EnvMan.instance == null)
        {
            SelfTest.Fail(FightName, "no local player or world");
            yield break;
        }
        var shores = FindShores(player.transform.position, MaxShores);
        if (shores.Count == 0)
        {
            c.Note("no shore with deep water within 4 km of the spawn: live fight not tested");
            c.Report(" (live fight not tested: no shore found)");
            yield break;
        }
        var rig = new FishRig(player);
        try
        {
            yield return FightBody(c, rig, shores);
            ClearOverrides();
            Fight.Shutdown();
            rig.DestroySpawned();
            if (rig.Travelled)
            {
                var back = new Box();
                yield return rig.Travel(rig.Origin, rig.OriginRotation, back, TravelTimeout);
                rig.Back = back.Ok;
                c.Check(back.Ok, "travel back to the spawn: " + back.Detail);
            }
        }
        finally
        {
            // Probe time-out or throw: SafeRunner dispose every level, this run (no yield here).
            rig.Restore();
        }
        c.Report();
    }

    private static IEnumerator FightBody(Checks c, FishRig rig, List<KeyValuePair<Vector3, Vector3>> shores)
    {
        var p = rig.P;
        var level = ZoneSystem.instance.m_waterLevel;
        var stand = Vector3.zero;
        var waterDir = Vector3.forward;
        var found = false;
        // WorldGenerator heights miss rivers, rocks and locations: try the spots in turn until one is real (dry
        // ground under the player, deep water where the float goes).
        for (var s = 0; s < shores.Count && !found; s++)
        {
            stand = shores[s].Key;
            waterDir = shores[s].Value;
            var trip = new Box();
            rig.Travelled = true;
            yield return rig.Travel(stand, Quaternion.LookRotation(waterDir), trip, TravelTimeout);
            if (!trip.Ok)
            {
                c.Note($"shore {s + 1} at {F(stand)}: travel failed ({trip.Detail})");
                continue;
            }
            // Let the area settle (water volumes, terrain).
            for (var i = 0; i < 60; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            var spotCheck = p.transform.position + waterDir * 10f;
            var floor = ZoneSystem.instance.GetGroundHeight(spotCheck);
            if (p.IsSwimming() || p.transform.position.y < level + 0.2f || floor > level - 2f)
            {
                c.Note($"shore {s + 1} at {F(stand)} not usable (swimming {p.IsSwimming()}, player y "
                       + $"{F(p.transform.position.y)}, floor 10 m out {F(floor)}, water {F(level)})");
                continue;
            }
            stand = p.transform.position;
            found = true;
            c.Note($"shore {s + 1} at {F(stand)}, water toward {F(waterDir)}, floor 10 m out {F(floor)}, travel "
                   + trip.Detail);
        }
        if (!c.Check(found, $"a usable shore among {shores.Count} candidates"))
        {
            yield break;
        }
        rig.Noon();
        rig.TakeStaminaRate();
        rig.TakeControls();
        rig.Face(waterDir);

        // Rod in hand, bait as ammo data, float and Fish1 in the water.
        var rod = rig.Give("FishingRod");
        if (!c.Check(rod != null, "FishingRod given"))
        {
            yield break;
        }
        p.EquipItem(rod, false);
        Transform rodTop = null;
        for (var i = 0; i < 100 && rodTop == null; i++)
        {
            yield return null;
            rodTop = Utils.FindChild(p.transform, "_RodTop");
        }
        if (!c.Check(rodTop != null, "rod top (_RodTop) shows after equipping the rod"))
        {
            yield break;
        }
        var floatPrefab = ZNetScene.instance.GetPrefab("FishingRodFloat");
        if (floatPrefab == null)
        {
            var attack = rod.m_shared.m_attack;
            var projectile = attack != null && attack.m_attackProjectile != null
                ? attack.m_attackProjectile.GetComponent<Projectile>()
                : null;
            floatPrefab = projectile != null ? projectile.m_spawnOnHit : null;
            c.Note("no prefab named FishingRodFloat; float from the rod's projectile: "
                   + (floatPrefab != null ? floatPrefab.name : "none"));
        }
        else
        {
            c.Note("float prefab FishingRodFloat found");
        }
        var baitPrefab = ObjectDB.instance.GetItemPrefab("FishingBait");
        var fishPrefab = ZNetScene.instance.GetPrefab("Fish1");
        if (!c.Check(floatPrefab != null && baitPrefab != null && fishPrefab != null, "float, bait and Fish1 prefabs exist"))
        {
            yield break;
        }
        var fishItem = fishPrefab.GetComponent<ItemDrop>();
        var fishName = fishItem != null ? fishItem.m_itemData.m_shared.m_name : "$animal_fish1";
        rig.Remember(fishName);
        var bait = baitPrefab.GetComponent<ItemDrop>().m_itemData.Clone();
        bait.m_dropPrefab = baitPrefab;

        var spot = stand + waterDir * 10f;
        var floatPos = new Vector3(spot.x, level + 0.2f, spot.z);
        var ffGo = UnityEngine.Object.Instantiate(floatPrefab, floatPos, Quaternion.identity);
        rig.Track(ffGo);
        var ff = ffGo.GetComponent<FishingFloat>();
        ff.Setup(p, Vector3.zero, 0f, null, rod, bait);
        var fishGo = UnityEngine.Object.Instantiate(fishPrefab, new Vector3(spot.x, level - 1.5f, spot.z), Quaternion.identity);
        rig.Track(fishGo);
        var fish = fishGo.GetComponent<Fish>();
        for (var i = 0; i < 10; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        if (!c.Check(ff != null && fish != null && ff.GetCatch() == null, "float and fish alive in the water"))
        {
            yield break;
        }

        // Pending rules: hooked fish stay vanilla (no fight).
        ServerRules.TestPending = true;
        ff.SetCatch(fish);
        for (var i = 0; i < 5; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        c.Check(Fight.Current == null, "pending rules: hooked fish, no take-over");
        ServerRules.TestPending = false;
        // Defaults for the whole live fight (the tester's own config never decide the expected numbers).
        ServerRules.TestRules = new FightRules();
        var turn = FightRules.Default.RodAngle + 15f;

        // Calm, fish pinned inside the bar: line in, no stamina used, regen on.
        Fight.TestPhase = FightPhase.Calm;
        Fight.TestPin = FishPin.InZone;
        for (var i = 0; i < 5 && Fight.Current == null; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        var fight = Fight.Current;
        if (!c.Check(fight != null && ReferenceEquals(fight.Fish, fish) && ReferenceEquals(fight.Float, ff),
                "fight starts on the hooked fish"))
        {
            yield break;
        }
        c.Note($"fight: difficulty {F(fight.Profile.Difficulty)} ({fight.Profile.Motion}), quality {fight.Quality}, "
               + $"line {F(ff.m_lineLength)} m, float prefab {Utils.GetPrefabName(ffGo)}");
        yield return null;
        yield return null;
        c.Check(FightHud.BarShown, "calm: catch bar on screen");
        SelfTest.Screenshot(FightName, "calm-bar");
        yield return null;

        p.m_stamina = p.GetMaxStamina() * 0.5f;
        p.m_staminaRegenTimer = 0f;
        var line0 = ff.m_lineLength;
        var stamina0 = p.GetStamina();
        var t0 = Time.time;
        rig.Reel(true);
        yield return new WaitForSeconds(1.5f);
        var line1 = ff.m_lineLength;
        c.Note($"calm reel speed {F((line0 - line1) / Mathf.Max(0.01f, Time.time - t0))} m/s at Fishing "
               + $"{F(p.GetSkillFactor(Skills.SkillType.Fishing) * 100f)} (float pull speed {F(ff.m_pullLineSpeed)} to "
               + $"{F(ff.m_pullLineSpeedMaxSkill)}, pull cost {F(ff.m_pullStaminaUse)}, x{F(ff.m_pullStaminaUseMaxSkillMultiplier)} "
               + $"at 100; fish cost calm {F(fish.m_staminaUse)}, fighting {F(fish.m_escapeStaminaUse)}; break distance "
               + $"{F(ff.m_breakDistance)}, max {F(ff.m_maxDistance)})");
        c.Check(line1 < line0 - 0.5f, $"fish in the bar: line comes in ({F(line0)} -> {F(line1)} m)");
        c.Check(p.GetStamina() >= stamina0 - 0.01f && p.m_staminaRegenTimer <= 0f,
            $"fish in the bar: no stamina used, regen on ({F(stamina0)} -> {F(p.GetStamina())}, regen timer {F(p.m_staminaRegenTimer)})");

        // Calm, fish away from the bar: line hold, stamina drain.
        Fight.TestPin = FishPin.Away;
        rig.Reel(false);
        yield return new WaitForFixedUpdate();
        rig.Fill(p.GetMaxStamina());
        var line2 = ff.m_lineLength;
        var stamina2 = p.GetStamina();
        var t2 = Time.time;
        yield return new WaitForSeconds(0.5f);
        c.Note($"off-bar drain {F((stamina2 - p.GetStamina()) / Mathf.Max(0.01f, Time.time - t2))} stamina/s");
        c.Check(Mathf.Abs(ff.m_lineLength - line2) < 0.05f, $"fish out of the bar: line holds ({F(line2)} -> {F(ff.m_lineLength)} m)");
        c.Check(p.GetStamina() < stamina2 - 1f, $"fish out of the bar: stamina drains ({F(stamina2)} -> {F(p.GetStamina())})");
        c.Check(!fight.Bar.InZone, "pinned away: fish outside the zone");

        // Struggle to the right.
        Fight.TestPin = null;
        Fight.TestSide = FightLogic.Right;
        Fight.TestPhase = FightPhase.Struggle;
        FightHud.TestArrow = true;
        rig.Fill(p.GetMaxStamina());
        yield return new WaitForFixedUpdate();
        yield return null;
        yield return null;
        c.Check(fight.Phase == FightPhase.Struggle && !FightHud.BarShown, "struggle: catch bar gone");
        c.Check(fish.m_nview.GetZDO().GetFloat(ZDOVars.s_escape) > 0f, "struggle: splash key (s_escape float) on for other games");
        c.Check(FightHud.ArrowShown, "struggle: arrow shows when turned on");

        // No reel: fish take line and swim to its right.
        rig.Reel(false);
        var playerPos = p.transform.position;
        var fishStartDir = Flat(fish.transform.position - playerPos);
        var line3 = ff.m_lineLength;
        var t3 = Time.time;
        yield return new WaitForSeconds(1f);
        c.Note($"fish takes line at {F((ff.m_lineLength - line3) / Mathf.Max(0.01f, Time.time - t3))} m/s");
        var fishNowDir = Flat(fish.transform.position - playerPos);
        var turned = FightLogic.SignedYaw(fishStartDir, fishNowDir);
        c.Check(ff.m_lineLength > line3 + 0.3f, $"struggle, no reel: fish takes line ({F(line3)} -> {F(ff.m_lineLength)} m)");
        c.Check(turned > 2f, $"struggle right: fish swims to the fisher's right ({F(turned)} deg around the fisher)");
        c.Note($"fish speed {F(fish.m_body.linearVelocity.magnitude)} m/s, run {F(fight.RunDir)}");
        SelfTest.Screenshot(FightName, "struggle-right");
        yield return null;

        // Wrong side (rod to the right of the line): x4 stamina, no line.
        var lineDir = Flat(ff.transform.position - p.transform.position);
        rig.Face(Quaternion.Euler(0f, turn, 0f) * lineDir);
        rig.Fill(p.GetMaxStamina());
        rig.Reel(true);
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        var verdictWrong = fight.Verdict;
        var line4 = ff.m_lineLength;
        var stamina4 = p.GetStamina();
        var t4 = Time.time;
        yield return new WaitForSeconds(0.4f);
        var wrongRate = (stamina4 - p.GetStamina()) / Mathf.Max(0.01f, Time.time - t4);
        c.Check(verdictWrong == RodVerdict.Wrong, $"fish right, rod {F(turn)} deg right: wrong side");
        c.Check(ff.m_lineLength <= line4 + 0.001f && ff.m_lineLength > line4 - 0.05f,
            $"wrong side reel: no line in ({F(line4)} -> {F(ff.m_lineLength)} m)");

        // Right side (rod to the left of the line): normal cost, line in.
        lineDir = Flat(ff.transform.position - p.transform.position);
        rig.Face(Quaternion.Euler(0f, -turn, 0f) * lineDir);
        rig.Fill(p.GetMaxStamina());
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        var verdictGood = fight.Verdict;
        var line5 = ff.m_lineLength;
        var stamina5 = p.GetStamina();
        var t5 = Time.time;
        yield return new WaitForSeconds(0.4f);
        var goodRate = (stamina5 - p.GetStamina()) / Mathf.Max(0.01f, Time.time - t5);
        c.Note($"struggle: right side reel {F((line5 - ff.m_lineLength) / Mathf.Max(0.01f, Time.time - t5))} m/s for "
               + $"{F(goodRate)} stamina/s; wrong side {F(wrongRate)} stamina/s");
        c.Check(verdictGood == RodVerdict.Good, $"fish right, rod {F(turn)} deg left: right side");
        c.Check(ff.m_lineLength < line5 - 0.05f, $"right side reel: line comes in ({F(line5)} -> {F(ff.m_lineLength)} m)");
        c.Note($"right side reel: float {F(Vector3.Distance(rodTop.position, ff.transform.position) - ff.m_lineLength)} m "
               + $"past the line (the reel works up to {F(FightLogic.StruggleDrag)} m in a fight)");
        var ratio = goodRate > 0.01f ? wrongRate / goodRate : 0f;
        var wantRatio = FightRules.Default.WrongSideStamina;
        c.Check(goodRate > 0.5f && ratio > wantRatio - 1f && ratio < wantRatio + 1f,
            $"wrong side costs about x{F(wantRatio)} ({F(wrongRate)}/s vs {F(goodRate)}/s, x{F(ratio)})");
        SelfTest.Screenshot(FightName, "struggle-arrow");
        yield return null;

        // Fish turn left: same rod now wrong.
        Fight.TestSide = FightLogic.Left;
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        c.Check(fight.Verdict == RodVerdict.Wrong, "fish switch to the left: rod on the left is now wrong");
        lineDir = Flat(ff.transform.position - p.transform.position);
        rig.Face(Quaternion.Euler(0f, turn, 0f) * lineDir);
        rig.Fill(p.GetMaxStamina());
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        c.Check(fight.Verdict == RodVerdict.Good, "turned the rod right: right side again");

        // Calm again: bar back, splash key off.
        Fight.TestPhase = FightPhase.Calm;
        FightHud.TestArrow = null;
        rig.Reel(false);
        yield return new WaitForFixedUpdate();
        yield return null;
        yield return null;
        c.Check(fight.Phase == FightPhase.Calm && FightHud.BarShown, "fight over: catch bar back");
        c.Check(Near(fish.m_nview.GetZDO().GetFloat(ZDOVars.s_escape), 0f), "fight over: splash key off");

        // Reel to the end (fast test rules): fish in the bag, float gone, fight over.
        ServerRules.TestRules = new FightRules { ReelSpeed = FightRules.ReelSpeedMax };
        Fight.TestPin = FishPin.InZone;
        rig.Face(Flat(ff.transform.position - p.transform.position));
        rig.Fill(p.GetMaxStamina());
        var before = rig.Inv.CountItems(fishName);
        var start = Time.time;
        while (ff != null && Time.time - start < 25f)
        {
            yield return new WaitForFixedUpdate();
        }
        yield return null;
        c.Check(ff == null, $"reeled in: float gone ({F(Time.time - start)} s)");
        c.Check(Fight.Current == null, "reeled in: fight over");
        c.Check(rig.Inv.CountItems(fishName) == before + 1, $"reeled in: {fishName} in the inventory");
        yield return null;
        c.Check(!FightHud.BarShown, "reeled in: bar gone");
        ServerRules.TestRules = new FightRules();
        Fight.TestPin = null;
        Fight.TestPhase = null;

        // Second fish, stamina at 0: lost, fish let go, float stay (vanilla loss).
        var ff2Go = UnityEngine.Object.Instantiate(floatPrefab, floatPos, Quaternion.identity);
        rig.Track(ff2Go);
        var ff2 = ff2Go.GetComponent<FishingFloat>();
        ff2.Setup(p, Vector3.zero, 0f, null, rod, bait);
        var fish2Go = UnityEngine.Object.Instantiate(fishPrefab, new Vector3(spot.x, level - 1.5f, spot.z), Quaternion.identity);
        rig.Track(fish2Go);
        var fish2 = fish2Go.GetComponent<Fish>();
        for (var i = 0; i < 10; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        ff2.SetCatch(fish2);
        for (var i = 0; i < 5 && Fight.Current == null; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        c.Check(Fight.Current != null && ReferenceEquals(Fight.Current.Fish, fish2), "second fish: fight starts");
        p.m_stamina = 0f;
        p.m_staminaRegenTimer = 5f;
        for (var i = 0; i < 3; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        c.Check(Fight.Current == null, "0 stamina: fight over");
        c.Check(ff2 != null && ff2.GetCatch() == null, "0 stamina: float stays, empty (vanilla loss)");
        c.Check(fish2 != null && !fish2.IsHooked(), "0 stamina: the fish is let go (vanilla forgets this)");
        rig.Fill(p.GetMaxStamina());
    }

    // Shore spots tried by the live test (each a trip of about 8 s).
    private const int MaxShores = 5;

    // Land just above the sea (or a lake), dry 3 m behind too, with deep water 10 m away in one of 8 directions and
    // room on both sides of it for the side runs. WorldGenerator heights, no zone needed (the live test checks the real
    // ground on arrival). Not Ashlands (hot water), Deep North nor Mountain. Spots at least 150 m apart, nearest first.
    private static List<KeyValuePair<Vector3, Vector3>> FindShores(Vector3 origin, int max)
    {
        var list = new List<KeyValuePair<Vector3, Vector3>>();
        var gen = WorldGenerator.instance;
        var level = ZoneSystem.instance.m_waterLevel;
        for (var r = 50f; r <= 4000f; r += 25f)
        {
            for (var a = 0; a < 360; a += 6)
            {
                var x = origin.x + Mathf.Sin(a * Mathf.Deg2Rad) * r;
                var z = origin.z + Mathf.Cos(a * Mathf.Deg2Rad) * r;
                var h = gen.GetHeight(x, z);
                if (h < level + 0.6f || h > level + 3f)
                {
                    continue;
                }
                var biome = gen.GetBiome(x, z);
                if (biome == Heightmap.Biome.AshLands || biome == Heightmap.Biome.DeepNorth
                    || biome == Heightmap.Biome.Mountain)
                {
                    continue;
                }
                var far = true;
                foreach (var known in list)
                {
                    far &= Vector2.Distance(new Vector2(known.Key.x, known.Key.z), new Vector2(x, z)) >= 150f;
                }
                if (!far)
                {
                    continue;
                }
                for (var k = 0; k < 8; k++)
                {
                    var dir = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                    var side = Vector3.Cross(Vector3.up, dir);
                    var w = new Vector3(x, 0f, z) + dir * 10f;
                    if (gen.GetHeight(x - dir.x * 3f, z - dir.z * 3f) < level + 0.5f
                        || gen.GetHeight(x + dir.x * 5f, z + dir.z * 5f) > level - 1f
                        || gen.GetHeight(w.x, w.z) > level - 3f
                        || gen.GetHeight(w.x + side.x * 6f, w.z + side.z * 6f) > level - 2.5f
                        || gen.GetHeight(w.x - side.x * 6f, w.z - side.z * 6f) > level - 2.5f
                        || gen.GetHeight(w.x + dir.x * 8f, w.z + dir.z * 8f) > level - 3f)
                    {
                        continue;
                    }
                    list.Add(new KeyValuePair<Vector3, Vector3>(new Vector3(x, h + 0.5f, z), dir));
                    if (list.Count >= max)
                    {
                        return list;
                    }
                    break;
                }
            }
        }
        return list;
    }
#endif
}
