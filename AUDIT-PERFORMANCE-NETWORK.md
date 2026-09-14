# Performance, physics, and multiplayer audit

Audit target: MoreStandsForShops 1.1.15
Audit date: 2026-09-02
Scope: production-code review, current game log review, stability implementation, and non-Unity regression tests.

The 1.1.12 stability, network-safety, and loading optimizations are implemented. Version 1.1.13 additionally scopes table-migration callbacks to active shop sessions after the always-on registration caused multiplayer lobby creation to abort. The in-game multiplayer matrix remains a release requirement.

## Executive result

The audited pre-1.1.12 log contains no MoreStandsForShops exception, Photon timeout, or disconnect.
The only logged patch failure belongs to REPO Fidelity. The mod's current one-shot table
rotation does not run every frame.

A safe first-grab stabilization can be inexpensive if it sets Rigidbody constraints once
and restores them once. It must not repeatedly write transforms, call `Teleport`, or call
the vanilla `FreezeForces` path every frame. Fixation does not solve the main multiplayer
scaling cost: vanilla serializes every networked physical item at 25 Hz even while it is
sleeping.

## Findings

### P1 — physical stock scales network traffic linearly

Status in 1.1.11: inherent engine cost; intentionally not capped or virtualized because doing so would change mod functionality.

The game sets both `PhotonNetwork.SerializationRate` and `SendRate` to 25. The vanilla
`PhotonTransformView.OnPhotonSerializeView` sends sleeping state, teleport state,
kinematic state, two velocities, position, movement direction, and rotation on every
serialization pass. `PhysGrabObject.OnPhotonSerializeView` also sends velocities and
physics flags.

The raw values are roughly 93 bytes per item per serialization pass before Photon event,
object, and protocol overhead. That is a lower-bound estimate of about 2.3 KB/s per item
at 25 Hz. With roughly 68 physical shop items (the current maximum display order of
magnitude), the raw state alone is around 158 KB/s before overhead. Five hundred physical
items would exceed 1.1 MB/s before overhead and would also create a very large late-join
room-object burst.

The current code also cannot display an arbitrary 500 standard items: the adaptive vanilla
tables provide 16 physical groups, the upgrades provide at most 28 configured places, and
the dedicated shelves provide their own finite places. An unlimited candidate pool is not
the same as unlimited physical display capacity.

Risk: host upload saturation, Photon send-queue growth, long late-join loading, physics and
script spikes, and eventually timeouts on weaker connections. If hundreds of stock entries
must be available, use a paged/virtual catalogue or reroll mechanism instead of hundreds of
simultaneous Photon room objects.

### P1 — the first cached stand clone can retain Photon components for one frame

Status in 1.1.11: fixed for the cached prefab and every usable stand clone. Copied vanilla scene ViewIDs are suppressed during cloning, their runtime identities are cleared without invoking PUN's unregistering setter, and the inherited PhotonViews are disabled before activation. Item PhotonViews and the mod's event synchronization are untouched.

The original implementation cloned an active vanilla object, then called `Object.Destroy` on a
`PhotonView`, and immediately instantiated the cached clone in the same frame. Unity's
`Destroy` is deferred until the end of the frame. The runtime log supports this finding:
the first custom upgrade stand disabled two child PhotonViews, while later instances
disabled one.

Risk: duplicate scene ViewID registration, unwanted vanilla callbacks/RPC observation on a
local-only visual, or a multiplayer-only initialization failure. The safe fix is to build
the private inactive clone without ever enabling copied network behaviour, synchronously
remove/reset all copied PhotonViews before the first usable clone, and verify that the
spawned custom visual contains zero enabled PhotonViews. The implemented clone helper
temporarily clears only the source objects' serialized scene IDs, restores them in a
`finally` block, and clears the clone's private runtime ID directly; using `ViewID = 0`
would be unsafe because this PUN build removes that numeric ID without verifying which
object owns the registered dictionary entry.

### P1 — host migration is incomplete during rerolls

Status in 1.1.11: fixed with a room-persisted replacement plan, spawn-before-destroy commit, master-switch recovery, and disconnected hold-owner cleanup.

Pending upgrade replacements exist only in the current master's local controller lists.
If the master leaves after originals are destroyed but before replacements spawn, the new
master cannot reconstruct the transaction. A client leaving while it owns the remote hold
also leaves `remoteHoldActorNumber` without an explicit player-left reset, which can reject
other clients' hold requests until the state returns to idle.

Risk: missing upgrades, a temporarily locked reroll button, or divergent animation after
host migration. Persist a transaction phase and replacement identities in host-owned room
state, cancel or recover on master switch, and clear remote hold ownership in
`OnPlayerLeftRoom`.

### P2 — client retry can multiply global scene scans

Status in 1.1.11: fixed with per-stand progress, cached stand references, and a bounded 0.25–2 second retry backoff.

`ClientShopLayoutApplier` retries four times per second for up to 30 seconds. A partial
layout failure can repeatedly call both `FindExistingSpawnedStand` methods, which use
`Resources.FindObjectsOfTypeAll<Transform>`. In the audited log the scene contained
4,138–4,986 transforms and 1,985–2,459 renderers.

Risk: a client with one missing scene reference can perform hundreds of full global scans
during loading. Cache the spawned visual references per scene, track upgrade and shelf
application independently, rebuild a stale client scene cache before retrying, and back off
the retry interval.

### P2 — duplicate cart audit schedules perform repeated full searches

Status in 1.1.12: fixed by deduplicating both callers onto one delayed audit routine, using local non-allocating overlap queries, caching the single post-population fallback scan, and stopping after the first clear post-population pass.

The upgrade stand schedules a five-step cart recheck when it spawns and schedules another
five-step routine after vanilla population. Together with the initial check, this can call
the cart audit eleven times over about 14 seconds. Every audit performs an overlap query,
global `ItemAttributes` and `PhysGrabCart` searches, and bounds aggregation.

Risk: intermittent host spikes after entering the shop. Keep one deduplicated routine,
prefer overlap-region candidates, and stop after the first post-population pass finds no
intersection.

### P2 — one-time shop initialization still has several synchronous scan/allocation spikes

Status in 1.1.12: reduced without changing selection results. The scene cache uses one global scan, shelf candidates are cached once, table placement tries a local non-allocating physics lookup before its compatibility fallback, and C.A.R.T. recovery performs only one cached global fallback scan.

The scene cache performs three global `Resources.FindObjectsOfTypeAll` calls and constructs
a hierarchy path for every active transform. Table placement performs a global
`FindObjectsOfType<ItemAttributes>` for every table spawn and can call
`Physics.SyncTransforms` for several candidate rotations. Dedicated shelf selection
re-enumerates and sorts the complete item dictionary for every selected shelf position.

These operations are finite rather than per-frame, but they directly extend shop loading.
Prefer one root hierarchy traversal, capture the just-instantiated object without a global
search, and cache eligible shelf candidates once per zone.

### P2 — initialization is not transactional

Status in 1.1.11: fixed for stand creation paths by validating before mutation and restoring recorded decor if later creation/configuration throws.

Some spawners disable decorative objects before all later operations succeed. If prefab
creation, volume configuration, or room-property publication fails afterward, the host can
retain disabled decor while clients receive no matching layout. Similar partial success on
the client causes the whole layout to retry.

Risk: host/client visual mismatch and repeated loading work. Collect a mutation plan first,
apply it only after validation, and roll it back on failure.

### P2 — reroll events are not scoped to a shop layout sequence

Status in 1.1.11: fixed. New events include the layout sequence and stand identity, validate the master sender, and retain legacy-payload acceptance only for an already queued update event.

Event code 187 is protected by the `MSFS_REROLL_V1` payload discriminator, so another mod
using the same event code is ignored safely. However, the payload has no layout sequence or
stand identity. A delayed reliable event from the previous shop can be consumed by the new
stand in the same room.

Risk: stale hold, reroll, or broken visuals after a level transition. Include the room
layout sequence and stable stand identity in every event and reject stale messages.

### P2 — synchronized scene paths are not guaranteed unique

Status in 1.1.11: fixed with a sibling-index signature only when a full name path is duplicated; legacy name-only lookups remain available.

Scene paths are constructed only from object names and contain no sibling index. Duplicate
names under the same parent map to the first cached transform. The host and client normally
share hierarchy order, but a modded hierarchy can make a disabled path target ambiguous.

Risk: a different decorative object is disabled on a client. Add sibling indices or a
deterministic component-local identifier to synchronized paths.

### P3 — debug logging is currently enabled locally

Status in 1.1.11: shipped default remains disabled; the local diagnostic config is intentionally left under user control.

The audited game config has `Debug Logs = true`. A single shop load writes hundreds of
messages for slots, candidates, paths, and selected items. Logging is useful for this
investigation but creates strings and synchronous disk work during the exact loading path
being measured.

Set it to false for normal multiplayer performance tests. The shipped default is already
false.

### P3 — permanent upgrade-stand Update work can be reduced

Status in 1.1.12: fixed with a collider-bounds distance rejection that is mathematically outside both the ray and sphere-cast reach, plus settled-state guards for the button and mesh springs. Every active animation/state update is preserved.

The custom stand performs a raycast and usually a sphere cast every frame even when the
player is far away, and writes several visual transforms every frame while idle. There is
only one custom stand, so this is not currently severe. A squared-distance early-out and
idle visual short-circuit would remove most of the steady cost.

## Implemented safe stabilization

Implemented in 1.1.12:

1. Run only on the master/single-player authority.
2. Wait until vanilla `ItemAttributes.Start` has applied its item-volume pivot correction
   and at least one physics step has completed.
3. Save the original Rigidbody constraints once.
4. Apply `FreezePositionX | FreezePositionZ` and the required rotation constraints once,
   while leaving Y free to settle. Do not literally freeze Y unless floating stock is
   desired.
5. Do not write `transform.position`, `Rigidbody.position`, or call `Teleport` each frame.
6. Poll only the public authoritative grab state, restore the original constraints once on
   first grab, wake the body, and remove the helper permanently.
7. Clean up on level exit and persist only still-ungrabbed Photon ViewIDs for safe
   master-switch recovery. The vanilla first-grab flag prevents a used item from being
   constrained again.

For 16 table items, a tiny component doing a few field checks per frame is negligible next
to the existing item scripts, physics, and Photon serialization. Setting constraints once
does not add Photon payload because constraints are not serialized. It also does not reduce
the unconditional 25 Hz vanilla item payload. Repeated `Teleport` would keep bodies awake,
force client corrections, and should not be used as stabilization.

## Automated coverage added

`Tests/MoreStandsForShops.AuditTests` provides zero-dependency regression checks for:

- every stock-category/config-budget mapping;
- dedicated-category exclusion from the standard table budget;
- exact C.A.R.T. weapon routing;
- unique and valid upgrade-stand presets;
- unique namespaced Photon room-property keys;
- one-shot, non-destructive table placement architecture;
- reversible authority-only first-grab stabilization and master-switch recovery;
- Photon Blaster and Shockwave Grenade rotation contracts;
- room-object item spawning/destruction and reliable stand events;
- transactional reroll plan encoding, spawn-before-destroy ordering, and recovery hooks;
- copied scene-ViewID suppression before local stand activation;
- incremental client layout retries and deterministic duplicate scene paths;
- one global scene-cache scan and deduplicated delayed C.A.R.T. checks;
- settled-state guards for permanent reroll-stand spring work;
- synchronized package and assembly versions.

These tests do not simulate Unity physics or a Photon room.

## Required in-game integration matrix

Run with debug logging disabled and record Unity Profiler CPU/GC/Physics plus Photon traffic:

1. Single-player: 10 consecutive shop entries at default and maximum configured stock.
2. Two clients: host enters first, client enters first, simultaneous entry, and late join.
3. Four clients with 100–200 ms latency and packet loss simulation.
4. Master leaves while idle, while a remote player holds reroll, before replacement spawn,
   immediately after replacements are created, and while original-destroy events propagate.
5. Remote holding player disconnects without sending hold-stop.
6. One client lacks a third-party item prefab used by the host (expected compatibility
   failure must be explicit and bounded).
7. Maximum supported physical stock: compare upload, receive rate, send queue, RTT, and
   late-join duration against vanilla stock.
8. First-grab stabilization: verify remote first grab, throw, host migration, sleep state,
   bandwidth, and physics cost.

Release gates should include zero MoreStands exceptions, no duplicate PhotonView warning,
no stuck reroll owner after disconnect, complete host-migration recovery, and bounded
late-join time at the documented supported stock maximum.
