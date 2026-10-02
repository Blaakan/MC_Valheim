# Distant Horizons

Draws the world all the way to the horizon: far terrain streamed as a level-of-detail quadtree over the whole map, far trees, rocks and buildings, the far sea, and thinner fog in clear weather.

In the normal game the land ends about 1.2 km away: beyond the zones around you the game only draws a fixed 3x3 grid
of coarse distant terrain (2.4 km across) and fog hides the rest. This mod replaces that grid with terrain that covers
the whole world, fine near you and coarser with distance, and adds what stands on it.

## Features

- **Terrain to the edge of the world.** The far ground is a quadtree of the game's own distant heightmaps: small,
  detailed tiles near you, big coarse tiles far away, streamed in a few at a time. Heights come from the game's world
  generator on its own terrain thread; the nearest levels use exactly the same height calculation as the real ground
  around you, so the two meet without a step.
- **Lit like the ground around you.** Far tiles use the real terrain material. The game's terrain shader fades every
  biome to black between 200 m and 400 m from the camera (vanilla never shows terrain that far without fog), so the
  mod paints the far tiles in a scaled-down space where that fade never starts. The real terrain stays lit too when
  you set a large simulation distance (`RealTerrainFadeFix`): beyond about 120 m it is drawn again at its true size
  with the fade switched off, and nearer ground is left exactly as the game draws it.
- **Far trees, rocks and buildings.** Beyond the loaded zones the mod draws what your game knows is there: trees as
  impostor cards (baked at run time from each tree's own model), big rocks and buildings (yours and ruins) as their
  lowest-detail meshes. Objects are placed on the far terrain (trees one by one, rocks and buildings with their
  zone), so they rarely float or sink. When you walk closer, the real
  objects take over without popping twice.
- **The far sea.** The game only gives water to the loaded zones plus one 4 km plane, and its water shader fades
  everything out between 300 m and 800 m. The mod paints a sheet of the same water far beyond that, so the sea does
  not end and turn into land with thin fog.
- **Thinner fog in clear weather** (`FogDensityMultiplier`, default 0.25): clear days show the far land. Rain and
  storms keep their fog, and so does dense weather such as thick mist and blizzards; lighter fog, like the Black
  Forest's daytime mist, is thinned partly (see `FogClearDensity` and `FogStormDensity`).
- **More through a spyglass** (with the MC Spyglass mod): while a spyglass is at your eye, the land in the direction
  you look is drawn in finer detail and far trees, rocks and buildings are drawn farther out there, as if that area were
  as many times nearer as the spyglass zooms (up to `SpyglassMaxBoost`, default 4). It builds up within a few seconds
  and goes back to normal when you lower the spyglass. Nothing changes without the Spyglass mod.
- **Turns on and off live** from the MC Mods panel: off gives back the normal fog and water at once and the normal
  distant terrain within a few seconds.

## Configuration

The config file `BepInEx/config/MC.Exploration.View.DistantHorizons.cfg` is created the first time you launch the game
with the mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration manager
installed that button is hidden: press F1 and use its window). Every setting can be changed in-game with
ConfigurationManager, or by editing the file (the game picks up the change while it runs). Settings marked "rebuilds all
terrain" tear down and re-stream the far terrain half a second after the last change; far-object settings apply half a
second after the last change; everything else applies at once.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| LOD layout | BaseTileSize | `256` | Edge in metres of the finest tile; each further level doubles it. Rebuilds all terrain. |
| LOD layout | BaseVertexSpacing | `2.5` | Metres between terrain samples on the finest level (real zones use 1 m, vanilla distant terrain 10 m); each level doubles it. Tiles are limited to 250x250 quads, so tiny values are rounded up (the log says what is used, at game start and after each change). Rebuilds all terrain. |
| LOD layout | LodLevels | `7` | Number of levels: 256 m tiles doubling up to 16.4 km tiles with the defaults. Clamped so the root grid stays between 2x2 and 8x8 tiles; the log reports the levels used. Rebuilds all terrain. |
| LOD layout | SplitFactor | `1` | A tile splits into four while the camera is closer than this many tile widths. 1 keeps neighbours at most one level apart (while a spyglass is raised, the edge of the sharpened area can put two or three levels side by side; `FillCracks` hides the seam); 0.5 uses far fewer tiles. |
| LOD layout | SplitHysteresis | `0.2` | A split tile stays split until the camera is `SplitFactor x (1 + SplitHysteresis)` tile widths away, so hovering around a boundary does not rebuild tiles. |
| LOD layout | ViewDistance | `22000` | Tiles farther than this (metres, up to 30000) are never subdivided, but still drawn as one coarse tile, so the ground never has holes. Also the outer edge of the far sea. |
| LOD layout | WorldRadius | `10500` | Tiles entirely outside this radius are skipped, and the far sea ends here (up to 20000 m). With a world-size mod, set its world radius plus edge size. Rebuilds all terrain. |
| LOD layout | FillCracks | `true` | Draws each split tile lowered under its children to hide the sky-coloured cracks where two levels meet (about a third more triangles). |
| LOD layout | CrackFillDepth | `1.5` | How far those parent tiles are lowered, in multiples of their own vertex spacing. |
| LOD layout | ExactMaxSpacing | `6` | Levels with a vertex spacing at or below this many metres compute heights exactly like real zones, so the far terrain meets the real terrain without a step; coarser levels use the game's cheaper distant approximation. Exact tiles cost the terrain thread 2 to 4 times more. Rebuilds all terrain. |
| LOD layout | NearTerrainOffset | `0.5` | Exact near tiles are drawn this many metres below their true height so they never flicker against the real terrain where both are drawn. |
| LOD layout | TerrainMaterial | `zone` | `zone`: the real terrain material (same textures and sun light as the ground around you). `lod`: the game's distant material, which renders every biome as dark ground once the fog is thin. Rebuilds all terrain. |
| LOD layout | FarTerrainDraw | `shrink` | `shrink`: far tiles are painted in a scaled-down space where the shader's 200-400 m fade to black never starts. `renderer`: plain drawing, which shows that fade. Rebuilds all terrain. |
| LOD layout | LodHideDistance | `0` | Only with `TerrainMaterial = lod`: distance inside which the distant shader dissolves the far tiles. 0 = automatic. |
| Streaming | MaxBuildsInFlight | `3` | Tiles queued on the game's terrain thread at once. That thread also loads the zones around you: keep this small. |
| Streaming | MaxMeshBuildsPerFrame | `1` | Finished tiles turned into meshes per frame (a few milliseconds each). |
| Streaming | UpdateInterval | `0.25` | Seconds between checks of which tiles are needed. |
| Streaming | UpdateStepDistance | `32` | The camera must move this far (metres) before the needed tiles are checked again. Far objects use it too. |
| Streaming | WaitForZones | `true` | Queue no far tiles while the zones around you are still loading, so the real ground always comes first. |
| Rendering | RaiseCameraFarClip | `true` | Raise the camera's far plane to `CameraFarClip` so far tiles are not cut off. Put back when the mod is turned off. |
| Rendering | CameraFarClip | `22000` | Far plane in metres (only ever raised). |
| Rendering | FogDensityMultiplier | `0.25` | Multiplies the fog of clear weather (the clear-day fog hides nearly everything past 2 to 3 km). 1 = normal fog, 0 = no distance fog in clear weather. Affects all fog, not only the far terrain. |
| Rendering | FogClearDensity | `0.006` | Weather with a fog density at or below this counts as clear and gets the full multiplier. `dh envs` lists the game's weathers and their densities. |
| Rendering | FogStormDensity | `0.02` | Weather with a fog density at or above this (storms, mist, blizzards) keeps its fog; the thinning fades out between the two values. |
| Rendering | KeepWetWeatherFog | `true` | Never thin the fog of rain and thunderstorms. |
| Rendering | RealTerrainFadeFix | `true` | The same shader fade darkens the real terrain between 200 m and 400 m, fully visible with a large simulation distance and thin fog. When on (with `FarTerrainDraw = shrink`), real zones reaching farther than about 120 m from the camera are drawn again at their true size with that fade switched off, so the ground stays lit up to the far tiles; nearer zones are left to the game. |
| Rendering | FarWater | `true` | Draw the sea past the point where the game stops drawing it. |
| Rendering | FarWaterInnerRadius | `0` | Where the far sea starts (metres). 0 = automatic: one zone inside the game's own water, so the two overlap. |
| Rendering | FarWaterGloss | `-1` | Glossiness of the far sea. -1 keeps the game's own value. Lower it if the far sea ever turns white. |
| Rendering | FarWaterFog | `true` | Rescale the fog for the far sea; without it the far sea stays perfectly clear and ends in a hard line against the sky. |
| Rendering | FarWaterFoam | `false` | Let the far sea draw foam. In the scaled-down space foam stretches into huge pale streaks, so it is off. |
| Objects | ObjectsEnabled | `true` | Draw far trees, rocks and buildings. |
| Objects | ObjectMeshDistance | `400` | Up to this distance (metres) every enabled object is drawn with its real lowest-detail mesh. |
| Objects | ObjectFullDistance | `1200` | Up to this distance every tree is an impostor card; big rocks and buildings stay meshes. |
| Objects | ObjectThinDistance | `2500` | Up to this distance one big tree in `ObjectThinFactor` is drawn. |
| Objects | ObjectFarDistance | `6000` | Up to this distance one big tree in `ObjectFarFactor` is drawn. Beyond it only the terrain remains. |
| Objects | ObjectThinFactor | `1` | Keep one big tree in this many in the thin band (1 = all). |
| Objects | ObjectThinScale | `1` | Card enlargement in the thin band, so a thinned forest keeps its mass. |
| Objects | ObjectFarFactor | `1` | Keep one big tree in this many in the far band (1 = all). |
| Objects | ObjectFarScale | `1` | Card enlargement in the far band. |
| Objects | PieceDistance | `2000` | Buildings (yours and ruins) are drawn up to this distance. |
| Objects | RockDistance | `2000` | Big rocks are drawn up to this distance. |
| Objects | BigRockSize | `8` | Rocks at least this large (metres) count as big and are kept beyond `ObjectMeshDistance`. |
| Objects | BigTreeHeight | `8` | Trees at least this tall (metres) count as big and are kept in the thin and far bands. |
| Objects | SmallTreeCards | `true` | Also draw small trees (saplings, small beeches and firs) as cards up to `ObjectFullDistance`. |
| Objects | DrawTrees | `true` | Draw far trees. |
| Objects | DrawRocks | `true` | Draw far rocks. |
| Objects | DrawBushes | `false` | Draw bushes and shrubs up to `ObjectMeshDistance` (costly: they are the most common object). |
| Objects | DrawPieces | `true` | Draw buildings, both player-built and ruins. |
| Objects | DrawLogs | `false` | Draw fallen logs up to `ObjectMeshDistance`. |
| Objects | ImpostorResolution | `128` | Pixels per tree card in the 1024x1024 atlas (64, 128 or 256). Re-bakes the cards. |
| Objects | ImpostorBakeBrightness | `1` | Light used when baking the cards. Lower it if far forests look brighter than real trees. Re-bakes the cards. |
| Objects | ImpostorShader | `vegetation` | `vegetation`: the game's leaf material (fog and light like real trees; no snow or rain on the cards, wind only with `FarObjectWind`). `standard`: Unity's Standard cutout shader. Re-bakes the cards. |
| Objects | ObjectBuildBudgetMs | `2` | Milliseconds per frame spent merging object meshes. |
| Objects | ObjectUpdateInterval | `0.25` | Seconds between checks of which object tiles are needed. |
| Objects | ObjectCacheSeconds | `60` | How long a zone's object list is trusted before it is read again (catches new buildings and grown trees). |
| Objects | MaxVertsPerMesh | `250000` | Merged meshes are split above this many vertices. |
| Objects | MaxVertsPerObject | `1500` | Up to `ObjectMeshDistance`, trees whose lowest-detail model has more vertices than this are drawn as cards instead (keeps dense pine forests cheap). |
| Objects | SnapObjectsToTerrain | `true` | Place far objects on the far (coarse) terrain instead of at their true height, so they mostly neither float nor sink. Trees are placed one by one; rocks and buildings move with their zone. |
| Objects | FarObjectWind | `false` | Let far trees sway in the wind. Off: merged trees share one origin, so sway would make whole trees slide. |
| Spyglass | SpyglassDetail | `true` | While a spyglass from the MC Spyglass mod is at your eye, draw finer land and farther objects in the direction you look. Nothing changes without that mod or while the spyglass is down. |
| Spyglass | SpyglassMaxBoost | `4` | Most the spyglass may bring things nearer for detail (1 to 8; 1 = no boost). Higher shows more far buildings and trees and finer land through a strong zoom, but builds more tiles each time you turn. |
| Logging | DebugLogging | `false` | Log tile builds and removals, and every 30 seconds the tile and object counts, to `BepInEx/LogOutput.log`. With it on, the tree card atlas is also saved once as `BepInEx/DistantHorizons_atlas.png`. |

To see less and save work: lower `ViewDistance` and `CameraFarClip` together, raise `BaseVertexSpacing` (for example
to 4), set `SplitFactor = 0.5`, lower `ObjectFarDistance`, or turn `SmallTreeCards` off. To see more of the ground's
shape near you, lower `BaseVertexSpacing` (keep `ExactMaxSpacing` at least twice it).

## Console

The in-game console (F5) must be enabled: turn on the console in the game's Gameplay settings, or start Valheim with
the `-console` launch option (Steam: Valheim > Properties > Launch Options). Without it, use the config file or
ConfigurationManager. With `DebugLogging` on, the diagnostics and the tree card atlas are written once by themselves.

| Command | What it does |
|---|---|
| `dh` or `dh stats` | Far terrain tile counts and drawn vertices. |
| `dh rebuild` | Tear down and re-stream the far terrain (for example after changing a world mod's settings in a world). |
| `dh envs` | The game's weathers with their fog densities (for `FogClearDensity` / `FogStormDensity`). |
| `dh objects`, `dh objects rebuild` | Far-object counts, or rebuild every object tile. `dh objects on` / `off` switches `ObjectsEnabled`. |
| `dh get <Setting>`, `dh set <Setting> <value>` | Read or change any of this mod's settings (the config file is updated too). |
| `dh off` | Turn the whole mod off (like unticking it in the MC Mods panel). Turn it back on in the panel. |

## Multiplayer

- **Who needs it:** Client-side only: only the players who want the feature need to install it. Works on vanilla servers and with vanilla friends.
- **Multiplayer support:** Works in multiplayer.

Only you need it; it works on vanilla servers and with friends who do not have it. It only draws, nothing is sent over the network. Far objects come from the world data your game already holds: the whole world as host or in single player; when you join someone else's game (a dedicated server or a friend's hosted world), only the areas you have been near.

The mod changes nothing for other players and stores nothing in the world or in your character. Far terrain comes
from the world seed, so it shows land you have never visited. Far objects come from your game's copy of the world:
as host or in single player that is every generated zone, including other players' buildings; when you join
someone else's game (a dedicated server or a friend's hosted world) it is only the areas you have been near this
session, so land you have not been near shows terrain only.

## Good to know

- **Fog decides what you see.** With normal fog the far land mostly shows on mountains and clear evenings. The mod
  thins clear-weather fog by default (`FogDensityMultiplier = 0.25`); set it to 1 for the normal fog.
- **Turning it on inside a world**: the normal distant terrain disappears at once and the far terrain streams in over
  a few seconds, nearest first. At world load this happens behind the loading screen.
- **Never-generated land** shows terrain but no trees or buildings: far objects only exist where the game has created
  the zone's objects at some point.
- **Terrain edits** (raised or dug ground) only show on loaded ground, as in the normal game: seen from far away,
  land keeps its generated shape, so a base on heavily raised ground can look like it floats.
- **Cracks at level boundaries**: `FillCracks` hides them with lowered parent tiles; on steep cliffs a thin flat sliver
  of a lowered tile can occasionally show.
- **Portals** are not drawn far away (the game keeps them apart from other objects). Far trees do not sway.
- **Dungeons and other interiors** hide the far terrain and the far sea, like the rest of the outside world.
- **Cost**: far terrain, far objects and the far sea add GPU work and memory, and the far terrain shares the game's
  terrain thread with zone loading (`WaitForZones` keeps the real ground first). See the end of Configuration for the
  settings that lower the cost.
- **Coming from the standalone Distant Horizons** (`BepInEx/plugins/DistantHorizons/`): remove it; while it is
  installed this mod stays off (its Status says so). Its config file `com.distanthorizons.valheim.cfg` is not read:
  the setting names are the same, the sections now have plain names without numbers.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. Safe to add or remove at any time: it stores nothing in the world or in your character.

- **Expand World Size, Better Continents**: far terrain is built by the game's world generator, so their terrain
  shows to the horizon. For a bigger world set `WorldRadius` to their world radius plus edge size (at most 20000 m:
  land and sea beyond 20 km are not drawn), and raise `CameraFarClip` (up to 50000) and `ViewDistance` (up to 30000)
  toward twice that. After changing their settings while in a world, run
  `dh rebuild` (or turn this mod off and on).
- **Expand World Data, Riverheim**: far terrain follows their terrain and biomes. **Expand World Rivers**: the far sea
  follows a custom water level.
- **Fog mods** (NoFogBruh, ValheimPlus `disableFog`, Seasons, GammaOfNightLights): this mod multiplies the fog they
  leave, so both apply. Set `FogDensityMultiplier = 1` to leave the fog to the other mod.
- **Valheim Performance Overhaul** (Skarif): its "Distant Terrain LOD Improvements" also thin the fog, so the fog is
  thinned twice (the log mentions it). Set one of the two back to 1 if the fog looks too thin.
- **New Horizons: Treelines** does the same job (far terrain and far tree cards): while it is installed this mod stays
  off and its Status says why.
- **Seasons**: far terrain takes the season's colours; far trees, rocks and buildings keep their normal look, and in
  winter the far sea stays open water past the frozen near sea.
- **Valheim Community Patch, Valheim Performance Optimizations**: should work together. Far tiles never count as the
  ground of a loaded zone for other mods.
- **Terrain mods** (AdvancedTerrainModifiers, Infinity Hammer, Heightmap Unlimited): see "Terrain edits" above.
  **Voxheim**: its voxel ground is not covered by `RealTerrainFadeFix` and turns dark beyond about 200 m.
  **Badgers HD Terrain** (replacement terrain shader): the far terrain relies on the game's own shader and may look
  wrong with it.
- **Camera mods** (zoom, field of view, first person, free cameras): should work. The far terrain is drawn for the
  main game camera only, so second cameras and VR do not show it. Mods that lower the camera's far plane: this mod
  raises it again every frame while `RaiseCameraFarClip` is on.
- **ConfigurationManager**: inside each section the settings are listed in the order above. shudnal's and the
  upstream build also keep this section order; aedenthorn's (Nexus 740) sorts the sections by name (General, LOD
  layout, Logging, Objects, Rendering, Streaming) and shows `SplitHysteresis` as a percentage (0.2 = 20%). Layout
  settings rebuild the terrain half a second after the last change.
- **Spyglass** (MC): through its spyglass you see this mod's far land and objects, with finer land and farther
  objects in the direction you look (`SpyglassDetail`, `SpyglassMaxBoost`) and less haze in clear weather (its
  `FogClearing`, applied on top of this mod's fog thinning).
- **Swim Dive** (MC): under water its own fog takes over as usual. **Sneak Ambush** (MC): its fog bonus uses the
  weather's own fog, not the rendered one, so thinner fog does not change stealth. **Deep North Awakening** (MC): its
  blizzards keep their fog when their density is at or above `FogStormDensity`.
- The mods above have not been tested in game with this one yet.
