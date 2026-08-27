using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoreStandsForShops.Shop;

internal static class ShopPoolPlanner
{
    internal static void PreparePools(ShopManager shopManager)
    {
        if (shopManager == null)
            return;

        TopUpPotentialPoolsFromStats(shopManager);
        RemoveControlledShelfItemsFromVanillaPools(shopManager);

        shopManager.potentialItemUpgrades = BuildUpgradePool();
        shopManager.potentialItems = FilterListByConfiguredLimits(shopManager.potentialItems, "standard");
        shopManager.potentialItemConsumables = FilterListByConfiguredLimits(shopManager.potentialItemConsumables, "consumables");
        shopManager.potentialItemHealthPacks = FilterListByConfiguredLimits(shopManager.potentialItemHealthPacks, "health");
    }


    private static List<Item> BuildUpgradePool()
    {
        int target = GetCount("Total Upgrades");
        var result = new List<Item>(System.Math.Max(0, target));
        var itemDict = StatsManager.instance?.itemDictionary;

        if (target <= 0 || itemDict == null)
            return result;

        int playerCount = GameDirector.instance != null
            ? GameDirector.instance.PlayerList.Count
            : 1;
        int sameItemLimit = Plugin.SameItemCopies.TryGetValue("Upgrades", out var copyEntry)
            ? copyEntry.Value
            : target;

        List<Item> candidates = itemDict.Values
            .Where(item => item != null && item.itemType == SemiFunc.itemType.item_upgrade)
            .GroupBy(ItemKey, System.StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(ItemKey, System.StringComparer.Ordinal)
            .ToList();

        var eligible = new List<Item>(candidates.Count);
        foreach (Item item in candidates)
        {
            if (TryGetUpgradeBlockReason(item, playerCount, out string reason))
            {
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[ShopPoolPlanner] Upgrade candidate blocked: item={ItemName(item)}, reason={reason}.");
                continue;
            }

            eligible.Add(item);
        }

        var selectedCounts = new Dictionary<string, int>(System.StringComparer.Ordinal);
        while (result.Count < target)
        {
            Item selected = SelectWeightedUpgrade(eligible, selectedCounts, sameItemLimit);
            if (selected == null)
                break;

            string key = ItemKey(selected);
            selectedCounts[key] = selectedCounts.TryGetValue(key, out int current) ? current + 1 : 1;
            result.Add(selected);
        }

        if (Plugin.DebugLogs.Value)
        {
            string candidateSummary = string.Join(", ", eligible.Select(item => $"{ItemName(item)}={Plugin.GetItemSpawnChance(item)}"));
            string selectionSummary = string.Join(", ", result
                .GroupBy(ItemKey, System.StringComparer.Ordinal)
                .OrderBy(group => ItemName(group.First()), System.StringComparer.Ordinal)
                .Select(group => $"{ItemName(group.First())} x{group.Count()}"));

            Plugin.Log.LogInfo(
                $"[ShopPoolPlanner] Built unified upgrade pool: registered={candidates.Count}, " +
                $"eligible={eligible.Count}, selected={result.Count}/{target}, sameItemLimit={sameItemLimit}.");
            Plugin.Log.LogInfo($"[ShopPoolPlanner] Eligible upgrade weights: {candidateSummary}.");
            Plugin.Log.LogInfo($"[ShopPoolPlanner] Selected upgrade pool: {selectionSummary}.");
        }

        return result;
    }


    private static bool TryGetUpgradeBlockReason(Item item, int playerCount, out string reason)
    {
        reason = null;

        if (item == null)
        {
            reason = "missing item";
            return true;
        }

        if (item.disabled)
        {
            reason = "item is disabled";
            return true;
        }

        if (item.prefab == null || !item.prefab.IsValid())
        {
            reason = "missing prefab";
            return true;
        }

        int chance = Plugin.GetItemSpawnChance(item);
        if (chance <= 0)
        {
            reason = "configured chance is 0";
            return true;
        }

        int purchased = SemiFunc.StatGetItemsPurchased(item.name);
        if (item.maxAmountInShop <= purchased)
        {
            reason = $"shop amount limit reached ({purchased}/{item.maxAmountInShop})";
            return true;
        }

        if (item.maxPurchase &&
            StatsManager.instance.GetItemsUpgradesPurchasedTotal(item.name) >= item.maxPurchaseAmount)
        {
            reason = $"purchase limit reached ({item.maxPurchaseAmount})";
            return true;
        }

        if (item.minPlayerCount > playerCount)
        {
            reason = $"requires {item.minPlayerCount} players (current {playerCount})";
            return true;
        }

        return false;
    }


    private static Item SelectWeightedUpgrade(
        List<Item> candidates,
        Dictionary<string, int> selectedCounts,
        int sameItemLimit)
    {
        int totalWeight = 0;

        foreach (Item item in candidates)
        {
            if (!CanSelectUpgrade(item, selectedCounts, sameItemLimit))
                continue;

            totalWeight += System.Math.Max(1, Plugin.GetItemSpawnChance(item));
        }

        if (totalWeight <= 0)
            return null;

        int roll = Random.Range(0, totalWeight);
        foreach (Item item in candidates)
        {
            if (!CanSelectUpgrade(item, selectedCounts, sameItemLimit))
                continue;

            int weight = System.Math.Max(1, Plugin.GetItemSpawnChance(item));
            if (roll < weight)
                return item;

            roll -= weight;
        }

        return null;
    }


    private static bool CanSelectUpgrade(
        Item item,
        Dictionary<string, int> selectedCounts,
        int sameItemLimit)
    {
        string key = ItemKey(item);
        int selected = selectedCounts.TryGetValue(key, out int current) ? current : 0;
        if (selected >= sameItemLimit)
            return false;

        int purchased = SemiFunc.StatGetItemsPurchased(item.name);
        return purchased + selected < item.maxAmountInShop;
    }


    private static void RemoveControlledShelfItemsFromVanillaPools(ShopManager shopManager)
    {
        if (shopManager == null)
            return;

        int removed = 0;
        removed += RemoveControlledShelfItems(shopManager.potentialItems);
        removed += RemoveControlledShelfItems(shopManager.potentialItemConsumables);
        removed += RemoveControlledShelfItems(shopManager.potentialItemUpgrades);
        removed += RemoveControlledShelfItems(shopManager.potentialItemHealthPacks);

        if (removed > 0)
            if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[ShopPoolPlanner] Removed {removed} controlled shelf entries from vanilla pools; custom shelf handling owns them.");
    }


    private static int RemoveControlledShelfItems(List<Item> items)
    {
        if (items == null)
            return 0;

        int before = items.Count;
        items.RemoveAll(item =>
            item != null &&
            (item.itemType == SemiFunc.itemType.drone ||
             item.itemType == SemiFunc.itemType.power_crystal ||
             item.itemType == SemiFunc.itemType.grenade));

        return before - items.Count;
    }


    private static int GetCount(string key)
    {
        return Plugin.ItemCounts.TryGetValue(key, out var entry) ? entry.Value : 0;
    }


    private static void TopUpPotentialPoolsFromStats(ShopManager shopManager)
    {
        var itemDict = StatsManager.instance?.itemDictionary;
        if (shopManager == null || itemDict == null)
            return;

        var candidatesByCountKey = itemDict.Values
            .Where(IsAvailableTopUpCandidate)
            .GroupBy(ItemKey, System.StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(item => new
            {
                Item = item,
                HasKeys = ShopStockCatalog.TryGetConfigKeys(item, out string countKey, out _),
                CountKey = countKey
            })
            .Where(entry => entry.HasKeys &&
                            entry.CountKey != "Total Upgrades" &&
                            !IsCustomShelfOnlyCountKey(entry.CountKey) &&
                            GetCount(entry.CountKey) > 0)
            .GroupBy(entry => entry.CountKey);

        foreach (var categoryGroup in candidatesByCountKey)
        {
            string countKey = categoryGroup.Key;
            int target = GetCount(countKey);
            if (target <= 0)
                continue;

            List<Item> pool = GetPoolForItem(shopManager, categoryGroup.First().Item);
            if (pool == null)
                continue;

            int current = CountPoolCategory(pool, countKey);
            int missing = target - current;
            if (missing <= 0)
                continue;

            List<Item> candidates = categoryGroup
                .Select(entry => entry.Item)
                .OrderBy(WeightedRandomSortKey)
                .ToList();

            int added = AddCategoryCandidates(pool, candidates, countKey, missing);

            if (Plugin.DebugLogs.Value && added > 0)
                if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[ShopPoolPlanner] Topped up {countKey} pool: added={added}, before={current}, target={target}.");
        }
    }


    private static bool IsCustomShelfOnlyCountKey(string countKey)
    {
        return countKey == "Drones" || countKey == "Power Crystals" || countKey == "Grenades";
    }


    private static int AddCategoryCandidates(List<Item> pool, List<Item> candidates, string countKey, int missing)
    {
        int added = 0;
        int safetyLimit = candidates.Count * 8;

        for (int index = 0; added < missing && candidates.Count > 0 && index < safetyLimit; index++)
        {
            Item item = candidates[index % candidates.Count];
            if (!CanAddCandidate(pool, item, countKey))
                continue;

            pool.Add(item);
            added++;
        }

        return added;
    }


    private static bool CanAddCandidate(List<Item> pool, Item item, string countKey)
    {
        if (!IsAvailableTopUpCandidate(item))
            return false;

        if (!ShopStockCatalog.TryGetConfigKeys(item, out string itemCountKey, out string copyKey))
            return false;

        if (itemCountKey != countKey)
            return false;

        int purchased = SemiFunc.StatGetItemsPurchased(item.name);
        int sameItemInPool = pool.Count(existing => existing != null && existing.name == item.name);

        if (sameItemInPool + purchased >= item.maxAmountInShop)
            return false;

        if (copyKey != null && Plugin.SameItemCopies.TryGetValue(copyKey, out var copyEntry))
        {
            if (sameItemInPool >= copyEntry.Value)
                return false;
        }

        return true;
    }


    private static bool IsAvailableTopUpCandidate(Item item)
    {
        if (item == null || item.disabled || item.prefab == null || !item.prefab.IsValid())
            return false;

        if (Plugin.GetItemSpawnChance(item) <= 0)
            return false;

        int players = GameDirector.instance != null ? GameDirector.instance.PlayerList.Count : 1;
        if (item.minPlayerCount > players)
            return false;

        int purchased = SemiFunc.StatGetItemsPurchased(item.name);
        if (item.maxAmountInShop > 0 && purchased >= item.maxAmountInShop)
            return false;

        return !item.maxPurchase ||
               StatsManager.instance.GetItemsUpgradesPurchasedTotal(item.name) < item.maxPurchaseAmount;
    }


    private static int CountPoolCategory(List<Item> pool, string countKey)
    {
        return pool.Count(item =>
            item != null &&
            ShopStockCatalog.TryGetConfigKeys(item, out string itemCountKey, out _) &&
            itemCountKey == countKey);
    }


    private static List<Item> GetPoolForItem(ShopManager shopManager, Item item)
    {
        return item.itemType switch
        {
            SemiFunc.itemType.item_upgrade => shopManager.potentialItemUpgrades,
            SemiFunc.itemType.healthPack => shopManager.potentialItemHealthPacks,
            SemiFunc.itemType.power_crystal => shopManager.potentialItemConsumables,
            _ when item.itemSecretShopType == SemiFunc.itemSecretShopType.none => shopManager.potentialItems,
            _ => null
        };
    }


    private static List<Item> FilterListByConfiguredLimits(List<Item> items, string listName)
    {
        if (items == null || items.Count == 0)
            return items;

        int before = items.Count;
        List<Item> weightedItems = ApplyItemSpawnChanceOrder(items, listName);
        var countPerCategory = new Dictionary<string, int>();
        var countPerName = new Dictionary<string, int>();
        var result = new List<Item>(weightedItems.Count);

        foreach (var item in weightedItems)
        {
            if (item == null) continue;

            if (!ShopStockCatalog.TryGetConfigKeys(item, out string countKey, out string copyKey))
            {
                result.Add(item);
                continue;
            }

            int categoryTarget = GetCount(countKey);
            int categoryCount = countPerCategory.TryGetValue(countKey, out int currentCategoryCount)
                ? currentCategoryCount
                : 0;

            if (categoryTarget <= 0 || categoryCount >= categoryTarget)
            {
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[ShopPoolPlanner] Filtered out {ItemName(item)}: category {countKey} limit {categoryTarget} reached.");
                continue;
            }

            if (copyKey != null && Plugin.SameItemCopies.TryGetValue(copyKey, out var copyEntry))
            {
                string name = item.name;
                int nameCount = countPerName.TryGetValue(name, out int currentNameCount)
                    ? currentNameCount
                    : 0;

                if (nameCount >= copyEntry.Value)
                {
                    if (Plugin.DebugLogs.Value)
                        Plugin.Log.LogInfo($"[ShopPoolPlanner] Filtered out excess copy of {ItemName(item)} (limit {copyEntry.Value}).");
                    continue;
                }

                countPerName[name] = nameCount + 1;
            }

            countPerCategory[countKey] = categoryCount + 1;
            result.Add(item);
        }

        if (Plugin.DebugLogs.Value)
        {
            string counts = string.Join(", ", countPerCategory.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}={kvp.Value}/{GetCount(kvp.Key)}"));
            if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[ShopPoolPlanner] Filtered {listName} pool: {before} -> {result.Count}. Category counts: {counts}");
        }

        return result;
    }


    private static List<Item> ApplyItemSpawnChanceOrder(List<Item> items, string listName)
    {
        var weightedItems = new List<Item>(items.Count);
        int chanceDisabled = 0;

        foreach (Item item in items)
        {
            if (item == null)
            {
                continue;
            }

            if (item.disabled)
            {
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[ShopPoolPlanner] Filtered out {ItemName(item)} from {listName}: item is disabled.");
                continue;
            }

            int chance = Plugin.GetItemSpawnChance(item);
            if (chance <= 0)
            {
                chanceDisabled++;
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[ShopPoolPlanner] Filtered out {ItemName(item)} from {listName}: item spawn chance is 0.");
                continue;
            }

            weightedItems.Add(item);
        }

        if (chanceDisabled > 0)
        {
            if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[ShopPoolPlanner] Removed {chanceDisabled} {listName} pool entries because item spawn chance is 0.");
        }

        return weightedItems
            .OrderBy(WeightedRandomSortKey)
            .ToList();
    }


    private static double WeightedRandomSortKey(Item item)
    {
        int weight = System.Math.Max(1, Plugin.GetItemSpawnChance(item));
        double roll = System.Math.Max(UnityEngine.Random.value, 0.000001f);

        return -System.Math.Log(roll) / weight;
    }


    private static string ItemName(Item item)
    {
        if (item == null)
            return "<null>";

        return string.IsNullOrWhiteSpace(item.itemName) ? item.name : item.itemName;
    }


    private static string ItemKey(Item item)
    {
        if (item == null)
            return string.Empty;

        return string.IsNullOrWhiteSpace(item.name) ? ItemName(item) : item.name;
    }

}
