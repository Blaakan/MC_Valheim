#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.FishingFightMod;

// Debug build only. The real thing in place of a test shortcut (same class as SelfTests.cs, rig of SelfTests.cs), each
// in its own test at a real shore:
//   fishing.nibble         T01: real cast, six Perch put 3 m around the float: one picks the float by itself, swims to
//                          it and nibbles (no nibble given by the test), the float dips, Block at once = "Hooked"
//   fishing.wrong-bait     T01: the same cast with a fishing bait that is not on the Perch's bait list: a Perch comes
//                          by itself, the game says "It's not taking the bait", Block hooks nothing
//   fishing.shallows       T24: real reel-in until the fish is in water too shallow for it, then a fight with the
//                          side left to the fish and its own side changes off: every turn has shallow water ahead and
//                          deeper water the other way, at most one turn, the run goes along the shore, arrow steady
//   fishing.lift           T28: own phase timer, a few metres of line from a shore above the water: right-side reel
//                          in a fight until the short line lifts the fish out of the water = fight over at once; no
//                          new fight while it hangs; the fisher walks down until it is in the water again = the next
//                          fight after the calm time left (real water, the game's own Fish.IsOutOfWater)
//   fishing.toggle-escape  T20: feature off mid-fight: speed of the fish through its quiet second (said, for a person
//                          to judge the look), then the normal game's own fight: splash objects at the fish, a normal
//                          short length
//   fishing.log-clean      T22: no error line of this mod and none naming its code since the plugin started (BootTap,
//                          a log listener made when the settings are bound). Registered last: every other test of the
//                          mod ran before it.
internal static partial class SelfTests
{
    private const string NibbleName = "fishing.nibble";
    private const string WrongBaitName = "fishing.wrong-bait";
    private const string ShallowsName = "fishing.shallows";
    private const string LiftName = "fishing.lift";
    private const string ToggleEscapeName = "fishing.toggle-escape";
    private const string LogCleanName = "fishing.log-clean";

    // Namespace of the mod's code, as a stack trace prints it.
    private const string CodeName = "MC.Farming.FishingFightMod.";

    private static void RegisterReal()
    {
        SelfTest.Register(NibbleName, RunNibble);
        SelfTest.Register(WrongBaitName, RunWrongBait);
        SelfTest.Register(ShallowsName, RunShallows);
        SelfTest.Register(LiftName, RunLift);
        SelfTest.Register(ToggleEscapeName, RunToggleEscape);
    }

    private static void UnregisterReal()
    {
        SelfTest.Unregister(NibbleName);
        SelfTest.Unregister(WrongBaitName);
        SelfTest.Unregister(ShallowsName);
        SelfTest.Unregister(LiftName);
        SelfTest.Unregister(ToggleEscapeName);
    }

    // Last test of the mod (it reads what every test before it left in the log).
    private static void RegisterLast() => SelfTest.Register(LogCleanName, RunLogClean);

    private static void UnregisterLast() => SelfTest.Unregister(LogCleanName);

    // ---------- fishing.nibble (T01) ----------

    private static IEnumerator RunNibble() => RunLive(NibbleName, true, NibbleBody);

    // Me = a float cast for real with Perch put around it.
    private sealed class Pond
    {
        internal FishingFloat Float;
        internal Vector3 Center;
        internal readonly List<Fish> Perch = new List<Fish>();
        internal readonly List<float> From = new List<float>();
        internal string BaitName = "";
        internal float BaitChance;
        internal bool Ready;
    }

    // A real cast (as fishing.cast: a second, shorter one when the first went past the line's reach), the fisher
    // quiet again, then six Perch 3 m around the float. Ready = all of it there.
    private static IEnumerator CastAmongPerch(Checks c, FishRig rig, Stage st, Pond pond)
    {
        var p = rig.P;
        rig.Fill(p.GetMaxStamina());
        var cast = new CastResult();
        yield return Cast(rig, st, 1f, null, cast);
        var ff = cast.Float;
        for (var i = 0; i < 10 && ff != null; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        if (ff == null)
        {
            c.Note("first cast left no float (" + cast.Detail + "); casting again with a shorter hold");
            rig.Fill(p.GetMaxStamina());
            cast = new CastResult();
            yield return Cast(rig, st, 0.45f, null, cast);
            ff = cast.Float;
        }
        if (!c.Check(ff != null, "cast: a float of this player lands (" + cast.Detail + ")"))
        {
            yield break;
        }
        var asked = Time.time;
        while (ff != null && !ff.IsInWater() && Time.time - asked < 3f)
        {
            yield return new WaitForFixedUpdate();
        }
        if (!c.Check(ff != null && ff.IsInWater() && ff.GetCatch() == null, "cast: the float sits in the water, nothing on it"))
        {
            yield break;
        }

        // The cast made noise: a fish that hears a player keeps away from floats (Fish.CustomFixedUpdate). Wait
        // until the fisher is quiet, as a player who casts and waits is.
        asked = Time.time;
        while ((p.InAttack() || p.GetNoiseRange() > 0.5f) && Time.time - asked < 10f)
        {
            yield return new WaitForFixedUpdate();
        }
        c.Check(p.GetNoiseRange() <= 0.5f && ff != null,
            $"the fisher stands still and quiet after the cast (heard {F(p.GetNoiseRange())} m around)");
        if (ff == null)
        {
            yield break;
        }

        // Six Perch 3 m around the float, on both sides of it, facing it (like "spawn Fish1" next to the float).
        // Each at least 2.5 m off in x: the game's own "at the float" test compares x and height only.
        var center = ff.transform.position;
        var offsets = new[]
        {
            new Vector3(3f, 0f, 0f), new Vector3(-3f, 0f, 0f), new Vector3(2.5f, 0f, 1.6f), new Vector3(-2.5f, 0f, 1.6f),
            new Vector3(2.5f, 0f, -1.6f), new Vector3(-2.5f, 0f, -1.6f),
        };
        foreach (var off in offsets)
        {
            var at = new Vector3(center.x + off.x, st.Level - 1f, center.z + off.z);
            if (!ZoneSystem.instance.GetSolidHeight(at, out var floor) || st.Level - floor < 1.8f)
            {
                continue;
            }
            var go = UnityEngine.Object.Instantiate(st.FishPrefab, at, Quaternion.LookRotation(Flat(center - at).normalized));
            rig.Track(go);
            var one = go.GetComponent<Fish>();
            if (one != null)
            {
                pond.Perch.Add(one);
                pond.From.Add(Vector3.Distance(Flat(at), Flat(center)));
            }
        }
        if (!c.Check(pond.Perch.Count >= 3, $"{pond.Perch.Count} Perch put in the water 3 m around the float (each needs water 1.8 m deep)"))
        {
            yield break;
        }
        pond.BaitName = ff.GetBait() ?? "";
        foreach (var bait in pond.Perch[0].m_baits)
        {
            if (bait != null && bait.m_bait != null && bait.m_bait.name == pond.BaitName)
            {
                pond.BaitChance = Mathf.Max(pond.BaitChance, bait.m_chance);
            }
        }
        var first = pond.Perch[0];
        c.Note($"float {F(Vector3.Distance(Flat(center), Flat(p.transform.position)))} m from the fisher, bait '{pond.BaitName}'; a Perch picks a float "
               + $"within {F(ff.m_range)} m with chance {F(first.m_baseHookChance)} each time it chooses where to swim, and takes this bait "
               + $"with chance {F(pond.BaitChance)}; it swims at {F(first.m_speed)} m/s, turns {F(first.m_turnRate)} deg/s");
        pond.Float = ff;
        pond.Center = center;
        pond.Ready = true;
    }

    private static IEnumerator NibbleBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        ServerRules.TestRules = new FightRules();
        FightHud.TestDisplay = DefaultDisplay;
        var given = rig.Give("FishingBait", 5);
        if (!c.Check(given != null, "FishingBait given"))
        {
            yield break;
        }
        var pond = new Pond();
        yield return CastAmongPerch(c, rig, st, pond);
        if (!pond.Ready)
        {
            yield break;
        }
        var ff = pond.Float;
        var perch = pond.Perch;
        var from = pond.From;
        var center = pond.Center;
        if (!c.Check(pond.BaitChance > 0f, $"the Perch takes the bait '{pond.BaitName}' in this game (its bait list)"))
        {
            yield break;
        }

        // Watch: nothing given by the test. First natural nibble with the right bait ends the wait.
        var hookedText = Localize("$msg_fishing_hooked");
        var hookedBefore = Stat(PlayerStatType.FishHooked);
        var nibbleBase = ff.m_nibbleTime;
        var targeted = new bool[perch.Count];
        Fish nibbler = null;
        var lastY = center.y;
        var yBefore = center.y;
        var took = false;
        var watch = Time.time;
        while (ff != null && nibbler == null && Time.time - watch < 30f)
        {
            lastY = ff.transform.position.y;
            yield return new WaitForFixedUpdate();
            if (ff == null)
            {
                break;
            }
            took |= Fight.Current != null;
            for (var i = 0; i < perch.Count; i++)
            {
                if (perch[i] != null && ReferenceEquals(perch[i].m_waypointFF, ff))
                {
                    targeted[i] = true;
                }
            }
            if (ff.m_nibbler != null && ff.m_nibbleTime > nibbleBase)
            {
                nibbler = ff.m_nibbler;
                yBefore = lastY;
            }
        }
        var waited = Time.time - watch;
        var went = 0;
        var refused = 0;
        for (var i = 0; i < perch.Count; i++)
        {
            went += targeted[i] ? 1 : 0;
            refused += perch[i] != null && ff != null && ReferenceEquals(perch[i].m_failedBait, ff) ? 1 : 0;
        }
        if (!c.Check(ff != null && nibbler != null,
                $"a Perch swims to the float and nibbles by itself within 30 s ({went} of {perch.Count} Perch went for the float, {refused} refused the bait, waited {F(waited)} s)"))
        {
            yield break;
        }
        c.Check(!took, "before the bite nothing of the fight runs");

        // Block at once, like a player who sees the float dip.
        var gap = Vector3.Distance(Flat(nibbler.transform.position), Flat(ff.transform.position));
        var index = -1;
        for (var i = 0; i < perch.Count; i++)
        {
            if (ReferenceEquals(perch[i], nibbler))
            {
                index = i;
            }
        }
        rig.Fill(p.GetMaxStamina());
        rig.Reel(true);
        Fish caught = null;
        var textAtHook = "";
        var dipped = false;
        var lowest = ff.transform.position.y;
        var press = Time.time;
        while (ff != null && Time.time - press < 0.45f)
        {
            yield return new WaitForFixedUpdate();
            if (ff == null)
            {
                break;
            }
            // The nibble pushes the float down on the next physics step.
            dipped |= ff.m_body.linearVelocity.y < -1f;
            lowest = Mathf.Min(lowest, ff.transform.position.y);
            if (caught == null)
            {
                caught = ff.GetCatch();
                if (caught != null)
                {
                    textAtHook = CenterText();
                }
            }
        }
        rig.Reel(false);
        c.Check(index >= 0 && targeted[index],
            "the fish that nibbled is one of the test's Perch, and it had picked the float as the place to swim to (the game's own choice)");
        if (index >= 0)
        {
            c.Check(gap < from[index] - 0.5f,
                $"it swam to the float: {F(from[index])} m from it when put in the water, {F(gap)} m at the nibble, {F(waited)} s later");
        }
        c.Check(dipped && yBefore - lowest > 0.05f, $"nibble: the float dips ({F(yBefore - lowest)} m down)");
        c.Check(caught != null && ReferenceEquals(caught, nibbler) && caught.IsHooked() && ff != null && ReferenceEquals(ff.GetCatch(), caught),
            "Block right after the nibble: the fish that nibbled is on the hook");
        c.Check(textAtHook == hookedText, $"the game says \"{hookedText}\" (got \"{textAtHook}\")");
        c.Check(Near(Stat(PlayerStatType.FishHooked), hookedBefore + 1f), "counted as a hooked fish");
        if (caught == null || ff == null)
        {
            yield break;
        }
        for (var i = 0; i < 5 && Fight.Current == null; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        var fight = Fight.Current;
        yield return null;
        yield return null;
        c.Check(fight != null && ReferenceEquals(fight.Fish, caught) && ReferenceEquals(fight.Float, ff) && FightHud.BarShown,
            "the fight takes over only now, on the hooked fish: the catch bar is on screen");
    }

    // ---------- fishing.wrong-bait (T01) ----------

    private static IEnumerator RunWrongBait() => RunLive(WrongBaitName, true, WrongBaitBody);

    // Same cast with a bait the Perch does not take: a Perch comes, the game says so, nothing is hooked.
    private static IEnumerator WrongBaitBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        ServerRules.TestRules = new FightRules();
        FightHud.TestDisplay = DefaultDisplay;
        // A fishing bait of the game that is not on the Perch's own bait list.
        var perchData = st.FishPrefab.GetComponent<Fish>();
        var taken = new HashSet<string>();
        if (perchData != null)
        {
            foreach (var bait in perchData.m_baits)
            {
                if (bait != null && bait.m_bait != null)
                {
                    taken.Add(bait.m_bait.name);
                }
            }
        }
        var normal = st.BaitPrefab.GetComponent<ItemDrop>();
        ItemDrop other = null;
        foreach (var prefab in ObjectDB.instance.m_items)
        {
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop != null && prefab.name.StartsWith("FishingBait", StringComparison.Ordinal) && !taken.Contains(prefab.name)
                && drop.m_itemData.m_shared.m_itemType == normal.m_itemData.m_shared.m_itemType
                && drop.m_itemData.m_shared.m_ammoType == normal.m_itemData.m_shared.m_ammoType)
            {
                other = drop;
                break;
            }
        }
        if (!c.Check(perchData != null && taken.Count > 0 && other != null,
                $"the game has a fishing bait the Perch does not take (the Perch takes: {string.Join(", ", new List<string>(taken).ToArray())})"))
        {
            yield break;
        }
        var wrongName = other.gameObject.name;
        rig.Remember(other.m_itemData.m_shared.m_name);
        var given = rig.Give(wrongName, 3);
        if (!c.Check(given != null && p.EquipItem(given, false), $"{wrongName} given and set as the bait to cast"))
        {
            yield break;
        }
        var pond = new Pond();
        yield return CastAmongPerch(c, rig, st, pond);
        if (!pond.Ready)
        {
            yield break;
        }
        var ff = pond.Float;
        var perch = pond.Perch;
        if (!c.Check(pond.BaitName == wrongName && pond.BaitChance <= 0f, $"the float carries {wrongName} (it carries '{pond.BaitName}')"))
        {
            yield break;
        }

        // Watch: nothing given by the test. The first Perch that comes and refuses ends the wait.
        var wrongText = Localize("$msg_fishing_wrongbait");
        var hookedBefore = Stat(PlayerStatType.FishHooked);
        var targeted = new bool[perch.Count];
        var refuser = -1;
        var textAtRefusal = "";
        var took = false;
        var watch = Time.time;
        while (ff != null && refuser < 0 && Time.time - watch < 30f)
        {
            yield return new WaitForFixedUpdate();
            if (ff == null)
            {
                break;
            }
            took |= Fight.Current != null;
            for (var i = 0; i < perch.Count; i++)
            {
                if (perch[i] == null)
                {
                    continue;
                }
                if (ReferenceEquals(perch[i].m_waypointFF, ff))
                {
                    targeted[i] = true;
                }
                if (refuser < 0 && ReferenceEquals(perch[i].m_failedBait, ff))
                {
                    refuser = i;
                    textAtRefusal = CenterText();
                }
            }
        }
        var waited = Time.time - watch;
        var went = 0;
        for (var i = 0; i < perch.Count; i++)
        {
            went += targeted[i] ? 1 : 0;
        }
        if (!c.Check(ff != null && refuser >= 0,
                $"a Perch swims to the float and tries the wrong bait within 30 s ({went} of {perch.Count} Perch went for the float, waited {F(waited)} s)"))
        {
            yield break;
        }
        if (textAtRefusal != wrongText)
        {
            // The message may reach the screen a frame after the nibble.
            yield return null;
            textAtRefusal = CenterText();
        }
        if (ff == null)
        {
            c.Check(false, "the float was gone right after the wrong-bait nibble");
            yield break;
        }
        var gap = perch[refuser] != null ? Vector3.Distance(Flat(perch[refuser].transform.position), Flat(ff.transform.position)) : -1f;
        c.Check(targeted[refuser] && gap >= 0f && gap < pond.From[refuser] - 0.5f,
            $"it had picked the float by itself and swam to it ({F(pond.From[refuser])} m from it when put in the water, {F(gap)} m at the nibble, {F(waited)} s later)");
        c.Check(textAtRefusal == wrongText, $"wrong bait: the game says \"{wrongText}\" (got \"{textAtRefusal}\")");

        // Block right after it, like a player who saw the float move: nothing is hooked, no fight.
        rig.Fill(p.GetMaxStamina());
        rig.Reel(true);
        var press = Time.time;
        var hooked = false;
        while (ff != null && Time.time - press < 0.45f)
        {
            yield return new WaitForFixedUpdate();
            hooked |= ff != null && ff.GetCatch() != null;
            took |= Fight.Current != null;
        }
        rig.Reel(false);
        yield return null;
        took |= Fight.Current != null;
        c.Check(!hooked && !took && !FightHud.Shown && Near(Stat(PlayerStatType.FishHooked), hookedBefore),
            "wrong bait: Block right after the nibble hooks nothing, no fight starts, no catch bar");
    }

    // ---------- fishing.shallows (T24) ----------

    private static IEnumerator RunShallows() => RunLive(ShallowsName, true, ShallowsBody);

    private static IEnumerator ShallowsBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        FightHud.TestDisplay = DefaultDisplay;
        FightHud.TestArrow = true;
        // Hooked 10 m out, calm pinned, the fish kept in the zone: the line comes in (twice the normal reel speed),
        // until the water under the fish is clearly too shallow for it.
        ServerRules.TestRules = new FightRules { ReelSpeed = 2f };
        Fight.TestPhase = FightPhase.Calm;
        Fight.TestPin = FishPin.InZone;
        var hook = new Hooked();
        yield return HookNew(rig, st, 1, hook);
        var fight = hook.Fight;
        var ff = hook.Float;
        var fish = hook.Fish;
        if (!c.Check(fight != null, "fight starts on the Perch hooked 10 m out"))
        {
            yield break;
        }
        var minDepth = fish.m_minDepth;
        var line0 = ff.m_lineLength;
        var start = Time.time;
        while (ff != null && fish != null && ReferenceEquals(Fight.Current, fight) && ff.m_lineLength > 3.2f && Time.time - start < 14f)
        {
            rig.Fill(p.GetMaxStamina());
            yield return new WaitForFixedUpdate();
            if (ff == null || fish == null)
            {
                break;
            }
            if (!fish.IsOutOfWater() && fish.GetPointDepth(fish.transform.position) < minDepth - 0.2f)
            {
                break;
            }
        }
        // The reel stops: fish off the zone for free, and no line is taken in the fight to come (the fish stays
        // where the fisher brought it).
        ServerRules.TestRules = new FightRules { OffBarStamina = 0f, LineRunSpeed = 0f, StruggleSeconds = FightRules.StruggleSecondsMax };
        Fight.TestPin = FishPin.Away;
        var reelTime = Time.time - start;
        var settle = Time.time;
        while (ff != null && fish != null && ReferenceEquals(Fight.Current, fight) && Time.time - settle < 1f)
        {
            rig.Fill(p.GetMaxStamina());
            yield return new WaitForFixedUpdate();
        }
        if (ff == null || fish == null || !ReferenceEquals(Fight.Current, fight))
        {
            c.Check(false, "the fight ended while the fish was reeled toward the beach");
            yield break;
        }
        var depthHere = fish.GetPointDepth(fish.transform.position);
        var out0 = Vector3.Dot(Flat(fish.transform.position - st.Stand), st.WaterDir);
        if (!c.Check(!fish.IsOutOfWater() && depthHere < minDepth,
                $"the fish is reeled into the shallows and is still in the water ({F(line0)} -> {F(ff.m_lineLength)} m of line in {F(reelTime)} s, the fish {F(out0)} m out from the fisher, water {F(depthHere)} m deep under it; a Perch keeps to water at least {F(minDepth)} m deep)"))
        {
            yield break;
        }

        // The fight: phase pinned, side left to the fish, its own side changes off: every turn seen comes from the
        // shallow-water rule. Not reeling. Watched 2.5 s (a fight of the default settings lasts 2 to 4 s).
        Fight.TestSide = 0;
        Fight.TestSwitchRate = 0f;
        Fight.TestPin = null;
        rig.Reel(false);
        Fight.TestPhase = FightPhase.Struggle;
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        if (!c.Check(ff != null && fish != null && ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Struggle && fight.Side != 0,
                "a fight starts with the fish in the shallows"))
        {
            yield break;
        }
        var shore = Vector3.Cross(Vector3.up, st.WaterDir).normalized;
        var lastSide = fight.Side;
        var changes = 0;
        var unexplained = 0;
        var lastChange = -1f;
        var minGap = float.MaxValue;
        var ticks = 0;
        var wet = 0;
        var shallowAhead = 0;
        var due = 0;
        var along = 0f;
        var across = 0f;
        var reason = false;
        var reason1 = false;
        var reason2 = false;
        var last = fish.transform.position;
        var arrowOn = 0;
        var arrowFlips = 0;
        var lastArrow = 0;
        var watchStart = Time.time;
        while (ff != null && fish != null && ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Struggle
               && Time.time - watchStart < 2.5f)
        {
            rig.Fill(p.GetMaxStamina());
            // What the fish's own swim code is about to see (FishPatches: 2 m along each run).
            var pos = fish.transform.position;
            var ahead = fight.RunDir != Vector3.zero ? fish.GetPointDepth(pos + fight.RunDir * 2f) : float.MaxValue;
            var other = fight.OtherRunDir != Vector3.zero ? fish.GetPointDepth(pos + fight.OtherRunDir * 2f) : float.MaxValue;
            // A hair wider than the rule: the fish reads the depths a few centimetres from where the test does.
            reason2 = reason1;
            reason1 = reason;
            reason = ahead < minDepth + 0.1f && (other >= minDepth - 0.1f || other > ahead + FightLogic.ShallowTurnMargin - 0.1f);
            yield return new WaitForFixedUpdate();
            if (ff == null || fish == null || !ReferenceEquals(Fight.Current, fight))
            {
                break;
            }
            ticks++;
            wet += fish.IsOutOfWater() ? 0 : 1;
            shallowAhead += ahead < minDepth ? 1 : 0;
            due += FightLogic.TurnFromShallows(ahead, other, minDepth) ? 1 : 0;
            if (fight.Side != lastSide)
            {
                if (lastChange >= 0f)
                {
                    minGap = Mathf.Min(minGap, Time.time - lastChange);
                }
                lastChange = Time.time;
                lastSide = fight.Side;
                changes++;
                if (!reason && !reason1 && !reason2)
                {
                    unexplained++;
                }
            }
            var step = fish.transform.position - last;
            last = fish.transform.position;
            along += Mathf.Abs(Vector3.Dot(step, shore));
            across += Mathf.Abs(Vector3.Dot(step, st.WaterDir));
            if (FightHud.ArrowShown)
            {
                arrowOn++;
                var sign = FightHud.TestArrowPosition.x > 0f ? 1 : -1;
                if (lastArrow != 0 && sign != lastArrow)
                {
                    arrowFlips++;
                }
                lastArrow = sign;
            }
        }
        var watched = Time.time - watchStart;
        c.Check(ticks > 80 && ff != null && fish != null && ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Struggle,
            $"the fight goes on for the {F(watched)} s watched ({ticks} ticks)");
        c.Check(ticks > 0 && wet >= ticks * 0.8f, $"the fish stays in the water, where it can run (on {wet} of {ticks} ticks)");
        c.Check(shallowAhead >= 10,
            $"the fish has water too shallow for it ahead of its run (on {shallowAhead} of {ticks} ticks; a turn to the deeper side was due on {due} of them)");
        c.Check(changes <= 1 && unexplained == 0,
            $"the fish turns once at most, and only where the water ahead is too shallow and the other way is deeper ({changes} turn(s) in {F(watched)} s, {unexplained} without that reason, shortest time on a side {(changes < 2 ? "n/a" : F(minGap) + " s")})");
        c.Check(along > 1.5f && along > across,
            $"the fish runs along the shore ({F(along)} m along it, {F(across)} m toward or away from it)");
        c.Check(arrowOn >= ticks - 10 && arrowFlips <= changes && arrowFlips <= 1,
            $"with the arrow setting on the arrow stays steady ({arrowFlips} flip(s) for {changes} turn(s), shown on {arrowOn} of {ticks} ticks)");
    }

    // ---------- fishing.lift (T28) ----------

    private static IEnumerator RunLift() => RunLive(LiftName, true, LiftBody);

    private static IEnumerator LiftBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        var turn = FightRules.Default.RodAngle + 15f;
        var dt = Time.fixedDeltaTime;
        FightHud.TestDisplay = DefaultDisplay;
        // The settings of the item: CalmSeconds 2, StruggleSeconds 10. Fish off the zone for free while calm (the
        // line holds). The fight's own phase timer runs: nothing pinned but the marker.
        var quick = new FightRules
        {
            CalmSeconds = FightRules.CalmSecondsMin, StruggleSeconds = FightRules.StruggleSecondsMax, OffBarStamina = 0f,
        };
        ServerRules.TestRules = quick;
        Fight.TestPin = FishPin.Away;

        // Float and Perch a few metres out, where the water is deep enough for a Perch to swim.
        var near = Vector3.zero;
        var found = false;
        for (var d = 5.5f; d <= 8.01f && !found; d += 0.5f)
        {
            var at = st.Stand + st.WaterDir * d;
            if (ZoneSystem.instance.GetSolidHeight(at, out var floor) && st.Level - floor >= 1.2f)
            {
                near = at;
                found = true;
            }
        }
        if (!c.Check(found, "water at least 1.2 m deep 5.5 to 8 m out from the fisher"))
        {
            yield break;
        }
        var ff = SpawnFloat(rig, st, new Vector3(near.x, st.Level + 0.2f, near.z));
        var fish = SpawnFish(rig, st, 1, new Vector3(near.x, st.Level - 0.6f, near.z));
        for (var i = 0; i < 10; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        if (!c.Check(ff != null && fish != null, "float and Perch in the water"))
        {
            yield break;
        }
        ff.SetCatch(fish);
        for (var i = 0; i < 5 && (Fight.Current == null || !ReferenceEquals(Fight.Current.Fish, fish)); i++)
        {
            yield return new WaitForFixedUpdate();
        }
        var fight = Fight.Current;
        var rodTop = st.RodTop;
        if (!c.Check(fight != null && ReferenceEquals(fight.Fish, fish) && ReferenceEquals(fight.Float, ff) && rodTop != null
                     && fight.Phase == FightPhase.Calm && !fish.IsOutOfWater(),
                "fight starts calm on the hooked Perch, the fish in the water"))
        {
            yield break;
        }
        var above = rodTop.position.y - st.Level;
        c.Note($"a few metres of line: {F(ff.m_lineLength)} m, float {F(Vector3.Distance(Flat(near), Flat(st.Stand)))} m out; the fisher stands "
               + $"{F(p.transform.position.y - st.Level)} m above the water, the rod's top {F(above)} m above it");
        var d01 = fight.Profile.D01;
        var calmHigh = quick.CalmSeconds * 1.4f * Mathf.Lerp(1.2f, 0.8f, d01);

        // The first fight comes by the fish's own timer.
        var asked = Time.time;
        while (ff != null && fish != null && ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Calm
               && Time.time - asked < calmHigh + 1.5f)
        {
            rig.Fill(p.GetMaxStamina());
            yield return new WaitForFixedUpdate();
        }
        if (!c.Check(ff != null && fish != null && ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Struggle,
                $"the fish starts to fight by itself after its calm time ({F(Time.time - asked)} s, at most {F(calmHigh)} s)"))
        {
            yield break;
        }
        var fightLength = fight.PhaseLeft;
        var fightStart = Time.time;
        var zdo = fish.m_nview.GetZDO();
        var splashOn = zdo.GetFloat(ZDOVars.s_escape) > 0f;
        var mark = tap.Mark();

        // Reel on the right side (rod kept turned away from the run, as a player does) until the short line lifts
        // the fish out of the water: the game's own answer (Fish.IsOutOfWater).
        var lifted = false;
        var lineAtLift = -1f;
        while (ff != null && fish != null && ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Struggle
               && Time.time - fightStart < 9f)
        {
            rig.Face(WantLook(p, ff, -turn * fight.Side));
            rig.Fill(p.GetMaxStamina());
            rig.Reel(true);
            yield return new WaitForFixedUpdate();
            if (ff == null || fish == null)
            {
                break;
            }
            if (fish.IsOutOfWater())
            {
                lifted = true;
                lineAtLift = ff.m_lineLength;
                break;
            }
        }
        var inFight = Time.time - fightStart;
        if (ff == null || fish == null || !ReferenceEquals(Fight.Current, fight))
        {
            rig.Reel(false);
            c.Check(false, $"the fishing ended during the right-side reel, {F(inFight)} s into the fight (caught, line broke or fish lost): the fish was never lifted out of the water");
            yield break;
        }
        if (!c.Check(lifted,
                $"reeling on the right side in a fight lifts the fish out of the water ({F(inFight)} s into a fight of {F(fightLength)} s, line {F(ff.m_lineLength)} m, rod top {F(above)} m above the water, the fish {(fish.IsOutOfWater() ? "out of" : "in")} the water, fight phase {fight.Phase})"))
        {
            rig.Reel(false);
            yield break;
        }
        // The fight ends at once: this tick or the next two (the fight reads the water on its own tick).
        var ticks = 0;
        while (ticks < 10 && fish != null && ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Struggle)
        {
            rig.Fill(p.GetMaxStamina());
            yield return new WaitForFixedUpdate();
            ticks++;
        }
        rig.Reel(false);
        var ended = Time.time - fightStart;
        yield return null;
        yield return null;
        if (!c.Check(fish != null && ff != null && ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Calm && ticks <= 2,
                $"the fish leaves the water during a fight: the fight ends at once ({ticks} ticks after it left the water, line {F(lineAtLift)} m)"))
        {
            yield break;
        }
        c.Check(ended < fightLength - 1f,
            $"long before its time: the fight ended after {F(ended)} s, by its own clock it had {F(fightLength)} s");
        c.Check(splashOn && FightHud.BarShown && Near(zdo.GetFloat(ZDOVars.s_escape), 0f) && tap.Has("Fight: calm for ", mark),
            "fight ended by leaving the water: the catch bar is back, the splash key is off, Debug line \"Fight: calm for ... s.\"");

        // No new fight while the fish hangs out of the water: the calm clock runs only on ticks in the water.
        var calmLeft = fight.PhaseLeft;
        var hang = calmHigh + 1.5f;
        var hangTicks = 0;
        var dryTicks = 0;
        var wetSeconds = 0f;
        var fought = false;
        asked = Time.time;
        while (ff != null && fish != null && ReferenceEquals(Fight.Current, fight) && Time.time - asked < hang)
        {
            rig.Fill(p.GetMaxStamina());
            yield return new WaitForFixedUpdate();
            if (ff == null || fish == null)
            {
                break;
            }
            hangTicks++;
            if (fish.IsOutOfWater())
            {
                dryTicks++;
            }
            else
            {
                wetSeconds += dt;
            }
            fought |= fight.Phase == FightPhase.Struggle;
        }
        if (ff == null || fish == null || !ReferenceEquals(Fight.Current, fight))
        {
            c.Check(false, "the fishing ended while the fish hung on the short line");
            yield break;
        }
        c.Check(hangTicks > 0 && dryTicks >= hangTicks * 0.9f,
            $"the fish hangs out of the water on the short line for {F(hang)} s (out of the water on {dryTicks} of {hangTicks} ticks)");
        c.Check(fought ? wetSeconds >= calmLeft - 0.15f : Mathf.Abs(calmLeft - fight.PhaseLeft - wetSeconds) <= 0.15f,
            fought
                ? $"a new fight started during the wait: the fish had been back in the water for {F(wetSeconds)} s of a calm time of {F(calmLeft)} s"
                : $"no new fight starts while the fish hangs there, longer than any calm time ({F(hang)} s, the calm time is at most {F(calmHigh)} s): the calm clock only ran on its {F(wetSeconds)} s in the water ({F(calmLeft)} -> {F(fight.PhaseLeft)} s left)");
        if (fought)
        {
            // The rest needs a calm fish out of the water.
            c.Check(false, "the fish did not stay out of the water: walking down to the water was not tested");
            yield break;
        }

        // Walk down to the water like a player (forward held) until the fish is back in it.
        var walkFrom = p.transform.position;
        var wetRun = 0;
        asked = Time.time;
        while (ff != null && fish != null && ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Calm
               && Time.time - asked < 7f && wetRun < 3 && !p.IsSwimming() && p.transform.position.y > st.Level - 0.6f)
        {
            rig.Face(st.WaterDir);
            rig.Fill(p.GetMaxStamina());
            p.SetControls(new Vector3(0f, 0f, 1f), false, false, false, false, false, false, false, false, false, false);
            yield return new WaitForFixedUpdate();
            if (ff == null || fish == null)
            {
                break;
            }
            wetRun = fish.IsOutOfWater() ? 0 : wetRun + 1;
        }
        rig.Reel(false);
        var walked = Vector3.Distance(Flat(walkFrom), Flat(p.transform.position));
        if (!c.Check(ff != null && fish != null && ReferenceEquals(Fight.Current, fight) && !fish.IsOutOfWater() && !p.IsSwimming(),
                $"the fisher walks down to the water and the fish is back in it (walked {F(walked)} m in {F(Time.time - asked)} s, the fisher now {F(p.transform.position.y - st.Level)} m above the water, swimming {p.IsSwimming()})"))
        {
            yield break;
        }

        // Back in the water the calm time runs on: the next fight starts when it is over.
        var left = fight.PhaseLeft;
        var inWater = 0f;
        asked = Time.time;
        while (ff != null && fish != null && ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Calm
               && inWater < left + 1.5f && Time.time - asked < 12f)
        {
            rig.Fill(p.GetMaxStamina());
            yield return new WaitForFixedUpdate();
            if (ff == null || fish == null)
            {
                break;
            }
            if (!fish.IsOutOfWater())
            {
                inWater += dt;
            }
        }
        c.Check(ff != null && fish != null && ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Struggle
                && Mathf.Abs(inWater - left) <= 0.2f,
            $"back in the water the calm time runs on and the next fight starts when it is over ({F(inWater)} s in the water, {F(left)} s were left; a Perch's calm time with these settings is {F(quick.CalmSeconds * 0.6f * Mathf.Lerp(1.2f, 0.8f, d01))} to {F(calmHigh)} s)");
    }

    // ---------- fishing.toggle-escape (T20) ----------

    private static IEnumerator RunToggleEscape() => RunLive(ToggleEscapeName, true, ToggleEscapeBody);

    private static IEnumerator ToggleEscapeBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        var view = FeatureRegistry.Find(ModInfo.Guid);
        if (!c.Check(view != null && view.Value.IsActive, "the mod is active before the test"))
        {
            yield break;
        }
        var me = view.Value;
        // A fight to the right, not reeled: the fish runs.
        QuietCalm();
        var hook = new Hooked();
        yield return HookNew(rig, st, 1, hook);
        if (!c.Check(hook.Fight != null, "fight starts on the hooked Perch"))
        {
            yield break;
        }
        var ff = hook.Float;
        var fish = hook.Fish;
        Fight.TestPin = null;
        Fight.TestSide = FightLogic.Right;
        Fight.TestPhase = FightPhase.Struggle;
        rig.Reel(false);
        rig.Fill(p.GetMaxStamina());
        yield return new WaitForSeconds(0.8f);
        if (!c.Check(ff != null && fish != null && ReferenceEquals(Fight.Current, hook.Fight) && fish.IsEscaping(), "the fish fights"))
        {
            yield break;
        }
        var zdo = fish.m_nview.GetZDO();
        var running = fish.m_body.linearVelocity.magnitude;
        var names = SplashNames(fish);
        var seen = new HashSet<int>();
        NewSplashes(fish, names, seen);

        // Turned off (what unticking it in the MC Mods panel does: the framework's own turn-off).
        Block(rig, true);
        var offAt = Time.time;
        c.Check(running > 1f && !me.IsActive && Fight.Current == null && fish.IsHooked() && !fish.IsEscaping(),
            $"turned off during a fight: the fish ran at {F(running)} m/s, now it is hooked and no longer fights ({me.State})");

        // Its quiet second: speed every quarter second and the way it still goes (for a person to judge the look).
        var speeds = "";
        var nextSample = 0.25f;
        var path = 0f;
        var last = fish.transform.position;
        var slowest = running;
        var escapeAt = -1f;
        while (ff != null && fish != null && Time.time - offAt < 2.5f)
        {
            rig.Fill(p.GetMaxStamina());
            yield return new WaitForFixedUpdate();
            if (ff == null || fish == null)
            {
                break;
            }
            if (fish.IsEscaping())
            {
                escapeAt = Time.time - offAt;
                break;
            }
            var speed = fish.m_body.linearVelocity.magnitude;
            slowest = Mathf.Min(slowest, speed);
            path += Vector3.Distance(fish.transform.position, last);
            last = fish.transform.position;
            if (Time.time - offAt >= nextSample)
            {
                speeds += $"{F(nextSample)} s: {F(speed)} m/s; ";
                nextSample += 0.25f;
            }
        }
        if (ff == null || fish == null)
        {
            c.Check(false, "the float or the fish was gone within 2.5 s of the turn-off");
            Block(rig, false);
            yield break;
        }
        c.Note($"after the turn-off the fish (running at {F(running)} m/s) slows like this: {speeds}slowest {F(slowest)} m/s, {F(path)} m moved before its own fight began");
        c.Check(escapeAt > 0.8f, $"about a second later the normal game's own fight starts ({(escapeAt < 0f ? "not within 2.5 s" : F(escapeAt) + " s after the turn-off")})");
        // Fish still driven keep its run speed: a drop of a hair prove nothing, a fifth do (two game runs of
        // fishing.toggle saw 0.61 and 0.10 of the run speed half a second after the turn-off).
        c.Check(slowest < running * 0.8f,
            $"meanwhile the fish is not driven any more: it loses a fifth of its speed or more ({F(running)} -> {F(slowest)} m/s at its slowest)");

        // The normal game's fight that follows: splashes at the fish, a normal short length. Watched until 6 s after
        // the turn-off (the game makes a splash on about one tick in forty, and goes on after its fight ended: it
        // writes the end of the fight as a whole number, the splash reads the other number).
        var vanillaMax = fish.m_escapeMax + fish.m_escapeMaxPerLevel;
        var escapeLength = escapeAt > 0f ? fish.m_escapeTime : -1f;
        var escapeEnd = -1f;
        // Splash object made in the quiet second (before the normal game's fight) not count below: said only.
        var quietSplashes = NewSplashes(fish, names, seen);
        var splashes = 0;
        var ticks = 0;
        var hookedAll = true;
        while (escapeAt > 0f && ff != null && fish != null && Time.time - offAt < 6f)
        {
            rig.Fill(p.GetMaxStamina());
            yield return new WaitForFixedUpdate();
            if (ff == null || fish == null)
            {
                break;
            }
            ticks++;
            hookedAll &= fish.IsHooked() && zdo.GetInt(ZDOVars.s_hooked) == 1;
            if (escapeEnd < 0f && !fish.IsEscaping())
            {
                escapeEnd = Time.time - offAt;
            }
            if (ticks % 5 == 0)
            {
                splashes += NewSplashes(fish, names, seen);
            }
        }
        if (fish != null)
        {
            splashes += NewSplashes(fish, names, seen);
        }
        c.Check(escapeAt > 0f && ff != null && fish != null && hookedAll,
            "the fish stays on the hook through the normal game's fight (the normal game's reel has it)");
        c.Check(escapeAt > 0f && escapeLength > 0f && escapeLength <= vanillaMax + 0.05f && escapeEnd > 0f
                && escapeEnd - escapeAt <= vanillaMax + 0.3f,
            $"the normal game's fight has a normal short length: {F(escapeLength)} s (at most {F(vanillaMax)} s for a plain Perch), over {(escapeEnd < 0f ? "not within the watch" : F(escapeEnd - escapeAt) + " s after it began")}");
        c.Check(names.Count > 0 && splashes > 0,
            $"splashes appear at the fish once the normal game's fight has begun ({splashes} new splash objects in the {F(Time.time - offAt - Mathf.Max(0f, escapeAt))} s from its start, {quietSplashes} in the quiet second before it; the game's splash effects: {string.Join(", ", names.ToArray())})");
        c.Check(!OwnPatchOn(typeof(Fish), nameof(Fish.CustomFixedUpdate)) && !OwnPatchOn(typeof(Fish), nameof(Fish.SwimDirection))
                && Fight.Current == null && !FightHud.Shown,
            "all that time the mod stays out: no patch on the fish, no fight, nothing on screen");
        Block(rig, false);
        c.Check(me.IsActive, "turned on again at the end");
    }

    // ---------- fishing.log-clean (T22) ----------

    // Me hear the log from the plugin's start (settings bound) to the game's end: error lines of this mod, and error
    // lines of any other source that name this mod's code (an exception that got out: Unity logs it with the stack).
    // Lines of the tests themselves ("[selftest] ...") left out. Log events can come from any thread: lock, never throw.
    private sealed class BootTap : ILogListener
    {
        // Probe's own line at the end of every test: "[selftest] END <name>: passed (...)" or "...: FAILED (...)".
        private const string EndMark = SelfTest.Prefix + " END ";
        private const string PassedMark = ": passed";

        private readonly object _gate = new object();
        private readonly List<string> _lines = new List<string>();
        private readonly HashSet<string> _passed = new HashSet<string>();
        private int _count;

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null || eventArgs.Source == null)
                {
                    return;
                }
                var error = (eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) != 0;
                if (!error && (eventArgs.Level & LogLevel.Info) == 0)
                {
                    return;
                }
                var text = eventArgs.Data as string ?? eventArgs.Data?.ToString();
                if (text == null)
                {
                    return;
                }
                if (!error)
                {
                    // Which tests ran and passed in this game (fishing.log-clean must know: see RunLogClean).
                    if (text.StartsWith(EndMark, StringComparison.Ordinal))
                    {
                        var at = text.IndexOf(PassedMark, EndMark.Length, StringComparison.Ordinal);
                        if (at > EndMark.Length)
                        {
                            lock (_gate)
                            {
                                _passed.Add(text.Substring(EndMark.Length, at - EndMark.Length));
                            }
                        }
                    }
                    return;
                }
                if (text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                {
                    return;
                }
                var source = eventArgs.Source.SourceName;
                if (source != ModInfo.Name && text.IndexOf(CodeName, StringComparison.Ordinal) < 0
                    && text.IndexOf(ModInfo.Guid, StringComparison.Ordinal) < 0)
                {
                    return;
                }
                var cut = text.IndexOf('\n');
                lock (_gate)
                {
                    _count++;
                    if (_lines.Count < 20)
                    {
                        _lines.Add(source + ": " + (cut > 0 ? text.Substring(0, cut).TrimEnd() : text));
                    }
                }
            }
            catch (Exception)
            {
                // Listener must never throw into the logger.
            }
        }

        public void Dispose()
        {
        }

        internal int Count
        {
            get
            {
                lock (_gate)
                {
                    return _count;
                }
            }
        }

        internal string First
        {
            get
            {
                lock (_gate)
                {
                    return _lines.Count > 0 ? _lines[0] : "";
                }
            }
        }

        // Names of 'tests' the probe did not report as passed in this game.
        internal List<string> NotPassed(string[] tests)
        {
            var missing = new List<string>();
            lock (_gate)
            {
                foreach (var test in tests)
                {
                    if (!_passed.Contains(test))
                    {
                        missing.Add(test);
                    }
                }
            }
            return missing;
        }
    }

    // Every other single-player test of the mod (the items the clean log is about are played by them). The log of
    // a game where some of them did not run (filtered run, -Only) or failed half way says nothing about those:
    // fishing.log-clean fails then, it never passes on a log nothing happened in.
    private static readonly string[] PlayThroughTests =
    {
        NetworkName, LogicName, PendingName, FightName, CastName, PhasesName, ExitsName, SwimName, ToggleName, InputName,
        DualName, HarpoonName, NibbleName, WrongBaitName, ShallowsName, LiftName, ToggleEscapeName, PadZoneName, PadTurnName,
        PadTrinketName, HarpoonThrowName, FilterName,
    };

    private static BootTap _boot;

    // Plugin.BindConfig (through RegisterAlways): once per game, never taken off.
    private static void StartBootTap()
    {
        if (_boot != null)
        {
            return;
        }
        _boot = new BootTap();
        BepInEx.Logging.Logger.Listeners.Add(_boot);
    }

    private static IEnumerator RunLogClean()
    {
        yield return null;
        if (_boot == null)
        {
            SelfTest.Fail(LogCleanName, "the log was not watched from the start of the game (the listener is made when the mod's settings are bound)");
            yield break;
        }
        var missing = _boot.NotPassed(PlayThroughTests);
        if (missing.Count > 0)
        {
            SelfTest.Fail(LogCleanName,
                $"{missing.Count} of the mod's {PlayThroughTests.Length} other single-player tests did not run and pass in this game before this one "
                + $"({string.Join(", ", missing.ToArray())}): the log says nothing about what they play. Run all of them in one game "
                + "(tools/Test-InWorld.ps1 without -Only, or -Only fishing.)");
            yield break;
        }
        var count = _boot.Count;
        if (count == 0)
        {
            SelfTest.Pass(LogCleanName,
                $"no error line of {ModInfo.Name} and no error line naming its code since the game started (the mod's {PlayThroughTests.Length} other single-player tests ran and passed before this one)");
        }
        else
        {
            SelfTest.Fail(LogCleanName, $"{count} error line(s) of {ModInfo.Name} or naming its code since the game started, first: {_boot.First}");
        }
    }
}
#endif
