# Distant Horizons — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the git commit shown in the `[MC:ready]` log line and next to the mod in the
MC Mods panel. Smoke test passed 2026-10-02 with all 24 MC mods (loads, patches cleanly, JitCheck clean: 592
methods, 0 failures). In-world self tests (T00) passed 2026-10-02 on the same code: 4/4 `horizons.*`, and the full
run of every mod's tests with this mod deployed (108/108). Near-ground fix after that (same day, real zones next
to the player drawn at true size, T21-T22), then the Spyglass boost (same day, C07-C08): smoke test of all 25 MC mods
passed (JitCheck 700 methods, 0 failures), `horizons.*` 6/6 including the new `horizons.near` and `horizons.boost`,
run together with the Spyglass mod's tests (in-world run 2026-10-02). No hands-on in-game test yet. The
standalone Distant Horizons was tested in game by its author (2026-09-21 to 09-28); this port changes its life cycle,
so everything below is pending.

**Automated checks (2026-10-08):** 33 of 39 self-tests mapped to this list passed (`./tools/Test-InWorld.ps1`,
`./tools/Test-Multiplayer.ps1`) on the working tree of commit `66b484f`. An item ending in "Automated: ..." is checked
in full by the named self-tests and is ticked by them alone; a "Partly automated" item still needs its by-hand part;
"FAILED: automated test" names the self-test that fails.

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
  the camera, far plane raised), `horizons.objects` (far object tiles built), `horizons.boost` (spyglass boost maths,
  slot reading, recompute trigger, and more far tiles in the looked-at direction while a fake spyglass is up, back after)
  and `horizons.detach` (the turn-off path
  leaves no manager, root object, bake rig or camera buffer, gives back the vanilla grid with its meshes without the
  camera moving, the far plane and the game's distant water plane; the turn-on path in a world brings the far
  terrain back), and `horizons.near` (at a Meadows sea shore near spawn: no far-tile vertex above the real ground
  within 120 m, every painted zone's depth renderer untessellated, also with every zone forced to be painted;
  screenshots of the same views in every draw mode in the run's `shots\` folder, to compare by eye: no dark ovals or
  lines on the grass in `1-mod` and `a-all-zones-painted`).
  *Partly automated (`horizons.logic`, `horizons.terrain`, `horizons.objects`, `horizons.boost`, `horizons.detach`,
  `horizons.near`); by hand: The item also asks to compare the horizons.near screenshots by eye: open '1-mod' and
  'a-all-zones-painted' in the run's shots folder and check there are no dark ovals or lines on the grass.*
- [ ] **T01 Terrain to the horizon:** `env Clear`, `tod 0.5`, `fly` up about 200 m above a mountain. Expected: land
  in every direction up to the world's edge (the sea beyond), no holes, no sky-coloured cracks between tiles, mountains
  and coasts in their real shape. Screenshot it. Then Esc → MC Mods → untick Distant Horizons: the land ends about
  1.2 km away in fog; tick it again.
  *Partly automated (`horizons.horizon`, `horizons.terrain`, `horizons.toggle`); by hand: Look at the
  'clear-noon-200m-up' screenshot or fly up yourself: no sky-coloured cracks between tiles, mountains and coasts look
  right, and after unticking the land visibly ends about 1.2 km away in fog.*
- [ ] **T02 Meeting the real ground:** walk and run in hilly land, then fly low. Expected: where the real terrain ends
  (about 130-200 m around you) there is no step, no gap and no flicker between real and far ground, and no
  "melting" or sinking band of ground.
  *Partly automated (`horizons.seam`, `horizons.near`); by hand: Walk, run and fly low in hilly land: no visible step,
  gap, flicker or melting band where the real ground ends (the test stands at the spawn point, it does not move
  through hills).*
- [ ] **T03 Far land lit:** clear noon, look across 0.3-2 km of meadows and forest. Expected: the far ground has the
  same colours and sunlight as the ground near you, no dark-brown band 200-400 m away. Then Settings > Graphics >
  raise the simulation distance to its maximum: the real ground stays lit out to the far tiles (`RealTerrainFadeFix`);
  set `RealTerrainFadeFix = false`: the real ground beyond about 200 m turns dark (the game's own fade), back to true.
  *Partly automated (`horizons.realground`); by hand: Colours and sunlight by eye: no dark-brown band 200-400 m away,
  and the dark band appears with RealTerrainFadeFix = false. Raising the simulation distance to its maximum in the
  graphics menu cannot be done by a test (it uses the distance the profile has).*
- [ ] **T04 Streaming while moving:** `fly` fast along a straight line for 3-4 km, then turn around. Expected: detail
  appears near you within a few seconds, far tiles never leave holes, no long freezes (note the frame rate with and
  without the mod: informational). Zones around you still load normally.
  *Partly automated (`horizons.stream`); by hand: Fly it yourself at the game's own fly speed over 3-4 km: detail near
  you appears within a few seconds while moving, it feels smooth, and note the frame rate with and without the mod.*
- [!] **T05 Far objects:** from a hill on a clear day look at forests, big rocks, ruins and your own buildings 0.5-6 km
  away. Expected: forests as tree shapes up to about 6 km, big rocks and buildings up to about 2 km, all standing on
  the ground (not floating, not sunk), no white or black trees.
  **FAILED:** automated test: Far trees in the 32 m strip past their far-object tile's west / south edge are not stood
  again when the neighbouring far terrain tile refines or merges (self-test `horizons.bug.tree-reseat`)
- [ ] **T06 Hand-over to real objects:** walk towards a forest edge 400-600 m away. Expected: as you get close the real
  trees replace the far ones without a moment with no tree and without trees shown twice.
  *Partly automated (`horizons.simdistance`); by hand: Walk towards a forest edge 400-600 m away and watch the moment
  of the swap: no instant without trees and no trees shown twice (the test compares settled states, not the moment).*
- [ ] **T07 Far sea:** on a coast, clear noon, look out to sea. Expected: the sea continues to the horizon with the
  same colour as the near water (a slightly darker or greener far sea is known), no white sheet, no flicker where the
  near water ends, no pale foam streaks. `FarWater = false`: the far sea is gone and the game's own water is back
  (no hole near you); back to true.
  *Partly automated (`horizons.sea`); by hand: On a coast at clear noon: the far sea has the near water's colour, no
  white sheet, no flicker where the near water ends, no pale foam streaks, and no hole near you with FarWater =
  false.*
- [x] **T08 Fog by weather:** `env Clear`: thin fog, far land visible. `env Rain`: normal rain fog. `resetenv`.
  `FogDensityMultiplier = 1`: normal clear-weather fog; back to 0.25. `dh envs` lists weathers and densities.
  *Automated: `horizons.fog`.*
- [x] **T09 Interiors:** enter a Burial Chamber or a Troll Cave, and a Deep North dungeon if you have one. Expected:
  no far terrain or sea shows through walls or floors; when you come out everything is back.
  *Automated: `horizons.interior`.*
- [ ] **T10 Simulation distance change in a world:** Settings > Graphics > change the simulation distance and apply.
  Expected: the far terrain keeps meeting the real ground cleanly (no dissolving band).
  *Partly automated (`horizons.simdistance`, `horizons.simdistance-rim`); by hand: Change the simulation distance in
  Settings > Graphics and look at the band where the real ground ends: nothing dissolves or flickers.*
- [x] **T11 Live toggle in a world (MC Mods panel):** Esc → MC Mods → untick Distant Horizons while the game is paused.
  Expected at once, still paused: normal fog, no far objects, no far sea, normal sky distance; within a few seconds,
  without moving, the vanilla distant terrain is back (land ends about 1.2 km away in fog). Resume: no error. Tick it
  again: the far terrain streams back within a few seconds (the horizon can show sky for a moment). Do it twice more
  quickly: no doubled terrain, no error.
  *Automated: `horizons.toggle`.*
- [x] **T12 Live toggle from the file or ConfigurationManager:** set `General/Enabled = false` in
  `BepInEx/config/MC.Exploration.View.DistantHorizons.cfg` (or in ConfigurationManager) while in a world: same as T11;
  `Status` says "Off". Back to true: Active.
  *Automated: `horizons.mp.console`, `horizons.toggle`.*
- [ ] **T13 Disabled in the config file:** `Enabled = false`, start the game, load the world: vanilla distant terrain,
  normal fog; the MC Mods panel shows the mod Off. Back to true.
  *Partly automated (`horizons.toggle`); by hand: Really start the game with Enabled = false in the config file and
  load a world: vanilla distant terrain and fog, and the MC Mods panel shows the mod Off.*
- [x] **T14 Settings apply live:** in ConfigurationManager (or the file) change `ViewDistance` to 5000 (far detail
  stops refining past 5 km), `DrawTrees = false` (far trees go half a second later), `ObjectsEnabled = false` (all far
  objects go), `BaseVertexSpacing = 4` (far terrain rebuilds about half a second after the last change). Drag
  `ImpostorBakeBrightness` slowly: the tree cards are re-baked once after you let go, not while dragging. Put the
  values back.
  *Automated: `horizons.live-terrain`, `horizons.live-objects`.*
- [ ] **T15 ConfigurationManager list:** with shudnal's Valheim Configuration Manager, F1 → Distant Horizons. Expected
  sections General, LOD layout, Streaming, Rendering, Objects, Logging, each listing its settings in the README's
  order (with aedenthorn's build, Nexus 740: the same settings, sections sorted by name, General first);
  `TerrainMaterial`, `FarTerrainDraw`, `ImpostorResolution` and `ImpostorShader` are drop-down choices; `Status` is
  plain text.
  *Partly automated (`horizons.settings`, `horizons.logic`); by hand: Open shudnal's Configuration Manager (and
  aedenthorn's build) and look at the Distant Horizons list as drawn. Note: the item's text lists six sections, the
  mod also has a 'Spyglass' section between Objects and Logging.*
- [x] **T16 Console:** `dh` (tile counts), `dh envs`, `dh objects`, `dh objects rebuild`, `dh rebuild` (far terrain
  streams again), `dh get ViewDistance`, `dh set FogDensityMultiplier 1` (fog thicker at once; the file shows 1),
  `dh get Enabled` ("unknown setting": the General settings belong to the MC Mods panel), `dh off` (the mod turns off,
  the message says how to turn it back on, `dh` is then an unknown command). Turn it back on in the panel.
  *Automated: `horizons.console`, `horizons.mp.console`.*
- [ ] **T17 Second world:** with the mod on, log out to the main menu and load another world (or the same one).
  Expected: the far terrain works again, no error.
  *Partly automated (`horizons.relog`); by hand: Really log out to the main menu and load a world (the same and
  another one): far terrain works again and the log has no error. The scene unload itself is not simulated.*
- [ ] **T18 Quit from a world:** with the mod on, Esc > Quit > Quit to desktop. Expected in `BepInEx/LogOutput.log`:
  no error from Distant Horizons after the world starts shutting down.
- [ ] **T19 Clean log:** after a session, no errors or exceptions mentioning `Distant Horizons` or
  `MC.Exploration.View.DistantHorizons`.
  *Partly automated (`horizons.cleanlog`); by hand: A longer hand-played session, then read the log for errors or
  exceptions mentioning Distant Horizons.*
- [ ] **T20 Standalone also installed:** put the standalone `DistantHorizons.dll` back in `BepInEx/plugins/`. Expected:
  this mod's Status says "Inactive: The standalone Distant Horizons (BepInEx/plugins/DistantHorizons) also draws the
  distant terrain. Remove one of them.", one warning in the log, only the standalone draws. Remove it again.
  *Partly automated (`horizons.blocked`, `horizons.logic`); by hand: Put the real standalone DistantHorizons.dll in
  BepInEx/plugins and check that only the standalone draws the far terrain.*
- [ ] **T21 Ground next to you (Tessellation on):** Settings > Graphics > Tessellation on. `env Clear`, `tod 0.5`, then
  `tod 0.7` (low sun). Walk 300-400 m along a Meadows sea shore just above sea level, and across open meadow, looking
  at the ground 5-80 m around you from the normal camera and from high up (zoom out). Expected: no dark or brownish
  oval patches on the grass, no ground that looks like a second surface crossing the real one, no dark straight lines
  across the grass (the 64 m zone grid), and the shore's sand and wet bands the same as with the mod off (Esc → MC
  Mods → untick: only the fog is thicker). Tessellation off: the same. If anything looks wrong, Debug build: stand
  still, F5 → `dh terrain abshots`, close the console within 3 s and keep still about 10 s; send the
  `BepInEx/DistantHorizons_ab_*.png` files and the log.
  *Partly automated (`horizons.near`, `horizons.seam`); by hand: By eye with Tessellation on and off, low sun, along
  300-400 m of shore and open meadow: no dark ovals, no second surface, no dark straight lines, shore bands as with
  the mod off (screenshots are in the run's shots folder).*
- [!] **T22 Far ground at the edge of the loaded area, low simulation distance:** Settings > Graphics > lowest
  simulation distance, Tessellation on, clear noon. Look at the ground 60-200 m away all around (where the real zones
  end). Expected: no dark specks or ovals on the far ground there, and far trees and rocks still standing on it (not
  floating, not sunk). Put the simulation distance back.
  **FAILED:** automated test: Far trees in the 32 m strip past their far-object tile's west / south edge are not stood
  again when the neighbouring far terrain tile refines or merges (self-test `horizons.bug.tree-reseat`)
- [!] **T23 Turning the mod off inside a dungeon:** enter a Burial Chamber or a Troll Cave, Esc → MC Mods → untick
  Distant Horizons, then walk out. Expected outside: the vanilla distant terrain (land ends about 1.2 km away in fog),
  normal fog, the ground around you drawn normally (no missing, dark or sunken ground), the game's own distant water,
  no error. Tick it again: the far terrain streams back. Automated: `horizons.interior-off`.
  **FAILED:** automated test: Turning the mod off in a world sets off Valheim Community Patch exceptions (self-test
  `horizons.bug.off-foreign-errors`)
- [ ] **T24 Simulation distance change at a coast:** on a coast with the far sea on (`FarWater = true`), Settings >
  Graphics > change the simulation distance and apply, then put it back. Expected: the far sea keeps meeting the near
  water with no flicker, no gap and no white sheet (the game's own distant water plane never shows through the far
  sea), and the far sea starts one zone inside the game's water again. Automated part: `horizons.simdistance`.
  *Partly automated (`horizons.simdistance`); by hand: At a real coast, change the simulation distance and look: no
  flicker, gap or white sheet where the near water meets the far sea.*
- [!] **T25 Turning the mod back on while paused:** Esc → MC Mods → untick Distant Horizons, tick it again while the
  game is still paused, and wait until the far terrain has finished streaming. Expected, still paused: the far trees
  within about 1 km stand on the far ground (not floating, not sunk); they do not wait for the game to resume.
  Automated: `horizons.bug.paused-objects`.
  **FAILED:** automated test: Turned back on while paused: far objects do not follow the terrain until the game
  resumes (self-test `horizons.bug.paused-objects`)
- [!] **T26 Right after turning the mod on (or loading a world):** Esc → MC Mods → untick, tick again, close the menu at
  once and look at the ground 50-400 m around you during the first seconds. Expected: no coarse far ground drawn over
  the real ground (no flat or wrong-shaped patches lying on the grass that vanish after a few seconds); only the
  horizon may show sky for a moment. Automated: `horizons.bug.attach-cover` (it also takes a screenshot of that
  moment, `coarse-ground-over-loaded-zones`).
  **FAILED:** automated test: Right after attach, coarse far tiles lie over loaded real ground (self-test
  `horizons.bug.attach-cover`)
- [!] **T27 Near far ground keeps exact heights when the terrain builder is busy:** automated only,
  `./tools/Test-InWorld.ps1 -Mod View.DistantHorizons -Only horizons.bug.exact-evict` (Debug build, game closed). The
  test makes the game's terrain builder drop the finished heights of one exact far tile before the mod fetches them, as
  happens when many other terrain builds finish in between (exploring fresh land during a long frame). Expected:
  `[selftest] PASS`: that tile is built from exact heights again, or is no longer treated as exact, so no step stays
  where the far ground meets the real ground.
  **FAILED:** automated test: Exact near tile built from approximate heights after the builder dropped its exact data
  (self-test `horizons.bug.exact-evict`)
- [!] **T28 Simulation distance change while standing still:** stand still, Settings > Graphics > lower the simulation
  distance, apply, close the menu and do not move. Expected within a few seconds: the far trees and rocks fill the
  ring the real ones left (no bare ring between the real objects and the far ones). Raise it again without moving:
  no trees shown twice in the ring. Automated: `horizons.bug.simdistance-still`.
  **FAILED:** automated test: Far objects do not follow a simulation-distance change until the camera moves 32 m
  (self-test `horizons.bug.simdistance-still`)
- [ ] **T29 Far sea with a lower ViewDistance:** on a coast in clear weather (`env Clear`, `tod 0.5`), `FarWater =
  true`, set `ViewDistance = 5000` and `fly` up 200 m. Expected (README: `ViewDistance` is "also the outer edge of the
  far sea"): the far sea stops about 5 km out at once while the land beyond is still drawn (coarse, no hole). Note how
  the sea beyond 5 km looks: no water is drawn there, so the sea bed shows as dry land unless `CameraFarClip` is lowered
  together with `ViewDistance` (as the README advises); say so if the sea should keep reaching the horizon instead. Put
  the value back: the sea reaches the horizon again. Automated part: `horizons.viewdistance-sea` (sea edge follows the
  setting both ways, land stays whole).
  *Partly automated (`horizons.viewdistance-sea`); by hand: How it looks: beyond the sheet no water is drawn, so the
  sea bed shows as dry land unless CameraFarClip is lowered too; decide whether that is acceptable.*
- [!] **T30 Far trees after the far terrain streams in again:** stand on a hill with forest 400-1200 m away, F5 →
  `dh rebuild` and at once `dh objects rebuild`, wait until the far terrain and the far trees are back (about 10 s).
  Expected: every far tree within about 1.2 km stands on the far ground (not floating, not sunk), also the ones near
  the edge of a 256 m / 512 m far-object tile. Found by the self tests (2026-10-07): 12 of 80 sampled trees stood up to
  0.38 m off until `dh objects rebuild` was run once more. Automated: `horizons.bug.tree-reseat`.
  **FAILED:** automated test: Far trees in the 32 m strip past their far-object tile's west / south edge are not stood
  again when the neighbouring far terrain tile refines or merges (self-test `horizons.bug.tree-reseat`)

## 0.1.0 — multiplayer (needs a second player)

- [x] **M01 Dedicated server without the mod:** join a vanilla dedicated server. Expected: the mod is Active, far
  terrain and sea work, far objects appear only around areas you have been near this session, no error.
  *Automated: `horizons.mp.server`, `probe.mp.baseline`.*
- [ ] **M02 Friend without the mod (hand-off):** host a world with the mod; a friend without it joins and visits your
  base. Expected: nothing changes for them (the mod writes nothing in the world); they see your buildings normally.
  Their game does not mention the mod.
  *Partly automated (`horizons.local-only`, `horizons.mp.server`, `horizons.toggle`); by hand: A friend without the
  mod joins your hosted world and visits your base: nothing changes for them and their game does not mention the mod
  (needs a second player).*
- [ ] **M03 Both players with the mod:** each sees far terrain; nothing is synced between them (each player's own
  settings apply).
  *Partly automated (`horizons.local-only`, `horizons.mp.server`); by hand: Two players with the mod in one world:
  each sees far terrain with their own settings (needs a second player).*

## 0.1.0 — compatibility (optional, needs another mod)

- [ ] **C01 Swim Dive (MC):** with Swim Dive, dive under water near a coast. Expected: its under-water fog and view as
  usual; no far-sea sheet seen from below; back at the surface the far sea is there.
- [ ] **C02 Sneak Ambush (MC):** in clear weather the Fog stealth icon stays as without this mod (its bonus uses the
  weather data, not the thinner rendered fog).
  *Partly automated (`horizons.fog`); by hand: With Sneak Ambush, look at the Fog stealth icon in clear weather with
  and without this mod (its own sneak.cover test checks the icon when both mods run).*
- [x] **C03 Deep North Awakening (MC):** during an awakened-area blizzard (`env Twilight_SnowStorm`), note whether the
  blizzard fog stays thick (it does if its density is at or above `FogStormDensity`, see `dh envs`).
  *Automated: `horizons.fog-blizzard`.*
- [ ] **C04 ConfigurationManager (Nexus 740):** see `src/Shared/TESTING.md` F14; in addition the Distant Horizons
  sliders apply while dragging and the terrain rebuilds once after you let go.
  *Partly automated (`horizons.live-terrain`, `horizons.live-objects`); by hand: aedenthorn's ConfigurationManager
  (Nexus 740) itself: F14 of src/Shared/TESTING.md and dragging the Distant Horizons sliders in its window.*
- [ ] **C05 Valheim Community Patch:** with its "Verify Heightmap Registry" setting on, play 10 minutes: no "DIVERGED"
  line in the log (far tiles are never registered as real ground).
  *Partly automated (`horizons.horizon`, `horizons.stream`); by hand: With Valheim Community Patch and its 'Verify
  Heightmap Registry' setting on, play 10 minutes: no DIVERGED line.*
- [ ] **C06 Expand World Size:** bigger world, `WorldRadius` set to its radius plus edge size: far land and sea reach
  the new edge.
  *Partly automated (`horizons.world-radius`); by hand: A real Expand World Size world with WorldRadius set to its
  size: land and sea really reach the new edge. Known limit to check there: far objects beyond 16.4 km from the centre
  are never drawn.*
- [ ] **C07 Spyglass (MC), detail where you look:** with the Spyglass mod, clear weather (`env Clear`), noon, on a high
  point, raise the spyglass (x8) toward land 2-5 km away and wait 5 s. Expected: the land there gets finer within a
  few seconds and far buildings (yours or ruins) and forests show up farther than without the spyglass; turning to a
  new direction refines that one too; frame rate stays playable. Lower the spyglass: within a second or two the detail
  goes back to normal (no holes, no flicker). Automated part: `horizons.boost` (T00).
  *Partly automated (`horizons.spyglass`, `horizons.boost`); by hand: A real spyglass from the Spyglass mod on a high
  point: the land and far buildings visibly sharpen where you look, frame rate stays playable, no flicker when
  lowering. Also by eye: after lowering, tiles the spyglass split within 1.0 to 1.2 tile widths of you stay split
  (SplitHysteresis, as documented) until you move away, so a strip of land can stay one level finer than before; check
  that this is not noticeable.*
- [ ] **C08 Spyglass settings:** `SpyglassDetail = false` (or `SpyglassMaxBoost = 1`): raising the spyglass changes no
  detail. Set `SpyglassMaxBoost = 8`: more detail through a x8 spyglass. Put them back. Without the Spyglass mod
  installed nothing changes and the log has no Spyglass line.
  *Partly automated (`horizons.spyglass`, `horizons.boost`); by hand: Run once without the Spyglass mod installed and
  check the log has no Spyglass line; try the settings with a real spyglass.*
- [!] **C09 Valheim Community Patch, turning the mod off in a world:** with Valheim Community Patch installed, go into
  a dungeon (or `fly` far up and come back down, anything that makes the grass around you grow again), Esc → MC Mods →
  untick Distant Horizons while inside, walk out and walk around for a minute. Expected: no red
  `ArgumentOutOfRangeException ... ValheimCommunityPatch.HeightmapSampling` lines in the log, and the grass grows
  normally around you. Found by the self tests (2026-10-07, Valheim Community Patch 0.31.0): 22 such lines right after
  turning the mod off, until the game's own distant terrain had its meshes. Automated:
  `horizons.bug.off-foreign-errors`.
  **FAILED:** automated test: Turning the mod off in a world sets off Valheim Community Patch exceptions (self-test
  `horizons.bug.off-foreign-errors`)
