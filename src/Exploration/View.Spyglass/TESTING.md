# Spyglass — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also in the MC Mods panel). Smoke
test (`./tools/Test-Smoke.ps1 -Mod Spyglass`) passed 2026-10-02 (loads, patches cleanly, JitCheck clean). In-world
self-tests (`./tools/Test-InWorld.ps1 -Mod View.Spyglass`) passed 2026-10-02, with Distant Horizons on:
`spyglass.network`, `spyglass.item` (registration, recipe, tool taking both hands, no punch, dropped copy),
`spyglass.view` (raise, x4 zoom, camera at the eye, body hidden, overlay, crosshair, feet locked, aim slowed, zoom cap,
Distant Horizons message and fog, lower, put away while up, pending rules), `spyglass.pose` (eyepiece at the eye, also
as other players see it), `spyglass.far` (far view from a shore across the sea at x3 and x8), `spyglass.export`; and
Distant Horizons' `horizons.boost` (9 far tiles in the looked-at direction before, 37 with a x4 spyglass, 9 after).
After the review fixes (near plane at the eye, vanilla body-hide rule kept, real-time lowering, abort on world exit,
no allocation in the item check, boosted far objects re-seated on refined land), together with that day's Distant
Horizons near-ground update: smoke test of all 25 MC mods passed, framework test passed (28 checks), in-world run
14/14 (`spyglass.*` 6/6 incl. `spyglass.far`, `horizons.*` 6/6) on 2026-10-02. After the change to a tool taking
both hands (like the hammer; no punch on click): smoke test of all 25 MC mods passed (Spyglass JitCheck 525 methods,
0 failures), in-world run 12/12 (`spyglass.*` 6/6, `horizons.*` 6/6) on 2026-10-02. No hands-on in-game test yet.

**Setup:** F5 for the console: `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). `god`
keeps you alive (type it again to turn it off), `heal` refills health and stamina, `killall` removes nearby creatures.
Items: `spawn MC_Spyglass` drops a spyglass at your feet, `spawn Bronze 2` and `spawn Crystal 2` the default materials,
`spawn Torch`, `spawn ShieldWood` (a wooden shield; prefab name unverified), `spawn forge` places a Forge.
Creatures: `spawn Greyling`, `spawn Boar`. Time and weather: `tod 0.5` (noon), `tod -1` (normal time); `env Clear`
forces clear weather, `resetenv` gives the normal weather back. `goto <x> <z>` (whole numbers) teleports you far away;
`debugmode` then `Z` flies. A good far view: a mountain top or a hill above the sea, in clear weather at noon.

**Names checked:** `MC_Spyglass`, `Bronze`, `Crystal`, `Torch`, `KnifeFlint` and `forge` are resolved by the
`spyglass.item` self-test (1.0.16); `Greyling` and `Boar` are spawned by other MC test lists; every console command
above is in the game code (`Terminal`). `ShieldWood` is not checked (if it does not spawn, use any one-handed shield).
Values from the in-world run: camera field of view 65 (so x4 zoom = field of view 18.1 and x8 = 9.1, computed), eye
height 1.81 m.

Most expected results name Debug log lines: turn on Debug logging with `./tools/Setup.ps1 -DevBepInExConfig` and
follow the log with `./tools/Watch-Log.ps1 -Mine`. Settings are changed with ConfigurationManager (F1) or in
`BepInEx/config/MC.Exploration.View.Spyglass.cfg`; default settings unless an item says otherwise, and put each
changed one back afterwards.

## 0.1.0 — single player

- [ ] **T01 Recipe (G1):** new character (or one that never saw Crystal): `spawn Bronze 2`, `spawn forge`, pick the
  bronze up. Expected: no spyglass recipe yet. `spawn Crystal 2`, pick it up. Expected: "New recipe" Spyglass with its
  icon; at the Forge the Spyglass costs 2 Bronze and 2 Crystal and makes one spyglass; the tooltip says two-handed
  (like the hammer), shows no damage, and the description explains the controls.
- [ ] **T02 A tool like the hammer (G2):** equip a torch, then the spyglass. Expected: the spyglass is in the right
  hand and the torch is put away (both hands taken, as with the hammer). Same with a shield in the left hand. Equip the
  torch, a sword or an axe: the spyglass is put away. Press R: the spyglass hangs where the hammer does.
- [ ] **T03 Raise (G3):** hold the spyglass, look at a far hill, left click. Expected: in about half a second your arm
  lifts the spyglass to your right eye while the camera slides forward into your head and zooms in, and the view
  closes in to the round spyglass view. No jerk, no view of the inside of your head; the arm is seen rising only at
  the start.
- [ ] **T04 Spyglass view (G4):** while looking. Expected: a sharp round view in the middle (about 70% of the screen
  height), blurred and darker outside it, round on any window shape; no crosshair and no name of what you point at;
  health, hotbar and map still on screen; the zoom is x4 (things look 4 times bigger than before).
- [ ] **T05 Aim, no walking (G5):** while looking, move the mouse, then press W, A, S, D, Shift, Space, Ctrl and the
  dodge key. Expected: the view turns, slower than usual (an object crosses the screen about as fast as without
  zoom), your body turns to face the aim; you do not move, jump, crouch, dodge or run. Auto-run started before raising
  stops when you raise.
- [ ] **T06 Wheel zoom:** while looking, scroll up and down. Expected: the zoom goes up by steps to x8 and down to
  x1.5; the aim speed follows the zoom; the camera distance (third person) is the same as before once you lower it.
  Lower and raise again: the zoom you picked is kept.
- [ ] **T07 Lower:** while looking, left click. Expected: the view opens up, the camera slides back to third person,
  the arm comes down; you can walk again at once. Raise again and press Block (right mouse button): same.
- [ ] **T08 Hold to look:** set HoldToLook = true. Expected: hold left mouse = up, release = down; Block still lowers.
  Put it back to false.
- [ ] **T09 Lowers by itself:** while looking: (a) `spawn Greyling` next to you and let it hit you, `god` off: lowered
  when hit; (b) stand in fire for a moment: not lowered by the burning; (c) open the inventory (Tab), the map (M), the
  menu (Esc) and the chat (Enter): lowered each time; (d) press R: view back at once, spyglass put away; (e) equip
  another weapon from the hotbar: view back at once.
- [ ] **T10 Cannot raise:** sitting on a chair, at a ship's helm, swimming, in build mode (hammer) or during the
  equip animation, left click with the spyglass (where it is held). Expected: nothing happens (sitting: the click
  stands you up as in the normal game).
- [ ] **T11 Far view without Distant Horizons (G6):** Distant Horizons off (or not installed). On a hill in clear
  weather, raise and look at the horizon at x8. Expected: trees and buildings within about 300-500 m are seen big and
  clear; beyond, the land fades into the normal fog (unchanged by the spyglass). No error in the log.
- [ ] **T12 Far view with Distant Horizons (G6):** Distant Horizons on, clear weather (`env Clear`), noon, on a high
  point. Raise and look across the land at x8, wait a few seconds. Expected: far land to the horizon, with less haze
  than without the spyglass; far forests, big rocks and buildings (yours or ruins) several kilometres away; the land
  in the looked-at direction gets finer within a few seconds; turning to a new direction refines it too. Lower:
  detail and haze back as before within a second or two.
- [ ] **T13 Rain keeps its fog:** with Distant Horizons on, `env Rain` (or any storm), raise. Expected: the fog is not
  cleared (only clear weather is). `resetenv`.
- [ ] **T14 Dropped spyglass:** drop the spyglass from the inventory. Expected: it lies on the ground as the spyglass
  model (not a knife), can be picked up again, and keeps no durability.
- [ ] **T15 Never punches:** stand next to a tree with the spyglass in hand. Left click raises it and never punches;
  middle click does nothing; Block blocks with the fists as with the hammer. Turn the mod off (MC Mods panel), left
  click and middle click: nothing happens (no punch, no stamina used). Turn it back on.
- [ ] **T16 Settings live:** while looking, change ClearViewSize (0.4, then 1), EdgeBlur (off), EdgeDarkness (0, then
  1), AimSensitivity (0.5) and MaxMagnification (3). Expected: each change shows at once; with MaxMagnification 3 the
  zoom is capped at x3. Put them back.
- [ ] **T17 Render scale:** Settings → Graphics → render scale below 100% (if your game has it). Expected: the round
  view, blur and dark edge still line up with the picture.
- [ ] **T18 Live toggle off while looking:** raise, then untick Spyglass in the MC Mods panel (or set Enabled = false).
  Expected: the normal view comes back at once (camera, zoom, fog, crosshair, your body); the spyglass stays in your
  hand; left click does nothing; the recipe is gone from the Forge. Tick it again: everything works again.
- [ ] **T19 Enabled = false + restart:** set Enabled = false, restart the game and load the character. Expected: the
  spyglass is still in the inventory (not lost); no recipe; Status says off. Set it back to true.
- [ ] **T20 Close walls and roofs:** walk face first into a wall, raise the spyglass and look at it; then stand under a
  low sloped roof or a beam (or a rock overhang) touching your head and raise it toward the roof. Expected: the wall and
  the roof are drawn (you never see through them into the room or sky behind).
- [ ] **T21 No body flash:** look up steeply (the camera comes close to you), raise and lower; then stand indoors with
  your back about 1 m from a wall, raise and lower. Expected: your own body or the back of your head never pops into
  view during the raise or the lowering.
- [ ] **T22 Logout while looking:** single player, raise the spyglass, Esc (the spyglass comes down behind the menu
  even though the game is paused), Logout, Yes. Expected: the main menu and the character select show no round view,
  blur or dark ring. Load the world again: normal view, the spyglass can be raised.
- [ ] **T23 Clean log:** after T01-T22, the log has no error or warning from Spyglass (`./tools/Watch-Log.ps1 -Mine`).

### Cross-mod (MC)

- [ ] **X01 Distant Horizons settings:** with Distant Horizons on, set its SpyglassDetail = false and raise. Expected:
  no extra detail in the looked-at direction (the haze is still cleared). SpyglassMaxBoost 1: same. Put them back.
- [ ] **X02 Sort Chest / Crafting Search and Sort:** put a spyglass, a hammer and a sword in a chest and sort it, and
  look at the crafting list grouping. Expected: the spyglass is grouped with the tools (hammer, hoe), not the weapons.
- [ ] **X03 Dual Wielding:** with a dual-wield pair in hand, equip the spyglass, then try the dual-wield keys.
  Expected: the pair is put away (the spyglass takes both hands, like the hammer) and the spyglass is never paired.
- [ ] **X04 Tower Shield Wall:** a tower shield in hand, equip the spyglass. Expected: the shield is put away (as with
  the hammer); no error.
- [ ] **X05 Swim Dive:** walk into deep water with the spyglass up. Expected: the view comes back at once when you
  start swimming (the game puts the spyglass away); diving works as usual.

## 0.1.0 — multiplayer

- [ ] **M01 Server rules:** host (or dedicated server) with MaxMagnification 3 and RecipeResources Bronze:1; a friend
  with the mod joins. Expected: their log says "Using the server's rules: recipe Bronze:1 at forge level 1, zoom up to
  x3..."; their Forge recipe costs 1 Bronze; their wheel zoom stops at x3.
- [ ] **M02 Others see the pose:** your friend raises the spyglass. Expected: on your screen their arm lifts the
  spyglass to their eye and comes down when they lower it; their head follows where they look.
- [ ] **M03 Refused without the mod:** a friend without the mod joins a server with it (AllowPlayersWithoutMod false).
  Expected: about a second after loading in, their game shows "Incompatible version"; the server log names them.
- [ ] **M04 Hand-off to a player without the mod:** AllowPlayersWithoutMod = true, a friend without the mod joins.
  (a) Drop a spyglass next to them. Expected: they see nothing there and cannot pick it up ("Missing prefab hash" in
  their log); it stays on the ground for you. (b) Hold a spyglass in front of them. Expected: they see an empty right
  hand; nothing else breaks. (c) Put a spyglass and some wood in a chest; they open the chest (no spyglass shown,
  "Failed to find item prefab" in their log) and take the wood; you open the chest again. Expected: the spyglass is
  gone from the chest, as the README warns.
- [ ] **M05 Pending rules:** join a server with the mod and click with the spyglass in the first second after loading
  (hard to time; or watch the log). Expected: "The server has not sent the spyglass settings yet." until the rules
  arrive; afterwards it works.
- [ ] **M06 Dedicated server:** start a dedicated server with the mod. Expected: it loads without errors (no graphics
  device: the item is registered without its look), players with the mod can craft and use spyglasses, and dropped
  spyglasses stay on the ground after a server restart.
