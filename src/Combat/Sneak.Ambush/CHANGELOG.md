# Changelog

## 1.0.0

- First release.

## 0.1.2

- When a server refuses you because of this mod, the "Incompatible version" screen now says why: under "MC mods:" it
  lists Sneak Ambush with the reason (your game does not have it, has another version, or has it turned off or cannot
  run it) and what to do. The server sends that line, so the screen names the mod even when your game does not have
  it.
- This needs at least one MC mod in your game that is made to run on both the server and the players (any mod a server
  can refuse you for, or Sleep Through the Day); the server does not need to have that mod. A game whose MC mods are
  all client-side shows only "Incompatible version".
- When a server refuses you for several MC mods, the screen names the first one that refused you; once that one is
  fixed, the next join names the next one.
- When the server sends no line (a refusal that is not about an MC mod, or a server with an older version of the MC
  mods), the screen lists what your own game noticed while connected: an MC mod turned off while the server has it on,
  or a server copy whose version cannot play with yours. That may not be why you were refused.
- With Jotunn on the server and on your game, Jotunn's window shows the same lines under "Failed Connection:"; with
  Jotunn only on your game, the vanilla panel shows them.

## 0.1.1

- Fixed a false error at the main menu, "The vanilla Smoke Bomb (BombSmoke) is missing from the item database", shown
  when another mod has its own item list there (seen with Jotunn-based mods). The Smoke Screen always worked; only the
  message was wrong.
- Fixed five false recipe warnings in the log ("... is not an item in this game", "... is not a crafting station ...",
  "... has no valid material ...") when you left a server that does not have the mod or has it turned off.

## 0.1.0

- Initial version. Sneak attacks (the normal game's backstab on an unaware creature) give Sneak XP: 3 plus 10 per 100
  creature health (capped at 3 times, stars count), half for arrows, bolts and thrown weapons, at most once per
  creature every 5 minutes; tames and training dummies pay nothing. A "Sneak attack! +N% Sneak" message shows the
  progress gained.
- Sneaking is up to 15% harder to see at Sneak 0; the help fades out and sneaking is exactly the normal game at Sneak
  100.
- Holding still while crouched: after 1 second creatures see you at 30% of the distance; moving ends it at once.
- Fog hides you outdoors (up to 30% in thick fog, read from the weather); bushes and tree crowns show the cover they
  already give in the normal game.
- Status icons: Holding still, In foliage, Fog, In the mist and In smoke, with explanations in the Active effects page.
- New item, the Smoke Screen (workbench: 2 Resin, 1 Coal and 1 Leather scraps make 2): a 15-second cloud that hides
  players from creatures both ways, blinds creatures chasing someone near the burst so they give up, and hides the
  health bars of creatures inside it (tames keep theirs). A creature you hit sees you; bosses are not fooled. No
  damage, no choking.
- Every number is a setting. Required on the server (or the host) and on every player's game: the server refuses
  players without the mod, with it turned off or with another version of it (AllowPlayersWithoutMod), and its settings
  apply to everyone. Smoke Screens stay in inventories and chests while the mod is turned off.
