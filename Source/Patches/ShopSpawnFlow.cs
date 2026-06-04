using System.Collections.Generic;
using MoreStandsForShops.Shop;
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
    }


    internal static void PrepareBeforeVanillaPopulate(PunManager punManager)
    {
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
    }

}
