# Changelog

## 1.0.0

- First release.

## 0.2.1

- When a server refuses you because of this mod, the "Incompatible version" screen now says so: it lists Tower Shield
  Wall under "MC mods:" with the reason (your game does not have it, has another version, or has it turned off or
  cannot run it) and what to do. With Jotunn on the server and on your game, Jotunn's window shows the same lines
  under "Failed Connection:"; with Jotunn only on your game, the vanilla panel shows them. A server with this version
  sends its reason, so the screen names the mod even when your game does not have it. This needs at least one MC mod
  in your game that is made to run on both the server and the players (any mod a server can refuse you for, or Sleep
  Through the Day); the server does not need to have that mod. A game whose MC mods are all client-side shows only
  "Incompatible version". When a server refuses you for several MC mods, the screen names the first one that refused
  you; once that one is fixed, the next join names the next one. When the server sends no line (a refusal that is not
  about an MC mod, or a server with an older version of the MC mods), the screen lists what your own game noticed
  while connected: an MC mod turned off while the server has it on, or a server copy whose version cannot play with
  yours. That may not be why you were refused.
- The `AutoTowers` description now says exactly which shields it takes: those with a parry bonus below 1 (the iron
  square shield, parry bonus 1, is left out, as before).

## 0.2.0

- Tower shields from other mods are found automatically: every shield with a parry bonus below 1 (it cannot parry at
  all) is a tower shield (new setting `AutoTowers`, on by default), for example Warfare's Bronze, Chitin and Carapace
  tower shields. Shields a server sends after you join are picked up without a restart; changes Jotunn mods make to
  item data while the game loads are taken into account. Copies that only creatures carry stay normal. The log lists
  the shields found.
- New setting `ExcludedTowers`: shields that are never tower shields.
- `Towers` now adds tower shields and sets their bash damage. A tower shield without a number (and every tower
  shield found automatically) gets a bash damage from its block armor, on the same scale as the normal-game towers
  (it used to be 10). The seven normal-game tower shields keep their values.
- Multiplayer: the server's `AutoTowers` and `ExcludedTowers` are used for everyone. Players need this version
  (network version 3).

## 0.1.1

- Fixed a false warning, "The Towers setting names items this game does not have (MISSING), ignored: ...", that listed
  every default tower shield. It was logged at the main menu with some other item mods installed (seen with
  Jotunn-based mods) and when you left a server that does not have the mod or has it turned off. A real typo in the
  Towers setting is still reported.
- Fixed a punch or a weapon swing that started by itself: a press of the attack button that was waiting for the bash
  cooldown stayed active for up to half a second after the tower shield left your hands (put away, another weapon
  equipped) or after the mod was turned off. Easy to run into with a long `BashCooldown`. The waiting press now ends
  with the tower shield; a press made in the last half second still counts, as in the normal game.

## 0.1.0

- Initial version: tower shields (Wood, Bone, Iron, Serpent scale, Black metal, Flametal, Nord Greatshield) become
  two-handed and cannot parry; they keep their shield look (left hand, back shield slot, armor stand shield slot).
- Very slow in hand: jog -30%, sprint -45% (never slower than jogging); no slowdown while put away on the back.
- Block armor ×2.5 (also per quality level).
- Bracing (holding block with a tower shield): 30% slower movement and turning; enemy attacks from the front that
  normally cannot be blocked (poison clouds, sprays, area attacks) are blocked, while allies' spells still reach you;
  80% less stagger and no push back from the front while you have stamina for one more full block; a "Braced" status
  icon that flashes "Braced (exhausted)" when stamina runs low.
- Shield bash on the attack button: low blunt damage per tower shield, heavy stagger (×25 of its blunt, applied before
  the creature's armor), trained by Blocking. Every enemy in its arc takes its damage and push, but only the one nearest
  the middle of the swing takes the heavy stagger. A deliberate move: 20 stamina (a bash stagger costs about what a
  buckler parry stagger costs a player who lands one parry in three, and like a parry one bash staggers one creature at
  most), at most one bash every 2 s (a press before the time is up starts the next bash when it is), and a slowed swing
  (0.6 of its speed, hit included, seen the same by every player). A creature a bash staggered takes no heavy bash
  stagger for 8 s. Selectable animation (ShieldPunch, OtherPunch, Kick, Custom; any case) with an automatic fallback
  when an animation does not land its hit or does not end.
- New Game+: the bash gets no world-level damage bonus, and its stagger follows creature health.
- Tooltip lines: "Cannot parry", bash stagger, bash cooldown, what bracing does.
- Settings: the tower shield list with each bash damage, block armor and block force, carry and brace slowdown,
  brace stagger and knockback resistance, unblockable attacks, and every bash value (animation and its speed, stamina,
  cooldown, stagger, stagger lock, knockback, reach, arc).
- Required on the server (or the host) and on every player's game: the server refuses players without the mod, with
  it turned off or with a version that cannot talk to the server's (AllowPlayersWithoutMod), and its settings apply
  to everyone.
- A log warning names other installed mods that change shields, blocking or attacks.
