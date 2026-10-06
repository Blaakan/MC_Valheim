# Changelog

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
