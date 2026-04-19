using UnityEngine;
using UnityEditor;

/// <summary>
/// AR TP2 — Vuforia one-click scene builder.
/// Menu: AR TP2 ▶ Setup Vuforia Scene   (or Ctrl+Alt+V)
///
/// NOTE: ImageTargets themselves must be added manually via
/// GameObject → Vuforia Engine → Image Target (Unity needs the database
/// picker). This builder handles everything else: ARCamera, GeminiClient,
/// lighting, InfoPanel prefab.
/// </summary>
public static class VuforiaSceneSetup
{
    [MenuItem("AR TP2/Setup Vuforia Scene %&v")]
    public static void Setup()
    {
        // ── 0. Clean up previous run ─────────────────────────────────────────
        DestroyIfExists("AR Session");
        DestroyIfExists("XR Origin");
        DestroyIfExists("ARCamera");
        DestroyIfExists("GeminiClient");
        DestroyIfExists("Directional Light (Vuforia)");

        // ── 1. Default lighting (Vuforia ARCamera prefab has no light) ───────
        var lightGo = new GameObject("Directional Light (Vuforia)");
        var light   = lightGo.AddComponent<Light>();
        light.type       = LightType.Directional;
        light.intensity  = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // ── 2. Instantiate Vuforia ARCamera prefab ───────────────────────────
        GameObject arCameraPrefab = FindVuforiaARCameraPrefab();
        GameObject arCamera;
        if (arCameraPrefab != null)
        {
            arCamera = (GameObject)PrefabUtility.InstantiatePrefab(arCameraPrefab);
            arCamera.name = "ARCamera";
            // Make sure there's a MainCamera tag so Camera.main works
            var camComp = arCamera.GetComponentInChildren<Camera>();
            if (camComp != null) camComp.tag = "MainCamera";
            Debug.Log("[AR TP2 Vuforia] ✅ Instantiated Vuforia ARCamera prefab.");
        }
        else
        {
            Debug.LogWarning("[AR TP2 Vuforia] ⚠️ Vuforia ARCamera prefab not found in project.\n" +
                             "→ Is the Vuforia Engine package installed?\n" +
                             "→ If yes, add it manually: GameObject → Vuforia Engine → AR Camera");
            arCamera = new GameObject("ARCamera");
            arCamera.AddComponent<Camera>().tag = "MainCamera";
        }

        // ── 3. GeminiClient ──────────────────────────────────────────────────
        var geminiGo     = new GameObject("GeminiClient");
        var geminiClient = geminiGo.AddComponent<GeminiClient>();

        // ── 4. Reuse the 3D InfoPanel prefab builder from ARSceneSetup ───────
        var infoPanelPrefab = ARSceneSetup.BuildInfoPanelPrefab();

        // ── 5. Mark dirty ────────────────────────────────────────────────────
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log(
            "[AR TP2 Vuforia] ✅ Scene base ready!\n" +
            "── Now do manually (one-time per target) ──\n" +
            "1. GameObject → Vuforia Engine → Image Target  " +
            "(select your database + pick a target)\n" +
            "2. Select that ImageTarget → Inspector → Add Component → " +
            "VuforiaObjectScanner\n" +
            "3. Drag the 'GeminiClient' GameObject into the scanner's " +
            "'Gemini Client' field\n" +
            "4. Drag Assets/Prefabs/InfoPanel.prefab into the scanner's " +
            "'Info Panel Prefab' field\n" +
            "5. (Optional) fill 'Subject Override' with a nicer name than " +
            "the Vuforia target slug\n" +
            "6. Paste your Gemini API key on the GeminiClient GameObject\n" +
            "7. Hit Play!");

        Selection.activeGameObject = geminiGo;
        EditorGUIUtility.PingObject(infoPanelPrefab);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the ARCamera prefab shipped with the Vuforia Engine package.
    /// In Vuforia 10.x it lives under:
    ///   Packages/com.ptc.vuforia.engine/Vuforia/Prefabs/ARCamera.prefab
    /// We search loosely to survive version bumps.
    /// </summary>
    static GameObject FindVuforiaARCameraPrefab()
    {
        // Try common package paths first (fast path)
        string[] knownPaths =
        {
            "Packages/com.ptc.vuforia.engine/Vuforia/Prefabs/ARCamera.prefab",
            "Assets/Vuforia/Prefabs/ARCamera.prefab",
            "Assets/Resources/VuforiaConfiguration.asset",   // probe
        };
        foreach (var p in knownPaths)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go != null) return go;
        }

        // Fallback: search the whole project
        string[] guids = AssetDatabase.FindAssets("ARCamera t:Prefab");
        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            if (path.ToLower().Contains("vuforia"))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) return go;
            }
        }
        return null;
    }

    static void DestroyIfExists(string goName)
    {
        var go = GameObject.Find(goName);
        if (go != null) Object.DestroyImmediate(go);
    }
}
