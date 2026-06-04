using System.Collections.Generic;
using HarmonyLib;
using MoreStandsForShops.Spawners;
using MoreStandsForShops.Utilities;

namespace MoreStandsForShops.Patches;

[HarmonyPatch(typeof(PunManager))]
internal static class PunManagerPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(PunManager.ShopPopulateItemVolumes))]
    private static void ShopPopulateItemVolumesPrefix(PunManager __instance)
    {
        if (!Plugin.EnableMod.Value || !SemiFunc.IsMasterClientOrSingleplayer() || !SemiFunc.RunIsShop())
        {
            return;
        }

        ShopSpawnFlow.PrepareBeforeVanillaPopulate(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(PunManager.ShopPopulateItemVolumes))]
    private static void ShopPopulateItemVolumesPostfix()
    {
        if (!Plugin.EnableMod.Value || !SemiFunc.IsMasterClientOrSingleplayer() || !SemiFunc.RunIsShop())
        {
            return;
        }

        UpgradeStandSpawner.SchedulePostPopulateCartOverlapRecheck();
    }


    [HarmonyPrefix]
    [HarmonyPatch("SpawnShopItem")]
    private static bool SpawnShopItemPrefix(PunManager __instance, ItemVolume itemVolume, List<Item> itemList, ref int spawnCount, bool isSecret, ref bool __result)
    {
        if (ShopSpawnFlow.IsCallingVanilla)
        {
            return true;
        }

        if (!Plugin.EnableMod.Value || !SemiFunc.IsMasterClientOrSingleplayer() || !SemiFunc.RunIsShop())
        {
            return true;
        }

        if (ShopSpawnFlow.TryHandleSpawnShopItem(__instance, itemVolume, itemList, ref spawnCount, isSecret, out bool result))
        {
            __result = result;
            return false;
        }

        LogTableMultiSizeSpawnCandidate(itemVolume, itemList);

        return true;
        
    }


    [HarmonyPostfix]
    [HarmonyPatch("SpawnShopItem")]
    private static void SpawnShopItemPostfix(ItemVolume itemVolume, bool __result)
    {
        if (ShopSpawnFlow.IsCallingVanilla || !Plugin.EnableMod.Value || !SemiFunc.IsMasterClientOrSingleplayer() || !SemiFunc.RunIsShop())
        {
            return;
        }

        ShopSpawnFlow.NoteSpawnShopItemResult(itemVolume, __result);
    }
    
    private static void LogTableMultiSizeSpawnCandidate(ItemVolume itemVolume, List<Item> itemList)
    {
        if (!Plugin.DebugLogs.Value || itemVolume == null || itemList == null)
            return;

        MoreStandsMultiSizeVolume marker = itemVolume.GetComponent<MoreStandsMultiSizeVolume>();
        if (marker == null || string.IsNullOrEmpty(marker.GroupId))
            return;

        Item selected = PredictVanillaSelectedItem(itemVolume, itemList);
        if (selected == null)
        {
            Plugin.Log.LogInfo(
                $"[MultiSizeSlot] Vanilla candidate missing: group={marker.GroupId}, " +
                $"slotVolume={itemVolume.itemVolume}, local={itemVolume.transform.localPosition}, " +
                $"world={itemVolume.transform.position}.");
            return;
        }

        Plugin.Log.LogInfo(
            $"[MultiSizeSlot] Vanilla candidate: group={marker.GroupId}, " +
            $"slotVolume={itemVolume.itemVolume}, item={ItemName(selected)}, " +
            $"itemVolume={selected.itemVolume}, local={itemVolume.transform.localPosition}, " +
            $"world={itemVolume.transform.position}, yaw={itemVolume.transform.eulerAngles.y:F1}.");
    }
    

    private static Item PredictVanillaSelectedItem(ItemVolume itemVolume, List<Item> itemList)
    {
        for (int i = itemList.Count - 1; i >= 0; i--)
        {
            Item item = itemList[i];
            if (item != null && item.itemVolume == itemVolume.itemVolume)
                return item;
        }

        return null;
    }
    

    private static string ItemName(Item item)
    {
        if (item == null)
            return "<null>";

        return string.IsNullOrWhiteSpace(item.itemName) ? item.name : item.itemName;
    }
    
}
