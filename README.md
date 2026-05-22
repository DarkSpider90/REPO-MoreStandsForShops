# MoreStandsForShops

MoreStandsForShops is a shop expansion mod for R.E.P.O. v0.4.0.

It adds extra shop stands and rewrites selected vanilla shop volumes while keeping the normal shop item spawning flow. The host performs shop mutations in multiplayer, and clients receive synchronized visual placement through Photon room properties.

## Features

- Passive second upgrade stand with curated preset placement.
- Dedicated drone and power crystal stand.
- Drones and power crystals are kept on the custom stand instead of vanilla tables.
- Vanilla health shelf rewrite for health packs and small items.
- Multi-size table slot support for medium, large, and large_high items.
- Configurable item counts by category.
- Configurable same-item copy limits.
- Per-item spawn chance weights.
- Optional shop pool limit override so extra slots keep trying to fill.
- Multiplayer-oriented host/client placement synchronization.
- Compatibility handling for custom gambling-style shop module layouts.

## Requirements

- BepInEx 5.4.23.5
- R.E.P.O. v0.4.0

All players in a multiplayer lobby should install the same mod version.

## Configuration

The config is created automatically after first launch:

`BepInEx/config/DarkSpider90.MoreStandsForShops.cfg`

Main sections:

- `General`
  - Enable or disable the mod.
  - Enable or disable the additional upgrade stand.
  - Enable or disable the vanilla shelf/table rewrite.
  - Enable or disable the shop pool limit override.
  - Enable debug logs.

- `Item Counts`
  - Controls category item limits.
  - Setting a category to `0` disables that category.

- `Same Item Copies`
  - Controls how many copies of the exact same item can appear.

- `Item Spawn Chances`
  - Per-item relative chance weights.
  - `0` disables an item in this mod's shop pools.
  - `100` is the default weight.

## Installation

### Mod Manager

Install with a Thunderstore-compatible mod manager.

### Manual

1. Install BepInEx.
2. Place `MoreStandsForShops.dll` into:

`BepInEx/plugins/MoreStandsForShops/`

## Credits / Inspirations

Created by DarkSpider90.

This mod is an original implementation built around R.E.P.O.'s vanilla shop systems, Unity scene objects, and Photon multiplayer synchronization. Existing community knowledge about R.E.P.O. modding and observed vanilla behavior helped guide compatibility decisions, but the code and package identity are maintained as an original DarkSpider90 project.

## License

MIT. See `LICENSE`.
