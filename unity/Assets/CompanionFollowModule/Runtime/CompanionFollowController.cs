using UnityEngine;
using UnityEngine.Serialization;

namespace CompanionAvatar
{
    public enum CompanionFollowMode { Side, Lead, Trail, Stay, Approach, Roam }
    public enum CompanionSidePreference { Auto, Left, Right }
    public enum CompanionFollowIntent { Lead, Side, Trail, Nearby }
    public enum CompanionDeparturePhase { None, Braking, Turning, Starting }

    /// <summary>Owns the stable character root pose; animation remains in AvatarMotionController.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(AvatarMotionController)), DefaultExecutionOrder(-50)]
    public sealed class CompanionFollowController : MonoBehaviour
    {
        [Header("Position, view and locomotion are independent")]
        [FormerlySerializedAs("playerRoot")]
        public Transform playerPositionReference;
        [Tooltip("Documentation/debug reference only. Never used to calculate formation.")]
        public Transform viewReference;
        [Tooltip("Stable track +Z by default. Must not be the camera or a view-rotated XR rig.")]
        public Transform locomotionHeading;
        public AvatarMotionController motion;
        [Tooltip("Optional turning-only adaptation; does not select formation targets.")] public AvatarTurningAdaptation turningAdaptation;
        public BoxCollider stableFloor;
        [Header("Offsets in Locomotion Heading coordinates, centred on Player Position")]
        public Vector3 leadOffset = new Vector3(.8f, 0, 2.5f);
        public Vector3 sideOffset = new Vector3(1.8f, 0, 0);
        public Vector3 trailOffset = new Vector3(.8f, 0, -2.2f);
        [Min(.05f)] public float positionSmoothTime = .55f;
        [Min(.05f)] public float rotationSmoothTime = .3f;
        [Min(1)] public float teleportRecoveryDistance = 12f;
        [Header("Normal formation, catch-up and animation limits")]
        [Min(.1f)] public float normalMaxSpeed = 3f;
        [Min(.1f)] public float catchUpMaxSpeed = 4.5f;
        [Min(.1f)] public float maxAcceleration = 2.5f;
        [Min(.1f)] public float formationCorrectionSpeed = 1.2f;
        [Min(1)] public float catchUpDistance = 6f;
        [Min(.05f)] public float visualSpeedSmoothTime = .2f;
        [Min(.1f)] public float maxVisualSpeed = 3.5f;
        [Min(.1f)] public float nearbyMoveSpeed = 1.3f;
        [Header("Soft formation — preferences, not a rigid transform")]
        public CompanionSidePreference sidePreference = CompanionSidePreference.Auto;
        [Min(.05f)] public float positionDeadZone = .45f;
        [Min(.1f)] public float preferredRadius = .75f;
        [Min(.1f)] public float repositionThreshold = 1.1f;
        [Min(0)] public float repositionDelay = 1f;
        [Min(0)] public float formationStartDelay = 1.2f;
        [Min(.1f)] public float formationStartSpeed = .35f;
        [Header("Nearby to formation — orient before setting off")]
        [Range(10,120)] public float departureTurnThreshold = 45f;
        [Range(2,40)] public float departureReleaseAngle = 18f;
        [Min(15)] public float departureTurnSpeed = 110f;
        [Min(.05f)] public float departureTurnSmoothTime = .22f;
        [Min(.1f)] public float departureAccelerationTime = .85f;
        [Header("Social positioning — independent of formation side")]
        [Min(.1f)] public float minPersonalDistance = .75f;
        [Min(.1f)] public float preferredSocialDistance = 1.7f;
        [Min(.2f)] public float maxNearbyDistance = 3.5f;
        [Header("Nearby — used automatically when the player stops")]
        [HideInInspector] public float stayRadius = 2.7f; // Legacy serialized value; social distances supersede it.
        [Min(.1f)] public float maxRoamRadius = 3.5f;
        [Min(.1f)] public float preferredConversationDistance = 1.5f;
        [Min(.5f)] public float roamInterval = 5f;
        [Header("Stable straight road limits")]
        [Min(.1f)] public float roadHalfWidth = 4.4f;
        [Min(0)] public float floorEdgeMargin = .35f;
        [Tooltip("Existing stable floor top is -0.01m; this keeps the root at Y=0.")]
        public float groundClearance = .01f;
        [Header("Movement detection and settling")]
        [Min(.01f)] public float playerStartSpeed = .15f;
        [Min(0)] public float playerStopSpeed = .06f;
        [Min(0)] public float stopDelay = .7f;
        [Min(.001f)] public float arrivalDistance = .025f;
        [SerializeField] CompanionFollowMode followMode = CompanionFollowMode.Side;
        [SerializeField] CompanionFollowMode nearbyMode = CompanionFollowMode.Stay;
        public CompanionFollowMode FollowMode => followMode;
        public CompanionFollowMode NearbyMode => nearbyMode;
        public CompanionFollowMode EffectiveMode { get; private set; }
        public bool PlayerMoving { get; private set; }
        public bool FormationActive { get; private set; }
        public CompanionDeparturePhase DeparturePhase { get; private set; }
        public float DepartureAngle { get; private set; }
        // Gaze keeps social attention during the turn, then fades to locomotion attention.
        public float DepartureMovementBlend => !FormationActive ? 0 : DeparturePhase==CompanionDeparturePhase.None ? 1 : DeparturePhase==CompanionDeparturePhase.Starting ? Mathf.SmoothStep(0,1,departureProgress) : 0;
        public bool ForceSocial => forceSocial;
        public int CurrentSide { get; private set; } = 1;
        public bool IsRepositioning { get; private set; }
        public float ActualMoveSpeed { get; private set; }
        public float VisualMoveSpeed { get; private set; }
        public bool IsCatchingUp { get; private set; }
        public Vector3 TargetPosition { get; private set; }
        public bool NearbyRangeReachable { get; private set; } = true;
        public int RecoveryCount { get; private set; }
        public struct MotionSample
        {
            public float deltaTime;
            public Vector3 playerDelta, currentCompanionPosition, desiredTarget;
            public Vector3 compensationDelta, smoothCorrection, finalDelta;
            public Vector3 playerVelocity, desiredVelocity, integratedVelocity;
            public float calculatedCompanionSpeed, visualSpeed;
            public bool recovery;
        }
        public MotionSample LastMotionSample { get; private set; }
        Vector3 previousPlayer, velocity, nearbyAnchor, filteredPlayerVelocity;
        float yawVelocity, filteredPlayerSpeed, stoppedFor, nextRoam, fixedHeight;
        bool initialized, anchorPending = true;
        bool forceSocial, lateralReposition, socialReposition, stationaryFacingRequested;
        float formationTimer, lateralTimer, socialTimer;
        float departureProgress;
        Vector3 stationaryFacing;
        float stationaryTurnSpeed = 30;
        Transform trackedPlayer;
        readonly System.Random random = new System.Random();

        void Awake() { if (motion == null) motion = GetComponent<AvatarMotionController>(); }
        void OnEnable() { initialized = false; anchorPending = true; ResetDeparture(); }
        void OnDisable() { velocity = Vector3.zero; ActualMoveSpeed = VisualMoveSpeed = 0; ResetDeparture(); if (motion != null) motion.SetMoveSpeed(0); }
        // Desktop moves the rig in Update. Read the completed player pose once, before UMT's LateUpdate.
        void LateUpdate() { Tick(Time.deltaTime); }

        public void SetFollowMode(CompanionFollowMode mode)
        {
            if (!System.Enum.IsDefined(typeof(CompanionFollowMode), mode)) return;
            if (mode == CompanionFollowMode.Side || mode == CompanionFollowMode.Lead || mode == CompanionFollowMode.Trail)
            { followMode = mode; nearbyMode = CompanionFollowMode.Stay; forceSocial = false; }
            else { nearbyMode = mode; forceSocial = true; }
            // Preserve velocity: mode changes retarget smoothly, without teleporting.
            anchorPending = true; nextRoam = 0;
            lateralTimer = socialTimer = 0; lateralReposition = socialReposition = false;
        }
        public void SetFollowIntent(CompanionFollowIntent intent)
        {
            SetFollowMode(intent==CompanionFollowIntent.Lead?CompanionFollowMode.Lead:intent==CompanionFollowIntent.Trail?CompanionFollowMode.Trail:intent==CompanionFollowIntent.Nearby?CompanionFollowMode.Stay:CompanionFollowMode.Side);
        }
        // Gaze requests orientation only. This component retains ownership of root rotation.
        public void RequestStationaryFacing(Vector3 direction,float degreesPerSecond=30)
        { stationaryFacing=Horizontal(direction);stationaryTurnSpeed=Mathf.Max(1,degreesPerSecond);stationaryFacingRequested=true; }

        public void Tick(float dt)
        {
            if (!isActiveAndEnabled || playerPositionReference == null || motion == null || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt))
            { ActualMoveSpeed = 0; if (motion != null) motion.SetMoveSpeed(0); return; }
            float elapsed = dt;
            dt = Mathf.Min(dt, .1f);
            Vector3 player = playerPositionReference.position;
            if (!initialized || trackedPlayer != playerPositionReference)
            {
                initialized = true; trackedPlayer = playerPositionReference; previousPlayer = player;
                fixedHeight = transform.position.y; velocity = Vector3.zero;
                filteredPlayerSpeed = 0; filteredPlayerVelocity = Vector3.zero;
                stoppedFor = formationTimer = lateralTimer = socialTimer = 0;
                FormationActive = false; lateralReposition = socialReposition = false;
                PlayerMoving = false; IsCatchingUp = false; anchorPending = true;
                ResetDeparture();
            }
            Vector3 playerDelta = Horizontal(player - previousPlayer);
            float playerStep = playerDelta.magnitude;
            previousPlayer = player;
            // Recovery is triggered only by a discontinuous PLAYER jump, never a mode switch.
            if (playerStep > Mathf.Max(1, teleportRecoveryDistance))
            {
                Vector3 prior = transform.position;
                transform.position = Constrain(OffsetPosition(), Mathf.Max(.1f, maxRoamRadius));
                TargetPosition = transform.position;
                velocity = filteredPlayerVelocity = Vector3.zero; yawVelocity = 0; filteredPlayerSpeed = 0;
                ActualMoveSpeed = VisualMoveSpeed = 0; PlayerMoving = FormationActive = false; IsCatchingUp = false; anchorPending = true;
                formationTimer = lateralTimer = socialTimer = 0; lateralReposition = socialReposition = false;
                LastMotionSample = new MotionSample { deltaTime=elapsed, playerDelta=playerDelta, currentCompanionPosition=prior, desiredTarget=TargetPosition, finalDelta=transform.position-prior, recovery=true };
                ResetDeparture(); RecoveryCount++; motion.SetMoveSpeed(0); return;
            }
            filteredPlayerVelocity = Vector3.Lerp(filteredPlayerVelocity, playerDelta / elapsed, 1 - Mathf.Exp(-dt / .2f));
            filteredPlayerSpeed = Mathf.Lerp(filteredPlayerSpeed, playerStep / elapsed, 1 - Mathf.Exp(-dt / .2f));
            bool wasMoving = PlayerMoving;
            if (filteredPlayerSpeed > Mathf.Max(.01f, playerStartSpeed)) { PlayerMoving = true; stoppedFor = 0; }
            else if (filteredPlayerSpeed < Mathf.Min(playerStopSpeed, playerStartSpeed))
            { stoppedFor += dt; if (stoppedFor >= stopDelay) PlayerMoving = false; }
            else stoppedFor = 0;
            if (wasMoving && !PlayerMoving) anchorPending = true;
            Vector3 forward=PlayerHeading()*Vector3.forward;
            float progress=Mathf.Abs(Vector3.Dot(filteredPlayerVelocity,forward));
            bool longitudinal=progress>formationStartSpeed && progress>filteredPlayerVelocity.magnitude*.7f;
            if(!forceSocial && longitudinal)formationTimer+=dt;else if(!FormationActive || !PlayerMoving)formationTimer=0;
            bool previousFormation=FormationActive;
            FormationActive=!forceSocial && PlayerMoving && (FormationActive || formationTimer>=formationStartDelay);
            if(previousFormation!=FormationActive)
            {
                anchorPending=true;lateralTimer=socialTimer=0;lateralReposition=socialReposition=false;
                if(FormationActive)BeginDeparture(forward);else ResetDeparture();
            }
            UpdateDeparture(forward,dt);
            EffectiveMode = FormationActive ? followMode : nearbyMode;
            float radius = Mathf.Max(.2f, EffectiveMode==CompanionFollowMode.Roam?Mathf.Min(maxRoamRadius,maxNearbyDistance):maxNearbyDistance);
            if(anchorPending){nearbyAnchor=FloorPoint(transform.position);anchorPending=false;socialReposition=false;nextRoam=0;}
            Vector3 target = FormationActive ? FormationTarget(player,dt) : SocialTarget(player,radius,dt);
            TargetPosition = target;
            Vector3 before = transform.position;
            Vector3 error = Horizontal(target-before);
            // Catch-up is for actual separation from the player, not an offset/mode change.
            float separation = Horizontal(before-player).magnitude;
            if (separation > Mathf.Max(1, catchUpDistance)) IsCatchingUp = true;
            else if (separation < Mathf.Max(1, catchUpDistance)*.65f) IsCatchingUp = false;
            float speedLimit = Mathf.Max(.1f, IsCatchingUp ? catchUpMaxSpeed : FormationActive ? normalMaxSpeed : nearbyMoveSpeed);
            float correctionLimit = IsCatchingUp ? speedLimit : Mathf.Max(.1f,formationCorrectionSpeed);
            correctionLimit = Mathf.Min(correctionLimit,.8f*Mathf.Sqrt(2*Mathf.Max(.1f,maxAcceleration)*error.magnitude));
            Vector3 correction = Vector3.ClampMagnitude(error / Mathf.Max(.05f,positionSmoothTime), correctionLimit);
            // Longitudinal progress only: lateral and social motion are never mirrored.
            Vector3 longitudinalVelocity=FormationActive?forward*Vector3.Dot(filteredPlayerVelocity,forward):Vector3.zero;
            Vector3 desiredVelocity = Vector3.ClampMagnitude(longitudinalVelocity + correction, speedLimit);
            if(DeparturePhase==CompanionDeparturePhase.Braking || DeparturePhase==CompanionDeparturePhase.Turning)
                desiredVelocity=Vector3.zero;
            else if(DeparturePhase==CompanionDeparturePhase.Starting)
            {
                float blend=DepartureMovementBlend;
                // Ease forward first; formation's lateral correction returns progressively.
                float along=Mathf.Max(0,Vector3.Dot(desiredVelocity,forward));
                desiredVelocity=(forward*along+Vector3.ProjectOnPlane(desiredVelocity,forward)*blend)*blend;
            }
            // Turning-only braking; the formation target and social policy are unchanged.
            if(turningAdaptation!=null&&turningAdaptation.enabled&&DeparturePhase==CompanionDeparturePhase.None)desiredVelocity=turningAdaptation.LimitVelocity(desiredVelocity,velocity,dt);
            // One velocity state and one integration. Never add playerDelta directly to world position.
            velocity = Vector3.MoveTowards(velocity, desiredVelocity, Mathf.Max(.1f,maxAcceleration)*dt);
            Vector3 integratedVelocity = velocity;
            Vector3 after = before + velocity*dt;
            after = Constrain(after, -1);
            // Inside the convex floor/disk intersection, keep every step inside.
            // From outside (e.g. a new, smaller radius), return smoothly, not by snapping.
            if (EffectiveMode==CompanionFollowMode.Roam && Horizontal(before - player).magnitude <= radius + .001f) after = Constrain(after, radius);
            // No arrival snap: it would turn a tiny positional correction into a speed spike.
            if (error.magnitude < arrivalDistance && velocity.magnitude < .015f && !FormationActive)
            { after = before; velocity = Vector3.zero; }
            transform.position = after;
            Vector3 displacement = Horizontal(after - before);
            // Remove blocked velocity at road boundaries; do not accumulate invisible motion.
            velocity = displacement/dt;
            ActualMoveSpeed = displacement.magnitude / elapsed;
            float visualTarget = Mathf.Clamp(ActualMoveSpeed < .015f ? 0 : ActualMoveSpeed,0,Mathf.Max(.1f,maxVisualSpeed));
            float filteredVisual = Mathf.Lerp(VisualMoveSpeed,visualTarget,1-Mathf.Exp(-dt/Mathf.Max(.05f,visualSpeedSmoothTime)));
            VisualMoveSpeed = Mathf.MoveTowards(VisualMoveSpeed,filteredVisual,Mathf.Max(.1f,maxAcceleration)*dt);
            if(visualTarget==0 && VisualMoveSpeed<.015f)VisualMoveSpeed=0;
            motion.SetMoveSpeed(VisualMoveSpeed);
            LastMotionSample = new MotionSample { deltaTime=elapsed, playerDelta=playerDelta, currentCompanionPosition=before, desiredTarget=target,
                compensationDelta=Vector3.zero, smoothCorrection=correction*dt, finalDelta=displacement,
                playerVelocity=filteredPlayerVelocity, desiredVelocity=desiredVelocity, integratedVelocity=integratedVelocity,
                calculatedCompanionSpeed=ActualMoveSpeed, visualSpeed=VisualMoveSpeed };
            Vector3 facing = displacement;
            if (ActualMoveSpeed < .08f) facing = !FormationActive&&stationaryFacingRequested?stationaryFacing:Vector3.zero;
            bool departureTurning=DeparturePhase==CompanionDeparturePhase.Turning || DeparturePhase==CompanionDeparturePhase.Starting;
            if(departureTurning)facing=forward;
            else if(DeparturePhase==CompanionDeparturePhase.Braking)facing=Vector3.zero;
            if(turningAdaptation!=null&&turningAdaptation.enabled&&!departureTurning&&DeparturePhase!=CompanionDeparturePhase.Braking)facing=turningAdaptation.Facing(facing,ActualMoveSpeed);
            if (facing.sqrMagnitude > .000001f)
            {
                float yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
                float turnTime=departureTurning?departureTurnSmoothTime:rotationSmoothTime;
                float turnSpeed=departureTurning?departureTurnSpeed:ActualMoveSpeed<.08f?stationaryTurnSpeed:120;
                if(turningAdaptation!=null&&turningAdaptation.enabled)turnSpeed=turningAdaptation.YawLimit(turnSpeed,ActualMoveSpeed);
                float smoothed = Mathf.SmoothDampAngle(transform.eulerAngles.y, yaw, ref yawVelocity, Mathf.Max(.05f, turnTime), Mathf.Max(15,turnSpeed), dt);
                transform.rotation = Quaternion.Euler(0, smoothed, 0);
            }
            else yawVelocity = 0;
            stationaryFacingRequested=false;
        }
        void ResetDeparture()
        { DeparturePhase=CompanionDeparturePhase.None;departureProgress=0;DepartureAngle=0; }
        void BeginDeparture(Vector3 forward)
        {
            departureProgress=0;yawVelocity=0;
            DepartureAngle=Vector3.Angle(Horizontal(transform.forward),forward);
            DeparturePhase=DepartureAngle>Mathf.Max(10,departureTurnThreshold)
                ? CompanionDeparturePhase.Braking : CompanionDeparturePhase.Starting;
        }
        void UpdateDeparture(Vector3 forward,float dt)
        {
            if(DeparturePhase==CompanionDeparturePhase.None)return;
            DepartureAngle=Vector3.Angle(Horizontal(transform.forward),forward);
            // Finish any existing approach movement before a large root turn.
            if(DeparturePhase==CompanionDeparturePhase.Braking && velocity.magnitude<.025f)
                DeparturePhase=CompanionDeparturePhase.Turning;
            if(DeparturePhase==CompanionDeparturePhase.Turning && DepartureAngle<=Mathf.Clamp(departureReleaseAngle,2,Mathf.Max(2,departureTurnThreshold-1)))
                DeparturePhase=CompanionDeparturePhase.Starting;
            if(DeparturePhase==CompanionDeparturePhase.Starting)
            {
                departureProgress=Mathf.Clamp01(departureProgress+dt/Mathf.Max(.1f,departureAccelerationTime));
                if(departureProgress>=1 && DepartureAngle<8)DeparturePhase=CompanionDeparturePhase.None;
            }
        }
        Vector3 FormationTarget(Vector3 player,float dt)
        {
            Quaternion heading=PlayerHeading();Vector3 relative=Quaternion.Inverse(heading)*Horizontal(transform.position-player);
            if(sidePreference==CompanionSidePreference.Auto){if(Mathf.Abs(relative.x)>.35f)CurrentSide=relative.x>0?1:-1;}
            else CurrentSide=sidePreference==CompanionSidePreference.Left?-1:1;
            Vector3 preferred=followMode==CompanionFollowMode.Lead?leadOffset:followMode==CompanionFollowMode.Trail?trailOffset:new Vector3(Mathf.Abs(sideOffset.x)*CurrentSide,0,sideOffset.z);
            float lateralError=preferred.x-relative.x;
            if(Mathf.Abs(lateralError)>Mathf.Max(positionDeadZone,repositionThreshold))lateralTimer+=dt;else lateralTimer=0;
            if(lateralTimer>=repositionDelay)lateralReposition=true;
            if(Mathf.Abs(lateralError)<Mathf.Max(positionDeadZone,preferredRadius)){lateralReposition=false;lateralTimer=0;}
            float x=lateralReposition?lateralError-Mathf.Sign(lateralError)*positionDeadZone:0;
            float zError=preferred.z-relative.z;
            float z=Mathf.Abs(zError)>preferredRadius?zError-Mathf.Sign(zError)*preferredRadius:0;
            IsRepositioning=lateralReposition||Mathf.Abs(z)>.01f;
            return FloorPoint(transform.position+heading*new Vector3(x,0,z));
        }
        Vector3 SocialTarget(Vector3 player,float radius,float dt)
        {
            Vector3 radial=Horizontal(transform.position-player);float distance=radial.magnitude;
            if(distance<.001f)radial=PlayerHeading()*Vector3.right;else radial/=distance;
            bool unsafeSpace=distance<minPersonalDistance || distance>radius;
            if(unsafeSpace)socialTimer+=dt;else socialTimer=0;
            if(socialTimer>=repositionDelay || distance<minPersonalDistance*.55f)socialReposition=true;
            float preferred=Mathf.Clamp(preferredSocialDistance,minPersonalDistance+.1f,radius);
            if(EffectiveMode==CompanionFollowMode.Approach)
            {
                preferred=Mathf.Clamp(preferredConversationDistance,minPersonalDistance+.1f,radius);
                if(Mathf.Abs(distance-preferred)>positionDeadZone)socialReposition=true;
            }
            if(socialReposition)
            {
                if(Mathf.Abs(distance-preferred)<positionDeadZone*.5f){socialReposition=false;socialTimer=0;nearbyAnchor=transform.position;}
                else {IsRepositioning=true;return Constrain(player+radial*preferred,radius);}
            }
            if(EffectiveMode==CompanionFollowMode.Roam && !unsafeSpace)
            {
                nextRoam-=dt;
                if(nextRoam<=0)
                {
                    float turn=Mathf.Lerp(-55,55,(float)random.NextDouble());
                    float range=Mathf.Lerp(Mathf.Min(preferred,radius*.8f),radius*.85f,(float)random.NextDouble());
                    nearbyAnchor=Constrain(player+(Quaternion.AngleAxis(turn,Vector3.up)*radial)*range,radius);
                    nextRoam=Mathf.Max(.5f,roamInterval);
                }
                IsRepositioning=Horizontal(nearbyAnchor-transform.position).magnitude>arrivalDistance;
                return Constrain(nearbyAnchor,radius);
            }
            IsRepositioning=false;
            return FloorPoint(transform.position); // Comfortable social space holds WORLD position.
        }
        Vector3 OffsetPosition()
        {
            Vector3 offset = followMode == CompanionFollowMode.Lead ? leadOffset : followMode == CompanionFollowMode.Trail ? trailOffset : new Vector3(Mathf.Abs(sideOffset.x)*CurrentSide,0,sideOffset.z);
            return playerPositionReference.position + PlayerHeading() * Horizontal(offset);
        }
        Quaternion PlayerHeading()
        {
            // Never fall back to camera/rig yaw. An unassigned heading uses stable world +Z.
            Vector3 forward = Horizontal(locomotionHeading != null ? locomotionHeading.forward : Vector3.forward);
            return Quaternion.LookRotation(forward.sqrMagnitude > .001f ? forward : Vector3.forward, Vector3.up);
        }
        static Vector3 Horizontal(Vector3 p) { p.y = 0; return p; }
        Vector3 FloorPoint(Vector3 p)
        {
            if (stableFloor == null) { p.y = fixedHeight; return p; }
            Transform t = stableFloor.transform;
            Vector3 local = t.InverseTransformPoint(p), center = stableFloor.center, half = stableFloor.size * .5f;
            float x = Mathf.Max(0, Mathf.Min(half.x, roadHalfWidth) - floorEdgeMargin), z = Mathf.Max(0, half.z - floorEdgeMargin);
            local.x = Mathf.Clamp(local.x, center.x - x, center.x + x);
            local.z = Mathf.Clamp(local.z, center.z - z, center.z + z);
            local.y = center.y + half.y;
            return t.TransformPoint(local) + Vector3.up * groundClearance;
        }
        Vector3 Constrain(Vector3 p, float radius)
        {
            p = FloorPoint(p);
            if (radius < 0) return p;
            Vector3 closest = FloorPoint(playerPositionReference.position);
            NearbyRangeReachable = Horizontal(closest - playerPositionReference.position).magnitude <= radius;
            // When the player leaves the road entirely, a floor/radius intersection may not exist.
            if (!NearbyRangeReachable) return closest;
            if (Horizontal(p - playerPositionReference.position).magnitude <= radius) return p;
            float lo = 0, hi = 1;
            for (int i = 0; i < 20; i++)
            {
                float mid = (lo + hi) * .5f;
                if (Horizontal(Vector3.Lerp(closest, p, mid) - playerPositionReference.position).magnitude <= radius) lo = mid;
                else hi = mid;
            }
            return Vector3.Lerp(closest, p, lo);
        }
    }
}
