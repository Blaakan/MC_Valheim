# Changelog

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
