using UnityEngine;

public class RingCollectible : MonoBehaviour
{
    [Header("Ring Settings")]
    public int pointValue = 10;

    [Header("Effects (Optional)")]
    public AudioClip collectSound;
    public ParticleSystem collectSparks;

    private void OnTriggerEnter(Collider other)
    {
        // Check if the object flying through is the Player
        if (other.CompareTag("Player"))
        {
            // 1. Add Score 
            ScoreManager playerScore = other.GetComponent<ScoreManager>();
            if (playerScore != null)
            {
                playerScore.AddScore(pointValue);
            }

            // 2. Play visual/audio feedback
            if (collectSound != null)
            {
                AudioSource.PlayClipAtPoint(collectSound, transform.position);
            }

            if (collectSparks != null)
            {
                collectSparks.transform.parent = null;
                collectSparks.Play();
            }

            // 3. Tell the RingManager that this ring was passed
            if (RingManager.Instance != null)
            {
                RingManager.Instance.RingPassed();
            }

            // 4. Destroy the ring object
            Destroy(gameObject);
        }
    }
}