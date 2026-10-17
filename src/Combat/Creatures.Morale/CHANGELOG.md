# Changelog

## 0.1.2

- When a server refuses you because of this mod, the "Incompatible version" screen now says why: under "MC mods:" it
  lists Creature Morale with the reason (your game does not have it, has another version, or has it turned off or
  cannot run it) and what to do. The server sends that line, so the screen names the mod even when your game does not
  have it.
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

- Fixed: a sleeping creature that is afraid of you and happens to face you lost the sneak-attack bonus. A sleeper
  watches nobody, so a hit on it is a sneak attack again, as in the normal game.

## 0.1.0

- Initial version: weak creatures are afraid of the players who have outgrown them. A creature is afraid of a player
  who has helped kill the boss two bosses after its biome's boss (Black Forest greydwarfs once Bonemass and Moder are
  dead); elites and pack leaders need one boss more, and each star one more. Each player is judged by their own boss
  rank (the furthest boss they helped kill) and their kills of each kind of creature (100 kills = one boss more toward
  that kind, 400 = two, at most one boss early).
- A creature counts as a creature of the biome it spawned in, or of its home biome if that is harder: skeletons that
  spawned in the Plains are Plains creatures, a greydwarf in the Deep North is a Deep North creature, the greydwarfs
  that come out at night in the Meadows stay Black Forest greydwarfs. One home biome list per biome, plus the open sea;
  creatures in no list behave as in the normal game.
- Afraid creatures never attack you, do not start the combat music and do not stop you from sleeping or resting. They
  run away when you come within 12 m and they see or hear you, and calm down once you are a few metres farther away;
  sneak up unseen and quietly and they don't notice you. They fight back when you hit them, land a projectile within
  4 m of them or corner them (they run but you stay within 3 m of them for 2 s), and are afraid of you again 30 s after
  the last time.
- Packs run away when their leader falls: Greydwarfs and Greylings when a Troll, Greydwarf Shaman or Greydwarf Brute
  dies, plus the Deep North greydwarfs, Fulings, Draugr, Cultists, Seekers and Charred. They flee for 15 s within
  25 m, then are afraid of everyone for 60 s unless attacked.
- The night hunters sent after some boss kills follow the same rules (setting NightHuntersCanBeAfraid).
- An afraid creature that sees you gives no sneak-attack bonus, and crawling near afraid creatures raises Sneak slowly.
- No extra name plates: a creature running from a player, or a pack running from its fallen leader, shows the game's
  own alert icon (the red "!" above its name) to every player, and loses it when it calms down.
- Messages when a boss kill or a number of kills makes more creatures afraid of you (can be turned off).
- Bosses and creatures near a boss fight, raids, tames, training dummies and the Dvergr always behave as in the
  normal game.
- Required on the server (or the host) and on every player's game: the server refuses players without it, with it
  turned off or with a version that cannot talk to the server's (AllowPlayersWithoutMod), and its settings apply to
  everyone.
