# Existing mods — Combat / UX

Research done 2026-09 (snapshot taken 2026-09-28, about three weeks after Valheim 1.0 shipped on 2026-09-09).

## How to read this

- **Sources.** Thunderstore package pages and the Thunderstore package API (`/api/experimental/package/<ns>/<name>/`) for versions and dates. GitHub for source repositories. Nexus pages return HTTP 403 to automated fetches, so Nexus facts come from search snippets only and are marked as such.
- **Dates.** "Updated" is Thunderstore's `date_updated`. Listing edits can also change this date, so it does not always mean a new build.
- **1.0 status legend:**
  - **yes**: the author says the mod supports Valheim 1.0, Unity 6 or the Deep North update.
  - **likely**: a release came out after 2026-09-09, but the author does not say it supports 1.0.
  - **unknown**: the last release is from before 1.0. Valheim 1.0 moved the game to Unity 6000.0.75f1 and broke most BepInEx mods, so assume these need a rebuild.
  - **dead**: the package is deprecated or has not changed since 2021-2022.
- **Coverage.** Coverage says how much of *our* idea the existing mods already implement: none, partial or full.
- **Low-effort mods.** Many packages uploaded after 1.0 carry Thunderstore's "AI Generated" category and have very few downloads. Treat them as sources of ideas only, not as a measure of quality.
- **Licenses.** Several useful repos are GPL/LGPL. Read them to learn, but do not copy their code into our repo.

---

## Combat

### Weapon revamp (Revamp): a slight rework with a roll attack, a parry attack and a jump attack

**Coverage: partial.** GCO already has jump attacks, but only inside a large overhaul. Parry follow-ups exist as a sped-up secondary counter (PPR) or as damage buffs (GrindstoneSkills' Riposte, Combat Momentum). No mod found adds a roll attack: the vanilla input buffer already lets an attack fire as a roll ends, and one mod suppresses part of that as an exploit. For livelier swings, GCO tunes lunge, attack movement and swing speed, and a few small mods cancel or chain attacks. Nobody ships the three context attacks as a light, standalone layer on the vanilla moveset.

| Mod | Status | Notes |
|---|---|---|
| [Goo's Combat Overhaul (gnls)](https://thunderstore.io/c/valheim/p/gnls/GoosCombatOverhaul/) | v2.1.2, updated 2026-09-26, 1.0: yes (targets 1.0.16) | Souls-like overhaul. Jump attack: jump, then press primary before landing (a deliberate jump qualifies at once; stepping off a ledge needs 1 s of airtime). Running attack: sprint into primary. Both borrow a chain step of the family (often the finisher) with their own tuning, and a jump attack does not advance the combo. Also per-family lunge, attack movement, swing and recovery speed, block canceling after the hit, hyperarmor, and a "Counter" bonus against targets that are mid-attack. No roll attack and no parry attack. YAML per weapon, needs ConditionalConfigSync. Closed source, listed under AI Generated. About 8.6K downloads. |
| [PPR – Perfect Parry & Reflect (LJS)](https://thunderstore.io/c/valheim/p/LJS/PPR/) | v1.2.6, updated 2026-09-26, 1.0: likely | A stricter perfect parry (0.1 s by default) opens a 1 s counter window: the secondary attack then plays at 2× animation speed, and damage taken is halved during the window. Also reflects blocked projectiles. Tagged AI Generated. About 2.5K downloads. |
| [GrindstoneSkills (MilkyTeam)](https://thunderstore.io/c/valheim/p/MilkyTeam/GrindstoneSkills/) | v0.10.0, updated 2026-09-29, 1.0: likely | "Riposte", a perk of its new Defense skill from level 25: an attack started within 2 s of a parry deals 25% more melee damage and staggers what it hits (not bosses). The parry window widens with Defense (0.35 s at 100). Server and every client. About 650 downloads. |
| [Combat Momentum (SENEZ)](https://thunderstore.io/c/valheim/p/SENEZ/CombatMomentum/) | v1.0.0, 2026-07-10, 1.0: unknown | Each perfect parry adds a Momentum stack (up to 5, 10 s each): +10% damage and +8% attack speed per stack, and a 20% chance of double damage at 5 stacks. About 330 downloads. |
| [SpecialAttack (MM94)](https://thunderstore.io/c/valheim/p/MM94/SpecialAttack/) | v1.2.2, updated 2026-09-27, 1.0: yes (rebuilt for Unity 6) | One special per weapon category (10 categories) on the secondary attack or a custom key, extra hits unlocked by skill level, cooldown HUD. A "Quickstep" dash replaces the roll. Tagged AI Generated, about 1K downloads. |
| [SecondaryAttacks (sighsorry)](https://thunderstore.io/c/valheim/p/sighsorry/SecondaryAttacks/) | v1.2.14, updated 2026-09-29, 1.0: yes (tagged Deep North) | New secondary attacks for every weapon class and Blood Magic. YAML with per-prefab overrides, plus a reference list of usable animation names. Optional quickstep for knives and fists. Tagged AI Generated. About 12.6K downloads. |
| [AttackCancel (MrGay)](https://thunderstore.io/c/valheim/p/MrGay/AttackCancel/) | v1.27.0, updated 2026-09-14, 1.0: yes (requires Valheim 1.0.x) | Block during an attack cancels it into the block/parry state; Block + Jump cancels it into the vanilla directional roll. Tagged AI Generated. About 1.6K downloads. |
| [AttackCancleCounter (IDRdhnTM)](https://thunderstore.io/c/valheim/p/IDRdhnTM/AttackCancleCounter/) | v1.2.0, 2025-11-16, 1.0: unknown | Cancels an attack mid-animation into a parry or dodge, and allows a "counter" when cancelling while chaining attacks. About 1.7K downloads. |
| [Cancel Animation Cancels (sighsorry)](https://thunderstore.io/c/valheim/p/sighsorry/Cancel_Animation_Cancels/) | v1.0.3, updated 2026-09-09, 1.0: yes (tagged Deep North) | The opposite view: treats vanilla block, dodge and emote cancels as exploits and restarts the combo after one (it watches a 0.25 s window after an attack). For unarmed, spears, axes, battleaxes and atgeirs, a dodge started right after an attack clears the queued attacks, and the primary attack is suppressed while block and attack are held during a dodge. A compatibility trap for a roll attack pressed mid-roll with block still held. Server-synced. Tagged AI Generated. About 1.3K downloads. |
| [Sword Heavy Slash (Fai)](https://thunderstore.io/c/valheim/p/Fai/Sword_Heavy_Slash/) | v1.0.0, 2026-09-22, 1.0: yes (built for Valheim 1.0) | Swords play `dualaxes0` as their third combo hit and `greatsword2` as their secondary attack, and hide the shield during the heavy attack. Shows that other families' animation triggers play on a sword. Tagged AI Generated. About 80 downloads. |
| [ChainAttacks (blacks7ar)](https://thunderstore.io/c/valheim/p/blacks7ar/ChainAttacks/) | v1.0.3, updated 2026-09-13, 1.0: yes (tagged Deep North) | After the first swing, the next combo swings skip their starting animation, so the chain flows continuously. About 5.5K downloads. |
| Related | | [Valheim Legends 1.0 port (momos3939)](https://thunderstore.io/c/valheim/p/momos3939/ValheimLegends/) (v0.7.12, 2026-09-29, 1.0: yes) has the Duelist's "Riposte", an evasive stance that strikes back at incoming attacks. [WeaponArts (j1gA)](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/) (v0.14.1, 2026-09-26, 1.0: yes, tested with 1.0.16) gives one active art per weapon type on a key. [Quickstep (shudnal)](https://thunderstore.io/c/valheim/p/shudnal/Quickstep/) (v1.0.15, 2026-09-17, 1.0: yes) replaces the roll with a dash. [Movement (PIXPIX)](https://thunderstore.io/c/valheim/p/PIXPIX/Movement/) (v0.7.3, 2026-03-02, 1.0: unknown) has an airborne "Slam" on its own key and a landing roll. [WeaponsAttackAnimationManager (blacks7ar)](https://thunderstore.io/c/valheim/p/blacks7ar/WeaponsAttackAnimationManager/) ([Nexus 2708](https://www.nexusmods.com/valheim/mods/2708)) (v1.0.3, 2025-03-11, dead: deprecated) remapped primary and secondary animations per weapon; its README lists the vanilla attack animation names from before 1.0. |

**Inspiration.**
- **Vanilla today:** you can already swing in the air (no ground check in `Humanoid.StartAttack`), an attack pressed late in a roll fires as it ends (0.5 s input buffer), and a parry staggers the attacker, who then takes double damage. The three moments exist; they just play the normal swing.
- **Borrow:**
  - GCO's input rules for the jump attack (primary before landing after a deliberate jump, a second of airtime when stepping off a ledge) and its choice to borrow the family's finisher without advancing the combo.
  - PPR's short counter window after a parry, and GrindstoneSkills' 2 s riposte window.
  - Sword Heavy Slash's proof that other families' triggers play on a weapon, and the vanilla animation names listed by WeaponsAttackAnimationManager and SecondaryAttacks.
- **What they get wrong:**
  - GCO only comes as a full overhaul (stamina, hyperarmor, PvP, enemy behaviour).
  - PPR puts its counter on the secondary attack; GrindstoneSkills and Combat Momentum only add numbers.
  - SpecialAttack and SecondaryAttacks add specials on the secondary slot or on new keys, with effects that do not look like Valheim.
  - Nobody has a roll attack, and Cancel Animation Cancels even suppresses some attacks queued through a roll for some weapons.
- **How ours can differ:**
  - A slight, standalone layer: the primary attack becomes a roll attack, a parry attack or a jump attack when started in a short window after that action, each with its own vanilla animation and a modest bonus. The secondary attack keeps its vanilla role.
  - Optional light swing tuning (movement while swinging, lunge, tempo), mild by default.
  - Technically client-only with vanilla animations; it ships as Both, with the server's settings for everyone.
  - A per-move adrenaline bonus that the Adrenaline revamp can pay. The moves cost no adrenaline: the bar is kept for the trinket, which the player triggers (Trinket revamp).
  - Detect GCO, PPR, Cancel Animation Cancels and the quickstep mods, and warn or turn off the overlapping move.

### Sneak revamp (Revamp): Sneak XP on sneak attacks, much stealthier when standing still

**Coverage: partial.** The XP line is covered. SecondaryAttacks (1.0, updated 2026-09-29) grants Sneak XP whenever a hit triggers the vanilla backstab, detected on the victim's owner the way we planned it, and Goo's Combat Overhaul can award Sneak XP with its sneak bonus. Both ship inside large combat mods. SmartSkills does it too, but its Thunderstore build predates 1.0 (the GitHub source has a 1.0 fix). No mod makes a crouched player who stands still harder to see: current sneak mods scale movement speed, noise or the whole stealth curve with skill.

| Mod | Status | Notes |
|---|---|---|
| [SecondaryAttacks (sighsorry)](https://thunderstore.io/c/valheim/p/sighsorry/SecondaryAttacks/) | v1.2.14, updated 2026-09-29, 1.0: yes (tagged Deep North) | Mostly new secondary attacks (see Weapon revamp). Its General section also grants Sneak XP whenever any attack triggers the vanilla backstab (`Backstab Sneak Skill Raise Amount`, default 1, server-synced): a `Character.RPC_Damage` prefix and postfix on the victim's owner compare `m_backstabTime`, and an RPC sends the XP to the attacker's client. By default it also doubles the skill-based part of the crouched visibility reduction and the crouched speed at Sneak 100, and adds a knife "Sneak Ambush" that charges while crouched. Nothing depends on standing still. Tagged AI Generated, about 12.7K downloads. Source: [sighsorry1029/SecondaryAttacks](https://github.com/sighsorry1029/SecondaryAttacks). |
| [SmartSkills (Smoothbrain)](https://thunderstore.io/c/valheim/p/Smoothbrain/SmartSkills/) | v1.0.2, updated 2024-06-21, 1.0: unknown | Sneak raises backstab damage, and every hit on an unaware enemy (not alerted, no target) gives bonus Sneak XP (20 by default). The source decides this on the attacker's client (`Character.Damage` prefix), with no crouch check and no cooldown. The repo got "fixes for 1.0" on 2026-09-12 (source version 1.0.3), not yet released on Thunderstore. Open source: [blaxxun-boop/SmartSkills](https://github.com/blaxxun-boop/SmartSkills). About 120K downloads. |
| [SNEAKer (blacks7ar)](https://thunderstore.io/c/valheim/p/blacks7ar/SNEAKer/) | v1.1.8, updated 2026-09-15, 1.0: yes (tagged Deep North) | Sneak movement speed scales with Sneak skill; configurable Sneak XP multiplier; ServerSync. It rewards moving, not standing still. About 139K downloads. |
| [Sneaky Viking (Brutaliaa)](https://thunderstore.io/c/valheim/p/Brutaliaa/Sneaky_Viking/) | v1.0.0, updated 2026-09-24, 1.0: yes (tagged Deep North) | Walking, running, jumping and dodging make less noise as Sneak rises (up to 80% less at skill 100). ServerSync. Source: [Brutaliaa/SneakyViking](https://github.com/Brutaliaa/SneakyViking). About 100 downloads. |
| [SetUpSkills (Neocor)](https://thunderstore.io/c/valheim/p/Neocor/SetUpSkills/) | v0.1.0, updated 2026-09-22, 1.0: yes (built for 1.0.x) | Makes hard-coded skill numbers configurable, including the stealth curve at skill 100 (visibility in darkness, weight of light) and sneak stamina, through transpilers on `Player.UpdateStealth` and `Player.OnSneaking`. Nothing depends on movement. Source: [NeocorDK/SetUpSkills](https://github.com/NeocorDK/SetUpSkills). |
| [Goo's Combat Overhaul (gnls)](https://thunderstore.io/c/valheim/p/gnls/GoosCombatOverhaul/) | v2.1.2, updated 2026-09-26, 1.0: yes (targets 1.0.16) | A "Sneak" damage bonus against unaware targets that scales with Sneak skill and, per its README, can award Sneak XP. No stillness rule. Part of a large combat overhaul (see Weapon revamp); tagged AI Generated, closed source, about 8.6K downloads. |
| [Valheim Combat Overhaul (Kyresel / leseryk)](https://thunderstore.io/c/valheim/p/Kyresel/CombatOverhaul/) ([Nexus 591](https://www.nexusmods.com/valheim/mods/591)) | v1.7.8, updated 2021-04-22, 1.0: dead (deprecated) | Sneak XP for a successful sneak attack, the amount set by the weapon's backstab bonus and lower for projectiles, plus sneak-attack damage scaled by Sneak skill (half of vanilla at skill 0). Its README names early detection as a problem but leaves the detection formula unchanged. The reuploads are deprecated too: [HHPatch](https://thunderstore.io/c/valheim/p/NotMyMods/CombatOverhaul_HHPatch/) and blacks7ar's [CombatOverhaulREwrite](https://thunderstore.io/c/valheim/p/blacks7ar/CombatOverhaulREwrite/) (v1.2.5, 2025-03-11). |
| Related | | [ImpactfulSkills (MidnightMods)](https://thunderstore.io/c/valheim/p/MidnightMods/ImpactfulSkills/) (v0.20.2, updated 2026-09-28) raises sneak speed with skill and reduces sneak noise from Sneak 50. [BetterStealthIndicator (Jaybirds)](https://thunderstore.io/c/valheim/p/Jaybirds/BetterStealthIndicator/) (v0.4.16, updated 2026-09-27, tagged AI Generated) is a clearer stealth HUD. |

**Inspiration.**
- **Vanilla today:**
  - Sneak XP only comes from moving while crouched (`Player.OnSneaking`: 1 per second near unaware enemies, 0.1 otherwise). Standing still earns nothing.
  - The sneak attack is the backstab in `Character.RPC_Damage`, decided on the victim's owner: damage × the weapon's backstab bonus when the victim is not alerted, at most once per 300 s per victim. It gives no XP.
  - The stealth factor (`Player.UpdateStealth`) depends on crouching, light and Sneak skill only, never on movement, so holding still is no better than crawling.
- **Borrow:**
  - SecondaryAttacks' detection (compare `m_backstabTime` around `Character.RPC_Damage` on the victim's owner, then an RPC to the attacker), which confirms our design.
  - Kyresel's lower XP for projectiles. Kyresel also paid more XP for a bigger backstab bonus; ours scales by the victim's max HP instead. SmartSkills' rule is simpler but pays for any hit on an unaware enemy and ignores the vanilla cooldown.
- **Ours:**
  - A clear "hold still" reward, which no mod offers: after a second without moving, a crouched player's stealth factor drops (for example it halves), shown by the vanilla stealth bar and a status icon. A floor keeps it from stacking with other stealth effects (for example the Sneak tiers of New ability depending on skill level) into full invisibility. Ambushes and bow shots from cover become a real tactic, and monster perception stays untouched.
  - Sneak-attack XP in a small standalone mod, scaled by the victim's max HP with no floor (weak creatures pay almost nothing, training dummies nothing), for players who do not want a whole combat overhaul. It stays off when SecondaryAttacks, GCO or SmartSkills already pays sneak XP.

### Trinket revamp (Revamp): vanilla trinkets fired by the player with a key; the bar fills while fighting and never drains

**Coverage: partial.** No mod found lets the player fire a vanilla trinket: every trinket mod keeps the automatic pop or turns trinkets into passives. The other parts exist separately. RageNAdrenaline spends its own meters (not the vanilla bar) with a key, AdrenalineModifier can stop the drain, and Odin (Nexus, per search snippets) stops it while enemies are near. The passive-trinket mods that matched the earlier version of this idea (BetterTrinkets, Passive_Trinket_Modifiers, Balrond Battle Flow) now matter as conflicts.

| Mod | Status | Notes |
|---|---|---|
| [RageNAdrenaline (Jawlessjman665)](https://thunderstore.io/c/valheim/p/Jawlessjman665/RageNAdrenaline/) | v1.2.0, updated 2026-09-26, 1.0: yes (tagged Deep North Update) | The closest model, on meters of its own. Rage fills while enemies are near, drains when none are, plays a sound once when full, and is spent with a key (a Jötunn button with a gamepad binding). A boss-only Adrenaline meter is spent the same way and resets when you are hit. The default keys are F and G, and D-pad up and left: the vanilla Forsaken power, radial menu and hotbar inputs, and the button does not block them. Separate from the vanilla bar and trinkets. Source: [jawlessjman/RageNAdrenaline](https://github.com/jawlessjman/RageNAdrenaline) (MIT). About 16 downloads. |
| [AdrenalineModifier (mightywa33ior)](https://thunderstore.io/c/valheim/p/mightywa33ior/AdrenalineModifier/) | v1.0.1, updated 2025-10-05, 1.0: unknown | `AdrenalineDecayMultiplier` at 0 stops the drain. Its prefix on `Player.AddAdrenaline` scales every negative amount, though, so the miss and unblocked-hit penalties go too. The pop stays automatic. Source: [lukeadickinson/valhiem-adrenalinemodifier](https://github.com/lukeadickinson/valhiem-adrenalinemodifier) (MIT). About 3.2K downloads. |
| [KeepAdrenalineLonger (zopthemop)](https://thunderstore.io/c/valheim/p/zopthemop/KeepAdrenalineLonger/) | v1.0.0, updated 2026-06-05, 1.0: unknown | Only a longer delay (a flat 15 s) before the drain: the bar still empties between fights. About 270 downloads. |
| Odin ([Nexus 3188](https://www.nexusmods.com/valheim/mods/3188)) | version and date unknown, 1.0: unknown (Nexus, from search snippets) | An all-in-one admin and gameplay mod. Per search snippets: running, attacking and dodging turn stamina into adrenaline, blocks and parries add more, adrenaline does not decay while enemies are near, each trinket has a cooldown that survives a trinket swap, and each vanilla trinket has its own cost. The snippets mention no manual trigger. |
| [Balrond Battle Flow (Balrond)](https://thunderstore.io/c/valheim/p/Balrond/balrond_battle_flow/) | see Adrenaline revamp | Conflict. Every trinket becomes a passive that scales with the bar (0% at 0 adrenaline, 100% at 100). At the max, a Surge holds the bar for a short time, then an Overcharge drains it with bonuses and drawbacks. |
| [BetterTrinkets (Schwifty)](https://thunderstore.io/c/valheim/p/Schwifty/BetterTrinkets/) | v1.0.0, updated 2026-02-28, 1.0: unknown | Conflict. A weaker always-on passive, doubled for the trinket's normal duration by a full bar. Config for the 13 pre-Deep North trinkets. Client-side. About 2.7K downloads. Unofficial 1.0 patch: [BetterTrinkets Deep North Compat (Gabadur)](https://thunderstore.io/c/valheim/p/Gabadur/BetterTrinkets_Deep_North_Compat/) (v1.0.9, updated 2026-09-18, 1.0: yes), which leaves out the two Deep North trinkets. |
| [Passive_Trinket_Modifiers (Gabadur)](https://thunderstore.io/c/valheim/p/Gabadur/Passive_Trinket_Modifiers/) | v0.35.1, updated 2026-09-27, 1.0: yes (supports the Deep North Neckstabber and Witch Crown) | Conflict. A passive section and a "doubled" section per trinket, many `SE_Stats` fields, and a global adrenaline gain multiplier. AI Generated tag, about 160 downloads. |
| [MultiTrinket (QQMZR)](https://thunderstore.io/c/valheim/p/QQMZR/MultiTrinket/) | v1.0.1, updated 2026-03-04, 1.0: unknown | For extra trinket-slot mods (needs ExtraSlots and ExtraSlots Custom Slots): uses the highest cost of the equipped trinkets and fires them all together. The linked repository has only a README. About 580 downloads. |
| [Surge (Ezomic)](https://thunderstore.io/c/valheim/p/Ezomic/Surge/) | see Adrenaline revamp | Per-trinket costs, edited live on `SharedData.m_maxAdrenaline`. Works with ours: the bar follows the current max. |
| Related | | [ClassTrinkets (JamesJonesTV)](https://thunderstore.io/c/valheim/p/JamesJonesTV/ClassTrinkets/) (v1.0.2, 2026-02-12, 1.0: unknown) adds 40 static-stat trinkets: new content, not a rework. RPG Equipment ([Nexus 3992](https://www.nexusmods.com/valheim/mods/3992), per search snippets) has an option that divides the combined trinket cost by the number of trinkets equipped. [Valheim Level System (Lorska)](https://thunderstore.io/c/valheim/p/Lorska/Valheim_Level_System_by_Lorska/) (v0.99.9, 2026-09-14, 1.0: yes, tagged Deep North Update; also Nexus 2797): its changelog adds an "AdrenalineHoldover" that counts the bar as full for a set time after a trinket fires, and a Tactician class that keeps 20% of the adrenaline used. |

**Inspiration.**
- **Vanilla:**
  - A trinket adds adrenaline capacity (`SharedData.m_maxAdrenaline`), and its effect (`m_fullAdrenalineSE`, 30 to 120 s per the wiki) fires by itself the moment the bar is full (`Player.AddAdrenaline`), often at the end of a fight or while running away.
  - The bar drains 6 to 10 s after the last gain, so the next fight starts from zero.
  - The Forsaken power is the vanilla model of a power on a key, with a "not ready" message while it cools down.
- **Borrow:**
  - RageNAdrenaline's key trigger and its one-time "full" sound;
  - the Forsaken power's "not ready" message, and the vanilla bar flash;
  - MultiTrinket's "fire every equipped trinket together", which the vanilla pop already does.
- **What they get wrong:**
  - RageNAdrenaline's default keys clash with vanilla actions (both fire), and its meters are separate from the trinkets.
  - AdrenalineModifier's zero decay also drops the penalties, and the pop stays automatic.
  - KeepAdrenalineLonger still empties the bar between fights.
  - The passive mods change what a trinket gives, which the sheet now asks to keep as it is.
- **Ours:**
  - Vanilla trinket effects and costs, with no lasting item data change.
  - A bar that fills while fighting and never drains (the Adrenaline revamp).
  - A full bar that waits, with a flash, a message and a tooltip line naming the key.
  - One key (and a gamepad combination per layout) that fires every equipped trinket through the vanilla pop.
  - Ships as Both: the server refuses players without the mod, and its settings apply to everyone.

### Adrenaline revamp (Revamp): faster build-up that is fair across weapons, no decay, and a full bar every fight

**Coverage: partial.** Number tweaks exist: gain, decay and decay-delay multipliers, a longer hold before decay, and trinket costs. Balrond Battle Flow also reworks what adrenaline does for trinkets and weapons. None of the mods found adds income while fighting (TastyAdrenaline trickles adrenaline in only while Tasty Mead is active), makes gains fair across weapon speeds, or aims at one full bar per fight.

| Mod | Status | Notes |
|---|---|---|
| [Balrond Battle Flow (Balrond)](https://thunderstore.io/c/valheim/p/Balrond/balrond_battle_flow/) | v0.1.2, updated 2026-09-20, 1.0: yes (tagged Deep North Update) | "Adrenaline/Trinket system rework". Weapon-type bonuses grow with the bar, and trinkets scale from 0% power at 0 adrenaline to 100% at 100 adrenaline. A Surge holds the bar at max, then an Overcharge drains it with bonuses and drawbacks. Its README describes what adrenaline does, not how it is earned. Server and client, synced, no config yet. No source link (Discord only). About 2.7K downloads. |
| [KeepAdrenalineLonger (zopthemop)](https://thunderstore.io/c/valheim/p/zopthemop/KeepAdrenalineLonger/) | v1.0.0, updated 2026-06-05, 1.0: unknown | Replaces the 6 to 10 s decay delay with a flat 15 s, through a transpiler on `Player.AddAdrenaline`. Source: [zopthemop/valheim-keepadrenalinelonger](https://github.com/zopthemop/valheim-keepadrenalinelonger) (AGPL-3.0). About 270 downloads. |
| [AdrenalineModifier (mightywa33ior)](https://thunderstore.io/c/valheim/p/mightywa33ior/AdrenalineModifier/) | v1.0.1, updated 2025-10-05, 1.0: unknown | Multipliers for gain, decay and decay delay: a prefix on `Player.AddAdrenaline` scales gains and decay, and a postfix scales the decay timer. Source: [lukeadickinson/valhiem-adrenalinemodifier](https://github.com/lukeadickinson/valhiem-adrenalinemodifier) (MIT). About 3.2K downloads. |
| [Surge (Ezomic)](https://thunderstore.io/c/valheim/p/Ezomic/Surge/) | v1.0.3, updated 2026-08-16, 1.0: unknown | Sets each trinket's adrenaline cost (multiplier, flat value or per trinket) by editing `SharedData.m_maxAdrenaline` live. Its README lists the 13 pre-1.0 vanilla costs read from the game (10 to 100) and says the player's own base is 0. Source: [Ezomic/valheim-surge](https://github.com/Ezomic/valheim-surge) (MIT). AI Generated tag, about 170 downloads. |
| [RageNAdrenaline (Jawlessjman665)](https://thunderstore.io/c/valheim/p/Jawlessjman665/RageNAdrenaline/) | v1.2.0, updated 2026-09-26, 1.0: yes (tagged Deep North Update) | Two extra meters from Terraria's Calamity mod. Rage fills while enemies are near and is spent with a key. A boss-only Adrenaline meter resets when you are hit. Both are separate from the vanilla bar and trinkets. Needs Jötunn. Source: [jawlessjman/RageNAdrenaline](https://github.com/jawlessjman/RageNAdrenaline) (MIT), also on Nexus (3362). About 16 downloads. |
| [GrindstoneSkills (MilkyTeam)](https://thunderstore.io/c/valheim/p/MilkyTeam/GrindstoneSkills/) | v0.10.0, updated 2026-09-29, 1.0: likely | Its Defense skill gives up to 25% more adrenaline from blocks and parries, and 25% less lost to unblocked hits, through a prefix on `Player.AddAdrenaline`. Source: [geraldjglasgow/ValheimMods](https://github.com/geraldjglasgow/ValheimMods/tree/main/GrindstoneSkills). About 640 downloads. |
| Related | | [TastyAdrenaline (RiftWood)](https://thunderstore.io/c/valheim/p/RiftWood/TastyAdrenaline/) (v1.0.1, 2026-09-25, 1.0: yes) gives +1 adrenaline every 2 s while Tasty Mead is active (AI Generated, about 40 downloads). [BetterTrinkets](https://thunderstore.io/c/valheim/p/Schwifty/BetterTrinkets/) and [Passive_Trinket_Modifiers](https://thunderstore.io/c/valheim/p/Gabadur/Passive_Trinket_Modifiers/) (see Trinket revamp) make trinkets work without a full bar; the latter also has a global gain multiplier. [ForsakenPowerOverhaul](https://thunderstore.io/c/valheim/p/momos3939/ForsakenPowerOverhaul/) keeps the vanilla +10 adrenaline when a power is activated (its README). |

**Inspiration.**
- **Vanilla today:**
  - No trinket, no bar. The player's own max is 0 (Surge's README; the code default is 100), so gains are ignored.
  - Gains per the wiki:
    - +1 per hit for one-handed weapons;
    - +2 per hit for two-handed weapons, polearms, bows and crossbows;
    - parry +5, perfect dodge +5, block +1 or +2, stagger +3, Forsaken power +10.
  - The bar drains after 6 to 10 s without a gain, at 1 to 4 per second.
  - Why bows fall behind:
    - melee pays per character hit in a swing (`Attack.DoMeleeAttack`);
    - an arrow pays once, with no enemy multiplier (`Projectile.OnHit`);
    - a bow's draw only gets shorter with skill (`Humanoid.GetAttackDrawPercentage`).
  - A Steam thread ("trinkets need a rework", 2025-09) says the build-up is too slow and asks for passives. A player's reply there explains that the decay delay shrinks from 10 to 6 s as the bar fills, and that an AoE weapon that staggers several mobs earns several chunks per hit.
- **Borrow:**
  - KeepAdrenalineLonger's longer hold and AdrenalineModifier's decay multiplier, taken to the end: no decay while a trinket is equipped;
  - RageNAdrenaline's "fills while enemies are near" and TastyAdrenaline's per-tick trickle, as income;
  - Battle Flow's framing of adrenaline as combat momentum.
- **What they get wrong:**
  - The number mods scale every source alike, so bows stay behind swords.
  - Battle Flow changes what the bar does, not how fast it fills.
  - RageNAdrenaline adds a second meter instead of fixing the vanilla one, and TastyAdrenaline's income depends on a mead, not on fighting.
- **Ours:**
  - Income while fighting that grows with the number of enemies engaged.
  - Weapon gains per second of attacking, so every weapon fills at the same pace.
  - A kill bonus.
  - No decay, so chained lone fights add up.
  - Tuned so the bar of a mid-cost trinket fills about once per real fight; the player then triggers the trinket (Trinket revamp).
  - Technically client-only; ships as Both, so the server refuses players without the mod and its settings apply to everyone.

### Boss power revamp (Revamp): slightly stronger Forsaken powers

**Coverage: full.** Longer buffs and shorter cooldowns are one config line in ValheimPlus and ForsakenPowersPlusRemastered, and BossRules can edit any stat per power. Only a small, curated "slightly stronger" preset is missing.

| Mod | Status | Notes |
|---|---|---|
| [ValheimPlus, Grantapher fork](https://thunderstore.io/c/valheim/p/Grantapher/ValheimPlus_Grantapher_Temporary/) | v10.2.0, updated 2026-09-20, 1.0: yes (tagged Deep North Update) | `guardianBuffDuration` (default 300) and `guardianBuffCooldown` (default 1200) in the `[Player]` section of `valheim_plus.cfg`, synced with the rest of V+. About 360K downloads. |
| [ForsakenPowersPlusRemastered (turbero)](https://thunderstore.io/c/valheim/p/turbero/ForsakenPowersPlusRemastered/) | v2.0.4, updated 2026-09-10, 1.0: yes (tagged Deep North Update) | Duration and cooldown config (defaults 300 and 1200 s), cycling through earned powers with a key, reset, passive mode and stacking. ServerSync. Source: [Turbero/ForsakenPowersPlusRemastered](https://github.com/Turbero/ForsakenPowersPlusRemastered). About 17K downloads. |
| [BossRules (sighsorry)](https://thunderstore.io/c/valheim/p/sighsorry/BossRules/) | v1.1.3, updated 2026-09-29, 1.0: yes (tagged Deep North Update) | YAML edits per power: duration, cooldown, regen, damage, armor, speed, skills, resistances, and the adrenaline gained on activation. Its README compares the vanilla rows (300 s / 1200 s for all seven powers, from DataForge's effects reference) with its own preset. That preset is a full rework (31 s buff, 120 s cooldown), not a slight boost. Also remote power rotation and boss-altar rules. AI Generated tag, about 8.5K downloads. |
| [EasyVitals (s6652289)](https://thunderstore.io/c/valheim/p/s6652289/EasyVitals/) | v1.0.0, updated 2026-09-17, 1.0: likely | Boss power duration ×2 (the 20-minute cooldown unchanged) among other multipliers. Client-only, no sync. AI Generated tag, about 120 downloads. |
| [ForsakenPowersRadius (turbero)](https://thunderstore.io/c/valheim/p/turbero/ForsakenPowersRadius/) | v1.0.1, updated 2026-09-12, 1.0: yes (tagged Deep North Update) | Raises the 10 m sharing radius (10 to 200 m), with a server-enforced option. The linked GitHub repository returns 404. About 600 downloads. |
| [PassivePowers (Smoothbrain)](https://thunderstore.io/c/valheim/p/Smoothbrain/PassivePowers/) | v1.1.5, updated 2026-02-05, 1.0: unknown | The passive alternative: weaker permanent powers, with an optional short burst on activation. The repository ([blaxxun-boop/PassivePowers](https://github.com/blaxxun-boop/PassivePowers)) was pushed on 2026-09-11. About 490K downloads. |
| Related | | Duration and cooldown config without sync: [ForsakenPowersRevisited (Gerbesh)](https://thunderstore.io/c/valheim/p/Gerbesh/ForsakenPowersRevisited/) (v1.0.0, 2026-01-14, 1.0: unknown). Passive models: [ForsakenPowerOverhaul](https://thunderstore.io/c/valheim/p/momos3939/ForsakenPowerOverhaul/) (v2.2.0, 2026-09-10, 1.0: yes), [ProgressivePowers](https://thunderstore.io/c/valheim/p/MidnightMods/ProgressivePowers/) (v0.3.3, 2026-09-14, 1.0: yes), [PassivePowers (Jawlessjman665)](https://thunderstore.io/c/valheim/p/Jawlessjman665/PassivePowers/) (v1.2.3, 2026-09-27, 1.0: yes). Shorter waits: [ForsakenRest](https://thunderstore.io/c/valheim/p/JXR_Creations/ForsakenRest/) and [PowerCooldownOnSleep](https://thunderstore.io/c/valheim/p/VibeODrone/PowerCooldownOnSleep/) clear or advance the cooldown when you sleep. |

**Inspiration.**
- **Vanilla:**
  - A power is one `SE_Stats` (`GP_*`) with `m_ttl` (buff length) and `m_cooldown`.
  - `Player.ActivateGuardianPower` shares it by hash with players within 10 m, adds 10 adrenaline, and starts the cooldown from the activator's copy.
  - Each receiver uses its own `ObjectDB` copy of the effect, so data edits are enough.
- **What they get wrong:**
  - The config mods leave the "how much" to the user.
  - BossRules' preset changes the powers' character.
  - EasyVitals doubles the duration without sync.
- **Ours:**
  - A tiny data-only mod with a curated preset: a bit longer, a bit sooner, a bit stronger.
  - The same model and radius, with per-power multipliers applied from stored originals.
  - The vanilla tooltip shows the new numbers.
  - Ships as Both: the server refuses players without the mod, and its multipliers apply to everyone.
  - Otherwise, recommend a ValheimPlus or ForsakenPowersPlusRemastered config and skip this idea.

### Blood trinket (New): drop to 15% HP when triggered, then heal it back over a few seconds after 10 s

**Coverage: none.** No trinket or item was found that sets the player's HP low on purpose for a timed window. The related mods reward or protect low HP.

| Mod | Status | Notes |
|---|---|---|
| [GrindstoneSkills (MilkyTeam)](https://thunderstore.io/c/valheim/p/MilkyTeam/GrindstoneSkills/) | v0.10.0, updated 2026-09-29, 1.0: likely | Defense skill. "Desperation" doubles damage reduction below 25% health. "Last Stand" (level 100) leaves you at 1 HP and untouchable for 2 s on a killing blow. Would soften our window. |
| [UndyingAmulet (Gamesmodding)](https://thunderstore.io/c/valheim/p/Gamesmodding/UndyingAmulet/) | v1.1.1, updated 2026-09-27, 1.0: yes (needs Valheim 1.0.15 or newer) | Cheat death from the inventory: a lethal hit restores 50% health with 30 s of immunity, on a 30 min cooldown. While it is ready, it also gives +50% sprint speed, half stamina costs and 20% lifesteal. Needs Jötunn. AI Generated tag, about 130 downloads. |
| [EpicLoot (RandyKnapp)](https://thunderstore.io/c/valheim/p/RandyKnapp/EpicLoot/) | v0.14.13, updated 2026-09-24, 1.0: yes | "LowHealth" variants of magic effects apply at 30% HP or less by default. Blood shardstones such as Bloodrage (see Blood stone revamp in the backlog). About 2.26M downloads. |
| [JardsAdditions (jard_hu)](https://thunderstore.io/c/valheim/p/jard_hu/JardsAdditions/) | v2.1.0, updated 2025-12-15, 1.0: unknown | Optional bloodstone vampirism (off by default) that grows with missing HP. About 2.2K downloads. |
| [ValheimLegends (momos3939)](https://thunderstore.io/c/valheim/p/momos3939/ValheimLegends/) | v0.7.12, updated 2026-09-29, 1.0: yes | Class mod. The Berserker trades health for speed and damage. AI Generated tag, about 4.6K downloads. |

**Inspiration.**
- **Vanilla:**
  - Bloodstone weapons already reward low HP: `Attack.ModifyDamage` adds `m_damageMultiplierPerMissingHP` per missing HP (wiki: 0.2%).
  - Blood-magic costs are a share of current HP.
  - Going low today means casting a blood staff or taking hits.
- **Borrow:**
  - EpicLoot's 30% "low health" threshold, as a reference point.
  - Not borrowed: GrindstoneSkills' and UndyingAmulet's low-HP safety nets. The window has no 1 HP floor, so a big hit kills.
- **Ours:**
  - A trinket that, when the player triggers it, makes the bloodstone bonus peak for up to 10 s (heals still work, so the player can end it sooner), then gives the HP back over a few seconds, with no eitr and no weapon swap.
  - It pairs with the Blood stone revamp's blood rite: nothing is clamped in the window, so that idea's lifesteal works there too.
  - With the Trinket revamp, the player picks the moment of the drop, for example right before a bloodstone combo.

### Ballista revamp (Revamp): turns and shoots faster, aims better, several targets per ballista

**Coverage: partial.** ReBallista (first released 2026-09-13) now does much of "better aim, turns faster" for the vanilla ballista: lead from velocity and flight time, faster tracking, no spread, a lower aim on short creatures, and homing bolts. BetterBallistas, ZenWorldSettings and MultiTargetBallista lift the one-trophy limit. For the vanilla ballista, no mod says it keeps its target until it dies, prefers a target it can hit, or keeps tracking while it reloads (vanilla: the closest creature, re-picked every second). ValheimFortress's Automated Ballista and Zarkow Turret Defense have such target rules, but only for their own new pieces.

| Mod | Status | Notes |
|---|---|---|
| [ReBallista (Skarif)](https://thunderstore.io/c/valheim/p/Skarif/ReBallista/) | v1.0.416, updated 2026-09-29, 1.0: yes (tagged Deep North Update) | Never targets players or tames. Leads shots from the target's velocity and the bolt's flight time. Reworked turning and acceleration for a fast traverse. Lowers the aim point for short creatures (Ticks, Necks, Leeches). Removes spread. Bolts home in, up to 65 degrees per second within 25 m. About 2.5K downloads; no source link. |
| [BetterBallistas (Neobotics)](https://thunderstore.io/c/valheim/p/Neobotics/BetterBallistas/) | v1.0.0, updated 2026-09-11, 1.0: yes | Number of target trophies per ballista (its README notes that vanilla code already supports several). Targeting toggles for enemies, tames and players, detection range, firing arc up to 180 degrees, ammo cap. Scan arc, sweep interval and turn rate, but the turn rate only applies while scanning with no target. About 21K downloads. |
| [ZenWorldSettings (ZenDragon)](https://thunderstore.io/c/valheim/p/ZenDragon/ZenWorldSettings/) | v1.13.1, updated 2026-09-23, 1.0: yes (tagged Deep North Update) | Turret settings: max trophy targets (default 2, vanilla 1), no player or tame targeting, max ammo. With ZenHoverItem, the assigned trophies show as icons on hover. Part of a larger world-settings mod, about 100K downloads. |
| [MultiTargetBallista (Marchiori)](https://thunderstore.io/c/valheim/p/Marchiori/MultiTargetBallista/) | v1.0.5, updated 2026-09-28, 1.0: yes (tagged Deep North Update) | Building a chest on a ballista gives it an inventory: trophies placed inside set several targets, and ammo goes in too. This replaces the vanilla hotbar flow for trophies and ammo. Tagged AI Generated, about 80 downloads. |
| [Turrets Redo (MrGay)](https://thunderstore.io/c/valheim/p/MrGay/Turrets_Redo/) | v0.0.4, updated 2026-09-21, 1.0: yes (tagged Deep North Update) | Turn speed, projectile speed, view distance and attack rate as percentages of vanilla. Lead shots from position, collider centre, velocity and projectile speed. Spread off, or compensated for range. Ignores players, tames and friendly Dvergr (optional projectile immunity). Infinite ammo; bolts no longer hit their own turret. Needs ConditionalConfigSync. Tagged AI Generated, about 200 downloads. |
| [ValheimPlus (Grantapher fork)](https://thunderstore.io/c/valheim/p/Grantapher/ValheimPlus_Grantapher_Temporary/) | v10.2.0, updated 2026-09-20, 1.0: yes (tagged Deep North Update) | `[Turret]` section, off by default: turn rate, attack cooldown, view distance, projectile velocity and accuracy, ignore players, unlimited ammo. |
| [BottleShips (sighsorry)](https://thunderstore.io/c/valheim/p/sighsorry/BottleShips/) | v1.1.14, updated 2026-09-26, 1.0: yes (tagged Deep North Update) | "Ballista Targeting Tweaks": trophy targets come first but other hostiles stay valid; players, tames and `PlayerSpawned` creatures are never picked. Also an ammo capacity multiplier. |
| [TurretRevamped (blacks7ar)](https://thunderstore.io/c/valheim/p/blacks7ar/TurretRevamped/) | v1.0.4, updated 2026-09-17, 1.0: yes (tagged Deep North Update) | Attack cooldown, max ammo, horizontal and vertical angle; reloads from nearby chests; no player or tame targeting. About 26K downloads. |
| [ValheimFortress (MidnightMods)](https://thunderstore.io/c/valheim/p/MidnightMods/ValheimFortress/) | v0.37.2, updated 2026-09-18, 1.0: yes (tagged Deep North Update) | Wave-arena mod with its own "Automated Ballista" piece, driven by its own component (`VFTurret`); the vanilla ballista is unchanged. From its source ([MidnightsFX/Valheim_Fortress](https://github.com/MidnightsFX/Valheim_Fortress), `VFTurret.cs`): it keeps its target until the target dies, picks the closest valid enemy it has a clear line to (raycast), skips players, tames and (by config) passive animals, and can check the line of fire again before each shot. About 107K downloads. |
| [Zarkow Turret Defense (DigitalSoftware)](https://thunderstore.io/c/valheim/p/DigitalSoftware/Zarkow_Turret_Defense/) | v1.4.1100, updated 2026-09-16, 1.0: yes (tagged Deep North Update) | New sci-fi turret pieces, not the ballista. Threat-based target choice: creatures of the ForestMonsters faction that are not alerted are only attacked within half the range; targets are re-acquired every 0.4 s, closest attackable first; threats behind walls are tracked but not shot; some missile turrets split their payload between several targets. About 87K downloads. |
| Related | | Friendly fire only: [ServersideQoL SmartDefense (ArgusMagnus)](https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL_SmartDefense/) (v2.1.0, updated 2026-09-25, 1.0: likely; server-side only, also reloads from containers) and [ImFRIENDLY DAMMIT (Azumatt)](https://thunderstore.io/c/valheim/p/Azumatt/ImFRIENDLY_DAMMIT/) (v1.1.9, updated 2025-03-14, 1.0: dead, deprecated). [G3A3 (G3A1)](https://thunderstore.io/c/valheim/p/G3A1/G3A3/) (v1.9.75, updated 2026-09-29, 1.0: likely) lists a "smart ballista" setting in a large gameplay pack, without details. [NoobBallista (GsiX)](https://thunderstore.io/c/valheim/p/GsiX/NoobBallista/) (v1.3.9, updated 2024-08-06, 1.0: unknown) is a separate ballista piece that ignores players and tames and, since 1.3.1, abandons a target when its line is blocked. |

**Inspiration.**
- **Why vanilla ballistas miss** (from the code, with ServersideQoL's 1.0 component dump of `piece_turret`; details in `docs/game/combat.md` §12):
  - The last degrees of turning slow down in proportion to the remaining angle, and the ballista only fires within about 1.6°. A creature whose bearing changes faster than about 6° per second is followed but never shot.
  - The turret does not move during its 2 s reload.
  - The lead uses only the x and y distance and is doubled.
  - The aim is taken from the turret body's pivot, while the bolt leaves from the eye.
  - The target is re-picked every second, so a closer creature steals it.
- **Borrow:**
  - ReBallista's lower aim point for short creatures, and optional spread removal.
  - BottleShips' "trophy kinds first, other hostiles after" as an option.
  - Trophy icons on hover (ZenHoverItem) once a ballista holds several trophies.
  - BetterBallistas' finding: vanilla code already handles several trophies, only `m_maxConfigTargets = 1` blocks it.
  - The target rules of ValheimFortress's Automated Ballista (keep the target until it dies, pick the closest enemy with a clear line) and Zarkow's threat levels.
- **What they skip or get wrong:**
  - No mod applies such target rules to the vanilla ballista, and none keeps it tracking during the reload.
  - ReBallista's homing bolts fix misses by bending the bolt, which does not look vanilla.
  - MultiTargetBallista replaces the vanilla trophy flow with a chest.
- **Ours:** vanilla-looking ballistas that turn visibly faster, lead correctly, keep a target until it dies and accept several trophies through the vanilla hotbar flow. Friendly fire stays with the mods above.

### Mob AI revamp (Revamp): weak enemies leave strong players alone, packs flee when their leader dies

**Coverage: partial.** The first two lines are now well covered. TruePassiveMobs (released 2026-09-28) does almost exactly them, including boars and necks that leave you alone and enemies that fight back when hit, and FearMe, Odin's Ótti and the unreleased 0.4.0 source of The Mark of Oden also make outclassed enemies leave you alone. Nobody makes a pack flee when its leader dies. The closest are Odin's Ótti, whose pack courage drops when a big ally is gone (but a provoked pack fights to the death), The Mark of Oden, where a creature in a fight breaks sooner as its packmates fall, and Monster AI Tweaks, where the last member of a small group flees (Nexus snippet).

| Mod | Status | Notes |
|---|---|---|
| [TruePassiveMobs (lhoffl)](https://thunderstore.io/c/valheim/p/lhoffl/TruePassiveMobs/) | v1.1.0, updated 2026-09-28, 1.0: yes (built for 1.0) | Two features. Passive creatures (Lox, Asksvin, Moose, Boar, Hen, Neck, Bjorn by default) ignore players until damaged, then fight back for a while; skittish and territorial variants. Enemies flee from a player whose gear outclasses them: their strongest attack after your armor and resistances deals at most 10% of your max HP, and/or your weapon kills them in 3 hits or fewer, counting stars, world difficulty and player count. Attacked enemies fight back; raid creatures never flee. Stores each player's combat profile in the player ZDO and prefix-skips `MonsterAI.UpdateAI` (with a `BaseAI.UpdateAI` reverse patch). Needs Jötunn; required on the server and every client. MIT: [lhoffl/TruePassiveMobs](https://github.com/lhoffl/TruePassiveMobs). About 200 downloads. |
| [FearMe (tulivu)](https://thunderstore.io/c/valheim/p/tulivu/FearMe/) | v1.0.1, updated 2026-09-19, 1.0: yes (updated for Deep North) | Enemies compare your equipped gear tier (per-biome config) with their own. "Cautious" ignores you (a `BaseAI.FindEnemy` postfix returns no target); "Afraid" flees (a transpiler adds a flee branch just before the `m_fleeIfHurtWhenTargetCantBeReached` check in `MonsterAI.UpdateAI`). MIT: [tulivu/ValheimMods](https://github.com/tulivu/ValheimMods/tree/main/src/FearMe). About 11K downloads. |
| [Odin's Ótti (DrakosDJ)](https://thunderstore.io/c/valheim/p/DrakosDJ/OdinsOtti/) | v1.0.1, updated 2026-09-12, 1.0: likely | Outmatched or outnumbered enemies calmly path around your party. Party threat comes from armor, weapon, skills, food and buffs plus nearby players, tames and summons; enemy courage from the combined HP of allies within 20 m, with a bonus near their spawner. Hitting one provokes allies within 25 m, who then fight to the death. Crouching hides your threat. Tagged AI Generated, about 200 downloads, no public source found. |
| [CowardlyGreydwarfs (omnipeach)](https://thunderstore.io/c/valheim/p/omnipeach/CowardlyGreydwarfs/) | v1.0.0, updated 2026-09-22, 1.0: yes (tagged Deep North) | Greylings and greydwarfs flee after a heavy hit or at low health, and come back when they feel strong enough. About 60 downloads. |
| [FleeOnSight (Revel)](https://thunderstore.io/c/valheim/p/Revel/FleeOnSight/) ([Nexus 2764](https://www.nexusmods.com/valheim/mods/2764)) | v1.1.1, updated 2024-05-11, 1.0: unknown | Listed creatures flee as soon as they are alerted once a boss is dead (global key). Default: Greylings after Eikthyr, Greydwarfs after the Elder. Option to keep fighting during raids. About 3.7K downloads. |
| Monster AI Tweaks ([Nexus 758](https://www.nexusmods.com/valheim/mods/758), [Thunderstore](https://thunderstore.io/c/valheim/p/tweaks/MonsterAITweaks/)) | v0.4.0 on Thunderstore, updated 2022-11-05, 1.0: unknown (Nexus search snippets show activity into 2026) | Per-monster target preferences, alertness, fire fear, sight and hearing ranges. The Nexus version adds flee and chase behaviour by diet and, per a Nexus snippet, groups of three or fewer fight a larger threat until one is left, which then flees. |
| [The Mark of Oden (PicSoul)](https://github.com/PicSoul/TheMarkOfOden) ([Thunderstore](https://thunderstore.io/c/valheim/p/PICS0UL/TheMarkOfOden/)) | v0.1.0, updated 2026-09-16, 1.0: dead (Thunderstore package deprecated; the GitHub source is at 0.4.0, pushed 2026-09-24, not released) | Creatures judge threat from **bosses you helped kill and species kill count, not gear**. In the 0.4.0 source, a creature you outrank leaves you alone (it neither attacks nor runs); hit it and it fights back until, badly hurt, its nerve breaks and it flees, sooner as its packmates fall. Hunted animals (boar, neck, wolf, lox) may leave you alone but never break once provoked. Night courage, raid and boss-add exemptions, nameplate states (wary, fleeing, provoked, unafraid). MIT. The Thunderstore upload is tagged AI Generated. |
| Related | | [MonsterDB (RustyMods)](https://thunderstore.io/c/valheim/p/RustyMods/MonsterDB/) (v0.4.0, updated 2026-09-20, 1.0: likely) edits the vanilla `MonsterAI` flee fields per creature. [Cautious Creatures (coemt)](https://thunderstore.io/c/valheim/p/coemt/Cautious_Creatures/) (deprecated, 2023) made up to six creatures behave like deer. |

**Inspiration.**
- **Vanilla:**
  - The only "this player is too strong" rule is the Crown of Valheim: non-boss creatures that target a player in crown mode flee from them (`MonsterAI.m_crownFearRange`). The other flee settings (`m_fleeIfLowHealth`, `m_fleeIfNotAlerted`, `m_fleeIfHurtWhenTargetCantBeReached`) ignore who the player is.
  - Creatures have no leader or group link, and a death is only seen on the dying creature's owner.
- **Differs from** The Mark of Oden's "progression, not gear" model: the sheet asks for gear ("fully geared from Ashlands"), so ours compares the creature's best hit with the player's real armor and max HP.
- **Borrow:**
  - TruePassiveMobs' "can it hurt you" test (the enemy's best hit after your armor, against your max HP) and its player profile stored in the player ZDO, which works whoever owns the creature.
  - FearMe's "cautious" state: ignore the player without running away.
- **Ours adds:**
  - The rout: kill a Troll, a Greydwarf Shaman or a Greydwarf Brute and the greydwarfs around it break and flee for a while. Nobody does this.
  - Weak creatures calmly keep their distance while they sense the player (a sneaking player can still close in) instead of fleeing in panic, and fight back when hit by a player or cornered ("except if frightened"). Bosses, tames, raid creatures and training dummies are never affected.
- **Build or recommend:** TruePassiveMobs already ships the first two lines. We could recommend it and build only the rout, or build both in one mod, since they share the flee plumbing.
- The AI runs on whichever client owns each creature, so all logic must work there: player strength travels in the player ZDO, and the leader's death as an RPC to each follower's owner.

### Training dummies revamp (Revamp): fight hostile creatures, ON/OFF switch, a dummy per weapon type

**Coverage: none.** No mod makes dummies fight creatures, switches them off, or gives them other weapons. TouchGrass goes the other way (dummies can hunt players at night) and lets you change one dummy's damage type, which is the closest thing to "a dummy per weapon type". This idea also absorbs More training dummies (cancelled in the idea sheet; see that section for the utility variants).

| Mod | Status | Notes |
|---|---|---|
| [TouchGrass (sighsorry)](https://thunderstore.io/c/valheim/p/sighsorry/TouchGrass/) | v1.0.7, updated 2026-09-10, 1.0: yes (tagged Deep North Update) | Using a dummy opens a window that sets that dummy's damage amount and type. Config for dummy health and recipe. Optional "night aggro" slides dummies toward players within 16 m (they cannot walk). Crowding limit (by default a 5th dummy within 4 m is refused). DPS and XP meter, stationary skill fatigue. Tagged AI Generated, about 1.9K downloads. |
| [OdinTrainingPlace (OdinPlus)](https://thunderstore.io/c/valheim/p/OdinPlus/OdinTrainingPlace/) | v1.6.6, updated 2026-09-19, 1.0: yes | Wooden dummy and a mechanical dummy for blocking practice, among other training pieces. None of them attacks creatures. About 248K downloads. |
| [DPS (JereKuusela)](https://thunderstore.io/c/valheim/p/JereKuusela/DPS/) | v1.7.0, updated 2026-09-10, 1.0: likely | Console commands to spawn, reset and kill dummies, with configurable resistances and status effects. A test tool, not a gameplay feature. |

**Inspiration.**
- **Vanilla T.W.I.G.** (ServersideQoL's 1.0 component dump and the wiki): `piece_TrainingDummy` is itself a `Humanoid` with `MonsterAI` and `Piece`. It has 2,500 HP and regenerates fully in 30 s, cannot walk, sees 30 m up to 90° off its facing, hears nothing, and only attacks once alerted (10 m alert range). `m_aiSkipTarget` is true, so creatures never pick it on their own. Every attack deals 1 damage (wiki).
- **No prior art** for dummies that fight creatures, can be switched off or wield other weapons. One faction rule (`BaseAI.IsEnemy`) and a longer alert range used against creatures alone make dummies fight hostile creatures, while players and tames still meet the vanilla dummy; creatures never pick a dummy on their own (`m_aiSkipTarget`) and only fight back when a dummy hits them while they have no target.
- **Borrow** from TouchGrass: per-dummy settings on interaction (on the alternate key, so E stays the ON/OFF switch; they default to vanilla) and the crowding limit, which also stops dummy walls.
- **Watch out for:** base-defence balance (tanky, regenerating decoys), loot farms (a creature killed by a dummy drops its loot), TouchGrass using the same interact key and moving dummies at night, and our other combat ideas, which must ignore dummies (adrenaline income, the Mob AI weakness test, Sneak XP on backstabs).

### Increase base HP/Stamina with stats (Revamp): running, jumping, and so on

**Coverage: full.** VitalityRewrite is almost exactly this idea. Stamina-only and RPG-attribute variants also exist.

| Mod | Status | Notes |
|---|---|---|
| [VitalityRewrite (Gratak)](https://thunderstore.io/c/valheim/p/Gratak/VitalityRewrite/) | v1.2.6, updated 2026-09-22, 1.0: likely | Running, swimming, jumping, mining, woodcutting and "not dying" raise a Vitality skill, which increases max HP, stamina and eitr and improves regeneration. Source: [Gra-tak/ValheimVitalityRewrite](https://github.com/Gra-tak/ValheimVitalityRewrite) (last push 2024, so the source may lag behind the release). |
| [StaminaExtended (shudnal)](https://thunderstore.io/c/valheim/p/shudnal/StaminaExtended/) | v1.0.11, updated 2026-09-11, 1.0: likely | Base stamina scales with skill levels up to skill 100. Also changes regeneration, encumbrance, swimming, sneaking and surface-dependent stamina. Source: [shudnal/StaminaExtended](https://github.com/shudnal/StaminaExtended). |
| [WackyEpicMMOSystem (WackyMole)](https://thunderstore.io/c/valheim/p/WackyMole/WackyEpicMMOSystem/) | v1.9.67, 2026-09-13, 1.0: yes (rebuilt for 1.0) | A full level and attribute-point system that feeds HP, stamina and more. Heavy RPG layer. Source: [Wacky-Mole/WackyEpicMMOSystem](https://github.com/Wacky-Mole/WackyEpicMMOSystem). |
| [SkillsReworked (M2Valheim)](https://thunderstore.io/c/valheim/p/M2Valheim/SkillsReworked/) | v2.0.0, 2026-09-24, 1.0: likely | Level XP from kills and 4 attributes that drive all skills, with prestige. |

**Inspiration.**
- **Vanilla:** base values are `Player.m_baseHP` and `Player.m_baseStamina`. Their code defaults are 25 and 75 in 1.0, but the prefab may override them. Food adds to them in `Player.GetTotalFoodValue`.
- **Ours:** a small, capped bonus from existing skills only (Run, Jump, Swim, Sneak), with no new "Vitality" skill. That keeps it a light touch next to food.
- Only build this if it fits our progression philosophy; VitalityRewrite already covers the idea.

### Better tower shields (Revamp): a two-handed wall that cannot parry, heavily slowed, blocks nearly everything, with a low-damage, heavy-stagger bash

**Coverage: partial.** GCO is closest to the wall stance, but only inside a huge overhaul and without a two-handed rule or a bash. CaptainValheim and ShieldBash add shield strikes whose damage grows with block power, for every shield, and keep tower shields one-handed. The standalone tower-shield mods only change numbers. No mod makes tower shields two-handed or gives them a low-damage, heavy-stagger bash.

| Mod | Status | Notes |
|---|---|---|
| [Goo's Combat Overhaul](https://thunderstore.io/c/valheim/p/gnls/GoosCombatOverhaul/) | see above, 1.0: yes | Tower shields keep blocking until a blocked hit empties your stamina. A successful block prevents stagger and knockback, covers a wider angle and reduces the remaining physical damage. You cannot parry or run while blocking, and the equipment penalty is heavier. No two-handed rule and no shield bash. |
| [CaptainValheim (sighsorry)](https://thunderstore.io/c/valheim/p/sighsorry/CaptainValheim/) | v1.0.11, updated 2026-09-27, 1.0: yes (rebuilt for 1.0.7, tagged Deep North) | Shields as weapons. With an empty right hand, the primary attack is a shield strike built from a clone of the vanilla unarmed attack; its damage comes from block power and its push from deflection force (×0.4 each by default), scaled by the Blocking skill. The page describes no stagger tuning. Also a shield throw, a charge (block + secondary attack), projectile reflection and the vanilla block-charge counter. Every shield gets it through a global fallback, tower shields stay one-handed, and its techniques check `ItemType.Shield`. Patches `Humanoid.GetCurrentWeapon`, `StartAttack`, `Pickup`, `BlockAttack` (prefix and transpiler) and `Character.RPC_Damage`, among others. Source: [sighsorry1029/CaptainValheim](https://github.com/sighsorry1029/CaptainValheim) (GPL-3.0 and MIT license files). Tagged AI Generated. About 2.5K downloads. |
| [ShieldBash (Mexanik)](https://thunderstore.io/c/valheim/p/Mexanik/ShieldBash/) | v1.5.5, 2026-07-04, 1.0: unknown | A bash on its own key (F by default) with its own animation and hit and miss sounds. Blunt damage scales with block power and the Blocking skill, plus enemy knockback and a dynamic stamina cost; no stagger setting on the page. Needs Jotunn 2.29.1. About 6.5K downloads. |
| [ZenCombat (ZenDragon)](https://thunderstore.io/c/valheim/p/ZenDragon/ZenCombat/) | v1.0.2, updated 2026-09-25, 1.0: yes (tagged Deep North) | Tower shield "block charges": turns on the vanilla block-charge counter, so every N blocks (default 5) trigger a counter-attack; the default list is the Wood, Bone, Iron, Serpentscale, Blackmetal and Flametal tower shields. "Reliable block": block defence still applies while a hit staggers you, as long as you have stamina (tower shields only by default). Also knockback scaling, a dodge button and auto-equip of your last shield. Closed source (the [GitHub repo](https://github.com/ZenDragonX/ZenMods_Valheim/wiki) is only a wiki and issue tracker). About 97K downloads. |
| [Make Tower Shields Great Again (FactoriaTeam)](https://thunderstore.io/c/valheim/p/FactoriaTeam/Make_Tower_Shields_Great_Again/) ([Nexus 2900](https://www.nexusmods.com/valheim/mods/2900)) | v0.0.4, 2024-10-25, 1.0: unknown | +20% tower block armor (more with quality), extra resistances, knockback damage on block scaled by the Blocking skill, and a heavier slow (-25%, against -20% in vanilla per its page) with +10% run and walk stamina. A data pack built on WackysDatabase 2.4.31 and ReliableBlock 1.0.0, both from before 1.0. About 3.5K downloads. |
| [ReliableBlock (Korppis)](https://thunderstore.io/c/valheim/p/Korppis/ReliableBlock/) | v1.0.0, 2022-06-14, 1.0: dead (unchanged since 2022) | A block or parry keeps mitigating while you have stamina, even when the leftover damage fills the stagger bar; you still stagger. A `Humanoid.BlockAttack` transpiler. Source: [karkkant/valheim-reliable_block](https://github.com/karkkant/valheim-reliable_block). About 140K downloads. |
| [ReliableBlockRebuilt (RYEO)](https://thunderstore.io/c/valheim/p/RYEO/ReliableBlockRebuilt/) | v1.1.0, updated 2026-09-26, 1.0: yes (built and startup-tested on 1.0.16) | Preview fork of ReliableBlock with the same rule, for the local player only; it disables itself when `BlockAttack` does not match the code it inspected. MIT, source: [anneryeo/valheim-reliable-block](https://github.com/anneryeo/valheim-reliable-block). Tagged AI Generated. About 40 downloads. |
| [GrindstoneSkills (MilkyTeam)](https://thunderstore.io/c/valheim/p/MilkyTeam/GrindstoneSkills/) | v0.10.0, updated 2026-09-29, 1.0: likely | Perks of its new Defense skill (not the vanilla Blocking skill): "Shield bash" (up to a 15% chance that a normal block staggers the attacker), up to 25% more poise, and "Shield Wall" from level 50 (players within 4 m behind a blocking player take 10% less damage). Server and every client. Source: [geraldjglasgow/ValheimMods](https://github.com/geraldjglasgow/ValheimMods). About 650 downloads. |
| [Combat Adjustments (Mushroom_Vikings)](https://thunderstore.io/c/valheim/p/Mushroom_Vikings/CombatAdjustments/) | v0.8.3, updated 2026-09-24, 1.0: yes (checked against 1.0.7 per its design doc) | Hold-block rework: an equipped shield adds a flat amount to the stagger bar (a `Character.GetStaggerTreshold` postfix; Flametal tower +70 at max quality), and tower and round shields get +5% block armor and +20% durability. No slow, parry or two-handed change. Server-set, needs MushroomSync. Source: [NickSpinosa/Valheim_Mushroom_Mods](https://github.com/NickSpinosa/Valheim_Mushroom_Mods). About 200 downloads. |
| [WeaponArts (j1gA)](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/) | v0.14.1, updated 2026-09-26, 1.0: yes (tested with 1.0.16) | One active art per weapon type on a key; the tower shield's is "Taunt": nearby monsters come to you and you take less damage. Source: [tbsj1ga/WeaponArtsValheim](https://github.com/tbsj1ga/WeaponArtsValheim). Tagged AI Generated. About 290 downloads. |
| [Valheim Combat Overhaul (Kyresel)](https://thunderstore.io/c/valheim/p/Kyresel/CombatOverhaul/) ([Nexus 591](https://www.nexusmods.com/valheim/mods/591)) | v1.7.8, 2021-04-22, 1.0: dead (deprecated) | No parry, but blocks damage beyond block power: surplus damage drains stamina instead of health, and no stagger while stamina remains. 40% less block stamina than other shields (Nexus snippet), halved knockback on a block, and "stickiness" to the opponent. |

**Inspiration.**
- **Vanilla already provides most parts:**
  - The `TwoHandedWeaponLeft` equip rules, and `m_attachOverride` for the back model and the armor stand slot.
  - The unarmed combo, which already plays in the shield stance with an empty right hand.
  - A block formula where very high block armor absorbs almost everything (less damage, stamina, stagger and push per hit), and a block-charge counter that is switched off.
  - A stagger lever independent of damage: a landed hit adds its physical and lightning damage × `HitData.m_staggerMultiplier` to the victim's stagger bar. "Low damage, heavy stagger" is a small blunt value with a large multiplier on the bash's `Attack`.
- **Borrow:**
  - GCO's "hold until stamina is empty, no stagger or knockback".
  - ReliableBlock (and its 1.0 fork) and ZenCombat: the block still counts when a hit staggers you. Ours goes further and prevents that stagger while stamina lasts.
  - CaptainValheim's empty-hand strike built from the cloned unarmed attack, with Blocking skill scaling.
  - Make Tower Shields Great Again's knockback on block, and GrindstoneSkills' block that sometimes staggers the attacker (an optional extra).
- **What they get wrong:**
  - GCO locks the stance inside a huge overhaul.
  - CaptainValheim and ShieldBash give every shield a strike whose damage grows with block power, and keep tower shields one-handed, so you still carry a sword.
  - Make Tower Shields Great Again only changes numbers and depends on mods from before 1.0; Combat Adjustments only enlarges the stagger bar.
- **Ours:** a standalone tower-shield identity.
  - Two-handed under the vanilla rules; very slow when carried, slower still while bracing.
  - Much more block armor, no parry, and a frontal block that also stops AoE and projectiles.
  - A small, cheap bash on the normal attack button: low damage, heavy stagger. It is crowd control that sets up allies (staggered enemies take double damage), not a damage source.
- Detect GCO, ZenCombat, CaptainValheim, ShieldBash and ReliableBlock (either version) and warn.

### Dual wielding (New): a one-handed weapon in each hand

**Coverage: full.** Two maintained 1.0 mods already let you hold two one-handed weapons and attack with both: DualWielder plays the vanilla dual-axe and dual-knife moves and alternates the weapons per combo step, and balrond DualMastery strikes with both weapons in its own animation, with a Dual Wield skill for the off hand. The most downloaded one, Smoothbrain's DualWield, uses custom clips and same-type pairs, and its 1.0 fixes are only in its source so far. Valheim Ascended has an off-hand slot inside an RPG overhaul. What is left is polish on the DualWielder approach: each hit from the weapon that lands it, the vanilla dual stance, the knives' own combo length and consistent equip handling.

| Mod | Status | Notes |
|---|---|---|
| [DualWielder (RustyMods)](https://thunderstore.io/c/valheim/p/RustyMods/DualWielder/) | v1.1.2, updated 2026-09-26, 1.0: yes (tagged Deep North Update) | Any two one-handed weapons (sword + axe works, spears too) except the Abyssal Harpoon; a second one-handed weapon always goes to the left hand, and a key (Left Alt by default) swaps the hands. An `Attack.Start` prefix swaps the clone's trigger for the vanilla `dualaxes` or `dual_knives` set (any knife in the pair picks the knives) with 4 chain levels, and uses the left weapon for the whole 2nd and 4th steps. The secondary becomes `dualaxes_secondary` (any axe in the pair) or `dual_knives_secondary`; a spear in the main hand keeps its own secondary. No stance change: vanilla keeps the left weapon's stance. An `UnequipItem` prefix moves the left weapon to the right hand when the right one leaves. A cloned back joint holds the sheathed left weapon. No asset bundle. Its README still lists a damage merge option, a damage modifier and a "DualWielder" skill, but the 1.1.2 source ("overhauled plugin") only has a config lock and the swap key. Categorized client-side, but it uses ServerSync and a version check: a server with the mod refuses players without it. Extracted from the deprecated Almanac Class System. Source: [RustyMods/DualWielder](https://github.com/RustyMods/DualWielder) (no license file found). About 32.8K downloads. |
| [balrond DualMastery (Balrond)](https://thunderstore.io/c/valheim/p/Balrond/balrond_DualMastery/) | v0.2.8, updated 2026-09-28, 1.0: yes (a Deep North fix in 0.2.3, tagged Deep North Update) | Any two one-handed weapons. A "custom dual wield attack animation" in which both weapons strike in sequence, each with its own damage; speed, range and angle depend on the weapons. A new Dual Wield skill sets the off-hand damage from 50% (skill 0) to 75% (skill 100). The secondary attack replaces the weapons' own special attacks: it repeats the primary without the off-hand penalty, for more stamina. A spear in the pair takes over and plays as a vanilla spear. The README says no configuration is needed; 0.2.8 added a damage scale setting. Server and every client; its page says it is not compatible with other dual wield mods. No public source found. About 10.6K downloads. |
| [DualWield (Smoothbrain)](https://thunderstore.io/c/valheim/p/Smoothbrain/DualWield/) | v1.0.10, updated 2026-02-05, 1.0: unknown (the [GitHub source](https://github.com/blaxxun-boop/DualWield) has "fixes for 1.0" from 2026-09-10 and version 1.0.12, last pushed 2026-09-26, not on Thunderstore) | Axes, clubs, knives and swords, both of the same skill; no spears; an exclusion list. Its own clips from an embedded AssetBundle (plus a special attack for dual axes), swapped in on each client through an `AnimatorOverrideController` in a `ZSyncAnimation.RPC_SetTrigger` prefix, so players without the mod see the normal one-handed swing. Every `Hit` event also strikes with the left weapon (a second `DoMeleeAttack` with a mirrored angle, in an `Attack.OnAttackTrigger` prefix), with per-step damage, speed and stamina tables per weapon type that scale the item's shared damage during the hit. One off-hand skill per type (or a shared one). A `SetupEquipment` prefix moves the left weapon to the right hand when the right hand empties (not inside `EquipItem` or `UnequipAllItems`). Transpilers on `Humanoid.EquipItem`, `ShowHandItems` and `Attack.DoMeleeAttack`; left trails; ServerSync. No license file. About 553K downloads. |
| [Valheim Ascended (kpttr)](https://thunderstore.io/c/valheim/p/kpttr/Valheim_Ascended/) | v0.4.1, updated 2026-09-29, 1.0: yes (its changelog has a Valheim 1.0 section; the README still names Valheim 0.221.12) | RPG overhaul (classes, talents, abilities, enchanting) with persistent main-hand and off-hand slots: a one-handed weapon in each hand survives sheathing and save/load, a spear displaces the pair, and a capstone talent allows two two-handed weapons. The page does not describe how the pair attacks. Built as a co-op mod; its README asks for the same mod on every client and the server (for its boats). Tagged AI Generated. About 9.4K downloads. |
| Related | | Dual weapons as single items: [DualSwords (AlexDrake)](https://thunderstore.io/c/valheim/p/AlexDrake/DualSwords/) (v1.0.2, updated 2026-09-21, 1.0: yes, tagged Deep North Update; also [Nexus 3698](https://www.nexusmods.com/valheim/mods/3698), per a Nexus snippet) builds 22 dual swords (14 from vanilla swords, 8 more with Valheim Armory) that copy the combat and animations of `AxeBerzerkr`. [DualSwords (SunRay)](https://thunderstore.io/c/valheim/p/SunRay/DualSwords/) (v0.4.1, updated 2026-07-31, 1.0: unknown) has craftable dual swords with the vanilla dual-axe animations; needs Jötunn. [Warfare (Therzie)](https://thunderstore.io/c/valheim/p/Therzie/Warfare/) (v1.9.4, updated 2026-09-22, 1.0: yes, tagged Deep North Update) adds dual knives from bone to flametal, a dual scimitar, a dual axe and a dual scythe; its roadmap lists dual war pikes, axes and swords for all tiers. A Nexus "Dual wield" mod ([1649](https://www.nexusmods.com/valheim/mods/1649)) has been removed (Nexus snippet). |

**Inspiration.**
- **Vanilla already has the moves:** the Berserkir axes (`AxeBerzerkr`) and Skoll and Hati (`KnifeSkollAndHati`) are single items whose attacks play the player's dual-axe and dual-knife animation sets. Blocking without a shield already works: the left item blocks first (`Humanoid.GetCurrentBlocker`). What vanilla lacks is an equip rule: `Humanoid.EquipItem` takes a one-handed weapon out of the left hand.
- **Borrow:**
  - DualWielder's vanilla dual triggers on the per-swing clone: no assets, and players without the mod see the same moves. Also its swap key and its left back joint.
  - balrond DualMastery's off-hand damage factor (50% to 75%), and its secondary attack that drops the off-hand penalty for more stamina.
  - Smoothbrain's left weapon trails, exclusion list and its rule that spears stay out (their secondary throws the weapon).
- **What they get wrong:**
  - Smoothbrain replaces clips on each client, only pairs weapons of the same skill, and scales the items' shared damage during each hit. Its Thunderstore release predates 1.0.
  - DualWielder always sends a second one-handed weapon to the left hand, so changing the main weapon takes an unequip or the swap key. It switches weapons per chain step rather than per hit, keeps the left weapon's own stance, and sets 4 chain levels for the knives, while the wiki gives Skoll and Hati a 3-hit combo (whether a fourth trigger exists is unverified). Its README still lists a skill and damage options that the 1.1.2 code no longer has.
  - balrond DualMastery is closed source and adds a skill; Valheim Ascended ties its off-hand slot to an RPG overhaul.
- **Ours, if we build it:**
  - Any two one-handed swords, axes, clubs or knives; a key held while equipping picks the main hand.
  - The vanilla dual stance and triggers, read from the vanilla dual items at runtime. Every hit uses the weapon that lands it (damage, status effect, skill, durability), with an off-hand factor.
  - Blocking with the off-hand weapon, as vanilla does, or with the better of the two.
  - Consistent hide and show, eating, load, death and torch handling; left trails and a second back slot.
  - Technically client-only; it ships as Both, with the server's settings for everyone.
- **Or recommend DualWielder:** it is maintained on 1.0 and already works without assets.

### Crossbow revamp (QoL): stays loaded

**Coverage: full, but abandoned.** The exact feature exists in SaveCrossbowState, which is now deprecated. No maintained 1.0 mod has it. FastArbalest only keeps the bolt loaded while the crossbow is slung on your back.

| Mod | Status | Notes |
|---|---|---|
| [SaveCrossbowState (Azumatt)](https://thunderstore.io/c/valheim/p/Azumatt/SaveCrossbowState/) | v1.0.2, updated 2025-08-29, **deprecated** (no reason given), 1.0: unknown | Keeps the loaded state when you switch weapons or unequip. v1.0.1 added the Dundr staff, which also uses reload. Client-only, no config. About 152K downloads. No public source repo found (github.com/AzumattDev/SaveCrossbowState returns 404). |
| [FastArbalest (Neobotics)](https://thunderstore.io/c/valheim/p/Neobotics/FastArbalest/) ([Nexus 2316](https://www.nexusmods.com/valheim/mods/2316)) | v1.0.0, updated 2026-09-11, 1.0: yes | Reload speed (10-100%) with optional proportional damage. "Pausable reload" pauses instead of restarting. "Sling Loaded" keeps the bolt only while the crossbow is hidden with the show/hide key or while swimming: *unequipping or swapping still unloads it*. Also realistic recoil and knockback. |
| [BetterCrossbows (xtavim)](https://thunderstore.io/c/valheim/p/xtavim/BetterCrossbows/) | v1.1.0, updated 2026-09-09, 1.0: likely | Reload time, reload while moving, stamina drain toggle, ServerSync. Its README tells users to add SaveCrossbowState to get this feature. Source: [xtavim/BetterCrossbows](https://github.com/xtavim/BetterCrossbows). |
| [Crossbow Reload Tweaked (Crbakc)](https://thunderstore.io/c/valheim/p/Crbakc/Crossbow_Reload_Tweaked/) | v1.1.1, 2025-06-01, 1.0: unknown | Reload speed, movement speed and stamina drain while reloading. |

**Inspiration.**
- **Vanilla mechanics:**
  - `Player` stores the loaded weapon as an `ItemData` reference (`m_weaponLoaded`) and mirrors a bool to the player ZDO (`ZDOVars.s_weaponLoaded`) so other clients can see it (`Player.SetWeaponLoaded` / `Player.IsWeaponLoaded`).
  - `Humanoid.UnequipItem` calls `ResetLoadedWeapon` for any weapon.
  - `Player.UpdateWeaponLoading` queues a reload action whenever the equipped weapon is not the loaded one. The action drains eitr through `m_reloadEitrDrain` for Dundr.
  - `Attack` checks `IsWeaponLoaded` for weapons with `m_requiresReload`.
- **Ours:**
  - Persist a per-item "loaded" flag, for example in `ItemData.m_customData`, so the state survives unequip, swaps and (by config) logout.
  - Restore it on equip instead of re-queuing the reload.
  - Cover *every* weapon with `m_requiresReload`: crossbows, Dundr and modded weapons.
  - Decide what happens when the item is dropped or traded, and whether eitr is refunded.
- The market gap is simply a maintained, 1.0-verified version.

### Ritual (New): consumes items, needs several people

**Coverage: none.** No mod found requires several participants. The existing altar and sacrifice mods are single-player, and most are deprecated.

| Mod | Status | Notes |
|---|---|---|
| [Krumpac Sacrificing Altars](https://thunderstore.io/c/valheim/p/Krumpac/Krumpac_Sacrificing_Altars/) | v7.3.2, 2025-09-07, dead (deprecated; needs registration) | Altars per biome to Norse gods, a "praying" skill, and sacrifices for blessings. Part of the Reforge modpack. About 300 MB. |
| [CustomizeAltars (Huntardys)](https://thunderstore.io/c/valheim/p/Huntardys/CustomizeAltars/) | v2.0.0, 2024-02, dead (deprecated) | JSON-configured boss altars: which boss spawns, and which items and amounts are required. |
| [Vikings Summoner (Radamanto)](https://thunderstore.io/c/valheim/p/Radamanto/Vikings_Summoner/) | v1.5.1, 2026-09-26, 1.0: likely | "Ritual artifacts" and summoning totems in a necromancer theme. The ritual is flavour only; one player uses an item. |
| [RtDMonsters (Soloredis)](https://thunderstore.io/c/valheim/p/Soloredis/RtDMonsters/) | v2.4.14, 2026-05, dead (deprecated) | Summon custom bosses by placing items on an altar. |

**Inspiration.**
- **Vanilla building blocks:**
  - `OfferingBowl`: the boss altar, a single interaction that consumes items and spawns something.
  - `Incinerator`: the obliterator, which consumes items and produces a result.
  - `ShieldGenerator` and `PrivateArea`: area effects.
- **Ours:** a ritual circle piece with N participant runestones. Each player must hold "interact" at the same time, synchronized through the piece's ZDO. The ritual consumes offerings and triggers a world effect: summon, weather, a timed area buff, faster crop growth, calling a raid, and so on.
- This is a strong co-op hook, and a good cross-category "magic" theme (Combat, Farming, Building).

### New summons (New)

**Coverage: partial.** Undead necromancy is thoroughly covered. Non-undead, command-driven summons are not.

| Mod | Status | Notes |
|---|---|---|
| [Cheb's Necromancy (ChebGonaz)](https://thunderstore.io/c/valheim/p/ChebGonaz/ChebsNecromancy/) | v5.2.1, 2026-09-28, 1.0: likely | Wands for skeleton and draugr warriors, archers, mages, miners and woodcutters. Commands: follow, wait, roam, teleport to you. Pylons for gathering, refuelling, farming, repair and ghost guards. Built on Jötunn. Open source (Unlicense): [jpw1991/chebs-necromancy](https://github.com/jpw1991/chebs-necromancy). About 119K downloads. |
| [Vikings Summoner (Radamanto)](https://thunderstore.io/c/valheim/p/Radamanto/Vikings_Summoner/) | v1.5.1, 2026-09-26, 1.0: likely | Necromancer weapons, summoning totems and armor for each biome. |
| [MagicPlugin (blacks7ar)](https://thunderstore.io/c/valheim/p/blacks7ar/MagicPlugin/) | v2.2.2, 2026-09-24, 1.0: likely | Summoning totems (7 summonable creatures) among many magic weapons and accessories. |
| [Summon Mastery (Kolefield)](https://thunderstore.io/c/valheim/p/Kolefield/Summon_Mastery/) | v0.1.2, 2026-09-13, 1.0: likely | Summons scale with the staff's upgrade level (HP, regen, armor, speed, damage), and nearby summons follow you through portals. Source: [kolefield/valheim-summon-mastery](https://github.com/kolefield/valheim-summon-mastery). |

**Inspiration.**
- **Vanilla:** summons are spawned through `SE_Spawn`/spawn attacks (for example, Dead Raiser skeletons). They are short-lived tames with no commands.
- **Borrow** Cheb's command set and Summon Mastery's upgrade scaling and portal following.
- **Ours:**
  - Avoid undead, which is Cheb's territory. Go for nature, elemental or spirit summons that fit Valheim lore.
  - Make them come out of our ritual system, and give them a small command layer (hold, follow, attack the pinged target) that ties into the ping revamp.

### New magical items based on Valheim magic (ward, heal) (New)

**Coverage: partial.** Big content packs have healing staffs and shields. One new mod (ArcaneWard) is close to our "ward" idea and uses only vanilla assets.

| Mod | Status | Notes |
|---|---|---|
| [ArcaneWard (Madkatz)](https://thunderstore.io/c/valheim/p/Madkatz/ArcaneWard/) | v1.1.15, 2026-09-25, 1.0: yes | Three off-hand "wards" that block using a separate eitr reserve that scales with Blocking and Elemental Magic, plus battlemage weapons (Arcane Blade and Knife) with elemental secondaries. **Built only from vanilla assets.** Needs Jötunn. Source: [Torvios/ArcaneWards](https://github.com/Torvios/ArcaneWards). |
| [Wizardry (Therzie)](https://thunderstore.io/c/valheim/p/Therzie/Wizardry/) | v1.2.2, 2026-09-22, 1.0: likely | 5 elemental and 2 blood-magic staffs, mage armors, scrolls, potions, summons, a Wizard Table, a potion cauldron, and new enemies and locations. Custom assets (107 MB). About 509K downloads. |
| [MagicPlugin (blacks7ar)](https://thunderstore.io/c/valheim/p/blacks7ar/MagicPlugin/) | v2.2.2, 2026-09-24, 1.0: likely | Includes a Staff of Healing, totems, and magical belts, rings, earrings and necklaces with their own slots. |
| [RtDMagic (Soloredis)](https://thunderstore.io/c/valheim/p/Soloredis/RtDMagic/) | v0.7.34, 2026-05, dead (deprecated) | Four tiers of AoE healing staffs, mage armor, early eitr food. |

**Inspiration.**
- **Vanilla already has:**
  - The blood-magic shield status effect `SE_Shield` (Staff of Protection).
  - The base `PrivateArea` ward.
  - The Ashlands `ShieldGenerator` dome.
- **Ours:** extend these systems, the ArcaneWard way (vanilla assets and mechanics first):
  - Placeable ward and heal totems that pulse `SE_Shield` or heal-over-time to allies in a radius.
  - A personal "ward charm".
  - Cleansing (remove poison or burning).
- Theme them with the ritual system rather than making another big staff pack.

### More training dummies (New): merged into Training dummies revamp

Cancelled in the idea sheet: merged into the Training dummies revamp.

**Coverage: partial.** The per-weapon dummies live in the Training dummies revamp, whose section lists the mods for them. For the utility variants left here (optional extras for that mod), DPS and TouchGrass already give DPS readouts and configurable resistances or damage, and OdinTrainingPlace has its own training pieces.

| Mod | Status | Notes |
|---|---|---|
| [OdinTrainingPlace (OdinPlus)](https://thunderstore.io/c/valheim/p/OdinPlus/OdinTrainingPlace/) | v1.6.6, updated 2026-09-19, 1.0: yes (updated in 1.6.5) | Archery target (also for crossbows), wooden dummy, mechanical block-training dummy, woodcutting pole, flint rock, running track, swimming pool, and XP potions. About 248K downloads. Focused on skill grinding. |
| [TargetPractice (Norheim)](https://thunderstore.io/c/valheim/p/Norheim/TargetPractice_Valheim/) ([Nexus 1246](https://www.nexusmods.com/valheim/mods/1246)) | v1.0.2, updated 2021-08-24, 1.0: dead | Deprecated. A dummy and 4 archery targets. |
| [DPS (JereKuusela)](https://thunderstore.io/c/valheim/p/JereKuusela/DPS/) | v1.7.0, updated 2026-09-10, 1.0: likely | Configurable dummies (resistances, status effects) and DPS, stamina and XP meters. |
| [TouchGrass (sighsorry)](https://thunderstore.io/c/valheim/p/sighsorry/TouchGrass/) | v1.0.7, updated 2026-09-10, 1.0: yes (tagged Deep North Update) | DPS, DPH and XP meter on the vanilla dummy, per-dummy damage amount and type, anti-macro fatigue. |
| [ParrySense (Kallik)](https://thunderstore.io/c/valheim/p/Kallik/ParrySense/) | v0.1.1, updated 2026-09-01, 1.0: unknown | Client-side parry-timing feedback (too early, parry, too late) against any attacker. Source: [khallik/ParrySense](https://github.com/khallik/ParrySense). |

**Inspiration.**
- The dummies themselves are planned in Training dummies revamp. The optional variants left here: a resistance or weakness dummy, a parry trainer that swings on a telegraphed rhythm, a DPS readout, a moving archery target, and a tall dummy.
- Reuse the DPS readouts of DPS or TouchGrass rather than building one.
- ParrySense's timing feedback pairs well with a parry trainer that swings on a rhythm.
- Keep XP gains neutral. TouchGrass's fatigue idea shows why: players exploit dummies to macro-farm skills.

---

## UX

### Loot filter (QoL)

**Coverage: full.** The user already knew of Nexus 116. All current options except one are configured through **text lists of prefab names**, which is poor UX.

| Mod | Status | Notes |
|---|---|---|
| Loki's Autoloot Trash Filtering ([Nexus 116](https://www.nexusmods.com/valheim/mods/116), [Thunderstore LCDR](https://thunderstore.io/c/valheim/p/LCDR/Lokis_Autoloot_Trash_Filtering/)) | Thunderstore v1.0.1, 2022-12-18, 1.0: unknown | Three modes (block all, block the trash list, allow all) and a configurable trash list (stone, wood, resin, bone fragments, and so on). |
| [AutoPickupIgnorer (PipMods)](https://thunderstore.io/c/valheim/p/PipMods/AutoPickupIgnorer/) | v1.1.0, updated 2026-09-09, 1.0: likely | You uncomment item names in the config. A hotkey cycles between config list, ignore all, and vanilla. Source: [michaelpipkin/PipValheimMods](https://github.com/michaelpipkin/PipValheimMods/tree/main/AutoPickupIgnorer). |
| [LootFilter (wonkotron)](https://thunderstore.io/c/valheim/p/wonkotron/LootFilter/) | v1.0.2, 2026-02-17, 1.0: unknown | A comma-separated prefab blacklist, edited through Configuration Manager. Source: [wonkovalheim/lootfilter](https://github.com/wonkovalheim/lootfilter). |
| [InventoryActions (sighsorry)](https://thunderstore.io/c/valheim/p/sighsorry/InventoryActions/) | v1.1.8, 2026-09-26, 1.0: likely | Per-item auto-pickup control from an inventory hover button, with controller support. Also sort, favourites, restock and trash. |

**Inspiration.**
- **Best UX idea found:** the deprecated [RagnarsRokare AutoPickupSelector](https://thunderstore.io/c/valheim/p/RagnarsRokare/RagnarsRokare_AutoPickupSelector/) (2022) turned the Trophies tab into a clickable list that toggled auto-pickup per item.
- **Vanilla:** pickup is `Player.AutoPickup`, an overlap sphere of `m_autoPickupRange` with a global toggle key.
- **Ours:**
  - Toggle "never auto-pickup" from the inventory (modifier+click, or a context button) and show an icon on filtered items.
  - Category rules (trophies, materials of past biomes).
  - Share the same item-marking model as our sort, trash and favourite features.

### Sort chest (QoL)

**Coverage: full.** The space is saturated: one mature, MIT-licensed reference and at least four new 1.0-era competitors.

| Mod | Status | Notes |
|---|---|---|
| [Quick Stack Store Sort Trash Restock (Goldenrevolver)](https://thunderstore.io/c/valheim/p/Goldenrevolver/Quick_Stack_Store_Sort_Trash_Restock/) | v1.4.15, 2026-09-12, 1.0: yes (rebuilt for 1.0) | Sort the container and the inventory by category, name, weight or value. Alt+click to favourite items or slots. Quick stack to nearby chests, store all, trash, restock. Will not touch chests another player has open. About 940K downloads. MIT source: [Goldenrevolver/QuickStackStore](https://github.com/Goldenrevolver/QuickStackStore). |
| [QuickStackPlus (Goneryx)](https://thunderstore.io/c/valheim/p/Goneryx/QuickStackPlus/) | v1.2.1, 2026-09-24, 1.0: yes (released after 1.0) | "Smart Storage": item filters per container, stored on the container, which drive quick-stack routing. Sorting only on demand. Blacklist and junk. Server-controlled master settings. |
| [RunicStorage (Chazman)](https://thunderstore.io/c/valheim/p/Chazman/RunicStorage/) | v1.3.12, 2026-09-28, 1.0: likely | Remembers chest contents; quick-stack routing rules by item and biome; coloured chest labels; previews; restock, search, sort and consolidation. |
| [HexQuickStackStorage (Hex_Viking)](https://thunderstore.io/c/valheim/p/Hex_Viking/HexQuickStackStorage/) | v1.4.3, 2026-09-26, 1.0: yes (released after 1.0) | A Sort button under the container and an "S" button for the inventory, plus quick stack and delete. |

**Inspiration.**
- **Vanilla `InventoryGui`** only has Take All and Stack All (`OnTakeAll`, `OnStackAll`).
- **Build ours only as part of a unified storage-UX module** (sort, search, filter and loot rules sharing one item-category model and one favourites model). A standalone sorter adds nothing.
- **Borrow** QSSSTR's favouriting and multiplayer safety. Container writes must respect ZDO ownership.
- **Avoid** auto-sorting on open. QuickStackPlus deliberately does not do it, and it destroys players' spatial memory.

### Sort bags (QoL)

Cancelled in the idea sheet.

**Coverage: full** for the player inventory. For modded backpacks, the coverage is unclear.

| Mod | Status | Notes |
|---|---|---|
| [Quick Stack Store Sort Trash Restock](https://thunderstore.io/c/valheim/p/Goldenrevolver/Quick_Stack_Store_Sort_Trash_Restock/) | see above | Sorts the inventory while respecting favourites and the hotbar. Documents compatibility with Comfy Quick Slots and AzuExtendedPlayerInventory, but not with backpack mods. |
| [Stackmaster (JStack424)](https://thunderstore.io/c/valheim/p/JStack424/Stackmaster/) | v1.2.0, 2026-09-24, 1.0: likely | "Backpack" here means the player inventory. Auto-sorts it on open, plus opted-in vanilla chests. The hotbar and equipped items stay fixed. Protected stacks can have restock targets. Source: [JStack424/Stackmaster](https://github.com/JStack424/Stackmaster). |
| [InventoryActions (sighsorry)](https://thunderstore.io/c/valheim/p/sighsorry/InventoryActions/) | see above | Sorts inventories and containers, with favourites and controller support. |
| [AdventureBackpacks (Vapok)](https://thunderstore.io/c/valheim/p/Vapok/AdventureBackpacks/) | v2.2.1, 2026-09-28, 1.0: likely | The main backpack mod. No sort feature documented. A compatibility target if "bags" means backpacks. Source: [Vapok/AdventureBackpacks](https://github.com/Vapok/AdventureBackpacks). |

**Inspiration.**
- **Open question:** does "bags" mean the player inventory, or backpack items? If it means backpacks, the gap is sorting inside AdventureBackpacks and similar mods, which needs soft integration with them.
- Otherwise this is the same module as "Sort chest", applied to the player grid: lock the hotbar row, respect favourites.

### Search chest (QoL)

**Coverage: full.** The user assumed nothing existed, but a wave of mods appeared right after 1.0.

| Mod | Status | Notes |
|---|---|---|
| [SonicChestFilters (SonicDM)](https://thunderstore.io/c/valheim/p/SonicDM/SonicChestFilters/) | v1.0.1, 2026-09-11, 1.0: yes (released after 1.0) | A Jötunn text box on the open chest. Substring and wildcard matching on localized name, token and prefab. **Hides** non-matching slots. The `locate` command makes matching nearby chests glow. Sort button. Tagged AI Generated. Source: [sonicdm/SonicChestFilters](https://github.com/sonicdm/SonicChestFilters). |
| [TidyChests (Muindor)](https://thunderstore.io/c/valheim/p/Muindor/TidyChests/) | v1.3.1, 2026-09-24, 1.0: likely | Ctrl+O opens a search box over everything in nearby chests; a hotkey shows where an item is stored; quick stack. Source: [egor-muindor/ValheimMods](https://github.com/egor-muindor/ValheimMods). |
| [RunicStorage (Chazman)](https://thunderstore.io/c/valheim/p/Chazman/RunicStorage/) | see above | Remembers chest contents, so it can search chests that are not loaded, and highlights every matching chest. |
| [CraftSearch (XineladaLendaria)](https://thunderstore.io/c/valheim/p/XineladaLendaria/CraftSearch/) | v2.0.10, 2026-09-27, 1.0: yes | Search inside the open chest, which moves matches to the front of the visible grid, plus the recipe search below. |
| Older | | [Chest Search (QuinnWilton)](https://thunderstore.io/c/valheim/p/QuinnWilton/Chest_Search/) (2021, dead, a chat command that highlights chests). [ChestContents (Sticky)](https://thunderstore.io/c/valheim/p/Sticky/ChestContents/) (2025-05, an index of chest contents). Nexus-only (dates not visible): Search Bar Plus ([3630](https://www.nexusmods.com/valheim/mods/3630)), MixueStack ([3907](https://www.nexusmods.com/valheim/mods/3907)), and hover previews such as Contents Within ([1838](https://www.nexusmods.com/valheim/mods/1838)). |

**Inspiration.**
- There are two separate features:
  - **(a) Filter the open grid.** Prefer dimming or highlighting to hiding or reordering: hiding breaks spatial memory and makes drag-and-drop confusing.
  - **(b) Find across nearby containers.** Only containers in loaded zones have live inventories, so "remember what was inside" (RunicStorage) is the only way to search further.
- **Vanilla 1.0 now has a native search field in the build menu** (`BuildUi`: a `GuiInputField` filtered by localized search terms in `BuildUi.UpdateSearch`). Copy that look and behaviour, including controller focus handling, so our search feels built-in rather than like a Jötunn overlay.

### Search crafting station (QoL)

**Coverage: full.** Several maintained options exist.

| Mod | Status | Notes |
|---|---|---|
| [CraftSearch (XineladaLendaria)](https://thunderstore.io/c/valheim/p/XineladaLendaria/CraftSearch/) | v2.0.10, 2026-09-27, 1.0: yes | Recipe search in inventory, workbench, forge, cauldron and so on. Category filters (Weapons, Armor, Consumables...), `@mod` source search, and quantity crafting that works with craft-from-chest mods. Client-only. Its readme says "network version 39"; the game is now at 40. |
| [AAA Crafting (Azumatt)](https://thunderstore.io/c/valheim/p/Azumatt/AAA_Crafting/) | v2.1.8, updated 2026-09-14, but **flagged deprecated** | Amount-to-craft field, search filter, `!` prefix to search by ingredient, recipe tracking, controller virtual keyboard. |
| [CraftingFilter (cjayride fork)](https://thunderstore.io/c/valheim/p/cjayride/CraftingFilter/) | v1.2.1, 2026-09-09, 1.0: yes ("Updated for Valheim 1.0") | A category filter menu on the crafting tab. A fork of aedenthorn's mod. Source: [cjayride/CraftingFilter_Fork](https://github.com/cjayride/CraftingFilter_Fork). |
| [Sorted Menus (Goldenrevolver)](https://thunderstore.io/c/valheim/p/Goldenrevolver/Sorted_Menus_Cooking_Crafting_and_Skills_Menu/) | v1.3.4, 2024-10-04, 1.0: unknown | Sorts the crafting, cooking and skills lists by configurable criteria. Source: [Goldenrevolver/ValheimSortedMenus](https://github.com/Goldenrevolver/ValheimSortedMenus). |

**Inspiration.**
- **Mirror vanilla.** 1.0 gave the hammer build menu a search field (`BuildUi.UpdateSearch`) but not the `InventoryGui` crafting list, so a mod that looks exactly like the vanilla build search is the most "native" option.
- **Borrow** ingredient search (AAA's `!`), a "craftable now" toggle and category chips.
- **Ours:** one search component shared between crafting, containers and the build menu.
- **Related:** [VNEI (MSchmoecker)](https://thunderstore.io/c/valheim/p/MSchmoecker/VNEI/) (v0.17.6, 2026-09-10) is a full item and recipe browser, useful as a reference for item indexing.

### Notifications stay longer and show multiple (QoL)

**Coverage: partial.** Mods for duration and visible count exist, but none is confirmed on 1.0, and they mostly target the top-left pickup feed.

| Mod | Status | Notes |
|---|---|---|
| [NotificationsOverhaul (blacks7ar)](https://thunderstore.io/c/valheim/p/blacks7ar/NotificationsOverhaul/) ([Nexus 3160](https://www.nexusmods.com/valheim/mods/3160)) | v1.0.3, 2026-02-25, 1.0: unknown | Configurable duration, max visible notifications, position, font size and colour. Can apply to pickups only or to everything. About 12K downloads. |
| [BetterPickupNotifications (Pfhoenix)](https://thunderstore.io/c/valheim/p/Pfhoenix/BetterPickupNotifications/) | v1.3.3, 2025-11-15, 1.0: unknown | Reworks how pickup messages are handled so you can see everything you pick up. |
| [StackingNotifications (paddywan)](https://thunderstore.io/c/valheim/p/paddywan/StackingNotifications/) | v1.0.3, 2021-09, dead | A timed notification layer with several stacked notifications at the bottom right. Source: [paddywaan/PaddysVHMods](https://github.com/paddywaan/PaddysVHMods/tree/master/StackingNotifications). |
| [NotificationTweaks (digitiliad)](https://thunderstore.io/c/valheim/p/digitiliad/NotificationTweaks/) | v0.3.2, 2021-04, dead (deprecated) | Number shown at once, fade speed, font size, and merging of similar notifications. |

**Inspiration.**
- **Vanilla `MessageHud`:**
  - Top-left messages go through a queue and are shown **one at a time** on a short timer. Identical consecutive messages are merged by text and icon (`MessageHud.UpdateMessage`).
  - Unlock messages have their own queue.
  - A 50-entry `m_messageLog` exists.
- **Ours:**
  - A stacked feed that shows N messages at once.
  - Merge counts per item ("Wood x37").
  - Show important messages (unlocks, skill-ups) longer than pickups.
  - Optionally add a recallable history panel built from the existing message log.
- This is a small, low-risk mod, and nothing current and maintained is confirmed on 1.0.

### Ping system revamp (QoL)

**Coverage: partial.** World pings and auto-pins exist (the QuickPing revival), and so do ping behaviour tweaks (PingTweaks). No mod offers *typed* contextual pings (enemy, loot, go here, danger), a ping wheel, or a shared in-world marker with distance for everyone.

| Mod | Status | Notes |
|---|---|---|
| [QuickPing, TeamVibes revival](https://thunderstore.io/c/valheim/p/TeamVibes/QuickPing/) ([Nexus 2033](https://www.nexusmods.com/valheim/mods/2033)) | v1.0.5, 2026-09-27, 1.0: yes | Ping what you look at: its name floats in the world and on the minimap. Auto-pins useful things (ores, berries, dungeons, portals...), force-pins, a pin editor (rename, choose icon), and a tracer beam. The [original by Atopy](https://thunderstore.io/c/valheim/p/Atopy/QuickPing/) is deprecated (2024); source [Vodianoi/QuickPingMod](https://github.com/Vodianoi/QuickPingMod). |
| [PingTweaks (Riintouge)](https://thunderstore.io/c/valheim/p/Riintouge/PingTweaks/) | v1.0.7, updated 2026-09-11, 1.0: yes (tagged Deep North) | Pings are private unless you hold a modifier; persistent pings; re-ping to cancel; marker text; distance in metres and elevation; configurable duration and colour; double-click to pin another player's ping. LGPL source: [Terrenteller/Valheim-Plugins](https://github.com/Terrenteller/Valheim-Plugins). |
| [ImRightHere (Vidguy)](https://thunderstore.io/c/valheim/p/Vidguy/ImRightHere/) | v1.0.2, 2026-03-17, 1.0: unknown | Ping your own position with a hotkey; duration, colour, live distance; pin other players' pings. |
| [Hotkey To Ping (shineblind)](https://thunderstore.io/c/valheim/p/shineblind/Hotkey_To_Ping/) ([Nexus 2993](https://www.nexusmods.com/valheim/mods/2993)) | date not checked | Aim and press T to send a vanilla map ping, with a pointing emote. |

**Inspiration.**
- **Vanilla:**
  - A ping is only sent from the map (`Minimap` calls `Chat.SendPing`).
  - It travels through the `ChatMessage` routed RPC as `Talker.Type.Ping`.
  - It is drawn by `Minimap.UpdatePingPins` and as world text.
- **Keep the vanilla RPC** so players without the mod still see a basic ping.
- **Borrow:** QuickPing's "ping what you look at" and PingTweaks' distance, cancel and private options.
- **Ours adds:**
  - Ping types chosen by context (a creature becomes "enemy", an item "loot") or from a small wheel.
  - An in-world marker with distance that everyone with the mod sees.
  - A "follow this target" link for our summons.
- [ExploreTogether](https://thunderstore.io/c/valheim/p/Rolo/ExploreTogether/) (2021, deprecated) first introduced the world-ping key.

---

## Notable mods to study

| Mod | Why |
|---|---|
| [Jötunn](https://github.com/Valheim-Modding/Jotunn) (MIT, v2.30.2, 2026-09-21, 1.0: yes) | The standard Valheim modding library: custom items, pieces, recipes, GUI helpers, input and config sync. Read it even if we decide not to depend on it (several mods above, such as ChebsNecromancy, ArcaneWard and SonicChestFilters, do). |
| [Quick Stack Store Sort Trash Restock](https://github.com/Goldenrevolver/QuickStackStore) (MIT, pushed 2026-09-12) | Mature, 1.0-rebuilt reference for inventory and container manipulation: favourites, multiplayer-safe container access, sort comparators, UI buttons in `InventoryGui`. About 940K downloads. |
| [Cheb's Necromancy](https://github.com/jpw1991/chebs-necromancy) (Unlicense, pushed 2026-09-28) | Best open-source example of custom minion AI, command keys, worker behaviours and custom pieces with Jötunn. Directly relevant to summons and ritual pieces. |
| [blaxxun-boop / Smoothbrain mods](https://github.com/blaxxun-boop/PassivePowers) (PassivePowers pushed 2026-09-11, SmartSkills) | Small, focused, server-syncable mods. This author also maintains the widely used ServerSync / *Manager helper libraries. A good model for our "one feature per mod, config synced" style. |
| [valheim-dps (JereKuusela)](https://github.com/JereKuusela/valheim-dps) (Unlicense, pushed 2026-09-10) | A testing tool as well as a reference: dummy spawning, DPS and stamina meters. Use it while balancing the combat revamps. |
| [Valheim Community Patch (MidnightsFX)](https://github.com/MidnightsFX/Valheim-Community-Patch) (GPL-3.0, pushed 2026-09-27) | Written for 1.0 and Unity 6 from day one. Shows how the 1.0 internals (AI, ZDO) are patched today. GPL: study only, do not copy code. |
| [PingTweaks / Valheim-Plugins (Terrenteller)](https://github.com/Terrenteller/Valheim-Plugins) (LGPL-3.0, pushed 2026-09-27) | Minimal, clean patches of the vanilla ping pipeline (`Chat` and `Minimap`), maintained across every update up to Deep North. |
| [Goo's Combat Overhaul](https://thunderstore.io/c/valheim/p/gnls/GoosCombatOverhaul/) (closed source, v2.1.2, 1.0.16) | The most complete *design* reference for combat in 1.0: per-weapon YAML profiles, hyperarmor modes, running and jump attacks, tower shields. It is also the main mod our combat revamps must coexist with or detect. |

Also worth a look:
- [ArcaneWard](https://github.com/Torvios/ArcaneWards): new magic built only from vanilla assets.
- [ForsakenPowerOverhaul](https://github.com/JuneGame/Valheim.ForsakenPowerOverhaul): a layered power design.
- [StaminaExtended (shudnal)](https://github.com/shudnal/StaminaExtended): clean, config-heavy stat changes.

## Key takeaways

1. **Already well covered, only worth doing inside a bigger integrated module:**
   - Sort chest, sort bags, search chest, search crafting station and the loot filter. A wave of 1.0-era mods appeared in September 2026.
   - Boss powers made passive or boosted (duration and cooldown configs), and HP/stamina from activity.
   - Dual wielding one-handed weapons: DualWielder and balrond DualMastery work on 1.0, and Smoothbrain's DualWield has 1.0 fixes in its source.
2. **Real gaps:**
   - A maintained "crossbow stays loaded" mod: the only precedent is deprecated.
   - A redesign of how adrenaline is earned, trinkets fired by the player with a key (no mod does it for vanilla trinkets), a tower-shield "wall" stance as a standalone mod, and a better target choice for the vanilla ballista (ReBallista now covers aim and turning).
   - Pack morale (a pack flees when its leader dies), dummies that fight monsters, can be switched off and come in one type per weapon, and multi-player rituals.
   - Typed and contextual pings, and a notification feed confirmed on 1.0.
   - A trinket that trades HP for a short low-HP window (Blood trinket), a stealth bonus for standing still (Sneak revamp) and a roll attack (Weapon revamp).
3. **Design lever.** Adrenaline and trinkets work as one system: the Adrenaline revamp decides how the bar fills (income while fighting, no decay), the Trinket revamp when a trinket fires (a full bar waits for the player's key; vanilla effects and costs), and the Weapon revamp's roll, parry and jump attacks earn a per-move adrenaline bonus but cost none, so the bar is kept for the trinket. Boss powers stay separate (the Boss power revamp only asks for slightly stronger powers). Balrond Battle Flow (2026-09) is the first mod found that reworks adrenaline and trinkets together, but it does not change how adrenaline is earned.
