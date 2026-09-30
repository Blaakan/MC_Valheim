# Sneak Ambush — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also in the MC Mods panel). Smoke
test (`./tools/Test-Smoke.ps1`) passed 2026-09-30: loads, patches cleanly, JitCheck clean. In-world self-tests
(`./tools/Test-InWorld.ps1 -Mod Sneak.Ambush`) passed 2026-09-30 in a full run with every MC mod (71 of 71 tests):
`sneak.content`, `sneak.curve`, `sneak.still`, `sneak.cover`, `sneak.xp`, `sneak.smoke-throw`, `sneak.smoke-sight`,
`sneak.smoke-blind`, `sneak.healthbars`, `sneak.network`, `sneak.guard`, `sneak.pending`. Their NOTE lines in the log
record the values the design lists as unverified. No hands-on in-game test yet.

**Setup:** F5 for the console: `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). `god`
keeps you alive (type it again to turn it off), `die` kills you (with `god` off), `heal` refills health and stamina,
`killall` removes nearby creatures, `tame` tames the tameable creatures near you (a Boar). Spawn with `spawn <name>
<amount> <level>`: `spawn Greydwarf 1 1` gives one creature with no star (stars change the XP); add `p` to put items
straight into your inventory (`spawn MC_SmokeScreen 10 p`). Skills: `resetskill Sneak` (back to 0), `raiseskill Sneak
<levels>` (adds levels). Time and weather: `tod 0` (midnight), `tod 0.5` (noon), `tod -1` (normal time); `env <name>`
forces a weather, `env` alone lists the names, `resetenv` gives the normal weather back. Names checked in the 1.0.16
game data:

- creatures: `Greydwarf` (40 health), `Greyling`, `Troll` (600), `Skeleton`, `Boar` (10), `Neck`, `Deer`,
  `Draugr_sleeping` (wakes up within 10 m), `Eikthyr` (a boss; it hunts you as soon as it spawns),
  `piece_TrainingDummy`;
- items: `KnifeFlint`, `KnifeCopper`, `SwordIron`, `Bow`, `CrossbowArbalest`, `ShieldWoodTower`, `SpearChitin` (the
  harpoon), `BombSmoke` (the game's Smoke Bomb, which stays unchanged).

Not in the checked data **(unverified)**: the materials `Resin`, `Coal`, `LeatherScraps` and the station
`piece_workbench` (`sneak.content` checks them), the ammo `ArrowWood` and `BoltBone`, the plants `Bush01`,
`RaspberryBush` (`sneak.cover` checks `Bush01`) and `Beech1`, the ship `Karve` (any ship works), and the weather names
`Misty`, `Clear`, `SwampRain`, `Darklands_dark` (`env` alone lists the real ones; `sneak.cover` forces them). This
mod's own names: `MC_SmokeScreen` (the Smoke Screen) and `MC_SmokeScreen_cloud` (a cloud with the default settings,
without a throw).

Most expected results name Debug log lines: turn on Debug logging with `./tools/Setup.ps1 -DevBepInExConfig` and
follow the log with `./tools/Watch-Log.ps1 -Mine`. Settings are changed with ConfigurationManager (F1) or in
`BepInEx/config/MC.Combat.Sneak.Ambush.cfg`; default settings unless an item says otherwise, and put each changed one
back afterwards. A creature's health bar shows only within 30 m and for 60 s after your crosshair was on it (normal
game; `sneak.healthbars` notes both values): in every health bar item, aim at the creature first. Run the items
outside "other mods" with Creature Morale turned off in the MC Mods panel (or not installed): creatures that are afraid
of you run from you and give no sneak attack while they see you (X01 covers it). Use a world with the default
skill-gain world modifier.

## 0.1.0 — single player

- [ ] **T01 Sneak attack gives XP:** `resetskill Sneak`, `spawn Greydwarf 1 1`, wait until it wanders without an alert
  icon, crouch up behind it and hit it with `KnifeFlint`. Expected: the backstab effect; "Sneak attack!" at the top left
  and Sneak goes from 0 to 1 with the game's level-up message; Debug "Sneak attack on … by …: sent 40 health to their
  game for Sneak XP." and "Sneak attack: 7 Sneak XP, Sneak level up."
- [ ] **T02 Once per creature:** `spawn Greydwarf 1 1`, sneak attack it with bare hands (the hit does not kill it; a
  Debug "Sneak attack: … Sneak XP" line), let it chase you, throw a Smoke Screen at your feet and move a few metres
  inside the cloud, wait until it gives up (its alert icon goes off and it walks away), then sneak up on it again and
  hit it from behind within 5 minutes. Expected: no backstab, no XP, no message (the normal game allows one backstab
  per creature every 5 minutes).
- [ ] **T03 Ranged pays half:** `resetskill Sneak`, `raiseskill Sneak 5`, not Rested (or Rested for both hits). Sneak
  attack a fresh level-1 Greydwarf with `Bow` and `ArrowWood`, and another with `KnifeFlint`. Expected: the bow gives
  "Sneak attack! +N% Sneak" with about half the knife's N (about 22% against 45% without Rested); Debug "Sneak attack:
  3.5 Sneak XP (ranged)." against "Sneak attack: 7 Sneak XP."
- [ ] **T04 Bigger creatures pay more:** sneak attacks on a level-1 `Troll` and a level-1 `Boar`. Expected: Debug
  "sent 600 health" then "Sneak attack: 33 Sneak XP", and "sent 10 health" then "Sneak attack: 4 Sneak XP".
- [ ] **T05 No XP from tames and dummies:** `spawn piece_TrainingDummy`; `spawn Boar 1 1` and `tame`. Sneak attack each
  from behind. Expected: a backstab can happen, but no XP, no message and no "Sneak attack" Debug line.
- [ ] **T06 Early-game curve:** `resetskill Sneak`, `tod 0.5`, open ground away from trees. Crouch and crawl slowly in
  small circles (so holding still never starts) and look at the stealth bar after about 3 s. Expected: about 85% full
  (the normal game: full). `raiseskill Sneak 100`, same. Expected: about 60% full (the normal game's value).
  `resetskill Sneak` afterwards.
- [ ] **T07 Holding still:** Sneak 0, `tod 0.5`, open ground: crouch and do not move. Expected: after about 1 s a
  "Holding still" icon reading "-70%"; the bar sinks to about a quarter over 2-3 s; turning the camera keeps it. A
  Greydwarf wandering past 8-10 m in front of you does not notice you. Control: crawl slowly while one passes at the
  same distance: it notices you and comes over.
- [ ] **T08 Holding still ends:** from T07, take one step. Expected: the icon goes and the bar jumps up at once, then
  ramps as usual. Standing up, jumping, and taking a hit (`god` off, let a Greydwarf hit you) each end it the same way.
  On a ship (`Karve` (unverified), any ship works) with the sail set and nobody at the helm, crouch on deck while it
  moves. Expected: no Holding still icon. Crouch, hold still until the icon shows, then draw a `Bow`: note whether the
  icon stays (it depends on whether the game keeps you crouched while drawing; both are fine).
- [ ] **T09 In foliage icon:** crouch against a raspberry bush or a Bush01 (`spawn RaspberryBush` / `spawn Bush01`,
  unverified). Expected: an "In foliage" icon without a number; its entry in the compendium's Active effects page
  explains the sight block and the shade; at noon the bar drops a little (shade). Crouch under a big tree (beech, oak).
  Expected: the same icon. Step into the open. Expected: the icon goes about 1 s later.
- [ ] **T10 Bushes block sight:** crouched behind a bush, an unaware Greydwarf walking toward you on the other side.
  Expected: it does not see you until it walks round the bush.
- [ ] **T11 Fog icon:** crouched outdoors in the Meadows: `env Misty` and `tod 0`. Expected: a "Fog" icon reading
  "-30%". `tod 0.5`: "-4%". `env Clear`: no fog icon. Standing up: no fog icon. `resetenv`, `tod -1`, in the Swamp:
  a fog icon of 4-9% in its rain and dark weathers. Inside a dungeon (a Burial Chamber): no fog icon.
- [ ] **T12 In the mist:** in the Mistlands mist, without a Wisplight, crouch. Expected: an "In the mist" icon without a
  number; it goes when you stand up.
- [ ] **T13 Recipe:** a character that never picked up Resin, Coal or Leather scraps, next to a workbench
  (`spawn piece_workbench`, unverified). `spawn Resin 2 p`, `spawn Coal 1 p`, `spawn LeatherScraps 1 p`. Expected: the
  Smoke Screen recipe appears at the workbench once all three were picked up; crafting uses 2 Resin + 1 Coal + 1
  Leather scraps and gives 2 Smoke Screens; the item's tooltip shows its description ("A pouch of soot and resin. …").
- [ ] **T14 Throw:** `spawn MC_SmokeScreen 10 p`, equip it, throw it at the ground 6 m away (an unaware Greydwarf 12 m
  away), then at a Greydwarf, then into deep water, then onto a lit campfire. Expected: the throw looks like the game's
  Smoke Bomb; the stack goes down by one per throw; one grey cloud about 8 m wide where it lands (on the creature: at
  its feet; on water: on the surface); it hides until about 15 s after it lands (the smoke starts thinning a little
  before that) and is gone about 4 s later. No damage numbers, no Smoked icon or choking, the campfire keeps burning,
  no Smoke Screen dropped back; the Greydwarf 12 m away and the one that was hit stay unaware.
- [ ] **T15 Hidden inside:** stand in a cloud, an unaware Greydwarf 6-10 m away outside it, facing you. Expected: it
  does not notice you, even when you walk or run inside the cloud (it cannot hear you either). An "In smoke" icon
  while you are inside (crouched or standing); it goes about 1 s after you leave.
- [ ] **T16 Hidden from inside, health bar:** aim at an unaware Greydwarf within 10 m so its bar shows, throw the Smoke
  Screen onto it and stay outside the cloud. Expected: its bar disappears within a moment and it does not notice you,
  even facing you. After the cloud ends, its bar shows again once you aim at it.
- [ ] **T17 Both inside:** you and a Greydwarf inside the same cloud (`spawn MC_SmokeScreen_cloud` next to it, or a
  throw). Expected: it notices you only when about 2.5 m away.
- [ ] **T18 Cloud between:** neither of you inside, the cloud between you and an unaware Greydwarf facing you about 15 m
  away; stand up. Expected: it does not see you through the cloud; step sideways out of the cloud's line: it sees you.
- [ ] **T19 Burst blinds pursuers:** get chased by two or three Greydwarfs and aim at them so their bars show, throw a
  Smoke Screen at your feet and move a few metres inside the cloud. Expected: they stop attacking; Debug "Smoke Screen
  burst: 2 creature(s) chasing a player nearby lose their sight for … s." (the number they are); within about 3-4 s
  their alert icons go off and they walk back the way they came. After about 6 s, once you leave the smoke, they can
  find you again the normal way.
- [ ] **T20 Hits reveal:** the first hit must not kill (an unaware creature takes the backstab, x3): `god` on,
  `resetskill Bows` (at Bows 0 a full-draw `Bow` shot with `ArrowWood` does 11-24 damage, 33-73 as a backstab; the
  arrow's 22 pierce is the normal game's value, unverified), and starred targets. Stand inside a cloud, two unaware
  two-star Greydwarfs outside it (`spawn Greydwarf 1 3`: 120 health), about 15 m from each other. Shoot one at full
  draw. Expected: a backstab of about 33-73 damage that it survives; that one comes into the smoke and fights you; the
  other does not notice you (it may walk over to look where the arrow came from, normal game, but does not find you
  while you stay more than 2.5 m from it). From inside the smoke, shoot a level-9 Deer (`spawn Deer 1 9`: 90 health)
  the same way. Expected: it survives the hit (about 33-73) and flees from you. A kill is not a result (higher Bows
  skill, other bow or arrows, a target without stars): redo it with the setup above.
- [ ] **T21 Bosses:** `god` on, stand inside a cloud and `spawn Eikthyr` next to it. Expected: it sees you and attacks;
  its health bar stays; it is not blinded. `killall` afterwards.
- [ ] **T22 Health bars both ways:** you inside a cloud, a Greydwarf outside it within 10 m that you aimed at. Expected:
  its bar shows (`HealthBars = OnlyInside`). Set `HealthBars = ThroughSmoke`. Expected: its bar goes. Put it back to
  `OnlyInside`.
- [ ] **T23 Death:** `env Misty`, `tod 0`, crouch and hold still until the Holding still and Fog icons show; `god` off,
  `die`. After the respawn, crouch again. Expected: the icons come back while crouching; no error in the log.
- [ ] **T24 Throw refused while off:** untick Sneak Ambush in the MC Mods panel, try to throw a Smoke Screen. Expected:
  no throw; the message "Smoke Screen does nothing here: Sneak Ambush is turned off (or the server does not have it)."
  at the top left (at most every 3 s); the stack is unchanged. Tick it again: the throw works.
- [ ] **T25 Sleeping creatures:** `spawn Draugr_sleeping`, throw a Smoke Screen about 7 m from it and walk into the
  cloud. Expected: the Draugr wakes up when you come within about 10 m (normal game), but it does not come for you while
  you stay in the smoke more than 2.5 m from it.
- [ ] **T26 Tames keep their bars:** `spawn Boar 1 1` and `tame`, aim at it so its bar shows, throw a Smoke Screen onto
  it and stay outside the cloud. Expected: its bar stays (the smoke hides players only; T16 shows a wild creature's bar
  going).
- [ ] **L01 Live toggle:** in fog (`env Misty`, `tod 0`), crouch and hold still inside an active cloud, with an
  unaware Greydwarf outside it about 8 m from you, facing you; then untick Sneak Ambush in the MC Mods panel. Expected:
  every icon goes, the stealth bar climbs back to the normal game's value, the Greydwarf notices you (the smoke still
  shows until it ends), the Smoke
  Screen recipe is gone from the workbench, throws are refused. Tick it again. Expected: icons, bonuses and recipe come
  back, the cloud hides you again, throws work.
- [ ] **L02 `Enabled = false` + restart:** with Smoke Screens in your inventory and in a chest, set `Enabled = false` in
  `BepInEx/config/MC.Combat.Sneak.Ambush.cfg` and start the game. Expected: `Status` reads "Off (disabled in
  settings)."; the Smoke Screens are still in the inventory and the chest and can be moved and dropped; the recipe is
  hidden; throws are refused with the message; no stealth icon; no error in the log.
- [ ] **L03 Clean log:** play through T01-T26 while `./tools/Watch-Log.ps1 -Mine` runs, then quit the game from the
  world (Esc, Logout, Exit). Expected: no error and no warning from Sneak Ambush (in particular no "Could not find the
  smoke look" and no recipe warning, also not while the game quits).
- [ ] **L04 Turned on mid-game:** start the game with `Enabled = false`, hit a few Greydwarfs (they fight you), then
  tick the mod on and do T20 with its setup (`resetskill Bows`, two-star Greydwarfs, so the shot does not kill).
  Expected: the Greydwarf you shoot from the smoke survives, sees you and fights you.

## 0.1.0 — multiplayer

- [ ] **M01 Server rules:** a dedicated server (or a host) and two players, all with the mod. Set `StillBonus = 50` in
  the server's config while they play. Expected: both players log Info "Using the server's rules: … holding still 50%
  …" and their Holding still icon reads "-50%"; their own config files are unchanged. Put it back to 70.
- [ ] **M02 Player without the mod refused:** a player without the mod joins. Expected: about a second after joining
  their game goes back to the menu with "Incompatible version"; the server logs a Warning "Refused <player>: does not
  have the mod. This server requires Sneak Ambush on every player …". With `AllowPlayersWithoutMod = true` on the
  server they can play, and the server logs a Warning "<player> does not have the mod; AllowPlayersWithoutMod is on, so
  they may play, …". A player with the mod on joins normally (Debug "<player> has Sneak Ambush on, with the same
  network version: allowed.").
- [ ] **M03 XP across games:** player B spawns a level-1 Greydwarf (`spawn Greydwarf 1 1` on B's game, so B's game
  controls it); player A sneak attacks it. Expected: A gets the XP and the message; Debug "Sneak attack on … by A:
  sent 40 health to their game for Sneak XP." in B's log and "Sneak attack: 7 Sneak XP" in A's.
- [ ] **M04 Smoke across games:** B spawns two Greydwarfs; A throws a Smoke Screen near them, A and B inside the cloud.
  Expected: the Greydwarfs see neither A nor B; both players see the same cloud for the same time. A player outside
  who aimed at a Greydwarf inside sees its bar go.
- [ ] **M05 Holding still across games:** A holds still crouched near a Greydwarf that B spawned. Expected: it notices
  A only as close as in T07.
- [ ] **M06 Hand-off through a chest:** `AllowPlayersWithoutMod = true` on the server. Put a Smoke Screen and another
  item in a throwaway chest. (a) A player without the mod opens and closes it. Expected: the Smoke Screen is still there
  for players with the mod. (b) They open it, take the other item and close it. Expected: the Smoke Screen is gone for
  everyone (documented loss). Also: creatures that player's game controls ignore the smoke, and their log shows
  "Missing prefab hash" warnings while they are near a cloud (documented).
- [ ] **M07 Thrower disconnects:** B stands in A's cloud, Greydwarfs outside; A disconnects. Expected: the cloud stays
  for B and still hides B until its normal end, then disappears.
- [ ] **M08 Server without the mod:** join a server without the mod. Expected: the MC Mods panel shows "Inactive: the
  server does not have this mod. …"; the recipe is hidden, throws are refused with the message, Smoke Screens stay in
  the inventory.
- [ ] **M09 Blind on another game's creatures:** B spawns two Greydwarfs that chase A; A throws a Smoke Screen at his
  feet. Expected: both give up within about 4 s (their alert icons go off on both screens). A then hits one from the
  smoke: it fights A.
- [ ] **M10 Thrower teleports:** A throws a Smoke Screen, B stands inside it; A goes through a portal (or dies and
  respawns at a far bed). Expected: the cloud stays for B and still hides B until its end.
- [ ] **M11 Other fights untouched:** B fights a Greydwarf in melee about 10 m from the edge of the spot where A throws
  a Smoke Screen. Expected: it keeps fighting B.
- [ ] **M12 Mod turned off, allowed in:** `AllowPlayersWithoutMod = true` on the server; a player with the mod and
  `Enabled = false` joins. Expected: they stay; the server logs a Warning "<player> has the mod turned off;
  AllowPlayersWithoutMod is on, …"; creatures their game controls ignore the smoke (documented). They sneak attack a
  creature controlled by a player with the mod on. Expected: no XP for them and no warning in either log.
- [ ] **M13 Dedicated server:** a dedicated server with the mod and one player. A dedicated server never controls
  creatures or creates world objects (the players' games do), so this checks what it does do. (a) Do T15 and T19 there,
  with wild Greydwarfs or spawned ones (on a dedicated server, `devcommands` needs your ID in the server's
  `adminlist.txt`). Expected: the same results as in single player (your game controls the creatures). (b) Turn on
  Debug logging on the server (in its `BepInEx/config/BepInEx.cfg`, add `Debug` to `LogLevels` under
  `[Logging.Disk]`) and start it. Expected: its `BepInEx/LogOutput.log` has Debug "Made the Smoke Screen from
  BombSmoke: item MC_SmokeScreen, projectile MC_SmokeScreen_projectile, cloud MC_SmokeScreen_cloud…" (the Smoke Screen
  is registered on the server). (c) Put a Smoke Screen in a chest, drop one on the ground, throw one, and stop the
  server while its cloud is still up (Ctrl+C in the server window saves the world); start it again and rejoin.
  Expected: the server log has no "ZDOs with unknown prefabs" warning at start-up (a server without the mod warns
  about these objects there; another mod's objects can cause a warning of their own); the dropped Smoke Screen and the
  one in the chest are still there; no error from Sneak Ambush in the server's log or yours.
- [ ] **M14 Server with `Enabled = false`:** dedicated server with the mod, a Smoke Screen dropped on the ground; set
  `Enabled = false` on the server and restart it. Expected: the Smoke Screen is still there; the server log has no "ZDOs
  with unknown prefabs" warning at start-up (the Smoke Screen stays registered while the mod is off); clients see the
  recipe hidden and throws refused. Then set `Enabled = true` on the server without a restart. Expected: throws work
  and the cloud hides.
- [ ] **M15 Mod turned off, refused:** `AllowPlayersWithoutMod = false` (default) on the server. (a) A player with the mod
  and `Enabled = false` joins. Expected: about a second after joining their game shows "Incompatible version" and goes
  back to the menu; the server log says "Refused <player>: has the mod turned off". (b) A player with the mod on joins
  and plays, then unticks Sneak Ambush in the MC Mods panel. Expected: their log says "Told the server that Sneak Ambush
  is now off on this game."; about a second later they are refused the same way. (c) Untick and tick it again within a
  second. Expected: they stay. (d) Another player with the mod on is never affected.
- [ ] **M16 Other network version:** build a copy with another network version (`dotnet build
  src/Combat/Sneak.Ambush/MC.Combat.Sneak.Ambush.csproj -p:ModNetworkVersion=2 -p:DeployToGame=false`), copy that DLL
  over a second player's copy (put the normal build back afterwards), and join a server with network version 1.
  Expected: refused about a second after joining; the server log says "has another version of the mod (network
  version 2, the server has 1)". With `AllowPlayersWithoutMod = true` they stay: their MC Mods panel says the versions
  cannot talk to each other, Smoke Screens stay in their inventory and throws are refused.

## 0.1.0 — other mods

- [ ] **X01 Creature Morale (MC):** (same test as Creature Morale's X01) boss rank 6 in Creature Morale (its Debug
  setting `ForceBossRank = 6`, or a character that helped kill The Queen), in the Meadows: plain and two-star
  Greydwarfs are afraid of you (they run when you come within about 12 m and they see or hear you). Standing, shoot an
  afraid Greydwarf that faces you from about 15-20 m (it sees you but does not run yet) with the `Bow`. Expected: no
  backstab and no Sneak XP. Crouch-sneak up behind another, out of its sight, and hit it with `KnifeFlint`; from inside
  a Smoke Screen cloud, shoot a third that is outside the cloud or more than 2.5 m from you. Expected: backstabs that
  pay (Debug "Sneak attack: 7 Sneak XP" for the knife, "Sneak attack: 3.5 Sneak XP (ranged)" for the arrow). Crawl
  next to afraid creatures. Expected: Sneak rises only slowly. From inside a Smoke Screen cloud, walk up to an afraid
  Greydwarf (if it is inside the cloud too, stay more than 2.5 m from it). Expected: it does not run (it cannot see or
  hear you). From inside the cloud, shoot an afraid two-star Greydwarf (`spawn Greydwarf 1 3`, 120 health) with the
  `Bow` at Bows 0, as in T20, so the hit (a backstab of about 33-73) does not kill it. Expected: it targets you and
  fights you through the smoke; the other afraid Greydwarfs do not notice you. Stand in the smoke next to an afraid
  creature that has not noticed you. Expected: it is never cornered. Throw a Smoke Screen while creatures chase you
  (set rank 0 first, so they attack you). Expected: they forget you; a routing pack is unaffected. An afraid creature
  whose health bar the smoke hid gets its bar back once it is out of the smoke and you aim at it, with the game's red
  alert icon while it runs from you and no icon once it calms down. No errors.
- [ ] **X02 Tower Shield Wall (MC):** (= Tower Shield Wall's C08) crouch and hold still until the icon shows, then raise
  a `ShieldWoodTower`. Expected: the crouch and the Holding still icon end. A shield bash on an unaware Greydwarf:
  no backstab and no Sneak XP. From inside a Smoke Screen cloud, bash a Greydwarf. Expected: it sees you and fights
  you. NG+ variant, in a world at world level 1 (setup: Tower Shield Wall's T23): bash an unaware Greydwarf, then hit it
  with a knife. Expected: the knife hit is no sneak attack and pays no Sneak XP (the bash alerted it).
- [ ] **X03 Dual Wielding (MC):** (Dual Wielding's X04 checks the same from its side) with `KnifeCopper` and
  `SwordIron` equipped as a pair, equip a Smoke Screen. Expected: the pair is put away and the Smoke Screen never goes
  to the off hand. Sneak attack an unaware Greydwarf with the pair. Expected: one "Sneak attack" message and XP once.
- [ ] **X04 Weapon Moveset (MC):** (same as Weapon Moveset's X04 (a)-(c)) jumping or rolling with a Smoke Screen
  equipped, then attacking. Expected: the plain throw. With Creature Morale off (or a character with no boss kills, or
  from behind the Greyling), a sneak roll (crouch + jump) into a roll attack on an unaware `Greyling`. Expected: a
  backstab that pays sneak-attack XP once. With Morale on at boss rank 3 (its `ForceBossRank = 3`), in the Meadows, an
  afraid Greyling that faces you (it sees you from beyond 12 m before it runs; a running one is alert anyway).
  Expected: no backstab and no XP.
- [ ] **X05 Harpoon Hooks Tames (MC):** hook a tamed Boar with `SpearChitin`. Expected: no Sneak XP, no errors.
- [ ] **X06 Encyclopedia (MC):** a creature you only ever saw inside the smoke (bar hidden). Expected: it is not "met"
  until its bar shows.
- [ ] **X07 Forge Idol Upgrades (MC):** both installed. Expected: both load, the Forge of Potential works, the Smoke
  Screen can be crafted.
- [ ] **X08 Crossbow Stays Loaded (MC):** sneak attack an unaware Greydwarf with `CrossbowArbalest` (bolts `BoltBone`,
  unverified). Expected: Debug "Sneak attack: 3.5 Sneak XP (ranged)."; Crossbow Stays Loaded works as usual.
- [ ] **X09 Creature Kill and Tame Counts (MC):** A hits a Greydwarf with a Smoke Screen and B kills it. Expected: both
  get the kill (the normal game's "took part" credit, documented).
- [ ] **X10 Other sneak-XP mods (optional):** with SecondaryAttacks (or SmartSkills) installed. Expected: Info
  "SecondaryAttacks is installed: it pays Sneak XP for sneak attacks, so Sneak Ambush pays none on this game unless
  PayAlongsideOtherSneakXpMods is on. …" (or the SmartSkills line) at the first stealth refresh; a sneak attack gives
  Debug "Sneak attack: no Sneak XP from Sneak Ambush, another installed mod pays it (PayAlongsideOtherSneakXpMods is
  off)."; with `PayAlongsideOtherSneakXpMods = true` this mod pays too. The holding-still and fog icons and bonuses
  still work.
