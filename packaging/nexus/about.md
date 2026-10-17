## How these mods are made

Each mod starts as an idea on my list and goes through the same steps: a design document that records every decision
and the game code it relies on, a list of in-game tests, and automated tests that run inside a real game (and on a
real dedicated server for the multiplayer parts) before every release. I design the mods, decide what they do, and
play and test them in game; the code, the test tooling and these texts are written with AI help (Claude, by
Anthropic), and every change is reviewed against the game's own code.

Everything is public on GitHub: the source, the design documents, the test lists and the full development history:
[github.com/Blaakan/MC_Valheim](https://github.com/Blaakan/MC_Valheim).

Found a bug, or a mod that does not play well with another one? Tell me in the comments or the Bugs tab, with your
BepInEx/LogOutput.log if you can.
