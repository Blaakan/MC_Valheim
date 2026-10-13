#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SwimmingDiveMod;

// Debug build only. Live tests that do the real thing where an older test used a shortcut:
//   dive.tarpit        T18: a real tar pit of the world (nearest location whose name holds "TarPit"), the swimmer put
//                      where its tar is deepest: Crouch does not dive, the swimmer floats in the tar as without
//                      Crouch, stays tarred, the camera stays out of the view under water
//   dive.serpent-hunt  T31: a Serpent with its AI on, told to hunt players (the state raid creatures have) and seen
//                      to target the swimmer, then the diver 6 m down for 15 s: no bite, and the Serpent never goes
//                      deeper than its own swimming depth plus the waves
//   dive.death         T20: a real death by drowning 6 m down (god mode off, 1 health, no stamina): the dive ends, the
//                      dead camera stays where it was and keeps the tint while it is under water, the tombstone floats
//                      up, view normal while there is no player and after the respawn. The player's items are taken
//                      out before (one Club goes into the tombstone) and put back after; skills, statistics, death
//                      point, map pin, god mode and place are put back too. Registered last: it leaves a new player
//                      object behind
internal static partial class SelfTests
{
    private const string TarPitName = "dive.tarpit";
    private const string SerpentHuntName = "dive.serpent-hunt";
    private const string DeathName = "dive.death";

    // Item that goes into the tombstone of the death test (the dive.exits test uses the same name).
    private const string GraveFiller = "Club";

    // ---------- dive.tarpit (T18) ----------

    // Nearest tar pit of the world. Location names are game data, not code: looked up by name at run time (unverified
    // name "TarPit": none found = the test fails and says so).
    private static bool FindTarPit(Vector3 origin, out Vector3 spot, out string name)
    {
        spot = Vector3.zero;
        name = "";
        var best = float.MaxValue;
        foreach (var instance in ZoneSystem.instance.m_locationInstances.Values)
        {
            var location = instance.m_location;
            if (location == null || string.IsNullOrEmpty(location.m_prefabName)
                || location.m_prefabName.IndexOf("TarPit", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            var d = Vector3.Distance(Flat(origin), Flat(instance.m_position));
            if (d < best)
            {
                best = d;
                spot = instance.m_position;
                name = location.m_prefabName;
            }
        }
        return best < float.MaxValue;
    }

    private static IEnumerator RunTarPit()
    {
        yield return Settle();
        var s = Begin(TarPitName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        var tap = s.Tap;
        try
        {
            ApplyDefaults();
            if (!c.Check(FindTarPit(rig.Origin, out var pit, out var pitName),
                    "no tar pit in this world (no location whose name holds \"TarPit\" in ZoneSystem.m_locationInstances): not tested"))
            {
                c.Report();
                yield break;
            }
            c.Note($"tar pit '{pitName}' at {F(pit)}, {F(Vector3.Distance(Flat(pit), Flat(rig.Origin)))} m from the start");
            var trip = new Box();
            rig.Travelled = true;
            yield return rig.Travel(pit + Vector3.up * 3f, Quaternion.identity, trip, TravelTimeout);
            if (!c.Check(trip.Ok, "travel to the tar pit: " + trip.Detail))
            {
                c.Report();
                yield break;
            }
            rig.TakeControls();
            rig.Undo("tar on the player", () => player.m_seman.RemoveStatusEffect(SEMan.s_statusEffectTared, true));

            // ---- the tar of the pit, and the spot where it is deepest ----
            LiquidVolume tar = null;
            var t0 = Time.time;
            while (tar == null && Time.time - t0 < 6f)
            {
                var nearest = 60f;
                foreach (var volume in UnityEngine.Object.FindObjectsByType<LiquidVolume>(FindObjectsSortMode.None))
                {
                    if (volume == null || volume.m_liquidType != LiquidType.Tar)
                    {
                        continue;
                    }
                    var d = Vector3.Distance(Flat(volume.transform.position), Flat(pit));
                    if (d < nearest)
                    {
                        nearest = d;
                        tar = volume;
                    }
                }
                if (tar == null)
                {
                    yield return new WaitForSeconds(0.5f);
                }
            }
            if (!c.Check(tar != null, "the tar pit has a tar volume within 60 m of its centre (LiquidVolume, liquid type Tar)"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(1f);
            var width = tar.m_width;
            var deepest = 0f;
            var at = tar.transform.position;
            for (var gx = 0; gx <= width; gx++)
            {
                for (var gy = 0; gy <= width; gy++)
                {
                    var depth = tar.GetDepth(gx, gy);
                    if (depth > deepest)
                    {
                        deepest = depth;
                        at = tar.transform.position + new Vector3((gx - width * 0.5f) * tar.m_scale, 0f, (gy - width * 0.5f) * tar.m_scale);
                    }
                }
            }
            var need = Mathf.Max(0f, player.m_swimDepth - 0.4f) + 0.3f;
            if (!c.Check(deepest >= need,
                    $"the tar is deep enough to swim in somewhere (deepest {F(deepest)} m, a swimmer needs {F(need)} m)"))
            {
                c.Report();
                yield break;
            }
            var surface = tar.GetSurface(at);
            MovePlayer(player, new Vector3(at.x, surface + 0.3f, at.z));
            t0 = Time.time;
            while (Time.time - t0 < 6f && !(player.IsSwimming() && player.InLiquid() && !player.InWater()))
            {
                yield return FixedTick;
            }
            yield return Ticks(50);
            var inTar = player.IsSwimming() && player.InLiquid() && !player.InWater() && player.m_tarLevel > player.m_waterLevel;
            if (!c.Check(inTar,
                    $"swimming in the tar where it is {F(deepest)} m deep (swimming {player.IsSwimming()}, tar level {F(player.m_tarLevel)}, "
                    + $"water level {F(player.m_waterLevel)}, feet {F(player.transform.position.y)})"))
            {
                c.Report();
                yield break;
            }

            // ---- no Crouch, 1.5 s: how the normal game floats the swimmer in tar ----
            var plainLow = float.PositiveInfinity;
            for (var i = 0; i < 75; i++)
            {
                rig.Drive(Vector3.zero);
                yield return FixedTick;
                plainLow = Mathf.Min(plainLow, player.transform.position.y - player.GetLiquidLevel());
            }

            // ---- Crouch held 2 s (the dive key and the game's own crouch input): no dive, the same floating ----
            tap.Clear();
            var cam = GameCamera.instance;
            var dived = false;
            var gravityOff = false;
            var viewOn = false;
            var leftTar = false;
            var crouchLow = float.PositiveInfinity;
            DiveInput.TestUp = false;
            DiveInput.TestDown = true;
            for (var i = 0; i < 100; i++)
            {
                rig.Drive(Vector3.zero, i == 0, false, false);
                yield return FixedTick;
                dived |= DiveState.Diving;
                gravityOff |= DiveState.GravityOff || !player.m_body.useGravity;
                viewOn |= UnderwaterView.Active || UnderwaterView.FogApplied || DiveCamera.Lifted;
                leftTar |= player.InWater() || !player.InLiquid();
                crouchLow = Mathf.Min(crouchLow, player.transform.position.y - player.GetLiquidLevel());
            }
            DiveInput.TestDown = false;
            rig.Drive(Vector3.zero);
            var tarred = player.m_seman.HaveStatusEffect(SEMan.s_statusEffectTared);
            var camPos = cam.transform.position;
            c.Check(!dived && !gravityOff && tap.Count("Dive started (") == 0,
                $"Crouch held 2 s in a real tar pit: no dive (dive {dived}, gravity turned off {gravityOff}, \"Dive started\" lines "
                + $"{tap.Count("Dive started (")})");
            c.Check(!leftTar && player.IsSwimming() && crouchLow > plainLow - 0.5f,
                $"Crouch held in tar: the swimmer floats as without Crouch (feet at the lowest {F(-crouchLow)} m under the tar "
                + $"surface with Crouch, {F(-plainLow)} m without; left the tar {leftTar})");
            c.Check(tarred, "in the tar pit the swimmer is tarred, as in the normal game (status effect Tared)");
            c.Check(!viewOn && camPos.y > Floating.GetLiquidLevel(camPos),
                $"in tar: the camera stays above the tar, no under-water view (view on {viewOn})");
            player.m_seman.RemoveStatusEffect(SEMan.s_statusEffectTared, true);

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.serpent-hunt (T31) ----------

    private static IEnumerator RunSerpentHunt()
    {
        yield return Settle();
        var s = Begin(SerpentHuntName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            ApplyDefaults();
            CalmWind(rig);
            // Serpent: prefab name from the 1.0.16 runtime dump (TESTING.md, "Names checked").
            var prefab = ZNetScene.instance.GetPrefab("Serpent");
            if (!c.Check(prefab != null && prefab.GetComponent<Character>() != null && prefab.GetComponent<MonsterAI>() != null,
                    "the game has a Serpent creature with a MonsterAI"))
            {
                c.Report();
                yield break;
            }
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            rig.TimeOfDay(0f);
            rig.SaveHealth();
            player.SetHealth(player.GetMaxHealth());
            var here = player.transform.position;
            GameObject go = UnityEngine.Object.Instantiate(prefab, new Vector3(here.x + 14f, player.GetLiquidLevel() - 0.5f, here.z),
                Quaternion.identity);
            rig.Undo("serpent", () =>
            {
                if (go != null && ZNetScene.instance != null)
                {
                    ZNetScene.instance.Destroy(go);
                }
            });
            var serpent = go.GetComponent<Character>();
            var ai = go.GetComponent<MonsterAI>();
            // Hunt players: what the game sets on raid creatures (BaseAI.SetHuntPlayer). The AI itself picks the target.
            ai.SetHuntPlayer(true);

            // ---- the Serpent takes the swimmer as its target (15 s at most) ----
            var t0 = Time.time;
            while (Time.time - t0 < 15f && go != null && !ReferenceEquals(ai.m_targetCreature, player))
            {
                yield return FixedTick;
            }
            if (!c.Check(go != null && ReferenceEquals(ai.m_targetCreature, player),
                    $"the Serpent hunts the swimmer (its AI took the player as target {F(Time.time - t0)} s after it was told to hunt "
                    + "players); without that the dive would prove nothing"))
            {
                c.Report();
                yield break;
            }
            var swimDepth = serpent.m_swimDepth;

            // ---- 6 m down for 15 s ----
            var last = player.m_lastHit;
            yield return DiveTo(player, 6f, 8f, box);
            c.Check(box.Ok, "dive to 6 m: " + box.Detail);
            var bites = 0;
            var ticks = 0;
            var hunting = 0;
            var closest = float.MaxValue;
            var serpentDeepest = float.NegativeInfinity;
            var shallowest = float.PositiveInfinity;
            t0 = Time.time;
            while (Time.time - t0 < 15f && go != null && serpent != null)
            {
                yield return FixedTick;
                if (go == null || serpent == null)
                {
                    break;
                }
                ticks++;
                if (NewHitFrom(player, ref last, serpent))
                {
                    bites++;
                }
                if (ReferenceEquals(ai.m_targetCreature, player))
                {
                    hunting++;
                }
                var at = serpent.transform.position;
                var level = Floating.GetLiquidLevel(at, 1f, LiquidType.Water);
                if (level > -9000f)
                {
                    serpentDeepest = Mathf.Max(serpentDeepest, level - at.y);
                }
                closest = Mathf.Min(closest, Vector3.Distance(at, player.GetCenterPoint()));
                shallowest = Mathf.Min(shallowest, Under(player));
            }
            var facts = $"{bites} bite(s) in {F(Time.time - t0)} s, the diver at least {F(shallowest)} m under the rest height, the "
                        + $"Serpent came within {F(closest)} m, went at most {F(serpentDeepest)} m under the surface (its swimming "
                        + $"depth is {F(swimDepth)} m) and had the diver as target {hunting} of {ticks} ticks";
            c.Note("diver 6 m under a hunting Serpent: " + facts);
            c.Check(ticks > 500 && bites == 0 && DiveState.Deep && shallowest >= 5.5f,
                $"a Serpent that hunts the swimmer cannot bite the diver 6 m down ({facts})");
            c.Check(serpentDeepest <= swimDepth + 1.5f,
                $"the Serpent stays near the surface: never deeper than its swimming depth plus 1.5 m of waves ({facts})");
            yield return Surface(player, 10f, box);
            if (go != null)
            {
                ZNetScene.instance.Destroy(go);
                go = null;
            }
            player.SetHealth(player.GetMaxHealth());

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.death (T20) ----------

    // What a real death changes on the test character, kept to put it back on the player that comes back.
    private sealed class DeathSave
    {
        private readonly List<ItemDrop.ItemData> _items = new List<ItemDrop.ItemData>();
        private readonly List<ItemDrop.ItemData> _equipped = new List<ItemDrop.ItemData>();
        private readonly List<KeyValuePair<Skills.SkillType, Vector2>> _skills = new List<KeyValuePair<Skills.SkillType, Vector2>>();
        private readonly List<Player.Food> _foods = new List<Player.Food>();
        private float _health;
        private float _timeSinceDeath;
        private bool _softDeath;
        private bool _deathTutorial;
        private float _deaths;
        private float _drowned;
        private float _daysSurvived;
        private bool _hadDeathPoint;
        private Vector3 _deathPoint;

        internal bool ItemsOut;   // items taken out of the inventory and not put back yet
        internal bool GodOff;     // god mode turned off by the test

        internal static DeathSave Take(Player player, PlayerProfile profile)
        {
            var save = new DeathSave();
            foreach (var item in player.GetInventory().GetAllItems())
            {
                save._items.Add(item);
                if (item.m_equipped)
                {
                    save._equipped.Add(item);
                }
            }
            foreach (var pair in player.GetSkills().m_skillData)
            {
                save._skills.Add(new KeyValuePair<Skills.SkillType, Vector2>(pair.Key,
                    new Vector2(pair.Value.m_level, pair.Value.m_accumulator)));
            }
            save._foods.AddRange(player.m_foods);
            save._health = player.GetHealth();
            save._timeSinceDeath = player.m_timeSinceDeath;
            save._softDeath = player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectSoftDeath);
            save._deathTutorial = player.m_shownTutorials.Contains("death");
            save._deaths = profile.GetStat(PlayerStatType.Deaths);
            save._drowned = profile.GetStat(PlayerStatType.DeathByDrowning);
            save._daysSurvived = profile.GetStat(PlayerStatType.ConsecutiveDaysSurvived);
            save._hadDeathPoint = profile.HaveDeathPoint();
            save._deathPoint = profile.GetDeathPoint();
            return save;
        }

        // Inventory emptied (items live on in my list): nothing of the player's goes into the tombstone.
        internal void TakeItemsOut(Player player)
        {
            player.UnequipAllItems();
            var inventory = player.GetInventory();
            inventory.m_inventory.Clear();
            inventory.Changed();
            ItemsOut = true;
        }

        // On the player that is there now (the new one after a death, the old one when it never died). Safe to call
        // twice.
        internal void PutBack(Player player, PlayerProfile profile)
        {
            if (ItemsOut)
            {
                player.UnequipAllItems();
                var inventory = player.GetInventory();
                inventory.m_inventory.Clear();
                inventory.m_inventory.AddRange(_items);
                inventory.Changed();
                // Player that never died is still in the water, where the game refuses to equip unless something
                // solid is touched: touch faked for these calls (the field IsOnGround reads).
                if (player.IsSwimming())
                {
                    player.m_lastGroundTouch = 0f;
                }
                foreach (var item in _equipped)
                {
                    player.EquipItem(item, false);
                }
                ItemsOut = false;
            }
            var skills = player.GetSkills();
            var gone = new List<Skills.SkillType>(skills.m_skillData.Keys);
            foreach (var pair in _skills)
            {
                var skill = skills.GetSkill(pair.Key);
                skill.m_level = pair.Value.x;
                skill.m_accumulator = pair.Value.y;
                gone.Remove(pair.Key);
            }
            foreach (var type in gone)
            {
                skills.m_skillData.Remove(type);
            }
            player.m_foods.Clear();
            player.m_foods.AddRange(_foods);
            player.m_timeSinceDeath = _timeSinceDeath;
            if (!_softDeath)
            {
                player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectSoftDeath, true);
            }
            if (!_deathTutorial)
            {
                player.m_shownTutorials.Remove("death");
            }
            if (!player.IsDead())
            {
                player.SetHealth(Mathf.Min(_health, player.GetMaxHealth()));
            }
            if (GodOff)
            {
                player.SetGodMode(true);
                GodOff = false;
            }
            profile.SetStat(PlayerStatType.Deaths, _deaths);
            profile.SetStat(PlayerStatType.DeathByDrowning, _drowned);
            profile.SetStat(PlayerStatType.ConsecutiveDaysSurvived, _daysSurvived);
            if (ZNet.instance != null)
            {
                var data = profile.GetWorldData(ZNet.instance.GetWorldUID());
                data.m_haveDeathPoint = _hadDeathPoint;
                data.m_deathPoint = _deathPoint;
            }
        }
    }

    // Fog on screen right now = the under-water tint. Me read RenderSettings, not only my own flags (flag true with
    // other fog on screen would be a tick for nothing). Same compare as weather change check of dive.view. Call at
    // end of frame, default view settings forced (ApplyDefaults).
    private static bool TintOnScreen()
    {
        var want = Mathf.Max(UnderwaterView.BaseDensity, UnderwaterView.Density(RenderSettings.fogMode, Visuals.DefaultVisibility));
        return Near(RenderSettings.fogDensity, want, 0.0001f)
               && Near(RenderSettings.fogColor, UnderwaterView.Tint(UnderwaterView.BaseColor, Visuals.DefaultFogColor, UnderwaterView.LastDepth));
    }

    private static IEnumerator RunDeath()
    {
        yield return Settle();
        var s = Begin(DeathName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var old = s.P;
        var tap = s.Tap;
        var profile = Game.instance.GetPlayerProfile();
        DeathSave save = null;
        var died = false;
        // Wind kept by hand: after a death the rig is not restored (its player is gone).
        var env = EnvMan.instance;
        var windOn = env.m_debugWind;
        var windAngle = env.m_debugWindAngle;
        var windIntensity = env.m_debugWindIntensity;
        try
        {
            ApplyDefaults();
            env.SetDebugWind(0f, 0.05f);
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, false, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            if (!c.Check(old.m_tombstone != null && Minimap.instance != null && ObjectDB.instance != null
                         && ObjectDB.instance.GetItemPrefab(GraveFiller) != null,
                    $"the player has a tombstone prefab, the game a map and the item {GraveFiller}"))
            {
                c.Report();
                yield break;
            }
            var cam = GameCamera.instance;
            save = DeathSave.Take(old, profile);
            var stonesBefore = new HashSet<TombStone>(UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None));
            var pinsBefore = new HashSet<Minimap.PinData>(Minimap.instance.m_pins);

            var box = new Box();
            yield return DiveTo(old, 6f, 8f, box);
            yield return Ticks(40);
            yield return ViewSettled(2);
            var camPos = cam.transform.position;
            var surface = Floating.GetLiquidLevel(camPos, 1f, LiquidType.Water);
            if (!c.Check(box.Ok && DiveState.Deep && UnderwaterView.Active && UnderwaterView.FogApplied && TintOnScreen()
                         && camPos.y < surface - 0.3f,
                    $"before the death: diving ({box.Detail}), camera {F(surface - camPos.y)} m under water, tint on "
                    + $"{UnderwaterView.FogApplied} and on screen {TintOnScreen()} (fog colour {F(RenderSettings.fogColor)}, density "
                    + $"{F(RenderSettings.fogDensity)})"))
            {
                c.Report();
                yield break;
            }
            // Fog as on screen under water: after the respawn it must not be this any more.
            var tintUnder = RenderSettings.fogColor;
            var densityUnder = RenderSettings.fogDensity;

            // ---- a real death by drowning: god mode off, 1 health, no stamina (out of breath) ----
            // The player's own items out of harm's way first; one Club so the game makes a tombstone.
            save.TakeItemsOut(old);
            var filler = old.GetInventory().AddItem(GraveFiller, 1, 1, 0, 0L, "", false);
            c.Check(filler != null, $"one {GraveFiller} in the inventory for the tombstone");
            tap.Clear();
            old.SetGodMode(false);
            save.GodOff = true;
            old.SetHealth(1f);
            var camAlive = cam.transform.position;
            var t0 = Time.time;
            while (!old.IsDead() && Time.time - t0 < 4f)
            {
                old.m_stamina = 0f;
                camAlive = cam.transform.position;
                yield return FixedTick;
            }
            died = old.IsDead();
            var drowned = old.m_lastHit != null && old.m_lastHit.m_hitType == HitData.HitType.Drowning;
            if (!c.Check(died && drowned,
                    $"the player drowned under water (dead {died} after {F(Time.time - t0)} s, last hit "
                    + $"{(old.m_lastHit != null ? old.m_lastHit.m_hitType.ToString() : "none")})"))
            {
                c.Report();
                yield break;
            }
            var deathPos = old.transform.position;
            var deathLevel = Floating.GetLiquidLevel(deathPos, 1f, LiquidType.Water);
            yield return Ticks(3);
            c.Check(!DiveState.Diving && tap.Count("Dive ended: Dead.") == 1,
                $"death under water ({F(deathLevel - deathPos.y)} m under the surface): the dive ends (\"Dive ended: Dead.\" lines "
                + $"{tap.Count("Dive ended: Dead.")})");
            yield return ViewSettled();
            var camDead = cam.transform.position;
            c.Check(Vector3.Distance(camDead, camAlive) < 0.3f,
                $"at the death the camera stays where it was ({F(Vector3.Distance(camDead, camAlive))} m from its last place alive)");
            TombStone stone = null;
            var nearest = 20f;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None))
            {
                if (candidate == null || stonesBefore.Contains(candidate))
                {
                    continue;
                }
                var d = Vector3.Distance(candidate.transform.position, deathPos);
                if (d < nearest)
                {
                    nearest = d;
                    stone = candidate;
                }
            }
            var stoneStart = stone != null
                ? Floating.GetLiquidLevel(stone.transform.position, 1f, LiquidType.Water) - stone.transform.position.y
                : 0f;

            // ---- dead (the game waits 10 s before the respawn): 7 s watched ----
            var frames = 0;
            var underFrames = 0;
            var underNoTint = 0;
            var aboveWithTint = 0;
            var maxDrift = 0f;
            var stoneBest = float.PositiveInfinity;
            var watchEnd = Time.time + 7f;
            while (Time.time < watchEnd && old != null && ReferenceEquals(Player.m_localPlayer, old))
            {
                yield return FrameEnd;
                frames++;
                var pos = cam.transform.position;
                maxDrift = Mathf.Max(maxDrift, Vector3.Distance(pos, camDead));
                var level = Floating.GetLiquidLevel(pos, 1f, LiquidType.Water);
                var depth = level > -9000f ? level - pos.y : -1f;
                if (depth > UnderwaterView.ViewMargin + 0.05f)
                {
                    underFrames++;
                    // Flags and the fog values really on screen.
                    if (!(UnderwaterView.Active && UnderwaterView.FogApplied) || !TintOnScreen())
                    {
                        underNoTint++;
                    }
                }
                else if (depth < -0.05f && UnderwaterView.FogApplied)
                {
                    aboveWithTint++;
                }
                if (stone != null)
                {
                    var stonePos = stone.transform.position;
                    var stoneLevel = Floating.GetLiquidLevel(stonePos, 1f, LiquidType.Water);
                    if (stoneLevel > -9000f)
                    {
                        stoneBest = Mathf.Min(stoneBest, stoneLevel - stonePos.y);
                    }
                }
                yield return null;
            }
            c.Check(frames > 20 && maxDrift < 0.05f,
                $"while dead the camera stays where it was (moved at most {F(maxDrift)} m in {frames} frames)");
            c.Check(underFrames > 0 && underNoTint == 0 && aboveWithTint == 0,
                $"while dead the view keeps the under-water tint as long as the camera is under water ({underFrames} of {frames} "
                + $"frames under water, {underNoTint} of them without the tint; {aboveWithTint} frames above water with the tint)");
            c.Check(stone != null && stoneStart > 2f && stoneBest < 1.5f,
                $"the tombstone floats up (found {stone != null}, {F(stoneStart)} m under the surface at first, {F(stoneBest)} m at "
                + "its highest in 7 s)");
            // Tombstone (one Club inside) and the map's death pin away again.
            if (stone != null && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(stone.gameObject);
            }
            foreach (var pin in new List<Minimap.PinData>(Minimap.instance.m_pins))
            {
                if (pin.m_type == Minimap.PinType.Death && !pinsBefore.Contains(pin))
                {
                    Minimap.instance.RemovePin(pin);
                }
            }

            // ---- no player object, then the respawn ----
            var nullFrames = 0;
            var noPlayerFrames = 0;
            var noPlayerDirty = 0;
            var r0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - r0 < 45f && (Player.m_localPlayer == null || ReferenceEquals(Player.m_localPlayer, old)))
            {
                yield return FrameEnd;
                // Second frame without a player on: the first one ends before the view saw the player go.
                nullFrames = Player.m_localPlayer == null ? nullFrames + 1 : 0;
                if (nullFrames >= 2)
                {
                    noPlayerFrames++;
                    if (UnderwaterView.Active || UnderwaterView.FogApplied || UnderwaterView.FlippedCount > 0)
                    {
                        noPlayerDirty++;
                    }
                }
                yield return null;
            }
            var now = Player.m_localPlayer;
            if (!c.Check(now != null && !ReferenceEquals(now, old),
                    $"the player respawned ({F(Time.realtimeSinceStartup - r0)} s after the 7 s watched)"))
            {
                c.Report();
                yield break;
            }
            c.Check(noPlayerDirty == 0,
                $"between the death and the respawn (no player, {noPlayerFrames} frames): no tint, no turned surface "
                + $"({noPlayerDirty} frames with one)");
            yield return new WaitForSeconds(1.5f);
            yield return ViewSettled(2);
            cam = GameCamera.instance;
            camPos = cam.transform.position;
            c.Check(!now.IsDead() && !now.IsSwimming() && !DiveState.Diving && !UnderwaterView.Active && !UnderwaterView.FogApplied
                    && UnderwaterView.FlippedCount == 0 && TurnedSurfaces() == 0 && !DiveCamera.Lifted
                    && camPos.y > Floating.GetLiquidLevel(camPos, 1f, LiquidType.Water),
                $"after the respawn the view is normal: no tint, no turned surface ({TurnedSurfaces()} upside down), camera not "
                + $"under water, no dive (swimming {now.IsSwimming()})");
            c.Check(!(Near(RenderSettings.fogColor, tintUnder, 0.01f) && Near(RenderSettings.fogDensity, densityUnder, 0.0001f)),
                $"after the respawn the fog on screen is not the under-water tint (colour {F(RenderSettings.fogColor)}, density "
                + $"{F(RenderSettings.fogDensity)}; under water it was {F(tintUnder)}, density {F(densityUnder)})");
            CheckLog(s);

            // ---- everything of the death put back on the new player, and the player back at the start ----
            save.PutBack(now, profile);
            var away = Vector3.Distance(Flat(now.transform.position), Flat(rig.Origin));
            if (away > 3f)
            {
                if (away < 60f)
                {
                    MovePlayer(now, rig.Origin);
                    yield return Ticks(10);
                }
                else
                {
                    var started = now.TeleportTo(rig.Origin, rig.OriginRotation, true);
                    t0 = Time.time;
                    yield return null;
                    while (now != null && now.IsTeleporting() && Time.time - t0 < TravelTimeout)
                    {
                        yield return new WaitForSeconds(0.25f);
                    }
                    c.Check(started && now != null && !now.IsTeleporting(), "travel back to the start after the respawn");
                }
            }
            c.Note($"respawned {F(away)} m from the place the test began; items, skills, statistics, death point and god mode put back");
        }
        finally
        {
            ClearOverrides();
            tap.Stop();
            try
            {
                if (EnvMan.instance != null)
                {
                    EnvMan.instance.m_debugWind = windOn;
                    EnvMan.instance.m_debugWindAngle = windAngle;
                    EnvMan.instance.m_debugWindIntensity = windIntensity;
                }
                var player = Player.m_localPlayer;
                if (save != null && player != null)
                {
                    save.PutBack(player, profile);
                }
                else if (save != null && (save.ItemsOut || save.GodOff))
                {
                    SelfTest.Note(DeathName, "cut short with no player object: the test character's items and god mode could not be put back");
                }
            }
            catch (Exception e)
            {
                SelfTest.Note(DeathName, $"putting the player back after the death test failed: {e.Message}");
            }
            // Old player still there (it never died): the rig puts its part back and sends the player home.
            if (!died)
            {
                rig.Restore();
            }
        }
        c.Report();
    }
}
#endif
