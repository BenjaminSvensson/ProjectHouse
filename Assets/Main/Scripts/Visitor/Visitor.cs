using System.Collections.Generic;
using UnityEngine;

public class Visitor : MonoBehaviour
{
    private enum GoalType
    {
        None,
        Wander,
        Door,
        ChasePlayer
    }

    [Header("References")]
    [SerializeField] private Transform eyes;
    [SerializeField] private Transform player;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private CharacterController controller;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Transform visualRoot;

    [Header("Perception")]
    [SerializeField] private float viewDistance = 22f;
    [SerializeField, Range(5f, 179f)] private float viewAngle = 95f;
    [SerializeField] private LayerMask visionBlockerMask = ~0;
    [SerializeField] private float playerEyeOffset = 1f;

    [Header("Movement")]
    [SerializeField] private float searchSpeed = 2.6f;
    [SerializeField] private float chaseSpeed = 5.8f;
    [SerializeField] private float turnSpeed = 180f;
    [SerializeField] private float gravity = -22f;
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

    [Header("Wall Avoidance")]
    [SerializeField] private float avoidProbeDistance = 1.2f;
    [SerializeField] private float avoidProbeRadius = 0.28f;
    [SerializeField] private float sideProbeAngle = 60f;
    [SerializeField] private LayerMask obstacleMask = ~0;
    [SerializeField] private int directionTryCount = 9;
    [SerializeField] private float directionTryAngleStep = 25f;

    [Header("Stuck Recovery")]
    [SerializeField] private float stuckCheckInterval = 0.25f;
    [SerializeField] private float stuckMinMoveDistance = 0.06f;
    [SerializeField] private float stuckTimeBeforeRetry = 0.9f;
    [SerializeField] private float forcedDirectionMinTime = 0.35f;
    [SerializeField] private float forcedDirectionMaxTime = 0.85f;

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

    [Header("Debug Gizmos")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private bool showPerceptionGizmos = true;
    [SerializeField] private bool showGoalGizmos = true;
    [SerializeField] private bool showAvoidanceGizmos = true;
    [SerializeField] private bool showDoorGizmos = true;

    private GoalType _goal = GoalType.None;
    private Vector3 _spawnPosition;
    private Vector3 _wanderTarget;
    private Door _focusedDoor;
    private float _verticalVelocity;
    private float _nextDoorActionTime;
    private int _searchLocationIndex;
    private float _nextSearchLocationMoveTime;

    private readonly Dictionary<Door, int> _kickCounts = new Dictionary<Door, int>();
    private readonly Collider[] _doorOverlapHits = new Collider[24];
    private readonly RaycastHit[] _doorForwardHits = new RaycastHit[16];
    private Vector3 _visualPositionOffset;
    private Quaternion _visualYawOffset = Quaternion.identity;
    private Vector3 _debugGoalTarget;
    private Vector3 _debugDesiredDirection;
    private Vector3 _debugSteeredDirection;
    private Vector3 _debugProbeOrigin;
    private Vector3 _debugForwardProbeDirection;
    private Vector3 _debugLeftProbeDirection;
    private Vector3 _debugRightProbeDirection;
    private bool _debugBlockedForward;
    private bool _debugBlockedLeft;
    private bool _debugBlockedRight;
    private bool _debugCanSeePlayer;
    private Door _debugDoorAhead;
    private int _debugDirectionAttempts;
    private bool _debugUsingForcedDirection;
    private Vector3 _debugForcedDirection;
    private bool _isTryingToMove;
    private Vector3 _lastStuckCheckPosition;
    private float _nextStuckCheckTime;
    private float _stuckAccumulatedTime;
    private bool _hasForcedDirection;
    private Vector3 _forcedDirection;
    private float _forcedDirectionUntil;

    private void Awake()
    {
        if (controller == null)
        {
            controller = GetComponent<CharacterController>();
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (eyes == null)
        {
            eyes = transform;
        }

        if (visualRoot == null && transform.childCount > 0)
        {
            visualRoot = transform.GetChild(0);
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
        ResolvePlayerReference();
        EvaluateGoal();
        ExecuteGoal(Time.deltaTime);
        UpdateStuckRecovery();
        HandleDoorAction();
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
            _goal = GoalType.ChasePlayer;
            _focusedDoor = null;
            return;
        }

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
        _isTryingToMove = false;
        _debugGoalTarget = transform.position;

        if (_goal == GoalType.ChasePlayer && player != null)
        {
            _debugGoalTarget = player.position;
            MoveTowards(player.position, chaseSpeed, deltaTime);
            return;
        }

        if (_goal == GoalType.Door && _focusedDoor != null && !_focusedDoor.IsKickedDown)
        {
            _debugGoalTarget = _focusedDoor.transform.position;
            MoveTowards(_focusedDoor.transform.position, searchSpeed, deltaTime);
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
                AdvanceSearchLocation();
                return;
            }

            if (Random.value < 0.015f)
            {
                PickNewWanderTarget();
            }
            return;
        }

        _debugGoalTarget = _wanderTarget;
        MoveTowards(_wanderTarget, searchSpeed, deltaTime);
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

    private void MoveTowards(Vector3 targetPosition, float speed, float deltaTime)
    {
        Vector3 flatPosition = FlattenY(transform.position);
        Vector3 flatTarget = FlattenY(targetPosition);
        Vector3 toTarget = flatTarget - flatPosition;

        if (toTarget.sqrMagnitude <= 0.0001f)
        {
            ApplyMotion(Vector3.zero, deltaTime);
            return;
        }

        _isTryingToMove = true;
        Vector3 desiredDirection = toTarget.normalized;
        _debugDesiredDirection = desiredDirection;
        Vector3 steeredDirection = ApplyWallAvoidance(desiredDirection);
        _debugSteeredDirection = steeredDirection;
        RotateTowards(steeredDirection, deltaTime);

        Vector3 horizontalVelocity = steeredDirection * speed;
        ApplyMotion(horizontalVelocity, deltaTime);
    }

    private Vector3 ApplyWallAvoidance(Vector3 desiredDirection)
    {
        Vector3 origin = eyes != null ? eyes.position : transform.position + Vector3.up * 1f;
        float castDistance = Mathf.Max(0.05f, avoidProbeDistance);
        Vector3 baseDirection = desiredDirection;

        if (_hasForcedDirection && Time.time < _forcedDirectionUntil)
        {
            baseDirection = Vector3.Slerp(desiredDirection, _forcedDirection, 0.88f).normalized;
            _debugUsingForcedDirection = true;
            _debugForcedDirection = _forcedDirection;
        }
        else
        {
            _hasForcedDirection = false;
            _debugUsingForcedDirection = false;
            _debugForcedDirection = Vector3.zero;
        }

        _debugProbeOrigin = origin;
        _debugForwardProbeDirection = baseDirection;
        _debugLeftProbeDirection = Quaternion.Euler(0f, -sideProbeAngle, 0f) * baseDirection;
        _debugRightProbeDirection = Quaternion.Euler(0f, sideProbeAngle, 0f) * baseDirection;
        _debugBlockedForward = false;
        _debugBlockedLeft = false;
        _debugBlockedRight = false;
        _debugDirectionAttempts = 0;

        _debugBlockedForward = IsDirectionBlocked(origin, _debugForwardProbeDirection, castDistance, out _, out _);
        _debugBlockedLeft = IsDirectionBlocked(origin, _debugLeftProbeDirection, castDistance, out _, out _);
        _debugBlockedRight = IsDirectionBlocked(origin, _debugRightProbeDirection, castDistance, out _, out _);

        int attempts = Mathf.Max(1, directionTryCount);
        for (int i = 0; i < attempts; i++)
        {
            float offsetAngle = DirectionOffsetByIndex(i);
            Vector3 candidateDirection = Quaternion.Euler(0f, offsetAngle, 0f) * baseDirection;
            _debugDirectionAttempts = i + 1;

            if (!IsDirectionBlocked(origin, candidateDirection, castDistance, out _, out _))
            {
                return candidateDirection.normalized;
            }
        }

        // No clear candidate: keep pushing forward so we still move and let stuck recovery pick new headings.
        return baseDirection;
    }

    private bool IsDirectionBlocked(Vector3 origin, Vector3 direction, float castDistance, out float hitDistance, out Vector3 hitNormal)
    {
        hitDistance = castDistance;
        hitNormal = Vector3.zero;

        if (direction.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        bool hitSomething = Physics.SphereCast(
            origin,
            avoidProbeRadius,
            direction.normalized,
            out RaycastHit hit,
            castDistance,
            obstacleMask,
            QueryTriggerInteraction.Ignore
        );

        if (!hitSomething)
        {
            return false;
        }

        if (IsIgnorableObstacle(hit.collider))
        {
            return false;
        }

        hitDistance = hit.distance;
        hitNormal = hit.normal;
        return true;
    }

    private float DirectionOffsetByIndex(int index)
    {
        if (index <= 0)
        {
            return 0f;
        }

        int ring = (index + 1) / 2;
        float sign = (index % 2 == 1) ? 1f : -1f;
        return sign * ring * directionTryAngleStep;
    }

    private bool IsIgnorableObstacle(Collider colliderHit)
    {
        if (colliderHit == null)
        {
            return true;
        }

        if (colliderHit.transform == transform || colliderHit.transform.IsChildOf(transform))
        {
            return true;
        }

        Door door = colliderHit.GetComponentInParent<Door>();
        if (door == null)
        {
            return false;
        }

        if (door.IsKickedDown || door.IsOpen)
        {
            return true;
        }

        return _focusedDoor != null && door == _focusedDoor;
    }

    private void RotateTowards(Vector3 direction, float deltaTime)
    {
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * deltaTime);
    }

    private void ApplyMotion(Vector3 horizontalVelocity, float deltaTime)
    {
        if (controller != null)
        {
            if (controller.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;
            }

            _verticalVelocity += gravity * deltaTime;
            Vector3 velocity = horizontalVelocity + Vector3.up * _verticalVelocity;
            controller.Move(velocity * deltaTime);
            return;
        }

        transform.position += horizontalVelocity * deltaTime;
    }

    private void UpdateStuckRecovery()
    {
        if (!_isTryingToMove)
        {
            _stuckAccumulatedTime = Mathf.Max(0f, _stuckAccumulatedTime - Time.deltaTime);
            _lastStuckCheckPosition = FlattenY(transform.position);
            _nextStuckCheckTime = Time.time + stuckCheckInterval;
            return;
        }

        if (Time.time < _nextStuckCheckTime)
        {
            return;
        }

        Vector3 currentFlatPosition = FlattenY(transform.position);
        float movedDistance = Vector3.Distance(currentFlatPosition, _lastStuckCheckPosition);
        if (movedDistance < stuckMinMoveDistance)
        {
            _stuckAccumulatedTime += stuckCheckInterval;
        }
        else
        {
            _stuckAccumulatedTime = Mathf.Max(0f, _stuckAccumulatedTime - stuckCheckInterval * 0.75f);
        }

        if (_stuckAccumulatedTime >= stuckTimeBeforeRetry)
        {
            ForceTryNewDirection();
            _stuckAccumulatedTime = 0f;
        }

        _lastStuckCheckPosition = currentFlatPosition;
        _nextStuckCheckTime = Time.time + stuckCheckInterval;
    }

    private void ForceTryNewDirection()
    {
        Vector3 origin = eyes != null ? eyes.position : transform.position + Vector3.up * 1f;
        Vector3 baseDirection = _debugDesiredDirection.sqrMagnitude > 0.0001f
            ? _debugDesiredDirection.normalized
            : transform.forward;

        Vector3 chosenDirection = baseDirection;
        int attempts = Mathf.Max(4, directionTryCount);
        for (int i = 0; i < attempts; i++)
        {
            float randomAngle = Random.Range(-170f, 170f);
            Vector3 candidateDirection = Quaternion.Euler(0f, randomAngle, 0f) * baseDirection;

            if (!IsDirectionBlocked(origin, candidateDirection, avoidProbeDistance * 0.95f, out _, out _))
            {
                chosenDirection = candidateDirection.normalized;
                break;
            }

            chosenDirection = candidateDirection.normalized;
        }

        _hasForcedDirection = true;
        _forcedDirection = chosenDirection;
        _forcedDirectionUntil = Time.time + Random.Range(forcedDirectionMinTime, forcedDirectionMaxTime);

        if (_goal == GoalType.Wander && Random.value < 0.35f)
        {
            PickNewWanderTarget();
        }
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
                // Prefer locked doors slightly so visitor aggressively clears blocked rooms.
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

    private bool CanSeePoint(Vector3 point)
    {
        return CanSeePoint(point, null);
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

    private void AdvanceSearchLocation()
    {
        if (searchLocations.Count == 0)
        {
            return;
        }

        _nextSearchLocationMoveTime = Time.time + searchLocationLookTime;
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
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_wanderTarget, 0.35f);
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position + Vector3.up * 0.15f, _debugGoalTarget + Vector3.up * 0.15f);
            Gizmos.DrawWireSphere(_debugGoalTarget, 0.2f);

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

                    if (i + 1 < searchLocations.Count && searchLocations[i + 1] != null)
                    {
                        Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.35f);
                        Gizmos.DrawLine(point.position, searchLocations[i + 1].position);
                    }
                }

                if (loopSearchLocations && searchLocations.Count > 1 && searchLocations[0] != null && searchLocations[searchLocations.Count - 1] != null)
                {
                    Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.2f);
                    Gizmos.DrawLine(searchLocations[searchLocations.Count - 1].position, searchLocations[0].position);
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

        if (showAvoidanceGizmos)
        {
            DrawProbe(_debugProbeOrigin, _debugForwardProbeDirection, _debugBlockedForward ? Color.red : Color.green);
            DrawProbe(_debugProbeOrigin, _debugLeftProbeDirection, _debugBlockedLeft ? Color.red : Color.green);
            DrawProbe(_debugProbeOrigin, _debugRightProbeDirection, _debugBlockedRight ? Color.red : Color.green);

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position + Vector3.up * 0.45f, transform.position + Vector3.up * 0.45f + _debugDesiredDirection * 1.6f);
            Gizmos.color = Color.white;
            Gizmos.DrawLine(transform.position + Vector3.up * 0.6f, transform.position + Vector3.up * 0.6f + _debugSteeredDirection * 1.6f);

            if (_debugUsingForcedDirection)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
                Gizmos.DrawLine(
                    transform.position + Vector3.up * 0.8f,
                    transform.position + Vector3.up * 0.8f + _debugForcedDirection * 1.7f
                );
            }
        }
    }

    private void DrawProbe(Vector3 origin, Vector3 direction, Color color)
    {
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Vector3 dir = direction.normalized;
        Vector3 end = origin + dir * avoidProbeDistance;
        Gizmos.color = color;
        Gizmos.DrawLine(origin, end);
        Gizmos.DrawWireSphere(end, avoidProbeRadius);
    }
}
