# MC framework — in-game tests

Legend: `[ ]` to test · `[x]` passed · `[!]` failed (add a note) · `[-]` skipped / not applicable.
Automated coverage: `./tools/Test-Framework.ps1` (live toggle, config watching, dependency gating) and
`./tools/Test-Smoke.ps1`. The items below need eyes and hands.

**Build under test:** framework v1 (build id shown next to each mod in the MC Mods panel).

## Framework v1 — panel and notices

- [ ] **F01 Panel button:** main menu shows an **MC Mods (N)** button top-right. Also in the pause menu (Esc) in a
  world. It does not show during normal play.
- [ ] **F02 Panel content:** click it. Each MC mod appears under its category with a toggle, version + build id, scope,
  who needs it, multiplayer support, a coloured status line, and its multiplayer notes.
- [x] **F03 Live toggle:** (passed 2026-09-28 as crossbow T21, build f15f765) in a world, pause → MC Mods → untick Crossbow Stays Loaded. Status turns grey "Off".
  Resume: crossbow behaves like vanilla (reload after swap). Tick it again: the mod works again, no restart.
- [ ] **F04 Clicks don't leak:** with the panel open over the pause menu, clicking toggles never triggers the game
  buttons behind (Logout, Settings...). Clicking the MC Mods button itself never clicks what is under it. Closing
  the panel restores the menu's buttons. On the character-select screen, clicking in the panel does not rotate the
  character. No errors in the log while the panel is open (also over Settings > Graphics).
- [ ] **F09 No button during play:** in a world, open a game popup (e.g. build menu favourites > new category):
  the MC Mods button must NOT appear. It only appears in the Esc menu and main menu.
- [ ] **F05 Esc closes cleanly:** press Esc with the panel open (menu closes). The panel disappears; the game
  controls and cursor behave normally.
- [ ] **F06 UI scale:** panel is readable at your resolution (it scales with screen height).
- [ ] **F07 Config file sync:** toggle a feature in the panel, then open its `.cfg`: `Enabled` and `Status` match.
- [ ] **F08 Spawn notice:** make a feature inactive for a reason other than "Off" (for example run
  `Test-Framework.ps1 -KeepProbes`, turn Probe A off, enter a world). A top-left message says how many features are
  inactive and why.

## Framework v1 — multiplayer (needs a second player and/or a dedicated server)

- [ ] **N01 Client mod on vanilla server:** join a server without mods. Client-side features (Crossbow) stay Active.
- [ ] **N02 Both-side mod, server has it:** (needs a `Both` mod, none exists yet) join a server with the mod: Active.
- [ ] **N03 Both-side mod, server lacks it:** (needs a `Both` mod) join a vanilla server: status "the server does not
  have this mod", feature off, no errors. Leave and load single-player: Active again.
- [ ] **N04 Server turns it off live:** (needs a `Both` mod + dedicated server or host) with a client connected, turn
  the feature off on the server: the client's status becomes "the server has this mod turned off"; on again: Active.
- [ ] **N05 Host notice:** host with a `Both` mod, a friend joins without it: the host's log names that player.
