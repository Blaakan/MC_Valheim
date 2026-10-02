# Changelog

## 0.1.0

- Initial version in the MC collection, from the standalone Distant Horizons (0.1.0, plugin
  `com.distanthorizons.valheim`). Far terrain over the whole world as a level-of-detail quadtree of the game's own
  distant heightmaps, exact heights near the real ground, far tiles painted in a scaled-down space so the terrain
  shader's 200-400 m fade to black never starts, the same fix for the real terrain, far trees (impostor cards), big
  rocks and buildings, the far sea, and thinner fog in clear weather.
- Turns on and off live from the MC Mods panel or the config file, also inside a world; off gives back the normal fog,
  distant water and camera range at once and the normal distant terrain within a few seconds.
- Settings are grouped in plain sections (LOD layout, Streaming, Rendering, Objects, Logging) and keep their order
  inside each section in every ConfigurationManager (shudnal's and the upstream build also keep the section order;
  aedenthorn's sorts sections by name, General first). `ImpostorShader` is a choice list. Far-object settings apply
  half a second after the last change instead of on every step of a slider.
- Fog thinning now multiplies the fog the game (and other fog mods) set, instead of replacing it.
- The ground around you looks like the game's own again: with the Tessellation graphics setting on, the standalone
  showed dark oval patches and thin dark lines on the grass next to the player and a too-wide pale shore. The real
  ground within about 120 m is now left to the game, and the real ground farther out is redrawn at its true size
  (only to remove the game's fade to black in the distance).
- The far sea follows the world's water level and ends at `WorldRadius`; it is hidden inside dungeons.
- Turning the far sea off gives the game's own distant water plane back at once (the standalone left it hidden until
  the simulation distance changed).
- Far tiles never count as the ground of a loaded zone for other mods (for example Valheim Community Patch).
- Works with the MC Spyglass mod: while a spyglass is at your eye, the land in the looked-at direction is drawn in
  finer detail and far trees, rocks and buildings farther out there (new Spyglass settings `SpyglassDetail` and
  `SpyglassMaxBoost`). Nothing changes without that mod.
- Stays off, with a Status saying why, while the standalone Distant Horizons or New Horizons: Treelines is installed.
- Console: `dh on` and `dh toggle` are gone (use the MC Mods panel). `dh dump`, `dh atlas`, `dh shot` and `dh terrain`
  (the tile inspector and render experiments) are only in Debug builds; with `DebugLogging` on, the diagnostics and
  the tree card atlas are still written once.
