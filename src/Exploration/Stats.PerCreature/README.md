# Creature Kill and Tame Counts

Shows how many of each creature you have killed, and how, plus how many you have tamed, at the top of Player Statistics in the Valheim Compendium. Kills come from the game's own saved stats, so past kills show too. Tames count from install.

## Features

- **A "Creatures" section at the top of Valheim Compendium > Player Statistics** (inventory → the game's Valheim
  Compendium button → last entry, on its Texts tab when the MC Encyclopedia mod is installed; not in the Encyclopedia,
  which has no Player Statistics). One
  line per creature, for example `Greydwarf: 243 killed (melee 180, ranged 50, other 13)` or
  `Boar: 12 killed (melee 11, other 1), 3 tamed`. Creature names are shown in your game language. The vanilla
  statistics follow below, unchanged.
- **Kills are the game's own all-time count per creature.** The game has saved a kill count per creature since the
  Call to Arms update, but it never showed it. So the list is retroactive: kills made before you installed the mod
  show right away. It covers every world you played with this character. Kills from before that update were not saved
  per creature, so they are missing.
- **Who gets a kill is the game's rule**: every player who hit the creature gets the kill, even with a hit that did no
  damage, not only the one who landed the last hit. A player who logged out before it died gets nothing.
- **How the kills were made** (can be turned off): melee, ranged, magic, unarmed, and **other**. The game saves the
  weapon type only for kills made since the Deep North update. "Other" holds the rest: kills with several weapon
  types (for example a club hit, then a bow kill), kills with no weapon, kills of a creature that healed back to full
  health first (the game then forgets the earlier weapon), and kills from before the game recorded weapon types. The
  parts always add up to the kill count. When only "other" is known (for example all kills are older than Deep
  North), the parentheses are left out: `Neck: 40 killed`.
- **"Other names" line**: names that are not creatures with a translation key, such as player names from PvP kills
  saved by an older game version, or modded creatures without a translation key, are listed under their own grey
  line, so a player name never reads as a creature.
- **Tames per creature, counted while the mod is on**, from the day it first ran for this character. The page shows
  that "since" date. The game itself only keeps a total. Who gets a tame:
  - the player the game tells "... has been tamed" (the closest player within 30 m of the creature);
  - if nobody was that close, the player whose game ran the taming (the game that controls the creature, not always
    the player standing next to it), unless that game sees another player closer to the creature. Then nobody gets
    it: a missed tame is better than a tame given to the wrong player. That game only sees the players in the area
    it has loaded around it.
  - In single player you always get your own tames.
- **Not counted as tames**: offspring born from your tames, hatched chicks, young animals that grow up, summoned
  creatures (they start tame without being tamed), and tames made while the mod is off or not installed.
- The vanilla "Creature Tamed" line further down the page can differ from the per-creature tames: the game gives that
  total to the player whose game ran the taming, and the `tame` console command also adds to it for creatures it does
  not tame (for example ones that are already tame).
- Turn it off in the **MC Mods** panel at any time to get the vanilla page back, no restart needed.

## Configuration

The config file `BepInEx/config/MC.Exploration.Stats.PerCreature.cfg` is created the first time you launch the game with
the mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration manager
installed that button is hidden: press F1 and use its window). The display settings can be changed in-game with
ConfigurationManager, or by editing the file (the game picks up the change while it runs). Display settings apply the
next time you open the Valheim Compendium.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| Display | SortBy | `MostKilled` | Order of the creature list in Player Statistics. `MostKilled`: the creatures you killed most come first. `Name`: alphabetical. |
| Display | ShowWeaponTypes | `true` | After each kill count, show how the kills were made: melee, ranged, magic, unarmed, or other (several weapon types, no weapon, or kills from before the game recorded weapon types, that is before the Deep North update). Left out when only other is known. |

## Multiplayer

- **Who needs it:** only you. It works on vanilla servers and with friends who don't have it.
- **Multiplayer support:** works in multiplayer.

Kills follow the game's own rule: every player who hit a creature gets the kill, whoever landed the last hit. The game
sends each of them the kill, even when the creature is controlled by a friend or a server without the mod.

A tame goes to the player the game tells it has been tamed (the closest one within 30 m). The game sends that message
whoever runs the taming, so this works with friends and servers without the mod. If nobody was that close, the tame
goes to the player whose game ran the taming (that player needs the mod), unless that game sees another player closer
to the creature; then nobody gets it. A game only sees the players in the area it has loaded around it. A tame is
never given to two players.

Each player's numbers stay on their own character. The mod sends nothing to other players and stores nothing on
creatures: a creature tamed with the mod is an ordinary tame for everyone, including friends without the mod.

## Good to know

- The section is built when you open the Valheim Compendium. A kill or tame made while it is open shows the next
  time you open it.
- Tame counts are saved with your character (in the character file, like the game's own stats), so they survive
  death, logout and cloud saves. If you remove the mod, the game keeps them in the character file and ignores them;
  reinstall it and they show again, with the same "since" date.
- Tames are counted even while the HUD is hidden (Ctrl+F3), when the game does not show the "has been tamed" message.
- Bosses are listed like any creature. Star levels and variants that share a name (for example every Greydwarf
  level) share one line.
- Kills made by your tames, other creatures or console kill commands are not yours (the game gives no credit), unless
  you had hit that creature before.
- Kills made with cheats on count too: the game's all-time count includes them (only its achievement stats leave
  them out).

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. Safe to add or remove at any time: kills are only read, and tame counts are extra data the
game ignores.

- **DudeWhatAreMyStats** shows the same game data in its own window, so the kill totals should match (not tested in
  game yet).
- **Almanac, TrophyHuntMod** show kill counts in their own panels. They may keep counts of their own, so their numbers
  can differ from this list. This mod only reads the game's data, so they do not get in each other's way.
- **Other mods that edit the Valheim Compendium** (EquipmentAndQuickSlots, SecondaryAttacks): compatible, since this mod only
  adds text to the Player Statistics entry (not tested in game yet). If a mod removes that entry, the section is not
  shown.
- **One Click Repair All** (MC): works together; the two mods change different things.
- **Harpoon Hooks Tames** (MC): hooking a tame does not make its later death your kill when the game running the
  tame runs that mod (always in single player); otherwise the game may credit you with that kill.
- Mods that tame creatures through their own code, without the game's normal taming, are not counted.

## For mod authors

The numbers can be used by other mods, for example a bestiary or compendium. Everything below is a stable contract:
names, meanings and the storage format never change; new members may be added (then `ApiVersion` goes up).

### API

Public static class `MC.Exploration.StatsPerCreatureMod.CreatureCounts` in `MC.Exploration.Stats.PerCreature.dll`.
Reference that DLL when you build, and add `[BepInDependency("MC.Exploration.Stats.PerCreature")]` to your plugin.
The creature key everywhere is `Character.m_name`, the translation token such as `"$enemy_greyling"` (variants and
star levels share it). Every member is read-only, must be called from the main thread, never throws, and works
whether the feature is turned on or off.

| Member | Returns |
|---|---|
| `int ApiVersion` (static property) | `1`. Read at runtime, see below. |
| `bool IsAvailable` | True when every member can be read: in a world **and** the local character is spawned. False on the main menu, while the world loads (before the first spawn) and while respawning after death (from when the game removes the dead character, about 10 s after death, until the new one spawns). If you cache the data, read it again when this turns true (after loading, after each respawn). |
| `int GetKills(string creatureName)` | All-time kills of that creature, every weapon type (the game's total). |
| `int GetKills(string creatureName, KillModifiers modifier)` | Kills for one bucket of the game's `KillModifiers`: `MixedAndTotal` = total; `Unarmed`, `Magic`, `Ranged`, `Melee` = kills made with only that weapon type; `CountNone` = 0. |
| `CreatureCounts.KillBreakdown GetKillBreakdown(string creatureName)` | Read-only struct with `Total`, `Melee`, `Ranged`, `Magic`, `Unarmed`, `Other`. `Other` = `Total` minus the four, never below 0, so the parts add up to `Total`. `Other` holds kills with several weapon types, with no weapon, after a full-health reset, and kills from before the Deep North update: do not show it as "mixed". |
| `int GetTames(string creatureName)` | Tames of that creature counted by this mod while it was on, since `CountingSince`. |
| `DateTime? CountingSince` | Local date this mod started counting tames for this character. Null when it has not started, or when there is no local character right now (main menu, world loading, respawn wait). |
| `List<string> GetCreatureNames()` | New list (yours to keep): every name with at least 1 kill or 1 tame, unsorted. Includes names without `$` (player names from old PvP saves, modded creatures without a translation key). |
| `string TamesDataKey`, `string CountingSinceDataKey` (constants) | The `Player.m_customData` keys described below. |

Kill members need only the game and the player profile (0 or empty on the main menu). Tame members also need the
local character (0, null or empty on the main menu, while the world loads and while waiting to respawn).

`ApiVersion` is read at runtime. Put calls to members newer than version 1 in a separate method marked
`[MethodImpl(MethodImplOptions.NoInlining)]`, and call that method only after checking `CreatureCounts.ApiVersion`.
The runtime resolves a missing member when it compiles the method that contains the call, so a call written inline
would fail even behind the check.

### Data, without a reference to this mod

- **Kills** are the game's own data: `Game.instance.GetPlayerProfile().m_playerStats[0].m_enemyStats`, an array of 5
  dictionaries indexed by `KillModifiers` (`[0]` total, `[1]` unarmed, `[2]` magic, `[3]` ranged, `[4]` melee), keyed
  by creature name, float values. Use slot `0` (lifetime) and never add the slots up: the other slots are copies kept
  for achievements (`PlayerProfile.GetStat`, for the general stats, reads one of those copies whenever achievements
  are allowed).
- **Tames** are in `Player.m_localPlayer.m_customData["MC.Exploration.Stats.PerCreature.Tames"]`. Format version 1:
  lines separated by `\n`; the first line is the format version `1`; every other line is the count (plain digits),
  one TAB, then the creature name (the rest of the line). Example: `1\n3\t$enemy_boar\n2\t$enemy_wolf`. Skip lines you
  cannot read. If the first line is not `1`, a newer version of this mod wrote it.
- **Start date** is in `Player.m_localPlayer.m_customData["MC.Exploration.Stats.PerCreature.CountingSince"]`:
  `yyyy-MM-dd`, invariant culture (Gregorian calendar, Western digits).
- A missing key means nothing recorded yet. This works even when this mod is not installed.
