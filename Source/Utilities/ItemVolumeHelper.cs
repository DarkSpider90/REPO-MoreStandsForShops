using UnityEngine;

namespace MoreStandsForShops.Utilities;

/// <summary>
/// Helper methods for working with ItemVolume components.
/// </summary>
public static class ItemVolumeHelper
{
    private static readonly Vector3[] CrystalSlotPositions =
    {
        new(-0.72f, 1.68f, 0.50f),
        new(-0.36f, 1.68f, 0.50f),
        new(0.00f, 1.68f, 0.50f),
        new(0.36f, 1.68f, 0.50f),
        new(0.72f, 1.68f, 0.50f)
    };

    private static readonly Vector3[] DroneSlotPositions =
    {
        new(-0.77f, 1.18f, 0.50f),
        new(-0.55f, 1.18f, 0.50f),
        new(-0.33f, 1.18f, 0.50f),
        new(-0.11f, 1.18f, 0.50f),
        new(0.11f, 1.18f, 0.50f),
        new(0.33f, 1.18f, 0.50f),
        new(0.55f, 1.18f, 0.50f),
        new(0.77f, 1.18f, 0.50f)
    };

    /// <summary>
    /// Assign zones to existing volumes, or create explicit passive slots when the visual shelf has no ItemVolume children.
    /// </summary>
    public static void AssignVolumesForDroneCrystalStand(GameObject stand)
    {
        var volumes = stand.GetComponentsInChildren<ItemVolume>(true);
        if (volumes.Length == 0)
        {
            CreateDroneCrystalVolumes(stand);
            return;
        }

        System.Array.Sort(volumes, (a, b) => a.transform.position.y.CompareTo(b.transform.position.y));

        int mid = volumes.Length / 2;
        for (int i = 0; i < volumes.Length; i++)
        {
            bool isDroneSlot = i < mid;
            volumes[i].itemVolume = isDroneSlot
                ? SemiFunc.itemVolume.small
                : SemiFunc.itemVolume.power_crystal;
            volumes[i].itemSecretShopType = SemiFunc.itemSecretShopType.none;
            MoreStandsShelfVolume marker = volumes[i].gameObject.GetComponent<MoreStandsShelfVolume>() ??
                                          volumes[i].gameObject.AddComponent<MoreStandsShelfVolume>();
            marker.Zone = isDroneSlot ? MoreStandsShelfZone.Drone : MoreStandsShelfZone.Crystal;
            marker.Handled = false;

            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"[ItemVolumeHelper] Volume {volumes[i].name} (Y={volumes[i].transform.position.y:F3}) -> {(i < mid ? "small" : "power_crystal")}");
        }

        if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[ItemVolumeHelper] Assigned {mid} lower volume(s) to 'small' (drones) and {volumes.Length - mid} upper volume(s) to 'power_crystal'.");
    }

    private static void CreateDroneCrystalVolumes(GameObject stand)
    {
        if (stand == null)
        {
            return;
        }

        for (int i = 0; i < CrystalSlotPositions.Length; i++)
        {
            CreateVolumeSlot(
                stand.transform,
                $"MoreStandsForShops Crystal Slot {i + 1:00}",
                SemiFunc.itemVolume.power_crystal,
                MoreStandsShelfZone.Crystal,
                CrystalSlotPositions[i]);
        }

        for (int i = 0; i < DroneSlotPositions.Length; i++)
        {
            CreateVolumeSlot(
                stand.transform,
                $"MoreStandsForShops Drone Slot {i + 1:00}",
                SemiFunc.itemVolume.small,
                MoreStandsShelfZone.Drone,
                DroneSlotPositions[i]);
        }

        if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[ItemVolumeHelper] Created {CrystalSlotPositions.Length} crystal ItemVolume(s) and {DroneSlotPositions.Length} drone ItemVolume(s) on drone/crystal stand.");
    }

    private static ItemVolume CreateVolumeSlot(Transform parent, string name, SemiFunc.itemVolume itemVolume, MoreStandsShelfZone zone, Vector3 localPosition)
    {
        GameObject slot = new(name);
        slot.transform.SetParent(parent, false);
        slot.transform.localPosition = localPosition;
        slot.transform.localRotation = Quaternion.identity;

        ItemVolume volume = slot.AddComponent<ItemVolume>();
        volume.itemVolume = itemVolume;
        volume.itemSecretShopType = SemiFunc.itemSecretShopType.none;

        MoreStandsShelfVolume marker = slot.AddComponent<MoreStandsShelfVolume>();
        marker.Zone = zone;
        marker.Handled = false;

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[ItemVolumeHelper] Created {name} zone={itemVolume} local={localPosition}.");

        return volume;
    }
}
