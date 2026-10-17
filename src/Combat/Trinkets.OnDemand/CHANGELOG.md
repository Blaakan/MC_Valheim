# Changelog

## 0.2.1

- When a server refuses you because of this mod, the "Incompatible version" screen now says why: under "MC mods:" it
  lists Trinkets on Demand with the reason (your game does not have it, has another version, or has it turned off or
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

## 0.2.0

- Other players count like creatures: while PvP is on for you and for the other player, hitting them, being hit by
  them (blocked or not) or perfectly dodging their attack starts a fight and gives the income while fighting. With PvP
  off for either of you, their hits do nothing and start no fight. In 0.1.0 other players were meant not to count,
  yet a perfect dodge of a player's attack did start a fight; now every case follows one rule.

## 0.1.0

- Initial version.
- Income while fighting: 1 adrenaline per second while you wear a trinket and are in a fight (a hit given to or taken
  from a hostile creature within the last 6 seconds, longer while an alerted creature keeps hunting you), on top of
  the normal game's gains. Training dummies, tamed creatures and other players do not count.
- No drain: while a trinket gives the bar its room, the bar never drains over time. Losses in a fight stay.
- A full bar waits: it no longer fires the trinket by itself. Press Y (gamepad: left trigger + right stick press) to
  fire every trinket you wear, as the normal game's full bar does. Messages for a partly filled bar and for no trinket;
  optional refusal while the effect still runs (RefuseWhileActive, off by default).
- Bows and crossbows pay more adrenaline per hit when a shot takes longer than 1.5 seconds: the draw held (bows) or
  the reload (crossbows) plus half a second, against a 1.5-second reference (about the same adrenaline per second as
  melee), up to 4 times the normal amount.
- The bar flashes when it becomes full (again every 4 seconds while full) and a message names your trigger; trinket
  tooltips name it too.
- Trinkets are not changed: every trinket keeps its effect and cost, trinkets from other mods included.
- Stays off (and says why) when BetterTrinkets, Passive Trinket Modifiers or Balrond Battle Flow is installed.
- The main numbers (income, fight length, ranged reference and cap, flash interval) are settings.
- Required on the server (or the host) and on every player's game: the server refuses players without the mod, with it
  turned off or with another version of it (AllowPlayersWithoutMod), and its settings apply to everyone. The keys and
  the full-bar feedback are each player's own.
