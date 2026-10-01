# Trinkets on Demand — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
When a code change affects a behaviour, its tests go back to `[ ]`.
Run `./tools/Get-TestTodo.ps1` to see every pending test across the project.

**Build under test:** 0.1.0, build id = the commit in the `[MC:ready]` log line (also in the MC Mods panel). Smoke
test (`./tools/Test-Smoke.ps1`) passed 2026-10-01 (loads, patches cleanly, JitCheck clean). In-world self-tests
(`./tools/Test-InWorld.ps1`) passed 2026-10-01 on the final code: all 8 of this mod's tests (`trinkets.network`,
`trinkets.controls`, `trinkets.pending`, `trinkets.hold-and-fire`, `trinkets.income`, `trinkets.combat-state`,
`trinkets.ranged-factor`, `trinkets.ranged-shot`); their NOTE lines gave the measured values below. No hands-on
in-game test yet.

**Setup:** F5 for the console: `devcommands` (if the console asks you to confirm cheats, run `confirmcheats`). `god`
keeps you alive (type it again to turn it off; turn it off for the items where you take hits), `heal` refills health,
`killall` removes nearby creatures. Spawn with `spawn <name> <amount> <level>`, and add `p` to put items straight
into your inventory (`spawn TrinketBronzeHealth 1 1 p`). `adrenaline <n>` sets the bar (it goes through the normal
gain rules, so `adrenaline 999` fills it; `adrenaline 0` empties it). Skills: `resetskill Bows`, `raiseskill Bows
100` (same for `Crossbows`). Names checked in the 1.0.16 game data:

- trinkets: `TrinketBronzeHealth`, `TrinketBronzeStamina`, `TrinketIronHealth`, `TrinketIronStamina`,
  `TrinketSilverDamage`, `TrinketSilverResist` (and the other vanilla trinkets listed in `docs/game/combat.md` §8);
- creatures: `Greydwarf`, `Greyling`, `Boar`, `Deer`, the training dummy `piece_TrainingDummy`;
- items: `SwordIron`, `KnifeCopper`, `ShieldWood`, `ShieldGoldTower`, `Bow`, `CrossbowArbalest`, `SpearChitin` (the
  harpoon);
- ammo: `ArrowWood`, `BoltBone` (every arrow and bolt is listed in the `trinkets.ranged-factor` NOTE).

Values measured by the in-world self-tests (Valheim 1.0.16, NOTE lines of `trinkets.hold-and-fire`,
`trinkets.ranged-factor` and `trinkets.ranged-shot`):

- the 15 vanilla trinkets cost 10 to 100 adrenaline (`TrinketBronzeHealth` 50, `TrinketIronHealth` 65,
  `TrinketSilverDamage` 55, `TrinketChitinSwim` 10, `TrinketFlametalStaminaHealth` 100), their effects run 1 to 120
  seconds, and none has an up-front adrenaline gain;
- the Player: a melee miss and an unblocked hit cost 0, a perfect dodge pays 5, a stagger 3; the drain runs at 1 per
  second near empty up to 4 per second when full, and starts 10 seconds (empty) to 6 seconds (full) after the last
  gain; the gain multiplier is 1 at every fill; no tier effects; the pop effect is `fx_Adrenaline1`; the HUD bar has
  the Flash animation;
- every arrow and bolt pays 2 per hit; every player bow has a 2.5 s full draw at Bows 0 (0.5 s at 100) and every player
  crossbow a 3.5 s reload at Crossbows 0 (1.75 s at 100). A full-draw `Bow` shot at Bows 0 was measured: cycle 3 s,
  factor x3, the arrow paid 6 (with the old 1 s reference; at the 1.5 s default the same shot gives x2, 4);
- melee pay per enemy hit of the 130 player weapons: the primary attack of one-handed weapons (swords, axes, maces,
  knives, spears, fists, pickaxes) pays 1, of two-handed ones (two-handed swords, battleaxes, sledges, most atgeirs) 2;
  secondary attacks pay 1 to 3.

Still **(unverified)**: the half second counted for firing a shot (`ShotSeconds`, a design value), everything about
gamepads (T12, T13), and the GUIDs of the other mods in X08 (Surge's, `ezomic.valheim.surge`, is from its source).

Most expected results name Debug log lines: turn on Debug logging with `./tools/Setup.ps1 -DevBepInExConfig` and
follow the log with `./tools/Watch-Log.ps1 -Mine`. Settings are changed with ConfigurationManager (F1) or in
`BepInEx/config/MC.Combat.Trinkets.OnDemand.cfg`; default settings unless an item says otherwise, and put each changed
one back afterwards. "Wear a trinket" = `spawn TrinketBronzeHealth 1 1 p` and equip it (its cost sets the bar's
width). Use a world with the default world modifiers. Run the items outside "other mods" with Creature Morale and
Sneak Ambush turned off in the MC Mods panel (or not installed): afraid or smoke-blinded creatures stop hunting you.

## 0.1.0 — single player

- [ ] **T01 Trinkets unchanged (G1):** with the mod turned off in the MC Mods panel, hover `TrinketBronzeHealth` and
  note its tooltip (effect, adrenaline cost), wear it and note the bar's width. Turn the mod on. Expected: the same
  effect and cost in the tooltip, plus one line "Trigger: [Y] when the adrenaline bar is full" right after the
  full-adrenaline line; the same bar width. Fire it (T08) and compare the effect's duration and numbers with the
  normal game's (fill the bar with the mod off): the same.
- [ ] **T02 Income in a fight (G2):** wear a trinket, `adrenaline 0`, `spawn Greydwarf 1 1` and let it hit your raised
  shield (`ShieldWood`) without hitting back. Expected: Debug "In a fight: adrenaline income on."; the bar rises by about
  1 every second on top of the block gains (each block adds its normal amount).
- [ ] **T03 Fight ends:** after T02, `killall` (or walk far away). Expected: about 6 seconds after the last hit, Debug
  "Fight over: adrenaline income off."; the bar stops rising and keeps its value.
- [ ] **T04 Hunting keeps the fight:** with a `Bow`, hit a `Greydwarf` once from far away and back off while it chases
  you, without anyone landing a hit. Expected: income continues while it hunts you (the combat music plays), up to
  about 20 seconds after that hit, then stops.
- [ ] **T05 Not a fight:** `adrenaline 0`, wear a trinket. (a) Hit `piece_TrainingDummy` (build it, or spawn it) many
  times with `SwordIron`. Expected: each hit adds only the sword's normal amount, no Debug "In a fight" line, no income.
  (b) Hit a tamed creature (`spawn Boar`, `tame`, then hit it): no income. (c) Fall from a height: no income.
- [ ] **T06 No drain (G3):** wear a trinket, `adrenaline 20`, wait 60 seconds out of a fight. Expected: the bar stays at
  20 (the normal game starts draining it 6 to 10 seconds after the last gain). Unequip the trinket. Expected: the bar
  drains and hides as in the normal game.
- [ ] **T07 Losses stay (G3):** in Valheim 1.0.16 the normal game's losses are 0 (an unblocked hit and a melee miss,
  measured), so this checks that the mod adds none: `god` off, wear a trinket, `adrenaline 20`, let a `Greydwarf` hit
  you without blocking, and swing at nothing. Expected: neither the hit nor the miss lowers the bar (in the fight the
  income adds about 1 per second).
- [ ] **T08 A full bar waits, the key fires it (G4, G5):** wear a trinket, `adrenaline 999`. Expected: the bar is full
  and stays full: no trinket effect starts; hits, blocks and swings that miss do not fire it; out of a fight it still
  stays full after a minute. Press Y. Expected: the trinket's effect starts (its icon), the bar empties, the normal
  game's adrenaline effect (`fx_Adrenaline1`) plays; Debug "Trinket triggered by the player: bar emptied, effects
  added or refreshed."
- [ ] **T09 Answers to a press:** (a) half bar (`adrenaline 10` with a costlier trinket), press Y. Expected: "Adrenaline
  not full yet" in the middle of the screen, nothing spent. (b) No trinket worn, press Y. Expected: "No trinket to
  trigger".
- [ ] **T10 Press while the effect runs:** fire the trinket, `adrenaline 999` at once, press Y again. Expected: the
  effect restarts (its timer is full again) and the bar empties. Set `RefuseWhileActive = true`, repeat. Expected:
  "Trinket effect still active", the bar stays full; once the effect ends, Y fires it.
- [ ] **T11 When the key works:** full bar; press Y (a) with the chat open (type a message with a "y" in it), (b) with the
  inventory, the map, the console or the Esc menu open, (c) with the radial menu open (hold G), (d) in build mode
  (hammer). Expected: nothing fires and no message, in all four. (e) Press Y mid-swing and mid-roll. Expected: it fires
  at once, the swing or roll goes on.
- [ ] **T12 Gamepad, default layout:** full bar; hold the left trigger (your shield rises) and press the right stick.
  Expected: the trinket fires. Release the left trigger before the right stick: your weapons are not put away; hold
  the right stick longer: the radial menu does not open. The right stick alone still puts your weapons away (tap) and
  opens the radial menu (hold). The message and the tooltip show the gamepad buttons while the gamepad is in use.
- [ ] **T13 Gamepad, alternative layouts:** switch to Alternative 1, then Alternative 2 (Settings, Controls). Press
  left trigger + right stick with a full bar. Note what else happens (Alternative 1: weapons put away or the radial
  menu opening from the left trigger, crouch; Alternative 2: shield raised, crouch, the "alternative placement"
  message). Then set `GamepadModifier = RightBumper`, `GamepadButton = DPadRight` and try again. Expected: the trinket
  fires; note any side effect, for the README's advice.
- [ ] **T14 Other keys:** set `TriggerKey = U` while playing. Expected: U fires, Y does nothing; the full-bar message
  and the tooltip say [U]. Set `TriggerKey = F13`. Expected: one Warning "Controls.TriggerKey = F13: the game cannot
  read this key, so the keyboard trigger is off."; the message then names the gamepad buttons. Set `TriggerKey = None`
  and `GamepadButton = None`. Expected: the message says to set a trigger key; the tooltip says "no key set".
- [ ] **T15 Full-bar feedback:** wear a trinket, fill the bar by fighting. Expected: when it becomes full, the bar
  flashes and the top left says "Adrenaline full: press [Y] to trigger your trinket"; while it stays full, it flashes
  again every 4 seconds; the message comes back at most every 20 seconds. With
  `ShowFullMessage = false`: no message, the flash stays; with `FullFlashInterval = 0`: one flash only.
- [ ] **T16 Swapping trinkets:** pick two trinkets with different costs, for example `TrinketIronHealth` (65) and
  `TrinketBronzeHealth` (50) (all costs are in the `trinkets.hold-and-fire` NOTE line). Full bar with the costlier
  one, swap to the cheaper one. Expected: the bar stays full and its number drops to the cheaper cost; Y fires the
  cheaper one. Then full bar with the cheaper one, swap to the costlier one. Expected: the bar is no longer full
  (same amount, wider bar); Y says "Adrenaline not full yet".
- [ ] **T17 Bow (G6):** `resetskill Bows`, `Bow`, a stack of `ArrowWood`, `spawn Greydwarf 2 3` (tough ones), wear a
  trinket, `adrenaline 0`. (a) Full-draw shots that hit, at least 3 seconds apart. Expected: Debug "Ranged shot (Bows):
  cycle 3 s, first shot (or … s since the last shot) -> adrenaline per hit x2", and each hit raises the bar by 4 (the
  arrow's 2 times 2, plus income). (b) Quick taps that hit. Expected: the line says x1 and each hit adds only 2. (c)
  `raiseskill Bows 50`: full draws. Expected: cycle 2 s, x1.33 (about 2.7 per hit). `raiseskill Bows 50` again (Bows
  at 100): cycle 1 s, x1 (2 per hit).
- [ ] **T18 Crossbow (G6):** `resetskill Crossbows`, `CrossbowArbalest` with `BoltBone`, wear a trinket, `adrenaline 0`.
  Expected: Debug "Ranged shot (Crossbows): cycle 4 s … -> adrenaline per hit x2.67" and each bolt hit raises the bar
  by about 5.3 (plus income); after `raiseskill Crossbows 100` (Crossbows at 100): cycle 2.25 s, x1.5 (3 per hit).
- [ ] **T19 Other ranged weapons unchanged:** throw a spear, cast a staff, throw a bomb. Expected: no "Ranged shot" line,
  normal adrenaline.
- [ ] **T20 Respawn and login:** with a half bar, die (`god` off) or log out and back in. Expected: the bar is empty
  (normal game), no error.
- [ ] **L01 Live toggle:** (a) wear a trinket, `adrenaline 20`, wait 30 seconds, then untick Trinkets on Demand in the
  MC Mods panel. Expected: about a second later the bar starts draining (normal game). (b) Tick it on, stand next to a
  `Greydwarf`, `adrenaline 0` then `adrenaline 999` (held full; the normal game's drain delay starts at 10 seconds),
  untick it and hit the Greydwarf within about 5 seconds. Expected: the trinket fires on that hit (normal game). Tick
  it on, fill the bar the same way, untick it and do nothing. Expected: within about 10 seconds the bar drains from
  full without firing the trinket (normal game: the drain never fires it). (c) Tick it on again. Expected: Y works
  again, the bar stops draining, the tooltip line is back.
- [ ] **L02 `Enabled = false` + restart:** set `Enabled = false` in `BepInEx/config/MC.Combat.Trinkets.OnDemand.cfg`
  and start the game. Expected: `Status` reads "Off (disabled in settings)."; the bar drains and fires at once as in
  the normal game; Y does nothing; no tooltip line; no error in the log.
- [ ] **L03 Clean log:** play through T01-T20 while `./tools/Watch-Log.ps1 -Mine` runs, then quit the game from the
  world (Esc, Logout, Exit). Expected: no error and no warning from Trinkets on Demand (the F13 warning of T14 aside),
  in particular no Unity warning about a missing "Flash" animator parameter.

## 0.1.0 — multiplayer

- [ ] **M01 Server rules:** a dedicated server (or a host) and two players, all with the mod. Set `IncomePerSecond = 3`
  in the server's config while they play. Expected: both players log Info "Using the server's rules: income 3
  adrenaline per second in a fight …" and gain about 3 per second in a fight; their own config files are unchanged. Put
  it back to 1.
- [ ] **M02 Player without the mod refused:** a player without the mod joins. Expected: about a second after joining
  their game goes back to the menu with "Incompatible version"; the server logs a Warning "Refused <player>: does not
  have the mod. This server requires Trinkets on Demand on every player …". With `AllowPlayersWithoutMod = true` on
  the server they can play, and the server logs a Warning "<player> does not have the mod; AllowPlayersWithoutMod is
  on, so they may play, but their adrenaline and trinkets work like the normal game …". A player with the mod on joins
  normally (Debug "<player> has Trinkets on Demand on, with the same network version: allowed.").
- [ ] **M03 Hand-off:** `AllowPlayersWithoutMod = true` on the server. A player with the mod drops a trinket (or puts it
  in a chest); a player without the mod takes it and wears it. Expected: for them it works as in the normal game (its
  tooltip has no trigger line, a full bar fires it at once, the bar drains); for the player with the mod, trinkets keep
  working with the key.
- [ ] **M04 Server without the mod:** join a server without the mod. Expected: the MC Mods panel shows "Inactive: the
  server does not have this mod. …"; the bar works as in the normal game (drains, fires at once), Y does nothing.
- [ ] **M05 Personal settings stay yours:** the server has `TriggerKey = U`, `ShowFullMessage = false`; a player keeps
  Y and true. Expected: the player fires with Y and sees the full-bar message.
- [ ] **M06 Another player's creatures:** player B spawns a `Greydwarf` (B's game controls it); A wears a trinket and
  fights it (hits it, gets hit, blocks). Expected: A gets the income as in T02, and A's full bar waits for A's key.
  B staggers a creature A's game controls: B gets the stagger adrenaline, held when B's bar is full.
- [ ] **M07 Mod turned off, refused:** `AllowPlayersWithoutMod = false` (default) on the server. (a) A player with the
  mod and `Enabled = false` joins. Expected: about a second after joining their game shows "Incompatible version";
  the server log says "Refused <player>: has the mod turned off". (b) A player with the mod on joins and plays, then
  unticks Trinkets on Demand in the MC Mods panel. Expected: their log says "Told the server that Trinkets on Demand is
  now off on this game."; about a second later they are refused the same way. (c) Untick and tick it again within a
  second. Expected: they stay.
- [ ] **M08 Other network version:** build a copy with another network version (`dotnet build
  src/Combat/Trinkets.OnDemand/MC.Combat.Trinkets.OnDemand.csproj -p:ModNetworkVersion=2 -p:DeployToGame=false`), copy
  that DLL over a second player's copy (put the normal build back afterwards), and join a server with network version
  1. Expected: refused about a second after joining; the server log says "has another version of the mod (network
  version 2, the server has 1)". With `AllowPlayersWithoutMod = true` they stay, and their MC Mods panel says the
  versions cannot talk to each other; their bar works as in the normal game.
- [ ] **M09 Dedicated server:** a dedicated server with the mod and one player. Do T02, T08 and T17 there. Expected:
  the same results as in single player (everything runs on the player's game); no error in the server's log.

## 0.1.0 — other mods

- [ ] **X01 Dual Wielding (MC):** dual wield two `KnifeCopper` (see Dual Wielding's README), wear a trinket.
  Expected: H swaps hands and Y fires the trinket (no clash); a both-hands hit pays adrenaline once per swing as
  without this mod; with a full bar, dual-wield swings that hit or miss do not fire it.
- [ ] **X02 Weapon Moveset (MC):** jump and roll attacks with `SwordIron` on a `Greydwarf`, wear a trinket. Expected:
  they pay adrenaline like other swings and count as a fight (income); press Y mid-roll with a full bar: it fires.
- [ ] **X03 Crossbow Stays Loaded (MC):** load two crossbows, fire one, swap to the other (still loaded) and fire at
  once. Expected: the second shot's Debug "Ranged shot" line says about 1 s since the last shot and x1 or close to it
  (under 1.5 s since the last shot earns no bonus), not x2.67.
- [ ] **X04 Harpoon Hooks Tames (MC):** hook a tamed `Boar` with `SpearChitin` while wearing a trinket. Expected: the
  bar does not move, no "Ranged shot" line (a harpoon is not a bow or crossbow), no error.
- [ ] **X05 Tower Shield Wall (MC):** wear a trinket, `ShieldGoldTower`, block right as a `Greydwarf` hits. Expected:
  a half bar rises by the normal block amount (a tower shield never parries); with a full bar, the block does not fire
  the trinket.
- [ ] **X06 Sneak Ambush (MC):** in a fight with a `Greydwarf`, throw a Smoke Screen at your feet and stay inside.
  Expected: it loses you, and about 6 seconds after the last hit Debug "Fight over: adrenaline income off.".
- [ ] **X07 Creature Morale (MC):** make a `Greyling` flee (Creature Morale's own test setup). Expected: while it flees
  and nobody hits, the fight ends after about 6 seconds; hitting it again starts a new one.
- [ ] **X08 Other mods (optional):** with BetterTrinkets (or Passive_Trinket_Modifiers, or Balrond Battle Flow)
  installed. Expected: one Warning "<mod> (<GUID>) also changes when or what trinkets fire, so Trinkets on Demand stays
  off …" naming its plugin name and GUID (note them for the design doc), and the MC Mods panel says "Inactive: <mod>
  also changes when trinkets fire. Remove one of them.". With AdrenalineModifier, KeepAdrenalineLonger,
  RageNAdrenaline, MultiTrinket or Surge (this mod on, in a world): an Info line "<mod> is installed: …", the Debug
  line "Loaded plugins (GUID, name, version): …" shows its GUID and name (note them for the design doc; Surge's is
  `ezomic.valheim.surge`), and both work.
