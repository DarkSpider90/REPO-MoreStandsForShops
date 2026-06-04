using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MoreStandsForShops.Network;
using MoreStandsForShops.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;
using MoreStandsForShops.Stands.Upgrade;

namespace MoreStandsForShops.Spawners;

public static class UpgradeStandSpawner
{
    private static GameObject _cachedPrefab;
    private static bool _prefabPrepared;
    private static readonly RaycastHit[] WallHits = new RaycastHit[16];


    public static bool EnsurePrefabPrepared()
    {
        return _prefabPrepared || PreparePrefab();
    }


    public static bool TrySpawn(out GameObject spawnedStand, bool configureItemVolumes = true)
    {
        spawnedStand = null;

        if (!Plugin.EnableAdditionalUpgradeStand.Value)
        {
            if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo("[UpgradeStandSpawner] Disabled in config.");
            return false;
        }

        spawnedStand = FindExistingSpawnedStand();
        if (spawnedStand != null)
        {
            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo("[UpgradeStandSpawner] Additional upgrade stand already exists; skipping duplicate spawn.");
            return true;
        }

        // Prepare prefab if not done yet
        if (!_prefabPrepared)
        {
            if (!PreparePrefab())
                return false;
        }

        bool protectPaintingObjects = IsPaintingSecretShopPresent();

        // Try each spawn point for the active main shop module until one succeeds.
        var points = CleanPresetDatabase.GetSpawnPoints()
            .OrderByDescending(point => point.SourceCount)
            .ToList();

        foreach (var point in points)
        {
            // Find main module by full path from root
            Transform module = FindTransformByPath(point.MainModule);
            if (module == null)
            {
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[UpgradeStandSpawner] Module '{point.MainModule}' not found.");
                continue;
            }

            // Calculate world position and rotation
            Vector3 position = module.TransformPoint(point.LocalPosition);
            Quaternion rotation = module.rotation * Quaternion.Euler(0f, point.LocalYaw, 0f);

            // Check wall proximity (must be against a wall)
            if (!IsAgainstWall(position, rotation))
            {
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[UpgradeStandSpawner] Variant '{point.VariantId}' rejected: no back wall within 0.5m. local={point.LocalPosition}, world={position}, yaw={point.LocalYaw}, sourceCount={point.SourceCount}.");
                continue;
            }

            if (ScenePathUtility.HasActivePath(point.RejectIfPresentPaths, out string presetReject))
            {
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[UpgradeStandSpawner] Variant '{point.VariantId}' rejected: preset protected path is active '{presetReject}'. local={point.LocalPosition}, world={position}, yaw={point.LocalYaw}, sourceCount={point.SourceCount}.");
                continue;
            }

            // Check for protected objects
            if (HasProtectedOverlap(position, rotation, protectPaintingObjects, out string protectedObj))
            {
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[UpgradeStandSpawner] Variant '{point.VariantId}' rejected: protected overlap '{protectedObj}'. local={point.LocalPosition}, world={position}, yaw={point.LocalYaw}, sourceCount={point.SourceCount}.");
                continue;
            }

            // Disable known preset blockers first, then run a final direct-overlap cleanup as a safety net.
            List<string> disabledObjects = ScenePathUtility.DisableExactPaths(point.DisablePaths, "[UpgradeStandSpawner]");
            disabledObjects.AddRange(DisableMovableOverlaps(position, rotation));

            spawnedStand = Object.Instantiate(_cachedPrefab, position, rotation);
            spawnedStand.name = "MoreStandsForShops Upgrade Stand";
            spawnedStand.SetActive(true);
            spawnedStand.transform.SetParent(module, true);

            if (configureItemVolumes)
                ConfigureUpgradeVolumes(spawnedStand);
            else
                DisableItemVolumes(spawnedStand);

            if (configureItemVolumes && SemiFunc.IsMultiplayer() && Photon.Pun.PhotonNetwork.IsMasterClient)
                ShopLayoutSync.SetUpgradeStand(new UpgradeStandLayout
                {
                    Enabled = true,
                        VariantId = point.VariantId,
                        Position = position,
                        Rotation = rotation,
                        ParentPath = ScenePathUtility.GetTransformPath(module),
                        DisabledPaths = disabledObjects.ToArray(),
                        UpgradeSlotCount = Plugin.ItemCounts.TryGetValue("Upgrades Per Stand", out var upgradeSlots)
                            ? upgradeSlots.Value
                            : 14
                });

            if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[UpgradeStandSpawner] Successfully spawned additional upgrade stand: variant={point.VariantId}, sourceCount={point.SourceCount}, main={point.MainModule}, local={point.LocalPosition}, world={position}, yaw={point.LocalYaw}, itemVolumes={configureItemVolumes}, disabled={disabledObjects.Count}, protectPainting={protectPaintingObjects}.");
            SchedulePresetBlockerRecheck(point.DisablePaths, "[UpgradeStandSpawner:Delayed]");
            ScheduleMagazineDisplayRecheck(position, rotation);
            ScheduleCartOverlapRecheck(position, rotation);
            return true;
        }

        Plugin.Log.LogWarning("[UpgradeStandSpawner] No valid spawn point found.");
        return false;
    }


    public static bool SpawnNetworkVisual(string spawnId, string variantId, Vector3 position, Quaternion rotation, string parentPath, string[] disabledPaths)
    {
        if (!EnsurePrefabPrepared())
            return false;

        GameObject existing = FindExistingSpawnedStand();
        if (existing != null)
            return true;

        Transform parent = ScenePathUtility.FindTransformByPath(parentPath);
        if (parent == null)
            return false;

        ScenePathUtility.DisableExactPaths(disabledPaths, "[UpgradeStandSpawner:Network]");

        GameObject spawnedStand = Object.Instantiate(_cachedPrefab, position, rotation);
        spawnedStand.name = $"MoreStandsForShops Upgrade Stand {variantId}";
        spawnedStand.SetActive(true);
        spawnedStand.transform.SetParent(parent, true);
        DisableItemVolumes(spawnedStand);

        if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[UpgradeStandSpawner] Network visual spawned: id={spawnId}, variant={variantId}, parent={parentPath}.");
        SchedulePresetBlockerRecheck(disabledPaths, "[UpgradeStandSpawner:NetworkDelayed]");
        ScheduleMagazineDisplayRecheck(position, rotation);
        return true;
    }

    private static void SchedulePresetBlockerRecheck(IEnumerable<string> paths, string logPrefix)
    {
        if (Plugin.Instance == null || paths == null)
            return;

        string[] capturedPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct()
            .ToArray();

        if (capturedPaths.Length == 0)
            return;

        Plugin.Instance.StartCoroutine(RecheckPresetBlockersRoutine(capturedPaths, logPrefix));
    }

    private static IEnumerator RecheckPresetBlockersRoutine(string[] paths, string logPrefix)
    {
        yield return new WaitForSeconds(0.25f);
        ScenePathUtility.DisableExactPaths(paths, logPrefix);

        yield return new WaitForSeconds(1f);
        ScenePathUtility.DisableExactPaths(paths, logPrefix);
    }

    private static void ScheduleMagazineDisplayRecheck(Vector3 position, Quaternion rotation)
    {
        if (Plugin.Instance == null)
            return;

        Plugin.Instance.StartCoroutine(RecheckMagazineDisplaysRoutine(position, rotation));
    }

    private static IEnumerator RecheckMagazineDisplaysRoutine(Vector3 position, Quaternion rotation)
    {
        yield return new WaitForSeconds(0.25f);
        DisableMagazineDisplaysAt(position, rotation);

        yield return new WaitForSeconds(1f);
        DisableMagazineDisplaysAt(position, rotation);
    }

    private static void DisableMagazineDisplaysAt(Vector3 position, Quaternion rotation)
    {
        Vector3 halfExtents = new(1.15f, 1.15f, 0.75f);
        Vector3 center = position + Vector3.up * halfExtents.y;
        var disabledTargets = new HashSet<Transform>();
        var disabledPaths = new List<string>();
        DisableMagazineDisplaysInsideStand(BuildMagazineCleanupBounds(center, halfExtents, rotation), disabledTargets, disabledPaths);
    }

    private static void ScheduleCartOverlapRecheck(Vector3 position, Quaternion rotation)
    {
        if (Plugin.Instance == null || !SemiFunc.IsMasterClientOrSingleplayer())
            return;

        Plugin.Instance.StartCoroutine(RecheckCartOverlapsRoutine(position, rotation));
    }

    public static void SchedulePostPopulateCartOverlapRecheck()
    {
        if (Plugin.Instance == null || !SemiFunc.IsMasterClientOrSingleplayer())
            return;

        GameObject stand = FindExistingSpawnedStand();
        if (stand == null)
            return;

        Plugin.Instance.StartCoroutine(RecheckCartOverlapsRoutine(stand.transform.position, stand.transform.rotation));
    }

    private static IEnumerator RecheckCartOverlapsRoutine(Vector3 position, Quaternion rotation)
    {
        yield return new WaitForSeconds(0.25f);
        MoveCartOverlaps(position, rotation);

        yield return new WaitForSeconds(1f);
        MoveCartOverlaps(position, rotation);

        yield return new WaitForSeconds(2f);
        MoveCartOverlaps(position, rotation);

        yield return new WaitForSeconds(4f);
        MoveCartOverlaps(position, rotation);

        yield return new WaitForSeconds(7f);
        MoveCartOverlaps(position, rotation);
    }


    private static GameObject FindExistingSpawnedStand()
    {
        return Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.activeInHierarchy)
            .Where(t => t.name.StartsWith("MoreStandsForShops Upgrade Stand", System.StringComparison.OrdinalIgnoreCase))
            .Select(t => t.gameObject)
            .FirstOrDefault();
    }


    private static void ConfigureUpgradeVolumes(GameObject spawnedStand)
    {
        int maxSlots = Plugin.ItemCounts.TryGetValue("Upgrades Per Stand", out var entry) ? entry.Value : 14;

        ItemVolume[] volumes = GetStandItemVolumes(spawnedStand);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[UpgradeStandSpawner] Clone child ItemVolume count before source copy: {volumes.Length}.");

        if (volumes.Length < maxSlots && maxSlots > 0)
        {
            if (volumes.Length > 0)
            {
                DisableExistingUpgradeVolumeChildren(volumes);

                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[UpgradeStandSpawner] Disabled {volumes.Length} incomplete cloned upgrade ItemVolume(s) before vanilla source copy.");
            }

            // Old hand-built grid is intentionally not used anymore:
            // CreateUpgradeVolumes(spawnedStand.transform, maxSlots);

            volumes = CreateUpgradeVolumesFromVanillaSources(spawnedStand.transform);
        }

        for (int i = 0; i < volumes.Length; i++)
        {
            volumes[i].itemVolume = SemiFunc.itemVolume.upgrade;
            volumes[i].itemSecretShopType = SemiFunc.itemSecretShopType.none;

            if (volumes[i].gameObject.GetComponent<MoreStandsUpgradeVolume>() == null)
                volumes[i].gameObject.AddComponent<MoreStandsUpgradeVolume>();

            bool enabled = i < maxSlots;
            volumes[i].gameObject.SetActive(enabled);
            volumes[i].enabled = enabled;
        }

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[UpgradeStandSpawner] Configured upgrade ItemVolumes: active={System.Math.Min(maxSlots, volumes.Length)}, total={volumes.Length}.");
    }
    
    
    private static ItemVolume[] GetStandItemVolumes(GameObject stand)
    {
        return stand.GetComponentsInChildren<ItemVolume>(true)
            .Where(v => v != null)
            .OrderByDescending(v => v.transform.localPosition.y)
            .ThenBy(v => v.transform.localPosition.x)
            .ToArray();
    }
    
    
    private static void DisableExistingUpgradeVolumeChildren(ItemVolume[] volumes)
    {
        foreach (ItemVolume volume in volumes)
        {
            if (volume == null)
                continue;

            volume.enabled = false;
            volume.gameObject.SetActive(false);
        }
    }


    private static ItemVolume[] CreateUpgradeVolumesFromVanillaSources(Transform targetStand)
    {
        UpgradeStand originalStand = FindOriginalUpgradeStand();
        if (originalStand == null)
        {
            Plugin.Log.LogWarning("[UpgradeStandSpawner] Cannot copy upgrade ItemVolumes: vanilla UpgradeStand not found.");
            return new ItemVolume[0];
        }

        List<UpgradeVolumeSource> allUpgradeSources = FindAllVanillaUpgradeVolumeSources(originalStand.transform)
            .ToList();

        List<UpgradeVolumeSource> sources = allUpgradeSources
            .Where(IsLikelyOriginalUpgradeSlot)
            .OrderByDescending(s => s.LocalPosition.y)
            .ThenBy(s => s.LocalPosition.x)
            .Take(14)
            .ToList();

        if (Plugin.DebugLogs.Value)
        {
            if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[UpgradeStandSpawner] Vanilla upgrade slot scan: all={allUpgradeSources.Count}, accepted={sources.Count}, original={GetTransformPath(originalStand.transform)}.");

            foreach (UpgradeVolumeSource source in allUpgradeSources.OrderBy(s => s.Distance).Take(24))
            {
                bool accepted = IsLikelyOriginalUpgradeSlot(source);
                if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[UpgradeStandSpawner] Vanilla upgrade slot candidate: accepted={accepted}, distance={source.Distance:F2}, local={source.LocalPosition}, path={source.Path}");
            }
        }

        if (sources.Count == 0)
        {
            Plugin.Log.LogWarning("[UpgradeStandSpawner] Cannot copy upgrade ItemVolumes: no nearby vanilla upgrade ItemVolumes found.");
            return new ItemVolume[0];
        }
        
        if (sources.Count < 14)
        {
            Plugin.Log.LogWarning($"[UpgradeStandSpawner] Only {sources.Count}/14 vanilla upgrade ItemVolume(s) accepted. Passive stand may have fewer upgrade slots.");
        }

        List<ItemVolume> created = new();

        for (int i = 0; i < sources.Count; i++)
        {
            UpgradeVolumeSource source = sources[i];

            GameObject slot = Object.Instantiate(source.Volume.gameObject, targetStand, false);
            slot.name = $"MoreStandsForShops Upgrade Slot {i + 1:00}";
            slot.transform.localPosition = source.LocalPosition;
            slot.transform.localRotation = source.LocalRotation;
            slot.transform.localScale = source.LocalScale;
            slot.SetActive(true);

            ItemVolume volume = slot.GetComponent<ItemVolume>();
            if (volume == null)
                volume = slot.AddComponent<ItemVolume>();

            volume.itemVolume = SemiFunc.itemVolume.upgrade;
            volume.itemSecretShopType = SemiFunc.itemSecretShopType.none;
            volume.enabled = true;

            if (slot.GetComponent<MoreStandsUpgradeVolume>() == null)
                slot.AddComponent<MoreStandsUpgradeVolume>();

            created.Add(volume);

            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"[UpgradeStandSpawner] Copied vanilla upgrade slot {i + 1}: source={source.Path}, local={source.LocalPosition}, yaw={source.LocalRotation.eulerAngles.y:F1}");
        }

        if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[UpgradeStandSpawner] Copied {created.Count} vanilla upgrade ItemVolume(s) for passive stand.");
        return created
            .OrderByDescending(v => v.transform.localPosition.y)
            .ThenBy(v => v.transform.localPosition.x)
            .ToArray();
    }


    private static IEnumerable<UpgradeVolumeSource> FindAllVanillaUpgradeVolumeSources(Transform originalStand)
    {
        return ShopSceneCache.Current.ItemVolumes
            .Where(v => v != null)
            .Where(v => v.gameObject.activeInHierarchy)
            .Where(v => v.itemVolume == SemiFunc.itemVolume.upgrade)
            .Where(v => !IsInsideModOwnedObject(v.transform))
            .Select(v => new UpgradeVolumeSource(v, originalStand));
    }


    private static bool IsLikelyOriginalUpgradeSlot(UpgradeVolumeSource source)
    {
        Vector3 local = source.LocalPosition;

        return source.Distance <= 2.5f &&
               Mathf.Abs(local.x) <= 1.6f &&
               local.y >= 0.4f &&
               local.y <= 2.5f &&
               local.z >= -1.2f &&
               local.z <= 1.4f;
    }


    private static bool IsInsideModOwnedObject(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            if (current.name.StartsWith("MoreStandsForShops", System.StringComparison.OrdinalIgnoreCase))
                return true;

            if (current.name.StartsWith("ExtraItemsShop", System.StringComparison.OrdinalIgnoreCase))
                return true;

            current = current.parent;
        }

        return false;
    }


    private sealed class UpgradeVolumeSource
    {
        internal readonly ItemVolume Volume;
        internal readonly Vector3 LocalPosition;
        internal readonly Quaternion LocalRotation;
        internal readonly Vector3 LocalScale;
        internal readonly float Distance;
        internal readonly string Path;

        internal UpgradeVolumeSource(ItemVolume volume, Transform originalStand)
        {
            Volume = volume;
            LocalPosition = originalStand.InverseTransformPoint(volume.transform.position);
            LocalRotation = Quaternion.Inverse(originalStand.rotation) * volume.transform.rotation;
            LocalScale = volume.transform.localScale;
            Distance = Vector3.Distance(originalStand.position, volume.transform.position);
            Path = GetTransformPath(volume.transform);
        }
    }


    private static float[] EvenlySpaced(float min, float max, int count)
    {
        if (count <= 1)
        {
            return new[] { (min + max) * 0.5f };
        }

        float[] values = new float[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = Mathf.Lerp(min, max, i / (float)(count - 1));
        }

        return values;
    }


    private static bool PreparePrefab()
    {
        // Find original upgrade stand via component
        UpgradeStand original = FindOriginalUpgradeStand();
        if (original == null)
        {
            Plugin.Log.LogError("[UpgradeStandSpawner] No vanilla UpgradeStand found in scene.");
            return false;
        }

        _cachedPrefab = Object.Instantiate(original.gameObject);
        _cachedPrefab.name = "MoreStandsForShops_UpgradeStand_Prefab";
        _cachedPrefab.SetActive(false);
        Object.DontDestroyOnLoad(_cachedPrefab);

        // Keep vanilla visual, but replace vanilla UpgradeStand logic with our safe controller.
        var standComp = _cachedPrefab.GetComponent<UpgradeStand>();
        if (standComp != null)
        {
            var rerollController = _cachedPrefab.GetComponent<UpgradeStandRerollController>();
            if (rerollController == null)
                rerollController = _cachedPrefab.AddComponent<UpgradeStandRerollController>();

            rerollController.ConfigureFromVanilla(standComp);
            Object.Destroy(standComp);
        }

        var pv = _cachedPrefab.GetComponent<Photon.Pun.PhotonView>();
        if (pv != null) Object.Destroy(pv);

        _prefabPrepared = true;
        if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo("[UpgradeStandSpawner] Upgrade stand prefab prepared.");
        return true;
    }


    private static void DisableItemVolumes(GameObject stand)
    {
        foreach (ItemVolume volume in stand.GetComponentsInChildren<ItemVolume>(true))
        {
            if (volume == null)
                continue;

            volume.enabled = false;
            volume.gameObject.SetActive(false);
        }
    }

    private static UpgradeStand FindOriginalUpgradeStand()
    {
        return ShopSceneCache.Current.Transforms
            .Select(t => t == null ? null : t.GetComponent<UpgradeStand>())
            .FirstOrDefault(s => s != null &&
                                 s.gameObject.activeInHierarchy &&
                                 !s.name.StartsWith("MoreStandsForShops", System.StringComparison.OrdinalIgnoreCase) &&
                                 !s.name.StartsWith("ExtraItemsShop", System.StringComparison.OrdinalIgnoreCase));
    }


    private static bool IsPaintingSecretShopPresent()
    {
        return ShopSceneCache.Current.Transforms.Any(t =>
            t != null &&
            t.gameObject.activeInHierarchy &&
            t.name == "Module - Shop - DE - Painting Secret Shop(Clone)");
    }


    private static bool IsAgainstWall(Vector3 position, Quaternion rotation)
    {
        Vector3 direction = rotation * Vector3.back;
        float maxWallDistance = 0.5f;
        Vector3 origin = position + Vector3.up * 0.5f;

        int hitCount = Physics.RaycastNonAlloc(origin, direction, WallHits, maxWallDistance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = WallHits[i];
            if (hit.transform == null)
                continue;

            if (Mathf.Abs(hit.normal.y) < 0.1f && IsStructuralShopSurface(GetTransformPath(hit.transform).ToLowerInvariant()))
                return true;
        }

        return false;
    }


    private static bool HasProtectedOverlap(Vector3 position, Quaternion rotation, bool protectPaintingObjects, out string objectName)
    {
        objectName = null;
        Vector3 halfExtents = new(0.85f, 1.10f, 0.55f);
        Vector3 center = position + Vector3.up * halfExtents.y;
        Bounds standBounds = BuildWorldBounds(center, halfExtents, rotation, 0.03f);

        Collider[] overlaps = Physics.OverlapBox(center, halfExtents, rotation, ~0, QueryTriggerInteraction.Ignore);
        foreach (var col in overlaps)
        {
            if (col == null || col.transform == null) continue;
            string path = GetTransformPath(col.transform).ToLowerInvariant();

            // Skip our own objects
            if (col.transform.name.StartsWith("MoreStandsForShops", System.StringComparison.OrdinalIgnoreCase))
                continue;
            if (col.transform.name.StartsWith("ExtraItemsShop", System.StringComparison.OrdinalIgnoreCase))
                continue;

            // Check if protected
            if (IsProtected(col.transform, protectPaintingObjects))
            {
                objectName = col.transform.name;
                return true;
            }

            if (IsStructuralShopSurface(path))
                continue;

            // Skip environment colliders only after protected extraction/truck objects were checked.
            if (path.Contains("collider") || path.Contains("trigger"))
                continue;
        }

        foreach (Renderer renderer in ShopSceneCache.Current.Renderers)
        {
            if (renderer == null || !renderer.gameObject.activeInHierarchy)
                continue;

            Transform rendererTransform = renderer.transform;
            if (rendererTransform.name.StartsWith("MoreStandsForShops", System.StringComparison.OrdinalIgnoreCase))
                continue;
            if (rendererTransform.name.StartsWith("ExtraItemsShop", System.StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsProtected(rendererTransform, protectPaintingObjects))
                continue;

            if (renderer.bounds.Intersects(standBounds))
            {
                objectName = GetTransformPath(rendererTransform);
                return true;
            }
        }

        return false;
    }


    private static bool IsProtected(Transform t)
    {
        return IsProtected(t, IsPaintingSecretShopPresent());
    }

    private static bool IsProtected(Transform t, bool protectPaintingObjects)
    {
        string path = GetTransformPath(t).ToLowerInvariant();
        string[] protectedFragments =
        {
            "cash register", "cashier", "cashiers desk", "shop owner", "shopkeeper",
            "upgrade stand", "health stand", "revive stand", "battery upgrade stand",
            "item stands", "valuable shelf", "weapon stand", "weapon shelf",
            "extraction", "truck",
            "door", "secret", "hidden", "passage", "entrance"
        };

        if (protectPaintingObjects && path.Contains("painting"))
            return true;

        foreach (string frag in protectedFragments)
        {
            if (path.Contains(frag)) return true;
        }

        if (t.GetComponentInChildren<ItemVolume>(true) != null) return true;
        if (t.GetComponentInParent<UpgradeStand>(true) != null) return true;

        return false;
    }


    private static bool IsStructuralShopSurface(string path)
    {
        string leaf = path;
        int slash = leaf.LastIndexOf('/');
        if (slash >= 0 && slash + 1 < leaf.Length)
            leaf = leaf.Substring(slash + 1);

        return path.Contains("/walls/") ||
               path.Contains("/floor") ||
               path.Contains("/ceiling") ||
               path.Contains("wall window") ||
               path.Contains("window shop") ||
               path.Contains("window glass") ||
               leaf == "walls" ||
               leaf == "floor" ||
               leaf == "ceiling" ||
               leaf.StartsWith("wall ") ||
               leaf.StartsWith("wall_") ||
               leaf.Contains("wall collider") ||
               leaf.Contains(" wall ") ||
               leaf.Contains("wall");
    }


    private static bool IsShopModulePath(string path)
    {
        return path.Contains("level generator/level/module - shop");
    }


    private static List<string> DisableMovableOverlaps(Vector3 position, Quaternion rotation)
    {
        bool protectPaintingObjects = IsPaintingSecretShopPresent();
        Vector3 halfExtents = new(1.15f, 1.15f, 0.75f);
        Vector3 center = position + Vector3.up * halfExtents.y;
        Bounds standBounds = BuildWorldBounds(center, halfExtents, rotation, 0.03f);
        HashSet<Transform> disabledTargets = new();
        var disabledPaths = new List<string>();

        MoveCartOverlaps(position, rotation);

        Collider[] overlaps = Physics.OverlapBox(center, halfExtents, rotation, ~0, QueryTriggerInteraction.Ignore);
        foreach (var col in overlaps)
        {
            if (col == null || col.transform == null) continue;
            if (col.transform.name.StartsWith("MoreStandsForShops", System.StringComparison.OrdinalIgnoreCase)) continue;
            if (col.transform.name.StartsWith("ExtraItemsShop", System.StringComparison.OrdinalIgnoreCase)) continue;

            string path = GetTransformPath(col.transform).ToLowerInvariant();
            if (!IsShopModulePath(path)) continue;
            if (IsProtected(col.transform, protectPaintingObjects)) continue;
            if (IsStructuralShopSurface(path)) continue;

            Transform disableTarget = FindDecorativeDisableRoot(col.transform);
            TryDisableDecorativeTarget(disableTarget, disabledTargets, disabledPaths, "collider", protectPaintingObjects);
        }

        foreach (Renderer renderer in ShopSceneCache.Current.Renderers)
        {
            if (renderer == null || !renderer.gameObject.activeInHierarchy)
                continue;

            Transform rendererTransform = renderer.transform;
            if (rendererTransform.name.StartsWith("MoreStandsForShops", System.StringComparison.OrdinalIgnoreCase)) continue;
            if (rendererTransform.name.StartsWith("ExtraItemsShop", System.StringComparison.OrdinalIgnoreCase)) continue;
            if (!renderer.bounds.Intersects(standBounds)) continue;

            string path = GetTransformPath(rendererTransform).ToLowerInvariant();
            if (!IsShopModulePath(path)) continue;
            if (IsProtected(rendererTransform, protectPaintingObjects)) continue;
            if (IsStructuralShopSurface(path)) continue;
            if (path.Contains("collider") || path.Contains("trigger")) continue;

            Transform disableTarget = FindDecorativeDisableRoot(rendererTransform);
            TryDisableDecorativeTarget(disableTarget, disabledTargets, disabledPaths, "renderer", protectPaintingObjects);
        }

        DisableMagazineDisplaysInsideStand(BuildMagazineCleanupBounds(center, halfExtents, rotation), disabledTargets, disabledPaths);

        return disabledPaths;
    }

    private static int MoveCartOverlaps(Vector3 position, Quaternion rotation)
    {
        Vector3 halfExtents = new(1.20f, 1.15f, 0.85f);
        Vector3 center = position + Vector3.up * halfExtents.y;
        Bounds standBounds = BuildWorldBounds(center, halfExtents, rotation, 0.05f);
        Vector3 moveDirection = GetStandForward(rotation);
        HashSet<Transform> movedTargets = new();

        Collider[] overlaps = Physics.OverlapBox(center, halfExtents, rotation, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider col in overlaps)
        {
            if (col == null || col.transform == null)
                continue;

            Transform moveTarget = FindCartMoveRoot(col.transform);
            if (moveTarget == null || movedTargets.Contains(moveTarget))
                continue;

            if (!TryGetCombinedObjectBounds(moveTarget, out Bounds cartBounds) || !cartBounds.Intersects(standBounds))
                continue;

            Vector3 moveOffset = CalculateCartMoveOffset(standBounds, cartBounds, moveDirection);
            MoveCartTarget(moveTarget, moveOffset, movedTargets);
        }

        return movedTargets.Count;
    }

    private static Vector3 CalculateCartMoveOffset(Bounds standBounds, Bounds cartBounds, Vector3 moveDirection)
    {
        float standHalfAlongMove = ProjectBoundsHalfExtent(standBounds, moveDirection);
        float cartHalfAlongMove = ProjectBoundsHalfExtent(cartBounds, moveDirection);
        float currentDistance = Vector3.Dot(cartBounds.center - standBounds.center, moveDirection);
        float targetDistance = standHalfAlongMove + cartHalfAlongMove + 0.35f;
        float moveDistance = Mathf.Max(1f, targetDistance - currentDistance);

        return moveDirection * moveDistance;
    }

    private static float ProjectBoundsHalfExtent(Bounds bounds, Vector3 axis)
    {
        Vector3 absAxis = new(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
        return Vector3.Dot(bounds.extents, absAxis);
    }

    private static Vector3 GetStandForward(Quaternion rotation)
    {
        Vector3 forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
        return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
    }

    private static Transform FindCartMoveRoot(Transform transform)
    {
        if (transform == null)
            return null;

        ItemAttributes attributes = transform.GetComponentInParent<ItemAttributes>();
        if (attributes != null && IsMovableCartItem(attributes.item))
            return attributes.transform;

        return FindNamedCartAncestor(transform);
    }

    private static Transform FindNamedCartAncestor(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            string name = current.name.ToLowerInvariant();
            if (IsMovableCartName(name))
                return current;

            current = current.parent;
        }

        return null;
    }

    private static bool IsMovableCartItem(Item item)
    {
        if (item == null)
            return false;

        if (item.itemType == SemiFunc.itemType.cart || item.itemType == SemiFunc.itemType.pocket_cart)
            return true;

        return IsMovableCartName(item.name.ToLowerInvariant());
    }

    private static bool IsMovableCartName(string name)
    {
        return name.Contains("item cart medium") ||
               name.Contains("item cart small") ||
               name.Contains("item cart large") ||
               name.Contains("cart medium") ||
               name.Contains("cart small") ||
               name.Contains("c.a.r.t.") ||
               name.Contains("pocket c.a.r.t") ||
               name.Contains("pocket cart");
    }

    private static void MoveCartTarget(Transform moveTarget, Vector3 moveOffset, HashSet<Transform> movedTargets)
    {
        if (moveTarget == null)
            return;

        if (moveTarget.name.StartsWith("MoreStandsForShops", System.StringComparison.OrdinalIgnoreCase))
            return;

        if (moveTarget.GetComponentInParent<UpgradeStand>(true) != null)
            return;

        Vector3 oldPosition = moveTarget.position;
        moveTarget.position = oldPosition + moveOffset;

        foreach (Rigidbody rb in moveTarget.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.Sleep();
        }

        Physics.SyncTransforms();
        movedTargets.Add(moveTarget);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[UpgradeStandSpawner] Moved cart away from upgrade stand: {GetTransformPath(moveTarget)} {oldPosition} -> {moveTarget.position}");
    }

    private static void DisableMagazineDisplaysInsideStand(Bounds standBounds, HashSet<Transform> disabledTargets, List<string> disabledPaths)
    {
        foreach (Transform transform in ShopSceneCache.Current.Transforms)
        {
            if (transform == null || !transform.gameObject.activeInHierarchy)
                continue;

            string path = GetTransformPath(transform).ToLowerInvariant();
            if (!IsMagazineDisplayPath(path))
                continue;

            Transform root = FindMagazineDisplayRoot(transform);
            if (root == null || disabledTargets.Contains(root))
                continue;

            if (!TryGetCombinedObjectBounds(root, out Bounds objectBounds))
                continue;

            if (!objectBounds.Intersects(standBounds))
                continue;

            TryDisableDecorativeTarget(root, disabledTargets, disabledPaths, "magazine-display");
        }
    }

    private static Bounds BuildMagazineCleanupBounds(Vector3 center, Vector3 halfExtents, Quaternion rotation)
    {
        Vector3 magazineHalfExtents = new(
            halfExtents.x + 1.10f,
            halfExtents.y + 0.35f,
            halfExtents.z + 0.55f);

        return BuildWorldBounds(center, magazineHalfExtents, rotation, 0.05f);
    }

    private static bool IsMagazineDisplayPath(string path)
    {
        return path.Contains("shop magazine holder") ||
               path.Contains("shop magazine stand") ||
               path.Contains("magazines");
    }

    private static Transform FindMagazineDisplayRoot(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            string name = current.name.ToLowerInvariant();
            if (name.Contains("shop magazine holder") || name.Contains("shop magazine stand"))
                return current;

            current = current.parent;
        }

        return FindDecorativeDisableRoot(transform);
    }

    private static bool TryGetCombinedObjectBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || !collider.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds;
    }


    private static bool TryDisableDecorativeTarget(Transform disableTarget, HashSet<Transform> disabledTargets, List<string> disabledPaths, string reason, bool protectPaintingObjects = false)
    {
        if (disableTarget == null)
            return false;

        if (disabledTargets.Contains(disableTarget))
            return false;

        if (disableTarget.name.StartsWith("MoreStandsForShops", System.StringComparison.OrdinalIgnoreCase))
            return false;

        if (disableTarget.name.StartsWith("ExtraItemsShop", System.StringComparison.OrdinalIgnoreCase))
            return false;

        if (IsUnsafeDisableRoot(disableTarget))
            return false;

        string targetPath = GetTransformPath(disableTarget).ToLowerInvariant();
        bool isMagazineDisplay = reason == "magazine-display" || IsMagazineDisplayPath(targetPath);

        if (!IsShopModulePath(targetPath))
            return false;

        if (IsProtected(disableTarget, protectPaintingObjects))
            return false;

        if (!isMagazineDisplay && IsStructuralShopSurface(targetPath))
            return false;

        disableTarget.gameObject.SetActive(false);
        disabledTargets.Add(disableTarget);
        disabledPaths.Add(GetTransformPath(disableTarget));

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[UpgradeStandSpawner] Disabled overlapping non-critical object via {reason}: {GetTransformPath(disableTarget)}");

        return true;
    }


    private static bool IsUnsafeDisableRoot(Transform transform)
    {
        string name = transform.name.ToLowerInvariant();
        return name == "props" ||
               name == "items" ||
               name == "item stands" ||
               name == "dependencies" ||
               name == "walls" ||
               name == "top" ||
               name == "bot" ||
               name == "left" ||
               name == "right" ||
               name == "connected" ||
               name == "not connected" ||
               name == "floor" ||
               name == "ceiling" ||
               name.Contains("---- level") ||
               name.StartsWith("module - shop") ||
               name == "level" ||
               name == "level generator";
    }


    private static Bounds BuildWorldBounds(Vector3 center, Vector3 localHalfExtents, Quaternion rotation, float padding)
    {
        Vector3 right = rotation * Vector3.right;
        Vector3 up = rotation * Vector3.up;
        Vector3 forward = rotation * Vector3.forward;

        Vector3 worldHalfExtents = new(
            Mathf.Abs(right.x) * localHalfExtents.x + Mathf.Abs(up.x) * localHalfExtents.y + Mathf.Abs(forward.x) * localHalfExtents.z,
            Mathf.Abs(right.y) * localHalfExtents.x + Mathf.Abs(up.y) * localHalfExtents.y + Mathf.Abs(forward.y) * localHalfExtents.z,
            Mathf.Abs(right.z) * localHalfExtents.x + Mathf.Abs(up.z) * localHalfExtents.y + Mathf.Abs(forward.z) * localHalfExtents.z);

        worldHalfExtents += Vector3.one * padding;
        return new Bounds(center, worldHalfExtents * 2f);
    }


    private static Transform FindDecorativeDisableRoot(Transform transform)
    {
        Transform current = transform;
        while (current != null && current.parent != null)
        {
            string parentName = current.parent.name.ToLowerInvariant();
            if (parentName == "props" ||
                parentName == "items" ||
                parentName == "item stands" ||
                parentName == "dependencies" ||
                parentName == "not connected" ||
                parentName == "connected" ||
                parentName == "walls" ||
                parentName.Contains("---- level") ||
                parentName.StartsWith("module - shop"))
            {
                return current;
            }

            current = current.parent;
        }

        return transform;
    }


    private static Transform FindTransformByPath(string path)
    {
        return ScenePathUtility.FindTransformByPath(path);
    }

    private static string GetTransformPath(Transform t)
    {
        return ShopSceneCache.Current.GetTransformPath(t);
    }
}
