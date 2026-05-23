using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace MoreStandsForShops;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; }
    internal static Plugin Instance { get; private set; }

    // General config
    internal static ConfigEntry<bool> EnableMod;
    internal static ConfigEntry<bool> EnableAdditionalUpgradeStand;
    internal static ConfigEntry<bool> EnableVanillaShelfTableRewrite;
    internal static ConfigEntry<bool> DisableShopPoolLimit;
    internal static ConfigEntry<bool> DebugLogs;

    // Item Counts
    internal static Dictionary<string, ConfigEntry<int>> ItemCounts = new();

    // Same Item Copies
    internal static Dictionary<string, ConfigEntry<int>> SameItemCopies = new();

    // Per-item spawn chance / weight
    internal static Dictionary<string, ConfigEntry<int>> ItemSpawnChances = new();

    private static readonly string[] VanillaItemSpawnChanceNames =
    {
        "Duct Taped Grenades",
        "Feather Drone",
        "Grenade",
        "Human Grenade",
        "Indestructible Drone",
        "Recharge Drone",
        "Roll Drone",
        "Rubber Duck",
        "Shockwave Grenade",
        "Stun Grenade",
        "Zero Gravity Drone",
        "Zero Gravity Orb",
        "Boltzap",
        "Explosive Mine",
        "Gun",
        "Pulse Pistol",
        "Shockwave Mine",
        "Shotgun",
        "Tranq Gun",
        "Trapzap",
        "Defibro",
        "Duck Bucket",
        "Frying Pan",
        "Phase Bridge",
        "Photon Blaster",
        "C.A.R.T.",
        "Energy Crystal",
        "Baseball Bat",
        "C.A.R.T. Cannon",
        "C.A.R.T. Laser",
        "Extraction Tracker",
        "Inflatable Hammer",
        "Leaf Blower",
        "Prodzap",
        "Roll Staff",
        "Semibot Walkies",
        "Sledge Hammer",
        "Sword",
        "Valuable Tracker",
        "Void Staff",
        "Zero Gravity Staff",
        "Autoscan Upgrade",
        "Crouch Rest Upgrade",
        "Death Head Battery Upgrade",
        "Extra Jump Upgrade",
        "Extra Life Upgrade",
        "Health Upgrade",
        "Item Resist Upgrade",
        "Item Value Upgrade",
        "Mana Regeneration Upgrade",
        "Map Enemy Tracker Upgrade",
        "Map Player Count Upgrade",
        "Map Player Tracker Upgrade",
        "Map Zoom Upgrade",
        "Range Upgrade",
        "Scout Cooldown Upgrade",
        "Sprint Speed Upgrade",
        "Sprint Usage Upgrade",
        "Stamina Upgrade",
        "Strength Upgrade",
        "Tumble Climb Upgrade",
        "Tumble Launch Upgrade",
        "Tumble Wings Upgrade",
        "Valuable Count Upgrade",
        "Large Health Pack (100)",
        "Medium Health Pack (50)",
        "Small Health Pack (25)",
        "POCKET C.A.R.T.",
        "Hauler",
        "Scout"
    };

    private Harmony _harmony;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        LoadConfig();

        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        _harmony.PatchAll();

        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} v{MyPluginInfo.PLUGIN_VERSION} loaded.");
    }

    private void LoadConfig()
    {
        // ========== General ==========
        EnableMod = Config.Bind("General", "Enable Mod", true, "Enable or disable the entire mod.");
        EnableAdditionalUpgradeStand = Config.Bind("General", "Enable Additional Upgrade Stand", true, "Spawn a second upgrade stand in the shop.");
        EnableVanillaShelfTableRewrite = Config.Bind("General", "Enable Vanilla Shelf Table Rewrite", true, "Move small items to the lower health shelf and make vanilla table small slots accept medium/large/large_high items.");
        DisableShopPoolLimit = Config.Bind("General", "Disable Shop Pool Limit", true, "Raise vanilla shop spawn budget to the filtered item pool size so free matching slots keep trying to fill.");
        DebugLogs = Config.Bind("General", "Debug Logs", false, "Enable detailed debug logging for troubleshooting.");

        // ========== Item Counts ==========
        var ic = "Item Counts";
        ItemCounts["Upgrades Per Stand"] = Config.Bind(ic, "Upgrades Per Stand", 8, new ConfigDescription("Upgrade items per stand (0-14).", new AcceptableValueRange<int>(0, 14)));
        ItemCounts["Total Upgrades"] = Config.Bind(ic, "Total Upgrades", 14, new ConfigDescription("Total upgrade items across the vanilla stand and passive second stand (0-28).", new AcceptableValueRange<int>(0, 28)));
        ItemCounts["Drones"] = Config.Bind(ic, "Drones", 4, new ConfigDescription("Drone items to spawn (0-8).", new AcceptableValueRange<int>(0, 8)));
        ItemCounts["Power Crystals"] = Config.Bind(ic, "Power Crystals", 3, new ConfigDescription("Power crystals (0-5).", new AcceptableValueRange<int>(0, 5)));
        ItemCounts["Orbs"] = Config.Bind(ic, "Orbs", 1, new ConfigDescription("Orbs (0-3).", new AcceptableValueRange<int>(0, 3)));
        ItemCounts["Grenades"] = Config.Bind(ic, "Grenades", 4, new ConfigDescription("Grenades (0-6).", new AcceptableValueRange<int>(0, 6)));
        ItemCounts["Mines"] = Config.Bind(ic, "Mines", 2, new ConfigDescription("Mines (0-3).", new AcceptableValueRange<int>(0, 3)));
        ItemCounts["Melee"] = Config.Bind(ic, "Melee", 4, new ConfigDescription("Melee weapons (0-6).", new AcceptableValueRange<int>(0, 6)));
        ItemCounts["Guns"] = Config.Bind(ic, "Guns", 4, new ConfigDescription("Guns (0-6).", new AcceptableValueRange<int>(0, 6)));
        ItemCounts["C.A.R.T. Cannon"] = Config.Bind(ic, "C.A.R.T. Cannon", 1, new ConfigDescription("C.A.R.T. Cannon items (0-4).", new AcceptableValueRange<int>(0, 4)));
        ItemCounts["Launchers"] = Config.Bind(ic, "Launchers (Staff)", 2, new ConfigDescription("Launchers/staffs (0-6).", new AcceptableValueRange<int>(0, 6)));
        ItemCounts["Tools"] = Config.Bind(ic, "Tools", 2, new ConfigDescription("Tools (0-4).", new AcceptableValueRange<int>(0, 4)));
        ItemCounts["Health Packs"] = Config.Bind(ic, "Health Packs", 3, new ConfigDescription("Health packs (0-5).", new AcceptableValueRange<int>(0, 5)));
        ItemCounts["Carts"] = Config.Bind(ic, "Carts", 2, new ConfigDescription("Carts (0-4).", new AcceptableValueRange<int>(0, 4)));
        ItemCounts["Pocket Carts"] = Config.Bind(ic, "Pocket Carts", 1, new ConfigDescription("Pocket carts (0-4).", new AcceptableValueRange<int>(0, 4)));
        ItemCounts["Vehicles"] = Config.Bind(ic, "Vehicles", 1, new ConfigDescription("Vehicles (0-4).", new AcceptableValueRange<int>(0, 4)));

        // ========== Same Item Copies ==========
        var sic = "Same Item Copies";
        SameItemCopies["Upgrades"] = Config.Bind(sic, "Upgrades", 3, new ConfigDescription("Max copies of the same upgrade item (1-6).", new AcceptableValueRange<int>(1, 6)));
        SameItemCopies["Drones"] = Config.Bind(sic, "Drones", 2, new ConfigDescription("Max copies of the same drone (1-6).", new AcceptableValueRange<int>(1, 6)));
        SameItemCopies["Guns"] = Config.Bind(sic, "Guns", 2, new ConfigDescription("Max copies of the same gun (1-6).", new AcceptableValueRange<int>(1, 6)));
        SameItemCopies["Melee"] = Config.Bind(sic, "Melee", 2, new ConfigDescription("Max copies of the same melee weapon (1-6).", new AcceptableValueRange<int>(1, 6)));
        SameItemCopies["Launchers"] = Config.Bind(sic, "Launchers (Staff)", 2, new ConfigDescription("Max copies of the same launcher (1-6).", new AcceptableValueRange<int>(1, 6)));
        SameItemCopies["Grenades"] = Config.Bind(sic, "Grenades", 3, new ConfigDescription("Max copies of the same grenade (1-6).", new AcceptableValueRange<int>(1, 6)));
        SameItemCopies["Mines"] = Config.Bind(sic, "Mines", 2, new ConfigDescription("Max copies of the same mine (1-6).", new AcceptableValueRange<int>(1, 6)));
        SameItemCopies["Health Packs"] = Config.Bind(sic, "Health Packs", 2, new ConfigDescription("Max copies of the same health pack (1-6).", new AcceptableValueRange<int>(1, 6)));

        BindVanillaItemSpawnChanceConfigs();
    }

    private static void BindVanillaItemSpawnChanceConfigs()
    {
        bool createdAny = false;
        foreach (string itemName in VanillaItemSpawnChanceNames)
        {
            BindItemSpawnChanceConfig(itemName, ref createdAny);
        }

        if (createdAny)
        {
            Instance.Config.Save();
            Log.LogInfo($"Created vanilla item spawn chance config entries for {ItemSpawnChances.Count} items.");
        }
    }

    internal static void EnsureItemSpawnChanceConfigs(IEnumerable<Item> items)
    {
        if (Instance == null || items == null)
        {
            return;
        }

        bool createdAny = false;
        foreach (Item item in items)
        {
            if (item == null)
            {
                continue;
            }

            string key = ItemConfigName(item);
            BindItemSpawnChanceConfig(key, ref createdAny);
        }

        if (createdAny)
        {
            Instance.Config.Save();
            Log.LogInfo($"Created/updated item spawn chance config entries for {ItemSpawnChances.Count} items.");
        }
    }

    internal static int GetItemSpawnChance(Item item)
    {
        if (item == null)
        {
            return 0;
        }

        string key = ItemConfigName(item);
        return ItemSpawnChances.TryGetValue(key, out ConfigEntry<int> entry) ? entry.Value : 100;
    }

    private static void BindItemSpawnChanceConfig(string key, ref bool createdAny)
    {
        if (string.IsNullOrWhiteSpace(key) || ItemSpawnChances.ContainsKey(key))
        {
            return;
        }

        ItemSpawnChances[key] = Instance.Config.Bind(
            "Item Spawn Chances",
            key,
            100,
            new ConfigDescription("Relative spawn chance/weight for this exact item. 0 disables it in this mod's shop pools; 100 is default.", new AcceptableValueRange<int>(0, 100)));
        createdAny = true;
    }

    private static string ItemConfigName(Item item)
    {
        if (item == null)
        {
            return "<null>";
        }

        return string.IsNullOrWhiteSpace(item.itemName) ? item.name : item.itemName;
    }

}
