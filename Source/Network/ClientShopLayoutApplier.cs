using System.Collections;
using MoreStandsForShops.Spawners;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace MoreStandsForShops.Network;

internal sealed class ClientShopLayoutApplier : MonoBehaviour
{
    private const int MaxAttempts = 120;
    private const float RetryDelay = 0.25f;

    private static ClientShopLayoutApplier _instance;
    private static bool _isApplying;
    private static int _lastAppliedSequence;
    private static Room _lastAppliedRoom;

    internal static void ResetForLevelChange()
    {
        Reset(clearRoomIdentity: false);
    }

    internal static void ResetForSession()
    {
        Reset(clearRoomIdentity: true);
    }

    private static void Reset(bool clearRoomIdentity)
    {
        if (_instance != null)
            _instance.StopAllCoroutines();

        _isApplying = false;

        if (!clearRoomIdentity)
            return;

        _lastAppliedRoom = null;
        _lastAppliedSequence = 0;
    }

    internal static void ApplyWhenReady()
    {
        Room currentRoom = PhotonNetwork.CurrentRoom;
        if (!ReferenceEquals(_lastAppliedRoom, currentRoom))
        {
            _lastAppliedRoom = currentRoom;
            _lastAppliedSequence = 0;
        }

        if (_isApplying)
            return;

        if (_instance == null)
        {
            GameObject host = new("MoreStandsForShops_ClientShopLayoutApplier");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<ClientShopLayoutApplier>();
        }

        _instance.StartCoroutine(_instance.ApplyRoutine());
    }
    

    private IEnumerator ApplyRoutine()
    {
        _isApplying = true;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            bool ready = ShopLayoutSync.IsReady();
            int sequence = ShopLayoutSync.GetSequence();

            if (Plugin.DebugLogs.Value)
            {
                Plugin.Log.LogInfo($"[ClientShopLayoutApplier] Waiting layout: attempt={attempt}/{MaxAttempts}, ready={ready}, sequence={sequence}, lastApplied={_lastAppliedSequence}.");
            }

            if (ready && sequence != _lastAppliedSequence)
            {
                if (ApplyNow(sequence))
                {
                    _isApplying = false;
                    yield break;
                }

                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[ClientShopLayoutApplier] Layout sequence {sequence} is ready but scene objects are not ready yet; retrying.");
            }

            yield return new WaitForSeconds(RetryDelay);
        }

        _isApplying = false;
        Plugin.Log.LogWarning($"[ClientShopLayoutApplier] Host shop layout did not become ready with a new sequence in time. lastApplied={_lastAppliedSequence}, current={ShopLayoutSync.GetSequence()}, ready={ShopLayoutSync.IsReady()}.");
    }
    

    private static bool ApplyNow(int sequence)
    {
        bool appliedAny = false;
        bool success = true;

        if (ShopLayoutSync.TryGetUpgradeStand(out UpgradeStandLayout upgradeLayout))
        {
            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"[ClientShopLayoutApplier] Upgrade layout received: variant={upgradeLayout.VariantId}, slots={upgradeLayout.UpgradeSlotCount}.");

            bool applied = UpgradeStandSpawner.SpawnNetworkVisual(
                "room-layout",
                upgradeLayout.VariantId,
                upgradeLayout.Position,
                upgradeLayout.Rotation,
                upgradeLayout.ParentPath,
                upgradeLayout.DisabledPaths,
                upgradeLayout.RerollCount,
                upgradeLayout.MaxRerollCount,
                upgradeLayout.RerollBroken);
            appliedAny |= applied;
            success &= applied;
        }

        if (ShopLayoutSync.TryGetDroneCrystalShelf(out DroneCrystalShelfLayout shelfLayout))
        {
            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"[ClientShopLayoutApplier] Shelf layout received: droneSlots={shelfLayout.DroneSlotCount}, crystalSlots={shelfLayout.CrystalSlotCount}.");

            bool applied = DroneCrystalStandSpawner.SpawnNetworkVisual(
                "room-layout",
                shelfLayout.DisabledPaths);
            appliedAny |= applied;
            success &= applied;
        }

        if (!success)
            return false;

        _lastAppliedRoom = PhotonNetwork.CurrentRoom;
        _lastAppliedSequence = sequence;

        if (Plugin.DebugLogs.Value)
        {
            if (appliedAny)
                Plugin.Log.LogInfo("[ClientShopLayoutApplier] Applied host shop layout.");
            else
                Plugin.Log.LogInfo("[ClientShopLayoutApplier] Host shop layout was ready but contained no custom stands.");
        }

        return true;
    }
}
