using System;
using System.Collections.Generic;
using System.Linq;
using MoreStandsForShops.Utilities;

namespace MoreStandsForShops.Shop;

internal static class ShelfItemSelector
{
    private static readonly Dictionary<MoreStandsShelfZone, int> SpawnedByZone = new();
    private static readonly Dictionary<string, int> SpawnedByItem = new();

    internal static void Reset()
    {
        SpawnedByZone.Clear();
        SpawnedByItem.Clear();
    }

    internal static Item Select(MoreStandsShelfZone zone, SemiFunc.itemVolume slotVolume)
    {
        if (StatsManager.instance == null || SpawnedCount(zone) >= TargetFor(zone))
            return null;

        return StatsManager.instance.itemDictionary.Values
            .Where(item => item != null && !item.disabled)
            .Where(item => IsZoneItem(item, zone))
            .Where(item => item.itemVolume == slotVolume)
            .Where(item => Plugin.GetItemSpawnChance(item) > 0)
            .Where(item => SameItemCount(zone, item) < SameCopyLimit(zone))
            .OrderBy(WeightedRandomSortKey)
            .FirstOrDefault();
    }

    internal static void RecordSpawn(MoreStandsShelfZone zone, Item item)
    {
        SpawnedByZone[zone] = SpawnedCount(zone) + 1;

        string key = ItemKey(zone, item);
        SpawnedByItem[key] = SpawnedByItem.TryGetValue(key, out int count) ? count + 1 : 1;
    }

    internal static int TargetFor(MoreStandsShelfZone zone)
    {
        string key = zone == MoreStandsShelfZone.Drone ? "Drones" : "Power Crystals";
        return Plugin.ItemCounts.TryGetValue(key, out var entry) ? entry.Value : 0;
    }

    internal static int SpawnedCount(MoreStandsShelfZone zone)
    {
        return SpawnedByZone.TryGetValue(zone, out int count) ? count : 0;
    }

    internal static string ItemName(Item item)
    {
        if (item == null)
            return "<null>";

        return string.IsNullOrWhiteSpace(item.itemName) ? item.name : item.itemName;
    }

    private static double WeightedRandomSortKey(Item item)
    {
        int weight = Math.Max(1, Plugin.GetItemSpawnChance(item));
        double roll = Math.Max(UnityEngine.Random.value, 0.000001f);

        return -Math.Log(roll) / weight;
    }

    private static bool IsZoneItem(Item item, MoreStandsShelfZone zone)
    {
        ShopStockCategory category = ShopStockCatalog.GetCategory(item);

        return zone switch
        {
            MoreStandsShelfZone.Drone => category == ShopStockCategory.Drones,
            MoreStandsShelfZone.Crystal => category == ShopStockCategory.PowerCrystals,
            _ => false
        };
    }

    private static int SameCopyLimit(MoreStandsShelfZone zone)
    {
        if (zone == MoreStandsShelfZone.Crystal)
            return TargetFor(zone);

        return Plugin.SameItemCopies.TryGetValue("Drones", out var entry) ? entry.Value : TargetFor(zone);
    }

    private static int SameItemCount(MoreStandsShelfZone zone, Item item)
    {
        return SpawnedByItem.TryGetValue(ItemKey(zone, item), out int count) ? count : 0;
    }

    private static string ItemKey(MoreStandsShelfZone zone, Item item)
    {
        return zone + ":" + ItemName(item);
    }
}