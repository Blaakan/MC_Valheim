using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using MC.Shared;
using UnityEngine;
#endif

namespace MC.Exploration.SwimmingDiveMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1,
// -Mod Swimming.Dive). Design 7.5:
//   dive.network  rules on the wire (round trip, wild values clamped, unknown layout and cut-off refused), which rules
//                 apply (own / server / pending), push debounce, join check verdicts (pure)
//   dive.logic    dive state rules (deep hysteresis, start/keep/end, target speed, near-surface hand-back to vanilla
//                 buoyancy, blockers), camera clamp maths, fog
//                 maths, fog override on RenderSettings and the _SunFogColor global (no compounding, base taken again
//                 after SetEnv, put back only when still ours, linear start/end kept through SetEnv; NOTE how Unity
//                 store a global colour in this colour space), surface flip round trip on a live
//                 WaterVolume (rotation and _depth back exactly; fail when no loaded surface is safe to turn),
//                 other-mod classification. NOTE dumps of the water surface prefab layout
//   dive.pending  rules pending (client waiting for the server) = dive refused; pending win over test rules
//   dive.dive     live, in deep water found with WorldGenerator (probe spawn is far from water): travel there,
//                 pending = no dive, Crouch = down, release = hold depth, open water = nothing solid above and
//                 sideways input = no sideways move, Jump refused while diving, still under water = stamina drain,
//                 Jump = up and back to vanilla at the surface, still at the surface = no drain, Crouch + Jump held at
//                 the surface = vertical left to vanilla every tick (rides the waves, never deep) and no drain; camera
//                 under the surface, fog and turned surfaces while under, all back
//                 after; screenshots; NOTE dumps of player, camera, water and fog values (sun fog global too); travel
//                 back
// Me force rules only with ServerRules.TestRules / TestPending, keys with DiveInput.TestDown / TestUp, view settings
// with Visuals.TestAllOn: never config. Rig put back controls, look, time, world stamina rate and the player's place.
internal static class SelfTests
{
    private const string NetworkName = "dive.network";
    private const string LogicName = "dive.logic";
    private const string PendingName = "dive.pending";
    private const string DiveName = "dive.dive";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(NetworkName, RunNetwork);
        SelfTest.Register(LogicName, RunLogic);
        SelfTest.Register(PendingName, RunPending);
        SelfTest.Register(DiveName, RunDive);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(NetworkName);
        SelfTest.Unregister(LogicName);
        SelfTest.Unregister(PendingName);
        SelfTest.Unregister(DiveName);
        ClearOverrides();
#endif
    }

#if DEBUG
    // Water at least this deep (m) for the live dive: 3 m dive + floor margin + wave troughs.
    private const float MinWaterDepth = 12f;

    // Each trip (vanilla distant teleport: 8 s minimum, then area ready and floor found). Two trips + the dive fit in
    // the probe's 120 s test timeout.
    private const float TravelTimeout = 35f;

    private static void ClearOverrides()
    {
        ServerRules.TestPending = false;
        ServerRules.TestRules = null;
        DiveInput.ClearTest();
        Visuals.TestAllOn = null;
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

    // Result of a nested coroutine.
    private sealed class Box
    {
        internal bool Ok;
        internal string Detail = "";
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string F(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";

    private static string F(Color c) => $"({F(c.r)}, {F(c.g)}, {F(c.b)}, {F(c.a)})";

    private static bool Near(float a, float b, float tolerance = 0.001f) => Mathf.Abs(a - b) <= tolerance;

    private static bool Near(Color a, Color b) => Near(a.r, b.r) && Near(a.g, b.g) && Near(a.b, b.b) && Near(a.a, b.a);

    private static bool NearRaw(Vector4 a, Vector4 b) =>
        Near(a.x, b.x, 0.0001f) && Near(a.y, b.y, 0.0001f) && Near(a.z, b.z, 0.0001f) && Near(a.w, b.w, 0.0001f);

    private static string F(Vector4 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)}, {F(v.w)})";

    private static bool SameRotation(Quaternion a, Quaternion b) => Quaternion.Angle(a, b) < 0.01f;

    // ---------- dive.network ----------

    private static IEnumerator RunNetwork()
    {
        var c = new Checks(NetworkName);
        var custom = new DiveRules { DiveSpeedMultiplier = 1.5f, IdleRiseSpeed = 0.3f, UnderwaterStaminaMultiplier = 2f };
        var pkg = new ZPackage();
        custom.Write(pkg);
        pkg.SetPos(0);
        c.Check(DiveRules.TryRead(pkg, out var back, out var clamped) && !clamped && back.Describe() == custom.Describe()
                && Near(back.IdleRiseSpeed, 0.3f) && !back.IsPending,
            "rules survive the wire unchanged");

        var wild = new DiveRules { DiveSpeedMultiplier = float.NaN, IdleRiseSpeed = 99f, UnderwaterStaminaMultiplier = -3f };
        pkg = new ZPackage();
        wild.Write(pkg);
        pkg.SetPos(0);
        var d = DiveRules.Default;
        c.Check(DiveRules.TryRead(pkg, out back, out clamped) && clamped
                && Near(back.DiveSpeedMultiplier, d.DiveSpeedMultiplier) && Near(back.IdleRiseSpeed, DiveRules.IdleRiseMax)
                && Near(back.UnderwaterStaminaMultiplier, 0f),
            "out-of-range and NaN values from the wire are pulled into range");
        wild = new DiveRules { DiveSpeedMultiplier = float.PositiveInfinity, UnderwaterStaminaMultiplier = 50f };
        pkg = new ZPackage();
        wild.Write(pkg);
        pkg.SetPos(0);
        c.Check(DiveRules.TryRead(pkg, out back, out clamped) && clamped
                && Near(back.DiveSpeedMultiplier, d.DiveSpeedMultiplier)
                && Near(back.UnderwaterStaminaMultiplier, DiveRules.StaminaMultiplierMax),
            "infinite and too-big values from the wire are pulled into range");

        pkg = new ZPackage();
        pkg.Write(DiveRules.Layout + 1);
        pkg.Write(1f);
        pkg.SetPos(0);
        c.Check(!DiveRules.TryRead(pkg, out _, out _), "unknown rules layout refused");
        pkg = new ZPackage();
        pkg.Write(DiveRules.Layout);
        pkg.Write(1f);
        pkg.SetPos(0);
        c.Check(!DiveRules.TryRead(pkg, out _, out _), "cut-off rules package refused");
        c.Check(DiveRules.Pending.IsPending && !DiveRules.Default.IsPending && !DiveRules.Own().IsPending,
            "only the built-in Pending rules are pending");
        c.Check(DiveRules.Default.DiveSpeedMultiplier == 1f && DiveRules.Default.IdleRiseSpeed == 0f
                && DiveRules.Default.UnderwaterStaminaMultiplier == 1f,
            "defaults: speed x1, hold depth, drain x1");

        // A server (this single-player world is one) never takes rules from a peer.
        var current = ServerRules.Current;
        pkg = new ZPackage();
        custom.Write(pkg);
        pkg.SetPos(0);
        c.Check(!ServerRules.Receive(pkg) && ReferenceEquals(ServerRules.Current, current) && !ServerRules.UsingServer
                && !ServerRules.IsPending,
            "single player / server took rules from a peer or is pending");

        var own = new DiveRules();
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

    // ---------- dive.logic ----------

    private static IEnumerator RunLogic()
    {
        var c = new Checks(LogicName);
        const float rest = 28f;

        // Deep: EnterMargin 0.5 to become deep, ExitMargin 0.2 to stop; never deep while not diving.
        c.Check(!DiveLogic.Deep(false, false, rest - 5f, rest), "not diving is never deep");
        c.Check(!DiveLogic.Deep(true, false, rest - 0.4f, rest) && DiveLogic.Deep(true, false, rest - 0.6f, rest),
            "deep starts 0.5 m under the rest height");
        c.Check(DiveLogic.Deep(true, true, rest - 0.3f, rest) && !DiveLogic.Deep(true, true, rest - 0.1f, rest),
            "deep stays until 0.2 m under the rest height (hysteresis)");

        // Diving: start needs Crouch and stamina; keep while deep or Crouch held with stamina.
        c.Check(DiveLogic.Diving(false, false, true, true), "Crouch with stamina starts a dive");
        c.Check(!DiveLogic.Diving(false, false, true, false), "no dive start at 0 stamina");
        c.Check(!DiveLogic.Diving(false, false, false, true), "no dive without Crouch");
        c.Check(DiveLogic.Diving(true, true, false, true) && DiveLogic.Diving(true, true, false, false),
            "a deep diver keeps diving without Crouch, also at 0 stamina (forced rise)");
        c.Check(!DiveLogic.Diving(true, false, false, true), "near the surface without Crouch: back to vanilla");
        c.Check(!DiveLogic.Diving(true, false, true, false), "near the surface at 0 stamina: back to vanilla");
        c.Check(DiveLogic.Diving(true, false, true, true), "near the surface with Crouch: still diving");

        // Target speed.
        c.Check(Near(DiveLogic.TargetSpeed(true, false, true, 2f, 0f), -2f), "Crouch = down at swim speed");
        c.Check(Near(DiveLogic.TargetSpeed(false, true, true, 2f, 0f), 2f), "Jump = up at swim speed");
        c.Check(Near(DiveLogic.TargetSpeed(true, true, true, 2f, 0f), 0f), "both keys = stay");
        c.Check(Near(DiveLogic.TargetSpeed(false, false, true, 2f, 0f), 0f)
                && Near(DiveLogic.TargetSpeed(false, false, true, 2f, 0.3f), 0.3f),
            "no key = IdleRiseSpeed (0 = hold depth)");
        c.Check(Near(DiveLogic.TargetSpeed(true, false, false, 2f, 0f), 2f), "0 stamina = up, Crouch ignored");

        // Near the surface with nothing to do up or down: vertical left to vanilla buoyancy (rides the waves).
        c.Check(DiveLogic.VanillaVertical(false, true, DiveLogic.TargetSpeed(true, true, true, 2f, 0f)),
            "near the surface, Crouch and Jump: vertical left to vanilla (rides the waves)");
        c.Check(DiveLogic.VanillaVertical(false, true, 0f), "near the surface, floor stops the Crouch: left to vanilla");
        c.Check(DiveLogic.VanillaVertical(false, true, DiveLogic.TargetSpeed(true, false, true, 0f, 0f)),
            "near the surface, Crouch with no swim speed (minor action): left to vanilla");
        c.Check(!DiveLogic.VanillaVertical(true, true, 0f), "deep, target 0: the diver holds the depth itself");
        c.Check(!DiveLogic.VanillaVertical(false, true, -2f) && !DiveLogic.VanillaVertical(false, true, 2f)
                && !DiveLogic.VanillaVertical(false, true, 0.3f),
            "near the surface going down, up or idle rise: own vertical speed");
        c.Check(!DiveLogic.VanillaVertical(false, false, 0f) && !DiveLogic.VanillaVertical(true, false, 2f),
            "0 stamina: never left to vanilla (forced ascent)");

        // Blockers, in order.
        c.Check(DiveLogic.Blocker(true, true, true, false, false, false, false) == DiveBlock.Pending, "pending blocks");
        c.Check(DiveLogic.Blocker(false, false, true, false, false, false, false) == DiveBlock.NotSwimming,
            "not swimming blocks");
        c.Check(DiveLogic.Blocker(false, true, false, false, false, false, false) == DiveBlock.NotWater, "tar blocks");
        c.Check(DiveLogic.Blocker(false, true, true, true, false, false, false) == DiveBlock.Dead, "dead blocks");
        c.Check(DiveLogic.Blocker(false, true, true, false, true, false, false) == DiveBlock.Attached, "attached blocks");
        c.Check(DiveLogic.Blocker(false, true, true, false, false, true, false) == DiveBlock.Teleporting,
            "teleporting blocks");
        c.Check(DiveLogic.Blocker(false, true, true, false, false, false, true) == DiveBlock.DebugFly, "debug fly blocks");
        c.Check(DiveLogic.Blocker(false, true, true, false, false, false, false) == DiveBlock.None,
            "swimming in water: dive allowed");

        // Camera.
        c.Check(!DiveCamera.LiftNext(false, 0.3f) && DiveCamera.LiftNext(false, 0.4f), "camera goes under at 0.35 m eye depth");
        c.Check(DiveCamera.LiftNext(true, 0.2f) && !DiveCamera.LiftNext(true, 0.1f), "camera comes back up at 0.15 m");
        c.Check(Near(DiveCamera.Clearance(0.2f), 0.1f) && Near(DiveCamera.Clearance(5f), 0.5f)
                && Near(DiveCamera.Clearance(0.02f), 0.05f),
            "clearance = half the eye depth, 0.05-0.5 m");
        var cut = DiveCamera.BelowSurface(new Vector3(0f, -2f, 0f), new Vector3(0f, 1f, -4f), -0.5f);
        c.Check(Near(cut.y, -0.5f) && Near(cut.z, -2f), $"camera pulled along the eye line to the surface limit: {F(cut)}");
        var under = new Vector3(1f, -3f, 2f);
        c.Check(DiveCamera.BelowSurface(new Vector3(0f, -2f, 0f), under, -0.5f) == under, "camera already under: unchanged");
        var flat = DiveCamera.BelowSurface(new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, -4f), -0.5f);
        c.Check(Near(flat.y, -0.5f) && Near(flat.z, -4f), "eye above the limit: only the height is cut");

        // Fog maths.
        c.Check(Near(UnderwaterView.Density(FogMode.Exponential, 25f), 0.1565f, 0.0005f)
                && Near(UnderwaterView.Density(FogMode.ExponentialSquared, 25f), 0.0791f, 0.0005f),
            "25 m visibility = density 0.156 (exponential), 0.079 (exponential squared)");
        var tint = Visuals.DefaultFogColor;
        c.Check(Near(UnderwaterView.Tint(new Color(0.6f, 0.6f, 0.6f), tint, 0f), tint), "bright day, at the surface: the tint");
        var night = UnderwaterView.Tint(Color.black, tint, 0f);
        c.Check(Near(night.g, tint.g * UnderwaterView.MinBrightness), "night: tint at the minimum brightness");
        var deepColor = UnderwaterView.Tint(new Color(0.6f, 0.6f, 0.6f), tint, 100f);
        c.Check(Near(deepColor.g, tint.g * UnderwaterView.MinDepthLight), "100 m down: darkest depth light");

        // Corners.
        var src = new[] { 0.1f, 0.2f, 0.3f, 0.4f };
        var dst = new float[4];
        UnderwaterView.Corners(src, true, dst);
        c.Check(Near(dst[0], 0.4f) && Near(dst[1], 0.3f) && Near(dst[2], 0.2f) && Near(dst[3], 0.1f),
            "flipped surface: depth corners [3],[2],[1],[0]");
        UnderwaterView.Corners(src, false, dst);
        c.Check(Near(dst[0], 0.1f) && Near(dst[3], 0.4f), "surface back: depth corners unchanged");

        // Other mods.
        c.Check(ForeignMods.Classify("MainStreetGaming.BetterDiving", "BetterDiving") == ForeignKind.Blocks
                && ForeignMods.Classify("sighsorry.DiveIn", "DiveIn") == ForeignKind.Blocks
                && ForeignMods.Classify("Searica.Valheim.UnderTheSea", "UnderTheSea") == ForeignKind.Blocks
                && ForeignMods.Classify("blacks7ar.VikingsDoSwim", "VikingsDoSwim") == ForeignKind.Blocks
                && ForeignMods.Classify("ch.easy.develope.vh.diving.mod", "Diving") == ForeignKind.Blocks,
            "the five dive mods block");
        c.Check(ForeignMods.Classify("dev.crystal.underwater", "Underwater") == ForeignKind.Composes
                && ForeignMods.Classify("Aegir", "Aegir") == ForeignKind.Composes
                && ForeignMods.Classify("projjm.improvedswimming", "Improved Swimming") == ForeignKind.Composes
                && ForeignMods.Classify("MidnightsFX.ImpactfulSkills", "ImpactfulSkills") == ForeignKind.Composes
                && ForeignMods.Classify("some.author.guid", "NoUnderwaterCamera") == ForeignKind.Composes,
            "camera and swim tweaks compose");
        c.Check(ForeignMods.Classify(ModInfo.Guid, ModInfo.Name) == ForeignKind.None
                && ForeignMods.Classify("MC.Combat.Sneak.Ambush", "Sneak Ambush") == ForeignKind.None
                && ForeignMods.Classify(null, null) == ForeignKind.None,
            "this mod and unrelated mods are left alone");
        c.Check(ForeignMods.BlockerText() == null, "no other dive mod in the test game");

        FogRoundTrip(c);
        SurfaceRoundTrip(c);
        c.Report();
        yield break;
    }

    // Scratch global for Raw(): never read by any shader.
    private static readonly int ScratchColorId = Shader.PropertyToID("_MCDiveSelfTestColor");

    // Raw stored value Shader.SetGlobalColor give a colour (Linear space = converted), like SetEnv's write.
    private static Vector4 Raw(Color color)
    {
        Shader.SetGlobalColor(ScratchColorId, color);
        return Shader.GetGlobalVector(ScratchColorId);
    }

    // RenderSettings and the _SunFogColor global driven by hand, in one frame (no LateUpdate in between): override, no
    // compounding, base taken again after "SetEnv", put back, and left alone when someone else wrote. Linear mode (fog
    // mod): start/end kept through a SetEnv that only rewrite colour and density (and sun fog).
    private static void FogRoundTrip(Checks c)
    {
        if (UnderwaterView.FogApplied || UnderwaterView.Active)
        {
            c.Note("under-water fog already on (camera under water?): fog round trip not tested");
            return;
        }
        var sunId = UnderwaterView.SunFogId;
        var savedColor = RenderSettings.fogColor;
        var savedDensity = RenderSettings.fogDensity;
        var savedStart = RenderSettings.fogStartDistance;
        var savedEnd = RenderSettings.fogEndDistance;
        var savedMode = RenderSettings.fogMode;
        var savedSun = Shader.GetGlobalVector(sunId);
        var savedTest = Visuals.TestAllOn;
        c.Note($"RenderSettings now: fog {RenderSettings.fog}, mode {RenderSettings.fogMode}, density {F(savedDensity)}, "
               + $"colour {F(savedColor)}, start {F(savedStart)}, end {F(savedEnd)}; _SunFogColor raw {F(savedSun)}");
        try
        {
            // How Unity store a global colour here (put back uses the raw vector, so any answer works).
            var probe = new Color(0.5f, 0.5f, 0.5f, 1f);
            var probeRaw = Raw(probe);
            c.Note($"colour space {QualitySettings.activeColorSpace}: SetGlobalColor{F(probe)} stored raw {F(probeRaw)}, "
                   + $"GetGlobalColor gives {F(Shader.GetGlobalColor(ScratchColorId))}");

            Visuals.TestAllOn = true;
            var mode = RenderSettings.fogMode;
            var want = UnderwaterView.Density(mode, Visuals.DefaultVisibility);
            var baseA = new Color(0.5f, 0.55f, 0.6f, 1f);
            var sunA = new Color(0.7f, 0.62f, 0.3f, 1f);
            RenderSettings.fogColor = baseA;
            RenderSettings.fogDensity = 0.01f;
            Shader.SetGlobalColor(sunId, sunA);
            UnderwaterView.ApplyFog(3f);
            var first = RenderSettings.fogDensity;
            var firstColor = RenderSettings.fogColor;
            var firstSun = Shader.GetGlobalVector(sunId);
            c.Check(Near(first, Mathf.Max(0.01f, want), 0.0001f)
                    && Near(firstColor, UnderwaterView.Tint(baseA, Visuals.DefaultFogColor, 3f)),
                $"fog override: density {F(first)} (want {F(want)}), colour {F(firstColor)}");
            c.Check(NearRaw(firstSun, Raw(firstColor)) && NearRaw(UnderwaterView.BaseSunFog, Raw(sunA)),
                $"sun fog override: _SunFogColor raw {F(firstSun)} = the fog tint {F(Raw(firstColor))}, base "
                + $"{F(UnderwaterView.BaseSunFog)} (want {F(Raw(sunA))})");
            UnderwaterView.ApplyFog(3f);
            UnderwaterView.ApplyFog(3f);
            c.Check(Near(RenderSettings.fogDensity, first, 0.00001f) && Near(RenderSettings.fogColor, firstColor),
                "fog applied again without SetEnv: unchanged (no compounding)");
            c.Check(NearRaw(Shader.GetGlobalVector(sunId), firstSun) && NearRaw(UnderwaterView.BaseSunFog, Raw(sunA)),
                $"sun fog applied again without SetEnv: unchanged, base still the vanilla one "
                + $"({F(UnderwaterView.BaseSunFog)}, no compounding)");
            var baseB = new Color(0.1f, 0.1f, 0.12f, 1f);
            var sunB = new Color(0.2f, 0.18f, 0.12f, 1f);
            RenderSettings.fogColor = baseB;
            RenderSettings.fogDensity = 0.3f;
            Shader.SetGlobalColor(sunId, sunB);
            UnderwaterView.ApplyFog(3f);
            c.Check(Near(UnderwaterView.BaseDensity, 0.3f, 0.0001f) && Near(RenderSettings.fogDensity, Mathf.Max(0.3f, want), 0.0001f)
                    && Near(RenderSettings.fogColor, UnderwaterView.Tint(baseB, Visuals.DefaultFogColor, 3f)),
                "new vanilla fog (SetEnv) taken as the new base; thicker vanilla fog stays");
            c.Check(NearRaw(UnderwaterView.BaseSunFog, Raw(sunB))
                    && NearRaw(Shader.GetGlobalVector(sunId), Raw(RenderSettings.fogColor)),
                $"new vanilla sun fog (SetEnv) taken as the new base ({F(UnderwaterView.BaseSunFog)}, want {F(Raw(sunB))}); "
                + "the global holds the new tint");
            UnderwaterView.RestoreFog();
            c.Check(!UnderwaterView.FogApplied && Near(RenderSettings.fogDensity, 0.3f, 0.00001f)
                    && Near(RenderSettings.fogColor, baseB),
                "fog put back to the last vanilla values");
            var backSun = Shader.GetGlobalVector(sunId);
            c.Check(NearRaw(backSun, Raw(sunB)),
                $"sun fog put back to the last vanilla value: raw {F(backSun)} (want {F(Raw(sunB))})");
            RenderSettings.fogDensity = 0.01f;
            UnderwaterView.ApplyFog(3f);
            RenderSettings.fogDensity = 0.5f;
            var sunOther = new Color(0.9f, 0.1f, 0.4f, 1f);
            Shader.SetGlobalColor(sunId, sunOther);
            UnderwaterView.RestoreFog();
            c.Check(Near(RenderSettings.fogDensity, 0.5f, 0.00001f), "fog written by someone else is left alone");
            c.Check(NearRaw(Shader.GetGlobalVector(sunId), Raw(sunOther)),
                $"sun fog written by someone else is left alone (raw {F(Shader.GetGlobalVector(sunId))})");

            // Linear fog (only a fog mod set it): start/end are their own pair.
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 10f;
            RenderSettings.fogEndDistance = 500f;
            RenderSettings.fogColor = baseA;
            RenderSettings.fogDensity = 0.01f;
            UnderwaterView.ApplyFog(3f);
            var linearStart = RenderSettings.fogStartDistance;
            var linearEnd = RenderSettings.fogEndDistance;
            RenderSettings.fogColor = baseB;
            RenderSettings.fogDensity = 0.3f;
            UnderwaterView.ApplyFog(3f);
            UnderwaterView.RestoreFog();
            c.Check(Near(linearStart, 0f) && Near(linearEnd, Mathf.Min(500f, Visuals.DefaultVisibility))
                    && Near(RenderSettings.fogStartDistance, 10f) && Near(RenderSettings.fogEndDistance, 500f)
                    && Near(RenderSettings.fogDensity, 0.3f, 0.00001f) && Near(RenderSettings.fogColor, baseB),
                $"linear fog: start/end {F(linearStart)}/{F(linearEnd)} under water, back to "
                + $"{F(RenderSettings.fogStartDistance)}/{F(RenderSettings.fogEndDistance)} after a SetEnv (want 10/500)");
        }
        finally
        {
            UnderwaterView.RestoreFog();
            Visuals.TestAllOn = savedTest;
            RenderSettings.fogMode = savedMode;
            RenderSettings.fogColor = savedColor;
            RenderSettings.fogDensity = savedDensity;
            RenderSettings.fogStartDistance = savedStart;
            RenderSettings.fogEndDistance = savedEnd;
            Shader.SetGlobalVector(sunId, savedSun);
        }
    }

    // A loaded WaterVolume turned and back, in one frame: rotation and _depth exactly as before. NOTE the prefab
    // layout the flip rely on (1.0.16 run: surface own object, not carrying the trigger, shader Custom/Water with no
    // _Cull; all loaded volumes safe). Loaded surfaces but none safe = surface from below dead: fail.
    private static void SurfaceRoundTrip(Checks c)
    {
        if (UnderwaterView.FlippedCount > 0)
        {
            c.Note("water surfaces already turned (camera under water?): flip round trip not tested");
            return;
        }
        WaterVolume volume = null;
        var safe = 0;
        var total = 0;
        foreach (var v in WaterVolume.Instances)
        {
            if (v == null || v.m_waterSurface == null)
            {
                continue;
            }
            total++;
            if (UnderwaterView.SafeToFlip(v))
            {
                safe++;
                if (volume == null)
                {
                    volume = v;
                }
            }
        }
        c.Note($"{total} loaded water volumes with a surface, {safe} safe to turn");
        if (volume == null)
        {
            if (total > 0)
            {
                c.Check(false, $"no loaded water surface is safe to turn ({total} loaded): surface from below cannot work");
            }
            else
            {
                c.Note("no water surface is loaded: flip round trip not tested");
            }
            return;
        }
        NoteVolume(c, volume, "first loaded volume");
        var t = volume.m_waterSurface.transform;
        var rotation = t.localRotation;
        var position = t.position;
        var material = volume.m_waterSurface.material;
        var vanilla = new float[4];
        UnderwaterView.VanillaCorners(volume, vanilla);
        try
        {
            UnderwaterView.FlipSurfaces();
            var flippedDepth = material.GetFloatArray("_depth");
            c.Check(UnderwaterView.TryGetSaved(volume, out var saved) && SameRotation(saved, rotation)
                    && SameRotation(t.localRotation, rotation * Quaternion.Euler(180f, 0f, 0f)),
                "surface turned 180 degrees about its local X");
            c.Check((t.position - position).sqrMagnitude < 0.0001f, "surface did not move");
            c.Check(flippedDepth != null && flippedDepth.Length >= 4 && Near(flippedDepth[0], vanilla[3])
                    && Near(flippedDepth[3], vanilla[0]) && Near(flippedDepth[1], vanilla[2]),
                "turned surface: _depth corners swapped");
            UnderwaterView.RestoreSurfaces();
            var backDepth = material.GetFloatArray("_depth");
            c.Check(UnderwaterView.FlippedCount == 0 && t.localRotation == rotation, "surface rotation back exactly");
            c.Check(backDepth != null && backDepth.Length >= 4 && Near(backDepth[0], vanilla[0])
                    && Near(backDepth[3], vanilla[3]),
                "surface _depth back to the vanilla corners");
        }
        finally
        {
            UnderwaterView.RestoreSurfaces();
        }
    }

    private static void NoteVolume(Checks c, WaterVolume volume, string label)
    {
        var surface = volume.m_waterSurface;
        var st = surface.transform;
        var col = volume.m_collider;
        var filter = surface.GetComponent<MeshFilter>();
        var mesh = filter != null ? filter.sharedMesh : null;
        var shared = surface.sharedMaterial;
        c.Note($"{label} '{volume.name}': volume at {F(volume.transform.position)}, surface '{st.name}' at {F(st.position)} "
               + $"(child of the volume: {st.IsChildOf(volume.transform) && st != volume.transform}, same object: "
               + $"{st == volume.transform}), local rotation {F(st.localRotation.eulerAngles)}, "
               + $"collider bounds y {(col != null ? F(col.bounds.min.y) + ".." + F(col.bounds.max.y) : "none")}, "
               + $"xz size {(col != null ? F(col.bounds.size.x) + " x " + F(col.bounds.size.z) : "-")}, "
               + $"mesh bounds centre {(mesh != null ? F(mesh.bounds.center) : "none")} size {(mesh != null ? F(mesh.bounds.size) : "-")}, "
               + $"shader {(shared != null && shared.shader != null ? shared.shader.name : "none")}, "
               + $"_Cull {(shared != null && shared.HasProperty("_Cull") ? F(shared.GetFloat("_Cull")) : "absent")}, "
               + $"forceDepth {F(volume.m_forceDepth)}, surfaceOffset {F(volume.m_surfaceOffset)}, "
               + $"global wind {volume.m_useGlobalWind}, shadows {surface.shadowCastingMode}");
    }

    // ---------- dive.pending ----------

    private static IEnumerator RunPending()
    {
        var c = new Checks(PendingName);
        try
        {
            c.Check(!ServerRules.IsPending, "single player is never pending");
            ServerRules.TestRules = new DiveRules { DiveSpeedMultiplier = 2f };
            ServerRules.TestPending = true;
            var rules = ServerRules.Current;
            c.Check(rules.IsPending && ReferenceEquals(rules, DiveRules.Pending), "pending wins over test rules");
            c.Check(rules.Describe().IndexOf("waiting", StringComparison.Ordinal) >= 0, "pending rules say they wait");
            c.Check(DiveLogic.Blocker(rules.IsPending, true, true, false, false, false, false) == DiveBlock.Pending,
                "pending rules refuse the dive (normal swimming, Crouch does nothing)");
            ServerRules.TestPending = false;
            rules = ServerRules.Current;
            c.Check(!rules.IsPending && Near(rules.DiveSpeedMultiplier, 2f), "pending over: rules in force again");
            c.Check(DiveLogic.Blocker(rules.IsPending, true, true, false, false, false, false) == DiveBlock.None,
                "rules in force: dive allowed");
            c.Note("pending in water (Crouch held, no dive) is checked live by dive.dive");
        }
        finally
        {
            ServerRules.TestPending = false;
            ServerRules.TestRules = null;
        }
        c.Report();
        yield break;
    }

    // ---------- dive.dive (live) ----------

    // Me = what the live dive change on player and world. Restore (finally, no yield) put everything back.
    private sealed class DiveRig
    {
        internal readonly Player Player;
        internal readonly Vector3 Origin;
        internal readonly Quaternion OriginRotation;
        private readonly float _pitch;
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

        internal DiveRig(Player player)
        {
            Player = player;
            Origin = player.transform.position;
            OriginRotation = player.transform.rotation;
            _pitch = player.m_lookPitch;
        }

        // Me drive the player (no keyboard in between).
        internal void TakeControls()
        {
            if (_controller == null)
            {
                _controller = Player.GetComponent<PlayerController>();
                if (_controller != null)
                {
                    _controllerEnabled = _controller.enabled;
                    _controller.enabled = false;
                }
            }
            Drive(Vector3.zero);
        }

        internal void Drive(Vector3 move) =>
            Player.SetControls(move, false, false, false, false, false, false, false, false, false, false);

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

        internal void GiveStaminaRateBack()
        {
            if (!_staminaTaken)
            {
                return;
            }
            _staminaTaken = false;
            if (_hadStamina && ZoneSystem.instance != null)
            {
                ZoneSystem.instance.SetGlobalKey(GlobalKeys.StaminaRate, _stamina);
            }
        }

        // Noon for the screenshots (like console "tod 0.5").
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

        internal void Look(float pitch)
        {
            Player.m_lookPitch = pitch;
            Player.UpdateEyeRotation();
        }

        // Vanilla teleport (loads the far zones, waits for the floor). Result in box.
        internal IEnumerator Travel(Vector3 target, Quaternion rotation, Box result, float timeout)
        {
            var start = Time.time;
            while (!Player.TeleportTo(target, rotation, true))
            {
                if (Time.time - start > 6f)
                {
                    result.Detail = "Player.TeleportTo refused for 6 s";
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
            }
            yield return null;
            while (Player != null && Player.IsTeleporting())
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
            Try("controls", () =>
            {
                if (_controller != null)
                {
                    Drive(Vector3.zero);
                    _controller.enabled = _controllerEnabled;
                }
            });
            Try("look", () => Look(_pitch));
            Try("stamina rate", GiveStaminaRateBack);
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
                if (!Travelled || Back || Player == null)
                {
                    return;
                }
                // Test cut short: send the player home (vanilla finish the teleport after the test). Already
                // teleporting (cut during the trip out): same trip, new target.
                if (Player.IsTeleporting())
                {
                    Player.m_teleportTargetPos = Origin;
                    Player.m_teleportTargetRot = OriginRotation;
                }
                else
                {
                    Player.TeleportTo(Origin, OriginRotation, true);
                }
            });
        }

        private static void Try(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                SelfTest.Note(DiveName, $"restore {what} failed: {e.Message}");
            }
        }
    }

    private static IEnumerator RunDive()
    {
        var c = new Checks(DiveName);
        var player = Player.m_localPlayer;
        if (player == null || ZoneSystem.instance == null || WorldGenerator.instance == null
            || GameCamera.instance == null || EnvMan.instance == null || ZNet.instance == null)
        {
            SelfTest.Fail(DiveName, "no local player or no world");
            yield break;
        }
        var rig = new DiveRig(player);
        // Camera value before the dive: the finalizer must always put it back.
        var waterDistance = GameCamera.instance.m_minWaterDistance;
        try
        {
            ServerRules.TestRules = new DiveRules();
            Visuals.TestAllOn = true;
            NotePlayer(c, player);
            if (!FindDeepWater(rig.Origin, out var spot, out var waterDepth))
            {
                c.Note($"no water {F(MinWaterDepth)} m deep within 5 km of the spawn: live dive not tested");
                c.Report(" (live dive not tested: no deep water found)");
                yield break;
            }
            var level = ZoneSystem.instance.m_waterLevel;
            c.Note($"deep water at {F(spot)}, {F(Vector3.Distance(Flat(spot), Flat(rig.Origin)))} m from the spawn, "
                   + $"about {F(waterDepth)} m deep (WorldGenerator)");

            // ---- travel ----
            var trip = new Box();
            rig.Travelled = true;
            yield return rig.Travel(new Vector3(spot.x, level + 1.5f, spot.z), Quaternion.identity, trip, TravelTimeout);
            if (!c.Check(trip.Ok, "travel to the water: " + trip.Detail))
            {
                c.Report();
                yield break;
            }
            c.Note("travel out took " + trip.Detail);
            rig.TakeControls();
            rig.Noon();
            var waitStart = Time.time;
            while (!player.IsSwimming() && Time.time - waitStart < 12f)
            {
                yield return new WaitForSeconds(0.2f);
            }
            if (!c.Check(player.IsSwimming() && player.InWater(),
                    $"swimming after the trip (feet {F(player.transform.position.y)}, liquid {F(player.GetLiquidLevel())})"))
            {
                c.Report();
                yield break;
            }
            NoteWater(c, player);
            yield return new WaitForSeconds(3f);
            var rest = player.GetLiquidLevel() - player.m_swimDepth;
            c.Check(!DiveState.Diving, "floating at the surface: not diving");
            c.Note($"at the surface: feet {F(player.transform.position.y)}, rest height {F(rest)}, liquid "
                   + $"{F(player.GetLiquidLevel())}, ground {F(ZoneSystem.instance.GetGroundHeight(player.transform.position))}");
            NoteFog(c, "at the surface");

            // ---- pending = vanilla (G3, 4.3) ----
            ServerRules.TestPending = true;
            DiveInput.TestDown = true;
            yield return new WaitForSeconds(1.5f);
            // Rest height again now: it follow the waves (a storm move it well over 0.45 m in 1.5 s).
            rest = player.GetLiquidLevel() - player.m_swimDepth;
            c.Check(!DiveState.Diving && !DiveState.GravityOff && player.transform.position.y > rest - 0.45f,
                $"rules pending: Crouch does nothing (diving {DiveState.Diving}, feet {F(player.transform.position.y)}, "
                + $"rest {F(rest)})");
            ServerRules.TestPending = false;

            // ---- Crouch = down (G1) ----
            var startY = player.transform.position.y;
            var t0 = Time.time;
            while (player.transform.position.y > rest - 3f && Time.time - t0 < 6f)
            {
                yield return new WaitForFixedUpdate();
            }
            var fell = startY - player.transform.position.y;
            var took = Time.time - t0;
            c.Check(DiveState.Diving && DiveState.Deep && fell > 2f,
                $"Crouch dives: {F(fell)} m down in {F(took)} s (diving {DiveState.Diving}, deep {DiveState.Deep})");
            c.Note($"descent {F(fell / Mathf.Max(0.01f, took))} m/s average, own vertical speed {F(DiveState.Vy)}, swim speed "
                   + $"{F(DiveController.SwimSpeed(player))}, gravity on {player.m_body.useGravity}");

            // ---- release = hold depth ----
            DiveInput.TestDown = false;
            DiveInput.TestUp = false;
            yield return new WaitForSeconds(1.5f);
            var holdY = player.transform.position.y;
            yield return new WaitForSeconds(2f);
            var drift = player.transform.position.y - holdY;
            c.Check(DiveState.Diving && DiveState.Deep && Mathf.Abs(drift) < 0.3f,
                $"no key under water: holds depth (moved {F(drift)} m in 2 s, vertical speed {F(DiveState.Vy)})");

            // ---- camera under, fog, surface from below (added) ----
            // End of frame: after this frame's LateUpdate (camera and fog written), before the next SetEnv.
            yield return null;
            yield return new WaitForEndOfFrame();
            var camera = GameCamera.instance;
            var camPos = camera.transform.position;
            var camSurface = Floating.GetLiquidLevel(camPos, 1f, LiquidType.Water);
            c.Check(camSurface > -9000f && camPos.y < camSurface - 0.04f && DiveCamera.Lifted,
                $"camera under the surface (camera y {F(camPos.y)}, surface {F(camSurface)}, lifted {DiveCamera.Lifted})");
            c.Check(Mathf.Abs(camera.m_minWaterDistance - waterDistance) < 0.0001f,
                $"camera water distance never left changed ({F(camera.m_minWaterDistance)})");
            var wantDensity = Mathf.Max(UnderwaterView.BaseDensity,
                UnderwaterView.Density(RenderSettings.fogMode, Visuals.DefaultVisibility));
            c.Check(UnderwaterView.Active && UnderwaterView.FogApplied
                    && Near(RenderSettings.fogDensity, wantDensity, 0.0001f),
                $"under-water fog on (density {F(RenderSettings.fogDensity)}, want {F(wantDensity)})");
            c.Check(UnderwaterView.FlippedCount > 0,
                $"water surfaces turned for the view from below ({UnderwaterView.FlippedCount} turned, "
                + $"{UnderwaterView.UnsafeCount} not safe)");
            NoteFog(c, "under water");
            c.Note($"camera near clip {F(camera.m_camera != null ? camera.m_camera.nearClipPlane : -1f)}, water clipping "
                   + $"{camera.m_waterClipping}, eye depth {F(player.GetLiquidLevel() - player.m_eye.position.y)}, "
                   + $"surfaces turned {UnderwaterView.FlippedCount}, not safe {UnderwaterView.UnsafeCount}");
            SelfTest.Screenshot(DiveName, "underwater");
            yield return null;
            yield return null;
            rig.Look(-50f);
            yield return null;
            yield return null;
            yield return null;
            SelfTest.Screenshot(DiveName, "underwater-looking-up");
            yield return null;
            yield return null;
            rig.Look(0f);

            // ---- sideways input under water = no sideways move (G2) ----
            // Open ocean, no ship or build: the ceiling rule (D5) must never fire here (T13 cover a real ceiling).
            var before = Flat(player.transform.position);
            var moveEnd = Time.time + 2f;
            var blocked = false;
            while (Time.time < moveEnd)
            {
                rig.Drive(Vector3.forward);
                yield return new WaitForFixedUpdate();
                blocked |= DiveState.BlockedAbove;
            }
            rig.Drive(Vector3.zero);
            var sideways = Vector3.Distance(before, Flat(player.transform.position));
            c.Check(!blocked, "open water: nothing solid above the diver");
            c.Check(sideways < 0.4f, $"forward input under water: no sideways move ({F(sideways)} m in 2 s)");

            // ---- Jump refused while diving (a touch of the sea floor would make it a real jump) ----
            var jumpTimer = player.m_jumpTimer;
            var velocity = player.m_body.linearVelocity;
            player.m_hitWorldTime = 0f;
            player.Jump();
            c.Check(player.m_jumpTimer == jumpTimer && Mathf.Abs(player.m_body.linearVelocity.y - velocity.y) < 0.01f,
                "Jump while diving next to something solid: no jump");

            // ---- still under water = stamina drain (G4) ----
            rig.TakeStaminaRate();
            yield return new WaitForSeconds(0.5f);
            var s0 = player.GetStamina();
            yield return new WaitForSeconds(2f);
            var s1 = player.GetStamina();
            var skill = player.GetSkills().GetSkillFactor(Skills.SkillType.Swim);
            var expected = Mathf.Lerp(player.m_swimStaminaDrainMinSkill, player.m_swimStaminaDrainMaxSkill, skill)
                           * Game.m_moveStaminaRate * Game.m_staminaRate;
            c.Check(s0 - s1 > 1f, $"still under water: stamina drains ({F(s0)} -> {F(s1)} in 2 s)");
            c.Note($"drain still under water {F((s0 - s1) / 2f)}/s; vanilla swim formula without gear gives "
                   + $"{F(expected)}/s (Swim factor {F(skill)}, move stamina rate {F(Game.m_moveStaminaRate)}, "
                   + $"stamina rate {F(Game.m_staminaRate)})");

            // ---- Jump = up, back to vanilla at the surface ----
            DiveInput.TestUp = true;
            t0 = Time.time;
            while (DiveState.Diving && Time.time - t0 < 10f)
            {
                yield return new WaitForFixedUpdate();
            }
            var riseTook = Time.time - t0;
            DiveInput.TestUp = false;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            var restNow = player.GetLiquidLevel() - player.m_swimDepth;
            c.Check(!DiveState.Diving && player.transform.position.y > restNow - 0.6f && player.m_body.useGravity,
                $"Jump rises and hands back to normal swimming at the surface in {F(riseTook)} s (feet "
                + $"{F(player.transform.position.y)}, rest {F(restNow)}, gravity {player.m_body.useGravity})");

            // ---- still at the surface = no drain (vanilla) ----
            yield return new WaitForSeconds(2f);
            var s2 = player.GetStamina();
            yield return new WaitForSeconds(2f);
            var s3 = player.GetStamina();
            c.Check(Mathf.Abs(s3 - s2) < 0.2f && !DiveState.Diving,
                $"still at the surface: no drain ({F(s2)} -> {F(s3)} in 2 s)");

            // ---- Crouch + Jump held at the surface = a dive with nothing to do: no drain (G4) ----
            // Not deep + target 0 = vertical left to vanilla buoyancy and gravity: diver ride the waves, so a wave never
            // make him deep. Deep and vanilla hand-back sampled every physics tick of the window (each tick can change
            // them). Deep anyway (a storm wave faster than vanilla buoyancy) = drain on purpose: NOTE, check skipped.
            DiveInput.TestDown = true;
            DiveInput.TestUp = true;
            yield return new WaitForSeconds(1f);
            var heldDiving = DiveState.Diving;
            var s4 = player.GetStamina();
            var ticks = 0;
            var deepTicks = 0;
            var vanillaTicks = 0;
            var maxUnder = float.NegativeInfinity;
            var maxSpeed = 0f;
            var heldEnd = Time.time + 2f;
            while (Time.time < heldEnd)
            {
                yield return new WaitForFixedUpdate();
                ticks++;
                if (DiveState.Deep)
                {
                    deepTicks++;
                }
                if (DiveState.Diving && DiveState.HandedBack && player.m_body.useGravity)
                {
                    vanillaTicks++;
                }
                maxUnder = Mathf.Max(maxUnder, DiveState.RestY - player.transform.position.y);
                maxSpeed = Mathf.Max(maxSpeed, Mathf.Abs(DiveState.Vy));
            }
            var s5 = player.GetStamina();
            // Crouch off: not deep, so the dive end next tick; Jump kept until then anyway.
            DiveInput.TestDown = false;
            var releaseEnd = Time.time + 5f;
            while (DiveState.Diving && Time.time < releaseEnd)
            {
                yield return new WaitForFixedUpdate();
            }
            DiveInput.TestUp = false;
            var ride = $"deep {deepTicks} of {ticks} ticks, vertical left to vanilla {vanillaTicks} of {ticks}, feet at most "
                       + $"{F(maxUnder)} m under the rest height, own vertical speed at most {F(maxSpeed)}";
            c.Note("Crouch and Jump held at the surface: " + ride);
            if (deepTicks > 0)
            {
                c.Note($"Crouch and Jump held at the surface: a wave still took the diver under, no-drain check skipped "
                       + $"({F(s4)} -> {F(s5)})");
            }
            else
            {
                c.Check(heldDiving && ticks > 0 && vanillaTicks == ticks,
                    $"Crouch and Jump held at the surface: vertical left to vanilla buoyancy every tick (diving "
                    + $"{heldDiving}, {ride})");
                c.Check(heldDiving && Mathf.Abs(s5 - s4) < 0.2f,
                    $"Crouch and Jump held at the surface: no drain ({F(s4)} -> {F(s5)} in 2 s, diving {heldDiving}, {ride})");
            }
            yield return new WaitForSeconds(1f);
            c.Check(!DiveState.Diving, "keys released at the surface: back to normal swimming");
            rig.GiveStaminaRateBack();

            // ---- view back ----
            camPos = camera.transform.position;
            camSurface = Floating.GetLiquidLevel(camPos, 1f, LiquidType.Water);
            c.Check(!UnderwaterView.Active && !UnderwaterView.FogApplied && UnderwaterView.FlippedCount == 0
                    && !DiveCamera.Lifted && camPos.y > camSurface,
                $"at the surface: camera above water, fog and surfaces back (camera y {F(camPos.y)}, surface {F(camSurface)})");
            SelfTest.Screenshot(DiveName, "surface");
            yield return null;
            yield return null;

            // ---- home ----
            ClearOverrides();
            trip = new Box();
            yield return rig.Travel(rig.Origin, rig.OriginRotation, trip, TravelTimeout);
            rig.Back = trip.Ok;
            c.Check(trip.Ok, "travel back to the spawn: " + trip.Detail);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // Nearest deep water (WorldGenerator heights, no zone needed), rings around the spawn; not Ashlands (hot water) nor
    // Deep North; the 4 points 12 m around also deep (a basin, not a trench).
    private static bool FindDeepWater(Vector3 origin, out Vector3 spot, out float depth)
    {
        var gen = WorldGenerator.instance;
        var level = ZoneSystem.instance.m_waterLevel;
        for (var r = 100f; r <= 5000f; r += 50f)
        {
            for (var a = 0; a < 360; a += 10)
            {
                var x = origin.x + Mathf.Sin(a * Mathf.Deg2Rad) * r;
                var z = origin.z + Mathf.Cos(a * Mathf.Deg2Rad) * r;
                if (x * x + z * z > 9500f * 9500f)
                {
                    continue;
                }
                var h = gen.GetHeight(x, z);
                if (h > level - MinWaterDepth)
                {
                    continue;
                }
                var biome = gen.GetBiome(x, z);
                if (biome == Heightmap.Biome.AshLands || biome == Heightmap.Biome.DeepNorth)
                {
                    continue;
                }
                if (gen.GetHeight(x + 12f, z) > level - MinWaterDepth + 4f || gen.GetHeight(x - 12f, z) > level - MinWaterDepth + 4f
                    || gen.GetHeight(x, z + 12f) > level - MinWaterDepth + 4f || gen.GetHeight(x, z - 12f) > level - MinWaterDepth + 4f)
                {
                    continue;
                }
                spot = new Vector3(x, level, z);
                depth = level - h;
                return true;
            }
        }
        spot = Vector3.zero;
        depth = 0f;
        return false;
    }

    private static void NotePlayer(Checks c, Player p)
    {
        var col = p.m_collider;
        var body = p.m_body;
        var camera = GameCamera.instance;
        c.Note($"player: swimDepth {F(p.m_swimDepth)}, swimSpeed {F(p.m_swimSpeed)}, swimTurnSpeed {F(p.m_swimTurnSpeed)}, "
               + $"swimAcceleration {F(p.m_swimAcceleration)}, swim drain {F(p.m_swimStaminaDrainMinSkill)} (skill 0) to "
               + $"{F(p.m_swimStaminaDrainMaxSkill)} (skill 100) per s, staminaRegenDelay {F(p.m_staminaRegenDelay)}, collider "
               + $"{(col != null ? $"height {F(col.height)} radius {F(col.radius)} centre {F(col.center)}" : "none")}, eye "
               + $"{F(p.m_eye.position.y - p.transform.position.y)} m and head {F(p.GetHeadPoint().y - p.transform.position.y)} m "
               + $"above the feet, body mass {(body != null ? F(body.mass) : "-")}, drag {(body != null ? F(body.linearDamping) : "-")}, "
               + $"sleep threshold {(body != null ? F(body.sleepThreshold) : "-")}, fixed dt {F(Time.fixedDeltaTime)}");
        c.Note($"camera: minWaterDistance {F(camera.m_minWaterDistance)}, near clip {F(camera.m_nearClipPlaneMin)}..{F(camera.m_nearClipPlaneMax)}, "
               + $"max distance {F(camera.m_maxDistance)}");
    }

    private static void NoteWater(Checks c, Player p)
    {
        var feet = p.transform.position;
        WaterVolume here = null;
        foreach (var v in WaterVolume.Instances)
        {
            if (v != null && v.m_collider != null)
            {
                var b = v.m_collider.bounds;
                if (feet.x >= b.min.x && feet.x <= b.max.x && feet.z >= b.min.z && feet.z <= b.max.z)
                {
                    here = v;
                    break;
                }
            }
        }
        var ground = ZoneSystem.instance.GetGroundHeight(feet);
        c.Note($"water here: surface {F(p.GetLiquidLevel())}, ground {F(ground)}, depth {F(p.GetLiquidLevel() - ground)}");
        if (here != null)
        {
            var b = here.m_collider.bounds;
            c.Note($"volume trigger bottom {F(b.min.y)} vs ground {F(ground)}: "
                   + (b.min.y <= ground ? "reaches the sea floor" : "does NOT reach the sea floor here"));
            NoteVolume(c, here, "volume at the dive spot");
        }
        else
        {
            c.Note("no water volume found around the dive spot");
        }
    }

    private static void NoteFog(Checks c, string where)
    {
        c.Note($"fog {where}: on {RenderSettings.fog}, mode {RenderSettings.fogMode}, density {F(RenderSettings.fogDensity)}, "
               + $"colour {F(RenderSettings.fogColor)}, start {F(RenderSettings.fogStartDistance)}, end "
               + $"{F(RenderSettings.fogEndDistance)}; view base density {F(UnderwaterView.BaseDensity)}, base colour "
               + $"{F(UnderwaterView.BaseColor)}, camera depth {F(UnderwaterView.LastDepth)}");
        // Sun-side fog colour (the olive haze looking up at noon when it stay vanilla).
        var sunRaw = Shader.GetGlobalVector(UnderwaterView.SunFogId);
        var env = EnvMan.instance;
        c.Note($"sun fog {where}: _SunFogColor {F(Shader.GetGlobalColor(UnderwaterView.SunFogId))} (raw {F(sunRaw)}), "
               + $"same as the fog colour {NearRaw(sunRaw, Raw(RenderSettings.fogColor))}; vanilla EnvMan sun fog "
               + $"{(env != null ? F(env.GetSunFogColor()) : "-")}; view base raw {F(UnderwaterView.BaseSunFog)}");
    }
#endif
}
