using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace MoreStandsForShops.Stands.Upgrade;

internal sealed partial class UpgradeStandRerollController
{
    private void ResolveReferences()
    {
        if (buttonRoot == null)
            buttonRoot = FindChildByNamePart(transform, "button");

        if (buttonGrabObject == null)
            buttonGrabObject = GetComponentsInChildren<StaticGrabObject>(true).FirstOrDefault();

        if (buttonColliderRoot == null && buttonGrabObject != null)
            buttonColliderRoot = buttonGrabObject.colliderTransform;

        if (scanBox == null)
            scanBox = FindChildByNameParts(transform, "upgrade", "inside", "box");

        if (scanBox == null)
            scanBox = FindChildByNameParts(transform, "inside", "box");

        if (Plugin.DebugLogs.Value)
        {
            if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo(
                $"[UpgradeStandReroll.References] Resolved. " +
                $"button={NameOrNull(buttonRoot)}, " +
                $"buttonCollider={NameOrNull(buttonColliderRoot)}, " +
                $"scanBox={NameOrNull(scanBox)}, " +
                $"hatch={NameOrNull(hatch)}, " +
                $"compartment={NameOrNull(upgradeCompartment)}, " +
                $"allMeshes={NameOrNull(allMeshesTransform)}.");
        }
    }

    private void CaptureOriginalTransforms()
    {
        if (buttonRoot != null)
        {
            buttonOriginalPosition = buttonRoot.localPosition;
            buttonOriginalRotation = buttonRoot.localRotation;
        }

        if (hatch != null)
        {
            hatchOriginalPosition = hatch.localPosition;
            hatchOriginalScale = hatch.localScale;
        }

        if (upgradeCompartment != null)
            compartmentOriginalRotation = upgradeCompartment.localRotation;

        if (allMeshesTransform != null)
        {
            allMeshesOriginalPosition = allMeshesTransform.localPosition;
            allMeshesOriginalRotation = allMeshesTransform.localRotation;
        }
    }

    private void DisableVanillaButtonNetworking()
    {
        int grabAreaCount = 0;
        int staticGrabCount = 0;
        int photonViewCount = 0;

        foreach (PhysGrabObjectGrabArea area in GetComponentsInChildren<PhysGrabObjectGrabArea>(true))
        {
            area.enabled = false;
            grabAreaCount++;
        }

        foreach (StaticGrabObject grab in GetComponentsInChildren<StaticGrabObject>(true))
        {
            grab.enabled = false;
            staticGrabCount++;
        }

        foreach (PhotonView view in GetComponentsInChildren<PhotonView>(true))
        {
            view.enabled = false;
            photonViewCount++;
        }

        if (Plugin.DebugLogs.Value)
        {
            if (Plugin.DebugLogs.Value) Plugin.Log.LogInfo(
                $"[UpgradeStandReroll.References] Disabled vanilla button networking. " +
                $"grabAreas={grabAreaCount}, staticGrabs={staticGrabCount}, photonViews={photonViewCount}.");
        }
    }

    private static Transform FindChildByNamePart(Transform root, string part)
    {
        string loweredPart = part.ToLowerInvariant();

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.ToLowerInvariant().Contains(loweredPart))
                return child;
        }

        return null;
    }

    private static Transform FindChildByNameParts(Transform root, params string[] parts)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            string name = child.name.ToLowerInvariant();
            bool matched = true;

            foreach (string part in parts)
            {
                if (!name.Contains(part.ToLowerInvariant()))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
                return child;
        }

        return null;
    }

    private static string NameOrNull(Transform transform)
    {
        return transform == null ? "<null>" : transform.name;
    }
}