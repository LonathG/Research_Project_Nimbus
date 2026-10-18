using UnityEngine;

public class RingCollectible : MonoBehaviour
{
    [Header("Ring Settings")]
    public int pointValue = 10;
    public string playerTag = "Player";

    [Tooltip("If enabled, any entering collider triggers collection.")]
    public bool acceptAnyCollider = false;

    [Header("Proximity Fallback (If XROrigin has no Collider)")]
    [Tooltip("Allows collecting the ring when player/VR camera is within this distance, even if XROrigin lacks a collider component.")]
    public bool useProximityFallback = true;
    public float collectionRadius = 3.5f;

    [Header("Effects (Optional)")]
    public AudioClip collectSound;
    public ParticleSystem collectSparks;

    private bool isCollected = false;
    private Transform playerTarget;

    private void Awake()
    {
        // 1. Ensure this ring has an active trigger collider
        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            col = GetComponentInChildren<Collider>();
            if (col == null)
            {
                SphereCollider sphere = gameObject.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = collectionRadius;
                col = sphere;
            }
        }

        if (col != null)
        {
            // MeshCollider requires convex = true to be marked as a trigger in Unity
            if (col is MeshCollider meshCol && !meshCol.convex)
            {
                meshCol.convex = true;
            }
            col.isTrigger = true;
        }

        // 2. Ensure this ring has a kinematic Rigidbody
        // In Unity, if neither colliding object has a Rigidbody, OnTriggerEnter NEVER fires!
        // Adding a kinematic Rigidbody guarantees trigger events fire for all moving colliders.
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    private void Start()
    {
        FindPlayerTarget();
    }

    private void Update()
    {
        if (isCollected) return;

        // Proximity Fallback: in case XR Origin has no Collider component at all
        if (useProximityFallback)
        {
            if (playerTarget == null)
            {
                FindPlayerTarget();
            }

            if (playerTarget != null)
            {
                float dist = Vector3.Distance(transform.position, playerTarget.position);
                if (dist <= collectionRadius)
                {
                    CollectRing(playerTarget.gameObject, "Proximity Distance Check");
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isCollected) return;

        if (IsPlayerCollider(other))
        {
            CollectRing(other.gameObject, other.name);
        }
    }

    private bool IsPlayerCollider(Collider other)
    {
        if (acceptAnyCollider) return true;
        if (other == null) return false;

        // 1. Tag checks on collider or root
        if (!string.IsNullOrEmpty(playerTag))
        {
            if (other.CompareTag(playerTag)) return true;
            if (other.transform.root != null && other.transform.root.CompareTag(playerTag)) return true;
        }

        if (other.CompareTag("MainCamera")) return true;

        // 2. Component checks on collider or parents
        if (other.GetComponentInParent<BroomFlightController>() != null) return true;
        if (other.GetComponentInParent<CharacterController>() != null) return true;
        if (other.GetComponentInParent<Camera>() != null) return true;
        if (other.GetComponentInParent<ScoreManager>() != null) return true;

        // 3. Name heuristics for XR Origin / Player hierarchies
        string colName = other.gameObject.name.ToLower();
        string rootName = other.transform.root.name.ToLower();

        if (colName.Contains("xrorigin") || colName.Contains("xr origin") || colName.Contains("player") ||
            colName.Contains("camera") || colName.Contains("broom") || colName.Contains("rig"))
            return true;

        if (rootName.Contains("xrorigin") || rootName.Contains("xr origin") || rootName.Contains("player") ||
            rootName.Contains("broom") || rootName.Contains("rig"))
            return true;

        return false;
    }

    private void CollectRing(GameObject sourceObj, string sourceName)
    {
        if (isCollected) return;
        isCollected = true;

        Debug.Log($"[RingCollectible] Ring '{name}' collected by '{sourceName}'! Awarding {pointValue} pts.");

        // 1. Add Score (checks colliding object, parents, or scene)
        ScoreManager playerScore = null;
        if (sourceObj != null)
        {
            playerScore = sourceObj.GetComponentInParent<ScoreManager>();
        }
        if (playerScore == null)
        {
            playerScore = FindObjectOfType<ScoreManager>();
        }
        if (playerScore != null)
        {
            playerScore.AddScore(pointValue);
        }

        // 2. Play Audio Feedback
        if (collectSound != null)
        {
            AudioSource.PlayClipAtPoint(collectSound, transform.position);
        }

        // 3. Play Particle Sparks Feedback
        if (collectSparks != null)
        {
            collectSparks.transform.parent = null;
            collectSparks.Play();
            float duration = collectSparks.main.duration;
            Destroy(collectSparks.gameObject, duration > 0 ? duration + 0.5f : 2.0f);
        }

        // 4. Notify RingManager
        if (RingManager.Instance != null)
        {
            RingManager.Instance.RingPassed();
        }

        // 5. Notify VR Lighting Controller if present
        VRSerialController lightingController = FindObjectOfType<VRSerialController>();
        if (lightingController != null)
        {
            lightingController.TriggerRing();
        }

        // 6. Immediately hide visuals & disable colliders so collection feels instant
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            r.enabled = false;
        }
        foreach (Collider c in GetComponentsInChildren<Collider>())
        {
            c.enabled = false;
        }

        // 7. Destroy Ring
        Destroy(gameObject);
    }

    private void FindPlayerTarget()
    {
        // 1. Check BroomFlightController rigTarget or transform
        BroomFlightController broom = FindObjectOfType<BroomFlightController>();
        if (broom != null)
        {
            if (broom.rigTarget != null)
            {
                playerTarget = broom.rigTarget;
                return;
            }
            if (broom.vrCamera != null)
            {
                playerTarget = broom.vrCamera;
                return;
            }
            playerTarget = broom.transform;
            return;
        }

        // 2. Check Camera.main
        if (Camera.main != null)
        {
            playerTarget = Camera.main.transform;
            return;
        }

        // 3. Check GameObject tagged "Player"
        try
        {
            GameObject playerObj = GameObject.FindWithTag(playerTag);
            if (playerObj != null)
            {
                playerTarget = playerObj.transform;
                return;
            }
        }
        catch { }

        // 4. Heuristic name search
        GameObject xrObj = GameObject.Find("XR Origin (VR)") ?? GameObject.Find("XR Origin") ?? GameObject.Find("XROrigin");
        if (xrObj != null)
        {
            playerTarget = xrObj.transform;
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Draw detection sphere in Scene view for visual debugging
        Gizmos.color = new Color(0.2f, 1.0f, 0.4f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, collectionRadius);
    }
}