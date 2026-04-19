using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.InputSystem.XR;
using Unity.XR.CoreUtils;
using UnityEditor;
using TMPro;

/// <summary>
/// AR TP2 — one-click scene builder.
/// Menu: AR TP2 ▶ Setup Scene   (or Ctrl+Alt+S)
/// </summary>
public static class ARSceneSetup
{
    // ─────────────────────────────────────────────────────────────────────────
    [MenuItem("AR TP2/Setup Scene %&s")]
    public static void SetupScene()
    {
        // ── 0. Clean up previous run ─────────────────────────────────────────
        DestroyIfExists("AR Session");
        DestroyIfExists("XR Origin");
        DestroyIfExists("GeminiClient");

        // ── 1. AR Session ────────────────────────────────────────────────────
        var arSessionGo = new GameObject("AR Session");
        arSessionGo.AddComponent<ARSession>();

        // ── 2. XR Origin ─────────────────────────────────────────────────────
        var xrOriginGo = new GameObject("XR Origin");
        var xrOrigin   = xrOriginGo.AddComponent<XROrigin>();

        // AR managers that must live on the same GameObject as XROrigin
        xrOriginGo.AddComponent<ARPlaneManager>();     // plane detection
        xrOriginGo.AddComponent<ARRaycastManager>();   // required by ARObjectScanner

        // ── 3. Camera hierarchy: XR Origin → Camera Offset → AR Camera ───────
        var camOffsetGo = new GameObject("Camera Offset");
        camOffsetGo.transform.SetParent(xrOriginGo.transform, false);

        var arCameraGo = new GameObject("AR Camera");
        arCameraGo.transform.SetParent(camOffsetGo.transform, false);
        arCameraGo.tag = "MainCamera";  // keeps Camera.main working

        var cam = arCameraGo.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.Color;
        cam.backgroundColor = Color.black;
        cam.nearClipPlane   = 0.1f;
        cam.farClipPlane    = 100f;   // webcam canvas sits near the back plane

        arCameraGo.AddComponent<ARCameraManager>();
        arCameraGo.AddComponent<ARCameraBackground>();

        // ── TrackedPoseDriver (Input System) — required by XROrigin ──────────
        // Silences the "transform will not be updated" warning and enables
        // XR Simulation to move the camera in the Editor.
        var tpd = arCameraGo.AddComponent<TrackedPoseDriver>();
        tpd.trackingType       = TrackedPoseDriver.TrackingType.RotationAndPosition;
        tpd.updateType         = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

        // Wire camera into XROrigin
        xrOrigin.Camera                  = cam;
        xrOrigin.CameraFloorOffsetObject = camOffsetGo;

        // ── 4. GeminiClient ──────────────────────────────────────────────────
        var geminiGo     = new GameObject("GeminiClient");
        var geminiClient = geminiGo.AddComponent<GeminiClient>();

        // ── 5. ARObjectScanner (lives on XR Origin alongside ARRaycastManager) ──
        var scanner = xrOriginGo.AddComponent<ARObjectScanner>();
        scanner.geminiClient = geminiClient;

        // ── 6. InfoPanel prefab ──────────────────────────────────────────────
        var infoPanelPrefab = BuildInfoPanelPrefab();
        scanner.infoPanelPrefab = infoPanelPrefab;

        // ── 7. Mark scene dirty ──────────────────────────────────────────────
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log(
            "[AR TP2] ✅ Scene setup complete!\n" +
            "→ Paste your Gemini API key in GeminiClient → Api Key\n" +
            "→ Get a free key at https://aistudio.google.com/app/apikey\n" +
            "→ To test in Editor: Project Settings > XR Plug-in Management > PC tab > enable 'XR Simulation'");

        // Ping the scanner in the hierarchy so the user sees it
        EditorGUIUtility.PingObject(xrOriginGo);
        Selection.activeGameObject = xrOriginGo;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // InfoPanel prefab builder
    // ─────────────────────────────────────────────────────────────────────────

    public static GameObject BuildInfoPanelPrefab()
    {
        const string prefabPath = "Assets/Prefabs/InfoPanel.prefab";

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        // ── Root: world-space panel spawned at the tap location ──────────────
        var root = new GameObject("InfoPanel");
        root.transform.localScale = Vector3.one * 0.35f;   // readable at ~1.5m

        // ── Background card (dark quad with white border via scaled child) ──
        var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "Background";
        Object.DestroyImmediate(bg.GetComponent<Collider>());
        bg.transform.SetParent(root.transform, false);
        bg.transform.localPosition = new Vector3(0f, 0f, 0.01f);  // slightly behind text
        bg.transform.localScale    = new Vector3(1.6f, 1.0f, 1f);
        var bgMat = new Material(Shader.Find("Unlit/Color"));
        bgMat.color = new Color(0.08f, 0.08f, 0.14f, 1f);
        bg.GetComponent<MeshRenderer>().sharedMaterial = bgMat;

        var border = GameObject.CreatePrimitive(PrimitiveType.Quad);
        border.name = "Border";
        Object.DestroyImmediate(border.GetComponent<Collider>());
        border.transform.SetParent(root.transform, false);
        border.transform.localPosition = new Vector3(0f, 0f, 0.02f);
        border.transform.localScale    = new Vector3(1.65f, 1.05f, 1f);
        var borderMat = new Material(Shader.Find("Unlit/Color"));
        borderMat.color = Color.white;
        border.GetComponent<MeshRenderer>().sharedMaterial = borderMat;

        // ── Loading root ──────────────────────────────────────────────────────
        var loadingRoot = new GameObject("LoadingRoot");
        loadingRoot.transform.SetParent(root.transform, false);

        var loadingTMP = MakeTMP3D(
            "LoadingText", loadingRoot.transform, Vector3.zero,
            text: "Analyzing…", size: 0.9f,
            color: new Color(0.85f, 0.9f, 1f), style: FontStyles.Italic,
            width: 1.4f, height: 0.8f);

        // ── Content root (populated when Gemini reply arrives) ────────────────
        var contentRoot = new GameObject("ContentRoot");
        contentRoot.transform.SetParent(root.transform, false);

        var nameTMP = MakeTMP3D(
            "NameText", contentRoot.transform, new Vector3(0f,  0.32f, 0f),
            text: "Object Name", size: 1.1f,
            color: Color.white, style: FontStyles.Bold,
            width: 1.5f, height: 0.3f);

        var descTMP = MakeTMP3D(
            "DescriptionText", contentRoot.transform, new Vector3(0f, 0f, 0f),
            text: "One-sentence description.", size: 0.55f,
            color: new Color(0.9f, 0.9f, 0.9f), style: FontStyles.Normal,
            width: 1.5f, height: 0.5f);

        var factTMP = MakeTMP3D(
            "FactText", contentRoot.transform, new Vector3(0f, -0.33f, 0f),
            text: "💡 Interesting fact", size: 0.45f,
            color: new Color(1f, 0.9f, 0.35f), style: FontStyles.Italic,
            width: 1.5f, height: 0.3f);

        contentRoot.SetActive(false);

        // ── Wire InfoPanel script ─────────────────────────────────────────────
        var panel = root.AddComponent<InfoPanel>();
        panel.nameText        = nameTMP;
        panel.descriptionText = descTMP;
        panel.factText        = factTMP;
        panel.loadingText     = loadingTMP;
        panel.contentRoot     = contentRoot;
        panel.loadingRoot     = loadingRoot;

        // ── Save prefab ───────────────────────────────────────────────────────
        var asset = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[AR TP2] InfoPanel prefab → " + prefabPath);
        return asset;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    static TextMeshPro MakeTMP3D(
        string name, Transform parent, Vector3 localPos,
        string text, float size, Color color, FontStyles style,
        float width, float height)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.color     = color;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;

        // 3D TMP uses a RectTransform for its text box size
        var rt = tmp.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);
        return tmp;
    }

    static void DestroyIfExists(string goName)
    {
        var go = GameObject.Find(goName);
        if (go != null)
            Object.DestroyImmediate(go);
    }
}
