using System.Collections.Generic;


namespace MoreStandsForShops.Shop;

internal static class ShopStockCatalog
{
    internal static IReadOnlyList<string> StandardBudgetCountKeys { get; } = new[]
    {
        "Orbs",
        "Grenades",
        "Mines",
        "Melee",
        "Guns",
        "Launchers",
        "Tools",
        "Carts",
        "Pocket Carts",
        "Vehicles"
    };

    internal static ShopStockCategory GetCategory(Item item)
    {
        if (item == null)
            return ShopStockCategory.None;

        return item.itemType switch
        {
            SemiFunc.itemType.item_upgrade => ShopStockCategory.Upgrades,
            SemiFunc.itemType.drone => ShopStockCategory.Drones,
            SemiFunc.itemType.power_crystal => ShopStockCategory.PowerCrystals,
            SemiFunc.itemType.orb => ShopStockCategory.Orbs,
            SemiFunc.itemType.grenade => ShopStockCategory.Grenades,
            SemiFunc.itemType.mine => ShopStockCategory.Mines,
            SemiFunc.itemType.melee => ShopStockCategory.Melee,
            SemiFunc.itemType.gun => ShopStockCategory.Guns,
            SemiFunc.itemType.launcher => ShopStockCategory.Launchers,
            SemiFunc.itemType.tool => ShopStockCategory.Tools,
            SemiFunc.itemType.tracker => ShopStockCategory.Tools,
            SemiFunc.itemType.healthPack => ShopStockCategory.HealthPacks,
            SemiFunc.itemType.cart => ShopStockCategory.Carts,
            SemiFunc.itemType.pocket_cart => ShopStockCategory.PocketCarts,
            SemiFunc.itemType.vehicle => ShopStockCategory.Vehicles,

            // Vanilla ShopManager only treats item_upgrade as shop upgrades.
            SemiFunc.itemType.player_upgrade => ShopStockCategory.None,

            _ => ShopStockCategory.None
        };
    }

    internal static bool TryGetConfigKeys(Item item, out string countKey, out string copyKey)
    {
        return TryGetConfigKeys(GetCategory(item), out countKey, out copyKey);
    }

    internal static bool TryGetConfigKeys(ShopStockCategory category, out string countKey, out string copyKey)
    {
        countKey = null;
        copyKey = null;

        switch (category)
        {
            case ShopStockCategory.Upgrades:
                countKey = "Total Upgrades";
                copyKey = "Upgrades";
                return true;
            case ShopStockCategory.Drones:
                countKey = "Drones";
                copyKey = "Drones";
                return true;
            case ShopStockCategory.PowerCrystals:
                countKey = "Power Crystals";
                return true;
            case ShopStockCategory.Orbs:
                countKey = "Orbs";
                return true;
            case ShopStockCategory.Grenades:
                countKey = "Grenades";
                copyKey = "Grenades";
                return true;
            case ShopStockCategory.Mines:
                countKey = "Mines";
                copyKey = "Mines";
                return true;
            case ShopStockCategory.Melee:
                countKey = "Melee";
                copyKey = "Melee";
                return true;
            case ShopStockCategory.Guns:
                countKey = "Guns";
                copyKey = "Guns";
                return true;
            case ShopStockCategory.Launchers:
                countKey = "Launchers";
                copyKey = "Launchers";
                return true;
            case ShopStockCategory.Tools:
                countKey = "Tools";
                return true;
            case ShopStockCategory.HealthPacks:
                countKey = "Health Packs";
                copyKey = "Health Packs";
                return true;
            case ShopStockCategory.Carts:
                countKey = "Carts";
                return true;
            case ShopStockCategory.PocketCarts:
                countKey = "Pocket Carts";
                return true;
            case ShopStockCategory.Vehicles:
                countKey = "Vehicles";
                return true;
            default:
                return false;
        }
    }
}