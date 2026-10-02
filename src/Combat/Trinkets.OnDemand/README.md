# Trinkets on Demand

Adrenaline builds while you fight and no longer drains away. A full bar waits for you: press a key to fire your
trinkets when you choose. Bows and crossbows earn more adrenaline to make up for their slower attacks.

Trinkets themselves are not changed: every trinket keeps its effect and its adrenaline cost, trinkets added by other
mods included.

## Features

### The bar fills while you fight

- **Income while fighting:** while you wear a trinket and are in a fight, you gain 1 adrenaline every second, on top
  of everything that already gives adrenaline in the normal game (hits, blocks, parries, perfect dodges, staggers,
  the Forsaken power). World modifiers and effects that change adrenaline gains apply to it too.
- **What counts as a fight:** you hit a creature, or a creature hits you (blocked or not), or you make a perfect dodge.
  The fight lasts 6 seconds after the last such hit. While an alerted creature keeps hunting you (kiting, closing in
  with a bow), it lasts longer, up to 20 seconds after the last hit.
- **What does not count:** training dummies, tamed creatures, the Dvergr (unless you angered them), other players
  (PvP), and falling or drowning. A creature hunting you without anyone landing a hit is not a fight either.

### The bar never drains

While a trinket gives the bar its room, the bar keeps what it earned between fights: it no longer drains over time.
Anything that takes adrenaline away in the normal game still does (in Valheim 1.0.16 neither a missed swing nor a hit
you do not block costs any, so in practice the bar only empties when you fire it). Without a trinket, the normal game's
rules apply: the bar drains and hides.

### A full bar waits for you

In the normal game a full bar fires your trinket at once. With this mod it waits, full, until you press the trigger:

- **Keyboard:** `Y` (setting TriggerKey).
- **Gamepad:** hold the left trigger and press the right stick (settings GamepadModifier and GamepadButton).

The press does what the normal game's full bar does: every trinket you wear starts (or restarts) its effect, the bar
empties and the normal game's adrenaline effect plays. A press with a partly filled bar says "Adrenaline not full
yet"; without a trinket it says "No trinket to trigger". The key works whenever the game takes your input (not in
menus, chat, the map or the inventory, not while the radial menu is open or while building), also mid-swing and
mid-roll: it plays no animation.

Pressing again while the effect still runs restarts it and spends the bar, as the normal game does. With
RefuseWhileActive on, the press is refused instead ("Trinket effect still active") and the bar is kept.

### Bows and crossbows catch up

In the normal game every arrow and bolt pays 2 adrenaline per hit, a small fixed amount like a melee hit (1 per enemy
hit for a one-handed weapon, 2 for a two-handed one), although a shot takes much longer than a swing. Here a bow or
crossbow hit pays more when the shot took longer than 1.5 seconds (setting RangedReferenceSeconds): a shot that took 3
seconds pays twice the normal amount. That way a slow shot earns about 1.3 adrenaline per second of shooting, between
a one-handed weapon (about 1.1 per second) and a two-handed one (about 1.4). The numbers below are for the default
settings and world modifiers:

- **Bow:** the time is the draw you actually held plus half a second to shoot. A full draw takes 2.5 seconds at Bows 0
  and gets shorter as the skill rises, down to 0.5 seconds at Bows 100 (the same for every bow). So a full draw pays
  twice the normal amount at Bows 0 (4 per hit instead of 2), 1.33 times at Bows 50 (about 2.7), and the normal amount
  from Bows 75 up (the bow is fast enough by then). A quick tap pays the normal amount.
- **Crossbow:** the time is the reload plus half a second. The reload takes 3.5 seconds at Crossbows 0 and half that
  at Crossbows 100 (the same for every crossbow), so a crossbow hit pays 2.67 times the normal amount at Crossbows 0
  (about 5.3 per hit instead of 2) and 1.5 times at Crossbows 100 (3).
- At most 4 times the normal amount (RangedMaxMultiplier). A shot fired within moments of the previous one (a quick
  weapon swap) earns no bonus. Weapons that shoot several projectiles share the bonus between them.
- Only bows and crossbows: spears, staffs, bombs and other ranged weapons pay as in the normal game.

### You see when it is ready

- When the bar becomes full it flashes, and a message at the top left names your trigger ("Adrenaline full: press [Y]
  to trigger your trinket"; at most every 20 seconds). While it stays full it flashes again every 4 seconds.
- Trinket tooltips say which key or buttons fire them.

### Turning it off

Turn it off in the **MC Mods** panel at any time: the normal game's rules are back at once. The bar drains again, full
or not, once the normal game's delay since your last gain has passed (about a second if your last gain was a while
ago). A bar waiting full fires at your next hit or block only if that comes before the drain starts.

## Configuration

The config file `BepInEx/config/MC.Combat.Trinkets.OnDemand.cfg` is created the first time you launch the game with the
mod. To turn the mod on or off in-game, open the menu (Esc) and click **MC Mods** (with a configuration manager
installed that button is hidden: press F1 and use its window). Every setting can be changed in-game with
ConfigurationManager, or by editing the file (the game picks up the change while it runs), and applies at once. In
multiplayer the server's (or host's) settings are used for everyone, except the Controls and Feedback settings (each
player's own choice); AllowPlayersWithoutMod is read only by the server.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Enabled | `true` | Turn the feature on or off. Takes effect immediately. |
| General | Status | — | Written by the mod: shows whether the feature is active, and if not, why. |
| General | AllowPlayersWithoutMod | `false` | Server (or host) only. Off: a player whose game does not have this mod, has it turned off or has another version of it is refused about a second after joining, or about a second after turning it off (their game shows "Incompatible version"). On: they may play; their adrenaline and trinkets work like the normal game. |
| Controls | TriggerKey | `Y` | Keyboard key (or mouse button) that fires your trinkets when the bar is full. `None` = no keyboard trigger. Each player's own choice. |
| Controls | GamepadModifier | `LeftTrigger` | Gamepad button to hold while pressing GamepadButton (`None`, `LeftTrigger`, `LeftBumper`, `RightBumper`, `RightTrigger`). `None` = GamepadButton alone. Each player's own choice. |
| Controls | GamepadButton | `RightStick` | Gamepad button that fires your trinkets (`None`, `RightStick`, `LeftStick`, `DPadUp`, `DPadDown`, `DPadLeft`, `DPadRight`, `ButtonA`, `ButtonB`, `ButtonX`, `ButtonY`, `Back`). `None` = no gamepad trigger. Each player's own choice. |
| Feedback | ShowFullMessage | `true` | Show the "Adrenaline full" message when the bar becomes full (at most every 20 seconds). The bar flashes either way. Each player's own choice. |
| Feedback | FullFlashInterval | `4` | While the bar stays full, flash it again every this many seconds (0-60). 0 = flash once. Each player's own choice. |
| Adrenaline | IncomePerSecond | `1` | Adrenaline gained every second in a fight while you wear a trinket (0-10). 0 = no income. |
| Adrenaline | CombatLingerSeconds | `6` | A fight lasts this many seconds after the last hit you gave to or took from a hostile creature (1-30). |
| Trigger | RefuseWhileActive | `false` | On: the trigger is refused while the effect of a trinket you wear still runs (the bar is kept). Off: triggering again restarts the effect and spends the bar, as in the normal game. |
| Ranged | RangedReferenceSeconds | `1.5` | Bow and crossbow hits pay more when a shot takes longer than this many seconds (0.2-5). The default makes shooting earn about as much adrenaline per second as melee. |
| Ranged | RangedMaxMultiplier | `4` | Most a bow or crossbow hit can pay, as a multiple of the normal amount (1-10). 1 = no ranged bonus. |

## Multiplayer

- **Who needs it:** required on the server (or the host) and on every player's game.
- **Multiplayer support:** works in multiplayer.

Install it on the server (or the host) and on every player's game. It changes combat for everyone, so the server
refuses players who do not have the mod, have it turned off or have another version of it (their game shows
Incompatible version), unless its AllowPlayersWithoutMod setting is on, and the settings of the server (or host) apply
to everyone. Trinkets themselves are not changed: a trinket that reaches a player without the mod works as in the
normal game.

- **Refused players:** about a second after joining, a player whose game does not have the mod, has it turned off or
  has another version of it goes back to the menu with "Incompatible version"; the server log names them and the
  reason. Turning the mod off in the MC Mods panel while connected gets you refused the same way about a second later
  (turning it off and on again within that second is fine). The server (or host) can let such players in with
  AllowPlayersWithoutMod = true.
- **The server's settings apply to everyone** (your game logs "Using the server's rules"); your own settings apply
  again in single player and when you host. The Controls and Feedback settings stay your own. Until the server's
  settings arrive (normally before your character appears), your bar works like the normal game.
- **Everything runs on your own game:** your bar, your trigger, your income and your arrows. Nothing new is sent between
  players besides the server's settings, and nothing is saved in the world or on items.
- **On a server without the mod** the mod turns itself off on your game (the MC Mods panel says why) and your bar works
  like the normal game.
- **Players without the mod** (only possible with AllowPlayersWithoutMod on): their bar drains, fires the trinket as
  soon as it is full, and gets no income or ranged bonus, as in the normal game. Trinkets you give them work normally.
- **Dedicated servers:** install BepInEx on the server and put the mod in its `BepInEx/plugins` folder; the server's
  config file decides the rules and it checks who joins.

## Good to know

- **Gamepad layouts:** left trigger + right stick press is free in the game's default gamepad layout (the right stick
  press alone still hides your weapons; after a trigger, that press does not also hide them or open the radial menu).
  The left trigger is also block in the default layout: holding it to trigger raises your shield. In the Alternative
  1 layout the left trigger puts your weapons away (tap) and opens the radial menu (hold; the trigger does not work
  while the radial menu is open), and the right stick press is crouch. In the Alternative 2 layout the left trigger is
  block and the alt button, and the right stick press is crouch (with the alt button it toggles alternative
  placement). Pick another pair there (GamepadModifier, GamepadButton), or use the keyboard key.
- **Training dummies** still pay the normal adrenaline for your hits, so you can try a trinket on one, and the bar
  keeps what you earned. They give no income.
- **Hunting counts as a fight:** hitting a deer or a boar counts too. Every creature counts except tamed ones, the
  Dvergr (unless angered) and training dummies.
- **The bar is not saved** (normal game): it is empty after you log in or respawn.
- **Swapping trinkets keeps the bar.** A bar above a cheaper trinket's cost counts as full (and shows that cost); a
  dearer trinket needs more.
- **Several trinkets** (mods that add trinket slots): one press fires every trinket you wear, as the normal game's full
  bar does.
- **The world level rule for equipping trinkets** (New Game+) still applies.
- **Trinket costs** (the size of the full bar) go from 10 to 100 adrenaline in Valheim 1.0.16, and their effects last
  from 1 to 120 seconds. This mod changes neither.
- **The flash** is the game's own adrenaline bar flash. If a future game version drops that animation, only the
  message tells you the bar is full.

## Installation

Download from Nexus Mods and extract the zip into your Valheim folder. Full steps are added here when the mod is
packaged (from packaging/nexus/install.md).

## Compatibility

Built for Valheim 1.0.16. Nothing is saved: removing the mod leaves no trace in your character or world.

- **Dual Wielding** (MC): its swap-hands key is H, this mod's trigger is Y. A dual-wield swing pays adrenaline as
  before, and a full bar waits during it.
- **Weapon Moveset** (MC): jump and roll attacks pay adrenaline like any swing; the trigger also works mid-roll.
- **Crossbow Stays Loaded** (MC): swapping to a crossbow that is already loaded skips the reload, so a shot right
  after a swap earns only the bonus for the time since your last shot.
- **Harpoon Hooks Tames** (MC): hooking a tame still pays no adrenaline (a harpoon is not a bow or crossbow anyway).
- **Tower Shield Wall** (MC): a tower shield never parries, so a well-timed block pays the normal block adrenaline;
  a full bar waits through it, like through any block.
- **Sneak Ambush** and **Creature Morale** (MC): a creature that loses you in the smoke, or flees in fear, stops hunting
  you, so the fight ends 6 seconds after the last hit. Hitting it again starts a new one.
- **BetterTrinkets, Passive Trinket Modifiers, Balrond Battle Flow:** they change when or what trinkets fire, relying
  on the normal game's full bar. With one of them installed this mod stays off (the MC Mods panel says why): remove
  one of the two. A server that requires this mod then refuses that game.
- **AdrenalineModifier, KeepAdrenalineLonger, RageNAdrenaline, MultiTrinket, Surge:** they work together with this mod
  (the log says so when one is found). AdrenalineModifier's scaling also applies to the income; KeepAdrenalineLonger
  only matters without a trinket; RageNAdrenaline's own keys (F, G, D-pad up and left by default) must not be Y;
  MultiTrinket's trinkets all fire on one press; Surge's costs set the size of the full bar.
