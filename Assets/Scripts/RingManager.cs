using UnityEngine;
using System.Collections.Generic;
using TMPro; // 1. Add the TextMeshPro library

public class RingManager : MonoBehaviour
{
    public static RingManager Instance;

    [Header("Course Setup")]
    public List<GameObject> courseRings;
    private int nextRingIndex = 2;

    [Header("Timer Tracking")]
    private bool isTimerRunning = false;
    private float elapsedTime = 0f;
    private int ringsCollected = 0;

    [Header("UI Elements")]
    public TextMeshProUGUI timerText; // 2. Create a slot for your text UI

    void Awake()
    {
        if (Instance == null) { Instance = this; }
        else { Destroy(gameObject); }
    }

    void Start()
    {
        foreach (GameObject ring in courseRings)
        {
            if (ring != null) ring.SetActive(false);
        }

        if (courseRings.Count > 0) courseRings[0].SetActive(true);
        if (courseRings.Count > 1) courseRings[1].SetActive(true);

        // Ensure text is zeroed out at the start
        if (timerText != null) timerText.text = "Time: 0.00";
    }

    void Update()
    {
        if (isTimerRunning)
        {
            elapsedTime += Time.deltaTime;

            // 3. Update the text on screen every frame
            if (timerText != null)
            {
                timerText.text = "Time: " + elapsedTime.ToString("F2");
            }
        }
    }

    public void RingPassed()
    {
        ringsCollected++;

        if (ringsCollected == 1)
        {
            isTimerRunning = true;
            elapsedTime = 0f;
        }

        if (ringsCollected == courseRings.Count)
        {
            isTimerRunning = false;
            // 4. Show a final completion message
            if (timerText != null)
            {
                timerText.text = "FINAL TIME: " + elapsedTime.ToString("F2");
                timerText.color = Color.green; // Turn the text green when finished
            }
        }
        else if (nextRingIndex < courseRings.Count)
        {
            if (courseRings[nextRingIndex] != null) courseRings[nextRingIndex].SetActive(true);
            nextRingIndex++;
        }
    }
}