using Photon.Pun;
using System;
using System.Reflection;
using UnityEngine;

namespace MoreStandsForShops.Utilities;

/// <summary>
/// Keeps Photon components copied from vanilla scene props inert. Custom stands are
/// static local visuals whose layout and interaction state are synchronized by
/// ShopLayoutSync and UpgradeStandRerollController; shop items keep their own
/// PhotonViews and are never passed to this helper.
/// </summary>
internal static class StandNetworkSafety
{
    private static readonly FieldInfo RuntimeViewIdField = typeof(PhotonView).GetField(
        "viewIdField",
        BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// Clones a vanilla scene visual without letting its copied scene PhotonView IDs
    /// replace the registrations owned by the original scene objects. The original
    /// IDs are restored synchronously before this method returns.
    /// </summary>
    internal static GameObject CloneVanillaSceneVisual(GameObject source, string context)
    {
        if (source == null)
            return null;

        PhotonView[] sourceViews = source.GetComponentsInChildren<PhotonView>(true);
        int[] sceneViewIds = new int[sourceViews.Length];

        try
        {
            for (int i = 0; i < sourceViews.Length; i++)
            {
                PhotonView view = sourceViews[i];
                if (view == null)
                    continue;

                sceneViewIds[i] = view.sceneViewId;

                // Directly changing sceneViewId does not unregister the live vanilla
                // view. It only prevents the clone's Awake from claiming that ID.
                view.sceneViewId = 0;
            }

            GameObject clone = UnityEngine.Object.Instantiate(source);
            ClearInheritedPhotonViewIdentities(clone, context);
            return clone;
        }
        finally
        {
            for (int i = 0; i < sourceViews.Length; i++)
            {
                PhotonView view = sourceViews[i];
                if (view != null)
                    view.sceneViewId = sceneViewIds[i];
            }
        }
    }

    internal static int DisableInheritedPhotonViews(
        GameObject standRoot,
        string context,
        bool includeChildren = true)
    {
        if (standRoot == null)
            return 0;

        int found = 0;
        int disabled = 0;

        PhotonView[] views = includeChildren
            ? standRoot.GetComponentsInChildren<PhotonView>(true)
            : new[] { standRoot.GetComponent<PhotonView>() };

        foreach (PhotonView view in views)
        {
            if (view == null)
                continue;

            found++;
            ClearInheritedPhotonViewIdentity(view, context);

            if (!view.enabled)
                continue;

            view.enabled = false;
            disabled++;
        }

        if (Plugin.DebugLogs.Value && (found > 0 || disabled > 0))
        {
            Plugin.Log.LogInfo(
                $"[{context}] Inherited vanilla PhotonViews made inert before stand activation: " +
                $"found={found}, disabled={disabled}.");
        }

        return disabled;
    }

    private static void ClearInheritedPhotonViewIdentities(GameObject standRoot, string context)
    {
        if (standRoot == null)
            return;

        foreach (PhotonView view in standRoot.GetComponentsInChildren<PhotonView>(true))
        {
            if (view != null)
                ClearInheritedPhotonViewIdentity(view, context);
        }
    }

    private static void ClearInheritedPhotonViewIdentity(PhotonView view, string context)
    {
        // Never clear this through the PhotonView.ViewID setter. PUN removes the numeric ID from
        // its global dictionary without checking that this clone owns the entry;
        // that could unregister the original vanilla scene object.
        view.sceneViewId = 0;
        view.InstantiationId = 0;
        view.isRuntimeInstantiated = false;

        if (view.ViewID == 0)
            return;

        if (RuntimeViewIdField == null)
        {
            throw new InvalidOperationException(
                $"[{context}] Cannot safely clear an inherited PhotonView identity; " +
                "the PUN runtime field was not found.");
        }

        RuntimeViewIdField.SetValue(view, 0);
    }

    internal static bool HasEnabledInheritedPhotonView(GameObject standRoot)
    {
        if (standRoot == null)
            return false;

        foreach (PhotonView view in standRoot.GetComponentsInChildren<PhotonView>(true))
        {
            if (view != null && view.enabled)
                return true;
        }

        return false;
    }
}
