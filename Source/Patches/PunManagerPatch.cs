using System.Collections.Generic;
using HarmonyLib;

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
}
