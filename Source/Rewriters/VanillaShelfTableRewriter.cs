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

        int convertedSmallVolumes = ConvertVanillaSmallVolumesInItemAreas();

        Plugin.Log.LogInfo($"[ShelfTableRewrite] Rewritten health shelves={rewrittenShelves}, converted table small ItemVolume(s)={convertedSmallVolumes}.");
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

    private static int ConvertVanillaSmallVolumesInItemAreas()
    {
        int converted = 0;
        foreach (ItemVolume volume in Resources.FindObjectsOfTypeAll<ItemVolume>())
        {
            if (volume == null || volume.itemVolume != SemiFunc.itemVolume.small)
                continue;

            if (volume.name.StartsWith("MoreStandsForShops", StringComparison.OrdinalIgnoreCase))
                continue;

            string path = GetTransformPath(volume.transform);
            if (path.IndexOf("/ITEM STANDS/ITEMS/", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            string groupId = $"table-small:{volume.GetInstanceID()}:{path}";
            if (HasMultiSizeGroup(groupId))
                continue;

            MarkMultiSizeVolume(volume, groupId, SemiFunc.itemVolume.medium);
            CreateMirroredMultiSizeVolume(volume, groupId, SemiFunc.itemVolume.large);
            CreateMirroredMultiSizeVolume(volume, groupId, SemiFunc.itemVolume.large_high);
            converted++;

            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"[ShelfTableRewrite] Converted vanilla table small volume to medium/large/large_high group: {path}");
        }

        return converted;
    }

    private static bool HasMultiSizeGroup(string groupId)
    {
        return Resources.FindObjectsOfTypeAll<MoreStandsMultiSizeVolume>()
            .Any(marker => marker != null && string.Equals(marker.GroupId, groupId, StringComparison.Ordinal));
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

    private static ItemVolume CreateMirroredMultiSizeVolume(ItemVolume source, string groupId, SemiFunc.itemVolume itemVolume)
    {
        GameObject slot = new($"{MultiSizePrefix} {itemVolume}");
        slot.transform.SetParent(source.transform.parent, false);
        slot.transform.localPosition = source.transform.localPosition;
        slot.transform.localRotation = source.transform.localRotation;
        slot.transform.localScale = source.transform.localScale;

        ItemVolume volume = slot.AddComponent<ItemVolume>();
        volume.itemVolume = itemVolume;
        volume.itemSecretShopType = SemiFunc.itemSecretShopType.none;

        MoreStandsMultiSizeVolume marker = slot.AddComponent<MoreStandsMultiSizeVolume>();
        marker.GroupId = groupId;
        return volume;
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
        return Resources.FindObjectsOfTypeAll<Transform>()
            .Where(transform => transform != null && transform.gameObject.activeInHierarchy)
            .Where(transform => string.Equals(transform.name, exactName, StringComparison.OrdinalIgnoreCase))
            .Where(transform => GetTransformPath(transform).IndexOf("/ITEM STANDS/", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
            return "<null>";

        Stack<string> stack = new();
        Transform current = transform;
        while (current != null)
        {
            stack.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", stack);
    }
}
