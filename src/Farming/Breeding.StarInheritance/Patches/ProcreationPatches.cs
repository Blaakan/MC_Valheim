using System;
using System.Globalization;
using HarmonyLib;
using MC.Shared;

namespace MC.Farming.BreedingStarInheritanceMod.Patches;

// Where partner level come from, for the birth decision.
internal enum PartnerSource : byte
{
    None,          // no tamed partner near at birth: own level
    Recorded,      // note written at conception
    BredAlone,     // note say: conceived without partner
    FoundAtBirth,  // no good note: nearest tamed partner in population range
}

// Me = what prefix saw and decided, handed to postfix and finalizer (same class = same __state). Struct: no alloc.
internal struct BirthState
{
    internal bool Owned;         // owner, tamed, has Character: me look at this call
    internal bool WasPregnant;
    internal bool Decided;       // rule ran for this call (Decision valid)
    internal bool Swapped;       // parent m_level hold baby level right now
    internal int OriginalLevel;
    internal int Partner;
    internal PartnerSource Source;
    internal float PartnerDistance;
    internal bool FarmerKnown;
    internal bool FarmerLocal;
    internal float FarmingLevel;
    internal Player Farmer;
    internal SettingsSource Settings;  // own config, server's numbers, or self test override
    internal BirthDecision Decision;
}

// Vanilla Procreate give baby max(m_minOffspringLevel, parent GetLevel()) (live baby SetLevel, egg SetQuality).
// Me put rule result in parent m_level for the call only, so vanilla (and mods reading parent level) pass it on;
// me put real level back first among postfixes, and again in finalizer if something throw.
// Me no predict birth: pregnant tick = decide + swap; postfix see from ZDO if birth (pregnant before, not after) or
// conception (not before, pregnant after) happened. No patch on SetLevel/SetQuality/EggGrow/Growup: wild spawns
// never touched. Framework only patch while Active. Every body catch own errors.
[HarmonyPatch(typeof(Procreation), nameof(Procreation.Procreate))]
internal static class ProcreationPatches
{
    private const string StandAsideText =
        "Another mod skipped the game's breeding code for a birth that was due; Breeding Star Inheritance leaves that call alone.";

    private static bool _standAsideLogged;

    internal static void ClearStandAsideLog()
    {
        _standAsideLogged = false;
    }

    // Last: other prefixes (Seasons, BreedingUpgrades) ran, maybe skipped original. Ticks on every game for every
    // loaded breeding animal (wild too): publish throttle first, then cheap checks, real work only when pregnant.
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static void Procreate_Prefix(Procreation __instance, bool __runOriginal, out BirthState __state)
    {
        __state = default;
        try
        {
            FarmerSkill.PublishThrottled();
            if (!__runOriginal)
            {
                NoteSkippedBirth(__instance);
                return; // other mod own this call
            }
            if (Compat.StarLevelSystemLoaded)
            {
                return;
            }
            var nview = __instance.m_nview;
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                return;
            }
            var tameable = __instance.m_tameable;
            if (tameable == null || !tameable.IsTamed())
            {
                return;
            }
            var character = __instance.m_character;
            if (character == null)
            {
                return; // Pet piece: vanilla give m_minOffspringLevel
            }
            var zdo = nview.GetZDO();
            var stamp = zdo.GetLong(ZDOVars.s_pregnant, 0L);
            __state.Owned = true; // only after state known: postfix never mistake pregnant parent for conception
            if (stamp == 0L)
            {
                return;
            }
            __state.WasPregnant = true;
            Decide(__instance, character, zdo, stamp, ref __state);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(Procreate_Prefix), e);
        }
    }

    // First: other mods' postfixes see real parent level.
    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    private static void Procreate_Postfix(Procreation __instance, BirthState __state)
    {
        if (!__state.Owned)
        {
            return;
        }
        try
        {
            var character = __instance.m_character;
            if (__state.Swapped && character != null)
            {
                character.m_level = __state.OriginalLevel;
            }
            var nview = __instance.m_nview;
            if (nview == null || !nview.IsValid())
            {
                return;
            }
            var zdo = nview.GetZDO();
            var stamp = zdo.GetLong(ZDOVars.s_pregnant, 0L);
            if (__state.WasPregnant)
            {
                if (stamp == 0L)
                {
                    OnBirth(__instance, zdo, __state);
                }
            }
            else if (stamp != 0L && character != null)
            {
                OnConception(__instance, character, zdo, stamp);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(Procreate_Postfix), e);
        }
    }

    // Exception path only (original or a postfix threw, so our postfix maybe never ran): parent never keep baby
    // level. Normal path = nothing (postfix did it; me never overwrite what later postfix set).
    // Void = exception go on as vanilla.
    [HarmonyFinalizer]
    private static void Procreate_Finalizer(Procreation __instance, BirthState __state, Exception __exception)
    {
        if (__exception == null || !__state.Swapped)
        {
            return;
        }
        try
        {
            var character = __instance.m_character;
            if (character != null)
            {
                character.m_level = __state.OriginalLevel;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(Procreate_Finalizer), e);
        }
    }

    // Pregnant tick: partner (note, else nearest tamed in pen), best farmer, rule; swap level if different.
    private static void Decide(Procreation proc, Character character, ZDO zdo, long stamp, ref BirthState st)
    {
        var own = character.m_level;
        if (BirthRecord.TryRead(zdo, stamp, out var recorded))
        {
            st.Partner = recorded;
            st.Source = recorded > BirthRule.NoPartner ? PartnerSource.Recorded : PartnerSource.BredAlone;
        }
        else
        {
            var partner = PartnerFinder.FindNearest(proc, readyOnly: false, PartnerFinder.BirthRange(proc), out var distance);
            if (partner != null)
            {
                st.Partner = partner.GetLevel();
                st.Source = PartnerSource.FoundAtBirth;
                st.PartnerDistance = distance;
            }
            else
            {
                st.Partner = BirthRule.NoPartner;
                st.Source = PartnerSource.None;
            }
        }

        var settings = Plugin.CurrentSettings();
        st.Settings = settings.Source;
        st.FarmerKnown = FarmerSkill.FindBest(proc.transform.position, settings.FarmerRange,
            out st.Farmer, out st.FarmingLevel, out st.FarmerLocal);
        st.Decision = BirthRule.Decide(own, st.Partner, proc.m_minOffspringLevel, settings.Rule,
            st.FarmerKnown, st.FarmingLevel, UnityEngine.Random.value);
        st.OriginalLevel = own;
        st.Decided = true;
        if (st.Decision.Level != own)
        {
            character.m_level = st.Decision.Level;
            st.Swapped = true;
        }
    }

    // Birth happened in this call: note cleared (overwrite, reach other games), Debug line tell every number.
    private static void OnBirth(Procreation proc, ZDO zdo, in BirthState st)
    {
        BirthRecord.Clear(zdo);
        if (!st.Decided)
        {
            return; // prefix failed (already reported): vanilla level, nothing true to tell
        }
        var d = st.Decision;
        string partner;
        switch (st.Source)
        {
            case PartnerSource.Recorded:
                partner = $"{st.Partner} (recorded)";
                break;
            case PartnerSource.BredAlone:
                partner = "none (bred alone)";
                break;
            case PartnerSource.FoundAtBirth:
                partner = $"{st.Partner} (found at birth, {Metres(st.PartnerDistance)} m)";
                break;
            default:
                partner = $"none (no tamed partner {Within(PartnerFinder.BirthRange(proc))})";
                break;
        }
        string farmer;
        if (!st.FarmerKnown)
        {
            farmer = "no farmer known";
        }
        else if (st.FarmerLocal)
        {
            farmer = $"farmer you Farming {Number(st.FarmingLevel)} (own)";
        }
        else
        {
            var name = st.Farmer != null ? st.Farmer.GetPlayerName() : "?";
            farmer = $"farmer {name} Farming {Number(st.FarmingLevel)} (published)";
        }
        var outcome = d.AtCap
            ? $"level {d.Level}, at the cap: no roll"
            : $"chance {Number(d.Chance)}%; roll {d.Roll.ToString("0.000", CultureInfo.InvariantCulture)} -> level {d.Level}"
              + (d.Bonus ? ", extra star" : "");
        Log.Debug($"Birth by {Utils.GetPrefabName(proc.gameObject)} (simulated by this game): own {st.OriginalLevel}, "
                  + $"partner {partner}, min offspring {proc.m_minOffspringLevel} -> base {d.Base}; {farmer} -> {outcome}; "
                  + $"{SettingsText(st.Settings)}.");
    }

    private static string SettingsText(SettingsSource source)
    {
        switch (source)
        {
            case SettingsSource.Server:
                return "server settings";
            case SettingsSource.Test:
                return "self-test settings";
            default:
                return "own settings";
        }
    }

    // Conception in this call: note nearest ready partner (the one vanilla just counted) on pregnant parent.
    private static void OnConception(Procreation proc, Character character, ZDO zdo, long stamp)
    {
        var range = proc.m_partnerCheckRange;
        var partner = PartnerFinder.FindNearest(proc, readyOnly: true, range, out var distance);
        var name = Utils.GetPrefabName(proc.gameObject);
        if (partner != null)
        {
            var level = partner.GetLevel();
            BirthRecord.Write(zdo, level, stamp);
            Log.Debug($"Conceived: {name} (level {character.m_level}) with partner level {level} at {Metres(distance)} m.");
        }
        else if (proc.m_noPartnerOffspring != null)
        {
            BirthRecord.Write(zdo, BirthRule.NoPartner, stamp);
            Log.Debug($"Conceived: {name} (level {character.m_level}): no ready partner {Within(range)}, bred alone.");
        }
        else
        {
            // Vanilla counted partner me cannot see (prefab without BaseAI, or mate added by other mod's count).
            // Old note (if any) cleared; its stamp never match anyway.
            BirthRecord.Clear(zdo);
            Log.Debug($"Conceived: {name} (level {character.m_level}): the game counted a partner this mod cannot see; "
                      + "the partner will be looked for at birth.");
        }
    }

    // Skipped call: log once per activation, only if it was a due birth (conception-only skippers stay quiet).
    private static void NoteSkippedBirth(Procreation proc)
    {
        if (_standAsideLogged)
        {
            return;
        }
        var nview = proc.m_nview;
        if (nview == null || !nview.IsValid() || !nview.IsOwner())
        {
            return;
        }
        var tameable = proc.m_tameable;
        if (tameable == null || !tameable.IsTamed() || !proc.IsPregnant() || !proc.IsDue())
        {
            return;
        }
        _standAsideLogged = true;
        Log.Debug(StandAsideText);
    }

    // Range 0 or less = vanilla "no limit".
    private static string Within(float range)
    {
        return range > 0f ? $"within {Metres(range)} m" : "anywhere nearby";
    }

    private static string Metres(float value)
    {
        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static string Number(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
