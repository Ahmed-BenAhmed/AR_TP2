using UnityEngine;
using TMPro;

/// <summary>
/// World-space 3D info panel that floats at the location the user tapped.
/// Billboards to face the camera every frame, so the user can move the phone
/// and the panel stays anchored to the real-world spot while remaining readable.
/// </summary>
public class InfoPanel : MonoBehaviour
{
    [Header("UI References (3D TextMeshPro)")]
    public TextMeshPro nameText;
    public TextMeshPro descriptionText;
    public TextMeshPro factText;
    public TextMeshPro loadingText;
    public GameObject  contentRoot;   // parent of name/desc/fact labels
    public GameObject  loadingRoot;   // "Analyzing…" text

    [Header("Settings")]
    public float autoDestroyAfterSeconds = 12f;

    private float _showTime = -1f;

    void LateUpdate()
    {
        // Billboard: always face the camera so the text is readable from any angle
        var cam = Camera.main;
        if (cam != null)
        {
            transform.LookAt(cam.transform);
            transform.Rotate(0f, 180f, 0f);   // flip so front-face points at camera
        }

        // Auto-destroy after result has been shown for a while
        if (_showTime > 0f && Time.time - _showTime > autoDestroyAfterSeconds)
            Destroy(gameObject);
    }

    public void ShowLoading()
    {
        if (loadingRoot != null) loadingRoot.SetActive(true);
        if (contentRoot != null) contentRoot.SetActive(false);
        if (loadingText != null) loadingText.text = "Analyzing…";
    }

    public void ShowResult(GeminiResult result)
    {
        if (loadingRoot != null) loadingRoot.SetActive(false);
        if (contentRoot != null) contentRoot.SetActive(true);

        _showTime = Time.time;

        if (nameText        != null) nameText.text        = result.name        ?? "Unknown";
        if (descriptionText != null) descriptionText.text = result.description ?? "";
        if (factText        != null) factText.text        = "💡 " + (result.fact ?? "");
    }

    public void ShowError(string message)
    {
        if (loadingRoot != null) loadingRoot.SetActive(false);
        if (contentRoot != null) contentRoot.SetActive(true);

        _showTime = Time.time;

        if (nameText        != null) { nameText.text = "Error"; nameText.color = Color.red; }
        if (descriptionText != null) descriptionText.text = message;
        if (factText        != null) factText.text = "Tap to try again";
    }

    public void Dismiss() => Destroy(gameObject);
}
