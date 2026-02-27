using UnityEngine;

public class Door : MonoBehaviour, IInteractable
{
    [Header("References")]
    [SerializeField] private Transform hinge;
    [SerializeField] private AudioSource audioSource;

    [Header("Pose (Local To Hinge)")]
    [SerializeField] private Vector3 closedLocalEuler = Vector3.zero;
    [SerializeField] private Vector3 openLocalEuler = new Vector3(0f, 95f, 0f);
    [SerializeField] private bool startsOpen = false;

    [Header("Animation")]
    [SerializeField] private float rotateSmoothTime = 0.16f;

    [Header("Lock")]
    [SerializeField] private bool startsLocked = false;
    [SerializeField] private float lockedShakeImpulse = 75f;

    [Header("Sway")]
    [SerializeField] private Vector3 hingeAxis = Vector3.up;
    [SerializeField] private float swayImpulse = 55f;
    [SerializeField] private float swayStiffness = 42f;
    [SerializeField] private float swayDamping = 10f;
    [SerializeField] private float maxSwayAngle = 10f;

    [Header("Audio")]
    [SerializeField] private AudioClip openSound;
    [SerializeField] private AudioClip closeSound;
    [SerializeField] private AudioClip lockedSound;
    [SerializeField] private AudioClip kickedDownSound;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 1f;

    [Header("Kick Down")]
    [SerializeField] private float kickDownAngle = 140f;
    [SerializeField] private float kickDownRotateSpeed = 460f;
    [SerializeField] private bool kickDirectionFromForce = true;
    [SerializeField] private bool disableCollisionOnKickDown = true;

    private bool _isOpen;
    private bool _isLocked;
    private bool _isKickedDown;
    private bool _isKickingDown;
    private float _blend;
    private float _blendVelocity;
    private float _swayAngle;
    private float _swayVelocity;
    private float _lockedShakeDirection = 1f;
    private Quaternion _kickDownTargetRotation;

    public bool IsOpen => _isOpen;
    public bool IsLocked => _isLocked;
    public bool IsKickedDown => _isKickedDown;

    private void Awake()
    {
        if (hinge == null)
        {
            hinge = transform;
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        _isOpen = startsOpen;
        _isLocked = startsLocked;
        _blend = _isOpen ? 1f : 0f;
        ApplyDoorRotation();
    }

    private void Update()
    {
        if (_isKickedDown)
        {
            if (_isKickingDown && hinge != null)
            {
                hinge.localRotation = Quaternion.RotateTowards(
                    hinge.localRotation,
                    _kickDownTargetRotation,
                    kickDownRotateSpeed * Time.deltaTime
                );

                if (Quaternion.Angle(hinge.localRotation, _kickDownTargetRotation) <= 0.1f)
                {
                    hinge.localRotation = _kickDownTargetRotation;
                    _isKickingDown = false;
                }
            }

            return;
        }

        float dt = Time.deltaTime;
        float targetBlend = _isOpen ? 1f : 0f;
        _blend = Mathf.SmoothDamp(_blend, targetBlend, ref _blendVelocity, rotateSmoothTime, Mathf.Infinity, dt);

        _swayVelocity += -_swayAngle * swayStiffness * dt;
        _swayVelocity *= Mathf.Exp(-swayDamping * dt);
        _swayAngle += _swayVelocity * dt;
        _swayAngle = Mathf.Clamp(_swayAngle, -maxSwayAngle, maxSwayAngle);

        ApplyDoorRotation();
    }

    public void Interact()
    {
        if (_isKickedDown)
        {
            return;
        }

        if (_isLocked)
        {
            _lockedShakeDirection = -_lockedShakeDirection;
            _swayVelocity += lockedShakeImpulse * _lockedShakeDirection;
            PlaySound(lockedSound);
            return;
        }

        _isOpen = !_isOpen;
        _swayVelocity += (_isOpen ? 1f : -1f) * swayImpulse;
        PlaySound(_isOpen ? openSound : closeSound);
    }

    public void SetLocked(bool locked)
    {
        if (_isKickedDown)
        {
            return;
        }

        _isLocked = locked;
    }

    public void Lock()
    {
        if (_isKickedDown)
        {
            return;
        }

        _isLocked = true;
    }

    public void Unlock()
    {
        if (_isKickedDown)
        {
            return;
        }

        _isLocked = false;
    }

    public void Rumble(float impulse)
    {
        if (_isKickedDown)
        {
            return;
        }

        float direction = Mathf.Sign(_lockedShakeDirection);
        if (Mathf.Approximately(direction, 0f))
        {
            direction = 1f;
        }

        _lockedShakeDirection = -direction;
        _swayVelocity += Mathf.Abs(impulse) * _lockedShakeDirection;
    }

    public bool KickDown(Vector3 force, Vector3 hitPoint)
    {
        if (_isKickedDown)
        {
            return false;
        }

        _isKickedDown = true;
        _isLocked = false;
        _isOpen = true;
        _blend = 1f;
        _blendVelocity = 0f;
        _isKickingDown = true;

        if (hinge != null)
        {
            Vector3 axis = hingeAxis.sqrMagnitude > 0.0001f ? hingeAxis.normalized : Vector3.up;
            float sign = 1f;

            if (kickDirectionFromForce && force.sqrMagnitude > 0.0001f)
            {
                Vector3 localForce = hinge.InverseTransformDirection(force.normalized);
                float projection = Vector3.Dot(localForce, axis);
                if (!Mathf.Approximately(projection, 0f))
                {
                    sign = Mathf.Sign(projection);
                }
            }

            _kickDownTargetRotation = hinge.localRotation * Quaternion.AngleAxis(kickDownAngle * sign, axis);
        }

        if (disableCollisionOnKickDown)
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = false;
                }
            }
        }

        PlaySound(kickedDownSound);
        return true;
    }

    private void ApplyDoorRotation()
    {
        if (hinge == null)
        {
            return;
        }

        Quaternion closedRotation = Quaternion.Euler(closedLocalEuler);
        Quaternion openRotation = Quaternion.Euler(openLocalEuler);
        Quaternion baseRotation = Quaternion.Slerp(closedRotation, openRotation, _blend);

        Vector3 axis = hingeAxis.sqrMagnitude > 0.0001f ? hingeAxis.normalized : Vector3.up;
        Quaternion swayRotation = Quaternion.AngleAxis(_swayAngle, axis);
        hinge.localRotation = baseRotation * swayRotation;
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource == null || clip == null)
        {
            return;
        }

        audioSource.PlayOneShot(clip, soundVolume);
    }

    [ContextMenu("Set Closed Rotation From Current")]
    private void SetClosedFromCurrent()
    {
        if (hinge == null)
        {
            hinge = transform;
        }

        closedLocalEuler = hinge.localEulerAngles;
    }

    [ContextMenu("Set Open Rotation From Current")]
    private void SetOpenFromCurrent()
    {
        if (hinge == null)
        {
            hinge = transform;
        }

        openLocalEuler = hinge.localEulerAngles;
    }
}
