using System.Collections.Generic;
using System.Linq;
using MoreStandsForShops.Utilities;

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
        if (upgradeCount > 0)
        {
            Plugin.Log.LogInfo($"[ShelfSpawnController] Prioritized {upgradeCount} additional upgrade ItemVolume(s).");
        }
    }


    private static void SpawnAndRemoveShelfVolumes(PunManager punManager)
    {
        List<ItemVolume> shelfVolumes = ShopManager.instance.itemVolumes
            .Where(volume => volume != null && volume.GetComponent<MoreStandsShelfVolume>() != null)
            .OrderBy(volume => volume.GetComponent<MoreStandsShelfVolume>().Zone == MoreStandsShelfZone.Crystal ? 0 : 1)
            .ThenByDescending(volume => volume.transform.position.y)
            .ThenBy(volume => volume.transform.position.x)
            .ToList();

        if (shelfVolumes.Count == 0)
        {
            return;
        }

        ShopManager.instance.itemVolumes.RemoveAll(volume => volume == null || volume.GetComponent<MoreStandsShelfVolume>() != null);
        Plugin.Log.LogInfo($"[ShelfSpawnController] Removed {shelfVolumes.Count} controlled shelf ItemVolume(s) from vanilla population.");

        foreach (ItemVolume volume in shelfVolumes)
        {
            MoreStandsShelfVolume marker = volume.GetComponent<MoreStandsShelfVolume>();
            if (marker == null)
            {
                continue;
            }

            marker.Handled = true;
            Item item = ShelfItemSelector.Select(marker.Zone, volume.itemVolume);
            if (item == null)
            {
                Plugin.Log.LogInfo($"[ShelfSpawnController] Shelf slot skipped: zone={marker.Zone}, slotVolume={volume.itemVolume}, target={ShelfItemSelector.TargetFor(marker.Zone)}, spawned={ShelfItemSelector.SpawnedCount(marker.Zone)}.");
                continue;
            }

            bool spawned = VanillaShopItemSpawner.TrySpawnSingle(punManager, volume, item, isSecret: false);
            if (!spawned)
            {
                Plugin.Log.LogWarning($"[ShelfSpawnController] Vanilla rejected shelf item: zone={marker.Zone}, item={ShelfItemSelector.ItemName(item)}, itemVolume={item.itemVolume}, slotVolume={volume.itemVolume}.");
                continue;
            }

            ShelfItemSelector.RecordSpawn(marker.Zone, item);
            Plugin.Log.LogInfo($"[ShelfSpawnController] Spawned shelf item: zone={marker.Zone}, item={ShelfItemSelector.ItemName(item)}, slotVolume={volume.itemVolume}.");
        }
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

        bool spawned = VanillaShopItemSpawner.TrySpawnSingle(punManager, itemVolume, item, isSecret);
        if (spawned)
        {
            ShelfItemSelector.RecordSpawn(marker.Zone, item);
            if (!isSecret)
            {
                spawnCount++;
            }

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
            _ => false
        };
    }

}
