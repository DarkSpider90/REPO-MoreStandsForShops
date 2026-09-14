using System;
using System.Collections.Generic;
using System.Linq;
using MoreStandsForShops.Utilities;

namespace MoreStandsForShops.Shop;

internal static class ShopPoolPlanner
{
    internal static void PreparePools(ShopManager shopManager)
    {
        if (shopManager == null)
            return;

        List<Item> registeredItems = GetRegisteredItems();
        if (registeredItems.Count == 0)
        {
            shopManager.potentialItems = new List<Item>();
            shopManager.potentialItemConsumables = new List<Item>();
            shopManager.potentialItemUpgrades = new List<Item>();
            shopManager.potentialItemHealthPacks = new List<Item>();
            return;
        }

        // Vanilla builds its pools from maxAmountInShop minus the number bought in
        // previous shops. The mod's Item Counts are current-shop targets, so rebuild
        // the visible pools from the complete registered catalogue instead of trying
        // to repair an already truncated vanilla list.
        shopManager.potentialItemUpgrades = BuildConfiguredPool(registeredItems, "Total Upgrades");
        // Health packs are selected directly by the dedicated health/grenade shelf.
        // Keeping a second vanilla health pool would let the same category leak back
        // into arbitrary healthPack volumes and defeat the shelf's spacing rules.
        shopManager.potentialItemHealthPacks = new List<Item>();
        shopManager.potentialItemConsumables = new List<Item>();

        var standardPool = new List<Item>();
        foreach (string countKey in ShopStockCatalog.StandardBudgetCountKeys)
            standardPool.AddRange(BuildConfiguredPool(registeredItems, countKey));

        standardPool.Shuffle();
        shopManager.potentialItems = standardPool;

        if (Plugin.DebugLogs.Value)
        {
            Plugin.Log.LogInfo(
                $"[ShopPoolPlanner] Rebuilt complete configured pools: " +
                $"registered={registeredItems.Count}, standard={shopManager.potentialItems.Count}, " +
                $"upgrades={shopManager.potentialItemUpgrades.Count}, " +
                $"health={GetCount("Health Packs")} (dedicated shelf); " +
                "drones, crystals, grenades and health packs are selected directly by their shelves.");
        }
    }

    internal static void FillAdaptiveTableCapacity(ShopManager shopManager)
    {
        if (shopManager?.potentialItems == null || shopManager.itemVolumes == null ||
            !Plugin.EnableVanillaShelfTableRewrite.Value ||
            !Plugin.DisableShopPoolLimit.Value)
        {
            return;
        }

        int adaptiveGroupCount = shopManager.itemVolumes
            .Where(volume => volume != null)
            .Select(volume => volume.GetComponent<MoreStandsMultiSizeVolume>())
            .Where(marker => marker != null && !string.IsNullOrEmpty(marker.GroupId))
            .Select(marker => marker.GroupId)
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (adaptiveGroupCount <= 0)
            return;

        int configuredTableTarget = ShopStockCatalog.TableBudgetCountKeys.Sum(GetCount);
        int target = Math.Min(adaptiveGroupCount, configuredTableTarget);
        int selectedTableCount = shopManager.potentialItems.Count(IsAdaptiveTableItem);
        int missing = target - selectedTableCount;
        if (missing <= 0)
            return;

        List<Item> eligibleFallbacks = GetRegisteredItems()
            .Where(IsAdaptiveTableItem)
            .Where(item =>
                ShopStockCatalog.TryGetConfigKeys(item, out string countKey, out _) &&
                ShopStockCatalog.TableBudgetCountKeys.Contains(countKey) &&
                GetCount(countKey) > 0 &&
                !TryGetBlockReason(item, out _))
            .ToList();

        var selectedCounts = shopManager.potentialItems
            .Where(IsAdaptiveTableItem)
            .GroupBy(ItemIdentity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        int added = 0;
        while (added < missing)
        {
            Item selected = SelectWeightedCandidate(
                eligibleFallbacks,
                selectedCounts,
                Math.Max(1, configuredTableTarget));
            if (selected == null)
                break;

            string identity = ItemIdentity(selected);
            selectedCounts[identity] = selectedCounts.TryGetValue(identity, out int count)
                ? count + 1
                : 1;
            shopManager.potentialItems.Add(selected);
            added++;
        }

        if (added > 0)
            shopManager.potentialItems.Shuffle();

        string message =
            $"[ShopPoolPlanner] Adaptive table fallback filled {added}/{missing} missing configured " +
            $"position(s): selectedTable={selectedTableCount + added}/{target}, " +
            $"physicalSlots={adaptiveGroupCount}.";

        if (added < missing)
            Plugin.Log.LogWarning(message + " Eligible items reached their Same Item Copies limits.");
        else if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo(message);
    }

    private static List<Item> GetRegisteredItems()
    {
        var itemDictionary = StatsManager.instance?.itemDictionary;
        if (itemDictionary == null)
            return new List<Item>();

        return itemDictionary.Values
            .Where(item => item != null)
            .GroupBy(ItemIdentity, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(ItemIdentity, StringComparer.Ordinal)
            .ToList();
    }

    private static List<Item> BuildConfiguredPool(List<Item> registeredItems, string countKey)
    {
        int target = GetCount(countKey);
        var result = new List<Item>(Math.Max(0, target));
        if (target <= 0)
            return result;

        List<Item> categoryItems = registeredItems
            .Where(item =>
                ShopStockCatalog.TryGetConfigKeys(item, out string itemCountKey, out _) &&
                string.Equals(itemCountKey, countKey, StringComparison.Ordinal))
            .ToList();

        var eligible = new List<Item>(categoryItems.Count);
        foreach (Item item in categoryItems)
        {
            if (TryGetBlockReason(item, out string reason))
            {
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[ShopPoolPlanner] Candidate blocked: category={countKey}, item={ItemName(item)}, reason={reason}.");
                continue;
            }

            eligible.Add(item);
        }

        var selectedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        while (result.Count < target)
        {
            Item selected = SelectWeightedCandidate(eligible, selectedCounts, target);
            if (selected == null)
                break;

            string identity = ItemIdentity(selected);
            selectedCounts[identity] = selectedCounts.TryGetValue(identity, out int count) ? count + 1 : 1;
            result.Add(selected);
        }

        result.Shuffle();

        if (Plugin.DebugLogs.Value)
        {
            string selection = string.Join(", ", result
                .GroupBy(ItemIdentity, StringComparer.Ordinal)
                .OrderBy(group => ItemName(group.First()), StringComparer.Ordinal)
                .Select(group => $"{ItemName(group.First())} x{group.Count()}"));

            Plugin.Log.LogInfo(
                $"[ShopPoolPlanner] Built category pool: category={countKey}, " +
                $"registered={categoryItems.Count}, eligible={eligible.Count}, selected={result.Count}/{target}. " +
                $"Items: {selection}.");
        }

        if (result.Count < target)
        {
            Plugin.Log.LogWarning(
                $"[ShopPoolPlanner] Category {countKey} produced only {result.Count}/{target} item(s). " +
                "Every eligible item reached its configured Same Item Copies limit, or no eligible item is registered.");
        }

        return result;
    }

    private static Item SelectWeightedCandidate(
        List<Item> candidates,
        Dictionary<string, int> selectedCounts,
        int categoryTarget)
    {
        List<Item> available = candidates
            .Where(item => SelectedCount(item, selectedCounts) < CopyLimit(item, categoryTarget))
            .ToList();

        if (available.Count == 0)
            return null;

        int totalWeight = available.Sum(item => Math.Max(1, Plugin.GetItemSpawnChance(item)));
        int roll = UnityEngine.Random.Range(0, totalWeight);
        foreach (Item item in available)
        {
            int weight = Math.Max(1, Plugin.GetItemSpawnChance(item));
            if (roll < weight)
                return item;
            roll -= weight;
        }

        return available[available.Count - 1];
    }

    private static bool TryGetBlockReason(Item item, out string reason)
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

        if (Plugin.GetItemSpawnChance(item) <= 0)
        {
            reason = "configured chance is 0";
            return true;
        }

        int playerCount = GameDirector.instance != null ? GameDirector.instance.PlayerList.Count : 1;
        if (item.minPlayerCount > playerCount)
        {
            reason = $"requires {item.minPlayerCount} players (current {playerCount})";
            return true;
        }

        if (item.maxPurchase &&
            StatsManager.instance.GetItemsUpgradesPurchasedTotal(item.name) >= item.maxPurchaseAmount)
        {
            reason = $"purchase limit reached ({item.maxPurchaseAmount})";
            return true;
        }

        if (item.itemSecretShopType != SemiFunc.itemSecretShopType.none)
        {
            reason = "secret-shop item";
            return true;
        }

        return false;
    }

    private static bool IsAdaptiveTableItem(Item item)
    {
        if (item == null)
            return false;

        return item.itemVolume == SemiFunc.itemVolume.small ||
               item.itemVolume == SemiFunc.itemVolume.medium ||
               item.itemVolume == SemiFunc.itemVolume.large ||
               item.itemVolume == SemiFunc.itemVolume.large_high;
    }

    private static int SelectedCount(Item item, Dictionary<string, int> selectedCounts)
    {
        return selectedCounts.TryGetValue(ItemIdentity(item), out int count) ? count : 0;
    }

    private static int CopyLimit(Item item, int categoryTarget)
    {
        if (ShopStockCatalog.TryGetConfigKeys(item, out _, out string copyKey) &&
            copyKey != null &&
            Plugin.SameItemCopies.TryGetValue(copyKey, out var copyEntry))
        {
            return Math.Max(0, copyEntry.Value);
        }

        return categoryTarget;
    }

    private static int GetCount(string key)
    {
        return Plugin.ItemCounts.TryGetValue(key, out var entry) ? Math.Max(0, entry.Value) : 0;
    }

    private static string ItemName(Item item)
    {
        if (item == null)
            return "<null>";

        return string.IsNullOrWhiteSpace(item.itemName) ? item.name : item.itemName;
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
