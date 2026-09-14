using System;
using System.Collections.Generic;
using System.Linq;
using MoreStandsForShops.Utilities;

namespace MoreStandsForShops.Shop;

internal static class ShelfItemSelector
{
    private static readonly Dictionary<MoreStandsShelfZone, int> SpawnedByZone = new();
    private static readonly Dictionary<string, int> SpawnedByItem = new();
    private static readonly Dictionary<MoreStandsShelfZone, List<Item>> CandidatesByZone = new();
    private static bool _candidateCacheBuilt;

    internal static void Reset()
    {
        SpawnedByZone.Clear();
        SpawnedByItem.Clear();
        CandidatesByZone.Clear();
        _candidateCacheBuilt = false;
    }

    internal static Item Select(MoreStandsShelfZone zone, SemiFunc.itemVolume slotVolume)
    {
        if (StatsManager.instance == null || SpawnedCount(zone) >= TargetFor(zone))
            return null;

        EnsureCandidateCache();

        if (!CandidatesByZone.TryGetValue(zone, out List<Item> zoneCandidates))
            return null;

        List<Item> candidates = zoneCandidates
            .Where(item => item.itemVolume == slotVolume)
            .Where(item => CanSpawnItem(zone, item))
            .Where(item => SameItemCount(zone, item) < SameCopyLimit(zone))
            .ToList();

        return candidates.OrderBy(WeightedRandomSortKey).FirstOrDefault();
    }

    private static void EnsureCandidateCache()
    {
        if (_candidateCacheBuilt || StatsManager.instance == null)
            return;

        _candidateCacheBuilt = true;

        IEnumerable<Item> uniqueItems = StatsManager.instance.itemDictionary.Values
            .Where(item => item != null && !item.disabled)
            .GroupBy(ItemIdentity, StringComparer.Ordinal)
            .Select(group => group.First())
            .Where(item => Plugin.GetItemSpawnChance(item) > 0);

        foreach (Item item in uniqueItems)
        {
            foreach (MoreStandsShelfZone zone in new[]
                     {
                         MoreStandsShelfZone.Drone,
                         MoreStandsShelfZone.Crystal,
                         MoreStandsShelfZone.Grenade,
                         MoreStandsShelfZone.Health
                     })
            {
                if (!IsZoneItem(item, zone) || IsBlockedShelfItem(item, zone))
                    continue;

                if (!CandidatesByZone.TryGetValue(zone, out List<Item> items))
                {
                    items = new List<Item>();
                    CandidatesByZone.Add(zone, items);
                }

                items.Add(item);
                break;
            }
        }

        if (Plugin.DebugLogs.Value)
        {
            string counts = string.Join(", ", CandidatesByZone
                .OrderBy(pair => (int)pair.Key)
                .Select(pair => $"{pair.Key}={pair.Value.Count}"));
            Plugin.Log.LogInfo($"[ShelfItemSelector] Candidate cache built once for shop: {counts}.");
        }
    }

    internal static void RecordSpawn(MoreStandsShelfZone zone, Item item)
    {
        SpawnedByZone[zone] = SpawnedCount(zone) + 1;

        string key = ItemKey(zone, item);
        SpawnedByItem[key] = SpawnedByItem.TryGetValue(key, out int count) ? count + 1 : 1;
    }

    internal static int TargetFor(MoreStandsShelfZone zone)
    {
        string key = zone switch
        {
            MoreStandsShelfZone.Drone => "Drones",
            MoreStandsShelfZone.Crystal => "Power Crystals",
            MoreStandsShelfZone.Grenade => "Grenades",
            MoreStandsShelfZone.Health => "Health Packs",
            _ => string.Empty
        };

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
            MoreStandsShelfZone.Grenade => category == ShopStockCategory.Grenades,
            MoreStandsShelfZone.Health => category == ShopStockCategory.HealthPacks,
            _ => false
        };
    }

    private static bool IsBlockedShelfItem(Item item, MoreStandsShelfZone zone)
    {
        if (zone != MoreStandsShelfZone.Grenade)
            return false;

        return IsDuctTapedGrenade(item.name) || IsDuctTapedGrenade(ItemName(item));
    }

    private static bool IsDuctTapedGrenade(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string lowered = name.ToLowerInvariant();
        return lowered.Contains("duct") && lowered.Contains("tape") && lowered.Contains("grenade");
    }

    private static int SameCopyLimit(MoreStandsShelfZone zone)
    {
        if (zone == MoreStandsShelfZone.Crystal)
            return TargetFor(zone);

        string key = zone switch
        {
            MoreStandsShelfZone.Drone => "Drones",
            MoreStandsShelfZone.Grenade => "Grenades",
            MoreStandsShelfZone.Health => "Health Packs",
            _ => string.Empty
        };

        return Plugin.SameItemCopies.TryGetValue(key, out var entry) ? entry.Value : TargetFor(zone);
    }

    private static int SameItemCount(MoreStandsShelfZone zone, Item item)
    {
        return SpawnedByItem.TryGetValue(ItemKey(zone, item), out int count) ? count : 0;
    }

    private static bool CanSpawnItem(MoreStandsShelfZone zone, Item item)
    {
        if (item?.prefab == null || !item.prefab.IsValid())
            return false;

        int players = GameDirector.instance != null ? GameDirector.instance.PlayerList.Count : 1;
        if (item.minPlayerCount > players)
            return false;

        return !item.maxPurchase ||
               StatsManager.instance.GetItemsUpgradesPurchasedTotal(item.name) < item.maxPurchaseAmount;
    }

    private static string ItemKey(MoreStandsShelfZone zone, Item item)
    {
        return zone + ":" + ItemIdentity(item);
    }

    private static string ItemIdentity(Item item)
    {
        if (item == null)
            return string.Empty;

        string internalName = string.IsNullOrWhiteSpace(item.name) ? ItemName(item) : item.name;
        string resourcePath = item.prefab?.ResourcePath ?? string.Empty;
        return internalName + "|" + resourcePath;
    }
}
