using System;
using MC.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, status, live on/off. Patches in Patches/ apply only while Active (HeightmapBuilder
// prefix stay on from game start, see HeightmapBuilderPatches). No Awake/Start/Update/OnDestroy here (they hide
// ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me draw world all the way out: far terrain = level-of-detail quadtree of game's own distant heightmaps
// (LodTerrainManager), far trees, rocks and buildings from object data this game hold (DistantObjectManager), far
// sea, thinner clear-weather fog. Client only: me only draw, nothing sent, nothing saved in world.
internal sealed partial class Plugin : ModPlugin
{
    internal static Plugin Instance { get; private set; }
    internal static DHConfig Cfg { get; private set; }

#if DEBUG
    // Self test only, in memory (see LocalBlocker). Null = nothing.
    internal static string TestBlocker;
#endif

    protected override void BindConfig()
    {
        Instance = this;
        Cfg = new DHConfig(Config);
        Log.Info($"Finest tiles {Cfg.BaseTileSize.Value} m at {Cfg.EffectiveVertexSpacing:0.##} m spacing "
                 + $"({Cfg.TileWidth}x{Cfg.TileWidth} quads), {Cfg.LodLevels.Value} LOD levels configured.");
        if (Mathf.Abs(Cfg.EffectiveVertexSpacing - Cfg.BaseVertexSpacing.Value) > 0.01f * Cfg.BaseVertexSpacing.Value)
        {
            Log.Warning($"BaseVertexSpacing {Cfg.BaseVertexSpacing.Value} m is not reachable with BaseTileSize "
                        + $"{Cfg.BaseTileSize.Value} m (tiles are limited to 8..250 quads per edge); using "
                        + $"{Cfg.EffectiveVertexSpacing:0.##} m.");
        }
    }

    // No graphics device (batch mode, server start through valheim.exe): nothing to draw. Other far-terrain mod
    // loaded: me stand aside (ForeignMods).
    protected override string LocalBlocker()
    {
#if DEBUG
        // Self test say "something block me": framework then run its real turn-off and turn-on path.
        if (!string.IsNullOrEmpty(TestBlocker))
        {
            return TestBlocker;
        }
#endif
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            return "Inactive: no graphics device (dedicated server or batch mode), nothing to draw.";
        }
        return ForeignMods.BlockerText();
    }

    protected override void OnActivated()
    {
        TerrainLink.AttachIfInWorld();
        DhCommand.Register();
        SelfTests.Register();
    }

    // Patches still on during this call (also at game quit). Each step alone: one failure never skip the rest.
    protected override void OnDeactivated()
    {
        // Conditional method (gone in Release): no delegate to it, so own try instead of Step().
        try
        {
            SelfTests.Unregister();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Plugin.OnDeactivated SelfTests.Unregister", e);
        }
        Step("DhCommand.Unregister", DhCommand.Unregister);
        Step("TerrainLink.Detach", TerrainLink.Detach);
    }

    private static void Step(string site, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Plugin.OnDeactivated " + site, e);
        }
    }
}
