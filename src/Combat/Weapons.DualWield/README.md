# Dual Wielding

Wield a one-handed sword, axe, club or knife in each hand. Equip a second one-handed weapon and it goes to your off
hand. A pair fights with the game's own dual axe moves (the Berserkir axes), or the dual knife moves (Skoll and Hati)
when both weapons are knives. Every hit is struck by one of your two weapons, or by both, with that weapon's own
damage, effects, skill and wear.

## Features

- **Any mix of one-handed weapons.** Swords, axes, clubs and maces, and knives pair in any mix: sword + axe, knife +
  mace, two different swords, even two copies of the same weapon. Spears, the butcher knife (it only hurts tames),
  tankards and bombs never pair. Modded one-handed weapons of these kinds pair too, unless they are in
  **ExcludedWeapons**.
- **Automatic pairing.** With a one-handed sword, axe, club or knife in your hand, equip another one (hotbar,
  inventory right-click or radial menu): it goes to your left hand and your first weapon stays in your right hand. Equip a third one: it
  replaces the off-hand weapon. Both weapons show as equipped, and you switch to the dual stance. The first time you
  pair in a session, a message on screen names the two keys below.
- **Main-hand key** (Left Alt by default). Hold it while equipping a one-handed weapon to equip it the normal game way:
  it replaces your main weapon, and a weapon in your off hand is put away.
- **Swap key** (H by default). Press it to swap your two weapons between your hands. The swap is an equip like any
  other: the game's equip animation plays for as long as equipping a weapon takes (0.2 s for the game's one-handed
  weapons, with the "Equipping" bar), then your weapons change hands with the draw animation of the hide key (R). You
  cannot attack meanwhile: hold the attack button and the swing starts as soon as the swap and its draw animation
  allow it (a single click during the swap may start the swing late, or be lost). The swap does not start while you
  sprint (a message says so), and a dodge, a jump or a sprint cancels it, as it cancels any equip. A message names
  your new main weapon, and your next attack starts the combo from its first swing. Swaps in a row come at most every
  half second (the game's own pause between two equips), and never in the middle of an attack (nor in the moment one
  starts).
- **Shields and torches work as before.** Equip a shield while dual wielding: the shield replaces the off-hand weapon and
  the main weapon stays. Equip a weapon while holding a shield: it replaces your main weapon and the shield stays (no
  pair). A torch also replaces the off-hand weapon and the main weapon stays. Anything else (a two-handed weapon, a bow,
  a tool, a spear, a bomb) puts both weapons away, as the game always does.
- **The game's own dual moves.** Every pair uses the stance, four-swing combo and special attack (the cleave) of the
  Berserkir axes; a pair of two knives uses the fast three-stab combo and the leap of Skoll and Hati. The moves, reach
  and multipliers are read from those two items in your game, so they follow the game's own values.
- **Both weapons fight.** With the default **HitPattern**, each hit of a dual attack comes from one weapon:

  | Moves | 1st swing | 2nd swing | 3rd swing | 4th swing (double damage) | Special attack |
  |---|---|---|---|---|---|
  | Dual axe moves (every pair but two knives) | main hand | off hand | main, then off hand | main, then off hand | both weapons at once |
  | Dual knife moves (two knives) | main hand | off hand | both knives (double damage) | | both knives at once |

  Each hit is entirely the striking weapon's: its damage and damage types (a frost sword's frost, a poison axe's
  poison), its status effects and special effects (the Blood weapons' bonus at low health, the Lightning weapons'
  strikes), its backstab bonus, its skill experience and its durability loss. When both weapons strike at once, each
  deals half its damage (setting **BothHandsDamage**), so the blow is worth one weapon's hit, like the game's own dual
  weapons. Set **HitPattern** to BothHands to have both weapons strike on every hit instead. If **PairMoves** names an
  item that is not a dual weapon (a sword, for example), the swings of its combo alternate between your weapons (main
  hand first) and its special attack strikes with both.
- **Balanced like the game's own dual weapons.** The game's dual weapons hit about as hard per hit as a one-handed
  weapon of their tier and get their edge from more hits per combo. With two weapons of the same tier and the default
  settings, a pair plays the same way: with the dual axe moves its combo deals about 50% more damage per stamina than
  one of its weapons alone (the same gain as the Berserkir axes), two knives deal what Skoll and Hati deal, the special
  attack deals and costs the same as the special attack of the game's dual weapon, and you have no shield.
- **Stamina.** A dual attack costs the higher normal attack cost of your two weapons (so a cheap weapon in the main hand
  never pays for a strong one in the off hand). The special attack costs 2 times that with the dual axe moves and 3
  times with the dual knife moves, like the game's own dual weapons. Your main weapon's skill, your equipment and your
  status effects still lower it as usual.
- **Blocking and parrying** use your off-hand weapon (the game's own rule for anything in the left hand): its block
  power and parry bonus. Swap hands to block with the other weapon. A knife in the off hand blocks badly, like a single
  knife.
- **Skills.** Each hit trains the skill of the weapon that struck it: a sword + axe pair trains both Swords and Axes.
  When both weapons strike at once, each gets half.
- **The pair holds.** Your weapons stay in the hands you chose through everything the game does to your hands: hiding
  and drawing them (R), eating (also several foods in a row, or when you roll, attack or dive right after), crafting
  stations, beds, the barber, swimming, logging out and in, dying in a world where you keep your equipment, moving
  items in your inventory, and the hammer of the radial menu. If you put the main weapon away (unequip it, drop it,
  move it to a chest), the off-hand weapon moves to your main hand.
- **Off-hand swing trails.** The game draws a swing trail only on the right-hand weapon; your off-hand weapon gets one
  too (setting **LeftHandTrails**), except on your main weapon's own special attack (SecondaryMoves = MainWeapon),
  which the off-hand weapon does not strike.
- **Sheathed pair by weapon kind.** When you put a pair away (hide key, crafting station, bed, swimming), the game
  hangs swords, axes and maces on your back and knives at your right hip, but two weapons of the same kind land on the
  same spot and overlap. With the mod, where a weapon is holstered depends on the weapon:
  - two swords, axes or maces (in any mix) cross in an X on your back, hilts over both shoulders. The X moves with
    your back but stays level: both weapons keep the same angle either side of upright while you stand, walk or run;
  - two knives hang one on each hip: your main-hand knife where the game puts a knife (right hip), your off-hand knife
    in the mirrored spot on your left hip. Both move with your hips like knives on a belt;
  - a knife with a sword, axe or mace (in either hand): each where the game puts it, the knife at your hip and the
    other weapon on your back.

  A single weapon is sheathed as in the game. You see every player's sheathed pair this way (setting
  **CrossSheathedPair**).
- Turn it off in the **MC Mods** panel at any time: your off-hand weapon goes back to your inventory and everything
  works as in the vanilla game, no restart needed.

## Configuration

The config file `BepInEx/config/MC.Combat.Weapons.DualWield.cfg` is created the first time you launch the game with
the mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods**. Every setting can be changed
in-game with ConfigurationManager, or by editing the file (the game picks up the change while it runs). In
multiplayer the server's (or host's) Combat and Moves settings are used for everyone; the Controls and Visuals
settings are each player's own.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | Server (or host) only. Off: a player whose game does not run this mod (not installed, turned off, or a version that cannot talk to this one) is refused about a second after joining, or after turning it off while connected; their game shows "Incompatible version" and the server log says why. On: such players may play (with a warning in the server log); they play without this mod's rules (a player running another dual wield mod may still dual wield with that mod), and they see other players' pairs normally. |
| Controls | MainHandKey | `LeftAlt` | Hold this key while you equip a one-handed weapon (hotbar, inventory or radial menu) to put it in your main hand the normal game way instead of pairing it. The weapon in your off hand is put away. `None` = off. A keyboard key or mouse button (no gamepad button yet). Each player's own setting. |
| Controls | SwapHandsKey | `H` | Press this key while dual wielding to swap your weapons between your hands. The swap is an equip like any other: it takes as long as equipping a weapon, plays the game's equip animation and then the draw animation of the hide key. You cannot attack meanwhile (hold the attack button to swing as soon as the swap and its draw animation allow). It does not start while you sprint, and a dodge, a jump or a sprint cancels it. Your next attack starts the combo from its first swing. `None` = off. A keyboard key or mouse button (no gamepad button yet). Each player's own setting. |
| Combat | OffHandDamage | `100` | Damage of the hits struck by your off-hand weapon, in percent of that weapon's normal damage (10 to 200). 100 = full damage, like the game's own dual weapons. |
| Combat | HitPattern | `Alternate` | Which weapon strikes each hit of a dual attack. `Alternate`: the hits alternate between your weapons as in the table above (one weapon, the other, then one hit from each), and the special attack and the last knife stab strike with both at once. `BothHands`: both weapons strike on every hit, each dealing BothHandsDamage percent (the feel of other dual wield mods). |
| Combat | BothHandsDamage | `50` | When both weapons strike the same hit, the damage of each, in percent of its normal damage (10 to 100). 50 = together they deal about one weapon's hit. Higher values make pairs much stronger. The off-hand weapon's share is also scaled by OffHandDamage. |
| Combat | SwingStamina | `100` | Stamina of a dual attack, in percent of the higher normal attack cost of your two weapons (25 to 300). The special attack costs 2 times more with the axe moves and 3 times more with the knife moves. |
| Combat | SecondaryMoves | `PairMoves` | Special attack of a pair. `PairMoves`: the cleave of the Berserkir axes, or the leap of Skoll and Hati, struck by both weapons. `MainWeapon`: the normal special attack of your main-hand weapon, struck by that weapon only. A pair has no special attack when its main-hand weapon has none (the wooden club). |
| Combat | ExcludedWeapons | (empty) | One-handed weapons that can never be dual wielded, as comma-separated prefab names (as used by the spawn command, for example `AxeBronze`), for modded weapons that look wrong in the dual moves. A name the game does not know is ignored and named in the log. When a weapon of the pair you hold becomes excluded, your off-hand weapon is put away. |
| Moves | PairMoves | `AxeBerzerkr` | The game item whose moves (stance, attack combo, special attack, reach and damage multipliers) a pair uses, unless both weapons are knives. Default: the Berserkir axes. If the item does not exist or its animations are missing, the default is used and the log says why. With an item that is not a dual weapon (a sword, an axe), the swings of its combo alternate between your weapons (main hand first) and its special attack strikes with both. |
| Moves | KnifePairMoves | `KnifeSkollAndHati` | The game item whose moves a pair of two knives uses. Default: Skoll and Hati. Same checks as PairMoves. |
| Visuals | LeftHandTrails | `true` | Show the swing trail on off-hand weapons too, for every player you see dual wielding. Each player's own setting. |
| Visuals | CrossSheathedPair | `true` | Place a sheathed pair (weapons put away with the hide key, at a crafting station, in water...) by weapon kind, for every player you see with one: two swords, axes or maces crossed in an X on the back, two knives one on each hip. A knife with a sword, axe or mace hangs where the game puts each (the knife at the hip, the other weapon on the back) either way. Off: two weapons of the same kind sit on the same spot and overlap, as the game places them. Only what you see changes. Each player's own setting. |

When you enter a world (and again when a moves setting changes or you turn the mod back on), the log names the moves
in use, for example "Pairs use the moves of AxeBerzerkr (dualaxes0-3, special dualaxes_secondary); two knives use
KnifeSkollAndHati (dual_knives0-2, special dual_knives_secondary)."

## Multiplayer

- **Who needs it:** Required on the server and on every client.
- **Multiplayer support:** Works in multiplayer.

Install it on the server (or the host) and on every player's game. It changes combat, so the server refuses players
whose game does not run it, and the server's combat settings apply to everyone. Players see each other's pairs, dual
stance and dual moves.

- **Players whose game does not run the mod are refused** about a second after they join: without the mod, with it
  turned off, with a version that cannot talk to the server's, or with another dual wield mod installed (see
  Compatibility). Their game goes back to the menu with "Incompatible version", and the server log names the player
  and the reason. The server (or host) can let them in with AllowPlayersWithoutMod = true: the server log then warns,
  and they play without this mod's rules (a player running another dual wield mod may still dual wield with that
  mod, by its own rules).
- **Turning the mod off while you play** on such a server puts your off-hand weapon away and disconnects you about a
  second later (unless the server allows players without the mod). Turned back on within that second, you stay.
- **The server's settings apply to everyone** (your log says "Using the server's dual wielding rules: …"): the Combat
  and Moves settings. Your keys and your Visuals settings stay your own, and your own Combat and Moves settings apply
  again in single player and when you host. Until the server's settings arrive (normally before you spawn), your game
  uses the built-in defaults, so a pair you logged out with loads as a pair; if the server excludes one of your two weapons,
  your off-hand weapon is put away the moment its settings arrive.
- **The server changes a setting or turns the mod off:** every player follows at once. A pair that is no longer
  allowed loses its off-hand weapon (put back in the inventory); when the server turns the mod off, every player's
  off-hand weapon goes back to the inventory, and pairs work again when it is back on.
- **On a server without the mod** the mod turns itself off on your game (the MC Mods panel says why): equipping and
  combat are vanilla there, and a pair you logged out with loads as one weapon (the other stays in your inventory).
- **Everyone sees a pair**, even players without the mod (when the server lets them in): both weapons in hand, the dual
  stance, the dual moves and the equip and draw animations of a swap come from the game's own data. Without the mod they see no
  off-hand swing trail, and a sheathed pair of the same kind overlaps on one spot as the game places it; players with
  the mod see it placed by weapon kind (each by their own CrossSheathedPair setting).
- **Hits are normal hits**: creatures and other players (PvP) take, block and parry each weapon's hit as usual, on
  every game. When both weapons strike at once, a blocking player blocks two hits: the damage, push and stagger add
  up to one weapon's hit, but the per-block rewards (Blocking skill, block adrenaline, block charges of shields that
  build them) and a perfect block's stamina cost count twice.
- **Giving away a former off-hand weapon**: it carries a small mark in its item data, which the game without the mod
  keeps and ignores. For anyone it is a normal weapon; back with you, it pairs normally.
- **Dedicated servers:** install BepInEx and the mod on the server; its config file decides the rules. The server only
  runs the join check and sends the settings.

## Good to know

- **Removing the mod, or playing where it is off:** a pair you logged out with loads as one weapon (the one that comes
  later in your inventory), the other stays in your inventory. Nothing is lost.
- **Trees:** the dual axe combo starts again at every tree hit (as with the Berserkir axes), so on a tree every swing
  is the first one, struck by your main-hand weapon. An axe in the main hand chops like a single axe; with a sword or
  mace in the main hand, press the swap key to chop with an off-hand axe. Two knives do not chop.
- **The wooden club** has no special attack of its own, so a pair with the club in the main hand has none either. Swap
  hands to get the cleave.
- **Sneak attacks:** the first hit of the combo is the main hand's, so the main-hand weapon gives the backstab bonus.
  Put a knife in the main hand for the knife's large backstab bonus.
- **Eating:** most food eaten from the inventory leaves both weapons in your hands, as in the game. When the game does
  hide your main weapon for a bite (when the food shows in your hand), the attack button does nothing for that second
  and the stance is a single weapon's; then the main weapon comes back and the pair is whole again.
- **Bombs, tankards and other one-handed items that are not weapons** put both weapons away when you equip them (the
  game's rule for one-handed items). Equip your weapons again afterwards.
- **Both weapons wear**, each by its own hits. A pair weighs and slows you down like both weapons together (a sword and
  an axe slow you about as much as a sword and a round shield), as in the game.
- **Gamepad:** the main-hand key and the swap key are keyboard or mouse only for now. With a controller, a second
  one-handed weapon always pairs; unequip your weapon first to replace it instead.
- **Looks:** the dual moves were made for the game's own dual weapons, so long swords and maces may clip a little in
  some swings. The crossed pair on the back and the mirrored knife on the left hip are worked out from each weapon's
  shape and your character's body; if a (modded) weapon or body looks wrong there, turn CrossSheathedPair off to get
  the game's placement back.
- **Swap timing:** the swap follows the game's equip rules, so it takes the weapon's own equip time (0.2 s for the
  game's one-handed weapons) and cannot run while you sprint (sprinting cancels every equip in the game; the swap key
  tells you so instead of doing nothing). The game has no equip speed bonus of its own; mods that change equip times
  change the swap the same way.
- **Creature weapons** from the spawn command (items a player normally never gets) pair too if they are one-handed
  swords, axes, clubs or knives.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is
packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. The only data it stores is a small mark in the item data of the weapon in your off hand
(the game keeps and ignores it without the mod). Safe to add at any time; after removing it, a pair loads as one
weapon (see Good to know).

- **Other dual wield mods** handle the same equipping and attacks and cannot be used together with this one.
  **Smoothbrain DualWield** (and Goo's Combat Overhaul's dual wielding, which needs it): this mod does not load, and
  the BepInEx log says why. **DualWielder** (RustyMods), **DualWieldCore** (Ketanol) and **DualMastery** (balrond): this
  mod stays off, and the MC Mods panel and the log name the other mod. Either way, a server with this mod refuses that game.
  **Valheim Ascended** has its own off-hand slot: do not use both.
- **Weapon Moveset** (MC): its jump and roll attacks work with pairs, using the dual axe or dual knife animations. With
  its default animations, the dual axe jump attack (the fourth swing) strikes main hand then off hand, the dual knife
  jump attack (the third stab) strikes with both knives, and the roll attacks (the second swing) strike with the off
  hand, flowing straight out of the end of the roll like any roll attack; the combo carries on from there. A pair
  move set to another animation in Weapon Moveset strikes with both weapons. Its multipliers stack with this mod's.
- **Tower Shield Wall** (MC): a tower shield is two-handed, so equipping one puts both weapons of a pair away, and
  equipping a weapon puts the tower shield away. Round shields and bucklers work as described above.
- **Sneak Ambush** (MC): a pair's sneak attack backstabs with the main-hand weapon's bonus. The Smoke Screen is never
  paired: equipping it puts both weapons away, as any bomb does.
- **Creature Morale** (MC): a hit from either hand counts like any hit, so a creature that is afraid of you (it runs
  away) fights back when your off-hand weapon hits it, as with your main weapon. Its sneak rule applies to pairs too:
  an afraid creature that sees you gives no sneak-attack bonus; one that cannot see you still does.
- **Crossbow Stays Loaded** (MC): crossbows are two-handed and never paired; a loaded crossbow stays loaded when you
  switch to a pair and back.
- **Forge Idol Upgrades** (MC): refining your off-hand weapon puts it away; refining your main weapon moves the
  off-hand weapon to your main hand.
- **Creature Kill and Tame Counts** (MC): kills made with a pair count as melee kills, like any sword, axe, club or
  knife kill.
- **Goo's Combat Overhaul** (without Smoothbrain DualWield): it has no dual wielding of its own; its attack tuning may
  or may not apply to pair attacks.
- **EpicLoot**: each hit carries the weapon that struck it, so magic effects that follow the weapon of a hit should
  follow the right hand.
- **Animation mods** that replace the dual axe or dual knife animations change how pairs look too.
- **Inventory and equipment slot mods** (extra slots, loadouts, quick slots): equipping through the game follows the
  rules above. Mods that put items straight into the hands skip them.
- **Other mods using Left Alt or H**: change MainHandKey or SwapHandsKey.
- **Mods that move sheathed weapons** (holster or back-slot mods): this mod places a sheathed pair only when both
  weapons are still on the game's own back spot or both on its hip spot; a pair such a mod moved elsewhere is left
  where that mod put it. If the result looks wrong, set CrossSheathedPair = false.
- These combinations have not been tested in game yet.
