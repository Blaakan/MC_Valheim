# Music Instruments

Craft a flute, a lyre and a tambourine and play them: built-in songs, your own MIDI files or the server's, or a rhythm
mini-game whose good performances give everyone nearby extra comfort.

## Features

### Three instruments

- **Wooden Flute**: an end-blown flute carved from fine wood. Workbench, 4 Fine Wood.
- **Tambourine**: a fine wood hoop with a leather skin and bronze jingles. Workbench level 2, 3 Fine Wood and 4
  Leather Scraps.
- **Silver Lyre**: a small round lyre, a silver frame strung with linen. Workbench level 2, 2 Silver and 8 Linen
  Thread.
- The materials set the tier, as in the game: fine wood needs a bronze axe, silver comes from the Mountains and linen
  thread from the Plains. Server owners can change the materials, the stations and their levels.
- **Tools, like the hammer**: held in the right hand, they take both hands (equipping one puts away what you hold, and
  a weapon or torch puts it away); put away (R), they hang at your hip. They never wear out and cannot be upgraded.
- **Their own look and icons**, made by the mod itself (no extra files).

### Playing

- **Attack (left mouse button) with an instrument in hand opens the song window.** Pick a song, then:
  - **Play**: the song plays by itself. You can walk while you play (no running, jumping or dodging). **Repeat** plays
    it again and again. Attack or Block stops it. It keeps playing behind the pause menu, like the game's music.
  - **Perform**: play it yourself in the rhythm game (below).
  - **Close**, Esc or the gamepad B button closes the window.
- **Built-in songs**: four traditional tunes (Drunken Sailor, Greensleeves, Vem kan segla förutan vind, Kjerringa med
  staven) and four written for this mod (Hearthfire Lullaby, Row the Longship, Raven's Jig, Mead Hall Reel). Each
  instrument plays its own part: the flute the melody, the lyre the melody with strummed chords, the tambourine the
  rhythm.
- **Your own MIDI files** (any song you have a MIDI file of, for your own use): put `.mid` files (also `.midi`, `.kar`, `.rmi`) in `BepInEx/config/MC_Valheim/Songs` (the
  folder is made the first time you open the window; the SongsFolder setting can point elsewhere). The window lists
  them every time it opens. For each instrument the mod picks the part that sounds most like the tune (the lyre adds a
  soft bass line, the tambourine plays the drums, or the tune's rhythm when the file has no drums); the part chooser
  lets you pick any other part. Other players do not need the file: they hear your notes.
- **Server songs** (optional, off by default): a server (or host) with ShareSongs on shares the MIDI files of its
  `BepInEx/config/MC_Valheim/ServerSongs` folder. They show under "Server songs" in every player's window. Picking one
  (click, Enter, Play or Perform; moving over it with the keys does not) downloads it from the server, with the progress
  in the window: about a second for a usual file, up to a minute or more for a very big one. Play or Perform on a song
  still coming starts it when it is here. It then plays like your own files, split into a part per instrument.
  Downloaded songs are kept until you leave the world. The server can also allow only its own songs and the built-in
  ones (AllowPlayerSongs).
- **Play by the fire**: you can play sitting on a chair or bench or in the sit emote, and you stay seated.
- **It stops by itself** when you put the instrument away or swap it, swim, take a ship's helm or a saddle, lie in a
  bed, die or teleport, and when you are hit (fire, poison, frost, smoke, water and drowning damage do not count),
  staggered or knocked back.
- **The sound is made live by the mod** (a small synthesizer, no audio files): a breathy wooden flute, a plucked lyre
  and a jingling tambourine. The game's Master and Sound effects volume sliders apply, plus the mod's own Volume
  setting. While instruments are heard the game's own background music fades (GameMusicVolume).

### The rhythm game and comfort

- **Notes fall in four lanes**; press the lane's key (D, F, J, K by default) when a note reaches the line. Each hit
  plays that note (and the quick notes after it), so a good run plays the whole song; a miss is silent. You stand
  still while you perform; the song repeats until you stop (right mouse button or Esc). The rhythm game is played
  on the keyboard (no gamepad lanes yet).
- **The meter** beside the lanes fills while you play well and drains twice as fast when you do not; it waits during
  long rests. "Well" means your last ten notes average at least 70 %: a near-perfect hit counts fully, a less exact
  one three quarters, a missed note or a key pressed with no note to hit nothing; three misses in a row also stop it. **After 20 seconds of good
  playing: Encore!** You and every player within 20 metres get **Music** for 10 minutes: **+3 comfort**. Each further
  20 seconds of good playing renews it. Songs that play by themselves never fill the meter.
- **What +3 comfort does**: comfort sets how long Rested lasts (in the base game 1 minute per point), so resting by a
  fire with Music gives 3 more minutes of Rested. It counts wherever you rest: in a shelter, and also sitting by a
  campfire under the open sky (the game's own comfort there is 1 and Rested lasts 8 minutes; with Music, comfort 4 and
  11 minutes). The Music icon shows the bonus and
  the time left.

### Other players

- **They hear you** from where you stand (up to 40 metres), and **see you play**: your arms hold the flute to your
  mouth, the lyre against your chest or the tambourine up high, and move with the notes.
- **They share the Encore**: every player within 20 metres of you when it happens gets Music too.

### Turning it off

Turn the mod off in the MC Mods panel (or set Enabled = false) at any time: the music stops at once, the recipes are
hidden and clicking with an instrument does nothing. Your instruments stay in your inventory. A Music effect already
running keeps its icon until it ends but adds no comfort while the mod is off. In multiplayer the server refuses
players who turn it off (unless its AllowPlayersWithoutMod setting is on): your game returns to the menu with
"Incompatible version" about a second later.

## Configuration

The config file `BepInEx/config/MC.Exploration.Music.Instruments.cfg` is created the first time you launch the game
with the mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration
manager installed that button is hidden: press F1 and use its window). Every setting can be changed in-game with
ConfigurationManager, or by editing the file (the game picks up the change while it runs). In multiplayer the
server's (or host's) settings in the Recipes, Comfort and Hearing sections and AllowPlayerSongs are used for
everyone; ShareSongs and ServerSongsFolder are used only by the server (or host); Enabled and the Sound, MiniGame and
Songs sections are each player's own.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | Used only by the server (or host). Off: players without the mod, with it turned off or with another version of it are refused (about a second after joining, or after turning it off). On: they may play. Players without the mod never see instruments (on the ground, in chests or in hands), a chest loses its instruments when such a player takes or adds an item in it, and they hear no music; players with the mod turned off or another version of it see and keep instruments, but hear no music and get no comfort bonus. |
| Recipes | FluteRecipe | `FineWood:4` | Materials of one flute, as item names with amounts. Unknown names are skipped (with a warning in the log); with no valid material the recipe is hidden. Server's setting in multiplayer. |
| Recipes | FluteStation | `piece_workbench` | Crafting station by its object name (`piece_workbench`, `forge`, `piece_artisanstation`...). Empty: crafted from the inventory. Server's setting. |
| Recipes | FluteStationLevel | `1` | Station level the recipe needs (1 to 10). Server's setting. |
| Recipes | LyreRecipe, LyreStation, LyreStationLevel | `Silver:2,LinenThread:8`, `piece_workbench`, `2` | The same for the lyre. Server's settings. |
| Recipes | TambourineRecipe, TambourineStation, TambourineStationLevel | `FineWood:3,LeatherScraps:4`, `piece_workbench`, `2` | The same for the tambourine. Server's settings. |
| Comfort | ComfortBonus | `3` | Comfort added by Music (0 to 10; 0 = no bonus). Server's setting. |
| Comfort | SuccessSeconds | `20` | Seconds of good playing for an Encore (5 to 120); each further stretch renews Music. Server's setting. |
| Comfort | SuccessAccuracy | `0.7` | How well you must play while the meter fills: the average of your last ten notes (a near-perfect hit counts 1, a less exact one 0.75, a miss or a stray key 0), 0.3 to 1. Server's setting. |
| Comfort | BonusMinutes | `10` | How long Music lasts (1 to 60 minutes). Server's setting. |
| Comfort | BonusRange | `20` | Players within this many metres of the performer get Music too (3 to 50). Server's setting. |
| Hearing | HearingRange | `40` | Players farther than this from a performer hear nothing (10 to 80 metres; the sound fades toward it). Server's setting. |
| Sound | Volume | `0.8` | Loudness of every instrument you hear, yours and others' (0 to 1; the game's Master and Sound effects volume apply on top). Your own choice. |
| Sound | GameMusicVolume | `0.3` | Loudness of the game's own music while you hear someone play (0 to 1; 1 = unchanged). It fades back afterwards. Your own choice. |
| MiniGame | Lane1Key ... Lane4Key | `D`, `F`, `J`, `K` | Keys of the four lanes, left to right. The game's own keys (walking, hotbar, inventory, map, chat, console) are held back during the rhythm game, so most keyboard keys work. Esc and the right mouse button cannot be lane keys (they stop the rhythm game): such a lane is turned off, with a warning in the log. Your own choice. |
| MiniGame | NoteSpeed | `1` | How fast the notes fall (0.5 to 2): higher = farther apart, easier to read in fast songs. Does not change the song's tempo. Your own choice. |
| Songs | SongsFolder | empty | Folder with your MIDI songs. Empty: `BepInEx/config/MC_Valheim/Songs`. Your own choice. |
| ServerSongs | ShareSongs | `false` | Used only by the server (or host). On: every player can play the MIDI files of the server songs folder; a file is sent to a player's game when they pick it. |
| ServerSongs | ServerSongsFolder | empty | Used only by the server (or host). Folder of the shared songs (up to 200 files of at most 2 MB). Empty: `BepInEx/config/MC_Valheim/ServerSongs`. Read again when a player opens the song window. |
| ServerSongs | AllowPlayerSongs | `true` | On: players may also play their own MIDI files. Off: only the built-in songs and the server's songs. Server's setting. |

## Multiplayer

- **Who needs it:** required on the server (or the host) and on every player's game.
- **Multiplayer support:** works in multiplayer.

Install it on the server (or the host) and on every player's game. The instruments are new craftable items and their
music gives a comfort bonus, so the server refuses players who do not have the mod, have it turned off or have another
version of it (their game shows Incompatible version), unless its AllowPlayersWithoutMod setting is on, and the
settings of the server (or host) apply to everyone. The server passes each performance on to the players near the
performer; those within hearing range hear it from the performer's position; a good mini-game performance gives the Music comfort bonus
to every player near the performer. The server can also share its own MIDI songs with every player (ShareSongs). A player without the mod hears nothing, never sees instruments (on the ground, in
chests or in hands), and a chest loses its instruments when such a player takes or adds an item in it. On a server
without the mod, instruments dropped on the ground can be deleted.

- **Players who cannot play by the server's rules are refused** about a second after they join (or after they turn
  the mod off); their game shows
  "Incompatible version". This covers a player without the mod, a player whose version of the mod cannot talk to the
  server's, and a player who has it turned off. With AllowPlayersWithoutMod = true they may play. A game without the mod does
  not know the instruments: they cannot see or pick up a dropped one, they see an empty hand when you hold one, an
  instrument in a chest does not show for them (when they take or add an item in that chest it is saved without it,
  and it is lost for everyone), and they hear no music. Keep instruments out of chests such players use. A player with the
  mod turned off or another version of it sees and keeps instruments, but hears no music and gets no comfort bonus.
- **On a server without the mod** the mod turns itself off for you (no recipes, no playing), and an instrument you drop
  on the ground there can be deleted by the server.
- **The server's settings apply to everyone** (your game logs "Using the server's rules"): the recipes, the comfort
  bonus rules and the hearing range. Until they arrive after you join, the recipes are hidden and you cannot play
  ("The server has not sent the instrument settings yet."). Your own settings apply again in single player and when
  you host.
- **The sound settings, the mini-game keys and the songs folder are yours.** Whether your own MIDI files may be
  played is the server's choice (AllowPlayerSongs).
- **Server songs** go only to players with the mod, only when they pick one, one file at a time per player, in small
  pieces sent only while the connection has room: the world data keeps priority.
- **The music travels as notes**, not as sound: a performance costs well under 1 KB per second per listener, and only
  players near the performer receive it (within hearing range or the Encore range, plus a small margin). Listeners hear it a fraction of a second late (a quarter of a second for the rhythm game,
  less for songs that play by themselves; a safety margin against network hiccups that can grow to three quarters of a
  second after one; after a longer stall the notes it held back are skipped and the music goes on in time); the
  performer hears it at once.
- **Dedicated servers:** install BepInEx on the server and put the mod in its `BepInEx/plugins` folder; the server's
  config file decides the Recipes, Comfort, Hearing and AllowPlayerSongs settings for everyone. To share songs, set
  ShareSongs = true there and put the MIDI files in its `BepInEx/config/MC_Valheim/ServerSongs` folder (the mod makes it
  when ShareSongs is on: at start, or when the setting changes). The server makes no sound itself.

## Good to know

- **Rested only grows while you rest.** Comfort counts when Rested is set: after about 20 seconds of resting by a fire
  (sitting or in shelter), and while you keep resting. Music that ends while you rest never shortens Rested.
- **Comfort displays.** The Resting icon shows the comfort with the bonus. Mods that compute comfort on their own to
  display it do not include the Music bonus, and the bonus does not count toward the game's comfort achievement or
  the "highest comfort" statistic.
- **Blocking and clicking.** As with the hammer, Block with an instrument in hand blocks with your fists (not while
  you play or while the window is open). A click with an instrument never punches, also while the mod is turned off.
- **MIDI files** can be up to 2 MB (server songs too). Damaged files are read up to the damage; files that cannot be read show why in the
  window. Very busy files are thinned out: the flute plays one note at a time, the lyre up to four, the tambourine at
  most about fourteen hits a second from a drum part (about seven when it follows the tune of a file without drums).
- **Uninstalling** the mod removes every instrument from inventories and chests the next time they load, as with any
  item a mod adds. Turning it off (Enabled = false) keeps them.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is
packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. It adds three items (`MC_Flute`, `MC_Lyre`, `MC_Tambourine`) and one status effect
(`SE_MC_Music`), and saves one temporary value on your character while you play; nothing else is stored.

- **Comfort mods** (Epic Loot's Comfortable effect, Jewelcrafting, HeadRest, RuneboundRest, ComfortTweaks...): their
  bonuses and Music add up. Mods that change how long Rested lasts per comfort point (Comfortable, Valheim+, Seasons)
  change what +3 is worth.
- **Mods that change the game music volume** (Viking Shanties, Ominous): set GameMusicVolume = 1 so the two do not
  fight over it.
- **Other instrument mods** (Bardheim, Bragi): different items; they can be installed together.
- **Spyglass** (MC): both are tools; each one only acts while it is in your hand.
- **Sleep Through the Day** (MC): waking up with Music on shows the comfort with the bonus in the Rested message.
- **Encyclopedia**, **Crafting Search and Sort** (MC): all hold the game's keys back the same way while their windows
  or the rhythm game are open.
- **Dual Wielding** (MC): instruments are never part of a dual-wield pair; equipping one puts a pair away.
- **Sort Chest**, **Crafting Search and Sort** (MC): instruments are sorted with the tools.
- **Other MC mods:** no overlap.
