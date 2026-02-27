using UnityEngine;
using UnityEngine.InputSystem;

public interface IInteractable
{
    void Interact();
}

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private Camera playerCamera;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 4.25f;
    [SerializeField] private float runSpeed = 7f;
    [SerializeField] private float crouchSpeed = 2.3f;
    [SerializeField] private float acceleration = 18f;
    [SerializeField] private float airControl = 0.25f;
    [SerializeField] private float jumpHeight = 1.25f;
    [SerializeField] private float coyoteTime = 0.12f;
    [SerializeField] private float jumpBufferTime = 0.12f;
    [SerializeField] private float gravity = -22f;
    [SerializeField] private float groundedStickForce = -2f;

    [Header("Look")]
    [SerializeField] private float lookSensitivity = 0.12f;
    [SerializeField] private float lookSmoothing = 14f;
    [SerializeField] private float minPitch = -82f;
    [SerializeField] private float maxPitch = 86f;

    [Header("Crouch")]
    [SerializeField] private bool toggleCrouch = false;
    [SerializeField] private float standingHeight = 1.8f;
    [SerializeField] private float crouchingHeight = 1.12f;
    [SerializeField] private float crouchTransitionSpeed = 12f;
    [SerializeField] private float standingCameraHeight = 0.82f;
    [SerializeField] private float crouchingCameraHeight = 0.48f;
    [SerializeField] private LayerMask standUpCollisionMask = ~0;
    [SerializeField] private float standUpRadiusPadding = 0.01f;

    [Header("Lean")]
    [SerializeField] private float leanAngle = 10f;
    [SerializeField] private float leanOffset = 0.13f;
    [SerializeField] private float leanSmooth = 10f;

    [Header("Zoom (Scroll Wheel)")]
    [SerializeField] private float defaultFov = 80f;
    [SerializeField] private float minZoomFov = 38f;
    [SerializeField] private float maxZoomFov = 95f;
    [SerializeField] private float zoomStep = 4.5f;
    [SerializeField] private float zoomWheelScale = 1f;
    [SerializeField] private float zoomSmooth = 9f;

    [Header("Camera Motion")]
    [SerializeField] private float bobFrequency = 2f;
    [SerializeField] private float bobAmplitude = 0.11f;
    [SerializeField] private float runBobMultiplier = 1.45f;
    [SerializeField] private float crouchBobMultiplier = 0.72f;
    [SerializeField] private float movementTilt = 3.5f;
    [SerializeField] private float lookTilt = 1.5f;
    [SerializeField] private float bobSmoothing = 12f;
    [SerializeField] private float landingKickPosition = 0.065f;
    [SerializeField] private float landingKickRotation = 4.8f;
    [SerializeField] private float landingKickDamping = 10f;

    [Header("Camera Collision")]
    [SerializeField] private bool preventCameraClipping = true;
    [SerializeField] private LayerMask cameraCollisionMask = ~0;
    [SerializeField] private float cameraCollisionRadius = 0.12f;
    [SerializeField] private float cameraCollisionPadding = 0.02f;

    [Header("Interaction")]
    [SerializeField] private float interactionDistance = 2.5f;
    [SerializeField] private float interactionRadius = 0.08f;
    [SerializeField] private LayerMask interactionMask = ~0;

    private CharacterController _controller;
    private InputSystem_Actions _actions;

    private Vector2 _moveInput;
    private Vector2 _lookInput;
    private Vector2 _smoothedLook;

    private Vector3 _horizontalVelocity;
    private float _verticalVelocity;
    private float _coyoteTimer;
    private float _jumpBufferTimer;
    private float _pitch;
    private float _yaw;

    private bool _isGrounded;
    private bool _wasGrounded;
    private bool _isCrouching;
    private bool _crouchHeld;

    private float _targetHeight;
    private float _targetCameraHeight;
    private float _currentCameraHeight;
    private float _targetFov;

    private float _lean;
    private float _bobCycle;
    private Vector3 _bobPosition;
    private Vector3 _bobRotation;
    private Vector3 _baseCameraLocalPosition;
    private float _landingPositionImpulse;
    private float _landingRotationImpulse;
    private readonly RaycastHit[] _cameraCollisionHits = new RaycastHit[8];
    private readonly Collider[] _cameraOverlapHits = new Collider[16];
    private readonly RaycastHit[] _interactionHits = new RaycastHit[16];
    private readonly Collider[] _standUpHits = new Collider[16];

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _actions = new InputSystem_Actions();
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>();
        }

        if (cameraPivot == null && playerCamera != null)
        {
            cameraPivot = playerCamera.transform;
        }

        if (cameraPivot == null)
        {
            cameraPivot = transform;
        }

        _yaw = transform.eulerAngles.y;
        _pitch = 0f;

        _targetHeight = standingHeight;
        _targetCameraHeight = standingCameraHeight;
        _targetFov = maxZoomFov;

        _controller.height = standingHeight;
        _controller.center = new Vector3(0f, standingHeight * 0.5f, 0f);

        if (cameraPivot != null)
        {
            _baseCameraLocalPosition = cameraPivot.localPosition;
            _baseCameraLocalPosition.y = standingCameraHeight;
            _currentCameraHeight = standingCameraHeight;
            cameraPivot.localPosition = _baseCameraLocalPosition;
        }

        if (playerCamera != null)
        {
            playerCamera.fieldOfView = maxZoomFov;
        }
    }

    private void OnEnable()
    {
        _actions.Player.Enable();
        _actions.Player.Crouch.started += OnCrouchStarted;
        _actions.Player.Crouch.canceled += OnCrouchCanceled;
        LockCursor();
    }

    private void OnDisable()
    {
        _actions.Player.Crouch.started -= OnCrouchStarted;
        _actions.Player.Crouch.canceled -= OnCrouchCanceled;
        _actions.Player.Disable();
        UnlockCursor();
    }

    private void OnDestroy()
    {
        _actions?.Dispose();
    }

    private void Update()
    {
        ReadInput();
        HandleInteraction();
        HandleLook(Time.deltaTime);
        HandleMovement(Time.deltaTime);
        HandleZoom(Time.deltaTime);
        HandleCameraMotion(Time.deltaTime);
    }

    private void ReadInput()
    {
        _moveInput = _actions.Player.Move.ReadValue<Vector2>();
        _lookInput = _actions.Player.Look.ReadValue<Vector2>();
    }

    private void HandleLook(float deltaTime)
    {
        Vector2 lookDelta = _lookInput * lookSensitivity;
        _smoothedLook = Vector2.Lerp(_smoothedLook, lookDelta, 1f - Mathf.Exp(-lookSmoothing * deltaTime));

        _yaw += _smoothedLook.x;
        _pitch -= _smoothedLook.y;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
    }

    private void HandleMovement(float deltaTime)
    {
        _wasGrounded = _isGrounded;
        _isGrounded = _controller.isGrounded;

        bool jumpPressed = _actions.Player.Jump.WasPressedThisFrame();
        bool runHeld = _actions.Player.Run.IsPressed();

        if (jumpPressed)
        {
            _jumpBufferTimer = jumpBufferTime;
        }
        else
        {
            _jumpBufferTimer -= deltaTime;
        }

        bool wantsCrouch = _isCrouching;
        if (toggleCrouch)
        {
            if (_actions.Player.Crouch.WasPressedThisFrame())
            {
                wantsCrouch = !_isCrouching;
            }
        }
        else
        {
            wantsCrouch = _crouchHeld;
        }

        if (!wantsCrouch && !CanStandUp())
        {
            _isCrouching = true;
        }
        else
        {
            _isCrouching = wantsCrouch;
        }

        float speed = walkSpeed;
        if (_isCrouching)
        {
            speed = crouchSpeed;
        }
        else if (runHeld && _moveInput.y > 0.1f)
        {
            speed = runSpeed;
        }

        Vector3 desiredMove = (transform.right * _moveInput.x + transform.forward * _moveInput.y);
        desiredMove = Vector3.ClampMagnitude(desiredMove, 1f) * speed;

        float effectiveAcceleration = _isGrounded ? acceleration : acceleration * airControl;
        _horizontalVelocity = Vector3.Lerp(_horizontalVelocity, desiredMove, 1f - Mathf.Exp(-effectiveAcceleration * deltaTime));

        if (_isGrounded)
        {
            _coyoteTimer = coyoteTime;

            if (!_wasGrounded && _verticalVelocity < -8f)
            {
                float impact = Mathf.InverseLerp(8f, 26f, -_verticalVelocity);
                _landingPositionImpulse += landingKickPosition * impact;
                _landingRotationImpulse += landingKickRotation * impact;
            }

            _verticalVelocity = groundedStickForce;
        }
        else
        {
            _coyoteTimer -= deltaTime;
            _verticalVelocity += gravity * deltaTime;
        }

        if (!_isCrouching && _jumpBufferTimer > 0f && _coyoteTimer > 0f)
        {
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;
        }

        Vector3 frameVelocity = _horizontalVelocity + Vector3.up * _verticalVelocity;
        _controller.Move(frameVelocity * deltaTime);

        _targetHeight = _isCrouching ? crouchingHeight : standingHeight;
        _targetCameraHeight = _isCrouching ? crouchingCameraHeight : standingCameraHeight;

        _controller.height = Mathf.Lerp(_controller.height, _targetHeight, 1f - Mathf.Exp(-crouchTransitionSpeed * deltaTime));
        _controller.center = new Vector3(0f, _controller.height * 0.5f, 0f);
    }

    private void HandleZoom(float deltaTime)
    {
        float scrollY = 0f;
        if (Mouse.current != null)
        {
            scrollY = Mouse.current.scroll.ReadValue().y;
        }

        if (Mathf.Abs(scrollY) > 0.001f)
        {
            float scrollSteps = Mathf.Abs(scrollY) > 20f ? scrollY / 120f : scrollY;
            scrollSteps *= zoomWheelScale;
            _targetFov -= scrollSteps * zoomStep;
        }

        _targetFov = Mathf.Clamp(_targetFov, minZoomFov, maxZoomFov);

        if (playerCamera != null)
        {
            playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, _targetFov, 1f - Mathf.Exp(-zoomSmooth * deltaTime));
        }
    }

    private void HandleInteraction()
    {
        if (!_actions.Player.Interact.WasPressedThisFrame())
        {
            return;
        }

        if (playerCamera == null)
        {
            return;
        }

        Transform cameraTransform = playerCamera.transform;
        Vector3 origin = cameraTransform.position;
        Vector3 direction = cameraTransform.forward;

        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            interactionRadius,
            direction,
            _interactionHits,
            interactionDistance,
            interactionMask,
            QueryTriggerInteraction.Ignore
        );

        IInteractable bestInteractable = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _interactionHits[i];
            if (hit.collider == null)
            {
                continue;
            }

            IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (interactable == null)
            {
                continue;
            }

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                bestInteractable = interactable;
            }
        }

        bestInteractable?.Interact();
    }

    private void HandleCameraMotion(float deltaTime)
    {
        if (cameraPivot == null)
        {
            return;
        }

        float horizontalSpeed = new Vector2(_horizontalVelocity.x, _horizontalVelocity.z).magnitude;
        float speedRatio = Mathf.Clamp01(horizontalSpeed / runSpeed);

        float bobMultiplier = 1f;
        if (_isCrouching)
        {
            bobMultiplier *= crouchBobMultiplier;
        }
        else if (_actions.Player.Run.IsPressed())
        {
            bobMultiplier *= runBobMultiplier;
        }

        float bobStrength = speedRatio * bobMultiplier;
        if (_isGrounded && speedRatio > 0.05f)
        {
            _bobCycle += deltaTime * bobFrequency * (0.7f + speedRatio * 1.8f);
            float sinA = Mathf.Sin(_bobCycle * Mathf.PI * 2f);
            float sinB = Mathf.Sin(_bobCycle * Mathf.PI * 4f);

            _bobPosition = new Vector3(sinA * bobAmplitude * 0.6f, Mathf.Abs(sinB) * bobAmplitude, 0f) * bobStrength;
            _bobRotation = new Vector3(-Mathf.Abs(sinB) * bobAmplitude * 28f, 0f, sinA * bobAmplitude * 42f) * bobStrength;
        }
        else
        {
            _bobPosition = Vector3.Lerp(_bobPosition, Vector3.zero, 1f - Mathf.Exp(-bobSmoothing * deltaTime));
            _bobRotation = Vector3.Lerp(_bobRotation, Vector3.zero, 1f - Mathf.Exp(-bobSmoothing * deltaTime));
        }

        float leanInput = 0f;
        if (_actions.Player.LeanLeft.IsPressed())
        {
            leanInput -= 1f;
        }
        if (_actions.Player.LeanRight.IsPressed())
        {
            leanInput += 1f;
        }
        leanInput = Mathf.Clamp(leanInput, -1f, 1f);
        _lean = Mathf.Lerp(_lean, leanInput, 1f - Mathf.Exp(-leanSmooth * deltaTime));

        _landingPositionImpulse = Mathf.Lerp(_landingPositionImpulse, 0f, 1f - Mathf.Exp(-landingKickDamping * deltaTime));
        _landingRotationImpulse = Mathf.Lerp(_landingRotationImpulse, 0f, 1f - Mathf.Exp(-landingKickDamping * deltaTime));

        _currentCameraHeight = Mathf.Lerp(_currentCameraHeight, _targetCameraHeight, 1f - Mathf.Exp(-crouchTransitionSpeed * deltaTime));

        Vector3 desiredLocalPosition = _baseCameraLocalPosition;
        desiredLocalPosition.y = _currentCameraHeight;
        desiredLocalPosition.x += _lean * leanOffset;
        desiredLocalPosition += _bobPosition;
        desiredLocalPosition.y -= _landingPositionImpulse;
        desiredLocalPosition = ResolveCameraCollision(desiredLocalPosition);

        float strafeTilt = -_moveInput.x * movementTilt;
        float lookRoll = -_smoothedLook.x * lookTilt;
        float totalRoll = _lean * -leanAngle + strafeTilt + lookRoll + _bobRotation.z - _landingRotationImpulse;
        float totalPitch = _pitch + _bobRotation.x + Mathf.Clamp(-_smoothedLook.y * lookTilt, -4f, 4f);

        Quaternion targetLocalRotation = Quaternion.Euler(totalPitch, 0f, totalRoll);

        cameraPivot.localPosition = Vector3.Lerp(cameraPivot.localPosition, desiredLocalPosition, 1f - Mathf.Exp(-bobSmoothing * deltaTime));
        cameraPivot.localRotation = Quaternion.Slerp(cameraPivot.localRotation, targetLocalRotation, 1f - Mathf.Exp(-(bobSmoothing + 4f) * deltaTime));
    }

    private Vector3 ResolveCameraCollision(Vector3 desiredLocalPosition)
    {
        if (!preventCameraClipping)
        {
            return desiredLocalPosition;
        }

        Transform cameraSpace = cameraPivot.parent != null ? cameraPivot.parent : transform;

        Vector3 anchorLocalPosition = _baseCameraLocalPosition;
        anchorLocalPosition.y = desiredLocalPosition.y;

        Vector3 anchorWorldPosition = cameraSpace.TransformPoint(anchorLocalPosition);
        Vector3 targetWorldPosition = cameraSpace.TransformPoint(desiredLocalPosition);
        Vector3 castVector = targetWorldPosition - anchorWorldPosition;
        float castDistance = castVector.magnitude;

        if (castDistance <= 0.0001f)
        {
            return desiredLocalPosition;
        }

        Vector3 castDirection = castVector / castDistance;
        int hitCount = Physics.SphereCastNonAlloc(
            anchorWorldPosition,
            cameraCollisionRadius,
            castDirection,
            _cameraCollisionHits,
            castDistance + cameraCollisionPadding,
            cameraCollisionMask,
            QueryTriggerInteraction.Ignore
        );

        float nearestDistance = castDistance;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _cameraCollisionHits[i];
            if (hit.collider == null)
            {
                continue;
            }

            if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            float hitDistance = Mathf.Max(0f, hit.distance - cameraCollisionPadding);
            if (hitDistance < nearestDistance)
            {
                nearestDistance = hitDistance;
            }
        }

        float allowedDistance = nearestDistance;
        bool targetBlocked = IsCameraPositionBlocked(targetWorldPosition);

        if (targetBlocked)
        {
            float low = 0f;
            float high = allowedDistance;
            for (int i = 0; i < 8; i++)
            {
                float mid = (low + high) * 0.5f;
                Vector3 midPosition = anchorWorldPosition + castDirection * mid;
                if (IsCameraPositionBlocked(midPosition))
                {
                    high = mid;
                }
                else
                {
                    low = mid;
                }
            }

            allowedDistance = low;
        }

        if (!targetBlocked && allowedDistance >= castDistance)
        {
            return desiredLocalPosition;
        }

        Vector3 clippedWorldPosition = anchorWorldPosition + castDirection * allowedDistance;
        return cameraSpace.InverseTransformPoint(clippedWorldPosition);
    }

    private bool CanStandUp()
    {
        float radius = Mathf.Max(0.01f, _controller.radius - standUpRadiusPadding);
        float capsuleHeight = Mathf.Max(standingHeight, radius * 2f + 0.01f);

        Vector3 feetWorldPosition = transform.TransformPoint(new Vector3(_controller.center.x, 0f, _controller.center.z));
        Vector3 capsuleBottom = feetWorldPosition + Vector3.up * radius;
        Vector3 capsuleTop = feetWorldPosition + Vector3.up * (capsuleHeight - radius);

        int hitCount = Physics.OverlapCapsuleNonAlloc(
            capsuleBottom,
            capsuleTop,
            radius,
            _standUpHits,
            standUpCollisionMask,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _standUpHits[i];
            if (hit == null)
            {
                continue;
            }

            if (hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private bool IsCameraPositionBlocked(Vector3 worldPosition)
    {
        int overlapCount = Physics.OverlapSphereNonAlloc(
            worldPosition,
            cameraCollisionRadius,
            _cameraOverlapHits,
            cameraCollisionMask,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < overlapCount; i++)
        {
            Collider colliderHit = _cameraOverlapHits[i];
            if (colliderHit == null)
            {
                continue;
            }

            if (colliderHit.transform == transform || colliderHit.transform.IsChildOf(transform))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && isActiveAndEnabled)
        {
            LockCursor();
        }
    }

    private void OnCrouchStarted(InputAction.CallbackContext _)
    {
        _crouchHeld = true;
    }

    private void OnCrouchCanceled(InputAction.CallbackContext _)
    {
        _crouchHeld = false;
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
