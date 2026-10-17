# Changelog

## 1.0.0

- First release.

## 0.1.1

- Fixed a tame that was never counted: a creature that finished taming while you had no character in the world (in
  the seconds between your dead body disappearing and your respawn, or while the world was still loading) was tamed
  by the game but missing from the per-creature tame counts. Such a tame is now kept and counted as soon as your
  character appears.

## 0.1.0

- Initial version.
- ConfigurationManager lists SortBy before ShowWeaponTypes.
