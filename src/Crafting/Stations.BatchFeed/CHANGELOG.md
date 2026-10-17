# Changelog

## 1.0.0

- First release.

## 0.1.1

- Fixed: the hover hint now follows a station that changes while you keep looking at it. When another mod turns a
  fire into a switch or back (for example Switchable Lights turned on or off), the "x5" line goes away or comes back
  within about half a second. Before, it stayed wrong until you looked at something else.

## 0.1.0

- Initial version.
- A ModifierKey the game cannot read (for example F13, picked in ConfigurationManager) is refused with a warning in
  the log, and batch feeding uses the game's Alternative placement key instead of silently stopping.
