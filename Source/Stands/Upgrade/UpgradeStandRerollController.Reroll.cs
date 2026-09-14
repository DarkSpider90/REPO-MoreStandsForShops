using System.Collections.Generic;
using UnityEngine;

namespace MoreStandsForShops.Stands.Upgrade;

internal sealed partial class UpgradeStandRerollController
{
    private void TryStartReroll(bool visualOnly, bool broadcastVisual)
    {
        if (state != RerollState.Holding && state != RerollState.Idle)
            return;

        if (visualOnly)
        {
            visualOnlyReroll = true;
            StateSet(RerollState.PressSucceed);
            return;
        }

        if (!SemiFunc.IsMasterClientOrSingleplayer())
            return;

        RefreshSynchronizedStateForHost();

        if (isBroken)
        {
            StateSet(RerollState.PressFail);
            return;
        }

        List<CachedUpgrade> upgrades = ScanUpgradesInside();
        if (upgrades.Count == 0)
        {
            if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo("[UpgradeStandReroll] Reroll skipped: no upgrades inside stand.");
            StateSet(RerollState.PressFail);
            return;
        }

        int cost = RerollCost;
        if (SemiFunc.StatGetRunCurrency() < cost)
        {
            if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[UpgradeStandReroll] Reroll skipped: not enough currency. cost={cost}, current={SemiFunc.StatGetRunCurrency()}.");
            StateSet(RerollState.PressFail);
            return;
        }

        List<PendingReplacement> replacements = BuildPendingReplacements(upgrades);
        if (replacements.Count == 0)
        {
            if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo("[UpgradeStandReroll] Reroll skipped: no valid replacement upgrades.");
            StateSet(RerollState.PressFail);
            return;
        }

        activeRerollTransactionId = BeginPendingRerollTransaction(replacements);
        if (SemiFunc.IsMultiplayer() && activeRerollTransactionId <= 0)
        {
            Plugin.Log.LogWarning("[UpgradeStandReroll] Reroll skipped: pending network transaction could not be stored safely.");
            StateSet(RerollState.PressFail);
            return;
        }

        SemiFunc.StatSetRunCurrency(SemiFunc.StatGetRunCurrency() - cost);
        if (CurrencyUI.instance != null)
            CurrencyUI.instance.FetchCurrency();

        if (maxRerollCount < 0)
            maxRerollCount = Random.Range(1, 4);

        rerollCount++;
        MoreStandsForShops.Network.ShopLayoutSync.SetUpgradeRerollState(
            rerollCount,
            maxRerollCount,
            broken: false);
        cachedUpgrades.Clear();
        cachedUpgrades.AddRange(upgrades);
        pendingReplacements.Clear();
        pendingReplacements.AddRange(replacements);
        pendingRerollCost = cost;
        replacementsCommitted = false;
        visualOnlyReroll = false;

        BroadcastHoldVisualStop();

        if (broadcastVisual)
            BroadcastRerollVisual();

        if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[UpgradeStandReroll] Reroll accepted. upgrades={upgrades.Count}, replacements={replacements.Count}, cost={cost}, rerollCount={rerollCount}, maxBeforeBreak={maxRerollCount}.");

        StateSet(RerollState.PressSucceed);
    }

    private void RefreshSynchronizedStateForHost()
    {
        if (!SemiFunc.IsMultiplayer() || !Photon.Pun.PhotonNetwork.IsMasterClient)
            return;

        if (MoreStandsForShops.Network.ShopLayoutSync.TryGetUpgradeRerollState(
                out int synchronizedRerollCount,
                out int synchronizedMaxRerollCount,
                out bool synchronizedBroken))
        {
            ApplySynchronizedState(
                synchronizedRerollCount,
                synchronizedMaxRerollCount,
                synchronizedBroken);
        }
    }
    

    private bool CanAttemptRerollLocally()
    {
        if (isBroken)
            return false;

        if (SemiFunc.StatGetRunCurrency() < RerollCost)
            return false;

        return scanBox != null;
    }


    private void HandleRerollCommitFailure()
    {
        if (pendingRerollCost > 0)
        {
            SemiFunc.StatSetRunCurrency(SemiFunc.StatGetRunCurrency() + pendingRerollCost);
            if (CurrencyUI.instance != null)
                CurrencyUI.instance.FetchCurrency();
        }

        pendingRerollCost = 0;
        rerollCount = Mathf.Max(0, rerollCount - 1);
        MoreStandsForShops.Network.ShopLayoutSync.SetUpgradeRerollState(
            rerollCount,
            maxRerollCount,
            broken: false);
        MoreStandsForShops.Network.ShopLayoutSync.CompleteUpgradeRerollTransaction(
            activeRerollTransactionId);
        activeRerollTransactionId = 0;
        cachedUpgrades.Clear();
        pendingReplacements.Clear();
        replacementsCommitted = false;
        visualOnlyReroll = true;
        BroadcastStateCorrection();

        Plugin.Log.LogWarning("[UpgradeStandReroll] Replacement commit failed safely; originals were preserved and currency was refunded.");
    }
}
