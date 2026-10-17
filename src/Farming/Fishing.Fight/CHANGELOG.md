# Changelog

## 0.1.2

- When a server refuses you because of this mod, the "Incompatible version" screen now says so: it lists Fishing Fight
  under "MC mods:" with the reason (your game does not have it, has another version, or has it turned off or cannot
  run it) and what to do (install it, install the server's version, or turn it on in Esc > MC Mods, or with F1 when a
  configuration manager is installed). With Jotunn on the server and on your game, Jotunn's window shows the same
  lines under "Failed Connection:"; with Jotunn only on your game, the vanilla panel shows them.
- The list needs at least one MC mod in your game that is made to run on both the server and the players (any mod a
  server can refuse you for, Fishing Fight included, or Sleep Through the Day); the server does not need to have that
  mod. A game whose MC mods are all client-side shows only "Incompatible version".
- When a server refuses you for several MC mods, the screen names the first one that refused you; once that one is
  fixed, the next join names the next one.
- When the server sends no line (a refusal that is not about an MC mod, or a server with an older version of the MC
  mods), the screen lists what your own game noticed while connected: an MC mod turned off while the server has it on,
  or a server copy whose version cannot play with yours. That may not be why you were refused.

## 0.1.1

- Fixed: reeling a fighting fish with the rod on the right side came in stop-and-go and stopped for good about a metre
  from the catch, while the stamina cost went on. Turning the rod away from the fish no longer holds the reel back: the
  line keeps coming in, down to the catch if the fight lasts that long.
- Fixed: a fish out of the water no longer fights. While the hooked fish hangs on a short line above the water or lies
  on the shore, no fight starts and the catch bar stays; a fight in progress ends as soon as the fish leaves the water,
  and the next one waits until the fish is back in the water. Before, such a fish "fought" without moving: the bar went
  away, line ran out, and reeling toward it cost the wrong-side price.

## 0.1.0

- Initial version. Once a fish is hooked, a catch bar shows next to the crosshair: hold Block to raise the zone, let
  go to let it fall. While the fish's icon is inside the zone the line reels in on its own and costs no stamina;
  outside it no line comes in and stamina drains at the normal reeling cost.
- Every few seconds the fish fights: the bar goes away and the fish runs to your left or right, dragging the float and
  the line, with splashes. Point your rod (the way you face) at least 30 degrees off the line on the other side and
  reel to bring line in; reeling the wrong way costs four times the stamina and brings no line; not reeling lets the
  fish take line back, and past 30 m the line breaks. The fish can change sides mid-fight.
- Fish from later biomes and fish with stars are harder (wilder in the bar, longer and more frequent fights, faster
  line runs); the Fishing skill makes the zone bigger and lowers the costs. Casting, bait, the bite and losing the
  fish at 0 stamina work as in the normal game.
- Optional arrow during fights (off by default), bar size and position: personal settings. The bar size, difficulty,
  stamina costs, rod angle and timings are server settings. Required on the server (or the host) and on every
  player's game: the server refuses players without the mod, with it turned off or with another version of it
  (AllowPlayersWithoutMod), and its settings apply to everyone. Stays off next to Trolling Fishing, PeasFishing,
  ChillHook and ChillFishing, and next to GrindstoneSkills or Hooked while their own fishing fight is on.
