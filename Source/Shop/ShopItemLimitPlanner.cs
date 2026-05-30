namespace MoreStandsForShops.Shop;

internal static class ShopItemLimitPlanner
{
    // Флаг: конфиги для предметов уже были зарегистрированы в эту сессию
    private static bool _spawnChanceConfigsEnsured;

    // Сброс при выходе в меню — чтобы при следующей сессии снова подхватить новые предметы
    internal static void ResetForSession()
    {
        _spawnChanceConfigsEnsured = false;
    }

    internal static void ApplyConfiguredItemLimits()
    {
        var itemDict = StatsManager.instance?.itemDictionary;
        if (itemDict == null)
            return;

        // EnsureItemSpawnChanceConfigs вызывается только один раз за сессию,
        // а не при каждом вызове ApplyConfiguredItemLimits
        if (!_spawnChanceConfigsEnsured)
        {
            Plugin.EnsureItemSpawnChanceConfigs(itemDict.Values);
            _spawnChanceConfigsEnsured = true;
        }

        foreach (var item in itemDict.Values)
        {
            if (item == null)
                continue;

            if (!ShopStockCatalog.TryGetConfigKeys(item, out string countKey, out _))
                continue;

            if (!Plugin.ItemCounts.TryGetValue(countKey, out var countEntry))
                continue;

            int newMax = countEntry.Value;
            item.maxAmountInShop = newMax;
            item.maxAmount = newMax;
            item.maxPurchase = newMax > 0;
            item.maxPurchaseAmount = newMax;

            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"[ShopItemLimitPlanner] Set {item.itemName} maxAmountInShop to {newMax}");
        }

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo("[ShopItemLimitPlanner] Applied item count overrides.");
    }
}