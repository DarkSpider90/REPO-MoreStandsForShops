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

        // Try each spawn point until one succeeds
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

            bool gamblingCompatibility = IsGamblingModulePresent();
            if (!string.IsNullOrEmpty(point.ExtraModule) && !IsModulePresent(point.ExtraModule) && !gamblingCompatibility)
            {
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[UpgradeStandSpawner] Variant '{point.VariantId}' skipped: extra module '{point.ExtraModule}' not present.");
                continue;
            }

            if (gamblingCompatibility && !string.IsNullOrEmpty(point.ExtraModule) && Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"[UpgradeStandSpawner] Variant '{point.VariantId}' allowed by gambling shop module compatibility.");

            // Calculate world position and rotation
            Vector3 position = module.TransformPoint(point.LocalPosition);
            Quaternion rotation = module.rotation * Quaternion.Euler(0f, point.LocalYaw, 0f);

            // Check wall proximity (must be against a wall)
            if (!IsAgainstWall(position, rotation))
            {
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[UpgradeStandSpawner] Variant '{point.VariantId}' rejected: no wall within 0.5m. local={point.LocalPosition}, world={position}, yaw={point.LocalYaw}, sourceCount={point.SourceCount}.");
                continue;
            }

            if (ScenePathUtility.HasActivePath(point.RejectIfPresentPaths, out string presetReject))
            {
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[UpgradeStandSpawner] Variant '{point.VariantId}' rejected: preset protected path is active '{presetReject}'. local={point.LocalPosition}, world={position}, yaw={point.LocalYaw}, sourceCount={point.SourceCount}.");
                continue;
            }

            // Check for protected objects
            if (HasProtectedOverlap(position, rotation, out string protectedObj))
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

            Plugin.Log.LogInfo($"[UpgradeStandSpawner] Successfully spawned additional upgrade stand: variant={point.VariantId}, sourceCount={point.SourceCount}, main={point.MainModule}, extra={point.ExtraModule ?? "<fallback>"}, local={point.LocalPosition}, world={position}, yaw={point.LocalYaw}, itemVolumes={configureItemVolumes}, disabled={disabledObjects.Count}.");
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

        Plugin.Log.LogInfo($"[UpgradeStandSpawner] Network visual spawned: id={spawnId}, variant={variantId}, parent={parentPath}.");
        return true;
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
            Plugin.Log.LogInfo($"[UpgradeStandSpawner] Vanilla upgrade slot scan: all={allUpgradeSources.Count}, accepted={sources.Count}, original={GetTransformPath(originalStand.transform)}.");

            foreach (UpgradeVolumeSource source in allUpgradeSources.OrderBy(s => s.Distance).Take(24))
            {
                bool accepted = IsLikelyOriginalUpgradeSlot(source);
                Plugin.Log.LogInfo($"[UpgradeStandSpawner] Vanilla upgrade slot candidate: accepted={accepted}, distance={source.Distance:F2}, local={source.LocalPosition}, path={source.Path}");
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

        Plugin.Log.LogInfo($"[UpgradeStandSpawner] Copied {created.Count} vanilla upgrade ItemVolume(s) for passive stand.");
        return created
            .OrderByDescending(v => v.transform.localPosition.y)
            .ThenBy(v => v.transform.localPosition.x)
            .ToArray();
    }


    private static IEnumerable<UpgradeVolumeSource> FindAllVanillaUpgradeVolumeSources(Transform originalStand)
    {
        return Resources.FindObjectsOfTypeAll<ItemVolume>()
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
        Plugin.Log.LogInfo("[UpgradeStandSpawner] Upgrade stand prefab prepared.");
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
        return Resources.FindObjectsOfTypeAll<UpgradeStand>()
            .FirstOrDefault(s => s.gameObject.activeInHierarchy && 
                                 !s.name.StartsWith("MoreStandsForShops", System.StringComparison.OrdinalIgnoreCase) &&
                                 !s.name.StartsWith("ExtraItemsShop", System.StringComparison.OrdinalIgnoreCase));
    }


    private static bool IsModulePresent(string moduleName)
    {
        return Resources.FindObjectsOfTypeAll<Transform>()
            .Any(t => t != null && t.gameObject.activeInHierarchy && t.name == moduleName);
    }

    private static bool IsGamblingModulePresent()
    {
        return Resources.FindObjectsOfTypeAll<Transform>()
            .Any(t =>
            {
                if (t == null || !t.gameObject.activeInHierarchy)
                    return false;

                string name = t.name.ToLowerInvariant();
                return name.Contains("module - shop - de - gambling room") ||
                       name.Contains("module - shop - de - solo slot") ||
                       name.Contains("module - shop - de - solo wheel");
            });
    }


    private static bool IsAgainstWall(Vector3 position, Quaternion rotation)
    {
        Vector3[] directions =
        {
            rotation * Vector3.forward,
            rotation * Vector3.back,
            rotation * Vector3.right,
            rotation * Vector3.left
        };

        float maxWallDistance = 0.5f;
        LayerMask wallMask = LayerMask.GetMask("Default");

        foreach (Vector3 dir in directions)
        {
            if (Physics.Raycast(position + Vector3.up * 0.5f, dir, out RaycastHit hit, 2f, wallMask, QueryTriggerInteraction.Ignore))
            {
                if (Mathf.Abs(hit.normal.y) < 0.1f && hit.distance <= maxWallDistance)
                    return true;
            }
        }
        return false;
    }


    private static bool HasProtectedOverlap(Vector3 position, Quaternion rotation, out string objectName)
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
            if (IsProtected(col.transform))
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

        foreach (Renderer renderer in Resources.FindObjectsOfTypeAll<Renderer>())
        {
            if (renderer == null || !renderer.gameObject.activeInHierarchy)
                continue;

            Transform rendererTransform = renderer.transform;
            if (rendererTransform.name.StartsWith("MoreStandsForShops", System.StringComparison.OrdinalIgnoreCase))
                continue;
            if (rendererTransform.name.StartsWith("ExtraItemsShop", System.StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsProtected(rendererTransform))
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
        string path = GetTransformPath(t).ToLowerInvariant();
        string[] protectedFragments =
        {
            "cash register", "cashier", "cashiers desk", "shop owner", "shopkeeper",
            "upgrade stand", "health stand", "revive stand", "battery upgrade stand",
            "item stands", "valuable shelf", "weapon stand", "weapon shelf",
            "extraction", "truck",
            "door", "painting", "secret", "hidden", "passage", "entrance"
        };

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
        Vector3 halfExtents = new(1.15f, 1.15f, 0.75f);
        Vector3 center = position + Vector3.up * halfExtents.y;
        Bounds standBounds = BuildWorldBounds(center, halfExtents, rotation, 0.03f);
        HashSet<Transform> disabledTargets = new();
        var disabledPaths = new List<string>();

        Collider[] overlaps = Physics.OverlapBox(center, halfExtents, rotation, ~0, QueryTriggerInteraction.Ignore);
        foreach (var col in overlaps)
        {
            if (col == null || col.transform == null) continue;
            if (col.transform.name.StartsWith("MoreStandsForShops", System.StringComparison.OrdinalIgnoreCase)) continue;
            if (col.transform.name.StartsWith("ExtraItemsShop", System.StringComparison.OrdinalIgnoreCase)) continue;

            string path = GetTransformPath(col.transform).ToLowerInvariant();
            if (!IsShopModulePath(path)) continue;
            if (IsProtected(col.transform)) continue;
            if (IsStructuralShopSurface(path)) continue;

            Transform disableTarget = FindDecorativeDisableRoot(col.transform);
            TryDisableDecorativeTarget(disableTarget, disabledTargets, disabledPaths, "collider");
        }

        foreach (Renderer renderer in Resources.FindObjectsOfTypeAll<Renderer>())
        {
            if (renderer == null || !renderer.gameObject.activeInHierarchy)
                continue;

            Transform rendererTransform = renderer.transform;
            if (rendererTransform.name.StartsWith("MoreStandsForShops", System.StringComparison.OrdinalIgnoreCase)) continue;
            if (rendererTransform.name.StartsWith("ExtraItemsShop", System.StringComparison.OrdinalIgnoreCase)) continue;
            if (!renderer.bounds.Intersects(standBounds)) continue;

            string path = GetTransformPath(rendererTransform).ToLowerInvariant();
            if (!IsShopModulePath(path)) continue;
            if (IsProtected(rendererTransform)) continue;
            if (IsStructuralShopSurface(path)) continue;
            if (path.Contains("collider") || path.Contains("trigger")) continue;

            Transform disableTarget = FindDecorativeDisableRoot(rendererTransform);
            TryDisableDecorativeTarget(disableTarget, disabledTargets, disabledPaths, "renderer");
        }

        return disabledPaths;
    }


    private static bool TryDisableDecorativeTarget(Transform disableTarget, HashSet<Transform> disabledTargets, List<string> disabledPaths, string reason)
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
        if (!IsShopModulePath(targetPath))
            return false;

        if (IsProtected(disableTarget))
            return false;

        if (IsStructuralShopSurface(targetPath))
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
        if (t == null) return "<null>";
        var stack = new Stack<string>();
        var current = t;
        while (current != null)
        {
            stack.Push(current.name);
            current = current.parent;
        }
        return string.Join("/", stack);
    }
}
