using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;

namespace MoreStandsForShops.Network;

internal static class ShopLayoutSync
{
    private const string UpgradeActiveKey = "MSFS.Upgrade.Active";
    private const string UpgradeVariantKey = "MSFS.Upgrade.Variant";
    private const string UpgradePositionKey = "MSFS.Upgrade.Position";
    private const string UpgradeRotationKey = "MSFS.Upgrade.Rotation";
    private const string UpgradeParentKey = "MSFS.Upgrade.Parent";
    private const string UpgradeDisabledKey = "MSFS.Upgrade.Disabled";

    private const string ShelfActiveKey = "MSFS.Shelf.Active";
    private const string ShelfDroneSlotCountKey = "MSFS.Shelf.DroneSlotCount";
    private const string ShelfCrystalSlotCountKey = "MSFS.Shelf.CrystalSlotCount";
    private const string ShelfDisabledKey = "MSFS.Shelf.Disabled";

    private const string LayoutReadyKey = "MSFS.Layout.Ready";
    private const string LayoutSequenceKey = "MSFS.Layout.Sequence";
    private const string UpgradeSlotCountKey = "MSFS.Upgrade.SlotCount";

    internal static void Clear()
    {
        if (!CanWrite())
            return;

        var props = new Hashtable
        {
            { UpgradeActiveKey, false },
            { UpgradeVariantKey, string.Empty },
            { UpgradePositionKey, Vector3.zero },
            { UpgradeRotationKey, Quaternion.identity },
            { UpgradeParentKey, string.Empty },
            { UpgradeDisabledKey, string.Empty },
            { UpgradeSlotCountKey, 0 },

            { ShelfActiveKey, false },
            { ShelfDroneSlotCountKey, 0 },
            { ShelfCrystalSlotCountKey, 0 },
            { ShelfDisabledKey, string.Empty },

            { LayoutReadyKey, false },
            { LayoutSequenceKey, NextSequence() },
        };

        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        
        Plugin.Log.LogInfo($"[ShopLayoutSync] Cleared shop layout. sequence={ReadInt(props, LayoutSequenceKey)}.");
    }


    internal static void MarkReady()
    {
        if (!CanWrite())
            return;

        int sequence = NextSequence();

        var props = new Hashtable
        {
            { LayoutReadyKey, true },
            { LayoutSequenceKey, sequence }
        };

        PhotonNetwork.CurrentRoom.SetCustomProperties(props);

        Plugin.Log.LogInfo($"[ShopLayoutSync] Marked shop layout ready. sequence={sequence}.");
    }


    internal static void SetUpgradeStand(UpgradeStandLayout layout)
    {
        if (!CanWrite() || layout == null)
            return;

        var props = new Hashtable
        {
            { UpgradeActiveKey, layout.Enabled },
            { UpgradeSlotCountKey, layout.UpgradeSlotCount },
            { UpgradeVariantKey, layout.VariantId ?? string.Empty },
            { UpgradePositionKey, layout.Position },
            { UpgradeRotationKey, layout.Rotation },
            { UpgradeParentKey, layout.ParentPath ?? string.Empty },
            { UpgradeDisabledKey, JoinPaths(layout.DisabledPaths) }
        };

        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        
        Plugin.Log.LogInfo($"[ShopLayoutSync] Stored upgrade stand layout: enabled={layout.Enabled}, variant={layout.VariantId}, slots={layout.UpgradeSlotCount}, disabledPaths={layout.DisabledPaths?.Length ?? 0}.");
    }


    internal static bool TryGetUpgradeStand(out UpgradeStandLayout layout)
    {

        layout = null;

        if (!CanRead())
            return false;

        Hashtable props = PhotonNetwork.CurrentRoom.CustomProperties;

        if (!ReadBool(props, UpgradeActiveKey))
            return false;

        layout = new UpgradeStandLayout
        {
            Enabled = ReadBool(props, UpgradeActiveKey),
            UpgradeSlotCount = ReadInt(props, UpgradeSlotCountKey),
            VariantId = ReadString(props, UpgradeVariantKey),
            Position = ReadVector3(props, UpgradePositionKey),
            Rotation = ReadQuaternion(props, UpgradeRotationKey),
            ParentPath = ReadString(props, UpgradeParentKey),
            DisabledPaths = SplitPaths(ReadString(props, UpgradeDisabledKey))
        };

        return true;
    }


    internal static void SetDroneCrystalShelf(DroneCrystalShelfLayout layout)
    {
        if (!CanWrite() || layout == null)
            return;

        var props = new Hashtable
        {
            { ShelfActiveKey, layout.Enabled },
            { ShelfDroneSlotCountKey, layout.DroneSlotCount },
            { ShelfCrystalSlotCountKey, layout.CrystalSlotCount },
            { ShelfDisabledKey, JoinPaths(layout.DisabledPaths) }
        };

        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        
        Plugin.Log.LogInfo($"[ShopLayoutSync] Stored drone/crystal shelf layout: enabled={layout.Enabled}, droneSlots={layout.DroneSlotCount}, crystalSlots={layout.CrystalSlotCount}, disabledPaths={layout.DisabledPaths?.Length ?? 0}.");
    }


    internal static bool TryGetDroneCrystalShelf(out DroneCrystalShelfLayout layout)
    {
        layout = null;

        if (!CanRead())
            return false;

        Hashtable props = PhotonNetwork.CurrentRoom.CustomProperties;

        if (!ReadBool(props, ShelfActiveKey))
            return false;

        layout = new DroneCrystalShelfLayout
        {
            Enabled = ReadBool(props, ShelfActiveKey),
            DroneSlotCount = ReadInt(props, ShelfDroneSlotCountKey),
            CrystalSlotCount = ReadInt(props, ShelfCrystalSlotCountKey),
            DisabledPaths = SplitPaths(ReadString(props, ShelfDisabledKey))
        };

        return true;
    }


    internal static bool IsReady()
    {
        if (!CanRead())
            return false;

        return ReadBool(PhotonNetwork.CurrentRoom.CustomProperties, LayoutReadyKey);
    }


    internal static int GetSequence()
    {
        if (!CanRead())
            return 0;

        return ReadInt(PhotonNetwork.CurrentRoom.CustomProperties, LayoutSequenceKey);
    }


    private static bool CanWrite()
    {
        return PhotonNetwork.InRoom &&
               PhotonNetwork.IsMasterClient &&
               PhotonNetwork.CurrentRoom != null;
    }


    private static bool CanRead()
    {
        return PhotonNetwork.InRoom &&
               PhotonNetwork.CurrentRoom != null;
    }


    private static bool ReadBool(Hashtable props, string key)
    {
        return props.TryGetValue(key, out object value) && value is bool boolValue && boolValue;
    }


    private static int ReadInt(Hashtable props, string key)
    {
        return props.TryGetValue(key, out object value) && value is int intValue ? intValue : 0;
    }


    private static string ReadString(Hashtable props, string key)
    {
        return props.TryGetValue(key, out object value) && value is string stringValue ? stringValue : string.Empty;
    }


    private static Vector3 ReadVector3(Hashtable props, string key)
    {
        return props.TryGetValue(key, out object value) && value is Vector3 vector ? vector : Vector3.zero;
    }


    private static Quaternion ReadQuaternion(Hashtable props, string key)
    {
        return props.TryGetValue(key, out object value) && value is Quaternion rotation ? rotation : Quaternion.identity;
    }


    private static string JoinPaths(string[] paths)
    {
        return paths == null ? string.Empty : string.Join("\n", paths);
    }


    private static string[] SplitPaths(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static int _nextSequence;


    private static int NextSequence()
    {
        _nextSequence++;
        if (_nextSequence <= 0)
            _nextSequence = 1;

        return _nextSequence;
    }

}