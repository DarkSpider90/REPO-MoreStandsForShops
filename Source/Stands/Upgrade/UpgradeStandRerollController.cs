using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace MoreStandsForShops.Stands.Upgrade;

internal sealed partial class UpgradeStandRerollController : MonoBehaviour, IOnEventCallback
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
    
    private const float HoldProgressSyncInterval = 0.15f;
    private const float ButtonUseDistance = 3.25f;
    private const float ButtonCastRadius = 0.06f;
    private const float BuildUpStageDuration = 0.3f;
    private const float BuildUpFinalHoldDuration = 0.5f;

    [SerializeField] private Transform scanBox;
    [SerializeField] private Transform buttonRoot;
    [SerializeField] private Transform buttonColliderRoot;
    [SerializeField] private Transform hatch;
    [SerializeField] private Transform upgradeCompartment;
    [SerializeField] private Transform allMeshesTransform;
    
    [SerializeField] private StaticGrabObject buttonGrabObject;
    
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
    [SerializeField] private AnimationCurve flickerFadeLightCurve;

    [SerializeField] private UpgradeStand.BuildUpStage[] buildUpStages;
    
    [SerializeField] private SpringQuaternion meshRotationSpring;
    [SerializeField] private SpringVector3 meshPositionSpring;
    [SerializeField] private SpringFloat buttonRotationSpring;
    
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
    [SerializeField] private Sound soundFluorescentLightTurnOff;
    [SerializeField] private Sound soundLilButtonFire;
    
    private RerollState state = RerollState.Idle;
    
    private Vector3 buttonOriginalPosition;
    private Vector3 hatchOriginalPosition;
    private Vector3 hatchOriginalScale;
    private Vector3 allMeshesOriginalPosition;
    
    private Quaternion buttonOriginalRotation;
    private Quaternion compartmentOriginalRotation;
    private Quaternion allMeshesOriginalRotation;
    
    private Color rollbackCurrentStageStartLightColor;
    private Color rollbackCurrentStageEndLightColor;
    
    private bool stateStart = true;
    private bool activationHeld;
    private bool activationStartedThisFrame;
    private bool activationReleasedThisFrame;
    private bool remoteHoldVisual;
    private bool holdVisualBroadcasted;
    private bool isBroken;
    private bool visualOnlyReroll;
    private bool finalRollSqueakPlayed;
    private bool hatchCloseImpactPlayed;
    private bool hatchOpenImpactPlayed;
    private bool fireActive;
    private bool [] chargeStageTriggered;
    private bool buildUpActive;
    private bool holdRequestSent;
    private bool resumeChargeFromRollback;
    private bool rollbackResumeRequested;

    private float stateTimer;
    private float stateTimerMax;
    private float chargeElapsed;
    private float buttonAnimationEval;
    private float buttonRotationTarget;
    private float buttonRotationAngle;
    private float buttonStageRotationAngle = 45f;
    private float hatchAnimationEval;
    private float compartmentAnimationEval;
    private float firePerlinOffsetX;
    private float firePerlinOffsetY;
    private float fireLightFadeInTimer;
    private float buildUpTimer;
    private float holdProgressSyncTimer;
    private float rollbackCurrentStageElapsed;
    private float rollbackCurrentStageStartEmission;
    private float rollbackCurrentStageStartLightIntensity;
    private float rollbackCurrentStageEndLightIntensity;

    private readonly List<CachedUpgrade> cachedUpgrades = new();
    private readonly List<PendingReplacement> pendingReplacements = new();
    private readonly RaycastHit[] buttonCastHits = new RaycastHit[64];
    
    private int rollbackTopStage;
    private int rollbackCurrentStage = -1;
    private int rerollCount;
    private int maxRerollCount = -1;
    private int rerollTicksPlayed;
    private int remoteHoldActorNumber = -1;
    private int RerollCost => 5 + rerollCount * 5;

    internal void ApplySynchronizedState(int synchronizedRerollCount, int synchronizedMaxRerollCount, bool synchronizedBroken)
    {
        bool wasBroken = isBroken;
        rerollCount = Mathf.Max(0, synchronizedRerollCount);
        maxRerollCount = Mathf.Max(-1, synchronizedMaxRerollCount);
        isBroken = synchronizedBroken;

        if (isBroken)
        {
            state = RerollState.Broken;
            stateStart = true;
        }
        else if (wasBroken && state == RerollState.Broken)
        {
            state = RerollState.Idle;
            stateStart = true;
        }
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
    
}
