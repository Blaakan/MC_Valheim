# Music Instruments — design

| | |
|---|---|
| Mod | Music Instruments |
| GUID / project | `MC.Exploration.Music.Instruments` (`src/Exploration/Music.Instruments/`, root namespace `MC.Exploration.MusicInstrumentsMod`, package `MusicInstruments`) |
| Category / scope | Exploration / New |
| Side | **Both** (server or host and every player), multiplayer **Compatible**, network version 1. The server refuses players without the mod, with it turned off or with another network version of it (setting `AllowPlayersWithoutMod`), sends its gameplay settings to everyone and relays the music (section 4). |
| Sheet idea | `music instruments` ("prerecorded songs + midi support for buffs well rested") |
| Game version checked | Valheim 1.0.16 (Unity 6000.0.75f1, Mono), decompiled `assembly_valheim`, `assembly_utils`, `assembly_guiutils` in `.ref/` (SE_Rested, SE_Cozy, SEMan, StatusEffect, Player, Humanoid, VisEquipment, ItemDrop, ObjectDB, ZNetScene, ZNet, ZRpc, ZRoutedRpc, ZPackage, ZDOMan, AudioMan, MusicMan, MusicVolume, GameCamera, Menu, Chat, Terminal, ZInput, PlayerController, Emotes, Chair); research briefs of 2026-10-05 (comfort, audio, existing mods, items, input and UI, network, pose, MIDI and preset songs), each fact-checked by a second agent |
| Status | In development (v0.1.0 code) |

## Goal

The user's expected behaviours (run request, 2026-10-05), numbered. Each one is scope and has at least one test
(section 8).

1. **G1 — Craftable instruments of different tiers:** a Flute (fine wood), a Lyre (silver and linen) and a Tambourine
   (fine wood and leather scraps).
2. **G2 — Ways to play:** built-in songs (presets), MIDI files, and an in-game music mini-game.
3. **G3 — Comfort from the mini-game:** playing the mini-game successfully for 20 s gives an additional +3 comfort.
4. **G4 — Others hear it:** in multiplayer, other players nearby hear the music.
5. **G5 — Others share the comfort:** in multiplayer, other players nearby also get the +3 comfort.
6. **G6 — Assets:** models and icons for the flute, the lyre and the tambourine.

### Added beyond the request (small)

- **E1** The game's background music fades while instruments are heard (personal setting `GameMusicVolume`).
- **E2** A Repeat switch for songs that play by themselves.
- **E3** Songs that play by themselves let the bard walk; every mode works seated on a chair or bench or in the sit
  emote (the place to play is by the fire).
- **E4** Other players see the playing pose (arms posed in code; strums, shakes and bobs follow the notes they hear).
- **E5** MIDI files: the mod picks the part that sounds like the tune for each instrument, and the player can pick any
  other part.
- **E6** The Music effect icon shows the bonus and the time left ("+3 9:41").
- **E7** Lane keys and note speed of the mini-game are personal settings.

### Non-goals

Free play (keys mapped to notes), playing together in time (ensembles), recording, lyrics, an instrument skill,
difficulty levels, hold notes, gamepad lanes, prerecorded audio files (the music is synthesized: no audio files are
shipped or loaded).

### Later (cut from v1, one-line sketches)

- Free play: number keys play the notes of a scale, streamed like the mini-game.
- Ensembles: a second performer joins a performance and plays another part of the same song in time (shared start
  time in the player ZDO).
- Gamepad lanes (D-pad left/right, X, B with own edges: the D-pad auto-repeats).
- Easy / Hard settings (lane count, minimum gap, timing windows), hold notes for long flute notes.
- More instruments (frame drum, horn, harp), a placeable music stand, songbook items found in the world.

## 1. Vanilla behaviour

### 1.1 Comfort and Rested

- Comfort is computed every 2 s on the local player in `Player.UpdateBaseValue` → `SE_Rested.CalculateComfortLevel(Player)`
  and stored in `Player.m_comfortLevel`; `Player.GetComfortLevel()` returns it. The base is 1; **only in shelter**
  (`InShelter`: cover ≥ 0.8 and a roof) do +1 and the best piece of each comfort group within 10 m count. A higher
  value raises the `MaxComfort` profile stat and its platform achievement stat.
- Readers of comfort: the Resting icon text (`SE_Cozy.GetIconText`), the Rested length (`SE_Rested.UpdateTTL`) and the
  "You feel rested (Comfort: N)" message (`SE_Rested.Setup`).
- Resting (`Player.UpdateEnvStatusEffects`) needs: not sensed by enemies, sitting or in shelter, near a fire, not
  cold, wet or burning. After its delay (about 20 s) `SE_Cozy` adds Rested with a time reset every tick, so Rested is
  pinned at `base + (comfort - 1) × perLevel` (wiki: 8 min + 1 min per comfort point) while resting. `UpdateTTL` only
  ever lengthens Rested.
- Status effects tick only on their owner; only an attribute bitmask is synced. `SEMan.AddStatusEffect(int hash)` on
  another player sends `RPC_AddStatusEffect` to that player's owner, which looks the hash up in its own `ObjectDB`
  (unknown hash: nothing happens). Effects are not saved and are cleared on death.

### 1.2 Items and hands

- A `Tool` (hammer, hoe) takes the right hand and empties both; put away it hangs at the hip (`m_backTool`). A tool
  without build pieces attacks with the fists on a click (`Humanoid.GetCurrentWeapon`). Chairs keep the hand item
  (`Chair.Interact`, `hideWeapons: false`); beds, swimming, crafting stations and the Hide key put it away.
- `Player.SetControls` stands a seated or emoting player up on move, attack, block, jump or crouch input unless a
  prefix removes the input first.

### 1.3 Audio

- The listener follows the local player's eyes; mod `AudioSource`s without a mixer group ignore the Master and Sound
  effects sliders (the SFX group of `AudioMan.m_masterMixer` carries them). The game itself never synthesizes audio.
- Unity applies a source's volume, 3D rolloff and panning before custom filters (`OnAudioFilterRead`), so a filter
  that generates sound on a source without a clip is not spatialized.
- `MusicMan` plays one 2D music source; `MusicVolume.UpdateProximityVolumes` gives its target volume every frame and
  MusicMan fades toward it.

### 1.4 Input

- `ZInput` reads the Unity Input System. Vanilla gates (walking, mouse look, hotbar, Use, inventory, map, camera zoom)
  all ask `Chat.HasFocus`; `Menu.Update` (Esc) does not.
- Console key binds fire from `Chat.Update` through `Terminal.TryRunCommand(skipAllowedCheck: true)`.

### 1.5 Network

- Plain `ZRpc` calls ride each connection (reliable, ordered). A malformed parameter read in a plain ZRpc handler
  disconnects the peer (and on a host, logs everyone out), so all our data travels inside a `ZPackage` with a layout
  byte. `ZRpc.Register` replaces an existing handler.
- The server knows each player's character ZDO (`ZNetPeer.m_characterID`, position about 100 ms old) and
  `m_refPos` (2 s old). Network time is not good enough to align notes across machines (overwritten every 2 s with no
  latency compensation).

## 2. Design

### 2.1 Items (G1, G6)

Three items cloned from the vanilla Flint Knife (`KnifeFlint`: its only look is the `attach` child, also used on the
ground) and made Tools like the hammer: `MC_Flute` "Wooden Flute", `MC_Lyre` "Silver Lyre", `MC_Tambourine`
"Tambourine"; recipes `Recipe_MC_<X>`. The knife's look is removed and a model made in code is put in its place,
sized from the knife's length; a collider fitting the model is added for pickup. No build pieces, no attack, no
damage, no durability, no value (silver never turns into coins at a trader), weight 0.5 / 2 / 1, stack 1, quality 1,
teleportable. The tooltip subtitle shows `[Attack] Songs   [Block] Stop` with the player's own keys. An always-on
`Player.PlayerAttackInput` prefix stops the fists from punching while an instrument is in hand (also with the feature
off). Registration (`ObjectDB.Awake` / `CopyOtherDB`, `ZNetScene.Awake`) is always on, so instruments never vanish
from inventories.

Recipes (server rules, all at the workbench; materials gate the tier as in vanilla):

| Instrument | Materials | Station | Tier gate |
|---|---|---|---|
| Wooden Flute | FineWood ×4 | Workbench 1 | fine wood needs a bronze axe (Black Forest) |
| Tambourine | FineWood ×3, LeatherScraps ×4 | Workbench 2 | fine wood |
| Silver Lyre | Silver ×2, LinenThread ×8 | Workbench 2 | silver (Mountains) and linen (Plains); same materials and station as the vanilla Linen Cape |

Models (`InstrumentModels`): an end-blown wooden flute (beak mouthpiece, six holes), a Germanic round lyre (silver
frame, soundbox, yoke with pegs, six linen strings) and a frame drum (wood hoop, leather skin, bronze jingles). Each is
one mesh with sub-meshes and procedural textures on copies of the knife's material, shared by every copy for the
session. Empty marker children (mouth, left hand, strum point, body) tell the pose where to put hands. Icons
(`InstrumentIcons`) are drawn in code with supersampling, like the Spyglass's; the package icon shows the three
instruments together.

### 2.2 Playing (G2)

Holding an instrument, **Attack** opens the song window (cursor shown, game keys held back). It lists the built-in
songs, then the MIDI files of the songs folder (`BepInEx/config/MC_Valheim/Songs` unless `SongsFolder` says
otherwise; `.mid`, `.midi`, `.kar`, `.rmi`; listed at every opening, read when picked). For a MIDI file a part chooser
offers "Automatic" and every part (track and channel). Buttons: **Play** (the song plays by itself), **Perform** (the
mini-game), **Repeat**, **Close** (Esc and the gamepad B also close).

- **Play (autoplay):** the arranged notes are scheduled 0.5 s ahead on the player's own sound source and streamed
  (2.4). The bard may walk (no running, jumping or dodging). Attack or Block stops; the song also ends by itself
  unless Repeat is on. It plays on behind the pause menu, like the game's own music. After a frame hitch (a freeze of
  the game window) notes already more than 0.1 s late are skipped, never played in a burst.
- **Perform (mini-game):** notes fall in four lanes toward a hit line; the lane keys (D F J K by default) must be
  pressed when a note reaches the line. A hit plays that note and the notes the chart leaves out (too close
  together to play) until the next chart note, so a clean run plays the whole song; a miss is silence. The feet stay still, the game's keys are held
  back (`Chat.HasFocus` postfix while `KeyCapture` is held, console binds skipped). Right click, Esc (menu) or any game
  screen ends it. The song loops until stopped.
- Seated players (chair, bench, sit emote) stay seated in every mode: the `SetControls` prefix removes the clicks it
  uses before vanilla sees them, and a click that stops a song or closes the window is also cleared from `ZInput`
  (`ResetButtonStatus`, as the game's own windows do), so the still-held button never reaches vanilla on the next
  frame (no standing up, no block stance switched on with the Toggle block accessibility setting).
- Stops at once: instrument put away or replaced, death, teleport, cutscene, bed, sleep, ship helm, saddle,
  swimming, build mode, debug fly. Ends after a real hit (damage over time does not count), stagger or knockback.

Built-in songs (`PresetSongs`): four traditional tunes in the public domain (Drunken Sailor, Greensleeves, Vem kan
segla förutan vind, Kjerringa med staven; notes from published transcriptions) and four written for this mod
(Hearthfire Lullaby, Row the Longship, Raven's Jig, Mead Hall Reel). Each has a melody (flute, lyre), a chord line
(lyre accompaniment, strummed low to high) and a tambourine rhythm (march, reel, waltz, jig or lullaby) repeated over
the song. Every bar was checked to add up.

MIDI (`MidiReader`, `SongLibrary`): Standard MIDI Files format 0, 1 and 2, RIFF-wrapped files, ticks-per-quarter and
SMPTE timing, a tempo map from every track, running status (kept across meta and sysex events), note-on velocity 0 as
note-off, a repeated note-on ending the held note, notes never closed ending at their track's end, files up to 2 MB,
damaged tails kept up to the damage. The automatic part scores each melodic part like a tune (many notes, much of the
song covered, mostly one note at a time, near the treble, small steps, named "melody", "lead", "flute"...).

Arranging (`Arranger`): the flute plays one note at a time (highest wins) in D4-A6; the lyre plays up to four notes
struck together (top and bottom kept first) in A2-C6 plus, from MIDI, a soft bass line taken from the lowest other
part; whole songs move by octaves into the range, single notes fold in. The tambourine plays hits, not tones: MIDI
drums map to four kinds of hit (thump, hit, jingle, shake), and a song without drums gets one hit per onset of the tune
(at most about seven a second).

### 2.3 Sound (G4)

`SynthCore` (pure C#, no allocation after construction): the flute is a soft wavetable tone with breath noise, a
chiff at the start and vibrato after a moment; the lyre is a plucked string (Karplus-Strong with all-pass tuning and a
pluck shape for a full tone) through a small wooden body filter; the tambourine is a skin part (pitch-dropping thump or
tap) and jingles (noise through metal resonances and ringing partials, retriggered in small bursts). Voices: eight per
source, oldest stolen; a soft limiter keeps the output in range.

`Emitter`: one `AudioSource` per sounding player, looping a clip of 1.0 samples; `OnAudioFilterRead` multiplies the
synth output into that signal, so Unity's volume, 3D rolloff and panning, and the game's SFX mixer group (Master and
Sound effects sliders) apply. The performer hears themself in 2D; others are 3D with a linear fade from 3 m to the
hearing range. Notes reach the audio thread through a small locked list with sample-exact start times. Any audio-thread
error clears the buffer (the 1.0 clip must never reach the speakers) and silences that source; the main thread reports
it once. No sound objects are made on a dedicated server or a game without sound.

### 2.4 Network (G4)

Performer → server → listeners, over one plain `ZRpc` `<guid>.Notes` carrying a `NoteBatch` package (layout byte,
performance id, performer, position, instrument, sequence, send time, flags End / Encore / Live, up to 48 notes as
time, pitch, velocity and length). Autoplay sends every 0.2 s; the mini-game every 0.05 s. The server ignores batches
from players whose mod is missing, off or another version, caps each sender at 40 batches a second, stamps the
performer from the sending connection's own character (a game cannot speak for another player) and forwards the batch
to every compatible player within `max(HearingRange, BonusRange) + 10 m`; a host also plays it for its own player.

Listeners map the performer's clock to their own audio clock with an anchor set by the first batch plus a safety delay
(0.25 s for the mini-game, 0.15 s for autoplay, which already sends ahead); the base anchor then follows the fastest
batch seen, so a first batch that came late is caught up. A late batch pushes the anchor later while the total delay
stays within 0.75 s; a longer stall (crossplay resends after 1-3 s) keeps the anchor, so notes more than 0.1 s late are
dropped instead of all sounding at once and the next batches play on time; when batches come early again the anchor
creeps back toward the base (at most 10 ms per batch and 3 % of the time between batches: no rush is heard). The slack
is measured on the batch's earliest note (autoplay notes lead the send time). The performer's clock never jumps
during a multiplayer performance and the performer sends a batch at least every second, even an empty one, so a long
rest is not taken for a stall and the anchor is never set again. A performance times out 3 s after its last batch but
never before its last scheduled note has sounded (a long held note is heard to its end); if it comes back (stall over,
or the listener walked back into range) it starts over in a new sound source on its old timing. Nothing more is taken
under a performance id after its End (each song gets a new id). The first batch goes out at once. A performer who is not loaded on the listener's game is
heard at the batch's position; one who walks out of hearing range is let go. Single player sends nothing.

The stream is never trusted: the server takes the performer from the sending connection only when that character's ZDO
is owned by the sender's game (vanilla accepts any character id a game sends), drops notes more than 2 s before or 3 s
after their batch's send time, and lets an Encore through only from a live (mini-game) batch and at most once per
0.8 x SuccessSeconds per sender. Listeners apply the same note window and Encore spacing, keep one live performance per
performer (a new id lets the old one go; its ringing tail keeps a slot, an older tail is cut) and hear at most eight
performances at once: when all slots are taken, a ringing tail gives its slot first, else the farthest performance,
but only to a newcomer at least 8 m closer (else the newcomer is not heard; an Encore in its batch still counts). The
server also checks the sender before reading the batch, never builds a performer id from the wire, and accepts a new
performance id from a sender at most once per second.

### 2.5 Comfort (G3, G5)

The mini-game's success meter (`SuccessMeter`) fills with real playing time while the player is "in flow": the
rolling accuracy of the last ten judgements (Perfect 1, Good 0.75, Miss and stray press 0; at least four judgements) is
at least `SuccessAccuracy` (0.7) and fewer than three notes in a row were missed. Out of flow it drains twice as fast;
during a long rest (no note within 1.5 s) it waits. Full (`SuccessSeconds`, 20 s) = **Encore**: the performer gets the
Music status effect, an Encore flag goes into the stream, and every listener whose player is within `BonusRange`
(20 m) of the performer gives the effect to its own player. The meter then starts again: each further 20 s of good play
renews the effect for `BonusMinutes` (10 min). Songs that play by themselves never fill it.

The Music effect (`MusicEffect`, `SE_MC_Music`, registered always-on in `ObjectDB`) shows "+3" and the time left. A
`Player.GetComfortLevel` postfix adds `ComfortBonus` (3) for the local player while the effect is on and, by default,
only in shelter (`BonusOnlyInShelter`, D4/D5). Players resting by the fire therefore get 3 more minutes of Rested.

### 2.6 Pose (E4)

`InstrumentPose` bends both arms in a `Player.LateUpdate` postfix for every player holding an instrument while they
play (the local player from the performance, others from the player ZDO int `<guid>.Playing`), with two-bone IK to
markers in the model: the flute at the mouth pointing forward-down (about 45 degrees below horizontal, like a
recorder; steeper when the player looks down) with both hands on it, the lyre against the chest
with a strumming hand, the tambourine raised and shaken. Notes heard pulse the pose (strum, shake, bob). Only arms and
the item are posed, so a seated player stays seated (`IsSitting` is an animator tag). The animated rotations are put
back before every new pose and when a performance ends.

## 3. Decisions

- **D1 Tools, cloned from the Flint Knife** (same as the Spyglass, research: lightest base, no weapon trail, build mode
  or drink logic). Both hands are emptied, the tooltip says two-handed, the item hangs at the hip when put away.
- **D2 Recipe tiers** follow vanilla: materials gate the tier (fine wood: bronze axe; silver: Mountains; linen thread:
  Plains), stations at workbench level 1-2 like the Finewood Bow, Tankard and Linen Cape. The lyre takes exactly the
  requested silver and linen (no extra wood).
- **D3 Synthesized sound, no audio files**: nothing copyrighted can ship, the download stays small, and every
  listener can rebuild the music from notes (MIDI files never travel over the network).
- **D4 Comfort via `Player.GetComfortLevel`, not `SE_Rested.CalculateComfortLevel`**: the latter is the community's
  usual hook, but its result also raises the `MaxComfort` stat and the platform comfort achievement (Comfort is King,
  comfort 20); music should not make an achievement easier. The getter also shows the bonus at once. Other mods that
  compute comfort themselves to display it do not see the bonus (documented).
- **D5 The bonus counts only in shelter by default** (vanilla-consistent: no comfort source counts outside a shelter;
  outside, comfort is always 1). `BonusOnlyInShelter = false` lets it count at a campfire under the open sky. Flagged
  to the user.
- **D6 Comfort only matters while resting**: the effect lasts 10 minutes and renews while the performer keeps playing
  well, so listeners resting by the fire get the longer Rested; it does not refresh Rested on its own (that would be a
  full Rested refresh anywhere, much stronger than "+3 comfort").
- **D7 Only the mini-game gives comfort** (as requested): autoplay is background music.
- **D8 The server relays notes** (instead of a broadcast routed RPC): only compatible players in range receive them,
  the performer is taken from the sending connection (and must own its character), and players without the mod never
  get unknown messages.
- **D9 Each listener applies the Encore to its own player** after its own distance check (status effects tick only on
  their owner; works also when the performer is not loaded on that game; always the server's rules).
- **D10 Mini-game input**: D F J K by default (the classic rhythm-game home-row keys). The game's own keys are held
  back while it runs (the console key too: `Console.Update` prefix), so most keys may be used; Esc still opens the
  menu and the right mouse button stops, so neither can be a lane key (that lane is turned off, with one warning).
  Keyboard lanes only in v1: on a gamepad the window does not offer Perform (X says the rhythm game is played on the
  keyboard), and the HUD names Start (the menu) as the way out.
- **D11 The bard may walk while a song plays by itself** (feet free, actions blocked); the mini-game holds the feet.
- **D12 Display names "Wooden Flute" and "Silver Lyre"**: known recipes and inventory lookups match item display
  names, and other mods add items called "Lyre" and "Flute".
- **D13 Required everywhere** (user rule for mods that change the experience): new items, a comfort bonus and server
  rules for recipes, comfort and hearing range.

## 4. Multiplayer

### 4.1 Who runs each piece

| Piece | Where |
|---|---|
| Items, recipes, Music effect registration | every game (always on); recipes from the rules in force |
| Song window, mini-game, autoplay, own sound | the performer's game |
| Note relay, join check (`PlayerCheck`), rules push (`ServerRules`) | server (dedicated or host) |
| Hearing others, Encore check, music fade | each listener's game |
| Pose | every game with the mod, for every player who plays |
| Comfort bonus | each player's own game (local player only) |

### 4.2 RPCs, ZDO keys, network version

RPCs `MC.Exploration.Music.Instruments.Settings` / `.SettingsRequest` (rules layout 1) and `.Notes` (note batch layout
1); player ZDO int `MC.Exploration.Music.Instruments.Playing` (stable hash; the instrument being played, 0 = none);
prefabs `MC_Flute`, `MC_Lyre`, `MC_Tambourine`; recipes `Recipe_MC_Flute`, `Recipe_MC_Lyre`, `Recipe_MC_Tambourine`;
status effect `SE_MC_Music`. Network version 1.

### 4.3 Server settings and join check

Copied from Cultivator Replant (itself Spyglass and Swim Dive): the server pushes its rules 0.5 s after the last
change to compatible players; a client uses them for that session and is pending (recipes hidden, no playing, no
bonus) until they arrive; recipes are rebuilt 0.5 s after the rules stop changing. Players without the mod, with it off
or another network version are refused after a 1 s grace unless `AllowPlayersWithoutMod`.

### 4.4 Hand-off cases

- A player without the mod (allowed in) never receives notes (the server sends only to compatible players) and so
  hears nothing; dropped instruments are unknown prefabs for them (never seen); a chest they change loses its
  instruments ("Failed to find item prefab" on load, saved without them); they see an empty hand and no pose.
- An instrument handed over through a chest to a player with the mod but feature off: the item is known (always-on
  registration), Attack does nothing (no punch: the attack guard is always on).
- A Music effect reaching a game without the mod is impossible by design (each game applies it to itself).

### 4.5 Dedicated server

Items are built from the network prefab list's `KnifeFlint`; no mesh, texture, material, icon, sound or UI is made
(no graphics device). The rules, the join check and the note relay run on the server.

## 5. Config

| Section | Key | Default | Range | Scope |
|---|---|---|---|---|
| General | Enabled | true | | own |
| General | AllowPlayersWithoutMod | false | | server only |
| Recipes | FluteRecipe / FluteStation / FluteStationLevel | `FineWood:4` / `piece_workbench` / 1 | text / text / 1-10 | server wins |
| Recipes | LyreRecipe / LyreStation / LyreStationLevel | `Silver:2,LinenThread:8` / `piece_workbench` / 2 | | server wins |
| Recipes | TambourineRecipe / TambourineStation / TambourineStationLevel | `FineWood:3,LeatherScraps:4` / `piece_workbench` / 2 | | server wins |
| Comfort | ComfortBonus | 3 | 0-10 | server wins |
| Comfort | SuccessSeconds | 20 | 5-120 | server wins |
| Comfort | SuccessAccuracy | 0.7 | 0.3-1 | server wins |
| Comfort | BonusMinutes | 10 | 1-60 | server wins |
| Comfort | BonusRange | 20 | 3-50 m | server wins |
| Comfort | BonusOnlyInShelter | true | | server wins |
| Hearing | HearingRange | 40 | 10-80 m | server wins |
| Sound | Volume | 0.8 | 0-1 | personal |
| Sound | GameMusicVolume | 0.3 | 0-1 | personal |
| MiniGame | Lane1Key .. Lane4Key | D, F, J, K | keys | personal |
| MiniGame | NoteSpeed | 1 | 0.5-2 | personal |
| Songs | SongsFolder | empty (= `BepInEx/config/MC_Valheim/Songs`) | path | personal |

## 6. Compatibility

### 6.1 Other MC mods

- **Spyglass** patches the same `Player` methods (`SetControls`, `Update`, `LateUpdate`, `OnDamaged`,
  `PlayerAttackInput`); each acts only for its own item in hand, so they never both act.
- **Sleep Through the Day** patches `Player.SetSleeping`, where waking up adds Rested: with the Music effect on, the
  "You feel rested (Comfort: N)" message (shown only when Rested is new) includes the bonus.
- **Encyclopedia**, **Crafting Search and Sort** OR their own flags into `Chat.HasFocus` and skip console binds the same
  way: all coexist.
- **Dual Wielding** (`Humanoid.EquipItem`, `VisEquipment`): instruments are tools, never paired; equipping one puts a
  pair away (vanilla tool rule).
- **Sort Chest**, **Crafting Search and Sort**: `ItemKinds` files the instruments under Tool by their item type.

### 6.2 Popular external mods

| Mod | Overlap | Effect |
|---|---|---|
| Epic Loot, Jewelcrafting, HeadRest, RuneboundRest, ComfortTweaks (`CalculateComfortLevel` postfixes), BossComfort, DeepNorthHotSprings (`GetComfortLevel` postfixes) | comfort | all additive: bonuses stack |
| RuneboundRest, ComfortTweaks | replace the resting rules / Rested length | our bonus still adds to comfort; how much Rested it gives follows their rules |
| Comfortable, Valheim+, Seasons | Rested length per comfort point | scale what +3 is worth |
| Bardheim, Bragi, Skald of Camelot | other instruments ("Lyre", "Flute", items keyed on G) | different item names and prefabs; no shared patches |
| Viking Shanties, Ominous | write the game music volume | fight over the music fade: set `GameMusicVolume = 1` |
| Skald of Camelot | mutes any sound whose name contains "cat" | none of our names do |
| NotSoLoud, AudioPatch | route group-less sources to the SFX group | ours already use it |
| ComfortAudit and other comfort displays | call `CalculateComfortLevel` | do not show our bonus (D4) |

## 7. Implementation plan

### 7.1 Files

`Plugin.cs` (config, life cycle), `MusicRules.cs`, `ServerRules.cs`, `PlayerCheck.cs`;
`Items/` `InstrumentContent.cs` (items, recipes, effect registration), `InstrumentModels.cs`, `InstrumentIcons.cs`,
`MusicEffect.cs`; `Music/` `Notes.cs`, `MidiReader.cs`, `SongText.cs`, `PresetSongs.cs`, `SongLibrary.cs`,
`Arranger.cs`, `Chart.cs`, `Judge.cs` (pure C#, also built by the offline test harness); `Audio/` `SynthCore.cs`
(pure), `AudioKit.cs`, `Emitter.cs`, `Listeners.cs`; `Net/` `NoteBatch.cs`, `NoteRelay.cs`; `Play/` `Performance.cs`,
`MiniGame.cs`, `MusicBonus.cs`, `KeyCapture.cs` (with `LaneKeys`, `GameScreens`); `Pose/InstrumentPose.cs`; `Ui/`
`SongWindow.cs`, `MiniGameHud.cs`; `Patches/` (`ObjectDBPatches`, `ZNetScenePatches`, `PlayerAttackGuardPatches`
always on; `PlayerPatches`, `ComfortPatches`, `InputPatches` (chat focus, console binds, Esc, cursor, world exit,
music fade), `ZNetPatches`); `SelfTests.cs`.

### 7.2 Turning off

`OnDeactivated`: the performance stops at once (sound hushed, End sent, window and HUD closed, ZDO int 0), every
listener emitter is destroyed, every pose is put back, recipes are hidden, the relay, join check and rules stop. Items
and the Music effect stay registered; an effect already running stays on its icon but adds no comfort (the comfort
patch is off). World exit (logout, disconnect, quit): a `GameCamera.OnDestroy` postfix shuts the performance and the
listeners down.

## 8. Test plan

Offline (scratch harness, pure code): MIDI reading (formats, tempo map, running status, retrigger, SMPTE, RIFF,
truncated and random data never throwing), song text and chords, every preset on every instrument (ranges, chart
lanes), MIDI folder scan, load cache and part choice, arranger rules, judge and success meter (perfect, 85 %, 40 %,
masher, idle players), synth (pitch within 12 cents, peaks, silence after notes, voices freed, CPU).

In-world self-tests (Debug): `music.network` (rules and note batch wire, clamps, rule selection), `music.item`
(registration, recipes under default rules, tool, no punch also with the feature off, recipes hidden while off, hand
model, dropped copies screenshot), `music.synth` (filter called, sound out, left/right panning of a source to the right,
silence after hush, mixer group), `music.songs` (presets and a generated MIDI file through the library),
`music.perform` (mini-game with simulated presses: Encore, Music effect, comfort +3 with the shelter rule off, no bonus
outside with it on, stop; a half-right run gets no Encore), `music.autoplay` (notes sound, ZDO flag, pose weight, music
fade, putting the instrument away stops), `music.listen` (another player's batches heard at their place, Encore in range gives
the effect and out of range nothing, notes far from their batch time dropped, a restart keeps the old timing, a
batch late after a 2 s stall has its notes dropped with the delay still bounded, nothing taken after End, End frees
the emitter), `music.window` (window and HUD
screenshots, Perform from the window), `music.pose` (markers near targets, screenshots, also as others see it),
`music.export` (icons). Hands-on list: `src/Exploration/Music.Instruments/TESTING.md`.

## 9. Open questions and unverified

- The sound of the three synthesized instruments, their loudness against each other and the game (to tune by ear).
- Instrument sizes in hand use the Flint Knife length measured in game (0.379 m); the in-world screenshots show
  real-size instruments. The flute (36 cm, 2.4 cm thick, like a real recorder) is mostly hidden behind the large
  Valheim hands from the front: if it reads poorly in game, make the model thicker or longer (markers and hand
  offsets follow the model). Pose offsets (mouth position, lyre lean) still to judge in game.
- `SE_Cozy` delay and Rested values come from prefab data (wiki: 20 s, 8 min + 1 min per comfort).
- Checked in game (2026-10-05): Unity 6 applies the spatial gains before custom filters (`music.synth`: a source on
  the right peaks 0.178 right, 0.023 left), and the "SFX" mixer group is found by name (a warning is logged if a
  later game version renames it).
