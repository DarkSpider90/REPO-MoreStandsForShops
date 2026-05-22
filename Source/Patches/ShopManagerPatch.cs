using MoreStandsForShops.Network;
using MoreStandsForShops.Rewriters;
using MoreStandsForShops.Spawners;
using MoreStandsForShops.Shop;
using HarmonyLib;


namespace MoreStandsForShops.Patches;

[HarmonyPatch(typeof(ShopManager))]
internal static class ShopManagerPatch
{

    [HarmonyPrefix]
    [HarmonyPatch("ShopInitialize")]
    private static void ShopInitializePrefix(ShopManager __instance)
    {
        if (!SemiFunc.RunIsShop())
            return;

        if (StatsManager.instance == null)
            return;

        if (!Plugin.EnableMod.Value)
        {
            Plugin.Log.LogInfo("[ShopManagerPatch] Mod disabled by config; shop customizations skipped.");
            return;
        }

        bool isHostOrSingleplayer = SemiFunc.IsMasterClientOrSingleplayer();

        if (isHostOrSingleplayer)
        {
            ShopSpawnFlow.ResetForShop();
        }

        if (!isHostOrSingleplayer)
        {
            Plugin.Log.LogInfo("[ShopManagerPatch] Client detected; waiting for host shop layout.");
            ClientShopLayoutApplier.ApplyWhenReady();
            return;
        }

        Plugin.Log.LogInfo("[ShopManagerPatch] Running host shop initialization customizations...");

        if (SemiFunc.IsMultiplayer())
            ShopLayoutSync.Clear();

        // 1. Spawn additional upgrade stand
        RunCustomizationStep("UpgradeStandSpawner", () => UpgradeStandSpawner.TrySpawn(out _, configureItemVolumes: true));

        // 2. Spawn drone/crystal stand
        RunCustomizationStep("DroneCrystalStandSpawner", () => DroneCrystalStandSpawner.TrySpawn(out _, configureItemVolumes: true));

        // 3. Rewrite vanilla health shelf/table ItemVolumes before vanilla collects scene volumes.
        RunCustomizationStep("VanillaShelfTableRewriter", VanillaShelfTableRewriter.Apply);

        // 4. Override item counts in StatsManager
        RunCustomizationStep("ShopItemLimitPlanner", ShopItemLimitPlanner.ApplyConfiguredItemLimits);

        if (SemiFunc.IsMultiplayer())
            ShopLayoutSync.MarkReady();

        Plugin.Log.LogInfo("[ShopManagerPatch] Customization complete.");
    }


    private static void RunCustomizationStep(string stepName, System.Action action)
    {
        try
        {
            action();
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogError($"[ShopManagerPatch] {stepName} failed; continuing vanilla shop initialization.\n{ex}");
        }
    }


    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch("GetAllItemsFromStatsManager")]
    private static void GetAllItemsFromStatsManagerPrefix(ShopManager __instance)
    {
        if (!Plugin.EnableMod.Value || !SemiFunc.IsMasterClientOrSingleplayer())
            return;

        ShopItemLimitPlanner.ApplyConfiguredItemLimits();
    }


    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch("GetAllItemsFromStatsManager")]
    private static void GetAllItemsFromStatsManagerPostfix(ShopManager __instance)
    {
        if (!Plugin.EnableMod.Value || !SemiFunc.IsMasterClientOrSingleplayer())
            return;

        ShopPoolPlanner.PreparePools(__instance);
        ShopBudgetPlanner.ApplyConfiguredBudgets(__instance);
    }

}
