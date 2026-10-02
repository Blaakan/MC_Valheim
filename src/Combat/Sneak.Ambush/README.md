# Sneak Ambush

Sneak attacks train your Sneak skill, sneaking is a little easier early on, holding still while crouched hides you
much better, fog hides you and the game shows when bushes and fog cover you, and a craftable Smoke Screen hides you
from creatures and makes pursuers lose you.

## Features

### Sneak attacks give Sneak XP

A sneak attack is the normal game's backstab: a hit on a creature that has not noticed you (the backstab effect plays
and the hit does extra damage). The normal game gives no XP for it; with this mod it raises your Sneak skill, and
**"Sneak attack! +N% Sneak"** shows at the top left, N being the progress gained toward your next Sneak level (after a
level-up only "Sneak attack!", next to the game's own level-up message).

- **How much:** 3 XP, plus 10 for every 100 base health of the creature (its stars count, the world level does not),
  the health part capped at 3 times. Sneak attacks with arrows, bolts and thrown weapons pay half. The Rested bonus and
  the world's skill-gain setting apply as for any skill. 1 XP is what one second of sneaking near unaware creatures
  gives in the normal game.
- **How often:** a creature pays sneak-attack XP at most once every 5 minutes, whoever hits it. Tamed creatures and
  training dummies never pay.

Examples (creatures without a star; the share of a level is for a melee sneak attack, not Rested):

| Creature | Sneak XP and share of a level |
|---|---|
| Neck | 3.5 melee, 1.75 ranged; 22% of a level at Sneak 5, 4% at Sneak 20 |
| Boar, Deer, Hare, Hen | 4 melee, 2 ranged; 25% of a level at Sneak 5, 4% at Sneak 20 |
| Greyling | 5 melee, 2.5 ranged; 32% of a level at Sneak 5, 5% at Sneak 20 |
| Greydwarf, Skeleton | 7 melee, 3.5 ranged; 45% of a level at Sneak 5, 7% at Sneak 20 |
| Draugr | 13 melee, 6.5 ranged; 83% of a level at Sneak 5, 13% at Sneak 20 |
| Troll | 33 melee, 16.5 ranged; a full level at Sneak 5, 34% at Sneak 20 |

### Stealth

- **A little easier early on, with the same end-game power.** At Sneak 0, creatures see you sneaking at 85% of their
  normal distance (a Greydwarf in full daylight: 25.5 m instead of 30 m). The help shrinks as your Sneak rises and is
  gone at Sneak 100, where sneaking is exactly the normal game. The stealth bar shows it.
- **Holding still hides you much better.** Crouch and do not move: after 1 second a **Holding still -70%** icon
  appears and creatures see you at 30% of the distance they otherwise would; the stealth bar sinks over 2-3 seconds. A
  Greydwarf that notices a crawling Sneak-0 player in daylight from 25.5 m must come within 7.7 m of you when you hold
  still (3.8 m at night), so one wandering past 8-10 m away walks on. Turning the camera is fine. Walking, standing
  up, jumping, being hit or being carried (a moving ship or cart) ends it at once: the icon goes and the bar jumps back
  up. Holding still gives no Sneak XP, as in the normal game.
- **Fog hides you.** Crouched outdoors in fog, creatures see you at up to 30% less distance, depending on how thick
  the fog is. A **Fog -N%** icon shows the bonus of the moment. The fog comes from the weather and the time of day (not
  from your graphics settings); approximate bonuses per weather are in the list below.
- **Bushes and trees show their cover.** In the normal game, bushes and tree crowns already block a creature's line of
  sight and shade you from the sun and moon (in daylight your stealth bar drops when you crouch in or under them), but
  nothing tells you. Now an **In foliage** icon shows it while you crouch within half a metre of a bush or low
  branches, or under a tree crown. It adds no number of its own (the server can add one with FoliageBonus). The plants
  that hide you are bushes (plain, raspberry, blueberry and lingonberry bushes) and shrubs, and trees such as beech,
  birch, oak, fir and pine. Grass does not hide you.
- **Mistlands mist.** Crouched in the Mistlands mist, an **In the mist** icon reminds you that creatures without mist
  sight cannot see you from more than 10 m away (the normal game's rule).
- **Status icons.** Holding still, In foliage, Fog, In the mist (all while crouched) and In smoke sit with your other
  status icons; the Active effects page of the compendium (open it from the inventory) explains each one with the
  current numbers. This mod's bonuses never take your visibility below 10% (setting VisibilityFloor); when you reach
  that floor, the icons that show a number read "max". Each player can hide the icons (ShowStealthCues); the bonuses
  stay.

Fog bonus per weather (approximate):

| Weather (biome) | Fog bonus |
|---|---|
| Clear (most biomes), Snow (Mountain) | none |
| Misty (Meadows, Black Forest, Plains) | 4% by day, 30% at dawn, dusk and night |
| Heath clear (Plains) | none by day and evening, 9% in the morning, 4% at night |
| Deep forest mist (Black Forest) | none by day, 4% otherwise |
| Rain, thunderstorm, light rain | 9% |
| Swamp weathers | 4-9% (the Swamp is always a little foggy) |
| Snowstorm (Mountain) | 17% |
| Mistlands weathers | 4-17%, plus the mist rule |
| Ashlands weathers | 0-17% |
| Deep North weathers | 0-26% |
| Boss arenas | 0-17% |
| Dungeons and caves | none (their darkness already hides you) |

### Smoke Screen, a new item

Crafted at the workbench (level 1): 2 Resin + 1 Coal + 1 Leather scraps make 2 Smoke Screens. It looks and throws like
the game's Smoke Bomb. Where it lands (the ground, a creature, or the surface of water) it raises a grey cloud 8 m wide
and 4 m high. The smoke builds up for half a second and hides you until 15 seconds after the impact; it starts
thinning a little before that and is gone a few seconds later. It does no damage, chokes nobody, puts out no fire and
makes no noise that alerts creatures; a creature it hits stays unaware.

- **Hidden both ways.** Inside the cloud, creatures outside cannot see or hear you; outside it, creatures inside cannot
  see or hear you. A cloud between you and a creature also blocks its sight. You and a creature inside the same cloud
  notice each other only within 2.5 m. An **In smoke** icon shows while you are inside.
- **A creature you hit sees you** through the smoke for 8 seconds after each hit landed while a smoke cloud is nearby
  (any hit that carries damage, fire or poison included), so it fights you. Only that creature: the others still
  cannot see you.
- **The burst blinds pursuers.** When the smoke is up, every creature chasing a player within 5 m of the cloud cannot
  see or hear any player for about 6 seconds (except one who hits it). A creature that cannot sense its target because
  of the smoke gives up the chase after 3 seconds, as when it loses you in the normal game: its alert icon goes off, it
  walks back, and it only looks for players again 5 seconds later. Raids and other creatures that hunt you in the
  normal game never give up (see Good to know), and an arrow that lands near a pursuer starts its 3 seconds again.
- **Health bars:** while you are outside a cloud, you do not see the health bars of creatures inside it (setting
  HealthBars). Your tames keep theirs.
- **Bosses are not fooled:** they see you through the smoke, are never blinded and keep their health bar.
- **Players only, creatures only:** the smoke hides players (not your tames), and only from creatures: ballistas still
  shoot you.

### Turning it off

Turn it off in the **MC Mods** panel at any time: sneak-attack XP and smoke work like the normal game again at once,
and your stealth follows the normal game again (the stealth bar climbs back to its normal value over a few seconds,
as it always does). The recipe is hidden and Smoke Screens cannot be thrown (a message says why). Smoke Screens stay
in your inventory and chests.

## Configuration

The config file `BepInEx/config/MC.Combat.Sneak.Ambush.cfg` is created the first time you launch the game with the mod.
To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration manager installed
that button is hidden: press F1 and use its window). Every setting can be changed in-game with ConfigurationManager, or
by editing the file (the game picks up the change while it runs): recipe settings rebuild the recipe, the cloud's size
and duration apply to the next throw, the rest applies at once. In multiplayer the server's (or host's) settings are
used for everyone, except ShowSneakAttackMessage and ShowStealthCues (each player's own choice); AllowPlayersWithoutMod
is read only by the server.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. Smoke Screens stay in your inventory while it is off. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | Server (or host) only. Off: a player whose game does not have this mod, has it turned off or has another version of it is refused about a second after joining, or about a second after turning it off (their game shows "Incompatible version"). On: they may play; creatures their game controls ignore smoke and give nobody Sneak XP for sneak attacks, their stealth follows the normal game, and a player without the mod loses any Smoke Screen that reaches their inventory. |
| Sneak attacks | SneakAttackXpFlat | `3` | Sneak XP every sneak attack gives, before the bonus for the creature's health (0-50). Set this and SneakAttackXp to 0 to turn sneak-attack XP off. |
| Sneak attacks | SneakAttackXp | `10` | Extra Sneak XP for a creature with ReferenceHealth health. Bigger creatures pay more, up to MaxHealthScale times this; small ones less. Stars count, world level does not (0-50). |
| Sneak attacks | ReferenceHealth | `100` | Health of a creature that pays exactly SneakAttackXp extra (10-2000). |
| Sneak attacks | MaxHealthScale | `3` | Cap on the health bonus: a creature pays at most this many times SneakAttackXp extra (0.1-10). |
| Sneak attacks | RangedXpFactor | `0.5` | Share of the XP for sneak attacks with arrows, bolts and thrown weapons (0-1; 0.5 = half). |
| Sneak attacks | SneakAttackXpCooldown | `300` | A creature pays sneak-attack XP at most once in this many seconds, whoever hits it (0-3600). A sneak attack counts even when the attacker's game pays nothing for it (for example because another mod pays Sneak XP there instead). |
| Sneak attacks | PayAlongsideOtherSneakXpMods | `false` | Off: when SecondaryAttacks or SmartSkills is installed on a player's game, they pay that player's sneak-attack XP and this mod does not. On: both pay. |
| Sneak attacks | ShowSneakAttackMessage | `true` | Show "Sneak attack!" and the Sneak progress gained at the top left of the screen. Each player's own choice. |
| Stealth | EarlyGameBonus | `15` | How much harder you are to see while sneaking at Sneak 0, in percent (0-25). The bonus shrinks evenly as Sneak rises and is gone at Sneak 100. |
| Stealth | StillBonus | `70` | Crouched and not moving: creatures see you at this much less distance, in percent (0-90; 70 = they must come three times closer). |
| Stealth | StillDelay | `1` | How long you must hold still before the holding-still bonus starts, in seconds (0-5). |
| Stealth | StillEndsAtOnce | `true` | On: moving ends the holding-still bonus immediately. Off: it fades out at the normal speed of the stealth bar. |
| Stealth | FoliageBonus | `0` | Extra bonus while crouched touching a bush or low branches, in percent (0-90). Foliage already blocks sight and shades you in the normal game, which the In foliage icon shows; 0 adds nothing to that. |
| Stealth | FoliageReach | `0.5` | How close to foliage your body must be to count as touching it, in metres (0-1.5). |
| Stealth | FogBonus | `30` | Bonus in the thickest fog, outdoors, in percent (0-90): creatures see you at this much less distance. Thinner fog gives less. |
| Stealth | FogDensityNoBonus | `0.01` | Fog density at or below which fog gives nothing (0-0.2). A clear night is 0.01. |
| Stealth | FogDensityFullBonus | `0.08` | Fog density at or above which fog gives the full bonus (0.01-0.3). Misty dawn, dusk and night are thicker than this. If it is not above FogDensityNoBonus, any fog thicker than FogDensityNoBonus gives the full bonus. |
| Stealth | VisibilityFloor | `0.1` | This mod's bonuses never take your visibility below this (0-0.5; 1 = fully visible, 0.1 = creatures see you at a tenth of the normal distance). |
| Stealth | ShowStealthCues | `true` | Show status icons for holding still, foliage, fog, mist and smoke. The bonuses apply either way. Each player's own choice. |
| Smoke Screen | RecipeResources | `Resin:2,Coal:1,LeatherScraps:1` | Materials to craft Smoke Screens: comma-separated Name:amount pairs, with prefab names as used by the spawn command. Unknown names are skipped (the log says which). |
| Smoke Screen | RecipeAmount | `2` | Smoke Screens per craft (1-50). |
| Smoke Screen | RecipeStation | `piece_workbench` | Crafting station (prefab name, as used by the spawn command). Empty: craft anywhere. A name that is not a crafting station hides the recipe (the log says so). |
| Smoke Screen | RecipeStationLevel | `1` | Station level needed (1-10). |
| Smoke Screen | CloudRadius | `4` | Radius of the smoke cloud, in metres (2-10). Applies to the next throw. |
| Smoke Screen | CloudHeight | `4` | Height of the smoke cloud, in metres (2-10). Applies to the next throw. |
| Smoke Screen | CloudDuration | `15` | How long the cloud lasts, in seconds, counted from the impact (3-60): it hides you from the end of ActivationDelay until then, and then fades. Applies to the next throw. |
| Smoke Screen | ActivationDelay | `0.5` | Time for the smoke to build up after the impact before it hides anyone, in seconds (0-3). It is part of CloudDuration, and is shortened when needed so that a cloud always hides for at least 1 second. |
| Smoke Screen | InsideSightRange | `2.5` | Inside the same cloud, you and creatures notice each other only this close, in metres (0-10). |
| Smoke Screen | BlocksLineOfSight | `true` | Creatures cannot see you through a cloud even when neither of you is inside it. |
| Smoke Screen | BlocksHearing | `true` | Creatures cannot hear you through the smoke either. |
| Smoke Screen | RevealSeconds | `8` | A creature you hit while a smoke cloud is nearby can see you through smoke for this many seconds; each hit starts it again (0-60). |
| Smoke Screen | BlindMargin | `5` | Creatures chasing a player who is within this many metres of the cloud are blinded by the burst (0-20). |
| Smoke Screen | BlindSeconds | `6` | How long the burst blinds them, in seconds, counted from the impact: they cannot see or hear any player, except one who hits them (0-30). The blind never lasts longer than the cloud and its fade (CloudDuration + 3 seconds). |
| Smoke Screen | ForgetSeconds | `3` | A creature that cannot sense its target because of the smoke gives up the chase after this many seconds (0-30). Creatures that hunt you in the normal game (raids and other hunters) never give up; an arrow landing near a pursuer starts its count again. |
| Smoke Screen | HealthBars | `OnlyInside` | `OnlyInside`: you do not see the health bars of creatures inside a smoke cloud while you are outside it. `ThroughSmoke`: bars are hidden through smoke either way (also creatures outside while you are inside, and through a cloud between you). `Off`: normal game. Bosses and tamed creatures always keep their bar. |

## Multiplayer

- **Who needs it:** required on the server (or the host) and on every player's game.
- **Multiplayer support:** works in multiplayer.

Install it on the server (or the host) and on every player's game. It changes combat for everyone, so the server
refuses players who do not have the mod, have it turned off or have another version of it (their game shows
Incompatible version), unless its AllowPlayersWithoutMod setting is on, and the settings of the server (or host) apply
to everyone.

- **Refused players:** about a second after joining, a player whose game does not have the mod, has it turned off or
  has another version of it goes back to the menu with "Incompatible version"; the server log names them and the
  reason. Turning the mod off in the MC Mods panel while connected gets you refused the same way about a second later
  (turning it off and on again within that second is fine). The server (or host) can let such players in with
  AllowPlayersWithoutMod = true.
- **The server's settings apply to everyone** (your game logs "Using the server's rules"); your own settings apply
  again in single player and when you host. ShowStealthCues and ShowSneakAttackMessage stay your own. Until the
  server's settings arrive (normally before your character appears), the mod changes nothing and a throw says "Smoke
  Screen: waiting for the server's Sneak Ambush settings."
- **Each game runs the smoke rule for the creatures it controls** (the game of a player near them), with the server's
  settings. A dedicated server never controls creatures: players' games always do.
- **A cloud outlives its thrower:** when the thrower leaves, teleports, dies or disconnects, another player's game near
  the cloud takes it over, and it keeps hiding the players in it until its normal end.
- **On a server without the mod** the mod turns itself off on your game (the MC Mods panel says why): the recipe is
  hidden and throws are refused, but your Smoke Screens stay in your inventory. In a game hosted by a player without
  the mod, a Smoke Screen dropped on the ground is deleted once the host comes near it; a dedicated server without the
  mod keeps it.
- **Players without the mod** (only possible with AllowPlayersWithoutMod on, which also lets in players with the mod
  turned off): creatures their game controls ignore the smoke, and sneak attacks on those creatures give nobody Sneak
  XP, not even players with the mod. Their own sneak attacks give them no Sneak XP. A player without the mod sees no
  cloud (their log shows "Missing prefab hash" warnings while they are near one); a Smoke Screen that reaches their
  inventory is deleted, and when they take or add an item in a chest that holds Smoke Screens, those Smoke Screens are
  gone for everyone. Keep Smoke Screens away from them.
- **Dedicated servers:** install BepInEx on the server and put the mod in its `BepInEx/plugins` folder; the server's
  config file decides the rules and it checks who joins. The server does not run creatures or smoke itself (the
  players' games do). It keeps the Smoke Screen known even with Enabled = false, so Smoke Screens and clouds saved in
  the world load without "unknown prefab" warnings.

## Good to know

- **One sneak attack per creature every 5 minutes** is the normal game's rule for backstabs: the first hit alerts the
  creature, and the next backstab on it needs it to be unaware again. A creature that gave up the chase after a smoke
  burst is unaware again, so it can be sneak attacked again once those 5 minutes are over.
- **Holding still ends** when you walk, stand up, jump, are hit, or are carried by a ship or cart. Raising a shield
  ends crouching in the normal game, so it ends holding still too.
- **Standing up** raises your head above low bushes: creatures look at your eyes instead of your chest.
- **Sleeping creatures still wake up** when you come close (a sleeping Draugr within 10 m), smoke or not, as in the
  normal game. Once awake, they cannot find you while you stay hidden.
- **Raids:** creatures sent by a raid (and other creatures that hunt you in the normal game) always know where you
  are. The smoke stops them seeing and attacking you, but they keep coming, never give up the chase (the game keeps
  them alert), and inside the cloud they find you within 2.5 m.
- **Do not wait where the smoke landed.** Pursuers first run to where they last sensed you; once the blind is over, one
  that is still in the cloud can find you within 2.5 m. Moving off inside the cloud is the safer play.
- **Noise still travels:** an arrow or bolt that lands near creatures makes them come and look where it was shot from
  (normal game). A creature you set on fire from the smoke loses sight of you 8 seconds after your last direct hit
  (the burn does not count as a hit).
- **A Smoke Screen that hits a creature counts as your hit**, like a harpoon: if a teammate kills that creature soon
  after, you get the kill too.
- **The game's own Smoke Bomb is unchanged:** it does not hide you.
- **Your stealth bonuses count everywhere**, also for creatures a game without the mod controls: they are part of your
  stealth value, which every game reads.
- **Turning the mod off** keeps your Smoke Screens (they cannot be thrown while it is off). Clouds already in the world
  keep their smoke until they end, but no longer hide anyone.
- **Before removing the mod**, use or drop your Smoke Screens: without the mod, the game deletes them from your
  inventory, and from a chest the next time someone takes or adds an item in it.
- **Health bars come back** after a creature leaves the smoke once you aim at it again (in the normal game a creature's
  bar shows for 60 seconds after you aimed at it, within 30 m).

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. It adds the Smoke Screen item, its thrown projectile and its cloud to the game, and keeps on
each creature that paid sneak-attack XP the time of that payout. Safe to add at any time; before removing it, see
"Before removing the mod" above.

- **Creature Morale** (MC): a creature that is afraid of you and can see you takes no sneak attack (its rule), so it
  pays no Sneak XP, and one that runs from you is alert, so it takes none either. One that cannot see you (you are
  behind it, out of its sight while you hold still, or in the smoke) takes a normal sneak attack and pays. An afraid
  creature runs only from a player it can see or hear, so holding still and the smoke let you get close: inside the
  smoke you can walk right up to it. A hit from inside the smoke makes an afraid creature fight back and lets it see
  you, so it fights you through the smoke; the other afraid creatures still cannot see you. Standing in the smoke next
  to an afraid creature that has not noticed you never counts as cornering it. Crawling next to afraid creatures
  raises Sneak only slowly. A smoke burst makes creatures chasing you forget you; a routing pack is not affected.
- **Tower Shield Wall** (MC): raising the tower shield ends crouching (normal game), so it ends holding still. The
  shield bash is never a sneak attack and pays no Sneak XP, and the creature it hits is alerted, so your next hit on it
  is not a sneak attack either (also in New Game+ worlds). From inside the smoke, a creature you bash sees you and
  fights you.
- **Dual Wielding** (MC): a Smoke Screen is never part of a pair: equipping it puts the pair away (the normal
  one-handed rule). A pair's sneak attack uses the main-hand weapon's sneak-attack bonus and pays XP once.
- **Weapon Moveset** (MC): the Smoke Screen throw stays a plain throw. A sneak roll into a roll attack on an unaware
  creature is a sneak attack and pays XP like any other (with Creature Morale, not on an afraid creature that sees
  you).
- **Crossbow Stays Loaded** (MC): a crossbow sneak attack counts as ranged and pays half XP.
- **Harpoon Hooks Tames** (MC): hooking a tame never pays Sneak XP (tames never pay).
- **Encyclopedia** (MC): a creature you only ever saw inside the smoke (bar hidden) counts as met once its health bar
  shows.
- **Creature Kill and Tame Counts** (MC): a Smoke Screen that hits a creature counts as your hit, so you also get the
  kill when a teammate finishes it.
- **Forge Idol Upgrades** (MC): both add their own items and recipes; no overlap.
- **Sort Chest** and **Crafting Search and Sort** (MC): the Smoke Screen sorts as a weapon, like the game's bombs.
  **Loot Pickup Filter** (MC) lists it like any item.
- **Switchable Lights** (MC): lights you switch off make the area darker, which helps sneaking (normal light rule).
- **SecondaryAttacks** and **SmartSkills** pay Sneak XP for sneak attacks too: when one of them is installed on a
  player's game, this mod pays that player none (setting PayAlongsideOtherSneakXpMods), and the log says so.
  SecondaryAttacks also changes how visible sneaking is: set its "Sneak Visibility Skill Effect Factor" to 1 to keep
  this mod's early-game curve; the other bonuses of this mod still apply.
- **Goo's Combat Overhaul** changes sneak damage and XP too, and this mod cannot detect it yet, so both may pay Sneak
  XP: set SneakAttackXpFlat and SneakAttackXp to 0 to keep only its XP.
- **SetUpSkills, ImpactfulSkills, Sneaky Viking, SNEAKer** (sneak skill curve, noise, crouch speed or XP): this mod's
  bonuses apply on top of theirs.
- **Mods that replace the enemy health bars** may still show bars through the smoke.
- **Fog removers and view-distance mods** do not change the fog bonus (it comes from the weather, not the picture).
- **Item mods** (Jotunn, ItemManager): no conflict; the Smoke Screen has its own `MC_` names.
- These combinations have not been tested in game yet.
