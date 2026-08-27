using HarmonyLib;
using MoreStandsForShops.Network;
using MoreStandsForShops.Shop;
using MoreStandsForShops.Utilities;

namespace MoreStandsForShops.Patches;

[HarmonyPatch(typeof(RunManager))]
internal static class RunStateResetPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(RunManager.ChangeLevel))]
    private static void ChangeLevelPostfix()
    {
        ResetSessionState("level change", clearRoomIdentity: false);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(RunManager.LeaveToMainMenu))]
    private static void LeaveToMainMenuPostfix()
    {
        ResetSessionState("leave to main menu", clearRoomIdentity: true);
    }

    private static void ResetSessionState(string reason, bool clearRoomIdentity)
    {
        if (clearRoomIdentity)
            ClientShopLayoutApplier.ResetForSession();
        else
            ClientShopLayoutApplier.ResetForLevelChange();
        ShopItemLimitPlanner.ResetForSession();
        ShopSceneCache.Clear();

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[RunStateResetPatch] Session state reset on {reason}.");
    }
}
