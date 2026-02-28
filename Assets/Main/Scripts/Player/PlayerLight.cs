using UnityEngine;

public class PlayerLight : MonoBehaviour
{
    [SerializeField] private Light targetLight;
    [SerializeField] private bool startEnabled = true;
    [SerializeField] private AudioSource toggleSound;

    private InputSystem_Actions _actions;
    private bool _isEnabled;

    private void Awake()
    {
        _actions = new InputSystem_Actions();

        if (targetLight == null)
        {
            targetLight = GetComponentInChildren<Light>();
        }

        if (targetLight == null)
        {
            enabled = false;
            Debug.LogWarning($"{nameof(PlayerLight)} on {name} has no Light assigned.");
            return;
        }

        _isEnabled = startEnabled;
        targetLight.enabled = _isEnabled;
    }

    private void OnEnable()
    {
        _actions?.Player.Enable();
    }

    private void OnDisable()
    {
        _actions?.Player.Disable();
    }

    private void OnDestroy()
    {
        _actions?.Dispose();
    }

    private void Update()
    {
        if (_actions != null && _actions.Player.FlashLight.WasPressedThisFrame())
        {
            if (toggleSound != null)
            {
                toggleSound.Play();
            }
            _isEnabled = !_isEnabled;
            targetLight.enabled = _isEnabled;
        }
    }
}
