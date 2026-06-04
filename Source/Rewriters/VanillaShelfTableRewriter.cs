using System;
using System.Collections.Generic;
using System.Linq;
using MoreStandsForShops.Utilities;
using UnityEngine;

namespace MoreStandsForShops.Rewriters;

internal static class VanillaShelfTableRewriter
{
    private const string GeneratedPrefix = "MoreStandsForShops Vanilla Shelf Slot";
    private const string MultiSizePrefix = "MoreStandsForShops Table Multi Slot";
    private const float SameTableSlotDistance = 0.03f;

    private static readonly SemiFunc.itemVolume[] TableSlotTypes =
    {
        SemiFunc.itemVolume.medium,
        SemiFunc.itemVolume.large,
        SemiFunc.itemVolume.large_high
    };

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

        int convertedTableSlots = ConvertVanillaTableVolumesInItemAreas();

        if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[ShelfTableRewrite] Rewritten health shelves={rewrittenShelves}, converted table slot group(s)={convertedTableSlots}.");
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

    private static int ConvertVanillaTableVolumesInItemAreas()
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
            int created = 0;
            int reused = 0;
            int disabled = 0;

            for (int i = 0; i < logicalSlots.Count; i++)
            {
                TableLogicalSlot slot = logicalSlots[i];
                string groupId = $"table-slot:{tableRoot.GetInstanceID()}:{i:00}";
                RewriteLogicalTableSlot(tableRoot, slot, groupId, ref reused, ref created, ref disabled);
            }

            converted += logicalSlots.Count;

            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo(
                    $"[ShelfTableRewrite] Converted table item area to multi-size slots: " +
                    $"root={GetTransformPath(tableRoot)}, originalVolumes={originalVolumes.Count}, " +
                    $"slotGroups={logicalSlots.Count}, reused={reused}, created={created}, disabled={disabled}.");
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

    private static ItemVolume CreateMultiSizeVolume(Transform parent, TableVolumePose pose, string groupId, SemiFunc.itemVolume itemVolume)
    {
        GameObject slot = new($"{MultiSizePrefix} {itemVolume}");
        slot.transform.SetParent(parent, false);
        ApplyPose(slot.transform, pose);

        ItemVolume volume = slot.AddComponent<ItemVolume>();
        volume.itemVolume = itemVolume;
        volume.itemSecretShopType = SemiFunc.itemSecretShopType.none;
        volume.volumes = new List<GameObject>(pose.Source.Volumes);

        MoreStandsMultiSizeVolume marker = slot.AddComponent<MoreStandsMultiSizeVolume>();
        marker.GroupId = groupId;
        return volume;
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

    private static void RewriteLogicalTableSlot(
        Transform tableRoot,
        TableLogicalSlot slot,
        string groupId,
        ref int reused,
        ref int created,
        ref int disabled)
    {
        HashSet<ItemVolume> used = new();

        foreach (SemiFunc.itemVolume itemVolume in TableSlotTypes)
        {
            ItemVolume reusable = PickReusableVolume(slot, itemVolume, used);
            TableVolumePose pose = BuildPoseForType(slot, itemVolume);

            if (reusable != null)
            {
                ApplyPose(reusable.transform, pose);
                reusable.volumes = new List<GameObject>(pose.Source.Volumes);
                MarkMultiSizeVolume(reusable, groupId, itemVolume);
                used.Add(reusable);
                reused++;
            }
            else
            {
                CreateMultiSizeVolume(tableRoot, pose, groupId, itemVolume);
                created++;
            }

            if (Plugin.DebugLogs.Value)
            {
                Plugin.Log.LogInfo(
                    $"[ShelfTableRewrite] Prepared table slot variant: group={groupId}, type={itemVolume}, " +
                    $"local={FormatVector(pose.LocalPosition)}, yaw={pose.LocalRotation.eulerAngles.y:F1}, " +
                    $"template={pose.Source.Name}.");
            }
        }

        foreach (ItemVolume extra in slot.Volumes.Where(volume => !used.Contains(volume)))
        {
            extra.enabled = false;
            extra.gameObject.SetActive(false);
            disabled++;
        }
    }

    private static ItemVolume PickReusableVolume(TableLogicalSlot slot, SemiFunc.itemVolume itemVolume, HashSet<ItemVolume> used)
    {
        ItemVolume exact = slot.Volumes.FirstOrDefault(volume => !used.Contains(volume) && volume.itemVolume == itemVolume);
        if (exact != null)
            return exact;

        if (itemVolume == SemiFunc.itemVolume.medium)
            return slot.Volumes.FirstOrDefault(volume => !used.Contains(volume));

        return null;
    }

    private static TableVolumePose BuildPoseForType(TableLogicalSlot slot, SemiFunc.itemVolume itemVolume)
    {
        TableVolumeTemplate template = FindNearestTemplate(slot, itemVolume)
                                       ?? FindNearestTemplate(slot, SemiFunc.itemVolume.medium)
                                       ?? new TableVolumeTemplate(slot.Volumes[0]);

        Vector3 templateLocal = template.LocalPosition;
        return new TableVolumePose(
            template,
            new Vector3(slot.Center.x, templateLocal.y, slot.Center.y),
            template.LocalRotation,
            template.LocalScale);
    }

    private static TableVolumeTemplate FindNearestTemplate(TableLogicalSlot slot, SemiFunc.itemVolume itemVolume)
    {
        return slot.AllTableVolumes
            .Where(volume => volume.ItemVolume == itemVolume)
            .OrderBy(volume => Vector2.Distance(slot.Center, new Vector2(volume.LocalPosition.x, volume.LocalPosition.z)))
            .FirstOrDefault();
    }

    private static void ApplyPose(Transform transform, TableVolumePose pose)
    {
        transform.localPosition = pose.LocalPosition;
        transform.localRotation = pose.LocalRotation;
        transform.localScale = pose.LocalScale;
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
        internal List<TableVolumeTemplate> AllTableVolumes { get; private set; }
        internal Vector2 Center { get; private set; }

        internal void Add(ItemVolume volume, Vector2 point)
        {
            Center = Volumes.Count == 0
                ? point
                : ((Center * Volumes.Count) + point) / (Volumes.Count + 1);

            Volumes.Add(volume);
            AllTableVolumes ??= volume.transform.parent.GetComponentsInChildren<ItemVolume>(true)
                .Where(itemVolume => itemVolume != null && IsTableVolumeType(itemVolume.itemVolume))
                .Where(itemVolume => !itemVolume.name.StartsWith("MoreStandsForShops", StringComparison.OrdinalIgnoreCase))
                .Where(itemVolume => itemVolume.GetComponent<MoreStandsMultiSizeVolume>() == null)
                .Select(itemVolume => new TableVolumeTemplate(itemVolume))
                .ToList();
        }
    }
    
    
    private sealed class TableVolumeTemplate
    {
        internal readonly ItemVolume Source;
        internal readonly SemiFunc.itemVolume ItemVolume;
        internal readonly string Name;
        internal readonly Vector3 LocalPosition;
        internal readonly Quaternion LocalRotation;
        internal readonly Vector3 LocalScale;
        internal readonly List<GameObject> Volumes;

        internal TableVolumeTemplate(ItemVolume source)
        {
            Source = source;
            ItemVolume = source.itemVolume;
            Name = source.name;
            LocalPosition = source.transform.localPosition;
            LocalRotation = source.transform.localRotation;
            LocalScale = source.transform.localScale;
            Volumes = new List<GameObject>(source.volumes);
        }
    }
    

    private readonly struct TableVolumePose
    {
        internal readonly TableVolumeTemplate Source;
        internal readonly Vector3 LocalPosition;
        internal readonly Quaternion LocalRotation;
        internal readonly Vector3 LocalScale;

        internal TableVolumePose(TableVolumeTemplate source, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            Source = source;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
            LocalScale = localScale;
        }
    }
}
