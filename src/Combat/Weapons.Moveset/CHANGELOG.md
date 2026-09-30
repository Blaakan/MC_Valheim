# Changelog

## 0.1.0

- Initial version: a **jump attack** (jump, then attack before you land) and a **roll attack** (roll, then attack) for
  melee weapons. Each move plays another swing of the weapon's own combo (jump: the last swing, roll: the second
  swing) with its own multipliers: jump ×1.2 damage and ×2 stagger, roll ×1.3 damage and ×1.5 stagger by default. The
  combo carries on from the move.
- The roll attack flows straight out of the roll, with no standing up to idle in between: a pressed or held attack
  cuts into the very end of the roll (0.85 seconds after it started by default, so only the stand-up is gone; never
  within 0.2 seconds of the roll's invulnerability ending, so no player sees you invulnerable during the swing) and the
  swing blends out of it. A new press right after the roll makes a roll attack only while you are still getting up
  from the roll: a roll attack never plays after a stand-up. This holds from the very first roll attack after starting
  the game (the mod looks the attack animations up in the game's animation set). Other players with the mod see the
  same.
- A jump attack's swing follows your aim up or down by up to 30 degrees while you are in the air.
- Spears and sledgehammers (and the battleaxe roll attack) are off by default; every weapon type's animation for each
  move is a setting (a list of the game's melee attack animations, or Off).
- Settings: each move on or off, damage, stagger, knockback and stamina multipliers per move, aim angle, roll window,
  when the roll attack may cut into the roll and how long it blends out of it, and a 1-second cooldown between moves.
- Only the game's own animations: every player sees the moves. Secondary attacks, ranged weapons, tools, torches and
  shields stay vanilla, and so does every attack after a roll that is not a roll attack.
- Required on the server (or the host) and on every player's game: the server refuses players without it, with a
  version that cannot talk to it, or with it turned off (AllowPlayersWithoutMod), and its settings apply to everyone.
- Goo's Combat Overhaul installed: this mod's jump attack stays off, the roll attack still works.
