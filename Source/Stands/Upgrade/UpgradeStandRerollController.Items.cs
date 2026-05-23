using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace MoreStandsForShops.Stands.Upgrade;

internal sealed partial class UpgradeStandRerollController
{
    private List<PendingReplacement> BuildPendingReplacements(List<CachedUpgrade> upgrades)
    {
        Dictionary<string, int> displayedCounts = BuildDisplayedCounts(upgrades);
        Dictionary<string, int> selectedCounts = new();
        List<PendingReplacement> replacements = new();

        foreach (CachedUpgrade cached in upgrades)
        {
            Item replacement = SelectReplacement(cached.Item, displayedCounts, selectedCounts);
            if (replacement == null)
                continue;

            string key = ItemKey(replacement);
            selectedCounts[key] = selectedCounts.TryGetValue(key, out int count) ? count + 1 : 1;
            replacements.Add(new PendingReplacement(cached.Upgrade, replacement, cached.Position, cached.Rotation));
        }

        if (Plugin.DebugLogs.Value)
        {
            Plugin.Log.LogInfo(
                $"[UpgradeStandReroll.Items] Built replacement list. " +
                $"source={upgrades.Count}, replacements={replacements.Count}.");
        }

        return replacements;
    }

    private List<CachedUpgrade> ScanUpgradesInside()
    {
        List<CachedUpgrade> result = new();

        if (scanBox == null)
        {
            Plugin.Log.LogWarning("[UpgradeStandReroll.Items] Missing scan box; cannot scan upgrades.");
            return result;
        }

        HashSet<ItemUpgrade> seen = new();
        Collider[] colliders = Physics.OverlapBox(scanBox.position, scanBox.localScale * 0.5f, scanBox.rotation);

        foreach (Collider collider in colliders)
        {
            ItemUpgrade upgrade = collider.GetComponent<ItemUpgrade>() ?? collider.GetComponentInParent<ItemUpgrade>();
            if (upgrade == null || !seen.Add(upgrade))
                continue;

            ItemAttributes attributes = upgrade.GetComponent<ItemAttributes>();
            if (attributes == null || attributes.item == null)
                continue;

            result.Add(new CachedUpgrade(upgrade, attributes.item, upgrade.transform.position, upgrade.transform.rotation));
        }

        if (Plugin.DebugLogs.Value)
        {
            Plugin.Log.LogInfo(
                $"[UpgradeStandReroll.Items] Scan complete. " +
                $"colliders={colliders.Length}, upgrades={result.Count}, " +
                $"scanBox={NameOrNull(scanBox)}, position={scanBox.position}, scale={scanBox.localScale}.");
        }

        return result;
    }

    private void DestroyCachedUpgrades()
    {
        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[UpgradeStandReroll.Items] Destroying cached upgrades. count={pendingReplacements.Count}.");

        foreach (PendingReplacement replacement in pendingReplacements)
            DestroyUpgrade(replacement.OriginalUpgrade);
    }

    private void SpawnPendingReplacements()
    {
        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[UpgradeStandReroll.Items] Spawning replacement upgrades. count={pendingReplacements.Count}.");

        foreach (PendingReplacement replacement in pendingReplacements)
            SpawnReplacement(replacement.Item, replacement.Position, replacement.Rotation);
    }

    private static Dictionary<string, int> BuildDisplayedCounts(IEnumerable<CachedUpgrade> upgrades)
    {
        Dictionary<string, int> counts = new();

        foreach (CachedUpgrade upgrade in upgrades)
        {
            string key = ItemKey(upgrade.Item);
            counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
        }

        return counts;
    }

    private static Item SelectReplacement(Item previous, Dictionary<string, int> displayedCounts, Dictionary<string, int> selectedCounts)
    {
        Item selected = SelectReplacementInternal(previous, displayedCounts, selectedCounts, allowPrevious: false);
        return selected ?? SelectReplacementInternal(previous, displayedCounts, selectedCounts, allowPrevious: true);
    }

    private static Item SelectReplacementInternal(
        Item previous,
        Dictionary<string, int> displayedCounts,
        Dictionary<string, int> selectedCounts,
        bool allowPrevious)
    {
        if (StatsManager.instance == null)
            return null;

        int sameLimit = Plugin.SameItemCopies.TryGetValue("Upgrades", out var entry) ? entry.Value : 6;
        int players = GameDirector.instance != null ? GameDirector.instance.PlayerList.Count : 1;
        List<(Item item, int weight)> candidates = new();

        foreach (Item item in StatsManager.instance.itemDictionary.Values)
        {
            if (item == null || item.disabled || item.itemType != SemiFunc.itemType.item_upgrade)
                continue;

            if (!allowPrevious && item == previous)
                continue;

            int chance = Plugin.GetItemSpawnChance(item);
            if (chance <= 0)
                continue;

            string key = ItemKey(item);
            int displayed = displayedCounts.TryGetValue(key, out int displayedCount) ? displayedCount : 0;
            int selected = selectedCounts.TryGetValue(key, out int selectedCount) ? selectedCount : 0;
            int purchased = SemiFunc.StatGetItemsPurchased(item.name);

            if (displayed + selected >= sameLimit)
                continue;

            if (item.maxAmountInShop > 0 && purchased + displayed + selected >= item.maxAmountInShop)
                continue;

            if (item.maxPurchase && StatsManager.instance.GetItemsUpgradesPurchasedTotal(item.name) >= item.maxPurchaseAmount)
                continue;

            if (item.minPlayerCount > 1 && players < item.minPlayerCount)
                continue;

            candidates.Add((item, Mathf.Max(1, chance)));
        }

        if (candidates.Count == 0)
        {
            if (Plugin.DebugLogs.Value)
            {
                Plugin.Log.LogInfo(
                    $"[UpgradeStandReroll.Items] No replacement candidates. " +
                    $"previous={ItemKey(previous)}, allowPrevious={allowPrevious}, sameLimit={sameLimit}.");
            }

            return null;
        }

        int totalWeight = candidates.Sum(candidate => candidate.weight);
        int roll = Random.Range(0, totalWeight);

        foreach ((Item item, int weight) in candidates)
        {
            if (roll < weight)
            {
                if (Plugin.DebugLogs.Value)
                {
                    Plugin.Log.LogInfo(
                        $"[UpgradeStandReroll.Items] Selected replacement. " +
                        $"previous={ItemKey(previous)}, selected={item.name}, " +
                        $"allowPrevious={allowPrevious}, candidates={candidates.Count}, totalWeight={totalWeight}.");
                }

                return item;
            }

            roll -= weight;
        }

        Item fallback = candidates[candidates.Count - 1].item;

        if (Plugin.DebugLogs.Value)
        {
            Plugin.Log.LogInfo(
                $"[UpgradeStandReroll.Items] Selected fallback replacement. " +
                $"previous={ItemKey(previous)}, selected={fallback.name}, candidates={candidates.Count}.");
        }

        return fallback;
    }

    private static void DestroyUpgrade(ItemUpgrade upgrade)
    {
        if (upgrade == null)
            return;

        PhotonView view = upgrade.GetComponent<PhotonView>();
        GameObject target = view != null ? view.gameObject : upgrade.gameObject;

        if (Plugin.DebugLogs.Value)
        {
            Plugin.Log.LogInfo(
                $"[UpgradeStandReroll.Items] Destroy upgrade. " +
                $"object={target.name}, hasPhotonView={view != null}, multiplayer={SemiFunc.IsMultiplayer()}.");
        }

        if (SemiFunc.IsMultiplayer() && view != null)
            PhotonNetwork.Destroy(target);
        else
            Destroy(target);
    }

    private void SpawnReplacement(Item item, Vector3 position, Quaternion fallbackRotation)
    {
        Quaternion rotation = fallbackRotation;

        if (ShopManager.instance != null && ShopManager.instance.itemRotateHelper != null)
        {
            Transform helper = ShopManager.instance.itemRotateHelper.transform;
            helper.parent = transform;
            helper.position = position;
            helper.localRotation = item.spawnRotationOffset;
            rotation = helper.rotation;
            helper.parent = ShopManager.instance.transform;
        }

        if (Plugin.DebugLogs.Value)
        {
            Plugin.Log.LogInfo(
                $"[UpgradeStandReroll.Items] Spawn replacement. " +
                $"item={item.name}, position={position}, rotation={rotation.eulerAngles}, multiplayer={SemiFunc.IsMultiplayer()}.");
        }

        if (SemiFunc.IsMultiplayer())
            PhotonNetwork.InstantiateRoomObject(item.prefab.ResourcePath, position, rotation, 0);
        else
            Instantiate(item.prefab.Prefab, position, rotation);
    }

    private static string ItemKey(Item item)
    {
        return item == null ? string.Empty : item.name;
    }

    private readonly struct CachedUpgrade
    {
        internal readonly ItemUpgrade Upgrade;
        internal readonly Item Item;
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;

        internal CachedUpgrade(ItemUpgrade upgrade, Item item, Vector3 position, Quaternion rotation)
        {
            Upgrade = upgrade;
            Item = item;
            Position = position;
            Rotation = rotation;
        }
    }

    private readonly struct PendingReplacement
    {
        internal readonly ItemUpgrade OriginalUpgrade;
        internal readonly Item Item;
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;

        internal PendingReplacement(ItemUpgrade originalUpgrade, Item item, Vector3 position, Quaternion rotation)
        {
            OriginalUpgrade = originalUpgrade;
            Item = item;
            Position = position;
            Rotation = rotation;
        }
    }
}