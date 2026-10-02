# Weapon Moveset

Two new moves for melee weapons, built from the game's own animations: jump and attack before you land for a **jump
attack**, or roll and attack for a **roll attack** that flows straight out of the roll. Each move plays another swing
of your weapon's combo with modest damage and stagger bonuses, and your combo carries on from it. Everything else
stays vanilla.

## Features

- **Jump attack.** Jump, then attack before you land: the first attack of that jump plays the last swing of your
  weapon's combo (the sword's finisher, for example) with **×1.2 damage and ×2 stagger** by default. You keep the
  jump's momentum through the swing. One jump attack per jump; a second attack in the same jump is a normal air swing.
- **Aim down (or up) in the air.** While you are still in the air, a jump attack's swing follows your aim up or down by
  up to 30 degrees, so you can strike a boar below you or a flyer above. The game tilts a swing a little less than your
  view (looking 30 degrees down tilts it about 27 degrees). Normal swings are always level, and a jump attack is level
  again once you land.
- **Roll attack.** Roll, then attack: the first attack of the roll plays the second swing of your weapon's combo with
  **×1.3 damage and ×1.5 stagger** by default.
- **The roll attack flows out of the roll.** No standing up to idle between the roll and the swing: press attack during
  the second half of the roll (the game remembers a press for half a second) or hold the button, and the swing cuts
  into the very end of the roll 0.85 seconds after it started (setting **FlowStart**) and grows out of it over 0.15
  seconds (setting **FlowBlend**). A new press right after the roll, while you are still getting up from it (about the
  first 0.2 seconds), also makes a roll attack that flows out of the roll's end; a later press is a normal swing, so a
  roll attack never plays after the stand-up. The roll's invulnerability (its first 0.4 seconds or so) always ends at
  least 0.2 seconds before the swing starts: a roll attack is never invulnerable, on your screen or on anyone else's.
- **The combo carries on.** A move takes the place of one swing of your combo: after a sword roll attack (the second
  swing) your next attack is the finisher, as usual. A jump attack that plays the finisher is followed by the first
  swing, as after any finisher.
- **Default animations per weapon type** (each one is a setting, see Configuration):

  | Weapons | Jump attack | Roll attack |
  |---|---|---|
  | One-handed swords | third swing (`swing_longsword2`) | second swing (`swing_longsword1`) |
  | Maces and clubs | third swing (`swing_longsword2`) | second swing (`swing_longsword1`) |
  | One-handed axes | third swing (`swing_axe2`) | second swing (`swing_axe1`) |
  | Battleaxes | third swing (`battleaxe_attack2`) | off |
  | Dual axes (Berserkir axes; with Dual Wielding, every pair except two knives) | fourth swing, two hits (`dualaxes3`) | second swing (`dualaxes1`) |
  | Two-handed swords | third swing (`greatsword2`) | second swing (`greatsword1`) |
  | Atgeirs | third swing (`atgeir_attack2`) | second swing (`atgeir_attack1`) |
  | Knives | third stab (`knife_stab2`) | second stab (`knife_stab1`) |
  | Dual knives (Skoll and Hati; with Dual Wielding, pairs of two knives) | third swing (`dual_knives2`) | second swing (`dual_knives1`) |
  | Spears | off | off |
  | Bare hands and fist weapons | second punch (`unarmed_attack1`) | second punch (`unarmed_attack1`) |
  | Sledgehammers | off | off |

  "Off" means the attack stays a normal swing with no bonus. Spears and sledgehammers have a single attack animation,
  so a move would look exactly like a normal attack; the battleaxe's second swing hits much sooner than its slow first
  swing, so a roll attack would open far faster than the weapon normally does.
- **Everyone sees your moves.** They are the game's own animations, so every player sees them, with or without the
  mod. Players with the mod also see your roll attacks flow out of the roll; a player without it (only if the server
  allows such players) sees your roll finish first, then the swing.
- **Only the primary attack of melee weapons changes.** Secondary attacks, bows, crossbows, staffs, bombs, tools,
  pickaxes, torches, the fishing rod, shields and the tower shield bash (Tower Shield Wall) always stay vanilla. A move
  keeps the weapon's own reach, hit arc, damage types and effects; only the animation and the multipliers change.
- **Stamina.** The jump or roll costs its normal stamina, then the swing costs its normal stamina (times the move's
  StaminaMultiplier, 1 by default). If you can pay a normal swing but not the move, you get a normal swing.
- **A short cooldown.** Two moves cannot start within 1 second of each other (setting **Cooldown**). A normal roll or
  jump already takes longer, so this only limits instant dash mods and fast jump-attack spam.
- **Safe fallback.** If a move's animation does not start within half a second (possible with some animation
  choices), the mod cancels it, a normal swing follows at once, and the log names the setting to change.
- Turn it off in the **MC Mods** panel at any time: combat is vanilla again, no restart needed. Each move can also be
  switched off on its own (settings **JumpAttack** and **RollAttack**).

## Configuration

The config file `BepInEx/config/MC.Combat.Weapons.Moveset.cfg` is created the first time you launch the game with the
mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration manager
installed that button is hidden: press F1 and use its window). Every setting can be changed in-game with
ConfigurationManager, or by editing the file (the game picks up the change while it runs). In multiplayer the server's
(or host's) settings are used for everyone, except Enabled (each player's own) and AllowPlayersWithoutMod (read only by
the server).

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | Server (or host) only. Off: a player whose game does not have this mod, has a version that cannot talk to this one, or has it turned off is refused about a second after joining or after turning it off (their game shows "Incompatible version"). On: such players may play; their own jumps and rolls stay normal. |
| Moves | JumpAttack | `true` | Jump, then attack before you land: the first attack of the jump becomes a jump attack. Off: attacks in the air after a jump stay normal swings, even right after a roll. |
| Moves | RollAttack | `true` | Roll, then attack: the first attack of the roll becomes a roll attack, and it flows straight out of the end of the roll. |
| Moves | Cooldown | `1` | Minimum time in seconds (0-10) between the start of one jump or roll attack and the next one (either kind); an attack inside that time is a normal swing. 0 = no limit. |
| Jump attack | DamageMultiplier | `1.2` | Damage of a jump attack compared with a normal swing of the same weapon (0.1-5; 1 = the same). It also applies to trees and rocks. |
| Jump attack | StaggerMultiplier | `2` | How much faster a jump attack fills the target's stagger bar (0-10; 1 = like a normal swing). |
| Jump attack | PushMultiplier | `1` | Knockback of a jump attack (0-5; 1 = like a normal swing). |
| Jump attack | StaminaMultiplier | `1` | Stamina of a jump attack's swing, on top of the jump itself (0-5; 1 = the swing's normal cost). If you can pay a normal swing but not the jump attack, you get a normal swing. |
| Jump attack | AimAngle | `30` | Up to how many degrees (0-45) a jump attack's swing tilts toward where you look, down or up, while you are in the air. The game tilts a swing less than your view (looking 30 degrees down tilts it about 27 degrees) and never more than 45 degrees, hence the limit. Level again once you land. 0 = always level. No effect on sledgehammers (their slam hits an area). |
| Roll attack | DamageMultiplier | `1.3` | Damage of a roll attack compared with a normal swing of the same weapon (0.1-5). It also applies to trees and rocks. |
| Roll attack | StaggerMultiplier | `1.5` | How much faster a roll attack fills the target's stagger bar (0-10). |
| Roll attack | PushMultiplier | `1` | Knockback of a roll attack (0-5). |
| Roll attack | StaminaMultiplier | `1` | Stamina of a roll attack's swing, on top of the roll itself (0-5). If you can pay a normal swing but not the roll attack, you get a normal swing. |
| Roll attack | Window | `0.4` | How long (seconds, 0.05-2) after a roll ends a new press of the attack button still makes a roll attack. After a normal roll only while you are still getting up from it (about the first 0.2 seconds), so a roll attack always flows out of the roll: a later press is a normal swing, whatever this value. The whole time counts after a dash from a dash mod (it has no roll animation). A press during the roll (the game remembers a press for half a second) or holding the button makes the roll attack cut into the end of the roll instead (see FlowStart). |
| Roll attack | FlowStart | `0.85` | How long (seconds, 0-2) after the start of a roll an attack can cut into it: from then on a pressed, remembered or held attack ends the roll and the roll attack flows straight out of it, with no stop in between. A normal roll lasts about 0.9 seconds, so the default plays almost all of it and only removes the stand-up; a lower value (for example 0.7) gives a snappier roll attack that cuts the end of the roll short. The roll's invulnerability ends about 0.4 seconds in, and the attack never starts until 0.2 seconds after that, whatever this value, so no other player's game still sees you invulnerable. Longer than the roll = the attack starts as the roll ends, still without the stop. |
| Roll attack | FlowBlend | `0.15` | How long (seconds, 0-0.5) the animation blends from the roll into the roll attack. Only the look changes: the swing hits at the same time. 0 = an instant switch. |
| Jump attack animations | Swords | `swing_longsword2` | Animation of the jump attack for one-handed swords. |
| Jump attack animations | Maces | `swing_longsword2` | Same, for maces and clubs. |
| Jump attack animations | Axes | `swing_axe2` | Same, for one-handed axes. |
| Jump attack animations | Battleaxes | `battleaxe_attack2` | Same, for battleaxes. |
| Jump attack animations | DualAxes | `dualaxes3` | Same, for dual axes (the Berserkir axes, and every Dual Wielding pair except two knives). |
| Jump attack animations | Greatswords | `greatsword2` | Same, for two-handed swords. |
| Jump attack animations | Atgeirs | `atgeir_attack2` | Same, for atgeirs. |
| Jump attack animations | Knives | `knife_stab2` | Same, for knives. |
| Jump attack animations | DualKnives | `dual_knives2` | Same, for dual knives (Skoll and Hati, and Dual Wielding pairs of two knives). |
| Jump attack animations | Spears | `Off` | Same, for spears. Off by default: a single attack animation. |
| Jump attack animations | Fists | `unarmed_attack1` | Same, for bare hands and fist weapons. |
| Jump attack animations | Sledges | `Off` | Same, for sledgehammers. Off by default: a single attack animation. |
| Roll attack animations | Swords | `swing_longsword1` | Animation of the roll attack for one-handed swords. |
| Roll attack animations | Maces | `swing_longsword1` | Same, for maces and clubs. |
| Roll attack animations | Axes | `swing_axe1` | Same, for one-handed axes. |
| Roll attack animations | Battleaxes | `Off` | Same, for battleaxes. Off by default: the battleaxe's other swings hit much sooner than its slow first swing. |
| Roll attack animations | DualAxes | `dualaxes1` | Same, for dual axes. |
| Roll attack animations | Greatswords | `greatsword1` | Same, for two-handed swords. |
| Roll attack animations | Atgeirs | `atgeir_attack1` | Same, for atgeirs. |
| Roll attack animations | Knives | `knife_stab1` | Same, for knives. |
| Roll attack animations | DualKnives | `dual_knives1` | Same, for dual knives. |
| Roll attack animations | Spears | `Off` | Same, for spears. Off by default: a single attack animation. |
| Roll attack animations | Fists | `unarmed_attack1` | Same, for bare hands and fist weapons. |
| Roll attack animations | Sledges | `Off` | Same, for sledgehammers. Off by default: a single attack animation. |

**Animation settings.** Each one is a drop-down: its default first, then `Off` (a normal swing, no bonus), then the
game's melee attack animations: `swing_longsword0`-`2`, `sword_secondary`, `mace_secondary`, `swing_axe0`-`2`,
`axe_secondary`, `battleaxe_attack0`-`2`, `battleaxe_secondary`, `dualaxes0`-`3`, `dualaxes_secondary`,
`greatsword0`-`2`, `greatsword_secondary`, `atgeir_attack0`-`2`, `atgeir_secondary`, `knife_stab0`-`2`,
`knife_secondary`, `dual_knives0`-`2`, `dual_knives_secondary`, `spear_poke`, `unarmed_attack0`-`1`, `unarmed_kick`,
`swing_sledge`. A move keeps the weapon's own reach, hit arc and numbers whatever animation it plays: an animation of
another weapon type can look odd or clip through a shield, and a borrowed kick hits like a punch. A value typed into the
file that is not in the list is replaced by that setting's default when the game starts. If the game ever lacks an
animation (after a game update), the move uses the default instead (or stays a normal swing) and the log says so once.
In multiplayer these are the server's settings: the warning then asks you to tell the server admin, since your own
settings do not apply there.

## Multiplayer

- **Who needs it:** required on the server (or the host) and on every player's game.
- **Multiplayer support:** works in multiplayer.

Install it on the server (or the host) and on every player's game. It changes combat, so the server makes sure every
player fights with the same moves and numbers. On a server without the mod it turns itself off.

- **Players who cannot play by the server's moves are refused** about a second after they join; their game shows
  "Incompatible version". This covers a player without the mod, a player whose version of the mod cannot talk to the
  server's, and a player who has it turned off (Enabled = false, or unticked in the MC Mods panel). A player who turns
  it off while connected is refused about a second later. The server (or host) can let such players in with
  AllowPlayersWithoutMod = true: their own jumps and rolls stay normal, and the server log names each of them.
- **The server's settings apply to everyone** (your game logs "Using the server's move settings"). Until they arrive
  after you join, your attacks are normal swings. Your own settings apply again in single player and when you host.
  When the server's settings change, players get them half a second after the last change (dragging a slider sends
  them once).
- **Every player sees the moves**, with or without the mod: they are the game's own animations. A roll attack flows
  out of the roll on the screens of players with the mod (no extra network message: each game does it on its own); a
  player without the mod sees the roll finish, then the swing.
- **Hits work on everyone.** A move's damage, stagger and knockback are worked out on the attacker's game, so they
  also apply to creatures another player's game controls and, in PvP, to players without the mod.
- **Dedicated servers:** install BepInEx on the server and put the mod in its `BepInEx/plugins` folder; the server's
  config file decides the moves and numbers for everyone.
- Nothing is stored on items, characters or the world.

## Good to know

- **A jump attack needs a jump you make.** Walking or rolling off a ledge, a catapult, a launch or a grappling pull is
  not a jump, so the attack stays normal. If your attack only starts after you land, it is a normal swing.
- **No jump attack from sneak or with block held (keyboard, and gamepad on the Default layout).** The game turns the
  jump press into a roll there, so you get a roll attack instead. On the Alternative gamepad layouts the jump button
  jumps even while crouching or blocking, and the jump attack works.
- **Which way a roll attack faces** follows your own Gameplay option for attack direction, as every first swing does.
  With the keyboard default the strike turns to where the camera looks, so "roll past, strike back" works by looking
  back. With the gamepad default the strike follows the stick: after a roll the stick usually still points along the
  roll, so pull it back toward the enemy to strike back (no stick input: no turn).
- **Press in the second half of the roll, or hold the button.** The game remembers a press for half a second, so a
  press made in the first third of a roll (more than half a second before the roll attack can cut in) is lost, as in
  vanilla. A press after the roll counts only while you are still getting up from it; once you stand, it is a normal
  swing.
- **Only roll attacks flow out of the roll.** Every other attack after a roll stays as in the base game: a secondary
  attack, a weapon type whose roll attack is off (battleaxes, spears, sledgehammers by default), RollAttack turned off,
  the cooldown still running, or a normal swing because you lack stamina for the roll attack. Those start only once the
  roll is over, with the usual stand-up in between.
- **Even the first roll attack flows.** The game does not tell mods which animation an attack plays, so the first
  time you roll with a weapon type the mod asks a hidden copy of the character's animation set (a few milliseconds,
  once per weapon type and game session) and your roll attack flows out of the roll from the start. If that ever fails
  (after a game update, or with a mod that swaps the animations), a roll attack plays after the roll's stand-up, as in
  the base game, until the mod has seen that animation play once (any combo that uses it shows it), and flows from then
  on.
- **The roll attack comes a little sooner than roll-then-attack in the base game,** since the stand-up is gone: its
  swing starts about 0.85 seconds into the roll instead of about 1.1 seconds. Lower FlowStart (for example to 0.7) for a
  snappier roll attack that also cuts the end of the roll short (the roll may then carry you a little less far); you
  are vulnerable from about 0.4 seconds into the roll either way.
- **The finisher bonus.** A normal combo's last swing hits twice as hard; a jump attack that plays the finisher uses its
  own ×1.2 instead. A roll attack is followed by the real finisher.
- **The damage bonus also hits trees and rocks**, like the weapon's normal swings.
- **No moves in water**, or while sitting, riding or steering a ship.
- **Stamina does not come back during a jump.** If a jump leaves you too little stamina for a swing, there is no attack
  in that jump (the stamina bar flashes, as in vanilla).
- **Adrenaline** comes from a move's hits like from any swing, and the higher stagger multiplier staggers enemies
  sooner, which also gives the stagger adrenaline sooner.
- **Dash mods and the cooldown.** With a mod that turns the roll into a quick dash, roll attacks come much sooner (an
  attack right after the dash; a dash is never cut into); the 1-second cooldown still spaces moves out. Raise **Cooldown** if moves come too often for your taste (in multiplayer,
  the server's Cooldown applies).
- **A custom animation that does not play** (no way into it from a jump or the end of a roll) is cancelled after half a
  second, a normal swing follows, and the log says: "The jump attack animation ... did not start ... Pick another
  animation from the list in the "Jump attack animations" settings." Pick another animation for that weapon type. In
  multiplayer the animation comes from the server's settings, so the warning asks you to tell the server admin. A
  dedicated server has no player of its own and never sees this warning: try a custom animation in single player (or
  as the host) before you set it on a server.
- **Turning the mod off** in the MC Mods panel lets a move already playing finish; the next attack is vanilla.
- **On a server with the mod**, turning it off on your own game (or joining with it off) gets you disconnected about a
  second later, unless the server allows players without it.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is
packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. It stores nothing of its own in the world, on characters or on items, so it is safe to add
or remove at any time.

- **Dual Wielding** (MC): a pair of two knives uses the dual knives rows above, and every other pair (two axes, two
  swords, two maces, or a mixed pair) uses the dual axes rows, because Dual Wielding gives all of them the Berserkir
  axes' moves (its PairMoves setting; with another template there, the rows of that template's weapon type apply).
  Dual Wielding decides which hand strikes in each swing (the dual axes jump attack hits main hand then off hand; the
  roll attacks strike with the off hand), and the combo carries on through the pair's own four-swing axe combo. The
  move's multipliers stack with Dual Wielding's.
- **Tower Shield Wall** (MC): the tower shield bash is never a move, even right after a jump or a roll: it keeps its
  own animation (a shield punch by default), speed, stagger and cooldown.
- **Sneak Ambush** (MC): the Smoke Screen throw stays a plain throw. A sneak roll (crouch + jump) into a roll attack can
  backstab an unaware creature and counts like any backstab. A jump ends crouching and makes noise.
- **Creature Morale** (MC): a move's hit counts like any other hit: a creature that is afraid of you fights back when a
  jump or roll attack hits it. An afraid creature that can see you takes no backstab (Creature Morale's rule), whatever
  the attack; one that cannot see you (you are behind it, out of its sight, or in smoke) still does.
- **Crossbow Stays Loaded** (MC) and **Harpoon Hooks Tames** (MC): crossbows and the harpoon are never moves, so both
  work as before.
- **Other MC mods** (Forge Idol Upgrades, Creature Kill and Tame Counts, …): no overlap; a kill by a move counts like
  any other kill with that weapon.
- **Goo's Combat Overhaul** has its own jump attack: when it is installed, this mod's jump attack stays off (the log
  says so) and the roll attack still works. An attack in the air after a jump is left to it, even right after a roll.
- **Quickstep**, the **SecondaryAttacks** quickstep and **SpecialAttack** dashes count as rolls: an attack right after
  a dash is a roll attack. A dash has no roll animation to flow out of, so the attack starts after it.
- **Cancel Animation Cancels**: with bare hands, spears, axes or polearms, a roll attack pressed during a keyboard roll
  (block + jump) while block is still held may be lost. Press after the roll, or release block first.
- **Attack-cancel mods** (AttackCancel, AttackCancleCounter, "Cancel Attack to Roll-Dodge"): an attack cancelled into a
  roll is followed by a roll attack, with its bonus.
- **SDW DualWield**: the default animations work like its normal combo. Animations picked from another weapon type may
  show the wrong animation set. When it swaps in an animation set that lacks the roll attack's animation, that roll
  attack plays after the roll instead of flowing out of it.
- **Weapons with their own animations** (Therzie's Warfare and other mods that use custom attack animations) stay
  vanilla: the mod only knows the game's own weapon types. Modded weapons that use the game's own animations get the
  moves of that weapon type.
- **Mods that change attack speed or swap animations by attack name** may treat a move like the swing whose name it
  borrows. Sword Heavy Slash and ChainAttacks change the same swings: not tested together.
- These combinations have not been tested in game yet.
