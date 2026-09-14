using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

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

    internal static bool TrySpawnSingle(
        PunManager punManager,
        ItemVolume itemVolume,
        Item item,
        bool isSecret,
        float? worldEulerXOverride = null)
    {
        if (punManager == null || itemVolume == null || item == null || SpawnShopItemMethod == null)
            return false;

        List<Item> singleItemPool = new() { item };
        int tempSpawnCount = 0;
        object[] args = { itemVolume, singleItemPool, tempSpawnCount, isSecret };
        Quaternion originalSpawnRotation = item.spawnRotationOffset;

        try
        {
            if (worldEulerXOverride.HasValue)
            {
                Quaternion originalWorldRotation = itemVolume.transform.rotation * originalSpawnRotation;
                Vector3 worldEuler = originalWorldRotation.eulerAngles;
                worldEuler.x = worldEulerXOverride.Value;
                Quaternion desiredWorldRotation = Quaternion.Euler(worldEuler);
                item.spawnRotationOffset = Quaternion.Inverse(itemVolume.transform.rotation) * desiredWorldRotation;

                if (Plugin.DebugLogs.Value)
                {
                    Vector3 originalEuler = originalWorldRotation.eulerAngles;
                    Vector3 appliedEuler =
                        (itemVolume.transform.rotation * item.spawnRotationOffset).eulerAngles;
                    Plugin.Log.LogInfo(
                        $"[VanillaShopItemSpawner] Forced world X rotation for {ItemName(item)}: " +
                        $"original=({originalEuler.x:F1}, {originalEuler.y:F1}, {originalEuler.z:F1}), " +
                        $"applied=({appliedEuler.x:F1}, {appliedEuler.y:F1}, {appliedEuler.z:F1}).");
                }
            }

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
            item.spawnRotationOffset = originalSpawnRotation;
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
