using System.Collections.Generic;
using System.Linq;
using MoreStandsForShops.Shop;
using MoreStandsForShops.Spawners;
using MoreStandsForShops.Utilities;

namespace MoreStandsForShops.Patches;

internal static class ShopSpawnFlow
{
    internal static bool IsCallingVanilla => VanillaShopItemSpawner.IsCallingVanilla;

    internal static void ResetForShop()
    {
        ShopSceneCache.Rebuild();
        ShelfSpawnController.ResetForShop();
        MultiSizeSlotController.ResetForShop();
        ShopTableItemPlacementController.ResetForShop();
        UpgradeStandSpawner.ResetForShop();
    }


    internal static void PrepareBeforeVanillaPopulate(PunManager punManager)
    {
        ShopPoolPlanner.FillAdaptiveTableCapacity(ShopManager.instance);
        ShelfSpawnController.PrepareVolumesForPopulate(punManager);
        ShopBudgetPlanner.ApplyFillAllShopSlotBudget(ShopManager.instance);
    }


    internal static bool TryHandleSpawnShopItem(
        PunManager punManager,
        ItemVolume itemVolume,
        List<Item> itemList,
        ref int spawnCount,
        bool isSecret,
        out bool result)
    {
        if (ShelfSpawnController.TryHandleSpawnShopItem(
                punManager,
                itemVolume,
                itemList,
                ref spawnCount,
                isSecret,
                out result))
        {
            return true;
        }

        if (MultiSizeSlotController.TrySkipHandledSlot(itemVolume, out result))
        {
            return true;
        }

        return false;
    }


    internal static void NoteSpawnShopItemResult(ItemVolume itemVolume, bool spawned)
    {
        MultiSizeSlotController.NoteSpawnResult(itemVolume, spawned);
        ShopTableItemPlacementController.NoteSpawn(itemVolume, spawned);
    }


    internal static void LogPostPopulateResults()
    {
        ShopManager shopManager = ShopManager.instance;
        if (shopManager == null)
            return;

        LogRemainingPool("standard", shopManager.potentialItems);
        LogRemainingPool("upgrades", shopManager.potentialItemUpgrades);
        LogRemainingPool("health", shopManager.potentialItemHealthPacks);
    }


    private static void LogRemainingPool(string poolName, List<Item> pool)
    {
        int remaining = pool?.Count ?? 0;
        if (remaining == 0)
        {
            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"[ShopSpawnAudit] {poolName} pool fully placed.");
            return;
        }

        string byVolume = string.Join(", ", pool
            .Where(item => item != null)
            .GroupBy(item => item.itemVolume)
            .OrderBy(group => (int)group.Key)
            .Select(group => $"{group.Key}={group.Count()}"));

        Plugin.Log.LogWarning(
            $"[ShopSpawnAudit] {remaining} configured {poolName} item(s) had no compatible free display place. " +
            $"Remaining by volume: {byVolume}.");
    }

}
