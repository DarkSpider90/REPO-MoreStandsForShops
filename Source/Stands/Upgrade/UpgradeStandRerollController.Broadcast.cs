using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace MoreStandsForShops.Stands.Upgrade;

internal sealed partial class UpgradeStandRerollController
{
    public void OnEvent(EventData photonEvent)
    {
        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[UpgradeStandReroll.Sync] Event received. code={photonEvent.Code}, sender={photonEvent.Sender}, isMaster={PhotonNetwork.IsMasterClient}, state={state}.");

        if (photonEvent.Code == HoldRequestStartEvent)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                BeginRemoteHoldVisual(ReadProgressPayload(photonEvent, chargeElapsed));
                BroadcastHoldVisualStart();
            }

            return;
        }

        if (photonEvent.Code == HoldRequestStopEvent)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                StopRemoteHoldVisual(ReadProgressPayload(photonEvent, chargeElapsed));
                BroadcastHoldVisualStop();
            }

            return;
        }

        if (photonEvent.Code == HoldVisualStartEvent)
        {
            if (!PhotonNetwork.IsMasterClient)
                BeginRemoteHoldVisual(ReadProgressPayload(photonEvent, chargeElapsed));

            return;
        }

        if (photonEvent.Code == HoldVisualStopEvent)
        {
            if (!PhotonNetwork.IsMasterClient)
                StopRemoteHoldVisual(ReadProgressPayload(photonEvent, chargeElapsed));

            return;
        }
        
        if (photonEvent.Code == HoldVisualProgressEvent)
        {
            if (!PhotonNetwork.IsMasterClient)
                ApplyRemoteHoldProgress(ReadProgressPayload(photonEvent, chargeElapsed));

            return;
        }

        if (photonEvent.Code == RerollRequestEvent)
        {
            if (PhotonNetwork.IsMasterClient)
                TryStartReroll(visualOnly: false, broadcastVisual: true);

            return;
        }

        if (photonEvent.Code == RerollVisualEvent)
        {
            if (!PhotonNetwork.IsMasterClient)
                BeginVisualReroll();

            return;
        }

        if (photonEvent.Code == BrokenVisualEvent && !PhotonNetwork.IsMasterClient)
            BreakButton();
    }
    

    private void BroadcastRerollVisual()
    {
        if (!SemiFunc.IsMultiplayer())
            return;

        PhotonNetwork.RaiseEvent(
            RerollVisualEvent,
            new object[0],
            new RaiseEventOptions { Receivers = ReceiverGroup.Others },
            SendOptions.SendReliable);

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

        PhotonNetwork.RaiseEvent(
            HoldVisualStopEvent,
            new object[] { Mathf.Clamp(chargeElapsed, 0f, HoldDuration) },
            new RaiseEventOptions { Receivers = ReceiverGroup.Others },
            SendOptions.SendReliable);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[UpgradeStandReroll.Sync] Broadcast hold visual stop. progress={chargeElapsed:0.00}.");
    }
    
    
    private void BroadcastHoldVisualProgress(bool force)
    {
        if (!SemiFunc.IsMultiplayer() || !PhotonNetwork.IsMasterClient || !holdVisualBroadcasted)
            return;

        holdProgressSyncTimer += Time.deltaTime;
        if (!force && holdProgressSyncTimer < HoldProgressSyncInterval)
            return;

        holdProgressSyncTimer = 0f;

        PhotonNetwork.RaiseEvent(
            HoldVisualProgressEvent,
            new object[] { Mathf.Clamp(chargeElapsed, 0f, HoldDuration) },
            new RaiseEventOptions { Receivers = ReceiverGroup.Others },
            SendOptions.SendReliable);
    }
    

    private void BroadcastHoldVisualStart()
    {
        if (holdVisualBroadcasted)
            return;

        holdVisualBroadcasted = true;
        holdProgressSyncTimer = 0f;

        if (!SemiFunc.IsMultiplayer() || !PhotonNetwork.IsMasterClient)
            return;

        PhotonNetwork.RaiseEvent(
            HoldVisualStartEvent,
            new object[] { Mathf.Clamp(chargeElapsed, 0f, HoldDuration) },
            new RaiseEventOptions { Receivers = ReceiverGroup.Others },
            SendOptions.SendReliable);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[UpgradeStandReroll.Sync] Broadcast hold visual start. progress={chargeElapsed:0.00}.");
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

        PhotonNetwork.RaiseEvent(
            RerollRequestEvent,
            new object[0],
            new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient },
            SendOptions.SendReliable);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo("[UpgradeStandReroll.Sync] Sent reroll request to host.");
    }
    

    private void RequestHostHoldStart()
    {
        if (holdRequestSent || !SemiFunc.IsMultiplayer() || PhotonNetwork.IsMasterClient)
            return;

        holdRequestSent = true;

        PhotonNetwork.RaiseEvent(
            HoldRequestStartEvent,
            new object[] { Mathf.Clamp(chargeElapsed, 0f, HoldDuration) },
            new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient },
            SendOptions.SendReliable);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo("[UpgradeStandReroll.Sync] Sent hold-start request to host.");
    }
    

    private void RequestHostHoldStop()
    {
        if (!holdRequestSent || !SemiFunc.IsMultiplayer() || PhotonNetwork.IsMasterClient)
            return;

        holdRequestSent = false;

        PhotonNetwork.RaiseEvent(
            HoldRequestStopEvent,
            new object[] { Mathf.Clamp(chargeElapsed, 0f, HoldDuration) },
            new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient },
            SendOptions.SendReliable);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo("[UpgradeStandReroll.Sync] Sent hold-stop request to host.");
    }
    

    private void BroadcastBroken()
    {
        if (!SemiFunc.IsMultiplayer() || !PhotonNetwork.IsMasterClient)
            return;

        PhotonNetwork.RaiseEvent(
            BrokenVisualEvent,
            new object[0],
            new RaiseEventOptions { Receivers = ReceiverGroup.Others },
            SendOptions.SendReliable);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo("[UpgradeStandReroll.Sync] Broadcast broken visual.");
    }
    
    
    private static float ReadProgressPayload(EventData photonEvent, float fallback)
    {
        object data = photonEvent.CustomData;

        if (data is object[] array && array.Length > 0)
            data = array[0];

        return data switch
        {
            float value => value,
            double value => (float)value,
            int value => value,
            _ => fallback
        };
    }
    
}