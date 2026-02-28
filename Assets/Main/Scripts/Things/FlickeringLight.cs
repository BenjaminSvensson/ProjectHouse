using UnityEngine;

public class FlickeringLight : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Light targetLight;

    [Header("Intensity")]
    [SerializeField] private float minIntensity = 0.6f;
    [SerializeField] private float maxIntensity = 1.2f;

    [Header("Flicker")]
    [SerializeField] private float flickerIntervalMin = 0.03f;
    [SerializeField] private float flickerIntervalMax = 0.12f;
    [SerializeField] private bool smoothFlicker = false;
    [SerializeField] private float smoothSpeed = 20f;

    private float _nextFlickerTime;
    private float _targetIntensity;

    private void Awake()
    {
        if (targetLight == null)
        {
            targetLight = GetComponent<Light>();
        }

        if (targetLight == null)
        {
            enabled = false;
            Debug.LogWarning($"{nameof(FlickeringLight)} on {name} has no Light assigned.");
            return;
        }

        minIntensity = Mathf.Max(0f, minIntensity);
        maxIntensity = Mathf.Max(minIntensity, maxIntensity);
        flickerIntervalMin = Mathf.Max(0.001f, flickerIntervalMin);
        flickerIntervalMax = Mathf.Max(flickerIntervalMin, flickerIntervalMax);

        _targetIntensity = Random.Range(minIntensity, maxIntensity);
        targetLight.intensity = _targetIntensity;

        // Random phase so multiple lights don't flicker in sync.
        _nextFlickerTime = Time.time + Random.Range(0f, flickerIntervalMax);
    }

    private void Update()
    {
        if (Time.time >= _nextFlickerTime)
        {
            _targetIntensity = Random.Range(minIntensity, maxIntensity);
            _nextFlickerTime = Time.time + Random.Range(flickerIntervalMin, flickerIntervalMax);
        }

        if (smoothFlicker)
        {
            targetLight.intensity = Mathf.MoveTowards(
                targetLight.intensity,
                _targetIntensity,
                smoothSpeed * Time.deltaTime
            );
        }
        else
        {
            targetLight.intensity = _targetIntensity;
        }
    }
}
