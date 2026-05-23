using System.Reflection;
using UnityEngine;

namespace MoreStandsForShops.Stands.Upgrade;

internal sealed partial class UpgradeStandRerollController
{
    private static readonly FieldInfo SpringQuaternionVelocityField =
        typeof(SpringQuaternion).GetField("springVelocity", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo SpringVector3VelocityField =
        typeof(SpringVector3).GetField("springVelocity", BindingFlags.Instance | BindingFlags.NonPublic);
    
    private Vector3 ButtonSoundPosition => buttonRoot != null ? buttonRoot.position : transform.position;
    private Vector3 HatchSoundPosition => hatch != null ? hatch.position : transform.position;
    private Vector3 CompartmentSoundPosition => upgradeCompartment != null ? upgradeCompartment.position : transform.position;


    private float HoldDuration => buildUpStages == null || buildUpStages.Length == 0
        ? 1.7f
        : buildUpStages.Length * BuildUpStageDuration + BuildUpFinalHoldDuration;
    
    
    private float ChargeStageDuration => BuildUpStageDuration;

    
    private float ComputeChargingButtonTarget(float elapsed)
    {
        int stageCount = Mathf.Max(1, buildUpStages.Length);
        return Mathf.Clamp(Mathf.FloorToInt(elapsed / ChargeStageDuration) + 1, 0, stageCount) * buttonStageRotationAngle;
    }
    

    private void SyncChargeStageTriggers(float elapsed)
    {
        if (chargeStageTriggered == null || chargeStageTriggered.Length != buildUpStages.Length)
            chargeStageTriggered = new bool[buildUpStages.Length];

        for (int i = 0; i < chargeStageTriggered.Length; i++)
            chargeStageTriggered[i] = elapsed >= i * ChargeStageDuration;
    }
    

    private void ApplyChargeVisualsSilent(float elapsed)
    {
        if (buildUpStages == null || buildUpStages.Length == 0)
            return;

        buttonRotationTarget = ComputeChargingButtonTarget(elapsed);

        for (int i = 0; i < buildUpStages.Length; i++)
        {
            float stageStart = i * ChargeStageDuration;
            float localT = Mathf.Clamp01((elapsed - stageStart) / ChargeStageDuration);
            float value = SemiFunc.Photosensitivity() ? localT : Evaluate(buildUpIntroCurve, localT);
            ApplyStageEmission(buildUpStages[i], value);
        }

        UpdateBuildUpLight(elapsed);
    }
    
    
    private void BeginRollbackStage(int stageIndex)
    {
        if (buildUpStages == null || buildUpStages.Length == 0)
        {
            rollbackCurrentStage = -1;
            return;
        }

        rollbackCurrentStage = Mathf.Clamp(stageIndex, 0, buildUpStages.Length - 1);
        rollbackCurrentStageElapsed = 0f;

        UpgradeStand.BuildUpStage currentStage = buildUpStages[rollbackCurrentStage];

        rollbackCurrentStageStartEmission =
            rollbackCurrentStage == rollbackTopStage
                ? Mathf.Clamp01(GetStageIntroValue(rollbackCurrentStage, chargeElapsed))
                : 1f;

        if (buildUpLight != null)
        {
            rollbackCurrentStageStartLightIntensity = buildUpLight.intensity;
            rollbackCurrentStageStartLightColor = buildUpLight.color;
        }
        else
        {
            rollbackCurrentStageStartLightIntensity = 0f;
            rollbackCurrentStageStartLightColor = Color.black;
        }

        if (rollbackCurrentStage == 0)
        {
            rollbackCurrentStageEndLightIntensity = 0f;
            rollbackCurrentStageEndLightColor = currentStage.stageColor;
        }
        else
        {
            UpgradeStand.BuildUpStage previousStage = buildUpStages[rollbackCurrentStage - 1];
            rollbackCurrentStageEndLightIntensity = previousStage.lightIntensity;
            rollbackCurrentStageEndLightColor = previousStage.stageColor;
        }

        buttonRotationTarget = rollbackCurrentStage * buttonStageRotationAngle;

        if (soundStageBeep != null)
        {
            soundStageBeep.Pitch = Mathf.Lerp(
                0.8f,
                1.4f,
                rollbackCurrentStage / Mathf.Max(1f, buildUpStages.Length - 1f));

            soundStageBeep.Play(transform.position);
        }

        if (soundStateOff != null)
            soundStateOff.Play(transform.position);

        if (soundButtonTwistDown != null)
        {
            soundButtonTwistDown.Pitch = 1f + rollbackCurrentStage * 0.2f;
            soundButtonTwistDown.Play(ButtonSoundPosition);
        }
    }
    

    private void ApplyRollbackStageVisuals(float eased)
    {
        if (rollbackCurrentStage < 0 || buildUpStages == null || rollbackCurrentStage >= buildUpStages.Length)
            return;

        float remaining = 1f - Mathf.Clamp01(eased);

        ApplyStageEmission(
            buildUpStages[rollbackCurrentStage],
            rollbackCurrentStageStartEmission * remaining);

        if (buildUpLight != null)
        {
            buildUpLight.intensity = Mathf.Lerp(
                rollbackCurrentStageEndLightIntensity,
                rollbackCurrentStageStartLightIntensity,
                remaining);

            buildUpLight.color = Color.Lerp(
                rollbackCurrentStageEndLightColor,
                rollbackCurrentStageStartLightColor,
                remaining);
        }

        chargeElapsed =
            rollbackCurrentStage * ChargeStageDuration +
            ChargeStageDuration * rollbackCurrentStageStartEmission * remaining;
    }
    

    private float GetStageIntroValue(int stageIndex, float elapsed)
    {
        float stageStart = stageIndex * ChargeStageDuration;
        if (elapsed < stageStart)
            return 0f;

        float localT = Mathf.Clamp01((elapsed - stageStart) / ChargeStageDuration);
        return SemiFunc.Photosensitivity() ? localT : Evaluate(buildUpIntroCurve, localT);
    }
    

    private void ResumeChargingFromRollback(int stageJustRolledBack)
    {
        chargeElapsed = stageJustRolledBack * ChargeStageDuration;

        if (chargeStageTriggered == null || chargeStageTriggered.Length != buildUpStages.Length)
            chargeStageTriggered = new bool[buildUpStages.Length];

        for (int i = 0; i < chargeStageTriggered.Length; i++)
            chargeStageTriggered[i] = i < stageJustRolledBack;

        rollbackResumeRequested = false;
        resumeChargeFromRollback = true;
        StateSet(RerollState.Holding);

        if (Plugin.DebugLogs.Value)
        {
            Plugin.Log.LogInfo(
                $"[UpgradeStandReroll.State] Resume from rollback stage={stageJustRolledBack}, " +
                $"chargeElapsed={chargeElapsed:0.00}.");
        }
    }
    
    
    private void ResetChargeStageTriggers()
    {
        if (chargeStageTriggered == null || chargeStageTriggered.Length != buildUpStages.Length)
            chargeStageTriggered = new bool[buildUpStages.Length];

        for (int i = 0; i < chargeStageTriggered.Length; i++)
            chargeStageTriggered[i] = false;
    }
    

    private void UpdateChargeVisuals(float elapsed)
    {
        buttonRotationTarget = ComputeChargingButtonTarget(elapsed);
        
        for (int i = 0; i < buildUpStages.Length; i++)
        {
            float stageStart = i * BuildUpStageDuration;
            if (elapsed < stageStart)
                continue;

            if (!chargeStageTriggered[i])
            {
                chargeStageTriggered[i] = true;
                if (soundStageBeep != null)
                {
                    soundStageBeep.Pitch = Mathf.Lerp(0.8f, 1.4f, i / Mathf.Max(1f, buildUpStages.Length - 1f));
                    soundStageBeep.Play(transform.position);
                }

                if (soundStateOn != null)
                    soundStateOn.Play(transform.position);

                if (soundButtonTwistUp != null)
                {
                    soundButtonTwistUp.Pitch = 1f + i * 0.2f;
                    soundButtonTwistUp.Play(ButtonSoundPosition);
                }
            }

            float localT = Mathf.Clamp01((elapsed - stageStart) / BuildUpStageDuration);
            ApplyStageEmission(buildUpStages[i], Evaluate(buildUpIntroCurve, localT));
        }

        UpdateBuildUpLight(elapsed);
    }
    
    
    private void FadeBuildUpVisuals(float value)
    {
        if (buildUpStages != null)
        {
            foreach (UpgradeStand.BuildUpStage stageInfo in buildUpStages)
                ApplyStageEmission(stageInfo, value);
        }

        if (buildUpLight != null)
        {
            buildUpLight.intensity = Mathf.Lerp(0f, 1f, Mathf.Clamp01(value));
            if (value <= 0f)
                buildUpLight.gameObject.SetActive(false);
        }
    }
    

    private void ApplyStageEmission(UpgradeStand.BuildUpStage stageInfo, float value)
    {
        if (stageInfo?.emissionMeshes == null)
            return;

        Color color = Color.Lerp(Color.black, stageInfo.stageColor, Mathf.Clamp01(value));
        foreach (MeshRenderer meshRenderer in stageInfo.emissionMeshes)
        {
            if (meshRenderer == null)
                continue;

            Material material = meshRenderer.material;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color);
        }
    }
    

    private void UpdateBuildUpLight(float elapsed)
    {
        if (buildUpLight == null || buildUpStages == null || buildUpStages.Length == 0)
            return;

        buildUpLight.gameObject.SetActive(true);
        int index = Mathf.Clamp(Mathf.FloorToInt(elapsed / BuildUpStageDuration), 0, buildUpStages.Length - 1);
        buildUpLight.intensity = buildUpStages[index].lightIntensity;
        buildUpLight.color = buildUpStages[index].stageColor;
    }
    

    private void ResetBuildUpVisuals()
    {
        if (buildUpStages != null)
        {
            foreach (UpgradeStand.BuildUpStage stageInfo in buildUpStages)
                ApplyStageEmission(stageInfo, 0f);
        }

        if (buildUpLight != null)
        {
            buildUpLight.intensity = 0f;
            buildUpLight.gameObject.SetActive(false);
        }

        if (buildUpParticles != null)
            buildUpParticles.Stop(true);
    }


    private void StartBreakBuildUpVisual()
    {
        if (isBroken)
            return;

        buildUpActive = true;
        buildUpTimer = 0f;

        if (!SemiFunc.Photosensitivity() && buildUpParticles != null)
            buildUpParticles.Play(true);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo("[UpgradeStandReroll.Visuals] Break build-up visual started.");
    }
    

    private void UpdateBuildUpLoop()
    {
        if (!buildUpActive || soundBuildUpLoop == null)
            return;

        buildUpTimer += Time.deltaTime;
        float t = Mathf.Clamp01(buildUpTimer / 1.025f);
        float pitch = Mathf.Lerp(1f, 4f, t);
        float volume = Mathf.Lerp(0f, 0.6f, t);

        soundBuildUpLoop.PlayLoop(true, 100f, 100f, pitch, volume);
    }
    

    private void ApplyButtonRotation()
    {
        if (buttonRoot == null)
            return;

        buttonRoot.localRotation = buttonOriginalRotation * Quaternion.Euler(0f, buttonRotationAngle, 0f);
    }
    
    
    private void UpdateButtonRotationSpring()
    {
        if (buttonRotationSpring == null)
        {
            buttonRotationAngle = Mathf.Lerp(buttonRotationAngle, buttonRotationTarget, Time.deltaTime * 14f);
            return;
        }

        buttonRotationAngle = SemiFunc.SpringFloatGet(buttonRotationSpring, buttonRotationTarget);
    }
    
    
    private void UpdateMeshSprings()
    {
        if (allMeshesTransform == null || meshRotationSpring == null || meshPositionSpring == null)
            return;

        if (state is RerollState.RollStart or RerollState.Rolling or RerollState.RollEnd)
        {
            float rotationShakeScale = Time.deltaTime * 10f;
            float positionShakeScale = Time.deltaTime * 2f;

            AddSpringVelocity(meshRotationSpring, new Vector3(
                Random.Range(-4f, 4f),
                Random.Range(-4f, 4f),
                Random.Range(-4f, 4f)) * rotationShakeScale);

            AddSpringVelocity(meshPositionSpring, new Vector3(
                Random.Range(-0.8f, 0.8f),
                Random.Range(-0.8f, 0.8f),
                Random.Range(-0.8f, 0.8f)) * positionShakeScale);
        }

        allMeshesTransform.localRotation = SemiFunc.SpringQuaternionGet(meshRotationSpring, allMeshesOriginalRotation);
        allMeshesTransform.localPosition = SemiFunc.SpringVector3Get(meshPositionSpring, allMeshesOriginalPosition);
    }
    
    
    private static void AddSpringVelocity(SpringQuaternion spring, Vector3 delta)
    {
        if (spring == null || SpringQuaternionVelocityField == null)
            return;

        Vector3 current = (Vector3)SpringQuaternionVelocityField.GetValue(spring);
        SpringQuaternionVelocityField.SetValue(spring, current + delta);
    }
    

    private static void AddSpringVelocity(SpringVector3 spring, Vector3 delta)
    {
        if (spring == null || SpringVector3VelocityField == null)
            return;

        Vector3 current = (Vector3)SpringVector3VelocityField.GetValue(spring);
        SpringVector3VelocityField.SetValue(spring, current + delta);
    }
    
    
    private void SetButtonPositionPressed(float value)
    {
        if (buttonRoot == null)
            return;

        buttonRoot.localPosition = new Vector3(
            buttonOriginalPosition.x,
            buttonOriginalPosition.y - 0.06f * Mathf.Clamp01(value),
            buttonOriginalPosition.z);
    }
    

    private void ApplyHatchClosed(float closedValue)
    {
        if (hatch == null)
            return;

        float t = Evaluate(hatchAnimationCurve, Mathf.Clamp01(closedValue));
        hatch.localPosition = new Vector3(hatchOriginalPosition.x, Mathf.Lerp(-0.714f, -0.146f, t), hatchOriginalPosition.z);
        hatch.localScale = new Vector3(hatchOriginalScale.x, Mathf.Lerp(0.655f, 1f, t), hatchOriginalScale.z);
    }
    

    private void ApplyCompartmentRotation(float xAngle)
    {
        if (upgradeCompartment != null)
            upgradeCompartment.localRotation = compartmentOriginalRotation * Quaternion.Euler(xAngle, 0f, 0f);
    }
    

    private void BreakButton()
    {
        isBroken = true;
        
        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo("[UpgradeStandReroll.Visuals] Button broken visual activated.");
        
        if (particleButtonBreak != null)
            particleButtonBreak.Play(true);
        
        if (particleFireLoop != null)
            particleFireLoop.Play(true);
        
        if (soundButtonBreak != null)
            soundButtonBreak.Play(ButtonSoundPosition);
        
        buildUpActive = false;
        if (soundBuildUpLoop != null)
            soundBuildUpLoop.Stop();

        if (buttonRenderer != null)
            buttonRenderer.enabled = false;
        
        fireActive = true;
        fireLightFadeInTimer = 0f;
        firePerlinOffsetX = Random.Range(0f, 100f);
        firePerlinOffsetY = Random.Range(0f, 100f);

        if (buttonRoot != null && (buttonRubble == null || !buttonRubble.transform.IsChildOf(buttonRoot)))
            buttonRoot.gameObject.SetActive(false);

        if (buttonGrabObject != null)
        {
            if (buttonRubble == null || !buttonRubble.transform.IsChildOf(buttonGrabObject.transform))
                buttonGrabObject.gameObject.SetActive(false);
            else
                buttonGrabObject.enabled = false;
        }
        
        if (buttonRubble != null)
            buttonRubble.SetActive(true);
        
        if (fireHurtCollider != null)
            fireHurtCollider.SetActive(true);
        
        if (fireLight != null)
        {
            fireLight.gameObject.SetActive(true);
            fireLight.enabled = true;
            fireLight.intensity = 1f;
        }

        ResetBuildUpVisuals();
    }
    
    
    private void UpdateFire()
    {
        if (!fireActive)
            return;

        if (soundLilButtonFire != null)
            soundLilButtonFire.PlayLoop(true, 3f, 3f);

        if (fireLight != null)
        {
            if (fireLightFadeInTimer < 0.35f)
                fireLightFadeInTimer += Time.deltaTime;

            float fade = Mathf.Clamp01(fireLightFadeInTimer / 0.35f);
            float flicker = Mathf.PerlinNoise(Time.time * 9f + firePerlinOffsetX, firePerlinOffsetY);
            fireLight.intensity = Mathf.Lerp(1f, 1.6f, flicker) * fade;
        }
    }
    

    private void SetButtonNormal()
    {
        if (buttonRenderer != null && buttonNormalMaterial != null)
        {
            buttonRenderer.enabled = true;
            buttonRenderer.material = buttonNormalMaterial;
        }
    }
    

    private void SetButtonDeny()
    {
        if (buttonRenderer != null && buttonDenyMaterial != null)
            buttonRenderer.material = buttonDenyMaterial;
    }
    

    private void ShakeButton()
    {
        if (GameDirector.instance == null || buttonRoot == null)
            return;

        GameDirector.instance.CameraImpact.ShakeDistance(2f, 3f, 8f, buttonRoot.position, 0.1f);
        GameDirector.instance.CameraShake.ShakeDistance(2f, 3f, 8f, buttonRoot.position, 0.1f);
    }
    

    private void ShakeHatch()
    {
        if (GameDirector.instance == null || hatch == null)
            return;

        GameDirector.instance.CameraImpact.ShakeDistance(3f, 3f, 8f, hatch.position, 0.1f);
        GameDirector.instance.CameraShake.ShakeDistance(3f, 3f, 8f, hatch.position, 0.1f);
    }
    

    private void ShakeCompartment()
    {
        if (GameDirector.instance == null || upgradeCompartment == null)
            return;

        GameDirector.instance.CameraImpact.ShakeDistance(3f, 3f, 8f, upgradeCompartment.position, 0.1f);
        GameDirector.instance.CameraShake.ShakeDistance(3f, 3f, 8f, upgradeCompartment.position, 0.1f);
    }
    

    private static float Evaluate(AnimationCurve curve, float value)
    {
        return curve == null ? Mathf.Clamp01(value) : curve.Evaluate(Mathf.Clamp01(value));
    }
    
}
