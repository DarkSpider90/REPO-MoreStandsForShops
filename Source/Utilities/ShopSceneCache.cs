using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoreStandsForShops.Utilities;

internal sealed class ShopSceneCache
{
    private static ShopSceneCache _current;

    private readonly Dictionary<int, string> pathByInstanceId = new();
    private readonly Dictionary<string, Transform> transformByPath = new(StringComparer.Ordinal);
    private readonly int sceneHandle;

    private ShopSceneCache()
    {
        Scene scene = SceneManager.GetActiveScene();
        sceneHandle = scene.handle;
        Roots = scene.GetRootGameObjects();

        Transforms = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(transform => transform != null && transform.gameObject.activeInHierarchy)
            .Where(transform => transform.gameObject.scene == scene)
            .ToArray();

        // Derive component caches from the already filtered transform snapshot. This
        // preserves the transform discovery order while avoiding two additional
        // Resources.FindObjectsOfTypeAll scans over the entire Unity process.
        ItemVolumes = Transforms
            .SelectMany(transform => transform.GetComponents<ItemVolume>())
            .Where(volume => volume != null && volume.gameObject.activeInHierarchy)
            .ToArray();

        Renderers = Transforms
            .SelectMany(transform => transform.GetComponents<Renderer>())
            .Where(renderer => renderer != null && renderer.gameObject.activeInHierarchy)
            .ToArray();

        Dictionary<int, string> legacyPaths = Transforms.ToDictionary(
            transform => transform.GetInstanceID(),
            BuildPath);
        Dictionary<string, int> legacyPathCounts = legacyPaths.Values
            .GroupBy(path => path, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (Transform transform in Transforms)
        {
            string legacyPath = legacyPaths[transform.GetInstanceID()];
            string path = legacyPathCounts[legacyPath] > 1
                ? BuildIndexedPath(legacyPath, transform)
                : legacyPath;
            pathByInstanceId[transform.GetInstanceID()] = path;

            if (!transformByPath.ContainsKey(path))
                transformByPath.Add(path, transform);

            // Preserve every existing name-only lookup. When names are duplicated it
            // intentionally retains the same first-match behavior as Transform.Find,
            // while synchronized paths use the indexed key above.
            if (!transformByPath.ContainsKey(legacyPath))
                transformByPath.Add(legacyPath, transform);
        }

        if (Plugin.DebugLogs?.Value == true)
        {
            Plugin.Log.LogInfo(
                $"[ShopSceneCache] Built scene cache: transforms={Transforms.Length}, " +
                $"itemVolumes={ItemVolumes.Length}, renderers={Renderers.Length}.");
        }
    }

    internal GameObject[] Roots { get; }
    internal Transform[] Transforms { get; }
    internal ItemVolume[] ItemVolumes { get; }
    internal Renderer[] Renderers { get; }

    internal static ShopSceneCache Current
    {
        get
        {
            Scene scene = SceneManager.GetActiveScene();
            if (_current == null || _current.sceneHandle != scene.handle)
                _current = new ShopSceneCache();

            return _current;
        }
    }

    internal static ShopSceneCache Rebuild()
    {
        _current = new ShopSceneCache();
        return _current;
    }

    internal static void Clear()
    {
        _current = null;
    }

    internal Transform FindTransformByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (string candidate in GetPathCandidates(path))
        {
            if (transformByPath.TryGetValue(candidate, out Transform direct) &&
                direct != null)
            {
                return direct;
            }

            foreach (GameObject root in Roots)
            {
                if (root == null)
                    continue;

                Transform result = root.transform.Find(candidate);
                if (result != null)
                    return result;

                if (!candidate.StartsWith(root.name + "/", StringComparison.Ordinal))
                    continue;

                string subPath = candidate.Substring(root.name.Length + 1);
                result = root.transform.Find(subPath);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    internal string GetTransformPath(Transform transform)
    {
        if (transform == null)
            return "<null>";

        int id = transform.GetInstanceID();
        if (pathByInstanceId.TryGetValue(id, out string path))
            return path;

        string legacyPath = BuildPath(transform);
        path = legacyPath;
        if (transformByPath.TryGetValue(legacyPath, out Transform existing) &&
            existing != null && existing != transform)
        {
            path = BuildIndexedPath(legacyPath, transform);
        }

        pathByInstanceId[id] = path;
        if (!transformByPath.ContainsKey(path))
            transformByPath.Add(path, transform);

        if (!transformByPath.ContainsKey(legacyPath))
            transformByPath.Add(legacyPath, transform);

        return path;
    }

    private static string[] GetPathCandidates(string path)
    {
        string normalized = path.Trim().Trim('/');
        if (normalized.StartsWith("Main/", StringComparison.Ordinal))
            return new[] { normalized, normalized.Substring("Main/".Length) };

        return new[] { normalized };
    }

    private static string BuildPath(Transform transform)
    {
        var stack = new Stack<string>();
        Transform current = transform;
        while (current != null)
        {
            stack.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", stack);
    }

    private static string BuildIndexedPath(string legacyPath, Transform transform)
    {
        var siblingIndices = new Stack<int>();
        Transform current = transform;
        while (current != null)
        {
            siblingIndices.Push(current.GetSiblingIndex());
            current = current.parent;
        }

        return legacyPath + "|MSFSIDX:" + string.Join(".", siblingIndices);
    }
}
