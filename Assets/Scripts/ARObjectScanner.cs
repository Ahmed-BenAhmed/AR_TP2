using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Main AR controller for the TP.
///
/// Flow:
///   User taps screen
///   → Raycast against AR planes to get world position
///   → Capture screenshot
///   → Send to GeminiClient
///   → Show InfoPanel at world position with result
/// </summary>
[RequireComponent(typeof(ARRaycastManager))]
public class ARObjectScanner : MonoBehaviour
{
    [Header("References")]
    public GeminiClient geminiClient;
    public GameObject   infoPanelPrefab;   // assign the InfoPanel prefab here

    [Header("Settings")]
    [Tooltip("How far in front of the camera to place the panel when no plane is hit")]
    public float defaultPanelDistance = 1.5f;
    [Tooltip("How high above the plane hit to float the panel")]
    public float panelHeightOffset    = 0.4f;
    [Tooltip("Minimum seconds between two consecutive scans")]
    public float scanCooldown         = 3f;

    // ── Private state ──────────────────────────────────────────────────────────

    private ARRaycastManager         _raycastManager;
    private readonly List<ARRaycastHit> _hits = new();
    private bool  _scanning    = false;
    private float _lastScanTime = -999f;
    private InfoPanel _activePanel;

    // Camera feed used for both editor (laptop webcam) AND device (phone camera).
    // Makes the app work on any Android phone — ARCore optional.
    private WebCamTexture _webcam;
    private RawImage      _webcamDisplay;

    // ── Unity lifecycle ────────────────────────────────────────────────────────

    void Awake()
    {
        _raycastManager = GetComponent<ARRaycastManager>();

        if (geminiClient == null)
            geminiClient = FindFirstObjectByType<GeminiClient>();

        if (infoPanelPrefab == null)
            Debug.LogError("[ARObjectScanner] infoPanelPrefab is not assigned!");

        StartCoroutine(StartCameraFeed());
    }

    // ── Camera feed (editor + Android) ────────────────────────────────────────
    // Uses WebCamTexture on both platforms. On Android this is the phone's
    // back camera; in the editor it's the laptop webcam. Makes the app work
    // without ARCore — perfect for non-AR-compatible phones.
    IEnumerator StartCameraFeed()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Request camera permission at runtime (Android 6+)
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.Camera))
        {
            UnityEngine.Android.Permission.RequestUserPermission(
                UnityEngine.Android.Permission.Camera);
            // Wait up to 10s for the user to tap "Allow"
            float t = 0f;
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                       UnityEngine.Android.Permission.Camera) && t < 10f)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }
#endif

        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogWarning("[ARObjectScanner] No camera detected.");
            yield break;
        }

        // Prefer back-facing camera on phone; first device on laptop.
        string device = WebCamTexture.devices[0].name;
        foreach (var d in WebCamTexture.devices)
        {
            if (!d.isFrontFacing) { device = d.name; break; }
        }

        _webcam = new WebCamTexture(device, 1280, 720, 30);
        _webcam.Play();

        // Fullscreen camera feed on a ScreenSpaceCamera canvas placed near the
        // far clip plane. This is the KEY trick: overlay canvases render last
        // and hide 3D objects, but a camera-space canvas at a large plane
        // distance acts like a background layer — any 3D world-space object
        // closer to the camera (our InfoPanel) renders on top of it naturally.
        var arCam = Camera.main;
        if (arCam == null)
        {
            Debug.LogError("[ARObjectScanner] Camera.main is null — cannot build webcam canvas.");
            yield break;
        }

        var canvasGo = new GameObject("CameraFeedCanvas");
        var canvas   = canvasGo.AddComponent<Canvas>();
        canvas.renderMode    = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera   = arCam;
        canvas.planeDistance = arCam.farClipPlane * 0.9f;  // ~90m if farClip=100
        canvas.sortingOrder  = 0;
        canvasGo.AddComponent<CanvasScaler>();

        var imgGo = new GameObject("CameraFeedImage");
        imgGo.transform.SetParent(canvasGo.transform, false);
        var rt = imgGo.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _webcamDisplay                = imgGo.AddComponent<RawImage>();
        _webcamDisplay.texture        = _webcam;
        _webcamDisplay.raycastTarget  = false;     // let clicks pass through

#if UNITY_ANDROID && !UNITY_EDITOR
        // Phone cameras often need a rotation to display upright.
        imgGo.transform.localEulerAngles = new Vector3(0, 0, -_webcam.videoRotationAngle);
        if (_webcam.videoVerticallyMirrored)
            rt.localScale = new Vector3(-1, 1, 1);
#endif

        // ── Instruction overlay (persistent, on its OWN canvas above webcam)
        var instrCanvasGo = new GameObject("InstructionCanvas");
        var instrCanvas   = instrCanvasGo.AddComponent<Canvas>();
        instrCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        instrCanvas.sortingOrder = 10;
        instrCanvasGo.AddComponent<CanvasScaler>();
        BuildInstructionOverlay(instrCanvasGo.transform);

        Debug.Log($"[ARObjectScanner] Camera started: {device} " +
                  $"({_webcam.requestedWidth}x{_webcam.requestedHeight}) " +
                  $"rotation={_webcam.videoRotationAngle} mirrored={_webcam.videoVerticallyMirrored}");
    }

    void OnDestroy()
    {
        if (_webcam != null && _webcam.isPlaying)
            _webcam.Stop();
    }

    void Update()
    {
        if (_scanning) return;

        // ── Touch (real device) ───────────────────────────
        if (Input.touchCount > 0)
        {
            var touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
                TryStartScan(touch.position);
        }

        // ── Mouse click (editor / desktop fallback) ───────
#if UNITY_EDITOR
        if (Input.GetMouseButtonDown(0))
            TryStartScan(Input.mousePosition);
#endif
    }

    // ── Core logic ─────────────────────────────────────────────────────────────

    void TryStartScan(Vector2 screenPos)
    {
        if (Time.time - _lastScanTime < scanCooldown)
        {
            Debug.Log($"[ARObjectScanner] ⏳ Scan ignored (cooldown {scanCooldown}s).");
            return;
        }
        _lastScanTime = Time.time;

        Debug.Log($"[ARObjectScanner] 👆 Tap at screen ({screenPos.x:F0}, {screenPos.y:F0})");

        // Try to hit a detected AR plane (only works on ARCore-supported devices)
        Vector3 worldPos;
        if (_raycastManager != null && _raycastManager.subsystem != null &&
            _raycastManager.Raycast(screenPos, _hits, TrackableType.PlaneWithinPolygon))
        {
            worldPos = _hits[0].pose.position + Vector3.up * panelHeightOffset;
            Debug.Log($"[ARObjectScanner] ✅ Plane hit at {worldPos}");
        }
        else
        {
            // No AR → always spawn directly in front of whatever Camera.main sees.
            var cam = Camera.main.transform;
            worldPos = cam.position + cam.forward * defaultPanelDistance;
            Debug.Log($"[ARObjectScanner] ℹ️ No AR plane — panel in front of camera at {worldPos} " +
                      $"(cam pos={cam.position}, fwd={cam.forward})");
        }

        StartCoroutine(ScanCoroutine(worldPos));
    }

    IEnumerator ScanCoroutine(Vector3 worldPosition)
    {
        _scanning = true;
        float t0 = Time.realtimeSinceStartup;

        // 1. Spawn panel showing "Analyzing…"
        SpawnPanel(worldPosition);
        _activePanel.ShowLoading();
        Debug.Log("[ARObjectScanner] 🪧 InfoPanel spawned (loading state).");

        // 2. Wait for end of frame so the AR camera has rendered
        yield return new WaitForEndOfFrame();

        // 3. Capture a frame to send to Gemini
        Texture2D screenshot = CaptureFrame();
        Debug.Log($"[ARObjectScanner] 📸 Frame captured: " +
                  $"{screenshot.width}x{screenshot.height} " +
                  $"(source: {(Application.isEditor ? "webcam" : "screen")})");

        // 4. Call Gemini API
        GeminiResult result = null;
        string       error  = null;
        float        tApi   = Time.realtimeSinceStartup;

        Debug.Log("[ARObjectScanner] 🚀 Sending frame to Gemini…");
        yield return geminiClient.Analyze(
            screenshot,
            r => result = r,
            e => error  = e);

        float apiMs = (Time.realtimeSinceStartup - tApi) * 1000f;
        Destroy(screenshot);

        // 5. Update panel with result or error
        if (error != null)
        {
            Debug.LogError($"[ARObjectScanner] ❌ Gemini error after {apiMs:F0}ms: {error}");
            if (_activePanel != null) _activePanel.ShowError(error);
        }
        else if (result != null)
        {
            Debug.Log($"[ARObjectScanner] ✅ Gemini reply in {apiMs:F0}ms → " +
                      $"name=\"{result.name}\" | desc=\"{result.description}\" | fact=\"{result.fact}\"");
            if (_activePanel != null) _activePanel.ShowResult(result);
        }

        float totalMs = (Time.realtimeSinceStartup - t0) * 1000f;
        Debug.Log($"[ARObjectScanner] 🏁 Scan done — total {totalMs:F0}ms");

        _scanning = false;
    }

    // Builds a semi-transparent, bordered card at the top of the screen with
    // tap-to-scan instructions. Always visible, added ON TOP of the webcam
    // image (children later in the hierarchy render after).
    void BuildInstructionOverlay(Transform parentCanvas)
    {
        // Frame (bordered box)
        var frameGo = new GameObject("InstructionFrame");
        frameGo.transform.SetParent(parentCanvas, false);

        var frameRt = frameGo.AddComponent<RectTransform>();
        frameRt.anchorMin = new Vector2(0.5f, 1f);          // top-center
        frameRt.anchorMax = new Vector2(0.5f, 1f);
        frameRt.pivot     = new Vector2(0.5f, 1f);
        frameRt.anchoredPosition = new Vector2(0f, -40f);   // 40px from top
        frameRt.sizeDelta        = new Vector2(720f, 140f);

        var frameImg = frameGo.AddComponent<Image>();
        frameImg.color         = new Color(0f, 0f, 0f, 0.45f);  // semi-transparent black
        frameImg.raycastTarget = false;

        // White border using Outline component
        var outline = frameGo.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor    = Color.white;
        outline.effectDistance = new Vector2(3f, -3f);

        // Instruction text
        var textGo = new GameObject("InstructionText");
        textGo.transform.SetParent(frameGo.transform, false);

        var textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(20f, 10f);
        textRt.offsetMax = new Vector2(-20f, -10f);

        var tmp = textGo.AddComponent<TMPro.TextMeshProUGUI>();
        tmp.text      = "Point your camera at an object\nand tap the screen to identify it";
        tmp.fontSize  = 36f;
        tmp.color     = Color.white;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.fontStyle = TMPro.FontStyles.Bold;
        tmp.raycastTarget = false;
    }

    // Returns a Texture2D of whatever "the user is pointing at right now".
    // Caller is responsible for Destroy()-ing the returned texture.
    Texture2D CaptureFrame()
    {
        if (_webcam != null && _webcam.isPlaying && _webcam.width > 16)
        {
            var tex = new Texture2D(_webcam.width, _webcam.height,
                                    TextureFormat.RGB24, false);
            tex.SetPixels(_webcam.GetPixels());
            tex.Apply();
            return tex;
        }
        // Fallback if webcam unavailable: capture whatever is on screen
        return ScreenCapture.CaptureScreenshotAsTexture();
    }

    void SpawnPanel(Vector3 worldPosition)
    {
        // Remove previous panel if still visible
        if (_activePanel != null)
            Destroy(_activePanel.gameObject);

        var go = Instantiate(infoPanelPrefab, worldPosition, Quaternion.identity);
        _activePanel = go.GetComponent<InfoPanel>();
    }
}
