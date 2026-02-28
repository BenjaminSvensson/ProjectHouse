using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NavMeshAgent))]
public class Visitor : MonoBehaviour
{
    private enum GoalType
    {
        None,
        Wander,
        Door,
        InvestigatePlayer,
        ChasePlayer
    }

    private enum PanicActionType
    {
        None,
        Backpedal,
        StrafeLeft,
        StrafeRight,
        Spin
    }

    [Header("References")]
    [SerializeField] private Transform eyes;
    [SerializeField] private Transform player;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Transform visualRoot;

    [Header("Perception")]
    [SerializeField] private float viewDistance = 22f;
    [SerializeField, Range(5f, 179f)] private float viewAngle = 95f;
    [SerializeField] private LayerMask visionBlockerMask = ~0;
    [SerializeField] private float playerEyeOffset = 1f;

    [Header("Player Memory")]
    [SerializeField] private float playerMemoryDuration = 7f;
    [SerializeField] private float investigateRadius = 4.5f;
    [SerializeField] private float investigatePointReachedDistance = 1.35f;
    [SerializeField] private float investigateRetargetInterval = 0.8f;

    [Header("Movement")]
    [SerializeField] private float searchSpeed = 2.6f;
    [SerializeField] private float chaseSpeed = 5.8f;
    [SerializeField] private float turnSpeed = 180f;
    [SerializeField] private float repathInterval = 0.2f;
    [SerializeField] private float searchRadius = 14f;
    [SerializeField] private float searchPointReachedDistance = 1.2f;
    [SerializeField] private float lookAroundTurnSpeed = 95f;

    [Header("NavMesh Recovery")]
    [SerializeField] private float stuckCheckInterval = 0.3f;
    [SerializeField] private float stuckMinMoveDistance = 0.05f;
    [SerializeField] private float stuckTimeToTrigger = 1.0f;
    [SerializeField] private float panicStuckTime = 2.0f;
    [SerializeField] private float unstickSampleRadius = 2.8f;
    [SerializeField] private int unstickSampleCount = 10;
    [SerializeField] private float unstickTargetHoldTime = 0.8f;
    [SerializeField] private float doorForceInteractDistance = 2.6f;

    [Header("Panic Actions")]
    [SerializeField] private float panicActionMinDuration = 0.4f;
    [SerializeField] private float panicActionMaxDuration = 0.9f;
    [SerializeField] private float panicActionCooldown = 1.0f;
    [SerializeField] private float panicBackpedalSpeed = 2.4f;
    [SerializeField] private float panicStrafeSpeed = 2.8f;
    [SerializeField] private float panicSpinSpeed = 380f;
    [SerializeField] private float panicHopHeight = 0.35f;
    [SerializeField] private float panicHopDuration = 0.35f;

    [Header("Search Route")]
    [SerializeField] private List<Transform> searchLocations = new List<Transform>();
    [SerializeField] private bool loopSearchLocations = true;
    [SerializeField] private bool randomizeSearchLocations = false;
    [SerializeField] private float searchLocationReachedDistance = 1.6f;
    [SerializeField] private float searchLocationLookTime = 0.65f;
    [SerializeField] private float searchLocationMaxTravelTime = 10f;
    [SerializeField] private float searchLocationNoProgressTimeout = 2.2f;
    [SerializeField] private float searchLocationProgressThreshold = 0.35f;
    [SerializeField] private float searchLocationPathInvalidTimeout = 0.9f;

    [Header("Destination Validation")]
    [SerializeField] private float maxDestinationSampleOffset = 2.2f;

    [Header("Visual Follow")]
    [SerializeField] private bool followVisualRoot = false;
    [SerializeField] private bool followVisualPosition = true;
    [SerializeField] private bool followVisualYaw = true;
    [SerializeField] private float visualFollowLerp = 25f;

    [Header("Doors")]
    [SerializeField] private float doorViewDistance = 14f;
    [SerializeField] private float doorSearchPriorityRadius = 22f;
    [SerializeField] private float doorActionDistance = 1.75f;
    [SerializeField] private float doorDetectRadius = 0.24f;
    [SerializeField] private float doorApproachOffset = 0.6f;
    [SerializeField] private float doorTransitOffset = 0.95f;
    [SerializeField] private float doorNavSampleRadius = 1.2f;
    [SerializeField] private float doorDestinationSampleOffset = 3.8f;
    [SerializeField] private LayerMask doorMask = ~0;
    [SerializeField] private bool requireDoorInViewForSearch = false;
    [SerializeField] private float doorOpenCooldown = 0.4f;
    [SerializeField] private float kickInterval = 0.65f;
    [SerializeField] private int kicksToBreakLockedDoor = 2;
    [SerializeField] private float kickRumbleImpulse = 180f;
    [SerializeField] private float kickForce = 24f;
    [SerializeField] private float kickUpwardForce = 4f;

    [Header("Audio")]
    [SerializeField] private AudioClip[] kickSounds;
    [SerializeField, Range(0f, 1f)] private float kickSoundVolume = 1f;
    [SerializeField] private AudioSource chaseLoopAudioSource;
    [SerializeField] private AudioClip chaseLoopClip;
    [SerializeField, Range(0f, 1f)] private float chaseLoopVolume = 0.8f;
    [SerializeField] private AudioSource catchAudioSource;
    [SerializeField] private AudioClip catchPlayerClip;
    [SerializeField, Range(0f, 1f)] private float catchPlayerVolume = 1f;
    [SerializeField] private float catchDistance = 1.2f;
    [SerializeField] private float sceneReloadDelay = 0.1f;

    [Header("Debug Gizmos")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private bool showPerceptionGizmos = true;
    [SerializeField] private bool showGoalGizmos = true;
    [SerializeField] private bool showDoorGizmos = true;
    [SerializeField] private bool showPathGizmos = true;

    private GoalType _goal = GoalType.None;
    private Vector3 _spawnPosition;
    private Vector3 _wanderTarget;
    private Door _focusedDoor;
    private float _nextDoorActionTime;
    private float _nextRepathTime;
    private int _searchLocationIndex;
    private float _nextSearchLocationMoveTime;
    private bool[] _searchLocationVisited;
    private int _searchLocationsVisitedCount;
    private float _searchLocationTargetStartTime;
    private float _searchLocationLastProgressTime;
    private float _searchLocationBestDistance = Mathf.Infinity;
    private float _searchLocationPathIssueSince = -1f;
    private Vector3 _lastStuckCheckPosition;
    private float _nextStuckCheckTime;
    private float _stuckAccumulatedTime;
    private float _nextPanicAllowedTime;
    private bool _hasTemporaryUnstickTarget;
    private Vector3 _temporaryUnstickTarget;
    private float _temporaryUnstickUntil;
    private bool _hasPlayerMemory;
    private float _playerMemoryUntil;
    private Vector3 _lastSeenPlayerPosition;
    private Vector3 _investigateTarget;
    private float _nextInvestigateRetargetTime;

    private readonly Dictionary<Door, int> _kickCounts = new Dictionary<Door, int>();
    private readonly Dictionary<Door, Collider[]> _doorColliderCache = new Dictionary<Door, Collider[]>();
    private readonly Collider[] _doorOverlapHits = new Collider[24];
    private readonly RaycastHit[] _doorForwardHits = new RaycastHit[16];

    private Vector3 _visualPositionOffset;
    private Quaternion _visualYawOffset = Quaternion.identity;
    private bool _isCatchingPlayer;
    private Coroutine _reloadCoroutine;
    private Coroutine _panicHopCoroutine;
    private PanicActionType _panicAction = PanicActionType.None;
    private float _panicActionUntil;
    private float _baseAgentOffset;

    private Vector3 _debugGoalTarget;
    private bool _debugCanSeePlayer;
    private Door _debugDoorAhead;
    private bool _debugIsStuck;
    private Vector3 _debugUnstickTarget;
    private int _debugUnstickAttempts;
    private PanicActionType _debugPanicAction;
    private Vector3 _debugDoorNavTarget;
    private bool _debugHasDoorNavTarget;

    private void Awake()
    {
        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (chaseLoopAudioSource == null)
        {
            chaseLoopAudioSource = audioSource;
        }

        if (catchAudioSource == null)
        {
            catchAudioSource = audioSource;
        }

        if (eyes == null)
        {
            eyes = transform;
        }

        if (visualRoot == null && transform.childCount > 0)
        {
            visualRoot = transform.GetChild(0);
        }

        if (agent != null)
        {
            agent.updateRotation = false;
            agent.speed = searchSpeed;
            _baseAgentOffset = agent.baseOffset;
        }

        _spawnPosition = transform.position;
        _lastStuckCheckPosition = FlattenY(transform.position);
        _nextStuckCheckTime = Time.time + stuckCheckInterval;
        CacheVisualFollowOffsets();
        ResolvePlayerReference();
        InitializeSearchRoute();
        PickNewWanderTarget();
    }

    private void Update()
    {
        if (_isCatchingPlayer)
        {
            return;
        }

        ResolvePlayerReference();
        GoalType previousGoal = _goal;
        EvaluateGoal();
        HandleChaseAudio(previousGoal, _goal);
        ExecuteGoal(Time.deltaTime);
        UpdateStuckRecovery();
        HandleDoorAction();
        UpdateFacingFromAgent(Time.deltaTime);
    }

    private void LateUpdate()
    {
        UpdateVisualFollow(Time.deltaTime);
    }

    private void EvaluateGoal()
    {
        _debugCanSeePlayer = CanSeePlayer();
        if (_debugCanSeePlayer)
        {
            _hasPlayerMemory = true;
            _lastSeenPlayerPosition = player.position;
            _playerMemoryUntil = Time.time + playerMemoryDuration;
            _investigateTarget = _lastSeenPlayerPosition;
            _nextInvestigateRetargetTime = Time.time + investigateRetargetInterval;
            _goal = GoalType.ChasePlayer;
            _focusedDoor = null;
            return;
        }

        if (_hasPlayerMemory && Time.time <= _playerMemoryUntil)
        {
            _goal = GoalType.InvestigatePlayer;
            _focusedDoor = null;
            return;
        }

        _hasPlayerMemory = false;
        Door visibleDoor = FindBestSearchDoor();
        if (visibleDoor != null)
        {
            _goal = GoalType.Door;
            _focusedDoor = visibleDoor;
            return;
        }

        _focusedDoor = null;
        if (_goal != GoalType.Wander)
        {
            _goal = GoalType.Wander;
            PickNewWanderTarget();
        }
    }

    private void ExecuteGoal(float deltaTime)
    {
        _debugGoalTarget = transform.position;
        _debugPanicAction = _panicAction;
        _debugHasDoorNavTarget = false;

        if (HandlePanicAction(deltaTime))
        {
            return;
        }

        if (_hasTemporaryUnstickTarget && Time.time < _temporaryUnstickUntil && _goal != GoalType.ChasePlayer)
        {
            float unstickDistance = Vector3.Distance(FlattenY(transform.position), FlattenY(_temporaryUnstickTarget));
            if (unstickDistance <= Mathf.Max(0.45f, searchPointReachedDistance * 0.5f))
            {
                _hasTemporaryUnstickTarget = false;
            }
            else
            {
                SetAgentSpeed(searchSpeed);
                _debugGoalTarget = _temporaryUnstickTarget;
                TrySetDestination(_temporaryUnstickTarget, true, unstickSampleRadius + 1f);
                return;
            }
        }

        if (_goal == GoalType.ChasePlayer && player != null)
        {
            float chaseDistance = Vector3.Distance(FlattenY(transform.position), FlattenY(player.position));
            if (chaseDistance <= catchDistance)
            {
                BeginCatchSequence();
                return;
            }

            SetAgentSpeed(chaseSpeed);
            _debugGoalTarget = player.position;
            TrySetDestination(player.position);
            return;
        }

        if (_goal == GoalType.InvestigatePlayer)
        {
            if (!_hasPlayerMemory || Time.time > _playerMemoryUntil)
            {
                _hasPlayerMemory = false;
                _goal = GoalType.Wander;
            }
            else
            {
                UpdateInvestigateTarget();
                SetAgentSpeed(searchSpeed);
                _debugGoalTarget = _investigateTarget;
                if (!TrySetDestination(_investigateTarget))
                {
                    _nextInvestigateRetargetTime = 0f;
                }
                return;
            }
        }

        if (_goal == GoalType.Door && _focusedDoor != null && !_focusedDoor.IsKickedDown)
        {
            Vector3 doorTarget = GetDoorApproachPoint(_focusedDoor, transform.position);
            SetAgentSpeed(searchSpeed);
            _debugGoalTarget = doorTarget;
            _debugDoorNavTarget = doorTarget;
            _debugHasDoorNavTarget = true;
            if (!TrySetDestination(doorTarget, false, doorDestinationSampleOffset))
            {
                _focusedDoor = null;
                _goal = GoalType.Wander;
                PickNewWanderTarget();
            }
            return;
        }

        if (_goal != GoalType.Wander)
        {
            _goal = GoalType.Wander;
            PickNewWanderTarget();
        }

        if (searchLocations.Count > 0 && Time.time >= _nextSearchLocationMoveTime)
        {
            _wanderTarget = searchLocations[_searchLocationIndex].position;
        }

        float reachedDistance = searchLocations.Count > 0 ? searchLocationReachedDistance : searchPointReachedDistance;
        float distanceToWanderPoint = Vector3.Distance(FlattenY(transform.position), FlattenY(_wanderTarget));
        if (searchLocations.Count > 0 && ShouldSkipCurrentSearchLocation(distanceToWanderPoint, reachedDistance))
        {
            AdvanceSearchLocation(true);
            return;
        }

        if (distanceToWanderPoint <= reachedDistance)
        {
            _debugGoalTarget = _wanderTarget;
            transform.Rotate(0f, lookAroundTurnSpeed * deltaTime, 0f, Space.Self);

            if (searchLocations.Count > 0)
            {
                MarkCurrentSearchLocationVisited();
                bool hasNearbySearchableDoor = HasSearchableDoorNearPosition(transform.position, doorViewDistance);
                AdvanceSearchLocation(!hasNearbySearchableDoor);
                return;
            }

            if (Random.value < 0.015f)
            {
                PickNewWanderTarget();
            }
            return;
        }

        SetAgentSpeed(searchSpeed);
        _debugGoalTarget = _wanderTarget;
        if (!TrySetDestination(_wanderTarget))
        {
            if (TryRouteViaNearbyDoor(_wanderTarget))
            {
                return;
            }

            if (searchLocations.Count > 0)
            {
                AdvanceSearchLocation(true);
            }
            else
            {
                PickNewWanderTarget();
            }
        }
    }

    private void HandleDoorAction()
    {
        if (Time.time < _nextDoorActionTime)
        {
            return;
        }

        Door door;
        RaycastHit hit;
        if (!TryFindDoorAhead(out door, out hit))
        {
            _debugDoorAhead = null;
            return;
        }
        _debugDoorAhead = door;

        if (door.IsKickedDown)
        {
            _kickCounts.Remove(door);
            return;
        }

        Vector3 doorActionPoint = GetDoorApproachPoint(door, transform.position);
        float distanceToDoor = Vector3.Distance(FlattenY(transform.position), FlattenY(doorActionPoint));
        if (distanceToDoor > doorActionDistance)
        {
            return;
        }

        if (door.IsLocked)
        {
            KickDoor(door, hit.point);
            return;
        }

        if (!door.IsOpen)
        {
            door.Interact();
            _kickCounts.Remove(door);
            _nextDoorActionTime = Time.time + doorOpenCooldown;
        }
    }

    private bool TrySetDestination(Vector3 target, bool forceRepath = false, float allowedSampleOffset = -1f)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return false;
        }

        if (!forceRepath && Time.time < _nextRepathTime)
        {
            return true;
        }

        Vector3 targetFlat = FlattenY(target) + Vector3.up * transform.position.y;
        Vector3 destination = targetFlat;
        bool usedSample = false;
        if (NavMesh.SamplePosition(targetFlat, out NavMeshHit sample, 3f, NavMesh.AllAreas))
        {
            destination = sample.position;
            usedSample = true;
        }

        float maxOffset = allowedSampleOffset >= 0f ? allowedSampleOffset : maxDestinationSampleOffset;
        if (usedSample && maxOffset > 0f)
        {
            float sampleOffset = Vector3.Distance(FlattenY(targetFlat), FlattenY(destination));
            if (sampleOffset > maxOffset)
            {
                _nextRepathTime = Time.time + repathInterval * 0.35f;
                return false;
            }
        }

        bool destinationSet = agent.SetDestination(destination);
        _nextRepathTime = Time.time + repathInterval;
        return destinationSet;
    }

    private void SetAgentSpeed(float speed)
    {
        if (agent == null)
        {
            return;
        }

        agent.speed = speed;
    }

    private void UpdateFacingFromAgent(float deltaTime)
    {
        if (agent == null || !agent.enabled)
        {
            return;
        }

        Vector3 desiredDirection = agent.desiredVelocity;
        desiredDirection.y = 0f;
        if (desiredDirection.sqrMagnitude <= 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(desiredDirection.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * deltaTime);
    }

    private void UpdateStuckRecovery()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        if (_panicAction != PanicActionType.None)
        {
            _lastStuckCheckPosition = FlattenY(transform.position);
            _nextStuckCheckTime = Time.time + stuckCheckInterval;
            return;
        }

        bool isTryingToMove = agent.hasPath
            && !agent.pathPending
            && agent.remainingDistance > Mathf.Max(agent.stoppingDistance + 0.25f, 0.75f);

        if (!isTryingToMove)
        {
            _stuckAccumulatedTime = Mathf.Max(0f, _stuckAccumulatedTime - Time.deltaTime);
            _debugIsStuck = false;
            _lastStuckCheckPosition = FlattenY(transform.position);
            _nextStuckCheckTime = Time.time + stuckCheckInterval;
            return;
        }

        if (Time.time < _nextStuckCheckTime)
        {
            return;
        }

        Vector3 currentPosition = FlattenY(transform.position);
        float moved = Vector3.Distance(currentPosition, _lastStuckCheckPosition);
        if (moved < stuckMinMoveDistance)
        {
            _stuckAccumulatedTime += stuckCheckInterval;
        }
        else
        {
            _stuckAccumulatedTime = Mathf.Max(0f, _stuckAccumulatedTime - stuckCheckInterval * 0.5f);
        }

        _debugIsStuck = _stuckAccumulatedTime > 0.01f;
        if (_stuckAccumulatedTime >= panicStuckTime && Time.time >= _nextPanicAllowedTime)
        {
            TriggerPanicAction();
            _stuckAccumulatedTime = stuckTimeToTrigger * 0.6f;
            _nextPanicAllowedTime = Time.time + panicActionCooldown;
            _lastStuckCheckPosition = currentPosition;
            _nextStuckCheckTime = Time.time + stuckCheckInterval;
            return;
        }

        if (_stuckAccumulatedTime >= stuckTimeToTrigger)
        {
            AttemptUnstick();
            _stuckAccumulatedTime = Mathf.Min(_stuckAccumulatedTime, panicStuckTime * 0.75f);
        }

        _lastStuckCheckPosition = currentPosition;
        _nextStuckCheckTime = Time.time + stuckCheckInterval;
    }

    private bool HandlePanicAction(float deltaTime)
    {
        if (_panicAction == PanicActionType.None || Time.time >= _panicActionUntil)
        {
            if (_panicAction != PanicActionType.None)
            {
                _panicAction = PanicActionType.None;
                if (agent != null)
                {
                    agent.baseOffset = _baseAgentOffset;
                }
            }
            return false;
        }

        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return false;
        }

        // Drop current path so panic action is not overridden by NavMesh steering.
        agent.ResetPath();

        switch (_panicAction)
        {
            case PanicActionType.Backpedal:
                agent.Move(-transform.forward * panicBackpedalSpeed * deltaTime);
                break;
            case PanicActionType.StrafeLeft:
                agent.Move(-transform.right * panicStrafeSpeed * deltaTime);
                break;
            case PanicActionType.StrafeRight:
                agent.Move(transform.right * panicStrafeSpeed * deltaTime);
                break;
            case PanicActionType.Spin:
                transform.Rotate(0f, panicSpinSpeed * deltaTime, 0f, Space.Self);
                break;
        }

        return true;
    }

    private void TriggerPanicAction()
    {
        int actionIndex = Random.Range(0, 4);
        _panicAction = (PanicActionType)(actionIndex + 1);
        _panicActionUntil = Time.time + Random.Range(panicActionMinDuration, panicActionMaxDuration);

        if (_hasTemporaryUnstickTarget)
        {
            _hasTemporaryUnstickTarget = false;
        }

        if (_panicHopCoroutine != null)
        {
            StopCoroutine(_panicHopCoroutine);
        }

        // Frequent mini-hop to break collision deadlocks around rotating doors.
        if (Random.value < 0.7f)
        {
            _panicHopCoroutine = StartCoroutine(PanicHopRoutine());
        }
    }

    private IEnumerator PanicHopRoutine()
    {
        if (agent == null || !agent.enabled)
        {
            yield break;
        }

        float duration = Mathf.Max(0.1f, panicHopDuration);
        float elapsed = 0f;
        while (elapsed < duration && agent != null && agent.enabled)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float arc = Mathf.Sin(t * Mathf.PI);
            agent.baseOffset = _baseAgentOffset + panicHopHeight * arc;
            yield return null;
        }

        if (agent != null)
        {
            agent.baseOffset = _baseAgentOffset;
        }

        _panicHopCoroutine = null;
    }

    private void AttemptUnstick()
    {
        if (TryForceDoorAction())
        {
            return;
        }

        Vector3 transitTarget = _goal == GoalType.Wander ? _wanderTarget : transform.position + transform.forward * 2f;
        if (TryRouteViaNearbyDoor(transitTarget))
        {
            return;
        }

        if (!TryPickTemporaryUnstickTarget(out Vector3 target, out int attempts))
        {
            if (_goal == GoalType.Wander && searchLocations.Count > 1)
            {
                AdvanceSearchLocation(true);
            }
            return;
        }

        _debugUnstickAttempts = attempts;
        _debugUnstickTarget = target;
        _hasTemporaryUnstickTarget = true;
        _temporaryUnstickTarget = target;
        _temporaryUnstickUntil = Time.time + unstickTargetHoldTime;
        TrySetDestination(_temporaryUnstickTarget, true, unstickSampleRadius + 1f);
    }

    private bool TryForceDoorAction()
    {
        Door door;
        RaycastHit hit;
        if (TryFindDoorAhead(out door, out hit))
        {
            Vector3 doorActionPoint = GetDoorApproachPoint(door, transform.position);
            float distanceToDoor = Vector3.Distance(FlattenY(transform.position), FlattenY(doorActionPoint));
            if (distanceToDoor <= doorForceInteractDistance)
            {
                if (door.IsLocked)
                {
                    KickDoor(door, hit.point);
                }
                else if (!door.IsOpen)
                {
                    door.Interact();
                    _nextDoorActionTime = Time.time + doorOpenCooldown * 0.5f;
                }
                return true;
            }
        }

        if (TryFindNearbyBlockingDoor(out door))
        {
            Vector3 doorActionPoint = GetDoorApproachPoint(door, transform.position);
            float distanceToDoor = Vector3.Distance(FlattenY(transform.position), FlattenY(doorActionPoint));
            if (distanceToDoor <= doorForceInteractDistance)
            {
                if (door.IsLocked)
                {
                    KickDoor(door, doorActionPoint);
                }
                else if (!door.IsOpen)
                {
                    door.Interact();
                    _nextDoorActionTime = Time.time + doorOpenCooldown * 0.5f;
                }
                return true;
            }
        }

        return false;
    }

    private bool TryFindNearbyBlockingDoor(out Door nearestDoor)
    {
        nearestDoor = null;
        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            doorForceInteractDistance,
            _doorOverlapHits,
            doorMask,
            QueryTriggerInteraction.Ignore
        );

        float nearestDistance = float.MaxValue;
        Vector3 moveDirection = agent != null && agent.desiredVelocity.sqrMagnitude > 0.05f
            ? agent.desiredVelocity.normalized
            : transform.forward;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = _doorOverlapHits[i];
            if (hitCollider == null)
            {
                continue;
            }

            Door door = hitCollider.GetComponentInParent<Door>();
            if (door == null || door.IsKickedDown || door.IsOpen)
            {
                continue;
            }

            Vector3 doorPoint = GetDoorReferencePoint(door);
            Vector3 toDoor = FlattenY(doorPoint - transform.position);
            if (toDoor.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            if (Vector3.Dot(toDoor.normalized, FlattenY(moveDirection).normalized) < -0.2f)
            {
                continue;
            }

            float distance = toDoor.magnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestDoor = door;
            }
        }

        return nearestDoor != null;
    }

    private bool TryPickTemporaryUnstickTarget(out Vector3 target, out int attemptsUsed)
    {
        target = transform.position;
        attemptsUsed = 0;

        if (agent == null || !agent.isOnNavMesh)
        {
            return false;
        }

        Vector3 origin = transform.position;
        int attempts = Mathf.Max(1, unstickSampleCount);
        for (int i = 0; i < attempts; i++)
        {
            attemptsUsed = i + 1;
            Vector2 offset = Random.insideUnitCircle * Mathf.Max(0.5f, unstickSampleRadius);
            Vector3 candidate = origin + new Vector3(offset.x, 0f, offset.y);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit sample, 1.8f, NavMesh.AllAreas))
            {
                continue;
            }

            if (NavMesh.Raycast(origin, sample.position, out _, NavMesh.AllAreas))
            {
                continue;
            }

            target = sample.position;
            return true;
        }

        return false;
    }

    private bool TryRouteViaNearbyDoor(Vector3 towardTarget)
    {
        Door doorToUse = null;
        if (_focusedDoor != null && !_focusedDoor.IsKickedDown)
        {
            doorToUse = _focusedDoor;
        }
        else if (!TryFindNearbyBlockingDoor(out doorToUse))
        {
            return false;
        }

        if (!TryGetDoorTransitPoint(doorToUse, towardTarget, out Vector3 transitPoint))
        {
            return false;
        }

        _hasTemporaryUnstickTarget = true;
        _temporaryUnstickTarget = transitPoint;
        _temporaryUnstickUntil = Time.time + Mathf.Max(0.6f, unstickTargetHoldTime);
        _debugDoorNavTarget = transitPoint;
        _debugHasDoorNavTarget = true;
        bool routed = TrySetDestination(transitPoint, true, doorDestinationSampleOffset);
        if (!routed)
        {
            _hasTemporaryUnstickTarget = false;
        }
        return routed;
    }

    private Vector3 GetDoorReferencePoint(Door door)
    {
        if (door == null)
        {
            return transform.position;
        }

        Vector3 fromPosition = transform.position;
        Vector3 referencePoint = door.transform.position;
        Collider[] doorColliders = GetDoorColliders(door);
        float bestDistance = float.MaxValue;

        for (int i = 0; i < doorColliders.Length; i++)
        {
            Collider doorCollider = doorColliders[i];
            if (doorCollider == null || !doorCollider.enabled)
            {
                continue;
            }

            Vector3 candidate = doorCollider.ClosestPoint(fromPosition);
            float candidateDistance = (candidate - fromPosition).sqrMagnitude;
            if (candidateDistance < bestDistance)
            {
                bestDistance = candidateDistance;
                referencePoint = candidate;
            }
        }

        referencePoint.y = transform.position.y;
        return referencePoint;
    }

    private Collider[] GetDoorColliders(Door door)
    {
        if (door == null)
        {
            return null;
        }

        if (_doorColliderCache.TryGetValue(door, out Collider[] cached) && cached != null && cached.Length > 0)
        {
            return cached;
        }

        Collider[] fetched = door.GetComponentsInChildren<Collider>(true);
        _doorColliderCache[door] = fetched;
        return fetched;
    }

    private Vector3 GetDoorNormal(Door door)
    {
        if (door == null)
        {
            return FlattenY(transform.forward).normalized;
        }

        Vector3 normal = FlattenY(door.transform.forward);
        if (normal.sqrMagnitude <= 0.0001f)
        {
            normal = FlattenY(door.transform.right);
        }
        if (normal.sqrMagnitude <= 0.0001f)
        {
            normal = FlattenY(transform.forward);
        }

        return normal.normalized;
    }

    private bool TrySampleNavMeshNear(Vector3 point, float sampleRadius, out Vector3 sampledPoint)
    {
        Vector3 flatPoint = FlattenY(point) + Vector3.up * transform.position.y;
        float radius = Mathf.Max(0.25f, sampleRadius);
        if (NavMesh.SamplePosition(flatPoint, out NavMeshHit sample, radius, NavMesh.AllAreas))
        {
            sampledPoint = sample.position;
            return true;
        }

        sampledPoint = flatPoint;
        return false;
    }

    private Vector3 GetDoorApproachPoint(Door door, Vector3 fromPosition)
    {
        Vector3 referencePoint = GetDoorReferencePoint(door);
        Vector3 toDoor = FlattenY(referencePoint - fromPosition);
        Vector3 approachDirection = toDoor.sqrMagnitude > 0.0001f ? toDoor.normalized : GetDoorNormal(door);

        Vector3 candidate = referencePoint - approachDirection * Mathf.Max(0.1f, doorApproachOffset);
        if (TrySampleNavMeshNear(candidate, doorNavSampleRadius, out Vector3 approachPoint))
        {
            return approachPoint;
        }

        if (TrySampleNavMeshNear(referencePoint, doorNavSampleRadius, out Vector3 fallbackPoint))
        {
            return fallbackPoint;
        }

        return referencePoint;
    }

    private bool TryGetDoorTransitPoint(Door door, Vector3 towardTarget, out Vector3 transitPoint)
    {
        transitPoint = GetDoorApproachPoint(door, transform.position);
        if (door == null)
        {
            return false;
        }

        Vector3 referencePoint = GetDoorReferencePoint(door);
        Vector3 doorwayNormal = GetDoorNormal(door);
        float offset = Mathf.Max(0.2f, doorTransitOffset);

        Vector3 sideA = referencePoint + doorwayNormal * offset;
        Vector3 sideB = referencePoint - doorwayNormal * offset;

        bool hasA = TrySampleNavMeshNear(sideA, doorNavSampleRadius, out Vector3 sampledA);
        bool hasB = TrySampleNavMeshNear(sideB, doorNavSampleRadius, out Vector3 sampledB);
        if (!hasA && !hasB)
        {
            return false;
        }

        Vector3 targetFlat = FlattenY(towardTarget);
        float scoreA = hasA ? Vector3.Distance(FlattenY(sampledA), targetFlat) : float.MaxValue;
        float scoreB = hasB ? Vector3.Distance(FlattenY(sampledB), targetFlat) : float.MaxValue;

        transitPoint = scoreA <= scoreB ? sampledA : sampledB;
        return true;
    }

    private Door FindBestSearchDoor()
    {
        Vector3 origin = eyes != null ? eyes.position : transform.position + Vector3.up * 1f;
        float searchRadius = Mathf.Max(doorViewDistance, doorSearchPriorityRadius);
        int hitCount = Physics.OverlapSphereNonAlloc(
            origin,
            searchRadius,
            _doorOverlapHits,
            doorMask,
            QueryTriggerInteraction.Ignore
        );

        Door bestDoor = null;
        float bestScore = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = _doorOverlapHits[i];
            if (hitCollider == null)
            {
                continue;
            }

            Door door = hitCollider.GetComponentInParent<Door>();
            if (door == null || door.IsKickedDown || door.IsOpen)
            {
                continue;
            }

            Vector3 doorPoint = GetDoorReferencePoint(door);
            if (requireDoorInViewForSearch && !CanSeePoint(doorPoint, door.transform))
            {
                continue;
            }

            float distance = Vector3.Distance(FlattenY(transform.position), FlattenY(doorPoint));
            float score = distance;
            if (door.IsLocked)
            {
                score -= 1.5f;
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestDoor = door;
            }
        }

        return bestDoor;
    }

    private bool TryFindDoorAhead(out Door foundDoor, out RaycastHit bestHit)
    {
        foundDoor = null;
        bestHit = default;

        Vector3 origin = eyes != null ? eyes.position : transform.position + Vector3.up * 1f;
        Vector3 direction = transform.forward;
        if (agent != null && agent.desiredVelocity.sqrMagnitude > 0.05f)
        {
            Vector3 v = agent.desiredVelocity;
            v.y = 0f;
            if (v.sqrMagnitude > 0.0001f)
            {
                direction = v.normalized;
            }
        }
        else if (agent != null && agent.hasPath && agent.path.corners.Length > 1)
        {
            Vector3 toCorner = agent.path.corners[1] - transform.position;
            toCorner.y = 0f;
            if (toCorner.sqrMagnitude > 0.0001f)
            {
                direction = toCorner.normalized;
            }
        }

        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            doorDetectRadius,
            direction,
            _doorForwardHits,
            Mathf.Max(doorActionDistance + 0.8f, 0.8f),
            doorMask,
            QueryTriggerInteraction.Ignore
        );

        float nearestDistance = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _doorForwardHits[i];
            if (hit.collider == null)
            {
                continue;
            }

            Door door = hit.collider.GetComponentInParent<Door>();
            if (door == null)
            {
                continue;
            }

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                foundDoor = door;
                bestHit = hit;
            }
        }

        return foundDoor != null;
    }

    private void KickDoor(Door door, Vector3 hitPoint)
    {
        door.Rumble(kickRumbleImpulse);
        PlayKickSound();

        if (!_kickCounts.TryGetValue(door, out int currentKicks))
        {
            currentKicks = 0;
        }

        currentKicks++;
        if (currentKicks >= kicksToBreakLockedDoor)
        {
            Vector3 doorPoint = GetDoorReferencePoint(door);
            Vector3 forceDirection = FlattenY(doorPoint - transform.position).normalized;
            if (forceDirection.sqrMagnitude <= 0.0001f)
            {
                forceDirection = transform.forward;
            }

            Vector3 kickImpulse = forceDirection * kickForce + Vector3.up * kickUpwardForce;
            Vector3 resolvedHitPoint = hitPoint.sqrMagnitude > 0.0001f ? hitPoint : doorPoint;
            door.KickDown(kickImpulse, resolvedHitPoint);
            _kickCounts.Remove(door);
        }
        else
        {
            _kickCounts[door] = currentKicks;
        }

        _nextDoorActionTime = Time.time + kickInterval;
    }

    private bool CanSeePlayer()
    {
        if (player == null)
        {
            return false;
        }

        Vector3 playerPoint = player.position + Vector3.up * playerEyeOffset;
        return CanSeePoint(playerPoint, player);
    }

    private bool CanSeePoint(Vector3 point, Transform expectedTransform)
    {
        if (eyes == null)
        {
            return false;
        }

        Vector3 origin = eyes.position;
        Vector3 toPoint = point - origin;
        float distance = toPoint.magnitude;
        if (distance <= 0.001f || distance > viewDistance)
        {
            return false;
        }

        Vector3 direction = toPoint / distance;
        if (Vector3.Angle(eyes.forward, direction) > viewAngle * 0.5f)
        {
            return false;
        }

        if (Physics.Raycast(origin, direction, out RaycastHit hit, distance, visionBlockerMask, QueryTriggerInteraction.Ignore))
        {
            if (expectedTransform != null)
            {
                return hit.transform == expectedTransform || hit.transform.IsChildOf(expectedTransform);
            }
            return false;
        }

        return true;
    }

    private void ResolvePlayerReference()
    {
        if (player != null)
        {
            return;
        }

        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
        if (playerObject != null)
        {
            player = playerObject.transform;
        }
    }

    private void PickNewWanderTarget()
    {
        if (searchLocations.Count > 0)
        {
            _wanderTarget = searchLocations[_searchLocationIndex].position;
            RefreshSearchLocationTracking();
            _debugGoalTarget = _wanderTarget;
            return;
        }

        Vector2 randomCircle = Random.insideUnitCircle * searchRadius;
        _wanderTarget = _spawnPosition + new Vector3(randomCircle.x, 0f, randomCircle.y);
        _debugGoalTarget = _wanderTarget;
    }

    private void InitializeSearchRoute()
    {
        for (int i = searchLocations.Count - 1; i >= 0; i--)
        {
            if (searchLocations[i] == null)
            {
                searchLocations.RemoveAt(i);
            }
        }

        if (searchLocations.Count == 0)
        {
            _searchLocationVisited = null;
            _searchLocationsVisitedCount = 0;
            return;
        }

        _searchLocationIndex = Mathf.Clamp(_searchLocationIndex, 0, searchLocations.Count - 1);
        EnsureSearchLocationVisitedState(true);
        if (randomizeSearchLocations)
        {
            _searchLocationIndex = Random.Range(0, searchLocations.Count);
        }

        _wanderTarget = searchLocations[_searchLocationIndex].position;
        _nextSearchLocationMoveTime = 0f;
        RefreshSearchLocationTracking();
    }

    private void AdvanceSearchLocation(bool moveImmediately)
    {
        if (searchLocations.Count == 0)
        {
            return;
        }

        MarkCurrentSearchLocationVisited();
        _nextSearchLocationMoveTime = moveImmediately ? Time.time : Time.time + searchLocationLookTime;
        _searchLocationIndex = GetNextSearchLocationIndex();
        _wanderTarget = searchLocations[_searchLocationIndex].position;
        RefreshSearchLocationTracking();
    }

    private void EnsureSearchLocationVisitedState(bool resetVisited = false)
    {
        int count = searchLocations.Count;
        if (count <= 0)
        {
            _searchLocationVisited = null;
            _searchLocationsVisitedCount = 0;
            return;
        }

        bool rebuild = _searchLocationVisited == null || _searchLocationVisited.Length != count;
        if (rebuild)
        {
            _searchLocationVisited = new bool[count];
            _searchLocationsVisitedCount = 0;
            return;
        }

        if (!resetVisited)
        {
            return;
        }

        for (int i = 0; i < _searchLocationVisited.Length; i++)
        {
            _searchLocationVisited[i] = false;
        }
        _searchLocationsVisitedCount = 0;
    }

    private void MarkCurrentSearchLocationVisited()
    {
        if (searchLocations.Count == 0)
        {
            return;
        }

        EnsureSearchLocationVisitedState();
        int index = Mathf.Clamp(_searchLocationIndex, 0, searchLocations.Count - 1);
        if (_searchLocationVisited[index])
        {
            return;
        }

        _searchLocationVisited[index] = true;
        _searchLocationsVisitedCount++;
    }

    private int GetNextSearchLocationIndex()
    {
        int count = searchLocations.Count;
        if (count <= 1)
        {
            return 0;
        }

        EnsureSearchLocationVisitedState();
        if (_searchLocationsVisitedCount >= count)
        {
            if (!loopSearchLocations && !randomizeSearchLocations)
            {
                return Mathf.Clamp(_searchLocationIndex, 0, count - 1);
            }

            EnsureSearchLocationVisitedState(true);
        }

        int current = Mathf.Clamp(_searchLocationIndex, 0, count - 1);
        if (randomizeSearchLocations)
        {
            int start = Random.Range(0, count);
            int fallback = current;
            for (int offset = 0; offset < count; offset++)
            {
                int index = (start + offset) % count;
                if (index == current)
                {
                    continue;
                }

                if (!_searchLocationVisited[index])
                {
                    return index;
                }

                fallback = index;
            }

            return fallback;
        }

        if (loopSearchLocations)
        {
            for (int offset = 1; offset <= count; offset++)
            {
                int index = (current + offset) % count;
                if (!_searchLocationVisited[index])
                {
                    return index;
                }
            }

            return (current + 1) % count;
        }

        for (int index = current + 1; index < count; index++)
        {
            if (!_searchLocationVisited[index])
            {
                return index;
            }
        }

        for (int index = 0; index < current; index++)
        {
            if (!_searchLocationVisited[index])
            {
                return index;
            }
        }

        return count - 1;
    }

    private void RefreshSearchLocationTracking()
    {
        _searchLocationTargetStartTime = Time.time;
        _searchLocationLastProgressTime = Time.time;
        _searchLocationPathIssueSince = -1f;

        if (searchLocations.Count == 0)
        {
            _searchLocationBestDistance = Mathf.Infinity;
            return;
        }

        _searchLocationBestDistance = Vector3.Distance(FlattenY(transform.position), FlattenY(_wanderTarget));
    }

    private bool ShouldSkipCurrentSearchLocation(float distanceToTarget, float reachedDistance)
    {
        if (searchLocations.Count == 0)
        {
            return false;
        }

        if (Time.time < _nextSearchLocationMoveTime)
        {
            return false;
        }

        if (distanceToTarget <= reachedDistance + 0.05f)
        {
            return false;
        }

        if (distanceToTarget + searchLocationProgressThreshold < _searchLocationBestDistance)
        {
            _searchLocationBestDistance = distanceToTarget;
            _searchLocationLastProgressTime = Time.time;
            _searchLocationPathIssueSince = -1f;
        }

        bool noProgressTooLong = Time.time - _searchLocationLastProgressTime >= searchLocationNoProgressTimeout;
        bool travelTimedOut = Time.time - _searchLocationTargetStartTime >= searchLocationMaxTravelTime;

        bool hasPathIssue = false;
        if (agent != null && agent.enabled && agent.isOnNavMesh && !agent.pathPending)
        {
            bool invalidPath = !agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid;
            bool partialPathFarAway = agent.pathStatus == NavMeshPathStatus.PathPartial
                && agent.remainingDistance > Mathf.Max(reachedDistance + 1f, 2f);

            if (invalidPath || partialPathFarAway)
            {
                if (_searchLocationPathIssueSince < 0f)
                {
                    _searchLocationPathIssueSince = Time.time;
                }

                hasPathIssue = Time.time - _searchLocationPathIssueSince >= searchLocationPathInvalidTimeout;
            }
            else
            {
                _searchLocationPathIssueSince = -1f;
            }
        }

        return noProgressTooLong || travelTimedOut || hasPathIssue;
    }

    private bool HasSearchableDoorNearPosition(Vector3 position, float radius)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(
            position,
            radius,
            _doorOverlapHits,
            doorMask,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = _doorOverlapHits[i];
            if (hitCollider == null)
            {
                continue;
            }

            Door door = hitCollider.GetComponentInParent<Door>();
            if (door == null || door.IsKickedDown || door.IsOpen)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void UpdateInvestigateTarget()
    {
        float distance = Vector3.Distance(FlattenY(transform.position), FlattenY(_investigateTarget));
        if (distance <= investigatePointReachedDistance || Time.time >= _nextInvestigateRetargetTime)
        {
            Vector2 offset = Random.insideUnitCircle * investigateRadius;
            _investigateTarget = _lastSeenPlayerPosition + new Vector3(offset.x, 0f, offset.y);
            _nextInvestigateRetargetTime = Time.time + investigateRetargetInterval;
        }
    }

    private static Vector3 FlattenY(Vector3 v)
    {
        return new Vector3(v.x, 0f, v.z);
    }

    private void PlayKickSound()
    {
        if (audioSource == null || kickSounds == null || kickSounds.Length == 0)
        {
            return;
        }

        int index = Random.Range(0, kickSounds.Length);
        AudioClip clip = kickSounds[index];
        if (clip != null)
        {
            audioSource.PlayOneShot(clip, kickSoundVolume);
        }
    }

    private void HandleChaseAudio(GoalType previousGoal, GoalType currentGoal)
    {
        if (_isCatchingPlayer)
        {
            StopChaseLoop();
            return;
        }

        bool enteredChase = previousGoal != GoalType.ChasePlayer && currentGoal == GoalType.ChasePlayer;
        bool exitedChase = previousGoal == GoalType.ChasePlayer && currentGoal != GoalType.ChasePlayer;
        bool enteredInvestigate = previousGoal != GoalType.InvestigatePlayer && currentGoal == GoalType.InvestigatePlayer;
        bool exitedInvestigate = previousGoal == GoalType.InvestigatePlayer && currentGoal != GoalType.InvestigatePlayer;

        if (enteredChase || enteredInvestigate)
        {
            StartChaseLoop();
        }
        else if (exitedChase || exitedInvestigate)
        {
            StopChaseLoop();
        }
    }

    private void StartChaseLoop()
    {
        if (chaseLoopAudioSource == null || chaseLoopClip == null)
        {
            return;
        }

        chaseLoopAudioSource.clip = chaseLoopClip;
        chaseLoopAudioSource.loop = true;
        chaseLoopAudioSource.volume = chaseLoopVolume;
        if (!chaseLoopAudioSource.isPlaying)
        {
            chaseLoopAudioSource.Play();
        }
    }

    private void StopChaseLoop()
    {
        if (chaseLoopAudioSource == null)
        {
            return;
        }

        if (chaseLoopAudioSource.isPlaying)
        {
            chaseLoopAudioSource.Stop();
        }
        chaseLoopAudioSource.loop = false;
        chaseLoopAudioSource.clip = null;
    }

    private void BeginCatchSequence()
    {
        if (_isCatchingPlayer)
        {
            return;
        }

        _isCatchingPlayer = true;
        StopChaseLoop();

        float reloadDelay = sceneReloadDelay;
        if (catchAudioSource != null && catchPlayerClip != null)
        {
            catchAudioSource.PlayOneShot(catchPlayerClip, catchPlayerVolume);
            reloadDelay += catchPlayerClip.length;
        }

        if (_reloadCoroutine != null)
        {
            StopCoroutine(_reloadCoroutine);
        }
        _reloadCoroutine = StartCoroutine(ReloadSceneAfterDelay(reloadDelay));
    }

    private IEnumerator ReloadSceneAfterDelay(float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.buildIndex);
    }

    private void CacheVisualFollowOffsets()
    {
        if (visualRoot == null)
        {
            return;
        }

        _visualPositionOffset = visualRoot.position - transform.position;
        Quaternion baseYaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        _visualYawOffset = Quaternion.Inverse(baseYaw) * visualRoot.rotation;
    }

    private void UpdateVisualFollow(float deltaTime)
    {
        if (!followVisualRoot || visualRoot == null)
        {
            return;
        }

        float t = visualFollowLerp > 0f ? 1f - Mathf.Exp(-visualFollowLerp * deltaTime) : 1f;

        if (followVisualPosition)
        {
            Vector3 targetPosition = transform.position + _visualPositionOffset;
            visualRoot.position = Vector3.Lerp(visualRoot.position, targetPosition, t);
        }

        if (followVisualYaw)
        {
            Quaternion targetYaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * _visualYawOffset;
            visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, targetYaw, t);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos)
        {
            return;
        }

        Vector3 eyeOrigin = eyes != null ? eyes.position : transform.position + Vector3.up * 1f;
        Vector3 forward = eyes != null ? eyes.forward : transform.forward;

        if (showPerceptionGizmos)
        {
            Gizmos.color = _debugCanSeePlayer ? Color.green : new Color(0f, 0.9f, 0.9f, 0.8f);
            Gizmos.DrawWireSphere(eyeOrigin, viewDistance);

            Vector3 left = Quaternion.Euler(0f, -viewAngle * 0.5f, 0f) * forward;
            Vector3 right = Quaternion.Euler(0f, viewAngle * 0.5f, 0f) * forward;
            Gizmos.DrawLine(eyeOrigin, eyeOrigin + left * viewDistance);
            Gizmos.DrawLine(eyeOrigin, eyeOrigin + right * viewDistance);

            if (player != null)
            {
                Gizmos.color = _debugCanSeePlayer ? Color.red : new Color(1f, 0.4f, 0.4f, 0.8f);
                Gizmos.DrawLine(eyeOrigin, player.position + Vector3.up * playerEyeOffset);
            }
        }

        if (showGoalGizmos)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position + Vector3.up * 0.15f, _debugGoalTarget + Vector3.up * 0.15f);
            Gizmos.DrawWireSphere(_debugGoalTarget, 0.2f);

            if (_hasPlayerMemory)
            {
                Gizmos.color = new Color(1f, 0.65f, 0f, 0.9f);
                Gizmos.DrawWireSphere(_lastSeenPlayerPosition + Vector3.up * 0.15f, 0.35f);
                Gizmos.DrawWireSphere(_investigateTarget + Vector3.up * 0.15f, 0.28f);
            }

            if (searchLocations.Count > 0)
            {
                for (int i = 0; i < searchLocations.Count; i++)
                {
                    Transform point = searchLocations[i];
                    if (point == null)
                    {
                        continue;
                    }

                    Gizmos.color = i == _searchLocationIndex ? Color.green : new Color(0.2f, 1f, 0.3f, 0.5f);
                    Gizmos.DrawWireCube(point.position + Vector3.up * 0.2f, new Vector3(0.35f, 0.35f, 0.35f));
                }
            }
        }

        if (showDoorGizmos)
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(eyeOrigin + forward * doorActionDistance, doorDetectRadius);
            Gizmos.DrawLine(eyeOrigin, eyeOrigin + forward * doorActionDistance);

            if (_focusedDoor != null)
            {
                Gizmos.color = Color.blue;
                Vector3 focusedDoorPoint = GetDoorApproachPoint(_focusedDoor, transform.position);
                Gizmos.DrawLine(transform.position + Vector3.up * 0.25f, focusedDoorPoint + Vector3.up * 0.25f);
            }

            if (_debugDoorAhead != null)
            {
                Gizmos.color = Color.red;
                Vector3 aheadDoorPoint = GetDoorApproachPoint(_debugDoorAhead, transform.position);
                Gizmos.DrawWireSphere(aheadDoorPoint + Vector3.up * 0.2f, 0.28f);
            }

            if (_debugHasDoorNavTarget)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position + Vector3.up * 0.22f, _debugDoorNavTarget + Vector3.up * 0.22f);
                Gizmos.DrawWireSphere(_debugDoorNavTarget + Vector3.up * 0.22f, 0.2f);
            }
        }

        if (showPathGizmos && agent != null && agent.hasPath)
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.7f);
            Vector3[] corners = agent.path.corners;
            for (int i = 0; i < corners.Length - 1; i++)
            {
                Gizmos.DrawLine(corners[i], corners[i + 1]);
            }

            if (_debugIsStuck)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.9f);
                Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.2f, 0.35f);
            }

            if (_hasTemporaryUnstickTarget)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
                Gizmos.DrawLine(transform.position + Vector3.up * 0.2f, _temporaryUnstickTarget + Vector3.up * 0.2f);
                Gizmos.DrawWireSphere(_temporaryUnstickTarget + Vector3.up * 0.2f, 0.25f);
            }
        }

        if (_debugPanicAction != PanicActionType.None)
        {
            Gizmos.color = new Color(1f, 0.2f, 1f, 0.95f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 1.05f, 0.22f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.2f, transform.position + Vector3.up * 1.05f);
        }
    }
}
