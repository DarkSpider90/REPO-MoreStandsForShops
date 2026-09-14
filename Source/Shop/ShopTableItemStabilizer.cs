using System.Collections;
using System.Reflection;
using Photon.Pun;
using UnityEngine;

namespace MoreStandsForShops.Shop;

/// <summary>
/// Keeps a freshly displayed table item upright and inside its authored slot until
/// the first real grab. Only the physics authority applies constraints; clients
/// continue to follow the vanilla PhotonTransformView.
/// </summary>
internal sealed class ShopTableItemStabilizer : MonoBehaviour
{
    private const RigidbodyConstraints DisplayConstraints =
        RigidbodyConstraints.FreezePositionX |
        RigidbodyConstraints.FreezePositionZ |
        RigidbodyConstraints.FreezeRotation;

    private static readonly FieldInfo HasNeverBeenGrabbedField = typeof(PhysGrabObject).GetField(
        "hasNeverBeenGrabbed",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private PhysGrabObject _grabObject;
    private Rigidbody _body;
    private int _viewId;
    private RigidbodyConstraints _originalConstraints;
    private bool _initialized;
    private bool _constraintsApplied;
    private bool _released;

    internal int ViewId => _viewId;
    internal bool IsReleased => _released;

    internal void Initialize(PhysGrabObject grabObject, PhotonView photonView)
    {
        _grabObject = grabObject;
        _body = grabObject != null && grabObject.rb != null
            ? grabObject.rb
            : GetComponent<Rigidbody>();
        _viewId = photonView != null ? photonView.ViewID : 0;
        _initialized = true;
    }

    private IEnumerator Start()
    {
        // Let every vanilla Start method finish first. PhysGrabObject keeps a new
        // room object kinematic for another 0.1 s, so the constraints are installed
        // before gravity is enabled without racing ItemAttributes' pivot correction.
        yield return null;

        if (!_initialized || _released || _grabObject == null || _body == null ||
            !SemiFunc.IsMasterClientOrSingleplayer() || !SemiFunc.RunIsShop())
        {
            bool shouldPublishRemoval = SemiFunc.IsMasterClientOrSingleplayer() && SemiFunc.RunIsShop();
            Release(publishChange: shouldPublishRemoval);
            yield break;
        }

        if (IsBeingGrabbed(_grabObject))
        {
            Release(publishChange: true);
            yield break;
        }

        _originalConstraints = _body.constraints;
        _body.constraints = _originalConstraints | DisplayConstraints;
        _constraintsApplied = true;

        if (!_body.isKinematic)
        {
            Vector3 velocity = _body.velocity;
            _body.velocity = new Vector3(0f, velocity.y, 0f);
            _body.angularVelocity = Vector3.zero;
        }

        if (Plugin.DebugLogs.Value)
        {
            Plugin.Log.LogInfo(
                $"[TableItemStabilizer] Stabilized {name}: view={_viewId}, " +
                $"original={_originalConstraints}, applied={_body.constraints}.");
        }
    }

    private void FixedUpdate()
    {
        if (!_constraintsApplied || _released)
            return;

        if (!SemiFunc.IsMasterClientOrSingleplayer())
        {
            Release(publishChange: false);
            return;
        }

        if (IsBeingGrabbed(_grabObject))
            Release(publishChange: true);
    }

    internal void Release(bool publishChange)
    {
        if (_released)
            return;

        RestoreOriginalConstraints();
        _released = true;

        if (publishChange)
            ShopTableItemPlacementController.NoteStabilizerReleased(_viewId);
        else
            ShopTableItemPlacementController.ForgetStabilizer(this);

        Destroy(this);
    }

    private void OnDestroy()
    {
        RestoreOriginalConstraints();

        if (!_released)
            ShopTableItemPlacementController.NoteStabilizerReleased(_viewId);

        ShopTableItemPlacementController.ForgetStabilizer(this);

        _released = true;
    }

    private void RestoreOriginalConstraints()
    {
        if (!_constraintsApplied || _body == null)
            return;

        _body.constraints = _originalConstraints;
        _constraintsApplied = false;

        if (!_body.isKinematic)
            _body.WakeUp();

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[TableItemStabilizer] Released {name}: view={_viewId}.");
    }

    internal static bool TryWasNeverGrabbed(PhysGrabObject grabObject, out bool neverGrabbed)
    {
        neverGrabbed = false;
        if (grabObject == null || HasNeverBeenGrabbedField == null)
            return false;

        object value = HasNeverBeenGrabbedField.GetValue(grabObject);
        if (value is not bool flag)
            return false;

        neverGrabbed = flag;
        return true;
    }

    internal static bool IsBeingGrabbed(PhysGrabObject grabObject)
    {
        return grabObject != null &&
               (grabObject.grabbed ||
                grabObject.grabbedLocal ||
                (grabObject.playerGrabbing != null && grabObject.playerGrabbing.Count > 0));
    }
}
