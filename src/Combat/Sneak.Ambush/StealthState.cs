using UnityEngine;

namespace MC.Combat.SneakAmbushMod;

// Me = local player's stealth state (design 2.2-2.5). Player.UpdateStealth prefix fill me (still timer every call;
// cues, skill, fog share at each 0.5 s refresh); SEMan.ModifyStealth postfix read me and write the factor; UpdateStealth
// postfix snap and sync icons. Only local player: its own game compute its factor, vanilla ZDO "Stealth" carry it to
// every creature owner. Reset on new local player instance and on feature off.
internal static class StealthState
{
    // Moved more than this in world between two refreshes = not still (ship, cart, slide carry you).
    internal const float StillMoveLimit = 0.25f;

    // Icons of foliage and smoke stay this long after their condition ended (steadier icon row).
    internal const float CueLinger = 1f;

    internal static Player For;              // player instance this state belongs to

    // Holding still.
    internal static float StillTime;         // seconds crouched, grounded, not walking, not carried
    internal static bool StillActive;        // bonus in the target (decided at last refresh)
    internal static Vector3 LastRefreshPos;
    internal static bool HaveRefreshPos;

    // Last refresh.
    internal static bool Crouching;
    internal static float SkillFactor;
    internal static bool FoliageTouching;
    internal static bool FoliageCrown;
    internal static bool InMist;
    internal static bool InSmoke;
    internal static float FogShare;          // 0..1 of FogBonus (0 indoors or standing)
    internal static bool Capped;             // floor raised the factor at last refresh
    internal static float LastWithoutStill = 1f; // factor target without still bonus (snap)

    // This UpdateStealth call.
    internal static bool RefreshThisCall;
    internal static bool SnapPending;
    internal static float SnapValue;

    // Icon display (Time.time of last seen, hysteresis).
    internal static float FoliageSeenAt = -100f;
    internal static float SmokeSeenAt = -100f;
    internal static bool FogShown;

    // Fog density of current environment, from game data (EnvMan.SetEnv postfix), not render state. Kept across
    // resets (world keep its weather).
    internal static float FogDensity;
    internal static bool FogDensityKnown;

    internal static void Reset(Player player)
    {
        For = player;
        StillTime = 0f;
        StillActive = false;
        LastRefreshPos = Vector3.zero;
        HaveRefreshPos = false;
        Crouching = false;
        SkillFactor = 0f;
        FoliageTouching = false;
        FoliageCrown = false;
        InMist = false;
        InSmoke = false;
        FogShare = 0f;
        Capped = false;
        LastWithoutStill = 1f;
        RefreshThisCall = false;
        SnapPending = false;
        SnapValue = 1f;
        FoliageSeenAt = -100f;
        SmokeSeenAt = -100f;
        FogShown = false;
    }

    // Early-game curve (G2): 1 at Sneak 100, 1 - bonus at Sneak 0.
    internal static float Early(AmbushRules rules, float skillFactor) =>
        1f - rules.EarlyGameBonus / 100f * (1f - Mathf.Clamp01(skillFactor));

    // Share (0..1) of the fog bonus for a density. Full at or above FogDensityFullBonus; FullBonus not above NoBonus =
    // any fog thicker than NoBonus give full bonus.
    internal static float FogShareFor(float density, AmbushRules rules)
    {
        var none = rules.FogDensityNoBonus;
        var full = rules.FogDensityFullBonus;
        if (density <= none)
        {
            return 0f;
        }
        if (full <= none)
        {
            return 1f;
        }
        return Mathf.Clamp01((density - none) / (full - none));
    }

    // Our bonuses on "before" (vanilla curve + every status effect): multiply, then floor (never under
    // min(floor, before), never above before). capped = floor raised it.
    internal static float Apply(float before, float product, float floor, out bool capped)
    {
        capped = false;
        var after = before * product;
        if (product < 1f)
        {
            var min = Mathf.Min(floor, before);
            if (after < min)
            {
                after = min;
                capped = true;
            }
        }
        return after;
    }

    internal static float StillFactor(AmbushRules rules) => 1f - rules.StillBonus / 100f;

    internal static float FoliageFactor(AmbushRules rules) =>
        FoliageTouching && rules.FoliageBonus > 0 ? 1f - rules.FoliageBonus / 100f : 1f;

    internal static float FogFactor(AmbushRules rules) => 1f - rules.FogBonus / 100f * FogShare;

    // Fog bonus in whole percent now (cue text, hysteresis).
    internal static int FogPercent(AmbushRules rules) => Mathf.RoundToInt(rules.FogBonus * FogShare);
}
