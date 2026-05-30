using HarmonyLib;
using MoreStandsForShops.Network;
using MoreStandsForShops.Shop;
using Photon.Pun;

namespace MoreStandsForShops.Patches;

/// <summary>
/// Сбрасывает состояния между сессиями при выходе из комнаты.
/// Предотвращает застревание _isApplying=true в ClientShopLayoutApplier
/// и повторный вызов EnsureItemSpawnChanceConfigs каждый магазин.
/// </summary>
[HarmonyPatch(typeof(GameDirector))]
internal static class RunStateResetPatch
{
    [HarmonyPostfix]
    [HarmonyPatch("ReturnToMainMenu")]
    private static void ReturnToMainMenuPostfix()
    {
        ResetSessionState();
    }

    private static void ResetSessionState()
    {
        ClientShopLayoutApplier.Reset();
        ShopItemLimitPlanner.ResetForSession();

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo("[RunStateResetPatch] Session state reset on return to main menu.");
    }
}
