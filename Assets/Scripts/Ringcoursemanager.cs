using System.Collections.Generic;
using System.Net.NetworkInformation;
using UnityEngine;

/// <summary>
/// Drives the ring-flying minigame. Spawns rings one at a time along a
/// pre-placed path of waypoints, keeping exactly 2 rings active at once.
/// Starts the HUD timer on the first ring passed, stops it when the last
/// waypoint in the list has been passed.
/// </summary>
public class RingCourseManager : MonoBehaviour
{
    public static RingCourseManager Instance { get; private set; }

    [Header("Course Setup")]
    [Tooltip("Empty GameObjects marking where each ring should appear, in order. " +
             "The number of entries here is the total ring count for the course - " +
             "add/remove waypoints to change the course length.")]
    public List<Transform> ringWaypoints = new List<Transform>();

    [Tooltip("Ring prefab to spawn at each waypoint. Must have the Ring component and a trigger collider.")]
    public GameObject ringPrefab;

    [Header("HUD")]
    public RingTimerHUD timerHUD;

    private readonly List<Ring> activeRings = new List<Ring>();
    private int nextWaypointIndex = 0;
    private int ringsPassed = 0;
    private bool timerStarted = false;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (ringWaypoints.Count == 0)
        {
            Debug.LogError("[RingCourseManager] No waypoints assigned - populate the Ring Waypoints list in the Inspector.");
            return;
        }

        if (ringPrefab == null)
        {
            Debug.LogError("[RingCourseManager] No Ring Prefab assigned.");
            return;
        }

        if (timerHUD != null)
        {
            timerHUD.SetVisible(false);
        }

        // Seed the course with the first 2 rings so exactly 2 are visible from the start
        SpawnNextRing();
        SpawnNextRing();
    }

    void SpawnNextRing()
    {
        if (nextWaypointIndex >= ringWaypoints.Count) return;

        Transform point = ringWaypoints[nextWaypointIndex];
        if (point == null)
        {
            Debug.LogWarning($"[RingCourseManager] Waypoint at index {nextWaypointIndex} is empty, skipping.");
            nextWaypointIndex++;
            SpawnNextRing();
            return;
        }

        GameObject go = Instantiate(ringPrefab, point.position, point.rotation);
        Ring ring = go.GetComponent<Ring>();

        if (ring == null)
        {
            Debug.LogError("[RingCourseManager] Ring Prefab is missing the Ring component.");
            Destroy(go);
            return;
        }

        ring.Init(this, nextWaypointIndex);
        activeRings.Add(ring);
        nextWaypointIndex++;
    }

    /// <summary>Called by a Ring the instant the player passes through it.</summary>
    public void RingPassed(Ring ring)
    {
        activeRings.Remove(ring);
        ringsPassed++;

        if (!timerStarted)
        {
            timerStarted = true;
            if (timerHUD != null)
            {
                timerHUD.SetVisible(true);
                timerHUD.StartTimer();
            }
        }

        if (ringsPassed >= ringWaypoints.Count)
        {
            CompleteCourse();
        }
        else
        {
            // Keep exactly 2 rings visible by queuing the next one in the path
            SpawnNextRing();
        }
    }

    void CompleteCourse()
    {
        if (timerHUD != null)
        {
            timerHUD.StopTimer();
        }

        Debug.Log($"[RingCourseManager] Course complete! {ringsPassed} rings" +
                  (timerHUD != null ? $" in {timerHUD.ElapsedTime:F2}s" : ""));

        foreach (var r in activeRings)
        {
            if (r != null) Destroy(r.gameObject);
        }
        activeRings.Clear();
    }
}