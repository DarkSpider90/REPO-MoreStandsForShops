using HarmonyLib;
using MoreStandsForShops.Network;
using MoreStandsForShops.Shop;

namespace MoreStandsForShops.Patches;

[HarmonyPatch(typeof(RunManager))]
internal static class RunStateResetPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(RunManager.ChangeLevel))]
    private static void ChangeLevelPostfix()
    {
        ResetSessionState("level change");
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(RunManager.LeaveToMainMenu))]
    private static void LeaveToMainMenuPostfix()
    {
        ResetSessionState("leave to main menu");
    }

    private static void ResetSessionState(string reason)
    {
        ClientShopLayoutApplier.Reset();
        ShopItemLimitPlanner.ResetForSession();

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[RunStateResetPatch] Session state reset on {reason}.");
    }
}
