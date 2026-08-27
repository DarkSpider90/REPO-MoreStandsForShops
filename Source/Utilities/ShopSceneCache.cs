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

        ItemVolumes = Resources.FindObjectsOfTypeAll<ItemVolume>()
            .Where(volume => volume != null && volume.gameObject.activeInHierarchy)
            .Where(volume => volume.gameObject.scene == scene)
            .ToArray();

        Renderers = Resources.FindObjectsOfTypeAll<Renderer>()
            .Where(renderer => renderer != null && renderer.gameObject.activeInHierarchy)
            .Where(renderer => renderer.gameObject.scene == scene)
            .ToArray();

        foreach (Transform transform in Transforms)
        {
            string path = BuildPath(transform);
            pathByInstanceId[transform.GetInstanceID()] = path;

            if (!transformByPath.ContainsKey(path))
                transformByPath.Add(path, transform);
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
                direct != null &&
                direct.gameObject.activeInHierarchy)
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

        path = BuildPath(transform);
        pathByInstanceId[id] = path;
        if (!transformByPath.ContainsKey(path))
            transformByPath.Add(path, transform);

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
}
