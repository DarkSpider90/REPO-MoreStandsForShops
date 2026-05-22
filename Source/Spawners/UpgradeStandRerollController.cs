using System.Collections.Generic;
using System.Linq;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using System.Reflection;

namespace MoreStandsForShops.Spawners;

internal sealed class UpgradeStandRerollController : MonoBehaviour, IOnEventCallback
{
    private enum RerollState
    {
        Idle,
        Holding,
        Rollback,
        PressFail,
        PressSucceed,
        CloseHatch,
        RollStart,
        Rolling,
        RollEnd,
        OpenHatch,
        WaitingForHost,
        Broken
    }
    
    private static readonly FieldInfo SpringQuaternionVelocityField =
        typeof(SpringQuaternion).GetField("springVelocity", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo SpringVector3VelocityField =
        typeof(SpringVector3).GetField("springVelocity", BindingFlags.Instance | BindingFlags.NonPublic);

    private const byte RerollRequestEvent = 91;
    private const byte RerollVisualEvent = 92;
    private const byte BrokenVisualEvent = 93;

    private const float ButtonUseDistance = 3.25f;
    private const float ButtonCastRadius = 0.06f;
    private const float BuildUpStageDuration = 0.3f;
    private const float BuildUpFinalHoldDuration = 0.5f;

    [SerializeField] private Transform scanBox;
    [SerializeField] private Transform buttonRoot;

    [SerializeField] private StaticGrabObject buttonGrabObject;
    [SerializeField] private Transform buttonColliderRoot;
    [SerializeField] private Transform hatch;
    [SerializeField] private Transform upgradeCompartment;
    [SerializeField] private Transform allMeshesTransform;
    [SerializeField] private GameObject hatchClosed;
    [SerializeField] private GameObject hatchHurtCollider;
    [SerializeField] private GameObject rerollCompartmentHurtColliders;
    [SerializeField] private GameObject buttonRubble;
    [SerializeField] private GameObject fireHurtCollider;
    [SerializeField] private Light fireLight;
    [SerializeField] private Light buildUpLight;
    [SerializeField] private ParticleSystem hatchParticles;
    [SerializeField] private ParticleSystem rollingParticles;
    [SerializeField] private ParticleSystem particleButtonBreak;
    [SerializeField] private ParticleSystem particleFireLoop;
    [SerializeField] private ParticleSystem buildUpParticles;
    [SerializeField] private MeshRenderer buttonRenderer;
    [SerializeField] private Material buttonNormalMaterial;
    [SerializeField] private Material buttonDenyMaterial;
    [SerializeField] private LocalizedAsset interactLocalized;
    [SerializeField] private AnimationCurve buttonPressAnimationCurve;
    [SerializeField] private AnimationCurve buttonDenyCurve;
    [SerializeField] private AnimationCurve hatchAnimationCurve;
    [SerializeField] private AnimationCurve rollStartShakeCurve;
    [SerializeField] private AnimationCurve rollCurve;
    [SerializeField] private AnimationCurve rollEndShakeCurve;
    [SerializeField] private AnimationCurve buildUpIntroCurve;
    [SerializeField] private AnimationCurve buildUpOutroCurve;
    [SerializeField] private UpgradeStand.BuildUpStage[] buildUpStages;
    [SerializeField] private Sound soundButtonPress;
    [SerializeField] private Sound soundButtonDeny;
    [SerializeField] private Sound soundHatchClose;
    [SerializeField] private Sound soundRollStart;
    [SerializeField] private Sound soundRolling;
    [SerializeField] private Sound soundRollEnd;
    [SerializeField] private Sound soundHatchOpen;
    [SerializeField] private Sound soundButtonTwistUp;
    [SerializeField] private Sound soundButtonTwistDown;
    [SerializeField] private Sound soundStageBeep;
    [SerializeField] private Sound soundStateOn;
    [SerializeField] private Sound soundStateOff;
    [SerializeField] private Sound soundRerollStart;
    [SerializeField] private Sound soundRerollEnd;
    [SerializeField] private Sound soundRerollTick;
    [SerializeField] private Sound soundRerollSettle;
    [SerializeField] private Sound soundFinalRollSqueak;
    [SerializeField] private Sound soundHatchCloseImpact;
    [SerializeField] private Sound soundHatchOpenImpact;
    [SerializeField] private Sound soundButtonBreak;
    [SerializeField] private Sound soundBuildUpLoop;
    [SerializeField] private AnimationCurve flickerFadeLightCurve;
    [SerializeField] private SpringQuaternion meshRotationSpring;
    [SerializeField] private SpringVector3 meshPositionSpring;
    [SerializeField] private SpringFloat buttonRotationSpring;
    [SerializeField] private Sound soundFluorescentLightTurnOff;
    [SerializeField] private Sound soundLilButtonFire;
    
    private RerollState state = RerollState.Idle;
    private bool stateStart = true;
    private bool activationHeld;
    private bool activationStartedThisFrame;
    private bool activationReleasedThisFrame;
    private bool isBroken;
    private bool visualOnlyReroll;
    private int rerollCount;
    private int maxRerollCount = -1;
    private float stateTimer;
    private float stateTimerMax;
    private float chargeElapsed;
    private float buttonAnimationEval;
    private float buttonRotationTarget;
    private float buttonRotationAngle;
    private float buttonStageRotationAngle = 45f;
    private float hatchAnimationEval;
    private float compartmentAnimationEval;
    private int rerollTicksPlayed;
    private bool finalRollSqueakPlayed;
    private bool hatchCloseImpactPlayed;
    private bool hatchOpenImpactPlayed;
    private bool[] chargeStageTriggered;
    private Vector3 buttonOriginalPosition;
    private Quaternion buttonOriginalRotation;
    private Vector3 hatchOriginalPosition;
    private Vector3 hatchOriginalScale;
    private Quaternion compartmentOriginalRotation;
    private Vector3 allMeshesOriginalPosition;
    private Quaternion allMeshesOriginalRotation;
    private readonly List<CachedUpgrade> cachedUpgrades = new();
    private readonly List<PendingReplacement> pendingReplacements = new();
    private bool fireActive;
    private float firePerlinOffsetX;
    private float firePerlinOffsetY;
    private float fireLightFadeInTimer;
    private bool buildUpActive;
    private float buildUpTimer;
    
    private int RerollCost => 5 + rerollCount * 5;

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
    }

    private void OnEnable()
    {
        PhotonNetwork.AddCallbackTarget(this);
    }

    private void OnDisable()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    private void Start()
    {
        ResolveReferences();
        CaptureOriginalTransforms();
        DisableVanillaButtonNetworking();
        ResetBuildUpVisuals();

        if (buildUpStages == null || buildUpStages.Length == 0)
            buildUpStages = new UpgradeStand.BuildUpStage[4];

        chargeStageTriggered = new bool[buildUpStages.Length];
        buttonStageRotationAngle = 180f / Mathf.Max(1, buildUpStages.Length);

        buttonRotationSpring ??= new SpringFloat();
        meshRotationSpring ??= new SpringQuaternion();
        meshPositionSpring ??= new SpringVector3();

        buttonRotationSpring.speed = 80f;
        buttonRotationSpring.damping = 0.25f;

        meshRotationSpring.speed *= 0.75f;
        meshPositionSpring.speed *= 0.75f;
        
        if (buttonRenderer != null && buttonNormalMaterial != null)
            buttonRenderer.material = buttonNormalMaterial;

        if (buttonRubble != null)
            buttonRubble.SetActive(false);

        if (fireHurtCollider != null)
            fireHurtCollider.SetActive(false);

        if (fireLight != null)
            fireLight.gameObject.SetActive(false);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[UpgradeStandReroll] Controller ready. scanBox={NameOrNull(scanBox)}, button={NameOrNull(buttonRoot)}, collider={NameOrNull(buttonColliderRoot)}.");
    }

    private void Update()
    {
        ReadActivationInput();

        bool buttonFocused = IsLocalPlayerLookingAtButton();
        UpdateHover(buttonFocused);
        UpdateState(buttonFocused);
        UpdateButtonRotationSpring();
        ApplyButtonRotation();
        UpdateMeshSprings();
        UpdateFire();
        UpdateBuildUpLoop();

        if (stateTimer <= stateTimerMax)
            stateTimer += Time.deltaTime;
    }

    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code == RerollRequestEvent)
        {
            if (PhotonNetwork.IsMasterClient)
                TryStartReroll(visualOnly: false, broadcastVisual: true);

            return;
        }

        if (photonEvent.Code == RerollVisualEvent)
        {
            if (!PhotonNetwork.IsMasterClient)
                BeginVisualReroll();

            return;
        }

        if (photonEvent.Code == BrokenVisualEvent && !PhotonNetwork.IsMasterClient)
        {
            BreakButton();
        }
    }

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
                StateRollback();
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
            chargeElapsed = 0f;
            buttonRotationTarget = 0f;
            ResetChargeStageTriggers();
            ResetBuildUpVisuals();
            if (buildUpLight != null)
                buildUpLight.gameObject.SetActive(true);

            stateStart = false;
        }

        if (!activationHeld || activationReleasedThisFrame || !buttonFocused)
        {
            StateSet(RerollState.Rollback);
            return;
        }

        chargeElapsed += Time.deltaTime;
        UpdateChargeVisuals(chargeElapsed);

        if (chargeElapsed >= HoldDuration)
        {
            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                TryStartReroll(visualOnly: false, broadcastVisual: true);
            }
            else if (SemiFunc.IsMultiplayer())
            {
                RequestHostReroll();
                StateSet(RerollState.WaitingForHost);
            }
            else
            {
                StateSet(RerollState.PressFail);
            }
        }
    }

    private void StateRollback()
    {
        if (stateStart)
        {
            stateTimerMax = 0.45f;
            stateTimer = 0f;
            if (soundButtonTwistDown != null)
                soundButtonTwistDown.Play(ButtonSoundPosition);
            stateStart = false;
        }

        float t = Mathf.Clamp01(stateTimer / stateTimerMax);
        float eased = Evaluate(buildUpOutroCurve, t);
        buttonRotationTarget = Mathf.Lerp(180f, 0f, eased);
        FadeBuildUpVisuals(1f - eased);

        if (stateTimer >= stateTimerMax)
            StateSet(RerollState.Idle);
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

    private void TryStartReroll(bool visualOnly, bool broadcastVisual)
    {
        if (state != RerollState.Holding && state != RerollState.Idle)
            return;

        if (visualOnly)
        {
            visualOnlyReroll = true;
            StateSet(RerollState.PressSucceed);
            return;
        }

        if (!SemiFunc.IsMasterClientOrSingleplayer())
            return;

        if (isBroken)
        {
            StateSet(RerollState.PressFail);
            return;
        }

        List<CachedUpgrade> upgrades = ScanUpgradesInside();
        if (upgrades.Count == 0)
        {
            Plugin.Log.LogInfo("[UpgradeStandReroll] Reroll skipped: no upgrades inside stand.");
            StateSet(RerollState.PressFail);
            return;
        }

        int cost = RerollCost;
        if (SemiFunc.StatGetRunCurrency() < cost)
        {
            Plugin.Log.LogInfo($"[UpgradeStandReroll] Reroll skipped: not enough currency. cost={cost}, current={SemiFunc.StatGetRunCurrency()}.");
            StateSet(RerollState.PressFail);
            return;
        }

        List<PendingReplacement> replacements = BuildPendingReplacements(upgrades);
        if (replacements.Count == 0)
        {
            Plugin.Log.LogInfo("[UpgradeStandReroll] Reroll skipped: no valid replacement upgrades.");
            StateSet(RerollState.PressFail);
            return;
        }

        SemiFunc.StatSetRunCurrency(SemiFunc.StatGetRunCurrency() - cost);
        if (CurrencyUI.instance != null)
            CurrencyUI.instance.FetchCurrency();

        if (maxRerollCount < 0)
            maxRerollCount = Random.Range(1, 4);

        rerollCount++;
        cachedUpgrades.Clear();
        cachedUpgrades.AddRange(upgrades);
        pendingReplacements.Clear();
        pendingReplacements.AddRange(replacements);
        visualOnlyReroll = false;

        if (broadcastVisual && SemiFunc.IsMultiplayer())
            PhotonNetwork.RaiseEvent(RerollVisualEvent, new object[0], new RaiseEventOptions { Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);

        Plugin.Log.LogInfo($"[UpgradeStandReroll] Reroll accepted. upgrades={upgrades.Count}, replacements={replacements.Count}, cost={cost}, rerollCount={rerollCount}, maxBeforeBreak={maxRerollCount}.");
        StateSet(RerollState.PressSucceed);
    }

    private void BeginVisualReroll()
    {
        if (state != RerollState.Idle && state != RerollState.WaitingForHost)
            return;

        visualOnlyReroll = true;
        StateSet(RerollState.PressSucceed);
    }

    private void RequestHostReroll()
    {
        if (!SemiFunc.IsMultiplayer())
            return;

        PhotonNetwork.RaiseEvent(RerollRequestEvent, new object[0], new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient }, SendOptions.SendReliable);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo("[UpgradeStandReroll] Sent reroll request to host.");
    }

    private void BroadcastBroken()
    {
        if (SemiFunc.IsMultiplayer() && PhotonNetwork.IsMasterClient)
            PhotonNetwork.RaiseEvent(BrokenVisualEvent, new object[0], new RaiseEventOptions { Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);
    }

    private bool CanAttemptRerollLocally()
    {
        if (isBroken)
            return false;

        if (SemiFunc.StatGetRunCurrency() < RerollCost)
            return false;

        return scanBox != null;
    }

    private List<PendingReplacement> BuildPendingReplacements(List<CachedUpgrade> upgrades)
    {
        Dictionary<string, int> displayedCounts = BuildDisplayedCounts(upgrades);
        Dictionary<string, int> selectedCounts = new();
        List<PendingReplacement> replacements = new();

        foreach (CachedUpgrade cached in upgrades)
        {
            Item replacement = SelectReplacement(cached.Item, displayedCounts, selectedCounts);
            if (replacement == null)
                continue;

            string key = ItemKey(replacement);
            selectedCounts[key] = selectedCounts.TryGetValue(key, out int count) ? count + 1 : 1;
            replacements.Add(new PendingReplacement(cached.Upgrade, replacement, cached.Position, cached.Rotation));
        }

        return replacements;
    }

    private List<CachedUpgrade> ScanUpgradesInside()
    {
        List<CachedUpgrade> result = new();

        if (scanBox == null)
        {
            Plugin.Log.LogWarning("[UpgradeStandReroll] Missing scan box; cannot scan upgrades.");
            return result;
        }

        HashSet<ItemUpgrade> seen = new();
        Collider[] colliders = Physics.OverlapBox(scanBox.position, scanBox.localScale * 0.5f, scanBox.rotation);

        foreach (Collider collider in colliders)
        {
            ItemUpgrade upgrade = collider.GetComponent<ItemUpgrade>() ?? collider.GetComponentInParent<ItemUpgrade>();
            if (upgrade == null || !seen.Add(upgrade))
                continue;

            ItemAttributes attributes = upgrade.GetComponent<ItemAttributes>();
            if (attributes == null || attributes.item == null)
                continue;

            result.Add(new CachedUpgrade(upgrade, attributes.item, upgrade.transform.position, upgrade.transform.rotation));
        }

        return result;
    }

    private void DestroyCachedUpgrades()
    {
        foreach (PendingReplacement replacement in pendingReplacements)
            DestroyUpgrade(replacement.OriginalUpgrade);
    }

    private void SpawnPendingReplacements()
    {
        foreach (PendingReplacement replacement in pendingReplacements)
            SpawnReplacement(replacement.Item, replacement.Position, replacement.Rotation);
    }

    private static Dictionary<string, int> BuildDisplayedCounts(IEnumerable<CachedUpgrade> upgrades)
    {
        Dictionary<string, int> counts = new();

        foreach (CachedUpgrade upgrade in upgrades)
        {
            string key = ItemKey(upgrade.Item);
            counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
        }

        return counts;
    }

    private static Item SelectReplacement(Item previous, Dictionary<string, int> displayedCounts, Dictionary<string, int> selectedCounts)
    {
        Item selected = SelectReplacementInternal(previous, displayedCounts, selectedCounts, allowPrevious: false);
        return selected ?? SelectReplacementInternal(previous, displayedCounts, selectedCounts, allowPrevious: true);
    }

    private static Item SelectReplacementInternal(Item previous, Dictionary<string, int> displayedCounts, Dictionary<string, int> selectedCounts, bool allowPrevious)
    {
        if (StatsManager.instance == null)
            return null;

        int sameLimit = Plugin.SameItemCopies.TryGetValue("Upgrades", out var entry) ? entry.Value : 6;
        int players = GameDirector.instance != null ? GameDirector.instance.PlayerList.Count : 1;
        List<(Item item, int weight)> candidates = new();

        foreach (Item item in StatsManager.instance.itemDictionary.Values)
        {
            if (item == null || item.disabled || item.itemType != SemiFunc.itemType.item_upgrade)
                continue;

            if (!allowPrevious && item == previous)
                continue;

            int chance = Plugin.GetItemSpawnChance(item);
            if (chance <= 0)
                continue;

            string key = ItemKey(item);
            int displayed = displayedCounts.TryGetValue(key, out int displayedCount) ? displayedCount : 0;
            int selected = selectedCounts.TryGetValue(key, out int selectedCount) ? selectedCount : 0;
            int purchased = SemiFunc.StatGetItemsPurchased(item.name);

            if (displayed + selected >= sameLimit)
                continue;

            if (item.maxAmountInShop > 0 && purchased + displayed + selected >= item.maxAmountInShop)
                continue;

            if (item.maxPurchase && StatsManager.instance.GetItemsUpgradesPurchasedTotal(item.name) >= item.maxPurchaseAmount)
                continue;

            if (item.minPlayerCount > 1 && players < item.minPlayerCount)
                continue;

            candidates.Add((item, Mathf.Max(1, chance)));
        }

        if (candidates.Count == 0)
            return null;

        int totalWeight = candidates.Sum(candidate => candidate.weight);
        int roll = Random.Range(0, totalWeight);

        foreach ((Item item, int weight) in candidates)
        {
            if (roll < weight)
                return item;

            roll -= weight;
        }

        return candidates[candidates.Count - 1].item;
    }

    private static void DestroyUpgrade(ItemUpgrade upgrade)
    {
        if (upgrade == null)
            return;

        PhotonView view = upgrade.GetComponent<PhotonView>();
        GameObject target = view != null ? view.gameObject : upgrade.gameObject;

        if (SemiFunc.IsMultiplayer() && view != null)
            PhotonNetwork.Destroy(target);
        else
            Destroy(target);
    }

    private void SpawnReplacement(Item item, Vector3 position, Quaternion fallbackRotation)
    {
        Quaternion rotation = fallbackRotation;

        if (ShopManager.instance != null && ShopManager.instance.itemRotateHelper != null)
        {
            Transform helper = ShopManager.instance.itemRotateHelper.transform;
            helper.parent = transform;
            helper.position = position;
            helper.localRotation = item.spawnRotationOffset;
            rotation = helper.rotation;
            helper.parent = ShopManager.instance.transform;
        }

        if (SemiFunc.IsMultiplayer())
            PhotonNetwork.InstantiateRoomObject(item.prefab.ResourcePath, position, rotation, 0);
        else
            Instantiate(item.prefab.Prefab, position, rotation);
    }

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
        if (!buttonFocused || isBroken || state is not (RerollState.Idle or RerollState.Holding))
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

        RaycastHit[] hits = Physics.SphereCastAll(ray, ButtonCastRadius, ButtonUseDistance, ~0, QueryTriggerInteraction.Collide);
        foreach (RaycastHit sphereHit in hits.OrderBy(h => h.distance))
        {
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
        foreach (PhysGrabObjectGrabArea area in GetComponentsInChildren<PhysGrabObjectGrabArea>(true))
            area.enabled = false;

        foreach (StaticGrabObject grab in GetComponentsInChildren<StaticGrabObject>(true))
            grab.enabled = false;

        foreach (PhotonView view in GetComponentsInChildren<PhotonView>(true))
            view.enabled = false;
    }

    private void StateSet(RerollState nextState)
    {
        state = nextState;
        stateStart = true;
    }

    private float HoldDuration => buildUpStages == null || buildUpStages.Length == 0
        ? 1.7f
        : buildUpStages.Length * BuildUpStageDuration + BuildUpFinalHoldDuration;

    private void ResetChargeStageTriggers()
    {
        if (chargeStageTriggered == null || chargeStageTriggered.Length != buildUpStages.Length)
            chargeStageTriggered = new bool[buildUpStages.Length];

        for (int i = 0; i < chargeStageTriggered.Length; i++)
            chargeStageTriggered[i] = false;
    }

    private void UpdateChargeVisuals(float elapsed)
    {
        int stageCount = Mathf.Max(1, buildUpStages.Length);
        buttonRotationTarget = Mathf.Clamp(Mathf.FloorToInt(elapsed / BuildUpStageDuration) + 1, 0, stageCount) * buttonStageRotationAngle;

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
        if (particleButtonBreak != null)
            particleButtonBreak.Play(true);
        
        if (particleFireLoop != null)
            particleFireLoop.Play(true);
        
        if (soundButtonBreak != null)
            soundButtonBreak.Play(ButtonSoundPosition);
        
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

    
    private Vector3 ButtonSoundPosition => buttonRoot != null ? buttonRoot.position : transform.position;
    private Vector3 HatchSoundPosition => hatch != null ? hatch.position : transform.position;
    private Vector3 CompartmentSoundPosition => upgradeCompartment != null ? upgradeCompartment.position : transform.position;

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

    private static string ItemKey(Item item)
    {
        return item == null ? string.Empty : item.name;
    }

    private readonly struct CachedUpgrade
    {
        internal readonly ItemUpgrade Upgrade;
        internal readonly Item Item;
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;

        internal CachedUpgrade(ItemUpgrade upgrade, Item item, Vector3 position, Quaternion rotation)
        {
            Upgrade = upgrade;
            Item = item;
            Position = position;
            Rotation = rotation;
        }
    }

    private readonly struct PendingReplacement
    {
        internal readonly ItemUpgrade OriginalUpgrade;
        internal readonly Item Item;
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;

        internal PendingReplacement(ItemUpgrade originalUpgrade, Item item, Vector3 position, Quaternion rotation)
        {
            OriginalUpgrade = originalUpgrade;
            Item = item;
            Position = position;
            Rotation = rotation;
        }
    }
}
