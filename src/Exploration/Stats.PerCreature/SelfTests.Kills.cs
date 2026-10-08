#if DEBUG
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.StatsPerCreatureMod;

// Kill tests. Player is in god mode: vanilla mark each creature it hurt as cheated, but the lifetime slot (the one this
// mod read) count the kill anyway (PlayerProfile.IncrementStatEnemy). Creatures are frozen (AI off) and leave no loot.
internal static partial class SelfTests
{
    // Vanilla names (TESTING.md "Checked names").
    private const string Greyling = "Greyling";
    private const string Greydwarf = "Greydwarf";
    private const string Deer = "Deer";
    private const string Boar = "Boar";
    private const string Wolf = "Wolf";
    private const string ClubItem = "Club";
    private const string BowItem = "Bow";
    private const string ArrowItem = "ArrowWood";
    private const string ClawsItem = "FistFenrirClaw";
    private const string StaffItem = "StaffFireball";
    private const string ButcherItem = "KnifeButcher";

    // Attack.DoMeleeAttack: weapon named "Unarmed" with no status effect put this in the hit; Character.RPC_Damage
    // then file the hit under unarmed (any other Unarmed-skill weapon = melee).
    private const int BareHandsMark = 99999;
    private const string BareHandsName = "Unarmed";

    private static ItemDrop.ItemData.SharedData Shared(string prefabName)
    {
        var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        return drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
    }

    // Game save kill numbers as floats. Me want whole number like the API say (rounded, junk = 0).
    private static int Whole(float value) => !(value > 0f) ? 0 : value >= int.MaxValue ? int.MaxValue : (int)(value + 0.5f);

    private static string CreatureName(string prefabName)
    {
        var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
        var character = prefab != null ? prefab.GetComponent<Character>() : null;
        return character != null ? character.m_name : null;
    }

    // ---------- percreature.data ----------

    // Values the README and TESTING.md call "(unverified)": read from the loaded game. NOTE lines give what was found.
    private static IEnumerator RunData()
    {
        var c = new Checks(DataName);
        var player = Player.m_localPlayer;
        if (player == null || ObjectDB.instance == null || ZNetScene.instance == null)
        {
            SelfTest.Fail(DataName, "no local player or no world");
            yield break;
        }

        var hands = player.m_unarmedWeapon;
        if (c.Check(hands != null && hands.m_itemData != null && hands.m_itemData.m_shared != null,
                "the player has a bare-hands weapon (Humanoid.m_unarmedWeapon)"))
        {
            var s = hands.m_itemData.m_shared;
            c.Note($"bare hands: prefab {hands.name}, name '{s.m_name}', skill {s.m_skillType}, attack status effect "
                   + (s.m_attackStatusEffect != null ? s.m_attackStatusEffect.name : "none"));
            c.Check(s.m_name == BareHandsName, $"the bare-hands weapon is named '{BareHandsName}' (found '{s.m_name}'): only then vanilla files its kills under unarmed");
            c.Check(s.m_skillType == Skills.SkillType.Unarmed, $"bare hands use the Unarmed skill (found {s.m_skillType})");
            c.Check(s.m_attackStatusEffect == null, "bare hands have no attack status effect (with one, vanilla files the kill under melee)");
        }

        var claws = Shared(ClawsItem);
        if (c.Check(claws != null, $"item {ClawsItem} exists"))
        {
            c.Note($"{ClawsItem}: name '{claws.m_name}', skill {claws.m_skillType}");
            c.Check(claws.m_skillType == Skills.SkillType.Unarmed && claws.m_name != BareHandsName,
                $"{ClawsItem} uses the Unarmed skill but is not named '{BareHandsName}' (skill {claws.m_skillType}, name '{claws.m_name}'): its kills are melee");
        }

        var staff = Shared(StaffItem);
        if (c.Check(staff != null, $"item {StaffItem} exists"))
        {
            c.Note($"{StaffItem}: skill {staff.m_skillType}");
            c.Check(staff.m_skillType == Skills.SkillType.ElementalMagic, $"{StaffItem} uses Elemental Magic (found {staff.m_skillType})");
        }

        var butcher = Shared(ButcherItem);
        if (c.Check(butcher != null, $"item {ButcherItem} exists"))
        {
            c.Note($"{ButcherItem}: tames only {butcher.m_tamedOnly}, skill {butcher.m_skillType}");
            c.Check(butcher.m_tamedOnly && butcher.m_skillType == Skills.SkillType.Knives,
                $"{ButcherItem} only hits tames and uses the Knives skill (tames only {butcher.m_tamedOnly}, skill {butcher.m_skillType})");
        }

        var club = Shared(ClubItem);
        var bow = Shared(BowItem);
        c.Check(club != null && club.m_skillType == Skills.SkillType.Clubs, $"{ClubItem} uses the Clubs skill");
        c.Check(bow != null && bow.m_skillType == Skills.SkillType.Bows && Shared(ArrowItem) != null,
            $"{BowItem} uses the Bows skill and {ArrowItem} exists");

        foreach (var name in new[] { Boar, Wolf })
        {
            var prefab = ZNetScene.instance.GetPrefab(name);
            c.Check(prefab != null && prefab.GetComponent<Tameable>() != null && prefab.GetComponent<MonsterAI>() != null,
                $"{name} can be tamed (has Tameable and MonsterAI)");
        }
        var boar = ZNetScene.instance.GetPrefab(Boar);
        var boarAi = boar != null ? boar.GetComponent<MonsterAI>() : null;
        if (boarAi != null && boarAi.m_consumeItems != null)
        {
            var foods = new List<string>();
            foreach (var food in boarAi.m_consumeItems)
            {
                if (food != null)
                {
                    foods.Add(food.name);
                }
            }
            var tame = boar.GetComponent<Tameable>();
            c.Note($"Boar eats: {string.Join(", ", foods.ToArray())}; taming time {(tame != null ? F(tame.m_tamingTime) : "?")} s, fed for "
                   + $"{(tame != null ? F(tame.m_fedDuration) : "?")} s");
            c.Check(foods.Contains("Raspberry") && foods.Contains("Blueberries"), "Boar eats Raspberry and Blueberries");
        }
        foreach (var name in new[] { Greyling, Greydwarf, Deer, Boar, Wolf })
        {
            c.Check(!string.IsNullOrEmpty(CreatureName(name)) && CreatureName(name)[0] == '$',
                $"creature {name} exists and its name is a translation key (found '{CreatureName(name)}')");
        }
        c.Report();
    }

    // ---------- percreature.kills ----------

    // One frozen creature, one killing hit of this skill, wait until gone.
    private static IEnumerator KillStep(Rig rig, Checks c, Vector3 spot, string prefab, Skills.SkillType skill, int statusHash,
        string what, int melee, int ranged, int magic, int unarmed, int other)
    {
        var holder = new Character[1];
        yield return SpawnNear(rig, prefab, spot, holder);
        var victim = holder[0];
        if (!c.Check(victim != null, $"{what}: could not spawn {prefab}"))
        {
            yield break;
        }
        var name = victim.m_name;
        var before = Tally.Of(name);
        Hit(victim, rig.P, skill, 1e7f, statusHash);
        var w = new Waiter();
        yield return WaitGone(victim, 4f, w);
        var after = Tally.Of(name);
        c.Check(w.Met && after.Grew(before, 1, melee, ranged, magic, unarmed, other),
            $"{what}: {prefab} must count +1 killed with melee +{melee}, ranged +{ranged}, magic +{magic}, unarmed +{unarmed}, other +{other} "
            + $"(died {w.Met}; before: {before}; after: {after})");
    }

    private static IEnumerator RunKills()
    {
        var rig = Rig.Begin(KillsName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(KillsName);
        try
        {
            var spot = Ground(rig.P.transform.position + OpenDirection(rig, false, 3f) * 3f);
            var greyling = CreatureName(Greyling);
            var greydwarf = CreatureName(Greydwarf);
            var deer = CreatureName(Deer);
            if (!c.Check(greyling != null && greydwarf != null && deer != null, "Greyling, Greydwarf and Deer prefabs exist"))
            {
                c.Report();
                yield break;
            }
            // Like character that never killed these three: their rows then say only what this test kill.
            rig.ForgetKills(greyling, greydwarf, deer);
            rig.SetTames(null);

            yield return KillStep(rig, c, spot, Greyling, Skills.SkillType.Clubs, 0, "club hit (Clubs skill)", 1, 0, 0, 0, 0);
            yield return KillStep(rig, c, spot, Deer, Skills.SkillType.Bows, 0, "arrow hit (Bows skill)", 0, 1, 0, 0, 0);
            yield return KillStep(rig, c, spot, Greyling, Skills.SkillType.ElementalMagic, 0, "elemental magic hit", 0, 0, 1, 0, 0);
            yield return KillStep(rig, c, spot, Greyling, Skills.SkillType.BloodMagic, 0, "blood magic hit", 0, 0, 1, 0, 0);
            yield return KillStep(rig, c, spot, Greyling, Skills.SkillType.Unarmed, BareHandsMark, "bare-hands hit (Unarmed skill with the bare-hands mark)", 0, 0, 0, 1, 0);
            yield return KillStep(rig, c, spot, Greyling, Skills.SkillType.Unarmed, 0, "claws hit (Unarmed skill, no bare-hands mark)", 1, 0, 0, 0, 0);
            yield return KillStep(rig, c, spot, Greyling, Skills.SkillType.None, 0, "hit with no weapon skill", 0, 0, 0, 0, 1);
            yield return KillStep(rig, c, spot, Greydwarf, Skills.SkillType.Clubs, 0, "Greydwarf, club only", 1, 0, 0, 0, 0);

            // Two weapon types on one creature: small club hit (it live, below full health), then arrow kill.
            var holder = new Character[1];
            yield return SpawnNear(rig, Greydwarf, spot, holder);
            var mixed = holder[0];
            if (c.Check(mixed != null, "mixed: could not spawn Greydwarf"))
            {
                var before = Tally.Of(greydwarf);
                Hit(mixed, rig.P, Skills.SkillType.Clubs, 2f);
                yield return null;
                yield return null;
                c.Check(mixed != null && mixed.GetHealth() > 0f && mixed.GetHealth() < mixed.GetMaxHealth(),
                    "mixed setup: the Greydwarf lives after the small club hit, below full health");
                if (mixed != null)
                {
                    Hit(mixed, rig.P, Skills.SkillType.Bows, 1e7f);
                }
                var w = new Waiter();
                yield return WaitGone(mixed, 4f, w);
                var after = Tally.Of(greydwarf);
                c.Check(w.Met && after.Grew(before, 1, 0, 0, 0, 0, 1),
                    $"club hit then arrow kill: Greydwarf +1 killed, other +1, melee and ranged unchanged (before: {before}; after: {after})");
            }

            // Kill that other game send (game that control the creature tell every attacker): same routed RPC.
            var beforeRpc = Tally.Of(greyling);
            Game.instance.RegisterKill(ZNet.GetUID(), greyling, 0, KillModifiers.Melee, 2, true);
            yield return null;
            var afterRpc = Tally.Of(greyling);
            c.Check(afterRpc.Grew(beforeRpc, 1, 1, 0, 0, 0, 0),
                $"a Greyling kill sent by the game's own kill message (RPC_RegisterKill, 2 attackers) counts +1 melee (before: {beforeRpc}; after: {afterRpc})");

            // Parts must add up, every creature.
            var g = Tally.Of(greyling);
            var d = Tally.Of(greydwarf);
            var r = Tally.Of(deer);
            c.Check(g.Total == 7 && g.Melee == 3 && g.Magic == 2 && g.Unarmed == 1 && g.Ranged == 0 && g.Other == 1, $"Greyling totals: 7 killed, melee 3, magic 2, unarmed 1, other 1 (found {g})");
            c.Check(d.Total == 2 && d.Melee == 1 && d.Other == 1 && d.Ranged == 0, $"Greydwarf totals: 2 killed, melee 1, other 1 (found {d})");
            c.Check(r.Total == 1 && r.Ranged == 1 && r.Other == 0, $"Deer totals: 1 killed, ranged 1 (found {r})");
            foreach (var t in new[] { g, d, r })
            {
                c.Check(t.Melee + t.Ranged + t.Magic + t.Unarmed + t.Other == t.Total, $"the parts add up to the total ({t})");
            }

            // API = the game's own profile numbers (what DudeWhatAreMyStats and other worlds read): nothing per world.
            var names = CreatureCounts.GetCreatureNames();
            var raw = rig.Kills[(int)KillModifiers.MixedAndTotal];
            var mismatch = new List<string>();
            var killed = 0;
            foreach (var pair in raw)
            {
                var has = pair.Value >= 0.5f;
                killed += has ? 1 : 0;
                if (CreatureCounts.GetKills(pair.Key) != Whole(pair.Value) || names.Contains(pair.Key) != has)
                {
                    mismatch.Add($"{pair.Key}: profile {F(pair.Value)}, API {CreatureCounts.GetKills(pair.Key)}, listed {names.Contains(pair.Key)}");
                }
            }
            c.Check(killed >= 3 && names.Count == killed && mismatch.Count == 0,
                $"the API lists exactly the creatures of the game's lifetime kill table, with the same totals ({killed} with kills in the profile, "
                + $"{names.Count} listed; differences: {string.Join(", ", mismatch.ToArray())})");
            foreach (var bucket in new[] { KillModifiers.Melee, KillModifiers.Ranged, KillModifiers.Magic, KillModifiers.Unarmed })
            {
                rig.Kills[(int)bucket].TryGetValue(greyling, out var value);
                c.Check(CreatureCounts.GetKills(greyling, bucket) == Whole(value),
                    $"GetKills(Greyling, {bucket}) = the game's own {bucket} number ({F(value)})");
            }

            // Page must show same numbers.
            var page = new Page();
            yield return ReadPage(page);
            if (c.Check(page.Ok, $"Player Statistics could not be read: {page.Problem}"))
            {
                var s = ParseSection(page.Entry);
                c.Note($"page: {s.Describe()}");
                c.Check(s.Found && s.Creatures.Count + s.Others.Count == killed, $"the page has one row per creature with kills ({killed})");
                c.Check(s.Row(Loc(greyling)) == $"{Loc(greyling)}: 7 killed (melee 3, magic 2, unarmed 1, other 1)"
                        && s.Row(Loc(greydwarf)) == $"{Loc(greydwarf)}: 2 killed (melee 1, other 1)"
                        && s.Row(Loc(deer)) == $"{Loc(deer)}: 1 killed (ranged 1)",
                    "page rows: 'Greyling: 7 killed (melee 3, magic 2, unarmed 1, other 1)', 'Greydwarf: 2 killed (melee 1, other 1)', "
                    + $"'Deer: 1 killed (ranged 1)' (found {s.Describe()})");
            }
        }
        finally
        {
            rig.End();
        }
        c.Report();
    }

    // ---------- real weapons ----------

    // Real player attack (Humanoid.StartAttack -> vanilla Attack -> Character.Damage): swing at the held creature until
    // the owner game removed it (dead) or time is up. Player and creature stay on their spots, eyes on its middle.
    private static IEnumerator SwingUntilGone(Rig rig, Character victim, Vector3 spot, float timeout, Waiter w, int[] swings)
    {
        yield return Until(() => victim == null, timeout, () =>
        {
            rig.HoldFacing(spot);
            if (victim == null)
            {
                return;
            }
            Place(victim, spot, rig.P.transform.position - spot);
            rig.Aim(rig.P.m_eye.position, victim.GetCenterPoint());
            if (!rig.P.InAttack() && rig.P.StartAttack(null, false))
            {
                swings[0]++;
            }
        }, w);
        yield return SwingOver(rig, spot);
    }

    // Me wait until swing animation over (hand no change weapon during swing).
    private static IEnumerator SwingOver(Rig rig, Vector3 facing)
    {
        var end = Time.time + 3f;
        while (rig.P.InAttack() && Time.time < end)
        {
            rig.HoldFacing(facing);
            yield return null;
        }
        yield return null;
    }

    // Where vanilla launch this weapon's projectile from (Attack.GetProjectileSpawnPoint), so me aim from there.
    private static Vector3 LaunchPoint(Player p, Attack attack)
    {
        var origin = p.transform;
        if (!string.IsNullOrEmpty(attack.m_attackOriginJoint) && p.GetVisual() != null)
        {
            var joint = Utils.FindChild(p.GetVisual().transform, attack.m_attackOriginJoint);
            if (joint != null)
            {
                origin = joint;
            }
        }
        var t = p.transform;
        return origin.position + t.up * attack.m_attackHeight + t.forward * attack.m_attackRange + t.right * attack.m_attackOffset;
    }

    // Real bow shots (hold attack until full draw, let go) at held creature until it gone or no shot left.
    private static IEnumerator ShootUntilGone(Rig rig, ItemDrop.ItemData bow, Character victim, Vector3 spot, int maxShots, Waiter w, int[] shots)
    {
        var attack = bow.m_shared.m_attack;
        System.Action hold = () =>
        {
            rig.HoldFacing(spot);
            if (victim == null)
            {
                return;
            }
            Place(victim, spot, rig.P.transform.position - spot);
            rig.Aim(LaunchPoint(rig.P, attack), victim.GetCenterPoint());
        };
        w.Met = false;
        for (var shot = 0; shot < maxShots && victim != null; shot++)
        {
            // Hands rest first (draw timer start only after one tick without the hold).
            rig.Drive(false);
            yield return Wait(0.4f, hold);
            var waited = 0f;
            while (rig.P.GetAttackDrawPercentage() < 1f && waited < 6f)
            {
                hold();
                rig.Drive(true);
                waited += Time.deltaTime;
                yield return null;
            }
            hold();
            rig.Drive(false);
            shots[0]++;
            yield return Until(() => victim == null, 3f, hold, w);
            if (w.Met)
            {
                break;
            }
        }
        rig.Drive(false);
        yield return SwingOver(rig, spot);
    }

    // Common start of a real-weapon test: as on a character that never killed or tamed this creature (its page row
    // then reads exactly this test's kills), and me drive the player.
    private static void StartDuel(Rig rig, string creatureName)
    {
        rig.ForgetKills(creatureName);
        rig.SetTames(null);
        rig.TakeControls();
    }

    // ---------- percreature.club ----------

    private static IEnumerator RunClub()
    {
        var rig = Rig.Begin(ClubName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(ClubName);
        try
        {
            var name = CreatureName(Greyling);
            StartDuel(rig, name);
            var spot = Ground(rig.P.transform.position + OpenDirection(rig, true, 1.3f) * 1.3f);
            var club = rig.Give(ClubItem);
            if (!c.Check(club != null && rig.Equip(club) && rig.P.GetCurrentWeapon() == club, $"a {ClubItem} added and equipped"))
            {
                c.Report();
                yield break;
            }
            var victim = rig.Spawn(Greyling, spot, true);
            if (!c.Check(victim != null && name != null, "could not spawn a Greyling"))
            {
                c.Report();
                yield break;
            }
            yield return Wait(0.6f, () => Place(victim, spot, rig.P.transform.position - spot));
            victim.SetHealth(1f);
            var before = Tally.Of(name);
            var w = new Waiter();
            var swings = new int[1];
            yield return SwingUntilGone(rig, victim, spot, 10f, w, swings);
            var after = Tally.Of(name);
            c.Note($"{swings[0]} swing(s), dead after {F(w.Took)} s");
            c.Check(w.Met, $"a real {ClubItem} swing must kill the Greyling (1 health) within 10 s ({swings[0]} swing(s) started)");
            c.Check(after.Grew(before, 1, 1, 0, 0, 0, 0), $"{ClubItem} kill: Greyling +1 killed, melee +1 (before: {before}; after: {after})");

            var page = new Page();
            yield return ReadPage(page);
            if (c.Check(page.Ok, $"Player Statistics could not be read: {page.Problem}"))
            {
                var s = ParseSection(page.Entry);
                c.Check(s.Row(Loc(name)) == $"{Loc(name)}: 1 killed (melee 1)",
                    $"the reopened page shows 'Greyling: 1 killed (melee 1)' (found {s.Describe()})");
            }
        }
        finally
        {
            rig.End();
        }
        c.Report();
    }

    // ---------- percreature.fists ----------

    private static IEnumerator RunFists()
    {
        var rig = Rig.Begin(FistsName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(FistsName);
        try
        {
            var name = CreatureName(Greyling);
            StartDuel(rig, name);
            var spot = Ground(rig.P.transform.position + OpenDirection(rig, true, 1.2f) * 1.2f);
            var w = new Waiter();
            var swings = new int[1];

            // Bare hands: me hold nothing.
            rig.EmptyHands();
            var hands = rig.P.GetCurrentWeapon();
            if (!c.Check(hands != null && rig.P.m_unarmedWeapon != null && hands == rig.P.m_unarmedWeapon.m_itemData,
                    "with empty hands the current weapon is the bare-hands weapon"))
            {
                c.Report();
                yield break;
            }
            c.Note($"bare-hands weapon name '{hands.m_shared.m_name}', skill {hands.m_shared.m_skillType}");
            var first = rig.Spawn(Greyling, spot, true);
            if (!c.Check(first != null && name != null, "could not spawn a Greyling"))
            {
                c.Report();
                yield break;
            }
            yield return Wait(0.6f, () => Place(first, spot, rig.P.transform.position - spot));
            first.SetHealth(1f);
            var before = Tally.Of(name);
            yield return SwingUntilGone(rig, first, spot, 10f, w, swings);
            var after = Tally.Of(name);
            c.Check(w.Met, $"a real bare-hands punch must kill the Greyling (1 health) within 10 s ({swings[0]} punch(es) started)");
            c.Check(after.Grew(before, 1, 0, 0, 0, 1, 0),
                $"bare-hands kill: Greyling +1 killed, unarmed +1 (before: {before}; after: {after}; if melee went up instead, the bare-hands item "
                + $"is not named '{BareHandsName}': it is '{hands.m_shared.m_name}', fix the README)");

            // Fenris claws: Unarmed skill but real weapon = melee.
            yield return Wait(0.5f, () => rig.HoldFacing(spot));
            var claws = rig.Give(ClawsItem);
            if (!c.Check(claws != null && rig.Equip(claws) && rig.P.GetCurrentWeapon() == claws, $"{ClawsItem} added and equipped"))
            {
                c.Report();
                yield break;
            }
            var second = rig.Spawn(Greyling, spot, true);
            if (!c.Check(second != null, "could not spawn a second Greyling"))
            {
                c.Report();
                yield break;
            }
            yield return Wait(0.6f, () => Place(second, spot, rig.P.transform.position - spot));
            second.SetHealth(1f);
            before = Tally.Of(name);
            swings[0] = 0;
            yield return SwingUntilGone(rig, second, spot, 10f, w, swings);
            after = Tally.Of(name);
            c.Check(w.Met, $"a real {ClawsItem} swipe must kill the Greyling (1 health) within 10 s ({swings[0]} swipe(s) started)");
            c.Check(after.Grew(before, 1, 1, 0, 0, 0, 0), $"{ClawsItem} kill: Greyling +1 killed, melee +1, unarmed unchanged (before: {before}; after: {after})");

            var page = new Page();
            yield return ReadPage(page);
            if (c.Check(page.Ok, $"Player Statistics could not be read: {page.Problem}"))
            {
                var s = ParseSection(page.Entry);
                c.Check(s.Row(Loc(name)) == $"{Loc(name)}: 2 killed (melee 1, unarmed 1)",
                    $"the reopened page shows 'Greyling: 2 killed (melee 1, unarmed 1)' (found {s.Describe()})");
            }
        }
        finally
        {
            rig.End();
        }
        c.Report();
    }

    // ---------- percreature.butcher ----------

    private static IEnumerator RunButcher()
    {
        var rig = Rig.Begin(ButcherName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(ButcherName);
        try
        {
            var name = CreatureName(Boar);
            StartDuel(rig, name);
            var spot = Ground(rig.P.transform.position + OpenDirection(rig, true, 1.2f) * 1.2f);
            var boar = rig.Spawn(Boar, spot, true);
            var tameable = boar != null ? boar.GetComponent<Tameable>() : null;
            if (!c.Check(boar != null && tameable != null && name != null, "could not spawn a Boar that can be tamed"))
            {
                c.Report();
                yield break;
            }
            yield return Wait(0.6f, () => Place(boar, spot, rig.P.transform.position - spot));
            tameable.Tame();
            yield return null;
            var tamesAfterTame = CreatureCounts.GetTames(name);
            c.Check(boar.IsTamed() && tamesAfterTame == 1, $"setup: the boar is tame and counted once (tame {boar.IsTamed()}, counted {tamesAfterTame})");

            var knife = rig.Give(ButcherItem);
            if (!c.Check(knife != null && rig.Equip(knife) && rig.P.GetCurrentWeapon() == knife, $"{ButcherItem} added and equipped"))
            {
                c.Report();
                yield break;
            }
            boar.SetHealth(1f);
            var before = Tally.Of(name);
            var w = new Waiter();
            var swings = new int[1];
            yield return SwingUntilGone(rig, boar, spot, 10f, w, swings);
            var after = Tally.Of(name);
            c.Check(w.Met, $"a real {ButcherItem} stab must kill the tame boar (1 health) within 10 s ({swings[0]} stab(s) started)");
            c.Check(after.Grew(before, 1, 1, 0, 0, 0, 0), $"{ButcherItem} kill of your tame: Boar +1 killed, melee +1 (before: {before}; after: {after})");
            c.Check(CreatureCounts.GetTames(name) == tamesAfterTame, $"the tame count is unchanged by the kill ({CreatureCounts.GetTames(name)}, was {tamesAfterTame})");

            var page = new Page();
            yield return ReadPage(page);
            if (c.Check(page.Ok, $"Player Statistics could not be read: {page.Problem}"))
            {
                var s = ParseSection(page.Entry);
                c.Check(s.Row(Loc(name)) == $"{Loc(name)}: 1 killed (melee 1), 1 tamed",
                    $"the reopened page shows 'Boar: 1 killed (melee 1), 1 tamed' (found {s.Describe()})");
            }
        }
        finally
        {
            rig.End();
        }
        c.Report();
    }

    // ---------- percreature.bow ----------

    private static IEnumerator RunBow()
    {
        var rig = Rig.Begin(BowName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(BowName);
        try
        {
            var name = CreatureName(Deer);
            StartDuel(rig, name);
            var spot = Ground(rig.P.transform.position + OpenDirection(rig, true, 6f) * 6f);
            var bow = rig.Give(BowItem);
            var arrows = rig.Give(ArrowItem, 20);
            rig.EmptyHands();
            if (!c.Check(bow != null && arrows != null && rig.Equip(bow) && rig.P.GetCurrentWeapon() == bow, $"{BowItem} equipped, {ArrowItem} in the inventory"))
            {
                c.Report();
                yield break;
            }
            var deer = rig.Spawn(Deer, spot, true);
            if (!c.Check(deer != null && name != null, "could not spawn a Deer"))
            {
                c.Report();
                yield break;
            }
            yield return Wait(0.6f, () => Place(deer, spot, rig.P.transform.position - spot));
            deer.SetHealth(1f);
            var before = Tally.Of(name);
            var w = new Waiter();
            var shots = new int[1];
            yield return ShootUntilGone(rig, bow, deer, spot, 4, w, shots);
            var after = Tally.Of(name);
            c.Note($"{shots[0]} shot(s)");
            c.Check(w.Met, $"a real full-draw {BowItem} shot with {ArrowItem} must kill the Deer (1 health, 6 m away) within 4 shots ({shots[0]} fired)");
            c.Check(after.Grew(before, 1, 0, 1, 0, 0, 0), $"{BowItem} kill: Deer +1 killed, ranged +1 (before: {before}; after: {after})");

            var page = new Page();
            yield return ReadPage(page);
            if (c.Check(page.Ok, $"Player Statistics could not be read: {page.Problem}"))
            {
                var s = ParseSection(page.Entry);
                c.Check(s.Row(Loc(name)) == $"{Loc(name)}: 1 killed (ranged 1)",
                    $"the reopened page shows 'Deer: 1 killed (ranged 1)' (found {s.Describe()})");
            }
        }
        finally
        {
            rig.End();
        }
        c.Report();
    }

    // ---------- percreature.mixed ----------

    private static IEnumerator RunMixed()
    {
        var rig = Rig.Begin(MixedName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(MixedName);
        try
        {
            var name = CreatureName(Greydwarf);
            StartDuel(rig, name);
            var dir = OpenDirection(rig, true, 1.3f, 6f);
            var near = Ground(rig.P.transform.position + dir * 1.3f);
            var far = Ground(rig.P.transform.position + dir * 6f);
            var club = rig.Give(ClubItem);
            var bow = rig.Give(BowItem);
            var arrows = rig.Give(ArrowItem, 20);
            if (!c.Check(club != null && bow != null && arrows != null && name != null, $"{ClubItem}, {BowItem} and {ArrowItem} added"))
            {
                c.Report();
                yield break;
            }
            var w = new Waiter();
            var swings = new int[1];
            var page = new Page();

            // First Greydwarf: club only.
            c.Check(rig.Equip(club) && rig.P.GetCurrentWeapon() == club, $"{ClubItem} equipped");
            var first = rig.Spawn(Greydwarf, near, true);
            if (!c.Check(first != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return Wait(0.6f, () => Place(first, near, rig.P.transform.position - near));
            first.SetHealth(1f);
            var before = Tally.Of(name);
            yield return SwingUntilGone(rig, first, near, 10f, w, swings);
            var after = Tally.Of(name);
            c.Check(w.Met && after.Grew(before, 1, 1, 0, 0, 0, 0),
                $"{ClubItem} only: Greydwarf +1 killed, melee +1 (died {w.Met} after {swings[0]} swing(s); before: {before}; after: {after})");
            yield return ReadPage(page);
            if (c.Check(page.Ok, $"Player Statistics could not be read: {page.Problem}"))
            {
                var s = ParseSection(page.Entry);
                c.Check(s.Row(Loc(name)) == $"{Loc(name)}: 1 killed (melee 1)",
                    $"after the club kill the page shows 'Greydwarf: 1 killed (melee 1)' (found {s.Describe()})");
            }

            // Second Greydwarf: one real club hit (full health: it live), then bow.
            yield return Wait(0.5f, () => rig.HoldFacing(near));
            var second = rig.Spawn(Greydwarf, near, true);
            if (!c.Check(second != null, "could not spawn a second Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return Wait(0.6f, () => Place(second, near, rig.P.transform.position - near));
            var full = second.GetMaxHealth();
            swings[0] = 0;
            yield return Until(() => second == null || second.GetHealth() < full, 10f, () =>
            {
                rig.HoldFacing(near);
                if (second == null)
                {
                    return;
                }
                Place(second, near, rig.P.transform.position - near);
                rig.Aim(rig.P.m_eye.position, second.GetCenterPoint());
                if (!rig.P.InAttack() && rig.P.StartAttack(null, false))
                {
                    swings[0]++;
                }
            }, w);
            if (!c.Check(w.Met && second != null && second.GetHealth() > 0f,
                    $"one real {ClubItem} hit must hurt the full-health Greydwarf without killing it ({swings[0]} swing(s) started, "
                    + $"health {(second != null ? F(second.GetHealth()) : "gone")} of {F(full)})"))
            {
                c.Report();
                yield break;
            }
            // Swing over, then bow from 6 m.
            yield return SwingOver(rig, near);
            Place(second, far, rig.P.transform.position - far);
            rig.EmptyHands();
            c.Check(rig.Equip(bow) && rig.P.GetCurrentWeapon() == bow, $"{BowItem} equipped");
            second.SetHealth(1f);
            before = Tally.Of(name);
            var shots = new int[1];
            yield return ShootUntilGone(rig, bow, second, far, 4, w, shots);
            after = Tally.Of(name);
            c.Check(w.Met, $"a real {BowItem} shot must finish the Greydwarf (1 health, 6 m away) within 4 shots ({shots[0]} fired)");
            c.Check(after.Grew(before, 1, 0, 0, 0, 0, 1),
                $"club hit then bow kill: Greydwarf +1 killed, other +1, melee and ranged unchanged (before: {before}; after: {after})");
            c.Check(after.Melee + after.Ranged + after.Magic + after.Unarmed + after.Other == after.Total, $"the parts add up to the total ({after})");

            yield return ReadPage(page);
            if (c.Check(page.Ok, $"Player Statistics could not be read: {page.Problem}"))
            {
                var s = ParseSection(page.Entry);
                c.Check(s.Row(Loc(name)) == $"{Loc(name)}: 2 killed (melee 1, other 1)",
                    $"the reopened page shows 'Greydwarf: 2 killed (melee 1, other 1)' (found {s.Describe()})");
                c.Check(s.Found && s.Raw.IndexOf("mixed", System.StringComparison.OrdinalIgnoreCase) < 0, "the page never says 'mixed'");
            }
        }
        finally
        {
            rig.End();
        }
        c.Report();
    }
}
#endif
