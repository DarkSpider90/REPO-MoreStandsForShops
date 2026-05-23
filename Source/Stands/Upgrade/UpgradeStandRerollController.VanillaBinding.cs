namespace MoreStandsForShops.Stands.Upgrade;

internal sealed partial class UpgradeStandRerollController
{
    internal void ConfigureFromVanilla(UpgradeStand vanillaStand)
    {
        scanBox = vanillaStand.upgradeInsideBoxCheck;
        buttonGrabObject = vanillaStand.buttonGrabObject;
        buttonRoot = vanillaStand.button;
        buttonColliderRoot = buttonGrabObject != null ? buttonGrabObject.colliderTransform : null;
        hatch = vanillaStand.hatch;
        upgradeCompartment = vanillaStand.upgradeCompartment;
        hatchClosed = vanillaStand.hatchClosed;
        hatchHurtCollider = vanillaStand.hatchHurtCollider;
        rerollCompartmentHurtColliders = vanillaStand.rerollCompartmentHurtColliders;
        allMeshesTransform = vanillaStand.allMeshesTransform;
        interactLocalized = vanillaStand.interactLocalized;
        hatchParticles = vanillaStand.hatchParticles;
        rollingParticles = vanillaStand.rollingParticles;
        buttonPressAnimationCurve = vanillaStand.buttonPressAnimationCurve;
        buttonDenyCurve = vanillaStand.buttonDenyCurve;
        hatchAnimationCurve = vanillaStand.hatchAnimationCurve;
        rollStartShakeCurve = vanillaStand.rollStartShakeCurve;
        rollCurve = vanillaStand.rollCurve;
        rollEndShakeCurve = vanillaStand.rollEndShakeCurve;
        soundButtonPress = vanillaStand.soundButtonPress;
        soundButtonDeny = vanillaStand.soundButtonDeny;
        soundHatchClose = vanillaStand.soundHatchClose;
        soundRollStart = vanillaStand.soundRollStart;
        soundRolling = vanillaStand.soundRolling;
        soundRollEnd = vanillaStand.soundRollEnd;
        soundHatchOpen = vanillaStand.soundHatchOpen;
        soundButtonTwistUp = vanillaStand.soundButtonTwistUp;
        soundButtonTwistDown = vanillaStand.soundButtonTwistDown;
        soundStageBeep = vanillaStand.soundStageBeep;
        soundStateOn = vanillaStand.soundStateOn;
        soundStateOff = vanillaStand.soundStateOff;
        soundRerollStart = vanillaStand.soundRerollStart;
        soundRerollEnd = vanillaStand.soundRerollEnd;
        soundRerollTick = vanillaStand.soundRerollTick;
        soundRerollSettle = vanillaStand.soundRerollSettle;
        soundFinalRollSqueak = vanillaStand.soundFinalRollSqueak;
        soundHatchCloseImpact = vanillaStand.soundHatchCloseImpact;
        soundHatchOpenImpact = vanillaStand.soundHatchOpenImpact;
        buttonRenderer = vanillaStand.buttonRenderer;
        buttonNormalMaterial = vanillaStand.buttonNormalMaterial;
        buttonDenyMaterial = vanillaStand.buttonDenyMaterial;
        buildUpStages = vanillaStand.buildUpStages;
        buildUpLight = vanillaStand.buildUpLight;
        buildUpIntroCurve = vanillaStand.buildUpIntroCurve;
        buildUpOutroCurve = vanillaStand.buildUpOutroCurve;
        buttonRubble = vanillaStand.buttonRubble;
        fireHurtCollider = vanillaStand.fireHurtCollider;
        fireLight = vanillaStand.fireLight;
        particleButtonBreak = vanillaStand.particleButtonBreak;
        particleFireLoop = vanillaStand.particleFireLoop;
        buildUpParticles = vanillaStand.buildUpParticles;
        soundButtonBreak = vanillaStand.soundButtonBreak;
        soundBuildUpLoop = vanillaStand.soundBuildUpLoop;
        flickerFadeLightCurve = vanillaStand.flickerFadeLightCurve;
        meshRotationSpring = vanillaStand.meshRotationSpring;
        meshPositionSpring = vanillaStand.meshPositionSpring;
        buttonRotationSpring = vanillaStand.buttonRotationSpring;
        soundFluorescentLightTurnOff = vanillaStand.soundFluorescentLightTurnOff;
        soundLilButtonFire = vanillaStand.soundLilButtonFire;

        if (Plugin.DebugLogs.Value)
        {
            Plugin.Log.LogInfo(
                $"[UpgradeStandReroll.Binding] Copied vanilla references. " +
                $"vanilla={vanillaStand.name}, scanBox={NameOrNull(scanBox)}, button={NameOrNull(buttonRoot)}, " +
                $"hatch={NameOrNull(hatch)}, compartment={NameOrNull(upgradeCompartment)}, " +
                $"buildUpStages={(buildUpStages == null ? 0 : buildUpStages.Length)}.");
        }
    }
}