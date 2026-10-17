# Changelog

## 0.4.4

- Dropped instruments stay where they land: the flute no longer rolls away on level ground.

## 0.4.3

- Fixed: on a gamepad with the default controller layout, closing the song window with B could also make your
  character jump (B is the game's jump button too; about one close in six). The press that closes the window now
  does nothing else in the game, even while you keep B held.

## 0.4.2

- When a server refuses you because of this mod, the "Incompatible version" screen now says so: it lists Music
  Instruments under "MC mods:" with the reason (your game does not have it, has another version, or has it turned off
  or cannot run it) and what to do (install it, install the server's version, or turn it on in Esc > MC Mods, or with
  F1 when a configuration manager is installed). With Jotunn on the server and on your game, Jotunn's window shows
  the same lines under "Failed Connection:"; with Jotunn only on your game, the vanilla panel shows them.
- This needs at least one MC mod in your game that is made to run on both the server and the players (any mod a
  server can refuse you for, or Sleep Through the Day); the server does not need to have that mod. A game whose MC
  mods are all client-side shows only "Incompatible version".
- When a server refuses you for several MC mods, the screen names the first one that refused you; once that one is
  fixed, the next join names the next one.
- When the server sends no line (a refusal that is not about an MC mod, or a server with an older version of the MC
  mods), the screen lists what your own game noticed while connected: an MC mod turned off while the server has it
  on, or a server copy whose version cannot play with yours. That may not be why you were refused.

## 0.4.1

- Fixed eight false recipe warnings in the log ("... recipe material ... is not an item in this game", "... is not a
  crafting station ...", "... has no valid material ...") when you left a server that does not have the mod or has it
  turned off. The recipes were fine; only the messages were wrong.

## 0.4.0

- Free play: the song window's first entry turns the keyboard into a piano, laid out like one (by place on the
  keyboard): Z to M and Q to P are the white keys of two octaves, the black keys sit on the rows above them; hold Space
  for an octave up. An on-screen piano shows your keys and lights the ones you hold. The flute holds a note while its
  key is down, the lyre's strings ring together, the tambourine has four keys (Z X C V). Other players hear it live;
  it gives no comfort bonus.
- Network version 5: every player and the server need this version.

## 0.3.0

- The Encore is easier: hitting half of your last 20 notes for 20 seconds is enough (every hit counts, near-perfect or
  not; one wrong key while a note is due costs no more than that note's miss). Below that the meter only waits (it no
  longer drains), and misses in a row no longer stop it. The setting is now RequiredAccuracy (default 0.5) and
  replaces SuccessAccuracy (0.7), whose old line is ignored.
- Network version 4: every player and the server need this version.

## 0.2.0

- Music comfort now counts wherever you rest, also sitting by a campfire outdoors (comfort 1 + 3 = 4, so Rested lasts
  11 minutes instead of 8). The BonusOnlyInShelter setting is removed (an old line in the config file is ignored).
- Server songs: with ShareSongs on, a server (or host) shares the MIDI files of its
  `BepInEx/config/MC_Valheim/ServerSongs` folder. Every player sees them under "Server songs" in the song window and
  downloads one when they pick it (in small pieces; the world data keeps priority), then plays it like their own files.
- New server setting AllowPlayerSongs: off = only the built-in songs and the server's songs.
- Network version 3: every player and the server need this version.

## 0.1.0

- Initial version. Three craftable instruments with their own models and icons: a Wooden Flute (workbench, Fine Wood),
  a Tambourine (workbench level 2, Fine Wood and Leather Scraps) and a Silver Lyre (workbench level 2, Silver and
  Linen Thread). Tools like the hammer: held in the right hand, they take both hands.
- Attack with an instrument opens the song window: eight built-in songs (four traditional, four written for this mod)
  and your own MIDI files from `BepInEx/config/MC_Valheim/Songs`, with an automatic or chosen part per instrument.
  Play a song by itself (you may walk; Repeat available) or perform it in a four-lane rhythm game (D F J K).
- The sound is synthesized live (no audio files) and heard by players within 40 m, from the performer's position;
  others also see the playing pose. The game's music fades while instruments are heard.
- 20 seconds of good playing in the rhythm game gives Music (+3 comfort for 10 minutes, in shelter) to the performer
  and every player within 20 m; each further 20 seconds renews it.
- Required on the server (or the host) and on every player's game: the server refuses players without the mod, with
  it turned off or with another version of it (AllowPlayersWithoutMod), relays the music to players in range, and its
  recipe, comfort and hearing settings apply to everyone.
