# Changelog

## 1.1.2

* Preserved vanilla-authored table item positions, rotations, and volume types instead of fabricating unsafe large-item variants at unrelated slots.
* Fixed client shop layouts being skipped across rooms or marked complete after a failed scene lookup; clients now retry safely for longer.
* Kept layout sequence numbers monotonic across master-client changes.
* Synchronized upgrade reroll count, cost progression, break threshold, and broken state for late joiners and master-client migration.
* Prevented simultaneous remote users from taking over each other's upgrade-stand hold interaction.
* Stopped category shop settings from overwriting the global carried-item `maxAmount` limit.
* Added prefab, player-count, purchase-limit, duplicate-registration, and final-count validation to custom shelf and reroll candidates.
* Limited scene caches to the active scene, fixed stale duplicate-shelf detection, and removed the per-frame allocating button sphere cast.

## 1.1.1

* Fixed the total upgrade limit being incorrectly applied as a per-upgrade purchase limit.

## 1.1.0

* Fixed item count and duplicate spawn limits for C.A.R.T. weapons.
* Optimization fixes

## 1.0.0

* Initial release as MoreStandsForShops.
* Adds a passive second upgrade stand.
* Adds a dedicated drone and power crystal stand.
* Reworks selected vanilla shop shelf/table item volumes.
* Adds configurable item counts, same-item copy limits, and per-item spawn chance weights.
* Adds host/client shop placement synchronization through Photon events.
* Includes MIT license and original DarkSpider90 package metadata.
