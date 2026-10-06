# Music Instruments — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also in the MC Mods panel). Smoke
test (`./tools/Test-Smoke.ps1 -Mod Music.Instruments`) passed 2026-10-05 after both reviews (loads, patches cleanly,
JitCheck 1136 methods, 0 failures; build 8811b96+dirty). In-world self-tests (`./tools/Test-InWorld.ps1 -Mod
Music.Instruments -Only music`) passed 2026-10-05 after both reviews, 10/10: `music.network`, `music.item`,
`music.synth` (SFX mixer group found, a 3D source on the right sounds louder on the right), `music.songs`,
`music.perform` (simulated hits, Encore, comfort +3), `music.autoplay`, `music.listen` (incl. restart timing, a stall
and notes after End), `music.window`, `music.pose`, `music.export`; `music.pose` passed again after the flute angle
change (45 degrees). Both runs end "IN-WORLD TEST FAILED" only because two other MC mods (Sneak Ambush, Spyglass) log
an error when a third-party mod builds a partial item database; Music Instruments logs no error or warning.
First hands-on test (build 5f6f777): outdoors with a bonfire and Music, Rested was 8 minutes (comfort 1: the shelter
rule dropped the +3). Fixed after it: the bonus counts everywhere (setting BonusOnlyInShelter removed, network version
2). Smoke test and in-world self-tests passed again 2026-10-05 (10/10; `music.perform` outdoors: comfort 4 = 1 + 3,
Rested 660 s = 480 + 3 x 60); same two errors from Sneak Ambush and Spyglass only.

**Setup:** F5 for the console: `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). `god`
keeps you alive (type it again to turn it off), `heal` refills health and stamina. Items: `spawn MC_Flute`,
`spawn MC_Lyre`, `spawn MC_Tambourine` drop an instrument at your feet; materials `spawn FineWood 10`,
`spawn Silver 5`, `spawn LinenThread 10`, `spawn LeatherScraps 10`; `spawn piece_workbench` places a workbench
(build its level 2 extension, the chopping block, with the hammer). A fire: build a Campfire with the hammer (or
`spawn fire_pit`, prefab name unverified). Creatures: `spawn Greyling`, `spawn Boar`. Time: `tod 0.5` (noon),
`tod -1` (normal time). Sit: X (sit emote) or a chair/bench.

**Names checked:** `MC_Flute`, `MC_Lyre`, `MC_Tambourine`, `FineWood`, `Silver`, `LinenThread`, `LeatherScraps` and
`piece_workbench` are resolved by the `music.item` self-test; `Torch` by the same test; `Greyling` and `Boar` are
spawned by other MC test lists; `fire_pit` is not checked.

Most expected results name Debug log lines: turn on Debug logging with `./tools/Setup.ps1 -DevBepInExConfig` and
follow the log with `./tools/Watch-Log.ps1 -Mine`. Settings are changed with ConfigurationManager (F1) or in
`BepInEx/config/MC.Exploration.Music.Instruments.cfg`; default settings unless an item says otherwise, and put each
changed one back afterwards. For the MIDI items, put a few `.mid` files (any song from the internet, a simple one and a
busy one with drums) in `BepInEx/config/MC_Valheim/Songs`.

## 0.1.0 — single player

- [ ] **T01 Recipes (G1):** new character (or one that never had these materials), workbench nearby: pick up Fine
  Wood. Expected: "New recipe" Wooden Flute (4 Fine Wood, workbench level 1). Pick up Leather Scraps, upgrade the
  workbench to level 2: Tambourine (3 Fine Wood, 4 Leather Scraps). Pick up Silver and Linen Thread: Silver Lyre
  (2 Silver, 8 Linen Thread, level 2). Each has its own icon; the tooltip says two-handed, shows no damage, and its
  orange line shows the Attack and Block keys.
- [ ] **T02 Models and icons (G6):** hold each instrument (third person), then drop each on the ground. Expected: a
  wooden flute with finger holes, a silver lyre with strings, a tambourine with a skin and jingles, sized like real
  instruments in the hand; dropped ones lie on the ground, can be picked up, and look the same; the inventory icons
  match the models.
- [ ] **T03 Tools:** equip a torch, then an instrument. Expected: both hands taken (the torch is put away); a weapon or
  torch puts the instrument away; R hangs it at the hip like the hammer.
- [ ] **T04 Song window (G2):** hold the flute, left click. Expected: the Songs window opens with the cursor; the eight
  built-in songs, then "Your MIDI songs" with your files (or a hint naming the folder when it is empty); the camera and
  your character do not move while it is open; Esc closes it without opening the menu; Close and the right mouse
  button do too.
- [ ] **T05 Play a built-in song (G2):** pick Greensleeves, Play. Expected: the window closes, the flute plays the tune
  (a breathy wooden flute, in tune, steady tempo); your arms hold the flute to your mouth; a small status line shows the
  title and the time. The game's music fades. Walk around while it plays: it keeps playing; Shift does not run, Space
  does not jump. Left click (or right click): it stops, the arms come down, the game music comes back; the click does
  not punch or raise your block. Repeat on: the song starts again after its end (the time in the status line starts
  over at 0:00 then, not before); off: it stops by itself at the end.
- [ ] **T06 Each instrument:** play Mead Hall Reel on the lyre and on the tambourine. Expected: the lyre plucks the
  tune with strummed chords under it; the tambourine plays a steady rhythm of thumps, hits and jingles; each has its
  pose (lyre against the chest, tambourine raised and shaking with the beats).
- [ ] **T07 MIDI files (G2):** with your files in the songs folder, open the window. Expected: each file is listed;
  picking one shows its parts and length; Play on the flute plays a recognizable tune of it; on the lyre the tune with
  a bass line; on the tambourine the drums (or the tune's rhythm). Pick another part with the part chooser: that part
  plays. A broken file (rename any non-MIDI file to .mid) shows why it cannot play and nothing breaks.
- [ ] **T08 Rhythm game (G2, G3):** hold any instrument, pick Kjerringa med staven, Perform. Expected: a countdown, then
  notes fall in four lanes toward the line; pressing D F J K on time plays the notes (hits show "Perfect"/"Good"),
  missing shows "Miss" and plays nothing; you cannot walk, open the inventory or use the hotbar while it runs; the song
  repeats. Right click or Esc stops it (Esc also opens the menu).
- [ ] **T09 Encore and comfort (G3):** in a shelter (roof and walls) with a burning fire: sit (X) and play the rhythm
  game well for 20 seconds. Expected: the meter fills (it drains if you play badly), "Encore! +3 comfort", the Music
  icon appears with "+3" and 10:00 counting down; as soon as the Encore comes, the Resting icon shows comfort 3 higher
  and the Rested timer jumps 3 minutes higher (no new "You feel rested" message if Rested was already running).
  Keep playing well: each further 20 s renews Music to 10:00.
- [ ] **T10 Outdoors:** outdoors by a campfire or a bonfire (no roof), sit (X), open the window and perform. Expected:
  Resting shows comfort 1 and after about 20 s Rested starts at 8:00 (or keeps running if you were already Rested).
  When the Encore comes, the Resting icon shows comfort 4 (the game's 1 + 3) and the Rested timer jumps to 11:00 and
  stays there while you rest. No new "You feel rested" message: the game shows it only when Rested starts. Optional,
  when you are not Rested: get the Encore first (standing), then sit by the fire: after about 20 s "You feel rested
  (Comfort: 4)" and 11:00. (Changed after the first in-game test: with a bonfire and Music, Rested was 8 minutes.)
- [ ] **T11 Autoplay gives no comfort:** let a song play by itself for a minute. Expected: no meter, no Encore, no Music.
- [ ] **T12 Seated (E3):** sit on a chair (or press X), open the window and play, then perform. Expected: you stay
  seated the whole time (the clicks do not stand you up); stopping with a left or a right click, and closing the window
  with the right mouse button, do not stand you up either and never leave you blocking. Again with Settings →
  Accessibility → Toggle block on: after each right click you are not left in the block stance. With the lyre: when
  you sit down mid-song (X or a chair), the lyre moves onto your left thigh, its top leaning back toward your chest,
  both hands still on it; stand up (walk) mid-song: it moves back against your body.
- [ ] **T13 Stops by itself:** while a song plays: (a) `spawn Greyling`, `god` off, let it hit you: the song stops
  ("The hit cut your song short."); (b) stand in fire: it keeps playing; (c) press R: it stops at once; (d) equip a
  weapon from the hotbar: stops at once; (e) walk into deep water until you swim: stops at once. While performing:
  open the map, the inventory, the chat: not possible (keys held back); Esc stops it.
- [ ] **T14 Never punches:** stand next to a tree with an instrument in hand. Left click opens the window and never
  punches; Block blocks with the fists as with the hammer. Turn the mod off (MC Mods panel): left click does nothing
  (no punch, no stamina used). Turn it back on.
- [ ] **T15 Settings live:** change Volume (0.2), GameMusicVolume (1), NoteSpeed (2), Lane1Key (A) while playing or
  performing. Expected: each change applies at once (lane 1 now on A; with A the game does not walk left during the
  rhythm game), and the key shown under lane 1 changes to A at once (also while performing). A key the game cannot
  read (for example F15), Escape or Mouse1 turns that lane off with one warning in the log. Lane1Key = F5 (the console
  key): during the rhythm game F5 plays lane 1 and the console does not open. Put them back.
- [ ] **T16 Volume sliders:** Settings → Audio: lower the Sound effects slider while a song plays. Expected: the
  instrument gets quieter with it; Master too.
- [ ] **T17 Live toggle off while playing:** the pause menu itself ends the rhythm game and closes the window, so turn
  the mod off without it: (a) while a song plays by itself, untick Music Instruments in the MC Mods panel (Esc); (b)
  while performing, and (c) with the song window open, set `Enabled = false` in
  `BepInEx/config/MC.Exploration.Music.Instruments.cfg` from outside the game (or untick Enabled in ConfigurationManager,
  F1). Expected each time: the music stops at once, the arms come down, the rhythm game, its HUD and the window close,
  the game's keys and mouse work again, the game music comes back; the instrument stays in your hand; clicking does
  nothing (no punch); the recipes are gone. With Music running (get an Encore first, single player): its icon keeps
  counting down but shows no "+3", its tooltip says "Music heard nearby.", and the Resting comfort drops by 3. Turn it
  back on: everything works again and "+3" returns.
- [ ] **T18 Enabled = false + restart:** set Enabled = false, restart the game and load the character. Expected: the
  instruments are still in the inventory (not lost); no recipes; Status says off. Set it back to true.
- [ ] **T19 Logout while playing:** play a song, Esc, Logout, Yes. Expected: no sound, window or HUD left in the main
  menu. Load the world again: everything works.
- [ ] **T20 Pause:** single player. (a) Perform, then Esc. Expected: the rhythm game stops (the menu pauses the game)
  and no note keeps sounding. (b) Let a song play by itself, then Esc. Expected: it plays on behind the menu, like the
  game's music; close the menu: it goes on in time.
- [ ] **T21 Clean log:** after the other items, the log has no error or warning from Music Instruments
  (`./tools/Watch-Log.ps1 -Mine`), except the warning of T15's bad key.
- [ ] **T22 Window keys:** open the song window while holding W (or the stick forward): the selection does not run up
  the list. Up/Down choose a song, Left/Right cycle a MIDI file's part, Enter plays (the chat does not open); with a
  gamepad: D-pad up/down choose, A plays, Y toggles Repeat, B closes; X starts nothing and says the rhythm game is
  played on the keyboard (the gamepad help line does not offer it). Double-click a row: it plays.
- [ ] **T23 HUD:** perform: the lanes sit to the right of your character (it stays visible), a 3-2-1 countdown, key
  labels under the line, "Perfect"/"Good"/"Miss" near the line, the meter beside the lanes (gold filling, red
  draining, grey during rests), "Encore! +3 comfort" when it fills. Hide the HUD (Ctrl+F3): the rhythm game stays
  visible. While a song plays by itself: a small line with the title and time. Check at your usual GUI scale and once
  at a different one (Settings). The stop hint under the lanes says "Right click or Esc: stop", and "Start or right
  click: stop" once you touch a gamepad.
- [ ] **T24 Instrument swap:** play a song on the flute, then press the hotbar key of the lyre. Expected: the flute song
  stops at once (no flute song on the lyre), nothing stays posed.
- [ ] **T25 Freeze:** in windowed mode, let a busy song play by itself (Mead Hall Reel on the lyre), then hold the
  window's title bar for about two seconds (the game freezes) and let go. Expected: the song carries on in time; no
  burst of piled-up notes when the game comes back.

### Cross-mod (MC)

- [ ] **X01 Spyglass:** hold the spyglass, raise it; equip an instrument and play; equip the spyglass again. Expected:
  each acts only while it is in your hand; no stuck pose, view or sound.
- [ ] **X02 Sleep Through the Day:** with Music on (get an Encore), not Rested, sleep in a bed and wake up. Expected:
  "You feel rested (Comfort: N)" includes the +3.
- [ ] **X03 Encyclopedia / Crafting Search and Sort:** open the Encyclopedia (Valheim Compendium, Encyclopedia tab) and
  type in its search, or type in the crafting search, then close it and perform. Expected: keys behave normally in
  each (no stuck capture).
- [ ] **X04 Sort Chest / Crafting Search and Sort:** sort a chest with instruments, a hammer and a sword. Expected: the
  instruments are grouped with the tools.
- [ ] **X05 Dual Wielding:** with a dual-wield pair in hand, equip an instrument. Expected: the pair is put away, the
  instrument is never paired.

## 0.1.0 — multiplayer

- [ ] **M01 Server rules:** host (or dedicated server) with ComfortBonus 5, HearingRange 20 and FluteRecipe Wood:1; a
  friend with the mod joins. Expected: their log says "Using the server's rules: flute Wood:1..."; their workbench
  flute costs 1 Wood; their Encore gives +5.
- [ ] **M02 Others hear you (G4):** play a song next to your friend. Expected: they hear it from your position (louder
  close, softer and to the side as they walk around you), a fraction of a second after you, in time; they see your
  playing pose. At about 20 m (HearingRange 20) or more they hear nothing. On their screen: your hands move in time
  with what they hear (the lyre hand plucks, the flute bobs, the tambourine shakes); when you stop, swap instrument or
  die, your arms come down; sitting, you hold the lyre on your lap; a friend who arrives (or walks into range) while
  you already play sees you posed at once.
- [ ] **M03 Rhythm game heard:** perform next to your friend. Expected: they hear the notes you hit (not the ones you
  miss), steady, about a quarter of a second after you.
- [ ] **M04 Shared comfort (G5):** your friend sits near you (within 20 m); you get an Encore. Expected: they also get
  Music ("The music warms you: +3 comfort."), and resting by the fire their comfort is 3 higher (also outdoors); a
  third player farther than 20 m does not.
- [ ] **M05 MIDI without the file:** play one of your MIDI files. Expected: your friend (who does not have the file)
  hears it.
- [ ] **M06 Refused without the mod:** a friend without the mod joins a server with it (AllowPlayersWithoutMod false).
  Expected: about a second after loading in, their game shows "Incompatible version"; the server log names them.
- [ ] **M07 Hand-off to a player without the mod:** AllowPlayersWithoutMod = true, a friend without the mod joins.
  (a) Drop an instrument next to them: they see nothing there and cannot pick it up; it stays for you. (b) Hold an
  instrument and play: they see an empty hand, hear nothing, and nothing breaks for them. (c) Put an instrument and
  some wood in a chest; they open it (no instrument shown) and take the wood; you open the chest again: the instrument
  is gone, as the README warns.
- [ ] **M08 Pending rules:** join a server with the mod and left click with an instrument in the first second after
  loading (or watch the log). Expected: "The server has not sent the instrument settings yet." until the rules arrive.
- [ ] **M09 Dedicated server:** start a dedicated server with the mod (BepInEx installed there). Expected: it loads
  without errors (no graphics device: no models, no sound), players hear each other's music and share Encores, and
  dropped instruments stay on the ground after a server restart.
- [ ] **M10 Crossplay or a slow connection:** play with a friend over crossplay (or a laggy connection). Expected: the
  music they hear may skip a note now and then after a network hiccup, but never plays a burst of piled-up notes.
- [ ] **M11 Long notes and rests:** play a MIDI song with notes held for several seconds (a slow string or organ part,
  on the flute) and one with a long rest in it. Expected: your friend hears each long note to its end (never cut after
  about 3 seconds) and the song goes on after the rest.
