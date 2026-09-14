using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MoreStandsForShops.Network;
using MoreStandsForShops.Utilities;
using Photon.Pun;
using UnityEngine;

namespace MoreStandsForShops.Shop;

/// <summary>
/// Corrects the non-upright rotations authored for wall-supported vanilla slots and
/// preserves the authored yaw for ordinary table items. Photon Blaster and Semibot
/// Walkies receive their dedicated quarter-turn display rule. No item is removed or
/// replaced by this controller.
/// </summary>
internal static class ShopTableItemPlacementController
{
    private const float ClearOverlapArea = 0.0001f;
    private const float FootprintPadding = 0.015f;
    private const float SpawnLookupMaxSqrDistance = 0.04f;

    private static readonly float[] StandardYawTurns = { 0f };
    private static readonly float[] QuarterTurnYawTurns = { 90f };
    private static readonly List<ReservedFootprint> ReservedFootprints = new();
    private static readonly HashSet<int> PlacedObjectIds = new();
    private static readonly Collider[] SpawnLookupHits = new Collider[64];
    private static readonly HashSet<int> StabilizedViewIds = new();
    private static readonly List<ShopTableItemStabilizer> ActiveStabilizers = new();
    private static Coroutine _masterRecoveryRoutine;
    private static bool _populationFinalized;

    internal static void ResetForShop()
    {
        if (_masterRecoveryRoutine != null && Plugin.Instance != null)
            Plugin.Instance.StopCoroutine(_masterRecoveryRoutine);

        _masterRecoveryRoutine = null;
        _populationFinalized = false;

        foreach (ShopTableItemStabilizer stabilizer in ActiveStabilizers.ToArray())
        {
            if (stabilizer != null)
                stabilizer.Release(publishChange: false);
        }

        ActiveStabilizers.Clear();
        StabilizedViewIds.Clear();
        ReservedFootprints.Clear();
        PlacedObjectIds.Clear();
    }

    internal static void NoteSpawn(ItemVolume itemVolume, bool spawned)
    {
        if (!spawned || itemVolume == null || !SemiFunc.IsMasterClientOrSingleplayer())
            return;

        MoreStandsMultiSizeVolume marker = itemVolume.GetComponent<MoreStandsMultiSizeVolume>();
        if (marker == null || string.IsNullOrEmpty(marker.GroupId))
            return;

        ItemAttributes attributes = FindNewSpawnedItem(itemVolume.transform.position);
        if (attributes == null)
        {
            Plugin.Log.LogWarning(
                $"[TableItemPlacement] Spawned table item was not found near " +
                $"{itemVolume.transform.position}; group={marker.GroupId}. Keeping the vanilla spawn.");
            return;
        }

        Transform root = attributes.transform;
        PlacedObjectIds.Add(root.GetInstanceID());

        Vector3 originalEuler = root.rotation.eulerAngles;
        float baseYaw = originalEuler.y;
        bool usesQuarterTurnRule = UsesQuarterTurnRule(attributes.item);
        float[] yawTurns = usesQuarterTurnRule ? QuarterTurnYawTurns : StandardYawTurns;
        PlacementCandidate best = default;
        bool hasBest = false;

        foreach (float yawTurn in yawTurns)
        {
            Quaternion candidateRotation = Quaternion.Euler(
                0f,
                Mathf.Repeat(baseYaw + yawTurn, 360f),
                0f);
            ApplyImmediateRotation(root, candidateRotation);

            if (!TryGetPhysicalBounds(root, out Bounds bounds))
                bounds = new Bounds(root.position, new Vector3(0.2f, 0.2f, 0.2f));

            TableFootprint footprint = new(bounds, FootprintPadding);
            float overlapArea = ReservedFootprints.Sum(reserved =>
                reserved.Footprint.IntersectionArea(footprint));
            var candidate = new PlacementCandidate(
                candidateRotation,
                footprint,
                yawTurn,
                overlapArea);

            // Strict improvement preserves the earlier turn on ties. Ordinary items
            // retain their authored yaw; both dedicated items prefer +90 degrees.
            if (!hasBest || candidate.OverlapArea < best.OverlapArea - ClearOverlapArea)
            {
                best = candidate;
                hasBest = true;
            }

            if (overlapArea <= ClearOverlapArea)
                break;
        }

        if (!hasBest)
            return;

        ApplyNetworkedRotation(root, best.Rotation);
        ReservedFootprints.Add(new ReservedFootprint(best.Footprint));
        AttachStabilizer(attributes);

        Vector3 appliedEuler = best.Rotation.eulerAngles;
        string logMessage =
            $"[TableItemPlacement] Placed {ItemName(attributes)}: group={marker.GroupId}, " +
            $"original=({originalEuler.x:F1}, {originalEuler.y:F1}, {originalEuler.z:F1}), " +
            $"upright=({appliedEuler.x:F1}, {appliedEuler.y:F1}, {appliedEuler.z:F1}), " +
            $"yawTurn={best.YawTurn:F0}, quarterTurnRule={usesQuarterTurnRule}, " +
            $"remainingOverlap={best.OverlapArea:F4}.";

        if (best.OverlapArea > ClearOverlapArea)
            Plugin.Log.LogWarning(logMessage);
        else if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo(logMessage);
    }

    internal static void FinalizePopulation()
    {
        _populationFinalized = true;
        PublishStabilizedViewIds();
    }

    internal static void NoteStabilizerReleased(int viewId)
    {
        if (viewId > 0)
            StabilizedViewIds.Remove(viewId);

        if (_populationFinalized)
            PublishStabilizedViewIds();
    }

    internal static void ForgetStabilizer(ShopTableItemStabilizer stabilizer)
    {
        if (stabilizer == null)
            return;

        ActiveStabilizers.Remove(stabilizer);
        if (stabilizer.ViewId > 0)
            StabilizedViewIds.Remove(stabilizer.ViewId);
    }

    internal static void HandleMasterClientSwitched()
    {
        if (_masterRecoveryRoutine != null && Plugin.Instance != null)
            Plugin.Instance.StopCoroutine(_masterRecoveryRoutine);

        _masterRecoveryRoutine = null;

        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
        {
            foreach (ShopTableItemStabilizer stabilizer in ActiveStabilizers.ToArray())
            {
                if (stabilizer != null)
                    stabilizer.Release(publishChange: false);
            }

            ActiveStabilizers.Clear();
            StabilizedViewIds.Clear();
            return;
        }

        if (Plugin.Instance == null || !ShopLayoutSync.HasTableStabilizedViewIds())
            return;

        _masterRecoveryRoutine = Plugin.Instance.StartCoroutine(RecoverForNewMasterRoutine());
    }

    private static IEnumerator RecoverForNewMasterRoutine()
    {
        // Photon updates the master flag and room-object ownership before the next
        // frame. Waiting avoids racing that transfer.
        yield return null;

        _masterRecoveryRoutine = null;
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
            yield break;

        _populationFinalized = true;
        ActiveStabilizers.Clear();
        StabilizedViewIds.Clear();

        int[] persistedViewIds = ShopLayoutSync.GetTableStabilizedViewIds();
        if (persistedViewIds.Length == 0)
            yield break;

        bool reflectionUnavailable = false;

        foreach (int viewId in persistedViewIds)
        {
            PhotonView view = viewId > 0 ? PhotonView.Find(viewId) : null;
            if (view == null)
                continue;

            PhysGrabObject grabObject = view.GetComponent<PhysGrabObject>() ??
                                        view.GetComponentInChildren<PhysGrabObject>(true);
            if (grabObject == null || ShopTableItemStabilizer.IsBeingGrabbed(grabObject))
                continue;

            if (!ShopTableItemStabilizer.TryWasNeverGrabbed(grabObject, out bool neverGrabbed))
            {
                reflectionUnavailable = true;
                break;
            }

            if (!neverGrabbed)
                continue;

            ItemAttributes attributes = view.GetComponent<ItemAttributes>() ??
                                        view.GetComponentInChildren<ItemAttributes>(true);
            AttachStabilizer(attributes, grabObject, view);
        }

        if (reflectionUnavailable)
        {
            Plugin.Log.LogWarning(
                "[TableItemStabilizer] Host migration recovery skipped because the " +
                "vanilla first-grab state is unavailable; used items will never be re-frozen.");
            yield break;
        }

        PublishStabilizedViewIds();

        if (Plugin.DebugLogs.Value)
        {
            Plugin.Log.LogInfo(
                $"[TableItemStabilizer] Recovered {StabilizedViewIds.Count}/" +
                $"{persistedViewIds.Length} ungrabbed table item(s) for the new master.");
        }
    }

    private static void AttachStabilizer(ItemAttributes attributes)
    {
        if (attributes == null)
            return;

        PhysGrabObject grabObject = attributes.GetComponent<PhysGrabObject>();
        PhotonView view = attributes.GetComponent<PhotonView>();
        AttachStabilizer(attributes, grabObject, view);
    }

    private static void AttachStabilizer(
        ItemAttributes attributes,
        PhysGrabObject grabObject,
        PhotonView view)
    {
        if (attributes == null || grabObject == null)
            return;

        ShopTableItemStabilizer stabilizer = attributes.GetComponent<ShopTableItemStabilizer>();
        if (stabilizer == null || stabilizer.IsReleased)
            stabilizer = attributes.gameObject.AddComponent<ShopTableItemStabilizer>();

        stabilizer.Initialize(grabObject, view);

        if (!ActiveStabilizers.Contains(stabilizer))
            ActiveStabilizers.Add(stabilizer);

        if (view != null && view.ViewID > 0)
            StabilizedViewIds.Add(view.ViewID);
    }

    private static void PublishStabilizedViewIds()
    {
        if (!SemiFunc.IsMultiplayer() || !PhotonNetwork.IsMasterClient)
            return;

        if (!ShopLayoutSync.SetTableStabilizedViewIds(StabilizedViewIds))
        {
            Plugin.Log.LogWarning(
                "[TableItemStabilizer] Could not publish the recoverable table-item set; " +
                "current-host stabilization remains active.");
        }
    }

    private static ItemAttributes FindNewSpawnedItem(Vector3 spawnPosition)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(
            spawnPosition,
            Mathf.Sqrt(SpawnLookupMaxSqrDistance),
            SpawnLookupHits,
            ~0,
            QueryTriggerInteraction.Collide);

        ItemAttributes nearestPhysicsCandidate = null;
        float nearestPhysicsDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            Collider collider = SpawnLookupHits[i];
            SpawnLookupHits[i] = null;
            if (collider == null)
                continue;

            ItemAttributes attributes = collider.GetComponentInParent<ItemAttributes>();
            if (attributes == null || !attributes.gameObject.activeInHierarchy ||
                PlacedObjectIds.Contains(attributes.transform.GetInstanceID()))
            {
                continue;
            }

            float distance = Vector3.SqrMagnitude(attributes.transform.position - spawnPosition);
            if (distance < nearestPhysicsDistance)
            {
                nearestPhysicsDistance = distance;
                nearestPhysicsCandidate = attributes;
            }
        }

        if (nearestPhysicsCandidate != null && nearestPhysicsDistance <= SpawnLookupMaxSqrDistance)
            return nearestPhysicsCandidate;

        // Some third-party prefabs create or enable colliders in Start. Preserve the
        // previous global lookup as a compatibility fallback for those items only.
        ItemAttributes nearest = UnityEngine.Object.FindObjectsOfType<ItemAttributes>()
            .Where(attributes => attributes != null &&
                                 attributes.gameObject.activeInHierarchy &&
                                 !PlacedObjectIds.Contains(attributes.transform.GetInstanceID()))
            .OrderBy(attributes => Vector3.SqrMagnitude(
                attributes.transform.position - spawnPosition))
            .FirstOrDefault();

        return nearest != null &&
               Vector3.SqrMagnitude(nearest.transform.position - spawnPosition) <=
               SpawnLookupMaxSqrDistance
            ? nearest
            : null;
    }

    private static void ApplyImmediateRotation(Transform root, Quaternion rotation)
    {
        root.rotation = rotation;
        Rigidbody rigidbody = root.GetComponent<Rigidbody>();
        if (rigidbody != null)
            rigidbody.rotation = rotation;
        Physics.SyncTransforms();
    }

    private static void ApplyNetworkedRotation(Transform root, Quaternion rotation)
    {
        PhotonTransformView transformView = root.GetComponent<PhotonTransformView>();
        if (transformView != null)
            transformView.Teleport(root.position, rotation);
        else
            ApplyImmediateRotation(root, rotation);
    }

    private static bool TryGetPhysicalBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || !collider.enabled || collider.isTrigger ||
                !collider.gameObject.activeInHierarchy)
            {
                continue;
            }

            Encapsulate(ref bounds, ref hasBounds, collider.bounds);
        }

        if (hasBounds)
            return true;

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            Encapsulate(ref bounds, ref hasBounds, renderer.bounds);
        }

        return hasBounds;
    }

    private static void Encapsulate(ref Bounds combined, ref bool hasBounds, Bounds next)
    {
        if (!hasBounds)
        {
            combined = next;
            hasBounds = true;
        }
        else
        {
            combined.Encapsulate(next);
        }
    }

    private static bool UsesQuarterTurnRule(Item item)
    {
        string internalName = item?.name;
        string displayName = ItemName(item);

        return IsNamed(internalName, displayName, "Photon Blaster") ||
               IsNamed(internalName, displayName, "Semibot Walkies");
    }

    private static bool IsNamed(string internalName, string displayName, string expected)
    {
        return string.Equals(internalName, expected, System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(displayName, expected, System.StringComparison.OrdinalIgnoreCase);
    }

    private static string ItemName(ItemAttributes attributes)
    {
        if (attributes?.item == null)
            return attributes != null ? attributes.name : "<unknown>";

        return ItemName(attributes.item);
    }

    private static string ItemName(Item item)
    {
        if (item == null)
            return "<null>";

        return !string.IsNullOrWhiteSpace(item.itemName) ? item.itemName : item.name;
    }

    private readonly struct PlacementCandidate
    {
        internal readonly Quaternion Rotation;
        internal readonly TableFootprint Footprint;
        internal readonly float YawTurn;
        internal readonly float OverlapArea;

        internal PlacementCandidate(
            Quaternion rotation,
            TableFootprint footprint,
            float yawTurn,
            float overlapArea)
        {
            Rotation = rotation;
            Footprint = footprint;
            YawTurn = yawTurn;
            OverlapArea = overlapArea;
        }
    }

    private readonly struct ReservedFootprint
    {
        internal readonly TableFootprint Footprint;

        internal ReservedFootprint(TableFootprint footprint)
        {
            Footprint = footprint;
        }
    }

    private readonly struct TableFootprint
    {
        private readonly float _minX;
        private readonly float _maxX;
        private readonly float _minZ;
        private readonly float _maxZ;

        internal TableFootprint(Bounds bounds, float padding)
        {
            _minX = bounds.min.x - padding;
            _maxX = bounds.max.x + padding;
            _minZ = bounds.min.z - padding;
            _maxZ = bounds.max.z + padding;
        }

        internal float IntersectionArea(TableFootprint other)
        {
            float width = Mathf.Max(0f, Mathf.Min(_maxX, other._maxX) -
                                        Mathf.Max(_minX, other._minX));
            float depth = Mathf.Max(0f, Mathf.Min(_maxZ, other._maxZ) -
                                        Mathf.Max(_minZ, other._minZ));
            return width * depth;
        }
    }
}
