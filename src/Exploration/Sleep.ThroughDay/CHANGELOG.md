# Changelog

## 0.1.1

- Fixed: a sleep that started after nightfall (18:00) could be taken for a daytime sleep and wake you with "Good
  evening" when it ended on the same day, which only happens when another mod stops the game from skipping the night.
  Only a sleep that starts during the day (06:00 to 18:00) now counts as a daytime sleep; a sleep that starts at
  night always wakes you with "Good morning".

## 0.1.0

- Initial version: lie down in your bed in the morning and wake up at nightfall of the same day (18:00 by default),
  with the usual sleep screen, time skip and Rested bonus, and a "Good evening" message.
- Settings: wake-up hour (13 to 23), afternoon sleeps ending at the wake-up hour too (off by default), wake-up
  message.
- Required on the server (or the host) and on every player's game; turns itself off on servers without it.
