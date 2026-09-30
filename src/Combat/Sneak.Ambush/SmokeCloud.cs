using System;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.SneakAmbushMod;

// Me = one Smoke Screen cloud (prefab MC_SmokeScreen_cloud: ZNetView persistent + me). Projectile.SpawnOnHit make me
// on thrower's game and call Setup (IProjectile). Shape = vertical cylinder on ground or water, stored in ZDO with
// rules of the throw (shape never change mid-cloud, also for games whose settings lag):
//   <guid>.CloudStart     long   network time of impact, ms
//   <guid>.CloudDuration  float  seconds from impact to end of hiding (build-up inside)
//   <guid>.CloudRadius    float  metres
//   <guid>.CloudHeight    float  metres above base
// Base = ZDO position. Timeline (shared network time): ready once CloudStart known -> active from start +
// ActivationDelay (cut to leave MinHideSeconds of hiding) to start + duration -> fade 3 s (visual only) -> ended
// (inert: no visual, never hide). Blind never outlive me: at most until start + duration + fade (registry empty =
// blinds forgotten anyway).
// Persistent ZDO: vanilla hand me to a game near me when thrower leave (teleport, death, disconnect).
// Expiry: only owner destroy (ZDO gone for all). Other game still holding me 5 s after fade claim then destroy
// (owner without the mod). Expiry run whatever feature state. Blind burst: once per game, first frame active (or on
// arrival while still in blind window), only while feature active.
// Every entry point game call catch own errors (Projectile.OnHit and ZNetScene loops must never see a throw).
internal sealed class SmokeCloud : MonoBehaviour, IProjectile
{
    internal static readonly int StartKey = (ModInfo.Guid + ".CloudStart").GetStableHashCode();
    internal static readonly int DurationKey = (ModInfo.Guid + ".CloudDuration").GetStableHashCode();
    internal static readonly int RadiusKey = (ModInfo.Guid + ".CloudRadius").GetStableHashCode();
    internal static readonly int HeightKey = (ModInfo.Guid + ".CloudHeight").GetStableHashCode();

    internal const float FadeSeconds = 3f;
    internal const float ClaimDelay = 5f;       // non-owner wait after fade before it claim and destroy
    internal const float MinHideSeconds = 1f;   // ActivationDelay cut so cloud always hide at least this long
    private const float GroundProbeUp = 1f;
    // Long: projectile time run out in mid-air (m_spawnOnTtl, 4 s flight) can be far above ground (cliff, mountain).
    // Ray give closest hit, so normal ground hit same as short probe.
    private const float GroundProbeDown = 1000f;

    private static int _groundMask;

    private ZNetView _nview;
    private bool _ready;
    private double _start;
    private float _duration;
    private float _radius;
    private float _height;
    private Vector3 _base;
    private bool _blindDone;
    private bool _destroying;
    private bool _visualTried;
    private ParticleSystem _visual;
    private bool _visualStopped;

    internal bool Ready => _ready;
    internal double StartTime => _start;
    internal float Duration => _duration;
    internal float Radius => _radius;
    internal float Height => _height;
    internal Vector3 Base => _base;
    internal double EndTime => _start + _duration;
    internal ZNetView View => _nview;

    // Hides now? (ready, built up, not over).
    internal bool IsActive(double now, AmbushRules rules) =>
        _ready && now >= _start + BuildUp(_duration, rules) && now < _start + _duration;

    // Pure (self test): build-up time of a cloud. ActivationDelay count inside duration; cut so cloud always hide
    // MinHideSeconds (duration 3 + delay 3 would never hide).
    internal static float BuildUp(float duration, AmbushRules rules) =>
        Mathf.Clamp(rules.ActivationDelay, 0f, Mathf.Max(0f, duration - MinHideSeconds));

    // Pure (self test): end of the burst blind, shared network time. Never after cloud gone (duration + fade): the
    // registry forget blinds when last cloud leave, so a longer BlindSeconds would lie.
    internal static double BlindEnd(double start, float duration, AmbushRules rules) =>
        start + Math.Min(rules.BlindSeconds, duration + FadeSeconds);

    internal bool Contains(Vector3 p)
    {
        var dx = p.x - _base.x;
        var dz = p.z - _base.z;
        return dx * dx + dz * dz <= _radius * _radius && p.y >= _base.y - SmokeRegistry.BelowBase
               && p.y <= _base.y + _height;
    }

    // Cheap "segment cross cylinder": closest point of segment to axis (horizontal plane) within radius, its height
    // inside the cylinder.
    internal bool SegmentCrosses(Vector3 a, Vector3 b)
    {
        var abx = b.x - a.x;
        var abz = b.z - a.z;
        var lengthSq = abx * abx + abz * abz;
        var t = 0f;
        if (lengthSq > 1e-6f)
        {
            t = Mathf.Clamp01(((_base.x - a.x) * abx + (_base.z - a.z) * abz) / lengthSq);
        }
        var px = a.x + abx * t - _base.x;
        var pz = a.z + abz * t - _base.z;
        if (px * px + pz * pz >= _radius * _radius)
        {
            return false;
        }
        var y = a.y + (b.y - a.y) * t;
        return y >= _base.y - SmokeRegistry.BelowBase && y <= _base.y + _height;
    }

    // Point near the burst (blind): within margin of the cylinder, sideways and in height.
    internal bool NearBurst(Vector3 p, float margin)
    {
        var dx = p.x - _base.x;
        var dz = p.z - _base.z;
        var reach = _radius + margin;
        return dx * dx + dz * dz <= reach * reach && p.y >= _base.y - SmokeRegistry.BelowBase - margin
               && p.y <= _base.y + _height + margin;
    }

    // Debug self tests and console-style spawns: new cloud at a point, set up now with the rules in force.
    internal static SmokeCloud Spawn(Vector3 position)
    {
        var prefab = SmokeContent.CloudPrefab;
        if (prefab == null || ZNetScene.instance == null)
        {
            return null;
        }
        var go = Instantiate(prefab, position, Quaternion.identity);
        var cloud = go.GetComponent<SmokeCloud>();
        if (cloud != null)
        {
            cloud.SetupAt(position, RulesForNewCloud());
        }
        return cloud;
    }

    private void Awake()
    {
        try
        {
            _nview = GetComponent<ZNetView>();
        }
        catch (Exception e)
        {
            PatchGuard.Report("SmokeCloud.Awake", e);
        }
    }

    private void OnEnable()
    {
        try
        {
            SmokeRegistry.Add(this);
        }
        catch (Exception e)
        {
            PatchGuard.Report("SmokeCloud.OnEnable", e);
        }
    }

    private void OnDisable()
    {
        try
        {
            SmokeRegistry.Remove(this);
        }
        catch (Exception e)
        {
            PatchGuard.Report("SmokeCloud.OnDisable", e);
        }
    }

    // Visual live apart from me (never moves): me gone = stop emitting, smoke fade out alone, then go.
    private void OnDestroy()
    {
        try
        {
            if (_visual != null)
            {
                _visual.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(_visual.gameObject, SmokeVisual.MaxLifetime + 0.5f);
                _visual = null;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("SmokeCloud.OnDestroy", e);
        }
    }

    // Cloud made another way than a Smoke Screen hit (console "spawn MC_SmokeScreen_cloud"): owner set it up here
    // with the rules in force. Projectile clouds got Setup before Start: key already there.
    private void Start()
    {
        try
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner())
            {
                return;
            }
            if (_nview.GetZDO().GetLong(StartKey, 0L) == 0L)
            {
                SetupAt(transform.position, RulesForNewCloud());
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("SmokeCloud.Start", e);
        }
    }

    // IProjectile: Projectile.SpawnOnHit call me on thrower's game right after Instantiate. A throw here would stop
    // OnHit before m_didHit, projectile fly on and spawn a cloud every physics step: so me catch everything, destroy
    // me on failure and return normally.
    public void Setup(Character owner, Vector3 velocity, float hitNoise, HitData hitData, ItemDrop.ItemData item,
        ItemDrop.ItemData ammo)
    {
        try
        {
            SetupAt(transform.position, RulesForNewCloud());
        }
        catch (Exception e)
        {
            PatchGuard.Report("SmokeCloud.Setup", e);
            DestroySafely();
        }
    }

    // IProjectile: no tooltip (a constant: nothing here can throw).
    public string GetTooltipString(int itemQuality) => "";

    // Owner: base on ground or water surface, ZDO keys with given rules.
    private void SetupAt(Vector3 position, AmbushRules rules)
    {
        if (_nview == null || !_nview.IsValid() || !_nview.IsOwner())
        {
            return;
        }
        var basePoint = FindBase(position);
        transform.position = basePoint;
        transform.rotation = Quaternion.identity;
        var zdo = _nview.GetZDO();
        zdo.SetPosition(basePoint);
        var now = SmokeRegistry.Now;
        var startMs = (long)(now * 1000d);
        if (startMs == 0L)
        {
            startMs = 1L; // zero = "not set" in ZDO
        }
        zdo.Set(StartKey, startMs);
        zdo.Set(DurationKey, rules.CloudDuration);
        zdo.Set(RadiusKey, rules.CloudRadius);
        zdo.Set(HeightKey, rules.CloudHeight);
        Load(zdo);
    }

    // Pending (client still wait for server) = throws refused; a cloud still made then use the default shape.
    private static AmbushRules RulesForNewCloud()
    {
        var rules = ServerRules.Current;
        return rules.IsPending ? AmbushRules.Default : rules;
    }

    // Ray down on solid layers (long: mid-air time-out); water surface wins when higher (cloud sit on water, not on
    // sea floor). Nothing below (dungeon void) = stay where it burst.
    private static Vector3 FindBase(Vector3 position)
    {
        if (_groundMask == 0)
        {
            _groundMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
        }
        var y = position.y;
        var found = false;
        if (Physics.Raycast(position + Vector3.up * GroundProbeUp, Vector3.down, out var hit,
                GroundProbeUp + GroundProbeDown, _groundMask, QueryTriggerInteraction.Ignore))
        {
            y = hit.point.y;
            found = true;
        }
        // Burst on water: projectile a little under or over the surface; sample a bit lower too.
        var water = Mathf.Max(Floating.GetLiquidLevel(position), Floating.GetLiquidLevel(position + Vector3.down));
        if (water > -1000f && (!found || water > y))
        {
            y = water;
        }
        return new Vector3(position.x, y, position.z);
    }

    // ZDO -> fields. Never trust: other games wrote it. Out of range = clamped, junk = default.
    private void Load(ZDO zdo)
    {
        var startMs = zdo.GetLong(StartKey, 0L);
        if (startMs == 0L)
        {
            return;
        }
        var d = AmbushRules.Default;
        _start = startMs / 1000d;
        _duration = Sane(zdo.GetFloat(DurationKey, d.CloudDuration), AmbushRules.CloudDurationMin,
            AmbushRules.CloudDurationMax, d.CloudDuration);
        _radius = Sane(zdo.GetFloat(RadiusKey, d.CloudRadius), AmbushRules.CloudSizeMin, AmbushRules.CloudSizeMax,
            d.CloudRadius);
        _height = Sane(zdo.GetFloat(HeightKey, d.CloudHeight), AmbushRules.CloudSizeMin, AmbushRules.CloudSizeMax,
            d.CloudHeight);
        _base = zdo.GetPosition();
        _ready = true;
    }

    private static float Sane(float value, float min, float max, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return fallback;
        }
        return Mathf.Clamp(value, min, max);
    }

    private void Update()
    {
        try
        {
            if (_destroying || _nview == null || !_nview.IsValid())
            {
                return;
            }
            if (!_ready)
            {
                Load(_nview.GetZDO());
                if (!_ready)
                {
                    return;
                }
            }
            var now = SmokeRegistry.Now;
            var end = EndTime;
            UpdateVisual(now, end);
            if (!_blindDone && now < end)
            {
                TryBlind(now);
            }
            Expire(now, end);
        }
        catch (Exception e)
        {
            PatchGuard.Report("SmokeCloud.Update", e);
        }
    }

    // Burst blind (design 2.11), for creatures THIS game owns: non-boss, alerted MonsterAI chasing a player whose
    // traced point is near the burst. Once per game per cloud; creature ownership decide who run it.
    private void TryBlind(double now)
    {
        if (!Plugin.FeatureActive)
        {
            return;
        }
        var rules = ServerRules.Current;
        if (rules.IsPending || now < _start + BuildUp(_duration, rules))
        {
            return;
        }
        _blindDone = true;
        var until = BlindEnd(_start, _duration, rules);
        if (rules.BlindSeconds <= 0f || now >= until)
        {
            return;
        }
        var blinded = 0;
        foreach (var ai in BaseAI.BaseAIInstances)
        {
            if (!(ai is MonsterAI monster) || monster.m_nview == null || !monster.m_nview.IsValid()
                || !monster.m_nview.IsOwner())
            {
                continue;
            }
            var creature = monster.m_character;
            // Only aggroed (alerted) chasers: one that only noticed you and walk over is not blinded (design 2.11).
            if (creature == null || creature.IsBoss() || creature.IsDead() || !monster.IsAlerted())
            {
                continue;
            }
            var target = monster.m_targetCreature as Player;
            if (target == null)
            {
                continue;
            }
            if (!NearBurst(SmokeRegistry.TracedPoint(target), rules.BlindMargin))
            {
                continue;
            }
            AiMemory.Blind(monster.transform, until);
            blinded++;
        }
        if (blinded > 0)
        {
            Log.Debug($"Smoke Screen burst: {blinded} creature(s) chasing a player nearby lose their sight for "
                      + $"{until - now:0.#} s.");
        }
    }

    private void UpdateVisual(double now, double end)
    {
        if (_visual == null)
        {
            if (_visualTried || now >= end)
            {
                return;
            }
            _visualTried = true;
            if (SmokeVisual.Headless)
            {
                return;
            }
            _visual = SmokeVisual.Create(_base, _radius, _height);
            return;
        }
        // Stop emitting StopLead before end: last puffs stay thick until about the end, then fade (look follow the
        // hiding, not linger thick seconds after).
        if (!_visualStopped && now >= end - SmokeVisual.StopLead)
        {
            _visualStopped = true;
            _visual.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    // Owner: gone after fade. Other game: 5 s later claim and destroy (owner may not have the mod).
    private void Expire(double now, double end)
    {
        var fadeEnd = end + FadeSeconds;
        if (now < fadeEnd)
        {
            return;
        }
        if (_nview.IsOwner())
        {
            DestroySafely();
        }
        else if (now >= fadeEnd + ClaimDelay)
        {
            _nview.ClaimOwnership();
            DestroySafely();
        }
    }

    private void DestroySafely()
    {
        if (_destroying)
        {
            return;
        }
        _destroying = true;
        try
        {
            var scene = ZNetScene.instance;
            if (scene != null && _nview != null && _nview.IsValid())
            {
                scene.Destroy(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("SmokeCloud.Destroy", e);
        }
    }
}
