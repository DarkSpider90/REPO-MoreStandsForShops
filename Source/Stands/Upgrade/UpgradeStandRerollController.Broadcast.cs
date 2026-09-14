using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace MoreStandsForShops.Stands.Upgrade;

internal sealed partial class UpgradeStandRerollController
{
    private const byte RerollSyncEvent = 187;
    private const string SyncMagic = "MSFS_REROLL_V1";
    private const string SyncStandId = "additional-upgrade-stand";

    private const string MsgHoldRequestStart = "HoldRequestStart";
    private const string MsgHoldRequestStop = "HoldRequestStop";
    private const string MsgHoldVisualStart = "HoldVisualStart";
    private const string MsgHoldVisualStop = "HoldVisualStop";
    private const string MsgHoldVisualProgress = "HoldVisualProgress";
    private const string MsgRerollRequest = "RerollRequest";
    private const string MsgRerollVisual = "RerollVisual";
    private const string MsgBreakBuildUpVisual = "BreakBuildUpVisual";
    private const string MsgBrokenVisual = "BrokenVisual";
    private const string MsgStateCorrection = "StateCorrection";

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

        if (!IsCurrentStandEvent(data))
            return;

        if (IsHostBroadcast(message) && !WasSentByCurrentMaster(photonEvent.Sender))
            return;

        float progress = data.Length > 2 ? ReadProgressPayload(data[2], chargeElapsed) : chargeElapsed;

        if (isBroken &&
            message != MsgBrokenVisual &&
            message != MsgBreakBuildUpVisual &&
            message != MsgStateCorrection)
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

            case MsgStateCorrection:
                if (!PhotonNetwork.IsMasterClient)
                {
                    int synchronizedRerollCount = data.Length > 2
                        ? ReadIntPayload(data[2], rerollCount)
                        : rerollCount;
                    int synchronizedMaxRerollCount = data.Length > 3
                        ? ReadIntPayload(data[3], maxRerollCount)
                        : maxRerollCount;
                    bool synchronizedBroken = data.Length > 4 && data[4] is bool broken && broken;
                    ApplySynchronizedState(
                        synchronizedRerollCount,
                        synchronizedMaxRerollCount,
                        synchronizedBroken);
                }
                return;
        }
    }

    private void RaiseRerollEvent(string message, ReceiverGroup receivers, float? progress = null)
    {
        if (!SemiFunc.IsMultiplayer())
            return;

        object[] payload = progress.HasValue
            ? CreateEventPayload(message, progress.Value)
            : CreateEventPayload(message);

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
                CreateEventPayload(MsgRerollVisual, rerollCount, maxRerollCount),
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


    private void BroadcastStateCorrection()
    {
        if (!SemiFunc.IsMultiplayer() || !PhotonNetwork.IsMasterClient)
            return;

        PhotonNetwork.RaiseEvent(
            RerollSyncEvent,
            CreateEventPayload(MsgStateCorrection, rerollCount, maxRerollCount, isBroken),
            new RaiseEventOptions { Receivers = ReceiverGroup.Others },
            SendOptions.SendReliable);
    }


    private static object[] CreateEventPayload(string message, params object[] values)
    {
        int valueCount = values?.Length ?? 0;
        var payload = new object[valueCount + 4];
        payload[0] = SyncMagic;
        payload[1] = message;

        if (valueCount > 0)
            System.Array.Copy(values, 0, payload, 2, valueCount);

        payload[payload.Length - 2] = MoreStandsForShops.Network.ShopLayoutSync.GetSequence();
        payload[payload.Length - 1] = SyncStandId;
        return payload;
    }


    private static bool IsCurrentStandEvent(object[] data)
    {
        // Accept the previous payload shape for compatibility with an event already
        // queued during an update, but validate every newly generated envelope.
        if (data.Length < 4 || data[data.Length - 1] is not string standId)
            return true;

        if (data[data.Length - 2] is not int eventSequence)
            return true;

        if (!string.Equals(standId, SyncStandId, System.StringComparison.Ordinal))
            return false;

        int currentSequence = MoreStandsForShops.Network.ShopLayoutSync.GetSequence();
        return currentSequence <= 0 || eventSequence == currentSequence;
    }


    private static bool IsHostBroadcast(string message)
    {
        return message == MsgHoldVisualStart ||
               message == MsgHoldVisualStop ||
               message == MsgHoldVisualProgress ||
               message == MsgRerollVisual ||
               message == MsgBreakBuildUpVisual ||
               message == MsgBrokenVisual ||
               message == MsgStateCorrection;
    }


    private static bool WasSentByCurrentMaster(int senderActorNumber)
    {
        return PhotonNetwork.MasterClient == null ||
               PhotonNetwork.MasterClient.ActorNumber == senderActorNumber;
    }


    public void OnPlayerEnteredRoom(Player newPlayer)
    {
    }


    public void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (!PhotonNetwork.IsMasterClient || otherPlayer == null ||
            remoteHoldActorNumber != otherPlayer.ActorNumber)
        {
            return;
        }

        StopRemoteHoldVisual();
        remoteHoldActorNumber = -1;
        BroadcastHoldVisualStop();

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[UpgradeStandReroll.Sync] Released disconnected hold owner actor={otherPlayer.ActorNumber}.");
    }


    public void OnMasterClientSwitched(Player newMasterClient)
    {
        remoteHoldActorNumber = -1;
        holdRequestSent = false;
        activationHeld = false;

        if (state is RerollState.Holding or RerollState.Rollback or RerollState.WaitingForHost)
        {
            remoteHoldVisual = false;
            holdVisualBroadcasted = false;
            StateSet(isBroken ? RerollState.Broken : RerollState.Idle);
        }

        if (!PhotonNetwork.IsMasterClient)
        {
            ReleasePreparedReplacementsImmediately();
            return;
        }

        RefreshSynchronizedStateForHost();
        RecoverPendingRerollAsNewMaster();
    }


    public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
    }


    public void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
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
