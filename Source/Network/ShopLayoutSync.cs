using System;
using System.Collections.Generic;
using System.Linq;
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
    private const string UpgradeRerollCountKey = "MSFS.Upgrade.RerollCount";
    private const string UpgradeMaxRerollCountKey = "MSFS.Upgrade.MaxRerollCount";
    private const string UpgradeRerollBrokenKey = "MSFS.Upgrade.RerollBroken";
    private const string UpgradeRerollTransactionPendingKey = "MSFS.Upgrade.RerollTransaction.Pending";
    private const string UpgradeRerollTransactionIdKey = "MSFS.Upgrade.RerollTransaction.Id";
    private const string UpgradeRerollTransactionPlanKey = "MSFS.Upgrade.RerollTransaction.Plan";

    private const string ShelfActiveKey = "MSFS.Shelf.Active";
    private const string ShelfDroneSlotCountKey = "MSFS.Shelf.DroneSlotCount";
    private const string ShelfCrystalSlotCountKey = "MSFS.Shelf.CrystalSlotCount";
    private const string ShelfDisabledKey = "MSFS.Shelf.Disabled";

    private const string LayoutReadyKey = "MSFS.Layout.Ready";
    private const string LayoutSequenceKey = "MSFS.Layout.Sequence";
    private const string UpgradeSlotCountKey = "MSFS.Upgrade.SlotCount";
    private const string TableStabilizedViewIdsKey = "MSFS.Table.StabilizedViewIds";

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
            { UpgradeRerollCountKey, 0 },
            { UpgradeMaxRerollCountKey, -1 },
            { UpgradeRerollBrokenKey, false },
            { UpgradeRerollTransactionPendingKey, false },
            { UpgradeRerollTransactionIdKey, 0 },
            { UpgradeRerollTransactionPlanKey, string.Empty },

            { ShelfActiveKey, false },
            { ShelfDroneSlotCountKey, 0 },
            { ShelfCrystalSlotCountKey, 0 },
            { ShelfDisabledKey, string.Empty },

            { TableStabilizedViewIdsKey, string.Empty },

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


    internal static bool SetUpgradeStand(UpgradeStandLayout layout)
    {
        if (!CanWrite() || layout == null)
            return false;

        var props = new Hashtable
        {
            { UpgradeActiveKey, layout.Enabled },
            { UpgradeSlotCountKey, layout.UpgradeSlotCount },
            { UpgradeVariantKey, layout.VariantId ?? string.Empty },
            { UpgradePositionKey, layout.Position },
            { UpgradeRotationKey, layout.Rotation },
            { UpgradeParentKey, layout.ParentPath ?? string.Empty },
            { UpgradeDisabledKey, JoinPaths(layout.DisabledPaths) },
            { UpgradeRerollCountKey, layout.RerollCount },
            { UpgradeMaxRerollCountKey, layout.MaxRerollCount },
            { UpgradeRerollBrokenKey, layout.RerollBroken }
        };

        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props))
            return false;
        
        Plugin.Log.LogInfo($"[ShopLayoutSync] Stored upgrade stand layout: enabled={layout.Enabled}, variant={layout.VariantId}, slots={layout.UpgradeSlotCount}, disabledPaths={layout.DisabledPaths?.Length ?? 0}.");
        return true;
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
            DisabledPaths = SplitPaths(ReadString(props, UpgradeDisabledKey)),
            RerollCount = ReadInt(props, UpgradeRerollCountKey),
            MaxRerollCount = props.ContainsKey(UpgradeMaxRerollCountKey)
                ? ReadInt(props, UpgradeMaxRerollCountKey)
                : -1,
            RerollBroken = ReadBool(props, UpgradeRerollBrokenKey)
        };

        return true;
    }


    internal static void SetUpgradeRerollState(int rerollCount, int maxRerollCount, bool broken)
    {
        if (!CanWrite())
            return;

        var props = new Hashtable
        {
            { UpgradeRerollCountKey, rerollCount },
            { UpgradeMaxRerollCountKey, maxRerollCount },
            { UpgradeRerollBrokenKey, broken }
        };

        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }


    internal static bool TryGetUpgradeRerollState(out int rerollCount, out int maxRerollCount, out bool broken)
    {
        rerollCount = 0;
        maxRerollCount = -1;
        broken = false;

        if (!CanRead())
            return false;

        Hashtable props = PhotonNetwork.CurrentRoom.CustomProperties;
        if (!props.ContainsKey(UpgradeRerollCountKey) &&
            !props.ContainsKey(UpgradeMaxRerollCountKey) &&
            !props.ContainsKey(UpgradeRerollBrokenKey))
        {
            return false;
        }

        rerollCount = ReadInt(props, UpgradeRerollCountKey);
        maxRerollCount = props.ContainsKey(UpgradeMaxRerollCountKey)
            ? ReadInt(props, UpgradeMaxRerollCountKey)
            : -1;
        broken = ReadBool(props, UpgradeRerollBrokenKey);
        return true;
    }


    internal static int BeginUpgradeRerollTransaction(string encodedPlan)
    {
        if (!CanWrite() || string.IsNullOrWhiteSpace(encodedPlan))
            return 0;

        Hashtable current = PhotonNetwork.CurrentRoom.CustomProperties;
        int transactionId = ReadInt(current, UpgradeRerollTransactionIdKey) + 1;
        if (transactionId <= 0)
            transactionId = 1;

        var props = new Hashtable
        {
            { UpgradeRerollTransactionPendingKey, true },
            { UpgradeRerollTransactionIdKey, transactionId },
            { UpgradeRerollTransactionPlanKey, encodedPlan }
        };

        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props))
        {
            Plugin.Log.LogError($"[ShopLayoutSync] Failed to queue pending upgrade reroll transaction {transactionId}.");
            return 0;
        }

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[ShopLayoutSync] Stored pending upgrade reroll transaction: id={transactionId}, bytes={encodedPlan.Length}.");

        return transactionId;
    }


    internal static bool TryGetUpgradeRerollTransaction(out int transactionId, out string encodedPlan)
    {
        transactionId = 0;
        encodedPlan = string.Empty;

        if (!CanRead())
            return false;

        Hashtable props = PhotonNetwork.CurrentRoom.CustomProperties;
        if (!ReadBool(props, UpgradeRerollTransactionPendingKey))
            return false;

        transactionId = ReadInt(props, UpgradeRerollTransactionIdKey);
        encodedPlan = ReadString(props, UpgradeRerollTransactionPlanKey);
        return transactionId > 0 && !string.IsNullOrWhiteSpace(encodedPlan);
    }


    internal static void CompleteUpgradeRerollTransaction(int transactionId)
    {
        if (!CanWrite())
            return;

        if (transactionId <= 0)
            transactionId = ReadInt(
                PhotonNetwork.CurrentRoom.CustomProperties,
                UpgradeRerollTransactionIdKey);

        if (transactionId <= 0)
            return;

        var props = new Hashtable
        {
            { UpgradeRerollTransactionPendingKey, false },
            { UpgradeRerollTransactionPlanKey, string.Empty }
        };

        var expected = new Hashtable
        {
            { UpgradeRerollTransactionPendingKey, true },
            { UpgradeRerollTransactionIdKey, transactionId }
        };

        PhotonNetwork.CurrentRoom.SetCustomProperties(props, expected);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[ShopLayoutSync] Completed upgrade reroll transaction: id={transactionId}.");
    }


    internal static bool SetDroneCrystalShelf(DroneCrystalShelfLayout layout)
    {
        if (!CanWrite() || layout == null)
            return false;

        var props = new Hashtable
        {
            { ShelfActiveKey, layout.Enabled },
            { ShelfDroneSlotCountKey, layout.DroneSlotCount },
            { ShelfCrystalSlotCountKey, layout.CrystalSlotCount },
            { ShelfDisabledKey, JoinPaths(layout.DisabledPaths) }
        };

        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props))
            return false;
        
        Plugin.Log.LogInfo($"[ShopLayoutSync] Stored drone/crystal shelf layout: enabled={layout.Enabled}, droneSlots={layout.DroneSlotCount}, crystalSlots={layout.CrystalSlotCount}, disabledPaths={layout.DisabledPaths?.Length ?? 0}.");
        return true;
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


    internal static bool SetTableStabilizedViewIds(IEnumerable<int> viewIds)
    {
        if (!CanWrite())
            return false;

        string encoded = string.Join(",", (viewIds ?? Array.Empty<int>())
            .Where(viewId => viewId > 0)
            .Distinct()
            .OrderBy(viewId => viewId));

        return PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
        {
            { TableStabilizedViewIdsKey, encoded }
        });
    }


    internal static int[] GetTableStabilizedViewIds()
    {
        if (!CanRead())
            return Array.Empty<int>();

        string encoded = ReadString(
            PhotonNetwork.CurrentRoom.CustomProperties,
            TableStabilizedViewIdsKey);
        if (string.IsNullOrWhiteSpace(encoded))
            return Array.Empty<int>();

        var result = new List<int>();
        foreach (string part in encoded.Split(','))
        {
            if (int.TryParse(part, out int viewId) && viewId > 0 && !result.Contains(viewId))
                result.Add(viewId);
        }

        return result.ToArray();
    }


    internal static bool HasTableStabilizedViewIds()
    {
        if (!CanRead())
            return false;

        return !string.IsNullOrWhiteSpace(ReadString(
            PhotonNetwork.CurrentRoom.CustomProperties,
            TableStabilizedViewIdsKey));
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
        int roomSequence = CanRead()
            ? ReadInt(PhotonNetwork.CurrentRoom.CustomProperties, LayoutSequenceKey)
            : 0;

        _nextSequence = Math.Max(_nextSequence, roomSequence) + 1;
        if (_nextSequence <= 0)
            _nextSequence = 1;

        return _nextSequence;
    }

}
