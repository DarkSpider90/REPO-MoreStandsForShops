using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoreStandsForShops.Utilities;

internal static class ScenePathUtility
{
    public static Transform FindTransformByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string[] candidates = GetPathCandidates(path);
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (string candidate in candidates)
            {
                Transform result = root.transform.Find(candidate);
                if (result != null)
                    return result;

                if (!candidate.StartsWith(root.name + "/", System.StringComparison.Ordinal))
                    continue;

                string subPath = candidate.Substring(root.name.Length + 1);
                result = root.transform.Find(subPath);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    public static string GetTransformPath(Transform transform)
    {
        if (transform == null)
            return "<null>";

        var stack = new Stack<string>();
        Transform current = transform;
        while (current != null)
        {
            stack.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", stack);
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

            if (!target.gameObject.activeSelf && !target.gameObject.activeInHierarchy)
                continue;

            target.gameObject.SetActive(false);
            disabled.Add(GetTransformPath(target));

            if (Plugin.DebugLogs.Value)
                Plugin.Log.LogInfo($"{logPrefix} Disabled preset blocker: {GetTransformPath(target)}");
        }

        return disabled;
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

    private static string[] GetPathCandidates(string path)
    {
        string normalized = path.Trim().Trim('/');
        if (normalized.StartsWith("Main/", System.StringComparison.Ordinal))
            return new[] { normalized, normalized.Substring("Main/".Length) };

        return new[] { normalized };
    }
}
