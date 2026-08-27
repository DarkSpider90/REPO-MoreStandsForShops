using System;
using System.Collections.Generic;
using System.Linq;
using MoreStandsForShops.Utilities;
using UnityEngine;

namespace MoreStandsForShops.Rewriters;

internal static class VanillaShelfTableRewriter
{
    private const string GeneratedPrefix = "MoreStandsForShops Vanilla Shelf Slot";
    private const float SameTableSlotDistance = 0.03f;

    private static readonly Vector3[] HealthSlotPositions =
    {
        new(-0.72f, 1.68f, 0.50f),
        new(-0.36f, 1.68f, 0.50f),
        new(0.00f, 1.68f, 0.50f),
        new(0.36f, 1.68f, 0.50f),
        new(0.72f, 1.68f, 0.50f)
    };

    private static readonly Vector3[] LowerSmallSlotPositions =
    {
        new(-0.75f, 1.18f, 0.50f),
        new(-0.45f, 1.18f, 0.50f),
        new(-0.15f, 1.18f, 0.50f),
        new(0.15f, 1.18f, 0.50f),
        new(0.45f, 1.18f, 0.50f),
        new(0.75f, 1.18f, 0.50f)
    };

    internal static void Apply()
    {
        if (!Plugin.EnableVanillaShelfTableRewrite.Value)
        {
            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo("[ShelfTableRewrite] Disabled in config.");
            return;
        }

        List<Transform> healthShelves = FindItemStandRoots("valuable shelf short (1)").ToList();
        int rewrittenShelves = 0;
        foreach (Transform shelf in healthShelves)
        {
            if (RewriteHealthShelf(shelf))
                rewrittenShelves++;
        }

        int preservedTableSlots = PreserveVanillaTableVolumesInItemAreas();

        if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[ShelfTableRewrite] Rewritten health shelves={rewrittenShelves}, preserved vanilla table slot group(s)={preservedTableSlots}.");
    }

    private static bool RewriteHealthShelf(Transform shelf)
    {
        if (shelf == null || !shelf.gameObject.activeInHierarchy)
            return false;

        DisableOriginalShelfVolumes(shelf);

        if (shelf.GetComponentsInChildren<ItemVolume>(true)
            .Any(volume => volume != null && volume.name.StartsWith(GeneratedPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"[ShelfTableRewrite] Shelf already has generated slots: {GetTransformPath(shelf)}");
            return true;
        }

        for (int i = 0; i < HealthSlotPositions.Length; i++)
        {
            CreateVolumeSlot(
                shelf,
                $"{GeneratedPrefix} Health {i + 1:00}",
                SemiFunc.itemVolume.healthPack,
                HealthSlotPositions[i]);
        }

        for (int i = 0; i < LowerSmallSlotPositions.Length; i++)
        {
            CreateVolumeSlot(
                shelf,
                $"{GeneratedPrefix} Grenade {i + 1:00}",
                SemiFunc.itemVolume.small,
                LowerSmallSlotPositions[i],
                MoreStandsShelfZone.Grenade);
        }

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[ShelfTableRewrite] Created {HealthSlotPositions.Length} healthPack upper slot(s) and {LowerSmallSlotPositions.Length} grenade lower slot(s) on {GetTransformPath(shelf)}.");

        return true;
    }

    private static void DisableOriginalShelfVolumes(Transform shelf)
    {
        foreach (ItemVolume volume in shelf.GetComponentsInChildren<ItemVolume>(true))
        {
            if (volume == null || volume.name.StartsWith(GeneratedPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            volume.gameObject.SetActive(false);
            volume.enabled = false;
        }
    }

    private static int PreserveVanillaTableVolumesInItemAreas()
    {
        int converted = 0;
        var tableRoots = FindActiveVanillaTableVolumes()
            .GroupBy(volume => volume.transform.parent)
            .Where(group => group.Key != null)
            .ToList();

        foreach (var tableRootGroup in tableRoots)
        {
            Transform tableRoot = tableRootGroup.Key;
            if (tableRoot.GetComponentsInChildren<MoreStandsMultiSizeVolume>(true).Any())
            {
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[ShelfTableRewrite] Table already has multi-size slots: {GetTransformPath(tableRoot)}");
                continue;
            }

            List<ItemVolume> originalVolumes = tableRootGroup
                .OrderBy(volume => volume.transform.localPosition.z)
                .ThenBy(volume => volume.transform.localPosition.x)
                .ThenBy(volume => (int)volume.itemVolume)
                .ToList();

            List<TableLogicalSlot> logicalSlots = BuildLogicalSlots(originalVolumes);
            int reused = 0;

            for (int i = 0; i < logicalSlots.Count; i++)
            {
                TableLogicalSlot slot = logicalSlots[i];
                string groupId = $"table-slot:{tableRoot.GetInstanceID()}:{i:00}";
                PreserveLogicalTableSlot(slot, groupId, ref reused);
            }

            converted += logicalSlots.Count;

            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo(
                    $"[ShelfTableRewrite] Preserved vanilla table item area: " +
                    $"root={GetTransformPath(tableRoot)}, originalVolumes={originalVolumes.Count}, " +
                    $"slotGroups={logicalSlots.Count}, preserved={reused}.");
        }

        return converted;
    }

    private static void MarkMultiSizeVolume(ItemVolume volume, string groupId, SemiFunc.itemVolume itemVolume)
    {
        volume.itemVolume = itemVolume;
        volume.itemSecretShopType = SemiFunc.itemSecretShopType.none;
        volume.enabled = true;
        volume.gameObject.SetActive(true);

        MoreStandsMultiSizeVolume marker = volume.GetComponent<MoreStandsMultiSizeVolume>();
        if (marker == null)
            marker = volume.gameObject.AddComponent<MoreStandsMultiSizeVolume>();

        marker.GroupId = groupId;
    }

    private static IEnumerable<ItemVolume> FindActiveVanillaTableVolumes()
    {
        ShopSceneCache cache = ShopSceneCache.Current;

        return cache.ItemVolumes
            .Where(volume => volume != null && volume.gameObject.activeInHierarchy)
            .Where(volume => !volume.name.StartsWith("MoreStandsForShops", StringComparison.OrdinalIgnoreCase))
            .Where(volume => volume.GetComponent<MoreStandsMultiSizeVolume>() == null)
            .Where(volume => IsTableVolumeType(volume.itemVolume))
            .Where(volume => GetTransformPath(volume.transform).IndexOf("/ITEM STANDS/ITEMS/", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsTableVolumeType(SemiFunc.itemVolume itemVolume)
    {
        return itemVolume is SemiFunc.itemVolume.small or SemiFunc.itemVolume.medium or SemiFunc.itemVolume.large or SemiFunc.itemVolume.large_high;
    }

    private static List<TableLogicalSlot> BuildLogicalSlots(List<ItemVolume> volumes)
    {
        List<TableLogicalSlot> slots = new();

        foreach (ItemVolume volume in volumes)
        {
            Vector2 point = new(volume.transform.localPosition.x, volume.transform.localPosition.z);
            TableLogicalSlot slot = slots.FirstOrDefault(existing => Vector2.Distance(existing.Center, point) <= SameTableSlotDistance);

            if (slot == null)
            {
                slot = new TableLogicalSlot();
                slots.Add(slot);
            }

            slot.Add(volume, point);
        }

        return slots;
    }

    private static void PreserveLogicalTableSlot(
        TableLogicalSlot slot,
        string groupId,
        ref int reused)
    {
        // Vanilla does not correct item placement after spawning: it trusts the exact
        // position and rotation authored on every ItemVolume. Keep those authored
        // poses and types intact. Fabricating medium/large/large_high variants at the
        // same X/Z can place a large item on a surface designed for a different item,
        // causing it to collide with the stand or a neighbouring item and fall down.
        foreach (ItemVolume volume in slot.Volumes)
        {
            if (volume == null)
                continue;

            SemiFunc.itemVolume originalType = volume.itemVolume;
            if (slot.Volumes.Count > 1)
                MarkMultiSizeVolume(volume, groupId, originalType);
            reused++;

            if (Plugin.DebugLogs.Value)
            {
                Plugin.Log.LogInfo(
                    $"[ShelfTableRewrite] Preserved vanilla table slot: group={groupId}, type={originalType}, " +
                    $"local={FormatVector(volume.transform.localPosition)}, " +
                    $"yaw={volume.transform.localRotation.eulerAngles.y:F1}.");
            }
        }
    }

    private static ItemVolume CreateVolumeSlot(Transform parent, string name, SemiFunc.itemVolume itemVolume, Vector3 localPosition, MoreStandsShelfZone? zone = null)
    {
        GameObject slot = new(name);
        slot.transform.SetParent(parent, false);
        slot.transform.localPosition = localPosition;
        slot.transform.localRotation = Quaternion.identity;

        ItemVolume volume = slot.AddComponent<ItemVolume>();
        volume.itemVolume = itemVolume;
        volume.itemSecretShopType = SemiFunc.itemSecretShopType.none;

        if (zone.HasValue)
        {
            MoreStandsShelfVolume marker = slot.AddComponent<MoreStandsShelfVolume>();
            marker.Zone = zone.Value;
        }

        return volume;
    }

    private static IEnumerable<Transform> FindItemStandRoots(string exactName)
    {
        ShopSceneCache cache = ShopSceneCache.Current;

        return cache.Transforms
            .Where(transform => transform != null && transform.gameObject.activeInHierarchy)
            .Where(transform => string.Equals(transform.name, exactName, StringComparison.OrdinalIgnoreCase))
            .Where(transform => GetTransformPath(transform).IndexOf("/ITEM STANDS/", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string GetTransformPath(Transform transform)
    {
        return ShopSceneCache.Current.GetTransformPath(transform);
    }

    private static string FormatVector(Vector3 vector)
    {
        return $"({vector.x:F3}, {vector.y:F3}, {vector.z:F3})";
    }

    private sealed class TableLogicalSlot
    {
        internal readonly List<ItemVolume> Volumes = new();
        internal Vector2 Center { get; private set; }

        internal void Add(ItemVolume volume, Vector2 point)
        {
            Center = Volumes.Count == 0
                ? point
                : ((Center * Volumes.Count) + point) / (Volumes.Count + 1);

            Volumes.Add(volume);
        }
    }
}
