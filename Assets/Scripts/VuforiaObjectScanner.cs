using System.Collections;
using UnityEngine;
using Vuforia;

/// <summary>
/// Attach this to every Vuforia ImageTarget in the scene.
///
/// Flow:
///   Vuforia detects the Image Target
///   → OnTargetStatusChanged fires with TRACKED
///   → We spawn an InfoPanel as a child of the target (so it sticks in 6DoF)
///   → First time only: call Gemini (text-only) with the target's name
///   → Show result; cache it so re-tracking the same target reuses the answer
///
/// Lifecycle is tied to tracking: when the target is lost, the panel is hidden.
/// </summary>
[RequireComponent(typeof(ObserverBehaviour))]
public class VuforiaObjectScanner : MonoBehaviour
{
    [Header("References")]
    public GeminiClient geminiClient;
    public GameObject   infoPanelPrefab;

    [Header("Prompt")]
    [Tooltip("What to ask Gemini about. If empty, uses the Vuforia TargetName. " +
             "Useful when your target slug is ugly (e.g. 'mona_lisa' → 'Mona Lisa by Leonardo da Vinci').")]
    public string subjectOverride = "";

    [Header("Panel placement (local to target)")]
    public Vector3 panelLocalOffset = new Vector3(0f, 0.2f, 0f);
    public Vector3 panelLocalScale  = Vector3.one * 0.3f;

    // ── Private state ─────────────────────────────────────────────────────────
    private ObserverBehaviour _observer;
    private InfoPanel         _activePanel;
    private GeminiResult      _cachedResult;
    private bool              _requestInFlight;

    // ── Unity ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        _observer = GetComponent<ObserverBehaviour>();

        if (geminiClient == null)
            geminiClient = FindFirstObjectByType<GeminiClient>();

        if (geminiClient == null)
            Debug.LogError("[VuforiaObjectScanner] No GeminiClient found in scene!");

        if (infoPanelPrefab == null)
            Debug.LogError("[VuforiaObjectScanner] infoPanelPrefab is not assigned!");
    }

    void OnEnable()  { _observer.OnTargetStatusChanged += OnStatusChanged; }
    void OnDisable() { _observer.OnTargetStatusChanged -= OnStatusChanged; }

    // ── Vuforia callback ──────────────────────────────────────────────────────
    void OnStatusChanged(ObserverBehaviour ob, TargetStatus status)
    {
        bool tracked = status.Status == Status.TRACKED ||
                       status.Status == Status.EXTENDED_TRACKED;

        Debug.Log($"[VuforiaObjectScanner] '{ob.TargetName}' → {status.Status}");

        if (tracked) OnFound();
        else         OnLost();
    }

    void OnFound()
    {
        EnsurePanel();

        if (_cachedResult != null)
        {
            // Seen before — just re-show cached result instantly
            _activePanel.ShowResult(_cachedResult);
            return;
        }

        if (!_requestInFlight)
        {
            _activePanel.ShowLoading();
            StartCoroutine(QueryGemini());
        }
    }

    void OnLost()
    {
        if (_activePanel != null)
        {
            Destroy(_activePanel.gameObject);
            _activePanel = null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    void EnsurePanel()
    {
        if (_activePanel != null) return;

        var go = Instantiate(infoPanelPrefab, transform);   // child of target
        go.transform.localPosition = panelLocalOffset;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = panelLocalScale;

        _activePanel = go.GetComponent<InfoPanel>();
        // Tie lifecycle to tracking, not a timer
        _activePanel.autoDestroyAfterSeconds = 99999f;

        Debug.Log($"[VuforiaObjectScanner] 🪧 Panel spawned under '{_observer.TargetName}'");
    }

    IEnumerator QueryGemini()
    {
        if (geminiClient == null) yield break;

        _requestInFlight = true;

        string subject = string.IsNullOrWhiteSpace(subjectOverride)
            ? _observer.TargetName
            : subjectOverride;

        Debug.Log($"[VuforiaObjectScanner] 🚀 Asking Gemini about '{subject}'");

        yield return geminiClient.AnalyzeText(
            subject,
            r =>
            {
                _cachedResult = r;
                Debug.Log($"[VuforiaObjectScanner] ✅ Cached: {r.name}");
                if (_activePanel != null) _activePanel.ShowResult(r);
            },
            e =>
            {
                Debug.LogError($"[VuforiaObjectScanner] ❌ {e}");
                if (_activePanel != null) _activePanel.ShowError(e);
                // Don't cache the error — allow retry next time target is re-found
            });

        _requestInFlight = false;
    }
}

