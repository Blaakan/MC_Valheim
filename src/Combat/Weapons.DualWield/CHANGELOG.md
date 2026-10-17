# Changelog

## 0.1.1

- When a server refuses you because of this mod, the "Incompatible version" screen now says so: it lists
  Dual Wielding under "MC mods:" with the reason (your game does not have it, has another version, or has it turned
  off or cannot run it, for example because another dual wield mod keeps it off) and what to do. A server with this
  version sends the reason itself, so the screen names the mod even when your game does not have it.
- This needs at least one MC mod in your game that is made to run on both the server and the players (any mod a server
  can refuse you for, or Sleep Through the Day); the server does not need to have that mod. A game whose MC mods are
  all client-side shows only "Incompatible version".
- When a server refuses you for several MC mods, the screen names the first one that refused you; once that one is
  fixed, the next join names the next one.
- When the server sends no line (a refusal that is not about an MC mod, or a server with an older version of the MC
  mods), the screen lists what your own game noticed while connected: an MC mod turned off while the server has it
  on, or a server copy whose version cannot play with yours. That may not be why you were refused.
- With Jotunn on the server and on your game, Jotunn's window shows the same lines under "Failed Connection:"; with
  Jotunn only on your game, the vanilla panel shows them.

## 0.1.0

- Initial version: hold a one-handed sword, axe, club or knife in each hand, in any mix. Equipping a second one-handed
  weapon puts it in your off hand; hold Left Alt while equipping to replace your main weapon instead (the normal game
  way). Shields and torches keep their normal behaviour.
- Press H to swap hands: the swap is an equip like any other, with the game's equip animation for the weapon's equip
  time, then the draw animation of the hide key as the weapons change hands; attacks wait for it (hold the attack
  button to swing as soon as it allows), it does not start while you sprint (a message says so), and a dodge, jump or
  sprint cancels it.
- Pairs fight with the game's own dual moves: the Berserkir axes' stance, four-swing combo and cleave, or Skoll and
  Hati's combo and leap for two knives. Each hit is struck by one of your weapons, or by both, with that weapon's own
  damage, damage types, effects, skill experience and wear.
- The pair stays in the hands you chose when you hide and draw your weapons, eat, use stations, beds and the barber,
  swim, log out and in, die with the keep-equipment world setting, move items in your inventory or use the radial
  menu's hammer.
- A sheathed pair is holstered by weapon kind, for every player you see with one, instead of two weapons overlapping
  on one spot: two swords, axes or maces cross in an X on the back (the X stays level as its wearer moves), two knives
  hang one on each hip (mirrored), and a knife with a sword, axe or mace hangs where the game puts each (knife at the
  hip, the other weapon on the back).
- The off-hand weapon gets a swing trail too.
- Two knives block like Skoll and Hati: they block as one, with Skoll and Hati's block power scaled to the knives'
  slash, pierce and blunt damage (two Black Metal knives 18, two Nord daggers 40) and its parry bonus (4 times), never
  worse than one knife. A knife with any other weapon blocks with the off-hand weapon, as in the game.
- Settings: off-hand damage, both-hands damage, hit pattern (Alternate or BothHands), attack stamina, special attack,
  knife pair block, the items whose moves pairs use, excluded weapons, the two keys, the off-hand trail and the
  sheathed pair placement.
- Required on the server (or the host) and on every player's game: the server refuses players whose game does not run
  it (AllowPlayersWithoutMod) and its combat settings apply to everyone. Next to another dual wield mod it does not run.
