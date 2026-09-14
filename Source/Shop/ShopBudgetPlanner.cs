using System.Linq;

namespace MoreStandsForShops.Shop;

internal static class ShopBudgetPlanner
{
    internal static void ApplyConfiguredBudgets(ShopManager shopManager)
    {
        if (shopManager == null)
            return;

        int standard = ShopStockCatalog.StandardBudgetCountKeys.Sum(GetCount);

        shopManager.itemSpawnTargetAmount = standard;
        shopManager.itemConsumablesAmount = 0;
        shopManager.itemUpgradesAmount = GetCount("Total Upgrades");
        // Health packs are populated by ShelfSpawnController on their dedicated
        // authored shelf, outside the vanilla shuffled ItemVolume pass.
        shopManager.itemHealthPacksAmount = 0;

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[ShopBudgetPlanner] Shop budgets set: standard={shopManager.itemSpawnTargetAmount}, vanillaCrystals={shopManager.itemConsumablesAmount}, customDrones={GetCount("Drones")}, customCrystals={GetCount("Power Crystals")}, upgrades={shopManager.itemUpgradesAmount}, customHealth={GetCount("Health Packs")}.");
    }


    internal static void ApplyFillAllShopSlotBudget(ShopManager shopManager)
    {
        if (shopManager == null || !Plugin.EnableMod.Value || !Plugin.DisableShopPoolLimit.Value)
            return;

        int poolCount = shopManager.potentialItems?.Count ?? 0;
        int activeVolumes = shopManager.itemVolumes?.Count ?? 0;
        int oldTarget = shopManager.itemSpawnTargetAmount;

        // A spawn budget is a number of items, not a number of every kind of scene
        // volume (upgrade, health and shelf volumes are counted separately). Keeping
        // it at least as large as the complete standard pool guarantees that vanilla
        // attempts every configured entry without inflating the standard counter.
        shopManager.itemSpawnTargetAmount = System.Math.Max(oldTarget, poolCount);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[ShopBudgetPlanner] Shop pool limit disabled: standard target {oldTarget}->{shopManager.itemSpawnTargetAmount}, completeStandardPool={poolCount}, allActiveVolumes={activeVolumes}.");
    }


    private static int GetCount(string key)
    {
        return Plugin.ItemCounts.TryGetValue(key, out var entry) ? entry.Value : 0;
    }
}
