using UnityEngine;

public class RingTrigger : MonoBehaviour
{
    [SerializeField] private VRSerialController serialController;

    private bool hasTriggered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (hasTriggered)
            return;

        if (other.CompareTag("Player"))
        {
            hasTriggered = true;

            Debug.Log("[RingTrigger] RING PASSED");

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
}