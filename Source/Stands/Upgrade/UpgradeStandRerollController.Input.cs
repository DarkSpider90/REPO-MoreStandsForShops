using System.Linq;
using UnityEngine;

namespace MoreStandsForShops.Stands.Upgrade;

internal sealed partial class UpgradeStandRerollController
{
    private void ReadActivationInput()
    {
        activationStartedThisFrame = false;
        activationReleasedThisFrame = false;

        if (InputManager.instance == null)
            return;

        if (InputManager.instance.KeyDown(InputKey.Grab) || InputManager.instance.KeyDown(InputKey.Interact))
        {
            activationHeld = true;
            activationStartedThisFrame = true;
        }

        if (InputManager.instance.KeyUp(InputKey.Grab) || InputManager.instance.KeyUp(InputKey.Interact))
        {
            activationHeld = false;
            activationReleasedThisFrame = true;
        }
    }

    private void UpdateHover(bool buttonFocused)
    {
        if (!buttonFocused || isBroken || state is not (RerollState.Idle or RerollState.Holding or RerollState.Rollback))
            return;

        if (Aim.instance != null)
            Aim.instance.SetState(state == RerollState.Holding ? Aim.State.Grab : Aim.State.Grabbable);
        
        string text = BuildHoverText();
        if (buttonGrabObject != null)
        {
            buttonGrabObject.hoverText = text;
            buttonGrabObject.ShowHoverText();
        }
        else if (ItemInfoUI.instance != null)
        {
            ItemInfoUI.instance.ItemInfoText(null, text);
        }
    }

    private string BuildHoverText()
    {
        string cost = "<color=#FF0000>-" + SemiFunc.DollarGetString(RerollCost) + "k</color>";
        if (interactLocalized != null)
        {
            return interactLocalized.GetLocalizedString(new object[1]
            {
                new { cost }
            });
        }

        return $"Reroll upgrades {cost}";
    }

    private bool IsLocalPlayerLookingAtButton()
    {
        PlayerAvatar player = SemiFunc.PlayerAvatarLocal();
        if (player == null || player.localCamera == null)
            return false;

        Transform camera = player.localCamera.GetOverrideTransform();
        if (camera == null)
            return false;

        Ray ray = new(camera.position, camera.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, ButtonUseDistance, ~0, QueryTriggerInteraction.Collide) &&
            IsButtonTarget(hit.transform))
        {
            return true;
        }

        int hitCount = Physics.SphereCastNonAlloc(
            ray,
            ButtonCastRadius,
            buttonCastHits,
            ButtonUseDistance,
            ~0,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit sphereHit = buttonCastHits[i];
            if (sphereHit.transform == null)
                continue;

            if (IsButtonTarget(sphereHit.transform))
                return true;
        }

        return false;
    }

    private bool IsButtonTarget(Transform hitTransform)
    {
        if (hitTransform == null)
            return false;

        if (buttonRoot != null && (hitTransform == buttonRoot || hitTransform.IsChildOf(buttonRoot)))
            return true;

        if (buttonColliderRoot != null && (hitTransform == buttonColliderRoot || hitTransform.IsChildOf(buttonColliderRoot)))
            return true;

        StaticGrabObject staticGrab = hitTransform.GetComponentInParent<StaticGrabObject>();
        return staticGrab != null && staticGrab == buttonGrabObject;
    }
}
