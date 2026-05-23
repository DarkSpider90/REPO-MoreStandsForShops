using Photon.Pun;
using UnityEngine;

namespace MoreStandsForShops.Stands.Upgrade;

internal sealed partial class UpgradeStandRerollController
{
    
    private void UpdateState(bool buttonFocused)
    {
        switch (state)
        {
            case RerollState.Idle:
                StateIdle(buttonFocused);
                break;
            case RerollState.Holding:
                StateHolding(buttonFocused);
                break;
            case RerollState.Rollback:
                StateRollback(buttonFocused);
                break;
            case RerollState.PressFail:
                StatePressFail();
                break;
            case RerollState.PressSucceed:
                StatePressSucceed();
                break;
            case RerollState.CloseHatch:
                StateCloseHatch();
                break;
            case RerollState.RollStart:
                StateRollStart();
                break;
            case RerollState.Rolling:
                StateRolling();
                break;
            case RerollState.RollEnd:
                StateRollEnd();
                break;
            case RerollState.OpenHatch:
                StateOpenHatch();
                break;
            case RerollState.WaitingForHost:
                StateWaitingForHost();
                break;
            case RerollState.Broken:
                StateBroken();
                break;
        }
    }

    private void StateIdle(bool buttonFocused)
    {
        if (stateStart)
        {
            stateTimerMax = 0f;
            stateTimer = 0f;
            visualOnlyReroll = false;
            buttonAnimationEval = 0f;
            chargeElapsed = 0f;
            buttonRotationTarget = 0f;
            SetButtonPositionPressed(0f);
            SetButtonNormal();
            ResetBuildUpVisuals();
            stateStart = false;
            remoteHoldVisual = false;
            holdRequestSent = false;
            holdVisualBroadcasted = false;
            resumeChargeFromRollback = false;
            holdProgressSyncTimer = 0f;
            rollbackTopStage = 0;
            rollbackCurrentStage = -1;
            rollbackCurrentStageElapsed = 0f;
            rollbackCurrentStageStartEmission = 0f;
            rollbackResumeRequested = false;
        }

        if (isBroken)
        {
            StateSet(RerollState.Broken);
            return;
        }

        if (buttonFocused && activationStartedThisFrame)
        {
            if (!CanAttemptRerollLocally())
            {
                StateSet(RerollState.PressFail);
                return;
            }

            StateSet(RerollState.Holding);
        }
    }
    

    private void StateHolding(bool buttonFocused)
    {
        if (stateStart)
        {
            stateTimerMax = HoldDuration;
            stateTimer = 0f;

            if (resumeChargeFromRollback)
            {
                chargeElapsed = Mathf.Clamp(chargeElapsed, 0f, HoldDuration);
                SyncChargeStageTriggers(chargeElapsed);
                ApplyChargeVisualsSilent(chargeElapsed);

                if (Plugin.DebugLogs.Value)
                    Plugin.Log.LogInfo($"[UpgradeStandReroll.State] Resume charging from rollback. chargeElapsed={chargeElapsed:0.00}.");
            }
            
            else
            {
                chargeElapsed = 0f;
                buttonRotationTarget = 0f;
                ResetChargeStageTriggers();
                ResetBuildUpVisuals();
            }

            resumeChargeFromRollback = false;
            
            if (buildUpLight != null)
                buildUpLight.gameObject.SetActive(true);

            stateStart = false;
            
            if (!remoteHoldVisual)
            {
                if (SemiFunc.IsMultiplayer() && PhotonNetwork.IsMasterClient)
                    BroadcastHoldVisualStart();
                else if (SemiFunc.IsMultiplayer())
                    RequestHostHoldStart();
            }
        }

        if (!remoteHoldVisual && (!activationHeld || activationReleasedThisFrame || !buttonFocused))
        {
            if (PhotonNetwork.IsMasterClient)
                BroadcastHoldVisualStop();
            else
                RequestHostHoldStop();

            StateSet(RerollState.Rollback);
            return;
        }

        chargeElapsed += Time.deltaTime;
        UpdateChargeVisuals(chargeElapsed);
        
        if (SemiFunc.IsMultiplayer() && PhotonNetwork.IsMasterClient)
            BroadcastHoldVisualProgress(force: false);

        if (chargeElapsed >= HoldDuration)
        {
            if (remoteHoldVisual)
            {
                chargeElapsed = HoldDuration;
                UpdateChargeVisuals(chargeElapsed);
                return;
            }

            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                TryStartReroll(visualOnly: false, broadcastVisual: true);
            }
            else if (SemiFunc.IsMultiplayer())
            {
                holdRequestSent = false;
                RequestHostReroll();
                StateSet(RerollState.WaitingForHost);
            }
            else
            {
                StateSet(RerollState.PressFail);
            }
        }
    }
    

    private void StateRollback(bool buttonFocused)
    {
        BroadcastHoldVisualStop();

        if (stateStart)
        {
            rollbackResumeRequested = false;
            rollbackTopStage = Mathf.Clamp(
                Mathf.FloorToInt(chargeElapsed / ChargeStageDuration),
                0,
                Mathf.Max(0, buildUpStages.Length - 1));

            BeginRollbackStage(rollbackTopStage);

            if (Plugin.DebugLogs.Value)
            {
                Plugin.Log.LogInfo(
                    $"[UpgradeStandReroll.State] Stage rollback started. " +
                    $"chargeElapsed={chargeElapsed:0.00}, topStage={rollbackTopStage}.");
            }

            stateTimer = 0f;
            stateTimerMax = 0f;
            stateStart = false;
        }

        if (rollbackCurrentStage < 0)
            return;

        if (!remoteHoldVisual && buttonFocused && activationStartedThisFrame)
            rollbackResumeRequested = true;

        rollbackCurrentStageElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(rollbackCurrentStageElapsed / 0.5f);
        float eased = SemiFunc.Photosensitivity() ? t : Evaluate(buildUpOutroCurve, t);

        ApplyRollbackStageVisuals(eased);

        if (t < 1f)
            return;

        ApplyStageEmission(buildUpStages[rollbackCurrentStage], 0f);

        if (!remoteHoldVisual && rollbackResumeRequested)
        {
            ResumeChargingFromRollback(rollbackCurrentStage);
            return;
        }

        rollbackCurrentStage--;

        if (rollbackCurrentStage < 0)
        {
            chargeElapsed = 0f;
            ResetBuildUpVisuals();
            buttonRotationTarget = 0f;
            StateSet(RerollState.Idle);
            return;
        }

        BeginRollbackStage(rollbackCurrentStage);
    }

    private void StatePressFail()
    {
        if (stateStart)
        {
            stateTimerMax = 0.3f;
            stateTimer = 0f;
            buttonAnimationEval = 0f;
            buttonRotationTarget = 0f;
            SetButtonDeny();
            if (soundButtonDeny != null)
                soundButtonDeny.Play(ButtonSoundPosition);
            ShakeButton();
            stateStart = false;
        }

        buttonAnimationEval = Mathf.Clamp01(buttonAnimationEval + 3f * Time.deltaTime);
        buttonRotationTarget = 22.5f * Evaluate(buttonDenyCurve, buttonAnimationEval);

        if (stateTimer >= stateTimerMax)
            StateSet(isBroken ? RerollState.Broken : RerollState.Idle);
    }

    private void StatePressSucceed()
    {
        if (stateStart)
        {
            stateTimerMax = 0.3f;
            stateTimer = 0f;
            buttonAnimationEval = 0f;
            if (soundButtonPress != null)
                soundButtonPress.Play(ButtonSoundPosition);
            ShakeButton();
            stateStart = false;
        }

        buttonAnimationEval = Mathf.Clamp01(buttonAnimationEval + 8f * Time.deltaTime);
        SetButtonPositionPressed(Evaluate(buttonPressAnimationCurve, buttonAnimationEval));

        if (stateTimer >= stateTimerMax)
            StateSet(RerollState.CloseHatch);
    }
    

    private void StateCloseHatch()
    {
        if (stateStart)
        {
            stateTimerMax = 0.4f;
            stateTimer = 0f;
            hatchAnimationEval = 0f;
            hatchCloseImpactPlayed = false;
            if (hatchParticles != null)
                hatchParticles.Play(true);
            if (soundHatchClose != null)
                soundHatchClose.Play(HatchSoundPosition);
            if (soundRerollStart != null)
                soundRerollStart.Play(transform.position);
            ShakeHatch();
            stateStart = false;
        }

        hatchAnimationEval = Mathf.Clamp01(stateTimer / stateTimerMax);
        ApplyHatchClosed(hatchAnimationEval);

        if (hatchAnimationEval >= 0.2f)
        {
            if (hatchClosed != null)
                hatchClosed.SetActive(true);
            if (hatchHurtCollider != null)
                hatchHurtCollider.SetActive(true);
        }

        if (hatchAnimationEval >= 0.2f && !hatchCloseImpactPlayed)
        {
            hatchCloseImpactPlayed = true;
            if (soundHatchCloseImpact != null)
                soundHatchCloseImpact.Play(HatchSoundPosition);
        }

        if (stateTimer >= stateTimerMax)
            StateSet(RerollState.RollStart);
    }
    

    private void StateRollStart()
    {
        if (stateStart)
        {
            stateTimerMax = 0.3f;
            stateTimer = 0f;
            compartmentAnimationEval = 0f;
            
            if (hatchHurtCollider != null)
                hatchHurtCollider.SetActive(false);
            
            if (soundRollStart != null)
                
                soundRollStart.Play(CompartmentSoundPosition);
            if (!visualOnlyReroll)
                DestroyCachedUpgrades();
            
            ShakeCompartment();
            
            if (meshRotationSpring != null)
                AddSpringVelocity(meshRotationSpring, new Vector3(5f, 0f, 0f));

            if (meshPositionSpring != null)
                AddSpringVelocity(meshPositionSpring, new Vector3(0f, 2f, 0f));

            stateStart = false;
        }

        compartmentAnimationEval = Mathf.Clamp01(stateTimer / stateTimerMax);
        float shake = Evaluate(rollStartShakeCurve, compartmentAnimationEval);
        ApplyCompartmentRotation(shake * 20f);

        if (stateTimer >= stateTimerMax)
            StateSet(RerollState.Rolling);
    }
    

    private void StateRolling()
    {
        if (stateStart)
        {
            stateTimerMax = 0.5f;
            stateTimer = 0f;
            compartmentAnimationEval = 0f;
            rerollTicksPlayed = 0;
            finalRollSqueakPlayed = false;
            if (rerollCompartmentHurtColliders != null)
                rerollCompartmentHurtColliders.SetActive(true);
            if (rollingParticles != null)
                rollingParticles.Play(true);
            if (soundRolling != null)
                soundRolling.Play(CompartmentSoundPosition);
            stateStart = false;
        }

        compartmentAnimationEval = Mathf.Clamp01(stateTimer / stateTimerMax);
        float roll = Evaluate(rollCurve, compartmentAnimationEval);
        ApplyCompartmentRotation(roll * 360f);

        float tickStep = stateTimerMax / 12f;
        while (rerollTicksPlayed < 12 && stateTimer >= rerollTicksPlayed * tickStep)
        {
            if (soundRerollTick != null)
            {
                soundRerollTick.Pitch = rerollTicksPlayed / 3 switch
                {
                    2 => 2f,
                    1 => 1.5f,
                    _ => 1f
                };
                soundRerollTick.Play(CompartmentSoundPosition);
            }

            rerollTicksPlayed++;
        }

        if (compartmentAnimationEval >= 0.9f && !finalRollSqueakPlayed)
        {
            finalRollSqueakPlayed = true;
            if (soundFinalRollSqueak != null)
                soundFinalRollSqueak.Play(CompartmentSoundPosition);
        }

        if (stateTimer >= stateTimerMax)
            StateSet(RerollState.RollEnd);
    }
    

    private void StateRollEnd()
    {
        if (stateStart)
        {
            stateTimerMax = 0.625f;
            stateTimer = 0f;
            compartmentAnimationEval = 0f;
            
            if (rerollCompartmentHurtColliders != null)
                rerollCompartmentHurtColliders.SetActive(false);
            
            if (rollingParticles != null)
                rollingParticles.Stop(true);
            
            if (soundRollEnd != null)
                soundRollEnd.Play(CompartmentSoundPosition);
            
            if (soundRerollSettle != null)
                soundRerollSettle.Play(CompartmentSoundPosition);
            
            ShakeCompartment();
            
            if (meshRotationSpring != null)
                AddSpringVelocity(meshRotationSpring, new Vector3(15f, 0f, 0f));

            if (meshPositionSpring != null)
                AddSpringVelocity(meshPositionSpring, new Vector3(0f, 1f, 0f));
            
            if (maxRerollCount > 0 && rerollCount >= maxRerollCount)
            {
                buildUpActive = true;
                buildUpTimer = 0f;

                if (!SemiFunc.Photosensitivity() && buildUpParticles != null)
                    buildUpParticles.Play(true);
            }
            
            stateStart = false;
        }

        compartmentAnimationEval = Mathf.Clamp01(stateTimer / stateTimerMax);
        float shake = Evaluate(rollEndShakeCurve, compartmentAnimationEval);
        ApplyCompartmentRotation(shake * 15f);

        if (stateTimer >= stateTimerMax)
            StateSet(RerollState.OpenHatch);
    }
    

    private void StateOpenHatch()
    {
        const float hatchOpenDuration = 0.4f;
        const float buttonPopDuration = 0.2f;
        const float buttonResetDelay = 0.2f;
        const float waitAfterOpen = 1.2f;

        if (stateStart)
        {
            stateTimerMax = hatchOpenDuration + buttonPopDuration + buttonResetDelay + waitAfterOpen;
            stateTimer = 0f;
            hatchAnimationEval = 1f;
            hatchOpenImpactPlayed = false;
            if (!visualOnlyReroll)
                SpawnPendingReplacements();
            if (hatchParticles != null)
                hatchParticles.Play(true);
            if (soundHatchOpen != null)
                soundHatchOpen.Play(HatchSoundPosition);
            if (soundRerollEnd != null)
                soundRerollEnd.Play(transform.position);
            ShakeHatch();
            stateStart = false;
        }

        float hatchT = Mathf.Clamp01(stateTimer / hatchOpenDuration);
        hatchAnimationEval = 1f - hatchT;
        ApplyHatchClosed(hatchAnimationEval);

        if (hatchAnimationEval <= 0.7f && hatchClosed != null)
            hatchClosed.SetActive(false);

        if (hatchAnimationEval <= 0.8f && !hatchOpenImpactPlayed)
        {
            hatchOpenImpactPlayed = true;
            if (soundHatchOpenImpact != null)
                soundHatchOpenImpact.Play(HatchSoundPosition);
        }

        if (stateTimer >= hatchOpenDuration)
        {
            float buttonT = Mathf.Clamp01((stateTimer - hatchOpenDuration) / buttonPopDuration);
            SetButtonPositionPressed(1f - buttonT);
            FadeBuildUpVisuals(1f - buttonT);
        }

        if (stateTimer >= hatchOpenDuration + buttonPopDuration + buttonResetDelay)
            buttonRotationTarget = 0f;

        if (stateTimer >= stateTimerMax)
        {
            cachedUpgrades.Clear();
            pendingReplacements.Clear();

            if (!visualOnlyReroll && maxRerollCount > 0 && rerollCount >= maxRerollCount)
            {
                BreakButton();
                BroadcastBroken();
                StateSet(RerollState.Broken);
            }
            else
            {
                StateSet(RerollState.Idle);
            }
        }
    }
    

    private void StateWaitingForHost()
    {
        if (stateStart)
        {
            stateTimerMax = 2f;
            stateTimer = 0f;
            stateStart = false;
        }

        if (stateTimer >= stateTimerMax)
            StateSet(RerollState.Idle);
    }
    

    private void StateBroken()
    {
        if (stateStart)
        {
            BreakButton();
            stateTimer = 0f;
            stateTimerMax = 0f;
            stateStart = false;
        }
    }
    

    private void StateSet(RerollState nextState)
    {
        if (Plugin.DebugLogs.Value && state != nextState)
            Plugin.Log.LogInfo($"[UpgradeStandReroll.State] {state} -> {nextState}.");

        state = nextState;
        stateStart = true;
    }
    
}