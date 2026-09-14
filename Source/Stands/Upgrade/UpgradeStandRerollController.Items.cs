using System.Collections.Generic;
using System.Linq;
using MoreStandsForShops.Network;
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
            string previousKey = ItemKey(cached.Item);
            DecrementCount(displayedCounts, previousKey);

            Item replacement = SelectReplacement(cached.Item, displayedCounts, selectedCounts);
            if (replacement == null)
            {
                IncrementCount(displayedCounts, previousKey);
                continue;
            }

            string key = ItemKey(replacement);
            IncrementCount(selectedCounts, key);
            replacements.Add(new PendingReplacement(cached.Upgrade, replacement, cached.Position, cached.Rotation));
        }

        if (Plugin.DebugLogs.Value)
        {
            if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo(
                $"[UpgradeStandReroll.Items] Built replacement list. " +
                $"source={upgrades.Count}, replacements={replacements.Count}.");
        }

        return replacements;
    }

    private static void IncrementCount(Dictionary<string, int> counts, string key)
    {
        counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
    }

    private static void DecrementCount(Dictionary<string, int> counts, string key)
    {
        if (!counts.TryGetValue(key, out int count))
            return;

        if (count <= 1)
            counts.Remove(key);
        else
            counts[key] = count - 1;
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
            if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo(
                $"[UpgradeStandReroll.Items] Scan complete. " +
                $"colliders={colliders.Length}, upgrades={result.Count}, " +
                $"scanBox={NameOrNull(scanBox)}, position={scanBox.position}, scale={scanBox.localScale}.");
        }

        return result;
    }

    private int BeginPendingRerollTransaction(IEnumerable<PendingReplacement> replacements)
    {
        if (!SemiFunc.IsMultiplayer())
            return 0;

        var entries = new List<UpgradeRerollTransactionEntry>();
        foreach (PendingReplacement replacement in replacements)
        {
            PhotonView view = FindOwningPhotonView(replacement.OriginalUpgrade);
            if (view == null || view.ViewID <= 0)
            {
                Plugin.Log.LogError(
                    $"[UpgradeStandReroll.Items] Cannot start a recoverable transaction: " +
                    $"{ItemKey(replacement.Item)} has no owning PhotonView.");
                return 0;
            }

            entries.Add(new UpgradeRerollTransactionEntry(
                view.ViewID,
                replacement.Item?.prefab?.ResourcePath,
                replacement.Position,
                replacement.Rotation));
        }

        string encodedPlan = UpgradeRerollTransactionCodec.Encode(entries);
        return ShopLayoutSync.BeginUpgradeRerollTransaction(encodedPlan);
    }


    private bool CommitPendingReplacementsAtomically()
    {
        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[UpgradeStandReroll.Items] Committing replacement upgrades atomically. count={pendingReplacements.Count}.");

        if (pendingReplacements.Count == 0)
        {
            ShopLayoutSync.CompleteUpgradeRerollTransaction(activeRerollTransactionId);
            activeRerollTransactionId = 0;
            replacementsCommitted = true;
            return true;
        }

        var spawnedReplacements = new List<PreparedReplacement>(pendingReplacements.Count);

        try
        {
            // Never remove an original until every corresponding network object was
            // created successfully. This closes the former empty-stand failure window.
            foreach (PendingReplacement replacement in pendingReplacements)
            {
                GameObject spawned = SpawnReplacement(
                    replacement.Item,
                    replacement.Position,
                    replacement.Rotation);
                if (spawned == null)
                    throw new System.InvalidOperationException($"Spawn returned null for {ItemKey(replacement.Item)}.");

                // Replacements must exist before originals are removed so a failed
                // network spawn cannot empty the stand. Suppress their physics for
                // this same-frame overlap window so the two objects cannot push each
                // other out of the slot before Photon destroys the original.
                spawnedReplacements.Add(new PreparedReplacement(
                    spawned,
                    replacement.Position,
                    replacement.Rotation));
            }

        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogError($"[UpgradeStandReroll.Items] Atomic replacement commit failed; rolling back new objects and preserving originals. {ex}");

            foreach (PreparedReplacement spawned in spawnedReplacements)
            {
                try
                {
                    DestroySpawnedReplacement(spawned.GameObject);
                }
                catch (System.Exception rollbackEx)
                {
                    Plugin.Log.LogError($"[UpgradeStandReroll.Items] Failed to remove a rollback replacement safely. {rollbackEx}");
                }
            }

            return false;
        }

        // At this point every replacement exists. A destroy failure may leave a
        // duplicate, but can no longer create an empty slot or lose an item.
        foreach (PendingReplacement replacement in pendingReplacements)
        {
            if (replacement.OriginalUpgrade == null)
                continue;

            try
            {
                DestroyUpgrade(replacement.OriginalUpgrade);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError(
                    $"[UpgradeStandReroll.Items] Replacement exists, but an original could not be removed; preserving both is safer than item loss. {ex}");
            }
        }

        // The old working flow exposed replacements only when OpenHatch began. Keep
        // that physical timing: the room objects already exist for transaction safety,
        // but remain staged and non-colliding throughout the rolling compartment
        // animation. StateOpenHatch releases them at their exact saved slot poses.
        preparedReplacements.AddRange(spawnedReplacements);

        ShopLayoutSync.CompleteUpgradeRerollTransaction(activeRerollTransactionId);
        activeRerollTransactionId = 0;
        replacementsCommitted = true;
        return true;
    }


    private void MaintainPreparedReplacementStaging()
    {
        if (preparedReplacements.Count == 0 ||
            (SemiFunc.IsMultiplayer() && !PhotonNetwork.IsMasterClient))
        {
            return;
        }

        for (int i = preparedReplacements.Count - 1; i >= 0; i--)
        {
            if (!preparedReplacements[i].MaintainStagedPose())
                preparedReplacements.RemoveAt(i);
        }
    }


    private void ActivatePreparedReplacementsForReveal()
    {
        if (preparedReplacements.Count == 0)
            return;

        var released = new List<PreparedReplacement>(preparedReplacements);
        preparedReplacements.Clear();

        foreach (PreparedReplacement replacement in released)
        {
            try
            {
                replacement.ReleaseAtFinalPose();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError(
                    $"[UpgradeStandReroll.Items] Replacement reveal placement failed. {ex}");
            }
        }

        StartCoroutine(SettleReleasedReplacements(released));
    }


    private System.Collections.IEnumerator SettleReleasedReplacements(
        List<PreparedReplacement> released)
    {
        // PhysGrabObject.EnableRigidbody runs after the network prefab's Start and a
        // short vanilla delay. Wait for that lifecycle instead of writing velocity to
        // the initial kinematic body (which was the source of the 1.1.14 warnings).
        for (int attempt = 0; attempt < 20 && released.Count > 0; attempt++)
        {
            yield return new WaitForFixedUpdate();

            for (int i = released.Count - 1; i >= 0; i--)
            {
                if (released[i].TrySettleAfterVanillaEnable())
                    released.RemoveAt(i);
            }
        }
    }


    private void ReleasePreparedReplacementsImmediately()
    {
        if (preparedReplacements.Count == 0)
            return;

        foreach (PreparedReplacement replacement in preparedReplacements)
        {
            try
            {
                replacement.ReleaseAtFinalPose();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError(
                    $"[UpgradeStandReroll.Items] Emergency replacement release failed. {ex}");
            }
        }

        preparedReplacements.Clear();
    }


    private static void DestroySpawnedReplacement(GameObject spawned)
    {
        if (spawned == null)
            return;

        PhotonView view = spawned.GetComponent<PhotonView>() ?? spawned.GetComponentInChildren<PhotonView>(true);
        if (SemiFunc.IsMultiplayer() && view != null && PhotonNetwork.IsMasterClient)
            PhotonNetwork.Destroy(view.gameObject);
        else
            Destroy(spawned);
    }


    private static PhotonView FindOwningPhotonView(ItemUpgrade upgrade)
    {
        if (upgrade == null)
            return null;

        // Walk only through the item's own hierarchy. Never fall through to the
        // copied stand PhotonView: destroying that view would remove the whole
        // stand instead of the upgrade. Items parented below the stand remain safe
        // because their own PhotonView is encountered before the controller root.
        for (Transform current = upgrade.transform; current != null; current = current.parent)
        {
            if (current.GetComponent<UpgradeStandRerollController>() != null)
                break;

            PhotonView parentView = current.GetComponent<PhotonView>();
            if (parentView != null)
                return parentView;
        }

        return upgrade.GetComponentInChildren<PhotonView>(true);
    }


    private void RecoverPendingRerollAsNewMaster()
    {
        if (!SemiFunc.IsMultiplayer() || !PhotonNetwork.IsMasterClient)
            return;

        if (!ShopLayoutSync.TryGetUpgradeRerollTransaction(
                out int transactionId,
                out string encodedPlan))
        {
            return;
        }

        if (!UpgradeRerollTransactionCodec.TryDecode(encodedPlan, out List<UpgradeRerollTransactionEntry> entries))
        {
            Plugin.Log.LogError($"[UpgradeStandReroll.Recovery] Pending transaction {transactionId} is malformed; originals were left untouched.");
            return;
        }

        var recovered = new List<PendingReplacement>();
        int unresolved = 0;

        foreach (UpgradeRerollTransactionEntry entry in entries)
        {
            // A missing original means the previous host completed this entry before
            // migration. Never spawn it a second time.
            PhotonView originalView = entry.OriginalViewId > 0
                ? PhotonView.Find(entry.OriginalViewId)
                : null;
            if (originalView == null)
                continue;

            ItemUpgrade originalUpgrade = originalView.GetComponent<ItemUpgrade>() ??
                                          originalView.GetComponentInChildren<ItemUpgrade>(true);
            Item item = ResolveItemByResourcePath(entry.ResourcePath);
            if (originalUpgrade == null || item == null)
            {
                unresolved++;
                continue;
            }

            recovered.Add(new PendingReplacement(
                originalUpgrade,
                item,
                entry.Position,
                entry.Rotation));
        }

        if (unresolved > 0)
        {
            Plugin.Log.LogError(
                $"[UpgradeStandReroll.Recovery] Transaction {transactionId} could not resolve {unresolved} entry/entries; originals were preserved and no partial recovery was attempted.");
            return;
        }

        activeRerollTransactionId = transactionId;
        pendingRerollCost = 0;
        pendingReplacements.Clear();
        pendingReplacements.AddRange(recovered);
        visualOnlyReroll = false;

        if (!CommitPendingReplacementsAtomically())
        {
            Plugin.Log.LogError($"[UpgradeStandReroll.Recovery] Transaction {transactionId} remains pending after a safe commit failure.");
            return;
        }

        // Re-emit the visual from the new authority. Clients already animating the
        // old master's event reject the duplicate by state; clients that missed it
        // start normally. An idle new host also completes the normal break sequence.
        BroadcastRerollVisual();
        if (state == RerollState.Idle)
        {
            visualOnlyReroll = false;
            StateSet(RerollState.PressSucceed);
        }

        if (Plugin.DebugLogs.Value)
        {
            Plugin.Log.LogInfo(
                $"[UpgradeStandReroll.Recovery] Transaction {transactionId} recovered after host migration. " +
                $"remainingOriginals={recovered.Count}.");
        }
    }


    private static Item ResolveItemByResourcePath(string resourcePath)
    {
        if (StatsManager.instance == null || string.IsNullOrWhiteSpace(resourcePath))
            return null;

        return StatsManager.instance.itemDictionary.Values
            .Where(item => item?.prefab != null &&
                           string.Equals(
                               item.prefab.ResourcePath,
                               resourcePath,
                               System.StringComparison.Ordinal))
            .FirstOrDefault();
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

        IEnumerable<Item> registeredItems = StatsManager.instance.itemDictionary.Values
            .Where(item => item != null)
            .GroupBy(ItemKey, System.StringComparer.Ordinal)
            .Select(group => group.First());

        foreach (Item item in registeredItems)
        {
            if (item == null || item.disabled || item.itemType != SemiFunc.itemType.item_upgrade)
                continue;

            if (item.prefab == null || !item.prefab.IsValid())
                continue;

            if (!allowPrevious &&
                string.Equals(ItemKey(item), ItemKey(previous), System.StringComparison.Ordinal))
                continue;

            int chance = Plugin.GetItemSpawnChance(item);
            if (chance <= 0)
                continue;

            string key = ItemKey(item);
            int displayed = displayedCounts.TryGetValue(key, out int displayedCount) ? displayedCount : 0;
            int selected = selectedCounts.TryGetValue(key, out int selectedCount) ? selectedCount : 0;

            if (displayed + selected >= sameLimit)
                continue;

            if (item.maxPurchase &&
                StatsManager.instance.GetItemsUpgradesPurchasedTotal(item.name) >= item.maxPurchaseAmount)
                continue;

            if (item.minPlayerCount > 1 && players < item.minPlayerCount)
                continue;

            candidates.Add((item, Mathf.Max(1, chance)));
        }

        if (candidates.Count == 0)
        {
            if (Plugin.DebugLogs.Value)
            {
                if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo(
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
                    if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo(
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
            if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo(
                $"[UpgradeStandReroll.Items] Selected fallback replacement. " +
                $"previous={ItemKey(previous)}, selected={fallback.name}, candidates={candidates.Count}.");
        }

        return fallback;
    }

    private static void DestroyUpgrade(ItemUpgrade upgrade)
    {
        if (upgrade == null)
            return;

        PhotonView view = FindOwningPhotonView(upgrade);
        GameObject target = view != null ? view.gameObject : upgrade.gameObject;

        if (Plugin.DebugLogs.Value)
        {
            if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo(
                $"[UpgradeStandReroll.Items] Destroy upgrade. " +
                $"object={target.name}, hasPhotonView={view != null}, multiplayer={SemiFunc.IsMultiplayer()}.");
        }

        if (SemiFunc.IsMultiplayer() && view != null)
            PhotonNetwork.Destroy(target);
        else
            Destroy(target);
    }

    private GameObject SpawnReplacement(Item item, Vector3 position, Quaternion fallbackRotation)
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
            if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo(
                $"[UpgradeStandReroll.Items] Spawn replacement. " +
                $"item={item.name}, position={position}, rotation={rotation.eulerAngles}, multiplayer={SemiFunc.IsMultiplayer()}.");
        }

        if (SemiFunc.IsMultiplayer())
            return PhotonNetwork.InstantiateRoomObject(item.prefab.ResourcePath, position, rotation, 0);

        return Instantiate(item.prefab.Prefab, position, rotation);
    }

    private static string ItemKey(Item item)
    {
        if (item == null)
            return string.Empty;

        string resourcePath = item.prefab?.ResourcePath ?? string.Empty;
        return item.name + "|" + resourcePath;
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

    private sealed class PreparedReplacement
    {
        private readonly GameObject _gameObject;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        private readonly Rigidbody _body;
        private readonly PhysGrabObject _grabObject;
        private readonly bool _originalIsKinematic;
        private readonly bool _originalDetectCollisions;
        private readonly RigidbodyConstraints _originalConstraints;

        internal GameObject GameObject => _gameObject;

        internal PreparedReplacement(
            GameObject gameObject,
            Vector3 position,
            Quaternion rotation)
        {
            _gameObject = gameObject ??
                          throw new System.ArgumentNullException(nameof(gameObject));
            _position = position;
            _rotation = rotation;
            _grabObject = gameObject.GetComponent<PhysGrabObject>() ??
                          gameObject.GetComponentInChildren<PhysGrabObject>(true);
            _body = _grabObject != null && _grabObject.rb != null
                ? _grabObject.rb
                : gameObject.GetComponent<Rigidbody>() ??
                    gameObject.GetComponentInChildren<Rigidbody>(true);
            _originalIsKinematic = _body != null && _body.isKinematic;
            _originalDetectCollisions = _body == null || _body.detectCollisions;
            _originalConstraints = _body != null
                ? _body.constraints
                : RigidbodyConstraints.None;

            MaintainStagedPose();
        }

        internal bool MaintainStagedPose()
        {
            if (_gameObject == null)
                return false;

            if (_grabObject != null)
                _grabObject.OverrideKinematic(0.25f);
            else if (_body != null)
                _body.isKinematic = true;

            if (_body != null)
                _body.detectCollisions = false;

            if ((_gameObject.transform.position - _position).sqrMagnitude > 0.000001f ||
                Quaternion.Angle(_gameObject.transform.rotation, _rotation) > 0.05f)
            {
                ApplyFinalPose();
            }

            return true;
        }

        internal void ReleaseAtFinalPose()
        {
            if (_gameObject == null)
                return;

            ApplyFinalPose();
            Physics.SyncTransforms();

            if (_body == null)
                return;

            _body.constraints = _originalConstraints;
            _body.detectCollisions = _originalDetectCollisions;
            if (_grabObject == null)
                _body.isKinematic = _originalIsKinematic;
        }

        internal bool TrySettleAfterVanillaEnable()
        {
            if (_gameObject == null || _body == null)
                return true;

            if (_grabObject != null && _body.isKinematic)
                return false;

            ApplyFinalPose();
            Physics.SyncTransforms();

            if (!_body.isKinematic)
            {
                _body.velocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
                _body.Sleep();
            }

            return true;
        }

        private void ApplyFinalPose()
        {
            PhotonTransformView transformView = _gameObject.GetComponent<PhotonTransformView>();
            if (transformView != null)
                transformView.Teleport(_position, _rotation);
            else
                _gameObject.transform.SetPositionAndRotation(_position, _rotation);

            if (_body != null)
            {
                _body.position = _body.transform.position;
                _body.rotation = _body.transform.rotation;
            }
        }
    }
}
