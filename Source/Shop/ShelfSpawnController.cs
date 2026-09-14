using System;
using System.Collections.Generic;
using System.Linq;
using MoreStandsForShops.Utilities;
using UnityEngine;

namespace MoreStandsForShops.Shop;

internal static class ShelfSpawnController
{

    internal static void ResetForShop()
    {
        ShelfItemSelector.Reset();
    }


    internal static void PrepareVolumesForPopulate(PunManager punManager)
    {
        if (ShopManager.instance?.itemVolumes == null)
        {
            return;
        }

        foreach (ItemVolume volume in ShopManager.instance.itemVolumes.Where(volume => volume != null))
        {
            MoreStandsShelfVolume marker = volume.GetComponent<MoreStandsShelfVolume>();
            if (marker != null)
            {
                marker.Handled = false;
            }
        }

        SpawnAndRemoveShelfVolumes(punManager);

        ShopManager.instance.itemVolumes = ShopManager.instance.itemVolumes
            .Where(volume => volume != null)
            .OrderBy(volume => volume.GetComponent<MoreStandsUpgradeVolume>() == null ? 1 : 0)
            .ToList();

        int upgradeCount = ShopManager.instance.itemVolumes.Count(volume => volume.GetComponent<MoreStandsUpgradeVolume>() != null);
        if (upgradeCount > 0 && Plugin.DebugLogs.Value)
        {
            Plugin.Log.LogInfo($"[ShelfSpawnController] Prioritized {upgradeCount} additional upgrade ItemVolume(s).");
        }
    }


    private static void SpawnAndRemoveShelfVolumes(PunManager punManager)
    {
        List<ItemVolume> shelfVolumes = ShopManager.instance.itemVolumes
            .Where(volume => volume != null && volume.GetComponent<MoreStandsShelfVolume>() != null)
            .ToList();

        if (shelfVolumes.Count == 0)
        {
            return;
        }

        ShopManager.instance.itemVolumes.RemoveAll(volume => volume == null || volume.GetComponent<MoreStandsShelfVolume>() != null);
        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[ShelfSpawnController] Removed {shelfVolumes.Count} controlled shelf ItemVolume(s) from vanilla population.");

        foreach (ItemVolume volume in shelfVolumes)
        {
            MoreStandsShelfVolume marker = volume.GetComponent<MoreStandsShelfVolume>();
            if (marker != null)
                marker.Handled = true;
        }

        foreach (IGrouping<MoreStandsShelfZone, ItemVolume> zoneGroup in shelfVolumes
                     .GroupBy(volume => volume.GetComponent<MoreStandsShelfVolume>().Zone)
                     .OrderBy(group => (int)group.Key))
        {
            List<ItemVolume> ordered = zoneGroup
                .OrderBy(volume => volume.transform.parent != null ? volume.transform.parent.GetInstanceID() : 0)
                .ThenByDescending(volume => volume.transform.localPosition.y)
                .ThenBy(volume => volume.transform.localPosition.x)
                .ThenBy(volume => volume.transform.localPosition.z)
                .ToList();

            List<ItemVolume> selectedVolumes = SelectEvenlySpacedVolumes(
                ordered,
                ShelfItemSelector.TargetFor(zoneGroup.Key));

            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo(
                    $"[ShelfSpawnController] Controlled shelf zone={zoneGroup.Key}: " +
                    $"availableSlots={ordered.Count}, selectedSlots={selectedVolumes.Count}, " +
                    $"target={ShelfItemSelector.TargetFor(zoneGroup.Key)}.");

            foreach (ItemVolume volume in selectedVolumes)
            {
                MoreStandsShelfVolume marker = volume.GetComponent<MoreStandsShelfVolume>();
                if (marker == null)
                    continue;

                Item item = ShelfItemSelector.Select(marker.Zone, volume.itemVolume);
                if (item == null)
                {
                    if (Plugin.DebugLogs.Value)
                        Plugin.Log.LogInfo($"[ShelfSpawnController] Shelf slot skipped: zone={marker.Zone}, slotVolume={volume.itemVolume}, target={ShelfItemSelector.TargetFor(marker.Zone)}, spawned={ShelfItemSelector.SpawnedCount(marker.Zone)}.");
                    continue;
                }

                bool spawned = SpawnShelfItem(punManager, volume, marker.Zone, item, isSecret: false);
                if (!spawned)
                {
                    Plugin.Log.LogWarning($"[ShelfSpawnController] Vanilla rejected shelf item: zone={marker.Zone}, item={ShelfItemSelector.ItemName(item)}, itemVolume={item.itemVolume}, slotVolume={volume.itemVolume}.");
                    continue;
                }

                ShelfItemSelector.RecordSpawn(marker.Zone, item);
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[ShelfSpawnController] Spawned shelf item: zone={marker.Zone}, item={ShelfItemSelector.ItemName(item)}, slotVolume={volume.itemVolume}.");
            }
        }
    }


    private static List<ItemVolume> SelectEvenlySpacedVolumes(List<ItemVolume> ordered, int target)
    {
        if (ordered == null || ordered.Count == 0 || target <= 0)
            return new List<ItemVolume>();

        if (target >= ordered.Count)
            return new List<ItemVolume>(ordered);

        if (target == 1)
            return new List<ItemVolume> { ordered[ordered.Count / 2] };

        var selected = new List<ItemVolume>(target);
        for (int i = 0; i < target; i++)
        {
            float normalized = i / (float)(target - 1);
            int index = Mathf.RoundToInt(normalized * (ordered.Count - 1));
            selected.Add(ordered[index]);
        }

        return selected;
    }


    internal static bool TryHandleSpawnShopItem(PunManager punManager, ItemVolume itemVolume, List<Item> itemList, ref int spawnCount, bool isSecret, out bool result)
    {
        result = false;
        MoreStandsShelfVolume marker = itemVolume == null ? null : itemVolume.GetComponent<MoreStandsShelfVolume>();
        if (marker == null)
        {
            return false;
        }

        if (!Plugin.EnableMod.Value || isSecret || !SemiFunc.IsMasterClientOrSingleplayer())
        {
            result = false;
            return true;
        }

        if (marker.Handled)
        {
            result = false;
            return true;
        }

        if (!IsAllowedVanillaList(marker.Zone, itemList))
        {
            result = false;
            return true;
        }

        marker.Handled = true;
        Item item = ShelfItemSelector.Select(marker.Zone, itemVolume.itemVolume);
        if (item == null)
        {
            Plugin.Log.LogWarning($"[ShelfSpawnController] No valid {marker.Zone} item for slotVolume={itemVolume.itemVolume}; slot skipped.");
            result = false;
            return true;
        }

        bool spawned = SpawnShelfItem(punManager, itemVolume, marker.Zone, item, isSecret);
        if (spawned)
        {
            ShelfItemSelector.RecordSpawn(marker.Zone, item);
            if (!isSecret)
            {
                spawnCount++;
            }

            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"[ShelfSpawnController] Spawned {marker.Zone}: {ShelfItemSelector.ItemName(item)} in slotVolume={itemVolume.itemVolume}.");
        }
        else
        {
            Plugin.Log.LogWarning($"[ShelfSpawnController] Vanilla rejected {marker.Zone}: {ShelfItemSelector.ItemName(item)} itemVolume={item.itemVolume}, slotVolume={itemVolume.itemVolume}.");
        }

        result = spawned;
        return true;
    }


    private static bool IsAllowedVanillaList(MoreStandsShelfZone zone, List<Item> itemList)
    {
        if (ShopManager.instance == null)
        {
            return false;
        }

        return zone switch
        {
            MoreStandsShelfZone.Drone => ReferenceEquals(itemList, ShopManager.instance.potentialItems),
            MoreStandsShelfZone.Crystal => ReferenceEquals(itemList, ShopManager.instance.potentialItemConsumables),
            MoreStandsShelfZone.Grenade => ReferenceEquals(itemList, ShopManager.instance.potentialItems),
            MoreStandsShelfZone.Health => ReferenceEquals(itemList, ShopManager.instance.potentialItemHealthPacks),
            _ => false
        };
    }


    private static bool SpawnShelfItem(
        PunManager punManager,
        ItemVolume itemVolume,
        MoreStandsShelfZone zone,
        Item item,
        bool isSecret)
    {
        float? worldEulerXOverride = zone == MoreStandsShelfZone.Grenade && IsShockwaveGrenade(item)
            ? 90f
            : null;

        if (worldEulerXOverride.HasValue && Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo("[ShelfSpawnController] Forcing Shockwave Grenade world X rotation to 90 degrees.");

        return VanillaShopItemSpawner.TrySpawnSingle(
            punManager,
            itemVolume,
            item,
            isSecret,
            worldEulerXOverride);
    }


    private static bool IsShockwaveGrenade(Item item)
    {
        return string.Equals(item?.name, "Shockwave Grenade", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ShelfItemSelector.ItemName(item), "Shockwave Grenade", StringComparison.OrdinalIgnoreCase);
    }

}
