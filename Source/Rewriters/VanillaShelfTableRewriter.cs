using System;
using System.Collections.Generic;
using System.Linq;
using MoreStandsForShops.Utilities;
using UnityEngine;

namespace MoreStandsForShops.Rewriters;

internal static class VanillaShelfTableRewriter
{
    private const string GeneratedPrefix = "MoreStandsForShops Vanilla Shelf Slot";
    private const string MultiSizePrefix = "MoreStandsForShops Table Adaptive Slot";
    private const float SameTableSlotDistance = 0.03f;

    private static readonly SemiFunc.itemVolume[] UniversalTableSlotTypes =
    {
        SemiFunc.itemVolume.small,
        SemiFunc.itemVolume.medium,
        SemiFunc.itemVolume.large,
        SemiFunc.itemVolume.large_high
    };

    private static readonly Vector3[] HealthSlotPositions =
    {
        new(-0.72f, 1.68f, 0.50f),
        new(-0.36f, 1.68f, 0.50f),
        new(0.00f, 1.68f, 0.50f),
        new(0.36f, 1.68f, 0.50f),
        new(0.72f, 1.68f, 0.50f)
    };

    private static readonly Vector3[] LowerSmallSlotPositions =
    {
        new(-0.75f, 1.18f, 0.50f),
        new(-0.45f, 1.18f, 0.50f),
        new(-0.15f, 1.18f, 0.50f),
        new(0.15f, 1.18f, 0.50f),
        new(0.45f, 1.18f, 0.50f),
        new(0.75f, 1.18f, 0.50f)
    };

    internal static void Apply()
    {
        if (!Plugin.EnableVanillaShelfTableRewrite.Value)
        {
            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo("[ShelfTableRewrite] Disabled in config.");
            return;
        }

        List<Transform> healthShelves = FindItemStandRoots("valuable shelf short (1)").ToList();
        int rewrittenShelves = 0;
        foreach (Transform shelf in healthShelves)
        {
            if (RewriteHealthShelf(shelf))
                rewrittenShelves++;
        }

        int adaptiveTableSlots = RewriteVanillaTableVolumesInItemAreas();

        if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo($"[ShelfTableRewrite] Rewritten health shelves={rewrittenShelves}, adaptive vanilla table slot group(s)={adaptiveTableSlots}.");
    }

    private static bool RewriteHealthShelf(Transform shelf)
    {
        if (shelf == null || !shelf.gameObject.activeInHierarchy)
            return false;

        if (shelf.GetComponentsInChildren<ItemVolume>(true)
            .Any(volume => volume != null && volume.name.StartsWith(GeneratedPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"[ShelfTableRewrite] Shelf already has generated slots: {GetTransformPath(shelf)}");
            return true;
        }

        List<ShelfVolumePose> authoredHealthPoses = CaptureShelfVolumePoses(
            shelf,
            SemiFunc.itemVolume.healthPack);

        DisableOriginalShelfVolumes(shelf);

        List<ShelfVolumePose> healthPoses = BuildEvenlySpacedShelfPoses(
            authoredHealthPoses,
            HealthSlotPositions);

        for (int i = 0; i < healthPoses.Count; i++)
        {
            CreateVolumeSlot(
                shelf,
                $"{GeneratedPrefix} Health {i + 1:00}",
                SemiFunc.itemVolume.healthPack,
                healthPoses[i].LocalPosition,
                MoreStandsShelfZone.Health,
                healthPoses[i].LocalRotation);
        }

        for (int i = 0; i < LowerSmallSlotPositions.Length; i++)
        {
            CreateVolumeSlot(
                shelf,
                $"{GeneratedPrefix} Grenade {i + 1:00}",
                SemiFunc.itemVolume.small,
                LowerSmallSlotPositions[i],
                MoreStandsShelfZone.Grenade);
        }

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo(
                $"[ShelfTableRewrite] Created {healthPoses.Count} controlled healthPack upper slot(s) " +
                $"from {authoredHealthPoses.Count} vanilla pose(s), and {LowerSmallSlotPositions.Length} " +
                $"controlled grenade lower slot(s) on {GetTransformPath(shelf)}.");

        return true;
    }

    private static List<ShelfVolumePose> CaptureShelfVolumePoses(
        Transform shelf,
        SemiFunc.itemVolume itemVolume)
    {
        return shelf.GetComponentsInChildren<ItemVolume>(true)
            .Where(volume => volume != null &&
                             !volume.name.StartsWith(GeneratedPrefix, StringComparison.OrdinalIgnoreCase) &&
                             volume.itemVolume == itemVolume)
            .Select(volume => new ShelfVolumePose(
                shelf.InverseTransformPoint(volume.transform.position),
                Quaternion.Inverse(shelf.rotation) * volume.transform.rotation))
            .OrderBy(pose => pose.LocalPosition.x)
            .ThenBy(pose => pose.LocalPosition.z)
            .ToList();
    }

    private static List<ShelfVolumePose> BuildEvenlySpacedShelfPoses(
        List<ShelfVolumePose> authored,
        IReadOnlyList<Vector3> fallbackPositions)
    {
        if (authored == null || authored.Count < 2)
        {
            return fallbackPositions
                .Select(position => new ShelfVolumePose(position, Quaternion.identity))
                .ToList();
        }

        (ShelfVolumePose start, ShelfVolumePose end) = FindFarthestShelfPosePair(authored);
        var result = new List<ShelfVolumePose>(fallbackPositions.Count);
        for (int i = 0; i < fallbackPositions.Count; i++)
        {
            float t = fallbackPositions.Count <= 1 ? 0.5f : i / (float)(fallbackPositions.Count - 1);
            Vector3 position = Vector3.Lerp(start.LocalPosition, end.LocalPosition, t);
            ShelfVolumePose nearest = authored
                .OrderBy(pose => Vector3.SqrMagnitude(pose.LocalPosition - position))
                .First();
            result.Add(new ShelfVolumePose(position, nearest.LocalRotation));
        }

        return result;
    }

    private static (ShelfVolumePose start, ShelfVolumePose end) FindFarthestShelfPosePair(
        IReadOnlyList<ShelfVolumePose> poses)
    {
        ShelfVolumePose start = poses[0];
        ShelfVolumePose end = poses[poses.Count - 1];
        float farthest = -1f;

        for (int i = 0; i < poses.Count; i++)
        {
            for (int j = i + 1; j < poses.Count; j++)
            {
                float distance = Vector3.SqrMagnitude(poses[i].LocalPosition - poses[j].LocalPosition);
                if (distance <= farthest)
                    continue;

                farthest = distance;
                start = poses[i];
                end = poses[j];
            }
        }

        return start.LocalPosition.x <= end.LocalPosition.x ? (start, end) : (end, start);
    }

    private static void DisableOriginalShelfVolumes(Transform shelf)
    {
        foreach (ItemVolume volume in shelf.GetComponentsInChildren<ItemVolume>(true))
        {
            if (volume == null || volume.name.StartsWith(GeneratedPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            volume.gameObject.SetActive(false);
            volume.enabled = false;
        }
    }

    private static int RewriteVanillaTableVolumesInItemAreas()
    {
        int rewritten = 0;
        var tableRoots = FindActiveVanillaTableVolumes()
            .GroupBy(volume => volume.transform.parent)
            .Where(group => group.Key != null)
            .ToList();

        foreach (var tableRootGroup in tableRoots)
        {
            Transform tableRoot = tableRootGroup.Key;
            if (tableRoot.GetComponentsInChildren<MoreStandsMultiSizeVolume>(true).Any())
            {
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[ShelfTableRewrite] Table already has multi-size slots: {GetTransformPath(tableRoot)}");
                continue;
            }

            List<ItemVolume> originalVolumes = tableRootGroup
                .OrderBy(volume => volume.transform.localPosition.z)
                .ThenBy(volume => volume.transform.localPosition.x)
                .ThenBy(volume => (int)volume.itemVolume)
                .ToList();
            List<TableVolumeTemplate> tableTemplates = originalVolumes
                .Select(volume => new TableVolumeTemplate(volume))
                .ToList();

            List<TableLogicalSlot> logicalSlots = BuildLogicalSlots(originalVolumes);
            List<List<TableLogicalSlot>> tableRows = BuildTableRows(logicalSlots);
            int reused = 0;
            int created = 0;
            int disabled = 0;
            int groupIndex = 0;

            for (int rowIndex = 0; rowIndex < tableRows.Count; rowIndex++)
            {
                TableRowLayout row = BuildTableRowLayout(tableRows[rowIndex], tableTemplates);
                for (int cellIndex = 0; cellIndex < row.Slots.Count; cellIndex++)
                {
                    TableLogicalSlot slot = row.Slots[cellIndex];
                    string groupId = $"table-slot:{tableRoot.GetInstanceID()}:{groupIndex++:00}";
                    RewriteLogicalTableSlot(
                        tableRoot,
                        slot,
                        groupId,
                        row,
                        cellIndex,
                        rowIndex,
                        ref reused,
                        ref created,
                        ref disabled);
                }
            }

            rewritten += logicalSlots.Count;

            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo(
                    $"[ShelfTableRewrite] Rebuilt vanilla table item area with adaptive slots: " +
                    $"root={GetTransformPath(tableRoot)}, originalVolumes={originalVolumes.Count}, " +
                    $"physicalPositions={logicalSlots.Count}, tableRows={tableRows.Count}, " +
                    $"universalSlotGroups={logicalSlots.Count}, vanillaStandardTarget=8, " +
                    $"reused={reused}, created={created}, disabled={disabled}.");
        }

        return rewritten;
    }

    private static void MarkMultiSizeVolume(ItemVolume volume, string groupId, SemiFunc.itemVolume itemVolume)
    {
        volume.itemVolume = itemVolume;
        volume.itemSecretShopType = SemiFunc.itemSecretShopType.none;
        volume.enabled = true;
        volume.gameObject.SetActive(true);

        MoreStandsMultiSizeVolume marker = volume.GetComponent<MoreStandsMultiSizeVolume>();
        if (marker == null)
            marker = volume.gameObject.AddComponent<MoreStandsMultiSizeVolume>();

        marker.GroupId = groupId;
    }

    private static IEnumerable<ItemVolume> FindActiveVanillaTableVolumes()
    {
        ShopSceneCache cache = ShopSceneCache.Current;

        return cache.ItemVolumes
            .Where(volume => volume != null && volume.gameObject.activeInHierarchy)
            .Where(volume => !volume.name.StartsWith("MoreStandsForShops", StringComparison.OrdinalIgnoreCase))
            .Where(volume => volume.GetComponent<MoreStandsMultiSizeVolume>() == null)
            .Where(volume => IsTableVolumeType(volume.itemVolume))
            .Where(volume => GetTransformPath(volume.transform).IndexOf("/ITEM STANDS/ITEMS/", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsTableVolumeType(SemiFunc.itemVolume itemVolume)
    {
        return itemVolume is SemiFunc.itemVolume.small or SemiFunc.itemVolume.medium or SemiFunc.itemVolume.large or SemiFunc.itemVolume.large_high;
    }

    private static List<TableLogicalSlot> BuildLogicalSlots(List<ItemVolume> volumes)
    {
        List<TableLogicalSlot> slots = new();

        foreach (ItemVolume volume in volumes)
        {
            Vector2 point = new(volume.transform.localPosition.x, volume.transform.localPosition.z);
            TableLogicalSlot slot = slots.FirstOrDefault(existing => Vector2.Distance(existing.Center, point) <= SameTableSlotDistance);

            if (slot == null)
            {
                slot = new TableLogicalSlot();
                slots.Add(slot);
            }

            slot.Add(volume, point);
        }

        return slots;
    }

    private static List<List<TableLogicalSlot>> BuildTableRows(List<TableLogicalSlot> slots)
    {
        if (slots.Count <= 8)
            return new List<List<TableLogicalSlot>> { new(slots) };

        List<TableEdge> tree = BuildMinimumSpanningTree(slots);
        TableEdge bestCut = default;
        List<int> bestComponent = null;
        int bestBalance = int.MaxValue;

        foreach (TableEdge cut in tree)
        {
            List<int> component = CollectTreeComponent(slots.Count, tree, cut, cut.A);
            int balance = Math.Abs(slots.Count - component.Count * 2);
            if (balance > bestBalance ||
                (balance == bestBalance && bestComponent != null && cut.Distance <= bestCut.Distance))
            {
                continue;
            }

            bestBalance = balance;
            bestCut = cut;
            bestComponent = component;
        }

        if (bestComponent == null || bestComponent.Count == 0 || bestComponent.Count == slots.Count)
            return new List<List<TableLogicalSlot>> { new(slots) };

        HashSet<int> firstIndices = new(bestComponent);
        var first = new List<TableLogicalSlot>();
        var second = new List<TableLogicalSlot>();
        for (int i = 0; i < slots.Count; i++)
        {
            (firstIndices.Contains(i) ? first : second).Add(slots[i]);
        }

        return new[] { first, second }
            .OrderBy(row => row.Min(slot => slot.Center.x + slot.Center.y))
            .ToList();
    }

    private static List<TableEdge> BuildMinimumSpanningTree(IReadOnlyList<TableLogicalSlot> slots)
    {
        var edges = new List<TableEdge>(Math.Max(0, slots.Count - 1));
        if (slots.Count <= 1)
            return edges;

        var included = new bool[slots.Count];
        included[0] = true;

        while (edges.Count < slots.Count - 1)
        {
            TableEdge nearest = default;
            bool found = false;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!included[i])
                    continue;

                for (int j = 0; j < slots.Count; j++)
                {
                    if (included[j])
                        continue;

                    float distance = Vector2.Distance(slots[i].Center, slots[j].Center);
                    if (found && distance >= nearest.Distance)
                        continue;

                    nearest = new TableEdge(i, j, distance);
                    found = true;
                }
            }

            if (!found)
                break;

            included[nearest.B] = true;
            edges.Add(nearest);
        }

        return edges;
    }

    private static List<int> CollectTreeComponent(
        int slotCount,
        IReadOnlyList<TableEdge> tree,
        TableEdge cut,
        int start)
    {
        var result = new List<int>();
        var visited = new bool[slotCount];
        var pending = new Queue<int>();
        pending.Enqueue(start);
        visited[start] = true;

        while (pending.Count > 0)
        {
            int current = pending.Dequeue();
            result.Add(current);

            foreach (TableEdge edge in tree)
            {
                if (edge.EqualsUndirected(cut))
                    continue;

                int next = edge.A == current ? edge.B : edge.B == current ? edge.A : -1;
                if (next < 0 || visited[next])
                    continue;

                visited[next] = true;
                pending.Enqueue(next);
            }
        }

        return result;
    }

    private static TableRowLayout BuildTableRowLayout(
        List<TableLogicalSlot> slots,
        List<TableVolumeTemplate> allTemplates)
    {
        (Vector2 start, Vector2 end) = FindFarthestSlotPair(slots);
        Vector2 direction = (end - start).normalized;
        if (direction.sqrMagnitude < 0.001f)
            direction = Vector2.right;

        if ((Mathf.Abs(direction.x) >= Mathf.Abs(direction.y) && direction.x < 0f) ||
            (Mathf.Abs(direction.y) > Mathf.Abs(direction.x) && direction.y < 0f))
        {
            direction = -direction;
        }

        Vector2 normal = new(-direction.y, direction.x);
        List<TableLogicalSlot> ordered = slots
            .OrderBy(slot => Vector2.Dot(slot.Center, direction))
            .ToList();
        float minProjection = ordered.Min(slot => Vector2.Dot(slot.Center, direction));
        float maxProjection = ordered.Max(slot => Vector2.Dot(slot.Center, direction));
        float averageNormal = ordered.Average(slot => Vector2.Dot(slot.Center, normal));
        HashSet<ItemVolume> rowVolumes = slots
            .SelectMany(slot => slot.Volumes)
            .Where(volume => volume != null)
            .ToHashSet();
        List<TableVolumeTemplate> rowTemplates = allTemplates
            .Where(template => rowVolumes.Contains(template.Source))
            .ToList();

        return new TableRowLayout(
            ordered,
            rowTemplates,
            allTemplates,
            direction,
            normal,
            minProjection,
            maxProjection,
            averageNormal);
    }

    private static (Vector2 start, Vector2 end) FindFarthestSlotPair(
        IReadOnlyList<TableLogicalSlot> slots)
    {
        Vector2 start = slots[0].Center;
        Vector2 end = slots[slots.Count - 1].Center;
        float farthest = -1f;

        for (int i = 0; i < slots.Count; i++)
        {
            for (int j = i + 1; j < slots.Count; j++)
            {
                float distance = Vector2.SqrMagnitude(slots[i].Center - slots[j].Center);
                if (distance <= farthest)
                    continue;

                farthest = distance;
                start = slots[i].Center;
                end = slots[j].Center;
            }
        }

        return (start, end);
    }

    private static void RewriteLogicalTableSlot(
        Transform tableRoot,
        TableLogicalSlot slot,
        string groupId,
        TableRowLayout row,
        int cellIndex,
        int rowIndex,
        ref int reused,
        ref int created,
        ref int disabled)
    {
        ItemVolume anchor = slot.Volumes.FirstOrDefault(volume => volume != null);
        if (anchor == null)
            return;

        HashSet<ItemVolume> used = new();
        foreach (SemiFunc.itemVolume itemVolume in UniversalTableSlotTypes)
        {
            ItemVolume reusable = slot.Volumes.FirstOrDefault(volume =>
                volume != null && !used.Contains(volume) && volume.itemVolume == itemVolume);

            if (reusable == null && used.Count == 0)
                reusable = anchor;

            TableVolumePose pose = row.BuildPose(cellIndex, itemVolume);
            ItemVolume volume = reusable ?? CreateAdaptiveVolume(tableRoot, pose, groupId, itemVolume);
            if (reusable != null)
            {
                ApplyPose(volume.transform, pose);
                volume.volumes = new List<GameObject>(pose.Volumes);
                reused++;
            }
            else
            {
                created++;
            }

            MarkMultiSizeVolume(volume, groupId, itemVolume);
            used.Add(volume);

            if (Plugin.DebugLogs.Value)
            {
                Plugin.Log.LogInfo(
                    $"[ShelfTableRewrite] Prepared adaptive table slot: group={groupId}, type={itemVolume}, " +
                    $"row={rowIndex}, cell={cellIndex + 1}/{row.Slots.Count}, universal=True, " +
                    $"local={FormatVector(volume.transform.localPosition)}, " +
                    $"yaw={volume.transform.localRotation.eulerAngles.y:F1}.");
            }
        }

        foreach (ItemVolume extra in slot.Volumes.Where(volume => volume != null && !used.Contains(volume)))
        {
            extra.enabled = false;
            extra.gameObject.SetActive(false);
            disabled++;
        }
    }

    private static ItemVolume CreateAdaptiveVolume(
        Transform parent,
        TableVolumePose pose,
        string groupId,
        SemiFunc.itemVolume itemVolume)
    {
        GameObject slot = new($"{MultiSizePrefix} {itemVolume}");
        slot.transform.SetParent(parent, false);
        ApplyPose(slot.transform, pose);

        ItemVolume volume = slot.AddComponent<ItemVolume>();
        volume.itemVolume = itemVolume;
        volume.itemSecretShopType = SemiFunc.itemSecretShopType.none;
        volume.volumes = new List<GameObject>(pose.Volumes);

        MoreStandsMultiSizeVolume marker = slot.AddComponent<MoreStandsMultiSizeVolume>();
        marker.GroupId = groupId;
        return volume;
    }

    private static void ApplyPose(Transform target, TableVolumePose pose)
    {
        target.localPosition = pose.LocalPosition;
        target.localRotation = pose.LocalRotation;
        target.localScale = pose.LocalScale;
    }

    private static ItemVolume CreateVolumeSlot(
        Transform parent,
        string name,
        SemiFunc.itemVolume itemVolume,
        Vector3 localPosition,
        MoreStandsShelfZone? zone = null,
        Quaternion? localRotation = null)
    {
        GameObject slot = new(name);
        slot.transform.SetParent(parent, false);
        slot.transform.localPosition = localPosition;
        slot.transform.localRotation = localRotation ?? Quaternion.identity;

        ItemVolume volume = slot.AddComponent<ItemVolume>();
        volume.itemVolume = itemVolume;
        volume.itemSecretShopType = SemiFunc.itemSecretShopType.none;

        if (zone.HasValue)
        {
            MoreStandsShelfVolume marker = slot.AddComponent<MoreStandsShelfVolume>();
            marker.Zone = zone.Value;
        }

        return volume;
    }

    private static IEnumerable<Transform> FindItemStandRoots(string exactName)
    {
        ShopSceneCache cache = ShopSceneCache.Current;

        return cache.Transforms
            .Where(transform => transform != null && transform.gameObject.activeInHierarchy)
            .Where(transform => string.Equals(transform.name, exactName, StringComparison.OrdinalIgnoreCase))
            .Where(transform => GetTransformPath(transform).IndexOf("/ITEM STANDS/", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string GetTransformPath(Transform transform)
    {
        return ShopSceneCache.Current.GetTransformPath(transform);
    }

    private static string FormatVector(Vector3 vector)
    {
        return $"({vector.x:F3}, {vector.y:F3}, {vector.z:F3})";
    }

    private readonly struct ShelfVolumePose
    {
        internal readonly Vector3 LocalPosition;
        internal readonly Quaternion LocalRotation;

        internal ShelfVolumePose(Vector3 localPosition, Quaternion localRotation)
        {
            LocalPosition = localPosition;
            LocalRotation = localRotation;
        }
    }

    private readonly struct TableEdge
    {
        internal readonly int A;
        internal readonly int B;
        internal readonly float Distance;

        internal TableEdge(int a, int b, float distance)
        {
            A = a;
            B = b;
            Distance = distance;
        }

        internal bool EqualsUndirected(TableEdge other)
        {
            return (A == other.A && B == other.B) || (A == other.B && B == other.A);
        }
    }

    private sealed class TableRowLayout
    {
        internal readonly List<TableLogicalSlot> Slots;

        private readonly List<TableVolumeTemplate> _rowTemplates;
        private readonly List<TableVolumeTemplate> _allTemplates;
        private readonly Vector2 _direction;
        private readonly Vector2 _normal;
        private readonly float _minProjection;
        private readonly float _maxProjection;
        private readonly float _averageNormal;

        internal TableRowLayout(
            List<TableLogicalSlot> slots,
            List<TableVolumeTemplate> rowTemplates,
            List<TableVolumeTemplate> allTemplates,
            Vector2 direction,
            Vector2 normal,
            float minProjection,
            float maxProjection,
            float averageNormal)
        {
            Slots = slots;
            _rowTemplates = rowTemplates;
            _allTemplates = allTemplates;
            _direction = direction;
            _normal = normal;
            _minProjection = minProjection;
            _maxProjection = maxProjection;
            _averageNormal = averageNormal;
        }

        internal TableVolumePose BuildPose(int cellIndex, SemiFunc.itemVolume itemVolume)
        {
            float t = Slots.Count <= 1 ? 0.5f : cellIndex / (float)(Slots.Count - 1);
            float targetProjection = Mathf.Lerp(_minProjection, _maxProjection, t);

            List<TableVolumeTemplate> typeTemplates = _rowTemplates
                .Where(template => template.ItemVolume == itemVolume)
                .ToList();
            if (typeTemplates.Count == 0)
            {
                typeTemplates = _allTemplates
                    .Where(template => template.ItemVolume == itemVolume)
                    .ToList();
            }

            TableVolumeTemplate typeTemplate = typeTemplates
                .OrderBy(template => Mathf.Abs(
                    Vector2.Dot(ToXZ(template.LocalPosition), _direction) - targetProjection))
                .FirstOrDefault() ?? _rowTemplates.FirstOrDefault() ?? _allTemplates.First();

            float typeNormal = typeTemplates.Count > 0
                ? typeTemplates.Average(template => Vector2.Dot(ToXZ(template.LocalPosition), _normal))
                : _averageNormal;
            float normalOffset = Mathf.Clamp(typeNormal - _averageNormal, -0.15f, 0.15f);
            Vector2 targetPoint = _direction * targetProjection +
                                  _normal * (_averageNormal + normalOffset);
            Vector3 localPosition = new(targetPoint.x, typeTemplate.LocalPosition.y, targetPoint.y);
            Vector3 templateEuler = typeTemplate.LocalRotation.eulerAngles;
            Quaternion uprightLocalRotation = Quaternion.Euler(0f, templateEuler.y, 0f);

            return new TableVolumePose(
                localPosition,
                uprightLocalRotation,
                typeTemplate.LocalScale,
                typeTemplate.Volumes);
        }

        private static Vector2 ToXZ(Vector3 position)
        {
            return new Vector2(position.x, position.z);
        }
    }

    private sealed class TableLogicalSlot
    {
        internal readonly List<ItemVolume> Volumes = new();
        internal Vector2 Center { get; private set; }

        internal void Add(ItemVolume volume, Vector2 point)
        {
            Center = Volumes.Count == 0
                ? point
                : ((Center * Volumes.Count) + point) / (Volumes.Count + 1);

            Volumes.Add(volume);
        }
    }

    private sealed class TableVolumeTemplate
    {
        internal readonly ItemVolume Source;
        internal readonly SemiFunc.itemVolume ItemVolume;
        internal readonly Vector3 LocalPosition;
        internal readonly Quaternion LocalRotation;
        internal readonly Vector3 LocalScale;
        internal readonly List<GameObject> Volumes;

        internal TableVolumeTemplate(ItemVolume source)
        {
            Source = source;
            ItemVolume = source.itemVolume;
            LocalPosition = source.transform.localPosition;
            LocalRotation = source.transform.localRotation;
            LocalScale = source.transform.localScale;
            Volumes = new List<GameObject>(source.volumes);
        }
    }

    private readonly struct TableVolumePose
    {
        internal readonly Vector3 LocalPosition;
        internal readonly Quaternion LocalRotation;
        internal readonly Vector3 LocalScale;
        internal readonly List<GameObject> Volumes;

        internal TableVolumePose(
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale,
            List<GameObject> volumes)
        {
            LocalPosition = localPosition;
            LocalRotation = localRotation;
            LocalScale = localScale;
            Volumes = volumes;
        }
    }
}
