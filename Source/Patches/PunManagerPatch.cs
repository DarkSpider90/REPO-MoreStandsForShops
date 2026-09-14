using System;
using System.Collections.Generic;
using HarmonyLib;
using MoreStandsForShops.Shop;
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

        RunCustomizationStep(
            "prepare custom shop population",
            () => ShopSpawnFlow.PrepareBeforeVanillaPopulate(__instance));
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(PunManager.ShopPopulateItemVolumes))]
    private static void ShopPopulateItemVolumesPostfix()
    {
        if (!Plugin.EnableMod.Value || !SemiFunc.IsMasterClientOrSingleplayer() || !SemiFunc.RunIsShop())
        {
            return;
        }

        RunCustomizationStep("audit populated shop", ShopSpawnFlow.LogPostPopulateResults);
        RunCustomizationStep("finalize table stabilization", ShopTableItemPlacementController.FinalizePopulation);
        RunCustomizationStep(
            "schedule cart overlap recheck",
            UpgradeStandSpawner.SchedulePostPopulateCartOverlapRecheck);
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

        try
        {
            if (ShopSpawnFlow.TryHandleSpawnShopItem(__instance, itemVolume, itemList, ref spawnCount, isSecret, out bool result))
            {
                __result = result;
                return false;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"[PunManagerPatch] Custom SpawnShopItem handling failed; using vanilla for this slot. {ex}");
            return true;
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

        RunCustomizationStep(
            "record adaptive table spawn",
            () => ShopSpawnFlow.NoteSpawnShopItemResult(itemVolume, __result));
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


    private static void RunCustomizationStep(string stepName, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"[PunManagerPatch] Failed to {stepName}; continuing vanilla shop flow. {ex}");
        }
    }
    
}
