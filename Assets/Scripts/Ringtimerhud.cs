using UnityEngine;
using TMPro;

/// <summary>
/// Drives a HUD timer display: hidden until the first ring is passed, then
/// counts up until RingCourseManager stops it on course completion.
/// </summary>
public class RingTimerHUD : MonoBehaviour
{
    [Tooltip("The TextMeshPro element that displays the timer.")]
    public TextMeshProUGUI timerText;

    [Tooltip("The object to show/hide. Defaults to this GameObject if left empty.")]
    public GameObject displayRoot;

    public float ElapsedTime { get; private set; }

    private bool running = false;

    void Awake()
    {
        if (displayRoot == null) displayRoot = gameObject;
    }

    void Update()
    {
        if (!running) return;

        ElapsedTime += Time.deltaTime;
        UpdateDisplay();
    }

    public void SetVisible(bool visible)
    {
        if (displayRoot != null) displayRoot.SetActive(visible);
    }

    public void StartTimer()
    {
        ElapsedTime = 0f;
        running = true;
        UpdateDisplay();
    }

    public void StopTimer()
    {
        running = false;
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        if (timerText == null) return;

        int minutes = Mathf.FloorToInt(ElapsedTime / 60f);
        float seconds = ElapsedTime % 60f;
        timerText.text = $"{minutes:00}:{seconds:00.00}";
    }
}