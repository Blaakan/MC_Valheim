# Changelog

## 0.1.0

- Initial version. Three craftable instruments with their own models and icons: a Wooden Flute (workbench, Fine Wood),
  a Tambourine (workbench level 2, Fine Wood and Leather Scraps) and a Silver Lyre (workbench level 2, Silver and
  Linen Thread). Tools like the hammer: held in the right hand, they take both hands.
- Attack with an instrument opens the song window: eight built-in songs (four traditional, four written for this mod)
  and your own MIDI files from `BepInEx/config/MC_Valheim/Songs`, with an automatic or chosen part per instrument.
  Play a song by itself (you may walk; Repeat available) or perform it in a four-lane rhythm game (D F J K).
- The sound is synthesized live (no audio files) and heard by players within 40 m, from the performer's position;
  others also see the playing pose. The game's music fades while instruments are heard.
- 20 seconds of good playing in the rhythm game gives Music (+3 comfort for 10 minutes, so a longer Rested, also when
  resting outdoors by a campfire) to the performer and every player within 20 m; each further 20 seconds renews it.
- Required on the server (or the host) and on every player's game: the server refuses players without the mod, with
  it turned off or with another version of it (AllowPlayersWithoutMod), relays the music to players in range, and its
  recipe, comfort and hearing settings apply to everyone.
