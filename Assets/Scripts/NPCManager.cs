using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(BoxCollider))]
public class NPCManager : MonoBehaviour
{
    [Header("NPC Model Pool")]
    [Tooltip("Drag your 2-4 variation NPC prefabs (from the Project folder) here.")]
    [SerializeField] private List<GameObject> npcPrefabPool;

    [Header("Spawning Settings")]
    [Tooltip("The total number of NPCs to maintain in the pitch volume.")]
    [Range(1, 100)] // Using [Range] ensures they don't spawn 10,000 and crash Unity
    public int totalNPCCount = 12;

    [Header("Flying Constraints")]
    [Tooltip("Global minimum flight speed for NPCs.")]
    public float minSpeed = 5f;
    [Tooltip("Global maximum flight speed for NPCs.")]
    public float maxSpeed = 15f;
    [Tooltip("How smoothly the NPC turns towards its target. Higher is faster.")]
    public float turnSmoothing = 3f;

    // Internal storage of the instantiated objects (optional, good for cleanup)
    private List<FlyingNPC> activeNPCInstances = new List<FlyingNPC>();

    // Bounds of the flight volume
    private Bounds flightBounds;

    void Start() // Start is better here than Awake to ensure prefabs load.
    {
        // 1. Define Bounds
        BoxCollider collider = GetComponent<BoxCollider>();
        collider.isTrigger = true;
        flightBounds = collider.bounds;

        // 2. Validate Prefab Pool
        if (npcPrefabPool == null || npcPrefabPool.Count == 0)
        {
            Debug.LogError("NPCManager: Please assign at least one NPC Prefab to the 'Npc Prefab Pool'!", this);
            return;
        }

        // 3. Spawn NPCs up to totalNPCCount
        SpawnNPCSystem();
    }

    void SpawnNPCSystem()
    {
        for (int i = 0; i < totalNPCCount; i++)
        {
            // Pick a random variation model from the 2-4 in the pool
            int randomPrefabIndex = Random.Range(0, npcPrefabPool.Count);
            GameObject chosenPrefab = npcPrefabPool[randomPrefabIndex];

            // Instantiate at the Manager's center temporarily (we position it next)
            GameObject spawnedGameObject = Instantiate(chosenPrefab, transform.position, Quaternion.identity);

            // Access the script
            FlyingNPC npcScript = spawnedGameObject.GetComponent<FlyingNPC>();

            if (npcScript != null)
            {
                // Pass constraints to the individual NPC and let it find its first point
                npcScript.SetupNPC(this, flightBounds);
                activeNPCInstances.Add(npcScript); // Store ref
            }
            else
            {
                Debug.LogWarning($"The prefab '{chosenPrefab.name}' is missing the 'FlyingNPC' script. It will spawn but not fly.");
            }
        }
    }

    // A helpful editor visualization of the flight volume
    void OnDrawGizmos()
    {
        BoxCollider collider = GetComponent<BoxCollider>();
        if (collider != null)
        {
            Gizmos.color = new Color(0, 1, 0, 0.2f); // Semi-transparent green
            Gizmos.DrawCube(collider.bounds.center, collider.bounds.size);
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
        }
    }
}