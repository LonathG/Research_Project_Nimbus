using UnityEngine;

public class RingTrigger : MonoBehaviour
{
    [SerializeField] private VRSerialController serialController;

    private bool hasTriggered = false;

    private void Awake()
    {
        if (serialController == null)
        {
            serialController = FindObjectOfType<VRSerialController>();
        }

        // Ensure trigger collider and kinematic Rigidbody
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            if (col is MeshCollider mc && !mc.convex)
            {
                mc.convex = true;
            }
            col.isTrigger = true;
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasTriggered)
            return;

        if (IsPlayer(other))
        {
            hasTriggered = true;

            Debug.Log($"[RingTrigger] RING PASSED triggered by '{other.name}'");

            if (serialController == null)
            {
                serialController = FindObjectOfType<VRSerialController>();
            }

            if (serialController != null)
            {
                serialController.TriggerRing();
            }
            else
            {
                Debug.LogWarning("[RingTrigger] VRSerialController reference is missing.");
            }
        }
    }

    private bool IsPlayer(Collider other)
    {
        if (other == null) return false;

        if (other.CompareTag("Player") || (other.transform.root != null && other.transform.root.CompareTag("Player")))
            return true;

        if (other.CompareTag("MainCamera"))
            return true;

        if (other.GetComponentInParent<BroomFlightController>() != null ||
            other.GetComponentInParent<CharacterController>() != null ||
            other.GetComponentInParent<Camera>() != null)
            return true;

        string colName = other.gameObject.name.ToLower();
        string rootName = other.transform.root.name.ToLower();

        return colName.Contains("xrorigin") || colName.Contains("xr origin") || colName.Contains("player") ||
               rootName.Contains("xrorigin") || rootName.Contains("xr origin") || rootName.Contains("player");
    }
}