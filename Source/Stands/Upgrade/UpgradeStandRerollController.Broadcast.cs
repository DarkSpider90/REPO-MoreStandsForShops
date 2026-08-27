using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace MoreStandsForShops.Stands.Upgrade;

internal sealed partial class UpgradeStandRerollController
{
    private const byte RerollSyncEvent = 187;
    private const string SyncMagic = "MSFS_REROLL_V1";

    private const string MsgHoldRequestStart = "HoldRequestStart";
    private const string MsgHoldRequestStop = "HoldRequestStop";
    private const string MsgHoldVisualStart = "HoldVisualStart";
    private const string MsgHoldVisualStop = "HoldVisualStop";
    private const string MsgHoldVisualProgress = "HoldVisualProgress";
    private const string MsgRerollRequest = "RerollRequest";
    private const string MsgRerollVisual = "RerollVisual";
    private const string MsgBreakBuildUpVisual = "BreakBuildUpVisual";
    private const string MsgBrokenVisual = "BrokenVisual";

    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code != RerollSyncEvent)
            return;

        if (photonEvent.CustomData is not object[] data || data.Length < 2)
            return;

        if (data[0] is not string magic || magic != SyncMagic)
            return;

        if (data[1] is not string message)
            return;

        float progress = data.Length > 2 ? ReadProgressPayload(data[2], chargeElapsed) : chargeElapsed;

        if (isBroken &&
            message != MsgBrokenVisual &&
            message != MsgBreakBuildUpVisual)
            return;

        if (Plugin.DebugLogs.Value)
        {
            Plugin.Log.LogInfo(
                $"[UpgradeStandReroll.Sync] Event received. " +
                $"msg={message}, sender={photonEvent.Sender}, " +
                $"isMaster={PhotonNetwork.IsMasterClient}, state={state}.");
        }

        switch (message)
        {
            case MsgHoldRequestStart:
                if (PhotonNetwork.IsMasterClient)
                {
                    if (state != RerollState.Idle &&
                        state != RerollState.Rollback &&
                        state != RerollState.WaitingForHost)
                        return;

                    if (remoteHoldActorNumber > 0 && remoteHoldActorNumber != photonEvent.Sender)
                        return;

                    remoteHoldActorNumber = photonEvent.Sender;
                    BeginRemoteHoldVisual(progress);
                    BroadcastHoldVisualStart();
                }
                return;

            case MsgHoldRequestStop:
                if (PhotonNetwork.IsMasterClient)
                {
                    if (remoteHoldActorNumber != photonEvent.Sender)
                        return;

                    StopRemoteHoldVisual(progress);
                    BroadcastHoldVisualStop();
                    remoteHoldActorNumber = -1;
                }
                return;

            case MsgHoldVisualStart:
                if (!PhotonNetwork.IsMasterClient)
                    BeginRemoteHoldVisual(progress);
                return;

            case MsgHoldVisualStop:
                if (!PhotonNetwork.IsMasterClient)
                    StopRemoteHoldVisual(progress);
                return;

            case MsgHoldVisualProgress:
                if (!PhotonNetwork.IsMasterClient)
                    ApplyRemoteHoldProgress(progress);
                return;

            case MsgRerollRequest:
                if (PhotonNetwork.IsMasterClient)
                    HandleRemoteRerollRequest(photonEvent.Sender);
                return;

            case MsgRerollVisual:
                if (!PhotonNetwork.IsMasterClient)
                {
                    int synchronizedRerollCount = data.Length > 2
                        ? ReadIntPayload(data[2], rerollCount)
                        : rerollCount;
                    int synchronizedMaxRerollCount = data.Length > 3
                        ? ReadIntPayload(data[3], maxRerollCount)
                        : maxRerollCount;
                    ApplySynchronizedState(
                        synchronizedRerollCount,
                        synchronizedMaxRerollCount,
                        synchronizedBroken: false);
                    BeginVisualReroll();
                }
                return;

            case MsgBreakBuildUpVisual:
                if (!PhotonNetwork.IsMasterClient)
                    StartBreakBuildUpVisual();
                return;

            case MsgBrokenVisual:
                if (!PhotonNetwork.IsMasterClient)
                    BreakButton();
                return;
        }
    }

    private void RaiseRerollEvent(string message, ReceiverGroup receivers, float? progress = null)
    {
        if (!SemiFunc.IsMultiplayer())
            return;

        object[] payload = progress.HasValue
            ? new object[] { SyncMagic, message, progress.Value }
            : new object[] { SyncMagic, message };

        PhotonNetwork.RaiseEvent(
            RerollSyncEvent,
            payload,
            new RaiseEventOptions { Receivers = receivers },
            SendOptions.SendReliable);
    }

    private void BroadcastRerollVisual()
    {
        if (SemiFunc.IsMultiplayer())
        {
            PhotonNetwork.RaiseEvent(
                RerollSyncEvent,
                new object[] { SyncMagic, MsgRerollVisual, rerollCount, maxRerollCount },
                new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                SendOptions.SendReliable);
        }

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo("[UpgradeStandReroll.Sync] Broadcast reroll visual.");
    }

    private void BroadcastHoldVisualStop()
    {
        if (!holdVisualBroadcasted)
            return;

        holdVisualBroadcasted = false;
        holdProgressSyncTimer = 0f;

        if (!SemiFunc.IsMultiplayer() || !PhotonNetwork.IsMasterClient)
            return;

        float progress = Mathf.Clamp(chargeElapsed, 0f, HoldDuration);
        RaiseRerollEvent(MsgHoldVisualStop, ReceiverGroup.Others, progress);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[UpgradeStandReroll.Sync] Broadcast hold visual stop. progress={progress:0.00}.");
    }

    private void BroadcastHoldVisualProgress(bool force)
    {
        if (!SemiFunc.IsMultiplayer() || !PhotonNetwork.IsMasterClient || !holdVisualBroadcasted)
            return;

        holdProgressSyncTimer += Time.deltaTime;
        if (!force && holdProgressSyncTimer < HoldProgressSyncInterval)
            return;

        holdProgressSyncTimer = 0f;

        RaiseRerollEvent(
            MsgHoldVisualProgress,
            ReceiverGroup.Others,
            Mathf.Clamp(chargeElapsed, 0f, HoldDuration));
    }

    private void BroadcastHoldVisualStart()
    {
        if (holdVisualBroadcasted)
            return;

        holdVisualBroadcasted = true;
        holdProgressSyncTimer = 0f;

        if (!SemiFunc.IsMultiplayer() || !PhotonNetwork.IsMasterClient)
            return;

        float progress = Mathf.Clamp(chargeElapsed, 0f, HoldDuration);
        RaiseRerollEvent(MsgHoldVisualStart, ReceiverGroup.Others, progress);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[UpgradeStandReroll.Sync] Broadcast hold visual start. progress={progress:0.00}.");
    }

    private void BeginVisualReroll()
    {
        if (state != RerollState.Idle &&
            state != RerollState.WaitingForHost &&
            state != RerollState.Holding &&
            state != RerollState.Rollback)
        {
            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"[UpgradeStandReroll.Sync] Ignored visual reroll in state={state}.");

            return;
        }

        remoteHoldVisual = false;
        holdRequestSent = false;
        visualOnlyReroll = true;
        StateSet(RerollState.PressSucceed);
    }

    private void BeginRemoteHoldVisual(float syncedChargeElapsed = -1f)
    {
        if (isBroken)
        {
            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo("[UpgradeStandReroll.Sync] Ignored remote hold visual: stand is broken.");

            return;
        }

        if (state != RerollState.Idle &&
            state != RerollState.Rollback &&
            state != RerollState.WaitingForHost)
        {
            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"[UpgradeStandReroll.Sync] Ignored remote hold visual in state={state}.");

            return;
        }

        if (syncedChargeElapsed >= 0f)
        {
            chargeElapsed = Mathf.Clamp(syncedChargeElapsed, 0f, HoldDuration);
            resumeChargeFromRollback = chargeElapsed > 0f;
        }
        else
        {
            resumeChargeFromRollback = state == RerollState.Rollback;
        }

        remoteHoldVisual = true;
        visualOnlyReroll = true;
        StateSet(RerollState.Holding);
    }

    private void StopRemoteHoldVisual(float syncedChargeElapsed = -1f)
    {
        if (!remoteHoldVisual)
            return;

        remoteHoldVisual = false;

        if (syncedChargeElapsed >= 0f)
            chargeElapsed = Mathf.Clamp(syncedChargeElapsed, 0f, HoldDuration);

        if (state == RerollState.Holding)
            StateSet(RerollState.Rollback);
    }

    private void ApplyRemoteHoldProgress(float syncedChargeElapsed)
    {
        if (!remoteHoldVisual || state != RerollState.Holding)
            return;

        chargeElapsed = Mathf.Clamp(syncedChargeElapsed, 0f, HoldDuration);
        SyncChargeStageTriggers(chargeElapsed);
        ApplyChargeVisualsSilent(chargeElapsed);
    }

    private void RequestHostReroll()
    {
        if (!SemiFunc.IsMultiplayer())
            return;

        RaiseRerollEvent(MsgRerollRequest, ReceiverGroup.MasterClient);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo("[UpgradeStandReroll.Sync] Sent reroll request to host.");
    }

    private void HandleRemoteRerollRequest(int senderActorNumber)
    {
        if (remoteHoldActorNumber != senderActorNumber)
        {
            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"[UpgradeStandReroll.Sync] Rejected reroll request from actor {senderActorNumber}: no matching hold owner.");
            return;
        }

        remoteHoldVisual = false;
        remoteHoldActorNumber = -1;
        BroadcastHoldVisualStop();
        TryStartReroll(visualOnly: false, broadcastVisual: true);
    }

    private void RequestHostHoldStart()
    {
        if (holdRequestSent || !SemiFunc.IsMultiplayer() || PhotonNetwork.IsMasterClient)
            return;

        holdRequestSent = true;

        RaiseRerollEvent(
            MsgHoldRequestStart,
            ReceiverGroup.MasterClient,
            Mathf.Clamp(chargeElapsed, 0f, HoldDuration));

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo("[UpgradeStandReroll.Sync] Sent hold-start request to host.");
    }

    private void RequestHostHoldStop()
    {
        if (!holdRequestSent || !SemiFunc.IsMultiplayer() || PhotonNetwork.IsMasterClient)
            return;

        holdRequestSent = false;

        RaiseRerollEvent(
            MsgHoldRequestStop,
            ReceiverGroup.MasterClient,
            Mathf.Clamp(chargeElapsed, 0f, HoldDuration));

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo("[UpgradeStandReroll.Sync] Sent hold-stop request to host.");
    }

    private void BroadcastBroken()
    {
        if (!SemiFunc.IsMultiplayer() || !PhotonNetwork.IsMasterClient)
            return;

        MoreStandsForShops.Network.ShopLayoutSync.SetUpgradeRerollState(
            rerollCount,
            maxRerollCount,
            broken: true);
        RaiseRerollEvent(MsgBrokenVisual, ReceiverGroup.Others);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo("[UpgradeStandReroll.Sync] Broadcast broken visual.");
    }

    private void BroadcastBreakBuildUpVisual()
    {
        if (!SemiFunc.IsMultiplayer() || !PhotonNetwork.IsMasterClient)
            return;

        RaiseRerollEvent(MsgBreakBuildUpVisual, ReceiverGroup.Others);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo("[UpgradeStandReroll.Sync] Broadcast break build-up visual.");
    }

    private static float ReadProgressPayload(object data, float fallback)
    {
        return data switch
        {
            float value => value,
            double value => (float)value,
            int value => value,
            _ => fallback
        };
    }

    private static int ReadIntPayload(object data, int fallback)
    {
        return data switch
        {
            int value => value,
            short value => value,
            byte value => value,
            _ => fallback
        };
    }
}
