using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// ── Data models ────────────────────────────────────────────────────────────────

[Serializable]
public class GeminiResult
{
    public string name;
    public string description;
    public string fact;
}

// Internal Gemini REST response shape
[Serializable] public class GeminiResponse  { public GeminiCandidate[] candidates; }
[Serializable] public class GeminiCandidate { public GeminiContent content; }
[Serializable] public class GeminiContent   { public GeminiPart[] parts; }
[Serializable] public class GeminiPart      { public string text; }

// ── Client ─────────────────────────────────────────────────────────────────────

public class GeminiClient : MonoBehaviour
{
    [Header("Gemini API")]
    [Tooltip("Get a free key at https://aistudio.google.com/app/apikey")]
    public string apiKey = "PASTE_YOUR_GEMINI_API_KEY_HERE";

    [Tooltip("Model name as it appears in Google AI Studio (e.g. gemini-3-flash, gemini-2.5-flash, gemini-1.5-flash)")]
    public string model = "gemini-3-flash";

    private const string API_BASE = "https://generativelanguage.googleapis.com/v1beta/models/";

    // ── Text-only prompt (used by the Vuforia path — target is already known) ──
    /// <summary>
    /// Ask Gemini for information about a named subject, no image required.
    /// Used when Vuforia has already identified the target by name, so we just
    /// want rich content about it (description + fun fact).
    /// </summary>
    public IEnumerator AnalyzeText(string subject,
                                    Action<GeminiResult> onResult,
                                    Action<string>       onError)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.StartsWith("PASTE_"))
        {
            Debug.LogError("[GeminiClient] ❌ API key is not set!");
            onError?.Invoke("Missing API key");
            yield break;
        }

        string prompt =
            "Tell me about: " + subject + ". " +
            "Reply ONLY with a valid JSON object — no markdown, no code fences. " +
            "Use this exact schema: " +
            "{\"name\":\"<human-readable name>\",\"description\":\"<one sentence>\",\"fact\":\"<one interesting fact>\"}";

        string escapedPrompt = prompt.Replace("\"", "\\\"");
        string body = "{\"contents\":[{\"parts\":[{\"text\":\"" + escapedPrompt + "\"}]}]}";

        string url = API_BASE + model + ":generateContent?key=" + apiKey;

        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 20;

        Debug.Log($"[GeminiClient] 🌐 POST (text-only, subject=\"{subject}\") → {model}");
        yield return req.SendWebRequest();
        Debug.Log($"[GeminiClient] 📨 HTTP {(int)req.responseCode} — {req.downloadHandler.text.Length} bytes");

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[GeminiClient] ❌ {req.error}\n{req.downloadHandler.text}");
            onError?.Invoke("API error: " + req.error);
            yield break;
        }

        try
        {
            var resp = JsonUtility.FromJson<GeminiResponse>(req.downloadHandler.text);
            string raw = resp.candidates[0].content.parts[0].text.Trim();
            if (raw.StartsWith("```"))
            {
                int start = raw.IndexOf('\n') + 1;
                int end   = raw.LastIndexOf("```");
                raw = raw.Substring(start, end - start).Trim();
            }
            var result = JsonUtility.FromJson<GeminiResult>(raw);
            Debug.Log($"[GeminiClient] ✅ Parsed → name=\"{result.name}\"");
            onResult?.Invoke(result);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GeminiClient] ❌ Parse error: {ex.Message}\nRaw: {req.downloadHandler.text}");
            onError?.Invoke("Parse error: " + ex.Message);
        }
    }

    /// <summary>
    /// Send a screenshot to Gemini and get back what object is in the image.
    /// </summary>
    public IEnumerator Analyze(Texture2D image,
                                Action<GeminiResult> onResult,
                                Action<string>       onError)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.StartsWith("PASTE_"))
        {
            Debug.LogError("[GeminiClient] ❌ API key is not set! " +
                           "Paste your key in the Inspector → GeminiClient → Api Key.");
            onError?.Invoke("Missing API key");
            yield break;
        }

        // 1. Encode image → base64 JPEG
        byte[] jpg = image.EncodeToJPG(75);
        string b64 = Convert.ToBase64String(jpg);
        Debug.Log($"[GeminiClient] 📦 JPEG {jpg.Length / 1024f:F1} KB, base64 {b64.Length / 1024f:F1} KB");

        // 2. Build JSON body
        string prompt =
            "Look at this image and identify the most prominent object. " +
            "Reply ONLY with a valid JSON object — no markdown, no code fences. " +
            "Use this exact schema: " +
            "{\"name\":\"<object name>\",\"description\":\"<one sentence>\",\"fact\":\"<one interesting fact>\"}";

        // Escape the prompt for embedding in JSON
        string escapedPrompt = prompt.Replace("\"", "\\\"");

        string body = "{\"contents\":[{\"parts\":[" +
                      "{\"inlineData\":{\"mimeType\":\"image/jpeg\",\"data\":\"" + b64 + "\"}}," +
                      "{\"text\":\"" + escapedPrompt + "\"}" +
                      "]}]}";

        // 3. POST to Gemini
        string url = API_BASE + model + ":generateContent?key=" + apiKey;

        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 20;

        Debug.Log($"[GeminiClient] 🌐 POST → {model} (body {body.Length / 1024f:F1} KB)");

        yield return req.SendWebRequest();

        Debug.Log($"[GeminiClient] 📨 HTTP {(int)req.responseCode} — " +
                  $"{req.downloadHandler.text.Length} bytes received");

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[GeminiClient] ❌ Network/API error: {req.error}\n" +
                           $"Response body: {req.downloadHandler.text}");
            onError?.Invoke("API error: " + req.error + " | " + req.downloadHandler.text);
            yield break;
        }

        // 4. Parse response
        try
        {
            var resp = JsonUtility.FromJson<GeminiResponse>(req.downloadHandler.text);
            string raw = resp.candidates[0].content.parts[0].text.Trim();

            Debug.Log($"[GeminiClient] 📝 Raw text from Gemini:\n{raw}");

            // Strip markdown code fences if Gemini adds them anyway
            if (raw.StartsWith("```"))
            {
                int start = raw.IndexOf('\n') + 1;
                int end   = raw.LastIndexOf("```");
                raw = raw.Substring(start, end - start).Trim();
                Debug.Log($"[GeminiClient] ✂️ Stripped code fences → {raw}");
            }

            var result = JsonUtility.FromJson<GeminiResult>(raw);
            Debug.Log($"[GeminiClient] ✅ Parsed JSON → " +
                      $"name=\"{result.name}\" | desc=\"{result.description}\" | fact=\"{result.fact}\"");
            onResult?.Invoke(result);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GeminiClient] ❌ Parse error: {ex.Message}\n" +
                           $"Raw response: {req.downloadHandler.text}");
            onError?.Invoke("Parse error: " + ex.Message + "\nRaw: " + req.downloadHandler.text);
        }
    }
}
