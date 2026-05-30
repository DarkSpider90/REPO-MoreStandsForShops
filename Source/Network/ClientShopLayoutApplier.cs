using System.Collections;
using MoreStandsForShops.Spawners;
using UnityEngine;

namespace MoreStandsForShops.Network;

internal sealed class ClientShopLayoutApplier : MonoBehaviour
{
    private const int MaxAttempts = 40;
    private const float RetryDelay = 0.25f;

    private static ClientShopLayoutApplier _instance;
    private static bool _isApplying;
    private static int _lastAppliedSequence;

    // Вызывать при выходе в меню / смене сессии, чтобы не застрять с _isApplying=true
    internal static void Reset()
    {
        if (_instance != null)
            _instance.StopAllCoroutines();

        _isApplying = false;
        // _lastAppliedSequence НЕ сбрасываем — он защищает от повторного применения
        // одного и того же layout при реконнекте в ту же комнату
    }

    internal static void ApplyWhenReady()
    {
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
                ApplyNow(sequence);
                _isApplying = false;
                yield break;
            }

            yield return new WaitForSeconds(RetryDelay);
        }

        _isApplying = false;
        Plugin.Log.LogWarning($"[ClientShopLayoutApplier] Host shop layout did not become ready with a new sequence in time. lastApplied={_lastAppliedSequence}, current={ShopLayoutSync.GetSequence()}, ready={ShopLayoutSync.IsReady()}.");
    }
    

    private static void ApplyNow(int sequence)
    {
        bool appliedAny = false;

        if (ShopLayoutSync.TryGetUpgradeStand(out UpgradeStandLayout upgradeLayout))
        {
            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"[ClientShopLayoutApplier] Upgrade layout received: variant={upgradeLayout.VariantId}, slots={upgradeLayout.UpgradeSlotCount}.");

            appliedAny |= UpgradeStandSpawner.SpawnNetworkVisual(
                "room-layout",
                upgradeLayout.VariantId,
                upgradeLayout.Position,
                upgradeLayout.Rotation,
                upgradeLayout.ParentPath,
                upgradeLayout.DisabledPaths);
        }

        if (ShopLayoutSync.TryGetDroneCrystalShelf(out DroneCrystalShelfLayout shelfLayout))
        {
            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"[ClientShopLayoutApplier] Shelf layout received: droneSlots={shelfLayout.DroneSlotCount}, crystalSlots={shelfLayout.CrystalSlotCount}.");

            appliedAny |= DroneCrystalStandSpawner.SpawnNetworkVisual(
                "room-layout",
                shelfLayout.DisabledPaths);
        }

        _lastAppliedSequence = sequence;

        if (Plugin.DebugLogs.Value)
        {
            if (appliedAny)
                Plugin.Log.LogInfo("[ClientShopLayoutApplier] Applied host shop layout.");
            else
                Plugin.Log.LogInfo("[ClientShopLayoutApplier] Host shop layout was ready but contained no custom stands.");
        }
    }
}