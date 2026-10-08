using System;
using MC.Shared;
using UnityEngine;
using Random = System.Random;

namespace MC.Farming.FishingFightMod;

internal enum FightPhase : byte
{
    Calm,      // catch bar: fish in the zone = line come in free
    Struggle,  // bar gone, fish run to one side: rod the other way + reel
}

// Me = the local player's fight with one hooked fish, from hook to catch, loss or line break. FishingFloat.FixedUpdate
// prefix (fisher's game = float owner, design 4.1) call TryStep every physics tick while that float has a fish: me do
// the whole tick in place of vanilla and vanilla skip it. No fish, pending rules, not the local player's float: vanilla
// tick (cast, bite, hook, empty reel stay vanilla).
// Me keep vanilla's tail as it is (rod line slack, line break, the two pulls) and call vanilla's own success path
// (FishingFloat.Catch, SetCatch(null), OnHooked(null), destroy). Every exit also call fish.OnHooked(null): vanilla
// forget it on stamina-out and on a missing rod, fish then stay "hooked" to nothing.
// Fish side: FishPatches read Phase / RunDir every fish tick and steer the hooked fish (design 2.4).
// One fight at a time (Current). Second hooked float of the same player (multi-float mods): vanilla reel for it.
internal sealed class Fight
{
    internal static Fight Current;

    internal readonly FishingFloat Float;
    internal readonly Fish Fish;
    internal readonly FishProfile Profile;
    internal readonly int Quality;
    internal readonly CatchBar Bar;
    internal readonly float StartLine;
    internal readonly Sprite Icon;

    private readonly Random _rng;
    private float _sinceSwitch;
    private float _flipCooldown;

    internal FightPhase Phase { get; private set; }
    internal float PhaseLeft { get; private set; }
    internal int Side { get; private set; }

    // Last tick, for the HUD and the fish steering.
    internal bool Reeling { get; private set; }
    internal RodVerdict Verdict { get; private set; }
    internal Vector3 RunDir { get; private set; }

    // Run the fish would take on the other side (shallow-water turn test, FishPatches).
    internal Vector3 OtherRunDir { get; private set; }
    internal float LineLength { get; private set; }

    // Bar values before / after the last tick: HUD draw between them (smooth at any frame rate).
    internal float PrevZone { get; private set; }
    internal float PrevFish { get; private set; }

#if DEBUG
    // Self test: pin the fish marker (inside the zone / far from it), force a phase and side. Null = normal.
    // TestSide 0 with a forced struggle = phase pinned, side free: the fish pick and switch sides itself (side switch
    // and shallow-water turn run). TestSwitchRate = side switches per second in place of the fish's own rate.
    internal static FishPin? TestPin;
    internal static FightPhase? TestPhase;
    internal static int TestSide = FightLogic.Right;
    internal static float? TestSwitchRate;

    internal static void ClearTest()
    {
        TestPin = null;
        TestPhase = null;
        TestSide = FightLogic.Right;
        TestSwitchRate = null;
    }
#endif

    private Fight(FishingFloat ff, Fish fish, FightRules rules, float skill)
    {
        Float = ff;
        Fish = fish;
        var drop = fish.m_itemDrop;
        Quality = drop != null && drop.m_itemData != null ? Math.Max(1, drop.m_itemData.m_quality) : 1;
        Icon = drop != null && drop.m_itemData != null ? SafeIcon(drop.m_itemData) : null;
        var biome = WorldGenerator.instance != null
            ? WorldGenerator.instance.GetBiome(fish.transform.position)
            : Heightmap.Biome.None;
        Profile = FishProfile.For(Utils.GetPrefabName(fish.gameObject), Quality, biome, rules.FishDifficulty);
        // Seed from fish id and clock: every fight move its own way.
        _rng = new Random(fish.m_nview != null && fish.m_nview.IsValid()
            ? fish.m_nview.GetZDO().m_uid.GetHashCode() ^ Environment.TickCount
            : Environment.TickCount);
        Bar = new CatchBar(Profile, FightLogic.ZoneSize(rules, skill), _rng);
        PrevZone = Bar.ZonePos;
        PrevFish = Bar.FishPos;
        StartLine = Math.Max(1f, ff.m_lineLength);
        LineLength = ff.m_lineLength;
        EnterCalm(rules, first: true);
    }

    // Prefix entry. True = me did this tick (vanilla must skip it).
    internal static bool TryStep(FishingFloat ff)
    {
        DropStale();
        if (ReferenceEquals(ff, _broken))
        {
            return false;
        }
        var nview = ff.m_nview;
        if (nview == null || !nview.IsValid() || !nview.IsOwner())
        {
            return false;
        }
        var fish = ff.GetCatch();
        if (fish == null)
        {
            if (Current != null && ReferenceEquals(Current.Float, ff))
            {
                Current.End("the fish is gone");
            }
            return false;
        }
        var rules = ServerRules.Current;
        if (rules.IsPending)
        {
            if (Current != null && ReferenceEquals(Current.Float, ff))
            {
                Current.End("the server's rules are not here yet");
            }
            return false;
        }
        var player = Player.m_localPlayer;
        if (player == null || !ReferenceEquals(ff.GetOwner(), player))
        {
            if (Current != null && ReferenceEquals(Current.Float, ff))
            {
                // Owner gone (respawn, logout): vanilla destroy this float next and never let the fish go. Me do.
                if (IsOurs(Current.Fish))
                {
                    Release(Current.Fish);
                }
                Current.End("the fisher is gone");
            }
            return false;
        }
        var fight = Current;
        if (fight != null && !ReferenceEquals(fight.Float, ff))
        {
            // Another float of this player already fight: this one reel vanilla.
            return false;
        }
        if (fight == null || !ReferenceEquals(fight.Fish, fish))
        {
            fight?.End("a new fish took the hook");
            fight = new Fight(ff, fish, rules, player.GetSkillFactor(Skills.SkillType.Fishing));
            Current = fight;
            Log.Debug($"Fight started: {Utils.GetPrefabName(fish.gameObject)} quality {fight.Quality}, difficulty "
                      + $"{fight.Profile.Difficulty:0} ({fight.Profile.Motion}), line {ff.m_lineLength:0.0} m.");
        }
        return fight.Step(rules, player, Time.fixedDeltaTime);
    }

    // One physics tick. True = done here (vanilla skip), false = vanilla run this tick (fight just ended with the
    // float still there and empty).
    private bool Step(FightRules rules, Player owner, float dt)
    {
        var ff = Float;
        var fish = Fish;
        var rodTop = ff.GetRodTop(owner);
        if (rodTop == null)
        {
            // Vanilla: rod gone (put away, swimming, dead) = float destroyed silently. Me also let the fish go.
            Release(fish);
            ff.m_nview.Destroy();
            End("the rod is gone");
            return true;
        }
        if (owner.InAttack() || owner.IsDrawingBow())
        {
            // Vanilla cancel (bait is already spent once hooked: ReturnBait give nothing back).
            ff.ReturnBait();
            Release(fish);
            ff.m_nview.Destroy();
            End("the fisher attacked");
            return true;
        }
        var floatPos = ff.transform.position;
        var magnitude = (rodTop.position - floatPos).magnitude;
        if (!owner.HaveStamina())
        {
            // Vanilla loss: float stay in the water, empty; vanilla run the rest of this tick.
            ff.SetCatch(null);
            Release(fish);
            ff.Message("$msg_fishing_lost", prioritized: true);
            Game.instance.IncrementPlayerStat(PlayerStatType.FishLost);
            End("out of stamina");
            return false;
        }

        var skill = owner.GetSkillFactor(Skills.SkillType.Fishing);
        UpdatePhase(rules, dt);
        Reeling = owner.IsBlocking();
        var reelSpeed = FightLogic.ReelSpeed(ff.m_pullLineSpeed, ff.m_pullLineSpeedMaxSkill, skill, rules.ReelSpeed);
        var reelCost = FightLogic.ReelCost(ff.m_pullStaminaUse, fish.GetStaminaUse(), Quality,
            ff.m_pullStaminaUseMaxSkillMultiplier, skill);
        var before = ff.m_lineLength;
        var ownerPos = owner.transform.position;
        var lineDir = floatPos - ownerPos;

        PrevZone = Bar.ZonePos;
        PrevFish = Bar.FishPos;
        if (Phase == FightPhase.Calm)
        {
            Bar.Resize(FightLogic.ZoneSize(rules, skill));
            Bar.Step(dt, Reeling);
            ApplyTestPin();
            Verdict = RodVerdict.Good;
            RunDir = Vector3.zero;
            OtherRunDir = Vector3.zero;
            if (Bar.InZone)
            {
                // Free: no UseStamina call at all, so stamina regen keep going.
                ReelIn(ff, reelSpeed * dt, magnitude, dt, FightLogic.CalmDrag);
            }
            else
            {
                Use(owner, reelCost * rules.OffBarStamina * dt);
            }
        }
        else
        {
            Verdict = FightLogic.Judge(lineDir, owner.transform.forward, Side, rules.RodAngle);
            var fishDir = fish.transform.position - ownerPos;
            var runBase = fishDir.sqrMagnitude > 0.01f ? fishDir : lineDir;
            RunDir = FightLogic.RunDirection(runBase, Side, !Reeling);
            OtherRunDir = FightLogic.RunDirection(runBase, -Side, !Reeling);
            if (Reeling)
            {
                var cost = reelCost * rules.StruggleStamina;
                if (Verdict == RodVerdict.Wrong)
                {
                    cost *= rules.WrongSideStamina;
                }
                Use(owner, cost * dt);
                if (Verdict == RodVerdict.Good)
                {
                    ReelIn(ff, reelSpeed * FightLogic.StruggleReelFactor * dt, magnitude, dt, FightLogic.StruggleDrag);
                }
            }
            else
            {
                ff.m_lineLength += FightLogic.LineRunSpeed(rules, Profile.D01) * dt;
            }
        }
        LineLength = ff.m_lineLength;

        if ((int)ff.m_lineLength != (int)before)
        {
            ff.Message(ff.m_lineLength.ToString("0m"));
        }
        if (ff.m_fishingSkillImproveTimer > 1f)
        {
            ff.m_fishingSkillImproveTimer = 0f;
            owner.RaiseSkill(Skills.SkillType.Fishing);
        }

        if (ff.m_lineLength <= 0.5f)
        {
            // Vanilla success path.
            var msg = FishingFloat.Catch(fish, owner);
            ff.Message(msg, prioritized: true);
            ff.SetCatch(null);
            Release(fish);
            ff.m_nview.Destroy();
            End("caught");
            return true;
        }
        if (ff.m_lineLength > ff.m_maxDistance)
        {
            // Fish took all the line.
            BreakLine(ff, fish, "the fish took all the line");
            return true;
        }

        // Vanilla tail: rod line slack, break when the float is too far, float follow fish, rope to the rod.
        ff.m_rodLine.SetSlack((1f - Utils.LerpStep(ff.m_lineLength / 2f, ff.m_lineLength, magnitude)) * ff.m_maxLineSlack);
        if (magnitude - ff.m_lineLength > ff.m_breakDistance || magnitude > ff.m_maxDistance)
        {
            BreakLine(ff, fish, "the float was pulled too far");
            return true;
        }
        Utils.Pull(ff.m_body, fish.transform.position, 0.5f, ff.m_moveForce, 0.5f, 0.3f);
        Utils.Pull(ff.m_body, rodTop.position, ff.m_lineLength, ff.m_moveForce, 1f, 0.3f);
        return true;
    }

    // Line in, as vanilla: only while the float is not dragged more than 'drag' beyond the line (else the line would
    // run ahead of the float and break it). Vanilla drag 0.2 m (calm). A running fish keep the float about 0.2-0.3 m
    // past the line (its pull vs the rope pull), so a struggle allow more (StruggleDrag), still far from the 4 m break.
    // Skill XP like vanilla reeling with a fish on (timer x2).
    private static void ReelIn(FishingFloat ff, float amount, float magnitude, float dt, float drag)
    {
        if (ff.m_lineLength > magnitude - drag)
        {
            ff.m_lineLength -= amount;
            ff.m_fishingSkillImproveTimer += dt * ff.m_fishingSkillImproveHookedMultiplier;
        }
    }

    private static void Use(Player owner, float stamina)
    {
        // Vanilla UseStamina ignore 0, but me never call it for 0 (keep regen delay untouched).
        if (stamina > 0f)
        {
            owner.UseStamina(stamina);
        }
    }

    private void BreakLine(FishingFloat ff, Fish fish, string why)
    {
        ff.Message("$msg_fishing_linebroke", prioritized: true);
        Release(fish);
        var pos = ff.transform.position;
        ff.m_nview.Destroy();
        ff.m_lineBreakEffect.Create(pos, Quaternion.identity);
        End(why);
    }

    private void UpdatePhase(FightRules rules, float dt)
    {
#if DEBUG
        if (TestPhase.HasValue)
        {
            if (Phase != TestPhase.Value)
            {
                if (TestPhase.Value == FightPhase.Calm)
                {
                    EnterCalm(rules, first: false);
                }
                else
                {
                    EnterStruggle(rules);
                }
            }
            if (Phase == FightPhase.Struggle)
            {
                if (TestSide != 0)
                {
                    Side = TestSide;
                }
                else
                {
                    UpdateSide(dt);
                }
            }
            PhaseLeft = 999f;
            return;
        }
#endif
        PhaseLeft -= dt;
        if (Phase == FightPhase.Calm)
        {
            if (PhaseLeft <= 0f)
            {
                EnterStruggle(rules);
            }
            return;
        }
        if (PhaseLeft <= 0f)
        {
            EnterCalm(rules, first: false);
            return;
        }
        UpdateSide(dt);
    }

    // Fighting fish: now and then it turn to the other side (never in its first second on a side).
    private void UpdateSide(float dt)
    {
        _sinceSwitch += dt;
        _flipCooldown -= dt;
        if (_sinceSwitch >= FightLogic.MinSecondsPerSide && Chance(SwitchRate(), dt))
        {
            Side = -Side;
            _sinceSwitch = 0f;
        }
    }

    private float SwitchRate()
    {
#if DEBUG
        if (TestSwitchRate.HasValue)
        {
            return TestSwitchRate.Value;
        }
#endif
        return FightLogic.SwitchRate(Profile.D01);
    }

    private void EnterCalm(FightRules rules, bool first)
    {
        Phase = FightPhase.Calm;
        PhaseLeft = FightLogic.CalmDuration(rules, Profile.D01, _rng);
        Side = 0;
        Bar.ZoneSpeed = 0f;
        SetEscapeZdo(0f);
        if (!first)
        {
            Log.Debug($"Fight: calm for {PhaseLeft:0.0} s.");
        }
    }

    private void EnterStruggle(FightRules rules)
    {
        Phase = FightPhase.Struggle;
        PhaseLeft = FightLogic.StruggleDuration(rules, Profile.D01, Quality, _rng);
        Side = _rng.NextDouble() < 0.5 ? FightLogic.Left : FightLogic.Right;
        _sinceSwitch = 0f;
        _flipCooldown = 0f;
        // Other games see the fight: vanilla splash at the fish while s_escape (float) > 0.
        SetEscapeZdo(PhaseLeft);
        Log.Debug($"Fight: the fish fights for {PhaseLeft:0.0} s, running {(Side > 0 ? "right" : "left")}.");
    }

    // Fish patch: shallow water ahead of the run = fish turn to the other side (not more than once a second).
    internal void RequestFlip()
    {
        if (Phase != FightPhase.Struggle || _flipCooldown > 0f || _sinceSwitch < FightLogic.MinSecondsPerSide)
        {
            return;
        }
#if DEBUG
        // Self test pin the side: no turn. Side free (TestSide 0): turn like in a real fight.
        if (TestPhase.HasValue && TestSide != 0)
        {
            return;
        }
#endif
        Side = -Side;
        _sinceSwitch = 0f;
        _flipCooldown = 1f;
    }

    // Vanilla writes the int overload when its escape ends, which leave the float value (the one the splash read)
    // positive: me always write the float.
    private void SetEscapeZdo(float value)
    {
        var nview = Fish != null ? Fish.m_nview : null;
        if (nview != null && nview.IsValid() && nview.IsOwner())
        {
            nview.GetZDO().Set(ZDOVars.s_escape, value);
        }
    }

    private void ApplyTestPin()
    {
#if DEBUG
        if (!TestPin.HasValue)
        {
            return;
        }
        if (TestPin.Value == FishPin.InZone)
        {
            Bar.FishPos = Bar.ZonePos + Bar.ZoneSize * 0.5f;
        }
        else
        {
            Bar.FishPos = Bar.ZonePos + Bar.ZoneSize * 0.5f > 0.5f ? 0f : 1f;
        }
        Bar.FishSpeed = 0f;
#endif
    }

    // Fight over. Fish (if still here and ours) back to vanilla timing: next escape soon, no splash.
    internal void End(string why)
    {
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }
        try
        {
            if (Fish != null && Fish.m_nview != null && Fish.m_nview.IsValid() && Fish.m_nview.IsOwner())
            {
                // Still hooked (feature off mid-fight): passive now, vanilla escape in a second (its own length,
                // its own splash). Unhooked: unused.
                if (Fish.IsHooked())
                {
                    Fish.m_escapeTime = -1f;
                    Fish.m_haveWaypoint = false;
                }
                Fish.m_nextEscape = Time.time + 1f;
                Fish.m_nview.GetZDO().Set(ZDOVars.s_escape, 0f);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Fight.End", e);
        }
        Log.Debug($"Fight ended: {why}.");
    }

    // Fight whose float or fish is gone without a tick of mine (logout, disconnect, zone unload, other code destroy
    // it): end it, let the fish go if it is still here and ours. Float tick, HUD frame and world end call me: cheap
    // (Unity null checks) and nothing to do while no fight.
    internal static void DropStale()
    {
        var fight = Current;
        if (fight == null)
        {
            return;
        }
        var ff = fight.Float;
        if (ff != null && fight.Fish != null && ff.m_nview != null && ff.m_nview.IsValid())
        {
            return;
        }
        var fish = fight.Fish;
        if (IsOurs(fish) && (fish.m_fishingFloat == null || ReferenceEquals(fish.m_fishingFloat, ff)))
        {
            Release(fish);
        }
        fight.End("its float or fish is gone");
    }

    // Feature off or rules gone mid-fight: fish stay hooked, vanilla reel take over from the next tick.
    internal static void Shutdown()
    {
        Current?.End("the mod was turned off");
        Current = null;
        _broken = null;
    }

    // Float whose tick threw: vanilla reel it to the end (no new fight on it every tick). Cleared with Shutdown;
    // the float itself die with its fishing.
    private static FishingFloat _broken;

    internal static void MarkBroken(FishingFloat ff)
    {
        _broken = ff;
        if (Current != null && ReferenceEquals(Current.Float, ff))
        {
            Current.End("an error");
        }
    }

    private static void Release(Fish fish)
    {
        if (fish != null)
        {
            fish.OnHooked(null);
        }
    }

    // Fish still in the world and run by this game (OnHooked claim ownership: never steal one).
    private static bool IsOurs(Fish fish) =>
        fish != null && fish.m_nview != null && fish.m_nview.IsValid() && fish.m_nview.IsOwner();

    private bool Chance(float ratePerSecond, float dt) =>
        ratePerSecond > 0f && _rng.NextDouble() < 1.0 - Math.Exp(-ratePerSecond * dt);

    private static Sprite SafeIcon(ItemDrop.ItemData item)
    {
        try
        {
            return item.GetIcon();
        }
        catch (Exception)
        {
            return null;
        }
    }
}

#if DEBUG
internal enum FishPin : byte
{
    InZone,
    Away,
}
#endif
