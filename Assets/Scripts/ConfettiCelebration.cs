using UnityEngine;

/// <summary>
/// Spawns confetti particle bursts from ground level all around the pitch
/// when triggered. Place this on an empty GameObject in the scene.
///
/// In the Scene view, select this object to see yellow spheres previewing
/// where each launcher will spawn — updates live as you change the radii.
/// </summary>
public class ConfettiCelebration : MonoBehaviour
{
    public static ConfettiCelebration Instance { get; private set; }

    [Header("Confetti Setup")]
    [Tooltip("Confetti prefab to spawn (e.g. VRTemplateAssets/Prefabs/Blaster/Confetti). " +
             "Must have a ParticleSystem component.")]
    public GameObject confettiPrefab;

    [Tooltip("World-space centre of the pitch. The confetti ring spawns around this point.")]
    public Vector3 pitchCentre = Vector3.zero;

    [Tooltip("Y position of ground level where confetti shoots upward from.")]
    public float groundY = 0f;

    [Tooltip("Half-length of the pitch along the X axis (the wider or narrower side).")]
    public float radiusX = 40f;

    [Tooltip("Half-length of the pitch along the Z axis.")]
    public float radiusZ = 60f;

    [Tooltip("Number of confetti launchers evenly spaced around the oval.")]
    [Range(4, 32)]
    public int launcherCount = 12;

    [Tooltip("Whether the confetti particle effect should loop continuously.")]
    public bool loop = false;

    [Tooltip("Seconds before the spawned confetti objects are destroyed.")]
    public float lifetime = 6f;

    void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// Call this once to fire all confetti launchers around the pitch.
    /// </summary>
    public void Fire()
    {
        if (confettiPrefab == null)
        {
            Debug.LogWarning("[ConfettiCelebration] No confetti prefab assigned.");
            return;
        }

        float angleStep = 360f / launcherCount;

        for (int i = 0; i < launcherCount; i++)
        {
            Vector3 spawnPos = GetLauncherPosition(i, angleStep);

            // Each launcher faces straight up
            Quaternion rotation = Quaternion.LookRotation(Vector3.up);

            GameObject go = Instantiate(confettiPrefab, spawnPos, rotation);

            var allPS = go.GetComponentsInChildren<ParticleSystem>();
            foreach (var ps in allPS)
            {
                var main = ps.main;
                main.loop = loop;
                // The prefab's emission duration is only 1s — override it
                // so particles keep emitting for the full lifetime.
                main.duration = lifetime;

                if (!ps.isPlaying)
                    ps.Play();
            }

            Destroy(go, lifetime);
        }

        Debug.Log($"[ConfettiCelebration] Fired {launcherCount} confetti launchers.");
    }

    /// <summary>Returns the world position of launcher at the given index.</summary>
    private Vector3 GetLauncherPosition(int index, float angleStep)
    {
        float angleRad = index * angleStep * Mathf.Deg2Rad;

        return new Vector3(
            pitchCentre.x + Mathf.Cos(angleRad) * radiusX,
            groundY,
            pitchCentre.z + Mathf.Sin(angleRad) * radiusZ
        );
    }

    // ────────────────────────────────────────────────────────────────
    //  EDITOR PREVIEW — visible in the Scene view when this object
    //  is selected. Updates live as you drag the Inspector sliders.
    // ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        float angleStep = 360f / launcherCount;

        // Draw the oval outline
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f); // translucent yellow
        Vector3 prev = GetLauncherPosition(0, angleStep);
        int segments = 64;
        for (int i = 1; i <= segments; i++)
        {
            float angleRad = (i * 360f / segments) * Mathf.Deg2Rad;
            Vector3 next = new Vector3(
                pitchCentre.x + Mathf.Cos(angleRad) * radiusX,
                groundY,
                pitchCentre.z + Mathf.Sin(angleRad) * radiusZ
            );
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        // Draw each launcher position as a sphere + upward arrow
        Gizmos.color = Color.yellow;
        for (int i = 0; i < launcherCount; i++)
        {
            Vector3 pos = GetLauncherPosition(i, angleStep);
            Gizmos.DrawSphere(pos, 0.6f);
            Gizmos.DrawLine(pos, pos + Vector3.up * 3f); // little upward arrow
        }

        // Label at centre
        Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
        Gizmos.DrawWireCube(pitchCentre + Vector3.up * groundY, new Vector3(1f, 0.1f, 1f));
    }
#endif
}
