# Creature Morale — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, reworked on 2026-09-30 after the first in-game test (build `3cda33b+dirty`): creatures are
afraid of a player two bosses past their biome's boss, judged by the biome they spawned in, and run from that player
instead of ignoring them. Build id = the commit in the `[MC:ready]` log line (also in the MC Mods panel). The Debug and
Release builds are clean; the smoke test (`./tools/Test-Smoke.ps1`) and the in-world self-tests
(`./tools/Test-InWorld.ps1 -Mod Creatures.Morale`: `morale.rules`, `morale.publish`, `morale.calm`, `morale.provoke`,
`morale.stealth`, `morale.rout`) have **not run on the reworked build yet** (the first build passed both on
2026-09-30, 71 of 71 self-tests). Their NOTE lines in the log record the values the design lists as unverified. No
hands-on in-game test of the reworked build yet. Changed later on 2026-09-30 (user feedback, not run in the game yet):
no more "afraid" / "routed" line under the health bar and no `ShowOnNameplates` setting; a creature that runs from a
player, or a routed pack, shows the game's own alert icon and loses it when it calms down. Changed again after the
review of that change (not run in the game yet): a run (fear or rout) that ends next to a player the creature will
fight keeps its alert for that fight (T34), and a creature that goes for a building after its run no longer keeps the
run's alert. **The game folder may still hold an older build** (these changes were built without deploying): close
the game and run `dotnet build ValheimMods.slnx` before testing. The right build shows this change's build id in the
MC Mods panel, and ConfigurationManager's `Display` section has no `ShowOnNameplates` setting (an older build still
has it).

**Setup:** use a Debug build (`dotnet build ValheimMods.slnx` deploys it to the game). It adds the setting
`ForceBossRank` (section `[Debug]` of `BepInEx/config/MC.Combat.Creatures.Morale.cfg`, applied while the game runs):
-1 = your real boss rank, 0 = no boss, 1-8 = Eikthyr, The Elder, Bonemass, Moder, Yagluth, The Queen, Fader, Kall. "Rank
N" below means `ForceBossRank = N` (or a character that has really helped kill that boss: `spawn Eikthyr` and kill it,
spawned kills count). Kill counts still come from your character. With the default settings a creature is afraid of a
player two bosses past its biome's boss: Meadows creatures from rank 3, the Black Forest from rank 4, the Swamp and the
Ocean from 5, the Mountains from 6, the Plains from 7, the Mistlands from 8; elites and pack leaders one rank later;
each star one rank later. F5 for the console: `devcommands` (if the console asks you to confirm cheats, run
`confirmcheats`), `god` to survive while testing (creatures still attack a god-mode player), `nospawn` to stop natural
spawns so only your creatures are around, `spawn <name> <amount> <level>` (level 2 = 1 star, 3 = 2 stars), `killall`,
`tame`, `event <name>`, `setkey <key>`, `goto <x> <z>` (to test other biomes; the `morale.rules` self-test NOTE "biome
points found" lists a Mountains, Plains and Deep North point of the test world, your map shows yours). Creatures
spawned with `spawn` count as spawned where you stand, so **stand in the Meadows** for every step unless it says
otherwise: a Boar, Neck or Greyling spawned in the Black Forest is a Black Forest creature and needs one more boss
(the Greydwarf steps at rank 4 also work in the Black Forest). Do not use `ghost` or `debugmode` flying: creatures ignore such
players. Watch the log with `./tools/Watch-Log.ps1 -Mine`; Debug lines need the Debug log level
(`./tools/Setup.ps1 -DevBepInExConfig`). At spawn and on every change the log shows "Standing: boss rank N (boss),
kill bonuses: ..." ("forced for testing" with ForceBossRank). Name plates (health bars) show only within 30 m and for
60 s after your crosshair passed over a creature: aim at each creature you check. "Runs away" = the creature turns and
runs with the game's red alert icon (the "!" above its name); "calms down" = the alert icon goes away. The mod adds no
line or text of its own to any health bar. For kill-step tests, note your
character's kill counts first (MC Creature Kill and Tame Counts shows them), or use a fresh character. A config file
from the first builds keeps its old `[Calm creatures]` and `[Frightened]` entries and a `ShowOnNameplates` line under
`[Display]`; they are no longer read and can be deleted.

Names checked in the 1.0.16 game data: creatures `Boar`, `Neck`, `Hen`, `Greyling`, `Greydwarf`, `Greydwarf_Shaman`,
`Greydwarf_Elite` (Greydwarf Brute), `Troll`, `Skeleton`, `Skeleton_Swamps`, `Skeleton_Mountains`, `Draugr`,
`Draugr_sleeping`, `Draugr_Elite`, `Wolf`, `Ulv`, `Lox`, `Goblin` (Fuling), `GoblinArcher`, `GoblinBrute` (Fuling
Berserker), `GoblinShaman`, `Fenring`, `Fenring_Cultist`, `Seeker`, `SeekerBrood`, `SeekerBrute` (Seeker Soldier),
`Charred_Melee`, `Charred_Mage`, `Greydwarf_Frozen`, `Greydwarf_Shaman_Frozen`, `Eikthyr`, `Deer`, `Dverger`,
`piece_TrainingDummy` (or build a training dummy with the hammer); items `Bow`, `SpearFlint`, `KnifeFlint`, `Club`,
`BombOoze` (Ooze bomb), `SpearChitin` (Abyssal Harpoon), `ShieldWoodTower`; world key `defeated_goblinking` (Fuling
night hunters). Not in the checked data **(unverified)**: the arrows `ArrowWood`, the food `Mushroom` and `RawMeat`, the
raid `army_eikthyr`, the English creature names in messages.

## 0.1.0 — single player

- [ ] **T01 Afraid boars and necks:** rank 3, in the Meadows; `spawn Boar 3` and `spawn Neck 3`, walk 25 m away, then walk back
  toward them, aiming at each. Expected: while you are far, their health bars look as in the normal game (no alert
  icon, nothing under the bar); they never attack you; when you come within about 12 m and they see or hear you, they
  run away and each one you aim at shows the red alert icon ("!" above its name) while it runs; once you are about
  16 m from them they calm down, the alert icon goes away and they wander again. No health bar ever shows a line or
  word of the mod's own ("afraid", "routed").
- [ ] **T02 Contrast at rank 2:** same place with rank 2 (The Elder: one boss short). Expected: boars and necks
  behave as in the normal game (they notice you and charge when you come close).
- [ ] **T03 Sleep and rest near afraid creatures:** rank 4; build a bed and a campfire, `spawn Greydwarf 3` about 10 m
  away. Expected: they run from you but never attack; at the fire you get Rested; at night you can sleep in the bed
  (no message about enemies nearby); no combat music.
- [ ] **T04 Boss kill raises the rank:** a character that has killed no boss, `ForceBossRank = -1`: `spawn Eikthyr`,
  kill it. Expected: Eikthyr's own death message, **no** "Weaker creatures now keep out of your way" (nothing is afraid
  of a rank 1 player with the default settings); the log shows "Standing: boss rank 1 (Eikthyr), ..."; boars still
  attack you. Then set `BossesAhead = 0`, `spawn Eikthyr` again and kill it. Expected: about 4-5 s after the kill, once
  Eikthyr's death message has faded, "Weaker creatures now keep out of your way" in the middle of the screen; boars
  and necks are now afraid of you. Put `BossesAhead` back to 2.
- [ ] **T05 Kill step:** rank 2, in the Meadows; note your Greyling kill count N. Set `KillSteps` = N+3 (the number), then kill 3
  Greylings (`spawn Greyling`). Expected: after the third, a corner message "Greyling: N+3 kills. They will be afraid
  of you sooner."; the Standing log line shows "Greyling +1"; other Greylings are now afraid of you (one boss early),
  Boars still attack you. Put `KillSteps` back to `100, 400`.
- [ ] **T06 Kills bring a kind at most one boss early:** rank 2; note your Greydwarf kill count N; set `KillSteps` =
  `N+1, N+2` (the numbers) and kill two Greydwarfs. Expected: no corner message, and other Greydwarfs still attack you
  (they need Moder, rank 4; kills bring a kind at most one boss early, so 2 + 2 is not enough). Set rank 3: now they
  are afraid. Put `KillSteps` back.
- [ ] **T07 Stars:** rank 4: `spawn Greydwarf 1 3` (2 stars) and `spawn Greydwarf` next to it. Expected: the 2-star
  one attacks you, the plain one runs from you. Rank 5, in the Meadows: `spawn Boar 1 3` (2 stars): afraid.
- [ ] **T08 Ranks across biomes:** rank 6, in the Meadows: `spawn Troll`, `spawn Draugr`, `spawn Wolf`,
  `spawn Goblin`, `spawn GoblinBrute`, one at a time (creatures of different biomes fight each other; `killall`
  between them). Expected: Troll, Draugr and Wolf are afraid (they run when you come close); the Fuling and the Fuling
  Berserker attack you.
- [ ] **T09 Standing changes mid-chase:** rank 2: let a Boar chase you, then set rank 3. Expected: within about 2 s it
  turns and runs away from you; once you are about 16 m away it calms down and wanders again.
- [ ] **T10 Provoked by a hit:** rank 4: catch an afraid Greydwarf (or corner it, T13) and hit it once. Expected: it
  stops running and fights you (the alert icon stays on, no second alert sound). Stop fighting, walk 20 m away and wait
  35 s. Expected: no alert icon, it wanders and does not come for you; walk up to it: it runs (alert icon again).
- [ ] **T11 Near misses:** rank 4, `Bow` and arrows (`ArrowWood`, unverified name). From 20 m (outside its fear
  range), shoot an arrow into the ground within 2 m of an afraid Greydwarf. Expected: it comes for you and attacks. Shoot
  a `Deer` standing 5-7 m from afraid Greydwarfs, from 20 m (hit or miss): farther than NearMissRange (4 m) but inside
  the bow's 8 m noise range, where the normal game would alert them. Expected: they do not come for you. Throw a
  `SpearFlint` at a tree 10 m from them, from 20 m (inside the spear's 30 m noise range). Expected: they do not come for
  you.
- [ ] **T12 Fire- or poison-only hit:** rank 4: from about 15 m, hit an afraid Greydwarf with a hit that deals only
  fire or poison damage, for example an Ooze bomb (`BombOoze`; the `morale.rules` self-test notes list the player
  bombs' damage: poison only). Expected: it comes for you and fights.
- [ ] **T13 Cornered:** rank 3, in the Meadows, `nospawn`. (a) Build a small pen (or use a spot where a creature cannot get away, for
  example a corner between two walls), `spawn Boar` inside, walk in and stay within 2-3 m of it. Expected: it tries to
  run, cannot get away, and after about 2 s it attacks you. (b) Chase an afraid Boar in the open and stay right behind
  it: if you keep within about 3 m for 2 s, it turns and attacks; if it outruns you, it just runs. (c) Keep 5 m from a
  trapped one: it stays afraid and never attacks. (d) Crouch-sneak up behind an unaware afraid Boar (out of its sight,
  quietly) and stay next to it: it does not notice you, never runs and never attacks.
- [ ] **T14 No free sneak attacks:** rank 4: an afraid Greydwarf that faces you from beyond 12 m (it sees you but
  does not run yet): walk up and hit it. Expected: no sneak-attack effect or bonus damage (it runs as soon as you are
  within 12 m, and a running creature is alert: no sneak attack either); it fights you. Crouch-sneak up behind another
  one, out of its sight and quietly, and hit it. Expected: a normal sneak attack. Walk (not crouched) up behind a third
  one. Expected: it hears you within about 12 m and runs (a walking player is heard; the first version's free sneak
  attack from a noisy approach is gone). Crawl for 30 s near afraid Greydwarfs only. Expected: the log (Debug level)
  shows "Sneak: only afraid creatures near, slow rate."; crawling near an unaware hostile creature (rank 0) does not
  show it.
- [ ] **T15 Rout:** rank 0: `spawn Troll`, then `spawn Greydwarf 4` near it; kill the Troll. Expected: the
  Greydwarfs within about 25 m turn and run away from where it fell for about 15 s, each one you aim at showing the
  alert icon while it runs (no "routed" line or other text under the bar); the log (Debug level) shows "Rout:
  $enemy_troll died, applied to 4 creature(s) here."; when the rout ends, the alert icon goes away within about a
  second on the ones you aim at (keep more than about 15 m from them, else they go on running from you, afraid); then
  for about 60 s they are afraid of you (they
  run, with the alert icon, when you come within 12 m, and never attack), then attack you again.
- [ ] **T16 Other Greydwarf leaders:** as T15 with a `Greydwarf_Shaman`, then with a `Greydwarf_Elite` (Greydwarf
  Brute) as the leader. Expected: the same rout.
- [ ] **T17 Who does not flee:** as T15 with a `Boar` next to the Troll, and one more Greydwarf about 40 m away.
  Expected: the Boar does not flee; the far Greydwarf does not flee (unless it comes within 25 m of the fallen Troll
  during the 15 s: then it joins the rout).
- [ ] **T18 Hitting a fleeing creature:** during a rout, hit a fleeing Greydwarf. Expected: it keeps running until the
  rout ends, then fights you (your hit counts); the others are afraid of you.
- [ ] **T19 Other packs:** rank 0, for each pack spawn the leader with its followers and kill the leader. Expected:
  the followers flee each time: `GoblinBrute` with `spawn Goblin 3`; `GoblinShaman` with `spawn GoblinArcher 3`;
  `Draugr_Elite` with `spawn Draugr 3`; `Fenring_Cultist` with `spawn Fenring 2`; `SeekerBrute` with
  `spawn Seeker 3`; `Charred_Mage` with `spawn Charred_Melee 3`; `Greydwarf_Shaman_Frozen` with
  `spawn Greydwarf_Frozen 3`.
- [ ] **T20 Always normal:** rank 8. Expected: `Eikthyr` attacks you, and a Boar within 60 m of Eikthyr while it
  fights you behaves as in the normal game (it does not run from you); a raid (`event army_eikthyr`, unverified name)
  attacks you; a training dummy (`piece_TrainingDummy`) takes your hits as usual; a Dvergr (`Dverger`) you hit fights
  back.
- [ ] **T21 Night hunters:** covered by the `morale.calm` self-test. In normal play: rank 7 or more (the Fulings are
  Plains creatures), at night in the Meadows of a world where Yagluth is dead (or after `setkey defeated_goblinking`).
  Expected: a group of hunting Fulings does not attack you (afraid) and runs when you come within about 12 m; with
  `NightHuntersCanBeAfraid = false`, they hunt you. Optional, at rank 6 (the Fulings hunt you): run more than 200 m
  away from a Fuling that chases you. Expected: it keeps chasing you, as in the normal game. They are rare: skip when
  none appears.
- [ ] **T22 Taming:** rank 6: find (or `spawn`) a `Wolf`, drop meat (`RawMeat`, unverified name) near it and step
  back beyond about 16 m, or sneak. Expected: it eats and becomes tame after the usual taming time; while it runs from
  you it is alert and taming pauses, as in the normal game; it never attacks you unless you corner it (`tame`
  finishes it if it takes too long; the point is that it never attacks).
- [ ] **T23 Tames still fight:** rank 4, a tamed Wolf next to you (`spawn Wolf`, then `tame`), `spawn Greydwarf`
  15 m away. Expected: the Greydwarf and the Wolf fight each other (normal game) while you stay back; the Greydwarf
  never goes for you; when you come within 12 m it leaves the Wolf and runs.
- [ ] **T25 Personal settings:** ConfigurationManager's `Display` section shows only `ShowProgressMessages` (no
  `ShowOnNameplates` any more; a config file from an earlier build may keep an unread `ShowOnNameplates` line: delete
  it). Set `ShowProgressMessages = false`, rank 2, `KillSteps` = your Greyling count + 1, and
  kill a Greyling. Expected: no corner message (the Standing log line still changes). Put both settings back.
- [ ] **T26 Settings live and name check:** rank 3, in the Meadows: add `Greydwarf` to the `Meadows` list (in ConfigurationManager,
  typing the name letter by letter). Expected: about a second after you stop typing, Greydwarfs are afraid of you; the
  log warns once that Greydwarf is in several home biome lists and the easiest biome is used, with no warning for the
  partial names you typed on the way ("G", "Gr", ...). Add a typo (`Boarr`). Expected: the log warns that `Boarr` is
  not a creature of this game. Set `RoutSeconds = 5`: the next rout lasts about 5 s. Set `FearRange = 25`: afraid
  creatures run when you are within about 25 m. Put every setting back.
- [ ] **T27 Follower in no home biome list:** rank 0; remove `Greydwarf` from the `BlackForest` list (it keeps
  following the Troll in `Packs`). `spawn Troll`, `spawn Greydwarf 3` near it, kill the Troll; while the Greydwarfs
  run, hit one of them. Expected: they all run away; when the rout ends, the one you hit fights you, the others are
  afraid of you for about 60 s. Put `BlackForest` back.
- [ ] **T28 Sleeping follower:** rank 0: `spawn Draugr_sleeping`, then walk 12-15 m away from it (outside its 10 m
  wake range, inside the 25 m rout radius), `spawn Draugr_Elite` next to you and kill it there. Expected: the sleeper
  stays asleep. Walk up to it within 15 s of the kill so it wakes. Expected: it attacks you as in the normal game and
  does not run away (it slept through its leader's death).
- [ ] **T29 Fear range:** rank 4, `nospawn`, `spawn Greydwarf 3`, walk 30 m away and stand still. Expected: they
  wander and never come to attack you. Walk toward them. Expected: each one runs away when you come within about 12 m
  and it sees or hears you, and calms down once you are about 16 m from it (or out of its sight for about 5 s). Set
  `FearRange = 5`: they let you come much closer before running. Put `FearRange` back to 12.
- [ ] **T30 Sneaking up:** rank 4, at dusk or in the forest: crouch-sneak up behind an afraid Greydwarf that is busy
  (out of its sight, slowly). Expected: it does not run until it sees or hears you; you can reach it and get a sneak
  attack from behind.
- [ ] **T31 Biome by spawn place:** rank 6. `goto` a Mountains area and `spawn Skeleton`. Expected: it is afraid of you
  (a Mountains skeleton: rank 6). `goto` a Plains area and `spawn Skeleton`. Expected: it attacks you (a Plains
  skeleton needs rank 7); set rank 7: it is afraid. Back in the Meadows at rank 3 at night (world key
  `defeated_eikthyr` set, or `setkey defeated_eikthyr`): the Greydwarfs that spawn in the Meadows attack you (they stay
  Black Forest creatures: rank 4). Optional: in the Deep North at rank 8, a `spawn Greydwarf` attacks you.
- [ ] **T32 Elites one boss later:** rank 4, in the Meadows: `spawn Greydwarf`, `spawn Greydwarf_Shaman`,
  `spawn Greydwarf_Elite`, `spawn Troll` (apart from each other). Expected: the Greydwarf is afraid; the Shaman, the
  Brute and the Troll attack you. Rank 5: all four are afraid.
- [ ] **T33 Fighting beats fear (single player with a tame):** rank 4: an afraid Greydwarf fighting your tamed Wolf
  15 m from you. Walk up to within 12 m. Expected: it leaves the Wolf and runs from you. Hit it (catch it or shoot it).
  Expected: it stops running at once and fights you.
- [ ] **T34 End of a rout next to a bed or next to a player it fights:** (a) rank 0, `nospawn`: build a bed about
  15 m from where you will spawn the pack, `spawn Troll` and `spawn Greydwarf 4` next to it, kill the Troll, then keep
  15-25 m from the followers and aim at them. Expected: when the rout ends (about 15 s after the kill), the alert icon
  goes away within about a second on every follower you aim at, also on one that then goes for the bed (it walks to
  the bed and hits it without the alert icon, as in the normal game). (b) Set `ShakenSeconds = 0`, rank 0, the same
  rout without the bed: follow the followers as they run and stand about 8-10 m from one of them when the rout ends.
  Expected: it turns and fights you, and its alert icon stays on all along (no second alert sound, the icon never
  switches to the yellow "aware" one in between). Put `ShakenSeconds` back to 60.
- [ ] **T24 Clean log:** play through T01-T34 while `./tools/Watch-Log.ps1 -Mine` runs. Expected: no error and no
  warning from Creature Morale with the default settings (the warnings asked for in T26 aside).

## 0.1.0 — multiplayer

Needs a second player (P2). "P1 rank 8" = `ForceBossRank = 8` on P1's game (Debug build on both).

- [ ] **M01 Each player judged by their own progress:** server with the mod; P1 rank 8, P2 rank 0, standing together.
  P2 runs `spawn Greydwarf 3` (P2's game controls them). Expected: they attack P2, never target P1, and do not run from
  P1 while P2, whom they fight, is near them; both players see the same health bars (no line of the mod's own). P2
  walks 50 m away: they now run from P1 when P1 comes within 12 m. Then P1 runs `spawn Greydwarf 3` (P1's game controls
  them) with P2 next to P1. Expected: the same.
- [ ] **M02 Rout across games:** rank 0 for both. P1 runs `spawn Greydwarf 4`; P2 runs `spawn Troll` 15 m from them;
  P2 kills the Troll. Expected: the Greydwarfs flee; P1's log (Debug level) shows "Rout from another game applied to 4
  creature(s)."
- [ ] **M03 Provocation survives an ownership change:** P2 runs `spawn Greydwarf`; P1 (rank 8) hits it. Expected: it
  fights P1. P2 walks 300 m away (P1's game takes the Greydwarf over). Expected: it keeps fighting P1 until 30 s after
  P1's last hit, then runs from P1 (if P1 is close) or leaves P1 alone.
- [ ] **M04 Per-player provocation:** both rank 8, near an afraid Greydwarf. P1 corners it (T13) or hits it. Expected:
  it attacks P1. P2 then lands an arrow next to it. Expected: it keeps fighting P1 (its target; the near miss
  provokes it toward P2 as well, each anger with its own timer). Second part: P1 hits a fresh afraid Greydwarf once
  and walks away; 5 minutes later P2 hits it. Expected: it fights P2 and never goes for P1.
- [ ] **M05 Rout across an ownership change:** rank 0 for both. P2 runs `spawn Troll` and `spawn Greydwarf 4`; P1 kills
  the Troll; while the Greydwarfs run, P1 hits one of them and P2 walks 300 m away. Expected: on P1's game they keep
  running from the same place until the rout ends; then the one P1 hit fights P1, and the others are afraid of P1 for
  about 60 s.
- [ ] **M06 Server settings for everyone:** on the server only, set `KillSteps` = P2's Greyling count + 1; P2 at rank
  2. Expected: P2's log says "Using the server's creature rules: ..."; after one more Greyling kill by P2, Greylings are
  afraid of P2. Change a server setting while P2 plays, for example `FearRange = 20`. Expected: about a second later
  P2's log says "Using the server's creature rules: ..." again, with "fear range 20 m" (any change of the server's
  rules logs this line again, also one the summary does not show, such as a home biome list edit). P2's own
  ShowProgressMessages still applies.
- [ ] **M07 Hand-off: player without the mod:** a player without the mod joins. Expected: refused about a second after
  joining, their game shows "Incompatible version"; the server log says "Refused <player>: their game does not have
  the mod. ...". With `AllowPlayersWithoutMod = true` on the server: they join (server log warning naming the problem);
  creatures near them attack them normally; creatures their game controls attack everyone normally; a Troll killed
  next to Greydwarfs controlled by their game routs nothing; no error on either side. Alert icon on the vanilla
  player's screen: P1 (rank 8) runs `spawn Greydwarf` (P1's game controls it; it runs from P1) and steps back until it
  calms down. The vanilla player crouches and stands still about 18 m to one side of it (farther than 12 m: a creature
  does not run from P1 while a player it fights is that close and sensed; within 30 m: the health bar range) and aims at
  it (again now and then: a health bar shows for 60 s after the crosshair). P1 walks straight at it from a direction
  at a right angle to the vanilla player's, so it runs away from P1, neither toward nor away from the vanilla player.
  Expected: the vanilla player's screen shows the alert icon while it runs, and the icon goes away when P1 walks off
  and it calms down (the game's own alert state; the vanilla game needs nothing from the mod to show it). If it then
  sees or hears the vanilla player, it comes for them as in the normal game: the yellow aware icon, then the alert icon
  again as it closes in. (If it goes for the vanilla player before P1 comes, start again with a new one.) Optional: a
  Greydwarf provoked by P1, controlled for a while by the vanilla player's game and taken back by a modded game within
  30 s, still fights P1.
- [ ] **M08 Turned off while connected:** a connected client (rank 8, afraid Greydwarfs controlled by the other
  player's game next to it) turns the mod off in the MC Mods panel. Expected: the client log says "Told the server
  that Creature Morale is now off on this game." and "Standing withdrawn: ..."; the server log says "<player> turned
  Creature Morale off on their game." and, about a second later, "Refused <player>: their game has the mod turned off.
  ..."; the client goes back to the menu with "Incompatible version". With `AllowPlayersWithoutMod = true` on the
  server: the client stays, the server logs a warning naming the problem, and the Greydwarfs attack that player once
  they see or hear them; turned back on: no refusal, and they are afraid of that player again.
- [ ] **M09 Joins with the mod turned off:** a client with `Enabled = false` in its config joins. Expected: refused
  about a second after joining (usually before its character appears); the server log names "has the mod turned off".
- [ ] **M12 Joins with another network version:** (optional, needs a second build with another `ModNetworkVersion`,
  for example the first test build `3cda33b+dirty`, network version 1; this build is 2) such a client joins. Expected: refused the same way; the server log names "has another version of the mod (network
  version N, the server has M)".
- [ ] **M10 Dedicated server:** mod on a dedicated server and both players. Expected: the server log shows the mod
  loaded, no error; M02 and M08 work through it.
- [ ] **M11 Server turns the mod off:** the host (or the dedicated server's config) turns the mod off. Expected: the
  clients' copies turn off (MC Mods panel: "Inactive: the server has this mod turned off ..."), nobody is refused;
  afraid creatures stop running and notice players again as in the normal game once they see or hear them. Back on:
  afraid again.
- [ ] **M13 Running creature across an ownership change:** P1 rank 8, P2 rank 8. P2 runs `spawn Greydwarf`; P1 walks
  up so it runs from P1; while it runs, P2 walks 300 m away (P1's game takes it over). Expected: on P1's game it keeps
  running from P1 (or starts again within a second), never attacks, and calms down once P1 is about 16 m away (the
  alert icon goes away within a few seconds).
- [ ] **M14 Alert icon on every screen:** P1 rank 8, P2 rank 8 (the Greydwarf is afraid of both: a player it would
  fight near it would stop it from running from P1). P2 runs `spawn Greydwarf` (P2's game controls it); it runs from
  P2. Once it has calmed down, P2 stands still about 18 m to one side of it (more than 16 m: it does not run from P2;
  less than 30 m: the health bar range). P1 then walks straight at it, from a direction at a right angle to P2's, to
  within 12 m, so it runs away from P1, neither toward nor away from P2. Both players aim at it (again now and then: a
  health bar shows for 60 s after the crosshair). Expected: both see the alert icon on it while it runs; when P1 walks
  away and it calms down, the icon goes away on both screens. Repeat with a Greydwarf P1 spawned (P1's game controls
  it; P1 steps back until it calms down, P2 stands about 18 m to one side of it, then P1 walks in again): the same on
  both screens. Then a rout: rank 0 for both, P1 kills a Troll next to Greydwarfs P2 spawned, then both players keep
  15-25 m from the followers and aim at them. Expected: both see the alert icon on the running followers, and it goes
  away on both screens when the rout ends (they come for the players again once their 60 s of fear are over, as in
  T15).

## 0.1.0 — other mods

- [ ] **X01 Sneak Ambush (MC)** (same test as its X01): rank 6, in the Meadows (plain and two-star Greydwarfs are
  afraid). Standing, shoot an afraid Greydwarf that faces you from about 15-20 m (it sees you but does not run yet):
  no sneak attack and no Sneak XP. A hit from behind while you are crouched out of its sight, or an arrow from inside
  a Smoke Screen cloud at one outside the cloud or more than 2.5 m from you, is a sneak attack and pays. Crawling next
  to afraid creatures raises Sneak only slowly. From inside a Smoke Screen cloud, walk up to an afraid Greydwarf
  (more than 2.5 m from it if it is inside the cloud too): it does not run (it cannot sense you). From inside the
  cloud, shoot an afraid two-star Greydwarf (`spawn Greydwarf 1 3`) with the `Bow` at Bows 0 (`resetskill Bows`, as in
  Sneak Ambush's T20, so the hit does not kill it): it targets you and fights you through the smoke; the other afraid
  Greydwarfs do not notice you. Standing in the smoke next to an afraid creature that has not noticed you never
  corners it. At rank 0, creatures that chase you forget you after a smoke burst; a routing pack is unaffected. An
  afraid creature whose health bar the smoke hid gets its bar back once it is out of the smoke and you aim at it, with
  the game's red alert icon while it runs from you and no icon once it calms down. Expected: all of this, no errors.
- [ ] **X02 Encyclopedia (MC):** look at an afraid creature you have never met, first while it runs from you (alert
  icon), then calm. Expected: it counts as met; the Encyclopedia shows its normal name.
- [ ] **X03 Creature Kill and Tame Counts (MC):** after T05. Expected: the Greyling kill count it shows is the count
  the kill step used.
- [ ] **X04 Sleep Through the Day (MC):** afraid Greydwarfs near the bed, running or not. Expected: they do not stop a
  day sleep; hostile ones (rank 0) do.
- [ ] **X05 Tower Shield Wall (MC)** (its C06; the New Game+ part pairs with its T23): rank 4, shield bash an afraid
  Greydwarf: crouch-sneak up behind one that has not noticed you, or catch one that runs (the bash is slow: its hit
  lands about 0.8 s after you press attack, and the next bash waits 2 s). Expected: it stops running (if it was) and
  fights you. In a New Game+ world (world level 1): a bash on an afraid Greydwarf provokes it too, and a knife hit
  right after the bash is not a sneak attack.
- [ ] **X06 Harpoon Hooks Tames (MC):** rank 3, in the Meadows. Harpoon (`SpearChitin`) an afraid wild Boar. Expected: it fights you
  (the harpoon deals damage). Hook your own tame with afraid Boars more than 4 m from it. Expected: the hook works and
  those Boars do not come for you.
- [ ] **X07 Dual Wielding (MC)** (its X06): rank 4, hit an afraid Greydwarf with the off-hand weapon only (the second
  swing of the combo: swing once in the air, then hit it; sneak up on it or catch it first, since a cornered one
  already fights back). Expected: it fights you, like after a main-hand hit.
- [ ] **X08 Weapon Moveset (MC)** (its X04 and X05): rank 3, in the Meadows. A sneak roll attack on an afraid
  `Greyling` that faces you (from beyond 12 m, so it sees you before it runs). Expected: no sneak attack and no Sneak
  Ambush XP. The same from behind it, unseen: a sneak attack. A jump attack on an afraid Greyling. Expected: it fights
  you.
- [ ] **X09 Another creature AI mod:** (optional) with TruePassiveMobs or The Mark of Oden installed, load a world.
  Expected: one warning "<mod> is installed and also changes when creatures attack players. Both mods will run
  together with Creature Morale ..."; no error.

## 0.1.0 — live toggle, disabled, log

- [ ] **L01 Live toggle:** rank 4, afraid Greydwarfs nearby. Turn the mod off in the MC Mods panel. Expected: the log
  says "Standing withdrawn: ...", and the Greydwarfs notice you as in the normal game once they see or hear you (a
  running one stops running and comes for you, still alert). Turn it on. Expected: within about 2 s they no longer
  attack and run from you when you are close (alert icon), and calm down (no icon) once you are about 16 m away.
- [ ] **L02 Off during a rout:** turn the mod off while a pack is running away. Expected: the creatures stop running
  (normal game).
- [ ] **L03 `Enabled = false` + restart:** set `Enabled = false` in `BepInEx/config/MC.Combat.Creatures.Morale.cfg`,
  start the game and load a world. Expected: Status "Off (disabled in settings)."; creatures behave as in the normal
  game; no "Standing:" line in the log.
- [ ] **L04 Clean log:** over the whole test session (single player and multiplayer). Expected: no error from Creature
  Morale.
