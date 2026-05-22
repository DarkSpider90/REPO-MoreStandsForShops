using System.Collections.Generic;
using System.Linq;
using MoreStandsForShops.Network;
using MoreStandsForShops.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoreStandsForShops.Spawners;

public static class DroneCrystalStandSpawner
{
    private const string CosmeticModuleName = "Level Generator/Level/Module - Shop - S - Cosmetic Machine 01(Clone)";
    private const string CandyShelf2Name = "Candy Shelf 2";
    // Local position/yaw for the shelf inside the Cosmetic Machine module
    private static readonly Vector3 ShelfLocalPosition = new(2.04f, 0.03f, -5.475f);
    private const float ShelfLocalYaw = 270f;

    // Known paths to vanilla health shelf visuals
    private static readonly string[] KnownHealthShelfPaths =
    {
        "Level Generator/Level/Module - Shop - N - Middle Stands(Clone)/ITEM STANDS/valuable shelf short (1)",
        "Level Generator/Level/Module - Shop - N - Center Extract(Clone)/ITEM STANDS/valuable shelf short (1)",
        "Level Generator/Level/Module - Shop - N - Corner Stands(Clone)/ITEM STANDS/valuable shelf short (1)"
    };

    private static GameObject _cachedPrefab;
    private static bool _prefabPrepared;

    public static bool EnsurePrefabPrepared()
    {
        return _prefabPrepared || PreparePrefab();
    }

    public static bool TrySpawn(out GameObject spawnedStand, bool configureItemVolumes = true)
    {
        spawnedStand = null;

        spawnedStand = FindExistingSpawnedStand();
        if (spawnedStand != null)
        {
            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo("[DroneCrystalStandSpawner] Drone/Crystal stand already exists; skipping duplicate spawn.");
            return true;
        }

        // Find the Cosmetic Machine module
        Transform module = FindTransformByPath(CosmeticModuleName);
        if (module == null)
        {
            Plugin.Log.LogWarning("[DroneCrystalStandSpawner] Cosmetic Machine module not found.");
            return false;
        }

        // Find Candy Shelf 2 inside the module
        Transform props = module.Find("---- Level ------------/PROPS");
        Transform candyShelf2 = props?.Find(CandyShelf2Name);

        if (candyShelf2 == null)
        {
            Plugin.Log.LogWarning("[DroneCrystalStandSpawner] Candy Shelf 2 not found in Cosmetic Machine.");
            return false;
        }

        // Prepare prefab
        if (!_prefabPrepared)
        {
            if (!PreparePrefab())
                return false;
        }

        // Calculate spawn position
        Vector3 position = module.TransformPoint(ShelfLocalPosition);
        Quaternion rotation = module.rotation * Quaternion.Euler(0f, ShelfLocalYaw, 0f);

        // Check wall proximity
        if (!IsAgainstWall(position, rotation))
        {
            Plugin.Log.LogWarning("[DroneCrystalStandSpawner] Position not against a wall.");
            return false;
        }

        // Prepare area
        var disabledObjects = new List<string>();
        if (!PrepareArea(position, rotation, candyShelf2, disabledObjects))
        {
            Plugin.Log.LogWarning("[DroneCrystalStandSpawner] Area preparation failed.");
            return false;
        }

        spawnedStand = Object.Instantiate(_cachedPrefab, position, rotation);
        spawnedStand.name = "MoreStandsForShops Drone Crystal Stand";
        spawnedStand.SetActive(true);

        // Parent to PROPS
        spawnedStand.transform.SetParent(props, true);

        // Disable Candy Shelf 2
        candyShelf2.gameObject.SetActive(false);
        disabledObjects.Add(GetTransformPath(candyShelf2));

        if (configureItemVolumes)
            ItemVolumeHelper.AssignVolumesForDroneCrystalStand(spawnedStand);
        else
            DisableItemVolumes(spawnedStand);

        if (configureItemVolumes && SemiFunc.IsMultiplayer() && Photon.Pun.PhotonNetwork.IsMasterClient)
        {
            ShopLayoutSync.SetDroneCrystalShelf(new DroneCrystalShelfLayout
            {
                Enabled = true,
                    DroneSlotCount = Plugin.ItemCounts.TryGetValue("Drones", out var drones)
                        ? drones.Value
                        : 0,
                    CrystalSlotCount = Plugin.ItemCounts.TryGetValue("Power Crystals", out var crystals)
                        ? crystals.Value
                        : 0,
                    DisabledPaths = disabledObjects.ToArray()
            });
        }

        Plugin.Log.LogInfo($"[DroneCrystalStandSpawner] Drone/Crystal stand spawned successfully. itemVolumes={configureItemVolumes}, disabled={disabledObjects.Count}.");
        return true;
    }


    public static bool SpawnNetworkVisual(string spawnId, string[] disabledPaths)
    {
        if (!EnsurePrefabPrepared())
            return false;

        GameObject existing = FindExistingSpawnedStand();
        if (existing != null)
            return true;

        Transform module = FindTransformByPath(CosmeticModuleName);
        if (module == null)
        {
            Plugin.Log.LogWarning("[DroneCrystalStandSpawner] Network visual skipped: Cosmetic Machine module not found.");
            return false;
        }

        Transform props = module.Find("---- Level ------------/PROPS");
        if (props == null)
        {
            Plugin.Log.LogWarning("[DroneCrystalStandSpawner] Network visual skipped: PROPS root not found.");
            return false;
        }

        Vector3 position = module.TransformPoint(ShelfLocalPosition);
        Quaternion rotation = module.rotation * Quaternion.Euler(0f, ShelfLocalYaw, 0f);

        ScenePathUtility.DisableExactPaths(disabledPaths, "[DroneCrystalStandSpawner:Network]");

        GameObject spawnedStand = Object.Instantiate(_cachedPrefab, position, rotation);
        spawnedStand.name = "MoreStandsForShops Drone Crystal Stand";
        spawnedStand.SetActive(true);
        spawnedStand.transform.SetParent(props, true);
        DisableItemVolumes(spawnedStand);

        Plugin.Log.LogInfo($"[DroneCrystalStandSpawner] Network visual spawned: id={spawnId}, parent={GetTransformPath(props)}.");
        return true;
    }

    private static GameObject FindExistingSpawnedStand()
    {
        return Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.activeInHierarchy)
            .Where(t => t.name.StartsWith("MoreStandsForShops Drone Crystal Stand", System.StringComparison.OrdinalIgnoreCase))
            .Select(t => t.gameObject)
            .FirstOrDefault();
    }

    private static bool PreparePrefab()
    {
        // Find a vanilla health shelf visual
        Transform healthShelf = FindHealthShelfVisual();
        if (healthShelf == null)
        {
            Plugin.Log.LogError("[DroneCrystalStandSpawner] No vanilla health shelf visual found.");
            return false;
        }

        _cachedPrefab = Object.Instantiate(healthShelf.gameObject);
        _cachedPrefab.name = "MoreStandsForShops_DroneCrystalStand_Prefab";
        _cachedPrefab.SetActive(false);
        Object.DontDestroyOnLoad(_cachedPrefab);

        // Remove PhotonView if present
        var pv = _cachedPrefab.GetComponent<Photon.Pun.PhotonView>();
        if (pv != null) Object.Destroy(pv);

        _prefabPrepared = true;
        Plugin.Log.LogInfo("[DroneCrystalStandSpawner] Drone/Crystal stand prefab prepared.");
        return true;
    }

    private static Transform FindHealthShelfVisual()
    {
        foreach (string path in KnownHealthShelfPaths)
        {
            Transform t = FindTransformByPath(path);
            if (t != null && t.gameObject.activeInHierarchy)
            {
                // Must have renderers
                if (t.GetComponentsInChildren<Renderer>(true).Length > 0)
                    return t;
            }
        }

        return Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t != null && t.gameObject.activeInHierarchy)
            .Where(t => string.Equals(t.name, "valuable shelf short (1)", System.StringComparison.OrdinalIgnoreCase))
            .Where(t => GetTransformPath(t).IndexOf("/ITEM STANDS/", System.StringComparison.OrdinalIgnoreCase) >= 0)
            .FirstOrDefault(t => t.GetComponentsInChildren<Renderer>(true).Length > 0);
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

    private static bool PrepareArea(Vector3 position, Quaternion rotation, Transform candyShelf2, List<string> disabledObjects)
    {
        Vector3 halfExtents = new(0.85f, 1.10f, 0.55f);
        Vector3 center = position + Vector3.up * halfExtents.y;

        Collider[] overlaps = Physics.OverlapBox(center, halfExtents, rotation, ~0, QueryTriggerInteraction.Ignore);
        foreach (var col in overlaps)
        {
            if (col == null || col.transform == null) continue;
            if (col.transform == candyShelf2 || col.transform.IsChildOf(candyShelf2)) continue;
            if (col.transform.name.StartsWith("MoreStandsForShops", System.StringComparison.OrdinalIgnoreCase)) continue;

            string path = GetTransformPath(col.transform).ToLowerInvariant();

            // Skip walls/floors/triggers
            if (path.Contains("/walls/") || path.Contains("/floor") || path.Contains("/ceiling")) continue;
            if (path.Contains("collider") || path.Contains("trigger")) continue;

            // Check for protected objects
            if (IsProtected(col.transform))
            {
                Plugin.Log.LogWarning($"[DroneCrystalStandSpawner] Protected object blocks spawn: {col.transform.name}");
                return false;
            }

            // Disable decorative objects (candy shelves, props)
            if (!IsProtected(col.transform))
            {
                Transform disableTarget = FindDecorativeDisableRoot(col.transform);
                if (disableTarget == null || IsProtected(disableTarget) || IsUnsafeDisableRoot(disableTarget))
                    continue;

                disableTarget.gameObject.SetActive(false);
                disabledObjects.Add(GetTransformPath(disableTarget));
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[DroneCrystalStandSpawner] Disabled: {GetTransformPath(disableTarget)}");
            }
        }
        return true;
    }

    private static bool IsProtected(Transform t)
    {
        string path = GetTransformPath(t).ToLowerInvariant();
        string[] protectedFragments =
        {
            "cash register", "cashier", "cashiers desk", "shop owner", "shopkeeper",
            "upgrade stand", "health stand", "revive stand", "battery upgrade stand",
            "item stands", "valuable shelf", "weapon stand", "weapon shelf",
            "window shop", "extraction", "truck"
        };

        foreach (string frag in protectedFragments)
        {
            if (path.Contains(frag)) return true;
        }

        if (t.GetComponentInChildren<ItemVolume>(true) != null) return true;
        if (t.GetComponentInParent<UpgradeStand>(true) != null) return true;

        return false;
    }

    private static bool IsUnsafeDisableRoot(Transform transform)
    {
        string path = GetTransformPath(transform).ToLowerInvariant();
        string name = transform.name.ToLowerInvariant();
        return path.Contains("/dependencies/") ||
               name == "props" ||
               name == "items" ||
               name == "item stands" ||
               name == "dependencies" ||
               name.Contains("---- level") ||
               name.StartsWith("module - shop") ||
               name == "level" ||
               name == "level generator";
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
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            Transform result = root.transform.Find(path);
            if (result != null) return result;

            if (root.name == path.Split('/')[0])
            {
                string subPath = path.Substring(path.IndexOf('/') + 1);
                result = root.transform.Find(subPath);
                if (result != null) return result;
            }
        }
        return null;
    }

    private static string GetTransformPath(Transform t)
    {
        if (t == null) return "<null>";
        var stack = new System.Collections.Generic.Stack<string>();
        var current = t;
        while (current != null)
        {
            stack.Push(current.name);
            current = current.parent;
        }
        return string.Join("/", stack);
    }
}
