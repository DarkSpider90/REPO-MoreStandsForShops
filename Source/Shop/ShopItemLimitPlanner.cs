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

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo(
                "[ShopItemLimitPlanner] Registered per-item settings without changing vanilla item assets; " +
                "configured current-shop counts are applied while rebuilding pools.");
    }
}
