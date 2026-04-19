# AR_TP2 — AI-Powered AR Object Recognition

A Unity AR application that uses the device camera to identify real-world objects and display rich information about them, powered by **Google Gemini** for AI recognition and either **AR Foundation** or **Vuforia** for augmented reality tracking.

![Demo](Screenshot%202026-04-19%20014036.png)

## What it does

Point your phone at an object → tap the screen (AR Foundation) or hold it in front of a printed image target (Vuforia) → a floating panel appears showing:

- **Object name** — what the AI thinks it is
- **One-sentence description** — what it is, in plain language
- **💡 Interesting fact** — a fun/useful tidbit generated on the fly

## Two AR backends, same AI pipeline

The project implements **both** paths from the TP guidelines so you can compare them:

| | AR Foundation | Vuforia |
|---|---|---|
| **Detection** | Tap the screen (no object recognition) | Vuforia matches a pre-registered Image Target |
| **AI's role** | Identifies the object from the screenshot | Generates rich content about the already-known target |
| **Works on** | ARCore-compatible phones (and works without ARCore via WebCamTexture fallback) | Any Android phone (Vuforia handles its own camera) |
| **Tracking** | 6DoF if ARCore available, else fixed-in-camera-space | True 6DoF — panel sticks to the printed image |
| **Panel behavior** | Spawned in front of camera at tap direction | Anchored as child of the Image Target |

## Project structure

```
Assets/
├── Scripts/
│   ├── GeminiClient.cs           # Gemini API wrapper (image + text modes)
│   ├── InfoPanel.cs              # 3D world-space panel (billboard)
│   ├── ARObjectScanner.cs        # AR Foundation path: tap + screenshot → Gemini
│   ├── VuforiaObjectScanner.cs   # Vuforia path: target detected → Gemini (text-only)
│   └── Editor/
│       ├── ARSceneSetup.cs       # One-click scene builder (AR Foundation)
│       └── VuforiaSceneSetup.cs  # One-click scene builder (Vuforia)
├── Prefabs/
│   └── InfoPanel.prefab          # 3D floating card (auto-generated)
└── Plugins/Android/
    └── gradleTemplate.properties # Tuned JVM heap for builds
```

## Editor shortcuts

- **AR TP2 → Setup Scene** (`Ctrl+Alt+S`) — builds the AR Foundation scene
- **AR TP2 → Setup Vuforia Scene** (`Ctrl+Alt+V`) — builds the Vuforia base scene

## Setup

### 1. Unity version
Unity **6000.3.11f1** or compatible.

### 2. Gemini API key
1. Get a free key at https://aistudio.google.com/app/apikey
2. In Unity: select the `GeminiClient` GameObject → paste key into **Api Key** field in the Inspector
3. Model defaults to `gemini-2.5-flash`

### 3. AR Foundation path
- **Window → Package Manager** → install **AR Foundation** + **Google ARCore XR Plugin** (6.1.0+)
- **Project Settings → XR Plug-in Management → Android** → enable **ARCore**
- **Project Settings → Player → Android → Graphics APIs** → remove Vulkan, keep only OpenGLES3
- Run menu `AR TP2 → Setup Scene`, then **File → Build And Run**

### 4. Vuforia path
1. Register at https://developer.vuforia.com → get a Basic (free) license key
2. Create an Image Target database, upload a high-contrast image (book cover, painting, product label)
3. Download the target `.unitypackage` + install Vuforia Engine (`com.ptc.vuforia.engine`) in Package Manager
4. **Project Settings → Vuforia Engine** → paste license
5. Import target .unitypackage
6. Run menu `AR TP2 → Setup Vuforia Scene`, then **GameObject → Vuforia Engine → Image Target** to add each target, and attach `VuforiaObjectScanner` to each
7. Print your target image, build, and point the phone at the print

## Build settings (Android)

- **Scripting Backend**: IL2CPP
- **Target Architectures**: ARM64
- **Minimum API Level**: Android 7.0 (API 24)
- **Graphics APIs**: OpenGLES3 only
- **Custom Gradle Properties Template**: checked (uses `Assets/Plugins/Android/gradleTemplate.properties` for 6GB JVM heap)

## Troubleshooting

| Symptom | Fix |
|---|---|
| Black screen on launch | Check Camera.main exists; webcam canvas needs `ScreenSpaceCamera` mode with `planeDistance = farClipPlane * 0.9` |
| Tap does nothing | Project Settings → Player → Active Input Handling = **Both** |
| "Vulkan not supported" build error | Remove Vulkan from Graphics APIs |
| "Vuforia requires ARCore XR Plugin 6.1.0+" | Update the ARCore XR Plugin via Package Manager → Version History |
| Gradle daemon OOM during build | Enable Custom Gradle Properties Template (already set in this repo) |
| 💡 emoji shows as a blank rectangle | Default TMP font lacks emoji glyphs — harmless, or import an emoji-capable font asset |

## Course context

Built for the TP2 assignment in the "Ingénierie et Maquette Numérique" program. The TP guidelines allowed choosing **either** AR Foundation or Vuforia — this project implements both for comparison purposes.
