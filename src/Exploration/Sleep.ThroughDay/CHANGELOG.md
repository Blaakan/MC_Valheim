# Changelog

## 1.0.0

- First release.

## 0.1.2

- When a server refuses you because of another MC mod (your game does not have it, has another version, or has it
  turned off or cannot run it), the "Incompatible version" screen now lists that mod under "MC mods:" with the reason
  and what to do. This needs at least one MC mod in your game that is made to run on both the server and the players
  (any mod a server can refuse you for, or Sleep Through the Day), so Sleep Through the Day is enough on its own; the
  server does not need to have it. A game whose MC mods are all client-side shows only "Incompatible version". Sleep
  Through the Day itself never gets you refused.
- When a server refuses you for several MC mods, the screen names the first one that refused you; once that one is
  fixed, the next join names the next one.
- When the server sends no line (a refusal that is not about an MC mod, or a server with an older version of the MC
  mods), the screen lists what your own game noticed while connected: an MC mod turned off while the server has it on
  (Sleep Through the Day included), or a server copy whose version cannot play with yours. That may not be why you
  were refused.
- With Jotunn on the server and on your game, Jotunn's window shows the same lines under "Failed Connection:"; with
  Jotunn only on your game, the vanilla panel shows them.

## 0.1.1

- Fixed: a sleep that started after nightfall (18:00) could be taken for a daytime sleep and wake you with "Good
  evening" when it ended on the same day, which only happens when another mod stops the game from skipping the night.
  Only a sleep that starts during the day (06:00 to 18:00) now counts as a daytime sleep; a sleep that starts at
  night always wakes you with "Good morning".

## 0.1.0

- Initial version: lie down in your bed in the morning and wake up at nightfall of the same day (18:00 by default),
  with the usual sleep screen, time skip and Rested bonus, and a "Good evening" message.
- Settings: wake-up hour (13 to 23), afternoon sleeps ending at the wake-up hour too (off by default), wake-up
  message.
- Required on the server (or the host) and on every player's game; turns itself off on servers without it.
