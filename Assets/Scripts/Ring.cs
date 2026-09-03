using UnityEngine;

/// <summary>
/// Sits on the ring prefab. Detects the player passing through the ring's
/// trigger volume, reports the pass to RingCourseManager, then destroys itself.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Ring : MonoBehaviour
{
    [Tooltip("Tag used to detect the player. Must match the tag on your XR rig / CharacterController.")]
    public string playerTag = "Player";

    [Tooltip("Optional: particle or sound effect prefab to spawn when this ring is passed.")]
    public GameObject passEffectPrefab;

    private RingCourseManager courseManager;
    private int ringIndex;
    private bool passed = false;

    /// <summary>Called by RingCourseManager right after Instantiate.</summary>
    public void Init(RingCourseManager manager, int index)
    {
        courseManager = manager;
        ringIndex = index;
    }

    void OnTriggerEnter(Collider other)
    {
        if (passed) return;
        if (!other.CompareTag(playerTag)) return;

        passed = true;

        // Disable the collider immediately so a fast pass can't double-fire this
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        if (passEffectPrefab != null)
        {
            Instantiate(passEffectPrefab, transform.position, transform.rotation);
        }

        if (courseManager != null)
        {
            courseManager.RingPassed(this);
        }

        Destroy(gameObject);
    }
}