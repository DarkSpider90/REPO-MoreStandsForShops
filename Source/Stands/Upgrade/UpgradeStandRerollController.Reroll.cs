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

        SemiFunc.StatSetRunCurrency(SemiFunc.StatGetRunCurrency() - cost);
        if (CurrencyUI.instance != null)
            CurrencyUI.instance.FetchCurrency();

        if (maxRerollCount < 0)
            maxRerollCount = Random.Range(1, 4);

        rerollCount++;
        cachedUpgrades.Clear();
        cachedUpgrades.AddRange(upgrades);
        pendingReplacements.Clear();
        pendingReplacements.AddRange(replacements);
        visualOnlyReroll = false;

        BroadcastHoldVisualStop();

        if (broadcastVisual)
            BroadcastRerollVisual();

        if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[UpgradeStandReroll] Reroll accepted. upgrades={upgrades.Count}, replacements={replacements.Count}, cost={cost}, rerollCount={rerollCount}, maxBeforeBreak={maxRerollCount}.");

        StateSet(RerollState.PressSucceed);
    }
    

    private bool CanAttemptRerollLocally()
    {
        if (isBroken)
            return false;

        if (SemiFunc.StatGetRunCurrency() < RerollCost)
            return false;

        return scanBox != null;
    }
}