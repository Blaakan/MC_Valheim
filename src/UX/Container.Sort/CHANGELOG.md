# Changelog

## 0.1.1

- Fixed: after a Sort click that had nothing to move (for example right after you dragged a stack out of the chest and
  put it back in the same slot), every later Sort click was refused with "This container changed elsewhere. Close it
  and open it again to sort." until you closed and reopened the chest. Sort now keeps working, also after you switch
  the sort order.
- Changed: the sort order button is used with the mouse only. It has no controller key and no controller glyph any
  more: the left stick click it asked for is the game's own Take all (so it never changed the sort order), and the
  right stick click is the game's own Place stacks. Sort Chest leaves both to the game. Sorting with View/Select while
  the chest's slots are selected works as before.
- Fixed: the log no longer gets the warning "The game's 'TakeAll' button already uses the controller key ..." each time
  the buttons are created.
- Fixed: the log no longer gets a "Sort buttons stick out of the container panel" warning the first time a container
  size is opened. The buttons stay where they were: in the game's row of Take all and Place stacks, to the right of
  Place stacks, just past the edge of the container panel.
- Fixed: tankards sort in the "Other" group again (last in the "By type" order). They landed between the pickaxes and
  the torches.

## 0.1.0

- Initial version.
