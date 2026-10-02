using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Me = typed view over BepInEx config file.
internal sealed class DHConfig
{
    // LOD layout
    public readonly ConfigEntry<float> BaseTileSize;
    public readonly ConfigEntry<float> BaseVertexSpacing;
    public readonly ConfigEntry<int> LodLevels;
    public readonly ConfigEntry<float> SplitFactor;
    public readonly ConfigEntry<float> SplitHysteresis;
    public readonly ConfigEntry<float> ViewDistance;
    public readonly ConfigEntry<float> WorldRadius;
    public readonly ConfigEntry<bool> FillCracks;
    public readonly ConfigEntry<float> CrackFillDepth;
    public readonly ConfigEntry<float> ExactMaxSpacing;
    public readonly ConfigEntry<float> NearTerrainOffset;
    public readonly ConfigEntry<float> LodHideDistance;
    public readonly ConfigEntry<string> TerrainMaterial;
    public readonly ConfigEntry<string> FarTerrainDraw;

    // Streaming / performance
    public readonly ConfigEntry<int> MaxBuildsInFlight;
    public readonly ConfigEntry<int> MaxMeshBuildsPerFrame;
    public readonly ConfigEntry<float> UpdateInterval;
    public readonly ConfigEntry<float> UpdateStepDistance;
    public readonly ConfigEntry<bool> WaitForZones;

    // Rendering
    public readonly ConfigEntry<bool> RaiseCameraFarClip;
    public readonly ConfigEntry<float> CameraFarClip;
    public readonly ConfigEntry<float> FogDensityMultiplier;
    public readonly ConfigEntry<float> FogClearDensity;
    public readonly ConfigEntry<float> FogStormDensity;
    public readonly ConfigEntry<bool> KeepWetWeatherFog;
    public readonly ConfigEntry<bool> RealTerrainFadeFix;
    public readonly ConfigEntry<bool> FarWater;
    public readonly ConfigEntry<float> FarWaterInnerRadius;
    public readonly ConfigEntry<float> FarWaterGloss;
    public readonly ConfigEntry<bool> FarWaterFoam;
    public readonly ConfigEntry<bool> FarWaterFog;

    // Distant objects
    public readonly ConfigEntry<bool> ObjectsEnabled;
    public readonly ConfigEntry<float> ObjectMeshDistance;
    public readonly ConfigEntry<float> ObjectFullDistance;
    public readonly ConfigEntry<float> ObjectThinDistance;
    public readonly ConfigEntry<float> ObjectFarDistance;
    public readonly ConfigEntry<int> ObjectThinFactor;
    public readonly ConfigEntry<float> ObjectThinScale;
    public readonly ConfigEntry<int> ObjectFarFactor;
    public readonly ConfigEntry<float> ObjectFarScale;
    public readonly ConfigEntry<float> PieceDistance;
    public readonly ConfigEntry<float> RockDistance;
    public readonly ConfigEntry<float> BigRockSize;
    public readonly ConfigEntry<float> BigTreeHeight;
    public readonly ConfigEntry<bool> SmallTreeCards;
    public readonly ConfigEntry<bool> DrawTrees;
    public readonly ConfigEntry<bool> DrawRocks;
    public readonly ConfigEntry<bool> DrawBushes;
    public readonly ConfigEntry<bool> DrawPieces;
    public readonly ConfigEntry<bool> DrawLogs;
    public readonly ConfigEntry<int> ImpostorResolution;
    public readonly ConfigEntry<float> ImpostorBakeBrightness;
    public readonly ConfigEntry<string> ImpostorShader;
    public readonly ConfigEntry<float> ObjectBuildBudgetMs;
    public readonly ConfigEntry<float> ObjectUpdateInterval;
    public readonly ConfigEntry<float> ObjectCacheSeconds;
    public readonly ConfigEntry<int> MaxVertsPerMesh;
    public readonly ConfigEntry<int> MaxVertsPerObject;
    public readonly ConfigEntry<bool> SnapObjectsToTerrain;
    public readonly ConfigEntry<bool> FarObjectWind;

    // Spyglass (MC Spyglass mod, see ViewBoost)
    public readonly ConfigEntry<bool> SpyglassDetail;
    public readonly ConfigEntry<float> SpyglassMaxBoost;

    // Debug
    public readonly ConfigEntry<bool> DebugLogging;

    // Section names. General (Enabled, Status) = framework's, not mine.
    public const string LodSection = "LOD layout";
    public const string StreamingSection = "Streaming";
    public const string RenderingSection = "Rendering";
    public const string ObjectsSection = "Objects";
    public const string SpyglassSection = "Spyglass";
    public const string LoggingSection = "Logging";

    // Me fire this after any setting change, with entry that changed. Each handler run alone (throw in one never
    // skip next).
    public event Action<ConfigEntryBase> Changed;

    // Next ConfigurationManager Order per section: first bound = 100 = top of its section
    // (CM sort by Order, high first).
    private readonly Dictionary<string, int> _nextOrder = new Dictionary<string, int>();

    // Quads per tile edge. Capped so tile stay under 65k vertex limit of 16-bit index mesh.
    public int TileWidth => Mathf.Clamp(Mathf.RoundToInt(BaseTileSize.Value / BaseVertexSpacing.Value), 8, 250);

    // Vertex spacing really used on finest level (not same as BaseVertexSpacing when TileWidth get clamped).
    public float EffectiveVertexSpacing => BaseTileSize.Value / TileWidth;

    // BepInEx config file under me (for generic get/set by name).
    public readonly ConfigFile File;

    public DHConfig(ConfigFile file)
    {
        File = file;
        BaseTileSize = Bind(LodSection, "BaseTileSize", 256f, new ConfigDescription(
            "Edge length in metres of the finest LOD tile. Each further level doubles it (vanilla uses 800 m regions at 10 m spacing). " +
            "Changing this rebuilds all terrain.",
            new AcceptableValueRange<float>(128f, 3200f)));

        BaseVertexSpacing = Bind(LodSection, "BaseVertexSpacing", 2.5f, new ConfigDescription(
            "Metres between terrain samples on the finest LOD level (real zones use 1 m, vanilla distant terrain 10 m). " +
            "Lower = more detail and more triangles. Each further level doubles it. " +
            "Tiles are limited to 250x250 quads, so very small values are rounded up. Changing this rebuilds all terrain.",
            new AcceptableValueRange<float>(1f, 64f)));

        LodLevels = Bind(LodSection, "LodLevels", 7, new ConfigDescription(
            "Number of LOD levels. Coarsest tile = BaseTileSize * 2^(levels-1); with 256 m and 7 levels that is 16.4 km, " +
            "enough to cover the whole 10.5 km world radius with a 2x2 root grid. Levels coarser than that are ignored, and " +
            "extra coarse levels are added automatically if the root grid would otherwise exceed 8x8 tiles. Changing this rebuilds all terrain.",
            new AcceptableValueRange<int>(1, 9)));

        SplitFactor = Bind(LodSection, "SplitFactor", 1f, new ConfigDescription(
            "A tile is subdivided into 4 finer tiles while the camera is closer than SplitFactor * tile size (Chebyshev distance). " +
            "1.0 keeps neighbouring tiles at most one LOD level apart (except at the edge of the area a raised MC spyglass sharpens, " +
            "where two or three levels can meet; FillCracks hides that seam). Lower values (0.5) use far fewer tiles.",
            new AcceptableValueRange<float>(0.25f, 3f)));

        SplitHysteresis = Bind(LodSection, "SplitHysteresis", 0.2f, new ConfigDescription(
            "A tile that is already subdivided stays subdivided until the camera is SplitFactor * (1 + SplitHysteresis) tile sizes away " +
            "(and, for the ViewDistance cut-off, (1 + SplitHysteresis) * ViewDistance away), so walking back and forth across a LOD " +
            "boundary does not destroy and rebuild tiles every time.",
            new AcceptableValueRange<float>(0f, 1f)));

        ViewDistance = Bind(LodSection, "ViewDistance", 22000f, new ConfigDescription(
            "Tiles farther than this (metres) from the camera are never subdivided. They are still drawn as one coarse tile, " +
            "so lowering this trades far detail for fewer tiles without ever leaving holes. The default refines everything; " +
            "fog usually limits what you can actually see.",
            new AcceptableValueRange<float>(1000f, 30000f)));

        WorldRadius = Bind(LodSection, "WorldRadius", 10500f, new ConfigDescription(
            "Tiles entirely outside this radius from the world origin are skipped, and the far sea ends here. The playable world " +
            "ends at 10500 m; with a world-size mod (for example Expand World Size) set its world radius plus edge size (up to 20000 m). " +
            "Changing this rebuilds all terrain.",
            new AcceptableValueRange<float>(1000f, 20000f)));

        FillCracks = Bind(LodSection, "FillCracks", true,
            "Also render each subdivided (parent) tile, lowered by CrackFillDepth, underneath its finer children. " +
            "Hides the sky-coloured cracks that appear where two LOD levels meet, at roughly +33% triangles.");

        CrackFillDepth = Bind(LodSection, "CrackFillDepth", 1.5f, new ConfigDescription(
            "How far (in multiples of the parent tile vertex spacing) parent tiles are lowered when FillCracks is on.",
            new AcceptableValueRange<float>(0.25f, 8f)));

        ExactMaxSpacing = Bind(LodSection, "ExactMaxSpacing", 6f, new ConfigDescription(
            "LOD levels whose vertex spacing is at most this many metres compute their heights exactly the way real zones do " +
            "(per 64 m zone, blending the four corner biomes), so the far terrain meets the real terrain without a step. " +
            "Coarser levels use the game's cheaper distant approximation. Exact tiles cost about 2 to 4 times more builder time.",
            new AcceptableValueRange<float>(0f, 64f)));

        NearTerrainOffset = Bind(LodSection, "NearTerrainOffset", 0.5f, new ConfigDescription(
            "Exact near tiles are drawn this many metres below their true height so they never z-fight with the real terrain " +
            "in the ring where both are drawn (about 180 to 200 m from the player).",
            new AcceptableValueRange<float>(0f, 5f)));

        TerrainMaterial = Bind(LodSection, "TerrainMaterial", "zone", new ConfigDescription(
            "Which terrain material the far tiles use. 'zone': the real terrain material (same textures and sun lighting as " +
            "the ground around the player; tessellation off). 'lod': the game's distant-LOD material, which the game only ever " +
            "shows through fog and which renders every biome as dark, ambient-lit ground when the fog is thin.",
            new AcceptableValueList<string>("zone", "lod")));

        FarTerrainDraw = Bind(LodSection, "FarTerrainDraw", "shrink", new ConfigDescription(
            "How the far tiles are drawn. 'shrink': the tiles render normally for depth, and the mod paints them again " +
            "inside the main camera's render in a coordinate space scaled down so the game's terrain shader believes " +
            "every pixel is within 200 m of the camera. " +
            "That defeats the shader's built-in fade to black between 200 m and 400 m from the camera (hard-coded, no " +
            "setting can turn it off), which is what makes every biome look dark brown beyond 400 m. 'renderer': plain " +
            "renderers, which show that fade.",
            new AcceptableValueList<string>("shrink", "renderer")));

        LodHideDistance = Bind(LodSection, "LodHideDistance", 0f, new ConfigDescription(
            "Only with TerrainMaterial = lod. Camera distance (metres) inside which the distant-LOD shader dissolves the far " +
            "terrain so it cannot show through the real terrain. 0 = automatic: (near simulation distance - 1) x 64 m, which " +
            "keeps the dissolve band inside the zones the game always has loaded. Vanilla uses about 181 m, which lets the " +
            "band show past the loaded zones as melting ground. With the zone material the tiles are instead pushed under " +
            "every loaded zone, so no dissolve is needed.",
            new AcceptableValueRange<float>(0f, 600f)));

        MaxBuildsInFlight = Bind(StreamingSection, "MaxBuildsInFlight", 3, new ConfigDescription(
            "How many tiles may be queued on the terrain builder thread at once. The builder is shared with normal zone loading, keep this small.",
            new AcceptableValueRange<int>(1, 8)));

        MaxMeshBuildsPerFrame = Bind(StreamingSection, "MaxMeshBuildsPerFrame", 1, new ConfigDescription(
            "How many finished tiles may be turned into meshes per frame (main-thread work, a few ms each).",
            new AcceptableValueRange<int>(1, 8)));

        UpdateInterval = Bind(StreamingSection, "UpdateInterval", 0.25f, new ConfigDescription(
            "Seconds between checks of which tiles are needed.",
            new AcceptableValueRange<float>(0.05f, 5f)));

        UpdateStepDistance = Bind(StreamingSection, "UpdateStepDistance", 32f, new ConfigDescription(
            "The camera must move at least this far (metres) before the needed tile set is recomputed.",
            new AcceptableValueRange<float>(1f, 512f)));

        WaitForZones = Bind(StreamingSection, "WaitForZones", true,
            "Do not queue LOD tiles while the zones around the player are still loading, so normal terrain always wins.");

        RaiseCameraFarClip = Bind(RenderingSection, "RaiseCameraFarClip", true,
            "Raise the main camera far clip plane to CameraFarClip so distant tiles are not clipped.");

        CameraFarClip = Bind(RenderingSection, "CameraFarClip", 22000f, new ConfigDescription(
            "Far clip plane in metres (only ever raised, never lowered below the value the game sets).",
            new AcceptableValueRange<float>(1000f, 50000f)));

        FogDensityMultiplier = Bind(RenderingSection, "FogDensityMultiplier", 0.25f, new ConfigDescription(
            "Multiplies the fog density of CLEAR weather only (vanilla clear-day fog is about 0.004 and hides nearly everything past 2 to 3 km). " +
            "1 = vanilla, 0 = no distance fog in clear weather. Rain, storms and dense weather (thick mist, blizzards) keep their own fog: environments denser than " +
            "FogStormDensity are untouched and the effect fades in between. Affects all fog, not just terrain.",
            new AcceptableValueRange<float>(0f, 2f)));

        FogClearDensity = Bind(RenderingSection, "FogClearDensity", 0.006f, new ConfigDescription(
            "Environments with fog density at or below this are treated as clear weather and get the full FogDensityMultiplier. " +
            "Use the console command 'dh envs' to list the game's environments and their densities.",
            new AcceptableValueRange<float>(0.0001f, 0.1f)));

        FogStormDensity = Bind(RenderingSection, "FogStormDensity", 0.02f, new ConfigDescription(
            "Environments with fog density at or above this (storms, mist, blizzards) keep vanilla fog. Between FogClearDensity and this value the thinning fades out.",
            new AcceptableValueRange<float>(0.0001f, 0.2f)));

        KeepWetWeatherFog = Bind(RenderingSection, "KeepWetWeatherFog", true,
            "Never thin the fog of wet environments (rain, thunderstorms) regardless of their density.");

        RealTerrainFadeFix = Bind(RenderingSection, "RealTerrainFadeFix", true,
            "The game's terrain shader also fades the REAL terrain to black between 200 m and 400 m from the camera; with a " +
            "large simulation distance that band is fully visible once the fog is thin. When on (and FarTerrainDraw = shrink), " +
            "every real zone that reaches farther than about 120 m from the camera keeps rendering only for depth (sunk out of " +
            "sight) and the mod draws it again on top at its true size with that fade switched off, so the ground stays lit all " +
            "the way to the far tiles. Nearer zones are left to the game.");

        FarWater = Bind(RenderingSection, "FarWater", true,
            "Draw the sea past the point where the game stops drawing it. The game only gives water to its loaded zones " +
            "plus one 4 km plane, and its water shader fades everything out between 300 m and 800 m from the camera with " +
            "constants no setting reaches, so with thin fog the sea ends and the seabed renders as land. The mod paints a " +
            "sheet of the same water in its shrunk space, where that fade never starts.");

        FarWaterInnerRadius = Bind(RenderingSection, "FarWaterInnerRadius", 0f, new ConfigDescription(
            "Where the far sea starts. 0 = automatic: one zone inside the game's own water, so the two overlap rather " +
            "than leaving a gap.",
            new AcceptableValueRange<float>(0f, 4000f)));

        FarWaterGloss = Bind(RenderingSection, "FarWaterGloss", -1f, new ConfigDescription(
            "Glossiness of the far sea. -1 keeps the game's own value (0.94), which matches the near water. Lower it if " +
            "the far sea ever blows out to white: the sheet is a dead-flat plate, so a mirror finish on it reflects the " +
            "sky evenly instead of being broken up by waves.",
            new AcceptableValueRange<float>(-1f, 1f)));

        FarWaterFog = Bind(RenderingSection, "FarWaterFog", true,
            "Rescale the fog for the far sea. The water shader applies fog itself, against its own eye depth, which is " +
            "shrunk along with everything else, so without this the far sea stays perfectly clear to its outer edge and " +
            "that edge is visible as a hard line against the sky.");

        FarWaterFoam = Bind(RenderingSection, "FarWaterFoam", false,
            "Let the far sea draw foam. Its foam textures are placed by world position with fixed scales the mod cannot " +
            "rescale, so in the shrunk space they stretch into huge pale streaks across the whole sea, which reads as a " +
            "wind pattern painted over the water.");

        ObjectsEnabled = Bind(ObjectsSection, "ObjectsEnabled", true,
            "Draw far trees, rocks and buildings beyond the loaded zones, using the object data this client holds " +
            "(the whole generated world in single player or as host; only the areas you have been near this session when you join someone else's game).");

        ObjectMeshDistance = Bind(ObjectsSection, "ObjectMeshDistance", 400f, new ConfigDescription(
            "Up to this distance (metres) every enabled object is drawn with its real lowest-detail mesh.",
            new AcceptableValueRange<float>(100f, 2000f)));

        ObjectFullDistance = Bind(ObjectsSection, "ObjectFullDistance", 1200f, new ConfigDescription(
            "Up to this distance every tree is drawn as an impostor card, big rocks and buildings as meshes.",
            new AcceptableValueRange<float>(200f, 6000f)));

        ObjectThinDistance = Bind(ObjectsSection, "ObjectThinDistance", 2500f, new ConfigDescription(
            "Up to this distance one big tree in ObjectThinFactor is drawn as an enlarged card.",
            new AcceptableValueRange<float>(200f, 10000f)));

        ObjectFarDistance = Bind(ObjectsSection, "ObjectFarDistance", 6000f, new ConfigDescription(
            "Up to this distance one big tree in ObjectFarFactor is drawn as an enlarged card. Beyond it only the terrain remains.",
            new AcceptableValueRange<float>(200f, 12000f)));

        ObjectThinFactor = Bind(ObjectsSection, "ObjectThinFactor", 1, new ConfigDescription(
            "Keep one big tree in this many in the thin band.", new AcceptableValueRange<int>(1, 64)));
        ObjectThinScale = Bind(ObjectsSection, "ObjectThinScale", 1f, new ConfigDescription(
            "Card enlargement in the thin band, so the forest keeps roughly its visual mass.", new AcceptableValueRange<float>(1f, 5f)));
        ObjectFarFactor = Bind(ObjectsSection, "ObjectFarFactor", 1, new ConfigDescription(
            "Keep one big tree in this many in the far band.", new AcceptableValueRange<int>(1, 256)));
        ObjectFarScale = Bind(ObjectsSection, "ObjectFarScale", 1f, new ConfigDescription(
            "Card enlargement in the far band.", new AcceptableValueRange<float>(1f, 8f)));

        PieceDistance = Bind(ObjectsSection, "PieceDistance", 2000f, new ConfigDescription(
            "Buildings (player and ruins) are drawn as meshes up to this distance.", new AcceptableValueRange<float>(0f, 10000f)));
        RockDistance = Bind(ObjectsSection, "RockDistance", 2000f, new ConfigDescription(
            "Big rocks are drawn as meshes up to this distance.", new AcceptableValueRange<float>(0f, 10000f)));
        BigRockSize = Bind(ObjectsSection, "BigRockSize", 8f, new ConfigDescription(
            "Rocks at least this large (metres) count as big and are kept beyond ObjectMeshDistance.", new AcceptableValueRange<float>(1f, 100f)));
        BigTreeHeight = Bind(ObjectsSection, "BigTreeHeight", 8f, new ConfigDescription(
            "Trees at least this tall (metres) count as big and are kept in the thin and far bands.", new AcceptableValueRange<float>(1f, 60f)));
        SmallTreeCards = Bind(ObjectsSection, "SmallTreeCards", true,
            "Also draw small trees (saplings, small beeches and firs) as cards in the full band.");

        DrawTrees = Bind(ObjectsSection, "DrawTrees", true, "Draw far trees.");
        DrawRocks = Bind(ObjectsSection, "DrawRocks", true, "Draw far rocks.");
        DrawBushes = Bind(ObjectsSection, "DrawBushes", false, "Draw bushes and shrubs in the mesh band (costly: shrubs are the most common object).");
        DrawPieces = Bind(ObjectsSection, "DrawPieces", true, "Draw buildings, both player-built and ruins.");
        DrawLogs = Bind(ObjectsSection, "DrawLogs", false, "Draw fallen logs in the mesh band.");

        ImpostorResolution = Bind(ObjectsSection, "ImpostorResolution", 128, new ConfigDescription(
            "Pixels per side of one tree card in the 1024x1024 atlas (128 = 64 tree types, 256 = 16). Changing this rebuilds objects.",
            new AcceptableValueList<int>(64, 128, 256)));
        ImpostorBakeBrightness = Bind(ObjectsSection, "ImpostorBakeBrightness", 1f, new ConfigDescription(
            "Ambient brightness used when baking the cards. Lower if far forests look too bright compared to real trees.",
            new AcceptableValueRange<float>(0.2f, 2f)));
        ImpostorShader = Bind(ObjectsSection, "ImpostorShader", "vegetation", new ConfigDescription(
            "Shader family for the cards: 'vegetation' clones the game's leaf material (fog and lighting like real trees; no snow or rain, wind only with FarObjectWind), 'standard' uses Unity's Standard cutout shader.",
            new AcceptableValueList<string>("vegetation", "standard")));

        ObjectBuildBudgetMs = Bind(ObjectsSection, "ObjectBuildBudgetMs", 2f, new ConfigDescription(
            "Main-thread milliseconds per frame spent merging object meshes.", new AcceptableValueRange<float>(0.5f, 16f)));
        ObjectUpdateInterval = Bind(ObjectsSection, "ObjectUpdateInterval", 0.25f, new ConfigDescription(
            "Seconds between re-evaluations of which object tiles are needed.", new AcceptableValueRange<float>(0.1f, 5f)));
        ObjectCacheSeconds = Bind(ObjectsSection, "ObjectCacheSeconds", 60f, new ConfigDescription(
            "How long a zone's object list is trusted before it is re-read from the object store (catches new buildings and grown trees).",
            new AcceptableValueRange<float>(5f, 600f)));
        MaxVertsPerMesh = Bind(ObjectsSection, "MaxVertsPerMesh", 250000, new ConfigDescription(
            "Merged meshes are split when they exceed this many vertices.", new AcceptableValueRange<int>(20000, 2000000)));
        MaxVertsPerObject = Bind(ObjectsSection, "MaxVertsPerObject", 1500, new ConfigDescription(
            "In the mesh band, trees whose lowest LOD has more vertices than this are drawn as cards instead (keeps dense pine forests cheap).",
            new AcceptableValueRange<int>(50, 10000)));

        SnapObjectsToTerrain = Bind(ObjectsSection, "SnapObjectsToTerrain", true,
            "Place far objects on the far (coarse) terrain instead of at their true height, so they mostly neither float nor sink where the " +
            "coarse terrain differs from the real one. Trees are snapped individually; rocks and buildings shift with their zone.");

        FarObjectWind = Bind(ObjectsSection, "FarObjectWind", false,
            "Let far trees sway in the wind. Off by default: merged meshes share one origin, so the leaf shader's sway makes whole trees slide.");

        SpyglassDetail = Bind(SpyglassSection, "SpyglassDetail", true,
            "While a spyglass from the MC Spyglass mod is at your eye, draw the land in finer detail and the far objects farther out " +
            "in the direction you look (as if that area were as many times nearer as the spyglass zooms, up to SpyglassMaxBoost). " +
            "Nothing changes without that mod or while the spyglass is down.");
        SpyglassMaxBoost = Bind(SpyglassSection, "SpyglassMaxBoost", 4f, new ConfigDescription(
            "Most the spyglass may bring things nearer for detail (1 = no boost). Higher shows more far buildings and trees and finer land " +
            "through a strong zoom, but builds more tiles each time you turn.",
            new AcceptableValueRange<float>(1f, 8f)));

        DebugLogging = Bind(LoggingSection, "DebugLogging", false,
            "Log tile builds and removals, and every 30 seconds the tile and object counts, to the BepInEx log.");

        file.SettingChanged += OnSettingChanged;
    }

    private ConfigEntry<T> Bind<T>(string section, string key, T value, string description)
    {
        return Bind(section, key, value, new ConfigDescription(description));
    }

    // Me give every setting an Order so ConfigurationManager list them in this file's order, not by name.
    private ConfigEntry<T> Bind<T>(string section, string key, T value, ConfigDescription d)
    {
        if (!_nextOrder.TryGetValue(section, out var order))
        {
            order = 100;
        }
        _nextOrder[section] = order - 1;
        return File.Bind(section, key, value, new ConfigDescription(d.Description, d.AcceptableValues,
            new ConfigurationManagerAttributes { Order = order }));
    }

    private void OnSettingChanged(object sender, SettingChangedEventArgs e)
    {
        var handlers = Changed;
        if (handlers == null || e == null)
        {
            return;
        }
        foreach (var d in handlers.GetInvocationList())
        {
            try
            {
                ((Action<ConfigEntryBase>)d)(e.ChangedSetting);
            }
            catch (Exception ex)
            {
                PatchGuard.Report("DHConfig.Changed " + d.Method.DeclaringType?.Name, ex);
            }
        }
    }
}
