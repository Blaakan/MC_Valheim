# Creature Morale

Weak creatures become afraid of the players who have outgrown them, judged by each player's own boss kills and kill
counts: they never attack such a player and run away when that player comes close, unless the player attacks or
corners them. And when a pack leader falls, the rest of its pack runs away.

## Features

- **Weak creatures are afraid of you.** Once you have helped kill the boss **two bosses after** a creature's biome
  boss, that creature is afraid of you: it never picks you as a target, never attacks you, doesn't start the combat
  music and doesn't stop you from sleeping in a bed or getting Rested. It is not passive either: when you come within
  about 12 m and it can see or hear you, it runs away, and it calms down once you are a few metres farther away (or out
  of its sight for a few seconds). Sneak up on it unseen and quietly and it won't notice you. While it runs, it is
  alert the way the game shows any alert creature (the red "!" above its name), for every player; once it calms down
  the "!" goes away. A creature that is afraid of you but not running looks like any other. For example, the Black
  Forest's greydwarfs are afraid of you once Bonemass **and** Moder are dead; boars and necks once The Elder and
  Bonemass are dead; after Fader ("fully geared from the Ashlands"), every ordinary creature from the Meadows to the
  Plains runs from you.
- **Your progress, not your gear.** Three things count, all read from the game's own records of your character:
  - your **boss rank**: the furthest boss, in the game's order, that you helped kill (you hit it at least once before
    it died): 1 Eikthyr, 2 The Elder, 3 Bonemass, 4 Moder, 5 Yagluth, 6 The Queen, 7 Fader, 8 Kall Fimbulbringer. If
    you skip a boss, the furthest one still counts.
  - your **kills of each kind of creature**: 100 kills of a kind count as one more boss toward that kind, 400 as two
    (setting **KillSteps**). Kills can make a kind afraid of you at most one boss early (**MaxBossesSkippedByKills**);
    beyond that they only make up for stars.
  - each **star** of a creature needs one more boss (**StarRank**): a plain Greydwarf is afraid of you after Moder, a
    2-star one only after The Queen.

  Gear, food and skills do not count. Two players standing side by side can get different treatment from the same
  creature.
- **The biome it spawned in.** Each biome's boss sets the bar: Meadows Eikthyr, Black Forest The Elder, Swamp
  Bonemass, Mountains Moder, Plains Yagluth, Mistlands The Queen, Ashlands Fader, Deep North Kall; the open sea counts
  as the Swamp. A creature counts as a creature of the biome it **spawned in**, or of its home biome if that is harder.
  So skeletons that spawned in the Plains are Plains creatures (afraid of you only after Fader), skeletons in the
  Mountains are Mountain creatures, a greydwarf in the Deep North is a Deep North creature, while the greydwarfs that
  come out at night in the Meadows are still Black Forest greydwarfs, and the night hunters are still Plains Fulings,
  Mistlands Seekers and Ashlands Charred. Elites and pack leaders need **one boss more** than the others of their biome.
- **Who is afraid of you, and when** (default settings, each creature in its home biome, no kill counts):

  | After you helped kill | These creatures are afraid of you |
  |---|---|
  | Bonemass | the Meadows: Boar, Neck, Greyling, Hen, Meadows skeletons |
  | Moder | the Black Forest: Greydwarf, Skeleton, Ghost |
  | Yagluth | Black Forest elites (Greydwarf Shaman, Greydwarf Brute, Troll, Bear); the Swamp: Draugr and Draugr archers, Swamp skeletons, Rancid Remains, Blob, Leech, Surtling, Swamp bats; the Ocean: Serpent |
  | The Queen | Swamp elites (Draugr Elite, Oozer, Wraith, Writhan, Abomination); the Mountains: Wolf, Ulv, Drake, Bat, Frost Blob, Mountain skeletons, Fenring |
  | Fader | Mountain elites (Cultist, Stone Golem); the Plains: Fuling, Fuling archer, Deathsquito, Growth, Lox |
  | Kall Fimbulbringer | Plains elites (Fuling Berserker, Fuling Shaman, Vile); the Mistlands: Seeker, Seeker Brood, Tick |
  | Kall and 100 kills of that kind | Mistlands elites (Seeker Soldier, Gjall); the Ashlands: the Charred (Warrior, Marksman, Twitcher), Asksvin and its hatchlings, Volture, Lava Blob, Bonemaw |
  | never | Ashlands elites (Charred Warlock, Morgen, Fallen Valkyrie) and the whole Deep North |

  A creature in no list always behaves as in the normal game (the Deep North's elites, Hildir's quest bosses,
  creatures added by other mods).
- **Unless you attack or corner it.** A creature that is afraid of you fights back when **you**:
  - **hit it**: any hit that carries damage, including a fire-only or poison-only hit and a blocked hit;
  - **land a projectile close to it**: an arrow, bolt, thrown spear or harpoon that lands within 4 m of it (setting
    **NearMissRange**), even on another target. Farther away it ignores the impact (in the normal game every creature
    within the weapon's noise range is alerted: 8 m for bows and crossbows, 4 m for the Huntsman bow, 30 m for a
    thrown spear or the harpoon). That noise range is also as far as NearMissRange can reach;
  - **corner it**: while it runs from you, you stay within 3 m of it for 2 s: it cannot get away, or you keep up with
    it (**CorneredRange**, **CorneredSeconds**). A creature you keep a few metres away from, or that has not noticed
    you, is never cornered.

  It then stops running and fights you as in the normal game, and is afraid of you again 30 s after the last time you
  attacked it (**ProvokedSeconds**). It is angry at you only: when your friend hits it, it fights your friend, not
  you.
- **A pack runs away when its leader falls.** Kill a Troll, a Greydwarf Shaman or a Greydwarf Brute, and the
  Greydwarfs and Greylings within 25 m of it run away from where it fell for 15 s, whoever killed it and whatever
  anyone's progress, alert (the red "!" above their names) while they run. Then they are afraid of every player
  for 60 s unless someone attacks them, so the pack does not come straight back (**RoutRadius**, **RoutSeconds**,
  **ShakenSeconds**). The rout happens only when a player or a
  tame took part in the kill: a leader that drowns, falls or is killed by other creatures alone does not scare its
  pack. Sleeping followers don't notice, not even when they wake up while the others are still running. An awake
  follower that comes within 25 m of the fallen leader while the others are still running joins them. Hitting a
  running creature does not stop it; your hit still counts when the rout ends. Default packs (setting **Packs**):

  | Leaders | Followers |
  |---|---|
  | Troll, Greydwarf Brute, Greydwarf Shaman | Greydwarfs, Greylings |
  | the Deep North's Greydwarf Shaman | the Deep North's frozen Greydwarfs |
  | Fuling Berserker, Fuling Shaman | Fulings, Fuling archers |
  | Draugr Elite | Draugr, Draugr archers |
  | Cultist | Fenrings, Ulvs |
  | Seeker Soldier | Seekers, Seeker Broods |
  | Charred Warlock | Charred Warriors, Marksmen and Twitchers |

- **Night hunters follow the same rules.** The creatures the game sends at night into the lower biomes after some
  boss kills (Fulings after Yagluth, Seekers after The Queen, Charred after Fader) are afraid of the players who
  outclass them, as creatures of their home biome, and hunt the others (setting **NightHuntersCanBeAfraid**).
- **No free sneak attacks.** A creature that is afraid of you and can see you gets no sneak-attack bonus from you (the
  game's own rule for its Passive Mobs world setting): it watched you walk up. One that is running from you is alert,
  which also rules out a sneak attack. From behind it, out of its sight and quiet, or in smoke, a sneak attack works as
  usual. Crawling around creatures that are afraid of you raises Sneak only at the slow rate, as if nobody who cared
  were near.
- **No extra name plates.** The mod adds nothing to the health bars. A creature running from a player, or a pack
  running from its fallen leader, is simply alert in the game's own way: the red "!" above its name, the icon the game
  shows for any alert creature, seen by every player. It goes away when the creature calms down. The game
  shows a creature's health bar within 30 m and for a minute after your crosshair passed over it, so aim at a creature
  to see it.
- **Progress messages.** When a boss kill raises your boss rank far enough to make more creatures afraid of you, a
  message in the middle of the screen says "Weaker creatures now keep out of your way", a few seconds after the kill,
  once the boss's own death message has faded (with the default settings nothing is afraid of you after Eikthyr or The
  Elder alone, so those kills show no such message). When you reach a number of kills that matters for a kind of
  creature, a message in the corner says so, for example "Greydwarf: 100 kills. They will be afraid of you sooner."
  (setting **ShowProgressMessages**).
- **Always as in the normal game:** bosses and every creature within 60 m of a boss that is fighting, raids, tames,
  training dummies, the Dvergr, and animals that only flee (deer, hares, piglets...).
- Turn it off in the **MC Mods** panel at any time: creatures behave as in the normal game again, no restart needed.

## Configuration

The config file `BepInEx/config/MC.Combat.Creatures.Morale.cfg` is created the first time you launch the game with the mod.
To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods**. Every setting can be changed in-game with
ConfigurationManager, or by editing the file (the game picks up the change while it runs). Every setting applies
while you play, no restart needed: a creature setting takes effect about a second after you stop editing it, so
typing a list or dragging a slider applies only the final value. In multiplayer the server's (or host's) settings are
used for everyone, except ShowProgressMessages. Creature names in the lists are prefab names, as
used by the `spawn` command, comma-separated.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | Server (or host) only. Off: a player whose game does not have this mod, has it turned off, or has a version that cannot talk to the server's is refused about a second after joining (or after turning the mod off while playing), and their game shows "Incompatible version". On: such players may play; creatures behave as in the normal game toward them. |
| Afraid creatures | BossesAhead | `2` | How many bosses past the boss of a creature's biome a player must have helped kill (hit it at least once before it died) before that creature is afraid of them (0-8). The biome bosses are: Meadows Eikthyr, Black Forest The Elder, Swamp and Ocean Bonemass, Mountains Moder, Plains Yagluth, Mistlands The Queen, Ashlands Fader, Deep North Kall. A creature counts as a creature of the biome it spawned in, or of its home biome if that is harder. |
| Afraid creatures | Elites | `Greydwarf_Shaman, Greydwarf_Elite, Troll, Troll_sleeping, Bjorn, Bjorn_sleeping, Draugr_Elite, Draugr_Elite_sleeping, BlobElite, Wraith, Writhan, Abomination, Fenring_Cultist, StoneGolem, GoblinShaman, GoblinBrute, Unbjorn, SeekerBrute, Gjall, Charred_Mage, Morgen, Morgen_NonSleeping, FallenValkyrie, Greydwarf_Shaman_Frozen` | Creatures that need EliteExtraBosses more bosses than the others of their biome: pack leaders and the biome's big creatures. A creature must also be in a Home biomes list. |
| Afraid creatures | EliteExtraBosses | `1` | How many more bosses the Elites need before they are afraid of a player (0-3). |
| Afraid creatures | StarRank | `1` | Each star of a creature needs this many more bosses before it is afraid of you (0-5). 0 = stars do not matter. |
| Afraid creatures | KillSteps | `100, 400` | Up to 5 increasing numbers (1-100000), comma-separated. Each number of kills you reach of one kind of creature counts as one more boss toward that kind only. Kills are your character's lifetime kills, as the game counts them: creatures that share a name share the count (all skeletons; Greydwarfs and Frozen Greydwarfs), and killing your own tames counts. Empty = kill counts do not matter. |
| Afraid creatures | MaxBossesSkippedByKills | `1` | Kill steps can make a kind of creature afraid of you at most this many bosses early (0-8); beyond that they only make up for its stars. 0 = kill steps only make up for stars. |
| Afraid creatures | FearRange | `12` | An afraid creature runs away when a player it is afraid of comes this close (in metres, 3-40) and it can see or hear them, until that player is a few metres farther away; then it calms down. A player who sneaks up unseen and unheard can get closer. It never attacks that player unless they attack it or corner it. |
| Afraid creatures | NightHuntersCanBeAfraid | `true` | The creatures the game sends at night to hunt players after some boss kills (Fulings after Yagluth, Seekers after The Queen, Charred after Fader) follow the same rules: they are afraid of the players who outclass them and hunt the others. They count as creatures of their home biome (Plains, Mistlands, Ashlands). Off = they hunt everyone as in the normal game. Raids and bosses always behave as in the normal game. |
| Home biomes | Meadows | `Boar, Neck, Greyling, Hen, Skeleton_Meadows, Skeleton_Meadows_noarcher` | Creatures that live in the Meadows, whose boss is Eikthyr. A creature in no list always behaves as in the normal game; a creature in several lists uses the easiest one (the log warns). A name the game does not know is ignored and listed in the log. |
| Home biomes | BlackForest | `Greydwarf, Greydwarf_Shaman, Greydwarf_Elite, Troll, Troll_sleeping, Skeleton, Skeleton_NoArcher, Ghost, Ghost_sleeping, Bjorn, Bjorn_sleeping` | Same, the Black Forest (The Elder). |
| Home biomes | Swamp | `Draugr, Draugr_sleeping, Draugr_Ranged, Draugr_Ranged_sleeping, Draugr_Elite, Draugr_Elite_sleeping, Skeleton_Swamps, Skeleton_Swamps_noarcher, Skeleton_Poison, Blob, BlobElite, Leech, Leech_cave, Surtling, Bat_Swamp, Wraith, Writhan, Abomination` | Same, the Swamp (Bonemass). |
| Home biomes | Mountain | `Wolf, Ulv, Hatchling, Bat, BlobFrost, Skeleton_Mountains, Skeleton_Mountains_noarcher, Fenring, Fenring_Cultist, StoneGolem` | Same, the Mountains (Moder). |
| Home biomes | Plains | `Goblin, GoblinArcher, GoblinShaman, GoblinBrute, Deathsquito, BlobTar, Lox, Unbjorn` | Same, the Plains (Yagluth). |
| Home biomes | Mistlands | `Seeker, SeekerBrood, SeekerBrute, Tick, Gjall` | Same, the Mistlands (The Queen). |
| Home biomes | Ashlands | `Charred_Melee, Charred_Archer, Charred_Twitcher, Charred_Mage, Asksvin, Asksvin_hatchling, Volture, BlobLava, Morgen, Morgen_NonSleeping, FallenValkyrie, BonemawSerpent` | Same, the Ashlands (Fader). |
| Home biomes | DeepNorth | `Greydwarf_Frozen, Greydwarf_Shaman_Frozen, Skeleton_DeepNorth, BlobMork, BlobMorkMini, Elaking, ElakingLantern, Moose` | Same, the Deep North (Kall Fimbulbringer). |
| Home biomes | Ocean | `Serpent` | Creatures of the open sea. The Ocean has no boss and counts as the Swamp's level (Bonemass). A creature that spawned at sea counts as a creature of its home biome. |
| Fighting back | ProvokedSeconds | `30` | After you hit an afraid creature (or one of your projectiles lands within NearMissRange of it), it fights you until this many seconds have passed since the last time, then is afraid of you again (5-300). |
| Fighting back | NearMissRange | `4` | One of your projectiles (arrow, bolt, thrown spear, harpoon) that lands this close (in metres, 0-30) to an afraid creature, even on another target, makes it fight you. Farther away it ignores the impact. It cannot reach beyond the weapon's own noise range, since creatures farther from the impact never notice it: 8 m for bows and crossbows, 4 m for the Huntsman bow, 30 m for thrown spears and the harpoon. 0 = only real hits count. |
| Fighting back | CorneredRange | `3` | An afraid creature that is running from you fights back when you stay this close to it (in metres, 0-6) for CorneredSeconds: it cannot get away, or you keep up with it. 0 = never. |
| Fighting back | CorneredSeconds | `2` | How long (in seconds, 1-30) you must stay within CorneredRange of a running creature before it fights back. |
| Rout | Packs | `Troll, Troll_sleeping, Greydwarf_Elite, Greydwarf_Shaman > Greydwarf, Greyling; Greydwarf_Shaman_Frozen > Greydwarf_Frozen; GoblinBrute, GoblinShaman > Goblin, GoblinArcher; Draugr_Elite, Draugr_Elite_sleeping > Draugr, Draugr_sleeping, Draugr_Ranged, Draugr_Ranged_sleeping; Fenring_Cultist > Fenring, Ulv; SeekerBrute > Seeker, SeekerBrood; Charred_Mage > Charred_Melee, Charred_Archer, Charred_Twitcher` | Packs whose followers flee when a leader dies: each pack is "leader prefabs > follower prefabs", comma-separated, packs separated by semicolons. A creature can lead several packs. The rout happens only when a player or a tame took part in the kill. Empty = no rout. |
| Rout | RoutRadius | `25` | Followers within this distance (in metres, 5-60) of the dead leader flee. |
| Rout | RoutSeconds | `15` | How long (in seconds, 3-60) the followers run away. |
| Rout | ShakenSeconds | `60` | After running away, the followers are afraid of every player for this many seconds (0-600) unless someone attacks them. 0 = they go back to normal at once. |
| Display | ShowProgressMessages | `true` | Show a message when a boss kill or a number of kills makes more creatures afraid of you. Each player's own choice. |
| Debug | ForceBossRank | `-1` | Only in development (Debug) builds, not in the released mod: publish this boss rank (0-8) instead of your real one, for testing. -1 = off. |

## Multiplayer

- **Who needs it:** required on the server (or the host) and on every player's game, turned on.
- **Multiplayer support:** works in multiplayer.

Install it on the server (or the host) and on every player's game, and keep it turned on. Each creature is controlled
by one player's game, and each player's boss kills and kill counts are only known to their own game, so every game
needs the mod. The settings of the server (or host) apply to everyone, and each player is judged by their own
progress, whichever game controls the creature.

- **Players who cannot take part are refused.** A player whose game does not have the mod, has it turned off, or has
  a version that cannot talk to the server's is refused about a second after joining; their game goes back to the
  menu with "Incompatible version", and the server log says why. **Turning the mod off in the MC Mods panel while
  playing on such a server disconnects you** about a second later. The server (or host) can let such players in with
  AllowPlayersWithoutMod = true: creatures then behave as in the normal game toward them, and the creatures their
  game controls attack everyone the normal way.
- **The server's settings apply to everyone** (your log says "Using the server's creature rules", again each time
  the server changes them); your own settings apply again in single player and when you host. ShowProgressMessages
  stays your own. While your game waits for the server's settings as you join (normally before your character
  appears), every creature behaves as in the normal game.
- **Everyone sees the same alert icon.** A creature is alert on every player's screen at the same time, whichever
  game controls it, because the mod uses the game's own alert state.
- **Your progress travels with you.** Your game shares your standing (your boss rank and the kill counts that still
  matter) with the other players' games, so a creature controlled by someone else's game treats you by your own
  progress. After you join or respawn, other games see it within a moment.
- **Playing with a less advanced friend.** A creature that is fighting a player it is not afraid of, or that sees or
  hears such a player within 12 m of it, does not run from you: it fights your friend (and still never attacks you).
  When your friend is gone, it runs from you again.
- **On a server without the mod** the mod turns itself off on your game (the MC Mods panel says why): creatures
  behave as in the normal game there.
- **Dedicated servers:** install BepInEx on the server and put the mod in its `BepInEx/plugins` folder; the server's
  config file decides the rules. The server checks the players, sends its settings and passes pack routs between the
  players' games; it controls no creatures itself.

## Good to know

- **Progress belongs to your character, in every world.** A character that has helped kill Moder keeps that rank in
  a new world. Boss kills and kill counts from before you installed the mod count too, as far back as the game's own
  kill records go. Kills made with cheats (for example a boss you spawned) count as well.
- **Creatures spawned with the `spawn` command** count as spawned where you stand: a Skeleton you spawn in the Plains
  is a Plains skeleton; a Troll you spawn in the Meadows is still a Black Forest Troll (its home biome is harder).
- **Kill counts only travel where they can matter.** Your game shares a kill count only while it can still change how
  that kind of creature treats you in the biomes where the game itself spawns it (its home biome and its natural
  spawns elsewhere, such as Draugr at night in the Plains). A creature of that kind found in a harder biome than
  those (one you spawned with the `spawn` command, one from a nest or a dungeon in such a biome, or one another mod
  put there) may ignore your kill count for it.
- **Shared kill counts.** The game counts kills by creature name, so creatures that share a name share the count: all
  skeletons, Greydwarfs and the Deep North's frozen Greydwarfs, Draugr and Draugr archers. Killing your own tames
  counts too (the game does not tell them apart). MC Creature Kill and Tame Counts shows these counts.
- **A kill step you reach only shows a message when it still matters**: once a kind of creature is afraid of you
  anyway, reaching 100 or 400 kills of it changes nothing and says nothing.
- **An afraid creature can still hurt you by accident**: one fighting your tame next to you can hit you with a swing.
  Your hit back provokes it. When you come within 12 m it usually leaves your tame and runs.
- **Afraid creatures still fight your tames** while you keep your distance, and your tames still attack them.
- **Taming works as in the normal game**: an afraid tameable creature (Boar, Wolf, Lox, Asksvin) runs from you when you
  come close and it notices you, and taming pauses while it is alert. Drop the food and step back, or sneak. It never
  attacks you unless you corner it.
- **Hunting**: afraid animals run when they see or hear you within 12 m, like deer. Sneak up, shoot them, or chase and
  corner them. The first hit is not a sneak attack if the animal watches you.
- **Creatures that cannot get away** (in a pen, in a corner of a dungeon, a leech in its pond): they fight back if you
  stay right next to them for 2 s; keep a few metres away and they don't.
- **Buildings**: afraid creatures may still attack buildings as in the normal game while you are not close.
- **Passive Mobs world setting**: the game already stops creatures from picking players there; the rout and the
  afraid time after it still apply (without cornering).
- **Creatures from other mods** are never afraid until you add their prefab names to a home biome list; they can also
  be packs. Mods that give creatures more than 2 stars: each star needs one more boss, so very high levels are never
  afraid (set StarRank = 0 to ignore stars). Kill counts make up for at most 2 stars (the normal game's maximum);
  stars beyond that need bosses.
- **Upgrading from an early test build:** its `[Calm creatures]` and `[Frightened]` config entries and the
  `ShowOnNameplates` setting are no longer read and can be deleted from the config file.
- **What the mod stores**: while you play, a few bytes on your character with your standing (not saved). On a
  creature it provoked or routed, a few time stamps saved with the world; they run out on their own and the game
  ignores them without the mod. Nothing on items, building pieces or character saves: your standing is always
  recomputed from the game's own records.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. Safe to add or remove at any time (see "What the mod stores" above).

- **Other mods that change when creatures attack players** do the same job and should not be used together with this
  one: TruePassiveMobs, The Mark of Oden, FearMe, Odin's Ótti, CowardlyGreydwarfs, FleeOnSight, Monster AI Tweaks.
  When one of them is installed, the log shows a warning naming it; both mods run and the result is hard to predict.
- **MonsterDB** and other mods that change creatures' flee settings: compatible, this mod reads them as they are (an
  afraid creature runs with the game's own flee movement).
- **Mods that raise creature levels** (Creature Level and Loot Control, Star Level System): compatible; each level
  needs one more boss (StarRank).
- **BetterUI** and other mods that restyle health bars: compatible; this mod adds nothing to the health bars, and a
  running creature is shown as alert the way such a mod shows any alert creature.
- **Mods that add creatures** (RRR, Monstrum, and others): their creatures always behave as in the normal game unless
  you add them to a home biome list.
- **Sneak Ambush** (MC): its smoke and its stealth work together with this mod. An afraid creature that watches you
  gives no sneak attack and no sneak-attack XP; from behind or from inside a Smoke Screen cloud you get both. An afraid
  creature does not run from a player it cannot sense, so you can walk up to it through the smoke; a hit from inside
  the smoke provokes it and reveals you to it, so it fights you through the smoke. Standing in the smoke next to an
  afraid creature that has not noticed you never corners it.
- **Tower Shield Wall** (MC): a shield bash provokes an afraid creature like any hit (it stops running and fights),
  also in New Game+ worlds where armor absorbs nearly all of it; the hit right after the bash is not a sneak attack.
- **Dual Wielding** and **Weapon Moveset** (MC): off-hand hits and every move provoke like any hit. A sneak roll attack
  (Weapon Moveset) on an afraid creature that faces you is not a sneak attack; from behind, unseen, it is.
- **Harpoon Hooks Tames** (MC): harpooning an afraid wild creature provokes it (the harpoon deals damage); hooking
  your own tame provokes only afraid creatures within NearMissRange of the impact.
- **Creature Kill and Tame Counts** (MC): shows the same kill counts the kill steps use.
- **Encyclopedia** (MC): an afraid creature you see counts as met as usual; names and health bars are left as the
  game makes them.
- **Sleep Through the Day** (MC): afraid creatures near the bed, running or not, do not stop you from sleeping; hostile
  ones do.
- **Breeding Star Inheritance** (MC): tames are never touched by this mod.
- These combinations have not been tested in game yet.
