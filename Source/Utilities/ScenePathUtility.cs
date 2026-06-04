using System.Collections.Generic;
using UnityEngine;

namespace MoreStandsForShops.Utilities;

internal static class ScenePathUtility
{
    public static Transform FindTransformByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return ShopSceneCache.Current.FindTransformByPath(path);
    }

    public static string GetTransformPath(Transform transform)
    {
        return ShopSceneCache.Current.GetTransformPath(transform);
    }

    public static List<string> DisableExactPaths(IEnumerable<string> paths, string logPrefix)
    {
        var disabled = new List<string>();
        var seen = new HashSet<string>();

        if (paths == null)
            return disabled;

        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
                continue;

            Transform target = FindTransformByPath(path);
            if (target == null)
            {
                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"{logPrefix} Preset disable path not present: {path}");
                continue;
            }

            int disabledCount = DisableTree(target, disabled);

            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"{logPrefix} Disabled preset blocker: {GetTransformPath(target)} (nodes={disabledCount})");
        }

        return disabled;
    }

    private static int DisableTree(Transform root, List<string> disabled)
    {
        if (root == null)
            return 0;

        Transform[] nodes = root.GetComponentsInChildren<Transform>(true);
        int count = 0;

        foreach (Transform node in nodes)
        {
            if (node == null)
                continue;

            if (!node.gameObject.activeSelf && !node.gameObject.activeInHierarchy)
                continue;

            node.gameObject.SetActive(false);
            disabled.Add(GetTransformPath(node));
            count++;
        }

        return count;
    }

    public static bool HasActivePath(IEnumerable<string> paths, out string activePath)
    {
        activePath = null;
        if (paths == null)
            return false;

        foreach (string path in paths)
        {
            Transform target = FindTransformByPath(path);
            if (target == null || !target.gameObject.activeInHierarchy)
                continue;

            activePath = GetTransformPath(target);
            return true;
        }

        return false;
    }

}
