using System.Text.RegularExpressions;
using MoreStandsForShops;
using MoreStandsForShops.Network;
using MoreStandsForShops.Shop;
using UnityEngine;

internal static class Program
{
    private static readonly List<(string name, Action test)> Tests = new()
    {
        ("Every stock category maps to its expected config budget", StockCategoriesMapToExpectedBudgets),
        ("C.A.R.T. weapons use their exact shared budget", CartWeaponsUseExactBudget),
        ("Dedicated categories cannot leak into the standard table budget", DedicatedCategoriesStayOutOfStandardBudget),
        ("Dedicated shelf counts ignore previous-shop purchases", DedicatedShelfCountsIgnorePreviousPurchases),
        ("Upgrade-stand presets have stable unique identities", PresetsHaveUniqueIdentities),
        ("Upgrade-stand presets contain valid normalized coordinates", PresetsHaveValidCoordinates),
        ("Network room-property keys are unique and namespaced", NetworkPropertyKeysAreUnique),
        ("Table placement has no continuous transform writes or destructive rejection", TablePlacementIsOneShotAndNonDestructive),
        ("Table stabilization is authoritative, reversible, and first-grab limited", TableStabilizationIsSafe),
        ("Table migration callbacks are scoped away from lobby creation", TableCallbacksAreShopScoped),
        ("Reroll events retain a namespaced payload discriminator", RerollEventsHavePayloadDiscriminator),
        ("Reroll transaction plans round-trip without losing item identity or transforms", RerollTransactionsRoundTrip),
        ("Reroll transaction decoder rejects malformed plans", RerollTransactionsRejectMalformedPlans),
        ("Reroll creates every replacement before removing any original", RerollSpawnsBeforeDestroy),
        ("Reroll replacements remain staged until the old OpenHatch reveal phase", RerollCommitSuppressesOverlapPhysics),
        ("Host migration and disconnect callbacks remain wired", MultiplayerRecoveryCallbacksExist),
        ("All existing networked gameplay contracts remain present", NetworkedGameplayContractsRemainPresent),
        ("Stand Photon safety is selective and non-destructive", StandPhotonSafetyIsSelective),
        ("Scene cache performs only one global transform scan", SceneCacheUsesSingleGlobalScan),
        ("Synchronized scene paths disambiguate duplicate sibling names", ScenePathsDisambiguateDuplicates),
        ("Client layout retries retain partial progress and bounded backoff", ClientLayoutRetriesAreIncremental),
        ("Cart overlap audit is deduplicated", CartOverlapAuditIsDeduplicated),
        ("Idle upgrade-stand visuals stop settled spring work", IdleStandVisualWorkIsBounded),
        ("Package and assembly versions remain synchronized", PackageVersionsAreSynchronized)
    };

    private static int Main()
    {
        int failed = 0;
        foreach ((string name, Action test) in Tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"FAIL  {name}\n      {ex.Message}");
            }
        }

        Console.WriteLine($"\n{Tests.Count - failed}/{Tests.Count} audit tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void StockCategoriesMapToExpectedBudgets()
    {
        var expectations = new[]
        {
            (SemiFunc.itemType.item_upgrade, ShopStockCategory.Upgrades, "Total Upgrades", "Upgrades"),
            (SemiFunc.itemType.drone, ShopStockCategory.Drones, "Drones", "Drones"),
            (SemiFunc.itemType.power_crystal, ShopStockCategory.PowerCrystals, "Power Crystals", (string)null),
            (SemiFunc.itemType.orb, ShopStockCategory.Orbs, "Orbs", (string)null),
            (SemiFunc.itemType.grenade, ShopStockCategory.Grenades, "Grenades", "Grenades"),
            (SemiFunc.itemType.mine, ShopStockCategory.Mines, "Mines", "Mines"),
            (SemiFunc.itemType.melee, ShopStockCategory.Melee, "Melee", "Melee"),
            (SemiFunc.itemType.gun, ShopStockCategory.Guns, "Guns", "Guns"),
            (SemiFunc.itemType.launcher, ShopStockCategory.Launchers, "Launchers", "Launchers"),
            (SemiFunc.itemType.tool, ShopStockCategory.Tools, "Tools", (string)null),
            (SemiFunc.itemType.tracker, ShopStockCategory.Tools, "Tools", (string)null),
            (SemiFunc.itemType.healthPack, ShopStockCategory.HealthPacks, "Health Packs", "Health Packs"),
            (SemiFunc.itemType.cart, ShopStockCategory.Carts, "Carts", (string)null),
            (SemiFunc.itemType.pocket_cart, ShopStockCategory.PocketCarts, "Pocket Carts", (string)null),
            (SemiFunc.itemType.vehicle, ShopStockCategory.Vehicles, "Vehicles", (string)null)
        };

        foreach (var expected in expectations)
        {
            var item = new Item { itemType = expected.Item1, name = "test", itemName = "test" };
            Equal(expected.Item2, ShopStockCatalog.GetCategory(item), $"category for {expected.Item1}");
            True(ShopStockCatalog.TryGetConfigKeys(item, out string countKey, out string copyKey),
                $"missing config mapping for {expected.Item1}");
            Equal(expected.Item3, countKey, $"count key for {expected.Item1}");
            Equal(expected.Item4, copyKey, $"copy key for {expected.Item1}");
        }

        var playerUpgrade = new Item { itemType = SemiFunc.itemType.player_upgrade, name = "player", itemName = "player" };
        Equal(ShopStockCategory.None, ShopStockCatalog.GetCategory(playerUpgrade), "player upgrades must not enter item-upgrade stock");
        True(!ShopStockCatalog.TryGetConfigKeys(playerUpgrade, out _, out _), "player upgrade unexpectedly received a stock mapping");
    }

    private static void CartWeaponsUseExactBudget()
    {
        foreach (string name in new[] { "C.A.R.T. Cannon", "C.A.R.T. Laser" })
        {
            var byDisplayName = new Item { itemType = SemiFunc.itemType.gun, itemName = name, name = "internal" };
            True(ShopStockCatalog.TryGetConfigKeys(byDisplayName, out string countKey, out string copyKey), name);
            Equal("C.A.R.T. Weapons", countKey, name);
            Equal("C.A.R.T. Weapons", copyKey, name);

            var byInternalName = new Item { itemType = SemiFunc.itemType.gun, itemName = "localized", name = name };
            True(ShopStockCatalog.TryGetConfigKeys(byInternalName, out countKey, out copyKey), name);
            Equal("C.A.R.T. Weapons", countKey, name);
            Equal("C.A.R.T. Weapons", copyKey, name);
        }
    }

    private static void DedicatedCategoriesStayOutOfStandardBudget()
    {
        string[] keys = ShopStockCatalog.StandardBudgetCountKeys.ToArray();
        Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count(), "duplicate standard budget key");

        foreach (string dedicated in new[]
                 {
                     "Total Upgrades", "Drones", "Power Crystals", "Grenades", "Health Packs"
                 })
        {
            True(!keys.Contains(dedicated, StringComparer.Ordinal), $"dedicated category leaked into standard stock: {dedicated}");
        }

        foreach (ShopStockCategory category in new[]
                 {
                     ShopStockCategory.Orbs,
                     ShopStockCategory.Mines,
                     ShopStockCategory.Melee,
                     ShopStockCategory.Guns,
                     ShopStockCategory.Launchers,
                     ShopStockCategory.Tools,
                     ShopStockCategory.Carts,
                     ShopStockCategory.PocketCarts,
                     ShopStockCategory.Vehicles
                 })
        {
            True(ShopStockCatalog.TryGetConfigKeys(category, out string countKey, out _), category.ToString());
            True(keys.Contains(countKey, StringComparer.Ordinal), $"standard category is missing from its budget: {category}");
        }
    }

    private static void DedicatedShelfCountsIgnorePreviousPurchases()
    {
        string selector = ReadSource("Shop", "ShelfItemSelector.cs");
        int methodStart = selector.IndexOf("private static bool CanSpawnItem", StringComparison.Ordinal);
        int nextMethod = selector.IndexOf("private static string ItemKey", methodStart, StringComparison.Ordinal);
        True(methodStart >= 0 && nextMethod > methodStart, "could not locate dedicated-shelf eligibility logic");

        string eligibility = selector[methodStart..nextMethod];
        True(!eligibility.Contains("StatGetItemsPurchased", StringComparison.Ordinal),
            "dedicated shelf count still shrinks after items bought in an earlier shop");
        True(!eligibility.Contains("maxAmountInShop", StringComparison.Ordinal),
            "dedicated shelf count still inherits a mutated vanilla shop cap");
        True(selector.Contains("if (zone == MoreStandsShelfZone.Crystal)\r\n            return TargetFor(zone);", StringComparison.Ordinal) ||
             selector.Contains("if (zone == MoreStandsShelfZone.Crystal)\n            return TargetFor(zone);", StringComparison.Ordinal),
            "the crystal shelf cannot fill its configured target with repeated crystals");
    }

    private static void PresetsHaveUniqueIdentities()
    {
        List<SpawnPointData> points = CleanPresetDatabase.GetSpawnPoints();
        True(points.Count > 0, "preset list is empty");
        Equal(points.Count, points.Select(point => point.VariantId).Distinct(StringComparer.Ordinal).Count(), "duplicate variant id");

        int distinctModules = points.Select(point => point.MainModule).Distinct(StringComparer.Ordinal).Count();
        True(distinctModules >= 3, "expected coverage for at least three vanilla shop modules");

        string[] physicalKeys = points
            .Select(point => $"{point.MainModule}|{point.LocalPosition.x:R}|{point.LocalPosition.y:R}|{point.LocalPosition.z:R}|{point.LocalYaw:R}")
            .ToArray();
        Equal(physicalKeys.Length, physicalKeys.Distinct(StringComparer.Ordinal).Count(), "duplicate physical spawn preset");
    }

    private static void PresetsHaveValidCoordinates()
    {
        foreach (SpawnPointData point in CleanPresetDatabase.GetSpawnPoints())
        {
            True(!string.IsNullOrWhiteSpace(point.VariantId), "empty variant id");
            True(point.MainModule.StartsWith("Level Generator/Level/Module - Shop - N - ", StringComparison.Ordinal),
                $"unexpected module path: {point.MainModule}");
            True(point.SourceCount > 0, $"non-positive source count: {point.VariantId}");
            True(IsFinite(point.LocalPosition.x) && IsFinite(point.LocalPosition.y) && IsFinite(point.LocalPosition.z),
                $"non-finite position: {point.VariantId}");
            True(IsFinite(point.LocalYaw) && point.LocalYaw >= 0f && point.LocalYaw < 360f,
                $"yaw outside [0, 360): {point.VariantId}");
            True(Math.Abs(point.LocalYaw % 90f) < 0.001f, $"yaw is not a 90-degree step: {point.VariantId}");
            True(point.DisablePaths != null, $"null disable paths: {point.VariantId}");
            True(point.RejectIfPresentPaths != null, $"null reject paths: {point.VariantId}");
            Equal(point.DisablePaths.Length, point.DisablePaths.Distinct(StringComparer.Ordinal).Count(),
                $"duplicate disable path: {point.VariantId}");
        }
    }

    private static void NetworkPropertyKeysAreUnique()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Source", "Network", "ShopLayoutSync.cs"));
        string[] keys = Regex.Matches(source, "private const string \\w+Key = \"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        True(keys.Length >= 15, "unexpectedly few network room-property keys found");
        Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count(), "duplicate Photon room-property key");
        True(keys.All(key => key.StartsWith("MSFS.", StringComparison.Ordinal)), "room-property key is not namespaced with MSFS.");
    }

    private static void TablePlacementIsOneShotAndNonDestructive()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Source", "Shop", "ShopTableItemPlacementController.cs"));
        True(!source.Contains("PhotonNetwork.Destroy", StringComparison.Ordinal), "table placement destroys network objects");
        True(!source.Contains("RejectSpawn", StringComparison.Ordinal), "table placement still contains rejection logic");
        True(!Regex.IsMatch(source, @"\bvoid\s+(Update|FixedUpdate|LateUpdate)\s*\("),
            "table placement contains a continuous frame callback");
        True(!source.Contains("transform.position =", StringComparison.Ordinal),
            "table placement continuously rewrites item positions");
        True(source.Contains("Physics.SyncTransforms();", StringComparison.Ordinal), "one-shot physics synchronization was removed");
    }

    private static void TableStabilizationIsSafe()
    {
        string stabilizer = ReadSource("Shop", "ShopTableItemStabilizer.cs");
        string placement = ReadSource("Shop", "ShopTableItemPlacementController.cs");
        string callbacks = ReadSource("Network", "ShopTableStabilizationCallbacks.cs");
        string layout = ReadSource("Network", "ShopLayoutSync.cs");

        True(stabilizer.Contains("RigidbodyConstraints.FreezePositionX", StringComparison.Ordinal) &&
             stabilizer.Contains("RigidbodyConstraints.FreezePositionZ", StringComparison.Ordinal) &&
             stabilizer.Contains("RigidbodyConstraints.FreezeRotation", StringComparison.Ordinal),
            "display constraints no longer keep table items upright and inside their slots");
        True(stabilizer.Contains("_body.constraints = _originalConstraints", StringComparison.Ordinal),
            "the first grab no longer restores the prefab's exact constraints");
        True(stabilizer.Contains("playerGrabbing.Count > 0", StringComparison.Ordinal) &&
             stabilizer.Contains("grabbedLocal", StringComparison.Ordinal),
            "local and remote first grabs are not observed");
        True(stabilizer.Contains("SemiFunc.IsMasterClientOrSingleplayer()", StringComparison.Ordinal),
            "non-authoritative clients can constrain shop item physics");
        True(stabilizer.Contains("yield return null", StringComparison.Ordinal),
            "constraints can race vanilla Start/pivot initialization");
        True(!stabilizer.Contains("Teleport(", StringComparison.Ordinal) &&
             !stabilizer.Contains("MovePosition(", StringComparison.Ordinal) &&
             !stabilizer.Contains("FreezeForces(", StringComparison.Ordinal) &&
             !stabilizer.Contains("PhotonNetwork.Destroy", StringComparison.Ordinal),
            "stabilization continuously moves or destroys a networked item");
        True(callbacks.Contains("OnMasterClientSwitched", StringComparison.Ordinal) &&
             placement.Contains("RecoverForNewMasterRoutine", StringComparison.Ordinal) &&
             layout.Contains("TableStabilizedViewIdsKey", StringComparison.Ordinal),
            "ungrabbed-item stabilization cannot recover after host migration");
        True(placement.Contains("TryWasNeverGrabbed", StringComparison.Ordinal),
            "host migration can re-freeze an item that a player already used");
    }

    private static void RerollEventsHavePayloadDiscriminator()
    {
        string source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "Source",
            "Stands",
            "Upgrade",
            "UpgradeStandRerollController.Broadcast.cs"));

        Match magic = Regex.Match(source, "private const string SyncMagic = \"([^\"]+)\"");
        True(magic.Success && magic.Groups[1].Value.StartsWith("MSFS_", StringComparison.Ordinal),
            "missing namespaced reroll payload discriminator");
        True(source.Contains("magic != SyncMagic", StringComparison.Ordinal),
            "incoming reroll events are not filtered by their payload discriminator");
        True(source.Contains("SendOptions.SendReliable", StringComparison.Ordinal),
            "reroll state events are no longer reliable");
        True(source.Contains("SyncStandId", StringComparison.Ordinal),
            "reroll events no longer identify their stand");
        True(source.Contains("ShopLayoutSync.GetSequence()", StringComparison.Ordinal),
            "reroll events no longer include the shop layout generation");
    }

    private static void TableCallbacksAreShopScoped()
    {
        string plugin = ReadSource("Plugin.cs");
        string callbacks = ReadSource("Network", "ShopTableStabilizationCallbacks.cs");
        string shopPatch = ReadSource("Patches", "ShopManagerPatch.cs");
        string resetPatch = ReadSource("Patches", "RunStateResetPatch.cs");
        string placement = ReadSource("Shop", "ShopTableItemPlacementController.cs");

        True(!plugin.Contains("AddComponent<ShopTableStabilizationCallbacks>", StringComparison.Ordinal),
            "table callback is still created during plugin startup");
        True(!Regex.IsMatch(callbacks, @"OnEnable\s*\(\)[\s\S]*?AddCallbackTarget"),
            "table callback is still registered automatically before the shop");
        True(shopPatch.Contains("ShopTableStabilizationCallbacks.BeginShopSession()", StringComparison.Ordinal) &&
             resetPatch.Contains("ShopTableStabilizationCallbacks.EndShopSession()", StringComparison.Ordinal),
            "shop-scoped callback lifecycle is incomplete");
        True(callbacks.Contains("ShopLayoutSync.HasTableStabilizedViewIds()", StringComparison.Ordinal) &&
             callbacks.Contains("catch (Exception ex)", StringComparison.Ordinal),
            "early callback guard or Photon exception containment is missing");

        int handlerStart = placement.IndexOf("internal static void HandleMasterClientSwitched()", StringComparison.Ordinal);
        int routineStart = placement.IndexOf("private static IEnumerator RecoverForNewMasterRoutine()", StringComparison.Ordinal);
        True(handlerStart >= 0 && routineStart > handlerStart, "master-switch handler was not found");
        string handler = placement.Substring(handlerStart, routineStart - handlerStart);
        True(!handler.Contains("SemiFunc.", StringComparison.Ordinal),
            "master-switch handler still touches game-run singletons during Photon callbacks");
    }

    private static void RerollTransactionsRoundTrip()
    {
        var original = new List<UpgradeRerollTransactionEntry>
        {
            new(125, "Items/Upgrade|Unicode/Рывок", new Vector3(1.25f, -2.5f, 3.75f), new Quaternion(0.1f, 0.2f, 0.3f, 0.9f)),
            new(987654, "Mods/Third Party/Upgrade.prefab", new Vector3(-100.125f, 0f, 42f), new Quaternion(-0.5f, 0f, 0.5f, 0.70710677f))
        };

        string encoded = UpgradeRerollTransactionCodec.Encode(original);
        True(UpgradeRerollTransactionCodec.TryDecode(encoded, out List<UpgradeRerollTransactionEntry> decoded),
            "valid transaction plan was rejected");
        Equal(original.Count, decoded.Count, "transaction entry count");

        for (int i = 0; i < original.Count; i++)
        {
            Equal(original[i].OriginalViewId, decoded[i].OriginalViewId, $"view id at {i}");
            Equal(original[i].ResourcePath, decoded[i].ResourcePath, $"resource path at {i}");
            Equal(original[i].Position.x, decoded[i].Position.x, $"position x at {i}");
            Equal(original[i].Position.y, decoded[i].Position.y, $"position y at {i}");
            Equal(original[i].Position.z, decoded[i].Position.z, $"position z at {i}");
            Equal(original[i].Rotation.x, decoded[i].Rotation.x, $"rotation x at {i}");
            Equal(original[i].Rotation.y, decoded[i].Rotation.y, $"rotation y at {i}");
            Equal(original[i].Rotation.z, decoded[i].Rotation.z, $"rotation z at {i}");
            Equal(original[i].Rotation.w, decoded[i].Rotation.w, $"rotation w at {i}");
        }
    }

    private static void RerollTransactionsRejectMalformedPlans()
    {
        foreach (string malformed in new[]
                 {
                     "not-a-view-id|cGF0aA==|1|2|3|0|0|0|1",
                     "1|not-base64|1|2|3|0|0|0|1",
                     "1|cGF0aA==|NaN|2|3|0|0|0|1",
                     "1|cGF0aA==|1|2"
                 })
        {
            True(!UpgradeRerollTransactionCodec.TryDecode(malformed, out _),
                $"malformed transaction was accepted: {malformed}");
        }
    }

    private static void RerollSpawnsBeforeDestroy()
    {
        string source = ReadSource("Stands", "Upgrade", "UpgradeStandRerollController.Items.cs");
        int method = source.IndexOf("private bool CommitPendingReplacementsAtomically()", StringComparison.Ordinal);
        int spawn = source.IndexOf("GameObject spawned = SpawnReplacement", method, StringComparison.Ordinal);
        int destroy = source.IndexOf("DestroyUpgrade(replacement.OriginalUpgrade)", spawn, StringComparison.Ordinal);

        True(method >= 0 && spawn > method && destroy > spawn,
            "original upgrades can be removed before all replacement spawning is attempted");
        True(source.Contains("preserving originals", StringComparison.Ordinal),
            "safe spawn-failure rollback contract is missing");
    }

    private static void MultiplayerRecoveryCallbacksExist()
    {
        string controller = ReadSource("Stands", "Upgrade", "UpgradeStandRerollController.cs");
        string broadcast = ReadSource("Stands", "Upgrade", "UpgradeStandRerollController.Broadcast.cs");
        string items = ReadSource("Stands", "Upgrade", "UpgradeStandRerollController.Items.cs");

        True(controller.Contains("IInRoomCallbacks", StringComparison.Ordinal), "room callbacks are not registered");
        True(broadcast.Contains("OnPlayerLeftRoom", StringComparison.Ordinal), "disconnect cleanup is missing");
        True(broadcast.Contains("OnMasterClientSwitched", StringComparison.Ordinal), "master migration callback is missing");
        True(items.Contains("RecoverPendingRerollAsNewMaster", StringComparison.Ordinal), "pending reroll recovery is missing");
    }

    private static void RerollCommitSuppressesOverlapPhysics()
    {
        string items = ReadSource("Stands", "Upgrade", "UpgradeStandRerollController.Items.cs");
        string controller = ReadSource("Stands", "Upgrade", "UpgradeStandRerollController.cs");
        string states = ReadSource("Stands", "Upgrade", "UpgradeStandRerollController.StateMachine.cs");

        int spawn = items.IndexOf("new PreparedReplacement(", StringComparison.Ordinal);
        int destroyLoop = items.IndexOf("foreach (PendingReplacement replacement in pendingReplacements)", spawn, StringComparison.Ordinal);
        int staged = items.IndexOf("preparedReplacements.AddRange(spawnedReplacements)", destroyLoop, StringComparison.Ordinal);
        True(spawn >= 0 && destroyLoop > spawn && staged > destroyLoop,
            "loss-safe network creation no longer precedes original removal and staging");
        True(controller.Contains("private void LateUpdate()", StringComparison.Ordinal) &&
             controller.Contains("MaintainPreparedReplacementStaging();", StringComparison.Ordinal) &&
             items.Contains("_grabObject.OverrideKinematic(0.25f)", StringComparison.Ordinal) &&
             items.Contains("_body.detectCollisions = false", StringComparison.Ordinal),
            "replacements are not held non-colliding through the rolling animation");
        True(states.Contains("ActivatePreparedReplacementsForReveal();", StringComparison.Ordinal) &&
             states.IndexOf("ActivatePreparedReplacementsForReveal();", StringComparison.Ordinal) >
             states.IndexOf("private void StateOpenHatch()", StringComparison.Ordinal),
            "replacement physics is no longer activated in the old OpenHatch reveal phase");
        True(items.Contains("if (_grabObject != null && _body.isKinematic)", StringComparison.Ordinal) &&
             items.Contains("_body.velocity = Vector3.zero", StringComparison.Ordinal) &&
             items.Contains("_body.angularVelocity = Vector3.zero", StringComparison.Ordinal) &&
             items.Contains("_body.Sleep()", StringComparison.Ordinal),
            "post-Start settling can write velocity before vanilla enables the body");
        True(items.Contains("transformView.Teleport(_position, _rotation)", StringComparison.Ordinal) &&
             items.Contains("_body.constraints = _originalConstraints", StringComparison.Ordinal) &&
             items.Contains("_body.detectCollisions = _originalDetectCollisions", StringComparison.Ordinal),
            "normal prefab physics is not restored after placement");
    }

    private static void NetworkedGameplayContractsRemainPresent()
    {
        string table = ReadSource("Shop", "ShopTableItemPlacementController.cs");
        string shelf = ReadSource("Shop", "ShelfSpawnController.cs");
        string rerollItems = ReadSource("Stands", "Upgrade", "UpgradeStandRerollController.Items.cs");
        string broadcast = ReadSource("Stands", "Upgrade", "UpgradeStandRerollController.Broadcast.cs");
        string layout = ReadSource("Network", "ShopLayoutSync.cs");

        True(table.Contains("PhotonTransformView", StringComparison.Ordinal) &&
             table.Contains("transformView.Teleport", StringComparison.Ordinal),
            "networked table rotation was removed");
        True(table.Contains("StandardYawTurns = { 0f }", StringComparison.Ordinal),
            "ordinary table items can be rotated away from their authored yaw");
        True(table.Contains("QuarterTurnYawTurns = { 90f }", StringComparison.Ordinal) &&
             table.Contains("\"Photon Blaster\"", StringComparison.Ordinal) &&
             table.Contains("\"Semibot Walkies\"", StringComparison.Ordinal),
            "dedicated Photon Blaster/Semibot Walkies rotation rule changed");
        string pool = ReadSource("Shop", "ShopPoolPlanner.cs");
        string spawnFlow = ReadSource("Patches", "ShopSpawnFlow.cs");
        True(spawnFlow.Contains("ShopPoolPlanner.FillAdaptiveTableCapacity", StringComparison.Ordinal) &&
             pool.Contains("configuredTableTarget", StringComparison.Ordinal) &&
             pool.Contains("adaptiveGroupCount", StringComparison.Ordinal) &&
             pool.Contains("Same Item Copies limits", StringComparison.Ordinal),
            "adaptive table shortfall is no longer filled within configured limits");
        True(shelf.Contains("IsShockwaveGrenade", StringComparison.Ordinal) &&
             shelf.Contains("? 90f", StringComparison.Ordinal),
            "Shockwave Grenade rotation rule changed");
        True(rerollItems.Contains("PhotonNetwork.InstantiateRoomObject", StringComparison.Ordinal),
            "networked upgrade spawning was removed");
        True(rerollItems.Contains("PhotonNetwork.Destroy", StringComparison.Ordinal),
            "networked upgrade destruction was removed");
        True(broadcast.Contains("PhotonNetwork.RaiseEvent", StringComparison.Ordinal),
            "stand interaction synchronization was removed");
        True(layout.Contains("SetCustomProperties", StringComparison.Ordinal),
            "stand layout synchronization was removed");
    }

    private static void StandPhotonSafetyIsSelective()
    {
        string safety = ReadSource("Utilities", "StandNetworkSafety.cs");
        string upgradeSpawner = ReadSource("Spawners", "UpgradeStandSpawner.cs");
        string droneSpawner = ReadSource("Spawners", "DroneCrystalStandSpawner.cs");

        True(safety.Contains("view.enabled = false", StringComparison.Ordinal),
            "inherited PhotonViews are not made inert");
        True(safety.Contains("CloneVanillaSceneVisual", StringComparison.Ordinal) &&
             safety.Contains("view.sceneViewId = 0", StringComparison.Ordinal) &&
             safety.Contains("finally", StringComparison.Ordinal),
            "vanilla scene ViewIDs are not suppressed and restored during visual cloning");
        True(safety.Contains("RuntimeViewIdField.SetValue(view, 0)", StringComparison.Ordinal) &&
             !safety.Contains("view.ViewID = 0", StringComparison.Ordinal),
            "copied runtime ViewIDs are not cleared without unregistering the vanilla owner");
        True(!safety.Contains("PhotonNetwork.Destroy", StringComparison.Ordinal),
            "stand safety helper destroys network objects");
        True(!safety.Contains("ItemAttributes", StringComparison.Ordinal),
            "stand safety helper can target shop item networking");
        True(Regex.IsMatch(upgradeSpawner,
                @"DisableInheritedPhotonViews\(\s*spawnedStand[\s\S]*?spawnedStand\.SetActive\(true\)"),
            "upgrade stand is activated before inherited networking is disabled");
        True(Regex.IsMatch(droneSpawner,
                @"DisableInheritedPhotonViews\(\s*spawnedStand[\s\S]*?spawnedStand\.SetActive\(true\)"),
            "drone shelf is activated before inherited networking is disabled");
        True(upgradeSpawner.Contains("CloneVanillaSceneVisual", StringComparison.Ordinal) &&
             droneSpawner.Contains("CloneVanillaSceneVisual", StringComparison.Ordinal),
            "a stand prefab is still cloned with live vanilla scene ViewIDs");
    }

    private static void SceneCacheUsesSingleGlobalScan()
    {
        string source = ReadSource("Utilities", "ShopSceneCache.cs");
        Equal(1, Regex.Matches(source, "Resources\\.FindObjectsOfTypeAll<").Count,
            "scene cache global scan count");
    }

    private static void ScenePathsDisambiguateDuplicates()
    {
        string source = ReadSource("Utilities", "ShopSceneCache.cs");
        True(source.Contains("|MSFSIDX:", StringComparison.Ordinal),
            "duplicate hierarchy paths do not carry a stable sibling signature");
        True(source.Contains("legacyPathCounts", StringComparison.Ordinal),
            "duplicate full paths are not detected");
        True(source.Contains("transformByPath.ContainsKey(legacyPath)", StringComparison.Ordinal),
            "legacy name-only path lookup compatibility was removed");
    }

    private static void ClientLayoutRetriesAreIncremental()
    {
        string source = ReadSource("Network", "ClientShopLayoutApplier.cs");
        True(source.Contains("_upgradeApplied", StringComparison.Ordinal) &&
             source.Contains("_shelfApplied", StringComparison.Ordinal),
            "partial client layout progress is not retained");
        True(source.Contains("RetryDelayForAttempt", StringComparison.Ordinal) &&
             source.Contains("MaxRetryDelay", StringComparison.Ordinal),
            "bounded retry backoff is missing");
    }

    private static void CartOverlapAuditIsDeduplicated()
    {
        string source = ReadSource("Spawners", "UpgradeStandSpawner.cs");
        True(source.Contains("_cartRecheckRoutine != null", StringComparison.Ordinal),
            "cart overlap recheck can still be scheduled more than once");
        True(source.Contains("_cartRecheckRoutine = null", StringComparison.Ordinal),
            "cart overlap routine never releases its deduplication guard");
        Equal(1, Regex.Matches(source, "Object\\.FindObjectsOfType<PhysGrabObject>").Count,
            "cart fallback is globally rescanned on every retry");
        True(!source.Contains("Object.FindObjectsOfType<ItemAttributes>", StringComparison.Ordinal) &&
             !source.Contains("Object.FindObjectsOfType<PhysGrabCart>", StringComparison.Ordinal),
            "cart audit still performs multiple global component scans");
        True(source.Contains("ShouldStopCartRechecks", StringComparison.Ordinal) &&
             source.Contains("_shopPopulationCompleted", StringComparison.Ordinal),
            "post-population cart checks do not stop after a clear pass");
    }

    private static void IdleStandVisualWorkIsBounded()
    {
        string controller = ReadSource("Stands", "Upgrade", "UpgradeStandRerollController.cs");
        string visuals = ReadSource("Stands", "Upgrade", "UpgradeStandRerollController.Visuals.cs");

        True(controller.Contains("ButtonSpringNeedsUpdate()", StringComparison.Ordinal),
            "settled button spring still runs unconditionally every frame");
        True(visuals.Contains("if (!meshSpringsActive)", StringComparison.Ordinal) &&
             visuals.Contains("AreMeshSpringsSettled", StringComparison.Ordinal),
            "settled mesh springs still run permanently");
        True(visuals.Contains("state is RerollState.RollStart or RerollState.Rolling or RerollState.RollEnd", StringComparison.Ordinal),
            "active reroll animation no longer keeps spring updates enabled");
    }

    private static void PackageVersionsAreSynchronized()
    {
        string pluginInfo = ReadSource("MyPluginInfo.cs");
        string manifest = File.ReadAllText(Path.Combine(RepositoryRoot(), "manifest.json"));
        string changelog = File.ReadAllText(Path.Combine(RepositoryRoot(), "CHANGELOG.md"));

        Match assemblyVersion = Regex.Match(pluginInfo, "PLUGIN_VERSION = \"([^\"]+)\"");
        Match packageVersion = Regex.Match(manifest, "\"version_number\"\\s*:\\s*\"([^\"]+)\"");
        True(assemblyVersion.Success && packageVersion.Success, "version metadata is missing");
        Equal(assemblyVersion.Groups[1].Value, packageVersion.Groups[1].Value,
            "assembly/package version mismatch");
        True(changelog.Contains($"## {assemblyVersion.Groups[1].Value}", StringComparison.Ordinal),
            "current version is missing from changelog");
    }

    private static string ReadSource(params string[] parts)
    {
        string[] path = new string[parts.Length + 2];
        path[0] = RepositoryRoot();
        path[1] = "Source";
        Array.Copy(parts, 0, path, 2, parts.Length);
        return File.ReadAllText(Path.Combine(path));
    }

    private static string RepositoryRoot()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo current = new(start);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "MoreStandsForShops.csproj")) &&
                    Directory.Exists(Path.Combine(current.FullName, "Source")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate the MoreStandsForShops repository root.");
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected={expected}, actual={actual}");
    }
}
