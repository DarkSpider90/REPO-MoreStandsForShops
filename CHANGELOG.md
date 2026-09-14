# Changelog

## 1.1.17

* Made the Photon Blaster and Semibot Walkies display adjustment a deterministic +90-degree yaw instead of choosing between opposite quarter turns from overlap measurements.
* Filled unavailable configured table-category entries from the remaining enabled weighted table pool, up to the configured table total and the 16 physical adaptive positions, while still respecting per-item Same Item Copies limits.
* Kept carts, pocket carts, vehicles, dedicated shelves, upgrades, item authority, and first-grab stabilization outside the table fallback.
* Power Crystal shelves no longer lose configured slots because crystals were purchased in previous shops; every shop now attempts the full current configured count.

## 1.1.16

* Disabled automatic table-item yaw searching for ordinary items, preserving each prefab's authored upright direction instead of turning it sideways to reduce overlap.
* Kept the dedicated 90-degree display turn for Photon Blaster and added the same rule for Semibot Walkies.
* Preserved upright X/Z correction, first-grab stabilization, full table stock, and authoritative multiplayer rotation synchronization.

## 1.1.15

* Fixed the reroll timing regression introduced by the transactional refactor: network replacements are still created before originals are removed, but now remain non-colliding and kinematic throughout the rolling-compartment animation and are physically revealed only in the original `OpenHatch` phase.
* Waits for vanilla `PhysGrabObject.EnableRigidbody` before clearing velocity and sleeping the final pose, eliminating kinematic-body warnings and preventing delayed vanilla initialization from undoing placement.
* Preserved reliable room-object creation, rollback on spawn failure, host authority, exact synchronized slot poses, normal first-grab physics, and every reroll visual/state transition.

## 1.1.14

* Prevented rerolled upgrades from being scattered by physics while the transaction briefly contains both the original and its replacement in the same stand slot.
* Replacement colliders and Rigidbody simulation are now suppressed only during the atomic commit, then the exact synchronized slot pose and the prefab's original physics settings are restored with zero stale velocity.
* Preserved the loss-safe transaction order: every network replacement is still created successfully before any original upgrade is removed.

## 1.1.13

* Fixed multiplayer lobby creation by registering table-stabilization host-migration callbacks only while the shop scene is active, instead of during plugin startup.
* Added room-state guards and exception containment so optional table-display recovery can never abort Photon lobby or room flow.
* Preserved first-grab stabilization and host-migration recovery inside the shop, with direct Photon authority checks that do not access game-run singletons from network callbacks.
* Added a regression test that prevents shop-only network callbacks from being moved back into lobby startup.

## 1.1.12

* Added authority-only table-item stabilization: standard table stock keeps its selected upright rotation and horizontal slot position until its first local or remote grab, then restores the prefab's exact Rigidbody constraints without continuous transform writes or extra Photon serialization.
* Persisted only the still-ungrabbed table item ViewIDs so a new master can safely recover stabilization after host migration without ever re-freezing an item that has already been used.
* Reduced C.A.R.T. overlap correction to one cached post-population fallback scan plus local non-allocating overlap queries, and stop delayed retries after the first clear post-population pass.
* Stopped settled reroll button and mesh springs from doing permanent idle work while preserving every active animation, interaction, fire effect, and synchronized state transition.
* Expanded the zero-dependency regression suite for reversible first-grab stabilization, migration recovery, bounded cart auditing, and idle animation work.

## 1.1.11

* Preserved every existing shop feature while preventing copied vanilla scene ViewIDs from registering on local stand visuals, then making their inherited PhotonViews inert before activation; item PhotonViews, room-object spawning, synchronized rotations, stand layout properties, and reroll events remain intact.
* Made upgrade rerolls transactional: the host stores the exact replacement plan, creates every replacement before removing any original, safely rolls back failed spawns, and allows a new master client to recover an interrupted reroll.
* Released a disconnected player's upgrade-stand hold ownership and rejected stale stand events from an earlier shop layout or a non-master sender.
* Reduced shop loading work by deriving component caches from one scene scan, caching dedicated-shelf candidates, retaining partial client layout progress with bounded backoff, and using local physics lookup before the compatibility-wide table-item fallback.
* Deduplicated delayed C.A.R.T. overlap audits, cached spawned stand references, made shelf area preparation transactional, and added deterministic synchronized paths for duplicate hierarchy names.
* Added regression tests covering stock categories, special item rotations, networked item creation/destruction, stand interaction synchronization, reroll transaction recovery contracts, and the new loading-performance invariants.

## 1.1.10

* Restored full table stock by keeping every successful vanilla spawn; intersecting items now use the upright 90-degree orientation with the smallest collider-footprint overlap instead of being removed or replaced.
* Added a Photon Blaster display rule that turns its inherited cross-table orientation by 90 degrees while preserving normal physics and network synchronization.
* Changed the Shockwave Grenade shelf override to 90 world-space X degrees so it lies along the shelf depth.

## 1.1.9

* Limited intersection correction to one 90-degree yaw turn; items that still overlap are removed before physics can disturb the table and replaced by the next compatible configured item.
* Prioritized not-yet-displayed item identities when choosing an overlap replacement, while retaining vanilla queue order and allowing duplicates when no unseen candidate remains.
* Changed the Shockwave Grenade shelf override from 90 to exactly 180 world-space Z degrees.

## 1.1.8

* Removed the temporary first-grab Rigidbody stabilization and restored the normal vanilla physics lifecycle.
* Removed the inherited 11-14 degree wall-lean from universal table volumes so table items spawn with upright X/Z rotation at the already-correct vanilla height.
* Added one-time collider-footprint placement before physics activation: overlapping table items now try alternate upright yaw rotations instead of spawning intersected or stacked.

## 1.1.7

* Prevented stabilized table items from drifting by locking horizontal movement and rotation until first grab while leaving vertical settling enabled.
* Forced Shockwave Grenade's final world-space Z rotation to exactly 90 degrees and added before/after rotation diagnostics.

## 1.1.6

* Stabilized universal-table items at their authored display rotation until first grab, corrected the Shockwave Grenade rotation order, and excluded cart trigger volumes from delayed overlap correction.

## 1.1.5

* Changed configured stock selection to weighted random draws from the full eligible category on every draw, so duplicate items can appear immediately instead of waiting for a forced one-of-each pass.
* Kept drones/crystals, grenades/health packs, and upgrades in their dedicated display mechanisms and prevented those categories from leaking into the standard table pool.
* Rebuilt the 16 possible vanilla table positions as two evenly spaced table rows with universal small/medium/large compatibility, while retaining same-row vanilla height and rotation templates to reduce item intersections and falls. Vanilla's default standard-item target remains 8.
* Made the health shelf host-controlled, selected configured health positions evenly across the shelf, and retained vanilla-authored health-pack height and rotation.
* Spawned Shockwave Grenades sideways on the grenade shelf so they no longer catch inside the shelf.
* Prevented the additional upgrade stand from using a C.A.R.T. spawn volume and added a Photon-synchronized delayed cart-overlap fallback.

## 1.1.4

* Restored a full vanilla weapon/tool table by rebuilding its 16 physical places as adaptive slots; every slot keeps its own table position, type-correct height and row-relative rotation, while large items use alternating lower places for clearance.
* Rebuilt shop pools from the complete registered vanilla/modded item catalogue so previous purchases no longer reduce the current shop's configured stock (including power crystals).
* Ensured eligible items receive one selection pass before duplicate copies whenever a category has enough configured room; per-item weights and Same Item Copies settings still apply.
* Separated grenade shelf stock from the standard-table budget and added post-population diagnostics for any configured items that have no compatible display place.
* Kept vanilla host-authoritative item instantiation and added per-step fallbacks so a faulty third-party item cannot abort the entire shop population pass.

## 1.1.3

* Fixed third-party item names containing BepInEx-reserved characters preventing the shop from loading; unsafe names now receive stable sanitized config keys, isolated config failures fall back to the default spawn weight, and shop-pool callbacks can no longer abort vanilla initialization.

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
