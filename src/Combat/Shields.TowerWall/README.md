# Tower Shield Wall

Tower shields become a two-handed moving wall: very slow, 2.5 times the block armor, no parry. Hold block with one
(bracing) and it also stops poison clouds and area attacks from the front, and blocked hits no longer stagger you or
push you back while your stamina lasts. The attack button bashes with the shield: little damage, heavy stagger.

## Features

- **Two-handed.** Equipping a tower shield empties both hands. Equipping a one-handed weapon, a torch, another shield,
  a bow, a two-handed weapon or a tool while you hold one puts the tower shield away. It keeps its shield look: in the
  left hand, in the shield slot on your back when you put it away (R, or when you swim), in the shield slot of an
  armor stand. The tooltip says "Two-handed". A tower shield you pick up goes to your inventory, never straight into
  your hands (as in the normal game).
- **Very slow in your hands.** Holding a tower shield slows you by 30% when jogging and 45% when sprinting (normal
  game: 10% and 15%); sprinting never becomes slower than jogging, even in heavy armor. Like every item, the slowdown
  counts only while the shield is in your hands: put away on your back, it does not slow you. Walking and crouching
  speeds are not affected by carried items (normal game rule). The tooltip shows "Movement -30%", and running, jumping
  and dodging cost 30% more stamina (normal game rule for equipment that slows you).
- **Bracing: holding block with a tower shield.** You move 30% slower still (every speed, and turning), so a braced
  wall advances at about half the normal jogging speed:

  | | Tower shield, normal game | With this mod |
  |---|---|---|
  | Jog | 3.6 m/s | 2.8 m/s |
  | Sprint (run skill 0) | 5.95 m/s | 3.85 m/s (never below jogging) |
  | Walk / crouch | 1.6 / 2.0 m/s | 1.6 / 2.0 m/s |
  | Braced jog / walk | 3.6 / 1.6 m/s | 1.96 / 1.12 m/s |
  | Braced turning | 300°/s | 210°/s |
  | Put away on your back | normal | normal |

- **No parry.** A block with a tower shield is never a parry: no parry bonus, no stagger of the attacker, no parry
  adrenaline, no parry flash. Tower shields already could not parry in the normal game; the mod keeps it that way and
  removes the Nord Greatshield's misleading parry adrenaline tooltip line (15). The tooltip says "Cannot parry".
- **2.5 times the block armor** (also the armor gained per quality level). The Blocking skill still adds up to 50%.
  Each blocked hit lets less damage through and costs less stamina, less stagger and less wear:

  | Tower shield (prefab) | Block armor, quality 1: normal → mod | Per quality level | 100 blunt blocked at Blocking 0: damage through / stamina, normal → mod |
  |---|---|---|---|
  | Wood tower shield (`ShieldWoodTower`) | 10 → 25 | 6 → 15 | — |
  | Bone tower shield (`ShieldBoneTower`) | 32 → 80 | 6 → 15 | — |
  | Iron tower shield (`ShieldIronTower`) | 52 → 130 | 6 → 15 | 48 / 10 → 19 / 6.2 |
  | Serpent scale shield (`ShieldSerpentscale`) | 60 → 150 | 6 → 15 | — |
  | Black metal tower shield (`ShieldBlackmetalTower`) | 104 → 260 | 6 → 15 | 24 / 7.3 → 9.6 / 3.5 |
  | Flametal tower shield (`ShieldFlametalTower`) | 140 → 350 | 6 → 15 | 18 / 5.9 → 7.1 / 2.7 |
  | Nord Greatshield (`ShieldGoldTower`) | 158 → 395 | 6 → 15 | 16 / 5.3 → 6.3 / 2.4 |

- **A wall that holds, while your stamina lasts.** While you brace:
  - **Enemy attacks from the front that normally cannot be blocked are blocked too**: the Blob's poison cloud, the
    Greydwarf Shaman's poison spray and other area attacks. They show "Blocked", and a block also stops the poison or
    other effect the hit carries (an Iron tower shield keeps out about 83% of a Blob's poison at Blocking 0). Spells of
    your allies, such as the Staff of Protection's shield, are never blocked: you still get them.
  - **You take 80% less stagger**, so blocked hits no longer break your guard, as long as you have the stamina for at
    least one more full block (10 stamina by default).
  - **Hits and area knockback from the front do not push you back** under the same condition.
  - **When your stamina runs low, the guard breaks the normal way**: with stamina for one block or less, the next hit is
    blocked with the normal rules, and a block that staggers you or empties your stamina lets the whole hit through.
    The brace comes back once your stamina is above one full block again.
  - What stays unblockable: hits from behind or the side (they also push you normally), true damage, falls, fire and
    lava, drowning, damage over time already running, and hits from allies and from creatures that are not hostile to
    you. Attacks that throw you into the air still throw you.
- **"Braced" status icon.** While you brace, the status list shows your tower shield's icon with "Braced". When your
  stamina is too low for the brace to hold, it flashes and reads "Braced (exhausted)". It disappears when you release
  block. The tooltip lists what bracing does ("Braced: movement -30%, blocks attacks from the front that normally
  cannot be blocked; while you have stamina for another block: stagger -80%, no knockback from the front").
- **Shield bash on the attack button.** Pressing attack with a tower shield bashes with it: a heavy, deliberate shove
  that deals little blunt damage but fills a creature's stagger bar fast (25 times its blunt damage, before the
  creature's armor). Every enemy in front of you (1.8 m, 60° arc) takes its damage and push, but only one of them takes
  its heavy stagger: the one nearest the middle of the swing (the one you face). The others are staggered only as much
  as by a punch. It uses and trains the Blocking skill, which raises its damage and push; it can be blocked and dodged;
  it never counts as a sneak attack. Pressing attack while you brace bashes too: the guard drops for the bash and comes
  back right after it. Each hitting bash costs 1 durability (normal weapon rule). The tooltip shows the bash's blunt
  damage, stamina use and knockback, plus "Bash stagger: ×25" and "Bash cooldown: 2 s".
  - **It costs 20 stamina** (a third less at Blocking 100), spent when the swing starts, whether it hits or not, so
    staggering with the bash is not easier than with a buckler parry: a timed parry is free, but a missed one costs a
    full block and lets damage through, so a player who lands one parry in three also pays about 20 stamina per
    stagger, and the bash needs no timing. Like a parry, one bash staggers one creature at most, also in a group.
  - **One bash every 2 seconds at most.** A press before the time is up (once the swing is over, or in its last half
    second), or the attack button held down, starts the next bash as soon as the time is up.
  - **A slow, readable swing.** The shield punch plays at 0.6 of its normal speed, its hit included: the hit lands
    about 0.8 s after you press attack, and the swing is over after about 1.1 s. Your guard is down for the whole
    swing. Other players see the same slow swing.

  | Tower shield | Bash blunt damage | Mean stagger per bash at Blocking 0 / 50 / 100 | Damage at Blocking 0 / 100 |
  |---|---|---|---|
  | Wood | 6 | 60 / 105 / 139 | 2.4 / 5.6 |
  | Bone | 8 | 80 / 140 / 185 | 3.2 / 7.4 |
  | Iron | 12 | 120 / 210 / 278 | 4.8 / 11.1 |
  | Serpent scale | 15 | 150 / 263 / 347 | 6 / 13.9 |
  | Black metal | 20 | 200 / 350 / 463 | 8 / 18.5 |
  | Flametal | 28 | 280 / 490 / 648 | 11.2 / 25.9 |
  | Nord Greatshield | 32 | 320 / 560 / 740 | 12.8 / 29.6 |

  For scale, creatures stagger at: Greydwarf 12, Skeleton 20, Draugr 50, Draugr Elite 100, Troll 180, Lox 300. So an
  Iron tower shield staggers a Draugr in one bash and a Troll in two to four (its stagger bar drains between two
  bashes: two or three bashes for about four Trolls in five, four for the rest). A staggered creature takes double
  damage from the next hits (normal game rule): the bash sets up your allies and your next weapon hit.
- **No endless stagger lock.** After a bash staggers a creature, bashes add no heavy stagger to it for 8 seconds (for
  every player): it recovers and fights back between staggers. Other weapons are not limited. In a group, turn to
  another enemy: a bash aimed at the creature you just staggered staggers no one in those 8 seconds.
- **Choose the bash animation.** The normal game has no parry animation (a parry is the block pose plus effects). The
  default bash, **ShieldPunch**, is a punch with the shield arm. Other options: **OtherPunch** (the other arm),
  **Kick**, and **Custom** (any player attack animation, for example `throw_bomb`, `spear_poke`, `mace_secondary`;
  not the animations of attacks that are held, aimed or reloaded, such as `staff_rapidfire`, `bow_fire` or
  `crossbow_fire`). Every option plays at the bash speed (`BashAnimationSpeed`). If an animation cannot land its hit in
  your game, or does not end in time, the mod switches to another one and says so in the log: Custom to ShieldPunch,
  then OtherPunch; Kick to OtherPunch. Whatever the animation, a bash is always one hit for one stamina cost.
- **Only the players' tower shields change.** Creatures that carry a tower shield use their own copy of it and fight
  as in the normal game; a modded creature that picks up a player's tower shield keeps a normal one-handed shield.
- Turn it off in the **MC Mods** panel at any time: tower shields are normal again at once, in your hands, your
  inventory, loaded chests and on the ground; no restart needed. Turning it on again while you hold a weapon and a
  tower shield puts the weapon away.

## Configuration

The config file `BepInEx/config/MC.Combat.Shields.TowerWall.cfg` is created the first time you launch the game with
the mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods**. Every setting can be changed
in-game with ConfigurationManager, or by editing the file (the game picks up the change while it runs); changes apply
about half a second after the last edit (about a second for the other players of a server), without re-equipping. In
multiplayer the server's (or host's) settings are used for everyone, except `Enabled`. The file and
ConfigurationManager list the sections in alphabetical order (Bash, Bracing, General, Tower shields).

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | Used only by the server (or the host). Off: a player whose game does not have this mod, has it turned off, or has a version that cannot talk to the server's, is refused about a second after joining (or after turning it off); their game shows "Incompatible version". On: such players may play; for them tower shields are normal shields. |
| Tower shields | Towers | `ShieldWoodTower:6, ShieldBoneTower:8, ShieldIronTower:12, ShieldSerpentscale:15, ShieldBlackmetalTower:20, ShieldFlametalTower:28, ShieldGoldTower:32` | Which shields are tower shields: comma-separated prefab names (the names the spawn command uses), each with the blunt damage of its bash after a colon (0 to 200; 10 when left out). Only shields are accepted; a shield that can parry loses its parry. Names the game does not know are reported in the log. |
| Tower shields | BlockArmorMultiplier | `2.5` | Tower shield block armor is multiplied by this, also the armor gained per quality level (1 to 10). |
| Tower shields | BlockForcePercent | `100` | How hard a block pushes the attacker back, in percent of the tower shield's own block force (0 to 200). Because tower shields block more of each hit, 100 already pushes somewhat harder than a normal tower shield would; lower keeps attackers within bash range. |
| Tower shields | CarrySlowPercent | `30` | How much slower you jog while a tower shield is in your hands, in percent (0 to 40; the normal game value is 10). Sprinting is slowed 1.5 times as much, but never below jogging speed. Not while it is put away on your back. Armor slowness adds to it. |
| Bracing | BraceSlowPercent | `30` | Extra slowdown while you block with a tower shield, in percent (0 to 90), on every movement speed and on turning. |
| Bracing | BraceStaggerResistPercent | `80` | While you block with a tower shield and have stamina for at least one more full block, you take this much less stagger, in percent (0 to 100), so blocked hits do not break your guard. |
| Bracing | BraceKnockbackResistPercent | `100` | Under the same conditions, hits and area attacks from the front push you back this much less, in percent (0 to 100; 100 = not at all). |
| Bracing | BlockUnblockableAttacks | `true` | While you block with a tower shield, enemy attacks from the front that normally cannot be blocked (poison clouds, sprays, area attacks) are blocked too. Spells of your allies (such as the Staff of Protection's shield) are never blocked. |
| Bash | BashAnimation | `ShieldPunch` | Animation of the shield bash: `ShieldPunch` (a punch with the shield arm), `OtherPunch` (the other arm), `Kick`, `Custom` (the animation named in BashCustomTrigger). If the chosen animation cannot land its hit or does not end, the mod switches to another one and says so in the log: Custom to ShieldPunch, then OtherPunch; Kick to OtherPunch. Names can be written in any case; any other value is read as ShieldPunch (with a warning in the log). Try the options in single player or as the host. |
| Bash | BashCustomTrigger | (empty) | With BashAnimation = Custom: the attack animation to play (for example `throw_bomb`, `spear_poke`, `mace_secondary`). Names that are not player attack animations, and animations of attacks that are held, aimed or reloaded (such as `staff_rapidfire`, `bow_fire`, `crossbow_fire`), fall back to ShieldPunch and are reported in the log. |
| Bash | BashAnimationSpeed | `0.6` | Speed of the bash animation, as a multiple of its normal speed (0.3 to 1.5; 1 = as fast as a normal punch). Lower is slower: the whole swing, its hit included, takes longer, and other players see the same speed. |
| Bash | BashStamina | `20` | Stamina cost of a bash, spent when the swing starts, whether it hits or not (1 to 50; the Blocking skill lowers it by up to a third). |
| Bash | BashCooldown | `2` | Shortest time between the starts of two bashes, in seconds (0 to 10). Pressing attack before the time is up (once the swing is over, or in its last half second) starts the next bash as soon as it is. 0 = the next bash can start as soon as the previous one ends. |
| Bash | BashStagger | `25` | How much a bash fills a creature's stagger bar, as a multiple of its blunt damage before the creature's armor (2 to 50; only for the enemy nearest the middle of the swing, the others are staggered as by a punch). The heaviest normal-game attacks use 6. |
| Bash | BashStaggerLock | `8` | Seconds after a bash staggers a creature during which bashes add no heavy stagger to it (for every player), so it recovers and fights between staggers (0 to 30; 0 = no limit). |
| Bash | BashKnockback | `40` | How far a bash pushes enemies (0 to 200; grows with the Blocking skill). |
| Bash | BashRange | `1.8` | Reach of the bash in meters (1 to 3). |
| Bash | BashAngle | `60` | Width of the bash arc in degrees (10 to 180). Every enemy in it takes the bash's damage and push, but only the one nearest the middle of the swing takes its heavy stagger. |

Other shields can be made tower shields by adding their prefab name to `Towers` (modded shields too, for example
`ShieldIronSquare:12` for the iron square shield, which is not in the default list). An item that is not a shield is
refused with a log warning.

## Multiplayer

- **Who needs it:** required on the server (or the host) and on every player's game.
- **Multiplayer support:** works in multiplayer.

Install it on the server (or the host) and on every player's game: it changes combat for everyone, so the server
refuses players who cannot play by its rules, and its settings apply to everyone.

- **Players are refused** about a second after they join (their game goes back to the menu with "Incompatible
  version") when their game does not have the mod, has it turned off in the MC Mods panel, or has a version of the
  mod that cannot talk to the server's. The server log names the player and the reason. A player who turns the mod off
  while connected is refused about a second later. The server (or host) can let such players in with
  `AllowPlayersWithoutMod = true`; for them tower shields are normal shields.
- **The server's settings apply to everyone** (your log says "Using the server's tower shield rules"), also when the
  host changes them during play (about a second later, without re-equipping). Your own settings apply again in
  single player and when you host. For the same reason `BashAnimation` and `BashAnimationSpeed` follow the server: try
  the animations and speeds in single player or as the host.
- **Joining**: until the server's settings arrive (a moment after joining), your tower shields are normal shields, so
  nothing is taken off your hands because of your own settings.
- **On a server without the mod** it turns itself off on your game (the MC Mods panel says why): tower shields are
  normal there. When the server (or host) turns the mod off, it turns off for everyone and tower shields become normal
  at once; when it turns it back on, they become tower shields again without rejoining.
- **Dedicated servers:** install BepInEx on the server and put the mod in its `BepInEx/plugins` folder; the server's
  config file decides the rules. The server also applies the bash stagger to the creatures it controls.
- **The bash stagger is applied by the game that controls the creature** (every game with the mod does it the same
  way), so it does not shrink when several players are nearby. Which creature of a group gets it is decided by the
  bashing player's game. On a creature controlled by a game without the mod
  (only possible with `AllowPlayersWithoutMod` on), the bash staggers less and has no 8-second limit.
- **Giving a tower shield to a player without the mod** (only possible with `AllowPlayersWithoutMod` on): for them it
  is a normal one-handed tower shield. Nothing is stored on the item: back in the hands of a player with the mod, it is
  two-handed again.
- Every player sees a modded player's tower shield in the left hand and on the back, the block pose and the bash
  animation at the same slow speed (the game itself shares a player's animation speed), also players without the mod.

## Good to know

- **A tower shield has no secondary attack**: with a normal shield and an empty right hand, the secondary button
  kicks; with a two-handed tower shield it does nothing. There is no separate bash key.
- **The bash is a commitment**: your guard is down for the whole slow swing (about 1.1 s), a bash that misses still
  costs its 20 stamina, and the next one waits 2 seconds. Bash when an enemy is open, then raise the shield again.
- **No torch with a tower shield**: two-handed means no free hand (equipping a torch puts the shield away).
- **Stagger resistance works from every side while you brace**, but hits from behind are still not blocked: full
  damage and a full push, only less stagger.
- **Attacks that throw you into the air** (some area attacks) still do, even while braced.
- **The bash trains Blocking**, like any weapon trains its skill, also on a training dummy. Blocking weak creatures
  with 2.5 times the block armor is also cheaper than in the normal game.
- **New Game+ worlds** (world level above 0): the normal game adds 120 damage per world level to every weapon; the
  bash does not get it, so its tooltip keeps the list value and it deals almost no damage against New Game+ armor. Its
  stagger grows with creature health instead, so it staggers the same creatures in the same number of bashes as in a
  normal world. A bash that staggers a creature always alerts it, so the hit that follows is never a sneak attack.
- **The Serpent scale shield counts as a tower shield** (it has tower shield data: no parry, slow, heavy). The iron
  square shield (`ShieldIronSquare`) does not by default; add it to `Towers` if you want it.
- **A weapon and a tower shield both in hand** (possible while the mod is off): turning the mod on while you play puts
  the weapon away and keeps the tower shield. Loading a character saved that way with the mod on, or joining a server
  with the mod with it, keeps only one of the two (the game's own rule: the one later in the inventory), because the
  server's settings arrive before your character is in the world.
- **Nothing is stored** on items, characters or the world: removing the mod gives normal tower shields back
  everywhere, and adding it changes every tower shield you already have.
- The settings change the values of tower shield items directly; data mods that edit the same tower shield values
  (WackysDatabase packs such as Make Tower Shields Great Again) are overwritten by this mod. Use one or the other.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is
packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. It stores nothing of its own in the world, on characters or on items. Safe to add or remove
at any time.

When another mod that changes shields, blocking or attacks is installed, the log shows one warning naming it. Mods
that look for shields by their item type stop seeing tower shields as shields (they are two-handed now); round
shields and bucklers are not changed.

- **CaptainValheim**: its shield strike, throw, charge, reflection and block charges stop applying to tower shields;
  round shields keep them.
- **SecondaryAttacks, ShieldMeBruh, SmartShield, AutoShield**: their automatic shield equip ignores tower shields
  (no equip loop).
- **ZenCombat**: its tower shield block charges stop on these tower shields (they would fire the bash with no
  animation and no stamina). Its reliable block works alongside the brace.
- **Goo's Combat Overhaul**: has its own tower shield rules and may no longer treat these tower shields as tower
  shields. Use one of the two mods for tower shields.
- **ShieldBash**: adds a second bash on its own key. Use one of the two.
- **ReliableBlock, ReliableBlockRebuilt**: compatible in effect (they keep a staggered block working).
  ReliableBlockRebuilt turns itself off when another mod patches blocking (MC Crossbow Stays Loaded does).
- **WeaponArts, Combat Adjustments**: their tower shield bonuses (taunt, stagger bar) stop applying to these tower
  shields.
- **EpicLoot**: magic already on a tower shield stays; its enchanting rules may treat tower shields as two-handed
  weapons (not tested).
- **WackysDatabase** tower shield edits: overwritten (see "Good to know").
- **Mods that give player shields to creatures**: those creatures keep normal one-handed shields.

MC mods:

- **Weapon Moveset** (MC): jump and roll attacks are never used with a tower shield; the bash stays the bash. A bash
  pressed late in a roll starts once the roll is over, as in the normal game (it never flows out of the roll).
- **Dual Wielding** (MC): a tower shield puts both weapons of a pair away, and a weapon puts the tower shield away. A
  tower shield put away sits in the shield slot on your back as usual; only a sheathed pair of weapons is crossed.
- **Creature Morale** (MC): a bash is a hit, so a creature that is afraid of you (and runs from you) stops running and
  fights you after a bash, also in New Game+. To bash one, sneak up on it or catch it.
- **Sneak Ambush** (MC): a bash never counts as a sneak attack and pays no Sneak experience; raising the shield ends
  the crouch.
- **Crossbow Stays Loaded** (MC): a crossbow put away by a tower shield stays loaded.
- **Forge Idol Upgrades** (MC): a refined tower shield stays a tower shield, with the block armor of its new quality.
- **Harpoon Hooks Tames** (MC): both mods work on hits; they do not get in each other's way.
- **Sort Chest, Crafting Search and Sort, Encyclopedia** (MC): tower shields still sort and group with shields.
- **Creature Kill and Tame Counts** (MC): a bash kill counts as a melee kill.
- These combinations have not been tested in game yet.
