using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace MC.Farming.BreedingStarInheritanceMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1) in
// throwaway single-player world with fresh character (god mode):
//   breeding.rule   pure rule + chance line + Monte Carlo (no world needed)
//   breeding.farmer own Farming published on own player ZDO, withdraw writes -1, own player count at any distance
//   breeding.birth  two tamed Boar (level 1 + 3) in front of camera; me drive vanilla Procreate (patched) for many
//                   births: min rule, chance 0/100, defaults, fallback partner, no partner, cap, never lower, floor
//   breeding.egg    same with two tamed Hen: egg quality follow rule
//   breeding.network server settings on the wire (encode, decode, clamp, refuse junk), effective settings in single
//                   player (own config, network message ignored, override), join check verdicts, every species'
//                   breeding tick longer than join check grace + disconnect delay (real prefab values, NOTE line)
// Me force numbers with Override (no config file write). Me put back Override and destroy all me spawn at end.
// More tests live in the other parts of this class: SelfTests.World.cs (single player), SelfTests.Cross.cs (with other
// MC mods), SelfTests.Mp.cs (client joined to a dedicated server + its server halves), SelfTests.Hooks.cs (switches,
// notes from the patches, log of this mod's lines).
internal static partial class SelfTests
{
    private const string RuleName = "breeding.rule";
    private const string FarmerName = "breeding.farmer";
    private const string BirthName = "breeding.birth";
    private const string EggName = "breeding.egg";
    private const string NetworkName = "breeding.network";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(RuleName, RunRule);
        SelfTest.Register(FarmerName, RunFarmer);
        SelfTest.Register(BirthName, RunBirth);
        SelfTest.Register(EggName, RunEgg);
        SelfTest.Register(NetworkName, RunNetwork);
        StartSessionLog();
        RegisterWorld();
        RegisterCross();
        RegisterWorldLast();
        RegisterMultiplayer();
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(RuleName);
        SelfTest.Unregister(FarmerName);
        SelfTest.Unregister(BirthName);
        SelfTest.Unregister(EggName);
        SelfTest.Unregister(NetworkName);
        UnregisterWorld();
        UnregisterCross();
        UnregisterMultiplayer();
        Override = null;
        RollOverride = null;
#endif
    }

#if DEBUG
    // Set = every birth use these rule numbers instead of config or server (Plugin.CurrentSettings).
    internal static RuleSettings? Override;

    private static readonly RuleSettings Never = new RuleSettings(0f, 0f, 0f, 2);
    private static readonly RuleSettings Always = new RuleSettings(100f, 100f, 100f, 2);

    // Test animals: fixed ranges (code defaults) so the test not depend on prefab data.
    private const float PartnerRange = 3f;
    private const float PenRange = 10f;
    private const float MateGap = 1.5f;

    // ---------- breeding.rule ----------

    private static IEnumerator RunRule()
    {
        var failures = new List<string>();
        var checks = 0;

        void Check(bool ok, string what)
        {
            checks++;
            if (!ok)
            {
                failures.Add(what);
            }
        }

        var def = RuleSettings.Defaults;

        // Lower parent, vanilla floor, no partner = own level.
        Check(BirthRule.BaseLevel(1, 3, 0) == 1, "base(1, 3) must be 1");
        Check(BirthRule.BaseLevel(3, 1, 0) == 1, "base(3, 1) must be 1");
        Check(BirthRule.BaseLevel(2, 2, 0) == 2, "base(2, 2) must be 2");
        Check(BirthRule.BaseLevel(5, 5, 0) == 5, "base(5, 5) must be 5 (never lowered)");
        Check(BirthRule.BaseLevel(5, 1, 0) == 1, "base(5, 1) must be 1");
        Check(BirthRule.BaseLevel(3, BirthRule.NoPartner, 0) == 3, "base(3, no partner) must be 3");
        Check(BirthRule.BaseLevel(1, 1, 2) == 2, "base(1, 1, min offspring 2) must be 2");
        Check(BirthRule.BaseLevel(1, 3, 3) == 3, "base(1, 3, min offspring 3) must be 3");
        Check(BirthRule.Cap(2) == 3 && BirthRule.Cap(1) == 2 && BirthRule.Cap(0) == 1, "cap = MaxStars + 1");

        // Chance line and "no farmer".
        Check(Near(BirthRule.Chance(true, 0f, def), 15f), "Farming 0 -> 15%");
        Check(Near(BirthRule.Chance(true, 25f, def), 23.75f), "Farming 25 -> 23.75%");
        Check(Near(BirthRule.Chance(true, 50f, def), 32.5f), "Farming 50 -> 32.5%");
        Check(Near(BirthRule.Chance(true, 100f, def), 50f), "Farming 100 -> 50%");
        Check(Near(BirthRule.Chance(true, 150f, def), 50f), "Farming 150 counts as 100 -> 50%");
        Check(Near(BirthRule.Chance(true, -5f, def), 15f), "Farming below 0 counts as 0 -> 15%");
        Check(Near(BirthRule.Chance(false, 100f, def), 10f), "no farmer known -> 10% whatever the level");
        Check(Near(BirthRule.Chance(true, 0f, new RuleSettings(10f, 50f, 10f, 2)), 10f), "ChanceAtFarming0 10 -> 10% at Farming 0");

        // Roll edges: 100 always, 0 never.
        Check(BirthRule.Wins(100f, 1f), "chance 100 must win with roll 1");
        Check(!BirthRule.Wins(0f, 0f), "chance 0 must lose with roll 0");
        Check(BirthRule.Wins(15f, 0.1499f), "chance 15 must win with roll 0.1499");
        Check(!BirthRule.Wins(15f, 0.15f), "chance 15 must lose with roll 0.15");

        // Whole decision.
        Check(BirthRule.Decide(1, 3, 0, def, true, 0f, 0f).Level == 2, "1+3, roll 0 -> 2");
        Check(BirthRule.Decide(1, 3, 0, def, true, 0f, 0.5f).Level == 1, "1+3, roll 0.5 at 15% -> 1");
        Check(BirthRule.Decide(3, 1, 0, def, true, 100f, 0.49f).Level == 2, "3+1, roll 0.49 at 50% -> 2");
        var atCap = BirthRule.Decide(3, 3, 0, def, true, 100f, 0f);
        Check(atCap.Level == 3 && atCap.AtCap && !atCap.Bonus && atCap.Chance == 0f, "3+3 -> 3, at the cap, no roll");
        var high = BirthRule.Decide(5, 5, 0, Always, true, 100f, 0f);
        Check(high.Level == 5 && high.AtCap, "5+5 -> 5 (never lowered to the cap, no extra)");
        Check(BirthRule.Decide(5, 1, 0, Always, true, 0f, 0.99f).Level == 2, "5+1 at 100% -> 2");
        Check(BirthRule.Decide(2, 2, 0, Always, true, 0f, 0.99f).Level == 3, "2+2 at 100% -> 3 (1 star + 1 = 2 stars)");
        Check(BirthRule.Decide(2, 2, 0, new RuleSettings(100f, 100f, 100f, 1), true, 0f, 0f).Level == 2, "MaxStars 1: 2+2 -> 2");
        Check(BirthRule.Decide(1, 1, 0, new RuleSettings(100f, 100f, 100f, 1), true, 0f, 0.99f).Level == 2, "MaxStars 1: 1+1 at 100% -> 2");
        Check(BirthRule.Decide(1, 1, 0, new RuleSettings(100f, 100f, 100f, 0), true, 0f, 0f).Level == 1, "MaxStars 0: never an extra star");
        Check(BirthRule.Decide(1, 1, 2, Always, true, 0f, 0.99f).Level == 3, "min offspring 2 then extra -> 3");
        Check(BirthRule.Decide(1, 1, 2, Never, true, 100f, 0f).Level == 2, "min offspring 2, chance 0 -> 2");
        Check(BirthRule.Decide(1, 3, 0, Never, true, 100f, 0f).Level == 1, "chance 0, roll 0 -> 1");
        Check(BirthRule.Decide(1, 3, 0, Always, true, 0f, 1f).Level == 2, "chance 100, roll 1 -> 2");
        Check(BirthRule.Decide(3, BirthRule.NoPartner, 0, Never, true, 0f, 0f).Level == 3, "no partner -> own level");

        // Monte Carlo: seeded (repeatable), then Unity's own generator (the one births use).
        var report = new StringBuilder();
        var rng = new System.Random(20260929);

        void MonteCarlo(string label, bool known, float farming, float expected, Func<float> roll, float tolerance)
        {
            const int n = 20000;
            var extra = 0;
            var wrong = 0;
            for (var i = 0; i < n; i++)
            {
                var own = (i & 1) == 0 ? 1 : 3; // both parents give birth
                var level = BirthRule.Decide(own, own == 1 ? 3 : 1, 0, def, known, farming, roll()).Level;
                if (level == 2)
                {
                    extra++;
                }
                else if (level != 1)
                {
                    wrong++;
                }
            }
            var pct = 100.0 * extra / n;
            Check(wrong == 0, $"{label}: {wrong} babies outside levels 1-2 from a 1 + 3 pair");
            Check(Math.Abs(pct - expected) <= tolerance, $"{label}: {pct.ToString("0.00", CultureInfo.InvariantCulture)}% extra stars, "
                + $"expected {Inv(expected)}% +/- {Inv(tolerance)}");
            report.Append(' ').Append(label).Append(' ').Append(pct.ToString("0.00", CultureInfo.InvariantCulture))
                .Append("% (expected ").Append(expected.ToString(CultureInfo.InvariantCulture)).Append("%);");
        }

        MonteCarlo("Farming 0", true, 0f, 15f, () => (float)rng.NextDouble(), 1.5f);
        MonteCarlo("Farming 50", true, 50f, 32.5f, () => (float)rng.NextDouble(), 1.5f);
        MonteCarlo("Farming 100", true, 100f, 50f, () => (float)rng.NextDouble(), 1.5f);
        MonteCarlo("no farmer", false, 0f, 10f, () => (float)rng.NextDouble(), 1.5f);
        MonteCarlo("Unity Random.value, Farming 0", true, 0f, 15f, () => UnityEngine.Random.value, 2f);

        if (failures.Count == 0)
        {
            SelfTest.Pass(RuleName, $"{checks} checks OK; 20000 rolls each:{report}");
        }
        else
        {
            SelfTest.Fail(RuleName, $"{failures.Count} of {checks} checks failed: {string.Join("; ", failures.ToArray())}");
        }
        yield break;
    }

    private static bool Near(float a, float b)
    {
        return Math.Abs(a - b) < 0.001f;
    }

    // ---------- breeding.network ----------

    private static IEnumerator RunNetwork()
    {
        var failures = new List<string>();
        var checks = 0;

        void Check(bool ok, string what)
        {
            checks++;
            if (!ok)
            {
                failures.Add(what);
            }
        }

        var def = RuleSettings.Defaults;
        RuleSettings r;
        float range;
        bool clamped;

        // Wire names and the defaults on the wire.
        Check(ServerSettings.SettingsRpc == ModInfo.Guid + ".Settings" && ServerSettings.RequestRpc == ModInfo.Guid + ".SettingsRequest",
            "RPC names must be <GUID>.Settings and <GUID>.SettingsRequest");
        var wire = ServerSettings.Encode(def, 60f);
        Check(wire == "1|15|50|10|60|2", $"defaults must encode as 1|15|50|10|60|2, got {wire}");
        Check(ServerSettings.TryDecode(wire, out r, out range, out clamped) && Same(r, def) && range == 60f && !clamped,
            "defaults must decode back unchanged, not clamped");

        // Decimals survive bit-exact.
        var odd = new RuleSettings(12.5f, 49.75f, 0.1f, 1);
        wire = ServerSettings.Encode(odd, 33.3f);
        Check(ServerSettings.TryDecode(wire, out r, out range, out clamped) && Same(r, odd) && range == 33.3f && !clamped,
            $"decimals must survive the trip ({wire})");

        // A comma-decimal system language must not change the wire.
        var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
        var cultureChecked = false;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            cultureChecked = true;
            wire = ServerSettings.Encode(odd, 33.3f);
            Check(wire.IndexOf(',') < 0 && ServerSettings.TryDecode(wire, out r, out range, out _) && Same(r, odd) && range == 33.3f,
                $"de-DE system language: the wire must keep dots and decode back ({wire})");
            Check(ServerSettings.TryDecode("1|12.5|49.75|0.1|33.3|1", out r, out range, out _) && Same(r, odd) && range == 33.3f,
                "de-DE system language: 1|12.5|49.75|0.1|33.3|1 must decode to 12.5 / 49.75 / 0.1 / 33.3 / 1");
        }
        catch (ArgumentException) // CultureNotFoundException and kin: runtime without that culture
        {
            SelfTest.Note(NetworkName, "culture de-DE not available in this runtime: the system-language check was skipped");
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;
        }

        // Out of range = brought back into the config ranges (chances 0-100, range 5-64, MaxStars 0-10).
        Check(ServerSettings.TryDecode("1|-5|150|200|1000|99", out r, out range, out clamped) && clamped
              && r.ChanceAtFarming0 == 0f && r.ChanceAtFarming100 == 100f && r.ChanceWithoutFarmer == 100f
              && range == 64f && r.MaxStars == 10,
            "1|-5|150|200|1000|99 must clamp to 0 / 100 / 100 / 64 / 10");
        Check(ServerSettings.TryDecode("1|0|100|0|2|-1", out r, out range, out clamped) && clamped && range == 5f && r.MaxStars == 0,
            "FarmerRange 2 must clamp to 5 and MaxStars -1 to 0");
        Check(ServerSettings.TryDecode("1|0|100|0|5|0", out r, out range, out clamped) && !clamped
              && r.ChanceAtFarming0 == 0f && r.ChanceAtFarming100 == 100f && range == 5f && r.MaxStars == 0,
            "edge values 0 / 100 / 0 / 5 / 0 are in range (not clamped)");
        Check(ServerSettings.TryDecode("1|0|100|0|64|10", out _, out range, out clamped) && !clamped && range == 64f,
            "edge values 64 / 10 are in range (not clamped)");

        // Unreadable = refused whole (caller keeps what it had).
        string[] junk =
        {
            null, "", "2|15|50|10|60|2", "1|15|50|10|60", "1|15|50|10|60|2|7", "1|abc|50|10|60|2", "1|NaN|50|10|60|2",
            "1|15|Infinity|10|60|2", "1|15|50|-Infinity|60|2", "1|15|50|10|1e40|2", "1|15|50|10|60|2.5", "1|15,5|50|10|60|2",
            "1| |50|10|60|2", "1|15|50|10|60|", "15|50|10|60|2|1",
        };
        var refused = 0;
        foreach (var bad in junk)
        {
            if (ServerSettings.TryDecode(bad, out _, out _, out _))
            {
                failures.Add($"unreadable message accepted: {(bad == null ? "null" : "\"" + bad + "\"")}");
            }
            else
            {
                refused++;
            }
            checks++;
        }

        // Pick: server numbers only when told to.
        var own = new BreedingSettings(def, 60f, SettingsSource.Own);
        var serverRule = new RuleSettings(0f, 100f, 5f, 1);
        var picked = ServerSettings.Pick(own, true, serverRule, 20f);
        Check(picked.Source == SettingsSource.Server && Same(picked.Rule, serverRule) && picked.FarmerRange == 20f,
            "a connected client with server numbers must use them (rule and range)");
        picked = ServerSettings.Pick(own, false, serverRule, 20f);
        Check(picked.Source == SettingsSource.Own && Same(picked.Rule, def) && picked.FarmerRange == 60f,
            "without server numbers the own settings must be used");

        // Accessor in this single-player world: own config, network message ignored, Debug override on top.
        var net = ZNet.instance;
        var accessor = "not checked (no world)";
        if (net == null)
        {
            failures.Add("no world: the single-player accessor checks need one");
        }
        else
        {
            Check(net.IsServer() && net.GetPeers().Count == 0, "the probe world must be single player (its own server, no peer)");
            var saved = Override;
            try
            {
                Override = null;
                var mine = Plugin.OwnSettings();
                var current = Plugin.CurrentSettings();
                Check(current.Source == SettingsSource.Own && Same(current.Rule, mine.Rule) && current.FarmerRange == mine.FarmerRange,
                    $"single player must use the own config (source {current.Source})");
                var result = ServerSettings.Receive("1|0|100|0|5|0");
                Check(result == ServerSettings.ReceiveResult.NotAClient, $"single player must ignore settings from the network (got {result})");
                current = Plugin.CurrentSettings();
                Check(current.Source == SettingsSource.Own && Same(current.Rule, mine.Rule) && current.FarmerRange == mine.FarmerRange,
                    "single player must still use the own config after a settings message");
                Override = serverRule;
                current = Plugin.CurrentSettings();
                Check(current.Source == SettingsSource.Test && Same(current.Rule, serverRule) && current.FarmerRange == mine.FarmerRange,
                    "the Debug override must force the rule numbers and keep the effective range");
                accessor = $"own config ({ServerSettings.Describe(mine.Rule, mine.FarmerRange)}), network message ignored, override on top";
            }
            finally
            {
                Override = saved;
            }
            Check(!PlayerCheck.HasWork, "single player: no join check may be pending");
        }

        // Join check: only a server acts, only on a player fully in and still there.
        Check(PlayerCheck.Decide(true, true, true, false, false, false) == JoinVerdict.Refuse, "no mod, setting off -> refuse");
        Check(PlayerCheck.Decide(true, true, true, false, false, true) == JoinVerdict.Allowed, "no mod, AllowPlayersWithoutMod on -> allowed");
        Check(PlayerCheck.Decide(true, true, true, false, true, false) == JoinVerdict.HasMod, "mod answered -> fine");
        Check(PlayerCheck.Decide(false, true, true, false, false, false) == JoinVerdict.Skip, "not the server -> never acts");
        Check(PlayerCheck.Decide(true, false, true, false, false, false) == JoinVerdict.Skip, "player left before the deadline -> nothing");
        Check(PlayerCheck.Decide(true, true, false, false, false, false) == JoinVerdict.Skip, "player not fully in -> nothing");
        Check(PlayerCheck.Decide(true, true, true, true, false, false) == JoinVerdict.Skip, "player already being kicked -> nothing");
        // Refused game gone before its first breeding tick: every species with Procreation, real prefab values.
        var gone = PlayerCheck.GraceSeconds + PlayerCheck.DisconnectDelay;
        var ticks = "not checked (no ZNetScene)";
        var scene = ZNetScene.instance;
        if (scene == null)
        {
            failures.Add("no ZNetScene: the breeding tick check needs one");
        }
        else
        {
            var species = new List<string>();
            var shortest = float.MaxValue;
            var shortestName = "";
            foreach (var prefab in scene.m_namedPrefabs.Values)
            {
                var procreation = prefab != null ? prefab.GetComponent<Procreation>() : null;
                if (procreation == null)
                {
                    continue;
                }
                species.Add($"{prefab.name} {Inv(procreation.m_updateInterval)} s");
                if (procreation.m_updateInterval < shortest)
                {
                    shortest = procreation.m_updateInterval;
                    shortestName = prefab.name;
                }
                Check(procreation.m_updateInterval > gone,
                    $"{prefab.name}: breeding tick {Inv(procreation.m_updateInterval)} s must be longer than the join check "
                    + $"grace + disconnect delay ({Inv(gone)} s)");
            }
            species.Sort(StringComparer.Ordinal);
            Check(species.Count > 0, "at least one prefab must have a Procreation component");
            if (species.Count > 0)
            {
                ticks = $"shortest breeding tick {Inv(shortest)} s ({shortestName}) > {Inv(gone)} s";
                SelfTest.Note(NetworkName, $"breeding ticks of the {species.Count} species with Procreation: "
                                           + string.Join(", ", species.ToArray()) + $"; shortest {Inv(shortest)} s "
                                           + $"({shortestName}); a refused game is gone after {Inv(gone)} s "
                                           + $"(grace {Inv(PlayerCheck.GraceSeconds)} s + disconnect delay "
                                           + $"{Inv(PlayerCheck.DisconnectDelay)} s)");
            }
        }

        yield return null;
        if (failures.Count == 0)
        {
            SelfTest.Pass(NetworkName, $"{checks} checks OK; defaults on the wire {ServerSettings.Encode(def, 60f)}; clamping to the "
                                       + $"config ranges; {refused} unreadable messages refused; system language "
                                       + (cultureChecked ? "de-DE checked" : "check skipped") + $"; single player: {accessor}; "
                                       + $"join check verdicts; {ticks}");
        }
        else
        {
            SelfTest.Fail(NetworkName, $"{failures.Count} of {checks} checks failed: {string.Join("; ", failures.ToArray())}");
        }
    }

    private static bool Same(in RuleSettings a, in RuleSettings b)
    {
        return a.ChanceAtFarming0 == b.ChanceAtFarming0 && a.ChanceAtFarming100 == b.ChanceAtFarming100
               && a.ChanceWithoutFarmer == b.ChanceWithoutFarmer && a.MaxStars == b.MaxStars;
    }

    // Log numbers with a dot whatever the system language.
    private static string Inv(float value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    // ---------- breeding.farmer ----------

    private static IEnumerator RunFarmer()
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(FarmerName, "no local player (the test needs a world)");
            yield break;
        }
        var failures = new List<string>();
        // Fresh probe character usually never farmed: reading must not add a Farming entry (Skills tab row).
        var hadEntry = FarmerSkill.HasFarmingEntry(player);
        var farming = FarmerSkill.OwnLevel(player);
        var far = player.transform.position + new Vector3(500f, 0f, 0f);

        FarmerSkill.Reset();
        FarmerSkill.PublishNow();
        var published = FarmerSkill.ReadPublished(player);
        if (published != farming)
        {
            failures.Add($"published {Inv(published)} on the player object, own Farming is {Inv(farming)}");
        }
        if (!FarmerSkill.FindBest(far, 60f, out var farmer, out var level, out var local) || !local
            || !ReferenceEquals(farmer, player) || level != farming)
        {
            failures.Add("own player must be the farmer at any distance (500 m)");
        }

        FarmerSkill.Withdraw();
        if (FarmerSkill.ReadPublished(player) != FarmerSkill.NotTakingPart)
        {
            failures.Add($"withdraw must write {Inv(FarmerSkill.NotTakingPart)}, found {Inv(FarmerSkill.ReadPublished(player))}");
        }
        if (!FarmerSkill.FindBest(far, 60f, out _, out _, out local) || !local)
        {
            failures.Add("own player must still count after the published value is withdrawn");
        }

        FarmerSkill.PublishNow();
        if (FarmerSkill.ReadPublished(player) != farming)
        {
            failures.Add("publishing again after the withdraw did not restore the value");
        }
        if (!hadEntry && FarmerSkill.HasFarmingEntry(player))
        {
            failures.Add("reading Farming added a Farming skill entry to a character that had none");
        }
        yield return null;

        if (failures.Count == 0)
        {
            var entry = hadEntry ? "the character has a Farming entry" : "no Farming entry before or after (none added)";
            SelfTest.Pass(FarmerName, $"own Farming {Inv(farming)} published under {FarmerSkill.Key}, withdraw writes -1, "
                                      + $"own player counts at 500 m; {entry}; {Player.GetAllPlayers().Count} player(s) loaded");
        }
        else
        {
            SelfTest.Fail(FarmerName, string.Join("; ", failures.ToArray()));
        }
    }

    // ---------- pens (breeding.birth, breeding.egg) ----------

    private sealed class Pen
    {
        internal readonly string Test;
        internal readonly bool Egg;
        internal readonly List<GameObject> Spawned = new List<GameObject>();
        internal readonly HashSet<int> Seen = new HashSet<int>();
        internal readonly List<string> Failures = new List<string>();
        internal readonly List<GameObject> Keep = new List<GameObject>();
        internal Vector3 Center;
        internal Vector3 Forward;
        internal Vector3 Side;
        internal Character A;
        internal Character B;
        internal bool BothBreed;
        internal string Offspring;
        internal int Births;

        internal Pen(string test, bool egg)
        {
            Test = test;
            Egg = egg;
        }
    }

    private static IEnumerator RunBirth()
    {
        var pen = new Pen(BirthName, egg: false);
        try
        {
            if (!SkipForStarLevelSystem(pen.Test) && SetupPen(pen, "Boar", 1, 3))
            {
                yield return null;
                var summary = new StringBuilder();

                // 1. Chance 0: every baby at the lower parent level, whoever gives birth (vanilla: 3 from B).
                Override = Never;
                for (var i = 0; i < 5; i++)
                {
                    var mother = i < 3 || !pen.BothBreed ? pen.B : pen.A;
                    Expect(pen, Breed(pen, mother, Mate(pen, mother), "chance 0"), 1, "chance 0, parents 1 + 3");
                    yield return null;
                }

                // 2. Chance 100: every baby one above the lower parent. Keep some for the screenshot.
                Override = Always;
                for (var i = 0; i < 4; i++)
                {
                    var mother = i % 2 == 0 || !pen.BothBreed ? pen.B : pen.A;
                    Expect(pen, Breed(pen, mother, Mate(pen, mother), "chance 100", keep: i < 2), 2, "chance 100, parents 1 + 3");
                    yield return null;
                }

                // 3. Default numbers with the real farmer (this character): only 1 or 2, never 3.
                Override = RuleSettings.Defaults;
                var counts = new int[11];
                for (var i = 0; i < 12; i++)
                {
                    var mother = i % 2 == 0 || !pen.BothBreed ? pen.B : pen.A;
                    var level = Breed(pen, mother, Mate(pen, mother), "defaults");
                    if (level >= 0)
                    {
                        counts[Mathf.Min(level, 10)]++;
                        if (level != 1 && level != 2)
                        {
                            pen.Failures.Add($"defaults, parents 1 + 3: baby level {level}, expected 1 or 2");
                        }
                    }
                    yield return null;
                }
                var farming = Player.m_localPlayer != null ? FarmerSkill.OwnLevel(Player.m_localPlayer) : 0f;
                summary.Append($"defaults (own Farming {Inv(farming)}, chance {Inv(BirthRule.Chance(true, farming, RuleSettings.Defaults))}%): "
                               + $"12 births -> level 1 x{counts[1]}, level 2 x{counts[2]}, level 3 x{counts[3]}; ");

                // 4. No note (pregnancy from before the mod): partner found at birth 6 m away (pen range 10 m).
                Override = Never;
                Expect(pen, Breed(pen, pen.B, pen.A, "fallback", before: zdo =>
                {
                    BirthRecord.Clear(zdo);
                    Place(pen.A, pen.Center + pen.Side * 6f, pen.Forward);
                }), 1, "no note, partner found at birth 6 m away");
                yield return null;

                // 5. No note and no tamed partner within the pen range: own level.
                Expect(pen, Breed(pen, pen.B, pen.A, "no partner", before: zdo =>
                {
                    BirthRecord.Clear(zdo);
                    Place(pen.A, pen.Center + pen.Side * 25f, pen.Forward);
                }), 3, "no note, partner 25 m away: own level");
                yield return null;

                // 6. Cap: 3 + 3 stays 3 even at 100%; 5 + 5 stays 5 (never lowered).
                Override = Always;
                pen.A.SetLevel(3);
                pen.B.SetLevel(3);
                Expect(pen, Breed(pen, pen.B, pen.A, "cap", keep: true), 3, "chance 100, parents 3 + 3 (at the cap)");
                yield return null;
                pen.A.SetLevel(5);
                pen.B.SetLevel(5);
                Expect(pen, Breed(pen, pen.B, pen.A, "above cap"), 5, "chance 100, parents 5 + 5 (never lowered)");
                yield return null;

                // 7. Vanilla floor m_minOffspringLevel before the extra star.
                pen.A.SetLevel(1);
                pen.B.SetLevel(1);
                var procB = pen.B.GetComponent<Procreation>();
                procB.m_minOffspringLevel = 2;
                Override = Never;
                Expect(pen, Breed(pen, pen.B, pen.A, "floor"), 2, "min offspring 2, parents 1 + 1, chance 0");
                yield return null;
                Override = Always;
                Expect(pen, Breed(pen, pen.B, pen.A, "floor + extra"), 3, "min offspring 2, parents 1 + 1, chance 100");
                procB.m_minOffspringLevel = 0;
                Override = null;
                yield return null;

                // Screenshot: kept babies in a row in front of the camera, stars in their health bars.
                Place(pen.A, pen.Center + pen.Side * 3f + pen.Forward * 2f, pen.Forward);
                Place(pen.B, pen.Center - pen.Side * 3f + pen.Forward * 2f, pen.Forward);
                var shown = new StringBuilder();
                for (var i = 0; i < pen.Keep.Count; i++)
                {
                    var baby = pen.Keep[i];
                    if (baby == null)
                    {
                        continue;
                    }
                    var c = baby.GetComponent<Character>();
                    Place(c, Ground(pen.Center - pen.Forward * 1.5f + pen.Side * ((i - (pen.Keep.Count - 1) * 0.5f) * 1.2f)), -pen.Forward);
                    shown.Append(c.GetLevel()).Append(' ');
                }
                var until = Time.time + 1f;
                while (Time.time < until)
                {
                    // Game show a creature's health bar and stars only after the player looked at it: me do the look.
                    foreach (var baby in pen.Keep)
                    {
                        if (baby != null)
                        {
                            LookAtHud(baby.GetComponent<Character>());
                        }
                    }
                    yield return null;
                }
                // What the screenshot must show (TESTING.md T18): piglets with 1 star and one with 2 stars in their
                // health bars. Me read the creature HUD in the frame of the shot: bar on screen, star marks by level.
                var oneStar = 0;
                var twoStars = 0;
                foreach (var baby in pen.Keep)
                {
                    var kept = baby != null ? baby.GetComponent<Character>() : null;
                    if (kept == null)
                    {
                        pen.Failures.Add("screenshot: a piglet kept for it vanished");
                        continue;
                    }
                    var level = kept.GetLevel();
                    if (!HudStars(kept, out var onScreen, out var star1, out var star2))
                    {
                        pen.Failures.Add($"screenshot: the creature HUD has no health bar with star marks for the level-{level} piglet");
                        continue;
                    }
                    if (!onScreen || star1 != (level == 2) || star2 != (level == 3))
                    {
                        pen.Failures.Add($"screenshot: level-{level} piglet: health bar on screen {onScreen}, 1-star mark {star1}, "
                                         + $"2-star mark {star2}; expected True / {level == 2} / {level == 3}");
                    }
                    oneStar += star1 ? 1 : 0;
                    twoStars += star2 ? 1 : 0;
                }
                if (oneStar < 1 || twoStars < 1)
                {
                    pen.Failures.Add($"screenshot: the health bars show {oneStar} piglet(s) with 1 star and {twoStars} with 2 stars; "
                                     + "expected at least one of each");
                }
                SelfTest.Screenshot(pen.Test, "piglets_with_stars");
                yield return null;
                yield return null;
                summary.Append($"screenshot shows piglets of level {shown.ToString().Trim()} (level 2 = 1 star, 3 = 2 stars)");

                Report(pen, summary.ToString());
            }
        }
        finally
        {
            Cleanup(pen);
        }
    }

    private static IEnumerator RunEgg()
    {
        var pen = new Pen(EggName, egg: true);
        try
        {
            if (!SkipForStarLevelSystem(pen.Test) && SetupPen(pen, "Hen", 1, 3))
            {
                yield return null;
                var summary = new StringBuilder();

                Override = Never;
                for (var i = 0; i < 3; i++)
                {
                    var mother = i < 2 || !pen.BothBreed ? pen.B : pen.A;
                    Expect(pen, Breed(pen, mother, Mate(pen, mother), "chance 0"), 1, "chance 0, hens 1 + 3: egg quality");
                    yield return null;
                }

                Override = Always;
                for (var i = 0; i < 3; i++)
                {
                    var mother = i < 2 || !pen.BothBreed ? pen.B : pen.A;
                    Expect(pen, Breed(pen, mother, Mate(pen, mother), "chance 100"), 2, "chance 100, hens 1 + 3: egg quality");
                    yield return null;
                }

                Override = RuleSettings.Defaults;
                var counts = new int[11];
                for (var i = 0; i < 8; i++)
                {
                    var mother = i % 2 == 0 || !pen.BothBreed ? pen.B : pen.A;
                    var quality = Breed(pen, mother, Mate(pen, mother), "defaults");
                    if (quality >= 0)
                    {
                        counts[Mathf.Min(quality, 10)]++;
                        if (quality != 1 && quality != 2)
                        {
                            pen.Failures.Add($"defaults, hens 1 + 3: egg quality {quality}, expected 1 or 2");
                        }
                    }
                    yield return null;
                }
                summary.Append($"defaults: 8 eggs -> quality 1 x{counts[1]}, quality 2 x{counts[2]}, quality 3 x{counts[3]}; ");

                Override = Always;
                pen.A.SetLevel(3);
                pen.B.SetLevel(3);
                Expect(pen, Breed(pen, pen.B, pen.A, "cap"), 3, "chance 100, hens 3 + 3 (at the cap): egg quality");
                Override = null;
                yield return null;

                Report(pen, summary.ToString());
            }
        }
        finally
        {
            Cleanup(pen);
        }
    }

    private static bool SkipForStarLevelSystem(string test)
    {
        if (!Compat.StarLevelSystemLoaded)
        {
            return false;
        }
        SelfTest.Note(test, "Star Level System is installed: this mod leaves births to it by design.");
        SelfTest.Pass(test, "skipped (Star Level System installed)");
        return true;
    }

    // Two tamed adults in front of the camera, 6 m away, facing away (babies spawn behind the mother, towards us).
    private static bool SetupPen(Pen pen, string prefabName, int levelA, int levelB)
    {
        var player = Player.m_localPlayer;
        var scene = ZNetScene.instance;
        if (player == null || scene == null)
        {
            SelfTest.Fail(pen.Test, "no world or no local player");
            return false;
        }
        var prefab = scene.GetPrefab(prefabName);
        var proto = prefab != null ? prefab.GetComponent<Procreation>() : null;
        if (proto == null)
        {
            SelfTest.Fail(pen.Test, prefab == null ? $"prefab {prefabName} not found" : $"{prefabName} has no Procreation");
            return false;
        }
        if (proto.m_offspring == null)
        {
            SelfTest.Fail(pen.Test, $"{prefabName} Procreation has no offspring");
            return false;
        }
        pen.Offspring = Utils.GetPrefabName(proto.m_offspring);
        var offspringPrefab = scene.GetPrefab(pen.Offspring);
        var egg = offspringPrefab != null ? offspringPrefab.GetComponent<ItemDrop>() : null;
        var baby = offspringPrefab != null ? offspringPrefab.GetComponent<Character>() : null;
        NotePrefab(pen.Test, prefabName, prefab, proto, egg);
        if (pen.Egg ? egg == null || baby != null : baby == null)
        {
            SelfTest.Fail(pen.Test, $"{prefabName} offspring {pen.Offspring} is not {(pen.Egg ? "an item (egg)" : "a creature")}");
            return false;
        }

        var cam = Utils.GetMainCamera();
        var forward = cam != null ? cam.transform.forward : player.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = Vector3.forward;
        }
        pen.Forward = forward.normalized;
        pen.Side = Vector3.Cross(Vector3.up, pen.Forward).normalized;
        pen.Center = Ground(player.transform.position + pen.Forward * 6f);

        // Offspring already in the world are not ours.
        MarkExisting(pen);

        // B (higher level) is always of the breeding species; A is the partner (same species unless it has a
        // separate partner species, then only B gives birth).
        var partnerName = proto.m_seperatePartner != null ? proto.m_seperatePartner.name : prefabName;
        pen.BothBreed = partnerName == prefabName;
        pen.B = Spawn(pen, prefabName, pen.Center, levelB);
        pen.A = Spawn(pen, partnerName, pen.Center + pen.Side * MateGap, levelA);
        if (pen.A == null || pen.B == null)
        {
            SelfTest.Fail(pen.Test, "could not spawn the two parents");
            return false;
        }
        if (!pen.BothBreed)
        {
            SelfTest.Note(pen.Test, $"{prefabName} breeds with {partnerName}: only {prefabName} gives birth in this test");
        }
        return true;
    }

    private static Character Mate(Pen pen, Character mother)
    {
        return ReferenceEquals(mother, pen.A) ? pen.B : pen.A;
    }

    private static Character Spawn(Pen pen, string prefabName, Vector3 position, int level)
    {
        var prefab = ZNetScene.instance.GetPrefab(prefabName);
        if (prefab == null)
        {
            return null;
        }
        var go = Object.Instantiate(prefab, position, Quaternion.LookRotation(pen.Forward));
        pen.Spawned.Add(go);
        var character = go.GetComponent<Character>();
        if (character == null)
        {
            return null;
        }
        if (level > 1)
        {
            character.SetLevel(level);
        }
        var monster = go.GetComponent<MonsterAI>();
        if (monster != null)
        {
            monster.MakeTame();
        }
        else
        {
            character.SetTamed(true);
        }
        var proc = go.GetComponent<Procreation>();
        if (proc != null)
        {
            // Me drive Procreate myself: no own ticks, never skip, due at once, no population limit, fixed ranges.
            proc.CancelInvoke(nameof(Procreation.Procreate));
            proc.m_pregnancyChance = -1f;
            proc.m_pregnancyDuration = -1f;
            proc.m_maxCreatures = 1000;
            proc.m_partnerCheckRange = PartnerRange;
            proc.m_totalCheckRange = PenRange;
        }
        return character;
    }

    // One conception + one birth, both through vanilla Procreate (so our patches run). Returns the baby level or
    // egg quality, -1 when something went wrong (reason in pen.Failures).
    private static int Breed(Pen pen, Character mother, Character mate, string label, Action<ZDO> before = null,
        bool keep = false)
    {
        var proc = mother.GetComponent<Procreation>();
        var zdo = mother.m_nview.GetZDO();
        var mateZdo = mate.m_nview.GetZDO();
        Place(mother, pen.Center, pen.Forward);
        Place(mate, pen.Center + pen.Side * MateGap, pen.Forward);
        Ready(mother);
        Ready(mate);
        zdo.Set(ZDOVars.s_pregnant, 0L);
        mateZdo.Set(ZDOVars.s_pregnant, 0L);
        zdo.Set(ZDOVars.s_lovePoints, Mathf.Max(0, proc.m_requiredLovePoints - 1));

        proc.Procreate();
        var stamp = zdo.GetLong(ZDOVars.s_pregnant, 0L);
        if (stamp == 0L)
        {
            pen.Failures.Add($"{label}: no conception (the game did not make the level {mother.GetLevel()} parent pregnant)");
            return -1;
        }
        if (!BirthRecord.TryRead(zdo, stamp, out var recorded))
        {
            pen.Failures.Add($"{label}: no partner note after conception");
        }
        else if (recorded != mate.GetLevel())
        {
            pen.Failures.Add($"{label}: partner note says level {recorded}, the partner is level {mate.GetLevel()}");
        }

        before?.Invoke(zdo);
        var own = mother.GetLevel();
        Ready(mother);
        proc.Procreate();
        if (zdo.GetLong(ZDOVars.s_pregnant, 0L) != 0L)
        {
            pen.Failures.Add($"{label}: no birth (still pregnant after the due call)");
            return -1;
        }
        if (mother.GetLevel() != own)
        {
            pen.Failures.Add($"{label}: parent level {mother.GetLevel()} after the birth, was {own} (not restored)");
        }
        // Cleared = stamp overwritten with 0 (a removed key would stay on other games' copies).
        var stored = BirthRecord.StoredStamp(zdo);
        if (stored == -1L)
        {
            pen.Failures.Add($"{label}: partner note removed instead of cleared (a removal never reaches other games)");
        }
        else if (stored != 0L)
        {
            pen.Failures.Add($"{label}: partner note still on the parent after the birth");
        }
        pen.Births++;
        return TakeOffspring(pen, mother.transform.position, label, keep);
    }

    // New offspring near the mother: read level (creature) or quality (egg), then destroy it (or keep it for the
    // screenshot; cleanup destroys it later). Extra new ones by the mother (other mod's twins) destroyed too: no leak.
    private static int TakeOffspring(Pen pen, Vector3 near, string label, bool keep)
    {
        var wanted = pen.Offspring + "(Clone)";
        var fresh = new List<GameObject>();
        GameObject found = null;
        var level = -1;
        var best = float.MaxValue;
        if (pen.Egg)
        {
            foreach (var item in ItemDrop.s_instances)
            {
                if (item == null || item.gameObject.name != wanted || !pen.Seen.Add(item.gameObject.GetInstanceID()))
                {
                    continue;
                }
                fresh.Add(item.gameObject);
                var d = Vector3.Distance(near, item.transform.position);
                if (d < best)
                {
                    best = d;
                    found = item.gameObject;
                    level = item.m_itemData.m_quality;
                }
            }
        }
        else
        {
            foreach (var ai in BaseAI.BaseAIInstances)
            {
                if (ai == null || ai.m_character == null || ai.gameObject.name != wanted
                    || !pen.Seen.Add(ai.gameObject.GetInstanceID()))
                {
                    continue;
                }
                fresh.Add(ai.gameObject);
                var d = Vector3.Distance(near, ai.transform.position);
                if (d < best)
                {
                    best = d;
                    found = ai.gameObject;
                    level = ai.m_character.GetLevel();
                }
            }
        }
        if (found == null)
        {
            pen.Failures.Add($"{label}: no new {pen.Offspring} after the birth");
            return -1;
        }
        if (fresh.Count > 1)
        {
            // Only extras by the mother (twins): far ones are not from this birth, me leave them.
            var removed = 0;
            foreach (var extra in fresh)
            {
                if (extra != null && !ReferenceEquals(extra, found)
                    && Vector3.Distance(near, extra.transform.position) <= PenRange)
                {
                    ZNetScene.instance.Destroy(extra);
                    removed++;
                }
            }
            SelfTest.Note(pen.Test, $"{label}: {fresh.Count} new {pen.Offspring} appeared at once; the nearest one was read, "
                                    + $"{removed} other(s) within {Inv(PenRange)} m removed");
        }
        if (keep && !pen.Egg)
        {
            pen.Keep.Add(found);
            pen.Spawned.Add(found);
        }
        else
        {
            ZNetScene.instance.Destroy(found);
        }
        return level;
    }

    private static void MarkExisting(Pen pen)
    {
        var wanted = pen.Offspring + "(Clone)";
        if (pen.Egg)
        {
            foreach (var item in ItemDrop.s_instances)
            {
                if (item != null && item.gameObject.name == wanted)
                {
                    pen.Seen.Add(item.gameObject.GetInstanceID());
                }
            }
            return;
        }
        foreach (var ai in BaseAI.BaseAIInstances)
        {
            if (ai != null && ai.gameObject.name == wanted)
            {
                pen.Seen.Add(ai.gameObject.GetInstanceID());
            }
        }
    }

    private static void Expect(Pen pen, int actual, int expected, string what)
    {
        if (actual >= 0 && actual != expected)
        {
            pen.Failures.Add($"{what}: got {actual}, expected {expected}");
        }
    }

    private static void Report(Pen pen, string summary)
    {
        if (pen.Failures.Count == 0 && pen.Births > 0)
        {
            SelfTest.Pass(pen.Test, $"{pen.Births} births follow the rule; {summary}");
            return;
        }
        var shown = pen.Failures.Count > 12 ? pen.Failures.GetRange(0, 12) : pen.Failures;
        SelfTest.Fail(pen.Test, $"{pen.Failures.Count} problem(s) in {pen.Births} births: {string.Join("; ", shown.ToArray())}; {summary}");
    }

    // Fed (not hungry) and calm (alerted animals do not breed).
    private static void Ready(Character c)
    {
        var tameable = c.GetComponent<Tameable>();
        if (tameable != null)
        {
            tameable.ResetFeedingTimer();
        }
        var ai = c.GetComponent<BaseAI>();
        if (ai != null)
        {
            ai.SetAlerted(false);
        }
    }

    private static void Place(Character c, Vector3 position, Vector3 forward)
    {
        if (c == null)
        {
            return;
        }
        var rotation = Quaternion.LookRotation(forward);
        var t = c.transform;
        t.position = position;
        t.rotation = rotation;
        var body = c.m_body;
        if (body != null)
        {
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = Vector3.zero;
        }
    }

    private static Vector3 Ground(Vector3 p)
    {
        var zones = ZoneSystem.instance;
        if (zones != null && zones.GetGroundHeight(p, out var height))
        {
            p.y = height;
        }
        return p;
    }

    private static void Cleanup(Pen pen)
    {
        Override = null;
        var scene = ZNetScene.instance;
        foreach (var go in pen.Spawned)
        {
            if (go == null)
            {
                continue;
            }
            if (scene != null)
            {
                scene.Destroy(go);
            }
            else
            {
                Object.Destroy(go);
            }
        }
        pen.Spawned.Clear();
        pen.Keep.Clear();
    }

    private static void NotePrefab(string test, string name, GameObject prefab, Procreation p, ItemDrop egg)
    {
        var tameable = prefab.GetComponent<Tameable>();
        var sb = new StringBuilder();
        sb.Append(name).Append(" Procreation (prefab values): offspring ").Append(Utils.GetPrefabName(p.m_offspring))
            .Append(", partner range ").Append(Inv(p.m_partnerCheckRange))
            .Append(", population range ").Append(Inv(p.m_totalCheckRange))
            .Append(", max creatures ").Append(p.m_maxCreatures)
            .Append(", pregnancy ").Append(Inv(p.m_pregnancyDuration)).Append(" s")
            .Append(", skip chance per tick ").Append(Inv(p.m_pregnancyChance))
            .Append(", love points ").Append(p.m_requiredLovePoints)
            .Append(", tick ").Append(Inv(p.m_updateInterval)).Append(" s")
            .Append(", min offspring level ").Append(p.m_minOffspringLevel)
            .Append(", separate partner ").Append(p.m_seperatePartner != null ? p.m_seperatePartner.name : "none")
            .Append(", no-partner offspring ").Append(p.m_noPartnerOffspring != null ? p.m_noPartnerOffspring.name : "none")
            .Append(", fed for ").Append(tameable != null ? tameable.m_fedDuration.ToString(CultureInfo.InvariantCulture) : "?").Append(" s");
        if (egg != null)
        {
            var grow = egg.GetComponent<EggGrow>();
            sb.Append("; egg max quality ").Append(egg.m_itemData.m_shared.m_maxQuality)
                .Append(", hatches into ").Append(grow != null && grow.m_grownPrefab != null ? grow.m_grownPrefab.name : "?")
                .Append(", hatchling tamed ").Append(grow != null ? grow.m_tamed.ToString() : "?");
        }
        SelfTest.Note(test, sb.ToString());
    }
#endif
}
