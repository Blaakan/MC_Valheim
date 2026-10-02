# Distant Horizons — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the git commit shown in the `[MC:ready]` log line and next to the mod in the
MC Mods panel. Smoke test passed 2026-10-02 with all 24 MC mods (loads, patches cleanly, JitCheck clean: 592
methods, 0 failures). In-world self tests (T00) passed 2026-10-02 on the same code: 4/4 `horizons.*`, and the full
run of every mod's tests with this mod deployed (108/108). No hands-on in-game test yet. The
standalone Distant Horizons was tested in game by its author (2026-09-21 to 09-28); this port changes its life cycle,
so everything below is pending.

**Setup:** use a **test character and a test world**. Enable the console (game settings, Gameplay, or the
`-console` launch option: Steam > Properties > Launch Options), press F5 → `devcommands` (confirm cheats if asked).
Commands used below (names checked in the 1.0.16 game code): `fly` (toggle flying; Space up, Ctrl down),
`tod 0.5` (noon; `tod -1` = normal time), `env Clear` (force clear weather; `env Rain`, `resetenv`), `goto <x> <z>`
(teleport). Remove the standalone `BepInEx/plugins/DistantHorizons/` if you had it (T20 checks what happens when it
is there). Keep
`./tools/Watch-Log.ps1 -Mine` open; `DebugLogging = true` (section Logging) logs tile counts every 30 seconds.
**(unverified)**: the fog densities of the weathers (`dh envs` prints them: note Clear, Rain, Mist, SnowStorm and the
Deep North blizzard `Twilight_SnowStorm`); the game's own distant water plane being about 4 km wide; vanilla's camera
far plane value (note `cam` in the `DebugLogging` stats line before and after T11).

## 0.1.0 — single player

- [ ] **T00 Automated in-world self tests:** `./tools/Test-InWorld.ps1 -Mod View.DistantHorizons -Only horizons.`
  (Debug build, game closed). Expected: `[selftest] PASS` for `horizons.logic` (other-mod list, every setting has a
  ConfigurationManager order, no own Enabled), `horizons.terrain` (far tiles built, vanilla grid gone, paint buffer on
  the camera, far plane raised), `horizons.objects` (far object tiles built) and `horizons.detach` (the turn-off path
  leaves no manager, root object, bake rig or camera buffer, gives back the vanilla grid with its meshes without the
  camera moving, the far plane and the game's distant water plane; the turn-on path in a world brings the far
  terrain back).
- [ ] **T01 Terrain to the horizon:** `env Clear`, `tod 0.5`, `fly` up about 200 m above a mountain. Expected: land
  in every direction up to the world's edge (the sea beyond), no holes, no sky-coloured cracks between tiles, mountains
  and coasts in their real shape. Screenshot it. Then Esc → MC Mods → untick Distant Horizons: the land ends about
  1.2 km away in fog; tick it again.
- [ ] **T02 Meeting the real ground:** walk and run in hilly land, then fly low. Expected: where the real terrain ends
  (about 130-200 m around you) there is no step, no gap and no flicker between real and far ground, and no
  "melting" or sinking band of ground.
- [ ] **T03 Far land lit:** clear noon, look across 0.3-2 km of meadows and forest. Expected: the far ground has the
  same colours and sunlight as the ground near you, no dark-brown band 200-400 m away. Then Settings > Graphics >
  raise the simulation distance to its maximum: the real ground stays lit out to the far tiles (`RealTerrainFadeFix`);
  set `RealTerrainFadeFix = false`: the real ground beyond about 200 m turns dark (the game's own fade), back to true.
- [ ] **T04 Streaming while moving:** `fly` fast along a straight line for 3-4 km, then turn around. Expected: detail
  appears near you within a few seconds, far tiles never leave holes, no long freezes (note the frame rate with and
  without the mod: informational). Zones around you still load normally.
- [ ] **T05 Far objects:** from a hill on a clear day look at forests, big rocks, ruins and your own buildings 0.5-6 km
  away. Expected: forests as tree shapes up to about 6 km, big rocks and buildings up to about 2 km, all standing on
  the ground (not floating, not sunk), no white or black trees.
- [ ] **T06 Hand-over to real objects:** walk towards a forest edge 400-600 m away. Expected: as you get close the real
  trees replace the far ones without a moment with no tree and without trees shown twice.
- [ ] **T07 Far sea:** on a coast, clear noon, look out to sea. Expected: the sea continues to the horizon with the
  same colour as the near water (a slightly darker or greener far sea is known), no white sheet, no flicker where the
  near water ends, no pale foam streaks. `FarWater = false`: the far sea is gone and the game's own water is back
  (no hole near you); back to true.
- [ ] **T08 Fog by weather:** `env Clear`: thin fog, far land visible. `env Rain`: normal rain fog. `resetenv`.
  `FogDensityMultiplier = 1`: normal clear-weather fog; back to 0.25. `dh envs` lists weathers and densities.
- [ ] **T09 Interiors:** enter a Burial Chamber or a Troll Cave, and a Deep North dungeon if you have one. Expected:
  no far terrain or sea shows through walls or floors; when you come out everything is back.
- [ ] **T10 Simulation distance change in a world:** Settings > Graphics > change the simulation distance and apply.
  Expected: the far terrain keeps meeting the real ground cleanly (no dissolving band).
- [ ] **T11 Live toggle in a world (MC Mods panel):** Esc → MC Mods → untick Distant Horizons while the game is paused.
  Expected at once, still paused: normal fog, no far objects, no far sea, normal sky distance; within a few seconds,
  without moving, the vanilla distant terrain is back (land ends about 1.2 km away in fog). Resume: no error. Tick it
  again: the far terrain streams back within a few seconds (the horizon can show sky for a moment). Do it twice more
  quickly: no doubled terrain, no error.
- [ ] **T12 Live toggle from the file or ConfigurationManager:** set `General/Enabled = false` in
  `BepInEx/config/MC.Exploration.View.DistantHorizons.cfg` (or in ConfigurationManager) while in a world: same as T11;
  `Status` says "Off". Back to true: Active.
- [ ] **T13 Disabled in the config file:** `Enabled = false`, start the game, load the world: vanilla distant terrain,
  normal fog; the MC Mods panel shows the mod Off. Back to true.
- [ ] **T14 Settings apply live:** in ConfigurationManager (or the file) change `ViewDistance` to 5000 (far detail
  stops refining past 5 km), `DrawTrees = false` (far trees go half a second later), `ObjectsEnabled = false` (all far
  objects go), `BaseVertexSpacing = 4` (far terrain rebuilds about half a second after the last change). Drag
  `ImpostorBakeBrightness` slowly: the tree cards are re-baked once after you let go, not while dragging. Put the
  values back.
- [ ] **T15 ConfigurationManager list:** with shudnal's Valheim Configuration Manager, F1 → Distant Horizons. Expected
  sections General, LOD layout, Streaming, Rendering, Objects, Logging, each listing its settings in the README's
  order (with aedenthorn's build, Nexus 740: the same settings, sections sorted by name, General first);
  `TerrainMaterial`, `FarTerrainDraw`, `ImpostorResolution` and `ImpostorShader` are drop-down choices; `Status` is
  plain text.
- [ ] **T16 Console:** `dh` (tile counts), `dh envs`, `dh objects`, `dh objects rebuild`, `dh rebuild` (far terrain
  streams again), `dh get ViewDistance`, `dh set FogDensityMultiplier 1` (fog thicker at once; the file shows 1),
  `dh get Enabled` ("unknown setting": the General settings belong to the MC Mods panel), `dh off` (the mod turns off,
  the message says how to turn it back on, `dh` is then an unknown command). Turn it back on in the panel.
- [ ] **T17 Second world:** with the mod on, log out to the main menu and load another world (or the same one).
  Expected: the far terrain works again, no error.
- [ ] **T18 Quit from a world:** with the mod on, Esc > Quit > Quit to desktop. Expected in `BepInEx/LogOutput.log`:
  no error from Distant Horizons after the world starts shutting down.
- [ ] **T19 Clean log:** after a session, no errors or exceptions mentioning `Distant Horizons` or
  `MC.Exploration.View.DistantHorizons`.
- [ ] **T20 Standalone also installed:** put the standalone `DistantHorizons.dll` back in `BepInEx/plugins/`. Expected:
  this mod's Status says "Inactive: The standalone Distant Horizons (BepInEx/plugins/DistantHorizons) also draws the
  distant terrain. Remove one of them.", one warning in the log, only the standalone draws. Remove it again.

## 0.1.0 — multiplayer (needs a second player)

- [ ] **M01 Dedicated server without the mod:** join a vanilla dedicated server. Expected: the mod is Active, far
  terrain and sea work, far objects appear only around areas you have been near this session, no error.
- [ ] **M02 Friend without the mod (hand-off):** host a world with the mod; a friend without it joins and visits your
  base. Expected: nothing changes for them (the mod writes nothing in the world); they see your buildings normally.
  Their game does not mention the mod.
- [ ] **M03 Both players with the mod:** each sees far terrain; nothing is synced between them (each player's own
  settings apply).

## 0.1.0 — compatibility (optional, needs another mod)

- [ ] **C01 Swim Dive (MC):** with Swim Dive, dive under water near a coast. Expected: its under-water fog and view as
  usual; no far-sea sheet seen from below; back at the surface the far sea is there.
- [ ] **C02 Sneak Ambush (MC):** in clear weather the Fog stealth icon stays as without this mod (its bonus uses the
  weather data, not the thinner rendered fog).
- [ ] **C03 Deep North Awakening (MC):** during an awakened-area blizzard (`env Twilight_SnowStorm`), note whether the
  blizzard fog stays thick (it does if its density is at or above `FogStormDensity`, see `dh envs`).
- [ ] **C04 ConfigurationManager (Nexus 740):** see `src/Shared/TESTING.md` F14; in addition the Distant Horizons
  sliders apply while dragging and the terrain rebuilds once after you let go.
- [ ] **C05 Valheim Community Patch:** with its "Verify Heightmap Registry" setting on, play 10 minutes: no "DIVERGED"
  line in the log (far tiles are never registered as real ground).
- [ ] **C06 Expand World Size:** bigger world, `WorldRadius` set to its radius plus edge size: far land and sea reach
  the new edge.
