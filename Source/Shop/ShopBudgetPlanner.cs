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
        shopManager.itemHealthPacksAmount = GetCount("Health Packs");

        Plugin.Log.LogInfo($"[ShopBudgetPlanner] Shop budgets set: standard={shopManager.itemSpawnTargetAmount}, vanillaCrystals={shopManager.itemConsumablesAmount}, customDrones={GetCount("Drones")}, customCrystals={GetCount("Power Crystals")}, upgrades={shopManager.itemUpgradesAmount}, health={shopManager.itemHealthPacksAmount}.");
    }


    internal static void ApplyFillAllShopSlotBudget(ShopManager shopManager)
    {
        if (shopManager == null || !Plugin.EnableMod.Value || !Plugin.DisableShopPoolLimit.Value)
            return;

        int poolCount = shopManager.potentialItems?.Count ?? 0;
        int activeVolumes = shopManager.itemVolumes?.Count ?? 0;
        int oldTarget = shopManager.itemSpawnTargetAmount;

        shopManager.itemSpawnTargetAmount = System.Math.Max(oldTarget, System.Math.Max(poolCount, activeVolumes));

        Plugin.Log.LogInfo($"[ShopBudgetPlanner] Shop pool limit disabled: standard target {oldTarget}->{shopManager.itemSpawnTargetAmount}, standardPool={poolCount}, activeVolumes={activeVolumes}.");
    }


    private static int GetCount(string key)
    {
        return Plugin.ItemCounts.TryGetValue(key, out var entry) ? entry.Value : 0;
    }
}