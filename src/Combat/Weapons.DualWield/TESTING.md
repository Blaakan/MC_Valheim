# Dual Wielding — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also in the MC Mods panel). Smoke
test (`./tools/Test-Smoke.ps1`) passed 2026-09-30: loads, patches cleanly, JitCheck clean. In-world self-tests
(`./tools/Test-InWorld.ps1 -Mod Weapons.DualWield`) passed 2026-09-30 in a full run with every MC mod (71 of 71
tests): `dual.data`, `dual.equip`, `dual.keep`, `dual.attack`, `dual.damage`, `dual.fields`, `dual.visuals`. Their
NOTE lines in the log record the values the design lists as unverified. First hands-on test by the user on build
`3cda33b+dirty` (2026-09-30): its feedback changed the swap (T04, T32) and the sheathed pair (T33). The next full
in-world run (2026-09-30, every MC mod) passed `dual.data`, `dual.equip`, `dual.keep`, `dual.attack`, `dual.damage` and
`dual.fields` on those changes; `dual.visuals` failed one check: the two knives' X at the hip was 7.1 degrees off level
two frames after it crossed (the hip leans as the stance changes). The crossed pair is now kept level every frame
(T33); that fix and its new `dual.visuals` check (still level 0.8 s later) have not run yet. Then the user's holster
rule (2026-09-30: swords, maces and axes on the back, daggers and knives on the side hip) changed the sheathed pair
again: two knives now hang one per hip, and a knife with a back weapon stays where the game puts each (T33, M17). The
next full in-world run (2026-09-30, 69 of 71 tests with every MC mod) passed `dual.data`, `dual.equip`, `dual.keep`,
`dual.attack` and `dual.fields`; two test checks failed, neither from a mod bug: `dual.damage` compared an off-hand hit
that the game had lowered (its swing also touched the ground or a rock, and the game splits a hit's damage between
the objects a swing touches) with a main-hand hit that it had not, and `dual.visuals` expected the two knives to be
mirror images about the body while the hips stood turned 31-43° in the idle stance (the knives follow the hips, like
knives on a belt). The damage test now leaves the game's random roll and split out of its ratios, and the knife test
checks that each knife stays on its own side and outside its own leg, standing and while walking, jogging and
sprinting; those test changes have not run yet. Every item below is still to test.

**Setup:** F5 for the console: `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). `god`
keeps you alive (it does not change stamina costs), `ghost` makes creatures ignore you, `heal` refills health and
stamina, `killall` removes nearby creatures, `puke` empties your stomach so you can eat again, `raiseskill Swords 100`
raises a skill. Give yourself items with `spawn <name> [amount]` (Tab autocompletes; add `p` to put them straight into
your inventory: `spawn SwordIron 2 p`). Names checked in the 1.0.16 game data:

- weapons: `SwordIron` (two), `AxeIron` (two), `MaceIron`, `Club`, `KnifeBlackMetal` (two), `KnifeFlint`, `KnifeButcher`,
  `SpearFlint`, `SwordMistwalker` (frost damage), `AxeJotunBane` (poison damage), `SwordNiedhogg`, `SwordNiedhoggBlood`,
  `SwordNiedhoggLightning`; for comparison the game's own dual weapons `AxeBerzerkr` (Berserkir axes) and
  `KnifeSkollAndHati` (Skoll and Hati);
- other items: `ShieldBronzeBuckler`, `Torch`, `Hammer`, `BombSmoke`, `CrossbowArbalest`, `ShieldIronTower` (with
  Tower Shield Wall);
- targets: `Greyling`, `Troll` (spawn them a few metres away, with `god` on).

Checked in the game's asset list instead (not in the dumped data): the food `Raspberry`, the bolt `BoltBone` and the
feast pieces `FeastMeadows` and `FeastBlackforest` (T17).
`MC_SmokeScreen` is Sneak Ambush's own item (only with that mod). The keep-equipment death rule is the global key
`DeathKeepEquip`: `setkey DeathKeepEquip`, and `removekey DeathKeepEquip` afterwards (command and key checked in the
game code).

Put the weapons on the hotbar. "Pair" means `SwordIron` in the main (right) hand and `AxeIron` in the off (left) hand
unless an item says otherwise: equip the sword, then the axe. Keep `./tools/Watch-Log.ps1 -Mine` open. Settings are
changed with ConfigurationManager (F1) or in `BepInEx/config/MC.Combat.Weapons.DualWield.cfg`; put each one back to its
default after the item that changed it. For the stamina test (T12) use a world with the default stamina world
modifier. Multiplayer: a dedicated server or a host with the mod, and a second game. For M03, make a build with another
network version without deploying it: `dotnet build src/Combat/Weapons.DualWield/MC.Combat.Weapons.DualWield.csproj
-p:ModNetworkVersion=2 -p:DeployToGame=false`, copy `src/Combat/Weapons.DualWield/bin/Debug/MC.Combat.Weapons.DualWield.dll`
over the second game's copy in `BepInEx/plugins/MC_Valheim/Combat/MC.Combat.Weapons.DualWield/`, and put the normal
build back afterwards (build again without `-p:ModNetworkVersion=2` and copy it the same way).

## 0.1.0 — single player

- [ ] **T01 Pair:** equip the sword, then the axe from the hotbar; unequip both, then do the same with inventory
  right-clicks. Expected: the sword in the right hand, the axe in the left, both marked as equipped in the inventory and
  on the hotbar, the dual axe stance (the Berserkir axes' idle). The same with `MaceIron` + `KnifeFlint` and with two
  `SwordIron`.
- [ ] **T02 Replace the off hand:** with the pair, equip the mace. Expected: the mace replaces the axe (the axe is back
  in the inventory, not equipped), the sword stays.
- [ ] **T03 Main-hand key:** with the pair, hold Left Alt and equip the knife from the hotbar. Expected: the knife alone
  in the main hand, sword and axe put away. Repeat with an inventory right-click and with the radial menu (G): the same.
  Without a pair (sword alone), Left Alt + the second `SwordIron`: it replaces the sword. With the mace alone in hand,
  press Left Alt + the sword's hotbar key, then quickly the axe's key without Left Alt. Expected: the sword ends in the
  main hand and the axe in the off hand.
- [ ] **T04 Swap key:** with the pair, press H. Expected: after a short equip animation the hands swap in view, the
  equip sound plays, top left "Main hand: <the axe's name>" with its icon; the next attack starts with the axe, and
  blocking now uses the sword. H pressed during a swing (or right as one starts) does nothing: the swing keeps its two
  weapons. H pressed together with the attack button: either the swing comes first and H does nothing, or the swap
  comes first and the swing waits for it (T32); never a swing that changes weapons midway. Press H quickly several
  times: at most one swap every half second. Swing once, press
  H, swing again: the second swing is the first swing of the combo again. (Animation and timing: T32.)
- [ ] **T05 Shield:** with the pair, equip the buckler. Expected: the buckler in the left hand, the sword kept, the axe
  put away. Then equip the axe. Expected: the axe replaces the sword, the buckler stays (no pair).
- [ ] **T06 Torch:** with the pair, equip the torch. Expected: the torch in the left hand, the sword kept, the axe put
  away.
- [ ] **T07 Not pairable:** with the pair, equip the spear. Expected: the spear alone (both weapons put away). The same
  with `BombSmoke`. With the sword alone, equip `KnifeButcher`: it replaces the sword, it never goes to the off hand.
  Set `ExcludedWeapons = AxeIron`: sword, then axe: the axe replaces the sword (no pair). Set
  `ExcludedWeapons = NoSuchSword`: a log warning "ExcludedWeapons: no item named 'NoSuchSword' in this game; it is
  ignored. …", once. Put it back to empty.
- [ ] **T08 Axe moves:** with the pair, hold attack on a `Troll`. Expected: the four-swing combo of the Berserkir axes,
  six hits in all (one, one, two, two); the special attack (middle mouse) is the wide cleave. Compare with
  `AxeBerzerkr`: the same moves and stance. Also `MaceIron` + `SwordIron` and `KnifeBlackMetal` + `SwordIron`: the axe
  moves.
- [ ] **T09 Knife moves:** `KnifeFlint` + `KnifeBlackMetal`. Expected: the knife stance of Skoll and Hati, the fast
  three-stab combo, and the leap as special attack. Compare with `KnifeSkollAndHati`.
- [ ] **T10 Every hit from a real weapon:** `SwordMistwalker` (main) + `AxeJotunBane` (off) on a `Troll`. Expected: frost
  on the main-hand hits (first swing, first hit of the third and fourth), poison on the off-hand hits (second swing,
  second hit of the third and fourth), both on the cleave. Both weapons lose durability. Swords and Axes both rise
  (skills screen).
- [ ] **T11 Off-hand damage:** first `raiseskill Swords 100` (at a low skill each hit's damage varies by more than
  two times). Two `SwordIron` on a `Troll` (ignore the first hit of the fight: an unaware target takes a sneak attack
  bonus). Set `OffHandDamage = 50`. Expected, over several combos: each off-hand hit (the second swing's hit, the
  second hit of the third and of the fourth swing) is about half the main-hand hit before it in the same swing (or the
  first swing's hit); both hits of the fourth swing are doubled. Put it back to 100.
- [ ] **T12 Stamina:** in a world with the default stamina modifier, compare the stamina bar with one swing of each
  weapon alone. Expected: a pair swing costs about one swing of the dearer weapon: `KnifeFlint` + `SwordIron` (either
  hand) = the sword's cost; `KnifeBlackMetal` + `SwordIron` = the black metal knife's cost. The cleave costs twice a
  swing; with two knives the leap costs three times a swing.
- [ ] **T13 Club:** `Club` in the main hand + `SwordIron` in the off hand. Expected: the special attack does nothing
  (like the club alone). Press H (club in the off hand): the cleave works.
- [ ] **T14 Block and parry:** with the pair, block and parry a `Greyling` and a `Troll`. Expected: the off-hand weapon
  blocks (its block power) and a timed block parries. Put a knife in the off hand: it blocks badly (far less block
  power than the sword), like a single knife.
- [ ] **T15 Trees:** two `AxeIron` on a tree. Expected: every swing is the first swing of the combo, struck by the
  main-hand axe, which chops; Wood Cutting rises. Sword (main) + axe (off): the sword hits the tree and chops nothing;
  press H: the axe now chops. Two knives: the combo goes on, no chopping.
- [ ] **T16 Hide and draw:** with the pair, press R, R, several times. Expected: the same hands each time.
- [ ] **T17 Eating:** with the pair, eat a `Raspberry` from the inventory (`puke` lets you eat again). Expected: as in
  the game, no food shows in your hand and both weapons stay in your hands. Then `spawn FeastMeadows` and eat at it
  (E). Whether the game hides the main weapon there is unverified (the `dual.data` self-test notes it for every feast).
  If it does: the main weapon disappears for about a second, then the same hands. Eat and roll at once: the same hands
  after the roll. Eat and press attack at once: no attack until the main weapon is back, then the same hands. Spawn
  the feast next to deep water, eat and jump in: after leaving the water (and pressing R if needed), the same hands.
  Spawn `FeastBlackforest` next to the first feast, eat at one and at once at the other (within the second): the same
  hands after (not the axe alone in the main hand). The self-test `dual.keep` covers the hidden main weapon with food
  from the inventory, and two foods eaten in a row while it is hidden.
- [ ] **T18 Stations, beds, barber, swimming:** with the pair, use a workbench, sleep in a bed, use the barber, swim.
  Expected: after each (press R if the weapons are hidden), the same hands. Sit on a chair: the weapons stay in hand
  (as in the game).
- [ ] **T19 Relog:** log out and in with the pair. Expected: the same hands. Drag the off-hand weapon to another
  inventory slot (it now loads last), relog: the same hands. Drag the main weapon to another slot (now it loads last),
  relog: the same hands.
- [ ] **T20 Death:** die with the pair in a normal world. Expected: both weapons in the tombstone, nothing equipped
  after respawn. Then `setkey DeathKeepEquip` and die with the pair. Expected: the same pair after respawn. Then
  `removekey DeathKeepEquip`.
- [ ] **T21 Inventory moves:** with the pair, drag the main weapon to an empty slot. Expected: the same pair. Drag the
  main weapon onto the off-hand weapon's slot, then the off-hand weapon onto the main weapon's slot. Expected: the same
  pair each time. Drag the main weapon into a chest. Expected: the axe moves to the main hand. Pair again and drop the
  off-hand weapon on the ground. Expected: the sword stays.
- [ ] **T22 Unequip:** with the pair, unequip the main weapon (its hotbar key). Expected: the axe moves to the main
  hand. Pair again and unequip the off-hand weapon. Expected: the sword stays.
- [ ] **T23 Radial hammer:** with the pair, open the radial menu (G), pick the hammer, then pick it again. Expected:
  the same hands.
- [ ] **T24 Trails:** swing with the pair. Expected: both blades show a swing trail. Set `LeftHandTrails = false`:
  only the right one. Put it back. Set `SecondaryMoves = MainWeapon` and use the special attack: only the sword
  trails (the axe does not strike it). Put it back.
- [ ] **T25 Move settings:** after spawning, the log has one Info line "Pairs use the moves of AxeBerzerkr (dualaxes0-3,
  special dualaxes_secondary); two knives use KnifeSkollAndHati (dual_knives0-2, special dual_knives_secondary).". Set
  `PairMoves = KnifeSkollAndHati`. Expected: the sword + axe pair uses the knife moves and stance at once. Set
  `PairMoves = Nothing`. Expected: a warning "Moves.PairMoves = Nothing: no item with this name in the game. The
  default AxeBerzerkr is used instead." and the axe moves. Set `PairMoves = SwordIron` and fight a `Troll` with
  `SwordMistwalker` + `AxeJotunBane`. Expected: the one-handed sword stance and the sword's three-swing combo, whose
  swings strike frost, poison, frost (main, off, main hand); the special attack (the iron sword's special) shows two
  damage numbers, frost and poison. Put it back, then set `SecondaryMoves = MainWeapon`. Expected: the special attack is the
  sword's own, struck by the sword only. Put it back. After a relog (or turning the mod off and on in the MC Mods
  panel), the Info line is in the log again.
- [ ] **T26 Look and feel:** watch pairs of every kind (sword, axe, mace, knife, club) in the moves and the stance:
  idle, walk, run, jump, roll, block. Expected: nothing badly wrong. Note what looks odd (blades through the body, the
  left-hand grip). The sheathed pair has its own item (T33).
- [ ] **T27 Both hands:** `SwordMistwalker` + `AxeJotunBane`, set `HitPattern = BothHands`. Expected: every hit shows
  two damage numbers, with frost and poison each time. Set `BothHandsDamage = 100` (with `OffHandDamage` at its
  default 100, which also scales the off-hand number): each number is as large as a normal hit of that weapon. Put
  both back (`Alternate`, 50).
- [ ] **T28 Weapon effects:** first `raiseskill Swords 100` (so the random spread of each hit stays small).
  `SwordNiedhogg` (main) + `SwordNiedhoggBlood` (off). Turn `god` off and take damage down to about a fifth of your
  health. Expected, over several combos: the off-hand hits deal more than the main-hand hits of the same step; press
  H: now the main-hand hits deal more. With `SwordNiedhoggLightning` in the off hand: lightning strikes on some
  off-hand hits only.
- [ ] **T29 First pairing message:** start the game, pair two weapons from the hotbar. Expected: a message in the
  middle of the screen "Dual wielding: <weapon> is in your off hand. Hold Left Alt while equipping to replace your main
  weapon instead. Press H to swap hands." The next pairing shows nothing. Restart the game and log in with a saved
  pair: no message (it comes at the first pairing you make yourself).
- [ ] **T30 Rules change:** with the pair, set `ExcludedWeapons = AxeIron`. Expected: the axe is put away at once, the
  sword stays, with the single-weapon stance. Clear it: pairing works again. Pair again and set
  `ExcludedWeapons = SwordIron` (the main weapon). Expected: the axe (off hand) is put away, the sword stays alone.
  Clear it.
- [ ] **T31 Keys:** set `SwapHandsKey = J`: J swaps, H does nothing. Set `SwapHandsKey = None`: no key swaps. Set
  `MainHandKey = None`: Left Alt + equip pairs like a normal equip. Set `SwapHandsKey = JoystickButton0`: a warning
  "Controls.SwapHandsKey = JoystickButton0: gamepad buttons are not supported yet, so swapping your weapons between
  your hands is off. Pick a keyboard key or a mouse button.", and no key swaps. Put both back (`LeftAlt`, `H`).
- [ ] **T32 Swap animation and timing:** with the pair, stand still and press H; then walk and press H. Expected: first
  the game's equip animation, with the "Equipping <axe>" bar for about a fifth of a second (like switching weapons on
  the hotbar), then the weapons change hands with the draw animation of the hide key (R). While walking, the speed
  during the swap is what a hotbar equip gives. Press H, then hold attack while the "Equipping" bar runs: no swing
  until the hands changed, then the swing starts with the axe (note how long after the swap). Press H, then click
  attack once while the bar runs: note whether the swing starts after the swap (and how late) or not at all. Press H
  and dodge at once: no swap. The same with a jump. Press H while sprinting: no swap, and top left "Cannot swap hands
  while sprinting". Press H, then start sprinting at once: no swap, the same message. Press H twice very quickly: one
  swap. Note how the two animations look together (a stiff jump from one to the other, a draw that plays too late) and
  whether the swap feels too slow or too fast.
- [ ] **T33 Sheathed pair placement:** press R with each pair: two `SwordIron`, `SwordIron` + `AxeIron`, `MaceIron` +
  `SwordIron`, two `KnifeBlackMetal` (`spawn KnifeBlackMetal 2 p`), `KnifeFlint` + `KnifeBlackMetal`,
  `KnifeBlackMetal` + `SwordIron`, and `SwordIron` + `KnifeBlackMetal`. Look from behind, from the front and from both
  sides (move the camera around). Expected: (a) two swords, axes or maces: crossed in an X on the back, hilts over both
  shoulders; (b) two knives: one on each hip, the main-hand knife on the right hip exactly where the game hangs a
  single knife, the off-hand knife on the left hip as its mirror image (same height, same distance from the body, same
  angle, edge the same way round); `KnifeFlint` + `KnifeBlackMetal` the same, each knife at its own spot; (c) a knife
  with a sword (either hand): the knife on the right hip and the sword on the back, both exactly as the game places
  each one alone (nothing crossed, nothing moved). Use a workbench and swim with a pair: placed the same way. With the
  sword alone, press R: sheathed as in the game. Set `CrossSheathedPair = false` with each pair sheathed: at once two
  weapons of the same kind overlap on one spot
  (two knives on the right hip), as in the game, and the knife + sword pair does not change; set it back to true:
  placed again at once. With each pair sheathed, stand still, walk and run, and press R mid-stride several times: the
  X on the back stays level (both weapons at the same angle either side of upright, never one steeper than the other,
  also right after R); note whether it visibly turns against the back while you run. The two knives move with the
  hips like knives on a belt and stay mirror images of each other; note whether one looks higher, lower or more
  tilted than the other. Standing still, the hips turn a good way from where you face (about 30-45°), so the knives
  turn with them, one a little forward and one a little back: that is expected. Standing, walking and running, each
  knife stays on its own side and on the outside of its own leg: never both on one side, never between the legs or
  through a thigh. Lie in a bed with a back pair and with a knife pair sheathed, set `CrossSheathedPair` false
  then true while lying, then get up: the back pair is crossed within a moment of standing, the knives are one per
  hip. Note what looks wrong (blades through the body, the legs or the ground, the crossing point, the angle of the
  knives, a knife floating off the hip, a weapon floating off the back).
- [ ] **L01 Live toggle:** with the pair, turn the mod off in the MC Mods panel (Esc → MC Mods). Expected: the axe goes
  back to the inventory, the sword stays, stance and attacks are vanilla, a second one-handed weapon replaces the
  first. Turn it on: pairs work again. With the pair hidden (R), turn it off. Expected: only the sword stays on the
  back, where the game puts it; press R: only the sword is drawn. `Status` shows why the feature is off each time, and
  back to active when turned on.
- [ ] **L02 `Enabled = false` + restart:** log out with the pair, set `Enabled = false` in
  `BepInEx/config/MC.Combat.Weapons.DualWield.cfg` and start the game. Expected: `Status` reads "Off (disabled in
  settings).", the pair loads as one weapon (no error), equipping is vanilla (a second one-handed weapon replaces the
  first).
- [ ] **L03 Clean log:** play through the items above while `./tools/Watch-Log.ps1 -Mine` runs, and quit the game while
  dual wielding. Expected: no error and no warning from Dual Wielding, except those an item provokes on purpose (T07,
  T25, T31; the server's "Refused", "plays without" and "connected without" warnings of M01-M05; "Server does not have
  Dual Wielding" in M14; the other-mod warning of X08).

## 0.1.0 — multiplayer

- [ ] **M01 Player without the mod refused:** with `AllowPlayersWithoutMod = false` on the server, a player without the
  mod joins. Expected: about a second after joining their game goes back to the menu with "Incompatible version"; the
  server log warns "Refused <player>: their game does not have the mod. This server requires Dual Wielding on every
  player …". A player with the mod on joins normally.
- [ ] **M02 Mod turned off refused:** a player with the mod and `Enabled = false` joins. Expected: refused about a second
  after joining ("Incompatible version"); the server log says "their game has the mod turned off".
- [ ] **M03 Other network version refused:** a player with the network version 2 build of the Setup joins. Expected:
  refused about a second after joining ("Incompatible version"); the server log says "their game has another version
  of the mod (network version 2, the server has 1)"; their MC Mods panel says the versions cannot talk to each other.
- [ ] **M04 Allowed without the mod:** set `AllowPlayersWithoutMod = true` on the server; the three players of M01-M03
  join. Expected: all three play; for each the server log warns "<player> plays without Dual Wielding: their game
  <reason>. AllowPlayersWithoutMod is on, so they may play, without this mod's rules (with another dual wield mod
  installed they may still dual wield, by that mod's rules)."; none of them can make a pair (a second one-handed weapon
  replaces the first). The M03 player's log has no "Using the server's dual wielding rules" line and no "cannot read"
  warning (the server sends its rules only to the same network version). Set it back to false: the players who are
  still connected are refused about a second later.
- [ ] **M05 Turned off while connected:** a player with the mod joins and holds a pair, then unticks Dual Wielding in
  the MC Mods panel. Expected: the axe is put away; their log says "Told the server that Dual Wielding is now off on
  this game."; the server log says "<player> turned Dual Wielding off on their game."; about a second later they are
  refused ("Incompatible version"; server log "has the mod turned off"). Untick and tick again quickly (within the
  second): they stay. With `AllowPlayersWithoutMod = true`: they stay and the server log warns.
- [ ] **M06 Server settings:** server `OffHandDamage = 50`, the client's own left at 100. Expected: the client logs
  "Using the server's dual wielding rules: off-hand damage 50%, …" and its off-hand hits deal about half (T11); the
  client's config file is unchanged. Back in single player the client uses its own 100.
- [ ] **M07 Rejoin with a pair:** log out from the server holding a pair, join again. Expected: the same pair after
  spawning. Log out again with the pair; the server sets `ExcludedWeapons = AxeIron`; join again. Expected: no pair
  right after spawning: one weapon in the main hand, the other in the inventory (which one stays depends on whether
  the server's rules arrived before the spawn and on the inventory order), no error. The server clears the setting.
- [ ] **M08 Live rule change:** while a client holds the pair, the server sets `ExcludedWeapons = AxeIron`. Expected:
  the client's axe is put away at once. The server clears it, the client pairs again; the server sets
  `PairMoves = KnifeSkollAndHati`. Expected: the client's pair switches to the knife moves and stance at once.
- [ ] **M09 Hand-off, observer without the mod:** with `AllowPlayersWithoutMod = true`, a player without the mod stands
  next to a `Troll` they spawned (their game controls it) and watches a dual wielder fight it. Expected: they see both
  weapons in hand, the dual stance and the dual moves; the dual wielder's hits (both hands) damage the Troll and
  stagger it as usual. They see no off-hand trail and the two sheathed weapons overlapping (expected).
- [ ] **M10 Observer with the mod:** two players with the mod. Expected: each sees both blades of the other's dual
  swings trail. With `LeftHandTrails = false` on the observer's game: only the right trail.
- [ ] **M11 Hand-off, items:** with `AllowPlayersWithoutMod = true`, drop the former off-hand weapon (the axe) for a
  player without the mod. Expected: a normal axe for them (it equips and swings normally). Give it back: it pairs
  normally with your sword.
- [ ] **M12 PvP:** both players turn PvP on; a dual wielder attacks a player who blocks with a shield. Expected: the
  hits of both weapons are blocked, a timed block parries them; unblocked hits deal each weapon's own damage. A
  both-hands blow (the special attack) is blocked as two hits (two block sounds; known: the Blocking skill and block
  adrenaline count twice).
- [ ] **M13 Server toggles the mod:** the host (or the server's config) turns the mod off. Expected: every client's
  off-hand weapon is put away, their MC Mods panel shows "Inactive: the server has this mod turned off (or it is not
  working there)."; pairing is vanilla. On again: pairs work, the clients log the server's rules again.
- [ ] **M14 Server without the mod:** a client with the mod, holding a pair in its save, joins a server without it.
  Expected: the MC Mods panel shows "Inactive: the server does not have this mod. It must be installed on the server
  too."; the pair loads as one weapon, equipping is vanilla, no error.
- [ ] **M15 Dedicated server:** install the mod on a dedicated server. Expected: the mod loads, M01 and M06 behave as
  above, the server log has no error from Dual Wielding.
- [ ] **M16 Swap seen by others:** a second player watches a dual wielder press H (once with the mod, once without it,
  `AllowPlayersWithoutMod = true`). Expected: they see the equip animation (the game shares it, as for any hotbar
  equip), then the weapons change hands with the draw animation.
- [ ] **M17 Sheathed pair on other players:** two players with the mod; one sheathes a sword pair (R), then a knife
  pair. Expected: the other sees each placed as in T33 (the swords crossed on the back and level while that player
  walks and runs, the knives one on each hip). With `CrossSheathedPair = false` on the observer's game: overlapping
  on one spot. With `AllowPlayersWithoutMod = true`, the observer turns Dual Wielding off in the MC Mods panel: at once
  overlapping, and placed again at once when turned back on. The other player lies in a bed with the sword pair
  sheathed; the observer sets `CrossSheathedPair` false, then true: once that player gets up, crossed within a moment.
  A player without the mod sees both pairs overlapping (the game's placement).

## 0.1.0 — other mods

- [ ] **X01 Crossbow Stays Loaded (MC):** load a `CrossbowArbalest` (`BoltBone`), switch to a pair, then back to the
  crossbow. Expected: the crossbow puts the pair away (it is two-handed), the pair can be made again, and back on the
  crossbow it is still loaded.
- [ ] **X02 Weapon Moveset (MC):** `AxeIron` (main) + `AxeJotunBane` (off, its poison shows its hits). Expected: the
  jump attack plays the fourth dual axe swing and hits main hand then off hand; the roll attack plays the second swing
  and strikes with the off hand, flowing straight out of the end of the roll (no stand-up in between), and the next
  swings continue the combo (third swing: main, then off). Two knives:
  the dual knife moves, the roll attack (second stab) strikes with the off hand. With Moveset's jump attack animation
  `DualAxes = dualaxes2`, the swing after a jump attack is the fourth swing (the pair's four-swing combo). No errors in
  the log.
- [ ] **X03 Tower Shield Wall (MC):** with the pair, equip `ShieldIronTower`. Expected: both weapons put away. Equip the
  sword: the tower shield is put away.
- [ ] **X04 Sneak Ambush (MC):** crouch up to an unaware `Greyling` with `KnifeBlackMetal` (main) + `SwordIron` (off)
  and attack. Expected: a knife backstab (the knife's large bonus); the Sneak skill gains once for the ambush. Swap
  hands (sword in the main hand) and ambush another unaware `Greyling` (the game gives one backstab per creature every
  5 minutes). Expected: the backstab is the sword's (smaller). Equip `MC_SmokeScreen` while paired: both weapons put
  away; it never goes to the off hand. (With Creature Morale too: a `Greyling` that is afraid of you, from boss rank 3
  in the Meadows, still gives the backstab as long as it has not seen you; one that watches you gives none.)
- [ ] **X05 Forge Idol Upgrades (MC):** refine the off-hand weapon at the Forge of Potential. Expected: it comes back
  unequipped, the main weapon stays. Refine the main weapon: the off-hand weapon moves to the main hand.
- [ ] **X06 Creature Morale (MC):** boss rank 3 (Creature Morale's Debug setting `ForceBossRank = 3`, see its
  `TESTING.md`), in the Meadows, `spawn Greyling`: it is afraid of you (it runs when you come within about 12 m and it
  sees or hears you, with the game's red alert icon on its health bar while it runs). Crouch up to it from behind, unseen, with `SwordIron`
  (main) + `AxeIron` (off); swing once in the air beside it without touching it, then hit it with the second swing (an
  off-hand hit; if it starts running, catch up and hit it with the off-hand swing). Expected: it stops running and
  fights you, as after a main-hand hit.
- [ ] **X07 Goo's Combat Overhaul** (optional, without Smoothbrain DualWield): pair two weapons and fight. Expected: a
  pair swing costs stamina once; note whether its lunge and speed tuning applies to pair swings; no errors.
- [ ] **X08 Other dual wield mods:** install Smoothbrain DualWield next to the mod. Expected: the BepInEx log says Dual
  Wielding was not loaded (incompatible), and a server with the mod refuses that player ("does not have the mod").
  Install RustyMods DualWielder instead. Expected: the MC Mods panel shows "Inactive: <its plugin name> also handles
  dual wielding. Remove one of them.", the log warns once with the same name (write the name down), and a server with
  the mod refuses that player ("has the mod turned off").
- [ ] **X09 Creature Kill and Tame Counts (MC):** kill a `Greyling` with a sword + axe pair (let an off-hand hit land
  the kill). Expected: the kill counts under melee in its line of the Compendium's Player Statistics.
