using MC.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace MC.Combat.SneakAmbushMod;

// Me = look of a Smoke Screen cloud: own particle system built by code, material borrowed from vanilla smoke
// (SmokeContent.SmokeMaterial). Local only, no gameplay role, never on dedicated server (no graphics). Separate root
// object at cloud base (cloud never move); cloud stop its emission StopLead before its end and destroy it after last
// puff faded. Look follow the hiding: puff near full from birth (thick by ActivationDelay), full until 60% of life,
// so last puffs thin out when hiding end and are gone about 3.6 s later.
internal static class SmokeVisual
{
    internal const float MinLifetime = 4f;
    internal const float MaxLifetime = 6f;
    private const float FullUntil = 0.6f;       // share of life a puff stay fully thick
    internal const float StopLead = MinLifetime * FullUntil;
    private const int MaxParticles = 80;
    private const float BurstCount = 40f;       // quick build-up at impact
    private const float RatePerSecond = 14f;    // ~70 alive at 5 s average life

    private static bool _noMaterialLogged;

    // Dedicated server (server DLL say so) or any game without graphics.
    internal static bool Headless
    {
        get
        {
            var net = ZNet.instance;
            return (net != null && net.IsDedicated()) || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
        }
    }

    // Null = no smoke look (no material found; SmokeContent warned once).
    internal static ParticleSystem Create(Vector3 basePoint, float radius, float height)
    {
        var material = SmokeContent.SmokeMaterial;
        if (material == null)
        {
            if (!_noMaterialLogged)
            {
                _noMaterialLogged = true;
                Log.Debug("Smoke Screen cloud without smoke look (no vanilla smoke material).");
            }
            return null;
        }

        // Me build it inactive: duration and friends change only while not playing.
        var go = new GameObject(SmokeContent.CloudName + "_visual");
        go.SetActive(false);
        go.transform.position = basePoint + Vector3.up * Mathf.Min(1f, height * 0.25f);
        go.transform.rotation = Quaternion.identity;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(MinLifetime, MaxLifetime);
        main.startSize = new ParticleSystem.MinMaxCurve(3f, 4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.55f, 0.55f, 0.55f, 0.75f);
        main.maxParticles = MaxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.02f; // slow drift up

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = RatePerSecond;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, BurstCount) });

        // Flat disc of the cloud's radius; size and drift fill the height.
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = Mathf.Max(0.5f, radius - 1f);
        shape.radiusThickness = 1f;
        shape.rotation = new Vector3(90f, 0f, 0f);

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.1f, Mathf.Max(0.2f, height / MaxLifetime * 0.6f));
        velocity.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

        // Near full at birth, full within ~0.4 s (burst thick before ActivationDelay), fade out after FullUntil.
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(1f, FullUntil),
                new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(gradient);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        if (renderer == null)
        {
            renderer = go.AddComponent<ParticleSystemRenderer>();
        }
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        go.SetActive(true);
        ps.Play(true);
        return ps;
    }
}
