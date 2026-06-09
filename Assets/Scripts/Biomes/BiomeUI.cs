using UnityEngine;
using UnityEngine.UI;

public class BiomeUI : MonoBehaviour
{
    public BiomeDetector biomeDetector;

    [Header("UI References")]
    public Text biomeLabel;
    public CanvasGroup canvasGroup;

    [Header("Settings")]
    public float fadeDuration = 0.5f;
    public float displayDuration = 2.5f;

    private float _fadeTimer;
    private float _displayTimer;
    private bool _fading;
    private bool _visible;

    void Start()
    {
        if (biomeDetector != null)
            biomeDetector.OnBiomeChanged += ShowBiome;

        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }

    void OnDestroy()
    {
        if (biomeDetector != null)
            biomeDetector.OnBiomeChanged -= ShowBiome;
    }

    private void ShowBiome(BiomeData biome)
    {
        if (biomeLabel != null && biome != null)
            biomeLabel.text = "Entering " + biome.biomeName;

        _displayTimer = displayDuration;
        _fading = false;
        _visible = true;
        _fadeTimer = 0f;
    }

    void Update()
    {
        if (canvasGroup == null) return;

        if (_visible && !_fading)
        {
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, 1f, Time.deltaTime / fadeDuration);

            _displayTimer -= Time.deltaTime;
            if (_displayTimer <= 0f)
                _fading = true;
        }
        else if (_fading)
        {
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, 0f, Time.deltaTime / fadeDuration);
            if (canvasGroup.alpha <= 0f)
            {
                _fading = false;
                _visible = false;
            }
        }
    }
}
