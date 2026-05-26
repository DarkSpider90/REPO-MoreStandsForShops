# MoreStandsForShops

MoreStandsForShops makes the R.E.P.O. shop feel fuller, cleaner, and a little more useful between runs.

I made it for games where the shop starts to feel too small once you play with friends, extra money, extra items, or a bigger mod list. Instead of leaving everything fighting for the same few table spots, the mod adds more proper places for items to appear.

## What It Adds

- A second upgrade stand.
- A dedicated shelf for drones and power crystals.
- Reworked use of the vanilla health shelf, with health packs on top and grenades below.
- Additional spawn space for weapons, tools, staffs, and other large shop items.

The goal is not to turn the shop into chaos. The goal is to give the game more room to breathe.

## Settings

The config is created after launching the game once with the mod installed.

### General

This section controls the big switches.

- Enable or disable the whole mod.
- Enable or disable the extra upgrade stand.
- Enable or disable additional shelves and table layout changes.
- Enable or disable custom item pool changes.
- Enable debug logs if you want to check what the mod is doing.

### Item Counts

This is where you decide how much of each item type can show up.

Set something to `0` if you do not want that category to appear through this mod.

For example:

- More upgrades if your group likes heavy upgrade shopping.
- Fewer drones if they feel too common.
- Fewer C.A.R.T. Cannons if they are taking over the shop.
- More health packs if your team is having one of those nights.

### Same Item Copies

This controls how many copies of the exact same item can appear.

If you want variety, keep these lower.  
If you do not mind seeing repeats, raise them.

### Item Spawn Chances

Every item can have its own chance weight.

- `100` is the normal default.
- `0` disables that item from this mod's shop pools.
- Higher numbers make an item more likely compared to other items in the same group.
- Lower numbers make it rarer.

This is useful if you like the item pool overall, but want to tune the mood of your shop.

Spawn weights are relative within the same item category.

## Multiplayer

For the best multiplayer experience, everyone in the lobby should use the same mod version and preferably similar config settings.

The host controls the shop layout and synchronized item pools.

## Notes

This mod tries to stay close to the normal shop feeling. Items should still feel like they belong in the shop, just with more space and better organization.

## Credits / Inspirations

Special thanks to Jettcodey and other R.E.P.O. modders whose earlier shop-related mods helped inspire experimentation with custom stand systems and multiplayer shop layouts.
