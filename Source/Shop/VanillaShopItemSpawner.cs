using System;
using System.Collections.Generic;
using System.Reflection;

namespace MoreStandsForShops.Shop;

internal static class VanillaShopItemSpawner
{
    private static readonly MethodInfo SpawnShopItemMethod = typeof(PunManager).GetMethod(
        "SpawnShopItem",
        BindingFlags.Instance | BindingFlags.NonPublic,
        null,
        new[] { typeof(ItemVolume), typeof(List<Item>), typeof(int).MakeByRefType(), typeof(bool) },
        null);

    internal static bool IsCallingVanilla { get; private set; }

    internal static bool TrySpawnSingle(PunManager punManager, ItemVolume itemVolume, Item item, bool isSecret)
    {
        if (punManager == null || itemVolume == null || item == null || SpawnShopItemMethod == null)
            return false;

        List<Item> singleItemPool = new() { item };
        int tempSpawnCount = 0;
        object[] args = { itemVolume, singleItemPool, tempSpawnCount, isSecret };

        try
        {
            IsCallingVanilla = true;
            return (bool)SpawnShopItemMethod.Invoke(punManager, args);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[VanillaShopItemSpawner] Failed to call vanilla SpawnShopItem for {ItemName(item)}: {ex}");
            return false;
        }
        finally
        {
            IsCallingVanilla = false;
        }
    }

    private static string ItemName(Item item)
    {
        if (item == null)
            return "<null>";

        return !string.IsNullOrWhiteSpace(item.itemName) ? item.itemName : item.name;
    }
}