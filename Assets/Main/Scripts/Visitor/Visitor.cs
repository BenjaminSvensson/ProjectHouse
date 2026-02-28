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

    [Header("Search Route")]
    [SerializeField] private List<Transform> searchLocations = new List<Transform>();
    [SerializeField] private bool loopSearchLocations = true;
    [SerializeField] private bool randomizeSearchLocations = false;
    [SerializeField] private float searchLocationReachedDistance = 1.6f;
    [SerializeField] private float searchLocationLookTime = 0.65f;

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
    private bool _hasPlayerMemory;
    private float _playerMemoryUntil;
    private Vector3 _lastSeenPlayerPosition;
    private Vector3 _investigateTarget;
    private float _nextInvestigateRetargetTime;

    private readonly Dictionary<Door, int> _kickCounts = new Dictionary<Door, int>();
    private readonly Collider[] _doorOverlapHits = new Collider[24];
    private readonly RaycastHit[] _doorForwardHits = new RaycastHit[16];

    private Vector3 _visualPositionOffset;
    private Quaternion _visualYawOffset = Quaternion.identity;
    private bool _isCatchingPlayer;
    private Coroutine _reloadCoroutine;

    private Vector3 _debugGoalTarget;
    private bool _debugCanSeePlayer;
    private Door _debugDoorAhead;

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
        }

        _spawnPosition = transform.position;
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
                TrySetDestination(_investigateTarget);
                return;
            }
        }

        if (_goal == GoalType.Door && _focusedDoor != null && !_focusedDoor.IsKickedDown)
        {
            SetAgentSpeed(searchSpeed);
            _debugGoalTarget = _focusedDoor.transform.position;
            TrySetDestination(_focusedDoor.transform.position);
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
        if (distanceToWanderPoint <= reachedDistance)
        {
            _debugGoalTarget = _wanderTarget;
            transform.Rotate(0f, lookAroundTurnSpeed * deltaTime, 0f, Space.Self);

            if (searchLocations.Count > 0)
            {
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
        TrySetDestination(_wanderTarget);
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

        float distanceToDoor = Vector3.Distance(FlattenY(transform.position), FlattenY(door.transform.position));
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

    private void TrySetDestination(Vector3 target)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        if (Time.time < _nextRepathTime)
        {
            return;
        }

        Vector3 targetFlat = FlattenY(target) + Vector3.up * transform.position.y;
        if (NavMesh.SamplePosition(targetFlat, out NavMeshHit sample, 3f, NavMesh.AllAreas))
        {
            agent.SetDestination(sample.position);
        }
        else
        {
            agent.SetDestination(targetFlat);
        }

        _nextRepathTime = Time.time + repathInterval;
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

            Vector3 doorPoint = door.transform.position;
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

        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            doorDetectRadius,
            direction,
            _doorForwardHits,
            Mathf.Max(doorActionDistance, 0.5f),
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
            Vector3 forceDirection = FlattenY(door.transform.position - transform.position).normalized;
            if (forceDirection.sqrMagnitude <= 0.0001f)
            {
                forceDirection = transform.forward;
            }

            Vector3 kickImpulse = forceDirection * kickForce + Vector3.up * kickUpwardForce;
            Vector3 resolvedHitPoint = hitPoint.sqrMagnitude > 0.0001f ? hitPoint : door.transform.position;
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
            return;
        }

        _searchLocationIndex = Mathf.Clamp(_searchLocationIndex, 0, searchLocations.Count - 1);
        if (randomizeSearchLocations)
        {
            _searchLocationIndex = Random.Range(0, searchLocations.Count);
        }

        _wanderTarget = searchLocations[_searchLocationIndex].position;
        _nextSearchLocationMoveTime = 0f;
    }

    private void AdvanceSearchLocation(bool moveImmediately)
    {
        if (searchLocations.Count == 0)
        {
            return;
        }

        _nextSearchLocationMoveTime = moveImmediately ? Time.time : Time.time + searchLocationLookTime;
        if (randomizeSearchLocations)
        {
            int nextIndex = _searchLocationIndex;
            if (searchLocations.Count > 1)
            {
                while (nextIndex == _searchLocationIndex)
                {
                    nextIndex = Random.Range(0, searchLocations.Count);
                }
            }

            _searchLocationIndex = nextIndex;
            _wanderTarget = searchLocations[_searchLocationIndex].position;
            return;
        }

        _searchLocationIndex++;
        if (_searchLocationIndex >= searchLocations.Count)
        {
            _searchLocationIndex = loopSearchLocations ? 0 : searchLocations.Count - 1;
        }

        _wanderTarget = searchLocations[_searchLocationIndex].position;
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
                Gizmos.DrawLine(transform.position + Vector3.up * 0.25f, _focusedDoor.transform.position + Vector3.up * 0.25f);
            }

            if (_debugDoorAhead != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(_debugDoorAhead.transform.position + Vector3.up * 0.2f, 0.28f);
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
        }
    }
}
