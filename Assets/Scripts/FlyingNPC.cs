using UnityEngine;

[RequireComponent(typeof(BoxCollider))] // Recommened for collision safety later
public class FlyingNPC : MonoBehaviour
{
    // Assigned by NPCManager
    private NPCManager manager;
    private Bounds allowedBounds;
    private float currentSpeed;

    // Internal Movement State
    private Vector3 targetPoint;
    private bool initialized = false;

    // Minimum distance to the target before picking a new one
    private const float arrivalThreshold = 2f;

    /// <summary>
    /// Receives constraints and positions itself randomly initially.
    /// </summary>
    public void SetupNPC(NPCManager managerRef, Bounds bounds)
    {
        manager = managerRef;
        allowedBounds = bounds;

        // Choose a random starting speed within the manager's constraints
        currentSpeed = Random.Range(manager.minSpeed, manager.maxSpeed);

        // Position the NPC at a random point initially inside the volume
        transform.position = GetRandomPointInBounds();

        // Pick the first flight target inside the volume
        PickNewRandomTarget();

        // Properly orient towards the first target immediately so we don't 'jerk'
        if (targetPoint != transform.position)
        {
            transform.rotation = Quaternion.LookRotation(targetPoint - transform.position);
        }

        initialized = true;
    }

    void Update()
    {
        if (!initialized) return;

        MoveNPC();
        CheckArrival();
    }

    void MoveNPC()
    {
        // 1. SMOOTH TURNING (Properly orient where heading)
        Vector3 directionToTarget = (targetPoint - transform.position).normalized;

        if (directionToTarget != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
            // Smoothly rotate from current rotation to target rotation
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, manager.turnSmoothing * Time.deltaTime);
        }

        // 2. FORWARD MOVEMENT (Translate forward relative to own rotation)
        transform.Translate(Vector3.forward * currentSpeed * Time.deltaTime);
    }

    void CheckArrival()
    {
        if (Vector3.Distance(transform.position, targetPoint) < arrivalThreshold)
        {
            // Dynamic speed variation per leg of flight
            currentSpeed = Random.Range(manager.minSpeed, manager.maxSpeed);
            PickNewRandomTarget();
        }
    }

    void PickNewRandomTarget()
    {
        targetPoint = GetRandomPointInBounds();
    }

    Vector3 GetRandomPointInBounds()
    {
        // Different elevations are handled naturally by picking random Y
        return new Vector3(
            Random.Range(allowedBounds.min.x, allowedBounds.max.x),
            Random.Range(allowedBounds.min.y, allowedBounds.max.y),
            Random.Range(allowedBounds.min.z, allowedBounds.max.z)
        );
    }
}